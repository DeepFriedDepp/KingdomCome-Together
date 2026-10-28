# WO-210 tester script — playable from the join on: fights, clinches, knockdowns, deaths, a shared night, a joined talk (0.63.9)

Two players, one host and one joiner, both on 0.63.9, in the same world. If anything below looks wrong, press the **period key** at
that moment. To say what you saw, open the console (~) and type `mark <what you saw>`: the note reaches the other player's bug report
too. Send both bug reports after the session.

**Every switch named below is safe to flip during play, both ways** (`mp_<name> off`, then `mp_<name> on`): nothing should break when
you flip one, in a tavern or in the middle of a fight.

## 1. The join, then a fight at sword's length, two bandits on the joiner
- **Host:** while you wait for your partner, watch the game's memory in Task Manager (see "Known in this build").
- **Joiner:** join, walk with the host to a bandit camp. Let two bandits go for you; keep them at sword's length.
- **Expected (joiner):** the bandits draw and take their stance the moment they do on the host's screen; nobody stands idle in the
  fight; every swing the host's screen shows at you comes at you on yours. You block, perfect-block, parry and dodge as in your own
  game.
- **Joiner, hit back:** your blows on a bandit are judged by the host's game only. You still see your swing and hear and see it land;
  the bandit's flinch, block or parry is the one the host's screen shows, a moment later, and its health is the host's.
- **Mark** a bandit standing still while it fights you, a swing on the host's screen that never came on yours, a blow that hit you
  while you blocked, a blow of yours that landed with no sound, or a bandit that flinched on your screen but not on the host's.

## 2. A clinch, both ways
- **Joiner:** let a bandit get close with your guard up, blades crossed, the way a bandit grabs you in your own game. Then press in
  close on a bandit yourself, blades crossed (the game starts a grab by itself at that range; there is no grab key).
- **Expected:** in this build a clinch between the joiner and a host bandit does **not** start (the clinch between you and the host's
  people is the host's, and that is not in this build); the fight goes on normally, with no frozen bandit and no bandit in a T-pose.
- **Mark** any frozen or T-posed bandit after a grab attempt.

## 3. Knock a bandit down
- Drain a bandit's stamina (block and hit it until it staggers and falls).
- **Expected:** it falls on both screens at the same moment and gets up on both when it gets up on the host's screen; it never stays
  lying on the joiner's screen after it stood up on the host's.

## 4. The joiner kills a bandit; both loot it
- **Joiner:** kill a bandit. **Both:** walk to the body and look for the "Search" prompt; the host takes something, then the joiner.
- **Expected:** the bandit dies with the game's own death (it falls as any dead body does), stays dead on both screens, and both of
  you can search it.
- **Mark** a body that stands, walks on, or offers no "Search".

## 5. A shared sleep in a village, then the morning
- Go to a village at night. Sleep together (both agree to the sleep).
- **Expected:** the sleep ends at the same time on both screens and the clocks agree afterwards (the sky too). In the morning nobody
  lies in a bed while their real self walks around on the host's screen, and nobody floats.
- **Mark** anybody lying or sitting where the host's screen has them walking, and the moment the two skies look different.

## 6. A tavern at night
- A few minutes among the drinkers, the sleepers, the innkeeper.
- **Expected (joiner):** drinkers seated, sleepers in their beds, the innkeeper serving; nobody in a T-pose.

## 7. A joined conversation, each player once as the starter
- **Joiner** starts a talk with someone; the **host** joins and listens. Then the other way round.
- **Voting, in plain words:** whoever is listening sees the same answers as the one talking. The listener chooses an answer the
  usual way -- the arrow keys or the **mouse wheel** to move, **Enter or the left mouse button** to vote. The vote shows next to that
  answer on the talker's screen. Move and vote again to change it; the game's back key takes the vote back (pressed again with no
  vote, it leaves the talk). The talker still makes the choice. (The old F-key vote is gone; nothing needs a special key.)
- **Expected (listener):** you see a ring on the answer the talker's cursor is on, and the talker's pick shows on its answer for a
  second before the line is spoken. If a timed answer comes up, the timer shows on both screens and runs out normally.
- **Mark** a vote that did not show, a pick you never saw, a cursor ring that lagged.

## 8. A caravan or a rider on the road
- If a rider or a caravan passes, follow it for a minute.
- **Expected (joiner):** riders on their horses; where your game cannot put the rider on his horse, he walks on the ground beside where
  the horse is on the host's screen -- never running through the air. Merchants' carts are not moved on the joiner's screen in this
  build.

## 9. Flip one switch mid-session, then back
- In a tavern and again in a fight: **both** type `mp_copy_anim_owner off`, wait half a minute, then `mp_copy_anim_owner on`.
- **Expected:** nothing breaks either way: no T-pose, nobody stuck lying or sitting, the fight goes on.

## 10. Mark anything odd
- The period key and `mark <what you saw>`, as above, for anything else.

## Known in this build
- **The host's memory while waiting alone** for a partner still climbs (our bench: 30-80 MB a second) until the partner is in -- not
  fixed in this build. Join within a couple of minutes of the host loading; the host: tell us how high Task Manager showed the game
  and how long you waited, and which graphics card you have.
- **A clinch between the joiner and a host bandit does not start** (step 2): the host-driven clinch is not in this build.
- **A bandit can turn to the host on the joiner's screen** while it fights the joiner on the host's: its swings at the joiner still show,
  but the host's game judges them, so a block or a dodge against it counts a moment later. Mark it when you see it.
- **Carts** in a caravan are not moved on the joiner's screen; their drivers stand on the ground beside them.
