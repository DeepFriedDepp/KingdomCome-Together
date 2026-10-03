# WO-154 — progress

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

The answer and the evidence: `docs/WO-154-findings.md`.

Unattended run (the WO's rule): no stop to ask; the conservative option taken and recorded under "Decisions made
unattended" in the findings. Three usage-limit restarts; the work carried on from the notes each time. The
maintainer was present for the last part and gave two instructions mid-run: the soaks on the mod version only (no
vanilla comparison), and (answered) that the game opening and closing during the join trials was intended.

| part | status | where |
|---|---|---|
| WO-150/153 leftovers | pushed first (WO-153's three commits); `v0.43.0` already existed | — |
| the evidence | the evening's and the tutorial evening's zips and the testers' zips read by six investigators; every WO line counted; the log wins | findings §E |
| 1 — questing together | ports on every thread; contested States; coalesced AI steps; 0 failed to apply in the replay | findings §1 |
| 2 — the partner's figure | identity from spawn, kept, fail closed; knockdowns mirrored both ways | findings §2 |
| 3 — fighting together | the host a real target; no copy resumed; death and respawn clear fights; fair crime; end-combat | findings §3 |
| 4 — joins | the panel through the hold; patient loads; plain reasons; **the game's video-player freeze found**, the menu's video stopped first, a frozen load told plainly | findings §4 |
| 5 — riding | the ridden horse on the native writer, gait hysteresis; 50 % → 0 % frozen frames on both screens | findings §5 |
| 6 — the rest | fast travel off; far/seated binds; the skip line; the female NPC; the minigame rule's 21 States; the exit exception; voice off | findings §6 |
| 7 — Windows blocking | the launcher's plain messages, the relay reused (the launcher stream) | findings §7 |
| 7b — the mod menu | Insert; host-locked settings; remembered; proven in frames on both roles | findings §7b |
| 8 — docs | release notes, tester page, quick start, README, checklist §WO-154, Discord text, installer testing | findings §8 |
| 9 — proof and build | gates, the soaks (mod only), the live passes, the install pass, the self-review, the installer, the privacy sweep, the tag | findings §9 |

## Work log

1. **Setup.** WO-153's commits pushed. All 373 save files backed up with a sha256 list before anything else; the
   throwaway playline is playline4 (copies of the evening's saves). Every gate run once on the unchanged tree
   (native 377, agent 852, relay 62, setup 64, 43 synthetic suites, 3 static checks): green. The installed mod's pak
   and manifest backed up.
2. **The evidence.** Six read-only investigators (quests, the set piece, the avatar's reaction, fights, joins, riding
   and Phase 6) wrote their reports into the scratchpad; the findings' §E is their table, every line re-counted.
3. **Phase 1** (`ba9756d`, `f4c1139`, `4e6ab75`, `17aab69`): the port attribution on every thread, the contest rule,
   bool corrections, the coalescer. [L1]: ports on worker threads; the host replay.
4. **Two side streams** in worktrees, merged fast-forward: the **mod menu** (`d23fc22`…`70732ec`: the keys pak,
   the agent half, the menu, the player page, `mp_fast_travel`) and the **launcher** (`09c6bec`…`8f22593`: the
   player's files key by key, voice off unless chosen, Windows blocking, the relay reused, CONNECT gating, the join
   messages and "Join with a new character").
5. **Phase 2** (`114be22`, `c53cee2`): the knockdown mirror, the identity from spawn. [L2].
6. **Phase 3** (`11e7888`, `4a6fa6f`): the host target, the respite, fair crime, end-fights, the scene guard without
   resume. [L2], [L4].
7. **Phase 4** (`a5c5e62`, `f9c26db`): the join panel, patient loads, the reasons. [L3], [L4].
8. **Phase 5 and 6.2** (`0912010`, `8838065`): the ridden horse on the native writer, gait hysteresis, far and seated
   binds. [L5].
9. **Phase 6.3–6.6** (`d18fa15`): the skip line, the female NPC's log line, the minigame rule against the game's own
   quest data (21 States; the knight's dice shared), the exit exception (a test reproduces the field's order).
10. **The joiner pass** [L6]: the host's figure's knockdown on the joiner's screen, the menu with a host's real values,
    the 6.3 line live, far and seated binds live.
11. **The freeze** [L7]: a join's load froze the game for good; a stack walker on dbghelp (no debugger here) showed the
    game's own video player deadlocked at the loading screen, no KCDMP frame on any stuck stack; the field's hung join
    has the same signature. [M1]: `wh_ui_StopMovie` stops the menu's video and a load after it works. `ba0df03`: the
    stop-video step, the frozen-load message, the host-abort keeping the file.
12. **Trials** [JT]: 10 real joins, old and new flows alternating: all loaded, saves intact after each.
13. **Riding on the joiner's screen** [L8]: 50.1 % → 0.0 % frozen frames, ~56/s → 0 clip mismatches.
14. **The simulated freeze** [L9]: the detector never fired (its window never filled at the probe's real spacing);
    `dff5d38` fixed it; [L9b]: FROZEN at 90 s, the host released, the line shown, the file kept then removed.
15. **Fast travel on the host** [L10] and the host's menu frame.
16. **The version** (`ab2426e`): VERSION 0.45.0 (the maintainer's number, from the WO), the README badge and rows, the
    rebuilt pak; the Phase 9 tools (`a449638`): the partner soak, the mod-only verdict, the settings test, the
    Verify-Install markers.
17. **The install pass**: the candidate installer (built in a throwaway clone under the git-ignored `release\`; a clone
    under `%LocalAppData%` fails the setup tests here because of the sandbox's redirection) — 24 / 24 in the settings
    test from 0.43.0 and 0.44.0, the launcher alive throughout; the test launcher's own daily log, written into the
    player's `%AppData%\KCDMP_Launcher`, moved aside afterwards.
18. **Docs**: the findings, this page, the release notes, the tester page, the checklist §WO-154, the quick start, the
    README, the Discord text, the installer testing page.
19. **The soaks** (the mod only, the maintainer's call): soak 1 (the mod) and soak 2 (the mod + a joined partner through
    six fights), 10 minutes each, the window focused: 71–74 FPS, no `FAULT`, the DLL's cost flat at ~0.65 ms a frame.
    Soak 1's single stack row of 2 was a mid-frame read: the reader now takes the settled value; soak 2's two dips
    were the game window losing focus while the maintainer typed (his message at 14:16:19.6 lands in the dip). The
    record is soak 2's, PASS. Final gates: all green.
20. **The build**: pushed; the shipping installer from a fresh clone of `origin/main`; the privacy sweep; the tag (the
    record: the next commit).

## Housekeeping

* The throwaway files in playline4 removed at the end; `tools`-side scratch (bundles, runs, frames, stacks) stays in the
  session's scratchpad, never committed; field logs never committed.
* The installed mod's pak and manifest restored to the backup taken at the start.
