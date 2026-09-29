#pragma once
// WO-143: the engine-free rules of the activities-part-2 DLL half (wo143.cpp,
// and WO-141's apply, which carries the hands). native/tests/wo143_rules_tests.cpp
// pins them.

#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>

namespace kcdmp::wo143rules {

// ---- hands (M3) --------------------------------------------------------------
// An item class id as the game keeps it (C_Item slot 0x18; a Windows GUID in
// memory). All zero = the hand is empty.
struct ClassId {
    uint8_t b[16]{};
    bool empty() const { for (uint8_t v : b) if (v) return false; return true; }
    bool operator==(const ClassId& o) const { return std::memcmp(b, o.b, 16) == 0; }
    bool operator!=(const ClassId& o) const { return !(*this == o); }
};
// Slot [1] of the NPC state is the left hand, [2] the right (read live: the
// field hoe in [1] = Lua GetItemInHand(1); the woodworker's saw in [2] = hand 0).
struct Hands {
    ClassId left, right;
    bool empty() const { return left.empty() && right.empty(); }
    bool operator==(const Hands& o) const { return left == o.left && right == o.right; }
    bool operator!=(const Hands& o) const { return !(*this == o); }
};
constexpr uint8_t kHandLeft = 1, kHandRight = 2;   // C_HandContentElement +0x48 (its GameLoad)
constexpr unsigned kSlotLeft = 1, kSlotRight = 2;

// ---- gaits (M5) --------------------------------------------------------------
// The 9 actorCondition_* entity contexts (Tables.pak ai/ScriptContext.xml,
// census s1.1): bit i = kGaits[i].
constexpr int kGaitCount = 9;
inline const char* gait_name(int i) {
    static const char* const k[kGaitCount] = {
        "actorCondition_forcedHoeing", "actorCondition_forcedDrunkenness", "actorCondition_forcedInjured",
        "actorCondition_forcedArmored", "actorCondition_forcedFatness", "actorCondition_fatness",
        "actorCondition_forcedCrimeScanning", "actorCondition_forcedCrimeWatching_violent",
        "actorCondition_forcedCrimeWatching_nonViolent",
    };
    return (i >= 0 && i < kGaitCount) ? k[i] : "?";
}
constexpr uint16_t kGaitMaskAll = (1u << kGaitCount) - 1;

// What the copy must change: bits to set (host has, copy lacks) and bits to
// clear -- only those this DLL set itself (the store is refcounted; a context
// the copy's own world set is never removed).
struct GaitPlan { uint16_t set = 0, clear = 0; };
inline GaitPlan gait_plan(uint16_t host, uint16_t copyHas, uint16_t weSet) {
    GaitPlan p;
    p.set = static_cast<uint16_t>(host & ~copyHas & kGaitMaskAll);
    p.clear = static_cast<uint16_t>(weSet & ~host & copyHas & kGaitMaskAll);
    return p;
}

// ---- quieter logs (Phase 7) --------------------------------------------------
// A refused apply is logged on its first kLogFirst tries, then at most once per
// kLogEveryS with the count of the tries in between; a success starts over.
constexpr int    kLogFirst = 3;
constexpr double kLogEveryS = 60.0;
struct LogPace { int tries = 0; int since = 0; double lastLog = -1e9; };
inline bool log_due(LogPace& p, double now, int* suppressed) {
    ++p.tries;
    if (p.tries <= kLogFirst) { p.lastLog = now; *suppressed = 0; return true; }
    if (now - p.lastLog >= kLogEveryS) { *suppressed = p.since; p.since = 0; p.lastLog = now; return true; }
    ++p.since;
    return false;
}
inline void log_reset(LogPace& p) { p = LogPace{}; }

// ---- one-shots (M4) ----------------------------------------------------------
// The host sends a tracked NPC's one-shot at most once per kOneShotGapS (a dice
// player's reactions, a guest's sips) and at most kOneShotPerSecond overall.
constexpr double kOneShotGapS = 1.5;
constexpr int    kOneShotPerSecond = 8;
struct OneShotBudget { double windowAt = -1e9; int inWindow = 0; };
inline bool oneshot_allowed(OneShotBudget& b, double lastForNpc, double now) {
    if (now - lastForNpc < kOneShotGapS) return false;
    if (now - b.windowAt >= 1.0) { b.windowAt = now; b.inWindow = 0; }
    if (b.inWindow >= kOneShotPerSecond) return false;
    ++b.inWindow;
    return true;
}
// A paused copy's NPC-state machine is ticked by the DLL while its one-shot
// runs, for at most this long (a request the game never finishes stops here).
constexpr double kOneShotTickLimitS = 15.0;
// A body the game ticks itself (an avatar's minigame loop) keeps its request's
// bookkeeping this long at most (the loop itself runs until the next request).
constexpr double kOneShotKeepS = 600.0;

// ---- looks (idles) -----------------------------------------------------------
// C_NPCLookTarget's resolved target: 0 none, 1 an entity (a WUID), 2 a point.
constexpr uint8_t kLookNone = 0, kLookEntity = 1, kLookPoint = 2;
// Who a copy looks at, on the wire: nobody, the host's own player, a player's
// avatar (by ghost id), or an NPC (by name).
constexpr uint8_t kTargetNone = 0, kTargetHostPlayer = 1, kTargetPeer = 2, kTargetNpc = 3;
// Cheap: only NPCs within kLookRangeM of a player are read, every kLookPeriodS,
// a change goes out at once, at most kLookPerSecond changes across the scene.
constexpr float  kLookRangeM = 20.0f;
constexpr double kLookPeriodS = 0.5;
constexpr int    kLookPerSecond = 6;
struct LookBudget { double windowAt = -1e9; int inWindow = 0; };
inline bool look_allowed(LookBudget& b, double now) {
    if (now - b.windowAt >= 1.0) { b.windowAt = now; b.inWindow = 0; }
    if (b.inWindow >= kLookPerSecond) return false;
    ++b.inWindow;
    return true;
}

// ---- the live runs' rules (J1-J5) -------------------------------------------
// A paused copy's NPC state does not run: after WO-141's apply takes a tool
// into a hand, the take is settled under the three NPC-state steps for
// kSettleS; a copy the host shows hoeing is ticked while it hoes and
// kHoeSettleS after (J1/J2: the hoe flipped between two grips every frame once
// she stood; six seconds of ticks settled it).
constexpr double kSettleS = 2.0;
constexpr double kHoeSettleS = 3.0;
constexpr uint16_t kGaitHoeingBit = 1;   // actorCondition_forcedHoeing
inline bool hoeing(uint16_t hostGaits) { return (hostGaits & kGaitHoeingBit) != 0; }

// A copy the host shows hoeing creeps along its row (H1: 0.08-0.10 m/s, under
// the gait writer's walking floor): while it creeps it walks, and the tags see
// the pace the game hoes at (J1: 0.4 m/s showed the hoeing walk). Standing
// still, it stands. Returns whether the walking class is kept; out = the
// velocity the tags see.
constexpr float kHoeTagMps = 0.4f;
constexpr float kHoeCreepMps = 0.005f;
inline bool hoe_tags(float vx, float vy, float* ox, float* oy) {
    *ox = vx; *oy = vy;
    const float v = std::sqrt(vx * vx + vy * vy);
    if (!(v > kHoeCreepMps)) return false;
    if (v < kHoeTagMps) { *ox = vx / v * kHoeTagMps; *oy = vy / v * kHoeTagMps; }
    return true;
}

// ---- the host's refresh (as WO-141's) ----------------------------------------
constexpr double kRefreshS = 10.0;
inline bool send_due(bool changed, bool isEmpty, double sinceSentS) {
    return changed || (!isEmpty && sinceSentS >= kRefreshS);
}

} // namespace kcdmp::wo143rules
