# WO-223 — Enemies notice the joiner, and fight him on both screens

Roles only: "the host", "the joiner", "a tester", "the maintainer". Footing marks: [code] read in this repository, [files] read in the
game's own data files, [binary] read in the game's own code, [bench] measured on the bench (two copies of the game on the
maintainer's PC, joined through our relay with the field's link shape — 80 ms ± 20 round trip, 0.5 % loss, two stalls of 0.3–0.9 s a
minute), [reasoned] an inference, said as one. "Shipped" is the final build with every switch on; "the twin" is the same build with
every WO-223 switch off on both machines but the notice log (an instrument, never part of a twin). Run names are the bench's own;
their outputs, frames and ledgers are in the WO's notes.

**The field bundles.** The two testers' bundles of the session this work order answers were not on the bench PC when it ran (looked
for at the start and again before the final runs). Everything below is measured on the bench, with instruments built to show what
the bundles could not; the work order's own quotes from the testers are taken as what happened.

**The builds.** The last four builds of the work order carry the code of Parts B and E; they differ in three things. The
copy's pin of Part C (5.2) came with the second; rows measured before it say so. Part D's default (section 6) was turned on
the third: the two before it retired WO-222's reaction offer, from the third on it is kept. The fourth — the shipped code —
makes the host's gate ask the engine less often while no enemy is near (2.2, 9.1). "The final build" in sections 0 to 5 and 7
is the second or third of these; section 6, the solo fights and one guard scene are on the third; the fourth has its own short
table (0), its scoreboard runs (9.1) and a last pass of the swap, the two-player camp, the guard scene and the ten checks.
Nothing was committed part by part: the commits were made at the end, from the tree the fourth build was made of.

## In short

- **The cause.** A hostile NPC never looks at a body that is not the player unless one of the game's behaviours tells it to, and
  the engine runs full perception only around the host. The joiner's figure is such a body. Alone, he walked into a camp of six
  and stood among them: nobody turned (five runs of five on the twin).
- **The fix (Part B).** The host's mod keeps the engine's own recognition meter for every hostile NPC near a joiner, with the
  joiner's own visibility, noise and stealth skill, and names the pair to the engine when a player in his place would be
  recognised; until then the NPC's own sight of him is held off. From there the alert, the draw, the fight and the charge are
  the engine's own. Walking and sprinting he is now noticed, warned and drawn on within a second of the host's times
  (attacked 0.3 s sooner at a walk, 2.3 s later at a sprint); in one walk of eighteen nobody noticed him. A camp keeps its own rule (no attack on a crouched or unarmed visitor for half a minute); its warnings are not
  played to the joiner.
- **Two players at a camp.** Each has an enemy on him within 5 s in 23 fights of 25 (3 of 5 with that switch off).
- **On the joiner's screen (Part C)** the copy of an enemy that fights him is in his fight and aimed at him in 99.8 % of samples,
  also after a swap and when the host strikes the same enemy. The host's guesses of blows on him are down to 2 to 6 % (the bar
  was 1 %): what is left are blows of chains and combos his copy cannot play in time.
- **Part D.** An enemy that came at the joiner by itself is in combat mode at most of his blows and flinches at his clean
  hits: 16 of 16 in fights both players are in, 9 of 12 in a fight he has alone with the host 120 m off. In WO-222's staged
  reference scene it is not — 14 of 27 as shipped against 16 of 17 with this work order's switches off — so there this work
  order costs flinches; why was not found. WO-222's stand-in is kept, not retired (retiring it was built and is the switch's
  off side).
- **Part E.** Villagers, guards, shops, a witnessed crime, a dog and a horse behave as in 0.65.2 on the bench; wolves and a
  companion could not be shown.
- **Bars missed:** the distance half of Part B's bar, the camp's warnings, night; the 1 % for host guesses; the reaction
  parity of Part D in WO-222's reference scene. See 9.3.
- **The scoreboard found a regression of this work order's, and it was fixed before shipping.** With a full tavern, place read 69.6 to 70.7 where 0.65.2 read 72.7 and the twin 76.5 and 83.6: the host's gate asked the engine for every entity around the figure six times a second, enemy or not. The shipped build asks once a second while no enemy is near and reads 74.4 and 79.3 with a full tavern; pulls, flips and the ten checks are inside the spread (9.1).
- No crash. Five times the bench's memory guard closed both games while one of them stood far above its usual size — an
  old, intermittent condition, seen on 0.65.2's own build too (9.2).

## 0. Part A's table and the bars

One bandit camp of six, the same for every run; each run from a freshly loaded camp, the walker starting 80 m out and walking
straight in. Distances are from the walker to the bandit that did it, times from the walker's first step; each cell is the mean
of the runs that showed that stage (their number in brackets). "The camp has him" is the mod's own mark for a joiner (the moment a
camp NPC would recognise a player in his place); "noticed" is the engine's alert, "warned" its first bark at him, "drawn on" a
weapon out, "attacked" the bandit's own fight set on him. Host rows and the joiner's rows of five were measured on the build with
Part B's final rule; the joiner's further runs and every two-player row on the last builds — six of his walks, four of his
sprints, one crouched walk and two two-player walks on the shipped one. The joiner is armed (as the bench host is); his
conspicuousness is shifted to the host's for a like-for-like table (2.3). [bench]

**Before** (the twin: every switch of this work order off — the game as 0.65.2 and WO-222 left it):

| who | pace | runs | noticed | attacked | first set on |
|---|---|---:|---|---|---|
| both together | walk, day | 1 | 20.9 m / 20.4 s (1) | 13.7 m / 22.7 s (1) | the joiner 1 |
| the joiner alone | crouched walk, day | 1 | – | – | nobody |
| the joiner alone | sprint, day | 1 | – | – | nobody |
| the joiner alone | walk, day | 2 | – | – | nobody |
| the joiner alone | crouched walk, night | 1 | – | – | nobody |

Alone, nobody noticed him in any of the five runs: he walked, sprinted and crouched to the middle of the camp. With the host
beside him the camp woke for the host and at once turned on the joiner's figure, from 21 m — a body that is no player is seen
at an alert bandit's full view radius, without the seconds a player is given.

**After** (shipped):

| who | pace | runs | m/s | the camp has him | noticed | warned | drawn on | attacked | first set on |
|---|---|---:|---:|---|---|---|---|---|---|
| the host alone | walk, day | 5 | 2.9 | – | 7.3 m / 26.4 s (5) | 5.9 m / 26.7 s (5) | 4.5 m / 29.4 s (5) | 3.7 m / 30.3 s (5) | the host 5 |
| the joiner alone | walk, day | 18 | 3.1 | 11.4 m / 24.4 s (18) | 8.8 m / 26.2 s (17) | 8.2 m / 26.7 s (17) | 6.1 m / 29.1 s (17) | 5.7 m / 30.0 s (16) | the joiner 16 |
| both together | walk, day | 7 | 2.9 | 13.7 m / 23.5 s (6) | 7.2 m / 24.6 s (7) | 6.9 m / 24.7 s (7) | 6.6 m / 27.1 s (7) | 5.9 m / 27.9 s (7) | the host 4, the joiner 3 |
| the host alone | sprint, day | 5 | 4.4 | – | 11.7 m / 16.9 s (5) | 11.0 m / 17.1 s (5) | 8.1 m / 19.1 s (5) | 5.6 m / 20.7 s (5) | the host 5 |
| the joiner alone | sprint, day | 10 | 4.8 | 11.7 m / 15.1 s (10) | 5.0 m / 17.0 s (10) | 4.2 m / 17.2 s (10) | 4.2 m / 19.8 s (10) | 4.1 m / 23.0 s (7) | the joiner 7 |
| both together | sprint, day | 5 | 4.4 | 12.4 m / 15.8 s (5) | 12.4 m / 14.6 s (5) | 12.4 m / 14.6 s (5) | 6.3 m / 17.4 s (5) | 5.5 m / 19.2 s (5) | the host 3, the joiner 2 |
| the host alone | crouched walk, day | 5 | 1.8 | – | 7.7 m / 46.4 s (5) | 7.2 m / 46.8 s (5) | 4.6 m / 49.0 s (5) | 3.5 m / 74.6 s (5) | the host 5 |
| the joiner alone | crouched walk, day | 7 | 1.9 | 9.3 m / 40.0 s (7) | 5.4 m / 70.2 s (6) | 5.5 m / 71.0 s (6) | 5.5 m / 73.8 s (6) | 5.5 m / 74.6 s (6) | the joiner 6 |
| both together | crouched walk, day | 5 | 1.8 | 9.8 m / 44.5 s (5) | 7.6 m / 45.6 s (5) | 7.3 m / 46.0 s (5) | 6.5 m / 48.2 s (5) | 4.9 m / 53.5 s (4) | the joiner 4 |
| the host alone | crouched walk, night | 5 | 1.8 | – | 6.2 m / 50.8 s (4) | 6.2 m / 51.2 s (4) | 5.2 m / 54.2 s (4) | 3.5 m / 55.6 s (4) | the host 4 |
| the joiner alone | crouched walk, night | 6 | 2.0 | 3.4 m / 46.8 s (3) | 1.9 m / 56.2 s (2) | 1.9 m / 56.8 s (2) | 2.1 m / 60.6 s (2) | 2.7 m / 49.0 s (1) | the joiner 1 |
| both together | crouched walk, night | 5 | 1.8 | 5.6 m / 57.6 s (3) | 5.8 m / 42.1 s (5) | 5.5 m / 42.3 s (5) | 5.0 m / 45.8 s (5) | 4.4 m / 47.2 s (5) | the host 3, the joiner 2 |

