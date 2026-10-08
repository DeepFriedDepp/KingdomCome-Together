// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-163 Stage A: engine-free checks of native/KCDMP/wo163_rules.h -- where a sync attack keeps its row GUID (A1) and the
// MP-FIGHTSNAP accumulator (A5), against a real writer trace and fight118.py's own numbers. Linked into KCDMP_NativeTests;
// wo163_tests() returns the number of failures.
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>

#include "wo163_rules.h"

using namespace kcdmp::wo163;

namespace {
int g_fail = 0, g_pass = 0;
#define RCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)

// {the GUID at descriptor +0x7C, what the legacy read at +0x84 held}: the distinct sync-attack row dumps of the field logs (24 of them;
// 16 are the two sessions WO-162 counted). Both are 16 bytes in hex; the second starts 8 bytes into the first.
const char* kSyncDumps[][2] = {
    {"5810bf9a45e9e838ad87b5471c939c53", "ad87b5471c939c539a99d93f01010064"},
    {"afbd33aab7ca8e38a33c6778b9162de9", "a33c6778b9162de90e11d13f01010000"},
    {"23ab84478aea1e39913e80961260fe4c", "913e80961260fe4c4144c43f0101006d"},
    {"320014ad3f484332bd7e76caa14032c6", "bd7e76caa14032c6dfdd1d4001010000"},
    {"1dfd7af57da31e35a6b77509f8220ad1", "a6b77509f8220ad1babb3b4001010069"},
    {"f2a2b4f9b983d43b92a26de00478fe19", "92a26de00478fe19cdcccc3f0101006c"},
    {"e40f94f0c323e832b2578794acac3d0b", "b2578794acac3d0b0e11d13f01010061"},
    {"32b883c70d9fc436b593173b99fa39c9", "b593173b99fa39c96666064001010067"},
    {"90dbf5c661b73b3cba625c45617aef32", "ba625c45617aef328788684001010072"},
    {"040ddc21934415948641a19334ed0f03", "8641a19334ed0f03333333400101002e"},
    {"d1a77e19e0c0674365d0315a4f8c598e", "65d0315a4f8c598edfdd3d4001010074"},
    {"193a05576f55ca35886ec90ca9e6c585", "886ec90ca9e6c5856666663f01010070"},
    {"0cd58220f2c2723e8304cd327636b41d", "8304cd327636b41dbabb3b4001010020"},
    {"4a782eece223a33c87d435fe8f78d731", "87d435fe8f78d731acaa0a400101006f"},
    {"d85b823ce571a232b46494c37aea46a5", "b46494c37aea46a5acaa0a4001010000"},
    {"a95fc0722169ee36bbd18647478dfc21", "bbd18647478dfc219a99993f0101005f"},
    {"e44f88432dcc233715c507e05a4bfc87", "15c507e05a4bfc87babb1b4001010000"},
    {"d99c851062763b3ca7ae63d38d11ad3c", "a7ae63d38d11ad3c6666e63f01010038"},
    {"acfe450b76ad70399c3d7e54f4bfb9a0", "9c3d7e54f4bfb9a08788484001010070"},
    {"cbd9a49c50a6bc399bf0ce4bb7c5fe20", "9bf0ce4bb7c5fe20878848400101006e"},
    {"ccd184adbbaf1d0593d6f7ffb4924784", "93d6f7ffb4924784acaa2a4001010072"},
    {"5d45265912af1f348fd09209ff604d09", "8fd09209ff604d094644644001010022"},
    {"096120ac4a7dac3f8829c0d41c42c47d", "8829c0d41c42c47dcdcccc3f01010033"},
    {"800fb141336d3e3195c75eb0cc9dfb0c", "95c75eb0cc9dfb0c6666064001010033"},
};

bool hex16(const char* h, uint8_t out[16]) {
    if (std::strlen(h) != 32) return false;
    for (int i = 0; i < 16; ++i) { char b[3] = { h[2 * i], h[2 * i + 1], 0 }; out[i] = static_cast<uint8_t>(std::strtoul(b, nullptr, 16)); }
    return true;
}

struct TraceRow { double t; int wrote; float hx, hy, hz, wx, wy, wz; };
const TraceRow kTrace[] = {
#include "wo163_fight_trace.inc"
};
} // namespace

