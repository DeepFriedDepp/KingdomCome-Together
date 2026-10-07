# Kingdom Come: Together 0.45.7 — Start Game connects the host by itself, every time

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

0.45.6 plus one fix from its first live session (WO-159, 2026-10-07). The installer,
`KingdomComeTogether-Setup-0.45.7.exe`, comes from the maintainer. The evidence is `docs/WO-159-findings.md`.

**Both machines and the relay must run the same build.** It refuses every other version at the handshake.

---

## Fixed

- **The host is connected after Start Game every time.** In 0.45.6 the launcher could try to connect a second before
  its own CONNECT button became ready; the try was refused, never repeated, and the game said the connect had failed
  (the launcher sat at CONNECT). The launcher now waits for that moment itself. There is still no need to Alt+Tab.
- When the game closes, the launcher removes our menu logo again even if the game still holds the file for a moment
  (it retries for up to 15 seconds).

## Known issues

- Those of 0.45.5 and 0.45.6 (`docs/releases/RELEASE-NOTES-0.45.5.md`, `docs/releases/RELEASE-NOTES-0.45.6.md`).
- **Not soak-tested** (the maintainer's decision; the waiver is recorded beside the installer). Not signed yet.
