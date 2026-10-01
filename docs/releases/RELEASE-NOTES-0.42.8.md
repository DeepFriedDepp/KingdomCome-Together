# Kingdom Come: Together 0.42.8 — the frame rate holds

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

Everything on `main` from WO-136 to WO-148 (2026-10-01), and one fix on top.
New since 0.42.7: the frame-rate fix. The installer,
`KingdomComeTogether-Setup-0.42.8.exe`, comes from the maintainer and is not on
GitHub. The tester page is `docs/TEST-0.42.8.md`.

**Both machines and the relay must run the same build.** 0.42.8 refuses every
other version at the handshake, 0.42.7 and 0.42.5 included. Don't go back to
either for a long session: both have the bug this release fixes.

**Verified:** in the built DLL's own machine code, by the native unit tests, and
live on one machine: in a crowd and in a fight the game's stat bookkeeping stayed
empty and the frame rate held. The cause, the evidence and the numbers:
`docs/WO-148-findings.md` section 7.

---

## Fixed

- **The frame rate no longer falls in fights and crowds.** Since 0.42.5 the DLL
  read the health and stamina of everyone within 15 m of you, and from the
  second person on it passed the game a wrong value for the stamina. The game
  faulted on it and left an entry behind in its own stat bookkeeping, one per
  read. Every later stat look-up in the game, its own included, had to walk all
  of them. After a few minutes near several people (a fight in a village, a
  market) the frame rate fell from about 75 to under 10 and stayed there until
  the game was restarted. Every read now gets the right value.
- With it: the stamina read for the second and later person near you is right
  now. Before, a stamina-only blow on them could be missed, or a blow that never
  happened could be reported.

## Off switches (the console, `~`)

Unchanged from 0.42.7: `mp_carry_sync`, `mp_carry_objects`, and the earlier
ones listed in `docs/releases/RELEASE-NOTES-0.42.7.md`.

## Everything from 0.42.7, 0.42.5, 0.42.2 and 0.42.0

`docs/releases/RELEASE-NOTES-0.42.7.md`, `docs/releases/RELEASE-NOTES-0.42.5.md`,
`docs/releases/RELEASE-NOTES-0.42.2.md`, `docs/releases/RELEASE-NOTES-0.42.0.md`.

## Still known

- The partner's carrying does not count for the host's quests yet (a burial,
  sacks to deliver); crimes for carrying a body are not shared yet.
- There is no throw in the game; "drop" is the put-down.
- Where the host's animals stand can differ on the partner's screen until they
  come close.
- A cutscene plays for each player separately; on the partner's screen it can
  stay black until the host's next step.
- An escort (the sheep in "Find Mutt!") follows only the player who leads it.
- The partner does not hear the host whistle, and the other way round.
- The journal's marker letters can differ between the two players.
- At the grindstone the blade is not in the other player's figure's hands.

---

Copyright (C) 2026 the Kingdom Come: Together contributors ([AUTHORS](../../AUTHORS)).
GPLv3 with the section 7 additional terms in [NOTICE](../../NOTICE): keep the
credits and the official-repository notice; a modified version must say it is
not the official one.
