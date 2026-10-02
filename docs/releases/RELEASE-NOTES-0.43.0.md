# Kingdom Come: Together 0.43.0 — what the long session showed, and the beta's safeguards

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

Everything on `main` up to WO-151 (2026-10-02). The installer,
`KingdomComeTogether-Setup-0.43.0.exe`, comes from the maintainer and is not on
GitHub. The tester page is `docs/TEST-0.43.0.md`; the evidence is
`docs/WO-151-findings.md`.

**Both machines and the relay must run the same build.** 0.43.0 refuses every
other version at the handshake, 0.42.8 included.

**Verified:** solo on one machine against a scripted partner, on throwaway
saves, with frames for every visual fix; every synthetic suite, the agent, relay
and native tests, the static checks, and the frame-rate soak (the game with the
mod against the same scene without it). What needs two people is listed in
`docs/TWO-PLAYER-CHECKLIST.md` (WO-151).

---

## Fixed

- **Fighting the same enemy.** On the partner's screen an enemy you both fight no
  longer falls over by itself, sinks into the ground or takes out a hoe: it shows
  the host's own reactions, and its health is the host's.
- **Riding.** A horse being ridden belongs to its rider: nothing of the mod drives,
  pauses or animates it meanwhile (it was being moved by three things at once).
- **Catching up.** After a join or the host's reload the partner acts only once
  their quests match the host's; a step the host has already done is never done
  again.
- **Black screens after a scene** that waits for the host's people are helped along
  (and given up at 90 s), and the host no longer reads a black partner as free.
- **The join really pauses the host's world** (until now it kept running).
- **The anvil**: the host could not leave the smithing minigame; fixed. The
  partner's smithing now shows on the host's screen, and no station looks taken.
- **Doubled theft reports** (a stack taken at once counted per piece).

## New

- **The host's weather**, read from the game as it changes, is the session's.
- **Doors** belong to the host's world, both ways.
- **The whistle** is heard at your figure on the other screen.
- **A living person a quest lets you carry** is carried on both screens.
- **Crimes count for both players** (`mp_crime_mode individual` for the old way); a
  cleared record clears the wanted icon too.
- **Safety nets for the beta:** no native error goes unlogged, and the frame rate is
  written to the log every minute; `mp_unstuck` gets you out of any stuck state.

## Still known

- The host's friendly fire hurts the partner but shows no reaction on him yet.
- A wolf's bite hurts, but the bite itself is not shown yet.
- Conversations and barks are not heard by the other player.
- A scene that waits for a quest step can still stay black until the next load.
- More in the README's "Not built yet, and known gaps".
