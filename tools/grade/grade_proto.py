# The film grade, prototyped on display-space pixels exactly as PSX/Blit will do it
# (gamma space, before the quantizer). Same constants, same order of operations.
import sys, numpy as np
from PIL import Image, ImageFilter

LIFT  = np.array([0.112, 0.108, 0.104], np.float32)   # the matte floor: faded print black, a touch warm
CEIL  = np.array([0.915, 0.935, 0.915], np.float32)   # the cream ceiling: never paper white, a breath of green
SAT_COOL, SAT_WARM = 0.72, 0.96                       # blues and greens fade; reds, oranges, yellows hold
CONTRAST = 0.22                                       # how much of an S is mixed into the middle
WARM_MID = 0.022                                      # mids lean amber
GREEN_TO_OLIVE = 0.10                                 # foliage greens pulled toward olive
GLOW_KNEE, GLOW_GAIN = 0.78, 0.55
GLOW_TINT = np.array([1.00, 0.86, 0.66], np.float32)
VIGNETTE = 0.20

def grade(a, glow_radius_px):
    c = a.copy()
    # halation: what is brighter than the knee bleeds, warm
    bright = np.clip(c - GLOW_KNEE, 0, None)
    im = Image.fromarray((np.clip(bright, 0, 1) * 255).astype(np.uint8))
    blur = np.asarray(im.filter(ImageFilter.GaussianBlur(glow_radius_px))).astype(np.float32) / 255.0
    c = c + blur * GLOW_GAIN * GLOW_TINT
    # greens toward olive
    l = (c * np.array([0.299, 0.587, 0.114], np.float32)).sum(-1, keepdims=True)
    greenness = np.clip((c[..., 1:2] - np.maximum(c[..., 0:1], c[..., 2:3])) * 3.0, 0, 1)
    c[..., 0:1] += greenness * GREEN_TO_OLIVE * c[..., 1:2]
    c[..., 2:3] -= greenness * GREEN_TO_OLIVE * 0.5 * c[..., 1:2]
    # saturation by warmth
    l = (c * np.array([0.299, 0.587, 0.114], np.float32)).sum(-1, keepdims=True)
    warm = np.clip((c[..., 0:1] - c[..., 2:3]) * 2.5, 0, 1)
    sat = SAT_COOL + (SAT_WARM - SAT_COOL) * warm
    c = l + (c - l) * sat
    c = np.clip(c, 0, 1)
    # a little S in the middle
    s = c * c * (3 - 2 * c)
    c = c + (s - c) * CONTRAST
    # warm mids
    l = (c * np.array([0.299, 0.587, 0.114], np.float32)).sum(-1, keepdims=True)
    mid = 4 * l * (1 - l)
    c[..., 0:1] += WARM_MID * mid
    c[..., 2:3] -= WARM_MID * mid
    # the floor and the ceiling
    c = LIFT + (CEIL - LIFT) * np.clip(c, 0, 1)
    # vignette
    h, w = c.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    u = (xx / (w - 1) - 0.5) * 2; v = (yy / (h - 1) - 0.5) * 2
    r2 = (u * u + v * v) * 0.5
    vig = 1 - VIGNETTE * np.clip((r2 - 0.25) / 0.75, 0, 1) ** 1.5
    c = LIFT + (c - LIFT) * vig[..., None]
    return np.clip(c, 0, 1)

def stats(name, a):
    Y = (a * np.array([0.299, 0.587, 0.114], np.float32)).sum(-1)
    mx = a.max(-1); mn = a.min(-1)
    sat = np.where(mx > 1e-4, (mx - mn) / np.maximum(mx, 1e-4), 0)
    p = np.percentile(Y, [0.1, 1, 5, 25, 50, 75, 95, 99, 99.9])
    hi = a[Y >= np.percentile(Y, 97)].mean(0); lo = a[Y <= np.percentile(Y, 5)].mean(0)
    print(f"{name:>10}  Y .1/1/5/25/50/75/95/99/99.9 = " + " ".join(f"{v:.3f}" for v in p) +
          f"   sat {sat.mean():.3f}   lo {lo[0]:.3f} {lo[1]:.3f} {lo[2]:.3f}   hi {hi[0]:.3f} {hi[1]:.3f} {hi[2]:.3f}")

if __name__ == "__main__":
    src, out = sys.argv[1], sys.argv[2]
    im = Image.open(src).convert("RGB")
    a = np.asarray(im).astype(np.float32) / 255.0
    stats("before", a)
    g = grade(a, glow_radius_px=im.size[1] * 0.018)
    stats("after", g)
    both = np.concatenate([a, g], axis=0)
    Image.fromarray((both * 255 + 0.5).astype(np.uint8)).save(out)
