# WO-150 findings — painless first-run setup

Evidence marks: **[O]** observed on the maintainer's machine (read-only), **[R]** read from
Warhorse's tool (its decompiled IL, read-only; nothing of it is in this repo), **[S]**
synthetic test in this repo (fake Steam library, fake tool), **[U]** not verified yet.

## The answer

1. **Warhorse's Workspace Setup cannot be driven through a pipe — twice over.**
   - Its manifest says **`requireAdministrator`** **[O]**. Windows refuses to start it from
     a normal process with its streams redirected (`ERROR_ELEVATION_REQUIRED`, 740) — in
     milliseconds, before anything runs, with no prompt and no window **[O]** (Stage B2, the
     real tool against the real install). An elevated start through UAC ("runas") cannot
     redirect streams at all.
   - Started with pipes by an **elevated** parent, it reads its first answer with
     `Console.ReadKey()` and dies: `InvalidOperationException: Cannot read keys when ...
     console input has been redirected`, 544 ms, after printing its two folders and the
     prompt and before changing anything **[O]** (Stage B2b, one UAC prompt; the `<MT>`
     listing identical before and after, 95 entries).

   WO-150 allows no other way in (no keystrokes, no visible console), so the launcher **tries
   the tool first, recognises either refusal at once, and then makes the same links
   itself**. If a future version reads standard input and drops the admin requirement, the
   same code drives it (proven with a pipe-drivable fake **[S]**). The admin requirement also
   explains the tool's own symlinks: started from Steam it always runs elevated, so `S` works
   there without Developer Mode.
2. **What the tool makes:** for every `*.pak` directly in `<GAME>\Data`,
   `<GAME>\Localization` and each `<GAME>\Data\Levels\<level>`, the same path under `<MT>`:
   a **symlink** if answered `S`, a **copy** if answered `C` **[R]**. Nothing else is touched
   **[R][O]**. The maintainer's machine has **91 copies, 83.6 GB** (the `C` answer), made on
   2026-08-26 **[O]**. Full list: `docs/WO-150A-workspace-map.md`.
3. **The admin question.** A symlink needs **Developer Mode or admin rights**; a hard link
   needs neither but works only on the same volume. This machine: Developer Mode **off**
   (no `AppModelUnlock` key), the account an administrator with a split token (so one UAC
   prompt can elevate) **[O]**. An unelevated symlink here fails with
   `ERROR_PRIVILEGE_NOT_HELD` (1314) **[S]** (a real call, in the tests).
