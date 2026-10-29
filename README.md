# Legend Platform — 原创传奇类三端多版本运营平台 (Phase C/D)

> **100% 原创自研声明**：本仓库采用自有 Protobuf 协议、自有帧格式 (`[Len:4][MsgId:2][Seq:4][Payload]`)、自有版本包 (`version-packs/`) 与自有白名单 Lua 沙箱 API (`legend_api.*`)，严禁任何对第三方闭源引擎的逆向、破解或协议兼容。能力对标经典传奇玩法，**非协议/资源兼容 996**。

## 当前里程碑：Phase C/D（多区开区编排 · 合区工具链 · 支付沙箱闭环）

协议版本 **1.20.0**。在 M19 Admin API 之上增加：

| 项 | 约定 |
|----|------|
| 多区配置 | `realms/*.json` + `realms/catalog.json`；Host `--realm-config` / `--realm-id` / `--realm-catalog` |
| 开区 | `scripts/create-realm.sh <id> [name] [port] [admin-port]` 生成配置与 catalog |
| RealmList | Login 返回 catalog 内全部区服；本区端口为实际 BoundPort |
| 合区 | `tools/Legend.Merge`：同账号合并角色、重名 `_m` 后缀、同行会改名；`--dry-run` / 正式合区 |
| 合区规则 | `tools/Legend.Merge/MERGE_RULES.md` |
| 支付抽象 | `IPaymentProvider` + `MockPaymentProvider`（HMAC）；商品目录 `PaymentCatalog` |
| 游戏协议 | MsgId 3801–3805 PaymentCreate/Status/Fulfilled；`ERR_PAYMENT_*` |
| Webhook | `POST /admin/payment/webhook`（`X-Payment-Signature`，不走 Admin Token） |
| 管理充值 | `POST /admin/recharge`（Bearer Token；`characterName/characterId + productId`） |
| 发货 | 幂等：Paid→邮件附件金币/道具→Fulfilled；重复回调 `already_fulfilled` |
| 验收 | `./scripts/verify-phase-cd.sh`；`PhaseCdIntegrationTests` |

### M19（Admin 在线/封禁/禁言/攻城/热重载）仍生效

| 项 | 约定 |
|----|------|
| GET `/admin/online` | 在线摘要 |
| POST `/admin/ban` `/admin/mute` `/admin/siege` `/admin/reload-pack` | 同 M19 |
| 验收 | `verify-m19.sh` |

### M18 / M16 / …

行会仓库审计、宠物、坐骑时装、世界 Boss、组队副本、邮件、攻城等仍生效。

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
├── realms/                     # 多区配置 catalog + realm-*.json
├── tools/Legend.Merge/         # 合区工具 + MERGE_RULES.md
└── scripts/verify-phase-cd.sh / verify-m19.sh
```

## 2. 快速验证
```bash
./scripts/verify-phase-cd.sh
# 回归：
./scripts/verify-m19.sh
./scripts/verify-m18.sh
```

### 启动双区（示例）
```bash
# 开新区
./scripts/create-realm.sh realm-classic-03 '自研先锋三区' 7300 7301

# 区1
dotnet run --project server/Legend.Host -- \
  --realm-id realm-classic-01 --realm-catalog realms \
  --admin-token "$LEGEND_ADMIN_TOKEN" --admin-bind 127.0.0.1 \
  --payment-secret "$LEGEND_PAYMENT_SECRET"

# 区2（另一进程）
dotnet run --project server/Legend.Host -- \
  --realm-config realms/realm-classic-02.json --realm-catalog realms \
  --admin-token "$LEGEND_ADMIN_TOKEN" --admin-bind 127.0.0.1
```

### 合区
```bash
dotnet run --project tools/Legend.Merge -- \
  --source-a data/realms/realm-classic-01.db \
  --source-b data/realms/realm-classic-02.db \
  --target data/realms/merged.db --dry-run

dotnet run --project tools/Legend.Merge -- \
  --source-a data/realms/realm-classic-01.db \
  --source-b data/realms/realm-classic-02.db \
  --target data/realms/merged.db
```

### 支付沙箱
1. 进服后 `PaymentCreateOrder`（商品 `gold_100` / `gold_1000` / `item_hp_pack`）
2. `POST /admin/payment/webhook`，canonical=`orderId|txn|paid`，`X-Payment-Signature`=HMAC-SHA256(secret)
3. 或 `POST /admin/recharge` 管理直充；邮件领取金币/道具
4. 重复 webhook 幂等



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
