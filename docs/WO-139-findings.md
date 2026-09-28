# WO-139 — Crime and guards: findings

Kingdom Come: Together. Unofficial; not affiliated with or endorsed by Warhorse
Studios.

Evidence marks: **(observed)** seen live in the game, **(synthetic)** the real
game against a synthetic peer or a console stand-in for a player's action, or
the Lua/C# test harness, **(code-verified)** read in the code or the game's data
and binaries but not run, **(inconclusive)** tried, no clear answer.

All live runs were solo on one machine (the Modding Tools game, 1.5.5), on a
throwaway copy of a save in Zelejov, never saved (a script save lock and a
frozen playline for the whole session). Host runs: the real game as the host
with a scripted partner (avatarpeer). Joiner runs: the real game joined a
synthetic host (synthpeer). No input was sent, so where a player presses a key
a console stand-in is used; what each stand-in proves is said where it is used.
The research is in `docs/WO-139-crime-reference.md` (how a crime is raised) and
`docs/WO-139-guards-reference.md` (how guards act).

## The answer

1. **The joiner's crimes are crimes in the host's world, but not the engine's
   own.** KCD2's crime record has no culprit: every crime an NPC knows counts
   against the local player (§3.2). So the host cannot raise "the avatar's
   theft" natively, and must never raise it against the host's Henry. The mod
   keeps the joiner's record itself, and the game's own guard behaviour does
   the rest on the machine where it can target the right Henry:
   * **Detected on the joiner's machine, sent as events.** Trespass comes from
     the HUD's own trespass state, hooked natively. Thefts (loose items,
     stashes), lockpicking, horse theft and robbing a body come from the game's
     own Lua interactions. Each is a CrimeAsk Report to the host. **(observed**
     for trespass end to end: entering a private house read level 3
     "personal", the report reached the host, leaving it read 0; **synthetic**
     for thefts, because a console pick cannot play its animation, §5.1**)**
   * **Judged in the host's world.** Who sees the avatar at that spot (a ray from
     each NPC's eyes), which of them are guards, and whose settlement it is. A
     crime nobody saw is no crime, as in the game. A guard who saw it knows at
     once; a civilian's report reaches the settlement's guards after 20 s.
     **(observed)** — a theft in Zelejov: 4 witnesses, 0 guards, known 20 s
     later.
   * **Stopped by the host's guard, arrested on the joiner's machine.** A guard
     of that settlement who sees the avatar within 12 m stops him. The host
     holds its guard in place. The stop itself runs on the joiner's machine: his
     copy of that guard gets the host's list of crimes planted as the game's own
     crime stimuli, and its own brain does the game's own arrest against his
     Henry (bark, chat, crime dialogue, escalation). The outcome goes back to the
     host: paid, punished, persuaded or executed clears the record there; fought
     or fled is resisting arrest. **(observed** on both sides: the host held the
     guard and cleared the record on the partner's "paid"; on the joiner, the
     guard came to Henry, barked, asked him to reply, repeated the call three
     times, then escalated to an attack. The chat's Reply and the dialogue's
     choices need the player's keys, so they go in the checklist, §5.2**)**
   * **Violent crimes: the host's guards attack the avatar.** For an assault, a
     knockout or a murder, the host's guard runs the game's own attack
     interrupt, aimed at the avatar, and `combat_forcedTarget` keeps him on it.
     His hits reach the joiner as damage. The game's own rule decides attack or
     arrest: a violent crime that is fresh, or any crime after a resist under
     5 minutes old, means attack; otherwise the guard arrests (a stop, with the
     violent crimes in it). The joiner going down or dying ends the fight; the
     next guard to see him arrests him. **(observed)** — a guard 60 m away ran
     to the avatar and fought it; 3 to 10 hits reached the partner; when the
     partner went down, the guard stopped fighting and arrested him for the
     assault; "paid" cleared it.
2. **No time skip for punishment.** In a session the punishment quest's time
   sets never run (the WO-137 native time hook, scoped to the punishment
   module), and its skip-time cutscenes are set to one second. **(code-verified**
   for the hook; **observed** for the cutscene data on both machines: Duration
   `0h0m1s`, no TargetTime**)**. Through every stop on the joiner the clock ran
   at its normal rate (30,977 game seconds in 2,086 real ones: ×14.85, ratio 15).
   **(observed)** Jail and the stocks themselves need the dialogue's choice, so
   they go in the checklist.
3. **Execution** is WO-113's death: the crime cleared and a respawn outside the
   town, now within the leash. The host clears the joiner's record on
   "executed". **(code-verified** for the respawn; **synthetic** for the
   clearing**)**
4. **Nothing between the players is a crime.** A friendly-fire hit from the
   avatar on the host's Henry took 4 hp and raised nothing (no judgement, no
   skirmish, no crime line). **(observed)** A hit on an avatar is never judged.
   **(synthetic)** The host's own hits on the avatar are ignored by witnesses
   through WO-68's context on it, read back true live. **(observed)**
