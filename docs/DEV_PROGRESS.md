# 开发进度

生产清单：[MIR_PRODUCTION_FEATURE_MATRIX.md](MIR_PRODUCTION_FEATURE_MATRIX.md)。计分：`Done=1`，`Partial=0.5`，`NotStarted=0`。

| 项 | 值 |
|----|----|
| 最后更新 | 2026-11-01 |
| 阶段 | Phase R + 引擎 E0–E13 已完成（DocOnly 扩展切片）。功能清单 100%。见 [ENGINE_REFACTOR_PLAN.md](ENGINE_REFACTOR_PLAN.md) |
| 协议 | 1.21.0（新增 3971/3972 离开世界。`EntitySnapshot.title`、`CharacterSummary.gender`） |
| 客户端 | Avalonia `client/Legend.Playable` |
| 总体 | **100%**（217 / 217） |
| MATRIX_COMPLETE | **true** |

## 分节

| 节 | 得分 | 条目 | 完成度 |
|----|------|------|--------|
| 1 账号与角色 | 12.0 | 12 | 100% |
| 2 核心战斗与移动 | 15.0 | 15 | 100% |
| 3 战士技能 | 8.0 | 8 | 100% |
| 3 法师技能 | 11.0 | 11 | 100% |
| 3 道士技能 | 10.0 | 10 | 100% |
| 4 怪物与掉落 | 8.0 | 8 | 100% |
| 5 装备 | 14.0 | 14 | 100% |
| 6 背包与仓库 | 6.0 | 6 | 100% |
| 7 NPC | 8.0 | 8 | 100% |
| 8 任务 | 7.0 | 7 | 100% |
| 9 社交 | 10.0 | 10 | 100% |
| 10 PK | 8.0 | 8 | 100% |
| 11 地图 | 11.0 | 11 | 100% |
| 12 攻城 | 6.0 | 6 | 100% |
| 13 经济 | 5.0 | 5 | 100% |
| 14 聊天 | 8.0 | 8 | 100% |
| 15 坐骑宠物 | 5.0 | 5 | 100% |
| 16 画面 | 31.0 | 31 | 100% |
| 17 音效 | 15.0 | 15 | 100% |
| 18 操作 | 19.0 | 19 | 100% |

Done 217，Partial 0，NotStarted 0。清单 100%。MATRIX_COMPLETE=true。


## 画面石金壳对齐（2026-11-01）

- Avalonia 客户端导入原型 kit：`Assets/chrome/`（阴阳球 / 聊天 / BR / 小地图 / 背包人物整窗 / 九宫 / 按钮 / 物品格）。
- 登录、选角、主 HUD、背包网格、人物环绕槽对齐 OL-01-A/B/C · OL-02-A/B · OL-03-C 硬门槛；禁墙纸平铺与浮空灰块。
- 扩展窗：技能 OL-04-A、商店 OL-06-B、仓库 OL-06-E、交易 OL-08-A、行会 OL-09-A、大地图 OL-05-B、设置 OL-12-A、组队/好友/右键 OL-07、邮件 OL-17-BF、内挂中 OL-16-N、任务 OL-11、死亡/安全区/名色 OL-10、攻沙顶栏 OL-09-E、寄售 OL-08-H、商城 OL-17-A、英雄/宝宝 OL-13-A/OL-02-I、内挂 OL-16-K — 九宫 Framed + Tab/列表/追踪条（非整窗 AI 贴图；商城支付不接线；合击走既有 hero_combo）。
- 协议仍 **1.21.0**；服务端无协议变更。矩阵仍 100%。
- 进度对照 [VISUAL_PARITY.md](VISUAL_PARITY.md)。

- 2026-11-01：扩展窗布局合入 #33 → `840e6f3` — OL-04-A 技能列表+F1–F8、OL-06-B 商店 Tab 双栏、OL-06-E 仓库双栏、OL-08-A 交易双格、OL-09-A 行会、OL-05-B 大地图示意、OL-12-A 设置 Tab。仅客户端 UI；服务端无变更（试玩服未重部署）。Windows `dist/windows-player/LegendClient.exe` 已按 `840e6f3` 重打并同步用户机。见 [VISUAL_PARITY.md](VISUAL_PARITY.md)。

