// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-223: the native half's switch word, status and the attend pairs (wo223.h).
#include "wo223.h"
#include "wo203.h"
#include "wo163.h"
#include "hits.h"
#include "wo132.h"
#include "script_context.h"
#include "rttr_abi.h"
#include "anchors.h"
#include "fault_guard.h"
#include "log.h"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <string>
#include <unordered_map>
#include <vector>

namespace kcdmp::wo223 {

namespace {
struct N { const char* n; uint32_t b; };
const N kNames[] = {{"w223_perceive", kBitPerceive}, {"w223_copy_target", kBitCopyTarget}, {"w223_combat_mode", kBitCombatMode}, {"w223_guard", kBitGuard},
                    {"w223_notice_log", kBitNoticeLog}};
}

uint32_t twin_off_bits(const char* names) {
    if (!names) return 0;
    uint32_t bits = 0;
    const char* p = names;
    while (*p) {
        while (*p == ',' || *p == ' ') ++p;
        const char* e = p;
        while (*e && *e != ',' && *e != ' ') ++e;
        const size_t len = static_cast<size_t>(e - p);
        if (len == 3 && std::strncmp(p, "all", 3) == 0) bits |= kBitsDefault & ~kBitNoticeLog;   // the notice log is an instrument: every run keeps it
        for (const N& x : kNames) if (std::strlen(x.n) == len && std::strncmp(p, x.n, len) == 0) bits |= x.b;
        p = e;
    }
    return bits;
}

namespace {

std::atomic<uint32_t> g_bits{kBitsDefault};
std::atomic<bool> g_configured{false};
std::atomic<uint8_t> g_role{0};

std::string env(const char* k) {
    char b[256] = {};
    const DWORD n = GetEnvironmentVariableA(k, b, sizeof b);
    return (n > 0 && n < sizeof b) ? std::string(b, n) : std::string();
}

uint32_t twin_off() {
    static uint32_t s = 0xFFFFFFFFu;
    if (s == 0xFFFFFFFFu) {
        const std::string v = env("KCDMP_TEST_W223_OFF");
        s = v.empty() ? 0 : twin_off_bits(v.c_str());
        if (s) logf("WO223-CFG the bench's twin: bits 0x%X off for this game (KCDMP_TEST_W223_OFF=%s)", s, v.c_str());
    }
    return s;
}

struct More { const char* area; StatusFn fn; };
More g_more[24];
int g_moreN = 0;

// ---- B: the attend pairs this DLL set (main thread only) -------------------------------------------------------------

struct Pair { uint32_t npc, fig; void* npcSoul; void* figSoul; ULONGLONG seen; };
std::vector<Pair> g_pairs;
uint32_t c_set = 0, c_cleared = 0, c_refused = 0, c_stale = 0;

int attend_impl(uint32_t npcEid, uint32_t figEid, bool want, const char* why) {
    for (size_t i = 0; i < g_pairs.size(); ++i) {
        Pair& p = g_pairs[i];
        if (p.npc != npcEid || p.fig != figEid) continue;
        if (want) { p.seen = GetTickCount64(); return 0; }
        // cleared only when both souls are still the ones the pair was set on (an entity id may be another body after a load)
        void* ns = hits::soul_of_eid(npcEid);
        void* fs = hits::soul_of_eid(figEid);
        int r = 0;
        if (ns && fs && ns == p.npcSoul && fs == p.figSoul) r = sctx::set_soul_relation(ns, fs, kAttendContext, false);
        else ++c_stale;
        g_pairs.erase(g_pairs.begin() + static_cast<std::ptrdiff_t>(i));
        ++c_cleared;
        logf("WO223-ATTEND off npc=0x%X figure=0x%X (%s) -> %d", npcEid, figEid, why ? why : "?", r);
        return 1;
    }
    if (!want) return 0;
    void* ns = hits::soul_of_eid(npcEid);
    void* fs = hits::soul_of_eid(figEid);
    const int r = (ns && fs) ? sctx::set_soul_relation(ns, fs, kAttendContext, true) : -1;
    if (r < 0) { ++c_refused; logf("WO223-ATTEND REFUSED npc=0x%X figure=0x%X (%s): %s", npcEid, figEid, why ? why : "?", (ns && fs) ? "the engine's store said no" : "a soul is missing"); return -1; }
    // r == 0: the pair read set already (a behaviour of the game's own set it): not ours to clear later
    if (r == 1) g_pairs.push_back(Pair{npcEid, figEid, ns, fs, GetTickCount64()});
    ++c_set;
    logf("WO223-ATTEND on npc=0x%X figure=0x%X (%s) -> %s", npcEid, figEid, why ? why : "?", r == 1 ? "set (read back)" : "was set already");
    return r;
}

void clear_all(const char* why) {
    while (!g_pairs.empty()) { const Pair p = g_pairs.back(); attend_impl(p.npc, p.fig, false, why); }
}

// ---- B.3: the enemy called on the host's own player, pinned to him (main thread only) --------------------------------------

constexpr const char* kForcedTarget = "combat_forcedTarget";
struct Pin { uint32_t npc; void* npcSoul; void* tgtSoul; ULONGLONG until; };
std::vector<Pin> g_pins;
uint32_t c_pinSet = 0, c_pinCleared = 0, c_pinRefused = 0;

void pin_clear(size_t i, const char* why) {
    const Pin p = g_pins[i];
    g_pins.erase(g_pins.begin() + static_cast<std::ptrdiff_t>(i));
    // cleared only when both souls are still the ones the pin was set on
    int r = 0;
    if (hits::soul_of_eid(p.npc) == p.npcSoul && rttr::read_player_soul() == p.tgtSoul) r = sctx::set_soul_relation(p.npcSoul, p.tgtSoul, kForcedTarget, false);
    ++c_pinCleared;
    logf("WO223-CALL off npc=0x%X (%s) -> %d", p.npc, why, r);
}

int pin_call(const char* name) {
    const uint32_t eid = hits::eid_of_name(name);
    void* ns = eid ? hits::soul_of_eid(eid) : nullptr;
    void* ps = rttr::read_player_soul();
    for (Pin& p : g_pins) if (p.npc == eid) { p.until = GetTickCount64() + static_cast<ULONGLONG>(kCallPinS * 1000.0); return 0; }
    const int r = (ns && ps && ns != ps) ? sctx::set_soul_relation(ns, ps, kForcedTarget, true) : -1;
    if (r < 0) ++c_pinRefused; else ++c_pinSet;
    // r == 0: the pair read set already (the game's own fight set it): not ours to clear
    if (r == 1) g_pins.push_back(Pin{eid, ns, ps, GetTickCount64() + static_cast<ULONGLONG>(kCallPinS * 1000.0)});
    logf("WO223-CALL on npc=%s -> the host's own player: forced target for %.0f s -> %s", name, kCallPinS,
         r == 1 ? "set (read back)" : r == 0 ? "was set already" : (ns && ps) ? "refused: the engine's store said no" : "refused: a soul is missing");
    return r;
}

void pins_tick() {
    if (g_pins.empty()) return;
    const ULONGLONG now = GetTickCount64();
    const bool off = !on(kBitPerceive) || role() != 1;
    for (size_t i = g_pins.size(); i-- > 0;)
        if (off || now >= g_pins[i].until) pin_clear(i, off ? "no longer the host, or w223_perceive off" : "its time is up");
}

// The bench's file (KCDMP_TEST_W223_FILE=1 in the game's environment, never in a player's game): kcdmp-w223.txt beside the game, read
// once a second when its text changed. Lines: "attend <npc name>" (to every figure), "clear".
void file_tick() {
    static int s_on = -1;
    if (s_on < 0) s_on = env("KCDMP_TEST_W223_FILE").empty() ? 0 : 1;
    if (!s_on) return;
    static ULONGLONG s_at = 0;
    const ULONGLONG now = GetTickCount64();
    if (now - s_at < 1000) return;
    s_at = now;
    static std::string s_last;
    char b[2048] = {};
    FILE* f = nullptr;
    if (fopen_s(&f, "kcdmp-w223.txt", "rb") != 0 || !f) return;
    const size_t n = std::fread(b, 1, sizeof b - 1, f);
    std::fclose(f);
    b[n] = 0;
    if (s_last == b) return;
    s_last = b;
    logf("WO223-FILE the bench's file changed (%zu bytes)", n);
    uint32_t figs[8]; void* fsouls[8];
    const int nf = hits::avatar_list(figs, fsouls, 8);
    char* ctx = nullptr;
    for (char* line = strtok_s(b, "\r\n", &ctx); line; line = strtok_s(nullptr, "\r\n", &ctx)) {
        if (std::strncmp(line, "clear", 5) == 0) { clear_all("the bench's file"); continue; }
        if (std::strncmp(line, "attend ", 7) != 0) continue;
        const uint32_t eid = hits::eid_of_name(line + 7);
        if (!eid) { logf("WO223-FILE attend %s: no such entity", line + 7); continue; }
        if (nf <= 0) logf("WO223-FILE attend %s: no figure here", line + 7);
        for (int i = 0; i < nf; ++i) attend_impl(eid, figs[i], true, "the bench's file");
    }
}

// 213: "figure|npc,npc,..." (main thread). out: [set][cleared][refused].
void attend_set(const char* text, uint8_t* out) {
    out[0] = out[1] = out[2] = 0;
    char b[1024];
    strncpy_s(b, sizeof b, text, _TRUNCATE);
    char* bar = std::strchr(b, '|');
    if (!bar) { out[2] = 1; return; }
    *bar = 0;
    const uint32_t fig = hits::eid_of_name(b);
    if (!fig) { out[2] = 1; return; }
    uint32_t named[32]; int nn = 0;
    char* ctx = nullptr;
    for (char* n = strtok_s(bar + 1, ",", &ctx); n && nn < 32; n = strtok_s(nullptr, ",", &ctx)) {
        const uint32_t eid = hits::eid_of_name(n);
        if (!eid) { ++out[2]; continue; }
        named[nn++] = eid;
        const int r = attend_impl(eid, fig, true, "the mod's gate");
        if (r == 1) ++out[0]; else if (r < 0) ++out[2];
    }
    for (size_t i = g_pairs.size(); i-- > 0;) {
        if (g_pairs[i].fig != fig) continue;
        bool keep = false;
        for (int k = 0; k < nn; ++k) if (named[k] == g_pairs[i].npc) { keep = true; break; }
        if (!keep) { const Pair p = g_pairs[i]; attend_impl(p.npc, p.fig, false, "no longer named"); ++out[1]; }
    }
}

// ---- B: the engine's own recognition numbers (read-only; main thread) ----------------------------------------------------
// The rule the engine applies to a player (C_RPGPerception's recognition data, RPGModule): the radius an NPC sees him at and the
// seconds of clear view it needs. Its parameters live in one static object of RPGModule (the game's rpg_param values and the code's
// own defaults). Found by structure, never by a fixed address: the one function that carries the profiler name below holds
// "movss xmm2,[rax+0x854]" right after a call to the object's getter, whose body ends "lea rax,[object]; add rsp,28h; ret". Only
// memory is read (guarded); nothing of the engine is called. Unread = the mod keeps its built-in numbers (this build's).
struct EngineP { float minR, minVis, maxVis, tP, tConPos, tConNeg, gain, decay, npcMod, skT, skR; };
EngineP g_eng{};
int g_engState = 0;              // 0 not read yet, 1 read, -1 not on this build
const char* g_engWhy = "not tried";

bool rd(const void* p, void* out, size_t n) {
    KCDMP_FAULT_READ(site, "wo223::rd");
    return fault::guarded(site, [&] { std::memcpy(out, p, n); });
}

bool engine_plausible(const EngineP& e) {
    return e.minR > 0.0f && e.minR < 20.0f && e.minVis < -0.05f && e.minVis > -20.0f && e.maxVis > 0.05f && e.maxVis < 50.0f && e.tP > 0.05f && e.tP < 60.0f &&
           e.tConPos > -20.0f && e.tConPos < 20.0f && e.tConNeg > -20.0f && e.tConNeg < 20.0f && e.gain > 0.0f && e.gain < 1.0f &&
           e.decay > -20.0f && e.decay < 20.0f && e.npcMod >= 0.0f && e.npcMod < 20.0f && e.skT > -5.0f && e.skT < 5.0f &&
           e.skR >= 0.0f && e.skR <= 1.0f;
}

void engine_resolve() {
    static ULONGLONG s_at = 0;
    static int s_tries = 0;
    if (g_engState != 0 || s_tries >= 24) return;
    const ULONGLONG now = GetTickCount64();
    if (now - s_at < 5000) return;
    s_at = now;
    ++s_tries;
    HMODULE rpg = GetModuleHandleA("RPGModule.dll");
    if (!rpg) { g_engWhy = "RPGModule.dll not loaded"; return; }
    int n = 0;
    const uint8_t* fn = anchor::function_by_string(rpg, "wh::rpgmodule::C_RPGPerception::CalcBaseRecognitionTime", &n);
    anchor::Range fr{}, text{}, data{};
    if (!fn || n != 1 || !anchor::function_range(rpg, fn, &fr) || !anchor::section(rpg, ".text", &text) || !anchor::section(rpg, ".data", &data)) {
        g_engState = -1; g_engWhy = "the recognition function was not found (string)";
        logf("WO223-ENGINE the engine's recognition numbers are NOT read: %s (%d functions) -- the mod keeps its built-in ones", g_engWhy, n);
        return;
    }
    static const uint8_t kUse[] = {0xF3, 0x0F, 0x10, 0x90, 0x54, 0x08, 0x00, 0x00};   // movss xmm2,[rax+0x854]
    const uint8_t* obj = nullptr;
    uint8_t code[0x1000];
    // the function is cut into fragments that follow each other (its primary range is only the first): read a fixed window
    const size_t room = static_cast<size_t>(text.end - fr.begin);
    const size_t len = room < sizeof code ? room : sizeof code;
    if (text.contains(fr.begin) && rd(fr.begin, code, len)) {
        for (size_t i = 32; i + sizeof kUse <= len && !obj; ++i) {
            if (std::memcmp(code + i, kUse, sizeof kUse) != 0) continue;
            for (size_t k = i - 5; k + 28 >= i && k > 0 && !obj; --k) {
                if (code[k] != 0xE8) continue;
                int32_t rel = 0;
                std::memcpy(&rel, code + k + 1, 4);
                const uint8_t* callee = fr.begin + k + 5 + rel;
                if (!text.contains(callee)) continue;
                uint8_t g[0x48];
                if (!rd(callee, g, sizeof g)) continue;
                for (size_t j = 0; j + 12 <= sizeof g; ++j) {
                    if (g[j] != 0x48 || g[j + 1] != 0x8D || g[j + 2] != 0x05 || g[j + 7] != 0x48 || g[j + 8] != 0x83 || g[j + 9] != 0xC4 || g[j + 11] != 0xC3) continue;
                    int32_t d = 0;
                    std::memcpy(&d, g + j + 3, 4);
                    const uint8_t* o = callee + j + 7 + d;
                    if (data.contains(o) && data.contains(o + 0x8A4)) { obj = o; break; }
                }
            }
        }
    }
    if (!obj) {
        g_engState = -1; g_engWhy = "the parameter object was not found (structure)";
        logf("WO223-ENGINE the engine's recognition numbers are NOT read: %s -- the mod keeps its built-in ones", g_engWhy);
        return;
    }
    uint8_t raw[0x8A8];
    EngineP e{};
    if (!rd(obj, raw, sizeof raw)) { g_engWhy = "the parameter object did not read"; return; }
    auto f = [&](size_t off) { float v = 0; std::memcpy(&v, raw + off, 4); return v; };
    e.minR = f(0x820); e.minVis = f(0x838); e.maxVis = f(0x83C); e.tP = f(0x848); e.tConPos = f(0x84C); e.tConNeg = f(0x850); e.gain = f(0x854);
    e.decay = f(0x858); e.npcMod = f(0x868); e.skT = f(0x89C); e.skR = f(0x8A0);
    if (!engine_plausible(e)) { g_engWhy = "the numbers read are not plausible yet (the object is filled on its first use)"; return; }   // tried again
    g_eng = e;
    g_engState = 1;
    g_engWhy = "";
    logf("WO223-ENGINE the engine's recognition numbers: min view radius %.2f m, visibility floor %.2f and ceiling %.2f, time %.2f s (conspicuousness x %.2f / x %.2f), "
         "distance gain %.3f, decay %.2f /s, non-player time x %.2f, stealth skill: time x %.2f, radius x %.2f",
         e.minR, e.minVis, e.maxVis, e.tP, e.tConPos, e.tConNeg, e.gain, e.decay, e.npcMod, e.skT, e.skR);
}

int engine_text(char* out, int n) {
    if (g_engState != 1) return std::snprintf(out, static_cast<size_t>(n), "unread (%s)", g_engWhy);
    return std::snprintf(out, static_cast<size_t>(n), "minR=%.3f,minVis=%.3f,maxVis=%.3f,tP=%.3f,tConPos=%.3f,tConNeg=%.3f,gain=%.4f,decay=%.3f,npcMod=%.3f,skT=%.3f,skR=%.3f",
                         g_eng.minR, g_eng.minVis, g_eng.maxVis, g_eng.tP, g_eng.tConPos, g_eng.tConNeg, g_eng.gain, g_eng.decay, g_eng.npcMod, g_eng.skT, g_eng.skR);
}

void copy_pins_tick();   // C: defined with the copies' record below

void tick() {
    file_tick();
    engine_resolve();
    pins_tick();
    copy_pins_tick();
    if (g_pairs.empty()) return;
    if (!on(kBitPerceive)) { clear_all("w223_perceive off"); return; }
    static ULONGLONG s_at = 0;
    const ULONGLONG now = GetTickCount64();
    if (now - s_at < 1000) return;
    s_at = now;
    static int s_file = -1;
    if (s_file < 0) s_file = env("KCDMP_TEST_W223_FILE").empty() ? 0 : 1;
    if (s_file) return;   // the bench's file names its pairs once: they stay until its "clear"
    for (size_t i = g_pairs.size(); i-- > 0;) {
        if (now - g_pairs[i].seen < static_cast<ULONGLONG>(kPairStaleS * 1000.0)) continue;
        const Pair p = g_pairs[i];
        attend_impl(p.npc, p.fig, false, "not named for 6 s");
    }
}

// ---- F: the copies whose host NPC fights the local player (the joiner, main thread) --------------------------------------

struct CopyRec { bool wrong = false; double wrongSince = 0, lastAt = 0; bool mine = false; ULONGLONG mineTick = 0; };
std::unordered_map<uint32_t, CopyRec> g_copies;

// C: the copies pinned to the local player (main thread; this module's tick)
struct CopyPin { void* npcSoul; void* tgtSoul; bool ours; ULONGLONG asserted; };
std::unordered_map<uint32_t, CopyPin> g_copyPins;
uint32_t c_cpSet = 0, c_cpCleared = 0, c_cpRefused = 0;

void copy_pins_tick() {
    const bool onNow = on(kBitCopyTarget) && role() == 2;
    if (!onNow && g_copyPins.empty()) return;
    const ULONGLONG nowT = GetTickCount64();
    void* ps = rttr::read_player_soul();
    for (auto it = g_copyPins.begin(); it != g_copyPins.end();) {
        const auto c = g_copies.find(it->first);
        const bool want = onNow && c != g_copies.end() && c->second.mine && nowT - c->second.mineTick <= kCopyPinFreshMs;
        void* ns = hits::soul_of_eid(it->first);
        const bool same = ns && ns == it->second.npcSoul && ps && ps == it->second.tgtSoul;
        if (want && same) { ++it; continue; }
        // cleared only when both souls are still the ones it was set on, and only a pin this module set
        if (same && it->second.ours) sctx::set_soul_relation(ns, ps, kForcedTarget, false);
        ++c_cpCleared;
        logf("WO223-PIN off npc=0x%X (%s)", it->first, want ? "its body or the player's changed" : "its host NPC no longer fights this player");
        it = g_copyPins.erase(it);
    }
    if (!onNow || !ps) return;
    for (const auto& kv : g_copies) {
        const CopyRec& r = kv.second;
        if (!r.mine || nowT - r.mineTick > kCopyPinFreshMs) continue;
        const auto p = g_copyPins.find(kv.first);
        if (p != g_copyPins.end() && nowT - p->second.asserted < kCopyPinAgainMs) continue;
        void* ns = hits::soul_of_eid(kv.first);
        if (!ns || ns == ps) continue;
        const int s = sctx::set_soul_relation(ns, ps, kForcedTarget, true);
        if (s < 0) { ++c_cpRefused; continue; }
        if (p == g_copyPins.end()) {
            g_copyPins[kv.first] = CopyPin{ns, ps, s == 1, nowT};
            const uint32_t k = ++c_cpSet;
            if (k <= 200 || k % 50 == 0)
                logf("WO223-PIN on npc=0x%X -> the local player: its host NPC fights you (forced target %s) [n=%u]", kv.first, s == 1 ? "set" : "was set already", k);
        } else {
            p->second.asserted = nowT;
            if (s == 1) p->second.ours = true;   // someone cleared the pair meanwhile (WO-187's own unpin): ours now
        }
    }
}
uint32_t c_meSamples = 0, c_meAimed = 0, c_meInFight = 0, c_meBoth = 0, c_wrongEdges = 0;
double g_wrongS = 0, g_meS = 0;

uint32_t c_fightSeat = 0, c_fightSeatAsked = 0;

int status_copies(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "me_samples=%u aimed=%u (%.1f %%) in_my_fight=%u (%.1f %%) both=%u (%.1f %%) wrong_s=%.1f of %.1f wrong_spells=%u "
                         "seats_left_for_a_fight=%u (own posture released: %u) pinned_to_you=%zu (set %u cleared %u refused %u)",
                         c_meSamples, c_meAimed, c_meSamples ? 100.0 * c_meAimed / c_meSamples : 0.0, c_meInFight,
                         c_meSamples ? 100.0 * c_meInFight / c_meSamples : 0.0, c_meBoth, c_meSamples ? 100.0 * c_meBoth / c_meSamples : 0.0,
                         g_wrongS, g_meS, c_wrongEdges, c_fightSeat, c_fightSeatAsked, g_copyPins.size(), c_cpSet, c_cpCleared, c_cpRefused);
}

// ---- D: an NPC fighting a figure, in combat mode or not (the host, main thread) ------------------------------------------
uint32_t c_figSamples = 0, c_figCombat = 0;
std::atomic<uint32_t> c_parityRetired{0}, c_parityKept{0};
int status_figcombat(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "samples=%u combat_mode=%u (%.1f %%) blows_on_a_body_out_of_combat_mode: left_to_the_engine=%u offered_by_wo222=%u",
                         c_figSamples, c_figCombat, c_figSamples ? 100.0 * c_figCombat / c_figSamples : 0.0, c_parityRetired.load(), c_parityKept.load());
}

