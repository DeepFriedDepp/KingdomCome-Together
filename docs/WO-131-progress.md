# WO-131 — combat and bodies: progress

Unattended session (the maintainer away). Decisions are recorded here with
their reason, as they were made. Evidence marks: (observed), (code-verified),
(synthetic), (inconclusive).

## Phase 0 — the repo

- `origin` = `DeepFriedDepp/KingdomCome-Together`, `main`, clean, level with
  `origin/main` at `1bf8aee` (observed).
- **Version note.** The prompt names the current release 0.30.5; the repo's
  `VERSION` file reads **0.30.2** and `origin/main` has no 0.30.3–0.30.5
  commit (observed). No version was given for this WO, so `VERSION` is left
  exactly as it is and no installer is built.
- The 0.30.2 field logs the prompt summarises are not on this machine; the
  prompt's own summary is the field record used here.

## Root causes found in code (before any change)

| # | symptom (field) | cause | mark |
|---|---|---|---|
| 3 | a wandering NPC alive on the joiner 470 m from the host's copy, killed there, the kill forwarded | (a) a puppet whose stream went silent was released and **resumed** after the 10 s dwell (the host culls past 60 m): a free brain at a stale spot; (b) NPCs the host never streamed were never touched on the joiner; (c) hits are name-addressed with no binding or distance check; (d) a joiner-local death went out as FATAL | code-verified |
| 4 | guards ignore the joiner's avatar | `ghostsIgnorant` default true; lifted only 30 s after an attributed hit | code-verified |
| 1 | 64 + 25 damage, dead in two hits | the forward is the avatar's measured health loss; the avatar carries `kcdmp_avatar_guard` (imm) and nothing ever restored its health, so after the first hits it sits at the 1 hp floor and later hits measure little or nothing; armor = the peer's items on a roster soul with its own stats | code-verified |
| 7 | the fight survives a respawn | `StopFight` ran on knockdowns only; nothing on the host touched the NPCs' skirmish with the avatar | code-verified |
| 5 | `cap_attack=0` both sides | player: the combat actor is a `C_CombatPlayer` (RTTI, resolved offline from CombatModule's COL at `+0x612B28`), which the exact-vptr test rejected; NPC: `ca+0x2D8` is the owning `C_Actor`, which the CEntity name helper refused (`+0x5BF030` = plain `C_CombatActor`) | code-verified |
| 1 | `npc_rows_out=0` | the NPC capture never produced a name (item 5); also `npc_rows_out` only ever counts on the host | code-verified |
| 6 | road-encounter NPCs exist only on the host | no spawn-on-demand anywhere; streams, hits and rows for a name with no local entity are dropped | code-verified |

## Decisions

1. **1a: parked = suspended + hidden** (not parked in view). A parked body
   stands where the joiner's own world put it, which is not where the host's
   NPC is; showing it would be a second, wrong copy that can be fought,
   talked to or looted. The host adds a 2 s heartbeat for its tracked NPCs
   out to 150 m of any player (past the 60 m full-rate cull), so the joiner
   binds the host's real NPCs around it and only NPCs the host has elsewhere
   vanish. Horses and animals are not parked (a horse has a rider's rules;
   animals are local herds).
2. **1a: a released copy is parked, never resumed**, while the guard is active.
   Resume happens only when the guard goes off (agent gone for 10 s,
   disconnect, `mp_npc_guard off`, host authority off).
3. **1b: the gate is native** (pipe 0x21 op 1): bound by the DLL's writer, the
   newest host sample ≤ 3 s old, the body ≤ 3 m from that sample, not dead on
   the host. No answer = drop.
4. **1c: the copy never dies locally**: the existing `kcdmp_avatar_guard`
   (imm=1, upr=1) goes on every puppet; the copy's health is written from the
   host's stream (credited to the sampler so it never echoes); the guard comes
   off right before the host's death is applied. A copy pinned at the imm
   floor forwards its measured drop + 1 so the host's own health decides.
5. **1c looting: blocked on the joiner** for host-owned NPCs, with a plain
   message ("Only the host can loot bodies in co-op for now."). Loot as a
   request to the host was not built: it needs an inventory transfer protocol
   and the host-side item move, which is larger than this WO. The block wraps
   the game's own `BasicAIActions.OnLoot` and `OnPickpocketing` (the functions
   the body interaction calls), so it covers every body and every living NPC
   (pickpocketing duplicates the same way). Horses, animals, graves: untouched.
6. **1d: perception** — on the host of a shared world, avatars are never
   AI-ignorant, and the avatar's faction node is re-parented onto the local
   player's own faction (the WO-16/17 attach with the player as donor), so
   Henry's enemies are the avatar's enemies by the game's own tables. The
   avatar's own contexts (crime_*, combat_disableAllSkirmishBarks, the civic
   isolation set) and its native combat-automation-off are unchanged: they are
   what stop it barking, starting fights or reading as a crime victim.
   `mp_avatar_perceive off` goes back to the old rule.
7. **1e**: first the direct fix (restore the avatar's health after each
   measured NPC hit, so damage keeps flowing); the armor comparison is a live
   measurement (below).
8. **1g**: `StopFight` on the local player's death respawn (native), and on the
   host, `StopFight` on the joiner's avatar when the joiner dies and wakes;
   the engagement window ends too. Crime is untouched.
9. **Phase 3**: the countdown cancels only under 630 m (650 − 20); the dialogue
   hold counts only a conversation the player is in: `IsInDialog` **and** the
   engine's `DialogTwin_<player>` stand-in exists (WO-90: the conversation
   camera hangs from it; barks spawn none).

