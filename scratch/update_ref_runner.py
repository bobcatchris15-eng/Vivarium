file_path = "game/App/SmokeRunner.Reference.cs"

with open(file_path, "r", encoding="utf-8", newline="") as f:
    content = f.read()

target = "var layerId = sp.Mat != null ? CoverageLayerId.Mat : CoverageLayerId.Crust;"
replacement = "var layerId = sp.Mat != null ? CoverageLayerId.Mat : sp.Lichen != null ? CoverageLayerId.Crust : CoverageLayerId.Plasmodium;"

assert content.count(target) == 2, f"Expected 2 occurrences of target, found {content.count(target)}"
content = content.replace(target, replacement)

with open(file_path, "w", encoding="utf-8", newline="") as f:
    f.write(content)

print(f"Updated {file_path}")
