#!/usr/bin/env python3
"""Convert raw RGBA map snapshots (written by the headless map tests) into PNG files.

The EditMode test ``ExplorationMapTests.ScriptedExploration_FillsTheSheetStageByStage_AndSurvivesReload_Evidence``
writes ``<name>.rgba`` (raw RGBA32, rows bottom-up = Unity Texture2D order, south edge first) and
``<name>.json`` (``{"width":..,"height":..,"rowOrder":"bottom-up",...}``) when the environment
variable ``WAYFINDER_MAP_SNAPSHOT_DIR`` is set. This tool flips the rows so north is up and
encodes a standard 8-bit RGBA PNG using only the Python standard library (zlib + struct).

Usage:
    python3 Tools/Validation/rgba_to_png.py <dir> [--keep-raw] [--scale N]
    python3 Tools/Validation/rgba_to_png.py --self-test
"""
from __future__ import annotations

import argparse
import json
import os
import struct
import sys
import zlib

PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"


def _chunk(kind: bytes, data: bytes) -> bytes:
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)


def encode_png(width: int, height: int, rows_top_down: list[bytes]) -> bytes:
    """Encode RGBA8 rows (top row first) as a PNG (filter type 0 per row)."""
    if len(rows_top_down) != height:
        raise ValueError(f"expected {height} rows, got {len(rows_top_down)}")
    raw = bytearray()
    for row in rows_top_down:
        if len(row) != width * 4:
            raise ValueError(f"row length {len(row)} != {width * 4}")
        raw.append(0)
        raw.extend(row)
    header = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)  # 8-bit, colour type 6 = RGBA
    return PNG_SIGNATURE + _chunk(b"IHDR", header) + _chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + _chunk(b"IEND", b"")


def rgba_rows(pixels: bytes, width: int, height: int, bottom_up: bool, scale: int = 1) -> list[bytes]:
    if len(pixels) != width * height * 4:
        raise ValueError(f"pixel buffer is {len(pixels)} bytes, expected {width * height * 4}")
    stride = width * 4
    rows = [pixels[y * stride:(y + 1) * stride] for y in range(height)]
    if bottom_up:
        rows.reverse()
    if scale > 1:
        scaled = []
        for row in rows:
            wide = bytearray()
            for x in range(width):
                wide.extend(row[x * 4:x * 4 + 4] * scale)
            scaled.extend([bytes(wide)] * scale)
        rows = scaled
    return rows


def decode_png_size(data: bytes) -> tuple[int, int]:
    if not data.startswith(PNG_SIGNATURE) or data[12:16] != b"IHDR":
        raise ValueError("not a PNG")
    return struct.unpack(">II", data[16:24])


def convert_dir(directory: str, keep_raw: bool = False, scale: int = 1) -> list[str]:
    written = []
    for name in sorted(os.listdir(directory)):
        if not name.endswith(".rgba"):
            continue
        stem = name[:-5]
        meta_name = stem.replace(".terrain", "") + ".json"
        meta_path = os.path.join(directory, meta_name)
        if not os.path.exists(meta_path):
            raise FileNotFoundError(f"missing {meta_name} for {name}")
        with open(meta_path, encoding="utf-8") as f:
            meta = json.load(f)
        with open(os.path.join(directory, name), "rb") as f:
            pixels = f.read()
        width, height = int(meta["width"]), int(meta["height"])
        rows = rgba_rows(pixels, width, height, meta.get("rowOrder", "bottom-up") == "bottom-up", scale)
        png = encode_png(width * scale, height * scale, rows)
        out = os.path.join(directory, stem + ".png")
        with open(out, "wb") as f:
            f.write(png)
        written.append(out)
        if not keep_raw:
            os.remove(os.path.join(directory, name))
    return written


def self_test() -> int:
    # 2 x 2 image, bottom-up input: bottom row red/green, top row blue/white.
    pixels = bytes([255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255])
    rows = rgba_rows(pixels, 2, 2, bottom_up=True)
    assert rows[0] == bytes([0, 0, 255, 255, 255, 255, 255, 255]), "top row must be the last input row"
    png = encode_png(2, 2, rows)
    assert decode_png_size(png) == (2, 2)
    # Round-trip the IDAT payload.
    idat_len = struct.unpack(">I", png[33:37])[0]
    raw = zlib.decompress(png[41:41 + idat_len])
    assert raw == b"\x00" + rows[0] + b"\x00" + rows[1]
    big = rgba_rows(pixels, 2, 2, bottom_up=True, scale=3)
    assert len(big) == 6 and len(big[0]) == 6 * 4
    try:
        rgba_rows(pixels[:-1], 2, 2, True)
    except ValueError:
        pass
    else:
        raise AssertionError("short buffer must be rejected")
    print("rgba_to_png self-test: OK")
    return 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("directory", nargs="?")
    parser.add_argument("--keep-raw", action="store_true")
    parser.add_argument("--scale", type=int, default=1)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args(argv)
    if args.self_test:
        return self_test()
    if not args.directory:
        parser.error("directory required")
    for path in convert_dir(args.directory, args.keep_raw, max(1, args.scale)):
        print(f"wrote {path}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
