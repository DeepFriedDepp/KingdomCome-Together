# WO-140 — Sleep voting, the "own world" trap, the combined checklist: findings

Kingdom Come: Together. Unofficial; not affiliated with or endorsed by Warhorse
Studios.

Evidence marks: **(observed)** seen live in the game, **(synthetic)** the real
game against a synthetic peer or a console stand-in for a player's action,
**(code-verified)** read in the code or the game's binaries but not run,
**(inconclusive)** tried, no clear answer.

All live runs were solo on one machine (the Modding Tools game, 1.5.5), on a
throwaway copy of a save: the real game as the **host** with a scripted
partner (avatarpeer, run h1), the real game as a **joiner** of a synthetic host
(synthpeer, runs j1 and j3), and the real game **in its own world** connected
to a synthetic shared-world host (run j2). No input was sent: a player's
"Sleep" at a bed is the bed's own Lua call typed in the console, and choosing
the length is the time picker's own UI functions (`TurnRight`, `Confirm`)
called by script. The "other player" in every run is a peer program, so "both
screens" are the real game's screen in each role, one role per run.

## The answer: the vote

| the requirement | how it works | evidence |
|---|---|---|
| the Sleep choice is held **before the lie-down and the time picker** | KCD2's beds are `BedTrigger` entities; the interaction calls `TriggerBase.OnUsed/OnUsedHold → OnAction → self:ReportUse`, and a lying action there is the lie-down that ends in the picker. The mod wraps `BedTrigger.ReportUse` once (the instances resolve it through the class table): while a vote is needed, a lying action the game allows (its own `CanSleep` first) is held — nothing on screen but "Waiting for other players...", he does not lie down | **(observed)** h1: `WO140-HOLD bed=…` then the ask; frame 1 |
| the other player gets a plain prompt, yes / no | "Moose wants to sleep. Sleep too?" (the game's own centered text and a row with the seconds left); F11 yes, F12 no, or `mp_sleep_yes` / `mp_sleep_no` | **(observed)** h1 (host asked) and j3 (joiner asked), frame 2 |
| yes: the asker's sleep carries on as normal | the held call runs unchanged: the lie-down, the game's own picker; he chooses the length | **(observed)** h1: `WO140-GO`, picker open (frame 3) |
| when he confirms, **both sleep screens start together** | the DLL reads the game's skip state every frame; the asker's "began" edge carries the chosen hours (C_SkipTime +0x74) → Begin to everyone who said yes → each runs **the game's own sleep** for those hours, standing wherever he is (C_SkipTime's `StartSkipTime` with the Sleep id — what `wh_pl_ForcedSkipTime` calls — the same screen, the clock wheel, "Sleeping") | **(observed)** h1: Begin at the confirm (0.1 s); the host as the one who accepted: sleep screen, no bed (frame 6); j3: the joiner likewise (frame 5) |
| the same screen as the sleeper, not an imitation | it **is** the game's sleep: C_SkipTime id 2, "Sleeping", effects, the clock turning. Refused by the game (trespass, a fight): its forced sleep screen (id 14 "Fake Sleep") still runs, so the screen and the clock still match | **(observed)** id 2 outdoors: the screen and the rest; id 14: the screen with no rest (p2) |
| both get their rest | the accepter's sleep is a real sleep: tiredness and health restore as the game computes them for that spot (no bed = its lowest comfort) | **(observed)** host accepter 2 h: exhaust 75.4 → 90.1; joiner accepter 2 h: 66.9 → 81.7, health 92.9 → 100; asker 3 h: 56.8 → 79.1 (host), 44.9 → 67.3 and health 70 → 93 (joiner) |
| one shared time skip in the host's world, both clocks move together | the host's own skip is the world's (its time-skip report, kind sleep); a joiner's own skip moves only his copy, and his clock is then pinned to the host's: the host's result is held while his own skip runs, applied at its end, and pulled back if he is ahead | **(observed)** j3: joiner's result held during his skip, clocks equal within 24 game-seconds; joiner ahead after his 3 h → pulled back 525 s to the host's |
| wake together | whoever wakes first among the host and the asker wakes the others (the game's own `Stop` on their skip) | **(observed)** `woke` sent at the end; **(inconclusive)** the effect on a partner still asleep: every partner here was a program |
| no / no answer in 30 s | nothing happens: he never lay down, no skip, no rest; "Other players are not ready to sleep yet!" | **(observed)** h1 and j1: the clock moved only its natural seconds (+128, +137 s), exhaust unchanged; timeout after 31 s (frame 8) |
| the host's save on sleep | the host's own bed saves by itself; a joiner's bed that saves → the host makes the game's own rest save after the shared sleep | **(observed)** h1: the partner's `save 1` → `WO140-RESTSAVE Game.SaveGameViaResting called`, `autosave093` written (throwaway) |
| the joiner's clock never drifts ahead again | fall-safe: every clock reading on a joiner in the host's world is compared with the host's (its last report, extrapolated); ahead by more than 5 game-minutes → pulled back at once (native, his copy only). Never on a stale report or during the host's skip; during a shared sleep only after the host's result | **(observed)** j1: +3 h from the console → pulled back in 7 s; no false "reload" (the jump was swallowed) |
| wait / rest | "Wait" is the `open_skiptime` key, handled natively (no game Lua): the same `C_SkipTime` picker (id 1). The DLL gates `C_SkipTime::ShowDialog` for the player's own skips (wait 1, sleep 2, read 4): a picker that opens without a vote does not open, its arguments are kept, the vote runs, and on yes the same picker opens again | **(observed)** h1, the gate alone (the bed's Lua hold bypassed as a stand-in for the key): held → vote → yes → the kept picker opened again → Begin; no → dropped. The accepter's wait is the game's own ("Waiting", 1 h: +3916 s, no bed) **(observed)** p3, frame 10. **(code-verified)** the Wait key itself (no input) |