5. **No robbing each other.** Looting or pickpocketing the other player's avatar
   is refused on both machines with "You can't steal from each other in
   co-op." **(observed** on the host and on the joiner**)**
6. **Horses of the host's world are never a theft for the joiner.** Two levers
   on the joiner's copy of the horse: the game's own legal-mount flag, and the
   crime context the lent-horse quests use (`crime_ignoredHorseTheft_Horse`,
   written natively). A townsperson's horse stays a crime. **(observed)** — the
   host listed a horse as legal: `IsMountLegal` false → true, context written
   and read back; the next horse along stayed illegal; dropping it from the list
   restored both.
7. **Takedowns never blame the host's Henry.** Before the avatar's knockout or
   stealth kill, the victim gets the three contexts that make the game's
   stealth-hit branch (which hard-codes the player as the attacker) and the
   found-body checks skip it. The takedown is then the joiner's crime.
   **(observed)** — all three written and read back, the knockout done, the
   victim's shout named the avatar, nobody turned on the host's Henry, and a
   guard attacked the avatar for it 20 s later.
8. **Stolen goods:** the host's guards never see the joiner's inventory. A
   stop's crime dialogue on the joiner's machine does the game's own
   confiscation (fine or punishment moves stolen items to the district chest)
   from his real inventory. The game's frisk is not run for him (his copies of
   the guards are paused). **(code-verified**, §3.4**)**

## 1. What runs where

| Piece | Where | What |
|---|---|---|
| Trespass detector | joiner, DLL | `C_UIHudStates::SetTrespassState` listener (GUIModule), level edges → unsolicited pipe frame 0xA3 |
| Crime reports | joiner, Lua + agent | wrapped `PickableItem.OnUsed/OnUsedHold`, `Stash.OnUsed`, `Minigame.StartLockPicking`, `Horse.OnMount`, the WO-131/134 loot gate → `w139_crime` → CrimeAsk Report (0x64) |
| Judge | host, Lua + agent | `KCD2MP_W139HostJudge`: witnesses, guards, settlement → `w139_judged` → the joiner's record (agent) → CrimeHost Judged |
| Guards | host, agent | every second: guards around an avatar with a known record; stop (≤12 m, sees) or pursue (≤40 m, at most 3) |
| The stop | joiner, Lua | the guard's copy unbound from the host's stream, its brain resumed, the host's crimes planted; the game's own arrest; `Crime.SendResolveDialogResult` wrapped for the result; ends → CrimeAsk Outcome |
| The hold | host, Lua | WO-137's `KCD2MP_W137HostHold` keeps the host's guard still; released and placed where the joiner's stop ended |
| Pursuit | host, DLL + Lua | pipe op 3: skirmish add + `combat_forcedTarget` guard → avatar (read back); Lua `crime:attackInitiatedByConcept`; off clears only the pairs the mod set |
| Takedown marks | host, DLL | pipe op 4: `crime_suppressMeleeStealthHitReaction`, `crime_ignoredNPCHitVolume`, `crime_ignoredUnconsciousBody` / `crime_ignoredCorpse` on the victim |
| Legal horses | host → joiner | the host's list (its own horse, legal mounts) every 30 s → the joiner sets `mountIsLegal` + `crime_ignoredHorseTheft_Horse`, restores when dropped |
| Punishment clock | both, DLL + Lua | pipe op 5: the WO-137 time hook refuses the punishment module's time sets; skip-time cutscene data at 1 s |
| Rob refusal | both, Lua | the WO-131 loot gate refuses an avatar first, with the co-op line |

