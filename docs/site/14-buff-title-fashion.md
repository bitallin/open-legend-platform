# 14 · Buff、称号与时装

完成度以矩阵 **V-11**（名字与称号）、**R-02**（时装）、以及技能附带的 Buff 行为（如 **M-05** 魔法盾、**T-03** 施毒）为准。Buff 报文已存在。红点、斗笠、盾牌外观未进入矩阵。

## 概述与玩家体验

增益和减益显示在图标栏：战吼提高攻击，中毒持续掉血并减速，城主 Buff 标记攻城胜利方。死亡时按配置清除。称号画在名字上方：行会职位优先，否则按等级显示勇者、新兵或旅人。时装是披风层，盖住衣服与头盔的外观，不替换武器伤害。翅膀、时装衣武与限时不在 **R-02** 的验收里，见扩展路线。

## 协议 / MsgId

| MsgId | 消息 | 说明 |
|-------|------|------|
| 2807 | `BuffApplyNotify` | 挂上 Buff，含 `BuffState` |
| 2808 | `BuffRemoveNotify` | 移除 |
| 3407 / 3408 | `FashionStatusReq` / `FashionStatusResp` | 已解锁时装 |
| 3409 / 3410 | `FashionEquipReq` / `FashionEquipResp` | 穿脱 |
| 3413 | `AppearanceSyncNotify` | 外观广播，AOI 内其他人更新披风 |
| 快照字段 | `EntitySnapshot.title` | 称号字符串 |

错误码 `ERR_BUFF_DENIED (1051)`、`ERR_FASHION_DENIED (1072)`。

Buff 的数值结算在服务端。客户端图标只反映通知。魔法盾的吸收池使用 `PhaseMRules.AbsorbDamage`，吸收完移除盾。

高位 Buff id、红点计数、盾牌与斗笠的外观 id：**TBD**。

## 服务端模块与配置

- 配置：`version-packs/sample-classic/configs/buffs.json`
- 时装：`configs/fashions.json`
- 逻辑：`GameWorldModule.SkillsBuffs.cs`、`GameWorldModule.Appearance.cs`
- 脚本：`scripts/buffs.lua`、`scripts/appearance.lua`
- 钩子：`on_buff_apply`、`on_buff_expire`、`on_fashion_equip`

样例 Buff：

| buffId | 显示名 | 要点 |
|--------|--------|------|
| `buff_war_cry` | 战吼 | 12 秒，攻击 +25%，`stackRule=refresh`，死亡清除 |
| `debuff_poison` | 中毒 | 10 秒，每 2 秒 5 点，防御 -10%，减速 30%。样例未拆分前，红毒与绿毒都等价于这一条 |
| `buff_castle_lord` | 沙巴克城主 | 攻城奖励，id 与 `siege.json` 的 `ownerBuffId` 一致 |

公共字段：`kind`（`buff`/`debuff`）、`durationMs`、`tickMs`、`atkBonusPercent`、`defBonusPercent`、`moveSlowPercent`、`dotDamagePerTick`、`stackRule`、`clearOnDeath`。

称号规则（矩阵 V-11 说明）：会长、副会长、仓管优先；否则按等级为勇者、新兵或旅人。称号不另加报文，进快照。

时装穿上后纸娃娃披风层替换衣服与头盔的外观层，战斗属性仍来自装备槽（[07](07-equipment-inventory.md)）。

## 客户端表现

- 名字上方称号 **V-11**
- 图标栏读取 2807/2808。剩余时间用 `durationMs` 本地倒计时，到期以 2808 为准校正
- 中毒和减速要能在移动动画上看出来（色调或减速），数值仍以服务端位置为准
- 时装：外观面板穿脱（**R-02**），身后披风。图集分层见 `docs/VISUAL_PARITY.md`
- 城主 Buff 与行会称号可以同时存在：一个是 Buff 图标，一个是名字上的职位/城主文案。城主文案来自 `siege.json` 的 `ownerTitle`

## 登录器相关

无。

## 配置热更与脚本钩子

