# WO-160 — The joiner's NPC copies: one root cause, and the rest of the 2026-10-07 tester findings

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

Marks: **[L]** live, solo, on this machine's Modding Tools build (1.5.5), a throwaway playline only (every playline
backed up first, checked after every run); **[L-field]** read in the testers' logs of 2026-10-07 (0.45.8, both machines);
**[syn]** / **[unit]** / **[native]** a test that gates the build; **[code]** read in our code; **[disasm]** read in the
game's own machine code (XGenAIModule, by its strings and RTTI, never by a fixed address); **[not tested]** no run. No
names, paths or addresses of players are in this file. The version is the maintainer's (0.46.0); no tag, nothing uploaded.

Field notes 0.45.6–0.45.8: `research/testing_findings.md` (private, not in git; moved there from the repo root in WO-161).

## The answer

Every planner error in the joiner's log was on a body this mod had placed (60 of 60 NPCs), and the placement was the
bug: it cleared the loaded state of the body's NPC context, which took the body's shop, area labels and contexts off
the loaded state, so the game's planner dismantled them; and it asked the planner for a way out of a work activity that
the planner does not have. Both are fixed (DLL: the loaded state is no longer cleared; mod + agent: the body's stance
and unstance are released the game's own way first). Solo, on 13 bodies in a town: shipped build **2 placed, 11
refused, all three shopkeepers lost their shop element**; this build **13 placed, no error line, every shop kept**,
across a real 8-hour skip too. The false "You loaded your own save" at join (a stale "World" in an agent that outlived a
crashed game) cannot fire without the mod's own recent word that a world is loaded and 60 s after any join activity. A
partner's conversation now stands the NPC still on the other machine.

**Second pass (the maintainer's correction: naked NPCs were never a problem before, they went away when the host reloaded,
so it is our code; and "the joiner cannot talk to traders or NPCs" is the primary goal).** Right on both. It is the same
fault as the dead talks, found by building the field's state solo (§1.8): a copy is **paused**, so it never runs the NPC's
own morning routine; through a night or a wait it keeps the night the game put it to (the `sleepUnequip` undress, the
sleeping contexts, the sleep buff), and our placement stood it up *in that state* — naked, and a talk to it never started
(`CanTalk` false, no dialogue request at all). Separately, a placement the planner **refused** left the loaded state holding
the demand it could not meet, and a talk resumes the brain, which tries to meet it: the field's 20-second waits (reproduced:
a seated-by-refusal copy, no dialogue in 24 s). Both are fixed in the DLL (§1.8): a body shown awake whose state still holds
the sleep undress is placed from a cleared state with no equipment element, so the planner takes the sleep extras off and
dresses it; a refused placement makes the loaded state say what the body is. Solo, 8 of 8 night sleepers were placed
seated, **dressed, no extras, no planner line** (before: 8 of 8 naked), and a talk to the innkeeper and the woodworker
copies started in **0.4 s** (before: no dialogue at all); four refused copies all took a talk in 0.4 s (before: one of two
never did). **Not proven with two players** — the testers' items 150, 153 and 154 are the proof.

| # | Item | Result | Mark |
|---|---|---|---|
| 1 | The copy's torn context, the naked NPC, the dead talks | torn context fixed (§1.4); **naked NPC fixed in the DLL (§1.8)**; refused placements no longer leave an unreachable demand (§1.8); two-player proof pending | [L][native]; [syn][unit] for §1.4 |
| 2 | False "own save" at join | gated on the mod's own proof + 60 s quiet; real own-save still fires | [syn][unit]; [not tested] live join |
| 3 | The NPC in conversation keeps walking | the non-talking machine holds it (host freezes, joiner holds the copy) | [L] both halves; [syn][unit]; [not tested] two players |
| 4 | Items 2–8 of the 0.45.6–0.45.8 notes | all four fixes are in this build, with their log lines | [code] |
| 5 | WO-158 leads | herb clip proxy, mount gate, stop-time re-pulse built; QuickLoad probed: no-go for the join | [L] clip accepted / QuickLoad; [syn][unit][native]; [not tested] visual, live |
| 6 | The joiner's own horse | purchase told to the host, whistle fetches it (placement, not a gallop) | [syn][unit]; [not tested] live |

## 0. What the logs showed (numbers verified, not redone)