int wo163_tests(int* passed) {
    // ---- A1: the sync row's GUID is at +0x7C, the legacy +0x84 read is half of it --------------------------------------
    RCHECK(kSyncGuidOff == 0x7C && kSyncGuidLegacyOff == 0x84, "the sync GUID offset is +0x7C (the legacy +0x84 kept as a check)");
    {
        int n = 0, straddle = 0, differs = 0;
        for (auto& d : kSyncDumps) {
            uint8_t g7c[16], g84[16];
            if (!hex16(d[0], g7c) || !hex16(d[1], g84)) { RCHECK(false, "a fixture row is not 16 bytes of hex"); continue; }
            ++n;
            // rebuild the descriptor bytes the dump held: the GUID at 0x7C, the legacy read's last 8 bytes after it
            uint8_t desc[0x94]{};
            std::memcpy(desc + 0x7C, g7c, 16);
            std::memcpy(desc + 0x8C, g84 + 8, 8);
            uint8_t a[16], b[16];
            RCHECK(sync_guid_from(desc, sizeof desc, kSyncGuidOff, a) && std::memcmp(a, g7c, 16) == 0, "the +0x7C read gives the row's GUID");
            RCHECK(sync_guid_from(desc, sizeof desc, kSyncGuidLegacyOff, b) && std::memcmp(b, g84, 16) == 0, "the legacy read gives what the dump held");
            if (std::memcmp(a + 8, b, 8) == 0) ++straddle;
            if (std::memcmp(a, b, 16) != 0) ++differs;
            RCHECK(guid_nonzero(a) && guid_nonzero(b), "both reads are non-zero (the alt tail is sent)");
        }
        RCHECK(n == 24 && straddle == 24 && differs == 24, "24 dumps: the legacy read starts 8 bytes into the GUID in every one (%d/%d/%d)", n, straddle, differs);
        uint8_t x[16];
        uint8_t shortBuf[0x8B]{};
        RCHECK(!sync_guid_from(nullptr, 0x94, 0x7C, x) && !sync_guid_from(shortBuf, sizeof shortBuf, 0x7C, x), "a short buffer or none is refused");
        uint8_t z[16]{};
        RCHECK(!guid_nonzero(z), "an all-zero read is no GUID");
    }

    // ---- A5: the accumulator against a real writer trace and fight118's own numbers -------------------------------------
    // fight118.py (tools/wo118) on this file, runs of >= 5 unwritten frames: 6 holds; the largest hold 929.8 ms; resume step max 8.570 cm,
    // mean 3.849 cm; the largest render step in the second after a resume 12.745 cm. The applied position is the render position here
    // (0 of 1026 frames differ by 0.1 mm), which is the claim that no render hook is needed.
    {
        FightSnap fs;
        const size_t n = sizeof kTrace / sizeof kTrace[0];
        for (size_t i = 0; i < n; ++i) {
            const auto& r = kTrace[i];
            fs.frame(r.t / 1000.0, {r.hx, r.hy, r.hz}, {r.wx, r.wy, r.wz}, r.wrote == 1, false);
        }
        RCHECK(fs.worth_a_line(), "a window with holds is worth a line");
        const FightSnapOut o = fs.flush(0.0f);
        RCHECK(o.frames == n && o.holds == 6, "all %zu frames, 6 holds (got %u frames, %u holds)", n, o.frames, o.holds);
        RCHECK(std::fabs(o.holdMsMax - 929.8) < 1.0, "the longest hold %.1f ms (fight118: 929.8)", o.holdMsMax);
        RCHECK(std::fabs(o.resumeMaxCm - 8.570f) <= 1.0f, "resume_max %.2f cm (fight118: 8.57; within 1 cm)", o.resumeMaxCm);
        RCHECK(std::fabs(o.resumeMeanCm - 3.849f) <= 1.0f, "resume_mean %.2f cm (fight118: 3.85; within 1 cm)", o.resumeMeanCm);
        RCHECK(std::fabs(o.postHoldStepMaxCm - 12.745f) <= 1.0f, "post_hold_step_max %.2f cm (fight118: 12.74; within 1 cm)", o.postHoldStepMaxCm);
        RCHECK(o.stepMaxCm >= o.postHoldStepMaxCm, "the window's largest step is at least the post-hold one");
        RCHECK(o.snapsGt5m == 0, "no step over 5 m in this fight");
        RCHECK(o.corrP95Cm > 0 && o.corrP95Cm <= o.corrMaxCm + 0.001f, "the 95th percentile of the correction is inside its maximum (%.2f <= %.2f)", o.corrP95Cm, o.corrMaxCm);
        const FightSnapOut again = fs.flush(0.0f);
        RCHECK(again.frames == 0 && again.holds == 0, "a flush empties the window");
    }

    // ---- A5: the definitions, one at a time -----------------------------------------------------------------------------
    {
        FightSnap fs; double t = 0;
        // a standing puppet that is always written: no hold, no line
        for (int i = 0; i < 100; ++i, t += 0.016) fs.frame(t, {1, 1, 0}, {1, 1, 0}, true, false);
        RCHECK(!fs.worth_a_line(), "nothing held, not engaged: no line");
        // engaged alone is worth a line
        fs.frame(t, {1, 1, 0}, {1, 1, 0}, true, true);
        RCHECK(fs.worth_a_line(), "an engaged frame in the window is worth a line");
        const FightSnapOut o = fs.flush(0.0f);
        RCHECK(o.fightFrames == 1 && o.holds == 0, "one fight frame, no hold");
    }
    {
        // four unwritten frames are not a hold, five are
        FightSnap four, five; double t = 0;
        auto run = [&](FightSnap& f, int unwritten) {
            t = 0;
            f.frame(t, {0, 0, 0}, {0, 0, 0}, true, false);
            for (int i = 0; i < unwritten; ++i) { t += 0.016; f.frame(t, {0.1f * i, 0, 0}, {0, 0, 0}, false, false); }
            t += 0.016; f.frame(t, {0.5f, 0, 0}, {0.6f, 0, 0}, true, false);   // the resume: the engine at 0.5, we write 0.6 -> 10 cm
        };
        run(four, 4); run(five, 5);
        RCHECK(four.flush(0).holds == 0, "4 unwritten frames: not a hold");
        const FightSnapOut o = five.flush(0);
        RCHECK(o.holds == 1 && std::fabs(o.resumeMaxCm - 10.0f) < 0.01f, "5 unwritten frames: a hold, resume step 10 cm (got %.3f)", o.resumeMaxCm);
        RCHECK(std::fabs(o.holdMsMax - 80.0) < 0.5, "its length %.1f ms (from its first frame to the resume: 5 intervals of 16 ms, fight118's t[j] - t[i])", o.holdMsMax);
    }
    {
        // the resume frame's own step counts in the second after it; a step 1.2 s later does not
        FightSnap fs; double t = 0;
        fs.frame(t, {0, 0, 0}, {0, 0, 0}, true, false);
        for (int i = 0; i < 6; ++i) { t += 0.016; fs.frame(t, {0, 0, 0}, {0, 0, 0}, false, false); }
        t += 0.016; fs.frame(t, {0, 0, 0}, {0.30f, 0, 0}, true, false);       // resume: the applied position jumps 30 cm
        t += 1.2;   fs.frame(t, {0.3f, 0, 0}, {0.9f, 0, 0}, true, false);     // 1.2 s later a 60 cm step: outside the window
        const FightSnapOut o = fs.flush(0);
        RCHECK(std::fabs(o.postHoldStepMaxCm - 30.0f) < 0.01f, "post-hold step is the resume frame's 30 cm, not the later 60 (got %.2f)", o.postHoldStepMaxCm);
        RCHECK(std::fabs(o.stepMaxCm - 60.0f) < 0.01f, "the window's largest step is the 60 cm one");
    }
    {
        // the correction's 95th percentile from the histogram; the snaps counter; break_run keeps the window
        FightSnap fs; double t = 0;
        for (int i = 0; i < 100; ++i, t += 0.016) fs.frame(t, {0, 0, 0}, {i < 95 ? 0.01f : 0.10f, 0, 0}, true, false);
        const FightSnapOut o = fs.flush(0);
        RCHECK(std::fabs(o.corrMaxCm - 10.0f) < 0.01f && std::fabs(o.corrP95Cm - 2.0f) < 0.01f, "corr max 10 cm, p95 inside the first 2 cm bucket (got %.2f / %.2f)", o.corrMaxCm, o.corrP95Cm);
        FightSnap g; t = 0;
        g.frame(t, {0, 0, 0}, {0, 0, 0}, true, false);
        t += 0.016; g.frame(t, {6, 0, 0}, {6, 0, 0}, true, false);             // a 6 m step: the old counter
        RCHECK(g.flush(0).snapsGt5m == 1, "a step over 5 m is counted (the old counter kept for comparison)");
        FightSnap h; t = 0;
        h.frame(t, {0, 0, 0}, {0, 0, 0}, true, true);
        h.break_run();
        t += 0.016; h.frame(t, {9, 9, 0}, {9, 9, 0}, true, false);             // a body re-placed after a non-fight hold: no step across it
        const FightSnapOut ho = h.flush(0);
        RCHECK(ho.frames == 2 && ho.fightFrames == 1 && ho.stepMaxCm == 0.0f, "break_run keeps the window's frames and drops the step across it");
    }
    if (passed) *passed = g_pass;
    return g_fail;
}
