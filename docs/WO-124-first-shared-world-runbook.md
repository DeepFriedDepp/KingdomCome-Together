# The first shared-world session (two machines)

For the maintainer (the **host**) and one partner (the **joiner**). About 30
minutes. What it proves: the partner's character arrives in your world, plays
there, and keeps its progress in your world from one session to the next.
Background: `docs/WO-124-findings.md`, `docs/WO-125-findings.md`.

## Before you start

* Both machines on **0.30.0** (`KCDMP-Setup-0.30.0.exe`; the launcher's bottom
  bar shows the version). Both on the same build: an older host never tells
  the joiner which world it runs, and the relay refuses a different release
  anyway (the launcher says so in plain words, on both sides).
* The tester page for this build is `docs/TEST-0.30.0.md` (what to look at
  for the 0.30.0 fixes, then this runbook).
* **Joiner: delete any copy of the host's save you copied in by hand** under
  the old 0.28.x method. The mod ignores those now (a save of the host's
  playthrough is never treated as yours), but they clutter the save list, and
  if one is your newest save the main menu's Continue still loads it.
* The joiner needs **a save of their own with Henry in it** to bring their
  character, or **a new game's first save** (the one right after the prologue)
  to start fresh.
* The host is in the world in **daytime**, somewhere quiet, not in a fight,
  dialogue or cutscene, and past the prologue (the joiner can't join while the
  host plays Godwin). **Make a save of your own first** (it is your world).
* Host starts first: the host's launcher **HOST GAME** (relay on this machine).
  Leave **Also allow Steam** ticked: the window shows the host's code and
  "Steam: ready". Since 0.29.9 the host is the authority whatever order people
  connect in (WO-127), so starting first is only for convenience.

## Start it

1. **Host**: launcher → HOST GAME → START GAME. Load your save. Console (`~`):
   `mp_leash_trace on` (you read `WO127-LEASH trace=on`; the recorder for
   WO-128). The shared world is **on by default** since 0.30.0: nothing to
   type. `mp_shared_world` alone reports it (`WO122-TOGGLE shared_world=on`);
   `mp_shared_world off` goes back to separate worlds.
2. **Joiner**: launcher → **JOIN THROUGH STEAM**, type the host's code (or
   FIND FRIENDS), **TEST CONNECTION** first (reachable, same version, round
   trip), then **JOIN** → Launch. If Steam fails the launcher says why and
   offers the host's address in the same window (CONNECT BY ADDRESS); the old
   way (Add Server → TEST CONNECTION → JOIN SERVER) works as before. **Stay at
   the main menu.** Do not press Continue.
3. **First time in this host's world only:** the joiner's launcher asks
   **Bring my character** / **Start fresh**. Nothing is asked of the host
   until you answer, so take your time.
   * Bring: the character from your newest own Henry save.
   * Start fresh: a new game's starting Henry (from your first save after the
     prologue). Never the host's character.
   * The two buttons are in the launcher window, above the connection line.
     Nothing is typed in the game for the join.
4. Wait. Nothing else to click.

## What each screen should say

| when | host | joiner (launcher banner) |
|---|---|---|
| joiner starts the agent | — | "Connecting to your host..." (bottom line; it clears once connected) |
| joiner connects | — | "Waiting for your host..." |
| first time in this world | — | "First time in this world: bring your character, or start fresh?" + two buttons |
| host busy (fight, dialogue, loading) | nothing | "Your host is busy, you'll join in a moment." |
| host pauses | "<partner> is joining -- saving the world... N s", then "Sending the world to <partner>... N%" (under it: `[x] save [>] send N% [ ] load [ ] ready`) — world frozen, keys dead | "Your host is saving the world..." then "Receiving the world... N%" |
| joiner prepares | same | "Preparing your character..." |
| joiner loads (about 50-60 s, loading screen) | "<partner> is loading your world... N s" (the seconds count up; no percentage) | "Loading your host's world..." |
| in | world resumes by itself | "In your host's world." + in game: "Co-op: you are in your host's world." |

The joiner arrives **3 m beside the host**, with their own character, in the
host's world (time, NPCs, quests).

## What is kept now

* Every time **the host's game saves** (the 5-minute autosave, `mp_world_save`,
  a manual save, the exit save) while the joiner is in, the joiner's character
  is stored for this world. The joiner's screen hitches for about half a
  second each time; that is the snapshot.
