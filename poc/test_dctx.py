"""Unit tests for poc/dctx.py.

Run with:
    python -m unittest discover -s poc -p "test_*.py" -v

Standard library only.
"""

from __future__ import annotations

import unittest
import xml.etree.ElementTree as ET

from dctx import DictionaryMeta, Entry, build_dctx, join_lines, escape_text, normalize_newlines

NS = {"ns1": "http://www.microsoft.com/ime/dctx"}
XSI_NS = "http://www.w3.org/2001/XMLSchema-instance"


def make_meta(guid="{11111111-1111-1111-1111-111111111111}") -> DictionaryMeta:
    return DictionaryMeta(
        guid=guid,
        short_name="テスト辞書",
        long_name="テスト用ロングネーム",
        description="unittest 用の説明",
        copyright="Copyright (c) 2026 Test",
    )


def parse(data: bytes) -> ET.Element:
    xml_bytes = data[3:] if data.startswith(b"\xef\xbb\xbf") else data
    return ET.fromstring(xml_bytes)


def output_strings(root: ET.Element) -> list[str]:
    return [el.text or "" for el in root.findall("ns1:DictionaryEntry/ns1:OutputString", NS)]


class WellFormedTests(unittest.TestCase):
    def test_well_formed_and_parses(self):
        entries = [Entry(reading="よみ", output="変換結果")]
        data = build_dctx(make_meta(), entries, newline_mode="preserve")
        root = parse(data)  # raises if not well-formed
        self.assertTrue(root.tag.endswith("}Dictionary"))

    def test_header_field_order_and_values(self):
        meta = make_meta()
        entries = [Entry(reading="よみ", output="出力")]
        data = build_dctx(meta, entries, newline_mode="preserve")
        root = parse(data)
        header = root.find("ns1:DictionaryHeader", NS)
        tags = [el.tag.split("}")[-1] for el in header]
        self.assertEqual(tags, ["DictionaryGUID", "DictionaryLanguage", "DictionaryVersion",
                                  "CommentInsertion", "DictionaryInfo"])
        self.assertEqual(header.find("ns1:DictionaryGUID", NS).text, meta.guid)
        self.assertEqual(header.find("ns1:DictionaryLanguage", NS).text, "ja-jp")
        self.assertEqual(header.find("ns1:DictionaryVersion", NS).text, "1")
        self.assertEqual(header.find("ns1:CommentInsertion", NS).text, "true")
        info = header.find("ns1:DictionaryInfo", NS)
        self.assertEqual(info.get("Language"), "ja-jp")
        self.assertEqual(info.find("ns1:ShortName", NS).text, meta.short_name)
        self.assertEqual(info.find("ns1:LongName", NS).text, meta.long_name)
        self.assertEqual(info.find("ns1:Description", NS).text, meta.description)
        self.assertEqual(info.find("ns1:Copyright", NS).text, meta.copyright)

    def test_entry_field_order(self):
        entries = [Entry(reading="よみ", output="出力", part_of_speech="Noun",
                          priority=100, reverse_conversion=False, common_word=False)]
        data = build_dctx(make_meta(), entries, newline_mode="preserve")
        root = parse(data)
        entry_el = root.find("ns1:DictionaryEntry", NS)
        tags = [el.tag.split("}")[-1] for el in entry_el]
        self.assertEqual(tags, ["InputString", "OutputString", "PartOfSpeech",
                                  "Priority", "ReverseConversion", "CommonWord"])
        self.assertEqual(entry_el.find("ns1:InputString", NS).text, "よみ")
        self.assertEqual(entry_el.find("ns1:OutputString", NS).text, "出力")
        self.assertEqual(entry_el.find("ns1:PartOfSpeech", NS).text, "Noun")
        self.assertEqual(entry_el.find("ns1:Priority", NS).text, "100")
        self.assertEqual(entry_el.find("ns1:ReverseConversion", NS).text, "false")
        self.assertEqual(entry_el.find("ns1:CommonWord", NS).text, "false")

    def test_namespace_is_correct(self):
        data = build_dctx(make_meta(), [Entry(reading="a", output="b")], newline_mode="preserve")
        root = parse(data)
        self.assertEqual(root.tag, "{http://www.microsoft.com/ime/dctx}Dictionary")
        # xsi namespace declared on the root, even if unused elsewhere.
        self.assertIn(f'xmlns:xsi="{XSI_NS}"', data.decode("utf-8"))

    def test_no_bom_starts_with_xml_declaration(self):
        data = build_dctx(make_meta(), [Entry(reading="a", output="b")], newline_mode="preserve", bom=False)
        self.assertTrue(data.startswith(b"<?xml"))

    def test_bom_prefix(self):
        data = build_dctx(make_meta(), [Entry(reading="a", output="b")], newline_mode="preserve", bom=True)
        self.assertTrue(data.startswith(b"\xef\xbb\xbf<?xml"))


