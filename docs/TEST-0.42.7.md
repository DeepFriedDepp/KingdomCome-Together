<p align="center"><img src="branding/banner-1280.jpg" alt="Kingdom Come: Together" width="640"></p>

# Testing 0.42.7 (both players)

**Kingdom Come: Together.** Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial and free; not
affiliated with or endorsed by Warhorse Studios or PLAION.

0.42.7 is about carrying: when one of you picks up a body or a sack, the other
now sees it. One page for the host and the partner: install, then the checks
below. Tick what you saw, write the time next to anything odd, and send the logs
at the end.

## Install

Both of you: run `KingdomComeTogether-Setup-0.42.7.exe` (the maintainer sends
it; it is not on GitHub). It installs over 0.42.5. Both computers need it:
0.42.7 refuses every other version. Afterwards, `tools\Verify-Install.ps1` (if
you have it) must end with "all present".

## What's new in 0.42.7

- **Carrying shows on the other screen.** When one of you picks up a dead or
  knocked-out body, the other sees your figure lift it onto its shoulder, carry
  it and put it down, and the body lies where you put it, on both screens.
- **One carrier at a time.** If you both reach for the same body, the host's
  world decides who has it; the other's game puts it down again.
- **Sacks** (a task with sacks to carry): the other sees your figure holding the
  sack, and a dropped sack lying where it fell. It is only shown: it does not
  count for the other's task.
- **Nothing alive is moved.** A body is moved only while it is carried, and one
  that would end up in the air or under the ground goes back where it was picked
  up.
- **The dice keys** are now made on your computer from your own game's files
  (Setup does it, and the launcher before every start). Nothing to do; if the
  dice keys ever stop working, say so.
- **About** in the launcher: where the project lives, who made it, the licence.

> **If something looks wrong, turn it off with…** (the console, `~`; `on`
> switches it back; nothing else changes)
>
> | what looks wrong | who | type |
> |---|---|---|
> | bodies jumping, or carried oddly, on your screen | both | `mp_carry_sync off` |
> | sacks in the other's figure's hands, or lying about | both | `mp_carry_objects off` |
>
> Say which one you switched off, and when (`mark_odd` first).

**Still known**

- The partner's carrying does not count for the host's quests yet (a burial,
  sacks to deliver): only the host's own carrying does.
- Crimes for carrying a body are not shared yet.
- There is no throwing in the game: "drop" is putting it down.
- Where the host's animals stand can differ on the partner's screen until they
  come close.
- A cutscene plays for each of you separately; on the partner's screen it can
  stay black until the host's next step.
- An escort (the sheep in "Find Mutt!") follows only the player who leads it.
- The partner does not hear the host whistle, and the other way round.
- The journal's marker letters can differ between the two of you.
- At the grindstone the blade is not in the other player's figure's hands.

## Setup (both of you)

1. **Install** the same build on both machines. Marker: `mark_setup`.
2. **The host** starts the game from the launcher, loads their save, and clicks
   CONNECT once they can move.
3. **The partner** starts the game from the launcher and **waits at the main
   menu. Don't load a save.** Click CONNECT at the menu; the partner joins the
   host's world by itself. Marker: `mark_join`.

**Markers.** Each check has a one-word marker. When you start it, open the
console (`~`) and type it, for example `mark_carry`. It writes one line into the
logs so we can find the moment. Anything odd with no check to fit: `mark_odd`.

## The checks

1. **The partner carries a body.** After a fight, the partner picks up a dead
   body, carries it twenty steps or so and puts it down. Then the same with
   someone knocked out. Marker: `mark_carry`.
   * Host: the partner's figure lifts the body onto its shoulder, walks with it
     and puts it down. The body lies where the partner put it, on both screens.
2. **The host carries a body.** The same the other way round. Marker:
   `mark_carry_host`.
   * Partner: the host's figure carries it; it lies where the host put it down.
3. **Both reach for the same body.** Stand by one body and pick it up at the
   same moment. Marker: `mark_carry_both`.
   * Both: only one of you ends up carrying it. On the other's screen the body
     is put down ("Your partner has that body in the host's world.") and the
     winner's figure picks it up.
4. **A sack.** If one of you has a task with sacks to carry, carry a few and drop
   one on the way. Marker: `mark_sack`.
   * The other: the figure holds a sack while it carries one; a dropped sack lies
     where it fell and goes away when it is picked up again.
5. **A burial** (a quest where a body is carried to a grave). The partner carries
   the body while the host watches. Marker: `mark_bury`.
   * Host: write down what the quest does (nothing, moves on, or fails).
6. **The dice keys.** Play dice once each. Marker: `mark_dice_keys`.
   * Both: picking and throwing the dice work with the keys, as before.
7. **Again from 0.42.5:** bandits (`mark_fight`), the leash (`mark_leash`), a
   busy town (`mark_town`), loading a save in the session (`mark_load`).

## Logs to send afterwards

**Before you restart the game or the launcher after anything odd**, and at the
end of the evening: both of you click **Report a bug** in the launcher and send
the zip it puts on your desktop. It holds this evening's logs and those of the
six launches before.
