# Known issues (0.64.2 – 0.64.4)

Plain words, for players and testers. Each line says what you may see, and whether a switch changes it. The work orders' findings
(`docs/WO-215-findings.md`, `docs/WO-216-findings.md`, `docs/WO-217-findings.md`) have the measurements.

## People walking and working

- **Walking villagers are moved by the stream, not by their own feet.** On the joiner's screen a villager follows the host's
  villager a moment behind; once in a while one is pulled back a step. The game's own walking for copies (`mp_route on`) is
  closer to the host but corrects more and once crashed the joiner's game when a far body was removed: it is **off** by default.
  Try it with `mp_route on` and say which looks better.
- **Activities.** Most of what villagers do (working, eating, sleeping) now shows on the joiner's copy within a second. Still
  missing at times: **work tied to an object** (a smith at his forge, a cook at her pot) may show the copy standing idle at the
  place; and a copy that is already doing it may **drop it for a moment of its own** (a stretch, a serving gesture, an idle step).
- **Seated guests** jump between seats much less than in 0.64.3, but not never: a guest whose own seat on the joiner's side is a
  different chair from the host's may stay in its own chair. Press `.` when one jumps.
- **Horses of the animal care** (very long names): 0.64.4 can carry their names, but the test bench never saw one become a copy;
  they may still behave as before (the joiner's own horse, frozen).

## Fights

- **A knocked-down copy stays down while its host NPC lies** (0.64.4); it may get up up to about a second after the host's.
- **Paired attacks** (one fighter's move answered by another's) are not always shown on the joiner.

## Items and the world

- **Chests are each player's own** (by design): a chest the host emptied is still full for the joiner, and the reverse.
- **Herbs picked from a patch** (the gathering, not a loose item) are each player's own.
- **Carts** follow the host's carts on the joiner's screen (0.64.4). **The horses** may not always be with the joiner's cart (the
  test bench saw both); press `.` if a cart drives without them.
- **Road encounters** (a robbery, a broken cart): what they leave — the dead, the props — is sent to the joiner from 0.64.4, but this
  could not be tried on the test bench; scene 7 of the tester script is its test.
- **Doors.** A door you open or close shows the same for the other player. But a door may be **locked on the joiner's side** while
  the host's is unlocked (the joiner's world locked it by its own rules), and a door a villager opened on the host may stay shut
  for the joiner. Press `.` at such a door.

## The connection

- **A crowded place can put the joiner behind.** In a packed tavern the joiner's game may fall seconds — on the test PC, which runs
  both games at once, up to a minute — behind the host's: villagers late, a fight's falls late. 0.64.4 removed the biggest cause
  (see below). If it happens, press `.` and say where; the bug report shows how far behind it was.

## Fixed in 0.64.4

- **The joiner's conversations hung after the first line (0.64.3).** A talk started, then no choice ever came, the screen stayed in
  the conversation and the next talk or trade would not start. 0.64.3 hid the game's own conversation actors on the joiner's side.
  Fixed (`mp_dialog_twins`, on); if a joiner's talk still hangs, press `.` and say with whom.
- **The joiner fell behind in busy places (0.64.3).** In a village by day the joiner's game could run tens of seconds behind the
  host's: villagers late, a talk refused, an item dropped by the other player showing late. 0.64.3 handed the joiner's game every
  villager's walking plan, which nothing uses while `mp_route` is off. Fixed (`mp_route_skip`, on); with `mp_route on` the plans are
  still used.

