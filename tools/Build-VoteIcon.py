# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-203 I.4: the vote icon -- the partner's vote shown in a row of the dialogue's own option list.

Drawn here from plain shapes (no font, no image, no other game's art): a small round badge, a dark disc in a warm
metal rim, with a cream speech bubble holding an opening quotation mark. The game's option rows draw a row's icon from
Libs/UI/Textures/Icons/SkillChecks/<name>_icon.dds for any name that is not a perk, skill or stat (the HUD's own
GetSkillCheckFolderAndName), so the mod's row name kct_vote shows this file; the kct_ prefix keeps it from ever shadowing
a file of the game's. 64 x 64 like the game's own row icons, DXT5, no mips.

    python tools/Build-VoteIcon.py [--out kdcmp/Data/Libs/UI/Textures/Icons/SkillChecks] [--preview <png>]
"""
import argparse, os
import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SIZE = 64
SS = 8                      # drawn at 8x, then reduced
N = SIZE * SS


def disc(draw, cx, cy, r, fill):
    draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=fill)


def radial(size, inner, outer, cx, cy, r):
    """An RGBA layer: inner colour at the centre fading to outer at radius r (alpha 255 inside r)."""
    y, x = np.mgrid[0:size, 0:size].astype(np.float32)
    d = np.clip(np.hypot(x - cx, y - cy) / r, 0.0, 1.0)[..., None]
    rgb = np.array(inner, np.float32) * (1 - d) + np.array(outer, np.float32) * d
    a = np.full((size, size, 1), 255.0, np.float32)
    return Image.fromarray(np.concatenate([rgb, a], axis=2).astype(np.uint8), 'RGBA')


def mask_circle(size, cx, cy, r):
    m = Image.new('L', (size, size), 0)
    ImageDraw.Draw(m).ellipse([cx - r, cy - r, cx + r, cy + r], fill=255)
    return m


def draw_icon():
    c = N / 2.0
    out = Image.new('RGBA', (N, N), (0, 0, 0, 0))

    # the rim: a warm metal ring, lit from the top left
    r_out = N * 0.47
    r_in = N * 0.40
    rim = radial(N, (236, 214, 160), (122, 92, 52), c - N * 0.18, c - N * 0.20, N * 0.75)
    out.paste(rim, (0, 0), mask_circle(N, c, c, r_out))
    # a thin dark line between the rim and the disc
    dark = Image.new('RGBA', (N, N), (28, 22, 16, 255))
    out.paste(dark, (0, 0), mask_circle(N, c, c, r_in + N * 0.012))
    # the disc: dark, a little lighter towards the top
    face = radial(N, (64, 56, 46), (24, 20, 16), c - N * 0.06, c - N * 0.14, N * 0.48)
    out.paste(face, (0, 0), mask_circle(N, c, c, r_in))

    # the speech bubble: a rounded body with a tail at the lower left
    bub = Image.new('L', (N, N), 0)
    bd = ImageDraw.Draw(bub)
    bx0, by0, bx1, by1 = c - N * 0.25, c - N * 0.21, c + N * 0.25, c + N * 0.15
    bd.rounded_rectangle([bx0, by0, bx1, by1], radius=N * 0.11, fill=255)
    tail = [(c - N * 0.15, by1 - N * 0.02), (c - N * 0.20, by1 + N * 0.12), (c - N * 0.02, by1 - N * 0.02)]
    bd.polygon(tail, fill=255)
    cream = Image.new('RGBA', (N, N), (240, 231, 208, 255))
    # a soft shadow under the bubble first
    shadow = Image.new('RGBA', (N, N), (0, 0, 0, 150))
    sh = Image.new('L', (N, N), 0)
    sh.paste(bub, (int(N * 0.012), int(N * 0.018)))
    out.paste(shadow, (0, 0), sh)
    out.paste(cream, (0, 0), bub)

    # the opening quotation mark inside: two drops, each a round head with a tail curling down to the left
    qm = Image.new('L', (N, N), 0)
    qd = ImageDraw.Draw(qm)
    head_r = N * 0.052
    for dx in (-N * 0.075, N * 0.075):
        hx, hy = c + dx, c - N * 0.005
        disc(qd, hx, hy, head_r, 255)
        qd.polygon([(hx - head_r, hy), (hx + head_r * 0.15, hy - head_r * 0.9),
                    (hx - head_r * 0.35, hy - head_r * 2.4), (hx - head_r * 1.15, hy - head_r * 1.2)], fill=255)
    ink = Image.new('RGBA', (N, N), (58, 44, 30, 255))
    out.paste(ink, (0, 0), qm)
    return out


def reduce(im, size):
    a = np.asarray(im, np.float32) / 255.0
    rgb, alpha = a[..., :3] * a[..., 3:4], a[..., 3:4]
    pm = Image.fromarray((np.concatenate([rgb, alpha], 2) * 255).astype(np.uint8), 'RGBA').resize((size, size), Image.LANCZOS)
    b = np.asarray(pm, np.float32) / 255.0
    al = b[..., 3:4]
    col = np.where(al > 1e-4, b[..., :3] / np.maximum(al, 1e-4), 0)
    return Image.fromarray((np.clip(np.concatenate([col, al], 2), 0, 1) * 255).astype(np.uint8), 'RGBA')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', default=os.path.join(ROOT, 'kdcmp', 'Data', 'Libs', 'UI', 'Textures', 'Icons', 'SkillChecks'))
    ap.add_argument('--preview', default=None)
    a = ap.parse_args()
    icon = reduce(draw_icon(), SIZE)
    os.makedirs(a.out, exist_ok=True)
    path = os.path.join(a.out, 'kct_vote_icon.dds')
    icon.save(path, pixel_format='DXT5')
    print('wrote', path, os.path.getsize(path), 'bytes', icon.size)
    if a.preview:
        big = icon.resize((256, 256), Image.NEAREST)
        sheet = Image.new('RGBA', (256 + 64 + 24, 256), (70, 64, 56, 255))
        sheet.paste(big, (0, 0), big)
        sheet.paste(icon, (256 + 12, 96), icon)
        sheet.save(a.preview)
        print('preview', a.preview)


if __name__ == '__main__':
    main()
