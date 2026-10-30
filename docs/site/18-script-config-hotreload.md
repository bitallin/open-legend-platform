# 18 · 脚本、配置与热更

本页是版本包与 Lua 沙箱的规格。矩阵没有单独的脚本字母段；热重载属于 M19 Admin，验收脚本 `scripts/verify-m19.sh`。完成度不以「脚本命令数量」计分。

## 概述与玩家体验

玩家感觉不到沙箱。他们看到的是：公告、新手物品、活动是否按运营修改的表生效。运营修改 JSON 或 Lua 后，调用管理接口热重载，失败则世界仍用上一份成功的包。

脚本只能调用白名单，不能读磁盘、不能开网络、不能执行任意系统命令。

## 协议 / MsgId

热重载不是游戏报文。它是本机 Admin：

`POST /admin/reload-pack`，头 `Authorization: Bearer <token>`。

与脚本相关的游戏报文是 `ServerNotice`（3001）：`legend_api.send_welcome` 把标题和正文排进待发通知。发物品通过 `grant_starter_item` 填到通知上的 `GrantedItemId` / `GrantedItemCount`，由服务端在钩子返回后真正入包。

没有「执行一段 Lua」的客户端 MsgId，也不允许客户端上传脚本。

## 服务端模块与配置

实现：`server/Legend.Script/LuaScriptHost.cs`。

- 引擎：MoonSharp，`CoreModules.Preset_HardSandbox`
- 全局变量：只有 `legend_api`（`LegendLuaBridge`）
- 权限：`ScriptPermissionLevel` 为 `Read`、`GrantItem`、`Admin`。样例 Host 使用 `GrantItem`。低于该级别调用 `grant_starter_item` 会抛权限错误
- 桥接方法（仅此三个）：`send_welcome(title, content)`、`grant_starter_item(itemId, count)`、`log_info(message)`

加载器：`server/Legend.Core/VersionPackLoader.cs`，入口 `version-packs/sample-classic/manifest.json`。

manifest 引用地图、职业、技能、怪物、掉落、NPC、商店、任务、物品、等级、行会、交易、组队、Buff、攻城、邮件、副本、世界 Boss、坐骑、时装、宠物，以及 `scriptFiles` 列表。`protocolVersion` 必须与服一致（样例 `1.21.0`）。

已订阅并查找的钩子（函数不存在则跳过，异常记 `LuaSandbox` 日志，不应拖垮进程）：

| 钩子 | 时机 |
|------|------|
| `on_player_enter` | 进世界 |
| `on_kill` | 击杀 |
| `on_loot` | 拾取 |
| `on_talk` / `on_buy` | NPC、购买 |
| `on_quest_accept` / `on_quest_complete` | 任务 |
| `on_equip` / `on_enhance` / `on_use_item` | 装备与道具 |
| `on_level_up` | 升级 |
| `on_guild_create` / `on_guild_join` / `on_guild_leave` | 行会 |
| `on_guild_rank_change` | 任命 |
| `on_guild_warehouse_*` / `on_warehouse_audit` | 行会仓库 |
| `on_stall_list` / `on_stall_buy` / `on_stall_cancel` | 寄售 |
| `on_party_join` / `on_party_leave` | 组队 |
| `on_friend_add` / `on_whisper` | 社交 |
| `on_learn_skill` / `on_skill_level_up` | 技能 |
| `on_buff_apply` / `on_buff_expire` | Buff |
| `on_siege_declare` / `on_siege_capture` / `on_siege_end` | 攻城 |
| `on_mail_send` / `on_mail_claim` | 邮件 |
| `on_dungeon_enter` / `on_dungeon_complete` / `on_dungeon_leave` / `on_dungeon_party_enter` | 副本 |
| `on_world_boss_spawn` / `on_world_boss_kill` | 世界 Boss |
| `on_rare_loot_broadcast` | 稀有掉落 |
| `on_mount` / `on_fashion_equip` | 外观 |
| `on_pet_summon` / `on_pet_dismiss` | 宠物 |

参数顺序以 `LuaScriptHost` 里 `fn.Call` 的实参为准。写脚本前打开该文件核对，不要凭第三方手册猜参数。

### 概念对照表

调研材料里的服务端文本结构，只用来核对「数据有没有家」。OLP 的家是版本包。下表不是格式说明，也不能拿第三方文件替换 JSON。

