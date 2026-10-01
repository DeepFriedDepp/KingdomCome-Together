# WO-148 — progress

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

The answer and the evidence: `docs/WO-148-findings.md`. The census:
`docs/WO-148A-carry-census.md`.

| part | status | where |
|---|---|---|
| 0 — the bookmark | done | the tag `v0.42.5` on `7dcd01a` (the commit the 0.42.5 installer was built from, `docs/WO-147-progress.md`), pushed before any code |
| 1 — attribution and the content audit | done (Stage A); the release and history findings left to the maintainer | findings §1, §2 |
| 2 — the port-aware setup | done; all seven hooks armed live | findings §3 |
| 3.1 — the carry census | done; five of its seven questions answered live, one in part | `docs/WO-148A-carry-census.md` |
| 3.2 — carrying on the other screen | done, proven live both ways (solo) | findings §4 |
| 3.4 — the live runs | done after the maintainer's OK: H1, H2, J1–J3 | "Stage B" below |
| gates, docs, build | done | "Gates and the build" below |

## Stage A (no game, no window)

Everything ran from the command line: builds, unit tests, the synthetic suites,
the static checks, read-only scans of the repo, its history, the game's paks and
the saves folder, and the GitHub API (release metadata only). No game, relay or
agent was started against a game, no window was opened, no key was pressed, and
the Mods folder, the saves and the game install were not written to. Nothing of
the maintainer's was running (no game, launcher, agent, relay or Steam).

Work, in order:

1. **The bookmark**: `v0.42.5` → `7dcd01a`, pushed (lightweight, like the earlier
   version tags).
2. **Part 2, native**: the thread-id column (`log.h`), the x64 length decoder
   (`x64_len.h`), the boundary check in `inline_hook.cpp`, the shipped hooks'
   prologues in `hook_prologues.h`, the capstone vector generator
   (`tools/wo148/gen_x64_vectors.py` → `native/tests/wo148_x64_vectors.inc`) and
   the native tests (`wo148_x64_tests.cpp`, 30 checks). First run: 8 of 4,102
   vectors disagreed — `ud0`/`ud1` (capstone and the SDM disagree on their ModRM;
   both are refused as control transfers anyway) and three `[eip+disp]` operands
   capstone does not flag as RIP-relative; the generator now counts EIP-relative,
   the test compares only the refusal for a control transfer: 4,102/4,102.
3. **The audit** (two research helpers, read-only): the carry census of the game
   data, and the content scan of the tree, the history, the images and the docs.
4. **Part 1**: `AUTHORS`, `NOTICE`, the README's top and its licence section, the
   release-notes template, `docs/DISCORD-TEXT.md`, the launcher's About box, the
   file headers (551 files by a script; 44 with the upstream line).
5. **The audit's fixes**: the two config copies out of the tree and the pak; the
   dice keys built on the player's machine (`KeybindPak.cs`, the agent's
   `--keys-pak`; Setup, the launcher and `Build-And-Install-Mod.ps1` run it;
   `Verify-Install.ps1` checks it). Run against this machine's game into a
   scratch folder: written in 151 ms, `unchanged` on a rerun; the two files equal
   the old shipped copies except one trailing tab the game's own file has. The
   quest registry regenerated with keys only (same id); titles read at run time.
6. **Mid-stage instruction from the maintainer**: "do not replace the
   text[ures] in the launcher with plain CSS. Keep the launcher the way it is.
   This was completely out of scope." No launcher style or image had been changed
   yet; the launcher footer, the Report Bug link, the mod manifest, Setup's texts
   and the payload's notice files were then put back as they were. The UI
   textures and the branding stay; the findings record them.
