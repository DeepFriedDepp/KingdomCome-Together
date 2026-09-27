# KCD2-MP 0.30.5 — fighting together

Everything on `main` as of WO-131 (2026-09-26). The installer,
`KCDMP-Setup-0.30.5.exe`, comes from the maintainer and is not on GitHub.
The tester page is `docs/TEST-0.30.5.md`.

**Both machines and the relay must run the same build.** 0.30.5 refuses every
other version at the handshake, and says so on both sides.

**Verified solo:** one machine, the real game as host with a synthetic
partner, and as the partner with a synthetic host, through a real local relay.
Not yet played by two people. The numbers are in `docs/WO-131-findings.md`.

---

## One world, the host's

- On the partner's side, every person of the host's world is either moved by
  the host's game or hidden and paused. Nobody runs on their own there any
  more (the last session had the same traveller in two places 470 m apart).
- The host now sends the people up to 150 m from either player (every 2 s past
  60 m), so the partner sees the host's people around them.
- People the host's game spawned on the road (ambushes) now appear for the
  partner as look-alikes under the same name.

## Hits, deaths and bodies

- A hit by the partner counts only on a person the host's world has right
  there (within 3 m of where the host has them).
- On the partner's side a person never dies from local blows; their health
  follows the host's, and they die when they die in the host's world, where
  they fell there.
- The partner cannot loot bodies or pickpocket people for now ("Only the host
  can loot bodies in co-op for now."): the items stay on the body for the
  host. No duplicates.

## The partner is seen

- The host's people always notice the partner's figure (not only for 30 s
  after a hit), and it is on the host player's side in a fight.
- The partner's figure takes every hit that is measured: its health is put
  back after each one (it used to stop registering hits after the first few).

## Fights

- A death ends the fight: after waking up, the people you fought leave you
  alone (crime keeps its own rules).
- People's swings now reach the partner's screen: the host's game captures
  its people's attacks (it never did before) and the partner's copies play
  them.
- A person knocked flat by a partner's blow no longer sinks into the ground:
  they stand back up where the host's world has them standing.

## Staying together

- The countdown stops only back under 630 m (it flapped at 650 m).
- Chatter near a rider no longer counts as a conversation that pauses it.

## Known

- Road-ambush look-alikes may not swing (their look is approximate).
- The players' own swings on each other's figure are unproven.
- Damage to the partner compared with their own armour is not measured.
- A person the partner hits turns to the partner's figure but may not fight it
  on the host's side.
- Animals are separate in each game. Crime shared between the players is the
  next work order.
