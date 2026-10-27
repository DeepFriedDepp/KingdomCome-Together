# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-206 Part C: find the first moment the host's and the joiner's worlds disagree, from their checkpoint digests.

  python tools/digest/analyze.py <host bundle or dir> <joiner bundle or dir> [--out report.md] [--write-briefings] [--json]

A bundle is a folder or a .tar.zst / .tar archive; digest.jsonl, applied.jsonl and BRIEFING.md are found by name anywhere
inside (the shallowest copy wins). Archives are read through a temporary extraction; with --write-briefings an archive is
extracted into a sibling folder <name>.extracted/ and that copy's BRIEFING.md gets the section (the archive is never
modified). Checkpoints are aligned by (cat, epoch, seq); bucket sets are compared first, and only a differing checkpoint
has its reconstructed maps diffed to name keys. Offline test tool, never shipped; reads files only.

Exit: 0 no unexplained divergence, 1 at least one, 2 bad input (a side with zero digest rows is bad input: a hollow run
is not a pass).
"""
import argparse
import json
import os
import re
import subprocess
import sys
import tarfile
import tempfile
from bisect import bisect_right
from datetime import datetime, timezone
from statistics import median

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from digest import buckets  # noqa: E402

CATS = ("quest", "dead", "loot", "clock", "join")
IN_FLIGHT_RULE = True      # a difference that clears by the next checkpoint with an applied row on the lagging side
CLOCK_EDGE_RULE = True     # the clock one 15-minute step apart for one checkpoint (the joiner a few seconds off at a step's edge)
CLOCK_STEP_MIN = 15        # the agent's Wo206Digest.ClockValue step
CLOCK_EDGE_MAX = 2         # checkpoints an edge may last: the game clock runs 15x (a step = 60 real s), the joiner's follower a few
                           # real seconds off spans two 5 s checkpoints (c1r: six edges of exactly two)
KNOWN_LOCAL_RULE = True    # WO-207 D.1: the named clocks below run on the host alone by design, and what they drive is mirrored
# (key prefix, why). A clock is one of these only when its digest value is a clock's ("clk:"); a State under the same prefix is not.
KNOWN_LOCAL = (
    ("Barbora.trosecko.fogControl.",
     "known-local: the morning-fog clocks run on the host only (the joiner's clock pulses are quiet by design, WO-200); the fog they "
     "drive -- the fogControl_* game profiles -- reaches the joiner through the profile mirror (WO-189): on 0.6-2.5 s after the "
     "host's 05:00 and off after its 07:00 on the bench (WO-207, d4-dread)"),
)
# WO-208 D.2: the State mirror (WO-137) records a change only under a quest node -- wo137.cpp record(): "world simulation (event
# places, professions, ...): not under a C_Quest". The digest's keyspace is every State and clock of the quest FILES, so it also holds
# the concepts the game declares as <Gameplay> or <Module> (the tutorials, the open-world combat chatter, a random event's state, the
# fog machine): each machine runs its own, by that design, and they differ where the players' own play differs. A key whose top concept
# file (Scripts.pak Quests/Final/Barbora/.../<name>.xml, its first node after <Database>) is not a <Quest> is that, by the game's own
# files; with the files unreadable the rule stays off (fail closed: the key stays unexplained).
NOT_A_QUEST_RULE = True
# WO-209 D.4: a quest State on one side only at its initial value (sv:0) for the whole episode, its node not loaded on the other side --
# no value was ever set on either machine (WO-208's soak: eight Kuttenberg States at sv:0 on the joiner from its first checkpoint, absent
# on the host in the Trosky region, all 351 checkpoints). A value set later (sv:0 -> sv:1) or the other side holding a value is not this.
DEFAULT_ONLY_RULE = True
# WO-209 D.4: a quest timer both machines run, a step apart for a while and then the same again (clk: on both sides, cleared) -- the
# game's quest timers are not C_StateVariables and are never mirrored (WO-198), each machine runs its own, and the States they set are
# mirrored (WO-200's out-port replay). The smoke's inn-brawl timers: a step apart for 5-111 checkpoints. A timer on one machine only
# (the Jaunt's shape, WO-207) and a State differing are never this.
TIMER_LOCAL_RULE = True
# WO-209 D.4: an applied row whose "before" is the initial value (sv:0) on a side where the key was absent (its node asleep or not yet
# loaded there; WO-209's mp_quest_wake_with_host wakes it for this very change) -- absent reads as the initial value, nothing was lost.
ABSENT_INITIAL_RULE = True
SCRIPTS_PAK = os.environ.get("KCDMP_SCRIPTS_PAK", r"D:\SteamLibrary\steamapps\common\KCD2Mod\Data\Scripts.pak")
_CONCEPT_ROOTS = None
MAX_LISTED = 20
SECTION = "## First divergence"
WIN_TAR = r"C:\WINDOWS\system32\tar.exe"
ARCHIVE_SUFFIXES = (".tar.zst", ".tzst", ".tar.gz", ".tgz", ".tar")
MISSING = object()         # "value not known" (None means "key absent")
NUM, OPT_STR = (int, float), (str, type(None))
DIGEST_FIELDS = {"t": NUM, "m": str, "cat": str, "epoch": int, "seq": int, "b": dict, "e": (dict, type(None))}
APPLIED_FIELDS = {"t": NUM, "m": str, "cat": str, "key": str, "cause": str, "before": OPT_STR, "after": OPT_STR,
                  "epoch": int, "seq": int}


class BadInput(Exception):
    pass


def rank(cat):
    return CATS.index(cat) if cat in CATS else len(CATS)


def iso(t):
    if t is None:
        return "-"
    return datetime.fromtimestamp(t / 1000, timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f")[:-3] + "Z"


def stamp(t):
    return "-" if t is None else f"{int(t)} ({iso(t)})"


def fmt(v):
    return "(absent)" if v is None else "`" + str(v).replace("`", "'") + "`"


def cell(s):
    return str(s).replace("|", "\\|").replace("\n", " ")


# ---- reading a bundle ---------------------------------------------------------------------------------------------
def archive_stem(path):
    name = os.path.basename(path)
    return next((name[:-len(s)] for s in ARCHIVE_SUFFIXES if name.lower().endswith(s)), None)


def extract(archive, dest):
    os.makedirs(dest, exist_ok=True)
    try:
        with tarfile.open(archive, "r:*") as tf:
            tf.extractall(dest, filter="data")
        return
    except (tarfile.TarError, OSError, ImportError) as ex:   # no zstd in this Python: Windows' own tar reads it
        err = ex
    if os.path.isfile(WIN_TAR):
        r = subprocess.run([WIN_TAR, "-xf", archive, "-C", dest], capture_output=True, text=True)
        if r.returncode == 0:
            return
        err = r.stderr.strip()
    raise BadInput(f"cannot extract {archive}: {err}")


def open_bundle(path, role, for_write, tmp):
    if os.path.isdir(path):
        return path
    if not os.path.isfile(path):
        raise BadInput(f"{role}: no such file or folder: {path}")
    stem = archive_stem(path)
    if stem is None:
        raise BadInput(f"{role}: neither a folder nor a .tar.zst / .tar bundle: {path}")
    dest = (os.path.join(os.path.dirname(os.path.abspath(path)), stem + ".extracted") if for_write
            else os.path.join(tmp, role))
    extract(path, dest)
    return dest


def find_file(root, name):
    hits = []
    for d, dirs, files in os.walk(root):
        dirs.sort()
        hits += [os.path.join(d, f) for f in files if f.lower() == name.lower()]
    return sorted(hits, key=lambda p: (p.count(os.sep), p))


def read_jsonl(path, role, fields, warn):
    with open(path, encoding="utf-8-sig") as f:
        lines = [(n + 1, line) for n, line in enumerate(f.read().splitlines()) if line.strip()]
    rows = []
    for i, (no, line) in enumerate(lines):
        try:
            r = json.loads(line)
            if not isinstance(r, dict):
                raise ValueError("not a JSON object")
            for k, ty in fields.items():
                if not isinstance(r.get(k), ty) or isinstance(r.get(k), bool):
                    raise ValueError(f"field {k!r} missing or of the wrong type")
        except ValueError as ex:
            if i == len(lines) - 1:   # a crash can cut the last line short: say so, keep the rest
                warn(f"{role}: {path}:{no}: last line unreadable ({ex}); ignored")
                continue
            raise BadInput(f"{role}: {path}:{no}: {ex}")
        if r["m"] != role:
            raise BadInput(f"{path}:{no}: m is {r['m']!r} on the {role} side (host and joiner swapped?)")
        rows.append(r)
    return rows


class Side:
    def __init__(self, role, src, root, warn):
        self.role, self.src, self.root = role, src, root
        found = find_file(root, "digest.jsonl")
        if not found:
            raise BadInput(f"{role}: no digest.jsonl inside {src}")
        if len(found) > 1:
            warn(f"{role}: {len(found)} digest.jsonl files; using {found[0]}")
        self.digest_path = found[0]
        self.rows = read_jsonl(self.digest_path, role, DIGEST_FIELDS, warn)
        if not self.rows:
            raise BadInput(f"{role}: {self.digest_path} has zero digest rows (a hollow run is not a pass)")
        found = find_file(root, "applied.jsonl")
        self.applied_path = found[0] if found else None
        if not found:
            warn(f"{role}: no applied.jsonl; read as no applied rows")
        self.applied = read_jsonl(self.applied_path, role, APPLIED_FIELDS, warn) if found else []
        found = find_file(root, "BRIEFING.md")
        self.briefing = found[0] if found else None
        self._build(warn)

    def _build(self, warn):
        """Reconstruct the full map at every checkpoint: e when written, else the last map carried forward."""
        self.cps, self.by_cat, last, dupes, notes = {}, {}, {}, 0, {}
        for r in sorted(self.rows, key=lambda r: (rank(r["cat"]), r["cat"], r["t"], r["epoch"], r["seq"])):
            cat, b = r["cat"], {str(k): str(v).lower() for k, v in r["b"].items()}
            prev = last.get(cat)
            if r["e"] is not None:
                m = {str(k): str(v) for k, v in r["e"].items()}
                if buckets(m) != b:
                    notes.setdefault(f"{cat}: b does not match buckets(e) (recorder and digest.py disagree)", []).append(r["seq"])
            elif prev is None or prev[1] != b:
                m = None
                notes.setdefault(f"{cat}: e is null but b changed (or nothing earlier): map unknown", []).append(r["seq"])
            else:
                m = prev[2]
                if prev[0] != r["epoch"]:
                    notes.setdefault(f"{cat}: first checkpoint of an epoch without e (map carried)", []).append(r["seq"])
            last[cat] = (r["epoch"], b, m)
            key = (cat, r["epoch"], r["seq"])
            dupes += key in self.cps
            cp = {"t": r["t"], "cat": cat, "epoch": r["epoch"], "seq": r["seq"], "b": b, "map": m}
            self.cps[key] = cp
            self.by_cat.setdefault(cat, []).append(cp)
        for text, seqs in notes.items():
            warn(f"{self.role}: {text} at {len(seqs)} checkpoint(s), first seq {seqs[0]}")
        if dupes:
            warn(f"{self.role}: {dupes} repeated (cat, epoch, seq) checkpoint(s); the later row wins")
        for lst in self.by_cat.values():
            lst.sort(key=lambda c: c["t"])
        line = sorted((r["t"], r["epoch"]) for r in self.rows)
        self._ep_t, self._ep = [t for t, _ in line], [e for _, e in line]
        self.applied_by_key = {}
        for r in sorted(self.applied, key=lambda r: r["t"]):
            self.applied_by_key.setdefault((r["cat"], r["key"]), []).append(r)

    def epoch_at(self, t):
        i = bisect_right(self._ep_t, t) - 1
        return self._ep[i] if i >= 0 else None

    def cp_at(self, cat, t):
        lst = self.by_cat.get(cat, [])
        i = bisect_right([c["t"] for c in lst], t) - 1
        return lst[i] if i >= 0 else None

    def value_at(self, cat, key, t, earlier):
        """The side's value for key just before time t: last checkpoint's map, plus applied rows written since."""
        cp = self.cp_at(cat, t)
        if cp is None or cp["map"] is None:
            return MISSING
        v = cp["map"].get(key)
        for r in earlier:
            if r["t"] > cp["t"]:
                v = r["after"]
        return v

    def changed_between(self, cat, key, t1, t2, value):
        return any(t1 < c["t"] < t2 and c["map"] is not None and c["map"].get(key) != value
                   for c in self.by_cat.get(cat, []))

    def info(self):
        return {"source": self.src, "digest": self.digest_path, "applied": self.applied_path, "briefing": self.briefing,
                "digest_rows": len(self.rows), "applied_rows": len(self.applied), "checkpoints": len(self.cps),
                "epochs": sorted({r["epoch"] for r in self.rows}),
                "first_t": min(r["t"] for r in self.rows), "last_t": max(r["t"] for r in self.rows)}


# ---- comparing ----------------------------------------------------------------------------------------------------
def diff_checkpoint(hc, jc):
    if hc["b"] == jc["b"]:
        return {}
    hm, jm = hc["map"], jc["map"]
    bad = sorted({*hc["b"], *jc["b"]} - {k for k in hc["b"] if hc["b"][k] == jc["b"].get(k)}, key=int)
    unnamed = {f"(buckets {', '.join(bad)})": ("buckets-differ", None, None)}
    if hm is None or jm is None:
        return unnamed
    out = {}
    for k in sorted({*hm, *jm}):
        hv, jv = hm.get(k), jm.get(k)
        if hv != jv:
            out[k] = ("missing-on-joiner" if jv is None else "missing-on-host" if hv is None else "value-differs", hv, jv)
    return out or unnamed


def clock_edge(ep):
    """The world clock is digested in 15-minute steps; the joiner follows the host's clock a few seconds behind (or ahead), so at a
    step's edge the two read adjacent steps for a checkpoint or two. Adjacent steps that clear within CLOCK_EDGE_MAX checkpoints
    are that edge; two steps apart, or longer, are not."""
    if ep["cat"] != "clock" or ep["kind"] != "value-differs":
        return False
    try:
        return abs(int(ep["host_value"]) - int(ep["joiner_value"])) == CLOCK_STEP_MIN
    except (TypeError, ValueError):
        return False


def known_local(ep):
    """WO-207 D.1: why this quest clock is known to run on one machine only (KNOWN_LOCAL), or None."""
    if ep["cat"] != "quest":
        return None
    vals = [v for v in (ep["host_value"], ep["joiner_value"]) if isinstance(v, str)]
    if not vals or not all(v.startswith("clk:") for v in vals):
        return None
    return next((why for prefix, why in KNOWN_LOCAL if ep["key"].startswith(prefix)), None)


def concept_roots(pak=None):
    """{dotted concept path: its root node kind} for every quest file in the game's Scripts.pak ("Barbora.trosecko.kumaniNaTrosecku":
    "Quest", "Barbora.open_world.tutorialy.tutorial_x": "Gameplay"); {} when the pak cannot be read."""
    global _CONCEPT_ROOTS
    if pak is None and _CONCEPT_ROOTS is not None:
        return _CONCEPT_ROOTS
    import zipfile
    out = {}
    try:
        z = zipfile.ZipFile(pak or SCRIPTS_PAK)
    except (OSError, zipfile.BadZipFile):
        z = None
    if z is not None:
        for n in z.namelist():
            nn = n.replace("\\", "/")
            if not (nn.startswith("Quests/Final/") and nn.endswith(".xml")):
                continue
            head = z.read(n)[:4096].decode("utf-8", "replace")
            kinds = [k for k in re.findall(r"<(\w+)\s[^>]*\bName=\"", head) if k != "Database"]
            if kinds:
                out[nn[len("Quests/Final/"):-4].replace("/", ".")] = kinds[0]
    if pak is None:
        _CONCEPT_ROOTS = out
    return out


def not_a_quest(ep, roots=None):
    """WO-208 D.2: why this key is outside the State mirror by design (its top concept is not a <Quest>), or None."""
    if ep["cat"] != "quest" or not ep.get("key"):
        return None
    roots = concept_roots() if roots is None else roots
    if not roots:
        return None   # the game's files unreadable: fail closed
    parts = ep["key"].split(".")
    nearest = None
    for i in range(1, len(parts)):
        kind = roots.get(".".join(parts[:i]))
        if kind == "Quest":
            return None   # under a quest: the mirror carries it (wo137.cpp record(): the C_Quest ancestor)
        if kind is not None:
            nearest = (kind, ".".join(parts[:i]))
    if nearest is None or nearest[1].count(".") < 2:
        return None   # no concept file of its own below the level's (a name the files do not have): not ours to call
    return (f"known-local: under no quest (its nearest concept, {nearest[1]}, is a <{nearest[0]}>) -- each machine runs its own and the "
            f"State mirror carries quest States only (WO-137: world simulation is not under a C_Quest)")


def in_flight(ep, cur, nxt, h, j):
    """Explained when every lagging side has an applied row for the key within one checkpoint interval."""
    maps = [c["map"] for c in (nxt[1], nxt[2]) if c["map"] is not None]
    final = maps[0].get(ep["key"]) if maps else MISSING
    hits = []
    for side, now, then, val in ((h, cur[1], nxt[1], ep["host_value"]), (j, cur[2], nxt[2], ep["joiner_value"])):
        if final is not MISSING and val == final:
            continue                                    # this side already had the value both settled on
        iv = then["t"] - now["t"]
        rows = [r for r in side.applied_by_key.get((ep["cat"], ep["key"]), [])
                if r["epoch"] == ep["epoch"] and now["t"] - iv <= r["t"] <= then["t"]
                and (final is MISSING or final is None or r["after"] == final)]
        if rows:
            hits.append({"side": side.role, **rows[0]})
        elif final is not MISSING:
            return None
    return hits or None


def compare(h, j):
    stats, groups, deltas = {}, {}, []
    for key in sorted({*h.cps, *j.cps}, key=lambda k: (rank(k[0]), k)):
        st = stats.setdefault(key[0], dict.fromkeys(
            ("aligned", "host_only", "joiner_only", "differing", "explained", "unexplained"), 0))
        hc, jc = h.cps.get(key), j.cps.get(key)
        if hc and jc:
            st["aligned"] += 1
            groups.setdefault(key[:2], []).append((key[2], hc, jc))
            deltas.append(jc["t"] - hc["t"])
        else:
            st["host_only" if hc else "joiner_only"] += 1
    episodes = []
    for (cat, epoch), lst in groups.items():
        lst.sort(key=lambda x: x[0])
        live = {}

        def close(ep, i):
            ep["cleared_seq"] = lst[i][0]
            if CLOCK_EDGE_RULE and ep["count"] <= CLOCK_EDGE_MAX and clock_edge(ep):
                ep["explained"], ep["why"] = True, "clock-edge"
            elif IN_FLIGHT_RULE and ep["count"] == 1:
                ep["applied"] = in_flight(ep, lst[i - 1], lst[i], h, j)
                ep["explained"] = ep["applied"] is not None

        for i, (seq, hc, jc) in enumerate(lst):
            d = diff_checkpoint(hc, jc)
            stats[cat]["differing"] += bool(d)
            for k in [k for k in live if k not in d]:
                close(live.pop(k), i)
            for k, (kind, hv, jv) in d.items():
                if k in live:
                    live[k]["count"] += 1
                    live[k]["last_seq"] = seq
                    if (kind, hv, jv) != (live[k]["kind"], live[k]["host_value"], live[k]["joiner_value"]):
                        live[k]["changed"] = True   # the default-only rule needs the value unchanged for the whole episode
                else:
                    live[k] = {"kind": kind, "cat": cat, "key": k, "epoch": epoch, "seq": seq, "host_value": hv,
                               "joiner_value": jv, "host_t": hc["t"], "joiner_t": jc["t"], "sort_t": hc["t"],
                               "count": 1, "last_seq": seq, "cleared_seq": None, "explained": False,
                               "applied": None, "side": None}
                    episodes.append(live[k])
    if KNOWN_LOCAL_RULE:
        for ep in episodes:
            why = None if ep["explained"] else known_local(ep)
            if why: ep["explained"], ep["why"] = True, why
    if NOT_A_QUEST_RULE:
        for ep in episodes:
            why = None if ep["explained"] else not_a_quest(ep)
            if why: ep["explained"], ep["why"] = True, why
    if DEFAULT_ONLY_RULE:
        for ep in episodes:
            why = None if ep["explained"] else default_only(ep)
            if why: ep["explained"], ep["why"] = True, why
    if TIMER_LOCAL_RULE:
        for ep in episodes:
            why = None if ep["explained"] else timer_local(ep)
            if why: ep["explained"], ep["why"] = True, why
    return stats, episodes, (median(deltas) if deltas else 0)


def timer_local(ep):
    """WO-209 D.4: why this quest key is a timer each machine runs itself (TIMER_LOCAL_RULE), or None."""
    if ep["cat"] != "quest" or ep.get("changed") or ep["kind"] != "value-differs" or ep.get("cleared_seq") is None:
        return None
    vals = [ep["host_value"], ep["joiner_value"]]
    if not all(isinstance(v, str) and v.startswith("clk:") for v in vals):
        return None
    return ("timer-local: a quest timer both machines run, a step apart and then the same again -- the game's timers are not States "
            "and are never mirrored (WO-198); the States they set are")


def default_only(ep):
    """WO-209 D.4: why this key is a State at its initial value on one side only (DEFAULT_ONLY_RULE), or None."""
    if ep["cat"] != "quest" or ep.get("changed") or ep["kind"] not in ("missing-on-host", "missing-on-joiner"):
        return None
    present = ep["joiner_value"] if ep["kind"] == "missing-on-host" else ep["host_value"]
    if present != "sv:0":
        return None
    side = "joiner" if ep["kind"] == "missing-on-host" else "host"
    return (f"default-only: a State at its initial value (sv:0) on the {side} only, unchanged for the whole episode, its node not loaded "
            f"on the other side -- no value was ever set on either machine")


def applied_findings(side, offset):
    """applied-stale (old epoch, or before != the reconstructed value) and applied-twice, from one side's rows."""
    out = []

    def finding(kind, r, why, first=None):
        own = {"host_t": r["t"], "joiner_t": None} if side.role == "host" else {"host_t": None, "joiner_t": r["t"]}
        out.append({"kind": kind, "cat": r["cat"], "key": r["key"], "epoch": r["epoch"], "seq": r["seq"], **own,
                    "sort_t": r["t"] - (offset if side.role == "joiner" else 0), "side": side.role, "why": why,
                    "row": r, "first_row": first, "explained": False, "count": 1})

    for (cat, key), rows in side.applied_by_key.items():
        for i, r in enumerate(rows):
            cur = side.epoch_at(r["t"])
            if cur is not None and r["epoch"] < cur:
                finding("applied-stale", r, f"applied with epoch {r['epoch']} while the {side.role} was in epoch {cur}")
            else:
                v = side.value_at(cat, key, r["t"], rows[:i])
                if v is not MISSING and r["before"] != v:
                    finding("applied-stale", r, f"before {fmt(r['before'])} but the {side.role}'s reconstructed "
                                                f"value then was {fmt(v)}")
                    if ABSENT_INITIAL_RULE and v is None and r["before"] == "sv:0" and r["cat"] == "quest":
                        out[-1]["explained"] = True
                        out[-1]["why"] = (f"absent-is-initial: the {side.role} did not have the key (its node asleep or not loaded "
                                          f"there) and the change started from the initial value sv:0 -- nothing was lost")
            p = rows[i - 1] if i else None
            if (p and (p["after"], p["epoch"]) == (r["after"], r["epoch"]) and r["before"] == p["after"]
                    and not side.changed_between(cat, key, p["t"], r["t"], p["after"])):
                finding("applied-twice", r, f"{fmt(r['after'])} applied again; nothing changed it since the same write "
                                            f"at {iso(p['t'])} (cause {p['cause']})", first=p)
    return out


