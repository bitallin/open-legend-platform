# 术语表

本表只定义本仓库用词。第三方引擎里的同名概念若需对照，写在各篇扩展路线，不作为 API。

| 术语 | 含义 |
|------|------|
| OLP | Open Legend Platform，本仓库 |
| 版本包 | `version-packs/<id>/`，含 `manifest.json`、地图 JSON、配置 JSON、Lua |
| 区服 / Realm | 一份独立世界进程与存档。配置在 `realms/*.json`，目录在 `realms/catalog.json` |
| 帧 | `[Len:4][MsgId:2][Seq:4][Payload]`，大端 |
| MsgId | `Legend.Contracts.MsgId`，ushort。请求通常奇数，应答为请求号 +1 |
| Playable | `client/Legend.Playable`，Avalonia 可玩 PC 客户端，当前验收路径 |
| Unity MVP | `client/unity-mvp` 加 `client/unity-sdk`，第二条客户端路径 |
| 登录器 | `launcher/`，选区、延迟探测、更新检查、拉起客户端 |
| legend_api | Lua 沙箱唯一注入对象，方法见 `LegendLuaBridge` |
| 矩阵 ID | `docs/MIR_PRODUCTION_FEATURE_MATRIX.md` 里的 `A-01` 这类叶子编号 |
| 攻击模式 | `peace` / `good_evil` / `group` / `guild` / `all`，见 `PhaseMRules.NormalizeMode` |
| 安全区 | 地图 `safeZones` 矩形。区内攻击返回 `ERR_SAFE_ZONE (1015)` |
| AOI | `GridAoiManager`，按地图 `aoiCellSize` 与 `aoiRadiusCells` 同步进出视野 |
| 纸娃娃 | 客户端按装备 `paperdoll` 分层绘制。服务端不解释贴图 |
| 寄售 | 协议里的 Stall 报文（2601–2609），配置 `configs/trade.json`，要求安全区 |
| 面对面交易 | Trade 报文 3941–3953 |
| 攻城 | Siege 报文 2901–2909 与 `configs/siege.json`。日常行会战是另一套，见 09 |
| 热重载 | `POST /admin/reload-pack`，失败回滚已加载包 |
| 沙箱支付 | `mock` / `wechat` / `alipay`，无密钥时 `sandbox_sim` |
| TBD | 协议或配置尚未落地。文档可以写设计意图，不能发明 MsgId |

错误码段在 `protocol/legend_protocol.proto` 的 `ErrorCode`：业务拒绝从 `1001` 起，`ERR_INTERNAL = 1999`。查具体码时打开该枚举，不要在文档里复制一份会过期的全表。常用码在各功能页点名。
