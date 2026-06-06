#!/usr/bin/env python3
"""Binary diff of extracted v1 and v2 MHK resources."""
import hashlib
from pathlib import Path
from collections import defaultdict

V1 = Path("extracted")
V2 = Path("v2_extracted")

def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()

archives = sorted({p.name for p in V1.iterdir() if p.is_dir()} &
                  {p.name for p in V2.iterdir() if p.is_dir()})

print(f"{'Archive':<12} {'Type':<6} {'v1':>6} {'v2':>6} {'same':>6} {'diff':>6} {'onlyV1':>7} {'onlyV2':>7}")
print("-" * 70)

grand_same = grand_diff = grand_new = grand_gone = 0
diff_details = defaultdict(list)

for arch in archives:
    types = sorted({p.name for p in (V1/arch).iterdir()} |
                   {p.name for p in (V2/arch).iterdir()})
    for t in types:
        v1_files = {p.name for p in (V1/arch/t).iterdir()} if (V1/arch/t).exists() else set()
        v2_files = {p.name for p in (V2/arch/t).iterdir()} if (V2/arch/t).exists() else set()
        common = v1_files & v2_files
        only_v1 = v1_files - v2_files
        only_v2 = v2_files - v1_files
        same = diff = 0
        for fn in common:
            if sha(V1/arch/t/fn) == sha(V2/arch/t/fn):
                same += 1
            else:
                diff += 1
                diff_details[(arch, t)].append(fn)
        grand_same += same; grand_diff += diff
        grand_new += len(only_v2); grand_gone += len(only_v1)
        print(f"{arch:<12} {t:<6} {len(v1_files):>6} {len(v2_files):>6} {same:>6} {diff:>6} {len(only_v1):>7} {len(only_v2):>7}")

print("-" * 70)
print(f"TOTAL: identical={grand_same}  differing={grand_diff}  new_in_v2={grand_new}  removed_v1={grand_gone}")

# Spot-check differing resources: show first 3 per category that differ
print("\n=== CHANGED RESOURCES (binary different between v1 and v2) ===")
for (arch, t), files in sorted(diff_details.items())[:20]:
    print(f"  {arch}/{t}: {len(files)} differ — e.g. {sorted(files)[:3]}")
