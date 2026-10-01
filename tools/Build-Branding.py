# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""Builds the sized copies of the "Kingdom Come: Together" art (WO-134 Phase 6a).

The maintainer's originals stay in docs/branding/ only (15 MB each; never in the
build): Logo_Filter.png (the square logo, 3256x3256) and Banner_Filter.png (the
banner, 3840x2160; also the launcher's background, the maintainer's choice). This
writes:

  KCDMP_launcher/wwwroot/img/background.jpg     the launcher's page background,
                                                1920x1080 (replaces kcd2_bg.jpg; the
                                                title above the server list is text)
  KCDMP_launcher/wwwroot/img/logo-128.png       the square logo for the launcher's
                                                status bar
  KCDMP_launcher/app.ico                        the window / taskbar / installer /
                                                shortcut icon, 16..256 px: 48 px and up
                                                are the whole logo; 16, 24 and 32 px are
                                                a close crop of the "KC" letters (the
                                                plaque's lettering cannot be read that
                                                small)
  docs/branding/banner-1280.jpg                 the tester pages
  docs/branding/KCT_txt-900.png                 the README's header: the maintainer's text
                                                logo (KCT_txt.png), 900 wide, transparent

python tools/Build-Branding.py   (needs Pillow)
"""
import io, os, struct
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
B = os.path.join(ROOT, 'docs', 'branding')
logo = Image.open(os.path.join(B, 'Logo_Filter.png')).convert('RGBA')
banner = Image.open(os.path.join(B, 'Banner_Filter.png')).convert('RGB')
background = banner   # the maintainer's choice for the launcher (Banner3_Filter-background.png before)

# ---- the launcher's page background
bg = background.resize((1920, 1080), Image.LANCZOS)
out = os.path.join(ROOT, 'KCDMP_launcher', 'wwwroot', 'img', 'background.jpg')
bg.save(out, 'JPEG', quality=82, optimize=True, progressive=True)
print(out, bg.size, os.path.getsize(out), 'bytes')

# ---- the README's header: the text logo, transparency kept
txt = Image.open(os.path.join(B, 'KCT_txt.png')).convert('RGBA')
txt = txt.resize((900, round(900 * txt.size[1] / txt.size[0])), Image.LANCZOS)
out = os.path.join(B, 'KCT_txt-900.png')
txt.save(out, 'PNG', optimize=True)
print(out, txt.size, os.path.getsize(out), 'bytes')

# ---- tester pages
web = banner.resize((1280, 720), Image.LANCZOS)
out = os.path.join(B, 'banner-1280.jpg')
web.save(out, 'JPEG', quality=82, optimize=True, progressive=True)
print(out, web.size, os.path.getsize(out), 'bytes')

# ---- the status-bar logo
out = os.path.join(ROOT, 'KCDMP_launcher', 'wwwroot', 'img', 'logo-128.png')
logo.resize((128, 128), Image.LANCZOS).save(out, 'PNG', optimize=True)
print(out, os.path.getsize(out), 'bytes')

# ---- the icon
L = logo.size[0]
# The "KC" letters alone (measured: the white letters span x 14%..86%, y 17%..61%
# of the logo), with margin, centred on a dark square of the logo's own
# night-blue at 84% of its width: edge to edge they were clipped on the taskbar
# (maintainer's report). A square crop around them would take in the top of the
# plaque, unreadable at these sizes.
kc_rect = logo.crop((int(L * 0.10), int(L * 0.13), int(L * 0.90), int(L * 0.65)))
def small(size):
    from PIL import ImageFilter, ImageOps
    # the background: the same crop, blurred, covering the square (no hard band)
    big = 256
    cover = ImageOps.fit(kc_rect, (big, big), Image.LANCZOS).filter(ImageFilter.GaussianBlur(24))
    bg = Image.alpha_composite(cover, Image.new("RGBA", (big, big), (20, 24, 36, 200)))
    # the letters: only the white pixels of the crop, at 84% of the width, centred
    w = round(big * 0.84); h = round(w * kc_rect.size[1] / kc_rect.size[0])
    letters = kc_rect.resize((w, h), Image.LANCZOS)
    lum = letters.convert('L').point(lambda v: 0 if v < 170 else min(255, (v - 170) * 4))
    white = Image.new('RGBA', (w, h), (255, 255, 255, 255))
    bg.paste(white, ((big - w) // 2, (big - h) // 2), lum)
    return bg.resize((size, size), Image.LANCZOS)
entries = []
for size in (16, 24, 32, 48, 64, 128, 256):
    im = small(size) if size <= 32 else logo.resize((size, size), Image.LANCZOS)
    buf = io.BytesIO(); im.save(buf, 'PNG', optimize=True)
    entries.append((size, buf.getvalue()))
out = os.path.join(ROOT, 'KCDMP_launcher', 'app.ico')
with open(out, 'wb') as f:
    f.write(struct.pack('<HHH', 0, 1, len(entries)))
    off = 6 + 16 * len(entries)
    for size, data in entries:
        f.write(struct.pack('<BBBBHHII', size % 256, size % 256, 0, 0, 1, 32, len(data), off))
        off += len(data)
    for _, data in entries:
        f.write(data)
print(out, [s for s, _ in entries], os.path.getsize(out), 'bytes')
# previews of the small sizes (not shipped)
prev = Image.new('RGBA', (16 + 24 + 32 + 48 + 40, 48), (40, 40, 40, 255))
x = 4
for size, data in entries[:4]:
    prev.paste(Image.open(io.BytesIO(data)), (x, 0)); x += size + 8
prev.resize((prev.size[0] * 4, prev.size[1] * 4), Image.NEAREST).save(os.path.join(os.environ.get('TEMP', '.'), 'wo134-icon-preview.png'))
