// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-216: the rest of the body -- the native half's switch word, status and B's census (wo216.h). A's read and replay live beside the
// route they extend (wo215.cpp: the AI update's entry, the per-copy route).
#include "wo216.h"
#include "wo203.h"
#include "wo163.h"
#include "wo215.h"
#include "wo208.h"
#include "anchors.h"
#include "engine.h"
#include "fault_guard.h"
#include "log.h"
#include "npc_drive.h"
#include "npc_scan.h"
#include "respawn_actions.h"
#include "rttr_abi.h"
#include "hits.h"
#include "wo138.h"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace kcdmp::wo216 {

namespace {

using namespace wo216rules;

std::atomic<uint32_t> g_bits{kBitsDefault};
std::atomic<bool> g_configured{false};

uint32_t twin_off() {
    static uint32_t s = 0xFFFFFFFFu;
    if (s == 0xFFFFFFFFu) {
        char b[256] = {};
        const DWORD n = GetEnvironmentVariableA("KCDMP_TEST_W216_OFF", b, sizeof b);
        s = (n > 0 && n < sizeof b) ? twin_off_bits(b) : 0;
        if (s) logf("WO216-CFG the bench's twin: bits 0x%X off for this game (KCDMP_TEST_W216_OFF=%s)", s, b);
    }
    return s;
}

double now_s() {
    static LARGE_INTEGER f{};
    if (!f.QuadPart) QueryPerformanceFrequency(&f);
    LARGE_INTEGER c;
    QueryPerformanceCounter(&c);
    return static_cast<double>(c.QuadPart) / static_cast<double>(f.QuadPart);
}

std::atomic<uint8_t> g_role{0};   // op 160's role byte: 0 none, 1 the host, 2 a joiner in the host's world (not during a load)
void b_exempt(const char* name, bool on, double forS);
int b_status(char* out, int n);

uint8_t d_read(uint8_t maxN, uint8_t* out, size_t cap, size_t* outLen);
uint8_t d_of(const char* name, uint8_t* out, size_t cap, size_t* outLen);

uint8_t op(uint8_t opc, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (opc == kOpOutfitRead) { if (len != 2) return wo163::kRBadRequest; return d_read(body[1], out, cap, outLen); }
    if (opc == kOpOutfitOf) {
        if (len < 3 || len != 2u + body[1] || body[1] == 0 || body[1] > 63) return wo163::kRBadRequest;
        char nm[64]{};
        std::memcpy(nm, body + 2, body[1]);
        return d_of(nm, out, cap, outLen);
    }
    if (opc == kOpExempt) {   // [on:1][secs:2][nameLen:1][name]
        if (len < 5 || len != 5u + body[4] || body[4] == 0 || body[4] > 63) return wo163::kRBadRequest;
        char nm[64]{};
        std::memcpy(nm, body + 5, body[4]);
        uint16_t secs = 0;
        std::memcpy(&secs, body + 2, 2);
        b_exempt(nm, body[1] != 0, secs ? secs : 60.0);
        return wo163::kROk;
    }
    if (opc == kOpBrains) {
        char t[1200];
        int n = b_status(t, sizeof t);
        if (n < 0) n = 0;
        if (static_cast<size_t>(n) > cap) n = static_cast<int>(cap);
        std::memcpy(out, t, static_cast<size_t>(n));
        *outLen = static_cast<size_t>(n);
        return wo163::kROk;
    }
    if (opc == kOpConfig) {
        if ((len != 5 && len != 6) || cap < 4) return wo163::kRBadRequest;
        uint32_t bits = 0;
        std::memcpy(&bits, body + 1, 4);
        if (len == 6) {
            const uint8_t was = g_role.exchange(body[5]);
            if (was != body[5]) logf("WO216-CFG role %u -> %u (0 none, 1 the host, 2 a joiner in the host's world)", was, body[5]);
        }
        const uint32_t was = g_bits.exchange(bits);
        if (was != bits || !g_configured.exchange(true))
            logf("WO216-CFG bits=0x%X (was 0x%X; twin off 0x%X): route_replay=%d no_brains=%d activity_line=%d outfits=%d riders=%d poses=%d npc_fights=%d",
                 bits, was, twin_off(), on(kBitRouteReplay), on(kBitNoBrains), on(kBitActivityLine), on(kBitOutfits), on(kBitRiders), on(kBitPoses),
                 on(kBitNpcFights));
        const uint32_t eff = bits & ~twin_off();
        std::memcpy(out, &eff, 4);
        *outLen = 4;
        return wo163::kROk;
    }
    if (opc == kOpStatus) {
        char t[2400];
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

// ================================================================================================== B: no brain runs on the joiner
// Every NPC body here is checked once a second (the census): one with a running brain is suspended at once -- the engine's own
// wh_ai_PauseNPC (C_IntelligentObject::Suspend), through the console the way Lua's System.ExecuteCommand calls it -- and, unless the
// host streams it (the writer binds it), hidden (CEntity::Hide, the call Lua's entity:Hide makes). A body the writer binds is shown again.
// A body the engine spawns here on its own (an encounter, an animal, a caravan) is caught at its spawn (the entity system's SpawnEntity,
// its interface slot wrapped) and hidden in the same frame, suspended as soon as it has a brain (WO216-LOCALSPAWN). The partner's figure
// and the mod's own bodies (kcd2mp_*, kcdmp*) are never touched; a body the mod's own features run on purpose here (a guard's stop, a talk,
// the horse this player rides) is exempt while Lua says so (op 165), and counted apart.
// Neither engine call is wrapped by the fault guard: both may take an engine lock (a guarded fault inside one is a hang -- WO-215).
struct Api {
    void* conExec = nullptr;       // IConsole::ExecuteString's slot offset is resolved below; this is the slot's offset as a pointer-sized int
    size_t conExecSlot = 0;
    size_t hideSlot = 0;           // CEntity::Hide(bool)
    bool armed = false;
    const char* why = "not resolved";
    bool resolved = false;
} g_b;

void b_resolve() {
    if (g_b.resolved) return;
    g_b.resolved = true;
    // the console's ExecuteString: CScriptBind_System::ExecuteCommand (CryScriptSystem, found by its own error text) calls
    // mov rcx,[rdi+78h]; xor r9d,r9d; mov rdx,[rsp+48h]; xor r8d,r8d; mov rax,[rcx]; call [rax+<slot>]   (bSilent = 0, bDefer = 0)
    HMODULE ss = GetModuleHandleA("CryScriptSystem.dll");
    if (!ss) { g_b.why = "CryScriptSystem.dll not loaded"; return; }
    int nf = 0;
    const uint8_t* door = anchor::function_by_string(ss, "CScriptBind_System::ExecuteCommand", &nf);
    if (!door || nf != 1) { g_b.why = "System.ExecuteCommand's binding not found as one function"; return; }
    uint8_t code[0x100]{};
    KCDMP_FAULT_READ(rsite, "wo216::b_resolve");
    if (!fault::guarded(rsite, [&] { std::memcpy(code, door, sizeof code); })) { g_b.why = "the binding unreadable"; return; }
    static const uint8_t pat[] = {0x45, 0x33, 0xC9, 0x48, 0x8B, 0x54, 0x24, 0x48, 0x45, 0x33, 0xC0, 0x48, 0x8B, 0x01, 0xFF, 0x90};
    size_t at = 0;
    for (size_t i = 0; i + sizeof pat + 4 <= sizeof code && !at; ++i) if (std::memcmp(code + i, pat, sizeof pat) == 0) at = i + sizeof pat;
    if (!at) { g_b.why = "the binding's console call not found"; return; }
    int32_t slot = 0;
    std::memcpy(&slot, code + at, 4);
    if (slot <= 0 || slot > 0x400 || (slot & 7)) { g_b.why = "the console call's slot is implausible"; return; }
    g_b.conExecSlot = static_cast<size_t>(slot);
    // CEntity::Hide(bool) at slot 63: its own first act sets the entity's hidden bit (flags +8, bit 4):
    // mov ecx,[rsi+8]; mov r8d,r14d; shl r8d,4; mov eax,ecx; and eax,0FFFFFFEFh
    void* pe = engine::entity_by_id(0x7777);   // the local player's entity (any CEntity carries the vftable)
    void* vt = nullptr; void* fn = nullptr;
    KCDMP_FAULT_READ(vsite, "wo216::b_resolve vft");
    const size_t kHide = 63 * 8;
    if (!pe || !fault::guarded(vsite, [&] { vt = *reinterpret_cast<void**>(pe); fn = reinterpret_cast<void**>(vt)[63]; }) || !fn) { g_b.why = "no entity to read CEntity's vftable from"; g_b.resolved = false; return; }
    uint8_t hb[0x60]{};
    if (!fault::guarded(vsite, [&] { std::memcpy(hb, fn, sizeof hb); })) { g_b.why = "CEntity::Hide unreadable"; return; }
    static const uint8_t vHide[] = {0x8B, 0x4E, 0x08, 0x45, 0x8B, 0xC6, 0x41, 0xC1, 0xE0, 0x04, 0x8B, 0xC1, 0x83, 0xE0, 0xEF};
    bool hideOk = false;
    for (size_t i = 0; i + sizeof vHide <= sizeof hb && !hideOk; ++i) hideOk = std::memcmp(hb + i, vHide, sizeof vHide) == 0;
    if (!hideOk) { g_b.why = "CEntity's slot 63 does not set the hidden bit (not Hide on this build)"; return; }
    g_b.hideSlot = kHide;
    g_b.armed = true;
    g_b.why = "armed";
    char a[96];
    anchor::describe(door, a, sizeof a);
    logf("WO216-BRAINS armed: the console's ExecuteString is its slot +0x%X (System.ExecuteCommand's own call, %s); CEntity::Hide is slot 63 "
         "(its hidden bit checked)", slot, a);
}

// the engine's own calls, unguarded (see above); inputs: a console with its vftable checked, a live entity, our own text
bool b_exec(const char* cmd) {
    if (!g_b.armed) return false;
    void* con = engine::console();
    if (!con) return false;
    void* vt = *reinterpret_cast<void**>(con);
    auto fn = reinterpret_cast<void (*)(void*, const char*, bool, bool)>(reinterpret_cast<void**>(vt)[g_b.conExecSlot / 8]);
    fn(con, cmd, false, false);
    return true;
}
bool b_hide(uint32_t eid, bool hide) {
    if (!g_b.armed) return false;
    void* e = engine::entity_by_id(eid);
    if (!e) return false;
    void* vt = *reinterpret_cast<void**>(e);
    auto fn = reinterpret_cast<void (*)(void*, bool)>(reinterpret_cast<void**>(vt)[g_b.hideSlot / 8]);
    fn(e, hide);
    return true;
}

bool ours(const char* name) {
    return !name || !*name || std::strncmp(name, "kcd2mp", 6) == 0 || std::strncmp(name, "kcdmp", 5) == 0;
}

struct BBody {
    std::string name;
    bool hidden = false;       // hidden by B (shown again when bound or when B goes off)
    bool pausedByUs = false;
    double pauseAt = -1e9;     // the last pause we asked for (a body that runs again within 5 s is asked again at most every 2 s)
    double spawnAt = -1;       // a local spawn: when it was seen (its brain may come a frame or two later)
    int kind = 0;
};
std::unordered_map<uint32_t, BBody> g_bodies;           // main thread
std::unordered_map<std::string, double> g_exempt;       // name -> until (s)
std::vector<uint32_t> g_pauseQ, g_hideQ, g_showQ;       // main thread: the actions, at most kPerFrame a frame
constexpr int kPerFrame = 24;
std::mutex g_spawnMu;
std::vector<uint32_t> g_spawned;                        // the spawn watch's new entities (any thread -> main)
std::atomic<bool> g_spawnWatch{false};
std::atomic<void*> g_origSpawn{nullptr};
bool g_spawnHooked = false;
const char* g_spawnWhy = "not installed";
bool g_active = false;
bool g_act = false;                                     // no_brains on: the census acts (off: the twin -- it only counts)
double g_censusAt = 0, g_activeSince = 0;
bool g_swept = false;                                   // the first census after the join: every body here suspended (the join's sweep)
struct Census { uint32_t samples = 0, nonzero = 0, maxRunning = 0, lastRunning = 0, lastExemptRunning = 0, lastBodies = 0, lastHidden = 0, lastBound = 0;
                uint32_t nearNonzero = 0, maxNearRunning = 0, lastNearRunning = 0, lastNear = 0, lastDead = 0;
                double lastCostMs = 0, maxCostMs = 0; } g_cen;
std::atomic<uint32_t> c_bPauses{0}, c_bHides{0}, c_bShows{0}, c_bLocalSpawns{0}, c_bBreaches{0}, c_bSweepPaused{0}, c_bSweepHidden{0},
    c_bExemptRunning{0}, c_bSpawnSeen{0};
std::string g_lastNames;                                // the newest non-zero sample's bodies (the briefing names them)

void* spawn_detour(void* es, void* params, bool autoInit) {
    auto fn = reinterpret_cast<void* (*)(void*, void*, bool)>(g_origSpawn.load(std::memory_order_relaxed));
    void* e = fn(es, params, autoInit);   // the engine's own spawn first, outside any guard of ours
    if (e && g_spawnWatch.load(std::memory_order_relaxed)) {
        const uint32_t id = engine::entity_id(e);   // a guarded getter on the new entity
        if (id) {
            c_bSpawnSeen.fetch_add(1, std::memory_order_relaxed);
            std::lock_guard<std::mutex> l(g_spawnMu);
            if (g_spawned.size() < 4096) g_spawned.push_back(id);
        }
    }
    return e;
}

// The entity system's interface slot SpawnEntity (+0x60, the one engine.cpp spawns with) is wrapped: the vftable's pointer swapped once
// (one aligned store; never put back -- the wrapper checks the switch). Main thread.
void b_hook_spawn() {
    if (g_spawnHooked) return;
    g_spawnHooked = true;
    void* es = engine::entity_system();
    if (!es) { g_spawnWhy = "no entity system"; g_spawnHooked = false; return; }
    void** vt = *reinterpret_cast<void***>(es);
    void** slot = vt + 0x60 / 8;
    void* orig = *slot;
    HMODULE m = nullptr;
    if (!GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, static_cast<LPCSTR>(orig), &m) ||
        m != GetModuleHandleA("CryEntitySystem.dll")) { g_spawnWhy = "SpawnEntity's slot does not point into CryEntitySystem"; return; }
    g_origSpawn.store(orig);
    DWORD old = 0;
    if (!VirtualProtect(slot, sizeof(void*), PAGE_READWRITE, &old)) { g_spawnWhy = "the vftable could not be made writable"; return; }
    InterlockedExchangePointer(reinterpret_cast<PVOID volatile*>(slot), reinterpret_cast<PVOID>(&spawn_detour));
    DWORD tmp = 0;
    VirtualProtect(slot, sizeof(void*), old, &tmp);
    g_spawnWhy = "armed";
    logf("WO216-BRAINS the spawn watch is in: the entity system's SpawnEntity (+0x60) wrapped -- a body this engine spawns on its own is "
         "hidden in the frame it appears and suspended once it has a brain");
}

bool exempt(const std::string& name, double now) {
    auto it = g_exempt.find(name);
    if (it == g_exempt.end()) return false;
    if (now > it->second) { g_exempt.erase(it); return false; }
    return true;
}

void b_queue_pause(uint32_t eid, BBody& b, double now) {
    if (now - b.pauseAt < 2.0) return;
    b.pauseAt = now;
    g_pauseQ.push_back(eid);
}

struct CensusCtx { double now; uint32_t bodies = 0, running = 0, exemptRunning = 0, hidden = 0, bound = 0, nearRunning = 0, nearN = 0, deadRunning = 0; std::string names;
                   std::unordered_set<uint32_t> seen; bool havePlayer = false; float player[3]{}; };
bool census_visit(void* e, void* ctx) {
    auto* c = static_cast<CensusCtx*>(ctx);
    const int kind = npcscan::body_kind(e);
    if (kind < 0) return false;
    const char* nm = engine::entity_name(e);
    if (ours(nm)) return false;
    const uint32_t eid = engine::entity_id(e);
    if (!eid) return false;
    uint64_t w = 0; int st = -1, mask = -1;
    if (!actions::brain_state(e, &w, &st, &mask) || st < 0) return false;   // no brain (a corpse, a body not set up yet): nothing to freeze
    ++c->bodies;
    c->seen.insert(eid);
    BBody& b = g_bodies[eid];
    if (b.name.empty()) { b.name = nm; b.kind = kind; }
    const bool bound = npcdrive::is_bound_eid(eid);
    const bool ex = exempt(b.name, c->now);
    if (bound) ++c->bound;
    bool nearMe = false;   // within 150 m of this player (the bodies a player here could see)
    if (c->havePlayer) {
        float q[3];
        if (engine::entity_world_pos(e, q)) {
            const float dx = q[0] - c->player[0], dy = q[1] - c->player[1];
            nearMe = dx * dx + dy * dy <= 150.0f * 150.0f;
        }
    }
    if (nearMe) ++c->nearN;
    if (wo216rules::breach(st)) {
        bool dead = false;
        void* soul = hits::soul_of_eid(eid);
        if (soul && rttr::soul_bool(soul, "IsDead", &dead) && dead) { ++c->deadRunning; return false; }   // a corpse: nothing to freeze
        if (ex) ++c->exemptRunning;
        else {
            ++c->running;
            if (nearMe) ++c->nearRunning;
            if (c->names.size() < 600) { if (!c->names.empty()) c->names += ","; c->names += b.name + (bound ? "(bound)" : "(unbound)"); }
            if (g_act) b_queue_pause(eid, b, c->now);
        }
    }
    if (g_act && !ex && wo216rules::hide_unbound(bound, false) && wo216rules::streamable_name(b.name.size()) && !b.hidden) { b.hidden = true; g_hideQ.push_back(eid); }
    if (bound && b.hidden) { b.hidden = false; g_showQ.push_back(eid); }
    if (b.hidden) ++c->hidden;
    return false;
}

std::vector<uint32_t> g_npcList;   // every NPC body with a brain here (the full scan's; local spawns added as they come)
double g_listAt = -1e9;
bool list_visit(void* e, void* ctx) {
    if (npcscan::body_kind(e) < 0) return false;
    const char* nm = engine::entity_name(e);
    if (ours(nm)) return false;
    const uint32_t eid = engine::entity_id(e);
    if (eid) static_cast<std::vector<uint32_t>*>(ctx)->push_back(eid);
    return false;
}

void b_census(double now) {
    CensusCtx c;
    c.now = now;
    if (void* pe = engine::entity_by_id(0x7777)) c.havePlayer = engine::entity_world_pos(pe, c.player);
    const double t0 = now_s();
    // the full entity scan (~9 ms on the bench) at the join's sweep and then every 30 s as a net; between, the spawn watch adds every
    // body this engine spawns and a body gone drops out by itself (its id no longer answers)
    if (now - g_listAt >= 30.0 || !g_swept) {
        g_listAt = now;
        std::vector<uint32_t> v;
        engine::for_each_entity(&list_visit, &v);
        g_npcList.swap(v);
    }
    for (uint32_t eid : g_npcList) if (void* e = engine::entity_by_id(eid)) census_visit(e, &c);
    const double ms = (now_s() - t0) * 1000.0;
    // bodies gone since the last census: forgotten
    for (auto it = g_bodies.begin(); it != g_bodies.end();) { if (!c.seen.count(it->first)) it = g_bodies.erase(it); else ++it; }
    if (!g_swept) {
        g_swept = true;
        c_bSweepPaused.store(static_cast<uint32_t>(g_pauseQ.size()));
        c_bSweepHidden.store(static_cast<uint32_t>(g_hideQ.size()));
        logf("WO216-BRAINS the join's sweep: %u bodies with a brain here, %u running (%s), %u not streamed by the host (%s), %u bound, census %.1f ms",
             c.bodies, c.running, g_act ? "suspended now" : "left running: no_brains off", static_cast<uint32_t>(g_hideQ.size()),
             g_act ? "hidden" : "left shown", c.bound, ms);
        return;   // the contract's samples start after the sweep
    }
    ++g_cen.samples;
    g_cen.lastRunning = c.running; g_cen.lastExemptRunning = c.exemptRunning; g_cen.lastBodies = c.bodies; g_cen.lastHidden = c.hidden; g_cen.lastBound = c.bound;
    g_cen.lastCostMs = ms;
    if (ms > g_cen.maxCostMs) g_cen.maxCostMs = ms;
    g_cen.lastNearRunning = c.nearRunning; g_cen.lastNear = c.nearN; g_cen.lastDead = c.deadRunning;
    if (c.nearRunning) { ++g_cen.nearNonzero; if (c.nearRunning > g_cen.maxNearRunning) g_cen.maxNearRunning = c.nearRunning; }
    c_bExemptRunning.fetch_add(c.exemptRunning, std::memory_order_relaxed);
    if (c.running) {
        ++g_cen.nonzero;
        c_bBreaches.fetch_add(c.running, std::memory_order_relaxed);
        if (c.running > g_cen.maxRunning) g_cen.maxRunning = c.running;
        g_lastNames = c.names;
        if (g_cen.nonzero <= 200 || g_cen.nonzero % 50 == 0)
            logf("WO216-BRAINS sample=%u running=%u (not exempt; within 150 m %u of %u) exempt_running=%u bodies=%u hidden=%u bound=%u: %s -- %s [nonzero n=%u]",
                 g_cen.samples, c.running, c.nearRunning, c.nearN, c.exemptRunning, c.bodies, c.hidden, c.bound, c.names.c_str(),
                 g_act ? "suspended again" : "left running (no_brains off: the twin)", g_cen.nonzero);
    }
}

void b_spawns(double now) {
    std::vector<uint32_t> in;
    {
        std::lock_guard<std::mutex> l(g_spawnMu);
        in.swap(g_spawned);
    }
    for (uint32_t eid : in) {
        void* e = engine::entity_by_id(eid);
        if (!e) continue;
        const int kind = npcscan::body_kind(e);
        if (kind < 0) continue;
        const char* nm = engine::entity_name(e);
        if (ours(nm)) continue;
        BBody& b = g_bodies[eid];
        b.name = nm ? nm : "?";
        b.kind = kind;
        b.spawnAt = now;
        if (std::find(g_npcList.begin(), g_npcList.end(), eid) == g_npcList.end()) g_npcList.push_back(eid);
        const uint32_t k = c_bLocalSpawns.fetch_add(1, std::memory_order_relaxed) + 1;
        const bool bound = npcdrive::is_bound_eid(eid);
        if (g_act && !bound && wo216rules::streamable_name(b.name.size()) && !b.hidden) { b.hidden = true; b_hide(eid, true); c_bHides.fetch_add(1, std::memory_order_relaxed); }   // this frame
        if (k <= 200 || k % 50 == 0)
            logf("WO216-LOCALSPAWN npc=%s kind=%s eid=0x%X: spawned by this engine on its own -- %s [n=%u]",
                 b.name.c_str(), kind == 0 ? "human" : kind == 1 ? "horse" : "animal", eid,
                 !g_act ? "left as it is (no_brains off: the twin)" : bound ? "the host streams it (shown), suspended once it has a brain"
                                                                    : "hidden this frame, suspended once it has a brain", k);
    }
    // a local spawn's brain comes a frame or more after its entity: asked to pause each frame for up to 5 s until it has one
    for (auto& kv : g_bodies) {
        BBody& b = kv.second;
        if (b.spawnAt < 0) continue;
        if (now - b.spawnAt > 5.0) { b.spawnAt = -1; continue; }
        void* e = engine::entity_by_id(kv.first);
        if (!e) { b.spawnAt = -1; continue; }
        uint64_t w = 0; int st = -1, mask = -1;
        if (!actions::brain_state(e, &w, &st, &mask) || st < 0) continue;
        if (g_act && st == 0 && !exempt(b.name, now)) b_queue_pause(kv.first, b, now);
        b.spawnAt = -1;
    }
}

void b_actions() {
    int budget = kPerFrame;
    char cmd[96];
    while (budget > 0 && !g_pauseQ.empty()) {
        const uint32_t eid = g_pauseQ.back();
        g_pauseQ.pop_back();
        auto it = g_bodies.find(eid);
        if (it == g_bodies.end() || !engine::entity_by_id(eid)) continue;
        std::snprintf(cmd, sizeof cmd, "wh_ai_PauseNPC %s", it->second.name.c_str());
        if (b_exec(cmd)) { it->second.pausedByUs = true; c_bPauses.fetch_add(1, std::memory_order_relaxed); }
        --budget;
    }
    while (budget > 0 && !g_hideQ.empty()) {
        const uint32_t eid = g_hideQ.back();
        g_hideQ.pop_back();
        if (npcdrive::is_bound_eid(eid)) continue;
        if (b_hide(eid, true)) c_bHides.fetch_add(1, std::memory_order_relaxed);
        --budget;
    }
    while (budget > 0 && !g_showQ.empty()) {
        const uint32_t eid = g_showQ.back();
        g_showQ.pop_back();
        if (b_hide(eid, false)) c_bShows.fetch_add(1, std::memory_order_relaxed);
        --budget;
    }
}

// B going off (the switch, the session's end, a load): the bodies B hid are shown again (their pause stays: the bind-time rule's)
void b_stop(const char* why) {
    uint32_t shown = 0;
    for (auto& kv : g_bodies) if (kv.second.hidden) { if (b_hide(kv.first, false)) ++shown; kv.second.hidden = false; }
    g_bodies.clear();
    g_pauseQ.clear(); g_hideQ.clear(); g_showQ.clear();
    g_spawnWatch.store(false);
    g_swept = false;
    g_cen = Census{};
    g_npcList.clear();
    g_listAt = -1e9;
    logf("WO216-BRAINS off (%s): %u hidden bodies shown again", why, shown);
}

void b_tick(double now) {
    const bool want = g_role.load() == 2;   // the census measures on every joiner in the host's world (the twin too)
    if (!want) {
        if (g_active) { g_active = false; b_stop("not a joiner in the host's world now"); }
        return;
    }
    const bool act = on(kBitNoBrains);
    if (g_active && g_act && !act) { b_stop("the switch"); g_active = false; }
    g_act = act;
    if (!g_active) {
        b_resolve();
        if (!g_b.armed) {
            static bool s_told = false;
            if (!s_told) { s_told = true; logf("WO216-BRAINS NOT armed: %s -- the bind-time suspend only (as 0.64.2)", g_b.why); }
            return;
        }
        g_active = true;
        g_activeSince = now;
        g_censusAt = 0;
        b_hook_spawn();
        g_spawnWatch.store(true);
    }
    b_spawns(now);
    if (now - g_censusAt >= wo216rules::kBrainCensusS) { g_censusAt = now; b_census(now); }
    b_actions();
}

void b_exempt(const char* name, bool on, double forS) {
    // the pipe's main-thread path runs this (op 165 is a main-thread op)
    if (!name || !*name) return;
    if (on) g_exempt[name] = now_s() + forS; else g_exempt.erase(name);
    static uint32_t s_n = 0;
    if (++s_n <= 100 || s_n % 100 == 0) logf("WO216-BRAINS exempt npc=%s %s%s [n=%u]", name, on ? "on for " : "off", on ? std::to_string(static_cast<int>(forS)).append(" s").c_str() : "", s_n);
}

int b_status(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n),
        "B no_brains=%s role=%u active=%d (%s; spawn watch %s) samples=%u nonzero=%u max_running=%u last_running=%u near_nonzero=%u max_near_running=%u "
        "last_near_running=%u last_near=%u last_dead_with_brain=%u last_exempt_running=%u bodies=%u list=%zu "
        "hidden=%u bound=%u census_ms=%.1f max_ms=%.1f sweep_paused=%u sweep_hidden=%u pauses=%u hides=%u shows=%u local_spawns=%u spawns_seen=%u "
        "breaches=%u exempt_running=%u exempt_names=%zu last=%s",
        on(kBitNoBrains) ? "on" : "off", g_role.load(), g_active ? 1 : 0, g_b.why, g_spawnWhy, g_cen.samples, g_cen.nonzero, g_cen.maxRunning,
        g_cen.lastRunning, g_cen.nearNonzero, g_cen.maxNearRunning, g_cen.lastNearRunning, g_cen.lastNear, g_cen.lastDead, g_cen.lastExemptRunning, g_cen.lastBodies, g_npcList.size(), g_cen.lastHidden, g_cen.lastBound, g_cen.lastCostMs, g_cen.maxCostMs,
        c_bSweepPaused.load(), c_bSweepHidden.load(), c_bPauses.load(), c_bHides.load(), c_bShows.load(), c_bLocalSpawns.load(), c_bSpawnSeen.load(),
        c_bBreaches.load(), c_bExemptRunning.load(), g_exempt.size(), g_lastNames.empty() ? "-" : g_lastNames.substr(0, 300).c_str());
}

