from pathlib import Path

from PIL import Image

brand = Path(r"c:\workspace\fast-action\src\FastAction\Assets\Brand")
assets = Path(r"c:\workspace\fast-action\src\FastAction\Assets")

src256 = Image.open(brand / "mark-256.png").convert("RGBA")
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
    if path and path.exists():
        img = Image.open(path).convert("RGBA")
        if img.size != (size, size):
            img = img.resize((size, size), Image.Resampling.LANCZOS)
    else:
        img = src256.resize((size, size), Image.Resampling.LANCZOS)
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

src256.resize((32, 32), Image.Resampling.LANCZOS).save(brand / "tray-32.png")
print(f"Wrote {ico_path} ({ico_path.stat().st_size} bytes)")
print(f"ICO reports size {Image.open(ico_path).size}")
