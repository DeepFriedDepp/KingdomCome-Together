# WO-157 — the first public-beta patch: findings

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

Marks: **[log]** read in the testers' zips (0.45.1; never committed); **[L]** live, solo on this machine, as the host
(with a scripted partner where one is needed) or **[J]** as the joiner of a synthetic host, on a throwaway save
(`playline4/autosave111`, saves locked; frames in the session's scratchpad); **[unit]** / **[syn]** / **[static]** /
**[native]** a test that gates the build; **[code]** read in the code; **[not live]** no run in the game. No names,
paths or addresses of the field's players are in this file.

## The answer

Every item has a cause from the logs; the fixes are small and switchable, and none changes what a player without the
problem sees. What could not be proven or fixed as a patch is recorded with its evidence (§9, proposed WOs).

| # | The report | The cause (evidence) | What this patch does | Proof |
|---|---|---|---|---|
| 1.1 | Shops counted as trespass for the joiner; the host judged and (joint mode) paid | The joiner's own game raised every trespass and the host judged it **without asking its own world** (`KCD2MP_W139HostJudge` counted witnesses only) **[log][code]**. In the host's world the shop was open (the host stood in the blacksmith's shop dialogue at that moment) **[log]**. On the joiner the keepers are paused copies whose NPC state never reaches its shop-open state: 132 / 188 / 70 `NPCContext ... Couldn't find actions to get NPC into game loaded state` per joiner log, 0 on the hosts **[log]** | The host decides from **its own world's area labels** (`XGenAIModule.IsPointInAreaWithLabel`): a trespass only where `private` or `personal` and not opened by `antitrespass` / `publicServiceTrespassOverride`; unknown is no crime. The joiner's own trespass warning is hidden in a session (the DLL's HUD gate; the detector still reports) | **[L]** Tachov's blacksmith at 16:23: counter = `open`, the house beside it = `private` (the game's own trespass cue 1 there, 0 at the counter), the street = `public`; at 23:00 the counter = `private` and the blacksmith barked a trespass at Henry. Through the real judge: `id=901 here=open -> not a trespass`, `902 here=private -> judged`, `903 here=public -> not` **[L]**. **[J]** inside the private house: `WO139-TRESPASS level 255 -> 3`, reported to the host (`CRIMEASK`), the HUD raise hidden (`quiet=1 quieted=1`), no warning on screen. **[syn]** 9 new checks (open / override / public / unknown / private / the host's own detection / the switch) |
| 1.2 | The false crime followed him into the next session; "fled" began a spiral | The record parked for him held his machine's trespass reports; restored on return (`the crime record kept for that player is his again (1 crime(s))`), a guard stopped him 42.2 s after crime sharing came on, he walked off: `WO154-STOP fled moved=36.2`, three guards attacked **[log]** | On return every **trespass his own machine reported** leaves the record; other crimes stay. The **first walk-away from a stop** in a game is told, not fled (the record stands); the next one is fled | **[unit]** record: 1 of 4 entries dropped (his trespass), assault, theft and the host's own trespass kept. **[syn]** the first 12 m walk-away = `talked` + told, the second = `fled` |
| 2.1 | "A MOD FILE WAS REMOVED: KCDMP_LauncherInjector.exe", constantly | An antivirus took the separate injector exe (Setup's verify read it at -1 bytes; launchers logged `quarantined ... code=2` with it present again minutes later) **[log]** | **No injector exe.** The launcher loads KCDMP.dll from its own process (`GameInjector`): the game it started (image path, or the same file through a link), the DLL by sha256 against the install manifest, x64, fail closed with a plain message. Not in the payload, the installer or the verify list; an upgrade removes the old one (closed set) | **[L]** host and **[J]** joiner: `KCDMP.dll attached` then `tick is live` 1 s later, through the same code. **[unit]** a real process: loaded; another program refused; a different DLL refused; a missing DLL and a gone process refused. **[static]** nothing ships or starts the exe |
| 2.2 | "kdcmp.pak is gone" and refused to launch, with the file present and right | **The check looked in the wrong folder**: `GameRootOf` (the `steam_appid.txt` walk) falls back to the exe's `Bin\...` folder on a Modding Tools install that has never been started (the game writes that file itself), while Setup and the checklist use the folder above `Bin`. The fresh-install host: "Installed and verified" 59 s before `blocked kind=quarantined file=kdcmp.pak` **[log]**. A second trap: a launcher started with another working directory (one ran from `C:\WINDOWS\system32`) reads and writes no `settings.json` of its own **[log]** | The Mods folder from the install root (as Setup). The message names what was found: **missing**, **empty**, **present but unreadable** (locked or blocked), **present but different** (sha256, a warning only), **which path** (the full path checked: the Mods folder, the staged copy, the install folder), how to tell Windows Security from Smart App Control (no exclusions) and another antivirus. **LAUNCH ANYWAY** for the mod's own files; a game whose mod really is missing says so on its HUD. The installed launcher works in its own folder. Setup's "-1 bytes" names the antivirus | **[code]** the finding table; **[build]** launcher compiles; **[static]**. Setup's message: installer cases at build. **[not live]** the dialog itself (the launcher's window was not driven) |
| 2.3 | `trace_server.run.exe — MSVCP120.dll was not found` | The Modding Tools' trace server needs the Visual C++ 2013 runtime **[code: its imports]** | An optional checklist step: detect `System32\msvcp120.dll`; install from the Steam library's `Steamworks Shared\_CommonRedist\vcredist\2013\vcredist_x64.exe` (silent, Windows asks once) or open Microsoft's page. Never holds Host/Join; shown once at start when it asks | **[unit]** present / missing with the redistributable / missing without / the system folder / exit codes. **[not live]** the real install (this machine has the runtime) |
| 2.4 | Groups hosting the same start save collide; "copied world", "regular-game saves" | The world's identity is the playthrough seed; everyone starting from one save shares it: a player who hosted it has "only copies of the host's world", and the joiner's per-world Henry files cross between hosts (both machines of one pair reported world `45e96cf2ec`) **[log][code]** | A host sends its **install key** beside its world (JoinStatus state 10, same 9-byte packet): a joiner keeps a **second host's** world of the same seed in its own folder (the first host keeps the plain one: nothing moves for anyone else). A save of a world **this install hosted** is his own playthrough. "Join with a new character" also from a **pristine** Henry save with the host's seed (a Henry who has done nothing yet is nobody's). Plain words for each case | **[unit]** the four cases on fixtures: a fresh joiner (no saves: told; the start save: a new character), one who hosted the same start save (Bring and a new character), one with only regular-game saves (told; nothing a Modding Tools world can load), one with his own Modding Tools save (Bring, his own first save first); two hosts = two worlds; the wire |
| 2.5 | "Steam code does not decode" | Not in any zip (the attempts' source was not given) **[log]**; a pasted code with text, quotes, chat dashes or invisible spaces did not decode **[code]** | Forgiving parse (the check bits still decide); the message says what a code looks like and where the host finds it | **[unit]** 9 pastes decode, 4 look-alikes do not, an unknown app letter is refused |
| 2.6 | Code signing, prepared | — | `tools\CodeSigning.ps1` + `Build-Installer.ps1`: Azure Artifact Signing from the environment or the git-ignored `tools\signing.local.json`; every unsigned shipped exe/DLL, then Setup and its uninstaller, timestamped and verified; absent = unsigned, said in the build log; half configured = the build stops. `docs/CODE-SIGNING.md` | **[static]** none / half / full / the Inno command; the settings file ignored and untracked |
| 3.1 | The host's figure in a riding pose with no horse | The "(none)" lines are each agent's **own** first mount send; the host never rode in any logged window (all 129,642 host data lines flags=0) **[log]**. Found instead: the host's game died in `MountNPCOnHorse` (its last line) while the joiner's figure was ~2 km away (`ForceMount ok=` never printed) — WO-58's freeze, whose distance guard did not cover the stand-in horse **[log]** | No `ForceMount` more than 150 m from this player (tried again when near). The screenshot itself is not explained by the logs (the wire's riding bit is the DLL's own stance read) | **[syn]** 1 km: not mounted, logged; near: mounted |
| 3.2 | "Everything lags" | Median ping 63 ms, p90 300 ms, max 1,900 ms; the agent up to 2,486 ms behind; two queue holds (a load, the exit) **[log]** | The mod menu's Connection line adds "-- the connection is struggling (ping N ms)" while half of 10 s is bad (>= 250 ms, or >= 1 s behind); bad for 30 s: one line on screen, at most once in 5 minutes | **[L]** fed 420 ms: `struggling` after 5 samples, `told` at 30 s, `ok` when good again. **[syn]** 7 checks |
| 3.3 | NPCs desynced, vanish when bumped, T-posers | **Random events roll separately on each machine** (an armed caravan on the host, a civilian one on the joiner: 34 copies whose body vanished, 23 stand-ins spawned, 22 removed, one 400 m off); streams going silent hide or remove copies; the "T-posers" were stand-ins ragdolled by local blows and stood up again (17 `not-living`, 16 `WO131-STANDUP`) **[log]** | Recorded (§9: random events need their own WO) | — |
| 3.4 | The snap counter | It was a running total per figure, never reset (no cap at 120: an older zip reached 1,206) **[code][log]** | `snaps=` per 10 s window, `snaps_figure=`, `snaps_session=` | **[syn]** a window with one snap, the next window 0, the figure's total kept |
| 3b.1 | The joiner was "invulnerable" | 14 of 16 blows on his figure measured hp −0.0 st −30..−38 and were **dropped** (`SendPlayerHitAsync` returned for 0 health); the DLL's hit watch refilled the figure's stamina after every blow, so it never tired and every blow soaked into stamina **[log][code]** | Stamina-only blows go to the joiner; the figure keeps the stamina an NPC's blow cost (health still put back), so later blows cost health as they would him. "The victim decides whether he blocked" is recorded for the shared-combat WO | **[build][code]**; **[not live]** (an NPC that fights an avatar on demand is not available solo: WO-155's note) |
| 3b.2 | His blows never reached the host; bandits T-posed | **Refuted**: all 7 of his blows reached the host and were applied, the last one killed (`hits_fwd` counts only on a host). The "T-pose": the bandit copy sat in two eating/cooking one-shots for 600 s with 26 "queue too many actions" errors and 23 of 28 replayed swings refused **[log]** | A copy entering a fight on the joiner stops its one-shot (the game's own request, no fragment) | **[code]**; **[not live]** |
| 3b.3 | 8,076 `Animation-queue overflow` on the host's figure | All its look-pose layers; the figure's own look IK **[log]** | A figure's look IK and AI look target off at spawn (`mp_avatar_look` gives them back) | **[L]** `WO157-LOOK id=1 ... ok=true/true`, the figure idles normally (frame), 0 overflow lines. **[syn]** |
| 3b.4 | After the smithing quests no conversation; Trade closed at once | The engine cancelled his requests ("Request timed out": the copy never accepted the pause) while the copies sat in their save's loaded state they could not reach — the blacksmith's "LeftHand Held object: semifinished_sword ...; Unstance: blacksmith_forging" (40 errors), the innkeeper re-given the host's activity by the placement. Nothing stayed on the **joiner's own player** (his minigame row back to none, two talks after smithing worked) **[log]** | Talking to a copy resets its hands, stance and activity (the engine's `wh_ai_NPCStateResetElement`) and holds the placement until the talk ends (`mp_talk_free`) | **[syn]**; **[not live]** (the smithing quest step cannot be reached solo) |
| 3b.5 | Sleeping together broken both ways | (a) **the faster machine cut the slower's sleep**: the host's skip ended in 9.1 s, its "woke" stopped the joiner's at ~10.5 of 12 h, and his clock was then set forward while awake **[log]**. (b) **no rest**: live, the game's real no-bed sleep (the accepter's) rested in 1 of 6 fresh game processes and in 5 kept the awake rates (exhaust and hunger falling, health unchanged) — the field's host and the joiner's 12 h **[L][J]**; the trigger was not found | (a) a partner's "woke" never stops a sleep here; the clock meets the host's after it. (b) a real sleep the game gave nothing gets the rest the game's own sleep gives there (exhaust +12.45/h, health +7/h, measured; capped at 100; `mp_sleep_rest`); rest is logged (`WO157-REST`) | **[J]** the host's "woke" 3 s into 12 h: the joiner slept the full 12.0 h (world time +43,190 s). **[L]** two 2 h sleeps the game gave −4.2: topped up 34.7 → 63.8 and 59.4 → 88.6. **[syn]** 7 checks |
| 3b.6 | Shops as trespass, again | Same as 1.1 **[log]** | 1.1 | — |
| 3b.7 | The fast-travel notice read as an error | "Fast travel is off in this co-op session (your host can turn it on in the mod menu)" **[log]** | "Fast travel is turned off for co-op. The host can turn it on in the mod menu (Insert)." (the player's own key) | **[syn]** |
| 3b.8 | "Does my inventory save?" | It does (each host save pairs a snapshot of his character, WO-125); nothing said so **[code]** | "Your host's game saved, and your character with it" — at most once in 10 minutes | **[code]**; **[not live]** |
| 3b.9 | What must players install? | Setup installs the launcher with its own .NET runtime (no separate .NET install); WebView2 when missing; Steam installs the game and the Modding Tools | Said in QUICKSTART, the release notes and the checklist (the VC++ 2013 step) | — |
| 3b.10 | The pak quarantined again with exclusions and protections off | No log covers that moment **[log]**. An exclusion does not help when the blocker is Smart App Control (it has none) | The message says how to tell which one it is (2.2); the in-launcher load (2.1) and signing (2.6) are the real fixes | — |

**The 2026-10-06 session's wait vote** (host 16:21:41 → 16:22:11 timeout): the joiner's game showed the prompt
0.03 s after the host sent it and cleared it after 30 s; no key was pressed **[log]**. No change.

**Kept, as asked:** the joiner saw the carts and the horses. Nothing in this patch touches them.

## Decisions made unattended

1. **VERSION is blank in the work order.** It is the maintainer's string (`docs/VERSIONING.md`): no VERSION
   commit and no installer until the maintainer gives it. Everything else (code, gates, docs, the build path) is ready.
2. **The host's trespass test** is the engine's own labels at the reported spot **and** at the joiner's figure: private or
   personal, not opened by antitrespass / publicServiceTrespassOverride. Unknown (no answer) = no crime. The host's own
   detections (id 0) are judged as before. `mp_trespass_host off` = 0.45.1.
3. **The joiner's own warning is hidden, not his detector**: the DLL swallows only a *raise* of the HUD's trespass level in
   a session (a drop back to public always passes, so the HUD can never stick); the level still goes to the host.
   `mp_trespass_hud on` shows it again. The joiner's copies never arrested him locally (only the host's stops do).
4. **Mirroring the host's area labels onto the joiner's copies: not added.** The labels belong to the keepers' NPC
   state, and the joiner's copies are paused by design; keeping the shop elements in WO-141's placement is a native change
   to a proven path — proposed (§9).
5. **On return, every trespass his own machine reported is dropped**, including ones the host had judged with witnesses
   (they were judged without the host's world's check). Assault, theft, murder and the host's own detections stay.
6. **The stop grace is once per game run**, for any crime: the first walk-away is told and counts as "talked" (the record
   stands, the guard's 60 s cooldown applies, the next stop comes); the second is fled. `mp_stop_grace off` = 0.45.1.
7. **No injector exe; the source stays as a developer tool** (probes, harnesses; never shipped). The launcher compares the
   DLL with the install manifest's sha256 when the manifest lists it; a development build (no manifest) is not compared.
8. **The Mods folder from the install root**; **a "different" file is a warning, never a block** (a hand-built pak, or
   another install's manifest, must not stop a game); **LAUNCH ANYWAY only for the mod's own files** (a missing DLL or
   agent cannot work, so those still block). The installed launcher sets its working folder to its own (only when Setup's
   manifest is beside it: development runs unchanged).
9. **The VC++ 2013 step is optional**: it never holds Host/Join, and the checklist opens once at start only when it asks.
   The install is Microsoft's own redistributable from the player's Steam library, started only by his click.
10. **World identity without rewriting saves**: a running game's seed cannot be changed, so the host is told apart by an
    install key (random, made once, in the Henry store). Only a joiner that already keeps another host's world of that
    seed uses a qualified folder; every existing folder keeps working (claimed by the first host that comes).
11. **Signing**: only files without a valid signature are signed (Microsoft's runtime keeps its own); Setup and its
    uninstaller by Inno's `SignTool`; every shipped exe/DLL and Setup must carry a valid, timestamped signature after.
12. **The riding change was taken out again**: making the game's "mounted" count as riding broke WO-118's suite and had no
    proven benefit (the wire's riding bit is the DLL's stance read). The far-ForceMount guard stays.
13. **The connection thresholds**: 250 ms ping or 1 s behind; half of the last 10 s; the on-screen line after 30 s with two
    thirds bad, once in 5 minutes.
14. **The avatar's defence**: shipped "a blow lands as the blow" (stamina-only blows forwarded, the figure's stamina not
    refilled); "the victim's own game decides whether he blocked" is more than a patch (§9).
15. **The rest top-up uses the game's own measured rates** (no-bed sleep, this machine), and only when the game gave less
    than a quarter of them; a wait, the forced sleep screen and a nap (< 30 min) are never touched.
16. **3b.2's one-shot stop** and **3b.4's talk reset** ship on the logs' evidence without a live run (neither situation can
    be made solo); both are logged and switchable (`mp_oneshots` / `mp_talk_free`).
17. **The Smart App Control message is unchanged**: it says what SAC is and that turning it off is the player's decision;
    the launcher offers no button and changes nothing.

## The live runs (solo)

Throwaway save `playline4/autosave111`, `Game.AddSaveLock` on every run; the maintainer's installed mod files backed up
and restored byte for byte (sha1 `b31dc179…`); 397 save files backed up and checked after every run. The game took the
foreground at each launch and was left there (never pushed or activated again).

* **h1** (host, scripted partner): the in-launcher load; the host's trespass judge at the shop, the house, the street; the
  avatar's look IK; the connection indicator; the first no-rest sleeps.
* **j1** (joiner of a synthetic host): the in-launcher load; crime sharing on; the trespass in a private house (hidden,
  reported); the 12 h sleep vote with the host's "woke" 3 s in (slept 12.0 h).
* **s1–s4, j2**: the rest bisection (fresh processes with and without a session, a partner, clock and stat writes, a
  joined world, the solo save loaded in the same process).
* **h2** (host, scripted partner): the rest top-up.
* Two console moves left Henry under the terrain while the ground streamed in (the maintainer watched it); he was put
  back, and every later move held him on the ground until steady.
* **Steam Cloud** put back six old July autosaves into `playline1` when Steam started (added only, identical to
  `playline1 - Copy`, nothing overwritten). Left for the maintainer.

## The gates

See `docs/WO-157-progress.md` (the build section) for the counts of this commit: agent 1,119, setup 72+, every synthetic
suite (WO-157: 41), the static checks (WO-157: 17), native 406, relay, payload smoke, installer cases.

## §9 What a follow-up needs (proposed WOs)

1. **Random events per machine (3.3)**: the host's random events (caravans, duels) are not the joiner's; the joiner's copies
   of them vanish or are replaced by stand-ins. A shared-random-events WO: the host's event decides, the joiner's is
   suppressed.
2. **The shops on the joiner (1.1, the root)**: keep the keeper's shop elements (OpenShop, the area label change) in
   WO-141's placement, so the joiner's own game agrees with the host's; then the joiner's warning could come back.
3. **"The victim decides" (3b.1)**: the joiner's own block/parry against the host's NPC blow (the attack's direction and
   timing across, the defence judged on his machine).
4. **The no-rest sleep (3b.5)**: why the game's real sleep started through `C_SkipTime` slot 5 rests in some processes and
   not others (1 of 6); the top-up is a measured stand-in.
5. **A bed for the accepter**: the asker's bed quality for everyone (WO-140 noted the no-bed comfort).
6. **The horse screenshot (3.1)**: a riding pose with no horse was not in any log; a session with `[pos] riding=` and the
   host's mount lines kept would show it.
7. **The joiner's talk after smithing (3b.4)** and **the avatar's blows (3b.1)** in the next two-player session
   (`docs/TWO-PLAYER-CHECKLIST.md` §WO-157).
