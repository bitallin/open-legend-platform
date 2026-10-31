# Admin UI 视觉规范（M11）

> 时区：Asia/Shanghai · 面向生产级传奇运营控制台

## 设计北星

- **信息密度**：接近 Linear / Stripe 运营台，而非游戏 HUD 或学生作业表格。
- **图像优先**：内容列表默认卡片网格；缩略图 ≥160px；透明棋盘格底。
- **层级清晰**：Sticky 顶栏 + 可折叠侧栏 + 宽内容区（max ≈1680）。
- **中文界面**；对比度按深色 WCAG 近似目标（正文 `--text` on `--bg`）。
- **可选石金强调**：`--accent: #c9a227`，用于主按钮 / active / 健康 chip。

## Design Tokens

定义于 `wwwroot/css/admin.css` `:root`：

| Token | 用途 |
|-------|------|
| `--bg / --panel / --panel-2` | 页面与卡片表面 |
| `--border / --border-strong` | 分割与悬停边 |
| `--text / --muted / --faint` | 文字层级 |
| `--accent` | 石金主色 |
| `--ok / --warn / --danger / --info` | 状态 |
| `--checker` | 透明图棋盘格 |
| `--content-max` | 1680px |
| `--sidebar-w` | 248px（折叠 64px） |

## 布局壳

- `_Layout.cshtml`：`app-shell` + sticky `app-topbar`（折叠、标题、全局搜、用户 chip）+ 图标侧栏。
- 侧栏折叠状态写入 `localStorage` key `ol-admin-sidebar`。

## 页面模式

1. **Studio `/Studio`**：Hero + KPI + 素材墙（大缩略图）+ 实体卡片板；支持 kind / 缺图过滤。
2. **实体列表**：默认卡片；可切表格；缺图虚线 CTA。
3. **素材库**：拖拽上传区、类型色标、多选 sticky 批量条。
4. **编辑页**：左 sticky 预览 / 右字段；底部 sticky 保存。
5. **UI 皮肤**：槽位可视化板（中文名 + 空框）。
6. **登录**：居中品牌面板。

## 组件约定

- 主按钮：`.btn-primary`（石金渐变）
- 缺图：`.empty-art` / `.ecard.missing`
- Lightbox：点击 `.lightbox-src`
- 不要回退到「表格 + 邮票缩略图」作为主视图

## 自检清单

- [ ] Studio / Assets / Monsters / UiSkin / Login 目视不再像原型表
- [ ] CRUD 仍可用；关键路由 200
- [ ] 宽屏内容区充分利用；侧栏可折叠
