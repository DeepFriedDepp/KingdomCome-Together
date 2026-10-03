<p align="center"><img src="branding/banner-1280.jpg" alt="Kingdom Come: Together" width="640"></p>

# Testing 0.44.0 (both players)

**Kingdom Come: Together.** Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial and free; not
affiliated with or endorsed by Warhorse Studios or PLAION.

0.44.0 fixes what the tutorial session showed: a crash while picking herbs, copies
that acted on their own, commands lost while the game stalls, an enemy that could
stay standing after the host killed it. One page for the host and the partner:
install, then the checks below. Tick what you saw, write the time next to anything
odd, and send the logs at the end.

**Status:** none of this has run in the game; the checks below are its first real
test. The frame-rate soak was skipped for this build (the maintainer's call), so also
watch the frame rate: if it falls over a session, send the `FRAME` lines of
`kcdmp-native.log` (one per minute) with the logs.

## Install

Both of you: run `KingdomComeTogether-Setup-0.44.0.exe` (the maintainer sends it;
it is not on GitHub). It installs over 0.43.0. Both computers need it: 0.44.0
refuses every other version. Afterwards, `tools\Verify-Install.ps1` (if you have
it) must end with "all present".

## What's new in 0.44.0

- **Herbs:** pick herbs close together; neither game crashes. The other player's
  figure stands while its player picks.
- **Dogs** stand still; they never bite or chase on the partner's screen.
- **The host's own fights** do not pull the partner into combat.
- **A killed enemy** lies dead on both screens, once, and can be looted once.
- **A stall** (the partner's load, a hang) is waited out: what was sent during it
  arrives afterwards, in order.
- **Doors** are in the host's state right after a join; none flaps.
- **No notice on screen** about an NPC being moved by the machine's own AI, and no
  `FAULT motion::` lines in the native log.

**Still known:** the engine's cause of the herb crash is open; shared-quest steps
with no port can leave the partner stuck (a seat after a sleep); a friendly-fire
hit on the partner shows no reaction; a wolf's bite is not shown; you don't hear
each other's conversations.

## Setup (both of you)

As before: install on both machines (`mark_setup`), the host loads their save and
clicks CONNECT, the partner waits at the main menu and clicks CONNECT there
(`mark_join`). **Use throwaway saves only.**

**Markers.** Each check has a one-word marker. When you start it, open the console
(`~`) and type it, for example `mark_herbs`. It writes one line into the log so we
can find the moment.

## The checks (`docs/TWO-PLAYER-CHECKLIST.md`, WO-153, items 104–110)

1. **Herbs together** (`mark_herbs`). Both pick herbs in one patch for a few
   minutes. No crash on either machine; the other's figure stands while its player
   picks.
2. **A dog** (`mark_dog`). The partner walks past a village dog with a weapon
   drawn. The dog stands where it is.
3. **The host fights alone** (`mark_hostfight`). The host fights a bandit or a
   villager with the partner within ten metres. The partner is not drawn in and has
   no "in combat" state afterwards.
4. **A death** (`mark_death`). The host kills an enemy the partner can see (best: a
   road ambush). The partner's copy lies dead where the host's does, once; loot
   opens once; nothing is left standing.
5. **A stall** (`mark_stall`). The partner joins (the load takes about 40 s), and
   once during play the partner alt-tabs for 5 s. Afterwards the world matches the
   host's.
6. **Doors after the join** (`mark_door2`). A few doors the host opened earlier
   are in the host's state on the partner's screen; none flaps.
7. **Quiet logs** (`mark_quiet`). At the end: no on-screen notice about an NPC
   being moved by the machine's own AI, no `FAULT motion::` in the native log.

Anything stuck: `mp_unstuck` in the console (then `mp_unstuck hard`), marker
`mark_stuck`.

## Logs to send afterwards

Both machines: Report a bug in the launcher. It collects the logs of the six
launches before as well, so a restart does not lose the logs of a crash.
