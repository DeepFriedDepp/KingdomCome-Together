# Kingdom Come: Together 0.45.6 — New adventure works: the start saves are in

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

0.45.5 plus the three bundled start saves (WO-159, 2026-10-07). The installer, `KingdomComeTogether-Setup-0.45.6.exe`,
comes from the maintainer. The evidence is `docs/WO-159-findings.md`.

**Both machines and the relay must run the same build.** It refuses every other version at the handshake.

---

## New

- **New adventure works.** Start Game → New adventure → Soldier, Adviser or Scout → Skip the prologue (or watch its
  cutscenes). You start as Henry in Troskowitz, where Hans and Henry part ways, with the playstyle you chose.
- **Join with a new character works without a save of your own**: the partner picks a playstyle and starts from the same
  point.
- All three start saves come from one playthrough, so the prologue went the same way in each; only the playstyle's
  starting skills differ (exactly what the game's own choice in the prologue gives).

## Fixed

- With no start saves installed, New adventure's line said "run Setup again"; it now says the start saves come with the
  installer.

## Known issues

- Those of 0.45.5 (`docs/releases/RELEASE-NOTES-0.45.5.md`).
- **Not soak-tested** (the maintainer's decision; the waiver is recorded beside the installer). Not signed yet.
