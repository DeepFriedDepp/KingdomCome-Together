# WO-179 — 0.50.7: watching videos together, being brought along, no long black screens

Unattended overnight, 2026-10-13. Version 0.50.7 (the maintainer's string). Combat code, combat switches and combat defaults are
untouched (0.50.6's combat build is still unplayed).

## What was built

**Release gates**
* `tools/Check-Publishing.ps1` — the publishing gate. Every added line, every new or renamed file name and every commit message that a push
  would publish is checked against a denylist kept outside the repository (`KCDMP_PUBLISH_DENYLIST`). It fails closed (no list = no push).
  `tools/Build-Installer.ps1` runs it before every build and pushes only through it (`-Push`). `docs/WO-TEMPLATE.md` carries the rule.
* The live save round trip: a **skipped** run now fails a release build unless `-SaveRoundTripWaiver "<who, why>"` is given (written down
  beside the installer, like the soak waiver).
* The joiner's "only the host saves" lock is a **lease**: the agent renews it every second; if the agent is killed or freezes, the game
  releases it on its own 20 s later (it used to stay until the next load). A normal agent exit releases it at once.

**Part A — the scene guard (`mp_scene_guard`, on)**
* Re-measured first. In today's builds the joiner's long black screens came from two causes, not from cutscene end positioning: a talk
  with one of the host's people that never got its dialogue stand-ins (20 s, six times), and a conversation's scene that never ended
  (about 26 s, four times). Cutscene end positioning stalls (the old main cause) did not appear since 0.42.2.
* Bounds now: a conversation that makes no dialogue stand-in in 3.5 s is ended the game's own way; a conversation's scene still running 2 s
  after the conversation is released (was 20 s); a scene's end positioning is bounded 2 s past the later of the host's and this machine's
  end, then the screen comes back and the player stands up. The end-of-scene signal stays at the engine's real release (WO-151).
* Every fade on and off is logged on both machines (`WO179-FADE`, with the scene and how long it lasted past the scene's own end).

**Part B — the scene bus (`mp_scene_bus`, on)**: both games tell each other when a shared scene starts, where Henry ends up, when and how
it ended, skip presses, landings and time skips (side channel kind 15, append-only). Either player can be the one who triggers it.

**Part C — shared scenes**
* C1 videos (`mp_scene_video_shared`, on): when one player's game starts a pre-rendered video, the other game plays the same video if its
  own story copy has not started it within 1 s (from the game's own video table), and ends it on the same beat as the triggerer's.
* C2 scenes that move Henry: the DLL watches the local player's position 20 times a second; a jump during a scene, its end fade or a black
  screen is a relocation, sent at once with the destination. A scene that ends more than 30 m from where it started is the fallback. A
  joiner's scene that skipped time has that skip applied once in the host's world. Crime punishments and level changes are only logged
  (`WO179-SKIP`).
* C3 the quiet exit (`mp_scene_fight_clear`, on, its own code path): before the move the partner's conversation is interrupted the game's
  way, a fight he is in ends with the game's own stop-fight message to the enemies and end-of-combat on him (no crime record touched), he is
  taken off his horse, minigames, stations and carries are aborted the game's way; a dead or downed partner is waited for.
* C4 the landing (`mp_scene_bring_along`, on): a free ground spot 3, 6 or 10 m from the relocated Henry, behind a short fade (or behind the
  video). Then for up to 10 minutes, or until the partner walks 60 m away, the host's people near him are told by the game's own perception
  call to ignore him (`mp_scene_partner_ignored`, on).

## Checked live (solo, a synthetic partner)

| check | result |
|---|---|
| the host's relocation brings the joiner along | 119 m -> 3 m beside, behind a 0.5 s fade out and back in |
| the host's video: the joiner watches too | played 1.0 s after the triggerer's start, the move under it, ended on the triggerer's beat |
| the joiner's relocation brings the host along, in the host's own world | 105 m -> 3 m; the ignore window started |
| the joiner's relocation skipped 2 h | applied once in the host's world; the repeat refused |
| the agent killed while the joiner's save lock was held | released by itself after 23 s; the game then saved |
| the publishing gate | clean repository passes; a planted commit fails with 3 hits |

## Not done, or not proven

* **Both players must press skip** is not built: the game's own video skip could not be held back from outside in the time box, and the
  game draws nothing over a video, so "Waiting for your partner to skip" cannot be shown. A skip press is sent to the partner and logged; a
  copy the partner plays ends when both pressed.
* Detecting a real story relocation at the source and the Part A bounds were not reached in a real scene (no throwaway save sits right
  before one); the stuck conversation scenes and the stand-in wait do not happen with one player.
* The ignore window's effect on real guards, the fight clear on real enemies, and the ladder's talking and riding steps were not staged.
* The frame-rate comparison with 0.50.6 was not run.
* The two-shared-scenes-at-once rule is "hold the second and offer it after the first"; with two players that both relocate at once it may
  swap their places — a two-player check.

Everything above is on the two-player checklist (§WO-179).
