import sqlite3, os, json, datetime, sys

db = os.path.join(os.environ["USERPROFILE"], ".local", "share", "opencode", "opencode.db")
con = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
cur = con.cursor()

sid = sys.argv[1]
limit = int(sys.argv[2]) if len(sys.argv) > 2 else 30


def ts(v):
    try:
        return datetime.datetime.fromtimestamp(v / 1000).strftime("%H:%M:%S")
    except Exception:
        return str(v)


cur.execute("select id, type, seq, time_created, data from session_message where session_id=? order by seq desc limit ?", (sid, limit))
rows = cur.fetchall()
for mid, mtype, seq, tc, data in reversed(rows):
    try:
        d = json.loads(data)
    except Exception:
        d = {}
    print(f"--- {ts(tc)} seq={seq} type={mtype}")
    # pull any text out of the payload
    def walk(o, depth=0):
        if isinstance(o, dict):
            if o.get("type") in ("text", "reasoning") and isinstance(o.get("text"), str):
                t = o["text"].strip()
                if t:
                    print(("  [reasoning] " if o.get("type") == "reasoning" else "  ") + t.replace("\n", "\n  ")[:1500])
            for k, v in o.items():
                if k != "text":
                    walk(v, depth + 1)
        elif isinstance(o, list):
            for v in o:
                walk(v, depth + 1)
    walk(d)
