"""Copy tools/ffb/pidffb.js into Assets/Plugins/WebGL/PSXForceFeedback.jslib.

The in-game force feedback and the standalone test page must send the SAME
reports, so the protocol engine lives in one file (pidffb.js) and the jslib
carries a copy between its PIDFFB-BEGIN / PIDFFB-END marker lines.

    py tools/ffb/sync-jslib.py          # rewrite the block
    py tools/ffb/sync-jslib.py --check  # exit 1 if the jslib is out of date
"""
import io, os, sys

here = os.path.dirname(os.path.abspath(__file__))
src = os.path.join(here, "pidffb.js")
dst = os.path.join(here, "..", "..", "Assets", "Plugins", "WebGL", "PSXForceFeedback.jslib")

with io.open(src, encoding="utf-8") as f:
    engine = f.read().replace("\r\n", "\n").rstrip("\n") + "\n"
with io.open(dst, encoding="utf-8") as f:
    text = f.read().replace("\r\n", "\n")

lines = text.split("\n")
b = next(i for i, l in enumerate(lines) if l.startswith("// PIDFFB-BEGIN"))
e = next(i for i, l in enumerate(lines) if l.startswith("// PIDFFB-END"))
out = "\n".join(lines[:b + 1]) + "\n" + engine + "\n".join(lines[e:])
if "--check" in sys.argv:
    sys.exit(0 if out == text else 1)
with io.open(dst, "w", encoding="utf-8", newline="\n") as f:
    f.write(out)
print("synced %d lines of pidffb.js into %s" % (engine.count("\n"), os.path.normpath(dst)))
