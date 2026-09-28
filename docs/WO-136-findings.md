# WO-136 — world presence: findings

Unattended, solo, one machine: the Modding Tools game, a throwaway copy of
`playline1/quicksave036` as `playline4` (removed afterwards), a local relay on
its own port (7779). The real game ran as the **host** with
`tools/wo121/avatarpeer` as the joiner (runs H1–H7) and as the **joiner** of
`tools/wo118/synthpeer --join-host125` (run J1: a menu join, then an in-world
join, "J2"). Evidence marks: (observed), (code-verified), (synthetic),
(inconclusive). Poses are judged only by consecutive frames on the screen that
matters. `VERSION` is untouched (0.40.0); no installer (WO-140 builds it).
Frames: `docs/wo136-shots/`. Side effects and gates: `docs/WO-136-progress.md`.

## 0. Answer first

| field item | phase | result |
|---|---|---|
| 8 joiner load stall ~55 s, "Loading screen timeouted…" | 1 load hold | **fixed** (observed). Nothing touches an NPC or an avatar while a world loads: the agent holds every NPC state, avatar, NPC damage and action frame from the load command until `Gameplay started` + 2 s (newest per NPC / per peer kept, the rest in order, then replayed), and the mod holds its own paths (pause, pause reconcile, puppet tick, copy guard, avatar update and interpolation, body/chest loop). Menu join J1: file read 46.9 s → `Gameplay started` 54.4 s (7.5 s after the read), 1,037 frames held, 4 replayed, no red overlay. In-world join J2: 10 s, 2 replayed, no overlay |
| 1 spawned animals missing on the joiner | 2 stand-ins | **fixed** (observed). The host streams animals like horses (flag 0x80); the joiner's stand-in is the host's **own soul in its own class** (the agent reads Tables.pak: 8,206 souls, name = soul name, archetype = class). J1: `prepadeniNaCeste_wolf_1` → class Wolf, soul 2981ff4d, walks with the host's stream (frame 7). Its **bite is not animated** on the joiner (the native swing path resolves human actors only, `target-missing`); the bite's **damage does reach the joiner** through the host (observed H7) |
| 2 horses vanish under the joiner | 3 horses | **fixed** (observed). The rider owns the horse: on the mount the copy is unbound, its brain back, shown, the host's stream for it ignored and the copy guard lifted; after the dismount the host's stream takes it back 3 s later from where the rider left it — no teleport (J1, frame 9). Host side: the avatar rides the host's horse (frame 1), the host stops streaming that horse while it is ridden and streams it again, where it was left, after the dismount (H1). Found and fixed: `GetHorse()` returns a WUID, not an entity id |
| 3 knockout stays standing on the joiner | 4 fights | **fixed** (observed). Two causes: the field host never sent a knockout (its enemies had *surrendered*, `SoulSurrender`); and an engaged copy ignored the knockout. Now a host knockout (or death) ends the engagement **first**, then WO-135's knockout mode takes the copy down (J1, frame 8: guard stance → falls → stays down) |
| 4 the fight gives up when the host dies; enemies never pick the joiner | 4 fights | **fixed** (observed, H6/H7). The engine's own "fight this one" is the relation context `combat_forcedTarget` (Warhorse's battle controller and quest fights use it); the mod now sets it NPC → avatar. Avatar hits switch a wolf from the host to the avatar in 154–159 ms; at the host's real death the other wolf is handed to the avatar (read back 480 ms); the avatar was bitten every ~3 s for 40+ s after the death, both wolves on it (frames 5, 6), no T-pose |
| 5 host outfit stuck in guard armour; joiner items refused | 5 outfit | **fixed** (observed). The avatar spawns with an **empty clothing preset** (the old white_red preset's pieces were not items and could never come off); it wears only the partner's real items (H1 frame 2, J1 frame 10, no retries). A refused item now names its class, kind and a known reason (`belt_2slot (QuickSlotContainer) (a quick-slot belt: not armour an NPC body wears)`) |
| 6 host torch not shown | 5 torch | **fixed** (observed, both directions). A torch in hand rides the state block (bit 0x20); the avatar draws the game's own torch into its left hand, lit, and puts it away (host H1 frame 3; joiner J1 frames 11–12, at 23:00) |
| 7 crouch not shown either way | 6 crouch | **shown both ways** (observed); the key path is **code-verified**. The crouch key's action (`toggle_crouch` → the actor's crouch action) changes the stance the capture now reads first: the game's own "is crouched" query (actor vtable +0xAF8). A crouch set that way is captured the same frame (`source: stance+byte+tag`, H2); the host avatar crouches on the host (frame 4) and on the joiner (frame 12). A real key press was never made (no input) |
| 9 false "Someone already took that" | 7 loot notice | **fixed** (synthetic). Root cause (field log): the loot screen adds the NPC's home key 62 ms after opening; it was reported as a put, the host's echo made two, "take all" asked for two → "gone". Now a put is only an item that left the player's own pack; the notice shows only when **another** player took it (`gone`); the player's own duplicate (`mine`) or an item the host never had (`none`) is taken back quietly |

