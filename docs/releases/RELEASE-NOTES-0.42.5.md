# Kingdom Come: Together 0.42.5 — the partner fights, the leash pulls

Unofficial. Not affiliated with or endorsed by Warhorse Studios.

Everything on `main` from WO-136 to WO-147 (2026-09-30). New since 0.42.2:
WO-147, the fixes from the first long two-player evening on 0.42.2. The
installer, `KingdomComeTogether-Setup-0.42.5.exe`, comes from the maintainer and
is not on GitHub. The tester page is `docs/TEST-0.42.5.md`.

**Both machines and the relay must run the same build.** 0.42.5 refuses every
other version at the handshake, 0.42.2 included. 0.42.2 stays available as the
fallback (the tag `v0.42.2`).

**Verified solo:** one machine, the real game as the host with a scripted
partner and as the partner with a synthetic host, console stand-ins for the
player's keys. Numbers and frames: `docs/WO-147-findings.md`.

---

## Fixed (WO-147)

- **The partner can fight.** Bandits and wild animals near the partner fight
  the partner, even when the host is not fighting them: lock on, block, hit;
  they fight back. Friendly people never join in by themselves.
- **The partner's hits count**, also the ones that only tire an enemy (a block,
  a blunt or damaged weapon), and the host's enemies turn on the partner's
  figure. Hitting a bandit or a wolf is no crime.
- **The leash brings the partner back** while the host sits in the inventory,
  the map or a conversation. A busy partner (talking, in a scene) waits at most
  a minute. Far away, the partner lands beside the host, or on a spot the host
  just stood on, without fall damage.
- **The host's quests are safe.** A step on the partner's side that would fail
  a quest, cancel an objective or count someone as dead only happens in the
  host's world if it happened there too; a knocked-out fighter is never dead on
  one screen only, also after a load or a rejoin.
- **The host's hits and kills land on the partner's screen** also on people the
  host's world spawned (an ambush, a quest's fighters): they went to an unseen
  copy before.
- **"The host is paused" no longer sticks** after a quest's sleep that cuts to
  a scene.
- **Flying or a quest's teleport is not fast travel**: the host is not told the
  partner tried to fast travel.
- **Busy towns.** The partner's agent no longer falls minutes behind in a
  crowded town: the host's villagers keep moving on the partner's screen
  instead of stopping, snapping back and starting again (`mp_npc_catchup off`
  goes back to 0.42.2's way).
- Leaving the host's world loads only a save this game version can load.
- The console markers of the 0.42.2 tester page (`mark_rejoin`, `mark_load`,
  `mark_clothes`, `mark_dice`, `mark_time`, `mark_icon`) exist now.

## Off switches (the console, `~`)

New: `mp_hostile_engage off` (partner: only what the host fights comes into
your combat mode), `mp_quest_safety off` (host: every quest step of the
partner's is applied as in 0.42.2), `mp_leash_cap_s 0` (host: a hold keeps the
pull back as long as it lasts), `mp_npc_catchup off` (partner: every NPC sample
through, the 3 s release rule here). All on by default. From 0.42.2:
`mp_avatar_dress`, `mp_avatar_lights`, `mp_show_animals`; from 0.42.0:
`mp_hand_items`, `mp_activity_gaits`, `mp_oneshots`, `mp_player_minigames`,
`mp_idles`, `mp_activities`, `mp_animal_attacks`, `mp_sleep_vote`,
`mp_quest_sync`, `mp_crime_shared`.

## Everything from 0.42.2 and 0.42.0

`docs/releases/RELEASE-NOTES-0.42.2.md`, `docs/releases/RELEASE-NOTES-0.42.0.md`.

## Still known

- Carrying a body or an object shows only on the carrier's screen.
- Where the host's animals stand can differ on the partner's screen until they
  come close.
- A cutscene plays for each player separately; on the partner's screen it can
  stay black until the host's next step.
- An escort (the sheep in "Find Mutt!") follows only the player who leads it.
- The partner does not hear the host whistle, and the other way round.
- The journal's marker letters can differ between the two players.
- At the grindstone the blade is not in the other player's figure's hands.
