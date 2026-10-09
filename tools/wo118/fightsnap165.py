#!/usr/bin/env python3
"""WO-165 0.2 -- the keeping measure for fight snapping: MP-FIGHTSNAP p50/p90/max, jumps excluded.

One MP-FIGHTSNAP line = one puppet, one window (<= 10 s, ending at the line's stamp). A window is EXCLUDED when it
contains a load, a fast travel, a mount/dismount, a teleport, or any single applied step > 20 m (the line's own
step_max_cm > 2000). The rest is reported over two populations:

  holds  -- windows with at least one hold (resume_max_cm is only defined after a hold): THE keeping population
  fight  -- windows with fight_frames > 0 (engaged, a swing hold or its blend)

Inputs: the DLL log (kcdmp-native.log, wall clock), optionally the agent log (wall clock) and the game log (kcd.log,
Lua t= stamps; mapped to the wall clock by the WO124-LOAD / "loads the host's world" pairs, else not used).

  python fightsnap165.py --native kcdmp-native.log [--agent agent.log] [--kcd kcd.log] [--json out.json] [--npc NAME]

The output counts and percentiles only; no log line is copied.
"""
import argparse
import json
import re
import sys

STEP_JUMP_CM = 2000.0       # "any single step > 20 m"
TELEPORT_JUMP_M = 20.0      # a ghost TELEPORT over 20 m is a jump (5-20 m TELEPORTs are fight/gallop catch-ups: counted, not excluded)
LOAD_TAIL_S = 10.0          # puppets re-bind for a while after a load ends

SNAP = re.compile(r"^\[(\d\d):(\d\d):(\d\d)\.(\d{3})\] t\d+ MP-FIGHTSNAP (.*)$")
KV = re.compile(r"(\w+)=(\S+)")
AGENT_TS = re.compile(r"^(\d\d):(\d\d):(\d\d)\.(\d{3}) ")
LUA_T = re.compile(r" t=(\d+\.\d+)")


def wall(h, m, s, ms):
    return int(h) * 3600 + int(m) * 60 + int(s) + int(ms) / 1000.0


