# 02 · 角色与职业

完成度以矩阵 **A-04–A-12** 为准。职业显示名可以对齐战法道，id 使用原创字符串。

## 概述与玩家体验

登录后看到最多若干个角色（样例 `maxCharactersPerAccount` 为 3）。玩家选择职业与性别、输入名字并创建，再选一个角色进入世界。世界中按 Alt+X 离开地图，回到选角，登录连接保持。删除角色在协议里有，可玩客户端需要有明确入口才算操作闭环（矩阵说明：协议 1105 已有）。

三职业手感差异来自 `classes.json` 的基础生命、魔法、攻击，以及 `levels.json` 的每级成长，而不是三套协议。

## 协议 / MsgId

| MsgId | 消息 | 说明 |
|-------|------|------|
| 1101 / 1102 | `CharListReq` / `CharListResp` | `CharacterSummary` 含职业、等级、性别等 |
| 1103 / 1104 | `CreateCharReq` / `CreateCharResp` | `CreateCharReq.gender` 在 Phase N 写入。非法职业 `ERR_INVALID_CLASS (1009)`，重名 `ERR_CHAR_NAME_TAKEN (1008)`，超过上限 `ERR_CHAR_LIMIT (1010)` |
| 1105 / 1106 | `DeleteCharReq` / `DeleteCharResp` | 删除后列表刷新 |
| 1107 / 1108 | `SelectCharReq` / `SelectCharResp` | 选定本连接要进入的角色 |
| 2001 / 2002 | `EnterWorldReq` / `EnterWorldResp` | 按 `character_id` 进图，下发出生地图与自身快照 |
| 3971 / 3972 | `LeaveWorldReq` / `LeaveWorldResp` | 离开世界，角色离开 AOI，连接仍在，客户端回选角 |

角色不存在：`ERR_CHAR_NOT_FOUND (1007)`。

名字规则的服务端长度与重名检查已存在。非法字符集若与 UI 提示不一致，以矩阵 A-09 的说明和当前校验代码为准，改规则时同时改客户端提示。

英雄、国家、阵营字段：**TBD**。`EntitySnapshot` 没有英雄 id，也没有国家 id。

## 服务端模块与配置

- 角色存档模型：`server/Legend.Storage/Models.cs`（外观 JSON 里也放个人仓库等扩展状态）
- 职业表：`version-packs/sample-classic/configs/classes.json`

| classId | displayName | baseHp | baseMp | baseAtk |
|---------|-------------|--------|--------|---------|
| `warrior` | 武者 | 180 | 40 | 25 |
| `mage` | 术士 | 100 | 160 | 32 |
| `taoist` | 灵师 | 135 | 110 | 20 |

- 人数上限：manifest 字段 `maxCharactersPerAccount`
- 进世界与离开世界：`server/Legend.Game/GameWorldModule.cs` 与 `GameWorldModule.PhaseP.cs`
- 外观（性别影响纸娃娃）：`GameWorldModule.Appearance.cs`。快照字段 `EntitySnapshot.gender`、`CharacterSummary.gender`

另一份职业表示例在 `version-packs/sample-176/configs/classes.json`，改包时保持 id 字符串稳定，避免存档对不上。

## 客户端表现

- 选角与创角画面：矩阵 **V-02**。Playable 立绘按职业和性别取图集正面帧，`Ui/ClassPortrait.cs`。肩宽和衣服色随性别变化（A-08）。
- 世界里的纸娃娃分层见 [20](20-audio-visual.md)。性别不改变技能表，只改变外观。
- Alt+X 绑定见 [19](19-client-ui-controls.md)，报文是 `LeaveWorldReq`。
- Unity 会话枚举：`client/unity-mvp/.../SessionPhase.cs`。创角 UI 完整度以矩阵为准；改 Unity 时保持与 Playable 同一组 MsgId。

## 登录器相关

登录器不创角、不选角。它只保证启动参数里的区服与玩家所选一致。换区等于换进程和换存档，角色不跨区共享，除非走合区工具（[17](17-cross-realm-merge.md)）。

## 配置热更与脚本钩子

`on_player_enter` 在进入世界时触发，可 `send_welcome` 或在权限允许时 `grant_starter_item`。创角瞬间没有单独钩子。若运营要「创角送绑定礼包」，优先在首次 `on_player_enter` 里按等级或标记发放，避免绕过职业校验。

热重载可以换 `classes.json` 的成长基数。已经在线的角色是否立即重算，以实现时的加载代码为准；规格要求重载后**新进入世界**的角色使用新表，在线角色至少在下次升级或重新进世界时一致。不要让热重载改掉已写入存档的 `classId`。

## 验收标准

| 矩阵 ID | 规格要点 |
|---------|----------|
| A-04 | 登录后拉角色列表 |
| A-05 | 创建 `warrior`，显示武者 |
| A-06 | 创建 `mage`，显示术士 |
| A-07 | 创建 `taoist`，显示灵师 |
| A-08 | 男/女写入外观并改变纸娃娃 |
| A-09 | 名字长度约 2–16，重名拒绝；非法字符与 UI 提示保持一致 |
| A-10 | 1105 删除角色 |
| A-11 | 选角后 `EnterWorld` 出现在地图出生点 |
| A-12 | Alt+X 发送 3971，回到选角且不断开登录 |

## 非目标

- 不使用第三方职业编号或英雄协议。
- 不把「武者/术士/灵师」做成与任何闭源服互登的角色格式。
- 性别不是权限位，不参与伤害公式。

## 扩展路线

未来工作，未进入矩阵。

1. **英雄**。独立于主角的可召唤单位。完整规格草案（存档、生命周期、忠诚、装备、模式、报文职责）写在 [15](15-pet-hero-mount.md) 扩展路线「A 英雄」。本页只约束：英雄不占用 `maxCharactersPerAccount` 的三个主角槽；英雄 id 与主角 `character_id` 分开；合击判定步骤见 [05](05-combat-skills.md) 扩展路线「合击」。
2. **多职业**。第四职业及以上只加 `classes.json` 行、技能 `allowedClasses` 和客户端立绘。不要换 MsgId。未提供立绘与技能的职业 id 创建时返回 `ERR_INVALID_CLASS`。
3. **阵营**。可选字符串，默认空。PK 规则仍以攻击模式为准（[10](10-pk-redname.md)）。阵营若参与「可攻击」，必须在服务端判定，客户端切换无效。报文 **TBD**。
4. **国家**。与区服不同。国家是角色属性，区服是进程。改名、国王、国家仓库都未设计。在跨服（[17](17-cross-realm-merge.md)）之前不要做国家战。
5. **内功**。角色面板上的第二套等级，见 [03](03-attributes-growth.md)，不新开职业 id。
