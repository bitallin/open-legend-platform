# Open Legend Platform 文档站

本站是引擎、服务端、客户端、登录器的**功能规格**。一篇文档覆盖一个大功能域（00–22）。

完成度只看 [功能矩阵](../MIR_PRODUCTION_FEATURE_MATRIX.md)。进度数字看 [DEV_PROGRESS.md](../DEV_PROGRESS.md)。协议增量看 [PROTOCOL_NOTES.md](../PROTOCOL_NOTES.md)。本站不维护第二套 Done / Partial 表。

协议、帧格式、版本包与 `legend_api.*` 沙箱都是本仓库原创。能力对标经典传奇与现代私服常见玩法域。站点不记录、也不实现任何第三方闭源引擎的协议兼容。

## 怎么构建与浏览

需要 .NET SDK 8 与 DocFX 2.x。

```bash
dotnet tool install -g docfx --version 2.78.3
export PATH="$PATH:$HOME/.dotnet/tools"
docfx build docs/site/docfx.json
```

或：

```bash
./scripts/build-docs-site.sh
```

用浏览器打开 `docs/site/_site/index.html`。构建只生成静态页，不编译 `server/` 或客户端。C# API 交叉引用留到后续阶段，避免文档 CI 依赖完整游戏构建。

仓库里直接读 Markdown 也可以：从本页的目录进入各篇。CI 工作流是 `.github/workflows/docs-site.yml`，只在文档路径变更时跑 `docfx build`。

## 目录

| 篇 | 文档 | 开发时先看 |
|----|------|------------|
| 00 | [总览与架构](00-overview.md) | 分层、帧、版本包、四端职责 |
| 01 | [账号、登录与登录器](01-account-login-launcher.md) | 会话、选区、更新通道 |
| 02 | [角色与职业](02-character-class.md) | 三职业 id、创角、回选角 |
| 03 | [属性与成长](03-attributes-growth.md) | 等级曲线、死亡惩罚 |
| 04 | [地图与移动](04-map-movement.md) | 格子、传送、安全区 |
| 05 | [战斗与技能](05-combat-skills.md) | 技能 id、伤害与冷却 |
| 06 | [怪物与掉落](06-monsters-loot.md) | AI、刷怪、掉落表 |
| 07 | [装备、背包与仓库](07-equipment-inventory.md) | 槽位、负重、仓库 |
| 08 | [NPC 与任务](08-npc-quests.md) | 对话、商店、任务链 |
| 09 | [行会与攻沙](09-guild-siege.md) | 行会仓、攻城状态机 |
| 10 | [PK 与红名](10-pk-redname.md) | 攻击模式、名字颜色 |
| 11 | [交易、摆摊与拍卖](11-trade-stall-auction.md) | 面对面交易、寄售 |
| 12 | [商城与支付](12-mall-payment.md) | 沙箱支付、幂等发货 |
| 13 | [聊天与社交](13-chat-social.md) | 频道、组队、好友、邮件 |
| 14 | [Buff、称号与时装](14-buff-title-fashion.md) | Buff 表、称号、披风 |
| 15 | [宠物、英雄与坐骑](15-pet-hero-mount.md) | 跟随助战、召唤、坐骑 |
| 16 | [合成、强化与镶嵌](16-craft-enhance-gem.md) | 强化、套装 |
| 17 | [跨服、合区与多区](17-cross-realm-merge.md) | catalog、合区工具 |
| 18 | [脚本、配置与热更](18-script-config-hotreload.md) | `legend_api`、reload-pack |
| 19 | [客户端 UI 与操作](19-client-ui-controls.md) | Playable 与 Unity、键位 |
| 20 | [音效与画面](20-audio-visual.md) | 图集政策、视听条目 |
| 21 | [运维、部署与 GM](21-ops-gm-deploy.md) | Docker、Admin |
| 22 | [安全与反外挂](22-security-anti-cheat.md) | 会话、验签、速率 |

附录：[编写约定](conventions.md)、[术语表](glossary.md)、[经典名称对照](classic-name-map.md)、[调研来源](research/SOURCES.md)。

## 和现有短文的关系

| 现有文件 | 在本站的角色 |
|----------|----------------|
| `docs/MIR_PRODUCTION_FEATURE_MATRIX.md` | 完成度事实来源。各篇「验收标准」只列矩阵 ID |
| `docs/DEV_PROGRESS.md` | 分数与阶段日志 |
| `docs/PROTOCOL_NOTES.md` | 已落地报文与仍待升版本的缺口 |
| `docs/PLAYABLE_PC.md` | 玩家操作速查，规格在 19 |
| `docs/LAUNCHER.md` | 登录器运行说明，规格在 01 |
| `docs/VISUAL_PARITY.md` | 画面完成记录，规格在 20 |
| `docs/PAYMENT_SANDBOX.md` | 商户沙箱步骤，规格在 12 |
| `docs/UNITY_*`、`docs/ANDROID_BUILD.md` | 客户端构建附录 |

## 读文档的顺序

新同学先读 00、18、19，再按自己负责的端进入对应篇。改协议前先读 `PROTOCOL_NOTES.md`：当前对外版本字符串是 **1.21.0**，`protocol/README.md` 里残留的 1.1.0 说明已过时，以 `protocol/VERSION` 与 `ProtocolConstants.CurrentVersion` 为准。