class NewlineRoundTripTests(unittest.TestCase):
    LINES = ["発言者：", "結論：", "補足：", "対応："]

    def _roundtrip(self, output: str, newline_mode: str) -> str:
        entries = [Entry(reading="よみ", output=output)]
        data = build_dctx(make_meta(), entries, newline_mode=newline_mode)
        root = parse(data)
        return output_strings(root)[0]

    def test_cr_roundtrip_exact(self):
        expected = "\r".join(self.LINES)
        got = self._roundtrip(expected, newline_mode="preserve")
        self.assertEqual(got, expected)

    def test_lf_roundtrip_exact(self):
        expected = "\n".join(self.LINES)
        got = self._roundtrip(expected, newline_mode="preserve")
        self.assertEqual(got, expected)

    def test_crlf_roundtrip_exact(self):
        expected = "\r\n".join(self.LINES)
        got = self._roundtrip(expected, newline_mode="preserve")
        self.assertEqual(got, expected)

    def test_newline_mode_cr_forces_cr(self):
        source = "\n".join(self.LINES)  # written with plain \n
        data = build_dctx(make_meta(), [Entry(reading="a", output=source)], newline_mode="cr")
        self.assertIn(b"&#13;", data)
        self.assertNotIn(b"&#10;", data)
        got = output_strings(parse(data))[0]
        self.assertEqual(got, "\r".join(self.LINES))

    def test_newline_mode_lf_forces_lf(self):
        source = "\r\n".join(self.LINES)
        data = build_dctx(make_meta(), [Entry(reading="a", output=source)], newline_mode="lf")
        self.assertIn(b"&#10;", data)
        self.assertNotIn(b"&#13;", data)
        got = output_strings(parse(data))[0]
        self.assertEqual(got, "\n".join(self.LINES))

    def test_newline_mode_crlf_forces_crlf(self):
        source = "\n".join(self.LINES)
        data = build_dctx(make_meta(), [Entry(reading="a", output=source)], newline_mode="crlf")
        self.assertIn(b"&#13;&#10;", data)
        got = output_strings(parse(data))[0]
        self.assertEqual(got, "\r\n".join(self.LINES))

    def test_cr_bytes_contain_numeric_ref_not_raw_cr(self):
        entries = [Entry(reading="a", output="line1\rline2")]
        data = build_dctx(make_meta(), entries, newline_mode="preserve")
        self.assertIn(b"&#13;", data)
        # Raw CR must not appear as a literal byte within the entry text
        # (it would be normalized away by any conformant XML parser).
        body_start = data.index(b"<ns1:DictionaryEntry>")
        self.assertNotIn(b"\r", data[body_start:])

    def test_crlf_bytes_contain_both_numeric_refs(self):
        entries = [Entry(reading="a", output="line1\r\nline2")]
        data = build_dctx(make_meta(), entries, newline_mode="preserve")
        self.assertIn(b"&#13;&#10;", data)


class SpecialCharacterTests(unittest.TestCase):
    def test_escape_ampersand_lt_gt_quotes(self):
        raw = """A&B <tag> "quote" 'apos'"""
        entries = [Entry(reading="a", output=raw)]
        data = build_dctx(make_meta(), entries, newline_mode="preserve")
        got = output_strings(parse(data))[0]
        self.assertEqual(got, raw)
        # Confirm the raw special characters are actually escaped in the
        # serialized bytes, not merely "happens to parse".
        text = data.decode("utf-8")
        self.assertIn("&amp;", text)
        self.assertIn("&lt;tag&gt;", text)

    def test_escape_text_helper_directly(self):
        self.assertEqual(escape_text("A&B"), "A&amp;B")
        self.assertEqual(escape_text("<x>"), "&lt;x&gt;")
        self.assertEqual(escape_text('"q"'), "&quot;q&quot;")
        self.assertEqual(escape_text("'q'"), "&apos;q&apos;")
        self.assertEqual(escape_text("\r"), "&#13;")
        self.assertEqual(escape_text("\n"), "&#10;")
        self.assertEqual(escape_text("\t"), "&#9;")

    def test_mixed_special_and_newlines_roundtrip(self):
        raw = "A&B\r\n<tag>\r\n\"quote\"\r\n'apos'"
        entries = [Entry(reading="a", output=raw)]
        data = build_dctx(make_meta(), entries, newline_mode="preserve")
        got = output_strings(parse(data))[0]
        self.assertEqual(got, raw)


class TabAndBlankLineTests(unittest.TestCase):
    def test_tab_preserved(self):
        raw = "\t発言者：\r\n\t結論："
        entries = [Entry(reading="a", output=raw)]
        data = build_dctx(make_meta(), entries, newline_mode="preserve")
        self.assertIn(b"&#9;", data)
        got = output_strings(parse(data))[0]
        self.assertEqual(got, raw)

    def test_blank_line_preserved(self):
        raw = "発言者：\r\n\r\n結論："
        entries = [Entry(reading="a", output=raw)]
        data = build_dctx(make_meta(), entries, newline_mode="preserve")
        got = output_strings(parse(data))[0]
        self.assertEqual(got, raw)