**The bar for Part B, pace by pace** (the joiner alone against the host alone: within 15 % of his distance and within 2 s of his
time, the mean of the runs):

| pace | noticed | warned | drawn on | attacked | verdict |
|---|---|---|---|---|---|
| walk, day | +21 % / −0.2 s | +39 % / 0.0 s | +36 % / −0.3 s | +54 % / −0.3 s | time met at every stage; distance missed; in one walk of eighteen nobody noticed him |
| sprint, day | −57 % / +0.1 s | −62 % / +0.1 s | −48 % / +0.7 s | −27 % / +2.3 s | time met but for the attack (one run of the seven that had one came at 36 s: a tree held him); distance missed |
| crouched walk, day | −30 % / +23.8 s | −24 % / +24.2 s | +20 % / +24.8 s | +57 % / 0.0 s | the attack on time; the three stages before it missed |
| crouched walk, night | −69 % / +5.4 s | −69 % / +5.6 s | −59 % / +6.3 s | −22 % / −6.6 s | missed: noticed in two runs of six |

What the numbers say, and why the bar is not met as written:

- **Walking and sprinting, the joiner is noticed, warned and drawn on at the host's time** — within 0.7 s at every stage,
  inside the 2 s; the attack too at a walk (−0.3 s), and 2.3 s later at a sprint over the seven runs that had one (one of
  them at 36 s, the walker held by a tree). The distances are another matter. The two bench walkers do not move alike (2.9
  against 3.1 m/s at a walk, 4.4 against 4.8 at a sprint), the mod's rule is the engine's own, which is a rule of seconds in
  view, and single runs scatter by more than the two means differ: the joiner was noticed at a walk between 3.0 and 12.2 m
  out in the seventeen runs in which he was, the host between 5.4 and 8.8 m in his five; at a sprint 1.8 to 10.8 m against
  8.7 to 15.8 m. Which bandits face the path when he comes decides most of it.
- **In one walk of eighteen nobody noticed him** (a run of the shipped build). He reached the middle of the camp and stood
  there for 45 s. The mod had named the one bandit that had him in view by its own line of sight, 14.4 m off, and the DLL had
  set the pair; that bandit never alerted — the engine's own rays did not see him where he had stopped. Two bandits slept;
  the two awake ones within 3.3 m faced away (the rule for "this near, whichever way it looks" is 1.6 m, as measured on the
  host's own player). The mod's line of sight and the engine's can disagree, and when the only NPC in view is one the engine
  itself does not see, nobody notices him until he moves.
- **Crouched by day** the camp attacks the joiner at the same second as the host (after its half minute of patience), and from
  further off. The three stages before the attack come 24 s late because they *are* the camp's warnings, which the joiner is
  not played (section 3): for him the camp is silent until it attacks.
- **Crouched at night** the joiner gets nearer than the host before anyone notices — in four runs of six he reached the middle
  of the camp unnoticed, where the host was noticed in four of five. His own game reports him darker than the host's reports
  the host on the same path at the same hour (visibility −1.46 against −1.21), and by the engine's rule that is a radius of
  5.5 m against 7.9 m. The mod takes his own game's word; which of the two readings is right for that spot was not settled.
- **Both together** (the two walking in side by side, 1.5 m apart): the camp's first fight was set on the host in 10 runs and
  on the joiner in 11 of the 21 that had one, at the host's own times and distances (the last four columns of the rows "both
  together"). The second half of that bar — an enemy on each within 5 s — is section 4.

Bar for Part B, in one line: **met for the time at a walk, and at a sprint up to the draw, and for the moment of the attack
when crouched by day; missed for the distance, for the sprint's attack (+2.3 s), for the warnings of a camp, and at night;
and once in eighteen walks he was not noticed.** Three ways of setting the distance were built and measured
on the way (WO-195B's reach factor; the engine's meter with its visibility term; the meter without it and with the look-ahead
stepped along his path, shipped); 2.3 has what each got wrong.

## 1. What the engine reads to decide a player is seen or heard

Read live on the host's game, in the game's own behaviour files and in its code. [bench] [files] [binary]

- **Each NPC's own eyes.** Two derived stats of its soul: a view radius (18.7–21.4 m awake at the bench camp, 28.1 m once it is
  alert, 0 asleep) and a field of view. Inside both, the engine casts rays from the NPC to the body it looks at; a tree, a rock or a
  tent in the way hides it.