Joiner session 1 (`kcd.log`, 54 session minutes): 2,478 `Couldn't find actions to get NPC into game loaded state`
(host: **0**), 3,684 `Execution of N actions from load counldn't reach the loaded state in M updates`, 3,214
`Animation-queue overflow` (host: 10), 13 `WO157-TALK free` / `WO137-TALK resume`, of which **5 started** (0.1–0.2 s after
the request) and **8 never started** (5 waited the engine's 20.0–20.1 s, 1 the mod's own 25 s, 2 refused in the same
frame by the request fallback). The mod's own apply is in the native log: 13,341 `WO141-APPLY` lines, **5,308 logged
applies with `exec 0`** (the game refused) against 3,008 with `exec 1`; the pace logs only the first three tries and then
one a minute, so these are logged applies, not all of them. **All 60 NPCs with an error line were NPCs the mod had applied
an activity to**; none was a body the mod never touched [L-field]. Per session minute the refusals run 131, 61, 63, 50 ...
in the first ten minutes and come back in bursts (minute 24–27: 75, 71; minute 48: 176), with the overflow bursts at minutes
30–32, 36, 48–50 and 54 [L-field]. Session 2 joiner: `WO124-SESSION` and `WO140-SEPARATE "You loaded your own save…"`
at the same instant (mod t=27.973), three toasts in 160 ms, the join's own load 20 s later (`MP-JOIN joiner: ... loaded`).
Host (both sessions): `WO137-HOLD off … why=talk held_s=23.0 exec=was-not-paused` for the blacksmith and the innkeeper
(also 29–30 s for `ttkc_man_11`, 97.1 s for the barbora): the host's NPC was never paused for a joiner's talk
[L-field].

## 1. The copy's torn context

### 1.1 Probe (a): what resets, what sets the loaded state — answered [L][disasm]

