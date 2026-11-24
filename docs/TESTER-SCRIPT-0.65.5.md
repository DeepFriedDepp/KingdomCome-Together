# Kingdom Come: Together 0.65.5 — tester script (draft: WO-222's and WO-223's scenes; WO-224 completes it)

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

## Scenes E to I — enemies notice the joiner (WO-223)

These scenes are about one thing: **an enemy must notice the joiner the way it notices the host, and fight him on both screens.**
Every mechanism here has a switch (`mp_w223_status` in the console lists them); leave them all on. The host's bug report now says,
for every enemy, whom it noticed first and from how far — so say out loud who walked in first.

## Scene E — the joiner walks into a camp alone (5 minutes) · `mark sceneE`
- A bandit camp by day. **The host stays well back (100 m or more) and does not come nearer.** The joiner walks straight into the
  camp **with his weapon drawn**.
- **Joiner, watch the bandits:** the first one that makes you out must shout, draw and attack — about where it would in
  single-player (some ten metres at a walk, a little further at a run). A camp that **ignores you until you stand among them** is
  the bug: press `.` and say "ignored". Bandits that come at you from far outside the camp (20 m and more) are the other bug: say
  "too early".
- **Then the same at another camp with the weapon away.** In single-player a camp warns an unarmed visitor a few times ("get out")
  and attacks after half a minute, or at once if he walks up to one of them. **The joiner does not hear those warnings yet** (they
  are the player's own script): for him the camp stays quiet and attacks after about half a minute on its ground, or when he walks
  up to a bandit. Say what you saw and after how long.
- **Joiner, fight back, still alone, and watch the bandit you hit:** on our bench three of the joiner's twelve clean hits drew no
  flinch from an enemy he fought without the host (the hit still wounds; Known Issues has it). Tell us how it looked: does a
  bandit stagger when your blow lands clean, or take it without moving — and if it does not, had you just been beaten down and
  got up? Then let the host walk up and join the fight: from then on they should flinch at your blows as they do at his.
- **Host, afterwards walk in yourself** the same way at another camp (the joiner stays back). Say whether the bandits noticed the
  two of you at about the same distance.

## Scene F — sneaking in at night (5 minutes) · `mark sceneF`
- The same at night, the joiner **crouched**, no torch. The host stays back.
- **Joiner:** crouched in the dark you should get close — a few metres — before anyone notices, as in single-player. Bandits that
  spot you from far away while you sneak in the dark are a bug (press `.`, say "seen in the dark"); so are bandits that never notice
  you even when you stand up beside them (say "ignored"). A camp that has made you out while you crouch does not attack at once:
  as for the host, it does after half a minute, or when you come right up to one of them.

## Scene G — both players at a camp (5 minutes) · `mark sceneG`
- Both players walk into a camp of four or more together and fight for three minutes, a few metres apart.
- **Both, watch who fights whom:** each of you should have at least one bandit on him within a few seconds. **Joiner:** the bandits
  that the host sees attacking you must attack *you* on your screen too — not run at the host's figure and swing at the air beside
  him. A bandit that fights the wrong player on your screen: press `.` and say "wrong player".
- **Joiner, also:** blows you block or dodge must not cost health (Scene C's check, now with more enemies on you).

## Scene H — the swap (4 minutes) · `mark sceneH`
- One bandit fights the host. The host steps back and stops fighting; the joiner steps in and strikes it until it turns on him. Then
  the other way round. Three swaps.
- **Joiner, watch the bandit at each swap:** when it turns to you on the host's screen it must turn to you on yours without a
  noticeable wait (and turn away again when it goes back to the host). Press `.` and say "late swap" if it keeps attacking the
  other player's figure for more than a moment.
- **Both, try the lock-on** on a bandit that is fighting the *other* player: it must lock.

## Scene I — peace (5 minutes) · `mark sceneI`
- A village by day, then by night. The joiner walks past villagers and guards, into a shop, into a house; the host stays 50 m away.
- **Nobody must attack the joiner or run from him** just for being there (a house at night is trespassing for him exactly as for
  the host). A guard or villager that draws on the joiner for no reason: press `.` and say "village".
- Talk, trade and listen in on the other player's conversation as before: all of it must work as in 0.65.2.
- If you meet wolves or wild dogs: they should go for the joiner as they go for the host. Say what they did.
