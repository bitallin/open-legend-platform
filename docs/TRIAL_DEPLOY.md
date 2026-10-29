# 试玩档下单与部署手册

> 完整可操作版见 Cursor Page《试玩档下单与部署手册》。数字取自腾讯云轻量 [价格总览](https://cloud.tencent.com/document/product/1207/73452)（文档更新 **2026-09-24**；抓取日 **2026-10-29**）。预算总览见 `docs/PUBLIC_DEPLOY_BUDGET.md`。

## 主推规格（照着点）

| 项 | 值 |
|----|-----|
| 厂商 / 产品 | **腾讯云 · 轻量应用服务器（Lighthouse）** |
| 地域 | **上海**（华东；北京亦可） |
| 套餐 | **锐驰型 · 2 核 · 4 GB** |
| 系统盘 | 50 GB SSD |
| 带宽 / 流量 | 峰值 **200 Mbps** · **无限流量** |
| 镜像 | **Ubuntu Server 22.04 LTS** 64 位 |
| 时长 | 包月（1–5 月无时长折；≥6 月 88 折；≥12 月 85 折） |
| 目录月费 | **65 元/月** |
| 备选 | 阿里云轻量 · 华东 · 2核2G 约 45 元/月（社区整理；以下单页为准） |

同价可换：入门型 2C4G / 60GB / 5Mbps / 500GB = 65 元/月。更省：入门型 2C2G / 40GB / 3Mbps / 200GB = 40 元/月（内存偏紧，仅自测）。内地超额流量 0.8 元/GB。

| 项 | 主推锐驰 | 同价入门 | 更省入门 |
|----|----------|----------|----------|
| CPU/内存 | 2C4G | 2C4G | 2C2G |
| 盘 | 50GB | 60GB | 40GB |
| 带宽 | 峰值200M | 5M | 3M |
| 流量 | 无限 | 500GB | 200GB |
| 月费 | 65元 | 65元 | 40元 |

价目：https://cloud.tencent.com/document/product/1207/73452 · 产品页：https://cloud.tencent.com/product/lighthouse

## 安全组 / 防火墙

Legend.Host 默认游戏 TCP **7100**（`--port` 可改）；Admin 默认 `--admin-bind 127.0.0.1` + `--admin-port`（区服示例 **7101**）。支付 Webhook 挂在 Admin 路径下，**禁止对公网开放**。

| 方向 | 协议/端口 | 来源 | 动作 |
|------|-----------|------|------|
| 入站 | TCP 7100（或你改的游戏端口） | 0.0.0.0/0 或玩家网段 | 允许 |
| 入站 | TCP 22（SSH） | 仅你的公网 IP/32 | 允许 |
| 入站 | TCP 7101 / 其它 Admin | 任意公网 | **拒绝** |
| 入站 | 支付 notify / webhook | 任意公网 | **拒绝** |

轻量控制台 → 防火墙。本机可用 `ufw allow 7100/tcp` 与 `ufw allow from YOUR_IP to any port 22`。看 Admin：`ssh -L 7101:127.0.0.1:7101 user@公网IP`。

## ICP 备案边界

大陆机房对大陆用户提供持续可访问的互联网信息服务，须完成网站/APP 等法定备案（ICP）及游戏相关许可，**未备案不得对大陆用户正式开服运营**。本手册只覆盖「买机 + 起服 + 自测」；不构成规避备案的方案。自测可用境外地域，或大陆机仅对自己 IP / 内网联调；正式对公开放前走完备案与资质。

## 部署步骤

1. **下单**：腾讯云控制台 → 轻量 → 上海 → 锐驰型 2C4G（或上表同价/更省）→ Ubuntu 22.04 → 密钥 → 包月；记下公网 IP。
2. **备案提示**：面向大陆用户公网运营须先完成 ICP 等流程；仅自己联调可先跳过，但勿宣传对外开服。
3. **装 .NET 8 Runtime**：`sudo apt update && sudo apt install -y aspnetcore-runtime-8.0`（或微软源 `dotnet-runtime-8.0`）。`dotnet --list-runtimes` 应含 8.x。
4. **上传发布包**（本机仓库根）：
   ```bash
   dotnet publish server/Legend.Host/Legend.Host.csproj -c Release -o /tmp/legend-publish
   # scp 到服务器 /opt/legend/，并拷贝 version-packs/、realms/（及 data/）
   ```
   也可用 `ops/Dockerfile.host` 构建镜像（只映射 7100，勿映射 Admin）。
5. **改区服 host**：编辑 `realms/realm-classic-01.json` 的 `host` 为公网 IP；`port` 7100、`adminPort` 7101、`sqlitePath` 如 `data/realms/realm-classic-01.db`。密钥只放环境变量。
6. **systemd 起服**（见下）→ `systemctl enable --now legend-host`；日志应含 `Legend.Host ready on 0.0.0.0:7100`，Admin=`127.0.0.1:7101`。
7. **冒烟**：
   ```bash
   dotnet run --project server/Legend.CliClient -- --host <公网IP> --port 7100
   ```
   Unity MVP：`LegendClientConfig` Host/Port → 公网 IP:7100。
8. **Sqlite 日备 cron**（Asia/Shanghai）：
   ```cron
   30 3 * * * mkdir -p /var/backups/legend && cp -a /opt/legend/data/realms/*.db /var/backups/legend/$(date +\%Y\%m\%d)/
   ```

## systemd 示例

参数与 `server/Legend.Host/Program.cs` 一致。

```ini
# /etc/systemd/system/legend-host.service
[Unit]
Description=Legend.Host trial realm
After=network.target

[Service]
WorkingDirectory=/opt/legend
Environment=LEGEND_ADMIN_TOKEN=换成长随机串
Environment=LEGEND_PAYMENT_SECRET=换成长随机串
Environment=LEGEND_PAYMENT_PROVIDER=mock
ExecStart=/usr/bin/dotnet /opt/legend/Legend.Host.dll --bind 0.0.0.0 --realm-id realm-classic-01 --realm-catalog realms --admin-port 7101 --admin-bind 127.0.0.1
Restart=on-failure
RestartSec=5
User=legend

[Install]
WantedBy=multi-user.target
```

等价环境变量：`LEGEND_ADMIN_BIND`、`LEGEND_ADMIN_PORT`、`LEGEND_REALM_ID`、`LEGEND_REALM_CATALOG`、`LEGEND_PAYMENT_PROVIDER`。

单机无 realm 时：

```bash
dotnet Legend.Host.dll --bind 0.0.0.0 --port 7100 \
  --manifest version-packs/sample-classic/manifest.json \
  --storage Sqlite --sqlite data/legend.db \
  --admin-port 7101 --admin-token "$LEGEND_ADMIN_TOKEN" --admin-bind 127.0.0.1
```

Admin 隧道内：`curl -H "Authorization: Bearer $LEGEND_ADMIN_TOKEN" http://127.0.0.1:7101/admin/online`。开新区：`./scripts/create-realm.sh <id> [name] [port] [admin-port]`（每区独立进程与 Sqlite）。

## 验收清单（未做）

- [ ] 腾讯云上海锐驰 2C4G 已下单并拿到公网 IP
- [ ] 防火墙仅放行 TCP 7100；SSH 限自己 IP；Admin/支付未对公网
- [ ] 已装 .NET 8；发布包与 version-packs/realms 就位
- [ ] systemd legend-host 运行中；日志含 ready on 0.0.0.0:7100
- [ ] CliClient / Unity MVP 已连公网 IP:7100 冒烟通过
- [ ] Sqlite 日备 cron 已挂上
- [ ] 若对大陆用户正式开服：ICP 等资质已完成
