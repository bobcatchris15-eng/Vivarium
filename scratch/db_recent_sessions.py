import sqlite3, os, json, datetime

db = os.path.join(os.environ["USERPROFILE"], ".local", "share", "opencode", "opencode.db")
con = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
cur = con.cursor()


def ts(v):
    try:
        return datetime.datetime.fromtimestamp(v / 1000).strftime("%m-%d %H:%M")
    except Exception:
        return str(v)


cutoff = (datetime.datetime.now() - datetime.timedelta(hours=6)).timestamp() * 1000
cur.execute(
    "select id, directory, title, time_created, time_updated, agent, model from session_v2 "
    "where time_updated > ? order by time_updated desc", (cutoff,))
print(f"{'session':<26} {'created':<12} {'updated':<12} {'dir':<26} title")
for sid, d, title, tc, tu, agent, model in cur.fetchall():
    print(f"{sid:<26} {ts(tc):<12} {ts(tu):<12} {str(d)[-24:]:<26} {str(title)[:70]}")
