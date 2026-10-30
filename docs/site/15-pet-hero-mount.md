# 15 · 宠物、英雄与坐骑

完成度以矩阵 **R-01、R-03、R-04、T-06、T-07** 为准。宠物跟随和坐骑已有报文。英雄系统没有 MsgId，整段在扩展路线，不得标 Done。

## 概述与玩家体验

玩家在外观面板解锁、召唤、收回宠物。宠物跟在身后，按冷却协助造成一定比例伤害。道士的召唤骷髅、召唤神兽走技能，和道具宠可以同时存在于「召唤物」规则里，但 AI 参数以各自配置为准。

坐骑（样例白马）提高移速，角色脚下绘制坐骑。收回坐骑后间隔恢复为步行。

英雄是另一名可培养的出战角色：独立名字、忠诚、装备和合击。当前版本没有这套循环，客户端不要做可点的英雄按钮，除非按钮明确标为未开放且不发未知 MsgId。

## 协议 / MsgId

宠物：

| MsgId | 消息 |
|-------|------|
| 3501 / 3502 | `PetStatusReq` / `PetStatusResp` |
| 3503 / 3504 | `PetSummonReq` / `PetSummonResp` |
| 3505 / 3506 | `PetDismissReq` / `PetDismissResp` |
| 3511 | `PetSyncNotify` |

坐骑：

| MsgId | 消息 |
|-------|------|
| 3401 / 3402 | `MountStatusReq` / `MountStatusResp` |
| 3403 / 3404 | `MountSummonReq` / `MountSummonResp` |
| 3405 / 3406 | `MountDismissReq` / `MountDismissResp` |

实体类型 `ENTITY_PET = 4`。错误 `ERR_PET_DENIED (1074)`、`ERR_MOUNT_DENIED (1071)`。

技能召唤使用 `AttackReq` 的 `summon_bone` / `summon_beast`，生成实体走 `EntitySpawn`（2104）。不要为召唤技能再发一遍 3503，除非实现已经统一成宠物 id。规格允许两种实现并存，但客户端必须根据回包画实体，不能本地生成一只只有自己看得见的宠。

英雄创建、出战、忠诚、改名、合击：**TBD**，无 MsgId。

## 服务端模块与配置

- 宠物逻辑：`server/Legend.Game/GameWorldModule.Pets.cs`
- 配置：`configs/pets.json`、`configs/mounts.json`
- 脚本：`scripts/pets.lua`、`scripts/appearance.lua`
- 钩子：`on_pet_summon`、`on_pet_dismiss`、`on_mount`

样例宠：

| petId | 显示名 | 协助 |
|-------|--------|------|
| `pet_wolf_pup` | 幼狼 | 25% 伤害，间隔 1200ms，距离 3，跟随 2 |
| `pet_bone_servant` | 骨仆 | 30%，1400ms |
| `pet_spirit_beast` | 灵兽 | 45%，1600ms |

解锁物品：`item_pet_wolf_pup` 等，写在 `items.json`。

坐骑：`mount_white_horse` 白马，`moveSpeedBonusPercent` 50，默认移动间隔 `defaultMoveIntervalMs` 300。移速仍然受 `ERR_MOVE_TOO_FAST` 约束。

道士召唤兽（**R-04**）应有独立追击与攻击间隔，而不是把玩家普攻动画套在空实体上。技能 id 见 [05](05-combat-skills.md)。

## 客户端表现

- 外观面板：解锁、召唤、收回（**R-01 / R-03**）
- 白马画在脚下，宠物使用狼等原创帧（`VISUAL_PARITY.md`）
- 移速变化要换跑动动画间隔，格子仍由 `MoveSync` 推动
- 召唤技能释放后等待 `EntitySpawn`，超时无实体则提示失败

Unity 实体视图需识别 `ENTITY_PET`，不要把宠物画成掉落物。

## 登录器相关

无。

## 配置热更与脚本钩子

重载 `pets.json` 改变协助比例后，下一次协助攻击生效。已召唤的宠物若 id 被删，服务端应解散并通知。坐骑同理。

`on_pet_summon` 不要再发一只宠物。脚本只打日志或公告。

## 验收标准

| 矩阵 ID | 规格要点 |
|---------|----------|
| R-01 | 3403 召唤白马，移速与脚下外观 |
| R-03 | 3503 召唤幼狼等，跟随并助战 |
| R-04 | 召唤兽独立 AI |
| T-06 | `summon_bone` |
| T-07 | `summon_beast` |

CLI 验收里 Host 支持 `--pet` 一类参数（仓库 README 的 CliClient 说明）。自动化以 `Legend.Tests` 与对应 verify 脚本为准。

## 非目标

- 不实现第三方骑乘命令、宠物模式编号或宝宝属性命令。
- 宠物伤害不由客户端上报。
- 英雄不是把第二个账号角色塞进同一格子。未做之前，三个角色槽只属于 [02](02-character-class.md)。

## 扩展路线

未来工作，未进入矩阵。默认 NotStarted。

1. **英雄**。建议独立存档：`hero_id`、名字、职业（复用三职业或子集）、等级、忠诚、装备（复用槽位规则）、是否出战。能力清单：创建、召唤、忠诚升降（死亡下降、喂养上升）、改名、装备变化触发被动、与主角合击。合击判定见 [05](05-combat-skills.md)。报文号段 **TBD**，建议新段而不是复用 3501，以免宠物 UI 误解析。
2. **宠物等级**。`configs/pet_levels.json`：每级协助倍率。经验来自助战命中。满级不再涨。没有该文件时，协助比例只用 `pets.json` 的常数。
3. **宝宝强度**。召唤兽的攻击用技能等级缩放，而不是玩家面板攻击的隐藏百分比。公式写在 `summon_bone` / `summon_beast` 的配置扩展字段，服务端计算。
4. **同时存在数量**。道具宠 1 只，召唤兽 1 只。超限时新的召唤解散旧的并通知。英雄出战不占宠物名额。
5. **假人**。运营用的氛围角色：无掉落、无经验、可选自动聊天（文本来自版本包，不执行脚本）。实体类型 **TBD**。不占用在线玩家名额，不进好友列表。假人不是给玩家用的托管外挂。
6. **自动召唤英雄**。登录进世界后若存档标记「默认出战」，服务端直接召唤。客户端不循环重试失败请求。
