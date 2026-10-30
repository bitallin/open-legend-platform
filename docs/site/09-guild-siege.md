# 09 · 行会与攻沙

完成度以矩阵 **S-03–S-07、B-01–B-06、I-06** 为准。行会战和沙巴克攻城是两套规则，不要混用报文。

## 概述与玩家体验

玩家花费金币创建行会，或加入已有行会。会长任命副会、仓管和成员，编辑公告。行会频道聊天见 [13](13-chat-social.md)。行会仓库按职位存取，清空和审计有权限。

攻城是沙巴克循环：宣战、按日程或管理员开战、攻击城门与城墙、占领旗帜、城主行会拿奖励与称号 Buff。进行中的攻城会抑制普通死亡掉装（[03](03-attributes-growth.md)）。

## 协议 / MsgId

行会：

| MsgId | 消息 |
|-------|------|
| 2501 / 2502 | `GuildCreateReq` / `GuildCreateResp` |
| 2503 / 2504 | `GuildJoinReq` / `GuildJoinResp` |
| 2505 / 2506 | `GuildLeaveReq` / `GuildLeaveResp` |
| 2507 / 2508 | `GuildInfoReq` / `GuildInfoResp`（含 `notice`、成员与职位） |
| 3701 / 3702 | `GuildSetRankReq` / `GuildSetRankResp` |
| 3601 / 3602 | `GuildWarehouseListReq` / `Resp` |
| 3603 / 3604 | 存入 |
| 3605 / 3606 | 取出 |
| 3607 / 3608 | 清空 |
| 3703 / 3704 | `GuildWarehouseListAuditReq` / `Resp` |

拒绝码：`ERR_GUILD_DENIED (1027)`、`ERR_GUILD_NOT_FOUND`、`ERR_GUILD_NAME_TAKEN`、`ERR_ALREADY_IN_GUILD`、`ERR_GUILD_FULL`、`ERR_NOT_IN_GUILD`、`ERR_GUILD_WAREHOUSE_DENIED`、`ERR_GUILD_WAREHOUSE_FULL`、`ERR_GUILD_RANK_DENIED`、`ERR_GUILD_INVALID_RANK`。

攻城：

| MsgId | 消息 |
|-------|------|
| 2901 / 2902 | `SiegeDeclareReq` / `SiegeDeclareResp` |
| 2903 / 2904 | `SiegeStatusReq` / `SiegeStatusResp`（含 `schedule_note`） |
| 2905 | `SiegeStateNotify` |
| 2906 / 2907 | `SiegeControlReq` / `SiegeControlResp` |
| 2908 / 2909 | `SiegeCaptureReq` / `SiegeCaptureResp` |

拒绝码：`ERR_SIEGE_DENIED`、`ERR_SIEGE_NOT_ACTIVE`、`ERR_SIEGE_ALREADY_DECLARED`、`ERR_SIEGE_CONTESTED`。

Admin：`POST /admin/siege`，body 里 `action` 为 `start` 或 `stop`。这是运维开关，不是玩家报文。

日常行会战（野战、积分、非城门）没有独立 MsgId，**S-07** 的说明写明攻城是另一套、日常行会战未单独做。不要把 2901 宣战当成野战。

## 服务端模块与配置

- 逻辑：`server/Legend.Game/GameWorldModule.cs` 的行会路径，攻城在 `GameWorldModule.Siege.cs`
- 存储：`server/Legend.Storage/GuildStores.cs`、`SiegeStores.cs`。行会仓库列 `guilds.warehouse_json`
- 配置：`configs/guild.json`、`configs/siege.json`
- 脚本：`scripts/guild.lua`、`scripts/siege.lua`

`guild.json` 要点：创建费 50、名字 2–12、人数 20、仓库 40 格、成员默认可存取、会长/副会/仓管可清空、会长可任命、副会最多 2、仓管最多 3、审计保留 100 条。

