// WO-147 -- see wo147.h.
#include "wo147.h"

#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>

#include "buffs.h"
#include "hits.h"
#include "log.h"
#include "rttr_abi.h"

namespace kcdmp::wo147 {
namespace {

std::atomic<uint32_t> c_testHits{0}, c_testFail{0};

void* c_soul_of(void* soul) {
    void* cs = soul ? buffs::as_c_soul(soul) : nullptr;
    return cs ? cs : soul;
}

} // namespace

uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (len < 1 || cap < 16) return kRBadRequest;
    const uint8_t op = body[0];
    const uint8_t* a = body + 1;
    const size_t n = len - 1;
    switch (op) {
    case kOpTestPlayerHit: {
        if (n != 12) return kRBadRequest;
        uint32_t eid = 0; float hp = 0, st = 0;
        std::memcpy(&eid, a, 4); std::memcpy(&hp, a + 4, 4); std::memcpy(&st, a + 8, 4);
        if (!eid || !std::isfinite(hp) || !std::isfinite(st) || hp < 0 || st < 0) return kRBadRequest;
        void* victim = c_soul_of(hits::soul_of_eid(eid));
        void* player = c_soul_of(rttr::read_player_soul());
        if (!victim || !player) { c_testFail.fetch_add(1); return kRNoActor; }
        // As the hit hook marks a real blow of the player's: the sampler's LocalHit carries "by the player".
        hits::mark_player_hit(eid);
        float before = -1, after = -1;
        rttr::soul_state(victim, "health", &before);
        const bool ok = rttr::apply_damage_soul(victim, st, hp, player);
        rttr::soul_state(victim, "health", &after);
        logf("WO147-TEST player hit stand-in on eid=0x%X hp -%.1f st -%.1f: %s (health %.1f -> %.1f) -- the swing is a stand-in, the rest is the real path",
             eid, hp, st, ok ? "taken" : "FAILED", before, after);
        if (!ok) { c_testFail.fetch_add(1); return kRFailed; }
        c_testHits.fetch_add(1);
        std::memcpy(out, &before, 4); std::memcpy(out + 4, &after, 4);
        *outLen = 8;
        return kROk;
    }
    case kOpStatus: {
        char t[160];
        int m = std::snprintf(t, sizeof(t), "wo147 test_hits=%u test_fail=%u", c_testHits.load(), c_testFail.load());
        if (m < 0) m = 0;
        if (static_cast<size_t>(m) > cap) m = static_cast<int>(cap);
        std::memcpy(out, t, m);
        *outLen = static_cast<size_t>(m);
        return kROk;
    }
    default:
        return kRBadRequest;
    }
}

} // namespace kcdmp::wo147
