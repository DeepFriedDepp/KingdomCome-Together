// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-213 (docs/WO-213-findings.md): the pure rules of the internet stream's native half (engine-free: native/tests check them).
//   D.2  the host's NPC sender sends each NPC at its band's rate (fighting / within 15 m in combat 30 Hz, 40 m 20 Hz, 150 m 10 Hz,
//        beyond 2 Hz); an NPC that did not change is still skipped (its 2 s heartbeat aside)
//   E.1  the joiner plays each stream a delay behind the host's clock: the stream's inter-arrival p90 plus a margin, 50..300 ms,
//        recomputed every second and changed smoothly; past the newest sample at most 150 ms of extrapolation, then hold
//   E.2  the owner holds a copy's last posture while its host NPC's state is late (no state past the host's 2 s resend), up to 2 s more;
//        late is counted apart from missing and never reseats; after those 2 s the body is asked for again
//   E.3  a seated, lying or working copy takes no position write while its posture holds and its body stays in place (once off its
//        host NPC's place, the stream every frame for the rest of that sitting)
//   D.4  the figures in the top band: the pipe answers one main-thread request a frame, and the agent's position loop waited for one
//        each round (the field's figures went out at 9-17 Hz); the local player's state is read once a frame on the main thread
//        while the agent asks for it, and the pipe's reader answers from that read at once
#include <algorithm>
#include <cstdint>
#include <cstring>

