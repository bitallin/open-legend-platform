# 画面、动作、特效对照

功能清单 [MIR_PRODUCTION_FEATURE_MATRIX.md](MIR_PRODUCTION_FEATURE_MATRIX.md) 已经全部 Done。这一页记可玩客户端是否**看起来、动起来**像经典热血传奇 / 私服 PC。

素材：

- 世界图集由 `tools/gen_mir_sheet.py` 生成：`client/Legend.Playable/Assets/atlas.png`（原创像素，不是官方 WIL/WZL/WMX，也不是 996 包）。
- **UI 石金壳**来自原型 kit（`open-legend-prototype/assets/chrome/kit` → `client/Legend.Playable/Assets/chrome/`）：阴阳球、聊天框、BR 簇、小地图框、背包/人物整窗、九宫框、按钮四态、物品格。对齐 OL-01 / OL-02 / OL-03 硬门槛，不是 AI 整屏糊脸。

协议仍是 1.21.0。

| 项 | 状态 | 现在的做法 |
|----|------|------------|
| 登录壳 OL-01-A | Done | 单场景氛围底 + 双石金窗（账号登录 / 游戏公告）；禁墙纸 3×3 平铺 |
| 选角 OL-01-C | Done | 石金框 + 职业立绘预览 |
| 主 HUD OL-02 | Done | BL 阴阳合一批 · BC 聊天腰带 · BR 石纹菜单簇 · TR 小地图框；禁浮空灰块 |
| 背包 OL-03 | Done | `bag-window-full` 整窗 + 真物品格图标网格 + 货币行 |
| 人物 OL-03 | Done | 立绘纸娃娃 + 左右环绕装备槽（非绿字列表）；开人物时联排打开背包 |
| 技能窗 OL-04-A | Done | 列表行（图标+名+Lv/Exp 条+F 键）+ 底栏 F1–F8 快捷格；九宫 Framed + kit 格，非 AI 整屏 |
| NPC 商店 OL-06-B | Done | 购买/卖出/修理/特殊 Tab；购买双栏（列表+详情合计）+ 金币行 |
| 仓库 OL-06-E | Done | 仓库/背包双栏石金列表，取出/存入 |
| 交易 OL-08-A | Done | 对方/自己 5×4 格双栏 + 锁定态 + 金币 + 操作钮 |
| 行会 OL-09-A | Done | 公告+列头成员表 + 任命 + 创建/加入/退出 |
| 大地图 OL-05-B | Done | 示意画板（己方金点/传送蓝点）+ 传送点列表 |
| 设置 OL-12-A | Done | 音量/画质/键位/辅助 Tab；扩展入口含邮件/内挂；小退/大退 |
| 组队 OL-07-A | Done | 列头+成员 HP/在线色 + 附近邀请 + 邀请/离队；Alt+W |
| 好友 OL-07-B | Done | 列头+在线排序色讯；黑名单/师徒 Tab；私聊/组队/删除 |
| 右键菜单 OL-07-C | Done | Examine 玩家弹出石金「玩家菜单」（交易/组队/好友等） |
| 任务 OL-11-A/B | Done | 任务窗列表行+进度条；左上角任务追踪 |
| 死亡 OL-10-A | Done | 全屏暗幕 + 石金死亡窗（回城/就地） |
| 安全区 OL-10-B | Done | 进出区顶栏提示条 |
| 名色 OL-10-D | Done | 世界名板色阶 + 名色对照窗 |
| 攻沙顶栏 OL-09-E | Done | 有 SiegeNote 时顶栏条；攻城信息窗 |
| 寄售 OL-08-H | Done | 浏览/我的/上架 Tab + 列头列表行购买/下架 |
| 商城 OL-17-A | Done | 热销等 Tab 网格 + 充值档位示意（元宝购买不接线） |
| 英雄 OL-13-A | Done | 英雄窗 + Ctrl+E；Ctrl+S/`hero_combo` 合击（现有 Attack） |
| 邮件 OL-17-BF | Done | 标题「邮件」；收件/发件/已删；左列表+右读信/附件格；提取/删除/回复(disabled)；全部已读/清空已读；静红点；禁绑 OL-14-* |
| 内挂 OL-16-K | Done | 标题「内挂设置」；打怪/施法/拾取/保护/关于；四分区默认关语感；风险门闩→开启挂机；禁绑 OL-14-* |
| 挂机中 OL-16-N | Done | HUD 小「挂中」+点回设置；克制浮层停挂/开设置；禁霓虹大条 |
| 宝宝 OL-02-I | Done | 外观窗宝宝/坐骑/时装 Tab |
| 地砖 / 八方向 / 特效 | Done | 见既有 atlas 切片 |
| 操作手感 / 音效 | Done | 见既有 Playable 输入与程序波形 |

## 对照原型权威

本地原型目录（未入库）：`/workspace/open-legend-prototype/`

- 北星：`refs/ref-hud-user-sifu.png`
- 硬门槛：`progress/user-reject-rework-20261101.md` §5
- 预览：`prototypes/previews/OL-01-A.png` · `OL-02-A.png` · `OL-03-C.png` 等

## 还没做成官方客户端那种的地方

- 菱形砖是俯视 45°，不是官方地图文件那套带高度遮挡的建筑切片。
- 换装层是武器、衣服、头盔三层原创像素，不是每件装备一张独立高清纸娃娃。
- 真正元宝支付 / 英雄合击数值平衡 / 内挂技能表与药表深度配置：仍浅；邮件与内挂已有石金窗并接线既有协议/本地 FSM，商城购买仍不接线支付。
- 帧数已经按动作加长，仍然是低分辨率原创像素，不是官方上百帧。

VISUAL_PARITY_COMPLETE=true（OL-01–03 核心壳 + OL-04/05-B/06/07/08/09-A/E/10/11/12/13/16/17 + OL-17-BF 邮件 / OL-16-K 内挂 / OL-16-N 挂中对齐；非整窗位图贴图。商城支付仍不接线；合击走既有 hero_combo）

跑客户端：`scripts/verify-playable-client.sh`，Windows 包是 `dist/windows-player/LegendClient.exe`。

- 2026-11-01：第四刀 — 邮件 OL-17-BF / 入口 OL-07-H、内挂 OL-16-K（默认关+风险门）、OL-07/08/09/17 密度加深、英雄合击 UI→`hero_combo`、抽检叠字/调试残片规避。仅客户端；协议 1.21.0。

- 2026-11-01：权威预览落地对照 — OL-17-BF / OL-16-K / OL-16-N（md5 与 prototypes/previews 一致）；邮件双栏+附件 CTA、内挂四分区+门闩、挂机中小标浮层按像素语义加深。禁 OL-14-*。
- 2026-11-01：接线更正 — 邮件权威预览冻结为 **OL-17-BF**（入口可用 OL-07-H）；内挂深页 **OL-16-K**；**禁止**对照 OL-14-A..H（VL 样张）。客户端壳按此重对齐。
