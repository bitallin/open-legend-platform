# 12 · 商城与支付

完成度：矩阵经济段覆盖金币（**Y-01**）。商城支付是 Phase E 能力，步骤以 `docs/PAYMENT_SANDBOX.md` 为准，本页写规格边界。支付不在矩阵字母表里单独成行；不要为此在文档里新造 Done 状态。

## 概述与玩家体验

玩家在游戏内选择商品下单，得到收银台所需的 `provider_id`、`checkout_url`、`pay_payload`。支付成功后服务端幂等发货：金币或物品进账，通常配合邮件领取。重复回调不会发第二次。

无商户密钥时走本地 `sandbox_sim`，便于开发和验收。配置了微信或支付宝环境变量后，同一套游戏报文指向对应渠道。

## 协议 / MsgId

| MsgId | 消息 | 说明 |
|-------|------|------|
| 3801 / 3802 | `PaymentCreateOrderReq` / `PaymentCreateOrderResp` | 创建订单。回包含渠道字段 |
| 3803 / 3804 | `PaymentOrderStatusReq` / `PaymentOrderStatusResp` | 查询 |
| 3805 | `PaymentFulfilledNotify` | 发货完成通知 |

拒绝码：`ERR_PAYMENT_DENIED (1081)`、`ERR_PAYMENT_INVALID (1082)`、`ERR_PAYMENT_DUPLICATE (1083)`。

商品 id 现为代码内目录（`IPaymentProvider` 一侧的产品表，见 `server/Legend.Contracts/IPaymentProvider.cs` 的查找逻辑）：

| productId | 含义 | 标价（分） |
|-----------|------|------------|
| `gold_100` | 100 金币档 | 100 |
| `gold_1000` | 1000 金币档 | 900 |
| `item_hp_pack` | 药品包 | 50 |

金额与 `PriceCents` 不一致的回调必须拒绝。客户端不能上传价格。

## 服务端模块与配置

- 玩法：`server/Legend.Game/GameWorldModule.Payment.cs`
- 渠道：`server/Legend.Contracts/Payment/WechatPaymentProvider.cs`、`AlipayPaymentProvider.cs`、`PaymentProviderFactory.cs`
- 存储：`server/Legend.Storage/PaymentStores.cs`
- HTTP：`server/Legend.Host/AdminHttpServer.cs`
- 本地样例：`tools/payment-sandbox-sim`
- 占位证书说明：`certs/payment/README.md`。真实密钥不要进 git

| 入口 | 作用 |
|------|------|
| `POST /admin/payment/webhook` | Mock，头 `X-Payment-Signature` 为 HMAC-SHA256，canonical 字符串 `orderId\|txn\|paid` |
| `POST /admin/payment/wechat/notify` | 微信通知 |
| `POST /admin/payment/alipay/notify` | 支付宝通知 |
| `POST /admin/payment/sandbox-sim/build-notify` | 生成模拟签名体 |
| `POST /admin/recharge` | 管理直充 |

环境变量：`LEGEND_PAYMENT_PROVIDER`、`LEGEND_PAYMENT_SECRET`、`WECHAT_*`、`ALIPAY_*`。命令行 `--payment-provider`。

发货幂等：同一订单已履约则返回重复，不叠加金币。订单不参与合区合并，新区重新下单（`MERGE_RULES.md`）。

## 客户端表现

商城面板调用 3801，展示回包里的 URL 或 payload。Playable 若只做了协议调用而没有完整货架皮肤，以支付验收脚本为准：`scripts/verify-phase-e-payment.sh`。不要在客户端写死密钥或签名算法。

Unity 同样只发 3801/3803。收银台可以是系统浏览器打开 `checkout_url`。

## 登录器相关

登录器当前不包含支付（`docs/LAUNCHER.md` 已说明）。若以后做网页收银台，登录器只打开 URL，不接触支付密钥。

## 配置热更与脚本钩子

没有 `on_payment` 钩子。发货结果用邮件（`on_mail_send` / `on_mail_claim`）和金币变更。商品价格改动要发版或改代码中的产品表；在价格进版本包之前，**不要**用 `reload-pack` 假设价格已变。

扩展时把产品表挪到 `configs/mall.json` 并由 manifest 引用，reload 后只影响新订单，旧订单锁定创建时的 `PriceCents`。

## 验收标准

脚本与文档：

- `scripts/verify-phase-e-payment.sh`
- `docs/PAYMENT_SANDBOX.md`
- 回归 `scripts/verify-phase-cd.sh` 仍通过

行为：

1. `PaymentCreateOrder` 三种 `productId` 都能得到渠道字段。
2. Mock webhook 签名正确才发货。
3. 微信/支付宝模拟 notify 可被验签。
4. `POST /admin/recharge` 直充。
5. 金额不符拒绝；重复通知不二次发货。
6. 玩家通过邮件领取附件（邮件报文见 [13](13-chat-social.md)）。

矩阵 **Y-01** 仍是金币余额的玩法规格。商城把外部支付换成同一余额。

## 非目标

- 不把第三方商城表或货币编号当成协议。
- 不在仓库存放生产私钥。`*.example.pem` 只是形状示例。
- 客户端不计算签名。

## 扩展路线

未来工作，未进入矩阵。

1. **商城配置**。`configs/mall.json`：`productId`、显示名、`priceCents`、发货内容（金币数量或物品列表）、限购。与 3801 的 `productId` 对齐。
2. **多货币**。除 `gold` 外增加版本包声明的 `currencyId`（例如绑定金币、荣誉）。每种货币独立余额。支付商品明确写入哪一种。不使用 1–99 的匿名钱位。
3. **退款**。渠道退款回调把已发金币扣回；余额不足则记欠账并限制交易。MsgId **TBD**。在规则写清前不自动扣物品。
4. **网页货架**。与登录器分离的静态页，下单仍创建同一订单号。游戏内 3805 通知到账。
