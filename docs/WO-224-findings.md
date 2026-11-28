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
| B — combos | 16–28 | 26th 08:30 | 26th 14:10 (the confirmation round ran on to the afternoon) | six builds; the defence scene 0 guesses in 45 blows and its own verdict passes, the swap scene 1 in 80; the copy plays the host NPC's own row in 78 to 93 % of swings (22 % before); committed |
| C — the official-build stamp | 28–31 | 26th 14:10 (prepared beside A and B while the bench ran) | 26th 14:35 | the stamp, the check, the four places that say it, `AGENTS.md`; proof on a release-shaped payload: unsigned unofficial, signed official, one file swapped unofficial; committed |
| E — a contributor's pull request | 31–34 | – | – | skipped: no rebased pull request with only the compatible fixes existed at the session's start |
| D — the crash after deaths | 34–38 | 26th 14:28 | 26th 23:55 (its four hours ran to six and a half of work; the maintainer paused the session from 20:43 to 23:45) | the suspect refuted (it is a protection); every fault follows the mod's own step 2.5 s after a death; five builds; what ships does nothing there: 2 runs, 80 deaths, no fault, against 2 closes in 5 runs — not proven; committed |
| F — re-checks and finals | 38–44 | 26th 23:55 (on the final build, the moment D was committed) | 27th 18:45 | A re-measured 19 of 20, 23 of 23, 25 of 25; B's chain on every scene (the same row 76 to 86 %, the twin 0 to 10 %), the defence scene's guesses 9 of 132 (bar missed); WO-222's bars 41 of 47 and 84 of 84; WO-223's table, camp 5 of 5, guard pass; the massacre 0 closes in 5 runs; **the scoreboard: place lower than today's `main` in 3 pairs of 3 (a regression, cause not found)**; no crash, no closed game |
| G — the build, the documents, the push | 44–48 | 27th 18:45 | – | the release commit; the installer and the push follow (8) |

## 1. Conduct

- **A compile during a bench run (00:19).** A type-check build of the agent into a temporary folder ran for 50 s while the first
  control scoreboard run was in its village scene. No build folder of the run was touched, but the rule is "no compile while a run
  uses the bench". That control run is not used as a judge anywhere below; every later build waited for the bench's lock.
- **A diagnostic run from a build folder during a bench run (06:18).** The agent's own table dump (`--dump-swing-catalog`, one
  second, reads the game's tables) was started from the build folder of the run then on the bench. It wrote a settings file and a
  log into that build folder — not into the copies the run used — and both were removed once the bench was free. No compile, no
  rebuild; said here because it touched a build folder mid-run.
- **The maintainer paused the session (26th 20:43 to 23:45).** A bench round of Part D was running; its next three sessions did not
  come up or lost both games during a join (no crash report), and the round ended on its stop file. Those three are not counted
  anywhere. Nothing was built, run or committed during the pause.
- **Part D ran over its box.** Four hours were planned; it took about six and a half of work, taken from the hours Parts A to C
  had left over (they ended fourteen hours ahead of the plan), not from hours 38 to 48.
- **Disk.** The bench's run folders (logs and the animation probes' files of every run) stood at 32 GB on the system drive when
  the finals began. They are scratch, outside the repository; the final reply says where they are.

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

## 3. Part B — combos as combos (`mp_w224_chains`)

**The contract:** an enemy's chain of swings plays on its copy as the same chain at the host's timing, so the joiner's own game
judges every blow of it.

### 3.1 B.0 — what the measure found

