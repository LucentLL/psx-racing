# audit_3dep_report.py - the per-stage numbers of the lidar audit as markdown.
#
#   py -I tools/roads/audit_3dep_report.py <audit dir> <doc.md>
#
# Replaces everything between "<!-- STAGES:BEGIN -->" and "<!-- STAGES:END -->"
# in the doc (and "<!-- TABLE:BEGIN/END -->" with the one-line-per-stage table)
# from the <pre>_audit.json files tools/roads/audit_3dep.mjs wrote.

import json, os, sys

ORDER = ['swa', 'gap', 'mtm', 'beech', 'chimney', 'brp', 'swiss', 'brock']
GAME_WIDTH = {'swa': 6.1, 'gap': 6.7, 'mtm': 6.1, 'beech': 6.1, 'chimney': 6.1, 'brp': 6.4, 'swiss': 6.4, 'brock': 6.4}

def table(ds):
    o = ['| Stage | Length | Line vs bench: median / p95 / worst | Stretches > 2 m off | Height: bias / RMS / worst | Climb + descent, real / game | Grade p95, real / game | Grade max, real / game | Grade off by > 1% | Near grid RMS (30-100 m / 100-500 m) | Far grid RMS |',
         '|---|---|---|---|---|---|---|---|---|---|---|']
    for d in ds:
        p, l, t = d['profile'], d['line'], d['terrain']
        far = t['far']['all']['rmsM']
        o.append(f"| {d['road']} (`{d['stage']}`) | {d['lengthM'] / 1000:.2f} km | {l['medianAbsM']:.2f} / {l['p95AbsM']:.1f} / {l['worstAbsM']:.1f} m | {l['over2mRuns']} ({l['over2mRunsM']} m, {100 * l['over2mRunsM'] / d['lengthM']:.0f}%) | "
                 f"{p['biasM']:+.2f} / {p['rmsM']:.2f} / {p['worstM']:+.1f} m at {p['worstAtM']} m | {p['climbReal']['up'] + p['climbReal']['down']:.0f} / {p['climbGame']['up'] + p['climbGame']['down']:.0f} m | "
                 f"{p['gradeReal']['p95Pct']:.1f} / {p['gradeGame']['p95Pct']:.1f}% | {p['gradeReal']['maxPct']:.1f} / {p['gradeGame']['maxPct']:.1f}% | {p['gradeOff1pctShare']:.0f}% of the route | "
                 f"{t['near']['bands'][1]['rmsM']:.1f} / {t['near']['bands'][2]['rmsM']:.1f} m | {(f'{far:.1f} m' if far < 20 else 'HOLE, see text')} |")
    return '\n'.join(o)

