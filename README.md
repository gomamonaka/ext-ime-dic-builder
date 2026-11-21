# ExtIMEDicBuilder

Windows 11 の Microsoft IME 向けに、複数行の定型文を「変換候補」として登録できる
WPF アプリです。通常の IME ユーザー辞書では複数行テキストを変換結果として
持てないため、Microsoft IME の Open Extended Dictionary（`.dctx`）機構を使い、
複数行の定型文を IME 拡張辞書として生成・登録します。

このアプリは `poc/` で実機検証済みの DCTX 生成ロジック・登録手順をそのまま
GUI 化したものです。DCTX の仕様や改行コードの扱いの技術的な背景は
`poc/README.md` を参照してください。

## 構成

```
src\ExtImeDicBuilder.Core\        DCTX生成 + JSON正本管理 + バリデーション + IME連携コマンド (net8.0)
src\ExtImeDicBuilder\             WPF アプリ本体 (net8.0-windows)
tests\ExtImeDicBuilder.Core.Tests\ Core のユニットテスト (xUnit, net8.0)
poc\                               実機検証済みの PoC（変更禁止・移植元）
tools\make_icon.py                app.ico を標準ライブラリのみで生成するスクリプト
ExtImeDicBuilder.sln
```

## ビルド方法

.NET 8 SDK が必要です（動作確認は `dotnet 10.0.303` に同梱の `net8.0` ターゲット
で実施）。

```powershell
dotnet build ExtImeDicBuilder.sln -c Release
```

### テスト

```powershell
dotnet test ExtImeDicBuilder.sln -c Release
```

`ExtImeDicBuilder.Core.Tests` には、`poc/generate_poc.py` と同じ固定 GUID・
メタ情報・エントリ内容から DCTX を再構築し、`poc/out/*.dctx` の実ファイルと
バイト単位で一致することを検証するテスト（`PocByteExactTests`）が含まれます。
これが「移植が正しい」ことの決定論的な根拠です。

### フォーマット

```powershell
dotnet format ExtImeDicBuilder.sln --verify-no-changes
```

## 配布ビルド

配布先に .NET ランタイムのインストールを要求しない self-contained・単一 exe
としてビルドできます。

```powershell
dotnet publish src\ExtImeDicBuilder -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none `
  -o dist\win-x64