**Two ways the game itself ends a sleep early** (not WO-140, both observed): a
sleep in someone else's barn was woken after 3 s by a "Hired hand" walking up;
and a player already about 90 % rested gets a short sleep, or none (the game's
oversleep rule). Both apply to the one who accepted too.

## The answer: the "own world" trap (Phase 0b)

| the fix | evidence |
|---|---|
| the launcher's Ready line for a joiner: "Stay at the main menu. Don't load a save. Click CONNECT -- you'll join your host's world automatically." (the host keeps "load your save") | **(code-verified)** `Home.razor.cs`; marker in `Verify-Install.ps1` |
| a joiner in a world that is not the host's (host's mode shared, this game in a world, no join/rejoin/leave running) = **separate**: the agent says `MP-JOIN joiner: connected from its own world -- NOT joined (separate)`, and every minute again with what it dropped | **(observed)** j2 |
| in the game: the plain message, as the game's own centered text every 5 s (always on screen) and a row, plus "You and your host are in separate worlds right now." | **(observed)** j2, frame 9 |
| the launcher: the join status `own-world` → a window, once, with the same text | **(observed)** the agent's `/join-status` = `own-world` + the message; **(code-verified)** the window (`AgentStatusBanner.OwnWorld`, unit-tested) |
| nothing of the host's world is applied: its NPC stream, NPC damage and deaths, NPC actions, item drops and claims, loot, quests, crime and leash messages are dropped at the socket and in the processor | **(observed)** j2: 218 NPC rows, a leash warning + pull, a crime stop, an item drop, 6 loot messages — all dropped; **0 NPC puppets, 0 stand-ins, 0 paused NPCs, copy guard off (`guard_on=0`)**. The one native bind was the host's own figure (`kcd2mp_0`): the ghosts still show each other |
| none of the joiner's own world goes to the host (NPC hits, loot asks) | **(code-verified)** `Wo140HoldOutbound` at the WO-131 hit gate and the WO-134 sender |
| the host does not leash a separate joiner (the field's "every pull held, joiner-loading") | the joiner's leash state carries a new flag, separate; the host logs `joiner N is in its own world -- not leashed, no pull held` and skips its leash **(observed)** h1 (avatarpeer's flag on and off) |

## 1. The levers (Phase 1)

| what | where | how it was found |
|---|---|---|
| a bed's Sleep | `BedTrigger.ReportUse(user, items, action)` with `action.sAction == "lying"` → `PlayerStateHandler.ChangeStance(bed, "lying")` (Scripts.pak `ActionTrigger.lua`, `BedTrigger.lua`). 462 BedTriggers in the level, no `Bed` entities. Click = sit, hold = lie on most beds | Scripts.pak; live (the wrap, the lie-down, the picker) |
| the picker and the skip | PlayerModule `wh::playermodule::C_SkipTime` (a function-local static; export `?I@C_SkipTime@playermodule@wh@@SAAEAV123@XZ`). Its vftable: slot 5 `StartSkipTime(id, hours-as-bits in r8, spot16)` = ShowDialog + the hours + confirm (what `wh_pl_ForcedSkipTime` calls with id 0); slot 6 `Stop(id, bool)`; slot 14 `ShowDialog(id, float, float, const S16* spot, float)` (+0x4C5BF0 on this build). State at +0x68 (0 idle, 1 picker, 2/3 skipping; a confirmed sleep goes 0-1-2-3-1-0), id +0x6C, hours +0x74 | disassembly; live states |
| the picker's own controls | UI element `SkipTime` (IPL_GameData `Libs/UI/UIElements/SkipTime.xml`): `TurnRight` (+1 h), `TurnLeft`, `Confirm` (Start), `Cancel` (Back) through `UIAction.CallFunction` | live |
| skip ids | Tables `rpg/skiptime.xml`: 1 Wait (interactive), 2 Sleep (interactive, comfort), 4 Read, 14 "Fake Sleep, Hidden Stats" (forced; its stats are frozen: no rest, no hunger) | Tables.pak; live |
| the rest | only the real Sleep id gives it; the "sleep" buff (`cbbedb16…`) added during a forced skip changes nothing | live (p2) |
| the calendar | `*(wh::GetGameIface() + 0x1B0)` (`C_GameInterface::SetCalendar`); world time in ms at +0x68; `C_Calendar::SetWorldTime` (RPGModule +0x189ED0) refuses anything lower ("World time must not be set backwards") | disassembly; live pull |
| why a joiner's own sleep drifted | the joiner's skip moves its own calendar; WO-133 made the host ignore it, so it stayed ahead until the next join | WO-133 findings; confirmed by j1 |

Anchors are found by what they are (fail closed, `WO140-BUILD` logs what
armed): ShowDialog = the one function loading its own `__FUNCTION__` string,
with its exact 16-byte prologue, and the instance's vftable slot 14 must be it;
SetWorldTime likewise with its 21-byte prologue.

## 2. What was built

* **Wire** (`ProtocolWo140.cs`): SleepVote 0x68/0x69 on the WO-123 join
  channel, `JoinFrom.Either` (a joiner's to the host, the host's to one
  joiner): Ask, Answer, Begin, Cancel (`woke` after a Begin). The host
  coordinates a vote (its own player and every other joiner). Next free type:
  **0x6A**. LeashState flag `0x0100` separate.
* **Native** (`wo140.{h,cpp}`, `wo140_rules.h`): the gate on
  `C_SkipTime::ShowDialog` (keeps the held picker's arguments from the thunk's
  frame and shows it again on approve), the state edges, the accepter's
  `StartSkipTime`, `Stop`, the clock pull. Pipe **0x26 → 0xA4**, unsolicited
  **0xA5** (next free request 0x27).
* **Agent** (`Wo140.cs`, `GameBridge.Wo140.cs` + hooks in `GameBridge.cs`,
  `.Wo114`, `.Wo123`, `.Wo131`, `.Wo134`, `CombatPipe.cs`): the vote, the
  Begin, the accepter's start, the wake, the rest save, the pin and the
  fall-safe; the separate state, its drops, its log and UI state, the leash.
* **Mod** (`kdcmp.lua`, WO-140 section): the bed hold, the prompt, the waiting
  and refusal lines, the own-world message, the rest save, `mp_sleep_vote`,
  `mp_sleep_status`, `mp_sleep_yes/no`, and the checklist's `mark_<word>`
  markers (`MP-MARK`).
* **Launcher**: the joiner's Ready line; the own-world window.
* **Test peers** (never shipped): avatarpeer `sleep auto|ask|answer|begin|cancel`,
  `leash flags separate`; synthpeer `sleep …`, `clock <t>`, `sleeptime <t>`.

## 3. Things worth knowing

* **The game's Lua numbers are 32-bit floats.** A vote id of 16,777,217 came
  back from the mod as 16,777,216 (observed). Vote ids are `(ghost << 16) | n`,
  below 2^24.
* **The log's `AfterSkipTime` "started" line comes at the END of a skip**, not
  its start: the DLL's state edges are the only live signal of a running skip.
* **A clock write during a skip ends it** (the skip reaches its target): the
  first j1 accepter got almost no rest because the host's result was written
  1 s in. Now held until the skip ends (observed fixed in j3).
* **Lying in bed without a picker** (after a native hold and a no) stays lying,
  exactly as after the picker's own Back: `OnBedStop`, `OnBedInterrupt` and the
  picker's Cancel all leave the player lying (observed); `IsLaying()` reads
  false while lying, and `InterruptSitting` is not registered on this build.
  The bed path is held in Lua before any lie-down, so this happens only on the
  natively-held path (sleeping from a bed one sits on).
* **Trespass:** a house bed is someone's property: the game allows sleeping
  there but guards react to a long stay (a guard killed Henry in probe p2; the
  throwaway was reloaded). The game refuses the accepter's real sleep inside
  trespass; the forced screen still runs then.
* `wh_pl_ForcedSkipTime <h>` (MT build) is a real skip with no screen and no
  rest (id 0).

## 4. What is not proven here

* **Two real machines.** Every "other player" was a program; "both screens"
  are one real screen per role. The waking of a partner still asleep, and the
  two screens starting "together", need two players (checklist WO-140).
* **The Wait key itself**, sitting-then-sleep and reading in bed: the gate that
  catches them was observed with the bed's hold bypassed; the key needs input.
* **Three or more players**: the host's collection of a vote is code-verified
  and unit-tested, not run.
* **The launcher window** (code-verified + unit test; the launcher was not
  started: it would take focus).

## 5. Gates

See `docs/WO-140-progress.md`: every gate green (relay 55, agent 596, every
synthetic suite including the new `Test-WO140Synthetic` 61/61, the static
checks, the local payload publish and smoke, native 204). No installer, no
`VERSION` change, no GitHub Release.

## 6. Log lines

`MP-W140`, `MP-WO140-STATS`, `WO140-HOLD`, `WO140-GO`, `WO140-DROP`,
`WO140-PROMPT`, `WO140-ANSWER`, `WO140-SEPARATE`, `WO140-RESTSAVE`,
`WO140-SESSION`, `WO140-INSTALL`, `MP-MARK`; native `WO140-BUILD`,
`WO140-CONFIG`, `WO140-HELD`, `WO140-APPROVE`, `WO140-DROP`, `WO140-STATE`,
`WO140-START`, `WO140-STOP`, `WO140-PULL`; `MP-JOIN joiner: connected from its
own world -- NOT joined (separate)`; `MP-LEASH host: joiner N is in its own world`.
