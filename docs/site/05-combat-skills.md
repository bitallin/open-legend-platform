# 05 · 战斗与技能

完成度以矩阵 **C-03–C-15、W-01–W-08、M-01–M-11、T-01–T-10、P-08** 为准。技能 id 原创，显示名对齐经典叫法。

## 概述与玩家体验

玩家选中怪物或角色后普攻或放技能。服务端检查距离、冷却、MP、施法时间、安全区和攻击模式，然后广播命中、未命中、治疗或击杀。战士近战、法师远程与地面效果、道士治疗与召唤，都走同一条 `AttackReq`，用 `skill_id` 区分。

技能可以随使用增长技能经验并升级，伤害和冷却读表。学习技能书走独立请求。客户端技能栏把 F1–F8 映射到当前职业的技能 id。合击判定在扩展路线，当前没有合击槽。

## 协议 / MsgId

| MsgId | 消息 | 说明 |
|-------|------|------|
| 2101 / 2102 | `AttackReq` / `AttackResp` | `AttackReq` 含目标与技能。`force` 表示强制攻击（Phase M） |
| 2103 | `CombatSync` | 结果类型：`COMBAT_HIT`、`COMBAT_MISS`、`COMBAT_HEAL`、`COMBAT_KILL` |
| 2104 / 2105 | `EntitySpawn` / `EntityDespawn` | 召唤物等实体出现与消失 |
| 2801 / 2802 | `LearnSkillReq` / `LearnSkillResp` | 技能书。拒绝：`ERR_SKILL_LEARN_DENIED`、`ERR_SKILL_NOT_LEARNED`、`ERR_SKILL_MAX_LEVEL` |
| 2803 / 2804 | `SkillListReq` / `SkillListResp` | 已学技能与等级 |
| 2805 | `SkillListNotify` | 列表变化 |
| 2806 | `SkillLevelUpNotify` | 技能升级 |
| 3967 | `GroundEffectNotify` | 地面火墙等持续区域 |
| 3921 / 3922 | `SetAttackModeReq` / `SetAttackModeResp` | 攻击模式，详见 [10](10-pk-redname.md) |

常见拒绝：`ERR_INVALID_SKILL (1012)`、`ERR_SKILL_COOLDOWN (1013)`、`ERR_OUT_OF_RANGE (1014)`、`ERR_SAFE_ZONE (1015)`、`ERR_INVALID_TARGET (1016)`、`ERR_DEAD (1018)`。

连击条、合击报文：**TBD**。合击若落地，优先在 `CombatSync` 上带表现位，不要复用 `force`。判定步骤见扩展路线。英雄未实现前客户端不显示合击槽。

## 服务端模块与配置

- 施法：`server/Legend.Game/GameWorldModule.SkillCast.cs`
- Buff 与技能状态：`GameWorldModule.SkillsBuffs.cs`
- 配置：`version-packs/sample-classic/configs/skills.json`
- 等级曲线辅助：`server/Legend.Contracts` 中与 Phase 规则相邻的纯函数；逐级伤害在矩阵 **W-08** 记为 `SkillHelper`

每条技能的稳定字段：`skillId`、`displayName`、`mpCost`、`cooldownMs`、`range`、`damageMultiplier`、`damageMultiplierPerLevel`、可选 `cooldownMsPerLevel`、`allowedClasses`、`requiresLearn`、`maxLevel`、`skillExpPerCast`、`skillExpPerKill`、`expToNextLevel`、可选 `castTimeMs`、可选 `applyDebuffOnHit`。

样例技能（显示名以 JSON 为准）：

| 矩阵 | skillId | 显示名 | 职业 |
|------|---------|--------|------|
| W-01 | `basic_slash` | 基本剑术 | 三职业普攻 |
| W-02 | `power_strike` | 攻杀剑术 | warrior |
| W-03 | `thrust_slash` | 刺杀剑术 | warrior |
| W-04 | `half_moon` | 半月弯刀 | warrior，朝向前方扇形 |
| W-05 | `fatal_strike` | 烈火剑法 | warrior |
| W-06 | `rush` | 野蛮冲撞 | warrior，朝目标冲最多 3 格，不进入安全区 |
| M-01 | `arcane_bolt` | 火球术 | mage，命中可挂 `debuff_poison` |
| M-02 | `repulse_ring` | 抗拒火环 | mage |
| M-03 | `fire_wall` | 火墙 | mage，地面效果 3967 |
| M-04 | `lightning` | 雷电术 | mage |
| M-05 | `mana_shield` | 魔法盾 | mage，吸收上限见实现（矩阵说明最多 80） |
| M-06 | `ice_roar` | 冰咆哮 | mage |
| M-07 | `fast_beam` | 疾光电影 | mage，直线 |
| M-08 | `hell_lightning` | 地狱雷光 | mage |
| M-09 | `burst_flame` | 爆裂火焰 | mage |
| M-10 | `blink` | 瞬息移动 | mage |
| M-11 | `fire_rain` | 火雨 | mage |
| T-01 | `heal` | 治愈术 | taoist |
| T-02 | `spirit_hit` | 精神力战法 | taoist |
| T-03 | `poison_charm` | 施毒术 | taoist |
| T-04 | `soul_charm` | 灵魂火符 | taoist |
| T-05 | `hide` | 隐身术 | taoist |
| T-06 | `summon_bone` | 召唤骷髅 | taoist，见 [15](15-pet-hero-mount.md) |
| T-07 | `summon_beast` | 召唤神兽 | taoist |
| T-08 | `holy_armor` | 神圣战甲术 | taoist |
| T-09 | `ghost_shield` | 幽灵盾 | taoist |
| T-10 | `group_hide` | 集体隐身术 | taoist |

