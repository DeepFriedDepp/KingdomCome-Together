# Kingdom Come: Together 0.42.7 — carrying shows for both of you

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

Everything on `main` from WO-136 to WO-148 (2026-10-01). New since 0.42.5:
WO-148. The installer, `KingdomComeTogether-Setup-0.42.7.exe`, comes from the
maintainer and is not on GitHub. The tester page is `docs/TEST-0.42.7.md`.

**Both machines and the relay must run the same build.** 0.42.7 refuses every
other version at the handshake, 0.42.5 included. 0.42.5 stays available as the
fallback (the tag `v0.42.5`).

**Verified solo:** one machine, the real game as the host with a scripted
partner and as the partner with a synthetic host, console stand-ins for the
player's keys. Numbers and frames: `docs/WO-148-findings.md`.

---

## New (WO-148)

- **Carrying shows on the other screen.** A dead or knocked-out body picked up,
  carried and put down by one player is carried by that player's figure on the
  other screen, and lands where the carrier put it down. The host's world
  decides who carries a body; the other game puts its own carry down again.
- **Sacks**: the carrier's figure holds the sack, and a dropped sack lies where
  it fell on the other screen (shown only; the other's task does not count it).
- **Fail-safe**: nothing alive is moved; a body moves only while carried; a body
  that would land in the air or under the ground goes back where it was picked
  up, and the log says so.
- **About** in the launcher, `AUTHORS` and `NOTICE`: the official repository,
  the project's lineage, the licence and its section 7 terms.

## Changed (WO-148)

- **The dice keys** are built on the player's machine from the game's own files
  (Setup, and the launcher before every start); the mod no longer carries copies
  of the game's key files.
- **Quest titles** in the shared-quest messages come from the player's own game.
- The native log carries the thread id on every line, and every hook checks on
  the running game's own code that it patches whole instructions (it refuses,
  and logs why, otherwise).

## Off switches (the console, `~`)

New: `mp_carry_sync off` (bodies, and the whole carry layer: a carry shows only
on the carrier's screen, as in 0.42.5), `mp_carry_objects off` (sacks only).
From 0.42.5: `mp_hostile_engage`, `mp_quest_safety`, `mp_leash_cap_s`,
`mp_npc_catchup`; from 0.42.2: `mp_avatar_dress`, `mp_avatar_lights`,
`mp_show_animals`; from 0.42.0: `mp_hand_items`, `mp_activity_gaits`,
`mp_oneshots`, `mp_player_minigames`, `mp_idles`, `mp_activities`,
`mp_animal_attacks`, `mp_sleep_vote`, `mp_quest_sync`, `mp_crime_shared`.

## Everything from 0.42.5, 0.42.2 and 0.42.0

`docs/releases/RELEASE-NOTES-0.42.5.md`, `docs/releases/RELEASE-NOTES-0.42.2.md`,
`docs/releases/RELEASE-NOTES-0.42.0.md`.

## Still known

- The partner's carrying does not count for the host's quests yet (a burial,
  sacks to deliver); crimes for carrying a body are not shared yet.
- There is no throw in the game; "drop" is the put-down.
- Where the host's animals stand can differ on the partner's screen until they
  come close.
- A cutscene plays for each player separately; on the partner's screen it can
  stay black until the host's next step.
- An escort (the sheep in "Find Mutt!") follows only the player who leads it.
- The partner does not hear the host whistle, and the other way round.
- The journal's marker letters can differ between the two players.
- At the grindstone the blade is not in the other player's figure's hands.

---

Copyright (C) 2026 the Kingdom Come: Together contributors ([AUTHORS](../../AUTHORS)).
GPLv3 with the section 7 additional terms in [NOTICE](../../NOTICE): keep the
credits and the official-repository notice; a modified version must say it is
not the official one.
