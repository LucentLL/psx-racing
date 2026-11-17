# linegate_worklist.py - the line gate's failures grouped by CAUSE, for the agent that fixes them.
#
#   python -I tools/city/linegate_worklist.py --csv city_linegate.csv [--top 6] [--first x,z,label] > worklist.md
#
# A cause = the class, its kind, and what the builder has on that edge there (the CSV's cause column:
# "model:relay(...) + taper(...)", "map:split-end", ...). One row per place (the gate's 25 m places).
import sys, csv, re, argparse, collections

ap = argparse.ArgumentParser()
ap.add_argument('--csv', required=True); ap.add_argument('--top', type=int, default=6); ap.add_argument('--first')
a = ap.parse_args()
rows = [r for r in csv.DictReader(open(a.csv, newline='', encoding='utf-8')) if r['absorbed'] != '1']
best = {}
for r in rows:
    k = (r['class'], r['place'])
    if k not in best or float(r['visibility']) > float(best[k]['visibility']):
        best[k] = r


def sig(r):
    c = r['cause']
    who, _, dev = c.partition(':')
    devs = [re.sub(r'\(.*?\)', '', d).strip() for d in dev.split(' + ') if d.strip()]
    # the device that most likely makes it: the first of these present
    for want in ('relay', 'refuge-shift', 'refuge-profile', 'refuge-widen', 'refuge-pair', 'split-end', 'aux-lane', 'shift', 'two-sided-taper', 'taper', 'clipped-branch', 'ramp'):
        if want in devs:
            return (who or '-', want)
    return (who or '-', 'none')


groups = collections.defaultdict(list)
for r in best.values():
    who, dev = sig(r)
    groups[(r['class'], r['kind'], who, dev)].append(r)
order = sorted(groups.items(), key=lambda kv: -sum(float(r['visibility']) for r in kv[1]))
print('| # | class / kind | cause (line model device on that edge) | places | T1 | T2 | T3 | summed visibility |')
print('|---:|---|---|---:|---:|---:|---:|---:|')
for i, ((cls, kind, who, dev), lst) in enumerate(order):
    t = [sum(1 for r in lst if r['tier'] == str(k)) for k in (1, 2, 3)]
    print(f"| {i + 1} | {cls} / {kind} | {who}: {dev} | {len(lst)} | {t[0]} | {t[1]} | {t[2]} | {sum(float(r['visibility']) for r in lst):.0f} |")
print()
for i, ((cls, kind, who, dev), lst) in enumerate(order[:40]):
    lst.sort(key=lambda r: -float(r['visibility']))
    print(f"### {i + 1}. {cls} / {kind} - {who}: {dev} ({len(lst)} places)")
    for r in lst[:a.top]:
        print(f"- {r['road']} e{r['edge']} s {r['s0']}-{r['s1']} ({r['lat']},{r['lon']}; x {float(r['x']):.0f} z {float(r['z']):.0f}) {r['mph']} mph: {r['what']} [{r['cause']}]")
    print()
