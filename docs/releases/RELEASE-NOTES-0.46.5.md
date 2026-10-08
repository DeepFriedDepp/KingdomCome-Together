# Kingdom Come: Together 0.46.5 — shared combat gets its bookkeeping

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

0.46.0 plus WO-161 (2026-10-08). The installer, `KingdomComeTogether-Setup-0.46.5.exe`, comes from the maintainer. The
evidence is `docs/WO-161-findings.md`; what to check live is `docs/TWO-PLAYER-CHECKLIST.md`, items 162–170 (and 150–161 from
0.46.0, still pending).

**Both machines and the relay must run the same build.** It refuses every other version at the handshake.

---

## Changed

- **Every blow an enemy lands on a player is numbered, applied once, and written to the log with the swing it came from —
  or the reason there was none.** The host sends one message per blow (a new message type on the join channel, the next free
  pair) instead of the bare 0.46.0 hit; the joiner applies each blow once, exactly as before (no knockdown, never while he is
  down or waking), and says in the log whether the enemy's swing was shown on his screen with it. `WO161-HIT` on both
  machines and `MP-WO161-STATS` once a minute carry it. The old path is the fallback (`HitVerdictEnabled` in
  `kcdmp-client.json`, or `--no-hit-verdict` on the agent).
- The repository root is tidier (field notes and the Setup dry-run moved; the brand pak's source now lives beside its
  artwork). **README and `docs/LAUNCHING.md` are rewritten to what is true now** (Start Game / Join Game, no CONNECT click; the
  launcher has driven every tester session since 0.45.0; the master server is the C# one).

## What did not change, and why (`docs/WO-161-findings.md`)

- **The host's game still decides whether a blow hits or is blocked**, against its copy of the joiner. The joiner's own
  block is not asked. The reason is measured: a joiner's copy of an enemy is a puppet and fires no hit event on the joiner
  (none in 161 of the testers' log windows, with 35 blows landing on the partner in the same minutes), so the joiner's game
  cannot judge a swing it never receives as a hit. Calling the game's own hit routine with a built hit is the next work order's
  research, with its gates written down.
- **Shared targeting and the snapping fixes were not built.** The host being able to lock onto a guard who is beating the
  partner rests on an engine rule that was not read; the three snapping causes are located in the code, but a fix is kept
  only if a fight measurement moves, and that measurement needs the game's window in front. Both are designed in the findings.

## Known issues

- In the testers' fights about **one enemy blow in three** (16 of 45) reached the player with no swing shown on the partner's
  screen — the host's game made a blow the mod did not capture. It is now counted and named in the log; not fixed.
- Set-piece brawls, a killed person standing up again on the other screen (the standing dead copy), people left naked after a
  wait or a sleep (fixed in 0.46.0, waiting for the testers), bows and arrows, the host's lock-on to a guard beating the
  partner, two players on one enemy, and a small step or snap of the other player's figure in a fight: as in 0.46.0.
- **Not soak-tested** (the maintainer's standing rule; the waiver is recorded beside the installer). No file of the mod's
  Lua or of the native plugin changed since 0.46.0 (the commits show none), so nothing in the game's process changed — the
  change is in the agent, the relay and their shared wire. Not signed unless the build says so.