`mp_crime_shared on|off` (the host's value is the session's, default on).
`mp_crime_status` prints `WO139-STATUS`; the agents print `MP-WO139-STATS`.

## 2. The proofs (Phase 5)

| Proof | Result | Mark |
|---|---|---|
| Joiner: a trespass detected and sent | native `WO139-TRESPASS level 255 -> 3 (personal)` inside a Zelejov house, `3 -> 0 (public)` outside; `MP-W139 joiner -> host crime #1: trespass`; the host got `CRIMEASK report tok=1 … trespass` | observed |
| Joiner: a theft detected and sent | the game refuses a console pick ("did not create actor anim request for npc 'Dude'"), so no real theft was made. The report path ran on that failed pick: a false positive (§6.4), fixed; on the fixed build the failed pick is "not reported". The detection of a real take is covered by the Lua suite | synthetic (a real theft: checklist) |
| Host: the crime raised | `WO139-JUDGE … theft … witnesses=4 guards=0 settlement=trosecko_settlements_zelejov`; known 20 s later | observed |
| Host: a guard's knowledge and the stop | `tzel_man_7 (…, 7 m) stops ghost 1 for theft:1 -- held here`; the guard stood still 48 s; `CrimeHost stop tok=1: tzel_man_7 theft:1 500` reached the partner; its "paid" → guard placed, released, `1 crime(s) cleared`, `CrimeHost cleared` | observed |
| Host: pursuit | `WO139-PURSUE on … forced_target=set (read back)` plus the attack; the guard ran 60 m and fought; `WO132-HITS npc hit on avatar … -7.28 / -12.47 / -21.83` reached the partner as damage | observed |
| Host: the fight ends, the arrest follows | partner downed → `stops fighting ghost 1's avatar (the joiner downed)`, `forced_target=cleared (read back) skirmish=left`; then `stops ghost 1 for assault:1`; "paid" → cleared | observed |
| Host: resisting arrest | partner "fled" at a stop → `resisting arrest: the guards … fight him now` → attack at once | observed |
| Host: takedown | three contexts written and read back before `RequestKnockOut`; judged as the partner's knockout; no reaction to the host's Henry | observed |
| Joiner: a stop dialogue, fine paid, no clock change | the stop ran the game's own arrest against the joiner's Henry; the clock stayed at its normal rate. The chat's Reply needs the key (`AcceptChatRequest` from script leaves the chat `WAITING_FOR_INTERACTION`), so the dialogue with its fine choice was not reached | observed up to the chat; the fine: checklist |
| Joiner: an unanswered stop | first call, chat, three repeated calls, the chat interrupted, the escalation bark, the attack at 43.7 s → `fled`; on the fixed build the copy's fight is ended before it is suspended again, and it stays calm afterwards | observed |
| Rob refusal, both sides | `WO139-ROB refused kind=loot|pickpocket avatar=kcd2mp_1` (host), `…kcd2mp_0` (joiner), each with the co-op line | observed |
| Legal horses | legal and restored as the host's list said | observed |
| No crime between players | friendly fire 100 → 96 hp, nothing raised | observed |
| Gates | relay 54/54, agent 567/567, every synthetic suite (WO-139: 113/113), native 178/178, payload smoke | synthetic |

## 3. Research answers (Phase 1)

### 3.1 How a crime is raised

