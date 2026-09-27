# KCD2-MP 0.30.7 — joins that load, safe damage, combat mode together

Everything on `main` as of WO-132 (2026-09-27). The installer,
`KCDMP-Setup-0.30.7.exe`, comes from the maintainer and is not on GitHub.
The tester page is `docs/TEST-0.30.7.md`.

**Both machines and the relay must run the same build.** 0.30.7 refuses every
other version at the handshake, and says so on both sides.

**Verified solo:** one machine, the real game as the partner with a synthetic
host and as the host with a synthetic partner, through a real local relay.
Not yet played by two people. The numbers are in `docs/WO-132-findings.md`.

---

## Joining

- Joins no longer stop on the loading screen for some host worlds. The
  rebuilt world file now follows the game's own rule for parts that do not
  compress, and every file is checked against the limits the game checks
  before it is placed.
- An early-game character (stripped of everything by the story) can join and
  host again.

## Damage

- Nobody is hit while dead or waking up: nothing from the host's world reaches
  a player from the moment they go down until five seconds after they wake.
- Only real hits are sent. Bleeding on the host's side no longer counts as
  damage to the partner.
- Every hit lands once (a second, empty copy of each hit is gone).
- When the partner goes down, only the partner's figure leaves the fight on
  the host's side; the fight goes on for the host. The figure is hidden where
  it fell and appears again where the partner wakes up.
- When the partner hits someone who is already fighting the host, that person
  stays on the host.

## Fighting

- The partner gets the game's own combat mode against the host's people who
  fight near them: the direction indicator, block, the enemy facing them in its
  guard, changing guard and swinging. The host's world still decides every
  hit, wound and death, and those people never run on their own on the
  partner's side.

## Known

- The damage the partner takes compared with their own armour is not measured
  yet (please note both numbers, tester page item 8).
- People's blocks can look like their guard on the partner's screen.
- The players' own swings on each other's figures: still unproven.
- Animals are separate in each game.
