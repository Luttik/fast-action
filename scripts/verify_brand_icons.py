"""Verify FastAction brand marks have transparent corners (no white filler).

Used by CI to keep WinGet/Store-style icons compliant with transparent rounded
assets (Microsoft unplated / tray icon expectations).
"""

from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image

REPO = Path(__file__).resolve().parent.parent
BRAND = REPO / "src" / "FastAction" / "Assets" / "Brand"

REQUIRED = [
    "mark-16.png",
    "mark-32.png",
    "mark-64.png",
    "mark-256.png",
    "tray-32.png",
    "tray-32-light.png",
]


def corner_ok(path: Path) -> tuple[bool, str]:
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    samples = [
        im.getpixel((0, 0)),
        im.getpixel((w - 1, 0)),
        im.getpixel((0, h - 1)),
        im.getpixel((w - 1, h - 1)),
    ]
    for rgba in samples:
        r, g, b, a = rgba
        if a != 0:
            return False, f"opaque corner rgba={rgba}"
        if r > 200 and g > 200 and b > 200:
            return False, f"light RGB left in corner rgba={rgba}"
    return True, "ok"


def main() -> int:
    failed = False
    for name in REQUIRED:
        path = BRAND / name
        if not path.exists():
            print(f"FAIL {name}: missing")
            failed = True
            continue
        ok, detail = corner_ok(path)
        print(f"{'PASS' if ok else 'FAIL'} {name}: {detail}")
        failed = failed or not ok
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