- **Whom it bothers to look at.** The engine gives every body an NPC could look at a priority and works on the best few (the
  game's parameters: a boost of 20 for the player, conspicuousness × 6, a drawn weapon × 6, people in view × 3). The player's boost
  puts him at the top for everyone, always. Another NPC's body has no such boost. The game's own behaviours raise an NPC's
  attention to one body with a per-pair setting, the relation context `PerceptionPriorityBoost`.
- **How long it needs to recognise a player.** For the player — and only for him — seeing is not knowing. The engine keeps a
  meter per NPC (its recognition data):
  - the radius he can be seen at = the NPC's view radius × (his visibility below 0: 1 − visibility / −2, held in 0..1) ×
    (1 − 0.3 × his stealth skill as a fraction), never under 2.5 m;
  - the seconds of clear view it needs = (4 − 2 × his conspicuousness; − 4 × below 0) × a curve of distance / radius (0 at the
    NPC's feet, 1 at the edge, steep in the middle: the engine's gain function, 0.1) × (1 + 0.6 × skill); nearer than 2.5 m none.
    (The game's code also carries a factor for a visibility above 0, 1 − visibility / 3.5. Measured on the host's own player, its
    alert does not follow it: a meter with that factor fills 0.8–1.3 s before the engine's alert, one without it on the beat —
    0.0 s and +0.5 m over six approaches, sprints included. The mod's meter leaves it out.)
  - "clear view" = inside the NPC's field of view and one free line from its eyes to his chest (the form that matched the
    engine's alert best of six tried: no line, one line, any of six lines; each with and without the factor above);
  - out of view what it has gathered falls by half a second's worth a second.

  For any other body the engine multiplies the time by a parameter that is 0: an NPC's body is known the moment it is in view, at
  the full radius, by night as by day. The numbers are the game's parameter object's, read from the running game by the DLL (the
  `WO223-ENGINE` line) and handed to the mod; the mod's built-in copies are the same.
- **The stages a player goes through.** While the meter gathers, the NPC is in the first recognition level: beyond 5 m it says
  the "recognising" bark; it is not alert yet. Then it comes to check, then it knows him and its reaction runs. The table below
  calls the bark `sign` and the alert (the NPC's view radius going to its alerted 28.1 m) `notice`.
- **Who is an enemy, and what a camp is.** The awareness behaviour asks the relationship between the two souls. A soul that reads
  enemy is attacked on sight. The bench camp's bandits do **not** read enemy to the host's own player (their relationship to him is
  0; −0.2 after a fight with him): the game sets them on a player through its camp-trespass routine instead. The camp's ground is
  a private area (out to 25–40 m from the fire). A player they recognise or hear on it: armed and standing, he is attacked at once
  (the game's own script calls it "armed and looks like a threat"); otherwise he is warned ("get out", a talk he can answer), every
  seven seconds or so, and attacked after the fourth warning — 29.9 s after the alert, measured — or when he walks up to one of
  them. All of that routine is written for the player alone: its talk, its HUD marks, its crime record.
- **Where the engine runs any of this.** Its AI has three levels of detail around the host's own player: full detail for the 70
  nearest NPCs within 150 m, a reduced level to 600 m, and a sleeping level beyond. Perception runs in full detail only.

**What the joiner's figure carried into this before WO-223:** a body of the NPC class with an ordinary soul — an enemy to bandits
by the faction table (−1), registered as something NPCs can perceive. **What it did not carry:** the player's boost (so no bandit
attended to it), any of his own stealth values (an NPC's body is seen at the full radius or not at all), the camp routine (it was
an enemy on sight where the host is a trespasser to be warned), and the host's presence (with the host more than about 140 m away
the camp's NPCs are not in full detail and perceive nothing).

**What the figure gives the engine now:** the per-pair attention from each hostile NPC that would recognise a player in the
joiner's place, set at the moment it would; until then that NPC's own sight of the figure held off; the joiner's own noise as the
engine's movement sound from the figure; the camp routine's outcome; and full detail around him wherever the host is.

## 2. Part B — why the camp ignored the joiner, and the fix (`mp_w223_perceive`, the host)

### 2.1 The cause (B.1)

Measured before anything was changed (the twin): the joiner walked from 80 m into the middle of a camp of six bandits, passed one
at 3 m and stood among them for two minutes. Nobody turned. Three things were ruled out first: the relationship (−1, enemy), the
figure's registration as perceivable (the engine takes it), and the engine's perception switches (no effect at run time). Two
causes were found, each shown by changing it alone: [bench]

1. **Attention.** With the game's own per-pair setting (`PerceptionPriorityBoost`) set from the six bandits to the figure — and
   nothing else changed — the first walk in was noticed at 20 m: the bandit's view radius went to its alert value, it barked "sees
   an enemy and will attack", drew at 16 m, the engine armed its own skirmish with the figure in it and the bandit charged. Without
   the pair, eleven walks gave one notice.
2. **The level of detail.** With the pairs set and the host 200 m from the camp, nothing; with the engine's full-detail radius
   raised to reach the camp (`WH_AI_LOD_MaxDetailDistance`, which the engine reads live), noticed again.

### 2.2 The fix

All on the host, in the mod's Lua and one small piece of the DLL: [code]

1. **The gate.** Twice a second, for each joiner's figure, the host's mod looks at the hostile NPCs within 45 m and keeps the
   engine's recognition meter for each (NPC, joiner) pair — with the **joiner's own** visibility, conspicuousness, noise and
   stealth skill, as his own game reads them (his agent sends them on every change), the NPC's own view radius and field of view,
   and one line of sight. It looks ahead along his way in (the meter stepped a tenth of a second at a time at his closing speed,
   four seconds ahead) and so knows, for every NPC, **when it would recognise a player in his place**. With no enemy of his
   within those 45 m for three seconds the look runs once a second instead of twice, and the notice log (Part F) reads the
   gate's own look instead of making its own: in a village that is one query of the engine a second where the builds before
   the last one made six (9.1).
2. **The naming.** 1.75 s before that moment the pair is named: the DLL sets the game's own per-pair attention from the NPC to the
   figure, and takes it away when the gate drops the pair. From there everything is the engine's: the alert, the bark, the draw,
   its own skirmish with the figure in it, the charge.
3. **The hold.** Until 0.6 s before that moment the NPC's own perception of the figure is held off with the game's own "do not
   perceive this body for a while" (`XGenAIModule.IgnorePerception`, as the game's behaviours call it). Without it an NPC with the
   figure in clear view finds it by itself, as it finds any NPC's body: at once, at its full radius, crouched or not, dark or not.
   The hold is a chain: the engine drops a hold asked for while one runs and has no way to end one from a script, so the mod asks
   for a hold for every NPC whose eyes are not yet due on every tick of its 20 ms timer — the engine takes the one right after the
   last hold's end — and each hold is no longer than what is left until the NPC's eyes are due (3 s at most; 1.2 s once that is
   within the look-ahead).
4. **The range.** While a figure is further from the host than the engine's full-detail radius, the radius is raised to reach it
   (never above 450 m) and put back after — also when the agent's beat stops.

Hearing: his steps within WO-195B's noise reach name the pair too ("heard"; a sleeper is woken), and the engine is given his
movement sound from the figure, as loud as his own game says.

Hostile means: the NPC's own relationship to the figure reads enemy, or it is in a fight with him already, or it is an animal
that reads the host's own player as an enemy.

### 2.3 What went wrong on the way (so nobody repeats it)

- **The hold's length must be written as the game writes one.** `IgnorePerception` takes a text: `'5s'`, `'500ms'`, `'1m'`,
  `'-1'`. A decimal (`"1.5s"`, `"1.0s"`, `"0.1s"`) is taken without any error and holds nothing. Five builds of this work order
  ran with decimal lengths: 4,045 holds asked for in one session, and every bandit with the figure in clear view alerted to it
  un-named (day crouch at 12.4 m twice, a walk at 20 m). No file of the game's 2,704 behaviour scripts writes a decimal length; 95
  write `'500ms'`. [bench] [files]
- **A hold asked for while one runs is dropped.** Shown by one run with 60 s holds: a bandit named at 13 m (and asked for a 50 ms
  hold on that beat) never saw the joiner for the rest of the run. So a hold renewed every half second leaves a gap of up to half
  a second at each end, and in a gap an NPC sees the figure at once: two bandits facing the path alerted in the same instant at
  20 m, un-named. Asking around an *estimated* end (2 s holds, every 40 ms) still leaked once in two crouched runs. Asking on
  every 20 ms tick did not leak in any run after: 14 first alerts over the joiner-alone runs of the last builds, every one of a bandit the gate had already named. [bench]
- **A hold stops eyes, not ears.** A sleeper the gate named "heard" answered the sound under a running 60 s hold. [bench]
- **The yardstick.** The host's first "notice" in the early tables was the "recognising" bark of the player's first recognition
  level — said only beyond 5 m, and not an alert. The table has a stage of its own for it now (`sign`).
- **Two different men.** The bench joiner's Henry (a sword and nothing else on) reads a conspicuousness of 0.60 standing and 0.10
  crouched; the bench host's Henry 0.12 and −0.38. By the engine's own rule the joiner is made out a quarter to a third sooner —
  correctly. The table's like-for-like rows give the host's mod the joiner's values with that one number shifted by −0.48 (a bench
  stand-in for "the same Henry"; nothing of it is in the mod); two rows with his own values are shown beside.
- **The look-ahead.** Judging the whole look-ahead by the seconds needed at its end named a walking joiner 1.2 s early: the
  engine's curve of seconds over distance is steep in the middle of the reach, which a walk crosses. The meter is stepped along his
  way in instead.

## 3. A camp is not an enemy on sight (`mp_w223_guard`, inside Part B)

The bench camp's bandits read **0** to the host's own player and **−1** to the joiner's figure. With Part B alone the camp attacked
the joiner the moment the first bandit made him out, at 12–20 m, where the host — crouching in with his weapon out — is recognised
at 8 m, warned four times and attacked half a minute later. The game's camp routine cannot be given to a figure (its talk, its
marks and its crime record are the player's own script), so the mod keeps its **outcome** for an NPC that reads enemy to the
figure but no enemy to the host's own player: [code] [bench]

- off the camp's private ground (the host world's own area label): nothing;
- on it, once a camp NPC would recognise or hear a player in his place, the camp "has caught him" (`WO223-CAMP` in the log): an
  **armed joiner who stands** is fought at once; **anyone else** — crouched, or weapon away — when the camp's patience is spent:
  30 s after the catch, or at once when he comes right up to one of them (first had further than 4 m and now within 2 m, or 10 m
  nearer than where it first had him). Once one of the camp fights him, all that have him do. Off its ground for a minute, the
  camp's count of him starts again.
- Its warnings are **not** played to the joiner. During the patience he sees and hears nothing from the camp. That is the limit
  of this part: "warned" and "drawn on" have no meaning for a crouched or unarmed joiner at a camp until its patience is spent, and
  the tester script says so.

Measured: a crouched joiner by day was attacked 32.3, 33.7 and 33.8 s after the camp had him (the 30 s, and the two to four
seconds an enemy needs from free eyes to its own attack); the crouched host 29 to 36 s after its notice in four runs of five
(14.6 s in the one where he walked right up to them). In the table (section 0) both are attacked at 74.6 s into the walk.

Twin (`mp_w223_guard off`, the rest on): a camp NPC is an enemy on sight again — on the final build a crouched
joiner was named "seen" at 10.9 m, noticed at 9.2 m 43.7 s into his walk and attacked 7 s later (50.8 s), where the crouched host
and the joiner with the switch on are attacked at 74.6 s. [bench]

## 4. Both players at the camp (`mp_w223_split`, the host)

Two players walking into the camp side by side: the joiner always had an enemy on him within 5 s of the fight's start, the host in
five runs of ten (the first ten-run scene, before this part). In the five others every bandit awake in those seconds had taken the
joiner. Two things make it so: [bench]

- The camp fights an armed joiner who stands on its ground from the moment one of it would recognise him — 13 to 17 m — and the
  pair is named then. The host's own player is noticed by the engine's own routine at about 7 m and attacked a few seconds later.
- A fight with the host's player pulls the rest of the camp in within a second (the host alone: a second bandit on him within
  1.2 s in six runs of nine). A fight with the figure pulls them in one at a time, each when it would itself recognise him (the
  joiner alone: 3 to 8 s for the second).

The fix has two halves, both under `mp_w223_split`:

1. **One enemy on the figure is enough while the host has none (the hold).** An enemy whose eyes would now be freed for the
   figure, or that would be named for him by sight, is **not named** — its eyes stay held off him — while the host's own player
   stands within 12 m of it, upright, and the game says nothing is after him (`IsInCombatDanger`), for 5 s at most. Its own
   perception then has only the host's player to go by. A host who crouches is offered to nobody; an enemy that hears the joiner
   or has him at arm's length is his whatever the host does. [code]
