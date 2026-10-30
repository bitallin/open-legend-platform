# 08 · NPC 与任务

完成度以矩阵 **N-01–N-08、Q-01–Q-07** 为准。NPC 行为由配置和白名单脚本表达，不兼容第三方 NPC 脚本方言。

## 概述与玩家体验

玩家走近 NPC，打开对话。商人可以买和卖，修理匠收修理费，仓库管理员打开个人仓库，传送和行会能力可以挂在对话选项上。任务有主线链、支线、每日和跑环：接取、击杀计数、交付、拿金币和物品。

样例里这些事很多集中在「阿福」（`npc_village_merchant`）身上，用来验收闭环。生产包应按职责拆成多个 NPC，协议不用变。

## 协议 / MsgId

| MsgId | 消息 | 说明 |
|-------|------|------|
| 2201 / 2202 | `TalkReq` / `TalkResp` | 对话。过远 `ERR_NPC_TOO_FAR (1022)`，未知 `ERR_UNKNOWN_NPC (1023)` |
| 2203 / 2204 | `ShopBuyReq` / `ShopBuyResp` | 购买。金币不足 `ERR_NOT_ENOUGH_GOLD (1019)`，拒绝 `ERR_SHOP_DENIED (1020)` |
| 3901 / 3902 | `ShopSellReq` / `ShopSellResp` | 出售给商店 |
| 2205 / 2206 | `QuestAcceptReq` / `QuestAcceptResp` | 接取。拒绝 `ERR_QUEST_DENIED (1021)` |
| 2207 / 2208 | `QuestTurnInReq` / `QuestTurnInResp` | 交付 |
| 2209 | `QuestProgressNotify` | 击杀计数等进度 |
| 3903 / 3904 | `RepairReq` / `RepairResp` | 修理，可由修理 NPC 触发 |
| 3905–3910 | 个人仓库 | 可由仓库 NPC 打开，报文见 [07](07-equipment-inventory.md) |

对话选项的「传送」「打开行会」若只是客户端切面板，服务端仍要检查距离。地图传送点不经过 `TalkReq`（**N-06** 的说明：传送点已有，对话传送要单独接）。行会创建目前走 2501，不强制 NPC（**N-08**）。

押镖进度、答题场次：**TBD**。

## 服务端模块与配置

- NPC：`version-packs/sample-classic/configs/npcs.json`
- 商店：`configs/shops.json`（标价，矩阵 **Y-02**）
- 任务：`configs/quests.json`
- 脚本：`scripts/npc_quest.lua`，钩子 `on_talk`、`on_buy`、`on_quest_accept`、`on_quest_complete`

样例任务：

| questId | 类型 | 规则字段 |
|---------|------|----------|
| `quest_slime_hunt` | 主线第一步 | 杀 `slime_green` ×2，奖 50 金和药 |
| `quest_bone_path` | 主线后续 | `prerequisiteQuestId=quest_slime_hunt`，杀 `bone_walker` ×2 |
| `quest_boar_side` | 支线 | 杀 `boar_wild` ×1 |
| `quest_daily_slime` | 日常 | `repeatable=daily` |
| `quest_ring_walk` | 跑环 | `repeatable=ring`，目标 `marsh_crawler` |

任务字段：`title`、`description`、`acceptNpcId`、`turnInNpcId`、`objectiveType`（当前为 `kill`）、`targetMonsterId`、`targetCount`、`rewardGold`、`rewardItems`、可选 `prerequisiteQuestId`、可选 `repeatable`。

日常重置以服务器日期为准，存在角色上，不靠客户端报「今天已接」。副本日限制是另一套（`ERR_DUNGEON_DAILY_LIMIT`），不要拿来代替任务日常。

## 客户端表现

- 点 NPC：走近并打开对话（目标键位，矩阵 **O-01** 的 NPC 行）
- 对话面板 **V-23 / N-01**：内容同时可以进聊天
- 商店 **V-24 / N-02 / N-03**
- 修理 **N-04**、仓库 **N-07 / V-25**、任务 **V-29 / Q-05**
- 任务追踪显示标题、进度 `current/target`。数据来自 `QuestProgressNotify` 与接取回包

Unity 若只做了 HUD，NPC 面板仍以 Playable 为验收面（矩阵对 N/Q 的 Status）。两边报文必须一致。

## 登录器相关

无。

## 配置热更与脚本钩子

重载 `quests.json` 时，已接任务保持旧目标直到完成或放弃。放弃报文若还没有，**TBD**；在落地前不要靠删配置把进行中的任务卡死：缺少的 `questId` 应显示「任务已关闭」并允许交付失败后清除进度。

`on_talk` 可以追加欢迎语。商店成交价以 `shops.json` 为准，脚本不能改价。若要活动折扣，改表并 reload，或在服务端增加显式折扣字段后再让脚本可读。

`on_quest_complete` 适合发额外公告。奖励物品以配置为准，避免脚本再 `grant_starter_item` 导致双倍，除非任务设计写明「配置奖励 + 脚本额外礼」。

## 验收标准

| 矩阵 ID | 规格要点 |
|---------|----------|
| N-01 | `Talk` 对话 |
| N-02 | `ShopBuy` |
| N-03 | `ShopSell` 3901 |
| N-04 | 修理 3903 |
| N-05 | 任务 NPC 接取与交付 |
| N-06 | 地图传送点；对话传送需走服务端目的地表 |
| N-07 | 仓库 NPC 打开 3905 流程 |
| N-08 | 行会报文可用；NPC 入口可选 |
| Q-01 | `quest_slime_hunt` → `quest_bone_path` |
| Q-02 | `quest_boar_side` |
| Q-03 | `quest_daily_slime` |
| Q-04 | `quest_ring_walk` |
| Q-05 | 任务追踪 UI |
| Q-06 | `QuestProgressNotify` 击杀计数 |
| Q-07 | 交付奖励金币与物品 |

## 非目标

- 不加载 Market_Def、MerChant 或 QFunction 脚本文本，也不提供同名函数。
- 客户端不本地判定「杀够了就可以领奖」。交付请求由服务端对计数。
- 不把答题答案表放在客户端包里可被直接读出的明文（若做答题，见扩展）。

## 扩展路线

未来工作，未进入矩阵。

1. **更多目标类型**。`objectiveType` 扩展为 `talk`、`collect`、`reach_map`。收集消耗背包物品，到达由服务端位置判定。每种类型先有样例任务再加矩阵行。
2. **新任务章节**。长链用 `chapterId` 分组，客户端追踪只钉一条。这是配置分组，不是另一套任务协议。
3. **押镖**。接镖生成一辆跟随车（实体类型可新增值，**TBD**，在加枚举前不要复用 `ENTITY_PET` 冒充）。失败条件：距离、死亡、超时。奖励走任务交付。
4. **答题**。题目在服务端，按 `questionId` 发题干与选项。客户端回传选项序号。判分和发奖在服务端。题目文本放版本包，不放在第三方题库文件里。
5. **对话脚本分支**。选项 id 列表由 `TalkResp` 下发。客户端只显示，不执行选项里的代码字符串。
