# WO-137 — Shared quests, step 1: findings

Kingdom Come: Together. Unofficial; not affiliated with or endorsed by Warhorse
Studios. Game dialogue and quest text are Warhorse's: they are cited here by
their IDs and paraphrased in a few words, never quoted.

Evidence marks: **(observed)** seen live in the game, **(synthetic)** the real
game against a synthetic peer or a scripted stand-in for one side,
**(code-verified)** read in the code or the game's data but not run,
**(inconclusive)** tried, no clear answer.

## The answer

**All quests are shared in the host's world, both ways, and the joiner can
talk to NPCs.** Both directions run on the game's own quest machinery:

* **Host → joiner.** The host's DLL sees every quest State change in the
  host's world at the engine's own setter (real changes only, in the engine's
  order). The agent sends each one to the joiner, whose DLL applies it through
  the State's own `Set<Value>` port — the same edge the game fires when a
  player makes that step, so the joiner's own journal, HUD notice, markers
  and comments follow. **(observed)** — the host's bandit-corpse step raised
  the "Find Mutt!" update on the joiner (frames 1, 2); the host's main-quest
  step (M05, the smith line) moved the joiner's M05 objective.
* **Joiner → host.** The joiner's own steps (a conversation's outcome, an
  examined object, a timer) go to the host as requests. The host judges each
  against its own value — already there: counted once; at the joiner's
  starting value: applied through the same port; anything else: refused and
  the joiner put back — and an applied request comes back to every joiner as
  ordinary changes. **(observed)** — five joiner steps applied on the real
  host, one counted once, one refused; the host's journal moved (frame 3).
* **Talking.** A joiner's Talk on the copy of a host NPC gives the copy its
  brain back for the conversation; the host holds its own NPC busy meanwhile;
  the copy is paused again when the conversation ends. **(observed)** — the
  joiner held the Find Mutt herbalist conversation on her paused copy (frame
  4); its outcome (three quest steps) counted on the joiner and, sent as
  requests, on the real host: both journals ended the same (frame 5, §6).
* **Dead is dead.** A copy or stand-in follows the host's dead flag whatever
  killed the NPC; a corpse is never paused or detached. **(observed)** — the
  Find Mutt bandit stays lying on the joiner (the field bug, frame 6); a
  villager killed on the host with no hit lies dead on the joiner at the
  host's spot (frame 7); a runtime NPC the host already has dead gets a
  stand-in that is created dead and never shown alive (frame 8).
* **The kill switch** `mp_quest_sync off` on the host stops the mirror and the
  requests at once, on both ends. **(observed)**

**What does not work yet**, plainly (details §9):

1. **Surrendered NPCs are not shown kneeling** on the joiner, and a surrender
   conversation is therefore not reproduced there. Not built: the game gives
   Lua no read of an NPC's surrender, the stream's flags byte has no free bit,
   and no way to put a copy into the surrender pose was found in this WO.
2. **The joiner's examine/pick-up interactions were not pressed live** (they
   need a key; the no-input rule). The requests they produce were sent by the
   synthetic joiner with the exact path, port and values the interaction
   fires (quest graph + the engine's own record). The conversation route was
   live end to end.
3. The dialogue **sequence-used ledger is per machine** (no setter): a topic
   used on one machine is still offered on the other. Asking again replays
   the conversation, but its quest steps are already there (the engine's own
   no-op) — no step is taken twice.
4. **Quest items** a joiner receives from a conversation or a consumer exist on
   the joiner's Henry only (D4; WO-124 strips quest items at the next join).
5. **Quest time sets on the joiner are gated** (only the host's clock moves the
   world) — the gate was armed live, but no State-driven time set was
   reachable in the test save, so the skip itself is **(code-verified)**.
6. A forced conversation the joiner's copy starts is tracked and the host
   holds its NPC, but it is **not held back** when it would not follow the
   host's state (there is no way to tell).

## 1. Phase 1 — the questions

Solo runs r1–r5 on throwaway copies of the maintainer's `playline1/autosave087`
(Find Mutt, at the ambush site) in `playline4`, a research DLL, no agent.

| # | question | answer |
|---|---|---|
| Q1 | do the exported `C_Quest` / `C_Objective` getters return the journal? | **yes (observed)**: `hledaniPsa` level 2, 15 objectives; the dog objective started = the save; after the steps the Mutt and herbalist objectives read started. Used for the joiner/host reads in every run since |
| Q2 | does a pulse on a State's `Set<Value>` port change exactly that State and its consumers? | **yes (observed)**: the deer State `SetDone` 1→2 ran only its consumers (the camera focus, Henry's comment on the deer); the bandit State `SetDone` gave the same 11 State changes, sound, comment and HUD notice as the real `088→089` step |
| Q4 | does a paused NPC hold a conversation if resumed within the request timeout? | **yes (observed)**: a request to a paused NPC waited and started on resume (`wh_dlg_RequestTimeout` 20 s). WO-137's route resumes first: the herbalist's conversation started 0.1 s after the Talk |
| Q5 | does a proxy on `I_UIHudEventsQuest` see every quest event? | **no (observed)**: 2 of 11 changes (objective starts only) reach the HUD sink. Presentation only; not a route |
| Q6 | does a forced conversation start on a paused NPC? | **yes (observed)**: a player-forced conversation on a paused NPC reaches the reply menu. NPC–NPC forced with both paused stalls until resumed |
| Q10 | what does a tracked quest do that is not active in the host's world? | **nothing (observed)**: no marker, no error |