// ================================================================================================== D: outfits
// The host reads each streamed body's worn items (its soul's EquipmentManager: armour and weapons by class) round-robin, ~10 bodies a
// second, and keeps the bodies whose set changed (or that are new to the stream) for the agent (op 166): the copy is dressed with the
// spawn and on a change. The joiner reads its copy's own set (op 167) for the agent's diff. Reads only (the reflection's own getters).
constexpr int kMaxWorn = 40;
struct Worn { uint8_t n = 0; uint8_t cls[kMaxWorn][16]{}; };
std::unordered_map<uint32_t, std::pair<std::string, Worn>> g_wornSent;   // eid -> (name, the set last handed out)
std::unordered_map<uint32_t, std::pair<std::string, Worn>> g_wornOut;    // eid -> (name, the set to hand out)
std::vector<std::pair<uint32_t, std::string>> g_wornList;
size_t g_wornRR = 0;
double g_wornListAt = -1e9, g_wornAt = 0;
std::atomic<uint32_t> c_dReads{0}, c_dChanged{0}, c_dTaken{0}, c_dUnread{0}, c_dOfReads{0};

int worn_of_eid(uint32_t eid, Worn* w) {
    void* soul = hits::soul_of_eid(eid);
    if (!soul) return -1;
    unsigned char tmp[kMaxWorn][16];
    const int n = rttr::soul_equipped(soul, tmp, kMaxWorn);
    if (n < 0) return -1;
    // sorted, so a set compares as a set
    std::vector<std::array<uint8_t, 16>> v(static_cast<size_t>(n));
    for (int i = 0; i < n; ++i) std::memcpy(v[static_cast<size_t>(i)].data(), tmp[i], 16);
    std::sort(v.begin(), v.end());
    w->n = static_cast<uint8_t>(n);
    for (int i = 0; i < n; ++i) std::memcpy(w->cls[i], v[static_cast<size_t>(i)].data(), 16);
    return n;
}
bool same(const Worn& a, const Worn& b) { return a.n == b.n && std::memcmp(a.cls, b.cls, static_cast<size_t>(a.n) * 16) == 0; }

