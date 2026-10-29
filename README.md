# Legend Platform — 原创传奇类三端多版本运营平台 (Phase A / M18)

> **100% 原创自研声明**：本仓库采用自有 Protobuf 协议、自有帧格式 (`[Len:4][MsgId:2][Seq:4][Payload]`)、自有版本包 (`version-packs/`) 与自有白名单 Lua 沙箱 API (`legend_api.*`)，严禁任何对第三方闭源引擎的逆向、破解或协议兼容。能力对标经典传奇玩法，**非协议/资源兼容 996**。

## 当前里程碑：M18（行会仓库审计流水与副会长/仓管职级权限）

协议版本 **1.18.0**。在 M17 共享仓库之上新增职级（Leader / ViceLeader / WarehouseAdmin / Member）与仓库审计流水：会长任命/撤职；仓管可 Clear；Deposit/Withdraw/Clear 记流水并可 ListAudit。三后端 `ranks_json` + `guild_warehouse_audit` 持久化。

| 项 | 约定 |
|----|------|
| 职级 | `leader` / `vice_leader` / `warehouse_admin` / `member`；会长 `GuildSetRank` 任命/撤职 |
| 配置 | `guild.json`：`viceLeaderCanClear`/`warehouseAdminCanClear`/`memberCanClear`、`maxViceLeaders`、`maxWarehouseAdmins`、`auditLogLimit` |
| Clear | 会长/副会长/仓管按配置可 Clear；普通会员默认否（存取仍按 M17） |
| 协议 | `GuildSetRank`（3701–3702）、`GuildWarehouseListAudit`（3703–3704）；`ERR_GUILD_RANK_DENIED=1077`、`ERR_GUILD_INVALID_RANK=1078` |
| 审计 | Deposit/Withdraw/Clear → `guild_warehouse_audit`（操作者/物品/数量/时间）；ListAudit |
| 持久化 | `guilds.ranks_json` + `guild_warehouse_audit`（InMemory / Sqlite / Postgres） |
| Lua | `on_guild_rank_change` / `on_warehouse_audit`（仓库钩子保留） |
| 验收 | CLI `--guild-audit`；`M18IntegrationTests`；`scripts/verify-m18.sh` |

### M16（宠物召唤跟随与轻量战斗协助）

| 项 | 约定 |
|----|------|
| 协议 | PetStatus/Summon/Dismiss/Sync（3501–3511）；`ERR_PET_DENIED` |
| 验收 | CLI `--pet`；`verify-m16.sh` |

### M15 / M14 / …

坐骑时装、世界 Boss、组队副本、邮件、攻城等仍生效。

## 1. 目录结构
```text
legend-platform/
├── protocol/                    # Protobuf 契约 (VERSION=1.18.0)
├── server/
│   ├── Legend.Contracts/        # MsgId 3601–3608 行会仓；3501–3511 宠物
│   ├── Legend.Core/             # VersionPackLoader(+guild warehouse 字段)
│   ├── Legend.Game/             # GameWorldModule.GuildWarehouse 存取/清空
│   ├── Legend.Storage/          # guilds.warehouse_json 三后端
│   ├── Legend.Script/           # on_guild_warehouse_*
│   ├── Legend.CliClient/        # --guild-wh / --pet 验收
│   └── Legend.Tests/            # M0–M18
├── client/unity-sdk/            # GuildRanks/Audit + Warehouse MsgId (1.18.0)
├── version-packs/sample-classic/# guild.json 仓权限 + guild.lua
└── scripts/verify-m17.sh
```

## 2. 快速验证
```bash
./scripts/verify-m17.sh
# 回归：
./scripts/verify-m16.sh
./scripts/verify-m15.sh
```

### 启动（Sqlite）
```bash
dotnet run --project server/Legend.Host -- --port 7100 \
  --manifest version-packs/sample-classic/manifest.json --storage Sqlite --sqlite data/legend.db
```

### 手动验收（行会职级与仓库审计）
1. Handshake `1.18.0` → Register/Login → CreateChar → EnterWorld（会长 + 会员 + 仓管）
2. 会长创建行会，会员与仓管加入；会员尝试 `GuildSetRank` → `ERR_GUILD_RANK_DENIED`
3. 会长任命仓管为 `warehouse_admin`、另一人为 `vice_leader`；`GuildInfo` 职级正确
4. 存取若干次后，会长 `GuildWarehouseListAudit` 可见 deposit/withdraw 流水
5. 普通会员 Clear 拒绝；仓管 Clear 成功并出现 clear 流水
6. 重启 Host，审计与职级仍在


## 3. Unity 接入
见 `client/unity-sdk/README.md`。协议 `1.18.0`；`RequestGuildSetRank` / `RequestGuildWarehouseListAudit`；Warehouse/Pet/Mount 同前。

## 4. 环境变量
| 变量 | 含义 |
|------|------|
| `LEGEND_STORAGE` | `InMemory` / `Sqlite` / `Postgres` |
| `LEGEND_SQLITE_PATH` | Sqlite 文件路径 |
| `LEGEND_PG` / `LEGEND_DB_CONN` | Npgsql 连接串 |
| `LEGEND_REDIS` / `LEGEND_REDIS_CONN` | Redis 连接串 |
