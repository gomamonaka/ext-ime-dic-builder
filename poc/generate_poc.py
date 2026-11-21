"""Generate the PoC .dctx dictionaries under poc/out/.

Standard library only. No registry access, no IME configuration changes, no
network access. Run with:

    python poc\\generate_poc.py

After writing each file this script re-parses it with
``xml.etree.ElementTree`` and asserts that every ``OutputString`` comes back
byte-for-byte identical to the Python string that was used to generate it.
This round-trip check is the core of the PoC: it proves the numeric
character references actually survive standard XML parsing instead of being
collapsed by end-of-line normalization.
"""

from __future__ import annotations

import pathlib
import xml.etree.ElementTree as ET

from dctx import DictionaryMeta, Entry, build_dctx, join_lines

OUT_DIR = pathlib.Path(__file__).resolve().parent / "out"

NS = {"ns1": "http://www.microsoft.com/ime/dctx"}

# Fixed, per-file GUIDs. Generated once with uuid.uuid4() and hardcoded here
# so that repeated runs of this script are idempotent (same GUID every time).
GUID_MAIN = "{00d41162-3de9-4a60-8903-e98d815c6bdb}"
GUID_CR = "{6468566d-56e1-4383-aba2-d52eec31bd2a}"
GUID_LF = "{5bb39678-5470-487a-92f0-fdd7fe764e70}"
GUID_CRLF = "{3bb7ca6b-ba78-4f89-8ae4-0dff57a6a6a1}"
GUID_ASCII = "{ac5ab976-5c1c-46c0-97a6-d7b4eafd58c7}"
GUID_BOM = "{41e14f35-4291-4559-ac46-226df28b0b29}"

BODY_LINES = ["発言者：", "結論：", "補足：", "対応："]

COPYRIGHT = "Copyright (c) 2026 PoC"


def body(newline_mode: str, trailing_newline: bool = False) -> str:
    return join_lines(BODY_LINES, newline_mode, trailing_newline=trailing_newline)


def build_main_entries() -> list[Entry]:
    entries = [
        Entry(reading="ぎじしーあーる", output=body("cr")),
        Entry(reading="ぎじえるえふ", output=body("lf")),
        Entry(reading="ぎじしーあーるえるえふ", output=body("crlf")),
        Entry(reading="ぎじたんいつ", output="発言者：結論：補足：対応："),
        Entry(
            reading="ぎじくうぎょう",
            output=join_lines(["発言者：", "", "結論：", "補足："], "crlf"),
        ),
        Entry(
            reading="ぎじたぶ",
            output=join_lines(["\t発言者：", "\t結論：", "\t補足：", "\t対応："], "crlf"),
        ),
        Entry(reading="ぎじまつび", output=body("crlf", trailing_newline=True)),
        Entry(
            reading="ぎじとくしゅ",
            output=join_lines(["A&B", "<tag>", '"quote"', "'apos'"], "crlf"),
        ),
    ]
    return entries


def build_ascii_entries() -> list[Entry]:
    return [
        Entry(reading="ぎじcr", output=body("cr")),
        Entry(reading="ぎじlf", output=body("lf")),
        Entry(reading="ぎじcrlf", output=body("crlf")),
    ]


def write_file(path: pathlib.Path, meta: DictionaryMeta, entries, *, newline_mode: str, bom: bool = False) -> bytes:
    data = build_dctx(meta, entries, newline_mode=newline_mode, bom=bom)
    path.write_bytes(data)
    return data


def verify_file(path: pathlib.Path, data: bytes, entries) -> None:
    """Re-parse ``data`` and assert every OutputString matches exactly."""
    xml_bytes = data
    if xml_bytes.startswith(b"\xef\xbb\xbf"):
        xml_bytes = xml_bytes[3:]
    root = ET.fromstring(xml_bytes)
    parsed_outputs = [el.text or "" for el in root.findall("ns1:DictionaryEntry/ns1:OutputString", NS)]
    expected_outputs = [entry.output for entry in entries]
    assert len(parsed_outputs) == len(expected_outputs), (
        f"{path.name}: entry count mismatch: parsed={len(parsed_outputs)} "
        f"expected={len(expected_outputs)}"
    )
    for i, (parsed, expected) in enumerate(zip(parsed_outputs, expected_outputs)):
        assert parsed == expected, (
            f"{path.name}: entry {i} OutputString mismatch\n"
            f"  expected={expected!r}\n"
            f"  parsed  ={parsed!r}"
        )


