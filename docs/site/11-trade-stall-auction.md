# 11 · 交易、摆摊与拍卖

完成度以矩阵 **Y-01–Y-05** 为准。面对面交易和寄售已经有报文。拍卖行与求购没有 MsgId。

## 概述与玩家体验

金币是基础货币，NPC 用 `shops.json` 标价（买卖见 [08](08-npc-quests.md)）。两名玩家可以面对面：发起、接受、各自放入物品和金币、双方锁定、成交。取消或掉线应退回物品。

寄售在安全区打开窗口：上架、浏览、购买、下架。卖出后通知卖家。样例限制每人最多 10 条，单价 1–100000，税率 0，且 `requireSafeZone`。

回收价走出售给 NPC 的 **Y-05**，不是玩家拍卖。多货币账本与按日限的回收表在扩展路线，不改变 Y-01–Y-05 的验收。

## 协议 / MsgId

面对面（Phase M）：

| MsgId | 消息 |
|-------|------|
| 3941 / 3942 | `TradeStartReq` / `TradeStartResp` |
| 3943 | `TradeInviteNotify` |
| 3944 / 3945 | `TradeRespondReq` / `TradeRespondResp` |
| 3946 / 3947 | `TradePutReq` / `TradePutResp` |
| 3948 | `TradeStateNotify` |
| 3949 / 3950 | `TradeLockReq` / `TradeLockResp` |
| 3951 / 3952 | `TradeCancelReq` / `TradeCancelResp` |
| 3953 | `TradeResultNotify` |

寄售：

| MsgId | 消息 |
|-------|------|
| 2601 / 2602 | `StallListReq` / `StallListResp` |
| 2603 / 2604 | `StallBrowseReq` / `StallBrowseResp` |
| 2605 / 2606 | `StallBuyReq` / `StallBuyResp` |
| 2607 / 2608 | `StallCancelReq` / `StallCancelResp` |
| 2609 | `StallSoldNotify` |

拒绝码：`ERR_TRADE_DENIED (1034)`、`ERR_LISTING_NOT_FOUND`、`ERR_LISTING_LIMIT`、`ERR_SELF_BUY_DENIED`、`ERR_NOT_ENOUGH_GOLD`。

商店出售是 3901，与玩家交易不同。拍卖、求购、跨服摊位：**TBD**。多货币余额同步也是 **TBD**（可挂进世界快照或独立 Notify），见扩展路线，不要编造 MsgId。

成交必须双方锁定后由服务端原子交换。只锁一边不能完成。任一方背包放不下时整笔取消并回滚。

## 服务端模块与配置

- 交易：`server/Legend.Game/GameWorldModule.Trade.cs`
- 寄售配置：`version-packs/sample-classic/configs/trade.json`
- 脚本：`scripts/trade.lua`
- 钩子：`on_stall_list`、`on_stall_buy`、`on_stall_cancel`
- 金币字段在角色状态。支付发金币见 [12](12-mall-payment.md)，到账后与打怪金币是同一余额

`trade.json`：

| 字段 | 样例 |
|------|------|
| `maxListingsPerChar` | 10 |
| `minUnitPriceGold` | 1 |
| `maxUnitPriceGold` | 100000 |
| `taxRatePercent` | 0 |
| `requireSafeZone` | true |

面对面距离使用与 NPC 类似的「过远则拒绝」。具体格数以交易处理代码为准，文档要求至少与对话同量级，不能跨地图锁交易。

合区时寄售以目标区为准，源区寄售丢弃（`tools/Legend.Merge/MERGE_RULES.md`）。合区前应停服并让玩家下架。

## 客户端表现

- 交易面板 **V-26 / Y-03**：邀请、格子、金币、锁定、结果
- 摆摊窗口 **Y-04 / V-22** 的交易入口：上架、浏览、买、下架
- 系统按钮包含交易。面板打开期间移动可以保留，但服务端会因距离取消

价格显示用整数金币。税率非 0 时，买家支付标价，卖家收到扣税后的数额，税额差值在 `Trade`/`Stall` 的服务端账上核销（不进玩家背包）。样例税率为 0，UI 仍应预留「税后可得」。

## 登录器相关

无。

## 配置热更与脚本钩子

重载 `trade.json` 后，新上架使用新上限与新税率。已经上架的条目保持原价，不追溯补税。把 `requireSafeZone` 从 false 改成 true 时，非安全区的旧摊位在下次购买前被服务端下架并退回物品（若实现尚未退回，补实现时以退回为准，避免吞物品）。

脚本钩子用于审计日志，不用于改价。`legend_api.log_info` 可以记一条上架记录。

