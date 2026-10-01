// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#include "join_native.h"
#include "engine.h"
#include "hangover.h"
#include "npc_drive.h"
#include "respawn_actions.h"
#include "log.h"

#include <windows.h>
#include <cmath>
#include <cstring>
#include <string>

namespace kcdmp::joinnative {

namespace {
bool  g_fallHeld = false;
DWORD g_fallUntil = 0;
// WO-147: an exact placement (the leash's fallback) can put the player above the ground (the host's
// reported height was off: the field's host stood 7-8 m above its grave; the live run placed him 55 m
// over a valley and he died on landing, the 6 s hold already over). Its hold lasts until the player is
// on the ground again (not flying, not dropping, the navmesh under it) plus a moment, at most this long.
DWORD g_fallHardUntil = 0;
bool  g_fallUntilLanded = false;
constexpr DWORD kFallMaxMs = 60000, kLandedGraceMs = 1000;
constexpr uint32_t kPlayerEid = 0x7777;

// The local player's physics says it is in the air (pe_status_living.bFlying); false when unreadable.
bool player_airborne() {
    void* e = engine::entity_by_id(kPlayerEid);
    npcdrive::PhysicsStatus ps{};
    return e && npcdrive::physics_status(e, &ps) && ps.present && ps.living && ps.flying;
}

// ...and its height still dropping, frame to frame (the live run: a 117 m fall read "not flying" at 6 s
// and the landing killed the player). Sampled every frame while an exact placement's hold runs.
float g_prevZ = 0.0f;
bool  g_havePrevZ = false;
DWORD g_lastFallAt = 0;
DWORD g_groundCheckAt = 0;
bool  g_onGround = false;

// Ground under the player (the navmesh within 2 m below or above): checked at most every 250 ms. A body the
// engine holds in the air while the area streams in drops only once it has (the live run hovered 80 m up for
// over a minute) -- the hold lasts until there is ground under it.
bool player_on_ground(DWORD now) {
    if (now - g_groundCheckAt < 250) return g_onGround;
    g_groundCheckAt = now;
    float p[3]{}, g[3]{};
    g_onGround = actions::player_position(p) && hangover::snap_to_ground(p, g) && std::fabs(g[2] - p[2]) < 2.0f;
    return g_onGround;
}
} // namespace

bool find_beside(float hx, float hy, float hz, float dist, float out[3], int* tried) {
    int n = 0;
    bool found = false;
    for (int k = 0; k < 8 && !found; ++k) {
        const float a = 0.785398f * static_cast<float>(k);
        const float in[3] = {hx + dist * std::cos(a), hy + dist * std::sin(a), hz};
        ++n;
        if (hangover::snap_to_ground(in, out) && std::fabs(out[2] - hz) < 3.0f) found = true;
    }
    if (tried) *tried = n;
    return found;
}

PlaceReport place(float hx, float hy, float hz, float dist) {
    PlaceReport r{};
    float g[3]{};
    // WO-147: dist <= 0 = this exact spot, no ground search. The leash's fallback when the ground
    // beside the host is not loaded here yet (the field: "no ground in 8 directions" 1.2 and 1.9 km
    // out) -- a spot the host itself just stood on; the fall damage is held a little longer, the
    // agent places the player on the ground beside the host once the area has loaded.
    const bool exact = !(dist > 0.0f);
    if (exact) { g[0] = hx; g[1] = hy; g[2] = hz; r.snapped = false; r.tried = 0; }
    else r.snapped = find_beside(hx, hy, hz, dist, g, &r.tried);
    actions::player_position(r.before);
    if (!exact && !r.snapped) {
        logf("MP-JOINPLACE host=(%.2f, %.2f, %.2f) dist=%.1f: no ground in %d directions -- NOT placed (the joiner keeps the spliced spot)",
             hx, hy, hz, dist, r.tried);
        std::memcpy(r.after, r.before, sizeof(r.after));
        return r;
    }
    std::memcpy(r.target, g, sizeof(g));
    // A lower landing after a hit arms the fall tracker (WO-113 s5.4): hold it.
    r.fallHeld = actions::suppress_fall_damage(true);
    if (r.fallHeld) {
        g_fallHeld = true;
        g_fallUntil = GetTickCount() + (exact ? 6000 : 3000);
        g_fallUntilLanded = exact;   // WO-147: then held until the player has landed (at most 20 s)
        g_fallHardUntil = GetTickCount() + kFallMaxMs;
        g_havePrevZ = false;
        g_lastFallAt = GetTickCount();
        g_groundCheckAt = 0;
        g_onGround = false;
    }
    const bool ran = actions::teleport_player(g[0], g[1], g[2]);
    actions::player_position(r.after);
    const float dx = r.after[0] - g[0], dy = r.after[1] - g[1];
    r.residual = std::sqrt(dx * dx + dy * dy);
    r.ok = ran && r.residual < 1.5f;
    logf("MP-JOINPLACE host=(%.2f, %.2f, %.2f) target=(%.2f, %.2f, %.2f) %s fall_held=%d before=(%.2f, %.2f, %.2f) "
         "after=(%.2f, %.2f, %.2f) residual_m=%.2f teleport=%s -> %s", hx, hy, hz, g[0], g[1], g[2],
         exact ? "exact (no ground search)" : (std::string("tried=") + std::to_string(r.tried)).c_str(), r.fallHeld ? 1 : 0,
         r.before[0], r.before[1], r.before[2], r.after[0], r.after[1], r.after[2], r.residual,
         ran ? "ran" : "FAILED", r.ok ? "placed" : "NOT placed");
    return r;
}

void tick() {
    if (!g_fallHeld) return;
    const DWORD now = GetTickCount();
    if (g_fallUntilLanded) {
        float pos[3]{};
        if (actions::player_position(pos)) {
            if (g_havePrevZ && g_prevZ - pos[2] > 0.03f) g_lastFallAt = now;   // still going down
            g_prevZ = pos[2];
            g_havePrevZ = true;
        }
    }
    if (static_cast<int>(now - g_fallUntil) >= 0) {
        // WO-147: an exact placement's hold waits for the landing (in the air, or still dropping a second
        // ago: another moment).
        if (g_fallUntilLanded && static_cast<int>(now - g_fallHardUntil) < 0
            && (player_airborne() || now - g_lastFallAt < kLandedGraceMs || !player_on_ground(now))) {
            g_fallUntil = now + kLandedGraceMs;
            return;
        }
        const bool landedWait = g_fallUntilLanded;
        g_fallHeld = false;
        g_fallUntilLanded = false;
        logf("MP-JOINPLACE fall damage given back (%s)%s", actions::suppress_fall_damage(false) ? "previous value restored" : "restore FAILED",
             landedWait ? " -- the player is on the ground (or 60 s passed)" : "");
    }
}

} // namespace kcdmp::joinnative
