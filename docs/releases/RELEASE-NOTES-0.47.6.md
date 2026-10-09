# Kingdom Come: Together 0.47.6 — talking to the host's people, the map crash, fleeing enemies

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

0.47.0 plus WO-164 (2026-10-09), the same code as the second 0.47.5 build, versioned 0.47.6 by the maintainer. The installer,
`KingdomComeTogether-Setup-0.47.6.exe`, comes from the maintainer. The evidence is
`docs/WO-164-findings.md`; what to check live is `docs/TWO-PLAYER-CHECKLIST.md`, items 177–204 (and the earlier ones still pending).

**Both machines and the relay must run the same build.** It refuses every other version at the handshake.

---

## Changed

- **Talking to the host's people (joiner).** The flood of "the game could not place this person" errors was the mod itself asking
  the game, every few seconds, for a placement it had already refused: it now gives up after three tries and shows the person by
  their movement alone. After a wait or a sleep every person near you is reset the game's own way (four at a time), a person
  who keeps refusing is reset by itself, and the person you walk up to is made ready before you press the key. Pressing the key
  again and again on one person no longer restarts their conversation each time; a request that does not start within four
  seconds is cancelled and asked once more; then "Try again in a moment." A person you talk to drops their own greeting first.
- **Quest values without a port** (joiner): a quest counter that differs from the host's for more than ten seconds — and every
  such value before you talk to someone — is set to the host's value directly (whole-number and yes/no values only).
- **The map no longer crashes a fresh joiner.** A map marker (a grave) waits until the game's map is ready; if the game's map
  ever fails while adding one, markers are switched off for the session and you are told "Map markers are off this session (a
  game error)."
- **Fleeing enemies.** No more tug-of-war over a fleeing bandit's sword on the joiner's screen (it cost a weapon re-creation
  every half second); a hold the mod made on an enemy that has been fleeing for 20 seconds is let go, so combat can end.
- **Sitting.** A partner's figure the game will not seat stands beside the seat instead of freezing in a pose; a figure left
  sitting after its player stood up (or reloaded) is stood up; "I'm stuck" also gets you out of a seat, and pressed again within
  ten seconds puts you beside your partner.
- **Random events (joiner):** your own game's random events (caravans, riders, ambushes) are off while you are in a session, so
  the host's are the only ones. `mp_joiner_events on|off`.
- **The host's torch** now reaches the joiner's screen (a second, reliable message besides the old one).
- **The host's reload is told:** "Your host is reloading the world - please wait", then "Back with your host".
- **For bug reports:** "Something's wrong here" now writes a state snapshot on **both** machines under one mark, so the two logs
  can be read side by side; every talk writes one line saying how it went and why (`WO164-TALK`).
- **Your partner on the map:** a pin shows where your partner is and follows them; it goes when they leave. Mod menu → Display →
  "Partner on the map" (`mp_partner_marker`).
- **People's clothes follow the host's world:** a person the host sees dressed is dressed on the joiner's screen too, also after
  a wait or a sleep (no more night clothes at dawn).
- **Leading a quest animal (Ignatius):** the host's world accepts the joiner's steps of the quest he is leading, and the host
  sees the animal walk behind the joiner.
- **Riders and their horses move as one** on the joiner's screen.
- **The host's walk, gait and torch after a ride:** a refused position read no longer stops the host's movement details (and
  torch) for the rest of the session; it is tried again after a minute.
- **Enemies keep their swords out** on the joiner's screen while they fight; **talking to a villager** next to one of the host's
  people no longer freezes the host's person for 20 seconds.
- **The sleep screen** of the partner who did not start the sleep shows the rest gained.

## Known issues

- **Checked in the game with a synthetic host only** (2026-10-09, a throwaway save): the partner pin, the quest-value fix, the
  snapshot and its ping, the torch, the clothes, the sit fallback, "I'm stuck", the random-event switch, the escort walk, the
  sweep. **Talking** could not be driven by a script and is not checked live: the checklist's items 177–204 are the proof. Each
  new piece has its own switch: `mp_talk_sweep`, `mp_talk_guard`, `mp_flee_limit`, `mp_joiner_events`, `mp_partner_marker`,
  `mp_rider_unit` (console, default on).
- **Not soak-tested** (the maintainer's standing rule; the waiver is recorded beside the installer). The native plugin, the agent,
  the relay's shared wire and the mod's Lua all changed since 0.47.0. The mod's own cost per frame was measured against 0.47.0 in
  the same scenes (a town, a three-person fight): 0.64–0.67 ms against 0.70–0.72 ms. The frame rate with the window in front was
  not measured. Not signed unless the build says so.