- 2026-11-01：密度加深合入 — OL-07-D 组队邀请卡、OL-08-J 寄售无结果空态+筛选、OL-09-D 行会双栏升/降/踢文案、OL-17-B 礼包 2×2 赞助档；权威预览 md5 对齐；不升协议、商城支付仍不接线。仅客户端。
- 2026-11-01：第四刀 chrome 对照权威预览加深 — OL-17-BF 邮件双栏读信/附件/提取、OL-16-K 内挂设置四分区+风险门闩、OL-16-N 挂中小标+克制浮层；Pickup 默认关；禁 OL-14-*。仅客户端。
- 2026-11-01：接线更正合入 — 邮件权威 **OL-17-BF**、内挂 **OL-16-K**（禁 OL-14-*）；壳按冻结预览重对齐后重打 LegendClient。

- 2026-11-01：第三刀扩展窗合入 — OL-07 组队/好友/玩家菜单、OL-11 任务窗+追踪、OL-10 死亡/安全区条/名色、OL-09-E 攻沙顶栏、OL-08-H 寄售 Tab、OL-17-A 商城占位、OL-13-A/OL-02-I 英雄与宝宝。仅客户端 UI；协议仍 1.21.0；试玩服未重部署。Windows `LegendClient.exe` 重打并同步用户机。见 [VISUAL_PARITY.md](VISUAL_PARITY.md)。
- 2026-11-01：第四刀扩展窗合入 — 邮件 OL-17-BF（收件/发件主窗；入口 OL-07-H）、内挂 OL-16-K（总览开关+四 Tab·默认关·风险确认）、加深 OL-07/08/09/17 列头与状态色、英雄合击 Ctrl+S→`hero_combo`（无新 MsgId）、规避抽检叠字/调试残片。仅客户端 UI；协议仍 1.21.0；试玩服未重部署。Windows `LegendClient.exe` 重打并同步用户机。见 [VISUAL_PARITY.md](VISUAL_PARITY.md)。


- 2026-11-01：Avalonia 石金壳合入 #32 → `686e207`。试玩服 `150.158.164.90:7100` 无服务端增量（仍跑既有 tip）；CliClient M3 冒烟通过。Windows `dist/windows-player/LegendClient.exe` 已按 `686e207` 重打并同步用户机。协议仍 1.21.0。


画面对照另见 [VISUAL_PARITY.md](VISUAL_PARITY.md)。Phase R 起是斜 45° 菱形地图、装备分层和贴图主界面。VISUAL_PARITY_COMPLETE=true。

## 下一批

清单叶子条目已经全部是 Done（矩阵不计 DocOnly）。引擎 **E0–E13** 已合入（顺序与 [ENGINE_REFACTOR_PLAN.md](ENGINE_REFACTOR_PLAN.md)「E8 之后」一致：E9 多货币 → E10 天赋/内功 → E11 生肖/神器 → E12 英雄合击 → E13 内挂）。

后续可选（仍 DocOnly，一项一 PR）：离线挂机、拍卖行、翅膀/限时时装、坐骑状态机、宠物蛋、跨服等；原版向 Avalonia 观感改版等用户确认 todo 212。

## 日志