* When the joiner quits (or crashes), their character in this world is the
  one from **the host's last save**. The launcher says: "Your progress in this
  world is saved up to your host's last save." Anything after that save is
  lost, never duplicated.
* Next time, the joiner's character for this world comes back by itself (no
  question). Their own saves are never touched.
* Each host world has its own character on the joiner's side; joining another
  host world never changes it.

## Try

* Walk, run and sprint side by side: the partner's legs must move with the
  speed (no gliding), on both screens. The same for NPCs walking near the
  joiner, on the joiner's screen.
* **Swing at an NPC, and at each other** (friendly fire is on): the other
  screen must show the same swing on your figure. This is the one 0.30.0 fix
  that could not be tried with a real mouse click before release; note it if
  it does not show.
* A fist fight with each other; one of you rides a horse and gets off (the
  partner's avatar must come off it).
* Joiner: die once (a fall, a fight, anything): you must wake up somewhere
  else with a **grave** holding your things where you died (0.29.9 made no
  grave for a joiner).
* Wander apart (the host far away, a few hundred metres): the NPCs around the
  joiner must keep moving normally on the joiner's screen.
* Joiner: the pause menu's **Save & Quit** is greyed out. That is correct:
  only the host saves this world.
* Host: `mp_world_save`. The joiner's screen hitches once (the snapshot).
* **Rejoin:** joiner picks something up, host `mp_world_save`, joiner picks up
  one more thing, then quits and starts the game again and waits at the menu.
  After the join: the first thing is there, the second is not.
* **A host reload takes the joiner along:** host loads a save of this world
  (from the pause menu). The joiner sees "Your host is reloading…", then
  rejoins from inside the world by itself (about 15 s) and their character
  rewinds to where it was at that save.
* Joiner console: `mp_henry_files` lists the host worlds your character is
  stored for.

## Staying together (from 0.30.2, WO-114)

* The joiner past **600 m** from the host: a warning on both screens, once.
* Past **650 m**: a 10 s countdown on the joiner's screen; back inside stops
  it; at zero the joiner is put 3 m beside the host (dismounted first). Log:
  `MP-LEASH pulled from=<m> to=<m> residual=<m>` (joiner), `MP-LEASH host:`
  lines (host).
* Never while either player is dead, loading, talking, in a cutscene or a
  menu (the countdown waits, then resumes).
* Only the host fast-travels: the joiner comes along on arrival; the joiner's
  own fast travel is refused with a message.
* Try it: the joiner rides away from the host past 650 m and waits; then the
  host fast-travels once.

## Fights together (from 0.30.5, WO-131)

* **The joiner never runs NPCs of their own.** Every NPC of the host's world is
  driven by the host's stream or parked on the joiner (suspended + hidden).
  Log (joiner kcd.log): `WO131-GUARD state=on`, `WO131-GUARD park|unpark
  npc=<name>`; status `mp_w131_status`; off switch `mp_npc_guard off` (gives
  every body back). The host streams NPCs past 60 m out to 150 m of either
  player on a 2 s heartbeat.
* **A joiner's hit counts only on the host's NPC where the host has it**
  (bound, fresh, within 3 m). Refusals: `MP-DMG dir=drop ... reason=wo131-<why>`
  in the joiner's agent log (`not-bound`, `far-from-host`, `stale-stream`,
  `dead-on-host`, `no-answer`).
