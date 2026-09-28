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

## Fights together (from 0.30.5, WO-131; 0.30.7, WO-132)

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
* **Looting host-owned bodies (0.30.9, WO-134):** no longer blocked; see
  "Loot together" below. **Pickpocketing** a living host-owned NPC stays
  blocked on the joiner (`WO131-LOOT blocked npc=... kind=pickpocket`).
* **Road encounters:** an NPC only the host has gets a stand-in under its own
  name on the joiner (`WO131-STANDIN spawn npc=...`); `mp_npc_standin off`.
* **Perception (host):** avatars are never AI-ignorant in a shared world and
  join the host player's faction (`MP-WO131 avatar N -> the player's faction:
  joined`); `mp_avatar_perceive off` goes back to the old rule.
* **After a death (0.30.7, WO-132):** only the dead player leaves the fight.
  Host agent log: `MP-W132 peer N downed|died: its avatar left its skirmish ->
  removed`; native: `WO132-LEAVEFIGHT` (the host's own death too; StopFight
  only as the fallback). The avatar is healed of bleeding and hidden where it
  fell (`WO132-AVATAR down`), shown again after the wake (`WO132-AVATAR up`).
* **No stuck poses:** a copy ragdolled by a local blow while the host has it
  standing is stood up (`WO131-STANDUP npc=... why=not-living`).
* **Leash:** the countdown is cancelled only back under 630 m; a dialogue hold
  needs a real conversation (the DialogTwin stand-in), not chatter.
* **Nobody is hit while dead or waking (0.30.7):** the host forwards nothing
  to a player from their down until 5 s after their wake (`MP-W132 npc hit on
  avatar N ... NOT forwarded: its player is down|waking`); the joiner refuses
  one in the same window itself (`[playerhit] ... REFUSED`).
* **Only real hits (0.30.7):** an NPC's hit on the partner's figure is measured
  at the hit itself (`WO132-HITS npc hit on avatar` in kcdmp-native.log, `MP-W132
  npc hit on avatar N by <npc> ... forwarded` in the host agent log); bleeding
  is never sent. One path per hit (`reason=wo132-one-path` for the old second).
* **Combat mode on the joiner (0.30.7):** a host NPC in a fight near the
  joiner is engaged on the joiner's side (`MP-W132 engage on <npc>` / `engage
  off`; native `WO132-ENGAGE`): the game's own direction indicator and block,
  the copy still held and paused. The host watches its drawn NPCs (`MP-W132
  watching`, `npc-combat out`).
* Counters every 60 s in the agent log: `MP-WO131-STATS`, `MP-W132-STATS`.
* Two-player checklist: `docs/WO-132-findings.md`, section 7 (and WO-131's).

## Loot together (from 0.30.9, WO-134)

In plain words:

* **Things you drop for each other** work exactly as before: drop it, the other
  player sees it, whoever picks it up first has it.
* **Bodies are shared.** A body is the host's. When the joiner loots it, the
  loot screen shows what the **host's** body holds; each thing the joiner takes
  comes out of the host's body too. If the host (or anyone) took it a moment
  earlier, the joiner gets "Someone already took that." and the item goes back.
  When the host strips a body, it is stripped on the joiner's screen too.
* **Loose things lying about** (food on a table, a tool, coins on the ground)
  exist once. Whoever picks one up first has it; it is gone for both.
  Something only one game has (it fell somewhere else there) stays as it was.
  **Herbs you gather** are separate for each player.
* **Chests are per player.** You each loot your own copy (you can both take
  the same dice). It is remembered per world: after a join, what the host took
  is back in the joiner's chests, and what the joiner took stays taken for him.
  After the game's own restock period (usually 7 days) a chest refills on its
  own, as it always did.

Logs:

* joiner kcd.log: `WO134-BODY open npc=<name>` → `state ... reason=open` →
  `the loot screen opens on the host's items`; each take `WO134-BODY take`,
  then `result ... ok` or `gone`. Loose items: `WO134-ITEM ask` → `ok` /
  `gone` / `unmatched` (per machine); the host's pickups `WO134-ITEM host-gone`.