An NPC's own "switch" brain turns what it perceives into a crime
**information**: `{label, PerceivedWuid, position}` plus tagged values.
`PerceivedWuid` is always the **object** of the crime (the victim, the item, the
lock, the horse, the house or area), never the culprit. The triggers are the
local player in a perceivable state (`loot`, `lockpick`, `pickpocketing`,
`trespass`, `carriedBody`, …), a short-lived crime volume (`theft`,
`crime_hit`, `crime_playerMounted`, …), a native message (`hitReaction`,
`hearingInfo`, …), or an injected `switch:stimulus:*` message. Every path is
gated on `$__player`. **(code-verified**, reference A §1**)**

Witnesses learn by seeing it (full recognition only), by a volume, by a
sound, or by a report: a civilian runs to a guard and transfers the
information (only between friends). Reputation hits are the game's own rows,
per witness and per settlement faction when an authority learns.

### 3.2 Can an NPC be the perpetrator? No

There is no culprit slot. Every crime information in an NPC's memory is treated
as the local player's: the arrest, the self-help, the recognition and the
punishment all target `$__player`. The closest the engine has is hostility
without a crime: a hit by a non-player attacker makes the victim decide a fight
against it; `crime:attackInitiatedByConcept` sends an NPC to attack a target;
`combat_forcedTarget` retargets a fighting NPC. **(code-verified)** So the
honest alternative, used here: the mod's own record on the host, the host's
guards made hostile to the avatar (for violent crimes), and the arrest itself
run on the joiner's machine, where "the local player" is the joiner's Henry.

### 3.3 The crime entry points the mod uses

The `crime_stimulus` mailbox takes every `switch:stimulus:*` message (a prefix
filter), but the tree consumes only nine: theft, murder, kettlePoisoning,
escalatedTrespass, disturbance, information, hit (only with the player as
attacker), combat, animalAbuse. **(code-verified)** What a stop plants:

