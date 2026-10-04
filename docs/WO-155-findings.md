# WO-155 — hits never knock a player down; the figure falls only on death (0.45.1): findings

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

Marks: **[log]** read in the maintainer's field zips (0.45.0, host and joiner; never committed); **[L]** a live solo
session on this machine as the host with a scripted partner; **[J]** a live solo session as the joiner of a synthetic
host; both on a throwaway copy of a save, with frames where it is visual (`docs/wo155-shots/`, contact sheets of
screenshots of the game window; the numbers in the text are from the game's own logs); **[unit]** / **[syn]** /
**[native]** a unit, synthetic or native test; **[code]** read in the code; **[not live]** no run in the game. No names,
paths or addresses of the field's players are in this file.

## The answer

**A hit no longer knocks a player down, and a player's figure falls on the other screen only when he dies — or, for a
friendly-fire hit you landed yourself, with the game's own knockdown and get-up animations.** Cause confirmed
(WO-151 §1.1 held): the joiner's game applied every blow of the host's NPCs as the game's scripted hit
(`TakeDamage` with two arguments, `suppressHitReaction: false` in `ApplyPlayerHitAsync`), which knocks a player down
whenever he is not in a combat stance. 9 of the field's 10 knockdowns each followed a `[playerhit] took N damage from
an NPC in the authority's world` by 0.19–0.23 s; the 10th followed a friendly-fire hit by 0.25 s **[log]**. The
host's mirror then ran the ragdoll and `Revive`: the frozen animation state and the T-pose the maintainer saw.

| | What was wrong (the evidence) | What 0.45.1 does | Proof |
|---|---|---|---|
| 1 An NPC's or an animal's blow | The joiner went down 10 times in his native log (14:58:15–15:00:55, 3.0–6.9 s each, `WO154-DOWN … DOWN` then `up again`): 9 after a host-NPC blow (0.19–0.23 s), 1 after a friendly-fire hit; on the host 10 `WO154-FALL`/`WO154-RISE` pairs **[log]** | The blow goes in through the four-argument call with `SuppressHitReaction` (`mp_hit_knockdown`, default **off**): health and stamina exact, no fall; a death kills as before | **[L1]** the plain hit on the local player: `DOWN` 0.27 s later, up 3.8 s after; three suppressed hits: health 71.8 → 66.8 → 61.8 → 56.8 → 51.8, no `DOWN`. **[J1]** 10 host-NPC blows on the joiner: 71.76 → 67.26 → 58.26 → 51.76 → 39.76 → 36.76 → 28.76 → 23.76 → 13.26 → 6.26, **0** `WO154-DOWN` edges, every line `(no knockdown: WO-155)`; the 10th blow (6.0 at 6.26 hp) killed him as before (the death guard, grave, wake 257 m away, 6.8 s hold) |
| 2 The figure falls only on death | The mirror ragdolled the figure on every Downed edge and stood it up with `Revive`; `ok=true` meant only that the call returned | A **death** lays the figure down where it stands (the engine's ragdoll, held on the ground) until the respawn removes it and a fresh figure stands where he woke; nothing else (a knockout, the death guard's knockdown, a blow) makes it fall; no `Revive` ever runs on a living figure | **[L5]** host screen: collapse, lying on the ground, removed at the respawn, a fresh figure 40 m away (frames 6). **[J4]** the host's figure on the joiner's screen: the same (frames 7). The hold: a body forced 1.0 m under the ground vanishes, the hold puts it back and it is visible again (frames 8) |
| 3 Friendly fire | The victim's own knockdown was mirrored by the same ragdoll and `Revive` | The victim falls on his own screen as before and **never twice**: not while he is down, nor for 5 s after the hit that knocked him (`mp_ff_knockdown`); on the **attacker's** screen his figure falls and gets up with the game's own `Actor.Fall` and get-up animations, proven by frames (frames 2, 3, 5), and only for a down edge that follows a hit the watcher sent | **[J3]** the victim's own screen, hits at +0.0 s, +1.8 s, +4.7 s, +11.2 s: `allowed` (DOWN at +0.22 s, up at +3.7 s), `no(he is down)`, `no(inside the 5 s window)`, `allowed` (DOWN again); health exact each time (frames 10). **[L4]**, **[J2]** the attacker's figure: stagger, lying on the grass, get-up, standing in a normal pose with its animation playing, no T-pose (frames 2, 3, 5); a down edge with no hit of mine: the figure stays (frames 4) |
| 4 The T-pose | Found on the way: **any avatar bound to the native writer stands in a T-pose for 5–8 s** (a fresh spawn with a stream, the body of a figure that fell and was taken back), however long it waited | The DLL starts the avatar's locomotion graph with a walk-class pulse 0.1 s after each bind (`WO155-NUDGE`) | **[L3]** frames 1 (the field's pose after a hand-back, before the pulse), 3 and 11 (10 frames after a hand-back and 10 after a fresh spawn: normal in every one) |

