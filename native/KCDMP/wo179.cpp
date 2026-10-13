// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#include "wo179.h"

#include <windows.h>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>

#include "local_state.h"
#include "log.h"
#include "respawn_actions.h"
#include "wo163.h"

namespace kcdmp::wo179 {
namespace {

constexpr float kJumpM = 12.0f;            // one 50 ms sample: a galloping horse moves < 1 m; a scene's move is tens of metres
constexpr uint64_t kSampleMs = 50;         // the position sample period
constexpr uint64_t kOwnMs = 3000;          // a jump this soon after this DLL's own teleport is its own
constexpr uint32_t kRing = 64;

struct Ev { uint32_t seq; uint8_t kind; uint64_t atMs; float from[3]; float to[3]; };

std::atomic<bool> g_on{true};
std::atomic<uint64_t> g_ownAt{0};
SRWLOCK g_lock = SRWLOCK_INIT;
Ev g_ring[kRing]{};
uint32_t g_seq = 0;                        // under g_lock
bool g_faded = false, g_fadeKnown = false; // main thread
uint64_t g_fadeSince = 0;
bool g_havePos = false;
float g_pos[3]{};
uint64_t g_nextSample = 0;
std::atomic<uint32_t> c_fadeOn{0}, c_fadeOff{0}, c_jumps{0}, c_ownJumps{0}, c_noFader{0}, c_polls{0};
// the last answer the agent can read off the main thread
std::atomic<bool> a_faded{false}, a_havePos{false};
std::atomic<float> a_x{0}, a_y{0}, a_z{0};

void push(uint8_t kind, uint64_t at, const float* from, const float* to) {
    AcquireSRWLockExclusive(&g_lock);
    Ev& e = g_ring[g_seq % kRing];
    e.seq = ++g_seq;
    e.kind = kind;
    e.atMs = at;
    for (int i = 0; i < 3; ++i) { e.from[i] = from ? from[i] : 0.0f; e.to[i] = to ? to[i] : 0.0f; }
    ReleaseSRWLockExclusive(&g_lock);
}

void put_u32(uint8_t* p, uint32_t v) { std::memcpy(p, &v, 4); }
void put_f(uint8_t* p, float v) { std::memcpy(p, &v, 4); }

} // namespace

void note_own_teleport() { g_ownAt.store(GetTickCount64()); }

void tick() {
    if (!g_on.load(std::memory_order_relaxed)) return;
    const uint64_t now = GetTickCount64();
    // the fade, every frame (a vcall on the controller the WO-113 resolver checked by RTTI; guarded inside)
    bool faded = false;
    if (actions::screen_faded_out(&faded)) {
        if (!g_fadeKnown) { g_fadeKnown = true; g_faded = faded; g_fadeSince = now; }
        else if (faded != g_faded) {
            const uint64_t held = now - g_fadeSince;
            g_faded = faded; g_fadeSince = now;
            if (faded) c_fadeOn.fetch_add(1); else c_fadeOff.fetch_add(1);
            push(faded ? kEvFadeOn : kEvFadeOff, now, g_havePos ? g_pos : nullptr, nullptr);
            logf("WO179-FADE-NATIVE %s held_ms=%llu", faded ? "on" : "off", static_cast<unsigned long long>(held));
        }
        a_faded.store(faded);
    } else {
        c_noFader.fetch_add(1);
    }
    // the local player's position, 20 Hz
    if (now < g_nextSample) return;
    g_nextSample = now + kSampleMs;
    localstate::LocalState s{};
    if (!localstate::read_local_state(&s) || !std::isfinite(s.x) || !std::isfinite(s.y) || !std::isfinite(s.z)) {
        a_havePos.store(false);
        return;   // a load, the menu: the next good sample starts over (no jump across a gap is invented)
    }
    const float p[3] = {s.x, s.y, s.z};
    if (g_havePos) {
        const float dx = p[0] - g_pos[0], dy = p[1] - g_pos[1], dz = p[2] - g_pos[2];
        const float d = std::sqrt(dx * dx + dy * dy + dz * dz);
        if (d > kJumpM) {
            const bool own = now - g_ownAt.load() < kOwnMs;
            if (own) c_ownJumps.fetch_add(1); else c_jumps.fetch_add(1);
            push(own ? kEvOwnJump : kEvJump, now, g_pos, p);
            logf("WO179-JUMP from=%.1f,%.1f,%.1f to=%.1f,%.1f,%.1f d=%.0f own=%d", g_pos[0], g_pos[1], g_pos[2], p[0], p[1], p[2], d, own ? 1 : 0);
        }
    }
    g_havePos = true;
    std::memcpy(g_pos, p, sizeof g_pos);
    a_x.store(p[0]); a_y.store(p[1]); a_z.store(p[2]); a_havePos.store(true);
}

int status_text(char* out, int n) {
    return std::snprintf(out, n, "on=%d fade_on=%u fade_off=%u jumps=%u own_jumps=%u no_fader=%u polls=%u faded=%d",
                         g_on.load() ? 1 : 0, c_fadeOn.load(), c_fadeOff.load(), c_jumps.load(), c_ownJumps.load(), c_noFader.load(),
                         c_polls.load(), a_faded.load() ? 1 : 0);
}

uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    switch (op) {
        case kOpStatus: {
            const int k = status_text(reinterpret_cast<char*>(out), static_cast<int>(cap));
            *outLen = k <= 0 || cap == 0 ? 0 : (static_cast<size_t>(k) < cap ? static_cast<size_t>(k) : cap - 1);
            return wo163::kROk;
        }
        case kOpConfig: {
            if (len != 2 || cap < 1) return wo163::kRBadRequest;
            if (body[1] != 255) {
                const bool was = g_on.exchange(body[1] != 0);
                if (was != g_on.load()) logf("WO179-CFG the scene watch %s", g_on.load() ? "on" : "off");
            }
            out[0] = g_on.load() ? 1 : 0;
            *outLen = 1;
            return wo163::kROk;
        }
        case kOpPoll: {
            if (len != 5 || cap < 23) return wo163::kRBadRequest;
            c_polls.fetch_add(1);
            uint32_t since = 0; std::memcpy(&since, body + 1, 4);
            const uint64_t now = GetTickCount64();
            AcquireSRWLockShared(&g_lock);
            const uint32_t last = g_seq;
            put_u32(out, last);
            out[4] = a_faded.load() ? 1 : 0;
            out[5] = a_havePos.load() ? 1 : 0;
            put_f(out + 6, a_x.load()); put_f(out + 10, a_y.load()); put_f(out + 14, a_z.load());
            uint8_t nEv = 0;
            size_t at = 19;
            const uint32_t first = (last > kRing && since < last - kRing) ? last - kRing : since;
            for (uint32_t s = first + 1; s <= last && at + 37 <= cap && nEv < 32; ++s) {
                const Ev& e = g_ring[(s - 1) % kRing];
                if (e.seq != s) continue;
                put_u32(out + at, e.seq); out[at + 4] = e.kind;
                put_u32(out + at + 5, static_cast<uint32_t>(now - e.atMs));
                for (int i = 0; i < 3; ++i) { put_f(out + at + 9 + 4 * i, e.from[i]); put_f(out + at + 21 + 4 * i, e.to[i]); }
                at += 33; ++nEv;
            }
            ReleaseSRWLockShared(&g_lock);
            out[18] = nEv;
            *outLen = at;
            return wo163::kROk;
        }
        case kOpFade: {
            if (len != 6 || cap < 1) return wo163::kRBadRequest;
            float secs = 0.5f; std::memcpy(&secs, body + 2, 4);
            if (!std::isfinite(secs) || secs < 0.05f || secs > 5.0f) secs = 0.5f;
            const bool ok = body[1] ? actions::fade_out(secs) : actions::fade_in(secs);
            logf("WO179-FADE-OWN %s %.2f s %s", body[1] ? "out" : "in", secs, ok ? "ok" : "REFUSED (no fader / not ours)");
            out[0] = ok ? 1 : 0;
            *outLen = 1;
            return wo163::kROk;
        }
        default:
            return wo163::kRBadRequest;
    }
}

} // namespace kcdmp::wo179
