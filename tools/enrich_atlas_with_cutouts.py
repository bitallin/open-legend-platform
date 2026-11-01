#!/usr/bin/env python3
"""Stamp AI cutouts into the playable atlas (post gen_mir_sheet)."""
from __future__ import annotations

import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from mir_cutouts import (  # noqa: E402
    load_war_poses,
    load_class_row,
    load_mob_poses,
    load_props,
    animate_base,
    flip_h,
    fit_frame,
    to_rgba_knock_black,
    equal_bands,
)

ASSETS = ROOT / "client/Legend.Playable/Assets"
SHOTS = ROOT / "docs/client/shots"


def pose_pick(poses: dict, action: str):
    if action == "idle":
        return poses.get("idle")
    if action in ("walk", "run"):
        return poses.get("walk") or poses.get("idle")
    if action == "attack":
        return poses.get("attack") or poses.get("idle")
    if action == "cast":
        return poses.get("cast") or poses.get("attack") or poses.get("idle")
    if action in ("hit", "death"):
        return poses.get("hit") or poses.get("idle")
    return poses.get("idle")


def clear_rect(img: Image.Image, ox: int, oy: int, w: int, h: int) -> None:
    ImageDraw.Draw(img).rectangle([ox, oy, ox + w - 1, oy + h - 1], fill=(0, 0, 0, 0))


def equal_rows(im: Image.Image, count: int):
    W, H = im.size
    h = H // count
    return [im.crop((0, i * h, W, (i + 1) * h if i < count - 1 else H)) for i in range(count)]


def build_map() -> dict:
    war = load_war_poses()
    classes = load_class_row()
    mobs = load_mob_poses()
    m = {
        "war_m": war,
        "fashion_m": war,
        "war_f": {k: classes.get("war_f") for k in ("idle", "walk", "attack", "cast", "hit")},
        "fashion_f": {k: classes.get("war_f") for k in ("idle", "walk", "attack", "cast", "hit")},
        "mage_f": {k: classes.get("mage_f") for k in ("idle", "walk", "attack", "cast", "hit")},
        "mage_m": {k: classes.get("mage_f") for k in ("idle", "walk", "attack", "cast", "hit")},
        "tao_m": {k: classes.get("tao_m") for k in ("idle", "walk", "attack", "cast", "hit")},
        "tao_f": {k: classes.get("tao_m") for k in ("idle", "walk", "attack", "cast", "hit")},
        "npc": {k: classes.get("npc") for k in ("idle", "walk", "attack", "cast", "hit")},
    }
    for name, poses in mobs.items():
        m[name] = poses
    # extra mob sheet 2x3
    extra_path = SHOTS / "mir-mob-extra.jpg"
    if extra_path.exists():
        extra = to_rgba_knock_black(Image.open(extra_path), 30)
        W, H = extra.size
        cw, ch = W // 3, H // 2
        for actor, (c, r0) in {
            "crawler": (0, 0),
            "wraith": (1, 0),
            "dragon": (2, 0),
        }.items():
            idle = extra.crop((c * cw, 0, (c + 1) * cw, ch))
            atk = extra.crop((c * cw, ch, (c + 1) * cw, 2 * ch))
            m[actor] = {"idle": idle, "walk": idle, "attack": atk, "cast": atk, "hit": atk}
    return m