def nearby_applied(f, sides, interval):
    out = []
    for side in sides:
        t = f["host_t"] if side.role == "host" else f["joiner_t"]
        if t is None:
            continue
        out += [{"side": side.role, **r} for r in side.applied_by_key.get((f["cat"], f["key"]), [])
                if t - 2 * interval <= r["t"] <= t + interval]
    return out


def analyze(host_path, joiner_path, for_write=False, tmp=None):
    warnings = []
    h = Side("host", host_path, open_bundle(host_path, "host", for_write, tmp), warnings.append)
    j = Side("joiner", joiner_path, open_bundle(joiner_path, "joiner", for_write, tmp), warnings.append)
    stats, episodes, offset = compare(h, j)
    if not any(s["aligned"] for s in stats.values()):
        warnings.append("no checkpoint is present on both sides: nothing could be compared")
    findings = episodes + applied_findings(h, offset) + applied_findings(j, offset)
    for f in findings:
        if f["kind"].startswith("applied-"):
            stats.setdefault(f["cat"], {"aligned": 0, "host_only": 0, "joiner_only": 0, "differing": 0,
                                        "explained": 0, "unexplained": 0})
        stats[f["cat"]]["explained" if f["explained"] else "unexplained"] += 1
    order = lambda f: (f["sort_t"], f["seq"], rank(f["cat"]), f["cat"], f["key"])  # noqa: E731
    bad = sorted((f for f in findings if not f["explained"]), key=order)
    first = [f for f in bad if bad and (f["cat"], f["epoch"], f["seq"], f["side"]) ==
             (bad[0]["cat"], bad[0]["epoch"], bad[0]["seq"], bad[0]["side"]) and f["sort_t"] == bad[0]["sort_t"]]
    gaps = [b["t"] - a["t"] for lst in h.by_cat.values() for a, b in zip(lst, lst[1:])]
    interval = median(gaps) if gaps else 0
    for f in first:
        f["nearby"] = nearby_applied(f, (h, j), interval)
    return {"exit": 1 if bad else 0, "host": h.info(), "joiner": j.info(), "clock_offset_ms": offset,
            "checkpoint_interval_ms": interval, "first": first, "unexplained": bad,
            "explained": sorted((f for f in findings if f["explained"]), key=order),
            "stats": dict(sorted(stats.items(), key=lambda kv: (rank(kv[0]), kv[0]))), "warnings": warnings,
            "_sides": (h, j)}