- 2026-10-30：清单建立时总体 31.6%（68.5/217）。
- 2026-10-30：Phase L 总体 64.7%。Phase M 总体 71.7%。Phase N 总体 82.3%，NotStarted 清零。
- 2026-10-30：Phase O 直线技能、烈火地面、抗拒击退、施毒跳伤、隐身脱仇恨、集体隐身、城墙缺口、日程停战发金币、原创图集、背包拖拽。总体 96.8%（210/217）。Done 203，Partial 14，NotStarted 0。未满 100%。
- 2026-10-30：Phase P 离开世界回选角、世界 BOSS 血条与仇恨、行会任命、好友列表、师徒经验、摆摊窗口、脏词表、坐骑时装宠物面板、登录城市场景、选角立绘、称号。总体 100%（217/217）。Done 217，Partial 0，NotStarted 0。MATRIX_COMPLETE=true。
- 2026-10-30：Phase Q 用原创图集换掉占位渲染。八方向走跑攻施法受击死亡、地砖、怪物、HUD 金边、技能光效和飘字。功能清单仍是 100%。细节见 VISUAL_PARITY.md。
- 2026-10-30：Phase R 斜 45° 菱形镜头（点地仍回格子坐标）、武器衣服头盔分层、动作加帧、原创主界面贴图。VISUAL_PARITY_COMPLETE=true。功能清单仍是 100%。
- 2026-10-31：文档站扩展规格加厚（货币/生肖/神器/天赋/时装翅/坐骑/宠物/英雄/地图 flags）。矩阵仍 100%（扩展不计分）。详见 docs/site 各篇扩展路线与 research/SOURCES.md。
- 2026-10-31：补占位：地图 `noHang` / `noAutoPickup`，红毒绿毒拆分，内挂、拾取过滤与离线挂机仅交叉链（主规格留 19 与 06）。矩阵仍 100%。
- 2026-10-31：文档站 Round2（内挂/拾取过滤/三职业自动与施毒；B 域二扫增量）。矩阵仍 100%（H-*/L-* 仅 DocOnly 草案）。详见各篇扩展路线。
- 2026-10-30：引擎重构 E0。计划与 E0–E8 里程碑见 [ENGINE_REFACTOR_PLAN.md](ENGINE_REFACTOR_PLAN.md)，端口与登录到战斗的数据流见 [ENGINE_LAYER_CONTRACTS.md](ENGINE_LAYER_CONTRACTS.md)。地图与物品在 Host 启动时按 Schema 失败即停。矩阵仍 100%。多货币、生肖、英雄合击、内挂、天赋留在 E8 之后。
- 2026-10-30：引擎重构 E1。技能、怪物、掉落、NPC、商店、任务、等级、行会、交易走同一 Schema 校验器。掉落物品 id 等交叉引用在加载期失败。`Legend.MapValidate` 校验单张地图时调用该校验器。矩阵仍 100%。协议仍是 1.21.0。
- 2026-10-30：引擎重构 E2。`ILegendStorage` 与记录类型编入 `Legend.Contracts`，命名空间仍是 `Legend.Storage`。Game 与 Login 不再引用存储实现项目。没有 SQL 变更。矩阵仍 100%。
- 2026-10-30：引擎重构 E3。进世界落点、阻挡、城门、步长、骑乘移速和传送门收进 `IMovementRules`。移动拒绝仍是 `ERR_MOVE_BLOCKED (1005)` 与 `ERR_MOVE_TOO_FAST (1073)`。AOI 行为未改。矩阵仍 100%。
- 2026-10-31：引擎重构 E4。攻击 / 技能 / Buff 结算门闩收进 `ICombatResolver`；`IAntiCheatProbe.ValidateAttackInterval` 默认与原冷却一致且可替换。安全区仍是 `ERR_SAFE_ZONE (1015)`。伤害公式未改。矩阵仍 100%。协议仍是 1.21.0。
- 2026-10-31：引擎重构 E5。`IMovePrediction` 在 ClientCore；Playable 乐观移格子，`MoveSync.Accepted=false` 回服务器坐标。不改 Avalonia 布局与 Unity。矩阵仍 100%。协议仍是 1.21.0。
- 2026-10-31：引擎重构 E6。`ProtocolCatalogTests` 锁定 MsgId↔proto 消息、`ProtocolErrorCode` 不重复不回收；协议仍 1.21.0。矩阵仍 100%。
- 2026-10-31：引擎重构 E7。进图/击杀/拾取/对话钩子异常隔离；`ScriptSandboxTests` 锁沙箱无 IO 与坏热更回滚后仍可移动。矩阵仍 100%。协议仍是 1.21.0。
- 2026-10-31：引擎重构 E8。地图 Schema 增加可选 `mapFlags`；服务端执行 `noPk`（安全区外禁玩家互攻，`ERR_INVALID_TARGET`）。样例地图行为不变。其它 flag 仅 Schema。矩阵仍 100%。协议仍是 1.21.0。
- 2026-10-31：收尾。`Stall_OfflineSeller` / M3 商店路径误选 `npc_waygate`（无 shop）改为显式 `npc_village_merchant`（#21 → `c23e8a0`）。试玩服 `150.158.164.90:7100` 由 `3b97446` 升至 `c23e8a0`，CliClient M3 冒烟通过。Windows `dist/windows-player/LegendClient.exe` 基于 `c23e8a0` 重打。矩阵仍 100%。协议仍是 1.21.0。

