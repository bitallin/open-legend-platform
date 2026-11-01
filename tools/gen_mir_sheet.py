#!/usr/bin/env python3
"""Original higher-fidelity Mir-style sheet for the playable client.
Not Mir WIL/WZL data. Frames 48x64 with shading; tiles may sample docs refs.
"""
from __future__ import annotations

import json
import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageEnhance

FRAME_W = 48
FRAME_H = 64
DIRS = 8
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
TILES = ["grass", "grass_b", "grass_c", "dirt", "path", "stone", "safe", "water", "flowers", "fence", "lamp", "bush", "rocks", "wall", "tree", "portal"]
FX = ["slash", "fire", "lightning", "heal", "level", "portal", "buff"]
FX_FRAMES = 6
TILE = 48

OUT = (18, 10, 8, 255)
SKIN = (232, 186, 142, 255)
SKIN_D = (196, 142, 98, 255)
SKIN_L = (245, 210, 175, 255)
HAIR = (42, 24, 16, 255)
HAIR_F = (92, 42, 28, 255)
BOOT = (46, 30, 20, 255)
SHADOW = (0, 0, 0, 90)
GOLD = (214, 168, 64, 255)
STEEL = (78, 104, 150, 255)
STEEL_D = (42, 60, 96, 255)
STEEL_L = (140, 170, 210, 255)
CAPE = (128, 28, 26, 255)
MAGE = (86, 62, 150, 255)
MAGE_D = (48, 32, 96, 255)
TAO = (58, 110, 86, 255)
TAO_D = (28, 64, 50, 255)
FASH = (176, 36, 48, 255)
FASH_D = (110, 18, 28, 255)
WHITE = (236, 232, 220, 255)
ROOT = Path(__file__).resolve().parents[1]


def clamp(v, lo, hi):
    return lo if v < lo else hi if v > hi else v


def facing_name(d: int) -> str:
    # 8 isometric facings: N NE E SE S SW W NW
    return ["back", "ne", "side", "se", "front", "sw", "west", "nw"][d % 8]


def palette(actor: str) -> dict:
    female = actor.endswith("_f")
    hair = HAIR_F if female else HAIR
    if actor.startswith("mage"):
        cloth, dark, light, metal = MAGE, MAGE_D, (150, 120, 210, 255), (186, 170, 230, 255)
    elif actor.startswith("tao"):
        cloth, dark, light, metal = TAO, TAO_D, (110, 170, 130, 255), (220, 200, 120, 255)
    elif actor.startswith("fashion"):
        cloth, dark, light, metal = FASH, FASH_D, (220, 90, 100, 255), GOLD
    elif actor == "npc":
        cloth, dark, light, metal = (120, 78, 42, 255), (72, 44, 24, 255), (160, 110, 70, 255), GOLD
        hair = (70, 48, 28, 255)
    else:
        cloth, dark, light, metal = STEEL, STEEL_D, STEEL_L, GOLD
    return {
        "cloth": cloth, "dark": dark, "light": light, "metal": metal,
        "hair": hair, "female": female, "actor": actor,
    }


def new_canvas() -> tuple[Image.Image, int]:
    rows = len(ACTORS) * len(ACTIONS)
    char_h = rows * FRAME_H
    height = char_h + TILE + TILE
    return Image.new("RGBA", (DIRS * SLOTS * FRAME_W, height), (0, 0, 0, 0)), char_h


class Canvas:
    def __init__(self, img: Image.Image, ox: int, oy: int):
        self.img = img
        self.ox = ox
        self.oy = oy
        self.draw = ImageDraw.Draw(img, "RGBA")

    def pix(self, x, y, c):
        if 0 <= x < FRAME_W and 0 <= y < FRAME_H:
            self.img.putpixel((self.ox + x, self.oy + y), c)

    def rect(self, x, y, w, h, fill, outline=None):
        if w <= 0 or h <= 0:
            return
        x0, y0 = self.ox + x, self.oy + y
        self.draw.rectangle([x0, y0, x0 + w - 1, y0 + h - 1], fill=fill, outline=outline)

    def ellipse(self, x, y, w, h, fill, outline=None):
        if w <= 0 or h <= 0:
            return
        x0, y0 = self.ox + x, self.oy + y
        self.draw.ellipse([x0, y0, x0 + w - 1, y0 + h - 1], fill=fill, outline=outline)

    def line(self, x0, y0, x1, y1, fill, width=1):
        self.draw.line(
            [self.ox + x0, self.oy + y0, self.ox + x1, self.oy + y1],
            fill=fill, width=width,
        )

    def poly(self, pts, fill, outline=None):
        shifted = [(self.ox + x, self.oy + y) for x, y in pts]
        self.draw.polygon(shifted, fill=fill, outline=outline)


