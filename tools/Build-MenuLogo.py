# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-159: the main menu's logo for a game the launcher starts.

The game draws its main-menu logo from Libs/UI/Textures/KCDLogo.dds (1024x512, DXT5, no mips). This builds our own
texture of exactly that shape from docs/branding/KCT_txt.png -- scaled to the same visible box the game's own logo
fills, so it sits where the original sits -- into kdcmp_brand/Libs/UI/Textures/KCDLogo.dds. tools/Publish-Release.ps1
packs it into kdcmp_brand.pak; the launcher puts that pak into the mod's folder only for a game it starts and takes it
out again when that game exits, so a game started any other way keeps the game's own logo.

    python tools/Build-MenuLogo.py [--game-logo <the game's KCDLogo.dds, for its visible box>]

Without --game-logo the box measured on the Modding Tools build 1.5.5 is used (BOX below).
"""
import argparse, os, sys
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "docs", "branding", "KCT_txt.png")
OUT = os.path.join(ROOT, "kdcmp_brand", "Libs", "UI", "Textures", "KCDLogo.dds")
W, H = 1024, 512
BOX = (28, 47, 966, 465)   # the game's own logo, alpha > 8: left, top, right, bottom (measured on 1.5.5)


def alpha_box(im, threshold=8):
    a = im.getchannel("A").point(lambda v: 255 if v > threshold else 0)
    return a.getbbox()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--game-logo")
    ap.add_argument("--out", default=OUT)
    a = ap.parse_args()
    box = BOX
    if a.game_logo:
        box = alpha_box(Image.open(a.game_logo).convert("RGBA"))
        print("the game's logo fills", box)
    src = Image.open(SRC).convert("RGBA")
    src = src.crop(alpha_box(src))
    bw, bh = box[2] - box[0], box[3] - box[1]
    s = min(bw / src.width, bh / src.height)
    logo = src.resize((max(1, round(src.width * s)), max(1, round(src.height * s))), Image.LANCZOS)
    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    x = box[0] + (bw - logo.width) // 2
    y = box[1] + (bh - logo.height) // 2
    canvas.alpha_composite(logo, (x, y))
    os.makedirs(os.path.dirname(a.out), exist_ok=True)
    canvas.save(a.out, pixel_format="DXT5")
    check = Image.open(a.out)
    if check.size != (W, H):
        sys.exit("the written texture is %r, not %r" % (check.size, (W, H)))
    print("wrote %s (%dx%d DXT5, logo %dx%d at %d,%d)" % (os.path.relpath(a.out, ROOT), W, H, logo.width, logo.height, x, y))


if __name__ == "__main__":
    main()
