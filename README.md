# Legend Platform — 原创传奇类三端多版本运营平台 (Phase A / M4)

> **100% 原创自研声明**：本仓库采用自有 Protobuf 协议、自有帧格式 (`[Len:4][MsgId:2][Seq:4][Payload]`)、自有版本包 (`version-packs/`) 与自有白名单 Lua 沙箱 API (`legend_api.*`)，严禁任何对第三方闭源引擎的逆向、破解或协议兼容。

## 当前里程碑：M4（装备栏穿戴 + 属性加成挂钩权威战斗 + 装备强化）

协议版本 **1.4.0**。在 M3 商店/任务基础上新增表驱动道具与装备配置、装备栏穿戴/卸下、装备强化（`+1..+5`）、角色有效面板属性（`EffectiveAtk / EffectiveDef / EffectiveMaxHp`）实时挂钩权威战斗伤害结算，以及 `equipment_json` 落库持久化。

### M4 规则摘要
| 项 | 约定 |
|----|------|
| 装备槽位 | `weapon` / `armor` / `ring` |
| 穿戴/卸下 | 权威校验背包、槽位、职业与等级；穿戴扣减背包 1 件（原槽位有装备则替换回背包）；卸下退回背包 |
| 属性重算 | `TotalAtk = BaseAtk + sum(EquipAtk + EnhanceBonus)`，`TotalDef = BaseDef + sum(EquipDef + EnhanceBonus)`，`TotalMaxHp = BaseMaxHp + sum(EquipMaxHp + EnhanceBonus)` |
| 权威战斗挂钩 | `AttackReq` 与怪物反击实时使用 `EffectiveAtk` / `EffectiveDef` 结算伤害 |
| 装备强化 | 表驱动 `enhanceRules`（`+1..+3` 100% 成功率保证测试确定性，`+4..+5` 概率成功）；扣金币、提升槽位强化等级并持久化 |
| Lua 钩子 | `on_equip(ctx)`、`on_enhance(ctx)`（`scripts/equip.lua`） |

### M3 规则（仍生效）
| 项 | 约定 |
|----|------|
| NPC 交互距离 | Chebyshev ≤ 3 |
| 商店 | 表驱动价格（含木剑 `item_wooden_sword`、粗布衣 `item_cloth_armor`、青铜戒指 `item_bronze_ring`）；仅购买（扣金币 + 加背包）；权威落库 |
| 起始金币 | 新角色 100 |
| 任务示例 | `quest_slime_hunt`：击杀 2 只绿史莱姆 → 交任务领金币 |

### M2 规则（仍生效）
| 项 | 约定 |
|----|------|
| 安全区 | **完全禁攻**；离开后可打怪 |
| PvP | 关闭（攻击目标仅怪物） |
| 掉落归属 | 击杀者独占 15s，之后自由拾取 |

## 1. 目录结构
```text
legend-platform/
├── protocol/                    # Protobuf 契约 (VERSION=1.4.0)
├── server/
│   ├── Legend.Contracts/        # MsgId 2101–2109 战斗 + 2201–2209 商店/任务 + 2301–2306 装备/强化
│   ├── Legend.Core/             # VersionPackLoader(npcs/shops/quests/items/enhanceRules)
│   ├── Legend.Storage/          # Gold / QuestJson / EquipmentJson 持久化
│   ├── Legend.Game/             # Equip/Unequip/EnhanceEquip + EffectiveAtk/Def/MaxHp 战斗挂钩
│   ├── Legend.Script/           # on_talk / on_buy / on_quest_* / on_equip / on_enhance
│   ├── Legend.CliClient/        # M3 / M4 (--equip) 全链路冒烟
│   └── Legend.Tests/            # M1 + M2 + M3 + M4 (22 tests)
├── client/unity-sdk/            # Talk / ShopBuy / Quest / Equip / Unequip / EnhanceEquip API
├── version-packs/sample-classic/# items.json + shops.json + equip.lua
└── scripts/verify-m4.sh
```

## 2. 快速验证
```bash
./scripts/verify-m4.sh
# 或 M3 商店/任务链路：
./scripts/verify-m3.sh
```

### 启动（Sqlite 默认）
```bash
dotnet run --project server/Legend.Host -- --sqlite data/legend.db \
  --manifest version-packs/sample-classic/manifest.json --port 7100
```

### 手动验收（装备穿戴 + 战斗伤害提升 + 强化 + 重启持久化）
1. Handshake `1.4.0` → Register/Login → CreateChar (`warrior`) → Select → EnterWorld（初始 `EffectiveAtk=25, EffectiveDef=0, EffectiveMaxHp=180`）
2. 出安全区对绿史莱姆释放 `basic_slash`，未穿戴伤害为 `24`
3. 到安全区 (12,12) 找 NPC `npc_village_merchant` 购买 `item_wooden_sword`（20 金）与 `item_cloth_armor`（15 金）
4. 发送 `EquipReq` 穿戴木剑与布衣 → 面板提升至 `EffectiveAtk=35, EffectiveDef=5, EffectiveMaxHp=210`，打怪伤害提升至 `34`
5. 发送 `EnhanceEquipReq(slot="weapon")` 强化武器 `+1`（扣 20 金）→ `EffectiveAtk=39`，打怪伤害提升至 `38`
6. 重启 Host 后再次 EnterWorld：已穿戴装备、强化等级 `+1` 与面板属性完整保留

## 3. Unity 接入
见 `client/unity-sdk/README.md`。协议 `1.4.0`；新增 `RequestEquipAsync` / `RequestUnequipAsync` / `RequestEnhanceEquipAsync`。

## 4. 环境变量
| 变量 | 含义 |
|------|------|
| `LEGEND_STORAGE` | `InMemory` / `Sqlite` / `Postgres` |
| `LEGEND_SQLITE_PATH` | Sqlite 文件路径 |
| `LEGEND_PG` / `LEGEND_DB_CONN` | Npgsql 连接串 |
| `LEGEND_REDIS` / `LEGEND_REDIS_CONN` | Redis 连接串 |

## 5. 建议 M5
**行会与社交系统（Guild & Chat）** 或 **角色等级成长/经验值与药水消耗品使用（Level Up & Consumables）**。
