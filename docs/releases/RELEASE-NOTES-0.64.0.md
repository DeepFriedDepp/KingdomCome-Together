# Kingdom Come: Together 0.64.0 — the host's world reaches the joiner in time

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

Everything on `main` from WO-213 (2026-11-01). The installer, `KingdomComeTogether-Setup-0.64.0-<build id>.exe`, comes from the
maintainer. The evidence is `docs/WO-213-findings.md`; what to try together is `docs/TESTER-SCRIPT-0.64.0.md`.

**Both machines and the relay must run the same build.** 0.64.0 refuses every other build at the handshake.

---

## New

- **The newest state wins.** A person's newest update replaces an older one that has not left yet, on both machines and in the relay;
  events (a hit, a death, a conversation line, the world on joining) go first, in order, and are never dropped. `mp_link_sched` (on).
- **Steam's link is set for the game**: no waiting to bundle small messages, and two lanes -- events reliable, the stream of states on
  a lane of its own -- so a state is not held behind the world while it crosses. `mp_steam_cfg` (on).
- **People near a fight are sent 30 times a second**, those within 40 m 20, within 150 m 10; further away less. `mp_npc_bands` (on).
- **The partner's figure goes out every frame it moves** (it went out 9 to 17 times a second in the last sessions). `mp_figure_band` (on).
- **The joiner plays the host's people a measured delay behind**, from how regularly their updates actually arrive, so they move
  smoothly over an uneven connection. `mp_playout_adaptive` (on).
- **Seated, lying and working people stay put**; a late update no longer makes the joiner's game re-seat them. `mp_posture_no_pos`,
  `mp_posture_hold_late` (on).
- **In a joined conversation a late line keeps its voice**, and the line that answers a pick is always voiced. `mp_line_latency` (on).
- **"Your connection to the host is struggling: NPCs may look jumpy"** when the link has been poor for 10 seconds (never for one
  spike). `mp_link_toast` (on).
- **The bug report opens with "Link"**: how fast the host's world reached the joiner, what Steam measured, and every gap over a second.

`mp_w213_status` lists every switch above; the link's own take effect at the next connection.

## Known

- Joining still moves the host's world across first (about 1.4 MB): a few seconds at Steam's usual rate.
- Most of what still looks jumpy on our bench is on the joiner's side right after a big change in the host's world (a new day: every
  villager sets off at once) -- the joiner's game takes a while to settle their postures. Tell us where you see it.
- In a fight the host ran about 3 fps lower on our bench with people near a fight sent 30 times a second (`mp_npc_bands`). If the
  host's frame rate drops noticeably in fights, try `mp_npc_bands off` and tell us.
- In a joined conversation the listener's vote was occasionally not taken for a whole talk on our bench (the listener's screen in an
  odd state when it began). If it happens to you, press Back to leave the talk and join again, and mark it.