| Crime | Planted as | Live |
|---|---|---|
| theft, the piece still on him | `switch:stimulus:theft` {method pick, pivot = the item's wuid, owner, immediate} | the pivot must be an item wuid (`entity.item:GetId()`); a pickable's `GetMyWUID` is 0 — observed |
| theft, the piece gone; robbing a body | `switch:stimulus:disturbance` {perceivedWuid = the player, priceOverride 500} | observed (a disturbance → the guard's bark and arrest) |
| trespass | `switch:stimulus:escalatedTrespass` {wuidType none, stimulusKind trespass} | observed |
| lockpick / horse theft | disturbance at 600 / 2000 | synthetic |
| assault, knockout / murder (once not fresh) | disturbance at 1500 / 20001 | observed (500 + 1500 planted) |

`switch:stimulus:information` only triggers a reaction to a crime the NPC
already holds ("No such information!"); it does not create one. **(observed)**
Murder is one decagroschen over the dialogue's branding threshold (20000),
because a disturbance carries no murder label; a fine-reduction perk can bring
it under.

### 3.4 Guards: the stop, the pursuit, the search

* **The stop** (`interrupt_arrest`): the guard follows the player, barks the
  first call at 7 m and requests the chat "surrender / refuse" (the player's
  Reply). Every 6–8 s without an answer he repeats the call; after four he
  escalates and attacks. Refusing, walking 25 m away, drawing a weapon, aiming
  or mounting also escalate. Surrendering starts the crime dialogue: pay the
  fine; "not enough money"; accept the punishment; one skill check (persuade,
  impress, dread, scholarship with a perk, drinking with a perk); fight.
  **There is no bribe option.** **(code-verified**; the call, repeats and
  escalation **observed** on the joiner**)**
* **The result** goes to the guard as `crime:resolveDialogFeedback` via
  `Crime.SendResolveDialogResult` (Lua). A fine confiscates the money; a fine
  or a punishment moves stolen items to the district's stolen-items chest; all
  the guard's crime informations (and his friends' within 50 m) are destroyed.
  With no feedback within 10 s the resolve assumes a successful skill check.
  **(code-verified)**
* **Escalation memory:** after a resist, the same guard attacks at once for
  5 minutes (`crime_arrestEscalationPeriod`). **(observed** on the joiner's
  copy: two stops within 5 minutes of a resist went straight to the attack**)**
* **Pursuit and search:** a guard who loses sight of the player searches
  (`lookAround`) and escalates; a guard told of a crime he did not see looks
  for the player. The game's pursuit always targets the local player, which
  is why the host's pursuit of the avatar is the attack interrupt plus the
  forced target. **(code-verified; the host's pursuit observed)**
* **Stolen goods:** "stolen" is a native item flag set by `item:OnSteal`. The
  frisk is done by authorities on stationary duty, with a chance and a 90-minute
  cooldown; found stolen items are confiscated without a crime. The host's
  guards never see the joiner's inventory (the avatar carries only his outfit),
  and his copies of the guards are paused, so no frisk runs for him. In a
  stop's dialogue the game's own confiscation runs on his machine.
  **(code-verified)**

### 3.5 How jail and the stocks move time

The punishment quest (`open_world/nextnextgenpunishment`) fades, teleports the
player to the punishment spot, then moves the clock:

* within 200 m of the punishment cutscene: a skip-time cutscene, short (2 h,
  `Duration`) between 08:00 and 17:00, long (to 10:00, `TargetTime`)
  otherwise. The game reads both from the cutscene data entity's script
  properties when the cutscene starts (GUIModule
  `C_SkipTimeCutscene::InitializeDuration`). In a session the mod sets them to
  one second (and no target time) and restores them after;
* beyond 200 m: a fast-travel cutscene, then `AdvanceWorldTime TimeOfDay=10h`
  (a second arrest from afar: 9 h). In a session the time hook refuses these;
* the beating and branding cutscenes declare `Time="8h"` (GUIModule
  `C_IngameCutscene::SetTime`): read as the cutscene's lighting, not a clock
  jump **(inferred)**;
* execution plays its cutscene and ends in Game Over 44, which WO-113 turns
  into its death. **(code-verified)**

The game's clock never goes backwards, so skips are refused at the source
rather than undone.

## 4. Decisions made unattended, and why

1. **The host keeps the joiner's record itself** (the engine can't hold a crime
   for anyone but its own player, §3.2). It is per joiner, lives for the
   session, and clears on a resolution, on an execution and on a host reload.
2. **The arrest runs on the joiner's machine**, with his copy of the guard
   running its own brain. It is the only place the game's own arrest,
   dialogue, fine and confiscation target the right Henry. The host holds its
   guard meanwhile and places it where the stop ended.
3. **What a stop plants is the host's list.** The joiner's own record only
   names the stolen pieces (so a theft is planted with its item when he still
   carries it). A crime nobody saw, or one already paid for, is never planted
   again; `Cleared` from the host wipes the joiner's record. (Found in the live
   runs, §6.5.)
4. **Attack or arrest follows the game's own rule** (reference B §1.2): fresh
   violent crime (2 minutes after the guards learn of it) or a resist under
   5 minutes old → the host's guards attack the avatar; otherwise → a stop,
   violent crimes included. The joiner going down or dying ends the fight and
   calms the record (the crimes stand; a death is no sentence).
5. **A fight starts with the game's own attack interrupt.** `combat_forcedTarget`
   alone never starts one: the guard knew and walked on. Its WO-136 finding
   ("retargets fighting NPCs") holds. **(observed)**
6. **A trespass is one crime per settlement while open.** The joiner re-reports
   every 8 s while he stays inside (someone may walk in on him); later reports
   only add what they saw. **(observed:** three reports, one crime, one
   judgement**)**
7. **A theft counts once the piece is in his inventory**, not when the pick
   starts: a failed pick can leave the item hidden in the world and in no
   inventory. **(observed)**
8. **Villagers' reports belong to their settlement.** With no guard witness and
   no victim, the settlement is the civilian witnesses'. (Before this, a house
   trespass seen by four villagers was filed under "the wilds" and never acted
   on. **observed**, then fixed; **synthetic** after.)
