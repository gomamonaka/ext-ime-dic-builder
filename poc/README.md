# IME 拡張辞書 (DCTX) 複数行 PoC

Windows 11 Microsoft IME の「オープン拡張辞書 (.dctx)」で、変換候補
(`OutputString`) に複数行のテキスト（改行を含む文字列）を持たせられるかを
検証するための PoC。GUI はなく、CLI で `.dctx` ファイルを生成するだけ。

標準ライブラリのみで実装（外部ライブラリ・pip install・ネットワークアクセス
なし）。レジストリ書き込みや IME 設定変更、既存ユーザー辞書へのアクセスは
一切行わない（読み取りも行わない）。

## 構成

```
poc/
  dctx.py           コア（副作用なしの純粋関数。GUIからの再利用を想定）
  generate_poc.py   6ファイルを poc/out/ に生成するCLI + 自己検証
  test_dctx.py      unittest
  out/              生成物の出力先（generate_poc.py 実行で作成される）
  README.md         このファイル
```

## 技術的な核心: 改行コードの格納方法

XML 1.0 の仕様上、パーサは行末を正規化する（生の CR や CRLF を LF に
潰す）ため、`OutputString` 要素のテキストに生の `\r` や `\r\n` を書いても
IME 側でパースされた時点で失われる可能性がある。これを避けるため、この
PoC では改行を数値文字参照でエンコードする:

- CR: `&#13;`
- LF: `&#10;`（生の LF と等価だが、意図を明示するため数値参照で統一）
- CRLF: `&#13;&#10;`

エスケープは `xml.sax.saxutils.escape` に集約し（`&` `<` `>` `"` `'` の5文字）、
その後に制御文字 (`\r` `\n` `\t`) を数値文字参照へ変換する。この処理は
`poc/dctx.py` の `escape_text()` 一箇所に集約されており、生成コードの他の
場所では文字列連結によるエスケープは行っていない。

`generate_poc.py` は生成した各ファイルを `xml.etree.ElementTree` で
再パースし、`OutputString` のテキストが生成時に意図した Python 文字列と
1バイトも違わず一致することを assert する（自己検証）。

## 生成手順

```powershell
python poc\generate_poc.py
```

