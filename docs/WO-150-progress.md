# WO-150 progress — painless first-run setup

`VERSION` was left blank in the order: **commits only, no installer, `VERSION` untouched**
(still 0.43.0). Nothing pushed. Stage A is complete; Stage B waits for the maintainer's OK.
Findings: `docs/WO-150-findings.md`. Workspace map: `docs/WO-150A-workspace-map.md`.
Player page: `docs/QUICKSTART.md` (linked from the top of the README).

## Stage A — done (no game, no change to the maintainer's installs)

### What was read (read-only)

- `<MT>\Data`, `<MT>\Localization`, `<MT>\Data\Levels\*` on the maintainer's machine: 91
  copies, 83.6 GB, no links (`docs/WO-150A-workspace-map.md`).
- Both appmanifests, `libraryfolders.vdf`, Developer Mode (off), the account's token
  (admin, split), Steam's `ActiveProcess` values (only whether they were set; values never
  printed), the installed .NET runtimes.
- `WorkspaceSetup.exe`: decompiled with ilspycmd into the scratchpad, read, not committed,
  not run.

### What was built

| Piece | Where |
|---|---|
| Shared setup code: manifest/vdf reading, Steam + game + Modding Tools detection, workspace verify, linker, tool runner, the flow, mod check/placement, the checklist state machine, redaction, the installer's report | `dotnet/KcdMp.Setup/` |
| `KcdMpSetup.exe` (NativeAOT, WinExe, 2.9 MB): `detect`, `check-exe`, `check` (dry run), `link` (the elevated step), `tool` (the real tool, evidence run) | `dotnet/KcdMp.SetupHost/` |
| Unit + synthetic tests (63) and the fake WorkspaceSetup | `dotnet/KcdMp.Setup.Tests/`, `dotnet/KcdMp.Setup.FakeTool/` |
| Launcher: background check at start, the checklist modal, CHECK SETUP, the Host/Join gate, auto-actions, redacted step log | `KCDMP_launcher/Pages/Home.Wo150.cs`, `Components/Modals/SetupChecklistModal.razor(.css)`, small edits in `Home.razor(.cs)`, `Home.Wo127.cs`, `StatusBar.razor` |
| Installer: detection through the helper (Pascal detection deleted), no dead end, mod held back unless linked, the hand-off page | `installer/KCDMP.iss`, `installer/SteamDetect.iss` |
| Build: the helper and the staged mod in the payload; three new gates | `tools/Publish-Release.ps1`, `tools/Build-Installer.ps1` |
| Installer tests: the probe with the four cases; an isolated end-to-end harness; the Stage B evidence harness | `installer/tests/SteamDetectProbe.iss`, `tools/Test-InstallerDetect.ps1`, `tools/wo150/Test-SetupCases.ps1`, `tools/wo150/Run-RealToolEvidence.ps1` |
| Verify-Install knows a held-back mod; Test-InstallerUpgrade's fixture has the game and a link | `tools/Verify-Install.ps1`, `tools/Test-InstallerUpgrade.ps1`, `tools/Test-Installer.ps1` (doc) |

### Proof

| Suite | Result |
|---|---|
| `dotnet test dotnet\KcdMp.Setup.Tests` — manifests, vdf, detection, link checks (missing / hard link / copy / stale copy / stale hard link / broken symlink), linker (same drive, read-only stale copy, different drive, refused symlink, in use), the flow (pipe route, ReadKey route, stall route, different drive + one elevation, declined, already linked, no .NET 6, evidence run with N), mod check/placement (dev build, held back, prune, junction never followed, foreign, hand-built pak warns), checklist states, the four installer cases, redaction, timing | **63/63** |
| `tools\Test-InstallerDetect.ps1` — the compiled installer wrapper + the AOT helper on 13 fixtures (incl. the four cases), Browse, and this machine read-only | **26/26** |
| `tools\wo150\Test-SetupCases.ps1` — the shipping `KCDMP.iss` compiles; a Setup compiled from it (own AppId/key, no shortcuts, no process gate) installs each of the four cases silently on fake libraries, then uninstalls | **41/41** |
| `KcdMpSetup.exe check` on the real machine (read-only) | Ready, 91/91 copies, **36–56 ms** |
| Rehearsal of `tools\wo150\Run-RealToolEvidence.ps1` on a fake workspace with the ReadKey fake tool | CannotDrive in 261 ms, 0 tool windows, focus never on the tool, listing identical |
| Solution build (`KCD2-MP.sln`) | 0 errors; the 12 warnings are all pre-existing (none in WO-150 code) |
| Checklist look | Static mock in the launcher's own CSS/frame, viewed in the in-app browser pane (mock deleted) |

