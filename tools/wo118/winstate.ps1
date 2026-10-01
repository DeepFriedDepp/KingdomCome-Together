# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
# WO-118 harness: minimize the game window, or restore it and bring it to the
# foreground. KCD2 runs at ~26 fps (its background frame limiter) whenever the
# window is not in front, which changes every per-frame number.
param([ValidateSet('min', 'restore')][string]$state = 'restore')
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class W118 {
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@
$g = Get-Process KingdomCome -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $g) { exit 1 }
if ($state -eq 'min') { [void][W118]::ShowWindow($g.MainWindowHandle, 6) }
else { [void][W118]::ShowWindow($g.MainWindowHandle, 9); [void][W118]::SetForegroundWindow($g.MainWindowHandle) }
