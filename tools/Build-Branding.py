"""Builds the sized copies of the "Kingdom Come: Together" art (WO-134 Phase 6a).

The maintainer's originals stay in docs/branding/ only (15 MB each; never in the
build): Logo_Filter.png (the square logo, 3256x3256) and Banner_Filter.png (the
banner, 3840x2160). This writes:

  KCDMP_launcher/wwwroot/img/banner-header.jpg  the launcher header: a strip of the
                                                banner that keeps the whole title
                                                ("Kingdom Come" + the TOGETHER plaque)
                                                and the riders, 1920 wide
  KCDMP_launcher/wwwroot/img/logo-128.png       the square logo for the launcher's
                                                status bar
  KCDMP_launcher/app.ico                        the window / taskbar / installer /
                                                shortcut icon, 16..256 px: 48 px and up
                                                are the whole logo; 16, 24 and 32 px are
                                                a close crop of the "KC" letters (the
                                                plaque's lettering cannot be read that
                                                small)
  docs/branding/banner-1280.jpg                 README and tester pages

python tools/Build-Branding.py   (needs Pillow)
"""
import io, os, struct
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
B = os.path.join(ROOT, 'docs', 'branding')
logo = Image.open(os.path.join(B, 'Logo_Filter.png')).convert('RGBA')
banner = Image.open(os.path.join(B, 'Banner_Filter.png')).convert('RGB')

# ---- the launcher header: rows 14%..62% of the banner (the title sits at 25%..52%)
W, H = banner.size
strip = banner.crop((0, int(H * 0.14), W, int(H * 0.62)))
strip = strip.resize((1920, round(1920 * strip.size[1] / strip.size[0])), Image.LANCZOS)
out = os.path.join(ROOT, 'KCDMP_launcher', 'wwwroot', 'img', 'banner-header.jpg')
strip.save(out, 'JPEG', quality=84, optimize=True, progressive=True)
print(out, strip.size, os.path.getsize(out), 'bytes')

# ---- README / tester pages
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
# The "KC" letters alone: x 13%..87%, y 16%..60% of the logo, centred on a dark
# square of the logo's own night-blue (a square crop around them would take in the
# top of the plaque, unreadable at these sizes).
kc_rect = logo.crop((int(L * 0.13), int(L * 0.16), int(L * 0.87), int(L * 0.60)))
def small(size):
    sq = Image.new('RGBA', (size, size), (28, 32, 46, 255))
    w = size; h = max(1, round(size * kc_rect.size[1] / kc_rect.size[0]))
    letters = kc_rect.resize((w, h), Image.LANCZOS)
    sq.alpha_composite(letters, (0, (size - h) // 2))
    return sq
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
