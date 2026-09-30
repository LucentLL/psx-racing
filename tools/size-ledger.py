"""size-ledger.py - what the download costs, without Unity (plan section 2.4, WP-01).

    py tools/size-ledger.py                       # the sandbox's last build, if any
    py tools/size-ledger.py --build <dir> --log <unity build log> --json out.json

Reports:
  BUILD    every file of a WebGL build (<dir>/Build/*), in MiB. FAILS (exit 1)
           when any is over 95 MiB - the plan's ratchet under GitHub's hard
           100 MiB file limit (WebGL.data.unityweb was 99.59 MiB once).
  DATA     each Charlotte data file in Resources: raw bytes and Brotli
           (quality 11, window 24, via node - a proxy for what the file adds
           to the Brotli-compressed data file).
  REPORT   the Build Report split Unity writes to the build log ("Build
           Report" block: bytes by category, and the Resources files that
           ship, Charlotte's picked out). Absent if no log is given/found.
  GIT      what Charlotte's data costs the public repo (plan critic C45):
           every committed version of the data and truth files, by blob, and
           the repo's pack size. Commit data only at G-ship.

Nothing is written unless --json is given. The sandbox is $PSX_SANDBOX
(default C:\\Users\\mcgee\\PSXBuild); this script only READS it.
"""
import argparse, json, os, re, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
MIB = 1024 * 1024
LIMIT = 95 * MIB
DATA = ['charlotte_city.bytes', 'charlotte_dem.bytes', 'charlotte_bld.bytes', 'charlotte_routes.json', 'charlotte_canopy.bytes', 'charlotte_signs.bytes']


def git(*args):
    return subprocess.run(['git', '-C', REPO, *args], capture_output=True, text=True, encoding='utf-8', errors='replace').stdout


def build_files(build):
    d = os.path.join(build, 'Build')
    if not os.path.isdir(d):
        return None
    rows = []
    for n in sorted(os.listdir(d)):
        p = os.path.join(d, n)
        if os.path.isfile(p):
            rows.append({'file': n, 'bytes': os.path.getsize(p)})
    for extra in ('index.html',):
        p = os.path.join(build, extra)
        if os.path.isfile(p):
            rows.append({'file': extra, 'bytes': os.path.getsize(p)})
    return rows


def build_report(log):
    """The 'Build Report' block Unity prints after a player build."""
    if not log or not os.path.isfile(log):
        return None
    lines = open(log, encoding='utf-8', errors='replace').read().splitlines()
    starts = [i for i, l in enumerate(lines) if l.strip() == 'Build Report']
    if not starts:
        return None
    i = starts[-1] + 1
    cats, used = {}, []
    unit = {'kb': 1024, 'mb': MIB, 'gb': 1024 * MIB, 'b': 1}
    rx_cat = re.compile(r'^(\S[\w ]+?)\s+([\d.]+) (kb|mb|gb|b)\b')
    rx_used = re.compile(r'^\s*([\d.]+) (kb|mb|gb|b)\s+([\d.]+)% (.+)$')
    mode = 'cat'
    for l in lines[i:]:
        if l.startswith('Used Assets'):
            mode = 'used'
            continue
        if mode == 'cat':
            m = rx_cat.match(l)
            if m:
                cats[m.group(1).strip()] = round(float(m.group(2)) * unit[m.group(3)])
        else:
            m = rx_used.match(l)
            if not m:
                if used:
                    break
                continue
            used.append({'asset': m.group(4).strip(), 'bytes': round(float(m.group(1)) * unit[m.group(2)])})
    city = [u for u in used if re.search(r'charlotte_|/City/|CityProps|city_', u['asset'], re.I)]
    return {'log': log, 'categories': cats, 'used_assets': len(used), 'charlotte': city,
            'charlotte_bytes': sum(u['bytes'] for u in city)}


