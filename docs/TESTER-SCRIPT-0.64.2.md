# Kingdom Come: Together 0.64.2 — tester script (five scenes, about 20 minutes)

**The mark key is the period key (`.`).** It writes `mark_odd` into both players' bug reports at that moment. Press it **the moment
something looks wrong** — a body in the bind pose (arms straight out), frozen in a pose, walking on the spot, moonwalking, sliding,
floating, standing where the other player sees it sitting or working. At the **start of each scene**, open the console (`~`) and type
`mark scene1` (then `scene2`, …): the bug reports line up the two machines by these marks.

Two players, one host and one joiner, on the **same 0.64.2 build**, over the internet with the launcher's default Steam route (the
host's join code). Steam must be running before either game starts. Do not alt-tab away for long (a game behind another window runs slower).

## Scene 1 — the village walk (5 minutes) · `mark scene1`
- By day, in a busy village. The **joiner follows villagers who walk**, 10 to 30 m behind them, one after another. The host stays nearby.
- **Joiner, watch:** do they walk the way villagers walk on the host's screen — feet stepping with the ground, turning where they turn,
  stopping where they stop? Or do they glide, walk on the spot, moonwalk, or jump forward?
- **Host, watch** the same villagers: the joiner says when one looks wrong; press `.` together.
- **Then the same walk with the old movement, to compare:** the joiner opens the console and types `mp_route off`, then `mark scene1b`,
  and follows villagers for 2 more minutes; then `mp_route on` again. Say which of the two looked better (the bench could not decide it
  for real machines).

## Scene 2 — the blacksmith's house (4 minutes) · `mark scene2`
- Find a smith at the anvil (or any workshop: a carpenter, a cook, someone sweeping or washing), and people eating at a table.
  If it is night, a house with people asleep in their beds.
- **Both, watch:** is each one doing the **same thing** on both screens — hammering, eating, sleeping? Or does the joiner see someone
  just standing (or in the bind pose) where the host sees that body work or sleep? Press `.` and say what each of you sees.

## Scene 3 — the tavern brawl (5 minutes) · `mark scene3`
- In the evening, a tavern: start a brawl (or wait for one), **both players in it**, until the last body is down.
- **Both, watch:** does anyone freeze in a fist-up or weapon-up pose after the fight, or stand empty? Does the joiner end up stuck "in
  combat" with nobody to fight (unable to sheathe, sleep or save)? Press `.` when it happens.

## Scene 4 — the road encounter (3 minutes) · `mark scene4`
- Ride or walk a road out of town until you meet travellers, bandits or a robbery (a bandit robbing a villager, a Cuman patrol).
- **Joiner, watch:** is there anyone on your screen the host cannot see (ask him)? Anyone standing still and empty on the road?
  Do the encounter's people look the same, dressed the same, on both screens? (Say so if not: this build does not send an NPC's
  clothes yet.)

## Scene 5 — the ride-past (3 minutes) · `mark scene5`
- The **host mounts a horse** and rides past the joiner twice, close (about 5 m), at a walk and at a gallop. Then both stop beside a
  parked cart or wagon if one is near.
- **Joiner, watch:** is the host on the horse both times, at the right height? Any rider or horse floating, or a rider walking under a
  horse? Is the cart where the host sees it?

## Then
- Both of you: send the bug report from the launcher **right away**. Its first section, **Link**, now ends with one line for each of
  this build's promises: how the copies walked (the game's own walk, the corrections), empty bodies over the whole session, activities a
  copy could not show (named), and the dormant and ghost bodies kept out.

## The switches (only if asked)
Every part of 0.64.2's work can be turned off live from the console, one at a time, to compare: `mp_w215_status` lists them
(`mp_route`, `mp_route_gait`, `mp_ladder`, `mp_letgo_cull`, `mp_activity_line`, `mp_dormant`, `mp_ghost_fight`, `mp_probe_all`), and `mp_route_correct_m <metres>` sets how far a walking copy may be from the host's before it is pulled back.
