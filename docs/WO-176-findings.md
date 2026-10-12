# WO-176 — save safety finished, fighting together (0.50.0)

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

2026-10-11/12, unattended (overnight). Marks: **[L]** live, on this machine's Modding Tools build (1.5.5), copies of saves in the throwaway
`playline4` only (never a playline above 4, never a real slot), the game minimised except during input tests; **[K]** live with real key /
mouse input under the maintainer's input policy (2026-10-11, quoted in the WO; log summary at the end); **[syn]** synthetic (the real
kdcmp.lua under MoonSharp, the synthetic peer); **[unit]** the .NET / native / Lua suites; **[F]** read from field files (game data,
the host's saves); **[not run]**. The version is the maintainer's (**0.50.0**, given in the WO). The partner was always the synthetic peer;
nobody was at the keyboard. Entity names such as `ttac_man_3` are the game's own.

Housekeeping before anything: the whole saves folder backed up byte-equal (826 files, 1,422,012,848 bytes, SHA-256 each, outside the repo);
the installed `Mods\kdcmp` (four files) backed up with hashes. The maintainer's own launcher relay was running on 7778 / 5273 (started 18:52);
it was left alone — the harness relay ran on 7788 / 5283 (`KCDMP_TEST_PORT`, `KCDMP_TEST_URLS`). `playline4` held the launcher's staged
start save `quicksave022.whs` (WO-159, 18:53); it is left untouched and compared at the end.

## In short

| item | result | default |
|---|---|---|
| **A1** the partner's figure and a quest's arranged fight | built: by the quest's own contexts (+ WO-174's names), 60 s aftermath, brain paused near it, shielded, taken out when a fighter turns on it, its blows routed nowhere, the joiner told once. Live on the field's own meadow fight (`323` copy): **0 figure blows into the fight** (45/45 kept out), 0 mod skirmish adds, verdict **`Won`**, the save reloads and **draws by key 1** with the sword on the HUD. **Not met: 0 figure members** — the engine itself adds the figure (its body is a Zelejov villager's soul) | `mp_arranged_guard` **on** |
| **A2** S4 live | **pass**: the agent repaired the loaded `335` copy into `_fixed`, rescanned, loaded it, said so once; key 1 draws, HUD sword | `mp_save_health` on |
| **A3** S1 headless | **pass** on a scratch saves root with the launcher's own calls (original byte-identical, "Together: …" → the copy); not live here (no free playline) | `mp_coop_playline` on |
| **A4** the live save round trip | **PASS** with the game up (not skipped) | — |
| **B1** real hits vs an armed bandit | host's own 45 blows: blocked + parried **68.9 %**; joiner's blows: **not stageable** — the bandit never fought the figure (72 engine hits, 0 blocks); applied once, no one-blow knock-out, 0 faults | `mp_real_hits` **on** (the maintainer's decision; second build) |
| **B2** your own block decides | no block: **hit 20/20 ✔**; block held / perfect: **0/20** — the player reaches ParryInPlace against the striking copy, but has no Opponent, so the block zone is unset and the zone test fails; built: such a block is referred to the host | `mp_victim_decides` **off** |
| **B3** blocked outcomes shown | (a) the copy's own block row **26/26**, (b) the attacker's recoil **18/18** (host-verdict path), screenshots show the poses, FIGHTSNAP p90 4.3 / 10.5 cm | `mp_copy_blocks` **on**, `mp_block_recoil` on |

## Part A — save safety

### A2 — S4 live [L][K]

The agent (repo build, host role, local relay) running **before** the load; `wh_sys_LoadGame 4 autosave935` (a copy of the field file
`autosave335`, `whs_repair.py check`: one stuck fight, `souboj_na_poli`):

| step | evidence |
|---|---|
| the agent checks the loaded file | `MP-W175 playline4/autosave935_fixed.whs written and listed -- loading the repaired copy` |
| the copy is listed (native rescan) and loaded | `Loading saved game '%USER%/saves/playline4/autosave935_fixed.whs'` |
| the player is told once | `MP-TOAST kind=native text="A quest fight in this save never finished, so your weapons were locked. The mod made a repaired copy (your original is untouched) and loads it now."` (`WO175-HEALTH when=load result=repaired … file=playline4/autosave935_fixed.whs`) |
| the copy is healthy | `MP-W175 check why=load playline4/autosave935_fixed.whs: healthy (no stuck quest fight)`, `WO175-HEALTH when=load result=ok` |
| key **1** (real input, the game in front, foreground-checked) | `PlayAudio: qam_open` → `Changing weapon for Dude - will create new weapon collisions.`; `player.human:IsWeaponDrawn()` = **true** |
| HUD | the active-weapon slot shows the **sword** icon (the game's own `r_GetScreenShot 1`) |

The original `autosave935` was not written (the copy is a new file beside it).

### A4 — the live save round-trip gate [L]

`tools/Test-WO175SaveRoundTrip.py` with the game up (throwaway `playline4`): draw before **DRAWN** → `Game.SaveGameViaResting` →
`playline4/autosave336`: **0 stuck fights** → reloaded → draw after **DRAWN** → `WO175 save round-trip (live): PASS` (not SKIPPED).

### A3 — S1 headless [syn: the launcher's own agent calls, real files]

The launcher's exact calls, without its window (`KcdMpClient --w159 saves`, `--w175coop prepare`, `--w175coop commit --pl 0 --name …`,
the same binary the live test ran), on a scratch saves root (outside the game's folder): `playline0` = copies of the field world
(`autosave321`, `autosave322`), every other playline free.

| step | result |
|---|---|
| menu before | `Playline 0 (Oct 10 22:01)` → `playline0/autosave322`; free 1–4 |
| prepare (before the game starts) | `the placeholder for a world's first co-op copy: playline1/autosave001 (free playline1)` |
| the menu's pick commits | `WO175-COOP copy from=playline0/autosave322 to=playline1/autosave001 seed=3d31150313` — the copy's bytes = the world's (cmp) |
| the session saves into the co-op playline | the file the **live game wrote** in A4 (`autosave336`, same world seed) placed as `playline1/autosave002` |
| menu after | `{"pl":1,"name":"autosave002","label":"Together: Playline 0 (Oct 10 22:01)"}` and the original `{"pl":0,"name":"autosave322","label":"Playline 0 (Oct 10 22:01)"}` |
| the original playline | `sha256sum -c`: `playline0/autosave321.whs: OK`, `playline0/autosave322.whs: OK` (byte-identical) |

**Not run live:** this machine's saves folder has no free playline ≤ 4 (`playline0`–`3` are the maintainer's, `playline4` the
throwaway), so S1 would refuse here by design (`WO175-COOP refused reason=no-free-playline`); the game was never pointed at a co-op
playline.

### A1 — the partner's figure and a quest's arranged fight [L][K][unit][syn]

**What the quest graph does [F]** (`Scripts.pak … konecZabomysichValek/…/souboj_na_poli.xml`, read this session): `SetEntityContext
combat_arrangedFight` and `SetEntityContext combat_forceUnarmedWhileInUnarmedSkirmish` on **every soul of both groups** (`joinarrays1`
= skupina_1 + skupina_2), `IsActive ← skirmishControler.Active`. So every fighter of an arranged fight — not only the player — carries
the contexts while it runs. Live, in the `323` copy: **18 fighters + Henry** read `HasScriptContext` true for both (`ttac_*`, `tzel_*`,
`tapo_safarik`); none of them is on WO-174's name list.

**Built.** `native/KCDMP/wo176.*` + `wo176_rules.h`: `hits::scene_owned_eid` (the one question every keep-out site asks — WO-121's
skirmish add and raw blow, WO-132's engage, WO-136's retarget and threat, WO-163's pair, WO-165's lock-on) now asks the actor's soul for
the two contexts first (the engine's own context store, through the WO-143 reader, main thread, cached 0.5 s), WO-174's name list second.
WO-173's real hit asks it before anything else (`Fallback::Arranged`, `fallback=arranged-fight`): **neither the engine path nor the raw
path runs**; WO-121's raw path refuses the blow before any damage (reason 9). An actor stays the story's for **60 s after** its contexts
lift (`quest=context-aftermath`): the quest heals its fighters and runs its own aftermath, and a knocked-out fighter is not finished off
by a figure in that window. A 2 Hz tick reads every actor within 40 m of a figure: a figure inside such a fight (the actor's opponent, or
the engine's relation test) leaves the skirmish (`RemoveSoulFromSkirmish`), the mod's forced target / threats on that actor are dropped
(`wo136::forget_npc`), and while a story fight is near the figure carries the game's own `combat_invisible` + `combat_alwaysWithdrawSkirmish`
(refcount-aware, only what the mod wrote is cleared, off 5 s after no story actor is near). Agent: fallback 11 / reason 9 → no WO-121
fallback, no engage, no brain message, no crime/fight watchers, a fatal packet for that actor within 60 s is not applied, the joiner is
told once (W164 kind 9 `story-fight` → their HUD: *"This fight belongs to the story — your partner fights it."*). `mp_arranged_guard`
(host, default **on**). Lines: `WO176-ARRANGED skirmish=npc:<actor> quest=context|context-aftermath|by-actor-name context=<name> npc=…
site=… [figure=…] figure_kept_out=yes|removed …`, `WO176-SHIELD figure=… on|off contexts=…`, `MP-W176 kept …`, `MP-W176-STATS`.

**The live check.** Not by replaying Procek's dialogue, the travel cutscene and the meadow polylog from `322` with real input: the field's
own `autosave323` (copy → `playline4/autosave923`) **is** that path, saved by the engine on the first frame of the scripted fight (controller
Active, result Started, the contexts already set by the quest). Host role, the synthetic joiner's figure 2.5 m from Henry inside the fight,
the joiner's swings + attributed hits on `tzel_frantisek` every 2 s (30) and the host's own "set this NPC on the avatar" lever on
`tzel_olbram` (`avatarfight`, 15).

| | run 1 (keep-out of blows only) | run 2 (+ shield + aftermath) | control (no figure) |
|---|---|---|---|
| figure blows refused (`fallback=arranged-fight`) | 8 while the contexts held | **45** | — |
| figure blows the engine or the raw path applied | 22 — all **after** the controller went Done (the contexts lift with it): the harness kept hitting knocked-out fighters | **0** | — |
| WO-121 skirmish adds | 2 (after Done) | **0** | — |
| kept-out lines | `quest=context` | 87 `quest=context`, 1 `quest=context-aftermath` | — |
| the joiner told | once (`SYNTH got W164 … kind=story-fight tzel_frantisek`) | once | — |
| quest fighters killed | **2** (`tzel_frantisek`, `tzel_olbram`, killer `kcd2mp_1`, after Done) → a witness (`tapo_safarik`) killed Henry → the mod's grave (inventory 0) | **0** | 0 |
| verdict in a save taken after (`whs_dump.py`) | `Done` / **`Won`** | `Done` / **`Won`** | `Done` / `Won` |
| that save reloaded, key **1** | no draw (Henry had been killed: no weapon) | **draws** (`Changing weapon for Dude`, `IsWeaponDrawn` true), HUD **sword** | draws |
| the figure in the fight's skirmish | added by the engine, removed by the tick 22× | added by the engine **176×**, the engine itself withdrew it 102×, the tick 151×; Tachov fighters targeted it 25×, `ttac_procek` hit it | — |

**Why the figure is still pulled in, and what was done (runs 3–4).** The figure's body is spawned from a local NPC's soul for its face —
`face pick for '1': key=synth-host … soul=tzel_man_10`, **a Zelejov villager**, keyed by the partner's name (the same soul wherever they
are). The engine's own village-fight logic treats that soul as one of Zelejov's: `Connected to the soul NPC was created kcd2mp_1` →
`SoulAdded on kcd2mp_1` the moment it spawns, and again right after every removal. A probe paused the figure's brain with the game's own
`wh_ai_PauseNPC` (the mod's WO-102 lever) inside the fight: **0** re-joins and **0** fighters turning on it for the next 75 s. So:

- **Lua (kdcmp.lua WO-176):** while any actor within 40 m of a figure carries an arranged-fight context, the figure's brain is paused
  (`WO176-PAUSE figure=… on story=<actor>`), resumed 5 s after none is near (`… off`); a load re-arms the chain; `mp_arranged_guard off`
  resumes at once. Its body still moves and swings as its partner does (positions and rows are written natively). 8 Lua checks.
- **DLL:** the 2 Hz tick takes the figure out of the skirmish only when a fighter is actually turned on it (its opponent is the figure);
  a relation-only membership is counted (`passive_member`), not removed — every removal was answered by an immediate re-add (run 3:
  195 adds for 79 removals).

| | run 2 (removal + shield) | run 3 (+ brain pause, remove on relation) | run 4 (remove only when targeted) | **run 5 (the shipped build)** |
|---|---|---|---|---|
| figure blows kept out / applied | 45 / 0 | 45 / 0 | 45 / 0 | **45 / 0** |
| mod skirmish adds | 0 | 0 | 0 | **0** |
| the engine's `SoulAdded` on the figure | 176 | 195 | 62 | **28** |
| the mod's removals | 151 | 79 | 8 | **8** (only when a fighter turned on it) |
| fighters turning on the figure | 25 (`ttac_procek` hit it) | 0 | 10 | **8** |
| quest fighters killed | 0 | 0 | 0 | **0** |
| verdict (save after, `whs_dump.py`) | `Won` | — | `Won` | **`Won`** |

**The WO's check "0 figure members in the fight's skirmish": not met.** The engine itself adds the figure as a (mostly passive) member
because of its villager soul; the mod adds it nowhere, takes it out when the fight turns on it, pauses its brain and routes none of its
blows into the fight. The root fix is the figure's soul: a face pick from a soul with no village (a traveller's) — the spawn code's
choice, changed by nobody this WO (it would change every partner's face).
**Met:** 0 figure blows routed into the fight; the fight ends with a verdict (`Won`, runs 1, 2, 4 and the control); that save reloads and
**draws by key 1** with the sword on the HUD (run 2, `docs/wo176-shots/a1-after-story-fight-hud-sword.jpg`); the joiner told once.

**The flee case (the graph hole)** was not re-created live: it needs the player to flee the fight, be pulled back in, and have their side
lose while they stand — not reproducible by script without changing the fight. The S4 repair's catch of exactly that saved state was
run live in A2 (the field file `335`).

## Part B — fighting together

### What was built (code) [unit][native][syn]

- **B1 instruments (read only, never change a blow):** the DLL's hit slot now logs the engine's verdict on the host's **own** melee blows
  (`WO176-HOSTHIT victim=… outcome=hit|blocked|pb|broken rec=[blk second pb broken]`, from the record the engine built: +0x54 block,
  +0x56 perfect, +0x57 broken) and on every NPC blow that lands on the local player (`WO176-HITME attacker=… outcome=…`). The joiner's side
  was already logged by WO-173 (`WO173-HIT … engine=…`).
- **B2 — the player's own block decides (replay flag 0x80):** WO-165's replay referred a player holding block to the host's verdict
  (flag 0x10) because a copy never struck. Since WO-166 C3 a copy reads **Striking** inside its swing's window, so the agent now asks for
  `0x10 | 0x80`: the DLL refers a blocking player **only when the attacking copy does not read Striking** (`wo165_rules.h refer_blocking`,
  the attacker's State read in the frame of the call and logged on `WO165-REPLAY … attacker_state=0x…`); a striking copy lets the engine
  judge the block (the victim's own State, zones and perfect-block window), its result replaces the host's verdict on the joiner and goes
  back to the host (W164 kind 4, as before). An older DLL ignores 0x80 (refers as before). 7 native checks, 1 agent test.
- **B3a — the copy's own block row:** when the host's engine blocked / parried / broke the joiner's blow and the host sent **no reaction
  of its own** (react ≠ queued — a queued reaction's row already reaches the copy through the host's capture, WO-151), the joiner's agent
  plays the NPC's own row: `combat_action_block` action 6 (blocked / parried), 8 (block broken) or `combat_action_perfect_block` action 14
  (pb), an NPC row (never a `player` row) whose weapon tags are exactly the copy's own (from the last swing row it played), the blow's
  zone preferred, then the lowest tags and GUID (every machine picks the same). `mp_copy_blocks` (joiner, default **on**).
  `WO176-COPYBLOCK npc=… verdict=… row="…"|none why=…`. 2 agent tests (the choice over a literal catalog, the never-twice rule).
- **B3b — the attacker's recoil:** unchanged WO-165 C3 (`mp_block_recoil`): a blocked / perfect-blocked outcome on the joiner plays the
  copy's `combat_action_failed_attack` row (action 27 / 15) chosen by the swing's weapon; with B2 the outcome is the joiner's own engine's.
  On the host, nothing is forced on a real NPC (its own engine decides).

### B1 — real hits against armed enemies [L][K]

**Setup.** A copy of `322` (Henry with a sword, `playline4/autosave922`), the camp of `tapo_kasparBandit_1..7` (armed: a weapon in the
right hand, `WO143-HANDS … R=1fc42528-…`). A camp bandit ignores Henry until he strikes it, and walks back to its camp when moved, so each
engagement: a fresh load, the mod's own pair lever (`mp_w163_probe pair <bandit> on 1`, WO-165 P6's explicit hostile pair), Henry's
sword drawn by **key 1**, then Henry's **own blows with the left mouse button** (real input, the game in front, no camera moves), the
bandit and Henry put on open ground 100 m from the camp for a one-on-one (in the first three engagements the camp joined in). Henry's
health was topped up by the harness (`player.soul:SetState("health", 100)` from an in-game timer below 70 — Henry never blocks and a
bandit lands ~25 blows a minute); nothing of the bandits' own rules was touched. Verdicts read from the record the engine built
(`WO176-HOSTHIT`), the bandits' blows on Henry from `WO176-HITME`.

**(a) the host's own blows** — 7 engagements, 301 left-button clicks, **45 blows the engine judged**:

| verdict | hit | blocked | perfect block (parried) | block broken | blocked + parried |
|---|---|---|---|---|---|
| host, real mouse attacks | 13 | 17 | 14 | 1 | **31 / 45 = 68.9 %** |

The bandits' attack rate at Henry: ~18–29 blows a minute per bandit one-on-one (48, 31 and 30 blows in ~100 s engagements; 218 in all).

**(b) the joiner's blows through `mp_real_hits`** — the synthetic joiner's figure beside the bandit on the same ground, its swing row
(longsword stab, heavy) then its attributed hit every 2–2.5 s. **Not stageable on this machine:** in four attempts the bandit never
fought the figure — not with the figure alone (WO-121's skirmish add and WO-136's retarget both ran: `WO121-HITS attributed … skirmish
add (override 1)`, `WO136-TARGET … taken`), not after the host's pair lever, not after Henry's own blows started its fight. It stood in
**Idle (State 0x1)**, `victim-cannot-react`. Found why: the figure's body is spawned from a local soul for its face, keyed by the partner's
name — here always `tzel_man_10`, **a Zelejov villager** — so to the engine the partner is a villager, and a camp bandit has no quarrel
with them (WO-173 saw the same camp sit idle).

| (b) attempt | engine verdicts | blocked / parried | raw fallbacks (why) | the bandit's blows on the figure |
|---|---|---|---|---|
| figure alone | 7 hit | 0 | 3 (2 dead, 1 replay refused) | 0 |
| + pair lever on the host | 8 hit | 0 | 32 no-swing-window | 1 |
| + Henry beside the figure, sword out | 28 hit | 0 | 5 (4 dead, 1 no window) | 0 |
| + Henry's own first blows | 29 hit (11 reactions queued → State 0x40) | 0 | 5 | 0 |
| **all** | **72 hit** | **0** | 45 (+14 `no-swing-row`: a harness artefact — a reused agent dropped the new peer's rows) | **1** |

Every blow went exactly one way (`applied=dup` 0), no knock-out from one blow (0), no new fault (the only counted faults are WO-160's
known `wo137::set_send_callback` agent-disconnect scan).

**The WO's checks:** blocked + parried of (b) within ±15 points of (a): **not met** (0 % vs 68.9 % — but (b)'s bandit never fought, so
the numbers do not compare one engine against the other). The bandit's attack rate at the figure ≥ 70 % of its rate at Henry: **not
met** (1 blow in all vs ~25 a minute). Every blow applied once, no knock-out from one blow, 0 faults: **met**.

**`mp_real_hits`: ON in 0.50.0 (the maintainer's decision, 2026-10-12).** The first 0.50.0 build shipped it off under the WO's rule
(*"on stays only if it passes"*); the maintainer then asked for it on by default and a rebuild of 0.50.0 (the second build below). So
the joiner's blows on the host's NPCs go through the host game's own hit processor; any refusal still takes WO-121's raw path with one
blow's stamina capped at 35 % (never a knock-out from one blow); `mp_real_hits off` (host console) turns it off. The real blocker for a
real partner is the figure's villager soul, not the engine path.

### B2 — your own block decides an enemy's blow on your screen [L][K]

Joiner role (`KCDMP_TEST_JOINER_IN_WORLD=1`, a synthetic host), Henry (sword drawn by key 1) facing one armed copy 2.2 m away (a host
road-event bandit's stand-in, a longsword in hand via the host's hand rows), engaged on the player (`WO132-ENGAGE … skirmish vs the local
player added`); 20 swing rows (longsword stab, heavy; the blow lands 1.233 s after the row) 4 s apart, the host's verdict at the row's own
hit time; `mp_victim_decides on`. The player's input triggered on the joiner agent's own per-swing line (`MP-ACTION section=inbound
kind=NpcAttack npc=…`, written as the row starts).

| case (20 swings) | the player's input | engine verdict | replays run (copy Striking: `attacker_state=0x8`) | the player's State | block zone | expected |
|---|---|---|---|---|---|---|
| 1 no block | — | **hit 20 / 20** | 20 | Idle 0x1 | — | hit ≥ 18 ✔ |
| 2 block held | right button down at the row's start, held 1.9 s | **hit 20 / 20** | 20 | **ParryInPlace 0x100** (20/20) | **−1 (unset)** | blocked ≥ 18 ✘ |
| 2b the same, copy held in front (not circling) + the pair lever | as 2 | hit 20 / 20 | 20 | 0x100 | −1 | ✘ |
| 3 perfect block | right button pressed 150 ms before the row's hit time, 250 ms | **hit 19**, 1 referred (the copy had left its window) | 19 | 0x100 (19/19), PerfectBlockState 0 | −1 | pb ≥ 18 ✘ |

Every blow applied once (`applied=engine` 20/20, 20/20, 19 + 1 host verdict; dup 0), the outcome went back to the host every time
(the synthetic host got 20 `hit-outcome` W164 messages per case), the player was never knocked down, 0 faults.

**What it shows.** The mechanism works up to the last step: with the copy Striking in its WO-166 window the player's held block becomes
the engine's own **ParryInPlace** (WO-165 P3's failure is gone), and the replay runs instead of being referred. The engine then still
judges `hit` because of the **zone test**: the player's BlockZoneId stays −1 — a block zone is only set against the player's
**Opponent**, and the joiner's player never gets the copy as their Opponent (`opp=0x0` on every read, also with the host-style explicit
pair; the copy is a paused puppet, not a fighting brain). The next step is the player's lock-on onto a copy (the Opponent link) — a
design decision, not built (writing the player's block zone from the copy's attack zone would be our code judging the block).

**Built because of it:** a blocking player whose block zone is unset is still referred to the host's verdict (`refer_blocking`: zone
−1 or unreadable → referral; 3 native checks). **`mp_victim_decides`: OFF** (the WO: on only if it passes; it did not), and with the
referral "on" can never make a held block worse than 0.47.x.

**The game's own prompts** in the striking window (screenshots at the window's middle, swings 2 / 8 / 14 of cases 1–2): **no** block or
riposte prompt appeared (the swing is visible, the HUD shows the drawn sword; no combat lock-on, consistent with `opp=0x0`).

### B3 — blocked outcomes shown on both screens [L][unit]

| | live | default |
|---|---|---|
| (a) the host's NPC blocked / parried / broke the joiner's blow, no host reaction coming | **20/20 + 6/6** `WO176-COPYBLOCK … row="CombatBlockGen, l_longsword+r_longsword+…" why=ok` (blocked), `CombatBlockPerfectGen …` (pb), `CombatBlockBrokenGen …` (broken) — the copy's own weapon rows, NPC rows only | `mp_copy_blocks` **on** |
| (b) the enemy's blow the joiner blocked → the attacker recoils on the joiner's screen | **12/12 + 6/6** `WO165-RECOIL … outcome=blocked recoil="CombatAttackFailed, l_longsword+r_longsword+aZ5+oppMale" why=ok` / `outcome=pb recoil="CombatAttackFailedPB, …"` (driven by the host's verdict: B2 never produced an engine block); on the host nothing is forced on a real NPC | `mp_block_recoil` **on** (unchanged) |
| the rows play, no T-pose | the game's own screenshots 0.35 s into each: the copy in its recoil (sword thrown up overhead) and in its own block (sword raised in front), a normal pose (`docs/wo176-shots/b3-copy-recoil.jpg`, `b3-copy-own-block.jpg`). No animation-state reader by name exists in the DLL: the "rows play" evidence is the dispatch result plus the screenshots | — |
| FIGHTSNAP p90 in the test fights | 61 fight windows: resume **4.3 cm**, post-hold **10.5 cm** (WO-173 recorded none; the last synthetic baseline, WO-165: 4.86 / 12.19 cm) — not worse | — |

## Frame rate [L]

A same-machine A/B against the 0.49.2 build (its own DLL, pak, agent and relay from `release\c0492`), the same spots, the window
minimised (the game's background limiter: ~25–35 fps), no input: the main menu (idle 135 s after the injection), Tachov (`922`, idle
135 s), and the 3-copy joiner fight (WO-167's recipe: three armed copies circling the player, a swing every 1.5 s, 140 s). Numbers: the
DLL's own `FRAME` lines (60 s windows): fps / the DLL's own cost per frame (`ours_us_mean`).

| | 0.49.2 | 0.50.0 first build | 0.50.0 shipped |
|---|---|---|---|
| menu | 34.5 fps / 436 µs | 34.6 / 447 | **34.6 / 442** |
| town | 27.0 / 601–619 | 27.1 / 599–627 | **27.1 / 596–620** |
| 3-copy fight | 25.2–25.5 / 692–700 | 24.9–25.3 / **1,151–1,158** | **24.9–25.7 / 698–703** |

**Found and fixed on the way:** the first 0.50.0 build cost ~460 µs a frame more in the fight. The per-task meter (`mp_main_cost on`)
put it on `wo176::tick`: A1's "already in it" check used `npcscan::for_each_in_radius`, which walks **every entity of the level**
(~7 ms a walk) twice a second. Now: the walk runs only in the host's world (the agent's role, op 23 — a joiner never runs an arranged
fight), every 4 s until a story actor is seen, then twice a second for 30 s; the Lua brain-pause query every 3 s until one is seen.
On the host near a figure the idle walk still costs ~7 ms every 4 s (≈ 1.75 ms a second). The frame rate is within 1–2 % of 0.49.2
in all three (the WO's 10 %). The menu could be sampled this time (the DLL's frame meter runs there).

## The input log (the maintainer's input policy, 2026-10-11)

Every event was sent by `tools/dev-input/game_input.py` (new, dev-only, beside WO-175's `send_key.py`; never shipped, never called by
shipped code): send only (no hooks, no key-state polling, no raw-input registration, nothing of the user's input read), the game's own
window brought to the front only for an input test and minimised after it, **the foreground checked before every single event**, every
event logged to the git-ignored `tools/dev-input/keys.log` (what, when, which test). Screenshots only through the game's own
`r_GetScreenShot`. The game's key bindings were read, never changed.

| sent this WO (all to the focused game) | count | tests |
|---|---|---|
| key **1** (draw, weapon slot 1) down / up | 29 / 29 | A2, A1 (×3), B1, B2 |
| left mouse button (attack) down / up | 307 / 333 (ups include the end-of-test releases) | B1(a) (301 attacks), the mouse probe, B1(b)'s start |
| right mouse button (block) down / up | 80 / 109 | B2 block / perfect |
| relative mouse moves (attack direction) | 80 | the first two B1(a) runs only, then dropped (they turned Henry off target) |
| the game window to the front / minimised | 29 / 25 | — |
| **refused** (foreground not the game — all input stopped) | **3** | below |

**Three refusals, two of which matter — said plainly:**
1. 22:09:06 — two harness loops ran at once (a stopped background task's child script survived); one minimised the game between the
   other's left-button down and up. The down had gone to the focused game; the up was refused. Both loops were killed.
2. **22:19:36 — a left-button down was sent while the game was the foreground window, and the foreground then became the Claude desktop
   app.** The cursor was most likely over that app's window (outside the game's window area), so the click reached it. The up was refused
   and all input stopped. **Most likely a breach of the policy's rule 2** (nothing but the game is touched): one button-down reached another app's
   window. A check of the foreground window alone is not enough for mouse buttons.
3. **22:21:30 — the same with the cursor now on the game's centre: the foreground became `BsSndRpt64.exe`**, the crash reporter of the
   game that had crashed at 22:15:35 (below), whose dialog sat over the game; the down most likely reached that dialog. The up was refused,
   input stopped; that reporter (spawned by the crashed game this session had launched) was terminated — no report was sent.

**Fixed in the tool after (2) and (3):** before every button event the cursor is put on the centre of the game's own window (its rect is
the only thing read) **and the window under that point must belong to the game** (else nothing is sent and input stops); a test starts by
releasing both buttons, and only while the game is in front. After the fix: 0 refusals over the rest of B1(a), B1(b) and B2.

## Incidents

- **A game crash, 22:15:35** (the game's own BugSplat report + `KCD246.trace`, Modding Tools build, our DLL `KCDMP_wo176e`): the main
  thread hung (`PIPE: … still waiting on the main thread past 3500ms`) right after `GameLoadFinished` of a `wh_sys_LoadGame` that the
  B1(a) harness issued while the previous engagement's bandit was still hitting Henry. Not reproduced; cause not determined. The harness
  then always ended a fight (pair off, Henry 350 m away) before any load: no further crash in 40+ loads.
- **Native logs of the 22:18–23:20 session (B1(a), B1(b), B2) were overwritten** by the next injection (the mirror log is truncated per
  injection, not rotated). Every number above was read from that log before it was lost (the harness's JSON records in
  `tools/wo118/logs/run176.*.json` and the counts quoted here).

## Untested, plainly

- **Two real players: everything.** The partner was the synthetic peer throughout.
- A1: the figure's passive membership in the story fight (the engine's own add, from the figure's villager soul) is not removed; the fix
  (a figure body from a soul with no village) was not made. The flee-and-pulled-back case was not re-created live (the S4 repair of
  that saved state was, A2). Replaying `322` → Procek's dialogue → the travel cutscene → the meadow polylog with real input was not
  done: the engine's own first-frame save of the fight (`323`) was used.
- A3: S1 against the live game (no free playline on this machine).
- B1(b): a bandit that fights the partner — not stageable here (above); the ±15-point and ≥70 % attack-rate checks are open.
- B2: a block counted by the joiner's own engine needs the copy as the player's Opponent (lock-on onto a copy) — not built.
- B3: the rows' animation state was not read by name (no reader in the DLL); the evidence is the dispatch result and two screenshots.
- The crash at 22:15:35 (a load during a running fight): cause not determined.

## Gates and the installer

Full record: `docs/WO-176-installer.md`. `release\KingdomComeTogether-Setup-0.50.0.exe`, **106,949,330 bytes**, sha256
`9e4ee30fc730d3b6093a8b8f2f2f1766949c62920762596fa6e7225541b1c021`, header **0.50.0**, unsigned, soak waived (quoted); built from a
fresh clone of `f00aef7`. Inside the build: agent 1,512 · native 9,364 · 61 Lua suites / 4,049 checks · relay 63 · setup 77 · logpack 14
· save tools 14 + 7 · guards 7/7 · no-input gate **0 hits** in our files · installer cases 26/26, 41/41 · **the live save round trip
PASS** (the game was up). All at least 0.49.2's counts.

## Housekeeping

- Saves: the whole folder backed up before the first launch (826 files, SHA-256 each); at the end **826/826 byte-identical** (0 missing,
  0 changed, 0 added). This WO's 30 test saves in `playline4` (copies of `322`, `323`, `335`, the repaired copy, and the game's own
  autosaves of the throwaway) were moved out to the session's scratch folder; `playline4` holds only the launcher's `quicksave022.whs`
  again (unchanged). Nothing was saved into a real slot; no playline above 4 was used.
- The installed `Mods\kdcmp` (four files) backed up and restored: hashes equal.
- This session's 22 screenshots moved out of the game's screenshot folder; three small crops are kept in `docs/wo176-shots/`.
- The maintainer's own launcher relay (7778 / 5273) and master server were left running and untouched.