void streamed_list_cb(const char* name, uint32_t eid, bool notHuman, void* ctx) {
    if (notHuman || !name) return;
    static_cast<std::vector<std::pair<uint32_t, std::string>>*>(ctx)->emplace_back(eid, name);
}

void d_tick(double now) {
    if (!on(kBitOutfits) || g_role.load() != 1) return;
    if (now - g_wornListAt >= 2.0) {
        g_wornListAt = now;
        g_wornList.clear();
        wo138::for_each_streamed(&streamed_list_cb, &g_wornList);
        // bodies no longer streamed: forgotten (a body streamed again is dressed again)
        for (auto it = g_wornSent.begin(); it != g_wornSent.end();) {
            bool in = false;
            for (const auto& b : g_wornList) if (b.first == it->first) { in = true; break; }
            if (!in) it = g_wornSent.erase(it); else ++it;
        }
    }
    if (g_wornList.empty() || now - g_wornAt < 0.1) return;
    g_wornAt = now;
    const auto& b = g_wornList[g_wornRR++ % g_wornList.size()];
    Worn w;
    if (worn_of_eid(b.first, &w) < 0) { c_dUnread.fetch_add(1, std::memory_order_relaxed); return; }
    c_dReads.fetch_add(1, std::memory_order_relaxed);
    auto it = g_wornSent.find(b.first);
    if (it != g_wornSent.end() && same(it->second.second, w)) return;
    g_wornSent[b.first] = {b.second, w};
    g_wornOut[b.first] = {b.second, w};
    c_dChanged.fetch_add(1, std::memory_order_relaxed);
}

