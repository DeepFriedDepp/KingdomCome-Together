# Kingdom Come: Together 0.63.9 beta

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

The installer is `KingdomComeTogether-Setup-0.63.9-<build id>.exe`. What to try together is in
`docs/BETA-0.63.9-tester-script.md`; the evidence is `docs/WO-211-findings.md` (and `docs/WO-210-findings.md` for what came before).

**Both players must install the same build.** The version stays 0.63.9 for the whole beta; builds are told apart by their **build
id** (the launcher's About shows it, and so does the installer's name). A partner on a different build of 0.63.9 is told so at the
connection: "Your partner is on a different 0.63.9 build (...); install the same one".

---

## Fixed in the beta

- **A dropped connection no longer costs the joiner his game.** You'll be reconnected: the joiner stays where he stands in the host's
  world, still, with "Connection to your host lost -- reconnecting... (n s)" on screen, while the link comes back for up to a minute;
  then he carries on where he stood -- no loading screen, no jump. The host keeps playing and sees the partner's figure marked
  "(reconnecting)". If the link does not come back (or the joiner chooses **Leave the host's world** in the co-op menu), his progress
  is saved first: his own game loads with his character as it was at the drop (health, belongings, where he stood). If that save
  cannot be made, he is told so in words. `mp_link_hold` (on).
- **The host's crash in a shared session** ("Attempt to Release already released script table"): the game's own menus (closing the
  map, here) made the game call the mod's menu listeners off its main thread. They are never called there now.
  `mp_ui_lua_main_only` (on).
- **Seated people standing up and sitting down over and over, and T-posing, on the joiner's screen.** A seated person who only changed
  his gesture (drinking, talking) was let go of his seat and seated again every half minute; his body's queue overflowed. Now he is let
  go only when he really leaves the seat, and a body whose queue the game says is overflowing is reset cleanly at once.
  `mp_overflow_reseat` (on).
- **"I hit my partner and got a fine."** A hit between partners with friendly fire off makes no crime. The field's fine was the
  **shared crime** setting at work: the host's own lockpicking, seen by a guard, counted against both players -- and nothing said so.
  Now a stop for your partner's crime says it plainly ("Shared crime is on: the guard holds you to account for your partner's
  lockpicking"), and the host is told when the partner's crime counts against him too. `mp_joint_crime_told` (on).
- **Flying is off in co-op.** The Modding Tools' debug flight (F3) breaks the people around you: in a session it does nothing but say
  "Flying is turned off in co-op: it breaks the people around you." Testers who need it: `mp_allow_fly on` (off by default).
- **The host's bug report said "role: joiner".** The briefing now reads the role each machine had at each mark.

## Fixed in 0.63.9 (the release candidate before the beta)

- **People lying in their beds after a shared sleep** while they walked around on the host's screen ("floating sleepers"). A person who
  gets up on the host's screen now gets up on the joiner's too, and nobody is left lying or sitting where the host's screen has them
  walking. `mp_copy_state_always` (on).
- **People in a T-pose.** Every person of the host's world on the joiner's screen now always shows what that person does on the host's:
  when the mod lets one go, switches it or stands it up, it gets the host's current state at once, or its own idle -- never nothing.
- **Fights stuck in a grab** ("refusal loops"): a bandit next to the joiner tried to grab him every frame and froze. The game's own grab
  test now says no for the joiner and a host bandit, so it never starts; the fight goes on. `mp_clinch_guard` (on).
- **Bodies the joiner killed stood up, walked on, or could not be searched.** A killing blow of the joiner's is now a normal death with
  the game's own death animation, and the body can be searched by both players. `mp_death_engine` (on). *Fixed; confirmed on our bench in an earlier build* (the beta build's own kill run did not land a kill: the bench's input lost the joiner's window on each blow, so it says nothing either way).
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

## Switches

Console: `mp_<name> on|off` (either machine; `mp_<name>` alone says its state). New mechanisms ship **on**; `mp_allow_fly` ships off.

**These switches were proven safe to flip during play** (each off and on twice, live, in a tavern at night and in a fight, on our
bench): `mp_copy_anim_owner`, `mp_copy_opponent_mirror`, `mp_copy_attack_ai`, `mp_defence_local`, `mp_clinch_guard`,
`mp_copy_state_always`, `mp_copy_paired_engine`, `mp_copy_oneshot_request`, `mp_knockdown_sync`, `mp_copy_ready`.

**The rest were not flipped live on our bench** -- they work as switches, but flipping them mid-fight or mid-conversation was not
tested: `mp_death_engine`, `mp_copy_hit_local_none`, `mp_combo_hold`, `mp_listen_wheel`, `mp_conv_hover`, `mp_conv_pick_hold`,
`mp_conv_mark_over_icon`, `mp_rider_sync`, `mp_state_one_request`, `mp_profile_snapshot`, and the beta's own `mp_link_hold`,
`mp_ui_lua_main_only`, `mp_overflow_reseat`, `mp_joint_crime_told`, `mp_allow_fly`.

## Known

- **Weapons can appear in hands**, sometimes late.
- **Some fleeing people T-pose** on the joiner's screen.
- **Grabs (clinches) do not happen** between the players and the host's people.
- **Carts do not move** on the joiner's screen, and their riders walk.
- **In a joined conversation the listener can miss his head**, and some camera shots are off when the person stands differently.
- **The host's memory climbs while the host waits alone** for the partner: join within a couple of minutes of the host loading.
- **Flying is off by default** (see above).

## How to report

Press the **period key** the moment something looks wrong, then type `mark <what you saw>` in the console (~): the note reaches the
other player's bug report too. After the session **both players** send their bug reports. Every report carries the build id; say
which build you played if you write to us.
