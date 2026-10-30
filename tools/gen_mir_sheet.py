#!/usr/bin/env python3
"""Original pixel sheet for the playable client. Not Mir WIL/WZL data."""
import json
from pathlib import Path

from PIL import Image

FRAME_W = 32
FRAME_H = 48
LOGICAL_W = 16
LOGICAL_H = 24
DIRS = 5
SLOTS = 10
ACTIONS = ["idle", "walk", "run", "attack", "cast", "hit", "death"]
ACTION_FRAMES = {"idle": 8, "walk": 10, "run": 8, "attack": 10, "cast": 8, "hit": 4, "death": 8}
ACTORS = [
    "war_m", "war_f", "mage_m", "mage_f", "tao_m", "tao_f",
    "fashion_m", "fashion_f", "npc",
    "slime", "boar", "bone", "crawler", "wraith", "dragon",
    "gate", "wall", "flag", "horse", "wolf",
    "arm_cloth", "arm_leather", "arm_plate", "arm_fashion",
    "helm_cloth", "helm_iron",
    "wep_blade", "wep_sword", "wep_iron", "wep_staff",
]
TILES = ["grass", "grass_b", "dirt", "path", "stone", "safe", "water", "wall", "tree", "portal"]
FX = ["slash", "fire", "lightning", "heal", "level", "portal", "buff"]
FX_FRAMES = 6
TILE = 32

OUT = (18, 10, 8, 255)
SKIN = (232, 186, 142, 255)
SKIN_D = (196, 142, 98, 255)
HAIR = (42, 24, 16, 255)
HAIR_F = (92, 42, 28, 255)
BOOT = (46, 30, 20, 255)
SHADOW = (0, 0, 0, 70)
GOLD = (214, 168, 64, 255)
STEEL = (78, 104, 150, 255)
STEEL_D = (42, 60, 96, 255)
CAPE = (128, 28, 26, 255)
MAGE = (86, 62, 150, 255)
MAGE_D = (48, 32, 96, 255)
TAO = (58, 110, 86, 255)
TAO_D = (28, 64, 50, 255)
FASH = (176, 36, 48, 255)
FASH_D = (110, 18, 28, 255)
WHITE = (236, 232, 220, 255)


def palette(actor: str) -> dict:
    female = actor.endswith("_f")
    hair = HAIR_F if female else HAIR
    if actor.startswith("mage"):
        cloth, dark, metal = MAGE, MAGE_D, (186, 170, 230, 255)
    elif actor.startswith("tao"):
        cloth, dark, metal = TAO, TAO_D, (220, 200, 120, 255)
    elif actor.startswith("fashion"):
        cloth, dark, metal = FASH, FASH_D, GOLD
    elif actor == "npc":
        cloth, dark, metal = (120, 78, 42, 255), (72, 44, 24, 255), GOLD
        hair = (70, 48, 28, 255)
    else:
        cloth, dark, metal = STEEL, STEEL_D, GOLD
    return {"cloth": cloth, "dark": dark, "metal": metal, "hair": hair, "female": female, "actor": actor}


def new_canvas() -> Image.Image:
    rows = len(ACTORS) * len(ACTIONS)
    char_h = rows * FRAME_H
    height = char_h + TILE + TILE
    return Image.new("RGBA", (DIRS * SLOTS * FRAME_W, height), (0, 0, 0, 0)), char_h


class Pen:
    def __init__(self, img: Image.Image, ox: int, oy: int):
        self.img = img
        self.ox = ox
        self.oy = oy

    def p(self, lx: int, ly: int, color) -> None:
        if not (0 <= lx < LOGICAL_W and 0 <= ly < LOGICAL_H):
            return
        for dy in range(2):
            for dx in range(2):
                self.img.putpixel((self.ox + lx * 2 + dx, self.oy + ly * 2 + dy), color)

    def block(self, x, y, w, h, fill) -> None:
        for yy in range(h):
            for xx in range(w):
                edge = xx == 0 or yy == 0 or xx == w - 1 or yy == h - 1
                self.p(x + xx, y + yy, OUT if edge else fill)

    def pix(self, x, y, color) -> None:
        self.p(x, y, color)


