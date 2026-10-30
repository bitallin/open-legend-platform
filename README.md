# Legend Platform — 原创传奇类三端多版本运营平台 (Phase E)

> **100% 原创自研声明**：本仓库采用自有 Protobuf 协议、自有帧格式 (`[Len:4][MsgId:2][Seq:4][Payload]`)、自有版本包 (`version-packs/`) 与自有白名单 Lua 沙箱 API (`legend_api.*`)，严禁任何对第三方闭源引擎的逆向、破解或协议兼容。能力对标经典传奇玩法，**非协议/资源兼容 996**。

## 生产完成度（事实来源）

可玩演示已合并，**还不等于生产版本**。玩法、画面、音效、PC 操作的条目与状态以 [docs/MIR_PRODUCTION_FEATURE_MATRIX.md](docs/MIR_PRODUCTION_FEATURE_MATRIX.md) 为准，进度数字以 [docs/DEV_PROGRESS.md](docs/DEV_PROGRESS.md) 为准。协议增补记在 [docs/PROTOCOL_NOTES.md](docs/PROTOCOL_NOTES.md)。资源只允许原创 / CC0 / 程序生成。

## 文档站（功能规格）

引擎、服务端、客户端、登录器的功能规格在 [docs/site](docs/site/index.md)，按 00–22 一文一域。完成度只维护矩阵，文档站不另做一套状态表。

生成静态站（.NET SDK 8，DocFX 2.78）：

```bash
dotnet tool install -g docfx --version 2.78.3
export PATH="$PATH:$HOME/.dotnet/tools"
docfx build docs/site/docfx.json
# 浏览器打开 docs/site/_site/index.html
```

也可以运行 `./scripts/build-docs-site.sh`。只改文档时，CI 工作流 `.github/workflows/docs-site.yml` 会执行同样的 `docfx build`，不跑游戏验收脚本。

## 可玩 PC 客户端（验收路径，无需 Unity）

协议 **1.21.0**。Windows 玩家包用 `dotnet publish` 生成，不依赖 Unity Editor。登录器选区后启动 `LegendClient.exe`，连上公网试玩服即可移动、打怪、拾取、聊天。

```bat
scripts\publish-playable-windows.bat
dist\windows-player\LegendClient.exe --host 150.158.164.90 --port 7100 --realm-id realm-classic-01
```

操作：WASD / 方向键移动，鼠标左键点地或选中，右键或 Space / J 攻击，Tab 切目标，B 背包，Enter 聊天，Z / G 拾取。生产键位以功能清单第 18 节为准。说明见 [docs/PLAYABLE_PC.md](docs/PLAYABLE_PC.md)。

## 当前里程碑：Phase J（Android 导出/打包流水线骨架）

协议仍 **1.21.0**。Unity MVP 增加 Android IL2CPP/ARM64 批处理入口与签名模板；本环境无 Editor，验收靠骨架脚本。

| 项 | 约定 |
|----|------|
| Editor | `client/unity-mvp/Assets/Editor/AndroidBuild.cs`（`Legend.Build.Android.BuildApk` / `BuildAab`） |
| 模板 | `client/unity-mvp/AndroidBuild/`（keystore example、player-settings defaults） |
| 构建 | `./scripts/build-android.sh`（有 Unity）；无 Unity 时非 0 + 安装指引 |
| 验收 | `./scripts/verify-phase-j-android.sh`；文档 `docs/ANDROID_BUILD.md` |
| 产物 | `dist/android/`（真实 APK 需本机 Unity Android Module） |

### 此前：Phase H（PC 登录器骨架）


协议仍 **1.21.0**。新增 `launcher/`：选区、TCP 延迟探测、更新检查骨架（`IUpdateChannel`）、启动 Unity/占位客户端。

| 项 | 约定 |
|----|------|
| 工程 | `launcher/Legend.Launcher.sln`（Core / Console / ClientStub / Tests） |
| 配置 | `launcher/config/{launcher,realms,version,update-manifest.sample}.json` |
| 验收 | `./scripts/verify-phase-h-launcher.sh`；文档 `docs/LAUNCHER.md` |
| 占位客户端 | `Legend.ClientStub` / `scripts/fake-client.sh` |

