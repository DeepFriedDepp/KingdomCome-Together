// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// Who holds a copy's body (holders.h). Main thread only (the position writer's loop and the modules it calls).
#include "holders.h"
#include "wo221.h"
#include "log.h"

#include <atomic>
#include <cstdio>
#include <unordered_map>

namespace kcdmp::holders {

namespace {

struct Row { Holder h = Holder::None; double since = 0; uint32_t handovers = 0; };
std::unordered_map<uint32_t, Row> g_rows;
uint32_t g_handover[static_cast<int>(Holder::Count)][static_cast<int>(Holder::Count)] = {};
std::atomic<uint32_t> c_handovers{0}, c_back{0};
uint32_t g_frames[static_cast<int>(Holder::Count)] = {};

int status(char* out, int n) {
    int k = std::snprintf(out, static_cast<size_t>(n), "on=%d bodies=%zu handovers=%u back_within_1s=%u frames:", on() ? 1 : 0, g_rows.size(), c_handovers.load(),
                          c_back.load());
    for (int i = 1; i < static_cast<int>(Holder::Count) && k > 0 && k < n; ++i)
        k += std::snprintf(out + k, static_cast<size_t>(n - k), " %s=%u", name(static_cast<Holder>(i)), g_frames[i]);
    // the commonest hand-overs
    for (int a = 1; a < static_cast<int>(Holder::Count); ++a)
        for (int b = 1; b < static_cast<int>(Holder::Count); ++b)
            if (g_handover[a][b] && k > 0 && k < n)
                k += std::snprintf(out + k, static_cast<size_t>(n - k), " %s>%s=%u", name(static_cast<Holder>(a)), name(static_cast<Holder>(b)), g_handover[a][b]);
    return k;
}
const bool kReg = wo221::add_status("holders", &status);

} // namespace

const char* name(Holder h) {
    switch (h) {
        case Holder::Writer: return "writer";
        case Holder::Driver: return "driver";
        case Holder::Seat: return "seat";
        case Holder::Activity: return "activity";
        case Holder::Fall: return "fall";
        case Holder::Mount: return "mount";
        case Holder::OneShot: return "one-shot";
        default: return "none";
    }
}

bool on() { return wo221::on(wo221::kBitHolders); }

Holder set(uint32_t eid, const char* npc, Holder h, double now) {
    Row& r = g_rows[eid];
    ++g_frames[static_cast<int>(h)];
    const Holder was = r.h;
    if (was == h) return was;
    if (was != Holder::None) {
        ++g_handover[static_cast<int>(was)][static_cast<int>(h)];
        const uint32_t k = c_handovers.fetch_add(1, std::memory_order_relaxed) + 1;
        const bool quick = now - r.since < 1.0;   // handed over and back inside a second: what the table is there to show
        if (quick) c_back.fetch_add(1, std::memory_order_relaxed);
        ++r.handovers;
        if (k <= 300 || k % 1000 == 0 || (quick && r.handovers % 50 == 1))
            logf("WO221-HOLD npc=%s %s -> %s (held %.1f s; its hand-over #%u)%s [n=%u]", npc ? npc : "?", name(was), name(h), now - r.since, r.handovers,
                 quick ? " QUICK" : "", k);
    }
    r.h = h;
    r.since = now;
    return was;
}

Holder of(uint32_t eid) {
    const auto it = g_rows.find(eid);
    return it == g_rows.end() ? Holder::None : it->second.h;
}

void forget(uint32_t eid) { g_rows.erase(eid); }

} // namespace kcdmp::holders
