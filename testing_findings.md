# Testing findings — 0.45.6 to 0.45.8 (2026-10-07)

Field reports from the maintainer's own sessions and two testers' sessions, what the logs showed, and what was done.
Everything below "Fixed after 0.45.8" is **on origin/main but in no installer yet**, and **none of it is verified live**.
The last installer is 0.45.8 (`release\KingdomComeTogether-Setup-0.45.8.exe`, local only, unsigned, soak waived).

Evidence detail for each item: `docs/WO-159-findings.md` (sections named below).

**Update (WO-160, 2026-10-07):** items 5–8 and the recap fixes are in the 0.46.0 release candidate (a local installer, not
tagged); with them the joiner's NPC copies were rebuilt (`docs/WO-160-findings.md`): the planner errors behind items 5–8 and
the "unable to talk to NPCs" bundles were the copies' torn context, the night's undress left on paused copies, and refused
placements leaving an unreachable demand. Item 9's sleeping-NPC desync is covered by that night-state fix (solo-verified, two
players pending); the phantom weapon / "enemy with a sword the joiner does not see" is **unverified** (probably the same
undress); the swing with no combat and the silent damage are still not investigated. The version string was answered: 0.46.0.

---

## Status at a glance

| # | Report | State | Commit |
|---|---|---|---|
| 1 | Host did not auto-connect after Start Game (0.45.6) | Fixed in 0.45.7 | `5401f85` |
| 2 | Prologue cutscenes: video sound very quiet, world sound audible | Fixed after 0.45.8, unverified | `5d058af` |
| 3 | Prologue cutscenes: "Skip All" skipped one video, the next started later | Fixed after 0.45.8, unverified | `5d058af` |
| 4 | 0.45.8: "Skip All" froze the game | Fixed after 0.45.8, unverified | `5d058af` |
| 5 | Door open for the host, shut for the joiner | Fixed after 0.45.8, unverified | `366349d` |
| 6 | Joiner's doors pushed unlock-and-close into the host's world | Fixed after 0.45.8, unverified | `366349d` |
| 7 | Joiner saw a trespass warning walking into a shop | Fixed after 0.45.8, unverified | `e0ea21e` |
| 8 | Joiner had no Trade option with a merchant | Fixed after 0.45.8, unverified | `41c0d68` |
| 9 | Sleeping NPC desync; phantom weapon stance; swing with no combat; silent damage | **Not investigated** | — |

**Tell testers on 0.45.8:** use "Skip the prologue", not "Watch the prologue" (item 4 can freeze the game).

---

## 1. Host auto-connect after Start Game (0.45.6) — fixed in 0.45.7

* **Report:** launched from the launcher, the game did not connect; it sat at CONNECT.
* **Cause:** the launcher tried to connect as soon as the world had settled on its own clock, about a second before its
  CONNECT gate opened; the try was refused, never repeated, and the game's HUD said the connect had failed.
* **Fix:** the host's auto-connect also waits for the gate to be open; the menu-logo removal at exit retries for 15 s.
* **Check:** the next Start Game as host connects without touching the launcher.

## 2–4. "Watch the prologue" (the recap of the prologue's rendered cutscenes)

**2. Quiet video, audible world (0.45.7).** The recap played each video with `wh_ui_PlayMovie`, the bare movie player.
The game's own cutscene player fires the audio trigger `audio_setup_video` (`silence:on` + the FMOD snapshot
`setup_video`: world muted, video mix up); the bare player does not, and it never pauses the world.

**3. Skip All skipped one video (0.45.7).** The movie player's own skip ends only the current video and writes nothing to
the log; the recap waited out the skipped video's length, then played the next. The mod's "hold E" skip never fired: a
video takes the keys, so Lua's `handleAction` never sees E.

**4. Skip All froze the game (0.45.8).** 0.45.8 played the videos through `wh_ui_PlayCutscene` (the cutscene player).
That paused the game (agent: `world FROZEN, held 0x10`, the video pause) and, after Skip All, the pause was never
released (still frozen a minute later; the ESC menu worked, the game was quit from it). A console-played cutscene has no
holder to finish it and logs no `CutscenePlayer::` lines, so the recap got no end signal and its own timers were frozen.

**Fix (all three, `5d058af`):** back to the bare movie player (never froze), plus the recap fires `audio_setup_video` on
the player's audio proxy (`player:ExecuteAudioTrigger(Sound.GetAudioTriggerID(...), player:GetDefaultAuxAudioProxyID())`,
the game's own scripts' pattern) and stops it at the end, on a skip and on a dead timer chain. The skip is detected by
movement: inside a video the player cannot move (0.45.7 log: no movement inside any video, movement right after each of
three skips), so a move over 0.3 m or a turn over 0.05 rad, from 1.5 s into a video, ends the whole recap. The HUD line
before the first video now says "Skipping one skips them all."

**Check (Watch the prologue):** video sound loud, world silent; one Skip All returns to the world for good once you move;
the world's sound normal afterwards (if it stays muted, the trigger's stop failed); the music normal in Troskowitz (the
siege-end video used to toggle a 10 s battle-music state; with the movie player that row is not involved).

## 5–6. Doors (testers' session, Troskowitz)

