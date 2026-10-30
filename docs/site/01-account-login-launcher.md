# 01 · 账号、登录与登录器

完成度以矩阵 **A-01、A-02、A-03** 为准。本页写会话怎么建立、区服从哪来、登录器拉起哪一个客户端。

## 概述与玩家体验

玩家在登录器里看到区服名称、状态和 TCP 延迟，选定后启动游戏。游戏内用账号注册或登录，拿到会话令牌，再进入角色流程（见 [02](02-character-class.md)）。被封禁的账号在登录阶段失败。登录器本身不处理创角和进世界。

公告可以在进入世界前后由服务端推送。样例脚本用欢迎钩子发一条 `ServerNotice`。

## 协议 / MsgId

均已在 `MsgId` 枚举中：

| MsgId | 消息 | 说明 |
|-------|------|------|
| 1001 / 1002 | `HandshakeReq` / `HandshakeResp` | 客户端带版本字符串。不兼容则 `ERR_VERSION_MISMATCH` |
| 1009 / 1010 | `RegisterReq` / `RegisterResp` | 用户名占用：`ERR_USERNAME_TAKEN (1011)` |
| 1003 / 1004 | `LoginReq` / `LoginResp` | 失败：`ERR_AUTH_FAILED`。封禁：`ERR_BANNED (1079)` |
| 1005 / 1006 | `RealmListReq` / `RealmListResp` | 返回 `realms/catalog.json` 中的区服。本区端口为实际监听端口 |
| 1007 / 1008 | `PingReq` / `PongResp` | 游戏连接保活。登录器延迟另走 TCP connect，不复用这条 |
| 3001 | `ServerNotice` | 系统公告。Lua `legend_api.send_welcome` 会排队一条 `NoticeType=LUA_HOOK` |

令牌字段在 `LoginResp`。后续业务报文靠连接上的会话，不在每条玩法请求里重传密码。无效会话：`ERR_INVALID_TOKEN (1003)`。区服不可用：`ERR_REALM_UNAVAILABLE (1004)`。

实名认证结果、设备指纹、渠道号：**TBD**，现有 proto 无对应字段。

## 服务端模块与配置

- 登录逻辑：`server/Legend.Login/LoginModule.cs`
- 账号与口令：`server/Legend.Storage/IAccountStore.cs`、`PasswordHasher.cs`。Sqlite / Postgres / 内存三套实现都在 `Legend.Storage`
- 会话：`RedisSessionStore.cs`（配置了 Redis 时）
- 封禁持久化：`ModerationStores.cs`。Admin `POST /admin/ban` 写入后，重启 Host（Sqlite/Postgres）仍然有效
- 区服文件：`realms/catalog.json` 列出 `realm-classic-01.json`、`realm-classic-02.json`。开新区用 `scripts/create-realm.sh`
- 每区 manifest 里的 `realmId` / `realmName` 与区服配置对齐。样例包 `versionId` 为 `sample-classic-1.21.0`

Host 绑定游戏端口与 Admin 端口分开。Admin 必须 `--admin-bind` 到本机或运维网，并带 `--admin-token`。

## 客户端表现

Playable 登录页在 `client/Legend.Playable/Ui/GameWindow.cs`，城市场景绘制见 `Ui/TownBackdrop.cs` 与图集（矩阵 **V-01**）。启动参数由 `LaunchOptions.cs` 解析：`--host`、`--port`、`--realm-id`。

会话阶段机：`Session/PlayableSession.cs`。Unity 侧对应 `client/unity-mvp/Assets/LegendClient/Scripts/ClientCore/LegendSessionFsm.cs` 与 `Session/LegendSessionService.cs`。

登录成功后的界面是角色列表，不是直接进图。

## 登录器相关

工程：`launcher/Legend.Launcher.sln`。