* **The game's `wh_ai_NPCStateResetElement` knows 15 element-type names and can reset exactly two.** Asked live for each
  name: `Stance`, `Unstance`, `LeftHand`, `RightHand`, `Minigame`, `ChangeEquipment`, `ItemSetAside`,
  `ChangeBehaviorState`, `AddContext`, `AddLink`, `EnableBehaviorForNPC`, `ChangeAreaLabel`, `HorseUsage`, `OpenShop`,
  `EnableRoleOnHub` (and the enum's `MandatoryElementCount` sentinel) are known; any other name answers `Unknown element
  type '…' specified`. **Only `Stance` and `Unstance` do anything**; the other 13 answer `Unsupported element type to
  reset!` [L]. The handler (XGenAIModule, found by its string; one function of 4.6 KB) takes the element type's enum value,
  `0` runs three calls on the NPC's human body (set stance standing, default stance data), `3` one call (set the default
  unstance), anything else prints the refusal [disasm]. It is a reset of the **human's** stance and unstance (the state
  elements follow it), not of the NPC context: live, resetting both on a seated scribe left every other element where it
  was [L].
* **The talk reset's `LeftHand` and `RightHand` never did anything.** All 26 of them in session 1's joiner log are followed
  by `[Error] [NPCStateDebug]:Unsupported element type to reset!` while the mod's own line said `LeftHand=ok,RightHand=ok`
  (`pcall` succeeded; the command printed the refusal) [L-field]. Removed; the line now says what was reset.
* **No context-level reset, no "set loaded state = idle" path.** `ForceIdleState` (the log's `ForceIdleState sets …`)
  is EntityModule's `C_IdleState` animation-tag setter, not the NPC context [code]. What the planner holds is two states
  per body, laid out the same way: the **current** state (`ctx+0x90`) and the **loaded** state (`ctx+0x180`), each a vector
  of element slots: fixed slots `[0]` Stance, `[1]` LeftHand, `[2]` RightHand, `[3]` Unstance, `[4]` ChangeEquipment,
  `[5]` Minigame, then the body's other elements in the order they were added (area labels, script contexts, links, the
  behaviour state, the shop, the metarole) — 6 to 33 slots on the NPCs probed [L]. The loaded state is the image the
  game restores at a load; on an untouched body it mirrors the current one slot for slot (scribe, woodworker, a lounger:
  same classes, same count) [L]. Its **`Clear` (slot 7) cuts the vector back to the six fixed slots** (a woodworker's 12
  slots became 6) [L], which is what the shipped placement called before writing its own stance, unstance, hands and
  equipment.

### 1.2 Probe (b): is placement the cause? — yes, only placement [L][L-field]

* In the field: 60 of 60 error NPCs were apply targets (above).
* Solo, on a copy that was only paused, reset and resumed (no placement), the game's own dialogue request started in 0.5 s
  and printed no planner line [L].

### 1.3 Probe (c): the solo reproduction [L]

A throwaway playline (a copy of a test save by the village), 13 NPCs near the player with their own work or seat
(woodworker with the saw at the debarking bench, a hammerer, two lumberjacks, the scribe at his desk, a leaner, a
sunbather, three shopkeepers, ...), each paused as a copy is, each asked for the host's activity "sitting on bench N":

| Build | Placed | Refused | Elements the body lost | Mark |
|---|---|---|---|---|
| shipped 0.45.8 (loaded state cleared, activity + equipment written) | **2 / 13** | 11 (one `Couldn't find actions` each) | the three shopkeepers' **shop element** (18, 24 and 14 → 0 extras), also the area labels and contexts of the others | [L] |
| loaded state kept (research mode 4), no release | 1 / 13 | 12 | none (shops kept) | [L] |
| mode 4 + the stance/unstance released the game's way first | **13 / 13** | **0** | none (all 13 extras counts kept, the shops kept) | [L] |
| the same, **the shipped code path** (Lua release + the DLL's reconcile, default mode) | **13 / 13** | **0** | none | [L] |

Two more findings, both from the solo runs:

* **The planner cannot leave a work activity.** From the woodworker's state (saw in the right hand, the debarking
  unstance) it refuses every target, **including the empty one** (`Req state: ''`), with `NPC state search failed: can't
  find a path from actions`; a leaning body, a sunbather, the scribe fail the same way. Standing bodies are placed.
  Releasing the unstance first (the game's own reset) is what makes the target reachable [L].
* **A stale save-time hand is an unreachable demand.** With the loaded state kept but not cleaned, a lumberjack's loaded
  `LeftHand log_cut` (a log long gone) made the plan impossible. The loaded build now writes every fixed slot itself: the
  activity's, the host's hands, else what the body holds, else nothing.

### 1.4 What was built

* **DLL (`wo141.cpp`)**: the loaded state is **not cleared** any more (mode 4, the default; modes 0–3 stay as `mode <n>`
  research switches); slot 0 and 3 are the activity's, slots 1 and 2 the host's hands, else the body's, else empty, slot 4
  the body's own equipment, slot 5 empty; the body's other elements are the game's own and stay. A body the planner has
  refused eight times with one activity is **not asked again** for 300 s (a new host activity starts the count again;
  `kMissesBeforeGiveUp`, `kGiveUpRetryS`, with the 15 s retries it replaces in `wo141_rules.h`). One `WO160-CTX npc=… loaded=mode4
  extras_on_body=N exec=E try=T refusals=R` line per placement log and a `WO160-CTX npc=… gave_up …` line when it stops. Nothing
  here adds an address, a vtable slot or a hook: the same resolved anchors, RTTI-checked.
* **Mod (Lua)**: `KCD2MP_W160Release(name, why)` — the game's own reset of the stance and the unstance (the reset WO-118
  has issued at every puppet start since 0.28.3), never on a corpse, a talker, a body in a dialogue or a cutscene
  (`WO160-REL`; `mp_ctx_release on|off`, default on). The talk reset is two elements, honestly logged. `mp_conv_hold` and
  the rest are in §3.
* **Agent**: before it hands the DLL a host row for an NPC copy that was **placed before** and whose activity **changed**,
  it releases the copy (awaited: the console call has run before the pipe op does); a repeated row of the same activity
  (the host's 10 s refresh) asks for nothing. It counts the planner's failure lines **per NPC** from the engine's own
  log lines (`[NPCContext]:Couldn't find actions…`, `…counldn't reach the loaded state…`), prints
  `WO160-CTX npc=… placed=… released=… loaded=kept cleared=no errors_before=… errors_after=… (+N in 6 s)` per copy and
  treatment, and `MP-WO160-STATS planner refused=… unreached=… npcs=… worst=[…]` once a minute while there is anything to
  say.
* **After a time skip** (this player's own sleep or wait ended, or the host's announced skip was applied) every copy the
  joiner holds is released and placed again (`WO160-CTX settle after …: N copies`), after three seconds for the game's
  own skip sim, at most 80, at most one settle in five seconds.
* **The naked NPC was left open in the first pass** (it was pocketed as "needs the host's equipment on the wire"); the
  maintainer's correction was right and it is built in §1.8 without any new wire data.

### 1.5 The crash this work found, and what it changed [L]

An earlier variant released the stance and unstance **by emptying the two slots in the state itself** (no game call).
It placed 13 of 13 as well, and **crashed the game five minutes after the 13 bodies were resumed**: `Validation of
expected results for action seller_In … 'wh::xgenaimodule::NPCState::ChangeUnstanceEffect' failed` on the seller's own
brain, then `Fatal error: 224 (BAD_PROGRAMMER), Pure function call`. Shipped path = the game's own reset (field-proven since
WO-118) **soaked 15 minutes with the same 13 bodies resumed: alive, no fatal error, no validation failure** [L]. The state
slots are never emptied by this mod.

### 1.6 Talks [L]

Through the mod's own `BasicAIActions.OnTalk` wrap on puppeted copies (the stream a scripted host), the dialogue started in
**0.40–0.49 s on 7 of 9 copies**, among them three whose stream was walking away at 1.4 m/s (the copy dragged 5–12 m). The
two that did not start: `ttkc_man_4`, a town guard on duty (his state is `guard_normal` + a guard-post behaviour: the game's
own refusal), and `ttkc_man_11` dragged away at 45–50 m. Those were **standing, never-placed copies**: the first pass did not
reproduce the 20-second waits because it did not test the field's states. §1.8 does, and finds them.

### 1.7 Acceptance

| Criterion | Result | Mark |
|---|---|---|
| Solo: planner error rate on a puppeted town ~0 after the fix | 13 placements, 0 refusals; a second wave on bodies the brains had moved: 3 `…counldn't reach…` lines on the tool users, no refusal, all in step | [L] |
| ...and stays there across a time skip | a real 8 h sleep skip (clock 551,245 → 580,104 s) with 13 placed copies paused: **0** planner lines during and after, **0** in the 8 s after the settle, **0** in the next 40 s | [L] |
| Scripted talk on a shopkeeper copy starts in < 2 s | 0.40–0.49 s on 7 copies (shopkeepers `ttkc_man_11`'s stall mate, the innkeeper among them) | [L] |
| Solo (§1.8): copies left in the night state through a wait: placed awake, dressed, talked to | 8 of 8 placed seated and dressed, 0 planner lines; the innkeeper and the woodworker: dialogue in 0.4 s | [L] |
| Solo (§1.8): copies whose placement was refused: a talk starts | 4 of 4 in 0.4 s (one of two never started before) | [L] |
| Live: a joiner sells to a keeper the host just traded with; no "can't talk to you right now" for an idle keeper; overflow < 50 per hour for copies; no naked NPC after a wait or a sleep | testers (items 150, 153, 154) | [not tested] |

### 1.8 Second pass: the night state, the refused demand, and what the field's failed talks were

**What the field's failed talks looked like.** Of the 15 never-started joiner talks in the three 0.45.8-era bundles, the last
planner line for the NPC before the talk shows **the sleep undress in both the current and the loaded state** for 6
(`ttkc_man_23`, `ttac_procek`, `ttac_blacksmith` twice, `tzel_vavrinec` twice: `ChangeEquipment … Equipment preset filter:
sleepUnequip / sleepSoldierUnequip … Outfitting mode: Unequip`) and **an empty current state against a demand it could not
reach** for 7 more (`ttac_blacksmith` ×2, `ttkc_inkeeper` ×2, `ttkc_woman_2`, `tvez_kocour` ×2; `Current state ''`, the
loaded state a forging or a tool use) [L-field]. The native log has the cause for the blacksmith: at 16:48 the copy was lying
on a bed in step with the host's; at 17:01 the host's blacksmith worked and the copy was asked for `blacksmith_forging` with
a tool in hand, **refused** (`exec 0`) every 1.5 s, and stayed lying [L-field].

**Reproduced solo** (a throwaway playline; the world waited to midnight with the game's own wait, so the sleepers lie in their
beds with the night's state; unpaused controls woken by a further 5 h wait are dressed again and carry no sleep elements, while
the **paused copies stay undressed with every night element** — eight of eight) [L]:

| State of the copy | What the shipped placement did | Mark |
|---|---|---|
| lying, `sleepUnequip`, 3 sleeping contexts + the sleep buff; the host says "awake, standing" | stood up **naked** with the night extras kept (kept by design since WO-144 4.5); `CanTalk` false even resumed for 4 s (`IdleToMove`, `Agent is stuck`); a talk through the mod's wrap: `OnTalk` and **no dialogue request at all** | [L] |
| placed with an activity the planner **refuses** (a work activity from a seat, tools), no release | the loaded state keeps the refused demand; the next resume of the brain tries to meet it; a copy: **no dialogue in 24 s** | [L] |

**What was built (DLL only, `wo141.cpp`, `wo141_rules.h`; no new address, slot or hook):**

* **Wake.** A body shown awake in the host's world (the host's activity is not lying) whose *current* state still holds the
  game's sleep undress — decided by the game's own text of the state (the function the research verbs already use, found by its
  string): an equipment element with `Equipment preset filter: sleep…` **and** `Outfitting mode: Unequip` — is placed from a
  **cleared loaded state with no equipment element**. The planner then removes the sleeping contexts and the buff and dresses
  the body (the controls end the same way). Guard armour (`guard_normal`, `Equip`), a party outfit, the bath (`spaBath`) are
  not sleep undresses and are left alone (native test). It runs from the reconcile as well as from the apply: a host row of
  **"none"** (a walker, an idler) asked the game for nothing, so a naked copy stayed naked for the whole session; now the body
  is not "in step" while it is still in the night's undress. The outfit text is read once per equipment element (the game's text
  function leaks ~100 B a call), at most every 5 s per body. `WO160-WAKE npc=… thread=…` once a body a minute; `mode`-independent;
  research verb `wake 0|1`; status line `woke_dressed N`.
