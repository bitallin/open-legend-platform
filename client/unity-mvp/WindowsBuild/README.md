# Windows Player 打包（Phase K）

输出目录默认：`dist/windows-player/LegendClient.exe`。

## Unity 菜单

`Legend → Build → Windows Player`

## 批处理

```bash
# 在仓库根目录
export UNITY_EDITOR="/path/to/Unity"   # 或 UNITY_PATH
./scripts/build-windows-player.sh
```

`-executeMethod Legend.Build.Windows.BuildPlayer`

详见 [`docs/PLAYABLE_PC.md`](../../../docs/PLAYABLE_PC.md)。
