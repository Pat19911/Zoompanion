#!/usr/bin/env python3
"""
Mohawk tBMP bitmap decoder for Zoombinis: Logical Journey.

Two-stage decompression pipeline (from ScummVM source):
  Stage 1 (Pack): LZ77 sliding-window decompression of entire data block
  Stage 2 (Draw): RLE8 per-scanline rendering OR raw pixel copy

Format flags (uint16 BE):
  Bits 0-2  (0x0007): BPP (0x02 = 8bpp)
  Bit  3    (0x0008): kBitmapHasCLUT (embedded palette follows header)
  Bits 4-7  (0x00F0): Draw method (0x00=Raw, 0x10=RLE8)
  Bits 8-11 (0x0F00): Pack method (0x00=None, 0x100=LZ)
"""

import struct
import sys
import io
from pathlib import Path
from PIL import Image

# Format constants
kBitmapHasCLUT = 0x0008
kDrawRaw    = 0x0000
kDrawRLE8   = 0x0010
kPackNone   = 0x0000
kPackLZ     = 0x0100

# LZ constants
LEN_BITS    = 6
MIN_STRING  = 3
MAX_STRING  = (1 << LEN_BITS) + MIN_STRING - 1  # 66
POS_BITS    = 16 - LEN_BITS  # 10
CBUFFERSIZE = 1 << POS_BITS  # 1024
POS_MASK    = CBUFFERSIZE - 1  # 0x3FF


def decompress_lz(data):
    """Decompress Mohawk LZ77 data.

    Header (10 bytes): uncompressed_size(4) + compressed_size(4) + dict_size(2)
    Then compressed data using sliding-window LZ with 1024-byte circular buffer.
    """
    if len(data) < 10:
        return data

    uncomp_size, comp_size, dict_size = struct.unpack_from('>IIH', data, 0)

    output = bytearray(uncomp_size)
    cbuffer = bytearray(CBUFFERSIZE)  # circular buffer
    insert_pos = 0
    bytes_out = 0
    pos = 10  # after LZ header
    flags = 0

    while bytes_out < uncomp_size and pos < len(data):
        flags >>= 1
        if not (flags & 0x100):
            if pos >= len(data):
                break
            flags = data[pos] | 0xFF00
            pos += 1

        if flags & 1:
            # Literal byte
            if pos >= len(data):
                break
            byte = data[pos]
            pos += 1
            output[bytes_out] = byte
            bytes_out += 1
            cbuffer[insert_pos] = byte
            insert_pos = (insert_pos + 1) & POS_MASK
        else:
            # Back-reference
            if pos + 1 >= len(data):
                break
            off_len = struct.unpack_from('>H', data, pos)[0]
            pos += 2
            string_len = (off_len >> POS_BITS) + MIN_STRING
            string_pos = (off_len + MAX_STRING) & POS_MASK

            for i in range(string_len):
                if bytes_out >= uncomp_size:
                    break
                byte = cbuffer[(string_pos + i) & POS_MASK]
                output[bytes_out] = byte
                bytes_out += 1
                cbuffer[insert_pos] = byte
                insert_pos = (insert_pos + 1) & POS_MASK

    return bytes(output[:bytes_out])


def draw_rle8(data, width, height, bytes_per_row):
    """Decode RLE8 per-scanline data into pixel buffer.

    Each row: uint16 BE row_byte_count, then RLE packets.
    Packet: code byte, then:
      code & 0x80: fill (code & 0x7F)+1 pixels with next byte
      else:        copy (code & 0x7F)+1 literal bytes
    """
    pixels = bytearray(width * height)
    pos = 0

    for y in range(height):
        if pos + 2 > len(data):
            break

        row_byte_count = struct.unpack_from('>H', data, pos)[0]
        row_start = pos + 2
        pos += 2

        dst = y * width
        remaining = width

        while remaining > 0 and pos < row_start + row_byte_count and pos < len(data):
            code = data[pos]
            pos += 1
            run_len = (code & 0x7F) + 1
            if run_len > remaining:
                run_len = remaining

            if code & 0x80:
                # Fill run
                if pos < len(data):
                    val = data[pos]
                    pos += 1
                    for i in range(run_len):
                        if dst + i < len(pixels):
                            pixels[dst + i] = val
                else:
                    break
            else:
                # Literal run
                for i in range(run_len):
                    if pos < len(data) and dst + i < len(pixels):
                        pixels[dst + i] = data[pos]
                        pos += 1
                    else:
                        break

            dst += run_len
            remaining -= run_len

        # Seek to exact end of row data
        pos = row_start + row_byte_count

    return pixels


def draw_raw(data, width, height, bytes_per_row):
    """Copy raw pixel data row by row."""
    pixels = bytearray(width * height)
    pos = 0

    for y in range(height):
        for x in range(width):
            if pos < len(data):
                pixels[y * width + x] = data[pos]
            pos += 1
        # Skip padding bytes
        pos += (bytes_per_row - width)

    return pixels


