# Kingdom Come: Together 0.48.0 — the host's lock-on, and the ground work for "your own block decides"

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

0.47.6 plus WO-165 (2026-10-09), versioned 0.48.0 by the maintainer. The evidence is `docs/WO-165-findings.md`; what to check live is
`docs/TWO-PLAYER-CHECKLIST.md`, items 205–211 (and the earlier ones still pending).

**Both machines and the relay must run the same build.** It refuses every other version at the handshake.

---

## Changed

- **The host can lock onto an enemy that is beating his partner.** Before, the game's lock-on would never offer it: the enemy's
  fight was with the partner's figure only. Now, while the host stands within 6 m of such an enemy and faces it, the host joins its
  fight as its foe the game's own way, so the game's lock-on can pick it. The enemy keeps fighting the partner — checked live: it
  turned to the host only when he hit it. The host leaves that fight again past 10 m, when the fight ends, when the enemy dies, or
  when it has been left behind for 20 seconds. If the enemy turns on the host, that fight is his own and is never cut.
  `mp_host_lock on|off` (default on).
- **The joiner's own game judges an enemy's blow** (`mp_victim_decides`, default on): the hit and its damage come from the
  joiner's game and are applied once (checked live: four blows, each applied by the joiner's game, none twice). While the joiner
  holds block the host's game still decides that blow, as in 0.47.x: the game counts a held block only against an enemy it sees
  mid-strike, and the enemy's copy on the joiner's screen never is.
- **The bounce-back** (`mp_block_recoil`, default on): an enemy whose blow was blocked plays the game's own bounce-back (only for
  halberds, longswords, short swords and sword-and-shield: the game has no other). Not judged by eye yet.
- **For testers:** new log lines `WO165-LOCK` (the host's lock-on: set, removed and why, or why not), `WO165-REPLAY`,
  `WO165-OUTCOME`, `WO165-RECOIL` (the two switched-off pieces), `MP-WO165-STATS`.

## Known issues

- **Checked in the game with a stand-in partner only** (2026-10-09, a throwaway save, the maintainer at the keyboard for the lock-on
  and the block). The checklist's items 205–211 are the proof with two people.
- **Fight snapping:** two fixes from the study were measured and **neither** made the step after a swing 30 % smaller in the
  test fight, so neither ships. The test fight snaps far less than real sessions do; the next measurement needs network delay
  and a moving host.
- **A blocked blow** is still decided by the host's game (see above). Bows and arrows wait on the same question. Both new joiner
  pieces are on by default and not yet checked with two players: `mp_victim_decides off` / `mp_block_recoil off` turn them off.
- **Not soak-tested** (the maintainer's standing rule; the waiver is recorded beside the installer). The mod's own cost per frame
  in a three-person fight: 0.54–0.61 ms (0.47.5: 0.64–0.67 ms). Not signed unless the build says so.
