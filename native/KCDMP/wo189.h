// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-189 -- 0.60.6's native half: the joiner never stays stuck in half of the host's quest sequence, and its saves never stall silently.
// No hook; every op is a main-thread call into the game's own functions, each found by its RTTI class or its own label (port notes:
// docs/WO-189-findings.md). The pure rules live in the agent (Wo189Rules).
//
//   readiness   the save readiness service (RTTI C_ReadinessService, embedded in CSystem -- found by its vftable among CSystem's bytes):
//               IsReady(BeforeGameSave = 0x2A) and, when false, the names of the tasks that hold it. CanSave(Quick) does not see this wait.
//   cutscene    the cutscene player (RTTI C_CutscenePlayer, a pointer of C_GUIModule, itself a pointer of the game interface): its active
//               configuration and "in progress"; a release the game's own way -- C_CutscenePlayer::PlayCutscene(player, &active) (found by
//               its label), the call the quest node's own PlayCutscene input makes; Interrupt (the vftable slot whose function references
//               its label) only as the last resort. Refused on the host.
//   fast-forward C_NPC's stop-FastForward (vftable slot +0x4B8, argument 0: the engine's own per-NPC call when a save lock cancels FF for
//               every NPC), for the copies the agent names only. Refused on the host.
//   positioning C_ScenePositioningManager's state (+0x44: 2 = waiting for the FastForward set), read-only, when its object is found.
//   profiles    the game profile manager (C_EntityModule::GetGameProfileManager, exported): EnableProfile (the vftable slot whose function
//               references its label) returns a C_ProfileHandle the mod keeps; its release (handle slot 0, "DisableProfile") switches the
//               profile off again. Only a profile that is not active on this machine is switched on (one the joiner's own quest holds is
//               never touched); only a handle this DLL took is released. Refused on the host.
//   concept     a quest State read through the WO-97 concept reader (read only; its value is logged in the native log).
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), main thread:
//   42 Status -> text
//   43 Config [role:1][release:1][ffStop:1][mirror:1] -> the four   (255 = unchanged; role 1 host, 2 joiner, 0 unknown)
//   44 Ready -> [armed:1][ready:1][posArmed:1][posState:1][posActive:1][posSecs:4f][namesLen:1][names]   (names: the not-ready tasks, ',')
//   45 Cutscene -> [armed:1][active:1][inProgress:1][cfg:8]
//   46 CutsceneRelease [mode:1][cfg:8] -> [result:1]   mode 1 PlayCutscene, 2 Interrupt; result 1 done, 0 refused (not that config / in
//        progress / none), 2 not armed, 3 fault, 5 refused on this machine (the host, or the switch off)
//   47 StopFF [maxDist:4f][n:1][eid:4]*n -> [n:1][res:1]*n   res 1 stopped, 0 no C_NPC for it, 2 farther than maxDist, 3 fault, 5 refused
//   48 Profile [sub:1] ...   sub 1 List [start:2] -> [total:2][next:2][names '\n']; sub 2 Enable [len:1][name] -> [result:1]
//        (1 enabled, 0 already active: nothing taken, 4 no such profile, 2 not armed, 3 fault, 5 refused); sub 3 Disable [len:1][name] ->
//        [result:1] (1 released, 0 no handle of this DLL's); sub 4 IsActive [len:1][name] -> [result:1] (0/1, 4 none)
//   49 Concept [len:1][path][len:1][port] -> [ok:1]   (the WO-97 reader, read only: the value is in the native log)
#pragma once
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo189 {

constexpr uint8_t kOpStatus = 42, kOpConfig = 43, kOpReady = 44, kOpCutscene = 45, kOpCutsceneRelease = 46, kOpStopFF = 47, kOpProfile = 48,
                  kOpConcept = 49;

// WO-194: the cutscene player (RTTI C_CutscenePlayer, a pointer of C_GUIModule) or null. Main thread.
void* cutscene_player_obj();
// WO-194: C_CutscenePlayer::PlayCutscene(player, &active) -- only when `cfg` is the player's active configuration and not in progress
// (the caller decides role and switches). 1 done, 0 refused (another configuration / in progress / none), 2 not armed, 3 fault.
uint8_t play_active(void* cfg);

uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);
int status_text(char* out, int n);

} // namespace kcdmp::wo189
