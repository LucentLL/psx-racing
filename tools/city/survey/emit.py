"""ROAD SURVEY: write a measures file (provider survey) for chosen junctions: their arms' and inside edges' stations
(survey.py rules S1-S9) and the junction lines (junctions.py J1-J5): returns that fit, noses of grass medians.

  py -I tools/city/survey/emit.py GPKG GRAPH.json STATIONS.pkl JUNCTIONS.pkl OUT.txt NAME:node,node NAME:node ...
"""
import sys, os, pickle
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import survey as SV

def main(gpkg, graph, stp, jp, out, specs):
    sv = SV.Survey(gpkg, graph, os.path.dirname(os.path.abspath(graph)))
    R = pickle.load(open(stp, "rb")); J = pickle.load(open(jp, "rb"))
    edges, blocks = [], []
    for spec in specs:
        name, nodes = spec.split(":"); want = set(int(v) for v in nodes.split(","))
        js = [j for j in J if want & set(j["nodes"])]
        for j in js:
            for ei in [a["e"] for a in j.get("arms", [])] + j.get("inner", []):
                if ei not in edges and ei in R: edges.append(ei)
            b = ["junction %s nodes %s   # survey: centre x %.1f z %.1f" % (name, " ".join(map(str, j["nodes"])), j["x"], j["z"])]
            for x in j.get("noses", []):
                if x["status"] == "hole" and "capA" in x:
                    b.append("nose %d %d %.2f %.2f %.2f %.2f r %.1f kind %s   # median end %.1f m out (rule %.1f m)" % (
                        x["a"], x["b"], x["capA"][0], x["capA"][1], x["capB"][0], x["capB"][1], x["r"], x["kind"], x["dS"], x["dR"]))
                else: b.append("# no nose %d %d: %s (painted / concrete / no median: the layer cannot say)" % (x["a"], x["b"], x["status"]))
            for x in j.get("returns", []):
                if x["status"] == "fit": b.append("return %d %d r %.1f   # rms %.2f m over %.0f deg (rule %.0f)" % (x["a"], x["b"], x["r"], x["rms"], x["span"], x["rule"]))
                else: b.append("# no return %d %d: %s%s" % (x["a"], x["b"], x["status"], (" r %.1f rms %.2f" % (x["r"], x["rms"])) if np.isfinite(x["r"]) else ""))
            blocks.append(b)
    SV.write_measures(sv, R, out, edges, "Junctions: " + " ".join(specs))
    with open(out, "a", newline="\n") as f:
        for b in blocks: f.write("\n".join(b) + "\n")
    print("wrote", out, len(edges), "edges", len(blocks), "junctions")

if __name__ == "__main__":
    main(*sys.argv[1:6], sys.argv[6:])
