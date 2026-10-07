# WO-159 — New Game without the prologue: Start Game / Join Game on the main menu

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

Marks: **[L]** live, solo, on this machine's Modding Tools build (1.5.5), throwaway saves only, every playline backed up
first (403 files, sha256) and checked after every run; **[harness]** the launcher's driver run headless (see "The live
runs"); **[unit]** / **[syn]** / **[static]** a test that gates the build; **[code]** read in our code or the game's
files; **[obs]** seen in the game's own data or binaries; **[not tested]** no run. No names, paths or addresses of
players are in this file. Commit only: no VERSION, no installer, no soak.

## The answer

A game the launcher starts shows **Start Game** and **Join Game** at the top of its main menu, with Continue / New Game
/ Load Game greyed ("Use Start Game / Join Game while Kingdom Come: Together is running.") and the three Modding Tools
"New Game Debug …" entries gone. A game started any other way keeps the game's menu exactly as shipped. Start Game
lists the host's own worlds and **New adventure** (the bundled start save, re-keyed into an empty save slot: a new world
each time, Henry right after the prologue). Join Game offers **Join with a new character** / **Bring my character**,
waits ("Waiting for the host…", Cancel) and then runs the existing join. Everything after a choice is code that already
existed (the WO-124 menu load after WO-154's video stop; the joiner's CONNECT behind the WO-154 gate; WO-125's
first-join answer). No native code was added or changed.

| Phase | Result | Mark |
|---|---|---|
| 0 Discovery | Menu is the game's own `Menu` UI element, driven from Lua; launcher-started = the launcher arms it live; saves/re-key/recap answered below | [L][obs][code] |
| 1 Menu takeover | Our two entries on top, shipped ones greyed, debug entries gone, disclaimer on the selected entry; normal start untouched | [L][syn] |
| 2 Start Game | Worlds list (one per playline) + New adventure (staged, re-keyed, loads, saves normally; twice = two keys; unplayed = taken back) | [L][unit][syn] |
| 3 Join Game | Choice page (plain reasons), Waiting for the host… + Cancel, then the existing join | [L] wait/cancel, [syn], [not tested] real join |
| 4 First-run pages | Read-only reader of the active profile; "start once from Steam and accept" message; maintainer's profile = accepted | [L][unit] |
| 5 Start-save pipeline | `tools/Validate-StartSave.ps1` (+ scrubbed copy), release payload entry, docs | [L][unit] |
| 6 Gates and live | 5/5 live gates run (see below); frame rate unchanged | [L] |

## Phase 0 — discovery

### 0.1 How the main menu is built (and changed)

