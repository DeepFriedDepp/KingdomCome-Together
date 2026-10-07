# Kingdom Come: Together 0.45.8 — The prologue's cutscenes with their sound

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

0.45.7 plus two fixes from its first live session (WO-159, 2026-10-07). The installer,
`KingdomComeTogether-Setup-0.45.8.exe`, comes from the maintainer. The evidence is `docs/WO-159-findings.md`.

**Both machines and the relay must run the same build.** It refuses every other version at the handshake.

---

## Fixed

- **"Watch the prologue" now plays the cutscenes like the game does.** In 0.45.7 their sound was barely audible and the
  world's sound played on underneath. Each video now plays through the game's own cutscene player, which sets the sound
  up as in the base game, pauses the world (no world sound underneath) and shows the narrator's captions.
- **Skipping a prologue cutscene skips them all.** In 0.45.7 the game's skip ended only that video, and the next one
  started when the skipped one would have ended. Hold E did nothing during a video (the video takes the keys).

## Known issues

- Those of 0.45.5–0.45.7 (`docs/releases/`).
- Neither fix has been seen in a live session yet. If the music after the recap is battle music where it shouldn't
  be, report it.
- **Not soak-tested** (the maintainer's decision; the waiver is recorded beside the installer). Not signed yet.
