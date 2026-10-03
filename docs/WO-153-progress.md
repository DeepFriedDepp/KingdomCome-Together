# WO-153 — progress

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

The answer and the evidence: `docs/WO-153-findings.md`.

Unattended run (the WO's rule): no stop to ask; the conservative option taken and recorded under
"Decisions made unattended" in the findings. `VERSION` was blank, so: commits only, no installer, no
soak (§D 1).

| part | status | where |
|---|---|---|
| the tag | `v0.43.0` → `b28ac78` (the commit the 0.43.0 installer was built from; lightweight, like `v0.42.8`), pushed first | — |
| the evidence | all nine zips unpacked; every WO line counted in the logs by six read-only investigators; the log wins where the text differs | findings §E |
| 1 — the herb crash | fixed safe: `mp_avatar_herbs` off by default; pick-ups never aligned; the engine cause is an Opus follow-up | findings §1 |
| 2 — copies never act on their own | the proven parts fixed (writer hand-over, dog, engage rule, contexts, discard table, cart retry, no toast); the rest is decided or follow-up | findings §2, §D 5 |
| 3 — no dropped commands | batches kept and sent again in order; the hold; the pipe loss declared at once; the cancel and appearance lines plain; the cause of each reported plainly | findings §3 |
| 4 — one body per NPC | the race closed in the agent and the mod; `UNRESOLVED`; no stand-in over a body | findings §4 |
| 5 — the smaller ones | faults, temp tools, doors fixed; the dog is not ours | findings §5 |
| the `mark_odd` moments | read; four new findings, not fixed | findings §6 |
| docs | findings, this page, the README row, `docs/TWO-PLAYER-CHECKLIST.md` §WO-153 | — |

## Work log

1. **The tag and the baseline.** `v0.43.0` pushed. A sha256 list of all 362 save files written before
   anything else; the game was never started, so no save was touched. Every gate run once before the
   changes: 42 synthetic suites, 3 static checks, agent 825, relay 62, native 369 — all green.
2. **The evidence.** Six investigators (read-only, scratchpad only) took the nine zips by topic:
   fights, movement/actions, dropped commands and the pipe, bodies and the missing dog, the faults / temp
   tools / doors, and the `mark_odd` moments. Their reports are the §E table. The host's clock is 60 min
   1.7 s ahead of the joiner's; `history\launch-*` names are END times.
3. **Phase 1.** `Wo143Rules.AvatarShow` / `MayAlignAt`; `mp_avatar_herbs` (Lua switch, console command,
   agent setting, sync, stats line); tests in `Wo143Tests` and `Test-WO143Synthetic.lua`.
4. **Phase 4 (the race).** `KCD2MP_NpcDeathHeld`, `KCD2MP.w153`, the `OwnerDeathCheck` / landing changes,
   `UNRESOLVED`; the agent's `Wo153DeathHeld`; the stand-in refusal over a body. New suite
   `tools/Test-WO153Synthetic.{lua,ps1}`; `Test-WO131Synthetic` (i2).
5. **Phase 3.** `BatchQueue.cs` + the rewritten batching in `HttpGameTransport` (the queue, the hold, the
   once-guard, `RetryBatches`); `KCD2MP_Once` in `kdcmp.lua`; `ClientConfig.BatchRetryEnabled`; the pipe's
   reader exit; the cancel and appearance lines; the yaw loop only in HTTP mode. `Wo153Tests` (a fake game).
6. **Phase 2.** The toast; `wo141::next_delay`; `NPC_NATIVE_UNBIND_HOLD_S`; `KCD2MP_W131PauseOnly`;
   `JudgeEngage`; `CopiesNeedingContexts`; the discard table. `Test-WO118/WO102/WO131Synthetic`, `Wo132RulesTests`.
7. **Phase 5.** `fault::plausible_address` and `motion.cpp`; `DeleteItem` counts and the 90 s tool hold;
   `Wo136DoorKey` and the door coalescing in the load hold.
8. **Gates, docs.** See below.
9. **The installer** (0.44.0): built on the maintainer's instruction with the soak waived
   (`-SoakWaiver`), inside a fresh clone at `a5da586`. `release\KingdomComeTogether-Setup-0.44.0.exe`,
   102,031,590 bytes (97.3 MB), SHA-256 `13f8fb0b9580128a524444770979356bb1986992f40c7938ef16ec0620b2bb28`,
   beside `release\SOAK-WAIVED-0.44.0.txt` (git-ignored; there is no GitHub release). Inside the build:
   relay tests 62, agent tests 852, Setup tests 64, `RELAY-SMOKE ok protocol=v10 release=0.44.0`.
   Two false starts, both recorded: (a) the first clone sat under the scratchpad's very long path and
   16 of the 64 Setup tests failed there (they pass 64 of 64 in the repo and in a short-path clone: a
   path-length effect, not a defect); (b) the payload privacy sweep found the maintainer's Windows user
   name in `KcdMpSetup.exe` (the native linker's PDB path, which Roslyn's PathMap does not cover);
   fixed in `KcdMp.SetupHost.csproj` and the installer rebuilt, the first one discarded.
   Checks on the final payload: the payload smoke (1,066 assembly entries, `failed: 0`), `Verify-Install`
   against the payload (every WO-153 marker present; only the dice keys and Setup's own verify file,
   which Setup writes, are missing), and the privacy sweep over all 1,038 payload and mod files (ASCII
   and UTF-16): none of the field players' names, the maintainer's user name or e-mail, Steam IDs or
   addresses; the matches left are the project's own repository name, words inside stock runtime tables,
   and the NAudio author's build path in its six NuGet DLLs (as in 0.43.0).

## Gates (HEAD `07ca5aa` + the docs commit)

| gate | result |
|---|---|
| every synthetic suite (`Test-*Synthetic.ps1`, 43 with the new `Test-WO153Synthetic`) | 0 failed |
| the three static checks (`Test-WO106ConsolePlaceholder`, `Test-WO110LuaLocals`, `Test-NativeGuards`) | exit 0 |
| agent tests | 852 passed (825 before) |
| relay tests | 62 passed |
| native tests | 377 passed (369 before; the native build is clean) |
| the payload smoke | see work log item 9 |
| the frame-rate soak | **waived by the maintainer; not run** (findings §D 1) |

## Not done

* **No live run of any kind.** Everything is unit and synthetic; `docs/TWO-PLAYER-CHECKLIST.md` §WO-153
  lists what needs two players, and a solo joiner/host run of the batch hold and the herb switch is the
  first thing to do with a throwaway save.
* The follow-ups in findings §F (herb fragment, combat actor lifetime, skirmish manager, the `Action`
  caller, the console server's queue, the talk wraps, quest ports, the pipe's connect hook, the horse
  detach).
* The stand-in removal by entity id; the dead-resync stand-in leak; the class list beyond `Dog`.
* Not pushed: the WO-153 commits (and the two WO-150 commits before them).
