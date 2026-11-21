using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Input;
using ExtImeDicBuilder.Core;
using Microsoft.Win32;

namespace ExtImeDicBuilder;

/// <summary>
/// Application state and commands for MainWindow. Deliberately not a
/// "framework" ViewModel: plain INotifyPropertyChanged + ObservableCollection,
/// no DI container, no MVVM toolkit, per this project's design constraints.
/// All non-UI logic (validation, DCTX generation, IME process orchestration)
/// lives in ExtImeDicBuilder.Core; this class only wires it to the view.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly EntryRepository _repository;
    private readonly ImeCommandRunner _imeRunner = new();
    private EntryStore _store;

    private string _readingInput = string.Empty;
    private string _outputInput = string.Empty;
    private string _searchText = string.Empty;
    private string? _editingId;
    private EntryListItem? _selectedEntry;
    private string _statusMessage = "読みと変換結果を入力して「登録」を押してください。";
    private bool _isBusy;

    public MainViewModel()
        : this(new EntryRepository())
    {
    }

    public MainViewModel(EntryRepository repository)
    {
        _repository = repository;
        _store = _repository.Load();

        Entries = new ObservableCollection<EntryListItem>(_store.Entries.Select(e => new EntryListItem(e)));
        LogLines = new ObservableCollection<string>();

        RegisterOrUpdateCommand = new RelayCommand(RegisterOrUpdate, () => !IsBusy);
        ClearFormCommand = new RelayCommand(ClearForm, () => !IsBusy);
        DeleteCommand = new RelayCommand(DeleteSelected, () => !IsBusy && SelectedEntry is not null);
        UpdateImeDictionaryCommand = new AsyncRelayCommand(UpdateImeDictionaryAsync, () => !IsBusy);
        UnregisterImeDictionaryCommand = new AsyncRelayCommand(UnregisterImeDictionaryAsync, () => !IsBusy);
        SaveDctxAsCommand = new RelayCommand(SaveDctxAs, () => !IsBusy);
    }

    public ObservableCollection<EntryListItem> Entries { get; }

    public ObservableCollection<string> LogLines { get; }

    public string ReadingInput
    {
        get => _readingInput;
        set => SetField(ref _readingInput, value);
    }

    public string OutputInput
    {
        get => _outputInput;
        set => SetField(ref _outputInput, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
            {
                RefreshEntries();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string RegisterButtonLabel => _editingId is null ? "登録" : "更新";

    public EntryListItem? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetField(ref _selectedEntry, value))
            {
                LoadIntoForm(value);
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public ICommand RegisterOrUpdateCommand { get; }

    public ICommand ClearFormCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand UpdateImeDictionaryCommand { get; }

    public ICommand UnregisterImeDictionaryCommand { get; }

    public ICommand SaveDctxAsCommand { get; }

    private void LoadIntoForm(EntryListItem? item)
    {
        if (item is null)
        {
            return;
        }

        _editingId = item.Id;
        ReadingInput = item.Reading;
        OutputInput = item.Record.Output;
        OnPropertyChanged(nameof(RegisterButtonLabel));
    }

    private void ClearForm()
    {
        _editingId = null;
        ReadingInput = string.Empty;
        OutputInput = string.Empty;
        SelectedEntry = null;
        OnPropertyChanged(nameof(RegisterButtonLabel));
        StatusMessage = "読みと変換結果を入力して「登録」を押してください。";
    }

    private void RegisterOrUpdate()
    {
        var reading = ReadingInput;
        var output = EntryValidator.NormalizeToCrlf(OutputInput);

        var basic = EntryValidator.Validate(reading, output);
        var duplicate = EntryValidator.ValidateAgainstExisting(reading, output, _store.Entries, _editingId);

        var errors = basic.Errors.Concat(duplicate.Errors).ToList();
        var warnings = basic.Warnings.Concat(duplicate.Warnings).ToList();

        if (errors.Count > 0)
        {
            MessageBox.Show(
                string.Join("\n", errors),
                "登録できません",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var now = DateTimeOffset.Now;

        if (_editingId is { } id)
        {
            var record = _store.Entries.First(e => e.Id == id);
            record.Reading = reading;
            record.Output = output;
            record.UpdatedAt = now;
            AppendLog($"更新しました: 「{reading}」");
        }
        else
        {
            var record = new DictionaryEntryRecord
            {
                Reading = reading,
                Output = output,
                CreatedAt = now,
                UpdatedAt = now,
            };
            _store.Entries.Add(record);
            AppendLog($"登録しました: 「{reading}」");
        }

        _repository.Save(_store);
        RefreshEntries();
        ClearForm();

        if (warnings.Count > 0)
        {
            MessageBox.Show(string.Join("\n", warnings), "確認", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        StatusMessage = "登録内容を保存しました。反映するには「IME辞書を更新」を押してください。";
    }

    private void DeleteSelected()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        var reading = SelectedEntry.Reading;
        var result = MessageBox.Show(
            $"「{reading}」を削除します。よろしいですか？",
            "削除の確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _store.Entries.RemoveAll(e => e.Id == SelectedEntry.Id);
        _repository.Save(_store);
        AppendLog($"削除しました: 「{reading}」");
        ClearForm();
        RefreshEntries();
        StatusMessage = "削除しました。反映するには「IME辞書を更新」を押してください。";
    }

    private void RefreshEntries()
    {
        Entries.Clear();
        var query = _store.Entries.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(e =>
                e.Reading.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                e.Output.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase));
        }

        foreach (var item in query.OrderByDescending(e => e.UpdatedAt).Select(e => new EntryListItem(e)))
        {
            Entries.Add(item);
        }
    }

    private void AppendLog(string message) =>
        LogLines.Add($"[{DateTimeOffset.Now:HH:mm:ss}] {message}");

    private void SaveDctxAs()
    {
        if (_store.Entries.Count == 0)
        {
            MessageBox.Show("登録済みの項目がありません。", "保存できません", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "IME拡張辞書 (*.dctx)|*.dctx|すべてのファイル (*.*)|*.*",
            FileName = _store.DictionaryGuid.Trim('{', '}') + ".dctx",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var data = DctxGenerator.Generate(_store);
            File.WriteAllBytes(dialog.FileName, data);
            AppendLog($"DCTX を保存しました: {dialog.FileName}");
            StatusMessage = "DCTX ファイルを保存しました。";
        }
        catch (Exception ex)
        {
            AppendLog($"DCTX の保存に失敗しました: {ex.Message}");
            MessageBox.Show($"保存に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// "IME辞書を更新" — regenerate the .dctx from the current master data
    /// (with <see cref="EntryStore.DictionaryVersion"/> incremented) and
    /// walk it through the full registration sequence confirmed in
    /// poc/README.md: imewdbld.exe (user must approve the signature dialog)
    /// → resolve the real installed file and confirm by SHA-256 that it is
    /// this run's output (imewdbld's exit code is not a reliable success
    /// signal — see <see cref="ImeCommandRunner.RunImewdbldAsync"/>) →
    /// ADDSYSDICT (enable for all conversion modes; imewdbld alone leaves it
    /// disabled) → CHECKSYSDICT (judged by stdout text, not exit code).
    /// </summary>
    private async Task UpdateImeDictionaryAsync()
    {
        if (_store.Entries.Count == 0)
        {
            MessageBox.Show("登録済みの項目がありません。先に定型文を登録してください。", "更新できません", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var proceed = MessageBox.Show(
            "IME辞書を更新します。この後 Windows の確認ダイアログ" +
            "「Microsoft IME オープン拡張辞書の署名」が表示されますので、必ず承認してください" +
            "（この承認は自動化できません）。続行しますか？",
            "IME辞書の更新",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);

        if (proceed != MessageBoxResult.OK)
        {
            return;
        }

        IsBusy = true;
        try
        {
            // 1. Bump DictionaryVersion (safety measure; see the doc comment
            //    on EntryStore.DictionaryVersion) and generate the .dctx into
            //    this app's own data folder.
            _store.DictionaryVersion++;

            var appDataDir = Path.GetDirectoryName(_repository.FilePath)!;
            var generatedDir = Path.Combine(appDataDir, "generated");
            Directory.CreateDirectory(generatedDir);
            var sourceDctxPath = Path.Combine(generatedDir, _store.DictionaryGuid.Trim('{', '}') + ".dctx");

            byte[] data;
            try
            {
                data = DctxGenerator.Generate(_store);
            }
            catch (Exception ex)
            {
                AppendLog($"DCTX の生成に失敗しました: {ex.Message}");
                MessageBox.Show($"DCTX の生成に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            File.WriteAllBytes(sourceDctxPath, data);
            _repository.Save(_store); // persist the incremented DictionaryVersion regardless of what happens below
            AppendLog($"DCTX を生成しました (DictionaryVersion={_store.DictionaryVersion}): {sourceDctxPath}");

            // 2-3. imewdbld.exe — shows the mandatory approval dialog. Its
            // exit code is logged only; it is not treated as success/failure
            // (see the doc comment on RunImewdbldAsync).
            AppendLog("Microsoft IME オープン拡張辞書の署名ダイアログが表示されます。承認してください…");
            var imewdbldResult = await _imeRunner.RunImewdbldAsync(sourceDctxPath).ConfigureAwait(true);
            AppendLog($"実行: {imewdbldResult.CommandLine} (終了コード={imewdbldResult.ExitCode?.ToString() ?? "なし"}、成否の判定には使用しません)");
            if (!imewdbldResult.Started)
            {
                AppendLog($"imewdbld.exe を起動できませんでした: {imewdbldResult.FailureReason}");
                MessageBox.Show(
                    $"imewdbld.exe を起動できませんでした。\n{imewdbldResult.FailureReason}",
                    "登録失敗",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            // 4. Resolve the *actual* installed file (real on-disk casing —
            //    never a guessed one) and confirm by SHA-256 that it matches
            //    what this run just generated. This is the fact-based
            //    success signal for the imewdbld step (see ImePaths.TryResolveInstalledDctxPath).
            if (!ImePaths.TryResolveInstalledDctxPath(_store.DictionaryGuid, out var installedPath) || installedPath is null)
            {
                AppendLog("辞書がインストールされていません。署名ダイアログで承認されなかった可能性があります。");
                MessageBox.Show(
                    "辞書がインストールされていません。署名ダイアログをキャンセルした可能性があります。",
                    "登録されていません",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            AppendLog($"インストール済みファイルを確認しました: {installedPath}");

            var installedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installedPath)));
            var generatedHash = Convert.ToHexString(SHA256.HashData(data));
            if (installedHash == generatedHash)
            {
                AppendLog("インストール済みファイルは今回生成した内容と SHA-256 が一致しました。今回の更新が反映されています。");
            }
            else
            {
                AppendLog(
                    "インストール済みファイルが今回生成した内容と SHA-256 が一致しません。" +
                    "古い辞書が残っている可能性があります。署名ダイアログの承認内容を確認してください。" +
                    $"\n  installed={installedHash}\n  generated={generatedHash}");
            }

            // 5. ADDSYSDICT — enable for all conversion modes, against the resolved real path.
            var addResult = await _imeRunner.RunAddSysDictAsync(installedPath).ConfigureAwait(true);
            AppendLog($"実行: {addResult.CommandLine}");
            AppendLog(addResult.Success
                ? "ADDSYSDICT が完了しました（全変換モードで有効化）。"
                : $"ADDSYSDICT に失敗しました: {addResult.FailureReason}\n{addResult.StandardError}");

            // 6. CHECKSYSDICT — judged by stdout text, not exit code.
            var checkResult = await _imeRunner.RunCheckSysDictAsync(installedPath).ConfigureAwait(true);
            AppendLog($"実行: {checkResult.CommandLine}");
            AppendLog($"CHECKSYSDICT 出力: {checkResult.StandardOutput.Trim()}");

            if (checkResult.Success)
            {
                AppendLog("辞書は全変換モードで有効になっています。");
                StatusMessage = "IME辞書を更新しました。IMEのオフ→オン、または対象アプリの再起動後に利用できます。";
                MessageBox.Show(
                    "IME辞書の更新が完了しました。\n反映するには IME をオフ→オンにするか、対象アプリを再起動してください。",
                    "完了",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                StatusMessage = "IME辞書の更新中に問題が発生しました。ログを確認してください。";
                MessageBox.Show(
                    "辞書の有効化状態を確認できませんでした。ログを確認してください。",
                    "確認が必要です",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// "IME辞書の登録を解除" — rollback per poc/README.md
    /// "ロールバック手順": REMOVESYSDICT then delete the installed .dctx and
    /// its auto-generated .dctr runtime index file(s). Uses the same
    /// real-file resolution as the update flow, never a guessed path.
    /// </summary>
    private async Task UnregisterImeDictionaryAsync()
    {
        var confirm = MessageBox.Show(
            "IME辞書の登録を解除します。インストール済みの辞書ファイルも削除されます。よろしいですか？",
            "登録解除の確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (!ImePaths.TryResolveInstalledDctxPath(_store.DictionaryGuid, out var installedPath) || installedPath is null)
            {
                AppendLog("インストール済みの辞書が見つかりませんでした。既に解除されている可能性があります。");
                MessageBox.Show("インストール済みの辞書が見つかりませんでした。既に解除されている可能性があります。", "対象なし", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var removeResult = await _imeRunner.RunRemoveSysDictAsync(installedPath).ConfigureAwait(true);
            AppendLog($"実行: {removeResult.CommandLine}");
            AppendLog(removeResult.Success
                ? "REMOVESYSDICT が完了しました。"
                : $"REMOVESYSDICT に失敗しました: {removeResult.FailureReason}\n{removeResult.StandardError}");

            try
            {
                if (File.Exists(installedPath))
                {
                    File.Delete(installedPath);
                    AppendLog($"削除しました: {installedPath}");
                }

                foreach (var dctrPath in ImePaths.ResolveInstalledDctrPaths(_store.DictionaryGuid))
                {
                    File.Delete(dctrPath);
                    AppendLog($"削除しました: {dctrPath}");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"インストール済みファイルの削除に失敗しました: {ex.Message}");
            }

            StatusMessage = "IME辞書の登録を解除しました。";
            MessageBox.Show("IME辞書の登録を解除しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
