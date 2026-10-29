"""Rank a road-logic census by traffic (Editor/CityTrafficCensus.cs) into
Docs/ROAD_LOGIC_TRAFFIC.md, a hotspot map, a hotspots CSV, the events CSV and
the top-6 spot list the close-ups job photographs. Re-runnable on the raw data
tools/city-traffic-census.ps1 copies back (Docs/traffic_census/raw/).

  py -3 tools/traffic_census_report.py --raw Docs/traffic_census/raw --out Docs/traffic_census
        --md Docs/ROAD_LOGIC_TRAFFIC.md [--top6 <sandbox>/census/top6.txt]
"""
import argparse, collections, csv, gzip, io, json, math, os, shutil

# ---- severity (0-10) and a one-line diagnosis guess per kind --------------------
K = {
    ("wreck", "barrier"): (9, "a lane or turn path runs into a barrier: the barrier stands inside the drawn pavement (a taper or gore end) or the lanes are laid wider than the road here"),
    ("wreck", "building"): (9, "a lane or turn path crosses a building footprint: the footprint sits on the road"),
    ("wreck", "pole"): (8, "a pole or sign post stands in a lane or a turn arc"),
    ("wreck", "ground"): (8, "the ground mesh rises through the road surface (terrain above the drawn road)"),
    ("wreck", "road-surface"): (8, "the car body meets the road collider: a step or a sharp grade change in the drawn road (junction tile vs ribbon height, or the car pitched off the surface)"),
    ("wreck", "tree"): (7, "a tree trunk stands in a lane or a turn arc"),
    ("wreck", "prop"): (7, "a solid prop stands in a lane or a turn arc"),
    ("wreck", "bank"): (7, "a creek bank rises into the road"),
    ("wreck", "water"): (9, "a lane runs into water"),
    ("wreck", "car"): (7, "two traffic cars collided: their paths conflict here (control, lane mapping or a merge)"),
    ("wreck", "car-in-junction"): (7, "two cars collided crossing or turning in one junction: the junction's control lets conflicting movements in together"),
    ("wreck", "car-roads-cross-no-node"): (9, "two roads cross here with no junction node between them: traffic on one cannot see the other (a missing node, or an at-grade crossing that should be a bridge)"),
    ("wreck", "car-roads-overlap"): (8, "two different roads run side by side close enough to collide: a ramp or carriageway laid over another road's lanes, or a merge with no node"),
    ("wreck", "car-merging-roads"): (8, "two cars on two roads that meet at a node collided short of it: the two roads' lanes run together before the node (a ramp beside lane 0, two approaches at a sharp angle) and one car did not see the other"),
    ("wreck", "car-parting-roads"): (8, "two cars on two roads that part at a node collided past it: the two roads' lanes still run together after the node (an exit just past the diverge)"),
    ("wreck", "car-across-node"): (7, "a car ran into one just past the node on the road ahead of it (not the road it planned): a rear-end across the node"),
    ("wreck", "car-near-node"): (7, "two cars on roads one short edge apart collided: lanes meeting across a short link"),
    ("wreck", "car-same-road"): (6, "two cars on one road collided: lanes too narrow for side-by-side cars here (a taper squeezing the lanes) or a lane change into a car"),
    ("wreck", "unknown"): (6, "a hit with no contact named"),
    ("wreck", "other"): (6, "a hit on something unnamed"),
    ("wreck", "player"): (1, "the player (the ghost should never be hit)"),
    ("contact", "barrier"): (5, "a lane or turn passes within a car's half width of a barrier (grazes it)"),
    ("contact", "building"): (5, "a lane or turn grazes a building"),
    ("contact", "pole"): (5, "a lane or turn grazes a pole or sign post"),
    ("contact", "ground"): (4, "the ground touches the car's body: terrain at road height or above at the edge"),
    ("contact", "tree"): (4, "a lane or turn grazes a tree"),
    ("contact", "prop"): (4, "a lane or turn grazes a prop"),
    ("contact", "bank"): (4, "a creek bank touches the car"),
    ("contact", "water"): (5, "water touches the car"),
    ("contact", "ground-at-road-level"): (0, "the ground surface within a few cm of the lane height beside or under the car (a speculative contact at speed): the verge at the road edge, not ground in the way"),
    ("contact", "car-bump"): (3, "two traffic cars bumped (below the wreck line): their paths touch here"),
    ("contact", "other"): (3, "a touch on something unnamed"),
    ("contact", "unknown"): (3, "a touch on something unnamed"),
    ("stuck", "crawl"): (7, "cars crawl under 1 m/s with nothing ahead: the lane's line has a hairpin or kink the speed planner reads as a tiny radius"),
    ("stuck", "crawl-in-turn"): (7, "cars crawl in this turn with nothing ahead: the arc or the road after it reads as a hairpin"),
    ("stuck", "gridlock-box"): (5, "a queue stands in the junction box 15 s+: the next junction is too close (no storage) or something blocks the exit"),
    ("stuck", "stop-deadlock"): (7, "cars wait 30 s+ at a STOP and never get to go: a STOP facing a busy road with no gap, or STOP approaches waiting on each other"),
    ("stuck", "keep-clear-wait"): (2, "cars wait 40 s+ short of a junction whose way out is full (they keep the box clear): a long queue from the next junction"),
    ("stuck", "yield-starved"): (4, "a minor road waits 40 s+ for a gap in the major one: a busy unsigned junction (a signal or STOP missing)"),
    ("stuck", "left-turn-starved"): (4, "a left turn waits 45 s+ for the oncoming flow: wants a protected left phase"),
    ("stuck", "red-never-ends"): (8, "a red held 60 s+ (the cycle is 38 s): the approach's phase or a blocked box"),
    ("stuck", "box-blocked-on-green"): (5, "cars hold 25 s+ on a green for a car still in the box: something stands in the junction"),
    ("stuck", "lane-change-wait"): (3, "cars wait short of the junction for the lane their turn needs: the turn lane begins too late"),
    ("stuck", "behind-wreck"): (1, "held behind a wreck (a consequence of a wreck nearby)"),
    ("stuck", "crossing-blocked"): (5, "a car crossing its path holds it 15 s+: two flows lock at a conflict point"),
    ("lane", "missed-turn-lane"): (4, "cars cannot reach the lane their turn needs in time (14 s with no gap): the turn lane is too short or opens too late"),
    ("lane", "forced-short-change"): (2, "a lane change squeezed into under 2.2 s: too little room before the node (a lane drop or turn lane right at it)"),
    ("lane", "forced-into-tight-gap"): (2, "a driver forced into a tight gap after waiting 5 s+: the lanes are too busy to change here"),
    ("lane", "turn-from-wrong-lane"): (5, "a turn begun from the wrong lane: it cuts across a lane in the box"),
    ("lanes", "no-continuing-lane"): (6, "straight on, no lane lines up across the node (2 m+): the lane centres jump sideways between the two edges"),
    ("lanes", "lanes-misaligned"): (4, "straight on, a lane finds no partner within 2 m though the next road has as many lanes: the lanes shift across the node"),
    ("merge", "merge-stalled"): (6, "cars stall in the ramp merge: the merge zone is short or the ramp meets the freeway badly"),
    ("merge", "ramp-waiting"): (5, "cars stop on the ramp for the mainline: no room to merge (no acceleration lane)"),
    ("merge", "short-merge-zone"): (5, "the ramp runs alongside the freeway under 30 m before it joins: no room to merge"),
    ("merge", "exit-stalled"): (5, "cars stall in the diverge onto the ramp"),
    ("offpave", "lane-outside-pavement"): (6, "the traffic lane lies outside the drawn pavement: the line set (lanes x width) is wider than the ribbon here"),
    ("offpave", "no-road-under-lane"): (7, "no road surface under a lane inside the pavement band: a hole or gap in the road mesh"),
    ("offpave", "turn-leaves-road"): (6, "the turn arc leaves the road surface: the junction surface does not cover this movement (a missing corner / curb return)"),
    ("offpave", "turn-clips-corner"): (2, "the car's nose crosses the walk on this turn: the curb return is tight for the arc"),
    ("wrongway", "across-divider"): (7, "a car in its own lane sits across the centre line: the lane layout disagrees with the divider"),
    ("route", "one-way-trap"): (9, "a one-way runs into a node with no legal way on: a wrong one-way tag or a missing connection"),
    ("route", "one-way-no-entry"): (6, "a one-way nobody may drive into: a wrong one-way tag or a missing connection"),
    ("route", "dead-end-junction"): (6, "the road meets other roads but may enter none (all one-way inbound): only a U-turn"),
    ("route", "major-road-dead-end"): (4, "a class-2+ road (or ramp) simply ends: a missing connection in the data"),
    ("route", "unreachable-part"): (5, "a part of the network no car from the rest of the city can drive into"),
    ("route", "inescapable-part"): (8, "a part of the network a car can drive into and never out of"),
    ("route", "dead-end"): (8, "a car ran out of legal ways on (a one-way into nothing)"),
    ("route", "u-turn-forced"): (4, "a U-turn at a node with other arms: none of them was legal (one-way tags) or the 1-in-1000 pick"),
    ("route", "u-turn-dead-end"): (0.3, "a U-turn at a cul-de-sac (normal)"),
    ("route", "hairpin-turn"): (4, "a movement of more than 150 deg between two roads at one node: an acute junction"),
    ("vertical", "crest-launch"): (8, "a crest sharp enough to launch a car at the limit: a grade break in the profile (a station spike, a deck end)"),
    ("vertical", "crest"): (5, "a sharp crest: a car at the limit goes light"),
    ("vertical", "sag-jolt"): (5, "a sharp sag: a car at the limit bottoms out"),
    ("vertical", "step"): (6, "a step in the road surface: a lip between pieces (junction tile vs ribbon, a deck end)"),
    ("vertical", "pose-off-surface"): (3, "the traffic's height model (the profile) disagrees with the drawn road by 35 cm+"),
    ("lateral", "kink"): (4, "a corner in the lane line mid-road (4 deg+ within a metre)"),
    ("lateral", "ramp-bend-no-limit"): (1.5, "a ramp bend tighter than the traffic's ramp default (72 km/h): the ramp carries no maxspeed of its own - wants an advisory limit, not new geometry"),
    ("route", "dead-end-map-edge"): (0.2, "the road is cut where the data stops (the map's edge, not a road-logic fault)"),
    ("lateral", "bend-tighter-than-limit"): (3, "a bend tighter than its limit allows (0.46 g+ at the limit): wants a smoother curve or a lower limit"),
    ("lateral", "turn-tighter-than-planned"): (3, "the turn arc is tighter than its planned radius (0.4 g+): the junction mouths are offset or close"),
    ("lateral", "heading-snap"): (4, "the heading jumps 6 deg+ where one leg hands over to the next: the turn arc does not meet the lane's direction"),
    ("conflict", "signal-crossing-flows"): (7, "cars from crossing approaches in the box together under signals: phase groups or clearance wrong"),
    ("conflict", "uncontrolled-crossing"): (6, "crossing flows meet in a junction with no control at all: a signal or STOP missing"),
    ("conflict", "stop-crossing-flows"): (5, "crossing flows meet in the box at a STOP junction"),
    ("conflict", "yield-crossing-flows"): (4, "crossing flows meet in the box at a yield junction"),
    ("signal", "conflicting-green"): (9, "crossing approaches share a phase group: both green at once"),
    ("control", "no-control-major-crossing"): (5, "three or more class-2+ arms meet with no signal or STOP and no through road: the data likely misses a signal"),
    ("speed", "implausible-limit"): (2, "an implausible maxspeed for the road's class"),
    ("speed", "limit-jump"): (2, "the limit jumps 20+ mph along one road at a node"),
    ("traffic", "overlap"): (3, "a traffic-code fault (two cars overlapping), not the road's"),
}
CATS = ["wreck", "contact", "stuck", "lane", "lanes", "merge", "offpave", "wrongway", "route", "vertical", "lateral", "conflict", "signal", "control", "speed", "traffic"]
CAT_NAME = {
    "wreck": "Wrecks (what was hit)", "contact": "Contacts (grazes, bumps)", "stuck": "Stuck and gridlock", "lane": "Lane changes and turn lanes",
    "lanes": "Lane alignment across nodes", "merge": "Ramp merges and diverges", "offpave": "Off the pavement", "wrongway": "Wrong way",
    "route": "Routes: one-way traps, dead ends, U-turns, islands", "vertical": "Vertical: steps, crests, sags", "lateral": "Lateral: kinks, tight bends, snaps",
    "conflict": "Crossing flows in one junction", "signal": "Signal phases", "control": "Junction control", "speed": "Speed limits", "traffic": "Traffic-code faults",
}
COL = {
    "wreck": (255, 64, 64), "contact": (255, 150, 60), "stuck": (255, 220, 40), "lane": (90, 200, 255), "lanes": (40, 120, 255),
    "merge": (190, 110, 255), "offpave": (255, 90, 200), "wrongway": (255, 255, 255), "route": (60, 230, 120), "vertical": (0, 230, 220),
    "lateral": (170, 230, 60), "conflict": (255, 40, 140), "signal": (255, 0, 0), "control": (200, 160, 110), "speed": (150, 150, 150), "traffic": (110, 110, 110),
}
DEFAULT_SEV = {"wreck": 7, "contact": 3, "stuck": 4, "lane": 2, "lanes": 3, "merge": 4, "offpave": 4, "wrongway": 6, "route": 4,
               "vertical": 4, "lateral": 3, "conflict": 5, "signal": 8, "control": 4, "speed": 1, "traffic": 2}
