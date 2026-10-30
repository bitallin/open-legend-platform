# 03 · 属性与成长

矩阵没有单独的「属性」字母段。相关叶子是战斗与成长反馈：**C-06**（MP）、**C-12**（死亡惩罚）、**U-11**（升级音）、**V-13**（HUD）。本页把等级、攻防、负重和死亡惩罚收成一份规格。完成度仍以这些 ID 的 Status 为准。

## 概述与玩家体验

角色有等级、经验、生命、魔法、攻击、防御、负重、金币和 PK 计数。升级时客户端播升级音并给出反馈。死亡时按配置扣除一部分金币，并有机会掉落背包物品；攻城进行中不执行这套惩罚（与 C-12 一致）。

样例曲线很短：`maxLevel` 为 10，便于验收。生产包可以加长 `entries`，不必改协议。天赋树与内功经络在扩展路线，样例只有这一条等级曲线。

## 协议 / MsgId

| MsgId | 消息 | 说明 |
|-------|------|------|
| 2403 | `ExpGainNotify` | 获得经验 |
| 2404 | `LevelUpNotify` | 升级 |
| 2102 / 2103 | `AttackResp` / `CombatSync` | 攻击回包带当前 MP 等战斗结果，供 HUD 对齐 |
| 3923 | `PkSyncNotify` | PK 计数与名字色，见 [10](10-pk-redname.md) |

`EntitySnapshot` 在进世界和 AOI 时带等级、外观、称号、名字色。没有单独的「属性全量面板」请求。人物面板用进世界快照、装备变更和升级通知在客户端重算展示；若以后要防作弊的面板校验，以服务端装备汇总为准，报文 **TBD**。

内功等级、经络、天赋树、自定义属性槽、大数值压缩字段：**TBD**。字段形状见扩展路线，不要编造 MsgId。

## 服务端模块与配置

- 曲线：`server/Legend.Game/LevelCurve.cs`，数据 `version-packs/sample-classic/configs/levels.json`
- 样例：1 级升 2 级需要 50 经验，逐级增加到 9→10 需要 800，10 级 `expToNext` 为 0
- `classGrowth` 每级加算：

| 职业 | atk | def | hp | mp |
|------|-----|-----|----|----|
| warrior | 3 | 1 | 20 | 5 |
| mage | 4 | 0 | 12 | 18 |
| taoist | 2 | 1 | 15 | 12 |

- 死亡：`deathGoldPercent` 10、`deathItemChancePercent` 25、`deathMaxItems` 1
- 负重：`PhaseMRules.BaseCarryWeight = 80`，每级 `WeightPerLevel = 5`。背包重量是物品 `weight` 乘数量。超重时的拒绝码走现有移动/拾取错误（以调用点为准），不要在客户端先放行
- 装备加成与损坏折半：`PhaseMRules.ApplyBroken`。耐久为 0 时攻防血加成减半，见 [07](07-equipment-inventory.md)
- 升级事件：`PlayerLeveledUpEvent` → Lua `on_level_up`

战斗力没有单独字段。若 UI 要显示「战力」，客户端可以用攻击、防御、生命做展示用加权，**不参与**命中判定。真正伤害在 [05](05-combat-skills.md)。

## 客户端表现

- HUD：矩阵 **V-13**（生命、魔法、经验、负重、等级）。Playable 底栏在 `Ui/MirChrome.cs` 与 `Ui/GameWindow.cs`
- 升级音：**U-11**，`client/Legend.Playable/Audio/PlayableAudio.cs`（程序波形）
- 升级飘字/光效属于画面 **V-08 / V-09**，资源在原创图集
- Unity HUD：`client/unity-mvp/Assets/LegendClient/Scripts/UI/LegendHudController.cs`。两边都只展示服务端给的数值

经验条用 `LevelUpNotify` 与 `ExpGainNotify` 填充。缺少当前经验字段时，客户端可以只播「升级」而不画精确百分比；要精确条，应在现有通知里带当前经验与升级所需（若 proto 已有字段则绑定，没有则 **TBD**，不要本地猜曲线）。

