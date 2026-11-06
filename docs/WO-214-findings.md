# WO-214 — After 0.64.0 in the field: walking NPCs, owned copies, deaths, draws

Version **0.64.1** (the maintainer's). Measured on the bench (two copies of the game on one PC, joined through our own relay with the
field's link shape: 246 KB/s, 26 ± 10 ms each way, 0.5 % loss, two stalls a minute) against the two bundles of the 0.64.0 internet
session. Every new mechanism ships **on** with its own switch (`mp_w214_status` lists them); its bench twin is the same build with that
switch off (`KCDMP_TEST_W214_OFF=<names>` for both agents and both games). Nothing built here reads input:
`tools/Check-NoInputCapture.ps1` passes on every binary and script, test tools included, first and last.

Roles only: "the host", "the joiner". "A copy" is the joiner's local body of one of the host's NPCs; "the figure" is a player's body in
the other player's game; "the owner" is the joiner's animation owner of copies (WO-208).

## A — walking NPCs

**A.0, why the near band read 0.5 Hz.** It did not mean walkers were sent at 0.5 Hz. The band line's median counts **every** NPC in the
band, and most village NPCs stand: a body that moved no more than 1.5 cm on any axis since its last send (health and flags unchanged)
goes out only on its 2 s heartbeat — 0.5 Hz by design. A decision log on the host (`KCDMP_TEST_W214_DECIDE=1`: 60 s windows, ten near
bodies, one line per tick with the read position, the last sent one, the per-axis delta, the band slot and the verdict) shows both kinds
side by side: standing villagers 27–28 sends a minute, every one a heartbeat (largest delta 0.0 cm); a walking one 786 sends in the
minute, every tick a change (2–10 cm against the 1.5 cm test) sent at its band slot. The read is the live entity matrix every tick (no
cache). The field's "single-frame writes" of 3.7 and 7.8 m were the joiner's own engine moving copies off the last write
(`MP-NPCPULL`), not writes. The briefing now says it plainly in its Link section (`MP-LINK npc-moving`, the band rates of the bodies
that walked in the window): "walking NPCs within 40 m reached you N times a second".

**A.1, a turn is a change (`send_turn`).** The change test had one hole: a body turning in place was never a change and went out only
on its heartbeat — 437 decision ticks of one 0.64.0 village run had a body turned by more than 1° without a send. Now a turn of over
1° counts as a change and goes out at the band's slot. The bench's village: of the decision ticks with a body turned in place, 267
were sent as turns and 1 was left (2,448 turn sends in the session); the twin (`send_turn` off) sent none and left 154.

**A.2, the bands.** WO-213's bands stand: within 40 m every 50 ms, to 150 m every 100 ms. Walking bodies (a body counts as walking in
a 10 s window it moved in for at least 2 s; its rate is over that time) reached the joiner at a median 17–19 times a second within 40 m
and 7–10 to 150 m; the slowest tenth within 40 m (the bodies that start or stop in the window) at 5–16.

**A.3, the receiving side.** Blends of over 1 m without a 1 s gap in the stream are the receiver's, and are reported here, not fixed:
33 in the village run (most at an extrapolation's end, then at a hold's end; median 1.1–1.5 m); 232 in B's twin, where the re-binds
made them. The single-frame steps over 50 cm are the writer correcting copies the joiner's own engine moved away from its last write
(up to 36 m, `MP-NPCPULL`).

**A.4, the figure turning in place (`figure_turn`).** The figure's rotation counts as a change (over 0.01 rad), not only its position:
the host turning in place, the joiner's view of his figure followed at p90 **2.2°** (at 200 ms); the twin (`figure_turn` off) at
p90 25.8° (its best fit 950 ms behind: the turn arrived with the 2 s heartbeat).

## B — one bind per body (`one_bind`)

The field's joiner bound 155 bodies 2,894 times in 8 minutes (18.7 a body); 1,266 binds re-asked for a bound body reset it, 1,393
bodies were dropped for silence. Two loops did it. The mod sent a bind again when the DLL's answer had not come in 3 s — a busy joiner's
console answers later (on the bench 644 of 1,072 binds were re-sends) — and the DLL took a re-ask of a bound body as a new bind (a new
anchor, a blend). And the DLL dropped a body its stream had not spoken of for 4 s (two lost 2 s heartbeats), while the agent told the
mod a stream was silent after 3 s, which released the puppet: bind, drop, bind, on bodies standing in range. Now a re-ask of a bound
body changes nothing; a silent stream's body holds where it stands and is forgotten only after 30 s (the host stopped sending it: it
left the host's range); the mod's unanswered bind waits 10 s; the agent releases a puppet in the host's range only after 12 s of
silence (3 s beyond it, as before). The bench's village (13 minutes, the joiner following walkers, 93–100 bodies):

