// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-206: the pure rules of the test bench's two native halves (wo206.cpp). Engine-free: native/tests/wo206_tests.cpp pins them.
//
//   C  the digest sweep: the agent registers every quest node path of the keyspace (States, Timers, TimeOfDayWatches); the sweep
//      reads kPerFrame of them a frame, round-robin, and starts a new pass at most every kMinPassGapS; an entry whose value changed
//      (or was never reported) is dirty until the agent reads it.
//   D  the animation probe: the bodies, the scope-read budget, the line's text.

#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstring>

namespace kcdmp::wo206rules {

// ---- C: the digest sweep
constexpr size_t kPerFrame = 120;          // node reads a frame (~10,250 nodes: one pass in ~86 frames)
constexpr double kMinPassGapS = 2.0;       // a new pass starts at most this often (the checkpoints are 5 s apart)
constexpr size_t kMaxPaths = 16384;
constexpr size_t kMaxPathLen = 400;        // the WO-137 read's own limit

struct Sweep {
    size_t cursor = 0;        // the next entry
    uint32_t passes = 0;      // completed passes
    double passStart = -1e9;  // when the current pass began
    bool inPass = false;
};

// How many entries to read this frame (0 = waiting for the next pass); advances the cursor and counts finished passes.
inline size_t sweep_take(Sweep& s, size_t total, size_t perFrame, double now, double minGap, size_t* first) {
    *first = 0;
    if (total == 0) return 0;
    if (!s.inPass) {
        if (now - s.passStart < minGap) return 0;
        s.inPass = true; s.passStart = now; s.cursor = 0;
    }
    *first = s.cursor;
    size_t n = total - s.cursor < perFrame ? total - s.cursor : perFrame;
    s.cursor += n;
    if (s.cursor >= total) { s.inPass = false; ++s.passes; }
    return n;
}

struct Entry { int32_t value = -1; uint8_t kind = 0; bool found = false, read = false, dirty = false; };

// A read lands: dirty when the value, the kind or found changed, or on the first read.
inline void entry_apply(Entry& e, bool found, uint8_t kind, int32_t value) {
    if (!e.read || e.found != found || e.kind != kind || e.value != value) e.dirty = true;
    e.read = true; e.found = found; e.kind = kind; e.value = value;
}

// ---- D: the probe
constexpr int kMaxBodies = 40;
constexpr float kDefaultRadiusM = 40.0f;
constexpr double kDefaultHz = 10.0;
constexpr int kDefaultScopeReads = 12;     // scope reads per sample (the rest carry their last read, at most kScopeMaxAgeS old)
constexpr double kWalkEveryS = 1.0;         // the NPC walk around the player (the probe's whole cost: 6-8 ms) once a second; between
                                            // walks the bodies it found are re-read (positions every sample) -- the bench: -6 % fps at 10 Hz
constexpr double kScopeMaxAgeS = 1.5;      // a body whose last scope read is older is left out of the sample (never written as empty)
constexpr int kMaxScopesOut = 12;
constexpr uint64_t kMaxFileBytes = 32ull << 20;   // kcdmp-anim.jsonl rotates to .1.jsonl past this (two files at most)

inline const char* machine_name(uint8_t m) { return m == 1 ? "host" : m == 2 ? "joiner" : "solo"; }

// A JSON string body: quotes, backslashes and control characters escaped (names are engine names, but never trust them).
inline size_t json_escape(const char* s, char* out, size_t cap) {
    size_t o = 0;
    if (!cap) return 0;
    for (; s && *s && o + 7 < cap; ++s) {
        const unsigned char c = static_cast<unsigned char>(*s);
        if (c == '"' || c == '\\') { out[o++] = '\\'; out[o++] = static_cast<char>(c); }
        else if (c < 0x20) o += static_cast<size_t>(std::snprintf(out + o, cap - o, "\\u%04x", c));
        else out[o++] = static_cast<char>(c);
    }
    out[o] = 0;
    return o;
}

// The ragdoll bit from the full body's installed action (a ragdoll / knocked-down class), when no physics read says it.
inline bool ragdoll_class(const char* cls) {
    return cls && (std::strstr(cls, "Ragdoll") || std::strstr(cls, "RagDoll") || std::strstr(cls, "KnockDown") || std::strstr(cls, "Knockdown"));
}

} // namespace kcdmp::wo206rules
