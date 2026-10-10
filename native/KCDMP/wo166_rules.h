// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-166: the engine-free rules of the 0.48.2 native half (wo166.cpp). native/tests/wo166_tests.cpp pins them.
//   C3  a copy counts as striking: the window of a host swing on the joiner's screen in which the copy's combat model reads Striking
//       (research/WO-162 Q1.4: E_CombatActorStateId Striking = 8; the engine's block and riposte answer an attacker it sees striking)
//   C5  the lock-on's "not-set" line once per NPC per minute

#include <cstddef>
#include <cstdint>

namespace kcdmp::wo166rules {

// ---- C3 ---------------------------------------------------------------------------------------------------------------------
constexpr int32_t kStateIdle = 1, kStateGuard = 2, kStateStriking = 8, kStateHit = 0x40, kStatePrepParry = 0x80,
                  kStateParryInPlace = 0x100, kStateDodge = 0x200;
// A row with no timings of its own (the catalog knows it, its table has none): the copy strikes from this long after the row arrives.
constexpr double kStrikeDefaultStartMs = 250.0;
constexpr double kStrikeDefaultHitMs = 450.0;
// The window ends this long after the row's own hit time (the hit core reads the attacker at the contact, a frame or two late).
constexpr double kStrikeTailMs = 150.0;
// No swing starts later than this after its row (the longest start+hit seen is a sync attack's 3.2 s: a start is far below it).
constexpr int kStrikeMaxStartMs = 3000;

struct StrikeWindow { bool valid = false; double begin_ms = 0, end_ms = 0; };

// The window of a row played at `now_ms` with its own attack_time_to_start / attack_time_to_hit (ms; -1 = the row's table has none).
inline StrikeWindow strike_window(double now_ms, int startMs, int hitMs) {
    StrikeWindow w;
    if (startMs > kStrikeMaxStartMs || hitMs > kStrikeMaxStartMs) return w;
    const double s = startMs >= 0 ? startMs : kStrikeDefaultStartMs;
    const double h = hitMs >= 0 ? hitMs : kStrikeDefaultHitMs;
    w.valid = true;
    w.begin_ms = now_ms + s;
    w.end_ms = now_ms + s + h + kStrikeTailMs;
    return w;
}
inline bool in_window(const StrikeWindow& w, double t_ms) { return w.valid && t_ms >= w.begin_ms && t_ms <= w.end_ms; }

// A copy may be put into Striking from Idle or Guard (or a state of our own making). Never over the engine's own Hit,
// PreparingToParry, ParryInPlace or Dodge: those are its reactions, which our write would cut off.
inline bool strike_state_ok(int32_t cur) {
    return cur == kStateIdle || cur == kStateGuard || cur == kStateStriking;
}
// At the window's end the prior state goes back only if the model still reads our Striking (the engine moved on otherwise).
inline bool restore_ok(int32_t cur, int32_t ours) { return cur == ours; }

// The four attack fields within the engine's own ranges: AttackType 0..15 (the row tables' types), AttackZone 0..5 (the six zones),
// AttackStrength 0..2, AttackHandSlot 0..1 (WO-165's replay writes the same).
inline bool attack_fields_ok(int32_t type, int32_t zone, float strength, int32_t hand) {
    return type >= 0 && type <= 15 && zone >= 0 && zone <= 5 && strength >= 0.0f && strength <= 2.0f && (hand == 0 || hand == 1);
}

// ---- C5 ---------------------------------------------------------------------------------------------------------------------
// One line per key (an NPC's entity id) per minute; the rest counted.
class LineLimiter {
public:
    static constexpr double kPeriodS = 60.0;
    bool allow(uint32_t key, double now_s) {
        for (size_t i = 0; i < n_; ++i) {
            if (keys_[i] != key) continue;
            if (now_s - at_[i] < kPeriodS) { ++suppressed_; return false; }
            at_[i] = now_s;
            return true;
        }
        size_t slot = n_ < kMax ? n_++ : oldest();
        keys_[slot] = key; at_[slot] = now_s;
        return true;
    }
    uint32_t suppressed() const { return suppressed_; }
private:
    static constexpr size_t kMax = 64;
    size_t oldest() const {
        size_t o = 0;
        for (size_t i = 1; i < n_; ++i) if (at_[i] < at_[o]) o = i;
        return o;
    }
    uint32_t keys_[kMax]{};
    double at_[kMax]{};
    size_t n_ = 0;
    uint32_t suppressed_ = 0;
};

} // namespace kcdmp::wo166rules
