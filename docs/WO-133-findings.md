# WO-133 — quest safety patch: findings

Unattended, solo, one machine: the Modding Tools game, throwaway saves, a local
relay on its own port. The real game ran as the **host** with
`tools/wo121/avatarpeer` as the joiner (run H1), and as the **joiner** with
`tools/wo118/synthpeer --join-host125` as the host, first in its own world
(J1, plus avatarpeer as a third client) and then after a real join into the
synthetic host's world (J2). Evidence marks: (observed), (code-verified),
(synthetic), (inconclusive). No installer, no `VERSION` change (WO-134 builds
the installer carrying WO-132, 133 and 134).

## 0. Answer first

| item | result |
|---|---|
| the rule | a **shared-world session** = a session is up (the relay's role has arrived) and its mode is a shared world (the host's own `mp_shared_world`; on a joiner the host's announced mode, else the local toggle). While it holds, none of the old separate-worlds quest layer acts, on either machine. Outside one, nothing changes (code-verified; synthetic; observed on the host with `mp_shared_world off`) |
| H1: the catch-up | **closed.** The agent pushes the gate to the mod (`KCD2MP_Wo133Gate`, on every change and every 5 s). `KCD2MP_QuestFire` (the one Haste write) and `KCD2MP_QuestAnswer` (every route to an answer) refuse; the prompt cannot be raised; anything standing is cleared when the gate goes on. Host: F11, F12, `confirm`, `cancel`, `ui_accept`, `ui_cancel` through the mod's own action hook, then `mp_quest_test_prompt`, `mp_quest_fire socky._initAndStart`, `mp_quest_yes`, `mp_quest_no` typed: 10 refusals, each "Quest catch-up is off in a shared world." in the log and on screen, **0** `HasteTrigger` / `Readiness observer` / `QUEST-CATCHUP FIRE` lines (observed). Joiner: the same, in its own world and in the host's world after a join (observed) |
| `confirm` / `cancel` | they **do** reach the answer when a prompt stands: called through the hook, all four answered the prompt (observed). Whether the game's input layer ever delivers them to that hook stays (inconclusive), and no longer matters: the gate sits at the answer and at the fire, below every key |
| proximity announcements | off in a shared world: no `quest_approach` event, no deferred offer (synthetic); the agent sends no approach and ignores a peer's (code-verified; observed: no line) |
| divergence, WAITING_FOR_PEER | off: host and joiner each got a peer marker different from their own and logged `[story] divergence: no peer comparison in a shared world`; no divergence toast, no `QUEST-DIVERGENCE` / `WAITING_FOR_PEER` line (observed). The joiner no longer sends its own (solo-world) marker: `our story marker is not sent` (observed) |
| fingerprints, quest-gap toasts | off: the joiner neither reads nor sends one (`fingerprint send skipped`, observed); nobody compares (host: `fingerprint compare skipped`, observed; joiner: no gap line or toast after a host fingerprint, observed, the skip line itself was throttled by a shared counter, now split). The host still fingerprints its own saves. **`mp_quest_off` now stops the gap toasts too** (synthetic; it did not before) |
| H3: `port_watch` | **closed.** The file-armed trigger never fires while a session runs (the agent's SetSession heartbeat) or an agent is attached: `WO133-PORTGATE: REFUSING the file-armed trigger …` with the host session up (observed). Probe and read still work. Solo with no agent: unchanged (native unit test) |
| kept, untouched | FindNode / concept read, the cutscene edge channel (kind 6), the objective registry, the hazard audit tool, the wire kinds 1–6, the `DialogTwin_` exclusion (code-verified) |
| H2: only the host moves the clock | **closed.** The host of a shared world drops every time skip that is not its own, every phase (start, done, done-quiet, sync), and does not remember it for reload convergence either. avatarpeer sent a sleep, a clock sync and a fast travel (+7200 / +9000 / +12000 s): 5 × `[timeskip] DROPPED`, the host's clock moved its natural 204 s in 14 s (observed). **Found and closed on the way:** a joiner also applied any other joiner's skip (a 3+ player session). A joiner of a shared world now applies only the host's: a third client's sleep was dropped, the host's fast travel applied (+2518 s) (observed) |
| WO-114's half | confirmed. Code: `ReportClockJumpAsync` withholds a joiner's jump in the host's world (`GameBridge.cs`, code-verified). Live, after a real join: the joiner's clock +3600 s → `clock jump ... on a joiner in the host's world -- NOT reported`, the synthetic host received no skip (observed) |
| the host's skips still reach the joiner | the host's clock jump 754018 → 757917 was reported (start + done, kind 2) and reached avatarpeer (observed) |
| solo / `mp_shared_world off` | as before: with the host's toggle off, `WO133-GATE no shared world`, the test prompt shown, F12 declined it, `mp_quest_fire` of a non-beat got the old registry refusal, and a peer's sleep applied again (759516 → 761273) (observed). Solo with no agent: the gate starts off in a fresh Lua (synthetic). Every existing quest suite is green (below) |

## 1. What changed

**Mod (`kdcmp.lua`).** `KCD2MP.w133` + `KCD2MP_Wo133Gate(on, role, why)` (agent →
mod), `KCD2MP_QuestSharedWorld()`, `KCD2MP_QuestSharedOff(what, typed)` (one
throttled `WO133-OFF` line per piece; a typed command also gets the on-screen
line). Checked in `KCD2MP_QuestShowPrompt`, `_QuestAnswer`, `_QuestFire`,
`_QuestTestPrompt`, `_QuestDivergence`, `_QuestObjectiveGap` and the 1 Hz
proximity tick (deferred offers and approach announcements). The gate going on
clears the prompt, the parked offer, the waiting rows and the gap rows.
`KCD2MP_QuestObjectiveGap` now also honours `mp_quest_off`. `mp_quest_sync`
says "but OFF in this shared world" while the gate holds.

**Agent.** `Wo133Rules` (pure) and `GameBridge.Wo133.cs`. The gate is computed
from the role, `_sharedWorld` and the host's announced mode, pushed from the
WO-122 1 s tick, on every role change, after a mod re-init and on disconnect
(off). Divergence (`ReportStoryDivergence`, `SendQuestDivergence`, the re-push,
a peer's approach), fingerprint compare, the joiner's fingerprint send and its
marker send all check it. `TimeSkipDown`: `Wo133DropTimeSkip` before anything
else.

**Native.** `port_gate.h` (pure) and one check in `port_watch` before
`port_op` for a `trigger` request; the request is consumed (it does not fire
later either).

## 2. Things worth knowing

- **A killed agent leaves the mod's gate on** until the game restarts (only a
  clean disconnect pushes it off). Observed when the harness killed the host
  agent. Fail-closed by design: after a crash the quest layer stays off, never
  the other way round.
- **The relay is unchanged** (out of scope; "the relay may route it"). One
  consequence (code-verified): if a joiner's skip start reaches the relay
  first, the host's own start is absorbed into it and the host's result goes
  out as done-quiet; the joiner still applies it unless it is within natural
  skew of its clock. The host's clock is never moved by the joiner.
- **A joiner's own sleep or wait** still moves the joiner's own clock (only the
  host ignores it), so that joiner runs ahead until the next join. Not changed
  here (code-verified; frequency inconclusive).
- The mod's quest registry, the hazard window hooks and `KCD2MP_QuestSetCurrent`
  still run; they act on nothing.

## 3. Gates

See `docs/WO-133-progress.md` (every release gate, no Inno Setup).