* **Neutral on refusal.** Every refused placement (`exec 0`) makes the loaded state say what the body is (its own current
  activity, hands and equipment): no unreachable demand stays on a body, so a resumed brain has nothing to do and a talk starts.
  The host's activity stays wanted and is asked again, paced, until the 8-refusal give-up of §1.4. `WO160-NEUTRAL npc=…` once a
  body a minute; status line `neutral N`.

**Solo results with the final DLL** [L]:

| Test | Result |
|---|---|
| 8 night sleepers (naked, lying) given "sitting on bench N" through the shipped path (pause, Lua release, DLL reconcile) | **8 / 8 placed, `eq=-` (dressed), no extras, 0 planner lines** |
| 4 night sleepers given "awake, standing" | dressed in one go; innkeeper and woodworker copies: **dialogue in 0.40 s** (the innkeeper's is the game's own closed-at-night line) |
| 13 daytime bodies through the shipped path (the §1.3 test) with the wake code in | 13 / 13, every shop kept, 0 planner lines, **0 wake triggers** (no day copy was taken for a sleeper) |
| 4 daytime copies with a **refused** placement, then a talk through the mod's wrap | **4 / 4 dialogues in 0.40 s** (the build before: the scribe, no dialogue in 24 s) |
| The talk matrix on a clean daytime world: untouched standing, placed OK + the mod's talk, placed OK + a plain request, refused | 8 / 8 start in 0.4 s except the one refused copy of the build before neutral |

