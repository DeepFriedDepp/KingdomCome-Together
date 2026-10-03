// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-141: the engine-free rules of the activity DLL half (wo141.cpp).
// native/tests/wo141_rules_tests.cpp pins them.
//
// An activity is what the game's NPC-state context says a body is in (the
// CURRENT state's fixed slots): a stance on a smart object (a bed, a seat, a
// kneeler, a cart slot), an unstance at a location object (a lean, a guard post,
// a workbench, a posed corpse), a minigame on an object (the player's). Objects
// travel as their level entity GUID -- the same on every machine that loaded the
// level, where a WUID is this machine's own index.

#include <cstddef>
#include <cstdint>
#include <cstring>

namespace kcdmp::wo141rules {

// XGenAI's stance enum (read live, WO-141 r4: C_StanceElement +0x28).
enum : uint8_t { kUndefined = 0, kStanding = 1, kLying = 2, kSitting = 3, kKneel = 4, kHorse = 5, kCrouch = 6, kCart = 7 };
constexpr uint16_t kNoUnstance = 0xFFFF;
constexpr uint8_t  kNoMinigame = 0xFF;
constexpr uint8_t  kFlagOwnsPos = 0x01;   // the activity holds the body on its object (the writer yields)

struct Activity {
    uint8_t  stance = 0;             // 0 = not in a synced stance
    uint8_t  cart = 0;               // the cart slot (0 = any)
    uint64_t stanceObj = 0;          // the stance's smart object (entity GUID; 0 none)
    uint16_t unstance = kNoUnstance; // the unstance database index
    uint64_t unstanceObj = 0;        // the unstance's location object (entity GUID; 0 none)
    uint8_t  minigame = kNoMinigame; // the minigame type (the player's)
    uint64_t minigameObj = 0;
    uint8_t  flags = 0;
};
constexpr size_t kWireBytes = 30;

inline const char* stance_name(uint8_t s) {
    switch (s) {
        case kUndefined: return "none";
        case kStanding: return "standing";
        case kLying: return "lying";
        case kSitting: return "sitting";
        case kKneel: return "kneel";
        case kHorse: return "horse";
        case kCrouch: return "crouch";
        case kCart: return "cart";
    }
    return "?";
}

// The stances an object holds a body in.
inline bool object_stance(uint8_t s) { return s == kLying || s == kSitting || s == kKneel || s == kCart; }

// What is synced: lying / sitting / kneeling / crouching / a cart slot. Standing
// is the absence of one; horse belongs to the riding path (WO-40, WO-136).
inline bool synced_stance(uint8_t s) { return object_stance(s) || s == kCrouch; }

inline bool owns_position(const Activity& a) {
    return (object_stance(a.stance) && a.stanceObj != 0) || (a.unstance != kNoUnstance && a.unstanceObj != 0);
}

inline Activity normalised(Activity a) {
    if (!synced_stance(a.stance)) { a.stance = 0; a.stanceObj = 0; a.cart = 0; }
    if (a.stance != kCart) a.cart = 0;
    if (a.unstance == kNoUnstance) a.unstanceObj = 0;
    if (a.minigame == kNoMinigame) a.minigameObj = 0;
    a.flags = owns_position(a) ? kFlagOwnsPos : 0;
    return a;
}

// WO-144 2.2: an avatar's crouch is the motion path's (WO-121/136: the partner's crouch bit,
// the actor's own SetCrouch). WO-141 neither sends it (the player's row) nor applies or undoes it
// on an avatar: the field's "wanted stance=none ... now stance=crouch" stood the avatar up every
// time the partner crouched ("Execution of action CrouchUp has failed").
inline Activity without_crouch(Activity a) {
    if (a.stance == kCrouch) { a.stance = 0; a.stanceObj = 0; a.cart = 0; }
    return normalised(a);
}
inline bool is_avatar_name(const char* n) {
    return n && n[0] == 'k' && n[1] == 'c' && n[2] == 'd' && n[3] == '2' && n[4] == 'm' && n[5] == 'p' && n[6] == '_';
}

// WO-144 5: the player sits on a bed's edge (the game's own player sit, before he lies down or
// gets up again). An NPC body has no way into it: "Couldn't find actions to get NPC into game
// loaded state ... Stance: sitting Using object: smartObject[Bed/...]" (observed, every try).
// Lying on the same bed is in step at once (observed), so an avatar is shown lying on it.
inline bool is_bed_name(const char* n) { return n && std::strstr(n, "[Bed/") != nullptr; }
inline Activity bed_sit_as_lying(Activity a, bool objIsBed) {
    if (a.stance == kSitting && a.stanceObj && objIsBed) a.stance = kLying;
    return normalised(a);
}

inline bool none(const Activity& a) {
    const Activity n = normalised(a);
    return n.stance == 0 && n.unstance == kNoUnstance && n.minigame == kNoMinigame;
}

// The body-visible part (what an apply changes): stance + object, unstance +
// location. The minigame is the player's and is shown, not applied.
inline bool same_body(const Activity& x, const Activity& y) {
    const Activity a = normalised(x), b = normalised(y);
    return a.stance == b.stance && a.stanceObj == b.stanceObj && a.cart == b.cart &&
           a.unstance == b.unstance && a.unstanceObj == b.unstanceObj;
}
inline bool same(const Activity& x, const Activity& y) {
    const Activity a = normalised(x), b = normalised(y);
    return same_body(a, b) && a.minigame == b.minigame && a.minigameObj == b.minigameObj;
}

// A one-shot the player plays at an object (the trough's WashFace) is not in his
// NPC state; the agent names the NPC unstance that shows it (housekeeper_faceWash
// at the same trough) for as long as it lasts. It stands in for the player's read
// only while he is in nothing else: a real stance or unstance always wins.
inline Activity with_shown(const Activity& real, const Activity& shown, bool live) {
    return (live && none(real)) ? normalised(shown) : real;
}

// ---- the wire (little-endian, 30 bytes) ----
inline void put16(uint8_t* p, uint16_t v) { p[0] = static_cast<uint8_t>(v); p[1] = static_cast<uint8_t>(v >> 8); }
inline void put64(uint8_t* p, uint64_t v) { for (int i = 0; i < 8; ++i) p[i] = static_cast<uint8_t>(v >> (8 * i)); }
inline uint16_t get16(const uint8_t* p) { return static_cast<uint16_t>(p[0] | (p[1] << 8)); }
inline uint64_t get64(const uint8_t* p) { uint64_t v = 0; for (int i = 7; i >= 0; --i) v = (v << 8) | p[i]; return v; }

inline void encode(const Activity& in, uint8_t out[kWireBytes]) {
    const Activity a = normalised(in);
    out[0] = a.stance; out[1] = a.cart; put64(out + 2, a.stanceObj);
    put16(out + 10, a.unstance); put64(out + 12, a.unstanceObj);
    out[20] = a.minigame; put64(out + 21, a.minigameObj);
    out[29] = a.flags;
}
inline bool decode(const uint8_t* p, size_t n, Activity* out) {
    if (n < kWireBytes) return false;
    Activity a;
    a.stance = p[0]; a.cart = p[1]; a.stanceObj = get64(p + 2);
    a.unstance = get16(p + 10); a.unstanceObj = get64(p + 12);
    a.minigame = p[20]; a.minigameObj = get64(p + 21);
    a.flags = p[29];
    *out = normalised(a);
    return true;
}

// ---- pacing ----
// The joiner's reconcile: a body out of step is applied at once, then checked
// again; a body the game keeps refusing backs off so one bad object cannot
// storm the frame.
struct Pace { double nextAt = 0; int misses = 0; };

// WO-143: the writer stays off a body the game places -- in step, or while it
// is tried. One the game keeps refusing here is written again once the retries
// back off (held, it stood wherever the stream had left it, and a forced look
// turned it bodily); each backed-off retry is held again. A cart stance stays
// held even when refused: its cart is not streamed, and a written cart horse
// walks off without it.
inline bool hold_wanted(bool owns, bool matched, int misses, bool waiting, uint8_t stance);

// WO-143: tools ride the apply only for a body that does not sit, lie or kneel
// on an object (the game's path to a seated tool goes through standing: the
// guest was stood up and sat down again), and not once the game has refused
// them three times with this activity (then shown exactly as WO-141 shows it).
constexpr int kHandsTriesBeforeDrop = 3;
inline bool hands_ride(bool handsSet, bool dropped, uint8_t stance);
constexpr double kRecheckS = 1.5;
constexpr double kBackoffS = 15.0;
constexpr int    kMissesBeforeBackoff = 4;
// WO-153 2: a copy never runs an action again and again on its own. The field's caravan horses were asked for their
// cart stance 26 times each in 340 s (104 `CartMount ... Slot is occupied` lines): the game's answer never changes
// (the slot's occupant is the horse itself), so after four quick and four 15 s tries the retry settles at one a minute
// (a new host row still starts the count again: set_desired).
constexpr int    kMissesBeforeLongBackoff = 8;
constexpr double kLongBackoffS = 60.0;
inline double next_delay(int misses) {
    return misses < kMissesBeforeBackoff ? kRecheckS : misses < kMissesBeforeLongBackoff ? kBackoffS : kLongBackoffS;
}

// The host's capture: a change is sent at once; an activity that is not "none"
// is sent again every kRefreshS (a joiner that came late, a lost frame).
constexpr double kRefreshS = 10.0;
inline bool send_due(bool changed, bool isNone, double sinceSentS) {
    return changed || (!isNone && sinceSentS >= kRefreshS);
}

// ---- WO-143 (declared beside Pace) -----------------------------------------
inline bool hold_wanted(bool owns, bool matched, int misses, bool waiting, uint8_t stance) {
    if (!owns) return false;
    return matched || misses < kMissesBeforeBackoff || !waiting || stance == kCart;
}
inline bool hands_ride(bool handsSet, bool dropped, uint8_t stance) {
    return handsSet && !dropped && !object_stance(stance);
}

} // namespace kcdmp::wo141rules