def stamp_fx(img: Image.Image, meta: dict) -> None:
    fy, fs, ff = meta["fxY"], meta["fxSize"], meta["fxFrames"]
    fx_names = meta["fx"]

    def paste(name, frames):
        if name not in fx_names:
            return
        i = fx_names.index(name)
        for f, fr in enumerate(frames[:ff]):
            ox = (i * ff + f) * fs
            clear_rect(img, ox, fy, fs, fs)
            img.paste(fr, (ox, fy), fr)

    misc = SHOTS / "mir-fx-misc.jpg"
    if misc.exists():
        bands = equal_bands(to_rgba_knock_black(Image.open(misc), 28), 6)
        mapping = [("fire", 0), ("lightning", 1), ("heal", 2), ("level", 3), ("portal", 4), ("buff", 5)]
        for name, bi in mapping:
            base = bands[bi]
            frames = []
            for f in range(ff):
                scale = 0.78 + 0.07 * ((f % 3) + 1)
                w = max(10, int(fs * scale))
                part = fit_frame(base, w, w, foot_pad=0)
                canvas = Image.new("RGBA", (fs, fs), (0, 0, 0, 0))
                canvas.paste(part, ((fs - w) // 2, (fs - w) // 2), part)
                if f >= 4:
                    a = canvas.split()[3].point(lambda v: int(v * 0.5))
                    canvas.putalpha(a)
                frames.append(canvas)
            paste(name, frames)
    slash = SHOTS / "mir-fx-slash.jpg"
    if slash.exists():
        rows = equal_rows(to_rgba_knock_black(Image.open(slash), 24), 6)
        paste("slash", [fit_frame(r, fs, fs, foot_pad=0) for r in rows])


def main() -> None:
    png = ASSETS / "atlas.png"
    meta_path = ASSETS / "atlas.json"
    meta = json.loads(meta_path.read_text(encoding="utf-8"))
    img = Image.open(png).convert("RGBA")
    fw, fh = meta["frameW"], meta["frameH"]
    dirs, slots = meta["dirSlots"], meta["framesPerDir"]
    actors = meta["actors"]
    actions = meta["actions"]
    action_frames = meta["actionFrames"]
    ai = build_map()
    stamped = 0
    for ai_name, poses in ai.items():
        if ai_name not in actors:
            continue
        ai_index = actors.index(ai_name)
        for ani, action in enumerate(actions):
            usable = action_frames.get(action, 1)
            for d in range(dirs):
                for f in range(slots):
                    src_f = min(f, usable - 1)
                    base = pose_pick(poses, action)
                    if base is None:
                        continue
                    framed = animate_base(base, action, src_f, fw, fh)
                    if d == 1:
                        framed = flip_h(framed)
                    ox = (d * slots + f) * fw
                    oy = (ai_index * len(actions) + ani) * fh
                    clear_rect(img, ox, oy, fw, fh)
                    img.paste(framed, (ox, oy), framed)
                    stamped += 1
    props = load_props()
    if props.get("tree") and "tree" in meta["tiles"]:
        ti = meta["tiles"].index("tree")
        tile = fit_frame(props["tree"], meta["tileSize"], meta["tileSize"], foot_pad=2)
        clear_rect(img, ti * meta["tileSize"], meta["tileY"], meta["tileSize"], meta["tileSize"])
        img.paste(tile, (ti * meta["tileSize"], meta["tileY"]), tile)
    if props.get("wall_prop") and "wall" in actors:
        wi = actors.index("wall")
        for ani, action in enumerate(actions):
            for d in range(dirs):
                for f in range(slots):
                    framed = animate_base(props["wall_prop"], "idle", 0, fw, fh)
                    ox = (d * slots + f) * fw
                    oy = (wi * len(actions) + ani) * fh
                    clear_rect(img, ox, oy, fw, fh)
                    img.paste(framed, (ox, oy), framed)
    if props.get("house") and "gate" in actors:
        gi = actors.index("gate")
        for ani, action in enumerate(actions):
            for d in range(dirs):
                for f in range(slots):
                    framed = animate_base(props["house"], "idle", 0, fw, fh)
                    ox = (d * slots + f) * fw
                    oy = (gi * len(actions) + ani) * fh
                    clear_rect(img, ox, oy, fw, fh)
                    img.paste(framed, (ox, oy), framed)
    stamp_fx(img, meta)
    img.save(png, optimize=True)
    meta["note"] = "Original sheet + AI cutout enrichment (actors/FX/props). Not official Mir art."
    meta_path.write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"enriched {png} stamped_frames={stamped} size={img.size} bytes={png.stat().st_size}")


if __name__ == "__main__":
    main()