Found and fixed on the way: a stand-in wolf was spawned into the joiner's
**own** world after it left the host's (J2; the copy guard now needs the host's
world loaded); a new load hold inherited an older hold's deadline (synthetic);
the H3 game crash was my harness, not the mod (§8).

## 1. Phase 1 — the load hold

**What the field showed.** The joiner's loading screen sat ~55 s after the
world file was read and ended with "Loading screen timeouted while still in
post load reconstruction". WO-135 had stopped the copy guard's pauses during a
load; the rest of the pipeline still touched NPCs: the NPC stream bound, paused
and wrote copies, the puppet tick moved them, avatars were spawned and moved.

**The hold, two halves.**
- Agent (`GameBridge.Wo136.cs`): holding = the game is loading or at the menu,
  or less than 2 s have passed since `Gameplay started`. While holding, the
  frame processor keeps back `NpcStateDown`, `Ghost`, `NpcDamageDown` and
  `ActionDown` (the newest per NPC name and per peer; everything else in order,
  capped at 512) and the native feed skips them. On release they go through the
  normal path once. `MP-W136 load hold released after …: N held frame(s) replayed`.
- Mod (`kdcmp.lua`, `KCD2MP_W136Hold`): the join/leave load sets the hold before
  `wh_sys_LoadGame` (TTL 240 s); the agent lifts it after the settle (and says
  so again every 5 s). Under the hold `KCD2MP_ApplyNpcState` only queues the
  newest sample per NPC; pauses, pause reconciles, the puppet tick, the copy
  guard, avatar updates and interpolation and the body/chest loop return.
  `WO136-HOLD on why=load-join` … `WO136-HOLD off why=gameplay+settle held_s=… replayed=…`.

**Live.** J1 (menu join, 3 NPC streams running through the load): hold on at
the load command; `Gameplay started` 54.4 s after the command (file read at
46.9 s); hold off 2 s later, 56.4 s held; the agent held 1,037 frames and
replayed 4. No "Loading screen timeouted" (0 in kcd.log). J2 (in-world join
while the wolf stream ran): 10 s to `Gameplay started`, hold off 11 s after it
went on, 2 replayed, no overlay. The leave load in between was held too
(12.6 s, 3 replayed) (observed).

## 2. Phase 2 — animal stand-ins

- Host: the NPC scan includes animals (the native class registry resolves
  `Wolf`, `WildDog`, `Boar`); every not-human body streams flag 0x80 (horses
  already did); the resync burst and the scan compare include animals (observed
  H1: a spawned wolf streamed with 128).
- Joiner: a name the host streams and this world lacks asks the agent
  (`w136_soul <name>`). The agent answers from the game's soul tables
  (`SoulIndex`, Tables.pak `rpg/soul*.xml` + `soul_archetype.xml`, 162 ms to
  read): soul guid + class. The stand-in is spawned with that soul and class
  under the same name, so every by-name path (puppet, native bind, copy guard,
  hit gate, host-only death) works unchanged. A horse never gets one (both
  worlds have the host's horses); an unknown animal name falls back to a species
  soul (wolf / dog / boar); an unknown human waits 1.5 s, then WO-131's guess.
- Live J1: `WO136-STANDIN soul npc=prepadeniNaCeste_wolf_1 class=Wolf soul=2981ff4d`,
  `WO131-STANDIN spawn … class=Wolf kind=animal`; it walked with the host's
  stream (frame 7, from behind the host avatar into view) (observed).
- **Bites:** the host's bite row reached the joiner (`NpcAttack … bite+attack_heavy`)
  but did not play: `result=target-missing`, also after the copy was engaged
  (`MP-W132 engage … skirmish vs me added`). The native swing path
  (`combat_construct.cpp`) resolves the actor through the human actor lookup;
  a wolf's does not resolve (code-verified). The bite's damage reaches the
  joiner through the host's world (the avatar is bitten there and the joiner
  gets the hit, H7). Open: an animal bite animation on the joiner's copy.

## 3. Phase 3 — horses: the rider owns the horse

- Joiner (`KCD2MP_W136RideTick`, every 100 ms from the interpolation tick):
  mounted on a horse the host streams → `WO136-RIDE take`: the copy is unbound
  natively, its puppet removed, unpaused (`wh_ai_ResumeNPC`), shown; the agent
  drops the host's samples for it and lifts its copy guard
  (`MP-W136 ride: this player rides …`). The rider's own position stream
  (riding + `horse_info`) moves the host's horse with the avatar. Dismounted →
  3 s later `WO136-RIDE return`: the host's stream again, from where it was left.
