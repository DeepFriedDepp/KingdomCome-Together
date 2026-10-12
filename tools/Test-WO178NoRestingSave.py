# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-178 Part 0: no code path the mod ships may save through Game.SaveGameViaResting().

WO-177 A2: a resting save written in the field world before the feud quest's meadow fight carried a weapon lock in its file. The mod's
saves go the Saviour Schnapps way (the engine's ManualSave through the DLL). This check fails (exit 1) on any call of
SaveGameViaResting in the shipped Lua, agent, server or launcher sources; comments may name it. The synthetic test scenarios stub
the function to prove it is NOT called, so tools/ is not scanned. The build runs it (tools/Build-Installer.ps1 gates).

    python3 -I tools/Test-WO178NoRestingSave.py [repo root]
"""
import os
import re
import sys

CALL = re.compile(r"SaveGameViaResting\s*[()]|pcall\s*\(\s*Game\.SaveGameViaResting")
ROOTS = ["kdcmp", "dotnet/KcdMp.Client", "dotnet/KcdMp.Server", "dotnet/KcdMp.Protocol", "KCDMP_launcher", "native/KCDMP"]
SKIP_DIRS = {"bin", "obj", ".vs"}


def strip(line, ext):
    if ext == ".lua":
        i = line.find("--")
        return line if i < 0 else line[:i]
    t = line.lstrip()
    if t.startswith("//") or t.startswith("*"):
        return ""
    i = line.find("//")
    return line if i < 0 else line[:i]


def main():
    root = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), ".."))
    hits, files = [], 0
    for r in ROOTS:
        base = os.path.join(root, r)
        for dp, dns, fns in os.walk(base):
            dns[:] = [d for d in dns if d not in SKIP_DIRS]
            for fn in fns:
                ext = os.path.splitext(fn)[1].lower()
                if ext not in (".lua", ".cs", ".cpp", ".h", ".razor"):
                    continue
                p = os.path.join(dp, fn)
                files += 1
                with open(p, encoding="utf-8", errors="replace") as f:
                    for n, line in enumerate(f, 1):
                        if CALL.search(strip(line, ext)):
                            hits.append("%s:%d: %s" % (os.path.relpath(p, root), n, line.strip()[:120]))
    for h in hits:
        print("RESTING-SAVE CALL " + h)
    print("RESULT: %d files scanned, %d resting-save calls (must be 0)" % (files, len(hits)))
    return 1 if hits else 0


if __name__ == "__main__":
    sys.exit(main())
