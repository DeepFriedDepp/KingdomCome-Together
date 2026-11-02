# Kingdom Come: Together 0.64.0 — tester script (the internet session)

Two players, one host and one joiner, both on the **same 0.64.0 build**, playing **over the internet** with the launcher's default
Steam route (the host's join code). This session is for the link between you: how fast the host's world reaches the joiner. If
anything looks wrong, press the **period key** at that moment, then open the console (~) and type `mark <what you saw>`: the note
reaches the other player's bug report too.

## Before you start
- Both of you: the same build (the build id is in the launcher's About and in the installer's name; a partner on another build is told
  so when you connect).
- Connect the launcher's default way, the host's **join code over Steam** -- not a LAN or VPN address: this session is for Steam's
  own link (both bug reports then show what Steam measured and the settings it took).
- Play for **about 20 minutes**. Do not alt-tab away for long: the game's own frame rate drops behind other windows.

## 1. A fight
- **Joiner:** pick a fight with two or three bandits (or the host's enemies near you); block, parry, strike as in your own game.
- **Watch:** do the bandits move smoothly on your screen, or do they slide, stutter or jump?
- **Host:** if your frame rate drops in the fight, `mark` it; if asked, fight once more with `mp_npc_bands off` to compare.

## 2. A tavern
- A few minutes among seated drinkers, the innkeeper serving.
- **Watch:** do the seated people stay still where they sit, or do they jitter in their seats? Does the host's figure jitter?

## 3. A village
- Walk through a busy village together, the joiner a little behind the host.
- **Watch:** do the villagers and the host's figure walk smoothly, without sliding or rubber-banding?

## 4. A joined talk, both ways
- **Host** starts a talk with someone; the **joiner** listens and votes. Then the other way round: the **joiner** starts one and the
  host listens.
- **Watch:** after the starter picks an answer, does the next line play with its voice, or is it skipped or cut off?

## 5. Mark the jitter
- Whenever something jitters, slides, jumps or skips: the **period key** and `mark <what you saw>` at that moment.
- If the game tells the joiner "Your connection to the host is struggling: NPCs may look jumpy", mark that moment too.

## 6. Both bug reports, right after
- Both of you: send the bug report from the launcher as soon as you stop. The report's first section, **Link**, says how fast the
  host's world reached the joiner, what Steam's own connection said, and every gap over a second with what came just before it.

## The switches (only if asked)
Every part of 0.64.0's link work can be turned off live from the console, one at a time, to compare: `mp_w213_status` lists them
(`mp_link_sched`, `mp_npc_bands`, `mp_playout_adaptive`, `mp_posture_no_pos`, `mp_line_latency`, `mp_link_toast` and the others).
The link's own switches take effect at the next connection.