def facing_name(d: int) -> str:
    return ["back", "ne", "side", "se", "front"][d]


def motion(action: str, frame: int) -> tuple[int, int]:
    import math
    count = ACTION_FRAMES.get(action, 6)
    if action not in ("walk", "run"):
        return 0, 0
    phase = frame / max(1, count) * math.tau
    amp = 3 if action == "run" else 2
    bob = 1 if math.sin(phase * 2) > 0 else 0
    if action == "run":
        bob += 1
    stride = int(round(math.sin(phase) * amp))
    return bob, stride


def layer_kind(actor: str) -> str:
    if actor.startswith("arm_"):
        return "armor"
    if actor.startswith("helm_"):
        return "helm"
    if actor.startswith("wep_"):
        return "weapon"
    return "body"


def draw_human(img, ox, oy, facing, action, frame, actor) -> None:
    pal = palette(actor)
    part = layer_kind(actor)
    if actor.startswith("arm_cloth"):
        pal = {**pal, "cloth": (176, 148, 96, 255), "dark": (120, 92, 54, 255), "metal": GOLD}
    elif actor.startswith("arm_leather"):
        pal = {**pal, "cloth": (112, 68, 36, 255), "dark": (70, 40, 22, 255), "metal": (160, 120, 60, 255)}
    elif actor.startswith("arm_plate"):
        pal = {**pal, "cloth": STEEL, "dark": STEEL_D, "metal": GOLD}
    elif actor.startswith("arm_fashion"):
        pal = {**pal, "cloth": FASH, "dark": FASH_D, "metal": GOLD}
    pen = Pen(img, ox, oy)
    if action == "death" and part == "body":
        draw_corpse(pen, min(frame, 3), pal)
        return
    if action == "death" and part != "body":
        return
    bob, stride = motion(action, frame)
    flinch = 1 if action == "hit" and frame == 0 else 0
    y0 = 1 + bob
    side = facing in ("side", "se", "ne")
    front = facing in ("front", "se")
    back = facing in ("back", "ne")
    shift = -1 if facing in ("ne", "back") else 1 if facing in ("se", "side") else 0
    shift += flinch if facing == "side" else 0

    for lx in range(5, 11):
        if part == "body":
            pen.pix(lx, 22, SHADOW)

    if part in ("helm", "weapon"):
        draw_weapon(pen, actor, facing, action, frame, shift, y0, pal) if part == "weapon" else draw_helmet(pen, actor, shift, y0, pal)
        return

    if part == "armor":
        shoulder = 4
        pen.block(shoulder + shift, 8 + y0, 9, 8, pal["cloth"])
        pen.block(6 + shift, 14 + y0, 4, 2, pal["metal"])
        pen.block(3 + shift, 8 + y0, 3, 3, pal["dark"])
        pen.block(11 + shift, 8 + y0, 3, 3, pal["dark"])
        return

    # legs
    if side:
        pen.block(7 + shift, 15 + max(0, -stride // 2), 3, 6, BOOT)
        pen.block(6 + shift, 14 + max(0, stride // 2), 3, 6, pal["dark"])
    else:
        pen.block(5, 15, 3, 6 + (stride // 2), BOOT)
        pen.block(8, 15, 3, 6 - (stride // 2), BOOT)

    # torso / robe
    shoulder = 5 if pal["female"] else 4
    pen.block(shoulder + shift, 8 + y0, 8 if pal["female"] else 9, 8, pal["cloth"])
    pen.block(6 + shift, 14 + y0, 4, 2, pal["metal"])
    if actor.startswith("war"):
        pen.block(3 + shift, 8 + y0, 3, 3, pal["dark"])
        pen.block(11 + shift, 8 + y0, 3, 3, pal["dark"])
    if actor.startswith("fashion"):
        pen.block(5 + shift, 10 + y0, 6, 2, GOLD)

    # cape on back / 3/4
    if back or actor.startswith("war") or actor.startswith("fashion"):
        cape = CAPE if not actor.startswith("fashion") else FASH_D
        sway = int(round(stride / 3)) if action in ("walk", "run") else 0
        pen.block(4 + shift + sway, 9 + y0, 2, 8, cape)

    # head
    pen.block(6 + shift, 3 + y0, 4, 4, SKIN)
    if front and action != "hit":
        pen.pix(6 + shift, 4 + y0, OUT)
        pen.pix(8 + shift, 4 + y0, OUT)
    if not front:
        pen.block(6 + shift, 3 + y0, 4, 2, pal["hair"])

    # hair / helmet
    if actor.startswith("mage"):
        pen.block(6 + shift, 1 + y0, 4, 3, pal["cloth"])
        pen.pix(7 + shift, 0 + y0, pal["cloth"])
        pen.pix(8 + shift, 0 + y0, GOLD)
    elif actor.startswith("tao"):
        pen.block(5 + shift, 2 + y0, 6, 3, pal["dark"])
    elif actor.startswith("fashion"):
        pen.block(5 + shift, 2 + y0, 6, 3, pal["hair"])
        if pal["female"]:
            pen.block(4 + shift, 5 + y0, 2, 6, pal["hair"])
        pen.pix(7 + shift, 2 + y0, GOLD)
    elif actor == "npc":
        pen.block(5 + shift, 1 + y0, 6, 2, (92, 48, 28, 255))
        pen.block(4 + shift, 3 + y0, 8, 2, (70, 36, 20, 255))
    else:
        pen.block(5 + shift, 2 + y0, 6, 2, pal["hair"])
        if pal["female"]:
            pen.block(4 + shift, 4 + y0, 2, 6, pal["hair"])


def and_helmet(pal):
    return pal["dark"]


def draw_helmet(pen, actor, shift, y0, pal) -> None:
    if "iron" in actor:
        pen.block(5 + shift, 2 + y0, 6, 3, STEEL)
        pen.pix(7 + shift, 1 + y0, GOLD)
        pen.pix(8 + shift, 1 + y0, GOLD)
    else:
        pen.block(5 + shift, 2 + y0, 6, 2, (150, 92, 48, 255))
        pen.pix(7 + shift, 2 + y0, GOLD)


def draw_weapon(pen, actor, facing, action, frame, shift, y0, pal) -> None:
    count = max(1, ACTION_FRAMES.get(action, 6))
    t = frame / count
    if action == "attack":
        arc = [(-2, -6), (-1, -8), (0, -8), (2, -7), (4, -4), (5, -2), (6, 0), (6, 2), (4, 3), (2, 2)]
        dx, dy = arc[min(frame, len(arc) - 1)]
    elif action == "cast":
        dx, dy = (3, -5 + int(t * 3))
    else:
        dx, dy = (4, 0)
    length = 5 if "blade" in actor else 9 if "iron" in actor else 7
    color = (150, 96, 48, 255) if "blade" in actor else (230, 232, 236, 255)
    hx, hy = 11 + shift + dx, 10 + y0 + dy
    if "staff" in actor or actor.startswith("mage") or actor == "npc":
        pen.block(hx, hy, 1, 8, (92, 64, 40, 255))
        pen.pix(hx, hy - 1, GOLD if action != "cast" else (255, 220, 120, 255))
        if action == "cast":
            pen.block(hx - 1, hy - 3, 3, 3, (255, 210, 90, 255) if frame % 2 == 0 else (255, 140, 40, 255))
    elif actor.startswith("tao"):
        pen.pix(8 + shift, 12 + y0, GOLD)
        if action == "cast":
            pen.block(8 + shift, 6 + y0, 3, 3, (140, 230, 190, 255) if frame % 2 == 0 else (80, 180, 140, 255))
        else:
            pen.block(hx, hy, 1, 6, (70, 90, 70, 255))
    else:
        pen.block(hx, hy, 1, length, color)
        pen.pix(hx, hy, GOLD)
        pen.pix(hx - 1, hy, GOLD)
        if action == "attack" and frame in (2, 3, 4):
            pen.pix(hx + 1, hy + 3, (255, 244, 200, 255))
            pen.pix(hx + 2, hy + 4, (255, 220, 140, 180))


def draw_corpse(pen: Pen, frame: int, pal) -> None:
    for lx in range(3, 13):
        pen.pix(lx, 22, SHADOW)
    drop = min(3, frame)
    y = 14 + drop * 2
    pen.block(3, y, 10, 4, pal["cloth"])
    pen.block(11, y + 1, 3, 3, SKIN)
    pen.block(2, y + 2, 3, 2, BOOT)
    if frame >= 3:
        pen.block(4, 18, 9, 3, pal["dark"])


def draw_slime(img, ox, oy, action, frame) -> None:
    pen = Pen(img, ox, oy)
    squash = 1 if action in ("walk", "run", "hit") and frame % 2 == 0 else 0
    if action == "death":
        pen.block(3, 18, 10, 3, (40, 120, 48, 255))
        return
    pen.block(4, 12 + squash, 8, 8 - squash, (64, 168, 62, 255))
    pen.block(6, 14 + squash, 2, 2, (210, 240, 180, 255))
    pen.pix(6, 16 + squash, OUT)
    pen.pix(9, 16 + squash, OUT)
    if action == "attack":
        pen.block(11, 14, 3, 3, (90, 200, 80, 255))


def draw_boar(img, ox, oy, facing, action, frame) -> None:
    pen = Pen(img, ox, oy)
    if action == "death":
        pen.block(2, 16, 12, 4, (120, 64, 40, 255))
        return
    bob = [0, 1, 0, 1][frame % 4]
    body = (138, 78, 46, 255)
    pen.block(3, 12 + bob, 10, 6, body)
    pen.block(11, 13 + bob, 3, 3, (168, 110, 74, 255))
    pen.pix(12, 14 + bob, OUT)
    pen.pix(13, 15 + bob, WHITE)
    pen.block(4, 17 + bob, 2, 3, BOOT)
    pen.block(9, 17 + bob, 2, 3, BOOT)
    if action == "attack":
        pen.block(14, 15, 2, 2, WHITE)


def draw_bone(img, ox, oy, action, frame) -> None:
    pen = Pen(img, ox, oy)
    if action == "death":
        pen.block(3, 18, 10, 2, WHITE)
        pen.pix(6, 17, (200, 40, 40, 255))
        return
    bob = frame % 2
    bone = (230, 226, 210, 255)
    pen.block(6, 6 + bob, 4, 4, bone)
    pen.pix(7, 7 + bob, (180, 30, 30, 255))
    pen.pix(9, 7 + bob, (180, 30, 30, 255))
    pen.block(7, 10 + bob, 2, 6, bone)
    pen.block(5, 11 + bob, 2, 5, bone)
    pen.block(9, 11 + bob, 2, 5, bone)
    pen.block(6, 16 + bob, 2, 4, bone)
    pen.block(8, 16 + bob, 2, 4, bone)
    if action == "attack":
        pen.block(11, 8 + bob, 4, 1, bone)


def draw_crawler(img, ox, oy, action, frame) -> None:
    pen = Pen(img, ox, oy)
    if action == "death":
        pen.block(2, 18, 12, 2, (40, 70, 40, 255))
        return
    wob = [0, 1, 0, -1][frame % 4]
    pen.block(3 + wob, 14, 10, 4, (48, 90, 48, 255))
    pen.pix(5, 15, (220, 40, 40, 255))
    pen.pix(8, 15, (220, 40, 40, 255))
    for leg in range(4):
        pen.pix(4 + leg * 2, 18 + (frame + leg) % 2, OUT)


def draw_wraith(img, ox, oy, action, frame) -> None:
    pen = Pen(img, ox, oy)
    if action == "death":
        pen.block(5, 16, 6, 4, (120, 80, 180, 120))
        return
    glow = (170, 120, 230, 200) if frame % 2 == 0 else (130, 80, 190, 170)
    pen.block(5, 6, 6, 12, glow)
    pen.pix(7, 9, (255, 255, 255, 230))
    pen.pix(9, 9, (255, 255, 255, 230))
    pen.block(6, 16, 4, 3, (100, 60, 150, 140))


def draw_dragon(img, ox, oy, action, frame) -> None:
    pen = Pen(img, ox, oy)
    if action == "death":
        pen.block(1, 16, 14, 4, (30, 90, 40, 255))
        return
    wing = 2 if action in ("attack", "cast") and frame % 2 == 0 else 0
    body = (46, 130, 58, 255)
    pen.block(2, 8 - wing, 4, 6, (70, 160, 80, 255))
    pen.block(10, 8 - wing, 4, 6, (70, 160, 80, 255))
    pen.block(5, 10, 6, 8, body)
    pen.block(10, 8, 4, 4, (60, 150, 70, 255))
    pen.pix(12, 9, (240, 40, 30, 255))
    pen.pix(11, 12, GOLD)
    if action == "attack":
        pen.block(13, 12, 3, 2, (255, 120, 30, 255))


def draw_gate(img, ox, oy, frame) -> None:
    pen = Pen(img, ox, oy)
    stone = (120, 108, 92, 255)
    pen.block(2, 4, 12, 16, stone)
    pen.block(5, 10, 6, 10, (28, 18, 12, 255))
    pen.pix(4, 8, GOLD)
    pen.pix(11, 8, GOLD)
    if frame % 2 == 0:
        pen.pix(7, 14, (255, 200, 80, 255))


def draw_wall(img, ox, oy) -> None:
    pen = Pen(img, ox, oy)
    pen.block(1, 6, 14, 14, (110, 100, 88, 255))
    pen.block(1, 6, 14, 3, (150, 136, 112, 255))
    for i in range(4):
        pen.pix(3 + i * 3, 12, OUT)


def draw_flag(img, ox, oy, frame) -> None:
    pen = Pen(img, ox, oy)
    pen.block(4, 4, 1, 16, (80, 60, 40, 255))
    wave = frame % 3
    pen.block(5, 4, 6 + wave, 4, (180, 32, 28, 255))
    pen.pix(8, 5, GOLD)


def draw_horse(img, ox, oy, facing, action, frame) -> None:
    pen = Pen(img, ox, oy)
    bob = [0, 1, 0, 1][frame % 4] if action in ("walk", "run") else 0
    body = (236, 232, 220, 255)
    pen.block(3, 12 + bob, 9, 5, body)
    pen.block(11, 8 + bob, 3, 4, body)
    pen.pix(12, 9 + bob, OUT)
    pen.block(4, 16 + bob, 2, 4, (90, 70, 50, 255))
    pen.block(9, 16 + bob, 2, 4, (90, 70, 50, 255))
    if action == "run":
        pen.block(2, 13 + bob, 2, 2, body)


def draw_wolf(img, ox, oy, action, frame) -> None:
    pen = Pen(img, ox, oy)
    if action == "death":
        pen.block(3, 17, 10, 3, (70, 70, 78, 255))
        return
    bob = frame % 2
    pen.block(3, 13 + bob, 9, 4, (120, 124, 132, 255))
    pen.block(11, 12 + bob, 3, 3, (150, 154, 160, 255))
    pen.pix(12, 13 + bob, (220, 40, 40, 255))
    pen.block(4, 17, 2, 3, (60, 60, 68, 255))
    pen.block(9, 17, 2, 3, (60, 60, 68, 255))


def draw_actor_frame(img, ox, oy, actor, facing_i, action, frame) -> None:
    facing = facing_name(facing_i)
    if actor in ("war_m", "war_f", "mage_m", "mage_f", "tao_m", "tao_f", "fashion_m", "fashion_f", "npc"):
        draw_human(img, ox, oy, facing, action, frame, actor)
    elif actor == "slime":
        draw_slime(img, ox, oy, action, frame)
    elif actor == "boar":
        draw_boar(img, ox, oy, facing, action, frame)
    elif actor == "bone":
        draw_bone(img, ox, oy, action, frame)
    elif actor == "crawler":
        draw_crawler(img, ox, oy, action, frame)
    elif actor == "wraith":
        draw_wraith(img, ox, oy, action, frame)
    elif actor == "dragon":
        draw_dragon(img, ox, oy, action, frame)
    elif actor == "gate":
        draw_gate(img, ox, oy, frame)
    elif actor == "wall":
        draw_wall(img, ox, oy)
    elif actor == "flag":
        draw_flag(img, ox, oy, frame)
    elif actor == "horse":
        draw_horse(img, ox, oy, facing, action, frame)
    elif actor == "wolf":
        draw_wolf(img, ox, oy, action, frame)


def blit_tile(img, index, painter) -> None:
    tile_y = len(ACTORS) * len(ACTIONS) * FRAME_H
    ox = index * TILE
    painter(img, ox, tile_y)


def paint_grass(img, ox, oy, alt=False) -> None:
    base = (58, 110, 48, 255) if not alt else (48, 96, 42, 255)
    for y in range(TILE):
        for x in range(TILE):
            img.putpixel((ox + x, oy + y), base)
    tufts = (86, 150, 62, 255)
    for x, y in ((4, 6), (14, 18), (22, 10), (8, 24), (26, 22)):
        img.putpixel((ox + x, oy + y), tufts)
        img.putpixel((ox + x, oy + y + 1), (30, 70, 28, 255))


def paint_dirt(img, ox, oy) -> None:
    base = (112, 84, 52, 255)
    for y in range(TILE):
        for x in range(TILE):
            img.putpixel((ox + x, oy + y), base)
    for x, y in ((6, 8), (18, 14), (10, 22), (24, 6)):
        img.putpixel((ox + x, oy + y), (86, 60, 36, 255))


def paint_path(img, ox, oy) -> None:
    base = (150, 132, 96, 255)
    for y in range(TILE):
        for x in range(TILE):
            img.putpixel((ox + x, oy + y), base)
    for x in range(0, TILE, 8):
        for y in range(TILE):
            img.putpixel((ox + x, oy + y), (120, 100, 70, 255))


def paint_stone(img, ox, oy) -> None:
    for y in range(TILE):
        for x in range(TILE):
            brick = ((x // 8) + (y // 8)) % 2 == 0
            img.putpixel((ox + x, oy + y), (132, 124, 112, 255) if brick else (108, 100, 90, 255))
            if x % 8 == 0 or y % 8 == 0:
                img.putpixel((ox + x, oy + y), (80, 72, 64, 255))


def paint_safe(img, ox, oy) -> None:
    for y in range(TILE):
        for x in range(TILE):
            img.putpixel((ox + x, oy + y), (168, 150, 110, 255))
            if (x + y) % 9 == 0:
                img.putpixel((ox + x, oy + y), (196, 170, 90, 255))


def paint_water(img, ox, oy) -> None:
    for y in range(TILE):
        for x in range(TILE):
            wave = (x + y) % 6 == 0
            img.putpixel((ox + x, oy + y), (48, 110, 150, 255) if not wave else (120, 190, 210, 255))


def paint_wall_tile(img, ox, oy) -> None:
    for y in range(TILE):
        for x in range(TILE):
            img.putpixel((ox + x, oy + y), (92, 84, 74, 255))
            if y < 6:
                img.putpixel((ox + x, oy + y), (140, 126, 104, 255))
            if x % 10 == 0:
                img.putpixel((ox + x, oy + y), (60, 52, 44, 255))


def paint_tree(img, ox, oy) -> None:
    for y in range(TILE):
        for x in range(TILE):
            img.putpixel((ox + x, oy + y), (0, 0, 0, 0))
    for y in range(8, 18):
        for x in range(8, 24):
            if (x - 16) ** 2 + (y - 12) ** 2 < 70:
                img.putpixel((ox + x, oy + y), (36, 110, 40, 255))
    for y in range(18, 30):
        img.putpixel((ox + 15, oy + y), (86, 58, 32, 255))
        img.putpixel((ox + 16, oy + y), (110, 74, 40, 255))


def paint_portal_tile(img, ox, oy) -> None:
    for y in range(TILE):
        for x in range(TILE):
            img.putpixel((ox + x, oy + y), (40, 70, 120, 255))
    for y in range(6, 26):
        for x in range(10, 22):
            img.putpixel((ox + x, oy + y), (80, 200, 220, 255) if (x + y) % 3 else (20, 40, 80, 255))


def paint_fx(img, name, index, frame) -> None:
    fx_y = len(ACTORS) * len(ACTIONS) * FRAME_H + TILE
    ox = (index * FX_FRAMES + frame) * TILE
    oy = fx_y
    for y in range(TILE):
        for x in range(TILE):
            img.putpixel((ox + x, oy + y), (0, 0, 0, 0))

    def dot(x, y, c):
        if 0 <= x < TILE and 0 <= y < TILE:
            img.putpixel((ox + x, oy + y), c)

    cx = cy = 16
    if name == "slash":
        color = (255, 236, 180, 230)
        for i in range(10 + frame * 2):
            dot(6 + i, 22 - i // 2 - frame, color)
            dot(7 + i, 21 - i // 2 - frame, (255, 255, 255, 200))
    elif name == "fire":
        for r in range(4 + frame, 0, -1):
            col = (255, 80 + frame * 20, 20, 220) if r > 2 else (255, 220, 80, 240)
            for a in range(r * 2):
                dot(cx - r + a, cy - r // 2, col)
                dot(cx, cy - r + a // 2, col)
    elif name == "lightning":
        col = (180, 220, 255, 240)
        x = 16
        for y in range(2, 30):
            x += 1 if ((y + frame) % 3 == 0) else -1
            dot(x, y, col)
            dot(x + 1, y, (255, 255, 255, 255))
    elif name == "heal":
        col = (120, 230, 170, 230)
        for i in range(-6, 7):
            dot(cx + i, cy, col)
            dot(cx, cy + i, col)
        if frame % 2 == 0:
            dot(cx, cy - 8, (255, 255, 255, 255))
    elif name == "level":
        col = (255, 210, 70, 240)
        for i in range(8):
            dot(cx - 8 + i * 2, cy - 8 + frame, col)
            dot(cx, cy - 10 + i, col)
    elif name == "portal":
        col = (80, 210, 230, 230)
        rad = 6 + frame
        for a in range(rad * 4):
            dot(cx - rad + (a % (rad * 2)), cy - 2, col)
        dot(cx, cy, (230, 255, 255, 255))
    elif name == "buff":
        col = (240, 200, 80, 255) if frame % 2 == 0 else (120, 180, 255, 255)
        for i in range(5):
            dot(cx - 2 + i, cy - 2, col)
            dot(cx, cy - 4 + i, col)


def fill(im, x, y, w, h, color) -> None:
    for yy in range(h):
        for xx in range(w):
            im.putpixel((x + xx, y + yy), color)


def frame_box(im, x, y, w, h, fill_c, edge) -> None:
    fill(im, x, y, w, h, fill_c)
    for i in range(w):
        im.putpixel((x + i, y), edge)
        im.putpixel((x + i, y + h - 1), edge)
        if h > 3:
            im.putpixel((x + i, y + 1), (90, 62, 28, 255))
            im.putpixel((x + i, y + h - 2), (40, 24, 12, 255))
    for i in range(h):
        im.putpixel((x, y + i), edge)
        im.putpixel((x + w - 1, y + i), edge)


def paint_ui(path: Path) -> None:
    im = Image.new("RGBA", (512, 360), (0, 0, 0, 0))
    wood = (62, 38, 22, 255)
    wood_d = (36, 22, 14, 255)
    edge = (214, 168, 64, 255)
    frame_box(im, 0, 0, 80, 44, wood, edge)
    frame_box(im, 8, 8, 64, 28, wood_d, (120, 86, 40, 255))
    frame_box(im, 0, 48, 180, 18, (42, 12, 12, 255), edge)
    frame_box(im, 0, 70, 180, 18, (12, 18, 48, 255), edge)
    frame_box(im, 0, 96, 512, 110, wood, edge)
    for x in range(16, 500, 36):
        im.putpixel((x, 104), edge)
        im.putpixel((x + 1, 104), edge)
    frame_box(im, 200, 0, 150, 150, wood, edge)
    frame_box(im, 208, 16, 134, 126, (18, 16, 14, 255), (120, 86, 40, 255))
    frame_box(im, 360, 160, 148, 190, wood, edge)
    frame_box(im, 368, 176, 132, 166, wood_d, (120, 86, 40, 255))
    im.save(path, optimize=True)
    print(f"wrote {path} bytes={path.stat().st_size}")


def main() -> None:
    img, char_h = new_canvas()
    for ai, actor in enumerate(ACTORS):
        for ani, action in enumerate(ACTIONS):
            usable = ACTION_FRAMES[action]
            for d in range(DIRS):
                for f in range(SLOTS):
                    ox = (d * SLOTS + f) * FRAME_W
                    oy = (ai * len(ACTIONS) + ani) * FRAME_H
                    src = min(f, usable - 1)
                    draw_actor_frame(img, ox, oy, actor, d, action, src)

    painters = [
        lambda im, x, y: paint_grass(im, x, y, False),
        lambda im, x, y: paint_grass(im, x, y, True),
        paint_dirt,
        paint_path,
        paint_stone,
        paint_safe,
        paint_water,
        paint_wall_tile,
        paint_tree,
        paint_portal_tile,
    ]
    for i, painter in enumerate(painters):
        blit_tile(img, i, painter)
    for i, name in enumerate(FX):
        for f in range(FX_FRAMES):
            paint_fx(img, name, i, f)

    root = Path(__file__).resolve().parents[1]
    out_png = root / "client/Legend.Playable/Assets/atlas.png"
    out_json = root / "client/Legend.Playable/Assets/atlas.json"
    img.save(out_png, optimize=True)
    meta = {
        "frameW": FRAME_W,
        "frameH": FRAME_H,
        "dirSlots": DIRS,
        "framesPerDir": SLOTS,
        "actions": ACTIONS,
        "actionFrames": ACTION_FRAMES,
        "actors": ACTORS,
        "tileSize": TILE,
        "tileY": char_h,
        "tiles": TILES,
        "fxY": char_h + TILE,
        "fx": FX,
        "fxFrames": FX_FRAMES,
        "fxSize": TILE,
        "note": "Original sheet generated by tools/gen_mir_sheet.py. Not official Mir art.",
    }
    out_json.write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"wrote {out_png} {img.size} bytes={out_png.stat().st_size}")
    paint_ui(root / "client/Legend.Playable/Assets/ui_chrome.png")


if __name__ == "__main__":
    main()
