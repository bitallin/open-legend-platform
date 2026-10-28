# Legend Platform — 原创传奇类三端多版本运营平台 (Phase A / M0)

> **100% 原创自研声明**：本仓库采用自有 Protobuf 协议、自有帧格式 (`[Len:4][MsgId:2][Seq:4][Payload]`)、自有版本包 (`version-packs/`) 与自有白名单 Lua 沙箱 API (`legend_api.*`)，严禁任何对第三方闭源引擎的逆向、破解或协议兼容。

## 1. 目录结构
```text
legend-platform/
├── protocol/                    # Protobuf 契约唯一真相源 (legend_protocol.proto) + 语义版本 (VERSION=1.0.0)
├── server/                      # .NET 8 权威服解决方案 (LegendPlatform.sln)
│   ├── Legend.Contracts/        # 核心接口 (IPlugin, IGameModule, IEventBus, IVersionPackLoader) + 帧编解码
│   ├── Legend.Core/             # 领域事件总线、插件热插拔管理器、版本包加载校验器、结构化日志
│   ├── Legend.Gate/             # TCP 异步接入网关、会话管理、协议帧分发
│   ├── Legend.Login/            # 握手版本校验、账密登录、Token 会话签发、区服列表、心跳
│   ├── Legend.Game/             # 权威世界模块、2D 网格空间索引 AOI (GridAoiManager)、阻挡与反作弊移动校验
│   ├── Legend.Script/           # MoonSharp Lua 强沙箱 (Preset_HardSandbox)、白名单 API 与热重载宿主
│   ├── Legend.Host/             # 统一启动宿主，加载版本包 manifest 并监听 TCP 端口
│   ├── Legend.CliClient/        # 命令行多端联机冒烟/压测客户端
│   └── Legend.Tests/            # xUnit 自动化集成测试套件
├── client/unity-sdk/            # Unity 2022 LTS (PC + Android) 纯 C# 网络与协议客户端 SDK
├── version-packs/               # 版本即制品（Manifest + 地图阻挡配置 + 职业表 + Lua 脚本）
│   ├── sample-classic/          # 示例经典复古版本包
│   └── sample-176/              # 示例 176 模板版本包
├── ops/                         # Docker Compose (Postgres 16 + Redis 7 + Legend.Host)
└── scripts/
    └── verify-m0.sh             # 一键编译、单元测试、真实 TCP 联机全链路验收脚本
```

## 2. 快速验证
```bash
./scripts/verify-m0.sh
```
