# Known issues (0.65.0)

Plain words, for players and testers. Each line says what you may see, what to do about it if anything, and whether a switch changes
it. The work orders' findings (`docs/WO-215-findings.md` … `docs/WO-218-findings.md`) have the measurements.

## People walking and working

- **Walking villagers are moved by the stream, not by their own feet.** On the joiner's screen a villager follows the host's villager
  a moment behind; once in a while one is pulled back a step. The game's own walking for copies (`mp_route on`) is closer to the host
  but corrects more and once crashed the joiner's game when a far body was removed: it is **off** by default. Try it with `mp_route on`
  for a while (the tester script's last scene) and say which looks better.
- **A person may flicker or jerk between two places** — usually for seconds, now and then for minutes (a woodcutter on our bench for
  fifteen): on the joiner's screen the person's own job or seat and the host's stream both place them. Most often right after a fight,
  or while someone works at a spot that is not where the host's person works. It happened in 0.64 too; it shows more now that the
  joiner sees everything on time. Press `.` when you see it and say who.
- **Activities.** Most of what villagers do (working, eating, sleeping) shows on the joiner's copy within a second. Still missing at
  times: **work tied to an object** (a smith at his forge, a cook at her pot) may show the copy standing idle at the place.
- **Seated guests** jump between seats much less than in 0.64.3, but not never. Press `.` when one jumps.
- **Horses of the animal care** (very long names) may still behave as the joiner's own horse (frozen) rather than the host's.

## Fights

- **A knocked-down copy stays down while its host NPC lies**; it may fall or get up a few tenths of a second after the host's.
- **Paired attacks** (one fighter's move answered by another's) are not always shown on the joiner. **Grabs** do not happen between the
  players and the host's people.
- **The host's frame rate in fights** may drop a few frames a second with the fast fight updates of 0.64; `mp_npc_bands off` in the
  host's console brings back the old rates. Tell us if you see it.
- **The mod's own work on the host** costs about 1.7 ms of a frame on average and, in about one frame in twenty, 9–10 ms (our bench;
  the bug report's check of the story's state was cut to a third of a millisecond in 0.65.0, the rest is not smaller yet). A stutter on
  the host you can feel: press `.`.

## Items and the world

- **Chests are each player's own** (by design): a chest the host emptied is still full for the joiner, and the reverse.
- **Herbs picked from a patch** (the gathering, not a loose item) are each player's own.
- **Loose items lying in the world** (a sword on the ground, a coin purse) may not be the same for both players.
- **Carts** follow the host's carts on the joiner's screen. **The horses** may not always be with the joiner's cart; press `.` if a
  cart drives without them.
- **Road encounters** (a robbery, a broken cart): what they leave — the dead, the props — is sent to the joiner, but our test bench
  could not start one on demand; the tester script's scene 6 is its test.
- **Stolen items:** an item's owner and stolen mark do not travel with it yet when it is looted or dropped: the mod sends them, but this
  game build gives scripts no way to set an item's owner (the call is not there), so the joiner's copy of the item is unmarked.

## Conversations

- **A trade with a busy shopkeeper may end by itself on the joiner's screen** a few seconds after it begins (once in two trades in our
  acceptance: the innkeeper's copy was busy with her own bar work when the conversation started). Talk to them again.

- **Listening in on the partner's talk:** once in a while the listener's keys may stop working for that talk (seen on the host, about
  one listen in five in some sessions on our bench, not traced). 0.65.0 re-asserts the listen keys once a second; press `.` if it
  still happens and leave with Back.

## The connection

- **The joiner keeps up now.** 0.65.0 hands the joiner's game everything waiting in one go instead of one request a frame: on our bench,
  in a packed tavern and a brawl, people's movements and the fight reached the joiner within a tenth of a second 99 % of the time, where
  0.64.4 fell up to a minute behind. What people do (sitting down, a job) usually shows within a quarter of a second, but can take up to
  about one and a half. If the joiner still sees the host's world late, the bug report's first section ("The queue") says how late;
  press `.` and say where.
- **A short stall when something loads** (a trade screen, a conversation's scene): the joiner's game itself stops for a second or
  two, and what the host does in that moment arrives just after.

## The game's own systems

- **Lip-sync of conversation lines** depends on the game's facial data, which the Modding Tools build does not ship. The mod installs
  nothing outside its own folder; if you want lip-sync, the community's own instructions for adding the game's facial data to the
  Modding Tools install apply.
- **Weapons may pop in** for a moment when a person draws on the joiner's screen.
- **Playing far apart** (beyond the leash) is not supported: people near the far player may freeze or be missing.
- **Flying is off** in co-op (`mp_allow_fly on` for testers).

## Fixed in 0.65.0

- **The joiner fell seconds to minutes behind in crowds and fights** (0.64.x on our bench: a packed tavern up to a minute, a brawl's
  falls and work late). Fixed (`mp_pipe_batch`, on).
- **Doors differed between the players** — a door locked on the joiner's side only, or a door a villager opened left shut for the
  joiner. Fixed within 60 m of either player (`mp_door_sweep`, on); a door farther off may differ until one of you comes near.
- **A copy dropped its work for a moment of its own** (a stretch, a serving gesture, an idle step) while the host's person went on.
  Fixed for the common cases (`mp_takeover_stop`, on).