`siege.json` 要点：`castleId=sabuk_castle`，显示名「沙巴克王城」，宣战费 50，仅会长可宣战，占领矩形在 `dark_forest`，城主 Buff `buff_castle_lord`，称号文案「沙巴克城主行会」，胜方金币 100，`openHour` 20、`closeHour` 21（UTC 说明写在 `scheduleNote`），城门怪 `castle_gate`，门洞传送 `gate`。城门被打破后才能进皇宫区域。日程结束和手动停战共用发奖（Phase O）。

城门、城墙、旗帜在客户端用原创图集实体表现。服务端有攻城状态机；若某次构建还没有独立碰撞实体，以矩阵 B-01/B-02 的说明和当前 `Siege` 代码为准。

## 客户端表现

- 行会面板 **V-27**：成员、公告、任命（点选副会/仓管/成员，**S-05**）
- 攻城：宣战、日程、攻击城门/城墙/旗帜、占领（**B-06**）
- 系统按钮里的行会入口 **V-22**
- 城主称号与 Buff 图标见 [14](14-buff-title-fashion.md)

开战条件：玩家宣战之后，仍可由控制报文或管理员在窗口内开始。规格要求客户端把「已宣战 / 进行中 / 未开始」分开显示，避免玩家以为点宣战就会立刻拆门。

## 登录器相关

无。行会数据在区服库。合区时行会改名规则见 [17](17-cross-realm-merge.md)。

## 配置热更与脚本钩子

钩子：`on_guild_create`、`on_guild_join`、`on_guild_leave`、`on_guild_rank_change`、`on_guild_warehouse_deposit`、`on_guild_warehouse_withdraw`、`on_guild_warehouse_clear`、`on_warehouse_audit`、`on_siege_declare`、`on_siege_capture`、`on_siege_end`。

热重载 `guild.json` 不解散已有行会。把 `maxMembers` 调低后，不再接受新成员，直到人数降到上限以下；不随机踢人。仓库容量调低时，禁止再存入，已有格子保留，允许取出。

攻城进行中重载 `siege.json` 不应当场改占领矩形导致秒占。规格：进行中的场次沿用开始时的快照，下一场用新表。

## 验收标准

| 矩阵 ID | 规格要点 |
|---------|----------|
| S-03 | 2501 创建，扣 `createCostGold` |
| S-04 | 2503 加入、2505 离开 |
| S-05 | 3701 任命，上限见配置 |
| S-06 | `GuildInfoResp.notice` |
| S-07 | 与攻城区分。日常行会战见扩展 |
| I-06 | 3601–3608 与审计 3703 |
| B-01 | 城门 `castle_gate` |
| B-02 | 城墙 |
| B-03 | 2908 占领旗帜 |
| B-04 | `openHour`/`closeHour` 与 Admin start/stop |
| B-05 | `winnerRewardGold` 与 `buff_castle_lord` |
| B-06 | 宣战 → 开战 → 占旗 → 领奖 |

## 非目标

- 不读取第三方 Castle 目录或 GuildBase 文本。
- 跨服攻城不在本页的现有报文里。见 [17](17-cross-realm-merge.md)。
- 行会仓库不是个人仓库，也不能当邮件附件中转。

## 扩展路线

未来工作，未进入矩阵。

1. **日常行会战**。两个行会在约战地图限时积分，杀人或占点得分。使用新的状态，不要复用 `sabuk_castle`。MsgId **TBD**。S-07 若要改成 Done 的完整野战，先改矩阵说明。
2. **守卫**。城主可在城堡矩形内放置守卫怪，模板仍用 `monsters.json`，归属行会 id 写在刷点上。守卫攻击规则遵循攻城敌对，不吃普通 PK 值。
3. **行会技能与资金**。除创建费和宣战费外的公款账户 **TBD**。在账户存在之前，奖励直接进会长邮件或在线成员金币，并在 `on_siege_end` 记日志。
4. **联盟**。两个行会 id 的互不伤害关系，叠加在攻击模式之上。未做之前，同行会免伤只来自 `guild` 模式（[10](10-pk-redname.md)）。
