#!/usr/bin/env python3
"""Copy version-packs/sample-classic/seed-media into assets/library|thumbs|published using assets_manifest.json."""
from __future__ import annotations
import json, shutil
from pathlib import Path
from PIL import Image

PACK = Path(__file__).resolve().parents[1] / "version-packs" / "sample-classic"
MANIFEST = PACK / "configs" / "assets_manifest.json"

def thumb(src: Path, dest: Path) -> None:
    dest.parent.mkdir(parents=True, exist_ok=True)
    with Image.open(src) as im:
        im = im.convert("RGB")
        im.thumbnail((128, 128), Image.Resampling.LANCZOS)
        im.save(dest, format="PNG", optimize=True)

def main() -> None:
    data = json.loads(MANIFEST.read_text(encoding="utf-8"))
    n = 0
    for a in data.get("assets", []):
        seed = a.get("seedPath")
        rel = a.get("path")
        if not seed or not rel:
            continue
        src = PACK / seed
        if not src.exists():
            print("missing", seed)
            continue
        dest = PACK / rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dest)
        th = a.get("thumb")
        if th:
            thumb(src, PACK / th)
        # published paths by kind/tags heuristics
        tags = a.get("tags") or ""
        kind = a.get("kind")
        fn = a.get("fileName") or dest.name
        stem = Path(fn).stem
        if kind == "ui":
            out = PACK / "assets" / "ui" / fn
            out.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, out)
        elif kind == "monster":
            out_i = PACK / "assets" / "monsters" / f"{stem}_icon.png"
            out_s = PACK / "assets" / "monsters" / f"{stem}_sprite.png"
            out_i.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, out_i)
            shutil.copy2(src, out_s)
        elif kind == "item":
            # published per-item icons already named in prior seed; skip generic
            pass
        elif kind == "map_tile":
            mid = stem.replace("_minimap", "")
            d = PACK / "assets" / "maps" / mid
            d.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, d / "minimap.png")
            shutil.copy2(src, d / "backdrop.png")
        n += 1
    # republish item icons from items.json
    items = json.loads((PACK / "configs" / "items.json").read_text(encoding="utf-8"))
    by_id = {a["id"]: a for a in data.get("assets", [])}
    for it in items.get("items", []):
        iid = it.get("itemId")
        aid = it.get("iconAssetId")
        if not iid or not aid or aid not in by_id:
            continue
        src = PACK / by_id[aid]["seedPath"]
        if not src.exists():
            continue
        out = PACK / "assets" / "items" / f"{iid}_icon.png"
        out.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, out)
    print(f"installed {n} seed assets into sample-classic/assets")

if __name__ == "__main__":
    main()