int status_attend(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "pairs=%zu set=%u cleared=%u refused=%u stale=%u | called on the host: pinned=%zu set=%u cleared=%u refused=%u",
                         g_pairs.size(), c_set, c_cleared, c_refused, c_stale, g_pins.size(), c_pinSet, c_pinCleared, c_pinRefused);
}

int status(char* out, int n) {
    int k = std::snprintf(out, static_cast<size_t>(n), "bits=0x%X twin_off=0x%X role=%u", g_bits.load(), twin_off(), g_role.load());
    for (const N& x : kNames) {
        if (k < 0 || k >= n) break;
        k += std::snprintf(out + k, static_cast<size_t>(n - k), " %s=%d", x.n, on(x.b) ? 1 : 0);
    }
    for (int i = 0; i < g_moreN && k > 0 && k < n - 8; ++i) {
        k += std::snprintf(out + k, static_cast<size_t>(n - k), " | %s: ", g_more[i].area);
        if (k >= n) break;
        const int m = g_more[i].fn(out + k, n - k);
        if (m > 0) k += (m < n - k ? m : n - k - 1);
    }
    return k;
}

uint8_t op(uint8_t opc, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (opc == kOpConfig) {
        if (len != 6 || cap < 4) return wo163::kRBadRequest;
        uint32_t bits = 0;
        std::memcpy(&bits, body + 1, 4);
        const uint8_t wasRole = g_role.exchange(body[5]);
        if (wasRole != body[5]) logf("WO223-CFG role %u -> %u (0 none, 1 the host, 2 a joiner in the host's world)", wasRole, body[5]);
        const uint32_t was = g_bits.exchange(bits);
        if (was != bits || !g_configured.exchange(true)) {
            char t[400];
            status(t, sizeof t);
            logf("WO223-CFG %s (was 0x%X)", t, was);
        }
        const uint32_t eff = bits & ~twin_off();
        std::memcpy(out, &eff, 4);
        *outLen = 4;
        return wo163::kROk;
    }
    if (opc == kOpStatus) {
        char t[1000];
        int n = 0;
        if (len >= 2) {
            const int page = body[1];
            if (page == 0) {
                n = std::snprintf(t, sizeof t, "bits=0x%X twin_off=0x%X role=%u", g_bits.load(), twin_off(), g_role.load());
                for (const N& x : kNames) if (n > 0 && n < static_cast<int>(sizeof t)) n += std::snprintf(t + n, sizeof t - static_cast<size_t>(n), " %s=%d", x.n, on(x.b) ? 1 : 0);
            } else if (page <= g_moreN) {
                n = std::snprintf(t, sizeof t, "%s: ", g_more[page - 1].area);
                const int m = g_more[page - 1].fn(t + n, static_cast<int>(sizeof t) - n);
                if (m > 0) n += (m < static_cast<int>(sizeof t) - n ? m : static_cast<int>(sizeof t) - n - 1);
            }
        } else n = status(t, sizeof t);
        if (n < 0) n = 0;
        if (static_cast<size_t>(n) >= sizeof t) n = static_cast<int>(sizeof t) - 1;
        if (static_cast<size_t>(n) > cap) n = static_cast<int>(cap);
        std::memcpy(out, t, static_cast<size_t>(n));
        *outLen = static_cast<size_t>(n);
        return wo163::kROk;
    }
    if (opc == kOpAttendSet) {
        if (len < 3 || len > 1000 || cap < 3) return wo163::kRBadRequest;
        char t[1024];
        std::memcpy(t, body + 1, len - 1);
        t[len - 1] = 0;
        if (!on(kBitPerceive) || role() != 1) { out[0] = out[1] = 0; out[2] = 1; *outLen = 3; return wo163::kROk; }
        attend_set(t, out);
        *outLen = 3;
        return wo163::kROk;
    }
    if (opc == kOpCallPin) {   // "<npc name>": the enemy the mod called on the host's own player (main thread)
        if (len < 2 || len > 65 || cap < 1) return wo163::kRBadRequest;
        char t[66];
        std::memcpy(t, body + 1, len - 1);
        t[len - 1] = 0;
        const int r = (!on(kBitPerceive) || role() != 1) ? -1 : pin_call(t);
        out[0] = r < 0 ? 255 : static_cast<uint8_t>(r);
        *outLen = 1;
        return wo163::kROk;
    }
    if (opc == kOpEngine) {   // the engine's recognition numbers as text; empty = not read (the mod keeps its own)
        char t[240];
        int n = g_engState == 1 ? engine_text(t, sizeof t) : 0;
        if (n < 0) n = 0;
        if (static_cast<size_t>(n) > cap) n = static_cast<int>(cap);
        std::memcpy(out, t, static_cast<size_t>(n));
        *outLen = static_cast<size_t>(n);
        return wo163::kROk;
    }
    if (opc == kOpAttend) {
        if (len != 10 || cap < 1) return wo163::kRBadRequest;
        uint32_t npc = 0, fig = 0;
        std::memcpy(&npc, body + 2, 4);
        std::memcpy(&fig, body + 6, 4);
        const bool want = body[1] != 0;
        const int r = (want && !on(kBitPerceive)) ? -1 : attend_impl(npc, fig, want, "the agent");
        out[0] = r < 0 ? 255 : static_cast<uint8_t>(r);
        *outLen = 1;
        return wo163::kROk;
    }
    return wo163::kRBadRequest;
}