* host kcd.log: `WO134-BODY host-take npc=... -> ok|gone`, `WO134-ITEM
  host-take ... -> ok|gone|unknown`, `WO134-ITEM host-gone` (its own pickup).
* chests, both: `WO134-CHEST take|put chest=...`; after a join the joiner's
  `WO134-CHEST apply rows=N applied=... expired=... skipped=...`; agent logs
  `MP-WO134 chests: ...` (the host ledger sent / arrived / applied).
* status `mp_w134_status`; switches `mp_loot_bodies`, `mp_loot_items`,
  `mp_loot_chests` (`off` = the 0.30.7 behaviour for that part). Counters
  every 60 s: `MP-WO134-STATS`.

## The partner's avatar, knocked-out enemies, crouching, saves (from 0.40.0, WO-135)

In plain words:

* **Your saves must be from the Modding Tools build.** A character is only
  brought into a world of the same game version. If all your saves come from
  the regular game, the launcher says: "Your saves are from game version X, but
  your host's game is version Y. Start or load a game in the Modding Tools
  build and save once, then join again." Nothing is asked of the host until you
  have one. If only Bring or only Start fresh can work, only that button shows.
* **The partner's avatar is seen, never heard.** Enemies see and attack it and
  guards notice it in a fight it is in, but it never speaks, never witnesses a
  crime, never reacts, never blocks on its own: its guard and block are the
  partner's own.
* **Knocked-out enemies are shared.** One knocked out on the host's screen lies
  in the same place on the partner's; either of you can loot it; it gets up on
  both screens when the host's does. The partner's "kill" on a lying body is
  done in the host's world by the partner's avatar.
* **Crouching shows** on the other screen.
* **Outfits** reach the other screen within a few seconds, fighting or not.

Logs:

* host kcd.log / native log: `WO135-QUIET body=kcd2mp_N group=... set`,
  `WO135-DIALOG gate armed`, `WO135-DIALOG refused #n` (an avatar bark that never
  started), `WO135-STANDUP avatar=` (a knocked-down avatar got up),
  `WO135-TAKEDOWN host npc=... -> done`.
* joiner: `WO135-KO npc=... knocked out|woken`, `WO135-TAKEDOWN ask|result`.
* both: `WO135-CROUCH local crouch=1 (source: ...)`; agent `[appearance] ghost N:
  +a -r (against its real equipped set of n)`; joiner agent `MP-HENRY joiner:
  skipping N save(s) of game build ...`.
* switches: `mp_avatar_quiet 0..15` (default 15), `mp_npc_ko_sync on|off`;
  status `mp_w135_status`, counters every 60 s `MP-WO135-STATS`.

## Animals, horses, fights, torches, loading (WO-136, in the next installer)

In plain words:

* **While a world loads nothing touches NPCs.** The joiner's loading screen
  ends normally; the host's NPCs appear a couple of seconds after the world
  does.
* **The host's animals are on both screens** (wolves, wild dogs, boars), in
  their own shape. The partner does not see their bites animate yet; the bites
  still hurt.
* **The rider owns the horse.** The partner can ride the host's horse; getting
  off, it stays there on both screens.
* **Knockouts win over a fight**: an enemy knocked out mid-fight goes down on
  the partner's screen too.
* **Enemies fight the partner**: they turn to whoever keeps hitting them, and
  when the host goes down they keep fighting the partner.
* **Clothes, torches and crouching** show on the other screen.
* **"Someone already took that"** only when the other player did.

Logs:

* joiner kcd.log: `WO136-HOLD on why=load-join` … `WO136-HOLD off why=gameplay+settle
  held_s=… replayed=…`; agent `MP-W136 load hold released after … s`;
  `WO136-STANDIN soul npc=… class=Wolf`; `WO136-RIDE take|dismount|return`;
  agent `MP-W136 …: the host's NPC is knocked out -> the engagement ends FIRST`.
* host native log: `WO136-TARGET npc=… -> avatar:… taken (read back after … ms)`,
  `WO136-FORCED … set (read back)`, `WO136-HANDOVER npc=… -> avatar … taken`.
