# 可玩 PC 客户端（非 Unity 为验收路径）

协议仍为 **1.21.0**（本阶段未改游戏协议）。画面是程序绘制的格子与色块，声音是程序生成的 WAV，**禁止**导入官方传奇 / 996 闭源资源包。

不需要 Unity Editor，也不需要 Godot。在装有 .NET 8 SDK 的 Windows 上一条命令打出 `LegendClient.exe`，再用 Avalonia 登录器选区启动。

## 和「VerifyClient 验收包」的区别

| 包 | 是什么 | 能否「真玩」 |
|----|--------|--------------|
| `legend-trial-windows-x64.zip`（旧） | Avalonia 登录器 + **协议验收客户端**（控制台 Handshake/Login） | **不能**当传奇客户端玩 |
| `dist/windows-player/LegendClient.exe`（**本路径，验收**） | .NET 8 Avalonia **带画面**玩家包，`dotnet publish` 生成 | 登录器选区后进世界移动、打怪、拾取、聊天 |
| 同路径的 Unity 包（可选） | 旧的 Unity 玩家工程，本机没有 Editor 时打不出来 | 能玩，但不再是验收前置 |

## 一条命令打出 Windows 包

在仓库根目录（需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)）：

```bat
scripts\publish-playable-windows.bat
```

或：

```bash
./scripts/publish-playable-windows.sh
```

等价命令：

```bat
dotnet publish client\Legend.Playable\Legend.Playable.csproj -c Release -r win-x64 --self-contained true -o dist\windows-player
```

成功后可执行文件是：

`dist\windows-player\LegendClient.exe`

这是自包含 win-x64 目录（exe 旁边有运行时文件，请整目录拷贝）。目标机不必再装 .NET。

## 直接进公网试玩服

```bat
dist\windows-player\LegendClient.exe --host 150.158.164.90 --port 7100 --realm-id realm-classic-01
```

不带参数时，登录页默认也是这台主机、端口 `7100`、区服 `realm-classic-01`。窗口里填写账号和密码（没有账号会先注册再登录），选角或创角（武者 / 术士 / 灵师）后进入地图。

## 登录器选区再进游戏

```bat
cd launcher
copy config\launcher.playable.example.json config\launcher.json
dotnet run --project Legend.Launcher.Desktop -c Release
```

`launcher.playable.example.json` 的 `clientExecutable` 指向 `../dist/windows-player/LegendClient.exe`，区服列表是 `realms.public.example.json`（自研先锋一区，`150.158.164.90:7100`）。

窗口：刷新区服 → 选「自研先锋一区」→ **启动游戏**。登录器传入：

`--host {host} --port {port} --realm-id {realmId}`

登录器不等待游戏进程退出。

## 能玩一局：操作

| 输入 | 作用 |
|------|------|
| WASD / 方向键 | 走格子（优先于点地） |
| 鼠标左键点地 | 自动走向那一格（绿色落点） |
| 左键点怪物 | 选中；够不着会先走过去再攻击 |
| 左键点掉落 | 走近并拾取 |
| 左键点 NPC | 范围内打开对话（商店购买界面仍是 P1） |
| 右键 / Space / J | 攻击当前目标，否则打最近的怪 |
| Tab | 切换怪物目标 |
| B | 开关背包（点击物品尝试使用） |
| Z / G | 拾取 |
| Enter | 聚焦聊天或发送（世界 / 附近） |
| Esc | 关闭背包并离开聊天框 |

### 画面

- 地面：棋盘格。安全区偏绿，野外偏暗，阻挡格接近黑色，传送点青色
- 角色 / 怪物 / NPC / 掉落：色块 + 朝向线 + 名字 + HP 条 + 选中环
- 飘字：伤害 / MISS / 治疗
- HUD：HP、MP（进世界时用角色 MP 与 `EffectiveMaxMp` 占位，攻击回包与升级会改当前 MP）、等级金币坐标、小地图、聊天、背包

### 声音

程序生成的正弦 WAV（登录 BGM、世界 BGM、攻击、受击、拾取、按钮）。Windows 上用 waveOut 播放；没有官方音效包。

## 本机验收（无 Unity、可无显示器）

```bash
./scripts/verify-playable-client.sh
```

脚本会 `dotnet test`、跑 `LegendClient --smoke`，并 `dotnet publish -r win-x64 --self-contained`。只想编译、不下载 Windows 运行时：

```bash
SKIP_WIN_PUBLISH=1 ./scripts/verify-playable-client.sh
```

有显示器时可以看窗口能否起来（约半秒后自动关闭）：

```bash
dotnet run --project client/Legend.Playable -c Release -- --ui-smoke
```

只验证公网服握手（不进游戏）：

```bash
dotnet run --project client/Legend.Playable -c Release -- --probe --host 150.158.164.90 --port 7100
```

## 「能玩一局」清单：已有 vs 缺

| 能力 | 状态 | 说明 |
|------|------|------|
| 不装 Unity 打出 Windows exe | ✅ 已有 | `dotnet publish -r win-x64` |
| 登录 / 注册 / 选角 / 创角 | ✅ 已有 | 协议 1.21.0 |
| 进图 / 格子画面 / 名字血条 | ✅ 已有 | 程序色块，不是精灵图 |
| 移动同步 / 点地 | ✅ 已有 | |
| 打怪 / 飘血 / 安全区禁攻 | ✅ 已有 | 技能 `basic_slash` |
| 掉落 / 拾取 | ✅ 已有 | |
| 聊天 | ✅ 已有 | 世界 / 附近 |
| 死亡回城 | ✅ 服务端已有 | 客户端 toast |
| 背包 | ⚠️ 骨架 | 列表 + 点击使用；没有拖拽装备 |
| BGM / 攻击受击拾取音 | ✅ 已有 | 程序 WAV，仅 Windows 出声 |
| 小地图 | ⚠️ 骨架 | 色点，不是地图贴图 |
| MP | ⚠️ 占位 | 快照无当前 MP；进世界、攻击回包、升级会更新 |
| 商店 / 任务 / 行会 / 技能栏 | ❌ 缺 | **P1**（点 NPC 只会把对话和货品打到聊天） |
| 精美 2D 精灵 / 地图块 | ❌ 缺 | **P1** |
| Unity 玩家包 | 可选 | 见下文；没有 Editor 也能验收 |

### P1（下一轮）

1. 技能栏与除 `basic_slash` 以外的快捷键
2. 商店购买、任务、行会面板
3. 自绘精灵替换色块
4. 非 Windows 也播放 BGM（当前 Linux 构建只保证逻辑与出包，不出声）

## 可选：Unity 2022.3 玩家包

本机执行器环境没有 Unity Editor。若你自己有 **2022.3**（工程写的是 2022.3.20f1），仍可用旧入口覆盖同一个输出目录：

```bat
set UNITY_EDITOR=C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe
set LEGEND_WINDOWS_OUTPUT=%CD%\dist\windows-player
"%UNITY_EDITOR%" -batchmode -nographics -quit -projectPath "%CD%\client\unity-mvp" -executeMethod Legend.Build.Windows.BuildPlayer -logFile "%LEGEND_WINDOWS_OUTPUT%\unity-windows-build.log"
```

菜单 **Legend → Build → Windows Player**，或 `./scripts/build-windows-player.sh`。该路径会打出同名 `LegendClient.exe`，和上面的 .NET 包二选一，不要混在一个目录里。验收以 **dotnet publish** 为准。

旧骨架检查（不产生 exe）：

```bash
./scripts/verify-phase-k-playable.sh
```
