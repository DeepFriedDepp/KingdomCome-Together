<p align="center"><img src="branding/banner-1280.jpg" alt="Kingdom Come: Together" width="640"></p>

# Testing 0.42.8 (both players)

**Kingdom Come: Together.** Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial and free; not
affiliated with or endorsed by Warhorse Studios or PLAION.

0.42.8 fixes the game getting slower and slower in fights and crowds. One page
for the host and the partner: install, then the checks below. Tick what you saw,
write the time next to anything odd, and send the logs at the end.

## Install

Both of you: run `KingdomComeTogether-Setup-0.42.8.exe` (the maintainer sends
it; it is not on GitHub). It installs over 0.42.7. Both computers need it:
0.42.8 refuses every other version. Afterwards, `tools\Verify-Install.ps1` (if
you have it) must end with "all present".

## What's new in 0.42.8

- **The frame rate holds.** In 0.42.5 and 0.42.7 the game got slower every time
  several people were near you (a fight in a village, a market), and it stayed
  slow until you restarted the game. That is fixed.

To see the frame rate, type `r_DisplayInfo 1` in the console (`~`); the number
is in the top corner. `r_DisplayInfo 0` hides it again.

**Still known:** as in 0.42.7 (`docs/TEST-0.42.7.md`), apart from the slow game.

## Setup (both of you)

As in 0.42.7: install on both machines (`mark_setup`), the host loads their save
and clicks CONNECT, the partner waits at the main menu and clicks CONNECT there
(`mark_join`).

**Markers.** Each check has a one-word marker. When you start it, open the
console (`~`) and type it, for example `mark_fight`. It writes one line into the
logs so we can find the moment. Anything odd with no check to fit: `mark_odd`.

## The checks

1. **A fight among people.** Fight bandits, or anyone, in or near a village with
   several people around, for five minutes or more. Marker: `mark_fight`.
   * Both: write down the frame rate before the fight, during it, and five
     minutes after. It should stay about where it started.
2. **A crowd.** Stand or walk in a busy place (a market, a tavern) for five
   minutes. Marker: `mark_town`.
   * Both: the frame rate stays about where it started.
3. **Everything from 0.42.7:** carrying a body (`mark_carry`, `mark_carry_host`,
   `mark_carry_both`), a sack (`mark_sack`), a burial (`mark_bury`), the dice
   keys (`mark_dice_keys`): `docs/TEST-0.42.7.md`.

If the game does get slow: type `mark_odd`, write down the time and the frame
rate, and send the logs **before** you restart the game.

## Logs to send afterwards

**Before you restart the game or the launcher after anything odd**, and at the
end of the evening: both of you click **Report a bug** in the launcher and send
the zip it puts on your desktop. It holds this evening's logs and those of the
six launches before.
