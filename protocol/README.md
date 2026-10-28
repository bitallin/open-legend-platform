# Legend Platform — 自有协议契约 (`protocol/`)

> **100% 原创协议说明**：本目录定义平台唯一真相源（Single Source of Truth）Protobuf 契约与帧格式，与任何第三方或闭源传奇引擎协议无任何兼容或衍生关系。

## 1. 语义版本 (`VERSION`)
当前协议版本：`1.0.0`
- **Major（主版本）**：不兼容的协议结构或帧头调整（客户端与服务端 Major 不一致时在 `Handshake` 阶段直接以 `ERR_VERSION_MISMATCH (1001)` 拒连）。
- **Minor（次版本）**：向后兼容的新增消息或可选字段（服务端 `Major` 相同且客户端 `Minor >= MinCompatibleMinor` 时允许连接）。
- **Patch（修订版）**：文档或内部枚举描述修正。

## 2. 二进制帧头格式（10 字节定长头 + 变长 Payload）
所有 TCP 数据包均采用大端序（Big-Endian / Network Byte Order）定长帧头：
```
+------------------+------------------+------------------+-------------------------+
|   Length (4B)    |    MsgId (2B)    |     Seq (4B)     |      Payload (NB)       |
|   uint32 BE      |    uint16 BE     |    uint32 BE     |   Protobuf Serialized   |
+------------------+------------------+------------------+-------------------------+
```
- `Length` (4 Bytes, `uint32`): `Payload` 字节长度（不含 10 字节帧头，最大单包限制 1 MB）。
- `MsgId` (2 Bytes, `uint16`): 消息类型枚举 ID。
- `Seq` (4 Bytes, `uint32`): 客户端递增序号（服务端响应或广播回传对应序号或服务端通知序号）。
- `Payload` (`Length` Bytes): Protobuf 序列化后的二进制数据。

## 3. 消息 ID 映射表
| MsgId | 消息名称 | 方向 | 说明 |
|-------|----------|------|------|
| 1001 | `HandshakeReq` | C -> S | 连接后首包握手，携带协议语义版本 |
| 1002 | `HandshakeResp` | S -> C | 握手结果，版本不兼容时返回 `ERR_VERSION_MISMATCH` |
| 1003 | `LoginReq` | C -> S | 账密登录请求 |
| 1004 | `LoginResp` | S -> C | 登录响应，颁发 `session_token` |
| 1005 | `RealmListReq` | C -> S | 拉取可用区服列表 |
| 1006 | `RealmListResp` | S -> C | 返回当前区服元数据与在线人数 |
| 1007 | `PingReq` | C -> S | 心跳请求 |
| 1008 | `PongResp` | S -> C | 心跳响应 |
| 2001 | `EnterWorldReq` | C -> S | 选区创角/进入地图 |
| 2002 | `EnterWorldResp` | S -> C | 进图响应，含自身快照与当前 AOI 视野内实体列表 |
| 2003 | `MoveReq` | C -> S | 请求移动到目标网格坐标 `(to_x, to_y)` |
| 2004 | `MoveSync` | S -> C | 权威位置同步（发给移动者及同屏 AOI 观察者） |
| 2005 | `EntityEnterAoi` | S -> C | 实体进入当前视野广播 |
| 2006 | `EntityLeaveAoi` | S -> C | 实体离开当前视野广播 |
| 3001 | `ServerNotice` | S -> C | 服务端公告 / Lua 热更脚本钩子通知（含新手赠礼） |