重载 `buffs.json` 不延长已存在实例的结束时间。已挂上的 Buff 用挂上时的时长。新挂的用新表。删除 `buffId` 后，现存实例在下次 tick 移除并通知。

`stackRule=refresh` 表示重复挂上会刷新持续时间，不叠成多层伤害。若以后要叠加层数，用新规则名 `stack`，并规定上限。

时装热更：未解锁的 id 不能穿，`ERR_FASHION_DENIED`。已穿的时装若从表删除，外观回退到装备纸娃娃并推 3413。

## 验收标准

| 矩阵 ID | 规格要点 |
|---------|----------|
| V-11 | 职位称号优先，否则等级称号 |
| R-02 | 时装穿脱与披风层 |
| M-05 | 魔法盾吸收并消失 |
| T-03 / 技能表 | 中毒 Debuff `debuff_poison` 能被技能挂上 |
| B-05 | 城主 Buff `buff_castle_lord` |

## 非目标

- 不移植第三方 Buff 列表控件或「Buff id 从 10000 起」的保留段。OLP 的 id 是字符串。
- 称号不由客户端本地根据击杀自行升级。
- 时装不改变 `atkBonus`。属性药和装备才改属性。私服里常见的「时装带攻击」不进入默认规格，时装槽不写 `atkBonus`。

## 扩展路线

未来工作，未进入矩阵。

1. **更多堆叠规则**。`stack` 层数上限、`replace` 互斥组（同一 `group` 只留一个）。魔法盾与神圣战甲若互斥，在技能配置里写 `buffGroup`。
2. **红点**。纯客户端派生：邮件未读、任务可交付、仓库审计有新记录。数据来自已有回包的计数，不新增「红点协议」。没有数据的系统不画红点。
3. **盾牌与斗笠、面巾**。外观槽 `shield`、`hat`、`veil`，图集单独层，画在时装之下或之上由 `fashions.json` 的 `layer` 决定。盾牌属性见 [07](07-equipment-inventory.md)。
4. **限时称号**。称号 id 与到期时间放进角色状态，到期后快照恢复等级称号。不要只用 Buff 冒充称号，除非设计明确「称号=Buff 图标+名字」。
5. **高位数值 Buff**。百分比加成超过 int 舒适区时走 [03](03-attributes-growth.md) 的大数值规则。id 继续用字符串，不分段。
6. **翅膀**。外观槽 `wing`。图层相对披风：默认画在披风之上、武器之下，可用 `fashions.json` 的 `layer` 改序。不占武器槽，不改 `atkBonus`。图集层见 [20](20-audio-visual.md)。
7. **时装衣 / 时装武**。槽位 `fashion_armor`、`fashion_weapon`，只替换外观。战斗属性仍来自 [07](07-equipment-inventory.md) 的 `armor` / `weapon`。不要用这两格存放带 `atkBonus` 的装备实例。
8. **限时**。时装实例带 `expiresAt`（区服时间）。到期后服务端脱下并推 `AppearanceSyncNotify`（3413）。客户端倒计时只作展示。
9. **隐藏时装**。客户端开关 `hideFashion`：自己看纸娃娃时可以隐藏时装层。其他人是否看见，由服务端旗标决定；默认他人看见实际穿戴。开关本身不改属性。

以上外形位不改变矩阵 **R-02**（披风）的完成度，未进矩阵、不计分。时装仍然不改变 `atkBonus`。

10. **红毒 / 绿毒**。在 buff 表增加两条，供 [05](05-combat-skills.md) 的 `poison_charm.variants` 使用。未进矩阵。样例包在拆分落地前继续只用 `debuff_poison`。

| buffId | 显示名 | 要点 |
|--------|--------|------|
| `debuff_poison_red` | 红毒 | 持续掉血。`dotDamagePerTick`、`tickMs`、`durationMs` 写在本行。不附带减速与减防 |
| `debuff_poison_green` | 绿毒 | 降低防御。负的 `defBonusPercent` 写在本行。不附带掉血 |

两条使用现有公共字段，可以同时挂在同一目标上，`stackRule` 各自 `refresh`，互不覆盖。内挂如何选择施加哪一种，见 [19](19-client-ui-controls.md)。
