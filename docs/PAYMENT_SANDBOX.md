# 支付沙箱对接（Phase E）

本仓库支持三种支付 Provider，通过 `--payment-provider` / 环境变量切换。**默认仍为 `mock`**，本地与 CI 不依赖真实商户号。

| ProviderId | 说明 | 回调路径 |
|---|---|---|
| `mock` | HMAC-SHA256（Phase C/D） | `POST /admin/payment/webhook` |
| `wechat` | 微信商户 APIv3 风格；无密钥走 `LocalWechatSandboxSimulator` | `POST /admin/payment/wechat/notify` |
| `alipay` | 支付宝开放平台 RSA2；无密钥走 `LocalAlipaySandboxSimulator` | `POST /admin/payment/alipay/notify` |

协议 **1.21.0**：`PaymentCreateOrderResp` 增加 `provider_id` / `checkout_url` / `pay_payload` / `provider_order_id`。

> 无真实商户密钥时，Host 会在日志打印 `mode=sandbox_sim`，**不会**对官方网关发起“假装成功”的假 HTTP。

---

## 1. 本地 / CI（推荐）

无需商户号。Host 启动时即使 `--payment-provider mock`，也会同时挂载 wechat/alipay 的 **sandbox_sim** 实例，供 notify 路径与 `build-notify` 使用。

```bash
# 全量 Phase E 验收（含微信/支付宝模拟回调 + mock 回归）
./scripts/verify-phase-e-payment.sh

# 仅协议级模拟器 CLI（生成 notify 样例，不启服）
dotnet run --project tools/payment-sandbox-sim -- wechat --order ORD1 --amount 100
dotnet run --project tools/payment-sandbox-sim -- alipay --order ORD2 --amount 100
```

Admin 辅助（需 Bearer Token；仅 sandbox_sim 可用）：

```bash
curl -s -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"provider":"wechat","orderId":"<uuid>","providerTxnId":"wx-1","amountCents":100}' \
  http://127.0.0.1:18081/admin/payment/sandbox-sim/build-notify
# 再把返回的 body + headers POST 到 /admin/payment/wechat/notify
```

幂等：同一订单重复回调返回成功应答，发货状态为 `already_fulfilled`。

---

## 2. 切换到微信真实沙箱 / 商户

### 申请与材料

1. 微信商户平台申请商户号，开通 Native/JSAPI（沙箱或正式均可，本实现按 APIv3）。
2. 下载商户 API 证书，导出 **商户私钥 PEM**、记下 **证书序列号**。
3. 设置 APIv3 密钥（32 字节），下载/缓存 **微信支付平台证书** PEM（验签用）。
4. 配置公网可达的 notify URL（可用内网穿透，如 frp/ngrok/cpolar）指向：
   `https://<你的域名>/admin/payment/wechat/notify`

### 环境变量（勿入库、勿进 git）

```bash
export WECHAT_MCH_ID=190000****
export WECHAT_APP_ID=wx********
export WECHAT_API_V3_KEY=xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx   # 32 chars
export WECHAT_MCH_SERIAL_NO=XXXXXXXXXXXXXXXX
export WECHAT_MCH_PRIVATE_KEY_PEM="$(cat /secure/wechat_mch_key.pem)"
# 或路径：
# export WECHAT_MCH_PRIVATE_KEY_PATH=/secure/wechat_mch_key.pem
export WECHAT_PLATFORM_CERT_PEM="$(cat /secure/wechat_platform_cert.pem)"
# 可选：强制本地模拟（即使有密钥）
# export WECHAT_FORCE_SIM=1
```

启动：

```bash
dotnet run --project server/Legend.Host -- \
  --payment-provider wechat \
  --admin-token "$LEGEND_ADMIN_TOKEN" \
  ...
```

下单返回 `checkout_url`（Native `code_url`）或 `pay_payload`（调起参数 JSON）。回调验签：`timestamp\nnonce\nbody\n` + 平台证书 RSA-SHA256；`resource` AES-256-GCM 解密后读取 `out_trade_no` / `transaction_id` / `trade_state`。成功应答 JSON：`{"code":"SUCCESS","message":"成功"}`。

---

## 3. 切换到支付宝沙箱

### 申请

1. [支付宝开放平台](https://open.alipay.com/) → 沙箱应用，获取 APPID。
2. 生成 RSA2 密钥对；上传**应用公钥**，保存**应用私钥**与**支付宝公钥**。
3. 沙箱网关默认：`https://openapi.alipaydev.com/gateway.do`
4. 异步通知 URL：`https://<域名>/admin/payment/alipay/notify`

### 环境变量

```bash
export ALIPAY_APP_ID=9021********
export ALIPAY_PRIVATE_KEY_PEM="$(cat /secure/alipay_app_private.pem)"
export ALIPAY_ALIPAY_PUBLIC_KEY_PEM="$(cat /secure/alipay_public.pem)"
export ALIPAY_GATEWAY=https://openapi.alipaydev.com/gateway.do
export ALIPAY_NOTIFY_URL=https://<域名>/admin/payment/alipay/notify
# export ALIPAY_FORCE_SIM=1
```

启动：`--payment-provider alipay`。

下单默认走 `alipay.trade.precreate`（返回二维码链接）或 page.pay form（`pay_payload`）。回调：RSA2 验签；仅 `trade_status=TRADE_SUCCESS|TRADE_FINISHED` 发货；应答纯文本 `success`。

---

## 4. 安全清单

- 密钥/证书只放环境变量或权限 `600` 的文件；仓库仅有 `certs/payment/*.example.pem` 占位。
- Admin API 与 notify **不要裸奔公网**：限制来源 IP、TLS、独立端口；mock webhook 同样勿暴露。
- 日志禁止打印完整 PEM / API v3 key；金额与订单 `PriceCents` 不符一律拒绝发货。
- 合区/迁移前先停支付写路径。

---

## 5. 与 mock 共存

- `--payment-provider mock` + `--payment-secret`：游戏内下单仍用 mock token；微信/支付宝 notify 仍可用 sandbox_sim 联调。
- 真实联调时把 `--payment-provider` 设为 `wechat` 或 `alipay`，并填齐对应 env。
- 商品目录不变：`gold_100` / `gold_1000` / `item_hp_pack`。
