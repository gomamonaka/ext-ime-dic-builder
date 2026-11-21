"""Generate src/ExtImeDicBuilder/app.ico from scratch, standard library only.

No PIL/Pillow, no NuGet, no network access. Builds a classic multi-resolution
.ico (ICONDIR -> ICONDIRENTRY[] -> uncompressed 32bpp BGRA DIB images) by hand
with `struct`, since PNG compression is not required for ICO: a 32-bit BGRA
DIB (the same pixel format Windows itself uses internally) is a fully valid
icon image format and needs no image codec at all.

ICO / classic-DIB-icon format notes (this is the tricky part):
  - ICONDIR: 3x WORD (reserved=0, type=1 for icon, image count).
  - ICONDIRENTRY (16 bytes) per image: width/height as BYTE (0 means 256),
    color count, reserved, planes, bit count, byte size of the image data,
    and the absolute file offset of that image data.
  - Each image here is a BITMAPINFOHEADER (40 bytes) followed by the pixel
    data. For an *icon* BITMAPINFOHEADER, biHeight must be set to TWICE the
    actual image height: the format's convention is that it covers a color
    (XOR) bitmap and a 1bpp AND mask stacked together, even though we store
    them as two separate blocks immediately following the header.
  - Color pixel data is bottom-up (last scanline first), 4 bytes/pixel BGRA.
  - The AND mask is a 1-bit-per-pixel bitmap, bottom-up, with each row
    padded to a 4-byte boundary. Since our color data already carries a real
    alpha channel, every mask bit is left at 0 (fully "opaque/use alpha"),
    but the row padding still has to be correct or readers will misparse
    subsequent bytes.

Run with:
    python tools\\make_icon.py
"""

from __future__ import annotations

import pathlib
import struct

ICO_PATH = pathlib.Path(__file__).resolve().parent.parent / "src" / "ExtImeDicBuilder" / "app.ico"

# Sizes bundled into the single .ico, covering Explorer's common display
# sizes (small list icons up to large "extra large" thumbnails).
SIZES = [16, 32, 48, 256]

# Abstract mark: a handful of horizontal bars of decreasing width, meant to
# read as "lines of text" (the app's subject: multi-line IME candidates).
# Not modeled on any real company or product logo.
BAR_RGB = (0, 141, 189)  # a plain teal/blue, high contrast against most backgrounds
WIDTH_FRACTIONS = (1.0, 0.82, 0.95, 0.62)


