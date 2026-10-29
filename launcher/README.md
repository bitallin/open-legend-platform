# Legend.Launcher — Phase H/I 登录器

.NET 8：Core 逻辑 + 控制台回退 + **Avalonia Desktop GUI**。

## 结构

```text
launcher/
├── Legend.Launcher.Core/
├── Legend.Launcher/              # 控制台
├── Legend.Launcher.Desktop/      # Avalonia 窗口
├── Legend.ClientStub/
├── Legend.Launcher.Tests/
└── config/
```

## 运行

```bash
cd launcher
dotnet build Legend.Launcher.sln -c Release
# GUI
dotnet run --project Legend.Launcher.Desktop -c Release --no-build
# 控制台无头
dotnet run --project Legend.Launcher -c Release --no-build -- \
  --headless --probe --check-update --download \
  --launch --realm-id realm-classic-01 \
  --client "$(pwd)/Legend.ClientStub/bin/Release/net8.0/Legend.ClientStub.dll"
```

详见 `docs/LAUNCHER.md`。验收：`./scripts/verify-phase-i-launcher-gui.sh`。
