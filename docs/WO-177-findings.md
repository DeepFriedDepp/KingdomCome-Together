# WO-177 — the partner's figure gets its own soul, the second weapon-lock variant, small cleanups, the joiner's pickups, corpses and stations

Unattended, one machine: the Modding Tools game (`playline4` copies only), a local relay on its own port, the synthetic peer
(`tools/wo118`) as the partner, the real agent as host or joiner. **No version bump and no installer** (the maintainer's instruction for this
WO; `VERSION` stays 0.50.0). Evidence marks: [L] live in the game, [syn] synthetic (the real `kdcmp.lua` under MoonSharp), [unit], [native],
[F] read from files. Screens: `docs/wo177-shots/`. Saves: the whole saves folder was copied byte-for-byte (592 files, 978,975,444 bytes,
sha256 manifest) before anything, and every test file lived in `playline4`.

## In short

| item | result | default |
|---|---|---|
| A1 the figure's own soul | **built, live-proven** [L]: twelve mod-owned souls (no village, no group, an empty brain, the faction `cutsceneOnly`, social class 13); the meadow fight's figure memberships 28 → 1 (the one the engine adds at the spawn, taken out at once), fighters turning on it 8 → 0; a village brawl does not turn on it; a save made with a figure present loads and draws without the mod | ON (`mp_figure_soul`) |
| A2 the second weapon-lock variant | **reproduced and narrowed, not named** [L][F]: the lock is in the *file* a rest save writes in that world (a fresh process loading it first is refused; the pristine file draws); not in the quest data, the souls, Henry's record, the contexts or the field `0x1300`; what is left is the `GameState` entity stream, which cannot be spliced alone. **No repair shipped**, no plain Lua save exists to switch to | — |
| A3 launcher banner | removed (the first-run notice and its call; the setting stays readable) | — |
| A4 native logs rotate | **built, live-checked** [native 5 checks][L]: `kcdmp-native.1..5.log` and the mirror's; the first line of the new log says what was kept; the log bundle collects them | ON |
| A5 no load in a fight | **built, live-checked** [unit 13][syn 11][L]: every load the agent issues asks the game first and waits (fight, or a hit within 10 s); live, the repaired-copy load waited 8.7 s behind a hit and then loaded; 3 minutes → not issued, the player is told | ON |
| B1 pickups | **found live and fixed**: the avatar places one item at a time — two drops in one tick left ONE item on the ground (the first placement was replaced), the field's *"one apple of two"*; now one placement is in flight at a time. Plus the exact amount on the ask and a message while the host answers. "E does nothing" for the host's drops and the axe beside a corpse: **not reproduced** (no logs); the owner/steal hypothesis was checked and is **not** the cause | ON |
| B2 corpses agree | **built, live-checked with the synthetic peer** [syn 17][unit 6][L]: the joiner asks the host about his dead copies, the host answers 1 / 2 / 3 / 0, an answer 2 hides the joiner's corpse (the entity record stays, hidden and inactive), the host tells the joiners unasked when its corpse goes | ON (`mp_corpse_sync`) |
| B3 stations | **partly**: a working NPC's tool swing is no damage on a figure [syn 8][unit 10]. A figure holding a station for the NPCs, and the joiner's station not locked as a paused copy: **not built** (needs a station's smart-object state names and a two-player session) | ON (`mp_tool_swing`) |
| B4 never stuck at a station | **built, partly live-checked** [syn 7][L]: `mp_unstuck` step 1 aborts every AI action of the player (`AI.AbortAction`), ends the player's interactive state (`player.player:OnEndInteractive`), `AbortUse` on interactive objects within 4 m, then `StandUp`; step 2 (a second press within 10 s) adds the game's own hit reaction with no damage. The calls run without error in the game; a real station use was **not** staged | ON |
| B5 enemy animals | **pocketed**, what it needs is written down below | — |

Gates: see the last section.

## A1 — the partner's figure gets its own soul [L][syn]

**Why.** WO-176 found the figure's body was spawned from a Zelejov villager's soul (`face pick … soul=tzel_man_10`): the engine treated the
partner as that villager (a village fight adopted it, a quest that takes "every soul of both groups" did too).

**What was learned about souls** (probes in the running game, 2026-10-12): `SpawnEntity` with a `SharedSoulGuid` makes a **new soul per
body** (its own `Guid`) from that guid as a **template**; the body's face and first clothes are a function of the template (two bodies of one
template look alike, another template looks different, `docs/wo177-shots/a1-two-templates-two-looks.jpg`). A template row can live in the
mod's own pak: `Libs/Tables/rpg/soul__kcdmp.xml` is read like the mod's other table rows. `SoulArchetypeName` alone (no template) also spawns
a body, with `SharedSoulGuid` all zeros — not used.