int status_reg(char* out, int n) { return status(out, n); }
const bool kReg = wo203::reg_op(kOpConfig, &op) && wo203::reg_op(kOpStatus, &op) && wo203::reg_op(kOpAttend, &op) && wo203::reg_op(kOpAttendSet, &op) &&
                  wo203::reg_op(kOpEngine, &op) && wo203::reg_op(kOpCallPin, &op) && wo203::reg_status("w223", &status_reg) && wo203::reg_tick("w223", &tick);
const bool kRegAttend = add_status("B attend", &status_attend);
const bool kRegEngine = add_status("B engine", &engine_text);
const bool kRegCopies = add_status("F copies", &status_copies);
const bool kRegFigCombat = add_status("D figure fights", &status_figcombat);

} // namespace

bool add_status(const char* area, StatusFn fn) {
    if (g_moreN >= 24) return false;
    g_more[g_moreN++] = More{area, fn};
    return true;
}
uint8_t role() { return g_role.load(); }
bool on(uint32_t bit) { return (g_bits.load(std::memory_order_relaxed) & ~twin_off() & bit) != 0; }

// (WO-132's own word for "held in the local player's fight": the discard list is every guarded copy near him -- p21 stood 17 seated
// villagers up with it)
bool fight_seat_rule(uint32_t copy) { return copy && role() == 2 && on(kBitCopyTarget) && wo132::engaged(copy); }

