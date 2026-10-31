#!/usr/bin/env python3
"""Seed sample-classic default Mir-style assets from /workspace/legend-asset-gen.

Prefer: after regen, also write seed-media/ (committed) via recompress path;
runtime install: tools/install_classic_seed_media.py
"""
from __future__ import annotations

import hashlib
import json
import shutil
from datetime import datetime, timezone
from pathlib import Path

from PIL import Image

REPO = Path("/workspace/legend-platform")
GEN = Path("/workspace/legend-asset-gen")
PACK = REPO / "version-packs" / "sample-classic"
LIB = PACK / "assets" / "library"
THUMBS = PACK / "assets" / "thumbs"
NOW = datetime.now(timezone.utc).isoformat()

UI_SLOTS = [
    ("login_bg", "ui/login_bg.jpg", "登录背景"),
    ("hud_frame", "ui/hud_frame.jpg", "HUD边框"),
    ("hp_orb", "ui/hp_orb.jpg", "血量球"),
    ("mp_orb", "ui/mp_orb.jpg", "蓝量球"),
    ("menu_ring", "ui/menu_ring.jpg", "菜单环"),
    ("window_frame", "ui/window_frame.jpg", "窗口框"),
    ("item_icon_atlas", "ui/item_icon_atlas.jpg", "物品图标集"),
    ("chat_panel", "ui/chat_panel.jpg", "聊天面板"),
    ("minimap_frame", "ui/minimap_frame.jpg", "小地图框"),
    ("button_primary", "ui/button_primary.jpg", "主按钮"),
]

MAPS = [
    "novice_plain",
    "oath_city",
    "sand_gate",
    "dark_forest",
    "bone_path",
    "marsh_den",
    "sealed_temple",
    "dungeon_slime_cave",
]

MONSTERS = [
    "slime_green",
    "boar_wild",
    "slime_cave",
    "world_green_dragon",
    "marsh_crawler",
    "bone_walker",
    "castle_gate",
    "castle_wall",
    "castle_flag",
    "temple_wraith",
    "deer_plain",
    "chicken_wild",
    "scarecrow",
    "worm_cave",
    "centipede_red",
    "skeleton_fighter",
    "cave_brute",
    "forest_wolf",
]

# itemId -> gen filename stem
ITEMS = {
    "item_hp_potion": "hp_potion",
    "item_mp_potion": "mp_potion",
    "item_dried_meat": "dried_meat",
    "item_copper_coin": "copper_coin",
    "item_wooden_blade": "wooden_blade",
    "item_wooden_sword": "wooden_sword",
    "item_iron_sword": "iron_sword",
    "item_cloth_armor": "cloth_armor",
    "item_bronze_ring": "bronze_ring",
    "item_cloth_helmet": "cloth_helmet",
    "item_cloth_boots": "cloth_boots",
    "item_rope_belt": "rope_belt",
    "item_skillbook_fatal_strike": "skillbook",
    "item_skillbook_rush": "skillbook",
    "item_skillbook_mana_shield": "skillbook",
    "item_skillbook_ice_roar": "skillbook",
    "item_skillbook_fire_rain": "skillbook",
    "item_skillbook_hide": "skillbook",
    "item_skillbook_summon_bone": "skillbook",
    "item_skillbook_summon_beast": "skillbook",
    "item_skillbook_group_hide": "skillbook",
    "item_town_scroll": "town_scroll",
    "item_random_scroll": "town_scroll",
    "item_dragon_scale": "dragon_scale",
    "item_slime_gel": "slime_gel",
    "item_torch": "torch",
    "item_bone_chip": "bone_chip",
}


def stable_id(kind: str, key: str) -> str:
    h = hashlib.sha256(f"sample-classic|{kind}|{key}".encode()).hexdigest()
    return h[:32]


def ensure_png(src: Path, dest: Path, max_side: int | None = None) -> tuple[int, int]:
    dest.parent.mkdir(parents=True, exist_ok=True)
    with Image.open(src) as im:
        im = im.convert("RGB")
        if max_side:
            im.thumbnail((max_side, max_side), Image.Resampling.LANCZOS)
        im.save(dest, format="PNG", optimize=True)
        return im.size


def make_thumb(src_png: Path, thumb_png: Path) -> None:
    thumb_png.parent.mkdir(parents=True, exist_ok=True)
    with Image.open(src_png) as im:
        im = im.convert("RGB")
        im.thumbnail((160, 160), Image.Resampling.LANCZOS)
        im.save(thumb_png, format="PNG", optimize=True)


def register(assets: list, asset_id: str, kind: str, file_name: str, tags: str, src: Path) -> dict:
    rel = f"assets/library/{asset_id}.png"
    abs_path = PACK / rel
    w, h = ensure_png(src, abs_path)
    thumb_rel = f"assets/thumbs/{asset_id}.png"
    make_thumb(abs_path, PACK / thumb_rel)
    sha = hashlib.sha256(abs_path.read_bytes()).hexdigest()
    entry = {
        "id": asset_id,
        "kind": kind,
        "fileName": file_name,
        "path": rel,
        "thumb": thumb_rel,
        "contentType": "image/png",
        "bytes": abs_path.stat().st_size,
        "width": w,
        "height": h,
        "sha256": sha,
        "tags": tags,
    }
    assets.append(entry)
    return entry


