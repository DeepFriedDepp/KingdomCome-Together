# WO-204 tester script — sleep and wait, fights, taverns, conversations, joins (0.62.7)

Two players, one host and one joiner, both on 0.62.7, in the same world. If anything below looks wrong, press the **period key** at
that moment. To say what you saw, open the console (~) and type `mark <what you saw>`: the note now reaches the other player's bug
report too. Send both bug reports after the session.

## 1. Sleep and wait, five times each
- **Host:** sleep in a bed, then use the Wait key, alternating, five times each. **Joiner:** say yes each time.
- Do it once while a quest has someone walking with you (an escort) and once far from any quest.
- **Expected:** no crash, before, during or after any of them. An escort that only one of you keeps up with does not fail.

## 2. A bandit fight with the joiner blocking and parrying
- Both fight the same bandits. **Joiner:** block, parry and (if you can) perfect-block their blows; attack too.
- **Expected:** the bandits never stand in a T-pose and never stop swinging; their swords come out when the fight starts (not 5-10
  seconds later); a blow of yours the bandit blocks does not take its health on the host's screen.
- **Mark** any bandit that stands still, floats, or takes a hit that "lands late".

## 3. A tavern at night
- Both stand in a busy tavern after dark for a few minutes: drinkers seated, the innkeeper serving, a dice table.
- **Expected (joiner):** no one slides while seated, no one sits inside a table, the innkeeper's serving and the drinking play.

## 4. A duel loser kneeling, a dog lying down
- Win a duel (or watch one), and find a village dog that lies down.
- **Expected:** the same pose on both screens. (Known: this build does not yet sync poses -- mark what you see.)

## 5. A conversation joined, with a vote both ways
- **Host:** talk to someone with choices (Barbora). **Joiner:** walk up, press talk on **Join conversation**.
- **Expected (joiner):** the host's camera, the lines as subtitles in your own language, the voices from the right people, and the
  host's choices on your screen in the same order. Move with your Up/Down keys, confirm to vote: a small icon marks your vote, on
  your screen and on the host's; confirming another row moves it; Back clears it (a second Back leaves). The host decides; you see
  "Your pick" or "Their pick".
- Then swap: the joiner talks, the host joins and votes.

## 6. A trialog (Semine and Gnarly)
- **Expected:** the listener sees the same speakers gesture as on the starter's screen. (Known: in a trialog only the starter's own
  Henry's gestures are carried in this build.)

## 7. A forced conversation (one that starts by itself when you walk somewhere)
- **Expected:** the partner within 30 m is joined automatically. (Known: not changed in this build -- mark what happens.)

## 8. A join with the world paused
- **Joiner:** leave and join again three times. **Host:** watch your clock and the NPCs.
- **Expected:** the host's world stays paused for the joiner's whole load, until the joiner is in the world; nothing moves, the clock
  does not run. If the joiner's game stops answering for a minute, the join gives up and the host's world runs again.

## Known in this build
- The joiner's copies' bodies still have several writers (Act II's single owner is not built): T-poses are now logged and the body's
  idle is put back within a second, seated copies are no longer moved by the position stream, and copies stuck in a work pose are
  released -- but swings, weapon draws and poses are not yet streamed as states.
- The timer of a timed choice is not shown on the listener's screen.
