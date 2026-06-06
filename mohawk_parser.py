#!/usr/bin/env python3
"""
Mohawk (.MHK) archive parser for Zoombinis: Logical Journey.

The Mohawk engine was developed by Brøderbund and used in Myst, Riven,
and Zoombinis. MHK files are resource archives containing images (tBMP),
sounds (tWAV), scripts, and other game data.

Format reference: ScummVM Mohawk engine source code.
"""

import struct
import sys
import os
from pathlib import Path
from collections import defaultdict


class MohawkArchive:
    """Parser for Mohawk .MHK resource archives."""

    # Known resource type tags
    TYPE_NAMES = {
        b'tBMP': 'Bitmap',
        b'tWAV': 'Wave Audio',
        b'tMID': 'MIDI',
        b'tPAL': 'Palette',
        b'tSCR': 'Script',
        b'tCOD': 'Code',
        b'NAME': 'Name Table',
        b'BLST': 'Button List',
        b'CLRC': 'Color Cursor',
        b'CTS ': 'Cursor Hotspot',
        b'FLST': 'Film List',
        b'HSPT': 'Hotspot',
        b'MSND': 'Mohawk Sound',
        b'PLST': 'Picture List',
        b'RMAP': 'Resource Map',
        b'RLST': 'Resource List',
        b'SLST': 'Sound List',
        b'TBMP': 'Thumbnail Bitmap',
        b'TMID': 'Thumbnail MIDI',
        b'VERS': 'Version',
        b'VIEW': 'View',
        b'WDIB': 'Windows DIB',
        b'tCUR': 'Cursor',
        b'tICN': 'Icon',
        b'tSTR': 'String',
        b'tLST': 'List',
        b'tSEQ': 'Sequence',
        b'tSPR': 'Sprite',
        b'tANM': 'Animation',
        b'PICT': 'Picture',
    }

    def __init__(self, filepath):
        self.filepath = filepath
        self.filename = os.path.basename(filepath)
        self.types = {}       # {type_tag: [ResourceEntry, ...]}
        self.file_table = []  # [{offset, size, flags}, ...]
        self._parse()

    def _parse(self):
        with open(self.filepath, 'rb') as f:
            self.data = f.read()

        # MHWK header
        magic = self.data[0:4]
        if magic != b'MHWK':
            raise ValueError(f"Not a Mohawk file: {magic}")

        file_size = struct.unpack_from('>I', self.data, 4)[0]

        # RSRC header at offset 8
        rsrc_magic = self.data[8:12]
        if rsrc_magic != b'RSRC':
            raise ValueError(f"Missing RSRC header: {rsrc_magic}")

        (version, compaction, rsrc_size, abs_offset,
         file_table_offset, file_table_size) = struct.unpack_from(
            '>HHIIHH', self.data, 12
        )

        self.version = version
        self.abs_offset = abs_offset

        # Parse file table (at abs_offset + file_table_offset)
        # Entry format (10 bytes): offset(4) + size_lo(2) + size_hi(1) + flags(1) + unknown(2)
        ft_pos = abs_offset + file_table_offset
        num_files = struct.unpack_from('>I', self.data, ft_pos)[0]
        ft_pos += 4

        self.file_table = []
        for i in range(num_files):
            data_offset, size_lo, size_hi, flags, _unknown = struct.unpack_from(
                '>IHBBH', self.data, ft_pos
            )
            size = (size_hi << 16) | size_lo
            self.file_table.append({
                'offset': data_offset,
                'size': size,
                'flags': flags,
            })
            ft_pos += 10

        # Parse resource type directory (at abs_offset)
        dir_pos = abs_offset
        name_table_offset, num_types = struct.unpack_from('>HH', self.data, dir_pos)
        dir_pos += 4

        self.types = {}
        for i in range(num_types):
            type_tag = self.data[dir_pos:dir_pos+4]
            res_table_offset, name_table_off = struct.unpack_from('>HH', self.data, dir_pos+4)
            dir_pos += 8

            # Parse resource table for this type
            rt_pos = abs_offset + res_table_offset
            num_resources = struct.unpack_from('>H', self.data, rt_pos)[0]
            rt_pos += 2

            resources = []
            for j in range(num_resources):
                res_id, file_index = struct.unpack_from('>HH', self.data, rt_pos)
                rt_pos += 4
                # file_index is 1-based
                if 1 <= file_index <= len(self.file_table):
                    ft_entry = self.file_table[file_index - 1]
                    resources.append({
                        'id': res_id,
                        'file_index': file_index,
                        'offset': ft_entry['offset'],
                        'size': ft_entry['size'],
                        'flags': ft_entry['flags'],
                    })
                else:
                    resources.append({
                        'id': res_id,
                        'file_index': file_index,
                        'offset': 0,
                        'size': 0,
                        'flags': 0,
                    })

            self.types[type_tag] = resources

    def get_resource_data(self, type_tag, res_id):
        """Get raw resource data by type and ID."""
        if type_tag not in self.types:
            return None
        for res in self.types[type_tag]:
            if res['id'] == res_id:
                offset = res['offset']
                size = res['size']
                return self.data[offset:offset+size]
        return None

    def list_resources(self):
        """Print a formatted list of all resources."""
        total_resources = sum(len(v) for v in self.types.values())
        print(f"\n{'='*70}")
        print(f"  {self.filename}")
        print(f"  Version: {self.version}, File table entries: {len(self.file_table)}")
        print(f"  Total resource types: {len(self.types)}, Total resources: {total_resources}")
        print(f"{'='*70}")

        for type_tag, resources in sorted(self.types.items()):
            type_name = self.TYPE_NAMES.get(type_tag, 'Unknown')
            tag_str = type_tag.decode('ascii', errors='replace')
            print(f"\n  [{tag_str}] {type_name} ({len(resources)} resources)")
            print(f"  {'ID':>6}  {'FileIdx':>7}  {'Offset':>10}  {'Size':>10}  {'Flags':>5}")
            print(f"  {'-'*6}  {'-'*7}  {'-'*10}  {'-'*10}  {'-'*5}")
            for res in sorted(resources, key=lambda r: r['id']):
                print(f"  {res['id']:>6}  {res['file_index']:>7}  "
                      f"0x{res['offset']:08x}  {res['size']:>10}  {res['flags']:>5}")

    def extract_all(self, output_dir):
        """Extract all resources to files."""
        output_dir = Path(output_dir)
        output_dir.mkdir(parents=True, exist_ok=True)

        count = 0
        for type_tag, resources in self.types.items():
            tag_str = type_tag.decode('ascii', errors='replace').strip().strip('\x00').replace('\x00', '')
            type_dir = output_dir / tag_str
            type_dir.mkdir(exist_ok=True)

            for res in resources:
                data = self.data[res['offset']:res['offset']+res['size']]
                if data:
                    filename = f"{tag_str}_{res['id']:04d}.bin"
                    (type_dir / filename).write_bytes(data)
                    count += 1

        print(f"  Extracted {count} resources to {output_dir}")
        return count