## 登录器相关

无。等级存在区服数据库，登录器不缓存角色属性。

## 配置热更与脚本钩子

`levels.json` 随 `reload-pack` 重载。规格：已累积经验不因热更回滚；若新表把 `maxLevel` 降低到当前等级以下，角色停在旧等级但不再获得有效升级，直到表把上限加回来。调低 `expToNext` 允许立刻升级。

`on_level_up` 可发公告或奖励。奖励仍通过 `legend_api.grant_starter_item` 这条受权限控制的路径，或后续正式的发物品 API（现在桥接只有 `grant_starter_item`、`send_welcome`、`log_info`）。不要在 Lua 里直接改数据库。

## 验收标准

| 矩阵 ID | 规格要点 |
|---------|----------|
| C-06 | 技能消耗 MP，攻击回包更新 MP，HUD 跟上 |
| C-12 | 死亡按 `levels.json` 掉金币和物品；攻城中不掉 |
| U-11 | 升级有音效 |
| V-13 | HUD 展示等级与资源条 |

击杀经验来自怪物 `expReward`，组队与师徒加成见 [13](13-chat-social.md)，不在本页改公式以外的社交规则。

## 非目标

- 不导入第三方经验表或内功表的二进制/INI。
- 不把战斗力做成服务端信任的客户端上报值。
- 不在热更时洗掉玩家已有等级。

## 扩展路线

未来工作，未进入矩阵。

1. **内功与经络**。第二经验池 `internalLevel`，与天赋点不是同一资源。配置建议 `configs/internal.json`：等级、每级加成、是否占 MP。可选 `meridians[]`：穴位 id、打通所需材料（`itemId` 与数量）、打通后的加成（白名单 `statId`）。未写 `meridians[]` 时只有等级池，没有穴位。报文可复用 `ExpGainNotify` 增加类型字段，或新 MsgId **TBD**。内功不影响三职业 id。
2. **天赋树**。与内功分界：内功是第二经验池；天赋是可重置的节点图。矩阵外。

`configs/talents.json`：

| 字段 | 含义 |
|------|------|
| `treeId` | 树 id。可按职业拆多棵 |
| `nodes[]` | 每项含 `nodeId`、`requires[]`（前置节点）、`costPoints`、`effects[]` |
| `pointsPerLevel` | 每级角色等级给予的天赋点 |
| `resetCost` | 重置消耗。引用 `currencyId` 与数量，货币表见 [11](11-trade-stall-auction.md) |

`effects[]` 只允许白名单 `statId` 或 `skillModifierId`。协议 **TBD**：加点、重置、全量同步。不要编造 MsgId。

非目标：脚本不得任意写属性名。未在 `talents.json` 声明的节点 id 拒绝。

矩阵占位建议：成长段未来叶子（天赋加点、内功经络）。实现前不写入矩阵、不计分。

3. **连击**。连续命中的短期加成更接近战斗状态，规格放在 [05](05-combat-skills.md)。本页只要求：连击点不写入等级存档，死亡或脱战清零。
4. **自定义属性**。若运营要「暴击、吸血」之外的键，用版本包声明 `statId` 与上限，服务端白名单求和。禁止脚本任意写内存属性名。天赋与经络的加成必须落在这份白名单上。
5. **大数值**。生命或攻击超过 32 位时，协议用字符串十进制或拆成 `hi/lo`，计算在服务端用十进制或 64 位以上类型。不要用未定义的 B0–B99 变量槽。展示层再缩写万/亿。在样例曲线（最大 10 级）阶段不启用。
6. **死亡惩罚扩展**。红名加重、装备掉落保护（投保）见 [10](10-pk-redname.md) 与 [16](16-craft-enhance-gem.md)。改 `deathGoldPercent` 即可调整金币比例，不必发版本。
