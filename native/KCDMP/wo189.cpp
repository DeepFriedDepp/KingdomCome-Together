// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#include "wo189.h"
#include "wo163.h"

#include "anchors.h"
#include "concept_read.h"
#include "engine.h"
#include "fault_guard.h"
#include "log.h"
#include "npcstate.h"

#include <windows.h>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <unordered_map>

namespace kcdmp::wo189 {

namespace {

constexpr uint32_t kPlayerEntity = 0x7777;   // the player entity id every save binds to (WO-125)
constexpr uint32_t kBeforeGameSave = 0x2A;   // the readiness mask the game's BeforeGameSave observer waits on (AI 0x02, 0x08, profile transitions 0x20)

// WO-188's layout reads (READ in its disassembly; each object's class is checked by RTTI before any of them is used)
constexpr size_t kSvcIsReady = 0x28;          // C_ReadinessService vtable: IsReady(mask)
constexpr ptrdiff_t kSvcTasksBegin = 0x48, kSvcTasksEnd = 0x50;   // its I_ReadinessTask* vector
constexpr size_t kTaskReady = 0x10, kTaskMask = 0x18, kTaskName = 0x20;   // I_ReadinessTask: IsReady(mask), category mask, name
constexpr ptrdiff_t kPlayerActive = 0x38, kPlayerInProgress = 0x80;   // C_CutscenePlayer: active configuration (shared_ptr), in progress
constexpr size_t kNpcStopFF = 0x4B8;          // C_NPC vtable: stop FastForward (argument 0 = whatever request runs)
constexpr ptrdiff_t kPosActive = 0x38, kPosTime = 0x3C, kPosState = 0x44;   // C_ScenePositioningManager: active, accumulated time, state
constexpr ptrdiff_t kProfileName = 0x08;      // C_GameProfile: the engine's own CryString name
constexpr ptrdiff_t kProfileActive = 0x38;    // C_GameProfile: active (a bool, not a count -- WO-188 R1.1)

constexpr char kLabelPlay[] = "wh::guimodule::C_CutscenePlayer::PlayCutscene";
constexpr char kLabelInterrupt[] = "wh::guimodule::C_CutscenePlayer::Interrupt";
constexpr char kLabelEnable[] = "wh::entitymodule::C_GameProfileManager::EnableProfile";
constexpr char kLabelDisable[] = "wh::entitymodule::C_GameProfileManager::DisableProfile";
constexpr char kLabelIsActive[] = "wh::entitymodule::C_GameProfileManager::IsProfileActive";
constexpr char kExpGetProfileMgr[] = "?GetGameProfileManager@C_EntityModule@entitymodule@wh@@UEBAAEAVI_GameProfileManager@23@XZ";

std::atomic<uint8_t> g_role{0};               // 1 host, 2 joiner, 0 unknown (the agent's Config)
std::atomic<bool> g_release{true}, g_ffStop{true}, g_mirror{false};
std::atomic<uint32_t> c_readyReads{0}, c_notReady{0}, c_releases{0}, c_interrupts{0}, c_ffStops{0}, c_enabled{0}, c_released{0}, c_concept{0};

template <class T> bool rd(const void* base, ptrdiff_t off, T* out) {
    if (!base) return false;
    KCDMP_FAULT_READ(site, "wo189::read");
    if (!fault::enabled(site)) return false;
    const uintptr_t a = reinterpret_cast<uintptr_t>(base) + static_cast<uintptr_t>(off);
    if (!fault::plausible_address(a)) return false;
    return fault::guarded(site, [&] { std::memcpy(out, reinterpret_cast<const void*>(a), sizeof(T)); });
}

// Committed, readable memory at p (a VirtualQuery, the last region kept): the .data scan below reads every qword as a pointer, and
// most are not (floats, ids) -- each was a counted fault (0.61.0/0.61.5 field and rig: wo189::read 269 a session)
bool readable(const void* p) {
    static uintptr_t lo = 0, hi = 0;
    const auto a = reinterpret_cast<uintptr_t>(p);
    if (!fault::plausible_address(a) || (a & 7) != 0) return false;
    if (a >= lo && a + 8 <= hi) return true;
    MEMORY_BASIC_INFORMATION mbi{};
    if (VirtualQuery(p, &mbi, sizeof mbi) != sizeof mbi || mbi.State != MEM_COMMIT) return false;
    const DWORD prot = mbi.Protect & 0xFF;
    if ((mbi.Protect & (PAGE_GUARD | PAGE_NOACCESS)) || !(prot & (PAGE_READONLY | PAGE_READWRITE | PAGE_WRITECOPY | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE)))
        return false;
    lo = reinterpret_cast<uintptr_t>(mbi.BaseAddress);
    hi = lo + mbi.RegionSize;
    return a + 8 <= hi;
}

bool is_a(const void* obj, void* const* vft) {
    void* vp = nullptr;
    return obj && vft && rd(obj, 0, &vp) && vp == static_cast<const void*>(vft);
}

void* vslot(const void* obj, size_t off) {
    void* vt = nullptr; void* fn = nullptr;
    return obj && rd(obj, 0, &vt) && vt && rd(vt, static_cast<ptrdiff_t>(off), &fn) ? fn : nullptr;
}

// printable ASCII at p (1..maxLen chars, NUL-terminated) -> out; false otherwise
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

// The object among the first `span` bytes of `holder` whose pointer leads to an object of class `vft` (offset cached, re-checked).
void* pointer_to(const void* holder, void* const* vft, size_t span, int* cachedOff) {
    if (!holder || !vft) return nullptr;
    auto at = [&](int off) -> void* {
        void* p = nullptr;
        return rd(holder, off, &p) && p && readable(p) && is_a(p, vft) ? p : nullptr;   // WO-200: a field that is not a pointer is skipped, not read
    };
    if (*cachedOff >= 0) if (void* p = at(*cachedOff)) return p;
    for (int off = 0; off < static_cast<int>(span); off += 8)
        if (void* p = at(off)) { *cachedOff = off; return p; }
    return nullptr;
}

// ------------------------------------------------------------------------------------------------ the readiness service

void* const* g_vftSystem = nullptr; void* const* g_vftSvc = nullptr;
int g_sysOff = -1, g_svcOff = -1, g_svcPtrOff = -1;
bool g_svcResolved = false;
const char* g_svcWhy = "not resolved";

void* readiness_service() {
    if (!g_svcResolved) {
        g_svcResolved = true;
        HMODULE cs = GetModuleHandleA("CrySystem.dll");
        g_vftSystem = cs ? anchor::find_vftable(cs, ".?AVCSystem@@", 0) : nullptr;
        g_vftSvc = cs ? anchor::find_vftable(cs, ".?AVC_ReadinessService@wh@@", 0) : nullptr;
        g_svcWhy = !g_vftSystem ? "RTTI CSystem not unique" : !g_vftSvc ? "RTTI C_ReadinessService not unique" : "armed";
        logf("WO189-READY resolver: CSystem %s, C_ReadinessService %s", g_vftSystem ? "found" : "MISSING", g_vftSvc ? "found" : "MISSING");
    }
    if (!g_vftSystem || !g_vftSvc) return nullptr;
    void* sys = pointer_to(engine::genv(), g_vftSystem, 0x400, &g_sysOff);
    if (!sys) { g_svcWhy = "no CSystem among gEnv's pointers"; return nullptr; }
    // the service is embedded in CSystem: the qword that is its own vftable (found once, re-checked)
    auto at = [&](int off) -> void* { void* v = nullptr; return rd(sys, off, &v) && v == static_cast<const void*>(g_vftSvc) ? static_cast<char*>(sys) + off : nullptr; };
    if (g_svcOff >= 0) if (void* p = at(g_svcOff)) return p;
    for (int off = 8; off < 0x10000; off += 8)
        if (void* p = at(off)) {
            g_svcOff = off;
            logf("WO189-READY the readiness service is embedded at CSystem+0x%X (by its vftable); gEnv+0x%X is CSystem", off, g_sysOff);
            return p;
        }
    // or held through a pointer of CSystem
    const bool first = g_svcPtrOff < 0;
    if (void* p = pointer_to(sys, g_vftSvc, 0x10000, &g_svcPtrOff)) {
        if (first) logf("WO189-READY the readiness service is CSystem's pointer at +0x%X (by its vftable); gEnv+0x%X is CSystem", g_svcPtrOff, g_sysOff);
        g_svcWhy = "armed";
        return p;
    }
    g_svcWhy = "C_ReadinessService neither embedded in nor pointed to by CSystem's first 64 KiB";
    return nullptr;
}

struct ReadyRead { bool armed = false, ready = true; std::string names; };

// A task's name (I_ReadinessTask vt+0x20): a char*, a CryString returned by value (into the scratch), or a reference to one.
std::string task_name(void* task) {
    void* fn = vslot(task, kTaskName);
    if (!fn) return "?";
    alignas(16) uint8_t scratch[64]{};
    void* r = nullptr;
    KCDMP_FAULT_CALL(site, "wo189::task_name");
    if (!fault::guarded(site, [&] { r = reinterpret_cast<void* (*)(void*, void*)>(fn)(task, scratch); })) return "?";
    std::string s;
    if (r == scratch) { void* p = nullptr; std::memcpy(&p, scratch, 8); if (text_at(p, &s)) return s; return "?"; }
    if (text_at(r, &s)) return s;
    void* p = nullptr;
    if (rd(r, 0, &p) && text_at(p, &s)) return s;
    return "?";
}

ReadyRead ready_read(bool names) {
    ReadyRead out;
    void* svc = readiness_service();
    if (!svc) return out;
    void* fn = vslot(svc, kSvcIsReady);
    if (!fn) return out;
    bool ready = true;
    KCDMP_FAULT_CALL(site, "wo189::IsReady");
    if (!fault::guarded(site, [&] { ready = reinterpret_cast<bool (*)(void*, uint32_t)>(fn)(svc, kBeforeGameSave); })) return out;
    out.armed = true; out.ready = ready;
    c_readyReads.fetch_add(1);
    if (ready || !names) return out;
    c_notReady.fetch_add(1);
    void* b = nullptr; void* e = nullptr;
    if (!rd(svc, kSvcTasksBegin, &b) || !rd(svc, kSvcTasksEnd, &e) || !b || !e || e < b) return out;
    const size_t n = (static_cast<char*>(e) - static_cast<char*>(b)) / 8;
    KCDMP_FAULT_CALL(tsite, "wo189::task");
    for (size_t i = 0; i < n && i < 128; ++i) {
        void* t = nullptr;
        if (!rd(b, static_cast<ptrdiff_t>(i * 8), &t) || !t) continue;
        void* fm = vslot(t, kTaskMask); void* fr = vslot(t, kTaskReady);
        if (!fm || !fr) continue;
        uint32_t mask = 0; bool tr = true;
        if (!fault::guarded(tsite, [&] { mask = reinterpret_cast<uint32_t (*)(void*)>(fm)(t); })) continue;
        if (!(mask & kBeforeGameSave)) continue;
        if (!fault::guarded(tsite, [&] { tr = reinterpret_cast<bool (*)(void*, uint32_t)>(fr)(t, kBeforeGameSave); })) continue;
        if (tr) continue;
        char m[16]; std::snprintf(m, sizeof m, "[0x%X]", mask & kBeforeGameSave);
        if (!out.names.empty()) out.names += ",";
        out.names += task_name(t) + m;
    }
    if (out.names.empty()) out.names = "-";
    return out;
}

// ------------------------------------------------------------------------------------------------ scene positioning (read only)

void* const* g_vftPos = nullptr; void* const* g_vftXg = nullptr;
int g_posOffMgr = -1, g_posOffXg = -1, g_xgOff = -1;
void* g_posSlot = nullptr;
bool g_posResolved = false;

void* positioning() {
    if (!g_posResolved) {
        g_posResolved = true;
        HMODULE xg = GetModuleHandleA("XGenAIModule.dll");
        g_vftPos = xg ? anchor::find_vftable(xg, ".?AVC_ScenePositioningManager@xgenaimodule@wh@@", 0) : nullptr;
        g_vftXg = xg ? anchor::find_vftable(xg, ".?AVC_XGenAIModule@xgenaimodule@wh@@", 0) : nullptr;
        logf("WO189-POS resolver: C_ScenePositioningManager %s, C_XGenAIModule %s", g_vftPos ? "found" : "MISSING", g_vftXg ? "found" : "MISSING");
    }
    if (!g_vftPos) return nullptr;
    if (g_posSlot) { void* p = nullptr; if (rd(g_posSlot, 0, &p) && is_a(p, g_vftPos)) return p; }
    if (void* p = pointer_to(wo141::x::npc_manager(), g_vftPos, 0x1000, &g_posOffMgr)) return p;
    void* xgm = pointer_to(engine::game_iface(), g_vftXg, 0x400, &g_xgOff);
    if (void* p = pointer_to(xgm, g_vftPos, 0x2000, &g_posOffXg)) return p;
    // a global of XGenAIModule's .data that points to it (scanned at most three times a session, a minute apart: one frame's cost each)
    static uint64_t nextScan = 0;
    static int scans = 0;
    const uint64_t now = GetTickCount64();
    if (now < nextScan || scans >= 3) return nullptr;
    nextScan = now + 60000;
    ++scans;
    HMODULE xg = GetModuleHandleA("XGenAIModule.dll");
    anchor::Range data{};
    if (!xg || !anchor::section(xg, ".data", &data)) return nullptr;
    const uintptr_t b = reinterpret_cast<uintptr_t>(data.begin), e = reinterpret_cast<uintptr_t>(data.end);
    for (uintptr_t a = (b + 7) & ~uintptr_t(7); a + 8 <= e; a += 8) {
        void* p = nullptr;
        if (!rd(reinterpret_cast<void*>(a), 0, &p) || !p || !readable(p)) continue;
        if (is_a(p, g_vftPos)) {
            g_posSlot = reinterpret_cast<void*>(a);
            logf("WO189-POS the scene positioning manager is held by XGenAIModule's .data at +0x%llX (by its vftable)",
                 static_cast<unsigned long long>(a - reinterpret_cast<uintptr_t>(xg)));
            return p;
        }
    }
    logf("WO189-POS the scene positioning manager was not found (the NPC manager, the AI module, its .data): the agent's lines decide");
    return nullptr;
}

// ------------------------------------------------------------------------------------------------ the cutscene player

void* const* g_vftGui = nullptr; void* const* g_vftPlayer = nullptr;
int g_guiOff = -1, g_playerOff = -1;
using PlayFn = void (*)(void* player, const void* cfgRef);
using InterruptFn = void (*)(void* player);
PlayFn g_play = nullptr; InterruptFn g_interrupt = nullptr;
bool g_csResolved = false;
const char* g_csWhy = "not resolved";

// the one vftable slot of `vft` whose function references `label` (the slot is found, never assumed)
void* slot_by_label(HMODULE mod, void* const* vft, const char* label, size_t* slotOut) {
    int nl = 0;
    const char* s = anchor::find_cstring(mod, label, &nl);
    anchor::Range text{};
    if (!s || !vft || !anchor::section(mod, ".text", &text)) return nullptr;
    int found = 0; void* fn = nullptr;
    for (size_t i = 0; i < 0x100; ++i) {
        void* f = nullptr;
        if (!rd(vft, static_cast<ptrdiff_t>(i * 8), &f) || !f || !text.contains(f)) break;
        if (anchor::function_refs(mod, f, s)) { if (f != fn) ++found; fn = f; if (slotOut) *slotOut = i * 8; }
    }
    return found == 1 ? fn : nullptr;
}

bool resolve_cutscene() {
    if (g_csResolved) return g_play != nullptr;
    g_csResolved = true;
    HMODULE gui = GetModuleHandleA("GUIModule.dll");
    if (!gui) { g_csWhy = "GUIModule not loaded"; logf("WO189-CUTSCENE not armed: %s", g_csWhy); return false; }
    g_vftGui = anchor::find_vftable(gui, ".?AVC_GUIModule@guimodule@wh@@", 0);
    g_vftPlayer = anchor::find_vftable(gui, ".?AVC_CutscenePlayer@guimodule@wh@@", 0);
    int n = 0;
    g_play = reinterpret_cast<PlayFn>(const_cast<uint8_t*>(anchor::function_by_string(gui, kLabelPlay, &n)));
    size_t slot = 0;
    g_interrupt = reinterpret_cast<InterruptFn>(slot_by_label(gui, g_vftPlayer, kLabelInterrupt, &slot));
    g_csWhy = !g_vftGui ? "RTTI C_GUIModule not unique" : !g_vftPlayer ? "RTTI C_CutscenePlayer not unique"
            : !g_play ? "PlayCutscene's label is not referenced by exactly one function" : "armed";
    char d[64] = "-";
    if (g_play) anchor::describe(reinterpret_cast<const void*>(g_play), d, sizeof d);
    logf("WO189-CUTSCENE resolver: %s; PlayCutscene=%s (by its label, %d function(s)); Interrupt %s (vftable slot 0x%zX by its label)", g_csWhy, d, n,
         g_interrupt ? "found" : "NOT found (no last resort)", slot);
    if (!g_vftGui || !g_vftPlayer) g_play = nullptr;
    return g_play != nullptr;
}

void* cutscene_player() {
    if (!resolve_cutscene()) return nullptr;
    void* gm = pointer_to(engine::game_iface(), g_vftGui, 0x400, &g_guiOff);
    return pointer_to(gm, g_vftPlayer, 0x400, &g_playerOff);
}

// ------------------------------------------------------------------------------------------------ the game profiles

void* const* g_vftMgr = nullptr; void* const* g_vftProfile = nullptr; void* const* g_vftHandle = nullptr; void* const* g_vftEnt = nullptr;
using GetMgrFn = void* (*)(const void* ent);
using EnableFn = void* (*)(void* mgr, void** outHandle, const void* name);
using ReleaseFn = void* (*)(void* h, uint32_t flags);
GetMgrFn g_getMgr = nullptr; EnableFn g_enable = nullptr; ReleaseFn g_releaseFn = nullptr;
int g_entOff = -1, g_vecOff = -1, g_mgrO1 = -1, g_mgrO2 = -1;
bool g_mgrScanned = false;
bool g_pfResolved = false;
const char* g_pfWhy = "not resolved";
std::unordered_map<std::string, void*> g_handles;   // main thread: the handles this DLL took, by profile name

bool resolve_profiles() {
    if (g_pfResolved) return g_enable != nullptr;
    g_pfResolved = true;
    HMODULE em = GetModuleHandleA("EntityModule.dll");
    if (!em) { g_pfWhy = "EntityModule not loaded"; logf("WO189-PROFILE not armed: %s", g_pfWhy); return false; }
    g_vftMgr = anchor::find_vftable(em, ".?AVC_GameProfileManager@entitymodule@wh@@", 0);
    g_vftProfile = anchor::find_vftable(em, ".?AVC_GameProfile@entitymodule@wh@@", 0);
    g_vftHandle = anchor::find_vftable(em, ".?AVC_ProfileHandle@entitymodule@wh@@", 0);
    g_vftEnt = anchor::find_vftable(em, ".?AVC_EntityModule@entitymodule@wh@@", 0);
    g_getMgr = reinterpret_cast<GetMgrFn>(GetProcAddress(em, kExpGetProfileMgr));
    size_t se = 0, sa = 0, sr = 0;
    g_enable = reinterpret_cast<EnableFn>(slot_by_label(em, g_vftMgr, kLabelEnable, &se));
    (void)kLabelIsActive; (void)sa;   // two overloads (by id, by name) share its label: the flag is read from the profile instead (C_GameProfile+0x38)
    g_releaseFn = reinterpret_cast<ReleaseFn>(slot_by_label(em, g_vftHandle, kLabelDisable, &sr));
    g_pfWhy = !g_vftMgr || !g_vftProfile || !g_vftHandle || !g_vftEnt ? "an RTTI class (manager, profile, handle, entity module) not unique"
            : !g_getMgr ? "GetGameProfileManager not exported" : !g_enable ? "EnableProfile's label not on exactly one manager slot"
            : !g_releaseFn ? "DisableProfile's label not on the handle's slot" : "armed";
    logf("WO189-PROFILE resolver: %s (EnableProfile slot 0x%zX, the handle's release slot 0x%zX, both by their labels; active = C_GameProfile+0x38)",
         g_pfWhy, se, sr);
    if (std::strcmp(g_pfWhy, "armed") != 0) { g_enable = nullptr; return false; }
    return true;
}

void* profile_manager() {
    if (!resolve_profiles()) return nullptr;
    void* gi = engine::game_iface();
    if (void* ent = pointer_to(gi, g_vftEnt, 0x400, &g_entOff)) {
        void* mgr = nullptr;
        KCDMP_FAULT_CALL(site, "wo189::GetGameProfileManager");
        if (fault::guarded(site, [&] { mgr = g_getMgr(ent); }) && is_a(mgr, g_vftMgr)) return mgr;
    }
    // the entity module is held through an interface pointer: the manager is a pointer of one of the game interface's objects
    // (two levels, found once by the manager's own vftable, re-checked every call)
    auto at = [&](int o1, int o2) -> void* {
        void* p = nullptr; void* m = nullptr;
        return rd(gi, o1, &p) && p && readable(p) && rd(p, o2, &m) && m && readable(m) && is_a(m, g_vftMgr) ? m : nullptr;   // WO-200: pointers only
    };
    if (g_mgrO1 >= 0) if (void* m = at(g_mgrO1, g_mgrO2)) return m;
    if (g_mgrScanned) return nullptr;
    g_mgrScanned = true;
    for (int o1 = 0; o1 < 0x400; o1 += 8)
        for (int o2 = 0; o2 < 0x400; o2 += 8)
            if (void* m = at(o1, o2)) {
                g_mgrO1 = o1; g_mgrO2 = o2;
                logf("WO189-PROFILE the profile manager is [[game interface+0x%X]+0x%X] (by its vftable)", o1, o2);
                return m;
            }
    logf("WO189-PROFILE the profile manager was not found among the game interface's objects");
    return nullptr;
}

// the manager's profile vector: the (begin, end) pair whose first element is a C_GameProfile (found once, re-checked)
bool profiles(void* mgr, void*** begin, size_t* count) {
    auto at = [&](int off) -> bool {
        void* b = nullptr; void* e = nullptr; void* first = nullptr;
        if (!rd(mgr, off, &b) || !rd(mgr, off + 8, &e) || !b || !e || e <= b) return false;
        const size_t n = (static_cast<char*>(e) - static_cast<char*>(b)) / 8;
        if (n > 20000 || (static_cast<char*>(e) - static_cast<char*>(b)) % 8) return false;
        if (!rd(b, 0, &first) || !is_a(first, g_vftProfile)) return false;
        *begin = static_cast<void**>(b); *count = n;
        return true;
    };
    if (g_vecOff >= 0 && at(g_vecOff)) return true;
    for (int off = 8; off < 0x200; off += 8) if (at(off)) { g_vecOff = off; return true; }
    return false;
}

std::string profile_name(void* p) {
    void* s = nullptr; std::string out;
    return rd(p, kProfileName, &s) && text_at(s, &out, 120) ? out : "";
}

void* profile_by_name(void* mgr, const std::string& name) {
    void** b = nullptr; size_t n = 0;
    if (!profiles(mgr, &b, &n)) return nullptr;
    for (size_t i = 0; i < n; ++i) {
        void* p = nullptr;
        if (rd(b, static_cast<ptrdiff_t>(i * 8), &p) && is_a(p, g_vftProfile) && profile_name(p) == name) return p;
    }
    return nullptr;
}

// 1 active, 0 not, -1 unknown
int is_active(void* mgr, void* profile) {
    (void)mgr;
    uint8_t a = 0;
    if (!is_a(profile, g_vftProfile) || !rd(profile, kProfileActive, &a)) return -1;
    return a ? 1 : 0;
}

bool joiner_acts(const std::atomic<bool>& sw) { return g_role.load() == 2 && sw.load(); }

void put_f(uint8_t* p, float v) { std::memcpy(p, &v, 4); }

} // namespace

// WO-194 (wo189.h)
void* cutscene_player_obj() { return cutscene_player(); }

uint8_t play_active(void* cfgWant) {
    void* pl = cutscene_player();
    if (!pl) return 2;
    void* cfg = nullptr; uint8_t prog = 0;
    if (!rd(pl, kPlayerActive, &cfg) || !rd(pl, kPlayerInProgress, &prog)) return 3;
    if (!cfg || cfg != cfgWant || prog) return 0;
    const void* ref = static_cast<char*>(pl) + kPlayerActive;
    KCDMP_FAULT_CALL(site, "wo189::PlayCutscene(wo194)");
    if (!fault::guarded(site, [&] { g_play(pl, ref); })) return 3;
    c_releases.fetch_add(1);
    return 1;
}

int status_text(char* out, int n) {
    return std::snprintf(out, n, "role=%u release=%d ff_stop=%d mirror=%d ready_reads=%u not_ready=%u releases=%u interrupts=%u ff_stops=%u "
                         "profiles_enabled=%u profiles_released=%u handles=%zu concept_reads=%u readiness=%s cutscene=%s profiles=%s",
                         g_role.load(), g_release.load() ? 1 : 0, g_ffStop.load() ? 1 : 0, g_mirror.load() ? 1 : 0, c_readyReads.load(), c_notReady.load(),
                         c_releases.load(), c_interrupts.load(), c_ffStops.load(), c_enabled.load(), c_released.load(), g_handles.size(), c_concept.load(),
                         g_svcWhy, g_csWhy, g_pfWhy);
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
            if (len != 5 || cap < 4) return wo163::kRBadRequest;
            if (body[1] != 255 && body[1] != g_role.exchange(body[1])) logf("WO189-CFG role=%s", body[1] == 1 ? "host" : body[1] == 2 ? "joiner" : "unknown");
            if (body[2] != 255) g_release.store(body[2] != 0);
            if (body[3] != 255) g_ffStop.store(body[3] != 0);
            if (body[4] != 255) g_mirror.store(body[4] != 0);
            out[0] = g_role.load(); out[1] = g_release.load() ? 1 : 0; out[2] = g_ffStop.load() ? 1 : 0; out[3] = g_mirror.load() ? 1 : 0;
            *outLen = 4;
            return wo163::kROk;
        }
        case kOpReady: {
            if (cap < 10 + 1) return wo163::kRBadRequest;
            const ReadyRead r = ready_read(true);
            out[0] = r.armed ? 1 : 0; out[1] = r.ready ? 1 : 0;
            void* pm = positioning();
            int32_t st = -1; uint8_t act = 0; float secs = 0;
            if (pm) { rd(pm, kPosState, &st); rd(pm, kPosActive, &act); rd(pm, kPosTime, &secs); }
            out[2] = pm ? 1 : 0; out[3] = static_cast<uint8_t>(st < 0 ? 255 : (st > 254 ? 254 : st)); out[4] = act ? 1 : 0;
            put_f(out + 5, std::isfinite(secs) ? secs : 0.0f);
            const size_t room = cap - 10;
            const size_t nl = r.names.size() < room && r.names.size() < 255 ? r.names.size() : (room < 255 ? room : 255);
            out[9] = static_cast<uint8_t>(nl);
            std::memcpy(out + 10, r.names.data(), nl);
            *outLen = 10 + nl;
            return wo163::kROk;
        }
        case kOpCutscene: {
            if (cap < 11) return wo163::kRBadRequest;
            void* pl = cutscene_player();
            void* cfg = nullptr; uint8_t prog = 0;
            const bool ok = pl && rd(pl, kPlayerActive, &cfg) && rd(pl, kPlayerInProgress, &prog);
            out[0] = ok ? 1 : 0; out[1] = ok && cfg ? 1 : 0; out[2] = prog ? 1 : 0;
            const uint64_t id = reinterpret_cast<uint64_t>(cfg);
            std::memcpy(out + 3, &id, 8);
            *outLen = 11;
            return wo163::kROk;
        }
        case kOpCutsceneRelease: {
            if (len != 10 || cap < 1) return wo163::kRBadRequest;
            *outLen = 1;
            if (!joiner_acts(g_release)) { out[0] = 5; logf("WO189-CUTSCENE release refused: %s", g_role.load() != 2 ? "not the joiner (the host's cutscenes are never released)" : "mp_cutscene_release is off"); return wo163::kROk; }
            void* pl = cutscene_player();
            if (!pl) { out[0] = 2; logf("WO189-CUTSCENE release not armed: %s", g_csWhy); return wo163::kROk; }
            uint64_t want = 0; std::memcpy(&want, body + 2, 8);
            void* cfg = nullptr; uint8_t prog = 0;
            if (!rd(pl, kPlayerActive, &cfg) || !rd(pl, kPlayerInProgress, &prog)) { out[0] = 3; return wo163::kROk; }
            if (!cfg || reinterpret_cast<uint64_t>(cfg) != want) { out[0] = 0; logf("WO189-CUTSCENE release refused: the player's active configuration changed (now %p)", cfg); return wo163::kROk; }
            const void* ref = static_cast<char*>(pl) + kPlayerActive;
            if (body[1] == 1) {
                if (prog) { out[0] = 0; logf("WO189-CUTSCENE release refused: the cutscene is in progress already"); return wo163::kROk; }
                KCDMP_FAULT_CALL(site, "wo189::PlayCutscene");
                if (!fault::guarded(site, [&] { g_play(pl, ref); })) { out[0] = 3; logf("WO189-CUTSCENE PlayCutscene FAULTED"); return wo163::kROk; }
                rd(pl, kPlayerInProgress, &prog);
                c_releases.fetch_add(1);
                logf("WO189-CUTSCENE released: C_CutscenePlayer::PlayCutscene(player, &active) -- the call the quest node's own PlayCutscene input makes; "
                     "in_progress=%d [n=%u]", prog ? 1 : 0, c_releases.load());
                out[0] = 1;
            } else if (body[1] == 2) {
                if (!g_interrupt) { out[0] = 2; logf("WO189-CUTSCENE Interrupt not armed (its slot was not found)"); return wo163::kROk; }
                KCDMP_FAULT_CALL(site, "wo189::Interrupt");
                if (!fault::guarded(site, [&] { g_interrupt(pl); })) { out[0] = 3; logf("WO189-CUTSCENE Interrupt FAULTED"); return wo163::kROk; }
                c_interrupts.fetch_add(1);
                logf("WO189-CUTSCENE Interrupt called (the last resort: the player's full abort, which also empties its queue) [n=%u]", c_interrupts.load());
                out[0] = 1;
            } else return wo163::kRBadRequest;
            return wo163::kROk;
        }
        case kOpStopFF: {
            if (len < 6) return wo163::kRBadRequest;
            float maxD = 0; std::memcpy(&maxD, body + 1, 4);
            const size_t n = body[5];
            if (len != 6 + 4 * n || cap < 1 + n) return wo163::kRBadRequest;
            out[0] = static_cast<uint8_t>(n);
            *outLen = 1 + n;
            const bool acts = joiner_acts(g_ffStop);
            float me[3]{}; bool haveMe = false;
            if (void* pe = engine::entity_by_id(kPlayerEntity)) haveMe = engine::entity_world_pos(pe, me);
            HMODULE xg = GetModuleHandleA("XGenAIModule.dll");
            static void* const* vftNpc = xg ? anchor::find_vftable(xg, ".?AVC_NPC@xgenaimodule@wh@@", 0) : nullptr;
            void* mgr = wo141::x::npc_manager();
            for (size_t i = 0; i < n; ++i) {
                uint32_t eid = 0; std::memcpy(&eid, body + 6 + 4 * i, 4);
                uint8_t& res = out[1 + i];
                if (!acts) { res = 5; continue; }
                if (maxD > 0 && haveMe) {
                    float p[3]{};
                    void* e = engine::entity_by_id(eid);
                    if (!e || !engine::entity_world_pos(e, p)) { res = 0; continue; }
                    const float dx = p[0] - me[0], dy = p[1] - me[1], dz = p[2] - me[2];
                    if (std::sqrt(dx * dx + dy * dy + dz * dz) > maxD) { res = 2; continue; }
                }
                void* npc = nullptr; void* get = vslot(mgr, 0x20);
                KCDMP_FAULT_CALL(gsite, "wo189::npc_of");
                if (!get || !fault::guarded(gsite, [&] { npc = reinterpret_cast<void* (*)(void*, const uint32_t*)>(get)(mgr, &eid); }) || !is_a(npc, vftNpc)) { res = 0; continue; }
                void* stop = vslot(npc, kNpcStopFF);
                KCDMP_FAULT_CALL(site, "wo189::StopFastForward");
                if (!stop || !fault::guarded(site, [&] { reinterpret_cast<void (*)(void*, uint64_t)>(stop)(npc, 0); })) { res = 3; continue; }
                res = 1;
                c_ffStops.fetch_add(1);
                const char* nm = nullptr; if (void* e = engine::entity_by_id(eid)) nm = engine::entity_name(e);
                logf("WO189-FF stopped npc=%s eid=0x%X -- C_NPC's stop-FastForward (argument 0), the engine's own per-NPC call [n=%u]", nm ? nm : "?", eid, c_ffStops.load());
            }
            return wo163::kROk;
        }
        case kOpProfile: {
            if (len < 2 || cap < 5) return wo163::kRBadRequest;
            const uint8_t sub = body[1];
            void* mgr = profile_manager();
            if (sub == 1) {   // list the active ones
                if (len != 4) return wo163::kRBadRequest;
                uint16_t start = 0; std::memcpy(&start, body + 2, 2);
                void** b = nullptr; size_t n = 0;
                uint16_t total = 0, next = 0;
                size_t at = 4;
                if (mgr && profiles(mgr, &b, &n)) {
                    uint16_t k = 0;
                    for (size_t i = 0; i < n; ++i) {
                        void* p = nullptr;
                        if (!rd(b, static_cast<ptrdiff_t>(i * 8), &p) || !is_a(p, g_vftProfile) || is_active(mgr, p) != 1) continue;
                        ++total;
                        if (k++ < start) continue;
                        const std::string nm = profile_name(p);
                        if (next == 0 && !nm.empty()) {
                            if (at + nm.size() + 1 > cap) { next = static_cast<uint16_t>(k - 1); continue; }
                            std::memcpy(out + at, nm.data(), nm.size()); at += nm.size(); out[at++] = '\n';
                        }
                    }
                }
                std::memcpy(out, &total, 2); std::memcpy(out + 2, &next, 2);
                *outLen = at;
                return wo163::kROk;
            }
            if (sub == 5) {   // a load: every handle this DLL took points into the world that is gone -- forgotten, never released
                const size_t k = g_handles.size();
                g_handles.clear();
                if (k) logf("WO189-PROFILE a load: %zu handle(s) of this DLL forgotten (never released into a new world)", k);
                out[0] = static_cast<uint8_t>(k > 255 ? 255 : k); *outLen = 1;
                return wo163::kROk;
            }
            if (len < 3 || len != 3u + body[2]) return wo163::kRBadRequest;
            const std::string name(reinterpret_cast<const char*>(body + 3), body[2]);
            *outLen = 1;
            if (!mgr) { out[0] = 2; return wo163::kROk; }
            void* p = profile_by_name(mgr, name);
            if (sub == 4) { out[0] = !p ? 4 : static_cast<uint8_t>(is_active(mgr, p) == 1 ? 1 : 0); return wo163::kROk; }
            if (sub == 2) {
                if (!joiner_acts(g_mirror)) { out[0] = 5; return wo163::kROk; }
                if (!p) { out[0] = 4; logf("WO189-PROFILE enable '%s': no such profile in this level", name.c_str()); return wo163::kROk; }
                if (g_handles.count(name)) { out[0] = 0; return wo163::kROk; }
                const int a = is_active(mgr, p);
                if (a != 0) { out[0] = a == 1 ? 0 : 3; logf("WO189-PROFILE enable '%s': %s -- nothing taken", name.c_str(), a == 1 ? "already active on this machine (its own quest holds it)" : "IsProfileActive faulted"); return wo163::kROk; }
                void* h = nullptr;
                KCDMP_FAULT_CALL(site, "wo189::EnableProfile");
                const void* nm = static_cast<char*>(p) + kProfileName;
                if (!fault::guarded(site, [&] { g_enable(mgr, &h, nm); })) { out[0] = 3; logf("WO189-PROFILE EnableProfile('%s') FAULTED", name.c_str()); return wo163::kROk; }
                if (!is_a(h, g_vftHandle)) { out[0] = 3; logf("WO189-PROFILE EnableProfile('%s') returned no handle (%p)", name.c_str(), h); return wo163::kROk; }
                g_handles[name] = h;
                c_enabled.fetch_add(1);
                logf("WO189-PROFILE enabled '%s' (EnableProfile, the handle kept) active_now=%d [n=%u]", name.c_str(), is_active(mgr, p), c_enabled.load());
                out[0] = 1;
                return wo163::kROk;
            }
            if (sub == 3) {
                auto it = g_handles.find(name);
                if (it == g_handles.end()) { out[0] = 0; return wo163::kROk; }
                void* h = it->second;
                g_handles.erase(it);
                if (g_role.load() != 2) { out[0] = 5; return wo163::kROk; }
                if (!is_a(h, g_vftHandle)) { out[0] = 3; logf("WO189-PROFILE release '%s': the handle is no longer a C_ProfileHandle -- forgotten", name.c_str()); return wo163::kROk; }
                KCDMP_FAULT_CALL(site, "wo189::ReleaseProfile");
                if (!fault::guarded(site, [&] { g_releaseFn(h, 1); })) { out[0] = 3; logf("WO189-PROFILE release '%s' FAULTED", name.c_str()); return wo163::kROk; }
                c_released.fetch_add(1);
                logf("WO189-PROFILE released '%s' (the handle this DLL took) active_now=%d [n=%u]", name.c_str(), p ? is_active(mgr, p) : -1, c_released.load());
                out[0] = 1;
                return wo163::kROk;
            }
            return wo163::kRBadRequest;
        }
        case kOpConcept: {
            if (len < 3 || cap < 1) return wo163::kRBadRequest;
            const size_t pl = body[1];
            if (len < 3 + pl || len != 3 + pl + body[2 + pl]) return wo163::kRBadRequest;
            const std::string path(reinterpret_cast<const char*>(body + 2), pl);
            const std::string port(reinterpret_cast<const char*>(body + 3 + pl), body[2 + pl]);
            c_concept.fetch_add(1);
            logf("WO189-CONCEPT read %s . %s (the WO-97 reader, read only)", path.c_str(), port.c_str());
            out[0] = conceptread::port_op(path.c_str(), port.c_str(), 1, false) ? 1 : 0;
            *outLen = 1;
            return wo163::kROk;
        }
        default:
            return wo163::kRBadRequest;
    }
}

} // namespace kcdmp::wo189
