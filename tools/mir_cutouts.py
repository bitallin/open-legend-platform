#!/usr/bin/env python3
"""Extract sprite cutouts from AI reference sheets (black background)."""
from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
SHOTS = ROOT / "docs/client/shots"


def to_rgba_knock_black(im: Image.Image, thresh: int = 30) -> Image.Image:
    im = im.convert("RGBA")
    px = im.load()
    w, h = im.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if r <= thresh and g <= thresh and b <= thresh:
                px[x, y] = (0, 0, 0, 0)
    return im


def content_bbox(im: Image.Image, alpha_min: int = 8):
    px = im.load()
    w, h = im.size
    minx, miny, maxx, maxy = w, h, -1, -1
    for y in range(h):
        for x in range(w):
            if px[x, y][3] > alpha_min:
                if x < minx:
                    minx = x
                if y < miny:
                    miny = y
                if x > maxx:
                    maxx = x
                if y > maxy:
                    maxy = y
    if maxx < 0:
        return None
    pad = 1
    return (max(0, minx - pad), max(0, miny - pad), min(w, maxx + 1 + pad), min(h, maxy + 1 + pad))


def equal_bands(im: Image.Image, count: int) -> list[Image.Image]:
    im = to_rgba_knock_black(im)
    w, h = im.size
    sprites = []
    for i in range(count):
        x0 = int(i * w / count)
        x1 = int((i + 1) * w / count)
        crop = im.crop((x0, 0, x1, h))
        bb = content_bbox(crop)
        if not bb:
            sprites.append(None)
            continue
        sprites.append(crop.crop(bb))
    return sprites


def fit_frame(src: Image.Image | None, fw: int, fh: int, foot_pad: int = 5) -> Image.Image:
    out = Image.new("RGBA", (fw, fh), (0, 0, 0, 0))
    if src is None:
        return out
    src = src.convert("RGBA")
    max_w = fw - 2
    max_h = fh - foot_pad - 1
    sw, sh = src.size
    scale = min(max_w / max(1, sw), max_h / max(1, sh))
    nw = max(1, int(sw * scale))
    nh = max(1, int(sh * scale))
    resized = src.resize((nw, nh), Image.Resampling.LANCZOS)
    resized = resized.filter(ImageFilter.UnsharpMask(radius=1.0, percent=110, threshold=2))
    x = (fw - nw) // 2
    y = fh - foot_pad - nh
    out.paste(resized, (x, y), resized)
    return out


def bob_frame(frame: Image.Image, dy: int) -> Image.Image:
    out = Image.new("RGBA", frame.size, (0, 0, 0, 0))
    out.paste(frame, (0, dy), frame)
    return out


def shift_frame(frame: Image.Image, dx: int, dy: int = 0) -> Image.Image:
    out = Image.new("RGBA", frame.size, (0, 0, 0, 0))
    out.paste(frame, (dx, dy), frame)
    return out


def tint_frame(frame: Image.Image, rgba=(255, 90, 90, 100)) -> Image.Image:
    out = frame.copy()
    overlay = Image.new("RGBA", frame.size, rgba)
    mask = frame.split()[-1]
    tinted = Image.composite(overlay, Image.new("RGBA", frame.size, (0, 0, 0, 0)), mask)
    return Image.alpha_composite(out, tinted)


def fall_frame(frame: Image.Image, amount: float) -> Image.Image:
    rotated = frame.rotate(-75 * amount, expand=True, resample=Image.Resampling.BICUBIC)
    return fit_frame(rotated, frame.size[0], frame.size[1], foot_pad=2)


def flip_h(frame: Image.Image) -> Image.Image:
    return frame.transpose(Image.Transpose.FLIP_LEFT_RIGHT)



