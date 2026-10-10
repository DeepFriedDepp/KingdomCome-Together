#!/usr/bin/env python3
"""WO-166 C4 -- the field's large fight snaps, classified by the three causes WO-161 located (docs/WO-161-findings.md 0.2).

A window = one MP-FIGHTSNAP line (one puppet, <= 10 s). Selected: fight_frames > 0 and resume_max_cm >= 50, and none of the
jump exclusions of fightsnap165.py (a single step > 20 m). Each selected window is put in exactly one class, by what the DLL's own
lines say about it (the native log only: the snap line itself, and the copy's WO121-MOTION combat-state lines in the same window):

  A  combat state applied on arrival   the copy's combat state changed inside the window (combat mode start / block / released)
                                       while no hold of a whole swing ran (hold_ms_max < 900): the state came before the drawn body
  C  hold-resume                       a hold of a swing or longer (hold_ms_max >= 900) ended in the large resume: the host's NPC moved
                                       on while its copy was held (chained swing holds: up to 4.6 s in the field)
  B  pose / placement (swing on arrival)  the rest: a large resume after a short hold -- the body itself was moved during the hold (the
                                       swing played on arrival, its own motion), not left behind by the stream

  python snapcause166.py <kcdmp-native.log> [more logs ...]   -> per log and total: counts and shares (no log line is copied)
"""
import re
import sys

SNAP = re.compile(r"^\[(\d\d):(\d\d):(\d\d)\.(\d{3})\] t\d+ MP-FIGHTSNAP (.*)$")
MOTION = re.compile(r"^\[(\d\d):(\d\d):(\d\d)\.(\d{3})\] t\d+ WO121-MOTION body=(\S+) (combat mode start|block=|combat mode released|combat automation)")
KV = re.compile(r"(\w+)=(\S+)")
JUMP_CM = 2000.0


def wall(h, m, s, ms):
    return int(h) * 3600 + int(m) * 60 + int(s) + int(ms) / 1000.0


def classify(path):
    snaps, motion = [], {}
    with open(path, encoding="utf-8", errors="replace") as f:
        for ln in f:
            m = SNAP.match(ln.rstrip("\n"))
            if m:
                kv = dict(KV.findall(m.group(5)))
                snaps.append((wall(*m.groups()[:4]), kv))
                continue
            m = MOTION.match(ln)
            if m:
                motion.setdefault(m.group(5).lower(), []).append(wall(*m.groups()[:4]))
    out = {"windows": 0, "large": 0, "A": 0, "B": 0, "C": 0, "excluded": 0}
    for t, kv in snaps:
        try:
            ff = int(kv.get("fight_frames", "0"))
            resume = float(kv.get("resume_max_cm", "0"))
            step = float(kv.get("step_max_cm", "0"))
            post = float(kv.get("post_hold_step_max_cm", "0"))
            hold = float(kv.get("hold_ms_max", "0"))
            win = float(kv.get("window_s", "10"))
        except ValueError:
            continue
        if ff <= 0:
            continue
        out["windows"] += 1
        if max(step, resume, post) > JUMP_CM:
            out["excluded"] += 1
            continue
        if resume < 50.0:
            continue
        out["large"] += 1
        npc = kv.get("npc", "").lower()
        state_change = any(t - win <= x <= t for x in motion.get(npc, []))
        if state_change and hold < 900.0:
            out["A"] += 1
        elif hold >= 900.0:
            out["C"] += 1
        else:
            out["B"] += 1
    return out


def main():
    tot = {"windows": 0, "large": 0, "A": 0, "B": 0, "C": 0, "excluded": 0}
    for i, p in enumerate(sys.argv[1:]):
        o = classify(p)
        for k in tot:
            tot[k] += o[k]
        n = max(1, o["large"])
        print(f"log{i + 1}: fight_windows={o['windows']} excluded_jumps={o['excluded']} large={o['large']} "
              f"A={o['A']} ({100 * o['A'] / n:.0f}%) B={o['B']} ({100 * o['B'] / n:.0f}%) C={o['C']} ({100 * o['C'] / n:.0f}%)")
    n = max(1, tot["large"])
    print(f"total: fight_windows={tot['windows']} excluded_jumps={tot['excluded']} large={tot['large']} "
          f"A={tot['A']} ({100 * tot['A'] / n:.0f}%) B={tot['B']} ({100 * tot['B'] / n:.0f}%) C={tot['C']} ({100 * tot['C'] / n:.0f}%)")


if __name__ == "__main__":
    main()