* both: `WO136-TORCH local=1|0`, `WO136-TORCH avatar id=N on|off -> …`,
  `WO136-CROUCH query armed`, `WO136-OUTFIT spawn … bare preset ok=true`,
  `WO134-BODY not-a-put` (the loot screen's own key, not sent).
* status `mp_w136_status`; counters every 60 s `MP-WO136-STATS`; live checks
  `mp_w136_check fights on|off | status`.

## Questing together (WO-137, in the next installer)

In plain words:

* **Every quest is shared** — main quests and side quests like "Find Mutt!".
  The host's world holds the story. A step either of you makes (examining,
  picking up, a conversation's outcome) updates both journals; the host's
  world decides, and a step that is already done counts once.
* **The partner can talk to people.** The person the partner talks to waits
  for the partner in the host's world meanwhile; what is decided in the
  conversation counts for both.
* **Dead stays dead** on both screens, also bodies the story placed and people
  the story kills.
* **Rewards are each player's own.** Time is the host's: a quest step on the
  partner's side never moves the clock.
* **Off switch (host):** `mp_quest_sync off` stops sharing at once;
  `mp_quest_sync on` starts it again (the partner's journal is compared and put
  right at once). A rejoin always loads the story exactly as the host has it.

Logs:

* both agents: `MP-W137 quest sync ON -- host: …` / `-- joiner: …`,
  `MP-WO137-STATS` every 60 s.
* host agent: `MP-W137 host change #…`, `MP-W137 host: request #… from ghost …:
  … APPLIED | already done … counted once | refused …`, `MP-W137 host: ghost …
  talks to … -- … HELD`.
* joiner agent: `MP-W137 joiner applied host change #…`, `MP-W137 joiner ->
  host request #…`, `MP-W137 MISMATCH …`, `checkpoint part …`.
* kcd.log: `WO137-TALK resume|start|end`, `WO137-HOLD on|off`, `WO137-DEAD …`,
  `MP-PAUSE … a-corpse-is-never-paused`; native `WO137-BUILD`, `WO137-CHANGE`,
  `WO137-APPLY`, `WO137-TIMESET`.
* status `mp_quest_status`; the partner's side: `mp_quest_talk on|off`.

## Crime together (WO-139, in the next installer)

In plain words:

* **The partner's crimes are crimes in the host's world** — stealing, picking
  locks, taking someone's horse, robbing a body, walking into someone's home.
  Only what people of the host's world see counts; unseen is no crime, as in
  the game. They are never counted against the host.
* **Guards deal with the partner:** a guard who saw it, or who was told by a
  witness (about 20 s later), comes up and arrests the partner. The arrest,
  the fine, the punishment, the talk and the fight are the game's own, on the
  partner's screen; the guard waits in the host's world meanwhile, and what is
  decided counts for both.
* **Violence:** the host's guards fight the partner for a fresh assault or
  murder, or after the partner resisted an arrest (for about 5 minutes). Once
  the partner is down, or later, the next guard arrests instead.
* **Punishment moves no clock** (jail and the stocks keep the shared time).
  **Execution** is the partner's death: they wake outside the town, near the
  host, and the crime is gone.
* **Nothing between the players is a crime**, and **you can't rob each other**
  ("You can't steal from each other in co-op.").
* **The host's horses** are free to ride for the partner.
* **Off switch (host):** `mp_crime_shared off` stops sharing crimes;
  `mp_crime_shared on` starts again.

Logs:

* host agent: `MP-W139 host: ghost …'s <crime> …: <n> witness(es), <n>
  guard(s) in <settlement>`, `… stops ghost …`, `… attacks his avatar`,
  `… stops fighting …`, `… cleared …`.
* joiner agent: `MP-W139 joiner -> host crime #…`, `MP-W139 joiner: stop #…
  by … -> <result>`, `MP-W139 joiner: the host cleared my record …`.
* both: `MP-WO139-STATS` every 60 s.
* kcd.log: `WO139-JUDGE`, `WO139-STOP start|planted|result|end`,
  `WO139-PURSUE attack`, `WO139-PLACE`, `WO139-HORSE`, `WO139-SKIPTIME`,
  `WO139-ROB`, `WO139-CRIME`; native `WO139-TRESPASS`, `WO139-PURSUE on|off`,
  `WO139-CONTEXT`, `WO139-TIMESET`.
* status `mp_crime_status`.

## Sleeping together, and the "own world" trap (WO-140, in the next installer)

In plain words:

* **Sleep together, or not at all.** Whoever picks "Sleep" at a bed (or waits)
  does not lie down yet: "Waiting for other players...". The other gets
  "<name> wants to sleep. Sleep too?" — **F11** yes, **F12** no, 30 s. On yes
  the sleeper lies down and chooses how long; the moment the length is
  confirmed the other player's game shows its own sleep screen for the same
  hours, wherever they stand. Both are rested, both clocks move together. On a
  no (or no answer): nothing happens, "Other players are not ready to sleep
  yet!".
* **One clock.** The host's clock is the world's; a partner's clock ahead of
  it is pulled back at once. A partner's bed that saves: the host makes the
  game's own rest save after the sleep.
* **The joiner waits at the main menu** (the launcher now says so). A joiner
  who loads their own save and connects is told, in the game (repeated) and in
  a launcher window, to quit and wait at the menu; until then nothing of the
  host's world is applied to theirs (no NPCs, fights, loot, quests, crime or
  leash) and the host does not leash them.
