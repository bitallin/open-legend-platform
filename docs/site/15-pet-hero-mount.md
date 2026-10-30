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

英雄创建、出战、忠诚、改名、合击：**TBD**，无 MsgId。职责名见扩展路线 A，不要占用 3501。

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

未来工作，未进入矩阵。默认 NotStarted。英雄整段没有 MsgId，不得标 Done。A/B/C 落地前不改矩阵 **R-01**、**R-03** 的 Status。

### A 英雄（Doc 规格草案，矩阵外）

1. **存档**。独立于主角槽：`hero_id`、`name`、`classId`（复用三职业或子集）、`level`、`loyalty`（0–1000）、`equipment`、`skills[]`、`deployed`、`mode`（`fight`、`follow`、`rest`、`guard`）。英雄 id 与主角 `character_id` 分开，不占用 [02](02-character-class.md) 的 `maxCharactersPerAccount`。
2. **生命周期**。创建（消耗道具或金币；货币 id 见 [11](11-trade-stall-auction.md)）→ 召唤 → 解散 → 改名 → 删除（客户端二次确认，服务端仍以请求为准）。登录进世界后若存档标记默认出战，由服务端直接召唤；客户端不循环重试失败请求。
3. **忠诚**。助战或喂养增加，英雄死亡或长时间未召唤减少。增减量写在配置，不由客户端上报。阈值表 `loyaltyGates[]`：低于某值则拒绝召唤或强制 `rest`，动作由配置声明。
4. **装备**。复用 [07](07-equipment-inventory.md) 的槽位规则。英雄装备不进主角纸娃娃，也不进主角属性汇总。可选另开 `zodiac` 容器，规则与主角生肖盒相同（12 格、逐格解锁、套装走 [16](16-craft-enhance-gem.md)），存档独立，不与主角格子混用。
5. **合击**。见 [05](05-combat-skills.md)。仅当 `deployed`、存活、在距离内、双方冷却就绪。范围与冷却在战斗篇，不在本页另写公式。
6. **协议**。新号段 **TBD**。不要占用 3501 宠物段，避免宠物 UI 误解析。消息只列职责，不编造数字：

| 职责名 | 作用 |
|--------|------|
| Status | 查询存档与是否出战、模式、忠诚 |
| Summon | 召唤出战 |
| Dismiss | 解散回存档 |
| SetMode | 设置 `fight` / `follow` / `rest` / `guard` |
| Equip | 穿脱英雄装备 |
| Rename | 改名 |

7. **客户端**。未开放时不显示可点的英雄按钮。若保留入口，必须标明未开放且不发未知 MsgId。双面板（主角 / 英雄）是扩展 UI，未做之前不要占正式键位。
8. **与宠物**。英雄不占宠物名额。道具宠与技能召唤兽的上限见下文 C。内挂的 `doNotAttackEntityKinds` 包含自身宠物与英雄（`pet_own`、`hero_own`），状态机见 [19](19-client-ui-controls.md)。
9. **矩阵占位建议**。`R-06` 英雄出战、`R-07` 忠诚、`R-08` 合击（或放在战斗段叶子）。实现前不写入矩阵、不计分。

### B 坐骑进阶

矩阵 **R-01** 仍只表示白马移速与脚下外观。下列状态机未进矩阵。

骑乘中的默认允许表（可用 `mounts.json` 覆盖）：

| 动作 | 默认 |
|------|------|
| 走、跑 | 可以 |
| 吃药（`UseItem`） | 可以 |
| 点击 NPC | 可以 |
| 攻击 | 不可以 |
| 拾取 | 不可以 |
| 挖矿 | 不可以 |

`dismountOnAttack`：为真时，攻击请求先下马再进入战斗；为假时攻击直接拒绝（`ERR_MOUNT_DENIED`）。`blockSkillsWhileMounted`：为真时骑乘中的技能请求拒绝。

类型：`solo`、`duo`、`fused`。

- `solo`：仅主人。
- `duo`：主人邀请另一名玩家。邀请距离写在配置（格数）。伤害结算打在邀请人身上，同乘者不另记一份伤害。
- `fused`：连体外观。伤害归属与 `duo` 相同，外观资源 id 单独配置。

地图 `noMount` 见 [04](04-map-movement.md)。进入禁骑地图时服务端下马并通知。

### C 宠物进阶

矩阵 **R-03** 仍是跟随助战。下列未进矩阵。

- **宠物蛋**。物品 `kind` 或 `use` 指向一枚蛋：使用后获得宠物栏位（写入已解锁 `petId`），不直接在脚下生成实体。之后的召唤仍走 3503。
- **等级**。`configs/pet_levels.json`：每级协助倍率。经验来自助战命中。满级不再涨。没有该文件时，协助比例只用 `pets.json` 的常数。
- **状态**。`stored`、`summoned`、`dead`。`dead` 不能助战。复活消耗配置中的货币或道具，成功后回到 `stored`。
- **模式**。`follow`、`attack`、`passive`、`rest`。模式存在服务端。切换请求的 MsgId **TBD**；未落地前不要复用 3503 硬凑模式语义。
- **虚装**。一组属性 id 列表（`statGroupIds[]`），没有真实格子，不进仓库。加成进入宠物协助结算，不进主角纸娃娃。
- **数量**。道具宠 1 只，召唤兽 1 只。超限时新的召唤解散旧的并通知。英雄出战不占此名额。
- **钩子建议名**。`on_pet_level_up`、`on_pet_die`。落地时写入 [18](18-script-config-hotreload.md)。现有 `on_pet_summon` / `on_pet_dismiss` 保持只打日志。

### 其余

1. **宝宝强度**。召唤兽的攻击用技能等级缩放，而不是玩家面板攻击的隐藏百分比。公式写在 `summon_bone` / `summon_beast` 的配置扩展字段，服务端计算。
2. **假人**。运营用的氛围角色：无掉落、无经验、可选自动聊天（文本来自版本包，不执行脚本）。实体类型 **TBD**。不占用在线玩家名额，不进好友列表。假人不是内挂：内挂是玩家客户端辅助（[19](19-client-ui-controls.md)），假人没有玩家经济收益，也不能被玩家当成离线托管角色。
