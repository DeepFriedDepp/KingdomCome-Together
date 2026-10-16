// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-190: engine-free checks of the animation trace's limits, the idle watch, the stand-in's weapon, the re-queue gate, the gait hold and the
// copy's swing takeover (wo190_rules.h). Linked into KCDMP_NativeTests; wo190_tests() returns the number of failures.
#include <cstdio>
#include <cstring>
#include <string_view>

#include "wo190_rules.h"

using namespace kcdmp;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo190_tests(int* passed) {
    using namespace wo190rules;
    // ---- 0.1 the trace's limits
    {
        Bucket b;
        int ok = 0;
        for (int i = 0; i < 100; ++i) ok += b.take(10.0, kBodyLinesPerS, kBodyBurst) ? 1 : 0;
        WCHECK(ok == static_cast<int>(kBodyBurst), "a body writes its burst at once, no more (%d)", ok);
        WCHECK(!b.take(10.05, kBodyLinesPerS, kBodyBurst) && b.take(10.2, kBodyLinesPerS, kBodyBurst), "then kBodyLinesPerS a second");
        MinuteCap m;
        int n = 0;
        for (int i = 0; i < kAutoLinesPerMin + 50; ++i) n += m.take(5.0, kAutoLinesPerMin) ? 1 : 0;
        WCHECK(n == kAutoLinesPerMin && m.dropped == 50, "the minute's cap holds and counts the rest");
        WCHECK(m.take(65.1, kAutoLinesPerMin), "a new minute starts again");
        WCHECK(mode_from_byte(2) == Mode::On && mode_from_byte(1) == Mode::Auto && mode_from_byte(0) == Mode::Off && mode_from_byte(9) == Mode::Off,
               "the mode byte");
        WCHECK(std::string_view(mode_name(Mode::Auto)) == "auto" && std::string_view(role_name(Role::Figure)) == "figure", "the log words");
        int x = 0, y = 0;
        ScopeSnap a{true, false, &x, 12, 0, 3}, c = a;
        WCHECK(!scope_changed(a, c), "the same action: no line");
        c.frag = 13; WCHECK(scope_changed(a, c), "another fragment: a line");
        c = a; c.action = &y; WCHECK(scope_changed(a, c), "another action object (the same fragment played again): a line");
        c = a; c.empty = true; WCHECK(scope_changed(a, c), "emptied: a line");
        ScopeSnap e1{true, true, nullptr, 0, 0, 0}, e2{true, true, nullptr, 5, 1, 2};
        WCHECK(!scope_changed(e1, e2), "two empty reads are the same");
    }
    // ---- A1: the idle watch
    {
        WCHECK(idle_watch_due(true, true, true, 0.3, -1) && idle_watch_due(true, true, true, 0.3, 1.2), "empty 0.25 s on a fighting body: re-queued");
        WCHECK(!idle_watch_due(true, true, true, 0.2, -1), "not before 0.25 s");
        WCHECK(!idle_watch_due(true, true, true, 0.6, 0.5), "at most once a second");
        WCHECK(!idle_watch_due(true, false, true, 2.0, -1) && !idle_watch_due(false, true, true, 2.0, -1) && !idle_watch_due(true, true, false, 2.0, -1),
               "not out of a fight, not when off, not when the full body holds an action");
    }
    // ---- A1: the stand-in's weapon (what the body holds -> the reaction rows' hand tags)
    {
        char t[48];
        WCHECK(hand_tags(1, 8, t, sizeof t) == 1 && std::string_view(t) == "r_swords+l_shield", "sword and shield (%s)", t);
        WCHECK(hand_tags(8, 1, t, sizeof t) == 1 && std::string_view(t) == "r_swords+l_shield", "the shield read in slot 0: the weapon is the other hand (%s)", t);
        WCHECK(hand_tags(4, -1, t, sizeof t) == 1 && std::string_view(t) == "r_longsword+l_longsword", "a longsword is two-handed (%s)", t);
        WCHECK(hand_tags(5, -1, t, sizeof t) == 1 && std::string_view(t) == "r_bluntWeapon+l_noShield", "a mace (%s)", t);
        WCHECK(hand_tags(3, 8, t, sizeof t) == 1 && std::string_view(t) == "r_bluntWeapon+l_shield", "an axe with a shield (%s)", t);
        WCHECK(hand_tags(7, -1, t, sizeof t) == 1 && std::string_view(t) == "r_halberd+l_halberd", "a halberd (%s)", t);
        WCHECK(hand_tags(0, -1, t, sizeof t) == 1 && std::string_view(t) == "r_shortSwords+l_noShield", "a dagger (%s)", t);
        WCHECK(hand_tags(12, -1, t, sizeof t) == 2 && std::string_view(t) == "r_noweapon+l_noShield", "bare hands (%s)", t);
        WCHECK(hand_tags(11, -1, t, sizeof t) == 0 && t[0] == 0, "a torch names no reaction row: unknown, the agent keeps its own guess");
        WCHECK(hand_tags(-1, -1, t, sizeof t) == 0, "unreadable: unknown");
        WCHECK(hand_tags(1, -1, t, 4) == 0, "a too-small buffer: unknown");
    }
    // ---- A3: the re-queue gate and the still deadband
    {
        RequeueGate g;
        WCHECK(g.allow(1, 1.0), "a first change is sent");
        WCHECK(!g.allow(1, 1.01) && g.deduped == 1, "the same value is never re-sent");
        int sent = 1;
        for (int i = 0; i < 20; ++i) sent += g.allow(i % 2 == 0 ? 0 : 1, 1.02 + i * 0.001) ? 1 : 0;
        WCHECK(sent <= static_cast<int>(kRequeueBurst) + 1 && g.limited > 0, "a value flipping every frame is held to the burst (%d sent, %u limited)", sent, g.limited);
        WCHECK(g.allow(g.last == 0 ? 1 : 0, 3.0), "and passes again once the window allows");
        WCHECK(!moving_with_hysteresis(false, 0.15f) && moving_with_hysteresis(false, 0.30f), "standing: starts above 2x kStillMps");
        WCHECK(moving_with_hysteresis(true, 0.15f) && !moving_with_hysteresis(true, 0.05f), "moving: stops below kStillMps");
    }
    // ---- A4: the gait through a stream gap
    {
        WCHECK(gait_speed(0.0f, 1.4f, 0.3, false) == 1.4f, "a figure: a 0.3 s gap keeps the last walk");
        WCHECK(gait_speed(0.0f, 1.4f, 0.6, false) == 0.0f, "past 0.5 s it stops");
        WCHECK(gait_speed(0.0f, 1.4f, 0.3, true) == 0.0f, "a source that said it stopped stops at once");
        WCHECK(gait_speed(1.2f, 1.4f, 0.3, false) == 1.2f, "a body still rendering motion keeps its own speed");
        WCHECK(gait_speed(0.0f, 0.1f, 0.3, false) == 0.0f, "a creep under the walk floor is never held");
        WCHECK(gait_speed(0.0f, 1.4f, -1.0, false) == 0.0f, "interpolating (no gap): nothing to hold -- a standing stream stands");
        WCHECK(gait_speed(0.0f, 1.4f, 0.2, false, kCopyGaitHoldS) == 1.4f && gait_speed(0.0f, 1.4f, 0.3, false, kCopyGaitHoldS) == 0.0f,
               "a copy (a moved-gated stream: a stop looks like a gap) holds only kCopyGaitHoldS");
        WCHECK(moving_floor(0.05f, 1.3f, false) == 1.3f, "a start or a catch-up: the rendered walk counts at once (no idle while moving)");
        WCHECK(moving_floor(1.4f, 3.2f, false) == 1.4f && moving_floor(0.05f, 3.2f, false) == kFloorCapMps,
               "a catch-up faster than the source never lifts a walk to a run");
        WCHECK(moving_floor(3.0f, 3.2f, false) == 3.0f, "a body the source runs keeps running");
        WCHECK(moving_floor(0.9f, 0.1f, false) == 0.9f, "a stop decays as before");
        WCHECK(moving_floor(0.2f, 12.0f, true) == 0.2f, "a snap is no pace");
    }
    // ---- A6: a copy still in its own last swing
    {
        WCHECK(leftover_endable(true, 0x20, false, false) && leftover_endable(true, 0x40, false, false), "0x20 / 0x40 left over: ended for the new swing");
        WCHECK(!leftover_endable(true, 0x40, true, false) && !leftover_endable(true, 0x20, false, true), "a real reaction is never cut off");
        WCHECK(!leftover_endable(false, 0x20, false, false), "off: refused as before");
        WCHECK(!leftover_endable(true, 0x80, false, false) && !leftover_endable(true, 0x100, false, false) && !leftover_endable(true, 0x200, false, false)
               && !leftover_endable(true, 2, false, false), "parry, dodge and guard are not leftovers");
        WCHECK(director_is_reaction("Hit") && director_is_reaction("HitMovement") && director_is_reaction("KnockDown") && director_is_reaction("Stagger"),
               "the director's reaction actions");
        WCHECK(!director_is_reaction("Attack") && !director_is_reaction("FailedAttack") && !director_is_reaction("Guard") && !director_is_reaction("none")
               && !director_is_reaction("") && !director_is_reaction(nullptr), "an attack's own actions are not reactions");
    }
    // ---- WO-192 4d: pose evidence outside fights
    {
        Evidence e;
        WCHECK(!e.gait(1.0f, 0.0) && !e.gait(1.0f, 0.1), "the first class and a steady one are no flip");
        WCHECK(!e.gait(2.0f, 0.2) && !e.gait(1.0f, 0.5), "two flips are not yet evidence");
        WCHECK(e.gait(2.0f, 0.9), "the third flip within 2 s is");
        Evidence s;
        s.gait(1.0f, 0.0); s.gait(2.0f, 1.0); s.gait(1.0f, 2.0);
        WCHECK(!s.gait(2.0f, 3.5), "three flips spread over 2.5 s are a walk, not a flap");
        WCHECK(!Evidence::empty_due(-1.0) && !Evidence::empty_due(0.49) && Evidence::empty_due(0.5), "an empty full body for 0.5 s");
        Evidence w;
        WCHECK(!w.on(0.0), "not armed: off");
        w.arm(10.0);
        WCHECK(w.on(10.0) && w.on(13.9) && !w.on(14.0), "armed: traced for 4 s");
    }
    *passed = g_pass;
    return g_fail;
}
