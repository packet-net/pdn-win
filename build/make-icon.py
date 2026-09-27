"""Draws the app icon: an amber pulse on the station page's near-black, in a rounded square.

Run from the repo root: python build/make-icon.py
"""
from PIL import Image, ImageDraw, ImageFilter

BG = (17, 22, 29, 255)
EDGE = (42, 52, 65, 255)
AMBER = (245, 168, 60, 255)
SIZE = 1024


def draw() -> Image.Image:
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pad = 64
    d.rounded_rectangle([pad, pad, SIZE - pad, SIZE - pad], radius=190, fill=BG, outline=EDGE, width=14)

    # The wordmark's pulse, scaled up: flat, a dip, a tall spike, a fall, flat.
    s = SIZE / 1024
    pts = [(200, 540), (360, 540), (430, 360), (520, 720), (610, 430), (660, 540), (824, 540)]
    pts = [(x * s, y * s) for x, y in pts]

    glow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ImageDraw.Draw(glow).line(pts, fill=(245, 168, 60, 150), width=int(70 * s), joint="curve")
    glow = glow.filter(ImageFilter.GaussianBlur(38 * s))
    img = Image.alpha_composite(img, glow)
    d = ImageDraw.Draw(img)
    d.line(pts, fill=AMBER, width=int(46 * s), joint="curve")
    for x, y in (pts[0], pts[-1]):
        r = 23 * s
        d.ellipse([x - r, y - r, x + r, y + r], fill=AMBER)
    return img


if __name__ == "__main__":
    import os

    icon = draw()
    icon.save("src/PdnWin/Assets/pdn-win.png")
    icon.save("src/PdnWin/Assets/pdn-win.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])

    # The Linux package's icon theme sizes (packaging/linux/build-deb.sh installs each one).
    os.makedirs("packaging/linux/icons", exist_ok=True)
    for size in (16, 24, 32, 48, 64, 128, 256, 512):
        icon.resize((size, size), Image.LANCZOS).save(f"packaging/linux/icons/{size}.png")