def stage(d):
    pre = d['stage']; p, l, t, w, st, osm = d['profile'], d['line'], d['terrain'], d['width'], d['structures'], d['osm']
    o = [f"### {d['road']} (`{pre}`, {d['id']})", '',
         f"{d['lengthM'] / 1000:.2f} km{', loop' if d['loop'] else ''}; baked by `{d['baker']}`. Plots: [profile](shots/lidar_audit/{pre}_profile.png), [plan and offsets](shots/lidar_audit/{pre}_plan.png).", '']
    o += [f"**Line.** Bench found at {l['confidentPct']:.0f}% of stations. Offset of the bench centre from the game centreline: median {l['medianAbsM']:.2f} m, p95 {l['p95AbsM']:.1f} m, worst single station {l['worstAbsM']:.1f} m (at {l['worstAtM']} m). "
          f"Mean offset {l['meanEastM']:+.2f} m east, {l['meanNorthM']:+.2f} m north. {l['over2mRuns']} stretches where the filtered offset is over 2 m, {l['over2mRunsM']} m of road in all. The largest:", '',
          '| From - to (m) | Length | Peak offset | Side |', '|---|---|---|---|']
    for r in l['worstRuns'][:6]:
        o.append(f"| {r['fromM']} - {r['toM']} | {r['lengthM']} m | {abs(r['peakM']):.1f} m | bench is to the {'left' if r['peakM'] > 0 else 'right'} of the line |")
    o += ['', f"**Height.** Real {p['startReal']:.1f} m at the first waypoint, {p['endReal']:.1f} m at the last (game {p['startGame']:.1f}, {p['endGame']:.1f}). Game minus real: bias {p['biasM']:+.2f} m, RMS {p['rmsM']:.2f} m, p95 {p['p95AbsM']:.2f} m, "
          f"worst {p['worstM']:+.2f} m at {p['worstAtM']} m. The bake's raw source sampled at the waypoints, before its smoothing: bias {p['srtmRawBiasM']:+.2f} m, RMS {p['srtmRawRmsM']:.2f} m, worst {p['srtmRawWorstM']:+.1f} m. "
          f"Climb {p['climbReal']['up']:.0f} m real / {p['climbGame']['up']:.0f} game; descent {p['climbReal']['down']:.0f} / {p['climbGame']['down']:.0f}."]
    if p['worstHeightRuns']:
        o.append('Stretches more than 3 m off after the bias is taken out: ' + '; '.join(f"{r['fromM']}-{r['toM']} m ({r['peakM']:+.1f} m)" for r in p['worstHeightRuns'][:6]) + '.')
    else: o.append('No stretch is more than 3 m off once the bias is taken out.')
    o += ['', f"**Grade** (over an 8 m gaussian). Real: mean {p['gradeReal']['meanPct']:.2f}%, p95 {p['gradeReal']['p95Pct']:.2f}%, max {p['gradeReal']['maxPct']:.2f}% at {p['gradeReal']['maxAtM']} m. "
          f"Game: mean {p['gradeGame']['meanPct']:.2f}%, p95 {p['gradeGame']['p95Pct']:.2f}%, max {p['gradeGame']['maxPct']:.2f}% at {p['gradeGame']['maxAtM']} m. Over 8.5%: {p['realOver8p5pctM']} m of the real road, {p['gameOver8p5pctM']} m of the game's. "
          f"The game's grade is more than 1% of grade off the real one on {p['gradeOff1pctRuns']} stretches, {p['gradeOff1pctM']} m ({p['gradeOff1pctShare']:.0f}% of the route); RMS difference {p['gradeRmsDiffPct']:.2f}% of grade. The largest:", '',
          '| From - to (m) | Length | Real grade | Game grade |', '|---|---|---|---|']
    for r in p['worstGradeRuns'][:6]:
        o.append(f"| {r['fromM']} - {r['toM']} | {r['lengthM']} m | {r['realPct']:+.1f}% | {r['gamePct']:+.1f}% |")
    lanes = ', '.join(f"{k}: {v} m" for k, v in osm['lanes'].items()) if osm else 'n/a'
    o += ['', f"**Width.** Game {GAME_WIDTH[pre]} m. Lidar bench (pavement + shoulders + any level verge): median {w['benchMedianM']} m, narrowest tenth {w['benchP10M']} m, widest tenth {w['benchP90M']} m. "
          f"OSM `lanes`: {lanes}; no `width` tag on any way of the route."]
    if osm and osm.get('notTwoLane'):
        o.append('OSM says these stretches are not a plain two-way two-lane road: ' + '; '.join(f"{r['fromM']}-{r['toM']} m ({'lanes ' + r['lanes'] if r['lanes'] else 'lanes untagged'}{', one-way' if r['oneway'] else ''}{', ' + r['name'] if r['name'] else ''})" for r in osm['notTwoLane']) + '.')
    if w['wideSpots']:
        o.append(f"{len(w['wideSpots'])} wide spots (bench 5 m over the median for 24 m or more: pull-offs, overlooks, junction mouths): " + '; '.join(f"{s['fromM']}-{s['toM']} m {s['side']} ({s['maxBenchM']} m)" for s in w['wideSpots'][:12]) + '.')
    o += ['', '**Terrain** (game grid minus 3DEP 1/3", on the grid\'s own lattice).', '',
          '| Grid | Distance from the road | Points | Bias | RMS | Worst | Worst at (x east, z north) |', '|---|---|---|---|---|---|---|']
    for g in ('near', 'far'):
        for b in t[g]['bands']:
            o.append(f"| {g} ({t[g]['cellM']} m) | {b['fromM']} - {b['toM'] if b['toM'] else 'edge'} m | {b['points']} | {b['biasM']:+.2f} m | {b['rmsM']:.2f} m | {b['worstM']:+.1f} m | {b['worstAt'][0]}, {b['worstAt'][1]} |")
    o.append('')
    o.append('Largest wrong landforms (block means): ' + '; '.join(f"{g} grid {b['sizeM']} m block at ({b['x']}, {b['z']}), {b['roadM']} m from the road near station {b['nearestStationM']} m: game {b['meanM']:+.1f} m" for g in ('near', 'far') for b in t[g]['worstBlocks'][:3]) + '.')
    o += ['', '**Structures.** ' + (
        'Stage bridges: ' + ('; '.join(f"{b['fromM']:.0f}-{b['toM']:.0f} m ({'in OSM' if b['inOsm'] == 'yes' else 'NOT in OSM'})" for b in st['gameBridges']) or 'none') + '. '
        + 'Stage tunnels: ' + ('; '.join(f"{b['fromM']:.0f}-{b['toM']:.0f} m ({'in OSM' if b['inOsm'] == 'yes' else 'NOT in OSM'})" for b in st['gameTunnels']) or 'none') + '. '
        + 'OSM bridge ways on the route: ' + ('; '.join(f"{b['fromM']}-{b['toM']} m (ground {b['groundBelowDeckM']} m below the deck line; {'in the stage' if b['inGame'] == 'yes' else 'NOT in the stage'})" for b in (st['osmBridges'] or [])) or 'none') + '. '
        + 'OSM tunnels: ' + ('; '.join(f"{b['fromM']}-{b['toM']} m ({b.get('name') or 'unnamed'}; {'in the stage' if b['inGame'] == 'yes' else 'NOT in the stage'})" for b in (st['osmTunnels'] or [])) or 'none') + '. '
        + 'Places the lidar ground drops away under the road with no bridge mapped: ' + ('; '.join(f"{b['fromM']}-{b['toM']} m ({b['groundBelowRoadM']} m)" for b in st['unmappedDips']) or 'none') + '.'), '']
    return '\n'.join(o)

def main():
    src, doc = sys.argv[1], sys.argv[2]
    ds = [json.load(open(os.path.join(src, p + '_audit.json'), encoding='utf8')) for p in ORDER if os.path.exists(os.path.join(src, p + '_audit.json'))]
    text = open(doc, encoding='utf8').read()
    def put(tag, body):
        nonlocal text
        a, b = f'<!-- {tag}:BEGIN -->', f'<!-- {tag}:END -->'
        i, j = text.index(a) + len(a), text.index(b)
        text = text[:i] + '\n' + body + '\n' + text[j:]
    put('TABLE', table(ds))
    put('STAGES', '\n'.join(stage(d) for d in ds))
    open(doc, 'w', encoding='utf8', newline='\n').write(text)
    print('wrote', doc)

if __name__ == '__main__':
    main()