| 项目 | 职责 |
|------|------|
| `Legend.Launcher.Core` | `LauncherFacade`、`RealmCatalogService`、`LatencyProbe`、`UpdateChecker`、`IUpdateChannel`、`ClientProcessLauncher` |
| `Legend.Launcher` | 控制台菜单与 `--realm-id` |
| `Legend.Launcher.Desktop` | Avalonia 窗口（Phase I） |
| `Legend.ClientStub` | 无头占位，验收用 |

配置：

- `launcher/config/launcher.json`：`realmsPath`、`realmListUrl`、`versionPath`、`updateManifestPath`、`clientExecutable`、`clientArgsTemplate`、`downloadCacheDir`、`probeTimeoutMs`
- `launcher/config/realms.json`：公开区服字段。不要写入 `adminToken` 或支付密钥
- `launcher/config/version.json` 与 `update-manifest.sample.json`

区服列表 URL 支持 http(s)、file 与本地路径，与本地 JSON 合并。没有真实包地址时，下载器写假进度到 `cache/updates/update-*.bin`，这是骨架。

默认参数模板：`--host {host} --port {port} --realm-id {realmId}`。验收客户端是发布出来的 `LegendClient.exe`，也可以指向 Unity 玩家或 `scripts/fake-client.sh`。

验收脚本：`scripts/verify-phase-h-launcher.sh`、`scripts/verify-phase-i-launcher-gui.sh`。操作细节保持在 `docs/LAUNCHER.md`，本页不重复按键步骤。

## 配置热更与脚本钩子

登录与注册不走 Lua 判定，避免脚本把认证绕开。欢迎公告走 `on_player_enter` → `legend_api.send_welcome`（见样例 `version-packs/sample-classic/scripts/welcome.lua`）。

换区服列表或更新清单不需要 reload-pack：登录器下次读取 JSON 或 URL 即可。游戏内规则热更见 [18](18-script-config-hotreload.md)。

## 验收标准

| 矩阵 ID | 规格要点 |
|---------|----------|
| A-01 | 无账号时先注册再登录。客户端登录页提供注册 |
| A-02 | 登录签发会话。错误口令失败，封禁账号返回 `ERR_BANNED` |
| A-03 | 登录器选区；游戏进程带 `--realm-id`。游戏窗能看到区服 id |

相关画面：**V-01**。Admin 在线与封禁属于 M19，见 [21](21-ops-gm-deploy.md)。

## 非目标

- 登录器不实现游戏内协议，也不伪装成第三方登录网关。
- 不在登录器配置存放管理令牌。
- 不把更新通道做成第三方客户端的静默补丁格式。

## 扩展路线

以下均为未来工作，未进入矩阵，不得标 Done。

1. **登录公告文件**。现在只有在线 `ServerNotice`。若运营要「维护公告」独立于进世界钩子，增加版本包内 `notices.json`（或区服 JSON 字段）并由 Login 在 `LoginResp` 之后下发。报文若需新字段，先记 PROTOCOL_NOTES，MsgId **TBD**。
2. **禁止登录名单**。账号封禁已有。IP、设备、机器码名单没有。设计上走 Admin 写入存储，在 `LoginModule` 拒绝，错误码可复用 `ERR_BANNED` 或新增码（**TBD**）。
3. **实名认证窗口**。客户端表单 + 服务端记录证件哈希。不在协议里发明字段，直到产品确定校验方（自建或外部）。玩法服不保存明文证件号。
4. **静默下载与资源热更**。`IUpdateChannel` 已预留。下一步是：清单含文件哈希与大小、断点续传、校验失败不启动游戏。与版本包热重载是两条线：登录器更新客户端二进制与图集，Host `reload-pack` 更新规则。
5. **渠道包 / 推广包**。登录器配置增加可选 `channelId`，注册请求将来携带（proto **TBD**）。服务端只存储渠道，不回传给其他玩家。
6. **客户端工具、中心式启动器、界面编辑器**。只保留产品概念：启动器可以挂「修复客户端」「打开日志目录」。不移植第三方工具的工程格式。GUI 编辑器若做，产出 OLP 自己的布局描述，见 [19](19-client-ui-controls.md)。