成功すると `poc\out\` に以下の 6 ファイルが生成され、標準出力に各ファイルの
サイズと自己検証結果（OK）が表示される。

| ファイル | 目的 |
|---|---|
| `ime_multiline_poc.dctx` | 本命。複数行候補・CR/LF/CRLF・特殊文字・タブ・空行・末尾改行などを網羅した8エントリ |
| `ime_multiline_poc_cr.dctx` | 切り分け用。CR区切りの1エントリのみ |
| `ime_multiline_poc_lf.dctx` | 切り分け用。LF区切りの1エントリのみ |
| `ime_multiline_poc_crlf.dctx` | 切り分け用。CRLF区切りの1エントリのみ |
| `ime_multiline_poc_ascii.dctx` | InputString（読み）に ASCII を含む場合の受け入れ確認用。本命ファイルとは別ファイル（ASCII読みが原因で辞書全体が弾かれるリスクを避けるため） |
| `ime_multiline_poc.utf8bom.dctx` | 本命と同一内容の UTF-8 BOM付き版。BOM有無で読み込み可否が変わるかの比較用 |

本命ファイル (`ime_multiline_poc.dctx`) のエントリ一覧:

| InputString（読み） | 内容 |
|---|---|
| `ぎじしーあーる` | 4行本文、区切り CR、末尾改行なし |
| `ぎじえるえふ` | 4行本文、区切り LF、末尾改行なし |
| `ぎじしーあーるえるえふ` | 4行本文、区切り CRLF、末尾改行なし |
| `ぎじたんいつ` | 改行なし1行（比較用の対照）: `発言者：結論：補足：対応：` |
| `ぎじくうぎょう` | CRLF区切り、途中に空行を1つ含む |
| `ぎじたぶ` | CRLF区切り、各行頭にタブ1個 |
| `ぎじまつび` | CRLF区切り、**末尾に改行あり** |
| `ぎじとくしゅ` | XML特殊文字テスト: `A&B` / `<tag>` / `"quote"` / `'apos'` をCRLF区切り4行 |

4行本文:
```
発言者：
結論：
補足：
対応：
```

## テスト実行

```powershell
python -m unittest discover -s poc -p "test_*.py" -v
```

## 検証環境

- Windows 11 Pro build 26200 / Microsoft IME 15.0（`IMJPUEXC` は
  10.0.26100.8972）。旧 IME 互換モードは無効。以下はすべて**現行 IME**
  での実測結果である。

## 実機検証手順（実測で確定した正しい手順）

`.dctx` は Windows 11 に既定で関連付けられており、GUI に辞書インポート用の
専用画面は存在しない（後述の代替手段はある）。実際に有効化まで到達した
手順は次のとおり。

1. `.dctx` は
   `C:\WINDOWS\system32\IME\SHARED\imewdbld.exe "%1"`
   （*Microsoft IME Open Extended Dictionary Module*）に関連付けられている。
   エクスプローラーでのダブルクリック、または次のコマンドで起動する。

   ```powershell
   & 'C:\Windows\System32\IME\SHARED\imewdbld.exe' 'C:\...\poc\out\ime_multiline_poc.dctx'
   ```

2. 「Microsoft IME オープン拡張辞書の署名」という確認ダイアログが**必ず**
   表示され、ユーザーの承認が必要になる。完全な無人自動化はできない。
3. 承認するとファイルが次の場所へコピーされる。

   ```
   %LOCALAPPDATA%\Microsoft\IME\15.0\IMEJP\Dicts\{DictionaryGUID}.dctx
   ```

   - **`.dic` へのコンパイルは行われず、DCTX が XML のまま配置される**
     （コピー後のファイルの SHA-256 が生成物の SHA-256 と完全一致することを
     確認済み）。
   - **ファイル名は DCTX 内の `DictionaryGUID` がそのまま使われる**。
     したがって同一 GUID で再インストールすれば上書き更新、別の GUID なら
     別辞書として追加される。
   - この際 IME が `{GUID}_{別GUID}.dctr`（131,072 bytes）という
     ランタイム索引ファイルを自動生成する。
4. **この時点では辞書はまだ無効**。レジストリ
   `HKCU\Software\Microsoft\IME\15.0\IMEJP\Dictionaries` に
   `DICnn = <path>,1,0` として登録されるが、末尾の `0` は「どの変換モードでも
   使用しない」を意味する。
5. 次のコマンドで全変換モードで有効化する（末尾が `,1,7` になる。これは
   既存の動作中の拡張辞書と同じ値）。

   ```powershell
   & 'C:\Windows\System32\IME\IMEJP\imjpuexc.exe' ADDSYSDICT "%LOCALAPPDATA%\Microsoft\IME\15.0\IMEJP\Dicts\{GUID}.dctx" 1
   ```

6. 状態を確認する。

   ```powershell
   & 'C:\Windows\System32\IME\IMEJP\imjpuexc.exe' CHECKSYSDICT "<同じパス>"
   ```

   有効なら `is used in all of conversion modes` と表示される。
   注: `CHECKSYSDICT` は状態を終了コードで返す仕様のため、終了コードが
   0 以外でも必ずしもエラーとは限らない。判定は出力文字列で行うこと。
7. 反映には IME のオフ→オン、または対象アプリの再起動が必要。
8. 各読み（`ぎじしーあーる` 等）を対象アプリの入力欄で実際にひらがな入力し、
   変換候補から選択する。選択後のテキストが「複数行として」確定されるか、
   改行がどう扱われるかを確認する。

補足: 現行 IME の設定画面には辞書追加 UI が無いため、GUI から操作したい
場合は旧・詳細設定ダイアログ `%SystemRoot%\system32\IME\IMEJP\IMJPUEX.EXE`
が代替手段になる。

## ロールバック手順（正しい手順）

```powershell
# 事前にレジストリをバックアップ（任意だが推奨）
reg export "HKCU\Software\Microsoft\IME\15.0\IMEJP\Dictionaries" backup.reg

