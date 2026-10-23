// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-204: the native half's pure rules, engine-free so native/tests pins them.
//   Act I  nearest_player_v2: a distance trigger on the player counts the nearer of the two players without writing into the node.
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>

namespace kcdmp::wo204rules {

// ---- the agent's switch word (op 115), the same bits as Wo204Rules.ConfigBits ----
constexpr uint32_t kBitSeatHold = 1, kBitTposeWatch = 2;
constexpr uint32_t kBitsDefault = kBitSeatHold | kBitTposeWatch;

// ---- II.2 the T-pose detector ----
// A copy (or a figure) whose full-body scope stands empty longer than this is in its bind pose: told (WO204-TPOSE) and its base reseated.
constexpr double kTposeEmptyS = 0.3;
inline bool tpose_due(double emptyForS) { return emptyForS > kTposeEmptyS; }

// ---- WO-201 B7's exception: a tutorial pop-up is the player's own ----
// The joiner's game shows him its own tutorials (his own state: encumbered, a broken armour piece, a fight with several opponents);
// B7 (the joiner's quest logic starts no story view) refused them as a story's view. A view node under the game's tutorial module.
inline bool is_tutorial_path(bool isView, const char* path) {
    if (!isView || !path) return false;
    for (const char* p = path; *p; ++p) if (std::strncmp(p, ".tutorialy.", 11) == 0) return true;
    return false;
}

// ---- II.3 seated copies: one authority ----
// The position stream never moves a copy that is itself seated, lying, kneeling or aligned at an object (its own current state), whatever
// the host's row wants at that moment: the field's copies slid while seated and sank into tables when a placement was refused or backed
// off (WO-141 held them only for the host's wanted activity). The body leaves the seat the game's own way when the host's NPC stands.
inline bool seat_hold(bool hostWantsHold, bool copyHeldByObject, bool on) { return hostWantsHold || (on && copyHeldByObject); }
constexpr float kSeatMoveCm = 5.0f;   // C2's measure: no seated copy moves more than 5 cm while seated

// ---- Act I: C_DistanceTrigger::Evaluate, read out of its own code ----
// Evaluate (XGenAIModule 1.5.5) is: the origin's and the target's position through their own virtual, the distance, a six-way
// compare of the distance with the node's threshold by the node's compare kind, then SetResult(node, bool) -- the function that
// stores the match state and fires OnMatch / OnUnmatch on a change. The override computes the same bool from the nearer player's
// position and hands it to the engine's own SetResult; it never writes into the node. The whole body must match the shape below
// (displacements and call targets masked, '?' in kEvalMask): a build whose Evaluate differs is not resolved and the triggers stay the
// host's Henry's.
inline constexpr uint8_t kEvalBody[] = {
    0x48, 0x89, 0x5C, 0x24, 0x08, 0x57, 0x48, 0x83, 0xEC, 0x40, 0x48, 0x8B, 0xF9, 0x0F, 0x29, 0x74,
    0x24, 0x30, 0x48, 0x8D, 0x0D, 0x00, 0x00, 0x00, 0x00, 0xE8, 0x00, 0x00, 0x00, 0x00, 0x48, 0x8B,
    0x8F, 0x00, 0x00, 0x00, 0x00, 0x48, 0x8B, 0x01, 0xFF, 0x50, 0x50, 0x48, 0x8B, 0x8F, 0x00, 0x00,
    0x00, 0x00, 0xF2, 0x0F, 0x10, 0x30, 0x8B, 0x58, 0x08, 0x48, 0x8B, 0x01, 0xFF, 0x50, 0x50, 0x0F,
    0x28, 0xDE, 0x0F, 0x28, 0xD6, 0x0F, 0xC6, 0xDB, 0x55, 0xF2, 0x0F, 0x10, 0x08, 0x0F, 0x28, 0xC1,
    0xF3, 0x0F, 0x5C, 0xD1, 0x0F, 0xC6, 0xC0, 0x55, 0xF3, 0x0F, 0x5C, 0xD8, 0x66, 0x0F, 0x6E, 0xCB,
    0xF3, 0x0F, 0x5C, 0x48, 0x08, 0x0F, 0x57, 0xC0, 0xF3, 0x0F, 0x59, 0xD2, 0xF3, 0x0F, 0x59, 0xDB,
    0xF3, 0x0F, 0x59, 0xC9, 0xF3, 0x0F, 0x58, 0xDA, 0xF3, 0x0F, 0x58, 0xD9, 0x0F, 0x2E, 0xC3, 0x77,
    0x09, 0x0F, 0x57, 0xC9, 0xF3, 0x0F, 0x51, 0xCB, 0xEB, 0x0B, 0x0F, 0x28, 0xC3, 0xE8, 0x00, 0x00,
    0x00, 0x00, 0x0F, 0x28, 0xC8, 0x48, 0x63, 0x87, 0x00, 0x00, 0x00, 0x00, 0x83, 0xF8, 0x05, 0x77,
    0x6C, 0x48, 0x8D, 0x15, 0x00, 0x00, 0x00, 0x00, 0x8B, 0x8C, 0x82, 0x00, 0x00, 0x00, 0x00, 0x48,
    0x03, 0xCA, 0xFF, 0xE1, 0x0F, 0x2E, 0x8F, 0x00, 0x00, 0x00, 0x00, 0x7A, 0x11, 0x75, 0x0F, 0xB2,
    0x01, 0xEB, 0x42, 0x0F, 0x2E, 0x8F, 0x00, 0x00, 0x00, 0x00, 0x7A, 0xF3, 0x75, 0xF1, 0x33, 0xD2,
    0xEB, 0x33, 0x0F, 0x2F, 0x8F, 0x00, 0x00, 0x00, 0x00, 0x0F, 0x97, 0xC2, 0xEB, 0x27, 0x0F, 0x2F,
    0x8F, 0x00, 0x00, 0x00, 0x00, 0xEB, 0x1B, 0xF3, 0x0F, 0x10, 0x87, 0x00, 0x00, 0x00, 0x00, 0x0F,
    0x2F, 0xC1, 0x0F, 0x97, 0xC2, 0xEB, 0x0E, 0xF3, 0x0F, 0x10, 0x87, 0x00, 0x00, 0x00, 0x00, 0x0F,
    0x2F, 0xC1, 0x0F, 0x93, 0xC2, 0x48, 0x8B, 0xCF, 0xE8, 0x00, 0x00, 0x00, 0x00, 0x48, 0x8B, 0x5C,
    0x24, 0x50, 0x0F, 0x28, 0x74, 0x24, 0x30, 0x48, 0x83, 0xC4, 0x40, 0x5F, 0xC3,
};
inline constexpr char kEvalMask[] = "xxxxxxxxxxxxxxxxxxxxx????x????xxx????xxxxxxxxx????xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx????xxxxxx????xxxxxxxx????xxx????xxxxxxxx????xxxxxxxxxxx????xxxxxxxxxxx????xxxxxxxx????xxxxxx????xxxxxxxxxxxx????xxxxxxxxxx????xxxxxxxxxxxxxxxx";
static_assert(sizeof kEvalMask - 1 == sizeof kEvalBody, "the mask covers the body");

// Where the shape's own fields sit (offsets into the body).
constexpr size_t kAtOrigin = 0x21, kAtTarget = 0x2E, kAtKind = 0x98, kAtBaseLea = 0xA1, kAtTable = 0xAB, kAtSetResultCall = 0x108;
constexpr size_t kAtThreshold[6] = {0xB7, 0xC6, 0xD5, 0xE1, 0xEB, 0xFB};
// The six cases' code, as offsets from Evaluate's start: equal, not equal, greater, greater-or-equal, less, less-or-equal.
constexpr uint32_t kCaseAt[6] = {0xB4, 0xC3, 0xD2, 0xDE, 0xE7, 0xF7};

struct EvalShape {
    bool ok = false;
    int32_t origin = -1, target = -1, kind = -1, threshold = -1;
    int64_t setResultAt = -1;   // SetResult, as an offset from Evaluate's start
    int64_t baseRel = -1;       // the module base, as an offset from Evaluate's start (the jump table's lea)
    uint32_t tableRva = 0;      // the jump table, as an RVA from that base
};

inline int32_t rd_i32(const uint8_t* p) { int32_t v; std::memcpy(&v, p, 4); return v; }

// p = Evaluate's first n bytes (n >= sizeof kEvalBody).
inline EvalShape evaluate_shape(const uint8_t* p, size_t n) {
    EvalShape s;
    if (!p || n < sizeof kEvalBody) return s;
    for (size_t i = 0; i < sizeof kEvalBody; ++i)
        if (kEvalMask[i] == 'x' && p[i] != kEvalBody[i]) return s;
    s.origin = rd_i32(p + kAtOrigin);
    s.target = rd_i32(p + kAtTarget);
    s.kind = rd_i32(p + kAtKind);
    s.threshold = rd_i32(p + kAtThreshold[0]);
    for (size_t k = 1; k < 6; ++k) if (rd_i32(p + kAtThreshold[k]) != s.threshold) return s;
    if (s.origin <= 0 || s.origin >= 0x1000 || s.target <= 0 || s.target >= 0x1000 || s.origin == s.target) return s;
    if (s.kind <= 0 || s.kind >= 0x1000 || s.threshold <= 0 || s.threshold >= 0x1000) return s;
    s.baseRel = static_cast<int64_t>(kAtBaseLea + 7) + rd_i32(p + kAtBaseLea + 3);
    s.tableRva = static_cast<uint32_t>(rd_i32(p + kAtTable));
    s.setResultAt = static_cast<int64_t>(kAtSetResultCall + 5) + rd_i32(p + kAtSetResultCall + 1);
    s.ok = true;
    return s;
}

// The jump table's six entries (RVAs from the module base) must land on the six cases above.
inline bool table_matches(const int32_t entries[6], int64_t baseRel) {
    for (int k = 0; k < 6; ++k)
        if (baseRel + entries[k] != static_cast<int64_t>(kCaseAt[k])) return false;
    return true;
}

// The engine's own compare, case for case (ucomiss / comiss: an unordered compare is false except "not equal").
inline bool trigger_result(int32_t kind, float d, float thr, bool* out) {
    switch (kind) {
        case 0: *out = d == thr; return true;
        case 1: *out = !(d == thr); return true;
        case 2: *out = d > thr; return true;
        case 3: *out = d >= thr; return true;
        case 4: *out = thr > d; return true;
        case 5: *out = thr >= d; return true;
        default: return false;   // the engine sets nothing for a kind it does not know
    }
}

inline float distance3(const float a[3], const float b[3]) {
    const float dx = a[0] - b[0], dy = a[1] - b[1], dz = a[2] - b[2];
    return std::sqrt(dy * dy + dx * dx + dz * dz);
}

// ---- Act I.3: quiet windows -- the override answers with the host's own Henry ----
constexpr uint64_t kQuietAfterMs = 5000;   // after a time skip ends, a load, a new player soul
constexpr uint64_t kSnapshotFreshMs = 500; // the figure's main-thread position older than this is not used
// C_SkipTime's state: 0 idle, 1 the picker, 2/3 the skip. Anything but idle is quiet (until kQuietAfterMs after it is idle again).
inline bool skip_quiet(uint32_t skipState) { return skipState != 0; }
inline bool near_quiet(uint64_t now, uint64_t quietUntil, bool agentQuiet) { return agentQuiet || now < quietUntil; }
inline bool snapshot_fresh(uint64_t now, uint64_t at) { return at != 0 && now >= at && now - at <= kSnapshotFreshMs; }

} // namespace kcdmp::wo204rules
