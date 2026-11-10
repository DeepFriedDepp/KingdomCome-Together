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
#include "wo138.h"
#include "wo216.h"

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
    // WO-216 A.0: the host's read of its NPCs' requests (the task's own copy of the request), each offset checked by the code that writes it
    void* taskStart = nullptr;      // C_MovementTask's start (task, request, id): the queue's call after the id
    void* const* vftTask = nullptr; // C_MovementTask (RTTI)
    bool capArmed = false;
    const char* capWhy = "not installed";
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
// WO-216: the same over a longer function, and the target of the call that follows a pattern ([pattern][E8 rel32])
bool body_has_long(const void* fn, const uint8_t* pat, size_t pn, size_t window) {
    static uint8_t b[0x600];
    if (window > sizeof b || !rd_bytes(fn, b, window)) return false;
    return find_seq(b, window, pat, pn, nullptr) != nullptr;
}
void* call_after(const void* fn, size_t window, const uint8_t* pat, size_t pn) {
    static uint8_t b[0x600];
    if (window > sizeof b || !rd_bytes(fn, b, window)) return nullptr;
    const uint8_t* m = find_seq(b, window, pat, pn, nullptr);
    if (!m || m + pn + 5 > b + window || m[pn] != 0xE8) return nullptr;
    return const_cast<void*>(rel32_target(static_cast<const uint8_t*>(fn), b, m + pn, 1, 5));
}

// WO-216 A.0: the host's read of a request, out of the task's own copy (docs/WO-216-findings.md A.0). The queue starts the task with
// (task, request, id); the start copies the request into the task -- each field below is checked by the instruction that writes it:
//   id          mov [rdi+10h],ebx                         (start)       speed class  mov [rdi+160h],r14d          (start)
//   exact block lea rcx,[rdi+168h] <- lea rdx,[rsi+20h]     (start)       its flag     req+0BCh -> block+9Ch = task+204h (the copy)
//   the end     block+18h (req+38h): point +4, direction +10h (the exact setter writes them there)
//   style       mov eax,[rsi+0FCh]; mov [rdi+29Ch],eax      (start)       tolerances   req+110h/114h -> task+238h/23Ch (start)
//   path        the path start clones the path request into +118h (mov [rdi+118h],rax); its goal at +80h (what the route builds)
//   path id     cmp dword [rdi+124h],-1                     (start: the path finder's request)
constexpr size_t kTaskId = 0x10, kTaskPath = 0x118, kTaskPathId = 0x124, kTaskSpeed = 0x160, kTaskExactPt = 0x184, kTaskExactDir = 0x190,
                 kTaskExactFlag = 0x204, kTaskTolA = 0x238, kTaskTolB = 0x23C, kTaskStyle = 0x29C;
constexpr size_t kPathTarget = 0x80;
constexpr size_t kMgrListHead = 0x10;   // the manager's std::unordered_map<WUID, C_MovementTask*>: its list's head (node: next +0, key +10h, value +18h)