The work order expected the cause in the queue ("one swing at a time queued behind the last"). Three instruments were built to
look: the host's watch on the engine's combo input (when its NPC asks for the next swing of a chain), a line for the row each
copy's own attack plays on the joiner (`W224-COPYROW`), and a snapshot of the combat model at each chain input (bench only).
What they showed, on the build before any change of this part (the twin's numbers): [bench]

1. **The copy swings from the other side.** Paired swing by swing with the host NPC's own rows, the copy's attack is of the same
   kind in 80 % — and starts from the same guard in **22 %**, strikes into the same zone in **23 %**, is the same row in
   **6 %**. Three swings in four are the mirror image of the host's: the joiner blocks a blow from the left that the host saw
   come from the right. The cause is one argument. The copy's attack is the engine's own AI attack, which first asks a *guard
   change* to the zone it is given and then attacks; the mod has given it the row's *attack* zone since 0.63.8. Read on the
   host at 109 attacks, the model's guard zone before a `leftGuard` row is 2 and before a `rightGuard` row is 1 — the opposite
   of the zone the swing ends in. So the copy was told to move its guard to the far side, and swung from there. [binary] [bench]
2. **A chain is asked for early, and the row says it late.** The host's NPC makes its combo input while its running swing's
   chain slot is open; the follow-up's own row — all the copy was told — is sent when the follow-up starts: 0.34 s later in the
   defence scene, 0.47 s in the swap (medians). By then the copy asks for a fresh attack, which the engine refuses until the
   running swing's recovery ends: the copy is never in a chain, and each chained swing of the host's puts it further behind.
3. **The engine opens a swing's chain slot on contact, and a dodged blow makes none.** The slot is a timed window the engine's
   combo trigger opens when the swing's contact event comes (a hit or a block) and closes again [binary]. On the host the blow
   lands on the host's figure of the joiner, the slot opens, the NPC chains. On the joiner's screen he may have stepped out of
   that same blow: no contact, no slot, and his copy cannot chain however early it is told. It waits out its swing and starts
   the follow-up fresh, a swing late — past the host's wait for the joiner's verdict. **These are the host's guesses**: traced
   one by one on the builds before the one that ships, the leftover guesses followed a slot that never opened on the copy, or a
   combo finisher the copy's own combo did not reach.
4. Not the cause: the queue. A row that finds the copy mid-swing is not queued behind it.

Four builds were measured before the one that ships (the numbers are in 3.3): the chain input made with the zone of the host's
*running* swing (the copy chained from the wrong guard and fell further behind); the corrected guard and the host NPC's own
requested zone (the copy now *looks* right — the same guard side in 97 to 100 % of swings — with the guesses unchanged); and a
variant that asked the follow-up's attack at once when the copy's slot had not opened (the engine refused it while the swing
ran; more guesses, 6 in 128).

### 3.2 What ships

- **The guard a row starts from.** Before a copy's attack is asked for a host row, its guard is the row's own (`leftGuard` /
  `rightGuard`), set at once through the engine's setter the stream already uses; the model's requested attack zone is the
  row's; and the engine's AI attack is given that guard, not the attack zone.
- **The chain input.** The host's DLL sees its NPCs' combo inputs (a detour on the engine's combo input that reads and records,
  on the engine's own thread, and runs the engine's function untouched) and tells the joiner each one made at his figure with
  its slot open, with the NPC's requested attack zone and input class — as it is made, before the follow-up's row. The copy
  sets the same requested zone and makes the engine's own combo input in the frame its own slot is open. The follow-up is then
  the engine's: the same row at the swing's own chain point, and at the end of a combo the finisher, played by the copy's own
  combo component on the local player. The follow-up's row, when it comes, finds its chain and asks nothing more.
- **The slot the joiner's dodge kept shut.** When the follow-up's row is here (the host's NPC did chain), the copy runs a plain
  attack, its slot is closed, and its swing is as old as the host NPC's was when its follow-up started (this row's arrival less
  the row before it; never later than 0.5 s after the row), the mod opens the copy's slot itself — through the engine's own
  setter of that property, the one its combo trigger calls — and makes the input. The copy chains as the host's NPC did. The
  mod closes a slot it opened when the follow-up starts, or when none came. What the joiner sees is what the host's NPC did: a
  bandit whose blow he dodged goes on with its chain, because on the host it did.
- **When no chain comes of it.** An input the engine does not follow (a hit in between, a finisher the copy's combo did not
  reach) asks the row's attack fresh as soon as that is known.
- Nothing is refused or changed on the host; with the switch off the watch is not installed. No NPC brain runs on the joiner:
  every one of these is the host NPC's own input, replayed.

### 3.3 Against the bar

"A blow" is an NPC's blow that landed on the joiner's figure on the host. "Judged by his own game" = the joiner's engine judged
the copy's blow in time and the host sent no verdict of its own; "a guess" = the host's fallback verdict. The row measures pair
each attack row the host's NPC played with the row its copy played on the joiner's screen. The field's link shape; the armed
joiner; each scene its own session. The first three columns are the build that ships. [bench]

| | the defence scene | the swap scene (the enemy's target changes, 8 cycles) | the bar |
|---|---|---|---|
| NPC blows on the joiner | 45 | 80 | – |
| judged by his own game | **45** | 79 | – |
| the host's guesses | **0 (0.0 %)** | **1 (1.2 %)** — the first blow of a copy not yet held in his fight when its row came (WO-223's ground, not a chain) | ≤ 1 % |
| left uncorrected | 0 | 0 | none |
| the copy played the same row as the host's NPC | 93 % (43 of 46) | 78 % (76 of 98) | – |
| ... the same guard side / the same attack zone | 100 % / 98 % | 100 % / 94 % | – |
| the copy's row starts after the host's row | median 86 ms, nine in ten within 181 ms | median 96 ms, nine in ten within 300 ms | B.1: within the host's own swing-to-swing timing (the host's median gap from input to follow-up: 411 and 378 ms) |
| his verdict reached the host after | median 172 ms, nine in ten within 484 ms | median 204 ms, nine in ten within 422 ms | – |
| chain inputs the copy made | 30 (26 by its own open slot, **4 by the slot the mod opened**) | 44 (34, **10**) | – |
| ... followed by the engine's chain | 30 of 30 | 44 of 44 (3 rows asked fresh: the input's swing had ended) | – |
| combo finishers played as the host's finisher | 2 of 3 | 3 of 5 | – |

The defence scene's own verdict (the rig's: at least 95 % of the blows judged by the joiner's game) **passes** on this build; it
fails on the twin's runs with ten guesses and failed on the build before this one.