def pct(vals, p):
    """Nearest-rank percentile (the one fight118 and the WO-162 notes use)."""
    if not vals:
        return None
    v = sorted(vals)
    k = max(0, min(len(v) - 1, int(-(-p * len(v) // 100)) - 1))
    return v[k]


def parse_snaps(lines):
    out = []
    prev = None
    day = 0.0
    for ln in lines:
        m = SNAP.match(ln.rstrip("\n"))
        if not m:
            continue
        t = wall(*m.groups()[:4]) + day
        if prev is not None and t < prev - 43200:   # midnight wrap
            day += 86400.0
            t += 86400.0
        prev = t
        kv = dict(KV.findall(m.group(5)))
        try:
            rec = {
                "end": t,
                "start": t - float(kv.get("window_s", "10")),
                "npc": kv.get("npc", "?"),
                "holds": int(kv.get("holds", "0")),
                "fight_frames": int(kv.get("fight_frames", "0")),
                "resume_max_cm": float(kv.get("resume_max_cm", "0")),
                "post_hold_step_max_cm": float(kv.get("post_hold_step_max_cm", "0")),
                "step_max_cm": float(kv.get("step_max_cm", "0")),
            }
        except ValueError:
            continue
        out.append(rec)
    return out


def agent_events(lines):
    """(kind, t0, t1) intervals from the agent log; also the pairs that map the game log's Lua t to the wall clock."""
    ev = []
    load_open = None
    anchors = []
    for ln in lines:
        m = AGENT_TS.match(ln)
        if not m:
            continue
        t = wall(*m.groups())
        if "local state -> paused: load" in ln:
            if load_open is None:
                load_open = t
        elif "local state -> running" in ln and load_open is not None:
            ev.append(("load", load_open, t + LOAD_TAIL_S))
            load_open = None
        elif "loads the host's world" in ln:
            anchors.append(t)
            ev.append(("load", t, t + LOAD_TAIL_S))
        elif " loaded: command" in ln or "level loaded" in ln:
            ev.append(("load", t - 1.0, t + LOAD_TAIL_S))
        elif "is flying" in ln:
            ev.append(("teleport", t - 1.0, t + 1.0))
        elif " ride: " in ln or "dismount" in ln.lower():
            ev.append(("mount", t - 2.0, t + 2.0))
        elif "fast travel" in ln.lower() and ("started" in ln or "ended" in ln):
            ev.append(("fasttravel", t - 2.0, t + 5.0))
    if load_open is not None:
        ev.append(("load", load_open, load_open + 600.0))
    return ev, anchors


def kcd_events(lines, anchors):
    """Events from the game log, on the Lua clock; mapped with the WO124-LOAD anchors (offset = wall - t, median)."""
    lua_anchor = []
    raw = []
    for ln in lines:
        if "WO124-LOAD" in ln:
            m = LUA_T.search(ln)
            if m:
                lua_anchor.append(float(m.group(1)))
            continue
        m = LUA_T.search(ln)
        if not m:
            continue
        t = float(m.group(1))
        if "] TELEPORT " in ln:
            d = re.search(r"dist=([\d.]+)", ln)
            if d and float(d.group(1)) > TELEPORT_JUMP_M:
                raw.append(("teleport", t - 1.0, t + 2.0))
        elif "FastTravel: started" in ln or "FastTravel: ended" in ln:
            raw.append(("fasttravel", t - 2.0, t + 5.0))
        elif re.search(r"RIDE |_ride |MountNPCOnHorse|[Dd]ismount|OnMount|ForceMount", ln):
            raw.append(("mount", t - 2.0, t + 2.0))
    n = min(len(lua_anchor), len(anchors))
    if n == 0:
        return [], None
    offs = sorted(anchors[i] - lua_anchor[i] for i in range(n))
    off = offs[len(offs) // 2]
    spread = offs[-1] - offs[0]
    return [(k, a + off, b + off) for (k, a, b) in raw], {"offset_s": round(off, 3), "anchors": n, "spread_s": round(spread, 3)}


def classify(snaps, events):
    ev = sorted(events, key=lambda e: e[1])
    for s in snaps:
        why = None
        if max(s["step_max_cm"], s["resume_max_cm"], s["post_hold_step_max_cm"]) > STEP_JUMP_CM:
            why = "step>20m"   # the applied step, the resume step or the post-hold step: each is one frame's step
        else:
            for kind, a, b in ev:
                if a > s["end"]:
                    break
                if b >= s["start"]:
                    why = kind
                    break
        s["excluded"] = why
    return snaps


def summarize(snaps):
    def block(rows):
        r = [x["resume_max_cm"] for x in rows]
        p = [x["post_hold_step_max_cm"] for x in rows]
        return {
            "windows": len(rows),
            "resume_max_cm": {"p50": pct(r, 50), "p90": pct(r, 90), "max": max(r) if r else None},
            "post_hold_step_max_cm": {"p50": pct(p, 50), "p90": pct(p, 90), "max": max(p) if p else None},
        }

    kept = [s for s in snaps if not s["excluded"]]
    excl = {}
    for s in snaps:
        if s["excluded"]:
            excl[s["excluded"]] = excl.get(s["excluded"], 0) + 1
    all_r = [s["resume_max_cm"] for s in snaps if s["holds"] > 0]
    return {
        "lines": len(snaps),
        "excluded": excl,
        "kept": len(kept),
        "holds": block([s for s in kept if s["holds"] > 0]),
        "fight": block([s for s in kept if s["fight_frames"] > 0]),
        "unfiltered_holds_resume_max_cm_max": max(all_r) if all_r else None,
    }


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[1])
    ap.add_argument("--native", required=True)
    ap.add_argument("--agent")
    ap.add_argument("--kcd")
    ap.add_argument("--npc", help="only this puppet")
    ap.add_argument("--json")
    a = ap.parse_args(argv)

    def read(p):
        with open(p, encoding="utf-8", errors="replace") as f:
            return f.readlines()

    snaps = parse_snaps(read(a.native))
    if a.npc:
        snaps = [s for s in snaps if s["npc"] == a.npc]
    events, anchors = agent_events(read(a.agent)) if a.agent else ([], [])
    mapping = None
    if a.kcd:
        kev, mapping = kcd_events(read(a.kcd), anchors)
        events += kev
    res = summarize(classify(snaps, events))
    res["events"] = {k: sum(1 for e in events if e[0] == k) for k in sorted({e[0] for e in events})}
    res["kcd_clock"] = mapping
    txt = json.dumps(res, indent=2)
    print(txt)
    if a.json:
        with open(a.json, "w", encoding="utf-8") as f:
            f.write(txt)
    return 0


if __name__ == "__main__":
    sys.exit(main())
