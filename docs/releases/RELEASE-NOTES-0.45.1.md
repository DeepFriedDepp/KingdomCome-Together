# Kingdom Come: Together 0.45.1 — hits never knock you down

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

A small update to 0.45.0 (WO-155, 2026-10-03). The installer,
`KingdomComeTogether-Setup-0.45.1.exe`, comes from the maintainer. The tester
page is `docs/TEST-0.45.1.md`; the evidence is `docs/WO-155-findings.md`.

**Both machines and the relay must run the same build.** 0.45.1 refuses every
other version at the handshake, 0.45.0 included.

**Verified:** solo on one machine, against a scripted partner or a synthetic host, on throwaway saves, in
the game, with frames where it is visual (`docs/WO-155-findings.md`); every gate. What needs two people is
`docs/TWO-PLAYER-CHECKLIST.md` §WO-155.

---

## Changed

- **A hit never knocks a player down.** In 0.45.0 every blow of an enemy or an
  animal on the joining player knocked him down (ten times in a few minutes in one
  evening, 3–7 s each, and he never got a swing in). Now a blow takes exactly its
  health and stamina and nothing else; a death still kills. The host's own game is
  unchanged (his enemies' blows on him are the game's own). `mp_hit_knockdown on`
  puts the old behaviour back.
- **A figure falls on the other screen only when its player dies.** It lies where
  it fell, on the ground, until the player respawns; then it vanishes and a fresh
  figure stands where they woke. (The 0.45.0 fall used a ragdoll and a revive: the
  figure sank into the ground and stood up in a T-pose.) `mp_avatar_falls off`
  brings back the old way of hiding it.
- **Friendly fire**: a hit from your friend still knocks you down on your own
  screen (the fun part), but **never twice within 5 seconds**: the next hits take
  their health and stamina without a second fall. On the attacker's screen the
  victim's figure falls and gets up with the game's own knockdown and get-up
  animations. `mp_ff_knockdown off` makes friendly fire never knock down.

## Fixed

- **The T-pose.** A figure of your partner stood with its arms out for 5–8 seconds
  after it spawned (a join, a respawn) and after it got back up. The mod now starts
  its animation with one small step.
- The log's `ok=true` for a fall that looked wrong: the new lines (`WO155-FALL`,
  `WO155-STATE`, `WO155-RISE`, `WO155-COLLAPSE`) say what the game's own animation
  state was.

## Known issues

- Those of 0.45.0 (`docs/releases/RELEASE-NOTES-0.45.0.md`): cutscenes are not shared
  yet; the tutorial is the roughest part; no lip sync; the partner's herb picking
  shows them standing; fast travel is off during co-op unless the host turns it on;
  conversations aren't heard by the other player; if a join's load ever freezes the
  game, close it and join again.
- **A knockout** (the game's own unconsciousness) shows no fall on the other screen:
  the figure stays as it is until the player is up.
- **Swinging and blocking between a guard's blows** was not shown solo (no keys in
  the test session); the two-player checklist covers it.

---

Copyright (C) 2026 the Kingdom Come: Together contributors ([AUTHORS](../../AUTHORS)).
GPLv3 with the section 7 additional terms in [NOTICE](../../NOTICE): keep the
credits and the official-repository notice; a modified version must say it is
not the official one.
