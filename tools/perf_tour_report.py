"""THE PERF TOUR'S FRAMES, READ (city perf round 1, 2026-10-30).

    py tools/perf_tour_report.py <run dir> [<run dir> ...] [--worst 12] [--csv out.csv]

Each run dir holds what tools/perf-tour.mjs received from Scripts/Dev/PerfTour.cs:
<tag>_env.txt, and per route <tag>_<route>_frames.csv / _spikes.txt / _summary.txt.
Prints per route: frames, average FPS, p50 / p95 / p99 / worst frame ms, frames
over 33 / 50 / 100 ms, collections, and WHAT TOOK each frame over 33 and over
50 ms (the largest of: the city's own slices, physics, the traffic's steps, the
other fixed / update / late scripts, rendering, UI, outside the player loop,
the rest of the loop; a frame with a collection in it whose largest part is
outside or the rest of the loop is put down to the collection). Then the worst
frames, with the player-loop systems the spike file names.
"""
import csv
import glob
import os
import sys

CITY_UPDATE = ["tile_step", "tile_finish", "tile_sync_build", "tile_stand-up", "tile_abandon", "tile_drop",
               "tree_step", "tree_urgent", "tree_stand", "signs", "proxy_step", "fade_tick"]
CITY_LATE = ["proxy_refresh", "far_land", "far_trees", "far_prefetch", "far_lights", "far_other", "skyline"]


def pct(s, q):
    if not s:
        return 0.0
    i = min(len(s) - 1, max(0, int(round(q * (len(s) - 1)))))
    return s[i]


def f(row, k):
    try:
        return float(row.get(k, 0) or 0)
    except ValueError:
        return 0.0


def buckets(r):
    b = {}
    for k in CITY_UPDATE + CITY_LATE:
        b[k] = f(r, k)
    b["traffic_fixed"] = f(r, "traffic_fixed")
    b["lamp_field"] = f(r, "lamp_field")
    b["physics"] = f(r, "phys")
    b["fixed_scripts"] = max(0.0, f(r, "fixs") - b["traffic_fixed"])
    b["fixed_other"] = max(0.0, f(r, "fix") - f(r, "phys") - f(r, "fixs"))
    b["update_scripts"] = max(0.0, f(r, "upd") - sum(b[k] for k in CITY_UPDATE))
    b["late_scripts"] = max(0.0, f(r, "late") - sum(b[k] for k in CITY_LATE))
    b["render"] = max(0.0, f(r, "rend") - b["lamp_field"])
    b["ui"] = f(r, "ui")
    b["outside_loop"] = f(r, "out")
    known = f(r, "fix") + f(r, "upd") + f(r, "late") + f(r, "rend") + f(r, "ui") + f(r, "out")
    b["rest_of_loop"] = max(0.0, f(r, "ms") - known)
    return b


def cause(r):
    b = buckets(r)
    k = max(b, key=lambda x: b[x])
    if int(f(r, "gc")) > 0 and k in ("outside_loop", "rest_of_loop"):
        return "GC (" + k + ")", b[k]
    return k, b[k]


