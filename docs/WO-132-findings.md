# WO-132 — join fix, damage safety, combat engagement: findings

Unattended, solo, one machine: the Modding Tools game, throwaway saves, a local
relay on its own port. The real game ran as the **joiner** with
`tools/wo118/synthpeer` as the host (runs J1–J3: a join with `--join-host125`,
plan-driven host streams with `--claim-host`), and as the **host** with
`tools/wo121/avatarpeer` as the joiner (runs H1–H3). Evidence marks:
(observed), (code-verified), (synthetic), (inconclusive). Screens:
`docs/wo132-shots/`, all from the joiner's own window, consecutive frames about
0.45 s apart. `VERSION` read **0.30.5** before this WO and is now **0.30.7**
(the maintainer's string).

## 0. Answer first

**Phase 1 — joins no longer fail at random.**

| item | result |
|---|---|
| why the joins failed | **found** (code-verified). The game's own writer (CryAction, the save output chunk, `+0x3b0ee0`) deflates each 32 KB piece with `ISystem::CompressDataBlock` at level 3 and keeps the result **only when it is smaller than 0x8000 bytes**; otherwise it writes the piece **stored** (`[-1][rawLen][raw]`). The reader (`C_SaveInputZlibStream::ReadBlock`, Framework `+0x13c020`) refuses a compressed block over 0x8000 (the field's "compressed 32770, uncompressed 32768, buffer 32768") and a stored block over 0x8000, and inflates into a 0x8000 buffer. Our splicer wrote every block compressed, so a chunk that does not compress overflowed |
| the fix | the rebuild stores such a block exactly as the game does; `WhsSave.Verify` (and the Python reference tools) now refuse any block the engine would refuse, so such a file is never placed (code-verified, synthetic) |
| every save on this machine | 276 game-written saves pass the stricter verify; the game's own largest compressed block is 32767 bytes and 23 blocks are stored (observed). Every 1.5.5 save re-spliced as the host world: **10 of 117** splices would have carried a ≥ 32768 compressed block under the old code (8.5 %, the field's "at random"); all 133 now splice and verify (observed) |
| found on the way | 16 early-game saves (Henry stripped by the story: an inventory record with no item list) could not splice at all, as host or joiner. An absent list is now an empty inventory (observed, synthetic) |
| live | two joins through the real flow (at the main menu → Bring my character → splice → place → load): both reached `Gameplay started`; the second world's splice (reproduced byte for byte offline) **carries a stored block**, and loaded (observed) |

**Phase 2 — damage is safe.**

