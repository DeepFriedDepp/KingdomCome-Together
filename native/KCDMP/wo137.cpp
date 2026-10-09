// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-137 -- see wo137.h.
#include "wo137.h"
#include "fault_guard.h"
#include "wo137_rules.h"

#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <vector>

#include "anchors.h"
#include "log.h"
#include "main_thread.h"
#include "pe_exports.h"
#include "pipe_server.h"
#include "port_gate.h"
#include "respawn.h"

namespace kcdmp::wo137 {

void (*g_send)(const uint8_t* body, uint16_t len) = nullptr;
void set_send_callback(void (*fn)(const uint8_t*, uint16_t)) { g_send = fn; }

namespace {

// ---------------------------------------------------------------------------
// SEH-guarded primitives. Each __try lives in its own function with no
// unwindable object (MSVC's rule, and this DLL's habit).
// ---------------------------------------------------------------------------
template <class T> bool rd(const void* base, size_t off, T* out) {
    KCDMP_FAULT_READ(site, "wo137::set_send_callback");
    return fault::guarded(site, [&] { *out = *reinterpret_cast<const T*>(static_cast<const char*>(base) + off); });
}
bool copy_cstr(const char* s, char* out, size_t n) {
    if (!s || n == 0) return false;
    KCDMP_FAULT_READ(site, "wo137::copy_cstr");
    if (fault::guarded(site, [&] {
        size_t i = 0;
        for (; i + 1 < n && s[i]; ++i) out[i] = s[i];
        out[i] = 0;
    })) return true;
    out[0] = 0;
    return false;
}
bool copy_bytes(const void* s, void* out, size_t n) {
    KCDMP_FAULT_READ(site, "wo137::copy_bytes");
    return fault::guarded(site, [&] { std::memcpy(out, s, n); });
}
// The class name behind an object's vptr (its RTTI locator; logs only).
bool rtti_name(const void* obj, char* out, size_t n) {
    out[0] = 0;
    const void* vptr = nullptr;
    if (!obj || !rd(obj, 0, &vptr) || !vptr) return false;
    const void* col = nullptr;
    if (!rd(vptr, static_cast<size_t>(-8), &col) || !col) return false;
    uint32_t sig = 0, tdRva = 0, selfRva = 0;
    if (!rd(col, 0, &sig) || sig != 1 || !rd(col, 12, &tdRva) || !rd(col, 20, &selfRva)) return false;
    const char* base = static_cast<const char*>(col) - selfRva;
    return copy_cstr(base + tdRva + 0x10, out, n);
}

// The COL's offset: how far this vptr's subobject sits from the full object.
bool rtti_offset(const void* obj, uint32_t* off) {
    const void* vptr = nullptr; const void* col = nullptr;
    if (!obj || !rd(obj, 0, &vptr) || !vptr || !rd(vptr, static_cast<size_t>(-8), &col) || !col) return false;
    uint32_t sig = 0;
    return rd(col, 0, &sig) && sig == 1 && rd(col, 4, off);
}

// ---------------------------------------------------------------------------
// Anchors. Everything by export or RTTI; the two patched slots are verified
// structurally before anything is written.
// ---------------------------------------------------------------------------
struct Anchors {
    HMODULE cm = nullptr, qm = nullptr, gm = nullptr, cs = nullptr;
    void* getGameIface = nullptr;
    // ConceptModule (exports)
    void* findNode = nullptr;
    void* getPort = nullptr;
    void* nodeName = nullptr;
    void* nodeParent = nullptr;
    void* release = nullptr;
    void* portDirection = nullptr;
    void* runtimeState = nullptr;
    void* resolver = nullptr;          // C_ConceptModule::GetResourceResolver
    void* const* stateVft = nullptr;   // C_StateVariable::vftable
    void* const* inTrigVft = nullptr;  // C_InputTriggerPort::vftable
    void* const* autoTrigVft = nullptr;// C_AutoTriggerPort::vftable
    void* inTrigTrigger = nullptr;     // their shared slot 15
    // QuestModule
    void* const* questVft = nullptr;
    void* questModuleVft = nullptr;    // exported ??_7C_QuestModule
    void* qObjectives = nullptr, *qLevel = nullptr, *qLast = nullptr;
    void* oName = nullptr, *oType = nullptr, *oLog = nullptr, *oOrder = nullptr, *oLast = nullptr;
    void* setHud = nullptr, *getHud = nullptr;
    void* getModuleByName = nullptr;   // Framework: C_ModulesManager::GetModuleByName
    void* const* hudQuestVft = nullptr;   // GUIModule C_UIHudEvents, the I_UIHudEventsQuest subobject (+0x58)
    void* const* fnVft = nullptr;         // ConceptModule C_Function::vftable (quest Function nodes)
    void* variantCtor = nullptr;          // CrySystem rttr::variant::variant() (the empty variant)
    // CrySystem rttr::variant
    void* vToInt = nullptr, *vGetType = nullptr, *tGetName = nullptr, *vIsValid = nullptr;
    // What armed.
    bool detector = false, apply = false, read = false, hud = false, timeGate = false;
} A;

constexpr size_t kOffGameIfaceConceptModule = 0x128;   // WO-97, live
constexpr size_t kOffConceptModuleManager   = 0x18;    // WO-97, live
constexpr size_t kOffNodeRuntime            = 0x18;    // C_Node::GetRuntimeState reads [this+0x18]; Execute needs 1
constexpr size_t kOffStateValue             = 0x68;    // the setter's own operand (rsi+0x68, rttr::variant)
constexpr size_t kSlotExecuteNode           = 33;      // +0x108: C_Node::Execute's dispatch
constexpr size_t kSlotStateChanged          = 42;      // +0x150: the setter's notification
constexpr size_t kSlotPortName              = 9;       // I_Port::GetName (WO-97)
constexpr size_t kSlotPortTrigger           = 15;      // I_Port::Trigger (WO-97/99.5)
constexpr size_t kSlotFnInvoke              = 12;      // +0x60: C_Function's invoke (ExecuteNode = invoke, then OnExec)
constexpr size_t kOffFnMethodName           = 0x68;    // C_FunctionBase: const char* MethodName (its method getter's own read)
constexpr int    kMaxPath                   = 400;

// --- CryStringT<char> argument (concept_read.cpp's convention, WO-97 s2.1b) --
struct CryStr {
    alignas(8) char storage[12 + kMaxPath + 1]{};
    char* data = nullptr;
    bool init(const char* s) {
        const size_t n = strnlen(s, kMaxPath);
        if (n == 0 || n >= static_cast<size_t>(kMaxPath)) return false;
        const int32_t hdr[3] = { 0x40000000, static_cast<int32_t>(n), static_cast<int32_t>(n) };
        std::memcpy(storage, hdr, sizeof(hdr));
        std::memcpy(storage + 12, s, n);
        storage[12 + n] = 0;
        data = storage + 12;
        return true;
    }
    const void* ref() const { return &data; }
};

// --- engine calls ----------------------------------------------------------
using GiFn        = const void* (*)();
using FindNodeFn  = void* (*)(void* mgr, void** ret, const void* str);
using GetPortFn   = void* (*)(void* node, void** ret, const void* str);
using NameFn      = const void* (*)(const void* node);             // -> const CryStringT&
using ParentFn    = void* (*)(const void* node, void** ret);        // -> _smart_ptr<C_ModuleBase>
using ReleaseFn   = void (*)(const void* res);
using IntFn       = int (*)(const void* self);
using VoidFn      = void (*)(void* self);
using ResolverFn  = void* (*)(void* conceptModule);
using ResolveFn   = void* (*)(void* resolver, void** ret, uint32_t id);
using I64Fn       = int64_t (*)(const void* self);
using U32Fn       = uint32_t (*)(const void* self);
using PtrFn       = const void* (*)(const void* self);
using HudSetFn    = void (*)(void* qm, void* sink);
using HudGetFn    = void* (*)(void* qm);
using VToIntFn    = int (*)(const void* v, bool* ok);
using VGetTypeFn  = void* (*)(const void* v, void* retType);
using TGetNameFn  = void* (*)(const void* type, void* retSv);
using VValidFn    = bool (*)(const void* v);
using ExecFn      = void (*)(void* self, const void* ctx);
using ChangedFn   = void (*)(void* self, const void* oldV, const void* newV, bool notify);

bool call_gi(const void** out) {
    KCDMP_FAULT_CALL(site, "wo137::call_gi");
    return fault::guarded(site, [&] { *out = reinterpret_cast<GiFn>(A.getGameIface)(); });
}
bool call_find(void* mgr, const void* s, void** out) {
    KCDMP_FAULT_CALL(site, "wo137::call_find");
    if (fault::guarded(site, [&] { *out = nullptr; reinterpret_cast<FindNodeFn>(A.findNode)(mgr, out, s); })) return true;
    *out = nullptr;
    return false;
}
bool call_get_port(void* node, const void* s, void** out) {
    KCDMP_FAULT_CALL(site, "wo137::call_get_port");
    if (fault::guarded(site, [&] { *out = nullptr; reinterpret_cast<GetPortFn>(A.getPort)(node, out, s); })) return true;
    *out = nullptr;
    return false;
}
bool call_name(const void* node, const char** out) {
    KCDMP_FAULT_CALL(site, "wo137::call_name");
    if (fault::guarded(site, [&] {
        const void* ref = reinterpret_cast<NameFn>(A.nodeName)(node);
        *out = ref ? *static_cast<const char* const*>(ref) : nullptr;
    })) return *out != nullptr;
    *out = nullptr;
    return false;
}
bool call_parent(const void* node, void** out) {
    KCDMP_FAULT_CALL(site, "wo137::call_parent");
    if (fault::guarded(site, [&] { *out = nullptr; reinterpret_cast<ParentFn>(A.nodeParent)(node, out); })) return true;
    *out = nullptr;
    return false;
}
void call_release(const void* res) {
    if (!res) return;
    KCDMP_FAULT_CALL(site, "wo137::call_release");
    fault::guarded(site, [&] { reinterpret_cast<ReleaseFn>(A.release)(res); });
}
bool call_int(void* fn, const void* self, int* out) {
    KCDMP_FAULT_CALL(site, "wo137::call_int");
    return fault::guarded(site, [&] { *out = reinterpret_cast<IntFn>(fn)(self); });
}
bool call_u32(void* fn, const void* self, uint32_t* out) {
    KCDMP_FAULT_CALL(site, "wo137::call_u32");
    return fault::guarded(site, [&] { *out = reinterpret_cast<U32Fn>(fn)(self); });
}
bool call_i64(void* fn, const void* self, int64_t* out) {
    KCDMP_FAULT_CALL(site, "wo137::call_i64");
    return fault::guarded(site, [&] { *out = reinterpret_cast<I64Fn>(fn)(self); });
}
bool call_ptr(void* fn, const void* self, const void** out) {
    KCDMP_FAULT_CALL(site, "wo137::call_ptr");
    return fault::guarded(site, [&] { *out = reinterpret_cast<PtrFn>(fn)(self); });
}
bool call_void(void* fn, void* self) {
    KCDMP_FAULT_CALL(site, "wo137::call_void");
    return fault::guarded(site, [&] { reinterpret_cast<VoidFn>(fn)(self); });
}
bool call_resolver(void* cmod, void** out) {
    KCDMP_FAULT_CALL(site, "wo137::call_resolver");
    return fault::guarded(site, [&] { *out = reinterpret_cast<ResolverFn>(A.resolver)(cmod); });
}
bool call_resolve(void* resolver, uint32_t id, void** out) {
    KCDMP_FAULT_CALL(site, "wo137::call_resolve");
    if (fault::guarded(site, [&] {
        *out = nullptr;
        void* vt = *static_cast<void**>(resolver);
        reinterpret_cast<ResolveFn>(static_cast<void**>(vt)[2])(resolver, out, id);
    })) return true;
    *out = nullptr;
    return false;
}
bool call_hud_set(void* qm, void* sink) {
    KCDMP_FAULT_CALL(site, "wo137::call_hud_set");
    return fault::guarded(site, [&] { reinterpret_cast<HudSetFn>(A.setHud)(qm, sink); });
}
bool call_hud_get(void* qm, void** out) {
    KCDMP_FAULT_CALL(site, "wo137::call_hud_get");
    return fault::guarded(site, [&] { *out = reinterpret_cast<HudGetFn>(A.getHud)(qm); });
}

// --- rttr::variant decode: no allocation (to_int, get_type, type::get_name) --
struct Val { bool valid = false; bool ok = false; int32_t i = 0; char type[48]{}; };

bool variant_valid(const void* v, bool* out) {
    KCDMP_FAULT_CALL(site, "wo137::variant_valid");
    return fault::guarded(site, [&] { *out = reinterpret_cast<VValidFn>(A.vIsValid)(v); });
}
bool variant_int(const void* v, int* out, bool* ok) {
    KCDMP_FAULT_CALL(site, "wo137::variant_int");
    return fault::guarded(site, [&] { *ok = false; *out = reinterpret_cast<VToIntFn>(A.vToInt)(v, ok); });
}
bool variant_type_name(const void* v, const char** data, size_t* size) {
    KCDMP_FAULT_CALL(site, "wo137::variant_type_name");
    return fault::guarded_or<bool>(site, false, [&]() -> bool {
        void* type[2] = { nullptr, nullptr };
        reinterpret_cast<VGetTypeFn>(A.vGetType)(v, type);
        struct { const char* d; size_t n; } sv{ nullptr, 0 };
        reinterpret_cast<TGetNameFn>(A.tGetName)(type, &sv);
        *data = sv.d; *size = sv.n;
        return sv.d != nullptr;
    });
}
void decode(const void* v, Val* out) {
    *out = Val{};
    if (!v || !A.vIsValid || !A.vToInt) return;
    bool valid = false;
    if (!variant_valid(v, &valid) || !valid) return;
    out->valid = true;
    int i = 0; bool ok = false;
    if (variant_int(v, &i, &ok)) { out->i = i; out->ok = ok; }
    const char* d = nullptr; size_t n = 0;
    if (A.vGetType && A.tGetName && variant_type_name(v, &d, &n) && d) {
        const size_t m = n < sizeof(out->type) - 1 ? n : sizeof(out->type) - 1;
        copy_bytes(d, out->type, m);
        out->type[m] = 0;
    }
}

// --- nodes -----------------------------------------------------------------
void* concept_manager() {
    const void* gi = nullptr;
    if (!A.getGameIface || !call_gi(&gi) || !gi) return nullptr;
    void* cmod = nullptr; void* mgr = nullptr;
    if (!rd(gi, kOffGameIfaceConceptModule, &cmod) || !cmod) return nullptr;
    if (!rd(cmod, kOffConceptModuleManager, &mgr)) return nullptr;
    return mgr;
}
void* concept_module() {
    const void* gi = nullptr;
    if (!A.getGameIface || !call_gi(&gi) || !gi) return nullptr;
    void* cmod = nullptr;
    rd(gi, kOffGameIfaceConceptModule, &cmod);
    return cmod;
}
// FindNode(path): the node with one reference taken (release it).
void* find_node(const char* path) {
    void* mgr = concept_manager();
    if (!mgr || !A.findNode) return nullptr;
    static CryStr s;   // static: a string the engine keeps a pointer to stays valid
    if (!s.init(path)) return nullptr;
    void* node = nullptr;
    if (!call_find(mgr, s.ref(), &node)) return nullptr;
    return node;
}
void* find_port(void* node, const char* name) {
    static CryStr s;
    if (!node || !A.getPort || !s.init(name)) return nullptr;
    void* port = nullptr;
    if (!call_get_port(node, s.ref(), &port)) return nullptr;
    return port;
}
bool port_name(const void* port, char* out, size_t n) {
    out[0] = 0;
    const void* vt = nullptr; void* fn = nullptr;
    if (!port || !rd(port, 0, &vt) || !vt || !rd(vt, kSlotPortName * 8, &fn) || !fn) return false;
    const void* ref = nullptr;
    if (!call_ptr(fn, port, &ref) || !ref) return false;
    const char* s = nullptr;
    if (!rd(ref, 0, &s) || !s) return false;
    return copy_cstr(s, out, n);
}
// The node's dotted path: its own name and every parent's, up to the root
// (FindNode's form, e.g. "Barbora.trosecko.hledaniPsa.h.findVorech").
bool node_path(const void* node, char* out, size_t cap, size_t* questEnd = nullptr) {
    out[0] = 0;
    if (questEnd) *questEnd = 0;
    if (!A.nodeName || !A.nodeParent || !A.release) return false;
    const char* names[40]; void* held[40];
    int n = 0, nh = 0, questAt = -1;
    const void* cur = node;
    while (cur && n < 40) {
        const char* nm = nullptr;
        if (!call_name(cur, &nm) || !nm) break;
        if (questAt < 0 && A.questVft) {   // the nearest C_Quest ancestor (or the node itself): the quest this State belongs to
            const void* vp = nullptr;
            if (rd(cur, 0, &vp) && vp == A.questVft) questAt = n;
        }
        names[n++] = nm;
        void* par = nullptr;
        if (!call_parent(cur, &par) || !par) break;
        held[nh++] = par;
        cur = par;
    }
    size_t o = 0;
    bool ok = n > 0;
    for (int i = n - 1; i >= 0 && ok; --i) {
        char buf[160];
        if (!copy_cstr(names[i], buf, sizeof buf)) { ok = false; break; }
        const size_t m = std::strlen(buf);
        if (o + m + 2 >= cap) { ok = false; break; }
        if (o) out[o++] = '.';
        std::memcpy(out + o, buf, m);
        o += m;
        out[o] = 0;
        if (i == questAt && questEnd) *questEnd = o;
    }
    for (int i = 0; i < nh; ++i) call_release(held[i]);
    return ok && o > 0;
}
// "brambora.Barbora.x" (a database segment above the root, WO-99.5) -> "Barbora.x".
const char* canonical(const char* p) {
    const char* b = std::strstr(p, "Barbora.");
    return b ? b : p;
}
int node_runtime(const void* node) {
    int v = -1;
    if (A.runtimeState) call_int(A.runtimeState, node, &v);
    else rd(node, kOffNodeRuntime, &v);
    return v;
}
bool is_state(const void* node) {
    const void* vp = nullptr;
    return A.stateVft && node && rd(node, 0, &vp) && vp == A.stateVft;
}

// ---------------------------------------------------------------------------
// The detector.
// ---------------------------------------------------------------------------
std::atomic<bool> g_detect{false};     // records made (agent Config or research)
std::atomic<bool> g_logAll{false};     // research: one log line per change
std::atomic<bool> g_send_on{false};    // frames to the agent
std::atomic<bool> g_hold{false};       // records kept back (the agent's load hold)
std::atomic<uint32_t> g_seq{0};
std::atomic<uint32_t> c_recorded{0}, c_sent{0}, c_dropped{0}, c_foreign{0}, c_noPath{0};
std::atomic<uint32_t> c_apply[10]{};
DWORD g_mainTid = 0;
uint8_t g_role = 0;

ChangedFn g_origChanged = nullptr;
ExecFn    g_origExecute = nullptr;
// WO-154: the State execution in progress, PER THREAD. Until 0.44.0 this was
// kept for the frame thread only, on the belief that the graph is single-
// threaded. It is not: the field's 400+ port-less records (both evenings,
// every log) were never once flagged a cascade, and both the port and the
// cascade flag were computed on the frame thread only -- they are the changes
// AI behaviours make from their worker threads (a distance trigger, a duel's
// state, a farmhand's evening). Every thread now carries its own execution,
// its own notification depth and its own apply depth, so a worker's change
// gets its port and its consequences are a cascade like anywhere else.
thread_local void* t_execNode = nullptr;
thread_local const void* t_execPort = nullptr;
thread_local int t_changeDepth = 0;   // >0 while a State's own notification runs on this thread (its consumers' changes are a cascade)
thread_local int t_applyDepth = 0;    // >0 while an Apply runs on this thread (its changes are a mirror)
std::atomic<uint32_t> c_worker{0}, c_workerPort{0};

struct Rec {
    uint32_t seq;
    uint8_t flags;
    int32_t oldI, newI;
    uint16_t questLen;   // the quest root's prefix of `path` ("Barbora.trosecko.hledaniPsa")
    uint32_t tid;        // WO-154: the thread the change was made on (logs)
    char port[64];
    char type[48];
    char path[kMaxPath + 1];
};
std::atomic<uint32_t> c_notQuest{0}, c_cascade{0};
std::mutex g_qm;
std::vector<Rec> g_queue;           // under g_qm
constexpr size_t kMaxQueue = 8192;

void record(void* self, const void* oldV, const void* newV, bool notify, bool cascade) {
    Rec r{};
    char full[kMaxPath + 64];
    size_t questEnd = 0;
    if (!node_path(self, full, sizeof full, &questEnd)) { c_noPath.fetch_add(1); return; }
    const char* p = canonical(full);
    if (std::strncmp(p, "Barbora.", 8) != 0) { c_foreign.fetch_add(1); return; }   // Haste and the rest: not quests
    const size_t cut = static_cast<size_t>(p - full);
    if (questEnd <= cut) { c_notQuest.fetch_add(1); return; }   // world simulation (event places, professions, ...): not under a C_Quest
    r.questLen = static_cast<uint16_t>(questEnd - cut);
    strncpy_s(r.path, p, _TRUNCATE);
    if (cascade) c_cascade.fetch_add(1);
    Val o{}, nv{};
    decode(oldV, &o);
    decode(newV, &nv);
    r.oldI = o.i; r.newI = nv.i;
    r.tid = GetCurrentThreadId();
    const bool worker = g_mainTid != 0 && r.tid != g_mainTid;
    r.flags = static_cast<uint8_t>((notify ? kFNotify : 0) | (t_applyDepth > 0 ? kFMirror : 0) |
                                   (o.ok ? kFOldOk : 0) | (nv.ok ? kFNewOk : 0) | (cascade ? kFCascade : 0) |
                                   (worker ? kFWorker : 0));
    strncpy_s(r.type, nv.type[0] ? nv.type : o.type, _TRUNCATE);
    if (t_execNode == self && t_execPort)
        port_name(t_execPort, r.port, sizeof r.port);
    if (worker) { c_worker.fetch_add(1); if (r.port[0]) c_workerPort.fetch_add(1); }
    r.seq = g_seq.fetch_add(1) + 1;
    c_recorded.fetch_add(1);
    std::lock_guard<std::mutex> lk(g_qm);
    if (g_queue.size() >= kMaxQueue) { c_dropped.fetch_add(1); return; }
    g_queue.push_back(r);
}

void hooked_changed(void* self, const void* oldV, const void* newV, bool notify) {
    // Recorded BEFORE the original runs: the notifications below may change
    // further States (a cascade), which then land after this one -- the
    // engine's own order. WO-154: on any thread (t_changeDepth is per thread).
    if (g_detect.load(std::memory_order_relaxed)) record(self, oldV, newV, notify, t_changeDepth > 0);
    ++t_changeDepth;
    g_origChanged(self, oldV, newV, notify);
    --t_changeDepth;
}

void hooked_execute(void* self, const void* ctx) {
    if (!ctx) { g_origExecute(self, ctx); return; }
    void* prevNode = t_execNode;
    const void* prevPort = t_execPort;
    const void* port = nullptr;
    rd(ctx, 8, &port);   // S_NodeExecuteContext: +8 the triggering port (C_Node::Execute reads it there)
    t_execNode = self; t_execPort = port;   // WO-154: per thread (a worker's execution keeps its own port)
    g_origExecute(self, ctx);
    t_execNode = prevNode; t_execPort = prevPort;
}

// ---------------------------------------------------------------------------
// The time gate (joiner): a quest time set runs nothing; OnExec still fires.
// ---------------------------------------------------------------------------
using FnInvokeFn = void* (*)(void* self, void* ret, void* out);
FnInvokeFn g_origFnInvoke = nullptr;
std::atomic<bool> g_timeGate{false};
std::atomic<uint32_t> c_timeSkipped{0};
std::atomic<bool> g_punishGate{false};      // WO-139
std::atomic<uint32_t> c_punishSkipped{0};

using wo137rules::is_time_method;   // wo137_rules.h (pinned by native/tests/wo137_rules_tests.cpp)
bool call_variant_ctor(void* v) {
    KCDMP_FAULT_CALL(site, "wo137::call_variant_ctor");
    return fault::guarded(site, [&] { reinterpret_cast<void (*)(void*)>(A.variantCtor)(v); });
}
std::mutex g_seenM;   // WO-154: the gates run on any thread now
void log_time_skip(void* self, const char* method) {
    static void* seen[64]{};
    static int nSeen = 0;
    {
        std::lock_guard<std::mutex> lk(g_seenM);
        for (int i = 0; i < nSeen; ++i) if (seen[i] == self) return;   // once per node per process
        if (nSeen < 64) seen[nSeen++] = self;
    }
    char path[kMaxPath + 64] = "?";
    node_path(self, path, sizeof path);
    logf("WO137-TIMESET joiner: quest time set %s (%s) NOT run -- only the host's clock moves the world "
         "(its OnExec still fires; the host's time reaches this game through the time sync)", canonical(path), method);
}
// WO-154: both gates hold on every thread. They were frame-thread only (as the
// port attribution was), so a quest time set an AI behaviour ran from a worker
// would have passed the joiner's gate.
void* hooked_fn_invoke(void* self, void* ret, void* out) {
    if (g_timeGate.load(std::memory_order_relaxed)) {
        const char* s = nullptr; char m[128];
        if (rd(self, kOffFnMethodName, &s) && s && copy_cstr(s, m, sizeof m) && is_time_method(m) && call_variant_ctor(ret)) {
            c_timeSkipped.fetch_add(1);
            log_time_skip(self, m);
            return ret;
        }
    }
    // WO-139: the punishment's own time sets, on either machine (only its nodes).
    if (g_punishGate.load(std::memory_order_relaxed)) {
        const char* s = nullptr; char m[128];
        if (rd(self, kOffFnMethodName, &s) && s && copy_cstr(s, m, sizeof m) && is_time_method(m)) {
            char path[kMaxPath + 64] = "";
            if (node_path(self, path, sizeof path) && wo137rules::is_punishment_path(path) && call_variant_ctor(ret)) {
                c_punishSkipped.fetch_add(1);
                logf("WO139-TIMESET punishment time set %s (%s) NOT run -- no time skip for punishment in co-op "
                     "(its OnExec still fires: the punishment goes on)", canonical(path), m);
                return ret;
            }
        }
    }
    return g_origFnInvoke(self, ret, out);
}

bool swap_slot(void* const* vft, size_t slot, void* repl, void** orig) {
    void** at = const_cast<void**>(&vft[slot]);
    DWORD oldProt = 0, ignored = 0;
    if (!VirtualProtect(at, sizeof(void*), PAGE_READWRITE, &oldProt)) return false;
    void* prev = InterlockedExchangePointer(at, repl);
    VirtualProtect(at, sizeof(void*), oldProt, &ignored);
    if (prev != *orig) {   // someone else changed it between our read and our write: put theirs back
        if (VirtualProtect(at, sizeof(void*), PAGE_READWRITE, &oldProt)) {
            InterlockedExchangePointer(at, prev);
            VirtualProtect(at, sizeof(void*), oldProt, &ignored);
        }
        return false;
    }
    return true;
}

// ---------------------------------------------------------------------------
// Apply: one Set<Value> pulse on a quest State (the engine's own entry).
// ---------------------------------------------------------------------------
Applied apply(const char* path, const char* portNm, Val* before, Val* after) {
    *before = Val{}; *after = Val{};
    if (!A.apply) return Applied::Unarmed;
    void* node = find_node(path);
    if (!node) return Applied::NoNode;
    Applied res = Applied::Fault;
    void* port = nullptr;
    do {
        if (!is_state(node)) { res = Applied::NotAState; break; }
        {   // under a C_Quest (the same rule as the detector): nothing else is ever pulsed
            char qp[kMaxPath + 64]; size_t qe = 0;
            if (!node_path(node, qp, sizeof qp, &qe) || qe == 0) { res = Applied::NotAQuest; break; }
        }
        decode(static_cast<char*>(node) + kOffStateValue, before);
        if (node_runtime(node) == 1) { res = Applied::Asleep; break; }   // 1 = Hibernating (C_Node::Execute's branch)
        port = find_port(node, portNm);
        if (!port) { res = Applied::NoPort; break; }
        const void* pv = nullptr; void* trig = nullptr;
        if (!rd(port, 0, &pv) || !pv || !rd(pv, kSlotPortTrigger * 8, &trig)) { res = Applied::Fault; break; }
        if ((pv != A.inTrigVft && pv != A.autoTrigVft) || trig != A.inTrigTrigger) { res = Applied::PortRefused; break; }
        int dir = -1;
        if (A.portDirection && (!call_int(A.portDirection, port, &dir) || dir != 1)) { res = Applied::PortRefused; break; }
        ++t_applyDepth;   // WO-154: per thread (a worker's own change during this pulse is no mirror)
        const bool ran = call_void(trig, port);
        --t_applyDepth;
        if (!ran) { res = Applied::Fault; break; }
        decode(static_cast<char*>(node) + kOffStateValue, after);
        const bool same = before->valid == after->valid && before->ok == after->ok && before->i == after->i &&
                          std::strcmp(before->type, after->type) == 0;
        res = same ? Applied::Unchanged : Applied::Changed;
    } while (false);
    call_release(port);
    call_release(node);
    c_apply[static_cast<size_t>(res) < 10 ? static_cast<size_t>(res) : 7].fetch_add(1);
    return res;
}

// ---------------------------------------------------------------------------
// WO-164 T6: the direct write of a quest State's value, for a value no port is known to produce (the field: the Mutt bait's
// numberOfMealsIgnazHasEaten stood at 2 against the host's 1 for 7 minutes). What the State's own setter does after its port
// fires: the variant at +0x68 takes the value, then slot 42 (the change notification WO-137 hooks) runs with the old and the
// new value, so its consumers run exactly as they would. Only an inline "int" or "bool" variant (no enum: its width and range
// are not read here); the write is read back and undone when it does not read as written.
// ---------------------------------------------------------------------------
std::atomic<uint32_t> c_setOk{0}, c_setRefused{0}, c_setFault{0};
bool set_value_call(void* node, const void* oldV, int v, bool isBool) {
    KCDMP_FAULT_CALL(site, "wo137::set_value");
    return fault::guarded(site, [&] {
        char* at = static_cast<char*>(node) + kOffStateValue;
        if (isBool) *reinterpret_cast<uint8_t*>(at) = v ? 1 : 0;
        else *reinterpret_cast<int32_t*>(at) = v;
        ++t_applyDepth;   // a mirror of the host's value, never sent back as this player's own step
        hooked_changed(node, oldV, at, true);
        --t_applyDepth;
    });
}
bool undo_value(void* node, const uint8_t* oldCopy) {
    KCDMP_FAULT_READ(site, "wo137::set_value_undo");
    return fault::guarded(site, [&] { std::memcpy(static_cast<char*>(node) + kOffStateValue, oldCopy, 24); });
}
Applied set_value(const char* path, int v, Val* before, Val* after) {
    *before = Val{}; *after = Val{};
    if (!A.apply || !g_origChanged) return Applied::Unarmed;
    void* node = find_node(path);
    if (!node) return Applied::NoNode;
    Applied res = Applied::Fault;
    do {
        if (!is_state(node)) { res = Applied::NotAState; break; }
        { char qp[kMaxPath + 64]; size_t qe = 0; if (!node_path(node, qp, sizeof qp, &qe) || qe == 0) { res = Applied::NotAQuest; break; } }
        if (node_runtime(node) == 1) { res = Applied::Asleep; break; }
        decode(static_cast<char*>(node) + kOffStateValue, before);
        const bool isInt = std::strcmp(before->type, "int") == 0, isBool = std::strcmp(before->type, "bool") == 0;
        if (!before->valid || !before->ok || !(isInt || isBool)) { res = Applied::TypeRefused; break; }
        if (isBool && v != 0 && v != 1) { res = Applied::TypeRefused; break; }
        if (before->i == v) { res = Applied::Unchanged; break; }
        alignas(8) uint8_t oldCopy[32]{};
        if (!copy_bytes(static_cast<char*>(node) + kOffStateValue, oldCopy, 24)) { res = Applied::Fault; break; }
        if (!set_value_call(node, oldCopy, v, isBool)) { res = Applied::Fault; break; }
        decode(static_cast<char*>(node) + kOffStateValue, after);
        if (!(after->ok && after->i == v && std::strcmp(after->type, before->type) == 0)) {
            // not what was written: the old bytes go back (no notification: the consumers saw the change, the next checkpoint compares)
            undo_value(node, oldCopy);
            res = Applied::Fault; break;
        }
        res = Applied::Changed;
    } while (false);
    call_release(node);
    (res == Applied::Changed ? c_setOk : res == Applied::Fault ? c_setFault : c_setRefused).fetch_add(1);
    return res;
}

const char* applied_text(Applied a) {
    switch (a) {
        case Applied::Changed: return "changed";
        case Applied::Unchanged: return "unchanged (already that value)";
        case Applied::NoNode: return "no such node";
        case Applied::NotAState: return "not a State node";
        case Applied::NoPort: return "no such port";
        case Applied::PortRefused: return "port refused (not a State In trigger)";
        case Applied::Asleep: return "node not active here (hibernated)";
        case Applied::Fault: return "FAULT";
        case Applied::Unarmed: return "apply not armed";
        case Applied::NotAQuest: return "not under a quest";
        case Applied::TypeRefused: return "type refused (only int / bool are written directly)";
    }
    return "?";
}

// ---------------------------------------------------------------------------
// Q1: the exported quest getters.
// ---------------------------------------------------------------------------
struct ObjRow { char name[80]; int type = -1; int log = -1; uint32_t order = 0; int64_t last = 0; };

bool read_quest(const char* path, int* level, int64_t* questLast, std::vector<ObjRow>* rows, char* cls, size_t clsN) {
    rows->clear();
    if (!A.read) return false;
    void* node = find_node(path);
    if (!node) return false;
    bool ok = false;
    rtti_name(node, cls, clsN);
    const void* vp = nullptr;
    if (rd(node, 0, &vp) && vp == A.questVft) {
        ok = true;
        call_int(A.qLevel, node, level);
        call_i64(A.qLast, node, questLast);
        const void* vec = nullptr;
        if (call_ptr(A.qObjectives, node, &vec) && vec) {
            const char* b = nullptr; const char* e = nullptr;
            rd(vec, 0, &b); rd(vec, 8, &e);
            void* cmod = concept_module(); void* res = nullptr;
            if (b && e && e >= b && (e - b) / 16 <= 256 && cmod && call_resolver(cmod, &res) && res) {
                for (const char* p = b; p < e; p += 16) {
                    uint32_t id = 0;
                    if (!rd(p, 0, &id)) continue;
                    void* obj = nullptr;
                    if (!call_resolve(res, id, &obj) || !obj) continue;
                    ObjRow r{};
                    const void* nm = nullptr;
                    if (call_ptr(A.oName, obj, &nm) && nm) { const char* s = nullptr; if (rd(nm, 0, &s)) copy_cstr(s, r.name, sizeof r.name); }
                    call_int(A.oType, obj, &r.type);
                    call_int(A.oLog, obj, &r.log);
                    call_u32(A.oOrder, obj, &r.order);
                    call_i64(A.oLast, obj, &r.last);
                    rows->push_back(r);
                    call_release(obj);
                }
            }
        }
    }
    call_release(node);
    return ok;
}

// ---------------------------------------------------------------------------
// Q5: a pass-through proxy on the quest HUD sink (SetUIHudEvents). Research.
// ---------------------------------------------------------------------------
struct HudProxy { void* const* vtbl; void* orig; };
HudProxy g_proxy{};
void* g_proxyVtbl[5]{};
void* g_questModule = nullptr;
std::atomic<uint32_t> c_hud[5]{};

void arg_text(uint64_t a, char* out, size_t n) {
    // A CryStringT const& is a pointer to a char*; print the text when it reads as one.
    const char* s = nullptr;
    char buf[96];
    if (a > 0x10000 && rd(reinterpret_cast<const void*>(a), 0, &s) && s &&
        reinterpret_cast<uintptr_t>(s) > 0x10000 && copy_cstr(s, buf, sizeof buf) && buf[0]) {
        bool printable = true;
        for (const char* c = buf; *c; ++c) if (static_cast<unsigned char>(*c) < 32 || static_cast<unsigned char>(*c) > 126) { printable = false; break; }
        if (printable) { _snprintf_s(out, n, _TRUNCATE, "\"%s\"", buf); return; }
    }
    _snprintf_s(out, n, _TRUNCATE, "0x%llX", static_cast<unsigned long long>(a));
}
void hud_log(int slot, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6) {
    c_hud[slot].fetch_add(1);
    char t2[128], t3[128], t4[128], t5[128], t6[128];
    arg_text(a2, t2, sizeof t2); arg_text(a3, t3, sizeof t3); arg_text(a4, t4, sizeof t4);
    arg_text(a5, t5, sizeof t5); arg_text(a6, t6, sizeof t6);
    logf("WO137-HUD slot=%d a2=%s a3=%s a4=%s a5=%s a6=%s", slot, t2, t3, t4, t5, t6);
}
template <int I>
uint64_t hud_fwd(HudProxy* self, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6, uint64_t a7) {
    if (I <= 1) hud_log(I, a2, a3, a4, a5, a6);   // slots 0/1 are the event entries; 2..4 are accessors
    else c_hud[I].fetch_add(1);
    void* orig = self->orig;
    void* const* vt = *static_cast<void* const* const*>(orig);
    using F = uint64_t (*)(void*, uint64_t, uint64_t, uint64_t, uint64_t, uint64_t, uint64_t);
    return reinterpret_cast<F>(vt[I])(orig, a2, a3, a4, a5, a6, a7);
}

using ModByNameFn = void* (*)(const void* mm, const char* name);
bool call_mod_by_name(const void* mm, const char* name, void** out) {
    KCDMP_FAULT_CALL(site, "wo137::call_mod_by_name");
    if (fault::guarded(site, [&] { *out = reinterpret_cast<ModByNameFn>(A.getModuleByName)(mm, name); })) return true;
    *out = nullptr;
    return false;
}

// The QuestModule instance, by its RTTI class (a slot may hold an interface
// subobject: the COL offset leads back to the full object). Where: the
// modules manager's vector (C_GameInterface+0xB8 -> +0x10/+0x18, the list
// C_ModulesManager::RegisterModule appends to), then the game interface's own
// module slots and one level below them.
void* quest_module_of(void* p) {
    char cls[96];
    if (!p || !rtti_name(p, cls, sizeof cls) || std::strcmp(cls, ".?AVC_QuestModule@questmodule@wh@@") != 0) return nullptr;
    uint32_t off = 0;
    if (!rtti_offset(p, &off)) return nullptr;
    void* full = static_cast<char*>(p) - off;
    const void* vp = nullptr;
    return (rd(full, 0, &vp) && vp == A.questModuleVft) ? full : nullptr;
}
int g_qmHow = 0;   // 1 registry, 2 gameIface slot, 3 one level down (for the log)
void* find_quest_module() {
    const void* gi = nullptr;
    if (!A.questModuleVft || !call_gi(&gi) || !gi) return nullptr;
    void* mm = nullptr;
    if (rd(gi, 0xB8, &mm) && mm) {
        void** b = nullptr; void** e = nullptr;
        if (rd(mm, 0x10, &b) && rd(mm, 0x18, &e) && b && e && e >= b && e - b < 256) {
            for (void** it = b; it < e; ++it) {
                void* m = nullptr;
                if (rd(it, 0, &m)) if (void* q = quest_module_of(m)) { g_qmHow = 1; return q; }
            }
        }
    }
    for (size_t off = 0; off < 0x400; off += 8) {
        void* p = nullptr;
        if (rd(gi, off, &p)) if (void* q = quest_module_of(p)) { g_qmHow = 2; return q; }
    }
    for (size_t off = 0; off < 0x400; off += 8) {
        void* qp = nullptr;
        if (!rd(gi, off, &qp) || !qp) continue;
        for (size_t j = 0; j < 0x400; j += 8) {
            void* p = nullptr;
            if (rd(qp, j, &p)) if (void* q = quest_module_of(p)) { g_qmHow = 3; return q; }
        }
    }
    return nullptr;
}

// Diagnostics: every registered module's class and name (research, once).
void log_modules() {
    const void* gi = nullptr; void* mm = nullptr;
    if (!call_gi(&gi) || !gi || !rd(gi, 0xB8, &mm) || !mm) { logf("WO137-MODULES no modules manager"); return; }
    void** b = nullptr; void** e = nullptr;
    if (!rd(mm, 0x10, &b) || !rd(mm, 0x18, &e) || !b || !e || e < b || e - b >= 256) { logf("WO137-MODULES vector unreadable"); return; }
    logf("WO137-MODULES manager %p: %lld modules", mm, static_cast<long long>(e - b));
    for (void** it = b; it < e; ++it) {
        void* m = nullptr; char cls[96] = "?";
        if (!rd(it, 0, &m) || !m) continue;
        rtti_name(m, cls, sizeof cls);
        logf("WO137-MODULES   %p %s", m, cls);
    }
}

// Q5 fallback: the same pass-through at the sink's own vtable (every call made
// through I_UIHudEventsQuest to C_UIHudEvents goes through these two slots).
void* g_vtOrig[2]{};
bool g_vtOn = false;
template <int I>
uint64_t hud_vt(void* self, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6, uint64_t a7) {
    hud_log(I, a2, a3, a4, a5, a6);
    using F = uint64_t (*)(void*, uint64_t, uint64_t, uint64_t, uint64_t, uint64_t, uint64_t);
    return reinterpret_cast<F>(g_vtOrig[I])(self, a2, a3, a4, a5, a6, a7);
}
bool hud_vtable(bool on, char* why, size_t whyN);

bool hud_on(bool on, char* why, size_t whyN) {
    if (!A.hud) { strncpy_s(why, whyN, "hud anchors not armed", _TRUNCATE); return false; }
    if (!g_questModule) g_questModule = find_quest_module();
    if (!g_questModule) {
        log_modules();
        const bool ok = hud_vtable(on, why, whyN);
        return ok;
    }
    void* cur = nullptr;
    if (!call_hud_get(g_questModule, &cur)) { strncpy_s(why, whyN, "GetUIHudEvents faulted", _TRUNCATE); return false; }
    if (on) {
        if (cur == &g_proxy) { strncpy_s(why, whyN, "already on", _TRUNCATE); return true; }
        const void* vp = nullptr;
        if (!cur || !rd(cur, 0, &vp) || vp != A.hudQuestVft) {
            _snprintf_s(why, whyN, _TRUNCATE, "the sink %p is not C_UIHudEvents' I_UIHudEventsQuest -- not proxied", cur);
            return false;
        }
        g_proxyVtbl[0] = reinterpret_cast<void*>(&hud_fwd<0>);
        g_proxyVtbl[1] = reinterpret_cast<void*>(&hud_fwd<1>);
        g_proxyVtbl[2] = reinterpret_cast<void*>(&hud_fwd<2>);
        g_proxyVtbl[3] = reinterpret_cast<void*>(&hud_fwd<3>);
        g_proxyVtbl[4] = reinterpret_cast<void*>(&hud_fwd<4>);
        g_proxy.vtbl = g_proxyVtbl;
        g_proxy.orig = cur;
        if (!call_hud_set(g_questModule, &g_proxy)) { strncpy_s(why, whyN, "SetUIHudEvents faulted", _TRUNCATE); return false; }
        _snprintf_s(why, whyN, _TRUNCATE, "SetUIHudEvents proxy on (quest module %p found by route %d, sink %p)", g_questModule, g_qmHow, cur);
        return true;
    }
    if (cur != &g_proxy) { strncpy_s(why, whyN, "not on", _TRUNCATE); return true; }
    call_hud_set(g_questModule, g_proxy.orig);
    _snprintf_s(why, whyN, _TRUNCATE, "proxy off (sink %p back)", g_proxy.orig);
    return true;
}

bool hud_vtable(bool on, char* why, size_t whyN) {
    void** at = const_cast<void**>(A.hudQuestVft);
    if (!at) { strncpy_s(why, whyN, "C_QuestModule not found and no sink vtable", _TRUNCATE); return false; }
    if (on == g_vtOn) { strncpy_s(why, whyN, on ? "vtable pass-through already on" : "not on", _TRUNCATE); return true; }
    void* repl[2] = { reinterpret_cast<void*>(&hud_vt<0>), reinterpret_cast<void*>(&hud_vt<1>) };
    for (int i = 0; i < 2; ++i) {
        DWORD oldProt = 0, ignored = 0;
        if (!VirtualProtect(&at[i], sizeof(void*), PAGE_READWRITE, &oldProt)) { strncpy_s(why, whyN, "VirtualProtect failed", _TRUNCATE); return false; }
        if (on) g_vtOrig[i] = InterlockedExchangePointer(&at[i], repl[i]);
        else InterlockedExchangePointer(&at[i], g_vtOrig[i]);
        VirtualProtect(&at[i], sizeof(void*), oldProt, &ignored);
    }
    g_vtOn = on;
    _snprintf_s(why, whyN, _TRUNCATE, "C_QuestModule not found in the registry -- pass-through %s at the sink's I_UIHudEventsQuest vtable %p (slots 0,1)",
                on ? "ON" : "off", at);
    return true;
}

// ---------------------------------------------------------------------------
// The research file.
// ---------------------------------------------------------------------------
bool research_path(char* out, size_t n) {
    char cwd[MAX_PATH]{};
    if (!GetCurrentDirectoryA(MAX_PATH, cwd) || !cwd[0]) return false;
    _snprintf_s(out, n, _TRUNCATE, "%s%skcdmp-quest.txt", cwd, cwd[std::strlen(cwd) - 1] == '\\' ? "" : "\\");
    return true;
}

void log_state(const char* path) {
    void* node = find_node(path);
    if (!node) { logf("WO137-STATE %s -- no node", path); return; }
    char cls[96]; rtti_name(node, cls, sizeof cls);
    Val v{};
    if (is_state(node)) decode(static_cast<char*>(node) + kOffStateValue, &v);
    char p[kMaxPath + 64]; node_path(node, p, sizeof p);
    logf("WO137-STATE %s class=%s runtime=%d valid=%d ok=%d int=%d type=%s walk=%s", path, cls, node_runtime(node),
         v.valid, v.ok, v.i, v.type, p);
    call_release(node);
}

void research_line(const char* line) {
    char verb[16]{}, a1[kMaxPath + 1]{}, a2[128]{}, a3[16]{};
    const int got = sscanf_s(line, "%15s %400s %127s %15s", verb, (unsigned)sizeof verb, a1, (unsigned)sizeof a1,
                             a2, (unsigned)sizeof a2, a3, (unsigned)sizeof a3);
    if (got < 1) return;
    if (!_stricmp(verb, "detect")) {
        const bool on = got >= 2 && !_stricmp(a1, "on");
        g_detect = on || g_send_on.load(); g_logAll = on;
        logf("WO137-RESEARCH detect %s (armed=%d)", on ? "on" : "off", A.detector);
    } else if (!_stricmp(verb, "hud")) {
        char why[160]{};
        const bool ok = hud_on(got >= 2 && !_stricmp(a1, "on"), why, sizeof why);
        logf("WO137-RESEARCH hud %s -- %s: %s", a1, ok ? "ok" : "refused", why);
    } else if (!_stricmp(verb, "timegate")) {
        g_timeGate = got >= 2 && !_stricmp(a1, "on") && A.timeGate;
        logf("WO137-RESEARCH timegate %s (armed=%d)", g_timeGate.load() ? "on" : "off", A.timeGate);
    } else if (!_stricmp(verb, "state") && got >= 2) {
        log_state(a1);
    } else if (!_stricmp(verb, "read") && got >= 2) {
        int level = -1; int64_t last = 0; std::vector<ObjRow> rows; char cls[96]{};
        const bool ok = read_quest(a1, &level, &last, &rows, cls, sizeof cls);
        logf("WO137-READ %s -- %s class=%s level=%d last=%lld objectives=%zu", a1, ok ? "C_Quest" : "NOT a quest",
             cls, level, static_cast<long long>(last), rows.size());
        for (const auto& r : rows)
            logf("WO137-READ   obj=%s type=%d log=%d order=%u last=%lld", r.name, r.type, r.log, r.order, static_cast<long long>(r.last));
    } else if (!_stricmp(verb, "pulse") && got >= 3) {
        using conceptread::PortGate;
        const PortGate g = conceptread::port_trigger_gate(respawn::session_active(), pipe::connected());
        if (g != PortGate::Allowed || got < 4 || std::strcmp(a3, "FIRE") != 0) {
            logf("WO137-RESEARCH pulse %s :: %s REFUSED -- %s", a1, a2,
                 g != PortGate::Allowed ? conceptread::port_gate_text(g) : "the FIRE token is missing");
            return;
        }
        Val b{}, a{};
        const Applied r = apply(a1, a2, &b, &a);
        logf("WO137-PULSE %s :: %s -> %s  before=(%d ok=%d %s) after=(%d ok=%d %s)", a1, a2, applied_text(r),
             b.i, b.ok, b.type, a.i, a.ok, a.type);
    } else {
        logf("WO137-RESEARCH unknown line: %s", line);
    }
}

void research_watch() {
    char path[MAX_PATH]{};
    if (!research_path(path, sizeof path)) return;
    WIN32_FILE_ATTRIBUTE_DATA fa{};
    static FILETIME lastWrite{};
    static DWORD lastSize = 0xFFFFFFFF;
    static bool baseline = false;
    if (!GetFileAttributesExA(path, GetFileExInfoStandard, &fa)) {
        lastSize = 0xFFFFFFFF;
        baseline = true;   // no file when this process started: the baseline is empty, a file made later runs
        return;
    }
    if (CompareFileTime(&fa.ftLastWriteTime, &lastWrite) == 0 && fa.nFileSizeLow == lastSize) return;
    lastWrite = fa.ftLastWriteTime; lastSize = fa.nFileSizeLow;
    char text[4096]{};
    FILE* f = nullptr;
    if (fopen_s(&f, path, "r") != 0 || !f) return;
    const size_t n = std::fread(text, 1, sizeof(text) - 1, f);
    std::fclose(f);
    text[n] = 0;
    static std::string last;
    if (last == text && baseline) return;
    last = text;
    if (!baseline) {   // whatever the file held when this process started is never run: a leftover FIRE must not re-fire
        baseline = true;
        logf("WO137-RESEARCH kcdmp-quest.txt present at start -- its content is a baseline, not run");
        return;
    }
    char* ctx = nullptr;
    for (char* l = strtok_s(text, "\r\n", &ctx); l; l = strtok_s(nullptr, "\r\n", &ctx))
        if (*l && *l != '#') research_line(l);
}

// ---------------------------------------------------------------------------
// The queue to the agent.
// ---------------------------------------------------------------------------
void emit(const Rec& r) {
    if (g_logAll.load() || g_send_on.load()) {
        logf("WO137-CHANGE seq=%u %s %s -> %d%s (from %d) type=%s%s%s%s%s tid=%u", r.seq, r.path, r.port[0] ? r.port : "-",
             r.newI, (r.flags & kFNewOk) ? "" : "?", r.oldI, r.type, (r.flags & kFMirror) ? " MIRROR" : "",
             (r.flags & kFCascade) ? " cascade" : " root", (r.flags & kFNotify) ? "" : " silent",
             (r.flags & kFWorker) ? " worker" : "", r.tid);
    }
    if (!g_send_on.load() || !g_send) return;
    uint8_t b[1 + 4 + 4 + 4 + 1 + 64 + 1 + 48 + 2 + kMaxPath + 8];
    size_t o = 0;
    std::memcpy(b + o, &r.seq, 4); o += 4;
    b[o++] = r.flags;
    std::memcpy(b + o, &r.oldI, 4); o += 4;
    std::memcpy(b + o, &r.newI, 4); o += 4;
    const size_t pl = std::strlen(r.port); b[o++] = static_cast<uint8_t>(pl); std::memcpy(b + o, r.port, pl); o += pl;
    const size_t tl = std::strlen(r.type); b[o++] = static_cast<uint8_t>(tl); std::memcpy(b + o, r.type, tl); o += tl;
    const uint16_t ph = static_cast<uint16_t>(std::strlen(r.path));
    std::memcpy(b + o, &ph, 2); o += 2; std::memcpy(b + o, r.path, ph); o += ph;
    std::memcpy(b + o, &r.questLen, 2); o += 2;
    g_send(b, static_cast<uint16_t>(o));
    c_sent.fetch_add(1);
}

void drain() {
    if (g_hold.load() && g_send_on.load()) return;   // kept, in order, until the hold lifts
    std::vector<Rec> batch;
    {
        std::lock_guard<std::mutex> lk(g_qm);
        if (g_queue.empty()) return;
        batch.swap(g_queue);
    }
    for (const auto& r : batch) emit(r);
}

void status_text(char* out, size_t n) {
    _snprintf_s(out, n, _TRUNCATE,
        "wo137 detector=%d apply=%d read=%d hud=%d detect=%d send=%d hold=%d role=%u recorded=%u sent=%u dropped=%u "
        "foreign=%u nopath=%u applies changed=%u unchanged=%u nonode=%u notstate=%u noport=%u refused=%u asleep=%u fault=%u "
        "hud0=%u hud1=%u queue=%zu timegate=%d/%d skipped=%u notquest=%u cascade=%u worker=%u worker_port=%u",
        A.detector, A.apply, A.read, A.hud, g_detect.load(), g_send_on.load(), g_hold.load(), g_role, c_recorded.load(),
        c_sent.load(), c_dropped.load(), c_foreign.load(), c_noPath.load(), c_apply[0].load(), c_apply[1].load(),
        c_apply[2].load(), c_apply[3].load(), c_apply[4].load(), c_apply[5].load(), c_apply[6].load(), c_apply[7].load(),
        c_hud[0].load(), c_hud[1].load(), g_queue.size(), A.timeGate, g_timeGate.load(), c_timeSkipped.load(),
        c_notQuest.load(), c_cascade.load(), c_worker.load(), c_workerPort.load());
}

void put_u16(uint8_t* p, uint16_t v) { std::memcpy(p, &v, 2); }
uint16_t get_u16(const uint8_t* p) { uint16_t v; std::memcpy(&v, p, 2); return v; }

} // namespace

// ===========================================================================
void install() {
    A.cm = GetModuleHandleA("ConceptModule.dll");
    A.qm = GetModuleHandleA("QuestModule.dll");
    A.gm = GetModuleHandleA("GUIModule.dll");
    A.cs = GetModuleHandleA("CrySystem.dll");
    if (HMODULE sh = GetModuleHandleA("Shared.dll"))
        A.getGameIface = GetProcAddress(sh, "?GetGameIface@wh@@YAPEBVC_GameInterface@shared@1@XZ");
    if (!A.cm || !A.qm || !A.cs || !A.getGameIface) {
        logf("WO137-BUILD modules missing (cm=%p qm=%p cs=%p gi=%p) -- quest sync NOT armed", A.cm, A.qm, A.cs, A.getGameIface);
        return;
    }
    const auto ce = module_exports(A.cm);
    A.findNode      = find_export(ce, "?FindNode@C_ConceptManager@conceptmodule@wh@@");
    A.getPort       = find_export(ce, "?GetPort@C_Node@conceptmodule@wh@@");
    A.nodeName      = find_export(ce, "?GetName@C_Node@conceptmodule@wh@@");
    A.nodeParent    = find_export(ce, "?GetParent@C_Node@conceptmodule@wh@@");
    A.release       = find_export(ce, "?Release@C_SharedResource@conceptmodule@wh@@");
    A.portDirection = find_export(ce, "?GetDirection@I_Port@conceptmodule@wh@@");
    A.runtimeState  = find_export(ce, "?GetRuntimeState@C_Node@conceptmodule@wh@@");
    A.resolver      = find_export(ce, "?GetResourceResolver@C_ConceptModule@conceptmodule@wh@@");
    void* execute   = find_export(ce, "?Execute@C_Node@conceptmodule@wh@@");
    A.stateVft    = anchor::find_vftable(A.cm, ".?AVC_StateVariable@conceptmodule@wh@@", 0);
    A.inTrigVft   = anchor::find_vftable(A.cm, ".?AVC_InputTriggerPort@conceptmodule@wh@@", 0);
    A.autoTrigVft = anchor::find_vftable(A.cm, ".?AVC_AutoTriggerPort@conceptmodule@wh@@", 0);
    const auto se = module_exports(A.cs);
    A.vToInt   = find_export(se, "?to_int@variant@rttr@@");
    A.vGetType = find_export(se, "?get_type@variant@rttr@@");
    A.tGetName = find_export(se, "?get_name@type@rttr@@");
    A.vIsValid = find_export(se, "?is_valid@variant@rttr@@");
    const auto qe = module_exports(A.qm);
    A.questVft       = anchor::find_vftable(A.qm, ".?AVC_Quest@questmodule@wh@@", 0);
    A.questModuleVft = find_export(qe, "??_7C_QuestModule@questmodule@wh@@6B@");
    A.qObjectives = find_export(qe, "?GetObjectives@C_Quest@questmodule@wh@@");
    A.qLevel      = find_export(qe, "?GetLevelId@C_Quest@questmodule@wh@@");
    A.qLast       = find_export(qe, "?GetLastUpdateTime@C_Quest@questmodule@wh@@");
    A.oName       = find_export(qe, "?GetObjectiveName@C_Objective@questmodule@wh@@");
    A.oType       = find_export(qe, "?GetType@C_Objective@questmodule@wh@@");
    A.oLog        = find_export(qe, "?GetLogType@C_Objective@questmodule@wh@@");
    A.oOrder      = find_export(qe, "?GetOrder@C_Objective@questmodule@wh@@");
    A.oLast       = find_export(qe, "?GetLastUpdateTime@C_Objective@questmodule@wh@@");
    A.setHud      = find_export(qe, "?SetUIHudEvents@C_QuestModule@questmodule@wh@@");
    A.getHud      = find_export(qe, "?GetUIHudEvents@C_QuestModule@questmodule@wh@@");
    if (A.gm) A.hudQuestVft = anchor::find_vftable(A.gm, ".?AVC_UIHudEvents@guimodule@wh@@", 88);
    if (HMODULE fw = GetModuleHandleA("Framework.dll"))
        A.getModuleByName = find_export(module_exports(fw), "?GetModuleByName@C_ModulesManager@framework@wh@@");

    // --- the two C_StateVariable slots, structurally verified ----------------
    // slot 42: the one function that references the setter's own trace string
    // must call [rax+0x150]; slot 33: C_Node::Execute must call [rax+0x108].
    char why[160] = "ok";
    const uint8_t kCall150[] = { 0xFF, 0x90, 0x50, 0x01, 0x00, 0x00 };
    const uint8_t kCall108[] = { 0xFF, 0x90, 0x08, 0x01, 0x00, 0x00 };
    anchor::Range text{};
    anchor::section(A.cm, ".text", &text);
    void* s33 = nullptr; void* s42 = nullptr;
    if (A.stateVft) { rd(A.stateVft, kSlotExecuteNode * 8, &s33); rd(A.stateVft, kSlotStateChanged * 8, &s42); }
    // Two setters reference the trace string (C_StateVariable's and one other
    // State class's), so the one wanted is found through the class itself: the
    // direct call target of its own slot 33 that references the string.
    const char* traceStr = anchor::find_cstring(A.cm, "state value changed from '%s' to '%s'");
    const uint8_t* setter = nullptr;
    int setters = 0;
    if (s33 && traceStr && text.contains(s33)) {
        const uint8_t* targets[96];
        const int nt = anchor::function_call_targets(A.cm, s33, targets, 96);
        for (int i = 0; i < nt; ++i)
            if (anchor::function_refs(A.cm, targets[i], traceStr)) { setter = targets[i]; ++setters; }
    }
    if (!A.stateVft) strncpy_s(why, "C_StateVariable vftable not found", _TRUNCATE);
    else if (setters != 1) _snprintf_s(why, sizeof why, _TRUNCATE, "the State setter: %d call targets of slot 33 reference its trace string (want 1)", setters);
    else if (!anchor::function_has_bytes(A.cm, setter, kCall150, sizeof kCall150)) strncpy_s(why, "the setter does not call slot 42", _TRUNCATE);
    else if (!execute || !anchor::function_has_bytes(A.cm, execute, kCall108, sizeof kCall108)) strncpy_s(why, "C_Node::Execute does not call slot 33", _TRUNCATE);
    else if (!s33 || !s42 || !text.contains(s33) || !text.contains(s42)) strncpy_s(why, "slot 33/42 not in ConceptModule .text", _TRUNCATE);
    else if (!A.nodeName || !A.nodeParent || !A.release) strncpy_s(why, "node name/parent/release exports missing", _TRUNCATE);
    else {
        g_origExecute = reinterpret_cast<ExecFn>(s33);
        g_origChanged = reinterpret_cast<ChangedFn>(s42);
        void* o33 = s33; void* o42 = s42;
        if (!swap_slot(A.stateVft, kSlotExecuteNode, reinterpret_cast<void*>(&hooked_execute), &o33)) strncpy_s(why, "slot 33 swap failed", _TRUNCATE);
        else if (!swap_slot(A.stateVft, kSlotStateChanged, reinterpret_cast<void*>(&hooked_changed), &o42)) {
            void* back = reinterpret_cast<void*>(&hooked_execute);
            swap_slot(A.stateVft, kSlotExecuteNode, s33, &back);
            strncpy_s(why, "slot 42 swap failed", _TRUNCATE);
        } else A.detector = true;
    }
    // The apply: the shared slot 15 of C_InputTriggerPort / C_AutoTriggerPort
    // (0xCC580 on 1.5.5: C_Node::Execute on the owner, WO-99.5's "own override").
    void* t1 = nullptr; void* t2 = nullptr;
    if (A.inTrigVft && rd(A.inTrigVft, kSlotPortTrigger * 8, &t1) && t1 && text.contains(t1) &&
        (!A.autoTrigVft || (rd(A.autoTrigVft, kSlotPortTrigger * 8, &t2) && t2 == t1)) &&
        execute && anchor::function_calls(A.cm, t1, execute) && A.findNode && A.getPort && A.stateVft && A.release) {
        A.inTrigTrigger = t1;
        A.apply = true;
    }
    // The time gate: C_Function's slot 12 (the invoke). Verified: two calls
    // down it reads the method name at +0x68 (mov r8,[rbx+0x68], the getter
    // that hands it to rttr::type::get_method).
    A.fnVft = anchor::find_vftable(A.cm, ".?AVC_Function@conceptmodule@wh@@", 0);
    A.variantCtor = find_export(se, "??0variant@rttr@@QEAA@XZ");
    {
        void* s12 = nullptr;
        bool nameRead = false;
        const uint8_t kReadName[] = { 0x4C, 0x8B, 0x43, 0x68 };
        if (A.fnVft && rd(A.fnVft, kSlotFnInvoke * 8, &s12) && s12 && text.contains(s12) && A.variantCtor) {
            const uint8_t* t1[32]; const int n1 = anchor::function_call_targets(A.cm, s12, t1, 32);
            for (int i = 0; i < n1 && !nameRead; ++i) {
                const uint8_t* t2[32]; const int n2 = anchor::function_call_targets(A.cm, t1[i], t2, 32);
                for (int j = 0; j < n2 && !nameRead; ++j)
                    if (anchor::function_has_bytes(A.cm, t2[j], kReadName, sizeof kReadName)) nameRead = true;
            }
        }
        if (nameRead) {
            g_origFnInvoke = reinterpret_cast<FnInvokeFn>(s12);
            void* o12 = s12;
            A.timeGate = swap_slot(A.fnVft, kSlotFnInvoke, reinterpret_cast<void*>(&hooked_fn_invoke), &o12);
        }
        logf("WO137-BUILD time gate %s (C_Function %s, method-name read %s)", A.timeGate ? "ARMED (off until a joiner's Config)" : "off",
             A.fnVft ? "found" : "NOT found", nameRead ? "verified" : "NOT verified");
    }
    A.read = A.findNode && A.questVft && A.qObjectives && A.qLevel && A.qLast && A.oName && A.oType && A.oLog &&
             A.oOrder && A.oLast && A.resolver && A.release;
    A.hud = A.questModuleVft && A.setHud && A.getHud && A.hudQuestVft;
    char d33[96]{}, d42[96]{}, dt[96]{};
    anchor::describe(s33, d33, sizeof d33); anchor::describe(s42, d42, sizeof d42); anchor::describe(A.inTrigTrigger, dt, sizeof dt);
    logf("WO137-BUILD detector=%s (%s; slot33 %s, slot42 %s) apply=%s (trigger %s) read=%s hud=%s variant=%s",
         A.detector ? "ARMED" : "off", why, d33, d42, A.apply ? "armed" : "off", dt, A.read ? "armed" : "off",
         A.hud ? "armed" : "off", (A.vToInt && A.vGetType && A.tGetName && A.vIsValid) ? "ok" : "MISSING");
}

void tick() {
    if (!g_mainTid) g_mainTid = GetCurrentThreadId();
    static unsigned frame = 0;
    if ((++frame % 30) == 0) research_watch();
    drain();
}

void on_disconnect() {
    g_send_on = false;
    g_timeGate = false;
    g_punishGate = false;   // WO-139
    g_hold = false;
    g_detect = g_logAll.load();
    g_role = 0;
    std::lock_guard<std::mutex> lk(g_qm);
    g_queue.clear();
    main_thread::post([] { char why[160]; hud_on(false, why, sizeof why); });
}

uint8_t handle(const uint8_t* req, size_t n, uint8_t* out, size_t cap, size_t* outN) {
    *outN = 0;
    if (n < 1) return kRBadRequest;
    const uint8_t op = req[0];
    switch (op) {
    case kOpConfig: {
        if (n < 3) return kRBadRequest;
        const bool on = req[1] != 0;
        g_role = req[2];
        const uint8_t cfg = n >= 4 ? req[3] : 0;
        if (on && !A.detector) return kRUnarmed;
        g_send_on = on;
        g_detect = on || g_logAll.load();
        const bool tg = wo137rules::time_gate_on(on, g_role, cfg & kCfgTimeGate, A.timeGate);
        g_timeGate = tg;
        if (!on) { std::lock_guard<std::mutex> lk(g_qm); g_queue.clear(); }
        // the agent re-sends its config every 10 s: logged on a change only
        static int lastKey = -1;
        const int key = wo137rules::config_key(on, g_role, tg);
        if (key != lastKey) {
            lastKey = key;
            logf("WO137-CONFIG detect=%s role=%u time_gate=%s", on ? "on" : "off", g_role, tg ? "on" : "off");
        }
        return kROk;
    }
    case kOpHold: {
        if (n < 2) return kRBadRequest;
        g_hold = req[1] != 0;
        logf("WO137-HOLD %s (queued %zu)", g_hold.load() ? "on" : "off", g_queue.size());
        return kROk;
    }
    case kOpApply: {
        if (n < 1 + 4 + 2) return kRBadRequest;
        uint32_t tag; std::memcpy(&tag, req + 1, 4);
        const uint16_t pl = get_u16(req + 5);
        if (pl == 0 || pl > kMaxPath || n < 7u + pl + 1u) return kRBadRequest;
        char path[kMaxPath + 1]{}; std::memcpy(path, req + 7, pl);
        const uint8_t portLen = req[7 + pl];
        if (portLen == 0 || portLen > 100 || n < 8u + pl + portLen) return kRBadRequest;
        char port[128]{}; std::memcpy(port, req + 8 + pl, portLen);
        if (std::strncmp(path, "Barbora.", 8) != 0) return kRBadRequest;   // quests only
        Val b{}, a{};
        const Applied r = apply(path, port, &b, &a);
        logf("WO137-APPLY tag=%u %s :: %s -> %s (from %d to %d, %s)", tag, path, port, applied_text(r), b.i, a.i, a.type);
        if (cap < 12 + 48) return kRFailed;
        size_t o = 0;
        out[o++] = static_cast<uint8_t>(r);
        out[o++] = b.ok; std::memcpy(out + o, &b.i, 4); o += 4;
        out[o++] = a.ok; std::memcpy(out + o, &a.i, 4); o += 4;
        const size_t tl = std::strlen(a.type[0] ? a.type : b.type);
        out[o++] = static_cast<uint8_t>(tl); std::memcpy(out + o, a.type[0] ? a.type : b.type, tl); o += tl;
        *outN = o;
        return kROk;
    }
    case kOpSetValue: {   // WO-164 T6: [tag:4][pathLen:2][path][value:4] -> the op 2 reply shape
        if (n < 1 + 4 + 2) return kRBadRequest;
        uint32_t tag; std::memcpy(&tag, req + 1, 4);
        const uint16_t pl = get_u16(req + 5);
        if (pl == 0 || pl > kMaxPath || n != 7u + pl + 4u) return kRBadRequest;
        char path[kMaxPath + 1]{}; std::memcpy(path, req + 7, pl);
        int32_t v; std::memcpy(&v, req + 7 + pl, 4);
        if (std::strncmp(path, "Barbora.", 8) != 0) return kRBadRequest;   // quests only
        Val b{}, a{};
        const Applied r = set_value(path, v, &b, &a);
        logf("WO164-QSET tag=%u %s = %d -> %s (from %d to %d, %s) thread=%lu", tag, path, v, applied_text(r), b.i, a.i,
             a.type[0] ? a.type : b.type, static_cast<unsigned long>(GetCurrentThreadId()));
        if (cap < 12 + 48) return kRFailed;
        size_t o = 0;
        out[o++] = static_cast<uint8_t>(r);
        out[o++] = b.ok; std::memcpy(out + o, &b.i, 4); o += 4;
        out[o++] = a.ok; std::memcpy(out + o, &a.i, 4); o += 4;
        const char* ty = a.type[0] ? a.type : b.type;
        const size_t tl = std::strlen(ty);
        out[o++] = static_cast<uint8_t>(tl); std::memcpy(out + o, ty, tl); o += tl;
        *outN = o;
        return kROk;
    }
    case kOpReadState:
    case kOpReadStateTyped: {   // WO-147: typed = each entry carries its value's type name (the correction port)
        const bool typed = op == kOpReadStateTyped;
        if (n < 2) return kRBadRequest;
        const uint8_t count = req[1];
        size_t i = 2, o = 1;
        if (cap < 1) return kRFailed;
        out[0] = 0;
        for (uint8_t k = 0; k < count; ++k) {
            if (i + 2 > n) return kRBadRequest;
            const uint16_t pl = get_u16(req + i); i += 2;
            if (pl == 0 || pl > kMaxPath || i + pl > n) return kRBadRequest;
            char path[kMaxPath + 1]{}; std::memcpy(path, req + i, pl); i += pl;
            if (o + 7 + (typed ? 1 + 63 : 0) > cap) break;
            void* node = std::strncmp(path, "Barbora.", 8) == 0 ? find_node(path) : nullptr;
            Val v{};
            int rt = -1;
            const bool st = node && is_state(node);
            if (st) { decode(static_cast<char*>(node) + kOffStateValue, &v); rt = node_runtime(node); }
            out[o++] = st ? 1 : 0;
            out[o++] = static_cast<uint8_t>(rt & 0xFF);
            out[o++] = v.ok;
            std::memcpy(out + o, &v.i, 4); o += 4;
            if (typed) {
                size_t tl = std::strlen(v.type);
                if (tl > 63) tl = 63;
                out[o++] = static_cast<uint8_t>(tl);
                std::memcpy(out + o, v.type, tl); o += tl;
            }
            call_release(node);
            out[0]++;
        }
        *outN = o;
        return kROk;
    }
    case kOpReadQuest: {
        if (n < 3) return kRBadRequest;
        const uint16_t pl = get_u16(req + 1);
        if (pl == 0 || pl > kMaxPath || n < 3u + pl) return kRBadRequest;
        char path[kMaxPath + 1]{}; std::memcpy(path, req + 3, pl);
        int level = -1; int64_t last = 0; std::vector<ObjRow> rows; char cls[96]{};
        const bool ok = read_quest(path, &level, &last, &rows, cls, sizeof cls);
        size_t o = 0;
        if (cap < 6) return kRFailed;
        out[o++] = ok ? 1 : 0;
        std::memcpy(out + o, &level, 4); o += 4;
        const size_t countAt = o++;
        uint8_t count = 0;
        for (const auto& r : rows) {
            const size_t nl = std::strlen(r.name);
            if (o + 1 + nl + 1 + 1 + 2 + 8 > cap || count == 255) break;
            out[o++] = static_cast<uint8_t>(nl); std::memcpy(out + o, r.name, nl); o += nl;
            out[o++] = static_cast<uint8_t>(r.type);
            out[o++] = static_cast<uint8_t>(r.log);
            put_u16(out + o, static_cast<uint16_t>(r.order)); o += 2;
            std::memcpy(out + o, &r.last, 8); o += 8;
            ++count;
        }
        out[countAt] = count;
        *outN = o;
        return kROk;
    }
    case kOpHud: {
        if (n < 2) return kRBadRequest;
        char why[160]{};
        const bool ok = hud_on(req[1] != 0, why, sizeof why);
        logf("WO137-HUD %s -- %s", req[1] ? "on" : "off", why);
        return ok ? kROk : kRUnarmed;
    }
    case kOpStatus: {
        char t[900];
        status_text(t, sizeof t);
        const size_t m = std::strlen(t) < cap ? std::strlen(t) : cap;
        std::memcpy(out, t, m);
        *outN = m;
        return kROk;
    }
    default:
        return kRBadRequest;
    }
}

// WO-139 (wo137.h): the punishment scope of the same C_Function hook.
bool set_punish_gate(bool on) {
    if (on && !A.timeGate) return false;
    const bool was = g_punishGate.exchange(on);
    if (was != on) logf("WO139-CONFIG punishment time gate %s", on ? "ON (a session with a partner: the punishment moves no clock)" : "off");
    return true;
}
bool punish_gate_armed() { return A.timeGate; }
uint32_t punish_skipped() { return c_punishSkipped.load(); }

} // namespace kcdmp::wo137
