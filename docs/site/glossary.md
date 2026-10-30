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
| `currencyId` | 版本包声明的货币稳定 id，如 `gold`、`ingot`。扩展账本见 11。样例只启用 `gold` |
| 绑定货币 | `currencyId` 以 `_bound` 结尾的余额。同组扣款时优先消耗，`tradable` 通常为 false |
| 生肖容器 | `containerId` 为 `zodiac` 的 12 格容器，槽位 `zodiac_01` 至 `zodiac_12`。矩阵外，见 07 |
| 神器槽 | `extraSlots` 中 `kind=artifact` 的装备位。等级不复用 `enhanceLevel`，见 07 与 16 |
| 天赋点 | 天赋树节点的消耗。与内功经验不是同一池。配置 `configs/talents.json`，见 03 |
| 内功 | 第二经验池 `internalLevel`，可选经络 `meridians`。不新开职业 id，见 03 |
| 合击 | 主角与已出战英雄同时就绪时，服务端结算的一次伤害。见 05 与 15。英雄未做前客户端不显示合击槽 |
| 坐骑状态 | 骑乘中允许走跑、吃药、点 NPC；默认禁止攻击、拾取、挖矿。见 15 |
| 宠物模式 | `follow`、`attack`、`passive`、`rest`。与道士技能召唤兽的 AI 分开，见 15 |
| 地图 flag | 地图布尔开关：`noPk`、`noMount`、`noSummon`、`noTeleport`、`noTrade`、`instance`、`noHang`、`noAutoPickup`。见 04 |
| 内挂 | 客户端输入辅助状态机：寻怪、走近、技能旋转、拾取、吃药、回城。每步发送现有移动、攻击、拾取、使用物品报文。默认关闭。见 19。未进矩阵 |
| `client_assist` | 内挂默认配置文件 `configs/client_assist.json`。玩家覆盖可在本机；跟账号同步的字段 TBD |
| 拾取过滤 | `configs/pickup_filters.json` 决定辅助拾取捡哪些地面物品。客户端只能再收紧。见 06 与 19 |
| 红毒 / 绿毒 | 施毒术的两种 Debuff。红为 `debuff_poison_red`（持续掉血），绿为 `debuff_poison_green`（降低防御）。扩展见 05 与 14。样例可仍用单一 `debuff_poison` |
| `noHang` | 地图 flag。为 true 时禁止维持内挂状态机。见 04 与 19 |
| 离线挂机 | 玩家角色在断线后由服务端按显式 `hangMode=offline` 继续结算。默认关闭。见 21 与 22。未进矩阵，不得标 Done |

错误码段在 `protocol/legend_protocol.proto` 的 `ErrorCode`：业务拒绝从 `1001` 起，`ERR_INTERNAL = 1999`。查具体码时打开该枚举，不要在文档里复制一份会过期的全表。常用码在各功能页点名。
