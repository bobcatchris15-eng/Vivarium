import json

with open('build/investigation-20261025/effect-sweep/render_report.json') as f:
    report = json.load(f)

# Let's see what information is in report
print("Keys:", list(report.keys()))
