# 公网部署与大陆机房预算（精简）

> 完整交互版见 Cursor Page《传奇平台公网部署与大陆机房预算》。数字为 **2026-10-29** 公开价估算，下单以官网计算器为准。大陆机房需 **ICP 备案**。

## 建议起步

**试玩档**：华东/华北一台 2C4G 轻量（Login+单区+本机 Admin，Sqlite），月费约 **50–120 元**。

## 三档月费区间（人民币）

| 档位 | 在线 | 拓扑 | 月费 |
|------|------|------|------|
| 试玩 | ≤50 | 单机轻量 · Sqlite | 50–120 |
| 小开 | 100–300 · 1–2 区 | 双机 · PG · 对象存储备份 | 400–900 |
| 正式 | 500+ · 多区 | 每区独立机 · LB · RDS HA · Redis | 2,000–6,000+ |

## 清单要点（均未做）

- 域名 + ICP；安全组只暴露 Gate；Admin/DB/Webhook 禁公网
- TLS 仅管理 HTTP；游戏 TCP 可先明文
- systemd/docker-compose；日备 → OSS/COS/OBS；合区停写+dry-run
- 观测在线/验签/订单；支付签名+白名单；客户端指向公网 host

## 价目线索（抓取日）

- 腾讯轻量：https://cloud.tencent.com/document/product/1207/73452 （2C4G≈65 元/月；超额≈0.8 元/GB）
- 阿里轻量计费：https://help.aliyun.com/zh/simple-application-server/product-overview/billing/
- 阿里 ECS 带宽整理：https://developer.aliyun.com/article/1145616 （华东 5M≈125；≥6M≈80/Mbps）
- 阿里 RDS/OSS：https://developer.aliyun.com/article/1714846 、https://www.aliyun.com/price/detail/oss
- 华为 Flexus / OBS：https://www.huaweicloud.com/product/flexus/pricing.html 、https://www.huaweicloud.com/product/obs/price.html