10. **1f: stand-ins under the host NPC's exact name**, so every by-name path
    (the puppet, the native bind and its WUID check, the hit gate, the host's
    death, the loot rule) works unchanged. The look is approximate (a road
    bandit wears an authored bandit soul, a Cuman a Cuman soul, anyone else a
    roster commoner). Horses are flagged by the host (stream bit 0x80) and
    never get one. Animals: not streamed, not done.
11. **2b: stand up, never hold a lying body upright.** The native writer drops
    a bound body whose physics stops being a living entity (checked twice a
    second) with reason `not-living`; Lua calls `actor:StandUp()` when the
    host's stream says alive and awake. A body the host has KO stays down.
12. **Test rigs.** Spawned test souls (autotest bandits, a road-ambush soul)
    never attack anyone on their own, not even Henry (they are homeless, no
    situation); a live bandit camp's reactions are scripted on the player.
    Wolves (the game's own spawn) were the only unprovoked attackers found
    without input. This is why 1d/1e/2a are partly (inconclusive).

## Log

- Stage 1 (code): native `wo131.cpp` (pipe 0x21 → 0x98), capture fixes in
  `motion.cpp`, respawn StopFight; agent `GameBridge.Wo131.cs` + `Wo131.cs`;
  Lua `KCD2MP.w131`; leash hysteresis. Agent tests 387/387 (12 new)
  (synthetic).
- Run A (host, avatarpeer joiner, a forest 290 m from the nearest NPC, then a
  bandit camp 1.9 km away): faction join read back `player`; wolves targeted
  and bit the avatar and it joined Henry's side of their skirmish; perception
  off: wolves still attack (not discriminating); spawned bandit souls attack
  nobody; the live camp warned Henry at 4 m (`CAMP_TRESPASS_CHAT`, weapons
  drawn — Henry moved away before it became a fight or a dialogue needing
  input) and ignored the avatar at 4 m; the peer's death → StopFight → the wolf
  dropped the fight. `RequestAction(attack/freeAttack)` null on NPC and player.
  Tools added: avatarpeer `--record`, `death`, `respawned`, `vitals`,
  `appearance mirror`; synthpeer `raw`, `die`, `hp`, `--claim-host`, and it
  prints received hits. 5 autosaves the host agent wrote were moved out of the
  playline (test files).
- Run B (joiner): guard on (52 parked), identity binding, stand-in, hp follow,
  host death at the host's position, loot block (toast), CSV 194 s: only the
  200 m edge row. Phasing reproduced by a lethal local blow; the hit gate
  dropped every hit as `no-answer` (a slow soul lookup; fixed by entity id).
- Run C (joiner): the gate still `no-answer` → found the reader-loop deadlock
  (the LocalHit handler awaited on the pipe's own reader); fixed, agent
  hot-swapped: forward and `not-bound` both observed. Attack rows: no swing.
  Manual `StandUp` fixes the phasing. The hard agent kill exercised the Lua
  backstop (everything given back after 10 s). CSV 249 s: 0 free humans.
- Run D (joiner, the native not-living drop): automatic stand-up observed;
  save load re-parks in ~1 s; release → park observed; CSV found the re-pause
  defect (bodies the load re-created, hidden but not suspended) — fixed.
- Run E (joiner): the fix; a save load (3 far bodies free for one sample); the
  local player's death (grave, wake inside the leash, StopFight sent).
- **Focus incident (run E launch):** the game window came to the foreground by
  itself while loading the save (`foreground=True` after the load). Nothing
  was typed or clicked. Returned at once: minimise (the OS activates the next
  window) and restore without activation at the bottom of the Z order;
  `foreground=False` for the rest of the session (checked after every load).
- Gates (no installer): relay 50/50, agent 387/387, 27 synthetic suites
  green (WO-131 79/79; WO-114 and WO-118 adjusted, see the findings §6), both
  static checks, native 47/47, local publish + payload smoke. The tracked
  `kdcmp.pak` is rebuilt from the sources.
- Run E CSV: 528 s, 72 names, 6 free human rows (one sample at a save load).
- Clean-up: the test game's original `kdcmp.pak` restored (hash checked);
  `kcdmp-combatwrite.txt` removed; 5 host autosaves (run A) and 1 (run B's
  first, mis-roled start) moved out of playline1 into the session scratchpad;
  no joiner run wrote a save.
- Run F (host, bandit camp): Henry trespassed with his weapon drawn, the
  avatar beside him wearing what it could of Henry's gear (4 of 7 items took).
  Two campers fought and killed Henry (hits 12.9, 39.2, 26.2, 20.7 hp); the
  avatar was on Henry's side of the skirmish but never attacked, so the 1e
  comparison still has no avatar number. Wake 487 m away, StopFight, no chase.
  The host's NPC rows: `cap_npc=5`, `npc_rows_out=5` (the capture fix, live).
  **Incident:** straight after the avatar's attributed hit on a camper, a
  civilian caravan passing the camp road got into a brawl with a camper and one
  caravan civilian died -- in the host's throwaway world, never saved. The camp
  turned out to border a settlement; no further runs there. 2 host autosaves
  moved out of the playline.
- Run G (joiner): the recording's rows replayed; the camera first sat behind a
  rock and a bush (re-aimed; the synthetic host's 120 s ran out once and the
  real game briefly became the authority: 4 autosaves moved out). The last
  replay, in open ground: both copies swing (sheets 7-8). The same camp
  bordered a settlement on the joiner side too (a "Surrender" prompt appeared
  while the camera search placed Henry there); Henry was moved away at once,
  excluded from targeting, and no fight happened.
- The test game's original `kdcmp.pak` restored again (hash checked).
