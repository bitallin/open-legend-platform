# 07 · 装备、背包与仓库

完成度以矩阵 **E-01–E-14、I-01–I-06** 为准。槽位是字符串，不是第三方物品模式编号。

## 概述与玩家体验

玩家把武器和防具穿到固定槽位，看到攻击与防御变化。装备有耐久，损坏后加成减半，修理花金币。鉴定走单独请求。背包可以喝药、受负重限制，数字键 1–6 对应快捷药。个人仓库通过 NPC 或仓库面板存取。行会仓库是另一套格子，带职位权限和审计。

物品显示名可以像「金创药(小)」「新手木刀」，id 仍然是 `item_hp_potion`、`item_wooden_blade` 这类原创字符串。

## 协议 / MsgId

| MsgId | 消息 | 说明 |
|-------|------|------|
| 2301 / 2302 | `EquipReq` / `EquipResp` | 穿戴。拒绝 `ERR_EQUIP_DENIED (1024)` |
| 2303 / 2304 | `UnequipReq` / `UnequipResp` | 脱下 |
| 2305 / 2306 | `EnhanceEquipReq` / `EnhanceEquipResp` | 强化，见 [16](16-craft-enhance-gem.md) |
| 2401 / 2402 | `UseItemReq` / `UseItemResp` | 药水等。拒绝 `ERR_USE_ITEM_DENIED (1026)` |
| 2109 | `InventoryNotify` | 背包全量或增量通知 |
| 3931 / 3932 | `IdentifyReq` / `IdentifyResp` | 鉴定 |
| 3903 / 3904 | `RepairReq` / `RepairResp` | 修理。按已装备强化等级扣金币 |
| 3905 / 3906 | `StorageListReq` / `StorageListResp` | 个人仓库 |
| 3907 / 3908 | `StorageDepositReq` / `StorageDepositResp` | 存入 |
| 3909 / 3910 | `StorageWithdrawReq` / `StorageWithdrawResp` | 取出 |
| 3601–3608 | 行会仓库列表、存、取、清空 | 见 [09](09-guild-siege.md) |
| 3407–3413 | 时装 | 见 [14](14-buff-title-fashion.md)，不占武器槽 |

`EquippedSlot` 含耐久（Phase M 起）。个人仓库堆叠写在角色 `appearance_json.storage`，没有单独的数据库列（见 `PROTOCOL_NOTES.md`）。

快捷栏布局如果只存在客户端，重装客户端会丢；要跨端保持，应把槽位顺序放进角色存档。报文 **TBD**。在此之前，1–6 的默认映射由客户端按物品 id 约定，服务端只校验 `UseItem`。

自定义槽位编号、盾牌、首饰盒：**TBD**。

## 服务端模块与配置

物品表：`version-packs/sample-classic/configs/items.json`。

装备字段：`itemId`、`displayName`、`kind`（`equipment` / `consumable` / `material`）、`slot`、`paperdoll`、`atkBonus`、`defBonus`、`maxHpBonus`、`enhanceAtkPerLevel` 等、`requiredLevel`、`weight`。消耗品用 `healHp` / `healMp`。

样例槽位与矩阵：

| 矩阵 | slot | 说明 |
|------|------|------|
| E-01 | `weapon` | 武器，`paperdoll` 如 `wep_blade` |
| E-02 | `armor` | 衣服 |
| E-03 | `helmet` | 头盔 |
| E-04 | `necklace` | 项链 |
| E-05 | `bracelet`、`bracelet2` | 左右手镯 |
| E-06 | `ring`、`ring2` | 左右戒指 |
| E-07 | `boots` | 靴子 |
| E-08 | `belt` | 腰带 |
| E-09 | `torch` | 烛 / 火把 |

耐久上限 `PhaseMRules.DurabilityMax = 100`。耐久 0 时 `ApplyBroken` 把该件攻防血加成减半。负重公式见 [03](03-attributes-growth.md)。