# a condition a car lives in for a while (a bend, a slow crawl through it) is
# counted once per car, not once per log line
PER_CAR = {("lateral", "bend-tighter-than-limit"), ("lateral", "ramp-bend-no-limit"), ("lateral", "turn-tighter-than-planned"),
           ("lateral", "kink"), ("vertical", "crest"), ("vertical", "sag-jolt"), ("vertical", "crest-launch")}
VISIBLE = {"wreck", "contact", "stuck", "lane", "lanes", "merge", "offpave", "wrongway", "route", "vertical", "lateral", "conflict", "signal", "control"}


def sev_of(cat, sub):
    if (cat, sub) in K:
        return K[(cat, sub)][0]
    if cat == "speed" and sub.startswith("missing-limit"):
        return 1
    return DEFAULT_SEV.get(cat, 2)


def diag_of(cat, sub):
    if (cat, sub) in K:
        return K[(cat, sub)][1]
    if cat == "speed" and sub.startswith("missing-limit"):
        return "no maxspeed on these edges: the traffic drives its class default"
    return cat + " / " + sub


def load_events(path):
    out = []
    with io.open(path, encoding="utf-8", newline="") as f:
        for r in csv.DictReader(f):
            try:
                r["x"] = float(r["x"]); r["z"] = float(r["z"]); r["y"] = float(r["y"])
                r["value"] = float(r["value"] or 0); r["hdg"] = float(r.get("hdg") or 0)
                r["node"] = int(r["node"]); r["edge"] = int(r["edge"]); r["edge2"] = int(r["edge2"])
            except (ValueError, KeyError):
                continue
            # a touch of the ground at the lane height is a speculative contact
            # (PhysX reports surfaces within a step's travel of the box, which
            # rides 0.22 m up): the verge or the ground under the road at road
            # level, not ground in the car's way - kept apart, scored 0
            if r["cat"] == "contact" and r["sub"] in ("ground", "bank") and r["value"] < 0.15:
                r["sub"] = "ground-at-road-level"
            out.append(r)
    return out


