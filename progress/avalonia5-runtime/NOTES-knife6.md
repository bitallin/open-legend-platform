# Avalonia5 runtime capture — knife6 (2026-11-01 Asia/Shanghai)

## Shots (runtime vs preview)
Dir: `progress/avalonia5-runtime/` (also mirrored under legend-platform)

| ID | Runtime | Preview |
|----|---------|---------|
| OL-07-D | OL-07-D.png | OL-07-D-preview.png |
| OL-09-D | OL-09-D.png | OL-09-D-preview.png |
| OL-17-BF | OL-17-BF.png | OL-17-BF-preview.png |
| OL-08-J | OL-08-J.png | OL-08-J-preview.png |
| OL-16-N | OL-16-N.png | OL-16-N-preview.png |
| OL-17-B | OL-17-B.png | OL-17-B-preview.png |
| OL-16-K | OL-16-K.png | OL-16-K-preview.png |

Capture path: `LegendClient.exe --ui-capture <dir>` offline SeedUiCapture (no trial login required).

## Runtime fixes in this knife
1. `MirChrome.Framed` middle row Star→Auto — stop EdgeV stretching to full HUD (叠层/竖条伪影).
2. Assist float only when `_openPanel` empty and no party invite — stop overlay on guild/mail/stall/mall.
3. Guild member list ScrollViewer MaxHeight 280 — near-full 198/200 no longer blows panel / floating kick.
4. `--ui-capture` seed + shot runner for expert round-2.

## Expert round-2
Please re-diff the seven runtime PNGs vs `*-preview.png`. User acceptance still gated until expert OK.
