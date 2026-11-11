# Kingdom Come: Together 0.65.0 — tester script (nine scenes, about 30 minutes)

**The mark key is the period key (`.`). Press it the moment something looks wrong.** It writes a mark into both players' bug reports
at that moment — a person late, frozen or in the bind pose (arms straight out), someone sliding or walking on the spot, a person
sitting on one screen and standing on the other, a door open for one of you and shut for the other, an item that should not be where
it is. At the **start of each scene**, open the console (`~`) and type `mark scene1` (then `scene2`, …): the bug reports line up the
two machines by these marks.

Two players, one host and one joiner, on the **same 0.65.0 build** (the launcher's About shows the build id), over the internet with
the launcher's default Steam route (the host's join code). Steam must be running before either game starts. Do not alt-tab away for
long (a game behind another window runs slower). This build carries the work since 0.63.9: the people around you, what they do and
wear, fights, loot, carts, doors — and the joiner's link to the host's game, which no longer falls behind in a crowd.

## Scene 1 — a packed tavern, then a brawl (6 minutes) · `mark scene1`
- In the evening, both players inside a tavern full of guests, for three minutes. The joiner stands 5 to 10 m from the seated guests.
- Then find bandits (or start a brawl) and fight them together for a minute or two.
- **Joiner, watch:** does what you see keep up with the host's game — a guest who stands up on the host's screen stands up on yours
  within a moment; in the fight, an enemy who falls on the host's screen falls on yours at the same time? **Say it out loud if the joiner
  sees things seconds late**, and press `.` — this is the one thing this build was made to fix.
- **Joiner, also:** does a seated guest jerk, snap or jump between their seat and another spot? Press `.` each time.

## Scene 2 — the blacksmith's house (3 minutes) · `mark scene2`
- By day, a smith at the anvil (or any workshop), people eating at a table; at night, a house with people asleep.
- **Both, watch:** is each one doing the **same thing** on both screens — hammering, eating, sleeping? Does someone on the joiner's
  screen stop what they do for a moment of their own (a stretch, a look around) while the host's goes on working? Press `.` and say what
  each of you sees.

## Scene 3 — a talk and a sale as the joiner, then as the host (4 minutes) · `mark scene3`
- The **joiner** talks to a villager to the end of the conversation; then talks to a shopkeeper, picks **Trade**, and **sells one item**.
- **Joiner, watch:** does each talk start within about two seconds, do the choices come, does the trade screen open and the sale go
  through? Press `.` if a talk does not start, hangs, or the trade screen never comes.
- The **host** then does the same, and the joiner watches.

## Scene 4 — looting one body together (3 minutes) · `mark scene4`
- Both players **open the same dead body's loot** at the same time and **take items one by one**, taking turns — and once, **both click
  the same item at the same moment**.
- **Both, watch:** when the other takes an item, does it leave **your open screen** at once (without closing and reopening)? When you
  both click the same item, does exactly **one** of you get it (the other sees "Already taken")? Is anything you took marked **stolen**
  on one screen but not the other? Press `.` at anything odd.

## Scene 5 — doors (3 minutes) · `mark scene5`
- In a village by day, each player opens and closes a few house doors; then both watch the doors along a street for two minutes while
  villagers come and go.
- **Both, watch:** is every door **open for both or shut for both**, and **locked for both or for neither**? A door a villager opens
  on the host's screen should open on the joiner's too. Press `.` at a door that differs, and say which.

## Scene 6 — a road encounter and a caravan (4 minutes) · `mark scene6`
- Ride or walk a road out of town until you meet an encounter (a robbery, a dead traveller, a broken cart, a camp), or a caravan.
- **Joiner, watch:** is everything the host sees there on your screen too — the cart, the dead, the people searching them; the carts on
  the road with their riders seated, the horse in front of each cart? Press `.` and say what floats or is missing.

## Scene 7 — a fight to the death, by each player (3 minutes) · `mark scene7`
- The **host kills one** enemy, then the **joiner kills one**.
- **Both, watch:** does each death show on both screens — the body falls and stays down? Anyone knocked down who gets up on one screen
  while still lying on the other? Press `.` when it happens.

## Scene 8 — listening in on the partner's talk (2 minutes) · `mark scene8`
- The **joiner** starts a conversation with a villager; the **host** joins it as a listener (the prompt on the host's screen), votes
  once, and leaves with Back. Then the other way round.
- **Both, watch:** do the listener's keys work for the whole talk (the vote, Back)? Press `.` the moment they stop working.

## Scene 9 — the other movement, five minutes (5 minutes) · `mark scene9a`, then `mark scene9b` at the end
- **Both players type `mp_route on`** in the console and press `.` (the mark at its start). Walk the village for five minutes, the
  joiner following villagers who walk, 10 to 30 m behind them.
- Then press `.` again (the mark at its end) and **both type `mp_route off`**. Say which looked better — this movement or the rest of
  the session's. (It ships off: it moves people the game's own way, but crashed the joiner's game once on our bench.)

## Then
- Both of you: send the bug report from the launcher **right away**. Its first section, **The queue**, says in one line whether the
  partner's world reached your game on time (and how late at worst); the **Link** section ends with one line for each of this build's
  promises.

## The switches (only if asked)
Every part of 0.65.0's own work can be turned off live from the console, one at a time, to compare: `mp_w218_status` lists them
(`mp_pipe_batch`, `mp_pipe_shed`, `mp_takeover_stop`, `mp_door_sweep`, `mp_listen_reassert`). The earlier builds' switches:
`mp_w217_status`, `mp_w216_status`.
