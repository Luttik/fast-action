"""Regenerate README overlay screenshots from docs/overlay-mock."""

from __future__ import annotations

import http.server
import threading
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MOCK_DIR = ROOT / "docs" / "overlay-mock"
OUT_DIR = ROOT / "docs" / "screenshots"
PORT = 8766

SHOTS: list[tuple[str, str, str]] = [
    ("overlay.html?view=overlay", "overlay-4x4.png", "#overlay"),
    ("overlay.html?view=overlay&start=Q&cols=3&rows=3", "overlay-3x3.png", "#overlay"),
    ("overlay.html?view=settings", "overlay-settings.png", "#overlay"),
    ("overlay.html?view=grid-menu", "overlay-grid-menu.png", ""),
    ("tile-edit.html", "overlay-tile-edit.png", "#editor"),
]


class _Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args: object, **kwargs: object) -> None:
        super().__init__(*args, directory=str(MOCK_DIR), **kwargs)

    def log_message(self, format: str, *args: object) -> None:
        del format, args


def _union_clip(
    first: dict[str, float],
    second: dict[str, float],
    pad: float = 24,
) -> dict[str, float]:
    left = min(first["x"], second["x"]) - pad
    top = min(first["y"], second["y"]) - pad
    right = max(first["x"] + first["width"], second["x"] + second["width"]) + pad
    bottom = max(first["y"] + first["height"], second["y"] + second["height"]) + pad
    x = max(left, 0.0)
    y = max(top, 0.0)
    return {"x": x, "y": y, "width": right - x, "height": bottom - y}


def _serve() -> http.server.ThreadingHTTPServer:
    server = http.server.ThreadingHTTPServer(("127.0.0.1", PORT), _Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    return server


def main() -> None:
    try:
        from playwright.sync_api import sync_playwright
    except ImportError as exc:
        raise SystemExit(
            "Install Playwright: pip install playwright && playwright install chromium"
        ) from exc

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    server = _serve()
    try:
        with sync_playwright() as playwright:
            browser = playwright.chromium.launch()
            page = browser.new_page(viewport={"width": 1100, "height": 900})
            for query, name, clip in SHOTS:
                page.goto(f"http://127.0.0.1:{PORT}/{query}", wait_until="domcontentloaded")
                page.wait_for_timeout(250)
                dest = OUT_DIR / name
                if clip:
                    page.locator(clip).screenshot(path=str(dest), type="png")
                else:
                    overlay = page.locator("#overlay").bounding_box()
                    flyout = page.locator("#sizeFly").bounding_box()
                    if overlay and flyout:
                        page.screenshot(
                            path=str(dest),
                            type="png",
                            clip=_union_clip(overlay, flyout),
                        )
                    else:
                        page.screenshot(path=str(dest), type="png")
                print(f"wrote {dest.relative_to(ROOT)}")
            browser.close()
    finally:
        server.shutdown()


if __name__ == "__main__":
    main()