- Found live: `human:GetHorse()` returns the horse's **WUID** (a userdata), not
  an entity id; `System.GetEntity` found nothing, so no ride was ever detected.
  Fixed with `XGenAIModule.GetEntityByWUID` (J1; the function was redefined live
  to finish the run, the pak carries the fix).
- Live J1: mount → taken by the tick; dismount → `return … after_s=29.9` 3.1 s
  after the dismount; the horse stayed at 2447.50, 2089.00 the whole time
  (never teleported), shown (`hidden=false`) (frame 9) (observed).
- Host H1: avatarpeer `ride wo136_horse` → the avatar mounted the host's horse
  (`NATIVE MOUNT SUCCESS`), the host stopped streaming it; after `ride off` the
  horse stayed where it was and was streamed again (frame 1) (observed).
- Noticed: the joiner's HUD offers **"Mount and steal"** on the host's horse
  copy (frame 10). ForceMount bypassed it in the test; a real key press may be a
  crime on the joiner's side. Shared crime is out of scope; two-player checklist
  item.

## 4. Phase 4 — fights

### 4.1 Knockout beats engagement (joiner)
The field host never streamed a knockout: its enemies surrendered
(`Skirmish event: SoulSurrender` on `tzel_man_6` / the corpse robber). And an
engaged copy (WO-132: the host's combat state held on it) ignored a knockout.
Now the agent notes every host NPC's down flags; a knockout or a death releases
the engagement first (`MP-W136 …: the host's NPC is knocked out -> the engagement
ends FIRST (released), then the copy goes down`), and an engagement is refused
while the host's NPC is down. Live J1: `tzel_man_6` engaged against the joiner
(guard up), then flag 2 → released → `WO135-KO … knocked out ok=true` → it fell
and stayed down (frame 8) (observed).

### 4.2 Enemies choose the joiner; the fight goes on after the host's death (host)
The mod keeps a threat table per NPC (host hits, avatar hits 2, avatar swings 1,
6 s window, a switch at most every 3 s, a challenger must beat the current
opponent by 3). What moved the engine, in order of the runs:

| run | lever | result |
|---|---|---|
| H1 | `AddSoulToSkirmish` override 1 (WO-119) | no `TargetChanged`: an NPC already fighting ignores it; at the host's real death the engine closed the fight (`TargetEscaped → PlayerFlee → SkirmishVictory`) |
| H2 | overrides 0–3 on a fighting wolf | no effect. A *fresh* wolf added against the avatar → `TargetChanged … kcd2mp_1`, `Set opponent: kcd2mp_1` |
| H2/H4/H5 | leave the skirmish + re-add | once switched and bit the avatar (H4); twice put straight back on the host, also after 3 redos (H2, H5) |
| H4/H5 | the player leaves first, then add | the wolf freed (`TargetChanged -`) but not taken (25 tries) |
| **H6/H7** | **`combat_forcedTarget`** (Relation context, NPC → avatar) + leave/re-add | **switched every time**, read back after 154–159 ms |

`combat_forcedTarget` is Warhorse's own: `Tables.pak ai/ScriptContext.xml`
(`Class="Relation" SideEffect="combatForcedTarget"`), set by the battle group
controller and the quest fight utilities (43 behaviour files). It is set through
the WO-68 context manager's slot [4] `SetRelationContext(bool, wuid from, wuid
to, node)` and read back through slot [8] (both checked against the
disassembled build: address + 12 prologue bytes; a mismatch refuses relations
only). The mod clears only pairs it set itself (the store is refcounted), when
the NPC has had no opponent for 2 s, a body is gone, or it takes another target.
Every switch is read back by the native tick for 3 s and redone (at most 3
times) if the engine put it back.

Live H7 (two test wolves in the open field, both first on the host):
- avatar hits on wolf G → `WO136-FORCED … set (read back)`, `WO136-TARGET … taken
  (read back after 159 ms)`; wolf G `Attack` / `HitTarget` on `kcd2mp_1`, the peer
  got the hits (`got NPC hit (0x22)`); wolf H kept biting the host — one on each
  (observed);
- Henry's health lowered, wolf H killed him (the real death, WO-113 grave):
  `WO136-HANDOVER npc=wo132_wolfH -> avatar … taken (read back after 480 ms)`;
  the avatar was bitten every ~3 s from t=107 to t=150 (40+ s after the death;
  13 `HitTarget on kcd2mp_1` by wolf G in the first half minute); wolf H then bit
  the avatar too — both on it (observed);
- frames (Henry walked back): the wolves lunge at the avatar in its fighting
  pose, bitten (frame 6); H6 frame 5: the wolf at the avatar while Henry faces
  the other. No T-pose in any frame (observed).
Not exercised live: the host winning an NPC back by its own hits (the same
path, `to = host` clears the forced pair; code-verified); an avatar's swing as a
combatant (code-verified; the live runs used attributed hits).

## 5. Phase 5 — outfit, refused items, torch

- **Preset:** the avatar spawned with the `white_red` clothing preset; its
  pieces are not inventory items, so `UnequipItem` failed ("Item was not found")
  and they held slots the partner's own clothes needed — the field's "guard
  armour". The spawn now equips `kcd2mp_bare` (`dc000004-…`, an empty preset in
  `clothing_preset__kdcmp.xml`); if a preset piece ever shows up again the agent
  clears with the same preset and re-applies once. Live: `WO136-OUTFIT spawn …
  bare preset ok=true`; H1: the host's 7 classes mirrored, no retries (frame 2);
  J1: 5 classes (including the belt and hose the field refused), `+4 -5`, no
  retries, the host avatar in plain clothes (frame 10) (observed).
- **Refused items** now log class, kind and a known reason: H2 (the avatar in
  combat first: `not worn yet -- the avatar is in combat; tried again by the next
  packet`), then `belt_2slot (QuickSlotContainer) (a quick-slot belt: not armour
  an NPC body wears) can't be worn by this avatar`, `HoseSeparate04_m01_E
  (Armor) can't be worn …` (sent alone, without the layers it sits on), an
  unknown class by its guid (observed). Why the same belt and hose were worn in
  J1's full outfit is not known (inconclusive).
