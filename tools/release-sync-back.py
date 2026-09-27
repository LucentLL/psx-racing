"""Bring the sandbox's import settings back into the source project.

ReleaseBudget (Editor/ReleaseBudget.cs) sets the WebGL texture overrides, the
prop models' tangents and mesh compression, and the renderers' post data in
the SANDBOX before every player build. Those settings live in .meta files and
two renderer assets; without this they exist only in the sandbox, a mirror
(verify.ps1 without -NoMirror) puts the source's old ones back, and every
publish re-imports ~900 textures.

Copies a sandbox .meta over the source's only when the asset it describes
exists in the source and the two differ (so a stale sandbox meta for a file
the source no longer has is never resurrected), plus the new scripts' metas
and the two renderer assets. Prints what it copied.

    py tools/release-sync-back.py            (dry run)
    py tools/release-sync-back.py --write
"""
import os, sys, filecmp, shutil

SRC = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SANDBOX = r'C:\Users\mcgee\PSXBuild'
write = '--write' in sys.argv
copied = []

def sync(rel_dir, only_meta=True):
    sroot = os.path.join(SANDBOX, rel_dir)
    for d, _, files in os.walk(sroot):
        for f in files:
            if only_meta and not f.endswith('.meta'):
                continue
            sp = os.path.join(d, f)
            rel = os.path.relpath(sp, SANDBOX)
            dp = os.path.join(SRC, rel)
            asset = dp[:-5] if f.endswith('.meta') else dp
            if not os.path.exists(asset):
                continue                      # no such asset in source
            if os.path.exists(dp) and filecmp.cmp(sp, dp, shallow=False):
                continue
            copied.append(rel)
            if write:
                shutil.copy2(sp, dp)

for rel in (r'Assets\PSXRacing\Art', r'Assets\PSXRacing\Resources',
            r'Assets\PSXRacing\Editor', r'Assets\PSXRacing\Scripts'):
    sync(rel)
for rel in (r'Assets\Settings\Mobile_Renderer.asset', r'Assets\Settings\PC_Renderer.asset'):
    sp, dp = os.path.join(SANDBOX, rel), os.path.join(SRC, rel)
    if os.path.exists(sp) and not filecmp.cmp(sp, dp, shallow=False):
        copied.append(rel)
        if write:
            shutil.copy2(sp, dp)

kinds = {}
for c in copied:
    k = c.split(os.sep)[2] if c.count(os.sep) > 2 else c
    kinds[k] = kinds.get(k, 0) + 1
print(('copied' if write else 'would copy'), len(copied), kinds)
for c in copied:
    if not c.endswith('.meta') or 'Editor' in c or 'Scripts' in c:
        print('  ', c)
