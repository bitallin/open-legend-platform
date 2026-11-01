# Playable Visual Plan — 可玩一屏生产观感

目标：用户进游戏后一眼「像能玩的传奇」，不再是色块 demo。

## 根因（对照用户截图）

- 内置 `Assets/atlas.png` 早期由 `tools/gen_mir_sheet.py` 以 **16×24 逻辑像素色块**绘制，站立/行走帧在画面上就是蓝/绿/棕方块。
- 地表菱形仅裁剪极简噪声；树/墙极简块。
- HUD 已石金化（PR#53），与色块世界割裂。
- Admin 肖像不可当战斗精灵；世界必须以 MirSheet 动作为准。

## 阶段

### P0 — 默认可玩一屏 ✅

1. `gen_mir_sheet.py` 帧 **48×64**、地砖/特效 **48**；程序底图 + 地砖参考采样。
2. `tools/mir_cutouts.py` + `enrich_atlas_with_cutouts.py`：AI 立绘切帧写入战士/法道/NPC/史莱姆/野猪/骷髅/爬虫/幽魂/龙；道具树/屋；刀光与技能 FX。
3. `WorldCanvas`：脚下阴影、更大精灵目标框、受击闪白、选中环与血条跟随高度；HUD 不回退。
4. 再生 atlas → publish → 同步用户机 `dist/windows-player` + 启动 bat。

### P1 — 八向 / 纸娃娃 / 高度遮挡（本刀）✅ 部分达标

1. **八向**：`dirSlots=8`；`orient_frame` 为每向写入独立帧（剪切/明暗/镜像合成），运行时 `DirSlots>=8` 不再水平翻转凑向，减少左右手穿帮。
2. **纸娃娃**：武器/衣服层默认可见（`SyncGear` 默认 `arm_cloth` + 职业武器；`DrawGear` 叠 `arm_*`/`wep_*`）；AI `mir-gear-layers` 切入装备层。
3. **高度遮挡**：树/墙从地砖通道挪到 `DrawSortedWorld`，与角色按 `DrawX+DrawY` 交错绘制；身后半透明（opacity≈0.45）。
4. 文档 + publish + 同步 windows-player。

### P2 — 仍待

- 真手绘/切片八向（非程序 shear）；全装备槽位换装与他人纸娃娃同步；建筑多瓦片屋顶正确遮挡；WIL 级帧密度。

## 验收

同场景对比：人物/怪可辨识；走动与攻击有帧变化；刀光/受击可见；地表有草/路/石层次；HUD 不乱叠；转圈八向帧有可见差异；自己身上武器/衣服层可见；站树后半透明或被树盖住。

诚实差距：八向仍为 cutout+orient 合成（非完整手绘八向）；纸娃娃仅为武器/衣服一层叠图；遮挡按 iso 深度+半透明近似，未做像素级屋顶裁剪；非官方 WIL。
