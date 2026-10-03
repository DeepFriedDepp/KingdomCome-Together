# Kingdom Come: Together 0.44.0 — what the tutorial session showed

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

Everything on `main` up to WO-153 (2026-10-02). The installer,
`KingdomComeTogether-Setup-0.44.0.exe`, comes from the maintainer and is not on
GitHub. The tester page is `docs/TEST-0.44.0.md`; the evidence is
`docs/WO-153-findings.md`.

**Both machines and the relay must run the same build.** 0.44.0 refuses every
other version at the handshake, 0.43.0 included.

**Verified:** by unit and synthetic tests only (every synthetic suite, the agent,
relay and native tests, the static checks) against the real mod and agent code.
**Not run in the game yet**, and the frame-rate soak was **skipped on the maintainer's
instruction**: there is no frame-rate comparison against the game without the mod for this
build. Anything that looks like a frame-rate drop is worth a report with the `FRAME` lines
of `kcdmp-native.log`.
What needs two people is listed in `docs/TWO-PLAYER-CHECKLIST.md` (WO-153).

---

## Fixed

- **A game crash while picking herbs.** Both of the partner's crashes in the
  tutorial ended when the other player's figure stopped its herb-picking animation
  at the moment the partner started picking too. The figure now stands while its
  player picks herbs (`mp_avatar_herbs on` brings the animation back, for a test).
- **A village dog attacked the partner** (the dog is never streamed from the host,
  so its own brain ran). Domestic dogs are now held still on the partner's screen.
- **Being pulled into the host's fights.** An enemy fighting only the host's
  character no longer puts the partner into combat with it.
- **A killed enemy that could stay standing** on the partner's screen, because a
  late "still alive" message undid the death's bookkeeping. Fixed; a kill that does
  not land says so in the log and is asked again.
- **Commands to the game lost in a stall.** While the game loads or hangs, what the
  agent sends is kept and sent again in order instead of being dropped.
- **Two writers on one NPC** after a hand-over (the "an NPC is being moved by this
  machine's own AI" notice): fixed, and the notice no longer shows on screen.
- **Doors flapping** open and shut right after a join.
- **Tools given and taken again and again** to villagers; every take also logged a
  script error.
- **A repeated cart-mount retry** for caravan horses (104 in one session).
- **A native fault** read in a loop (thousands a minute) after a long sleep or a
  clock jump.

## Changed

- The partner's figure **stands** while its player picks herbs (see above).

## Still known

- The engine's cause of the herb crash is not found; the figure simply does not
  play that animation.
- Shared-quest steps with no known port can leave the partner stuck (a seat after
  a sleep, counters that drift in a sack-carrying quest); `mp_unstuck` does not free
  him from a scene.
- The host's friendly fire shows no reaction on the partner; a wolf's bite is not
  shown; conversations and barks are not heard by the other player.
- Cutscene props at the world's origin, the tutorial's respawn spot and lip sync
  (the Modding Tools build lacks the data) are not part of this release.
