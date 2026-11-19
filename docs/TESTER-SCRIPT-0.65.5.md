# Kingdom Come: Together 0.65.5 — tester script (draft: WO-222's scenes; WO-224 completes it)

**The mark key is the period key (`.`). Press it the moment something looks wrong.** It writes a mark into both players' bug reports
at that moment. At the **start of each scene**, open the console (`~`) and type `mark scene1` (then `scene2`, …): the bug reports line
up the two machines by these marks.

Two players, one host and one joiner, on the **same 0.65.5 build** (the launcher's About shows the build id), over the internet with
the launcher's default Steam route. Steam must be running before either game starts. Do not alt-tab away for long.

The fight scenes below are about four things: **enemies that flinch at the other player's blows**, **the host's reactions showing
on the joiner's screen**, **the joiner's own blocks and dodges counting**, and **an enemy that runs away staying an enemy**. Every
mechanism here has a switch (`mp_w222_status` in the console lists them); leave them all on.

## Scene A — the duel (5 minutes) · `mark sceneA`
- One bandit. The host and the joiner take turns: the host lands about ten blows on it while the joiner watches from 5 m, then the
  joiner lands about ten while the host watches.
- **Both, watch the bandit when the other player strikes it:** does it flinch (a stagger, a step back) when a blow lands clean, as it
  does at your own blows? A bandit that blocks or parries a blow does not flinch — that is the game. A bandit that **just stands there
  and takes clean hits** from the other player is the bug: press `.` each time.

## Scene B — the camp (5 minutes) · `mark sceneB`
- Both players fight a bandit camp (three or more) together for a few minutes, side by side.
- **Joiner, watch** the bandits the host strikes: do they flinch on your screen when the host's blow lands? **Host, watch** the
  bandits the joiner strikes. Press `.` and say "no flinch" when one takes a clean hit without moving.
- **Joiner, also:** when you swing at a bandit right in front of you, does your blade connect? A swing that passes through a bandit
  that stands in reach is a bug: press `.` and say "passed through".

## Scene C — the joiner defends (4 minutes) · `mark sceneC`
- The joiner faces two bandits alone (the host stays 10 m away and does not fight). The joiner blocks, dodges, and now and then
  lets a blow through.
- **Joiner, watch your health:** a blow you blocked or dodged on your screen must not cost you health (a block costs some stamina).
  If a blocked or dodged blow takes health — or kills you — press `.` at once and say "blocked but hit".

## Scene D — the chase (5 minutes) · `mark sceneD`
- Fight one bandit until it is badly hurt; it may beg for mercy or run. **When it runs, the joiner chases it and kills it.** Then
  the same with the host chasing.
- **Joiner, watch the runner:** does it run with its legs (not slide in a standing pose)? Do your blows on it count — does it
  stagger, bleed, and finally die on both screens? A runner your blade passes through, or one that cannot be killed, is the bug:
  press `.` and say "runner".
- **Both, also:** does every dead bandit lie on the ground? A dead body standing up: press `.`.
