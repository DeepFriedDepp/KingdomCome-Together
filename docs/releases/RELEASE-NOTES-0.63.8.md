# Kingdom Come: Together 0.63.8 — enemies that fight the joiner on the joiner's screen

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

Everything on `main` from WO-209 (2026-10-26/27). The installer, `KingdomComeTogether-Setup-0.63.8.exe`, comes from the
maintainer. The evidence is `docs/WO-209-findings.md`; what to try together is `docs/WO-209-tester-script.md`.

**Both machines and the relay must run the same build.** 0.63.8 refuses every other version at the handshake.

---

## Fixed

- **Enemies on the joiner's screen fought the host.** An enemy that fights the joiner on the host's screen now fights the joiner on
  the joiner's screen too: it takes the joiner as its opponent, joins his fight, raises its guard and stays in that fight until it
  ends. Lock-on takes it when it is in front of the joiner. `mp_copy_opponent_mirror` (on).
- **Their swings at the joiner are their own attacks** (`mp_copy_attack_ai`, on): the enemy really attacks the joiner on his screen,
  aimed and timed by the game, instead of playing a swing animation that could not touch him.
- **Some of the host's people stood still on the joiner's screen** (camp bandits at their resting spots while they fought on the
  host's screen): two causes, both fixed.
- **The joiner's sleep or wait cut short** by the clock correction: it now runs to its end. `mp_clock_quiet_in_skip` (on).
- **A follow-up conversation twice.** When a talk the quest starts by itself reaches a player who already has it from the partner, it
  ends there and the player listens to the partner's. `mp_forced_talk_once` (on).
- **A part of a quest asleep on the joiner** that the host has awake wakes when the host's step arrives. `mp_quest_wake_with_host` (on).
- **Two crashes of the joiner's game in long fights** (found while testing this release, both the mod's): after the joiner died and
  respawned, an enemy could stay counted in his old fight and attack him from it; and an enemy the game itself no longer held
  hostile to him could still start an attack at him. Both are closed.

## Changed

- **The host's people on the joiner's screen now have one animation owner** (`mp_copy_anim_owner`, now **on**; 0.63.7 had it off):
  each one plays the animation the host's person plays, driven by the host's own state -- sleepers now lie down on the joiner's
  screen too, and fights match the host's far more closely. If the people around you look worse than in 0.63.7, both type `mp_copy_anim_owner off` and tell us where.
- **The joiner's own game judges his defence** (`mp_defence_local`, on as before): block, perfect block, parry and dodge against the
  host's enemies -- about nine blows in ten on our bench now that their swings are their own attacks; the rest (the last swing of a
  combo, the first of a fight) take the host's verdict.

## Known

- **Animations of the host's people on the joiner's screen** (`mp_copy_anim_owner`): sleepers now lie down, but seated people can
  still stand on the joiner's screen, walkers can slide more than on the host's, and in a village some people can lag behind
  where they are on the host's screen. A person the owner lets go can keep looping the same idle. If it looks worse than 0.63.7,
  both type `mp_copy_anim_owner off`.
- **Lock-on** takes the enemy in front of the joiner; an enemy attacking from the side is not taken until he turns to it.
- **Quest areas** count only the host's character (unchanged).
- **A timed choice in a joined conversation** has not been tried on our bench (none was in reach); tell us if you meet one.
