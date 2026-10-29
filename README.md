# Legend Platform — 原创传奇类三端多版本运营平台 (Phase A / M19)

> **100% 原创自研声明**：本仓库采用自有 Protobuf 协议、自有帧格式 (`[Len:4][MsgId:2][Seq:4][Payload]`)、自有版本包 (`version-packs/`) 与自有白名单 Lua 沙箱 API (`legend_api.*`)，严禁任何对第三方闭源引擎的逆向、破解或协议兼容。能力对标经典传奇玩法，**非协议/资源兼容 996**。

## 当前里程碑：M19（在线人数、封禁禁言、攻城开关与版本包热重载管理API）

协议版本 **1.19.0**。在 Host 旁路端口提供轻量 HTTP Admin API（Token 鉴权）：在线摘要、封禁/禁言落库并在登录/聊天权威拒绝、攻城开关复用现有 siege 控制、VersionPack 安全热重载（失败回滚）。

| 项 | 约定 |
|----|------|
| 启动 | `--admin-port` / `--admin-token` / `--admin-bind`；或 `LEGEND_ADMIN_PORT` / `LEGEND_ADMIN_TOKEN` / `LEGEND_ADMIN_BIND` |
| 鉴权 | `Authorization: Bearer <token>` 或 `X-Admin-Token` |
| GET `/admin/online` | 在线人数与角色摘要（characterId/name/accountId/mapId/level/guildId） |
| POST `/admin/ban` | `username/accountId/characterName + banned/reason/durationSeconds|accountId\|characterName, banned, reason, durationSeconds}`；落库；踢在线；登录 `ERR_BANNED` |
| POST `/admin/mute` | 同上结构（`muted`）；聊天权威拒绝；Login/EnterWorld 同步 `is_muted` |
| POST `/admin/siege` | `{action: start\|stop}` 复用攻城控制 |
| POST `/admin/reload-pack` | 可选 `{manifestPath}`；配置/Lua 热更，失败回滚 |
| 持久化 | `account_moderation`（InMemory / Sqlite / Postgres） |
| 协议 | `ERR_BANNED=1079`、`ERR_MUTED=1080`；LoginResp/EnterWorldResp 增 mute 字段 |
| 验收 | curl（`verify-m19.sh`）；`M19IntegrationTests` |

### M18（行会仓库审计流水与副会长/仓管职级权限）

| 项 | 约定 |
|----|------|
| 协议 | GuildSetRank / ListAudit；`ERR_GUILD_RANK_*`；职级与审计流水 |
| 验收 | CLI `--guild-audit`；`verify-m18.sh` |

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
├── protocol/                    # Protobuf 契约 (VERSION=1.19.0)
├── server/
│   ├── Legend.Contracts/        # MsgId 3601–3608 行会仓；3501–3511 宠物
│   ├── Legend.Core/             # VersionPackLoader(+guild warehouse 字段)
│   ├── Legend.Game/             # GameWorldModule.GuildWarehouse 存取/清空
│   ├── Legend.Storage/          # guilds.warehouse_json 三后端
│   ├── Legend.Script/           # on_guild_warehouse_*
│   ├── Legend.CliClient/        # --guild-wh / --pet 验收
│   └── Legend.Tests/            # M0–M19
├── client/unity-sdk/            # Admin mute/ban sync + MsgId (1.19.0)
├── version-packs/sample-classic/# guild.json 仓权限 + guild.lua
└── scripts/verify-m19.sh
```

## 2. 快速验证
```bash
./scripts/verify-m19.sh
# 回归：
./scripts/verify-m16.sh
./scripts/verify-m15.sh
```

### 启动（Sqlite + Admin）
```bash
dotnet run --project server/Legend.Host -- --port 7100 \
  --manifest version-packs/sample-classic/manifest.json --storage Sqlite --sqlite data/legend.db \
  --admin-port 7101 --admin-token "$LEGEND_ADMIN_TOKEN" --admin-bind 127.0.0.1
```

### 手动验收（Admin API）
1. `curl -H "Authorization: Bearer $TOKEN" http://127.0.0.1:7101/admin/online`
2. 玩家进服后 online≥1；`POST /admin/mute` 后聊天被拒；`POST /admin/ban` 后无法登录（`ERR_BANNED`）
3. `POST /admin/siege` `{"action":"start"}` / `stop` 开关攻城
4. `POST /admin/reload-pack` 热重载成功；错误路径回滚
5. 重启 Host，ban/mute 仍在（Sqlite/Postgres）


## 3. Unity 接入
见 `client/unity-sdk/README.md`。协议 `1.19.0`；`is_muted` / `ERR_BANNED`；`RequestGuildSetRank` / `RequestGuildWarehouseListAudit`；Warehouse/Pet/Mount 同前。

## 4. 环境变量
| 变量 | 含义 |
|------|------|
| `LEGEND_STORAGE` | `InMemory` / `Sqlite` / `Postgres` |
| `LEGEND_SQLITE_PATH` | Sqlite 文件路径 |
| `LEGEND_PG` / `LEGEND_DB_CONN` | Npgsql 连接串 |
| `LEGEND_REDIS` / `LEGEND_REDIS_CONN` | Redis 连接串 |
