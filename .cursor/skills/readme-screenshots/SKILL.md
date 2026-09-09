---
name: readme-screenshots
description: Create and maintain Fast Action README overlay screenshots from the HTML mock. Use when the overlay UI, layout, appearance, menu bar, or tile chrome changes, or when README stills look stale.
---

# README overlay screenshots

Fast Action is a WinUI 3 Windows overlay. Cloud agents usually cannot run it. README stills are generated from a **faithful HTML mock** so docs stay current without a Windows box.

## Source of truth

| Path | Role |
| --- | --- |
| `docs/overlay-mock/overlay.html` | Visual mock of the overlay (menu bar, grid, settings) |
| `docs/overlay-mock/tile-edit.html` | Visual mock of the shortcut editor (Lucide color) |
| `docs/screenshots/*.png` | Committed stills embedded in `README.md` |
| `scripts/capture_overlay_screenshots.py` | Regenerates the PNGs |

The mock must match `src/FastAction/Overlay/OverlayWindow.xaml` / `TileEditWindow.xaml` and the layout/appearance model:

- Window **hugs the tile grid** (`width: max-content`, no min-width floor). A 3×3 overlay is narrower than 4×4.
- Tiles show **key letter**, **icon**, and **action name**.
- Overlay settings include matrix size, start-key picker, theme/tiles/corners, and **acrylic + opacity**.
- **Lucide color** lives on the **shortcut editor** (`TileEditWindow`), not global appearance.

When you change overlay chrome, tile content, settings, or palette, **update the mock first**, then recapture.

## Views to capture

Serve `docs/overlay-mock` and shoot `#overlay` (or a clip around the overlay plus overflowing flyouts):

| File | URL query |
| --- | --- |
| `docs/screenshots/overlay-4x4.png` | `overlay.html?view=overlay` (default 4×4 from `1`) |
| `docs/screenshots/overlay-3x3.png` | `overlay.html?view=overlay&start=Q&cols=3&rows=3` |
| `docs/screenshots/overlay-settings.png` | `overlay.html?view=settings` |
| `docs/screenshots/overlay-grid-menu.png` | `overlay.html?view=grid-menu` |
| `docs/screenshots/overlay-tile-edit.png` | `tile-edit.html` (shortcut editor + Lucide palette) |

Include recognizable app names/icons so the grid is readable. Do not leave a wide empty frame around a small grid.

## How to capture

Preferred, if Playwright MCP is available:

1. `python -m http.server 8765 --directory docs/overlay-mock`
2. Open each URL above.
3. Screenshot `#overlay` (full page only for `grid-menu` if the flyout clips).
4. Write PNGs to `docs/screenshots/` using the filenames in the table.

Or run:

```bash
python scripts/capture_overlay_screenshots.py
```

(`playwright` Python package + Chromium). After capture, confirm `README.md` still points at those paths.

## README

Keep the hero stills near the top of `README.md`:

- `overlay-4x4.png` — default overlay
- `overlay-3x3.png` — start-key / size change
- `overlay-settings.png` — gear pane
- `overlay-tile-edit.png` — shortcut editor (Lucide color)

Do not commit mock-only experiments into `docs/screenshots/`. Replace the named files in place so git diffs show visual changes.

## Checklist after overlay UI work

- [ ] Mock updated to match XAML (menu, tiles, overlay settings, shortcut editor + Lucide swatches)
- [ ] Width follows column count (3×3 narrower than 4×4)
- [ ] Screenshots regenerated into `docs/screenshots/`
- [ ] README images still resolve