- **Torch:** sender: `WO136-TORCH local=1 (4cea28a0-…)` when the player holds
  `ui_nm_torch` → state bit 0x20. Receiver: the avatar gets the torch item if it
  has none, `DrawFromInventory(item, 1, true)` into the left hand; off:
  `HolsterToInventory(1, true)` (two arguments — with one it did nothing, found
  in H1), read back. Host H1: Henry's torch → the peer received the bit; the
  avatar's torch at night (frame 3). Joiner J1 at 23:00: the host avatar holds
  it lit (frame 11), puts it away (frame 12, only the belt lantern left)
  (observed). `Player.TryDrawTorch` is not registered on this build.

## 6. Phase 6 — crouch

The key: action `toggle_crouch` (EntityModule, `C_EntityActions+0x50`) → the
actor's crouch action (`C_ActorActionCrouch`) → the stance component; the game's
own "is crouched" is the actor vtable call at +0xAF8 (EntityModule `+0x9E710`:
the stance component at player+0xAC0, state 1), the same query that feeds
`player_in_crouch` and the jump check (code-verified). WO-135 read the
expansion's desire byte and the Mannequin stealth tag; the capture now asks the
stance query first (guarded by its exact call and compare bytes; `WO136-CROUCH
query armed`, H2) and ORs the three, logging the source. Live H2: a crouch set
through the player's crouch setter → `local crouch=1 (source: stance+byte+tag)`
the same frame → the peer got `crouch=1`. The avatar crouches on the host
(frame 4, four frames crouched, standing again) and the host's avatar on the
joiner (frame 12) (observed). The key press itself was never made (no input
allowed): code-verified that it reaches the stance the capture reads.

## 7. Phase 7 — the loot notice

Field (joiner log): the loot screen opened, the NPC's `key_home`
(494ed6dc-…) appeared in the body 62 ms later, was reported as a put, the
host's echo made two, "take all" asked for two and got "gone" → "Someone
already took that". Now:
- a put is an item whose id was in the player's own pack when the screen
  opened; anything else is the game's own (`WO134-BODY not-a-put … not sent`);
- the host answers a take `ok | gone | mine | none` (and world items `mine` when
  the same peer took it before); only `gone` shows the notice ("someone else took
  it first"); `mine` and `none` are taken back off the joiner's Henry quietly.
Synthetic (WO-134 suite 60/60 with the put-from-pack case; agent verdict tests);
not run live.

## 8. Harness notes (not the mod)

- H3 crashed the game: `run114.py stop` takes a process kind, not a run tag, so
  `stop H2` stopped nothing; the H2 relay, agent and peer kept running and the
  H3 agent joined as a guest of the dead H2 host (ghosts spawned on Henry). The
  host script now refuses a busy relay port.
- The host screen cannot show a fight after the host's death (Henry wakes
  ≥100 m away); frames of the post-death fight were taken after Henry walked
  back, with the camera turned by `actor:PlayerSetViewAngles` (a Lua call, not
  input). The joiner's screen during the host's death is a two-player item.

## 9. For the two-player session (the WO-136 part of the checklist)

`docs/TWO-PLAYER-CHECKLIST.md`, section WO-136: a load with enemies nearby, an
encounter wolf on both screens, riding the host's horse (and whether the mount
is a theft), a knockout in a fight, the host dying mid-fight, outfits, a torch at
night, crouching by the key, looting a body both ways.
