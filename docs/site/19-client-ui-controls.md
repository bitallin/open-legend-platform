# 19 · 客户端 UI 与操作

完成度以矩阵 **V-01–V-31、O-01–O-19** 为准。目标键位以矩阵 **18.1** 为生产标准。经典布局加上少量打磨（斜视角、金边底栏、分层纸娃娃），见 `docs/VISUAL_PARITY.md` 与 `docs/PLAYABLE_PC.md`。

## 概述与玩家体验

主界面是经典传奇式底栏：血蓝、经验、技能格、聊天、小地图、功能按钮。窗口覆盖背包、人物、技能、NPC、商店、仓库、交易、行会、组队、任务和死亡。登录与选角有独立画面，不使用世界 HUD。

Playable（Avalonia）是验收客户端。Unity MVP 是第二条路径，协议相同，面板可以分阶段追上，但不能发明另一套操作语义。

## 协议 / MsgId

UI 不单独占号段。每个面板绑定前面各篇的报文，例如背包用 2109 与 2401，交易用 3941 起。本页不重复全表。

客户端发给服务端的应是玩家意图（走哪一格、对谁放哪个 `skillId`、打开哪一个 `npcId`），不是「我已经把血量扣了」。

界面布局同步、元变量、H5 页面容器：**TBD**。

## 服务端模块与配置

服务端没有 UI 模块。与界面有关的只是：

- 进世界快照里的等级、外观、称号、模式
- 各 `*Notify` 推动面板刷新
- 版本包 JSON 作为图标与名字的数据源（客户端可携带同一份只读表，冲突时以服务端回包的 id 为准）

## 客户端表现

### Playable（验收）

工程 `client/Legend.Playable`。

| 区域 | 代码 |
|------|------|
| 入口与参数 | `Program.cs`、`LaunchOptions.cs` |
| 会话 | `Session/PlayableSession.cs` 及 Phase 分部 |
| 网络 | `Net/PlayableNetClient.cs` 及 Phase 分部 |
| 主窗与面板 | `Ui/GameWindow.cs`、`Ui/GameWindow.Panels.cs` |
| 底栏与框体 | `Ui/MirChrome.cs`、`Ui/MirSheet.cs` |
| 世界 | `Ui/WorldCanvas.cs`、`World/IsoProjection.cs`、`World/GridMath.cs` |
| 输入 | `Input/ClassicInput.cs` |
| 登录城景 | `Ui/TownBackdrop.cs` |
| 选角立绘 | `Ui/ClassPortrait.cs` |

发布：`scripts/publish-playable-windows.bat`（及 `.sh`）。冒烟：`scripts/verify-playable-client.sh`。

### Unity MVP

| 区域 | 代码 |
|------|------|
| 网络 | `client/unity-sdk/Runtime/LegendUnityNetworkClient.cs` |
| 引导 | `Assets/LegendClient/Scripts/Bootstrap/LegendClientBootstrap.cs` |
| HUD | `Scripts/UI/LegendHudController.cs`、`LegendUiBuilder.cs` |
| 虚拟摇杆 | `LegendVirtualJoystickUi.cs`（触屏辅助，不替换 PC 键位） |
| 输入 | `Scripts/Input/LegendMvpInput.cs` |
| 战斗飘字 | `Scripts/Combat/LegendDamagePopup.cs` |

Android 导出见 `docs/ANDROID_BUILD.md`。无 Editor 的环境只验收脚本骨架 `scripts/verify-phase-j-android.sh`。

### 面板与矩阵

| 面板 | 矩阵 | 键（18.1 目标） |
|------|------|-----------------|
| 登录 | V-01 | 登录器拉起后的第一屏 |
| 选角创角 | V-02 | |
| 背包 | V-16 / I-01 | F9（当前构建常用 `B`） |
| 人物 | V-17 | F10 |
| 技能页 | V-18 | F11 |
| 小地图 | V-19 / G-08 | 常驻 |
| 聊天 | V-20 | Enter |
| 攻击模式 | V-21 | Ctrl+H |
| 行会组队交易设置 | V-22 | 底栏按钮 |
| NPC / 商店 / 仓库 / 交易 / 行会 / 组队 / 任务 / 死亡 | V-23–V-30 | 交互打开，Esc 关闭 |
| 设置 | V-31 | 画质、音量、按键 |

