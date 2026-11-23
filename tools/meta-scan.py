# metascan.py <worktree>: tracked assets without a tracked .meta, tracked .meta without an asset, duplicate GUIDs (index = staged tree)
import subprocess,sys,os,collections
wt=sys.argv[1]
fs=set(subprocess.run(['git','-C',wt,'ls-files','Assets'],capture_output=True,text=True,encoding='utf-8').stdout.split('\n'))-{''}
ign={'Assets/StreamingAssets'}  # its folder meta is gitignored on purpose (.gitignore:70)
dirs=set()
for f in fs:
    p=f.split('/')
    for i in range(1,len(p)): dirs.add('/'.join(p[:i]))
miss=[f for f in sorted(fs|dirs) if f!='Assets' and not f.endswith('.meta') and f+'.meta' not in fs and f not in ign]
orph=[f for f in sorted(fs) if f.endswith('.meta') and f[:-5] not in fs and f[:-5] not in dirs]
g=collections.defaultdict(list)
for f in fs:
    if f.endswith('.meta'):
        for line in open(os.path.join(wt,f),encoding='utf-8',errors='replace'):
            if line.startswith('guid:'): g[line.split()[1].strip()].append(f);break
dup={k:v for k,v in g.items() if len(v)>1}
print(f"tracked files {len(fs)}, metas {sum(len(v) for v in g.values())}: asset without .meta {len(miss)}, .meta without asset {len(orph)}, duplicate GUIDs {len(dup)}")
for x in miss[:20]: print('  NO META',x)
for x in orph[:20]: print('  ORPHAN',x)
for k,v in list(dup.items())[:20]: print('  DUP',k,v)
sys.exit(1 if (miss or orph or dup) else 0)
