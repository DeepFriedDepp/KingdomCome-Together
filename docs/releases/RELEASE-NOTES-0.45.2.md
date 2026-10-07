# Kingdom Come: Together 0.45.2 — the first public-beta patch

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

The fixes for what the first public testers reported on 0.45.1 (WO-157, 2026-10-06). The installer,
`KingdomComeTogether-Setup-0.45.2.exe`, comes from the maintainer. The evidence is
`docs/WO-157-findings.md`; what a two-player session checks is `docs/TWO-PLAYER-CHECKLIST.md` §WO-157.

**Both machines and the relay must run the same build.** It refuses every other version at the handshake.

---

## What you need

Windows and Steam. **Setup installs everything else the mod needs**: the launcher brings its own .NET runtime (you do
**not** install .NET yourself), and Setup installs Microsoft's WebView2 runtime if it is missing. Steam installs the game
and the free *Kingdom Come: Deliverance II Modding Tools* (the launcher's checklist opens Steam's install window for you).
The checklist also offers Microsoft's **Visual C++ 2013 runtime** when it is missing: the Modding Tools need it (not the
mod), and without it Windows says "MSVCP120.dll was not found" when the game starts.

## Fixed

- **Shops no longer count as trespassing for the joining player.** The host's own world decides: an open shop is open,
  a house at night is private. The joiner's own game no longer shows its (wrong) trespass warning during co-op, and the
  host is no longer punished for a trespass that never happened.
- **A crime that wasn't does not follow you into the next session**: a returning player's old trespass reports are
  forgotten. And the first time you walk away from a guard who stops you, it doesn't count as fleeing: you are told what
  it means, and the next time it does.
- **"A MOD FILE WAS REMOVED: KCDMP_LauncherInjector.exe"**: that file is gone. The launcher now does its job itself, so
  there is one program fewer for an antivirus or Smart App Control to remove or block.
- **"kdcmp.pak is gone" when it isn't**: the launcher looked in the wrong folder on a Modding Tools install that had never
  been started. It now looks where Setup put the mod, says exactly what it found and which file it checked, and offers
  **LAUNCH ANYWAY**.
- **Groups starting from the same save** can now join each other: a player who hosted that start save can bring his own
  character to someone else's game of it, and "Join with a new character" works from the start save too. Two hosts of the
  same start save are two different worlds to a joiner.
- **Steam codes** are accepted with spaces, odd dashes, quotes or text around them; a wrong code says what a code looks like
  and where your host finds it.
- **The joiner wasn't hurt by enemies** (about 20 blows, no damage): a blow now costs him what it would cost anyone.
- **The other player's figure flooded the game's animation queue** (thousands of warnings, stutters): its own look-around
  animation is off.
- **After talking to the blacksmith, nobody would talk**, and Trade closed the conversation: the host's characters on the
  joiner's screen are freed before a conversation.
- **Sleeping together**: one player waking no longer cuts the other's sleep short, and a sleep the game gave no rest is
  given the rest the game's own sleep gives.
- **A host crash when the partner rode far away** (the game's horse mount, 2 km off): no mount that far.
- The fast-travel line was read as an error: it now says plainly "Fast travel is turned off for co-op. The host can turn
  it on in the mod menu (Insert)."

## New

- **The connection, said plainly.** When the connection to the other player struggles, the mod menu's Connection line
  says so (with the ping), and after 30 seconds one line on screen says it is the connection, not the game.
- **"Your host's game saved, and your character with it"**: the joining player is told that his inventory and progress
  are saved with the host's saves.
- The log's `MP-GHOSTCORR` counts snaps per 10-second window, per figure and per session.
- Setup and the launcher are ready for **code signing** (`docs/CODE-SIGNING.md`); this build is
  **not signed yet** (the signing account is being set up), and it is a **release candidate**: not soak-tested, for the maintainer's two-player session before the public release.

## If you see…

- **"MSVCP120.dll was not found" (trace_server.run.exe)** — the Modding Tools' trace server needs Microsoft's Visual C++
  2013 runtime. Open the launcher's checklist (CHECK SETUP): the step "Visual C++ 2013 runtime" installs it from your
  Steam library (Windows asks once), or opens Microsoft's download page. The game and the mod run without it.
- **"A MOD FILE WAS REMOVED" / "A MOD FILE IS MISSING"** — the message names the file and the full path the launcher
  checked, and whether the file is missing, empty, locked or different from what Setup installed. Usually Windows Security
  or another antivirus removed it: open **Windows Security > Virus & threat protection > Protection history**, restore the
  file and **Allow** it (or do the same in your antivirus), or run Setup again. It can be removed again until it is
  allowed. If you are sure the file is there, check the game path in the launcher's Settings, or click **LAUNCH ANYWAY**:
  the game starts, and the mod tells you in the game if it really is missing.
- **"Windows blocked a file the mod needs" (Smart App Control)** — Smart App Control blocks programs without a publisher
  signature and has **no exclusions**: allowing a file or excluding a folder does not help against it. Its blocks show as
  a "Smart App Control blocked…" notification (Windows Security > App & browser control); a Windows Security quarantine
  shows in Protection history instead. The mod never changes Windows' security settings; whether to keep Smart App
  Control on is your decision.
- **"The connection is struggling"** — the ping between the two of you is high or jumping (often Wi-Fi, a busy upload, or a
  long distance). Movement then lags or jumps. A wired connection, or the host closing uploads and streams, helps.
- **A guard stops you about a crime you don't understand** — walk away once: it is not fleeing the first time, and the
  message says what the next time means.

## Known issues

- Those of 0.45.0 and 0.45.1 (`docs/releases/RELEASE-NOTES-0.45.0.md`, `docs/releases/RELEASE-NOTES-0.45.1.md`):
  cutscenes are not shared yet; the tutorial is the roughest part; no lip sync; conversations aren't heard by the other
  player; fast travel is off during co-op unless the host turns it on.
- **Random events happen separately on each machine** (a caravan, a duel): on the joiner's screen some of the host's
  people in them can vanish or be replaced. A later update shares them.
- **The joining player's own game still thinks the shops are closed** (only its warning is hidden): the host's world
  decides.
- Whether the joining player blocked a blow is not yet his own game's decision: the blow lands as dealt.
