# Legend Platform — 自有协议契约 (`protocol/`)

> **100% 原创协议**：与任何第三方或闭源传奇引擎协议无兼容关系。

## 语义版本
当前：**1.1.0**（相对 1.0.0：新增注册、创角/选角/删角、地图切换通知、安全区标记；`EnterWorld` 改为按 `character_id` 进服）。
- Major 不一致 → Handshake 返回 `ERR_VERSION_MISMATCH (1001)`。
- MinCompatible：**1.1.0**（M1 起客户端需跟进角色协议）。

## 帧头（大端）
`[Len:4][MsgId:2][Seq:4][Payload]` — Length 不含 10 字节头，最大 Payload 1MB。

## MsgId 映射
| MsgId | 消息 | 方向 |
|------|------|------|
| 1001/1002 | HandshakeReq/Resp | C↔S |
| 1003/1004 | LoginReq/Resp | C↔S |
| 1005/1006 | RealmListReq/Resp | C↔S |
| 1007/1008 | PingReq/PongResp | C↔S |
| 1009/1010 | RegisterReq/Resp | C↔S |
| 1101/1102 | CharListReq/Resp | C↔S |
| 1103/1104 | CreateCharReq/Resp | C↔S |
| 1105/1106 | DeleteCharReq/Resp | C↔S |
| 1107/1108 | SelectCharReq/Resp | C↔S |
| 2001/2002 | EnterWorldReq/Resp | C↔S |
| 2003/2004 | MoveReq/MoveSync | C↔S |
| 2005/2006 | EntityEnterAoi/LeaveAoi | S→C |
| 2007 | MapChangeNotify | S→C |
| 3001 | ServerNotice | S→C |
