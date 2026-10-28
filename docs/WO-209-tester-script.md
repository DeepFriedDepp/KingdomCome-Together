# WO-209 tester script — the host's people on the joiner's screen, enemies that fight the joiner, a clinch, a forced follow-up, a shared sleep (0.63.8)

Two players, one host and one joiner, both on 0.63.8, in the same world. If anything below looks wrong, press the **period key** at
that moment. To say what you saw, open the console (~) and type `mark <what you saw>`: the note reaches the other player's bug report
too. Send both bug reports after the session.

## 0. Hosting
- **Host:** while you wait for your partner to join, keep Task Manager open on the game's memory. **Joiner:** join within a couple of
  minutes. On our bench a host waiting alone climbs about 80 MB a second (0.63.7 did the same) and stops climbing once the partner is in.
- **Expected:** the host's memory levels off once the joiner is in. Tell us how high it went and how long the host waited.

## 1. The same tavern, the animation owner off and on
- Both stand in a busy tavern for two minutes (the owner is on in this build). **Joiner:** type `mark owner on`, watch two minutes.
  Then **both** type `mp_copy_anim_owner off` (the joiner also `mark owner off`), watch two more, and **both** type
  `mp_copy_anim_owner on` again.
- **Expected:** with it on, the people sit where they sit on the host's screen, the innkeeper serves, nobody stands in a T-pose.
  Say which looked closer to the host's screen.

## 2. A tavern at night
- After dark, a few minutes among the drinkers, the innkeeper, the dice table.
- **Expected (joiner):** drinkers seated on their benches, sleepers lying, the innkeeper's serving plays; nobody stuck or in a T-pose.

## 3. A village walk
- Walk together through a village by day for a few minutes, past people going about their work.
- **Expected (joiner):** people walk from a standstill with their own start (no gliding), stop where they stop on the host's screen.

## 4. A fight, one bandit and then two, the joiner defending
- Both fight bandits; let one, then two, go for the joiner. **Joiner:** hold block, time blocks and perfect blocks, parry, dodge,
  lock on.
- **Expected (joiner):** the bandits that attack you on the host's screen attack you on yours, and keep at you until the fight ends;
  their swings are their own attacks at you. Lock-on takes the bandit in front of you that swings.
- **Expected (host):** your partner's figure raises its guard when they block.
- In this build the joiner's own game judges his blocks, perfect blocks, parries and dodges (about nine blows in ten on our bench;
  the rest the host's). **Then both type `mp_defence_local off`** and fight again: the host's game judges every blow.
  Say which felt right, and type `mp_defence_local on` again after.
- **Mark** a blow that hit you while blocking, a bandit fighting someone else on your screen than on the host's, a lock-on that took
  the wrong bandit, a bandit that stood still while it fought you on the host's screen.

## 5. A clinch
- **Joiner:** in a sword fight, cross blades with a bandit until it clinches.
- **Expected:** both of you in the clinch on both screens; its moves play on both. (Not seen on our bench: please mark what happens.)

## 6. A duel loser kneeling, a dog lying down
- A fistfight or duel to a surrender; a dog that lies down.
- **Expected (both):** the loser kneels on both screens; the dog lies on both.

## 7. A joined conversation with a forced follow-up
- **Joiner:** talk to someone whose talk leads straight into one the quest starts by itself; the host joins and listens.
- **Expected:** the follow-up runs once, on the joiner's machine; the host is taken in to listen. It must not start on the host's
  machine as well.

## 8. A shared sleep
- Sleep together (both agree) for several hours.
- **Expected (joiner):** your sleep runs its full length and ends with the host's; the clocks agree afterwards.

## Known in this build
- Lock-on takes the enemy in front of the joiner; one that attacks from the side is not taken until the joiner turns to it.
- About one blow in ten on the joiner is still judged by the host's game (the last swing of a combo, the first of a fight).
- With the animation owner on, walkers can slide more than on the host's screen. Mark it, and say whether `mp_copy_anim_owner off`
  looks better there.
- A place a quest waits for you to reach counts only the host's character.
- A timed choice in a joined conversation has not been tried on our bench; mark one if you meet it.