**Findings that are not about this build.** (1) The first-pass talk acceptance used never-placed copies and a world the
experiments had not yet worn out; after many night skips and cancelled dialogues in one session even untouched copies stopped
taking a talk (restart cleared it) — treat solo talk numbers from a long session as void. (2) A 16 h wait makes WO-137's
quest reader fault ~11.8 k times (guarded, counted, three sites, `wo137::set_send_callback` in the log) — **the same count on
the session-start build**, so it is not this WO's; pocketed. (3) The `Game.QuickLoad()` and herb items are unchanged.

**The sword.** The report ("the host sees an enemy with a sword, the joiner sees none"; item 9.2 of the earlier notes, never
investigated) is **not fixed and not investigated as its own item**. What §1.8 changes for it: the draw on a copy
(`human:DrawWeapon`, WO-40) draws what the copy's *equipment state* holds, and a copy left in `sleepUnequip` /
`sleepSoldierUnequip` has had its weapons put away in the stash; the wake path returns the default outfit. **Unverified**: the
solo harness has no way to see a weapon (`IsWeaponDrawn` answers true on an unarmed copy; the inventory's `entity` field does
not change), so whether a woken soldier shows its sword needs eyes. The checklist's item 160 asks for it.

## 2. The false "You loaded your own save" at join

* **What the logs showed.** In session 2 the joiner's agent had outlived its game: the first game ended without a quit
  line (the last of its Gameplay-started lines is 17:39:49; the log has no `CSystem::Quit`), the launcher started a new
  one, and the agent's `_where` stayed **World** while the new process was still booting (its main-menu line came 22 s
  after the verdict: `MP-JOIN joiner: the main menu is up` 17:42:16). The host connected at 17:41:53; the verdict
  (`connected from its own world -- NOT joined (separate)`, 17:41:54.649) and the "your host is in a shared world, quit and
  start again" line (17:41:54.715) were both issued from that stale value, ~20 s before the join's load began
  (`join … requested` 17:42:40). Sync, quests and the leash were fine [L-field].