def git_growth():
    paths = ['Assets/PSXRacing/Resources/' + n for n in DATA] + ['tools/city/truth']
    objs = git('rev-list', '--objects', '--all', '--', *paths).split('\n')
    blobs = {}
    for l in objs:
        parts = l.split(' ', 1)
        if len(parts) == 2 and parts[1]:
            blobs[parts[0]] = parts[1]
    if not blobs:
        return None
    r = subprocess.run(['git', '-C', REPO, 'cat-file', '--batch-check=%(objectname) %(objecttype) %(objectsize) %(objectsize:disk)'],
                       input='\n'.join(blobs) + '\n', capture_output=True, text=True)
    per = {}
    for l in r.stdout.splitlines():
        oid, typ, size, disk = l.split()
        if typ != 'blob':
            continue
        p = blobs[oid]
        e = per.setdefault(p, {'versions': 0, 'bytes': 0, 'disk': 0})
        e['versions'] += 1
        e['bytes'] += int(size)
        e['disk'] += int(disk)
    pack = {}
    for l in git('count-objects', '-v').splitlines():
        k, _, v = l.partition(':')
        if k in ('size-pack', 'size'):
            pack[k + '_kib'] = int(v.strip())
    return {'paths': per, 'total_disk': sum(e['disk'] for e in per.values()), **pack}


def main():
    ap = argparse.ArgumentParser()
    sandbox = os.environ.get('PSX_SANDBOX') or r'C:\Users\mcgee\PSXBuild'
    ap.add_argument('--build', default=os.path.join(sandbox, 'Build', 'WebGL'))
    ap.add_argument('--log', default=None, help='Unity log of the build (default: <sandbox>/build.log)')
    ap.add_argument('--data', default=os.path.join(REPO, 'Assets', 'PSXRacing', 'Resources'))
    ap.add_argument('--json', default=None)
    a = ap.parse_args()
    log = a.log or os.path.join(sandbox, 'build.log')
    R = {'sandbox': sandbox}
    failed = False

    rows = build_files(a.build)
    print(f'BUILD  {a.build}')
    if rows is None:
        print('  no WebGL build there (build with tools/build-and-publish.ps1, or pass --build)')
    else:
        for r in rows:
            flag = '  OVER 95 MiB' if r['bytes'] > LIMIT else ''
            print(f"  {r['file']:32s} {r['bytes'] / MIB:8.2f} MiB{flag}")
            failed |= r['bytes'] > LIMIT
        R['build'] = {'dir': a.build, 'files': rows, 'largest_mib': round(max(r['bytes'] for r in rows) / MIB, 2)}

    files = [os.path.join(a.data, n) for n in DATA if os.path.isfile(os.path.join(a.data, n))]
    br = json.loads(subprocess.run(['node', os.path.join(HERE, 'city', 'lib', 'brsize.mjs'), *files],
                                   capture_output=True, text=True, check=True).stdout)
    print(f'DATA   {a.data}  (Brotli q11)')
    R['data'] = {}
    tr = tb = 0
    for f in files:
        n = os.path.basename(f)
        v = br[f]
        R['data'][n] = v
        tr += v['raw']; tb += v['brotli']
        print(f"  {n:32s} {v['raw'] / 1e6:7.3f} MB raw  {v['brotli'] / 1e6:7.3f} MB brotli")
    print(f"  {'total':32s} {tr / 1e6:7.3f} MB raw  {tb / 1e6:7.3f} MB brotli")
    R['data_total'] = {'raw': tr, 'brotli': tb}

    rep = build_report(log)
    print(f'REPORT {log}')
    if rep is None:
        print('  no Build Report block in that log')
    else:
        R['build_report'] = rep
        for k, v in rep['categories'].items():
            print(f'  {k:24s} {v / MIB:8.2f} MiB')
        print(f"  Charlotte data + city art in the build: {len(rep['charlotte'])}, {rep['charlotte_bytes'] / MIB:.2f} MiB uncompressed")
        for u in rep['charlotte'][:12]:
            print(f"    {u['bytes'] / MIB:6.2f} MiB  {u['asset']}")

    gg = git_growth()
    print('GIT    (every committed version, by path; disk = packed)')
    if gg:
        R['git'] = gg
        for p, e in sorted(gg['paths'].items()):
            print(f"  {p:52s} {e['versions']:3d} versions  {e['bytes'] / 1e6:7.2f} MB raw  {e['disk'] / 1e6:6.2f} MB packed")
        print(f"  Charlotte data + truth in the repo: {gg['total_disk'] / 1e6:.2f} MB packed; repo pack {gg.get('size-pack_kib', 0) / 1024:.0f} MiB")

    if a.json:
        with open(a.json, 'w', encoding='utf-8', newline='\n') as f:
            json.dump(R, f, indent=1)
            f.write('\n')
        print('wrote', a.json)
    if failed:
        print('SIZE LEDGER: FAILED - a build file is over 95 MiB')
        sys.exit(1)
    print('SIZE LEDGER OK')


if __name__ == '__main__':
    main()
