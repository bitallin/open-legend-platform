# 06 · 怪物与掉落

完成度以矩阵 **K-01–K-08、C-10**（击杀）为准。怪物、刷点、掉落都在版本包 JSON。

## 概述与玩家体验

怪物按模板刷新。追击型会仇恨并走近，驻守型留在原地反击。精英和世界 Boss 有独立状态：顶栏能看到是否出现、血量和仇恨前列。死亡后按掉落表在地面生成物品，归属和拾取由服务端判定。挖肉（Alt）是另一种拾取，产出配置里的肉类物品。

新手被引导从城镇到野外再到洞窟。样例地图名见 [04](04-map-movement.md) 与 [经典对照](classic-name-map.md)。

## 协议 / MsgId

| MsgId | 消息 | 说明 |
|-------|------|------|
| 2104 / 2105 | `EntitySpawn` / `EntityDespawn` | 怪物出现与消失。`EntityType` 含 `ENTITY_MONSTER` |
| 2106 | `LootDropNotify` | 地面物品 |
| 2107 / 2108 | `PickupReq` / `PickupResp` | 拾取。拒绝 `ERR_LOOT_DENIED (1017)` |
| 2109 | `InventoryNotify` | 背包变化 |
| 3301 / 3302 | `WorldBossStatusReq` / `WorldBossStatusResp` | 查询 |
| 3303 | `WorldBossSpawnNotify` | 刷新 |
| 3304 | `WorldBossHpNotify` | 血量与仇恨 |
| 3305 / 3306 | `WorldBossControlReq` / `WorldBossControlResp` | 管理向控制（测试或 GM） |
| 3307 | `LootBroadcastNotify` | 稀有掉落广播 |
| 3963 / 3964 | `MineReq` / `MineResp` | 挖矿，与挖肉不同 |

怪物对话气泡、名字顶戴图标、自定义击杀榜报文：**TBD**。

## 服务端模块与配置

- 世界与 AI 循环：`server/Legend.Game/GameWorldModule.cs`
- 世界 Boss：`GameWorldModule.WorldBoss.cs`
- 模板与刷点：`version-packs/sample-classic/configs/monsters.json` 的 `templates` 与 `spawns`
- 掉落：`configs/loot_tables.json`
- Boss 参数：`configs/world_boss.json`
- 副本内刷点：`configs/dungeons.json` 的 `spawns`

模板字段（以 `slime_green` 为例）：`monsterId`、`displayName`、`maxHp`、`atk`、`def`、`aggroRange`、`attackRange`、`attackCooldownMs`、`lootTableId`、`respawnMs`、`ai`、`expReward`。

`ai` 当前使用：

| 值 | 行为 | 矩阵 |
|----|------|------|
| `chase` | 进入仇恨后追击 | K-01 |
| `stationary` | 驻守 | K-02 |

世界 Boss 与精英用独立配置和 3300 段报文（**K-03**），不要只靠把 `maxHp` 调大冒充 Boss 状态条。

掉落表 id 挂在怪物上。地面物品有归属规则时，非归属拾取返回 `ERR_LOOT_DENIED`。负重不足同样不能拾取（[03](03-attributes-growth.md)）。

挖肉产物在物品表，例如 `item_dried_meat`（显示名「风干肉干」）。Alt 拾取是输入（**K-06 / O-06**），合法性在服务端。

钩子：`on_kill`、`on_loot`、`on_rare_loot_broadcast`、`on_world_boss_spawn`、`on_world_boss_kill`。

## 客户端表现

- 怪物帧：原创图集中的史莱姆、野猪、骷髅、爬虫、幽灵、绿龙等（`docs/VISUAL_PARITY.md`）
- 世界 Boss 血条与仇恨前五：Playable 顶栏轮询 `WorldBossStatus` 并听 `WorldBossHpNotify`（矩阵 K-03 说明）
- 地面名字：**I-03**。品质色若未画，属于表现缺口，掉落数据仍然有效
- 拾取键：Z / G 以及点地拾取（**O-19、O-01**）
- 仇恨音与死亡音：**K-08 / U-08**
- Unity 实体视图：`LegendEntityView.cs`，伤害数字：`LegendDamagePopup.cs`

## 登录器相关

无。

## 配置热更与脚本钩子

重载怪物表后，未刷新的尸体与地面物品保持旧掉落直到被拾取或超时。新刷出的怪使用新表。删除 `monsterId` 时现存实体应被移除并 `EntityDespawn`，避免客户端留下不可攻击的残影。

`respawnMs` 只影响之后的刷新计时。把 `ai` 从 `chase` 改成 `stationary` 对已经在追的怪，下一次仇恨判定生效即可。

脚本可以在 `on_kill` 里发公告。掉落内容以 `loot_tables.json` 为准，Lua 不要再掷一次平行掉落，以免双倍物品。稀有广播走 `on_rare_loot_broadcast` 与 3307。

## 验收标准

| 矩阵 ID | 规格要点 |
|---------|----------|
| K-01 | `ai=chase` 追击 |
| K-02 | `ai=stationary` 驻守 |
| K-03 | 世界 Boss 状态、血条、仇恨前五 |
| K-04 | `loot_tables.json` |
| K-05 | 地面物品与拾取 |
| K-06 | Alt 挖肉 |
| K-07 | 曙光平原 → 幽影林 → 史莱姆洞窟。矩阵说明仍提醒缺少更长的「镇→兽穴→骨洞」连续带，地图文件 `marsh_den` 与 `bone_path` 已在包内，串路线时应改传送而不是改矩阵状态表 |
| K-08 | 怪物仇恨音与死亡音 |

## 非目标

- 不解析第三方刷怪文本（坐标一行一只怪的那种格式）。刷点写在 `monsters.json` 的 `spawns`。
- 不把爆率做成客户端可改的本地文件。
- 掉落归属不能由拾取请求里的自报字段决定。

## 扩展路线

未来工作，未进入矩阵。

1. **掉落数量上限与绑定**。掉落表增加 `maxPerDrop`、`bindOnPickup`。绑定物品在交易、寄售、个人仓库的拒绝规则见 [07](07-equipment-inventory.md) 与 [11](11-trade-stall-auction.md)。现在没有绑定字段，不要用物品名字前缀模拟。
2. **怪物说话与顶戴**。模板可选 `bark` 字符串数组与 `overheadId`。说话只是 `ServerNotice` 或实体气泡（气泡报文 **TBD**）。顶戴是客户端图标 id，引用原创图集。
3. **人形怪**。使用与玩家相同的纸娃娃分层，AI 仍是 `chase` 或以后的 `scripted`。不要占用玩家 `character_id`。
4. **怪物大血条**。世界 Boss 已有顶栏。普通精英若要大血条，用模板标记 `bossBar: true`，客户端复用 K-03 的 UI，血量仍来自实体快照，不新增 MsgId，除非仇恨列表也要展示。
5. **快速范围拾取**。客户端可以连续发 `PickupReq`，每次一件，受 `ERR_RATE_LIMITED (1006)` 约束。不提供「真空吸取」的服务端瞬移物品。
6. **击杀排行**。统计放服务端，展示接口见 [21](21-ops-gm-deploy.md)，不写进怪物 AI。
