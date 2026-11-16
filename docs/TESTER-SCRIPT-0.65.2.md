# Kingdom Come: Together 0.65.2 — tester script (eight scenes, about 30 minutes)

**The mark key is the period key (`.`). Press it the moment something looks wrong.** It writes a mark into both players' bug reports
at that moment — a person sliding or walking on the spot, someone jumping between two places, a dead body standing, a horse that
comes to the wrong player, a game that freezes or closes. At the **start of each scene**, open the console (`~`) and type
`mark scene1` (then `scene2`, …): the bug reports line up the two machines by these marks.

Two players, one host and one joiner, on the **same 0.65.2 build** (the launcher's About shows the build id), over the internet with
the launcher's default Steam route (the host's join code). Steam must be running before either game starts. Do not alt-tab away for
long (a game behind another window runs slower).

This build is about four things: **people who walk on their own feet** on the joiner's screen, **the fight crash**, **the joiner's
trade**, and **a horse for each player**. The scenes follow that order.

## Scene 1 — the village walk (5 minutes) · `mark scene1`
- By day, both players in a village street. The joiner follows villagers who walk, 5 to 15 m behind them, and looks at their feet.
- **Joiner, watch:** do they walk like the host's people — feet that land and stay, no sliding, no walking on the spot, no step taken
  twice as slowly as the body moves? Does anyone get pulled back a step, or jump several metres? Press `.` and say who.
- **Host, watch** the same people: do the two of you see each one in the same place?

## Scene 2 — the tavern (3 minutes) · `mark scene2`
- In the evening, both players inside a tavern full of guests, the joiner 5 to 10 m from the seated ones.
- **Joiner, watch:** does a seated guest jerk or jump between the seat and another spot? A guest who gets up and leaves should stand
  up and walk, not slide away seated. Press `.` each time.

## Scene 3 — a fight, walking away, coming back (5 minutes) · `mark scene3`
- Find bandits (or start a fight) and fight them together **to the death: each of you kills at least one**. Then **both players
  walk 300 m away, wait half a minute, and come back** to the same spot. Do it twice.
- **Both, watch:** does either game freeze or close — during the fight, when you come back, or in the minute after? This is the crash
  0.65.2 was made to fix. If a game closes, start it again and **send the bug report at once** (the crash report goes with it).
- **Joiner, also:** do the dead lie where the host's dead lie? Is any dead body standing up?

## Scene 4 — a talk and a sale as the joiner (4 minutes) · `mark scene4`
- The **joiner** talks to a shopkeeper, picks **Trade**, **sells one item** and closes the deal. Then the same with a second
  shopkeeper, then a plain talk with a villager to its end.
- **Joiner, watch:** does each talk start within a few seconds, do the choices come, does the shop screen open, does the sale go
  through (your purse goes up)? Does any talk end by itself while you are still choosing? Press `.` and say which shopkeeper.

## Scene 5 — a horse each (7 minutes) · `mark scene5`
- Both players go to a horse trader (Semine's groom, for one). The **joiner buys a horse**. Then the **host buys another one**.
- **Both, watch:** did the joiner pay once? Is the joiner's horse the joiner's on both screens, and the host's the host's?
  (While the joiner's horse still stands in the trader's yard, the trader offers it to the host too. **Do not buy it**: you would pay
  and get nothing — see Known issues.)
- Both players walk 50 m from their horses and 50 m from each other and **whistle** (the call key), one after the other.
- **Both, watch:** does **each whistle bring only that player's own horse**, on its own legs?
- Both mount and **ride side by side for 500 m**. **Both, watch:** is the partner on his horse on your screen, moving with it, the
  whole way? Does a horse lag, slide or stand still under its rider?
- The joiner opens his horse's inventory (the inventory screen's horse side), **puts two items in**; the host opens the same horse's
  inventory and **takes one**. **Both, watch:** does the item leave one list and arrive in the other exactly once?
- The **host saves and loads that save**; the joiner comes back in by himself. Then the **joiner quits the game, starts it again and
  joins**. **Joiner, watch:** is the horse still yours each time, where you left it?

## Scene 6 — the dead on the road and a caravan (3 minutes) · `mark scene6`
- Walk or ride a road out of town to a place with dead bodies (a robbery's scene, a battlefield), the joiner arriving a little after
  the host. Then wait for a caravan or walk to one.
- **Joiner, watch:** do the dead **lie** (none standing, none in the bind pose with arms straight out), with blood where the host's
  have it? On the carts: do the riders **sit**, do the **wheels turn**, are the horses in front of the cart? Press `.` and say what
  is different from the host's screen.

## Scene 7 — a drop out of a horse's bags (2 minutes) · `mark scene7`
- Each player in turn opens his own horse's inventory and **drops one item** out of it; the other picks it up.
- **Both, watch:** does the item lie on the ground for both of you, and does whoever picks it up have it — once?

## Scene 8 — the rest of a normal session (the time left)
- Play on as you would. Press `.` at anything odd; say it out loud so the other can look.

## Then
- Both of you: send the bug report from the launcher **right away**. Its first section, **The queue**, says in one line whether the
  partner's world reached your game on time; the briefing now counts only this launch of the game.

## The switches (only if asked)
Every part of 0.65.2's own work can be turned off live from the console, one at a time, to compare: `mp_w221_status` lists them
(`mp_combat_action_end`, `mp_talk_hold`, `mp_talk_force`, `mp_shop_keeper`, `mp_drive`, `mp_holders`, `mp_own_horse`,
`mp_saddlebags`, `mp_corpse_look`, `mp_horse_drop`, `mp_cart_seat`, `mp_cart_wheels`, `mp_route_quiet`, `mp_ragdoll_once`). The earlier builds'
switches: `mp_w218_status`, `mp_w217_status`, `mp_w216_status`.
