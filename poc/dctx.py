"""Core builder for Microsoft IME "Open Extended Dictionary" (.dctx) files.

This module is a pure, side-effect-free library: it only turns in-memory
data (``DictionaryMeta`` + a list of ``Entry``) into ``bytes``. It performs
no file I/O and no registry/IME access, so it can be reused as-is from a
future GUI layer.

Why not just use ``xml.etree.ElementTree.tostring``?
-----------------------------------------------------
XML 1.0 end-of-line handling (see https://www.w3.org/TR/xml/#sec-line-ends)
requires every conforming parser to normalize raw CR and CRLF sequences in
the *source* to a single LF before the document text is handed to the
application. That normalization happens no matter which serializer wrote the
file, so a raw ``\r`` or ``\r\n`` byte written into element text is silently
turned into ``\n`` in the round trip. The only reliable way to transmit CR or
CRLF through XML text content is to encode it as a numeric character
reference (``&#13;`` / ``&#13;&#10;``) which is *not* subject to
end-of-line normalization once parsed.

``ElementTree.tostring`` does not perform this control-character escaping
(it only escapes ``&``, ``<``, ``>`` and, in attributes, quotes), so this
module does its own minimal, centralized text serialization instead of
relying on ElementTree's serializer. All text escaping funnels through a
single function, :func:`escape_text`, which:

1. Escapes the five predefined XML characters (``& < > " '``) using
   ``xml.sax.saxutils.escape`` (never hand-rolled string replacement), then
2. Converts CR / LF / TAB into numeric character references.

Nothing else in this module concatenates raw, unescaped user text into the
output XML string.
"""

from __future__ import annotations

import dataclasses
from typing import Sequence
from xml.sax.saxutils import escape as _xml_escape

NS_URI = "http://www.microsoft.com/ime/dctx"
XSI_URI = "http://www.w3.org/2001/XMLSchema-instance"

# Extra entities beyond the saxutils default (& < >): quotes.
_EXTRA_ENTITIES = {'"': "&quot;", "'": "&apos;"}

# Control characters that XML 1.0 end-of-line normalization (or, for TAB,
# attribute-value normalization) would otherwise mangle. Order matters only
# in that these are disjoint single characters, so any order is safe.
_CONTROL_CHAR_REFS = {
    "\r": "&#13;",
    "\n": "&#10;",
    "\t": "&#9;",
}


def escape_text(value: str) -> str:
    """Escape ``value`` for use as XML element text content.

    This is the single centralized escaping routine used everywhere in this
    module. It escapes ``& < > " '`` via ``xml.sax.saxutils.escape`` and then
    replaces CR / LF / TAB with numeric character references so that CR and
    CRLF line breaks survive XML 1.0 end-of-line normalization intact.
    """
    escaped = _xml_escape(value, _EXTRA_ENTITIES)
    for ch, ref in _CONTROL_CHAR_REFS.items():
        escaped = escaped.replace(ch, ref)
    return escaped


NewlineMode = str  # one of: "cr", "lf", "crlf", "preserve"

_NEWLINE_SEPARATORS = {"cr": "\r", "lf": "\n", "crlf": "\r\n"}


def normalize_newlines(text: str, newline_mode: NewlineMode) -> str:
    """Rewrite line breaks in ``text`` to the style named by ``newline_mode``.

    "preserve" returns ``text`` unchanged, which lets a caller hand-build
    content that mixes newline styles within a single string (used for the
    combined PoC dictionary, whose entries each demonstrate a different
    style). The other modes first collapse every CRLF/CR/LF already present
    to a bare LF and then re-expand to the requested style, so callers may
    always write source text using plain "\\n" and let this function pick
    the final on-disk representation.
    """
    if newline_mode == "preserve":
        return text
    if newline_mode not in _NEWLINE_SEPARATORS:
        raise ValueError(f"unknown newline_mode: {newline_mode!r}")
    normalized = text.replace("\r\n", "\n").replace("\r", "\n")
    return normalized.replace("\n", _NEWLINE_SEPARATORS[newline_mode])


