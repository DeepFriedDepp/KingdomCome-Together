# WO-114 — the leash (refreshed): findings

Solo work on one machine: the Modding Tools game, a throwaway save, a local
relay on its own port, `tools/wo121/avatarpeer` as the synthetic joiner and
`synthpeer --join-host125` as the synthetic host. Plus a read of both field
logs from the first two-player 0.30.0 session. Evidence marks: (observed),
(code-verified), (synthetic), (inconclusive). Teleports are checked by reading
the position back, never by "the call succeeded". No installer: `VERSION`
stays 0.30.0 (no version string was given). Screens: `docs/wo114-shots/`.

## 0. Answer first

- **Shipped (commits only, tree ready to build).** The host decides; the
  joiner is the one brought back. `mp_leash` on by default, **600 m warning,
  650 m pull** (the maintainer's numbers), a 10 s countdown that a return
  cancels and any hold freezes, then the joiner is placed 3 m beside the host
  with WO-124's placement. Protocol **v10** (two new join-channel messages).
- **Proven live, solo:**
  - host side: warning once at 605 m, nothing at 590 or 640, countdown at
    660, cancel at 640, no second warning at 605 until under 550, countdown
    frozen at 7 s for a 12 s "downed" window and resumed from 7, pull at zero:
    the synthetic joiner placed 700 m → 3 m (observed);
  - joiner side, the real player: pulled **772 m → 3.0 m** on foot and
    **771 m → 3.0 m** mounted (dismounted first, the horse left behind),
    read back three ways (observed, screenshots);
  - a pull while the joiner is on the map screen is refused as busy
    (observed);
  - deaths with the partner 400 m away wake **78 m** and then **86 m** from
    the partner (the second time not the last spot); with no spot inside the
    leash, beside the partner; a countdown running at the death is held and
    then cancelled, never a pull (observed);
  - a real engine fast travel by the host pulls the joiner beside it on
    arrival (376 m → 3 m) and its time skip reaches the joiner (observed);
  - a joiner's fast travel is refused by the engine and the game's own HUD
    says "Only the host can fast travel in co-op." (observed, screenshot).
- **Found live and fixed:**
  1. KCD2's fast travel is a **sped-up simulated walk** (the position walks
     the road), not a jump: the jump rule never saw it. The host now reads the
     engine's own `FastTravel: started...` / `FastTravel: ended...` lines.
  2. A fast travel plays `ApseOpen` and **never `ApseClose`**, so the agent
     read "paused" for good after every fast travel (a pre-WO-114 pause
     detector defect; it also held the owed pull forever). The travel's end
     now clears it.
  3. The mod's text rows do not draw while the map holds the timers: the
     refusal line is queued and the game's own HUD toast is used at once.
- **Mounted: dismount first, leave the horse.** The engine's
  teleport-with-horse is a quest-graph behaviour only
  (`PlayerAction_TeleportOnHorse`, a tag-point destination); no scriptbind or
  export reaches it (code-verified).
- **The fast-travel block is the engine's own switch**,
  `wh_pl_FastTravelEnabled 0`, set on joining, re-asserted every 5 s, given
  back on leaving; not persisted across a restart (observed). A/B from the
  same spot: at 1 the trip ran, at 0 the engine refused (observed).
- **Not done / inconclusive:** two machines (everything above is solo); a
  joiner fast travel started from the map by hand (needs input; the console
  route was used); a dialogue or cutscene hold on a real game (the holds are
  pinned by tests and a synthetic "downed"/menu run only).

## 1. What the first session measured (checked against the field logs)

Host CSV (1 Hz), host agent/native logs, joiner agent logs. The joiner stayed
in town; the host went away (farthest 1,276.6 m).

| claim | evidence | mark |
|---|---|---|
| world alive around the joiner out to ~670 m | 9–14 of ~54 nearby NPCs moving per second out to 524 m; the 670 m second is a burst (46/54) in the same second the host drops their physics | observed; 670 m is one unusual sample |
| frozen by ~807 m | 806 m: physics present on **0/53** NPCs near the joiner; 21:37:19–44 (628–1,277 m): no walking-size moves in any sample; back at 480 m | observed |
| stream "flicker every other second" | NPCs in the host's stream alternate 29/18/29/18 per second; stationary NPCs alternate 77–85 % at every distance: the host's native scan is every 2 s, the CSV samples every 1 s; no silence drops on the joiner | observed; what players saw on screen (inconclusive) |
| host stops drawing NPCs past ~100–140 m | hidden 8 % at 90–100 m, 59 % at 110–120 m, 98 % at 130–140 m | observed |
| every respawn used the same spot, 309–363 m out | all four deaths (two per player) → one road spot, "nearest ≥ 100 m", 309 / 363 / 352 / 361 m; three of four needed the goto-shaped fallback teleport | observed |
| fast travel in that session | none (no fast-travel time skip; the host's 130–150 m/s moves had a normal clock) | observed |

WO-128's "no edge" reading was about the brain-suspension bit; the edge the
players met is the host's physics range (the maintainer's numbers stand).