# ---- the report ---------------------------------------------------------------------------------------------------
KIND_NOTE = {"missing-on-joiner": "the key is missing on the joiner", "missing-on-host": "the key is missing on the host",
             "value-differs": "the key is on both sides, the values differ",
             "buckets-differ": "the buckets differ but the keys could not be named (a map is unknown there)"}


def applied_line(r):
    return (f"{r['side']} t={stamp(r['t'])}: cause `{r['cause']}`, {fmt(r['before'])} -> {fmt(r['after'])}, "
            f"epoch {r['epoch']}, seq {r['seq']}")


def first_block(res):
    if not res["first"]:
        n = sum(s["aligned"] for s in res["stats"].values())
        return [f"No unexplained divergence: {n} aligned checkpoint(s) across {len(res['stats'])} categories; "
                f"{len(res['explained'])} in-flight difference(s) explained."]
    f0 = res["first"][0]
    seen = f" (the last checkpoint the {f0['side']} had seen when it applied)" if f0["side"] else ""
    out = [f"- category: `{f0['cat']}`, epoch {f0['epoch']}, seq {f0['seq']}{seen}",
           f"- host t: {stamp(f0['host_t'])}", f"- joiner t: {stamp(f0['joiner_t'])}"]
    for f in res["first"]:
        if f["side"]:
            out += [f"- kind: `{f['kind']}` on the {f['side']}", f"  - key: `{f['key']}`", f"  - {f['why']}",
                    f"  - applied row: {applied_line({'side': f['side'], **f['row']})}"]
            if f["first_row"]:
                out.append(f"  - earlier row: {applied_line({'side': f['side'], **f['first_row']})}")
            continue
        cleared = f"cleared at seq {f['cleared_seq']}" if f["cleared_seq"] is not None else "never cleared"
        out += [f"- kind: `{f['kind']}` ({KIND_NOTE[f['kind']]})", f"  - key: `{f['key']}`",
                f"  - host value: {fmt(f['host_value'])}", f"  - joiner value: {fmt(f['joiner_value'])}",
                f"  - lasted {f['count']} aligned checkpoint(s) (seq {f['seq']}..{f['last_seq']}), {cleared}"]
        near = f.get("nearby") or []
        out.append("  - applied rows for this key near it: " + ("none" if not near else ""))
        out += [f"    - {applied_line(r)}" for r in near]
    return out


