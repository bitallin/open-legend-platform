# 04 · 地图与移动

完成度以矩阵 **C-01、C-02、G-01–G-11、R-05** 为准。地图是原创 JSON，不是官方地图文件。

## 概述与玩家体验

玩家在格子上走或跑。点地、键盘都能移动。阻挡格进不去。安全区内不能发起攻击。走到传送点切换地图。回城卷把角色送回绑定出生点。世界地图传送走独立请求。挖矿在允许的格子上消耗一次操作并给资源。

样例主城带安全区与商人，野外和洞窟用传送点串起来。小地图用色点表示自己与周边，大地图与世界地图是单独面板（键位以 [19](19-client-ui-controls.md) 为准）。昼夜是可选的客户端色调，不改变碰撞。地图 flag、苍月类岛屿占位与镜像细则在扩展路线，样例图不必先加这些字段。

## 协议 / MsgId

| MsgId | 消息 | 说明 |
|-------|------|------|
| 2003 / 2004 | `MoveReq` / `MoveSync` | 走格。阻挡 `ERR_MOVE_BLOCKED (1005)`。过快 `ERR_MOVE_TOO_FAST (1073)` |
| 2005 / 2006 | `EntityEnterAoi` / `EntityLeaveAoi` | 进入或离开视野 |
| 2007 | `MapChangeNotify` | 传送、回城、副本进出 |
| 2001 / 2002 | `EnterWorldReq` / `EnterWorldResp` | 首次进图，出生点来自地图 `spawnX/spawnY` |
| 3961 / 3962 | `WorldTravelReq` / `WorldTravelResp` | 世界地图传送 |
| 3963 / 3964 | `MineReq` / `MineResp` | 挖矿 |
| 2107 / 2108 | `PickupReq` / `PickupResp` | 拾取，见 [06](06-monsters-loot.md) |

死亡复活回出生点由服务端完成（**C-11**），客户端收切图或位置同步。没有单独的「复活按钮」MsgId；若产品要确认框，客户端本地 UI 再发一次已有的回城或进世界请求，不要新造一套坐标协议让客户端指定落点。

随机传送卷：**矩阵 G-07 说明为回城卷已有、随机卷没有**。随机卷目标点算法 **TBD**，在物品 `use` 效果里由服务端选安全格，客户端不上传目标坐标。

## 服务端模块与配置

加载：`server/Legend.Core/VersionPackLoader.cs`。移动与 AOI：`server/Legend.Game/GameWorldModule.cs`、`GridAoiManager.cs`。

样例地图（`version-packs/sample-classic/maps/`）：

| 文件 | mapId | 名称 |
|------|-------|------|
| `novice_plain.json` | `novice_plain` | 曙光镇外 |
| `dark_forest.json` | `dark_forest` | 幽影林 |
| `dungeon_slime_cave.json` | `dungeon_slime_cave` | 史莱姆洞窟 |
| `marsh_den.json` | `marsh_den` | 沼窟外廊 |
| `bone_path.json` | `bone_path` | 骨脊小径 |
| `oath_city.json` | `oath_city` | 盟誓城 |
| `sand_gate.json` | `sand_gate` | 沙门关 |
| `sealed_temple.json` | `sealed_temple` | 封碑神殿 |

`novice_plain.json` 的字段即地图格式：`width/height`、`aoiCellSize`、`aoiRadiusCells`、`spawnX/spawnY`、`blockedCells`、`safeZones`（矩形 `x1,y1,x2,y2`）、`portals`（`portalId`、格点、`targetMapId`、`targetX/Y`）。

manifest 的 `defaultMapFile` 与 `mapFiles` 决定加载集合。副本实例配置在 `configs/dungeons.json`（进入报文 3201），与大世界地图文件并存。

跑：矩阵 **C-02** 要求更快的移动手感。坐骑间隔见 `configs/mounts.json` 的 `defaultMoveIntervalMs` 与 `moveSpeedBonusPercent`。服务端用 `ERR_MOVE_TOO_FAST` 拒绝超间隔的步子，客户端动画不能反过来逼服务端放行。

安全区禁攻在战斗模块检查，错误码 `ERR_SAFE_ZONE (1015)`，见 [10](10-pk-redname.md)。

校验工具：`tools/Legend.MapValidate`。改地图后应能跑通该工具（阻挡越界、传送指向不存在的地图要失败）。

## 客户端表现

- 画布：`client/Legend.Playable/Ui/WorldCanvas.cs`
- 格子与斜视角：`World/GridMath.cs`、`World/IsoProjection.cs`。点地先换成菱形砖下的格子，再发 `MoveReq`。镜头是斜 45°，碰撞仍是正交格子
- 地图目录缓存：`World/MapCatalog.cs`
- 小地图 **G-08 / V-19**：色点
- 昼夜 **G-11 / V-05**：客户端色调开关，不写进移动报文
- Unity：`LegendWorldPresenter.cs`、`LegendEntityView.cs`、`LegendMvpInput.cs`

走/跑动画帧数见 `docs/VISUAL_PARITY.md`。右键点地在目标键位里是跑（**O-02**）；若当前构建把右键用作攻击，以矩阵说明与 `Input/ClassicInput.cs` 的实际绑定为准，改键时同时改 19 的键位表。

