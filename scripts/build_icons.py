"""Regenerate FastAction's app icon and Windows logo assets from the brand mark.

Run after updating `src/FastAction/Assets/Brand/mark-*.png`:

    python scripts/build_icons.py
"""

from __future__ import annotations

from collections import deque
from pathlib import Path

from PIL import Image

repo_root = Path(__file__).resolve().parent.parent
brand = repo_root / "src" / "FastAction" / "Assets" / "Brand"
assets = repo_root / "src" / "FastAction" / "Assets"

# Approximate brand palette (sampled from mark-256).
NAVY = (15, 23, 41)
TILE = (36, 45, 61)
TEAL = (36, 188, 168)
PLAY = (255, 255, 255)
LIGHT_SHELL = (242, 245, 247)
LIGHT_TILE = (220, 226, 234)


def color_dist(a: tuple[int, int, int], b: tuple[int, int, int]) -> int:
    return abs(a[0] - b[0]) + abs(a[1] - b[1]) + abs(a[2] - b[2])


def make_corners_transparent(im: Image.Image, threshold: int = 96) -> Image.Image:
    """Flood-fill light corner filler to alpha=0 (keeps the white play triangle)."""
    im = im.convert("RGBA")
    w, h = im.size
    px = im.load()
    assert px is not None

    seeds = [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)]
    bg = px[0, 0][:3]
    # Only strip if the corners are a light filler, not the icon shell itself.
    if sum(bg) < 500:
        return im

    # Approximate dark shell under the white matte for edge decontamination.
    shell = NAVY

    visited = [[False] * w for _ in range(h)]
    queue: deque[tuple[int, int]] = deque()
    for x, y in seeds:
        r, g, b, a = px[x, y]
        if a > 0 and color_dist((r, g, b), bg) <= threshold:
            queue.append((x, y))
            visited[y][x] = True

    while queue:
        x, y = queue.popleft()
        r, g, b, _ = px[x, y]
        rgb = (r, g, b)
        dist = color_dist(rgb, bg)

        if dist <= 28:
            px[x, y] = (0, 0, 0, 0)
        else:
            # Remove white matte: recover dark shell + coverage alpha.
            alphas: list[float] = []
            for i in range(3):
                denom = bg[i] - shell[i]
                if abs(denom) < 1:
                    continue
                alphas.append(1.0 - ((rgb[i] - shell[i]) / denom))
            coverage = max(0.0, min(1.0, sum(alphas) / len(alphas))) if alphas else 0.0
            if coverage < 0.08:
                px[x, y] = (0, 0, 0, 0)
            else:
                px[x, y] = (*shell, int(round(255 * coverage)))

        for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            if nx < 0 or ny < 0 or nx >= w or ny >= h or visited[ny][nx]:
                continue
            nr, ng, nb, na = px[nx, ny]
            if na == 0:
                visited[ny][nx] = True
                continue
            # Keep expanding through light fringe / matte blends, but stop at solid shell.
            nd = color_dist((nr, ng, nb), bg)
            brightness = nr + ng + nb
            if nd <= threshold or (brightness > 120 and nd < 180):
                visited[ny][nx] = True
                queue.append((nx, ny))

    return im


def invert_for_dark_mode(im: Image.Image) -> Image.Image:
    """Build a light-shell mark that reads clearly on dark taskbars."""
    im = im.convert("RGBA")
    w, h = im.size
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    src = im.load()
    dst = out.load()
    assert src is not None and dst is not None

    for y in range(h):
        for x in range(w):
            r, g, b, a = src[x, y]
            if a == 0:
                continue
            rgb = (r, g, b)
            # Preserve teal accent and play glyph; remap dark shell/tiles to light.
            if color_dist(rgb, TEAL) < 90:
                dst[x, y] = (*TEAL, a)
            elif color_dist(rgb, PLAY) < 40:
                dst[x, y] = (*NAVY, a)
            elif color_dist(rgb, TILE) < 55 or (sum(rgb) < 140 and max(rgb) - min(rgb) < 40):
                # Mid tiles / near-navy body → light grey tiles or shell.
                if color_dist(rgb, NAVY) < 35 or sum(rgb) < 70:
                    dst[x, y] = (*LIGHT_SHELL, a)
                else:
                    dst[x, y] = (*LIGHT_TILE, a)
            else:
                # Fallback: invert luminance while keeping alpha.
                inv = (255 - r, 255 - g, 255 - b)
                dst[x, y] = (*inv, a)

    return out


def load_mark(size: int, path: Path | None) -> Image.Image:
    if path and path.exists():
        img = Image.open(path).convert("RGBA")
        if img.size != (size, size):
            img = img.resize((size, size), Image.Resampling.LANCZOS)
    else:
        img = src256.resize((size, size), Image.Resampling.LANCZOS)
    return make_corners_transparent(img)


# Source marks (may still have light corner filler — we strip it).
raw256 = Image.open(brand / "mark-256.png").convert("RGBA")
src256 = make_corners_transparent(raw256)
src256.save(brand / "mark-256.png")

# Keep mark-512 in sync if present.
if (brand / "mark-512.png").exists():
    make_corners_transparent(Image.open(brand / "mark-512.png")).save(brand / "mark-512.png")

sizes = {
    16: brand / "mark-16.png",
    32: brand / "mark-32.png",
    48: None,
    64: brand / "mark-64.png",
    128: None,
    256: brand / "mark-256.png",
}

images: list[Image.Image] = []
for size, path in sizes.items():
    img = load_mark(size, path)
    if path is not None:
        img.save(path)
    images.append(img)

ico_path = assets / "AppIcon.ico"
images[-1].save(
    ico_path,
    format="ICO",
    sizes=[(im.width, im.height) for im in images],
    append_images=images[:-1],
)

for name, size in [
    ("StoreLogo.png", 50),
    ("Square44x44Logo.scale-200.png", 88),
    ("Square150x150Logo.scale-200.png", 300),
    ("LockScreenLogo.scale-200.png", 48),
    ("Wide310x150Logo.scale-200.png", (620, 300)),
    ("SplashScreen.scale-200.png", (1240, 600)),
]:
    out = assets / name
    if isinstance(size, tuple):
        w, h = size
        canvas = Image.new("RGBA", (w, h), (15, 23, 42, 255))
        mark = src256.resize((min(w, h) * 2 // 3, min(w, h) * 2 // 3), Image.Resampling.LANCZOS)
        canvas.paste(mark, ((w - mark.width) // 2, (h - mark.height) // 2), mark)
        canvas.save(out)
    else:
        src256.resize((size, size), Image.Resampling.LANCZOS).save(out)

# Unplated / tray variants
src256.resize((24, 24), Image.Resampling.LANCZOS).save(
    assets / "Square44x44Logo.targetsize-24_altform-unplated.png"
)
src256.resize((48, 48), Image.Resampling.LANCZOS).save(
    assets / "Square44x44Logo.targetsize-48_altform-lightunplated.png"
)
tray = src256.resize((32, 32), Image.Resampling.LANCZOS)
tray.save(brand / "tray-32.png")

# Dark-mode tray: largely inverted (light shell) with transparent corners.
light256 = invert_for_dark_mode(src256)
light256.save(brand / "mark-256-light.png")
tray_light = light256.resize((32, 32), Image.Resampling.LANCZOS)
tray_light.save(brand / "tray-32-light.png")

print(f"Wrote {ico_path} ({ico_path.stat().st_size} bytes)")
print(f"ICO reports size {Image.open(ico_path).size}")
print(f"Corner alpha after fix: {src256.getpixel((0, 0))}")
print(f"Light tray saved: {brand / 'tray-32-light.png'}")