void note_fight_seat(const char* name, float offM, bool asked) {
    const uint32_t k = ++c_fightSeat;
    if (asked) ++c_fightSeatAsked;
    if (k <= 200 || k % 50 == 0)
        logf("WO223-SEAT npc=%s in your fight and %.1f m from its host NPC's place: its seat's hold ends, the stream takes the body%s [n=%u]", name ? name : "?",
             offM, asked ? ", its own posture released the game's way" : " (its own posture was asked to release a moment ago)", k);
}
int attend(uint32_t npcEid, uint32_t figureEid, bool want, const char* why) { return attend_impl(npcEid, figureEid, want, why); }

bool retire_parity_offer() {
    // As measured: a body that engaged the figure by itself is in combat mode at most blows (the joiner's clean hits: 16 of 16
    // in fights both players are in, 9 of 12 in his solo fights), in WO-222's staged reference scene only at half of them.
    // Retiring the offer (the order's wish if combat mode held) was built and is this switch's off side: the engine's word
    // alone. The offer stays the default: it cannot stack (it runs after the engine refused) and a body in guard still takes
    // it. With w223_perceive off there is no real skirmish: WO-222's way. The findings, Part D.
    const bool retire = !on(kBitCombatMode) && on(kBitPerceive);
    const uint32_t k = (retire ? c_parityRetired : c_parityKept).fetch_add(1) + 1;
    if (retire && (k <= 40 || k % 100 == 0))
        logf("WO223-REACT a body fighting the figure that struck it reads out of combat mode -- left to the engine's own answer, as for the host's own blow [n=%u]", k);
    return retire;
}