### 此前：Phase E（微信 / 支付宝商户沙箱适配）


协议版本 **1.21.0**。在 Phase C/D 多区 · 合区 · mock 支付之上增加真实商户沙箱对接层（无密钥时走本地 `sandbox_sim`，插密钥即可用）：

| 项 | 约定 |
|----|------|
| Provider | `mock` / `wechat` / `alipay`；`--payment-provider` + `LEGEND_PAYMENT_PROVIDER` |
| 接口 | `IMerchantPaymentProvider`：`CreateCheckoutAsync` / `ParseAndVerifyNotifyAsync` |
| 微信 | APIv3 风格；`POST /admin/payment/wechat/notify`；env `WECHAT_*` |
| 支付宝 | RSA2；`POST /admin/payment/alipay/notify`；env `ALIPAY_*` |
| 协议字段 | `PaymentCreateOrderResp`：`provider_id` / `checkout_url` / `pay_payload` / `provider_order_id` |
| 本地模拟 | `LocalWechatSandboxSimulator` / `LocalAlipaySandboxSimulator`；Admin `build-notify` |
| 验收 | `./scripts/verify-phase-e-payment.sh`；`PhaseEPaymentIntegrationTests`；`verify-phase-cd.sh` 仍绿 |
| 文档 | `docs/PAYMENT_SANDBOX.md` |

### Phase C/D（仍生效）

| 项 | 约定 |
|----|------|
| 多区配置 | `realms/*.json` + `realms/catalog.json`；Host `--realm-config` / `--realm-id` / `--realm-catalog` |
| 开区 | `scripts/create-realm.sh <id> [name] [port] [admin-port]` 生成配置与 catalog |
| RealmList | Login 返回 catalog 内全部区服；本区端口为实际 BoundPort |
| 合区 | `tools/Legend.Merge`：同账号合并角色、重名 `_m` 后缀、同行会改名；`--dry-run` / 正式合区 |
| 合区规则 | `tools/Legend.Merge/MERGE_RULES.md` |
| Mock 支付 | `MockPaymentProvider`（HMAC）；商品 `gold_100` / `gold_1000` / `item_hp_pack` |
| Webhook | `POST /admin/payment/webhook`（`X-Payment-Signature`） |
| 管理充值 | `POST /admin/recharge` |
| 发货 | 幂等；金额与 `PriceCents` 不符拒绝 |

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
├── protocol/                    # Protobuf 契约 (VERSION=1.21.0)
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
├── tools/payment-sandbox-sim/ # 微信/支付宝 notify 样例生成
├── certs/payment/              # *.example.pem 占位（勿放真实密钥）
├── launcher/                   # Phase H 登录器骨架
├── docs/PAYMENT_SANDBOX.md     # 商户沙箱接入说明
├── docs/LAUNCHER.md
└── scripts/verify-phase-h-launcher.sh / verify-phase-e-payment.sh / …
```

## 2. 快速验证
```bash
./scripts/verify-phase-e-payment.sh
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
详见 **[docs/PAYMENT_SANDBOX.md](docs/PAYMENT_SANDBOX.md)**。

1. 进服后 `PaymentCreateOrder`（商品 `gold_100` / `gold_1000` / `item_hp_pack`）→ 含 `provider_id` / `pay_payload` / `checkout_url`
2. Mock：`POST /admin/payment/webhook`，canonical=`orderId|txn|paid`，`X-Payment-Signature`=HMAC-SHA256(secret)
3. 微信模拟：`POST /admin/payment/wechat/notify`（可用 `/admin/payment/sandbox-sim/build-notify` 生成签名体）
4. 支付宝模拟：`POST /admin/payment/alipay/notify`
5. 或 `POST /admin/recharge` 管理直充；邮件领取；重复回调幂等
6. 真实商户：配置 `WECHAT_*` / `ALIPAY_*` 后 `--payment-provider wechat|alipay`



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
