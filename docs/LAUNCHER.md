# Phase H / I — PC 登录器

协议仍为 **1.21.0**（本阶段未改游戏协议；区服列表与更新清单走本地 JSON / 可选 HTTP）。

## 能力

| 功能 | 说明 |
|------|------|
| 区服列表 | 读 `launcher/config/realms.json`；可选 `realmListUrl`（http/https/file/本地路径）覆盖合并 |
| 选区 | **Avalonia 窗口**或控制台菜单 / `--realm-id`；展示区名 / 状态 / TCP connect RTT |
| 更新检查 | 本地 `version.json` vs `update-manifest*.json`（或 URL）；`IUpdateChannel` 可扩展 |
| 下载骨架 | 无真实包 URL 时假进度并写 `cache/updates/update-*.bin` |
| 启动客户端 | `clientExecutable`：Unity 玩家、`Legend.ClientStub.dll`、或 `scripts/fake-client.sh` |

**不包含**：完整 CDN 差分热更、精美皮肤、支付。

## 项目结构

```text
launcher/
├── Legend.Launcher.Core/       # 可测纯逻辑（Facade）
├── Legend.Launcher/            # 控制台（无头 / 交互菜单回退）
├── Legend.Launcher.Desktop/    # Avalonia 窗口 GUI（Phase I）
├── Legend.ClientStub/
├── Legend.Launcher.Tests/
└── config/
```

## 配置项（`config/launcher.json`）

| 字段 | 含义 |
|------|------|
| `realmsPath` | 本地区服 JSON |
| `realmListUrl` | 可选远端区服列表（同 JSON 形态，仅含公开字段） |
| `versionPath` | 本地客户端版本 |
| `updateManifestPath` / `updateManifestUrl` | 更新清单 |
| `clientExecutable` | 启动目标（dll / 可执行文件 / `.sh`） |
| `clientArgsTemplate` | 默认 `--host {host} --port {port} --realm-id {realmId}` |
| `downloadCacheDir` | 下载缓存目录 |
| `probeTimeoutMs` | 延迟探测超时 |

区服 JSON **勿**放入 `adminToken` / 支付密钥等敏感字段。

## 如何运行

### Avalonia 窗口 GUI（推荐有显示器时）

```bash
cd /path/to/legend-platform/launcher
dotnet build Legend.Launcher.sln -c Release

# Windows（本机有桌面）
dotnet run --project Legend.Launcher.Desktop -c Release --no-build

# Linux 有显示器
dotnet run --project Legend.Launcher.Desktop -c Release --no-build

# Linux 无显示器：先装/起 Xvfb，再跑（仅冒烟窗口能否起来）
# Xvfb :99 -screen 0 1280x720x24 &
# DISPLAY=:99 dotnet run --project Legend.Launcher.Desktop -c Release --no-build
```

窗口按钮：刷新区服 / 探测延迟 / 检查更新 / 下载更新 / 启动游戏；底栏为状态。未配置 `clientExecutable` 时 Desktop 会尝试自动指向已编译的 `Legend.ClientStub.dll` 或 `scripts/fake-client.sh`。

### 控制台回退（无头 CI / SSH）

```bash
cd /path/to/legend-platform/launcher
dotnet run --project Legend.Launcher -c Release --no-build

STUB="$(pwd)/Legend.ClientStub/bin/Release/net8.0/Legend.ClientStub.dll"
dotnet run --project Legend.Launcher -c Release --no-build -- \
  --headless --probe --check-update --download \
  --launch --realm-id realm-classic-01 \
  --client "$STUB"
```

## 验收

```bash
./scripts/verify-phase-h-launcher.sh   # 控制台 + Core
./scripts/verify-phase-i-launcher-gui.sh  # Desktop 编译 + Core 单测
```

人工（有显示器）：打开 Desktop → 刷新 → 探测 → 检查更新 → 选区启动 stub，状态栏看到成功信息。

## 限制

- 本仓库默认 Linux 构建机常**无显示器**：`verify-phase-i` 只要求 **headless 编译通过**，不强制弹出窗口。
- 未做完整热更 CDN / 差分包安装；下载仅为进度骨架。
- 未推远程 GitHub（按任务约束本地 commit）。

## 与 Unity

把 `clientExecutable` 指到 Unity 播放器或打包后的 Linux/Windows 可执行文件，参数模板保持 `--host/--port/--realm-id`。

## Phase K — Unity 玩家包

可玩 PC 竖切与 `LegendClient.exe` 接线见 [`PLAYABLE_PC.md`](PLAYABLE_PC.md)。示例配置：`launcher/config/launcher.playable.example.json`。
