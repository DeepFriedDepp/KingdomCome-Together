# Kingdom Come: Together 0.47.0 — shared combat, the certain fixes

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

0.46.5 plus WO-163 stage A (2026-10-08). The installer, `KingdomComeTogether-Setup-0.47.0.exe`, comes from the maintainer. The
evidence is `docs/WO-163-findings.md`; what to check live is `docs/TWO-PLAYER-CHECKLIST.md`, items 171–176 (and 150–170 from the
earlier builds, still pending).

**Both machines and the relay must run the same build.** It refuses every other version at the handshake.

---

## Changed

- **A blow with no swing behind it shows one.** When an enemy's blow reaches you and the host's game produced no swing for it
  (an animal's bite, a blow the mod could not read), your copy of the enemy plays one short generic lunge (an animal's bite, a
  man's punch, taken from the game's own tables) before the damage lands. `WO161-HIT … shown=generic`. Off: `GenericSwingEnabled`
  in `kcdmp-client.json`, or `--no-generic-swing` on the agent.
- **Combos, throws and the counter after a perfect block are shown.** The mod read the row of a paired attack at the wrong place,
  so every one of them was dropped on the other screen (8 % and 20 % of the host's swings in the testers' sessions); a counter-strike
  after a perfect block was never sent. Both are fixed at their source.
- **A blow is matched to its swing by the swing's own timing** (its wind-up plus its hit time, within a third of a second), not by
  "the newest swing in the last 1.2 seconds": on the testers' 45 blows 33 match (the old rule: 29, four of them to a swing it could
  not read). The log says `swing-unmatched` when swings exist but none fits, and `no-swing-captured` only when there was none.
- **An enemy turns from the player it fights only for clearly more damage** (25 % more over the last six seconds, on top of the
  existing rule): blocked blows and swings turn nothing.
- **Hitting a guard who fights your partner:** the host's 5-second crime judge now asks the game itself whether the victim is in a
  fight with the host before judging an assault; no answer in 1.5 seconds judges as 0.46.5 did. `mp_hostile_crime on|off` (host
  console, default on).
- **The fight "snap" is measured:** `MP-FIGHTSNAP` in the native log, one line per enemy copy per ten seconds — how far the copy
  steps when it comes back from a swing. It agrees with the development harness on the same run.
- **For the maintainer's live probes** (not for players): a read-only combat-model reader, the host's skirmish lever and a small
  console, `mp_w163_probe` (`docs/WO-163-findings.md`, Stage B).

## What did not change, and why (`docs/WO-163-findings.md`)

- **The host's game still decides whether a blow hits or is blocked.** Letting the player's own block decide on his own screen needs
  a call into the game's combat code that was not built: the first probe (does a copy that plays a host swing enter the combat
  module's "striking" state?) came back **no**, so that route needs more work and a supervised first run.
- **The host cannot lock onto a guard beating the partner, and the attacker does not recoil on a block:** their probes need a live
  session with the maintainer's hands. The levers exist as probe verbs; nothing of either is built.

## Known issues

- Checked in the game with **synthetic partners only** (a throwaway save): the verdict path with and without a swing, the fight
  measure, the probe console, a sync-attack row playing on a copy. **Not checked live:** the host reading a committed sync attack,
  a counter-strike after a perfect block, the crime judge with a real fight, the frame rate (menu, town, three-enemy fight).
- Set-piece brawls, a killed person standing up again on the other screen, bows and arrows, the host's lock-on to a guard beating
  the partner, the attacker's recoil, and a small step or snap of the other player's figure in a fight: as in 0.46.5.
- **Not soak-tested** (the maintainer's standing rule; the waiver is recorded beside the installer). The native plugin, the agent,
  the relay's shared wire and the mod's Lua all changed since 0.46.5. Not signed unless the build says so.
