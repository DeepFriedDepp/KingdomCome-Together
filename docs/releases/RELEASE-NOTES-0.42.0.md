# Kingdom Come: Together 0.42.0 — what people hold and do

Unofficial. Not affiliated with or endorsed by Warhorse Studios.

Everything on `main` from WO-136 to WO-143 (2026-09-28). New since 0.41.7:
WO-143. The installer, `KingdomComeTogether-Setup-0.42.0.exe`, comes from the
maintainer and is not on GitHub. The tester page is `docs/TEST-0.42.0.md`.

**Both machines and the relay must run the same build.** 0.42.0 refuses every
other version at the handshake, 0.41.7 included. 0.41.7 stays available as the
fallback (the tag `v0.41.7`).

**Verified solo:** one machine, the real game as the host with a scripted
partner and as the partner with a synthetic host that streamed the host's
townspeople as the real host had recorded them. Not yet played by two people.
Numbers and frames: `docs/WO-143-findings.md`.

---

## Tools, hoeing, gestures, and the other player's minigames (WO-143)

- **Tools in hands.** A villager walking with a saw, a bucket, a broom or a hoe
  carries the same tool in the same hand on the partner's screen.
- **Farmers hoe.** A farmer working a field hoes along the row, bent over the
  hoe, as on the host's screen, and holds it still at the row's end.
- **Tavern life.** Seated guests drink and dice players react on the partner's
  screen, and stay in their seats.
- **The other player's minigames.** At a grindstone the other player's figure
  sits on the seat and grinds; reading, alchemy, picking herbs, picking a lock,
  digging and smithing show as themselves, and the figure gets up when the
  minigame ends.
- People standing about still turn their heads to a player nearby by
  themselves; nobody is forced to look (on a paused copy that swung the head).
- A refused activity is logged three times, then once a minute.

## Off switches (the console, `~`)

New: `mp_hand_items off` (tools), `mp_activity_gaits off` (the hoeing, drunk or
injured walk), `mp_oneshots off` (drinks, reactions, short gestures),
`mp_player_minigames off` (the other player's figure stands),
`mp_idles off` (looks). All on by default; `mp_activity2_status` prints the
state. From 0.41.7: `mp_activities off`, `mp_animal_attacks off`,
`mp_sleep_vote off`, `mp_quest_sync off`, `mp_crime_shared off`. `on` switches
each back.

## Everything from 0.41.7 (WO-136 to WO-141)

Joining at the main menu, one world for both (animals, enemies, knockouts,
horses, clothes, torches), shared quests, nobody's menu stopping the other's
world, crimes in the host's world, sleeping together, and sitting, sleeping,
leaning and working on both screens, with animals' bites:
`docs/releases/RELEASE-NOTES-0.41.7.md`.

## Still known

- Work at a bench or a desk that needs a tool (a carpenter debarking, a sawyer,
  a scribe) is not shown: the person stands at the spot on the partner's screen.
- A seated guest's tankard is not in the hand (the drink plays).
- A barmaid serving or wiping a table, or someone busy on the way somewhere,
  may not show that gesture on the partner's screen.
- A cart's horses can walk ahead of the cart on the partner's screen (the cart
  itself is not sent yet).
- At the grindstone the blade is not in the other player's figure's hands;
  stone throwing and archery are not shown (the figure stands).
- A surrendering enemy does not kneel on the partner's screen yet.