2. **The camp's call.** The hold alone was not enough (below): with its eyes off the figure an enemy still has to recognise the
   host's player by the engine's own timer, and nothing of the fight with the figure tells it about him. So while an enemy fights
   the figure and nothing is after the host's own player: **one** enemy of the figure's that fights nobody yet, within 15 m of
   the host, that the engine says is alert (or that stands within 12 m with a clear line to him), is sent the game's own attack
   call at him — the inbox message its own behaviours use to start a fight. For a camp NPC (no enemy of the host's on sight) only
   where the camp's own rule would fight him at once: armed, on its private ground. If nothing has come of it after 3 s a second
   call goes to another enemy; no more in that fight. The call alone was not enough either: in ten runs with it, nine calls
   put the called enemy on the host within 5 s once — the message makes it join the camp's fight, and that fight's enemy is the
   figure (five took the joiner, three reached the host after 6–8 s). So the called enemy is also **pinned** to the host's own
   player with the game's own "fight this one" (the relation context `combat_forcedTarget`, which its battles and quest fights
   use) for 6 s, by the DLL, and for 8 s it is not named for the figure and its eyes stay held off him. After that the engine
   picks again. [code] [bench]

`WO223-SPLIT` in the host's log says which enemy was left to the host, which was called on him and why, and why the hold ended.

Measured — the two-player camp scene, ten runs a build, both players walking in side by side, the joiner armed; "an enemy on
him" = the first bandit whose target the engine sets on that player, timed from the first such event of the fight: [bench]