def decode_single_bitmap(data, external_palette=None):
    """Decode a single Mohawk tBMP (not compound shape)."""
    if len(data) < 8:
        return None

    raw_w, raw_h, raw_bpr, fmt = struct.unpack_from('>HHHH', data, 0)
    width = raw_w & 0x3FFF
    height = raw_h & 0x3FFF
    bpr = raw_bpr & 0x3FFE
    if bpr == 0:
        bpr = width

    bpp = fmt & 0x0007
    has_clut = bool(fmt & kBitmapHasCLUT)
    draw_method = fmt & 0x00F0
    pack_method = fmt & 0x0F00

    if width == 0 or height == 0 or width > 4096 or height > 65000:
        return None

    pos = 8
    palette = None

    # Read embedded palette if present
    if has_clut:
        if pos + 772 > len(data):
            pos = 8  # skip if not enough data
        else:
            table_size = struct.unpack_from('>H', data, pos)[0]
            rgb_bits = data[pos + 2]
            color_count = data[pos + 3]
            # Palette is BGR, 256 entries × 3 bytes
            pal_data = data[pos + 4:pos + 4 + 768]
            palette = [0] * 768
            for i in range(min(256, len(pal_data) // 3)):
                b, g, r = pal_data[i*3], pal_data[i*3+1], pal_data[i*3+2]
                palette[i*3] = r
                palette[i*3+1] = g
                palette[i*3+2] = b
            pos += 772

    pixel_data = data[pos:]

    # Stage 1: Unpack (LZ decompression)
    if pack_method == kPackLZ:
        pixel_data = decompress_lz(pixel_data)
    # else: use as-is

    # Stage 2: Draw (RLE8 or raw)
    if draw_method == kDrawRLE8:
        pixels = draw_rle8(pixel_data, width, height, bpr)
    else:
        pixels = draw_raw(pixel_data, width, height, bpr)

    # Use external palette if no embedded one
    if palette is None:
        palette = external_palette

    return create_image(pixels, width, height, palette)


def decode_compound_shape(data, external_palette=None):
    """Decode a compound shape (multiple sub-images in one tBMP).

    The outer header's 'width' field is repurposed as sub-image count.
    After LZ decompression, data contains:
      uint32[count] offsets, then sub-image data.
    """
    if len(data) < 8:
        return None

    raw_w, raw_h, raw_bpr, fmt = struct.unpack_from('>HHHH', data, 0)
    count = raw_w & 0x3FFF  # repurposed as sub-image count
    total_h = raw_h & 0x3FFF
    bpr = raw_bpr & 0x3FFE
    pack_method = fmt & 0x0F00

    if count == 0 or count > 2000:
        return None

    pos = 8
    has_clut = bool(fmt & kBitmapHasCLUT)
    outer_palette = None
    if has_clut and pos + 772 <= len(data):
        pal_data = data[pos + 4:pos + 4 + 768]
        outer_palette = [0] * 768
        for i in range(min(256, len(pal_data) // 3)):
            b, g, r = pal_data[i*3], pal_data[i*3+1], pal_data[i*3+2]
            outer_palette[i*3] = r
            outer_palette[i*3+1] = g
            outer_palette[i*3+2] = b
        pos += 772

    pixel_data = data[pos:]

    # Stage 1: LZ decompress
    if pack_method == kPackLZ:
        pixel_data = decompress_lz(pixel_data)

    if len(pixel_data) < count * 4:
        return None

    # Read offset table
    offsets = []
    for i in range(count):
        off = struct.unpack_from('>I', pixel_data, i * 4)[0]
        offsets.append(off)

    pal = outer_palette or external_palette

    # Decode first N sub-images
    max_decode = min(25, count)
    sub_images = []
    for i in range(max_decode):
        # Offset is relative; adjust by -8 for the outer header already consumed
        start = offsets[i] - 8 if offsets[i] >= 8 else offsets[i]
        if i + 1 < len(offsets):
            end = offsets[i + 1] - 8 if offsets[i+1] >= 8 else offsets[i+1]
        else:
            end = len(pixel_data)

        if start < 0 or start >= len(pixel_data):
            continue

        sub_data = pixel_data[start:end]
        try:
            img = decode_single_bitmap(sub_data, pal)
            if img:
                sub_images.append(img.convert('RGB'))
        except Exception:
            pass

    if not sub_images:
        return None

    # Arrange in a grid
    cols = min(5, len(sub_images))
    rows = (len(sub_images) + cols - 1) // cols
    max_w = max(img.size[0] for img in sub_images)
    max_h = max(img.size[1] for img in sub_images)

    grid = Image.new('RGB', (cols * (max_w + 2), rows * (max_h + 2)), (40, 40, 40))
    for i, img in enumerate(sub_images):
        r, c = divmod(i, cols)
        grid.paste(img, (c * (max_w + 2), r * (max_h + 2)))

    return grid


def decode_bitmap(data, palette=None):
    """Decode a Mohawk tBMP resource — auto-detects compound shapes."""
    if len(data) < 8:
        return None

    raw_w, raw_h, raw_bpr, fmt = struct.unpack_from('>HHHH', data, 0)
    width = raw_w & 0x3FFF
    height = raw_h & 0x3FFF

    # Heuristic: compound shape if width > ~500 and it's a reasonable count
    if width > 100 and height > 100 and width * height > 1000000:
        # Probably a normal large bitmap
        return decode_single_bitmap(data, palette)
    elif width > 100 and width < 2000:
        # Check if this looks like a compound shape (width = count of sub-images)
        # Compound shapes have very large "height" relative to "width"
        if height > width * 5:
            return decode_compound_shape(data, palette)
        else:
            return decode_single_bitmap(data, palette)
    else:
        return decode_single_bitmap(data, palette)


def create_image(pixels, width, height, palette=None):
    """Create a PIL Image from 8bpp pixel data."""
    if width <= 0 or height <= 0:
        return None

    img = Image.new('P', (width, height))
    img.putdata(list(pixels[:width * height]))

    if palette:
        img.putpalette(palette)
    else:
        pal = []
        for i in range(256):
            pal.extend([i, i, i])
        img.putpalette(pal)

    return img


def load_palette_from_tpal(data):
    """Parse a Mohawk tPAL resource.

    Format: uint16 start_index, uint16 color_count,
    then color_count × {R, G, B, flags} (4 bytes each).
    """
    if len(data) < 4:
        return None

    start_idx, color_count = struct.unpack_from('>HH', data, 0)
    if color_count == 0 or color_count > 256 or len(data) < 4 + color_count * 4:
        return None

    palette = [0] * 768
    for i in range(color_count):
        idx = start_idx + i
        if idx >= 256:
            break
        off = 4 + i * 4
        palette[idx * 3] = data[off]
        palette[idx * 3 + 1] = data[off + 1]
        palette[idx * 3 + 2] = data[off + 2]

    return palette


def load_palette(archive, pal_id=None):
    """Load a tPAL palette from a Mohawk archive."""
    for tag in archive.types:
        tag_str = tag.decode('ascii', errors='replace').replace('\x00', '')
        if tag_str == 'tPAL':
            resources = archive.types[tag]
            if not resources:
                continue
            res = resources[0]
            if pal_id is not None:
                for r in resources:
                    if r['id'] == pal_id:
                        res = r
                        break
            data = archive.data[res['offset']:res['offset'] + res['size']]
            return load_palette_from_tpal(data)
    return None


def extract_bitmaps(archive, output_dir, palette=None):
    """Extract all tBMP resources from an archive as PNG files."""
    output_dir = Path(output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)

    count = 0
    for tag in archive.types:
        tag_str = tag.decode('ascii', errors='replace').replace('\x00', '')
        if tag_str == 'tBMP':
            for res in archive.types[tag]:
                data = archive.data[res['offset']:res['offset'] + res['size']]
                try:
                    img = decode_bitmap(data, palette)
                    if img:
                        filename = f"tBMP_{res['id']:04d}.png"
                        if img.mode == 'P':
                            img = img.convert('RGB')
                        img.save(output_dir / filename)
                        print(f"  Saved {filename} ({img.size[0]}x{img.size[1]})")
                        count += 1
                    else:
                        raw_w, raw_h, raw_bpr, fmt = struct.unpack_from('>HHHH', data, 0)
                        print(f"  tBMP_{res['id']}: Could not decode "
                              f"(w={raw_w & 0x3FFF}, h={raw_h & 0x3FFF}, "
                              f"fmt=0x{fmt:04x}, {res['size']} bytes)")
                except Exception as e:
                    print(f"  tBMP_{res['id']}: Error: {e}")

    return count


def main():
    from mohawk_parser import MohawkArchive

    if len(sys.argv) < 2:
        print("Usage: bitmap_decoder.py <MHK_FILE> [OUTPUT_DIR]")
        sys.exit(1)

    mhk_path = sys.argv[1]
    output_dir = sys.argv[2] if len(sys.argv) > 2 else f'bitmaps/{Path(mhk_path).stem}'

    archive = MohawkArchive(mhk_path)
    palette = load_palette(archive)

    if not palette:
        maze_path = Path(mhk_path).parent / 'MAZE2.MHK'
        if maze_path.exists():
            maze_archive = MohawkArchive(str(maze_path))
            palette = load_palette(maze_archive)

    print(f"Extracting bitmaps from {Path(mhk_path).name}...")
    if palette:
        ncolors = sum(1 for i in range(0, 768, 3) if any(palette[i:i+3]))
        print(f"  Using palette ({ncolors} non-black colors)")
    else:
        print(f"  No palette found, using grayscale")

    count = extract_bitmaps(archive, output_dir, palette)
    print(f"\nExtracted {count} bitmaps to {output_dir}/")


if __name__ == '__main__':
    main()
