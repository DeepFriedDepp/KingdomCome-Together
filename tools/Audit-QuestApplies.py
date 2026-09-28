#!/usr/bin/env python3
"""WO-137 Phase 6 (hazard H4) -- vet the quest ports shared quests applies.

A shared-quests apply is the State's own Set<Value> port (native wo137.cpp): the
engine runs every consumer of the new value, exactly as the game does when the
player makes that step. An apply cannot be undone (H4: no setter, no inverse), so
each port applied in a session is walked here with the WO-97 audit's graph model
(tools/Audit-ObjectiveFixHazards.py: its Pak, hazard classes and edge rules) and
every hazard-class node on the consumer chain of "entering <Value>" is listed.

    python tools/Audit-QuestApplies.py --log agent.log [--log agent2.log]   # every applied port in the logs
    python tools/Audit-QuestApplies.py <conceptPath> <SetPort> [...]         # named ports
    python tools/Audit-QuestApplies.py --pak <Scripts.pak> ...               # (default: found like WO-97's)

Log lines read (the agent's own):
    MP-W137 joiner applied host change #<seq> <path> <port> <old>-><new>
    MP-W137 host: request #<n> from ghost <g>: <path> <port> <old>-><new>: APPLIED ...
A path maps to its module file (Quests/Final/<path minus its last segment>.xml,
case-insensitively) and the State node (the last segment). Reads Scripts.pak only.
"""
import argparse
import importlib.util
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("wo97", os.path.join(HERE, "Audit-ObjectiveFixHazards.py"))
wo97 = importlib.util.module_from_spec(spec)
spec.loader.exec_module(wo97)

APPLIED = [
    re.compile(r"MP-W137 joiner applied host change #\d+ (?P<path>Barbora\.\S+) (?P<port>\w+) "),
    re.compile(r"MP-W137 host: request #\d+ from ghost \d+: (?P<path>Barbora\.\S+) (?P<port>\w+) \S+: APPLIED"),
]


def _find(pak, suffix):
    suffix = suffix.lower()
    for k in pak.names:
        if k.lower().endswith(suffix):
            return k
    return None


def module_file(pak, path):
    """Barbora.trosecko.hledaniPsa.h.x.state -> (pak key of .../hledaniPsa/h/x.xml, 'state').

    A segment can be an INSTANCE of a module defined elsewhere (the node's tag names the module,
    e.g. <vyptavani Name="vyptavani_1"> in rybar_a_hospodsky.xml): the longest prefix that is a
    file is taken, then each remaining segment is followed through its parent's node tag.
    """
    segs = path.split(".")
    state = segs[-1]
    key, i = None, len(segs) - 1
    while i > 2:
        key = _find(pak, "quests/final/" + "/".join(segs[:i]) + ".xml")
        if key:
            break
        i -= 1
    if not key:
        return None, state
    for seg in segs[i:-1]:
        root = pak.tree(key)
        node = next((e for e in root.iter() if e.get("Name") == seg), None) if root is not None else None
        if node is None:
            return None, state
        ck = pak.child(key, node.tag)
        if not ck:   # a shared module: the file named after the tag, under the same quest
            quest_dir = "/".join(key.split("/")[:5])
            ck = next((k for k in pak.names if k.lower().endswith("/" + node.tag.lower() + ".xml")
                       and k.startswith(quest_dir)), None)
        if not ck:
            return None, state
        key = ck
    return key, state


def walk(pak, key, state, value, maxdepth=8, logic=True):
    """The WO-97 breadth-first walk, for the consumers of entering <value> (logic=False: the WO-97 model exactly)."""
    ports = ["On" + value, value]
    seen, out = set(), []
    frontier = [(key, state + "." + p, "pulse" if p.startswith("On") else "bool", 0) for p in ports]
    while frontier:
        fkey, spec_, kind, depth = frontier.pop(0)
        if depth > maxdepth or (fkey, spec_) in seen:
            continue
        seen.add((fkey, spec_))
        for tag, name, to in pak.edges_from(fkey, spec_):
            out.append((depth, kind, fkey, tag, name, to, wo97.hazards_of(tag, name, to)))
            if tag == "Output":
                pk = pak.parent(fkey)
                if pk:
                    frontier.append((pk, fkey.rsplit("/", 1)[1][:-4] + "." + to, kind, depth + 1))
                continue
            ck = pak.child(fkey, tag)
            if ck:
                frontier.append((ck, to, kind, depth + 1))
                continue
            if tag == "State" and to and to.startswith("Set"):
                frontier.append((fkey, name + ".On" + to[3:], "pulse", depth + 1))
                frontier.append((fkey, name + "." + to[3:], "bool", depth + 1))
                continue
            # WO-137: through the logic nodes too (the WO-97 model stops at them, which
            # under-reports: the live bandit cascade runs through an IfFunction).
            if not logic or not name:
                continue
            if tag in ("If", "IfFunction") and to == "Exec":
                frontier.append((fkey, name + ".True", "pulse", depth + 1))
                frontier.append((fkey, name + ".False", "pulse", depth + 1))
            elif tag == "Function" and to == "Exec":
                frontier.append((fkey, name + ".OnExec", "pulse", depth + 1))
            elif tag == "Timer" and to in ("SetRunning", "Start", "Restart"):
                frontier.append((fkey, name + ".OnFinished", "pulse", depth + 1))
            elif tag in ("Function", "IfFunction") and kind == "bool":
                frontier.append((fkey, name + ".bool", "bool", depth + 1))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pak", default="")
    ap.add_argument("--log", action="append", default=[])
    ap.add_argument("--all", action="store_true", help="print every consumer edge, not only hazard-class ones")
    ap.add_argument("--wo97", action="store_true", help="the WO-97 model exactly (no logic nodes, depth 6)")
    ap.add_argument("pairs", nargs="*", help="<conceptPath> <SetPort> ...")
    a = ap.parse_args()
    pak_path = a.pak or wo97.find_pak()
    if not pak_path or not os.path.exists(pak_path):
        sys.exit("Scripts.pak not found; pass --pak")
    pak = wo97.Pak(pak_path)
    todo = []
    for i in range(0, len(a.pairs) - 1, 2):
        todo.append((a.pairs[i], a.pairs[i + 1]))
    for lp in a.log:
        with open(lp, encoding="utf-8", errors="replace") as f:
            for line in f:
                for rx in APPLIED:
                    m = rx.search(line)
                    if m:
                        todo.append((m.group("path"), m.group("port")))
    seen, hz_total = set(), 0
    print("=== WO-137 applied-port audit (H4) ===")
    print("    pak   : %s" % pak_path)
    for path, port in todo:
        if (path, port) in seen:
            continue
        seen.add((path, port))
        key, state = module_file(pak, path)
        value = port[3:] if port.startswith("Set") else port
        print("=" * 92)
        print("%s :: %s" % (path, port))
        if not key:
            print("  !! no module file for this path in the pak")
            continue
        rows = walk(pak, key, state, value, 6, False) if a.wo97 else walk(pak, key, state, value)
        shown = rows if a.all else [r for r in rows if r[6]]
        if not shown:
            print("  no hazard-class node in %d consumer edges" % len(rows))
        for depth, kind, fkey, tag, name, to, hz in shown:
            hz_total += 1 if hz else 0
            print("  d%d %-5s %-36s <%s Name=%s> -> %s   %s"
                  % (depth, kind, fkey.rsplit("/", 1)[1][:36], tag, name, to, ",".join(hz)))
    print("\n%d port(s) audited, %d hazard-class consumer node(s)." % (len(seen), hz_total))
    return 0


if __name__ == "__main__":
    sys.exit(main())
