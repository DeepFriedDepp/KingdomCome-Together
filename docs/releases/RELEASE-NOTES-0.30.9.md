# Kingdom Come: Together 0.30.9 — loot together, and the new name

Unofficial. Not affiliated with or endorsed by Warhorse Studios.

Everything on `main` as of WO-134 (2026-09-27): it carries WO-132 (0.30.7),
WO-133 and WO-134. The installer, `KingdomComeTogether-Setup-0.30.9.exe`, comes
from the maintainer and is not on GitHub. The tester page is
`docs/TEST-0.30.9.md`.

**Both machines and the relay must run the same build.** 0.30.9 refuses every
other version at the handshake, and says so on both sides.

**Verified solo:** one machine, the real game as the partner with a synthetic
host and as the host with a synthetic partner, through a real local relay.
Not yet played by two people. The numbers are in `docs/WO-134-findings.md`
(and `docs/WO-132-findings.md`, `docs/WO-133-findings.md`).

---

## The new name

- The project is now **Kingdom Come: Together**: the launcher's window, banner,
  logo and bottom bar, the installer, the Start menu and desktop shortcuts
  ("Kingdom Come Together"), the entry in Windows' apps list, the in-game
  messages that name the mod, and the Discord hover text.
- The installer is `KingdomComeTogether-Setup-<version>.exe`. It upgrades an
  older install in place (same install folder, same settings, one entry in
  the apps list) and replaces the old shortcuts.
- Nothing internal was renamed: the mod folder, the save and grave names, the
  console commands and the logs are the same, so saves and upgrades keep working.

## Loot together (WO-134)

- **Things you drop for each other work exactly as before.**
- **Bodies are shared.** A body belongs to the host's world. The partner's loot
  screen shows what the host's body holds, and every take goes through the
  host: the first one wins; the other sees "Someone already took that." When
  the host strips a body, it is stripped on the partner's screen too.
  Pickpocketing a living person is still blocked for the partner.
- **Things lying about exist once**: food on a table, tools, coins. Whoever picks
  one up first has it; it is gone for both. (Something only one game has --
  it fell somewhere else there -- stays as it was.) Herbs you gather are
  separate for each player.
- **Chests are per player**, and remembered per world across joins: what the
  host took is back in the partner's chests after a join; what the partner took
  stays taken for him. The game's own restock (usually 7 days) refills chests
  as it always did.

## The story and the clock (WO-133)

- In a shared world the old quest catch-up is off: F11/F12 and the `mp_quest_…`
  commands do nothing to the story, and no quest-gap notices appear.
- Only the host's clock moves the world: the partner's sleeps and waits no
  longer move the host's time.

## Joins, damage, fights (WO-132, from 0.30.7)

- Joins no longer stop on the loading screen for some host worlds.
- Nobody is hit while dead or waking up; only real hits are sent, each once.
- The partner gets the game's own combat mode against the host's people who fight
  near them.

## Known

- Crime is still separate for each of you (stealing, fines, jail).
- The damage the partner takes compared with their own armour is not measured
  yet.
- Chest contents already taken before this version (by the host, alone) are not
  known to the new chest memory: those chests arrive as the host left them.
- Animals are separate in each game.
