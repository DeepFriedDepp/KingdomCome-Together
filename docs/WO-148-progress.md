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
| 2 — the port-aware setup | done (Stage A); all hooks arming live: Stage B | findings §3 |
| 3.1 — the carry census | done | `docs/WO-148A-carry-census.md` |
| 3.2 — carrying on the other screen | built and tested offline; the live proof: Stage B | findings §4 |
| 3.4 — the live runs | **waiting for the maintainer's go-ahead** | "Stage B plan" below |
| gates, docs, build | Stage A gates green; the installer after Stage B | "Gates" below |

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

## Stage B plan (after the maintainer's OK)

What it touches: **the saves folder** (a full backup first, with a sha256 list;
one throwaway playline of save copies, deleted afterwards), **the Modding Tools
`Mods\kdcmp` folder** (the current one backed up, the test build installed, the
original put back at the end), **the game window** (the Modding Tools game,
started minimized, never brought to the front, captured with PrintWindow only;
one push-down at most after a load), a relay on a free local port, our agent and
the scripted peers. No key or mouse input: console stand-ins only. The retail
game is never launched or touched.

* **Saves**: the throwaway copies come from the old 1.1.1 playthrough
  (`playline0`), never from the prologue (`permanent001`): a save in a sack task
  (the tracked quest `karelNesePytel`, its sack to carry and drop), a save in a
  burial quest (`sedmStatecnych2` or `zachrana`), and a save near dead bodies for
  the plain body runs.
* **H1 — host + scripted partner** (the real game hosts; `avatarpeer` joins as the
  partner): the partner's avatar stands by a dead body; `carry grab dead <body>`
  → the host's real body on the avatar's shoulder (frame); `move` → the avatar
  walks with it (frames); `carry put put <body> x y z` → the body lands there
  (`MP-CARRY land`, the body's position, a frame). Then an unconscious body
  (knocked out by the console), a far copy (fetch), a refusal (the host's own
  player carries it by `mp_carry_test grab`, then the partner's grab is refused:
  `refused: this host's player carries it`), a body set down in the air (put
  back), the partner leaving mid-carry, and the sack (`carry grab object …`, the
  avatar's hands; `carry put drop …`, the prop).
* **J1 — joiner + synthetic host** (the real game joins from the main menu;
  `synthpeer --join-host125` hosts from a reseeded copy): `npc <body> … 1` streams
  a dead body; `carry grab dead <body>` and `walk` → on the joiner's screen the
  host's avatar picks up the joiner's copy and carries it (frames); `carry put` →
  it lands; then the joiner's own carry (`mp_carry_test grab <body>`) refused by
  the host (`carry refuse`) → put down and back; both grabbing the same body (the
  joiner carrying, the host's grab arrives) → the joiner loses; the sack on the
  host's avatar.
* **The quest reaction (3.3)**: in the burial save, the partner carries the
  quest's body to the grave on the host; whatever the host's world does is logged
  (`MP-CARRY quest-reaction`).
* **The hooks arm**: every shipped hook's install line in the DLL's log (the
  boundary check passes them all), and the new log column.
* **The dice keys**: the game loads `kdcmp_keys.pak` (`[Mod] Opening paks in …`
  in kcd.log) and our actions exist (the action-map list cvar, by frame).
* Then the payload smoke, every gate again, the docs, the version (0.42.7) and the
  installer from a fresh clone of `origin/main`, with the privacy sweep of the
  payload.
