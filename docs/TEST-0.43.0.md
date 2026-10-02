<p align="center"><img src="branding/banner-1280.jpg" alt="Kingdom Come: Together" width="640"></p>

# Testing 0.43.0 (both players)

**Kingdom Come: Together.** Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial and free; not
affiliated with or endorsed by Warhorse Studios or PLAION.

0.43.0 fixes what your long evening of questing showed: fighting the same enemy,
riding, catching up after a reload, black screens after a scene, the stuck anvil,
and more. One page for the host and the partner: install, then the checks below.
Tick what you saw, write the time next to anything odd, and send the logs at the end.

## Install

Both of you: run `KingdomComeTogether-Setup-0.43.0.exe` (the maintainer sends
it; it is not on GitHub). It installs over 0.42.8. Both computers need it:
0.43.0 refuses every other version. Afterwards, `tools\Verify-Install.ps1` (if
you have it) must end with "all present".

## What's new in 0.43.0

- **Fighting the same enemy:** on the partner's screen it reacts like on the
  host's, never falls over by itself, never sinks into the ground.
- **Riding:** your horse is yours while you ride it, on both screens.
- **After the host reloads**, the partner waits a few seconds ("Catching up with
  the host's world"), then everything matches; nothing is done twice.
- **Black screens after a scene** are helped along; none should last long.
- **The join really pauses the host's world.**
- **Doors** are the same on both screens.
- **Weather** follows the host's game as it changes.
- **The whistle** is heard at the caller.
- **A person a quest lets you carry alive** is carried on both screens.
- **Smithing** shows on the other screen; the anvil never looks taken. Leaving the
  smithing minigame works again.
- **Crimes count for both of you**; paying the fine (either of you) clears both.
- **Stuck in a bed or a minigame?** Type `mp_unstuck` in the console (`~`).

**Still known:** a friendly-fire hit on the partner shows no reaction yet; a wolf's
bite is not shown; you don't hear each other's conversations; a scene that waits
for a quest step can still stay black until a reload.

## Setup (both of you)

As before: install on both machines (`mark_setup`), the host loads their save
and clicks CONNECT, the partner waits at the main menu and clicks CONNECT there
(`mark_join`).

**Markers.** Each check has a one-word marker. When you start it, open the
console (`~`) and type it, for example `mark_ride`. It writes one line into the
logs so we can find the moment. Anything odd with no check to fit: `mark_odd`.

## The checks

1. **Fight the same enemy together** for a while. `mark_fight_same`.
2. **Ride together**: mount, ride, gallop, dismount, each of you. `mark_ride`.
   Screenshots of both screens, please.
3. **The host reloads** a recent save mid-quest. `mark_reload`.
4. **A quest scene** together. `mark_scene`. If a screen stays black, write down
   for how long.
5. **Carry the wounded hunter** (or anyone a quest lets you carry), each of you once.
   `mark_carry_alive`.
6. **Whistle** for your horse, each of you. `mark_whistle`.
7. **Doors**: open and close a few, both of you. `mark_door`.
8. **Forge a sword**, each of you, while the other watches. `mark_forge`.
9. **A crime**: one of you steals something in front of someone. `mark_crime`.
10. **Weather**: when it changes for the host, does it change for the partner?
    `mark_weather`.

The full list, with what to look for on each screen: `docs/TWO-PLAYER-CHECKLIST.md`,
section WO-151.

If something gets stuck: type `mark_odd`, then `mp_unstuck`; write down the time.

## Logs to send afterwards

**Before you restart the game or the launcher after anything odd**, and at the
end of the evening: both of you click **Report a bug** in the launcher and send
the zip it puts on your desktop.
