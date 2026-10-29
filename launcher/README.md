# Legend.Launcher — Phase H 登录器骨架

.NET 8 控制台登录器：选区、延迟探测、更新检查骨架、启动客户端（Unity 或 `Legend.ClientStub` / `scripts/fake-client.sh`）。

## 结构

```text
launcher/
├── Legend.Launcher.Core/     # 可测纯逻辑
├── Legend.Launcher/          # 中文交互 + --headless
├── Legend.ClientStub/        # 占位客户端
├── Legend.Launcher.Tests/
├── config/
│   ├── launcher.json
│   ├── realms.json
│   ├── version.json
│   └── update-manifest.sample.json
└── Legend.Launcher.sln
```

## 运行

```bash
cd launcher
dotnet build Legend.Launcher.sln -c Release
# 交互
dotnet run --project Legend.Launcher -c Release --no-build
# 无头选区启动 stub
dotnet run --project Legend.Launcher -c Release --no-build -- \
  --headless --probe --check-update --download \
  --launch --realm-id realm-classic-01 \
  --client "$(pwd)/Legend.ClientStub/bin/Release/net8.0/Legend.ClientStub.dll"
```

详见 `docs/LAUNCHER.md`。验收：`./scripts/verify-phase-h-launcher.sh`。