The builds before it, the same scenes (the host's guesses of the blows): the first chain input 1 of 51 and 4 of 94 with the
verdict waits doubled; the corrected guard 3 of 54 and 1 of 186; the "ask at once" variant 3 of 17 and 6 of 128. The camp scene
on those builds: 3 of 112, 7 of 165 (4.2 %) and 2 of 46 (traced on the last two: each guess after a slot that did not open or a
finisher the copy did not reach).

### 3.3a On the final build, beside its twin

The finals ran each scene on the final build (Part D's tree, every switch on) and then on the same build with
`KCDMP_TEST_W224_OFF=w224_chains` on both machines (the twin; 3.4). Rows paired by time; each scene its own session. [bench]

| scene | the final build: blows, the host's guesses | ... the same row / guard side / attack zone | the twin: blows, guesses | ... the same row / guard side / attack zone |
|---|---|---|---|---|
| the defence scene | 51, **7 (13.7 %)** | 82 % / 100 % / 91 % | 9, 0 | 0 % / 7 % / 0 % (14 pairs) |
| the whole-scenes run | 226, 12 (5.3 %) | 80 % / 98 % / 92 % | 369, 19 (5.1 %) | 10 % / 23 % / 22 % |
| the camp scene (180 s, chains framed) | 133, **1 (0.8 %)** | 86 % / 99 % / 92 % | 118, 3 (2.5 %) | 10 % / 32 % / 27 % |
| the swap scene | 144, **3 (2.1 %)** | 76 % / 98 % / 86 % | 73, 3 (4.1 %) | 10 % / 29 % / 20 % |

- **The chain: met, and the twin fails it in every scene.** On the final build the copy plays the host's row in 76 to 86 % of
  swings and from the same guard side in 98 to 100 %; on the twin in 0 to 10 % and 7 to 32 %. Shifting the twin's rows by one
  swing pairs them better (the guard side 56 to 70 %): the twin's copy runs one swing behind, which is what this part fixed.
- **The guesses: met in the camp and swap scenes, missed in the defence scene.** Six of the defence scene's seven guesses are one
  copy: a third bandit that joined the fight, whose host NPC changed from a club to its fists while its rows played; the engine
  refused its attacks for 3 s and the guard it was given did not take. That is not the chain mechanism (its rows in that run
  were the same row in 82 %), but it counts against the bar. The defence scene's two top-up runs: 1 guess in 32 blows and 1 in
  49 (3.1 %, 2.0 %; the same row 96 % and 90 %, the guard side 100 %), each passing the scene's own verdict (96.9 %, 97.9 %). Over
  the three runs, **9 guesses in 132 blows (6.8 %), 3 of 132 (2.3 %) without the one copy**: the ≤ 1 % bar is **missed** on
  the final build; the B round's own defence run read 0 of 45 (3.3). The whole-scenes run
  reads 5.3 % shipped and 5.1 % twin: its guesses come in bursts from single copies in the chase scenes (four within 4 s from
  one), two after a combo finisher and two refused.
- The guess rate is stated beside the twin, not taken as B's test: the twin's defence scene here had one bandit and nine blows.
- The force's own twin (`KCDMP_TEST_W224_FORCE=0`: the slot the mod opens left shut): the defence
  scene 1 guess in 41 blows (its own verdict passes, 97.4 %), the swap scene 1 in 146; the same row 81 % and 77 %, the guard side
  100 % and 99 %. **This step's own effect is not shown by the finals**: with it off the build read as well as with it on. It
  is part of `mp_w224_chains` (no switch of its own); it stays because the dodge case it answers (3.2) was measured on the build
  that added it (3.3: 4 and 10 chain inputs through the opened slot, each followed by the engine's chain), not because the finals
  separate it.
- Every run: no fault, no stale read, no game closed; the real saves' hashes unchanged.

**Frames.** The camp scene's three frame pairs (taken mid-chain in the 180 s run) are each player's own first-person view in a
crowded fight, the host's under the hurt vignette: they show both screens in the same fight, not the same swing from comparable
angles, and are not offered as evidence of the row. The clean scene's pair (3.5) stays the like-for-like one.

### 3.4 The twin (`mp_w224_chains off`, the same build)

`KCDMP_TEST_W224_OFF=w224_chains` on both machines: no watch on the host, no chain input, the attack given the row's attack zone
as before. Measured on the two builds before the shipped one (the twin's code path is the same in all three); this build's own
twin runs are in 3.3a. [bench]

| | shipped | the twin |
|---|---|---|
| the defence scene: the host's guesses | **0 of 45** | **10 of 56 (17.9 %)** and 10 of 60, 1 of 48 |
| the swap scene: the host's guesses | 1 of 80 | 2 of 68 (2.9 %), 5 of 153, 4 of 118 |
| the copy played the same row | 93 % / 78 % | 22 % / 22 % |
| the same guard side | 100 % / 100 % | 39 % / 34 % |
| the same attack zone | 98 % / 94 % | 43 % / 36 % |
| combo finishers played as the host's finisher | 5 of 8 | 0 of 17 |
| a host row with no row of the copy's (the defence scene) | 0 of 46 | 8 of 72 |

The twin is the current `main`: three swings in four from the mirrored side, no finisher ever, and in a fight where the joiner
dodges, one blow in six judged by the host's guess.

### 3.5 Frames

The clean scene (one bandit on a standing joiner, the host beside them), the host's window and the joiner's at the same moment
of a chain's second swing; the file is in the WO's notes (`frames/B-chain-lone-b4-3-zoom.jpg`, the build with the corrected
guard). Looked at: the bandit's axe is raised over the same shoulder on both screens, the joiner's figure in the same guard.
The camp scene's frames, taken on the final build, are described in 3.3a.

### 3.6 Not done, and known

- **A finisher the copy's own combo does not reach.** The copy plays the host's finisher when its own combo component has
  counted the same chain (5 of 8 in the two scenes). When it has not (the chain began before the copy was in his fight, a
  step was a fresh attack), the copy strikes a plain attack in the finisher's place: one blow judged where the host's
  finisher has two. Forcing the finisher would mean building the engine's own combo query by hand; not attempted.
- **The chain goes on where the game's own would stop.** A bandit whose blow the joiner dodged, or blocked without a recoil on
  the host, chains on his screen as it did on the host. In the game alone that chain would have ended at the dodge. This is
  the price of one authority for the NPC; the alternative was the 0.5 to 1 s of drift the guesses came from.
- **The first blow of a copy that changes over to the joiner** can still come before the copy is held in his fight (the one
  guess of the swap scene): WO-223's mechanism, unchanged here.
- **A blow inside a grab** is judged by the host at once, as since WO-223 (the copy's request ends the clinch on the joiner's
  screen; 2.7). A build that waited for the joiner's verdict there made 10 guesses of 60 and was taken back.
- **Retail.** The chain's places are found by their bytes in the game's combat module at start (the combo input and slot off
  the engine's AI attack, the property's setter off the combo trigger's own "enter"), each checked before use; where one is
  not found the mechanism says so in the log and that step stays off. Found on the bench's build; the retail game's build is
  not checked in this work order.

## 4. Part C — an official build you can tell apart (`mp_w224_official`)

**The contract:** every build says whether it is the project's own. No network call, ever: the check is offline.

### 4.1 What ships

- **The stamp.** The release path (`tools/Build-Installer.ps1`, after every binary is final and before the install manifest)
  writes `official-build.txt` beside the binaries — the version, the build id and the SHA-256 of each of the build's own
  binaries: the DLL, the agent, the relay, the launcher, the setup helper and the project's shared assemblies (13
  files in the proof payload) — and signs it (ECDSA P-256) into `official-build.sig`. The signer is a small tool in the
  repository (`dotnet/KcdMp.Official.Tool`, never shipped).
- **The key.** The private key is a file on the maintainer's machine, outside every repository; its path is in the environment
  variable `KCT_OFFICIAL_KEY`. This work order generated the pair (none existed) and the final reply says where the file is.
  The public key is in the repository (`OfficialBuild.PublicKeyPem`). The signer refuses a key file that lies inside a git work
  tree and never overwrites one.
- **A build made without the key is unofficial**, and the release path says so and goes on (exit code 3 from the signer: nothing
  is written, an older signature in a reused folder is removed). A fork's build and the maintainer's own development builds
  are exactly that.
- **The check** (`OfficialBuild.Check`, in the assembly the launcher, the agent and the bug report share) reads three things
  beside its own binaries: the manifest, the signature, the files the manifest names. Official only if the signature is the
  project's, the manifest is for this binary's own version and build id, every named file is on disk with the signed hash, and
  the four main binaries are named. Anything else is unofficial, with the reason in plain words; it never throws and never
  refuses anything.
- **Where it says it:**
  - the launcher's status line, after the version, with the reason as its tooltip, and its About box;
  - the in-game mod menu, the line under its title (the agent tells the mod its check's result; until it has, the line reads
    "Build not checked yet");
  - the bug report's briefing, the line after the build id (from the agent's `MP-OFFICIAL` line; an agent older than this
    release: "not stated");
  - the agent's log (`MP-OFFICIAL official=… text=… reason=…`) and its status line.
  The words are "Official build 0.65.5" (the build's own version) or "Unofficial build — not made by Kingdom Come: Together".
- **The switch** `mp_w224_official` (on): off = the agent does not tell the mod its check's result as official — a build that
  is not checked reads **unofficial**. The line is never hidden by any switch.
- **`AGENTS.md`** in the repository root: the hard rules in plain words for any AI tool that opens the repository — no input
  capture; nothing outside the mod's own install copied, moved or altered; save safety; no NPC brain on the joiner; reliable
  events never dropped; a modified version must not call itself official — and the standing ones (privacy, the version string,
  the README, no network calls, switches and twins, third parties' terms, engine calls). `CLAUDE.md` points at it.
  `docs/OFFICIAL-BUILDS.md` explains the stamp for players, forks and the maintainer.

### 4.2 Proof

On a release-shaped payload of this tree (the release path's own publish step: the launcher, the agent, the relay, the setup
helper, the DLL and the shared assemblies in one folder, 13 files of the project's own), with the signer's and the checker's own
commands and the payload's own agent started for its first lines (an offline, read-only argument). The version is the tree's
(0.65.2 until the release commit sets 0.65.5). [bench, offline]

| the payload | the check says | the payload's own agent says (`MP-OFFICIAL`) |
|---|---|---|
| **built without the key** (`KCT_OFFICIAL_KEY` not set): the signer writes nothing and says so | Unofficial build — not made by Kingdom Come: Together (no signature beside the binaries: a build made without the project's key) | official=0, the same words and reason |
| **signed with the project's key** (13 files) | **Official build 0.65.2** (signed by the project's key; every file as signed) | official=1, "Official build 0.65.2" |
| **one file swapped** (the relay's assembly replaced by another build's) | Unofficial build (KcdMpServer.dll is not the file that was signed) | official=0, the same reason |
| the swapped file put back | Official build 0.65.2 | – |
| the same files asked as another version | Unofficial build (the signature is for 0.65.2 build …, this is 9.9.9 build …) | – |
| the manifest changed by one byte | Unofficial build (the signature is not the project's: the manifest was changed, or another key signed it) | – |

- **No network.** The check's source names no network API (a unit test reads the file for them); it opens the manifest, the
  signature and the files the manifest names, and nothing else. The signer reads the key file and writes the two files.
- **Unit tests** (the agent's suite, with keys the tests make themselves; the project's private key is never on a test
  machine): a signed folder reads official with its version; the same folder without a signature, with any of the four main
  binaries swapped, with a file missing, with another key's signature, with a changed manifest, with a signature for another
  build, with a manifest that leaves a main binary out or names a path outside its folder: unofficial, each with its reason;
  the .NET runtime's files are not listed; an incomplete folder is not signed. The word the agent gives the mod is official
  only for a checked, signed build with a version that is a version, and carries no character the console's command could
  break on. The bug report's line: official only when the agent's flag and its words agree; an older agent: "not stated".
- **The mod's own words** (a synthetic test of the real script, 27 checks): before the agent has spoken the menu's line reads
  "Build not checked yet", never "Official"; the agent's word sets it; a bad version, a missing flag or the switch off read
  unofficial; the line is drawn under the menu's title while the menu is open.
- **The launcher** is not started by this work order's bench (it is the maintainer's own program on his desktop): its line is
  the same call on the same folder as the check above, and it compiles into the payload. Its look on the screen and the
  in-game line on a real screen are in the tester script.
- The proof signature made with the project's key was removed from the proof payload again; that payload reads unofficial.
- **The release build itself** (the installer of section 8) is checked the same way after it is built; its line is in that
  section.
- **The twin** (`mp_w224_official off`): the agent's word to the mod is "not checked", never official (unit test), and the menu
  shows what it is told (synthetic test): unofficial. The launcher's and the bug report's lines have no switch.

### 4.3 What it is not

- It does not stop anyone from building, changing or running the mod; nothing is refused, an unofficial build connects to
  anyone.
- It covers the install folder's binaries. The mod's script pak in the game's mod folder is outside what the check reads: a
  changed pak under official binaries still reads official. Said in `docs/OFFICIAL-BUILDS.md`.
- The in-game line shows what the agent told the mod; anyone can type into their own game's console. The launcher's line and
  the bug report's line are the check's own.
- It is not Windows code signing (`docs/CODE-SIGNING.md`); the two are independent.

## 5. Part E — a contributor's pull request

**Skipped, as the work order says.** At the session's start the contributor's pull request was not rebased on the current `main`
and still carried the changes this project does not take beside the compatible fixes. Nothing of it was reviewed, built or
merged in this work order. [checked at the start: the pull request's base and its changed files]

## 6. Part D — the crash after deaths (`mp_w224_death`)

**The test:** WO-221's staged massacre on the pair — the host's world says an NPC near the joiner died, every seven seconds;
the mod's own swing is queued on the dying copy three seconds before; the joiner is sent 300 m away and back after every ten —
40 deaths a run, each run its own session. A **fault** is the joiner's game closing, or an access violation it survived (it
hangs for seconds and goes on; twice here). Every crash report is kept with its run. [bench]

### 6.1 The suspect, tested directly

WO-221 named its own third approach — before a death the mod applies, every combat action the mod queued on that body is
stopped the game's way and made quiet — as the leading suspect: a stop and a vftable write "on a block the engine may already
have freed".

| the massacre | runs | faults |
|---|---:|---|
| that step **left out** | 2 | **2**: the game closed after 4 deaths and after 1 (33 s and 24 s into the deaths) |
| that step in, as 0.65.2 | 5 | **2: the game closed five deaths in, both times** |

- **The step is a protection, not the cause.** Without it the joiner's game closes within half a minute: a swing of the mod's
  left on a dying body runs into the combat part the death takes apart — the fault WO-221 built the step against.
- **It does not touch freed blocks.** A check was built for exactly that suspicion: before the mod stops, lowers or gives back
  any action it queued itself, it asks whether the block still carries that action's own vftable (the engine's, or the mod's
  quiet copy); if not, the block is left alone and a line is logged. Asked more than 16,000 times over 14 runs:
  **not once was a block not the mod's action**, and with the check on the game faulted as it does without it. The suspect is
  refuted. (A first form of the check sat in the mod's general stop and refused 92 stops of copies' own movement and looking
  actions in one run — other classes, rightly stopped. That run is not counted; the check now asks only about actions the mod
  queued itself.)

### 6.2 What the faults have in common

**Every fault read — eight: six closes and two access violations the game survived, with that step in and out, on five
builds — came 2.6 to 6 seconds after a death was applied to a copy, 0.1 to 1.5 seconds after the mod's own "2.5 seconds later"
step did something to that body.** What the step
did differed from build to build, and the fault came all the same:

| build | 0.6 s after the death | 2.5 s after the death | runs | faults |
|---|---|---|---:|---|
| 0.65.2's way | – | the script's raw physics ragdoll (`RagDollize`) on a body that still stands — 38 of 40 bodies | 5 | 2: the game closed five deaths in, both times |
| a second | the game's own ragdoll request | the request again, then the script's ragdoll | 6 | 1 (survived), 0.1 s after the script's ragdoll |
| a third | the request | the request again; the script's ragdoll only far from the player | 2 | 1 (survived), 0.1 s after the second request — no script ragdoll near it |
| a fourth | the request, once | the script's ragdoll only far from the player | 2 | 1 (closed), 0.5 s after the script's ragdoll on a far body |
| **what ships** | the request, once | **nothing** | 2 | **0** |

Against that: the script's ragdoll thrown **five seconds or more** after a death — by the dead-lay sweep (5, 7 and 9 s) and the
corpse-lay sweep (every 3 s on a body standing within 40 m) — some 250 times in these runs, was never followed by a fault.

The crash sites differ from fault to fault — the action controller's update on an animation worker (a null read; a call
through a freed object), a vector of actions being released, the movement transitions controller of a *living* walking copy
releasing a transition action freed under it, the heap on a render thread — what WO-221 met as "one face each time". All are
one thing: something a body's action controller still pointed at had been freed. **Who frees it was not found.** The first
reading — "it only happens to a seated copy whose ragdoll does not take" (4 closes of 12 such bodies, 0 of 130 others, on
0.65.2's way) — did not survive the next builds: a standing guard and a far villager whose ragdoll took were hit as well.

Two things learned on the way: [binary] [bench]

- **Why the raw ragdoll ran at nearly every death.** The mod applies a copy's death by setting its dead state, with no death
  reaction; the copy stands frozen. The mod then finishes the body "the engine's way", and that routine returned as soon as it
  found the dead state already set — *before* its own ragdoll request. So 38 dead copies of 40 stood for 2.5 s and were thrown
  down by WO-195's fallback.
- **What the game's own request does.** Asked of the body's ragdoll manager (the request the game's hit-death handler makes),
  it lays a dead copy down at once within about 115 m of the player (17 of 19 bodies in one run: on the ground 1.0 s after the
  death). From about 125 m on it is accepted and left pending — the engine does not run it there — and so it is for a near
  body still held by what it was doing (a seat, a kneel). A second request on such a body is answered "no".

### 6.3 What ships

`mp_w224_death` (on):

- **A dead copy is asked to fall the game's own way, once**, 0.6 s after its death (the finish now makes the request it used to
  return before). Most copies near the player lie one second after the death instead of three.
- **Nothing is thrown at a dead copy for the first six seconds.** The script's raw ragdoll at 2.5 s is gone, and no second
  request is made. A body that still stands is laid down by the dead-lay sweep from six seconds on (it was four), by the
  corpse-lay sweep from nine (it was four) — with the script's ragdoll, as before, at a time it has not been followed by a
  fault.
- **The check of 6.1** stays in: a block that is no longer the mod's action is never stopped, lowered or released, and says so
  in the log (`W224-STALE`). It has not fired on the bench; in the field its line would name the caller.

The massacre on the build that ships: **2 runs, 80 deaths, no fault** (0.65.2's way: 2 closes in 5 runs). Of the 80 dead
copies, 48 were on the ground one second after the death by the game's own request; the other 32 were laid down by the sweep,
between 6 and 25 s after the death (half of them within 9 s) — **none was left standing** (0.65.2's way left 6 to 8 of 40 standing after its
three tries). The check of 6.1 was asked 3,805 times and found nothing.

**This is not proof that the fault is gone.** Two clean runs against a way that faults in two runs of five would happen by
chance about one time in three. Three more runs the shipped way and two of the twin were queued when the maintainer paused the
bench for three hours; they were lost with it (both games went away during a join; no crash report, nothing of the mod's). The
finals added three more runs (7.5): as shipped, 5 runs, 200 deaths, no close; the twin ran two there, with no close either. The crash stays in Known Issues, with what is known about it now.

### 6.4 The twin (`mp_w224_death off`, the same build)

`KCDMP_TEST_W224_OFF=w224_death`: no check, no request at the finish, the script's ragdoll 2.5 s after the death — 0.65.2's way.
Measured on four builds of this part (the code path is the same in all): 5 runs, **2 closes** (each five deaths in, within two
seconds of the script's ragdoll on the same seated copy), 3 clean with 6 to 8 of 40 bodies left standing. The twin's runs on
the shipped build itself are among those lost to the pause; the finals ran two (7.5), with no close, so the twin's record over
the work order is 2 closes in 7 runs.

### 6.5 Not done, and known

- **The cause is not found.** What frees the object a body's action controller still holds, in the seconds after the mod
  applied a death to it, is not known. The mod's own actions are ruled out as the freed blocks (6.1); the timing is known
  (6.2); the rest is open. The next step would be the engine's own death path for a body whose dead state was set from
  outside: what it tears down between two and six seconds after, and whether a ragdoll request or a physics ragdoll in that
  window races it.
- **A dead copy the game does not lay down at once stands for six to ten seconds** (a body still held by a seat or a kneel, a
  body beyond about 120 m): three seconds in 0.65.2. In a fight next to the player the game's own request works, and the body
  lies after one second instead of three.
- **Deaths in the massacre are the mod's applied deaths** (the host's word, no blow on the joiner's screen). A death by a blow
  plays its death reaction and falls by itself; those were not what closed the game on the bench.
- **The bench's own switch** `KCDMP_TEST_W224_NOEND=1` (leaves WO-221's step out) is in the DLL for this test and does nothing
  unless that environment variable is set.
- **Retail.** The request's address is the one WO-210 reads for knockdowns; nothing new is resolved.

## 7. The finals on the final build

The final build is the tree of Part D's commit (every switch on); every run below is that build, the field's link shape, each scene
its own session unless said. The finals ran from 26th 23:55 to 27th 18:45. Every run: the real saves' hashes unchanged, the
mirror roots clean. [bench]

### 7.1 Part A, re-measured

With the two follow-ups of 2.4 (the "in a clinch" word every second; a leave does not end a clinch younger than 0.4 s), the drill
as in 2.4:

| | the host's grabs (20 clinches) | the joiner's grabs (23) | the same, not swinging (25) | the bar |
|---|---|---|---|---|
| shown on the other screen within 1.5 s | **19 of 20** (median 0.11 s) | **23 of 23** (median 0.00 s, nine in ten within 0.21 s) | **25 of 25** | 18 of 20 |
| time in a clinch on both screens at once | 43.8 s of 48.2 s | 36.7 s of 40.9 s | 41.8 s of 46.0 s | – |
| one-screen stretches over 1 s | **1** (2.5 s, his own screen only) | **0** (the longest 0.98 s) | **0** (0.56 s) | none |
| the other screen left after it ended | median 0.22 s, longest 0.25 s | – | – | within 1 s |

"Plays on both screens": **met**. "None left stuck over 1 s": **met for the joiner's grabs** (one stretch of 1.5 s on the part's
own build), **missed by one stretch in the host's run**, as on the part's own build: a clinch that ended early on the joiner's
screen (2.4). The rig's own per-clinch count in the joiner's runs says "2 stuck" and "3 stuck": the host's world left his figure's
clinch more than 1 s after his own screen in a re-grab (2.7, "two engines grabbing at once"); no stretch of one screen only
exceeded 1 s.

### 7.2 WO-222's bars

By WO-222's own ledger, over the reference scene (twice) and the whole-scenes run:

| | the final build | WO-222's bar / WO-223's shipped build |
|---|---|---|
| the joiner's clean hits that make the enemy react | **41 of 47 (87 %)**: 5 of 6, 14 of 17, 22 of 24 | WO-223: the reference scene 14 of 27 |
| the host's own clean hits in the same runs | 26 of 29 (90 %) | – |
| the host's reactions shown on the copies | **84 of 84** | – |
| a fleeing enemy stays an enemy | **not shown**: no bandit fled in four chase scenes | as on WO-222's bench, "only now and then" |

The joiner's misses: three "the victim cannot react" (out of its fight), one refused by the enemy's own director, one with no
reaction found, one not applicable. The reference scene is the one WO-223 called out (the bench stops and restarts the bandit's
fight): 19 of 23 there now. The host-kills scene (D in a real fight) killed nobody in 57 minutes (the host's 85 to 93 swings);
it has no data, and its twin was not run.

### 7.3 WO-223's bars

**The approach table** (one session, 15 runs, the day; the mean distance from the walking player and time from the walk's start):

| who, pace | noticed | drawn on | attacked |
|---|---|---|---|
| the host, a walk (2 runs) | 3.4 m / 27.1 s | 6.0 m / 29.5 s | 6.7 m / 30.2 s |
| the joiner, a walk (3) | 8.7 m / 26.1 s | 7.0 m / 28.9 s | 6.4 m / 29.9 s |
| the host, a sprint (2) | 10.6 m / 18.9 s | 3.5 m / 21.2 s | 3.2 m / 21.9 s |
| the joiner, a sprint (3) | 6.1 m / 20.7 s | 5.1 m / 25.4 s | 5.1 m / 26.3 s |
| both, a walk (5) | 9.1 m / 24.8 s | 4.4 m / 27.1 s | 2.5 m / 28.4 s |

- At a walk the joiner is noticed, drawn on and attacked within a second of the host, noticed 5 m further out (WO-223's known
  "further at a walk"). At a sprint he is attacked 4.4 s later than the host (WO-223's shipped build: 2.3 s) — two and three
  runs, inside the runs' own spread, but the wrong way.
- Every run was noticed (WO-223: one walk in eighteen unnoticed). With both players, the camp went for the joiner first in four
  runs of five.

**The two-player camp** (five fights): each player had an enemy on him within 5 s in **5 of 5**. **The guard scene** (the
regression scenes of WO-223): **passes** (5 steps). **Lock-on:** the scene's own pass rule reads "no lock" (as on WO-223's final
build, where the game's own opponent-change lines were the measure: the rule waits 2.5 s for one line per key press in a camp
where everyone joins); the joiner's rule answered yes for a copy in the partner's fight 32 times and in his own 97.

In these four sessions the host's guesses were 3 of 212, 1 of 107, 1 of 147 blows (1.4, 0.9, 0.7 %), and no game faulted.

### 7.4 The scoreboard beside today's control

Three runs of the final build and three of today's `main` (the tree of WO-223's commit, built the same way), alternating on one
afternoon, the field plan, low settings; then two of the final build with every WO-224 switch off. [bench]

| | the final build | | | every WO-224 switch off | | today's `main` | | |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| run | 1 | 2 | 3 | 1 | 2 | 1 | 2 | 3 |
| place (the copy within 0.5 m of the host's person, %) | **76.5** | **63.0** | **75.8** | 82.4 | 73.3 | 82.3 | 82.8 | 80.9 |
| the same activity (%) | 86.0 | 75.6 | 80.7 | 85.5 | 80.1 | 82.3 | 78.4 | 80.9 |
| step | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| pulls / flips (per body-minute) | 0.09 / 0.008 | 0.12 / 0.012 | 0.23 / 0.031 | 0.12 / 0.012 | 0.11 / 0.017 | 0.14 / 0.034 | 0.26 / 0.077 | 0.10 / 0.008 |
| the stream's lag, p99 (ms) | 860 | 954 | 975 | 783 | 893 | 759 | 851 | 831 |
| the mod's cost on the host, 95th percentile (ms) | 7.6 | 8.0 | 7.8 | 9.8 | 8.3 | 8.0 | 5.7 | 7.9 |
| seated and lying bodies: share of samples / place | 38 % / 63.8 | 49 % / 43.9 | 33 % / 57.7 | 29 % / 71.7 | 44 % / 64.8 | 32 % / 63.0 | 23 % / 68.5 | 39 % / 77.1 |
| the other bodies: place | 82.7 | 79.0 | 81.2 | 84.7 | 77.3 | 89.6 | 81.9 | 79.5 |

The works checks (the join, a talk, a forced dialogue, a sleep vote, a loot take, drops, a chest, a mount, a knockdown, the
trade screen): 9 of 10 on the final build and 9 of 10 on `main`, the same check missing on both (no shop screen: the two
shopkeepers tried were busy).

**Place: a regression.** The final build reads lower than `main` in all three pairs, by 5.1 to 19.8 points (means 71.8 and
82.0); by the order's rule, the scoreboard does not show "no regression". The same activity is level (80.8 and 80.5); the
stream's lag is about 116 ms higher at its 99th percentile. Most of the loss is in seated and lying bodies (a sitter a seat off,
a sleeper a bed off): three sleepers a bed off made 1,636 of the second run's 1,732 misses in its tavern; the other bodies are
level in two pairs of three.

**The cause is not found.** With every WO-224 switch off the same build read 82.4 and 73.3: one run at `main`'s level, one at
the final build's. WO-224's mechanisms may carry part of it; the rest is the build or the scenes' mix (the seated share ran from
23 to 49 % between runs). It is in Known Issues, and the installer (8) was built as the order says with this stated beside it.

### 7.5 Part D, the massacre on the final build

The staged massacre of 6 (40 applied deaths around the joiner's figure in the village), three more runs as shipped and two of the
twin (`mp_w224_death off`), each its own session. [bench]

| | runs | deaths | the joiner's game closed | faults |
|---|---|---|---|---|
| as shipped, the finals | 3 | 120 | **0** | 0 (the check of 6.1 found a stale block 3 times in one run and left it alone) |
| as shipped, with 6.3's two runs | 5 | 200 | **0** | 0 |
| the twin, the finals | 2 | 80 | **0** | one burst of 32 caught reads at one site (the save-readiness reader of WO-189) during the join, three minutes before the first death; caught and counted, the game went on |
| the twin, over the work order (6.4 and these) | 7 | 280 | 2 | – |

The twin did not close the game in the finals. Over the work order the build that ships has 0 closes in 5 runs and the twin 2 in
7; if the shipped build closed as often as the twin, five clean runs would still happen about one time in five. **Not proven**,
and Known Issues says so. The rig's verdict on the first twin run reads "fault" for the caught reads at the join; they are not the
crash after deaths.

### 7.6 Crashes and incidents in the finals

- **No crash and no closed game in the finals**, on either copy, in any run (the crash folders' newest reports are from the
  26th, before the final build). No run was voided.
- **Faults:** none in any run of the final build, except the twin's one burst of 32 caught reads at the join (7.5).
- **Skipped:** the twin of the host-kills scene, because the scene killed nobody in 57 minutes as shipped (7.2), so its twin
  would have had nothing to compare.
- **The maintainer's own programs** (launcher, agent, relay) were not stopped or touched by the finals; the bench runs its own
  copies from their own folders.
- Every run: the real saves' hash list unchanged, the mirror roots clean.

## 8. The installer, and the official check on it

The installer is built from the release commit (the one that sets `VERSION` to 0.65.5), by the release-candidate path, signed
with the project's key. Its name, size, hash, the live save round trip and the official check on its payload are added by the
commit after it.

## 9. Thread and lock audit (every engine call WO-224 adds)

The rule: an engine movement or AI call is made on the thread and at the point in the frame the engine makes it; no engine call
that may take an engine lock runs under our fault guard. "Its own guard" below means a structured-exception catch around that
call; every per-frame task and pipe op of the mod has run inside the frame task list's outer catch since 0.4x, unchanged here.
Memory reads of engine objects are guarded everywhere (they take no lock) and are not listed.

**Where the engine itself makes these calls** — measured, not assumed. The two clinch detours and the combo-input detour count
the thread of every call the engine makes: for NPCs the clinch test, the clinch start and the combo input come on worker threads
(one host run: clinch tests main 9,960, other 93,482; clinch starts main 5, other 17), for the local player on the main thread.
And they count how many of the calls on another thread arrived while the main thread was inside the mod's own per-frame work
(after the engine's module update has returned: the task list and the queued pipe ops, where the mod makes its calls): **0 of
6,746,764 clinch tests and 8,916 clinch starts on other threads in every run of both machines** (71 machine-runs of the final build: the finals, the scoreboard and the massacre), and 0 of 2,586 combo inputs on other threads (378 more came on the main thread). The engine's combat workers do not run beside the mod's work; the mod's
calls on combat actors at that point race nothing of theirs. [bench]

| part | call | where the mod makes it | its own guard | may it take an engine lock? |
|---|---|---|---|---|
| A | the engine's clinch test and clinch start, hooked (detours: the answer is seen, the engine's own function runs untouched; a pair the rule refuses gets the function's own "no") | the engine's own thread, inside its own call | none (the detour is plain reads, atomics and one leaf lock of ours around a 64-entry record) | the engine's own call as its caller made it |
| A | `StartSyncGuard(35, master, slave, 0, 0)` — the mod's own clinch start | main thread, the mod's per-frame work (a pipe op, or the tick that follows the host's word) — where WO-197 has made this call since 0.61.0 | **none** (was guarded until this work order) | allocates the two guard actions through the actors' own factories; no lock of the engine's seen in its code path [binary, read] |
| A | `C_ActionDirector::TerminateInternal(director, 0)` then the engine's `EnsureGuardAction(actor)` — a fresh guard for a body held in an ended clinch (the two calls `EnsureGuardAction` itself makes for a body whose main action is not a guard) | main thread, the same point | none | as above |
| A | the weapon component's update (class from hands -> the model's `WeaponClasses`) and its class-from-hands read | main thread, the same point; only for a copy in combat mode whose two readings differ | none | reads the actor's hand items through the item manager (the engine's own getters) |
| A | the `WeaponClasses` property's own setter (one int, with the property's change signal) | main thread, the same point; only while the copy's hand holds no item object | none | no |
| A | `human:HolsterToInventory`, `human:DrawFromInventory(item, hand, false)`, `inventory:FindItem` (Lua) — the copy's weapon checked 0.9 s after a draw | main thread (a `Script.SetTimer` callback of the mod, where WO-167's draw runs) | Lua's `pcall` | the script binding's own |
| B | the engine's combo input, hooked on the host (a detour that reads the body's slot flag and requested zone and records; the engine's function runs untouched) | the engine's own thread, inside its own call | none | the engine's own call |
| B | the engine's combo input on a copy (`comboComponent->Input(inputClass)`) | main thread, the mod's per-frame work, in the frame the copy's own chain slot reads open | **none** | fires the component's own signals (the running attack's listener); no lock seen [binary, read] |
| B | the engine's setter of a model's bool property, on a copy's `ComboSlot` (open before the copy's combo input when the host NPC chained and no contact opened it here; closed again by the mod) | main thread, the same point, the same frame as the input | **none** | writes one byte and fires the property's change signal (the listeners the engine's own combo trigger reaches by the same call); no lock seen [binary, read] |
| B | the requested attack zone's setter and the guard zone's setter (the two the stream's writer has used for copies since 0.2x) | main thread, the same point | the existing guard of those two helpers, unchanged | property setters; no |
| B | the engine's AI attack for a copy (WO-209's call, unchanged; now given the row's starting guard instead of its attack zone) | main thread, as before | the existing guard of that call, unchanged | as audited in WO-209 |
| C | none: the official-build check reads files and verifies a signature; it makes no engine call and no network call | the launcher's and the agent's own start | – | – |
| D | the body's own ragdoll manager's `RequestRagdoll(manager, 0)` for a dead-state copy that is no ragdoll (the request the game's hit-death handler makes; WO-210 has made it for knockdowns since 0.63.9) | main thread, the mod's per-frame work (the agent's "finish this body" pipe op) | the existing guard of that call site, unchanged | a request: it checks the actor's state and hands the ragdoll to the actor's own update; no lock seen [binary, read] |
| D | the question "is this block still an action of ours" — one pointer read of the block's first word, compared with the engine's vftable for that class and the mod's quiet copy — before the mod's own stop, priority write and reference release | wherever those already ran (main thread; the table's stop also inside the engine's own release call on the engine's thread) | the read is under the fault guard (a read takes no lock); the calls it gates are as they were | no |
| D | removed for a body the manager lays down: the script's `entity:RagDollize(0)` on a dead copy (it stays the last resort for a body the manager does not lay down) | main thread (a console statement of the agent's) | Lua's `pcall` | the script binding's own |

No engine movement or AI call of this work order is made off the thread the engine uses for the player's own (the main thread)
except inside the engine's own calls (the detours); no call that may take an engine lock was put under a guard of its own.

## 10. README status for 0.65.5 (for the maintainer to paste)

`README.md` is the maintainer's and was not touched. The block below replaces its release badge line, its "Where it's at"
table, the "As of" line and keeps the Known Issues link. Each line of the table names what backs it: a run of this work order
(the bench's run names; their logs and frames are in the WO's notes), a framed scene, or the earlier findings it is carried
over from unchanged.

The badge (line 11 of the README):

```html
<a href="https://github.com/DeepFriedDepp/KingdomCome-Together/releases/latest"><img alt="Latest release" src="https://img.shields.io/badge/latest%20release-v0.65.5-8a3324?style=flat-square"></a>
```

The table and the two lines under it:

```markdown
| Works well | Works, but rough | Not yet |
|---|---|---|
| Joining a friend's world with your own character, or a new one | Joining each other's conversations: you listen in and vote on answers, but the camera and keys can misbehave | Playing far apart (you are kept within about 650 m) |
| Seeing each other: walking, riding, clothes, weapons | Villagers walk on their own feet on the partner's screen, but not always in the same place (a little less often than in 0.65.2) | Shared chests and shop stock (each player has their own) |
| Fighting the same enemies: hits, flinches, knockdowns and deaths show for both | Some villagers stand idle, or work without their tools, where the host sees them busy | Road encounters matching on both screens |
| Enemies notice and fight both players | Grabs in fights: both of you are held and see it; the strike out of a grab is a plain one for the joiner | Stolen marks on shared items |
| Shared quests: either of you can move a quest forward | Enemy combos play as the same chain on both screens; a finisher is sometimes a plain blow, and some blows on the joiner are still the host's call | The regular (non-Modding-Tools) game |
| Looting bodies and dropping items for each other | Cutscenes play for both, but one can leave the partner on a black screen for a while | |
| Sleeping together, one shared clock | The joiner's game can close after people die near him (rarer in this build) | |
| Crime: the host's guards judge both of you | Bows and crossbows: the shot shows, the draw and aim don't | |
| A horse for each player; trading as the partner | Carts follow about a metre off; lip sync, voice chat and dice between players are unpolished | |
| Every build says whether it is official | | |

<sub>As of release 0.65.5.</sub>

The full, current list lives in **[Known issues](docs/KNOWN-ISSUES.md)**. What changed in each build is on the
**[releases page](https://github.com/DeepFriedDepp/KingdomCome-Together/releases)**.
```

What backs each line:

| line | backed by |
|---|---|
| Joining a friend's world | every bench run of the finals joined (the join step of 30-odd sessions on the final build; the scoreboard's works check, 7) |
| Seeing each other | carried over unchanged from 0.65.2 (WO-221, WO-222); nothing of this work order touches it |
| Fighting the same enemies | WO-222's bars on the final build (7): the joiner's clean hits flinch 41 of 47, the host's reactions on copies 84 of 84; knockdowns 2 of 2 shown with get-ups (the works check) |
| Enemies notice and fight both players | WO-223's approach table on the final build (7): walking, every stage within a second of the host's; the two-player camp 5 of 5 with an enemy on each within 5 s |
| Shared quests | carried over from WO-210 and WO-221; not re-measured |
| Looting, dropping items | the works check on the final build: a loot take, 2 of 2 drops taken by the other, a chest (7) |
| Sleeping together | the works check: a sleep vote, one clock after it (7) |
| Crime | WO-223's guard scene on the final build: PASS, the crime lines on both (7) |
| A horse each; trading | the works check: a mount; trading fixed in 0.65.2 (Known Issues, "Fixed in 0.65.2"; the bench's shop screen did not open in this work order's works runs, shipped or control: two shopkeepers busy) |
| Every build says whether it is official | section 4.2 and the release build's own check (8) |
| Joining conversations | carried over from WO-206/WO-217; the works check's talk and forced dialogue passed (7) |
| Villagers walk on their own feet, not always in the same place | the scoreboard (7): the copy within 0.5 m of the host's person 72 % of samples on the final build against 82 % for today's `main` (a regression, cause not found: 7.4 and Known Issues) |
| Some villagers stand idle or work without tools | the scoreboard's "the same activity" (7) and WO-221's activity findings |
| Grabs in fights | section 2.4 and the re-measure on the final build (7): 19 of 20, 23 of 23, 25 of 25 shown; the exit strike (2.7) |
| Enemy combos | section 3.3 and 3.3a: the same chain, the host's guesses 0.7 to 2 % in camp and swap scenes; finishers 3.6 |
| Cutscenes | carried over from WO-194/WO-221 |
| The joiner's game can close after deaths | section 6 and 7 |
| Bows, carts, lip sync, voice, dice | carried over from 0.65.2's README; nothing of this work order touches them |
| Carts about a metre off | Known Issues since 0.65.2 (the cart switches); moved from "Not yet" |
| Not yet (the five left) | carried over from the README's current table; grabs and carts moved out of it |
