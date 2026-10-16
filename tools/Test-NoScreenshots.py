#!/usr/bin/env python3
# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-192 gate: nothing that ships takes a screenshot.

    python tools/Test-NoScreenshots.py <dir-or-file>...

WO-172 made a mark (mark_odd) take the game's own screenshot; that left image files on a tester's PC they never took. WO-192
removed it. This gate scans every shipped file (exe, dll, pak, Lua, scripts, configs) byte-wise, in ASCII and UTF-16, inside
zip/pak archives too, for the game's screenshot console commands and the OS screen-capture APIs. Every hit is listed; the gate
passes only with zero hits. Dev-only harness scripts under tools/ are never shipped and are not scanned. An exemption must be
listed in ALLOWED by file name with the reason, and only for Microsoft's own runtime files (none of our code calls them).
"""
import io
import os
import re
import sys
import zipfile

# the game's console: r_GetScreenShot (any case), capture_frames (a screenshot every frame); the OS: GDI+ / Win32 / DXGI / WinRT capture
PATTERNS = [
    (re.compile(rb'getscreenshot', re.I), 'r_GetScreenShot (the game\'s screenshot command)'),
    (re.compile(rb'capture_frames', re.I), 'capture_frames (the game\'s frame capture)'),
    (re.compile(rb'CopyFromScreen'), 'Graphics.CopyFromScreen'),
    (re.compile(rb'PrintWindow'), 'PrintWindow'),
    (re.compile(rb'DuplicateOutput'), 'IDXGIOutput1::DuplicateOutput (desktop duplication)'),
    (re.compile(rb'GraphicsCaptureItem'), 'Windows.Graphics.Capture'),
]
_MS = 'Microsoft .NET Desktop runtime (framework API, not called by this project)'
ALLOWED = {n: _MS for n in (
    'System.Windows.Forms.dll', 'System.Windows.Forms.Primitives.dll', 'System.Windows.Forms.Design.dll',
    'System.Drawing.Common.dll', 'System.Drawing.dll', 'PresentationCore.dll', 'PresentationFramework.dll',
    'Microsoft.VisualBasic.Forms.dll', 'UIAutomationTypes.dll', 'UIAutomationClient.dll', 'UIAutomationClientSideProviders.dll',
    'Microsoft.Web.WebView2.WinForms.dll', 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.Wpf.dll',
    'Microsoft.AspNetCore.Components.WebView.WindowsForms.dll', 'Microsoft.AspNetCore.Components.WebView.Wpf.dll')}
_MS_MARK = (b'Microsoft Corporation', 'Microsoft Corporation'.encode('utf-16-le'))


def _utf16(rx):
    # the same pattern as UTF-16 LE text (letters interleaved with NUL)
    src = rx.pattern.decode()
    flags = rx.flags
    parts = []
    for ch in src:
        parts.append(re.escape(ch).encode() + b'\\x00' if ch.isalnum() or ch == '_' else re.escape(ch).encode())
    return re.compile(b''.join(parts), flags)


PATTERNS16 = [(_utf16(rx), why) for rx, why in PATTERNS]


def scan_bytes(name, data, hits):
    for (rx, why), (rx16, _) in zip(PATTERNS, PATTERNS16):
        if rx.search(data) or rx16.search(data):
            hits.append((name, why))
    if data[:2] == b'PK':
        try:
            with zipfile.ZipFile(io.BytesIO(data)) as z:
                for n in z.namelist():
                    scan_bytes(name + '!' + n, z.read(n), hits)
        except zipfile.BadZipFile:
            pass


def main(argv):
    roots = argv or []
    if not roots:
        print('usage: Test-NoScreenshots.py <dir-or-file>...')
        return 2
    hits, files, microsoft = [], 0, set()
    for r in roots:
        paths = [r] if os.path.isfile(r) else [os.path.join(d, f) for d, _, fs in os.walk(r) for f in fs]
        for p in paths:
            files += 1
            with open(p, 'rb') as f:
                data = f.read()
            name = os.path.relpath(p, r) if os.path.isdir(r) else os.path.basename(p)
            if os.path.basename(p) in ALLOWED and any(m in data for m in _MS_MARK):
                microsoft.add(name)
            scan_bytes(name, data, hits)
    real = [(n, a) for n, a in hits if n.split('!')[0] not in microsoft]
    for n, a in hits:
        tag = 'ALLOWED (%s)' % ALLOWED[os.path.basename(n.split('!')[0])] if (n, a) not in real else 'HIT'
        print('%s  %s  %s' % (tag, n, a))
    print('no-screenshot gate: %d files scanned, %d hit(s)%s' % (files, len(real), '' if real else ' -- PASS'))
    return 1 if real else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
