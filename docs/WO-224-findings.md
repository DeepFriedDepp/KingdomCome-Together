# WO-224 — 0.65.5: grabs and combos in step, an official build you can tell apart, and the release

Roles only: "the host", "the joiner", "a tester", "a contributor", "the maintainer". Footing marks: [code] read in this repository,
[binary] read in the game's own code, [bench] measured on the bench (two copies of the game on the maintainer's PC, joined through
our relay with the field's link shape — 80 ms ± 20 round trip, 0.5 % loss, two stalls of 0.3–0.9 s a minute), [reasoned] an
inference, said as one. "Shipped" is the build with every switch on; "the twin" is the same build with the named switch off on
both machines. Run names are the bench's own; their logs, frames and ledgers are in the WO's notes.

**The field bundles.** The testers' bundles of the session this work order answers were not on the bench PC when it ran (looked
for at the start and again before the final runs). Everything below is measured on the bench.

## 0. The schedule as it went

Session start 2026-11-25 23:58 (hour 0).

| part | planned (hours) | started | ended | result |
|---|---|---|---|---|
| backup, control, reading | 0–2 | 25th 23:58 | 26th 00:40 | saves backed up and hashed; first control run made (not used as a judge: see Conduct) |
| A — clinches | 2–16 | 26th 00:10 | 26th 08:20 | fifteen builds; the bar met for "plays on both screens" (21 of 21 and 26 of 26), missed by one stretch each way for "nothing over 1 s on one screen only"; committed |

## 1. Conduct

- **A compile during a bench run (00:19).** A type-check build of the agent into a temporary folder ran for 50 s while the first
  control scoreboard run was in its village scene. No build folder of the run was touched, but the rule is "no compile while a run
  uses the bench". That control run is not used as a judge anywhere below; every later build waited for the bench's lock.
- **A diagnostic run from a build folder during a bench run (06:18).** The agent's own table dump (`--dump-swing-catalog`, one
  second, reads the game's tables) was started from the build folder of the run then on the bench. It wrote a settings file and a
  log into that build folder — not into the copies the run used — and both were removed once the bench was free. No compile, no
  rebuild; said here because it touched a build folder mid-run.

## 2. Part A — clinches (`mp_w224_clinch`)

**The contract:** a grab between a player and an enemy plays as one move on both screens — both bodies in step, the same
outcome — and ends cleanly for both.

### 2.1 What was wrong

1. **Every clinch event that named its body was dropped at the receiver, since 0.62.0.** A player's clinch goes out as one
   event with the body he clinched. That name rode behind a `|` inside the event's name field; the reader of that field refuses
   any character outside letters, digits and `_`, so the whole event was refused, silently. A move with both rows and a name was
   longer than the field and threw at the sender. On the current `main`, in a three-minute camp fight: the host sent 36 clinch
   events, the joiner received 0. [bench] [code]
2. **The mod asked the engine for a clinch with the wrong number, and refused its own call.** The engine's start takes an
   *action* type (35 = a new clinch, 20 = the clinch re-applied); the mod passed a *guard* type (4 or 24) — the engine's answer is
   "data not found for sync movement" — and the call ran into the mod's own gate at the same entry. [binary] [code]
3. **The mod's gate refused every clinch test and start between the joiner's player and a copy.** The field's "39,393 of
   50,946 refused" was this gate, not the engine's test (2.2: the engine's own test passes for the joiner as often as for the
   host). [code] [bench]
4. **A copy whose host NPC changed its weapon in the fight read unarmed.** In combat mode the engine does not look at a body's
   hands: its weapon class is a property of the combat model, written by one function of the combat actor's weapon component
   when the engine's own code changes a weapon. The mod's script draws a copy's weapon; that function was never called. And a
   weapon the copy does not own is created by the script first: the script's own reading says "held" within a second, but the
   engine's combat finds no item object in that hand for about ten seconds more. In both cases the copy read "unarmed" (weapon
   class 12): a 0.8 m reach, and no clinch row with an armed body, so every grab of the host with that bandit was refused on
   the joiner's screen — 36 and 39 refusals in a row in two runs, three grabs of twenty in another. Older than this work order
   (a copy that stood up from a meal fought with a 0.8 m reach for the whole fight); here it is what hid the host's grabs.
   [binary] [bench]

### 2.2 A.0 — the engine's clinch test, measured

The mod's two gates became detours that see every call and its answer (`mp_w224_status`; `W224-SSG` lines in the DLL's log).
The engine's test (both bodies in combat mode, mutual opponents, neither in a sync guard type already, both in a clinchable
action — guard, dodge, hit — and facing each other), **the player asking**: [bench]

| scene (first instrumented build) | the host's player and his enemy | the joiner's player and the copy |
|---|---:|---:|
| the swap (one bandit, eight turns) | 53.3 % of 19,474 | 51.2 % of 21,128 |
| a two-player camp fight, 3 min | 55.6 % | 65.3 % |

