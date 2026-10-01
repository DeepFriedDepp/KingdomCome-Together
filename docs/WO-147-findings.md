# WO-147 — The joiner can fight, and the leash pulls: findings

Kingdom Come: Together. Unofficial; not affiliated with or endorsed by Warhorse
Studios.

**Version: 0.42.5** (the maintainer's number). Every gate is green; the
installer `release\KingdomComeTogether-Setup-0.42.5.exe` is built from a fresh
clone of `origin/main` (section 6). No GitHub release. 0.42.2 stays the
fallback, bookmarked as the tag `v0.42.2` on `7bcc28a`.

Evidence marks: **(observed)** seen live in the game, in frames or in its own
log lines; **(synthetic)** the real game against a scripted partner or a
synthetic host, or a console stand-in for a player's key; **(code-verified)**
read in the code or the game's data, or pinned by a unit test, not run live;
**(inconclusive)** tried, no clear answer.

The field material: the log bundles of the first long two-player evening on
0.42.2 (the host and the joiner; the joiner played three games that evening)
and the two 0.42.0 evenings before it. Where a log disagreed with the work
order, the log won and this page says so (section 5). All live runs were solo
on one machine (the Modding Tools game, 1.5.5), on a throwaway playline of save
copies: the real game as the **host** with a scripted partner (H1) and as a
**joiner** of a synthetic host (J1–J7). No key was pressed; blows, the weapon,
the camera and the player's moves were console stand-ins (`mp_test_hit`,
`DrawWeapon`, `PlayerSetViewAngles`, `SetWorldPos`).

## The answer

1. **The joiner's blows reach the host.** In the field 34 of the joiner's blows
   landed on copies of the host's NPCs and animals; 11 reached the host. 20
   never left the joiner: a blow that cost the enemy only stamina (a block, and
   every blow after his sword broke mid-fight) was thrown away. 3 were refused
   by the hit gate: twice "not bound" — the blow before had knocked the copy
   over, and the DLL lets go of a copy lying on the ground — and once "stale"
   (a standing archer's 2 s heartbeat, read at 3.5 s). Now stamina drops are
   measured and sent, a knocked-over copy counts as the host's for 3 s, the
   gate waits 6 s, and only this player's own blows go. **(synthetic: 5 of 5
   blows through in every run from J2 on, against 1 of 3 in J1)**
2. **The joiner can fight what attacks him.** An NPC fighting the joiner's
   avatar reads *not* in combat on the host, so the joiner never got combat
   mode against it. Now an enemy's copy (the game's own public-enemy flag:
   bandits, wolves) near the joiner comes into the joiner's own combat mode
   while his weapon is out, whether or not the host fights it; friendly people
   never do. The host streams an avatar's fights, the enemy turns on the
   avatar, hitting a bandit or a wolf is no crime, and the kill is the avatar's.
   **(observed on the host with a real wolf pack and a bandit; synthetic on the
   joiner)**
3. **The leash pulls.** In the field it never brought anyone back: three
   pulls, three failures, and a fast-travel pull owed for 39 minutes. A quest
   sleep the engine *cancelled* left the host "paused" for 45 minutes; the
   joiner's agent ran up to 833 s behind; the pull targets were the host's
   height off the ground. Now a cancelled skip ends the pause, only states
   unsafe for the joiner hold a pull and none for over 60 s, leash messages
   skip the queue, and a pull with no ground beside the host lands on a spot
   the host stood on with the fall damage held until there is ground under the
   joiner. **(synthetic: the host in a menu pulled a partner from 700 m; a
   partner in a dialogue was pulled after 60 s)**
4. **A partner's step can no longer fail the host's quest.** In the fist fight
   the joiner's copy of his opponent *died* (its guard lost on a rejoin, the
   host's non-fatal blow applied raw), and the joiner's quest graph failed the
   quest in the host's world. Now the re-created copies are guarded again, the
   host's non-fatal hits stop 1 hp short, and a step that fails a quest,
   cancels an objective or marks someone dead waits up to 10 s for the host's
   world to agree, else is refused. **(synthetic)**
