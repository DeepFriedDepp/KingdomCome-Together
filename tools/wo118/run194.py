# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-194 live harness (never shipped): shared in-game cutscenes, solo -- the Modding Tools game is the JOINER of run184.py's synthetic host
(its world a copy of a throwaway save before the scene); the synthetic host speaks the scene bus by hand. Unattended: no key press, no focus
change. Needs KCDMP_TEST_W194=1 in the agent's environment (run184.py up passes the caller's).

usage (W184_SP = the session's rig folder, as run184.py):
  python run194.py t <verb ...>        one harness verb to the agent (state|statep|start|startp|copy|seq|set|stop|status), its lines printed
  python run194.py host <bus text>     the synthetic host sends one scene-bus message (W164 kind 15), e.g. "G 900 zbranePanaSemina_x"
  python run194.py watch <s> [rx]      the agent's and the game's WO194 / CutscenePlayer lines for <s> seconds
  python run194.py eid <npc>           the NPC's entity id (hex), read in game
  python run194.py lines <rx> [n]      the last n agent lines matching rx
Results: <W184_SP>/run194.results.jsonl
"""
import json, os, re, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import live
import run184 as R

RESULTS = os.path.join(R.SP, 'run194.results.jsonl')


def record(step, **kw):
    kw.update(step=step, at=time.strftime('%H:%M:%S'))
    open(RESULTS, 'a').write(json.dumps(kw) + '\n')


def agent_lines(off):
    return R.read_from(R.agent_log(), off).splitlines()


def t(verb):
    off = R.size(R.agent_log())
    live.lua('KCD2MP_EmitEvent("w194_test", "%s")' % verb.replace('"', ''))
    time.sleep(3)
    out = [l for l in agent_lines(off) if 'WO194' in l]
    for l in out: print(l)
    record('t', verb=verb, lines=out)


def host(text):
    R.ctl('w164 15 ' + text)
    R.say('host ->', text)
    record('host', text=text)


def watch(secs, rx='WO194|CutscenePlayer::|WO179-FADE|MP-W151|WO189-'):
    off = R.size(R.agent_log()); koff = R.size(live.KLOG)
    t0 = time.time()
    r = re.compile(rx)
    seen = []
    while time.time() - t0 < secs:
        time.sleep(1)
        for l in agent_lines(off):
            if r.search(l): print('A', l); seen.append(l)
        off = R.size(R.agent_log())
        for l in R.read_from(live.KLOG, koff).splitlines():
            if r.search(l): print('K', l); seen.append(l)
        koff = R.size(live.KLOG)
    record('watch', secs=secs, lines=seen[-200:])


def eid(npc):
    tag = 'eid%d' % time.time_ns()
    v = R.lua_reply('local e=System.GetEntityByName("%s"); System.LogAlways("[W184X] %s "..(e and string.match(tostring(e.id), "(%%x+)%%s*$") or "none"))' % (npc, tag), tag)
    print(v)
    return v


if __name__ == '__main__':
    v, a = sys.argv[1], sys.argv[2:]
    if v == 't': t(' '.join(a))
    elif v == 'host': host(' '.join(a))
    elif v == 'watch': watch(float(a[0]), a[1] if len(a) > 1 else 'WO194|CutscenePlayer::|WO179-FADE|MP-W151|WO189-')
    elif v == 'eid': eid(a[0])
    elif v == 'lines':
        rx = re.compile(a[0]); n = int(a[1]) if len(a) > 1 else 30
        for l in [l for l in agent_lines(0) if rx.search(l)][-n:]: print(l)
    else: print(__doc__)
