// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-218 A (docs/WO-218-findings.md): the pipe's batch, the pure half (unit-tested in native/tests/wo218_tests.cpp).
//
// The agent sends every waiting request of the generic op families (0x21..0x2B: [op][args] -> [ok][seq][op][reason][payload]) in one
// frame, kBatch 0x2D: [id:2][n:1] then n x ([type:1][len:2][body:len]). The DLL runs them in order on the main thread, at most
// kBudgetUs of a frame (at least one a frame; the rest the next frame), and answers once, kBatchReply 0xAF:
// [ok:1][id:2][n:1] then n x ([type:1][len:2][body:len]) -- each sub-reply exactly the frame the single request gets (its seq byte 0).
// The batch never takes a sequence number of the single requests (their replies keep their own order), and its reply is written from
// the main thread when its last request ran: the pipe thread meanwhile answers the agent's single requests (the position read).
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <vector>

namespace kcdmp::wo218rules {

constexpr uint8_t kBatch = 0x2D, kBatchReply = 0xAF;
constexpr uint8_t kFirstGeneric = 0x21, kLastGeneric = 0x2B;   // wo131 .. wo163 (0x2C is the pin label: not generic)
constexpr size_t  kMaxBatchLen = 4096;                         // the pipe's read buffer (kNpcSamplesMaxLen)
constexpr size_t  kMaxSubs = 255;
constexpr size_t  kReplyCap = 60000;                           // under a frame's 16-bit length
constexpr double  kBudgetUs = 2000.0;                          // the main thread's share a frame (the WO: 2 ms)
constexpr uint8_t kReasonTaskFaulted = 17, kReasonNotBatchable = 19;

inline bool batchable(uint8_t type) { return type >= kFirstGeneric && type <= kLastGeneric; }

struct Sub { uint8_t type = 0; uint16_t len = 0; size_t off = 0; };

/// [id:2][n:1] n x ([type:1][len:2][body]). False = malformed (nothing is run). The bodies stay in `b` (offsets).
inline bool parse_batch(const uint8_t* b, size_t len, uint16_t* id, std::vector<Sub>* out) {
    out->clear();
    if (!b || len < 3 || len > kMaxBatchLen) return false;
    *id = static_cast<uint16_t>(b[0] | (b[1] << 8));
    const size_t n = b[2];
    size_t o = 3;
    for (size_t i = 0; i < n; ++i) {
        if (o + 3 > len) return false;
        Sub s;
        s.type = b[o];
        s.len = static_cast<uint16_t>(b[o + 1] | (b[o + 2] << 8));
        s.off = o + 3;
        if (s.len == 0 || s.off + s.len > len) return false;
        out->push_back(s);
        o = s.off + s.len;
    }
    return o == len;
}

/// The reply under construction: [ok][id:2][n] then the sub-replies.
struct Reply {
    std::vector<uint8_t> b;
    size_t subs = 0, cut = 0;
    void begin(uint16_t id, uint8_t n) { b.assign({1, static_cast<uint8_t>(id & 0xFF), static_cast<uint8_t>(id >> 8), n}); subs = 0; cut = 0; }
    /// One sub-reply; past the cap the sub gets its failure with no payload (its first four bytes) -- counted in `cut`.
    void add(uint8_t type, const uint8_t* body, size_t len) {
        if (b.size() + 3 + len > kReplyCap) { len = len >= 4 ? 4 : len; ++cut; }
        if (b.size() + 3 + len > kReplyCap) len = 0;
        b.push_back(type);
        b.push_back(static_cast<uint8_t>(len & 0xFF));
        b.push_back(static_cast<uint8_t>(len >> 8));
        if (len) b.insert(b.end(), body, body + len);
        ++subs;
    }
};

/// The generic families' reply type for a request type (0 = not generic).
inline uint8_t reply_type(uint8_t type) {
    switch (type) {
        case 0x21: return 0x98; case 0x22: return 0x99; case 0x23: return 0x9D; case 0x24: return 0x9F; case 0x25: return 0xA2;
        case 0x26: return 0xA4; case 0x27: return 0xA6; case 0x28: return 0xA8; case 0x29: return 0xAA; case 0x2A: return 0xAB;
        case 0x2B: return 0xAC; default: return 0;
    }
}

/// A frame's budget: another request runs while none ran yet this frame, or while the frame's spent time is under the budget.
inline bool may_run(int ranThisFrame, double spentUs, double budgetUs = kBudgetUs) { return ranThisFrame == 0 || spentUs < budgetUs; }

/// The failure reply of a sub that never ran its handler: [0][0][op][reason].
inline size_t failure(uint8_t* rb, uint8_t op, uint8_t reason) { rb[0] = 0; rb[1] = 0; rb[2] = op; rb[3] = reason; return 4; }

/// Per-frame counting of what one frame's pipe turn served (main thread only).
struct FrameCount {
    unsigned long long frame = ~0ull;
    unsigned cur = 0, maxPerFrame = 0, frames = 0;
    unsigned long long total = 0;
    void add(unsigned long long f, unsigned n) {
        if (f != frame) { if (cur) { ++frames; if (cur > maxPerFrame) maxPerFrame = cur; } frame = f; cur = 0; }
        cur += n; total += n;
    }
    void reset() { cur = 0; maxPerFrame = 0; frames = 0; total = 0; frame = ~0ull; }
};

// ---- C.3 takeover_stop --------------------------------------------------------------------------------------------------------
// The host's posture families an activity holds (wo210rules::PFam: Lying 4, Sitting 5, Kneeling 6, Working 7): the copy's own later
// action is stopped only while the host's line holds one of these.
inline bool takeover_family(uint8_t pfam) { return pfam >= 4 && pfam <= 7; }
// The copy's own actions that took a placed activity over in WO-217's runs (its idle movement, a variation, a scripted callback, its
// movement transition); never a combat action, a hit reaction or anything of ours.
inline bool takeover_class(const char* cls) {
    if (!cls) return false;
    static const char* const k[] = {"CAnimActionAIMovement", "C_VariationAction", "C_CallbackAction", "CAnimActionMovementTransition"};
    for (const char* x : k) if (std::strcmp(cls, x) == 0) return true;
    return false;
}
constexpr double kTakeoverEveryS = 0.5;

// ---- C.1 drift_release: one writer -------------------------------------------------------------------------------------------
// The acceptance's flips (one woodcutter's copy every other frame 8.5 m apart for 15 minutes): WO-209's drift block lifted the first
// frame the stream itself had put the body back by its host NPC -- at once, since the stream writes a blocked body -- and the owner
// placed the host's activity again at the joiner's own object. Now a block lasts at least drift_block_s() (60 s, doubled for each
// drift of the same body within kDriftStrikeResetS, at most 8 minutes), and the writer catches two writers on its own: the engine
// moving a body it writes by more than kForeignMoveM, kForeignFrames times within kForeignWindowS.
constexpr double kDriftStrikeResetS = 600.0;
inline double drift_block_s(int strikes) {
    const int s = strikes < 0 ? 0 : (strikes > 3 ? 3 : strikes);
    return 60.0 * static_cast<double>(1 << s);
}
constexpr float kForeignMoveM = 4.0f;
constexpr int kForeignFrames = 10;
constexpr double kForeignWindowS = 2.0;
// Counts the frames something else moved a written body far; true on the frame the count reaches kForeignFrames within the window.
struct ForeignMeter {
    double since = -1;
    int n = 0;
    bool add(double now) {
        if (since < 0 || now - since > kForeignWindowS) { since = now; n = 0; }
        return ++n == kForeignFrames;
    }
    void reset() { since = -1; n = 0; }
};

}  // namespace kcdmp::wo218rules