def orient_frame(frame: Image.Image, dir_i: int) -> Image.Image:
    """Synthesize a distinct 8-dir facing from a mostly-front/side base.
    0 N,1 NE,2 E,3 SE,4 S,5 SW,6 W,7 NW — avoids pure runtime mirroring.
    """
    from PIL import ImageOps, ImageEnhance
    d = dir_i % 8
    im = frame.convert("RGBA")
    w, h = im.size
    # Start from facing-specific mirror so west-side dirs are not identical flips of east at runtime
    if d in (5, 6, 7):
        im = ImageOps.mirror(im)
    # Direction-unique shear / scale / tint
    shear = {
        0: 0.00, 1: 0.12, 2: 0.18, 3: 0.10,
        4: 0.00, 5: -0.10, 6: -0.18, 7: -0.12,
    }[d]
    scale_x = {0: 0.88, 1: 0.94, 2: 1.0, 3: 0.96, 4: 1.0, 5: 0.96, 6: 1.0, 7: 0.94}[d]
    # back views darker / slightly smaller
    bright = {0: 0.72, 1: 0.88, 2: 0.95, 3: 1.0, 4: 1.05, 5: 1.0, 6: 0.95, 7: 0.88}[d]
    # affine shear around center
    nw = max(8, int(w * scale_x))
    scaled = im.resize((nw, h), Image.Resampling.NEAREST)
    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    canvas.paste(scaled, ((w - nw) // 2, 0), scaled)
    # shear via transform
    sh = shear
    # x' = x + sh*(y-cy)
    cy = h / 2
    coeffs = (1, sh, -sh * cy, 0, 1, 0)
    sheared = canvas.transform((w, h), Image.Transform.AFFINE, coeffs, resample=Image.Resampling.NEAREST)
    # brightness
    rgb = sheared.convert("RGB")
    rgb = ImageEnhance.Brightness(rgb).enhance(bright)
    out = rgb.convert("RGBA")
    out.putalpha(sheared.split()[3])
    # north: slight vertical squash for back silhouette
    if d == 0:
        back = out.resize((w, max(8, int(h * 0.92))), Image.Resampling.NEAREST)
        pad = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        pad.paste(back, (0, h - back.size[1]), back)
        out = pad
    return out


def load_gear_layers() -> dict[str, Image.Image | None]:
    path = SHOTS / "mir-gear-layers.jpg"
    if not path.exists():
        return {}
    im = to_rgba_knock_black(Image.open(path), 28)
    W, H = im.size
    # 2 cols x 4 rows
    cw, ch = W // 2, H // 4
    names_left = ["arm_cloth", "arm_leather", "arm_plate", "arm_fashion"]
    names_right = ["wep_sword", "wep_iron", "wep_staff", "wep_blade"]
    out = {}
    for i, name in enumerate(names_left):
        out[name] = im.crop((0, i * ch, cw, (i + 1) * ch))
    for i, name in enumerate(names_right):
        out[name] = im.crop((cw, i * ch, W, (i + 1) * ch))
    return out

def load_war_poses() -> dict[str, Image.Image | None]:
    path = SHOTS / "mir-war-poses.jpg"
    if not path.exists():
        return {}
    sprites = equal_bands(Image.open(path), 5)
    return {
        "idle": sprites[0],
        "walk": sprites[1],
        "attack": sprites[2],
        "cast": sprites[3],
        "hit": sprites[4],
    }


def load_class_row() -> dict[str, Image.Image | None]:
    path = SHOTS / "mir-classes-npc.jpg"
    if not path.exists():
        return {}
    sprites = equal_bands(Image.open(path), 4)
    names = ["mage_f", "tao_m", "npc", "war_f"]
    return {n: sprites[i] for i, n in enumerate(names)}



def load_mobs_distinct() -> dict[str, dict[str, Image.Image | None]]:
    """Prefer high-contrast distinct slime/boar/bone sheet (3 horizontal bands)."""
    path = SHOTS / "mir-mobs-distinct.jpg"
    if not path.exists():
        return {}
    bands = equal_bands(Image.open(path), 3)
    names = ["slime", "boar", "bone"]
    out = {}
    for name, band in zip(names, bands):
        out[name] = {"idle": band, "walk": band, "attack": band, "cast": band, "hit": band}
    return out


def load_village_decor() -> dict[str, Image.Image | None]:
    """4x3 village tile/prop sheet -> named cutouts."""
    path = SHOTS / "mir-village-decor.jpg"
    if not path.exists():
        return {}
    im = to_rgba_knock_black(Image.open(path), 28)
    W, H = im.size
    cols, rows = 4, 3
    cw, ch = W // cols, H // rows
    names = [
        "grass", "grass_b", "dirt", "path",
        "stone", "flowers", "water", "fence",
        "lamp", "bush", "campfire", "rocks",
    ]
    out = {}
    for i, name in enumerate(names):
        c, r = i % cols, i // cols
        out[name] = im.crop((c * cw, r * ch, (c + 1) * cw, (r + 1) * ch))
    return out

def load_mob_poses() -> dict[str, dict[str, Image.Image | None]]:
    path = SHOTS / "mir-mob-poses.jpg"
    if not path.exists():
        return {}
    sprites = equal_bands(Image.open(path), 6)
    return {
        "slime": {"idle": sprites[0], "attack": sprites[1]},
        "boar": {"idle": sprites[2], "attack": sprites[3]},
        "bone": {"idle": sprites[4], "attack": sprites[5]},
    }


def load_props() -> dict[str, Image.Image | None]:
    path = SHOTS / "mir-props.jpg"
    if not path.exists():
        return {}
    im = to_rgba_knock_black(Image.open(path))
    w, h = im.size
    top = equal_bands(im.crop((0, 0, w, h // 2)), 3)
    bot = equal_bands(im.crop((0, h // 2, w, h)), 3)
    out = {}
    for n, s in zip(["tree", "wall_prop", "house"], top):
        out[n] = s
    for n, s in zip(["campfire", "rocks", "bush"], bot):
        out[n] = s
    return out


def animate_base(base: Image.Image | None, action: str, frame: int, fw: int, fh: int) -> Image.Image:
    fitted = fit_frame(base, fw, fh)
    if base is None:
        return fitted
    if action in ("walk", "run"):
        bob = (0, -1, 0, 1, 0, -1, 0, 1, 0, -1)[frame % 10]
        sway = (0, 1, 0, -1, 0, 1, 0, -1, 0, 1)[frame % 10]
        if action == "run":
            bob *= 2
            sway *= 2
        return shift_frame(bob_frame(fitted, bob), sway)
    if action == "idle":
        bob = (0, 0, -1, 0, 0, 0, -1, 0)[frame % 8]
        return bob_frame(fitted, bob)
    if action == "hit":
        return tint_frame(shift_frame(fitted, 2 if frame % 2 == 0 else -1))
    if action == "death":
        return fall_frame(fitted, min(1.0, (frame + 1) / 7))
    if action in ("attack", "cast"):
        # slight lean into strike
        dx = (0, -1, -2, 1, 2, 3, 2, 1, 0, 0)[frame % 10]
        return shift_frame(fitted, dx)
    return fitted


if __name__ == "__main__":
    out = SHOTS / "_cutouts"
    out.mkdir(exist_ok=True)
    war = load_war_poses()
    for k, v in war.items():
        fit_frame(v, 48, 64).save(out / f"war_{k}.png")
        print("war", k, None if v is None else v.size)
    for k, v in load_class_row().items():
        fit_frame(v, 48, 64).save(out / f"{k}.png")
        print("class", k, None if v is None else v.size)
    for mk, poses in load_mob_poses().items():
        for ak, v in poses.items():
            fit_frame(v, 48, 64).save(out / f"{mk}_{ak}.png")
            print("mob", mk, ak, None if v is None else v.size)
    for k, v in load_props().items():
        fit_frame(v, 48, 64).save(out / f"prop_{k}.png")
        print("prop", k, None if v is None else v.size)
