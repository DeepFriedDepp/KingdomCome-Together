# WO-135 — the avatar is a puppet, and four more fixes: progress

Findings: `docs/WO-135-findings.md`. Started from `69b1fb0` (`origin/main`,
clean, fast-forward only). No force-push.

## Log

- Read WO-119, 121, 131, 132, 134, 125, 115 findings; mapped the avatar's
  contexts, crouch capture, outfit apply, Henry source pick and knockout paths.
- Kept the host's 0.30.9 session logs from the game folder (before any launch
  overwrote them) and read them: the avatar's dialogues by kind, the plate's
  layering refusal, ten preset unequips that failed at the join, the joiner's
  streamed blocks.
- Extracted the game's Tables, Scripts and scriptbind docs to the scratchpad:
  the script-context table, the brain's switch trees (`interrupt_attack`'s
  automation decorators), the knockout buffs, `BasicAIActions` (the takedown
  interactions), the combat-shout dialogues.
- Disassembled (DialogModule) the dialogue-start function and its log builder
  (speakers / listeners), and (EntityModule) SetCrouch / GetCrouch.
- Built in the order: native (quiet groups, crouch diagnostics + test verb,
  copy-guard knockout/wake modes), Lua (W135 module), agent (MotionConfig byte,
  events, loot-channel kinds, outfit rewrite, same-build pick, host build
  announcement), launcher (one-button states), test peers (avatarpeer
  `takedown`, state printing; synthpeer `npcflags`, `build`, `hstate`,
  takedown answers).
- H1 → the dialogue gate (the contexts could not silence the hit screams) → H2
  → the avatar stand-up (the maintainer's report) → J1 → the copy wake Revive
  and the guard-during-load fix (the maintainer's report of the red post-load
  error) → J2.
- Decisions:
  * speech is a native gate, not a context: every context route left the
    screams voiced (observed);
  * listeners are never refused: another NPC may still shout at the avatar;
  * a joiner's takedown is performed by his avatar in the host's world (the
    game's own animation on the host's screen), with a direct engine result as
    the fallback;
  * the outfit diff is against the real equipped set; a refused class is
    skipped only until the outfit changes;
  * the host's build rides the existing loot channel (a new kind, append-only;
    no protocol bump: the release check already refuses mixed builds);
  * a woken copy is Revived (its brain is paused, so it cannot get up by itself).
- `VERSION` → `0.40.0` (the maintainer's string). Installer built locally
  (not uploaded; nothing is on GitHub).

## Gates (observed, the last `tools\Build-Installer.ps1` run, exit 0)

| gate | result |
|---|---|
| relay round trip | 51/51 |
| agent unit tests | 443/443 (new `Wo135Tests.cs`, 9) |
| synthetic suites | all green (new `Test-WO135Synthetic` 40/40; `Test-WO131Synthetic` 79/79 unchanged; `Test-WO134DropsSynthetic` 55/55) |
| static checks | console placeholders 7/7, Lua locals 6/6 |
| native unit tests | 53/53 |
| local publish + payload coherence + smoke | pass |
| installer | `release\KingdomComeTogether-Setup-0.40.0.exe`, 95.5 MB, sha256 `85bec6be74a2b78c6bc7df1cc5a7988eecf0456e95a50c377f3f54fa73b693ae` (local only; not uploaded) |

`Verify-Install.ps1` knows the WO-135 markers (agent `MP-WO135-STATS`; DLL
`WO135-DIALOG`, `WO135-QUIET`; pak `KCD2MP_W135KoTick`, `KCD2MP_W135HostTakedown`).
Not done from this shell (the AppData sandbox rule): installing it; the
maintainer installs over 0.30.9 and runs `tools\Verify-Install.ps1`.

## Side effects on this machine (disclosed)

- **Stopped the maintainer's launcher, agent and relay** (left running from the
  0.30.9 session, no game open) before the first launch, so they would not
  attach to the test game. Restart them from the shortcut.
- **Three old autosaves were overwritten and are lost:** the host-world
  autosaves my hosting agent triggered in H1 reused the names
  `playline1/autosave085`, `088`, `089`, which already held older saves (dated
  April 1). There was no copy of them. After finding it, every playline was
  copied to the scratchpad before each later run and a guard moved new files out
  and restored overwritten ones; `playline1` is back to its 82 original names
  (the three with my files removed), `playline0`, `2`, `3` untouched.
- **Focus:** (1) after the J1 world load the game held the foreground for about
  one to two minutes before I pushed it down (the known load trap). (2) I then
  ran a watcher that pushed the game down whenever it was the foreground window;
  the maintainer was looking at the game and saw it as screen jitter — it pushed
  the window down 50 times in about three minutes. Stopped at once when
  reported; not used again (only one push-down right after my own load in J2).
  No key or mouse input was ever sent.
- **The maintainer's character was read** (not written): J1's Bring took the
  Henry of `playline1/autosave084` (the newest same-build save) into the
  synthetic host's world; the joined world lived only in the join staging and
  the agent's own data folder, never saved into a playline.
- In H2 a spawned test wolf turned on Henry (death guard buff did not hold);
  WO-113's native death put him in a grave and woke him 360 m away — in the
  throwaway world, not saved.
- The test game's installed `kdcmp.pak` is the WO-135 build (the previous one is
  in the scratchpad). Test NPCs (`wo135_*`, `wo132_wolf`) were spawned, knocked
  out, woken and killed at the open field away from settlements; nothing stolen.
