"""Generates every icon/tile the package needs from one source logo (black glyph on white).

    python -I installer\\make-assets.py installer\\logo-source.jpg

Outputs (overwrites in place):
  DiscordBar\\Assets\\*              app tiles, splash, Store logo, taskbar icons
  DiscordBar\\GameBar\\Icons\\*      Game Bar home-bar icons (monochrome: white for dark theme, black for light)
  installer\\logo-glyph.png         the extracted glyph with transparency (master for anything else)
"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageOps

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "DiscordBar" / "Assets"
GB_ICONS = ROOT / "DiscordBar" / "GameBar" / "Icons"
WHITE = (255, 255, 255, 255)
BLACK = (0, 0, 0, 255)


def extract_glyph(src: Path) -> Image.Image:
    """Black-on-white source -> alpha mask of the dark pixels, cropped square with no padding."""
    g = Image.open(src).convert("L")
    # Dark pixels are the glyph; white (and the white eyes/mouth) become transparent.
    mask = g.point(lambda v: 255 if v < 128 else 0).filter(ImageFilter.GaussianBlur(0.6))
    mask = mask.point(lambda v: 255 if v > 110 else (0 if v < 40 else v))
    bbox = mask.getbbox()
    mask = mask.crop(bbox)
    w, h = mask.size
    side = max(w, h)
    square = Image.new("L", (side, side), 0)
    square.paste(mask, ((side - w) // 2, (side - h) // 2))
    return square


def glyph_rgba(mask: Image.Image, color) -> Image.Image:
    img = Image.new("RGBA", mask.size, color[:3] + (0,))
    img.putalpha(mask)
    return img


def fit(mask: Image.Image, box: int) -> Image.Image:
    return mask.resize((box, box), Image.LANCZOS)


def mono_icon(mask: Image.Image, size: int, color) -> Image.Image:
    """Glyph on transparent, filling ~88% of the box (Game Bar home-bar style)."""
    box = max(1, round(size * 0.88))
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.alpha_composite(glyph_rgba(fit(mask, box), color), ((size - box) // 2, (size - box) // 2))
    return out


def tile(mask: Image.Image, w: int, h: int, bg, fg, fill=0.6, rounded=False) -> Image.Image:
    """Glyph centred on a solid background (the supplied look: black mark on white)."""
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    if rounded:
        r = round(min(w, h) * 0.18)
        d = ImageDraw.Draw(out)
        d.rounded_rectangle((0, 0, w - 1, h - 1), radius=r, fill=bg)
    else:
        out = Image.new("RGBA", (w, h), bg)
    box = round(min(w, h) * fill)
    out.alpha_composite(glyph_rgba(fit(mask, box), fg), ((w - box) // 2, (h - box) // 2))
    return out


def main(src: str) -> None:
    mask = extract_glyph(Path(src))
    ASSETS.mkdir(parents=True, exist_ok=True)
    GB_ICONS.mkdir(parents=True, exist_ok=True)
    glyph_rgba(mask, BLACK).save(ROOT / "installer" / "logo-glyph.png")

    # Game Bar home bar: monochrome glyph, no background. Dark theme = white, light theme = black.
    for s in (16, 20, 24, 32, 44, 256):
        mono_icon(mask, s, WHITE).save(GB_ICONS / f"icon.targetsize-{s}.png")
        mono_icon(mask, s, BLACK).save(GB_ICONS / f"icon.light.targetsize-{s}.png")

    # Tiles and Store logo: black glyph on white, as supplied.
    for name, (w, h) in {
        "Square150x150Logo.png": (150, 150), "Square150x150Logo.scale-200.png": (300, 300),
        "Square44x44Logo.png": (44, 44), "Square44x44Logo.scale-200.png": (88, 88),
        "StoreLogo.png": (50, 50), "StoreLogo.scale-200.png": (100, 100),
        "Wide310x150Logo.png": (310, 150), "Wide310x150Logo.scale-200.png": (620, 300),
    }.items():
        tile(mask, w, h, WHITE, BLACK).save(ASSETS / name)
    # Taskbar / Start list (unplated): white rounded badge with the black glyph, readable on both themes.
    for s in (48, 256):
        tile(mask, s, s, WHITE, BLACK, fill=0.7, rounded=True).save(ASSETS / f"Square44x44Logo.targetsize-{s}_altform-unplated.png")
    # Splash: white glyph on transparent; the manifest paints #313338 behind it.
    for name, (w, h) in {"SplashScreen.png": (620, 300), "SplashScreen.scale-200.png": (1240, 600)}.items():
        out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        box = round(h * 0.55)
        out.alpha_composite(glyph_rgba(fit(mask, box), WHITE), ((w - box) // 2, (h - box) // 2))
        out.save(ASSETS / name)

    # Contact sheet for a quick visual check.
    sheet = Image.new("RGBA", (900, 420), (49, 51, 56, 255))
    x = 10
    for p in ("Square150x150Logo.png", "Wide310x150Logo.png", "StoreLogo.png", "Square44x44Logo.targetsize-48_altform-unplated.png"):
        im = Image.open(ASSETS / p)
        sheet.alpha_composite(im, (x, 10)); x += im.width + 10
    x = 10
    for s in (16, 20, 24, 32, 44, 256):
        im = Image.open(GB_ICONS / f"icon.targetsize-{s}.png")
        sheet.alpha_composite(im, (x, 180)); x += im.width + 12
    light = Image.new("RGBA", (900, 60), (240, 240, 240, 255))
    x = 10
    for s in (16, 20, 24, 32, 44):
        im = Image.open(GB_ICONS / f"icon.light.targetsize-{s}.png")
        light.alpha_composite(im, (x, 8)); x += im.width + 12
    sheet.alpha_composite(light, (0, 360))
    sheet.save(ROOT / "installer" / "asset-sheet.png")
    print("assets written; review installer\\asset-sheet.png")


if __name__ == "__main__":
    main(sys.argv[1])