* **Off switch:** `mp_sleep_vote off` (each sleeps alone; the partner's clock
  still follows the host's).

Logs:

* agent: `MP-W140 this player wants to sleep …`, `vote 0x… : everyone said
  yes` / `no` / `timeout`, `… chose N h -- everyone's sleep starts now`,
  `this player's sleep for N h -- the game's own sleep`, `this clock was N s
  ahead of the host's -- pulled back`; the trap: `MP-JOIN joiner: connected
  from its own world -- NOT joined (separate)` (every minute, with what was
  dropped), `MP-LEASH host: joiner N is in its own world`.
* both: `MP-WO140-STATS` every 60 s.
* kcd.log: `WO140-HOLD`, `WO140-GO`, `WO140-DROP`, `WO140-PROMPT`,
  `WO140-ANSWER`, `WO140-SEPARATE`, `WO140-RESTSAVE`, `MP-MARK <word>` (the
  checklist's markers); native `WO140-HELD`, `WO140-APPROVE`, `WO140-STATE`,
  `WO140-START`, `WO140-STOP`, `WO140-PULL`.
* status `mp_sleep_status`.

## Expected not to work yet

* **No "back to the main menu"** in KCD2. If the host leaves, or a check
  fails, the joiner is told and their **own** newest save loads. With no save
  of their own at all, the game drops to the main menu with a "Game load
  failed" box: press OK. That is intended.
* **Surrendering enemies** don't kneel on the joiner's screen (WO-137); talking
  to one is an ordinary conversation there.
* **A conversation topic you used** can still be offered to the other player;
  asking again changes nothing in the quest (WO-137).
* **Cutscenes** play only for the player who triggers them (WO-137).
* **NPCs don't fight back when the joiner hits them:** they turn, but don't
  swing (WO-121).
* **NPCs stand instead of sitting** on the joiner's screen.
* **Animals' bites don't animate** on the joiner's screen (WO-136); they still
  hurt.
* **Mounting the host's horse may say "Mount and steal"** on the joiner's
  screen (WO-136, not tried with the key): say what happens.
* **The prologue and Godwin's part of the story:** no join while the host
  plays one of them ("Your host is in a part of the story where you can't join
  yet."). What to do there together is a later WO.
* Already in a world when connecting? The joiner is told to quit, restart and
  wait at the main menu.
* **The partner's crimes don't touch the host's reputation**; the partner's
  own reputation changes on their own side (WO-139).
* **A guard the partner resisted** may attack them on sight for a few minutes,
  as in the game (WO-139).
* **The host's guards never search the partner** for stolen goods (WO-139).

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