面对面交易没有单独的 `on_trade` 钩子。需要时再加 `on_trade_commit`，名字落地后再写进 [18](18-script-config-hotreload.md)。

## 验收标准

| 矩阵 ID | 规格要点 |
|---------|----------|
| Y-01 | 角色金币，消费与奖励都改这一余额 |
| Y-02 | NPC 标价 `shops.json` |
| Y-03 | 3941–3953：一件物品加金币、双方锁定后成交 |
| Y-04 | 2601–2609，安全区，上架浏览购买下架 |
| Y-05 | 3901 按回收价卖给 NPC |

## 非目标

- 不提供第三方拍卖行协议或寄售数据库文件导入。
- 不支持交易请求里携带「对方看到的价格」以外的客户端结算。
- 支付订单不是面对面交易。见 [12](12-mall-payment.md)。

## 扩展路线

未来工作，未进入矩阵。样例包与矩阵 **Y-01** 仍是单一金币余额。下列账本与回收表落地前不要改矩阵 Status，也不要抬高计分分母。

### 扩展 · 货币账本（MVP 仍为单金币）

玩家体验：角色持有多种 `currencyId` 余额。HUD 与背包只显示版本包配置的子集；未列出的货币仍在服务端账上，由货币面板查看。

建议配置 `configs/currencies.json`：

| 字段 | 含义 |
|------|------|
| `currencyId` | 稳定 id。建议取值：`gold`、`gold_bound`、`ingot`、`ingot_bound`、`honor`、`token` |
| `displayName` | 客户端显示名。元宝类显示名绑在 `ingot` 上，不新造协议枚举 |
| `tradable` | 能否进入面对面交易 |
| `stallAllowed` | 能否作为寄售标价或货款 |
| `auctionAllowed` | 能否进入拍卖（拍卖本身仍是下方条目） |
| `deductGroup` | 扣款分组。同组内绑定货币优先，绑定不足再扣同组非绑定。不同组不互相抵扣 |

协议：**TBD**。余额同步可以挂进进世界快照，或独立 Notify。不要编造 MsgId 数字，也不要占用 2601 或 3941。

与 **Y-01** 的关系：样例包只启用 `gold`。多货币开启后，Y-01 仍表示「至少有一种基础金币」，不表示「全世界只有金币」。

非目标：不使用匿名 1–99 钱位；不兼容第三方货币命令名。

矩阵占位建议：`Y-06` 多货币余额。实现前不写入矩阵、不计分。

交叉：[12](12-mall-payment.md) 决定履约发哪一种货币；[07](07-equipment-inventory.md) 的绑定物品不可交易。货币本身的 `tradable=false` 与物品 `bind` 是两套开关。

### 扩展 · 回收表（加厚原「回收系统」）

在 **Y-05**（按商店卖给 NPC）之上，增加按物品 id 的回收价，而不是玩家拍卖。

`configs/recycle.json` 每行：

| 字段 | 含义 |
|------|------|
| `itemId` | 可回收物品 |
| `currencyId` | 付出的货币，须在 `currencies.json` 声明。未启用多货币时只能是 `gold` |
| `amount` | 单件回收额 |
| `dailyLimit` | 该角色该 `itemId` 的每日上限。0 表示不限。计数按区服日切，存在服务端 |
| `destroyItem` | 为真时回收即销毁，不进 NPC 商店库存 |

NPC 对话只是入口（见 [08](08-npc-quests.md)）。日限、货币种类与是否销毁由服务端校验。客户端显示的「今日剩余」只反映回包，不能本地先扣。

矩阵占位建议：`Y-07` 回收日限。实现前不写入矩阵、不计分。

### 其余条目

原回收系统一条已升为上一节。下列编号顺延。

1. **拍卖行**。全服限时竞价：起拍价、一口价、保证金、结束时间。新 MsgId **TBD**（不要占用 2601）。流拍退回邮件（[13](13-chat-social.md)）。成交扣税用 `taxRatePercent` 的同一口径。标价货币须 `auctionAllowed`。
2. **求购**。买家冻结对应 `currencyId`（未启用多货币时即为金币），卖家按求购价卖出。冻结必须在服务端账本，不能只从客户端减显示。
3. **多格交易**。Phase N 说明交易可多格。格子上限写进配置。每一格都走 `TradePut` 或一次提交全量快照；若当前实现已是多格，以 `TradeStateNotify` 的重复项为准。多货币开启后，货款带 `currencyId`；未开启时省略该字段即 `gold`。
4. **绑定物品**。扩展字段见 [07](07-equipment-inventory.md)。绑定物品 `TradePut` 与 `StallList` 直接 `ERR_TRADE_DENIED`。
