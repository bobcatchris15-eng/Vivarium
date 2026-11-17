"""Build the post-review roster from the handoff's stable review order."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ORDER = ROOT / 'docs/representation-rebuild/species-review-2026-11-17/review-order.json'
OUT = ROOT / 'docs/representation-rebuild/post-review-2026-11-17/manifest.json'
SELECTED = {
    1: 'plumegrass bannerreed coalspike banksorrow hollowcrown witchberry',
    2: 'antlerlace_lichen ashfingers_lichen',
    3: 'blackneedle bogfringe bogglass_moss',
    4: 'burlmantle burrflag butterglint',
    5: 'capstar_moss carrion_bell cloudtuft coinrunner',
    6: 'cradlecup cragparasol duskbristle',
    7: 'duskpuff emberbracken embercrown embercrust_lichen embercup emberfan_fungus',
    8: 'fencomb fenfoam fenforge floodlace_moss',
    9: 'frosttussock ghostbonnet glassfinger glasslily glassrush',
    10: 'glowbriar greywatch hallowsting heddlelace hookthicket',
    11: 'lacemantle lanternbrush',
    12: 'mirepocket mirespire',
    13: 'moonbark moonshell_lichen nutglade',
    14: 'pearl_cushion_moss pinequilt_moss pitcher_plant pondstars',
    15: 'quillfeather_moss quillsedge rainshield reekhorn ringreed',
    16: 'ruffle_lichen rustbellow_moss shadebell',
    17: 'silverfelt snaptrap',
    18: 'sootband splitcrown starbulb starmoss starsplit',
    19: 'stoneknot_moss streamribbon swanfrond',
    20: 'tankbloom thornmantle tidebell trifold',
    21: 'veilblade veilfern velvetweave_moss widowspire wispthread_moss',
}


def main():
    expected = {(sheet, name) for sheet, names in SELECTED.items() for name in names.split()}
    source = json.loads(ORDER.read_text(encoding='utf-8'))['species']
    found = [s for s in source if (s['sheet'], s['id']) in expected]
    if len(expected) != 79 or len(found) != 79 or len({s['id'] for s in found}) != 79:
        raise ValueError('Roster does not uniquely match all 79 requested species')
    if sum(s['sheet'] <= 6 for s in found) != 21:
        raise ValueError('Expected 21 earlier and 58 later corrections')
    records = [dict(number=i, id=s['id'], name=s['name'], original_sheet=s['sheet'],
                    sheet=(i - 1) // 6 + 1, position=(i - 1) % 6 + 1)
               for i, s in enumerate(found, 1)]
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps({'species_count': 79, 'species': records}, indent=2) + '\n', encoding='utf-8')
    print(f'{len(records)} species; 14 sheets; {OUT}')


if __name__ == '__main__':
    main()