**Side quests** register exactly like main quests: the same concept graph, the
same `C_Quest` / `C_StateVariable` classes, the same `Set<Value>` ports
(code-verified over the census: 219 quest roots under `Quests/Final/Barbora` —
Activity 66, Side 62, Micro 54, main 35, Event 1, Racing 1).
**"All quests" needs no more than that**: the detector takes every State under
any `C_Quest`. What it leaves out on purpose: DLC (the three `RequiredDLC`
roots `navstevaLekare`, `katuvSleh`, `zavodniPodkovy`, and any `dlc*` segment),
world-event machines that are not quests (the village announcer's states:
`notquest`, each world runs its own), and per-machine types (streaming,
camera focus).

## 2. The routes

* **Read** — the detector (the engine's State setter, hooked natively:
  `ConceptModule` slot 42 `OnStateChanged` after the setter's own compare, so
  a same-value set is the engine's no-op and is never recorded) plus the Q1
  getters for the journal reads. The HUD proxy stays a diagnostic.
* **Apply** — the State's own `Set<Value>` in-port (slot 15 of the port = the
  engine's `C_Node::Execute`). Haste was never needed. Refused unless the node
  is a `C_StateVariable` under a `C_Quest`, not hibernating, the port an input
  trigger of that State.
* **Talk** — the scoped resume (Q4): the copy is resumed for the conversation,
  the host holds its own NPC.

## 3. Host → joiner (Phase 2)

* **Every change, in order.** The DLL numbers each record (`seq`) and marks it
  `cascade` (made by another State's consumers) or `root`, and `mirror` when an
  apply caused it. The host sends every quest change except per-machine, DLC
  and silent ones; the joiner applies them in `seq` order through their ports.
  A non-growing `seq` is a host game that started again: the queue resets.
  **(observed h1/j1)**
* **Counted once.** The joiner applies the host's root step and its own graph
  runs the cascade; the host's cascade records then arrive as the engine's
  no-op (`unchanged`): j1, 5 of the host's 6 bandit-cascade records `unchanged`,
  the root and the host's own timer step `changed`. **(observed)**
* **Never during a load.** No apply, no request, no detection while WO-136's
  hold is on (either side); the host's requests wait for its own load. A join
  drops every host change queued before its load (`MP-W137 joiner: join … loads
  the host's world`): they are in that world already, and replaying one could
  set a State back. **(code-verified; the drop line observed j1)**
* **The periodic read (Q1 on both sides).** Every 30 s (and at once when a
  joiner's world has just loaded: a `Resync` ask) the host sends the current
  values of the States that changed this session, each with the port that made
  it; a value still on its way is left out. The joiner compares when caught up
  and corrects toward the host through that port: j1, `MISMATCH … this copy 1,
  the host 2 -- corrected toward the host (SetCompleted)`. **(synthetic host,
  observed on the joiner)**
* **Consumers.** On the joiner the State's consumers run as the game's own: its
  journal, HUD notice, markers, comments. World-changing ones are gated: quest
  time sets never run on the joiner (the native time gate, §9.5); the other
  world consumers met were per-machine by nature (camera focus, stream
  profiles) or only armed a trigger (§10).

## 4. Dead is dead (Phase 2b)

**The field bug** (twice): in Find Mutt the ambush bandit lay dead on the host
and stood alive on the joiner. Root cause, from the field log: the joiner's
puppet start paused the body and then ran WO-118's detach
(`wh_ai_NPCStateResetElement … Stance/Unstance`). A scripted corpse's lying
pose *is* its Unstance (the game's `special_deadBody_common` behaviour), so the
reset stood the corpse up (`result=DeadBody_BeingInteracted_Waiting->MotionIdle
why=puppet-start`), and the paused brain never laid it down again.

Now:

* **A corpse is never paused or detached** — dead in the host's stream (known
  before the pause decides now) or dead here. A copy that dies here while
  paused gets its brain back once, so its own dead-body behaviour runs.
* **The joiner follows the host's dead flag continuously** (the WO-122 owner
  death, every puppet tick) whatever caused it — a hit, a quest's kill node, a
  scripted kill with no hit.
* **A stand-in for an NPC the host already has dead** (WO-136's soul-accurate
  stand-in) is spawned hidden, killed by the owner-death apply, and shown only
  once it is dead, where the host has it. One that never dies here in 15 s is
  removed unseen (no retry for 120 s).

| proof | result |
|---|---|
| the Find Mutt bandit (`hledaniPsa_deadBody`) streamed dead to the joiner | not paused (`a-corpse-is-never-paused`), no detach; still `DeadBody_BeingInteracted_Waiting` 8 s later — lying (frame 6) **(observed; the host's stream synthetic)** |
| a scripted kill on the host with no hit (`tzel_man_3` streamed alive, then dead) | the death applied 0.1 s after the request, corpse 0.12 m from the host's spot; the copy got its brain back and lies over his sawhorse (frame 7) **(observed; synthetic host)** |
| an NPC spawned dead (`prepadeniNaCeste_bandit_3`, a road bandit the joiner's world never spawned) | `standin-hidden` → killed (second request, 3.2 s) → `standin-shown … created dead, lying where the host has it` 0.04 m off (frame 8) **(observed; synthetic host)** |

## 5. Joiner → host (Phase 3)

* **What is asked.** Only the joiner's own ROOT transitions with a port: the
  world did them here (a conversation's outcome, an examine, a pickup, a
  timer, an area). A mirrored change and a cascade are never asked back; the
  host's world derives the cascade itself when it applies the root.
* **The host's verdict**, against its own value (observed h1):

  | request | host value | verdict |
  |---|---|---|
  | deer `SetDone` 1→2 | 1 | **applied** (1→2); the camera cascade stayed on the host (per-machine); Henry's comment on the deer played on the host |
  | the same again | 2 | **already** — counted once, nothing applied |
  | deer `SetActive` 0→1 | 2 | **refused** (a step already past); the host's value and port go back for the joiner's correction |
  | bandit `SetDone` 1→2 | 1 | **applied**; 6 cascade changes to the joiner, 3 per-machine kept; the "Find Mutt!" update on the host (frame 1) |
  | herbalist `SetCompleted` 1→2 | 1 | **applied**; the herbalist objective completed on the host (frame 3) |
  | M05 smith line `SetKnowAboutFirstQuest` 1→2 | 1 | **applied**; M05's "reach the wedding with the smith" objective updated |
  | any, `mp_quest_sync off` | – | **off** — refused, the joiner told |

* **Corrections.** A refused step is put back on the joiner through the host's
  port for that State (or the port the joiner's own records learned for that
  value); the next checkpoint compares anyway. The joiner's copy never keeps a
  step the host's world refused. **(synthetic: the correction path ran live on
  the joiner, §3)**

## 6. Talking (Phase 4)

**The joiner** (observed j1, the host synthetic):

1. The copy of `tvez_bozena` (the Find Mutt herbalist) is a paused puppet; the
   game still offers its "talk" action to the joiner's Henry (read through
   the game's own `GetActions`).
2. The Talk (`BasicAIActions.OnTalk`, wrapped like WO-131's loot): the copy's
   brain comes back (`WO137-TALK resume … paused_before=true`), the host is told
   (`QuestAsk Talk on`), the game's own request runs, the conversation starts
   0.1 s later (runtime id 79), the host answers with its hold.
3. The conversation plays on the copy (frame 4, the menu blurred). With the
   engine's own test cvars `wh_dlg_ForcedDecision` (the herbalist decision
   `korenarka_1.dec1`) and `wh_dlg_AutoSkip 1` standing in for the player's
   choices, it ran to its end: the dialogue out-port (the player asked her
   about the dog) completed the herbalist objective and started the innkeeper
   and fisher objectives — three root steps on the joiner, each sent as a
   request.
4. The end (`[ID: 168] Dialog ending [Ex0: Dude Ex1: tvez_bozena …]`, relayed by
   the agent): the copy is paused again (`MP-PAUSE … why=w137-talk-end`), the
   host told (`Talk off`). The topic is used up on the joiner (frame 5).

**The host**: the same three requests applied on the real host (h2): the host's
journal = the joiner's (herbalist completed, innkeeper and fisher started).
**(observed)** The talk hold (h1): `tzel_olbram` suspended on the host for the
partner's conversation (`WO137-HOLD on … exec=ok`); the host's own request to
him stayed pending for 10 s while held and started once released — busy, no
second conversation. **(observed)**

* **No split** — the talking copy stays the host's puppet (bound, written by
  the stream) and is paused again at the end; the host held its NPC still, so
  the two stand where the host has them. Never parked mid-conversation (the
  release is deferred to the end).
* **Safety nets:** a conversation that never starts is ended after 25 s; one
  whose end line was missed ends 3 s after the player left dialogue (observed
  j1: the first attempt ended through the engine's second end form, which the
  agent now also reads); a host hold is released after 5 minutes, when the
  joiner leaves, or when the agent goes quiet.
* **Money and items** a conversation gives are the joiner's own (its Henry);
  quest items see §9.4. **Crucial-decision saves**: the herbalist module's
  quest save runs from the dialogue's out-port; on the joiner the host-only
  lock held and no save was written (observed; the engine logs no refusal).
* **Forced conversations** a copy starts (its quest logic follows the host's)
  run on the paused copy (Q6) and are tracked: the host holds its NPC, the end
  is seen. **(synthetic)**
* **Both players near one conversation**: each screen shows its own; the other
  player's figure just stands (j1: the host's avatar beside the herbalist,
  frame 4). No camera takeover.

## 7. Phase 5 — Find Mutt! and a main-quest step

All on throwaway copies of the maintainer's `playline1/autosave087` in
`playline4` (never the real save). The solo method: the real game as host with
the synthetic joiner (avatarpeer), the real game as joiner with the synthetic
host (synthpeer) replaying the host's recorded changes.

| step | host side | joiner side |
|---|---|---|
| the deer is examined | joiner's request applied, Henry's comment on the host **(observed h1; the request synthetic)** | the host's recorded change applied, the joiner's own comment **(observed j1; the host synthetic)** |
| the bandit corpse is examined | request applied, full cascade, "Find Mutt!" (frame 1) **(observed h1)** | host's change applied, joiner's cascade, "Find Mutt!" (frame 2) **(observed j1)** |
| the herbalist objective starts (a 1 s timer on the host) | a host-originated root step, sent **(observed h1)** | applied **(observed j1)** |
| the joiner talks to the herbalist | the three outcome steps applied, the journal moved **(observed h2; the requests replayed from j1)** | the conversation plays on her copy and counts (frames 4, 5) **(observed j1)** |
| M05 (main quest): the smith line knows the first job | request applied, M05 objective updated **(observed h1)** | the host's change applied, the same objective updated **(observed j1)** |
| no step twice | duplicate → `already` | the host's cascade records → `unchanged`; the replayed herbalist step → `unchanged` |

## 8. Safety (Phase 6)

* **H4 — no inverse.** Every port applied in h1, h2 and j1 was walked with the
  WO-97 audit's graph model (`tools/Audit-QuestApplies.py`, extended through
  logic nodes: the WO-97 model stops at them and under-reports, e.g. the live
  bandit cascade runs through an `IfFunction`). 8 ports: the deer and bandit
  steps reach the second-pack module's cutscene **area** (armed, not fired: it
  plays when a player enters it, on that player's machine) and a
  dialogue-module flag; the herbalist, innkeeper/fisher and M05 smith-line
  ports have no hazard-class consumer. The tool reads every applied port from
  the agent logs, so a session's applies can be vetted after the fact.
* **D2 — one-shot edges.** The apply fires the State's own port, so the
  `On<State>` edges run (no mirror without its transition). The host's cascade
  records are the engine's no-op on the joiner (its graph ran them once).
* **S2 — a journal step without its gameplay.** Not met in the tested steps:
  Q2 showed the pulse equals the real step, and j1's world after the mirror
  had the host's objectives and triggers. The risk remains for gameplay wired
  outside State edges (recorded, WO-97's sacks case).
* **S3 — non-Henry stretches.** "Switching to player N" (N≠0) holds the mirror
  on that machine: the host sends nothing and answers requests `held`; the
  joiner applies and asks nothing. As before, joins are refused in non-Henry
  worlds (WO-125). **(code-verified, unit-tested)**
* **The kill switch.** `mp_quest_sync off` on the host: the joiners are told at
  once (`QuestHost Mode off`), requests are refused `off`, the host's detector
  goes off, and anything still on its way is dropped (the agent drops records
  whenever the mirror is inactive). On the joiner: detection and the time gate
  off; host changes sent while the host is off are not kept. `on` again: an
  immediate checkpoint. **(observed h1, j1)**. The old quest layer stays off in
  a shared world (WO-133): `mp_quest_sync` was WO-94's argless status line
  (its toggles are `mp_quest_on/off`); WO-137 takes the name as the work order
  asks, and `mp_quest_status` prints both layers.
* **The coarse fallback stays**: a rejoin loads the host's world exactly (and
  drops what was queued before it).
* **H3** unchanged: the research file's pulse is refused whenever an agent is
  attached (WO-133's gate) — which is why the live host-side steps in the tests
  came from applied requests and host timers, not from a pulse.

## 9. What does not work, and known limits

1. **Surrender** — not built. The field host's enemies surrendered
   (`Skirmish event: SoulSurrender`, WO-136). Needed: a host-side read (the
   skirmish line, or the Mannequin `Stance` tag `surrender` that WO-100's
   native read can see), a message (the QuestHost channel has room), and a way
   to put the joiner's copy into the surrender pose — not found. Until then the
   copy stands paused and a Talk on it is an ordinary conversation.
2. **The joiner's interactions** (examine, pick up) need a key press; not
   performed live (the rule). Their requests are the same messages as any
   other (§5); the conversation route was live.
3. **The sequence-used ledger** (D1) — per machine, no setter found.
4. **Quest items** — D4 (WO-124's splice strips them at the next join).
5. **The time gate** — armed on the joiner (`WO137-CONFIG … role=2
   time_gate=on`, observed); the skip (`WO137-TIMESET joiner: quest time set …
   NOT run`) is code-verified: the only Find Mutt time set hangs off a debug
   trigger, and no State-driven one was reachable in the save.
6. **Forced conversations** are not held back when out of step with the host.
7. **Cutscenes** are per machine (out of scope, recorded): a joiner entering an
   armed cutscene area plays it locally; its State changes are requests.
8. **ChangeWeather** and other world consumers besides time are not gated.
9. **A host Godwin stretch** mid-session holds the mirror; what changes then
   reaches the joiner at its next rejoin.
10. The `C_QuestModule` vtable compare failed (unexplained); the HUD
    diagnostic uses the sink's vtable instead. Presentation only.

## 10. Consumer types met

| consumer | where it ran | kept / gated |
|---|---|---|
| objective log updates (the journal), HUD notices, markers | both | kept (each player's own) |
| Henry's comments (`AI::DoMonologue`, e.g. decision `hledaniPsa_commentOnDeadDeer`) | both — the host's Henry also comments on a step the joiner made | kept |
| `PlayAudio quest_objective` | both | kept |
| camera focus (`OnOffFocusCamControl*`) | each machine | per machine, never sent |
| stream profiles (`Streaming`) | each machine | per machine, never sent |
| timers (the herbalist objective 1 s after the bandit) | both | the host's result is the one sent; the joiner's own is the engine's no-op |
| cutscene-area and dialogue-flag arming | both | armed only |
| dialogue topics (the herbalist topic) | the machine that talks | per machine (ledger) |
| a quest save node (from a dialogue out-port) | the joiner | refused by the host-only lock |
| quest time sets | — | gated on the joiner (code-verified) |

## 11. Reference

**Wire** (the WO-123 join channel, `LootMsg` shape `[kind][tok][ASCII text]`,
rows in `Protocol.JoinWire`, no protocol bump):

| type | name | kinds |
|---|---|---|
| 0x60/0x61 | QuestHost (host → one joiner) | 1 Change `seq flags old new port\|- questLen path`; 2 Result `verdict hostVal hostPort\|- path`; 3 Checkpoint `part nparts val:port:path …`; 4 Hold `on\|off npc`; 5 Mode `on\|off why` |
| 0x62/0x63 | QuestAsk (joiner → host) | 1 Request `flags old new port questLen path`; 2 Talk `on\|off npc`; 3 Resync `why` |

**Pipe** (agent ↔ DLL, native `wo137.h`): 0x23 → 0x9D (ops: config, apply,
read States, read a quest, status, HUD, hold); unsolicited 0x9E = one quest
State change.

**Switches**: `mp_quest_sync on|off` (default on; the host's is the session's),
`mp_quest_talk on|off` (joiner, default on), `mp_quest_status`.

**Log lines**: native `WO137-BUILD`, `WO137-CONFIG`, `WO137-CHANGE`,
`WO137-APPLY`, `WO137-TIMESET`; agent `MP-W137 host change #…`, `MP-W137 host:
request #… APPLIED|already|refused`, `MP-W137 joiner applied host change #…`,
`MP-W137 joiner -> host request #…`, `MP-W137 MISMATCH …`, `MP-WO137-STATS`
(every 60 s); mod `WO137-SESSION`, `WO137-SYNC`, `WO137-TALK
resume|start|forced|end`, `WO137-HOLD on|off`, `WO137-DEAD
resumed|standin-hidden|standin-shown|standin-removed`, `MP-PAUSE … refused …
a-corpse-is-never-paused`, `MP-DETACH … result=skipped-dead`, `WO137-STATUS`.

## 12. Tests

* `dotnet/KcdMp.Client.Tests/Wo137Tests.cs` — the detector record, the
  vetoes both ways, DLC, the host's verdict (dedupe, refusals), every text
  round trip and hostile text, the ordered queue (duplicates, a correction put
  in front while an apply runs, a reset), what is kept around a join, the
  checkpoint's consistency, the kill switch / Godwin / load rule, the engine's
  dialogue lines (both end forms).
* `dotnet/KcdMp.Relay.Tests` — the two quest types cross the relay one way
  each, the longest text whole.
* `tools/Test-WO137Synthetic.lua` (84 checks) — dead is dead (no pause, no
  detach, resumed once, dead stand-ins), talking (resume, the tick does not
  re-pause, start, end, timeouts, the request fallback, a forced conversation,
  never parked mid-conversation, the gates), the host's hold (re-issued, an
  NPC in the host's own conversation, the join pause, the agent going
  silent), the kill switch and the commands.
* `native/tests/wo137_rules_tests.cpp` — the time-set method match, quest
  paths, the config log key, the time gate's condition.
* `tools/Audit-QuestApplies.py` — H4 vetting of applied ports.

## Frames (`docs/wo137-shots/`, game text blurred)

1. `1-host-joiner-bandit-step-quest-update.jpg` — the host after the joiner's
   bandit-corpse request: the quest update notice (h1).
2. `2-joiner-host-bandit-step-quest-update.jpg` — the joiner after the host's
   bandit step: the same notice (j1).
3. `3-host-objective-done-from-joiner-request.jpg` — the host after the joiner's
   herbalist request: the objective-completed notice (h1).
4. `4-joiner-talks-to-a-paused-host-copy.jpg` — the joiner in conversation with
   the herbalist's copy; the host's avatar beside them (j1).
5. `5-joiner-quest-log-after-its-conversation.jpg` — the joiner's quest-log
   overlay after the conversation (j1).
6. `6-joiner-mutt-bandit-lies-dead.jpg` — the Find Mutt bandit streamed dead:
   lying (j1).
7. `7-joiner-scripted-kill-without-a-hit.jpg` — a villager killed on the host
   with no hit: a corpse on the joiner (j1).
8. `8-joiner-stand-in-created-dead.jpg` — a stand-in created dead behind the
   joiner's Henry (j1).
