# WO-213 — The internet stream: the joiner sees the host's world in time

Version **0.64.0**. Measured on the bench (two copies of the game on one PC, joined through our own relay, started the launcher's way)
with the relay's link shaper between them, against the field's bundles of four internet sessions. Every new mechanism ships **on** with
its own switch; its bench twin is the same build with that switch off. Nothing built here reads input: `tools/Check-NoInputCapture.ps1`
passes on every binary and script, test tools included, first and last.

Roles only: "the host", "the joiner". "A copy" is the joiner's local body of one of the host's NPCs; "the figure" is a player's body in
the other player's game.

## What the field said

- **The link queued only while a world crossed it.** Every world transfer (the join, the rejoin) ran at 244-246 KB/s in all four
  sessions and pushed the joiner's ping to ~1 s while it ran: Steam's own default send rate (256 KB/s), reached by nothing else.
  Outside transfers the ping was p50 53 / p99 110 ms. One session had a real ~4 s stall both ways; the reliable stream then delivered
  everything it held at once.
- **"176 ms" was not the network.** It was measured after the joiner's frame queue, over idle windows too. The host sent its own figure
  every 58 ms at the median and every 113 ms at p90 while moving (9-17 Hz): the agent waited for a game frame on every read.
- **Most of what looked like jitter was the receiving side:** the owner found a host NPC's posture missing in 18 % of its checks and
  reseated copies for it; the first line after a pick was cut by the next.

## A: the instruments

Every 10 s unless said: `MP-LINK kinds` (both agents and the relay: messages and bytes per kind, each way), `MP-LINK out` (the agent's
send queue: depth, oldest, coalesced, dropped, the lock's wait and the write's time), `MP-LINK steam` (every 2 s on a Steam link: ping,
quality both ways, pending bytes, the send rate, each lane; the settings read back once with Steam's result codes), `MP-LINK relay`
(each client's queue and scheduler), `MP-LINK arrival` (the receiver: inter-arrival p50/p90/p99 and age on arrival per kind; moving
bodies silent over a second), `MP-LINK npc-bands` (each NPC's rate at the joiner by distance band). The bug report's briefing opens
with a **Link** section built from these: the stream per kind, Steam's numbers and settings, arrivals, fighting NPCs' rate, the
scheduler's counts, and every gap over a second with the lines just before it. The bench's shaped link adds `RELAY-SHAPER`.

## A.7: what the stream needs, and the cap

The 0.63.9 behaviour with Part A's instruments, a fight (two bandits, the joiner fighting) and a tavern (seated drinkers, the innkeeper
serving), through the relay. What the relay sent the joiner, averaged over each run (a join's world transfer is about 1.4 MB):

| kind (B/s to the joiner) | fight | tavern |
|---|---|---|
| NPC states | 1,034 | 2,567 |
| postures (npc-anim) | 144 | 572 |
| activities and their extras | 329 | 582 |
| combat actions, opponents | 115 | - |
| the host's figure | 25 | 12 |
| everything in play | **~1.7 KB/s** | **~3.8 KB/s** |
| to the host (the joiner's figure, vitals, pings) | 118 | 49 |

Then the same scenes through the shaper capped at 128 KB/s and at 256 KB/s (26 ms each way): the link's queue built **only while the
join's world crossed it** -- at 256 KB/s frames waited up to 1.0 s (197-229 KB in the send buffer), at 128 KB/s up to 2.0 s -- in
both scenes, and never in play. (One later stretch of the tavern's 256 KB/s run, with the buffer empty, was a build on the bench's own
PC making the shaper's timers late.) **The cap explains the field's one-second pings during every join and nothing else:** the stream
in play is about 1 % of Steam's 256 KB/s. What the field saw in play came from elsewhere: the figure's own send rate, the 10 Hz NPC
cap, the reliable stream's hold through a stall, and the receiving side (the owner's missing postures, the pipe to the game).

## What changed

Each part ships on with its switch (`mp_w213_status` lists them; the link's own take effect at the next connection). The twin is the
same build with the part off (the bench: `KCDMP_TEST_W213_OFF=<names>` for the relay, both agents and both DLLs).

**B. Steam's link (`mp_steam_cfg`).** On a Steam connection both ends set and read back: send rate min **262,144 B/s** (Steam's own
default, and the rate every field transfer held), max **1,048,576 B/s** (default 262,144: Steam's own estimate may now climb where the
path allows), Nagle **0** (default 5,000 us: a state message never waits for company), and **two lanes**: events reliable on lane 0,
state unreliable on lane 1, **equal priority, weights 3:1** rather than strict priority. Measured on Steam's own send path (a loopback
socket pair through the real library): with the defaults a 1.5 MB transfer ran at 253 KB/s and a state message beside it waited
**2.0 s** (p50 1,998, max 2,064 ms) -- the field's ping during every join; with a strict lower-priority state lane no state got
through at all while the transfer ran (0 of 39); with weighted lanes state arrived in **4 ms** (p90 6, max 8) and the transfer kept
its 253 KB/s. Raising the minimum to 512 KB/s doubled the transfer's rate, but a minimum is sent whatever the path holds, so it stays at
the rate the field proved. The send buffer stays Steam's 512 KB. End to end, the relay's own scheduler (C) on that path: an NPC's state
sent every 100 ms beside a 1.5 MB event burst arrived **59 of 59 times, p50 4 ms, max 20 ms**, and the burst still took 6.0 s; with
Steam's defaults (the twin) **none** arrived until the burst was over. (The first try found two faults, both fixed: the events spent the
state's send budget, and the events-first rule waited on a full event lane; now the events wait in the scheduler while their own lane
holds 64 KB, and only the state spends the state's budget.) Twin: Steam's defaults (one reliable lane).

**C. Latest wins (`mp_link_sched`, `mp_state_unreliable`).** Every state message (positions, NPC states, postures, opponents) has
one slot per body and kind -- in the agent's send path, in the relay's forwarding to each client, and in the joiner's pipe to the game
(after a backlog each body keeps its two newest samples; the samples go to the DLL in one round per game frame): a newer state replaces
the older one that has not left. Events (actions, hits, deaths, conversation lines, the world's chunks, the activity extras) go first,
in order, and are never dropped. A state older than 500 ms is dropped unsent. Where the link can (Steam's state lane, the bench's shaped
link) state travels unreliably; the relay repeats postures and opponents about once a second so a lost one is soon replaced, and the
joiner leaves out a repeat equal to what it already applied (the host's own 2 s resend still passes). Negotiated after the handshake
(link hello, version 1, capability bits); a partner on another build is refused by the build id before that. Twin: C off (the old
first-in-first-out queue, everything reliable).

**D. The budget and the bands (`mp_link_budget`, `mp_npc_bands`, `mp_npc_compact`, `mp_figure_band`).**
- D.1: the relay sends state within the link's own rate (Steam's send rate, the shaped link's cap) less 20 %, keeping at most 16 KB in
  the transport's own buffer, so a backlog stays where latest-wins applies.
- D.2: the host sends each NPC at its band's rate (config values, `KCDMP_W213_BANDS`): **fighting within 15 m 30 Hz, within 40 m
  20 Hz, within 150 m 10 Hz, beyond 2 Hz**; an NPC that did not change is still skipped (its 2 s heartbeat), a fighting one goes out
  every period of its band. Before: at most 10 Hz within 60 m, and from 60 to 150 m only the 2 s heartbeat. "Fighting" is a weapon in
  hand, engaged, **or a combat opponent** (the host's own read of each NPC's opponent): the bench's bandits fighting the joiner's
  figure report no weapon in hand, and the first build sent them in the 40 m band.
- D.3: NPC states to the joiner in a compact form (an id per link, positions as cell and centimetres, yaw in tenths of a degree).
- D.4: the figures in the top band. The DLL answers the agent's read of its own player from a read made once a frame on the main
  thread (the pipe serves one main-thread request a frame, and the position loop had waited for one on every round); that read goes
  first at the agent's pipe gate, at most every 25 ms, and the other calls wait on a line of their own, so it waits for one call at
  most; the figure's change gate is the NPC sender's 1.5 cm / 0.01 rad (5 cm had a walk
  cross it every second round); the relay never holds a position back for the budget; and on the receiving side the DLL takes every
  sample while the game's Lua figure update keeps its old pace (at most every 50 ms).

**E. The joiner's play-out (`mp_playout_adaptive`, `mp_posture_hold_late`, `mp_posture_no_pos`, `mp_line_latency`,
`mp_figure_playout`).**
- E.1: each copy plays its stream a delay behind the host = that stream's inter-arrival p90 (gaps while moving) plus 25 ms, within
  50-300 ms, recomputed every second and changed smoothly; past the newest sample at most 150 ms of extrapolation, then hold.
- E.2: the owner holds a copy's last posture while its host NPC's state is late, up to 2 s; late is counted apart from missing and
  never reseats; after those 2 s the body is asked for again.
- E.3: a seated, lying or working copy takes no position write while its posture holds and its body stays in place (within 0.3 m of
  its host NPC's newest position); a body found off its place takes the stream every frame for the rest of that sitting, as before.
- E.4: a conversation line's age is measured where it arrives. Inside its window (the measured line latency plus its length) it plays
  with its voice for what is left of it, after the line before it has had its time (while that still leaves it a second); only a line
  past its own end is a subtitle alone, and the first line after a pick is always voiced in full.
- E.5: the figures play on the same delay as the copies.

**G. The player.** The joiner reads "Your connection to the host is struggling: NPCs may look jumpy" after the link has been poor for
10 s (Steam's quality under 0.75, over 64 KB pending or a ping over 300 ms, five checks in a row; a TCP link: a ping over 400 ms), never
for one spike, at most every 2 minutes. Tester script: `docs/TESTER-SCRIPT-0.64.0.md` (a 20-minute internet session over Steam).

## F.3: before, after, field

Two copies of the game on one PC, joined through the relay's shaped link. **Before** = the final build with every WO-213 switch off,
run the same day as its **after** (every part on); a cell is before → after. **field-0** is the field's link: 246 KB/s, 26 ± 10 ms each
way, 0.5 % loss, two stalls a minute of 0.3-0.9 s. **tight**: 128 KB/s, 35 ± 15 ms, 1 % loss, three stalls a minute of 0.3-1.0 s.
**Field**: the field's own 4-session bundles (one session's numbers where the measure needs the probe). Single runs vary: the owner's
missing postures spread 6-21 % over four tavern after-runs with the same parts on, so that row is the final run, not a trend.

**field-0**

| measure | target | fight | tavern | village | talk | session | field |
|---|---|---|---|---|---|---|---|
| fighting NPCs at the joiner, Hz p50 | ≥ 25 | 3.3 → 25.9 | (no fight) | 3.2 → 28.7 | - → 28.4 (a villager's attack) | 4.7 → 28.3 | - |
| NPC states: inter-arrival p50 / p90, ms | - | 118/226 → 37/63 | 110/135 → 79/121 | 111/158 → 78/127 | 110/137 → 86/122 | 110/146 → 77/124 | - |
| NPC states: age p90, ms (worst 10 s window) | ≤ 100 | 26 (330) → 25 (187) | 32 (400) → 21 (233) | 41 (331) → 22 (253) | 66 (399) → 23 (254) | 36 (378) → 25 (438) | - |
| moving NPCs silent over 1 s | the link's stalls | 266 → 222 | 1,691 → 531 | 2,897 → 1,211 | 2,254 → 1,220 | 5,294 → 1,626 | - |
| the host's figure sent while moving, p50 / p90 ms | ≤ 33 p50 | 336/456 → 48/115 | 106/146 → 49/95 | 140/215 → 33/53 | 321/321 → 40/100 | 137/253 → 35/69 | 58/113 |
| figure walk: the joiner sees the host, p90 / delay | ≤ 1.5 m | - | - | 0.11 m, 300 ms → 0.10 m, 100 ms | - | 0.15 m, 400 ms → 0.08 m, 100 ms | - |
| copies missing their posture (the owner's checks) | down | 0.2 % → 0.1 % | 18.9 % → 20.6 % | 12.8 % → 8.6 % | 34.1 % → 12.8 % | 17.4 % → 15.8 % | 18.5 % |
| seated copies that moved > 5 cm while their host NPC sat still | 0 | 0 → (probe rotated) | 7 → 9 (of 40-43 k) | 58 → 20 (of 12-22 k) | 0 → 1 (of 8-20 k) | 0 → 22 (of 24-30 k) | - |
| the first line after a pick as a subtitle alone (E.4) | 0 | - | - | - | 0 → 0 (42 late lines voiced in their window) | - | - |
| relay → joiner, KB/s | ≤ 10 % of the cap | 4.5 → 5.7 | 6.0 → 7.3 | 7.3 → 10.6 | 6.1 → 7.9 | 6.0 → 5.9 | - |
| fps host / joiner, p50 of the DLL's 60 s frame counts | no drop | 35/72 → 30/72 | 48/72 → 53/69 | 48/66 → 46/64 | by half: 62/40 → 66/40, 50/69 → 42/63 | 51/72 → 41/61 | 64/63 |

**tight**

| measure | target | fight | tavern |
|---|---|---|---|
| fighting NPCs at the joiner, Hz p50 | ≥ 25 | 3.0 → 23.5 | - |
| NPC states: inter-arrival p50 / p90, ms | - | 119/247 → 41/67 | 111/576 → 94/125 |
| NPC states: age p90, ms (worst 10 s window) | ≤ 100 | 38 (370) → 27 (248) | 181 (1,725) → 26 (496) |
| moving NPCs silent over 1 s | the link's stalls | 201 → 425 | 2,166 → 473 |
| the host's figure sent while moving, p50 / p90 ms | ≤ 33 p50 | 358/444 → 47/111 | 345/453 → 38/72 |
| relay → joiner, KB/s | ≤ 10 % of the cap | 4.5 → 6.1 | 5.4 → 5.1 |
| fps host / joiner, p50 of the DLL's 60 s frame counts | no drop | 33/70 → 28/71 | 57/70 → 59/74 |

What the table says:
- **The rates met their targets where they could.** The fight band reached 26-29 Hz at the joiner (24 on tight), against 3 before; the
  host's figure goes out every 32-49 ms at the median, against 61-358. The fight band's p50 stays a little under 30 Hz: the sender runs on
  the host's frames (28-43 fps in the fights).
- **The figure walk alone** (two 40 s walks, no crowd): the host's figure every 61/99 → 32/46 ms; each side sees the other 0.08 m off
  at 100 ms of delay, against 0.08-0.12 m at 200-300 ms; fps unchanged (81/103 → 80/105).
- **The age target holds at p90, not in the worst window.** A stall of up to a second leaves the newest state that old; the 500 ms
  limit then drops what is older. That trade shows on tight's fight: more NPCs silent over a second (201 → 425), none older.
- **The link carries little.** 5-11 KB/s to the joiner, 2-8 % of the shaped link.
- **Seated copies** move about as much as before where the host NPC sits still (the before wrote them every frame), and less overall
  (the village 137 → 37 of all seated samples' moves). Half the session's 22 came in one frame after the joiner's game froze for 2.6 s
  (the talk step's teleports): lying copies put back onto their host NPC's spot.
- **The session's fps fell over the whole run** (51/72 → 41/61), not in its scenes: by step the after-run was as fast or faster in the
  village (52/75 → 58/82), the figure walk (49/74 → 59/93) and the tavern (33/62 → 38/61), the host lost 6 fps in the fight (below), and
  the talk step did different things -- before, one talk with a villager for 6.5 minutes; after, no villager would talk at 22:30 and
  the bench went to 14 in turn for 8 minutes. The joiner's mod time per frame there was the same (1.6 → 1.7 ms). The session's talk
  failed before and after alike, in all five session runs of this work (the hour: villagers asleep or not talking).
- **The host ran a fight about 3 fps slower with the NPC bands on**, on the bench; the mod's own time explains a tenth of that (below).

F.4, the twins (the same build and profile, one part off):

| twin | where | with the part → without it |
|---|---|---|
| B (Steam's defaults) | Steam's own path, one PC | a state beside a 1.5 MB transfer: 4 ms → 2.0 s; with C: 59 of 59 → none until the transfer ended |
| C (latest wins) | fight, field-0 | NPC state age p90 25 → 69 ms, worst window 187 → 706 ms; to the joiner 5.7 → 9.2 KB/s; silent over 1 s 222 → 157 |
| D (budget, bands, compact, figure band) | fight, field-0 | fighting NPCs 25.9 → 4.2 Hz; NPC states 37/63 → 120/159 ms; the host's figure 48/115 → 342/483 ms |
| E (play-out, late hold, seated hold, figure play-out, line latency) | tavern, field-0 | seated copies moving 0.06 % → 0.16 % of samples; no measure here moved with E.1 or E.2 |
| E | talk, field-0 | copies missing their posture 12.8 % → 29.3 % of the owner's checks, reseats 772 → 1,468; late lines voiced in their window 42 → 0 (none a subtitle alone either way); NPC state age and the figure unchanged |

**The host's fps in fights.** By the DLL's own frame counts (60 s windows over the fight) the host ran ten fights at 27.6-36.2 fps; the
joiner did not change (63-74 either way), nor did the host in the village, the figure walk or the tavern. Two fights split D: with the
NPC bands alone the host ran at 31.1 fps, with the figure band alone at 36.2. Over the ten, five with each switch on and five off, the
NPC bands go with about 3 fps less (30.3 against 33.2 on average; the same-day pairs 34.5 → 30.3 on field-0 and 32.9 → 27.6 on tight),
the figure band with none measurable (31.3 against 32.2). The mod's own work does not account for the 3 fps: the sender ticks 26
times a second instead of 8.5 and takes 2.0 ms of the host's main thread a second instead of 0.8, and the mod's whole time per frame
rises by at most 0.4 ms (about 0.3 fps at 30 fps). Single runs spread 9 fps, so whether two PCs show the rest is the tester session's
to measure; `mp_npc_bands off` restores the old rates. Every part ships on. (An earlier count of the
agent's once-a-minute fps samples had put the cost on the figure band; the DLL's frame counts do not.)

## Found on the way (fixed in this build)

- **The joiner's pipe to its game.** The agent talks to the DLL one request at a time, and the DLL answers a main-thread request once
  a frame. Putting D.4's position read and C's NPC feed first at that gate starved everything else on the joiner (its polls waited
  18 s at the median; a joined talk's listener took 66 s to join), and the relay's once-a-second repeats came back as one main-thread
  call each. Now: a call gives way to the priority calls once and to frames' calls at most 16 times, then keeps its place; the position
  read runs at most every 25 ms and the feed once a frame; the activity extras travel as reliable events; the joiner leaves out a
  posture or opponent repeat equal to what it applied (23,554 in one talk run); a joined talk's camera samples, answered off the main
  thread, take the priority path instead of the frames' one; and the Lua figure update keeps its old pace. Each with its test and twin.
- **Then the host's figure read waited behind the polls.** With every caller in one first-come line, the read went ahead only of the
  calls that still had their give-way left; on a busy host each waiting poll had already given way to an earlier read, so the next read
  waited behind all of them: 55 ms at the gate on average in the village, 101 ms in the fight (8 ms while give-ways were unbounded,
  85 ms before this work), and the host's figure went out every 168 / 216 ms at the median again. The other calls now wait on a line of
  their own and only its head waits at the gate: the read waits 9 ms (village) and 13 ms (fight) on average, and the host's figure
  goes out every 34 / 48 ms at the median. Test: a read after polls that gave way to the one before it is served within two calls;
  twin (one line): behind at least eight.
- **The fight band was empty.** Bandits fighting the joiner's figure report no weapon in hand: "fighting" now includes a combat opponent.
- **Each band kept its cadence only from the send.** On 22-30 ms frames a 33 ms period landed on every second frame (18-21 Hz); the
  next slot is now one period after the last slot.
- **Steam's two lanes end to end.** The events of a burst spent the state's send budget, and the events-first rule waited on a full
  event lane: an NPC's state got through 0 times beside a 1.5 MB burst; now 59 of 59 (see B).
- **The bench's probe file** was truncated under a reader when its rotation was refused (a figure walk's data lost): it now waits and
  appends.
- **E.3 let a seated copy walk off.** With position writes off for a held posture, nothing brought back a body that something else
  moved: in a joined talk one copy read as seated while it glided 24 m in 22 s, its host NPC still in his seat; in the village the
  owner's install of an animation that never started (tried again every 320 ms) yanked one copy 1-5 m each time. The writes every frame
  had covered both. Holding a body again after a while of writes failed both ways tried: with a 0.3 m bound the stream dragged a copy
  off its own seat (up to 1.2 m from where its host NPC sits) and the game put it back, a slide every 2 s; with 2 m the yanks and the
  writes alternated within a second. Now a body found 0.3 m off its place takes the stream every frame for the rest of that sitting --
  the old behaviour for that copy. Test: in place held; 0.31 m off written, and still written back in place; the next sitting held
  again. Twins: holding again after 2 s of writes; the posture alone (a body 24 m off held).

## Gates and the installer

- **Offline gates on the final source: 99 of 99 pass** -- native unit tests 10,258 (this work's rules among them: the bands' cadence,
  the play-out, late against missing, the seated hold's sitting); relay round trip 229; agent 2,055; setup 77; log bundles 79; 80
  synthetic Lua suites; the static checks; the relay message table (99) and fault-mode (37) gates; the digest analyzer (62) and rig (42)
  tests; the save tools (14, 7).
- **No-input-capture gate** (`tools/Check-NoInputCapture.ps1 -SelfTest`), first and last over every binary and script built here --
  the dev builds, the bench's tools and the Steam link probe included: PASS both times (72 binaries, 1,433 scripts), the self-test
  catching every planted capture.
- **The real saves:** checked before and after every bench run -- every check a match, mirror roots only.
- **The publishing gate** (found on the way): one tester's handle in this work's field bundles starts with a common word that another
  tester's mark note uses, and the rule that gates a note word starting a handle made every line with that word a hit. A note word the
  remote's main already uses as a word of its own is now the project's vocabulary, not the handle; the whole names still gate. Test:
  such a word passes and the handle in full fails; twin: the rule before fails the first.
- **The bench measured this code:** the dev build d15's native code section, agent, relay, protocol and Lua are identical to the
  installer's commit, which adds documents and the publishing gate's rule.
- **The installer** `KingdomComeTogether-Setup-0.64.0-905419f-2611030022.exe` is built from `905419f` with `tools/Build-Installer.ps1`
  (the soak waived as the WO says -- the safety gates are hard, the rest reported): build id `905419f-2611030022`, 107,783,113 bytes,
  sha256 `ebe5fd5d52aa27789df8f3125147c0a2f932c2915f420bdaaa38d251316b042c`, unsigned. The publishing gate, the live save round trip,
  the payload smoke (the published relay and agent as `0.64.0+905419f-2611030022`), the test installs (41 of 41), the payload's
  no-input-capture (963 binaries), no-input-tooling and no-screenshot gates and the relay fault gate all PASS; the real saves matched
  their hash list after the round trip too. Installers stay local.

## Open

- **The joined talk on the bench is noisy**, before and after alike in kind: the vote's own time is unchanged (157-263 ms before,
  129-186 ms on the final build), but runs lost talks to (a) the listener's game flagging combat danger right after it joined (a guard's
  arrest escalating at night, a villager attacking the host: the listening ends, as designed); (b) the first talk of a step joining a
  minute late (the bench's known first-join console delay).
- **The host as listener with its HUD in another state when the listen began** (2 of the usual 9 HUD clips visible): its dialogue keys
  do nothing for that talk. On this work's builds with every part on: 7 of 37 host listens, in 3 of 12 talk runs; none of 5 with
  every part off, none on the joiner, none of about 50 in earlier work. In the final build's run a villager attacked the host during
  its second listen (that listen ended for combat) and the fight was still on when the next two began; in the two other runs it came
  from the first listen with no fight. Not traced: the next step is to log which clips were visible, and the player's state, when a
  listen begins.
- **Both machines' pipes run near their capacity in busy scenes** (one request at a time, one main-thread answer a frame, 50-80
  calls a second): in the village the host's was busy 66 % of the time at the median (83 % at worst; 33 main-thread calls a second),
  the joiner's 53 % (79 %). The bounds and the own line keep every caller moving, but the lasting fix is to answer the owner's stream,
  the extras and the health writes off the main thread, as the NPC samples and the position read already are.
- **The host's fps in fights with the NPC bands on** (about 3 fps on the bench, F.3), of which the mod's own time explains a tenth: if
  two PCs show it, find where the rest goes before capping the fight band.
- **The owner keeps installing an animation that never starts** (the joiner's posture owner, from earlier work): it tries again every
  320 ms and each install moves the held body 1-5 m. E.3 now hands such a body back to the stream; the install loop itself is
  untouched.
- **The bench's 15-minute session reaches its talk at 22:10-22:30 game time**, when no villager starts a talk: the talk step failed in
  all five session runs, before and after alike. The session's talk needs an earlier hour or a talk first.
- **E.4: the voice cannot start mid-line**; a late line inside its window plays its voice from the start, its subtitle for what is left.
- **D.2's "beyond / not visible" band carries nothing**: the sender's cull (150 m) ends the stream first, and "not visible" would need an
  engine test per NPC.
- **B on the real internet**: Steam's settings were measured on its own send path on one PC (a loopback pair); how far Steam's estimate
  climbs above 256 KB/s on a real path is the tester session's to show.
