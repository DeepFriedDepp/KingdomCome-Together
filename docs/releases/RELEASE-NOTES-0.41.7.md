# Kingdom Come: Together 0.41.7 — one world for both

Unofficial. Not affiliated with or endorsed by Warhorse Studios.

Everything on `main` from WO-136 to WO-141 (2026-09-28). The installer,
`KingdomComeTogether-Setup-0.41.7.exe`, comes from the maintainer and is not on
GitHub. The tester page is `docs/TEST-0.41.7.md`.

**Both machines and the relay must run the same build.** 0.41.7 refuses every
other version at the handshake.

**Verified solo:** one machine, the real game as the host with a synthetic
partner and as the partner with a synthetic host. Not yet played by two people.
Numbers and frames: `docs/WO-136-findings.md` to `docs/WO-141-findings.md`.

---

## Joining

- **The partner waits at the main menu** and joins the host's world by clicking
  CONNECT there; only the host loads a save.
- **A partner who loads their own save** is told plainly, in the game and in the
  launcher, and nothing of the host's world is applied to theirs.
- Joining with people or animals about ends the loading screen normally.

## The world is there for both of you (WO-136)

- The host's animals appear for the partner; enemies fight both players and keep
  fighting the partner when the host goes down; knockouts show on both screens.
- Whoever rides a horse owns it for the ride; clothes, torches and crouching show
  on the other screen; "Someone already took that" only when it is true.

## Questing together (WO-137)

- Main quests and side quests live in the host's world: a step either player
  makes updates both journals. The partner can talk to people. A step counts
  once, rewards are each player's own, and dead stays dead.

## Nobody's menu stops the other's world (WO-138)

- The inventory, map, journal, ESC menu, a conversation or a cutscene of one
  player no longer freezes the other's world. Alone, the game pauses as before.

## Crime and guards (WO-139)

- The partner's crimes are crimes in the host's world and the host's guards
  deal with the partner — the game's own arrest, fine, punishment or fight.
  Punishment no longer skips hours. You can't rob each other.

## Sleeping together (WO-140)

- "Sleep" at a bed asks the other player first; on a yes both see the sleep
  screen for the same hours and wake together. One clock: the host's.

## Sitting, sleeping, leaning, working — and animals' bites (WO-141)

- What people do with things now shows on the other screen: a villager asleep
  in bed lies in the same bed, one on a bench sits on it, a woman leaning on a
  wall leans on it, a guard at his post stands at it — on the partner's screen
  as on the host's, instead of standing next to it.
- The same for the two of you: sit on a bench or lie in a bed and your figure
  does the same on the other screen, with the name above it. Washing your face
  at a water trough shows as a wash at that trough.
- A person who is doing something leaves it first when a fight, a conversation,
  a knockout or a death comes, and goes back to it afterwards.
- A wolf's bite now plays on the partner's screen (dogs and boars take the same
  path; not seen yet).

## Off switches (the console, `~`)

`mp_activities off` (bodies stand where they are, as before),
`mp_animal_attacks off` (animals walk but their bites are not animated on the
partner's screen), `mp_sleep_vote off`, `mp_quest_sync off`,
`mp_crime_shared off`. `on` switches each back.

## Still known

- Work that needs a tool in the hand (a woodworker's drawknife, a farmer's hoe,
  a broom) is not shown: the person stands at the spot on the other screen, or
  walks the field without the hoe.
- The grindstone, smithing, alchemy, reading and dice are not shown as such:
  the figure stands at the spot.
- A surrendering enemy does not kneel on the partner's screen yet.
