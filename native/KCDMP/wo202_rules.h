// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-202: the native half's pure rules (wo202.cpp and the files it changes), engine-free so native/tests pins them.
#include <cstddef>
#include <cstdint>
#include <cstring>

namespace kcdmp::wo202rules {

// ---- the agent's switch word (op 88), the same bits as Wo202Rules.ConfigBits ----
// (8, 128 and 256 were the draw, the walk start and the dead copy's items: no mechanism was built for them -- open items)
constexpr uint32_t kBitCrash = 1, kBitNearSafe = 2, kBitQuestFail = 4, kBitAnimShown = 16, kBitRecoilClinch = 32, kBitFightPosFresh = 64,
                   kBitDeathEngineOnly = 512;

inline bool contains_ci(const char* s, const char* needle) {
    if (!s || !needle || !*needle) return false;
    const size_t n = std::strlen(needle);
    for (; *s; ++s) {
        size_t i = 0;
        while (i < n && s[i] && ((s[i] | 0x20) == (needle[i] | 0x20))) ++i;
        if (i == n) return true;
    }
    return false;
}

// ---- II.4 quest_fail_trace ----
// A quest State change that is a failure: its Set method names a failure, an abort or a cancel (SetFailed, SetAborted, SetCanceled,
// SetCancelled, SetFail...), or it sets a "player bailed" value (the story's own words: hrac_se_na_to_vykaslal, HracSeNaQuestVykaslal) to
// anything but its zero.
inline bool is_fail_change(const char* path, const char* port, int32_t newI) {
    if (contains_ci(port, "fail") || contains_ci(port, "abort") || contains_ci(port, "cancel")) return true;
    return contains_ci(path, "vykaslal") && newI != 0;
}

// The kind of the node whose out-port pulse made the change, by its class name (rtti ".?AVC_Timer@rpgmodule@wh@@" and the like), in words.
inline const char* source_kind(const char* cls) {
    if (!cls || !*cls) return "unknown";
    if (contains_ci(cls, "Timer") || contains_ci(cls, "TimeOfDay")) return "timer";
    if (contains_ci(cls, "Distance")) return "distance-trigger";
    if (contains_ci(cls, "Area") || contains_ci(cls, "Volume")) return "area-trigger";
    if (contains_ci(cls, "Watch")) return "watch";
    if (contains_ci(cls, "Dialog") || contains_ci(cls, "Talk")) return "conversation";
    if (contains_ci(cls, "Behavior") || contains_ci(cls, "Behaviour")) return "behaviour";
    if (contains_ci(cls, "Waiter")) return "scene-waiter";
    if (contains_ci(cls, "Trigger")) return "trigger";
    return "node";
}

// ---- V.2 speech_share: the engine's own voice path ----
// The engine names a dialogue line's voice "Localization/dialog/<area>/<scene>/<file>" with no extension (the field's
// "Localization/dialog/trosecko/zbranePanaSemina/v_ta_pacholek__ale_dyt_ms_O8Jj"); WO-201 took only paths ending in ".ogg", so nothing
// was ever captured (0 lines on all four machines). A voice path: the Localization prefix, a dialog folder, a non-empty file name, no
// "..", 5 to 190 characters; either slash; an extension or none.
inline bool is_voice_path(const char* p) {
    if (!p) return false;
    const size_t n = std::strlen(p);
    if (n < 5 || n > 190 || std::strstr(p, "..")) return false;
    const char pre[] = "localization";
    for (size_t i = 0; i < 12; ++i)
        if ((p[i] | 0x20) != pre[i]) return false;
    if (p[12] != '/' && p[12] != '\\') return false;
    const char dlg[] = "dialog";
    for (size_t i = 0; i < 6; ++i)
        if (!p[13 + i] || (p[13 + i] | 0x20) != dlg[i]) return false;
    if (p[19] != '/' && p[19] != '\\') return false;
    const char last = p[n - 1];
    return last != '/' && last != '\\';
}

// ---- II.3 death_engine_only: the LocalHit sampler's FATAL bit ----
// The sampler read health only: a drop to 0 announced a death (the FATAL bit the host sends). A death is the engine's: the soul's own
// IsDead must read true as well. A soul at 0 that the engine keeps alive (a knockout, a mercy, an immortal) is a hit, not a death; its
// death, if the engine makes one later, reaches the joiner through the observer that watches the dead state.
inline bool fatal_is_death(bool healthZero, bool soulReadOk, bool soulDead, bool gateOn) {
    if (!healthZero) return false;
    if (!gateOn) return true;
    return soulReadOk && soulDead;
}

// ---- IV.4 fight_pos_fresh (host's native sender) ----
// A drawn, living, standing NPC within the engage range of ANY player (the host or a partner anchor) is sent every emit tick, whether
// it moved or not: a fighter in guard turns and steps in place under the send threshold.
inline bool fight_fresh(bool drawn, bool dead, bool ko, float dMinToAnyPlayer, float engageRange) {
    return drawn && !dead && !ko && dMinToAnyPlayer <= engageRange;
}

// The joiner's writer: how long past the host's newest sample a copy runs on at its last speed -- 0.3 s for a fighting copy (the
// stream's drawn bit), the walker's own limit otherwise.
constexpr double kFightExtrapMaxS = 0.3;
inline double extrap_max_s(bool fighting, bool freshOn, double walkerMaxS) { return fighting && freshOn ? kFightExtrapMaxS : walkerMaxS; }

// ---- IV.1 anim_shown (joiner): a copy's next swing never waits behind its own last one ----
// The host's NPC chains attacks the engine's way: its Attack action lowers the attack's anim action from priority 5 to 3 at the Withdraw
// (WO-197 read it: "its priority drops 5 -> 3 at the 0x20 Withdraw"), and the next attack (5) installs over it. A copy's swing is queued
// straight into its anim controller at 5 with no Attack action around it, so nothing lowers it: the copy's next swing waited out the whole
// previous fragment (session A's joiner: 53 of 182 never installed within the 0.6 s watch, 29 of them 0.6-1.5 s after the previous one).
// The base action (CombatModule 0x10BA90, read this WO): +0x24 the priority, +0x28 the status (2 Installed, 3 Exiting).
constexpr int32_t kSwingPriority = 5, kWithdrawPriority = 3;
constexpr uint32_t kActPriorityOff = 0x24, kActStatusOff = 0x28;
constexpr double kLastSwingKeepS = 2.5;   // the last swing's reference is kept this long at most (the field's chains: 0.6-1.5 s apart)
inline bool lower_previous(bool on, uint32_t status, int32_t priority) {
    return on && (status == 2 || status == 3) && priority == kSwingPriority;
}

// ---- I.2 near_safe (the WO-201 V.2 distance-trigger substitution, host) ----
// The partner's figure stands in for "the player" only on the main thread, inside the engine's own evaluation, with the figure's NPC
// looked up in that same call and unchanged for at least kFigureSettleMs (a new NPC object -- the soul re-created after a skirmish, a
// rejoin -- is not used while it is being connected).
constexpr uint64_t kFigureSettleMs = 2000;
inline bool figure_usable(const void* now, const void* stable, uint64_t stableSinceMs, uint64_t nowMs) {
    return now && now == stable && nowMs - stableSinceMs >= kFigureSettleMs;
}

} // namespace kcdmp::wo202rules
