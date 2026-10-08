# WO-161 — Shared combat: the design call, the verdict path, the repo cleanup and the README (0.46.5)

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

Marks: **[L]** live, solo, on this machine's Modding Tools build (1.5.5), a throwaway playline only (every playline backed
up first, checked after); **[L-field]** read in the testers' logs of 2026-10-07 (0.45.8, both machines; matched by event,
the joiner's clock is 4.6 s ahead of the host's, which the agent log's `off=` states); **[code]** read in our code;
**[unit]** / **[syn]** / **[native]** a test that gates the build; **[not tested]** no run. No names, paths or addresses of
players are in this file, and no tester log line is copied: the numbers were counted from the logs. Entity names such as
`ttkc_man_23` are the game's own. The version is the maintainer's (0.46.5); no tag, nothing uploaded.

This file is written phase by phase: a section is appended when its phase ends, so a cut-off session still leaves a
complete record.

## A. Setup record

* Start: `main` at `444caac` (WO-160's second 0.46.0 build record), VERSION 0.46.0, working tree clean apart from the
  maintainer's untracked tester bundles under `logs/` (never committed; see the cleanup list).
* Saves: every playline backed up wholesale before any launch (412 files, 689 MB, a timestamped copy beside the saves
  folder, file count checked equal to the original). No game was launched in the discovery phase.
* Checklist numbering: WO-160 ended at item 161; this WO's items start at **162**.

### A. Where combat lives (the file:line list the other sections cite)

| What | Where | Mark |
|---|---|---|
| The hit chokepoint: `C_CombatSoul` vtable slot 0x150 (melee), 0x158 (missile); an avatar's hit is measured and put back; an NPC's hit on an avatar is measured over a window and handed to the agent (pipe 0x9A); an engaged copy's local hit on the player is put back and counted (0x9C) | `native/KCDMP/hits.cpp:34` (slots), `:115-210` (watch kinds), `:558` (`mark_player_hit`), `:600-625` (`WO132-HITS` lines); `hits.h` header | [code] |
| Host: an NPC's hit on an avatar → `PlayerHitUp 0x21` (victim ghost, hp, st) → joiner `PlayerHitDown 0x22` → `TakeDamage` 4-argument, no knockdown | `GameBridge.Wo132.cs:124-149` (`OnNpcAvatarHitAsync`), `GameBridge.cs:4329-4357` (send), `:4380-4400` (apply), `:4975-4983` (receive) | [code] |
| An NPC's swing: the DLL's attack capture → `ActionUp`/`ActionDown` kind `NpcAttack` (row GUID + NPC name) → the copy plays the row natively (`dispatch=native-row`) | `GameBridge.Wo121.cs:247-300` (out), `:411-436` (in; `_npcRowAt[name]` = last row received) | [code] |
| A hit on an NPC from a partner: `LocalHit` → `NpcDamageUp 0x30` (name-addressed, flag ATTRIBUTED) → authority applies `TakeDamage(attacker avatar)` + a skirmish add | `GameBridge.cs:4867-4934` (receive), `hits.h` (attribution header) | [code] |
| An engaged copy on the joiner (combat mode, skirmish vs the player, host combat state held, local hits discarded) | `GameBridge.Wo132.cs:20-48, 287`, `docs/WO-132-findings.md` §3 | [code] |
| The 0.45.0 "host turns an enemy": `mp_host_target` — a host blow clears the mod's forced target on an NPC fighting an avatar (`WO136-FORCED … cleared (read back) -- the host struck it`, `WO139-PURSUE host-struck`), and the host's blows count as threat on it | `native/KCDMP/wo136.cpp:262-285`, `wo139.cpp:185-200`, `hits.cpp:645`, `GameBridge.Wo154.cs:80-140` | [code] |
| The retarget rule: an NPC switches to the source whose recent threat (6 s) clearly beats its opponent's, at most every 3 s | `wo136.cpp` `decide()`, `wo136.h` header | [code] |
| The writer: ring of the sender-stamped stream, blend-in at every resume, a flat 900 ms hold per swing | `native/KCDMP/npc_drive.cpp:70-110` (constants), `GameBridge.cs:5385-5410` (the 900 ms), `GameBridge.Wo121.cs:432` (copy rows), `kdcmp.lua:12145-12150, 12887-12897` (one-shot hold) | [code] |
| The avatar's body state (combat mode, block, crouch, gait) applied on arrival | `native/KCDMP/motion.cpp:705-830` (`WO121-MOTION body=… combat mode start #N / block / released`) | [code] |
| The snap counters: Lua `TELEPORT id=… dist=…` (a > 5 m step) and `MP-GHOSTCORR … snaps= snaps_figure= snaps_session=` per 10 s window | `kdcmp.lua:12713-12722, 12795-12809` (0.45.2) | [code] |
| The wire table of the recent additions: `JoinWire` (up/down pairs, length bounds, who may send) — the relay's gate reads only it | `dotnet/KcdMp.Protocol/ProtocolWo123.cs:62-110`, relay `ClientSession.cs:756-778`, `TcpBroadcastService.cs:295-320` | [code] |
| Next free type pair | `0x72/0x73` (WO-148's carry was `0x70/0x71`); protocol version stays 10 (release gate refuses mixed builds, WO-110/118) | [code] |

## Phase 0 — discovery (static, plus what the testers' logs hold)

### 0.1 How a fight works today, machine by machine

**Who owns an enemy's brain.** The **host** owns every NPC's brain — the enemy is its own real entity there, with its own
planner, its skirmish and its own target. On the **joiner** an enemy is a **paused copy** (`wh_ai_PauseNPC`), written every
frame by the DLL from the host's 2 s snapshot plus a 50–100 ms stream; its brain never runs (WO-107/109/131). [code]
Where the host's NPC fights the joiner's avatar, the joiner's copy is **engaged** (WO-132, extended by WO-147): the combat
actor joins a skirmish against the joiner's player and holds the host NPC's combat state (guard zone, attack zone, block),
so the game's own combat mode — the directional indicator, block — comes up against it; its brain stays paused. [code][L-field]
(`WO132-ENGAGE … its local hits on the player are discarded`, joiner native log, 6 times in the one session.)

**How a swing travels.** Not as a one-shot clip and not name-addressed: the **host's DLL captures each attack the NPC
commits** (`attack_capture`, the attack row's GUID from the game's own table) and the agent sends it as an `NpcAttack`
action (`ActionUp 0x3B` → `ActionDown 0x3C`, payload: sender ms, row GUID, NPC name). The joiner's agent looks the row up
in the row catalog and the DLL plays that exact row on the copy (`dispatch=native-row result=ok`); `_npcRowAt[name]` records
that a row came. The old Lua one-shot swing cue (0x2C/0x2D `CombatEvent`) is the fallback for avatars. In the field the row
reached the joiner **65–126 ms** after the host committed it and the host's damage landed **about 0.3 s after the swing's
start**. [code][L-field] A swing's *damage* travels separately (below); nothing ties a swing to the hit it causes.

**Who decides a hit on a player.** Today **the host's engine** does, against the avatar's body on the host: that body
replays the joiner's guard state a network trip late. The DLL measures the NPC's hit at the chokepoint (health and stamina
before/after, 20-frame window), puts the health back and hands the numbers to the agent (`MP-W132 npc hit on avatar …
measured hp -X st -Y`), which sends them as `PlayerHitUp 0x21`; the joiner's agent applies exactly that with a
four-argument `TakeDamage` that suppresses the hit reaction (WO-155: a blow never knocks a player down). The joiner's own
block or parry is **never consulted**; what a block does is the replayed guard on the host's avatar body (`hp -0.0 st -24`
= absorbed, 14 of 35 blows in the one session). The hit carries **no attacker identity and no swing id**. [code][L-field]
A *friendly-fire* hit (a player's blow on a partner) is the other path (`0x44/0x45`, the victim applies and decides its
own knockdown) and is not touched here. A **hit on an enemy** from either player is name-addressed damage (`0x30/0x31`),
flagged ATTRIBUTED when the sender's own player dealt it: the authority applies it with the avatar as the attacker and
adds a skirmish. [code]

**What the 0.45.0 "turn an enemy fighting your partner" logic does, and why the host still cannot target a guard beating
the avatar.** `mp_host_target` (default on) does two things, both on the **host's NPC**: a host blow on an NPC that the mod
holds on an avatar with the game's `combat_forcedTarget` (WO-136's lever; WO-139's pursuit of a guard onto the joiner's
avatar) **clears that forced target** (`WO136-FORCED … cleared (read back) -- the host struck it`, `WO139-PURSUE host-struck`),
and the host's blows **count as threat** on an NPC that fights an avatar even before the avatar touched it; the game's own
rules and the threat rule (switch at most every 3 s, only when the other source clearly beats the current one) then pick
whom it fights. It never makes the guard **lockable**, and it only runs *after* a blow of the host has landed. In the field
it never ran: `host_struck=0 host_counted=0` in all 54 + 24 minute-windows of the two sessions that had guards on the
avatar, and the only `WO136-FORCED` lines are the mod's own pursuit setting the forced target *on the avatar* (guards
`ttkc_man_1/23/3`, 3 pursuits, each ended by the avatar's death or `the avatar left its fight`). [L-field] Why the host
cannot target one: the guard's skirmish pair and its forced target are guard↔avatar only; Henry is not in that skirmish and
a guard is lawful to him, so the combat module offers him nothing to lock; a free blow at a guard is also an assault in the
host's own world (WO-154's `mp_fair_crime` judges it 5 s later, and only does not count it if the guard fights by then).
The lock-on candidate rule itself was **not read in the engine** — this is the code-level reading plus the counters,
**unverified**. [code][L-field]

