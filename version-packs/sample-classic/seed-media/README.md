# sample-classic 默认素材（seed-media）

热血传奇怀旧风默认图，由 `tools/seed_classic_default_assets.py` 从生成源重压入库。
Admin 启动时 `SeedData.SyncMediaFromPackManifestAsync` 会按 `configs/assets_manifest.json` 把对应文件同步进 `assets/library`（若缺失）与 SQLite MediaAssets。

勿把手动上传的临时文件放这里；运营上传仍走 MediaLibrary → library/（gitignore）。
