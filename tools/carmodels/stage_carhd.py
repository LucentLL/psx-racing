# Stage the artist's HD car deliveries in the game's names (the HD car tier's
# source folder, PSX_CAR_HD_SRC). One set per GAME LIVERY SHEET:
#   <body>__<sheet>_hd.png          colour, sRGB, alpha = material class (128 matte / 255 reflective)
#   <body>__<sheet>_hd_normal.png   tangent normal, +Y (OpenGL)
#   <body>__<sheet>_hd_orm.png      linear R = AO, G = roughness, B = metallic, A = clear coat (255 when not delivered)
# and one lamp map per body:
#   <body>_lightmask_hd.png         linear R running, G brake, B projector core, A lens scatter
# The lamp map takes the in-game 512 map's corrections (the 11/11 import zeroed
# texels / derived A): wherever the 512 map disagrees with the HD map's 4x4 box,
# that HD block is scaled (or filled) to the 512 value.
# Usage: py -I stage_carhd.py <repo> <cars dir> <stage dir>
import sys, os, json, shutil
import numpy as np
from PIL import Image

repo, cars, stage = sys.argv[1], sys.argv[2], sys.argv[3]
os.makedirs(stage, exist_ok=True)
MODELS = os.path.join(repo, 'Assets', 'PSXRacing', 'Art', 'Car', 'Models')
LAMPS = os.path.join(repo, 'Assets', 'PSXRacing', 'Resources', 'CarLights')

FD = 'FD_Shared_Independent_v01/Textured_17/'
VIP = 'Viper_GTS_RearOptics_v13/'
CTRB = 'CTR_Blue_HD_Spoilerless_v05/Maps/'
CTRY = 'CTR_Yellowbird_HD_v01/Maps/'
EVO = 'EvoVI_HD_Textured_v17/'
EG = 'EG_Independent_Textured_v21/'
S2K = 'S2000_Independent_Textured_v12/'
# body, game sheet, delivery stem, surface kind ('orm' RGB AO/rough/metal, 'surface' RGBA coat/rough/metal/AO, 'ctr' RGB AO/rough/metal)
SETS = [
    ('rx7_fd_93', 'fd_shared_512x256', FD + 'fd_1993_down', 'orm'),
    ('rx7_fd_99', 'fd_shared_512x256', FD + 'fd_1999_down', 'orm'),
    ('rx7_fd_nowing', 'fd_shared_512x256', FD + 'fd_none_down', 'orm'),
    ('viper_gts', 'coupe_96_blue_white_512x256', VIP + 'viper_blue_white', 'surface'),
    ('viper_gts', 'coupe_96_red_512x256', VIP + 'viper_red', 'surface'),
    ('flatsix_coupe', 'flatsix_coupe_blue_512x256', CTRB + 'ctr_blue', 'ctr'),
    ('flatsix_coupe_87', 'flatsix_coupe_87_yellow_512x256', CTRY + 'ctr_yellowbird', 'ctr'),
    ('rally_sedan_99', 'rally_sedan_99_blue_512x256', EVO + 'evo_vi', 'surface'),
    ('civic_eg', 'hatch_93_red_512x256', EG + 'eg_red', 'surface'),
    ('roadster_99', 'roadster_99_red_512x256', S2K + 's2000_red', 'surface'),
]
LAMPMAPS = {
    'rx7_fd_93': FD + 'FD_LightMask_RGB.png', 'rx7_fd_99': FD + 'FD_LightMask_RGB.png', 'rx7_fd_nowing': FD + 'FD_LightMask_RGB.png',
    'viper_gts': VIP + 'Viper_LightMask_RGB.png',
    'flatsix_coupe': CTRB + 'ctr_blue_overhaul1_hd_lights_rgb.png',
    'flatsix_coupe_87': CTRY + 'ctr_yellowbird_overhaul1_hd_lights_rgb.png',
    'rally_sedan_99': EVO + 'Lighting/EvoVI_LightMask_RGB.png',
    'civic_eg': EG + 'Civic_LightMask_RGB.png',
    'roadster_99': S2K + 'Lighting/S2000_LightMask_RGB.png',
}