* **Fix.** The own-world verdict now needs **the mod's own, recent "world" answer** (`KCD2MP_Wo124Where`, ≤ 12 s old, asked
  again every 5 s while the verdict or its precondition holds) and **no join activity in the last 60 s** (`Wo160Rules.SeparateVerdictAllowed`);
  the "quit and start again" line needs the same proof; the mod refuses the toast itself unless it reads **world**
  (`WO160-SEPARATE held at the menu` / `… at the loading`). A verdict shown stays until the next tick sees a join in flight
  or a fresh non-world answer, then `KCD2MP_W140Separate(false)` clears it. The real own-save case (checklist item 40:
  loading a save, then CONNECT) is a **world** answer with no join activity: it fires at once.
* **Tests.** Synthetic S1–S7: a session-mode packet at the menu, then the agent's verdict → no toast; during a load → none;
  in a loaded world → it fires; cleared when the join lands. Unit: the rule at the 27.97 s of the field, at 59.9 s, 60 s, with
  no confirmation, with the proof aged past 12 s. **[syn][unit]; not tested live** (needs a second machine and a crashed
  game).

## 3. The NPC in conversation keeps walking on the other screen

* **What the logs showed.** Joiner talks: the host's hold logged `exec=was-not-paused` (WO-151 3.6 made a hold block-only),
  so the host's NPC walked on for the whole talk; host talks: WO-138's `paused: dialogue` freezes the stream and the joiner's
  copy carried on with its last gait [L-field].
* **Built.** *Joiner talks:* the host's hold now **freezes** the NPC for a partner's **talk** (`wh_ai_PauseNPC`), ends with the
  talk's own end event (and in `heldMaxS`, 300 s, at the latest); a guard's stop stays block-only; the block-only fallback is
  logged with its reason (`exec=block-only(why=w139-stop,hold_freeze=off)`). *Host talks:* the host's `OnTalk` wrap tells the
  joiner (`w160_conv`, a new host kind of the existing `QuestHost` message: **kind 6 `Converse "<on|off> <npc>"`**, appended
  after the five that shipped); the joiner's copy gets no Lua write and its native writer is held every second until the host's
  conversation ends (the NPC's own `IsInDialog`, 3 s out; 25 s if it never started; 5 min cap on the joiner). `mp_conv_hold on|off`,
  default on. No position is written by anything for a held NPC, so there is no new fight.
* **Solo results [L].** The joiner half: a copy streamed walking away at 1.4 m/s, held by `KCD2MP_W160Conversation(true)`: moved **0.000 m in 4 s and 0.000 m in the next
  4 s** while the stream went 21.6 m ahead, `MotionIdle`; released: it follows again (the ring snaps it to the stream). The host
  half: a real walking NPC (`ttkc_man_25`, 6.4 m in 2 s) put on hold with `why=talk`: **0.00 m in 4 s and 0.00 m in the next 4 s**;
  released: 8.4 m in 3 s.
* **Tests.** Synthetic C1–C23 (host talk start/end, 25 s and 3 s rules, the copy held / released / max time, release-all,
  freeze vs block-only vs the switch); unit: kind 6's name and the text round trip with hostile names refused. **Not tested with two
  players** (item 152).

## 4. On main since 0.45.8, in this build, to be checked live

| Item | Commit | Log line that proves it |
|---|---|---|
| Recap sound (`audio_setup_video`) and skip (movement-detected) | `5d058af` | kcd.log `WO159-RECAP sound setup on … / off`; the world's sound normal after |
| Joiner doors: the privacy flag only; unlocks only within 300 s of his own lockpick | `366349d` | `mp_door_sync` status `privacy_skipped`, `own_unlocks`; no `WO151-DOOR ask … unlocked here` he did not make |
| Trespass HUD gated at `SetTrespassState` itself | `e0ea21e` | native log `WO139-BUILD trespass HUD gate ARMED … (the HUD's own refresh)` |
| "Ready to sell" context for a keeper the host's shop is open at | `41c0d68` | agent `MP-W139 joiner: <npc> ready to sell here … set` (host: `CrimeHostShops = 8`); with §1 the copy keeps its shop element and its keeper's context |

All four commits are ancestors of this build's head and their strings are in the built DLL and pak [code]. Nothing about
them was run live in this WO.

## 5. The WO-158 leads

* **Herb proxy.** The avatar never plays the minigame's own fragment (`PickingHerbs`, a player fragment: the camera-bone
  selector, MasterSlave context 5 on `Dude`; WO-153). It plays the herbalist NPCs' own **plain behaviour loop**,
  `animations/humans/male/behavior/herbs_picking_area_loop.caf` (in `male_general.dba`; `_in` and `_out` beside it) through the
  ghost's ordinary `StartAnimation`, the locomotion loop held off meanwhile; when it ends the DLL's walk-class pulse runs again
  (§5.3). `mp_avatar_herbs` is the switch and **defaults on** again. Solo: the clip is accepted and 8 s of it printed **no camera
  or context-5 line** [L]; **whether it visibly bends is not known**: a paused NPC's mannequin shows no layer-0 clip at all (head
  height unchanged), so the solo run could not see it [not tested]. `Wo143Rules.AvatarShow` returns null for herbs, switch or not.