未写入 `skills.json` 的高阶技能不要在客户端做假图标。新增时同时加矩阵行或更新已有 ID 的说明。

走位接近后再打：**C-15**。客户端可以自动走近，但超出 `range` 的请求由服务端拒绝。

## 客户端表现

- 输入：`client/Legend.Playable/Input/ClassicInput.cs`。左键选中，Shift+左键强制攻击，Space / J 为辅助攻击键
- 战斗表现：飘字 **C-14 / V-09**，技能光效 **V-08**
- 技能栏 **V-14 / O-08**：F1–F8
- 攻击模式指示 **V-21**，切换 **P-08 / O-13**（Ctrl+H）
- Unity：`Scripts/Combat/LegendCombatController.cs`、`LegendDamagePopup.cs`

施法锁帧期间客户端不叠第二下动画，服务端仍以冷却时间为准。

## 登录器相关

无。

## 配置热更与脚本钩子

钩子：`on_learn_skill`、`on_skill_level_up`。击杀 `on_kill` 在怪物死亡时触发，可用于技能经验以外的活动脚本。

热重载 `skills.json` 后，新冷却与新倍率对下一次施法生效。已经在冷却中的时间戳不延长、不缩短到负数。删除技能 id 时，已学会的角色施放得到 `ERR_INVALID_SKILL`，存档里的技能等级保留到 id 回归。

## 验收标准

战斗：**C-03** 普攻 `basic_slash`，**C-04** 指定 skill id，**C-05** 冷却，**C-06** MP，**C-07** 前摇 `castTimeMs`，**C-08** 选中，**C-09** `force`，**C-10** 死亡，**C-14** 飘字，**C-15** 距离。

技能树：W-01–W-08、M-01–M-11、T-01–T-10，id 见上表。**W-07** 技能书与技能升级（2801、2806）。**P-08** Ctrl+H 循环攻击模式。

## 非目标

- 不读取第三方技能威力表，不实现其施法命令名。
- 客户端本地结算的伤害只允许做预表现，最终数字以 `CombatSync` 为准。
- 不把「隔位刺杀」做成地图文件标记；刺杀范围写在 `thrust_slash` 的服务端形状里。

## 扩展路线

未来工作，未进入矩阵。

1. **连击**。服务端记录最近命中时间与段数，超时清零。加成用短期 Buff（[14](14-buff-title-fashion.md)），不新造伤害通道。
2. **合击**。英雄未实现前，客户端隐藏合击槽，且不发送合击请求。英雄草案见 [15](15-pet-hero-mount.md) 扩展路线「A 英雄」。矩阵占位建议：战斗段叶子或 `R-08`。落地再写入矩阵，本条不计分。

服务端判定顺序：

1. 主角与英雄均存活、在同一张地图，距离 ≤ 配置 `hejiRangeCells`。
2. 双方相关技能冷却就绪，且配置中存在 `comboId`（按职业对：主角职业 + 英雄职业）。没有 `comboId` 则不能合击。
3. 扣除双方冷却与 MP。数值在配置里，不由客户端上传。
4. 结算一次伤害。公式字段 **TBD**，由服务端计算。不要抄第三方威力表或命令名。
5. 广播 `CombatSync`，可带 `combo=true` 作为表现位。不要复用 `AttackReq.force` 表示合击。

任一步失败则整次合击不发生，不扣到一半。客户端只播放带 `combo=true` 的同步，不本地结算第二段伤害。仅当英雄 `deployed`、存活、在距离内且双方就绪时才进入上述步骤。
3. **技能书物品**。`requiresLearn: true` 的技能必须先 2801。物品使用与学习的衔接保持在服务端：使用技能书成功后推 `SkillListNotify`。
4. **更多地面效果**。沿用 3967，用效果 id 区分火墙、毒区、治疗圈。id 放版本包，不放魔法数字在客户端。
5. **开关技能**。半月、刺杀一类「开启后普攻变形态」若要做成开关，用 Buff 或技能状态位，**TBD** 字段。在落地前，每次施放就是一次 `AttackReq`。
6. **红毒 / 绿毒**。样例可继续单一 `debuff_poison`（见 [14](14-buff-title-fashion.md)）。扩展时 `poison_charm` 增加 `variants`：`poison_red` 施加 `debuff_poison_red`（持续掉血），`poison_green` 施加 `debuff_poison_green`（降低防御）。两种可以同时存在，数值写在 buff 表，不写进协议里的物品显示名。客户端内挂里的自动施毒开关留在 [19](19-client-ui-controls.md)，本页不写那套状态机。未进矩阵。
7. **三职业默认技能旋转**。内挂用的默认施法顺序主规格在 [19](19-client-ui-controls.md)。这里只约束：旋转中的 `skillId` 必须是本页技能表已有 id；冷却与 MP 仍由服务端判定。