Not run, and why: `tools\Test-InstallerUpgrade.ps1` and `tools\Test-Installer.ps1`
refuse a machine with a registered install and kill the launcher, agent and relay in silent
mode; `Test-SetupCases.ps1` covers their four-case question in isolation. Their fixtures
were updated for WO-150 but are unexercised. The launcher itself was not started (it opens a
maximised window: a focus change).

### Decisions made unattended

1. **The tool stays the first route although it cannot be driven.** The order is the WO's;
   detection costs 0.3–2 s (and Steam shows the Modding Tools as running for a moment, its
   `SteamClient.Init`). A future pipe-drivable version would be used as Warhorse intends.
2. **No pseudo-console (ConPTY).** It would make `ReadKey` work without a window, but it is
   still typing into a console; the rule is "a pipe, or the tool is not used".
3. **Hard links on one volume, symlinks across volumes, copies never.** Hard links need no
   rights and no space; a Steam update that replaces a pak leaves a stale hard link, which
   the startup check catches and relinks without a prompt. Copies would cost 84 GB.
4. **A copy counts as set up when it has the game file's size and modified time** (what the
   tool's `C` produces; hashing 84 GB at startup is impossible). The maintainer's 91 copies
   pass and are left exactly as they are.
5. **Steam's sign-in is "unknown" when `ActiveUser` is 0** (it is 0 here with Steam
   running), and the Steam step is only required while something still has to come from
   Steam — so a closed Steam never makes the checklist appear on a finished setup.
6. **What the launcher starts by itself:** Steam with `-silent` (no window) once, when it is
   needed; Steam's install window once per launcher session per app (the game first, the
   Modding Tools once the game is installed or downloading); linking and placing the mod
   whenever possible; the UAC prompt when Windows refuses a symlink, with the explanation
   shown in the checklist at the same moment. Declined: "Ask again".
7. **Disk space:** about 17 GB plus 3 GB margin on the game's library drive (where links
   are free). Too little: "Free up N GB, or choose another drive in Steam's install window"
   with that button; another drive means symlinks and the one prompt.
8. **Step 6 blocks only on** Setup's own FAIL verdict, missing mod files, or a foreign
   `kdcmp`. Files that differ from what Setup shipped (a hand-built pak, hand-copied DLLs)
   **warn and are never overwritten** — the launcher's existing InstallIntegrity rule, and
   the maintainer's normal state while developing.
9. **A foreign `kdcmp` is decided by its `mod.manifest`** (ours has never changed between
   releases), not only by the registry marker.
10. **The startup app check is sizes only**, not sha256 of ~700 files (time). Setup's
    install-time verdict is the sha256 check.
11. **Locked Host/Join look unchanged.** Clicking one before Ready opens the checklist,
    which says why; no restyle. **CHECK SETUP** is one more status-bar item in the existing
    style.
12. **`CheckGamePathOnStartup` is gone.** It opened Settings with an error on a missing game
    path; now the detected Modding Tools exe is written into `settings.json` and the
    checklist covers the rest. The retail-path refusal at launch time is unchanged.
13. **The installer's Modding Tools page informs instead of gating;** its "Get it on Steam"
    button is gone (the launcher opens Steam's window), Re-check and Browse stay (Browse is
    validated by the helper). `GamePath` / `ModsPath` are written to the registry only when
    known / placed; the launcher writes both after placing a held-back mod.