void resolve_capture(HMODULE xg, const anchor::Range& text) {
    // the task's start: the queue's call after the id (mov r8d,ebx; mov rdx,r14; mov rcx,rdi; call <start>)
    static const uint8_t pStart[] = {0x44, 0x8B, 0xC3, 0x49, 0x8B, 0xD6, 0x48, 0x8B, 0xCF};
    void* start = call_after(g_api.queue, 0x90, pStart, sizeof pStart);
    if (!start || !text.contains(start)) { g_api.capWhy = "the task's start not found in the queue"; return; }
    static const uint8_t vId[] = {0x48, 0x8D, 0x56, 0x20, 0x89, 0x5F, 0x10, 0x48, 0x8D, 0x8F, 0x68, 0x01, 0x00, 0x00};
    static const uint8_t vStyle[] = {0x8B, 0x86, 0xFC, 0x00, 0x00, 0x00, 0x89, 0x87, 0x9C, 0x02, 0x00, 0x00};
    static const uint8_t vSpeed[] = {0x44, 0x89, 0xB7, 0x60, 0x01, 0x00, 0x00};
    static const uint8_t vTol[] = {0x8B, 0x86, 0x10, 0x01, 0x00, 0x00, 0x89, 0x87, 0x38, 0x02, 0x00, 0x00};
    static const uint8_t vExact[] = {0x80, 0xBE, 0xBC, 0x00, 0x00, 0x00, 0x00};
    static const uint8_t vPathId[] = {0x83, 0xBF, 0x24, 0x01, 0x00, 0x00, 0xFF};
    if (!body_has_long(start, vId, sizeof vId, 0x500)) { g_api.capWhy = "the start does not store the id at +10h / the block at +168h"; return; }
    if (!body_has_long(start, vStyle, sizeof vStyle, 0x500) || !body_has_long(start, vSpeed, sizeof vSpeed, 0x500) ||
        !body_has_long(start, vTol, sizeof vTol, 0x500)) { g_api.capWhy = "the start does not copy the speed, style or tolerances where expected"; return; }
    if (!body_has_long(start, vExact, sizeof vExact, 0x500) || !body_has_long(start, vPathId, sizeof vPathId, 0x500)) { g_api.capWhy = "the start's exact flag or path id not found"; return; }
    // the block copy (lea rdx,[rsi+20h]; mov [rdi+10h],ebx; lea rcx,[rdi+168h]; call <copy>): its flag byte at +9Ch, the end at +18h
    void* copy = call_after(start, 0x500, vId, sizeof vId);
    static const uint8_t vCopy1[] = {0x48, 0x83, 0xC2, 0x18, 0x89, 0x41, 0x10, 0x48, 0x83, 0xC1, 0x18, 0xE8};
    static const uint8_t vCopy2[] = {0x0F, 0xB6, 0x83, 0x9C, 0x00, 0x00, 0x00, 0x88, 0x87, 0x9C, 0x00, 0x00, 0x00};
    if (!copy || !text.contains(copy) || !body_has(copy, vCopy1, sizeof vCopy1, 0xC0) || !body_has(copy, vCopy2, sizeof vCopy2, 0xC0)) {
        g_api.capWhy = "the exact block's copy differs"; return;
    }
    // the exact setter writes the end point at block+4 and its direction at block+10h (its local block at rsp+20h; the request's at +38h)
    static const uint8_t vSet1[] = {0xF2, 0x0F, 0x11, 0x44, 0x24, 0x24};
    static const uint8_t vSet2[] = {0x48, 0x8D, 0x4E, 0x38};
    if (!body_has(g_api.exactSet, vSet1, sizeof vSet1, 0x110) || !body_has(g_api.exactSet, vSet2, sizeof vSet2, 0x110)) { g_api.capWhy = "the exact setter's block differs"; return; }
    // the path start (movzx r8d,byte [rsi+18h]; mov rcx,rdi; mov rdx,[rsi+8]; call <path start>): it keeps the clone at +118h
    static const uint8_t pPath[] = {0x44, 0x0F, 0xB6, 0x46, 0x18, 0x48, 0x8B, 0xCF, 0x48, 0x8B, 0x56, 0x08};
    void* pstart = call_after(start, 0x500, pPath, sizeof pPath);
    static const uint8_t vClone[] = {0x48, 0x8B, 0x8F, 0x18, 0x01, 0x00, 0x00, 0x48, 0x89, 0x87, 0x18, 0x01, 0x00, 0x00};
    if (!pstart || !text.contains(pstart) || !body_has(pstart, vClone, sizeof vClone, 0x80)) { g_api.capWhy = "the path start does not keep its clone at +118h"; return; }
    g_api.vftTask = anchor::find_vftable(xg, ".?AVC_MovementTask@navigation@xgenaimodule@wh@@", 0);
    if (!g_api.vftTask) { g_api.capWhy = "C_MovementTask not found by RTTI"; return; }
    g_api.taskStart = start;
    g_api.capArmed = true;
    g_api.capWhy = "armed";
    char a[96], b[96], c[96];
    anchor::describe(start, a, sizeof a); anchor::describe(copy, b, sizeof b); anchor::describe(pstart, c, sizeof c);
    logf("WO216-ROUTE the host's read armed: the task's start %s (id +10h, speed +160h, style +29Ch, tolerances +238h/+23Ch, path id +124h), "
         "the exact block's copy %s (flag task+204h, end +184h, direction +190h), the path start %s (clone +118h, goal +80h); "
         "the tasks by WUID from the manager's own map", a, b, c);
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
    resolve_capture(xg, text);   // WO-216 A.0 (a miss leaves the route as WO-215's: the status says which piece)
    if (!g_api.capArmed) logf("WO216-ROUTE the host's read NOT armed: %s -- the copies follow the stream (WO-215's walk-to-sample)", g_api.capWhy);
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
// WO-216 A.1: what a replayed request carries beyond the route's own (the host's values, read out of its NPC's task)
struct ReqX {
    bool on = false;          // the style and the tolerances below are the host's
    int style = 7;            // the request's +0xFC
    float tolA = -1.0f, tolB = -2.0f;   // +0x110 / +0x114
    bool exact = false;       // an exact end of its own (the host's point and facing)
    float exactPt[3]{};
    float exactYaw = 0;
};
constexpr size_t kReqStyle = 0xFC, kReqTolA = 0x110, kReqTolB = 0x114;

uint32_t queue_request(void* npc, const float target[3], int speedEnum, bool exact, float yaw, const ReqX* x = nullptr) {
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
        if (x && x->on) {   // WO-216: the host's own style and tolerances (the ctor's defaults otherwise: 7, -1, -2)
            const int32_t st = x->style;
            std::memcpy(req + kReqStyle, &st, 4);
            std::memcpy(req + kReqTolA, &x->tolA, 4);
            std::memcpy(req + kReqTolB, &x->tolB, 4);
        }
        if (x && x->exact) {
            const float xd[3] = {-std::sin(x->exactYaw), std::cos(x->exactYaw), 0.0f};
            reinterpret_cast<Setter>(g_api.exactSet)(req, x->exactPt, xd);
        } else if (exact) reinterpret_cast<Setter>(g_api.exactSet)(req, tgt, dir);
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
enum : uint8_t { kOrdWalk = 1, kOrdExact = 2, kOrdCancel = 3, kOrdReplay = 4 };   // 4: WO-216 A.1, the host's own request
enum : uint8_t { kOutQueued = 1, kOutRefused = 2, kOutNoNpc = 3 };
struct Order { uint8_t kind = 0; float target[3]{}; int speed = 1; float yaw = 0; uint32_t seq = 0; ReqX x; };
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

void capture_ai(double now);   // WO-216 A.0 (the host): its streamed NPCs' requests
void ended_ai(double now);     // WO-216 A.1 (the joiner): which of our requests ended

void ai_drain() {
    c_aiTicks.fetch_add(1, std::memory_order_relaxed);
    const double tNow = now_s();
    capture_ai(tNow);
    ended_ai(tNow);
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
            const uint32_t id = queue_request(npc, o.target, o.speed, o.kind == kOrdExact, o.yaw, o.kind == kOrdReplay ? &o.x : nullptr);
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

// ---------------------------------------------------------------------------------------------------- WO-216 A.0: the host's requests
// The host's streamed bodies (the main thread posts them every half second out of the sender's own set); the AI update's entry reads
// their tasks out of the manager's own map -- reads only, no engine call but the C_NPC lookup the route's drain already makes -- and
// keeps the newest change per body for the agent (op 162). The probe's body on the joiner is read the same way (A.0's verification).
struct CapBody { uint32_t eid = 0; std::string name; bool beast = false; };
std::mutex g_capMu;                                   // guards the three below only (never held across an engine call)
std::vector<CapBody> g_capSet;
std::unordered_map<uint32_t, wo216rules::RouteRec> g_capOut;   // eid -> the newest line not yet taken by the agent
std::unordered_map<uint32_t, std::string> g_capNames;
std::atomic<uint32_t> g_capProbeEid{0};               // A.0's verification: the probe's body (the joiner), read as the host's are
wo216rules::RouteRec g_capProbeRec;                   // (g_capMu)
bool g_capProbeHave = false;
struct CapState { wo216rules::RouteRec last; double sentAt = -1e9; void* npc = nullptr; double npcAt = -1e9; uint32_t seq = 0; bool any = false; };
std::unordered_map<uint32_t, CapState> g_cap;         // the AI update's thread only
std::atomic<uint32_t> c_capTicks{0}, c_capBodies{0}, c_capLines{0}, c_capActive{0}, c_capExact{0}, c_capGoal{0}, c_capPrePath{0}, c_capNoTask{0},
    c_capNoNpc{0}, c_capTasks{0}, c_capSpeed[12]{}, c_capTaken{0}, c_capFaults{0}, c_capReaims{0};

// The manager's tasks by WUID: its own std::unordered_map, walked through its list (head at +10h; node next +0, key +10h, task +18h)
size_t tasks_by_wuid(void* mgr, std::unordered_map<uint64_t, void*>* out) {
    out->clear();
    void* head = nullptr;
    if (!mgr || !rd(mgr, kMgrListHead, &head) || !head) return 0;
    void* node = nullptr;
    if (!rd(head, 0, &node)) return 0;
    for (int guard = 0; node && node != head && guard < 8192; ++guard) {
        uint64_t w = 0; void* task = nullptr; void* next = nullptr;
        if (!rd(node, 0x10, &w) || !rd(node, 0x18, &task) || !rd(node, 0, &next)) { c_capFaults.fetch_add(1, std::memory_order_relaxed); break; }
        if (task && is_a(task, g_api.vftTask)) (*out)[w] = task;
        node = next;
    }
    return out->size();
}

bool read_task(void* task, wo216rules::RouteRec* rec) {
    using namespace wo216rules;
    uint32_t id = kNoReq;
    if (!rd(task, kTaskId, &id)) return false;
    rec->reqId = id;
    if (id == kNoReq) return true;
    rec->flags |= kRecActive;
    int32_t sp = 1, style = 7;
    if (rd(task, kTaskSpeed, &sp)) rec->speed = static_cast<uint8_t>(sp >= 0 && sp <= 15 ? sp : 1);
    if (rd(task, kTaskStyle, &style)) rec->style = static_cast<uint8_t>(style >= 0 && style <= 255 ? style : 7);
    rd(task, kTaskTolA, &rec->tolA);
    rd(task, kTaskTolB, &rec->tolB);
    uint32_t pid = kNoReq;
    if (rd(task, kTaskPathId, &pid)) rec->pathId = pid;
    void* path = nullptr;
    if (rd(task, kTaskPath, &path) && path) {
        float g[3];
        if (is_a(path, reinterpret_cast<void* const*>(g_api.pathVft)) && rd(path, kPathTarget, &g) && finite3(g)) { std::memcpy(rec->goal, g, 12); rec->flags |= kRecGoal; }
    } else rec->flags |= kRecPrePath;   // no path request: a path given with the request (its goal is not read here)
    uint8_t ex = 0;
    if (rd(task, kTaskExactFlag, &ex) && ex) {
        float pt[3], dir[3];
        if (rd(task, kTaskExactPt, &pt) && rd(task, kTaskExactDir, &dir) && finite3(pt) && finite3(dir)) {
            std::memcpy(rec->exact, pt, 12);
            rec->exactYaw = yaw_of_dir(dir[0], dir[1]);
            rec->flags |= kRecExact;
        }
    }
    if (!std::isfinite(rec->tolA)) rec->tolA = -1.0f;
    if (!std::isfinite(rec->tolB)) rec->tolB = -2.0f;
    return true;
}

void capture_ai(double now) {
    using namespace wo216rules;
    static double s_at = -1e9;
    if (now - s_at < kCaptureEveryS) return;
    s_at = now;
    if (!g_api.capArmed || !g_armed.load(std::memory_order_relaxed)) return;
    const uint32_t probeEid = g_capProbeEid.load(std::memory_order_relaxed);
    std::vector<CapBody> set;
    {
        std::lock_guard<std::mutex> l(g_capMu);
        set = g_capSet;
    }
    if (set.empty() && !probeEid) return;
    void* mgr = task_manager();
    if (!mgr) return;
    static std::unordered_map<uint64_t, void*> s_tasks;
    c_capTasks.store(static_cast<uint32_t>(tasks_by_wuid(mgr, &s_tasks)), std::memory_order_relaxed);
    c_capTicks.fetch_add(1, std::memory_order_relaxed);
    auto task_of_eid = [&](uint32_t eid, CapState& cs) -> void* {
        if (now - cs.npcAt > 2.0 || (cs.npc && !is_a(cs.npc, g_api.vftNpc))) { cs.npc = npc_of(eid); cs.npcAt = now; }
        if (!cs.npc) { c_capNoNpc.fetch_add(1, std::memory_order_relaxed); return nullptr; }
        uint64_t w = 0;
        if (!rd(cs.npc, kNpcWuid, &w)) return nullptr;
        auto it = s_tasks.find(w);
        return it == s_tasks.end() ? nullptr : it->second;
    };
    if (probeEid) {
        static CapState s_pcs;
        RouteRec rec;
        void* task = task_of_eid(probeEid, s_pcs);
        const bool ok = task && read_task(task, &rec);
        std::lock_guard<std::mutex> l(g_capMu);
        g_capProbeRec = rec;
        g_capProbeHave = ok;
    }
    for (const CapBody& b : set) {
        CapState& cs = g_cap[b.eid];
        RouteRec rec;
        if (b.beast) rec.flags |= kRecBeast;
        void* task = task_of_eid(b.eid, cs);
        if (!task) c_capNoTask.fetch_add(1, std::memory_order_relaxed);
        else if (!read_task(task, &rec)) continue;
        c_capBodies.fetch_add(1, std::memory_order_relaxed);
        const bool changed = !cs.any || rec_changed(rec, cs.last);
        if (!changed && !repeat_due((rec.flags & kRecActive) != 0, now - cs.sentAt)) continue;
        if (changed) {
            rec.seq = ++cs.seq;
            if (cs.any && (rec.flags & kRecActive) && rec.reqId == cs.last.reqId) c_capReaims.fetch_add(1, std::memory_order_relaxed);   // the same request re-aimed
            if (rec.flags & kRecActive) {
                c_capActive.fetch_add(1, std::memory_order_relaxed);
                c_capSpeed[rec.speed < 12 ? rec.speed : 11].fetch_add(1, std::memory_order_relaxed);
                if (rec.flags & kRecExact) c_capExact.fetch_add(1, std::memory_order_relaxed);
                if (rec.flags & kRecGoal) c_capGoal.fetch_add(1, std::memory_order_relaxed);
                if (rec.flags & kRecPrePath) c_capPrePath.fetch_add(1, std::memory_order_relaxed);
            }
        } else rec.seq = cs.seq;
        cs.last = rec;
        cs.any = true;
        cs.sentAt = now;
        c_capLines.fetch_add(1, std::memory_order_relaxed);
        std::lock_guard<std::mutex> l(g_capMu);
        g_capOut[b.eid] = rec;
    }
    // bodies the host no longer streams: forgotten (a body streamed again starts with a fresh line)
    static double s_prune = 0;
    if (now - s_prune > 5.0) {
        s_prune = now;
        for (auto it = g_cap.begin(); it != g_cap.end();) {
            bool in = false;
            for (const CapBody& b : set) if (b.eid == it->first) { in = true; break; }
            if (!in) it = g_cap.erase(it); else ++it;
        }
    }
}

// ---- WO-216 A.1 (the joiner): which of our requests ended. Our request id per copy is the drain's own (g_ourId); the copy's task says
// whether it is still the current one. The main thread hears the ends (route_frame: a replay that ended far from its goal is a no-path).
std::unordered_map<uint32_t, double> g_ended;   // eid -> when (g_ordMu)
std::atomic<uint32_t> c_endedSeen{0};

void ended_ai(double now) {
    static double s_at = -1e9;
    if (now - s_at < 0.10) return;
    s_at = now;
    if (!g_api.capArmed || !g_armed.load(std::memory_order_relaxed) || g_ourId.empty()) return;
    void* mgr = task_manager();
    if (!mgr) return;
    static std::unordered_map<uint64_t, void*> s_tasks;
    tasks_by_wuid(mgr, &s_tasks);
    for (auto it = g_ourId.begin(); it != g_ourId.end();) {
        void* npc = npc_of(it->first);
        uint64_t w = 0;
        uint32_t cur = kNoRequest;
        bool ended = false;
        if (npc && rd(npc, kNpcWuid, &w)) {
            auto t = s_tasks.find(w);
            if (t != s_tasks.end() && rd(t->second, kTaskId, &cur)) ended = cur != it->second;
        }
        if (ended) {
            c_endedSeen.fetch_add(1, std::memory_order_relaxed);
            {
                std::lock_guard<std::mutex> l(g_ordMu);
                g_ended[it->first] = now;
            }
            it = g_ourId.erase(it);
        } else ++it;
    }
}

bool take_ended(uint32_t eid) {
    std::lock_guard<std::mutex> l(g_ordMu);
    auto it = g_ended.find(eid);
    if (it == g_ended.end()) return false;
    g_ended.erase(it);
    return true;
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
    // WO-216 A.1: the host's own request replayed on this copy
    bool replay = false;        // the body's request is the host's (not WO-215's walk-to-sample)
    bool replayed = false;      // ... posted for the line below
    uint32_t replayReq = 0xFFFFFFFFu, replaySeq = 0;
    double replayRestUntil = 0; // after a refusal or a no-path: WO-215's way for a while
    double stallEndAt = -1e9;   // when the last link stall ended (a drift right after it is the stall's)
    double endedAt = -1;        // the AI update said our request ended (a replay far from its goal: no-path)
    double parSum = 0;          // A.2: this route's lateral offsets (the copy to the rendered sample) and their count
    uint32_t parN = 0;
    bool meterReplay = false;   // the gait meter's window ran on a replay
    float lastBody[3]{};        // WO-216: the body at the start of its pace window (its own pace: a stall while the host walks on)
    double lastBodyAt = -1;     // ... when that window began (a window is 0.25 s or more: a body's position steps, frame to frame)
    double orderAt = -1;        // the newest replay order (its start delay: until the copy walks)
    bool startSeen = true;
    double hostEndAt = -1;      // the host's request ended (arrived): the copy finishes its own to the same goal
    float way[wo216rules::kWayPoints][2]{};   // the host NPC's way over its last 1.2 s (a ring: the oldest at wayHead)
    int wayN = 0, wayHead = 0;
    double wayAt = -1e9;
    // WO-216: this body's frames by verdict since its last summary line (the analysis measures the routed copies apart)
    uint32_t fReplay = 0, fRoute = 0, fCorrect = 0, fWriter = 0;
    uint32_t fWhy[static_cast<int>(WriterWhy::kCount)]{};
};
std::unordered_map<uint32_t, Route> g_routes;
std::atomic<uint32_t> c_routedFrames{0}, c_writerFrames[static_cast<int>(WriterWhy::kCount)]{}, c_corrections{0}, c_orders{0}, c_faces{0},
    c_releases{0}, c_gaitChanges{0}, c_noNpc{0}, c_stallHolds{0};
std::atomic<uint32_t> c_routedBodyMs{0};   // the routed bodies' time, summed (ms; wraps after 49 days of body-time) -- corrections per body-minute
std::atomic<uint32_t> c_meterN{0}, c_meterOk{0};
std::atomic<uint32_t> c_meterRel[21]{};   // |render - stream| / stream in 5 % bins (the last: >= 100 %)
std::atomic<uint32_t> c_gaitOrders[kGaits]{};

float dist2d(const float* a, const float* b) { const float dx = a[0] - b[0], dy = a[1] - b[1]; return std::sqrt(dx * dx + dy * dy); }

// ---------------------------------------------------------------------------------------------------- WO-216 A.1: the replay (joiner)
// The host's lines arrive off the main thread (op 163) into g_linePending; the main thread's tick moves them into g_lines, which only
// the main thread reads (route_frame).
struct Line { wo216rules::RouteRec rec; double at = 0; };
std::mutex g_lineMu;
std::unordered_map<uint32_t, Line> g_linePending;     // (g_lineMu)
std::unordered_map<uint32_t, Line> g_lines;           // the main thread only
std::atomic<uint32_t> c_rpLinesIn{0}, c_rpOrders{0}, c_rpFrames{0}, c_rpNoLine{0}, c_rpNoGoal{0}, c_rpRest{0}, c_rpHostEnd{0}, c_rpArrived{0},
    c_rpExact{0}, c_rpRoutes{0}, c_rpFallFrames{0}, c_rpMeterN{0}, c_rpMeterOk{0}, c_rpBodyMs{0}, c_rpStallHolds{0}, c_rpEndedNear{0};
std::atomic<uint32_t> c_rpCause[static_cast<int>(wo216rules::Cause::kCount)]{};
std::atomic<uint32_t> c_rpReaims{0}, c_rpMovingFrames{0}, c_rpStallFrames{0}, c_rpStallStart{0}, c_rpHostArrived{0}, c_rpFinished{0}, c_rpHorseFrames{0};
std::atomic<uint32_t> c_rpStartBins[21]{};   // the delay from a replay order to the copy's first stride, 100 ms bins (the last: >= 2 s)
std::atomic<uint32_t> c_rpSpeed[12]{};
std::atomic<uint32_t> c_rpParity[wo216rules::kParityBins]{};
double g_rpParSum = 0;   // the main thread: the routes' mean offsets summed (their mean of means)
uint32_t g_rpParN = 0;

void parity_close(Route& r) {
    if (r.parN >= 10) {
        const float m = static_cast<float>(r.parSum / r.parN);
        c_rpParity[wo216rules::parity_bin(m)].fetch_add(1, std::memory_order_relaxed);
        c_rpRoutes.fetch_add(1, std::memory_order_relaxed);
        g_rpParSum += m;
        ++g_rpParN;
    }
    r.parSum = 0;
    r.parN = 0;
}

void count_cause(Route& r, wo216rules::Cause c, float distM, float thrM) {
    const uint32_t k = c_rpCause[static_cast<int>(c)].fetch_add(1, std::memory_order_relaxed) + 1;
    ++r.corrections;
    const uint32_t all = c_corrections.fetch_add(1, std::memory_order_relaxed) + 1;
    if (all <= 200 || all % 100 == 0)
        logf("WO216-CORRECT npc=%s cause=%s %.2f m (over %.2f m): %s [cause n=%u, body n=%u, all n=%u]", r.name.c_str(), wo216rules::cause_name(c),
             distM, thrM, c == wo216rules::Cause::Drift || c == wo216rules::Cause::Stall ? "its request cancelled, blended back by the writer, the host's "
             "request again from there" : "WO-215's walk-to-sample for 5 s", k, r.corrections, all);
}

// Back to WO-215's way (walk-to-sample, the face): the replay's state is dropped; `cancel` ends the request in flight first.
void replay_leave(Route& r, bool cancel) {
    if (cancel && (r.active || r.pendingSeq)) post_order(r.eid, kOrdCancel, nullptr, 0, 0.0f);
    parity_close(r);
    r.replay = false; r.replayed = false;
    r.active = false; r.exact = false; r.pendingSeq = 0; r.reqId = kNoRequest; r.correcting = false;
}

uint32_t post_replay(uint32_t eid, const wo216rules::RouteRec& L) {
    Order o;
    o.kind = kOrdReplay;
    std::memcpy(o.target, L.goal, 12);
    o.speed = wo216rules::known_speed(L.speed) ? L.speed : 1;
    o.yaw = L.exactYaw;
    o.x.on = true;
    o.x.style = L.style;
    o.x.tolA = L.tolA;
    o.x.tolB = L.tolB;
    o.x.exact = (L.flags & wo216rules::kRecExact) != 0;
    std::memcpy(o.x.exactPt, L.exact, 12);
    o.x.exactYaw = L.exactYaw;
    o.seq = g_ordSeq.fetch_add(1, std::memory_order_relaxed) + 1;
    {
        std::lock_guard<std::mutex> l(g_ordMu);
        g_orders[eid] = o;
    }
    g_ordersPending.store(true, std::memory_order_release);
    return o.seq;
}

enum class Rp : uint8_t { Fallback = 0, Routed = 1, Correct = 2, Budget = 3 };

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
    if (r.meterAt < 0) { std::memcpy(r.meterPos, body, 12); r.meterAt = in.now; r.meterStreamSum = 0; r.meterStreamN = 0; r.meterReplay = r.replay; return; }
    if (!r.replay) r.meterReplay = false;
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
        if (r.meterReplay && r.replay) {   // WO-216 A.3: a window the host's own request carried from end to end
            c_rpMeterN.fetch_add(1, std::memory_order_relaxed);
            if (rel < 0.15f) c_rpMeterOk.fetch_add(1, std::memory_order_relaxed);
        }
    }
    r.meterReplay = r.replay;
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
    bool exactAsked = false;   // WO-216 A.0: what the probe asked (the read is checked against it)
    float yawAsked = 0;
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
    // WO-216 A.0: the copy's own task, read the host's way at the AI update's entry -- against what the probe asked for
    wo216rules::RouteRec rr;
    bool have = false;
    {
        std::lock_guard<std::mutex> l(g_capMu);
        rr = g_capProbeRec;
        have = g_capProbeHave;
    }
    if (have)
        logf("WO216-A0 npc=%s t=%.3f read: req=%d active=%d speed=%u style=%u goal=%s(%.3f,%.3f,%.3f) exact=%d(%.3f,%.3f,%.3f yaw %.3f) tol=%.2f/%.2f "
             "path_id=%d pre_path=%d | asked: target=(%.3f,%.3f,%.3f) speed=%d exact=%d yaw=%.3f", g_probe.name.c_str(), now - g_probe.startAt,
             static_cast<int>(rr.reqId), (rr.flags & wo216rules::kRecActive) ? 1 : 0, rr.speed, rr.style, (rr.flags & wo216rules::kRecGoal) ? "" : "unknown",
             rr.goal[0], rr.goal[1], rr.goal[2], (rr.flags & wo216rules::kRecExact) ? 1 : 0, rr.exact[0], rr.exact[1], rr.exact[2], rr.exactYaw,
             rr.tolA, rr.tolB, static_cast<int>(rr.pathId), (rr.flags & wo216rules::kRecPrePath) ? 1 : 0, g_probe.start[0], g_probe.start[1],
             g_probe.start[2], speed_enum(g_probe.gait), g_probe.exactAsked ? 1 : 0, g_probe.yawAsked);
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
        g_probe.exactAsked = (g & 0x80) != 0;
        g_probe.yawAsked = v[3];
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

// ---- WO-216 A: the route lines (ops 162 / 163)
// 162 RouteRead [max:1] -> [count:1]{[nameLen:1][name][rec:52]}: the changed lines, oldest first is not kept (newest per body wins)
size_t route_read(uint8_t maxN, uint8_t* out, size_t cap) {
    using namespace wo216rules;
    if (cap < 1) return 0;
    out[0] = 0;
    size_t k = 1;
    std::lock_guard<std::mutex> l(g_capMu);
    for (auto it = g_capOut.begin(); it != g_capOut.end() && out[0] < maxN;) {
        auto nm = g_capNames.find(it->first);
        if (nm == g_capNames.end() || nm->second.empty() || nm->second.size() > 63) { it = g_capOut.erase(it); continue; }
        const size_t need = 1 + nm->second.size() + kRecWire;
        if (k + need > cap) break;
        out[k] = static_cast<uint8_t>(nm->second.size());
        std::memcpy(out + k + 1, nm->second.data(), nm->second.size());
        rec_put(it->second, out + k + 1 + nm->second.size());
        k += need;
        ++out[0];
        {   // A.0's record: the first lines and every 500th, as the host's read gave them
            static uint32_t s_n = 0;
            const RouteRec& rr = it->second;
            if (++s_n <= 200 || s_n % 500 == 0)
                logf("WO216-CAP npc=%s req=%d seq=%u active=%d speed=%u style=%u goal=%s(%.2f,%.2f,%.2f) exact=%d(%.2f,%.2f,%.2f yaw %.2f) tol=%.2f/%.2f "
                     "path_id=%d pre_path=%d beast=%d [n=%u]", nm->second.c_str(), static_cast<int>(rr.reqId), rr.seq, (rr.flags & kRecActive) ? 1 : 0,
                     rr.speed, rr.style, (rr.flags & kRecGoal) ? "" : "unknown", rr.goal[0], rr.goal[1], rr.goal[2], (rr.flags & kRecExact) ? 1 : 0,
                     rr.exact[0], rr.exact[1], rr.exact[2], rr.exactYaw, rr.tolA, rr.tolB, static_cast<int>(rr.pathId), (rr.flags & kRecPrePath) ? 1 : 0,
                     (rr.flags & kRecBeast) ? 1 : 0, s_n);
        }
        it = g_capOut.erase(it);
    }
    c_capTaken.fetch_add(out[0], std::memory_order_relaxed);
    return k;
}
// 163 RouteIn [count:1]{[eid:4][rec:52]}: the host's lines for this world's copies
bool route_in(const uint8_t* b, size_t len) {
    using namespace wo216rules;
    if (len < 1) return false;
    const size_t n = b[0];
    if (len != 1 + n * (4 + kRecWire)) return false;
    const double t = now_s();
    std::lock_guard<std::mutex> l(g_lineMu);
    for (size_t i = 0; i < n; ++i) {
        const uint8_t* q = b + 1 + i * (4 + kRecWire);
        uint32_t eid = 0;
        std::memcpy(&eid, q, 4);
        Line ln;
        if (!eid || !rec_get(q + 4, &ln.rec)) continue;
        ln.at = t;
        g_linePending[eid] = ln;
        c_rpLinesIn.fetch_add(1, std::memory_order_relaxed);
    }
    return true;
}
uint8_t route_op(uint8_t opc, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (opc == wo216::kOpRouteRead) {
        if (len != 2) return wo163::kRBadRequest;
        *outLen = route_read(body[1], out, cap);
        return wo163::kROk;
    }
    if (opc == wo216::kOpRouteIn) return route_in(body + 1, len - 1) ? wo163::kROk : wo163::kRBadRequest;
    return wo163::kRBadRequest;
}
bool route_op_off_main(uint8_t opc, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen, uint8_t* reason) {
    *reason = route_op(opc, body, len, out, cap, outLen);
    return true;
}

void install_reg() { resolve(); }
void tick_reg() { tick(); }
int status_reg(char* out, int n) { return status(out, n); }
const bool kReg = wo203::reg_op(kOpConfig, &op) && wo203::reg_op(kOpStatus, &op) && wo203::reg_op(kOpProbe, &op) &&
                  wo203::reg_op(kOpTune, &op) && wo203::reg_install("w215", &install_reg) && wo203::reg_tick("w215", &tick_reg) &&
                  wo203::reg_status("w215", &status_reg) && wo203::reg_op(wo216::kOpRouteRead, &route_op) && wo203::reg_op(wo216::kOpRouteIn, &route_op) &&
                  wo203::reg_op_off_main(wo216::kOpRouteRead, &route_op_off_main) && wo203::reg_op_off_main(wo216::kOpRouteIn, &route_op_off_main);

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

// WO-216 A.1: one routed body's frame under the host's own request. Fallback: no live line, a line without a goal (a path given with
// the request), the host's NPC standing, or a rest after a refusal -- WO-215's way below (walk-to-sample, the face), counted.
Rp replay_frame(Route& r, const RouteIn& in, const float body[3], int& used) {
    using namespace wo216rules;
    if (take_ended(in.eid)) r.endedAt = in.now;
    auto it = g_lines.find(in.eid);
    if (it == g_lines.end()) { if (r.replay) replay_leave(r, false); c_rpNoLine.fetch_add(1, std::memory_order_relaxed); return Rp::Fallback; }
    const RouteRec& L = it->second.rec;
    const bool hostActive = (L.flags & kRecActive) != 0;
    if (!line_live(hostActive, in.now - it->second.at)) {
        if (r.replay) replay_leave(r, false);
        c_rpNoLine.fetch_add(1, std::memory_order_relaxed);
        return Rp::Fallback;
    }
    if (in.now < r.replayRestUntil) { if (r.replay) replay_leave(r, true); c_rpRest.fetch_add(1, std::memory_order_relaxed); return Rp::Fallback; }
    // a link stall: the request in flight plays out (WO-215's rule)
    if (in.stall && r.replay && r.active) {
        if (!r.inStall) { r.inStall = true; c_rpStallHolds.fetch_add(1, std::memory_order_relaxed); }
        return Rp::Routed;
    }
    if (r.inStall && !in.stall) r.stallEndAt = in.now;
    r.inStall = false;
    if (!hostActive) {
        // the host's request ended. Arrived (the host NPC stands at the goal): the copy, a moment behind, finishes its own request to the
        // same goal -- no cancel a metre short of it, no second start to the same spot. Cancelled by its brain (the host NPC stopped away
        // from the goal): ours is cancelled too. Standing is WO-215's face from here (an exact request to the host's spot when it turns).
        if (r.replay) {
            const bool hostThere = dist2d(in.newest, r.orderPos) < 1.0f;
            if (r.active && hostThere && r.endedAt < 0) {
                if (r.hostEndAt < 0) { r.hostEndAt = in.now; c_rpHostArrived.fetch_add(1, std::memory_order_relaxed); }
                if (in.now - r.hostEndAt < 6.0 && dist2d(body, r.orderPos) > kLetFinishM) {
                    c_rpFrames.fetch_add(1, std::memory_order_relaxed);
                    return Rp::Routed;   // the copy's own request plays out
                }
            }
            if (r.active && r.endedAt < 0 && cancel_on_host_end(true, dist2d(body, r.orderPos)) && !hostThere) {
                c_rpHostEnd.fetch_add(1, std::memory_order_relaxed);
                replay_leave(r, true);
            } else {
                c_rpArrived.fetch_add(1, std::memory_order_relaxed);
                if (r.hostEndAt >= 0) c_rpFinished.fetch_add(1, std::memory_order_relaxed);
                replay_leave(r, false);
            }
            r.hostEndAt = -1;
            r.endedAt = -1;
        }
        return Rp::Fallback;
    }
    r.hostEndAt = -1;
    if (!(L.flags & kRecGoal)) { if (r.replay) replay_leave(r, true); c_rpNoGoal.fetch_add(1, std::memory_order_relaxed); return Rp::Fallback; }
    if (!r.replay) {   // WO-215's own request (if any) is replaced by the host's: a request cancels the task's current one by itself
        r.replay = true; r.replayed = false; r.correcting = false; r.endedAt = -1;
    }
    // the corrector: the copy against where its host NPC is now -- the newest sample carried on along its own velocity for the sample's
    // age (the copy makes the host's request a moment after the host does, so it walks beside the host NPC, not beside the rendered
    // sample, which the play-out delay holds 0.1-0.3 s behind: b1 pulled every replayed copy back that far, then restarted it from a stand)
    float hostNow[3] = {in.newest[0], in.newest[1], in.newest[2]};
    if (in.streamMps >= kMovingMps) {
        const float age = (std::min)(in.newestAgeS, 0.5f);
        hostNow[0] += in.streamVx * age;
        hostNow[1] += in.streamVy * age;
    }
    // the host's way: a point every 0.05 s, its last 1.2 s; the copy is measured against all of it (it walks the host's request a
    // moment after the host does) -- never against the newest point alone (b6: 0.8 s behind on the same path was a drift)
    if (in.now - r.wayAt > kWayGapS) { r.wayN = 0; r.wayHead = 0; r.wayAt = -1e9; }
    if (in.now - r.wayAt >= kWayStepS) {
        r.wayAt = in.now;
        if (r.wayN < kWayPoints) { const int s = (r.wayHead + r.wayN) % kWayPoints; r.way[s][0] = hostNow[0]; r.way[s][1] = hostNow[1]; ++r.wayN; }
        else { r.way[r.wayHead][0] = hostNow[0]; r.way[r.wayHead][1] = hostNow[1]; r.wayHead = (r.wayHead + 1) % kWayPoints; }
    }
    float pts[kWayPoints + 1][2];
    int np = 0;
    for (int i = 0; i < r.wayN; ++i) { const int j = (r.wayHead + i) % kWayPoints; pts[np][0] = r.way[j][0]; pts[np][1] = r.way[j][1]; ++np; }
    pts[np][0] = hostNow[0]; pts[np][1] = hostNow[1]; ++np;
    const float off = way_off(body, pts, np);
    const float thr = static_cast<float>(g_correctCm.load(std::memory_order_relaxed)) / 100.0f +
                      (in.gapS > 0.25f && in.streamMps > 0.0f ? in.streamMps * (std::min)(in.gapS, 1.5f) : 0.0f);
    // A.3's meter of the copy's own pace, over windows of a quarter second or more: a stall is the copy standing while its host NPC
    // walks on (counted in windows: moving_frames / stall_frames are windows from b9)
    const double win = in.now - r.lastBodyAt;
    if (r.lastBodyAt < 0 || win > 1.0) { std::memcpy(r.lastBody, body, 12); r.lastBodyAt = in.now; }
    else if (win >= 0.25) {
        const float cmps = dist2d(body, r.lastBody) / static_cast<float>(win);
        if (in.streamMps > 0.6f && r.replay && r.active) {
            c_rpMovingFrames.fetch_add(1, std::memory_order_relaxed);
            if (cmps < 0.2f) {
                c_rpStallFrames.fetch_add(1, std::memory_order_relaxed);
                if (r.orderAt > 0 && in.now - r.orderAt < 1.5) c_rpStallStart.fetch_add(1, std::memory_order_relaxed);
            }
        }
        if (!r.startSeen && cmps > 0.5f && r.orderAt > 0) {
            r.startSeen = true;
            int b = static_cast<int>((in.now - r.orderAt) * 10.0);
            c_rpStartBins[b < 0 ? 0 : (b > 20 ? 20 : b)].fetch_add(1, std::memory_order_relaxed);
        }
        std::memcpy(r.lastBody, body, 12);
        r.lastBodyAt = in.now;
    }
    if (r.correcting) {
        if (off <= kCorrectBackM) { r.correcting = false; r.replayed = false; }   // back: the host's request again, from here
        else { c_writerFrames[static_cast<int>(WriterWhy::Correcting)].fetch_add(1, std::memory_order_relaxed); return Rp::Correct; }
    } else if (r.replayed && r.active && off > thr) {
        count_cause(r, drift_cause(in.now - r.stallEndAt), off, thr);
        post_order(in.eid, kOrdCancel, nullptr, 0, 0.0f);   // the writer alone moves it back (no tug between the two)
        r.pendingSeq = 0; r.active = false; r.exact = false; r.reqId = kNoRequest; r.correcting = true; r.replayed = false;
        parity_close(r);
        c_writerFrames[static_cast<int>(WriterWhy::Correcting)].fetch_add(1, std::memory_order_relaxed);
        return Rp::Correct;
    }
    // our request ended while the host's runs: near its goal it arrived first (it stays); far from it, the engine found no way here
    if (r.replayed && r.endedAt > 0) {
        r.endedAt = -1;
        const float toGoal = dist2d(body, L.goal);
        if (no_path(true, true, toGoal)) {
            count_cause(r, Cause::NoPath, toGoal, kNoPathM);
            r.replayRestUntil = in.now + kReplayRestS;
            replay_leave(r, false);
            return Rp::Fallback;
        }
        c_rpEndedNear.fetch_add(1, std::memory_order_relaxed);
        r.active = false;
    }
    if (replay_due(r.replayed, r.replayReq, r.replaySeq, L)) {
        if (!budget_left(used)) {
            if (!r.active) { c_writerFrames[static_cast<int>(WriterWhy::Budget)].fetch_add(1, std::memory_order_relaxed); return Rp::Budget; }
            return Rp::Routed;   // the one in flight goes on; the new one next frame
        }
        ++used;
        if (r.replayReq != L.reqId) parity_close(r);
        else if (r.replayed) c_rpReaims.fetch_add(1, std::memory_order_relaxed);   // the same request re-aimed on the host
        r.pendingSeq = post_replay(in.eid, L);
        r.orderAt = in.now;
        r.startSeen = false;
        r.replayed = true; r.replayReq = L.reqId; r.replaySeq = L.seq;
        r.active = true; r.exact = (L.flags & kRecExact) != 0; r.endedAt = -1;
        std::memcpy(r.orderPos, L.goal, 12);
        r.lastOrderAt = in.now;
        ++r.orders;
        const uint32_t k = c_rpOrders.fetch_add(1, std::memory_order_relaxed) + 1;
        c_rpSpeed[L.speed < 12 ? L.speed : 11].fetch_add(1, std::memory_order_relaxed);
        if (r.exact) c_rpExact.fetch_add(1, std::memory_order_relaxed);
        if (k <= 60 || k % 500 == 0)
            logf("WO216-REPLAY npc=%s the host's request %u (seq %u): speed %u style %u to (%.1f,%.1f,%.1f)%s tol %.2f/%.2f, the copy %.2f m off the "
                 "stream order=%u [n=%u]", r.name.c_str(), L.reqId, L.seq, L.speed, L.style, L.goal[0], L.goal[1], L.goal[2],
                 r.exact ? " exact" : "", L.tolA, L.tolB, off, r.pendingSeq, k);
    }
    r.parSum += off;
    ++r.parN;
    c_rpFrames.fetch_add(1, std::memory_order_relaxed);
    return Rp::Routed;
}

RouteOut route_frame_inner(const RouteIn& in);
RouteOut route_frame(const RouteIn& in) {
    const RouteOut o = route_frame_inner(in);
    auto it = g_routes.find(in.eid);
    if (it != g_routes.end()) {
        Route& r = it->second;
        if (o.v == Verdict::Routed) { if (r.replay) ++r.fReplay; else ++r.fRoute; }
        else if (o.v == Verdict::Correct) ++r.fCorrect;
        else { ++r.fWriter; ++r.fWhy[static_cast<int>(o.why) < static_cast<int>(WriterWhy::kCount) ? static_cast<int>(o.why) : 0]; }
    }
    return o;
}

RouteOut route_frame_inner(const RouteIn& in) {
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
                if (r.replay) {   // WO-216: the host's request refused here -- WO-215's way for a while (the writer has it first, below)
                    count_cause(r, wo216rules::Cause::Refused, 0.0f, 0.0f);
                    r.replayRestUntil = in.now + wo216rules::kReplayRestS;
                    replay_leave(r, false);
                }
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
    // WO-216 E: a horse is its host's own request's when the host's horse has one (with or without its rider): the replay, never the
    // writer's entity writes; without a live request line it stays the writer's (WO-210's horse rules)
    bool horseReplay = false;
    if (in.horse && !in.figure && !in.parented && wo216::on(wo216rules::kBitRiders) && wo216::on(wo216rules::kBitRouteReplay) && on(kBitRoute) &&
        g_armed.load(std::memory_order_relaxed) && in.now >= r.refusedUntil) {
        auto li = g_lines.find(in.eid);
        horseReplay = li != g_lines.end() && (li->second.rec.flags & wo216rules::kRecActive) && (li->second.rec.flags & wo216rules::kRecGoal) &&
                      wo216rules::line_live(true, in.now - li->second.at);
        if (!horseReplay && r.replay) horseReplay = true;   // its replay plays out (the frame below lets it go when the line ends)
    }
    WriterWhy why = horseReplay ? WriterWhy::None
                                : writer_reason(on(kBitRoute), in.figure, in.beast, in.parented, air, in.fight, in.now < r.refusedUntil);
    if (why == WriterWhy::None && !in.human && !horseReplay) why = WriterWhy::NotHuman;
    if (why == WriterWhy::None && !in.physics && !horseReplay) why = WriterWhy::NoPhysics;
    if (why == WriterWhy::None) {   // WO-216: far from this player (the encounter pool's despawn range): the writer's, our request cancelled
        float me[3];
        if (player_pos(me) && route_far(dist2d(me, in.newest))) why = WriterWhy::Far;
    }
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
        if (r.replay) { parity_close(r); r.replay = false; r.replayed = false; }
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
    // WO-216 A.1: the host's own request, replayed once per change; the stream only corrects
    if (wo216::on(wo216rules::kBitRouteReplay)) {
        const Rp rp = replay_frame(r, in, body, s_used);
        if (horseReplay && rp == Rp::Fallback) {   // E: a horse leaves the replay to the writer (WO-215's walk-to-sample never takes a horse)
            c_writerFrames[static_cast<int>(WriterWhy::Beast)].fetch_add(1, std::memory_order_relaxed);
            out.v = Verdict::Writer; out.why = WriterWhy::Beast;
            return out;
        }
        if (horseReplay && rp == Rp::Routed) c_rpHorseFrames.fetch_add(1, std::memory_order_relaxed);
        if (rp == Rp::Routed) {
            meter(r, in, body);
            c_routedFrames.fetch_add(1, std::memory_order_relaxed);
            if (dtSeen > 0.0 && dtSeen < 0.5) {
                const uint32_t ms = static_cast<uint32_t>(dtSeen * 1000.0 + 0.5);
                c_routedBodyMs.fetch_add(ms, std::memory_order_relaxed);
                c_rpBodyMs.fetch_add(ms, std::memory_order_relaxed);
            }
            r.lastWhy = WriterWhy::None;
            out.v = Verdict::Routed; out.why = WriterWhy::None;
            return out;
        }
        if (rp == Rp::Correct) { out.v = Verdict::Correct; out.why = WriterWhy::Correcting; return out; }
        if (rp == Rp::Budget) { out.v = Verdict::Writer; out.why = WriterWhy::Budget; return out; }
        c_rpFallFrames.fetch_add(1, std::memory_order_relaxed);
    } else if (r.replay) replay_leave(r, true);
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
        // WO-216 B: how far from the host's player each parked body is (the briefing's distribution; the leash is not acted on)
        float me[3]{}, at[3]{};
        const bool haveD = player_pos(me) && engine::entity_world_pos(ent, at);
        logf("WO215-CENSUS npc=%s dormant (full body empty, physics %s, brain %s state=%d): not streamed until it wakes dist_host_m=%.0f [n=%u]",
             name ? name : "?", c.physics ? "yes" : "none", c.brainRun ? "running" : "not running", st, haveD ? dist2d(me, at) : -1.0f, k);
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

void streamed_cb(const char* name, uint32_t eid, bool notHuman, void* ctx) {
    auto* v = static_cast<std::vector<CapBody>*>(ctx);
    CapBody b; b.eid = eid; b.name = name ? name : ""; b.beast = notHuman;
    v->push_back(std::move(b));
}

void tick() {
    const double now = now_s();
    probe_tick(now);
    // WO-216 A.0 (the host): the bodies its sender streams, for the AI update's read (every half second)
    static double s_setAt = 0;
    if (now - s_setAt >= 0.5) {
        s_setAt = now;
        std::vector<CapBody> set;
        if (wo216::on(wo216rules::kBitRouteReplay) && g_api.capArmed) wo138::for_each_streamed(&streamed_cb, &set);
        std::lock_guard<std::mutex> l(g_capMu);
        g_capSet.swap(set);
        for (const CapBody& b : g_capSet) g_capNames[b.eid] = b.name;
        if (g_capNames.size() > 4096) g_capNames.clear();
    }
    g_capProbeEid.store(g_probe.on ? g_probe.eid : 0, std::memory_order_relaxed);
    // WO-216 A.1 (the joiner): the host's lines that came in off the main thread
    {
        std::unordered_map<uint32_t, Line> in;
        {
            std::lock_guard<std::mutex> l(g_lineMu);
            in.swap(g_linePending);
        }
        for (auto& kv : in) g_lines[kv.first] = kv.second;
        static double s_linePrune = 0;
        if (now - s_linePrune > 10.0) {
            s_linePrune = now;
            for (auto it = g_lines.begin(); it != g_lines.end();) { if (now - it->second.at > 60.0) it = g_lines.erase(it); else ++it; }
        }
    }
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
        // WO-216: each body's frames by verdict over the last 30 s (the analysis measures replayed copies apart from written ones)
        for (auto& kv : g_routes) {
            Route& r = kv.second;
            const uint32_t all = r.fReplay + r.fRoute + r.fCorrect + r.fWriter;
            if (all >= 30 && (r.fReplay || r.fRoute || r.fCorrect)) {
                int top = 0;
                for (int i = 1; i < static_cast<int>(WriterWhy::kCount); ++i) if (r.fWhy[i] > r.fWhy[top]) top = i;
                logf("WO216-BODY npc=%s replay=%u route=%u correct=%u writer=%u writer_why=%s", r.name.c_str(), r.fReplay, r.fRoute, r.fCorrect, r.fWriter,
                     r.fWriter ? writer_why(static_cast<WriterWhy>(top)) : "-");
            }
            r.fReplay = r.fRoute = r.fCorrect = r.fWriter = 0;
            for (auto& w : r.fWhy) w = 0;
        }
    }
}

// WO-216 A: the host's read and the joiner's replay, one line (wo216.cpp's status carries it)
int replay_status(char* out, int n) {
    using namespace wo216rules;
    uint32_t pb[kParityBins], tot = 0;
    for (int i = 0; i < kParityBins; ++i) { pb[i] = c_rpParity[i].load(); tot += pb[i]; }
    int p50 = -1, p90 = -1;
    uint32_t acc = 0;
    for (int i = 0; i < kParityBins; ++i) { acc += pb[i]; if (p50 < 0 && tot && acc * 2 >= tot) p50 = i; if (p90 < 0 && tot && acc * 10 >= tot * 9) p90 = i; }
    size_t outN = 0, linesN = 0;
    {
        std::lock_guard<std::mutex> l(g_capMu);
        outN = g_capOut.size();
        linesN = g_capSet.size();
    }
    uint32_t cs[static_cast<int>(Cause::kCount)];
    for (int i = 0; i < static_cast<int>(Cause::kCount); ++i) cs[i] = c_rpCause[i].load();
    const uint32_t corr = cs[0] + cs[1] + cs[2] + cs[3];
    const double bodyMin = c_rpBodyMs.load() / 60000.0;
    uint32_t sb[21], st = 0;
    for (int i = 0; i < 21; ++i) { sb[i] = c_rpStartBins[i].load(); st += sb[i]; }
    int sp50 = -1, sp90 = -1;
    uint32_t sacc = 0;
    for (int i = 0; i < 21; ++i) { sacc += sb[i]; if (sp50 < 0 && st && sacc * 2 >= st) sp50 = i * 100; if (sp90 < 0 && st && sacc * 10 >= st * 9) sp90 = i * 100; }
    const int k = std::snprintf(out, static_cast<size_t>(n),
        "A.0 read=%s (%s) set=%zu ticks=%u tasks=%u bodies=%u lines=%u taken=%u pending=%zu active=%u goal=%u exact=%u pre_path=%u no_task=%u "
        "no_npc=%u faults=%u speeds 0=%u 1=%u 2=%u 3=%u 4=%u 7=%u | A.1 lines_in=%u replay_orders=%u exact=%u replay_frames=%u replay_body_s=%.0f "
        "fallback_frames=%u (no_line=%u no_goal=%u rest=%u) host_end_cancels=%u arrived=%u ended_near=%u ended_seen=%u stall_holds=%u "
        "route_corrections=%u (drift=%u refused=%u no_path=%u stall=%u) per_body_min=%.2f | A.2 routes=%u parity_mean_m=%.2f p50_bin=%d p90_bin=%d "
        "(10 cm) | A.3 replay_gait windows=%u within15=%u | stalls moving_windows=%u stall_windows=%u stall_after_order=%u start_p50_ms=%d start_p90_ms=%d "
        "reaims host=%u replayed=%u host_arrived=%u finished=%u | E horse_replay_frames=%u",
        g_api.capArmed ? "armed" : "unarmed", g_api.capWhy, linesN, c_capTicks.load(), c_capTasks.load(), c_capBodies.load(), c_capLines.load(),
        c_capTaken.load(), outN, c_capActive.load(), c_capGoal.load(), c_capExact.load(), c_capPrePath.load(), c_capNoTask.load(), c_capNoNpc.load(),
        c_capFaults.load(), c_capSpeed[0].load(), c_capSpeed[1].load(), c_capSpeed[2].load(), c_capSpeed[3].load(), c_capSpeed[4].load(), c_capSpeed[7].load(),
        c_rpLinesIn.load(), c_rpOrders.load(), c_rpExact.load(), c_rpFrames.load(), c_rpBodyMs.load() / 1000.0, c_rpFallFrames.load(),
        c_rpNoLine.load(), c_rpNoGoal.load(), c_rpRest.load(), c_rpHostEnd.load(), c_rpArrived.load(), c_rpEndedNear.load(), c_endedSeen.load(),
        c_rpStallHolds.load(), corr, cs[0], cs[1], cs[2], cs[3], bodyMin > 0.01 ? corr / bodyMin : 0.0, c_rpRoutes.load(),
        g_rpParN ? g_rpParSum / g_rpParN : 0.0, p50, p90, c_rpMeterN.load(), c_rpMeterOk.load(), c_rpMovingFrames.load(), c_rpStallFrames.load(),
        c_rpStallStart.load(), sp50, sp90, c_capReaims.load(), c_rpReaims.load(), c_rpHostArrived.load(), c_rpFinished.load(), c_rpHorseFrames.load());
    if (k <= 0 || k >= n) return k;
    // every speed class the host's brains asked for, and the copies' replays of them (class:count, the classes seen only)
    int m = k;
    for (int pass = 0; pass < 2 && m < n; ++pass) {
        m += std::snprintf(out + m, static_cast<size_t>(n - m), pass == 0 ? " | speed_classes host=" : " replay=");
        bool any = false;
        for (int i = 0; i < 12 && m < n; ++i) {
            const uint32_t v = pass == 0 ? c_capSpeed[i].load() : c_rpSpeed[i].load();
            if (!v) continue;
            m += std::snprintf(out + m, static_cast<size_t>(n - m), "%s%d:%u", any ? "," : "", i, v);
            any = true;
        }
        if (!any && m < n) m += std::snprintf(out + m, static_cast<size_t>(n - m), "-");
    }
    return m < n ? m : n - 1;
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
                         "parented=%u air=%u fight=%u refused=%u no_npc=%u not_armed=%u correcting=%u figure=%u budget=%u not_human=%u no_physics=%u far=%u | A.2 gait orders walk=%u jog=%u run=%u "
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
                         w[static_cast<int>(WriterWhy::NoPhysics)], w[static_cast<int>(WriterWhy::Far)], c_gaitOrders[0].load(), c_gaitOrders[1].load(), c_gaitOrders[2].load(),
                         c_gaitOrders[3].load(), c_gaitChanges.load(), c_meterN.load(), c_meterOk.load(), p50, p90, c_dormantNow.load(),
                         c_wentDormant.load(), c_woke.load(), c_dormantSkips.load(), c_ghosts.load(), c_cancelStale.load(), miss, wc[1], wc[2], wc[0], wc[4], wc[3], wc[5]);
}

} // namespace kcdmp::wo215
