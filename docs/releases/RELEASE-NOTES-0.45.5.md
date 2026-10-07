# Kingdom Come: Together 0.45.5 — Start Game, Join Game, no prologue

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

What changed since 0.45.2 (WO-158, WO-159, 2026-10-07). The installer, `KingdomComeTogether-Setup-0.45.5.exe`, comes
from the maintainer. The evidence is `docs/WO-159-findings.md`.

**Both machines and the relay must run the same build.** It refuses every other version at the handshake.

---

## New

- **Start Game and Join Game on the game's main menu.** When the launcher starts the game, its main menu shows
  **Start Game** and **Join Game** at the top, with our logo. Continue, New Game and Load Game are greyed out while
  Kingdom Come: Together runs them, and the Modding Tools' debug entries are gone. A game you start from Steam keeps the
  game's own menu.
- **No prologue to replay.** Start Game → **New adventure** begins a new world as Henry right where Hans and Henry part
  ways: the ride, the ambush, the herbalist, the bar fight and the pillory are behind you. First you **choose your
  playstyle** (Soldier, Adviser or Scout — what the game itself asks in the prologue), then **Skip the prologue**
  (recommended when a partner is joining) or **Watch the prologue's cutscenes** (16 minutes of the prologue's rendered
  cutscenes, no conversations or choices; **hold E** to skip the rest). Each new adventure is its own world, in an empty
  save slot, and saves normally.
- **Your own worlds** are on the same page (one line per save slot, newest first); saves that can't be used are named
  under the list (from the regular game, copies of a host's world, still in the prologue).
- **Join Game → Join with a new character** asks for a playstyle too and starts your Henry where Hans and Henry part ways —
  you need no save of your own. **Bring my character** is there when you have one. Pressed before your host is ready:
  "Waiting for the host…" with Cancel, and you join by yourself when they are.
- **Nobody clicks CONNECT any more.** The host is connected by the launcher once the world has loaded; the joiner as soon
  as the host is in. No switching back to the launcher.
- **If your host is watching the prologue**, you are told how long is left and join when it ends or they skip it.
- **The game's two first-run pages**: if they were never accepted on this computer, the launcher says to start the game
  once from Steam and accept them (it never accepts them for you).
- The partner's **name badge** hangs at the neck (WO-158), and the player's own dog is never paused by the mod.

## If you see…

- **New adventure is greyed out: "No start save is installed"** — this build ships without the bundled start saves, or one
  playstyle's is missing (that playstyle is greyed). Run the newest Setup when one with start saves is out.
- **New adventure is greyed out: "All five save slots (playlines) hold saves"** — a new adventure needs an empty save slot.
  Start the game normally from Steam and delete a playline you no longer play in its Load Game list.
- **"Start the game once from Steam and accept the two pages"** — the game's licence and telemetry pages have never been
  accepted on this computer. Start the Modding Tools entry from Steam, accept both, close the game, press Host or Join
  again.
- **"Kingdom Come: Together could not connect"** on the game's screen — switch to the launcher window: it says why.

## Known issues

- Those of 0.45.2 (`docs/releases/RELEASE-NOTES-0.45.2.md`).
- **This build has no bundled start saves yet** unless the maintainer's notes say otherwise: New adventure and "Join with a
  new character" from a bundled save need them (`assets/start-save/README.md`).
- The "Hold E to skip" line shows on the screen for a few seconds before the cutscenes (the game draws its cutscene
  videos over everything else); the cutscene page in the menu says it too.
- **Not soak-tested**: built without the frame-rate soak on the maintainer's decision (the waiver is recorded beside the
  installer). Not signed yet.
