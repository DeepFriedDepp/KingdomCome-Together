# Known issues (0.65.2)

Plain words, for players and testers. Each line says what you may see, what to do about it if anything, and whether a switch changes
it. The work orders' findings (`docs/WO-215-findings.md` … `docs/WO-221-findings.md`) have the measurements.

## People walking and working

- **Walking villagers walk on their own feet now, but not always in the same place.** On the joiner's screen a walking person is
  walked by the game towards where the host's person is (`mp_drive`, on). The steps are right and the feet slide less (not yet
  as little as the host's own); the place is the host's within half a metre about seven times in ten (with the old placed walk
  about eight and a half), and now and then a person is put back a step or more (on our bench about once in five minutes per
  person, a fifth as often as in 0.65.0). A person who falls far behind is put where the host's is in one go. `mp_drive off` gives 0.65.0's placed walk back.
- **A person may still flicker or jerk between two places** — usually for seconds, now and then for minutes. It is nearly always
  someone the joiner's game itself holds somewhere else than the host's stream says (a guest it keeps on a seat, a worker at a
  spot). 0.65.2 leaves such a person where the game holds them after two tries (`mp_holders`, on), which makes it rarer, not gone
  (on our bench about once in half an hour per person, an eighth as often as with the switches off). The price: such a guest may
  sit on a different seat than on the host's screen. Press `.` when you see it and say who.
- **The game's own routes for copies (`mp_route`) stay off** and are still in the build: this build's walk did not hold every one of
  its marks on our bench, so the older ways were not removed.
- **Activities.** Most of what villagers do (working, eating, sleeping) shows on the joiner's copy within a second. Still missing at
  times: **work tied to an object** (a smith at his forge, a cook at her pot) may show the copy standing idle at the place.
- **Seated guests** jump between seats much less than in 0.64.3, but not never. Press `.` when one jumps.
- **Horses of the animal care** (very long names) may still behave as the joiner's own horse (frozen) rather than the host's.

## Fights