def load_edges(path):
    edges = {}
    with io.open(path, encoding="utf-8") as f:
        for line in f:
            p = line.rstrip("\n").split(";")
            if len(p) < 10:
                continue
            pts = []
            for q in p[9].split():
                a, b = q.split(",")
                pts.append((float(a), float(b)))
            edges[int(p[0])] = dict(cls=int(p[1]), link=p[2] == "1", oneway=p[3] == "1", lim=float(p[4]), drv=p[5] == "1",
                                    m=float(p[6]), disc=p[7] == "1", name=p[8], pts=pts,
                                    a=int(p[10]) if len(p) > 11 and p[10] else -1, b=int(p[11]) if len(p) > 11 and p[11] else -1)
    return edges


def split_car_wrecks(events, edges):
    """A car-on-car wreck by how the two cars' roads relate: in a junction (a
    turn leg), on one road, or on two roads that overlap or cross without a
    node between them."""
    import re
    rx = re.compile(r"run e(\d+) \S+ dir (-?1) lane (\d+)(?:->(\d+))? of (\d+) ([\d.]+)/([\d.]+) m")
    near_nodes = None
    if edges and next(iter(edges.values()))["a"] >= 0:
        near_nodes = collections.defaultdict(set)
        for x in edges.values():
            L = sum(math.dist(x["pts"][i], x["pts"][i + 1]) for i in range(len(x["pts"]) - 1))
            if L < 60:
                near_nodes[x["a"]].add(x["b"]); near_nodes[x["b"]].add(x["a"])
    def heading(eid, s):
        pts = edges[eid]["pts"]; acc = 0.0
        for i in range(len(pts) - 1):
            d = math.dist(pts[i], pts[i + 1])
            if acc + d >= s or i == len(pts) - 2:
                return math.degrees(math.atan2(pts[i + 1][1] - pts[i][1], pts[i + 1][0] - pts[i][0]))
            acc += d
        return 0.0
    for e in events:
        if e["cat"] != "wreck" or e["sub"] != "car":
            continue
        parts = e["detail"].split("| other:")
        a = rx.search(parts[0]); b = rx.search(parts[1]) if len(parts) > 1 else None
        if not a or not b:
            e["sub"] = "car-in-junction"
            continue
        ea, eb = int(a.group(1)), int(b.group(1))
        if ea == eb:
            e["sub"] = "car-same-road"
            continue
        if ea in edges and eb in edges and len(edges[ea]["pts"]) > 1 and len(edges[eb]["pts"]) > 1:
            # how the two roads RELATE in the graph first (2026-10-29: every
            # one of run 1's 98 "overlap" / "cross with no node" collisions was
            # on two edges sharing a node - merges, diverges, rear-ends across
            # a node - none on roads laid over each other or crossing unseen)
            A, B = edges[ea], edges[eb]
            if A["a"] >= 0 and B["a"] >= 0:
                da, db = int(a.group(2)), int(b.group(2))
                endA, startA = (A["b"], A["a"]) if da > 0 else (A["a"], A["b"])
                endB, startB = (B["b"], B["a"]) if db > 0 else (B["a"], B["b"])
                if endA == endB:
                    e["sub"] = "car-merging-roads"; continue
                if startA == startB:
                    e["sub"] = "car-parting-roads"; continue
                if endA == startB or endB == startA or {A["a"], A["b"]} & {B["a"], B["b"]}:
                    e["sub"] = "car-across-node"; continue
                if near_nodes is not None and (near_nodes.get(A["a"], set()) | near_nodes.get(A["b"], set())) & {B["a"], B["b"]}:
                    e["sub"] = "car-near-node"; continue
            d = abs((heading(ea, float(a.group(6))) - heading(eb, float(b.group(6))) + 180) % 360 - 180)
            d = min(d, 180 - d)
            e["sub"] = "car-roads-cross-no-node" if d > 45 else "car-roads-overlap"


