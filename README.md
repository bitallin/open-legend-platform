# Legend Platform — 原创传奇类三端多版本运营平台 (Phase A / M14)

> **100% 原创自研声明**：本仓库采用自有 Protobuf 协议、自有帧格式 (`[Len:4][MsgId:2][Seq:4][Payload]`)、自有版本包 (`version-packs/`) 与自有白名单 Lua 沙箱 API (`legend_api.*`)，严禁任何对第三方闭源引擎的逆向、破解或协议兼容。能力对标经典传奇玩法，**非协议/资源兼容 996**。

## 当前里程碑：M14（世界 Boss 刷新、仇恨榜与稀有掉落全服广播）

协议版本 **1.14.0**。在 M13 组队副本之上新增世界 Boss 最小闭环：定时/强制刷新 → 多端伤害累计仇恨 → 最高仇恨获稀有掉落 → 全服广播。

| 项 | 约定 |
|----|------|
| Boss 配置 | `configs/world_boss.json`；示例 `world_green_dragon`（`novice_plain` 25,12，HP=100，参与半径 40，刷新 90s） |
| 仇恨与归属 | 按造成伤害累计 TopN；**击杀归属 = 最高仇恨**；同伤害并列取最近一击 |
| 掉落 | `rewardGold` + `rareDrops`（含 `item_dragon_scale` 广播）；入包，满包走 M11 邮件 |
| 协议 | `WorldBossStatus/Spawn/Hp/Control`（3301–3306）、`LootBroadcastNotify`（3307）；兼用 `ServerNotice(rare_loot)` |
| 控制 | `WorldBossControlReq.action=force_spawn` 测试强制刷新 |
| VersionPack / Lua | `on_world_boss_spawn` / `on_world_boss_kill` / `on_rare_loot_broadcast` |
| 验收 | CLI `--boss`；`M14IntegrationTests`；`scripts/verify-m14.sh` |

### M13（组队共享进本与每日次数/CD 持久化）

| 项 | 约定 |
|----|------|
| 组队进本 | 队长 `partyEnter` 同 `instanceId`；按人校验 |
| 次数/CD | `dungeon_progress_json`；Asia/Shanghai 日界 |
| 验收 | CLI `--dungeon-party`；`verify-m13.sh` |

### M12 / M11 / M10 …

副本实例、邮件、攻城等仍生效。

## 1. 目录结构
```text
legend-platform/
├── protocol/                    # Protobuf 契约 (VERSION=1.14.0)
├── server/
│   ├── Legend.Contracts/        # MsgId … 3301–3307 世界 Boss
│   ├── Legend.Core/             # VersionPackLoader(+world_boss.json)
│   ├── Legend.Game/             # GameWorldModule.WorldBoss 仇恨/广播
│   ├── Legend.Script/           # on_world_boss_* / on_rare_loot_broadcast
│   ├── Legend.CliClient/        # --boss 验收
│   └── Legend.Tests/            # M0–M14
├── client/unity-sdk/            # WorldBoss Status/Control + Spawn/Hp/LootBroadcast (1.14.0)
├── version-packs/sample-classic/# world_boss.json + world_boss.lua
└── scripts/verify-m14.sh
```

## 2. 快速验证
```bash
./scripts/verify-m14.sh
./scripts/verify-m13.sh
# 回归：
./scripts/verify-m12.sh
./scripts/verify-m11.sh
```

### 启动（Sqlite）
```bash
dotnet run --project server/Legend.Host -- --port 7100 \
  --manifest version-packs/sample-classic/manifest.json --storage Sqlite --sqlite data/legend.db
```

### 手动验收（世界 Boss）
1. Handshake `1.14.0` → Register/Login → CreateChar → EnterWorld
2. `WorldBossControl force_spawn world_green_dragon` → 全服 `WorldBossSpawnNotify`
3. 两名玩家靠近 Boss：低仇恨打一下，高仇恨清完 → 高仇恨获 `item_dragon_scale`
4. 旁观端（安全区）收到 `LootBroadcastNotify` + `ServerNotice(rare_loot)`
5. `WorldBossStatus` 可见仇恨 TopN 与死亡/下次刷新

CLI：`dotnet run --project server/Legend.CliClient -- --host 127.0.0.1 --port 7100 --boss`

## 3. Unity 接入
见 `client/unity-sdk/README.md`。协议 `1.14.0`；新增 `RequestWorldBossStatusAsync` / `RequestWorldBossControlAsync`，事件 `OnWorldBossSpawn` / `OnWorldBossHp` / `OnLootBroadcast`。

## 4. 环境变量
| 变量 | 含义 |
|------|------|
| `LEGEND_STORAGE` | `InMemory` / `Sqlite` / `Postgres` |
| `LEGEND_SQLITE_PATH` | Sqlite 文件路径 |
| `LEGEND_PG` / `LEGEND_DB_CONN` | Npgsql 连接串 |
| `LEGEND_REDIS` / `LEGEND_REDIS_CONN` | Redis 连接串 |

## 5. 建议 M15
**交易行/摆摊挂单持久化与检索**，或 **坐骑/时装外观同步**，或 **跨服匹配/活动日历**。