4. **What the launcher makes instead:** **hard links** when the game and the Modding Tools
   share a volume (no admin, no Developer Mode, **no disk space**, and the game cannot tell
   one from the file). **Across drives** a hard link fails (`ERROR_NOT_SAME_DEVICE`), so it
   makes **symlinks** (the tool's own `S`): silently with Developer Mode, otherwise through
   **one UAC prompt** that runs only the link step elevated **[S]** (cross-drive simulated:
   this machine has one Steam volume; the UAC prompt itself is **[U]**, see Stage B).
   **Copies are never made** (84 GB is not a setup step), but an existing copy that still
   matches the game's file is accepted as set up: the maintainer's machine needs nothing.
5. **One detection code.** Setup and the launcher both run `dotnet\KcdMp.Setup`: the launcher
   in-process, Setup through `KcdMpSetup.exe` (the same code as a 2.9 MB NativeAOT exe, so it
   runs before any .NET runtime is installed). The old Pascal detection is gone **[S]**:
   26/26 probe checks and 41/41 end-to-end Setup checks on fake libraries.
6. **No dead end.** Setup no longer refuses without the Modding Tools: it installs the
   launcher, agent and the rest, **holds the mod back** unless the Modding Tools are
   installed **and linked**, and says the launcher will finish. The launcher's checklist then
   installs what is missing through Steam's own install window, links the workspace, places
   the mod **[S]**. The "114 tables are not loaded" crash of an unlinked Modding Tools
   (README's "Not done") is now a checklist step instead.
7. **Nothing visible when all is in place.** The startup check reads manifests, link
   identities and file sizes: **36–56 ms** on the maintainer's real install **[O]**, and
   reports Ready with nothing to do **[O]**. The checklist appears only when a check fails.

## How the tool works **[R]**

`<MT>\Tools\ModdingWorkspaceSetup\WorkspaceSetup.exe` (assembly version 1.0.0.0, net6.0
console app, 8.7 KB of IL, Facepunch.Steamworks beside it; its exe manifest requests
`requireAdministrator` **[O]**, so every normal start is a UAC prompt):

1. Initialises Steam **as app 2429020** and asks Steam whether 1771300 and 2429020 are
   installed and where. Not installed: prints a line and waits for a key.
2. Prints both install folders ("Modding tools installed in:", "Game installed in:"); the
   Steam API itself prints `SteamInternal_SetMinidumpSteamID: Caching Steam ID: <id>`.
   **Both are personal; both are redacted** before anything reaches a log (below).
3. Asks `Do you want to [C]opy packs or [S]ymlink them?.` (`ReadKey`, loops until S or C).
4. Mirrors the three places in item 2 of the answer. For each target that exists:
   `Delete File [Y]es/[N]o/Yes to [A]ll:` (`ReadKey`; `A` stops the asking).
5. `S`: `File.CreateSymbolicLink` (target = the game file). `C`: `File.Copy`.
6. Ends on another `ReadKey` ("press a key").

No command-line arguments, no configuration file. It needs **the .NET 6 runtime** (no
roll-forward); without it the host prints an error and exits, so the launcher checks for
`shared\Microsoft.NETCore.App\6.*` first and skips the tool when it is missing (this
machine has 6.0.11 and 6.0.36 **[O]**). It needs **Steam running** (otherwise the init
throws). Steam's own Play action for app 2429020 **is** this tool (WO-71 **[O]**), which is
why the launcher never starts it through Steam (`steam://run/2429020`, `-applaunch`).

Quick Edit stalls (maintainer's step 7) cannot happen to it here: it has no console window.
A tool that goes quiet for 20 s is stopped anyway (the stall route, **[S]** 2 s window).

## Which link kind, and the cases that need care

| | same volume | different volumes |
|---|---|---|
| hard link | works, no rights needed | `ERROR_NOT_SAME_DEVICE` |
| symlink | works with Developer Mode or elevated | same |
| what the launcher makes | **hard links**, never a prompt | **symlinks**: silent with Developer Mode, else **one UAC prompt** |

- **A Steam update and hard links.** When Steam replaces a changed pak (new file, same
  name), the Modding Tools' hard link keeps the old bytes. The check catches it at the next
  launcher start (different file identity, different size/time: `Stale`) and relinks just
  that file in milliseconds, no prompt **[S]**. A real Steam update doing this is **[U]**.
  Symlinks follow an update by themselves; copies go stale the same way (caught the same way).
- **Uninstalling the game** while hard links exist: the pak data stays on disk until the
  Modding Tools are removed too (both names point at it). Disk space, not a correctness issue.
- **FAT32/exFAT** libraries have no hard links and no symlinks: the linker reports the
  Windows error in one sentence ("Click Try again; if it keeps failing, send the logs").
- **A running game** holds the paks: linking waits with "Close the game first" **[S]**.
- **Each link is made under a temporary name and moved over the target in one step**, so a
  pak the game needs is never missing, even if the launcher is killed half way **[S]**.
  Nothing outside the expected `*.pak` paths is ever touched; an extra pak in `<MT>\Data` is
  left alone **[S]**.

## The fallbacks, in order (`KcdMp.Setup.WorkspaceSetupFlow`)

1. **Warhorse's tool** — skipped if it is absent, .NET 6 is missing or Steam is not
   running; otherwise a background process with **no window** (`CreateNoWindow`), stdin,
   stdout and stderr redirected, `S` written to the first prompt and `A` once to the delete
   prompt; its (redacted) output streams into the checklist and the log. Judged by
   re-reading the workspace, never by its exit code. The shipped tool ends here at once:
   Windows refuses to start it unelevated with pipes (740), logged as "it requires
   administrator rights (its manifest asks for them), so it cannot be started with a pipe".
2. **The launcher's own links** — for whatever is still missing or stale: hard links, then
   symlinks across volumes.
3. **One UAC prompt** — only if Windows refused a symlink: "Windows needs your permission to
   link the game's files into the Modding Tools", then `KcdMpSetup.exe link` runs elevated
   (a **windowless** exe, so even elevated nothing appears but Windows' own prompt). It
   re-derives the file list itself from the two install roots and refuses anything that is
   not a game install and a Modding Tools install. Declined: the step says so in one
   sentence, with an "Ask again" button.

**Considered and not used:** a pseudo-console (ConPTY) would let `ReadKey` work without a
visible window, but it is still typing into a console; WO-150's rule is "a pipe, or the tool
is not used". Recorded under "Decisions made unattended" in `docs/WO-150-progress.md`.

## What the checklist checks (always the real state, never a saved flag)

1. Steam running and signed in (only required while something still has to come from
   Steam; with everything in place a closed Steam is fine, the game starts it).
   Signed-in cannot be read reliably: `ActiveProcess\ActiveUser` reads **0** here while
   Steam runs **[O]**, so 0 is treated as "unknown", never "signed out".
2. The game (1771300): manifest + its data on disk. Missing: Steam's install window
   (`steam://install/1771300`), opened once per session; busy: progress from the manifest.
3. Disk space: about 17 GB (+3 GB margin) on the game's library drive, before step 4.
4. The Modding Tools (2429020): manifest + the build test (Framework.dll + CrySystem.dll).
   Missing: `steam://install/2429020` (no Tools filter needed); downloading: a progress bar
   from the manifest's `BytesDownloaded/BytesToDownload`, then `BytesStaged/BytesToStage`.
   Whether Steam rewrites those counters live during a download is **[U]**.
5. The workspace (above).
6. The mod: `install-verify.txt` says PASS, and `<MT>\Mods\kdcmp` has the shipped
   `mod.manifest` and `kdcmp.pak` (sha256). A mod Setup held back is placed from the staged
   copy in `<app>\mod\kdcmp`, the uninstaller's `ModsPath` marker written, the keys pak built.
   Files that **differ** from what Setup shipped (a hand-built pak) only **warn**: never
   overwritten, never blocking — the launcher's InstallIntegrity rule.
7. Ready! — Host, Join and Join-through-Steam open the checklist until then.

## Privacy

The tool's output and every setup line go through `KcdMp.Setup.Redact` before the log:
Steam IDs (17 digits, `[U:1:n]`, `steamid:n`), the Windows profile path (this account's and
any `X:\Users\<name>`), the account and machine names. Steam library paths stay (they are
what a setup report is about) with any user name inside them replaced **[S]**. The
appmanifest's `LastOwner` (a SteamID) is never read into memory at all **[S]**. Nothing
personal is in anything committed: the workspace map uses `<GAME>`/`<MT>`, and the fake
tool prints an invalid placeholder ID.

## Stage B evidence (2026-10-02)

| Run | Result |
|---|---|
| B1a dry run, Steam side (`KcdMpSetup.exe check`) | Ready, 91/91 copies, 23 ms **[O]** |
| B2 the real tool, unelevated, through the launcher's runner | refused by Windows (740, requires elevation) before it ran; 0 differences in 95 entries; no window; focus never on it **[O]** |
| B2b the real tool, started with pipes by the helper elevated (one UAC prompt) | `CannotDrive` in 544 ms at the first prompt (`ReadKey` on a pipe); 0 differences; no window; focus never on it **[O]** |
| B3 the product's elevation path on a fixture (hard links failing as on two drives, the real unelevated symlink refused with 1314, then `ElevatedLinker` through UAC) | twice **Declined** (once quickly, once after 122 s with the prompt up): the flow ended in the plain sentence with an "Ask again", nothing linked **[O]**. The **Yes** path is not yet observed |

Evidence files (redacted, git-ignored): `release\wo150-evidence\`.

## Not verified yet

- **The UAC Yes path** of the elevated step (B3 ended Declined twice; the elevated `link`
  verb itself is the same code the unit tests run unelevated).
- **The dry run against the installed launcher** (B1b, `check --app-dir`): it has to run in
  the maintainer's own session, because this shell's view of `%LOCALAPPDATA%\KCDMP` is
  redirected.
- A real Steam download's progress counters, and `steam -silent` starting Steam without a
  window. Need a machine without the Modding Tools.
- From this shell, `HKCU\Software\KCDMP` and the install's uninstall entry are not visible
  (consistent with the sandbox redirection notes); nothing here relies on reading them.