- 2026-10-31：引擎扩展 E9（#23 → `012a4ac`）。`currencies.json` / 回收日限；运行时钱包；样例仍只启用 `gold`。矩阵仍 100%。协议仍是 1.21.0。
- 2026-10-31：引擎扩展 E10（#24 → `c50db2f`）。天赋 / 内功 Schema 与存档字段；不做法术。矩阵仍 100%。协议仍是 1.21.0。
- 2026-10-31：引擎扩展 E11（#25 → `579b7f2`）。生肖容器与神器槽；装备位扩展与属性汇总。矩阵仍 100%。协议仍是 1.21.0。
- 2026-10-31：引擎扩展 E12（#26 → `bd40255`）。英雄存档与合击；`AttackReq` skillId=`hero_combo`；`CombatSync.combo`；无新 MsgId。矩阵仍 100%。协议仍是 1.21.0。
- 2026-10-31：引擎扩展 E13（#27 → `4849741`）。`client_assist.json` / `pickup_filters.json`；ClientCore 内挂状态机默认关；`map_no_hang` / `map_no_auto_pickup` 进世界同步。矩阵仍 100%。协议仍是 1.21.0。
- 2026-10-31：试玩服 `150.158.164.90:7100` 升至 tip `3462ee6`（含 E9–E13）；CliClient M3 冒烟通过。Windows `dist/windows-player/LegendClient.exe` 对齐同 SHA。矩阵仍 100%。协议仍是 1.21.0。

## E9–E13 审查加固（本机自审）

对照 `ENGINE_REFACTOR_PLAN` / docs/site 自审后合入的正确性修复（协议仍 **1.21.0**，矩阵计分不变）：

| 项 | 修复 |
|----|------|
| E9 | `AdjustGold` / `CreditCurrency` 先镜像再入账，避免扣金未同步钱包后 Credit 刷高 Gold |
| E10 | `RecalculateStats(pack)` 汇总天赋节点、内功等级与经脉 flat 加成（atk/def/maxHp/maxMp） |
| E12 | 合击前不再瞬移英雄；`SyncHeroFollow` 在移动/传送后跟随，距离门闩生效 |
| E13 | Playable 存储并下发使用 `MapNoHang`/`MapNoAutoPickup`；`ClientAssistEngine` 挂入 Session/Tick（默认关） |
| 测试 | 钱包回归、天赋属性、合击超距；攻城离线奖励用例补短暂等待降 flaky |

未做（产品化加深，非阻塞）：商城多货币履约、天赋战斗效果 MsgId、英雄 AI/完整 UI、内挂默认开、Avalonia 观感（仍等用户定稿）。

- 2026-10-31：审查加固合入 #30 → `06defcc`。试玩服 `150.158.164.90:7100` 已升至该 tip；CliClient M3 冒烟通过。Windows `dist/windows-player` 已在本机构建对齐 `06defcc`（用户机暂不可达时包在仓库 `dist/windows-player` / `/tmp/windows-player-06defcc.tar.gz`）。矩阵仍 100%。协议仍是 1.21.0。