* **Report:** a door open for the host was shut for the joiner; the host opened and closed it; the joiner walked in.
* **Cause:** the engine locks a building's doors when its area turns private and unlocks them when it opens
  (`AnimDoor:SetLockedDueToPrivate`, called by no script), and `Lock()` also **shuts an open door**. On the joiner the
  owners are paused copies whose NPC states never settle (repeated `ChangeAreaLabel ... antitrespass` errors), so the
  joiner's copy of the town flips between private and open — the host's open door was shut again on the joiner only.
* **Second effect:** each flip back to open unlocked that building's doors on the joiner, and the mod's Unlock wrap took
  any unlock outside the player's own use for his lockpick: two doors of one house were "unlocked here" in the same
  instant five times, each applied **in the host's world** as an unlock-and-close.
* **Fix (`366349d`, joiner only):** the joiner's copy only notes the privacy flag (no lock, no shut — the host's world owns
  every lock); an unlock asks the host only within 300 s of the player's own `Lockpick` on that door, once.
* **Check:** a door the host opens stays open on the joiner; the joiner's log has no "unlocked here" asks he did not make;
  `mp_door_sync` status shows `privacy_skipped` / `own_unlocks` counts.
* **Note for reading testers' logs:** the two bundles' clocks were one hour apart (match events, not times).

## 7. Joiner's trespass warning (same session)

* **Report:** walking into the shop, the joiner's screen showed a trespass. Both players could enter — a visual issue.
* **What the logs showed:** the joiner's game raised it (`WO139-TRESPASS level 2 -> 3 (personal)`) and reported it; the
  host received it (`reports_in=1`) and judged nothing (`judged=0`, no record, no guard) — the host's world decided
  correctly. Only the joiner's own HUD warning was wrong (it should be hidden in a session since 0.45.2).
* **Cause:** GUIModule has **two** callers of `C_UIHudStates::SetTrespassState` (RVA 0x27EBB0): the event listener the
  0.45.2 gate hooks (0x27E4C0) and the **HUD's own refresh** (0x27E3D0), which reads the player's level itself and calls
  the function directly, past the gate. The 0.45.2 live check (a jump straight into a house) never met the refresh.
* **Fix (`e0ea21e`, DLL):** `SetTrespassState` itself is gated too (17-byte prologue registered in `hook_prologues.h`;
  native tests 407/407). The joiner's agent now logs the DLL's hidden-warning counts after a minute with a trespass.
* **Check:** the joiner's native log at game start says `WO139-BUILD trespass HUD gate ARMED ... (the HUD's own refresh)`;
  walking into a closed house or shop shows no warning on the joiner.

## 8. No Trade option for the joiner

* **Report:** both players could talk to a merchant; the joiner saw no option to sell. (No log of that talk was available.)
* **Cause (game data):** the shop dialogue is offered only when `CheckEntityContext(seller, "shop_sellerReadyToSell")`
  (`utils/shop/is_seller_in_shop.xml`). That entity context is held by the keeper's **work activity** while the shop is
  open (`so_seller.xml`, the blacksmith's, the tavern's). On the joiner the keeper is a paused copy: the activity never
  runs (and the 0.45.2 talk reset clears it on purpose), so the context is never on.
* **Fix (`41c0d68`; protocol + DLL allowlist, no new hook):** the host names its keepers ready to sell within 300 m of
  either player every 10 s (`soul:HasScriptContext`); the list goes to the joiner (new crime kind `CrimeHostShops = 8`,
  append-only); the joiner sets the same context on those copies through the DLL's context op and clears only what it
  set, when a keeper leaves the list or the session ends. The host's shop hours decide.
* **Check:** the joiner gets Trade with a merchant whose shop is open on the host; agent log
  `MP-W139 joiner: <npc> ready to sell here ... set`. **Open:** whether Trade then opens the shop window on a copy whose
  own shop-open step never ran (0.45.2 once saw "Trade closed at once" on a copy stuck in a state).

## 9. NPC combat and state desync — reported, not investigated

Reported by the maintainer from the testers' session (no logs analysed, no changes made):

1. **Sleeping NPC:** an NPC was asleep for the host but not for the joiner.
2. **Phantom weapon stance:** when both players attacked different NPCs, on the **host** the NPC attacking the joiner stood
   as if holding a weapon, but no weapon was visible.
3. **Swing with no combat:** that NPC swiped at the joining player on the host's screen, but no combat was received.
4. **Silent damage:** the joining player still took damage, silently (no hit reaction / no visible combat).

Things that may be relevant when this is picked up (from earlier work orders, not checked against these reports):

* Joiner NPCs are paused copies driven by the host's stream; sleep/beds and other NPC-state elements are applied through
  the WO-141/143 placement (`NPC-state stance/unstance`), and copies whose states never settle are a known source of
  differences (items 5–8 above all trace back to that).
* NPC swings on the joiner's copies are native (WO-49: the brain fights the drawn state; weapon must be drawn —
  WO-45/47; polearms draw from inventory first). A stance without a visible weapon suggests the weapon draw/attach did
  not happen on that body.
* Damage to a player travels by name-addressed hits (0x30/0x31, WO-40) and the hit slot queues damage (WO-121); damage
  arriving without the swing being shown fits "the hit applied, the animation/reaction did not".
* Needed to investigate: log bundles from **both** machines of that session, the NPC's name (or place), the time, and
  which machine attacked which NPC.

---

## Pending

* **Version string** for the next installer (it carries items 2–8; both players and the relay must run the same build —
  the protocol and the DLL changed).
* Live checks listed under each item.
