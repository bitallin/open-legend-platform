# Legend Platform — 原创传奇类三端多版本运营平台 (Phase A / M12)

> **100% 原创自研声明**：本仓库采用自有 Protobuf 协议、自有帧格式 (`[Len:4][MsgId:2][Seq:4][Payload]`)、自有版本包 (`version-packs/`) 与自有白名单 Lua 沙箱 API (`legend_api.*`)，严禁任何对第三方闭源引擎的逆向、破解或协议兼容。能力对标经典传奇玩法，**非协议/资源兼容 996**。

## 当前里程碑：M12（独立副本实例与进本清怪奖励回城闭环）

协议版本 **1.12.0**。在 M11 邮件基础上新增独立副本最小闭环：进本 → 实例内清怪 → 结算奖励 → 回城。

| 项 | 约定 |
|----|------|
| 副本配置 | `configs/dungeons.json` + `maps/dungeon_slime_cave.json`；示例本 `dungeon_slime_cave`（等级门槛、可选金币门票、击杀全部目标、奖励金币+药水）；`dungeon_level_gate` 用于等级拒进验收 |
| 实例隔离 | 每次进入分配独立 `instanceId` / `inst_*` 地图切片与 GridAOI；队伍/玩家互不可见；退出或完成后回收内存实例 |
| 进入条件 | 等级门槛 + 可选 `entryCostGold`；不足返回 `ErrDungeonLevel` / `ErrDungeonCost` |
| 协议 | `DungeonEnter/Progress/Complete/Leave`（3201–3206）；成功进本附带 `MapChangeNotify` |
| 奖励 | 清怪完成发金币+道具即时入包；背包满走 **M11 邮件附件**；未完成 `Leave` **无清怪奖励** |
| 死亡 | 副本内死亡回**副本出生点满血**，不强制踢出 |
| Host 重启 | 内存实例丢失；断线时持久化回城坐标；若库中残留 `inst_*` 则进世界时 Sanitize 回主城 |
| VersionPack / Lua | `on_dungeon_enter` / `on_dungeon_complete` / `on_dungeon_leave`（`scripts/dungeon.lua`） |
| 验收 | CLI `--dungeon`；`M12IntegrationTests`；`scripts/verify-m12.sh` |

### M11（邮件系统与离线奖励/摆摊成交补发）

| 项 | 约定 |
|----|------|
| 邮件协议 | `MailList/Read/ClaimAttachment/Delete`（3101–3108）、`MailNotify`（3109）、`MailSend`（3110–3111） |
| 领取与删除 | 附件原子领取；背包满失败保留邮件；未领附件禁止删除 |
| 三后端持久化 | `IMailStore` / InMemory / Sqlite / Postgres |
| 摆摊/攻城离线 | 卖家离线成交与攻城结算经系统邮件附件发放 |
| 验收 | CLI `--mail`；`M11IntegrationTests`；`scripts/verify-m11.sh` |

### M10（沙巴克/据点争夺战最小闭环）

| 项 | 约定 |
|----|------|
| 攻城配置与皇宫区域 | `configs/siege.json`（`sabuk_castle`） |
| 协议 | `SiegeDeclare/Status/StateNotify/Control/Capture`（2901–2909） |
| 验收 | CLI `--siege`；`M10IntegrationTests`；`scripts/verify-m10.sh` |

### M9 / M8 / M7 / M6 / M5 / M4 / M3 / M2（仍生效）
技能书/Buff、组队/好友私聊、摆摊交易、行会与聊天、升级/药水、装备强化、NPC 商店/任务、安全区禁攻、击杀掉落归属。

## 1. 目录结构
```text
legend-platform/
├── protocol/                    # Protobuf 契约 (VERSION=1.12.0)
├── server/
│   ├── Legend.Contracts/        # MsgId … 3201–3206 副本
│   ├── Legend.Core/             # VersionPackLoader(+dungeons.json)
│   ├── Legend.Storage/          # 角色坐标 Sanitize / 邮件附件复用
│   ├── Legend.Game/             # GameWorldModule.Dungeon 实例隔离
│   ├── Legend.Script/           # on_dungeon_enter/complete/leave
│   ├── Legend.CliClient/        # --dungeon 验收
│   └── Legend.Tests/            # M0–M12
├── client/unity-sdk/            # DungeonEnter/Leave + Progress/Complete (1.12.0)
├── version-packs/sample-classic/# dungeons.json + dungeon_slime_cave map + dungeon.lua
└── scripts/verify-m12.sh
```

## 2. 快速验证
```bash
./scripts/verify-m12.sh
./scripts/verify-m11.sh
./scripts/verify-m10.sh
# 回归：
./scripts/verify-m9.sh
./scripts/verify-m8.sh
```

### 启动（Sqlite）
```bash
dotnet run --project server/Legend.Host -- --port 7100 \
  --manifest version-packs/sample-classic/manifest.json --storage Sqlite --sqlite data/legend.db
```

### 手动验收（副本闭环）
1. Handshake `1.12.0` → Register/Login → CreateChar → EnterWorld
2. `DungeonEnter dungeon_level_gate` → `ErrDungeonLevel`；`DungeonEnter dungeon_slime_cave` 扣门票进 `inst_*`
3. 清怪 → `DungeonProgressNotify` → `DungeonCompleteNotify` 发奖并回城
4. 再进本后 `DungeonLeave`（未完成无清怪奖励）回城
5. 进本后杀 Host 重启：重登应在主城，不卡在丢失实例

CLI：`dotnet run --project server/Legend.CliClient -- --host 127.0.0.1 --port 7100 --dungeon`

## 3. Unity 接入
见 `client/unity-sdk/README.md`。协议 `1.12.0`；新增 `RequestDungeonEnterAsync` / `RequestDungeonLeaveAsync`，事件 `OnDungeonProgress` / `OnDungeonComplete`。

## 4. 环境变量
| 变量 | 含义 |
|------|------|
| `LEGEND_STORAGE` | `InMemory` / `Sqlite` / `Postgres` |
| `LEGEND_SQLITE_PATH` | Sqlite 文件路径 |
| `LEGEND_PG` / `LEGEND_DB_CONN` | Npgsql 连接串 |
| `LEGEND_REDIS` / `LEGEND_REDIS_CONN` | Redis 连接串 |

## 5. 建议 M13
**组队进本（共享实例）**、**副本排行/每日次数持久化**，或 **坐骑/时装外观同步**，或 **跨服匹配入本**。