def main():
    args = sys.argv[1:]
    worst_n = 12
    if "--worst" in args:
        i = args.index("--worst"); worst_n = int(args[i + 1]); del args[i:i + 2]
    out_csv = None
    if "--csv" in args:
        i = args.index("--csv"); out_csv = args[i + 1]; del args[i:i + 2]
    table = []
    for d in args:
        for env in sorted(glob.glob(os.path.join(d, "*_env.txt"))):
            tag = os.path.basename(env)[:-len("_env.txt")]
            print("=" * 100)
            print(f"RUN {tag}  ({d})")
            for line in open(env, encoding="utf-8", errors="replace"):
                if line.startswith("systems "):
                    continue
                print("  " + line.rstrip())
            for fr in sorted(glob.glob(os.path.join(d, tag + "_*_frames.csv"))):
                route = os.path.basename(fr)[len(tag) + 1:-len("_frames.csv")]
                rows = list(csv.DictReader(open(fr, encoding="utf-8")))
                snaps = sum(1 for r in rows if r.get("ev") == "SNAP")
                nudges = sum(1 for r in rows if r.get("ev") == "NUDGE")
                # a look run's own captures, and the frames a held-up car was set on in
                rows = [r for r in rows if r.get("ev") not in ("SNAP", "NUDGE")]
                if not rows:
                    continue
                ms = sorted(f(r, "ms") for r in rows)
                n = len(ms)
                tot = sum(ms)
                o33 = sum(1 for m in ms if m > 33.4)
                o50 = sum(1 for m in ms if m > 50)
                o100 = sum(1 for m in ms if m > 100)
                gcs = sum(int(f(r, "gc")) for r in rows)
                gc_ms = [f(r, "ms") for r in rows if int(f(r, "gc")) > 0]
                dist = 0.0
                for a, b in zip(rows, rows[1:]):
                    dx, dz = f(b, "x") - f(a, "x"), f(b, "z") - f(a, "z")
                    s = (dx * dx + dz * dz) ** 0.5
                    if s < 60:
                        dist += s
                secs = f(rows[-1], "t")
                print("-" * 100)
                print(f"  {route}: {n} frames, {secs:.0f} s, {dist:.0f} m, avg {dist / max(secs, 1e-3) * 3.6:.0f} km/h")
                print(f"    avg fps {n * 1000.0 / tot:.1f}  p50 {pct(ms, .5):.1f}  p95 {pct(ms, .95):.1f}  p99 {pct(ms, .99):.1f}  worst {ms[-1]:.1f} ms;"
                      f"  >33 {o33}  >50 {o50}  >100 {o100};  GC {gcs} (frames {', '.join(f'{m:.0f}' for m in gc_ms[:12])})")
                # the managed heap: what was allocated between collections (the
                # used heap's rises, frame to frame, where no collection fell)
                alloc = 0.0
                for a, b in zip(rows, rows[1:]):
                    if int(f(b, "gc")) == 0:
                        rise = f(b, "usedMB") - f(a, "usedMB")
                        if rise > 0:
                            alloc += rise
                used = [f(r, "usedMB") for r in rows]
                # (allocKB: this thread's own count, where the runtime keeps one)
                akb = sum(f(r, "allocKB") for r in rows)
                print(f"    heap used {min(used):.0f}-{max(used):.0f} MB; allocated {alloc:.0f} MB in {secs:.0f} s ({alloc / max(secs, 1e-3):.2f} MB/s)"
                      + (f"; counted {akb / 1024:.0f} MB ({akb / 1024 / max(secs, 1e-3):.2f} MB/s)" if akb > 0 else "")
                      + (f"; {snaps} snapshot frames left out" if snaps else "")
                      + (f"; set on past a hold-up {nudges}x (those frames left out)" if nudges else ""))
                table.append([tag, route, n, round(n * 1000.0 / tot, 1), round(pct(ms, .5), 1), round(pct(ms, .95), 1),
                              round(pct(ms, .99), 1), round(ms[-1], 1), o33, o50, o100, gcs])
                for lim in (33.4, 50.0):
                    c = {}
                    for r in rows:
                        if f(r, "ms") > lim:
                            k, _ = cause(r)
                            c[k] = c.get(k, 0) + 1
                    if c:
                        print(f"    over {lim:.0f} ms, by largest part: " + ", ".join(f"{k} {v}" for k, v in sorted(c.items(), key=lambda kv: -kv[1])))
                # the spike file: the worst frames' systems
                sp = os.path.join(d, tag + "_" + route + "_spikes.txt")
                lines = []
                if os.path.exists(sp):
                    for line in open(sp, encoding="utf-8", errors="replace"):
                        p = line.split(" ", 3)
                        if len(p) >= 3:
                            try:
                                lines.append((float(p[2]), line.rstrip()))
                            except ValueError:
                                pass
                lines.sort(key=lambda t: -t[0])
                for _, line in lines[:worst_n]:
                    print("      " + line[:400])
    if out_csv and table:
        with open(out_csv, "w", newline="") as fh:
            w = csv.writer(fh)
            w.writerow(["run", "route", "frames", "avg_fps", "p50", "p95", "p99", "worst", "over33", "over50", "over100", "gc"])
            w.writerows(table)
        print("wrote " + out_csv)


if __name__ == "__main__":
    main()
