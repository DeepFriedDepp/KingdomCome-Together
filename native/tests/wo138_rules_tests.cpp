// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-138: engine-free checks of the native NPC sender's rules and the pause gate
// (native/KCDMP/wo138_rules.h): the flags, engaged, the swing cue, the cull and
// far band, the send gate, the round robin, the frame meter's classification,
// and which PauseGame calls the gate declines. Linked into KCDMP_NativeTests;
// wo138_rules_tests() returns the number of failures.
#include <cstdio>

#include "wo138_rules.h"

using namespace kcdmp::wo138rules;

namespace {
int g_fail = 0, g_pass = 0;
#define RCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)

Sample at(float x, float y) { Sample s; s.x = x; s.y = y; s.z = 10; s.hp = 100; return s; }
} // namespace

int wo138_rules_tests(int* passed) {
    Config c;

    // flags: the Lua sender's bits
    {
        Sample s = at(0, 0);
        RCHECK(flags(s, false, false) == 0, "a plain live human: no bits");
        s.dead = true; s.ko = true; s.drawn = true; s.notHuman = true;
        RCHECK(flags(s, true, true) == (kFlagDead | kFlagKo | kFlagDrawn | kFlagSwing | kFlagEngaged | kFlagNotHuman),
               "every bit where the Lua sender puts it (1,2,4,8,32,128)");
        RCHECK((flags(s, false, false) & 0x40) == 0, "0x40 (resync) is never set by the sender");
    }

    // engaged: drawn, alive, not KO, within 12 m (2D) of the local player
    {
        Sample s = at(10, 0); s.drawn = true;
        RCHECK(engaged(s, true, 0, 0, 12), "drawn at 10 m: engaged");
        s.x = 13; RCHECK(!engaged(s, true, 0, 0, 12), "drawn at 13 m: not engaged");
        s.x = 5; s.dead = true; RCHECK(!engaged(s, true, 0, 0, 12), "a dead NPC is never engaged");
        s.dead = false; s.ko = true; RCHECK(!engaged(s, true, 0, 0, 12), "a knocked-out NPC is never engaged");
        s.ko = false; s.drawn = false; RCHECK(!engaged(s, true, 0, 0, 12), "sheathed: not engaged");
        s.drawn = true; RCHECK(!engaged(s, false, 0, 0, 12), "no player position: not engaged");
        Sample h = at(3, 4); h.z = 500; h.drawn = true;
        RCHECK(engaged(h, true, 0, 0, 5), "the distance is 2D (height ignored)");
    }

    // the swing cue: the player was just hit, a drawn NPC within 4 m
    {
        Sample s = at(3, 0); s.drawn = true;
        RCHECK(swing_cue(true, s, true, 0, 0), "hit + drawn at 3 m: cue");
        RCHECK(!swing_cue(false, s, true, 0, 0), "no hit: no cue");
        s.x = 5; RCHECK(!swing_cue(true, s, true, 0, 0), "5 m away: no cue");
        s.x = 3; s.drawn = false; RCHECK(!swing_cue(true, s, true, 0, 0), "sheathed: no cue");
    }

    // the cull and the far band
    {
        Config k; k.cull = true; k.cullRadius = 60; k.farBand = 150;
        RCHECK(cull(50, k, false, false, false, false) == Cull::Send, "inside the cull radius: sent");
        RCHECK(cull(100, k, false, false, false, false) == Cull::Culled, "in the far band, nothing due: culled");
        RCHECK(cull(100, k, false, true, false, false) == Cull::FarBand, "in the far band, heartbeat due: sent");
        RCHECK(cull(100, k, false, false, true, false) == Cull::FarBand, "in the far band, first dead: sent");
        RCHECK(cull(100, k, false, false, false, true) == Cull::FarBand, "in the far band, a KO change: sent");
        RCHECK(cull(200, k, false, true, true, true) == Cull::Culled, "past the far band: culled whatever changed");
        RCHECK(cull(200, k, true, false, false, false) == Cull::Send, "engaged is never culled");
        Config nf = k; nf.farBand = 0;
        RCHECK(cull(100, nf, false, true, false, false) == Cull::Culled, "no far band (not a shared world): culled");
        Config off = k; off.cull = false;
        RCHECK(cull(1e9f, off, false, false, false, false) == Cull::Send, "cull off: everything is sent");
    }

    // the send gate
    {
        Sample s = at(0, 0);
        Sent t;
        RCHECK(should_send(s, t, false, false, 0, c), "nothing sent yet: send");
        t.any = true; t.x = 0; t.y = 0; t.z = 10; t.hp = 100; t.at = 10.0;
        RCHECK(!should_send(s, t, false, false, 10.5, c), "unchanged within the heartbeat: quiet");
        RCHECK(should_send(s, t, false, false, 12.0, c), "the 2 s heartbeat: send");
        Sample m = s; m.x = 0.06f;
        RCHECK(should_send(m, t, false, false, 10.5, c), "moved 6 cm: send");
        m.x = 0.04f;
        RCHECK(!should_send(m, t, false, false, 10.5, c), "moved 4 cm: quiet (eps 5 cm)");
        Sample h = s; h.hp = 99.0f;
        RCHECK(should_send(h, t, false, false, 10.5, c), "health -1: send");
        h.hp = 99.8f;
        RCHECK(!should_send(h, t, false, false, 10.5, c), "health -0.2: quiet");
        Sample u = s; u.hp = -1.0f;
        RCHECK(!should_send(u, t, false, false, 10.5, c), "an unreadable health is not a change");
        Sample k = s; k.ko = true;
        RCHECK(should_send(k, t, false, false, 10.5, c), "KO changed: send");
        Sample d = s; d.drawn = true;
        RCHECK(should_send(d, t, false, false, 10.5, c), "drawn changed: send");
        RCHECK(should_send(s, t, true, false, 10.5, c), "engaged changed: send");
        RCHECK(should_send(s, t, false, true, 10.5, c), "a swing cue: send");
        Sample dd = s; dd.dead = true;
        RCHECK(should_send(dd, t, false, false, 10.5, c), "the first dead sample: send");
        Sent td = t; td.dead = true;
        RCHECK(!should_send(dd, td, false, false, 10.5, c), "still dead, nothing else: quiet (the heartbeat carries it)");
    }

    // the round robin: every NPC refreshed once per attrMs
    {
        Config k; k.emitMs = 100; k.attrMs = 200;
        RCHECK(attr_batch(40, k) == 20, "40 NPCs, attrs every 2 ticks: 20 per tick");
        RCHECK(attr_batch(1, k) == 1, "one NPC: one per tick");
        RCHECK(attr_batch(0, k) == 0, "nobody tracked: none");
        k.attrMs = 100;
        RCHECK(attr_batch(40, k) == 40, "attrs every tick: all of them");
        k.attrMs = 250;
        RCHECK(attr_batch(9, k) == 3, "9 NPCs over 3 ticks: 3 per tick");
    }

    // the frame meter
    {
        RCHECK(classify(0.25, 0.25) == World::Running, "game time = real time: running");
        RCHECK(classify(0.0, 0.25) == World::Frozen, "no game time: frozen");
        RCHECK(classify(0.005, 0.25) == World::Frozen, "2 % of real time: frozen");
        RCHECK(classify(0.05, 0.25) == World::Slowed, "20 %: slowed (the Apse ratio)");
        RCHECK(classify(0.2, 0.25) == World::Running, "80 %: running (a hitch)");
        RCHECK(classify(1.0, 0.0) == World::Running, "no real time elapsed: running (no verdict)");
        RCHECK(scale_permille(0.125, 0.25) == 500, "half speed: 500 permille");
        RCHECK(scale_permille(0, 0.25) == 0, "frozen: 0");
    }

    // the PauseGame gate
    {
        RCHECK(decline_pause(true, true, kSrcInGameMenu, kDefaultMask), "a session, the ESC menu pausing: declined");
        RCHECK(!decline_pause(false, true, kSrcInGameMenu, kDefaultMask), "no session: the ESC menu pauses as usual");
        RCHECK(!decline_pause(true, false, kSrcInGameMenu, kDefaultMask), "a resume always runs");
        RCHECK(!decline_pause(true, true, kSrcGameOver, kDefaultMask), "Game Over is not in the default mask");
        RCHECK(!decline_pause(true, true, kSrcVideoMode, kDefaultMask), "a video is not in the default mask");
        RCHECK(!decline_pause(true, true, kSrcScriptBind, kDefaultMask), "a script's pause is not in the default mask");
        RCHECK(decline_pause(true, true, kSrcScriptBind, kDefaultMask | (1u << kSrcScriptBind)),
               "the live checks' mask with bit 2: a script's pause is declined");
        RCHECK(!decline_pause(true, true, 40, 0xFFFFFFFFu), "a source past the mask width is never declined");
    }

    if (passed) *passed = g_pass;
    return g_fail;
}
