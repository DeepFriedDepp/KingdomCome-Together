# Kingdom Come: Together 0.42.2 — what the first evenings found

Unofficial. Not affiliated with or endorsed by Warhorse Studios.

Everything on `main` from WO-136 to WO-144 (2026-09-30). New since 0.42.0:
WO-144, the fixes from the first real two-player sessions. The installer,
`KingdomComeTogether-Setup-0.42.2.exe`, comes from the maintainer and is not on
GitHub. The tester page is `docs/TEST-0.42.2.md`.

**Both machines and the relay must run the same build.** 0.42.2 refuses every
other version at the handshake, 0.42.0 included. 0.42.0 stays available as the
fallback (the tag `v0.42.0`).

**Verified solo:** one machine, the real game as the host with a scripted
partner and as the partner with a synthetic host. Numbers and frames:
`docs/WO-144-findings.md`.

---

## Fixed (WO-144)

- **The invisible second partner.** A partner who restarted the game stayed a
  partner too; every sleep question waited for him and timed out. A restart now
  replaces the old connection at once.
- **The host's crash on loading a save in a session.** Loading in the middle of
  a session is safe again.
- **The partner shown naked.** The figure wears what its player wears, and puts
  it back on by itself if the game takes it off.
- **Crouching** stays crouched; sneaking looks like sneaking.
- **The lantern.** At night a figure holds a light only when its player does;
  nothing is left on the ground.
- **Horses** that were missing on the partner's screen show up.
- **Talking.** The host's villager waits for the partner only while they really
  talk; a partner who can't talk to someone is told.
- **Dice with a villager** start on the partner's screen after the conversation.
- **One clock.** The partner's clock stops with the host's and stays within
  about a minute of it.
- **Early saves** (from the start of the game) can join; a join that cannot go
  through says why.
- **Sitting on a bed** shows as lying on it on the other screen.
- **Logs are kept** for the six launches before; Report a bug sends them.

## Off switches (the console, `~`)

New: `mp_avatar_dress off` (the other player's clothes, the 0.42.0 way),
`mp_avatar_lights off` (the figure's own lamps and torches), `mp_show_animals
off` (horses and animals shown where this world hides them). All on by default.
`on` switches each back. From 0.42.0: `mp_hand_items`, `mp_activity_gaits`,
`mp_oneshots`, `mp_player_minigames`, `mp_idles`, `mp_activities`,
`mp_animal_attacks`, `mp_sleep_vote`, `mp_quest_sync`, `mp_crime_shared`.

## Everything from 0.42.0 (WO-136 to WO-143)

`docs/releases/RELEASE-NOTES-0.42.0.md`.

## Still known

- A wolf, a dog or a boar from a random encounter on the host's side may be
  missing on the partner's screen.
- A red icon at the top of the partner's screen after a crime can stay on.
- The partner does not hear the host whistle, and the other way round.
- An escort (the sheep in "Find Mutt!") follows only the player who leads it, on
  that player's screen.
- A cutscene can stay black on the partner's screen until the host's next step.
- The journal's marker letters can differ between the two players.
- At the grindstone the blade is not in the other player's figure's hands.