**Built.**
- `kdcmp/Data/Libs/Tables/rpg/soul__kcdmp.xml`: 12 templates `kdcmp_partner_1..12` (guids `4b43444d-7301-4d67-b1a5-9e2f6d8c0a01..0c`): brain
  `00000000-0000-0000-0000-000000045000` ("Default": **no sub-brain** in `brain2subbrain.xml`, it plans nothing — the figure stays a puppet; the
  spawn then logs `NPC … doesn't have scheduler subbrain`, expected), faction `cutsceneOnly` (the game's own "souls for cutscenes only": no
  children, no relations), social class 13 "player" (crime role 0: not a witness, not an authority), archetype 0 (NPC), no `skald_character_name`.
  Read back from the game: the body's `FactionNode/Parent` is `cutsceneOnly` (a top-level faction), its social class `player`.
- `kdcmp.lua`: `KCD2MP_PickFigureFace` picks one of the twelve by the partner's name (the same one every time, like the old roster);
  `mp_figure_soul on|off` (default **on**) — off = the old village roster. `tools/Build-And-Install-Mod.ps1` packs the table.
- The look: the appearance pipeline is untouched (outfit by item, WO-9/WO-144); a figure without an outfit from the partner is bare either way
  (`a1-figure-new-soul.jpg` vs `a1-figure-old-village-soul.jpg`, the synthetic partner sends none).