def main() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    results = []

    # 1. Main combined PoC dictionary.
    main_entries = build_main_entries()
    main_meta = DictionaryMeta(
        guid=GUID_MAIN,
        short_name="複数行PoC",
        long_name="複数行変換候補 PoC 辞書",
        description="IME拡張辞書で複数行の変換候補（改行コード CR/LF/CRLF）が機能するかを検証するPoC辞書",
        copyright=COPYRIGHT,
    )
    path = OUT_DIR / "ime_multiline_poc.dctx"
    data = write_file(path, main_meta, main_entries, newline_mode="preserve")
    verify_file(path, data, main_entries)
    results.append((path, data))

    # 2-4. Single-entry isolation files (CR / LF / CRLF), separate GUIDs.
    cr_entries = [Entry(reading="ぎじしーあーる", output=body("cr"))]
    cr_meta = DictionaryMeta(
        guid=GUID_CR,
        short_name="複数行PoC_CR",
        long_name="複数行PoC 切り分け用 (CR)",
        description="CR区切りの複数行候補のみを含む切り分け用PoC辞書",
        copyright=COPYRIGHT,
    )
    path = OUT_DIR / "ime_multiline_poc_cr.dctx"
    data = write_file(path, cr_meta, cr_entries, newline_mode="cr")
    verify_file(path, data, cr_entries)
    results.append((path, data))

    lf_entries = [Entry(reading="ぎじえるえふ", output=body("lf"))]
    lf_meta = DictionaryMeta(
        guid=GUID_LF,
        short_name="複数行PoC_LF",
        long_name="複数行PoC 切り分け用 (LF)",
        description="LF区切りの複数行候補のみを含む切り分け用PoC辞書",
        copyright=COPYRIGHT,
    )
    path = OUT_DIR / "ime_multiline_poc_lf.dctx"
    data = write_file(path, lf_meta, lf_entries, newline_mode="lf")
    verify_file(path, data, lf_entries)
    results.append((path, data))

    crlf_entries = [Entry(reading="ぎじしーあーるえるえふ", output=body("crlf"))]
    crlf_meta = DictionaryMeta(
        guid=GUID_CRLF,
        short_name="複数行PoC_CRLF",
        long_name="複数行PoC 切り分け用 (CRLF)",
        description="CRLF区切りの複数行候補のみを含む切り分け用PoC辞書",
        copyright=COPYRIGHT,
    )
    path = OUT_DIR / "ime_multiline_poc_crlf.dctx"
    data = write_file(path, crlf_meta, crlf_entries, newline_mode="crlf")
    verify_file(path, data, crlf_entries)
    results.append((path, data))

    # 5. ASCII reading acceptance test file (kept separate from the main file
    #    on purpose: if ASCII readings are rejected, they must not take the
    #    rest of the dictionary down with them).
    ascii_entries = build_ascii_entries()
    ascii_meta = DictionaryMeta(
        guid=GUID_ASCII,
        short_name="複数行PoC_ASCII読み",
        long_name="複数行PoC ASCII読み受け入れテスト",
        description="InputString に ASCII 文字を含む読みが受理されるかを検証するPoC辞書",
        copyright=COPYRIGHT,
    )
    path = OUT_DIR / "ime_multiline_poc_ascii.dctx"
    data = write_file(path, ascii_meta, ascii_entries, newline_mode="preserve")
    verify_file(path, data, ascii_entries)
    results.append((path, data))

    # 6. Same content as the main file, but with a UTF-8 BOM.
    bom_meta = DictionaryMeta(
        guid=GUID_BOM,
        short_name="複数行PoC",
        long_name="複数行変換候補 PoC 辞書 (UTF-8 BOM付き)",
        description="本命ファイルと同一内容の UTF-8 BOM 付き版。BOM有無での読み込み可否比較用",
        copyright=COPYRIGHT,
    )
    path = OUT_DIR / "ime_multiline_poc.utf8bom.dctx"
    data = write_file(path, bom_meta, main_entries, newline_mode="preserve", bom=True)
    verify_file(path, data, main_entries)
    results.append((path, data))

    print(f"Generated {len(results)} file(s) in {OUT_DIR}:")
    for p, data in results:
        print(f"  {p.name}: {len(data)} bytes, self-verify OK")


if __name__ == "__main__":
    main()
