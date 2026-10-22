"""Analyze a Vivarium perf_report.json sample series.

Usage: python scratch/analyze_perf.py <perf_report.json> [bucket_seconds]
"""
import json
import re
import sys
from collections import defaultdict

path = sys.argv[1]
bucket = int(sys.argv[2]) if len(sys.argv) > 2 else 60
report = json.load(open(path))
samples = report["facts"]["samples"]
print(f"file: {path}")
print(f"samples: {len(samples)}")


def parse(line):
    out = {}
    for key, pat in [
        ("t", r"t=\s*(\d+)s"),
        ("fps", r"fps=\s*(\d+)"),
        ("worst", r"worstFrame=\s*([\d.]+)ms"),
        ("process", r"process=\s*([\d.]+)ms"),
        ("simLast", r"simLast=\s*([\d.]+)ms"),
        ("backlog", r"backlog=\s*(\d+)s"),
        ("flora", r"flora=(\d+)"),
        ("fauna", r"fauna=(\d+)"),
        ("prims", r"prims=(\d+)"),
        ("draws", r"draws=(\d+)"),
        ("floraVis", r"floraVis=(\d+)"),
        ("mem", r"mem=(\d+)MB"),
        ("vram", r"vram=\s*(\d+)MB"),
        ("ws", r"ws=\s*(\d+)MB"),
        ("gc0", r"gc0=(\d+)"),
        ("gc1", r"gc1=(\d+)"),
        ("gc2", r"gc2=(\d+)"),
        ("ticks", r"ticks/s=\s*(\d+)"),
        ("plasmo", r"plasmo_cells=(\d+)"),
    ]:
        m = re.search(pat, line)
        if m:
            out[key] = float(m.group(1))
    scopes = {}
    m = re.search(r"slowest: (.*?)(?: floraStages=| ambientInst=|$)", line)
    if m:
        for sm in re.finditer(r"([A-Za-z][\w.]*)=([\d.]+)", m.group(1)):
            scopes[sm.group(1)] = float(sm.group(2))
    out["scopes"] = scopes
    busy = {}
    m = re.search(r"busy: (.*)$", line)
    if m:
        for sm in re.finditer(r"([A-Za-z][\w.]*)=([\d.]+)/(\d+)", m.group(1)):
            busy[sm.group(1)] = (float(sm.group(2)), int(sm.group(3)))
    out["busy"] = busy
    return out


parsed = [parse(s) for s in samples]
print(f"parsed {len(parsed)}")
have_busy = any(p["busy"] for p in parsed)

buckets = defaultdict(list)
for p in parsed:
    buckets[int(p["t"] // bucket)].append(p)

print()
hdr = (f"{'bucket':>8} {'n':>4} {'fps':>6} {'proc':>7} {'sim':>6} {'worst':>7} {'backlog':>8} "
       f"{'flora':>6} {'fauna':>6} {'prims':>9} {'vram':>7} {'ws':>7}")
print(hdr)
for b in sorted(buckets):
    ps = buckets[b]

    def avg(k):
        vals = [p[k] for p in ps if k in p]
        return sum(vals) / len(vals) if vals else 0

    print(f"{b*bucket:>8} {len(ps):>4} {avg('fps'):>6.1f} {avg('process'):>7.1f} {avg('simLast'):>6.2f} "
          f"{avg('worst'):>7.1f} {avg('backlog'):>8.0f} {avg('flora'):>6.0f} {avg('fauna'):>6.0f} "
          f"{avg('prims'):>9.0f} {avg('vram'):>7.0f} {avg('ws'):>7.0f}")

if have_busy:
    # growth: mean busy ms/s in first fifth vs last fifth of the run
    n = len(parsed)
    head = [p for p in parsed[: n // 5] if p["busy"]]
    tail = [p for p in parsed[-(n // 5):] if p["busy"]]
    agg = defaultdict(lambda: [0.0, 0.0, 0.0, 0.0])  # head total, head calls, tail total, tail calls
    for p in head:
        for k, (ms, calls) in p["busy"].items():
            agg[k][0] += ms
            agg[k][1] += calls
    for p in tail:
        for k, (ms, calls) in p["busy"].items():
            agg[k][2] += ms
            agg[k][3] += calls
    nh = max(1, len(head))
    nt = max(1, len(tail))
    print()
    print(f"BUSY (total ms of CPU per second) head(first {nh}s) vs tail(last {nt}s)")
    print(f"{'scope':<36} {'head ms/s':>10} {'tail ms/s':>10} {'growth':>8} {'head c/s':>9} {'tail c/s':>9}")
    rows = []
    for k, (ht, hc, tt, tc) in agg.items():
        h = ht / nh
        t = tt / nt
        g = (t / h) if h > 0.01 else (float("inf") if t > 0.01 else 1.0)
        rows.append((t, k, h, t, g, hc / nh, tc / nt))
    for _, k, h, t, g, hc, tc in sorted(rows, reverse=True):
        gs = "inf" if g == float("inf") else f"{g:6.2f}x"
        print(f"{k:<36} {h:>10.2f} {t:>10.2f} {gs:>8} {hc:>9.1f} {tc:>9.1f}")

print()
print("scope frequency in 'slowest' list:")
freq = defaultdict(list)
for p in parsed:
    for k, v in p["scopes"].items():
        freq[k].append(v)
print(f"{'scope':<36} {'secs':>6} {'frac':>6} {'mean':>8} {'max':>8}")
for k, vals in sorted(freq.items(), key=lambda kv: -len(kv[1])):
    print(f"{k:<36} {len(vals):>6} {len(vals)/len(parsed):>6.2f} {sum(vals)/len(vals):>8.2f} {max(vals):>8.1f}")
