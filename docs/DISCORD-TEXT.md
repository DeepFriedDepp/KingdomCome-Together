# Discord text — ready to paste

Short lines for the project's Discord. Copy the block you need as it is.

## The official repository (pin this)

```
📌 Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together
Only builds from this repository are Kingdom Come: Together. Other GitHub repositories can have the same name (copies and forks of old versions) — they are not this project and we can't help with them.
```

## Whose game it is (for the rules / about channel)

```
Kingdom Come: Together is unofficial and free, made by its contributors and maintained by DeepFriedDepp. It is not affiliated with or endorsed by Warhorse Studios or PLAION. Kingdom Come: Deliverance II and everything in it belongs to Warhorse Studios and PLAION. Credits and lineage: https://github.com/DeepFriedDepp/KingdomCome-Together/blob/main/AUTHORS
```

## How to start a game together (WO-159; for the how-to / FAQ channel)

```
How to play together (0.48.0):
1. Both: open the Kingdom Come: Together launcher.
2. Host: HOST GAME, share the address (or the Steam code), PLAY. On the game's main menu press Start Game: pick one of your worlds, or New adventure — choose your playstyle (Soldier / Adviser / Scout), then Skip the prologue (recommended with a partner) or watch its cutscenes (16 min; skipping one skips them all). You start where Hans and Henry part ways.
3. Partner: JOIN with the host's address (or JOIN THROUGH STEAM), PLAY. On the main menu press Join Game, then Join with a new character (pick a playstyle; no save of your own needed) or Bring my character. Pressed too early? It says "Waiting for the host…" and joins by itself when they're ready.
Nobody clicks CONNECT any more, and you don't need anyone's save file. First time ever starting the game? Start it once from Steam and accept its two first-run pages, then use the launcher.
```

## Version 0.48.0 — the host's lock-on (WO-165)

```
Kingdom Come: Together 0.48.0 — fighting together, part two.
• Host: you can now lock onto an enemy that is beating your partner. Walk up to it (within 6 m) and face it, and the game's own lock-on can pick it. It keeps fighting your partner until you hit it. (mp_host_lock, on)
• Built but OFF, because they are not proven yet:
  – mp_victim_decides: your own game judging an enemy's blow against your own block. The game applies the blow, but it does not count a held block yet, so this stays off.
  – mp_block_recoil: an enemy whose blow you blocked bounces back. Not checked by eye yet.
• Fight snapping: two fixes were measured and neither helped enough, so neither ships.
Checked in the game with a stand-in partner. The checklist's items 205–211 need two real players. Both players and the relay need 0.48.0.
```

## Version 0.47.6 — talking to the host's people, the map crash, fleeing enemies (WO-164)

```
Kingdom Come: Together 0.47.6 — mostly for the joiner.
• Talking to the host's people: the mod stopped flooding the game with placements it had already refused (that is what broke talks after a wait). After a wait or a sleep the people near you are reset, the person you walk up to is made ready before you press the key, a talk that hangs is asked once more, then you see "Try again in a moment". Mashing the talk key no longer restarts the conversation.
• Quest counters that drift from the host's (the Mutt bait) are set back to the host's value within seconds.
• The map no longer crashes a fresh joiner. If the game's map ever fails, markers switch off for the session and you are told.
• Fleeing bandits no longer flicker their swords on your screen, and combat ends by itself once they have run off.
• A partner who can't be seated stands beside the bench instead of freezing; "I'm stuck" also gets you out of a seat — press it again within 10 s to land beside your partner.
• The joiner's own random events (caravans, riders) are off during a session, so you both see the host's.
• The host's torch shows on the joiner's screen (and the host's walk and gait no longer go missing after a ride), and the joiner is told when the host reloads.
• Your partner on the map: a pin follows them (mod menu → Display → "Partner on the map").
• People are dressed like in the host's world (no night clothes at dawn), riders sit on their horses, and a quest animal you lead (Ignatius) follows you on the host's screen too.
• Enemies keep their swords out on your screen while they fight.
• "Something's wrong here" now writes a snapshot on BOTH machines under one mark — press it at every problem and send both logs.
Switches (console, default on): mp_talk_sweep, mp_talk_guard, mp_flee_limit, mp_joiner_events, mp_partner_marker, mp_rider_unit.
Checked in the game with a stand-in partner; talking needs two real players — the checklist's items 177–204 are the test. Both players and the relay need 0.47.6.
```

## Version 0.47.0 — shared combat, the first part (WO-163) — built and played (the 0.47.0 session of 2026-10-08)

```
Kingdom Come: Together 0.47.0 — fighting together, part one.
• An enemy blow you never saw coming now comes with a swing: if the host's game showed none (an animal's bite, a blow its game could not read), your copy of the enemy lunges once before the damage lands.
• Combos, throws and the counter-strike after a perfect block are shown on your screen (before, they were dropped).
• An enemy that is fighting one of you only turns to the other if the other has clearly hurt it more — less flip-flopping when you gang up on it.
• Hitting a guard who is fighting your partner: the game itself is now asked whether the guard is fighting you before it counts as a crime.
• New log line for testers that measures how much an enemy's figure steps in a fight.
Switches: host console `mp_hostile_crime on|off` (default on); agent `--no-generic-swing` / `GenericSwingEnabled` in kcdmp-client.json (default on).
Not in this version: your own block deciding a blow on your own screen, locking on to an enemy that fights your partner, the attacker's recoil on a block (each waits for a live check).
Both players and the relay need 0.47.0.
```

## Version 0.42.7 (WO-148)

```
Kingdom Come: Together 0.42.7 — carrying shows for both of you.
• When one of you picks up, carries, puts down or drops a body or a sack, the other sees it, and the body or sack moves with the carrier on both screens.
• Quests don't count the partner's carrying yet (burying, sacks): that is next.
• The dice keys are now built from your own game's files when you install or start it (the mod no longer carries copies of them).
Both players and the relay need 0.42.7. Tester page: docs/TEST-0.42.7.md in the official repository.
```

## Version 0.45.1 — hits never knock you down (WO-155)

```
Kingdom Come: Together 0.45.1 — a small fix for a big annoyance.

An enemy's or an animal's blow no longer knocks a player down: it takes health and stamina, and that is all (a death
still kills). A player's figure now falls on the other screen only when he dies — it lies where he fell until he
respawns — or when your own friendly-fire hit knocked him down (the game's own fall and get-up; never twice within 5
seconds). The T-pose a figure showed for a few seconds after it spawned is gone too.

Both players and the relay need 0.45.1. Tester page in the official repository: docs/TEST-0.45.1.md
```

## Version 0.45.0 — the public beta (WO-154)

```
Kingdom Come: Together 0.45.0 — the public beta.
• Setup does itself: install, open the launcher, follow its checklist. No Workspace Setup, no console.
• Press Insert in the game for the new mod menu: every setting in plain words, "I'm stuck", and the host's settings shown on the partner's screen.
• Questing together: quest steps now go both ways, duels and brawls included.
• Fighting together: your blows turn an enemy that's fighting your partner; a partner who falls or respawns is left alone; a knocked-down player falls on both screens.
• Joins show their progress to the host, wait out slow loads, and say plainly when something can't work ("Join with a new character" where it helps).
• Smoother riding on both screens.
• Fast travel and the mod's voice chat are off unless you turn them on.
Known: cutscenes aren't shared yet (next big update), the tutorial is the roughest part, no lip sync (the Modding Tools build lacks the data).
Both players and the relay need 0.45.0. Quick start and tester page in the official repository: docs/QUICKSTART.md, docs/TEST-0.45.0.md
```
