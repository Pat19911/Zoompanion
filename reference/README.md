# reference/

Notes and pointers used while reverse-engineering the game's file formats.
These are **references**, not part of the build.

- `bitmap_format_notes.txt` — notes on the Mohawk `tBMP` bitmap format
  (LZ77 + RLE8). The actual decoder lives in `../bitmap_decoder.py`.
- `cliffs_algorithm.txt` — working notes on the Allergic Cliffs rule logic.

## Mohawk bitmap decoding — external reference

The `tBMP` decoder was cross-checked against **ScummVM's** Mohawk engine, which
implements the same format. ScummVM is **GPL-licensed**, so its source is *not*
vendored here (this project is MIT). See the original instead:

- https://github.com/scummvm/scummvm/blob/master/engines/mohawk/bitmap.cpp
- https://github.com/scummvm/scummvm/blob/master/engines/mohawk/bitmap.h