* **Mount gate (Lua).** Before an avatar's `ForceMount` the horse is polled every 0.25 s for up to 5 s (a real position, not hidden,
  active, within 25 m of the rider, the rider's body present); a horse not ready then is refused with its reason (`WO160-MOUNT
  id=… refused after 5.1 s: horse-hidden`) and tried again by the existing 3 s retry; `ready after N s` is logged when it waited.
  The claim is **fewer refused mounts**, not "fixes the mount crash". [syn] M1–M8; **[not tested live]** (needs an avatar and a horse).
* **Stop-time re-pulse (native).** WO-155's walk-class pulse (a freshly bound avatar stands in a T-pose until something moves it)
  now also runs, once and only if the body is still, when an avatar's one-shot ends and when the mod stops a clip on an avatar (a
  new `AvatarEvent` kind 2 through the existing pipe op): `WO160-NUDGE body=…`. [native build, no test of its own; **not tested live**]
* **`Game.QuickLoad()` probe: NO-GO as the join's load.** It takes no argument and loads the **newest QuickSave of the profile's
  current playline**: in a loaded world it re-read the throwaway's `quicksave022` in 9 s; **from the main menu it works too, and loaded
  the first playline's quicksave — a real playline** (read only; the game was switched to the throwaway at once and the real
  playline's files were checked unchanged) [L]. For the join to use it, the host's spliced world would have to be written as the
  newest QuickSave of the player's own playline: a write into a real slot and a shadowing of his own quicksave. `wh_sys_LoadGame
  <playline> <name>` stays the join's load. No implementation.

## 6. The joiner's own horse (0.45.1 pair)

* **What the logs showed.** The whistle is only an emote: `MP-W151 emote out whistle` on the joiner, `emote in from ghost 0:
  whistle` on the host, `WO151-WHISTLE ghost=0 played at its avatar (v_horse_whistle)`. Nothing moves a horse. On the joiner the
  bought horse (`tsem_horseForSale_2`) is a paused copy of the host's stream, and in the host's world the trader still owns it
  [L-field].
* **Probe (b), `wh_ai_PlayerHorseSchedulerProxy`:** it is a **cvar holding one entity name** ("Name of entity to be used as the
  player's horse's scheduler proxy") [code]: it cannot serve a second rider.
* **Built.** (a) The joiner's own game says which horse is his (`player.player:GetHorseId()`, read every 5 s from the session
  tick); a new one is told to the host (`QuestAsk` kind 4 `OwnHorse "<horse>"`, appended after the three that shipped) and the host's
  world marks it that peer's (`WO160-HORSE marked …`). (b) When the host receives that joiner's whistle (the emote it already
  receives) the horse is **placed 7 m beside his avatar** (a gallop is not available; refused with its reason when someone rides it,
  when the host's game has not loaded it — far from both players —, when his avatar is not there; left alone within 15 m).
  `mp_horse_fetch on|off`, default on. [syn] + [unit]; **[not tested live]**. Pocketed: the host-far or host-in-a-menu cases work
  by the same event path (a menu halts Lua timers, not an event's console call) but were not run.

## The tests and the frame rate

* Synthetic: **50 suites** (the 49 of 0.45.8 and `Test-WO160Synthetic`, 65 checks: S, R, T, C, H, M); four older suites changed for
  intended behaviour (WO-143 herb default, WO-151 hold, WO-157 talk reset and mount, none blind). Client **1,197**; native **413**;
  relay, setup, static checks, payload smoke and installer cases run inside the build.
* **Frame rate [L], the WO-148 rule** — the same throwaway save, window in front, the build at this session's start (before)
  against this build (after): **main menu 178.2 / 180.0 FPS → 178.3 / 180.0**; **town** (four stable one-minute windows after the
  load) **76.8–77.0 → 76.9–77.5 FPS**, the mod's own work per frame 533–538 µs → 521–526 µs. No change.

## The 0.46.0 build

**Second build (the current one, with the §1.8 DLL).** From a fresh clone of `e39caae` (`release\c0460b`) with the three
git-ignored start saves, the same `tools\Build-Installer.ps1 -SoakWaiver "The maintainer decided on 2026-10-07: no soak test
for 0.46.0."`: `release\KingdomComeTogether-Setup-0.46.0.exe`, 106,346,393 bytes, sha256
`8fdd24065edc7ae3948472793880637e84d6671c6f8d00283ef2a52499ffb508`. Local only; not tagged; unsigned. Inside the build: relay
62, agent 1,197, setup 77, native 421, **all 50 synthetic suites**, the static and native checks, the installer cases, the payload
smoke (`protocol=v10 release=0.46.0`); no FAIL line; payload 1,035 manifest entries, no player or machine name in the payload. The
first build (below) is superseded: it has the DLL without §1.8 and was kept beside it as `…superseded-first-build.exe`.