5. **The frame backlog is gone** (asked for during the work). Behind the host's
   NPC traffic the joiner's agent fell minutes behind, and a release/restart
   churn of the copies made the game slower still. In a live A/B with 36 NPCs
   at 10 Hz: before, the agent ran **122 s** behind, released 145 copies for
   "silence" and restarted them 164 times in two minutes; after, **0.34 s**
   behind, no churn, no lost batches, and the walkers walk as smoothly as with
   no load. **(synthetic)**

| item | status | evidence |
|---|---|---|
| 1.1 the joiner's blow is sent (stamina too) | fixed | (synthetic) J1: a stamina-only blow reached the host (`GOT NpcDamage ... hp=0.00 st=15.00`); J3–J6: blows carry stamina (`st=7.54`) |
| 1.2 a knocked-over copy still takes the next blow | fixed at the source | (observed) J1: `event=drop reason=not-living` 0.3–0.7 s after every blow, 2 of 3 refused `not-bound`; (synthetic) J2–J6: 5 of 5 through |
| 1.3 the host applies with the avatar as the attacker | works | (observed) H1: `MP-ATTRIB ... result=applied damage=1 history=1 skirmish=1 brain_msg=1` on a real wolf and on a bandit |
| 1.4 the NPC turns on the avatar | works | (observed) H1: the wolf pack `Set target: kcd2mp_1`, 10 bites on the avatar forwarded; the bandit `Set opponent: kcd2mp_1`, drew, attacked |
| 1.5 the joiner can lock on to an enemy's copy | built | (synthetic) J1–J6: `engage on` the bandit and the wolf (skirmish added), the villager "no enemy of this player"; the lock itself needs the player's key |
| 1.6 no crime on hostile NPCs | works | (observed) H1: `WO139-JUDGE ... -- not a crime (a public enemy)` for the wolf and the bandit |
| 1.7 the kill lands on both | fixed | (observed) H1: `Soul died ... killer = 'kcd2mp_1'`; (synthetic) J3, J7: `MP-OWNERDEATH ... applied=dead requests=1` on a stand-in copy, in J7 also after two host reloads (J1/J2: the death went to another soul, section 1.6; J4/J5: 6 tries over 137 s after a reload, section 3.2) |
| 2.1 the host's menu no longer holds a pull | fixed | (synthetic) H1: the host "in a menu", the partner at 700 m: warned, 10 → 1, pulled to 3 m |
| 2.2 a hold lasts at most 60 s | built | (synthetic) H1: `held (joiner-dialogue)` → 60 s → `pull #2 reason=distance,forced` → placed |
| 2.3 the stuck "paused: skip-time" | fixed at the source | (code-verified) unit tests with the field's two lines |
| 2.4 a pull with no ground beside the host | fixed | (synthetic) J1: `placed on a spot the host stood on`; J4: with the host's real height, ground found 518 m out on the first try; a spot 117 m up: fall damage held the full 60 s while the body hung there |
| 2.5 flights and teleports are not fast travels | fixed | (synthetic) J1: a 332 m teleport "not a fast travel", a 160 m/s flight "flying"; H1: the partner's 92 m/s flight read as flying |
| 2.6 20 minutes without a false jump | done | (synthetic) J1: a 20-minute soak with two refused fast travels and the host walking about: 0 jumps, 0 flights, 0 fast travels |
| 2.7 mounted | decided | (code-verified) a mounted joiner is taken off the horse cleanly; the horse stays |
| 2b.1 destructive requests wait for the host's world | built | (synthetic) H1: the field's NPC-dead request held 10 s and refused; a quest fail refused after 10.5 s; one from the joiner's own conversation applied, its repeat "already" |
| 2b.2 knockouts are not deaths on the joiner | fixed at the source | (synthetic) J1: the host's 150 hp non-fatal blow left a copy at 1 hp; J7: after each of two host reloads both re-created copies were reported and guarded again (J4/J5 lost them, section 3.2) |
| 2b.3 corrections fire a port that produces the host's value | built | (code-verified) unit tests with the field's `carryingBags` case |
| 2b.4 the fight-club sequence against the real host | done | (synthetic) H1: the field's 8 requests: 3 refused (never reached here), 4 already, the NPC-dead one held and refused |
| 3 README feature list | rewritten | an evidence level per feature, the known gaps |
| + the frame backlog | fixed | (synthetic) the A/B in section 5b |
| 4 gates | all green | section 6 |

