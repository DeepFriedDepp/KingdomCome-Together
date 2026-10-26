# WO-208 tester script — fights with blocking, a tavern, postures, joined conversations, a forced trialog, a join (0.63.7)

Two players, one host and one joiner, both on 0.63.7, in the same world. If anything below looks wrong, press the **period key** at
that moment. To say what you saw, open the console (~) and type `mark <what you saw>`: the note reaches the other player's bug report
too. Send both bug reports after the session.

## 1. A bandit fight, the joiner defending
- Both fight the same bandits. **Joiner:** hold block, time a few blocks and perfect blocks, dodge, and lock on to one bandit.
- **Expected (joiner):** damage arrives when the blow lands, not a moment later. A blow that lands on you while you block is judged
  by your own game (blocked, perfect block, guard broken). Most bandit blows are still judged by the host's game (known, below).
- **Expected (host):** your partner's figure raises its guard when they block. A bandit whose stab your partner blocked recoils.
- **Mark** any blow that hit you while you were blocking, one that hit on one screen and missed on the other, and say whether
  lock-on took a bandit that fights you.
- **Expected (both):** no crash. A crash of either game in a fight is the first thing to report (the bug report has it).

## 2. A tavern at night
- Both stand in a busy tavern after dark for a few minutes: drinkers seated, the innkeeper serving, a dice table.
- **Expected:** nobody stands in a T-pose and no queue of actions jams a body. **Known:** seated people may stand on the
  joiner's screen, and the innkeeper's serving may not play there (below).

## 3. Postures
- Find a villager asleep in bed at night, someone sitting on a bench.
- **Known in this build:** the joiner sees them standing beside the bed or the bench. Mark it once, with where it was; no need to
  mark every one.

## 4. A joined conversation with votes, both ways
- **Host:** talk to someone with choices (the innkeeper, the bailiff). **Joiner:** walk up and join.
- **Expected (joiner):** the host's view of the talk (or, where the game has no dialogue camera for it, the host's own view), the
  lines as subtitles, the choices; vote with Up/Down and confirm. The host decides. Nobody is dropped from a talk for silence.
- Then swap: the joiner talks, the host joins and votes.
- **Expected:** while you are in a conversation of your own, your partner's does not take you in.

## 5. A forced trialog
- Walk into a place where a conversation starts by itself (a quest's, or a character who walks up to you), with your partner
  within 30 m and not in a conversation or a fight.
- **Expected:** it runs on the machine of the player it started for; the partner is taken in to listen without pressing anything
  ("Listening in"); Back leaves it.

## 6. A join
- **Joiner:** leave and join again twice.
- **Expected:** the host's world holds still until the joiner is in it; the joiner arrives in the host's world.

## Known in this build
- On the joiner's screen the host's enemies aim their own fighting at the host's character: few of their blows land on the
  joiner's own character, so most blocks are still judged by the host's game, and lock-on rarely takes an enemy that fights the
  joiner. Report what you see anyway: the blows that do land are judged by the joiner's game.
- Sleeping and sitting people can stand on the joiner's screen, and work animations (serving) often do not play there.
- A place a quest waits for you to reach counts only the host's character.
- When the joiner's conversation leads straight into one the quest starts by itself, the host can get the same follow-up at the
  same moment; each player then has it on their own machine.
- After a long shared sleep the joiner's sleep screen can run on for up to a minute after the host's; the clocks meet afterwards.
