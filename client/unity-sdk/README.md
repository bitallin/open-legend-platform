# Legend Platform — Unity 2022 LTS 客户端网络与协议 SDK (`client/unity-sdk/`)

本目录包含供 **Unity 2022 LTS（PC + Android 同源工程）** 直接拖入使用的纯 C# 网络与协议 SDK，与服务端 `Legend.Host` 共享同一套 `protocol/legend_protocol.proto` 契约与 `[Len:4][MsgId:2][Seq:4][Payload]` 大端帧格式。

## 1. 目录文件清单
- `Runtime/LegendProtocol.cs`：由 `protocol/legend_protocol.proto` 预生成的标准 C# Protobuf 消息类（无需在 Unity 机器安装 `protoc`）。
- `Runtime/ProtocolConstants.cs`：消息 ID 枚举 (`MsgId`)、语义版本兼容校验与 10 字节定长头编解码器 (`PacketCodec`)。
- `Runtime/LegendUnityNetworkClient.cs`：纯 C# 异步 TCP 客户端，内置 `PumpMainThreadEvents()` 线程安全队列，将异步网络包自动派发回 Unity 主线程。
- `Runtime/LegendWorldDemoBehaviour.cs`：开箱即用的 `MonoBehaviour` 示例脚本（自动走完握手 -> 登录 -> 选区 -> 进图 -> 生成自身与同屏玩家占位胶囊体 -> WASD/方向键移动同步 -> 接收 Lua 欢迎礼）。

## 2. 3 分钟接入 Unity 2022 LTS 步骤
1. **引入 `Google.Protobuf.dll`**：
   - 在 Unity Package Manager 或 NuGetForUnity 中安装 `Google.Protobuf`（或直接从服务端构建产物 `server/Legend.Contracts/bin/Debug/net8.0/` 复制 `Google.Protobuf.dll` 到 Unity 的 `Assets/Plugins/`）。
2. **拖入 SDK 源码**：
   - 将 `client/unity-sdk/Runtime/` 下的 4 个 `.cs` 文件复制到 Unity 工程的 `Assets/Scripts/LegendSdk/`。
3. **挂载并运行联机场景**：
   - 启动服务端：`dotnet run --project server/Legend.Host -- --port 7100`
   - 在 Unity 新建空场景，创建空物体 `NetworkManager`，挂载 `LegendWorldDemoBehaviour` 组件。
   - 点击 Unity Play（可同时开启两个 Unity Editor / Build 实例），即可看到双方胶囊体在 `(10, 10)` 同屏生成，按 `WASD` 或方向键实时同步移动，且 Console 打印 Lua 热更脚本下发的新手欢迎礼与赠送物品 `item_wooden_blade x1`。
