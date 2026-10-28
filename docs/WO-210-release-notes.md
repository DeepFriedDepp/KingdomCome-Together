# Kingdom Come: Together 0.63.9 — playable from the join on

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

The installer, `KingdomComeTogether-Setup-0.63.9.exe`, comes from the maintainer. What to try together is in
`docs/WO-210-tester-script.md`; the evidence is `docs/WO-210-findings.md`.

**Both machines and the relay must run the same build.** 0.63.9 refuses every other version at the handshake.

---

## Fixed

- **People lying in their beds after a shared sleep** while they walked around on the host's screen ("floating sleepers"). A person who
  gets up on the host's screen now gets up on the joiner's too, and nobody is left lying or sitting where the host's screen has them
  walking. `mp_copy_state_always` (on).
- **People in a T-pose.** Every person of the host's world on the joiner's screen now always shows what that person does on the host's:
  when the mod lets one go, switches it or stands it up, it gets the host's current state at once, or its own idle -- never nothing.
- **Fights stuck in a grab** ("refusal loops"): a bandit next to the joiner tried to grab him every frame and froze. The game's own grab
  test now says no for the joiner and a host bandit, so it never starts; the fight goes on. `mp_clinch_guard` (on).
- **Bodies the joiner killed stood up, walked on, or could not be searched.** A killing blow of the joiner's is now a normal death with
  the game's own death animation, and the body can be searched by both players. `mp_death_engine` (on).
- **Knockdowns:** a bandit knocked down on the host's screen falls on the joiner's too and gets up with it. `mp_knockdown_sync` (on).
- **Enemies idling on the joiner's screen** while they fought on the host's: they draw and take their stance with the host's.
  `mp_copy_ready` (on).
- **The host could not move or vote as the listener** in a joined conversation (stuck on the top answer): the mouse wheel and the
  mouse button now move and vote, as in your own talks. The listener also sees a ring on the answer the talker's cursor is on, and the
  talker's pick for a second before the line plays. `mp_listen_wheel`, `mp_conv_hover`, `mp_conv_pick_hold`, `mp_conv_mark_over_icon` (on).
- **The joiner's game falling 8-12 seconds behind** right after joining. `mp_profile_snapshot` (on).
- **A rider running through the air** where the joiner's game cannot put him on his horse: he walks on the ground instead.
  `mp_rider_sync` (on).
- **After a shared wait the joiner's clock could stay hours ahead for half a minute** (a different sky on each screen).
- **A copy asking its game for two things at once** (log errors, people not starting their work). `mp_state_one_request` (on).
- **The joiner's blows judged twice:** a blow of the joiner's on a bandit used to be judged by the joiner's game too (its own damage,
  flinch and stagger on his screen, then put back to the host's). Now only the host's game judges it; the joiner still sees his swing
  land and hears the impact, and the bandit's reaction is the host's. `mp_copy_hit_local_none` (on).
- **The host putting a bandit's health back up** after a joiner's blow when the two games disagreed on the damage: the host's game
  now decides alone.
- **Bandits standing still on the joiner's screen while they swung at him on the host's:** a bandit that the joiner's own game had
  turned to the host showed none of its swings at the joiner. It now shows the host's swing; the host's game judges the blow.

## Changed

- Every new behaviour has its own console switch and ships **on**. **Every switch is safe to flip during play, both ways**
  (`mp_<name> off`, then `mp_<name> on`), in a tavern or in a fight.
- The joiner's blows on the host's people always go through the host's game now (its own damage, block and death rules); the joiner's
  own numbers for them are never applied (`mp_blow_joiner_values` is gone).

## Known

- **A grab between the joiner and a host bandit does not start at all** in this build (the grab played on both screens is not in yet).
- **Carts in a caravan** do not move on the joiner's screen; their drivers stand beside them.
- **The host waiting alone** still climbs in memory (our bench: 30-80 MB a second) until the partner is in: not fixed in this build.
  Have the partner join within a couple of minutes of the host loading, and tell us how high Task Manager showed it. We narrowed it
  down (it is the game building mesh data over and over while a session waits, not the mod's own memory) but did not find the trigger.
- **A timed answer in a joined conversation** has not been reached on our bench; tell us if you meet one.
- **A bandit can turn to the host on the joiner's screen** while it fights the joiner on the host's. Its swings still show, but the
  host's game judges them, so a block or a dodge against it counts a moment later.
- **Some bandit blows come with no swing on the joiner's screen** (a blow from a grab-like move, or with no attack running on the
  host's side); the host's game judges them.
- **The listener's new controls in a joined conversation** (the wheel, the mouse button, the cursor ring and the pick) were not confirmed
  on our bench; tell us how they work for you.

## How to report

Press the **period key** the moment something looks wrong, then type `mark <what you saw>` in the console (~): the note reaches the
other player's bug report too. After the session both players send their bug reports.
