# Legend Platform — 原创传奇类三端多版本运营平台 (Phase A / M17)

> **100% 原创自研声明**：本仓库采用自有 Protobuf 协议、自有帧格式 (`[Len:4][MsgId:2][Seq:4][Payload]`)、自有版本包 (`version-packs/`) 与自有白名单 Lua 沙箱 API (`legend_api.*`)，严禁任何对第三方闭源引擎的逆向、破解或协议兼容。能力对标经典传奇玩法，**非协议/资源兼容 996**。

## 当前里程碑：M17（行会仓库共享存取与权限校验）

协议版本 **1.17.0**。在 M16 宠物之上新增行会共享仓库：会员可存取个人背包↔行会仓（原子），会长可清空；未入会拒绝。三后端 `guilds.warehouse_json` 持久化，重启保留。

| 项 | 约定 |
|----|------|
| 配置 | `configs/guild.json`：`warehouseCapacitySlots=40`、`warehouseMaxStackPerItem=999`、`memberCanDeposit/Withdraw=true`、`leaderCanClear=true` |
| 权限 | 会员存/取（可配置）；仅会长清空；未入会 → `ERR_NOT_IN_GUILD` |
| 协议 | `GuildWarehouseList/Deposit/Withdraw/Clear`（3601–3608）；`ERR_GUILD_WAREHOUSE_DENIED=1075`、`ERR_GUILD_WAREHOUSE_FULL=1076` |
| 原子性 | 存：扣背包→写入会仓→双写持久化；取：扣仓→加背包 |
| 持久化 | `guilds.warehouse_json`（InMemory / Sqlite / Postgres，含迁移） |
| Lua | `on_guild_warehouse_deposit` / `on_guild_warehouse_withdraw` / `on_guild_warehouse_clear` |
| 验收 | CLI `--guild-wh`；`M17IntegrationTests`；`scripts/verify-m17.sh` |

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
├── protocol/                    # Protobuf 契约 (VERSION=1.17.0)
├── server/
│   ├── Legend.Contracts/        # MsgId 3601–3608 行会仓；3501–3511 宠物
│   ├── Legend.Core/             # VersionPackLoader(+guild warehouse 字段)
│   ├── Legend.Game/             # GameWorldModule.GuildWarehouse 存取/清空
│   ├── Legend.Storage/          # guilds.warehouse_json 三后端
│   ├── Legend.Script/           # on_guild_warehouse_*
│   ├── Legend.CliClient/        # --guild-wh / --pet 验收
│   └── Legend.Tests/            # M0–M17
├── client/unity-sdk/            # GuildWarehouse + Pet MsgId (1.17.0)
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

### 手动验收（行会仓库）
1. Handshake `1.17.0` → Register/Login → CreateChar → EnterWorld（会长 + 会员 + 局外人）
2. 局外人 `GuildWarehouseList` → `ERR_NOT_IN_GUILD`
3. 会长创建行会，会员加入；商店购买 `item_hp_potion`
4. 会长 `GuildWarehouseDeposit` → 背包减少、仓 UsedSlots+1；会员 List 可见
5. 会员 `GuildWarehouseWithdraw` → 仓减少、会员背包增加
6. 会员 `GuildWarehouseClear` → `ERR_GUILD_WAREHOUSE_DENIED`；会长 Clear 成功
7. 重启 Host 后 List 仍见重启前未清空的仓物品

CLI：`dotnet run --project server/Legend.CliClient -- --host 127.0.0.1 --port 7100 --guild-wh`

## 3. Unity 接入
见 `client/unity-sdk/README.md`。协议 `1.17.0`；`RequestGuildWarehouseList/Deposit/Withdraw/Clear`；Pet/Mount/Fashion 同前。

## 4. 环境变量
| 变量 | 含义 |
|------|------|
| `LEGEND_STORAGE` | `InMemory` / `Sqlite` / `Postgres` |
| `LEGEND_SQLITE_PATH` | Sqlite 文件路径 |
| `LEGEND_PG` / `LEGEND_DB_CONN` | Npgsql 连接串 |
| `LEGEND_REDIS` / `LEGEND_REDIS_CONN` | Redis 连接串 |
