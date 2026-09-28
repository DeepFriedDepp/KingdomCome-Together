# WO-141 — Activities and animal attacks: progress

Answer and evidence: `docs/WO-141-findings.md`. This page is what was done,
what it touched, and what is left.

## Phase status

| phase | status | notes |
|---|---|---|
| 0 — origin, fast-forward, the census first | done | `origin` = the project's repo; WO-140's findings on `origin/main`; the WO-141A census committed on its own (`29c78f7`) before any code |
| 1 — the read and the apply | done | the NPC-state context, the element layouts, the WUID ↔ GUID mapping, the game's post-load placement (findings §1–2) |
| 2 — NPC activities on the joiner | done | M1 stances + M2 unstances, the reconcile, the writer's yield; A/B of the field bug (observed) |
| 3 — the players' activities | done, one part not shown | sitting both ways (observed); the trough's wash both halves (observed); the grindstone travels but is not shown (no NPC activity matches) |
| 4 — animal attacks on the joiner | done | the actor-system fallback; a wolf's bite on the joiner's screen (observed) |
| M3 hand content, M5 activity gaits, M4 one-shots, cart passengers, animals' idles | not built / not seen | M3/M5/M4 not built (no switch needed: nothing half on); animals' idles ride the same read, not seen live |
| 5 — the build | done | every gate green; `VERSION` 0.41.7 (the maintainer's); the local installer from a fresh clone |

Commits on `origin/main`: `29c78f7` (the census), `6ad44d2` (the code),
`777b8c2` (0.41.7, the badge, the release notes), and the docs commit.

## Live runs (all solo, one machine, the Modding Tools game)

| run | what | build |
|---|---|---|
| r1–r10 | research DLLs through a file of verbs (read, near, make, want, frag): the context, the slots, the layouts, the placement, the reflection names | r1–r10 |
| h1 | host + avatarpeer: the village read, the host's NPC rows out (1,891 rows), the host's sit captured and sent, the partner's avatar sits on and stands up from an outdoor bench (frames 3–4), the nameplate issue the maintainer saw | t1 |
| j1 | joiner of synthpeer (a reseeded copy of the throwaway save; NPC rows replayed from h1's recording): beds, leans, the guard post, the snooze; the A/B; the wolf's bite; the host avatar's wash at the trough; the grindstone captured; fragments through the combat path (nothing shown) | t1 + t2 (research) |
| j2 | the agent restarted mid-session: the WO-140 own-world trap, as designed (activities off) | t3 |
| j3 | joiner again, the final build: the joiner's trough wash end to end on the wire; the nameplate over the body | t3 (+ the agent fix) |

Two fixes came out of the live runs: the show op refused the trough holder's
75-character name (the NPC-name limit of 63; the first try failed silently),
and the nameplate table was keyed by number while the ghost tables use the
string id. Both are fixed and pinned by tests.

## Gates

Relay round trip 56/56 (the new 0x6A/0x6C case included); agent unit tests
609/609 (`Wo141Tests` 14 + the widened join-range check); every
`Test-*Synthetic.ps1` (38 suites, `Test-WO141Synthetic` 33/33); both static
checks; native unit tests 237/237; the payload smoke. The same gates ran again
inside the fresh clone before the installer was compiled.

## The installer (local only)

`release\KingdomComeTogether-Setup-0.41.7.exe`, 100,333,076 bytes, sha256
`47D9FE2E8B4A4499CAC7588921EC0A05ED8FFA89BB657CC8A22107BB72EF73AD`, built by
`tools\Build-Installer.ps1` in a fresh clone of `origin/main` at `777b8c2`.
The shipped pak's Lua is identical to the committed `kdcmp.lua` with every
WO-141 marker; the DLL and agent carry theirs (`Verify-Install.ps1` knows
them). Privacy sweep of the payload: 1,026 files, no identifying strings (the
one "duckdns" is the launcher's placeholder example `myserver.duckdns.org`).
Nothing was uploaded; no GitHub release.

## Side effects (everything touched outside the repo)

**Saves** (`Saved Games\kingdomcome2\saves`):

* **Backup:** before the first launch every save file (292) was copied to the
  scratchpad with a sha256 list; the check ran after the runs.
* **The throwaway:** `playline4` (new) held copies of `playline1`'s
  `autosave087`, `quicksave032` and `quicksave036`. The runs wrote only there
  and to the joins' world copies in the scratchpad. In h1 the host's Henry was
  killed by a guard once (a trespass alarm while seated in a house: "You
  died"); the throwaway was reloaded, a save lock kept the game from writing,
  and later player tests used outdoor benches.
* **`playline1`'s folder time** changed during run j3 (a file was created and
  removed there by the game); every file in it still matches its sha256.
* **Final check:** all 292 original files match their sha256; `playline4` was
  moved out of the saves folder into the scratchpad (not deleted).

**The Modding Tools install** (`KCD2Mod`):

* The mod's `Mods\kdcmp\Data\kdcmp.pak` was replaced by the test builds (t1,
  then t3); the original pak and manifest were backed up first and put back
  at the end (sha1 checked).
* Research verb files `kcdmp-w141*.txt` (12, a few bytes each) were written
  beside `kcd.log` for the research DLLs; removed at the end.
* `kcd.log` and its one backup were overwritten by each launch (copies of each
  run's log are in the scratchpad).
* The test DLLs were injected into the running game only (never installed);
  the game was closed with `System.Quit()` after each run.

**Processes and ports:** the test relay on TCP 7779 / HTTP 5274, the test
agents on IPC 5911/5912, avatarpeer and synthpeer — all started and stopped by
the harness. The maintainer's launcher, agent and relay were not running and
were never touched. .NET build servers left by the builds were shut down.

**Windows and input:** no input was sent to the game (every action was the
console or the agent); at most one push-down of the game window after each
launch or load; no window watchers.

**Other:** four zero-byte Ghidra project marker files (`.gpr`) were recreated
next to older sessions' existing read-only analysis projects so they would
open; removed at the end.

## Left

See findings §8: hand content (M3) and with it the tool trades and the field
hoe (M5); NPC one-shots (M4); quieter logging of a refused activity; the
two-machine checks in `docs/TEST-0.41.7.md`.
