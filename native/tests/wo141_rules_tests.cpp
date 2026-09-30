// WO-141: engine-free checks of the activity DLL half (native/KCDMP/wo141_rules.h):
// which stances are synced and own the body, the 30-byte wire, what counts as "in
// step" for the joiner's reconcile, the reconcile's pace, the host's refresh, and
// the player's one-shot shown as an NPC unstance.
// Linked into KCDMP_NativeTests; wo141_rules_tests() returns the number of failures.
#include <cstdio>
#include <cstring>

#include "wo141_rules.h"

using namespace kcdmp::wo141rules;

namespace {
int g_fail = 0, g_pass = 0;
#define ACHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)

Activity bench(uint64_t obj) { Activity a; a.stance = kSitting; a.stanceObj = obj; return a; }
Activity lean(uint16_t id, uint64_t loc) { Activity a; a.unstance = id; a.unstanceObj = loc; return a; }
} // namespace

int wo141_rules_tests(int* passed) {
    // the stances (XGenAI's enum, read live)
    ACHECK(object_stance(kLying) && object_stance(kSitting) && object_stance(kKneel) && object_stance(kCart), "bed, seat, kneeler, cart slot: an object holds the body");
    ACHECK(!object_stance(kStanding) && !object_stance(kHorse) && !object_stance(kCrouch) && !object_stance(kUndefined), "standing, horse, crouch, undefined: no object");
    ACHECK(synced_stance(kCrouch) && !synced_stance(kStanding) && !synced_stance(kHorse), "crouch is synced (the avatar's rides WO-136), standing and horse are not");
    ACHECK(std::strcmp(stance_name(kSitting), "sitting") == 0 && std::strcmp(stance_name(kLying), "lying") == 0 && std::strcmp(stance_name(99), "?") == 0, "names");

    // who owns the body (the position writer yields)
    ACHECK(owns_position(bench(0x1234)), "sitting on a bench: the activity owns the body");
    ACHECK(!owns_position(bench(0)), "sitting on no object: the writer keeps the body");
    ACHECK(owns_position(lean(168, 0x55)), "an unstance at a location: owns the body");
    ACHECK(!owns_position(lean(168, 0)), "an unstance on the spot (no location): the writer keeps the body");
    { Activity a; a.stance = kCrouch; ACHECK(!owns_position(a), "a crouch never owns the body"); }

    // normalised: what is not synced is dropped, the flag is derived
    {
        Activity a; a.stance = kStanding; a.stanceObj = 7; a.cart = 3; a.flags = 0xFF;
        const Activity n = normalised(a);
        ACHECK(n.stance == 0 && n.stanceObj == 0 && n.cart == 0 && n.flags == 0, "standing on an object: nothing (and the flags are recomputed)");
    }
    {
        Activity a = bench(9); a.cart = 2;
        ACHECK(normalised(a).cart == 0 && normalised(a).flags == kFlagOwnsPos, "a cart slot outside a cart is dropped; the flag is set for a seat");
        Activity c; c.stance = kCart; c.stanceObj = 5; c.cart = 2;
        ACHECK(normalised(c).cart == 2, "a cart keeps its slot");
    }
    { Activity a; a.unstanceObj = 44; ACHECK(normalised(a).unstanceObj == 0, "no unstance: no location"); }
    { Activity a; a.minigameObj = 44; ACHECK(normalised(a).minigameObj == 0, "no minigame: no object"); }
    ACHECK(none(Activity{}) && !none(bench(1)) && !none(lean(3, 0)), "none: standing about");
    { Activity a; a.minigame = 1; ACHECK(!none(a), "the player at the grindstone is not none"); }

    // the wire
    {
        Activity a = bench(0x0102030405060708ull); a.unstance = 195; a.unstanceObj = 0x069FF2CFB2012C0Full; a.minigame = 1; a.minigameObj = 0xAABBull;
        uint8_t w[kWireBytes]; encode(a, w);
        ACHECK(w[0] == kSitting && w[2] == 0x08 && w[9] == 0x01 && w[10] == 195 && w[11] == 0 && w[12] == 0x0F && w[20] == 1 && w[29] == kFlagOwnsPos, "the layout");
        Activity b; ACHECK(decode(w, kWireBytes, &b) && same(a, b) && b.stanceObj == a.stanceObj && b.unstanceObj == a.unstanceObj && b.minigameObj == 0xAABB, "round trip");
        ACHECK(!decode(w, kWireBytes - 1, &b), "29 bytes: refused");
        w[29] = 0; ACHECK(decode(w, kWireBytes, &b) && b.flags == kFlagOwnsPos, "the flag is the receiver's own reading, not the sender's");
    }

    // in step: the body part only; the minigame is shown, never applied
    {
        Activity a = bench(5), b = bench(5);
        b.minigame = 1;
        ACHECK(same_body(a, b) && !same(a, b), "a minigame alone: same body, not the same activity");
        ACHECK(!same_body(bench(5), bench(6)), "another bench: out of step");
        ACHECK(!same_body(lean(168, 5), lean(179, 5)), "another unstance on the same spot: out of step");
        Activity s; s.stance = kStanding; s.stanceObj = 5;
        ACHECK(same_body(s, Activity{}), "standing is none");
    }

    // the reconcile's pace: at once, again every 1.5 s, 15 s after 4 refusals
    ACHECK(next_delay(0) == kRecheckS && next_delay(3) == kRecheckS, "the first misses: 1.5 s");
    ACHECK(next_delay(4) == kBackoffS && next_delay(40) == kBackoffS, "a body the game keeps refusing: 15 s");

    // the host's capture: a change now, an activity again every 10 s, "none" once
    ACHECK(send_due(true, true, 0) && send_due(true, false, 0), "a change is sent at once");
    ACHECK(!send_due(false, false, 9.9) && send_due(false, false, 10.0), "an activity again after 10 s");
    ACHECK(!send_due(false, true, 1e6), "standing about is never repeated");

    // the player's one-shot (the trough's WashFace shown as housekeeper_faceWash)
    {
        const Activity wash = lean(195, 0x069FF2CFB2012C0Full);
        ACHECK(same(with_shown(Activity{}, wash, true), wash) && with_shown(Activity{}, wash, true).flags == kFlagOwnsPos, "standing, shown: the wash (it owns the avatar)");
        ACHECK(none(with_shown(Activity{}, wash, false)), "expired: none again");
        ACHECK(same(with_shown(bench(5), wash, true), bench(5)), "a real stance wins over a shown one-shot");
        Activity grind; grind.minigame = 1;
        ACHECK(same(with_shown(grind, wash, true), grind), "a minigame is not none: it wins too");
    }

    // WO-144 2.2: an avatar's crouch is the motion path's, never WO-141's
    {
        Activity crouch; crouch.stance = kCrouch;
        ACHECK(none(without_crouch(crouch)), "the player's crouch is not sent as an activity row");
        ACHECK(same_body(without_crouch(crouch), Activity{}), "a crouched avatar with 'none' wanted is in step (no CrouchUp)");
        ACHECK(same(without_crouch(bench(9)), bench(9)), "a seat is left as it is");
        ACHECK(is_avatar_name("kcd2mp_2") && is_avatar_name("kcd2mp_0") && !is_avatar_name("tzel_man_5") && !is_avatar_name("kcd2m") && !is_avatar_name(nullptr),
               "only the partners' avatars (kcd2mp_<id>)");
    }

    // WO-144 5: the player on a bed's edge is shown lying on that bed (an NPC body has no sit-down there)
    {
        ACHECK(is_bed_name("smartObject[Bed/bed_high2_04f3a2d6-405b-4f1c-b081-cea78c380284]") && !is_bed_name("smartObject[Bench.bench_3places1_9b3f]")
               && !is_bed_name("") && !is_bed_name(nullptr), "a bed by the smart object's own name");
        const Activity b = bed_sit_as_lying(bench(0x0D9DE4355D3B66DBull), true);
        ACHECK(b.stance == kLying && b.stanceObj == 0x0D9DE4355D3B66DBull && b.flags == kFlagOwnsPos, "sitting on a bed -> lying on the same bed (it owns the body)");
        ACHECK(same(bed_sit_as_lying(bench(7), false), bench(7)), "a bench stays a seat");
        Activity lie; lie.stance = kLying; lie.stanceObj = 7;
        ACHECK(same(bed_sit_as_lying(lie, true), normalised(lie)), "already lying: unchanged");
    }

    *passed = g_pass;
    return g_fail;
}
