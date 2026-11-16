# Round 3 proof: the owner's video | round 2 (as merged in PC47) | round 3, each at the pose locked to the video,
# PC content (PSXPerf, Windows), the game's noon. Scores and differences from scores_r3.json.
import json, os, sys
from PIL import Image, ImageDraw, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__)); S = os.path.dirname(HERE)
BASE = sys.argv[1] if len(sys.argv) > 1 else 'r3base'
TAG = sys.argv[2] if len(sys.argv) > 2 else 'r3a'
F = ImageFont.truetype(r'C:/Users/mcgee/PSX Racing-lakelure/tools/city/fonts/Overpass-Variable.ttf', 17)
FS = ImageFont.truetype(r'C:/Users/mcgee/PSX Racing-lakelure/tools/city/fonts/Overpass-Variable.ttf', 14)
poses = json.load(open(os.path.join(HERE, 'poses_r3.json')))
scores = json.load(open(os.path.join(HERE, 'scores_r3.json'))) if os.path.exists(os.path.join(HERE, 'scores_r3.json')) else {}
CW, CH = 480, 270
RH = CH + 64
sheet = Image.new('RGB', (3 * CW + 40, 76 + len(poses) * RH), (24, 24, 26)); d = ImageDraw.Draw(sheet)
d.text((10, 6), 'LAKE LURE round 3: the owner\'s 2022 video  |  round 2 (as in PC47)  |  round 3 - the game camera locked to the video\'s spot and heading', font=F, fill=(255, 255, 255))
d.text((10, 30), 'Dash camera solved at 8:15: 68 deg across, 1.4 m up, 1.3 m right of the centre line, pitched up 10.7 deg plus the road grade. PC content (PSXPerf, Windows), the game\'s noon.', font=FS, fill=(255, 200, 160))
d.text((10, 50), 'Score: is it the same place? YES / ALMOST / NO, and what still differs.', font=FS, fill=(255, 200, 160))
def cell(p):
    if p and os.path.exists(p): return Image.open(p).convert('RGB').resize((CW, CH))
    im = Image.new('RGB', (CW, CH), (50, 20, 20)); ImageDraw.Draw(im).text((10, 10), 'missing', font=F, fill=(255, 255, 255)); return im
y = 76
for p in poses:
    n = p['name']; sc = scores.get(n, {})
    early = {'t0945': 6455, 't1620': 10700, 't1650': 11050}.get(n)
    note = f'  [round-2 column shot at the earlier estimate, {early} m]' if early else ''
    d.text((10, y + 2), f"{p['video']}  m {p['m']}  heading {p['heading']:.0f}  pitch {p['pitch']:.1f}{note}  -  {p['evidence'][:110]}", font=FS, fill=(255, 230, 120))
    for k, q in enumerate([os.path.join(S, 'frames', p['frame'] + '.png'),
                           os.path.join(S, 'shots_' + BASE, f'psx_look_LakeLure_{n}.png'),
                           os.path.join(S, 'shots_' + TAG, f'psx_look_LakeLure_{n}.png')]):
        sheet.paste(cell(q), (10 + k * (CW + 10), y + 22))
    if sc:
        col = {'YES': (120, 230, 120), 'ALMOST': (240, 200, 90), 'NO': (240, 110, 110)}.get(sc.get('score', ''), (255, 255, 255))
        d.text((10, y + 22 + CH + 3), f"{sc.get('score', '')}: {sc.get('note', '')}"[:200], font=FS, fill=col)
    y += RH
out = os.path.join(S, f'proof_lakelure_{TAG}_video.png'); sheet.save(out); print(out, sheet.size)