**Checks.** Host role, `autosave923` (the engine's own save on the first frame of the meadow fight), the synthetic partner's figure 2.5 m from
Henry inside it, 100 s; counts from `kcd.log`:

| | 0.50.0 (village soul, guard on) | new soul, guard on | new soul, guard off |
|---|---|---|---|
| `SoulAdded on <figure>` | 28 | **1** (at the spawn) | 1 |
| fighters turning on the figure (`target <figure>`) | 8 | **0** | 2 |
| the mod taking it out of the skirmish | 8 | 1 | 2 |
| verdict | `Won` | `SkirmishVictory` on Henry's side | `SkirmishVictory` |

So the engine still adds a *new* soul to a running skirmish when it connects (once, at the spawn: that is how a skirmish treats any soul
that appears inside it), but nothing re-adds it and nobody turns on it. **The WO's literal "0 figure members" holds after the mod's removal
at the spawn, not at the instant of the spawn.** The WO-176 keep-out (`mp_arranged_guard`) stays and is still what produces the 0 / 0: with it
off the new soul alone leaves 2 fighters targeting it.

The village brawl (`autosave317`, the neighbours' brawl `rvacka_neighbour_1/2` beside Henry, the figure 3 m away, 85 s): the figure was added
once (spawn) and taken out once, **0** fighters targeted it, the brawlers fought each other (39 skirmish events, none on the figure).

**Save safety** [L]. A save made while a figure existed (`autosave365`, `kcd2mp_2` in it) was loaded in the game **without the mod** (the mod's
folder moved out of `Mods\`; the engine loads every folder in `Mods\`, a renamed one too): it loaded, the weapons drew (scripted draw), the only
trace is the `[NPCManager] Can't find the NPC 'kcd2mp_2' …` warning that 0.50.0's own logs already carry for any saved figure. The twelve
template ids do not appear in the file as text; the figure's soul and entity are the same records as before this WO.

**Real key 1** [L]: on the new build, `autosave360` → rest save → reload, the weapon holstered, key **1** sent to the game window (the maintainer's input
policy: foreground-checked, send only, logged): `IsWeaponDrawn` false → **true**.

**Untested.** The key-1 draw on the meadow fight's own save after a reload (the Lua draw drew on `365`); two real players; a partner whose name hashes onto the same template as the
other partner (the same look twice — harmless).

## A2 — the second weapon-lock variant [L][F]

**Reproduced** (fresh game sessions each time, the mod's pak and no DLL/agent; the scripted draw = holster, 1.5 s, `DrawWeapon`, read `IsWeaponDrawn`):

| sequence | result |
|---|---|
| load `autosave922` (copy of the field's `322`) | draws |
| …then reload `922` (no save) | **draws** |
| …`Game.SaveGameViaResting()` → load the file it wrote (`autosave368`) | **REFUSED**, and the refusal then survives every later load in that process (360, 922 …) |
| the same without drawing before the save | **REFUSED** |
| a **fresh process** that loads `368` first | **REFUSED** — the lock is in the *file* |
| `Game.AutoSave()` (guard on and off), `Game.QuickSave()` | wrote **no file** on this build — there is no plain Lua save to switch to |

So in that world the resting save *is* the trigger, and the file it writes carries the lock. **S4 finds nothing**: the quest XML of the field's quest
`konecZabomysichValek` is byte-identical in `922` and `368` (accepted, no `souboj_na_poli`, no `_skirmishResult`); no arranged-fight / forced-unarmed
context on Henry (`HasScriptContext` false for all six candidate names, also after the refusal); no fight, no scene; 400 m from any brawl it is
still refused; `mp_repair_combat` ends at "equipped huntingSwordBasic but it does not draw".

**Bisect** (a scratch tool, never shipped, writes the pristine `922` with one node of `368` swapped in, re-deflated and re-signed; one fresh game
process per file, the file loaded first, scripted draw):

| node taken from the bad file | result |
|---|---|
| ConceptModule (`730a`, all quest XML) | draws |
| RPGModule souls (`7308/3529`, every soul incl. Henry's) | draws |
| RPGModule `352d` (Henry's perks / buffs / inventory) | draws |
| RPGModule `352c` + `352e` | draws |
| the bad file + the missing Henry-soul field `0x1300` put back (the one structural difference in Henry's record) | **still refused** — that field is not the cause |
| `GameState` alone (the CryAction entity-state stream, 13 KB smaller in the bad file) | **does not load cleanly**: a crash dump in one run, an endless loading screen in another (a stream whose entities do not match the souls) — cannot be isolated this way |
| `TerrainState` + `Timer` (a few KB) | not run |

What is left, then, is the `GameState` stream (or a combination with another node; `TerrainState` and `Timer` were not tested): the lock is written into the entity state, not into any
quest, soul or context the mod can read. A 4.5-minute round per file kept the bisect from going into the stream itself.

**Decision.** The state is **not named** and nothing in this WO claims a repair. The mod's own rest saves are unchanged (they pass the live
round-trip gate on `autosave360`); the gate stays on `360` and records that `922` fails in the game itself. The honest advice in the
checklist: if a joined world's weapons refuse to draw after a world save, `mp_repair_combat` first; if it cannot free them, load the older
save and tell us the save's name (`mark_odd`).

## A3 — the launcher's banner

`Wo175FirstRunNotice` and its call are gone; the setting stays so an older `settings.json` still reads. RESTORE SAVES keeps its own text,
`QUICKSTART.md` keeps "Your saves".

## A4 — native logs rotate [native][L]

`native/KCDMP/log.h`: on the first line of an injection `kcdmp-native.log` and `kcdmp-native.mirror.log` are shifted to `….1.log` … `….5.log`
(the oldest dropped, nothing truncated) before the new ones open; the first line of the new log says how many were kept, or that a log still
held open by a running game could not be moved. `LogBundle.Gather` collects `kcdmp-native.1..5.log` and `kcdmp-native.mirror.1..5.log`.
Native unit: names, 7 runs on real files (oldest dropped), mirror numbering separate. **Live**: the second injection of this session wrote
`kcdmp-native.mirror.1.log` (the previous log, 73 KB, untouched) and the new log began `LOG: earlier native logs kept as kcdmp-native.1..5.log
(primary 1 kept, mirror 1 kept)`.

## A5 — no load while a fight is running [unit][syn][L]

Every load goes through one Lua function and four agent call sites (join from a world, leave, exit-to-menu, the repaired copy's load). Each now
calls `Wo177WaitQuietAsync(why)`: one question a second (`KCD2MP_W177LoadQuiet` → the event `w177_load_quiet <why> ok|fight|hit`); **fight** =
this Henry or a figure in the game's own combat flags, **hit** = this Henry or a figure lost health in the last 10 s (read where the emit loop
already reads health); an unanswered question counts as not quiet (never as ok); one toast ("A fight is on: the load waits until it is
over."); 3 minutes → the load is not issued, the player is told, nothing is loaded. At the main menu there is no world: ok at once.
`MP-W177 load why=… waiting reason=… / go after … ms / NOT issued`. **Live**: `mp_w177_status` read `load_reason=fight` on the meadow fight's first
frame; on `autosave935` (a stuck field save) a hit window set 1 s after the load made the agent's repaired-copy load wait —
`MP-W177 load why=w175-repaired waiting reason=hit` … `go after 8722 ms (it was waiting for: hit)` — and then load the repaired copy, which checked healthy.

## B1 — picking up world items [L][syn]

The WO-134 mechanism (the joiner asks the host, class + position, `ok` / `gone` / `unknown`, ItemGone to the others) is what a joiner's pickup of
a world item already is; drops (WO-48, both directions) are a separate path. What was found and changed:

- **Found live: the avatar places ONE item at a time.** Two `PlaceItem` calls in the same tick (two same-class drops arriving together, e.g. the
  host dropping two apples) left **one** entity on the ground: `ITEM-SYNC drop 999001 FAILED: placed entity never appeared`, `999002 materialized`
  (an entity of amount 1, not 2). That is the field's *"one of two apples; both gone for the host"*: the receiver got one apple, the other was never
  there. Fixed in `mp_item_materialize`: finalize what is placing first, then start **one** pending drop; the next waits a tick. The synthetic
  D2c models the engine (the entity is minted a tick later, a second placement replaces the first): the previous Lua fails it
  (`ground/resolved`), this one passes, solo / joiner / host. (An entity another tracked drop already holds is also never adopted again; kept.)
- **The amount rides on the ask** (an optional 7th field of `w134_item`, the 6th of `LootAsk ItemTake`; append-only) and the host prefers the
  nearest item *of that amount* inside the match radius; it logs both (`amount joiner=2 host=2`). [syn P1–P6]
- **A pending ask is no longer silent**: a second press while the host's answer is out shows "Asking the host…" and a log line.
- **Checked live and ruled out: "the item belongs to the avatar, so E is a steal".** A drop the mod places through an avatar's `PlaceItem` reads
  back `GetOwnerId` = none, `CanSteal` = false, `CanPickUp` = true. (`CanUse` read false for it and for ordinary world items alike at that moment —
  it depends on the interactor's state — so it says nothing.)
- **Not reproduced: "E does nothing" for the host's drops and for the axe beside a corpse.** No logs came with the report and a real E press by
  the joiner (two machines, the input tool) was not run. The remaining suspects are the ones the checklist asks the testers to report with
  `mark_odd`: what the on-screen prompt read.

## B2 — corpses agree after deaths and respawns [syn][unit][L]

Each engine disposes of bodies on its own clock, so the joiner's world keeps bodies the host's no longer has. Built (W164 kinds 10 and 11,
append-only, no protocol bump; `mp_corpse_sync`, default on):
- the joiner, every 5 s in a shared world (the chain ticks every 2 s), asks the host about the dead NPC copies within 60 m of him (≤ 3 per
  question, `name:x:y`, never the same one twice in 20 s); the host answers per name: **1** a corpse there too, **2** gone from its world (the NPC
  is not there and the spot is within 60 m of the host's own player), **3** alive there, **0** cannot say; only **2** removes, only a dead copy, and
  it is said (`WO177-CORPSE gone on the host: removed here`);
- the host also tells every joiner unasked when a corpse it was watching within 55 m of its player goes (checked every 2 s);
- a load re-arms the chain and forgets what was asked; mod entities and excluded names (the quest layer's) are never touched.

**Live** (the real game and agent, the synthetic peer as the other side, `ttac_alsik` — a corpse 43 m from Henry in the `935` copy):
*joiner role* — the question reached the peer (`SYNTH got W164 … kind=unknown-10 ttac_alsik:1999:2530`, the peer's build only names kinds ≤ 9); at
t = 30 s the peer sent `ttac_alsik=2`: `WO177-CORPSE gone on the host: removed here npc=ttac_alsik ok=true`; the entity then reads **hidden and inactive**
(`RemoveEntity` on a world NPC leaves its record) — so the census treats a hidden corpse as gone and never asks about it again (both rules are
pinned synthetically). *Host role* — the peer asked `ttac_alsik:1999:2530 tzel_nobody:2000:2490 tzel_faraway:200:200` and got
`ttac_alsik=1 tzel_nobody=2 tzel_faraway=0`, exactly the design. **Not run with two machines**, and the "after a joiner respawn the lists match"
case only follows from the chain (it asks again after every load).
Synthetic C1–C15 + C7b / C10b / C13b, unit 6.

## B3 — stations

Built: **a working NPC's tool swing is no damage on a figure.** The host's Lua lists, every 2 s, the NPCs within 10 m of any figure that are
not in a fight (no combat mode, no combat danger) as `w177_calm … ! <names that are in a fight>`; the agent keeps the calm names 5 s and
forwards no hit by a calm NPC (`MP-W177 tool swing: … NOT forwarded; the avatar's health was put back`); a bandit, a wolf, a guard in a fight
are in combat mode when they swing and nothing changes for them; a missile is never a tool; never listed = a fight's blow (fail open).
`mp_tool_swing on|off`, default on. Synthetic T1–T7, unit 10.

**Not built** (and why): (1) the figure *holding* a station so NPC routines leave it alone: the game has smart-object state calls
(`AI.SetSmartObjectState`, `ModifySmartObjectStates`) but the state names of an anvil, a campfire pot and a wash tub are not known, and the
effect needs a second player at a station to see (the stations are `Smithery`, `Grindstone`, `WaterTubeActionTrigger`, `KettleActionTrigger`, `FireplaceSmartObject` classes); (2) the joiner's own station "not locked as a paused copy": a station is a
world object, not an NPC; what the tester saw (a pan and pot unusable) needs a log from that machine to name — it may be the same
owner-means-steal rule as B1.

## B4 — never stuck at a station [syn][L]

`KCD2MP_W177EndStation` (called by `mp_unstuck`): found live that the stations around Tachov are **ActionTrigger classes**
(`WaterTubeActionTrigger`, `Smithery`, `Grindstone`, `BedTrigger`, `KettleActionTrigger`, `FoodProcessingTrigger`, a bench's `ActionTrigger`) and
that no entity within 150 m has the `InteractiveObjectEx` script's `AbortUse`; the player's use of a station is an AI action and a player-level
"interactive" state. So step 1 now calls the game's own `AI.AbortAction(player)` (every AI action of the player), `player.player:OnEndInteractive()`
(the player's interactive end; `OnBedInterrupt`, `OnBedStop`, `IsSitting` exist beside it), `AbortUse` on any interactive object within 4 m, then
`StandUp` as before — named in the log (`WO177-STATION …`, `station-end(…)` in the `WO151-UNSTUCK` line); **step 2** (a second press within 10 s, which
already put him beside his partner) also adds the game's own hit reaction with no damage — the interrupt that freed the tester (a friendly-fire
knockdown). Live: both presses ran in the game (`soft: seat-reset,picker-back,stand-up`, then `hard: …,beside-partner,…,hit-reaction`), the two new
calls return without error. Synthetic U1–U6 + U3b. **Not run at a real station** (no scripted way to start a station use was found).

## B5 — enemy animals in a fight [pocketed]

A hostile animal (a bandit's dog, wolves) would have to stream like an enemy NPC. What it needs: (1) the NPC tracker's class filter
(`mp_npc_rescan`) widened to the animal classes, with the animals' own names excluded from the human-only paths; (2) the native writer
(per-frame position/rotation write, WO-116/118) working on the animals' skeletons (it reads a human's); (3) the attack rows of the animals in
`ActionRowCatalog` (the dog's bite rows) and the swing-cue path for non-humans; (4) their hits on players through the WO-121 chokepoint (an
animal's `C_CombatSoul` is the same class) and their deaths through the corpse path of B2. More than the 25 minutes of the box, and it needs
a live animal fight to check each piece.

## Gates

| gate | 0.50.0 | now |
|---|---|---|
| agent unit | 1,512 | **1,545** |
| native unit | 9,364 | **9,389** |
| Lua synthetic suites / checks | 61 / 4,049 | **62 / 4,133** (all green; WO-175 L7 and WO-154 B4 expectations updated for the new timers and step 2) |
| relay / setup / logpack / farkle | 63 / 77 / 14 / 59 | 63 / 77 / 14 / 59 |
| save tools (python) | 14 + 7 | 21 OK |
| native guards (no raw `__try`, no pointer `build_argument(`) | 7/7 | 7/7 |
| no-input gate over our own files (agent, protocol, relay, native, the pak's Lua, the launcher's pages and models) | 0 hits | **0 hits** (1,757 files; the one named exemption is the .NET runtime's `System.Console.dll`). The input tool stayed in `tools/dev-input/` |
| the live save round trip (`tools/Test-WO175SaveRoundTrip.py`, start save `autosave360`) | PASS | **PASS** (`DRAWN` before and after the save and reload; the new file `0 stuck fight(s)`). On `922` it fails in the game itself (A2) |
| a real key press after the reload (`tools/dev-input/send_key.py`, key **1**, the game window only, foreground-checked) | — | holstered → **drawn** |
| the launcher builds (`dotnet build KCDMP_launcher`) | — | succeeded |

**Frame rate** [L] (`fab.sh`: menu, Tachov idle, the 3-copy joiner fight; the window minimised, no input; the DLL's own `FRAME` and `MAIN-COST` lines).
The two builds were run back to back, but the harness left the window in a different state for each (the first run unthrottled, the second in the
game's ~26 fps background limiter), so the absolute fps of the pair cannot be compared. What can:

| | menu fps | town fps (3 windows) | fight fps (3) | `MAIN-COST ours_us_mean` in the fight |
|---|---|---|---|---|
| 0.50.0, run before this one, **same window state as the new build** (last night) | 34.4 / 34.6 | 11.7 / 21.8 / 27.1 | 26.1 / 25.7 / 25.3 | 709–726 |
| this build | **34.4 / 34.6** | **11.8 / 22.2 / 27.2** | **26.2 / 25.7 / 25.5** | **696–702** |
| 0.50.0 today, unthrottled window (not comparable in fps) | 178 / 180 | 53.8 / 62.0 / 62.1 | 66.5 / 69.9 / 70.5 | 645–646 |

Like for like the new build is within **2 %** of last night's 0.50.0 in every window, and the DLL's own per-frame cost is the same (696–702 µs against
709–726 µs); the same-day unthrottled baseline shows +8 % in that one number (646 → 700 µs, inside the 10 %), a difference that also separates
two runs of one build in different window states. No fault in any window. The new Lua chains (the corpse census 2 s, the calm list only with a
figure, the load gate only on a question) are the whole of this build's per-frame difference.

**The installer**: not built, by the maintainer's instruction for this WO (no version bump, no `Setup.exe`); nothing was pushed to a release.

## Untested, plainly

- Two real players: everything. The partner was the synthetic peer; B2 was only run with stubbed entities.
- A1: key 1 after a reload on the meadow fight's own save (it was run on `360`); a pak without the soul table (a mixed install) — `mp_figure_soul off` is the opt-out, nothing detects it; a figure of the same template as another partner.
- A2: the state itself (not named); whether the game's own autosaves, sleep saves and manual saves of that world are also affected (only
  the mod's rest-save path could be produced).
- A4: a game still running while the next injection starts (the held log cannot be moved: said in the new log, not fixed).
- A5: the wait inside a real join/leave (the Lua question is live-checked; the agent's loop is unit-pinned for its rules only).
- B1: a real E press by the joiner at a host drop (needs the input tool and two machines); why "E does nothing" for the host's drops and the axe beside a corpse (no logs, not reproduced).
- B3: the figure holding a station; the joiner's station as a "paused copy".
- B4: a real station use.