| item | result |
|---|---|
| nobody hit while dead or waking | **built** (synthetic; the down/wake edges observed). The host blocks every forward to a peer from its **down** (the downed bit in its vitals — with WO-113's death guard a dying joiner is floored, so no death packet comes first — or its death packet) until **5 s after its wake** (the respawn announcement, or the downed bit clearing on a knockdown). A down that never wakes stops blocking after 3 min. The joiner refuses a forwarded hit in the same window itself |
| stop only the avatar's fight | **built, observed in part.** `C_SkirmishManager::RemoveSoulFromSkirmish` (vtable `+0x18`, RPGModule `+0x645970`) found and used: at the peer's down, and again at its wake, only the avatar leaves its skirmish. Live: `SoulRemoved on kcd2mp_1`, the enemy's `Set opponent: -`, the enemy did not vanish (observed). Whether the **host's** own fight then goes on could not be shown: the only enemy this machine could put in a skirmish with both players never fought the host, so the fight ended (`SkirmishVictory`) once the avatar left (inconclusive) |
| the avatar at the peer's down | bleeding healed on every body part, health restored, hidden where it fell, shown again 1.5 s after the wake at the new position (observed) |
| no tick forwarding | **fixed** (code-verified, synthetic). The forward is now the hit itself: the DLL's hit chokepoint (the `C_CombatSoul` hit slots it already hooked) measures an NPC's hit on an avatar over its landing window, puts the health back, and only that goes to the owner. The Lua health sampler (which saw every drop, the 93 bleed ticks included) is superseded while the DLL's watch is armed; without the DLL a drop under 1 hp is a tick, never a hit. Bleeding on the avatar is healed after every hit |
| one path per hit | **fixed** (code-verified). The second line was the host's LocalHit observer also sending the avatar's health drop on the guid route; the joiner's guid fallback (on by default) then called ApplyDamage on a soul it does not have — the native log's `ApplyDamage ... -> soul not loaded / failed`. An avatar's hits now go only by 0x21 (an NPC's) and friendly fire (a player's) |
| fair damage | **not measured** (inconclusive). No NPC hit an avatar on this machine (spawned souls do not fight; see §2.4). The forward stays the avatar's measured loss; the two-player checklist asks for both numbers |

**Phase 3 — combat engagement.**

| item | result |
|---|---|
| route | **the combat actor / skirmish only, brain paused** (observed). A bound, suspended copy of a host NPC that is in a fight near the joiner joins a skirmish against the joiner (`AddSoulToSkirmish`, override 1) and holds the host NPC's own combat state (combat mode, guard zone and stance, attack zone, block — the same applier the avatars use). The host samples its drawn NPCs' combat models natively and sends `NpcCombat` (a new action kind, host-only at the relay) |
| the joiner's combat mode | **observed.** On the joiner's screen: the game's directional combat indicator on the copy and the combat icon under the compass, in every one of 6 + 12 + 4 consecutive frames over two runs; the engine reads back `IsLockedToOpponent(copy) = true` and the player's combat model opponent = the copy (supplementary) |
| block | the player's block works in that combat mode: raised through the engine's own `SetBlockMode` (what the block button calls, no input), four frames show the joiner's sword in the block pose (observed). The copy's "block" phase looked no different from its guard with a club (inconclusive) |
| the copy on his screen | faces him in a guard stance; **changes stance when the host NPC changes guard zone** (overhead → side, observed); swings (the host's swing cue, an overhead club blow over four frames, observed); host attack rows play as in WO-131 |
| position | held to the host's: 0.000 m from the host's position over 5 samples; the engine's per-frame pull on the engaged copy 0.35 cm mean, 1.5 cm max (observed) |
| afterwards | released at the host's combat end (`engage off`, skirmish left, lock false, no indicator); the copy stays bound and suspended: CSV 114 s, 68 names, **0 free human rows**, the copy `suspended=1 driven=1` throughout (observed) |
| its local hits on the joiner | discarded by construction (measured at the chokepoint and put back; a forwarded host hit landing in the window is kept). None happened live: a suspended copy never attacks (discard counter 0) (code-verified; inconclusive live) |
| the host engages too | **one cause fixed, the rest inconclusive.** The joiner's attributed hit used override 1, which makes the avatar the NPC's target even when it is fighting the host; now an NPC that already fights the host keeps him (override 0) (code-verified). The host's own death leaves only his skirmish (was StopFight). Live, the host never reached combat mode against the test enemy: its combat was terminated by the engine every frame ("Instant termination"), a spawned-soul limit (inconclusive) |
| split NPCs | none: engagement never unbinds or resumes a copy (CSV above) |

## 1. Phase 1 in detail

### 1.1 The engine's rule

Reader, Framework `C_SaveInputZlibStream::ReadBlock` (`+0x13c020`), per block
header `[i32 clen][u32 rawLen]`:

- `clen == -1`: stored; `rawLen > 0x8000` → "Invalid savegame block size: %u,
  buffer:%u";
- otherwise `clen > 0x8000` (unsigned) → "Invalid savegame block size:
  compressed %u, uncompressed %u, buffer:%u"; then
  `ISystem::DecompressDataBlock` into a 0x8000 buffer (a larger inflate fails);
  the block's length is the inflated length.

Writer, CryAction `+0x3b0ee0` (the save output chunk): per fragment of the
save stream (fragments are 0x8000 bytes, C_FragmentedMemoryIOStream),
`pSystem->CompressDataBlock(in, n, out, &outLen = bound, level 3)`; the result
is kept when the call succeeds **and `outLen < 0x8000`**; else the header is
`[-1][n]` and the raw bytes follow.

Evidence from 276 game-written saves (observed): largest compressed block
32767, 23 stored blocks. Python's zlib at level 3 would have compressed those 23
under 32768 (the game's zlib differs); the rule is the size, not the library.

### 1.2 What changed

- `WhsSave.Deflate`: a block whose deflate is not smaller than 0x8000 is written
  stored. The zlib level stays .NET's Optimal (no level-3 knob in .NET 8; the
  reader inflates any valid zlib).
- `WhsSave.Inflate` / `Verify`: refuse a stored or header length over 0x8000, a
  compressed length over 0x8000, a negative length other than -1, and an
  inflate past 0x8000. Every place a save is placed already gated on `Verify`.
- `tools/Splice-SaveHenry.py` (level 3 and the stored rule) and
  `tools/Read-SaveAnatomy.py` (the same limits) follow; they are reference
  tools only.
- An inventory record with no item list is an empty inventory (C# and Python).

### 1.3 Tests

| test | result | mark |
|---|---|---|
| a synthetic save with a random 70 KB chunk: the old rebuild writes a compressed block > 32768 and the new `Verify` refuses it ("block size: compressed …"); the new rebuild stores it and verifies; same stream | pass | synthetic |
| every reader limit (stored 32769, compressed over the buffer, inflate past the buffer even with a lying header, clen -2) | pass | synthetic |
| incompressible block 0 stored, the rest compressed | pass | synthetic |
| a stripped Henry both as joiner and as host | pass | synthetic |
| 276 saves through the new verify | 276/276 | observed |
| re-splice every 1.5.5 save as host (two Henrys: a normal one, a stripped one) | 133/133 verify; 10 (normal Henry) and 4 (stripped) carry a stored block | observed |
| live join, world copy of `playline1/quicksave043` | loaded (no stored block needed in that splice) | observed |
| live join, world copy of `playline2/autosave008` + the joiner's own Henry | the placed file (1,313,038 B, reproduced byte for byte offline) carries stored block #98; loaded, all four post-load checks passed | observed |

## 2. Phase 2 in detail

### 2.1 The gate

`LifeGate` (agent): `Down` at the first down signal, `Up` at every wake signal,
`Check` → down / waking (< 5 s after the last up) / open; a down older than
180 s with no up is dropped. The host keeps one per peer and checks it for
every NPC hit it would forward (`MP-W132 npc hit on avatar N ... NOT forwarded:
its player is down|waking`). The joiner keeps one for itself (the DLL's downed
and respawn frames) and refuses a 0x22 in the same window (`[playerhit] ...
REFUSED: I am down|waking`).

Live (H3): the peer's downed vitals → `MP-W132 peer 1 downed: nothing is
forwarded to it until 5 s after it wakes`; its respawn 30 s later → `awake --
forwards resume in 5 s` (observed). No NPC hit arrived during the window (none
could be produced), so the block itself is shown by the unit tests (synthetic).

### 2.2 Only the avatar leaves

Native op `0x22/1` LeaveFight → `RemoveSoulFromSkirmish(mgr, soul)` → the
engine's `C_Skirmish::RemoveSoul`. Live (H3, a test soldier in one skirmish
with the avatar and the host, targeting the avatar):

```
MP-W132 peer 1 downed: its avatar left its skirmish -> removed
Skirmish event: SoulRemoved on kcd2mp_1 (target -)
Skirmish event: TargetChanged on wo132_soldier (target -)
[wo132_soldier] Model: Set opponent: -
... SkirmishVictory on wo132_soldier ... SoulRemoved on Dude
WO132-AVATAR down id=1 bleed_healed=10 hidden=true
```

The avatar left alone and the soldier dropped it (observed). The skirmish then
ended because the soldier had no other opponent (it never fought the host in
this rig), so "the host's fight goes on" is not shown (inconclusive). The
soldier stayed in the world (observed). The host's own death now does the same
for the host (`WO132-LEAVEFIGHT the local player's death`; StopFight only as the
fallback; code-verified). The knockdown path keeps StopFight (it wakes in place
and needs the fight over).

### 2.3 Real hits only, one path

- Native: the existing hit hook now also watches an NPC attacker's hit on an
  avatar (`WO132-HITS npc hit on avatar ... measured hp -X`) and hands it to
  the agent (pipe frame 0x9A); the agent forwards it (0x21) unless the gate says
  otherwise, then heals the avatar's bleeding and restores its health.
- The Lua sampler's `ghost_hit` is superseded while `npc_watch=armed` (the
  status line; live: armed in every run); a tick is never a hit without it.
- The guid route for an avatar body is dropped at the host
  (`route=guid-ghost ... reason=wo132-one-path`).

### 2.4 What could not be produced solo

No NPC hit on an avatar happened, so neither the live forward nor the fair
damage comparison exist. Tried, in order: the avatar's and the host's takedown
attempt on a spawned test Cuman (nothing: the takedown needs sneaking), the
WO-17 hostile-donor faction on the test Cuman (no reaction: homeless souls have
no behaviour), and the combat autotests' own lever on a test soldier (a target,
combat mode, combat automation on, re-asserted every 0.25 s): it targeted the
avatar, then the host, but the engine terminated its combat each frame
("Requested combat termination: Instant termination"). The world's bandit
groups away from settlements on this save are quest groups, hidden until their
quest runs; not touched.

