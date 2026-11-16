# The locked video poses (round 3): station along the stage line (or an absolute pose), lane offset, height, yaw,
# the dash camera's mount pitch + the road's grade, horizontal FOV. Writes poses_r3.json (with the evidence) and
# looks_r3.txt (PSX_LOOK entries: name:eye:target:vfov).
import json, math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import overlay as O
HFOV = 68.0
POSES = [
  ('t0120', '1:20', 'v0080', 518, 0, 'road curving LEFT round the wooded hill at 560-600 m; Tryon Bay at the right edge; stone wall right (the 68 m round-1 spot was 450 m short: there the road runs straight north for 500 m)'),
  ('t0305', '3:05', 'v0185', 1750, 0, 'time model only (11.5 m/s between the 1:20 and 8:15 fixes); +/-100 m'),
  ('t0600', '6:00', 'v0360', 3650, 0, 'lake through the trees at the right, the road bending left; time model +/-60 m'),
  ('t0743', '7:43', 'v0463', 4900, 0, 'road edges converge on the downtown flat; the far buildings and the inn box sit on the video\'s skyline (+/-20 m)'),
  ('t0815', '8:15', 'v0495', 5310, -1.5, 'SOLVED: the inn\'s ends at x 160 / 383 of 640 and its base on y 270; the double yellow and the crosswalk line up (+/-5 m)'),
  ('t0845', '8:45', 'v0525', 5650, 0, 'road straight to the bend, W-beam right over the marina; the far skyline (+/-30 m)'),
  ('t0945', '9:45', 'v0585', 6468, 0, 'the white flat-roofed shop (#43) spans x 285-425 of 640, about 37 m off by its height; its gravel lot in front; the road bending left (+/-10 m)'),
  ('t1005', '10:05', 'v0605', 6640, 0, 'the house with the flag on the right at x 365-395; no building on the left (+/-30 m)'),
  ('t1125', '11:25', 'v0685', 7440, 0, 'Main Street: the porch building left, the green gable fronts and the log shop right (+/-25 m)'),
  ('t1410', '14:10', 'v0850', 9352, 0, 'round-1 estimate kept (+/-150 m), gorge'),
  ('t1620', '16:20', 'v0980', 10500, 0, 'the road bending LEFT ahead at 10,700 m, the trailer house far left of centre, signs and the gravel pull-off on the right (+/-40 m)'),
  ('t1650', '16:50', 'v1010', 10930, 0, 'the houses ahead centre, the fenced house on the left, nothing near on the right (+/-40 m)'),
]
out, looks = [], []
vfov = 2 * math.degrees(math.atan(math.tan(math.radians(HFOV / 2)) * 9 / 16))
for name, t, fr, m, yaw, ev in POSES:
    ps = O.pose(f'm={m} yaw={yaw}')
    cx, cy, cz = ps['c']; f = ps['f']; p = ps['pitch']
    tx, ty, tz = cx + f[0] * math.cos(p) * 100, cy + math.sin(p) * 100, cz + f[1] * math.cos(p) * 100
    out.append(dict(name=name, video=t, frame=fr, m=m, lat=1.3, height=1.4, yaw=yaw, heading=round(ps['hdg'], 1),
                    pitch=round(math.degrees(p), 2), hfov=HFOV, vfov=round(vfov, 2),
                    eye=[round(cx, 2), round(cy, 2), round(cz, 2)], target=[round(tx, 2), round(ty, 2), round(tz, 2)], evidence=ev))
    looks.append(f'{name}:{cx:.2f},{cy:.2f},{cz:.2f}:{tx:.2f},{ty:.2f},{tz:.2f}:{vfov:.2f}')
here = os.path.dirname(os.path.abspath(__file__))
json.dump(out, open(os.path.join(here, 'poses_r3.json'), 'w'), indent=1)
open(os.path.join(here, 'looks_r3.txt'), 'w').write(';'.join(looks))
print(len(out), 'poses; vfov %.2f' % vfov)
