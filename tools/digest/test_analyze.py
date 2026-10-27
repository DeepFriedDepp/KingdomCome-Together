# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-206 Part C: tests for analyze.py and digest.py on synthetic bundles.  python3 tools/digest/test_analyze.py

House rule: every check has a mutation twin (input or switch changed) on which the same check must FAIL; a twin line
passes only when its check failed. Prints one "RESULT: n passed, m failed" line; exit 0 when nothing failed.
"""
import contextlib
import hashlib
import io
import json
import os
import shutil
import subprocess
import sys
import tarfile
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import analyze  # noqa: E402
import digest  # noqa: E402

T0, IV, DELAY, N = 1_792_000_000_000, 15_000, 40, 80     # 80 checkpoints x 15 s = 20 minutes, 40 ms link delay
HERBS = "q_side_herbs/root/sequence_gather/objective_collect_marigold/counter_gathered"
RESULTS = []


def check(name, ok, detail=""):
    RESULTS.append(bool(ok))
    print(("PASS " if ok else "FAIL ") + name + ("" if ok or not detail else f" -- {detail}"))


def twin(name, holds, detail=""):
    """A mutation twin passes when the original check does NOT hold on the mutated input."""
    check(name + " [twin: must fail]", not holds, detail)


# ---- synthetic bundles --------------------------------------------------------------------------------------------
def world(seq, epoch):
    return {
        "quest": {"q_main/root/act1/sequence_arrival/talk_innkeeper": "sv:3", HERBS: "sv:0",
                  "q_main/root/act1/sequence_arrival/watch_dawn": "watch:started"},
        "dead": {"ttkc_man_14": "alive", "ttkc_woman_3": "alive", "ttkc_guard_2": "alive"},
        "loot": {"ledger:17": "placed", "ledger:18": "placed"},
        "clock": {"clock": str((480 + 5 * seq) % 1440)},
        "join": {"state": "in-world", "epoch": str(epoch)},
    }


def cp_t(role, seq):
    return T0 + seq * IV + (DELAY if role == "joiner" else 0)


def scenario(overrides=(), applied=(), skip=(), epoch_from=None, n=N):
    """overrides: (role, cat, key, value or None to delete, from_seq, until_seq or None)
    applied:   (role, cat, key, before, after, cause, at_seq, ms after that checkpoint, epoch or None for current)
    skip:      (role, seq) checkpoints that side never took."""
    out = {}
    for role in ("host", "joiner"):
        rows, prev = [], {}
        for seq in range(1, n + 1):
            epoch = 2 if epoch_from and seq >= epoch_from else 1
            st = world(seq, epoch)
            for r, cat, key, val, a, b in overrides:
                if r == role and a <= seq and (b is None or seq < b):
                    st[cat].pop(key, None) if val is None else st[cat].__setitem__(key, val)
            if (role, seq) in skip:
                continue
            for i, cat in enumerate(analyze.CATS):
                m = st[cat]
                fresh = prev.get(cat) != (epoch, m)
                rows.append({"t": cp_t(role, seq) + i, "m": role, "cat": cat, "epoch": epoch, "seq": seq, "n": len(m),
                             "b": digest.buckets(m), "e": dict(m) if fresh else None})
                prev[cat] = (epoch, dict(m))
        ap = []
        for r, cat, key, before, after, cause, seq, ms, ep in applied:
            if r == role:
                epoch = ep if ep is not None else (2 if epoch_from and seq >= epoch_from else 1)
                ap.append({"t": cp_t(role, seq) + ms, "m": role, "cat": cat, "key": key, "cause": cause,
                           "before": before, "after": after, "epoch": epoch, "seq": seq})
        out[role] = (rows, sorted(ap, key=lambda x: x["t"]))
    return out


def write_bundles(base, name, sc):
    paths = []
    for role in ("host", "joiner"):
        d = os.path.join(base, f"{name}-{role}")
        os.makedirs(os.path.join(d, "evidence", "digest"), exist_ok=True)
        with open(os.path.join(d, "BRIEFING.md"), "w", newline="\n") as f:
            f.write(f"# Briefing ({role})\n\n## Notes\n\nsynthetic {name}\n")
        with open(os.path.join(d, "MANIFEST.txt"), "w") as f:
            f.write("synthetic\n")
        for fname, rows in (("digest.jsonl", sc[role][0]), ("applied.jsonl", sc[role][1])):
            with open(os.path.join(d, "evidence", "digest", fname), "w", newline="\n") as f:
                f.writelines(json.dumps(r) + "\n" for r in rows)
        paths.append(d)
    return paths


def run(*args):
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf), contextlib.redirect_stderr(io.StringIO()):
        code = analyze.main(["--json", *args])
    try:
        return code, json.loads(buf.getvalue())
    except ValueError:
        return code, None


def first_is(res, kind, cat, key):
    f = (res or {}).get("first") or [{}]
    return any(x.get("kind") == kind and x.get("cat") == cat and x.get("key") == key for x in f)


# ---- digest.py ----------------------------------------------------------------------------------------------------
def fnv1_wrong(s):                                        # mutant: FNV-1 (multiply, then XOR)
    h = digest.FNV_OFFSET
    for c in s.encode("utf-8"):
        h = ((h * digest.FNV_PRIME) & digest.MASK64) ^ c
    return h


def buckets_by_entry(m):                                  # mutant: bucket chosen by key AND value
    sums = {}
    for k, v in m.items():
        b = digest.mix(k, v) % 256
        sums[b] = (sums.get(b, 0) + digest.mix(k, v)) & digest.MASK64
    return {str(b): format(s & 0xFFFFFFFF, "08x") for b, s in sums.items() if s & 0xFFFFFFFF}


def buckets_ordered(m):                                   # mutant: a non-commutative combine
    sums = {}
    for k, v in m.items():
        b = digest.bucket_of(k)
        sums[b] = (sums.get(b, 0) * 31 + digest.mix(k, v)) & digest.MASK64
    return {str(b): format(s & 0xFFFFFFFF, "08x") for b, s in sums.items() if s & 0xFFFFFFFF}


def one_value_one_bucket(fn):
    m = {f"q_{i % 7}/root/node_{i}": f"sv:{i}" for i in range(400)}
    base = fn(m)
    for k in list(m)[::13]:
        b2 = fn({**m, k: m[k] + "x"})
        if sum(base.get(x) != b2.get(x) for x in {*base, *b2}) != 1:
            return False
    return True


def order_free(fn):
    items = [(f"ttkc_npc_{i}", "dead" if i % 3 else "alive") for i in range(600)]
    return fn(dict(items)) == fn(dict(reversed(items)))


def test_digest():
    check("digest KAT fnv1a64('a') = 0xaf63dc4c8601ec8c", digest.fnv1a64("a") == 0xAF63DC4C8601EC8C)
    twin("digest KAT", fnv1_wrong("a") == 0xAF63DC4C8601EC8C, "FNV-1 must not match the FNV-1a answer")
    check("digest one changed value changes exactly one bucket", one_value_one_bucket(digest.buckets))
    twin("digest one changed value (bucket by key+value)", one_value_one_bucket(buckets_by_entry))
    check("digest entry order does not matter", order_free(digest.buckets))
    twin("digest entry order (non-commutative combine)", order_free(buckets_ordered))
    check("digest format: decimal bucket keys, 8 lower-case hex digits, no zero buckets",
          all(k.isdigit() and 0 <= int(k) < 256 and len(v) == 8 and v == v.lower() and int(v, 16)
              for k, v in digest.buckets({"a": "1", "b": "2", "clock": "485"}).items()))


# ---- analyze.py ---------------------------------------------------------------------------------------------------
def test_c1(tmp):
    inflight = scenario(
        overrides=[("host", "quest", HERBS, "sv:1", 10, None), ("joiner", "quest", HERBS, "sv:1", 11, None),
                   ("host", "dead", "ttkc_woman_3", "dead", 33, None), ("joiner", "dead", "ttkc_woman_3", "dead", 34, None),
                   ("host", "loot", "ledger:22", "placed", 57, None), ("joiner", "loot", "ledger:22", "placed", 58, None)],
        applied=[("joiner", "quest", HERBS, "sv:0", "sv:1", "quest-mirror", 10, 300, None),
                 ("joiner", "dead", "ttkc_woman_3", "alive", "dead", "death-announce", 33, 900, None),
                 ("joiner", "loot", "ledger:22", None, "placed", "loot-ledger", 57, 120, None)],
        skip=[("joiner", 70)])
    h, j = write_bundles(tmp, "c1", inflight)
    code, res = run(h, j)
    ok = code == 0 and res and not res["unexplained"] and len(res["explained"]) == 3
    check("C1 20 min identical + 3 in-flight -> 0 unexplained, exit 0", ok, f"code={code}")
    check("C1 a joiner-only gap is counted, not a divergence",
          ok and all(s["host_only"] == 1 for s in res["stats"].values()))
    analyze.IN_FLIGHT_RULE = False
    try:
        code2, res2 = run(h, j)
    finally:
        analyze.IN_FLIGHT_RULE = True
    twin("C1 with the in-flight rule off", code2 == 0 and res2 and not res2["unexplained"],
         f"code={code2}")
    check("C1 twin reports exactly those 3 as unexplained, exit 1",
          code2 == 1 and len(res2["unexplained"]) == 3 and
          {f["key"] for f in res2["unexplained"]} == {HERBS, "ttkc_woman_3", "ledger:22"})


def test_clock_edge(tmp):
    edge = scenario(overrides=[("host", "clock", "clock", "595", 20, 21)])   # the host one step ahead for one checkpoint
    h, j = write_bundles(tmp, "edge", edge)
    code, res = run(h, j)
    check("a clock one step apart for one checkpoint is the step's edge (explained)", code == 0 and res and not res["unexplained"]
          and any(f.get("why") == "clock-edge" for f in res["explained"]), f"code={code}")
    analyze.CLOCK_EDGE_RULE = False
    try:
        code2, res2 = run(h, j)
    finally:
        analyze.CLOCK_EDGE_RULE = True
    twin("the clock-edge rule off", code2 == 0 and res2 and not res2["unexplained"], f"code={code2}")
    two = scenario(overrides=[("host", "clock", "clock", "595", 20, 21), ("host", "clock", "clock", "600", 21, 22)])   # two checkpoints
    h4, j4 = write_bundles(tmp, "edge2", two)
    code4, res4 = run(h4, j4)
    check("an edge lasting two checkpoints is still the edge", code4 == 0 and res4 and not res4["unexplained"], f"code={code4}")
    three = scenario(overrides=[("host", "clock", "clock", "595", 20, 21), ("host", "clock", "clock", "600", 21, 22),
                                ("host", "clock", "clock", "605", 22, 23)])
    h5, j5 = write_bundles(tmp, "edge3", three)
    code5, res5 = run(h5, j5)
    check("one step apart for three checkpoints is not an edge", code5 == 1 and res5 and res5["unexplained"][0]["key"] == "clock",
          f"code={code5}")
    far = scenario(overrides=[("host", "clock", "clock", "610", 20, 21)])   # two steps apart: not an edge
    h3, j3 = write_bundles(tmp, "far", far)
    code3, res3 = run(h3, j3)
    check("a clock two steps apart stays unexplained", code3 == 1 and res3 and res3["unexplained"][0]["key"] == "clock",
          f"code={code3}")


def test_known_local(tmp):
    # WO-207 D.1: the fog machine's clock running on the host only (c1r: 89 checkpoints, never cleared) is known-local
    fog = "Barbora.trosecko.fogControl.fogUnstreaming"
    h, j = write_bundles(tmp, "fog", scenario(overrides=[("host", "quest", fog, "clk:1", 30, None)]))
    code, res = run(h, j)
    check("D.1 a fog clock on the host only is known-local (explained, named)", code == 0 and res and not res["unexplained"]
          and any(str(f.get("why", "")).startswith("known-local") and f["key"] == fog for f in res["explained"]), f"code={code}")
    analyze.KNOWN_LOCAL_RULE = False
    try:
        code2, res2 = run(h, j)
    finally:
        analyze.KNOWN_LOCAL_RULE = True
    twin("D.1 the known-local rule off", code2 == 0 and res2 and not res2["unexplained"], f"code={code2}")
    # the rule is narrow: a fog State (not a clock) and another module's clock stay unexplained
    st = "Barbora.trosecko.fogControl.FogStreaming.streamedFog"
    h3, j3 = write_bundles(tmp, "fogstate", scenario(overrides=[("host", "quest", st, "sv:1", 30, None)]))
    code3, res3 = run(h3, j3)
    check("D.1 a fog State differing stays unexplained", code3 == 1 and res3 and res3["unexplained"][0]["key"] == st, f"code={code3}")
    other = "Barbora.trosecko.jaunt.watch_1300"
    h4, j4 = write_bundles(tmp, "otherclock", scenario(overrides=[("host", "quest", other, "clk:1", 30, None)]))
    code4, res4 = run(h4, j4)
    check("D.1 another module's host-only clock stays unexplained (the Jaunt shape)", code4 == 1 and res4
          and res4["unexplained"][0]["key"] == other, f"code={code4}")


def test_default_only(tmp):
    # WO-209 D.4: a State at sv:0 on the joiner only for the whole run (WO-208's soak: eight Kuttenberg States) is default-only
    key = "Barbora.kutnohorsko.mapaKPokladu.hibernace.HracZiskavaMapuAHledaTabor"
    h, j = write_bundles(tmp, "defonly", scenario(overrides=[("joiner", "quest", key, "sv:0", 1, None)]))
    code, res = run(h, j)
    check("D.4 a State at sv:0 on one side only, all run long, is default-only (explained, named)", code == 0 and res
          and not res["unexplained"] and any(str(f.get("why", "")).startswith("default-only") and f["key"] == key for f in res["explained"]),
          f"code={code}")
    analyze.DEFAULT_ONLY_RULE = False
    try:
        code2, res2 = run(h, j)
    finally:
        analyze.DEFAULT_ONLY_RULE = True
    twin("D.4 the default-only rule off", code2 == 0 and res2 and not res2["unexplained"], f"code={code2}")
    # the rule is narrow: a value other than the default, or a default later set, stays unexplained
    h3, j3 = write_bundles(tmp, "defset", scenario(overrides=[("joiner", "quest", key, "sv:1", 1, None)]))
    code3, res3 = run(h3, j3)
    check("D.4 a State at sv:1 on one side only stays unexplained", code3 == 1 and res3 and res3["unexplained"][0]["key"] == key, f"code={code3}")
    h4, j4 = write_bundles(tmp, "deflater", scenario(overrides=[("joiner", "quest", key, "sv:0", 1, 40), ("joiner", "quest", key, "sv:2", 40, None)]))
    code4, res4 = run(h4, j4)
    check("D.4 a default set later (sv:0 -> sv:2) stays unexplained", code4 == 1 and res4 and any(f["key"] == key for f in res4["unexplained"]), f"code={code4}")


def test_timer_local(tmp):
    # WO-209 D.4: quest timers a step apart, and a timer on the host only, are each machine's own (never mirrored, WO-198)
    t1 = "Barbora.trosecko.kumaniNaTrosecku.rvacka_v_hospode.timer_keepSkirmishBarksActive"
    t2 = "Barbora.trosecko.kumaniNaTrosecku.rvacka_v_hospode.eskalace_bitky.escalationTimer"
    h, j = write_bundles(tmp, "timers", scenario(overrides=[("host", "quest", t1, "clk:1", 20, 40), ("joiner", "quest", t1, "clk:2", 20, 40)]))
    code, res = run(h, j)
    check("D.4 a quest timer both run, a step apart then the same, is timer-local (explained)", code == 0 and res and not res["unexplained"]
          and any(str(f.get("why", "")).startswith("timer-local") for f in res["explained"]), f"code={code}")
    analyze.TIMER_LOCAL_RULE = False
    try:
        code2, res2 = run(h, j)
    finally:
        analyze.TIMER_LOCAL_RULE = True
    twin("D.4 the timer-local rule off", code2 == 0 and res2 and not res2["unexplained"], f"code={code2}")
    h5, j5 = write_bundles(tmp, "timerhost", scenario(overrides=[("host", "quest", t2, "clk:1", 30, None)]))
    code5, res5 = run(h5, j5)
    check("D.4 a timer on the host only stays unexplained (the Jaunt's shape)", code5 == 1 and res5 and any(f["key"] == t2 for f in res5["unexplained"]), f"code={code5}")
    st = "Barbora.trosecko.kumaniNaTrosecku.rvacka_v_hospode.chovani_stamgastu_a_kumanu.sittingInPub"
    h3, j3 = write_bundles(tmp, "timerstate", scenario(overrides=[("host", "quest", st, "sv:2", 30, None)]))
    code3, res3 = run(h3, j3)
    check("D.4 a State differing (not a timer) stays unexplained", code3 == 1 and res3 and any(f["key"] == st for f in res3["unexplained"]), f"code={code3}")


def test_absent_initial(tmp):
    # WO-209 D.4: an applied change from sv:0 on a joiner that did not have the key (its node asleep) is absent-is-initial
    key = "Barbora.trosecko.zranenyLovci.tipsteri.marklivingsoul_1.removeNPCMetaroleConextNotSet"
    sc = scenario(overrides=[("host", "quest", key, "sv:1", 41, None), ("joiner", "quest", key, "sv:1", 41, None)],
                  applied=[("joiner", "quest", key, "sv:0", "sv:1", "host-change", 40, 300, None)])
    h, j = write_bundles(tmp, "absinit", sc)
    code, res = run(h, j)
    check("D.4 a change from sv:0 where the key was absent is absent-is-initial (explained)", code == 0 and res and not res["unexplained"]
          and any(str(f.get("why", "")).startswith("absent-is-initial") for f in res["explained"]), f"code={code}")
    analyze.ABSENT_INITIAL_RULE = False
    try:
        code2, res2 = run(h, j)
    finally:
        analyze.ABSENT_INITIAL_RULE = True
    twin("D.4 the absent-is-initial rule off", code2 == 0 and res2 and not res2["unexplained"], f"code={code2}")
    sc4 = scenario(overrides=[("host", "quest", key, "sv:2", 41, None), ("joiner", "quest", key, "sv:2", 41, None)],
                   applied=[("joiner", "quest", key, "sv:1", "sv:2", "host-change", 40, 300, None)])
    h4, j4 = write_bundles(tmp, "absinit1", sc4)
    code4, res4 = run(h4, j4)
    check("D.4 a change from sv:1 where the key was absent stays unexplained", code4 == 1 and res4
          and any(f["kind"] == "applied-stale" for f in res4["unexplained"]), f"code={code4}")


def test_not_a_quest(tmp):
    # WO-208 D.2: a State under no quest (a <Gameplay> / <Module> concept: a tutorial, the open-world combat chatter) is each machine's
    # own -- the State mirror carries quest States only; a quest's own State that differs stays unexplained
    import zipfile
    pak = os.path.join(tmp, "Scripts.pak")
    with zipfile.ZipFile(pak, "w") as z:
        for path, kind in (("Barbora", "Project"), ("Barbora/trosecko", "Level"), ("Barbora/trosecko/kovar", "Quest"),
                           ("Barbora/open_world", "Gameplay"), ("Barbora/open_world/tutorialy/tut_x", "Gameplay")):
            z.writestr("Quests/Final/%s.xml" % path, '<Database Name="brambora"><%s Name="%s"><Port Name="p"/></%s></Database>'
                       % (kind, path.split("/")[-1], kind))
    saved = analyze.SCRIPTS_PAK, analyze._CONCEPT_ROOTS
    analyze.SCRIPTS_PAK, analyze._CONCEPT_ROOTS = pak, None
    try:
        tut, kov = "Barbora.open_world.tutorialy.tut_x.state13", "Barbora.trosecko.kovar.KovarJeVKovarne"
        h, j = write_bundles(tmp, "tut", scenario(overrides=[("host", "quest", tut, "sv:1", 20, None), ("joiner", "quest", tut, "sv:0", 20, None)]))
        code, res = run(h, j)
        check("D.2 a tutorial State (a <Gameplay>, under no quest) differing is each machine's own (explained, named)", code == 0 and res
              and not res["unexplained"] and any(str(f.get("why", "")).startswith("known-local: under no quest") and f["key"] == tut
                                                 for f in res["explained"]), f"code={code}")
        analyze.NOT_A_QUEST_RULE = False
        try:
            code2, res2 = run(h, j)
        finally:
            analyze.NOT_A_QUEST_RULE = True
        twin("D.2 the not-a-quest rule off", code2 == 0 and res2 and not res2["unexplained"], f"code={code2}")
        h3, j3 = write_bundles(tmp, "kov", scenario(overrides=[("host", "quest", kov, "sv:1", 20, None), ("joiner", "quest", kov, "sv:2", 20, None)]))
        code3, res3 = run(h3, j3)
        check("D.2 a quest's own State differing stays unexplained (under a <Quest>)", code3 == 1 and res3 and res3["unexplained"][0]["key"] == kov,
              f"code={code3}")
        analyze.SCRIPTS_PAK, analyze._CONCEPT_ROOTS = os.path.join(tmp, "no-such.pak"), None
        code4, res4 = run(h, j)
        check("D.2 the game's files unreadable: the rule fails closed (unexplained)", code4 == 1 and res4 and res4["unexplained"][0]["key"] == tut,
              f"code={code4}")
    finally:
        analyze.SCRIPTS_PAK, analyze._CONCEPT_ROOTS = saved


def test_c2(tmp):
    key = "jaunt/watch_1300"
    h, j = write_bundles(tmp, "c2", scenario(overrides=[("host", "quest", key, "timer:armed:13:00", 40, None)]))
    code, res = run(h, j)
    holds = lambda c, r: c == 1 and first_is(r, "missing-on-joiner", "quest", key) and r["first"][0]["seq"] == 40  # noqa
    check("C2 Jaunt: first = missing-on-joiner, quest, jaunt/watch_1300, seq 40", holds(code, res))
    out = os.path.join(tmp, "c2.md")
    with contextlib.redirect_stdout(io.StringIO()):
        analyze.main([h, j, "--out", out])
    with open(out, encoding="utf-8") as f:
        md = f.read()
    check("C2 report prints the full node path and both values",
          f"key: `{key}`" in md and "host value: `timer:armed:13:00`" in md and "joiner value: (absent)" in md)
    h2, j2 = write_bundles(tmp, "c2twin", scenario())
    code2, res2 = run(h2, j2)
    twin("C2 without the timer key on the host", holds(code2, res2), f"code={code2}")
    check("C2 twin is clean (the finding came from that key)", code2 == 0 and not res2["unexplained"])
    proc = subprocess.run([sys.executable, "-I", os.path.join(HERE, "analyze.py"), h, j], capture_output=True, text=True)
    check("C2 CLI exit code 1", proc.returncode == 1, proc.stderr[-300:])
    proc2 = subprocess.run([sys.executable, "-I", os.path.join(HERE, "analyze.py"), h2, j2], capture_output=True, text=True)
    twin("C2 CLI exit code 1 (clean twin)", proc2.returncode == 1)


def test_c3(tmp):
    npc = "ttkc_man_14"
    sc = scenario(overrides=[("joiner", "dead", npc, "dead", 30, None)],
                  applied=[("joiner", "dead", npc, "alive", "dead", "death-announce", 29, 5000, None)])
    code, res = run(*write_bundles(tmp, "c3", sc))
    holds = lambda c, r: c == 1 and first_is(r, "value-differs", "dead", npc)  # noqa: E731
    check("C3 joiner-only death-announce: first = dead, value-differs, ttkc_man_14", holds(code, res))
    check("C3 values and the announcing row are named",
          res and res["first"][0]["host_value"] == "alive" and res["first"][0]["joiner_value"] == "dead"
          and any(r["cause"] == "death-announce" for r in res["first"][0]["nearby"]))
    code2, res2 = run(*write_bundles(tmp, "c3twin", scenario()))
    twin("C3 with the announcement removed", holds(code2, res2), f"code={code2}")


def test_applied(tmp):
    ov = [("host", "loot", "ledger:17", "taken", 21, None), ("joiner", "loot", "ledger:17", "taken", 21, None)]
    once = ("joiner", "loot", "ledger:17", "placed", "taken", "loot-take", 20, 3000, None)
    again = ("joiner", "loot", "ledger:17", "taken", "taken", "loot-take", 25, 3000, None)
    holds = lambda c, r, k: c == 1 and first_is(r, k, "loot" if k == "applied-twice" else "quest",  # noqa: E731
                                                "ledger:17" if k == "applied-twice" else HERBS)
    code, res = run(*write_bundles(tmp, "twice", scenario(overrides=ov, applied=[once, again])))
    check("applied-twice found (joiner, loot, ledger:17)", holds(code, res, "applied-twice") and
          res["first"][0]["side"] == "joiner" and res["first"][0]["seq"] == 25)
    code2, res2 = run(*write_bundles(tmp, "twicetwin", scenario(overrides=ov, applied=[once])))
    twin("applied-twice with the duplicate removed", holds(code2, res2, "applied-twice"))
    check("applied-twice twin is clean", code2 == 0 and not res2["unexplained"])

    ov = [("host", "quest", HERBS, "sv:2", 52, None), ("joiner", "quest", HERBS, "sv:2", 52, None)]
    row = lambda before, ep: ("joiner", "quest", HERBS, before, "sv:2", "quest-mirror", 51, 3000, ep)  # noqa: E731
    code, res = run(*write_bundles(tmp, "staleep", scenario(overrides=ov, applied=[row("sv:0", 1)], epoch_from=50)))
    check("applied-stale found (epoch 1 row after the joiner reached epoch 2)", holds(code, res, "applied-stale")
          and "epoch 1" in res["first"][0]["why"])
    code2, res2 = run(*write_bundles(tmp, "staleeptwin", scenario(overrides=ov, applied=[row("sv:0", 2)], epoch_from=50)))
    twin("applied-stale with the epoch made current", holds(code2, res2, "applied-stale"))
    check("applied-stale epoch twin is clean", code2 == 0 and not res2["unexplained"])

    code, res = run(*write_bundles(tmp, "stalebf", scenario(overrides=ov, applied=[row("sv:7", None)])))
    check("applied-stale found (before does not match the reconstructed value)", holds(code, res, "applied-stale")
          and "reconstructed" in res["first"][0]["why"])
    code2, res2 = run(*write_bundles(tmp, "stalebftwin", scenario(overrides=ov, applied=[row("sv:0", None)])))
    twin("applied-stale with before corrected", holds(code2, res2, "applied-stale"))


def test_bad_input(tmp):
    h, j = write_bundles(tmp, "bad", scenario())
    good_code, _ = run(h, j)
    with open(os.path.join(j, "evidence", "digest", "digest.jsonl"), "w") as f:
        f.write("")
    code, _ = run(h, j)
    check("bad input: an empty joiner side exits 2", code == 2, f"code={code}")
    twin("bad input (the same pair with rows)", good_code == 2)
    code3, _ = run(os.path.join(tmp, "no-such-bundle"), h)
    check("bad input: a missing bundle exits 2", code3 == 2)
    h2, j2 = write_bundles(tmp, "swap", scenario())
    code4, _ = run(j2, h2)
    check("bad input: host and joiner swapped exits 2", code4 == 2)
    twin("bad input: swapped (in the right order)", run(h2, j2)[0] == 2)


def section_count(path):
    with open(path, encoding="utf-8") as f:
        return f.read().count(analyze.SECTION)


def test_briefings(tmp):
    sc = scenario(overrides=[("host", "quest", "jaunt/watch_1300", "timer:armed:13:00", 40, None)])
    h, j = write_bundles(tmp, "brief", sc)
    stale = "stale finding from an earlier run"
    with open(os.path.join(h, "BRIEFING.md"), "a", newline="\n") as f:
        f.write(f"\n{analyze.SECTION}\n\n{stale}\n\n## Afterwards\n\nkept\n")
    holds = lambda p: section_count(p) == 1 and stale not in open(p, encoding="utf-8").read()  # noqa: E731
    twin("briefing: stale section replaced (before any run)", holds(os.path.join(h, "BRIEFING.md")))
    for _ in range(2):
        with contextlib.redirect_stdout(io.StringIO()):
            analyze.main([h, j, "--write-briefings", "--out", os.path.join(tmp, "brief.md")])
    hb, jb = os.path.join(h, "BRIEFING.md"), os.path.join(j, "BRIEFING.md")
    text = open(hb, encoding="utf-8").read()
    check("briefing: one section in each, stale copy replaced, other sections kept",
          holds(hb) and section_count(jb) == 1 and "## Notes" in text and "## Afterwards" in text and
          "jaunt/watch_1300" in text)

    if "zst" not in tarfile.TarFile.OPEN_METH:
        check("briefing: .tar.zst archive (SKIPPED: no zstd in this Python)", True)
        return
    arch = []
    for d in (h, j):
        a = os.path.join(tmp, os.path.basename(d) + "-bundle.tar.zst")
        with tarfile.open(a, "w:zst") as tf:
            tf.add(d, arcname=os.path.basename(d))
        arch.append(a)
    digest_of = lambda p: hashlib.sha256(open(p, "rb").read()).hexdigest()  # noqa: E731
    before = [digest_of(a) for a in arch]
    sib = [a[:-len(".tar.zst")] + ".extracted" for a in arch]
    has = lambda: all(os.path.isfile(p) and section_count(p) == 1  # noqa: E731
                      for p in (analyze.find_file(s, "BRIEFING.md")[0] if os.path.isdir(s) else "" for s in sib))
    code, _ = run(*arch)
    twin("archive: sibling .extracted/ briefing (run without --write-briefings)", has())
    check("archive: read from .tar.zst without writing a sibling", code == 1 and not any(map(os.path.isdir, sib)))
    with contextlib.redirect_stdout(io.StringIO()):
        code = analyze.main([*arch, "--write-briefings", "--out", os.path.join(tmp, "arch.md")])
    check("archive: --write-briefings writes into <name>.extracted/, archive unchanged",
          code == 1 and has() and [digest_of(a) for a in arch] == before)


def main():
    tmp = tempfile.mkdtemp(prefix="kcdmp-digest-test-")
    # the game's own Scripts.pak is never read by these tests: the not-a-quest rule sees only test_not_a_quest's own pak
    analyze.SCRIPTS_PAK, analyze._CONCEPT_ROOTS = os.path.join(tmp, "no-scripts.pak"), None
    try:
        test_digest()
        for t in (test_c1, test_clock_edge, test_known_local, test_default_only, test_timer_local, test_absent_initial, test_not_a_quest, test_c2, test_c3, test_applied, test_bad_input, test_briefings):
            t(tmp)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    passed = sum(RESULTS)
    print(f"RESULT: {passed} passed, {len(RESULTS) - passed} failed")
    return 0 if passed == len(RESULTS) else 1


if __name__ == "__main__":
    sys.exit(main())
