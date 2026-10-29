# Legend Platform — 原创传奇类三端多版本运营平台 (Phase A / M15)

> **100% 原创自研声明**：本仓库采用自有 Protobuf 协议、自有帧格式 (`[Len:4][MsgId:2][Seq:4][Payload]`)、自有版本包 (`version-packs/`) 与自有白名单 Lua 沙箱 API (`legend_api.*`)，严禁任何对第三方闭源引擎的逆向、破解或协议兼容。能力对标经典传奇玩法，**非协议/资源兼容 996**。

## 当前里程碑：M15（坐骑与时装外观同步系统）

协议版本 **1.15.0**。在 M14 世界 Boss 之上新增坐骑召唤/收回（权威移速）与时装穿戴（小幅 ATK 加成）闭环，外观经 `AppearanceSync` / `EntityEnterAoi` 同步，三后端 `appearance_json` 持久化，重启保留。

| 项 | 约定 |
|----|------|
| 坐骑配置 | `configs/mounts.json`；示例 `mount_white_horse`（移速 +50%，基准间隔 300ms → 骑乘 200ms） |
| 获得途径 | 商店购买 `item_mount_white_horse` → `UseItem` 解锁（`kind=mount_unlock`） |
| 协议 | `MountStatus/Summon/Dismiss`（3401–3406）、`FashionStatus/Equip`（3407–3410）、`AppearanceSyncNotify`（3413） |
| 移速权威 | **仅骑乘**校验 `LastMoveUnixMs`；步行不限间隔（兼容既有 Walk 辅助） |
| 时装配置 | `configs/fashions.json`；示例 `fashion_crimson_robe`（`atkBonus=2`） |
| 获得途径 | 商店 `item_fashion_crimson_robe` → `UseItem` 解锁（`kind=fashion_unlock`） |
| 外观字段 | `EntitySnapshot.mount_id` / `fashion_id` / `move_interval_ms`；AOI 广播 `AppearanceSync` |
| 持久化 | `appearance_json`：`{ownedMounts,activeMountId,ownedFashions,equippedFashionId}`；InMemory/Sqlite/Postgres |
| 错误码 | `ERR_MOUNT_DENIED=1071`、`ERR_FASHION_DENIED=1072`、`ERR_MOVE_TOO_FAST=1073` |
| VersionPack / Lua | `appearance.lua`：`on_mount` / `on_fashion_equip` |
| 验收 | CLI `--mount`；`M15IntegrationTests`；`scripts/verify-m15.sh` |

### M14（世界 Boss 刷新、仇恨榜与稀有掉落全服广播）

| 项 | 约定 |
|----|------|
| Boss 配置 | `configs/world_boss.json`；示例 `world_green_dragon` |
| 协议 | `WorldBossStatus/Spawn/Hp/Control`（3301–3306）、`LootBroadcastNotify`（3307） |
| 验收 | CLI `--boss`；`verify-m14.sh` |

### M13 / M12 / M11 …

组队副本、邮件、攻城等仍生效。

## 1. 目录结构
```text
legend-platform/
├── protocol/                    # Protobuf 契约 (VERSION=1.15.0)
├── server/
│   ├── Legend.Contracts/        # MsgId … 3401–3413 坐骑/时装/外观
│   ├── Legend.Core/             # VersionPackLoader(+mounts/fashions.json)
│   ├── Legend.Game/             # GameWorldModule.Appearance 召唤/穿戴/移速/AOI
│   ├── Legend.Storage/          # appearance_json 三后端
│   ├── Legend.Script/           # on_mount / on_fashion_equip
│   ├── Legend.CliClient/        # --mount 验收
│   └── Legend.Tests/            # M0–M15
├── client/unity-sdk/            # Mount/Fashion/AppearanceSync MsgId (1.15.0)
├── version-packs/sample-classic/# mounts.json + fashions.json + appearance.lua
└── scripts/verify-m15.sh
```

## 2. 快速验证
```bash
./scripts/verify-m15.sh
./scripts/verify-m14.sh
# 回归：
./scripts/verify-m13.sh
./scripts/verify-m12.sh
```

### 启动（Sqlite）
```bash
dotnet run --project server/Legend.Host -- --port 7100 \
  --manifest version-packs/sample-classic/manifest.json --storage Sqlite --sqlite data/legend.db
```

### 手动验收（坐骑 / 时装）
1. Handshake `1.15.0` → Register/Login → CreateChar → EnterWorld（两名客户端同图）
2. 商店购买 `item_mount_white_horse` → `UseItem` 解锁 → `MountStatus` 见 owned
3. `MountSummon mount_white_horse` → `move_interval_ms` 变短；另一客户端收到 `AppearanceSync(mountId)`
4. 骑乘状态下连续移动可通过权威间隔校验；`MountDismiss` 恢复步行
5. 购买并使用 `item_fashion_crimson_robe` → `FashionEquip` → AOI 见 `fashionId`；`EffectiveAtk` +2
6. 下线再进：`EntitySnapshot` 仍带 `mountId` / `fashionId`，时装 ATK 保留

CLI：`dotnet run --project server/Legend.CliClient -- --host 127.0.0.1 --port 7100 --mount`

## 3. Unity 接入
见 `client/unity-sdk/README.md`。协议 `1.15.0`；新增 Mount/Fashion MsgId 与 `AppearanceSyncNotify`；`EntitySnapshot` 含 `MountId` / `FashionId` / `MoveIntervalMs`。

## 4. 环境变量
| 变量 | 含义 |
|------|------|
| `LEGEND_STORAGE` | `InMemory` / `Sqlite` / `Postgres` |
| `LEGEND_SQLITE_PATH` | Sqlite 文件路径 |
| `LEGEND_PG` / `LEGEND_DB_CONN` | Npgsql 连接串 |
| `LEGEND_REDIS` / `LEGEND_REDIS_CONN` | Redis 连接串 |