7. **Part 3**: the census document; the protocol (`ProtocolWo148.cs`, Carry
   0x70/0x71 on the join channel, the relay's log leaves the 2 s Held out), the
   ledger and the landing rule (`Wo148.cs`), the agent (`GameBridge.Wo148.cs`), the
   mod (the WO-148 section of `kdcmp.lua` and its hooks into the stream, the puppet
   tick, the silence release and the copy guard), the scripted partner's and the
   synthetic host's `carry` commands (and `walk` on the host), the tests.
8. **Review fixes before the gates**: a leaver's body is kept where it lies (not
   put back); the ground probe skips the body itself and reads terrain and static
   geometry only (a ray that hit the body called a floating body "lying"); the
   stealth route's pick-up spot is the player's feet; the game confirms its carry
   every 2 s and the agent sets down on the other screens a carry not confirmed
   for 2 minutes (the next confirmation picks it up again); the method lookups in
   the 5 Hz probe are guarded (an older suite's player stub has no
   `IsCarryingCorpse`).
9. **Review fixes after the first full gate run**: the loser's put-back (2 s
   after its put-down) now leaves a body alone that the winner's figure has been
   given here — it moved a carried body and cut that figure's hold to the 5 s
   grace (three new suite checks; they fail on the old code, 85 passed / 3
   failed, and pass on the new); the dice-keys outcome the launcher logs keeps no
   folder of the machine in an exception's text (`KeybindPak.NoPaths`, one unit
   test).

### Side effects

* `release\KCDMP` (git-ignored) was rebuilt by `tools/Publish-Release.ps1` for the
  payload smoke; it still says 0.42.5 (the version changes in Stage B).
* The payload smoke ran the published relay and agent on a temporary copy, on a
  free loopback port, with no game; both exited and the copy was deleted.
* The scratchpad holds: the vanilla copies of the two config files (for the
  diff), the scan database (`gamedb.sqlite`, 594 MB) and its reports, the census
  extracts, a scratch mod folder with a test-built `kdcmp_keys.pak`, the header
  script. Nothing of it is committed.
* `kdcmp/Data/kdcmp.pak` (tracked) rebuilt: 3 entries.

## Gates (Stage A)

On the working tree of the Stage A commit (the mod pak and the native build
rebuilt from it first):

| gate | result |
|---|---|
| 41 `Test-*Synthetic.ps1` suites | all pass (`Test-WO148Synthetic` 88/88; `Test-WO1005Synthetic` prints its own 33/33 and a trailing wrapper "0 passed", as before) |
| `Test-WO106ConsolePlaceholder.ps1`, `Test-WO110LuaLocals.ps1` | 7/7, 6/6 |
| relay round trip (`KcdMp.Relay.Tests`) | 60/60 |
| agent unit tests (`KcdMp.Client.Tests`) | 752/752 |
| native unit tests (`KCDMP_NativeTests`, the boundary check included) | 328/328 |
| the payload smoke (`Test-PayloadSmoke.ps1 -Payload release\KCDMP`) | pass: coherence over 1,065 assembly entries (16 informational version differences, as before), `RELAY-SMOKE ok ... protocol=v10 release=0.42.5`, no load failure in either log |
| `Verify-Install.ps1` against the payload and a scratch mod folder | every WO-148 marker present (agent, DLL, launcher, pak); the published agent built `kdcmp_keys.pak` there (`written`, 10 actions, from `IPL_GameData.pak`); only the two installer layers fail (no Setup ran on that folder) |

The smoke script's own default (`$PSScriptRoot` in its `param` block) comes out
empty under Windows PowerShell 5.1 when the script is started with `-File` and has
`[CmdletBinding()]`, with or without the new header (checked with small scripts:
with `[CmdletBinding()]` empty both ways, without it set; `Publish-Release.ps1`
has none), so the payload is passed explicitly.

## Stage B (live, after the maintainer's OK)

The maintainer answered "OK" to the Stage A stop. The runs, what they showed and
the defects they found: `docs/WO-148-findings.md` section 4.4. Frames:
`docs/wo148-shots/`.

**What it touched, and how it was put back:**

