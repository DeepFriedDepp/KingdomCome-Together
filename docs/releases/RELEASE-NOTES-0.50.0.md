# Kingdom Come: Together 0.50.0 — a quest's own fights stay the story's

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

0.49.2 plus WO-176 (2026-10-11/12), versioned 0.50.0 by the maintainer. The evidence is `docs/WO-176-findings.md`; what to check live is
`docs/TWO-PLAYER-CHECKLIST.md`, items 276-280. Everything here was checked on one machine; none of it with two real players.

## What changed

- **A quest's arranged fights stay the story's.** A quest marks every fighter of a fight it arranges (the Tachov–Zelejov meadow fight,
  quest fist fights); the mod now reads those marks and keeps the partner's figure out: none of the partner's blows count in that fight,
  the mod adds the figure to it nowhere, pauses the figure's own brain while the fight is near and takes it out if a fighter turns on it,
  and the partner is told once: *"This fight belongs to the story — your partner fights it."* The fight ends the way the story says and
  weapons draw afterwards. `mp_arranged_guard` (on).
- **Locked saves repaired on load, checked in the game.** Loading a save that still carries the old weapon lock writes a repaired copy
  next to it, loads it and says so; the weapon key draws afterwards.
- **Blocks shown on the joiner's screen.** When the host's enemy blocks, parries or has its block broken by the joiner's blow and the
  host's game sends no reaction of its own, the joiner's copy of it plays its own block. `mp_copy_blocks` (on). The enemy's recoil after
  a blocked blow (`mp_block_recoil`, on) was checked in the game.
- **`mp_real_hits` stays on**: the joiner's blows are real hits in the host's game, one blow still never knocks an enemy out.
- **Frame rate**: the same as 0.49.2 on the same machine (menu, town, a 3-enemy fight).

## Known limits

The game itself still counts the partner's figure as a passive member of a village's fight (its body uses a villager's soul for its
face); a camp bandit does not fight the partner's figure, so real hits against a bandit that fights back were not tested; a joiner's own
held block still counts by the host's verdict (`mp_victim_decides` off: the joiner's copy of an enemy is never their locked-on opponent
yet). See the findings, "Untested, plainly".