* **Deaths are the host's.** A joiner's copy carries the imm guard, follows the
  host's health (`MP-WO131 follow`), and dies only on the host's order at the
  host's position (`MP-WO131 copy guard off <npc>: the host's death is applied
  now`). A local death is never sent (`[npcdeath] out: ... NOT sent (WO-131`).
* **Looting and pickpocketing host-owned NPCs is blocked on the joiner**
  (`WO131-LOOT blocked npc=...`, and the game's own toast).
* **Road encounters:** an NPC only the host has gets a stand-in under its own
  name on the joiner (`WO131-STANDIN spawn npc=...`); `mp_npc_standin off`.
* **Perception (host):** avatars are never AI-ignorant in a shared world and
  join the host player's faction (`MP-WO131 avatar N -> the player's faction:
  joined`); `mp_avatar_perceive off` goes back to the old rule.
* **After a death:** StopFight on the dead player's skirmish (`WO131-STOPFIGHT`
  in kcdmp-native.log; `MP-WO131 peer N died|respawned: StopFight` on the host).
* **No stuck poses:** a copy ragdolled by a local blow while the host has it
  standing is stood up (`WO131-STANDUP npc=... why=not-living`).
* **Leash:** the countdown is cancelled only back under 630 m; a dialogue hold
  needs a real conversation (the DialogTwin stand-in), not chatter.
* Counters every 60 s in the agent log: `MP-WO131-STATS`.
* Two-player checklist: `docs/WO-131-findings.md`, section 7.

## Expected not to work yet

* **No "back to the main menu"** in KCD2. If the host leaves, or a check
  fails, the joiner is told and their **own** newest save loads. With no save
  of their own at all, the game drops to the main menu with a "Game load
  failed" box: press OK. That is intended.
* **Talking to NPCs near the host:** on the joiner's machine those NPCs are
  held still (suspended), so the joiner can't talk to them (WO-112).
* **NPCs don't fight back when the joiner hits them:** they turn, but don't
  swing (WO-121).
* **NPCs stand instead of sitting** on the joiner's screen.
* **The prologue and Godwin's part of the story:** no join while the host
  plays one of them ("Your host is in a part of the story where you can't join
  yet."). What to do there together is a later WO.
* Quest progress does not reach the other player live (a later WO).
* Already in a world when connecting? The joiner is told to quit, restart and
  wait at the main menu.

## If it goes wrong

* Host stuck frozen: console `mp_join_cancel` (the world resumes at once). It
  also resumes by itself after 180 s.
* Joiner stuck on the loading screen for more than 3 minutes: quit the game;
  the host resumes by itself.
* Joiner wants to start over in this host's world: `mp_henry_reset` (the next
  join asks Bring / Start fresh again).
* The two buttons don't show on the joiner's launcher, or its bottom line
  stays on "Connecting...": send the launcher log (`app*.log`): since 0.30.0
  it has an `Agent status:` line for every change it read from the agent.
* Anything else: note the time, carry on or stop, and capture (below).

## Capture afterwards (both machines, before relaunching the game)

* `kcd.log` from the Modding Tools folder (the next launch overwrites it).
* The agent log (`agent.log` beside `KcdMpClient.exe`) and
  `kcdmp-native.mirror.log` (Modding Tools folder).
* The host: the relay's log (`relay*.log` beside `KcdMpServer.exe`).
* The recorder's CSVs: the `leash` folder beside `KcdMpClient.exe` (the
  launcher's REPORT BUG zip includes them).
* The launcher's log (`app*.log` in the launcher's folder; REPORT BUG takes it).
* Screenshots of anything odd (the launcher's two buttons, please).
* Do **not** send save files or the joiner's `KCDMP\henry` folder around
  (saves name the machine's account).

Lines worth grepping: `MP-JOIN`, `MP-HENRY`, `MP-SAVELOCK`, `MP-JOINPLACE`,
`MP-DISMOUNT`, `WO124-`, `WO125-`, `Game load failed`, since 0.29.9
`MP-CONN`, `MP-HOST-CLAIM`, `MP-AUTHORITY-OWNER`, `MP-LEASH`, `[steam]`, and
since 0.30.0 `WO129-GAIT tag hook`, `WO121-GAIT … class= … tags_applied=`,
`WO129-CAPTURE drop reason=`, `WO129-SHARED`, `ACTIONS: graves live`,
`MP-WORLDSAVED … skew_removed=` (native/agent/kcd.log) and `Agent status:`
(launcher log).

First checks afterwards (the maintainer, from the logs):

1. Both native logs: `WO129-GAIT tag hook installed`. Both kcd.logs: no
   `requested logical speed id … out of range`.
2. The last `MP-WO121-STATS` line on each side, after the swings:
   `cap_attack` above 0. If it is still 0, `cap_drop_notca`, `cap_drop_nodesc`,
   `cap_drop_noguid`, `cap_drop_noowner` and the first `WO129-CAPTURE drop
   reason=` lines say why; `cap_via_base8` counts the swings the 0.30.0 fix
   let through.
3. Joiner: `ACTIONS: graves live`, and at its death `MP-GRAVE made`, not
   `grave NOT made`.
4. Host: `WO129-SHARED` once the shared world is on, and no `WO1025-COLOCATE
   event=exit … released=` while the two of you are apart.
5. Launcher logs: `Agent status:` lines reaching `connection=connected` and, on the
   joiner's first join, `join=choose buttons=shown`.
