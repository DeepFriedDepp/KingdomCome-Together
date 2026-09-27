# KCD2-MP 0.30.2 — staying together

Everything on `main` as of WO-114 (2026-09-26). The installer,
`KCDMP-Setup-0.30.2.exe`, comes from the maintainer and is not on GitHub.
The tester page is `docs/TEST-0.30.2.md`.

**Both machines and the relay must run the same build.** 0.30.2 refuses
0.30.0 and older at the handshake (the connection protocol changed), and says
so on both sides.

**Verified solo:** one machine, the real game as host with a synthetic
partner, and as the partner with a synthetic host, through a real local relay.
Not yet played by two people. The numbers are in `docs/WO-114-findings.md`.

---

## The leash

The world is only alive around the host (the first two-player session showed
the partner's surroundings freezing past about 800 m). So the partner is kept
close, and the host decides.

- **600 m:** the partner sees "You're getting far from your host. Head back,
  or you'll be brought back." The host sees "<partner> is getting far away."
- **650 m:** "Bringing you back to your host in 10..." Walking back inside
  stops it. At zero the partner is put beside the host (off the horse first;
  the horse stays where it was).
- Never while either of you is dead, loading, talking, in a cutscene or in a
  menu: the countdown waits and carries on afterwards.
- Host settings: `mp_leash on|off` (on), `mp_leash_warn_m` (600),
  `mp_leash_pull_m` (650).

## Fast travel together

- Only the host fast-travels. When the host arrives, the partner is brought
  along ("Your host fast-travelled."), and the time that passed reaches the
  partner too.
- The partner's own fast travel is refused: "Only the host can fast travel in
  co-op."

## Waking up after a death

- A death wakes you at the wake-up spot nearest the other player (within
  600 m of them, at least 100 m from where you died), and not the same spot
  twice in a row. With no spot near them, you wake beside them. In 0.30.0
  every death in the first session woke at the same spot.

## Fixed

- After any fast travel the agent kept thinking the game was paused (the map
  screen's open sound has no matching close). It now clears when the travel
  ends.

## Known

- A long pull shows the game streaming the new place in (pop-in) for a
  moment; a fade may come later.
- Fights and NPC reactions around the partner, crime, and quest progress are
  unchanged from 0.30.0 (later work).
