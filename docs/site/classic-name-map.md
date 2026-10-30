# 经典名称对照

显示名可以对齐玩家熟悉的说法。**资源 id、地图文件、协议字段保持原创。** 本表不是官方地图或官方技能的文件清单。

## 职业

| 经典说法 | OLP `classId` | 显示名 | 配置 |
|----------|---------------|--------|------|
| 战士 | `warrior` | 武者 | `version-packs/sample-classic/configs/classes.json` |
| 法师 | `mage` | 术士 | 同上 |
| 道士 | `taoist` | 灵师 | 同上 |

## 地图（sample-classic）

| 经典对照（仅说明） | `mapId` | `mapName` |
|--------------------|---------|-----------|
| 新手主城 / 比奇一类安全城 | `novice_plain` | 曙光镇外 |
| 野外 | `dark_forest` | 幽影林 |
| 副本洞窟 | `dungeon_slime_cave` | 史莱姆洞窟 |
| 兽穴入口 | `marsh_den` | 沼窟外廊 |
| 骨洞路径 | `bone_path` | 骨脊小径 |
| 土城 / 盟重一类 | `oath_city` | 盟誓城 |
| 沙巴克城门 | `sand_gate` | 沙门关 |
| 封魔一类 | `sealed_temple` | 封碑神殿 |
| 苍月岛一类 | `moon_isle` | 文档占位，样例包不落文件 |

样例攻城占领区当前写在 `dark_forest` 的矩形上，城门怪 id 为 `castle_gate`。调整玩法循环时改 `configs/siege.json`，不要去找官方城堡目录。`moon_isle` 只在文档中占位，`version-packs/sample-classic/maps/` 不必添加该文件。地图 flag 与镜像见 [04](04-map-movement.md)。

## 技能显示名

显示名在 `configs/skills.json`。id 与显示名的完整列表见 [05 · 战斗与技能](05-combat-skills.md)。矩阵早期说明里 `arcane_bolt` 曾写成「奥术飞弹」，现行配置显示名是「火球术」。以 JSON 为准。

## 装备槽

经典槽位与 OLP `slot` 字符串的对应见 [07](07-equipment-inventory.md)。左右手镯、左右戒指在样例里是 `bracelet` / `bracelet2`、`ring` / `ring2`，不是数字位 0–100。

## 经济与货币

| 经典说法 | OLP id | 说明 |
|----------|--------|------|
| 金币 | `gold` | 样例内唯一启用的流通货币。矩阵 **Y-01** |
| 元宝一类显示名 | `ingot` | 扩展货币。显示名可叫元宝，id 保持 `ingot`。样例包未启用 |
| 绑定金币 / 绑定元宝 | `gold_bound` / `ingot_bound` | 绑定后缀 `_bound`。同组扣款优先，通常不可交易 |

支付商品 `gold_100`、`gold_1000` 在现网表示发 `gold`，不是第二种货币 id。多货币账本与充值改发 `ingot` 的规格见 [11](11-trade-stall-auction.md) 与 [12](12-mall-payment.md)，未进入矩阵。