### 0.2 The three snapping causes of the reference study, in our code

The study (private, reference only; techniques, no code) names three mechanisms behind the remaining snaps and jitter in
fights; each is in our code, found by its mechanism:

| # | Cause | Where | The log line that shows it | One-line fix idea |
|---|---|---|---|---|
| A | **Discrete state is applied the moment it arrives, not when the drawn body reaches it.** The state block (combat mode, guard, block, crouch, gait) is stored once per stream beside the ring, not in it; the body is drawn a delay (~120 ms plus a slewed allowance) behind the newest sample, so the guard or combat mode comes up before the body is where the stream put it | `motion.cpp:737-830` (applier), the ring in `npc_drive.cpp:70-110` | `WO121-MOTION body=… combat mode start #N / block=1 / released` against the next `MP-GHOSTCORR … corr_max_m` | put the state block on the ring sample and release it at the sample's stamp |
| B | **A swing plays on arrival.** The row is dispatched as it is read; avatar rows go through an inbox that waits only for the body to exist; none waits for the drawn pose | `GameBridge.cs:5385-5410` (avatar), `GameBridge.Wo121.cs:411-436` (NPC rows) | `MP-SWING hop=recv … hop=queued waited_ms=0`, `MP-ACTION section=inbound kind=NpcAttack … dispatch=native-row` | stamp rows with the sender clock (it exists, WO-98) and release at the pose clock, capped at 400 ms |
| C | **A flat 900 ms writer hold per swing**, then a blend-in. The stream keeps moving (fast footwork) while the body is held; at the end the blend closes up to 134 cm | `GameBridge.cs:5404` and `GameBridge.Wo121.cs:432` (`NpcHoldAsync(…, 900)`), `kdcmp.lua:12148, 12887-12897`, blend constants `npc_drive.cpp:95-110` | `npc_native_hold <entity> <ms>` (the Lua emit event), then the largest render step in the second after it (`tools/wo118/fight118.py` reports it) | end the hold when the swing's action ends, cap at 900 ms |