def copy_publish(asset_id: str, dest_stem: str) -> str:
    src = LIB / f"{asset_id}.png"
    dest = PACK / f"{dest_stem}.png"
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dest)
    return f"{dest_stem}.png"


def main() -> None:
    assets: list[dict] = []
    ui_ids: dict[str, str] = {}
    map_ids: dict[str, str] = {}
    mon_ids: dict[str, str] = {}
    item_ids: dict[str, str] = {}  # gen stem -> asset id (dedupe)

    # UI
    for slot, rel, cn in UI_SLOTS:
        src = GEN / rel
        if not src.exists():
            raise SystemExit(f"missing {src}")
        aid = stable_id("ui", slot)
        register(assets, aid, "ui", f"{slot}.png", f"默认UI,{cn},{slot}", src)
        published = copy_publish(aid, f"assets/ui/{slot}")
        ui_ids[slot] = aid
        print(f"ui {slot} -> {aid} ({published})")

    # Maps (minimap)
    for mid in MAPS:
        src = GEN / "maps" / f"{mid}.jpg"
        if not src.exists():
            raise SystemExit(f"missing {src}")
        aid = stable_id("map", mid)
        register(assets, aid, "map_tile", f"{mid}_minimap.png", f"默认地图,小地图,{mid}", src)
        copy_publish(aid, f"assets/maps/{mid}/minimap")
        # also use as backdrop for preview
        copy_publish(aid, f"assets/maps/{mid}/backdrop")
        map_ids[mid] = aid
        print(f"map {mid} -> {aid}")

    # Monsters
    for mid in MONSTERS:
        src = GEN / "monsters" / f"{mid}.jpg"
        if not src.exists():
            raise SystemExit(f"missing {src}")
        aid = stable_id("monster", mid)
        register(assets, aid, "monster", f"{mid}.png", f"默认怪物,{mid}", src)
        copy_publish(aid, f"assets/monsters/{mid}_icon")
        copy_publish(aid, f"assets/monsters/{mid}_sprite")
        mon_ids[mid] = aid
        print(f"monster {mid} -> {aid}")

    # Items (reuse skillbook/scroll images)
    for item_id, stem in ITEMS.items():
        if stem not in item_ids:
            src = GEN / "items" / f"{stem}.jpg"
            if not src.exists():
                raise SystemExit(f"missing {src}")
            aid = stable_id("item", stem)
            register(assets, aid, "item", f"{stem}.png", f"默认物品,{stem}", src)
            item_ids[stem] = aid
        aid = item_ids[stem]
        copy_publish(aid, f"assets/items/{item_id}_icon")
        print(f"item {item_id} -> {aid}")

    # assets_manifest
    manifest = {
        "version": 1,
        "packId": "sample-classic",
        "updatedAt": NOW,
        "updatedBy": "seed-classic-default-assets",
        "assets": assets,
    }
    (PACK / "configs" / "assets_manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )

    # ui_skin
    slots = {}
    for slot, _, cn in UI_SLOTS:
        aid = ui_ids[slot]
        slots[slot] = {
            "assetId": aid,
            "note": f"默认素材·{cn}",
            "publishedPath": f"assets/ui/{slot}.png",
        }
    skin = {
        "version": 1,
        "packId": "sample-classic",
        "updatedAt": NOW,
        "updatedBy": "seed-classic-default-assets",
        "slots": slots,
    }
    (PACK / "configs" / "ui_skin.json").write_text(
        json.dumps(skin, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )

    # monsters look
    monsters_path = PACK / "configs" / "monsters.json"
    monsters = json.loads(monsters_path.read_text(encoding="utf-8"))
    for t in monsters.get("templates", []):
        mid = t.get("monsterId")
        if mid in mon_ids:
            t["look"] = {
                "spriteAssetId": mon_ids[mid],
                "iconAssetId": mon_ids[mid],
                "sheetKey": mid,
                "frameW": 64,
                "frameH": 64,
                "directions": 1,
                "framesPerDir": 1,
            }
    monsters_path.write_text(json.dumps(monsters, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    # items iconAssetId
    items_path = PACK / "configs" / "items.json"
    items = json.loads(items_path.read_text(encoding="utf-8"))
    for it in items.get("items", []):
        iid = it.get("itemId")
        if iid in ITEMS:
            stem = ITEMS[iid]
            it["iconAssetId"] = item_ids[stem]
    items_path.write_text(json.dumps(items, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    # map look
    for mid, aid in map_ids.items():
        mp = PACK / "maps" / f"{mid}.json"
        data = json.loads(mp.read_text(encoding="utf-8"))
        data["look"] = {
            "minimapAssetId": aid,
            "backdropAssetId": aid,
            "tileAssetId": aid,
        }
        mp.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    print(f"DONE assets={len(assets)} ui={len(ui_ids)} maps={len(map_ids)} monsters={len(mon_ids)} item_stems={len(item_ids)}")


if __name__ == "__main__":
    main()