* **Saves**: every playline backed up first (318 files, sha256 list). One
  throwaway playline, `playline4`, of copies of four Modding Tools saves of
  `playline1` (an open field near a village, the sack task `socky` at "carry the
  sacks", the hunter quest at "save the hunter", an early `zachrana` step), under
  a save lock in every session (re-added after each load: a lock does not survive
  one). The plan's `playline0` saves were not used: their headers lack the
  Modding Tools build's `Configuration` attribute — they are the retail game's,
  which the standing rules forbid (the 173 saves at 1.5.5 carry it). The game
  autosaved once into `playline4` while a save loaded (before the lock was back).
  After every session: no real playline file new or changed; at the end
  `playline4` was moved out to the scratchpad and the full check read **318 of
  318 files unchanged, none missing, none new**.
* **The Modding Tools `Mods\kdcmp` folder**: backed up (SHA-1 `9ba56db…` for the
  pak, `18de23e…` for the manifest, as WO-147 recorded them); each test build's
  pak installed with the game closed, and `kdcmp_keys.pak` built there by the test
  agent (`--keys-pak`: written, 10 actions, from `IPL_GameData.pak`); at the end
  the original pak and manifest were put back (the same SHA-1s) and the keys pak
  removed. The folder's other mod was not touched.
* **The game window**: the Modding Tools game, started minimized each time; it
  restored itself full-screen and took the foreground at every start (the game's
  own doing); one push-down after the load each time (to the bottom of the
  window order, without activating it), nothing more. No key or mouse input:
  `mp_carry_test` and the game's own Lua callbacks as stand-ins. Frames by
  PrintWindow of the game window only; the camera by `PlayerSetViewAngles`.
* **Steam**: the first start failed (`SteamApi_Init failed`, the game quit at
  once: the Modding Tools game needs the Steam client); the maintainer started
  Steam, which was left running.
* **The game folder**: one NPC trace file the seventh hook's check wrote was
  moved to the scratchpad; the DLL's logs and `kcd.log` as every run writes them
  (the earlier `kcd.log` was copied first).
* **Processes**: a relay on TCP 7779 / HTTP 5274, the agent, the scripted partner
  or the synthetic host, all from the scratchpad builds; all stopped after every
  session. The maintainer's launcher, agent and relay were not running; nothing
  of theirs was stopped.
* **Hot loading**: the fixes of H2 and J1 were proven by loading the mod's
  WO-148 section into the running game (WO-48's method), then on fresh builds
  (J2, J3: `w148d`, `w148e`).
* **The frame rate** fell during the host sessions (findings section 4.4); the
  maintainer asked for it to be left for later.

## Gates and the build

On the final tree (VERSION 0.42.7, the maintainer's number; the mod pak rebuilt
from it): all 41 `Test-*Synthetic.ps1` suites (`Test-WO148Synthetic` 112/112),
both static checks (7/7, 6/6), the relay round trip 60/60, the agent unit tests
769/769, the native unit tests 328/328 (the boundary check included) — 46 gates,
all green; the local publish and the payload smoke (`RELAY-SMOKE ok ...
protocol=v10 release=0.42.7`, no load failure in either log); `Verify-Install.ps1`
against the payload: all 24 WO-148 marker checks present (only the two installer
layers fail on a folder no Setup ran on).

The installer build ran every gate again inside a fresh clone of `origin/main`
(at `8981dd3`): all green there too (the native DLL built from scratch, the
smoke relay at `release=0.42.7`), and `release\KingdomComeTogether-Setup-0.42.7.exe`
(95.8 MB, SHA-256 `02714b92…f80b7ac`) sits beside 0.42.5's in the git-ignored
release folder. No GitHub release. Its payload (1,026 files) was swept for the
field bundles' player and Steam names, the Windows user names, the addresses,
and any profile path or private address: none of ours in any file. The only
profile path is inside the six NAudio DLLs (the NuGet package's own files,
unchanged: the library author's build path); the only private-range address is
the documented example in the master server's settings.