## 2. Phase 1 — the leash

### 2.1 Rules (host agent, `LeashLogic.cs`, unit-tested)

| rule | value |
|---|---|
| distance | horizontal (2D) host ↔ joiner — the recorder's measure |
| warning | past `mp_leash_warn_m` (600): once per excursion; re-armed under 550 |
| countdown | past `mp_leash_pull_m` (650): 10 s, one message a second; back inside 650 cancels |
| pull | at zero: the joiner beside the host (3 m, 8 directions, navmesh snap, fall damage held 3 s) |
| holds (freeze the countdown, start nothing) | either player downed/respawning, loading/joining, in a cutscene, a dialogue or a menu; host in a non-Henry stretch, reloading, or fast-travelling; no fresh joiner position |
| failures | not placed / no answer in 12 s: 20 s cool-down; 3 in a row: pulls off for the session, warnings stay (`mp_leash off`/`on` re-arms). "Busy" is not a failure |
| off | `mp_leash off`: nothing; a death wakes by the WO-113 rule |

Settings live with the other host settings (the mod's `KCD2MP.w114`,
mirrored to the agent by `wo114_cfg`, presets clean = on / legacy = off). The
joiner receives the host's values (Leash kind Config, every 10 s).

### 2.2 Words (plain)

| who | when | text |
|---|---|---|
| joiner | warning | "You're getting far from your host. Head back, or you'll be brought back." |
| host | warning | "<partner> is getting far away." |
| joiner | countdown (row, centre-top) | "Bringing you back to your host in N..." |
| joiner | cancel | "You're back near your host." |
| joiner | pulled | "You were brought back to your host." / "Your host fast-travelled." |
| host | pulled | "<partner> was brought back to you." |
| joiner | fast travel refused | "Only the host can fast travel in co-op." |

### 2.3 Wire (protocol v10, `ProtocolWo114.cs`)

Two rows on the WO-123 join channel (same routing; the relay needed no code):
Leash 0x58/0x59 host → one joiner `[kind][seq][arg:2][hostX,Y,Z:12][distM:2]`;
LeashState 0x5A/0x5B joiner → host `[flags:2][pullSeq][result][fromM:2][toM:2][residualCm:2]`,
once a second and on any change. A v9 relay drops unknown types, so v9 and
v10 refuse each other at Handshake (relay test).

### 2.4 The pull on the joiner

Busy check (fresh dialogue read) → dismount (WO-124 6a: `ForceDismount`, read
back, up to 6 tries; never teleported while mounted) → `JoinPlace` (pipe 0x1C)
→ log `MP-LEASH pulled from=<m> to=<m> residual=<m>` → the emitter's position
read again 1.5 s later → result to the host.

### 2.5 Mounted

Carrying the horse: not reachable. `PlayerAction_TeleportOnHorse` is a
behaviour-tree node in the quest data taking a tag-point WUID; no DLL
contains it; no scriptbind teleports the player with the mount
(code-verified). Shipped: dismount, teleport, the horse stays (observed:
`WO114-DISMOUNT was=yes force=ok mounted=no`, one try; the horse read back
where it was).

## 3. Phase 2 — respawn within the leash (native, `wake_pick.h`)

For a death with the other player in the world (agent → pipe 0x20
SetPartner, about once a second; forgotten after 10 s or when the pipe
closes): spots ≥ 100 m from the death → within `mp_leash_warn_m` of the
partner → not this player's last spot if another is left → nearest to the
partner; none → beside the partner (WO-124's ground search + the XGenAI
teleport; the settle check covers it like a spot); no partner or leash off →
the WO-113 rule. Executions and knockdowns keep their own rules.

| run | partner | result (read back) | mark |
|---|---|---|---|
| death 1 | 400 m north | spot "…road_1", **78 m** from the partner, 375 m from the death; landed 0.2 m off | observed |
| death 2, same place, same partner | 400 m north | last spot skipped → "…quarry_1", **86 m** / 407 m | observed |
| host teleported back (376 m) between deaths | — | read as a host fast travel: the synthetic joiner pulled along (pull #2) | observed |
| leash 60 m (forced), partner 400 m away | beside | "no spot within the leash -- beside the partner", woke 2.2 m from them | observed |
| the same death with a countdown running | — | `held (host-downed)` at 8 s, then `cancelled (back-inside, d=3 m)`: no pull | observed |

The respawn's own 375 m teleport never counted as a fast travel (the jump
rule skips downed/loading) (observed).

## 4. Phase 3 — fast travel together