## 1. The joiner's fight

### 1.1 Why the joiner's blows did not arrive (the field)

Joiner blows that landed on a copy, all three games of 0.42.2 and the 0.42.0
evening: **34**. Forwarded and applied by the host: **11**. Dropped by the
joiner's hit gate: **3** (`not-bound` ×2, `stale-stream` ×1). Never reached the
gate: **20** — all under 0.5 hp.

* The DLL's hit sampler measured health only and sent stamina as 0; the agent
  drops a hit worth less than half a point of both. A blocked or glancing blow
  costs the victim stamina, not health.
* The joiner's sword broke mid-fight (`WeaponDestroyed on Dude`); his 17 later
  blows did 0.0–0.19 health. The third game's 15 blows are all of this kind.
* The work order's "hits_fwd=0 for the whole session" is true for the first and
  third games only; the second forwarded 10 and the host applied all 10.

**Fix.** The sampler reads stamina for every tracked soul within 15 m of the
player and reports a drop of 1 or more; the agent's half-point rule keeps the
noise out. On a joiner a drop goes only when the hit hook marked it as this
player's blow (the host avatar's blow on a local copy passed as the joiner's
before). **(code-verified; synthetic)**

### 1.2 The gate: a knocked-over copy, a standing archer

* `not-bound`: a blow — the host's or the joiner's — can knock a copy over; its
  physics stops being a living entity and the DLL lets go of it until it is
  stood up. The joiner's gate asks a few hundred ms after the blow, so the next
  blow was refused. Live (J1) every stand-in blow was followed by `event=drop
  reason=not-living` 0.3–0.7 s later, and two of three blows were refused. The
  DLL now answers "bound" for a copy it let go of for this reason within 3 s.
  J2–J6: five of five blows through, each time. **(observed cause; synthetic)**
