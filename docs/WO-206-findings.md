# WO-206 — The test bench: two real copies, a faulty link, digests, animation checks, epochs

Every overnight build since WO-200 was proven on one machine against a synthetic partner and then broke in the field. This WO
builds the bench that tests co-op with co-op: two real copies of the Modding Tools game on one PC, joined through our own relay,
started the way the launcher starts them, with a fault mode on the link, checkpoint digests compared across both machines,
animation checks in every bundle, and epoch stamps (the one behaviour change).

Nothing here reads input: every tool built for it passes `tools/Check-NoInputCapture.ps1`, first and last.

## The bench (`tools/rig/`)

| piece | what it does |
|---|---|
| `mirror.py` | Each copy's root beside the install (`kcdmp_rig_A`, `kcdmp_rig_B`): `Data`, `Localization`, `Engine` and `Bin` are hard links (no space, never written; `verify` stats every linked file), `Mods\kdcmp` and `system.cfg` are its own. Its `system.cfg` names its own user folder (`sys_user_folder`), pins the texture budget (`sys_spec_TextureResolution`: the pool size cvar alone is overridden at texture init), a window on its own monitor, muted audio and `sys_SleepIfInactiveWH 0` (the game sleeps while its window is inactive: the copy behind ran 20-34 fps instead of 170). The install and the real saves are only read. |
| `procs.py` | Every process the rig starts is recorded as (pid, creation time, image) and stopped only through that record; one rig at a time (a lease file); the real saves folder against the WO's hash list; device-removed / driver-reset lines in either copy's log. |
| `rig.py` | The A.3 checks before the second copy starts (saves hash list, mirror, the first copy at its menu, free VRAM over the second pool + 2 GB through a DXGI query), the menu test, the watch (VRAM, RAM, both game APIs, both frame rates through the engine's frame counter). |
| `pair.py` | A joined pair: per-copy agent, DLL, data and backup folders, pipe (`kcdmp-<id>`), game API, dice and version ports; the relay on the test port; the host the launcher's way (load, "Gameplay started", 10 s, inject, verify, agent `--hosting`), the joiner the launcher's way (its main menu, the host in, inject, verify, agent, the "bring my character" answer). |
| `scenarios.py` | Named pair scenarios: every action through the console, Lua or the agent, a postcondition with a timeout and a reason code; a failure folder with both bundles, the step and the last 200 lines of each log. |
| `bones.lua` / `bones.py` | The bone prototype (≤ 6 bodies, 5 Hz, `Entity.GetBonePos`) merged into the probe's samples. |

What the engine does with a second copy (static and live): `-multi_inst` only *allows* it -- the game's instance check then shows
a Yes/No "start another instance?" box on every second launch (`-unattended` refuses instead), and the bench needs it answered
each time; nothing in this repository answers it. The second copy writes `kcd(1).log` and
appends `(1)` to its user folder; its game API moves to port 1404. `sys_MaxFPS` did not cap the copies.

What two copies need from our own code (A.2): the DLL's pipe and log names take `KCDMP_INSTANCE` (`instance.h`; no id = the
players' `\\.\pipe\kcdmp` and `kcdmp-native.log`), and the agent with an id reads only its own root's newest kcd log (a second
instance writes `kcd(1).log`) and the saves folder its own engine names. A player's game and agent never have an id.

### How to run a pair

```
python tools/rig/pair.py prepare
python tools/rig/pair.py relay [plan.json]
python tools/rig/pair.py up A
python tools/rig/pair.py up B
python tools/rig/pair.py host A 4 <save>
python tools/rig/pair.py join B
python tools/rig/scenarios.py smoke
python tools/rig/pair.py collect <tag>
python tools/rig/pair.py down
```

Scenarios: `join`, `walk`, `sleep`, `wait`, `fight`, `talk`, `loot`, `smoke` (all of them), `soak [min]`, `c2` / `c3` (the digest
twins), `e2` / `e2twin` (a host reload during a fight), `b1` / `b1twin` (a shared cutscene), `bones`, `stats`, and the sessions
`long`, `joinfight`, `joinscene`, `e2pair`, `c1pair`. `pair.py host ... --settle-freeze` starts the host's agent the way the
0.62.7 field did (the mod loaded, its timers not ticking yet: game time held at 0 until 8.5 s into the agent's wait for them).
`pair.py down` ends every run: the processes it started (by their record), the real saves against the hash list, the mirrors.
Every run starts from a fresh copy of the seed saves. `KCDMP_RIG_HOST=B` swaps the roles; `KCDMP_RIG_HOST_SAVE_FILE` /
`KCDMP_RIG_HOST_SAVE` load another world (B1 used a world saved before the Semin scene).

## The relay's fault mode (Part B)

Test-only: it loads only with **both** `KCDMP_TEST_RELAY_FAULTS=1` and `--test-faults <plan.json>`; no config key can turn it on,
and `tools/Test-RelayFaultsGate.ps1` (run by `Build-Installer.ps1`) fails the build if a shipped file could. Faults per
direction and client role, at a named join step (`request`, `paused`, `world-sent`, `received`, `loaded`, `ready`) or a time:
`lag`, `jitter-hold`, `throttle`, `stall`, `cut` / `heal`. Seeded; `RELAY-FAULTS` summary lines count what was held, delayed,
stalled, throttled and cut, and a rule that never touched anything is `hollow` (a failed run, not a pass).

## Checkpoint digests (Part C)

The host takes a checkpoint every 5 s at a quiet point and tells the joiner (W203 kind 10); each machine writes one
`digest.jsonl` row per category: quest values (every State, Timer and TimeOfDayWatch node of the installed quest files, read by
the DLL's sweep a few a frame), the dead set around the host's Henry (death read as the mod's own observer reads it), the chest
ledger, the world clock (in 15-minute steps), the join state. The keyspace is sent to the DLL once per session and kept across
loads. `tools/digest/analyze.py <host bundle> <joiner bundle>` finds the first divergence and writes it into both briefings; a
difference that clears by the next checkpoint with an applied row on the lagging side is in flight, and the clock one step apart
for up to two checkpoints is the step's edge (the game clock runs 15 times real time). Switch: `mp_digest` (on).

## Animation checks (Part D)

The DLL's probe writes every body within 40 m, 10 times a second, to `kcdmp-anim.jsonl` (rotating at 32 MB). The NPCs around the
player are found by an entity walk once a second and re-read by position every sample: the walk itself costs 6-8 ms, and at 10 Hz it
took 6 % of both copies' frame rate (measured on the bench, A/B); once a second it is under 1 ms a sample. `WO206-PROBE cost` in
the DLL's log gives the probe's own cost every 10 s. The log packer runs
the detectors (`tpose`, `sliding`, `floating`, `sinking`, `jitter`, `rubberband`, `deathpose`, `stuck`) and adds an
"Animation" section to every briefing and `anim/anim-report.html` to the bundle; `KcdMp.LogPack.Tool anim-compare` compares
the joiner's copies with the host's NPCs. Switch: `mp_anim_probe` (on).

## Epochs (Part E, the one behaviour change)

Three stamps, one rule: nothing older than the newest is applied. The **join epoch** counts the host's join attempts (a joiner
state or join message of an older attempt is dropped); the **world epoch** counts the host's loads (the host announces it, W203
kind 11; every frame a machine reads is stamped with the epoch it knew at the read, and the epoch frame itself is taken at the
reader, ahead of the queue, so a frame read before the notice and applied after it is the old world's); the **scene instance**
counts plays of a shared cutscene (a sample of an older play is dropped). `mp_epoch_check` (on) switches the drop; off, an old
item is applied as before and counted `applied_old`. `mp_w206_status` prints the counts (`MP-W206-EPOCH`).

The bench also found the joiner's own pipeline carrying on after its join was aborted (the relay lost at "world-sent": the
abandoned world was still spliced, placed and loaded). The pipeline now stops when its attempt is no longer the live one,
before placing and before the load.

## Results

All runs on one PC (32 GB RAM, a 12 GB GPU), both copies the Modding Tools build in their own mirror roots, joined through the
relay's test port, started the launcher's way. The real saves folder was checked against the WO's hash list before and after every
run. It changed once, in A.5's first run (copy A then ran on the real saves folder's throwaway playline: the host's own world saves
wrote into it and the game's per-playline save cap deleted three old saves); everything was restored byte-equal from the WO's backup
at once, and since then both copies run from their own mirror roots and user folders. Every check after that: unchanged.

### A: two real copies

| check | result |
|---|---|
| A.4 menu test (attended, 30 min) | both copies at their menus 30.1 min, no driver reset; each ~2.4 GB working set, 34-35 fps unfocused; free VRAM 7.7 GB with both. The second copy needs the "start another instance?" box answered (criterion 1 as written fails) |
| A.5 first real join, the launcher's order | join 61-77 s (3 of 3), walk (probe-judged: p90 position error 0.6-1.1 m), shared wait, fight with two bandits, chest take (per player, by design) pass; the shared sleep runs but the clocks were 536 s apart right after it (pulled together later); the joined conversation was refused by the game ("not launched from queue ... during interactive scene") -- the rig had let each run start from the previous run's world; from a fresh copy of the save every run, the conversation starts (see the release gate) |
| background copy cost (in a world) | until the inactive-window sleep was found, the copy behind ran ~20-25 fps (the one in front ~110); free VRAM ~3.6 GB; the joiner copy's working set reaches 10-15 GB and free RAM fell to 0.27 GB once -- the rig now stops both under 0.5 GB for a minute |
| the bench's own lag (found late) | the copy whose window is inactive sleeps (`sys_SleepIfInactiveWH`, default 1). It made the host's digest keyspace take 7 min (27 s awake) and a shared cutscene's clock samples reach the joiner 40-90 s late. The mirrors now set it 0 |
| the host agent during a join | its queue median is 1.2-1.4 s in every run, but it falls 17-20 s behind while it holds and saves the world for a joiner (the scorecard's p99, awake or not) -- open |
| S1 (WO-204) on two real copies | 9 of 10 shared sleeps / waits pass (the clocks 16-296 s apart after, most ~30 s; one ask lost by the rig), no crash |
| A.6 the join's mutation twin | the 0.62.7 failure needs the field's slow settle (the mod loaded, its timers not ticking for 8.5 s after the agent starts); `--settle-freeze` makes it. Twin (the tail fix off): falls back to the debug API, the host's queue 437 s behind and growing, the world save that identifies the host never runs, the join never starts -- the field. The fixed build under the same order: the tail is kept, the first frame comes 6 s later, join in 64.8 s |

### B: the faulty link

| test | result |
|---|---|
| B2, a 5 s cut at each join step (fixed build) | world-sent, received: the host resumes in 5 s, the aborted attempt stops before placing, a second join completes (58-62 s); loaded: the host holds 58 s, resumes, the joiner goes back to its own save and its player is told (a joiner in a world joins only by choice); ready: the join completes; request, paused: the host resumes in 0.4 / 0.8 s, the joiner asks again and joins (62 s). All six steps pass |
| bugs B2 found (all fixed, each with a test and a twin switch) | an aborted join was still placed and loaded (the joiner played on in the abandoned world); a cut right after the request left the host's world paused 195 s (a late pause command, no resume); a request still waiting when the link dropped blocked every later try (the joiner never asked again) |
| B2 twin (stale-join guard off) | fails as it must: the aborted join is placed and its load starts |
| B2 twin (`mp_rejoin_recover` off) | passes at all six steps, the same as with it on: the switch never decides a 5 s cut (recovery is the joiner's automatic fresh join; `rejoin_recover` covers the in-world rejoin after a host reload and the join's silence timeout). As written this twin cannot fail; B2's failing twin is the one above |
| B3, lag 300 ms each way + throttle 256 kB/s (both copies awake) | join 75.1 s (64.7 without faults), fight pass (14,248 frames delayed, 356 throttled); scorecard (joiner), without -> with faults: ping p99 5 -> 643 ms, host swing to copy p90 18 -> 326 ms (target 250), fighting NPC position age p90 1.2 -> 1.7 s, weapon draw on copies p90 163 -> 228 ms |
| B1, a shared cutscene under jitter-hold (both copies awake; a world saved before the Semin scene, started as the host quest's own) | the joiner follows: 1 hard seek (forward, 6 s behind), the rest by speed (0.9-1.1), never backward -- a stale sample 13.7 s behind was met by slowing, not a jump back. Twin (`mp_cutscene_clock_v2` off, a fresh pair): the old follower sets the time 25 times, 22 of them hard and backward (21.5 s -> 5.0 s). In the bench save itself no scene can be started (every handler hibernated; the engine refuses a wake) |

### The WO's own cost (A/B on one joined pair at rest, 3 cycles, 9 samples each)

| readers | host fps off -> on | joiner fps off -> on |
|---|---|---|
| digest + probe (first build) | 22.9 -> 21.6 (-5.7 %) | 122.3 -> 114.0 (-6.8 %) |
| probe alone (first build) | 22.7 -> 21.2 (-6.6 %) | 120.0 -> 112.7 (-6.1 %) |
| probe alone (the walk once a second) | 22.7 -> 22.4 (-1.3 %, inside the spread) | 122.4 -> 123.0 |

The probe's NPC walk took 6-8 ms each sample; once a second it is under 1 ms a sample. The digest costs nothing measurable.

### C: checkpoint digests

| test | result |
|---|---|
| C1, 30 min in the shared world | 351 checkpoints on each side, no gap. 5 unexplained divergences, all named: two host-only fog timers (`trosecko.fogControl.fogUnstreaming` for 7.4 min, `fogStreaming2` once -- the Jaunt shape), `trosecko.kovar.KovarJeVKovarne` 1/2 for one checkpoint at each change (twice), `trosecko.zaby.henik_hlida_majku.state27` for three checkpoints. Not zero: these are the next mirroring work |
| C2 twin (a timer at rest on the joiner only) | the analyzer's first new divergence is that timer, missing on the joiner |
| C3 twin (a death announced from a health reading) | the shipped path stays silent for a living NPC (control); with the research switch the host announces it dead while its own copy lives, the joiner applies it, and the analyzer's first new divergence is the dead set naming that NPC |
| bugs C found (fixed) | the dead set was always empty (the scan used a death check this game's Lua does not have); after a host reload the host wrote checkpoints from an emptied quest map for 7 minutes and sent its whole keyspace again (now kept across loads) |

### D: animation checks

Baseline (the bench save's town + a two-bandit fight, two host reloads in the run), findings / bodies:

| detector | host | joiner |
|---|---|---|
| tpose | 17 / 17 | 18 / 18 |
| sliding | 26 / 13 | 30 / 8 |
| floating, sinking | 0 | 0 |
| jitter | 2 / 2 | 1 / 1 |
| rubberband | 1 / 1 | 2 / 2 |
| deathpose | 93 / 1 | 86 / 1 |
| stuck | 127 / 18 | 50 / 9 |

The T-poses cluster at the rig's teleport and after a reload, as many on the host as on the joiner: bodies streaming in.
Host vs joiner: 19 NPCs on both, the stream 215 ms behind, position error p50 0.08 m / p95 0.85 m, the full-body action differs
55 % of the time, the drawn weapon never. Twin: a bind pose and a 1.2 m/s slide inserted into real probe data are both found;
every other detector unchanged. The bone prototype ran (813 lines, 1,458 samples with bones).

### E: epochs

| test | result |
|---|---|
| E1 (the B2 cuts) | zero old items applied on either side in every cut; the host dropped 65 stale joiner states at the cut after the load |
| E2, the host reloads during a fight (the joiner's frame held 8 s across it) | the joiner dropped 5 rows of the old world, applied none |
| E2 twin (`mp_epoch_check` off) | the same reload lets the same 5 old rows through (`applied_old` 5) |

### The release gate (Part F) and the build

Every offline gate passes on the final code (the no-input-capture gate first and last; relay 191, agent 1,949, setup 77, log
bundle 58, every synthetic suite, the relay fault-mode gate, the digest analyzer 43, the rig 24). The pair smoke on the final build
(both copies awake, the launcher's order): **join, walk (p90 0.08 m), a shared sleep, a wait, a fight with two bandits and a loot
take pass; the joined conversation does not.** On the real pair the host's conversation starts and is offered, but the offer
reaches the joiner about a minute late (the joiner's queue climbs to 30-60 s behind after a fight), and once the joiner listens
the host sends no lines (`lines_out` 0 in two runs, the speech capture armed). One cause was found and fixed on the way (a single
refused speech poll during the join's hold silenced the host's conversations for the session: now retried, with a twin); the rest
is the next WO's. WO-204 proved the conversation against a synthetic partner only.

By the WO's own release gate **no installer was built** for 0.63.0. The code is committed with VERSION 0.63.0.

### Open (the next WO starts here)

* **The joined conversation on a real pair** (the release gate's failing item): the offer a minute late, no lines after the
  joiner listens. The bench reproduces it in the bench save (ttkc_drozd) and in the Semin camp; `scenarios.py jointalk`.

* **Mirror the divergences C1 names.** The host-only fog timers (`trosecko.fogControl.fogUnstreaming`, `fogStreaming2`: the Jaunt
  shape again) and the states each machine evaluates on its own (`trosecko.kovar.KovarJeVKovarne`,
  `trosecko.zaby.henik_hlida_majku.state27`). C1's bar of zero is not met; the bench now names what is left.
* **The `mp_rejoin_recover` twin cannot fail on a 5 s cut**: the switch covers the in-world rejoin after a host reload and the
  join's silence timeout. A pair test of those paths (a cut during the rejoin after a host reload) is still to be built.
* **A shared skip's clocks** are 300-700 s apart right after it, before the joiner's clock is pulled in; in S1 they met within
  60 s every time (16-296 s apart, most ~30 s). A.5's first sleep was still 536 s apart a minute later.
* **The host agent falls 17-20 s behind during a join** (it holds and saves the world for the joiner; median 1.2-1.4 s otherwise).
  Position age p90 of fighting NPCs is over its 200 ms target even without faults (1.2 s).
* **The joiner copy's working set** reaches 10-15 GB on this bench (32 GB PC with two copies); the rig stops both under 0.5 GB free.
* WO-204's C1 and C2 scenarios are not defined anywhere this WO could find; S1, V1 (above), J1 (the join) and C3 (combat) are.