def load(p, mode=None):
    im = Image.open(p)
    if mode: im = im.convert(mode)
    return np.asarray(im)

def box4(a):
    h, w = a.shape[0] // 4, a.shape[1] // 4
    return a.reshape(h, 4, w, 4, *a.shape[2:]).astype(np.float64).mean(axis=(1, 3))

def save(arr, path, mode):
    Image.fromarray(arr.astype(np.uint8), mode).save(path, optimize=False, compress_level=6)

report = {'sets': [], 'lamps': []}
for body, sheet, stem, kind in SETS:
    key = body + '__' + sheet
    src = os.path.join(cars, stem)
    col = load(src + '_overhaul1_hd.png', 'RGBA')
    game = load(os.path.join(MODELS, body, 'textures', sheet + '.png'), 'RGBA').astype(np.float64)
    d = np.abs(box4(col)[..., :3] - game[..., :3])
    shutil.copyfile(src + '_overhaul1_hd.png', os.path.join(stage, key + '_hd.png'))
    shutil.copyfile(src + '_overhaul1_hd_normal.png', os.path.join(stage, key + '_hd_normal.png'))
    H, W = col.shape[:2]
    if kind == 'surface':
        s = load(src + '_overhaul1_hd_surface.png', 'RGBA')
        orm = np.stack([s[..., 3], s[..., 1], s[..., 2], s[..., 0]], -1)
    else:
        s = load(src + ('_overhaul1_hd_orm.png' if kind == 'orm' else '_overhaul1_hd_surface.png'), 'RGB')
        orm = np.concatenate([s, np.full((H, W, 1), 255, np.uint8)], -1)
    save(orm, os.path.join(stage, key + '_hd_orm.png'), 'RGBA')
    # paint stats: strongly coloured, reflective-class texels
    c = col[..., :3].astype(np.float64) / 255
    hi, lo = c.max(-1), c.min(-1)
    paint = ((hi - lo) / np.maximum(hi, 0.08) > 0.45) & (hi > 0.25) & (col[..., 3] > 200)
    st = {k: float(np.median(orm[..., i][paint])) / 255 for i, k in enumerate(['ao', 'rough', 'metal', 'coat'])} if paint.any() else {}
    report['sets'].append({'key': key, 'src': stem, 'size': [W, H], 'game_vs_box4_mean': float(d.mean()), 'game_vs_box4_max': float(d.max()),
                           'paint_share': float(paint.mean()), 'paint_median': st,
                           'alpha_values': sorted({int(v) for v in np.unique(col[..., 3])})[:8]})

for body, rel in LAMPMAPS.items():
    hd = load(os.path.join(cars, rel), 'RGBA').astype(np.float64)
    if Image.open(os.path.join(cars, rel)).mode in ('RGB', 'L'):
        hd[..., 3] = 0
    game = load(os.path.join(LAMPS, body + '_lightmask.png'), 'RGBA').astype(np.float64)
    b = box4(hd)
    fixed = hd.copy()
    changed = 0
    for ch in range(4):
        diff = np.abs(b[..., ch] - game[..., ch]) > 2.5
        ys, xs = np.nonzero(diff)
        for y, x in zip(ys, xs):
            blk = fixed[y * 4:y * 4 + 4, x * 4:x * 4 + 4, ch]
            g = game[y, x, ch]
            if g <= 0.5: blk[:] = 0
            elif ch == 3 and b[y, x, 2] > 0.5: blk[:] = fixed[y * 4:y * 4 + 4, x * 4:x * 4 + 4, 2] * (g / b[y, x, 2])
            elif b[y, x, ch] > 0.5: blk[:] = blk * (g / b[y, x, ch])
            else: blk[:] = g
            changed += 1
    fixed = np.clip(np.round(fixed), 0, 255)
    after = np.abs(box4(fixed) - game).max()
    save(fixed, os.path.join(stage, body + '_lightmask_hd.png'), 'RGBA')
    report['lamps'].append({'body': body, 'src': rel, 'size': list(hd.shape[1::-1]), 'blocks_fixed': changed, 'max_diff_after_vs_512': float(after)})

json.dump(report, open(os.path.join(stage, 'stage_manifest.json'), 'w'), indent=1)
print(json.dumps(report, indent=1))
