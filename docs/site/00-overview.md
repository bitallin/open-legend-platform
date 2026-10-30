# 00 · 总览与架构

完成度以 [功能矩阵](../MIR_PRODUCTION_FEATURE_MATRIX.md) 为准。本页说明仓库怎么分层，以及后文 01–22 分别约束哪一端。

## 概述与玩家体验

玩家从登录器选区，启动 Avalonia 可玩客户端（或 Unity MVP），用账号进入自研区服。世界是格子地图：移动、战斗、拾取、对话、装备、任务、行会、攻城、交易和聊天都由服务端裁决，客户端负责呈现和采集输入。

产品目标是可运营的原创三端（PC 可玩端、Unity PC/Android、登录器）多版本平台。版本之间的差异放在版本包，而不是分叉协议。

## 协议 / MsgId

契约目录：`protocol/legend_protocol.proto`，版本文件 `protocol/VERSION`（当前 **1.21.0**）。运行时常量：`server/Legend.Contracts/ProtocolConstants.cs`。

握手 `HandshakeReq/Resp` 为 **1001 / 1002**。主版本不一致返回 `ERR_VERSION_MISMATCH (1001)`。`MinCompatibleVersion` 与当前字符串同为 `1.21.0`。

帧由 `PacketFrame.Encode` 写出：大端长度、MsgId、序号、Protobuf payload。网关在 `server/Legend.Gate/TcpGateServer.cs`。

号段（细节在各篇，这里只作索引）：

| 号段 | 域 |
|------|----|
| 1001–1010 | 握手、登录、区服列表、Ping、注册 |
| 1101–1108 | 角色列表、创建、删除、选择 |
| 2001–2007 | 进世界、移动、AOI、切图 |
| 2101–2109 | 攻击、战斗同步、掉落、拾取、背包通知 |
| 2201–2209 | 对话、购买、任务 |
| 2301–2306 | 穿脱、强化 |
| 2401–2404 | 使用物品、经验、升级 |
| 2501–2510 | 行会创建加入离开、聊天 |
| 2601–2609 | 寄售 |
| 2701–2716 | 组队、好友 |
| 2801–2808 | 技能、Buff |
| 2901–2909 | 攻城 |
| 3001 | `ServerNotice` |
| 3101–3111 | 邮件 |
| 3201–3209 | 副本 |
| 3301–3307 | 世界 Boss 与稀有掉落广播 |
| 3401–3413 | 坐骑、时装、外观同步 |
| 3501–3511 | 宠物 |
| 3601–3608、3701–3704 | 行会仓库、职位、审计 |
| 3801–3805 | 支付 |
| 3901–3972 | 出售、修理、个人仓库、攻击模式、鉴定、交易、世界地图、挖矿、地面特效、离开世界 |

`protocol/README.md` 仍写着 1.1.0 和很短的 MsgId 表，那是早期说明。以本节和 `PROTOCOL_NOTES.md` 为准。

## 服务端模块与配置

| 路径 | 职责 |
|------|------|
| `server/Legend.Host` | 进程入口、`LegendServerEngine`、Admin HTTP |
| `server/Legend.Login` | 注册、登录、区服列表 |
| `server/Legend.Gate` | TCP 帧 |
| `server/Legend.Game` | 世界、战斗、经济、社交 |
| `server/Legend.Core` | `VersionPackLoader`、事件总线 |
| `server/Legend.Script` | MoonSharp 沙箱 |
| `server/Legend.Storage` | InMemory / Sqlite / Postgres，Redis 会话 |
| `server/Legend.Contracts` | MsgId、规则纯函数、支付接口 |
| `version-packs/sample-classic` | 主样例规则 |
| `realms/` | 区服 catalog |
| `ops/` | Docker Compose 与 Host 镜像 |
| `tools/Legend.Merge` | 合区 |

Host 启动示例见仓库 `README.md`：`--manifest`、`--storage`、`--realm-id`、`--admin-token`。

## 客户端表现

两条路径，协议相同：

- **验收路径**：`client/Legend.Playable`（Avalonia）。输入、面板、世界画布、程序音效都在这里。说明见 `docs/PLAYABLE_PC.md` 与 [19](19-client-ui-controls.md)。
- **Unity**：`client/unity-sdk` 网络与演示行为，`client/unity-mvp` 场景、HUD、Android 导出。见 `docs/UNITY_CLIENT_MVP.md`、`docs/ANDROID_BUILD.md`。

视觉方向是经典传奇布局加少量打磨（斜 45° 菱形砖、金边底栏、分层纸娃娃）。细节在 `docs/VISUAL_PARITY.md` 与 [20](20-audio-visual.md)。

## 登录器相关

`launcher/` 在游戏协议之外工作：读区服 JSON、TCP 探测延迟、对照更新清单、按模板参数启动客户端。规格见 [01](01-account-login-launcher.md)，操作见 `docs/LAUNCHER.md`。

