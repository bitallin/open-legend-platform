# 画面与素材操作指南（运营）

> 时区：Asia/Shanghai · 对应里程碑 **M9 Asset Upload & Look Binding**

本指南说明如何在 **Legend.Admin** 上传素材图，并配置地图 / 界面 / 怪物 / 装备的模样。  
**后台不绘制游戏画面**；只接受你上传的原创或已获授权图片。禁止官方传奇 WIL/WZL 资源。

## 闭环

```
上传 PNG/WebP/JPG → 素材库（缩略图 + MediaAssets 索引 + assets_manifest.json）
    → 绑定外观（怪物 / 物品 / 地图 / UI 槽位）
    → 写入版本包 JSON + 复制到 assets/{monsters|items|maps|ui}/
    → （可选）热重载 Host；客户端 --assets-pack 加载覆盖
```

## 1. 上传

1. 登录后台 → 侧栏 **画面与素材 → 素材库**
2. 选择版本包（如 `sample-classic`）与类型：`ui` / `monster` / `item` / `map_tile` / …
3. 多选图片上传（单文件 ≤ 8MB）。系统校验文件头、SHA256 去重、生成 `assets/thumbs/` 缩略图。
4. 原图落盘：`version-packs/<pack>/assets/library/{assetId}.ext`
5. 自动重建：`configs/assets_manifest.json`

权限：`Operator` / `SuperAdmin` 可上传；`Support` 只读。

## 2. 绑定怪物外观

怪物列表 → **外观** → 选择精灵图 / 图标，可选 `sheetKey`、`frameW/H`、`directions`、`framesPerDir`。  
写入 `configs/monsters.json` 的 `look` 对象，并复制到 `assets/monsters/`。

```json
"look": {
  "spriteAssetId": "...",
  "iconAssetId": "...",
  "sheetKey": "slime",
  "frameW": 48,
  "frameH": 64,
  "directions": 5,
  "framesPerDir": 4
}
```

## 3. 绑定物品 / 装备外观

物品列表 → **外观** → `iconAssetId`、`dropSpriteAssetId`、`paperdoll`（层 id，如 `wep_sword`）。  
写入 `configs/items.json`，复制到 `assets/items/`。

## 4. 绑定地图外观

地图列表 → **外观** → 地砖图集 / 小地图 / 远景。  
写入 `maps/<mapId>.json` 的 `look`，复制到 `assets/maps/<mapId>/`。

## 5. UI 皮肤槽位

**画面与素材 → UI 皮肤**：为 `login_bg`、`hp_orb`、`hud_frame`、`window_frame` 等命名槽位选择素材。  
写入 `configs/ui_skin.json`，发布副本到 `assets/ui/{slot}.ext`。

## 6. 热更与客户端

1. 后台 **世界与区服 → 热重载**（需 Host Admin `7101` 可达）。
2. 客户端加载覆盖：

```bat
LegendClient.exe --host 150.158.164.90 --port 7100 --realm-id realm-classic-01 --assets-pack C:\path\to\version-packs\sample-classic
```

或环境变量 `LEGEND_ASSETS_PACK`。未绑定或文件缺失时回退内置 MirSheet / MirChrome。

## 7. 审计

上传、删除、绑定、重建 manifest、UI 槽位保存均写入 **操作审计**。

## 磁盘布局

```
version-packs/<packId>/
  assets/
    library/     # 上传原图 {assetId}.ext
    thumbs/      # 缩略图
    ui/ monsters/ items/ maps/
  configs/
    assets_manifest.json
    ui_skin.json
    monsters.json   # + look
    items.json      # + iconAssetId / dropSpriteAssetId
  maps/*.json       # + look
```

SQLite `MediaAssets` 表仅作管理台索引；**磁盘文件是真相源**。