## Decisions made unattended

1. **The friendly-fire window**: a hit that knocks the victim down opens a window of **5 s**; inside it, and while he is
   on the ground, every friendly-fire hit goes in suppressed. The window opens only when a down edge follows the hit
   within 1.5 s (a hit on a player in a combat stance knocks nothing and opens none). Chosen: 5 s from the knocking hit
   (a knockdown lasts 3.7–5.2 s, so he is also protected for part of the time he is up). The rule is
   `native/KCDMP/wo155_rules.h`, pinned by 18 native checks.
2. **A knockout (the game's own) or the death guard's knockdown**: no fall, no hide. The vitals flags cannot tell the
   game's own knockout from a mod knockdown (both are "down + knocked down") and a knockout lasts a minute: the WO's
   "if unsure, treat it like a hit". A death is "down, not knocked down" (and the 0x24 death packet).
3. **Who decides a friendly-fire fall**: the **attacker's** agent. It records the friendly-fire hit it sent
   (`_w155FfSentAt`) and lets his figure fall only for a down edge within 3 s of it. The victim's own knockdown and the
   5 s window are the victim's DLL. No new wire field (a pipe bit only, `mp_ff_knockdown`).
4. **The respawned figure is a fresh figure**, not the lying one stood up: removed 1.5 s after his respawn
   (`RemoveGhost`, then the next position spawns it: identity at spawn, name, outfit and isolation are the existing
   respawn path's, the same as after a save load). The lying body is hidden at once.
5. **A hold for the lying figure** (`WO155-HOLD`): a body more than 0.35 m under the terrain at its own spot is put back
   on it (a resting ragdoll was seen 0.2 m under the terrain's surface to 1.1 m over it, so only the terrain is a
   reference; a first version held the body to its own settled height and lifted it off the ground: PD3).
6. **The fall's hand-back**: the figure is let go only once the engine says it is up and idle **after** its get-up blend
   (`BlendRagdoll` seen and over, physicalization `alive`, animation `MotionIdle*`, 1.2 s); a fall never seen is not
   waited for (4 s), one that never settles is replaced by a fresh figure (20 s). The polling is `GetPhysicalizationProfile`
   and `GetCurrentAnimationState`, the engine's own readings (frames and log: `WO155-STATE`).
7. **`Actor.Fall`, not `RagDollize`+`Revive`, for the knockdown; `RagDollize` (held) for the death.** `Actor.Fall` is the
   game's own stagger, fall, lying and get-up; for a death nothing may get up, so the plain ragdoll lies.
8. **The native nudge is a core change** (the avatar writer): a 0.6 s pulse at 0.3 m/s along the body's own facing, 0.1 s
   after each bind, once per bind, only for an avatar that is not already walking. It was the only lever that cleared the
   T-pose (frames); a wrong class or direction would show as one small step at spawn. Off switch: none (it is a bind-time
   fix); the pulse is logged (`WO155-NUDGE`).
9. **Not in the mod menu**: the three new switches are console commands (`mp_hit_knockdown`, `mp_ff_knockdown`,
   `mp_avatar_falls`), like every behaviour switch; the menu is the player's page and these are not settings players
   asked for.
10. **The soaks and the installer**: see the build record below.

## The evidence, confirmed (the field's zips, both on 0.45.0)

* **Joiner** (native log): 10 `WO154-DOWN` edges 14:58:15.7 → 15:00:55.6: `DOWN` 14:58:15.737 / up 14:58:19.467 (3.7 s), 14:58:22.024 /
  26.041 (4.0), 14:58:49.572 / 52.716 (3.1), 14:59:30.870 / 36.576 (5.7), 14:59:39.668 / 42.736 (3.1), 14:59:56.008 /
  15:00:02.814 (6.8), 15:00:06.693 / 10.181 (3.5), 15:00:34.447 / 37.335 (2.9), 15:00:41.408 / 45.206 (3.8), 15:00:50.510 /
  55.643 (5.1). The first follows `WO121-HITS friendly-fire hit on the player … from ghost 0 -> applied` (14:58:15.484) by
  0.25 s; the others follow the nine agent lines `[playerhit] took 5.1 / 9.0 / 9.7 / 4.6 / 24.7 / 24.2 / 9.8 / 10.3 / 24.5
  damage from an NPC in the authority's world` (14:58:21.798 … 15:00:50.322) by 0.19–0.23 s. **[log]**
* **Host**: 10 pairs `WO154-FALL … ok=true` / `WO154-RISE … ok=true -- he stood up in his world (down 2.7–6.9 s)`,
  each `ok=true` with no check of the figure. **[log]**
* The WO's "about 13 minutes" is the length of the session; the ten edges are within 2 min 40 s of its fights. **[log]**

## 1. The blows (agent)

`ApplyPlayerHitAsync` (the damage authority's 0x22) passes `suppressHitReaction: !_w155HitKnockdown` to the DLL's
`ApplyDamage`, which calls `rttr::apply_damage_soul_ex(player, st, hp, null, 1)` (WO-151's four-argument call). Without the
four-argument export the old call runs (a log line says so). The host's own player, hit by the host's NPCs natively, is
not touched. [code] [L1] [J1]

## 2. The figure (Lua, agent)

* **Death** (`KCD2MP_W155AvatarCollapse`): signals — the 0x24 death packet and the vitals flags (down, not knocked down);
  whichever comes first (the second is a no-op). The avatar's writer lets go, `Actor.RagDollize`, a 1 s watch. His
  respawn (the vitals' "up" after a death, or 10 s of lying) hides it and, 1.5 s later, `KCD2MP_W155AvatarReplace`
  removes it. On the host the old hide-at-the-death-spot (WO-132) is off while `mp_avatar_falls` is on; the avatar still
  leaves its fights and its health is reset as before. [L5] [J4]
* **Friendly-fire knockdown** (`KCD2MP_W155AvatarFall`): `Actor.Fall`, the writer let go, a 0.4 s watch of
  `GetPhysicalizationProfile`/`GetCurrentAnimationState` (live, from the fall: `sleep` + `MotionIdle` 0–5.6 s, then
  `alive` + `BlendRagdoll` for 2.4–2.9 s, then `alive` + `MotionIdle*`; the hand-back at 9.3–10.5 s). [L4] [J2]
* **The T-pose** (`native/KCDMP/motion.cpp`): see decision 8 and the analysis below.
* **Switches**: `mp_avatar_falls on|off` (default on; off = 0.44.0's hide at a death, nothing falls),
  `mp_hit_knockdown on|off` (default off), `mp_ff_knockdown on|off` (default on). [syn] 74 checks (`tools/Test-WO155Synthetic`).

## 3. Friendly fire (DLL)

`hits::apply_pvp_hit` asks `wo155rules::FfWindow::suppress(now, downNow, lastDownEdge)` (the debounced DOWN edges of
WO-154 are exposed by `motion::local_down_now` / `local_last_down_edge_s`): suppressed hits take the four-argument
call, the others the old two-argument call that knocks him down when he is not in a combat stance; the agent's pipe bit
`0x80` suppresses all (`mp_ff_knockdown off`). The log line is `WO155-FF knockdown=allowed|no(he is down)|no(inside the 5 s
window)|off(mp_ff_knockdown)`. [native] 18 checks [J3]

## The T-pose, analysed (what the frames and the engine's own readings showed)

* The field's pose: the figure is taken back by the native writer after the fall. **Binding while the get-up blend runs**
  freezes the pose (the first frames, K0/K1: a hand-back at 7 s, the blend running to 8.6 s). **Waiting for the blend does
  not help by itself**: hand-backs 1.2, 2.5, 4.0 s after `MotionIdle` all left the figure in a T-pose for 2–5 s (PF3,
  settle trials; a hand-back after 8 s still showed it for the first 0.5 s). **A freshly spawned figure does too** when a
  position stream arrives with it (10 frames, 5 s, both with one packet and with a stream every 0.5 s): the T-pose is the
  animation graph's start-up under the writer, not the fall's. [L3]
* What did not clear it: `Actor.StandUp`, `wh_ai_ResumeNPC`, an unbind/rebind after the T-pose had already shown (it
  cleared it, but only after seconds of T-pose). What did: a partner's real 0.6 s step (the stream's gait), and its
  stand-in, the DLL's walk-class pulse. **Frames: 10 of 10 normal after the pulse**, from 0.5 s after the bind. [L3]
* So it is **not specific to this WO**: every avatar spawn since WO-121 has shown it for a few seconds when a stream was
  fresh. The nudge fixes the spawns too (the joiner's first sight of the host's figure, a figure after a respawn).

## Proof, as the WO asked (solo, throwaway saves, both roles)

**Host + scripted partner** (`tools/wo121/avatarpeer`, the host's agent and relay):
* A hostile commoner fought the partner's avatar; **10 blows** reached the partner (`got NPC hit (0x22)` ×10, hp 20.55 →
  5.18), every one with its damage; the figure's engine state stayed `alive` in 33 samples (`MotionIdle*` 32 times and one
  `HitDeath` reading: the local flinch of a hit, a bent pose for a moment), never `sleep`, `ragdoll` or `BlendRagdoll`;
  no T-pose; the avatar kept its animation (frames 9). The AI's fights are not reliable: the runs of this session gave
  10, 0, 1, 0, 0, 6 and 10 blows in 40–90 s; the run shown is the one that reached 10. [L6]
* The partner "died" (death packet + downed vitals): the figure collapsed on the host's screen and lay on the ground, not
  in it; a forced sink of 1.0 m made it vanish, the hold put it back (`WO155-HOLD sank 0.87 m … put back`) and it was
  visible again; the respawn hid it, `WO155-REPLACE` removed it 1.5 s later and a fresh figure stood 40 m away; the
  identity (`WO154-IDENTITY … 33 ok`) came with the spawn. [L5]
* A friendly-fire knockdown (the host's hit, the partner's down edge 0.3 s later and up edge 3.8 s later): the figure
  staggered, lay on the grass, got up (the engine's `BlendRagdoll` 5.6–6.9 s after the hit) and was handed back at
  9.3–10.5 s: standing in a normal pose with its animation playing in 10 frames after the hand-back (frames 2, 3). A
  down edge with no hit of mine: no fall (frames 4). [L4]

**Joiner of a synthetic host** (`tools/wo118/synthpeer` as the host; 10 blows, a lethal one, friendly fire):
* 10 host-NPC blows: see the table, **0** knockdown edges, health exact. "He can swing and block between them" is **[not
  live]**: this session sends no keys or mouse input (standing rule); what is shown is that he was never on the ground
  (`WO154-DOWN` counts physics that is no living entity) and that nothing took his combat actor. The two-player checklist
  item 128 swings between blows.
* His death (the 10th blow): the death guard, the grave at the death spot, the wake 257 m away, the 6.8 s hold, 0x3E
  sent; the game as on every death. [J1]
* The host's figure on the joiner's screen, the same two cases as above (a friendly-fire knockdown the joiner landed, and
  the host's death with the respawn): the same frames. [J2] [J4]

## Self-review — every item of the WO

| # | WO item | status | evidence |
|---|---|---|---|
| 1a | every NPC/animal blow on a player: four-argument, `SuppressHitReaction`; death as before | fixed and proven | [L1] [J1] |
| 1b | the host's own player, hit by the host's NPCs, untouched | kept | [code] |
| 1c | friendly fire keeps knocking the victim down, mirrored with the game's own animations, rise proven by frames | fixed and proven | [L4] [J2] [J3] frames 2, 3, 5, 10 |
| 1d | no stun-lock: a 5 s window after a friendly-fire knockdown | fixed and proven; the window recorded (decision 1) | [J3] [native] |
| 2a | `mp_avatar_falls` mirrors death only; collapses, lies until the respawn removes it | fixed and proven (plus the friendly-fire case above) | [L5] [J4] |
| 2b | the game's own animation if there is one; else a ragdoll held on the ground | `RagDollize`, held by the terrain rule | [L5] hold frames 8 |
| 2c | no `Revive` on a living figure, ever; the figure never stands up from a ragdoll | none in the code path; a fresh figure at the respawn | [syn] counts `revives == 0` |
| 2d | a knockout: only if it is the game's own state, else no fall | no fall (decision 2) | [code] [syn] |
| 3 | proof: host + scripted partner (10 blows; death), joiner of a synthetic host (10 blows; death), friendly fire both ways | done, except swings/blocks between blows | the sections above |
| D | docs, release notes, checklist §WO-155, tester page, installer | see the build record | — |

## What a follow-up needs

* **The two-player checklist §WO-155** (items 127–131): real guards on the joiner, real friendly fire both ways, a death
  on each side, swings and blocks between blows.
* **The game's own knockout** (a vanilla `unconscious` buff) is treated as no fall; if players want it shown, a state
  the other side can tell from the mod's knockdown is needed on the wire.
* **A figure that sinks** was never seen unforced (the field's sinking is explained by the old `RagDollize` + `Revive`
  and the writer taking a lying body back); the hold is a safety net proven against a forced sink.
* **The pulse's step** at every spawn is one small step; if it shows as a slide in the field, the pulse's speed or
  length (`kNudgeMps`, `kNudgeS` in `motion.cpp`) is the lever.

## The gates

On the tree of the shipping code (`8aac11e`; the commits after it are docs and the soak records): **native 406** (388 + 18 for
the friendly-fire window), **agent 1,097** (1,088 + 9), **relay 62**, **setup 64**, the launcher's build, the three static
checks (console placeholders 7, Lua locals 6, native guards 7) and **all 46 synthetic suites** (the new
`Test-WO155Synthetic` 74 checks; `Test-WO154GameSynthetic` 108, adapted: its fall/rise checks now describe the pre-WO-155
entry as the game's own fall) — **0 failed**. The same gates run again inside `tools/Build-Installer.ps1` for the shipping
build.

## The soaks (the mod only, as WO-154 ran them; window not focused by this session)

The WO's instruction ("the mod only, as WO-154 ran it"): `soak.py verdict --mod-only` records that the comparison with
the game without the mod is not made. Both runs on the throwaway save the 0.45.0 soak used (`playline4/autosave111`), 10
minutes each, the game as host with the relay and its agent, the WO-151 scene (three bystanders 3 m around the player,
an AI fight beside them from minute 3, a new pair whenever one goes down or stalls), **the committed code** (`8aac11e`).
The game window was never brought to the front by this session; it rendered at its full rate anyway (the DLL's own
`FRAME` rows read 72 FPS in the sessions before).

| | soak 1: the mod | soak 2: the mod + a joined partner through the fights |
|---|---|---|
| the first 2 minutes | 69.1 FPS | 68.6 FPS |
| the last 2 minutes | 68.2 FPS (−1.3 %) | 67.0 FPS (−2.3 %) |
| lowest row | 67.7 | 66.3 |
| the stat stack | 0 in all 55 rows | 0 in all 58 rows |
| `FAULT` lines | 0 | 0 |
| fight pairs | 5 | 3 (the partner landed an attributed hit on each attacker every 15 s) |

* **Lower than WO-154's 71–74 FPS and 0.65–0.67 ms of the DLL a frame — and not 0.45.1's doing.** The same scene with
  the 0.45.0 binaries (the maintainer's install, today, same save, same run): **68.8 → 68.1 FPS** first/last 2 minutes
  and the DLL's own cost 0.68–0.76 ms a frame, against 0.45.1's 69.1 → 68.2 FPS and 0.70–0.76 ms. The machine is
  3–4 FPS slower today than on the WO-154 day (`tools/perf/runs/ab0450-same-day.*`, kept on the machine: that folder is git-ignored); the 0.45.1 change adds nothing
  measurable. (The A/B run is not a gate; it is here because a 5 % lower number than the last soak is exactly what a
  regression would look like.)
* **The record** (`tools/perf/soak-record.json`): soak 2, PASS (the last 2 minutes within 10 % of the first 2, the stack 0
  in every row, no `FAULT` line, enough rows, the code committed); `soak.py check` says `soak PASS for this code (0.45.1 …)`.
  The run files are `tools/perf/runs/mod0451*.{json,csv,md}` (git-ignored, kept on the machine).

@@BUILD@@
