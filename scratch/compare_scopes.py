"""Compare the slowest scopes in the first vs last quarter of a perf report."""
import json
import re
import sys
from collections import defaultdict

path = sys.argv[1]
d = json.load(open(path))
s = d["facts"]["samples"]


def scopes(line):
    m = re.search(r"slowest: (.*?)(?: floraStages=| ambientInst=|$)", line)
    out = {}
    if m:
        for sm in re.finditer(r"([A-Za-z][\w.]*)=([\d.]+)", m.group(1)):
            out[sm.group(1)] = float(sm.group(2))
    return out


n = len(s)
head = s[: n // 4]
tail = s[-n // 4 :]


def agg(part):
    a = defaultdict(list)
    for line in part:
        for k, v in scopes(line).items():
            a[k].append(v)
    return {k: (sum(v) / len(v), len(v)) for k, v in a.items()}


h = agg(head)
t = agg(tail)
print(f"{'scope':<34} {'headMean':>9} {'headSecs':>9} {'tailMean':>9} {'tailSecs':>9}")
for k in sorted(set(h) | set(t), key=lambda k: -t.get(k, (0, 0))[0]):
    hm, hs = h.get(k, (0, 0))
    tm, ts = t.get(k, (0, 0))
    print(f"{k:<34} {hm:>9.1f} {hs:>9} {tm:>9.1f} {ts:>9}")
