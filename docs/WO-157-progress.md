# WO-157 — progress

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

The answer and the evidence: `docs/WO-157-findings.md`. Unattended run (the WO's rule): no stop to ask; the judgement
calls are under "Decisions made unattended" in the findings.

| part | status | where |
|---|---|---|
| the evidence | 13 zips + 3 screenshots read (five parallel log reads, each line re-checked); the WO's `-180859`/`-190834`/`-214442` were not among them: `-002406` (host) / `-002426` (joiner) are the Troskowitz pair | findings, the table |
| 1 crime that wasn't | the host's world decides a trespass (area labels, live); the joiner's warning hidden; the kept record drops his trespass reports; the first walk-away told | findings 1.1, 1.2 |
| 2 fresh installs | no injector exe (in-launcher load, live both roles); the file check (right folder, what/where, LAUNCH ANYWAY, the working folder); VC++ 2013 step; start saves (host key, hosted saves, a new character); Steam codes; signing prepared | findings 2.1–2.6, `docs/CODE-SIGNING.md` |
| 3 the fresh-install pair | no far ForceMount; the connection line (live); snaps per window; NPC desync recorded | findings 3.1–3.4 |
| 3b the 10-06 session | blows land; one-shots stop for a fight; look IK off (live); talk reset; sleeps never cut (live), rest topped up (live); fast-travel text; "saved" line; what to install | findings 3b |
| the build path | `Build-Installer.ps1 -ReleaseCandidate` (only the soak skipped, logged in `release\BUILD-<v>.log`, `RELEASE-CANDIDATE-<v>.txt` and `install-verify.txt`); optional Azure Artifact Signing | findings 2.6, `tools\CodeSigning.ps1` |
| docs | findings, this page, release notes (`docs/releases/RELEASE-NOTES-0.45.2.md`), QUICKSTART, the checklist §WO-157, `docs/CODE-SIGNING.md`, `Verify-Install.ps1` markers | — |
| VERSION, the installer | **0.45.2** (the maintainer's string, asked at the end: blank in the WO); a release candidate built from a fresh clone | "The build" below |

## Work log

1. **Setup.** The zips and screenshots copied to the scratchpad; 397 save files backed up with a sha256 list; the
   installed mod files backed up; Steam started silently (it was not running; the Modding Tools need it).
2. **The evidence**: five parallel reads (crime, install, the fresh-install pair, combat, talk/sleep), each claim checked
   against the log line.
3. **Code**, phase by phase: launcher (`Home.Wo157.cs`, the message dialog, `Program.cs`), the setup library
   (`GameInjector.cs`, `VcRuntime2013.cs`, the checklist), the agent (`GameBridge.Wo157.cs`, `Wo157.cs`, the crime record,
   the sleep vote, the hit path, world identity, Steam codes), the DLL (`wo139.cpp` the HUD gate, `hits.cpp` the stamina),
   the mod (`kdcmp.lua`: the WO-157 block, the judge, the stop grace, the mount guard, the connection line, the snap
   counter, the talk reset, the look IK, the rest top-up, the fast-travel line), the build (`Build-Installer.ps1`,
   `CodeSigning.ps1`, `Publish-Release.ps1`, `KCDMP.iss`, `Verify-Install.ps1`).
4. **Tests**: agent `Wo157Tests` + `Wo157WorldTests`, setup `Wo157Tests`, `Test-WO157Synthetic` (41), `Test-WO157Static`
   (17), the changed expectations of WO-139/WO-154Game/WO-1005/WO-154 tests.
5. **Live** (solo, throwaway save, saves locked): the host's label probe and judge, the in-launcher load (host and joiner),
   the joiner's hidden warning, the look IK, the connection line, the sleep vote's "woke", the rest bisection (8 game
   processes) and the top-up. Two moves left Henry under the ground (the maintainer saw it); fixed, and every later move
   held him on the ground.
6. **Housekeeping**: the maintainer's mod files restored byte for byte (sha1 `b31dc179…`); the throwaway playline's five
   files unchanged; no other save changed except six old autosaves Steam Cloud put back into `playline1` (left alone).

## Housekeeping notes

* Steam is left running (it was started for the Modding Tools).
* The harness (scratchpad `h/`): `tpsafe.py`/`tphold.py` (moves held on the ground), `labels.py` and `area157.py` (the
  area labels), `inj157` (the launcher's own loader against the game), the WO-140 pipe tool for the sleep bisection.

## The build

* **0.45.2, a release candidate**, built from a fresh clone of de72cd0 (`release\c0452`, a short path in the git-ignored
  release folder) with `tools\Build-Installer.ps1 -ReleaseCandidate`: `release\KingdomComeTogether-Setup-0.45.2.exe`,
  102,157,651 bytes, sha256 `c3b3a4be738a646ace4f867805b28866b4113686240b9569cd4cd51bcf33ced2`. **Local only**: nothing was
  uploaded; **not tagged** (the maintainer tags after his two-player session).
* **Not soak-tested** (the work order's rule: the soak runs only before a public release): `release\RELEASE-CANDIDATE-0.45.2.txt`,
  the build log `release\BUILD-0.45.2.log` (and the console capture `BUILD-0.45.2.console.txt`) and the installer's own
  `install-verify.txt` say so. Every other gate ran inside the build: relay 62, agent 1,119, setup 72, the 47 synthetic
  suites (none blind: no `0 passed, 0 failed`), the static checks (7, 6, 7, 17), native 406, the payload smoke
  (`RELAY-SMOKE ok ... protocol=v10 release=0.45.2 rtt_ms=18`), Steam detection and the four installer cases.
* **Unsigned**: no signing settings on this machine (`Code signing: OFF` in the build log). `docs/CODE-SIGNING.md` is the
  maintainer's setup.
* **The payload**: 1,030 files (0.45.1: 1,031 -- `KCDMP_LauncherInjector.exe` is gone; `KcdMp.Setup.dll` ships, holding the
  in-launcher loader). The privacy sweep of every file (the field's and this machine's names, profile paths, Steam IDs,
  private IPv4, ASCII and UTF-16): the same third-party matches as 0.45.0 and 0.45.1 (assembly versions `10.0.0.0`/`10.1.0.0`,
  the master server's config example, a word in WPF's tables, .NET and NAudio build paths) and nothing of ours.
* **To install it on this machine**: close the launcher, agent, relay and game; run the Setup; then
  `tools\Verify-Install.ps1` (it prints the release-candidate note and the WO-157 markers).
