// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#include "wo194.h"
#include "wo194_rules.h"
#include "wo163.h"

#include "anchors.h"
#include "engine.h"
#include "fault_guard.h"
#include "hook_prologues.h"
#include "inline_hook.h"
#include "log.h"
#include "npcstate.h"
#include "wo137.h"
#include "wo178.h"
#include "wo189.h"

#include <windows.h>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <deque>
#include <mutex>
#include <string>
#include <vector>

namespace kcdmp::wo194 {

namespace {

using namespace wo194rules;

constexpr uint32_t kPlayerEntity = 0x7777;    // the local player's entity id (WO-125)

// WO-193's reads (each object's class is checked by RTTI before any of them is used; port notes in docs/WO-194-findings.md)
constexpr ptrdiff_t kHandlerCfg = 0x340;      // C_CutsceneHandler: its configuration (shared_ptr) -- EnqueueCutscene's argument is &handler+0x340
constexpr ptrdiff_t kHandlerState = 0x338;    // C_CutsceneHandler: its state (0 idle; the enqueue requires it)
constexpr ptrdiff_t kCfgAutoPlay = 0x20;      // C_CutsceneConfiguration: AutoPlay (OnCutsceneInitialized reads it at that moment)
constexpr ptrdiff_t kCfgCutscene = 0x30;      // C_CutsceneConfiguration: the I_Cutscene from the cutscene database
constexpr ptrdiff_t kCsName = 0x08;           // I_Cutscene: its name (char*)
constexpr ptrdiff_t kCsSequence = 0xB8;       // C_IngameCutscene: its IAnimSequence
constexpr ptrdiff_t kCsHasTime = 0xC0;        // C_IngameCutscene: "has a time of day" (Play skips the time set when 0)
constexpr ptrdiff_t kPlayerActive = 0x38, kPlayerInProgress = 0x80;   // C_CutscenePlayer (WO-188)
constexpr size_t kMgrNpcOf = 0x20;            // C_NPCManager vtable: the C_NPC of an entity id (WO-189)
constexpr ptrdiff_t kMgrFFCount = 0xF0;       // C_NPCManager: NPCs in a FastForward (the positioning gate waits for 0)
constexpr size_t kNpcInFF = 0x4A0, kNpcStopFF = 0x4B8;   // C_NPC vtable: IsInFastForward(id), StopFastForward(id) (id 0 = any)
constexpr size_t kMovieStop = 0x100, kMovieGetTime = 0x1F0, kMovieSetTime = 0x200;   // CMovieSystem vtable (CryMovie)
// WO-202 V.1: GetPlayingSpeed / SetPlayingSpeed, the slots between and after the time pair (read this WO: the playing entry's +0x10 is the
// time, +0x14 the speed; SetPlayingSpeed is SetPlayingTime's shape without the IsPlaying test, as CryEngine's own source has it)
constexpr size_t kMovieGetSpeed = 0x1F8, kMovieSetSpeed = 0x208;

constexpr char kExpEnqueue[] = "?EnqueueCutscene@C_CutscenePlayer@guimodule@wh@@UEAAXAEAV?$shared_ptr@VC_CutsceneConfiguration@guimodule@wh@@@std@@@Z";

// The CryMovie functions' first bytes on the build WO-193 read (the build-identity test for a function we CALL).
// GetPlayingTime: mov [rsp+8],rbx; push rdi; sub rsp,20h; mov rdi,rcx; mov rbx,rdx
constexpr uint8_t kGetTimePro[16] = {0x48, 0x89, 0x5C, 0x24, 0x08, 0x57, 0x48, 0x83, 0xEC, 0x20, 0x48, 0x8B, 0xF9, 0x48, 0x8B, 0xDA};
// SetPlayingTime: mov [rsp+8],rbx; mov [rsp+10h],rsi; push rdi; sub rsp,30h; mov rsi,rcx
constexpr uint8_t kSetTimePro[18] = {0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x74, 0x24, 0x10, 0x57, 0x48, 0x83, 0xEC, 0x30, 0x48, 0x8B, 0xF1};
// StopSequence: mov [rsp+8],rbx; push rdi; sub rsp,30h; mov rdi,rcx; mov rbx,rdx
constexpr uint8_t kStopPro[16] = {0x48, 0x89, 0x5C, 0x24, 0x08, 0x57, 0x48, 0x83, 0xEC, 0x30, 0x48, 0x8B, 0xF9, 0x48, 0x8B, 0xDA};

std::atomic<uint8_t> g_role{0};
std::atomic<bool> g_hold{true}, g_ffStop{true}, g_timeGate{true};
std::atomic<uint32_t> g_holdMs{static_cast<uint32_t>(kHoldBoundMs)};
std::atomic<uint32_t> c_events{0}, c_own{0}, c_mod{0}, c_held{0}, c_released{0}, c_pulses{0}, c_directs{0}, c_starts{0}, c_ffStops{0},
                      c_timeOff{0}, c_timeBack{0}, c_seqSets{0}, c_seqStops{0}, c_seqSpeeds{0};   // c_seqSpeeds: WO-202 V.1
std::atomic<bool> g_armed{false};
const char* g_why = "not installed";

template <class T> bool rd(const void* base, ptrdiff_t off, T* out) {
    if (!base) return false;
    KCDMP_FAULT_READ(site, "wo194::read");
    if (!fault::enabled(site)) return false;
    const uintptr_t a = reinterpret_cast<uintptr_t>(base) + static_cast<uintptr_t>(off);
    if (!fault::plausible_address(a)) return false;
    return fault::guarded(site, [&] { std::memcpy(out, reinterpret_cast<const void*>(a), sizeof(T)); });
}

template <class T> bool wr(void* base, ptrdiff_t off, T v) {
    if (!base) return false;
    KCDMP_FAULT_CALL(site, "wo194::write");
    const uintptr_t a = reinterpret_cast<uintptr_t>(base) + static_cast<uintptr_t>(off);
    if (!fault::plausible_address(a)) return false;
    return fault::guarded(site, [&] { std::memcpy(reinterpret_cast<void*>(a), &v, sizeof(T)); });
}

bool is_a(const void* obj, void* const* vft) {
    void* vp = nullptr;
    return obj && vft && rd(obj, 0, &vp) && vp == static_cast<const void*>(vft);
}

void* vslot(const void* obj, size_t off) {
    void* vt = nullptr; void* fn = nullptr;
    return obj && rd(obj, 0, &vt) && vt && rd(vt, static_cast<ptrdiff_t>(off), &fn) ? fn : nullptr;
}

bool text_at(const void* p, std::string* out, int maxLen = 96) {
    if (!p) return false;
    std::string s;
    for (int i = 0; i < maxLen; ++i) {
        char ch = 0;
        if (!rd(p, i, &ch)) return false;
        if (!ch) break;
        if (ch < 0x20 || ch > 0x7E) return false;
        s.push_back(ch);
    }
    if (s.empty()) return false;
    *out = s;
    return true;
}

uint64_t now_ms() { return GetTickCount64(); }

template <class V, class P> void erase_where(V& v, P pred) { v.erase(std::remove_if(v.begin(), v.end(), pred), v.end()); }

// ------------------------------------------------------------------------------------------------ anchors

void* const* g_vftHandler = nullptr; void* const* g_vftCfg = nullptr; void* const* g_vftIngame = nullptr;
void* const* g_vftMovie = nullptr; void* const* g_vftSeq = nullptr;
void* const* g_vftNpc = nullptr; void* const* g_vftNpcMgr = nullptr;
void* g_enqueue = nullptr;
using EnqueueFn = void (*)(void* player, void* spRef);
EnqueueFn g_origEnqueue = nullptr;
bool g_movieChecked = false, g_movieOk = false, g_speedOk = false;   // g_speedOk: WO-202 V.1's two slots, checked on their own
int g_movieOff = -1;
const char* g_movieWhy = "not resolved";

void resolve_classes() {
    HMODULE gui = GetModuleHandleA("GUIModule.dll");
    HMODULE mv = GetModuleHandleA("CryMovie.dll");
    HMODULE xg = GetModuleHandleA("XGenAIModule.dll");
    if (gui) {
        g_vftHandler = anchor::find_vftable(gui, ".?AVC_CutsceneHandler@guimodule@wh@@", 0);
        g_vftCfg = anchor::find_vftable(gui, ".?AVC_CutsceneConfiguration@guimodule@wh@@", 0);
        g_vftIngame = anchor::find_vftable(gui, ".?AVC_IngameCutscene@guimodule@wh@@", 0);
        g_enqueue = reinterpret_cast<void*>(GetProcAddress(gui, kExpEnqueue));
    }
    if (mv) {
        g_vftMovie = anchor::find_vftable(mv, ".?AVCMovieSystem@@", 0);
        g_vftSeq = anchor::find_vftable(mv, ".?AVCAnimSequence@@", 0);
    }
    if (xg) {
        g_vftNpc = anchor::find_vftable(xg, ".?AVC_NPC@xgenaimodule@wh@@", 0);
        g_vftNpcMgr = anchor::find_vftable(xg, ".?AVC_NPCManager@xgenaimodule@wh@@", 0);
    }
}

// The movie system's three slots, each checked against the bytes WO-193 read (a different build is refused: the clock ops stay off).
bool movie_slots_ok() {
    if (g_movieChecked) return g_movieOk;
    g_movieChecked = true;
    if (!g_vftMovie || !g_vftSeq) { g_movieWhy = "RTTI CMovieSystem / CAnimSequence not unique"; logf("WO194-SEQ not armed: %s", g_movieWhy); return false; }
    auto same = [&](size_t slot, const uint8_t* pro, size_t n) {
        void* fn = nullptr;
        if (!rd(g_vftMovie, static_cast<ptrdiff_t>(slot), &fn) || !fn) return false;
        uint8_t b[32]{};
        for (size_t i = 0; i < n; ++i) if (!rd(fn, static_cast<ptrdiff_t>(i), &b[i])) return false;
        return std::memcmp(b, pro, n) == 0;
    };
    const bool g = same(kMovieGetTime, kGetTimePro, sizeof kGetTimePro), s = same(kMovieSetTime, kSetTimePro, sizeof kSetTimePro),
               t = same(kMovieStop, kStopPro, sizeof kStopPro);
    g_movieOk = g && s && t;
    // WO-202 V.1: the speed pair has the time pair's first bytes; its own test, so a build where it differs keeps the time ops
    const bool gs = same(kMovieGetSpeed, kGetTimePro, sizeof kGetTimePro), ss = same(kMovieSetSpeed, kSetTimePro, sizeof kSetTimePro);
    g_speedOk = g_movieOk && gs && ss;
    logf("WO194-SEQ speed slots: GetPlayingSpeed %s, SetPlayingSpeed %s -> %s", gs ? "ok" : "DIFFERS", ss ? "ok" : "DIFFERS",
         g_speedOk ? "armed (WO-202 cutscene_clock_v2 corrects small drift by speed)" : "NOT armed (the clock follows by seeks only)");
    g_movieWhy = g_movieOk ? "armed" : "a CMovieSystem slot's first bytes differ from the build WO-193 read";
    logf("WO194-SEQ resolver: %s (GetPlayingTime %s, SetPlayingTime %s, StopSequence %s)", g_movieWhy, g ? "ok" : "DIFFERS", s ? "ok" : "DIFFERS", t ? "ok" : "DIFFERS");
    return g_movieOk;
}

void* movie_system() {
    if (!movie_slots_ok()) return nullptr;
    void* ge = engine::genv();
    auto at = [&](int off) -> void* { void* p = nullptr; return rd(ge, off, &p) && p && is_a(p, g_vftMovie) ? p : nullptr; };
    if (g_movieOff >= 0) if (void* p = at(g_movieOff)) return p;
    for (int off = 0; off < 0x400; off += 8)
        if (void* p = at(off)) {
            if (g_movieOff != off) logf("WO194-SEQ the movie system is gEnv+0x%X (by its vftable)", off);
            g_movieOff = off;
            return p;
        }
    return nullptr;
}

// ------------------------------------------------------------------------------------------------ the event ring

struct Ev { uint32_t seq; uint8_t kind; uint64_t atMs; std::string name, path, detail; };
std::mutex g_mx;
std::deque<Ev> g_ring;   // the newest 32
uint32_t g_seq = 0;

void push(uint8_t kind, const std::string& name, const std::string& path, const std::string& detail) {
    std::lock_guard<std::mutex> lk(g_mx);
    g_ring.push_back(Ev{++g_seq, kind, now_ms(), name, path, detail});
    while (g_ring.size() > 32) g_ring.pop_front();
    c_events.fetch_add(1);
}

// ------------------------------------------------------------------------------------------------ holds, partner scenes, time gates

struct Held {
    void* cfg = nullptr; uint8_t autoPlay = 1;
    std::string name, path;
    uint64_t atMs = 0, doneAtMs = 0;
    Release asked = Release::None;
    bool done = false; int rechecks = 0;
};
struct Partner { std::string name; uint64_t atMs; };
struct TimeGate { void* cs = nullptr; void* cfg = nullptr; uint8_t orig = 1; std::string name; uint64_t atMs = 0; };

std::vector<Held> g_holds;          // under g_mx
std::vector<Partner> g_partner;     // under g_mx
std::vector<TimeGate> g_gates;      // under g_mx
std::string g_modPath;              // under g_mx: the node op 61 is pulsing
uint64_t g_modAtMs = 0;
uint32_t g_modSeen = 0;             // enqueues of g_modPath seen (op 61 checks it moved)
thread_local bool t_modPulse = false;

bool partner_playing_locked(const std::string& name, uint64_t now) {
    for (const auto& p : g_partner) if (p.name == name && now < p.atMs + kPartnerPlayingTtlMs) return true;
    return false;
}

// ------------------------------------------------------------------------------------------------ B1: the detour

void note_enqueue(void* player, void* spRef) {
    (void)player;
    void* cfg = nullptr;
    if (!rd(spRef, 0, &cfg) || !is_a(cfg, g_vftCfg)) return;
    void* cs = nullptr;
    if (!rd(cfg, kCfgCutscene, &cs) || !is_a(cs, g_vftIngame)) return;   // only in-game cutscenes are this WO's
    std::string name = "?";
    { void* np = nullptr; if (rd(cs, kCsName, &np)) text_at(np, &name, 80); }
    void* node = static_cast<char*>(spRef) - kHandlerCfg;
    const bool handler = is_a(node, g_vftHandler);
    char path[480] = "";
    if (handler && !wo137::path_of(node, path, sizeof path)) path[0] = 0;
    const uint64_t now = now_ms();
    uint8_t autoPlay = 0, hasTime = 0;
    rd(cfg, kCfgAutoPlay, &autoPlay);
    rd(cs, kCsHasTime, &hasTime);
    const uint8_t role = g_role.load();
    bool mod = t_modPulse, held = false, timeOff = false, partner = false;
    {
        std::lock_guard<std::mutex> lk(g_mx);
        if (!mod && path[0] && g_modPath == path && now < g_modAtMs + kModStartWindowMs) mod = true;
        if (path[0] && g_modPath == path) ++g_modSeen;
        partner = partner_playing_locked(name, now);
        if (handler && autoPlay && path[0] && hold_wanted(role, g_hold.load(), true, mod, partner) && wr<uint8_t>(cfg, kCfgAutoPlay, 0)) {
            Held h; h.cfg = cfg; h.autoPlay = autoPlay; h.name = name; h.path = path; h.atMs = now;
            g_holds.push_back(h);
            held = true;
        }
        if (time_gate_wanted(role, g_timeGate.load(), true, hasTime != 0) && wr<uint8_t>(cs, kCsHasTime, 0)) {
            TimeGate g; g.cs = cs; g.cfg = cfg; g.orig = hasTime; g.name = name; g.atMs = now;
            g_gates.push_back(g);
            timeOff = true;
        }
    }
    char det[160];
    std::snprintf(det, sizeof det, "autoplay=%u handler=%d partner_playing=%d own_time=%u tid=%lu", autoPlay, handler ? 1 : 0, partner ? 1 : 0, hasTime,
                  GetCurrentThreadId());
    const uint8_t kind = mod ? kEvMod : held ? kEvHeld : kEvOwn;
    (mod ? c_mod : c_own).fetch_add(1);
    if (held) c_held.fetch_add(1);
    push(kind, name, path, det);
    logf("WO194-ENQUEUE %s in-game cutscene '%s' handler=%s %s%s", mod ? "the mod's" : "this quest's", name.c_str(), path[0] ? path : "-", det,
         held ? " -- HELD at initialized (AutoPlay cleared; released by the host's copy, a save, the agent or 3 s)" : "");
    if (timeOff) {
        c_timeOff.fetch_add(1);
        push(kEvTimeOff, name, path, "the scene's own time of day is off here until its Play ran");
        logf("WO194-TIME '%s' sets its own time of day: off on the joiner (the host's clock gives it) until its Play ran", name.c_str());
    }
}

void enqueue_detour(void* player, void* spRef) {
    if (g_armed.load(std::memory_order_relaxed)) {
        KCDMP_FAULT_CALL(site, "wo194::note_enqueue");
        fault::guarded(site, [&] { note_enqueue(player, spRef); });
    }
    g_origEnqueue(player, spRef);
}

// ------------------------------------------------------------------------------------------------ B3: releases

struct NodeRead { void* cfg = nullptr; bool handler = false, holds = false; int state = -1; int rt[8]{}; int nrt = 0; };

bool read_node(void* node, void* ctx) {
    auto* r = static_cast<NodeRead*>(ctx);
    r->handler = is_a(node, g_vftHandler);
    void* p = nullptr;
    r->holds = r->handler && rd(node, kHandlerCfg, &p) && p == r->cfg;
    rd(node, kHandlerState, &r->state);
    r->nrt = wo137::runtime_chain(node, r->rt, 8);
    return true;
}

bool accept_initialized(void* node, void* ctx) {
    NodeRead r; r.cfg = ctx;
    read_node(node, &r);
    return r.holds && r.state == 2;
}

bool accept_idle(void* node, void* ctx) {
    auto* r = static_cast<NodeRead*>(ctx);
    read_node(node, r);
    return r->handler && r->state == 0;
}

void player_view(void* cfg, bool* active, bool* prog) {
    *active = false; *prog = false;
    void* pl = wo189::cutscene_player_obj();
    void* a = nullptr; uint8_t p = 0;
    if (pl && rd(pl, kPlayerActive, &a) && rd(pl, kPlayerInProgress, &p)) { *active = a && a == cfg; *prog = *active && p != 0; }
}

// Play an initialized copy: the handler's own PlayCutscene input (AutoPlay still off, as it requires), else the player's call.
std::string play_initialized(const Held& h) {
    const wo137::Applied a = wo137::pulse_node(h.path.c_str(), "PlayCutscene", &accept_initialized, h.cfg);
    bool active = false, prog = false;
    player_view(h.cfg, &active, &prog);
    if (a == wo137::Applied::Changed && prog) { c_pulses.fetch_add(1); return "its handler's PlayCutscene input"; }
    const std::string in = std::string(" (the input: ") + wo137::applied_name(a) + (a == wo137::Applied::Changed ? ", fired, not in progress after it)" : ")");
    const uint8_t d = wo189::play_active(h.cfg);
    player_view(h.cfg, &active, &prog);
    if (d == 1) { c_directs.fetch_add(1); return std::string(prog ? "C_CutscenePlayer::PlayCutscene" : "C_CutscenePlayer::PlayCutscene, NOT in progress after it") + in; }
    return "NEITHER the input nor the player's call" + in;
}

void release(Held& h, Release why) {
    h.done = true; h.doneAtMs = now_ms();
    NodeRead r; r.cfg = h.cfg;
    const bool found = wo137::visit_node(h.path.c_str(), &read_node, &r);
    bool active = false, prog = false;
    if (r.holds) player_view(h.cfg, &active, &prog);
    const Act act = release_act(found && r.holds, active, prog, r.state);
    std::string how = "-";
    switch (act) {
        case Act::Gone: how = "the handler no longer holds it (a load?) -- nothing written"; h.rechecks = 2; break;
        case Act::AlreadyPlaying: wr<uint8_t>(h.cfg, kCfgAutoPlay, h.autoPlay); how = "already playing; AutoPlay back"; h.rechecks = 2; break;
        case Act::Pulse: how = play_initialized(h); wr<uint8_t>(h.cfg, kCfgAutoPlay, h.autoPlay); h.rechecks = 1; break;
        case Act::RestoreOnly: wr<uint8_t>(h.cfg, kCfgAutoPlay, h.autoPlay); how = "AutoPlay back (still initializing: the game plays it itself)"; break;
    }
    c_released.fetch_add(1);
    char det[200];
    std::snprintf(det, sizeof det, "why=%s handler_state=%d active=%d in_progress=%d held_ms=%llu", release_name(why), r.state, active ? 1 : 0, prog ? 1 : 0,
                  static_cast<unsigned long long>(h.doneAtMs - h.atMs));
    push(kEvReleased, h.name, h.path, std::string(det) + " how=" + how);
    logf("WO194-HOLD released '%s': %s -- %s", h.name.c_str(), det, how.c_str());
}

void recheck(Held& h) {
    ++h.rechecks;
    NodeRead r; r.cfg = h.cfg;
    if (!wo137::visit_node(h.path.c_str(), &read_node, &r) || !r.holds) { h.rechecks = 2; return; }
    bool active = false, prog = false;
    player_view(h.cfg, &active, &prog);
    if (!recheck_stuck(active, prog)) return;
    const std::string how = r.state == 2 ? play_initialized(h) : (wo189::play_active(h.cfg) == 1 ? (c_directs.fetch_add(1), std::string("C_CutscenePlayer::PlayCutscene")) : std::string("the player's call refused"));
    char det[160];
    std::snprintf(det, sizeof det, "still initialized %llu ms after the release (handler_state=%d)", static_cast<unsigned long long>(now_ms() - h.doneAtMs), r.state);
    push(kEvRecheck, h.name, h.path, std::string(det) + " how=" + how);
    logf("WO194-HOLD re-check '%s': %s -- played: %s", h.name.c_str(), det, how.c_str());
}

// ------------------------------------------------------------------------------------------------ the active in-game sequence

struct Active { void* cs = nullptr; void* seq = nullptr; std::string name; };

bool active_ingame(const std::string& want, Active* out) {
    void* pl = wo189::cutscene_player_obj();
    void* cfg = nullptr;
    if (!pl || !rd(pl, kPlayerActive, &cfg) || !is_a(cfg, g_vftCfg)) return false;
    if (!rd(cfg, kCfgCutscene, &out->cs) || !is_a(out->cs, g_vftIngame)) return false;
    void* np = nullptr;
    if (!rd(out->cs, kCsName, &np) || !text_at(np, &out->name, 80)) return false;
    if (!want.empty() && out->name != want) return false;
    return rd(out->cs, kCsSequence, &out->seq) && is_a(out->seq, g_vftSeq);
}

void put_f(uint8_t* p, float v) { std::memcpy(p, &v, 4); }

} // namespace

// ------------------------------------------------------------------------------------------------ public

void install() {
    resolve_classes();
    if (!g_enqueue || !g_vftHandler || !g_vftCfg || !g_vftIngame) {
        g_why = !g_enqueue ? "EnqueueCutscene is not exported" : "an RTTI class (C_CutsceneHandler / C_CutsceneConfiguration / C_IngameCutscene) is not unique";
        logf("WO194-HOOK not armed: %s -- in-game cutscenes stay this machine's own", g_why);
        return;
    }
    const char* why = nullptr;
    void* orig = nullptr;
    if (!inlinehook::install_detour(g_enqueue, hookpro::kEnqueueCutscene, sizeof hookpro::kEnqueueCutscene, reinterpret_cast<void*>(&enqueue_detour), &orig, &why)) {
        g_why = "the patch was refused";
        logf("WO194-HOOK not armed: %s (%s)", g_why, why ? why : "?");
        return;
    }
    g_origEnqueue = reinterpret_cast<EnqueueFn>(orig);
    g_why = "armed";
    g_armed.store(true);
    char a[64];
    anchor::describe(g_enqueue, a, sizeof a);
    logf("WO194-HOOK armed: C_CutscenePlayer::EnqueueCutscene=%s (GUIModule's export, prologue %u bytes from the registry); in-game cutscenes "
         "by RTTI C_IngameCutscene, handlers by RTTI C_CutsceneHandler", a, static_cast<unsigned>(sizeof hookpro::kEnqueueCutscene));
}

void tick() {
    if (!g_armed.load()) return;
    const uint64_t now = now_ms();
    // The work is taken out of the shared lists first: a release pulses the quest graph, whose enqueue (this frame, this thread)
    // comes back through the detour and adds to them.
    std::vector<Held> holds;
    std::vector<TimeGate> gates;
    {
        std::lock_guard<std::mutex> lk(g_mx);
        holds.swap(g_holds);
        gates.swap(g_gates);
        erase_where(g_partner, [&](const Partner& p) { return now >= p.atMs + kPartnerPlayingTtlMs; });
    }
    if (holds.empty() && gates.empty()) return;
    bool anyOpen = false;
    for (const auto& h : holds) anyOpen |= !h.done;
    bool save = false;
    if (anyOpen) { const wo178::SaveState s = wo178::save_state(); save = s.armed && (s.pending || s.saving); }
    for (auto& h : holds) {
        if (!h.done) {
            const Release r = release_due(h.atMs, now, g_holdMs.load(), h.asked, save);
            if (r != Release::None) release(h, r);
        } else if (h.rechecks < 2 && now >= h.doneAtMs + kRecheckMs[h.rechecks]) recheck(h);
    }
    erase_where(holds, [](const Held& h) { return h.done && h.rechecks >= 2; });
    for (auto& g : gates) {
        bool active = false, prog = false;
        player_view(g.cfg, &active, &prog);
        const bool played = active && prog, late = now >= g.atMs + kTimeGateMaxMs;
        if (!played && !late) continue;
        if (is_a(g.cs, g_vftIngame) && wr<uint8_t>(g.cs, kCsHasTime, g.orig)) {
            c_timeBack.fetch_add(1);
            push(kEvTimeBack, g.name, "", played ? "its Play ran" : "3 min bound");
            logf("WO194-TIME '%s' own time of day restored (%s)", g.name.c_str(), played ? "its Play ran without it" : "the 3 min bound");
        }
        g.atMs = 0;
    }
    erase_where(gates, [](const TimeGate& g) { return g.atMs == 0; });
    std::lock_guard<std::mutex> lk(g_mx);
    holds.insert(holds.end(), g_holds.begin(), g_holds.end());   // the ones the detour added meanwhile
    g_holds.swap(holds);
    gates.insert(gates.end(), g_gates.begin(), g_gates.end());
    g_gates.swap(gates);
}

int status_text(char* out, int n) {
    size_t holds = 0, gates = 0, partner = 0;
    { std::lock_guard<std::mutex> lk(g_mx); holds = g_holds.size(); gates = g_gates.size(); partner = g_partner.size(); }
    return std::snprintf(out, n, "hook=%s role=%u hold=%d ff_stop=%d time_gate=%d hold_ms=%u own=%u mod=%u held=%u released=%u pulses=%u directs=%u starts=%u "
                         "ff_stops=%u time_off=%u time_back=%u seq_sets=%u seq_stops=%u seq_speeds=%u speed_slots=%d open_holds=%zu time_gates=%zu partner_scenes=%zu seq=%s",
                         g_why, g_role.load(), g_hold.load() ? 1 : 0, g_ffStop.load() ? 1 : 0, g_timeGate.load() ? 1 : 0, g_holdMs.load(), c_own.load(),
                         c_mod.load(), c_held.load(), c_released.load(), c_pulses.load(), c_directs.load(), c_starts.load(), c_ffStops.load(), c_timeOff.load(),
                         c_timeBack.load(), c_seqSets.load(), c_seqStops.load(), c_seqSpeeds.load(), g_speedOk ? 1 : 0, holds, gates, partner, g_movieWhy);
}

uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    auto str = [&](size_t at, std::string* s) -> size_t {   // [len:1][bytes] at `at`; returns the next offset (0 = malformed)
        if (at >= len || at + 1 + body[at] > len) return 0;
        s->assign(reinterpret_cast<const char*>(body + at + 1), body[at]);
        return at + 1 + body[at];
    };
    switch (op) {
        case kOpStatus: {
            const int k = status_text(reinterpret_cast<char*>(out), static_cast<int>(cap));
            *outLen = k <= 0 || cap == 0 ? 0 : (static_cast<size_t>(k) < cap ? static_cast<size_t>(k) : cap - 1);
            return wo163::kROk;
        }
        case kOpConfig: {
            if (len != 7 || cap < 6) return wo163::kRBadRequest;
            if (body[1] != 255 && body[1] != g_role.exchange(body[1])) logf("WO194-CFG role=%s", body[1] == 1 ? "host" : body[1] == 2 ? "joiner" : "unknown");
            if (body[2] != 255) g_hold.store(body[2] != 0);
            if (body[3] != 255) g_ffStop.store(body[3] != 0);
            if (body[4] != 255) g_timeGate.store(body[4] != 0);
            uint16_t ms = 0; std::memcpy(&ms, body + 5, 2);
            if (ms != 0xFFFF && ms >= 500) g_holdMs.store(ms);
            out[0] = g_role.load(); out[1] = g_hold.load() ? 1 : 0; out[2] = g_ffStop.load() ? 1 : 0; out[3] = g_timeGate.load() ? 1 : 0;
            const uint16_t hm = static_cast<uint16_t>(g_holdMs.load());
            std::memcpy(out + 4, &hm, 2);
            *outLen = 6;
            return wo163::kROk;
        }
        case kOpPoll: {
            if (len != 5 || cap < 5) return wo163::kRBadRequest;
            uint32_t since = 0; std::memcpy(&since, body + 1, 4);
            std::lock_guard<std::mutex> lk(g_mx);
            uint32_t last = g_seq;   // the pipe's reply holds 512 bytes: what does not fit comes on the next poll
            size_t at = 5; uint8_t n = 0;
            const uint64_t now = now_ms();
            for (const auto& e : g_ring) {
                if (e.seq <= since) continue;
                const size_t nl = e.name.size() < 80 ? e.name.size() : 80, pl = e.path.size() < 200 ? e.path.size() : 200,
                             dl = e.detail.size() < 160 ? e.detail.size() : 160;
                if (at + 9 + 3 + nl + pl + dl > cap) { last = e.seq - 1; break; }
                std::memcpy(out + at, &e.seq, 4); out[at + 4] = e.kind;
                const uint32_t age = static_cast<uint32_t>(now - e.atMs);
                std::memcpy(out + at + 5, &age, 4);
                at += 9;
                out[at++] = static_cast<uint8_t>(nl); std::memcpy(out + at, e.name.data(), nl); at += nl;
                out[at++] = static_cast<uint8_t>(pl); std::memcpy(out + at, e.path.data(), pl); at += pl;
                out[at++] = static_cast<uint8_t>(dl); std::memcpy(out + at, e.detail.data(), dl); at += dl;
                ++n;
            }
            std::memcpy(out, &last, 4);
            out[4] = n;
            *outLen = at;
            return wo163::kROk;
        }
        case kOpStart: {
            std::string path, name;
            size_t at = str(1, &path);
            const size_t at2 = at ? str(at, &name) : 0;
            if (!at2 || cap < 2) return wo163::kRBadRequest;
            const bool asOwn = len == at2 + 1 && body[at2] == 1;   // the harness only: the enqueue counts as this quest's own (the hold applies)
            *outLen = 2; out[1] = 255;
            if (!g_armed.load()) { out[0] = 5; return wo163::kROk; }
            uint32_t seen0 = 0;
            { std::lock_guard<std::mutex> lk(g_mx); g_modPath = asOwn ? std::string() : path; g_modAtMs = now_ms(); seen0 = g_modSeen; }
            NodeRead r;
            t_modPulse = !asOwn;
            const wo137::Applied a = wo137::pulse_node(path.c_str(), "EnqueueCutscene", &accept_idle, &r);
            t_modPulse = false;
            uint32_t seen1 = 0;
            { std::lock_guard<std::mutex> lk(g_mx); seen1 = g_modSeen; }
            if (asOwn) seen1 = seen0 + (a == wo137::Applied::Changed ? 1 : 0);   // not counted by path (the hook saw an own enqueue)
            out[1] = static_cast<uint8_t>(r.state < 0 ? 255 : (r.state > 254 ? 254 : r.state));
            using A = wo137::Applied;
            out[0] = a == A::Changed ? (seen1 != seen0 ? 1 : 6) : a == A::NoNode ? 2 : a == A::NotAState ? (r.handler ? 0 : 2) : a == A::PortRefused || a == A::NoPort ? 4
                   : a == A::Asleep ? 7 : a == A::Unarmed ? 5 : 3;
            if (out[0] == 1) c_starts.fetch_add(1);
            logf("WO194-START '%s' through its handler %s: %s (pulse: %s, handler state %d)", name.c_str(), path.c_str(),
                 out[0] == 1 ? "STARTED (its quest's own EnqueueCutscene input)" : out[0] == 0 ? "the handler is not idle -- this quest is not at that step"
                 : out[0] == 2 ? "no such CutsceneHandler" : out[0] == 6 ? "fired, but no enqueue followed" : out[0] == 7 ? "the node is hibernated -- not at that step"
                 : "refused", wo137::applied_name(a), r.state);
            return wo163::kROk;
        }
        case kOpHold: {
            if (len < 2 || cap < 1) return wo163::kRBadRequest;
            std::string name;
            if (body[1] == 1) {
                if (len < 3 || !str(3, &name)) return wo163::kRBadRequest;
                std::lock_guard<std::mutex> lk(g_mx);
                erase_where(g_partner, [&](const Partner& p) { return p.name == name; });
                if (body[2]) g_partner.push_back(Partner{name, now_ms()});
                out[0] = static_cast<uint8_t>(g_partner.size()); *outLen = 1;
                return wo163::kROk;
            }
            if (body[1] == 2) {
                if (len < 3 || !str(3, &name)) return wo163::kRBadRequest;
                const Release why = body[2] >= 1 && body[2] <= 4 ? static_cast<Release>(body[2]) : Release::Agent;
                std::lock_guard<std::mutex> lk(g_mx);
                uint8_t n = 0;
                for (auto& h : g_holds) if (!h.done && h.asked == Release::None && (name.empty() || h.name == name)) { h.asked = why; ++n; }
                out[0] = n; *outLen = 1;
                return wo163::kROk;   // released on this frame's tick (the same main thread)
            }
            if (body[1] == 3) {   // HandlerState [pathLen][path] -> [found:1][handler:1][state:1][n:1][runtime:1]*n (read only; the node, then its parents)
                std::string path;
                if (!str(2, &path) || cap < 12) return wo163::kRBadRequest;
                NodeRead r;
                const bool found = wo137::visit_node(path.c_str(), &read_node, &r);
                out[0] = found ? 1 : 0; out[1] = r.handler ? 1 : 0; out[2] = static_cast<uint8_t>(r.state < 0 ? 255 : (r.state > 254 ? 254 : r.state));
                out[3] = static_cast<uint8_t>(r.nrt);
                for (int i = 0; i < r.nrt; ++i) out[4 + i] = static_cast<uint8_t>(r.rt[i] < 0 ? 255 : r.rt[i]);
                *outLen = 4 + static_cast<size_t>(r.nrt);
                return wo163::kROk;
            }
            return wo163::kRBadRequest;
        }
        case kOpSeq: {
            if (len < 3) return wo163::kRBadRequest;
            const uint8_t sub = body[1];
            std::string name;
            const size_t at = str(2, &name);
            if (!at) return wo163::kRBadRequest;
            void* mv = movie_system();
            Active a;
            const bool have = mv && active_ingame(name, &a);
            auto res = [&](uint8_t r) { out[0] = r; };
            if (sub == 1) {
                if (cap < 5) return wo163::kRBadRequest;
                *outLen = 5; res(!mv ? 2 : have ? 1 : 0); put_f(out + 1, -1.0f);
                if (!have) return wo163::kROk;
                float t = -1.0f;
                KCDMP_FAULT_CALL(site, "wo194::GetPlayingTime");
                void* fn = vslot(mv, kMovieGetTime);
                if (!fn || !fault::guarded(site, [&] { t = reinterpret_cast<float (*)(void*, void*)>(fn)(mv, a.seq); })) { res(3); return wo163::kROk; }
                if (!std::isfinite(t) || t < 0) res(0);
                put_f(out + 1, t);
                return wo163::kROk;
            }
            if (sub == 2) {
                if (len != at + 4 || cap < 9) return wo163::kRBadRequest;
                float want = 0; std::memcpy(&want, body + at, 4);
                *outLen = 9; res(!mv ? 2 : have ? 1 : 0); put_f(out + 1, -1.0f); put_f(out + 5, -1.0f);
                if (!have) return wo163::kROk;
                if (!std::isfinite(want) || want < 0 || want > 3600) { res(4); return wo163::kROk; }
                void* fg = vslot(mv, kMovieGetTime); void* fs = vslot(mv, kMovieSetTime);
                float before = -1, after = -1; bool ok = false;
                KCDMP_FAULT_CALL(site, "wo194::SetPlayingTime");
                if (!fg || !fs || !fault::guarded(site, [&] {
                        before = reinterpret_cast<float (*)(void*, void*)>(fg)(mv, a.seq);
                        ok = reinterpret_cast<bool (*)(void*, void*, float)>(fs)(mv, a.seq, want);
                        after = reinterpret_cast<float (*)(void*, void*)>(fg)(mv, a.seq);
                    })) { res(3); return wo163::kROk; }
                put_f(out + 1, before); put_f(out + 5, after);
                if (!ok) { res(4); return wo163::kROk; }
                c_seqSets.fetch_add(1);
                logf("WO194-SEQ '%s' time %.3f -> %.3f s (SetPlayingTime, asked %.3f) [n=%u]", a.name.c_str(), before, after, want, c_seqSets.load());
                return wo163::kROk;
            }
            if (sub == 4) {   // WO-202 V.1: [speed:4] -> [res][before:4][after:4]; res 5 = the speed slots are not armed on this build
                if (len != at + 4 || cap < 9) return wo163::kRBadRequest;
                float want = 0; std::memcpy(&want, body + at, 4);
                *outLen = 9; res(!mv ? 2 : have ? 1 : 0); put_f(out + 1, -1.0f); put_f(out + 5, -1.0f);
                if (!have) return wo163::kROk;
                if (!g_speedOk) { res(5); return wo163::kROk; }
                if (!std::isfinite(want) || want < 0.5f || want > 1.5f) { res(4); return wo163::kROk; }
                void* fg = vslot(mv, kMovieGetSpeed); void* fs = vslot(mv, kMovieSetSpeed);
                float before = -1, after = -1; bool ok = false;
                KCDMP_FAULT_CALL(site, "wo194::SetPlayingSpeed");
                if (!fg || !fs || !fault::guarded(site, [&] {
                        before = reinterpret_cast<float (*)(void*, void*)>(fg)(mv, a.seq);
                        ok = reinterpret_cast<bool (*)(void*, void*, float)>(fs)(mv, a.seq, want);
                        after = reinterpret_cast<float (*)(void*, void*)>(fg)(mv, a.seq);
                    })) { res(3); return wo163::kROk; }
                put_f(out + 1, before); put_f(out + 5, after);
                if (!ok) { res(4); return wo163::kROk; }
                c_seqSpeeds.fetch_add(1);
                return wo163::kROk;
            }
            if (sub == 3) {
                *outLen = 1; res(!mv ? 2 : have ? 1 : 0);
                if (!have) return wo163::kROk;
                void* fn = vslot(mv, kMovieStop);
                bool ok = false;
                KCDMP_FAULT_CALL(site, "wo194::StopSequence");
                if (!fn || !fault::guarded(site, [&] { ok = reinterpret_cast<bool (*)(void*, void*)>(fn)(mv, a.seq); })) { res(3); return wo163::kROk; }
                if (!ok) { res(4); return wo163::kROk; }
                c_seqStops.fetch_add(1);
                logf("WO194-SEQ '%s' stopped (StopSequence: the normal end follows) [n=%u]", a.name.c_str(), c_seqStops.load());
                return wo163::kROk;
            }
            return wo163::kRBadRequest;
        }
        case kOpStopFF: {
            if (len < 3 || cap < 14) return wo163::kRBadRequest;
            uint16_t n = 0; std::memcpy(&n, body + 1, 2);
            const bool all = n == 0xFFFF;   // every NPC of the world (the gate counts a FastForward anywhere): the copies the agent knows are not all
            if (!all && len != 3u + 4u * n) return wo163::kRBadRequest;
            if (all && len != 3) return wo163::kRBadRequest;
            void* mgr = wo141::x::npc_manager();
            const bool mgrOk = is_a(mgr, g_vftNpcMgr);
            int32_t before = -1, after = -1;
            if (mgrOk) rd(mgr, kMgrFFCount, &before);
            uint16_t stopped = 0, inFF = 0, noNpc = 0;
            void* get = mgrOk ? vslot(mgr, kMgrNpcOf) : nullptr;
            auto one = [&](uint32_t eid, void* ent) {
                if (!ff_stop_allowed(g_role.load(), g_ffStop.load(), eid, kPlayerEntity)) return;
                void* npc = nullptr;
                KCDMP_FAULT_CALL(gsite, "wo194::npc_of");
                if (!fault::guarded(gsite, [&] { npc = reinterpret_cast<void* (*)(void*, const uint32_t*)>(get)(mgr, &eid); }) || !is_a(npc, g_vftNpc)) { ++noNpc; return; }
                void* isFF = vslot(npc, kNpcInFF); void* stop = vslot(npc, kNpcStopFF);
                bool in = false;
                KCDMP_FAULT_CALL(fsite, "wo194::IsInFastForward");
                if (!isFF || !fault::guarded(fsite, [&] { in = reinterpret_cast<bool (*)(void*, uint64_t)>(isFF)(npc, 0); }) || !in) return;
                const char* nm = ent ? engine::entity_name(ent) : nullptr;
                if (!nm) if (void* e = engine::entity_by_id(eid)) nm = engine::entity_name(e);
                if (nm && std::strncmp(nm, "kcd2mp", 6) == 0) return;   // the mod's own figures
                ++inFF;
                KCDMP_FAULT_CALL(ssite, "wo194::StopFastForward");
                if (!stop || !fault::guarded(ssite, [&] { reinterpret_cast<void (*)(void*, uint64_t)>(stop)(npc, 0); })) return;
                ++stopped;
                c_ffStops.fetch_add(1);
                logf("WO194-FF stopped copy=%s eid=0x%X -- it was in a FastForward (C_NPC's own StopFastForward(0)) [n=%u]", nm ? nm : "?", eid, c_ffStops.load());
            };
            if (!get) {
            } else if (!all) {
                for (uint16_t i = 0; i < n; ++i) { uint32_t eid = 0; std::memcpy(&eid, body + 3 + 4 * i, 4); one(eid, nullptr); }
            } else if (before != 0) {   // nothing in a FastForward anywhere: no walk at all
                struct Ctx { decltype(one)* f; void* mgr; int32_t left; } c{&one, mgr, before};
                engine::for_each_entity([](void* e, void* x) -> bool {
                    auto* c = static_cast<Ctx*>(x);
                    (*c->f)(engine::entity_id(e), e);
                    int32_t now = -1;
                    return rd(c->mgr, kMgrFFCount, &now) && now == 0;   // the gate is open: stop walking
                }, &c);
            }
            if (mgrOk) rd(mgr, kMgrFFCount, &after);
            if (stopped) logf("WO194-FF the NPC manager's FastForward count %d -> %d (the scene positioning runs at 0)", before, after);
            std::memcpy(out, &stopped, 2); std::memcpy(out + 2, &inFF, 2); std::memcpy(out + 4, &noNpc, 2);
            std::memcpy(out + 6, &before, 4); std::memcpy(out + 10, &after, 4);
            *outLen = 14;
            return wo163::kROk;
        }
        default:
            return wo163::kRBadRequest;
    }
}

} // namespace kcdmp::wo194
