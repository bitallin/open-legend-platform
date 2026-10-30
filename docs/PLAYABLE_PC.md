# 可玩 PC 客户端（非 Unity 为验收路径）

协议仍为 **1.21.0**（本阶段未改游戏协议版本字符串）。画面是原创八方向像素图集（`tools/gen_mir_sheet.py`），声音是程序生成的 WAV，**禁止**导入官方传奇 / 996 闭源资源包。观感对照见 [VISUAL_PARITY.md](VISUAL_PARITY.md)。

生产是否做完，不看这份「能玩一局」说明，而看 [MIR_PRODUCTION_FEATURE_MATRIX.md](MIR_PRODUCTION_FEATURE_MATRIX.md)。当前百分比与下一批任务见 [DEV_PROGRESS.md](DEV_PROGRESS.md)。协议若加报文，先写 [PROTOCOL_NOTES.md](PROTOCOL_NOTES.md)。

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
| WASD / 方向键 | 走格子；按住 Shift 跑 |
| 鼠标左键点地 | 走到那一格 |
| 左键点怪物 | 选中并普攻（受攻击模式约束） |
| Shift+左键 | 强制攻击 |
| 右键点地 / Ctrl+左键 | 跑 |
| 右键点单位 / Ctrl+右键 | 查看 |
| Alt+点击 | 挖取附近掉落 |
| F1–F8 | 技能栏 |
| 1–6 | 药品栏 |
| F9 / B | 背包 |
| F10 | 人物 |
| F11 | 技能页 |
| Tab | 大地图；`` ` `` 切换怪物目标 |
| Ctrl+H | 攻击模式 |
| Alt+X | 回到选角 |
| Space / J | 普攻 |
| Z / G | 拾取 |
| Enter | 聊天。`!` 喊话，`!!` 行会，`!~` 组队，`@名字 内容` 私聊 |
| Esc | 关闭面板 |

生产条目以 [MIR_PRODUCTION_FEATURE_MATRIX.md](MIR_PRODUCTION_FEATURE_MATRIX.md) 第 18 节为准。

### 画面

- 地面：斜 45° 菱形砖（64×32）。草地、土路、石板、安全区、树和墙、传送门光。点地仍落到原来的格子
- 角色 / 怪物 / NPC：八方向站立、走、跑、攻击、施法、受击、死亡。名字带阴影，选中怪才出红血条
- 飘字：伤害 / MISS / 治疗。技能有斩击、火焰、雷电、治疗光效
- HUD：红血、蓝魔法、金边技能栏、右上小地图、聊天、背包

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
| 进图 / 格子画面 / 名字血条 | ✅ 已有 | 原创八方向图集，不是官方 WIL |
| 移动同步 / 点地 | ✅ 已有 | |
| 打怪 / 飘血 / 安全区禁攻 | ✅ 已有 | 技能 `basic_slash` |
| 掉落 / 拾取 | ✅ 已有 | |
| 聊天 | ✅ 已有 | 世界 / 附近 |
| 死亡回城 | ✅ 服务端已有 | 客户端 toast |
| 背包 | ⚠️ 骨架 | 列表 + 点击使用；没有拖拽装备 |
| BGM / 攻击受击拾取音 | ✅ 已有 | 程序 WAV，仅 Windows 出声 |
| 小地图 | ⚠️ 骨架 | 色点，不是地图贴图 |
| MP | ⚠️ 占位 | 快照无当前 MP；进世界、攻击回包、升级会更新 |
| 商店 / 任务 / 行会 / 技能栏 | ⚠️ 已接面板 | 买/卖/修理、任务链、组队行会、F1–F8。范围技能与攻城 UI 仍见生产清单 |
| 精美 2D 精灵 / 地图块 | ❌ 缺 | **P1** |
| Unity 玩家包 | 可选 | 见下文；没有 Editor 也能验收 |

### 生产清单

上面这张表只描述「能玩一局」。完整生产条目（玩法、画面、音效、键位）在 [MIR_PRODUCTION_FEATURE_MATRIX.md](MIR_PRODUCTION_FEATURE_MATRIX.md)，进度在 [DEV_PROGRESS.md](DEV_PROGRESS.md)。

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