def join_lines(lines: Sequence[str], newline_mode: NewlineMode, trailing_newline: bool = False) -> str:
    """Join ``lines`` using the raw separator for ``newline_mode``.

    Convenience helper for building an ``Entry.output`` string with an
    explicit, already-final newline style (used together with
    ``newline_mode="preserve"`` in :func:`build_dctx`). ``newline_mode`` must
    be one of "cr", "lf", "crlf" (not "preserve").
    """
    if newline_mode not in _NEWLINE_SEPARATORS:
        raise ValueError(f"join_lines requires an explicit style, got {newline_mode!r}")
    sep = _NEWLINE_SEPARATORS[newline_mode]
    text = sep.join(lines)
    if trailing_newline:
        text += sep
    return text


@dataclasses.dataclass(frozen=True)
class Entry:
    """One ``DictionaryEntry``."""

    reading: str
    output: str
    part_of_speech: str = "Noun"
    priority: int = 100
    reverse_conversion: bool = False
    common_word: bool = False


@dataclasses.dataclass(frozen=True)
class DictionaryMeta:
    """The ``DictionaryHeader`` fields."""

    guid: str  # "{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}", lowercase, fixed per file
    short_name: str
    long_name: str
    description: str
    copyright: str
    language: str = "ja-jp"
    version: int = 1
    comment_insertion: bool = True


def _bool_str(value: bool) -> str:
    return "true" if value else "false"


def _element(tag: str, text: str) -> str:
    return f"<ns1:{tag}>{escape_text(text)}</ns1:{tag}>"


def _entry_xml(entry: Entry, newline_mode: NewlineMode) -> str:
    output_text = normalize_newlines(entry.output, newline_mode)
    lines = [
        "  <ns1:DictionaryEntry>",
        f"    {_element('InputString', entry.reading)}",
        f"    {_element('OutputString', output_text)}",
        f"    {_element('PartOfSpeech', entry.part_of_speech)}",
        f"    {_element('Priority', str(entry.priority))}",
        f"    {_element('ReverseConversion', _bool_str(entry.reverse_conversion))}",
        f"    {_element('CommonWord', _bool_str(entry.common_word))}",
        "  </ns1:DictionaryEntry>",
    ]
    return "\n".join(lines)


def build_dctx(
    meta: DictionaryMeta,
    entries: Sequence[Entry],
    *,
    newline_mode: NewlineMode,
    bom: bool = False,
) -> bytes:
    """Build a complete ``.dctx`` file and return it as bytes.

    ``newline_mode`` is one of "cr" / "lf" / "crlf" / "preserve" and controls
    how line breaks inside every ``Entry.output`` are represented on disk
    (see :func:`normalize_newlines`). Pass "preserve" when entries already
    contain their final, intended raw newline characters (e.g. when a single
    file mixes entries of different newline styles).

    The returned bytes are UTF-8. Pass ``bom=True`` to prepend a UTF-8 byte
    order mark (EF BB BF); the default (``False``) produces a BOM-less file.
    """
    header = [
        "  <ns1:DictionaryHeader>",
        f"    {_element('DictionaryGUID', meta.guid)}",
        f"    {_element('DictionaryLanguage', meta.language)}",
        f"    {_element('DictionaryVersion', str(meta.version))}",
        f"    {_element('CommentInsertion', _bool_str(meta.comment_insertion))}",
        f'    <ns1:DictionaryInfo Language="{_xml_escape(meta.language, _EXTRA_ENTITIES)}">',
        f"      {_element('ShortName', meta.short_name)}",
        f"      {_element('LongName', meta.long_name)}",
        f"      {_element('Description', meta.description)}",
        f"      {_element('Copyright', meta.copyright)}",
        "    </ns1:DictionaryInfo>",
        "  </ns1:DictionaryHeader>",
    ]

    body = [_entry_xml(entry, newline_mode) for entry in entries]

    doc = [
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>',
        f'<ns1:Dictionary xmlns:ns1="{NS_URI}" xmlns:xsi="{XSI_URI}">',
        *header,
        *body,
        "</ns1:Dictionary>",
    ]
    text = "\n".join(doc) + "\n"
    data = text.encode("utf-8")
    if bom:
        data = b"\xef\xbb\xbf" + data
    return data