Between runs of one machine the rate moves by as much as between the two machines (44.5 to 56.4 % on the host, 51.2 to 67.8 %
on the joiner over four runs each). So the test itself was never the joiner's problem: it passes at the host's rate.

When it fails, the condition that failed first (sampled twice a second in the engine's own order; the sampler agrees with the
engine's own answer in 94 to 99.9 % of samples), in the swap: on the joiner the copy's own action (an attack or a block running:
585 samples), the two not being each other's opponent (497: the bandit was on the host in alternate turns), no opponent (76),
the player's own action (49), not facing (41); on the host the enemy's action (992), not mutual (273), no opponent (269), the
player's action (207), already in a sync guard (121). Distance and facing are not what refuses: they come after.

Two more things stand between a passed test and a clinch, both the engine's own and the same on the host: a clinch row must
exist for the two bodies' weapon classes (cause 4), and the asker's cached distance to its combat target must be inside the
sync-guard distance, about 1.1 m (it refuses when too close as well: 0.61 m refused, 1.06 to 1.19 m started). In three minutes
of a free camp fight neither player's engine asked for a clinch start once; a player who walks into his enemy gets one every 10
to 20 s. The drill (`w224grab`) walks in. [bench] [binary]

### 2.3 What ships

- **The wire.** The clinch event names its body by a 32-bit hash of the body's name, inside the name field. A test proves the
  old form is refused by the reader and the new one is read. The host has no table of copies: it finds the body by that hash
  among the NPCs within 8 m of the joiner's figure.
- **A.1, the joiner's own clinch.** The joiner's own test is the engine's again; a copy's own test stays refused (a copy has no
  brain to ask with, and the host's world decides who grabs whom). When his engine starts a clinch with a copy, the host is told
  and starts the same clinch between his figure and its NPC the engine's way (action type 35, up to six tries 100 ms apart).
  When the host's engine will not start it, the joiner is told and his game ends its own — never one body in a clinch on one
  screen only. A clinch the host's NPC starts on his figure is told as it starts (the start itself is the event, so a clinch
  that ends and starts again inside one frame is not missed), and his game starts it between his player and the copy; each
  move of the host's NPC goes through the copy's own attack request, which in a clinch builds the paired move and starts his
  half itself.
- **A.2, the host's clinch.** The same event and the same start between the host's figure and the copy on the joiner's
  screen; the figure plays its half of each move from the host's own row. A clinch that begins inside an enemy's grabbing
  blow (the engine starts it while the player is still in the hit) is told from that frame on; before, its first word was its
  first move, 1.2 s later. A clinch held with no move is said again every 3 s.
- **A.3, the end.** A clinch ends on both screens when it ends on either: a body still *held* in the sync guard gets a fresh
  guard (the engine's own two calls: end the main action, ensure a guard), and the engine's re-application of the sync guard
  for a body marked as ending is refused, so its guard falls back to the plain one — the engine's own path when a sync guard
  cannot be re-established. A body in the clinch's last move is left to finish it (cutting it made the NPC grab again in the
  same frame); a figure or a copy still in the clinch 0.7 s after that gets its fresh guard then. For one second after the mod
  ended his clinch the joiner's own test is refused, so his engine does not re-grab what the host's world just let go.
- **The copy's weapon.** A copy's combat weapon class is compared with its hands before the mod's own clinch start and after
  every weapon draw, and refreshed by the engine's own function when they differ. The agent tells the DLL the host NPC's weapon
  class (from the game's own weapon tables); while the copy's hand holds no item object yet, its class is set to that through
  the property's own setter, and the engine's refresh takes over when the item arrives. A weapon changed in a fight is drawn
  over the old one, which stays in the hand until the new one is there.

### 2.4 Against the bar

The drill, each direction in its own session, the field's link shape, the final Part A build. "Shown" = the same clinch on the
other screen within 1.5 s of its start. "A one-screen stretch" = a time when one screen has the player in a clinch and the
other does not (both senses), measured on the two logs' clocks. [bench]

| | the host's grabs (he walks in, 21 clinches) | the joiner's grabs (26 clinches) | the same, the joiner not swinging (22 clinches) | the bar |
|---|---|---|---|---|
| shown on the other screen | **21 of 21**, median 0.13 s after it began, nine in ten within 0.20 s | **26 of 26**, median 0.00 s, nine in ten within 0.19 s | **22 of 22**, nine in ten within 0.14 s | 18 of 20 |
| time in a clinch on both screens at once | 59.5 s of his own 64.6 s | 44.8 s of 50.0 s | 37.6 s of 41.8 s | – |
| one-screen stretches over 1 s | **1**: 2.3 s — on the joiner's screen the pair fell apart 0.55 s into a 2.9 s clinch | **1**: 1.5 s — the host's world held his figure after his own screen had left (a re-grab race, below) | **0** (the longest 0.31 s) | none |
| the other screen left after it ended on his | median 0.23 s, longest 0.28 s | median 0.00 s | median 0.00 s | within 1 s |
| the host's world's clinches of the joiner's figure with himself in it | – | 25 of 25 | 21 of 21 | – |

- **"Plays on both screens": met**, each way, in every run of the last five builds but one (17 of 20 on the build before the
  copy's weapon class was set from the host's word: three grabs in a row inside the ten seconds after a weapon change).
- **"None left stuck over 1 s": missed by one stretch each way** in the two main runs, met in the third. Neither is a body left
  in a clinch pose: in the host's run the clinch ended *early* on the joiner's screen (two puppets drifted out of the sync
  guard's distance), in the joiner's run a "leave" that was on its way ended the clinch the host's NPC had started 70 ms
  before it arrived. Both have a fix that is built after this part's commit and measured in the final runs (section on the
  finals): the "in a clinch" word repeated every second, and a leave that does not end a clinch younger than 0.4 s.
- **The clinch test at the host's rate within 10 points: met where like is compared with like.** In free fights the two rates
  are equal (2.2). In the drills the raw rates are 42.0 % for the host's player and 26.6 % for the joiner's (both not
  swinging): the test fails for a body that is already in a clinch, and the joiner's drill has him in one for a third of its
  samples against a seventh for the host. Outside a clinch: 46.0 % against 39.2 %. One more thing is the mod's own: for one
  second after the mod ended his clinch the joiner's test is refused (2.3) — 16 % of his asks in that run.
- **The copy's weapon.** In the host's run the copy's weapon class was set from the host's word 16 times; no grab was lost to
  it. In the runs before that fix: 5 of 12, 6 of 17, 4 of 6, 2 of 3 and 17 of 20 shown, every miss with the copy reading
  class 12.

Earlier builds of this part, the same drill: the first that worked both ways showed 11 of 13 and 10 of 10; the misses that
followed were found one cause at a time (2.1's fourth cause; an entry the poll did not know; a quiet clinch ended by the
mod's own net after 6 s). A drill player who swings beats a lone bandit out of the fight in two to four minutes (its attack
rows stop for minutes): the host's runs are without swings.

### 2.5 The twin (`mp_w224_clinch off`, the same build)

The same build, `KCDMP_TEST_W224_OFF=w224_clinch` on both machines (the clinch log stays on: it is the instrument). [bench]

| | shipped | the twin |
|---|---|---|
| the host's clinches shown on the joiner's screen | 21 of 21 | **0 of 12** (32 clinch events sent, 0 read: the old name form) |
| time in a clinch on both screens, the host's run | 59.5 s of 64.6 s | 0.0 s of 33.7 s |
| the host's world's clinches of the joiner's figure with himself in it | 25 of 25 | **0 of 16** |
| the joiner's own test with a copy | the engine's (26.6 to 35.9 % yes) | refused by the mod, 7,995 of 7,995 |
| the joiner's screen and the host's agree while either has him in a clinch | 81.9 % | 0.5 % (his own screen had him in clinches for 194 s that the host's world did not have; the longest 47 s) |

The twin is the current `main`'s behaviour: the host's NPC grabs his figure and he stands free on his own screen, and a clinch
his own game starts with a body is never ended by anybody.

### 2.6 Frames

Taken by the drill at its first clinches, the host's window and the joiner's at the same moment (the bench's own windows
only; the files are in the WO's notes, `frames/A-host-grab.jpg` and `frames/A-joiner-grab-16.jpg`, each the two views side
by side). Looked at:

- *The host is grabbed.* The host's view: his own hands and the bandit's club crossed in the clinch. The joiner's view, from
  three metres: the host's figure and the bandit's copy locked together, clubs crossed, in the same pose.
- *The joiner is grabbed.* The joiner's view: face to face with the copy, weapons crossed. The host's view, from the hill:
  the joiner's figure and the bandit locked together (small in the frame: the host stood 7 m off).

### 2.7 Not done, and known

- **The enemy's clinch-exit strike is not paired on the joiner's screen.** The bandit's blow out of a clinch is a sync attack
  on the host (both bodies in the paired animation, counted "in the clinch" for its 1.4 s); on the joiner the copy's own
  attack request ends the clinch at once and strikes as a plain attack, which his own game judges. The copy's request passes
  the engine's default input class, not the host NPC's. One stretch of 1.4 s in one run was this. Not fixed.
- **Two engines grabbing at once.** When both games start a clinch with different leaders inside the same half second, the
  joiner's is ended and started again the host's way (five to eight times a run in the joiner's drill); the picture is a
  short break in the hold. Not counted by the bar; seen in the traces.
- **A weapon the copy did not own is not in its hand for up to ten seconds** after its host NPC changes to it (the game loads
  the item); the copy's old weapon is kept in the hand until then and its combat class is the host NPC's, so it fights and
  clinches as armed, but the picture is the old weapon.
- **The committed code and the measured build.** The numbers above are build a15's. The tree of this part's commit is that
  build's code.