修理与出售：`GameWorldModule` 中 Phase L 路径（3901 出售、3903 修理）。出售回收价对应矩阵 **Y-05**，商店表在 `configs/shops.json`。

行会仓库容量在 `configs/guild.json`：`warehouseCapacitySlots` 40，单格堆叠 999，职位能否存取清空见该文件。

## 客户端表现

- 背包键当前是 `B`（矩阵 **I-01 / V-16 / O-09**）。目标键位表写的是 F9。改绑定时以矩阵 18.1 为目标，并保留 `B` 直到目标键全部落地
- 人物面板 **V-17 / O-10**（F10）
- 仓库面板 **V-25**，行会仓库 UI 与 3601 对齐（**I-06**）
- 药品栏 **V-15 / O-15**
- 纸娃娃按 `paperdoll` 叠武器、衣服、头盔（`docs/VISUAL_PARITY.md`）
- 拖拽格子：Phase O 记录背包可拖拽。服务端仍以 Equip/Use/Storage 请求为准
- Unity 背包视图模型：`InventoryViewModel.cs`

地面物品名 **I-03**。品质色未作为物品必填字段；若要颜色，在 `items.json` 加可选 `quality`（`common`/`rare`/…），客户端映射色值。未加字段前不要按显示名猜颜色。

## 登录器相关

无。

## 配置热更与脚本钩子

钩子：`on_equip`、`on_use_item`、`on_enhance`。

热重载物品表不删除玩家背包里已有的 `itemId`。若 id 从新表消失，物品变成不可使用、不可装备，直到表恢复或 GM 回收。改 `weight` 后下次拾取和负重校验用新重量，已背在身上的总重在下一次背包通知时重算。

`on_equip` 不能取消已经成功的穿戴（服务端先判定再发事件）。要限制穿戴，改物品 `requiredLevel` 或服务端职业限制，不要依赖 Lua 抛错回滚，除非实现明确写了回滚。

## 验收标准

装备 E-01–E-12：槽位、耐久、修理、鉴定。E-13 强化见 [16](16-craft-enhance-gem.md)。E-14 套装同样见 16。

背包 I-01–I-04：列表、喝药、负重、地面名、快捷药。I-05 个人仓库 3905–3910。I-06 行会仓库 3601–3608。

## 非目标

- 不使用 StdMode、物品模式号或 0–100 的装备位编号作为协议。
- 不从第三方客户端读取物品图标。`paperdoll` 只索引原创图集。
- 仓库不是邮件。附件领取见 [13](13-chat-social.md)。

## 扩展路线

未来工作，未进入矩阵。

1. **盾牌**。新 `slot` 建议 `shield`，防御加成走现有 `defBonus`。外观层在纸娃娃上单独一帧。不要占用 `bracelet2`。
2. **首饰盒与生肖**。额外容器，报文可平行于个人仓库（新 MsgId **TBD**）或在 `StorageList` 增加 `container_id`。十二个生肖是十二个 `itemId`，加成用套装规则（[16](16-craft-enhance-gem.md)），不单独写十二套公式。
3. **自定义装备位**。版本包声明 `extraSlots[]`（id、显示名、是否可见）。服务端只接受声明过的 `slot`。客户端多出来的格子跟表走。不预留 71–100 这种数字空洞。
4. **绑定、禁止取下、拆捆**。物品字段 `bind`（`none`/`pickup`/`equip`）、`noUnequip`、`bundle`。交易与寄售读取 `bind`。现在没有这些字段，拆堆叠以 `InventoryNotify` 的 `count` 为准。
5. **装备对比与 Tips**。纯客户端：比较当前槽位与悬停物品的加成差值。数据来自已同步的物品模板。模板若未下发，客户端读随包发布的同一份 `items.json`，并在版本不一致时提示刷新。
6. **快捷栏存档**。六个药位写入角色状态，进世界时下发。MsgId **TBD**。
