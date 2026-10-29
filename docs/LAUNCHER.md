# Phase H — PC 登录器骨架

协议仍为 **1.21.0**（本阶段未改游戏协议；区服列表与更新清单走本地 JSON / 可选 HTTP）。

## 能力

| 功能 | 说明 |
|------|------|
| 区服列表 | 读 `launcher/config/realms.json`；可选 `realmListUrl`（http/https/file/本地路径）覆盖合并 |
| 选区 | 中文菜单或 `--realm-id`；展示区名 / 状态 / TCP connect RTT |
| 更新检查 | 本地 `version.json` vs `update-manifest*.json`（或 URL）；`IUpdateChannel` 可扩展 |
| 下载骨架 | 无真实包 URL 时假进度并写 `cache/updates/update-*.bin` |
| 启动客户端 | `clientExecutable`：Unity 玩家、`Legend.ClientStub.dll`、或 `scripts/fake-client.sh` |

**不包含**：完整 CDN 差分热更、登录器 GUI 皮肤、支付。

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

```bash
cd /path/to/legend-platform/launcher
dotnet build Legend.Launcher.sln -c Release

# 交互菜单
dotnet run --project Legend.Launcher -c Release --no-build

# 无头：探测 + 更新 + 启动 stub
STUB="$(pwd)/Legend.ClientStub/bin/Release/net8.0/Legend.ClientStub.dll"
dotnet run --project Legend.Launcher -c Release --no-build -- \
  --headless --probe --check-update --download \
  --launch --realm-id realm-classic-01 \
  --client "$STUB"
```

或用 bash 占位客户端：

```bash
dotnet run --project Legend.Launcher -c Release --no-build -- \
  --launch --realm-id realm-classic-01 \
  --client ../scripts/fake-client.sh
```

## 验收步骤（人工）

1. 跑 `./scripts/verify-phase-h-launcher.sh` 全绿  
2. 交互模式：`1` 刷新区服 → `2` 探测延迟 → `3` 检查更新（应提示 0.1.0→0.2.0）→ `5` 选一区启动 stub，看到 `host=` / `port=` / `realm-id=`  
3. 改 `config/version.json` 的 `clientVersion` 为 `0.2.0` 后再检查，应显示「已是最新」  
4. （可选）起 `Legend.Host` 后，`2` 探测一区应有 RTT 毫秒数  

## 与 Unity

将来把 `clientExecutable` 指到 Unity 播放器或打包后的 Linux/Windows 可执行文件，参数模板保持 `--host/--port/--realm-id`；Unity MVP Bootstrap 可读同名命令行（若尚未接线，先用 stub 验收登录器本身）。

## 目录

见 `launcher/README.md`。