- **Host:** `FastTravel: started...` → the joiners' countdowns hold
  (`host-travelling`); `FastTravel: ended...` + 1.5 s → a pull with the
  fast-travel reason, no countdown, unless the joiner is within 50 m. Also: a
  settled clock jump with ≥ 100 m moved (fallback), and a ≥ 200 m jump
  between samples (teleports). Live: pull #1 `reason=fast-travel` 376 m → 3 m
  (observed). The first try failed on the simulated walk (§0 fix 1) and then
  on the stale pause (§0 fix 2); both fixed and re-run.
- **Joiner:** blocked with `wh_pl_FastTravelEnabled 0` (§0). The engine's
  refusal line `FastTravel: unable to start fast travel ...` → the HUD toast
  over the map at once, the plain line when the map closes, and the host's
  log gets `fast-travel-refused` (observed). If a fast travel ever starts past
  the block, the joiner is told, its clock jump is **not** reported to the
  session, and the leash brings it back (code-verified).
- **Other levers looked at:** the player table's `FastTravelEnabled`
  (Godwin's false) is static data applied on a player switch; the scriptbind
  `EnableFastTravel` writes a field saved with the character, which would ride
  into the joiner's Henry snapshots; the `no_fast_travel` action filter needs
  `EnableActionFilter`, which is not registered (code-verified). Not used.
- **Time skip:** the host's fast travel is reported as TimeSkip kind 2 (the
  clock-jump watcher; `sent done kind=2`) and the joiner applies it: a
  synthetic host's skip to 758,827 moved the real joiner's clock 755,227 →
  758,975 (observed).

## 5. Phase 4 — the live runs

| # | item | result |
|---|---|---|
| 1 | warning at 600, countdown at 650, cancel back inside (real host, synthetic joiner) | **pass** (observed) |
| 2 | no pull during a scripted downed window | **pass** — frozen at 7 s for 12 s, resumed at 7 (observed) |
| 3 | hysteresis | **pass** — 580 m then 605 m: no warning; 540 m then 605 m: warning (observed) |
| 4 | real joiner pulled beside a far synthetic host, on foot | **pass** — 772 m → 3.0 m; placement readback, settle readback and a Lua position probe agree (observed, screens) |
| 5 | … mounted | **pass** — dismounted, 771 m → 3.0 m, horse left (observed, screens) |
| 6 | respawn, partner 400 m, within 600 m and not the last spot | **pass** (observed, §3) |
| 7 | host fast travel pulls the joiner | **pass** after two fixes (observed) |
| 8 | joiner fast travel refused with the message | **pass** on the console route; the map route needs input (observed / inconclusive) |
| 9 | time skip reaches the joiner | **pass** (observed) |
| 10 | a pull while the joiner is busy | **pass** — on the map: `refused -- joiner-menu`, the host got `busy` (observed) |

The dev build prints "Teleport outside PrecacheMode … moved 770 m in a single
frame … pop-ins will happen" on every long pull, as on WO-113's wake
teleport (observed). A short fade around the pull would hide it (carried).

## 6. Gates

Agent unit tests 375 (38 new), relay 50 (2 new: v9 refused, leash both ways),
native 47 (14 new: the wake choice), all 26 synthetic suites (new
`Test-WO114Synthetic`, 59; WO-108's preset count 32 → 33), both static
checks, the solution and the two test tools build. `Verify-Install` knows the
WO-114 markers. No installer.

## 7. For the first two-player session (predictions)

| # | prediction | P |
|---|---|---|
| P1 | the partner past 600 m gets the warning once; the host gets its line | 0.9 |
| P2 | past 650 m: the countdown, then the partner beside the host, `MP-LEASH pulled` with residual < 1.5 m | 0.85 |
| P3 | no pull while either player is dead, loading, talking or in a menu | 0.85 |
| P4 | a death with the partner near wakes within 600 m of them, not at the same spot twice | 0.85 |
| P5 | the host's fast travel brings the partner along within ~3 s of arrival | 0.75 |
| P6 | the partner's map fast travel is refused with the message | 0.7 |
| P7 | no false fast-travel pull from a host death, load or join | 0.9 |

Failure lines: `MP-LEASH host: joiner N held (...)` that never clears (a hold
source stuck — the pause flag was one); `pull #N result=failed:not-placed`
(no ground beside the host); `MP-RESPAWN-LEASH ... today's rule instead`.

## 8. Carried forward

1. A fade around the pull (the pop-in warning above).
2. The joiner's hold for a dialogue/cutscene is read from the joiner's own
   game; untested on a real conversation.
3. The pause detector's `ApseOpen` without a close may have other sources
   than fast travel (only fast travel was seen).
4. The field flicker is the host's 2 s NPC snapshot for stationary NPCs:
   belongs to the NPC/combat WO, not the leash.
5. Three of four field respawns needed the goto-shaped fallback: the spot
   teleport declines often; unchanged here.