9. **"Paid" with no money taken counts as talked** (the record stands). The
   game takes the fine right after the choice, so this only guards against a
   dialogue that never went on.
10. **The copy's fight ends before it is suspended again** (the game's own
    `stopFight`), because a copy suspended in the middle of its attack picks it
    up the next time it runs. **(observed:** without it, a later resume
    attacked Henry; with it, the copy stayed calm**)**
11. **Reputation:** the crime dialogue's reputation effects happen in the
    joiner's own game; nothing is applied to the host's world (shared
    reputation is out of scope).
12. **The host's own punishment moves no clock in a session either**, because
    the clock is shared. Outside a session nothing changes.

## 5. Not reachable without input, and other limits

1. **A real theft on the joiner** needs the player's pick animation; a console
   pick fails with "did not create actor anim request". The detection and its
   confirmation are covered by the suite and by the failed-pick case.
2. **The chat's Reply and the crime dialogue's choices** need the player's
   keys. `BasicAIActions.OnChatRequestAccepted` (what the Reply prompt runs)
   was called and logged, but the chat stayed `WAITING_FOR_INTERACTION`. So
   paying the fine, jail, the stocks and persuading are checklist items. The
   stop's result wrap was run with the dialogue's own Lua call.
   **(observed** for the wrap; the rest is checklist**)**
