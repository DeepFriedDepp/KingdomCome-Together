# WO-143 — Activities, part 2: progress

Answer and evidence: `docs/WO-143-findings.md`. This page is what was done,
what it touched, and what is left.

## Phase status

| phase | status | notes |
|---|---|---|
| 0 — origin, the bookmark | done | `origin` = the project's repo, clean, fast-forward; the tag `v0.41.7` on `777b8c2` (the 0.41.7 build) pushed before any code |
| 1 — tools in hand (M3) | done | read and applied; standing and walking copies shown (observed); tool trades at an object refused by the game (fall back to WO-141's apply); no tools on seated copies |
| 2 — activity gaits (M5) | done | the nine contexts; the hoer shown at the host's creep (observed); the maintainer's hoe jitter reproduced and fixed (observed) |
| 3 — one-shots (M4) | done | caught at `RequestStateChange`, played through the player handler's request, a paused copy's NPC state run under the DLL's tick; seated copies shown (observed); standing copies paused mid-errand: quiet |
| 4 — the players' minigames on the avatar | done | seven minigames shown on the host's screen (observed); stone throwing refused (stands) |
| 5 — carts | not covered | the cart is not streamed; a refused cart stance stays held (§1.6 of the findings) |
| 6 — idles and looks | done, not forced on copies | the host reads the look targets; a forced look on a paused copy swings its head and flashes its weapon, so none is forced |
| 7 — quieter logs | done | a refused apply: three tries, then once a minute with the count (observed) |
| 8 — the build | done | every gate green; `VERSION` 0.42.0 (the maintainer's); the local installer from a fresh clone |

## Live runs (all solo, one machine, the Modding Tools game)

| run | what | build |
|---|---|---|
| r1 | research DLL through a file of verbs: the hand slots, inventories, item classes, the one-shot request and its callback, the look target, the paused copy's tick | r1 |
| H1 | host + avatarpeer: 697 s of the village recorded (NPC rows, 1,697 activity, 2,546 extra messages); hands, gaits, looks and one-shots captured; the avatar's minigames on the host's screen (frames 1); the maintainer's grindstone note | t1–t2 |
| H2 | host again: the avatar where the game seats the player at the grindstone (frame 2); the one-shot's search state and the dropped sword | t2 |
| J1 | joiner of synthpeer (a reseeded copy of the throwaway save; the host's townspeople at H1's places): the hoer with a temporary hoe and the hoeing walk, **the maintainer's hoe jitter** (frame 4), the refused tool trades, the seated guest's drink (frame 6), the tankard's stand-up, the carpenter's forced look | t3 |
| J2 | the object use's tool fields, two-step and empty-state trials; the jitter settled by six seconds of the NPC-state steps; the tankard's stand-up on frames (frame 8); the creep read as standing; the dice reaction (frame 7) | t4 |
| J3 | the fixes: the hoer hoes at 0.08 m/s (frame 3) and stands steady at the row end (frame 5); the seated rule; the dropped tools; the writer's hold; the forced-look swing and the sword's flash (frame 9); the caravan's horses (frame 10); the innkeeper's walk-off | t5 |
| J4 | looks skipped on puppets (frame 9); the innkeeper not held | t6 |
| J5 | copies' one-shots in place | t7 |

What the live runs changed is in the findings, §1 (per piece) and §2 (the
writer's hold). In short: the one-shot's search state holds what the body must
keep; the object use names its tools; three refusals and the tools are dropped;
no tools on seated bodies; a hoer creeps at the walking class with the hoeing
pace in its tags and has its NPC state run while it hoes; a refused activity is
written again once its retries back off (cart stances excepted); copies' one-shots
are neither held nor aligned; no forced looks on puppets.

## Gates

Relay round trip 57/57; agent unit tests 622/622; every `Test-*Synthetic.ps1`
(39 suites, `Test-WO143Synthetic` 63/63); both static checks; native unit tests
289/289; the local publish; the payload smoke. The first full run had 4 timing
failures in the relay suite alone; it then passed three times in a row, and
again inside the fresh clone. There, `tools\Build-Installer.ps1` ran every gate
once more before it compiled the installer: all green.

## The installer (local only)

`release\KingdomComeTogether-Setup-0.42.0.exe`, 100,389,356 bytes, sha256
`9581D5DE2670E1957EFBE7857EA901FA052D8402DF98F133C96BA4384613632B`, built by
`tools\Build-Installer.ps1` in a fresh clone of `origin/main` at `53d3ba0`. The
shipped pak's Lua is identical to the committed `kdcmp.lua` (every WO-143
marker, the puppet look skip). Privacy sweep of the payload: 1,026 files, no
identifying strings (the one "duckdns" is the launcher's placeholder example
`myserver.duckdns.org`). The 0.41.7 installer stays beside it (the fallback).
Nothing was uploaded; no GitHub release.

## Side effects (everything touched outside the repo)

**Saves** (`Saved Games\kingdomcome2\saves`):

* **Backup:** before the first launch every save file (292) was copied to the
  scratchpad with a sha256 list; the check runs after the last run.
* **The throwaway:** `playline4` (new, 2026-09-28) holds copies of
  `playline1`'s `autosave087`, `quicksave032` and `quicksave036`. The host runs
  played there under a save lock; every join wrote its files there (the newest
  own save) and the joins' world copies stayed in the scratchpad. No crime was
  committed in town.
* **Final check:** all 292 original save files match their sha256 (below).

**The Modding Tools install** (`KCD2Mod`):

* The mod's `Mods\kdcmp\Data\kdcmp.pak` and `mod.manifest` were backed up
  (2026-09-28 16:43) and replaced by test builds (t1, then t6); put back at the
  end (sha1 checked).
* Research verb files `kcdmp-w141-*.txt` and `kcdmp-w143-*.txt` (a few bytes
  each) beside `kcd.log`, one per research build; removed at the end.
* `kcd.log` and its one backup were overwritten by each launch; copies of each
  run's `kcd.log` and native log are in the scratchpad.
* The test DLLs were injected into the running game only (never installed);
  the game was closed with `System.Quit()` after each run.

**Processes and ports:** the test relay on TCP 7779 / HTTP 5274, the test agents
on IPC 5911/5912, avatarpeer and synthpeer — all started and stopped by the
harness. The maintainer's launcher, agent and relay were not running and were
never touched.

**Windows and input:** no input was sent to the game (every action was the
console, Lua or the agent); one push-down of the game window, after J2's join
load (the game had come to the foreground); no window watchers.

**Other:** four zero-byte Ghidra project marker files (`.gpr`) were recreated
next to older sessions' read-only analysis projects so they would open; removed
at the end. The git tag `v0.41.7` was pushed (the bookmark the work order asked
for; no existing tag was moved).

## Cleanup

Done after the last run (J5):

* The mod's pak and manifest in the Modding Tools install put back from the
  backup; their sha1 match it.
* The six research verb files removed (`kcdmp-w141-KCDMP-t3/t4.txt`,
  `kcdmp-w143-KCDMP-t2…t5.txt`; the r1 ones were gone already).
* The four recreated `.gpr` markers removed (each still zero bytes); no Ghidra
  lock files left.
* `playline4` moved out of the saves folder into the scratchpad (not deleted).
* **All 292 original save files match their sha256.**
* Every test process stopped; the game closed with `System.Quit()`.

## Left

See findings §8: carts following the moving cart (a cart stream); tool trades at
an object (the holder's own item); forced looks on paused copies (the look
target component); one-shots of standing copies paused mid-errand; the
two-machine checks in `docs/TEST-0.42.0.md`.