**First build.** From a fresh clone of `85fd56a` (`release\c0460`) with the three git-ignored start saves copied in,
  `tools\Build-Installer.ps1 -SoakWaiver "The maintainer decided on 2026-10-07: no soak test for 0.46.0."` (the standing
  rule for this release candidate, as for 0.45.8): `release\KingdomComeTogether-Setup-0.46.0.exe`, 106,355,863 bytes,
  sha256 `7cd7ba9e0f6eefa651c5f9a60a073454efc0a9204f858e7bf3b630841c1290a0`. Local only; not tagged; unsigned (no signing settings).
* Inside the build: relay 62, agent 1,197, setup 77, **all 50 synthetic suites** (`Test-WO160Synthetic` among them), the static
  and native checks, the installer cases, the payload smoke (`protocol=v10 release=0.46.0`); no FAIL line. Payload 1,035 manifest
  entries; the payload carries no player or machine name.
* One compiler warning is new and harmless: `GameBridge._w160StatsMs` is never used (left in; removing it would change the
  commit the installer was built from).

## Pocket list (outside this scope, recorded)

* The reverse of the wake path: a copy of a body that sleeps in the host's world but is dressed on the joiner (paused copies
  never undress; the host's sleeper shows dressed on the joiner's screen). Harmless; not built.
* WO-137's quest reader faults ~11.8 k times (guarded, counted) during a 16 h wait in solo; the same on the session-start build.
* The sword (§1.8): not investigated; probably the same state, unverified.
* A residual `…counldn't reach the loaded state…` line now and then on a tool user placed after its brain moved it (3 in a
  second wave of 13); nothing is dismantled by it.
* The release is still the game's debug reset through Lua (field-proven since WO-118); the native route is the handler's own
  human-body calls (§1.1), found by their string and RTTI, not built.
* Herb clip visibly bending, the mount gate, the T-pose re-pulse, the horse fetch, the own-save timing: all need two players.
* `_joinedWorld` is also kept by an agent after a crash; the verdict no longer reads it unconfirmed, nothing else was changed.
* From the WO's own list: the Cuman brawl and the Semine Moravians' flags (set pieces, Part B); Tomcat's earlier "kick"; a
  "standing dead copy" (checklist item: the moment); the 13-minute clock skew at the joiner's 17:26; the Troskowitz arrest was
  legitimate (a failed talk prompt plus a click is a punch); the sleeping-NPC desync, phantom weapon stance, swing with no
  combat and silent damage of the earlier notes were not investigated.

## Standing rules checked

Modding Tools build only; the DLL's changes add no fixed RVA, export or vtable slot (the placement reads the same anchors,
RTTI-checked, and a new hook is not used); thread ids are in every native log line; protocol changes are append-only (host kind 6,
ask kind 4); no Lua depends on a debug console command for anything new *except* the release, named above; saves: every playline
backed up before the first launch, a copy as the only playline used (a second look at the real one after the QuickLoad probe: files
and times unchanged); no keys, no mouse, no window moved (the game takes the foreground itself at launch); no quest NPC's quest
logic touched (the test NPCs' own activities only); no launcher setting, no consent flag, no antivirus change.