* The page list is data (`Libs/Tables/ui/menu_pages.xml`: `RootMain`, `MaxButtons=8`), the button texts are data
  (`menu_buttons.xml`: `Continue`, `NewGame`, `NewGameDebug`, `NewGameDebugTrosecko`, `NewGameDebugKlaster`,
  `LoadGame`, …), but **which buttons a page gets is native** (GUIModule's `C_UIMenu`): the pages hold no button lists.
  The Flash element `Libs/UI/UIElements/Menu.xml` exposes the functions native code uses: `ClearAll`, `PreparePage`,
  `AddBasicButton(id, container, text, tooltip, disabled)`, `SetDisable`, `SelectButton`, `ShowPage`,
  `AddConfirmation`, and the events `OnButton(id)`, `OnConfirm(id, answer)`, `OnCreditsHide`, `OnHelpOverlayClose`.
  **[obs]**
* **All of it works from Lua at the main menu** (`UIAction.CallFunction("Menu", -1, …)`,
  `UIAction.RegisterElementListener`) **[L]**: `SetDisable` greys an entry; `AddBasicButton` appends (at the end of the
  scroll list); `ClearAll` + `PreparePage` + `AddBasicButton`… + `ShowPage` rebuilds the root in our order; the
  shipped ids keep working from our page (Settings opened the game's own Settings page); `@ui_*` string keys localize;
  `AddConfirmation` shows the game's own question and its answer comes back in `OnConfirm`.
* The game's menu **ignores an id it does not know** (our `MP_*` entries are ours alone) **[L]**, and pressing a
  greyed entry does nothing (the greyed Load Game did not open) **[L]**.
* **The game rebuilds its own root after our listener runs** (our rebuild inside the `Back` callback was overwritten)
  **[L]**, and **no Lua timer fires at the main menu** (`Script.SetTimer` / `SetTimerForFunction` at 300 ms never fired
  while frames ticked at ~100 fps — WO-124's observation, confirmed) **[L]**. So the redraw after one of the game's own
  pages is pushed from outside: the launcher's next tick (≤ 250 ms). The user sees the game's root for that moment.
* No pak file and no native hook are needed: **the pak adds no UI file** (it holds only `kdcmp.lua` and two tables), so a
  game that is never armed loads the game's own menu data byte for byte **[static][L]**.

### 0.2 How the game knows the launcher started it

Chosen: **the launcher tells the running game, live** — it calls `KCD2MP_W159Tick` through the game's own console (the
Modding Tools' local API, the agent's inbound path) while its game sits at the menu. Nothing in `kdcmp.lua` touches the
menu by itself **[syn N1–N4][L run 1: 0 WO159 lines]**. Why not the three listed options:
(a) a command-line argument — Lua cannot read the command line, and the host has no plugin at the menu to read it;
(b) a cvar — `+cvar` arguments apply too late (WO-72), Lua cannot register one, and a stored cvar would outlive the run;
(c) a marker file — the Lua sandbox has no `io`; `loadfile` could read one, but a marker left by a launcher that crashed
would take over the next normal start. A live call cannot go stale: no launcher, no call.

### 0.3 Saves

* Playlines: only `playline0`–`playline4` exist (WO-112); the game lists saves **once, at start**, and `wh_sys_LoadGame`
  resolves only against that list. The menu's own Continue and playline pick rescan (GUIModule), but nothing a host can
  call at the menu does — the plugin's rescan (pipe 0x1D) needs the DLL, which the host only gets at CONNECT. **So the
  launcher stages the start save before it starts the game** **[code][L]**.
* A free playline = no `.whs` in it (folder missing or empty). None free (this machine): New adventure greyed with
  "All five save slots (playlines) hold saves …" **[unit]**. The game's own New Game *deletes* a playline's saves
  (`wh_sys_NoPlaylineDeleting` help text) **[obs]** — greyed in launcher mode.
* **Re-key**: the world's identity is the playthrough seed (body `0x01FB`, WO-112/125). The staged copy gets a new random
  seed (`Inflate` → 4 bytes → `Deflate`, footer re-signed; the game checks no md5, WO-115) **[unit: only those 4 bytes
  change]**. Two New adventures = two worlds: `3e7c536d6e` and `034823ce9a` live, each kept by the game's own next save
  **[L]**. The install key (WO-157) is unchanged and still sent by the host beside the world.
* **The save header names people's machines**: `DebugInfoHistory` carries `UserName` (the account) and `BuildComputer`;
  `UsedMods` lists the mods installed when it was saved **[obs]**. The staged copy clears all three (header metadata
  only); the validator refuses a start save that still has them and writes a scrubbed copy.
* The installer can ship it: the payload is `release\KCDMP\*`, Setup's install manifest walks it recursively (sha256,
  closed set), so `start-save\` beside the launcher is verified like every other file **[code]**. Size ≈ 1.2 MB. It is
  the maintainer's own play data (no game asset files, no code); its distribution terms are the maintainer's call.

### 0.4 "Usable own save"

Reused, not re-written: `GameBridge.ListOwnSaves` (engine names, newest first), `Wo154Rules.IsRegularGameBuild` (WO-154
build rule), the seed tag + Henry store (`HostedHere`, joined worlds — WO-125/157) and `WhsSave.PlayerOf` (Henry, not
the prologue's `player_bohuta`). Per playline the newest save that passes is the world; hidden ones are counted and named
under the list ("Not shown: 1 from the regular game (not the Modding Tools)." on this machine) **[unit][L]**.

### 0.5 Prologue recap

The prologue's rendered videos: `Videos/m50/cin_m5010k_obranabohuta__siege_intro_start`, `Videos/m01/cin_m0110t_
prepadeni__intro_cutscene`, `Videos/m02/cin_m0210t_zachrana__fall_dream_clip01`, `…m0250t…first_dreaming_clip_01`,
`…m0260t…second_dreaming_clip01–03` **[obs]** (order inferred from the names). The engine's `wh_ui_PlayMovie <path>`
**takes the file at the main menu but draws nothing there, and stops the menu's own background video** (a bare name is
refused: "Cannot open specified file as BINK video") **[L]**. So the recap ships as **skip only**: New adventure asks
"Start a new adventure in a new save slot? You start as Henry, after the prologue." (Start / Back); the one place a
recap would start is the stub `KCD2MP_W159Recap()` (returns false, logged).

## What shipped

| Piece | Where |
|---|---|
| The menu (pages, listener, ticks, disarm, recap stub) | `kdcmp/Data/Scripts/Startup/kdcmp.lua`, section `WO-159` (globals only; no new main-chunk local) |
| The launcher's driver (consent, staging, worlds, ticks, choices, unstage at exit) | `KCDMP_launcher/Pages/Home.Wo159.cs`; hooks: 3 lines in `Home.razor.cs`, `KcdLogFollower.OnLine` (`Models/ConnectGate.cs`) |
| The rules (menu model, choices from the log, joiner's words, console client) | `KCDMP_launcher/Models/MenuTakeover.cs` |
| Settings (missing key = default; nothing rewritten) | `MenuTakeover` (on), `StartSavePath` (`start-save`) in `Models/AppModels.cs` |
| The agent's helpers (`--w159 saves|stage|unstage|used|validate`) | `dotnet/KcdMp.Client/Wo159.cs`, `Program.cs` (1 dispatch) |
| The first-run pages reader | `dotnet/KcdMp.Setup/ConsentFlags.cs` |
| Start-save check | `tools/Validate-StartSave.ps1`; release payload step in `tools/Publish-Release.ps1`; `assets/start-save/README.md` |
| Docs | `docs/QUICKSTART.md` step 4, `docs/DISCORD-TEXT.md` (how-to block), `docs/LAUNCHING.md` (one stale step marked) |

## Decisions made unattended

1. **Start Game shows our own page, not the game's Load Game list.** The game's list cannot be filtered per world from
   data or Lua (`wh_sys_LoadGameFilter` filters save *types* only) and cannot be opened from Lua. Our page lists one
   world per playline (its newest usable save, "Playline 2 (Oct 01 14:34)", up to five) — the same rule the WO asked the
   filter to apply. Choosing one loads it with the existing WO-124 call after WO-154's video stop.
2. **New adventure is staged before the game starts**, into the lowest empty playline, for a host launch only, when a
   start save is installed. Unplayed when the game exits (still the bytes written and its playline's only save): taken
   back, folder too. Loaded: a normal save from then on (marked used at the load; kept even before its first save).
3. **The staged copy keeps the bundled file name** when it is an engine name (else `permanent002`), so the game lists it
   and QuickSave numbers on from it (`quicksave003` live).
4. **Join Game's default**: Bring my character when the player has an own Modding Tools save, Join with a new character
   otherwise (the WO's rule); the reason Bring is greyed uses WO-157's words. The final say is still the agent's once the
   host's world is known.
5. **Join Game connects by itself** when the host is ready (the WO-154 gate opens), then sends the choice to the agent's
   existing `/join-choice`. No timeout while waiting; Cancel or our Back stops waiting.
6. **Nobody clicks CONNECT** (changed at the maintainer's word after the first commit: the WO's "Connect becomes
   available" left the host an Alt+Tab). The host's is pressed by the launcher once the world Start Game loaded has
   settled: the WO-154 gate's own rule ("Gameplay started", then 10 s with no new load), only after a load the menu
   asked for, at most once per launch. A save loaded any other way keeps the button. A connect that fails also puts a
   line on the game's own screen (the HUD's info text), since nobody is looking at the launcher then. The joiner's
   connects on Join Game once the host is ready, as before. **[unit]**; **[not tested]** live (the CONNECT step loads
   the DLL and starts the agent: the launcher's window was not driven).
7. **The first-run check stops the launch** when the pages are missing or older than the known accepted version (2): a
   page under a menu this launcher redraws could be wiped by `ClearAll`. No profile determinable: logged, not checked.
   Nothing is ever written.
8. **A menu choice is acted on only while the game is at its menu** (live finding: a replayed log line loaded a save in
   the world, in the harness — see below).
9. **The disclaimer** is the tooltip of the selected entry (Start Game for a host, Join Game for a joiner), so it shows
   once under the menu.

## The live runs (solo)

Steam started by me (it was not running); the game started minimized each time, took the foreground at start and was
pushed down once at most. Throwaway saves only: the WO-157 throwaway `playline4` was moved aside (it was in the backup)
to free a slot and put back byte for byte; the maintainer's worlds were listed on the menu but never loaded. The installed
mod files were backed up and restored byte for byte (sha1); the repo's tracked pak was restored from git.

**The harness.** The launcher is a window, and no input may be sent (WO-150 rule), so its driver was run headless: a
scratchpad program linking the launcher's own `MenuTakeover.cs` and `ConnectGate.cs`, the Setup consent reader and the
agent's `--w159` helpers, with the `Home.Wo159` loop mirrored (the joiner's host never ready: no relay). Menu presses in
the test were made through the menu's own Flash function (`SetInput menu_accept`, `SelectButton`) — a test-only UI call,
never shipped (`[syn D3]` pins that the mod sends none); a real mouse click on our entries is **[not tested]**.

| # | Run | Result | Evidence |
|---|---|---|---|
| 1 | Game started normally | The shipped menu (menu area ±0.3/255 against the morning's baseline frame, the background video aside); `MOD INIT` ran; **0** WO159 lines; pak entries: no UI file | **[L]** |
| 2 | Launcher-started (armed) | Start Game / Join Game on top; Continue / New Game / Load Game greyed with the hint; no debug entries; disclaimer; Settings → the game's Back → our root again on the next tick | **[L][harness]** |
| 3 | New adventure ×2 (placeholder) | staged `playline4/permanent002` (`3e7c536d6e`), the game's question, Start, loaded in 55 s, player = Henry, `Game.QuickSave` → `quicksave003` with the same key. Second run: `034823ce9a`, same results. Third staging (`6234eb7c48`) never played → taken back with its folder | **[L][harness]** |
| 4 | Join Game before any host | Join Game → Join with a new character → "Waiting for the host…" + Cancel → Cancel → the choice page; the launcher saw `join fresh` and `cancel` | **[L][harness]** |
| 5 | Consent reader, the maintainer's profile | `Accepted (licence version 2, telemetry answered)` | **[L]** |

**Frame rate** (WO-148 rule; frames over 12.5 s from the engine's own counters; no crash guard added):
main menu shipped 180.1 fps · armed (4 ticks/s) 180.1 / 180.1 · in the world, one static dialogue shot: driver off
68.7 / 66.3, later 66.5 / 55.4 / 45.0 / 45.0, driver on 45.0 / 45.0. The in-world drift is the scene's (off fell to 45.0
with no driver running); the driver makes no game call outside the menu (0 ticks in the world). Menu tick cost: mean
8.7–9.2 ms per tick for the two console calls, off the game's thread.

**Found live and fixed**: a driver that starts reading an older log replays its old menu choices — the harness, started
in the world, re-sent the earlier New adventure and the game reloaded the throwaway start save (6 s, back in the world;
nothing else touched). The launcher's driver reads only its own game's log from that game's start, but it now also
refuses a load or New adventure unless the game is at its menu (logged); the harness skips lines older than itself.

**Placeholder**: a copy of the throwaway `playline3/permanent002` (the game's own first save after the prologue),
header scrubbed. `Validate-StartSave` on it: FAIL on "no partner / mod data" (a `kcd2mp_6` figure was saved into that
world) and, before scrubbing, on the account/machine name and the mods list; on `permanent001`: FAIL "the player is
player_bohuta (the prologue)" **[L]**. It also starts Henry **inside the "What is your playstyle?" dialogue** — the real
start save should be made after that choice, at a calm spot (recipe updated).

## The gates

* **[syn]** `tools/Test-WO159Synthetic` 56/56 (menu untouched until armed; the root's order and greying; menu-only; the
  host page, the question, a load; the game's own pages and Back; the joiner's default, waiting, Cancel, joining;
  disarm; the recap stub). Picked up by the release gate's `Test-*Synthetic.ps1` glob.
* **[unit]** client `Wo159Tests` 9 (re-key changes only the seed; two stagings = two keys in two free playlines; never a
  playline with any save; refusals; unstage removes / keeps; the worlds list and its hidden counts; hosted ≠ copy; the
  recipe checks) + `Wo159MenuTests` 18 (choices from the log line, refusals, Lua escaping, the model within one console
  call, the joiner's words, the load call, a missing settings key = on); Setup `Wo159ConsentTests` 5 (accepted /
  missing / stale / no profile / reading writes nothing).
* **[static]** `Test-WO110LuaLocals` 6/6 (no new main-chunk local), `Test-WO106ConsolePlaceholder` 7/7,
  `Test-NativeGuards` 7/7, `Test-WO157Static` 17/17.
* **Regression**: all 49 `Test-*Synthetic.ps1` suites pass (0 failed in any); client tests 1,146/1,146; Setup tests
  77/77. The launcher builds (Debug). No relay, native or installer gate was run (no change there; no build).

## Not verified

* **The real start save** (not supplied; the pipeline ran on the placeholder; New adventure greys itself without one).
* **A fresh profile's first-run pages** (no new Windows profile here, as before).
* **Join Game against a live host** (two players): the auto-CONNECT and the posted first-join answer ran only up to
  "waiting" here.
* **A real mouse click / keyboard press** on our entries (presses were the menu's own Flash function).
* **The launcher's window itself** (the driver ran headless in the harness; the launcher builds and its rules are unit
  tested).

## Follow-up design probes (after the first commit, at the maintainer's direction)

The maintainer corrected the cut point and asked for three start saves, a playstyle choice, a pre-rendered-only recap
and a new-character join from the bundled saves. Probes, one game session, throwaway `playline4` moved aside and put
back (403 save files and the profile checked after):

* **The prologue ends where Hans and Henry split**: main quest **M03 `socky` ("Laboratores")** holds the Trosky
  journey video, the gate and tavern dialogues, the tavern (`v_hospode`: the bar fight) and the pillory (`pranyr`), then
  ends; **M05 "Wedding Crashers"** is Henry alone **[obs: Scripts.pak quest files]**. The start saves go after M03,
  before M05, at a calm spot.
* **The playstyle choice** is a `CharacterCreation` step in M01 with three stat presets **[obs]**: fighter (strength,
  agility; sword, heavy weapons, unarmed, large weapons, craftsmanship, riding), diplomat (speech; alchemy,
  scholarship, drinking, craftsmanship, riding), scout (vitality; marksmanship, survival, houndmaster, stealth,
  thievery, craftsmanship, riding, sword). The game's own panel text is `ui_tut_m01_t06_archetype`.
* **No left-hand panel at the main menu**: the HUD's `ShowTutorial` is accepted there and draws nothing; the menu's
  `ShowHelpOverlay` draws only pages compiled into `MenuHelpOverlays.gfx` ("Dummy page for …" otherwise) **[L]**. The
  playstyle is therefore a menu page like Start / Join (the maintainer's call), each entry's summary as its tooltip.
* **One prepared slot is enough**: a file written over the staged save after the game listed it is what the load reads
  (staged key `1a94658764`, overwritten with another save re-keyed `0d1166aae7`; the game loaded and QuickSaved
  `0d1166aae7`) **[L]**. So the chosen playstyle's save is written into the one staged slot just before the load.
* **The pre-rendered prologue videos play full screen in the world** (`wh_ui_PlayMovie <path>`; `wh_ui_StopMovie`
  returns to the world with the HUD) **[L]** — at the menu they draw nothing. The recap therefore plays right after a
  New adventure loads. The prologue's nine videos (siege intro start / end, the ambush intro, the fall dream, three
  dreaming clips in two parts, the Trosky journey) total **980.8 s = 16.3 min** (their Bink headers) **[obs]**.
* **A join is not held back during such a video**: `KCD2MP_JoinBusyReason()` returned nil mid-video **[L]** — the recap
  needs its own busy reason ("your host is watching the prologue").

## What 0.45.5 adds on top (the maintainer's design, built after the probes)

| Piece | What it does | Where |
|---|---|---|
| Playstyle page | New adventure → Choose your playstyle (Soldier / Adviser / Scout, each entry's tooltip = what its preset raises; a playstyle without its start save greyed) | `kdcmp.lua` WO-159 (`KCD2MP_W159_STYLES`, page `style`) |
| Prologue page | Skip the prologue (top, selected; "recommended when a partner is joining") / Watch the prologue's cutscenes (16 min; no conversations or choices; hold E) — replaces the question box | page `prologue`; event `newadv <style> <skip\|watch>` |
| One staged slot, swapped | the first installed playstyle is staged before the game starts; the chosen one is written over it before the load (`--w159 swap`: new seed, scrubbed header, ledger follows) | `Wo159.Swap`, launcher `Home.Wo159` |
| The recap | after the world loads: a HUD line "Hold E to skip them" for 4 s (a video covers all UI), then the nine rendered videos by their Bink lengths; the game's "use" held 1 s skips the rest; a dead timer chain ends it | `KCD2MP_W159RecapStart/Tick/OnAction`, `MenuTakeoverRule.RecapCall` |
| The join hold | a join during the recap is deferred with reason `prologue` (appended to the reason table: old ids unchanged) and the minutes left in the status argument; the joiner reads "Your host is watching the prologue (about N min left)…" | `KCD2MP_JoinBusyReason`, `Wo159Rules.BusyReason/DeferredText` |
| New character from a bundled save | Join with a new character → the same playstyle page → `/join-choice?c=fresh:<style>`; the agent takes that start save's Henry first (same build only), else the old sources; a saves folder is made for a player whose game ran but never saved; the join's slot folder is created if missing | `GameBridge.Wo159`, `Wo125SourceFor`, `ResolveSavesDirForJoin` |
| Three start saves | `assets/start-save/<soldier\|adviser\|scout>/`; validator: where Hans and Henry part (M03 Done, nothing of M05 done — the game starts M05 at once: the maintainer's own save reads M03 Done, M05 Active) and the playstyle (the preset's own skills carry the most experience) | `Wo159.CutPoint/PlaystyleOf`, `Validate-StartSave.ps1 -Style`, `Publish-Release.ps1` |
| Our logo | `KCDLogo.dds` (the menu's logo, 1024×512 DXT5) rebuilt from `docs/branding/KCT_txt.png` into the game logo's own box; `kdcmp_brand.pak` placed in the mod's Data folder by the launcher for its own game, removed at that game's exit (Setup prunes a leftover) | `tools/Build-MenuLogo.py`, `kdcmp_brand/`, `Home.Wo159` |
| Nobody clicks CONNECT | see decision 6 | |

**Live (r5, one game, throwaway `playline4`, two test playstyles: the placeholder as "soldier", the throwaway
`playline3/autosave003` as "scout") [L][harness]:** our logo on the menu in place of the game's; the playstyle page
(Adviser greyed: not installed); the prologue page; Watch → the swap put "scout" in the slot (new key `af58b8bdd2`) →
loaded (55 s) → the recap started 2 s after the world → **all nine videos in order, finished after 984 s** → a join
during it: `prologue`, 972 s left. Found and fixed: the "Hold E to skip" line was drawn under the video (neither
`System.DrawText` nor the HUD's info text shows while a video plays) — it now shows on the HUD for 4 s before the first
video. Saves (403 files), the profile and the installed mod files checked back after.

**Not tested live:** a real E held during the recap (the maintainer declined the in-session press; the synthetic suite
covers the hold, a short press, and other keys); the joiner's new character from a bundled save, and a brand-new
player's first join into a slot folder made during the game (needs two players: the native rescan of a new playline
folder is unproven); the three real start saves (not supplied); the launcher window itself (headless harness).

## The 0.45.5 build

* Built from a fresh clone of `ddbb775` (`release\c0455`, git-ignored) with `tools\Build-Installer.ps1 -SoakWaiver "The
  maintainer decided on 2026-10-07: no soak test for 0.45.5."`: `release\KingdomComeTogether-Setup-0.45.5.exe`,
  102,302,801 bytes, sha256 `2d5bd4c4a4ab1024b465fe248437f7945a6edabf390d32186de89147e323280f`. **Local only**; not tagged;
  **unsigned** (no signing settings here). The waiver is in `release\SOAK-WAIVED-0.45.5.txt`; the build log is
  `release\BUILD-0.45.5.log` (console: `BUILD-0.45.5.console.txt`).
* Every other gate ran inside the build, no FAIL line: relay 62, agent 1,167, setup 77, all 49 synthetic suites (none
  blind), the static checks (7, 6, 7, 17), the native tests, the payload smoke (`RELAY-SMOKE ok protocol=v10
  release=0.45.5`), Steam detection, the four installer cases.
* **Payload**: 1,031 files (0.45.2's 1,030 + `kdcmp_brand.pak`, 512 KB); **no start saves** (none supplied: New adventure
  and the bundled new character say "No start save is installed"). Privacy sweep of every payload file: no account,
  machine or contact name.

## The start saves (0.45.6)

At the maintainer's word: the three start saves are made from his own saves instead of three recorded playthroughs.

* **Where his saves stand** (402 read, read-only): 200 sit between the split and any Wedding Crashers objective; every one
  holds a leftover of a co-op session. The two clean candidates were the game's own milestone saves at the split:
  `permanent016` (Adviser, Jul 27) and `permanent014` (Scout, Sep 15). The Scout one carried the old partner in many places
  (its entity, guard lists, a statistics list) — not used.
* **The presets only add experience** (M01's three modules: `AddXPFromToSkillLevel` / `AddXPFromToStatLevel` from level
  5 to 7 (primary) or 6 (secondary); nothing else) **[obs]**. Through the Modding Tools API (`PlayerSoul/AddXPFromTo…`) on
  a throwaway continuation: every skill +25,600 for 5→6 and +55,040 for 5→7; strength +33,280, agility +29,440, speech
  +33,920, vitality +30,080 for 5→6; a repeated call adds the same again (fixed amounts); weapon skills also give fencing
  experience (the engine's own side effect, kept as the engine wrote it) **[L]**.
* **Built**: A = the Adviser save loaded and quick-saved; B = A + the Soldier preset; E = A + the Scout preset (both applied
  by the game's own function in the game); then offline: the old partner's soul record removed from the soul list
  (`kcd2mp_0`), the Adviser preset taken back from B and E (speech −33,920; riding, alchemy, scholarship −55,040;
  drinking, craftsmanship −25,600). Loaded in the game: the expected levels (Soldier strength 6 / large weapons 7; Adviser
  alchemy, scholarship, riding 7; Scout survival 7 / vitality 6) **[L]**. Their load still raised the original save's 53
  "NPC … does not have a faction" lines: a hidden body of the old partner ("Player0", 1.4 km off) — removed in the game
  (`System.RemoveEntity`) and quick-saved, which left no soul, reference or name of it. The final Soldier reloaded with
  **0** faction errors, no stray body, its levels, in free roam in Troskowitz **[L]**.
* All three pass `Validate-StartSave -Style` (the split, the playstyle, no mod data, a clean header). They are
  **git-ignored** (the maintainer's play data; the repository is public) and copied into the release clone.
* The maintainer's saves were only read; the work ran on copies in empty slots, moved out of the saves folder after.

## The 0.45.6 build

* From a fresh clone of `35a138b` (`release\c0456`) with the three start saves copied into `assets/start-save/<style>/`
  (git-ignored), `tools\Build-Installer.ps1 -SoakWaiver "The maintainer decided on 2026-10-07: no soak test for 0.45.6."`:
  `release\KingdomComeTogether-Setup-0.45.6.exe`, 106,329,689 bytes, sha256
  `31abf815bc07eefd34609dc6b7a037fa61d7ec6af7e8cdef7edf11d7a2dbdc92`. Local only; not tagged; unsigned.
* Inside the build: "Start saves: soldier, adviser, scout" (each passed `Validate-StartSave -Style` again), the logo pak,
  relay 62, agent 1,167, setup 77, all 49 synthetic suites, the static and native checks, the payload smoke, Steam
  detection, the four installer cases; no FAIL line. Payload 1,034 files; privacy sweep clean (no account, machine or
  contact name in any file; the saves' headers scrubbed, no `kcd2mp` in their worlds).

## The 0.45.7 build

* The fix from 0.45.6's first live session: the launcher tried to connect the host as soon as the world had settled on
  its own clock, a second before its CONNECT gate opened; the try was refused, never repeated, and the game's HUD said the
  connect had failed. `HostAutoConnect` now also needs the gate open (`5401f85`); the logo pak removal at exit retries
  for 15 s (an `IOException` while the game still held the file).
* From a fresh clone of `867c1c7` (`release\c0457`) with the same three start saves copied in (byte-identical to
  0.45.6's), `tools\Build-Installer.ps1 -SoakWaiver "The maintainer decided on 2026-10-07: no soak test for 0.45.7."`:
  `release\KingdomComeTogether-Setup-0.45.7.exe`, 106,327,998 bytes, sha256
  `1f2a61e1553d7b4785579d3a34d4e82f2b139ccae368d02c457c052d98c35372`. Local only; not tagged; unsigned.
* Inside the build: the three start saves and the logo pak, relay 62, agent 1,167, setup 77, the synthetic suites, the
  static and native checks, the installer cases; no FAIL line. Payload 1,034 files; privacy sweep clean (the only
  user-folder paths are inside the unchanged third-party audio library's own binaries).
* Not verified live yet: the fixed auto-connect itself (needs the maintainer's next Start Game).

## The recap's sound (after 0.45.7)

* **Live (the maintainer, 0.45.7, a Soldier New adventure with Watch):** the videos' sound was barely audible and the
  world's sound played on underneath **[L]**.
* The cause, from the game's own data and binaries: the videos carry their sound (audio track 1 for every language,
  `Libs/Tables/ui/video_language2audio_track.xml`), but `wh_ui_PlayMovie` is the bare movie player. The game plays them
  through its cutscene player (`C_CutscenePlayer`, `wh_ui_PlayCutscene <name>` from `Libs/Tables/ui/cutscene.xml`), which
  sets the audio up (`audio_setup_video`, `UseAudioSnapshot`) and shows the narrator captions (`CustomText` events).
* The fix: the recap plays each of the nine by its table row (`intro_new_game`,
  `zoufalaObranaZaBohutu_battleOpeningCutsceneGameEnd`, `story_switch_to_trosecko`, `zachrana_fall_dream`,
  `zachrana_probuzeni_data1`, `zachrana_prespani_data1..3`, `m03_trosky_journey`); `wh_ui_StopCutscene` before the next
  and on skip. The rows' events are skip points and captions only; the siege's end also toggles a music state
  (`STORY_M50_BATTLE_5_GODWIN_BATTLE`, 10 s) -- watch that the world's music is right afterwards.
* Synthetic 85/85 (never the bare movie player), client 1,167. **Not verified live yet.**

## The recap's skip (after 0.45.7)

* **Live (the maintainer, same session):** the game's own skip on a video ("Skip All") ended that video and returned to
  the world; the recap then started the next one when the skipped one's length had run out **[L]**. The bundle shows
  why: no `WO159-RECAP ended` line (the held E never reached `handleAction` -- a video takes the keys); no line at all for
  the bare movie player's skip; the WO-138 gate logged `calls=0` (`wh_ui_PlayMovie` never pauses the game, so the world
  and its sound ran on -- the other half of the sound report).
* Through the cutscene player a rendered video pauses the game and freezes every Lua timer (WO-80/WO-149), and the agent
  already hands its edges to Lua by name (`KCD2MP_SetCutscene`, from `CutscenePlayer::` lines). The recap now follows
  them: the playing video's end starts the next one at once; an end more than 5 s before its length is the player's skip
  and ends the whole recap ("skipping one skips them all", said on the HUD before the first). Without edges the old
  clock rule plays on; a start without an end moves on after the length + 10 s. A stale chain counts as dead only once
  no recap video is on and the playing one's length has passed (a frozen chain during a video is not a dead recap).
  The join reason `prologue` now comes before `cutscene` (the joiner is told the minutes).
* The video pause still applies with a joiner waiting: the levers need a partner's positions within 10 s, and a joiner
  held at the menu sends none.
* Synthetic 99/99 (14 new), all 49 suites; client 1,167. **Not verified live yet.**

## The 0.45.8 build

* Both recap fixes (the cutscene player: sound and the world paused; skipping one skips them all). From a fresh clone of
  `0f716fa` (`release\c0458`) with the same three start saves,
  `tools\Build-Installer.ps1 -SoakWaiver "The maintainer decided on 2026-10-07: no soak test for 0.45.8."`:
  `release\KingdomComeTogether-Setup-0.45.8.exe`, 106,326,145 bytes, sha256
  `7c0280574a07cde63e31526c2ea144c07159783faed35715495612579117091e`. Local only; not tagged; unsigned.
* Inside the build: the start saves and the logo pak, relay 62, agent 1,167, setup 77, all synthetic suites, the static
  and native checks, the installer cases; no FAIL line. Payload 1,034 files; privacy sweep clean.

## 0.45.8 live: Skip All froze the game (the cutscene player is out again)

* **Live (the maintainer, 0.45.8, a Scout New adventure with Watch):** skipping inside the long intro worked; Skip All
  left the game frozen (the ESC menu still opened; the game was quit from it) **[L]**.
* The bundle: `wh_ui_PlayCutscene intro_new_game` at t=119; the agent saw `world FROZEN (held 0x10)` -- the game's
  video pause (source 4), as intended -- and still `pause_state=frozen` a minute later; **no** `CutscenePlayer::` line
  in kcd.log for a console-played cutscene (so no edges reached the recap), and no recap line after video 1 (its timers
  frozen by that pause). A console-played rendered cutscene has no holder to finish it: after Skip All the video is
  gone and its pause is never released (inferred from the log; the in-video skip points work).
* **The fix:** back to the bare movie player (no game pause, never froze), and the recap fires the game's own video
  sound setup around it: `audio_setup_video` (`Libs/GameAudio/default_controls.xml`: `silence:on`, cleared on stop,
  and the FMOD snapshot `setup_video`) through `player:ExecuteAudioTrigger(Sound.GetAudioTriggerID(..),
  player:GetDefaultAuxAudioProxyID())`, the way the game's own entity scripts fire triggers; `StopAudioTrigger` at the
  end, on a skip and on a dead chain.
* **The skip:** the movie player's skip writes nothing, but a video takes the keys: in the 0.45.7 bundle the player's
  position and facing did not change inside any video and changed right after each of the three skips (about 147 s
  into video 1, 2 s into video 2, 44 s into video 3). So a move over 0.3 m or a turn over 0.05 rad, from 1.5 s into a
  video, ends the whole recap. The edge-driven version (above) is gone; the `prologue`-before-`cutscene` join reason
  stays.
* Synthetic 91/91, all 49 suites; client 1,167. **Not verified live yet** (the sound setup above all: heard, and
  released at the end).

## Pocketed (outside this WO)

1. **A prologue recap** would have to play somewhere the engine draws video (in the world, e.g. right after the load);
   at the menu `wh_ui_PlayMovie` draws nothing and stops the menu video.
2. **Read the game's own EULA version live**: `wh_ui_eulaCurrentVersion` / `wh_ui_eulaConfirmedVersion` are cvars
   (GUIModule) the menu's Lua could compare before arming; the launcher's known version (2) will be wrong after a game
   update that raises it.
3. **Save headers carry the account and build-machine names** (`DebugInfoHistory`): any save players pass around (the
   0.45.x "load the provided save" practice, bug reports with saves) carries them. The joiner's world transfer sends the
   host's header too (not checked here).
4. **A mod figure saved into a pristine Henry save** (`kcd2mp_6` in the throwaway `playline3/permanent002`): WO-84's
   savegame-restored ghost reached the game's first post-prologue save on this machine.
5. **After an aborted join** the joiner's menu keeps "Joining your host…" until the launcher's launch ends (the
   launcher's own buttons and the agent's messages still work).
6. **The menu listener stays registered for the game run** (inert while disarmed). The reported community crash on UI
   transitions was not seen in four runs.
7. **Steam Cloud** was running while test saves came and went in `playline4`; the cloud may still hold those files and
   bring them back later (WO-157 saw old autosaves restored).
8. The installed 0.45.2 RC pak is ~9.7 KB larger than a working-tree build of the same Lua (release stamping assumed;
   not looked into).
