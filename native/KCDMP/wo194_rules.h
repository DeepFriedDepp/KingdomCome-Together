// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-194: the pure rules of the shared in-game cutscene's native half (wo194.cpp). Engine-free: native/tests/wo194_tests.cpp pins them.
#pragma once
#include <cstdint>

namespace kcdmp::wo194rules {

constexpr uint8_t kRoleUnknown = 0, kRoleHost = 1, kRoleJoiner = 2;

/// B3: a held copy waits at most this long for the host's (then it plays solo).
constexpr uint64_t kHoldBoundMs = 3000;
/// The partner told us it plays a scene: remembered this long at most (its end clears it sooner).
constexpr uint64_t kPartnerPlayingTtlMs = 180000;
/// An enqueue of the node the mod just pulsed counts as the mod's start for this long (the pulse is synchronous; this is the margin).
constexpr uint64_t kModStartWindowMs = 2000;
/// After a release that only restored AutoPlay, the configuration is checked again this late (still initialized, never played = played).
constexpr uint64_t kRecheckMs[2] = {1500, 4000};
/// D: a scene's own time of day is switched off on the joiner until its Play ran; restored after this long whatever happened.
constexpr uint64_t kTimeGateMaxMs = 180000;

/// B3: the joiner's own in-game enqueue is held at "initialized" (AutoPlay cleared): the joiner only, the hold armed (a partner in the
/// session, the switch on), an in-game cutscene, not one the mod started (B2), and the partner is not already playing it (then it plays
/// at once and the clock aligns it).
inline bool hold_wanted(uint8_t role, bool holdArmed, bool ingame, bool modStart, bool partnerPlaying) {
    return role == kRoleJoiner && holdArmed && ingame && !modStart && !partnerPlaying;
}

/// D: on the joiner a scene's own time of day does not run (the host's clock gives it, as WO-137's quest time sets).
inline bool time_gate_wanted(uint8_t role, bool gateOn, bool ingame, bool hasTime) {
    return role == kRoleJoiner && gateOn && ingame && hasTime;
}

enum class Release : uint8_t { None = 0, Partner = 1, Bound = 2, Save = 3, Agent = 4 };

inline const char* release_name(Release r) {
    switch (r) {
        case Release::Partner: return "the host's copy runs";
        case Release::Bound: return "3 s bound (plays solo)";
        case Release::Save: return "a save request (never held across a save)";
        case Release::Agent: return "the agent (a menu, a load, the partner is not at that step)";
        default: return "-";
    }
}

/// Why a held copy is released now (None = keep holding). A save request wins over everything; the bound is the DLL's own, so a
/// hold never outlives a dead agent.
inline Release release_due(uint64_t heldAtMs, uint64_t nowMs, uint64_t boundMs, Release asked, bool savePending) {
    if (savePending) return Release::Save;
    if (asked != Release::None) return asked;
    if (nowMs >= heldAtMs + boundMs) return Release::Bound;
    return Release::None;
}

enum class Act : uint8_t { Gone = 0, RestoreOnly = 1, Pulse = 2, AlreadyPlaying = 3 };

/// How a release is done. Gone: the handler no longer holds this configuration (a load) -- nothing is written. The configuration
/// initialized and waiting (the player's active one, not in progress, the handler in state 2) = the handler's own PlayCutscene input;
/// still streaming = AutoPlay back on (OnCutsceneInitialized reads it later and plays).
inline Act release_act(bool nodeHoldsCfg, bool cfgActive, bool inProgress, int handlerState) {
    if (!nodeHoldsCfg) return Act::Gone;
    if (cfgActive && inProgress) return Act::AlreadyPlaying;
    if (cfgActive && handlerState == 2) return Act::Pulse;
    return Act::RestoreOnly;
}

/// The re-check after a restore: the active configuration that still does not play is played the game's own way.
inline bool recheck_stuck(bool cfgActive, bool inProgress) { return cfgActive && !inProgress; }

/// A: a copy's FastForward is stopped only on the joiner, with the switch on, never the local player's.
inline bool ff_stop_allowed(uint8_t role, bool on, uint32_t eid, uint32_t playerEid) { return role == kRoleJoiner && on && eid != 0 && eid != playerEid; }

} // namespace kcdmp::wo194rules
