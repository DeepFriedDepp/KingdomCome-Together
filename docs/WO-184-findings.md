# WO-184 — 0.60.0: the joiner's progress is saved, every time

2026-10-13/14, one unattended night. Version 0.60.0. Built on `main` 0.50.9 (`a545b66`). Evidence: the 0.50.9 logs of the host and
Moose (the joiner) and the 0.50.8 log of Mystigan (the joiner), kept privately and never committed. Proven live in the real game (the
Modding Tools build as the joiner of a synthetic host through a real local relay), by replays of the field logs, and by unit tests.

## Result

| part | result |
|---|---|
| A — audit | every path that can ask, wait, skip, drop, refuse, cancel or replace a joiner snapshot (30 in the agent, 5 in the mod, 3 in the DLL), every flag they read and its longest life, the timeline since WO-125 (the full audit is kept privately; the short form is below) |
| B1 — two hard blocks | the snapshot is asked at every host save, as WO-125 did. It waits only for a save already pending on this machine (the engine's own state) or for "not in the world" (loading, a join, the host's reload). Conversation, scene, fader, crime, downed and menu are no blocks any more. The engine's own "not now" (its QuickSave refused) is asked again every 2 s. No 20 s wait was needed: no case was found where asking makes the engine misbehave |
| B2 — every flag ends | the conversation clock starts only on a talk the player asked for or a forced dialogue with Henry in it; never on a bark, a combat shout or a monologue; it ends on every end line, a cancel, a time-out, an interrupt, no start within 5 s, and after 3 min at most. Every other flag with no end has one now (table below) |
| B3 — stuck | a request with no file and no "ready" in 60 s is stuck: logged, both players told, then the engine's own cancel (`CancelPendingSaveGame`, the call the game makes before every load) clears it and a fresh snapshot is taken. Proven live on a really pending save |
| B4 — nothing dropped silently | a final snapshot when the joiner leaves (the quit, the mod's leave, the host gone, the relay lost), paired with the newest host save; the quit's is started when the ESC menu opens. No retry cap. A leave that cannot take it says so. The quit notice says what is really saved |
| B5 — save health | every host save ends in a pair or one logged reason within 60 s; two in a row unpaired: both players told; `mp_save_status` on both machines; the host shows "Partner saved." on the joiner's pair (W164 kind 16, appended) |
| B6 — the restore | a rejoin restores the newest pair on the host's branch: proven from the code, the field rejoin and a test |
| C1–C5 | all pass (numbers below) |

## What broke, and which earlier WOs caused it

The joiner's Henry is kept by a snapshot: at every host save the joiner's game makes a QuickSave, the agent stores the Henry in it, paired with
that host save; a rejoin restores the newest pair. That worked until 0.50.7.

**The root, common to 0.50.7 and 0.50.9: Henry's own combat shouts counted as conversations.** The game logs every shout as a dialogue
attempt (`Attempting to start new dialogue … 'Ex: Dude - meta override: COMBAT_ACTOR_SCREAM_ATTACK'`), and such an attempt never ends.

| build | change | effect on the joiner's saves |
|---|---|---|
| 0.29 (WO-125) | the snapshot at every host save | works |
| 0.43.0 (WO-151), 0.44 (WO-154), 0.48.5 (WO-167) | the mod asks the joiner's game for a QuickSave to unstick a scene | a second, mod-made save request (harmless until 0.50.7) |
| 0.48.5 (**WO-167**) | **the conversation clock is started by every dialogue attempt with Henry in it**, barks and combat shouts included (`GameBridge.Wo137.cs:728`), though WO-147 had already written the bark test two lines below | latent |
| 0.50.0 (WO-176) | the save safeguards | **not implicated** (0.50.9's first host save paired with the lock held) |
| 0.50.7 (**WO-179**) | A3 ends a "conversation" with no sign of life after 3.5 s — on that clock | every shout was a "conversation": 36 false releases in Mystigan's 0.50.8 log, each followed by a scene-rescue save; the first one asked during an arrest never became ready, and the engine refused every later save (35 × "another savegame pending"), the host's seq 3 and 4 snapshots included |
| 0.50.9 (**WO-183**) | the rescue saves removed (right); **one save gate that waits for "a conversation is running", read from the same clock**, plus five other mod flags (three with no end); owed snapshots dropped when the joiner left | after the first shout or hit, or a talk that timed out, every later host save was owed, then dropped at the quit: Moose's seq 4–8 and 11; the rejoins restored seq 3's Henry, from before both item drops. The quit notice (WO-125's) still said "saved up to your host's last save" |

**Earlier WOs that caused it (the maintainer's rule):**
* **WO-167** built the conversation clock from every dialogue attempt with Henry, combat shouts included.
* **WO-151, WO-154, WO-167 and WO-179** asked the joiner's game for saves to unstick scenes; **WO-179** multiplied them by firing A3 on
  the shouts; one stayed pending forever (0.50.8).
* **WO-183** made every snapshot wait for that clock and for other mod flags with no end, dropped what was owed at a leave, and kept
  **WO-125**'s untrue quit notice.

## The flags with no end, and what each ends on now

| flag | before | now |
|---|---|---|
| the conversation clock (`_w179DudeStartMs`) | started by any attempt with Henry, shouts included; ended only by a `Dialog ending` with Henry | a real talk only; ends on an end line, a cancel, a time-out, an interrupt, no start within 5 s, 3 min at most, a load |
| the dialogue pause reason (`_w138Dialog`) | cleared only by the Lua chain a load kills | cleared by any load; the save no longer reads it |
| the local scene (`_w179Cur`) | no end line = forever | forgotten after 30 min with no end line (and at a load) |
| the interactive-scene count (`_w167IsmEnq/End`) | an enqueue with no end = counted running until a load | reset after 10 min with no queue line |
| a guard's crime stop (`_w139StoppedHere`) | outcome lost at a load = forever | forgotten after 15 min with no outcome, and at any load |
| the host's reload (`_rewinding`) | a host that never comes back = forever | after 10 min the joiner leaves the host's world (told) |
| the pending-save tracker | believed "not pending" after 30 s; a save that never became ready was never seen as stuck | the engine's own state through the DLL; stuck at 60 s (B3) |
| downed (`_localDowned`) | no end in the agent | the save no longer reads it |
| an owed snapshot | 60 tries, then given up; dropped at a leave or a load, silently | no cap; taken by the leave's final snapshot, or the player is told |

## The fixes, briefly

* **B1** `Wo184Rules.SaveBlock` has exactly four inputs (pending, a join, the host's reload, loading); a test pins that it cannot even be
  told about anything else. The snapshot is the only save request the joiner's agent makes (a test pins it).
* **B2** one clock (`Wo184Rules.ConversationClock`) feeds WO-179's A3 and WO-167's scene release; every line it reads is forwarded by the
  log tail (WO-183's class test covers the new handlers).
* **B3** the DLL reads the engine's `IsAnySaveGamePending` / `IsSaving` / `CanSave` and can call `CancelPendingSaveGame` (looked up by
  name, guarded, main thread). `mp_save_recover` (default on) switches the automatic cancel.
* **B4** a final snapshot at every leave (`mp_save_on_leave`, default on); for a quit it starts when the ESC menu opens (the DLL's
  PauseGame gate sends the menu's edges to the agent, in a session only), because the game logs its exit dialog only about 1 s before
  it ends; the exit dialog takes one itself if the menu's did not cover it. A snapshot whose file lands late still pairs (it was moved
  out as a "leak" before). WO-125's 3 s lag rejection is gone (the accepted late rule).
* **B5** `WO184-HEALTH` lines, the two-in-a-row warning, `mp_save_status`, "Partner saved." on the host (`mp_partner_saved`, default on;
  shown once per host save, a later snapshot of the same save only logged).
* The quit notice says what is true: "saved, up to the moment you quit", "saved up to your host's save #N (… ago)", "saved as it was
  when you last opened the menu", or "NOT saved (why)".

## Proof

**C1 — live, end to end, three rounds** (items dropped by the host, picked up with the game's own use action; host save; quit; restart;
rejoin; the whole inventory read in game):

| round | items | host save | paired after | restored | inventory at the save vs after the rejoin |
|---|---|---|---|---|---|
| 1 | 3 | seq 1 | 2.6 s | that snapshot | equal, 31 item classes |
| 2 | 5 | seq 2 | 2.6 s | that snapshot | equal, 35 |
| 3 | 7 | seq 3 | 2.6 s | that snapshot | equal, 42 |

**C2 — a blocker right before the host save** (each case also picked up an item first):

| case | the blocker as it was at the save | paired after |
|---|---|---|
| a | a talk request that timed out | 2.6 s |
| b | Henry's monologue (after photo mode) | 2.6 s |
| c | a dialogue with Henry still running (a villager's greeting) | 2.1 s |
| d | a fight running (a wolf pack; Henry in the skirmish, 13 hits on him before the save) | 2.6 s |
| e | the screen faded by the game's own fader | 2.6 s |
| f | the ESC menu open | 2.1 s |
| g | Henry shouts and is hit (3 attack shouts, 5 hits), the fight over just before the save | 2.6 s |

Then quit, restart, rejoin: the inventory equal (45 classes), every C2 item there. Three case runs that did not raise their blocker
(a talk that never started, two fights that were over or never began) were not counted and were rerun. The engine accepts a QuickSave
in the middle of a fight.

**B4 live.** The joiner picks something up, then quits: with the exit dialog's line 1.0 s before the quit, the final snapshot paired
(three runs). 0.3 s before it, the game quit before the save was written: nothing was paired, nothing was left behind, and the notice
said the true, older save. With the ESC menu's snapshot: the same 0.3 s case passes, and so do the real keys (Esc, W, W, Enter on Quit,
W, Enter on Yes) 0.35 s and 0.2 s apart (1.7 s from Esc to the last key): the item was there after the rejoin each time.

**B3 live.** A QuickSave left pending, then the engine's own cancel: pending 1 → 0, the engine logged "has canceled async waiting", no
file; the next host save paired in 2.6 s.

**C3 — replays of the field logs** through the real log filters (0.50.9's rules beside the new ones):

| log | Henry's combat shouts | A3 releases 0.50.9 → now | host saves |
|---|---|---|---|
| Moose, 0.50.9, first launch | 12 | 11 → 0 | seq 4–8: owed ("a conversation is running") → asked at once |
| Moose, 0.50.9, second launch | 1 | 1 → 0 | seq 11: owed → asked at once |
| Mystigan, 0.50.8 | 147 | 34 → 2 | seq 3, 4: refused (a rescue save pending) → asked at once; 0.50.8 asked 37 QuickSaves, the new code asks only the two snapshots |
| four more bundles | 0–213 | 4–21 → 0–2 | — |

The two A3 releases left in Mystigan's log are both a haggle with the merchant `ttkc_emerich`
(`…bribe_haggle_negotiation…smlouvani_v_obchode_muz`, t=306 and t=397): a real talk that shows no sign of life to A3 (WO-183 found the
same two; their cause is WO-181's research, not this WO's). The new clock is never open longer than 5.1 s in any bundle; 0.50.9's
stayed open for the rest of the session (498 s at the end of Moose's first launch).

**C4 — the live save round trip** of the build: in the installer record below.

**C5 — unit tests:** 38 WO-184 tests, the 5 replays among them (the two hard blocks, every flag's end, the clock, stuck at 60 s, the
final snapshot and the ESC menu's, the health check, the restore choice); the agent's whole suite 1663/1663; the native suite 9478/9478
(the menu edge included); the Lua synthetic suites (WO-184's own: the switches, `mp_save_status`, the snapshot answer).

## For the maintainer to confirm

1. **A final snapshot at every leave, not only when one is owed** (`mp_save_on_leave`, default on), and the ESC menu's early one. The WO
   asked for the leave's snapshot when one is owed; this goes further so that "the joiner quits right after picking something up" keeps
   the item. Both use the accepted late rule, with its known edge: if the host later loads an older save, the joiner keeps what they got
   after it (the world gives it back too).
2. The host's warning reads "they should restart their game", not "he should restart his game".
3. Two harness-only hooks in the agent (the screen fade and a pending save cancelled), live only with `KCDMP_TEST_W184=1`, which the
   launcher never sets.
4. Not run live: the leaves other than the quit (each loads the player's own newest save, which on the test machine is a real save), and
   the host's side of B5 (the test host is synthetic).

## Seen, not changed (out of scope)

* The talk with `ttac_blacksmith` timing out at once (pocketed by the WO).
* A3 still ends the two `ttkc_emerich` haggles above (WO-181).
* A village fight adopts the host's figure (known since WO-176): after the joiner's Henry was killed by the guards in a test, one guard
  kept hitting the figure.

## Switches (console)

`mp_save_status` (both machines) · `mp_save_on_leave on|off` (default on) · `mp_save_recover on|off` (default on) ·
`mp_partner_saved on|off` (host, default on).