def make_bars_pixels(size: int) -> list[list[tuple[int, int, int, int]]]:
    """Return a size x size grid of (B, G, R, A) tuples, top-down, row-major."""
    pixels = [[(0, 0, 0, 0) for _ in range(size)] for _ in range(size)]

    margin = max(1, size // 8)
    bar_count = len(WIDTH_FRACTIONS)
    gap = max(1, size // 24)
    available_h = size - 2 * margin
    bar_h = max(1, (available_h - gap * (bar_count - 1)) // bar_count)
    used_h = bar_h * bar_count + gap * (bar_count - 1)
    start_y = margin + max(0, (available_h - used_h) // 2)

    r, g, b = BAR_RGB
    for i, frac in enumerate(WIDTH_FRACTIONS):
        y0 = start_y + i * (bar_h + gap)
        y1 = min(y0 + bar_h, size)
        w = max(1, int((size - 2 * margin) * frac))
        x0 = margin
        x1 = min(x0 + w, size)
        for y in range(max(0, y0), y1):
            for x in range(x0, x1):
                pixels[y][x] = (b, g, r, 255)

    return pixels


def build_dib(pixels: list[list[tuple[int, int, int, int]]], size: int) -> bytes:
    header = struct.pack(
        "<IiiHHIIiiII",
        40,  # biSize
        size,  # biWidth
        size * 2,  # biHeight (icon convention: color + AND mask combined)
        1,  # biPlanes
        32,  # biBitCount
        0,  # biCompression = BI_RGB
        size * size * 4,  # biSizeImage (color data only)
        0,  # biXPelsPerMeter
        0,  # biYPelsPerMeter
        0,  # biClrUsed
        0,  # biClrImportant
    )

    color_bytes = bytearray(size * size * 4)
    offset = 0
    for y in range(size - 1, -1, -1):  # bottom-up
        row = pixels[y]
        for x in range(size):
            b, g, r, a = row[x]
            color_bytes[offset] = b
            color_bytes[offset + 1] = g
            color_bytes[offset + 2] = r
            color_bytes[offset + 3] = a
            offset += 4

    mask_row_bytes = ((size + 31) // 32) * 4  # 1bpp rows padded to 4-byte boundary
    mask_bytes = bytes(mask_row_bytes * size)  # all-zero: rely on the alpha channel

    return header + bytes(color_bytes) + mask_bytes


def build_ico(sizes: list[int]) -> bytes:
    images = [(size, build_dib(make_bars_pixels(size), size)) for size in sizes]

    icondir = struct.pack("<HHH", 0, 1, len(images))

    header_len = 6
    entry_len = 16
    data_offset = header_len + entry_len * len(images)

    entries = bytearray()
    blobs = bytearray()
    for size, data in images:
        wh_byte = size if size < 256 else 0
        entries += struct.pack(
            "<BBBBHHII",
            wh_byte,  # bWidth
            wh_byte,  # bHeight
            0,  # bColorCount (0 = not palettized)
            0,  # bReserved
            1,  # wPlanes
            32,  # wBitCount
            len(data),  # dwBytesInRes
            data_offset + len(blobs),  # dwImageOffset
        )
        blobs += data

    return icondir + bytes(entries) + bytes(blobs)


def self_check(ico_bytes: bytes, expected_sizes: list[int]) -> None:
    """Re-parse the bytes we just built and assert internal consistency.

    Deliberately does not use System.Drawing.Icon / any image library: this
    walks the same ICONDIR/ICONDIRENTRY/BITMAPINFOHEADER structure by hand so
    the check is independent of whatever wrote the file.
    """
    reserved, image_type, count = struct.unpack_from("<HHH", ico_bytes, 0)
    assert reserved == 0, f"ICONDIR.reserved must be 0, got {reserved}"
    assert image_type == 1, f"ICONDIR.type must be 1 (icon), got {image_type}"
    assert count == len(expected_sizes), f"entry count mismatch: {count} != {len(expected_sizes)}"

    entry_offset = 6
    for i, expected_size in enumerate(expected_sizes):
        (
            b_width,
            b_height,
            color_count,
            reserved2,
            planes,
            bit_count,
            bytes_in_res,
            image_offset,
        ) = struct.unpack_from("<BBBBHHII", ico_bytes, entry_offset)
        entry_offset += 16

        actual_width = 256 if b_width == 0 else b_width
        actual_height = 256 if b_height == 0 else b_height
        assert actual_width == expected_size, f"entry {i}: width {actual_width} != {expected_size}"
        assert actual_height == expected_size, f"entry {i}: height {actual_height} != {expected_size}"
        assert color_count == 0
        assert reserved2 == 0
        assert planes == 1
        assert bit_count == 32

        assert image_offset + bytes_in_res <= len(ico_bytes), (
            f"entry {i}: image data (offset={image_offset}, size={bytes_in_res}) "
            f"runs past end of file (len={len(ico_bytes)})"
        )

        (
            bi_size,
            bi_width,
            bi_height,
            bi_planes,
            bi_bit_count,
            bi_compression,
            bi_size_image,
            _xppm,
            _yppm,
            _clr_used,
            _clr_important,
        ) = struct.unpack_from("<IiiHHIIiiII", ico_bytes, image_offset)

        assert bi_size == 40, f"entry {i}: biSize must be 40, got {bi_size}"
        assert bi_width == expected_size, f"entry {i}: biWidth {bi_width} != {expected_size}"
        assert bi_height == expected_size * 2, (
            f"entry {i}: biHeight must be 2x image height ({expected_size * 2}), got {bi_height}"
        )
        assert bi_planes == 1
        assert bi_bit_count == 32
        assert bi_compression == 0
        assert bi_size_image == expected_size * expected_size * 4

        expected_total = 40 + expected_size * expected_size * 4 + (((expected_size + 31) // 32) * 4) * expected_size
        assert bytes_in_res == expected_total, (
            f"entry {i}: dwBytesInRes {bytes_in_res} != expected {expected_total} "
            "(header + color data + padded AND mask)"
        )


def main() -> None:
    ico_bytes = build_ico(SIZES)
    self_check(ico_bytes, SIZES)  # must pass before we write anything

    ICO_PATH.parent.mkdir(parents=True, exist_ok=True)
    ICO_PATH.write_bytes(ico_bytes)

    # Re-read from disk and self-check again, so the on-disk file itself
    # (not just the in-memory bytes) is verified.
    on_disk = ICO_PATH.read_bytes()
    self_check(on_disk, SIZES)

    print(f"Wrote {ICO_PATH} ({len(on_disk)} bytes), sizes={SIZES}, self-check OK")


if __name__ == "__main__":
    main()
