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
| A2 the second weapon-lock variant | **reproduced, narrowed, not named** [L][F] — see the section: the state is not in the save's quest data, contexts, souls or entities; it is process state set by loading a rest-written save of that world; no plain Lua save exists on this build to switch to. What shipped is detection and the plain words, not a repair | — |
| A3 launcher banner | removed [unit-free: one notice + its call] | — |
| A4 native logs rotate | **built** [native 5 checks][L]: `kcdmp-native.1..5.log` (and the mirror's), the log bundle collects them | ON |
| A5 no load in a fight | **built** [unit 13][syn 11]: every load the agent issues asks the game first and waits (fight, or a hit within 10 s); 3 minutes → the load is not issued and the player is told | ON |
| B1 pickups | exact amount carried and matched; two same-class drops can no longer adopt one entity; a pending ask is never silent. The tester's "E does nothing" is **not reproduced** (no logs) — see the section | ON |
| B2 corpses agree | **built** [syn 15][unit 6]: the joiner asks the host about his dead copies, the host answers, a corpse the host's world lost goes everywhere | ON (`mp_corpse_sync`) |
| B3 stations | **partly**: a working NPC's tool swing is no damage on a figure [syn 8][unit 10]. A figure holding a station for the NPCs, and the joiner's station not locked as a paused copy: **not built** (needs a station's smart-object state names and a two-player session) | ON (`mp_tool_swing`) |
| B4 never stuck at a station | **built** [syn 6]: `mp_unstuck` step 1 calls the game's `AbortUse` on interactive objects within 4 m; step 2 (a second press within 10 s) adds the game's own hit reaction with no damage | ON |
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

**Untested.** Real key 1 after the reload (the Lua draw was used); two real players; a partner whose name hashes onto the same template as the
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

## A3 — the launcher's banner [unit-free]

`Wo175FirstRunNotice` and its call are gone; the setting stays so an older `settings.json` still reads. RESTORE SAVES keeps its own text,
`QUICKSTART.md` keeps "Your saves".

## A4 — native logs rotate [native][L]

`native/KCDMP/log.h`: on the first line of an injection `kcdmp-native.log` and `kcdmp-native.mirror.log` are shifted to `….1.log` … `….5.log`
(the oldest dropped, nothing truncated) before the new ones open; the first line of the new log says how many were kept, or that a log still
held open by a running game could not be moved. `LogBundle.Gather` collects `kcdmp-native.1..5.log` and `kcdmp-native.mirror.1..5.log`.
Native unit: names, 7 runs on real files (oldest dropped), mirror numbering separate. Live: see the gates.

## A5 — no load while a fight is running [unit][syn]

Every load goes through one Lua function and four agent call sites (join from a world, leave, exit-to-menu, the repaired copy's load). Each now
calls `Wo177WaitQuietAsync(why)`: one question a second (`KCD2MP_W177LoadQuiet` → the event `w177_load_quiet <why> ok|fight|hit`); **fight** =
this Henry or a figure in the game's own combat flags, **hit** = this Henry or a figure lost health in the last 10 s (read where the emit loop
already reads health); an unanswered question counts as not quiet (never as ok); one toast ("A fight is on: the load waits until it is
over."); 3 minutes → the load is not issued, the player is told, nothing is loaded. At the main menu there is no world: ok at once.
`MP-W177 load why=… waiting reason=… / go after … ms / NOT issued`.

## B1 — picking up world items

The WO-134 mechanism (the joiner asks the host, class + position, `ok` / `gone` / `unknown`, ItemGone to the others) is what a joiner's pickup
of a world item already is; drops (WO-48, both directions) are a separate path and were not changed in behaviour. Found and changed:

- **Two same-class drops placed within 3 m in one tick adopted ONE engine entity** (the pre-existing WO-48 edge case, 0.30.9 §1): the first
  pickup removed both items for the other player — the tester's *"one of two apples, both gone for the host"*. `mp_item_finalize` now never
  adopts an entity another tracked drop holds. The synthetic suite cannot reproduce it (its `PlaceItem` is immediate, the engine's is one tick
  late), so D2b only proves nothing else broke; the fix is code-verified.
- **The amount rides on the ask** (an optional 7th field of `w134_item`, the 6th of `LootAsk ItemTake`; append-only) and the host prefers the
  nearest item *of that amount* inside the match radius; it logs both amounts (`amount joiner=2 host=2`). [syn P1–P6]
- **A pending ask is no longer silent**: a second press while the host's answer is out shows "Asking the host…" and a log line.
- **Not reproduced: "E does nothing" for the host's drops and for the axe beside a corpse.** No logs came with the report. One hypothesis
  was found in the game's own script (`PickableItem:GetActions`): an item whose owner is another character is offered as a **steal (hold E)**
  and `OnUsed` then does nothing for a tap; a drop the mod places through an avatar's `PlaceItem` could carry the avatar as owner. Checked live
  in `B1 live` below.

## B2 — corpses agree after deaths and respawns [syn][unit]

Each engine disposes of bodies on its own clock, so the joiner's world keeps bodies the host's no longer has. Built (W164 kinds 10 and 11,
append-only, no protocol bump; `mp_corpse_sync`, default on):
- the joiner, every 5 s in a shared world, asks the host about the dead NPC copies within 60 m of him (≤ 3 per question, `name:x:y`, never the
  same one twice in 20 s); the host answers per name: 1 a corpse there too, 2 gone from its world (the NPC is not there and the spot is within
  60 m of the host's own player), 3 alive there, 0 cannot say; only **2** removes, only a dead copy, and it is said (`WO177-CORPSE gone on the
  host: removed here`);
- the host also tells every joiner unasked when a corpse it was watching within 55 m of its player goes (one line per removal);
- a load re-arms the chain and forgets what was asked; mod entities, excluded names (the quest layer's) are never touched.
Synthetic C1–C15 (the whole question/answer/removal/watch logic with stubbed entities), unit 6 (kinds, the text codec, the 80-character limit,
the join-channel framing). **Not run live with two machines.**

## B3 — stations

Built: **a working NPC's tool swing is no damage on a figure.** The host's Lua lists, every 2 s, the NPCs within 10 m of any figure that are
not in a fight (no combat mode, no combat danger) as `w177_calm … ! <names that are in a fight>`; the agent keeps the calm names 5 s and
forwards no hit by a calm NPC (`MP-W177 tool swing: … NOT forwarded; the avatar's health was put back`); a bandit, a wolf, a guard in a fight
are in combat mode when they swing and nothing changes for them; a missile is never a tool; never listed = a fight's blow (fail open).
`mp_tool_swing on|off`, default on. Synthetic T1–T7, unit 10.

**Not built** (and why): (1) the figure *holding* a station so NPC routines leave it alone: the game has smart-object state calls
(`AI.SetSmartObjectState`, `ModifySmartObjectStates`) but the state names of an anvil, a campfire pot and a wash tub are not known from the
docs, and the effect needs a second player at a station to see; (2) the joiner's own station "not locked as a paused copy": a station is a
world object, not an NPC; what the tester saw (a pan and pot unusable) needs a log from that machine to name — it may be the same
owner-means-steal rule as B1.

## B4 — never stuck at a station [syn]

`KCD2MP_W177EndStation` (called by `mp_unstuck`): `AbortUse` — the game's own end of an interactive object's use
(`InteractiveObjectEx:AbortUse`, read from the game's entity script) — on every interactive object within 4 m, named in the log
(`WO177-STATION`, and `station-end(<names>)` in the `WO151-UNSTUCK` line), then `StandUp` as before; and **step 2** (a second press within
10 s, which already put him beside his partner) now also adds the game's own hit reaction with no damage — the interrupt that freed the tester
(a friendly-fire knockdown). Synthetic U1–U6. **Not run live at a real station** (no scripted way to start a station use was found).

## B5 — enemy animals in a fight [pocketed]

A hostile animal (a bandit's dog, wolves) would have to stream like an enemy NPC. What it needs: (1) the NPC tracker's class filter
(`mp_npc_rescan`) widened to the animal classes, with the animals' own names excluded from the human-only paths; (2) the native writer
(per-frame position/rotation write, WO-116/118) working on the animals' skeletons (it reads a human's); (3) the attack rows of the animals in
`ActionRowCatalog` (the dog's bite rows) and the swing-cue path for non-humans; (4) their hits on players through the WO-121 chokepoint (an
animal's `C_CombatSoul` is the same class) and their deaths through the corpse path of B2. More than the 25 minutes of the box, and it needs
a live animal fight to check each piece.

## Gates

(filled in at the end of the WO)

## Untested, plainly

- Two real players: everything. The partner was the synthetic peer; B2 was only run with stubbed entities.
- A1: real key 1 after a reload; a figure of the same template as another partner.
- A2: the state itself (not named); whether the game's own autosaves, sleep saves and manual saves of that world are also affected (only
  the mod's rest-save path could be produced).
- A4: a game still running while the next injection starts (the held log cannot be moved: said in the new log, not fixed).
- A5: the wait inside a real join/leave (the Lua question is live-checked; the agent's loop is unit-pinned for its rules only).
- B1: a real E press by the joiner at a host drop (needs the input tool and two machines); the ownership hypothesis.
- B3: the figure holding a station; the joiner's station as a "paused copy".
- B4: a real station use.
