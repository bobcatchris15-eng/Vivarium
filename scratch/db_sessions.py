import sqlite3, os, json, datetime

db = os.path.join(os.environ["USERPROFILE"], ".local", "share", "opencode", "opencode.db")
con = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
cur = con.cursor()


def ts(v):
    if not v:
        return "?"
    try:
        return datetime.datetime.fromtimestamp(v / 1000).strftime("%m-%d %H:%M")
    except Exception:
        return str(v)


cur.execute("select id, directory, title, time_created, time_updated, time_archived from session order by time_updated desc limit 12")
print(f"{'session':<26} {'updated':<12} {'dir':<32} title")
for sid, d, title, tc, tu, ta in cur.fetchall():
    print(f"{sid:<26} {ts(tu):<12} {str(d)[-30:]:<32} {str(title)[:60]}")

print()
print("=== most recent user prompts across sessions ===")
cur.execute(
    "select m.session_id, m.time_created, p.data from message m join part p on p.message_id=m.id "
    "where p.data like '%\"type\":\"text\"%' order by m.time_created desc limit 25"
)
seen = set()
for sid, tc, data in cur.fetchall():
    try:
        d = json.loads(data)
    except Exception:
        continue
    txt = (d.get("text") or "").strip().replace("\n", " ")
    if not txt or d.get("type") not in ("text",):
        continue
    if txt in seen:
        continue
    seen.add(txt)
    print(f"[{ts(tc)}] {sid[:14]}: {txt[:220]}")
