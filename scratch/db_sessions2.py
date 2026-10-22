import sqlite3, os, json, datetime

db = os.path.join(os.environ["USERPROFILE"], ".local", "share", "opencode", "opencode.db")
con = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
cur = con.cursor()


def ts(v):
    if not v:
        return "?"
    try:
        return datetime.datetime.fromtimestamp(v / 1000).strftime("%m-%d %H:%M:%S")
    except Exception:
        return str(v)


for t in ("session_v2", "session_message", "session_inbox", "session_share"):
    cur.execute(f"pragma table_info({t})")
    print(t, [r[1] for r in cur.fetchall()])

print()
cur.execute("select * from session_v2 order by rowid desc limit 6")
cols = [d[0] for d in cur.description]
for row in cur.fetchall():
    d = dict(zip(cols, row))
    print({k: (str(v)[:120] if v is not None else None) for k, v in d.items() if k in ("id", "title", "directory", "updated", "created", "time_created", "time_updated")})