| build | each player an enemy within 5 s | the host's first enemy, seconds after the fight's start |
|---|---:|---|
| Part B alone (no split) | 5 of 10 | 0.0 · 17.1 · 9.9 · 5.8 · 4.8 · 6.4 · 0.0 · 1.7 · 0.0 · 7.0 |
| + the hold | 6 of 10 | 1.4 · 9.1 · 5.4 · 0.4 · 6.6 · 0.8 · 3.1 · 1.9 · 2.9 · 8.3 |
| + the call (no pin) | 8 of 10 | 0.0 · 0.2 · 8.1 · 0.0 · 3.1 · 1.7 · 0.0 · 1.2 · 0.0 · 6.0 |
| + the pin (**shipped**), first ten | **9 of 10** | 0.0 · 1.7 · 1.5 · 0.0 · 0.0 · none in 12 s · 0.0 · 0.2 · 1.2 · 2.7 |
| the final build, ten more | **10 of 10** | 2.9 · 4.5 · 2.5 · 4.8 · 4.5 · 4.4 · 0.0 · 1.2 · 1.7 · 0.0 |
| the last build (the gate's idle rate), five more | 4 of 5 | 1.8 · 5.2 · 2.7 · 0.0 · 0.0 |

- The call without the pin: nine calls, the called enemy took the host four times (after 2.0, 6.8, 6.8 and 8.1 s) and the joiner
  five times. With the pin: 19 calls over the two ten-run scenes, the called enemy took the host 18 times (half of them within
  2.5 s; three after 6 s or more) and the joiner once. The last build's five runs: six calls, five took the host, one the
  joiner.
- The one miss of the shipped build: the host had stopped a few metres outside the camp's private ground. The camp's own rule
  does not fight him there, so nobody was called; the one enemy left to him (6.7 m away) did not make him out in its 5 s, and
  every bandit took the joiner. A wider rule for the call (an armed host within 12 m of a camp NPC, on its ground or not) was
  written and taken out again unmeasured.
- **Bar (9 runs of 10): met** — 23 of 25 with the shipped mechanism (9 of 10, 10 of 10, and 4 of 5 on the last build, whose
  miss came 0.2 s late); four of the ten of ten had less than a second to spare. The joiner had an enemy on him within 5 s in every run of every build.

Twin (`mp_w223_split off`, the rest on), five runs on the final build: each player an enemy within 5 s in **3 of 5** (the host's
first enemy after 0.0, 0.0, 12.8, 6.2 and 0.4 s); nothing held, nobody called. [bench]

## 5. Part C — the copy fights whom its host NPC fights (`mp_w223_copy_target`)

### 5.1 What was wrong

With Part B the host's NPC fights the figure in a real skirmish, and on the joiner's screen its copy is engaged in **his** fight by
the mirror WO-209 built. Where the NPC went for the joiner from the start that already held: in the two-player camp every copy
whose host NPC fought him was in his fight and aimed at him in every sample. What did not hold: [bench]

1. **The swap.** A host NPC that first fought the host and then turned to the joiner. On the joiner's screen its copy has a brain
   of its own, and the host's figure — which fights it there — is the enemy that brain picks. The mirror asked for the joiner as
   its target at once (13 ms at the median), the engine's relation said the copy was not in his fight, and WO-210's repair (end its
   fight the game's way, engage it again) ran every 5 s without curing it: its brain picked the figure again each time. In the swap
   scene the copies were in his fight and aimed at him in 87.9 % of samples — wrong spells of 7, 18, 19 and over 100 s (twenty
   rounds of the repair on that one) — and 24 % of the host's waits for his verdict ran out.
2. **A copy still seated.** A camp bandit that sat at the fire when the fight began: its host NPC stood up and fought the joiner
   3 m from its seat, and the copy stayed seated for 17 s — the writer leaves a body an activity holds alone until its host NPC is
   4 m away. Every attack of the copy was refused by the engine ("busy"), and seven blows in a row were the host's guess.
3. **An attack asked for while the last one ran.** The engine installs it when the running action ends. The DLL's watch gave it
   0.25 s, then told the host "nothing landed", and the verdict the joiner's engine reached 0.1–0.2 s later answered no blow of the
   host's: all three host guesses of the two-bandit defence scene.
4. **Two blows waiting at once** (a chain of swings, a combo): each took the first answer that fitted its own window, so a later
   blow took the answer an earlier one still waited for; and a "nothing landed" told *before* a blow answered it. A third of the
   host guesses in the camp scenes were of this kind — and never applied on the joiner, whose game skips a host verdict on an
   attacker it judged itself a moment ago.
5. **Blows inside a grab.** The joiner's game runs no grab between its player and a copy (a known limit). The host waited its
   whole cap — 3 s — for a verdict that could not come, for every blow struck in a grab.
6. **The cap itself.** WO-222 capped the wait at 1.5 s because half its waits were never answered; a "missed" is only known when the
   copy's attack has ended, 1.5–2 s after the host's blow.
7. **Answers paired by time alone.** The host gives a waiting blow the next verdict of that attacker that comes. A blow his
   engine never judged — a combo's second strike where his copy's combo landed once, an attack his copy could not start in time
   because it was still busy with the last — then takes the verdict of the *next* swing, that swing's blow takes the one after,
   and so on down a chain of swings until one blow has none. This is what is left (5.3); pairing a blow with the verdict of its
   own swing was tried and taken out again (5.4).

8. **A bandit both players are on.** Found last, in WO-222's reference scene on the build that was to be final: the host
   struck a bandit while it fought the joiner. On the joiner's screen its copy answered the body that hit it — the host's figure —
   left his fight, and stayed out for over three minutes through 94 rounds of the repair. The forced target WO-187 gives a copy
   (the game's own "fight this one") is only set once the copy is in his fight by the engine's own relation, and this one never
   was. Of that scene's samples 85.8 % were right.

### 5.2 The fix

- **The copy's own eyes (the joiner's mod, C.1/C.2).** What a brain cannot perceive it cannot pick. While the host's NPC fights
  this player — his agent names those copies to his mod at once when the host's opponent stream changes, and once a second — the
  copy's own perception of every other player's figure is held off with the same "do not perceive this body" the host's gate
  uses, 1.5 s at a time. WO-210's repair then sticks, and it may come again after 2 s instead of 5. `WO223-EYES` in the joiner's
  log. The hold lapses at most 1.5 s after the host's NPC has turned away again. [code]
- **Pinned to him (the joiner's DLL).** A copy whose host NPC fights this player is pinned to the local player with the game's own
  forced target (`combat_forcedTarget`) for as long as that lasts — also while the engine's relation says it is not in his
  fight — set again four times a second (WO-187's own bookkeeping clears the same pair) and cleared when its host NPC turns
  away. `WO223-PIN`. With it a copy that fought the host's figure was back on the joiner within 0.1 s (three such moments in
  the last run of that scene; before: 7 to 193 s), and inside the scene the repair ran not once and once in two runs (before: 94
  times in one). [code] [bench]
- **No seat in a fight (the joiner's DLL).** A copy WO-132 holds in his fight that an activity still holds more than 0.75 m from
  its host NPC's place for 0.3 s: the hold ends, the stream takes the body, its own posture is released the game's way, and no
  activity holds it again while the fight lasts. `WO223-SEAT`. [code]
- **The attack's watch (the joiner's DLL)** waits 0.7 s for a queued attack to be installed before it says anything. [code]
- **The host's wait (the host's agent).** The oldest blow of an attacker still waiting takes an answer first; a "nothing landed"
  answers only a blow that landed before it was told, and only 0.3 s after it came (a verdict may be a frame behind it); a blow
  struck inside a grab is the host's at once, logged apart (`WO208-DEF grab`); while the joiner answers at least nine waits in ten
  the wait may run to 3 s. [code]
- **C.3, lock-on.** Nothing new was needed: WO-178's rule already answers yes for a copy that fights the partner, and with the
  fight a real skirmish on the host the game's own rule lets the host lock on to an enemy that fights the joiner's figure. [bench]

### 5.3 Measured

The field's link shape, the joiner armed. Three builds side by side: **the twin** (the final build with every switch of this
work order off: the copies as 0.65.2 and WO-222 engaged them), **Part B alone** (enemies notice the joiner, nothing of Part C
yet), and **shipped** (the final build). A sample is one frame of one copy whose host NPC fights the joiner. [bench]

**On the joiner's screen: is the copy in his fight and aimed at him?**

| scene | the twin | Part B alone | shipped |
|---|---:|---:|---:|
| the swap (one bandit, the players take turns, 4 min) | 99.1 % of 13,369 (one spell of 13 s) | 87.9 % of 10,683 (141 s in four spells: 7, 18, 19 and over 100 s) | **100.0 %** of 11,660 and of 8,723 (the last build) |
| two-player camp, one fight of 3 min | – | 100 % of 5,747 inside the fight | **99.4 %** of 7,137 (one spell of 4 s) |
| two-player camp, short fights | 100 % of 4,334 (three fights) | 97.5 % of 14,918 (ten fights; the rest is the bench's reset between runs) | 99.2 % of 13,291 (ten fights, the build before the pin; resets among the rest); the last build **100.0 %** of 6,665 (five fights) |
| the joiner defends against two bandits | 100 % of 3,254 | 100 % of 3,995 | **100.0 %** in four runs (11,650, 2,453, 2,788 and 2,755 samples) |
| WO-222's reference scene (the players strike one bandit in turns, the other stands by) | – | 96.5 % · 85.8 % (two earlier builds); the final build with Part C's switch off: **81.5 %** of 12,428 · 98.2 % of 21,805 | 97.9 % (before the pin) · **99.9 %** of 3,593 · **100.0 %** of 20,261, of 20,138 and of 9,133 |

**Bar (≥ 98 % of samples): met** — 99.8 % over the final build's scenes together (97,381 of 97,533), every scene at 99.2 % or
more. Read the three columns before the bar, though:

- **Part B is what broke the swap, and Part C is what mended it.** In 0.65.2 a bandit turns to the joiner only when he strikes
  it, and its copy follows (99.1 %). With Part B the fight with his figure is a real skirmish of the host's engine, the bandit
  turns by its own judgement and at other moments, and the copy's own brain, left as it was, picked the host's figure for
  minutes (87.9 %; 27 rounds of the old repair in that run). With Part C: 100.0 %, the repair not run once, the eyes' hold set
  10 times, 11 pins.
- **The reference scene** was the last to come right. A copy the host strikes while its host NPC fights the joiner took the
  host's figure for minutes (85.8 % on the build before the pin); with the pin such a spell lasts a tenth of a second (99.9 %).
  One more run of that scene is not in the table: a bench fault had left the joiner without his weapon (9.2), and in it four
  copies stood with no fight at all on his screen for about a minute each while their host NPCs had him as their target
  (84.8 %). Not explained; it did not come back with an armed joiner, in that scene or any other.
- A copy turned to the joiner 13 ms (median) after the change arrived, in every build; what Part C changes is that it stays.

**On the host: who judged each NPC blow on the joiner?** (the host's guesses of all NPC blows on him)

| scene | the twin | Part B alone | shipped | shipped: guesses applied on the joiner |
|---|---:|---:|---:|---:|
| the joiner defends against two bandits | 2 of 49 (4.1 %) | 3 of 54 (5.6 %) | 2 of 50, 2 of 18, 2 of 49 and 1 of 45 (four runs; 4.3 % together) | 1, 1, 2 and 0 |
| the swap | 13 of 131 (9.9 %) | 43 of 177 (24.3 %) | 11 of 218 (5.0 %), and 6 blows inside a grab; the last build 2 of 94 (2.1 %) | 9 with the grab blows; 1 |
| two-player camp, one fight of 3 min | – | 18 of 130 (13.8 %) | 4 of 151 (2.6 %), and 3 inside a grab (the build before the pin: 12 of 156) | 3 |
| two-player camp, short fights | 4 of 91 (4.4 %; three fights) | 25 of 315 (7.9 %; ten) | 10 of 285 (3.5 %; ten, the build before the pin); the last build 5 of 157 (3.2 %; five) | 8; 3 |

**Bar (≤ 1 % of NPC blows, in the defence scene and over a whole scenes run): missed.** The shipped build has fewer host
guesses than 0.65.2's way in the swap (5.0 against 9.9 %) and about as many elsewhere (2.6 to 4.3 against 4.1 to 4.4 %); the
defence scene had 0 of 48 on one build of the night, 1 of 48 on a later one and 1 of 45 on the final build's last run. What is left, from the
traces of every guess: the copy on his screen could not play the host NPC's swing in time. A fighter in a chain or a combo
starts its next attack while the last still runs; the engine refuses the copy's attack ("busy"), the mod asks again for half a
second, and in the 3-minute camp fight 76 of the attacks asked for never started (152 did). For such a blow no verdict of his
exists; the host waits its 1 to 3 s and guesses, and where his own game judged a blow of that attacker within the last 1.5 s
he does not take the guess (a third of them). Three approaches were tried at this bar and measured — the longer wait while he
answers nine in ten (first night), the copy's own state and the order of the answers (5.2), pairing by swing (5.4). The next
step is not in the wait but in the copy: playing the host's chain as a chain (one attack queued behind the running one, as
the game's own combos do) — a work order of its own.

**C.3, lock-on, both ways: works.** In the lock scene on the final build the host's lock-on went to an enemy that fought the
joiner's figure four times (and three times to one that fought him), by the game's own "opponent change" lines; the joiner's
went to copies 145 times, and his rule answered yes for a copy "in the partner's fight" 22 times and "in his own" 56 times. (The
scene's own pass rule waits 2.5 s for one line after each key press and reads "no lock" in a camp where everyone joins; the
count from the game's lines is the measure.)

**The twin of Part C** (`mp_w223_copy_target off`, the rest on, the final build) fails in the reference scene and not in the
swap. Reference scene, two runs: the copies were in his fight and aimed at him in **81.5 %** of 12,428 samples (254 s in eleven
spells aimed at the host's figure) and in 98.2 % of 21,805 (41 s in eight spells), with 6 of 93 and 21 of 213 blows the host's
guess; shipped, the same scene, four runs: 99.9, 100.0, 100.0 and 100.0 %, no wrong spell longer than half a second, and 3 of
100, 8 of 218, 10 of 204 and 11 of 182 guesses (3 to 6 %).
Swap scene, one run: 100.0 % of 9,666 samples, no spell, 10 of 135 blows the host's guess (7.4 %; no rule for grabs). The
wrong-player spells Part C repairs are intermittent — they need the copy's own brain to pick the host's figure at the moment
its host NPC turns, which the reference scene (a bandit both players strike in turns) provokes and the swap on that night did
not.

One more run of the reference scene, taken for Part D with `mp_w223_split off` alone, is recorded here because it is the worst
of all for Part C and is not explained: the copies were aimed at him in 99.8 % of 20,135 samples but in his fight in only
63.1 % (835 s in twelve spells in which the copy fought the host's figure while pinned to him), and 93 of 267 blows were the
host's guess. Nothing like it was seen with the split on (eight runs of that scene and every other scene).

### 5.4 Tried and taken out: pairing a blow with the verdict of its own swing

The idea: the joiner's verdict says which swing it judged, the host gives it to the blow of that swing whenever it came, never to
another swing's blow, and a swing his engine has already judged is judged (a combo's further strikes on the host get no guess).
Built four ways in one night and measured each time; none beat the plain rules of 5.2, so none ships: [bench]

| host guesses / NPC blows on the joiner | defence (two bandits) | the swap | two-player camp, ten fights |
|---|---:|---:|---:|
| the rules of 5.2 (shipped) | 0 of 48 | 3 of 114 (2.6 %) | 13 of 286 (4.5 %) |
| + swing named by the row's timing | – | – | 18 of 268 (6.7 %) |
| + a wait to the swing's end, later strikes of a swing | – | – | 6 of 262 (2.3 %) |
| + later strikes only of a combo | 3 of 42 (7.1 %) | 13 of 164 (7.9 %) | – |
| + swing named by the DLL's own attack window | 4 of 51 (7.8 %) | 5 of 100 (5.0 %) | – |

What went wrong, from the traces: named by the row's timing, the joiner names the wrong row when the next row arrives just
before the blow lands (both fit); named by the DLL's attack window, a stall on the link of half a second moves the host's
estimate of when that row was sent onto the next swing, and a window of the copy's that lingers is taken for the running attack;
and either way the blows that are left are those the copy could not play in time, for which no verdict exists. In one trace the
pairing charged the joiner for a combo blow he had blocked 3.5 s earlier. Pairing needs the swing's own id on the wire in both
directions (the row message does not carry one); that is a protocol change for another work order.

## 6. Part D — the host's combat mode (`mp_w223_combat_mode`)

**Measured first, as asked: does the host's engine, with Part B in, count an NPC that fights a joiner's figure as in combat
mode? Where the enemy came at him by itself, at most blows. In WO-222's reference scene, half the time — less than before
this work order.** [bench]

| the joiner's clean hits (the engine's verdict "hit") | on a body in combat mode | drew a reaction |
|---|---:|---:|
| **as shipped**, fights both players are in (the swap, the two-player camp) | 16 of 16 | 16 of 16 |
| **as shipped**, his own fight: he walked into the camp alone and fought back, the host 120 m off (three runs) | 9 of 12 | 9 of 12 |
| **as shipped**, WO-222's reference scene (two runs) | 14 of 27 | **14 of 27** |
| the reference scene, every switch of this work order off (WO-222's way), one run | 16 of 17 | 16 of 17 |
| the reference scene, Part B off (`mp_w223_perceive` and `mp_w223_split` off, the rest on), one run | 1 of 43 | 39 of 43 (38 through WO-222's offer) |
| the reference scene, WO-222's offer retired (`mp_w223_combat_mode off`), four runs on the builds before the final one | 29 of 50 | 29 of 50 |

Of the samples of NPCs whose opponent is a figure, the share in combat mode: 100 % in the 3-minute camp fight (4,259), 96 to
100 % in the defence scene, 89 % in the swap; in the reference scene 62 and 65 % as shipped. (The share counts every NPC that
has the figure as its opponent, also one that has not reached him yet — in the solo sessions it is 15 %; the blows are the
measure that matters.)

What the table says:

- **Where an enemy engaged the joiner by itself** — noticed him, drew, attacked — the engine counts it in combat mode at most
  of his blows and plays its own hit reaction: 25 of 28 over the shared fights and his solo fights. That is what this part
  hoped for, and it holds with the host 120 m away.
- **In the reference scene it does not.** That scene is staged: the bench places the players, ends the bandit's fight between
  its phases with the game's own stop call, and orders the bandit at the figure again. With Part B the bandit then "fights" the
  figure in a skirmish of its own (the game's log: "Skirmish event: HitTarget on (the bandit) (target (the figure))") while
  reading **out of combat mode** for stretches of a minute, in combat state 1 with no guard zone — and lands blows meanwhile.
  Ten of the joiner's clean hits in a row fell in one such stretch. Why the engine keeps it out of combat mode there was not
  found; the stretch followed a death of the joiner and the mod's stop of the fights on him (WO-166) by forty seconds.
- **WO-222's way has it better in that scene** (16 of 17; and 39 of 43 with Part B alone off, where the body is out of combat
  mode at nearly every blow but stands in guard and takes WO-222's offered reaction every time). So in this scene this work
  order costs the joiner's hits their flinch about every second time.

**WO-222's workaround: not retired.** It offers the reaction when the engine refuses and the body's opponent is the very
figure that struck it; it runs only after the engine has refused, so it cannot stack with a reaction of the engine's own.
The order asked for it to be retired if combat mode held and the reaction parity still held. Retiring it was built and
measured on every build before the final one (the table's last row) and is now the switch's off side
(`mp_w223_combat_mode off`: the engine's word alone). It is not the default, because parity in the reference scene does not
hold either way and the offer still helps a body that stands in guard (the row with Part B off; six offers taken in one run
with Part B on). On a body out of combat mode *and* out of guard it does nothing: of the 13 offers in the reference scene as
shipped none gave a reaction — the engine's CreateHitAction found none for the body ten times, its action director refused
three times. [bench]

**For the players, plainly.** A joiner's clean hit makes the enemy flinch in a fight both players are in (16 of 16 on the
bench) and mostly in a fight he has alone (9 of 12). It can fail for a stretch on an enemy whose fight was broken off and
taken up again — the bench's reference scene: 14 of 27; the nearest thing in play is an enemy that goes on fighting him
after he was beaten down and got up. The hit wounds and kills either way. Known Issues says so.

**What would make it so at every blow.** (1) Finding what keeps a body that attacks the figure out of combat mode after its
fight was stopped (the next thing to measure: the body's combat state through a stop and a new fight, with and without the
bench's own order); (2) failing that, the reaction made on the joiner's own machine, where the copy is in the local player's
fight (Part C: 99.8 % of the time): one message from the host that it could not react, and the reaction created there as
WO-178 already creates the joiner's own recoil. Either is a piece of work of its own.

## 7. Part E — what the old rules protect

Five scenes, each run on the final build, on the twin (every switch of this work order off) and on 0.65.2's own build, with
the same armed joiner. What is compared is everything the host's game says about the joiner's figure during the scene — every
fight event with the figure in it, every bark at him, the mod's crime lines — category by category. [bench]

| scene | shipped (five runs, the last on the shipped build) | the twin | 0.65.2 | named difference |
|---|---|---|---|---|
| 1. friendly fire, ten strikes each way, off then on | off: a village brawl in two runs (villagers attacked the figure, seven times in one), nothing in the other three. On: the two players' own fight, no villager | off: nothing. On: a villager attacked the figure three times | off: nothing. On: nothing (none of the host's ten strikes went out) | none — see below |
| 2. villagers, guards, a shop, a house, by day and by night | nothing: nobody drew on him, fled from him or fought him | nothing | nothing (one fight event with no target in the night house) | none |
| 3. a punch by the joiner, witnessed | the mod's crime path ran on both machines, the host's keepers and guard as before | the same | the same (a guard's stop: "the record stands") | none |
| 4. talking, trade, forced dialogue, listening in | the scoreboard's checks 8, 10 and 9 of 10 (three builds, the last the shipped one): the joiner's talk to its choices, the trade screen and the forced dialogue passed in every run. The listen-in (staged apart): the joiner was offered the host's conversation, joined it and got the first decision's options | the checks 10 of 10. The listen-in: offered, joined, the options (his listening ended a moment later for a village brawl, as the game's rule has it) | the checks 9 of 10 (talk, trade, forced dialogue passed). The listen-in: not staged — no villager would talk to the bench's host in two runs (28 tried) | none in what could be compared — see below |
| 5. the host's dog, a horse | nothing in any run | nothing | nothing | none |
| 5. wolves | not shown: the bench's spawned wolf hunted neither player in any build | – | – | not judged |

- **Friendly fire** is the one scene whose footprints differ from run to run, and they differ as much between two runs of the
  shipped build as between shipped and 0.65.2. The bench's strikes are key presses: in each run some are refused by the game
  (the host's: all ten in two runs, six in three), and whether a villager standing by joins in depends on whom a stray blow
  touches. Villagers attacking the figure were seen in two of five shipped runs (with friendly fire off) and once on the twin
  (with it on); it is the game's own answer to a blow that touched a bystander, and 0.65.2's own way of passing a fight with
  the host on to his partner's figure (the twin's log names it). 0.65.2's run had no brawl, with eight of twenty strikes out in
  the phase with friendly fire off. Nothing that only the shipped build shows — but the scene cannot prove the two equal
  either: a staged bystander hit, the same in every build, would.
- **The rule for friends** (`mp_w223_guard`) is what keeps scene 2 quiet now that enemies notice the joiner: an NPC that reads
  enemy to his figure by its faction but friend to the host's own player is left out of the gate and its own sight of him is
  held off. In these villages no such NPC was met (the log line `WO223-GUARD` did not appear); the rule is covered by the mod's
  own test only.
- **Talking.** The scoreboard's checks cover the joiner's own talk, the trade screen and the forced dialogue; they pass on
  every build (9.1 has the two checks that did not, neither of them a talk). The listen-in is not one of those checks and had
  to be staged apart, and the bench's host is a wanted man in its save: of fourteen villagers tried a run, at most one would
  talk to him — once in two runs as shipped, once in two on the twin, in neither of two on 0.65.2's build. Where a
  conversation started, the joiner's game logged the same three things on the shipped build and on the twin — the offer, his
  join, the first decision's options — and the bench's own pass rule for that scene (written for WO-207, counting a line the
  agent has not printed since WO-213) failed in every build. So: the listen-in works as shipped and as it did before this
  work order; against 0.65.2's own build it could not be compared on this bench.
- **Animals:** an animal that reads enemy to the host's own player reads enemy to the figure (the figure's faction is not in the
  animals' table). The bench could not show a hunt: its spawned wolf stood 22 m from both players in every build. The host's dog
  and a horse did nothing to him. No companion was staged. Wild wolves of the game's own came into the camp once, in a
  reference run of the final build: the gate named one for the joiner's figure when it would have seen a player in his place;
  half a minute later the bench had moved him 120 m off, and all three hunted the host (62 bites). Whom they take with both
  players near was not seen.

**Part E's bar (every scene identical to 0.65.2): no named difference found; the wolves and a companion are not shown, and
the listen-in could be compared with the twin only, not with 0.65.2's own build.**


## 8. Part F — the instruments

- **The host's log, per enemy** (`WO223-NOTICE`): whom it noticed first — the player in its field of view and nearest at the moment
  the engine says it is alert — how far he was, how fast he moved, whether he crouched, and where the other player was.
  `WO223-GATE` says when and why each enemy was named for a figure (seen, heard, close, caught on its ground), `WO223-CAMP` when a
  camp caught him, `WO223-SPLIT` when an enemy was left to the host or called on him (the DLL's `WO223-CALL` for its pin),
  `WO223-GUARD` when a friend of the host's was held off.
- **The joiner's log, per copy** (`WO223-COPY`): every moment its host NPC fights him and the copy here does not — whom it fights
  instead, whether it is in his fight — and when it is right again, with the seconds; `WO223-EYES` and `WO223-SEAT` for the two
  repairs; a running total every 30 s.
- **The bug report's briefing**: "enemies noticed the host / the joiner first" (the counts and each side's mean distance) and
  "copies fighting the wrong player: N s of the M s enemies fought this player". WO-222's line on the host's waits stays; a blow
  inside a grab is its own line in the host's log (`WO208-DEF grab`).
- `mp_w223_status` in the console lists the six switches and both machines' numbers.

**The frames** (the bench's two windows only, both screens at the same moment; kept in the WO's notes, not in the repository):

- *A bandit drawing on the joiner as he approaches alone.* The host's screen, from the bushes 15 m off: two bandits with
  weapons out closing on the joiner's figure, one of them barking at him. The joiner's screen: the same two, weapons raised
  at him. (Taken at the first alert of a walk in; by the time both pictures are written the bandits have drawn and come.)
- *A two-player camp with enemies on each player.* The host's screen: a bandit's club raised at him at arm's length, a
  second behind it. The joiner's screen: a hooded bandit with a polearm squared up to him, and behind it the host's figure
  with two more on it.
- *The joiner blocking a copy that is aimed at him.* The joiner's screen: a bandit's mace raised over him, his guard up, the
  lock on it. The host's screen at that moment: his figure with three bandits around it.

The joiner's pictures are grey and red-edged where he had been hit: the game's own low-health view.

## 9. The scoreboard, crashes and incidents, and the bars

### 9.1 The scoreboard beside 0.65.2's control

The scoreboard's three scenes (the smithy, the village walk, the tavern; 14 minutes), the field's link shape, both games at
low settings: as shipped, on the twin (the same build, every switch of this work order off) and on 0.65.2's own build, through
one night and day. Two runs of the shipped kind were ended by the bench's low-memory guard (9.2) and are not in the
table. [bench]

| run | build | tavern guests | the host in a village brawl (checks) | place | smithy | village | tavern | pulls | flips | lane99 | cost95 |
|---|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|
| fs6 | shipped | 14 | 32/223 | **74.4** | 74.7 | 83.3 | 61.5 | 0.09 | 0.015 | 800 | 9.8 |
| fs7 | shipped | 3 | 41/203 | **82.0** | 79.3 | 78.0 | 87.8 | 0.12 | 0.005 | 782 | 10.1 |
| fs9 | shipped | 12 | 66/204 | **79.3** | 78.7 | 88.4 | 65.1 | 0.13 | 0.005 | 826 | 10.2 |
| fs1 | before the scans' idle rate | 9 | 139/223 | **70.7** | 79.3 | 76.9 | 53.6 | 0.20 | 0.047 | 762 | 10.8 |
| fs2 | before the scans' idle rate | 16 | 24/206 | **69.6** | 71.0 | 73.8 | 60.4 | 0.11 | 0.023 | 738 | 9.8 |
| fs3 | before the scans' idle rate | 0 | 56/227 | **86.9** | 86.5 | 87.2 | – | 0.08 | 0.000 | 811 | 6.6 |
| fs5 | before the scans' idle rate | 11 | 0/208 | **70.0** | 83.7 | 74.8 | 49.1 | 0.21 | 0.030 | 705 | 10.4 |
| ts1 | the twin | 11 | 39/225 | **76.5** | 79.5 | 80.7 | 68.9 | 0.18 | 0.053 | 690 | 8.3 |
| ts2 | the twin | 8 | 5/231 | **83.6** | 80.7 | 96.5 | 63.9 | 0.06 | 0.015 | 767 | 9.3 |
| cs1 | 0.65.2 | 1 | 12/205 | **83.5** | 82.5 | 80.6 | 92.2 | 0.16 | 0.024 | 722 | 9.6 |
| cs2 | 0.65.2 | 0 | 122/204 | **86.7** | 90.2 | 84.1 | 99.0 | 0.25 | 0.040 | 943 | 9.8 |
| cs3 | 0.65.2 | 11 | 55/206 | **72.7** | 86.3 | 69.0 | 65.0 | 0.14 | 0.004 | 875 | 9.0 |

**Place follows the tavern first.** The scene's tavern is full or empty by the game's own schedule (0 to 16 guests at the same
game hour), and a full tavern's bench sitters are also in the village walk's window. Like for like:

| the tavern | shipped | before the scans' idle rate | the twin | 0.65.2 |
|---|---|---|---|---|
| few guests (0 to 3) | 82.0 | 86.9 | – | 83.5, 86.7 |
| many (8 to 16) | 74.4, 79.3 | 70.7, 69.6, 70.0 | 76.5, 83.6 | 72.7; 77.2, 74.3, 74.8 in WO-222's finals |

**A regression this judge found, and what was done about it.** On the build that was to be final, the three runs with a full
tavern read 69.6 to 70.7 — under every such run of 0.65.2 (four, over two mornings) and of the twin (two). With few guests
there was no difference. Nothing of this work order acts in a village with no enemy in it, and in the lowest of those runs
not one of its mechanisms did (no enemy named, no eyes held, no copy pinned, no seat released, on either machine). What still
ran was its looking: the host's gate asked the engine for every entity within 45 m of the figure on each beat, and the notice
log asked twice more — six such queries a second in the middle of a full tavern, each with two relationship reads per NPC.
The last build shares one query between the gate and the log and halves the rate when no enemy has been in reach for three
seconds: one query a second for a pair that walks together. Its runs are the "shipped" rows.

What can and cannot be said: with a full tavern the shipped build read 74.4, 79.3 — inside 0.65.2's range and above all
three runs before the change. That is 2 runs, and the tavern's lottery is wide; the frame-cost counter of the mod does not show the
change (it does not time the Lua the agent calls). So: the loss was real, its likely cause is removed, and the proof that it
is gone is as thin as the count of full-tavern runs after it. Sitter by sitter (the same NPC in a run of each kind) the
copies sat off their host NPCs at the same seats in every build — 15 in both, 9 only before the change, 6 only in 0.65.2, of
39 seen in both — so the seats themselves are not this work order's.

**Pulls and flips** are inside each other's spread (shipped 0.09 to 0.13 and 0.005 to 0.015; 0.65.2 0.14 to 0.25 and 0.004 to 0.040).
**Step, lanes and cost** likewise.

**Works at all** (the ten checks, their own runs): shipped 9 of 10 (the mount check was not staged — the host's rider did not mount — as in 0.65.2's run); the two builds before it 10 and 8 of 10; 0.65.2's own build 9 of 10; the twin 10 of 10. The one failed check in all of these: in the 8-of-10 run one of two knocked-down bandits did not get up on the copy within the check's time — the same miss 0.65.2 had in two of its own runs in WO-222's finals.

**Verdict.** Pulls, flips, lanes, cost and the works checks: no regression outside the spread. Place: level with few guests;
with a full tavern a loss of about five points was found on the build before the last and its likely cause removed; the
last build's full-tavern reading is back in 0.65.2's range.

### 9.2 Crashes and incidents

**No game crashed** in this work order's runs (three nights and three days of bench sessions). Games that closed, and why: [bench]

- **Five times, not a crash: the bench's low-memory guard quit both games** (free memory under 500 MB for a minute on the
  32 GB bench PC with two games on it; the fifth time after the scene had ended, so that run counts). Each time one game —
  the host's four times, the joiner's once — stood at 14 to 17.5 GB within the first seven minutes of the session, while the
  joiner joined and the bench reloaded the host's save, where most sessions stay near 10 GB. A game that large that early
  is **not new and not this work order's**: the host's was in 13 of this work order's 123 bench sessions (2 of the 8 on
  0.65.2's own build), in 18 of WO-221's 75 and in 2 of WO-222's 33; the machine runs out only when the rest of it is full
  too. Two of the stops took a scoreboard run of the shipped kind (9.1); both were run again. Each game's own log ends with
  its orderly quit; the bundles are kept. A host whose game takes far more memory than usual after loading a save with a
  joiner in the session should restart the game and send the bug report.
- Four early sessions ended with a game gone and no crash report: a joiner's rejoin after the host's reload failed once ("join
  aborted": he went back to his own world; an existing weakness of the rejoin, not of this work order), and three were the
  bench's own stops. Each game's log ends with its orderly shutdown.
- **A bench fault that cost three runs:** the bench joiner's stored Henry for the host's world is the newest snapshot his agent
  holds. One guard scene whose staged crime found no villager left a snapshot without his weapon, and every session after it
  brought an unarmed joiner (the camp then waits out its half minute instead of fighting him, and his blows are fists). The runs
  between that snapshot and its discovery are not used for anything about the joiner's fighting; every stage after it ran with a
  store of its own. The rig's join also failed once outright (the first-join answer was not taken); that scene was run again.

### 9.3 The bars

| part | bar | result |
|---|---|---|
| A | the table, the host's, the joiner's and both, four paces, five runs each | measured (host 5, joiner 6 to 18, both 5 to 7 a pace); section 0 |
| B | the joiner noticed, warned, drawn on and attacked within 15 % of the host's distance and 2 s of his time, every pace | **time met** at a walk and a sprint (within 0.7 s up to the draw; the attack −0.3 s at a walk, +2.3 s at a sprint) and for the attack when crouched by day; **distance missed** (+21 to +54 % at a walk, −27 to −62 % at a sprint); **missed** for a camp's warnings (not played to him) and at night (noticed in two runs of six) |
| B | both at the camp: an enemy on each within 5 s in 9 runs of 10 | **met**: 9 of 10, 10 of 10, and 4 of 5 on the last build |
| C | the host's guesses ≤ 1 % of NPC blows on the joiner | **missed**: 4 % (defence), 5 % (swap), 2.6 to 3.5 % (camp), 3 to 6 % (the reference scene) |
| C | every copy whose host NPC fights him in his fight and aimed at him in ≥ 98 % of samples | **met**: 99.8 % over the scenes |
| C | a change of target on the host shows on the copy in the frame it arrives; lock-on both ways | the copy follows 15 ms (median) after the change arrives, 32 ms at the 90th percentile — one to two frames, not the same frame; lock-on works both ways |
| D | measure the host's combat mode; if it holds, retire WO-222's workaround and show the reactions still come | it holds where the enemy engaged him by itself (the joiner's clean hits on a body in combat mode, and reacting: 16 of 16 in shared fights, 9 of 12 in his solo fights); it does not in the staged reference scene (14 of 27, against 16 of 17 with this work order's switches off): **parity missed there**; the workaround is kept, its retirement is the switch's off side (section 6) |
| E | five regression scenes identical to 0.65.2 | no named difference; wolves and a companion not shown; the friendly-fire scene cannot prove equality; the listen-in compared with the twin only (the bench could not start a conversation on 0.65.2's build) |
| F | the bug report says whom each enemy noticed first and whom it fights on both screens; two briefing lines | in |
| scoreboard | not worse than 0.65.2's control outside the spread | pulls, flips and the works checks: inside the spread. Place: level with few tavern guests; with a full tavern a loss of about five points was found on the build before the last, its cause removed, and the shipped build reads 74.4 and 79.3 against 0.65.2's 72.7 (9.1) |

**What was tried where a bar was missed.**

- *The distance (Part B).* Three rules for when a pair is named, each measured over the table: WO-195B's reach factor on his
  visibility (too far by day, far too far at night); the engine's own meter with its term for a visibility above zero (0.8 to
  1.3 s early on the host's own player); the meter without that term, stepped along his path with a look-ahead (shipped: on the
  second, off by the two walkers' pace). A fourth would be to hold the joiner to the host's pace on the bench, which measures the
  bench.
- *The camp's warnings.* The routine is the player's own script in the game's files (its talk, its marks, its crime record); it
  was read, not ported. The outcome is kept (the half minute, the attack at once on an armed man); the warnings need the
  routine's barks played for a figure, which is a piece of work of its own.
- *Night.* The joiner's own game reports him darker than the host's game reports the host on the same path (−1.46 against
  −1.21). Tried: his value as it is (shipped), and a bench run with it shifted by the difference, which brings him to the
  host's distance — but nothing says which reading is the right one, so the shift is not in the mod.
- *The host's guesses (Part C).* The longer wait while he answers nine waits in ten; the copy's own state (eyes, seat, the
  attack's watch, the pin) and the order of the answers; pairing a blow with the verdict of its own swing, four ways (5.4).
  The guesses fell from 8 to 24 % with Part B alone to 3 to 5 %, which is also below 0.65.2's own way in the swap. What is left
  is the copy's replay of chains and combos.
- *The joiner's flinches in the reference scene (Part D).* Retiring WO-222's offer (29 of 50 clean hits reacted); keeping it
  (14 of 27: a body out of combat mode and out of guard refuses the offer); and measuring the same blows in fights nobody
  staged (25 of 28), which says the loss sits in what the scene does to the bandit — its fight stopped and ordered again —
  and was not found. Section 6 names the next two steps.



## 10. Thread and lock audit (every engine call WO-223 adds)

| call | where | thread and point in the frame | under our fault guard? |
|---|---|---|---|
| `SetRelationContext` / `HasRelationContext` for `PerceptionPriorityBoost` (the script-context manager's slots, as WO-136's forced target uses them) | DLL, pipe ops 212 / 213 and the tick's clean-up | the main thread, after the engine's own module update (the mod's per-frame task list) — where the mod has set relation contexts since WO-136 | the existing guarded calls of `script_context.cpp`, unchanged; the manager takes no lock (WO-68: a re-entrancy flag only) |
| entity lookups by name, souls by entity | DLL, op 213 | main thread | existing helpers |
| a read of the game's recognition parameters (the static parameter object of the RPG module, found by its own code's reference) | DLL, the tick, until read once | main thread; memory reads only, no engine call | the read guard around the copies (no engine call inside it) |
| `System.GetEntitiesInSphere`, `soul:GetRelationship`, `soul:GetDerivedStat`, `soul:IsInCombatMode` / `IsInCombatDanger`, `actor:IsDead`, `human:IsWeaponDrawn`, `entity:GetWorldPos` / `GetDirectionVector` | host mod (Lua), `KCD2MP_W223Tick`, twice a second | the main thread (a console command of the agent's batch, where every Lua of the mod runs) | no (Lua's own `pcall`) |
| the line of sight (WO-139's ray from the NPC's eyes to his chest) | host mod (Lua), per hostile NPC with the figure inside its reach and field of view, twice a second | main thread, a script call | no |
| `XGenAIModule.GetMyWUID`, `XGenAIModule.IgnorePerception(perceptor, perceptible, a length in whole milliseconds, game time off, interruptible)` — the game's own "do not perceive this body for a while", as its behaviours call it | host mod (Lua): planned twice a second by `KCD2MP_W223Tick`, asked per hostile NPC whose eyes are not yet due on every tick of the mod's 20 ms timer (`KCD2MP_InterpTick`, a `Script.SetTimer` callback) | main thread both ways: a console command, and the script system's own timer callback — where the game's own scripts make this call | no |
| `XGenAIModule.IsPointInAreaWithLabel` (is the figure on private ground: WO-157's read) | host mod (Lua), once a beat while a camp NPC is near | main thread | no |
| `System.SetCVar` for `WH_AI_LOD_MaxDetailDistance` / `WH_AI_LOD_MaxCountDetail` | host mod (Lua) | main thread, the console's own way | no |
| `XGenAIModule.ProduceSoundWUID` (the engine's movement sound, from the figure) | host mod (Lua), at most twice a second | main thread, a script call as the game's own scripts make it | no |
| reads of the opponent model and the skirmish relation for the two counters (D, F) | DLL, inside WO-132's and WO-209's existing ticks | main thread; the reads those ticks already made | existing |
| `XGenAIModule.GetMyWUID`, `XGenAIModule.IgnorePerception` from a copy to another player's figure (C.2) | joiner mod (Lua): asked when the agent names the copies that fight this player (once a second, and at once when that set changes) and on the mod's 20 ms timer as each 1.5 s hold ends | the main thread (the agent's console batch; a `Script.SetTimer` callback) — the same call at the same points as the host's gate | no |
| `player:GetWorldPos`, `player.soul:IsInCombatDanger` (B.3, is the host's own player beside this enemy with nothing after him) | host mod (Lua), inside `KCD2MP_W223Tick`, per enemy about to be named | main thread, script calls | no |
| the end of an activity's hold on a copy in the local player's fight, and the request to release its own posture (C) | DLL, the NPC writer's per-frame tick (`npc_drive`), in the branch WO-209's drift rule already runs | main thread; no engine call: the writer's own hold set, and a line in the queue the agent reads (the release itself is the mod's existing Lua call, WO-160's) | none added |
| the host's wait for the joiner's verdict (C: which waiting blow takes an answer) | host agent | the agent's own tasks; no engine call | — |
| `SetRelationContext` for `combat_forcedTarget` from a copy to the local player (C, the pin; the same helper and context as WO-187's pin), asked for by the mirror's sample, set and cleared by this module's own tick | joiner's DLL | the main thread, the mod's per-frame task list (not inside the mirror's guarded tick) | no |
| `SetRelationContext` / `HasRelationContext` for `combat_forcedTarget` from the called enemy to the host's own player (B.3; WO-136's lever and helper), set on pipe op 215 and cleared by the tick 6 s later (only a pin this DLL set, only while both souls are the ones it was set on) | DLL | the main thread, after the engine's own module update (the pipe's op queue and the mod's per-frame task list, as op 213) | no (the helper's own read-back only) |
| `XGenAIModule.MakeTableFromType` / `SendMessageToEntityData` for `crime:attackInitiatedByConcept` at the host's own player (B.3, the camp's call — the message and helper WO-139's pursuit already sends), with `KCD2MP_W223Alert`'s reads (`soul:GetDerivedStat`, `IsInCombatMode`, `IsInCombatDanger`), the line of sight and `player.human:IsWeaponDrawn` | host mod (Lua), inside `KCD2MP_W223Tick`, at most twice in a fight | main thread, script calls; the message goes into the NPC's own inbox and its brain reads it on its own update | no |

No engine movement or AI call is made off the main thread; no call that may take an engine lock was put under the fault guard.