def cluster(events, cell=30.0):
    """Hotspots: per (cat, sub), events within ~30 m of each other (grid cells,
    joined with their 8 neighbours)."""
    by = collections.defaultdict(list)
    glob = []
    for e in events:
        if e["x"] == 0 and e["z"] == 0 and e["node"] < 0:
            glob.append(e)
            continue
        by[(e["cat"], e["sub"])].append(e)
    spots = []
    for (cat, sub), evs in by.items():
        cells = collections.defaultdict(list)
        for e in evs:
            cells[(int(math.floor(e["x"] / cell)), int(math.floor(e["z"] / cell)))].append(e)
        parent = {c: c for c in cells}
        def find(c):
            while parent[c] != c:
                parent[c] = parent[parent[c]]
                c = parent[c]
            return c
        for (cx, cz) in cells:
            for dx in (-1, 0, 1):
                for dz in (-1, 0, 1):
                    n = (cx + dx, cz + dz)
                    if n in cells:
                        a, b = find((cx, cz)), find(n)
                        if a != b:
                            parent[a] = b
        groups = collections.defaultdict(list)
        for c, l in cells.items():
            groups[find(c)].extend(l)
        for g in groups.values():
            spots.append(make_spot(cat, sub, g))
    return spots, glob


def make_spot(cat, sub, g):
    n = len(g)
    x = sum(e["x"] for e in g) / n
    z = sum(e["z"] for e in g) / n
    ys = sorted(e["y"] for e in g)
    worst = max(g, key=lambda e: e["value"])
    nodes = collections.Counter(e["node"] for e in g if e["node"] >= 0)
    edges = collections.Counter(e["edge"] for e in g if e["edge"] >= 0)
    edges2 = collections.Counter(e["edge2"] for e in g if e["edge2"] >= 0)
    roads = collections.Counter(e["road"] for e in g if e["road"])
    moves = collections.Counter(e["move"] for e in g if e["move"])
    cars = len(set(e["car"] for e in g))
    sev = sev_of(cat, sub)
    # a lane jump is as bad as it is wide
    if (cat, sub) == ("lanes", "no-continuing-lane"):
        sev = 4 + min(4.0, worst["value"] / 2)
    elif (cat, sub) == ("lanes", "lanes-misaligned"):
        sev = 3 + min(3.0, worst["value"] / 2)
    static = all(e["move"] == "static" for e in g)
    k = cars if (cat, sub) in PER_CAR else n
    return dict(cat=cat, sub=sub, count=k, cars=cars, x=x, z=z, y=ys[len(ys) // 2], hdg=worst["hdg"], sev=sev, score=sev * k,
                wx=worst["x"], wz=worst["z"], wy=worst["y"],
                node=nodes.most_common(1)[0][0] if nodes else -1, edge=edges.most_common(1)[0][0] if edges else -1,
                edge2=edges2.most_common(1)[0][0] if edges2 else -1, roads=[r for r, _ in roads.most_common(2)],
                moves=[m for m, _ in moves.most_common(2)], vmax=worst["value"], vmean=sum(e["value"] for e in g) / n,
                detail=worst["detail"], static=static, diag=diag_of(cat, sub))


def fmt_spot(s, i=None):
    ids = []
    if s["node"] >= 0: ids.append("node %d" % s["node"])
    if s["edge"] >= 0: ids.append("e%d" % s["edge"])
    if s["edge2"] >= 0 and s["edge2"] != s["edge"]: ids.append("e%d" % s["edge2"])
    roads = " / ".join(r for r in s["roads"] if r) or "(unnamed)"
    head = ("%d. " % i) if i is not None else ""
    what = ("static, %d cars took it" % s["driven"]) if s["static"] and s.get("driven") else "static" if s["static"] else ("%d events, %d cars" % (s["count"], s["cars"]))
    mv = "" if s["static"] or not s["moves"] else " - moves: " + ", ".join(s["moves"])
    return "%s**%s / %s** score %.0f (sev %.1f x %d) - %s - (%.0f, %.0f) - %s - %s%s\n   - why: %s\n   - e.g. %s" % (
        head, s["cat"], s["sub"], s["score"], s["sev"], max(1, s.get("driven") or 0) if s["static"] and s["cat"] == "lanes" else s["count"], roads, s["x"], s["z"], ", ".join(ids), what, mv,
        s["diag"], (s["detail"] or "")[:300].replace("|", "/"))


def pick_top(spots, n=6, apart=200.0):
    """The spots to photograph: by score, one per kind and at most two per
    category (six bends in a row would show one thing six times), 200 m apart."""
    out, kinds, cats = [], set(), collections.Counter()
    for s in sorted(spots, key=lambda s: -s["score"]):
        if s["cat"] not in VISIBLE or s["sev"] <= 0 or s["static"]:
            continue
        if (s["cat"], s["sub"]) in kinds or cats[s["cat"]] >= 2:
            continue
        if any((s["x"] - o["x"]) ** 2 + (s["z"] - o["z"]) ** 2 < apart * apart for o in out):
            continue
        out.append(s); kinds.add((s["cat"], s["sub"])); cats[s["cat"]] += 1
        if len(out) >= n:
            break
    return out


def font(size, bold=False):
    from PIL import ImageFont
    for f in (["arialbd.ttf", "segoeuib.ttf"] if bold else ["arial.ttf", "segoeui.ttf"]):
        p = os.path.join(os.environ.get("WINDIR", "C:\\Windows"), "Fonts", f)
        if os.path.exists(p):
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()


def draw_map(edges, spots, top, summary, path, maxpx=2600):
    from PIL import Image, ImageDraw
    xs, zs = [], []
    for e in edges.values():
        if e["disc"] or e["drv"]:
            for (x, z) in e["pts"]:
                xs.append(x); zs.append(z)
    if not xs:
        for e in edges.values():
            for (x, z) in e["pts"]:
                xs.append(x); zs.append(z)
    x0, x1 = min(xs) - 300, max(xs) + 300
    z0, z1 = min(zs) - 300, max(zs) + 300
    legend_w = 560
    k = (maxpx - legend_w) / max(x1 - x0, z1 - z0)
    W, H = int((x1 - x0) * k) + legend_w, int((z1 - z0) * k)
    H = max(H, 1100)
    img = Image.new("RGB", (W, H), (18, 20, 26))
    d = ImageDraw.Draw(img)
    def P(x, z):
        return ((x - x0) * k, H - (z - z0) * k)
    order = sorted(edges.values(), key=lambda e: (e["drv"], e["cls"]))
    for e in order:
        if len(e["pts"]) < 2:
            continue
        if e["drv"]:
            c = (150, 156, 170) if e["cls"] < 4 and not e["link"] else (205, 210, 222)
            wdt = 1 if e["cls"] <= 1 else 2 if e["cls"] <= 3 else 3
        elif e["disc"]:
            c, wdt = (70, 60, 50), 1
        else:
            c, wdt = (40, 43, 52), 1
        d.line([P(x, z) for (x, z) in e["pts"]], fill=c, width=wdt)
    # hotspots: biggest first so small ones stay visible on top
    for s in sorted(spots, key=lambda s: -s["score"]):
        if s["cat"] in ("speed", "traffic") or s["sev"] <= 0:
            continue
        r = 2.5 + 2.2 * math.log10(1 + s["score"])
        px, pz = P(s["x"], s["z"])
        d.ellipse([px - r, pz - r, px + r, pz + r], outline=COL.get(s["cat"], (255, 255, 255)), width=2 if r > 4 else 1)
    f_lab = font(26, True)
    for i, s in enumerate(top, 1):
        px, pz = P(s["x"], s["z"])
        r = 16
        d.ellipse([px - r, pz - r, px + r, pz + r], fill=COL.get(s["cat"], (255, 255, 255)), outline=(0, 0, 0), width=2)
        tw = d.textlength(str(i), font=f_lab)
        d.text((px - tw / 2, pz - 15), str(i), font=f_lab, fill=(0, 0, 0))
    # uptown
    up = summary.get("uptown")
    if up:
        px, pz = P(up[0], up[1])
        d.rectangle([px - 5, pz - 5, px + 5, pz + 5], outline=(255, 255, 255), width=2)
    # the legend panel (right), every text measured so nothing clips
    lx = W - legend_w + 24
    d.rectangle([W - legend_w, 0, W, H], fill=(10, 11, 15))
    f_t, f_s, f_x = font(34, True), font(22), font(19)
    y = 22
    d.text((lx, y), "Charlotte road-logic census", font=f_t, fill=(240, 240, 240)); y += 46
    sub = "rush hour 17:15, %s spots, %.1f car-hours, %.0f km driven" % (summary.get("spots"), summary.get("car_hours", 0), summary.get("km_driven", 0))
    for line in wrap(d, sub, f_x, legend_w - 48):
        d.text((lx, y), line, font=f_x, fill=(180, 180, 190)); y += 26
    y += 12
    tot = collections.Counter()
    for s in spots:
        if s["sev"] > 0:
            tot[s["cat"]] += s["count"]
    for cat in CATS:
        if cat in ("speed", "traffic") or tot[cat] == 0:
            continue
        d.ellipse([lx, y + 4, lx + 18, y + 22], outline=COL[cat], width=3)
        d.text((lx + 30, y), "%s  (%d)" % (CAT_NAME[cat], tot[cat]), font=f_s, fill=(225, 225, 230))
        y += 32
    y += 10
    d.text((lx, y), "ring size: severity x count", font=f_x, fill=(170, 170, 180)); y += 26
    d.line([(lx, y + 10), (lx + 40, y + 10)], fill=(205, 210, 222), width=3); d.text((lx + 52, y), "driven", font=f_x, fill=(170, 170, 180)); y += 26
    d.line([(lx, y + 10), (lx + 40, y + 10)], fill=(70, 60, 50), width=3); d.text((lx + 52, y), "in a visited disc, never driven", font=f_x, fill=(170, 170, 180)); y += 26
    d.line([(lx, y + 10), (lx + 40, y + 10)], fill=(40, 43, 52), width=3); d.text((lx + 52, y), "not visited", font=f_x, fill=(170, 170, 180)); y += 34
    d.text((lx, y), "Top spots (numbered on the map):", font=f_s, fill=(240, 240, 240)); y += 32
    for i, s in enumerate(top, 1):
        line = "%d. %s / %s - %s" % (i, s["cat"], s["sub"], " / ".join(r for r in s["roads"] if r) or "(unnamed)")
        for j, part in enumerate(wrap(d, line, f_x, legend_w - 48)):
            if y > H - 70:
                break
            d.text((lx + (0 if j == 0 else 22), y), part, font=f_x, fill=(220, 220, 225)); y += 24
    # a 1 km bar and north
    bx, by = 30, H - 40
    d.line([(bx, by), (bx + 1000 * k, by)], fill=(230, 230, 230), width=4)
    d.text((bx, by - 30), "1 km", font=f_x, fill=(230, 230, 230))
    d.text((bx, 20), "N ^", font=f_s, fill=(230, 230, 230))
    img = img.quantize(colors=96, method=Image.Quantize.MEDIANCUT)
    img.save(path, optimize=True)


def wrap(d, text, f, width):
    words, lines, cur = text.split(), [], ""
    for w in words:
        t = (cur + " " + w).strip()
        if d.textlength(t, font=f) <= width:
            cur = t
        else:
            if cur:
                lines.append(cur)
            cur = w
    if cur:
        lines.append(cur)
    return lines


def sheet(raw, top, path):
    from PIL import Image, ImageDraw
    shots = [os.path.join(raw, "census_top%d.png" % i) for i in range(1, 7)]
    have = [p for p in shots if os.path.exists(p)]
    if not have:
        return False
    cw, ch, cap = 960, 540, 64
    img = Image.new("RGB", (cw * 2, (ch + cap) * 3), (12, 12, 16))
    d = ImageDraw.Draw(img)
    f1, f2 = font(24, True), font(18)
    for i in range(6):
        if i >= len(top):
            break
        x, y = (i % 2) * cw, (i // 2) * (ch + cap)
        if os.path.exists(shots[i]):
            im = Image.open(shots[i]).convert("RGB").resize((cw, ch))
            img.paste(im, (x, y))
        s = top[i]
        t1 = "%d. %s / %s  (score %.0f, %d)" % (i + 1, s["cat"], s["sub"], s["score"], s["count"])
        t2 = "%s  (%.0f, %.0f)  %s" % (" / ".join(r for r in s["roads"] if r) or "(unnamed)", s["x"], s["z"],
                                     ("node %d" % s["node"]) if s["node"] >= 0 else ("e%d" % s["edge"]))
        for t, f, yy in ((t1, f1, y + ch + 6), (t2, f2, y + ch + 36)):
            while d.textlength(t, font=f) > cw - 20 and len(t) > 8:
                t = t[:-4] + "..."
            d.text((x + 10, yy), t, font=f, fill=(235, 235, 240))
    img.save(path, quality=86)
    return True


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--raw", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--md", required=True)
    ap.add_argument("--top6", default="")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    summary = json.load(io.open(os.path.join(a.raw, "summary.json"), encoding="utf-8"))
    events = load_events(os.path.join(a.raw, "events.csv"))
    edges = load_edges(os.path.join(a.raw, "edges.txt"))
    moves = collections.Counter()
    mp = os.path.join(a.raw, "moves.csv")
    if os.path.exists(mp):
        with io.open(mp, encoding="utf-8") as f:
            for r in csv.DictReader(f):
                moves[(int(r["edge_in"]), int(r["edge_out"]))] += int(r["count"])
    split_car_wrecks(events, edges)
    spots, glob = cluster(events)
    for s in spots:
        if s["static"] and s["cat"] == "lanes" and s["edge"] >= 0:
            # exposure: the cars that took the movement in the census
            s["driven"] = moves.get((s["edge"], s["edge2"]), 0)
            s["score"] = s["sev"] * max(1, s["driven"])
    spots.sort(key=lambda s: -s["score"])
    top = pick_top(spots)
    # the close-ups already taken: the spots they show (the sheet's captions and
    # the map's numbers must match the pictures)
    shot_list = os.path.join(a.raw, "shots_top6.txt")
    if os.path.exists(shot_list) and os.path.exists(os.path.join(a.raw, "census_top1.png")):
        shown = []
        for line in io.open(shot_list, encoding="utf-8"):
            p = line.rstrip(chr(10)).split("|")
            if len(p) < 5:
                continue
            kind = p[0].split(" ", 1)[0]
            x, z = float(p[1]), float(p[2])
            cand = [s for s in spots if s["cat"] + "/" + s["sub"] == kind] or spots
            shown.append(min(cand, key=lambda s: (s["x"] - x) ** 2 + (s["z"] - z) ** 2))
        if shown:
            top = shown

    # ---- the hotspot table and the events
    hs_path = os.path.join(a.out, "hotspots.csv")
    with io.open(hs_path, "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f)
        w.writerow(["rank", "score", "severity", "count", "cars", "cat", "sub", "x", "z", "y", "node", "edge", "edge2", "roads", "moves", "value_max", "static", "diagnosis", "example"])
        for i, s in enumerate([x for x in spots if x["sev"] > 0], 1):
            w.writerow([i, "%.1f" % s["score"], s["sev"], s["count"], s["cars"], s["cat"], s["sub"], "%.0f" % s["x"], "%.0f" % s["z"], "%.1f" % s["y"],
                        s["node"], s["edge"], s["edge2"], " / ".join(s["roads"]), " / ".join(s["moves"]), "%.3g" % s["vmax"], int(s["static"]), s["diag"][:90], s["detail"][:200]])
    if os.path.getsize(hs_path) > 2_500_000:
        with io.open(hs_path, "rb") as fi, gzip.open(hs_path + ".gz", "wb", compresslevel=9) as fo:
            shutil.copyfileobj(fi, fo)
        os.remove(hs_path)
    elif os.path.exists(hs_path + ".gz"):
        os.remove(hs_path + ".gz")
    ev_src = os.path.join(a.raw, "events.csv")
    if os.path.getsize(ev_src) > 3_000_000:
        with io.open(ev_src, "rb") as fi, gzip.open(os.path.join(a.out, "events.csv.gz"), "wb", compresslevel=9) as fo:
            shutil.copyfileobj(fi, fo)
        if os.path.exists(os.path.join(a.out, "events.csv")):
            os.remove(os.path.join(a.out, "events.csv"))
        ev_name = "events.csv.gz"
    else:
        shutil.copyfile(ev_src, os.path.join(a.out, "events.csv"))
        ev_name = "events.csv"
    shutil.copyfile(os.path.join(a.raw, "summary.json"), os.path.join(a.out, "summary.json"))

    # ---- the top 6 for the close-ups (sandbox census\top6.txt)
    if a.top6:
        os.makedirs(os.path.dirname(a.top6), exist_ok=True)
        with io.open(a.top6, "w", encoding="utf-8") as f:
            for s in top:
                f.write("%s/%s %s|%.1f|%.1f|%.1f|%.0f\n" % (s["cat"], s["sub"], (" / ".join(s["roads"]) or "").replace("|", "/"), s["wx"], s["wz"], s["wy"], s["hdg"]))

    # ---- the map and the close-up sheet
    draw_map(edges, spots, top, summary, os.path.join(a.out, "census_map.png"))
    has_sheet = sheet(a.raw, top, os.path.join(a.out, "census_top6.jpg"))

    # ---- the markdown
    cov = summary.get("coverage", {})
    km_all = sum(v["km"] for v in cov.values())
    km_in = sum(v["km_in_discs"] for v in cov.values())
    km_drv = sum(v["km_driven"] for v in cov.values())
    n_all = sum(v["edges"] for v in cov.values())
    n_drv = sum(v["edges_driven"] for v in cov.values())
    tot = collections.Counter(); tot_spots = collections.Counter(); tot_score = collections.Counter()
    for s in spots:
        if s["sev"] <= 0:
            continue
        tot[s["cat"]] += s["count"]; tot_spots[s["cat"]] += 1; tot_score[s["cat"]] += s["score"]
    L = []
    L.append("# Charlotte road logic, by traffic (census)\n")
    L.append("Generated by `tools/city-traffic-census.ps1` (Editor/CityTrafficCensus.cs, Scripts/City/CityTraffic.Census.cs, "
             "tools/traffic_census_report.py) on %s. **Nothing here has been fixed** - this is the list to pick from.\n" % summary.get("date", "?"))
    L.append("## How it was measured\n")
    L.append("- Charlotte free roam at rush hour (17:15), the player a ghost touring **%s spots** (a greedy cover of the drivable road by 380 m discs, nearest-first from uptown), "
             "%s cars round each spot, time scale up to 30 (physics still 1/60 s a step)." % (summary.get("spots"), summary.get("cap")))
    L.append("- **%.1f car-hours** simulated in %.0f game minutes (%.0f real), **%.0f km driven**, %s cars born, %s distinct junction movements taken." % (
        summary.get("car_hours", 0), summary.get("game_min", 0), summary.get("real_min", 0), summary.get("km_driven", 0), summary.get("cars_born"), summary.get("movements")))
    L.append("- Coverage: **%d of %d routable edges driven (%.0f of %.0f km)**; %.0f km lie inside the visited discs." % (n_drv, n_all, km_drv, km_all, km_in))
    L.append("- Every car is a probe: what it hits, where it stops for no reason, lanes it cannot reach, turns that leave the road, the drawn road's surface under it every metre "
             "(steps, crests, sags as the vertical speed a car at the limit gets), its heading (kinks, bends tighter than the limit), crossing flows in one box. "
             "Plus a static audit that needs no driving (signal phase groups, unsigned big-road crossings, one-way traps and islands, limits, lane alignment, merge room).")
    L.append("- Score = severity (0-10, below) x count. Counts of driven events grow with traffic, so a busy junction ranks above a quiet one with the same fault.")
    L.append("- Files: `Docs/traffic_census/census_map.png` (hotspots by category), `census_top6.jpg` (close-ups), `hotspots.csv` (every scored hotspot; .gz when large), `%s` (every event), `summary.json`.\n" % ev_name)
    L.append("![map](traffic_census/census_map.png)\n")
    if has_sheet:
        L.append("![top 6](traffic_census/census_top6.jpg)\n")
    L.append("## Reading it\n")
    L.append("- Static items (move `static`) come from the network itself and cover the whole map; driven items only where the tour went (the map's bright roads). "
             "A static lane item is scored by the cars that took that movement in the census.")
    L.append("- Two kinds are partly the traffic's own: `lane / turn-from-wrong-lane` (the traffic changed lanes late, so its turn cuts across; check the lane alignment "
             "items at the same node first) and `lateral / turn-tighter-than-planned` (the traffic's own turn arc). `traffic / overlap` is a traffic-code fault outright.")
    L.append("- `contact` items say where the thing touched the car (side, nose, underside) and how high above the lane: a touch on the underside is the ground or a "
             "kerb standing above the road surface.\n")
    L.append("## Category totals\n")
    L.append("| category | events | hotspots | score |")
    L.append("|---|---:|---:|---:|")
    for cat in CATS:
        if tot[cat]:
            L.append("| %s | %d | %d | %.0f |" % (CAT_NAME[cat], tot[cat], tot_spots[cat], tot_score[cat]))
    L.append("")
    sub_tot = collections.Counter(e["cat"] + "/" + e["sub"] for e in events)
    L.append("<details><summary>Every kind, counted</summary>\n")
    for k2 in sorted(sub_tot):
        L.append("- %s: %d" % (k2, sub_tot[k2]))
    L.append("\n</details>\n")
    L.append("## Top 25 (all categories, by score)\n")
    for i, s in enumerate(spots[:25], 1):
        L.append(fmt_spot(s, i))
    L.append("")
    for cat in CATS:
        cs = [s for s in spots if s["cat"] == cat]
        if not cs:
            continue
        L.append("## %s\n" % CAT_NAME[cat])
        L.append("%d events in %d hotspots. Kinds: %s.\n" % (tot[cat], len(cs), ", ".join("%s %d" % (k2.split("/", 1)[1], v) for k2, v in sorted(sub_tot.items()) if k2.split("/", 1)[0] == cat)))
        for i, s in enumerate(cs[:12], 1):
            L.append(fmt_spot(s, i))
        if len(cs) > 12:
            L.append("\n... and %d more in `hotspots.csv`." % (len(cs) - 12))
        L.append("")
    if glob:
        L.append("## City-wide\n")
        for e in glob:
            L.append("- %s / %s: %s" % (e["cat"], e["sub"], e["detail"]))
        L.append("")
    # never driven (inside visited discs), by class
    names = ["local", "minor", "collector", "arterial", "trunk", "freeway", "ramp"]
    L.append("## Never driven\n")
    L.append("| class | edges | km | in visited discs | driven | in discs, never driven (km) |")
    L.append("|---|---:|---:|---:|---:|---:|")
    for n in names:
        v = cov.get(n)
        if not v:
            continue
        L.append("| %s | %d | %.0f | %d (%.0f km) | %d (%.0f km) | %.0f |" % (n, v["edges"], v["km"], v["edges_in_discs"], v["km_in_discs"], v["edges_driven"], v["km_driven"],
                                                                           max(0.0, v["km_in_discs"] - v["km_driven"])))
    nd = [(eid, e) for eid, e in edges.items() if e["disc"] and not e["drv"] and (e["cls"] >= 2 or e["link"])]
    nd.sort(key=lambda t: -sum(math.dist(t[1]["pts"][i], t[1]["pts"][i + 1]) for i in range(len(t[1]["pts"]) - 1)))
    if nd:
        L.append("\nLongest class-2+ edges and ramps inside the visited discs that no car drove (a road nobody routes onto is often a broken connection):\n")
        for eid, e in nd[:15]:
            ln = sum(math.dist(e["pts"][i], e["pts"][i + 1]) for i in range(len(e["pts"]) - 1))
            mid = e["pts"][len(e["pts"]) // 2]
            L.append("- e%d '%s' cls%d%s%s %.0f m at (%.0f, %.0f)" % (eid, e["name"], e["cls"], " ramp" if e["link"] else "", " one-way" if e["oneway"] else "", ln, mid[0], mid[1]))
    L.append("")
    L.append("## Static audit (as the census printed it)\n")
    L.append("```")
    for line in summary.get("report", []):
        L.append(line)
    L.append("```\n")
    L.append("## Severity table\n")
    L.append("| kind | severity | diagnosis guess |")
    L.append("|---|---:|---|")
    for (cat, sub), (sv, dg) in sorted(K.items(), key=lambda kv: (CATS.index(kv[0][0]) if kv[0][0] in CATS else 99, -kv[1][0])):
        L.append("| %s / %s | %s | %s |" % (cat, sub, sv, dg))
    L.append("")
    L.append("## Re-run\n")
    L.append("```\npowershell -ExecutionPolicy Bypass -File tools\\city-traffic-census.ps1 -Minutes 60       # the full tour + close-ups (2 Unity jobs)\n"
             "powershell -ExecutionPolicy Bypass -File tools\\city-traffic-census.ps1 -Minutes 8 -MaxSpots 6 -NoShots   # a smoke run\n"
             "powershell -ExecutionPolicy Bypass -File tools\\city-traffic-census.ps1 -ReportOnly       # re-rank the last raw data\n```\n")
    L.append("The sandbox is `$env:PSX_SANDBOX` (default C:\\Users\\mcgee\\PSXCity). Compare runs by the category totals and the hotspot ids; "
             "the tour is random (spawns, routes), so expect +-20% in counts between runs of the same code.\n")
    io.open(a.md, "w", encoding="utf-8").write("\n".join(L))
    print("report: %d events -> %d hotspots; top: %s" % (len(events), len(spots), "; ".join("%s/%s %.0f" % (s["cat"], s["sub"], s["score"]) for s in spots[:5])))


if __name__ == "__main__":
    main()