| | binds (a body) | re-asks that reset a bound body | silence drops | the mod's re-sends | releases in the host's range | blends over 1 m, no gap |
|---|---|---|---|---|---|---|
| 0.64.0 | 1,060 (11.4) | 552 | 234 | 644 | — | — |
| 0.64.1 | **182 (1.8)** | **0** | **0** | 2 | **0** | 33 |
| twin (`one_bind` off) | 648 (6.9) | 351 | 113 | 361 | 74 | 232 |

Every second bind of a body in the 0.64.1 run followed its release for leaving the host's range (77 of 77); the rest are the host's
figure, bound again at its teleports.

## C — owned copies

**C.1, the owner's let-go (`owner_base`).** The field: 116 let-gos ("far from the local player"), then reseats that could not install
for minutes (`path=unknown`, one body asked 23 times, its whole body empty 51 s). Now the copy gets a base in the same frame the owner
lets go of it (its host NPC's base, else its own neutral idle, else a nudge), a posture the owner cannot install falls back to the
neutral idle, and every path has a name (none is "unknown"). The empty-body count now comes from the owner's own watchdog over the
whole session (the probe saw only its last minutes). The joiner walking 70 m away and back, twice: no copy within 60 m stood with an
empty body for over 0.3 s, 45 let-gos all at 70 m, no reseat path "unknown". **Not demonstrated on the bench:** its twin (`owner_base`
off) read the same (0 empty, 42 let-gos at 70 m, no unknown path), and none of this part's own paths ran in any bench run (bases at a
let-go 0, postures refused 0). The bench never reproduced the field's failing reseat; the field session will say whether it holds.

**C.2, an action that is no longer the host NPC's ends (`combat_end`).** The field's four copies waited 203–249 s behind a
"C_CombatAnimAction". By its fragment's name it was the owner's own posture install (ReadingBook, Lying, SittingIdle, Guard: the owner
installs through the combat manager), still looping long after the host NPC stood up. The owner's withdraw only steps a running action's
priority down, and a looping posture never yields. Now a full-body action of that class on an owned copy is stopped the game's own way
and the host's base handed over, once its host NPC has been in a base for 1 s while it held the body that long with an action waiting
behind it. A combat row (a `Combat*` fragment) is stopped too once its host NPC is out of combat for 1 s, or once it outlived any row
(4.5 s). Only that class: a first version also stopped the engine's own walk transitions, and one copy's own movement system queued
them again every second (35 stops in two minutes, "Agent is stuck", the probe's sliding up). Now that never happens, and three stops on
one copy within 10 s rest the rule there for 30 s. Five fights (90 s each, three bandits on both players): 7 stale actions stopped
(2 rests after churn), no copy left stuck; the twin (`combat_end` off) stopped none and left one copy stuck and one in the bind
pose. The village: 4 stops (the first version: 210, in a loop).

**C.3, the idle flood (`anim_hold`).** The field's 1,030 animation-queue overflows had two writers. About half (503) were the
engine's own look poses on the copies of one caravan. The rest were the mod's own puppet layer: on bodies it writes itself, the
idle/walk clip flipped every 60 ms with the speed (`relaxed_idle_both` and the walk and sprint blendspaces), and each flip queued a
clip. Now a clip changes only after the current one held 0.4 s. The bench's village, overflows on people: 0.64.0 247, 0.64.1 **0**,
the twin (`anim_hold` off) 12 — most of the flood went with B, since a body that stays bound is written by the DLL with the engine's
own gait, not by the mod's clips (the twin's clip changes: 819 against 476). The look-pose half is not touched here: it shows on the
bench too, on the copies of one bandit group (400–800 a run, with the fix and its twin alike). So do two village horses (173–510 a
run, with every switch on or off): their clip's 1 s refresh piles up while their animation does not run.

## D — a death is never swallowed (`death_row`, `death_ground`, `reaction_rows`)

The field: the host NPC's death row reached its copy as a hit, and the joiner's pipe refused it. It refused **every** row on a copy the
owner owns, under the misleading name "engine-refused" — 23 of the 25 hit rows on the fought copy. WO-187's fallback then played a plain
hit, the death killed the soul as it stood, and WO-195's fall read the soul alive at 484 ms and stood down. Now the host NPC's hit and
death reactions play on owned copies (the owner refuses only other rows, under its own name, "owner-owns"); a death row is never
replaced by a plain hit — the copy's own weapon's death row, else the fall at the soul's death; and every death applied here ends on
the ground within a second (the soul read again up to three times; a body still living in physics after the host's death is
ragdolled). On the bench the joiner's copies of the fought bandits took **45 of 45** hit rows and 3 of 3 death rows (the field: 2 of
25).

