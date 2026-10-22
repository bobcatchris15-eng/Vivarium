"""Track the plasmodium phase timings over a perf report."""
import json
import re
import sys

path = sys.argv[1]
d = json.load(open(path))
s = d["facts"]["samples"]
keys = ["plasmo_cells", "net", "attr", "front", "phase", "relabel", "lifecycle", "flora", "fauna"]
rows = []
for line in s:
    row = {}
    for k in keys:
        m = re.search(rf"\b{k}=(-?[\d.]+)", line)
        if m:
            row[k] = float(m.group(1))
    m = re.search(r"t=\s*(\d+)s", line)
    if m:
        row["t"] = float(m.group(1))
    rows.append(row)
n = len(rows)
print(f"{'phase':>12} " + " ".join(f"{'b'+str(b):>8}" for b in range(5)))
for k in keys:
    if k == "t":
        continue
    cells = []
    for b in range(5):
        part = rows[b * n // 5 : (b + 1) * n // 5]
        vals = [r[k] for r in part if k in r]
        cells.append(sum(vals) / len(vals) if vals else 0)
    print(f"{k:>12} " + " ".join(f"{c:>8.2f}" for c in cells))