def stats_table(res):
    out = ["| category | aligned | host only | joiner only | differing | explained | unexplained |",
           "|---|---:|---:|---:|---:|---:|---:|"]
    for cat, s in res["stats"].items():
        out.append(f"| {cat} | {s['aligned']} | {s['host_only']} | {s['joiner_only']} | {s['differing']} | "
                   f"{s['explained']} | {s['unexplained']} |")
    return out + ["", "aligned / host only / joiner only / differing count checkpoints; explained / unexplained count "
                  "divergences (one per key per run of differing checkpoints, plus applied-twice / applied-stale rows)."]


def values(f):
    if f["side"]:
        r = f["row"]
        own = f"{fmt(r['before'])} -> {fmt(r['after'])}"
        return (own, "-") if f["side"] == "host" else ("-", own)
    return fmt(f["host_value"]), fmt(f["joiner_value"])


def verdict(res):
    if not res["unexplained"]:
        return f"**no unexplained divergence** ({len(res['explained'])} in-flight difference(s) explained)"
    f = res["unexplained"][0]
    return (f"**{len(res['unexplained'])} unexplained divergence(s)**; first: `{f['kind']}` in `{f['cat']}` at epoch "
            f"{f['epoch']}, seq {f['seq']}, key `{f['key']}`")