namespace kcdmp::wo213rules {

// ---- the agent's switch word (op 146), the same bits as the agent's W213 native switches; every bit on until the agent says otherwise
constexpr uint32_t kBitNpcBands = 0x01;        // D.2 npc_bands
constexpr uint32_t kBitPlayoutAdaptive = 0x02; // E.1 playout_adaptive
constexpr uint32_t kBitPostureHoldLate = 0x04; // E.2 posture_hold_late
constexpr uint32_t kBitPostureNoPos = 0x08;    // E.3 posture_no_pos
constexpr uint32_t kBitFigurePlayout = 0x10;   // E.5 figure_playout (the figures on the same play-out as NPCs)
constexpr uint32_t kBitFigureBand = 0x20;      // D.4 figure_band (the local player's state read each frame, served to the pipe at once)
constexpr uint32_t kBitsDefault = kBitNpcBands | kBitPlayoutAdaptive | kBitPostureHoldLate | kBitPostureNoPos | kBitFigurePlayout | kBitFigureBand;

// The bench's twin names (KCDMP_TEST_W213_OFF=name,name -- the same variable the agent and the relay read).
inline uint32_t twin_off_bits(const char* list) {
    struct N { const char* n; uint32_t b; };
    static const N k[] = {{"npc_bands", kBitNpcBands}, {"playout_adaptive", kBitPlayoutAdaptive}, {"posture_hold_late", kBitPostureHoldLate},
                          {"posture_no_pos", kBitPostureNoPos}, {"figure_playout", kBitFigurePlayout}, {"figure_band", kBitFigureBand}};
    uint32_t b = 0;
    if (!list) return 0;
    const char* p = list;
    while (*p) {
        while (*p == ',' || *p == ' ') ++p;
        const char* s = p;
        while (*p && *p != ',' && *p != ' ') ++p;
        const size_t n = static_cast<size_t>(p - s);
        if (n == 3 && std::strncmp(s, "all", 3) == 0) b |= kBitsDefault;
        for (const N& e : k)
            if (n == std::strlen(e.n) && std::strncmp(s, e.n, n) == 0) b |= e.b;
    }
    return b;
}

// ---- D.4 the local player's state, read each frame while asked (the reader answers from it while it is fresh) ----
constexpr double kLocalAskedS = 1.0;      // read each frame while the agent asked within this
constexpr double kLocalFreshS = 0.25;     // the reader answers from a read this recent; older (a menu, a load): the main-thread path
inline bool local_wanted(double nowS, double askedS) { return askedS > 0 && nowS - askedS <= kLocalAskedS; }
inline bool local_fresh(double nowS, double readS) { return readS > 0 && nowS - readS >= 0 && nowS - readS <= kLocalFreshS; }

// ---- D.2 the bands (config values: the agent sends them with the word; these are the defaults) ----
struct Bands {
    float fightM = 15.0f, nearM = 40.0f, midM = 150.0f;
    uint16_t fightMs = 33, nearMs = 50, midMs = 100, farMs = 500;
};
// The band of an NPC dMin metres from the nearer player: 0 fighting (drawn or engaged, within fightM), 1 nearM, 2 midM, 3 beyond.
inline int band(float dMin, bool fighting, const Bands& b) {
    if (fighting && dMin <= b.fightM) return 0;
    if (dMin <= b.nearM) return 1;
    if (dMin <= b.midM) return 2;
    return 3;
}
inline double band_period_s(int bandIdx, const Bands& b) {
    switch (bandIdx) { case 0: return b.fightMs / 1000.0; case 1: return b.nearMs / 1000.0; case 2: return b.midMs / 1000.0; default: return b.farMs / 1000.0; }
}
// The sender's tick: the fastest band's period (an NPC is due when its band's period passed since its last send).
inline double tick_s(const Bands& b) { return (std::min)((std::min)(b.fightMs, b.nearMs), (std::min)(b.midMs, b.farMs)) / 1000.0; }
inline bool band_due(double sinceSentS, int bandIdx, const Bands& b) { return sinceSentS + 0.002 >= band_period_s(bandIdx, b); }
// The band's own cadence, not the frames': the next slot is one period after the last SLOT while a send is less than a period late
// (frames of 22-30 ms put a 33 ms period on every second frame -- the bench's fights went out at 18-21 Hz, not 30); later than that,
// the cadence starts again at now. The sender's tick and each NPC's band keep their slot this way.
inline double band_slot(double prevSlotS, double nowS, double periodS) {
    return (nowS - (prevSlotS + periodS) < periodS) ? prevSlotS + periodS : nowS;
}

// ---- E.1 the play-out delay ----
constexpr double kPlayoutMinS = 0.050, kPlayoutMaxS = 0.300, kPlayoutMarginS = 0.025;
constexpr double kPlayoutEveryS = 1.0;      // recomputed every second
constexpr double kPlayoutSlew = 0.10;       // the applied delay moves at most 10 % of real time (a pace change, never a step)
constexpr double kExtrapCapS = 0.150;       // past the newest sample, at most this, then hold
constexpr double kGapPaceCapS = 1.0;        // a gap longer than this is a moved-gated silence (a heartbeat), not the stream's pace
constexpr int kGapRing = 64;

// The p90 of the gaps in a ring (n <= kGapRing used), or -1 when there are fewer than 4.
inline double gap_p90(const double* gaps, int n) {
    if (n < 4) return -1.0;
    double v[kGapRing];
    const int m = n > kGapRing ? kGapRing : n;
    for (int i = 0; i < m; ++i) v[i] = gaps[i];
    std::sort(v, v + m);
    const int idx = static_cast<int>(0.9 * (m - 1) + 0.5);
    return v[idx];
}
// The target delay for a stream whose gaps' p90 is p90 (-1 = not known yet: the bind's own delay, clamped).
inline double playout_target(double p90, double bindDelayS) {
    const double t = p90 > 0 ? p90 + kPlayoutMarginS : bindDelayS;
    return t < kPlayoutMinS ? kPlayoutMinS : t > kPlayoutMaxS ? kPlayoutMaxS : t;
}
// One frame's step of the applied delay toward the target.
inline double playout_step(double applied, double target, double dt) {
    const double maxStep = kPlayoutSlew * dt;
    if (target > applied + maxStep) return applied + maxStep;
    if (target < applied - maxStep) return applied - maxStep;
    return target;
}
// How far past the newest sample a moving stream may be extrapolated: E.1's cap (on) or the old bound (off).
inline double extrap_max_s(bool on, double oldMaxS) { return on ? (std::min)(kExtrapCapS, oldMaxS) : oldMaxS; }

// ---- E.2 a late host state against a missing posture ----
constexpr double kStateLateS = 2.5;         // the host resends every body each 2 s (wo208_rules.h kResendS): past this the state is late
constexpr double kLateHoldS = 2.0;          // held this much longer, then the body is asked for again
enum class Late : uint8_t { Fresh = 0, Late = 1, AskAgain = 2 };
inline Late lateness(double stateAgeS) {
    if (stateAgeS <= kStateLateS) return Late::Fresh;
    return stateAgeS <= kStateLateS + kLateHoldS ? Late::Late : Late::AskAgain;
}

// ---- E.3 a posture holds: the copy and its host NPC both in a posture family (sitting, lying, kneeling, working) ----
inline bool posture_holds(bool copyPosture, bool hostPosture, bool on) { return on && copyPosture && hostPosture; }

// ... and only while the body stays in place. A body that left its host NPC's newest position is not sitting still: the bench's talk had
// a "sitting" copy glide 24 m in 22 s with no write to bring it back, and the owner's install of a fragment that never starts (re-tried
// every 320 ms) yanked one village copy 1-5 m each time -- the writes every frame had covered both. Such a body takes the stream every
// frame for the rest of that sitting, as before E.3. A hold that came back after a while of writes (2 s windows: d13 with 0.3 m, d14 with
// 2 m) was a sawtooth or a flicker -- the stream put the body back, the next yank or the copy's own seat (up to 1.2 m off) took it again.
// A seat wrongly taken for a drift costs only the old writes. The sitting ends when the posture has not held for kPostureEndS.
constexpr float  kPostureInPlaceM = 0.30f;
constexpr double kPostureEndS = 2.0;
inline bool posture_in_place(float dx, float dy) { return dx * dx + dy * dy <= kPostureInPlaceM * kPostureInPlaceM; }
// One copy's sitting, as the writer keeps it.
struct PostureSitting { bool drifted = false; double offSince = 0; };
// The writer's call each frame. holds: the posture holds (both in a posture, the switch on). true = leave the body alone. No position
// read (havePos false) keeps the hold.
inline bool posture_hold_sitting(bool holds, bool havePos, float dx, float dy, double now, PostureSitting* st) {
    if (!holds) {
        if (st->offSince == 0) st->offSince = now;
        else if (now - st->offSince >= kPostureEndS) st->drifted = false;   // that sitting is over: the next one is held again
        return false;
    }
    st->offSince = 0;
    if (st->drifted) return false;
    if (havePos && !posture_in_place(dx, dy)) { st->drifted = true; return false; }
    return true;
}

} // namespace kcdmp::wo213rules
