# Phase K — 可玩 PC 竖切（画面 / 声音 / 操作 / 登录器进游戏）

协议仍为 **1.21.0**（本阶段未改游戏协议）。美术与音效均为 **程序生成占位或 CC0/自绘路径**，**禁止**导入官方传奇 / 996 闭源资源包。

## 和「VerifyClient 验收包」的区别

| 包 | 是什么 | 能否「真玩」 |
|----|--------|--------------|
| `legend-trial-windows-x64.zip`（旧） | Avalonia 登录器 + **协议验收客户端**（控制台 Handshake/Login） | **不能**当传奇客户端玩 |
| `dist/windows-player/LegendClient.exe`（本阶段） | Unity **带画面**玩家包 | 登录器选区后进世界移动战斗 |

本机执行器环境 **没有 Unity Editor**，仓库只落地工程改动与打包入口；**真实 Win 玩家包需在你装有 Unity 2022.3 的 Windows 上打出**。

## 本阶段已落地

### 画面

- 地面：**格子棋盘纹理**，安全区（默认 `novice_plain` 5–18）偏绿，野外偏暗
- 实体：彩色胶囊/方块 + **朝向鼻尖** + 名称板 + HP 条 + 选中环
- 飘血加粗；进点地移动有绿色落点标记
- HUD：HP / **MP 占位条**、等级金币坐标、**小地图占位**、聊天、背包、PC 操作提示
- 登录页：全屏暗底 + 金色标题条

### 声音

- `LegendAudioService`：登录 BGM、进世界 BGM、攻击 / 受击 / 拾取 / UI 点击
- 素材：`Assets/LegendClient/Audio/*.wav`（正弦生成占位）；可按 [`Audio/README.md`](../client/unity-mvp/Assets/LegendClient/Audio/README.md) 替换

### PC 操作（传奇习惯）

| 输入 | 作用 |
|------|------|
| WASD / 方向键 | 移动（优先于点地） |
| 鼠标左键点地 | 自动走格子 |
| 左键点实体 | 选中；点掉落自动拾取 |
| 右键 / Space / J | 攻击 |
| Tab | 切换怪物目标 |
| B | 开关背包 |
| Esc | 关背包 / 取消聊天焦点 |
| Enter | 聚焦聊天或发送 |
| Z / G | 拾取 |
| 虚拟摇杆 | **仅移动端**；Standalone 隐藏 |

### 登录器 → 进游戏

1. Unity 打出 `LegendClient.exe` 到 `dist/windows-player/`
2. Avalonia 登录器 `clientExecutable` 指向该 exe（见 `launcher/config/launcher.playable.example.json`）
3. 选区「自研先锋一区」→ 启动游戏；参数：`--host {host} --port {port} --realm-id {realmId}`
4. 客户端 `LegendClientBootstrap` 解析上述参数并填登录表单默认主机

公网试玩服：`150.158.164.90:7100`（区服示例见 `realms.public.example.json`）。

## 用户机：用 Unity 2022.3 打 Windows 包（精确命令）

Unity Hub 打开工程：`legend-platform/client/unity-mvp`（工程版本写的是 2022.3.20f1；你本机 `2022.3.62f3c1` 一般可直接升/开）。

**第一次：**

1. 运行 `client/unity-mvp/sync-sdk.sh`（若有）
2. 菜单 **Legend → Create MVP Boot Scene**（生成 `Assets/LegendClient/Scenes/Boot.unity` 并写入 Build Settings）
3. 菜单 **Legend → Build → Windows Player**  
   或批处理：

```bat
REM 将 UNITY_EDITOR 换成你的编辑器路径（中国版示例）
set UNITY_EDITOR=C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe
set LEGEND_WINDOWS_OUTPUT=%CD%\dist\windows-player
"%UNITY_EDITOR%" -batchmode -nographics -quit -projectPath "%CD%\client\unity-mvp" -executeMethod Legend.Build.Windows.BuildPlayer -logFile "%LEGEND_WINDOWS_OUTPUT%\unity-windows-build.log"
```

Linux/mac 包装脚本：

```bash
export UNITY_EDITOR="/path/to/Unity"
./scripts/build-windows-player.sh
```

成功后应有：`dist/windows-player/LegendClient.exe`。

## 用 Avalonia 登录器启动

```bat
cd launcher
copy config\launcher.playable.example.json config\launcher.json
REM 若 exe 不在相对路径，编辑 launcher.json 的 clientExecutable 为绝对路径
dotnet run --project Legend.Launcher.Desktop -c Release
```

窗口：刷新区服 → 选「自研先锋一区」→ **启动游戏**。登录器不阻塞等待 Unity 退出。

也可直接：

```bat
dist\windows-player\LegendClient.exe --host 150.158.164.90 --port 7100 --realm-id realm-classic-01
```

## 无 Unity 验收（骨架）

```bash
./scripts/verify-phase-k-playable.sh
```

## 「能玩一局」清单：已有 vs 缺

| 能力 | 状态 | 说明 |
|------|------|------|
| 登录 / 注册 | ✅ 已有 | 客户端 + Host |
| 选角 / 创角 | ✅ 已有 | |
| 进图 / AOI | ✅ 已有 | |
| 移动同步 | ✅ 已有 | + 点地走格子 |
| 打怪 / 飘血 | ✅ 已有 | 安全区禁止攻击 |
| 掉落 / 拾取 | ✅ 已有 | |
| 聊天 | ✅ 已有 | 世界 / 附近 |
| 死亡回城 | ✅ 服务端已有 | Host 复活到出生点；客户端 toast |
| 背包 / 金币 | ✅ 已有 | HUD 面板 |
| 商店 / 任务 / 行会 / 坐骑时装 | ⚠️ 协议层有 | **P1** 客户端面板未做完 |
| 技能栏 / 快捷键宏 | ❌ 缺 | **P1** |
| 精美 2D 精灵图 / 地图 | ❌ 占位 | **P1** 自绘/CC0 替换 |
| 真实 MP 同步到 HUD | ⚠️ 半缺 | EnterWorld 有 EffectiveMaxMp，快照无 Mp；**小改**可接到 SDK |
| iOS / 完整安卓真包 | ❌ / 骨架 | 见 Phase J |
| 热更新 CDN | ❌ | 登录器仅骨架 |

### 本轮小改（已做）

- 点地移动、Tab 切目标、PC 隐藏摇杆、音频、瓦片地面、Windows `BuildPlayer`、登录器 GUI 启动 `.exe`

### P1（下一轮）

1. 把 `EffectiveMaxMp` / 当前 Mp 接到 `LegendUnityNetworkClient` 与 HUD  
2. 技能按钮与 CD  
3. 自绘/CC0 精灵与地图块替换程序纹理  
4. 用户机打出 `LegendClient.exe` 后用登录器联公网服做完整验收录像

## 验证

```bash
./scripts/verify-phase-k-playable.sh
# 可选：登录器单测
cd launcher && dotnet test Legend.Launcher.Tests -c Release --nologo
```
