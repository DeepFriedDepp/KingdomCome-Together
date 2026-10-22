// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-200 -- see wo200.h.
#include "wo200.h"
#include "wo202.h"
#include "wo202_rules.h"
#include "wo200_rules.h"

#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

#include "anchors.h"
#include "fault_guard.h"
#include "hook_prologues.h"
#include "inline_hook.h"
#include "log.h"
#include "pe_exports.h"
#include "rttr_abi.h"
#include "wo137.h"

namespace kcdmp::wo200 {

using namespace wo200rules;

namespace {

uint64_t now_ms() { return GetTickCount64(); }

// ---------------------------------------------------------------------------
// Guarded primitives (each __try in its own function; nothing that unwinds).
// ---------------------------------------------------------------------------
template <class T> bool rd(const void* base, size_t off, T* out) {
    KCDMP_FAULT_READ(site, "wo200::rd");
    if (!base || !fault::plausible_address(reinterpret_cast<uintptr_t>(base))) return false;
    return fault::guarded(site, [&] { *out = *reinterpret_cast<const T*>(static_cast<const char*>(base) + off); });
}
bool copy_cstr(const char* s, char* out, size_t n) {
    if (!s || n == 0) return false;
    KCDMP_FAULT_READ(site, "wo200::copy_cstr");
    if (fault::guarded(site, [&] {
        size_t i = 0;
        for (; i + 1 < n && s[i]; ++i) out[i] = s[i];
        out[i] = 0;
    })) return true;
    out[0] = 0;
    return false;
}
// The class name behind an object's vptr (RTTI locator) and the object's full address (the COL's offset back).
bool rtti_name(const void* obj, char* out, size_t n) {
    out[0] = 0;
    const void* vptr = nullptr; const void* col = nullptr;
    if (!obj || !rd(obj, 0, &vptr) || !vptr || !rd(vptr, static_cast<size_t>(-8), &col) || !col) return false;
    uint32_t sig = 0, tdRva = 0, selfRva = 0;
    if (!rd(col, 0, &sig) || sig != 1 || !rd(col, 12, &tdRva) || !rd(col, 20, &selfRva)) return false;
    const char* base = static_cast<const char*>(col) - selfRva;
    return copy_cstr(base + tdRva + 0x10, out, n);
}
// Does the class behind this vftable derive from `baseDecorated` (".?AVC_Effect@conceptmodule@wh@@")? Its locator's hierarchy, read.
bool derives_from(const void* vft, const char* baseDecorated) {
    const void* col = nullptr;
    if (!vft || !rd(vft, static_cast<size_t>(-8), &col) || !col) return false;
    uint32_t sig = 0, chdRva = 0, selfRva = 0;
    if (!rd(col, 0, &sig) || sig != 1 || !rd(col, 16, &chdRva) || !rd(col, 20, &selfRva)) return false;
    const char* base = static_cast<const char*>(col) - selfRva;
    const char* chd = base + chdRva;
    uint32_t n = 0, arrRva = 0;
    if (!rd(chd, 8, &n) || !rd(chd, 12, &arrRva) || n > 64) return false;
    for (uint32_t i = 0; i < n; ++i) {
        uint32_t bcdRva = 0, tdRva = 0;
        if (!rd(base + arrRva, i * 4, &bcdRva) || !rd(base + bcdRva, 0, &tdRva)) return false;
        char nm[160];
        if (copy_cstr(base + tdRva + 0x10, nm, sizeof nm) && !std::strcmp(nm, baseDecorated)) return true;
    }
    return false;
}

// A pointer that can be an object's: user range, 8-aligned. A vtable must also lie in a loaded image -- some soul-typed ports hold a
// handle by value ({1, 2}), and treating it as an object faulted every read (the 0.61.5 runs: wo200::rd 101 per session).
bool plausible_ptr(const void* p) {
    const auto u = reinterpret_cast<uintptr_t>(p);
    return u >= 0x10000 && u < 0x7FFFFFFF0000ull && (u & 7) == 0;
}
bool image_ptr(const void* p) {
    if (!plausible_ptr(p)) return false;
    MEMORY_BASIC_INFORMATION mbi{};
    return VirtualQuery(p, &mbi, sizeof mbi) == sizeof mbi && mbi.Type == MEM_IMAGE && mbi.State == MEM_COMMIT;
}

const void* full_object(const void* obj) {
    const void* vptr = nullptr; const void* col = nullptr;
    if (!plausible_ptr(obj) || !rd(obj, 0, &vptr) || !image_ptr(vptr) || !rd(vptr, static_cast<size_t>(-8), &col) || !col) return obj;
    uint32_t sig = 0, off = 0;
    if (!rd(col, 0, &sig) || sig != 1 || !rd(col, 4, &off)) return obj;
    return static_cast<const char*>(obj) - off;
}

// ---------------------------------------------------------------------------
// Anchors: exports and RTTI names only; the one patched slot is verified by what its function is.
// ---------------------------------------------------------------------------
struct Anchors {
    HMODULE cm = nullptr, cs = nullptr;
    void* execute = nullptr;          // C_Node::Execute (the gate's target)
    void* portGetNode = nullptr;      // I_Port::GetNode -> _smart_ptr<C_Node>
    void* portGetType = nullptr;      // I_Port::GetType -> rttr::type
    void* portDirection = nullptr;    // I_Port::GetDirection
    void* release = nullptr;          // C_SharedResource::Release
    void* const* outTrigVft = nullptr;   // C_OutputTriggerPort
    void* const* trigVfts[4]{};          // every trigger-port class sharing C_ActiveTriggerPort's Trigger: Active, Output, Interface, Static
    int nTrigVfts = 0;
    void* const* iPortVft = nullptr;     // I_Port (to verify the Read slot)
    void* portRead = nullptr;            // I_Port::Read (export) -- slot 16 of I_Port's vftable
    size_t portsBegin = 0, portsEnd = 0; // C_Node's port vector, read out of GetPort's own code
    // CrySystem rttr
    void* vDtor = nullptr, *vValid = nullptr, *vRawPtr = nullptr, *vIsSeq = nullptr, *vSeqView = nullptr;
    void* seqSize = nullptr, *seqValue = nullptr, *seqDtor = nullptr, *tGetName = nullptr;
    bool tap = false, gate = false, ports = false;
    // research (V.1 probe O1): the engine's own per-node hibernation, by export name
    void* nodeHibernate = nullptr, *nodeWake = nullptr;
} A;

constexpr size_t kSlotPortName = 9;     // I_Port::GetName (WO-97)
constexpr size_t kSlotPortTrigger = 15; // I_Port::Trigger (WO-97/99.5)
constexpr size_t kSlotPortRead = 16;    // I_Port::Read (verified against the export at install)
constexpr size_t kOffClockState = 0x68; // C_StateBase<enum>'s value byte (RPGModule's setter's own operand: movzx eax, byte [r15+0x68])

using TriggerFn = void (*)(void* port);
TriggerFn g_origOut = nullptr;

// --- the class table -------------------------------------------------------
struct ClassRow { const char* module; const char* rtti; Kind kind; };
const ClassRow kClasses[] = {
    {"DialogModule.dll", ".?AVC_DialogueNode@data@dialogmodule@wh@@", Kind::Dialogue},
    {"DialogModule.dll", ".?AVC_DialogueWrapper@data@dialogmodule@wh@@", Kind::Dialogue},
    {"DialogModule.dll", ".?AVC_ForcedDialogueNode@data@dialogmodule@wh@@", Kind::DialogueStarter},
    {"DialogModule.dll", ".?AVC_FaderDialogueNode@data@dialogmodule@wh@@", Kind::DialogueStarter},
    {"RPGModule.dll", ".?AVC_Timer@rpgmodule@wh@@", Kind::Clock},
    {"RPGModule.dll", ".?AVC_TimeOfDayWatch@rpgmodule@wh@@", Kind::Clock},
    {"ConceptModule.dll", ".?AVC_SceneFinishedWaiter@conceptmodule@wh@@", Kind::Waiter},
    {"XGenAIModule.dll", ".?AVC_EnableBehavior@xgenaimodule@wh@@", Kind::Behaviour},
    {"XGenAIModule.dll", ".?AVC_SetEntityContext@xgenaimodule@wh@@", Kind::EntityContext},
    {"XGenAIModule.dll", ".?AVC_SetEntityContextPreset@xgenaimodule@wh@@", Kind::EntityContext},
    {"XGenAIModule.dll", ".?AVC_SchedulerLinkActivation@xgenaimodule@wh@@", Kind::SchedulerLink},
    {"XGenAIModule.dll", ".?AVC_AddInterruptConceptNode@xgenaimodule@wh@@", Kind::Interrupt},
    {"ConceptModule.dll", ".?AVC_Function@conceptmodule@wh@@", Kind::Function},
    {"ConceptModule.dll", ".?AVC_MemberFunction@conceptmodule@wh@@", Kind::Function},
    {"ConceptModule.dll", ".?AVC_EventFunction@conceptmodule@wh@@", Kind::Function},
    {"ConceptModule.dll", ".?AVC_EventMemberFunction@conceptmodule@wh@@", Kind::Function},
    {"AnimationModule.dll", ".?AVC_FocusCameraNode@animationmodule@wh@@", Kind::View},
    {"PlayerModule.dll", ".?AVC_DisplayTutorial@playermodule@wh@@", Kind::View},
    {"GUIModule.dll", ".?AVC_PlayTrackView@guimodule@wh@@", Kind::View},
    {"RPGModule.dll", ".?AVC_AddReward@rpgmodule@wh@@", Kind::RewardPlayer},
    {"PlayerModule.dll", ".?AVC_PlayerOutfitOverride@playermodule@wh@@", Kind::RewardPlayer},
    {"RPGModule.dll", ".?AVC_BuffEffect@rpgmodule@wh@@", Kind::BodyEffect},
    {"RPGModule.dll", ".?AVC_ClothingPresetOverride@rpgmodule@wh@@", Kind::BodyEffect},
    {"RPGModule.dll", ".?AVC_WeaponPresetOverride@rpgmodule@wh@@", Kind::BodyEffect},
    {"GUIModule.dll", ".?AVC_CutsceneHandler@guimodule@wh@@", Kind::SceneStarter},   // WO-203 I.5
};
constexpr int kNClasses = static_cast<int>(sizeof kClasses / sizeof kClasses[0]);
const void* g_vft[kNClasses]{};
bool g_isEffect[kNClasses]{};
int g_found = 0;
size_t g_offEffectActive = 0;   // C_Effect's own "is active" byte, read out of C_Effect::ExecuteNode (0 = not found)

// WO-203 I.5: what a scene handler's Execute starts (WO-193's reads, as wo194 makes them; each object's class is checked by RTTI before
// any field of it is used, and the handler's state only when the field after it is what it should be)
constexpr size_t kOffHandlerState = 0x338;   // C_CutsceneHandler: its state (0 idle, 1 queued, 2 ready, 3 playing, 4 ended, 5 finishing)
constexpr size_t kOffHandlerCfg = 0x340;     // C_CutsceneHandler: its configuration (shared_ptr; the enqueue builds it -- empty before the first)
constexpr size_t kOffCfgCutscene = 0x30;     // C_CutsceneConfiguration: the I_Cutscene from the cutscene database
constexpr size_t kOffCutsceneName = 0x08;    // I_Cutscene: its name (char*)
constexpr char kRttiHandler[] = ".?AVC_CutsceneHandler@guimodule@wh@@";
constexpr char kRttiSceneCfg[] = ".?AVC_CutsceneConfiguration@guimodule@wh@@";
constexpr char kRttiFader[] = ".?AVC_FaderCutscene@guimodule@wh@@";     // E_CutsceneType Fader (WO-149's census)
constexpr char kRttiIngame[] = ".?AVC_IngameCutscene@guimodule@wh@@";   // E_CutsceneType Ingame
const void* g_vftSceneCfg = nullptr;
const void* g_vftFader = nullptr;
const void* g_vftIngame = nullptr;

int class_row(const void* vt) {
    if (!vt) return -1;
    for (int i = 0; i < kNClasses; ++i)
        if (g_vft[i] == vt) return i;
    return -1;
}
Kind classify(const void* vt) {
    const int i = class_row(vt);
    return i < 0 ? Kind::None : kClasses[i].kind;
}
Kind classify_node(const void* node) {
    const void* vt = nullptr;
    return rd(node, 0, &vt) ? classify(vt) : Kind::None;
}

// --- configuration -----------------------------------------------------------
std::atomic<uint8_t> g_role{kRoleUnknown};
std::atomic<bool> g_joined{false};
std::atomic<uint8_t> g_tapBits{0}, g_gateBits{0}, g_logBits{1}, g_valueBits{0};
std::atomic<bool> g_tapOn{false}, g_gateOn{false};
std::atomic<const void*> g_playerFull{nullptr};

void recompute() {
    const bool role = g_role.load() != kRoleUnknown;
    g_tapOn = A.tap && role && g_tapBits.load() != 0;
    g_gateOn = A.gate && role && g_gateBits.load() != 0;
}

// --- thread marks ---------------------------------------------------------------
thread_local uint32_t t_closure = 0;
thread_local uint32_t t_sceneCarry = 0;   // WO-203 I.5: the closure a scene's out-port carries (Tap::ContinueLocal) -- the node gate's only
thread_local int t_inGate = 0;            // our own reads inside the gate run nodes' data inputs: no gate for them
thread_local bool t_mirrorTalker = false;
thread_local const void* t_srcNode = nullptr;   // WO-202 II.4: the out-port pulse running on this thread
thread_local const void* t_srcPort = nullptr;

// --- counters --------------------------------------------------------------------
std::atomic<uint32_t> c_tapCalls{0}, c_captured{0}, c_quiet{0}, c_continued{0}, c_gateCalls{0}, c_gateClassified{0}, c_targetReads{0};
std::atomic<uint32_t> c_verdict[static_cast<int>(Verdict::Count)]{};
std::atomic<uint32_t> c_replays{0}, c_replayRefused{0}, c_researchDrops{0};
std::atomic<uint32_t> c_sceneContinued{0};   // WO-203 I.5: scene out-ports that carried their closure to the node gate

// --- events ----------------------------------------------------------------------
struct Ev { uint32_t seq; uint8_t kind; uint32_t closure; uint64_t atMs; std::string a, b; };
std::mutex g_evMx;
std::vector<Ev> g_ev;            // the last kMaxEv events
std::atomic<uint32_t> g_evSeq{0};
constexpr size_t kMaxEv = 400;

void push_event(uint8_t kind, uint32_t closure, const char* a, const char* b) {
    std::lock_guard<std::mutex> lk(g_evMx);
    if (g_ev.size() >= kMaxEv) g_ev.erase(g_ev.begin(), g_ev.begin() + (g_ev.size() - kMaxEv + 1));
    g_ev.push_back(Ev{g_evSeq.fetch_add(1) + 1, kind, closure, now_ms(), a ? a : "", b ? b : ""});
}

// --- closure marks (waiters, scene handlers) ------------------------------------------
struct Mark { uint32_t id; uint64_t until; };
std::mutex g_markMx;
std::unordered_map<const void*, Mark> g_marks;          // waiter / scene handler node -> the closure that enqueued it
std::unordered_set<const void*> g_closureClassSeen;     // vftables already reported as met in a closure
std::unordered_set<uint64_t> g_sceneMeetSeen;           // WO-203 I.5: (closure, handler) meets already logged -- each closure's own
std::unordered_set<std::string> g_refusedSeen;          // "verdict|path" already logged
std::unordered_map<std::string, uint64_t> g_storyLogLast;   // WO-203 I.5: path -> when its always-logged refusal was last logged
std::unordered_map<const void*, uint64_t> g_quietLast;  // clock node -> when its drop was last logged
std::atomic<const void*> g_researchDrop{nullptr};        // O4-narrow: this node's next out-port fire is dropped (research)

std::unordered_set<const void*> g_waiterDropOnce;   // host: a waiter whose OnFinished a replay fired -- its own late one is dropped

// g_markMx held: `node` carries closure `cid` for the closure's life.
void mark_locked(const void* node, uint32_t cid) {
    g_marks[node] = Mark{cid, now_ms() + kClosureTtlMs};
    if (g_marks.size() > 4096) {
        const uint64_t t = now_ms();
        for (auto it = g_marks.begin(); it != g_marks.end();) it = t > it->second.until ? g_marks.erase(it) : std::next(it);
    }
}

bool marked_closure(const void* node, uint32_t* id) {
    std::lock_guard<std::mutex> lk(g_markMx);
    auto it = g_marks.find(node);
    if (it == g_marks.end()) return false;
    if (now_ms() > it->second.until) { g_marks.erase(it); return false; }
    *id = it->second.id;
    return true;
}

bool is_trigger_port_vft(const void* vt) {
    for (int i = 0; i < A.nTrigVfts; ++i) if (A.trigVfts[i] == vt) return true;
    return false;
}

// ---------------------------------------------------------------------------
// Engine calls
// ---------------------------------------------------------------------------
using GetNodeFn = void* (*)(const void* port, void** ret);
using GetTypeFn = void* (*)(const void* port, void* retType);
using DirFn = int (*)(const void* port);
using ReleaseFn = void (*)(const void* res);
using ReadFn = void* (*)(const void* port, rttr::Variant* ret);
using VDtorFn = void (*)(rttr::Variant* v);
using VBoolFn = bool (*)(const rttr::Variant* v);
using VPtrFn = void* (*)(const rttr::Variant* v);
using VSeqFn = void* (*)(const rttr::Variant* v, void* retView);
using SeqSizeFn = size_t (*)(const void* view);
using SeqValueFn = void* (*)(const void* view, rttr::Variant* ret, size_t i);
using SeqDtorFn = void (*)(void* view);
using TNameFn = void* (*)(const void* type, void* retSv);

void call_release(const void* res) {
    if (!res || !A.release) return;
    KCDMP_FAULT_CALL(site, "wo200::release");
    fault::guarded(site, [&] { reinterpret_cast<ReleaseFn>(A.release)(res); });
}
void* port_owner(const void* port) {
    if (!A.portGetNode) return nullptr;
    KCDMP_FAULT_CALL(site, "wo200::port_owner");
    void* node = nullptr;
    if (!fault::guarded(site, [&] { reinterpret_cast<GetNodeFn>(A.portGetNode)(port, &node); })) return nullptr;
    return node;
}
bool port_name(const void* port, char* out, size_t n) {
    out[0] = 0;
    const void* vt = nullptr; void* fn = nullptr;
    if (!port || !rd(port, 0, &vt) || !vt || !rd(vt, kSlotPortName * 8, &fn) || !fn) return false;
    KCDMP_FAULT_CALL(site, "wo200::port_name");
    const void* ref = nullptr;
    if (!fault::guarded(site, [&] { ref = reinterpret_cast<const void* (*)(const void*)>(fn)(port); }) || !ref) return false;
    const char* s = nullptr;
    return rd(ref, 0, &s) && s && copy_cstr(s, out, n);
}

// ---------------------------------------------------------------------------
// Who a node's soul inputs name. Each In port whose type is a soul type is read the way the node itself reads it (I_Port::Read), and
// every soul in the value (one, or a sequence through rttr's own view) is compared with the local player's soul (full objects).
// ---------------------------------------------------------------------------
bool type_name_of(const void* port, char* out, size_t n) {
    out[0] = 0;
    if (!A.portGetType || !A.tGetName) return false;
    KCDMP_FAULT_CALL(site, "wo200::port_type");
    struct { const char* d; size_t n; } sv{nullptr, 0};
    if (!fault::guarded(site, [&] {
        void* type[2] = {nullptr, nullptr};
        reinterpret_cast<GetTypeFn>(A.portGetType)(port, type);
        reinterpret_cast<TNameFn>(A.tGetName)(type, &sv);
    }) || !sv.d) return false;
    const size_t m = sv.n < n - 1 ? sv.n : n - 1;
    KCDMP_FAULT_READ(rsite, "wo200::port_type_copy");
    if (!fault::guarded(rsite, [&] { std::memcpy(out, sv.d, m); })) return false;
    out[m] = 0;
    return true;
}
int port_dir(const void* port) {
    if (!A.portDirection) return -1;
    KCDMP_FAULT_CALL(site, "wo200::port_dir");
    int d = -1;
    fault::guarded(site, [&] { d = reinterpret_cast<DirFn>(A.portDirection)(port); });
    return d;
}
bool read_port(const void* port, rttr::Variant* v) {
    void* fn = nullptr; const void* vt = nullptr;
    if (!rd(port, 0, &vt) || !vt || !rd(vt, kSlotPortRead * 8, &fn) || !fn) return false;
    KCDMP_FAULT_CALL(site, "wo200::port_read");
    std::memset(v, 0, sizeof *v);
    return fault::guarded(site, [&] { reinterpret_cast<ReadFn>(fn)(port, v); });
}
void variant_dtor(rttr::Variant* v) {
    KCDMP_FAULT_CALL(site, "wo200::variant_dtor");
    fault::guarded(site, [&] { reinterpret_cast<VDtorFn>(A.vDtor)(v); });
}
// One value's souls: `fn(soulFull)` for each; the count seen.
template <class F> int each_soul(rttr::Variant* v, F&& fn) {
    KCDMP_FAULT_CALL(site, "wo200::variant_souls");
    int seen = 0;
    bool valid = false;
    if (!fault::guarded(site, [&] { valid = reinterpret_cast<VBoolFn>(A.vValid)(v); }) || !valid) return 0;
    bool seq = false;
    fault::guarded(site, [&] { seq = reinterpret_cast<VBoolFn>(A.vIsSeq)(v); });
    if (!seq) {
        void* p = nullptr;
        if (fault::guarded(site, [&] { p = reinterpret_cast<VPtrFn>(A.vRawPtr)(v); }) && plausible_ptr(p)) { fn(full_object(p)); ++seen; }
        return seen;
    }
    alignas(16) unsigned char view[512]{};
    if (!fault::guarded(site, [&] { reinterpret_cast<VSeqFn>(A.vSeqView)(v, view); })) return 0;
    size_t n = 0;
    fault::guarded(site, [&] { n = reinterpret_cast<SeqSizeFn>(A.seqSize)(view); });
    if (n > 64) n = 64;
    for (size_t i = 0; i < n; ++i) {
        rttr::Variant e{};
        if (!fault::guarded(site, [&] { reinterpret_cast<SeqValueFn>(A.seqValue)(view, &e, i); })) break;
        void* p = nullptr;
        if (fault::guarded(site, [&] { p = reinterpret_cast<VPtrFn>(A.vRawPtr)(&e); }) && plausible_ptr(p)) { fn(full_object(p)); ++seen; }
        variant_dtor(&e);
    }
    fault::guarded(site, [&] { reinterpret_cast<SeqDtorFn>(A.seqDtor)(view); });
    return seen;
}

Target read_target(const void* node, bool dump = false) {
    if (!A.ports) return Target::Unknown;
    c_targetReads.fetch_add(1, std::memory_order_relaxed);
    const void* const* b = nullptr; const void* const* e = nullptr;
    if (!rd(node, A.portsBegin, &b) || !rd(node, A.portsEnd, &e) || !b || !e || e < b || e - b > 96) return Target::Unknown;
    const void* player = g_playerFull.load();
    ++t_inGate;
    Target t = Target::Unknown;
    for (const void* const* it = b; it < e; ++it) {
        const void* port = nullptr;
        if (!rd(it, 0, &port) || !port) continue;
        char ty[160]; char nm[96];
        const int dir = port_dir(port);
        const bool typed = type_name_of(port, ty, sizeof ty);
        char pcls[96] = "";
        if (dump) { port_name(port, nm, sizeof nm); rtti_name(port, pcls, sizeof pcls); }
        if (!typed || !is_soul_type(ty) || dir != 1) {
            if (dump) logf("WO200-RESEARCH port %s dir=%d type=%s class=%s", nm, dir, typed ? ty : "?", pcls);
            continue;
        }
        rttr::Variant v{};
        if (!read_port(port, &v)) continue;
        int players = 0, others = 0;
        const int n = each_soul(&v, [&](const void* soul) { if (player && soul == player) ++players; else ++others; });
        variant_dtor(&v);
        if (dump) logf("WO200-RESEARCH port %s dir=%d type=%s souls=%d player=%d other=%d", nm, dir, ty, n, players, others);
        if (players) { t = Target::Player; break; }
        if (others) t = Target::Other;
    }
    --t_inGate;
    return t;
}

// The node's parent module's instance name (library instances keep their library's name).
bool parent_name(const void* node, char* out, size_t n) {
    out[0] = 0;
    char path[480];
    if (!wo137::path_of(node, path, sizeof path)) return false;
    char* last = std::strrchr(path, '.');
    if (!last) return false;
    *last = 0;
    const char* p = std::strrchr(path, '.');
    return copy_cstr(p ? p + 1 : path, out, n);
}

// ---------------------------------------------------------------------------
// WO-203 I.5: what a C_CutsceneHandler's Execute would start. Its state is trusted only when the configuration field after it is what it
// should be (empty, or a C_CutsceneConfiguration by RTTI); the scene's class is read only when the decision needs it (the host inside a
// replay closure). A scene not read says why, once per reason.
// ---------------------------------------------------------------------------
struct HandlerRead { int state = -1; Scene scene = Scene::Unknown; char cls[96]; char name[96]; };
std::atomic<uint32_t> g_sceneWhySaid{0};

void scene_not_read(uint32_t bit, const char* why) {
    if (g_sceneWhySaid.fetch_or(bit) & bit) return;
    logf("WO200-SCENE a scene handler's scene is not read (%s) -- inside a host replay closure its start is refused anyway (fail closed); "
         "said once", why);
}

void read_handler(const void* node, bool wantClass, HandlerRead* h) {
    h->state = -1; h->scene = Scene::Unknown; h->cls[0] = 0; h->name[0] = 0;
    const void* cfg = nullptr; const void* cvt = nullptr;
    const bool cfgRead = rd(node, kOffHandlerCfg, &cfg);
    const bool cfgOk = cfgRead && plausible_ptr(cfg) && g_vftSceneCfg && rd(cfg, 0, &cvt) && cvt == g_vftSceneCfg;
    int32_t st = -1;
    if (cfgRead && (!cfg || cfgOk) && rd(node, kOffHandlerState, &st)) h->state = st;
    if (!handler_may_start(h->state)) { h->scene = Scene::NoStart; return; }
    if (!wantClass) return;
    if (!g_vftSceneCfg || (!g_vftFader && !g_vftIngame)) {
        scene_not_read(1, "C_CutsceneConfiguration, C_FaderCutscene or C_IngameCutscene not resolved by RTTI");
        return;
    }
    if (!cfgRead || !cfg) { scene_not_read(2, "the handler has no configuration yet: its first enqueue builds it"); return; }
    if (!cfgOk) { scene_not_read(4, "the handler's configuration field is not a C_CutsceneConfiguration: another layout?"); return; }
    const void* cs = nullptr; const void* svt = nullptr;
    if (!rd(cfg, kOffCfgCutscene, &cs) || !plausible_ptr(cs) || !rd(cs, 0, &svt) || !image_ptr(svt)) {
        scene_not_read(8, "the configuration holds no readable cutscene");
        return;
    }
    rtti_name(cs, h->cls, sizeof h->cls);
    const char* np = nullptr;
    if (rd(cs, kOffCutsceneName, &np) && np && fault::plausible_address(reinterpret_cast<uintptr_t>(np)) && copy_cstr(np, h->name, sizeof h->name))
        for (char* q = h->name; *q; ++q) if (*q < 0x20 || *q > 0x7E) *q = '?';
    const bool player = svt == g_vftFader || svt == g_vftIngame || derives_from(svt, kRttiFader) || derives_from(svt, kRttiIngame);
    h->scene = player ? Scene::Player : Scene::Other;
}

// ---------------------------------------------------------------------------
// Gate 1: the tap.
// ---------------------------------------------------------------------------
void hooked_out_trigger(void* port) {
    // The engine's own pulse always runs unguarded (a fault deep in the graph is the game's, never ours to swallow).
    if (!g_tapOn.load(std::memory_order_relaxed) && !g_researchDrop.load(std::memory_order_relaxed)) {
        if (wo202::on(wo202rules::kBitQuestFail)) {   // WO-202 II.4: the pulse's port is the source of what it changes (the owner, below, only when asked)
            const void* ps = t_srcPort; const void* ns = t_srcNode;
            t_srcPort = port; t_srcNode = nullptr;
            g_origOut(port);
            t_srcPort = ps; t_srcNode = ns;
            return;
        }
        g_origOut(port); return;
    }
    c_tapCalls.fetch_add(1, std::memory_order_relaxed);
    void* owner = port_owner(port);
    if (!owner) { g_origOut(port); return; }
    const void* drop = g_researchDrop.load();
    if (drop && owner == drop && g_researchDrop.compare_exchange_strong(drop, nullptr)) {   // O4-narrow (research): one fire, any class
        c_researchDrops.fetch_add(1);
        char path[480] = "?", pn[96] = "?";
        wo137::path_of(owner, path, sizeof path); port_name(port, pn, sizeof pn);
        logf("WO200-QUIET research: dropped one fire of %s :: %s (the clock stays readable; its downstream did not run)", path, pn);
        push_event(kEvQuietDrop, 0, pn, path);
        call_release(owner);
        return;
    }
    const Kind k = classify_node(owner);
    const uint32_t cur = t_closure;
    if (k == Kind::Waiter && cur == 0) {
        bool dropIt = false;
        {
            std::lock_guard<std::mutex> lk(g_markMx);
            if (g_waiterDropOnce.count(owner)) {
                char pn0[64] = "";
                port_name(port, pn0, sizeof pn0);
                if (!std::strcmp(pn0, "OnFinished")) { g_waiterDropOnce.erase(owner); dropIt = true; }
            }
        }
        if (dropIt) {
            char path[480] = "?";
            wo137::path_of(owner, path, sizeof path);
            logf("WO200-TAP host: %s :: OnFinished dropped -- the joiner's replay already finished this waiter (its own scene ended late)", path);
            call_release(owner);
            return;
        }
    }
    uint32_t markId = 0;
    const bool marked = (k == Kind::Waiter || k == Kind::SceneStarter) && (g_tapBits.load() & 4) && cur == 0 && marked_closure(owner, &markId);
    const Tap act = tap(g_role.load(), k, g_joined.load(), (g_tapBits.load() & 2) != 0, (g_tapBits.load() & 1) != 0, marked, cur != 0);
    switch (act) {
        case Tap::QuietDrop: {
            c_quiet.fetch_add(1, std::memory_order_relaxed);
            bool log = false;
            {
                std::lock_guard<std::mutex> lk(g_markMx);
                uint64_t& last = g_quietLast[owner];
                const uint64_t t = now_ms();
                if (last == 0 || t - last >= kQuietLogEveryMs) { last = t; log = true; }
            }
            if (log) {
                char path[480] = "?", pn[96] = "?";
                wo137::path_of(owner, path, sizeof path); port_name(port, pn, sizeof pn);
                logf("WO200-QUIET joiner: %s :: %s not run -- the host's clock decides (the host's values arrive as mirrors)", path, pn);
                push_event(kEvQuietDrop, 0, pn, path);
            }
            call_release(owner);
            return;
        }
        case Tap::Capture: {
            const uint32_t id = kLocalClosureBit | (c_captured.fetch_add(1) + 1);
            char path[480] = "?", pn[96] = "?";
            wo137::path_of(owner, path, sizeof path); port_name(port, pn, sizeof pn);
            push_event(kEvCapture, id, pn, path);
            logf("WO200-TAP %s %s :: %s closure=%08X (runs here; the agent decides what goes to the host)",
                 g_role.load() == kRoleHost ? "host" : "joiner", path, pn, id);
            t_closure = id;
            g_origOut(port);
            t_closure = cur;
            call_release(owner);
            return;
        }
        case Tap::Continue: {
            c_continued.fetch_add(1, std::memory_order_relaxed);
            char path[480] = "?", pn[96] = "?";
            wo137::path_of(owner, path, sizeof path); port_name(port, pn, sizeof pn);
            logf("WO200-TAP waiter %s :: %s continues closure %08X", path, pn, markId);
            // the joiner's own conversation goes on here (its scene ended): the agent sends this pulse as a continuation of the replay
            if (closure_is_local(markId)) push_event(kEvCapture, markId, pn, path);
            t_closure = markId;
            g_origOut(port);
            t_closure = cur;
            call_release(owner);
            return;
        }
        case Tap::ContinueLocal: {
            // WO-203 I.5: a scene a closure started (the fader's AfterPlay, frames later): the closure goes on for the node gate only. The
            // joiner's forced dialogue it starts is then his own chain (B7 runs it); on the host it is the replay's (the skip list refuses
            // it). Nothing is captured or sent, and the thread's own closure stays 0: the State changes it makes go out as before.
            c_sceneContinued.fetch_add(1, std::memory_order_relaxed);
            char path[480] = "?", pn[96] = "?";
            wo137::path_of(owner, path, sizeof path); port_name(port, pn, sizeof pn);
            logf("WO200-TAP %s scene %s :: %s continues closure %08X for the node gate (its scene began inside it; nothing is captured or sent)",
                 g_role.load() == kRoleHost ? "host" : "joiner", path, pn, markId);
            if (!_stricmp(pn, "OnFinished")) { std::lock_guard<std::mutex> lk(g_markMx); g_marks.erase(owner); }   // the scene's last port
            const uint32_t pc = t_sceneCarry;
            const void* ps = t_srcPort; const void* ns = t_srcNode;   // WO-202 II.4: the source of what this pulse changes, as for any pulse
            t_srcPort = port; t_srcNode = owner;
            t_sceneCarry = markId;
            g_origOut(port);
            t_sceneCarry = pc;
            t_srcPort = ps; t_srcNode = ns;
            call_release(owner);
            return;
        }
        default:
            break;
    }
    call_release(owner);
    const void* ps = t_srcPort; const void* ns = t_srcNode;   // WO-202 II.4: the source of what this pulse changes
    t_srcPort = port; t_srcNode = owner;
    g_origOut(port);
    t_srcPort = ps; t_srcNode = ns;
}

// ---------------------------------------------------------------------------
// Gate 2: the node gate (C_Node::Execute's entry; any thread the graph runs on).
// ---------------------------------------------------------------------------
void note_closure_class(const void* node, const void* vt, Kind k, uint32_t cid, bool carried) {
    if (k == Kind::Waiter && !carried) {   // a scene's carry marks no waiter: its continuation would go to the agent as the talk's
        std::lock_guard<std::mutex> lk(g_markMx);
        mark_locked(node, cid);
    }
    if (k == Kind::SceneStarter) return;   // WO-203 I.5: marked and logged per closure after the decision (scene_note)
    if (!(g_logBits.load() & 1)) return;
    {
        std::lock_guard<std::mutex> lk(g_markMx);
        if (g_closureClassSeen.size() > 2048 || !g_closureClassSeen.insert(vt).second) return;
    }
    char cls[160] = "?", m[128] = "";
    rtti_name(node, cls, sizeof cls);
    if (k == Kind::Function) wo137::function_method(node, m, sizeof m);
    char b[320];
    _snprintf_s(b, sizeof b, _TRUNCATE, "%s%s%s", cls, m[0] ? " " : "", m);
    logf("WO200-GATE closure %08X meets class %s (%s)%s", cid, b, kind_name(k), k == Kind::None ? " -- unclassified, runs" : "");
    push_event(kEvClosureClass, cid, kind_name(k), b);
}

// WO-203 I.5: a scene handler after the gate's decision -- its closure mark (a scene started inside a closure carries it to the scene's
// out-ports), and its first meet in each closure, logged with what it would start (session C's "unclassified, runs" was logged once per
// process and never again).
void scene_note(const void* node, uint32_t cid, const HandlerRead& h, Verdict v) {
    const SceneMark sm = scene_mark(cid, h.scene, v);
    bool first = false;
    {
        std::lock_guard<std::mutex> lk(g_markMx);
        if (sm == SceneMark::Mark) mark_locked(node, cid);
        else if (sm == SceneMark::Forget) g_marks.erase(node);
        if (cid && (g_logBits.load() & 1)) {
            if (g_sceneMeetSeen.size() > 4096) g_sceneMeetSeen.clear();
            first = g_sceneMeetSeen.insert((static_cast<uint64_t>(cid) << 32) ^ (reinterpret_cast<uintptr_t>(node) >> 3)).second;
        }
    }
    if (!first) return;
    char path[480] = "?";
    ++t_inGate;
    wo137::path_of(node, path, sizeof path);
    --t_inGate;
    logf("WO200-GATE closure %08X meets class %s (SceneStarter) %s: scene %s%s%s%s%s, handler state %d -> %s%s", cid, kRttiHandler, path,
         scene_name(h.scene), h.cls[0] ? " " : "", h.cls, h.name[0] ? " " : "", h.name, h.state, v == Verdict::Run ? "runs" : verdict_name(v),
         sm == SceneMark::Mark ? " -- its out-ports continue this closure for the node gate" : "");
    push_event(kEvClosureClass, cid, kind_name(Kind::SceneStarter), kRttiHandler);
}

bool gate(void* node, void* /*ctx*/) {
    if (!g_gateOn.load(std::memory_order_relaxed) || t_inGate) return false;
    c_gateCalls.fetch_add(1, std::memory_order_relaxed);
    const void* vt = nullptr;
    if (!rd(node, 0, &vt) || !vt) return false;
    const uint32_t cid = gate_closure(t_closure, t_sceneCarry);   // WO-203 I.5: a scene's out-port carries the closure that started it
    const bool carried = t_closure == 0 && cid != 0;
    const Kind k = classify(vt);
    if (cid) note_closure_class(node, vt, k, cid, carried);
    if (k == Kind::None) return false;
    c_gateClassified.fetch_add(1, std::memory_order_relaxed);
    const uint8_t role = g_role.load();
    const uint8_t gb = g_gateBits.load();
    Cfg c;
    c.joined = g_joined.load();
    c.puppets = (gb & 1) != 0;
    c.hostClosure = (gb & 2) != 0;
    c.route = (gb & 4) != 0;
    c.storyBarks = (gb & 8) != 0;   // WO-201 B7
    const bool inClosure = in_replay_closure(cid);
    const bool mirror = t_mirrorTalker;
    Method m = Method::Other;
    char mname[128] = "";
    if (k == Kind::Function) {
        if (!wo137::function_method(node, mname, sizeof mname)) return false;
        m = method_kind(mname);
        if (m == Method::Other) return false;
    }
    Target t = Target::Unknown;
    if (needs_target(role, k, m, inClosure, mirror, c)) t = read_target(node);
    bool urge = false, money = false;
    if ((role == kRoleHost && inClosure && k == Kind::Behaviour) || (k == Kind::Function && m == Method::Items && (inClosure || mirror))) {
        char pn[128];
        ++t_inGate;
        const bool ok = parent_name(node, pn, sizeof pn);
        --t_inGate;
        if (ok) { urge = is_urge_parent(pn); money = is_money_parent(pn); }
    }
    // WO-203 I.5: a scene handler -- may its Execute start a scene, and (the host inside a replay closure) is that scene the player's?
    HandlerRead hr;
    if (k == Kind::SceneStarter) read_handler(node, role == kRoleHost && inClosure && c.hostClosure, &hr);
    Verdict v = decide(role, k, m, t, inClosure, mirror, urge, money, c, hr.scene);
    // WO-201 B7: the joiner's own quest logic starts no story bark (his own conversation's chain and interactions stay his; WO-203 I.5: a
    // chain carried across a scene his conversation started is his own too)
    if (v == Verdict::Run && role == kRoleJoiner && c.joined && c.storyBarks && story_starter(k, m)) {
        char pn[128] = "";
        ++t_inGate;
        const bool ok = parent_name(node, pn, sizeof pn);
        --t_inGate;
        if (joiner_story_bark(role, k, m, ok && is_urge_parent(pn), own_chain(cid), ok && is_interaction_parent(pn), c)) v = Verdict::JoinerStoryBark;
    }
    // (a scene handler's own state already says whether this Execute starts anything: a busy one ran above, an idle one starts a scene
    // whatever its effect byte says)
    if (v != Verdict::Run && k != Kind::SceneStarter) {
        const int row = class_row(vt);
        if (row >= 0 && g_isEffect[row] && g_offEffectActive) {
            uint8_t active = 0;
            if (rd(node, g_offEffectActive, &active) && active) v = Verdict::Run;   // a switch-off (or a re-read): the gate only keeps things from starting
        }
    }
    if (k == Kind::SceneStarter) scene_note(node, cid, hr, v);
    if (v == Verdict::Run) return false;
    c_verdict[static_cast<int>(v)].fetch_add(1, std::memory_order_relaxed);
    char path[480] = "?";
    ++t_inGate;
    wo137::path_of(node, path, sizeof path);
    --t_inGate;
    char a[200];
    _snprintf_s(a, sizeof a, _TRUNCATE, "%s %s %s target=%s", verdict_name(v), kind_name(k), mname[0] ? last_segment(mname) : "-",
                t == Target::Player ? "player" : t == Target::Other ? "other" : "unknown");
    bool first = false;
    {
        std::lock_guard<std::mutex> lk(g_markMx);
        if (always_logged(v, k)) {
            // WO-203 I.5: a conversation or a scene kept from starting is always logged, at most once per node per 10 s (the cap below hid
            // session C's five joiner refusals: it was full 6 minutes before them)
            const uint64_t now = now_ms();
            if (g_storyLogLast.size() > 2048)
                for (auto it = g_storyLogLast.begin(); it != g_storyLogLast.end();)
                    it = now - it->second >= kStoryLogEveryMs ? g_storyLogLast.erase(it) : std::next(it);
            if (g_storyLogLast.size() > 4096) g_storyLogLast.clear();
            auto it = g_storyLogLast.find(path);
            if (log_due(it == g_storyLogLast.end() ? 0 : it->second, now, kStoryLogEveryMs)) { g_storyLogLast[path] = now; first = true; }
        } else if (g_refusedSeen.size() < 4096) {
            first = g_refusedSeen.insert(std::string(verdict_name(v)) + "|" + path).second;
        }
    }
    if (first) {
        logf("WO200-GATE %s: %s not run (%s)%s", role == kRoleHost ? "host" : "joiner", path, a,
             carried ? " in a closure its scene carried" : cid ? " in a closure" : mirror ? " in a mirror of the host's conversation" : "");
        push_event(kEvRefused, cid, a, path);
    }
    return true;
}

// ---------------------------------------------------------------------------
// The replay (host, main thread).
// ---------------------------------------------------------------------------
struct ReplayCtx { uint32_t closure; const char* port; uint8_t result; uint8_t runtime; };
bool replay_on(void* node, void* p) {
    auto* r = static_cast<ReplayCtx*>(p);
    const Kind k = classify_node(node);
    bool waiter = false;
    if (k == Kind::Waiter) {   // a continuation: only the waiter this replay enqueued here (the joiner's scene ended)
        uint32_t mid = 0;
        if (!marked_closure(node, &mid) || mid != r->closure) { r->result = 2; return true; }
        waiter = true;
    } else if (k != Kind::Dialogue && k != Kind::DialogueStarter) { r->result = 2; return true; }
    int rts[16]{};
    const int n = wo137::runtime_chain(node, rts, 16);
    r->runtime = static_cast<uint8_t>(n > 0 ? rts[0] & 0xFF : 0xFF);
    for (int i = 0; i < n; ++i)
        if (rts[i] != 0) { r->result = 4; r->runtime = static_cast<uint8_t>(rts[i] & 0xFF); return true; }
    if (n <= 0) { r->result = 4; return true; }
    void* port = wo137::find_port_ref(node, r->port);
    if (!port) { r->result = 3; return true; }
    const void* pvt = nullptr;
    if (!rd(port, 0, &pvt) || !is_trigger_port_vft(pvt)) { wo137::release_ref(port); r->result = 3; return true; }
    const uint32_t prev = t_closure;
    t_closure = r->closure;
    KCDMP_FAULT_CALL(site, "wo200::replay");
    const bool ok = fault::guarded(site, [&] { g_origOut(port); });
    t_closure = prev;
    wo137::release_ref(port);
    if (ok && waiter) { std::lock_guard<std::mutex> lk(g_markMx); g_waiterDropOnce.insert(node); }
    r->result = ok ? 1 : 5;
    return true;
}

void put_u32(uint8_t* p, uint32_t v) { std::memcpy(p, &v, 4); }
uint16_t get_u16(const uint8_t* p) { uint16_t v; std::memcpy(&v, p, 2); return v; }

// ReadNode: a clock's state byte or a State's int.
struct ReadCtx { uint8_t kind; uint8_t runtime; int32_t value; char cls[96]; };
bool read_on(void* node, void* p) {
    auto* r = static_cast<ReadCtx*>(p);
    rtti_name(node, r->cls, sizeof r->cls);
    const Kind k = classify_node(node);
    r->kind = static_cast<uint8_t>(k);
    int rts[2]{};
    r->runtime = static_cast<uint8_t>(wo137::runtime_chain(node, rts, 1) > 0 ? rts[0] & 0xFF : 0xFF);
    if (k == Kind::Clock) {
        uint8_t b = 0xFF;
        if (rd(node, kOffClockState, &b)) r->value = b;
    } else {
        int v = 0;
        if (wo137::state_int(node, &v)) r->value = v;
    }
    return true;
}

// Research (V.1 probe O4, the harness only): a clock's state byte written directly (no port, no pulse) -- the receive-only joiner's way of
// taking the host's clock values. Clock nodes only; the byte the setter itself reads (kOffClockState).
struct ClockWriteCtx { uint8_t value; bool clock = false, ok = false; int before = -1; };
bool clock_write_on(void* node, void* p) {
    auto* c = static_cast<ClockWriteCtx*>(p);
    if (classify_node(node) != Kind::Clock) return true;
    c->clock = true;
    uint8_t b = 0xFF;
    if (rd(node, kOffClockState, &b)) c->before = b;
    KCDMP_FAULT_CALL(site, "wo200::research_clock_write");
    c->ok = fault::guarded(site, [&] { *reinterpret_cast<volatile uint8_t*>(static_cast<char*>(node) + kOffClockState) = c->value; });
    return true;
}

bool dump_on(void* node, void*) {
    char cls[160] = "?";
    rtti_name(node, cls, sizeof cls);
    logf("WO200-RESEARCH node class %s kind %s target=%d", cls, kind_name(classify_node(node)), static_cast<int>(read_target(node, true)));
    return true;
}
bool arm_drop_on(void* node, void*) { g_researchDrop.store(node); return true; }   // compared only, never dereferenced
// Research (V.1 probe O1, the harness only): the engine's own C_Node::Hibernate / Wake on ONE node -- an effect, a timer or a time-of-day
// watch (the receive-only joiner's decision classes); anything else is left as it is and named. The node's runtime state before and after.
using HibFn = void (*)(void* node, int reason);
struct HibCtx { bool wake; int reason; int before = -1, after = -1; bool allowed = false, ran = false; char cls[160] = "?"; };
bool hib_on(void* node, void* p) {
    auto* h = static_cast<HibCtx*>(p);
    rtti_name(node, h->cls, sizeof h->cls);
    const void* vt = nullptr;
    if (!rd(node, 0, &vt) || !vt) return true;
    h->allowed = classify(vt) == Kind::Clock || derives_from(vt, ".?AVC_Effect@conceptmodule@wh@@");
    if (!h->allowed) return true;
    int rts[1]{};
    if (wo137::runtime_chain(node, rts, 1) > 0) h->before = rts[0];
    void* fn = h->wake ? A.nodeWake : A.nodeHibernate;
    if (!fn) return true;
    KCDMP_FAULT_CALL(site, "wo200::research_hibernate");
    h->ran = fault::guarded(site, [&] { reinterpret_cast<HibFn>(fn)(node, h->reason); });
    if (wo137::runtime_chain(node, rts, 1) > 0) h->after = rts[0];
    return true;
}
// Research (the harness): one pulse of the node's out-port through its own vftable -- the call the dialogue system makes
// (DialogModule's trigger: GetPort(name), then slot 15), so the tap sees it exactly as it sees a conversation's consequence.
struct FireCtx { const char* port; bool ok; };
bool fire_on(void* node, void* p) {
    auto* f = static_cast<FireCtx*>(p);
    void* port = wo137::find_port_ref(node, f->port);
    if (!port) return true;
    const void* pvt = nullptr; void* fn = nullptr;
    if (rd(port, 0, &pvt) && is_trigger_port_vft(pvt) && rd(pvt, kSlotPortTrigger * 8, &fn) && fn) {
        KCDMP_FAULT_CALL(site, "wo200::research_fire");
        f->ok = fault::guarded(site, [&] { reinterpret_cast<TriggerFn>(fn)(port); });
    }
    wo137::release_ref(port);
    return true;
}

} // namespace

// ===========================================================================
uint32_t closure_now() { return t_closure; }
void source_now(const void** node, const void** port) { *node = t_srcNode; *port = t_srcPort; }
void describe_source(const void* node, const void* port, char* cls, size_t ncls, char* path, size_t npath, char* pname, size_t npname) {
    if (ncls) cls[0] = 0;
    if (npath) path[0] = 0;
    if (npname) pname[0] = 0;
    if (!port) return;
    void* owner = node ? const_cast<void*>(node) : port_owner(port);   // the tap-off path keeps only the port: its owner is asked here
    if (owner) {
        rtti_name(owner, cls, ncls);
        wo137::path_of(owner, path, npath);
    }
    port_name(port, pname, npname);
    if (owner && !node) call_release(owner);
}
bool copy_neutral_always() { return g_role.load() == kRoleJoiner && g_joined.load() && (g_valueBits.load() & 1) != 0; }
void set_mirror_talker(bool on) { t_mirrorTalker = on; }

void install() {
    A.cm = GetModuleHandleA("ConceptModule.dll");
    A.cs = GetModuleHandleA("CrySystem.dll");
    if (!A.cm || !A.cs) { logf("WO200-BUILD ConceptModule/CrySystem missing -- the gates are NOT armed"); return; }
    const auto ce = module_exports(A.cm);
    A.execute = find_export(ce, "?Execute@C_Node@conceptmodule@wh@@");
    A.nodeHibernate = find_export(ce, "?Hibernate@C_Node@conceptmodule@wh@@");   // research only (op 75 sub 4/5)
    A.nodeWake = find_export(ce, "?Wake@C_Node@conceptmodule@wh@@");
    A.portGetNode = find_export(ce, "?GetNode@I_Port@conceptmodule@wh@@");
    A.portGetType = find_export(ce, "?GetType@I_Port@conceptmodule@wh@@");
    A.portDirection = find_export(ce, "?GetDirection@I_Port@conceptmodule@wh@@");
    A.release = find_export(ce, "?Release@C_SharedResource@conceptmodule@wh@@");
    A.portRead = find_export(ce, "?Read@I_Port@conceptmodule@wh@@");
    void* getPort = find_export(ce, "?GetPort@C_Node@conceptmodule@wh@@");
    A.outTrigVft = anchor::find_vftable(A.cm, ".?AVC_OutputTriggerPort@conceptmodule@wh@@", 0);
    A.iPortVft = anchor::find_vftable(A.cm, ".?AVI_Port@conceptmodule@wh@@", 0);
    void* const* activeVft = anchor::find_vftable(A.cm, ".?AVC_ActiveTriggerPort@conceptmodule@wh@@", 0);
    void* const* ifaceVft = anchor::find_vftable(A.cm, ".?AVC_InterfaceTriggerPort@conceptmodule@wh@@", 0);
    void* const* staticVft = anchor::find_vftable(A.cm, ".?AVC_StaticTriggerPort@conceptmodule@wh@@", 0);
    const auto se = module_exports(A.cs);
    A.vDtor = find_export(se, "??1variant@rttr@@QEAA@XZ");
    A.vValid = find_export(se, "?is_valid@variant@rttr@@");
    A.vRawPtr = find_export(se, "?get_raw_ptr@variant@rttr@@");
    A.vIsSeq = find_export(se, "?is_sequential_container@variant@rttr@@");
    A.vSeqView = find_export(se, "?create_sequential_view@variant@rttr@@");
    A.seqSize = find_export(se, "?get_size@variant_sequential_view@rttr@@");
    A.seqValue = find_export(se, "?get_value@variant_sequential_view@rttr@@");
    A.seqDtor = find_export(se, "??1variant_sequential_view@rttr@@QEAA@XZ");
    A.tGetName = find_export(se, "?get_name@type@rttr@@");

    // The class table, by RTTI name in each module.
    for (int i = 0; i < kNClasses; ++i) {
        HMODULE m = GetModuleHandleA(kClasses[i].module);
        g_vft[i] = m ? anchor::find_vftable(m, kClasses[i].rtti, 0) : nullptr;
        if (g_vft[i]) { ++g_found; g_isEffect[i] = derives_from(g_vft[i], ".?AVC_Effect@conceptmodule@wh@@"); }
        else logf("WO200-BUILD class %s (%s) not found -- that row of the gate stays off", kClasses[i].rtti, kClasses[i].module);
    }
    // WO-203 I.5: what a scene handler starts, by RTTI in GUIModule -- its configuration, and the two scene classes that are the player's
    if (HMODULE gui = GetModuleHandleA("GUIModule.dll")) {
        g_vftSceneCfg = anchor::find_vftable(gui, kRttiSceneCfg, 0);
        g_vftFader = anchor::find_vftable(gui, kRttiFader, 0);
        g_vftIngame = anchor::find_vftable(gui, kRttiIngame, 0);
    }
    logf("WO200-BUILD scenes: C_CutsceneConfiguration %s, C_FaderCutscene %s, C_IngameCutscene %s -- %s", g_vftSceneCfg ? "found" : "NOT found",
         g_vftFader ? "found" : "NOT found", g_vftIngame ? "found" : "NOT found",
         g_vftSceneCfg && (g_vftFader || g_vftIngame) ? "a scene handler's scene is read inside a host replay closure"
                                                      : "a scene handler's scene is not read: inside a host replay closure every start is refused (fail closed)");
    if (void* effExec = find_export(ce, "?ExecuteNode@C_Effect@conceptmodule@wh@@")) {
        uint8_t code[256]{};
        KCDMP_FAULT_READ(esite, "wo200::effect_code");
        if (fault::guarded(esite, [&] { std::memcpy(code, effExec, sizeof code); })) {
            for (int i = 0; i + 6 <= static_cast<int>(sizeof code) && !g_offEffectActive; ++i) {
                if (code[i] != 0x3A || code[i + 1] != 0x86) continue;   // cmp al, byte ptr [rsi + disp32]
                uint32_t d; std::memcpy(&d, code + i + 2, 4);
                for (int j = i + 6; j + 6 <= static_cast<int>(sizeof code); ++j) {
                    uint32_t d2; std::memcpy(&d2, code + j + 2, 4);
                    if (code[j] == 0x88 && code[j + 1] == 0x9E && d2 == d && d < 0x1000) { g_offEffectActive = d; break; }   // mov byte [rsi+d32], bl
                }
            }
        }
    }
    {
        int effects = 0;
        for (int i = 0; i < kNClasses; ++i) effects += g_isEffect[i];
        logf("WO200-BUILD effects: %d of the gate's classes derive from C_Effect; its active byte +0x%zX (%s) -- an active effect's Execute (a switch-off) is never refused",
             effects, g_offEffectActive, g_offEffectActive ? "read from C_Effect::ExecuteNode" : "NOT found: effects are gated as any node");
    }

    // C_Node's port vector, out of GetPort's own code: mov rsi,[rbx+E] then mov rbx,[rbx+B] (begin/end of the ports it compares by name).
    if (getPort) {
        const uint8_t* p = static_cast<const uint8_t*>(getPort);
        uint8_t code[96]{};
        KCDMP_FAULT_READ(site, "wo200::getport_code");
        if (fault::guarded(site, [&] { std::memcpy(code, p, sizeof code); })) {
            int endOff = -1, beginOff = -1;
            for (int i = 0; i + 4 <= static_cast<int>(sizeof code); ++i) {
                if (code[i] == 0x48 && code[i + 1] == 0x8B && code[i + 2] == 0x73 && endOff < 0) endOff = code[i + 3];
                if (code[i] == 0x48 && code[i + 1] == 0x8B && code[i + 2] == 0x5B && endOff >= 0 && beginOff < 0) beginOff = code[i + 3];
            }
            if (beginOff >= 0 && endOff == beginOff + 8) { A.portsBegin = static_cast<size_t>(beginOff); A.portsEnd = static_cast<size_t>(endOff); }
        }
    }
    void* readSlot = nullptr;
    const bool readOk = A.iPortVft && A.portRead && rd(A.iPortVft, kSlotPortRead * 8, &readSlot) && readSlot == A.portRead;
    A.ports = A.portsEnd != 0 && readOk && A.portGetType && A.portDirection && A.vDtor && A.vValid && A.vRawPtr && A.vIsSeq && A.vSeqView &&
              A.seqSize && A.seqValue && A.seqDtor && A.tGetName;

    // Gate 1: C_OutputTriggerPort's slot 15 must be C_ActiveTriggerPort's own Trigger (the function that names itself so).
    {
        void* s15 = nullptr; void* a15 = nullptr;
        anchor::Range text{};
        anchor::section(A.cm, ".text", &text);
        const char* label = anchor::find_cstring(A.cm, "C_ActiveTriggerPort::Trigger");
        const bool shape = A.outTrigVft && activeVft && rd(A.outTrigVft, kSlotPortTrigger * 8, &s15) && rd(activeVft, kSlotPortTrigger * 8, &a15) &&
                           s15 && s15 == a15 && text.contains(s15) && label && anchor::function_refs(A.cm, s15, label);
        if (shape && A.portGetNode && A.release) {
            g_origOut = reinterpret_cast<TriggerFn>(s15);
            // every trigger-port class whose slot 15 is that same function: the exact C_ActiveTriggerPort, its output ports, and the
            // interface ports (a module's boundary, a dialogue node's own ports: direction 3) with their static form
            void* const* cands[4] = {activeVft, A.outTrigVft, ifaceVft, staticVft};
            for (void* const* vt : cands) {
                void* cur = nullptr;
                if (!vt || !rd(vt, kSlotPortTrigger * 8, &cur) || cur != s15) continue;
                void** at = const_cast<void**>(&vt[kSlotPortTrigger]);
                DWORD oldProt = 0, ignored = 0;
                if (!VirtualProtect(at, sizeof(void*), PAGE_READWRITE, &oldProt)) continue;
                void* prev = InterlockedCompareExchangePointer(at, reinterpret_cast<void*>(&hooked_out_trigger), s15);
                VirtualProtect(at, sizeof(void*), oldProt, &ignored);
                if (prev == s15) A.trigVfts[A.nTrigVfts++] = vt;
            }
            A.tap = A.nTrigVfts > 0;
        }
        char d[96]{};
        anchor::describe(s15, d, sizeof d);
        logf("WO200-BUILD tap %s (slot 15 = %s of %d trigger-port classes -- Active/Output/Interface/Static; shared with C_ActiveTriggerPort: %s, "
             "names itself: %s)", A.tap ? "ARMED (off until a Config)" : "NOT armed", d, A.nTrigVfts, (s15 && s15 == a15) ? "yes" : "no",
             (label && s15 && anchor::function_refs(A.cm, s15, label)) ? "yes" : "no");
    }
    // Gate 2: C_Node::Execute's entry (the prologue in the registry; inline_hook refuses any other bytes).
    if (A.execute && g_found > 0) {
        const char* why = nullptr;
        A.gate = inlinehook::install_gate(A.execute, hookpro::kNodeExecute, sizeof hookpro::kNodeExecute, &gate, &why);
        char d[96]{};
        anchor::describe(A.execute, d, sizeof d);
        logf("WO200-BUILD gate %s (C_Node::Execute=%s, the export every node class calls; %d of %d classes found; soul reads %s, ports +0x%zX..+0x%zX)%s%s",
             A.gate ? "ARMED (off until a Config)" : "NOT armed", d, g_found, kNClasses, A.ports ? "armed" : "OFF", A.portsBegin, A.portsEnd,
             A.gate ? "" : " -- ", A.gate ? "" : (why ? why : "?"));
    } else {
        logf("WO200-BUILD gate NOT armed (C_Node::Execute %s, %d classes found)", A.execute ? "found" : "not exported", g_found);
    }
}

void tick() {
    static uint64_t last = 0;
    const uint64_t t = now_ms();
    if (t - last < 1000) return;
    last = t;
    void* soul = (g_tapOn.load() || g_gateOn.load()) ? rttr::read_player_soul() : nullptr;
    g_playerFull = soul ? full_object(soul) : nullptr;
    std::lock_guard<std::mutex> lk(g_markMx);
    for (auto it = g_marks.begin(); it != g_marks.end();) it = t > it->second.until ? g_marks.erase(it) : std::next(it);
}

int status_text(char* out, int n) {
    return _snprintf_s(out, static_cast<size_t>(n), _TRUNCATE,
        "wo200 tap=%d gate=%d ports=%d classes=%d/%d role=%u joined=%d tapBits=%u gateBits=%u taps=%u captured=%u quiet=%u continued=%u "
        "scene_continues=%u gates=%u classified=%u reads=%u puppet=%u host_dialogue=%u host_view=%u talker_host=%u talker_joiner=%u joiner_story=%u "
        "host_scene=%u replays=%u replay_refused=%u marks=%zu player=%d",
        A.tap, A.gate, A.ports, g_found, kNClasses, g_role.load(), g_joined.load(), g_tapBits.load(), g_gateBits.load(), c_tapCalls.load(),
        c_captured.load(), c_quiet.load(), c_continued.load(), c_sceneContinued.load(), c_gateCalls.load(), c_gateClassified.load(), c_targetReads.load(),
        c_verdict[static_cast<int>(Verdict::PuppetRefused)].load(), c_verdict[static_cast<int>(Verdict::HostDialogue)].load(),
        c_verdict[static_cast<int>(Verdict::HostView)].load(), c_verdict[static_cast<int>(Verdict::TalkerOnlyHost)].load(),
        c_verdict[static_cast<int>(Verdict::TalkerOnlyJoiner)].load(), c_verdict[static_cast<int>(Verdict::JoinerStoryBark)].load(),
        c_verdict[static_cast<int>(Verdict::HostScene)].load(), c_replays.load(), c_replayRefused.load(), g_marks.size(), g_playerFull.load() != nullptr);
}

uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    switch (op) {
        case kOpStatus: {
            char t[800];
            const int n = status_text(t, sizeof t);
            const size_t m = n > 0 ? (static_cast<size_t>(n) < cap ? static_cast<size_t>(n) : cap) : 0;
            std::memcpy(out, t, m);
            *outLen = m;
            return 0;
        }
        case kOpConfig: {
            if (len < 5 || cap < 5) return 1;
            const uint8_t prevRole = g_role.load();
            if (body[0] != 255) g_role = body[0];
            if (body[1] != 255) g_joined = body[1] != 0;
            if (body[2] != 255) g_tapBits = body[2];
            if (body[3] != 255) g_gateBits = body[3];
            if (body[4] != 255) g_logBits = body[4];
            if (len >= 6 && body[5] != 255) g_valueBits = body[5];   // WO-200 Act II: 1 copy_neutral_always
            recompute();
            static int lastKey = -1;
            const int key = (g_role.load() << 24) | (g_joined.load() << 20) | (g_tapBits.load() << 12) | (g_gateBits.load() << 4) | g_logBits.load()
                            | (g_valueBits.load() << 28);
            if (key != lastKey) {
                lastKey = key;
                logf("WO200-CONFIG role=%u joined=%d tap=%u gate=%u log=%u values=%u -> tap %s, gate %s, copies neutral %s", g_role.load(), g_joined.load(),
                     g_tapBits.load(), g_gateBits.load(), g_logBits.load(), g_valueBits.load(), g_tapOn.load() ? "on" : "off", g_gateOn.load() ? "on" : "off",
                     copy_neutral_always() ? "on" : "off");
            }
            if (prevRole != g_role.load()) {   // a new session: nothing of the last one's closures carries over
                std::lock_guard<std::mutex> lk(g_markMx);
                g_marks.clear(); g_quietLast.clear(); g_waiterDropOnce.clear(); g_sceneMeetSeen.clear();
            }
            out[0] = g_role.load(); out[1] = g_joined.load(); out[2] = g_tapBits.load(); out[3] = g_gateBits.load(); out[4] = g_logBits.load();
            *outLen = 5;
            return 0;
        }
        case kOpPoll: {
            if (len < 4 || cap < 10) return 1;
            uint32_t since; std::memcpy(&since, body, 4);
            size_t o = 4 + 1 + 1;
            uint8_t n = 0, more = 0;
            uint32_t last = since;
            std::lock_guard<std::mutex> lk(g_evMx);
            const uint64_t t = now_ms();
            for (const Ev& e : g_ev) {
                if (e.seq <= since) continue;
                const size_t al = e.a.size() < 120 ? e.a.size() : 120, bl = e.b.size() < 400 ? e.b.size() : 400;
                if (o + 4 + 1 + 4 + 4 + 1 + al + 2 + bl > cap || n == 255) { more = 1; break; }
                put_u32(out + o, e.seq); o += 4;
                out[o++] = e.kind;
                put_u32(out + o, e.closure); o += 4;
                put_u32(out + o, static_cast<uint32_t>(t - e.atMs)); o += 4;
                out[o++] = static_cast<uint8_t>(al); std::memcpy(out + o, e.a.data(), al); o += al;
                const uint16_t b16 = static_cast<uint16_t>(bl); std::memcpy(out + o, &b16, 2); o += 2; std::memcpy(out + o, e.b.data(), bl); o += bl;
                last = e.seq; ++n;
            }
            put_u32(out, last); out[4] = more; out[5] = n;
            *outLen = o;
            return 0;
        }
        case kOpReplay: {
            if (len < 4 + 2 || cap < 2) return 1;
            uint32_t cid; std::memcpy(&cid, body, 4);
            const uint16_t pl = get_u16(body + 4);
            if (pl == 0 || pl > 400 || len < 6u + pl + 1u) return 1;
            std::string path(reinterpret_cast<const char*>(body + 6), pl);
            const uint8_t portLen = body[6 + pl];
            if (portLen == 0 || portLen > 100 || len != 7u + pl + portLen) return 1;
            std::string port(reinterpret_cast<const char*>(body + 7 + pl), portLen);
            ReplayCtx r{cid & ~kLocalClosureBit, port.c_str(), 0, 0xFF};
            if (!A.tap || !g_origOut) r.result = 6;
            else if (!wo137::visit_node(path.c_str(), &replay_on, &r)) r.result = 0;
            (r.result == 1 ? c_replays : c_replayRefused).fetch_add(1);
            static const char* kRes[] = {"no such node", "ran", "not a dialogue node", "no such out-port", "hibernated or not loaded", "fault", "not armed"};
            logf("WO200-REPLAY closure %08X %s :: %s -> %s (runtime %u)", r.closure, path.c_str(), port.c_str(), kRes[r.result < 7 ? r.result : 0], r.runtime);
            char a[16]; _snprintf_s(a, sizeof a, _TRUNCATE, "%u", r.result);
            push_event(kEvReplay, r.closure, a, (path + " :: " + port).c_str());
            out[0] = r.result; out[1] = r.runtime;
            *outLen = 2;
            return 0;
        }
        case kOpReadNode: {
            if (len < 2 || cap < 8 + 96) return 1;
            const uint16_t pl = get_u16(body);
            if (pl == 0 || pl > 400 || len != 2u + pl) return 1;
            std::string path(reinterpret_cast<const char*>(body + 2), pl);
            ReadCtx r{0, 0xFF, -1, {}};
            const bool found = wo137::visit_node(path.c_str(), &read_on, &r);
            out[0] = found; out[1] = r.kind; out[2] = r.runtime;
            std::memcpy(out + 3, &r.value, 4);
            const size_t cl = std::strlen(r.cls);
            out[7] = static_cast<uint8_t>(cl); std::memcpy(out + 8, r.cls, cl);
            *outLen = 8 + cl;
            return 0;
        }
        case kOpResearch: {
            if (len < 3 || cap < 1) return 1;
            const uint16_t pl = get_u16(body + 1);
            if (pl == 0 || pl > 400 || len < 3u + pl) return 1;
            std::string path(reinterpret_cast<const char*>(body + 3), pl);
            bool ok = false;
            if (body[0] == 3) {   // fire <path> <port> through the tap
                if (len < 4u + pl) return 1;
                const uint8_t portLen = body[3 + pl];
                if (portLen == 0 || portLen > 100 || len != 4u + pl + portLen) return 1;
                std::string port(reinterpret_cast<const char*>(body + 4 + pl), portLen);
                FireCtx f{port.c_str(), false};
                ok = A.tap && wo137::visit_node(path.c_str(), &fire_on, &f) && f.ok;
                logf("WO200-RESEARCH fire %s :: %s -> %s", path.c_str(), port.c_str(), ok ? "pulsed through the tap" : "not pulsed");
                out[0] = ok;
                *outLen = 1;
                return 0;
            }
            if (body[0] == 6) {   // V.1 O4: a clock's state written directly: [6][pathLen:2][path][value:1]
                if (len != 4u + pl) return 1;
                ClockWriteCtx c{body[3 + pl]};
                const bool found = wo137::visit_node(path.c_str(), &clock_write_on, &c);
                logf("WO200-RESEARCH clockwrite %s clock=%d %d -> %u ok=%d", path.c_str(), c.clock ? 1 : 0, c.before, c.value, c.ok ? 1 : 0);
                out[0] = found && c.clock && c.ok;
                *outLen = 1;
                return 0;
            }
            if (len != 3u + pl) return 1;
            if (body[0] == 4 || body[0] == 5) {   // V.1 O1: hibernate (4, reason RandomEventEnd) / wake (5, reason RandomEventStart) one node
                HibCtx h{body[0] == 5, 0};
                h.reason = body[0] == 5 ? 0 : 2;
                const bool found = wo137::visit_node(path.c_str(), &hib_on, &h);
                logf("WO200-RESEARCH %s %s class=%s allowed=%d ran=%d runtime %d -> %d", h.wake ? "wake" : "hibernate", path.c_str(), h.cls,
                     h.allowed ? 1 : 0, h.ran ? 1 : 0, h.before, h.after);
                if (cap < 4) return 1;
                out[0] = found; out[1] = h.allowed; out[2] = h.ran; out[3] = static_cast<uint8_t>(h.after & 0xFF);
                *outLen = 4;
                return 0;
            }
            if (body[0] == 1) ok = wo137::visit_node(path.c_str(), &arm_drop_on, nullptr);
            else if (body[0] == 2) ok = wo137::visit_node(path.c_str(), &dump_on, nullptr);
            logf("WO200-RESEARCH sub %u %s -> %s", body[0], path.c_str(), ok ? "ok" : "no such node");
            out[0] = ok;
            *outLen = 1;
            return 0;
        }
        default:
            return 1;
    }
}

} // namespace kcdmp::wo200
