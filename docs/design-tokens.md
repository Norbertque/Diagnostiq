# Diagnostiq design tokens

Source: `src/Diagnostiq/Theme/Tokens.xaml`. The app sits on WPF-UI (Fluent), so colors reuse
Fluent's theme brushes, which switch automatically with the Windows light/dark setting.
Tokens only add what Fluent lacks.

## Color

| Role | Brush | Notes |
|---|---|---|
| Page background | `SolidBackgroundFillColorBaseBrush` | Mica on Windows 11 |
| Card | `CardBackgroundFillColorDefaultBrush` + `CardStrokeColorDefaultBrush` 1 px | cards use a stroke, not a shadow |
| Text | `TextFillColorPrimary/Secondary/Tertiary/DisabledBrush` | |
| Accent | `AccentFillColorDefaultBrush`, `TextOnAccentFillColorPrimaryBrush` | follows the Windows accent |

### Status (pills, key states, verdicts)

Foreground on background, measured with Windows 11 Fluent values (translucent backgrounds
composited over the card). AA for small text needs 4.5:1.

| Status | Style | Light | Dark |
|---|---|---|---|
| Pass | `Diag.Pill.Pass` (Success) | 4.76 | 5.57 |
| Warning | `Diag.Pill.Warn` (Caution) | 4.77 | 9.03 |
| Fail | `Diag.Pill.Fail` (Critical) | 4.79 | 6.61 |
| Info / Running | `Diag.Pill.Info` (Attention bg + `Diag.InfoForegroundBrush`) | 6.08 | 5.93 |
| Not tested / Skipped | `Diag.Pill.Neutral` (Neutral bg + secondary text) | 6.09 | 8.39 |

Two deliberate deviations from Fluent:
- Dark-theme attention text `#60CDFF` is 4.41:1, so `ThemeService` swaps `Diag.InfoForegroundBrush` to `#99EBFF`.
- `SystemFillColorNeutralBrush` text is 3.29:1 in light theme; neutral pills use secondary text instead.

Verdicts: **Excellent** = Success, **OK** = Caution, **Needs repair** = Critical (each ≥ 5.2:1 as large text on a card).

## Type (Windows 11 ramp)

`Diag.Text.*` styles: Caption 12/16 · Body 14/20 · BodyStrong 14/20 semibold · Subtitle 20/28 ·
Title 28/36 · TitleLarge 40/52 · Metric 40/52 tabular digits · Display 68/92 tabular · Mono 13.
Faces: Segoe UI Variable Text/Display, falling back to Segoe UI on Windows 10; Cascadia Mono → Consolas.

## Space, shape, depth, motion

- Spacing `Diag.Space.1..8` = 4, 8, 12, 16, 24, 32, 48, 64. Card padding 20, page padding 32/24.
- Radius: control 4, card 8, surface 12, pill 11.
- Elevation: cards = stroke only; `Diag.Elevation.Raised` (blur 24, depth 4, 14 %) for raised surfaces only.
- Motion: fast 83 ms, normal 167 ms, slow 250 ms, slide transitions 300 ms, decelerate spline `0.1,0.9,0.2,1`.

## Checking layouts

Debug builds can render themselves: `Diagnostiq.exe --snapshot out.png --theme dark --size 1280x800`.
