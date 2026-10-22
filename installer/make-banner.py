"""Builds docs/images/banner.png and docs/images/logo.png from installer/logo-glyph.png for the README."""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "docs" / "images"
OUT.mkdir(parents=True, exist_ok=True)

glyph = Image.open(ROOT / "installer" / "logo-glyph.png").convert("RGBA")
alpha = glyph.getchannel("A")

# logo.png: the mark in white on a dark rounded tile (reads well on GitHub's light and dark themes).
tile = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
ImageDraw.Draw(tile).rounded_rectangle((0, 0, 255, 255), radius=48, fill=(49, 51, 56, 255))
mark = Image.new("RGBA", (176, 176), (255, 255, 255, 0))
mark.putalpha(alpha.resize((176, 176), Image.LANCZOS))
tile.alpha_composite(mark, (40, 40))
tile.save(OUT / "logo.png")

# banner.png: mark + name on the app's own dark background.
W, H = 1400, 320
banner = Image.new("RGBA", (W, H), (49, 51, 56, 255))
m = Image.new("RGBA", (200, 200), (255, 255, 255, 0))
m.putalpha(alpha.resize((200, 200), Image.LANCZOS))
banner.alpha_composite(m, (70, 60))
d = ImageDraw.Draw(banner)
try:
    big = ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf", 56)
    small = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 26)
except OSError:
    big = small = ImageFont.load_default()
d.text((320, 100), "Unofficial Discord for Gamebar", font=big, fill=(255, 255, 255, 255))
d.text((322, 178), "Discord's web app as an Xbox Game Bar widget for handhelds", font=small, fill=(200, 203, 210, 255))
d.text((322, 214), "Not affiliated with Discord Inc.", font=small, fill=(150, 153, 160, 255))
banner.save(OUT / "banner.png")
print("docs/images/banner.png and logo.png written")