## 登录器相关

无。地图在版本包里，随游戏客户端与 Host 的 manifest 发行，不由登录器选择地图。

## 配置热更与脚本钩子

`reload-pack` 可替换地图与传送。规格：玩家若站在被删除的地图上，重载应把其放到 `defaultMapFile` 出生点并 `MapChangeNotify`，而不是断开连接。传送脚本不使用第三方地图事件语言。需要「踩点触发任务」时，在服务端移动成功后发内部事件，再调用已有 `on_*` 或新增明确命名的 `on_map_enter`（**钩子 TBD**，现在没有这个函数名）。

副本钩子已有：`on_dungeon_enter`、`on_dungeon_complete`、`on_dungeon_leave`、`on_dungeon_party_enter`。

## 验收标准

| 矩阵 ID | 规格要点 |
|---------|----------|
| C-01 | 走格：WASD、方向键、左键点地 |
| C-02 | 跑比走更快，并受服务端间隔约束 |
| G-01 | `novice_plain` 安全区 + 商人 |
| G-02 | `oath_city` 作为土城对照 |
| G-03 | `sealed_temple` 作为封魔对照 |
| G-04 | `dark_forest` 野外 |
| G-05 | `dungeon_slime_cave` 副本模板 |
| G-06 | `portals` 切图 |
| G-07 | 回城卷可用；随机卷见上文缺口 |
| G-08 | 小地图色点 |
| G-09 | 大地图（键位与 Tab 切目标的分工见 19） |
| G-10 | 世界地图与 3961 |
| G-11 | 昼夜可选 |
| R-05 | 挖矿 3963 |

新手路线的连续地带在 **K-07**（[06](06-monsters-loot.md)）。

## 非目标

- 不读取官方地图、不转换第三方地图编辑器工程。
- 客户端不能指定任意坐标完成传送。传送点 id 与世界地图目的地由服务端表决定。
- 不把镜像地图做成第三方「复制地图」命令的兼容层。

## 扩展路线

未来工作，未进入矩阵。

1. **地图事件**。进入矩形、击杀计数、限时关闭。配置建议 `maps/*.json` 增加 `events` 数组（id、矩形、动作类型）。动作只能调用 OLP 已有系统：发公告、传送到表内目的地、接任务。新 Lua 钩子名在落地时写入 [18](18-script-config-hotreload.md)，禁止执行任意字符串命令。
2. **镜像副本**。每个队伍一份地图实例。创建：进入时服务端分配实例 id，复制源图的阻挡与传送。销毁：超时、全员离开或完成条件满足后销毁，并把仍在实例内的角色踢回入口地图（`MapChangeNotify`）。客户端仍使用同一 `mapId` 的美术与阻挡。实例 id 只在服务端，可选透传字段供调试，不作为客户端选图参数。报文可扩展 `DungeonEnter` 的实例字段；若现有 3201 已能表达队伍副本，优先复用，不新号段。
3. **挂机寻路**。自动走动属于客户端辅助。完整状态机见 [19](19-client-ui-controls.md)。`noHang` 是地图侧总闸：为 true 时，开始辅助会提示并拒绝维持状态机。服务端仍逐格校验移动与攻击，并可对异常频率记日志。服务端不接受「直线瞬移到怪」。
4. **随机卷与记忆石**。物品使用效果 `recall` / `random` 写在 `items.json`，落点算法在服务端。
5. **光照**。烛槽见 [07](07-equipment-inventory.md)。地图级昼夜已有。动态光源是客户端表现，不新增 MsgId。
6. **地图 flags**。在地图 JSON 增加 `mapFlags` 布尔表。未写的键视为 false。安全区矩形仍然有效，flag 是额外限制。未进矩阵。

| 键 | 为 true 时 |
|----|------------|
| `noPk` | 禁止玩家互相攻击。安全区仍返回 `ERR_SAFE_ZONE` |
| `noMount` | 禁止骑乘。已骑乘进入时服务端下马，见 [15](15-pet-hero-mount.md) |
| `noSummon` | 禁止道具宠与技能召唤兽 |
| `noTeleport` | 禁止回城卷、随机卷与世界地图传送。地图内 `portals` 是否豁免由该传送点自己的字段决定，默认传送点仍可用 |
| `noTrade` | 禁止面对面交易与寄售 |
| `instance` | 该图作为镜像或副本的源图，不直接当大世界出生图 |
| `noHang` | 禁止客户端辅助挂机。开始时提示并拒绝维持状态机。服务端可对异常频率记日志。移动与攻击校验保持原阈值 |
| `noAutoPickup` | 可选。禁止辅助连续拾取。玩家手动点地拾取仍然允许 |

7. **岛屿对照**。经典「苍月岛一类」只在 [经典名称对照](classic-name-map.md) 占位 `mapId` `moon_isle`。样例包可以不落文件。真要进包时再写地图 JSON，并服从 `mapFlags`。攻城与刷怪仍见 [09](09-guild-siege.md)、[06](06-monsters-loot.md)，不在本条新造规则。
