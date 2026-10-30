# LegendClient Audio（Phase K）

本目录音效为 **程序生成的极短 WAV 占位**（正弦 beep / 简单和弦），**原创自研**，非任何闭源传奇或商业资源包。

| 文件 | 用途 |
|------|------|
| `login_bgm.wav` | 登录界面 BGM 循环 |
| `world_bgm.wav` | 进世界 BGM 循环 |
| `ui_click.wav` | UI 按钮点击 |
| `attack.wav` | 发起攻击 |
| `hit.wav` | 受击 / 飘血 |
| `pickup.wav` | 拾取 |

## 替换方式

1. 用同名文件覆盖（保持 16-bit PCM WAV 即可）。
2. 或在 Unity 中把 `AudioClip` 赋给运行时加载路径：`Resources` 不必；`LegendAudioService` 优先 `Resources.Load` 失败后走 `StreamingAssets` / 程序生成。
3. 推荐替换为明确 **CC0 / 自绘** 素材；**禁止**导入官方传奇、996 引擎闭源包内音效。

运行时若找不到磁盘资源，`LegendProceduralAudio` 会再生成等价 beep，保证无资源也能出声。
