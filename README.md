# Legend Platform — 原创传奇类三端多版本运营平台 (Phase A / M3)

> **100% 原创自研声明**：本仓库采用自有 Protobuf 协议、自有帧格式 (`[Len:4][MsgId:2][Seq:4][Payload]`)、自有版本包 (`version-packs/`) 与自有白名单 Lua 沙箱 API (`legend_api.*`)，严禁任何对第三方闭源引擎的逆向、破解或协议兼容。

## 当前里程碑：M3（NPC 商店购买 + 简单任务）

协议版本 **1.3.0**。在 M2 战斗/掉落基础上增加表驱动 NPC/商店/任务、金币持久化、击杀任务进度与交任务奖励。

### M3 规则摘要
| 项 | 约定 |
|----|------|
| NPC 交互距离 | Chebyshev ≤ 3 |
| 商店 | 表驱动价格；仅购买（扣金币 + 加背包）；权威落库 |
| 起始金币 | 新角色 100 |
| 任务示例 | `quest_slime_hunt`：击杀 2 只绿史莱姆 → 交任务领金币 |
| 安全区 | 与 M2 一致；商店 NPC 在新手村安全区 |

### M2 规则（仍生效）
| 项 | 约定 |
|----|------|
| 安全区 | **完全禁攻**；离开后可打怪 |
| PvP | 关闭（攻击目标仅怪物） |
| 掉落归属 | 击杀者独占 15s，之后自由拾取 |

## 1. 目录结构
```text
legend-platform/
├── protocol/                    # Protobuf 契约 (VERSION=1.3.0)
├── server/
│   ├── Legend.Contracts/        # MsgId 2101–2109 战斗 + 2201–2209 商店/任务
│   ├── Legend.Core/             # VersionPackLoader(npcs/shops/quests)
│   ├── Legend.Storage/          # Gold / QuestJson 持久化
│   ├── Legend.Game/             # NPC AOI、Talk/ShopBuy/Quest*
│   ├── Legend.Script/           # on_talk / on_buy / on_quest_*
│   ├── Legend.CliClient/        # M3 全链路冒烟
│   └── Legend.Tests/            # M1 + M2 + M3
├── client/unity-sdk/            # Talk / ShopBuy / Quest API
├── version-packs/sample-classic/# npcs/shops/quests + npc_quest.lua
└── scripts/verify-m3.sh
```

## 2. 快速验证
```bash
./scripts/verify-m3.sh
# 或仅战斗链路：
./scripts/verify-m2.sh
```

### 启动（Sqlite 默认）
```bash
dotnet run --project server/Legend.Host -- --sqlite data/legend.db \
  --manifest version-packs/sample-classic/manifest.json --port 7100
```

### 手动验收（商店 + 任务）
1. Handshake `1.3.0` → Register/Login → CreateChar → Select → EnterWorld（出生金币 100）
2. 安全区 (12,12) 附近找 NPC「杂货商人阿福」(`npc_village_merchant`)
3. `TalkReq` → 打开商店；`ShopBuyReq item_hp_potion`（价格 10 金）
4. `QuestAcceptReq quest_slime_hunt` → 出安全区击杀 2 只绿史莱姆 → 回 NPC `QuestTurnInReq`
5. 重启 Host 后 EnterWorld：金币、背包、任务状态（Completed）仍在

## 3. Unity 接入
见 `client/unity-sdk/README.md`。协议 1.3.0；`RequestTalkAsync` / `RequestShopBuyAsync` / `RequestQuestAcceptAsync` / `RequestQuestTurnInAsync`。

## 4. 环境变量
| 变量 | 含义 |
|------|------|
| `LEGEND_STORAGE` | `InMemory` / `Sqlite` / `Postgres` |
| `LEGEND_SQLITE_PATH` | Sqlite 文件路径 |
| `LEGEND_PG` / `LEGEND_DB_CONN` | Npgsql 连接串 |
| `LEGEND_REDIS` / `LEGEND_REDIS_CONN` | Redis 连接串 |

## 5. 建议 M4
**行会系统**（创建/加入/退出、频道、简单权限）或 **装备栏 + 强化**；视产品优先级二选一。