| 概念（仅种类） | OLP 位置 |
|----------------|----------|
| 启动与开关 | `manifest.json`、Host 命令行、`realms/*.json` |
| 文案、公告 | `ServerNotice`、Lua `send_welcome`、任务 `title/description` |
| 经验与等级 | `configs/levels.json` |
| 地图、阻挡、安全区、传送 | `maps/*.json` |
| 刷怪 | `configs/monsters.json` 的 `spawns`，副本在 `dungeons.json` |
| 掉落 | `configs/loot_tables.json` |
| 技能、物品、商店、NPC、任务 | 同名 `configs/*.json` |
| 行会与城堡 | `guild.json`、`siege.json` |
| 定时或机器人 | **尚未提供**。见扩展路线 |
| 管理命令 | Admin HTTP，见 [21](21-ops-gm-deploy.md)。没有命令文本文件 |

公开的 `cfg_*` 字段目录只证明「等级、技能、怪物、物品、掉落、商店」这些种类需要配置。字段名以本仓库 JSON 为准，见样例文件。

第三方 Lua 书（服务端书、客户端书、新客户端 GUI 书、新服务端书）若用来整理「进世界、击杀、定时、界面」有哪些**时机**，对应到上表钩子或 [19](19-client-ui-controls.md) 的界面。不把书中的函数名注册进 `legend_api`。

## 客户端表现

客户端不跑权威 Lua。表现由报文驱动。若以后做客户端界面脚本，必须是另一套只读 API，不能发奖、不能改坐标。当前没有这套 API。

资源热更与规则热更分离，见 [01](01-account-login-launcher.md)。

## 登录器相关

登录器的 `IUpdateChannel` 更新客户端文件，不调用 `reload-pack`。运维在服务器上对 Admin 端口热重载。

## 配置热更与脚本钩子

`reload-pack` 成功的定义：新 manifest 路径可读、JSON 通过加载器校验、Lua 能在沙箱里加载。任一失败则回滚到上一份包，并在 HTTP 响应里说明失败，世界保持旧规则。

重载期间正在结算的请求应跑完旧逻辑或整体失败重试，不能半次用旧技能表、半次用新技能表。

脚本运行时异常捕获后记日志，该钩子的奖励可以丢失，但玩家的移动和战斗循环必须继续。`grant_starter_item` 的权限错误同样不能打崩 Host。

## 验收标准

- `scripts/verify-m19.sh`：`reload-pack` 成功路径与错误路径回滚
- 样例 `scripts/welcome.lua` 能在进世界时发出通知（与 M 系列测试一致即可）
- 沙箱：脚本内不能调用未注入的全局 IO。新增桥接方法必须改 `LegendLuaBridge` 并补测试
- 各功能页列出的 `on_*` 与本表一致。新增钩子时两处一起改

## 非目标

- 不提供 QFunction、面板命令、检测命令或任何第三方命令兼容层。
- 不把 ShowDoc 或社区 `cfg_*` 表导入仓库。
- 不让 Lua 直接执行 SQL 或改 `MsgId`。
- Admin 不是给玩家用的 GM 聊天指令。玩家可见的 GM 见 [21](21-ops-gm-deploy.md) 扩展。

## 扩展路线

未来工作，未进入矩阵。

1. **定时脚本**。Host 内调度器按秒触发 `on_timer(id)`，id 来自 `configs/schedules.json`。没有文件就不触发。禁止 `while true` 卡死沙箱；钩子有执行时间上限。
2. **只读查询**。若脚本需要读等级，增加 `legend_api.get_level()` 这类明确方法，返回值拷贝。不把角色对象整个塞进 Lua。
3. **客户端布局数据**。JSON 描述面板，见 [19](19-client-ui-controls.md)。这不是元变量系统。
4. **更多发奖 API**。在 `grant_starter_item` 之外增加 `grant_item(characterId, itemId, count)` 前，先定权限与审计日志。
5. **配置校验器**。E0 起，地图与物品在 `VersionPackLoader` 里按 JSON Schema 子集校验，Host 启动失败即停，热更失败回滚。计划见仓库 `docs/ENGINE_REFACTOR_PLAN.md`。`tools/Legend.MapValidate` 仍做地图语义检查。技能、任务、掉落 id、NPC id 的交叉引用在后续里程碑补齐，不在进世界后才暴露空引用。
