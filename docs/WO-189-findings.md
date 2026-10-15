# WO-189 — the joiner never stays stuck in half of the host's quest sequence; its saves never stall silently (0.60.6)

Code: `dotnet/KcdMp.Client/Wo189.cs` (the rules), `GameBridge.Wo189.cs` (the agent), `native/KCDMP/wo189.cpp` (DLL ops 42–49),
`kdcmp.lua` (the switches). Tests: `Wo189Tests` (rules), `Wo189ReplayTests` (the two 0.60.5 tester logs), `Wo183Tests` (the class test
now covers the new forwarded lines). Every engine call is one WO-188 identified, found by RTTI class or by the game's own label.

## 1. The chain (confirmed against both logs)

| # | claim | result |
|---|---|---|
| 1 | The host never saved during the session | **Confirmed.** The host's agent shows only `world:identify` 19:36:24 (seq 1), `world:join` 19:48:22 (seq 2) and 20:38:22 (seq 3, the rejoin). World autosave off. |
| 2 | The host's tutorial wraps a world layer | **Confirmed.** 11 activations of `masterstrikeTutorial_arenaBarrier`, each followed by its deactivation, all in play; start fader on `…streamBarrierCH`, end fader on `…unstreamBarrierCH`. |
| 3 | The joiner ran only the end | **Confirmed.** No line naming that profile; `masterstrike_tutorial.po_duelu_1` at t≈948.9 (20:03:16); the scene "added into waiting players" 3× (plus several unnamed ones queued behind it); the end positioning began t≈953.1 (20:03:20) and finished after **750.29 s**. |
| 4 | 20:15:50.9, the ESC menu's snapshot | **Confirmed.** `InitiateSaveGame QuickSave` → "Cancelling FF for all NPCs" → positioning finished → `EndScene` → the end fader `OnCutsceneInitialized` (its only line) → `BeforeGameSave` never ready. WO-188's correction stands: `BeforeGameSave` waits on the AI task and profile transitions, never on a cutscene. |
| 5 | 21 minutes pending, nothing cleared it | **Confirmed.** 20:16:06.457: "its file had not come when the leave went on" — no cancel. 20:37:16.7 (ESC menu) and 20:37:18.3 (exit dialog): both "a save is pending on this machine". The rejoin restored the 19:49:11 join snapshot: 48 minutes lost. |

The clock: the joiner's kcd.log `t=` maps to its agent's clock with one offset (wall = t + 19:47:27.7), from the `[pos]`/DATA lines.

## 2. Results per part