class TrailingNewlineTests(unittest.TestCase):
    def test_trailing_newline_preserved(self):
        raw = "発言者：\r\n結論：\r\n"
        entries = [Entry(reading="a", output=raw)]
        data = build_dctx(make_meta(), entries, newline_mode="preserve")
        got = output_strings(parse(data))[0]
        self.assertEqual(got, raw)
        self.assertTrue(got.endswith("\r\n"))

    def test_no_trailing_newline_preserved(self):
        raw = "発言者：\r\n結論："
        entries = [Entry(reading="a", output=raw)]
        data = build_dctx(make_meta(), entries, newline_mode="preserve")
        got = output_strings(parse(data))[0]
        self.assertEqual(got, raw)
        self.assertFalse(got.endswith("\r\n"))
        self.assertFalse(got.endswith("\n"))


class UnicodeTests(unittest.TestCase):
    def test_japanese_roundtrip(self):
        raw = "発言者：結論：補足：対応："
        entries = [Entry(reading="よみ", output=raw)]
        data = build_dctx(make_meta(), entries, newline_mode="preserve")
        root = parse(data)
        self.assertEqual(root.find(".//ns1:InputString", NS).text, "よみ")
        self.assertEqual(output_strings(root)[0], raw)

    def test_emoji_surrogate_pair_roundtrip(self):
        raw = "テスト\U0001F600\U0001F44D終わり"  # includes astral-plane emoji
        entries = [Entry(reading="よみ", output=raw)]
        data = build_dctx(make_meta(), entries, newline_mode="preserve")
        got = output_strings(parse(data))[0]
        self.assertEqual(got, raw)


class BomTests(unittest.TestCase):
    def test_bom_absent_by_default(self):
        data = build_dctx(make_meta(), [Entry(reading="a", output="b")], newline_mode="preserve")
        self.assertFalse(data.startswith(b"\xef\xbb\xbf"))

    def test_bom_present_when_requested(self):
        data = build_dctx(make_meta(), [Entry(reading="a", output="b")], newline_mode="preserve", bom=True)
        self.assertTrue(data.startswith(b"\xef\xbb\xbf"))

    def test_bom_and_no_bom_parse_to_same_content(self):
        entries = [Entry(reading="a", output="b\r\nc")]
        no_bom = build_dctx(make_meta(), entries, newline_mode="preserve", bom=False)
        with_bom = build_dctx(make_meta(), entries, newline_mode="preserve", bom=True)
        self.assertEqual(with_bom, b"\xef\xbb\xbf" + no_bom)
        self.assertEqual(
            output_strings(parse(no_bom)),
            output_strings(parse(with_bom)),
        )


class IdempotencyTests(unittest.TestCase):
    def test_two_generations_are_byte_identical(self):
        meta = make_meta()
        entries = [
            Entry(reading="ぎじしーあーる", output="a\rb"),
            Entry(reading="ぎじえるえふ", output="a\nb"),
            Entry(reading="ぎじしーあーるえるえふ", output="a\r\nb"),
        ]
        data1 = build_dctx(meta, entries, newline_mode="preserve")
        data2 = build_dctx(meta, entries, newline_mode="preserve")
        self.assertEqual(data1, data2)


class NormalizeNewlinesTests(unittest.TestCase):
    def test_preserve_is_identity(self):
        self.assertEqual(normalize_newlines("a\rb\nc\r\nd", "preserve"), "a\rb\nc\r\nd")

    def test_lf_collapses_all_styles(self):
        self.assertEqual(normalize_newlines("a\rb\nc\r\nd", "lf"), "a\nb\nc\nd")

    def test_cr_collapses_all_styles(self):
        self.assertEqual(normalize_newlines("a\rb\nc\r\nd", "cr"), "a\rb\rc\rd")

    def test_crlf_collapses_all_styles(self):
        self.assertEqual(normalize_newlines("a\rb\nc\r\nd", "crlf"), "a\r\nb\r\nc\r\nd")

    def test_unknown_mode_raises(self):
        with self.assertRaises(ValueError):
            normalize_newlines("a", "bogus")


class JoinLinesTests(unittest.TestCase):
    def test_join_lines_cr(self):
        self.assertEqual(join_lines(["a", "b", "c"], "cr"), "a\rb\rc")

    def test_join_lines_trailing(self):
        self.assertEqual(join_lines(["a", "b"], "crlf", trailing_newline=True), "a\r\nb\r\n")

    def test_join_lines_rejects_preserve(self):
        with self.assertRaises(ValueError):
            join_lines(["a"], "preserve")


if __name__ == "__main__":
    unittest.main()