void note_figure_combat(bool combatMode) {
    ++c_figSamples;
    if (combatMode) ++c_figCombat;
}

void note_copy(uint32_t copy, const char* name, bool hostFightsMe, const char* haveName, bool match, bool inMyFight, double now) {
    CopyRec& r = g_copies[copy];
    r.mine = hostFightsMe; r.mineTick = GetTickCount64();   // C: the pin's wish (set and cleared by this module's tick)
    if (!on(kBitNoticeLog)) return;
    const double dt = (r.lastAt > 0 && now - r.lastAt < 1.0) ? now - r.lastAt : 0.0;   // a sample speaks for the time since the last one
    r.lastAt = now;
    if (!hostFightsMe) {
        if (r.wrong) { r.wrong = false; logf("WO223-COPY npc=%s its host NPC no longer fights you (it was wrong here for %.1f s)", name ? name : "?", now - r.wrongSince); }
        return;
    }
    ++c_meSamples;
    g_meS += dt;
    if (match) ++c_meAimed;
    if (inMyFight) ++c_meInFight;
    const bool right = match && inMyFight;
    if (right) ++c_meBoth; else g_wrongS += dt;
    if (!right && !r.wrong) {
        r.wrong = true; r.wrongSince = now;
        const uint32_t k = ++c_wrongEdges;
        if (k <= 400 || k % 50 == 0)
            logf("WO223-COPY npc=%s wrong: the host's NPC fights you; its copy here fights %s (in your fight: %d) [n=%u]", name ? name : "?",
                 haveName && *haveName ? haveName : "nobody", inMyFight ? 1 : 0, k);
    } else if (right && r.wrong) {
        r.wrong = false;
        logf("WO223-COPY npc=%s right again after %.1f s", name ? name : "?", now - r.wrongSince);
    }
    static double s_sum = 0;
    if (now - s_sum > 30.0 && c_meSamples > 0) {
        s_sum = now;
        char b[256];
        status_copies(b, sizeof b);
        logf("WO223-COPYSUM %s", b);
    }
}

} // namespace kcdmp::wo223