- **The joiner's game can still close after people die near the joiner.** The fight crash of 0.65.0 (a fighting copy whose body was
  taken away mid-swing) is fixed. A different crash after deaths is not: on our bench, in a staged massacre (a death near the
  joiner every 15 seconds), the joiner's game closed once in 2 to 26 deaths. If a game closes during or after a fight, start it
  again and send the bug report at once: the crash report goes with it. `mp_combat_action_end off` gives 0.65.0's behaviour back
  (it crashes on our bench too, sooner). WO-222's bench saw two more joiner closes in fights, in another place in the game's combat
  code (once with WO-222's switches off, once on), and one host close on the game's renderer thread after an hour of fights.
- **Enemies notice the joiner now** (`mp_w223_perceive`, on). A hostile camp or patrol makes the joiner out as it would the
  host — by the game's own measure of how well he can be seen and heard, with his own crouch, light, noise and stealth skill — and
  fights him in a real fight of the host's game. What is not yet the same as for the host:
  - **The distance.** At a walk the joiner was noticed a few metres further out than the host on our bench, at a sprint a few
    metres nearer; every stage up to the draw came within a second of the host's at both
    paces. Crouched in the dark he gets nearer than the host does before anyone notices (his game reports him a little darker
    than the host's own).
  - **Now and then nobody notices him.** In one bench walk of eighteen the joiner reached the middle of a camp and stood there
    unnoticed: the one bandit facing him did not see him where he had stopped, two slept, two stood with their backs to him.
    If a camp ignores you although a bandit plainly looks at you, press `.` and say "ignored" — and whether it woke when you moved.
  - **A camp's warnings are not heard by the joiner.** A camp does not attack a crouched or unarmed visitor at once: it warns him
    for half a minute, then attacks (or at once when he walks up to one of them). The host hears "get out"; the joiner hears and
    sees nothing during that half minute, then the camp attacks. An armed joiner who stands on its ground is attacked at once,
    as an armed host is.
  - **Wolves and wild dogs.** The rule is in (an animal that hunts the host hunts the joiner); our bench could not stage a hunt,
    so it is not shown. Tell us what they did.
- **Both players at a camp** (`mp_w223_split`, on): when the camp's fight starts with the joiner and nobody is on the host, one
  bandit near the host is sent at him (the game's own attack call and "fight this one", for six seconds; then the game picks
  again). On our bench each player had an enemy on him within five seconds in 23 fights of 25 (3 of 5 with the switch off); in
  one of the two the host stood outside the camp's ground and every bandit took the joiner for over ten seconds.
  `mp_w223_split off` leaves it to the game.
- **An enemy fighting the joiner on the host's screen now fights him on his own** (`mp_w223_copy_target`, on): on our bench its
  copy was in his fight and aimed at him in 99.8 % of the time it should be, also when both players are on one bandit (before,
  such a copy could go on swinging at the host's figure for minutes). One bench scene, with a joiner who had lost his weapon,
  showed copies standing idle for a minute while their host NPCs came at him; press `.` and say "idle enemy" if you see that.
- **A few blows on the joiner are still the host's guess, not his own game's verdict**: 3 to 4 % of the blows in a camp fight
  with several bandits on him, 5 % when a bandit swaps between the players, 4 % against two bandits (0.65.2's way on the same
  bench: 4 to 10 %). They are blows of fast chains and combos that the copy on his screen could not replay in time. The host waits
  up to 3 s for his verdict, then sends its own. A blow you blocked or dodged on your screen is never charged on top of it: a
  late verdict of yours stands, and a host verdict already applied is given back (`mp_def_wait`). **Blows inside a grab** are always the host's (the joiner's game
  has no grab with a copy). Press `.` when you take a hit you did not see coming.
- **A clean hit of the joiner's sometimes draws no flinch.** In a fight both players are in, his clean hits made the enemy
  flinch every time on our bench (16 of 16); in a fight he had alone, mostly (9 of 12). It can fail for a stretch — the host's
  game then reads the enemy "out of combat mode" and plays no hit reaction — on an enemy whose fight with him was broken off
  and taken up again: our bench's own duel scene, where the bandit's fight is stopped and started by the bench, had 14 of 27
  (16 of 17 before enemies noticed the joiner). The hit wounds and kills either way. Press `.` and say "no flinch", and what
  happened just before.
- **The joiner lands fewer blows than the host** on our bench (about one swing in five touched a body, the host's one in two): his
  attacks are cut short when he is struck, and the enemy blocks and dodges as the host's does. Every touch goes to the host and is
  judged there. The bug report now says, per swing, what it touched or why not (`W222-SWING`), and its briefing has one line for it.
- **An enemy that runs away (or begs for mercy) stays the joiner's enemy** (`mp_flee_live`): it runs on his screen and his blows on it
  count (on our bench every blow of his on a runner reached the host and landed). Our bench could make an enemy flee only now and
  then, and its joiner knocked a runner down but did not see one die: press `.` and say "runner" if one cannot be killed. A runner
  the joiner strikes may turn and fight him again.
- **A knocked-down copy stays down while its host NPC lies**; it may fall or get up a few tenths of a second after the host's, and
  on our bench one get-up in a handful was not shown.
- **Paired attacks** (one fighter's move answered by another's) are not always shown on the joiner. **Grabs** do not happen between the
  players and the host's people.
- **The host's frame rate in fights** may drop a few frames a second with the fast fight updates of 0.64; `mp_npc_bands off` in the
  host's console brings back the old rates. Tell us if you see it.
- **The mod's own work on the host** costs about 1.9 ms of a frame on average and, in about one frame in twenty, 10 ms
  (our bench; 0.65.0: 2.8 and 13 ms). The target was 1.5 ms in that one frame in twenty. The bug report's recorders now write
  their files on their own thread; what is left is the reading of the game's state itself, which has to happen on the game's main thread. A stutter on the host you can feel: press `.`.

## Horses

- **The joiner's horse may look bare on the host's screen** (no saddle), while it is saddled on the joiner's: the starting saddle is
  a gift to the buyer in the buyer's game only. The bags work all the same.
- **Host: do not buy a horse the joiner has already bought.** While the joiner's horse stands in the trader's yard, the trader
  offers it to the host as well (the game's rule for "for sale" is per player). If the host buys it, the horse stays the joiner's,
  the host keeps his own, a message says so — and **the price is gone** (the game takes it before the mod can step in).
- **Food dropped out of a horse's bags near the horse trader's stable may vanish** before the other player picks it up (an onion
  and an apple on our bench). 180 m away the joiner's food passed both runs; food out of the host's own horse is untested (our
  bench's script could not put food into it). Weapons, money and tools passed both ways.
  `mp_horse_drop off` turns the shared drop from a horse's inventory off.
- **A host who loads a save before the mod is connected** keeps the horse record of the running session, not that save's. It is
  right when the save is the newest one; after loading an older save this way, the joiner may be told a horse is his that he had
  not bought yet in that world. Loading from inside a session is not affected.
- **A horse ridden far from the other player may jump back on the other player's screen** for a moment (up to about 130 m on our
  bench, with the other player some 270 m away): there the ground is not loaded and the game's own fall-through rescue puts the
  horse back. Riding together it does not happen: over 24 stretches side by side the two horses were never more than 1.25 m apart
  between the two screens.
- **The first time a player mounts**, the game's own "Riding a horse" page opens and holds that player's game until it is closed.

## Items and the world

- **Chests are each player's own** (by design): a chest the host emptied is still full for the joiner, and the reverse.
- **Herbs picked from a patch** (the gathering, not a loose item) are each player's own.
- **Loose items lying in the world** (a sword on the ground, a coin purse) may not be the same for both players.
- **Carts** follow the host's carts on the joiner's screen about a metre off at most; the riders sit, the wheels turn, the horses
  pull. A rider's exact pose on the seat may differ a little from the host's.
- **Road encounters** (a robbery, a broken cart): what they leave — the dead, the props — is sent to the joiner, but our test bench
  could not start one on demand; the tester script's scene 6 is its test.
- **A dead body the joiner did not see die** lies down within a few seconds of the joiner coming near (up to about twenty for a body
  far from where it died); for that moment it may stand.
- **Stolen items:** an item's owner and stolen mark do not travel with it yet when it is looted or dropped: the mod sends them, but this
  game build gives scripts no way to set an item's owner (the call is not there), so the joiner's copy of the item is unmarked.

## Conversations

- **A talk may not start at the first try** (about one in twelve on our bench: the request timed out). Talk to them again.
- **Some shopkeepers do not take every item** on the joiner's side that they take on the host's (two keepers on our bench never
  took an onion). Press `.` and say which shopkeeper and which item.
- **Listening in on the partner's talk:** once in a while the listener's keys may stop working for that talk (seen on the host, about
  one listen in five in some sessions on our bench, not traced). The listen keys are re-asserted once a second; press `.` if it
  still happens and leave with Back.

## The connection

- **A short stall when something loads** (a trade screen, a conversation's scene): the joiner's game itself stops for a second or
  two, and what the host does in that moment arrives just after.
- **The host's memory climbs while the host waits alone:** join within a couple of minutes of the host loading.

## The game's own systems

- **The host's game may take twice its usual memory after it loads a save with a joiner in the session** (on our bench: 9 GB
  to 17.5 GB within a few minutes, then flat; about one session in twenty-five, also on 0.65.0). On a PC with 16 GB that ends in
  a slow or closed game. Restart the host's game and send the bug report if you see it.
- **Lip-sync of conversation lines** depends on the game's facial data, which the Modding Tools build does not ship. The mod installs
  nothing outside its own folder; if you want lip-sync, the community's own instructions for adding the game's facial data to the
  Modding Tools install apply.
- **Weapons may pop in** for a moment when a person draws on the joiner's screen.
- **Playing far apart** (beyond the leash) is not supported: people near the far player may freeze or be missing.
- **Flying is off** in co-op (`mp_allow_fly on` for testers).

## Fixed in 0.65.2

- **The joiner's game closing when a fighting person is taken away mid-swing** (`mp_combat_action_end`, on). The crash after
  many deaths near the joiner is not fixed (Fights, above); `mp_ragdoll_once` (on) lays a fresh body down only once.
- **The joiner's trade failing** — no shop screen with a shopkeeper at work, a talk with a busy person never starting, a talk ended
  by the mod while the joiner was choosing (`mp_shop_keeper`, `mp_talk_hold`, `mp_talk_force`, on).
- **Sliding feet and slow steps** of walking people on the joiner's screen (`mp_drive`, on).
- **A joiner's bought horse lost at the next join; a whistle that fetched instead of called** (`mp_own_horse`, on).
- **Dead bodies standing or in the bind pose** when the joiner had not seen them die (`mp_corpse_look`, on).
- **Cart riders standing, wheels not turning, cart horses missing** (`mp_cart_seat`, `mp_cart_wheels`, on).
