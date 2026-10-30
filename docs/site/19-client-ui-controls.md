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

未来工作，未进入矩阵。

1. **挂机**。客户端状态机：选怪、走近、放 F1、捡取、回城药。每一步仍发正常报文，频率受 [22](22-security-anti-cheat.md) 限制。服务端不提供「开始挂机」特权。设置里可关闭。
2. **快速拾取**。按住拾取键时按冷却连续 `PickupReq`，见 [06](06-monsters-loot.md)。
3. **红点**。见 [14](14-buff-title-fashion.md)，只派生已有数据。
4. **H5 或内嵌页**。仅用于公告和支付跳转（打开 `checkout_url`）。游戏操作不放进网页。
5. **布局描述**。若要做编辑器，产出 JSON：控件 id、锚点、默认是否可见。客户端内置经典布局为默认主题。编辑器是开发工具，概念见 [21](21-ops-gm-deploy.md)，不读取第三方界面工程。
6. **键位自定义**。V-31 的按键页把 18.1 的动作 id 映射到键。禁止把「攻击」映射成能跳过 `force` 规则的服务端开关。