## 配置热更与脚本钩子

`POST /admin/reload-pack` 重新加载 manifest 指向的包。错误路径回滚。脚本模型见 [18](18-script-config-hotreload.md)。

概念对照（第三方文本目录 → 本仓库文件，只表达数据种类）：

| 概念 | OLP |
|------|-----|
| 启动与版本 | `manifest.json`、Host 命令行 |
| 文案与公告 | `ServerNotice`、客户端字符串、Lua `send_welcome` |
| 经验 | `configs/levels.json` |
| 地图与刷怪 | `maps/*.json`、`configs/monsters.json` 的 `spawns` |
| 技能、物品、商店、任务 | `configs/*.json` |
| 脚本 | `scripts/*.lua`，白名单 `legend_api` |

## 验收标准

矩阵是全表。架构改动至少保持这些脚本可跑：`scripts/verify-playable-client.sh`、`scripts/verify-m19.sh`、`scripts/verify-phase-h-launcher.sh`。进度汇总看 `docs/DEV_PROGRESS.md`（矩阵计分，不在此重算）。

## 非目标

- 不提供 996 / Crystal / GOM 协议兼容层、登录网关兼容或资源转换器。
- 不把 `protocol/README.md` 的过时 MsgId 表当成契约。
- 不在本页记录 Done/Partial。那是矩阵的工作。

## 扩展路线

下列能力在调研中常见，**尚未**成为矩阵叶子。实现前先写进行为规格（本站对应篇），再加协议。状态都是未来工作。

| 能力 | 写在 |
|------|------|
| 静默下载、资源热更、实名、禁止登录名单、渠道包 | [01](01-account-login-launcher.md) |
| 英雄、多职业、阵营、国家 | [02](02-character-class.md)、[15](15-pet-hero-mount.md) |
| 内功、经络、大数值、自定义属性 | [03](03-attributes-growth.md) |
| 镜像副本、地图事件 | [04](04-map-movement.md) |
| 连击 | [05](05-combat-skills.md) |
| 掉落绑定与数量上限、怪物说话与顶戴、排行榜用的击杀统计 | [06](06-monsters-loot.md)、[21](21-ops-gm-deploy.md) |
| 首饰盒、生肖、自定义装备位、盾牌 | [07](07-equipment-inventory.md) |
| 押镖、答题、更丰富的任务类型 | [08](08-npc-quests.md) |
| 守卫、日常行会战细则 | [09](09-guild-siege.md) |
| 红名村 | [10](10-pk-redname.md) |
| 拍卖行、求购、回收规则扩展 | [11](11-trade-stall-auction.md) |
| 商城多货币 | [12](12-mall-payment.md) |
| 结婚、语音 | [13](13-chat-social.md) |
| 高位 Buff、红点、盾牌与斗笠外观 | [14](14-buff-title-fashion.md) |
| 英雄全套、宠物等级、假人 | [15](15-pet-hero-mount.md) |
| 打孔、合成、刀魂、投保 | [16](16-craft-enhance-gem.md) |
| 跨服、通区、跨服攻城、合区变量 | [17](17-cross-realm-merge.md) |
| 定时脚本、GUI 元变量（只作 UI 能力清单） | [18](18-script-config-hotreload.md)、[19](19-client-ui-controls.md) |
| H5 界面 | [19](19-client-ui-controls.md) |
| 自研特效管线配置 | [20](20-audio-visual.md) |
| 数据上报、GM 命令表、排行榜 | [21](21-ops-gm-deploy.md) |
| 防刷策略与客户端完整性 | [22](22-security-anti-cheat.md) |
| 多货币账本、回收日限 | [11](11-trade-stall-auction.md)、[12](12-mall-payment.md) |
| 天赋树 | [03](03-attributes-growth.md) |
| 十二生肖、神器槽 | [07](07-equipment-inventory.md)、[16](16-craft-enhance-gem.md) |
| 翅膀与限时时装 | [14](14-buff-title-fashion.md) |
| 英雄合击完整草案、坐骑状态机、宠物蛋 | [15](15-pet-hero-mount.md)、[05](05-combat-skills.md) |
| 地图 flags、岛屿对照 | [04](04-map-movement.md) |
| 客户端内挂、三职业自动、自动施毒、保护吃药 | [19](19-client-ui-controls.md) |
| 拾取过滤、范围连拾 | [06](06-monsters-loot.md)、[19](19-client-ui-controls.md) |
| 红绿毒 Debuff | [05](05-combat-skills.md)、[14](14-buff-title-fashion.md) |
| 地图禁挂 | [04](04-map-movement.md) |
| 离线挂机运营 | [21](21-ops-gm-deploy.md)、[22](22-security-anti-cheat.md) |

表末各行是扩展规格入口，仍未进入矩阵，不计分。字段形状以各篇「扩展路线」为准。
