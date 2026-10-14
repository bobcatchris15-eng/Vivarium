"""Read native RenderDoc XML chunk metadata; this is neither GPU timing nor aesthetic proof."""
import collections,json,sys,xml.etree.ElementTree as ET
from pathlib import Path
p=Path(sys.argv[1]);root=ET.parse(p).getroot()
counts=collections.Counter(c.get('name','') for c in root.iter('chunk'))
draws={k:v for k,v in counts.items() if 'Draw' in k or 'Dispatch' in k}
if not sum(draws.values()):raise SystemExit('Capture has no draw/dispatch chunks')
r={'capture_xml':str(p.resolve()),'api':root.findtext('header/driver'),'chunks':sum(counts.values()),'draw_dispatch_chunks':draws,
   'scope':'native recorded API command metadata; not per-frame timing or semantic visual acceptance'}
p.with_suffix('.inspection.json').write_text(json.dumps(r,indent=2));print(json.dumps(r,indent=2))
