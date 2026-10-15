// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-187: the engine-free rules of the native half (wo187.cpp). native/tests/wo187_tests.cpp pins them.
//   A2  a copy's opponent: who it should fight on this screen, when it is re-applied, when it is pinned
//   C2  the guard between moves: a copy holds combat mode for the host NPC's whole fight; a figure keeps it through a stream hiccup;
//       a body whose full-body scope is empty after a move gets its idle back
//   B3  the copy's perfect-blocked (0x10) length by weapon class: the measured values where they exist, a logged default elsewhere

#include <cstddef>
#include <cstdint>

namespace kcdmp::wo187rules {

// ---- A2: the copy's opponent ------------------------------------------------------------------------------------------------
// Who a copy should fight on this screen (the agent's word, from the host's npc-combat target).
enum class Opp : uint8_t { None = 0, LocalPlayer = 1, Figure = 2 };
// A wish the agent stopped refreshing for this long is dropped (the host's fight ended, or its stream stopped).
constexpr double kOppStaleS = 4.0;
// The opponent is re-read (and put back if something else wrote it) this often while a wish is live.
constexpr double kOppRecheckS = 0.5;
// Overwritten this many times in one wish: pinned with the game's own forced-target relation (combat_forcedTarget, copy -> target).
constexpr int kPinAfterOverwrites = 2;

inline bool opp_wish_live(double now, double refreshedAt) { return refreshedAt > 0 && now - refreshedAt < kOppStaleS; }
inline bool pin_due(int overwrites, bool pinned, bool pinOn) { return pinOn && !pinned && overwrites >= kPinAfterOverwrites; }

// A wind-up's verdict on the copy's opponent: no wish (nothing to check), held (still the wished one), overwritten (something else
// wrote it since: the skirmish picker, the opponent manager's own requested target) -- the caller writes it back.
enum class WindupOpp : uint8_t { NoWish = 0, Held = 1, Overwritten = 2 };
inline WindupOpp windup_verdict(bool wish, bool matches) {
    if (!wish) return WindupOpp::NoWish;
    return matches ? WindupOpp::Held : WindupOpp::Overwritten;
}
inline const char* windup_name(WindupOpp v) {
    switch (v) { case WindupOpp::Held: return "held"; case WindupOpp::Overwritten: return "overwritten"; default: return "no-wish"; }
}

// ---- C2: the guard between moves ---------------------------------------------------------------------------------------------
// A copy whose host NPC fights anyone (the host's npc-combat: combat=1 or a target set) holds combat mode -- and with it the game's own
// guard idle under its swings -- for the whole fight. An NPC fighting a player reads CombatMode 0 on the host (WO-147), so the bit
// alone left the copy of an NPC fighting the other player with no guard at all: the neutral pose between its swings.
constexpr double kGuardHoldStaleS = 3.0;   // no fresh report for this long: the hold lets go
inline bool guard_hold_wants(bool inCombat, bool hasTarget) { return inCombat || hasTarget; }

// A partner's figure: its combat mode follows the partner's combat bit. A stream that went quiet for a moment (no fresh sample) used to
// release it at 1.5 s -- combat mode, the guard idle and the guard zone gone mid-fight -- and start it again with the next sample. Now a
// quiet stream keeps the last fresh word for this long; a fresh combat=0 still releases at once.
constexpr double kFigureLingerS = 3.0;
inline bool figure_combat_wants(bool fresh, bool freshBit, double sinceLastBitS, bool lingerOn) {
    if (fresh) return freshBit;
    return lingerOn && sinceLastBitS >= 0.0 && sinceLastBitS < kFigureLingerS;
}

// A body that just played a move is watched this long; if its full-body scope holds no action for kIdleEmptyS after the move's anim
// action is gone, it is standing in nothing (the T-pose: no combat guard idle, no idle-state action) and gets its idle back.
constexpr double kMoveWatchS = 4.0;
constexpr double kIdleEmptyS = 0.25;
inline bool idle_restore_due(bool scopeEmpty, double emptyForS, bool restoredThisMove, bool idleOn) {
    return idleOn && scopeEmpty && emptyForS >= kIdleEmptyS && !restoredThisMove;
}

// ---- B3: the perfect-blocked phase by weapon class --------------------------------------------------------------------------------
// The weapon classes a row's right-hand tag names (ActionRowCatalog's own classification, mirrored on the agent: Wo187Rules.WeaponOf).
enum class Weapon : uint8_t { Unknown = 0, Sword = 1, ShortSword = 2, Longsword = 3, Polearm = 4, Blunt = 5, Unarmed = 6, Other = 7 };
inline const char* weapon_name(Weapon w) {
    switch (w) {
        case Weapon::Sword: return "sword"; case Weapon::ShortSword: return "short-sword"; case Weapon::Longsword: return "longsword";
        case Weapon::Polearm: return "polearm"; case Weapon::Blunt: return "blunt"; case Weapon::Unarmed: return "unarmed";
        case Weapon::Other: return "other"; default: return "unknown";
    }
}
inline Weapon weapon_from_byte(uint8_t b) { return b <= static_cast<uint8_t>(Weapon::Other) ? static_cast<Weapon>(b) : Weapon::Unknown; }

// 0x10 -> 0x20 after contact, seconds. Measured (WO-185's vanilla trace, one NPC with a one-handed sword, 46 perfect blocks) for the sword;
// every other class keeps the fallback until a vanilla trace of that weapon measures it (the attack row's withdraw - hit does not predict it).
// `measured` says which.
constexpr double kStage2Fallback = 0.42;
inline double pb_stage2(Weapon w, int type, int zone, bool* measured) {
    if (w == Weapon::Sword || w == Weapon::Unknown) {   // Unknown = a 0.60.3 agent: the old table, as before
        if (type == 0 && zone == 5) { *measured = true; return 0.41; }
        if (type == 1 && zone == 0) { *measured = true; return 0.50; }
        if (type == 1 && zone == 1) { *measured = true; return 0.29; }
        if (type == 1 && zone == 2) { *measured = true; return 0.41; }
    }
    *measured = false;
    return kStage2Fallback;
}

// ---- B2: a chasing copy -----------------------------------------------------------------------------------------------------
// A host NPC moving at least this fast (its stream's newest samples) is running: its copy is not held for a swing (mp_chase_follow).
constexpr float kChaseFollowMps = 2.0f;   // a walk is ~1.4, a combat strafe ~1; a run 3-5

// ---- B1: fight positions --------------------------------------------------------------------------------------------------------
constexpr int kMaxPosEids = 16;   // one read's bodies (op 40)

} // namespace kcdmp::wo187rules