def render(res):
    out = ["# Digest divergence report", "", "Verdict: " + verdict(res), "",
           "| side | source | digest rows | applied rows | checkpoints | epochs | first t | last t |",
           "|---|---|---:|---:|---:|---|---|---|"]
    for role in ("host", "joiner"):
        s = res[role]
        out.append(f"| {role} | {cell(s['source'])} | {s['digest_rows']} | {s['applied_rows']} | {s['checkpoints']} | "
                   f"{', '.join(map(str, s['epochs']))} | {iso(s['first_t'])} | {iso(s['last_t'])} |")
    out += ["", f"Joiner clock minus host clock (median over aligned checkpoints, link delay included): "
                f"{res['clock_offset_ms']:+.0f} ms. Checkpoint interval (host median): {res['checkpoint_interval_ms']:.0f} ms.",
            "", "## First unexplained divergence", "", *first_block(res), "", "## Per category", "", *stats_table(res),
            "", f"## Further unexplained divergences (up to {MAX_LISTED})", ""]
    rest = res["unexplained"][len(res["first"]):]
    if rest:
        out += ["| # | kind | cat | key | host | joiner | epoch | seq | host t | joiner t | lasted / why |",
                "|---:|---|---|---|---|---|---:|---:|---|---|---|"]
        for n, f in enumerate(rest[:MAX_LISTED], 1):
            hv, jv = values(f)
            why = f["why"] if f["side"] else (f"{f['count']} cp, " + (f"cleared at seq {f['cleared_seq']}"
                                              if f["cleared_seq"] is not None else "never cleared"))
            out.append(f"| {n} | {f['kind']} | {f['cat']} | `{cell(f['key'])}` | {cell(hv)} | {cell(jv)} | "
                       f"{f['epoch']} | {f['seq']} | {iso(f['host_t'])} | {iso(f['joiner_t'])} | {cell(why)} |")
        if len(rest) > MAX_LISTED:
            out.append(f"\n... and {len(rest) - MAX_LISTED} more.")
    else:
        out.append("None.")
    out += ["", f"## Explained (in flight, the clock's step edge, known-local, default-only, timer-local, absent-is-initial; up to {MAX_LISTED})", ""]
    if res["explained"]:
        out += ["| kind | cat | key | host | joiner | epoch | seq | cleared at | applied on the lagging side |",
                "|---|---|---|---|---|---:|---:|---:|---|"]
        for f in res["explained"][:MAX_LISTED]:
            hv, jv = values(f)
            ap = ("the clock's 15-minute step edge (no applied row needed)" if f.get("why") == "clock-edge"
                  else f["why"] if f.get("why")   # known-local, default-only, timer-local, absent-is-initial
                  else "; ".join(f"{a['side']} `{a['cause']}` at {iso(a['t'])}" for a in f.get("applied") or []))
            out.append(f"| {f['kind']} | {f['cat']} | `{cell(f['key'])}` | {cell(hv)} | {cell(jv)} | {f['epoch']} | "
                       f"{f['seq']} | {f.get('cleared_seq', '-')} | {cell(ap)} |")
        if len(res["explained"]) > MAX_LISTED:
            out.append(f"\n... and {len(res['explained']) - MAX_LISTED} more.")
    else:
        out.append("None.")
    out += ["", "## Warnings", ""] + ([f"- {w}" for w in res["warnings"]] or ["None."])
    return "\n".join(out) + "\n"


