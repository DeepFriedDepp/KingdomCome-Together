// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-214: the switch word, the status, the counters (wo214.h).
#include "wo214.h"
#include "wo203.h"
#include "wo163.h"
#include "log.h"
#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>

namespace kcdmp::wo214 {

namespace {

using namespace wo214rules;

std::atomic<uint32_t> g_bits{kBitsDefault};
std::atomic<bool> g_configured{false};

uint32_t twin_off() {
    static uint32_t s = 0xFFFFFFFFu;
    if (s == 0xFFFFFFFFu) {
        char b[256] = {};
        const DWORD n = GetEnvironmentVariableA("KCDMP_TEST_W214_OFF", b, sizeof b);
        s = (n > 0 && n < sizeof b) ? twin_off_bits(b) : 0;
        if (s) logf("WO214-CFG the bench's twin: bits 0x%X off for this game (KCDMP_TEST_W214_OFF=%s)", s, b);
    }
    return s;
}

std::atomic<uint32_t> c_turnSends{0}, c_rebindNoop{0}, c_silenceHolds{0}, c_silenceResumes{0}, c_forgets{0}, c_postureRefused{0};
std::atomic<uint32_t> c_baseLetGo[3]{}, c_baseRefused[3]{}, c_combatOver{0}, c_combatOutlived{0};
std::atomic<uint32_t> c_resumeMaxMs{0}, c_hostLeft{0}, c_churnRests{0};

// [op][bits:4] -> [effective bits:4]
uint8_t op(uint8_t opc, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (opc == kOpConfig) {
        if (len != 5 || cap < 4) return wo163::kRBadRequest;
        uint32_t bits = 0;
        std::memcpy(&bits, body + 1, 4);
        const uint32_t was = g_bits.exchange(bits);
        if (was != bits || !g_configured.exchange(true))
            logf("WO214-CFG bits=0x%X (was 0x%X; twin off 0x%X): send_turn=%d one_bind=%d owner_base=%d combat_end=%d reaction_rows=%d",
                 bits, was, twin_off(), on(kBitSendTurn), on(kBitOneBind), on(kBitOwnerBase), on(kBitCombatEnd), on(kBitReactionRows));
        const uint32_t eff = bits & ~twin_off();
        std::memcpy(out, &eff, 4);
        *outLen = 4;
        return wo163::kROk;
    }
    if (opc == kOpStatus) {
        char t[600];
        int n = status(t, sizeof t);
        if (n < 0) n = 0;
        if (static_cast<size_t>(n) >= sizeof t) n = static_cast<int>(sizeof t) - 1;
        if (static_cast<size_t>(n) > cap) n = static_cast<int>(cap);
        std::memcpy(out, t, static_cast<size_t>(n));
        *outLen = static_cast<size_t>(n);
        return wo163::kROk;
    }
    return wo163::kRBadRequest;
}

int status_reg(char* out, int n) { return status(out, n); }
const bool kReg = wo203::reg_op(kOpConfig, &op) && wo203::reg_op(kOpStatus, &op) && wo203::reg_status("w214", &status_reg);

} // namespace

bool on(uint32_t bit) { return (g_bits.load(std::memory_order_relaxed) & ~twin_off() & bit) != 0; }

void note_turn_send() { c_turnSends.fetch_add(1, std::memory_order_relaxed); }
void note_rebind_noop() { c_rebindNoop.fetch_add(1, std::memory_order_relaxed); }
void note_silence_hold() { c_silenceHolds.fetch_add(1, std::memory_order_relaxed); }
void note_silence_resume(double silentS) {
    c_silenceResumes.fetch_add(1, std::memory_order_relaxed);
    const uint32_t ms = static_cast<uint32_t>(silentS * 1000.0);
    uint32_t m = c_resumeMaxMs.load(std::memory_order_relaxed);
    while (ms > m && !c_resumeMaxMs.compare_exchange_weak(m, ms)) {}
}
void note_forget() { c_forgets.fetch_add(1, std::memory_order_relaxed); }
void note_base_given(Base b, bool atLetGo) {
    const int i = static_cast<int>(b) > 2 ? 2 : static_cast<int>(b);
    (atLetGo ? c_baseLetGo : c_baseRefused)[i].fetch_add(1, std::memory_order_relaxed);
}
void note_posture_refused() { c_postureRefused.fetch_add(1, std::memory_order_relaxed); }
std::atomic<uint32_t> c_ownedHits{0}, c_ownedDeaths{0};
void note_owned_reaction(bool death) { (death ? c_ownedDeaths : c_ownedHits).fetch_add(1, std::memory_order_relaxed); }
void note_combat_withdrawn(Stuck why, const char* name, double heldS, uint32_t frag) {
    const uint32_t k = (why == Stuck::FightOver ? c_combatOver : why == Stuck::HostLeft ? c_hostLeft : c_combatOutlived).fetch_add(1, std::memory_order_relaxed) + 1;
    if (k <= 60 || k % 50 == 0)
        logf("WO214-COMBATEND npc=%s frag=%u held %.1f s -> stopped (%s), the host's base handed over [n=%u]", name ? name : "?", frag, heldS,
             why == Stuck::FightOver ? "a combat row, its host NPC's fight is over" : why == Stuck::HostLeft ? "its host NPC left it for a base"
             : "a combat row longer than any row", k);
}

void note_combat_churn(const char* name, uint32_t frag, double spanS) {
    const uint32_t k = c_churnRests.fetch_add(1, std::memory_order_relaxed) + 1;
    if (k <= 60 || k % 50 == 0)
        logf("WO214-COMBATEND npc=%s frag=%u stopped %d times in %.1f s: it keeps coming back -- the rule rests on this copy for %.0f s [n=%u]",
             name ? name : "?", frag, wo214rules::kChurnStops, spanS, wo214rules::kChurnRestS, k);
}

int status(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n),
                         "w214 bits=0x%X twin_off=0x%X configured=%d | A.1 turn_sends=%u | B rebind_noop=%u silence_holds=%u silence_resumes=%u "
                         "resume_max_ms=%u forgets=%u | C.1 letgo_base host=%u neutral=%u nudge=%u refused_base host=%u neutral=%u nudge=%u "
                         "posture_refused=%u | C.2 stopped host_left=%u fight_over=%u outlived=%u churn_rests=%u | D owned_reaction_rows hits=%u deaths=%u",
                         g_bits.load(), twin_off(), g_configured.load() ? 1 : 0, c_turnSends.load(), c_rebindNoop.load(), c_silenceHolds.load(),
                         c_silenceResumes.load(), c_resumeMaxMs.load(), c_forgets.load(), c_baseLetGo[0].load(), c_baseLetGo[1].load(),
                         c_baseLetGo[2].load(), c_baseRefused[0].load(), c_baseRefused[1].load(), c_baseRefused[2].load(), c_postureRefused.load(),
                         c_hostLeft.load(), c_combatOver.load(), c_combatOutlived.load(), c_churnRests.load(), c_ownedHits.load(), c_ownedDeaths.load());
}

} // namespace kcdmp::wo214
