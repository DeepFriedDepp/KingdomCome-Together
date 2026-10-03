# Kingdom Come: Together 0.45.0 — the public beta: setup that does itself, questing together, fighting together

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

Everything on `main` up to WO-154 (2026-10-03). New since the last public build
(0.42.x): WO-150, WO-151 (0.43.0), WO-153 (0.44.0) and WO-154. The installer,
`KingdomComeTogether-Setup-0.45.0.exe`, comes from the maintainer. The tester
page is `docs/TEST-0.45.0.md`; the evidence is `docs/WO-154-findings.md`.

**Both machines and the relay must run the same build.** 0.45.0 refuses every
other version at the handshake. 0.43.0 stays available as the fallback (the tag
`v0.43.0`).

**Verified:** solo on one machine, against a scripted partner or a synthetic host, on throwaway saves:
every phase's fix ran in the game, with frames where it is visual (`docs/WO-154-findings.md` §9.3); every gate;
two 10-minute frame-rate soaks of the mod, the second with a joined partner through six fights (71–74 FPS, no faults; the comparison with the game without the mod skipped on the maintainer's call); the install pass (an upgrade from 0.43.0 or 0.44.0 keeps every launcher setting byte for byte). What needs
two people is `docs/TWO-PLAYER-CHECKLIST.md` §WO-154.

---

## New

- **Setup does itself.** Install, open the launcher, and its checklist does the
  rest in the background: it finds the game and the free Modding Tools, links the
  game's files into the Modding Tools (no Workspace Setup, no console), and says in
  one sentence when it needs you (Steam's own install window, or one Windows
  permission when the game and the Modding Tools are on different drives).
- **The mod menu.** Press **Insert** in the game: every setting in plain words, with
  its value and a one-line explanation, instead of console commands. The host's
  settings show on the partner's screen as **[set by the host]**. "I'm stuck" and
  "Something's wrong here" are in it. Your choices are remembered. See
  `docs/MOD-MENU.md`.
- **Questing together.** Quest steps now go both ways: what happens in the host's
  world reaches the partner's game, and the partner's own steps (a duel, a brawl, a
  battle) reach the host's world. (Until now most of them were lost: in one evening
  the partner's game applied 5 of 54 of the host's steps.)
- **Fighting together.** When you hit an enemy that is fighting your partner, it
  reacts to you and turns on you the way it would in the game. When your partner
  falls or respawns, every fight against them ends, and guards leave them alone for
  two minutes after they get up. A crime is judged on what really happened (a
  practice punch in a quest brawl is no assault, a murder needs a death).
- **Knocked down together.** When a player is knocked down, their figure falls on
  the other screen too, lies there, and gets up when they do.
- **Joining that looks like it works.** The host sees the partner's progress through
  the whole join (the game's own panel, even while the world is held). A slow load
  is waited out, never given up while the game is busy. Plain messages when a join
  can't work ("Your saves are from the regular game, not the Modding Tools", "Your
  only Modding Tools saves are copies of this same world") and a **Join with a new
  character** button where it helps. CONNECT waits until it can work and says why.
- **Smooth riding.** A partner's horse is moved every frame, with the game's own
  walk, trot and gallop (measured: frozen frames 50 % → 0 %, the horse's and the
  rider's animations no longer fight).
- **Windows blocking the mod, explained.** When Windows blocks the mod (Smart App
  Control, an Application Control policy, a quarantined file), the launcher says
  what happened and what you can do, including Smart App Control's catch: once
  turned off it cannot be turned back on without resetting Windows.

## Changed

- **Fast travel is off during a co-op session** unless the host turns it on in the
  mod menu (so nobody is left behind). It is as before when you play alone.
- **Voice chat is off** unless you turn it on (the launcher's Settings or the mod
  menu). Most players use their own voice chat. If you had turned it on before,
  turn it on once more; after that your choice is kept.
- **Your partner's figure stands while they pick herbs** (the animation crashed the
  game; `mp_avatar_herbs on` brings it back).
- An upgrade keeps every launcher setting exactly as you had it: your servers, your
  name, your game path, your relay and network choices.

## Fixed (the highlights)

- A join whose load froze the game for good (the game's video player stopped at the
  loading screen): the main menu's video is now stopped before the load, and if the
  game still freezes the launcher says so plainly (restart the game and join again)
  instead of waiting forever; the host gets their world back at once.
- The partner's figure turning on the host after a friendly-fire hit.
- Enemies that kept hunting a partner after they died and respawned.
- Quest "teleports": copies of the host's people jumping to the partner.
- Gang-ups by guards over crimes the partner didn't commit (a stop while standing in
  a loot screen counted as fleeing).
- Black screens at the end of a scene on the partner's game.
- A crash while picking herbs together; a village dog attacking the partner; the
  partner pulled into the host's own fights; a killed enemy left standing; doors
  flapping; tools handed to villagers again and again.
- Far-away people and caravan riders moved by the slow path (they are now on the
  per-frame path too, a cart rider held in their seat).
- When your own wait or sleep is undone because only the host's clock moves time in
  a shared world, the game now tells you so.
- A harmless error line at every game exit.

## Known issues

- **Cutscenes are not shared yet**: each player's game plays its own, and they can
  get out of step. This is the next big update.
- **The tutorial is the roughest part** of the game to play together. A save after
  the tutorial is the better start.
- **Lip sync**: characters' mouths don't move in conversations. The Modding Tools
  build of the game doesn't have the data; it is not the mod.
- **Your partner's herb picking** shows them standing (by design, see above).
- **Fast travel** is off during co-op unless the host turns it on (see above).
- **Conversations aren't heard by the other player** yet (their voice lines play on
  the talking player's game only).
- If a join's load ever freezes the game, close it (Task Manager if needed), start
  it again and join again.
- The blacksmith's tutorial at the anvil is each player's own; its four steps in the
  journal follow your own anvil.

---

Copyright (C) 2026 the Kingdom Come: Together contributors ([AUTHORS](../../AUTHORS)).
GPLv3 with the section 7 additional terms in [NOTICE](../../NOTICE): keep the
credits and the official-repository notice; a modified version must say it is
not the official one.