| item | result | default |
|---|---|---|
| A1 a wait that ends without its file cancels at once (any purpose) | replay: cancels at **20:16:06**; B3's 60 s path logs the same line | on (`mp_save_recover`) |
| A2 watchdog: pending > 60 s → named, cancelled, told once, owed | replay: engine waiting since 20:15:51 → cancelled **20:16:51** (≤ 20:16:56 at any 5 s phase). Live stall **not staged**: 22 paused villagers + a QuickSave were ready at once | on (`mp_save_recover`) |
| A3 ask only when the engine can take it | replay: at 20:15:50 the post-duel scene still positioned the copies → **not requested** (owed, 15 s). Live: the readiness service read works (CSystem's pointer at +0x4A68, `IsReady(0x2A)`) | on |
| A4 joiner's own snapshot | live (interval 1 min): **paired 1.5 s**, again every minute. Replay model: pairs 19:59, 20:09, 20:19, 20:29 and **the quit at 20:37 pairs** | on, 10 min (`mp_joiner_autosnap`) |
| A4 host reminder | **live PASS** (the game as host, a synthetic joiner second, interval 1 min): shown after 51 s, on screen through the game's toast | on, 15 min (`mp_save_reminder`) |
| A5 `mp_save_status` | `WO189-STATUS` beside `WO184-STATUS`; the screen line gains the next own save / the reminder | — |
| B capture (host) | replay of the host's log: the barrier's **11 on/off pairs**, no `weather_*`/`event_*` (also forwarded: three `fogControl_*` pairs, two pillory and one cart profile — all quest profiles changed in play) | — |
| B apply (joiner) | **live PASS**: the synthetic host sent `+`/`-masterstrikeTutorial_arenaBarrier`; the joiner's own kcd.log: "Activating …" → layer requested → "Profile … loaded" → "Deactivating …" → "request cancelled". `weather_bees` refused; a profile the joiner's own quest holds is left alone | **on** (`mp_profile_mirror`) — see 4 |
| B3 join catch-up | the joiner asks (`?`) after its join; the host answers its active quest profiles (`=name`); `=` applied live | on |
| C detect | replay: flags `masterstrikeTutorial_konecTutorialu_vasko` at **t=1713.4** (10 s after 1703.4), `holder=masterstrikeTutorial_unstreamBarrierCH`; nothing else flagged in the session | on (`mp_cutscene_release`) |
| C release | live: the player resolves (PlayCutscene by its label = WO-188's function; Interrupt = slot 0x10 by its label); with no stuck cutscene the release is refused (result 0). **The tutorial could not be staged**: no throwaway save is in it (there is no `playline4/save022.whs`; `quicksave022` tracks another quest at Troskowitz) | on |
| C3 never on the host | the DLL refuses any release, FF stop or profile switch unless the agent set the joiner role | — |
| D positioning bound | replay: the post-duel positioning (began t≈953.1) bounded at **t=968.5** (20 s after its scene's enqueue) instead of 750 s. Live: the engine's per-NPC FF stop ran on a paused copy (`stopped=1`, no fault); a staged scene on copies needs a conversation (real input): not run | on (`mp_positioning_bound`) |
| F the waits named | live: readiness task names on any pending save > 5 s; the barrier's two States read through WO-137's reader (on this world: `waitingForReactors=0`, `streamHandling=None`); the positioning manager found in XGenAI's data (read only). Which copy runs a behaviour execution: **no existing read** | — |
| WO-184 C1 round (regression) | **PASS**: 3 items, host save paired 2.1 s, quit, rejoin, inventory equal (32 classes) | — |
| Frame rate in town (Tachov) | 26.6 fps with every WO-189 switch on vs 26.9 off (ratio 0.99, within 10 %): an on/off A/B in one session, window not focused | — |

## 3. The class (Part E)

What the joiner's game can start on its own from the host's quest state, whether it can then wait on something only the host's world did,
and what covers it now.

| kind | can wait on the host's world? | how (the field) | covered by |
|---|---|---|---|
| Story dialogues | yes | a forced dialogue on a copy (`po_duelu_1`): its end positioning waits for the paused copies' fast-forward | D (20 s bound), WO-167/179 black lift; A3 never saves into it |
| Scenes (the interactive scene queue) | yes | every later scene waits "after currently playing player" behind the stuck one | D, per scene |
| Fader cutscenes (barrier holders) | yes | initialized, never played: the barrier's `stopcrime` (crime reactors = copies) or its `Streaming` state never reached Unstreamed | C (the game's own PlayCutscene; Interrupt last), F names the branch |
| Other cutscenes (ingame) | yes, at their end | their end positioning on copies | D; C if one sits at "initialized" |
| Pre-rendered videos | no | shared since WO-179 | WO-179/183 |
| Profiles and layers | yes (missing, not waiting) | the host's quest layer never on the joiner (no arena fence) | B (quest profiles only) |
| Barriers | yes | the joiner's graph ran only the unstream half; mirroring the layer does **not** release it (WO-188 R2.3) | C, F; B for world parity |
| Arranged fights | yes | a quest fight whose result happens on the host (WO-175's weapon lock) | WO-175/176 (unchanged here) |
| Saves | yes | the AI readiness task waits on behaviour executions of paused copies | A1–A3 |

Input for B-2 (who runs a story sequence at all): every row above starts on the joiner because its own quest graph runs from the host's
synced quest state; B-2 would decide per kind whether the joiner runs it, waits for the host's, or skips it.

## 4. Decisions to confirm

* **`mp_profile_mirror` ships ON**, though the WO said off: its live check passed (the joiner's own log shows the profile go active, then
  inactive), and the standing rule is that new mechanisms ship on with a switch. Known risk (WO-188 R1.1, INFERRED): if the joiner's own
  quest also switches the same profile on while the mod holds it, the host's switch-off releases it for both — the joiner then matches the
  host. A load forgets every handle (never released into a new world).
* The host's reminder text uses a plain hyphen ("… 15 minutes - press Home to save."): the console path is ASCII.

## 5. What still waits on WO-188 (UNKNOWN there, not settled here)

* Which readiness task held the joiner's save (the AI task 0x02 or profile transitions 0x20) — now logged (`WO189-READY … not-ready tasks`)
  the next time it happens; not reproduced here.
* Which barrier branch stalled (the crime reactors, or a same-value `Unstreamed` set) — now read and logged at a stuck cutscene
  (`WO189-BARRIER`); the tutorial was not staged.
* ConceptModule `C_StateVariable`: does a same-value set fire its output.
* The AI module's ready states (1 and 8) by name; which copy is inside a behaviour execution (no read exists).
* The profile manager's bookkeeping for "already active" (a hidden count would change the two-holder risk above).
* Who calls `C_CutscenePlayer::Interrupt` in normal play (it is only the last resort here).

## 6. Port notes (how each call is found)

| need | found by |
|---|---|
| readiness service | RTTI `CSystem` among gEnv's pointers; RTTI `C_ReadinessService`, embedded or pointed to by CSystem (live: a pointer at +0x4A68); `IsReady` = service vtable +0x28 |
| cutscene player | RTTI `C_GUIModule` among the game interface's pointers, RTTI `C_CutscenePlayer` among its pointers; PlayCutscene = the one function referencing its label; Interrupt = the vtable slot referencing its label |
| stop fast-forward | the NPC manager's NPC by entity id, RTTI-checked `C_NPC`, vtable +0x4B8 (argument 0) |
| positioning manager | RTTI `C_ScenePositioningManager`: the NPC manager's, the AI module's, else a pointer in XGenAIModule's data (scanned at most 3 times a session) |
| profiles | the manager by RTTI two levels under the game interface; EnableProfile / the handle's release = the slots referencing their labels; the profile list = the vector whose first element is a `C_GameProfile`; active = its `+0x38` |