* `stale-stream`: the gate's freshness was 3 s; a standing NPC streams a 2 s
  heartbeat (the field's archer: 0.00 m from its sample, 3,538 ms old): 6 s now.

### 1.3 Why an NPC fighting only the joiner could not be locked on

The joiner engages a copy (a skirmish with him, combat mode held on the copy)
only from the host's `NpcCombat` events, and only while the host's NPC is in
combat. An NPC fighting the avatar reads combat 0 on the host (every field
event with `target=Avatar` had `combat=0`), and the host sent those events only
for NPCs with a drawn weapon — so the cuman who hit the joiner five times was
never engaged.

* **The host** watches an NPC the avatar hits, and one that hits the avatar, for
  30 s; its state goes out every second (the DLL's heartbeat covers an NPC with
  any opponent now). Never a dead NPC. **(observed: H1, 60 events for two
  wolves in 30 s)**
* **The joiner** engages a copy whose host NPC fights *his* avatar.
* **Hostile copies**: once a second the joiner lists the host's copies within
  12 m — the game's own `soul:IsPublicEnemy()` (bandits, wolves; the crime
  judge's own test), an encounter animal, or `soul:GetRelationship(player)` at
  or under −0.1 — and engages an enemy while his weapon is out (kept to 15 m,
  released 5 s after the weapon goes away, at once when it dies or goes down,
  never while the host's stream calls it dead). Live values: a villager 0.21,
  the partner's avatar 0.5, a bandit **0** (the relationship alone would have
  missed bandits); wolves and bandits `IsPublicEnemy() = true`.
  `mp_hostile_engage off` turns it off.
* **Barks are no conversation.** The talk hold (a joiner talking to a copy holds
  the host's NPC) fired on combat shouts: the host froze a bandit five times in
  its last 28 s. An attempt whose meta overrides are all combat shouts
  (`COMBAT_*`, `SKIRMISH_*`) or the player's own barks (`HRAC_*`) is no
  conversation now — 1,707 such attempts in the field's logs opened the
  dialogue camera twice; the dice player's `KOSTKAR_UNISEX` opened it 31 times
  of 31 and stays a conversation. The host's "paused: dialogue" now needs the
  dialogue camera too.

### 1.4 The host side, live (H1)

A real wolf pack of this save and a bandit-soul NPC spawned beside the host:
the partner's blows on a wolf were applied with the avatar as the attacker; a
second wolf attacked the avatar, was watched in its turn and its bites were
forwarded; the bandit turned on the avatar, drew its weapon and attacked; the
crime judge said `not a crime (a public enemy)` for both; a 90 hp blow killed
the bandit, credited to the avatar. Spawned human souls *with a faction* fight
(WO-132's "spawned souls never fight" held for homeless autotest souls).
**(observed)** Frame: `docs/wo147-shots/1-host-partner-after-the-bandit-kill.jpg`.

### 1.5 The joiner side, live (J1–J6)

The synthetic host streamed a bandit (a stand-in with the host's soul), a wolf
(an animal stand-in) and a world villager beside the joiner. All were guarded.
The hostile list: `prepadeni_bandit_1:-1.00:6.8:1;dummyWanderer_mercenary_3:0.21:5.0:1;wolf_w147_1:-1.00:7.5:1`;
with the weapon drawn: `engage on` the bandit and the wolf, the villager "no
enemy of this player". Frame: `docs/wo147-shots/2-joiner-bandit-copy-engaged.jpg`
(the bandit copy; the game's enemy marker above the compass). The lock itself
(`IsLockedToOpponent`) needs the player's own key and stayed false. The host's
150 hp non-fatal blow on the wolf copy: `ApplyDamage non-lethal: 99.00 of
150.00 applied -- the host decides deaths`, 1 hp left. **(synthetic)**

### 1.6 Found on the way: the host's death went to another soul

J1 and J2: the host's death of the bandit stand-in never stuck — the owner
death was applied five times, the copy stood at 70 hp. The agent finds a copy's
soul by its *name*; the game answers a by-name lookup with any soul of that
name, here the game's own unplaced soul of the encounter bandit (`Position
"0,0,0"`, `IsDead="true"` after the five applies). A copy's soul is now taken
from its body (a new DLL op: the soul guid of an entity id), the name only as
the fallback. J3 and J7: `applied=dead requests=1`. After a host reload J4 and
J5 still needed 6 tries over 137 s: the agent's body-to-soul cache outlived the
load (section 3.2; fixed, J7: at once). The same lookup served the host's damage
on a copy (WO-131's health follow, the inbound hits), so those reach the right
soul now too. **(observed cause; synthetic fix)**

## 2. The leash

### 2.1 What held it in the field

* **The host's "menu" was a cancelled sleep.** The log tail ended a skip only on
  `is ready`; a quest sleep the engine cancelled (`has canceled async waiting`,
  then the quest's cutscene) left it set for **45 min 37 s**: the host read
  "paused: skip-time", told the joiner so, and every hold line carried
  `host-menu` (211 of them). A cancel ends the skip now, and a skip open
  3 minutes is cleared. The menu pump also stopped for good on the first
  console timeout in that hold; only its own cancel stops it now.
* **A fast-travel pull owed 39 minutes** — a quest cutscene's teleport of *both*
  players (both landed on the same point), never a fast travel. An owed pull
  lapses after 2 minutes now.
* **Late messages.** A countdown arrived 20 s late; pull #1's "busy" answer came
  after the host's 12 s timeout and counted as a failure. Leash frames have
  their own lane from the socket reader now, and a pull waits three round trips
  plus 2 s (at least 12 s, at most 90 s) or twice the partner's lag plus 4 s.
* **No ground beside the host.** Pulls #2 and #3 found no navmesh beside the
  host's reported spot, 1–2 km out. Live (J4), with the host's real height,
  ground was found 518 m out on the first try; the field's host was mid-run at
  33 m/s (#2) and 7–8 m above its grave (#3) — its *height* was off the ground,
  most likely the cause (**inconclusive** for the field itself). Now: no ground
  → the joiner is placed exactly on a spot the host stood on in the last 30 s
  (1.5–6 m from it, within 3 m of its height), else 1 m toward the joiner; the
  fall damage is held until there is ground under the joiner (the navmesh
  within 2 m, not dropping; at most 60 s); the agent places him beside the host
  once the area has loaded (retried for about 25 s); three failures pause the
  pulls for 3 minutes, no longer for the session.
* **The flights.** The joiner's own character moved at 30–640 m/s through
  terrain and water, steered, stopping when the map opened — the Modding Tools'
  free flight most likely (**inconclusive**: nothing in the logs names it). The
  jump check called it "a fast travel the block missed" and told the host
  "tried to fast travel". Now a jump (≥ 200 m in one step) and a flight
  (≥ 40 m/s for half a second, 1.5× mounted) are told apart from a fast travel,
  which only the engine's own `FastTravel: started` line reports; the host
  judges positions on the sender's clock and uses only current ones.

### 2.2 The rules now

Only what makes a pull unsafe holds it: the joiner loading, in a dialogue or a
cutscene, downed or respawning; the host loading, reloading or
fast-travelling; no current joiner position. The host's menu, dialogue,
cutscene and down no longer hold, nor the joiner's menu. A hold lasts at most
`mp_leash_cap_s` (60 s; 0 = no limit), then the pull is forced (the joiner's
conversation ended first); a load is never capped. Mounted: the joiner is
taken off the horse cleanly and pulled; the horse stays.

### 2.3 Live

* **H1:** the host "in a menu" (the manual pause switch, the inventory's hold),
  the partner at 700 m: `past warn=600 m`, countdown 10 → 1, `pull #1 ...
  placed (d now 3 m)`. The partner "in a dialogue": `held (joiner-dialogue)`,
  60 s, `the hold is over`, `pull #2 reason=distance,forced`, placed. The
  partner at 92 m/s: `flying`. **(synthetic)**
* **J1–J4:** a pull with no ground beside the host: `placed on a spot the host
  stood on`. With my harness's made-up host height (55 m, later 117 m above the
  ground) the joiner died of the fall after the 6 s hold (J1, J2), and then hung
  in the air where the engine held the body (J3, J4): the hold now lasts until
  there is ground under the joiner (J4: held the full 60 s while hanging). With
  the host's real height (J4) the ground beside the host was found 518 m out.
  A 332 m teleport: "a teleport ... not a fast travel"; a 100 m/s scripted
  flight: `flying (160 m/s ...)` and the flag at the host; the refused fast
  travel: the `fast-travel-refused` flag. A 20-minute soak (two refused fast
  travels, the host moving): 0 false jumps, 0 flights, 0 fast travels.

## 3. Quests: the host's world decides

### 3.1 The fist fight (the field)

The copy guard was lost on the joiner's rejoin (the load re-created the NPC;
the agent re-guards only on a new name or id, and the mod reported no new
body). The host's own non-fatal 9.35 hp blow reached the copy as damage right
after the health follow set it to the host's 4.8: `Soul died`. The quest's
`important_npc_death_objective` fired on the joiner: `npcIsDead SetNpcIsDead
0->1`, `defeatOpponent_objective SetNone 1->0`, `questProgress SetFailed 1->3` —
applied on the host, whose quest failed, while its NPC stood at 4.8 hp.

### 3.2 What changed

* **Guards after a load:** every load forgets the copy guards and the souls read
  from the copies' bodies; the mod reports every puppet's body when a load hold
  ends (`WO147-REANNOUNCE`), a copy with no body yet when its body comes —
  *whatever its entity id* — and any other new body of a live puppet
  (`WO147-NEWBODY`); the agent guards them and reads their souls anew.
  Found in the logs while writing this page: in J4 and J5 the stand-ins came
  back after the reload with their **old entity ids**, so the id compare saw
  nothing new: the wolf stayed unguarded, and the host's death of the bandit
  went to the dead old soul the agent had cached for that id — five failed
  tries until the stand-in was removed and spawned afresh, 137 s. J6: a new id,
  reported and guarded. J7, after the fix, two reloads: both copies reported
  (`WO147-NEWBODY npc=prepadeni_bandit_1 id ? -> ...80542`), guarded, and the
  bandit's death at once (`applied=dead requests=1 after_s=0.3`, then 0.5).
  Both J7 reloads happened to give new ids; the same-id case is pinned by the
  Lua suite (`Test-WO147Synthetic`, scenario g). **(synthetic)**
* **Non-lethal:** on a joiner the host's non-fatal hit never takes a copy under
  1 hp (a flag on the DLL's `ApplyDamage`). **(synthetic: J1)**
* **Destructive requests**, from the game's own quest data (2,776 State types
  read from `Scripts.pak` at start, about a second): a value whose objective
  type is Canceled (a quest's Failed is one); an objective taken back from a
  running value to its first value or one named for it (None, Aborted,
  Canceled, Failed, Reset…; not the silent completions such as `SetDone`); a
  value named for a death or a knockout (English and Czech; the 36 that only
  speak of one — `PlayerFoundDeadBody`, `NobodyDead`, `ZvedniMrtvoluStart` —
  are left out). The host holds such a request up to 10 s for its own world;
  applied if it got there or the step came out of the joiner's own
  conversation, else refused and the joiner's copy put back. `mp_quest_safety
  off` turns it off.
* **Corrections** fire "Set" + the name of the host's value from the quest data
  (the field's `carryingBags SetCart 4->2 (the host had 3)` would fire
  `SetBarn`), else a port this machine saw produce exactly that value, else
  none; each is re-checked just before it fires.

### 3.3 Live (H1)

The field's three destructive requests from the scripted partner:
`SetNpcIsDead 0->1`: `DESTRUCTIVE ... held until this world agrees (up to
10 s)`, then `REFUSED -- this world is still at 0 after 10.0 s`. A quest active
on this save failed by request: `REFUSED ... after 10.5 s`. The same kind of
step from the partner's own conversation: `its outcome, applied`, `APPLIED to
the host's world (0->3)`; its repeat: `already done in the host's world (host
value 3)`. The whole fight-club sequence (8 requests): 3 refused (never reached
in this world), 4 already, the NPC-dead request held and refused.

## 4. The README

The feature list is rewritten with one evidence level per feature — proven
with two players (from the field logs: joining, seeing each other, clothes,
riding, fighting together, enemies hurting the partner, animals' bites, dying,
the host's people, one clock, weather, sleeping together, crime, looting,
dropped items, fast travel host-only, shared quests, talking, the launcher and
the installer), works with a scripted partner, or not yet proven (voice chat:
it starts every session, nobody has confirmed hearing the other) — and the
known gaps (carrying bodies and objects, where the host's animals are, shared
cutscenes, escorts). The old detail table is `docs/FEATURE-HISTORY.md`.

## 5. Where the logs disagreed with the work order

* "hits_fwd=0 hits_drop=0 for the whole session": the joiner's second game
  forwarded 10 hits and the host applied them all.
* "The host sat in menus": the host's menu hold was the stuck skip-time flag,
  45 minutes of it.
* "25 requests, 16 applied, 8 already": 15 applied, 10 already.
* The copies' script contexts: every `SCTX` line on the joiner is on the host's
  avatar, none on a copy.
* The host's 846 m jump "a fast travel": a quest cutscene's teleport of both.

## 5b. The frame backlog

**Why the joiner's agent fell minutes behind.** Its reader keeps up; the one
processor behind it does not under the host's NPC traffic (up to 600 samples a
second in a town of 74–94 NPCs). Each NPC sample becomes a Lua statement; a
batch that fills waits on the game's `ExecuteString`, which slows with the
game's main-thread load (0.8 s times out a whole batch: `MP-BATCH-DROP`).
Behind by more than 3 s, the mod saw its puppets' pushes stop and released them
for "silence" (`wh_ai_ResumeNPC`); the next late push started each one again
(`wh_ai_PauseNPC`, two stance resets): 30,108 console commands in 16 minutes,
kcd.log at 10–13k lines a minute — slower still: a loop. A main-thread freeze
did the same at once, in the mod and in the DLL's writer.

**The fix (`mp_npc_catchup`, on).**
* **Behind (250 ms or more), a superseded sample is skipped**: one a newer
  sample of the same NPC with the same flags supersedes — never a death, a
  knockout, a weapon drawn, a swing cue or a resync row — but every NPC still
  gets a sample through every 300 ms (under a sustained lag *every* sample is
  superseded by the time it is taken: J5 starved walkers for up to 38 s). An
  avatar's sample without a state block is not pushed to Lua either.
* **The agent says when a stream is silent**: it sees samples arrive, gives the
  mod its word once a second (fresh for 10 s), and names a stream silent when
  nothing came for it for 3 s and the mod has its last sample. While the word
  is fresh the mod releases only then (or after 30 s of quiet); otherwise its
  own 3 s rule decides, as before.
* **A stall is no silence**: a stretch over 6 s the puppet tick did not run
  moves every puppet's packet clock on by it; in the DLL's writer any stall over
  1 s does.
* **The renderer stays behind by the agent's lag**: the samples come that late
  (and sparser), so the mod renders that much further back (the lag of the last
  second plus 0.25 s, up at once, down 0.5 s a second, at most 3 s) and treats
  a sample that late as not yet a stop. 0 when nothing lags.

**The A/B (synthetic):** the joiner, a synthetic host streaming 36 real NPCs of
this world at 10 Hz each, walking, for 120 s; the DLL's writer off (every
sample through Lua: the field's state after its silence drops).

| | fix off (J2) | fix on, first version (J2) | fix on, final (J6) | 8 NPCs, no lag (J4) |
|---|---|---|---|---|
| agent behind, worst ping | **121,954 ms** (7 pings handled in 2 min) | 1,030 ms (69) | **336 ms** (71) | 321 ms |
| copies released for "silence" | 145 | 36 (the previous streams' end) | 36 (its own streams' end) | 8 |
| copies started | 164 | 142 | 36 | 8 |
| pause/resume/reset commands | 809 | 942 | 230 | 249 |
| batches lost (`MP-BATCH-DROP`) | 6 | 15 | **0** | 0 |
| walkers' animation switches (sprint/run/walk/idle) | 0/0/3/3 (frozen) | 281/103/24/347 | **0/0/0/0** | 0/0/0/0 |

Between the first and the final version: the render lag (J3: sprint 81), the
lag's spike reaction (J4: sprint 0, walk/idle 369/371), and the 300 ms
guarantee (J6: none). **(synthetic; code-verified: unit tests and the Lua
suite's scenarios)**

**Found on the way:** the puppet chain's restart gate declared the item-sync
chain dead once under the load (`confirmed dead ... no heartbeat for 1.1s`),
which re-paused every puppet once. Existing WO-78 behaviour; noted, not changed.

## 6. Gates

On the final tree (VERSION 0.42.5): relay round trip 59/59; agent unit tests
729/729 (637 in 0.42.2); every `Test-*Synthetic.ps1` (40 suites,
`Test-WO147Synthetic` 59/59, `Test-WO102Synthetic` 196/196,
`Test-WO137Synthetic` 89/89); both static checks (7/7, 6/6); native unit tests
298/298; the local publish; the payload smoke — 47 gates, all green.
The installer build ran every gate again inside a fresh clone of
`origin/main`; `docs/WO-147-progress.md` ("Gates and the build") names the
commit and the payload sweep.

## 7. Decisions made unattended

Listed in `docs/WO-147-progress.md` ("Decisions made unattended").

## 8. Not done, and what to watch

* **Lock-on by the player's key**, the joiner's real swing on an engaged copy,
  and a fist fight's knockout on both screens need two players.
* A copy re-created by a load **with its old entity id**: pinned by the Lua
  suite; live, both J7 reloads gave new ids.
* With a host flying high (the free flight), a pulled joiner hangs or falls:
  the fall is held up to 60 s; a longer hang then a fall would hurt.
* A joiner who left the host's world once loaded his own old autosave and hung
  in the air at 1,027 m until the game closed — not reproduced.
* The Modding Tools' free flight is a guess (no log names it).
