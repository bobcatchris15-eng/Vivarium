"""Summarize matched PNGs; display-decoded values are comparative, not radiometric calibration."""
import json
import sys
from pathlib import Path
import numpy as np
from PIL import Image
run = Path(sys.argv[1])
report = json.loads((run / "captures/habitat_light_review.json").read_text())
rows = []
for capture in report["captures"]:
    path = Path(capture["path"])
    rgb = np.asarray(Image.open(path).convert("RGB"), dtype=np.float64) / 255
    linear = np.where(rgb <= .04045, rgb / 12.92, ((rgb + .055) / 1.055) ** 2.4)
    y = linear @ np.array([.2126, .7152, .0722])
    # Whole image includes the deliberately black background; it is not an object-only ROI.
    rows.append({"name":capture["name"], "display_decoded_luminance_mean":float(y.mean()),
        "luminance_p10":float(np.quantile(y,.1)), "luminance_p90":float(np.quantile(y,.9)),
        "pixel_fraction_any_channel_250":float((rgb.max(axis=2) >= 250/255).mean())})
result = {"scope":"matched whole-frame comparative statistics, includes black void", "captures":rows}
(run / "lighting-analysis.json").write_text(json.dumps(result,indent=2))
for row in rows:
    print(row["name"], round(row["display_decoded_luminance_mean"],5), row["pixel_fraction_any_channel_250"])
