#pragma once
// WO-140: the engine-free rules of the sleep DLL half (wo140.cpp).
// native/tests/wo140_rules_tests.cpp pins them.

#include <cstddef>
#include <cstdint>
#include <cstring>

namespace kcdmp::wo140rules {

// C_SkipTime ids (Tables Libs/Tables/rpg/skiptime.xml): 1 Wait, 2 Sleep, 4 Read.
// These are the player's own skips (a bed, the wait key, reading in bed); every
// other id is a quest's, jail's or the bath's and is never held.
inline bool gated_id(int id) { return id == 1 || id == 2 || id == 4; }

// What op 5 (the accepter's own skip) may start: the player's own skips, and
// 14 "Fake Sleep, Hidden Stats" (the game's forced sleep screen, no bed needed).
inline bool start_id_ok(int id) { return gated_id(id) || id == 14; }

inline const char* id_name(int id) {
    switch (id) {
        case 0: return "undefined";
        case 1: return "wait";
        case 2: return "sleep";
        case 4: return "read";
        case 5: return "jail";
        case 6: return "unconscious";
        case 14: return "fake-sleep";
    }
    return "other";
}

// C_SkipTime's state at +0x68 (the ShowDialog / Stop switch): 0 idle, 1 the
// picker is open, 2 and 3 the skip runs. Observed live (WO-140 p2): a confirmed
// sleep goes 0 -> 1 -> 2 -> 3 -> 1 -> 0 (the picker comes back for its fade-out
// before it closes), so "ended" is a return to 0 after a skip ran, whatever the
// state just before.
inline bool is_open(uint32_t st) { return st == 1; }
inline bool is_skipping(uint32_t st) { return st == 2 || st == 3; }

enum class Edge : uint8_t { None, Opened, Began, BackedOut, Ended };

// One state reading at a time (the frame tick): the edges the vote needs.
struct Tracker {
    uint32_t prev = 0;
    bool ran = false;   // a skip ran since the state was last idle
    Edge step(uint32_t now) {
        if (now == prev) return Edge::None;
        Edge e = Edge::None;
        if (is_skipping(now) && !ran) { e = Edge::Began; ran = true; }
        else if (is_open(now) && prev == 0) e = Edge::Opened;
        else if (now == 0) { e = ran ? Edge::Ended : Edge::BackedOut; ran = false; }
        prev = now;
        return e;
    }
};

inline const char* edge_name(Edge e) {
    switch (e) {
        case Edge::Opened: return "opened";
        case Edge::Began: return "began";
        case Edge::BackedOut: return "backed-out";
        case Edge::Ended: return "ended";
        default: return "none";
    }
}

// The inline-hook thunk's frame (inline_hook.cpp emit_thunk): four pushes and
// sub rsp,0x68 before `call cb`. From the callback's own return-address slot R:
// the thunk's rsp T = R + 8; the saved xmm2 / xmm3 at T+0x40 / T+0x50; the
// hooked function's entry rsp E = T + 0x88, so its 5th and 6th arguments sit at
// E+0x28 / E+0x30.
constexpr size_t kThunkRetToT   = 8;
constexpr size_t kTXmm2         = 0x40;
constexpr size_t kTXmm3         = 0x50;
constexpr size_t kTEntry        = 0x88;
constexpr size_t kEntryArg5     = 0x28;
constexpr size_t kEntryArg6     = 0x30;

// The picker's hours (C_SkipTime +0x74 after OnAcceptDialog): what the wire takes.
inline bool hours_ok(float h) { return h == h && h >= 0.25f && h <= 24.f; }

inline uint32_t float_bits(float f) { uint32_t u; std::memcpy(&u, &f, 4); return u; }

// The clock pull (the joiner's own copy only): refused unless the calendar reads
// what the agent believes (within the slack), and only ever backward.
enum class Pull : uint8_t { Pulled = 1, NotNeeded = 0, Mismatch = 2, SkipRunning = 3 };
// WO-144 3.3: a calendar further AHEAD than the agent read (its own skip ran on meanwhile -- the
// field's "refused: the calendar reads otherwise", twice) is still ahead of the host's: pulled. Only
// one that went BACK past the reading (a load, a rewind) is refused: the target may be stale then.
// A skip running on this machine is the handler's to wait out (SkipRunning), never a pull mid-skip.
inline Pull pull_verdict(int64_t curMs, uint32_t expectSec, uint32_t targetSec, uint32_t slackSec = 120) {
    const int64_t cur = curMs / 1000;
    const int64_t d = cur - static_cast<int64_t>(expectSec);
    if (d < -static_cast<int64_t>(slackSec)) return Pull::Mismatch;
    if (static_cast<int64_t>(targetSec) * 1000 >= curMs) return Pull::NotNeeded;
    return Pull::Pulled;
}

} // namespace kcdmp::wo140rules