def analyze_all_mhk(data_dir, output_dir=None):
    """Analyze all MHK files in a directory."""
    data_path = Path(data_dir)
    mhk_files = sorted(data_path.glob('*.MHK'))

    if not mhk_files:
        print(f"No MHK files found in {data_dir}")
        return

    print(f"Found {len(mhk_files)} MHK archives\n")

    all_types = defaultdict(int)

    for mhk_path in mhk_files:
        try:
            archive = MohawkArchive(str(mhk_path))
            archive.list_resources()

            for type_tag, resources in archive.types.items():
                all_types[type_tag] += len(resources)

            if output_dir:
                archive_out = Path(output_dir) / mhk_path.stem
                archive.extract_all(archive_out)

        except Exception as e:
            print(f"\n  ERROR parsing {mhk_path.name}: {e}")

    print(f"\n{'='*70}")
    print(f"  SUMMARY: Resource types across all archives")
    print(f"{'='*70}")
    for type_tag, count in sorted(all_types.items()):
        type_name = MohawkArchive.TYPE_NAMES.get(type_tag, 'Unknown')
        tag_str = type_tag.decode('ascii', errors='replace')
        print(f"  [{tag_str}] {type_name}: {count} resources")


if __name__ == '__main__':
    if len(sys.argv) < 2:
        print("Usage: mohawk_parser.py <MHK_FILE_OR_DIR> [--extract <OUTPUT_DIR>]")
        sys.exit(1)

    target = sys.argv[1]
    extract_dir = None
    if '--extract' in sys.argv:
        idx = sys.argv.index('--extract')
        extract_dir = sys.argv[idx + 1] if idx + 1 < len(sys.argv) else './extracted'

    if os.path.isdir(target):
        analyze_all_mhk(target, extract_dir)
    else:
        archive = MohawkArchive(target)
        archive.list_resources()
        if extract_dir:
            archive.extract_all(extract_dir)