uint8_t d_read(uint8_t maxN, uint8_t* out, size_t cap, size_t* outLen) {
    size_t k = 1;
    uint8_t cnt = 0;
    for (auto it = g_wornOut.begin(); it != g_wornOut.end() && cnt < maxN;) {
        const std::string& nm = it->second.first;
        const Worn& w = it->second.second;
        const size_t need = 1 + nm.size() + 1 + static_cast<size_t>(w.n) * 16;
        if (nm.empty() || nm.size() > 63) { it = g_wornOut.erase(it); continue; }
        if (k + need > cap) break;
        out[k] = static_cast<uint8_t>(nm.size()); std::memcpy(out + k + 1, nm.data(), nm.size()); k += 1 + nm.size();
        out[k++] = w.n;
        std::memcpy(out + k, w.cls, static_cast<size_t>(w.n) * 16); k += static_cast<size_t>(w.n) * 16;
        ++cnt;
        it = g_wornOut.erase(it);
    }
    out[0] = cnt;
    *outLen = k;
    c_dTaken.fetch_add(cnt, std::memory_order_relaxed);
    return wo163::kROk;
}

uint8_t d_of(const char* name, uint8_t* out, size_t cap, size_t* outLen) {
    if (cap < 2) return wo163::kRBadRequest;
    void* e = nullptr;
    // the copy by its name: the entity system's own lookup through the engine's iterator would be slow; the writer binds every copy
    // by name, and engine::entity_by_name is not exposed -- the soul by name instead (the copy's soul is its host NPC's)
    struct Ctx { const char* n; void* e; } cx{name, nullptr};
    engine::for_each_entity([](void* ent, void* p) {
        auto* c = static_cast<Ctx*>(p);
        const char* nm = engine::entity_name(ent);
        if (nm && _stricmp(nm, c->n) == 0) { c->e = ent; return true; }
        return false;
    }, &cx);
    e = cx.e;
    c_dOfReads.fetch_add(1, std::memory_order_relaxed);
    Worn w;
    if (!e || worn_of_eid(engine::entity_id(e), &w) < 0) { out[0] = 0; out[1] = 0; *outLen = 2; return wo163::kROk; }
    const size_t n = (std::min)(static_cast<size_t>(w.n), (cap - 2) / 16);
    out[0] = 1; out[1] = static_cast<uint8_t>(n);
    std::memcpy(out + 2, w.cls, n * 16);
    *outLen = 2 + n * 16;
    return wo163::kROk;
}