The bench then found the field's standing corpse once more: the host killed two bandits, and both copies were finished the engine's
way (WO-210's lethal branch) yet stood on with their physics still a living entity — "still upright" 2 of 2, one after its own death
row played. A finished body was taken as down. Now a body still living after the finish gets the ragdoll as well.

| the host's kills, the joiner's copies | deaths on the ground | still upright | owned copies' hit / death rows played | refused |
|---|---|---|---|---|
| before the finish fix | 0 of 2 | **2** | — | 0 |
| 0.64.1 | **4 of 4** (the engine's own fall 2, ragdolled 1, finished then ragdolled 1) | **0** | 11 / 2 | **0** |
| twin (`death_row`, `death_ground`, `reaction_rows` off) | no kill in its 10 tries | — | 0 | **4** ("engine-refused") |

The bench's kills are few: the bench's Henrys fight with their fists once the fight has them locked on (a scripted draw is refused
then, and fists do no harm to a bandit), so a run of ten tries gave 0–4 kills.

D.3's count also flags the world's authored corpses (crime-scene bodies, an ambush victim): they lie by their animation while their
physics is a living entity (5 in one run, with every switch on). They are listed by name, not counted with the kills.

## E — the host NPC's weapon in its copy's hand (`draw_event`)

The field: "weapon draws played on copies: 0". The briefing counted a line nothing printed; the copy's weapon came from the host's hand
rows only once a fight had started, and the host NPC drew before. Now the host NPC's hand is its copy's at once, in a fight or not, and
held while the host's hand holds it (`WO214-DRAW ... played`; a body that never held a weapon is not told to sheathe). Five fights:
the host's NPCs drew 27 times and their copies played **26**; 18 of 21 were told within 500 ms of the host's capture (p50 73 ms) — the
three late ones in the first seconds after the jump to the fight, before the joiner had those copies (3.8–6.2 s). The twin
(`draw_event` off): the copies drew 16 times against 45, each only once a fight's rows or an engagement had reached the copy, never at
the host NPC's own draw.

## F — the joiner's figure holds his weapon (`figure_hands`)

The field: at 19:11:52 the host saw the joiner's figure without the weapon the joiner held. The figure's weapon came only from the
outfit packets and the figure's own draw of its weapon set, and nothing ever compared the two hands. At 19:11:09 the joiner closed an
inventory screen; his next outfit packet listed one class of thirteen (his sword), the host's converge took pieces off his figure (the
game refused 10 of the 11), and the figure's next draw, four seconds later, showed no weapon. How exactly the figure lost it is not in
the logs; the bench makes that end state instead (below). Now each player's own main-hand item class goes to the partner at every
draw, sheathe and change (and every 10 s while drawn) on the WO-203 side channel (`W203FigureHand`, "v1|drawn|class"; no protocol
bump). The partner's figure is made to hold that class: at his draw, before the figure's own draw, the weapon he last held is put back
on the figure if it went missing; if the figure's hand is still wrong half a second after his message, the game's own equip into its
weapon set and a draw, `DrawFromInventory` only last. The converge never takes off the class its partner holds drawn. Both ends log it (`WO214-HAND own ...`,
`WO214-HAND figure ...`), and the briefing scores it ("the partner's figure held his weapon N of M").

On the bench a figure whose outfit is whole already draws the right weapon on today's path (10 of 10 reads). So the test makes the
field's end state: the host's agent takes the weapon off the figure just before each of the joiner's draws
(`KCDMP_TEST_W214_FIGURE_STRIP=1`, a bench-only fault). Five draws and five sheathes, the host's figure read against the joiner's own
hand each time:

| | the host's figure matched the joiner | his draws the figure showed |
|---|---|---|
| twin (`figure_hands` off) | 5 of 10 | **0 of 5** (no weapon in hand) |
| F, the hand fix only (the first build) | 10 of 10 | 5 of 5, fixed 1.8 s after his message (p50) |
| F, with the weapon put back at his draw | **10 of 10** | 5 of 5, every one the figure's own draw: 359 ms after his message (p50, max 370) |

In a fight (bandits on the host, the joiner beside him and counted in combat by his own game): with a whole outfit and F on, 10 of
10; with the weapon taken off before each draw, the twin's figure showed none of his 5 draws. The bench's Henry wears
nothing but his sword, and bandits that took his figure downed him in one burst in two runs before his first draw.

## G — for the next field session

`docs/TESTER-SCRIPT-0.64.1.md`: 20 minutes over the internet — a village walk following villagers at 10–30 m and 70 m away and back, a
tavern, a fight to the death by each player, a draw and sheathe test (five out of a fight, five in one, and one after an inventory
screen), the marks and both bug reports. The briefing's band table is in its Link section, with the plain-words verdict line.

## The field's marks

| mark (the joiner's clock) | part | now |
|---|---|---|
| 19:06:05 "odd" (an NPC 3.7 m in one frame; a 3.2 s gap) | A, B | improved: the jump was the joiner's own engine moving a copy; a gap now holds the body (B) |
| 19:07:32 "npc movement stuttering" | A | improved: walkers were already sent at their band (A.0); turns in place now are (A.1); copies no longer rebound every ~3 s (B) |
| 19:09:07 "the host slow to update position" | A.4 | fixed: a turn in place is a change |
| 19:09:56 "npc weapon not drawn in fight" | E | fixed: the copies drew at the host NPC's draw, 26 of 27 (the twin: only once a fight reached them) |
| 19:11:52 "the joiner's figure has no weapon for the host" | F | fixed: the figure holds his weapon (the bench, with the field's end state: 10 of 10; twin 0 of 5 draws) |
| 19:12:09 / 19:12:14 "for me the NPC died, for the joiner it did not" | D | fixed: an owned copy takes the host NPC's hit and death rows (the twin refused 4; 0.64.1 none) |
| 19:12:15 "dead or unconscious npc standing for the joiner" | D | fixed: the host's kills lay their copies down, 4 of 4 (2 of 2 stood before the last fix) |
| 19:12:43 "NPC movements not great, not smooth" | A, B | improved: binds 18.7 a body in the field, 1.8 on the bench (twin 6.9), no resets, no silence drops |
| "huge T-posing" (the report) | C | improved: stale postures end (C.2), the idle flood's own half is gone (C.3); the let-go's base (C.1) never had to act on the bench, with or without it |

## Found on the way

- **The agent never heard the mod's WO-214 events.** Its event router passed only `w213_*` names on, so F's hands and every in-game
  `mp_*` switch of this work order went nowhere (the bench's twins, set in the environment, were not affected). Each now has its own
  route. F's first bench runs found it.
- **The bench's Henrys.** The joiner's usual Henry (an early save of another playthrough) wears nothing at all; a sword created into his
  inventory is gone again before his draw. F ran with an armed Henry of another playthrough instead, pinned for a first join
  (`KCDMP_RIG_JOIN_SAVE` and `KCDMP_RIG_JOIN_DATA`, bench-only); he too wears nothing but his sword. Once locked on in a fight the game
  refuses a scripted draw, and fists do no harm to a bandit: the kill scenarios now draw first and never move the fighter mid-fight.
- **The walk transition.** The first C.2 stopped the engine's own walk transitions (above); found by the probe's sliding and the
  copy's "Agent is stuck".
- **Not this work order's, seen on the bench:** a joiner crash once, in a fight with copies attacking his player (the combat module
  calling freed memory, the family WO-207 traced to a copy's planned defence window); a host idling beside a joiner that never joined
  grows to out-of-memory (known since 0.63.7); the look-pose and horse overflows (C.3); the probe's "sliding" count runs 1–78 a run
  with every switch on or off.

## Open

- C.1 on a real session: the bench never left a copy empty after a let-go, with or without the fix.
- The receiving side's big blends (A.3): 33 in a village run.
- The figure's weapon lost after an inventory screen: the path itself (F holds the weapon whatever it is).
- Five draws in a fight with the weapon taken off (the bench's Henry was downed before them); the tester script asks for them.

## Gates and the installer

- **The offline gates** (every one of WO-206's list and this work order's suites): native 10,286 tests, the relay's 229, the agent's
  2,064, setup 77, the log bundle 83, every Lua suite, the static checks, the relay table and fault-mode gates, the digest analyzer 62,
  the rig 42, the save tools. The fault-mode gate's one timing test failed once while the bench ran beside it; it passed in the same
  run's relay round trip and again alone (37 of 37). The no-input-capture gate, first and last: 60 binaries and 1,440 scripts checked,
  PASS — nothing checked can read what a person types or clicks.
- **The installer**, from a clean clone of the work order's last commit with the safety gates (the frame-rate soak waived, as the work
  order says): `KingdomComeTogether-Setup-0.64.1-f1054e6-2611060351.exe`, 107,809,658 bytes, build id `f1054e6-2611060351`. In its
  build: the publishing gate PASS, the live save round trip PASS (drawn before, the save healthy, no stuck fight, drawn after the save
  and reload), the payload's install and uninstall tests 41 of 41, the no-input-tooling, no-screenshot and no-input-capture gates PASS.
- **The real saves**: their hash list unchanged before and after every bench run and the installer's (1,136 files). **The join**
  passed in every bench run.