$dic = "$env:LOCALAPPDATA\Microsoft\IME\15.0\IMEJP\Dicts\{00D41162-3DE9-4A60-8903-E98D815C6BDB}.dctx"
& 'C:\Windows\System32\IME\IMEJP\imjpuexc.exe' REMOVESYSDICT $dic
Remove-Item "$env:LOCALAPPDATA\Microsoft\IME\15.0\IMEJP\Dicts\{00D41162-3DE9-4A60-8903-E98D815C6BDB}*"
```

- `{00D41162-3DE9-4A60-8903-E98D815C6BDB}` は本命ファイル
  `ime_multiline_poc.dctx` の `DictionaryGUID`。他のファイルを登録した
  場合は、そのファイルの `DictionaryGUID` に読み替えること。
- 既存のユーザー辞書・システム辞書には一切影響しない。実測でも差分は
  `HKCU\...\Dictionaries` への `DICnn` の 1 行追加と `DictTimeStamp` の
  増分のみだった。

## 実機検証結果

**テスト済み: メモ帳（Windows 11）。全 8 エントリが変換候補に出現し、
すべて意図どおり確定された。**

| 読み | 結果 |
|---|---|
| `ぎじたんいつ` | 1行のまま。余計な改行の混入なし（対照群として正常） |
| `ぎじしーあーる` (CR) | 4行として正しく確定 |
| `ぎじえるえふ` (LF) | 4行として正しく確定 |
| `ぎじしーあーるえるえふ` (CRLF) | 4行として正しく確定 |
| `ぎじくうぎょう` | 途中の空行が保持された |
| `ぎじたぶ` | 各行頭のタブが保持された（タブ 4 個を確認） |
| `ぎじまつび` | 4行として確定。ただし末尾改行の有無はこのサンプルでは判定不能 |
| `ぎじとくしゅ` | `A&B` / `<tag>` / `"quote"` / `'apos'` がすべて無損失で到達。文字化け・エスケープ崩れなし |

改行方式ごとの、アプリケーション別の確定結果（実機検証で確定）:

| 改行方式 | メモ帳 | ブラウザ textarea | Excel | Teams | 判定 |
|---|---|---|---|---|---|
| CR   | ✅ 複数行として確定 | ✅ 複数行として確定 | ✅ 複数行として確定 | ❌ 1行に連結される | 可 |
| LF   | ✅ 複数行として確定 | ✅ 複数行として確定 | ✅ 複数行として確定 | ❌ 1行に連結される | 可 |
| CRLF | ✅ 複数行として確定 | ✅ 複数行として確定 | ✅ 複数行として確定 | ❌ 1行に連結される | **採用** |

Outlook は未検証のまま（検証環境の制約により未実施。検証済みと誤読しないこと）。

**CR / LF / CRLF のあいだで実機挙動差は一切観測されなかった。** メモ帳・
ブラウザ textarea・Excel はいずれの方式でも複数行として正しく確定し、
Teams はいずれの方式でも改行が失われ1行に連結される（受け側アプリが
改行を独自に正規化するため、DCTX 側の改行コード選択は結果に影響しない）。
差が無いため、Windows ネイティブの改行コードであり、正本データ
（entries.json）にもそのまま保持できる **CRLF を採用**した。

### 重要な注意（誤読防止）

**メモ帳の編集バッファはあらゆる改行を CRLF に正規化するため、確定後の
テキストをコピーしてバイト列を調べても「IME が実際に送出した改行コード」は
判別できない。** 実際に確定結果のクリップボードを解析したところ CR 単独
0 個・LF 単独 0 個・CRLF 43 組で、すべて CRLF に正規化されていた。
これはメモ帳というアプリ側の挙動であり、上表の「複数行として確定したか」
という判定自体には影響しない。

また、過去の Office IME 2010 時代の「改行は CR になるためメモ帳では
反映されない」という報告は、**現行 Windows 11 IME では再現しなかった**。

### IME 候補ウィンドウでの表示挙動（実測）

- 変換候補一覧では**1行に圧縮して表示**される。
- 候補の右側のコメント/説明ペインでは**複数行のまま表示**される。
- 初回変換時は候補順位が低いが、一度確定すると IME の学習により上位に
  繰り上がる。