int d_status(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "D outfits=%s reads=%u unread=%u changed=%u taken=%u pending=%zu streamed=%zu joiner_reads=%u",
                         on(kBitOutfits) ? "on" : "off", c_dReads.load(), c_dUnread.load(), c_dChanged.load(), c_dTaken.load(), g_wornOut.size(),
                         g_wornList.size(), c_dOfReads.load());
}

double g_lastLine = 0;
void tick_reg() {
    const double now = now_s();
    b_tick(now);
    d_tick(now);
    if (now - g_lastLine >= 30.0) {
        g_lastLine = now;
        char t[2400];
        if (status(t, sizeof t) > 0 && g_configured.load()) logf("WO216-STATUS %s", t);
    }
}
int status_reg(char* out, int n) { return status(out, n); }
const bool kReg = wo203::reg_op(kOpConfig, &op) && wo203::reg_op(kOpStatus, &op) && wo203::reg_op(kOpBrains, &op) &&
                  wo203::reg_op(kOpExempt, &op) && wo203::reg_op(kOpOutfitRead, &op) && wo203::reg_op(kOpOutfitOf, &op) &&
                  wo203::reg_tick("w216", &tick_reg) && wo203::reg_status("w216", &status_reg);

} // namespace

bool on(uint32_t bit) { return (g_bits.load(std::memory_order_relaxed) & ~twin_off() & bit) != 0; }
bool configured() { return g_configured.load(std::memory_order_relaxed); }

int status(char* out, int n) {
    if (!out || n <= 0) return 0;
    int k = std::snprintf(out, static_cast<size_t>(n), "w216 bits=0x%X twin_off=0x%X configured=%d | ", g_bits.load(), twin_off(), g_configured.load() ? 1 : 0);
    if (k < 0 || k >= n) return n - 1;
    k += wo215::replay_status(out + k, n - k);
    if (k < n - 2) { k += std::snprintf(out + k, static_cast<size_t>(n - k), " | "); if (k < n - 2) k += b_status(out + k, n - k); }
    if (k < n - 2) { k += std::snprintf(out + k, static_cast<size_t>(n - k), " | "); if (k < n - 2) k += d_status(out + k, n - k); }
    if (k < n - 2) { k += std::snprintf(out + k, static_cast<size_t>(n - k), " | "); if (k < n - 2) k += wo208::owner_rows_status(out + k, n - k); }
    return k < n ? k : n - 1;
}

} // namespace kcdmp::wo216