def motion(action: str, frame: int) -> tuple[int, int]:
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
        pal = {**pal, "cloth": (176, 148, 96, 255), "dark": (120, 92, 54, 255), "light": (210, 180, 120, 255), "metal": GOLD}
    elif actor.startswith("arm_leather"):
        pal = {**pal, "cloth": (112, 68, 36, 255), "dark": (70, 40, 22, 255), "light": (150, 100, 60, 255), "metal": (160, 120, 60, 255)}
    elif actor.startswith("arm_plate"):
        pal = {**pal, "cloth": STEEL, "dark": STEEL_D, "light": STEEL_L, "metal": GOLD}
    elif actor.startswith("arm_fashion"):
        pal = {**pal, "cloth": FASH, "dark": FASH_D, "light": (220, 90, 100, 255), "metal": GOLD}

    c = Canvas(img, ox, oy)
    if action == "death" and part == "body":
        draw_corpse(c, min(frame, 3), pal)
        return
    if action == "death" and part != "body":
        return

    bob, stride = motion(action, frame)
    flinch = 2 if action == "hit" and frame < 2 else 0
    y0 = 8 + bob
    side = facing in ("side", "se", "ne", "sw", "west", "nw")
    front = facing in ("front", "se", "sw")
    back = facing in ("back", "ne", "nw")
    shift = -2 if facing in ("ne", "back", "nw") else 2 if facing in ("se", "side", "sw", "west") else 0
    if facing in ("west", "sw", "nw"):
        shift = -abs(shift) if facing != "sw" else -1
    shift += flinch if facing == "side" else 0
    cx = 24 + shift

    # shadow
    if part == "body":
        c.ellipse(cx - 10, 56, 20, 7, SHADOW)

    if part in ("helm", "weapon"):
        if part == "weapon":
            draw_weapon(c, actor, facing, action, frame, cx, y0, pal)
        else:
            draw_helmet(c, actor, cx, y0, pal)
        return

    if part == "armor":
        draw_torso(c, pal, cx, y0, front, back, side, True)
        return

    # legs
    leg_w = 5 if pal["female"] else 6
    if side:
        c.rect(cx - 2, y0 + 28 + max(0, -stride), leg_w, 14, BOOT, OUT)
        c.rect(cx - 6, y0 + 28 + max(0, stride), leg_w, 14, pal["dark"], OUT)
        c.rect(cx - 1, y0 + 30 + max(0, -stride), 3, 4, pal["light"])
    else:
        c.rect(cx - 7, y0 + 28, leg_w, 14 + max(0, stride // 2), BOOT, OUT)
        c.rect(cx + 1, y0 + 28, leg_w, 14 - min(0, stride // 2), BOOT, OUT)
        c.rect(cx - 6, y0 + 36, 3, 3, (70, 48, 30, 255))
        c.rect(cx + 2, y0 + 36, 3, 3, (70, 48, 30, 255))

    draw_torso(c, pal, cx, y0, front, back, side, False)

    # cape
    if back or actor.startswith("war") or actor.startswith("fashion"):
        cape = CAPE if not actor.startswith("fashion") else FASH_D
        sway = int(round(stride / 2)) if action in ("walk", "run") else 0
        c.poly([
            (cx - 10 + sway, y0 + 12),
            (cx - 4, y0 + 10),
            (cx - 6 + sway, y0 + 34),
            (cx - 14 + sway, y0 + 32),
        ], cape, OUT)

    # head
    hx, hy = cx - 6, y0 + 2
    c.ellipse(hx, hy, 12, 12, SKIN, OUT)
    c.ellipse(hx + 2, hy + 2, 8, 6, SKIN_L)
    if front and action != "hit":
        c.pix(hx + 3, hy + 5, OUT)
        c.pix(hx + 8, hy + 5, OUT)
        c.pix(hx + 5, hy + 8, SKIN_D)
    if action == "hit":
        c.ellipse(hx, hy, 12, 12, (255, 220, 220, 200))

    # hair / hat
    draw_hair(c, actor, pal, hx, hy, front, back)

    # front arm / weapon for body layer (simple sword for war)
    if actor.startswith("war") or actor.startswith("fashion"):
        draw_weapon(c, "wep_sword", facing, action, frame, cx, y0, pal)
    elif actor.startswith("mage"):
        draw_weapon(c, "wep_staff", facing, action, frame, cx, y0, pal)
    elif actor.startswith("tao"):
        draw_weapon(c, "wep_blade", facing, action, frame, cx, y0, pal)
    elif actor == "npc":
        # satchel
        c.rect(cx + 6, y0 + 18, 6, 8, (90, 60, 30, 255), OUT)


def draw_torso(c: Canvas, pal, cx, y0, front, back, side, armor_only):
    tw = 14 if pal["female"] else 16
    th = 16
    tx = cx - tw // 2
    ty = y0 + 12
    c.rect(tx, ty, tw, th, pal["cloth"], OUT)
    c.rect(tx + 2, ty + 2, tw - 4, 4, pal["light"])
    c.rect(tx + 4, ty + 10, tw - 8, 3, pal["metal"])
    # shoulders
    c.ellipse(tx - 3, ty, 7, 7, pal["dark"], OUT)
    c.ellipse(tx + tw - 4, ty, 7, 7, pal["dark"], OUT)
    if pal["actor"].startswith("war") or armor_only:
        c.rect(tx + 1, ty + 1, 3, 5, STEEL_L)
        c.rect(tx + tw - 4, ty + 1, 3, 5, STEEL_L)
    if pal["actor"].startswith("fashion"):
        c.rect(tx + 3, ty + 4, tw - 6, 2, GOLD)


def draw_hair(c: Canvas, actor, pal, hx, hy, front, back):
    if actor.startswith("mage"):
        c.ellipse(hx - 1, hy - 2, 14, 10, pal["cloth"], OUT)
        c.pix(hx + 6, hy - 3, GOLD)
        c.pix(hx + 5, hy - 2, GOLD)
    elif actor.startswith("tao"):
        c.ellipse(hx - 1, hy - 1, 14, 8, pal["dark"], OUT)
        c.rect(hx + 2, hy + 2, 8, 2, pal["metal"])
    elif actor.startswith("fashion"):
        c.ellipse(hx - 2, hy - 1, 16, 10, pal["hair"], OUT)
        if pal["female"]:
            c.rect(hx - 3, hy + 6, 4, 12, pal["hair"], OUT)
            c.rect(hx + 11, hy + 6, 4, 12, pal["hair"], OUT)
        c.pix(hx + 6, hy, GOLD)
    elif actor == "npc":
        c.ellipse(hx - 1, hy - 2, 14, 8, (92, 48, 28, 255), OUT)
        c.rect(hx - 2, hy + 4, 16, 3, (70, 36, 20, 255), OUT)
    else:
        c.ellipse(hx - 1, hy - 1, 14, 8, pal["hair"], OUT)
        if pal["female"]:
            c.rect(hx - 2, hy + 6, 3, 10, pal["hair"], OUT)
            c.rect(hx + 11, hy + 6, 3, 10, pal["hair"], OUT)
        if not front:
            c.rect(hx, hy, 12, 4, pal["hair"])


def draw_helmet(c: Canvas, actor, cx, y0, pal):
    hx, hy = cx - 7, y0 + 1
    if "iron" in actor:
        c.ellipse(hx, hy, 14, 12, STEEL, OUT)
        c.rect(hx + 2, hy + 4, 10, 3, STEEL_D)
        c.pix(hx + 6, hy + 1, GOLD)
        c.pix(hx + 7, hy + 1, GOLD)
        c.rect(hx + 4, hy + 8, 6, 2, STEEL_L)
    else:
        c.ellipse(hx, hy, 14, 10, (150, 92, 48, 255), OUT)
        c.pix(hx + 6, hy + 2, GOLD)


def draw_weapon(c: Canvas, actor, facing, action, frame, cx, y0, pal):
    count = max(1, ACTION_FRAMES.get(action, 6))
    if action == "attack":
        arc = [(-8, -10), (-6, -14), (-2, -16), (4, -14), (10, -8), (12, -2), (12, 4), (10, 8), (6, 10), (2, 8)]
        dx, dy = arc[min(frame, len(arc) - 1)]
    elif action == "cast":
        dx, dy = (6, -10 + frame)
    else:
        dx, dy = (8, 2)

    hx, hy = cx + 6 + dx, y0 + 16 + dy
    if "staff" in actor:
        c.line(hx, hy, hx, hy + 22, (92, 64, 40, 255), 2)
        orb = (255, 220, 120, 255) if action == "cast" and frame % 2 == 0 else GOLD
        c.ellipse(hx - 3, hy - 5, 7, 7, orb, OUT)
        if action == "cast":
            c.ellipse(hx - 5, hy - 8, 11, 11, (255, 180, 60, 120))
    elif "blade" in actor and "tao" not in actor:
        # short blade / dao-style for tao when named wep_blade
        c.line(hx, hy, hx + 2, hy + 14, (150, 96, 48, 255), 2)
        c.pix(hx, hy, GOLD)
        if action == "attack" and frame in (2, 3, 4):
            c.line(hx - 4, hy + 4, hx + 8, hy + 10, (255, 244, 200, 200), 2)
    else:
        # sword / iron
        length = 20 if "iron" in actor else 16
        c.line(hx, hy, hx + 1, hy + length, (230, 232, 236, 255), 2)
        c.line(hx - 3, hy + 2, hx + 4, hy + 2, GOLD, 2)
        c.pix(hx, hy, GOLD)
        if action == "attack" and frame in (2, 3, 4, 5):
            c.line(hx - 6, hy + 2, hx + 10, hy + 12, (255, 250, 210, 220), 2)
            c.line(hx - 4, hy, hx + 8, hy + 10, (255, 220, 140, 160), 1)


def draw_corpse(c: Canvas, frame: int, pal) -> None:
    c.ellipse(10, 54, 28, 8, SHADOW)
    drop = min(3, frame)
    y = 34 + drop * 3
    c.ellipse(12, y, 24, 10, pal["cloth"], OUT)
    c.ellipse(30, y + 2, 10, 8, SKIN, OUT)
    c.rect(8, y + 4, 8, 4, BOOT, OUT)
    if frame >= 3:
        c.ellipse(14, 48, 20, 8, pal["dark"], OUT)


def draw_slime(img, ox, oy, action, frame) -> None:
    c = Canvas(img, ox, oy)
    c.ellipse(14, 54, 20, 7, SHADOW)
    if action == "death":
        c.ellipse(10, 48, 28, 10, (40, 120, 48, 200), OUT)
        return
    squash = 2 if action in ("walk", "run", "hit") and frame % 2 == 0 else 0
    stretch = 2 if action == "attack" else 0
    body = (48, 190, 58, 255)
    light = (170, 255, 140, 255)
    # bigger gooey silhouette vs other mobs
    w, h = 26 + stretch, 20 - squash
    x, y = 13 - stretch // 2, 34 + squash
    c.ellipse(x, y, w, h, body, OUT)
    c.ellipse(x + 4, y + 2, 10, 6, light)
    # eyes
    c.ellipse(x + 5, y + 7, 4, 5, (20, 20, 20, 255))
    c.ellipse(x + 13, y + 7, 4, 5, (20, 20, 20, 255))
    c.pix(x + 6, y + 8, WHITE)
    c.pix(x + 14, y + 8, WHITE)
    # highlight blob
    c.ellipse(x + 8, y + 3, 5, 3, (210, 255, 200, 180))
    if action == "attack":
        c.ellipse(x + w - 2, y + 6, 8, 8, (90, 200, 80, 220), OUT)


def draw_boar(img, ox, oy, facing, action, frame) -> None:
    c = Canvas(img, ox, oy)
    c.ellipse(12, 54, 24, 7, SHADOW)
    if action == "death":
        c.ellipse(8, 46, 32, 12, (120, 64, 40, 255), OUT)
        return
    bob = [0, 1, 0, 1][frame % 4]
    body = (110, 58, 28, 255)
    dark = (60, 30, 14, 255)
    light = (160, 96, 52, 255)
    y = 28 + bob
    # bulkier side-profile boar
    c.ellipse(6, y + 4, 30, 18, body, OUT)
    c.ellipse(8, y + 6, 16, 10, light)
    # head + tusks
    c.ellipse(30, y, 14, 14, light, OUT)
    c.pix(36, y + 6, OUT)
    c.pix(38, y + 8, WHITE)  # tusk
    c.pix(38, y + 10, WHITE)
    # mane
    for i in range(5):
        c.pix(14 + i * 3, y + 2, dark)
    # legs
    c.rect(14, y + 16, 4, 8, BOOT, OUT)
    c.rect(28, y + 16, 4, 8, BOOT, OUT)
    if action == "attack":
        c.ellipse(38, y + 6, 8, 6, WHITE, OUT)


def draw_bone(img, ox, oy, action, frame) -> None:
    c = Canvas(img, ox, oy)
    c.ellipse(14, 54, 20, 7, SHADOW)
    if action == "death":
        c.ellipse(10, 48, 28, 8, WHITE, OUT)
        c.pix(20, 50, (200, 40, 40, 255))
        return
    bob = frame % 2
    bone = (236, 232, 214, 255)
    rust = (160, 70, 40, 255)
    y = 10 + bob
    c.ellipse(17, y, 14, 14, bone, OUT)
    c.ellipse(20, y + 3, 3, 4, (40, 10, 10, 255))
    c.ellipse(27, y + 3, 3, 4, (40, 10, 10, 255))
    c.rect(16, y + 8, 6, 4, rust, OUT)  # pauldron
    c.rect(21, y + 12, 6, 14, bone, OUT)
    c.rect(14, y + 14, 6, 12, bone, OUT)
    c.rect(28, y + 14, 6, 12, bone, OUT)
    c.rect(18, y + 26, 5, 12, bone, OUT)
    c.rect(25, y + 26, 5, 12, bone, OUT)
    # sword
    if action == "attack":
        c.line(32, y + 10, 42, y + 2, (200, 200, 210, 255), 2)
    else:
        c.line(32, y + 14, 34, y + 28, (200, 200, 210, 255), 2)


def draw_crawler(img, ox, oy, action, frame) -> None:
    c = Canvas(img, ox, oy)
    c.ellipse(12, 54, 24, 6, SHADOW)
    if action == "death":
        c.ellipse(10, 48, 28, 8, (40, 70, 40, 255), OUT)
        return
    wob = [0, 1, 0, -1][frame % 4]
    y = 38
    c.ellipse(10 + wob, y, 28, 12, (48, 90, 48, 255), OUT)
    c.ellipse(14 + wob, y + 2, 12, 6, (80, 140, 70, 255))
    c.ellipse(16 + wob, y + 4, 4, 4, (220, 40, 40, 255))
    c.ellipse(24 + wob, y + 4, 4, 4, (220, 40, 40, 255))
    for leg in range(5):
        lx = 12 + leg * 5 + wob
        ly = y + 10 + ((frame + leg) % 2)
        c.line(lx, ly, lx - 2, ly + 6, OUT, 1)


def draw_wraith(img, ox, oy, action, frame) -> None:
    c = Canvas(img, ox, oy)
    if action == "death":
        c.ellipse(16, 40, 16, 16, (120, 80, 180, 100))
        return
    glow = (170, 120, 230, 200) if frame % 2 == 0 else (130, 80, 190, 170)
    c.ellipse(16, 12, 16, 28, glow, (60, 30, 90, 255))
    c.ellipse(20, 18, 4, 4, (255, 255, 255, 230))
    c.ellipse(26, 18, 4, 4, (255, 255, 255, 230))
    c.ellipse(18, 36, 12, 10, (100, 60, 150, 140))
    if action == "attack":
        c.ellipse(28, 20, 12, 12, (200, 160, 255, 160))


def draw_dragon(img, ox, oy, action, frame) -> None:
    c = Canvas(img, ox, oy)
    c.ellipse(8, 54, 32, 8, SHADOW)
    if action == "death":
        c.ellipse(6, 44, 36, 14, (30, 90, 40, 255), OUT)
        return
    wing = 4 if action in ("attack", "cast") and frame % 2 == 0 else 0
    body = (46, 130, 58, 255)
    wing_c = (70, 160, 80, 255)
    c.ellipse(4, 18 - wing, 14, 16, wing_c, OUT)
    c.ellipse(30, 18 - wing, 14, 16, wing_c, OUT)
    c.ellipse(14, 22, 20, 24, body, OUT)
    c.ellipse(28, 16, 14, 14, (60, 150, 70, 255), OUT)
    c.pix(36, 20, (240, 40, 30, 255))
    c.pix(32, 28, GOLD)
    if action == "attack":
        c.ellipse(38, 26, 10, 8, (255, 120, 30, 255), OUT)


def draw_gate(img, ox, oy, frame) -> None:
    c = Canvas(img, ox, oy)
    stone = (120, 108, 92, 255)
    c.rect(8, 8, 32, 48, stone, OUT)
    c.rect(12, 8, 24, 8, (150, 136, 112, 255), OUT)
    c.rect(16, 24, 16, 32, (28, 18, 12, 255), OUT)
    c.pix(14, 18, GOLD)
    c.pix(32, 18, GOLD)
    if frame % 2 == 0:
        c.ellipse(20, 36, 8, 8, (255, 200, 80, 180))


def draw_wall_actor(img, ox, oy) -> None:
    c = Canvas(img, ox, oy)
    c.rect(4, 16, 40, 40, (110, 100, 88, 255), OUT)
    c.rect(4, 16, 40, 8, (150, 136, 112, 255), OUT)
    for i in range(4):
        c.rect(8 + i * 9, 28, 6, 4, (80, 72, 64, 255))


def draw_flag(img, ox, oy, frame) -> None:
    c = Canvas(img, ox, oy)
    c.rect(18, 8, 3, 48, (80, 60, 40, 255), OUT)
    wave = frame % 3
    c.rect(21, 8, 16 + wave, 12, (180, 32, 28, 255), OUT)
    c.pix(28, 12, GOLD)


def draw_horse(img, ox, oy, facing, action, frame) -> None:
    c = Canvas(img, ox, oy)
    c.ellipse(10, 54, 28, 7, SHADOW)
    bob = [0, 1, 0, 1][frame % 4] if action in ("walk", "run") else 0
    body = (236, 232, 220, 255)
    y = 28 + bob
    c.ellipse(8, y + 4, 28, 16, body, OUT)
    c.ellipse(30, y - 2, 12, 14, body, OUT)
    c.pix(38, y + 4, OUT)
    c.rect(12, y + 16, 5, 12, (90, 70, 50, 255), OUT)
    c.rect(26, y + 16, 5, 12, (90, 70, 50, 255), OUT)
    if action == "run":
        c.ellipse(4, y + 8, 8, 6, body, OUT)


def draw_wolf(img, ox, oy, action, frame) -> None:
    c = Canvas(img, ox, oy)
    c.ellipse(12, 54, 24, 7, SHADOW)
    if action == "death":
        c.ellipse(10, 46, 28, 10, (70, 70, 78, 255), OUT)
        return
    bob = frame % 2
    y = 32 + bob
    fur = (120, 124, 132, 255)
    c.ellipse(10, y, 26, 14, fur, OUT)
    c.ellipse(30, y - 2, 12, 12, (150, 154, 160, 255), OUT)
    c.pix(36, y + 2, (220, 40, 40, 255))
    c.rect(14, y + 12, 4, 8, (60, 60, 68, 255), OUT)
    c.rect(28, y + 12, 4, 8, (60, 60, 68, 255), OUT)


def draw_actor_frame(img, ox, oy, actor, facing_i, action, frame) -> None:
    facing = facing_name(facing_i)
    if actor in ("war_m", "war_f", "mage_m", "mage_f", "tao_m", "tao_f", "fashion_m", "fashion_f", "npc"):
        draw_human(img, ox, oy, facing, action, frame, actor)
    elif actor.startswith("arm_") or actor.startswith("helm_") or actor.startswith("wep_"):
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
        draw_wall_actor(img, ox, oy)
    elif actor == "flag":
        draw_flag(img, ox, oy, frame)
    elif actor == "horse":
        draw_horse(img, ox, oy, facing, action, frame)
    elif actor == "wolf":
        draw_wolf(img, ox, oy, action, frame)


def load_tile_strips() -> list[Image.Image] | None:
    ref = ROOT / "docs/client/shots/mir-tile-ref.jpg"
    if not ref.exists():
        return None
    im = Image.open(ref).convert("RGBA")
    w, h = im.size
    # eight vertical panels
    strips = []
    for i in range(8):
        x0 = int(i * w / 8)
        x1 = int((i + 1) * w / 8)
        crop = im.crop((x0 + 4, 4, x1 - 4, h - 4))
        # take square center
        cw, ch = crop.size
        side = min(cw, ch)
        left = (cw - side) // 2
        top = (ch - side) // 2
        sq = crop.crop((left, top, left + side, top + side)).resize((TILE, TILE), Image.Resampling.LANCZOS)
        strips.append(sq)
    return strips


def noise_tile(base_rgb, speckles, seed) -> Image.Image:
    rng = random.Random(seed)
    im = Image.new("RGBA", (TILE, TILE), (*base_rgb, 255))
    px = im.load()
    for _ in range(speckles):
        x, y = rng.randrange(TILE), rng.randrange(TILE)
        d = rng.randint(-24, 24)
        r = clamp(base_rgb[0] + d, 0, 255)
        g = clamp(base_rgb[1] + d, 0, 255)
        b = clamp(base_rgb[2] + d, 0, 255)
        px[x, y] = (r, g, b, 255)
    return im


def paint_tree_tile() -> Image.Image:
    im = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    d = ImageDraw.Draw(im, "RGBA")
    d.ellipse([10, 4, 38, 28], fill=(36, 110, 40, 255), outline=OUT)
    d.ellipse([14, 8, 28, 20], fill=(70, 150, 60, 255))
    d.rectangle([22, 26, 26, 44], fill=(86, 58, 32, 255), outline=OUT)
    d.ellipse([16, 40, 32, 46], fill=SHADOW)
    return im


def paint_portal_tile() -> Image.Image:
    im = Image.new("RGBA", (TILE, TILE), (40, 70, 120, 255))
    d = ImageDraw.Draw(im, "RGBA")
    d.ellipse([10, 8, 38, 40], fill=(20, 40, 80, 255), outline=(80, 200, 220, 255))
    d.ellipse([16, 14, 32, 34], fill=(80, 200, 220, 180))
    d.ellipse([20, 18, 28, 28], fill=(230, 255, 255, 220))
    return im



def paint_flowers_tile() -> Image.Image:
    im = noise_tile((52, 108, 44), 120, 91)
    d = ImageDraw.Draw(im, "RGBA")
    for x, y, col in [(8, 10, (220, 80, 120, 255)), (18, 16, (240, 220, 80, 255)), (28, 8, (180, 100, 220, 255)),
                      (12, 24, (240, 140, 60, 255)), (22, 28, (220, 80, 120, 255)), (34, 22, (240, 220, 80, 255)),
                      (6, 34, (180, 100, 220, 255)), (30, 34, (240, 140, 60, 255))]:
        d.ellipse([x, y, x + 4, y + 4], fill=col)
        d.point((x + 1, y + 1), fill=(255, 255, 255, 180))
    return im


def paint_fence_tile() -> Image.Image:
    im = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    d = ImageDraw.Draw(im, "RGBA")
    wood, dark = (140, 96, 52, 255), (90, 58, 28, 255)
    for x in (10, 34):
        d.rectangle([x, 10, x + 4, 40], fill=wood, outline=OUT)
    d.rectangle([8, 14, 40, 18], fill=wood, outline=OUT)
    d.rectangle([8, 26, 40, 30], fill=dark, outline=OUT)
    d.ellipse([14, 40, 34, 46], fill=SHADOW)
    return im


def paint_lamp_tile() -> Image.Image:
    im = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    d = ImageDraw.Draw(im, "RGBA")
    stone, glow = (110, 110, 120, 255), (255, 220, 120, 220)
    d.rectangle([20, 28, 28, 42], fill=stone, outline=OUT)
    d.rectangle([16, 16, 32, 28], fill=stone, outline=OUT)
    d.ellipse([18, 10, 30, 20], fill=glow, outline=(255, 240, 180, 255))
    d.ellipse([14, 40, 34, 46], fill=SHADOW)
    return im


def paint_bush_tile() -> Image.Image:
    im = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    d = ImageDraw.Draw(im, "RGBA")
    d.ellipse([8, 18, 40, 42], fill=(36, 100, 40, 255), outline=OUT)
    d.ellipse([12, 14, 28, 30], fill=(60, 140, 55, 255))
    for x, y in [(14, 22), (22, 18), (28, 26), (18, 30)]:
        d.ellipse([x, y, x + 4, y + 4], fill=(180, 40, 50, 255))
    d.ellipse([14, 40, 34, 46], fill=SHADOW)
    return im


def paint_rocks_tile() -> Image.Image:
    im = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    d = ImageDraw.Draw(im, "RGBA")
    d.polygon([(10, 36), (18, 20), (30, 22), (36, 36)], fill=(120, 118, 110, 255), outline=OUT)
    d.polygon([(20, 38), (28, 26), (40, 30), (38, 40)], fill=(90, 100, 88, 255), outline=OUT)
    d.ellipse([12, 40, 36, 46], fill=SHADOW)
    return im


def build_tiles(img: Image.Image, tile_y: int) -> None:
    strips = load_tile_strips()
    mapping = {
        "grass": 0,
        "grass_b": 1,
        "dirt": 2,
        "path": 3,
        "stone": 4,
        "safe": 5,
        "water": 6,
        "wall": 7,
    }
    fallbacks = {
        "grass": (58, 110, 48),
        "grass_b": (48, 96, 42),
        "grass_c": (66, 122, 52),
        "dirt": (112, 84, 52),
        "path": (150, 132, 96),
        "stone": (120, 114, 104),
        "safe": (168, 150, 110),
        "water": (48, 110, 150),
        "flowers": (52, 108, 44),
        "wall": (92, 84, 74),
    }
    painters = {
        "tree": paint_tree_tile,
        "portal": paint_portal_tile,
        "flowers": paint_flowers_tile,
        "fence": paint_fence_tile,
        "lamp": paint_lamp_tile,
        "bush": paint_bush_tile,
        "rocks": paint_rocks_tile,
    }
    for i, name in enumerate(TILES):
        ox = i * TILE
        if name in painters:
            tile = painters[name]()
        elif name == "grass_c":
            tile = noise_tile(fallbacks["grass_c"], 200, 77)
            # taller tufts
            d = ImageDraw.Draw(tile, "RGBA")
            for x in range(6, 42, 5):
                d.line([(x, 30), (x - 1, 18)], fill=(40, 90, 36, 255), width=2)
        elif strips and name in mapping:
            tile = strips[mapping[name]].copy()
            tile = ImageEnhance.Sharpness(tile).enhance(1.4)
            tile = tile.filter(ImageFilter.SMOOTH_MORE)
        else:
            tile = noise_tile(fallbacks.get(name, (80, 80, 80)), 180, hash(name) & 0xFFFF)
        img.paste(tile, (ox, tile_y), tile if tile.mode == "RGBA" else None)


def paint_fx(img, name, index, frame, fx_y) -> None:
    ox = (index * FX_FRAMES + frame) * TILE
    oy = fx_y
    d = ImageDraw.Draw(img, "RGBA")
    # clear
    for y in range(TILE):
        for x in range(TILE):
            img.putpixel((ox + x, oy + y), (0, 0, 0, 0))
    cx = cy = TILE // 2
    if name == "slash":
        for i in range(4 + frame * 2):
            x0 = 8 + i
            y0 = 28 - i - frame
            d.line([ox + x0, oy + y0, ox + x0 + 10, oy + y0 - 8], fill=(255, 250, 210, 230), width=3)
            d.line([ox + x0 + 1, oy + y0 + 1, ox + x0 + 12, oy + y0 - 6], fill=(255, 220, 120, 160), width=2)
    elif name == "fire":
        for r in range(10 + frame, 2, -1):
            col = (255, 80 + frame * 15, 20, 200) if r > 5 else (255, 220, 80, 230)
            d.ellipse([ox + cx - r, oy + cy - r - frame, ox + cx + r, oy + cy + r // 2], fill=col)
    elif name == "lightning":
        pts = []
        x = cx
        for y in range(4, 44, 3):
            x += 4 if ((y // 3 + frame) % 2 == 0) else -4
            pts.append((ox + x, oy + y))
        if len(pts) >= 2:
            d.line(pts, fill=(180, 220, 255, 240), width=2)
            d.line([(p[0] + 1, p[1]) for p in pts], fill=(255, 255, 255, 200), width=1)
    elif name == "heal":
        col = (120, 230, 170, 230)
        d.line([ox + cx - 10, oy + cy, ox + cx + 10, oy + cy], fill=col, width=3)
        d.line([ox + cx, oy + cy - 10, ox + cx, oy + cy + 10], fill=col, width=3)
        if frame % 2 == 0:
            d.ellipse([ox + cx - 3, oy + cy - 14, ox + cx + 3, oy + cy - 8], fill=(255, 255, 255, 255))
    elif name == "level":
        col = (255, 210, 70, 240)
        for i in range(8):
            d.ellipse([ox + cx - 2, oy + cy - 16 + i * 2 - frame, ox + cx + 2, oy + cy - 12 + i * 2 - frame], fill=col)
        d.ellipse([ox + cx - 8, oy + cy - 4, ox + cx + 8, oy + cy + 8], outline=col, width=2)
    elif name == "portal":
        rad = 8 + frame
        d.ellipse([ox + cx - rad, oy + cy - rad, ox + cx + rad, oy + cy + rad], outline=(80, 210, 230, 230), width=2)
        d.ellipse([ox + cx - 3, oy + cy - 3, ox + cx + 3, oy + cy + 3], fill=(230, 255, 255, 255))
    elif name == "buff":
        col = (240, 200, 80, 255) if frame % 2 == 0 else (120, 180, 255, 255)
        d.polygon([
            (ox + cx, oy + cy - 8),
            (ox + cx + 6, oy + cy),
            (ox + cx, oy + cy + 8),
            (ox + cx - 6, oy + cy),
        ], fill=col)


def paint_ui(path: Path) -> None:
    # keep a lightweight chrome strip for fallbacks; main HUD uses kit PNGs
    im = Image.new("RGBA", (512, 360), (0, 0, 0, 0))
    d = ImageDraw.Draw(im, "RGBA")
    wood = (62, 38, 22, 255)
    edge = (214, 168, 64, 255)
    d.rectangle([0, 0, 79, 43], fill=wood, outline=edge)
    d.rectangle([0, 48, 179, 65], fill=(42, 12, 12, 255), outline=edge)
    d.rectangle([0, 70, 179, 87], fill=(12, 18, 48, 255), outline=edge)
    d.rectangle([0, 96, 511, 205], fill=wood, outline=edge)
    d.rectangle([200, 0, 349, 149], fill=wood, outline=edge)
    d.rectangle([360, 160, 507, 349], fill=wood, outline=edge)
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

    build_tiles(img, char_h)
    fx_y = char_h + TILE
    for i, name in enumerate(FX):
        for f in range(FX_FRAMES):
            paint_fx(img, name, i, f, fx_y)

    out_png = ROOT / "client/Legend.Playable/Assets/atlas.png"
    out_json = ROOT / "client/Legend.Playable/Assets/atlas.json"
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
        "fxY": fx_y,
        "fx": FX,
        "fxFrames": FX_FRAMES,
        "fxSize": TILE,
        "note": "Original sheet generated by tools/gen_mir_sheet.py (48x64). Not official Mir art.",
    }
    out_json.write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"wrote {out_png} {img.size} bytes={out_png.stat().st_size}")
    paint_ui(ROOT / "client/Legend.Playable/Assets/ui_chrome.png")


if __name__ == "__main__":
    main()
