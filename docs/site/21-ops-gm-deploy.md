# 21 · 运维、部署与 GM

Admin 在线、封禁、禁言、攻城开关、热重载是 M19。部署文档是 `docs/TRIAL_DEPLOY.md` 与 `docs/PUBLIC_DEPLOY_BUDGET.md`。矩阵没有单独的运维字母段。本页不把 Admin 的存在写成玩法 Done。

## 概述与玩家体验

玩家只间接感受到：服务器在跑、被封禁无法登录、被禁言无法聊天、攻城可以被管理员打开、公告会出现。管理员不使用游戏内的隐藏对话框执行系统命令；管理面是带令牌的 HTTP，默认绑在本机。

试玩与公网部署按预算文档选择机器。单区至少：游戏端口、Admin 端口、数据库。双区是两个进程。

## 协议 / MsgId

管理接口不走游戏帧。

| 方法 | 路径 | 作用 |
|------|------|------|
| GET | `/admin/online` | 在线摘要 |
| POST | `/admin/ban` | 封禁，登录返回 `ERR_BANNED` |
| POST | `/admin/mute` | 禁言，聊天 `ERR_MUTED` |
| POST | `/admin/siege` | `{"action":"start"}` 或 `stop` |
| POST | `/admin/reload-pack` | 热重载版本包 |
| POST | `/admin/recharge` | 管理直充 |
| POST | `/admin/payment/*` | 支付回调，见 [12](12-mall-payment.md) |

认证：`Authorization: Bearer`，令牌来自 `--admin-token` / 环境变量。`--admin-bind` 使用 `127.0.0.1`，除非运维网单独隔离。

游戏内 GM 聊天命令（踢人、拉人、发物品）**TBD**。在设计前，这些操作走 Admin 或测试客户端 `server/Legend.CliClient`，不走玩家聊天。

## 服务端模块与配置

- HTTP：`server/Legend.Host/AdminHttpServer.cs`
- 进程：`server/Legend.Host/Program.cs`、`LegendServerEngine.cs`
- 容器：`ops/docker-compose.yml`（Postgres 16、Redis 7、legend-host）、`ops/Dockerfile.host`
- 安装脚本：`scripts/install-legend-host-on-server.sh`、`scripts/pack-linux-host.sh`
- 开区：`scripts/create-realm.sh`
- 存储环境变量：`LEGEND_STORAGE`（`InMemory` / `Sqlite` / `Postgres`）、`LEGEND_SQLITE_PATH`、`LEGEND_PG` 或 `LEGEND_DB_CONN`、`LEGEND_REDIS` 或 `LEGEND_REDIS_CONN`

日志：服务端使用结构化日志（脚本侧频道名 `LuaSandbox`）。进程标准输出由部署环境收集。没有必须模仿的「按日期一个目录」格式；若要落盘，约定写在部署文档，路径不放进版本包。

验收：`scripts/verify-m19.sh`。支付与多区脚本见 README，不在此重复命令表。

## 客户端表现

客户端展示封禁与禁言的错误文案，不提供 Admin 按钮。Unity SDK 在 1.19.0 说明里包含 mute/ban 同步，仍以 `ERR_BANNED` / `ERR_MUTED` 为准。

在线人数若要给玩家看，用系统公告或后续的只读查询。现在 `GET /admin/online` 不对玩家开放。

## 登录器相关

登录器探测的是游戏端口的 TCP 连通性，不是 Admin 端口。区服 JSON 不包含管理地址。更新失败时不要启动半截客户端（扩展见 [01](01-account-login-launcher.md)）。

## 配置热更与脚本钩子

热重载规格在 [18](18-script-config-hotreload.md)。Admin 改封禁后立即生效，并写入 Sqlite/Postgres，重启仍在。

脚本没有权限调用 Admin。`ScriptPermissionLevel.Admin` 这个枚举值不等于 HTTP 管理权，不要在 Lua 里假设可以封禁玩家，除非桥接方法明确存在（当前不存在）。

## 验收标准

M19 行为（README 手动验收与 `verify-m19.sh`）：

1. 带令牌访问 `/admin/online`。
2. 玩家在线时摘要计数增加。
3. mute 后聊天被拒；ban 后无法登录。
4. siege start/stop 改变攻城状态。
5. reload-pack 成功；坏路径回滚。
6. 重启后 ban/mute 仍在。

部署文档：`docs/TRIAL_DEPLOY.md`、`docs/PUBLIC_DEPLOY_BUDGET.md`。容器能拉起数据库与 Host 即部署骨架通过；公网地址与预算不在规格里写死。

## 非目标

- 不实现第三方 GM 命令文件或 `@` 管理命令兼容。
- 不把 Admin 端口暴露在登录器区服列表。
- 不在文档中写生产口令、证书或机器登录方式。

## 扩展路线

未来工作，未进入矩阵。

1. **GM 命令表**。若要做游戏内 GM，用版本包 `configs/gm_ranks.json` 把角色 id 映射到权限，聊天以 `/gm` 前缀进入单独解析器，与玩家 `/拜师` 分开。每条命令对应已有 Admin 能力或明确的新用例。默认关闭。
2. **审计**。封禁、直充、热重载记操作者、时间和参数。与行会仓库审计分开存储。
3. **通区只读**。聚合多区 `/admin/online`，见 [17](17-cross-realm-merge.md)。凭证用运维令牌，不复用玩家会话。
4. **数据上报**。在线、收入、崩溃计数打到运营自建收集端。上报字段自定义，不套用第三方消息格式。默认关闭，避免试玩环境外连。
5. **自定义排行榜**。服务端按等级、金币或击杀生成 Top N，玩家用 NPC 或面板只读查询。MsgId **TBD**。榜数据不能由客户端提交。
6. **工具概念**。客户端修复（校验文件哈希）、打开日志目录、界面布局编辑器，都是独立工具。编辑器产出 [19](19-client-ui-controls.md) 的布局 JSON。不移植第三方中心或 GUI 工程。