```

成功すると `dist\win-x64\ExtImeDicBuilder.exe` **1 ファイルのみ**が生成されます
（実測サイズ: 約 71,892,950 バイト ≒ 68.6 MiB。.NET ランタイム一式を
自己完結で同梱しているため単体でこのサイズになります）。

配布は `dist\win-x64\ExtImeDicBuilder.exe` を配布先の任意のフォルダーに
コピーするだけで完了します。**.NET SDK / ランタイムのインストールは不要**です
（self-contained のため）。

`PublishTrimmed`（トリミング）は使用していません。WPF はトリミングに対応して
おらず、有効にすると実行時に例外で落ちます。

### アイコン

`src\ExtImeDicBuilder\app.ico` は `tools\make_icon.py`（Python 3 標準ライブラリ
のみ、外部パッケージ不要）で生成した ICO ファイルです。PNG 圧縮を使わず、
非圧縮 32bit BGRA の DIB 形式で 16 / 32 / 48 / 256 の 4 サイズを 1 ファイルに
格納しています。デザインは「複数行のテキスト行」を表す抽象的な横棒のみで、
実在の企業・製品のロゴを模したものではありません。再生成する場合:

```powershell
python tools\make_icon.py
```

生成スクリプト自身がバイト列を書き出した直後に ICONDIR / ICONDIRENTRY /
BITMAPINFOHEADER の整合性を読み戻して検証するため、壊れた ICO が
`app.ico` として残ることはありません。

## 使い方

1. アプリを起動する（`dotnet run --project src\ExtImeDicBuilder`、または
   `dotnet build` 後の `ExtImeDicBuilder.exe` を実行）。
2. 左側フォームで「読み」（例: `ぎじろく`）を入力する。
3. 「変換結果」欄に複数行のテキストを貼り付ける（複数行入力・改行そのまま可）。
4. 「登録」を押す。右側の一覧に追加され、内部データ
   （`%APPDATA%\ExtImeDicBuilder\entries.json`）に保存される。
5. 必要な項目をすべて登録し終えたら「IME辞書を更新」を押す。
6. Windows の確認ダイアログ「Microsoft IME オープン拡張辞書の署名」が
   表示されるので、内容を確認して**手動で承認する**（自動化不可）。
7. 承認後、アプリが自動的に辞書を全変換モードで有効化し、結果をログに表示する。
8. IME をオフ→オンにする（またはメモ帳などの対象アプリを再起動する）と、
   登録した読みで変換候補に複数行の定型文が表示されるようになる。

一覧の行をクリックするとフォームに読み込まれ、編集・削除ができます。
検索ボックスは読み・変換結果の両方を部分一致で検索します。
「DCTX を名前を付けて保存...」で、IME に登録せず `.dctx` ファイルのみを
任意の場所に出力することもできます。

## IME 反映の仕組み

`poc/README.md` の実機検証で確定した手順をそのまま自動化しています
（レジストリの直接編集は一切行いません。すべて `imjpuexc.exe` 経由です）。

1. 正本データ（entries.json）から DCTX を生成し、
   `%APPDATA%\ExtImeDicBuilder\generated\{DictionaryGUID}.dctx` に保存する。
2. `%SystemRoot%\System32\IME\SHARED\imewdbld.exe` にその DCTX を渡して起動する。
   ここで Windows 標準の署名確認ダイアログが表示される（承認はユーザー操作が必須）。
3. 承認されると IME が DCTX を
   `%LOCALAPPDATA%\Microsoft\IME\15.0\IMEJP\Dicts\{DictionaryGUID}.dctx`
   にコピーする。この時点では辞書はまだ無効（登録直後は全変換モードで無効）。
   **実機の実測では、DCTX 内の `DictionaryGUID`（entries.json も同じく小文字）
   に対し、コピー先の実際のファイル名は大文字になる**（例:
   `{00d41162-...}` → `{00D41162-...}.dctx`）。このアプリはこの綴りを
   推測せず、インストール先ディレクトリを実際に列挙して一致するファイルを
   探す（`ImePaths.TryResolveInstalledDctxPath`）。以降の手順はすべて、
   この方法で見つけた**実ファイルの綴りそのまま**のパスを使う
   （`imjpuexc.exe` に渡した文字列はレジストリの `DICnn` 値へそのまま
   書き込まれるため、綴りのずれは別エントリとしての重複登録につながる）。
4. imewdbld.exe の終了コードの意味は実測されていない（署名ダイアログを
   キャンセルしても 0 を返す可能性がある）ため、**終了コードでは成否を
   判定しない**。代わりに、上記の方法で実際にインストールされたファイルを
   確認し、その内容の SHA-256 が今回生成した DCTX と一致するかで
   「今回の更新が確かに反映されたか」を判定する。
5. `imjpuexc.exe ADDSYSDICT "<実際のインストール先パス>" 1` を実行し、
   全変換モードで有効化する。
6. `imjpuexc.exe CHECKSYSDICT "<同じパス>"` で状態を確認する。
   `CHECKSYSDICT` は終了コードでは成否を判定できない仕様のため、
   標準出力に `is used in all of conversion modes` が含まれるかで判定している。
7. 各コマンドの実行内容と結果は画面下部のログに表示される。

`DictionaryGuid` は初回保存時に一度だけ生成され、以後変更されません。
インストール先ファイル名はこの GUID に由来するため、同じ GUID で
再生成・再登録すれば既存の辞書が上書き更新され、別 GUID で登録することは
ありません。また「IME辞書を更新」を実行するたびに `DictionaryVersion`
を 1 ずつ増やして DCTX を再生成する（entries.json に永続化）。
IME が更新検出に `DictionaryVersion` を実際に使うかどうかは実測されて
いないが、安全側に倒す措置として毎回インクリメントしている。

## 実機検証結果（アプリ経由）

アプリ本体を使った登録・更新のフローは実機で検証済みです。

- アプリから読み `ぎじろく`（変換結果は「発言者：」「結論：」「補足：」
  「対応：」の4行）を登録し、「IME辞書を更新」を実行した。署名ダイアログを
  承認すると、`%LOCALAPPDATA%\Microsoft\IME\15.0\IMEJP\Dicts\{GUID}.dctx`
  に配置され、レジストリに `DICnn = <path>,1,7`（全変換モードで有効）として
  登録されることを確認した。
- **「IME辞書を更新」を計4回実行し（entries.json の `dictionaryVersion` が
  4 に到達）、その状態でメモ帳で `ぎじろく` を変換したところ、正しく4行と
  して確定された。** 同一 GUID による上書き更新を繰り返しても辞書が壊れず、
  複数行変換が維持されることを実機で確認した。
- 一方、「エントリを編集した内容が、編集後の変換結果に正しく反映されるか」
  という一点は、厳密には切り分けて確認できていない（**未確認**）。
- なお、PoC 段階で登録した検証用辞書（`poc/generate_poc.py` が生成した
  もの）は削除済みであり、現在端末に登録されているのはアプリが生成した
  辞書のみである。

## ロールバック手順

「IME辞書の登録を解除」ボタンで以下を実行します（確認ダイアログあり）。

1. `imjpuexc.exe REMOVESYSDICT "<インストール済みパス>"` を実行する。
2. インストール済みの `{GUID}.dctx` と、IME が自動生成した
   `{GUID}_*.dctr`（ランタイム索引ファイル）を削除する。

手動で行う場合は `poc\README.md` の「ロールバック手順」を参照してください。
既存のユーザー辞書・システム辞書には一切影響しません。

アプリ自体をアンインストールする場合は、上記の登録解除を先に行ってから
`%APPDATA%\ExtImeDicBuilder` フォルダーを削除してください。

## 既知の制約

- **受け側アプリによっては、複数行の変換結果が正しく受け取れないことがある。**
  これは DCTX・IME 側の問題ではなく、受け側アプリの入力欄の実装に起因する。
  実機で確認済みの例は以下のとおり。
  - **Teams の入力欄**: 確定時に改行が失われ、1行に連結される。CR / LF /
    CRLF のどの改行方式を DCTX に格納しても結果は同じ。
  - **Claude Code の VS Code 拡張のチャット入力欄**: 複数行の確定文字列が
    正しく描画・送信されない。入力欄自体には複数行が入っているが、1行目に
    連結版がゴースト表示として重なって見え、送信時には連結された状態で
    送られる。
  - なお **VS Code のエディタ本体（通常のファイル編集画面）は未検証**で
    ある。チャット入力欄で確認しただけであり、VS Code 全般が駄目という
    ことではない。
  - 対照的に、**メモ帳・ブラウザの textarea・Excel では CR / LF / CRLF の
    いずれでも正しく複数行として確定されることが実測済み**である（詳細は
    `poc/README.md` の検証結果表を参照）。
  - 一般論として、Enter キーが送信操作に割り当てられているチャット系の
    入力欄では複数行のテキストを正しく扱えないことがある。DCTX 側・IME
    側では回避できない制約のため、利用するアプリの入力欄の挙動を事前に
    確認することを推奨する。
- 実機検証の結果、メモ帳・ブラウザ textarea・Excel では CR / LF / CRLF の
  いずれでも複数行として正しく確定され、**3方式のあいだに挙動差は
  観測されなかった**（詳細は `poc/README.md` の検証結果表を参照）。
  差が無いため、Windows ネイティブの改行であり正本データ（entries.json）
  にもそのまま保持できる CRLF を採用している。
  Outlook は未検証のまま。
- 署名ダイアログ（「Microsoft IME オープン拡張辞書の署名」）の承認は
  Windows の仕様上自動化できない。毎回手動承認が必要。
- 変換結果（Output）の文字数上限は実機で計測されていない。アプリ側では
  ソフト上限（警告のみ、登録はブロックしない）を設けているが、この値は
  暫定値であり実測値ではない（`ExtImeDicBuilder.Core.EntryValidator.SoftOutputLengthWarningThreshold`）。
- 同一読みの重複登録はエラーにしていない（警告のみ）。Microsoft IME が
  複数候補として扱える可能性があるため。読みと変換結果の両方が完全一致する
  場合のみエラーとする。
- インストール先の実ファイル名が大文字になることは実測で確認済みだが、常に
  そうなるという保証は無いため、綴りを推測せず実際にディレクトリを列挙して
  解決している。一方、`DictionaryVersion` を IME が更新検出にどう使うかは
  実測できていないため、毎回インクリメントするのは安全側に倒した対処である
  （詳細は上記「IME 反映の仕組み」参照）。
- **IME 辞書フォルダに、IME 側の作業ファイルが常駐プロセスに掴まれたまま
  残ることがある。** 辞書を更新すると
  `%LOCALAPPDATA%\Microsoft\IME\15.0\IMEJP\Dicts\` に `REN????.tmp`
  （131,072 バイト、`Hidden` 属性）というファイルが残ることがある。実機では
  3 個・計 384KB の残存を確認しており、更新のたびに約 128KB ずつ増える
  （機能上の不具合ではなく、ディスク使用量への影響も 1 回の更新あたり
  約 128KB にとどまる）。サイズと作成時刻から、IME がランタイム索引
  （`.dctr`）を作り直す際の作業ファイルと見られる。
  - **このファイルはアプリからもユーザーからも削除できないことを実機で
    確認済みである。** 読み取り目的のオープン・排他オープン・ACL の読み取り
    （`Get-Acl`）のいずれを試みても `UnauthorizedAccessException`
    （アクセス拒否）となる。本アプリが自動削除を実装していないのは
    「安全でないと判断した」からではなく、**そもそも削除できず実装しても
    機能しないため**である。
  - **これは他プロセスによるファイルロックではないと見られる。** ロックで
    あれば Windows は「別のプロセスが使用中」（`IOException`）を返すはずだが、
    実際に一貫して返るのは「アクセス拒否」であり、これは削除保留
    （delete-pending）状態のファイルへのオープンが `STATUS_DELETE_PENDING`
    → `ERROR_ACCESS_DENIED` に変換される挙動と一致する。つまり IME は
    既に削除を要求済みで、最後のハンドルが閉じられるまで残っている
    可能性が高い（推測）。
  - **「IME だけを再起動して解放させる」ことは事実上できない。**
    `TextInputHost.exe` を終了させても（Windows が自動的に再起動する）
    ファイルは 3 個とも残ったままだった。日本語 IME の本体 DLL
    （`IMJPTIP` / `IMJPDAPI`）はテキスト入力を受け付けるほぼ全てのプロセスに
    読み込まれており、実機では explorer / chrome / msedge / Excel /
    メモ帳 / VS Code など 24 プロセスが該当した。したがって「IME の
    再起動」は実質「起動中のほぼ全アプリの再起動」を意味する。
  - サインアウトまたは PC の再起動で解消するのではないかと推測されるが、
    **これは未検証であり、「再起動すれば消える」と断定はできない。**