All three are real, none is a third invention. **What the 0.45.2 counter measures, and what it does not:** `TELEPORT` /
`MP-GHOSTCORR snaps=` count a step of **more than 5 m**. In the testers' fight sessions every cluster of those was a
fast travel, a load, a gallop or a respawn (hundreds of metres, or 48 steps in 3.7 s at ~30 m/s), none a fight; the
fight-time corrections are **below 5 m**: the correction maximum per 10 s window had a median of 0.29–0.40 m and a 90th
percentile of 0.68 m (host's view of the partner), 1.18 m (joiner's view) and 1.57 m (a session with a long fight), with
2 / 7 / 11 windows over 3 m. [L-field] So a fight "snap counter" that moves has to count the sub-5 m steps as well
(`WO161-SNAP` below); the old counter would read zero before and after.

### 0.3 The item 9 set against the WO-160 wake fix

The WO-160 wake fix acts on a **joiner's copy** that the planner left in the night's undress. Per symptom:

| # | Symptom | Verdict | The field line that decides it | Mark |
|---|---|---|---|---|
| 9.1 | A sleeping NPC asleep for the host, not for the joiner | **separate** (Part B / WO-160 territory: the copy is not in step with the host's sleep) — a copy standing where the host's NPC lies is the *opposite* fault to a dressed-wrong copy; the lying/sleeping placement is the WO-141 placement and its refusals. Recorded, not fixed (the WO's own rule). Eye-test in the checklist | the joiner's `[NPCStateSearch]` pairs show copies `Stance: lying … ChangeEquipment … sleepUnequip` against a day demand, i.e. the planner declined; no sleeping/awake row mismatch was timed | [L-field] |
| 9.2 | The NPC attacking the joiner "stood as if holding a weapon, no weapon visible" **on the host** | **separate**: on the host there is no copy, no placement and no wake fix; our code never draws or attaches a weapon on a real NPC (the draw/attach on a body is the copy's, WO-45/47). The only host-side lever on that NPC is the attributed hit (a skirmish add) that puts a real NPC into combat; a real NPC whose weapon was stashed by its own sleep undress then shows a combat stance without a sword — the game's behaviour, **unverified**. Not fixed here; eye-test added | the host's `MP-W132 npc hit on avatar` lines name the NPCs (guards, a commoner, a bandit with short swords); the one commoner of the 18:01 fight carried short swords (row spec `r_shortSwords`) and swung rows normally — the odd NPC was not identified by any marker | [L-field] |
| 9.3 | A swing on the host's screen but "no combat received" on the joiner | **separate**, two causes found. (i) **The first blow comes before the joiner's combat mode**: the engagement is armed by the first hit (`Wo147WatchFight(…, "it hit avatar")`), so combat mode comes up 37 ms after the first damage (`[playerhit]` 16:43:51.233, `MP-W132 engage on` 16:43:51.270). (ii) **Swings the host never captured**: see 9.4 | the joiner's agent log, the 17:43–17:44 guard fight (host clock) | [L-field] |
| 9.4 | **Silent damage** on the joiner | **separate, and the one Phase 1 fixes the bookkeeping of**: of the host's **45** NPC hits on avatars in the two sessions with fights, **16 had no captured swing row in the 1.2 s before them** (26 % of 35 in the first; 7 of 10 in the second — six of those a cat's bites at 3 s intervals, which are animal bites without a row). The joiner applied every one of them (`[playerhit] took … from an NPC in the authority's world`), so the damage was real and the swing was not shown | host `OnNpcAvatarHitAsync` lines matched to the host's outbound `NpcAttack` rows per NPC; the 18:01 fight: three hits, rows for the first two (shown 0.07–0.13 s after the host sent them, 0.3 s before the damage), none for the third | [L-field] |

None of the four is "the same fault as WO-160 §1.8". Per the default for what is not the same: **separate; not fixed in
the WO-160 sense; an eye-test is added; 9.4 is fixed by Phase 1's rule** (every damage line has a matching shown swing or
a logged reason, and never a dropped hit).

### 0.4 The design call

**Can a joiner's enemy copy be hit-tested?** Whether a swing of a puppet produces a **hit event on the joiner's player**
(the chokepoint's `WO132-HITS local hit on the player by engaged copy … discarded` line, counter `discarded=` in
`MP-W132-STATS`): in the 161 `MP-W132-STATS` windows of the testers' joiner bundles the counter is **0 in every one**,
including the minute-windows in which an engaged copy (`WO132-ENGAGE … on`, 0.8–1.8 m from the player, the host's swing
rows playing on it, `combat_in=67`) stood in front of the player while the host registered 35 hits on the avatar from the
same NPCs. A suspended copy "never attacks" (WO-132 §2.4 and §3.3, observed then too, inconclusive then). **No hit event
fires on a puppet.** The victim's own engine therefore cannot run its hit test (block, parry, armour, stamina) on the
joiner's player from the copy's swing. [L-field][code] (A solo run with a scripted host was not made: the evidence is the
counter across every field window, and a puppet that fires no event in 161 windows with a real fight on it is the answer;
the live check is the first line of the next WO.)

| | **Default (the WO's)**: host owns the brain, joiner's copy is a puppet, **victim decides** | **Alternative**: each machine's own brain for the enemy |
|---|---|---|
| Enemy brain | host's own; joiner: paused copy | each machine's own, two enemies per fight |
| Enemy swing shown | host: its own; joiner: the host's row played natively (exists) | each machine's own brain's swing |
| Hit on a player | **the victim's engine hit-tests the puppet's swing against its own player** — *needs the puppet to fire a hit event: it does not* | each machine decides for its own player (the 16× "copies fight back by their own brain" of WO-157's pocket) |
| Hit on the enemy | name-addressed, attributed, applied in the host's world (exists) | each machine damages its own copy: two healths, merge needed |
| Target lock | skirmish membership of either player (host side: needs a skirmish add of the host's Henry, override 0) | each machine's own lock |
| Retarget | the host's brain only | each machine's own, which diverge |

**Call: "puppet unworkable"** for the default's one load-bearing step. The rest of the default (the host's world owns every
enemy's brain; the joiner's copy plays the host's swing rows; a blow on an enemy is attributed and applied in the host's
world) is what ships today and stays. **The alternative lost, and still loses:** two brains per enemy mean two healths, two
deaths, two crime/quest histories and two loot piles for one enemy — the exact thing WO-133 (one world) and WO-51/52/60
(claims never fire in combat; the engine's own netcode is unusable here) closed; and 16 "copies fight back by their own
brain" lines in the WO-157 pocket are the symptom, not a feature.

**Two further options were considered and are recorded as not built:** (1) *the victim's machine models the defence from its
own readable state* (block mode, guard stance against the attack zone — the readings exist since WO-100) — it would be our
own re-implementation of the engine's block rule, not "the game's own hit test", and would put a second, approximate judge
beside the engine's; (2) *the victim's engine replays the hit* by calling the chokepoint's slot 0x150 itself with a built
`S_CombatHitData` (attacker = the copy's combat soul, victim = the local player) so the real block/parry/armour decide —
the study's technique for arrows. Its gates are the ones the study lists (does a built hit pass the engine's own guard
test, does the verdict follow the player's own block state, no double damage under both paths) and the standing rule is
that an empty or wrong cause crashed this very slot once (`hits.h`, WO-121 session 1). It needs its own native probe WO.

**What this WO therefore builds** (the WO's own branch for this case): Phase 1's **verdict path only** — the swing ledger,
the verdict message in the hit family, exactly-once application, the `WO161-HIT` line and its counters — **Phase 4**, the
**cleanup**, the **README and LAUNCHING rewrite**, the checklist and the build. **Phases 2 and 3 are not built**: the
puppet's parts of them are void with the puppet, the host-side targeting rests on an unread engine rule and cannot be
produced solo (an NPC that fights an avatar on demand is not available, WO-155 and WO-132 §2.4), and the snapping fixes are
core writer changes whose only keeping rule is a measurement in a scripted-partner fight (the harness needs the game's
window in front; this session never focuses it). They are written down as designs with their measurements in the pocket
list.

## Phase 1 — "victim decides": what the design call left of it (the verdict path)

**The rule as the WO states it was not built** — "the victim's machine runs the game's own hit test on its own player,
applies the damage once and sends the verdict back" needs the puppet's swing to fire a hit event on the victim, and it does
not (0.4). What was built is the part of the rule that does not need it: the bookkeeping a hit on a player never had. The
**host's engine still decides** hit or blocked (against the avatar's replayed guard, as since WO-132); what is new is that
every hit is **numbered, matched to the swing it belongs to, classified, applied once** and **checked on the victim against
the swing that was shown with it**, with the reason when there was none. Damage is never dropped for lacking a swing.

| Piece | What it does | Where | Mark |
|---|---|---|---|
| The message | `HitVerdict` **0x72 up / 0x73 down**, the next free pair, in the hit family beside 0x21/0x22 and 0x44/0x45; a row of `Protocol.JoinWire` (host only), so the relay's gate and pass-through come from the one table; body `[ver][verdict][flags][zone][swing:4][hp][st][nameLen][name]`, the join header's id slot is the hit id; verdicts hit / blocked, parried and missed **reserved** (never sent: they are the victim's own); no protocol bump (mixed releases are refused at the relay) | `dotnet/KcdMp.Protocol/ProtocolWo161.cs`, `ProtocolWo123.cs` (the row), `ClientSession.cs` (one log exclusion: a verdict per hit is not a relay log line) | [unit][syn] |
| The swing ledger (host) | each `NpcAttack` row the host sends gets an id, per NPC; a hit on an avatar is matched to its NPC's latest swing within 1.2 s (the field: the hit lands ~0.3 s after the row) | `Wo161.cs` `Wo161SwingLedger`, hooked in `GameBridge.Wo121.cs` `OnLocalActionAsync` | [unit] |
| The verdict (host) | blocked = no health lost and stamina paid (the field's `hp -0.0 st -24`), anything else = hit — damage is never classed away; numbered from 1; sent instead of the bare 0x21 (**one path per hit**); the 0.46.0 path is the fallback when the verdict path is off (`HitVerdictEnabled`, `kcdmp-client.json` / `--no-hit-verdict`) or the write fails; the zone is the attack zone the host's NpcCombat last said | `GameBridge.Wo161.cs` `Wo161SendHitAsync`, called from `OnNpcAvatarHitAsync` and the Lua sampler's `ghost_hit` | [unit]; **[not tested]** in the game |
| Exactly once (victim) | a hit id is applied once; a repeat within 30 s is logged `applied=dup` and counted; the memory is bounded and forgotten on a new connection | `Wo161Dedupe` | [unit] |
| Shown or a reason (victim) | the victim's agent records when a row of an NPC came in, was played on its copy, or was refused (and why); a verdict is judged against that: shown, or one of `no-swing-captured`, `row-not-received`, `row-not-played` / `row-refused-<why>`, `row-stale`, `no-attacker`, `missile`, `legacy` (a bare 0x22) | `Wo161Rules.Judge`, hooks in `GameBridge.Wo121.cs` (the inbound `NpcAttack` branch) | [unit]; **[not tested]** in the game |
| The log | `WO161-HIT victim=… by=… sid=… hid=… verdict=… dmg=<hp>/<st> dir=… shown=yes|no reason=… sent=…|applied=…` on both machines, and `MP-WO161-STATS` once a minute (out, fallback, in, applied, refused, dup_ignored, hit, blocked, damage totals, shown, not_shown with reasons, rows_played) | `GameBridge.Wo161.cs`, `Wo161Stats` | [unit] |

**Unchanged on purpose:** the knockdown rule (the victim's apply is the same four-argument call, `no knockdown: WO-155`,
never while down or waking: `ApplyPlayerHitAsync` only now says whether it landed); friendly fire (`0x44/0x45`); the DLL
(no native change, so no native test and no new hook, address or slot); the 0.46.0 behaviours of WO-160 (the placement, the
wake and neutral fixes, the talk hold) — none is touched.

**The swing the host never captured** (the field's 26 %: hits with no row in the 1.2 s before them) is the DLL's
`attack_capture`, not this layer: a late swing cannot be played because there is no row to play, and a generic row would be a
guess shown as the host's swing. It is now **counted and named** (`no-swing-captured`) on both machines; the next WO has the
number to work from. "A swing with no verdict" is a miss in a host-decides world, not a fault: `rows_played` is in the stats
line beside `applied` so the two can be read together.

**Tests** [unit][syn]: `Wo161Tests` (round trips, every hostile or damaged body refused — control characters, a Lua-shaped
name, a 65-character name, NaN/Infinity/negative/oversized figures, a lying length byte, reserved flag bits, a zone past
Lower, a named hit flagged NoAttacker and the reverse —, the ledger and its bounds, the verdict of the field's measured
hits, exactly-once across a duplicate and across the id wrap, the shown-or-reason table, a five-frame stream applying three
hits once each and counting the one with no swing) and the relay round trip (host → one joiner whole, the widest verdict,
a joiner's own verdict dropped, an over-long frame dropped with the framing intact). Client 1,236, setup 77, farkle 59,
relay 63, all green. The synthetic host (`tools/wo118/synthpeer`) has a new verb `hverdict <hit|blocked|parried|missed> <hp>
<st> <npc|-> [swing] [joiner] [hitId]` for the live run.

**The solo live run was not made** (it needs the game started, a throwaway playline loaded and the synthetic host as the
authority; see "What only two players, or the next session, can verify"): the 10 verdicts from a scripted host, 10
`WO161-HIT` lines, total damage equal to the sum, none unmatched, is the first line of the next session's runbook, with the
`hverdict` verb ready.

## Phases 2 and 3 — not built (the design call's consequence), and why each part

Per the WO's own branch for "puppet unworkable" (0.4): the puppet's parts of Phases 1–3 are void, and the appendix lists
what is still done (Phase 1's verdict path, 4, 5, 6 and the build). The reasons are recorded here per part so the next WO
starts from them, not from a guess:

* **2 shared targeting — the host's side** (a guard beating the avatar made lockable by the host's Henry, without changing the
  guard's target): rests on an engine rule not read (what the lock-on offers: 0.1), would add the host's soul to the guard's
  skirmish (`hits::skirmish_add`, override 0 exists), cannot be produced solo (an NPC that fights an avatar on demand is not
  available: WO-155, WO-132 §2.4), and would make a hit on a guard an assault in the host's own world unless WO-154's
  `mp_fair_crime` window judges it. Not built; the design is above and in the pocket list.
* **2 — the joiner's side and the "ganging up" retarget**: the joiner already engages any enemy copy it fights (WO-147,
  `engage on … whether or not the host fights it`) and its blows reach the host attributed; the retarget rule is
  `wo136::decide()` (switch at most every 3 s when the other source clearly beats the current one). A stickier rule is a
  native change with no way to measure it solo. Not built.
* **3 snapping**: the three causes are located (0.2), but the keeping rule ("a fix that does not move the number is
  reverted") needs a before/after in a scripted-partner fight, and the 0.45.2 counter measures steps over 5 m — which no
  fight in the field produced. The measure for a fight is the correction maximum per window and the largest render step after a
  hold (`tools/wo118/fight118.py`), and that harness needs the game's window in front (below 26 fps otherwise); this session
  never focuses the game. Three core writer changes with no measurement would be three guesses shipped. Not built.
  `WO161-SNAP` was therefore **not introduced** (a log line for a fix that does not exist).

## Phase 4 — the item 9 set

Done as 0.3 decided, per symptom: nothing was "the same fault as WO-160 §1.8", so **no fix was folded into the wake path**.
9.4 (silent damage) is Phase 1's rule — built, counted and named on both machines. 9.1 (the sleeping NPC) is recorded for
Part B and not fixed (the WO's own rule). 9.2 (the phantom stance on the host) and 9.3 (a swing and no combat) are separate,
not fixed, and each has an **eye-test in the checklist** (items 165–166 below) that names the lines to look at. What the
joiner's agent now logs for 9.3: the first blow lands before combat mode (37 ms in the field) — `WO161-HIT … shown=` and
`MP-W132 engage on` are on the same machine's log within a second of each other, so the order can be read.

## Phase 5 — the repo cleanup (its own commit, `2a72ddb`: moves only)

| What | Where it went | Why / proof |
|---|---|---|
| `testing_findings.md` | `research/` (private, git-ignored; untracked now) | the maintainer's field notes; a pointer line is in `docs/WO-160-findings.md` |
| `wo150-dryrun.txt` | `docs/wo150-dryrun.txt` | beside its WO findings (the default of the WO) |
| `kdcmp_brand/Libs/…/KCDLogo.dds` | `docs/branding/pak-source/Libs/…` | **the brand pak:** two readers (`tools/Publish-Release.ps1`, `tools/Build-MenuLogo.py`) — fewer than the WO's 3 — so the source moved and both were repointed; the pak's entry name `Libs/UI/Textures/KCDLogo.dds` does not depend on the folder. The shipped pak code, run against the new path, built a pak of **524,655 bytes with the one entry of 524,416 bytes, DDS sha256 `7d913350…1de86`** — the same entry, size and hash as 0.46.0's pak (the zip's own bytes differ by the entry timestamp, which is why the proof is the entry) [syn]. A launcher-started game showing the logo is **[not tested] live** (no launcher drive from here); the pak that game loads is byte-for-byte the one it loaded in 0.46.0 |
| `/logs/` (the maintainer's tester bundles, 248 MB, untracked) | stays; now git-ignored | so `git add -A` cannot publish players' logs; listed, not moved (they are the maintainer's evidence); proposed home `research/logs/` if they want it out of the tree |
| `kcd_launcher.log`, `kcdmp-native.log` (root leftovers, already ignored) | `research/root-leftovers/` | session artefacts |
| `KCDC_Review/` (an empty folder, untracked) | removed | nothing in it |
| everything else at the root | unchanged | `AUTHORS`, `Directory.Build.props`, `KCD2-MP.sln`, `LICENSE`, `NOTICE`, `README.md`, `VERSION` and the source folders (`KCDMP_launcher`, `assets`, `docs`, `dotnet`, `installer`, `kdcmp`, `native`, `tools`); `release/` and `research/` are ignored build output and private notes. There is no `.gitattributes` or `.editorconfig` to keep |
