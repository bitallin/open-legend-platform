# Legend Platform — 原创传奇类三端多版本运营平台 (Phase A / M5)

> **100% 原创自研声明**：本仓库采用自有 Protobuf 协议、自有帧格式 (`[Len:4][MsgId:2][Seq:4][Payload]`)、自有版本包 (`version-packs/`) 与自有白名单 Lua 沙箱 API (`legend_api.*`)，严禁任何对第三方闭源引擎的逆向、破解或协议兼容。能力对标经典传奇玩法，**非协议/资源兼容 996**。

## 当前里程碑：M5（打怪经验升级曲线 + 背包药水消耗使用）

协议版本 **1.5.0**。在 M4 装备/强化基础上新增：

| 项 | 约定 |
|----|------|
| 升级曲线 | `configs/levels.json`：至少到 Lv.10 的 `expToNext` + 职业成长（战士/法师/道士） |
| 击杀经验 | 怪物 `expReward`（绿史莱姆 30 / 野猪 60）；击杀者获 Exp，满阈值升级 |
| 升级效果 | Level+1、重算 BaseAtk/Def/MaxHp/MaxMp、满血满蓝、广播 `LevelUpNotify` + `ExpGainNotify` |
| 持久化 | `level` / `exp`（及 MaxHp/MaxMp）三后端一致；重启 Host 后保留 |
| 等级装限制 | 例：`item_iron_sword` 需 Lv.2+；升级后可穿 |
| 消耗品 | `UseItemReq/Resp`（MsgId 2401/2402）；`healHp`/`healMp`；仅存活可用 |
| Lua 钩子 | `on_level_up`、`on_use_item`（`scripts/level.lua`） |

### M4 规则（仍生效）
装备槽 `weapon/armor/ring`、穿戴/卸下、强化 `+1..+5`、Effective 面板挂钩权威战斗。

### M3 / M2（仍生效）
NPC 商店/任务、安全区禁攻、击杀掉落归属。

## 1. 目录结构
```text
legend-platform/
├── protocol/                    # Protobuf 契约 (VERSION=1.5.0)
├── server/
│   ├── Legend.Contracts/        # MsgId … 2301–2306 装备 + 2401–2404 升级/用药
│   ├── Legend.Core/             # VersionPackLoader(+levels.json)
│   ├── Legend.Storage/          # level / exp / equipment / inventory 持久化
│   ├── Legend.Game/             # LevelCurve + GrantExp + UseItem
│   ├── Legend.Script/           # on_level_up / on_use_item / on_equip / …
│   ├── Legend.CliClient/        # --level / --equip 全链路冒烟
│   └── Legend.Tests/            # M0–M5（25 tests）
├── client/unity-sdk/            # + RequestUseItemAsync；EnterWorld 含 level/exp
├── version-packs/sample-classic/# levels.json + level.lua + items healHp
└── scripts/verify-m5.sh
```

## 2. 快速验证
```bash
./scripts/verify-m5.sh
# 回归：
./scripts/verify-m4.sh
./scripts/verify-m3.sh
```

### 启动（Sqlite 默认）
```bash
dotnet run --project server/Legend.Host -- --sqlite data/legend.db \
  --manifest version-packs/sample-classic/manifest.json --port 7100
```

### 手动验收（经验升级 + 药水 + 重启持久化）
1. Handshake `1.5.0` → Register/Login → CreateChar (`warrior`) → EnterWorld（`Level=1 Exp=0 ExpToNext=50 Atk=25 MaxHp=180 MaxMp=40`）
2. 购 `item_hp_potion`×2 与 `item_iron_sword`；Lv.1 穿戴铁剑应 `ErrEquipDenied`
3. 出安全区击杀绿史莱姆（30 Exp）直至升级 → `LevelUpNotify`（Lv.2 Atk=28 MaxHp=200），满血满蓝
4. Lv.2 穿戴铁剑 → `EffectiveAtk=43`；受击后 `UseItem(item_hp_potion)` HP 回升且不超过 Max
5. 重启 Host 后 EnterWorld：`level/exp`、装备、背包药水完整保留

CLI：`dotnet run --project server/Legend.CliClient -- --host 127.0.0.1 --port 7100 --level`

## 3. Unity 接入
见 `client/unity-sdk/README.md`。协议 `1.5.0`；新增 `RequestUseItemAsync`；EnterWorld / LevelUp / ExpGain 同步 level/exp/effective stats。

## 4. 环境变量
| 变量 | 含义 |
|------|------|
| `LEGEND_STORAGE` | `InMemory` / `Sqlite` / `Postgres` |
| `LEGEND_SQLITE_PATH` | Sqlite 文件路径 |
| `LEGEND_PG` / `LEGEND_DB_CONN` | Npgsql 连接串 |
| `LEGEND_REDIS` / `LEGEND_REDIS_CONN` | Redis 连接串 |

## 5. 建议 M6
**行会与社交（Guild & Chat）**，或 **技能学习/快捷栏与 Buff 状态**，或 **交易行/摆摊最小闭环**。