14. **The helper is NativeAOT** so Setup can run it before any runtime exists. Publishing it
    needs the MSVC linker, found through vswhere: `Publish-Release.ps1` puts
    `...\Microsoft Visual Studio\Installer` on PATH.
15. **The Stage B evidence run answers N to the delete prompt** (the WO says A). A is what
    the launcher's real route sends; over a complete workspace N guarantees "after = before"
    even if the tool could be driven. The expected outcome is the same either way: the tool
    stops at its first prompt.
16. **Redaction keeps Steam library paths** (with any user name in them replaced): they are
    what a setup report is about.
17. Setup and SetupHost are in `KCD2-MP.sln`; the tests are not (the repo's convention).
18. Not pushed. Commits only, on `main`.

## Stage B — run after the maintainer's OK ("go ahead with everything", UAC allowed)

- **B1a** dry run, Steam side: Ready, 91/91 copies, 23 ms.
- **B1b** dry run with the installed launcher (`--app-dir`): **waiting for the maintainer**.
  The app's Terminal panel never reached a prompt twice, so the command could not be run
  in the maintainer's session from here.
- **B2** the real tool, unelevated: **new finding** — its manifest says
  `requireAdministrator`, so Windows refuses to start it with pipes (740) before it runs.
  0 differences in 95 `<MT>` entries, no window, focus never on it. The runner now names
  this case (`NotStarted`, "it requires administrator rights...").
- **B2b** the real tool started with pipes by the helper elevated (one UAC prompt, answered
  Yes): `CannotDrive` in 544 ms at `ReadKey`; 0 differences; no window; focus never on it.
  `Run-RealToolEvidence.ps1 -Elevated` added for this.
- **B3** the elevation path on a fixture, with `--kind symlink` added so the elevated step
  goes straight to symlinks (it exists only where hard links are impossible): ended
  **Declined** twice (the second after 122 s with the prompt up). The declined path is
  observed and correct; the **Yes** path is not yet.
- **B4** gates: 64/64 setup tests, 26/26 detection probe, 41/41 installer cases, 824/824 agent
  tests, solution build 0 errors.

## The stop — Stage B plan as approved

Nothing below starts a game, touches a playline or stops the launcher, agent or relay. No
window is brought to the front by any of it.

| # | Run | Who | Touches | Expected |
|---|---|---|---|---|
| B1a | `KcdMpSetup.exe check` (dry run) | me | reads `D:\SteamLibrary` only | Ready, 91/91 copies (already seen in Stage A) |
| B1b | `KcdMpSetup.exe check --app-dir "%LOCALAPPDATA%\KCDMP"` (dry run with the real install) | **the maintainer, in their own terminal** (my shell's view of that folder is sandbox-redirected) | reads the install's manifest and verdict and `<MT>\Mods\kdcmp`; writes one report file in the repo | Ready; step 6 Done (possibly "N files differ" if a hand-built pak is in) |
| B2 | `tools\wo150\Run-RealToolEvidence.ps1` — Warhorse's real WorkspaceSetup.exe once, through the launcher's runner, answers S then N | me | could only touch `<MT>\Data` and `<MT>\Localization`, and with N it keeps every file; the full listing is recorded before and after | the tool stops at its first prompt (`CannotDrive`), listing identical, no window, focus never on it; Steam shows the Modding Tools "running" for about a second |
| B3 *(optional)* | one real UAC prompt: the elevated `link` step on a scratch fixture (8 tiny fake paks in the scratchpad) | me starts it; **the maintainer clicks Yes** | the scratchpad only | 8 symlinks made elevated, nothing else; the secure-desktop prompt does take the screen while it is up |
| B4 | gates: `KcdMp.Setup.Tests`, `Test-InstallerDetect.ps1`, `Test-SetupCases.ps1`, `KcdMp.Client.Tests`, solution build | me | scratch folders only | all green |
| B5 | docs (findings/progress with the Stage B evidence), commit | me | the repo | — |

No installer is built (blank `VERSION`). Out of scope and not planned: converting the
maintainer's 91 copies to hard links (it would free 83.6 GB, but it changes the install).