HUD **V-13–V-15**：生命魔法经验负重等级、F1–F8、药品 1–6。

### 键位

生产目标（矩阵 18.1）：

| 输入 | 作用 |
|------|------|
| 左键点地 / 点怪 / 点掉落 / 点 NPC | 走、选中攻击、拾取、对话 |
| Shift+左键 | 强制攻击 |
| 右键点地 / 点单位 | 跑 / 查看 |
| Ctrl+左键 / Ctrl+右键 | 跑 / 查看 |
| Alt+点击 | 挖取 |
| 双击 | 拾取或使用 |
| WASD、方向键 | 走；Shift 为跑 |
| F1–F8 | 技能 |
| 1–6 | 药品 |
| F9 / F10 / F11 | 背包 / 人物 / 技能 |
| Tab | 大地图 |
| `` ` `` | 切换怪物目标 |
| Ctrl+H | 攻击模式循环 |
| Alt+X | 回选角（3971） |
| Enter / Esc | 聊天 / 关面板 |
| `!` `!!` `!~` `@` | 频道前缀 |

矩阵说明里部分键仍是过渡绑定（右键攻击、Tab 切目标、背包用 B）。改 `ClassicInput.cs` 时向 18.1 收敛，并保留 Space / J 攻击、Z / G 拾取作为辅助（**O-19**），不替代经典键。

## 登录器相关

登录器窗口是 Avalonia 另一工程（`Legend.Launcher.Desktop`），视觉可以接近游戏登录页，但不要复用游戏内 HUD。它只负责选区和启动。游戏内设置（音量、键位）存在客户端本地，不写进 `launcher.json`。

## 配置热更与脚本钩子

规则热更后，打开着的面板在下一次 Notify 时刷新。不要求客户端重新解释 Lua。

键位与画质存在本机。重装客户端可以恢复默认键，不能因此踢下线。

## 验收标准

V-01–V-31 与 O-01–O-19 的 Status 以矩阵为准。改 UI 时逐条点：空背包、满负重、死亡界面、Esc 关闭、Alt+X 不断线。

可玩路径说明保持在 `docs/PLAYABLE_PC.md`。本页改了键位语义就要回写该短文的「操作」段，避免两套说明。

## 非目标

- 不移植第三方 GUI 编辑器工程、元变量或 H5 引擎 API。
- 不把界面脚本做成能发物品的第二套服务端。
- 触屏摇杆只服务 Unity 移动端，不改变 PC 验收键位。

## 扩展路线

未来工作，未进入矩阵，不计分。下列内挂规格是客户端输入辅助，默认关闭。

### 扩展 · 客户端内挂（Assist / AutoPlay）——矩阵外

内挂是客户端上的输入辅助状态机。每一步只发送已经存在的正常报文：移动 `MoveReq`（2003）、攻击 `AttackReq`（2101）、拾取 `PickupReq`（2107）、使用物品 `UseItemReq`（2401，含药水与回城卷 `item_town_scroll`）。服务端按手动操作的同一套距离、冷却、MP、归属、负重和速率规则逐包裁决。服务端不提供「开始挂机」特权通道，也不因为客户端自称正在辅助而放宽校验。

设置默认关闭，玩家可以随时关掉。版本包下发默认值，玩家覆盖存在本机；若以后要跟账号走，同步字段 **TBD**，不新造特权号段。

建议配置 `configs/client_assist.json`（随版本包下发默认值）：

| 字段 | 含义 |
|------|------|
| `enabled` | 总开关。默认 false |
| `hotkey` | 开关键。动作 id 走现有自定义键（本页键位条），文档只给示例，不新占矩阵键位 |

页签字段见下面各节。正式数值以该文件为准；本页的三职业顺序是与 [05](05-combat-skills.md) 技能 id 对齐的建议，实现时允许被配置覆盖。

边界：第三方内存修改、加速倍率、真空吸物、伪造无冷却，以及召唤「拾取精灵」实体，都不在本规格内。过滤与范围连续拾取见 [06](06-monsters-loot.md)。

#### 1. 基础 / 显示

能映射到现有界面的，沿用现有开关；不能映射的标为客户端本地，不发新报文。

| 项 | 落点 |
|----|------|
| 怪物显名、地面物品显名 | 客户端本地显示开关。名字数据仍来自实体快照与 `LootDropNotify`（2106） |
| 拒绝交易请求 | 沿用交易面板已有拒绝。无单独「挂机拒交」报文 |
| 内挂按钮 | 主界面位置可配。扩展未开放时按钮灰色或不显示，且不发送未知 MsgId |

#### 2. 挂机寻路与自动打怪

| 字段 | 含义 |
|------|------|
| `mode` | `anchor`（定点半径）或 `patrol`（路径点）。只有这两种 |
| `anchorRadiusCells` | `anchor` 时相对锚点的半径（格） |
| `patrolPoints[]` | `patrol` 时的格坐标列表 |
| `pointSpacingHint` | 路径点间距提示，仅客户端插值，服务端仍逐格走 `MoveReq` |
| `searchRangeCells` | 搜怪半径 |
| `maxPullCount` | 同时拉怪数量上限。超过则不再主动接近新目标 |
| `ignoreTags[]` | 不打的怪物标签。怪物模板扩展字段 `tags[]`（字符串）。示例：`guard`、`archer_dummy`、`town_npc`。不使用种族数字当协议 |
| `doNotAttackEntityKinds[]` | 不打的实体种类：`pet_own`、`hero_own`、`friendly`。自身宝宝与英雄见 [15](15-pet-hero-mount.md) |
| `stopOnPortal` | 踩到传送点时停止辅助 |
| `stopOnMapChange` | 切图后停止 |
| `stopOnDeath` | 死亡后停止，不在复活后自动恢复，除非玩家再次打开 |
| `stopOnManualMove` | 玩家手动点地或按移动键时停止。可配置 |

状态机（每步仍是普通报文，服务端逐包校验）：

1. 在 `searchRangeCells` 内选怪。跳过 `ignoreTags` 与 `doNotAttackEntityKinds`。
2. 用 `MoveReq` 走近到所选技能的 `range` 内。超出距离的攻击由服务端拒绝（`ERR_OUT_OF_RANGE`）。
3. 按技能页的旋转施放（见下一节）。冷却与 MP 以服务端为准。
4. 范围内无怪则沿 `patrol` 走到下一点，或在 `anchor` 内等待。
5. 地面有可捡物品且物品页允许时，进入拾取子状态；`pauseAttackWhilePicking` 为真时暂停攻击。

地图 `noHang` 为 true 时，客户端在点开始时提示并停掉状态机，不得维持自动打怪。`noAutoPickup` 只停辅助连拾，手动点地拾取仍走 2107。两面旗标见 [04](04-map-movement.md)。速率见 [22](22-security-anti-cheat.md)。

断线重连后是否恢复辅助，只读本机设置。重连本身仍是正常登录进世界，不带「继续挂机」特权位。

#### 3. 技能页 · 自动施法

技能页可以在不开启寻路时单独使用（站桩按条件施法）。寻路开启时，打怪步骤调用同一张表。

`skills[]` 每项：

| 字段 | 含义 |
|------|------|
| `skillId` | 必须是 [05](05-combat-skills.md) 技能表已有 id。未学会的跳过 |
| `enabled` | 是否参与旋转 |
| `priority` | 整数。越大越先尝试 |
| `minMpPercent` | 当前 MP 百分比低于此值则不放 |
| `minHpPercent` | 用于治愈一类：自身 HP 百分比低于此值才放。不需要该门槛的技能省略 |
| `minMobCount` | 周围怪数达到此值才放（群攻）。单体技能省略或为 1 |
| `requireBuffMissing` | 可选。对应 buff 不在身上时才放（魔法盾、神圣战甲、幽灵盾） |

`globalCastGapMs`：客户端两次尝试之间的节流。它不能短于服务端冷却，也不能当成无冷却连打。真正的冷却、MP、距离、安全区由 `AttackResp` 决定（`ERR_SKILL_COOLDOWN`、`ERR_INVALID_SKILL` 等）。客户端收到拒绝就跳过该技能，不重发同一拍。

三职业默认旋转（建议表；`client_assist.json` 可覆盖。id 均来自 05 已有技能）：

| 职业 | 建议顺序 | 备注 |
|------|----------|------|
| `warrior` | `thrust_slash` → `fatal_strike` → `half_moon` | 刺杀类近战优先。烈火类在冷却就绪时插入。半月在怪数 ≥ `minMobCount` 时优先于单体。未学会则跳过，退回 `basic_slash` 普攻 |
| `mage` | `mana_shield` → `fire_wall` → `ice_roar` → `lightning` | 盾缺失或即将结束时优先补盾（`requireBuffMissing`）。火墙是地面效果（3967）。冰咆哮看怪数。单体用 `lightning`；只学会 `arcane_bolt` 时由配置换成它 |
| `taoist` | `summon_bone` → `poison_charm` → `soul_charm` → `heal` → `holy_armor` | 没有自身召唤兽时先召唤。已学 `summon_beast` 时可由配置替换 `summon_bone`。施毒见下一节。火符打当前目标。自身 HP 低于 `minHpPercent` 时放 `heal`。`holy_armor` / `ghost_shield` 在对应 buff 缺失时补 |

合击：`comboPriority` 取 `off` 或 `low`。英雄未开放时不显示合击槽，也不发送合击请求（见 05、15）。`low` 仅在英雄 `deployed` 且双方就绪时，排在职业旋转之后尝试。

#### 4. 自动施毒（道士）

只对已学会 `poison_charm` 的角色生效。红绿两种 Debuff 的 id 见 [05](05-combat-skills.md) 与 [14](14-buff-title-fashion.md)。样例包未拆分前，自动施毒仍对单一 `debuff_poison` 工作；拆分配置存在时才分别判断红与绿。

| 字段 | 含义 |
|------|------|
| `poison.enabled` | 道士自动施毒总开关 |
| `poison.applyRed` | 是否施加红毒（持续掉血，`debuff_poison_red`） |
| `poison.applyGreen` | 是否施加绿毒（降低防御，`debuff_poison_green`） |
| `poison.reapplyBeforeExpireMs` | 目标身上该 Debuff 剩余时间短于此值时补一次 |
| `poison.onlyIfTargetMissingDebuff` | 为真时，目标已有对应 Debuff 则不重复施放 |
| `poison.consumesItemId` | 可选。若版本包把施毒做成消耗物品，写物品 id；省略则只扣技能 MP，不另扣物品 |

群怪时是否施毒，用技能项的 `minMobCount`。MP 低于 `minMpPercent` 时停手。不把第三方毒符的显示名写进协议。

#### 5. 物品页 · 自动拾取

| 字段 | 含义 |
|------|------|
| `pickup.enabled` | 辅助拾取总开关 |
| `pickup.pickupRangeCells` | 客户端扫描地面物品的半径（格）。只决定扫哪些实体、按什么顺序发 `PickupReq` |
| `pickup.delayMs` | 两次拾取请求的客户端间隔 |
| `pickup.pauseAttackWhilePicking` | 拾取子状态期间是否暂停攻击 |
| `pickup.priorityItemIds[]` | 优先捡的物品 id，排在过滤表 `priority` 之前 |

过滤表在 [06](06-monsters-loot.md) 的 `configs/pickup_filters.json`。客户端本地可以再收紧（少捡），不能放宽服务端已经拒绝的项（归属、负重、距离、`noAutoPickup`）。

满包或超重：停止拾取。可选动作是使用回城卷，或在区服开启回收时转入下一节的自动回收。服务端仍返回现有拒绝码，客户端不得改成「吸进背包」。

每次请求一件。连续请求受 `ERR_RATE_LIMITED (1006)` 约束。禁止真空吸物（把远处物品瞬移进包）。禁止生成「拾取精灵」实体；该能力对标的是过滤加连续拾取，见 [经典名称对照](classic-name-map.md)。

#### 6. 保护页

用药与回城都走 `UseItemReq`（2401）。阈值在客户端判断，扣血扣蓝与传送结果以服务端回包为准。

| 字段 | 含义 |
|------|------|
| `hpPotions[]` | 若干档。每档含 HP 百分比阈值、`itemId`、优先级、最短间隔。样例药品 id：`item_hp_potion` |
| `mpPotions[]` | 同上，针对 MP。样例 id：`item_mp_potion` |
| `emergencyScrollHpPercent` | HP 低于此百分比时使用回城卷 |
| `emergencyScrollItemId` | 回城卷物品 id。样例为 `item_town_scroll` |
| `repair.durabilityPercent` | 可选。任一已装备耐久低于此百分比时尝试修理 |
| `repair.intervalMs` | 可选。定时检查耐久 |

修理：附近有修理 NPC 时走现有 `RepairReq`（3903）。没有可修理的协议路径（不在 NPC 旁、或该槽尚未支持）时，只回城，不发明远程修理报文。生肖格修理见 [07](07-equipment-inventory.md)。

#### 7. 与回收

`autoRecycleWhenSlotsBelow`：背包空格少于该数时，若区服开启 [11](11-trade-stall-auction.md) 的 `recycle.json`，客户端按该表发起回收（入口仍是 NPC 或已有回收请求，校验含日限与货币）。区服未开启回收时，只提示回城，不在本页另造经济或经验。回收可附带的 `grantExp` 也只写在 11。

#### 8. UI 与协议边界

内挂按钮放在主界面，位置由布局 JSON 配置（见下方布局条）。扩展未开放时按钮灰色或不显示。

协议：无新的特权包。若观战或检测需要知道「是否正在辅助」，只增加一个状态位，MsgId **TBD**。该位不授予权限，服务端不因该位放宽移动、攻击、拾取或用药。

离线挂机是另一项运营扩展，默认关闭，写在 [21](21-ops-gm-deploy.md) 与 [22](22-security-anti-cheat.md)。本状态机不在断线后继续替玩家行动。

矩阵占位（DocOnly，不写入矩阵计分表，不计分；与聊天篇矩阵 **H-01–H-08** 不是同一套编号）：

| 占位 | 含义 |
|------|------|
| H-01 | 客户端内挂状态机（寻怪、打、捡、停） |
| H-02 | 三职业技能循环配置 |
| H-03 | 自动施毒（红 / 绿） |
| H-04 | 保护阈值（药 / 回城） |

### 其余条目

原「挂机」「快速拾取」两条已升为上一节。下列顺延。

1. **红点**。见 [14](14-buff-title-fashion.md)，只派生已有数据。
2. **H5 或内嵌页**。仅用于公告和支付跳转（打开 `checkout_url`）。游戏操作不放进网页。
3. **布局描述**。若要做编辑器，产出 JSON：控件 id、锚点、默认是否可见。客户端内置经典布局为默认主题。编辑器是开发工具，概念见 [21](21-ops-gm-deploy.md)，不读取第三方界面工程。内挂按钮是其中一个控件 id。
4. **键位自定义**。V-31 的按键页把 18.1 的动作 id 映射到键。攻击映射不能变成跳过 `force` 规则的服务端开关。内挂热键只切换本机 `enabled`，不发送特权报文。
