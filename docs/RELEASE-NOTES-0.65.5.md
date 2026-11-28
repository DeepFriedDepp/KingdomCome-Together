# Kingdom Come: Together 0.65.5 beta

**Official repository: the project's own GitHub repository, KingdomCome-Together** — only builds made from it are this project's
releases; other repositories may carry the same name. **From this build on, every build says which it is**: the launcher's status
line, the in-game mod menu and a bug report's first lines read **"Official build 0.65.5"** or **"Unofficial build — not made by
Kingdom Come: Together"** (see "Official and unofficial builds" below).

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or PLAION. Kingdom Come: Deliverance II, its assets and its
content belong to Warhorse Studios and PLAION; this project's copyright covers only its own code.

The installer is `KingdomComeTogether-Setup-0.65.5-<build id>.exe`. What to try together is in `docs/TESTER-SCRIPT-0.65.5.md`; what
we know is not right yet is in `docs/KNOWN-ISSUES.md`; the evidence is `docs/WO-222-findings.md`, `docs/WO-223-findings.md` and
`docs/WO-224-findings.md`.

**Both players must install the same build.** Builds are told apart by their **build id** (the launcher's About shows it, and so does
the installer's name); a player on a different build is told so at the connection.

## This build: fights for two

Two testers on 0.65.2 said fights felt fluid, with problems: enemies never flinched at the partner's blows, enemies did not notice
the joiner until he stood among them, a grab left a bandit stuck, and fast combos landed blows the joiner never saw. This build is
about those. Everything below is **on** by default; each part has its own switch (`mp_w222_status`, `mp_w223_status` and
`mp_w224_status` in the console list them).

### Hits land for both players

- **The joiner's blows make enemies flinch.** In 0.65.2 an enemy fighting the joiner took his hits without a reaction: the host's
  game held such an enemy "out of combat mode", and the game plays no hit reaction on a body in that state. It is in combat mode at
  his blows now, and reacts as it does to the host's.
- **The host's hits show on the joiner's screen**: the enemy's flinch plays on the joiner's copy of it.
- **A fleeing enemy stays an enemy.** A runner runs on its own legs on the joiner's screen and takes his blows ("unkillable"
  runners were the mod dropping his hits on them).
- **A blow you blocked or dodged on your own screen is not charged to you later.** The joiner's own game judges the blows on him;
  where the host had to decide first, the joiner's later verdict stands and the host's is taken back.

### Enemies notice the joiner

- **A hostile camp or patrol makes the joiner out as it would the host** — by the game's own measure of how well he can be seen
  and heard, with his own crouch, light, noise and stealth skill — then draws and fights him in a real fight of the host's game.
  Before, he could walk into a camp of six and stand among them.
- **Both players at a camp each get an enemy** within a few seconds, instead of the whole camp taking one of them.
- **An enemy that fights the joiner on the host's screen fights him on his own screen**, also after it changed over from the host
  and when both players are on the same enemy.

### Grabs

- **A grab plays on both bodies on both screens, and ends on both.** When an enemy grabs the joiner — or the joiner grabs an
  enemy — both games now run the game's own clinch between the same two bodies: you are held on your own screen while your
  figure is held on your partner's, you can struggle and strike in it, and when it ends on one screen it ends on the other. In
  0.65.2 the joiner stood free on his own screen while his figure was grabbed on the host's, and a bandit could stay stuck in a
  grab. On our bench 19 of the host's 20 grabs and all 23 of the joiner's showed on the other screen within 1.5 seconds (most within a tenth of a second).
- **An enemy that changes its weapon** fights with the new weapon's style on the joiner's screen at once (its hands may show the
  old weapon for some seconds).

### Combos

- **An enemy's chain of swings plays on the joiner's screen as the same chain.** Until now the copy of an enemy swung from the
  mirrored side in three swings of four, fell behind in every chain, and never played a combo's finisher: the joiner blocked a
  blow from the left that came from the right, and the host had to guess the blows he never saw. Now the copy starts each swing
  from the guard the host's enemy starts it from and chains when the host's enemy chains. On our bench the copy plays the host's enemy's own swing in 76 to 86 % of swings (10 % or fewer before) and starts it from the same side in 98 to 100 %.
- What you may notice: an enemy whose swing you dodged goes on with its chain, because on the host's screen that swing counted.

### Dead enemies fall sooner

- **An enemy killed next to you lies down about a second after its death** on the joiner's screen (it stood frozen for three).
- **Fewer closed games after deaths.** The joiner's game closing after people died near him had one thing in common on our
  bench: the mod itself threw the dead body down two and a half seconds after the death. It no longer does anything to a dead
  body for six seconds. On our bench, five runs of 40 deaths each around the joiner closed his game none of the times (the old way: 2 times in 7). It is not proven gone (see Known).

### Official and unofficial builds

- **Every build says whether it is this project's own.** The launcher's status line and About box, the in-game mod menu (the
  line under its title) and the first lines of a bug report read "Official build 0.65.5" or "Unofficial build — not made by
  Kingdom Come: Together".
- An official build is one the maintainer made and signed; the mod checks that signature itself, offline. A fork's build, a build
  somebody changed and a development build read unofficial. **Nothing is refused**: an unofficial build runs and connects to
  anyone, and nothing is sent anywhere. `docs/OFFICIAL-BUILDS.md` has the details.
- If someone gives you an installer that reads "Unofficial build", it did not come from this project. That may be fine — forks
  are allowed — but it is not ours, and its bugs are not ours to fix.

### Bug reports

- A bug report now says who was noticed first at a camp, how long enemies fought the wrong player on your screen, how many of
  either player's swings touched a body, and whether the build is official.

## Known

`docs/KNOWN-ISSUES.md` is the full list. The ones you are most likely to meet:

- **The joiner's game can still close after people die near him.** Start the game again and send the bug report.
- **Some blows on the joiner are still the host's guess**, not his own game's verdict. On our bench about 1 or 2 blows in 100 in camp fights, about 5 in 100 over a long session of fights, and 2 to 7 in 100 in a duel where the joiner dodges a lot (most of those from one enemy that changed weapon in the fight).
- **A combo's finisher** is sometimes a plain blow on the joiner's screen.
- **An enemy's strike out of a grab** is a plain strike on the joiner's screen, not the paired one.
- **Now and then a camp does not notice the joiner**, and its warnings are not heard by him.
- **A dead body the game does not lay down at once** (one that sat or knelt, or one far away) stands for six to ten seconds.
- **Villagers who sit or lie are a little more often a seat or a bed off** on the joiner's screen than in 0.65.2 (our bench: in the
  host's place 72 % of the time against 82 %); the cause is not found. Walking and work at objects are as in 0.65.2.

## Switches

`mp_w224_status` lists this build's: `mp_w224_clinch` (grabs), `mp_w224_chains` (combos), `mp_w224_death` (dead bodies),
`mp_w224_official` (the official-build line; off reads unofficial). `mp_w223_status` and `mp_w222_status` list the ones for
noticing and for hits. Each defaults to on; `<switch> off` in the console gives the earlier behaviour back for that part.