def briefing_section(res, role):
    other = res["joiner" if role == "host" else "host"]["source"]
    when = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC")
    return "\n".join([SECTION, "", f"_From tools/digest/analyze.py, {when}. This is the {role}'s bundle; the other side: "
                      f"{other}._", "", "Verdict: " + verdict(res), "", *first_block(res), "", *stats_table(res), ""])


def put_section(text, section):
    """Replace every earlier copy of the section (up to the next level-1/2 heading) with one copy, else append it."""
    lines, out, at, skip = text.splitlines(), [], None, False
    for line in lines:
        if line.strip() == SECTION:
            at, skip = (len(out) if at is None else at), True
            continue
        if skip and re.match(r"#{1,2} ", line):
            skip = False
        if not skip:
            out.append(line)
    new = section.rstrip("\n").split("\n")
    if at is None:
        while out and not out[-1].strip():
            out.pop()
        out += ([""] if out else []) + new
    else:
        out[at:at] = new + ([""] if at < len(out) else [])
    return "\n".join(out) + "\n"


def write_briefings(res):
    for side in res["_sides"]:
        path = side.briefing or os.path.join(side.root, "BRIEFING.md")
        if not side.briefing:
            res["warnings"].append(f"{side.role}: no BRIEFING.md in the bundle; created {path}")
        old = ""
        if os.path.isfile(path):
            with open(path, encoding="utf-8-sig", newline="") as f:
                old = f.read()
        with open(path, "w", encoding="utf-8", newline="\r\n" if "\r\n" in old else "\n") as f:
            f.write(put_section(old, briefing_section(res, side.role)))
        res.setdefault("briefings_written", []).append(path)


