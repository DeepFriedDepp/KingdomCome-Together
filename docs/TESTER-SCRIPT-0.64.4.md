# Kingdom Come: Together 0.64.4 — tester script (ten scenes, about 30 minutes)

**The mark key is the period key (`.`). Press it the moment something looks wrong.** It writes a mark into both players' bug reports
at that moment — a body frozen or in the bind pose (arms straight out), someone sliding, floating or walking on the spot, a person
sitting on one screen and standing on the other, an item that should not be where it is. At the **start of each scene**, open the
console (`~`) and type `mark scene1` (then `scene2`, …): the bug reports line up the two machines by these marks.

Two players, one host and one joiner, on the **same 0.64.4 build**, over the internet with the launcher's default Steam route (the
host's join code). Steam must be running before either game starts. Do not alt-tab away for long (a game behind another window runs
slower). This build covers the work of 0.64.2, 0.64.3 and 0.64.4: the people around you, what they do, and the things they leave.

## Scene 1 — the village walk (3 minutes) · `mark scene1`
- By day, in a busy village. The **joiner follows villagers who walk**, 10 to 30 m behind them, one after another. The host stays nearby.
- **Joiner, watch:** do they walk the way they walk on the host's screen — feet on the ground, turning where they turn, stopping where
  they stop? Or do they glide, walk on the spot, or jump forward? Press `.` and tell the host which one.

## Scene 2 — the blacksmith's house (3 minutes) · `mark scene2`
- Find a smith at the anvil (or any workshop), and people eating at a table. If it is night, a house with people asleep.
- **Both, watch:** is each one doing the **same thing** on both screens — hammering, eating, sleeping? Or does the joiner see someone
  standing (or in the bind pose) where the host sees that person work, eat or sleep? Press `.` and say what each of you sees.

## Scene 3 — the tavern (3 minutes) · `mark scene3`
- In the evening, a tavern with guests at the tables. The **joiner stands 5 to 10 m from the seated guests** and watches them.
- **Joiner, watch:** does a seated guest **jerk, snap or jump** between their seat and another spot, or slide along the bench? Does
  anyone sit on your screen who stands or walks on the host's? Press `.` each time.

## Scene 4 — a talk and a trade as the joiner (3 minutes) · `mark scene4`
- The **joiner** talks to a villager and plays the conversation to its end; then talks to a shopkeeper, picks **Trade**, and **sells
  one item**; then talks to the nearest quest giver.
- **Joiner, watch:** does each one start at once (within about two seconds of pressing the key), and **do the choices come** (in 0.64.3
  the joiner's talks hung after the first line)? Does the trade screen open and the sale go through? Press `.` if a talk does not
  start, hangs, or the trade screen never comes.
- The **host** then does the same three, and the joiner watches the host's talks.

## Scene 5 — a fight to the death, by each player (4 minutes) · `mark scene5`
- Find bandits (or a willing enemy). The **host kills one**, then the **joiner kills one**.
- **Both, watch:** does each death show on both screens — the body falls and stays on the ground? Anyone knocked down who gets up on
  one screen while still lying on the other? Press `.` when it happens.

## Scene 6 — looting one body together (3 minutes) · `mark scene6`
- Both players **open the same dead body's loot** at the same time and **take items one by one**, taking turns — and once, **both click
  the same item at the same moment**.
- **Both, watch:** when the other takes an item, does it disappear from **your open screen** (without closing and reopening)? When you
  both click the same item, does exactly **one** of you get it (the other sees "Already taken")? Did any item end up with **both** of you,
  or with **neither**? Is anything you took marked **stolen** on one screen but not the other? Press `.` at anything odd.

## Scene 7 — a road encounter (3 minutes) · `mark scene7`
- Ride or walk a road out of town until you meet an encounter: a robbery, a dead traveller, a broken cart, a camp.
- **Joiner, watch:** is everything the host sees there on your screen too — the cart, the dead body, the people searching it? Is
  anything floating, or missing? Press `.` and say what you see. (The bench could not make the game start a road encounter on demand:
  this scene is the only test of what an encounter leaves behind.)

## Scene 8 — a caravan rides past (2 minutes) · `mark scene8`
- Find a caravan on a road (carts, horses, people riding on the carts) and let it pass you at about 20 m.
- **Joiner, watch:** are the carts **on the road**, or flying? Are the people on the carts **sitting on them**, or hovering beside them,
  before and after they pass? Is the **horse in front of the cart** (it may not be yet)? Press `.` when something floats or is missing.

## Scene 9 — a walk apart (2 minutes) · `mark scene9`
- The joiner walks away from the host until the **leash warning** appears, then walks back.
- **Joiner, watch:** do the people around you behave normally on the way out and back? Any frozen or empty bodies near you?

## Scene 10 — the other movement, five minutes (5 minutes) · `mark scene10a`, then `mark scene10b` at the end
- **Both players type `mp_route on`** in the console and press `.` (the mark at its start). Walk the village for five minutes,
  the joiner following walkers as in scene 1.
- Then press `.` again (the mark at its end) and **both type `mp_route off`**. Say which looked better — this movement or scene 1's.

## Then
- Both of you: send the bug report from the launcher **right away**. Its first section, **Link**, now ends with one line for each of
  this build's promises — among them: how activities were placed on the joiner's copies, the loot ledger (clean, or the item that
  went wrong), stolen marks sent, corpses and props sent, and carts.

## The switches (only if asked)
Every part of 0.64.4's work can be turned off live from the console, one at a time, to compare: `mp_w217_status` lists them
(`mp_dialog_twins`, `mp_seat_writer`, `mp_placement`, `mp_ghost_far`, `mp_kd_follow`, `mp_long_names`, `mp_layer_watch`,
`mp_loot_live`, `mp_item_flags`, `mp_encounter_world`, `mp_carts`, `mp_route_skip`).
