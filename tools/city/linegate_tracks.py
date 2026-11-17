# linegate_tracks.py - which candidate race tracks run through failing places of the line gate.
#
#   python -I tools/city/linegate_tracks.py --csv city_linegate.csv --tracks candidates.json [--md out.md]
#
# A course is a chain of [edge, dir]. Every failure of the CSV on one of its edges counts (absorbed ones
# excluded); places are the gate's own (failures of one class within 25 m).
import sys, csv, json, argparse

ORDER = ['SJOG', 'SHIFT', 'KINK', 'WIDTH', 'CONT', 'SURFACE', 'JUNCTION', 'MEDIAN', 'STEP', 'BARRIER', 'BRIDGE']
ap = argparse.ArgumentParser()
ap.add_argument('--csv', required=True); ap.add_argument('--tracks', required=True); ap.add_argument('--md')
a = ap.parse_args()
by_edge = {}
for r in csv.DictReader(open(a.csv, newline='', encoding='utf-8')):
    if r['absorbed'] == '1':
        continue
    by_edge.setdefault(int(r['edge']), []).append(r)
d = json.load(open(a.tracks, encoding='utf-8'))
rows = []
for c in d['courses']:
    edges = [e for e, _ in c['chain']]
    seen = {}
    worst = []
    for e in edges:
        for r in by_edge.get(e, []):
            k = (r['class'], r['place'])
            if k in seen:
                continue
            seen[k] = r
            worst.append(r)
    per = {cl: 0 for cl in ORDER}
    for (cl, _), r in seen.items():
        per[cl] = per.get(cl, 0) + 1
    worst.sort(key=lambda r: -float(r['visibility']))
    # without the systemic fan lips, which every junction has
    hard = [r for r in worst if not (r['class'] == 'STEP' and r['kind'] == 'junction fan lip') and r['class'] != 'SURFACE']
    rows.append((c, per, worst, hard))
out = []
out.append('| course | km | failing places | by class | worst three (not counting fan lips and stretched texture) |')
out.append('|---|---|---|---|---|')
for c, per, worst, hard in rows:
    tot = sum(per.values())
    cls = ', '.join(f'{k} {v}' for k, v in per.items() if v)
    w3 = '; '.join(f"{r['class']} e{r['edge']}: {r['what'][:110]}" for r in hard[:3])
    out.append(f"| {c['id']} {c['name']} | {c['lengthM'] / 1000:.1f} | {tot} | {cls} | {w3} |")
txt = '\n'.join(out)
if a.md:
    open(a.md, 'w', encoding='utf-8').write(txt + '\n')
clean = [c['id'] for c, per, worst, hard in rows if not hard]
print(txt.encode('ascii', 'replace').decode())
print('courses with no failure but fan lips / stretched texture:', clean)