3. **An unanswered chat request on a suspended copy** lingers until the player
   walks away. Walking away ends it ("Dialog ends but no response was
   played"). This came up only because a console stand-in answered the guard
   without the chat. **(observed)**
4. **The joiner's copy of a guard keeps its own memory of him**: after a
   resist it attacks on sight for 5 minutes, and planted crimes stay until a
   resolution destroys them. The game's forget message
   (`crime:forgetCrimesData`) exists but only runs in a running brain. It is not
   wired.
5. **The punishment path was not run live.** The fast-travel branch (beyond
   200 m) and WO-114's refusal of the joiner's fast travel may interact.
   **(inconclusive)**
6. **Crime expiry by the world clock** is in the record but not fed (records
   last the session).
7. **Several joiners:** records, stops and pursuits are per joiner, one stop
   at a time each.

## 6. Found and fixed during the live runs

1. The pursuit set its forced target, but the guard walked on. Fixed: the
   pursuit also sends the game's own attack message (observed working, §2).
2. Violent crimes could never end: the pursuit only stopped on a resolution,
   and a stop never included violent crimes. Fixed with the game's
   fresh-or-escalated rule and ending the fight when the joiner is down or dead.
3. A trespass re-reported every 8 s stacked a fine each time. Fixed with a
   merge.
4. A failed pick was reported as a theft (the item left the world but reached
   no inventory). Fixed: the take is confirmed by the inventory.
5. The joiner's own record was planted instead of the host's list: a crime
   nobody saw, or one already paid for, could be fined again. Fixed.
6. A copy suspended mid-attack resumed attacking later. Fixed with `stopFight`.
7. A house trespass seen only by villagers belonged to no settlement. Fixed.
8. The host's legal-horse list was never re-sent when unchanged, so a later
   joiner never got it. Fixed: re-sent every 30 s (observed).
9. Tests: the join wire range test widened to 0x67; the WO-131 check that
   looted an avatar now expects the refusal (by design); a new check covers a
   dialogue stand-in.

## 7. The native half

| What | Where (1.5.5, MT) | How |
|---|---|---|
| Trespass listener | GUIModule+0x27E4C0 (connected to the actor's trespass signal) | an inline gate records the level byte, pass-through; anchored by a 16-byte prologue |
| `C_UIHudStates::SetTrespassState` | GUIModule+0x27EBB0 | the anchor (17-byte prologue); the listener is found from the functions loading it |
| Trespass levels | public 0, semipublic 1, semipersonal 2, personal 3, prohibited 4 | the HUD warns for 3 and 4; so does the mod |
| Pursuit | `C_ScriptContextManager` slot [4] `SetRelationContext` (WHGame+0x6BF60), read back through slot [8] | `combat_forcedTarget` guard → avatar, plus a skirmish add; only the pairs the mod set are cleared, all of them when the pipe closes |
| Entity contexts | the same manager, entity contexts | an allow-list of five |
| Punishment clock | the WO-137 `FunctionInvoke` time hook | node paths under `Barbora.open_world.nextnextgenpunishment` refused while armed |
| Skip-time data | GUIModule `C_SkipTimeCutscene::InitializeDuration` (0x14E3E0) | reads `Duration` / `TargetTime` from the entity's script properties (the Lua side sets them) |
| Cutscene time | GUIModule `C_IngameCutscene::SetTime` (0x14AFC0) | read only |

Pipe op 0x25 (reply 0xA2): 1 config [on] → [armed][level]; 2 status → text;
3 pursue [on][avatarEid:4][nameLen][guard] → [1 set/cleared, 0 already, 2 no
such, 3 refused]; 4 context [on][len][ctx][len][entity] → [1 written, 0 already,
2 no such, 3 refused, 4 not allowed]; 5 punishment gate [on] → [armed].
Unsolicited 0xA3: kind 1 trespass [level][prev][x y z].

## 8. Reference

**The wire** (the join channel; the relay forwards joiner → host and host → one
joiner only):

| Type | Kind | Text |
|---|---|---|
| 0x64/0x65 CrimeAsk (joiner → host) | 1 Report | `<crime> <x> <y> <z> <victim|-> <itemClass|-> <where>` |
| | 2 Outcome | `<result> <guard|-> <fine> <x> <y> <z>` (tok = the stop's id) |
| | 3 Resync | `<why>` |
| 0x66/0x67 CrimeHost (host → a joiner) | 1 Judged | `<crime> <witnesses> <guards> <known 0|1> <settlement|->` |
| | 2 Stop | `<guard> <crime:n,…> <fine>` |
| | 3 Pursue | `on|off <guard>` |
| | 4 Record | `<open> <crime:n,…|-> <settlement,…|->` (the answer to a Resync) |
| | 5 Horses | `<part> <nparts> <name,…|->` |
| | 6 Cleared | `<why> <settlement|->` |
| | 7 Mode | `on|off <why>` |

Crimes: theft, lockpick, trespass, horsetheft, robbody (the joiner reports
these); assault, murder, knockout (the host sees these). Results: paid,
punished, fought, fled, bribed, persuaded, talked, executed, refused, nostop.

**The crime table** (Tables `rpg/crime.xml`, 1.5.5; fines in decagroschen):
theft 500, lockpick 600, trespass 250, horse theft 2000, corpse violation 2000
(violent), assault 1500 (violent), murder 20000 (violent).

**Timings:** a civilian's report 20 s; fresh violent 120 s; escalation 300 s;
the judge's sight 25 m, a ~210° view, a ray from the eyes; the guard scan 45 m;
a stop ≤12 m and seen; a pursuit ≤40 m, at most 3 guards per joiner, over
after 60 s away; the joiner's stop: the guard within 30 m, at most 240 s.

**Logs:** host agent `MP-W139 host: …`, joiner agent `MP-W139 joiner: …`, both
`MP-WO139-STATS`; kcd.log `WO139-JUDGE`, `WO139-STOP start|planted|result|end|return`,
`WO139-PURSUE attack`, `WO139-PLACE`, `WO139-HORSE`, `WO139-SKIPTIME`,
`WO139-ROB`, `WO139-CRIME`, `WO139-CLEARED`, `WO139-SESSION`, `WO139-STATUS`;
native `WO139-BUILD`, `WO139-CONFIG`, `WO139-TRESPASS`, `WO139-PURSUE on|off`,
`WO139-CONTEXT`, `WO139-TIMESET`, `WO139-NATIVE`.

Frame: `docs/wo139-shots/1-joiner-stop-guard-asks-to-reply.jpg` — the joiner's
screen during a stop: the host's guard at Henry's side, the game's own arrest
chat ("Guard — Reply"), the crime icon lit.
