# 10 · PK 与红名

完成度以矩阵 **P-01–P-08、C-09、C-12、C-13、V-12** 为准。攻击模式是服务端状态，客户端切换只发请求。

## 概述与玩家体验

默认和平：普通攻击只打怪。Shift 强制攻击可以打到人并计入 PK。玩家用 Ctrl+H 在和平、善恶、组队、行会、全体之间循环。善恶只主动打红名。组队模式不打队友。行会模式不打会友。全体可打任意玩家。安全区里这些模式都不能出手。

名字颜色随 PK 计数变化：白、黄、红。死亡掉金币和物品的规则见 [03](03-attributes-growth.md)；攻城进行中豁免。

## 协议 / MsgId

| MsgId | 消息 | 说明 |
|-------|------|------|
| 3921 / 3922 | `SetAttackModeReq` / `SetAttackModeResp` | 设置模式 |
| 3923 | `PkSyncNotify` | 同步 PK 与名字色 |
| 2101 | `AttackReq.force` | 强制攻击 |

`EntitySnapshot.name_color` 给 AOI 内其他人看色。模式字符串由 `PhaseMRules.NormalizeMode` 接受中英文：

| 规范值 | 也接受 | 谁可以被打（非强制） |
|--------|--------|----------------------|
| `peace` | 和平 | 不打玩家 |
| `good_evil` | 善恶、`goodevil` | 仅 `pkKills >= 3` 的目标 |
| `group` | 组队、`party` | 非同队 |
| `guild` | 行会 | 非同行会 |
| `all` | 全体 | 任意玩家 |

非法字符串规范化后为空，`SetAttackMode` 失败，错误码目前复用 `ERR_INVALID_SKILL`，角色保持原模式。默认存档值是 `peace`（`GridAoiManager` 与角色状态）。

`PhaseMRules.NameColor`：`pkKills >= 3` 为 `red`，`>= 1` 为 `yellow`，否则 `white`。常量 `RedNameKills = 3`。

安全区由战斗调用方先拒绝，错误码 `ERR_SAFE_ZONE (1015)`。强制攻击也不能绕过安全区。`AllowsPlayerTarget` 在强制时返回 true，仅表示模式允许，不表示格子允许。

红名村传送、PK 值衰减报文：**TBD**。当前没有「每分钟减一点 PK」的字段说明；若代码里已有衰减，以 `GameWorldModule.PhaseM.cs` 为准并补进 PROTOCOL_NOTES。

## 服务端模块与配置

- 规则纯函数：`server/Legend.Contracts/PhaseMRules.cs`
- 处理：`server/Legend.Game/GameWorldModule.PhaseM.cs`
- 安全区矩形：各地图 JSON 的 `safeZones`
- 死亡掉落豁免：攻城进行中，配置见 `configs/siege.json` 与 C-12

PK 计数存在角色状态，随 Sqlite/Postgres 保存。换图不清空。

## 客户端表现

- Ctrl+H 循环顺序：和平 → 善恶 → 组队 → 行会 → 全体（矩阵 18.1）
- HUD 攻击模式文字 **V-21 / P-08**
- 名字颜色 **V-12 / P-06**，使用快照里的 `name_color`，不要用本地击杀猜测
- Shift+左键 **C-09 / O-03**
- 安全区点击攻击应得到失败提示，而不是本地播攻击动画后无结果

## 登录器相关

无。

## 配置热更与脚本钩子

模式与颜色阈值目前是代码常量（3 次变红）。若要运营可调，再把 `redNameKills` 放进版本包（建议 `configs/pk.json`，**文件尚未作为 manifest 字段**，属扩展）。在文件落地前，热重载不改变红名阈值。

没有 `on_pk` 钩子。击杀玩家若需要公告，走现有 `on_kill` 时要能区分玩家与怪物；若钩子参数只有怪物 id，则玩家 PK 公告 **TBD**，不要在 Lua 里扫全图。

## 验收标准

| 矩阵 ID | 规格要点 |
|---------|----------|
| P-01 | 默认 `peace`，Shift 强制打人才计 PK |
| P-02 | `good_evil` 只打红名 |
| P-03 | `group` 不打队友 |
| P-04 | `guild` 不打会友 |
| P-05 | `all` |
| P-06 | 白 / 黄 / 红，阈值 1 与 3 |
| P-07 | 地图 `safeZones` + `ERR_SAFE_ZONE` |
| P-08 | Ctrl+H |
| C-09 | `AttackReq.force` |
| C-13 | 安全区禁攻 |
| C-12 | 死亡掉落；攻城中不掉 |
| V-12 | 名字颜色渲染 |

## 非目标

- 不把 PK 值同步成第三方颜色码或行会战开关。
- 客户端上报的「我是善恶模式」在服务端未确认前不影响判定。
- 不在安全区做例外伤害（包括火墙残留）。地面效果进安全区应不结算，见 `GroundEffectNotify` 的服务端过滤。

## 扩展路线

未来工作，未进入矩阵。

1. **红名村 / 监狱**。`pkKills` 达到阈值时，死亡复活点改为地图上的 `jail` 出生点，而不是主城 `spawn`。离开条件：在线时间或 NPC 交金。地图用原创 id，例如在样例包新增 `red_yard`。MsgId 不需要新号，复用复活落点逻辑。
2. **灰名与正当防卫**。被攻击后的短暂反击窗口不增加 PK。需要服务端记录「最近被谁攻击」。字段 **TBD**。
3. **PK 衰减**。在线每 N 秒减少 1 点，广播 `PkSyncNotify`。N 放 `configs/pk.json`。
4. **行会战期间免 PK**。只在 [09](09-guild-siege.md) 的行会战或攻城状态机声明的地图和时间内生效，不全球关闭红名。
