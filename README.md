# Legend Platform — 原创传奇类三端多版本运营平台 (Phase A / M2)

> **100% 原创自研声明**：本仓库采用自有 Protobuf 协议、自有帧格式 (`[Len:4][MsgId:2][Seq:4][Payload]`)、自有版本包 (`version-packs/`) 与自有白名单 Lua 沙箱 API (`legend_api.*`)，严禁任何对第三方闭源引擎的逆向、破解或协议兼容。

## 当前里程碑：M2（技能战斗、怪物、掉落拾取、背包持久化）

协议版本 **1.2.0**。在 M1 基础上增加表驱动技能/怪物/掉落、服务器权威伤害、AOI 战斗广播、击杀掉落归属与背包 JSONB/Sqlite 持久化。

### M2 规则摘要
| 项 | 约定 |
|----|------|
| 安全区 | **完全禁攻**（含打怪）；离开安全区后可打怪 |
| PvP | M2 关闭（攻击目标仅怪物） |
| 伤害 | `max(1, floor(atk * skillMult) - def)` |
| 掉落归属 | 击杀者独占 15s，之后自由拾取；60s 未拾取消失 |
| 拾取距离 | Chebyshev ≤ 2 |
| 玩家死亡 | 回当前地图出生点并满血满蓝 |

## 1. 目录结构
```text
legend-platform/
├── protocol/                    # Protobuf 契约 (VERSION=1.2.0)
├── server/
│   ├── Legend.Contracts/        # 接口 + 帧编解码 + MsgId 2101–2109
│   ├── Legend.Core/             # EventBus / Plugin / VersionPackLoader(skills/monsters/loot)
│   ├── Legend.Storage/          # 账号 PBKDF2、角色、会话、背包 SaveInventory
│   ├── Legend.Gate/             # TCP 网关
│   ├── Legend.Login/            # Handshake / Register / Login / Char CRUD
│   ├── Legend.Game/             # AOI、战斗、怪物 AI、掉落、拾取
│   ├── Legend.Script/           # MoonSharp：on_enter / on_kill / on_loot
│   ├── Legend.Host/             # 启动宿主
│   ├── Legend.CliClient/        # M2 全链路冒烟（含战斗）
│   └── Legend.Tests/            # M1 + M2 集成测试
├── client/unity-sdk/            # Unity 2022 LTS（含 Attack/Pickup）
├── version-packs/sample-classic/# skills/monsters/loot_tables + combat.lua
├── tools/Legend.MapValidate/
├── ops/docker-compose.yml
└── scripts/verify-m2.sh         # 一键验收（兼容 verify-m1.sh）
```

## 2. 快速验证
```bash
./scripts/verify-m2.sh
# 或仅 M1 链路：
./scripts/verify-m1.sh
```

### 启动（Sqlite 默认）
```bash
dotnet run --project server/Legend.Host -- --sqlite data/legend.db \
  --manifest version-packs/sample-classic/manifest.json --port 7100
```

### 手动打怪验收
1. Handshake `1.2.0` → Register/Login → CreateChar(warrior) → Select → EnterWorld
2. 出生点 (10,10) 在安全区；向东走到约 (22,10) 离开安全区
3. 附近应有「绿史莱姆」约 (25,10)；贴脸后 `AttackReq skillId=power_strike`（或 `basic_slash`）
4. 收到 `CombatSync`（含 TargetDied）与 `LootDropNotify`；走到掉落格 `PickupReq`
5. 重启 Host，再 Login → EnterWorld，`EnterWorldResp.inventory` 应仍有拾取物品

## 3. Unity 接入
见 `client/unity-sdk/README.md`。协议 1.2.0；调用 `RequestAttackAsync` / `RequestPickupAsync`。

## 4. 环境变量
| 变量 | 含义 |
|------|------|
| `LEGEND_STORAGE` | `InMemory` / `Sqlite` / `Postgres` |
| `LEGEND_SQLITE_PATH` | Sqlite 文件路径 |
| `LEGEND_PG` / `LEGEND_DB_CONN` | Npgsql 连接串 |
| `LEGEND_REDIS` / `LEGEND_REDIS_CONN` | Redis 连接串 |

## 5. 建议 M3
优先 **NPC 商店 + 简单任务**（对话、购买消耗背包、任务击杀计数写 Storage）；行会可放到 M4。
