// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-215: the animation substrate's native half -- the movement request (A.0), the route (A.1-A.3), the switches (wo215.h).
#include "wo215.h"
#include "wo203.h"
#include "wo163.h"
#include "npcstate.h"
#include "anchors.h"
#include "engine.h"
#include "fault_guard.h"
#include "hook_prologues.h"
#include "inline_hook.h"
#include "log.h"
#include "npc_drive.h"
#include "respawn_actions.h"
#include "wo187.h"
#include "wo208.h"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

namespace kcdmp::wo215 {

namespace {

using namespace wo215rules;

// ---------------------------------------------------------------------------------------------------- the switch word
std::atomic<uint32_t> g_bits{kBitsDefault};
std::atomic<bool> g_configured{false};

uint32_t twin_off() {
    static uint32_t s = 0xFFFFFFFFu;
    if (s == 0xFFFFFFFFu) {
        char b[256] = {};
        const DWORD n = GetEnvironmentVariableA("KCDMP_TEST_W215_OFF", b, sizeof b);
        s = (n > 0 && n < sizeof b) ? twin_off_bits(b) : 0;
        if (s) logf("WO215-CFG the bench's twin: bits 0x%X off for this game (KCDMP_TEST_W215_OFF=%s)", s, b);
    }
    return s;
}

// The route's tuning (op 153): the corrector's distance, and A.0's per-frame order mode (the bench's test of "a target every frame").
std::atomic<uint32_t> g_correctCm{static_cast<uint32_t>(kCorrectDefaultM * 100.0f)};
std::atomic<bool> g_perFrame{false};
Cadence g_cad;   // main thread (op 153 runs there)

double now_s() {
    static LARGE_INTEGER f{};
    if (!f.QuadPart) QueryPerformanceFrequency(&f);
    LARGE_INTEGER c;
    QueryPerformanceCounter(&c);
    return static_cast<double>(c.QuadPart) / static_cast<double>(f.QuadPart);
}

// ---------------------------------------------------------------------------------------------------- SEH-guarded reads
template <class T> bool rd(const void* base, size_t off, T* out) {
    KCDMP_FAULT_READ(site, "wo215::rd");
    const uintptr_t a = reinterpret_cast<uintptr_t>(base) + off;
    if (!base || !fault::plausible_address(a)) return false;
    return fault::guarded(site, [&] { std::memcpy(out, reinterpret_cast<const void*>(a), sizeof(T)); });
}
bool rd_bytes(const void* p, uint8_t* out, size_t n) {
    KCDMP_FAULT_READ(site, "wo215::rd_bytes");
    if (!p || !fault::plausible_address(reinterpret_cast<uintptr_t>(p))) return false;
    return fault::guarded(site, [&] { std::memcpy(out, p, n); });
}
void* vslot(const void* obj, size_t off) {
    void* vt = nullptr; void* fn = nullptr;
    return obj && rd(obj, 0, &vt) && vt && rd(vt, off, &fn) ? fn : nullptr;
}
bool is_a(const void* obj, void* const* vft) { void* v = nullptr; return obj && vft && rd(obj, 0, &v) && v == static_cast<const void*>(vft); }

// ---------------------------------------------------------------------------------------------------- A.0: the movement request
// XGenAIModule, re-found on every start by what it is (docs/WO-215-findings.md A.0). The anchor is the debug door's worker
// QueueMovementRequest(C_NPC*, const Vec3* target, bool exact, const Vec3* dir): the one function that references
// "Request movement for NPC '%s'. Request id '%u'." (and "Movement request debug"). Out of its own code, each by its instruction bytes:
//   the request's ctor        48 8B D3 48 8D 4C 24 60 E8 <rel>          mov rdx,rbx(npc); lea rcx,[rsp+60h](req); call
//   the path request's base   48 8B CF E8 <rel> 48 8D 05 <rip> C6 87 8C 00 00 00 01   ctor; lea rax,[vftable]; mov byte [rdi+8Ch],1
//   the exact-position setter 4D 8B C4 48 8D 4C 24 60 48 8B D6 E8 <rel>  mov r8,r12(dir); lea rcx,[req]; mov rdx,rsi(target); call
//   the movement root         48 8B 0D <rip> 48 8B 01 FF 90 A8 00 00 00  mov rcx,[root]; call [rax+0A8h] -> [+0x110] = task manager
//   the queue                 48 8D 54 24 60 48 8B C8 E8 <rel>           lea rdx,[req]; mov rcx,rax(manager); call -> request id
//   the request's dtor        the last 48 8D 4C 24 60 E8 <rel> (lea rcx,[req]; call) of the worker
// and the stop: the console door wh_ai_CancelDebugMovement (the one function that references its own argument error text) ends in a
// tail jump to the manager's cancel by request id: 45 33 C9 45 33 C0 8B D3 48 8B C8 48 83 C4 60 5B E9 <rel>.
// Each target is checked by its own bytes before use (the ctor writes the speed default, +0xF8 = 1; the cancel clears the task's id;
// the dtor releases the debug name at +0x258). A build that moves any of it leaves the route unarmed: the writer moves every copy, as
// before, and the status says which piece was not found.
constexpr const char* kWorkerText = "Request movement for NPC '%s'. Request id '%u'.";
constexpr const char* kWorkerText2 = "Movement request debug";
constexpr const char* kCancelText = "Incorrect number of arguments specified. The expected argument is request id";
constexpr const char* kIncludeFlagsCVar = "wh_ai_FindPathIncludeFlags";
constexpr size_t kReqSize = 0x260;          // the request (its last member, the debug name, at +0x258)
constexpr size_t kReqPath = 0x08;           // the path request (owned by the request; the task clones it)
constexpr size_t kReqSpeed = 0xF8;          // the speed enum (wo215_rules.h speed_enum)
constexpr size_t kPathSize = 0xA0;          // S_PathFindingRequestDefault
constexpr size_t kNpcAgentType = 0x208;     // C_NPC vtable: the navigation agent type (int*)
constexpr size_t kNpcPosition = 0x50;       // C_NPC vtable: its position (Vec3*)
constexpr size_t kNpcWuid = 0x10;           // C_NPC: the soul's WUID
constexpr size_t kMgrNpcOf = 0x20;          // C_NPCManager vtable: the C_NPC of an entity id (the debug door's own lookup)
constexpr uint32_t kNoRequest = 0xFFFFFFFFu;
// The AI update (C_XGenAIModule::UpdateInternal, found by its profiler label): a job on a worker thread, where the engine's brains make
// their own movement requests. The route makes every request of its own there too (ai_drain, an entry hook): a9's joiner crashed in that
// update's navigation while the route, on the main thread, replaced a task's request -- freeing the path request the update was walking.
constexpr const char* kAiUpdateText = "wh::xgenaimodule::C_XGenAIModule::UpdateInternal";
void ai_update_cb();
std::atomic<bool> g_hooked{false};

struct Api {
    void* reqCtor = nullptr;
    void* reqDtor = nullptr;
    void* pathBase = nullptr;
    void* pathVft = nullptr;
    void* exactSet = nullptr;
    void** rootSlot = nullptr;
    void* queue = nullptr;
    void* cancel = nullptr;
    void* taskOf = nullptr;     // the manager's get-or-create of an NPC's task (the queue's own first call)
    void* taskCancel = nullptr; // the task's own cancel (the queue's, before a new request: no manager mutex)
    void* const* vftNpc = nullptr;
    void* const* vftNpcMgr = nullptr;
    bool armed = false;
    const char* why = "not installed";
} g_api;
std::atomic<bool> g_armed{false};

const uint8_t* find_seq(const uint8_t* code, size_t n, const uint8_t* pat, size_t pn, const uint8_t* mask, size_t from = 0) {
    for (size_t i = from; i + pn <= n; ++i) {
        bool ok = true;
        for (size_t k = 0; k < pn && ok; ++k) ok = !mask || mask[k] ? code[i + k] == pat[k] : true;
        if (ok) return code + i;
    }
    return nullptr;
}
const void* rel32_target(const uint8_t* base, const uint8_t* copy, const uint8_t* insnInCopy, size_t relOff, size_t insnEnd) {
    int32_t d = 0;
    std::memcpy(&d, insnInCopy + relOff, 4);
    return base + (insnInCopy - copy) + insnEnd + d;
}
bool body_has(const void* fn, const uint8_t* pat, size_t pn, size_t window) {
    uint8_t b[0x200]{};
    if (window > sizeof b || !rd_bytes(fn, b, window)) return false;
    return find_seq(b, window, pat, pn, nullptr) != nullptr;
}

void resolve() {
    HMODULE xg = GetModuleHandleA("XGenAIModule.dll");
    if (!xg) { g_api.why = "XGenAIModule.dll not loaded"; return; }
    anchor::Range text{};
    if (!anchor::section(xg, ".text", &text)) { g_api.why = "XGenAI .text not found"; return; }
    int n1 = 0, n2 = 0, n3 = 0;
    const uint8_t* worker = anchor::function_by_string(xg, kWorkerText, &n1);
    const uint8_t* worker2 = anchor::function_by_string(xg, kWorkerText2, &n2);
    const uint8_t* cancelDoor = anchor::function_by_string(xg, kCancelText, &n3);
    if (!worker || n1 != 1 || worker != worker2 || n2 != 1) { g_api.why = "the movement request's worker (its two texts) not found as one function"; return; }
    if (!cancelDoor || n3 != 1) { g_api.why = "the cancel door (its argument text) not found as one function"; return; }
    anchor::Range wr{};
    if (!anchor::function_range(xg, worker, &wr)) { g_api.why = "the worker's range unknown"; return; }
    // the worker is one primary function with chained fragments; its bytes up to the dtor call are within 0x900 of the entry
    static uint8_t code[0x900];
    const size_t avail = static_cast<size_t>(text.end - worker);
    const size_t n = avail < sizeof code ? avail : sizeof code;
    if (!rd_bytes(worker, code, n)) { g_api.why = "the worker unreadable"; return; }
    static const uint8_t pCtor[] = {0x48, 0x8B, 0xD3, 0x48, 0x8D, 0x4C, 0x24, 0x60, 0xE8};
    static const uint8_t pBase[] = {0x48, 0x8B, 0xCF, 0xE8, 0, 0, 0, 0, 0x48, 0x8D, 0x05, 0, 0, 0, 0, 0xC6, 0x87, 0x8C, 0x00, 0x00, 0x00, 0x01};
    static const uint8_t mBase[] = {1, 1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1};
    static const uint8_t pExact[] = {0x4D, 0x8B, 0xC4, 0x48, 0x8D, 0x4C, 0x24, 0x60, 0x48, 0x8B, 0xD6, 0xE8};
    static const uint8_t pRoot[] = {0x48, 0x8B, 0x0D, 0, 0, 0, 0, 0x48, 0x8B, 0x01, 0xFF, 0x90, 0xA8, 0x00, 0x00, 0x00, 0x48, 0x8B, 0x08,
                                    0x48, 0x8B, 0x91, 0x10, 0x01, 0x00, 0x00};
    static const uint8_t mRoot[] = {1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1};
    static const uint8_t pQueue[] = {0x48, 0x8D, 0x54, 0x24, 0x60, 0x48, 0x8B, 0xC8, 0xE8};
    // the dtor: lea rcx,[req]; call <dtor>; then the worker's own stack-cookie check (mov rcx,[rbp+1C0h]; xor rcx,rsp)
    static const uint8_t pDtor[] = {0x48, 0x8D, 0x4C, 0x24, 0x60, 0xE8, 0, 0, 0, 0, 0x48, 0x8B, 0x8D, 0xC0, 0x01, 0x00, 0x00, 0x48, 0x33, 0xCC};
    static const uint8_t mDtor[] = {1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1};
    const uint8_t* c = find_seq(code, n, pCtor, sizeof pCtor, nullptr);
    const uint8_t* b = find_seq(code, n, pBase, sizeof pBase, mBase);
    const uint8_t* x = find_seq(code, n, pExact, sizeof pExact, nullptr);
    const uint8_t* r = find_seq(code, n, pRoot, sizeof pRoot, mRoot);
    const uint8_t* q = r ? find_seq(code, n, pQueue, sizeof pQueue, nullptr, static_cast<size_t>(r - code)) : nullptr;
    const uint8_t* d = q ? find_seq(code, n, pDtor, sizeof pDtor, mDtor, static_cast<size_t>(q - code)) : nullptr;
    if (!c || !b || !x || !r || !q || !d) { g_api.why = "a piece of the worker's code not found (ctor/path/exact/root/queue/dtor)"; return; }
    g_api.reqCtor = const_cast<void*>(rel32_target(worker, code, c, 9, 13));
    g_api.pathBase = const_cast<void*>(rel32_target(worker, code, b, 4, 8));
    g_api.pathVft = const_cast<void*>(rel32_target(worker, code, b, 11, 15));
    g_api.exactSet = const_cast<void*>(rel32_target(worker, code, x, 12, 16));
    g_api.rootSlot = reinterpret_cast<void**>(const_cast<void*>(rel32_target(worker, code, r, 3, 7)));
    g_api.queue = const_cast<void*>(rel32_target(worker, code, q, 9, 13));
    g_api.reqDtor = const_cast<void*>(rel32_target(worker, code, d, 6, 10));
    // the cancel: the door's tail jump (the door's code is chained fragments: every fragment of its function is scanned)
    //   xor r9d,r9d; xor r8d,r8d; mov edx,ebx(id); mov rcx,rax(manager); add rsp,60h; pop rbx; jmp <cancel>
    static const uint8_t pCancel[] = {0x45, 0x33, 0xC9, 0x45, 0x33, 0xC0, 0x8B, 0xD3, 0x48, 0x8B, 0xC8, 0x48, 0x83, 0xC4, 0x60, 0x5B};
    static const uint8_t pJmp[] = {0xE9};
    const uint8_t* tail = anchor::function_find_sequence(xg, cancelDoor, pCancel, sizeof pCancel, pJmp, sizeof pJmp, 1);
    if (!tail) { g_api.why = "the cancel's tail jump not found in the door"; return; }
    {
        int32_t dd = 0;
        uint8_t t5[sizeof pCancel + 5]{};
        if (!rd_bytes(tail, t5, sizeof t5)) { g_api.why = "the cancel's tail unreadable"; return; }
        std::memcpy(&dd, t5 + sizeof pCancel + 1, 4);
        g_api.cancel = const_cast<uint8_t*>(tail + sizeof pCancel + 5 + dd);
    }
    // the NPC's task, out of the queue's own code: mov rdx,[r14](npc); mov rcx,rsi(manager); call <get-or-create>; mov rdi,rax;
    // cmp byte [rax+2A0h],1 -- the cancel by id is only safe for a request that is still the task's current one (the manager's lookup by id
    // answers a list head's garbage for a finished request: the a1 bench run's first cancel faulted)
    {
        static const uint8_t pTask[] = {0x49, 0x8B, 0x16, 0x48, 0x8B, 0xCE, 0xE8, 0, 0, 0, 0, 0x48, 0x8B, 0xF8, 0x80, 0xB8, 0xA0, 0x02, 0x00, 0x00, 0x01};
        static const uint8_t mTask[] = {1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1};
        uint8_t qc[0x60]{};
        if (!rd_bytes(g_api.queue, qc, sizeof qc)) { g_api.why = "the queue unreadable"; return; }
        const uint8_t* t = find_seq(qc, sizeof qc, pTask, sizeof pTask, mTask);
        if (!t) { g_api.why = "the queue's task lookup not found"; return; }
        g_api.taskOf = const_cast<void*>(rel32_target(static_cast<const uint8_t*>(g_api.queue), qc, t, 7, 11));
        // the task's own cancel: cmp dword [rax+10h],-1; mov [rsp+30h],rbx; je +8; mov rcx,rax; call <task cancel>
        static const uint8_t pTc[] = {0x83, 0x78, 0x10, 0xFF, 0x48, 0x89, 0x5C, 0x24, 0x30, 0x74, 0x08, 0x48, 0x8B, 0xC8, 0xE8};
        const uint8_t* tc = find_seq(qc, sizeof qc, pTc, sizeof pTc, nullptr);
        if (!tc) { g_api.why = "the queue's task cancel not found"; return; }
        g_api.taskCancel = const_cast<void*>(rel32_target(static_cast<const uint8_t*>(g_api.queue), qc, tc, 15, 19));
        static const uint8_t vTc[] = {0xC6, 0x87, 0xA0, 0x02, 0x00, 0x00, 0x01};   // task cancel: mov byte [rdi+2A0h],1 (its own busy flag)
        if (!text.contains(g_api.taskCancel) || !body_has(g_api.taskCancel, vTc, sizeof vTc, 0x80)) { g_api.why = "the task cancel does not set its busy flag"; return; }
        static const uint8_t vTask[] = {0xB9, 0xB0, 0x02, 0x00, 0x00};   // get-or-create: mov ecx,2B0h (a new task's size)
        if (!text.contains(g_api.taskOf) || !body_has(g_api.taskOf, vTask, sizeof vTask, 0x100)) { g_api.why = "the task lookup does not make 0x2B0-byte tasks"; return; }
    }
    // each target by its own bytes
    static const uint8_t vSpeed[] = {0xC7, 0x87, 0xF8, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00};   // ctor: mov dword [rdi+0F8h],1
    static const uint8_t vCancel[] = {0xC7, 0x43, 0x10, 0xFF, 0xFF, 0xFF, 0xFF};                   // cancel: mov dword [rbx+10h],-1
    static const uint8_t vDtor[] = {0x48, 0x8B, 0x89, 0x58, 0x02, 0x00, 0x00};                     // dtor: mov rcx,[rcx+258h]
    static const uint8_t vExact[] = {0xC6, 0x86, 0xBC, 0x00, 0x00, 0x00, 0x01};                    // setter: mov byte [rsi+0BCh],1
    if (!text.contains(g_api.reqCtor) || !body_has(g_api.reqCtor, vSpeed, sizeof vSpeed, 0xC0)) { g_api.why = "the request ctor does not set the speed default"; return; }
    if (!text.contains(g_api.cancel) || !body_has(g_api.cancel, vCancel, sizeof vCancel, 0xC0)) { g_api.why = "the cancel does not clear the task's id"; return; }
    if (!text.contains(g_api.reqDtor) || !body_has(g_api.reqDtor, vDtor, sizeof vDtor, 0x40)) { g_api.why = "the request dtor does not release the debug name"; return; }
    if (!text.contains(g_api.exactSet) || !body_has(g_api.exactSet, vExact, sizeof vExact, 0x110)) { g_api.why = "the exact setter does not set the exact flag"; return; }
    if (!text.contains(g_api.pathBase) || !text.contains(g_api.queue)) { g_api.why = "the path ctor or the queue is outside the code"; return; }
    g_api.vftNpc = anchor::find_vftable(xg, ".?AVC_NPC@xgenaimodule@wh@@", 0);
    g_api.vftNpcMgr = anchor::find_vftable(xg, ".?AVC_NPCManager@xgenaimodule@wh@@", 0);
    if (!g_api.vftNpc || !g_api.vftNpcMgr) { g_api.why = "C_NPC / C_NPCManager not found by RTTI"; return; }
    void* const* pv = anchor::find_vftable(xg, ".?AUS_PathFindingRequestDefault@navigation@xgenaimodule@wh@@", 0);
    if (!pv || static_cast<const void*>(pv) != g_api.pathVft) { g_api.why = "the path request's vftable is not S_PathFindingRequestDefault's"; return; }
    // every request on the AI update's own thread: its entry hooked (the expected bytes in hook_prologues.h; any difference refuses)
    int nu = 0;
    const uint8_t* upd = anchor::function_by_string(xg, kAiUpdateText, &nu);
    if (!upd || nu != 1) { g_api.why = "the AI update (its profiler label) not found as one function"; return; }
    if (!g_hooked.load()) {
        const char* hw = nullptr;
        if (!inlinehook::install(const_cast<uint8_t*>(upd), hookpro::kAiUpdateInternal, sizeof hookpro::kAiUpdateInternal, &ai_update_cb, &hw)) {
            g_api.why = "the AI update's entry hook was refused (its first bytes differ)";
            logf("WO215-ROUTE the AI update's hook refused: %s -- the route stays unarmed", hw ? hw : "?");
            return;
        }
        g_hooked = true;
    }
    g_api.armed = true;
    g_api.why = "armed";
    g_armed = true;
    char a[96], b2[96], c2[96], d2[96], e2[96], f2[96], g2[96], h2[96], i2[96];
    anchor::describe(worker, a, sizeof a); anchor::describe(g_api.queue, b2, sizeof b2);
    anchor::describe(g_api.cancel, c2, sizeof c2); anchor::describe(g_api.reqCtor, d2, sizeof d2);
    anchor::describe(g_api.reqDtor, e2, sizeof e2); anchor::describe(g_api.exactSet, f2, sizeof f2);
    anchor::describe(g_api.taskOf, g2, sizeof g2); anchor::describe(g_api.taskCancel, h2, sizeof h2);
    anchor::describe(g_api.pathBase, i2, sizeof i2);
    char j2[96];
    anchor::describe(upd, j2, sizeof j2);
    logf("WO215-ROUTE resolver: the movement request armed -- worker %s (by its texts), queue %s, request ctor %s, dtor %s, exact %s, "
         "path ctor %s, task lookup %s, the stop (the task's own cancel) %s; the manager's cancel %s found, not used; "
         "the path request is S_PathFindingRequestDefault by RTTI; every request made at the AI update's entry %s (its own thread)",
         a, b2, d2, e2, f2, i2, g2, h2, c2, j2);
}

int include_flags() {
    // the worker's own: 0xF, or the CVar when the game has it
    static int s_flags = -1;
    static double s_at = -1e9;
    const double t = now_s();
    if (s_flags >= 0 && t - s_at < 30.0) return s_flags;
    s_at = t;
    int v = 0xF;
    void* con = engine::console();
    void* get = con ? vslot(con, 0xB8) : nullptr;   // IConsole::GetCVar (the worker's own call: [[gEnv+0A8h]]+0B8h)
    void* cv = nullptr;
    KCDMP_FAULT_CALL(site, "wo215::GetCVar");
    if (get && fault::guarded(site, [&] { cv = reinterpret_cast<void* (*)(void*, const char*)>(get)(con, kIncludeFlagsCVar); }) && cv) {
        void* gi = vslot(cv, 0x10);   // ICVar::GetIVal (the worker's [rcx+10h])
        int iv = 0;
        KCDMP_FAULT_CALL(isite, "wo215::GetIVal");
        if (gi && fault::guarded(isite, [&] { iv = reinterpret_cast<int (*)(void*)>(gi)(cv); })) v = iv;
    }
    s_flags = v;
    return v;
}

void* npc_of(uint32_t eid) {
    void* mgr = wo141::x::npc_manager();
    if (!is_a(mgr, g_api.vftNpcMgr)) return nullptr;
    void* get = vslot(mgr, kMgrNpcOf);
    void* npc = nullptr;
    KCDMP_FAULT_CALL(site, "wo215::npc_of");
    if (!get || !fault::guarded(site, [&] { npc = reinterpret_cast<void* (*)(void*, const uint32_t*)>(get)(mgr, &eid); })) return nullptr;
    return is_a(npc, g_api.vftNpc) ? npc : nullptr;
}

void* task_manager() {
    void* root = nullptr;
    if (!g_api.rootSlot || !rd(g_api.rootSlot, 0, &root) || !root) return nullptr;
    void* f1 = vslot(root, 0xA8);
    void* a = nullptr;
    KCDMP_FAULT_CALL(s1, "wo215::movement_root");
    if (!f1 || !fault::guarded(s1, [&] { a = reinterpret_cast<void* (*)(void*)>(f1)(root); }) || !a) return nullptr;
    void* f2 = vslot(a, 0x110);
    void* m = nullptr;
    KCDMP_FAULT_CALL(s2, "wo215::task_manager");
    if (!f2 || !fault::guarded(s2, [&] { m = reinterpret_cast<void* (*)(void*)>(f2)(a); })) return nullptr;
    return m;
}

std::atomic<uint32_t> c_queued{0}, c_refused{0}, c_faults{0}, c_cancels{0};

void disarm(const char* why) {
    if (!g_armed.exchange(false)) return;
    g_api.armed = false;
    g_api.why = why;
    logf("WO215-ROUTE DISARMED -- %s: every copy is the writer's again", why);
}

// One request: the engine's own sequence (the worker's), with the speed class the route asks for. The task clones the path request, so
// ours lives on our stack and is destroyed (never freed by the engine) after the queue. Returns the manager's id, kNoRequest if refused.
uint32_t queue_request(void* npc, const float target[3], int speedEnum, bool exact, float yaw) {
    if (!g_api.armed || !npc) return kNoRequest;
    void* mgr = task_manager();
    if (!mgr) return kNoRequest;
    alignas(16) uint8_t req[kReqSize + 0x40];
    alignas(16) uint8_t path[kPathSize + 0x20];
    std::memset(req, 0, sizeof req);
    std::memset(path, 0, sizeof path);
    const float tgt[3] = {target[0], target[1], target[2]};
    const float dir[3] = {-std::sin(yaw), std::cos(yaw), 0.0f};   // CryEngine: yaw 0 faces +Y
    const int flags = include_flags();
    uint32_t id = kNoRequest;
    bool built = false;
    KCDMP_FAULT_CALL(site, "wo215::queue_request");
    const bool ok = fault::guarded(site, [&] {
        using Ctor = void* (*)(void*, void*);
        using Base = void* (*)(void*);
        using Setter = void (*)(void*, const float*, const float*);
        using Queue = uint32_t (*)(void*, void*);
        using Dtor = void (*)(void*);
        reinterpret_cast<Ctor>(g_api.reqCtor)(req, npc);
        reinterpret_cast<Base>(g_api.pathBase)(path);
        built = true;
        *reinterpret_cast<void**>(path) = g_api.pathVft;
        path[0x8C] = 1;
        std::memset(path + 0x90, 0, 8);
        std::memset(path + 0x98, 0, 4);
        void* fAgent = *reinterpret_cast<void**>(*reinterpret_cast<uint8_t**>(npc) + kNpcAgentType);
        int tmp = 0;
        const int* agent = reinterpret_cast<const int* (*)(void*, int*)>(fAgent)(npc, &tmp);
        const int32_t at = agent ? *agent : 0;
        std::memcpy(path + 0x08, &at, 4);
        const int32_t fl = flags;
        std::memcpy(path + 0x64, &fl, 4);
        void* fPos = *reinterpret_cast<void**>(*reinterpret_cast<uint8_t**>(npc) + kNpcPosition);
        const float* pos = reinterpret_cast<const float* (*)(void*)>(fPos)(npc);
        if (pos) std::memcpy(path + 0x58, pos, 12);
        std::memcpy(path + 0x80, tgt, 12);
        std::memcpy(path + 0x70, static_cast<const uint8_t*>(npc) + kNpcWuid, 8);
        void* pp = path;
        std::memcpy(req + kReqPath, &pp, 8);
        const int32_t sp = speedEnum;
        std::memcpy(req + kReqSpeed, &sp, 4);
        if (exact) reinterpret_cast<Setter>(g_api.exactSet)(req, tgt, dir);
        id = reinterpret_cast<Queue>(g_api.queue)(mgr, req);
        // ours, not the engine's: detach it, destroy it in place (the scalar destructor without the free), then the request
        void* none = nullptr;
        std::memcpy(req + kReqPath, &none, 8);
        void* fDel = *reinterpret_cast<void**>(*reinterpret_cast<uint8_t**>(path));
        reinterpret_cast<void (*)(void*, uint32_t)>(fDel)(path, 0);
        reinterpret_cast<Dtor>(g_api.reqDtor)(req);
    });
    if (!ok) {
        c_faults.fetch_add(1, std::memory_order_relaxed);
        disarm(built ? "a fault inside the movement request (queue)" : "a fault inside the movement request (build)");
        return kNoRequest;
    }
    if (id == kNoRequest) c_refused.fetch_add(1, std::memory_order_relaxed);
    else c_queued.fetch_add(1, std::memory_order_relaxed);
    return id;
}

std::atomic<uint32_t> c_cancelStale{0};
// The stop: the manager's cancel of our request -- only while it is still the NPC's current one (task +0x10); a request that already ended
// (the body arrived, or the task took another) needs none, and the manager's lookup of a stale id is not safe.
bool cancel_request(void* npc, uint32_t id) {
    if (!g_api.armed || id == kNoRequest || !npc) return false;
    void* mgr = task_manager();
    if (!mgr) return false;
    void* task = nullptr;
    KCDMP_FAULT_CALL(tsite, "wo215::task_of");
    if (!fault::guarded(tsite, [&] { task = reinterpret_cast<void* (*)(void*, void*)>(g_api.taskOf)(mgr, npc); }) || !task) return false;
    uint32_t cur = kNoRequest;
    if (!rd(task, 0x10, &cur) || cur != id) {
        const uint32_t k = c_cancelStale.fetch_add(1, std::memory_order_relaxed) + 1;
        if (k <= 30 || k % 200 == 0) logf("WO215-CANCEL stale: the task's current request is %d, ours was %u -- nothing to cancel [n=%u]", static_cast<int>(cur), id, k);
        return false;
    }
    // the task's own cancel (what the queue runs before a new request): no manager mutex is taken, so no fault inside it can leave one held
    // (the a1 bench run: the manager's cancel faulted on a stale id inside its mutex, and the next frame's movement update hung on it)
    KCDMP_FAULT_CALL(site, "wo215::cancel_request");
    const bool ok = fault::guarded(site, [&] { reinterpret_cast<void (*)(void*)>(g_api.taskCancel)(task); });
    if (!ok) { c_faults.fetch_add(1); disarm("a fault inside the movement cancel"); return false; }
    c_cancels.fetch_add(1, std::memory_order_relaxed);
    return true;
}

// ---------------------------------------------------------------------------------------------------- the AI update's own thread
// The main thread (the route) and the probe post the newest order per body; the AI update's entry carries them out on its own thread,
// before the update runs -- the engine's brains make their requests in the same update. Posting keeps one order per body (a newer one
// replaces it: a request cancels the task's current one by itself), so a paused AI update never piles orders up.
enum : uint8_t { kOrdWalk = 1, kOrdExact = 2, kOrdCancel = 3 };
enum : uint8_t { kOutQueued = 1, kOutRefused = 2, kOutNoNpc = 3 };
struct Order { uint8_t kind = 0; float target[3]{}; int speed = 1; float yaw = 0; uint32_t seq = 0; };
struct Outcome { uint32_t seq = 0; uint32_t id = kNoRequest; uint8_t what = 0; };
std::mutex g_ordMu;                                // guards g_orders and g_outcomes only (never held across an engine call)
std::unordered_map<uint32_t, Order> g_orders;      // eid -> the newest order not yet carried out
std::unordered_map<uint32_t, Outcome> g_outcomes;  // eid -> the outcome of the newest order carried out (a request's, not a cancel's)
std::atomic<bool> g_ordersPending{false};
std::atomic<uint32_t> g_ordSeq{0}, c_aiTicks{0}, c_drained{0}, c_noNpcAi{0};
std::unordered_map<uint32_t, uint32_t> g_ourId;    // eid -> the request id the route last got (the AI update's thread only)

uint32_t post_order(uint32_t eid, uint8_t kind, const float target[3], int speed, float yaw) {
    Order o;
    o.kind = kind;
    if (target) std::memcpy(o.target, target, 12);
    o.speed = speed;
    o.yaw = yaw;
    o.seq = g_ordSeq.fetch_add(1, std::memory_order_relaxed) + 1;
    {
        std::lock_guard<std::mutex> l(g_ordMu);
        g_orders[eid] = o;
    }
    g_ordersPending.store(true, std::memory_order_release);
    return o.seq;
}

bool take_outcome(uint32_t eid, Outcome* out) {
    std::lock_guard<std::mutex> l(g_ordMu);
    auto it = g_outcomes.find(eid);
    if (it == g_outcomes.end()) return false;
    *out = it->second;
    g_outcomes.erase(it);
    return true;
}

void ai_drain() {
    c_aiTicks.fetch_add(1, std::memory_order_relaxed);
    if (!g_ordersPending.exchange(false, std::memory_order_acq_rel)) return;
    std::unordered_map<uint32_t, Order> work;
    {
        std::lock_guard<std::mutex> l(g_ordMu);
        work.swap(g_orders);
    }
    for (const auto& kv : work) {
        const uint32_t eid = kv.first;
        const Order& o = kv.second;
        Outcome oc;
        oc.seq = o.seq;
        void* npc = g_armed.load(std::memory_order_relaxed) ? npc_of(eid) : nullptr;
        if (!g_armed.load(std::memory_order_relaxed)) {
            oc.what = kOutRefused;
        } else if (!npc) {
            oc.what = kOutNoNpc;
            g_ourId.erase(eid);
            c_noNpcAi.fetch_add(1, std::memory_order_relaxed);
        } else if (o.kind == kOrdCancel) {
            auto it = g_ourId.find(eid);
            if (it != g_ourId.end()) { cancel_request(npc, it->second); g_ourId.erase(it); }
        } else {
            const uint32_t id = queue_request(npc, o.target, o.speed, o.kind == kOrdExact, o.yaw);
            oc.id = id;
            oc.what = id == kNoRequest ? kOutRefused : kOutQueued;
            if (id != kNoRequest) g_ourId[eid] = id; else g_ourId.erase(eid);
        }
        c_drained.fetch_add(1, std::memory_order_relaxed);
        if (o.kind != kOrdCancel) {
            std::lock_guard<std::mutex> l(g_ordMu);
            g_outcomes[eid] = oc;
        }
    }
}

void ai_update_cb() {
    try { ai_drain(); } catch (...) {}   // never an exception into the engine's job
}

// ---------------------------------------------------------------------------------------------------- A.1-A.3: the route
struct Route {
    uint32_t eid = 0;
    std::string name;
    uint32_t reqId = kNoRequest;
    bool active = false;        // a request of ours runs on this body
    bool exact = false;         // ... and it is the standing one (the face)
    double lastOrderAt = -1e9;
    float orderPos[3]{};
    float faceYaw = 0;
    Gait gait = Gait::Walk;
    double stillSince = 0;
    double refusedUntil = 0;
    uint32_t refusals = 0;
    bool correcting = false;
    uint32_t corrections = 0;
    uint32_t orders = 0, faces = 0;
    double lastSeenAt = 0;
    // A.2's gait meter: the body's own speed against the stream's, over half-second windows while it moves in the near band
    float meterPos[3]{};
    double meterAt = -1;
    double meterStreamSum = 0;
    uint32_t meterStreamN = 0;
    WriterWhy lastWhy = WriterWhy::Off;
    float gaitEma = -1.0f;      // A.2: the host's speed, smoothed (gait_ema)
    bool inStall = false;       // a link stall holds this body's request in flight (link_stall)
    uint32_t pendingSeq = 0;    // the newest request posted to the AI update and not yet answered (0 = none)
};
std::unordered_map<uint32_t, Route> g_routes;
std::atomic<uint32_t> c_routedFrames{0}, c_writerFrames[static_cast<int>(WriterWhy::kCount)]{}, c_corrections{0}, c_orders{0}, c_faces{0},
    c_releases{0}, c_gaitChanges{0}, c_noNpc{0}, c_stallHolds{0};
std::atomic<uint32_t> c_routedBodyMs{0};   // the routed bodies' time, summed (ms; wraps after 49 days of body-time) -- corrections per body-minute
std::atomic<uint32_t> c_meterN{0}, c_meterOk{0};
std::atomic<uint32_t> c_meterRel[21]{};   // |render - stream| / stream in 5 % bins (the last: >= 100 %)
std::atomic<uint32_t> c_gaitOrders[kGaits]{};

float dist2d(const float* a, const float* b) { const float dx = a[0] - b[0], dy = a[1] - b[1]; return std::sqrt(dx * dx + dy * dy); }

bool player_pos(float out[3]) {
    void* e = engine::entity_by_id(0x7777);   // the local player's entity id (WO-125)
    return e && engine::entity_world_pos(e, out);
}

void release_route(Route& r, const char* why) {
    if (r.active || r.pendingSeq) post_order(r.eid, kOrdCancel, nullptr, 0, 0.0f);   // carried out at the AI update's entry
    r.pendingSeq = 0;
    if (r.active) {
        c_releases.fetch_add(1, std::memory_order_relaxed);
        const uint32_t k = c_releases.load();
        if (k <= 40 || k % 200 == 0) logf("WO215-ROUTE npc=%s released (%s): its request cancelled, the writer has it [n=%u]", r.name.c_str(), why, k);
    }
    r.active = false;
    r.exact = false;
    r.reqId = kNoRequest;
    r.correcting = false;
    r.meterAt = -1;
}

void meter(Route& r, const RouteIn& in, const float body[3]) {
    // the near band only (WO-215 A.2: bodies within 40 m of this player), while the stream moves
    float me[3];
    if (!player_pos(me) || dist2d(me, body) > 40.0f || in.streamMps < kMovingMps) { r.meterAt = -1; r.meterStreamN = 0; r.meterStreamSum = 0; return; }
    if (r.meterAt < 0) { std::memcpy(r.meterPos, body, 12); r.meterAt = in.now; r.meterStreamSum = 0; r.meterStreamN = 0; return; }
    r.meterStreamSum += in.streamMps;
    ++r.meterStreamN;
    const double dt = in.now - r.meterAt;
    if (dt < kMeterWindowS) return;
    const float renderMps = dist2d(body, r.meterPos) / static_cast<float>(dt);
    const float streamMps = r.meterStreamN ? static_cast<float>(r.meterStreamSum / r.meterStreamN) : in.streamMps;
    if (streamMps >= kMovingMps && renderMps < 12.0f) {
        const float rel = std::fabs(renderMps - streamMps) / streamMps;
        int bin = static_cast<int>(rel * 20.0f);
        if (bin > 20) bin = 20;
        c_meterRel[bin].fetch_add(1, std::memory_order_relaxed);
        c_meterN.fetch_add(1, std::memory_order_relaxed);
        if (rel < 0.15f) c_meterOk.fetch_add(1, std::memory_order_relaxed);
    }
    std::memcpy(r.meterPos, body, 12);
    r.meterAt = in.now;
    r.meterStreamSum = 0;
    r.meterStreamN = 0;
}

// ---------------------------------------------------------------------------------------------------- A.0's probe
struct Probe {
    bool on = false;
    uint32_t eid = 0;
    std::string name;
    int mode = 0;
    float start[3]{}, dir[2]{};
    float speed = 0;
    double until = 0, startAt = 0, lastLine = 0;
    Gait gait = Gait::Walk;
    uint32_t orders = 0;
    uint32_t reqId = kNoRequest;
} g_probe;

void probe_line(double now) {
    void* e = engine::entity_by_id(g_probe.eid);
    float p[3]{};
    if (!e || !engine::entity_world_pos(e, p)) return;
    float tgt[3] = {g_probe.start[0], g_probe.start[1], g_probe.start[2]};
    if (g_probe.mode == 3) {
        const float s = g_probe.speed * static_cast<float>(now - g_probe.startAt);
        tgt[0] += g_probe.dir[0] * s; tgt[1] += g_probe.dir[1] * s;
    }
    logf("WO215-PROBE npc=%s t=%.3f mode=%d pos=(%.3f,%.3f,%.3f) target=(%.3f,%.3f,%.3f) dist_m=%.3f orders=%u req=%u",
         g_probe.name.c_str(), now - g_probe.startAt, g_probe.mode, p[0], p[1], p[2], tgt[0], tgt[1], tgt[2], dist2d(p, tgt), g_probe.orders,
         g_probe.reqId);
}

void probe_tick(double now) {
    if (!g_probe.on) return;
    if (now >= g_probe.until) {
        if (g_probe.mode == 3) post_order(g_probe.eid, kOrdCancel, nullptr, 0, 0.0f);
        logf("WO215-PROBE npc=%s done after %.1f s, %u orders", g_probe.name.c_str(), now - g_probe.startAt, g_probe.orders);
        g_probe = Probe{};
        return;
    }
    if (g_probe.mode == 3) {
        const float s = g_probe.speed * static_cast<float>(now - g_probe.startAt);
        const float tgt[3] = {g_probe.start[0] + g_probe.dir[0] * s, g_probe.start[1] + g_probe.dir[1] * s, g_probe.start[2]};
        g_probe.reqId = post_order(g_probe.eid, kOrdWalk, tgt, speed_enum(g_probe.gait), 0.0f);   // the order's number (the AI update makes it)
        ++g_probe.orders;
    }
    if (now - g_probe.lastLine >= 0.1) { g_probe.lastLine = now; probe_line(now); }
}

uint8_t probe_op(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    // [mode:1][nameLen:1][name][x y z yaw:4f][gait:1][secs:2]
    *outLen = 0;
    if (len < 2 || cap < 5) return wo163::kRBadRequest;
    const uint8_t mode = body[0], nl = body[1];
    if (nl == 0 || nl > 63 || len != 2u + nl + 16u + 1u + 2u) return wo163::kRBadRequest;
    char name[64]{};
    std::memcpy(name, body + 2, nl);
    float v[4];
    std::memcpy(v, body + 2 + nl, 16);
    const uint8_t g = body[2 + nl + 16];
    uint16_t secs = 0;
    std::memcpy(&secs, body + 2 + nl + 17, 2);
    void* e = wo141::x::entity_named(name);
    const uint32_t eid = e ? engine::entity_id(e) : 0;
    uint32_t id = kNoRequest;
    const char* why = "ok";
    if (!g_api.armed) why = g_api.why;
    else if (!eid) why = "no entity of that name";
    else if (mode == 1 || mode == 3) {
        const Gait gait = static_cast<Gait>((g & 0x7F) < kGaits ? (g & 0x7F) : 0);
        const double now = now_s();
        // the body leaves every writer for the probe's run (the main thread releases its route: the probe's body is "off" there)
        g_probe = Probe{};
        g_probe.on = true; g_probe.eid = eid; g_probe.name = name; g_probe.mode = mode; g_probe.gait = gait;
        g_probe.startAt = now; g_probe.until = now + (secs ? secs : 15);
        std::memcpy(g_probe.start, v, 12);
        g_probe.dir[0] = -std::sin(v[3]); g_probe.dir[1] = std::cos(v[3]);
        g_probe.speed = mode == 3 ? (gait == Gait::Walk ? 1.4f : gait == Gait::Jog ? 2.6f : gait == Gait::Run ? 3.0f : 4.5f) : 0.0f;
        if (mode == 1) {
            id = post_order(eid, (g & 0x80) ? kOrdExact : kOrdWalk, v, speed_enum(gait), v[3]);   // the order's number
            g_probe.reqId = id;
            ++g_probe.orders;
        }
        logf("WO215-PROBE npc=%s eid=0x%X mode=%d gait=%s exact=%d target=(%.2f,%.2f,%.2f) yaw=%.2f secs=%u -> id=%u (%s)", name, eid, mode,
             gait_name(gait), (g & 0x80) ? 1 : 0, v[0], v[1], v[2], v[3], secs, id, why);
    } else if (mode == 2) {
        post_order(eid, kOrdCancel, nullptr, 0, 0.0f);
        logf("WO215-PROBE npc=%s stop (the task's own cancel, at the AI update's entry; order %u was the last)", name, g_probe.reqId);
        id = g_probe.reqId;
    } else if (mode == 4) {
        post_order(eid, kOrdCancel, nullptr, 0, 0.0f);
        logf("WO215-PROBE npc=%s released to the writer", name);
        g_probe = Probe{};
    } else return wo163::kRBadRequest;
    out[0] = std::strcmp(why, "ok") == 0 ? 1 : 0;
    std::memcpy(out + 1, &id, 4);
    size_t k = 5;
    const size_t wl = std::strlen(why);
    if (cap > k) { const size_t m = (std::min)(wl, cap - k); std::memcpy(out + k, why, m); k += m; }
    *outLen = k;
    return wo163::kROk;
}

// ---------------------------------------------------------------------------------------------------- ops
uint8_t op(uint8_t opc, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (opc == kOpConfig) {
        if (len != 5 || cap < 4) return wo163::kRBadRequest;
        uint32_t bits = 0;
        std::memcpy(&bits, body + 1, 4);
        const uint32_t was = g_bits.exchange(bits);
        if (was != bits || !g_configured.exchange(true))
            logf("WO215-CFG bits=0x%X (was 0x%X; twin off 0x%X): route=%d gait=%d ladder=%d letgo_cull=%d activity_line=%d dormant=%d "
                 "ghost_fight=%d probe_all=%d", bits, was, twin_off(), on(kBitRoute), on(kBitGait), on(kBitLadder), on(kBitLetGoCull),
                 on(kBitActivityLine), on(kBitDormant), on(kBitGhostFight), on(kBitProbeAll));
        const uint32_t eff = bits & ~twin_off();
        std::memcpy(out, &eff, 4);
        *outLen = 4;
        return wo163::kROk;
    }
    if (opc == kOpStatus) {
        char t[2000];
        int n = status(t, sizeof t);
        if (n < 0) n = 0;
        if (static_cast<size_t>(n) >= sizeof t) n = static_cast<int>(sizeof t) - 1;
        if (static_cast<size_t>(n) > cap) n = static_cast<int>(cap);
        std::memcpy(out, t, static_cast<size_t>(n));
        *outLen = static_cast<size_t>(n);
        return wo163::kROk;
    }
    if (opc == kOpProbe) return probe_op(body + 1, len - 1, out, cap, outLen);
    if (opc == kOpTune) {
        if ((len != 4 && len != 12 && len != 14) || cap < 13) return wo163::kRBadRequest;
        uint16_t cm = 0;
        std::memcpy(&cm, body + 1, 2);
        if (cm >= 20 && cm <= 2000) g_correctCm = cm;
        if (body[3] != 0xFF) g_perFrame = body[3] != 0;
        if (len == 12) {
            uint16_t v[4];
            std::memcpy(v, body + 4, 8);
            if (v[0] != 0xFFFF && v[0] <= 5000) g_cad.minS = v[0] / 1000.0;
            if (v[1] != 0xFFFF && v[1] <= 1000) g_cad.stepM = v[1] / 100.0f;
            if (v[2] != 0xFFFF && v[2] <= 10000) g_cad.maxS = v[2] / 1000.0;
            if (v[3] != 0xFFFF && v[3] <= 5000) g_cad.leadS = v[3] / 1000.0;
        }
        if (len == 14) {
            uint16_t ap = 0;
            std::memcpy(&ap, body + 12, 2);
            if (ap != 0xFFFF && ap <= 1000) g_cad.approachM = ap / 100.0f;
        }
        const uint16_t now = static_cast<uint16_t>(g_correctCm.load());
        std::memcpy(out, &now, 2);
        out[2] = g_perFrame.load() ? 1 : 0;
        const uint16_t o[4] = {static_cast<uint16_t>(g_cad.minS * 1000.0 + 0.5), static_cast<uint16_t>(g_cad.stepM * 100.0f + 0.5f),
                               static_cast<uint16_t>(g_cad.maxS * 1000.0 + 0.5), static_cast<uint16_t>(g_cad.leadS * 1000.0 + 0.5)};
        std::memcpy(out + 3, o, 8);
        const uint16_t apc = static_cast<uint16_t>(g_cad.approachM * 100.0f + 0.5f);
        std::memcpy(out + 11, &apc, 2);
        *outLen = 13;
        logf("WO215-CFG tune: the corrector at %.2f m, per-frame orders %s, cadence min %.2f s step %.2f m max %.2f s lead %.2f s approach %.2f m", now / 100.0,
             g_perFrame.load() ? "on" : "off", g_cad.minS, g_cad.stepM, g_cad.maxS, g_cad.leadS, g_cad.approachM);
        return wo163::kROk;
    }
    return wo163::kRBadRequest;
}

void install_reg() { resolve(); }
void tick_reg() { tick(); }
int status_reg(char* out, int n) { return status(out, n); }
const bool kReg = wo203::reg_op(kOpConfig, &op) && wo203::reg_op(kOpStatus, &op) && wo203::reg_op(kOpProbe, &op) &&
                  wo203::reg_op(kOpTune, &op) && wo203::reg_install("w215", &install_reg) && wo203::reg_tick("w215", &tick_reg) &&
                  wo203::reg_status("w215", &status_reg);

double g_lastLine = 0;

// ---------------------------------------------------------------------------------------------------- D.1 the census (host)
struct Census {
    bool dormant = false;
    double checkAt = -1e9;
    double ruleSince = -1;   // since when the dormant rule holds (hysteresis: 2 s to go dormant, at once to wake)
    double since = 0;
    bool fbEmpty = false, physics = false, brainRun = false;
};
std::unordered_map<uint32_t, Census> g_census;
std::atomic<uint32_t> c_dormantNow{0}, c_wentDormant{0}, c_woke{0}, c_dormantSkips{0};
constexpr double kCensusEveryS = 0.5, kDormantAfterS = 2.0;

std::unordered_map<uint32_t, std::string> g_ghosts;   // D.1 (joiner): copies whose combat actor the engine could not set up
std::atomic<uint32_t> c_ghosts{0};

} // namespace

bool on(uint32_t bit) { return (g_bits.load(std::memory_order_relaxed) & ~twin_off() & bit) != 0; }
bool route_armed() { return g_armed.load(std::memory_order_relaxed); }

RouteOut route_frame(const RouteIn& in) {
    RouteOut out;
    static double s_frameAt = -1;
    static int s_used = 0;
    if (in.now != s_frameAt) { s_frameAt = in.now; s_used = 0; }   // one writer frame: every body of it shares its clock
    Route& r = g_routes[in.eid];
    if (r.eid != in.eid) { r = Route{}; r.eid = in.eid; r.name = in.name ? in.name : "?"; }
    const double dtSeen = r.lastSeenAt > 0 ? in.now - r.lastSeenAt : 0.0;
    r.lastSeenAt = in.now;
    // the AI update's answer to the newest request (made on its own thread): refused or no C_NPC -> the writer has it for 5 s
    if (r.pendingSeq) {
        Outcome oc;
        if (take_outcome(in.eid, &oc) && oc.seq == r.pendingSeq) {
            r.pendingSeq = 0;
            if (oc.what == kOutQueued) {
                r.reqId = oc.id;
            } else {
                r.refusedUntil = in.now + kRefusedRestS;
                ++r.refusals;
                r.active = false;
                r.exact = false;
                r.reqId = kNoRequest;
                if (oc.what == kOutNoNpc) c_noNpc.fetch_add(1, std::memory_order_relaxed);
                const uint32_t k = c_refused.load();
                if (r.refusals <= 3 && (k <= 60 || k % 100 == 0))
                    logf("WO215-ROUTE npc=%s the engine %s: the writer has it for %.0f s [body n=%u, all refused n=%u]", r.name.c_str(),
                         oc.what == kOutNoNpc ? "has no C_NPC for it" : "refused the request (no id)", kRefusedRestS, r.refusals, k);
            }
        }
    }
    if (!in.stall) r.gaitEma = gait_ema(r.gaitEma, in.streamMps, static_cast<float>(dtSeen));   // a stall says nothing of the host's pace
    const bool air = std::fabs(in.streamVz) > kAirVzMps;
    WriterWhy why = writer_reason(on(kBitRoute), in.figure, in.beast, in.parented, air, in.fight, in.now < r.refusedUntil);
    if (why == WriterWhy::None && !in.human) why = WriterWhy::NotHuman;
    if (why == WriterWhy::None && !in.physics) why = WriterWhy::NoPhysics;
    if (why == WriterWhy::None && !g_armed.load(std::memory_order_relaxed)) why = WriterWhy::NotArmed;
    if (why == WriterWhy::None && g_probe.on && g_probe.eid == in.eid) why = WriterWhy::Off;
    if (why != WriterWhy::None) {
        if (r.active || r.pendingSeq) {
            if (g_probe.on && g_probe.eid == in.eid) {   // the probe's body: its own requests stand (no cancel of ours over them)
                r.active = false; r.exact = false; r.pendingSeq = 0; r.reqId = kNoRequest; r.correcting = false;
            } else {
                release_route(r, writer_why(why));
            }
        }
        c_writerFrames[static_cast<int>(why)].fetch_add(1, std::memory_order_relaxed);
        r.lastWhy = why;
        out.v = Verdict::Writer;
        out.why = why;
        return out;
    }
    float body[3];
    if (!in.ent || !engine::entity_world_pos(in.ent, body)) {
        c_writerFrames[static_cast<int>(WriterWhy::NoNpc)].fetch_add(1, std::memory_order_relaxed);
        out.v = Verdict::Writer; out.why = WriterWhy::NoNpc;
        return out;
    }
    // a link stall (every stream quiet): the request in flight plays out -- no stop at the last sample (a led walker has passed it and
    // would turn and walk back), no correction against a sample the link holds back; the next sample decides again
    if (in.stall && r.active) {
        if (!r.inStall) { r.inStall = true; c_stallHolds.fetch_add(1, std::memory_order_relaxed); }
        c_routedFrames.fetch_add(1, std::memory_order_relaxed);
        out.v = Verdict::Routed;
        out.why = WriterWhy::None;
        return out;
    }
    r.inStall = false;
    // the corrector: where the stream says against where the body is -- for a walking copy, its distance to the way from the rendered
    // sample to the (led) target it was given; for a standing one, to the rendered sample
    const float off = (r.active && !r.exact) ? dist_to_segment(body, in.render, r.orderPos) : dist2d(body, in.render);
    const float thr = static_cast<float>(g_correctCm.load(std::memory_order_relaxed)) / 100.0f;
    if (r.correcting) {
        if (off <= kCorrectBackM) r.correcting = false;
        else { c_writerFrames[static_cast<int>(WriterWhy::Correcting)].fetch_add(1, std::memory_order_relaxed); out.v = Verdict::Correct; out.why = WriterWhy::Correcting; return out; }
    } else if (r.active && needs_correction(off, thr)) {
        r.correcting = true;
        ++r.corrections;
        const uint32_t k = c_corrections.fetch_add(1, std::memory_order_relaxed) + 1;
        if (k <= 200 || k % 100 == 0)
            logf("WO215-CORRECT npc=%s %.2f m from where the stream says (over %.2f m): blended back by the writer, never snapped; its request "
                 "cancelled, a new one from where the blend ends [body n=%u, all n=%u]", r.name.c_str(), off, thr, r.corrections, k);
        // the writer alone moves it back: the request in flight is cancelled, so the engine's movement and the writer never pull one body
        // two ways (a6: a hand-back after such a tug was a single-frame pull of 0.5-2.4 m); the route starts again from where the blend ends
        post_order(in.eid, kOrdCancel, nullptr, 0, 0.0f);
        r.pendingSeq = 0;
        r.active = false;
        r.exact = false;
        r.reqId = kNoRequest;
        c_writerFrames[static_cast<int>(WriterWhy::Correcting)].fetch_add(1, std::memory_order_relaxed);
        out.v = Verdict::Correct; out.why = WriterWhy::Correcting;
        return out;
    }
    const bool moving = in.streamMps >= kMovingMps;
    const bool perFrame = g_perFrame.load(std::memory_order_relaxed);
    if (moving) {
        r.stillSince = 0;
        const Gait g = on(kBitGait) ? gait_for(r.gaitEma, r.gait) : Gait::Walk;
        const bool gaitChanged = r.active && !r.exact && g != r.gait;
        float tgt[3];
        lead_target(in.newest, in.streamVx, in.streamVy, lead_for(g_cad.leadS, in.gapS), tgt);
        const float moved = dist2d(tgt, r.orderPos);
        const bool due = order_due(perFrame, in.now - r.lastOrderAt, moved, gaitChanged, !r.active || r.exact, g_cad,
                                   r.active && !r.exact ? dist2d(body, r.orderPos) : -1.0f);
        if (due && !budget_left(s_used) && !r.active) {
            c_writerFrames[static_cast<int>(WriterWhy::Budget)].fetch_add(1, std::memory_order_relaxed);
            out.v = Verdict::Writer; out.why = WriterWhy::Budget;
            return out;
        }
        if (due && budget_left(s_used)) {
            ++s_used;
            // posted: the AI update makes it on its own thread at its next entry (its answer comes back as an outcome)
            r.pendingSeq = post_order(in.eid, kOrdWalk, tgt, speed_enum(g), in.newest[3]);
            if (gaitChanged) c_gaitChanges.fetch_add(1, std::memory_order_relaxed);
            c_gaitOrders[static_cast<int>(g)].fetch_add(1, std::memory_order_relaxed);
            if (!r.active || r.exact) {
                const uint32_t k = c_orders.load() + 1;
                if (k <= 40 || k % 500 == 0)
                    logf("WO215-ROUTE npc=%s moves by the engine's request: %s (%.2f m/s on the stream) to (%.1f,%.1f,%.1f) order=%u",
                         r.name.c_str(), gait_name(g), in.streamMps, in.newest[0], in.newest[1], in.newest[2], r.pendingSeq);
            }
            r.active = true; r.exact = false; r.gait = g;
            r.lastOrderAt = in.now;
            std::memcpy(r.orderPos, tgt, 12);
            ++r.orders;
            c_orders.fetch_add(1, std::memory_order_relaxed);
        }
    } else {
        if (r.stillSince == 0) r.stillSince = in.now;
        if (!r.active && in.now - r.stillSince < kStillS) {   // not routed yet and not yet still for long: the writer holds it where it is
            c_writerFrames[static_cast<int>(WriterWhy::Off)].fetch_add(1, std::memory_order_relaxed);
            out.v = Verdict::Writer; out.why = WriterWhy::Off;
            return out;
        }
        if (in.now - r.stillSince >= kStillS) {
            // standing: one request to the host's spot that faces the host's heading (the engine walks the last bit, stops with its own
            // stop transition and turns; a body already there and facing that way is left as it is by the task's own check) -- again only
            // when the host turned or moved on
            const bool sent = r.active && r.exact;
            const bool due = face_due(sent, in.now - r.lastOrderAt, sent ? wrap_pi(in.newest[3] - r.faceYaw) : 0.0f,
                                      sent ? dist2d(in.newest, r.orderPos) : 0.0f);
            if (due && !budget_left(s_used) && !r.active) {
                c_writerFrames[static_cast<int>(WriterWhy::Budget)].fetch_add(1, std::memory_order_relaxed);
                out.v = Verdict::Writer; out.why = WriterWhy::Budget;
                return out;
            }
            if (due && budget_left(s_used)) {
                ++s_used;
                r.pendingSeq = post_order(in.eid, kOrdExact, in.newest, speed_enum(Gait::Walk), in.newest[3]);
                r.active = true; r.exact = true;
                r.lastOrderAt = in.now;
                std::memcpy(r.orderPos, in.newest, 12);
                r.faceYaw = in.newest[3];
                ++r.faces;
                c_faces.fetch_add(1, std::memory_order_relaxed);
            }
        }
    }
    meter(r, in, body);
    c_routedFrames.fetch_add(1, std::memory_order_relaxed);
    if (dtSeen > 0.0 && dtSeen < 0.5) c_routedBodyMs.fetch_add(static_cast<uint32_t>(dtSeen * 1000.0 + 0.5), std::memory_order_relaxed);
    r.lastWhy = WriterWhy::None;
    out.v = Verdict::Routed;
    out.why = WriterWhy::None;
    return out;
}

void route_release(uint32_t eid, const char* why) {
    auto it = g_routes.find(eid);
    if (it == g_routes.end()) return;
    release_route(it->second, why);
    g_routes.erase(it);
}

bool routed(uint32_t eid) {
    auto it = g_routes.find(eid);
    return it != g_routes.end() && it->second.active && !it->second.correcting;
}
bool walk_own(uint32_t eid) {
    auto it = g_routes.find(eid);
    return it != g_routes.end() && it->second.active;
}
bool probe_owned(uint32_t eid) { return g_probe.on && g_probe.eid == eid; }

bool dormant_host(uint32_t eid, void* ent, const char* name, double now) {
    if (!on(kBitDormant) || !eid || !ent) return false;
    Census& c = g_census[eid];
    if (now - c.checkAt < kCensusEveryS) { if (c.dormant) c_dormantSkips.fetch_add(1, std::memory_order_relaxed); return c.dormant; }
    c.checkAt = now;
    wo187::ScopeInfo sc[8];
    const int n = wo187::read_all_scopes(eid, sc, 8);
    bool fbKnown = false, fbEmpty = false;
    for (int i = 0; i < n; ++i)
        if (sc[i].ok && !std::strcmp(sc[i].name, "FullBody")) { fbKnown = true; fbEmpty = sc[i].empty; }
    npcdrive::PhysicsStatus ps{};
    const bool physKnown = npcdrive::physics_status(ent, &ps);
    uint64_t w = 0; int st = -1, mask = -1;
    const bool brainKnown = actions::brain_state(ent, &w, &st, &mask);
    c.fbEmpty = fbKnown && fbEmpty;
    c.physics = physKnown && ps.present;
    c.brainRun = brainKnown && st == 0;
    // the rule (wo215_rules.h dormant): no action on the full body (no stance, no activity), and no brain running or no physics
    // (the encounter pools' parked bodies: no physics; a suspended brain) -- unknown reads never make a body dormant
    const bool rule = fbKnown && fbEmpty && ((brainKnown && st != 0) || (physKnown && !ps.present));
    if (!rule) {
        c.ruleSince = -1;
        if (c.dormant) {
            c.dormant = false;
            c_dormantNow.fetch_sub(1, std::memory_order_relaxed);
            const uint32_t k = c_woke.fetch_add(1, std::memory_order_relaxed) + 1;
            logf("WO215-CENSUS npc=%s woke after %.1f s dormant (full body %s, physics %s, brain %s): streamed again, a normal spawn on the joiner [n=%u]",
                 name ? name : "?", now - c.since, c.fbEmpty ? "empty" : "acting", c.physics ? "yes" : "none", c.brainRun ? "running" : "not running", k);
        }
        return false;
    }
    if (c.ruleSince < 0) c.ruleSince = now;
    if (!c.dormant && now - c.ruleSince >= kDormantAfterS) {
        c.dormant = true;
        c.since = now;
        c_dormantNow.fetch_add(1, std::memory_order_relaxed);
        const uint32_t k = c_wentDormant.fetch_add(1, std::memory_order_relaxed) + 1;
        logf("WO215-CENSUS npc=%s dormant (full body empty, physics %s, brain %s state=%d): not streamed until it wakes [n=%u]",
             name ? name : "?", c.physics ? "yes" : "none", c.brainRun ? "running" : "not running", st, k);
    }
    return c.dormant;
}

void note_ghost(uint32_t eid, const char* name) {
    if (!eid) return;
    if (g_ghosts.count(eid)) return;
    g_ghosts[eid] = name ? name : "?";
    const uint32_t k = c_ghosts.fetch_add(1, std::memory_order_relaxed) + 1;
    logf("WO215-GHOST npc=%s eid=0x%X: the engine could not set up its combat actor -- unbound and parked, never left standing [n=%u]", name ? name : "?", eid, k);
}
bool ghost(uint32_t eid) { return g_ghosts.count(eid) != 0; }

void tick() {
    const double now = now_s();
    probe_tick(now);
    // routes of bodies the writer no longer asks about (unbound without a release): cancelled after 2 s
    static double s_prune = 0;
    if (now - s_prune > 1.0) {
        s_prune = now;
        for (auto it = g_routes.begin(); it != g_routes.end();) {
            if (now - it->second.lastSeenAt > 2.0) { release_route(it->second, "unseen"); it = g_routes.erase(it); }
            else ++it;
        }
    }
    if (now - g_lastLine >= 30.0) {
        g_lastLine = now;
        char t[2000];
        if (status(t, sizeof t) > 0 && (c_routedFrames.load() || c_queued.load() || c_refused.load())) logf("WO215-STATUS %s", t);
    }
}

int status(char* out, int n) {
    uint32_t w[static_cast<int>(WriterWhy::kCount)];
    for (int i = 0; i < static_cast<int>(WriterWhy::kCount); ++i) w[i] = c_writerFrames[i].load();
    // the gait meter's median bin
    uint32_t tot = 0, bins[21];
    for (int i = 0; i < 21; ++i) { bins[i] = c_meterRel[i].load(); tot += bins[i]; }
    int p50 = -1, p90 = -1;
    uint32_t acc = 0;
    for (int i = 0; i < 21; ++i) { acc += bins[i]; if (p50 < 0 && acc * 2 >= tot && tot) p50 = i; if (p90 < 0 && acc * 10 >= tot * 9 && tot) p90 = i; }
    uint32_t active = 0;
    for (const auto& kv : g_routes) if (kv.second.active) ++active;
    uint32_t wc[6]{}, miss = 0;
    wo208::w215_counters(wc, &miss);
    return std::snprintf(out, static_cast<size_t>(n),
                         "w215 bits=0x%X twin_off=0x%X configured=%d | A.0 request=%s (%s) queued=%u refused=%u cancels=%u faults=%u ai_hook=%d ai_ticks=%u drained=%u no_npc_ai=%u | A.1 routes=%zu active=%u "
                         "routed_frames=%u orders=%u faces=%u route_corrections=%u releases=%u stall_holds=%u routed_body_s=%u correct_m=%.2f per_frame=%d | writer_frames off=%u beast=%u "
                         "parented=%u air=%u fight=%u refused=%u no_npc=%u not_armed=%u correcting=%u figure=%u budget=%u not_human=%u no_physics=%u | A.2 gait orders walk=%u jog=%u run=%u "
                         "sprint=%u changes=%u meter windows=%u within15=%u p50_bin=%d p90_bin=%d (5%% bins) | D.1 census dormant_now=%u went_dormant=%u woke=%u "
                         "skipped_sends=%u ghosts=%u | cancels_stale=%u | C activity_missing_now=%u refused_here=%u refused_rest=%u ladder_bases=%u stale_ours=%u "
                         "dormant_host_parks=%u npc_state_paths=%u",
                         g_bits.load(), twin_off(), g_configured.load() ? 1 : 0, g_armed.load() ? "armed" : "unarmed", g_api.why, c_queued.load(),
                         c_refused.load(), c_cancels.load(), c_faults.load(), g_hooked.load() ? 1 : 0, c_aiTicks.load(), c_drained.load(), c_noNpcAi.load(),
                         g_routes.size(), active, c_routedFrames.load(), c_orders.load(),
                         c_faces.load(), c_corrections.load(), c_releases.load(), c_stallHolds.load(), c_routedBodyMs.load() / 1000u, g_correctCm.load() / 100.0, g_perFrame.load() ? 1 : 0,
                         w[static_cast<int>(WriterWhy::Off)], w[static_cast<int>(WriterWhy::Beast)], w[static_cast<int>(WriterWhy::Parented)],
                         w[static_cast<int>(WriterWhy::Air)], w[static_cast<int>(WriterWhy::Fight)], w[static_cast<int>(WriterWhy::Refused)],
                         w[static_cast<int>(WriterWhy::NoNpc)], w[static_cast<int>(WriterWhy::NotArmed)], w[static_cast<int>(WriterWhy::Correcting)],
                         w[static_cast<int>(WriterWhy::Figure)], w[static_cast<int>(WriterWhy::Budget)], w[static_cast<int>(WriterWhy::NotHuman)],
                         w[static_cast<int>(WriterWhy::NoPhysics)], c_gaitOrders[0].load(), c_gaitOrders[1].load(), c_gaitOrders[2].load(),
                         c_gaitOrders[3].load(), c_gaitChanges.load(), c_meterN.load(), c_meterOk.load(), p50, p90, c_dormantNow.load(),
                         c_wentDormant.load(), c_woke.load(), c_dormantSkips.load(), c_ghosts.load(), c_cancelStale.load(), miss, wc[1], wc[2], wc[0], wc[4], wc[3], wc[5]);
}

} // namespace kcdmp::wo215
