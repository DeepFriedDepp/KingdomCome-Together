# Kingdom Come: Together 0.40.0 — the avatar is a puppet

Unofficial. Not affiliated with or endorsed by Warhorse Studios.

Everything on `main` as of WO-135 (2026-09-27). The installer,
`KingdomComeTogether-Setup-0.40.0.exe`, comes from the maintainer and is not on
GitHub. The tester page is `docs/TEST-0.40.0.md`.

**Both machines and the relay must run the same build.** 0.40.0 refuses every
other version at the handshake.

**Verified solo:** one machine, the real game as the host with a synthetic
partner and as the partner with a synthetic host. Not yet played by two people.
Numbers and frames: `docs/WO-135-findings.md`.

---

## The partner's avatar is seen, never heard

- It no longer barks, greets, shouts when hit or comments on anything: every
  dialogue with the avatar as a speaker is refused where the game starts it.
- It is never a crime witness (it never reports the host, never turns a guard on
  the host), never reacts to what it sees or hears, and never blocks, dodges or
  attacks on its own. Enemies still see and target it.
- An avatar knocked to the ground gets up instead of lying in it.

## Knocked-out enemies are shared

- A knocked-out enemy lies in the same place on both screens and gets up on both.
- Either player can loot it; the partner's finishing move on a lying body is
  performed in the host's world by the partner's avatar.

## Crouching, outfits, joins

- Crouching is read from the game's own stance as well and shows on the other screen.
- Outfit changes are applied against what the avatar really wears: a layered
  piece (plate over a gambeson) is no longer refused, and nothing is ever
  suppressed for minutes.
- A join only brings a character from a save of the host's game version. With no
  such save, the launcher says so plainly and the host is never paused.
- Joining no longer holds the loading screen until the engine's post-load
  timeout (the red "post load reconstruction" error).