def main(argv=None):
    ap = argparse.ArgumentParser(description="WO-206 Part C: first divergence between host and joiner digests.")
    ap.add_argument("host", help="the host's evidence bundle (.tar.zst / .tar) or folder")
    ap.add_argument("joiner", help="the joiner's evidence bundle (.tar.zst / .tar) or folder")
    ap.add_argument("--out", help="also write the markdown report here")
    ap.add_argument("--write-briefings", action="store_true",
                    help="put a '## First divergence' section into both bundles' BRIEFING.md")
    ap.add_argument("--json", action="store_true", help="print the result as JSON instead of markdown")
    a = ap.parse_args(argv)
    try:
        sys.stdout.reconfigure(errors="backslashreplace")
    except (AttributeError, ValueError):
        pass
    try:
        with tempfile.TemporaryDirectory(prefix="kcdmp-digest-") as tmp:
            res = analyze(a.host, a.joiner, for_write=a.write_briefings, tmp=tmp)
            if a.write_briefings:
                write_briefings(res)
    except BadInput as ex:
        print(f"bad input: {ex}", file=sys.stderr)
        return 2
    md = render(res)
    if a.out:
        with open(a.out, "w", encoding="utf-8", newline="\n") as f:
            f.write(md)
    if a.json:
        print(json.dumps({k: v for k, v in res.items() if not k.startswith("_")}, indent=1, default=str))
    elif a.out:
        print(f"{verdict(res).replace('**', '')}; report: {a.out}")
    else:
        print(md, end="")
    return res["exit"]


if __name__ == "__main__":
    sys.exit(main())
