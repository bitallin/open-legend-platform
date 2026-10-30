# 13 · 聊天与社交

完成度以矩阵 **H-01–H-08、S-01、S-02、S-08–S-10** 为准。邮件有完整报文（3101–3111），矩阵没有单独的邮件字母行；邮件行为以本节和协议为准，完成度不要写成新的 Done 行。

## 概述与玩家体验

玩家在底部输入框聊天。前缀决定频道：附近、世界喊话、行会、组队、私聊。系统消息由服务器推送。脏词在发送前和广播前替换成 `**`。被禁言的角色发不出去。屏蔽列表让客户端不再显示某人，服务端禁言则谁都收不到。

组队有邀请、接受、离开、踢人。同队击杀分享经验。好友按名字添加，能看在线并删除。师徒用聊天指令记下师父：徒弟击杀经验有加成，在线师父分到一部分基础经验。

邮件可列表、阅读、领附件、删除、发送和通知。支付发货和系统奖励走邮件，避免直接塞爆背包时无提示（背包满：`ERR_MAIL_BAG_FULL`）。

## 协议 / MsgId

聊天：

| MsgId | 消息 | 说明 |
|-------|------|------|
| 2509 / 2510 | `ChatReq` / `ChatNotify` | 频道枚举见下 |
| 3001 | `ServerNotice` | 系统 |

`ChatChannel`：`CHAT_WORLD=0`、`CHAT_NEARBY=1`、`CHAT_GUILD=2`、`CHAT_WHISPER=3`、`CHAT_PARTY=4`。

输入前缀（矩阵 18.1，客户端在发 `ChatReq` 前解析，或服务端再校验一次）：

| 输入 | 频道 |
|------|------|
| 普通文本 | 附近 |
| `!文本` | 世界喊话 |
| `!!文本` | 行会 |
| `!~文本` | 组队 |
| `@名字 文本` | 私聊，目标名写入请求 |

拒绝：`ERR_CHAT_DENIED (1028)`、`ERR_MUTED (1080)`、`ERR_TARGET_OFFLINE (1047)`。

组队 2701–2710：邀请、接受、离开、踢人、`PartyInfoNotify`、`PartyInviteNotify`。错误：`ERR_PARTY_DENIED`、`ERR_PARTY_FULL`、`ERR_ALREADY_IN_PARTY`、`ERR_NOT_IN_PARTY`、`ERR_PARTY_NOT_FOUND`。

好友 2711–2716：添加、删除、列表。错误：`ERR_FRIEND_DENIED`、`ERR_FRIEND_LIMIT`、`ERR_NOT_FRIEND`、`ERR_FRIEND_NOT_FOUND`。

邮件 3101–3111：列表、读、领附件、删、`MailNotify`、发送。错误从 `ERR_MAIL_DENIED (1056)` 到 `ERR_MAIL_FULL (1061)`。

师徒没有单独 MsgId。矩阵 **S-10**：`/拜师` 记录师父；徒弟击杀经验 +10%，在线师父获得该次基础经验的 20%。黑名单 **S-09** 若使用 `/屏蔽`，与好友列表分开存储。

结婚、语音房间：**TBD**。

## 服务端模块与配置

- 聊天与行会频道：`server/Legend.Game/GameWorldModule.GuildChat.cs`
- 好友与邮件存储：`FriendStores.cs`、`MailStores.cs`
- 组队配置：`configs/party.json`
- 邮件配置：`configs/mail.json`
- 脏词：`configs/profanity.txt`（发送前与广播前替换）
- 脚本：`scripts/party.lua`、`scripts/social.lua`、`scripts/mail.lua`
- 禁言：Admin `POST /admin/mute`，存储 `ModerationStores.cs`

钩子：`on_party_join`、`on_party_leave`、`on_friend_add`、`on_whisper`、`on_mail_send`、`on_mail_claim`。

附近距离使用 `guild.json` 里的 `nearbyChebyshev`（样例 14）作为切比雪夫半径的同类参数；若聊天代码有自己的半径常量，改半径时两处一起核对，避免行会与附近共用一个误改。

经验分享在击杀结算时加到队员，客户端用 `ExpGainNotify` 看到结果（**S-02**）。

## 客户端表现

- 聊天框 **V-20 / H-01–H-06 / O-16**：Enter 聚焦或发送
- 世界按钮对应喊话（**H-02**）
- 私聊需要能填目标名（**H-03**）
- 组队与行会频道按钮应发送 `CHAT_PARTY` 与 `CHAT_GUILD`，不要只在本地改颜色
- 组队面板 **V-28 / S-01**
- 好友面板 **S-08**：按名字加、在线状态、删除
- 指令 **H-07 / O-17**

Unity 有 `ChatLogBuffer.cs`，面板完整度以 Playable 验收为准。

## 登录器相关

无。禁言和好友在区服库。合区迁入邮件与 moderation；好友以目标区为准（源区好友丢弃，见合区规则）。

## 配置热更与脚本钩子

`profanity.txt` 随版本包重载后，下一条聊天即用新词表。不要把词表热更做成重启。

禁言时长若在 Admin body 里，到期由服务端时钟解除。重载版本包不清禁言。

邮件附件物品 id 必须存在于物品表。热更删掉物品后，未领取附件显示为不可领并保留邮件，直到表恢复或管理员删除邮件。

## 验收标准

| 矩阵 ID | 规格要点 |
|---------|----------|
| H-01 | 附近 |
| H-02 | `!` 世界 |
| H-03 | `@名字` 私聊 |
| H-04 | `!~` 与 `CHAT_PARTY` |
| H-05 | `!!` 行会 |
| H-06 | `ServerNotice` 与系统频道 |
| H-07 | 四种前缀 |
| H-08 | `profanity.txt` 替换、禁言、`/屏蔽` |
| S-01 | 2701–2710 |
| S-02 | 组队经验 |
| S-08 | 2711–2716 |
| S-09 | 黑名单 / 屏蔽 |
| S-10 | `/拜师` 与经验比例 |

邮件验收（无矩阵 ID）：列表、阅读、领取、删除、发送、背包满拒绝、重复领取 `ERR_MAIL_ALREADY_CLAIMED`。支付发货路径见 [12](12-mall-payment.md)。

## 非目标

- 不接入第三方语音 SDK 的协议。
- 不把聊天内容交给 Lua 执行。
- 敏感词替换不是安全边界的全部，见 [22](22-security-anti-cheat.md)。

## 扩展路线

未来工作，未进入矩阵。

1. **结婚**。两名角色互相确认后写入关系 id。技能或经验加成用 Buff。离婚走 NPC。报文 **TBD**。在落地前不要占用师徒字段。
2. **语音**。若做，使用独立的房间令牌服务，游戏服只发「是否允许加入附近房间」。不在版本包里写第三方语音命令。
3. **师徒出师**。现有比例保持。出师奖励、等级差限制、多徒弟上限写进 `configs/party.json` 或新的 `mentorship.json`。指令仍用 `/` 前缀，由服务端解析，不进 Lua `load`。
4. **邮件附件金币**。若 `MailAttachment` 已支持金币则沿用；否则扩展字段 **TBD**，不要把金币塞进假物品 id。
5. **跨服私聊**。依赖 [17](17-cross-realm-merge.md) 的路由。单区阶段私聊只找本区在线角色。
