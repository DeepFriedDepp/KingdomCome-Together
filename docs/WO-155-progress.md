# WO-155 — progress

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

The answer and the evidence: `docs/WO-155-findings.md`.

Unattended run (the WO's rule): no stop to ask; the judgement calls are under "Decisions made unattended" in the findings.

| part | status | where |
|---|---|---|
| the evidence | the maintainer's two zips read; the cause confirmed (9 of 10 knockdowns after a host-NPC blow by 0.19–0.23 s, 1 after friendly fire) | findings, "The evidence, confirmed" |
| 1 no knockdown from a hit | NPC/animal blows suppressed; friendly fire kept with a 5 s window | findings §1, §3 |
| 2 the figure falls only on death | collapse, hold, respawn replacement; no Revive on a living figure | findings §2 |
| friendly-fire fall on the attacker's screen | the game's own `Actor.Fall`, handed back after the get-up | findings §2 |
| the T-pose (found on the way) | a native walk-class pulse after each bind | findings, "The T-pose, analysed" |
| 3 proof | both roles, solo, frames | findings, "Proof" |
| docs, release notes, tester page, checklist | done | findings, `docs/releases/RELEASE-NOTES-0.45.1.md`, `docs/TEST-0.45.1.md`, checklist §WO-155 |
| gates, soaks, installer | see the findings | findings |

## Work log

1. **Setup.** All 397 save files backed up with a sha256 list; the throwaway playline is playline4 (its five files). The
   installed 0.45.0 mod files (pak and manifest) backed up; the maintainer's launcher/agent/relay were not running and not
   touched; the harness of WO-154's session (launch, run, query, screenshot helpers) copied and reused.
2. **The cause**: the field zips (joiner's native and agent logs, host's kcd.log) correlated; `ApplyPlayerHitAsync` read.
3. **Live levers** (host, scripted partner): the plain and suppressed `TakeDamage` on the local player; `Actor.Fall`,
   `RagDollize`, the unconscious buff on the avatar; `GetPhysicalizationProfile`/`GetCurrentAnimationState` polling; the T-pose
   found and bisected (the writer binding during the get-up blend; then the generic bind T-pose); the nudge.
4. **Code**: agent (`GameBridge.Wo155.cs`, `Wo155.cs`), Lua (the fall/collapse/hold/replace block, three switches), native
   (`wo155_rules.h`, `hits.cpp`, `motion.cpp`), tests (native 18, agent 9, synthetic 74), the synthetic host's three verbs
   (`phit`, `ffhit`, `death`) and the agent's `mp_w154_check ffhit`.
5. **Live proofs**, both roles; two things found and fixed in them: the hold lifted a settling ragdoll (a terrain rule now),
   and the get-up hand-back needed the blend to have been seen.
6. **Version, docs, gates, soaks, build**: see the findings.

## Housekeeping

* The throwaway playline4 holds its five original files; the 392 real save files match the list taken at the start after
  every session (checked before each launch).
* The installed mod's pak and manifest are the maintainer's 0.45.0 again (sha1 checked against the backup); the test pak
  was never left in the Modding Tools folder.
* Scratch (bundles, runs, frames, logs) stays in the session's scratchpad, never committed; the field zips are never
  committed. `docs/wo155-shots/` holds eleven contact sheets of screenshots of the game window on throwaway saves.