## 3. Phase 3 in detail

### 3.1 Research: which part of a copy to wake

| candidate | verdict |
|---|---|
| the combat actor / skirmish only, brain paused | **chosen**: a skirmish against the joiner plus the host NPC's combat state held on the copy. The joiner's combat mode comes up (observed); the brain stays suspended, the native writer keeps the position (0.000 m) |
| a scoped brain resume while engaged | not needed; it would run the copy's own choices (the WO-131 split) |
| WO-119's harness driving the copy from the host's actions | used for the state (combat mode, guard zone, block — the same setters); the host's attack rows already play (WO-131) |

### 3.2 The rules as built

- Host: every live NPC it streams with its weapon drawn is watched natively
  (combat model every 100 ms); a change, and a 1 s heartbeat while in combat,
  go out as `NpcCombat` with who it fights (the host, an avatar, someone else).
  The relay forwards it only from the damage authority.
- Joiner: a copy is engaged only when it is **guarded and natively bound** (the
  host's copy, never a free one), the host NPC is in combat, and the copy is
  within 15 m (the DLL checks; farther → released). It engages with this player
  only. No host state for 3 s, combat over, or the copy no longer the host's →
  released: the hold ends, the copy leaves the skirmish, automation back as it
  was; the copy is still bound and suspended.
- Its local hits on this player are measured and put back; the host decides
  every hit, health change and death (WO-131's hit gate, copy guard and
  host-only death are unchanged).

### 3.3 Live, the joiner's screen (runs J1, J3)

| check | result | mark |
|---|---|---|
| host NPC in combat with the joiner's avatar → `MP-W132 engage on ... skirmish vs me added` | yes, 3.0–3.9 m | observed |
| the directional indicator and the combat icon on the copy | 6 frames (sheet 1), 12 frames (sheet 3) | observed |
| the copy faces him in a guard stance; the host's guard-zone change shows | sheet 3 (overhead), sheet 4 (side) | observed |
| the copy swings | 4 frames of an overhead club blow (sheet 1, the host's swing cue) | observed |
| his block | sheet 3, last row: the sword raised (SetBlockMode, no input) | observed |
| the copy's block | not distinguishable from its guard with a club | inconclusive |
| position | 0.000 m from the host's; pull 0.35 cm mean / 1.5 cm max per frame | observed |
| release at the host's combat end | `engage off`, lock false, no indicator (sheet 5) | observed |
| afterwards bound and paused | CSV 114 s: 0 free human rows | observed |
| local hits discarded | none happened (paused copy); counter 0 | inconclusive |

The host streams in these runs were the plan-driven synthetic host
(synthetic): a real host fight could not be recorded (§2.4), so the replay
of a real fight into the joiner (the WO's protocol) was not run.

### 3.4 The host's side

- The override fix (code-verified): `WO121-HITS attributed ... skirmish add
  (override 0: it already fights the host, who keeps it)`.
- The host's combat mode against an enemy it fights beside the avatar, and the
  enemy engaging the host, were not produced (§2.4) (inconclusive).
- Faction: unchanged from WO-131 (the avatar in the player's faction). The
  engine does not "hand the fight to the avatar" by the faction alone in these
  runs — the soldier kept whichever target the skirmish add gave it (observed).

## 4. Runs

| run | the real game | what |
|---|---|---|
| J1 | joiner (plan host) | first engagement, the indicator, the swing, the release |
| J2 | joiner (Host125) | two live joins, the second with a stored block |
| J3 | joiner (plan host) | guard, host block phase, guard-zone change, the player's block, position, release, CSV |
| H1–H3 | host (avatarpeer) | the native NPC watch armed; enemies tried (§2.4); the peer's down: the avatar leaves its skirmish, heal, hide; the wake: shown, grace |

## 5. Screens (`docs/wo132-shots/`)

| file | what |
|---|---|
| `1-joiner-combat-mode-strip.jpg` | J1, 6 consecutive frames: the indicator on the copy, the combat icon, the copy's swing |
| `2-joiner-combat-mode-frame.jpg` | J1 frame 0, full |
| `3-joiner-guard-npcblock-playerblock.jpg` | J3: guard (row 1), host NPC blocking (row 2), the player's block (row 3) |
| `4-joiner-guard-zone-change.jpg` | J3: the copy's stance after the host's guard-zone change |
| `5-joiner-after-release.jpg` | J1 after the release: no indicator, the copy at rest |

## 6. Gates

Every `Build-Installer.ps1` gate green (docs/WO-132-progress.md has the
numbers): the relay round trip; agent unit tests (new: `Wo132Tests.cs` block
limits, `Wo132RulesTests.cs` — the down/wake gate and its grace, no tick
forwarding, one path per hit, the NpcCombat wire and the engagement rules); the
synthetic suites (new `Test-WO132Synthetic`, 9/9: the avatar's down and wake,
the bleed heal); the static checks; the native tests; the local publish and
payload smoke. `Verify-Install.ps1` knows the WO-132 markers.

## 7. Two-player checklist (the next session)

Each item: what to look at on the **joiner's** screen and on the **host's**.

1. **The join works from any host save.** Host: load an old save and a new
   one in turn (a fresh one early in the story too). Joiner: every join reaches
   the world; never "Savegame loading failed". Host: nothing to see.
2. **Combat mode shows for both players.** Fight bandits together, weapons
   drawn. Joiner: the directional indicator on the bandit in front of you, the
   combat icon under the compass, and your block works. Host: the same on your
   own screen for the one you fight.
3. **Enemies swing on the joiner's screen.** Joiner: the bandit facing you
   raises its weapon and swings; its stance changes when it changes guard.
   Host: that bandit is really attacking in your world at that moment.
4. **The guard engages both.** Joiner starts it (hits a hostile first); host
   joins in. Host: the enemy turns to you when you hit it, and you get combat
   mode. Joiner: it keeps fighting you too. (Away from towns; never town
   guards.)
5. **No hits after a respawn.** Joiner dies in the fight. Joiner: after the
   wake, no damage at all for the first seconds, and none from the fight you
   left. Host: `MP-W132 ... NOT forwarded: its player is down|waking` in the
   agent log if the enemy was still hitting the avatar.
6. **No second death from bleeding.** Joiner: bleed in a fight, then get away:
   your health only drops from your own game's bleeding, never from the host's
   side; no "bleeding" death after the fight.
7. **The guard doesn't vanish when the joiner dies.** Host: the enemy that
   was fighting you both keeps fighting you when the joiner dies, and stays in
   the world. Joiner: after waking, it no longer comes for you.
8. **Fair damage.** Same armour, same enemy, one hit each. Joiner: health lost
   (kcd.log `[playerhit] took N damage`). Host: health lost on your own bar.
   Note both numbers; more than ~10 % apart → the next WO forwards the hit
   itself.
9. **Your swings on each other's screens.** Face each other, swing once each
   (friendly fire is on). Each screen: the other player's figure swings.

## 8. Carried forward

1. A live NPC hit on an avatar: the native forward, the gate blocking a real
   hit, and the fair-damage numbers (needs a real hostile; two players, item 8).
2. The host's own combat mode beside the avatar, and the enemy turning to the
   host after the joiner started the fight (item 4).
3. The host's fight going on after the joiner's death (item 7): the avatar
   leaves correctly; the rest needs an enemy that fights both.
4. A real host fight recorded and replayed into a joiner (the WO's protocol).
5. The copy's block pose with a shield or sword (clubs showed none).
6. The players' own swings (WO-131 2c) — still untested live.
