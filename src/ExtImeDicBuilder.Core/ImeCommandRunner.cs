using System.Diagnostics;

namespace ExtImeDicBuilder.Core;

/// <summary>Result of running one external IME command-line step.</summary>
public sealed record ImeCommandResult(
    string CommandLine,
    bool Started,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool Success,
    string? FailureReason);

/// <summary>
/// Thin wrapper around <see cref="Process"/> for the three executables
/// involved in registering/unregistering a .dctx with Microsoft IME, per the
/// sequence confirmed in poc/README.md "実機検証手順" /
/// "ロールバック手順". This class never touches the registry directly —
/// every state change goes through imjpuexc.exe, which is the only
/// IME-sanctioned way to flip a dictionary's enabled bit.
///
/// This class performs real process execution; it is exercised manually
/// through the running application, not by automated tests (the assistant
/// implementing this is expressly forbidden from invoking these
/// executables).
/// </summary>
public sealed class ImeCommandRunner
{
    /// <summary>
    /// Launch imewdbld.exe with the given .dctx path and wait for it to
    /// exit. This is the step that shows the mandatory "Microsoft IME
    /// オープン拡張辞書の署名" approval dialog; the caller's UI must tell
    /// the user to approve it, since it cannot be automated.
    ///
    /// <see cref="ImeCommandResult.Success"/> here means only "the process
    /// ran to completion" — imewdbld.exe's exit code meaning has never been
    /// measured on real hardware (it is a GUI process showing a modal
    /// approval dialog, so a cancel could plausibly still exit 0). Whether
    /// the signature was actually approved and the file actually installed
    /// must be determined separately, by resolving and hash-checking the
    /// installed file (see <see cref="ImePaths.TryResolveInstalledDctxPath"/>).
    /// The exit code is still reported in <see cref="ImeCommandResult.ExitCode"/>
    /// for logging, but callers must not treat it as a success/failure signal.
    /// </summary>
    public async Task<ImeCommandResult> RunImewdbldAsync(string dctxPath, CancellationToken cancellationToken = default)
    {
        var exePath = ImePaths.GetImewdbldExePath();
        return await RunAsync(exePath, Quote(dctxPath), successPredicate: r => true, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// ADDSYSDICT: register the installed .dctx for all conversion modes
    /// (poc/README.md step 5). <paramref name="installedDctxPath"/> must be
    /// the *actual* on-disk path resolved via
    /// <see cref="ImePaths.TryResolveInstalledDctxPath"/> — never the
    /// original source path, and never a guessed/normalized casing of the
    /// GUID — because this string is written verbatim into the registry.
    /// </summary>
    public async Task<ImeCommandResult> RunAddSysDictAsync(string installedDctxPath, CancellationToken cancellationToken = default)
    {
        var exePath = ImePaths.GetImjpuexcExePath();
        var args = $"ADDSYSDICT {Quote(installedDctxPath)} 1";
        return await RunAsync(exePath, args, successPredicate: r => r.ExitCode == 0, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// CHECKSYSDICT: confirm the dictionary is enabled for all conversion
    /// modes. Per poc/README.md, the exit code is NOT a reliable success
    /// signal for this specific command; success must be judged by whether
    /// standard output contains "is used in all of conversion modes".
    /// </summary>
    public async Task<ImeCommandResult> RunCheckSysDictAsync(string installedDctxPath, CancellationToken cancellationToken = default)
    {
        var exePath = ImePaths.GetImjpuexcExePath();
        var args = $"CHECKSYSDICT {Quote(installedDctxPath)}";
        return await RunAsync(
                exePath,
                args,
                successPredicate: r => r.StdOut.Contains("is used in all of conversion modes", StringComparison.Ordinal),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>REMOVESYSDICT: unregister the dictionary (rollback step 1).</summary>
    public async Task<ImeCommandResult> RunRemoveSysDictAsync(string installedDctxPath, CancellationToken cancellationToken = default)
    {
        var exePath = ImePaths.GetImjpuexcExePath();
        var args = $"REMOVESYSDICT {Quote(installedDctxPath)}";
        return await RunAsync(exePath, args, successPredicate: r => r.ExitCode == 0, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string Quote(string path) => $"\"{path}\"";

    private async Task<ImeCommandResult> RunAsync(
        string exePath,
        string arguments,
        Func<(int ExitCode, string StdOut, string StdErr), bool> successPredicate,
        CancellationToken cancellationToken)
    {
        var commandLine = $"\"{exePath}\" {arguments}";

        if (!File.Exists(exePath))
        {
            return new ImeCommandResult(
                commandLine,
                Started: false,
                ExitCode: null,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                Success: false,
                FailureReason: $"実行ファイルが見つかりません: {exePath}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new ImeCommandResult(
                commandLine,
                Started: false,
                ExitCode: null,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                Success: false,
                FailureReason: $"起動に失敗しました: {ex.Message}");
        }

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdOut = await stdOutTask.ConfigureAwait(false);
        var stdErr = await stdErrTask.ConfigureAwait(false);

        var success = successPredicate((process.ExitCode, stdOut, stdErr));

        return new ImeCommandResult(
            commandLine,
            Started: true,
            ExitCode: process.ExitCode,
            StandardOutput: stdOut,
            StandardError: stdErr,
            Success: success,
            FailureReason: success ? null : $"終了コード={process.ExitCode}");
    }
}
