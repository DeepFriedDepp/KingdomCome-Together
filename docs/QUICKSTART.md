# Quick start

From nothing to playing together, in four steps. You need Windows and Steam.

## 1. Install

Run **`KingdomComeTogether-Setup-<version>.exe`**. Click through it; it needs no
administrator rights. At the end, leave **"Launch Kingdom Come: Together now"** ticked.

You don't need anything else first. If Kingdom Come: Deliverance II or its free
**Modding Tools** aren't installed yet, Setup says so and the launcher takes care of it.

**What installs itself:** the launcher brings its own .NET runtime (you do **not** install
.NET), Setup installs Microsoft's WebView2 runtime if it is missing, and Steam installs the
game and the Modding Tools from the launcher's checklist. The checklist also offers Microsoft's
**Visual C++ 2013 runtime** when it is missing: the Modding Tools need it (without it Windows
says "MSVCP120.dll was not found" when the game starts); the mod itself does not.

**If Windows removes or blocks a mod file**, the launcher says which file, where it looked, and
what to do (`docs/releases/` "If you see…"). The mod never changes your Windows security
settings and never adds exclusions: you allow a file yourself, in Windows Security or your
antivirus.

## 2. Open the launcher

If everything is already set up, the launcher opens to its normal screen, and you can go
straight to step 4.

## 3. Follow the checklist (first time only)

If something is missing, the launcher shows a short checklist and does each step for you,
in the background. You can keep using your PC; the list updates by itself.

- **✅ done · ⏳ working · ❌ needs you.** A ❌ step says what to do in one sentence.
- **Steam's install window** may open for the game or the free *Kingdom Come: Deliverance II
  Modding tools*. Click **Install** there. The download can take a while; the checklist
  shows its progress, and you can close the launcher and come back later. It picks up where
  it stopped.
- **Windows may ask for permission once** ("Windows needs your permission to link the game's
  files into the Modding Tools"). That only happens when the game and the Modding Tools are
  on different drives. Choose **Yes**.
- Nothing else needs you: no console, no typing, no "Workspace Setup".

When every step shows ✅ the list says **Ready!** Click **PLAY**.

## 4. Host or Join

When the launcher starts the game, the game's main menu shows the Kingdom Come: Together
logo and **Start Game** and **Join Game** at the top. Continue, New Game and Load Game are greyed out while Kingdom Come:
Together runs them (start the game from Steam to play alone as usual).

- **Host:** click **HOST GAME**, then share the address the launcher shows. Click
  **PLAY**. In the game, press **Start Game**:
  - one of **your worlds** (your own Modding Tools saves, one line per save slot, newest
    first) — it loads where you left it; or
  - **New adventure** — a new world that starts as Henry **where Hans and Henry part
    ways** (the whole prologue is behind you: the ride, the ambush, the herbalist, the bar
    fight, the pillory). Choose your **playstyle** (Soldier, Adviser or Scout — each says
    what it raises), then **Skip the prologue** (recommended when a partner is joining: they
    can join right away) or **Watch the prologue's cutscenes** (16 minutes of its rendered
    cutscenes, no conversations or choices; **skipping one skips them all**; your partner joins
    when they end). It goes into an empty save slot and saves normally from then on; each
    new adventure is its own world.

  You don't need to switch back to the launcher: once your world has loaded and settled
  (about 10 seconds), the launcher connects by itself. If it can't, the game says so on
  screen and the launcher window says why.
- **Join:** click **ADD SERVER**, enter your host's address, then **JOIN** (or use
  **JOIN THROUGH STEAM** with your host's code). Click **PLAY**. In the game, press
  **Join Game**, then **Join with a new character** and its playstyle (your Henry starts
  where Hans and Henry part ways; you need no save of your own), or **Bring my character**
  if you have a Modding Tools save of your own. If your host isn't in their world yet, the menu says
  **Waiting for the host…** and you join as soon as they are (**Cancel** stops waiting).
  Your host's world comes to you, and your character lands beside the host. No CONNECT
  click on either side.

**Which save?** You don't need one. The **host** picks one of their worlds or a New
adventure; the **partner** never needs a save of the host's world. Saves the menu does not
show (from the regular game, copies of a host's world you joined, still in the prologue)
are named under the list.

**The game asks you to accept two pages first?** Kingdom Come: Deliverance II shows its
licence and telemetry pages the first time it starts. If you haven't accepted them yet, the
launcher says so: start the game once from Steam (the Modding Tools entry), accept them,
close it, and press Host or Join again.

Same house: that's all. Different houses: see [NETWORKING.md](NETWORKING.md).

## In the game

Press **Insert** for the mod menu: every setting in plain words, "I'm stuck" if
something holds you, and "Something's wrong here" to mark a moment for a bug report. See
[MOD-MENU.md](MOD-MENU.md). Fast travel is off while you play together unless the host
turns it on there, and voice chat is off unless you turn it on.

## If something breaks later

A Steam update, or a game that moved to another drive, can undo part of the setup. The
launcher notices when it opens and shows only the steps that need doing again. You can also
open the list any time with **CHECK SETUP** at the bottom of the launcher. Still stuck?
**REPORT BUG** collects the logs (personal details removed) into one zip to send.
