# tools/perf — the frame-rate soak and the stat-stack reader (WO-151)

Why: 0.42.5–0.42.7 lost the frame rate in every fight and crowd (75 → 4.5 FPS within
minutes) and nothing measured it; the cause was a reflection argument that outlived its
value, faulting inside the game's stat getter thousands of times behind a silent guard
(docs/WO-148-findings.md §7). These tools make a decline visible before an installer.

Everything here is read-only on the game except the soak's scene (spawned, never saved,
removed at the end), and runs only on a **throwaway save** (the soak reads which save is
loaded from `kcd.log` first and refuses any other playline).

## The soak (required before every installer)

```
python tools/perf/soak.py run --label mod1 --throwaway playline4            # the game WITH the mod (pak + DLL)
python tools/perf/soak.py run --label van1 --throwaway playline4 --vanilla  # the same save, the game WITHOUT the mod
python tools/perf/soak.py verdict --mod tools/perf/runs/mod1.json --vanilla tools/perf/runs/van1.json --version <x.y.z>
```

* 10 minutes: three commoners 3 m around the player (no AI, never saved: two or more
  souls within 15 m, the 0.42.5 leak's trigger), from minute 3 a fight beside them (two AI
  commoners, the game's own attack interrupt re-sent every 15 s). No input, no focus.
* Every 10 s: the frame rate over those 10 s, the depth of the game's stat stack on its
  main thread, the `FAULT` lines in `kcdmp-native.mirror.log` since the start.
* PASS: the mod's last 2 minutes within 10 % of its first 2, both within 10 % of the same
  windows without the mod, the stack 0 in every row, no `FAULT` line, the code committed.
* `verdict` writes `tools/perf/soak-record.json` (commit it). It carries the git trees of
  the code the soak ran (`native/KCDMP`, `kdcmp/Data/Scripts`, `kdcmp/Data/Libs`,
  `kdcmp/mod.manifest`, the agent, the relay, the protocol); `tools/Build-Installer.ps1`
  runs `soak.py check` first and stops when the record is missing, says FAIL, or names
  other trees than the ones being built.
* The game without the mod: close the game, move `Mods\kdcmp` out of the Modding Tools
  folder (and do not inject the DLL), run, put it back.

## The stat-stack reader

```
python tools/perf/statstack.py            # the running game, its main thread: depth=N
```

The stack must be 0 between frames. A depth that grows is a leak (WO-148 §7.3). The
offsets are the Modding Tools 1.5.5 RPGModule's; the reader checks that module's PE
identity and refuses any other build.
