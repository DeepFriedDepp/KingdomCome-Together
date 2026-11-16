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
  (it crashes on our bench too, sooner).
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
