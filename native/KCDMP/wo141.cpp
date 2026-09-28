#include "wo141.h"
#include "wo141_rules.h"
#include "anchors.h"
#include "combat_swing.h"
#include "engine.h"
#include "local_state.h"
#include "log.h"
#include "npc_drive.h"
#include "pe_exports.h"
#include "respawn_actions.h"
#include "wo138.h"

#include <windows.h>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

namespace kcdmp::wo141 {

namespace R = kcdmp::wo141rules;

namespace {

// ---------------------------------------------------------------------------
// SEH-isolated helpers (no destructible locals in any __try frame).
// ---------------------------------------------------------------------------
bool rd(const void* base, size_t off, void** out) {
    __try { *out = *reinterpret_cast<void* const*>(static_cast<const char*>(base) + off); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool rd64(const void* base, size_t off, uint64_t* out) {
    __try { *out = *reinterpret_cast<const uint64_t*>(static_cast<const char*>(base) + off); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool rd32(const void* base, size_t off, uint32_t* out) {
    __try { *out = *reinterpret_cast<const uint32_t*>(static_cast<const char*>(base) + off); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool rd8(const void* base, size_t off, uint8_t* out) {
    __try { *out = *(static_cast<const uint8_t*>(base) + off); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool rdstr(const char* p, char* out, size_t n) {
    __try {
        size_t i = 0;
        for (; i + 1 < n && p[i]; ++i) out[i] = (p[i] >= 32 && p[i] < 127) ? p[i] : '?';
        out[i] = 0;
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) { out[0] = 0; return false; }
}
void* vslot(void* obj, size_t off) {
    void* vt = nullptr; void* fn = nullptr;
    if (!obj || !rd(obj, 0, &vt) || !vt || !rd(vt, off, &fn)) return nullptr;
    return fn;
}
bool is_a(void* obj, void* const* vft) {
    void* vp = nullptr;
    return obj && vft && rd(obj, 0, &vp) && vp == static_cast<const void*>(vft);
}
template <typename R, typename... A>
bool vcall(void* obj, size_t off, R* out, A... a) {
    void* fn = vslot(obj, off);
    if (!fn) return false;
    __try { *out = reinterpret_cast<R (*)(void*, A...)>(fn)(obj, a...); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
template <typename R, typename... A>
bool fcall(void* fn, R* out, A... a) {
    if (!fn) return false;
    __try { *out = reinterpret_cast<R (*)(A...)>(fn)(a...); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
template <typename... A>
bool fcall_void(void* fn, A... a) {
    if (!fn) return false;
    __try { reinterpret_cast<void (*)(A...)>(fn)(a...); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool ilock_add(void* p, long d) {
    __try { InterlockedAdd(static_cast<volatile long*>(p), d); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
long ilock_add_get(void* p, long d, bool* ok) {
    __try { *ok = true; return InterlockedAdd(static_cast<volatile long*>(p), d); }
    __except (EXCEPTION_EXECUTE_HANDLER) { *ok = false; return 1; }
}
bool wr32(void* base, size_t off, uint32_t v) {
    __try { *reinterpret_cast<uint32_t*>(static_cast<char*>(base) + off) = v; return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool wr64(void* base, size_t off, uint64_t v) {
    __try { *reinterpret_cast<uint64_t*>(static_cast<char*>(base) + off) = v; return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool wr8(void* base, size_t off, uint8_t v) {
    __try { *(static_cast<uint8_t*>(base) + off) = v; return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool wr_slot(void* s, void* p, void* ctrl) {
    __try {
        *reinterpret_cast<void**>(s) = p;
        *reinterpret_cast<void**>(static_cast<char*>(s) + 8) = ctrl;
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

void where(const void* p, char* out, size_t n) {
    HMODULE m = nullptr;
    if (!p || !GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                                  static_cast<LPCSTR>(p), &m) || !m) {
        _snprintf_s(out, n, _TRUNCATE, "%p", p);
        return;
    }
    char path[MAX_PATH]{};
    GetModuleFileNameA(m, path, MAX_PATH);
    const char* base = std::strrchr(path, '\\');
    _snprintf_s(out, n, _TRUNCATE, "%s+0x%llX", base ? base + 1 : path,
                static_cast<unsigned long long>(static_cast<const char*>(p) - reinterpret_cast<const char*>(m)));
}
void vwhere(void* obj, char* out, size_t n) {
    void* vt = nullptr;
    if (!obj || !rd(obj, 0, &vt)) { _snprintf_s(out, n, _TRUNCATE, "-"); return; }
    where(vt, out, n);
}
std::string lower(const char* s) {
    std::string r(s ? s : "");
    for (auto& c : r) if (c >= 'A' && c <= 'Z') c = static_cast<char>(c - 'A' + 'a');
    return r;
}

// ---------------------------------------------------------------------------
// Anchors. Fail closed per piece: WO141-BUILD says what armed.
//   * the context:      RTTI .?AVC_NPCContext@NPCState@xgenaimodule@wh@@ (its vftable
//                       is checked on every lookup)
//   * the manager:      XGenAI +0x2E52F08, confirmed by a RIP-relative reference from
//                       C_NPCStateDebug::ResetCurrentStateElementCommand (its string)
//   * the WUID service: XGenAI +0x2E55FC8, confirmed by C_ScriptBindXGenAIModule::
//                       GetEntityByWUID's reference to it
//   * the unstances:    XGenAI +0x2E41928 (the database vector), confirmed by
//                       C_UnstanceElement::DebugInitFromString's reference to it
//   * the placement:    C_NPCContext::ExecuteStateChangeIntoLoadedState (its string)
//   * the loaded state: RTTI C_NPCRequiredState (slot 7 = its Clear)
//   * the elements:     RTTI C_StanceElement / C_StanceElementRequired / C_UnstanceElement
//                       / C_MinigameElement; rttr get_by_name / create / ~variant exports
//   * logs only:        the state's text (+0x1898330) and the stance names (+0xD62000),
//                       each by its exact prologue
// ---------------------------------------------------------------------------
HMODULE g_xg = nullptr;
void* const* g_vftCtx = nullptr;
void* const* g_vftStance = nullptr;
void* const* g_vftStanceReq = nullptr;
void* const* g_vftUnstance = nullptr;
void* const* g_vftMinigame = nullptr;
void* const* g_vftRequired = nullptr;
void* g_execLoaded = nullptr;
void* g_clearSearch = nullptr;
void* g_stateToString = nullptr;
void* g_stanceName = nullptr;
void* g_minigameName = nullptr;   // research: const char* (uint8 type), XGenAI +0x5B7400
void** g_npcMgrSlot = nullptr;
void** g_wuidSvcSlot = nullptr;
const char* g_unstDb = nullptr;
bool g_readArmed = false, g_applyArmed = false;

constexpr size_t kRvaNpcMgr      = 0x2E52F08;
constexpr size_t kRvaWuidSvc     = 0x2E55FC8;
constexpr size_t kRvaUnstanceDb  = 0x2E41928;   // std::vector<S_Unstance>: begin, end; 0xB0 per entry, name at +0
constexpr size_t kUnstanceStride = 0xB0;
constexpr size_t kRvaStateToString = 0x1898330;
constexpr size_t kRvaStanceName    = 0xD62000;
const uint8_t kProStateToString[] = {0x4C,0x8B,0xDC,0x55,0x57,0x41,0x57,0x48,0x81,0xEC,0x70,0x01,0x00,0x00};
const uint8_t kProStanceName[]    = {0x40,0x53,0x48,0x83,0xEC,0x20,0x48,0x63,0xD9};

constexpr size_t kMgrNpcById  = 0x20;    // mgr->vtbl[0x20](const uint32* entityId) -> the body's XGenAI object
constexpr size_t kObjCtx      = 0x9C0;   // XGenAI object + 0x9C0 = C_NPCContext
constexpr size_t kCtxCurrent  = 0x90;    // C_NPCCurrentState
constexpr size_t kCtxLoaded   = 0x180;   // C_NPCRequiredState (the loaded state)
constexpr size_t kCtxPending  = 0x5E8;   // the pending change request id (-1 none)
constexpr size_t kStateElemsB = 0x10;    // std::vector<shared_ptr<I_Element>> begin / end
constexpr size_t kStateElemsE = 0x18;
constexpr size_t kStateDirty  = 0x30;
constexpr size_t kStateClear  = 0x38;    // C_NPCStateBase slot 7: clear (the stance values kept)
constexpr size_t kStateAdded  = 0x50;    // slot 10: an element was added
constexpr size_t kSvcByWuid   = 0x10;    // svc->vtbl[0x10](const uint64* wuid) -> object; object->vtbl[0x10]() -> IEntity*
constexpr size_t kElId        = 0x28;
constexpr size_t kElObj       = 0x30;
constexpr size_t kElSlot      = 0x38;

bool prologue_ok(void* fn, const uint8_t* want, size_t n) {
    for (size_t i = 0; i < n; ++i) { uint8_t b = 0; if (!rd8(fn, i, &b) || b != want[i]) return false; }
    return true;
}

// rttr (CrySystem.dll exports).
struct RType { void* data; };
struct RVariant { uint8_t data[16]; void* policy; };
struct RArgVec { void* b; void* e; void* c; };      // std::vector<rttr::argument>, empty
using GetByNameFn   = void* (*)(RType* ret, const std::string_view* name);
using TypeCreateFn  = void* (*)(const RType* self, RVariant* ret, RArgVec* args);
using VariantDtorFn = void (*)(RVariant* self);
GetByNameFn   g_getByName = nullptr;
TypeCreateFn  g_typeCreate = nullptr;
VariantDtorFn g_variantDtor = nullptr;
RType g_tStanceReq{}, g_tUnstance{};

bool bind_type(const char* name, RType* out) {
    std::string_view nm(name);
    void* r = nullptr;
    return fcall(reinterpret_cast<void*>(g_getByName), &r, out, static_cast<const std::string_view*>(&nm)) && out->data;
}

struct SharedPtr { void* p = nullptr; void* ctrl = nullptr; };

// A new element of a reflected class: the variant holds std::shared_ptr<T>
// inline (policy as_std_shared_ptr); one reference is taken for the caller.
bool sp_create(const RType& t, void* const* wantVft, SharedPtr* out) {
    *out = {};
    if (!g_typeCreate || !g_variantDtor || !t.data) return false;
    RVariant v{}; RArgVec args{};
    void* r = nullptr;
    if (!fcall(reinterpret_cast<void*>(g_typeCreate), &r, &t, &v, &args) || !v.policy) return false;
    uint64_t d0 = 0, d1 = 0;
    std::memcpy(&d0, v.data, 8); std::memcpy(&d1, v.data + 8, 8);
    SharedPtr sp{reinterpret_cast<void*>(d0), reinterpret_cast<void*>(d1)};
    const bool ok = sp.p && sp.ctrl && is_a(sp.p, wantVft) && ilock_add(static_cast<char*>(sp.ctrl) + 8, 1);
    fcall_void(reinterpret_cast<void*>(g_variantDtor), &v);
    if (!ok) return false;
    *out = sp;
    return true;
}

void sp_release(void* ctrl) {
    if (!ctrl) return;
    bool ok = false;
    if (ilock_add_get(static_cast<char*>(ctrl) + 8, -1, &ok) != 0 || !ok) return;
    fcall_void(vslot(ctrl, 0x00), ctrl);                                   // _Destroy
    if (ilock_add_get(static_cast<char*>(ctrl) + 0xC, -1, &ok) == 0 && ok)
        fcall_void(vslot(ctrl, 0x08), ctrl);                               // _Delete_this
}

// Put `sp` into a state's fixed slot (the reference handed over), releasing what
// was there -- C_NPCStateBase::DebugAddElement's own insertion. Null empties it.
bool state_set_slot(void* state, unsigned slot, SharedPtr sp) {
    void* b = nullptr; void* e = nullptr;
    if (slot > 5 || !rd(state, kStateElemsB, &b) || !rd(state, kStateElemsE, &e) || !b) { sp_release(sp.ctrl); return false; }
    if (static_cast<size_t>(static_cast<char*>(e) - static_cast<char*>(b)) / 16 <= slot) { sp_release(sp.ctrl); return false; }
    char* s = static_cast<char*>(b) + slot * 16;
    void* oldCtrl = nullptr; rd(s, 8, &oldCtrl);
    if (!wr_slot(s, sp.p, sp.ctrl)) { sp_release(sp.ctrl); return false; }
    wr8(state, kStateDirty, 0);
    sp_release(oldCtrl);
    if (sp.p) fcall_void(vslot(state, kStateAdded), state, sp.p);
    return true;
}

void* entity_by_name(const char* name) {
    void* es = engine::entity_system(); void* e = nullptr;
    if (!es || !name || !*name || !vcall(es, 0x80, &e, name)) return nullptr;   // IEntitySystem::FindEntityByName
    return e;
}

void* ctx_of_eid(uint32_t eid) {
    if (!g_npcMgrSlot || !g_vftCtx || !eid) return nullptr;
    void* mgr = nullptr;
    if (!rd(g_npcMgrSlot, 0, &mgr) || !mgr) return nullptr;
    void* o = nullptr;
    if (!vcall(mgr, kMgrNpcById, &o, static_cast<const uint32_t*>(&eid)) || !o) return nullptr;
    void* ctx = static_cast<char*>(o) + kObjCtx;
    return is_a(ctx, g_vftCtx) ? ctx : nullptr;
}

bool wuid_valid(uint64_t w) { return (w & 0x00FFFFFFFFFFFFFFull) != 0 && (w & 0x00FFFFFFFFFFFFFFull) != 0x00FFFFFFFFFFFFFFull; }

void* entity_of_wuid(uint64_t w) {
    if (!g_wuidSvcSlot || !wuid_valid(w)) return nullptr;
    void* svc = nullptr; void* o = nullptr; void* ent = nullptr;
    if (!rd(g_wuidSvcSlot, 0, &svc) || !svc) return nullptr;
    if (!vcall(svc, kSvcByWuid, &o, static_cast<const uint64_t*>(&w)) || !o) return nullptr;
    if (!vcall(o, 0x10, &ent)) return nullptr;
    return ent;
}
uint64_t guid_of_wuid(uint64_t w) { void* e = entity_of_wuid(w); return e ? engine::entity_guid(e) : 0; }
uint64_t wuid_of_guid(uint64_t g) { void* e = g ? engine::entity_by_guid(g) : nullptr; return e ? actions::entity_wuid(e) : 0; }

const char* stance_label(int id) {
    const char* s = nullptr;
    if (!g_stanceName || !fcall(g_stanceName, &s, id) || !s) return R::stance_name(static_cast<uint8_t>(id));
    return s;
}

const char* unstance_name(uint16_t id, char* buf, size_t n) {
    buf[0] = 0;
    if (!g_unstDb || id == R::kNoUnstance) return buf;
    void* b = nullptr; void* e = nullptr;
    if (!rd(g_unstDb, 0, &b) || !rd(g_unstDb, 8, &e) || !b) return buf;
    const size_t cnt = static_cast<size_t>(static_cast<char*>(e) - static_cast<char*>(b)) / kUnstanceStride;
    if (id >= cnt) return buf;
    void* np = nullptr;
    if (rd(static_cast<char*>(b) + id * kUnstanceStride, 0, &np) && np) rdstr(static_cast<const char*>(np), buf, n);
    return buf;
}
int unstance_id(const char* name) {
    if (!g_unstDb) return -1;
    void* b = nullptr; void* e = nullptr;
    if (!rd(g_unstDb, 0, &b) || !rd(g_unstDb, 8, &e) || !b) return -1;
    const size_t cnt = static_cast<size_t>(static_cast<char*>(e) - static_cast<char*>(b)) / kUnstanceStride;
    for (size_t i = 0; i < cnt; ++i) {
        void* np = nullptr; char nb[96];
        if (!rd(static_cast<char*>(b) + i * kUnstanceStride, 0, &np) || !np) continue;
        rdstr(static_cast<const char*>(np), nb, sizeof nb);
        if (!_stricmp(nb, name)) return static_cast<int>(i);
    }
    return -1;
}

// The game's own text of a state (logs; the CryString it writes is leaked, ~100 B).
std::string state_text(void* state) {
    if (!g_stateToString || !state) return "";
    void* s = nullptr; void* r = nullptr;
    if (!fcall(g_stateToString, &r, state, static_cast<void**>(&s), static_cast<uint8_t>(0)) || !s) return "<fault>";
    char buf[1400];
    rdstr(static_cast<const char*>(s), buf, sizeof buf);
    return buf;
}

// ---------------------------------------------------------------------------
// Read: the current state's slots -> an Activity (objects as WUIDs), then GUIDs.
// ---------------------------------------------------------------------------
struct Raw { uint8_t stance = 0, cart = 0; uint64_t sObj = 0; uint16_t unst = R::kNoUnstance; uint64_t uObj = 0;
             uint8_t mg = R::kNoMinigame; uint64_t mObj = 0; };

bool read_raw(void* ctx, Raw* out) {
    *out = {};
    void* st = static_cast<char*>(ctx) + kCtxCurrent;
    void* b = nullptr; void* e = nullptr;
    if (!rd(st, kStateElemsB, &b) || !rd(st, kStateElemsE, &e) || !b) return false;
    const size_t n = static_cast<size_t>(static_cast<char*>(e) - static_cast<char*>(b)) / 16;
    if (n < 6 || n > 256) return false;
    void* el = nullptr;
    if (rd(b, 0 * 16, &el) && el && (is_a(el, g_vftStance) || is_a(el, g_vftStanceReq))) {
        uint32_t s = 0; uint8_t c = 0;
        rd32(el, kElId, &s); rd64(el, kElObj, &out->sObj); rd8(el, kElSlot, &c);
        out->stance = static_cast<uint8_t>(s <= 7 ? s : 0); out->cart = c;
    }
    el = nullptr;
    if (rd(b, 3 * 16, &el) && el && is_a(el, g_vftUnstance)) {
        uint32_t u = 0;
        rd32(el, kElId, &u); rd64(el, kElObj, &out->uObj);
        out->unst = static_cast<uint16_t>(u < 0xFFFF ? u : R::kNoUnstance);
    }
    el = nullptr;
    if (rd(b, 5 * 16, &el) && el && is_a(el, g_vftMinigame)) {
        rd8(el, kElId, &out->mg); rd64(el, kElObj, &out->mObj);
    }
    return true;
}

R::Activity to_activity(const Raw& r) {
    R::Activity a;
    a.stance = r.stance; a.cart = r.cart;
    a.stanceObj = wuid_valid(r.sObj) ? guid_of_wuid(r.sObj) : 0;
    a.unstance = r.unst;
    a.unstanceObj = wuid_valid(r.uObj) ? guid_of_wuid(r.uObj) : 0;
    a.minigame = r.mg;
    a.minigameObj = wuid_valid(r.mObj) ? guid_of_wuid(r.mObj) : 0;
    return R::normalised(a);
}

bool read_activity(uint32_t eid, R::Activity* out) {
    void* ctx = ctx_of_eid(eid);
    Raw r;
    if (!ctx || !read_raw(ctx, &r)) return false;
    *out = to_activity(r);
    return true;
}

std::string describe(const R::Activity& a) {
    char u[96]; char buf[320];
    _snprintf_s(buf, sizeof buf, _TRUNCATE, "stance=%s obj=%016llX%s unstance=%s obj=%016llX minigame=%u owns=%d",
                R::stance_name(a.stance), static_cast<unsigned long long>(a.stanceObj), a.cart ? " cart" : "",
                a.unstance == R::kNoUnstance ? "-" : unstance_name(a.unstance, u, sizeof u),
                static_cast<unsigned long long>(a.unstanceObj), a.minigame, R::owns_position(a) ? 1 : 0);
    return buf;
}

// ---------------------------------------------------------------------------
// Apply: the loaded state := the activity; the game's own post-load placement.
// ---------------------------------------------------------------------------
enum Applied : uint8_t { kApOk = 0, kApNoBody = 1, kApNoObject = 2, kApBuild = 3, kApExec = 4, kApNotArmed = 5 };
const char* applied_name(uint8_t r) {
    switch (r) { case kApOk: return "ok"; case kApNoBody: return "no-body"; case kApNoObject: return "object-not-here";
                 case kApBuild: return "build-failed"; case kApExec: return "execute-faulted"; case kApNotArmed: return "not-armed"; }
    return "?";
}

uint8_t apply_now(uint32_t eid, const R::Activity& want, uint8_t* execResult) {
    *execResult = 0;
    if (!g_applyArmed) return kApNotArmed;
    void* ctx = ctx_of_eid(eid);
    if (!ctx) return kApNoBody;
    void* loaded = static_cast<char*>(ctx) + kCtxLoaded;
    if (!is_a(loaded, g_vftRequired)) return kApBuild;
    const R::Activity a = R::normalised(want);
    uint64_t sW = 0, uW = 0;
    if (a.stance && a.stanceObj) { sW = wuid_of_guid(a.stanceObj); if (!sW) return kApNoObject; }
    if (a.unstance != R::kNoUnstance && a.unstanceObj) { uW = wuid_of_guid(a.unstanceObj); if (!uW) return kApNoObject; }
    SharedPtr s{}, u{};
    if (a.stance && !sp_create(g_tStanceReq, g_vftStanceReq, &s)) return kApBuild;
    if (a.unstance != R::kNoUnstance && !sp_create(g_tUnstance, g_vftUnstance, &u)) { sp_release(s.ctrl); return kApBuild; }
    if (s.p) { wr32(s.p, kElId, a.stance); wr64(s.p, kElObj, sW); wr8(s.p, kElSlot, a.cart); }
    if (u.p) { wr32(u.p, kElId, a.unstance); wr64(u.p, kElObj, uW); }
    // the save's leftovers go (contexts, links, buffs, equipment of whatever the
    // body was loaded in); the stance and unstance are ours
    fcall_void(vslot(loaded, kStateClear), loaded);
    const bool ok = state_set_slot(loaded, 0, s) && state_set_slot(loaded, 3, u);
    if (!ok) return kApBuild;
    uint8_t res = 0;
    if (!fcall(g_execLoaded, &res, ctx)) return kApExec;
    *execResult = res;
    return kApOk;
}

// ---------------------------------------------------------------------------
// Live state
// ---------------------------------------------------------------------------
std::atomic<bool> g_captureNpcs{false}, g_capturePlayer{false}, g_applyOn{false};
std::atomic<uint16_t> g_periodMs{250};
FrameFn g_frame = nullptr;

struct Sent { R::Activity a; double at = -1e9; bool have = false; };
std::unordered_map<std::string, Sent> g_sent;     // host: per lower name (the player under "")
bool g_resync = false;

struct Desired {
    std::string name;
    R::Activity a;
    uint32_t eid = 0;
    double resolveAt = -1e9;
    R::Pace pace;
    bool held = false;
    bool matched = false;
    uint64_t applies = 0;
};
std::unordered_map<std::string, Desired> g_desired;

std::atomic<uint64_t> c_rowsSent{0}, c_applies{0}, c_applyOk{0}, c_applyFail{0}, c_leaves{0}, c_reads{0}, c_shows{0};

// op 8: a one-shot of the player's, shown as an NPC unstance (with_shown)
R::Activity g_shown;
double g_shownUntil = -1;
double g_lastTick = 0;

double now_s() { return npcdrive::now_s(); }

// 0xA7 frames: [count]{[kind][nameLen][name][30]}
std::vector<uint8_t> g_out;
uint8_t g_outCount = 0;
void flush_rows() {
    if (g_outCount && g_frame) {
        g_out[0] = g_outCount;
        g_frame(g_out.data(), static_cast<uint16_t>(g_out.size()));
        c_rowsSent.fetch_add(g_outCount);
    }
    g_out.assign(1, 0); g_outCount = 0;
}
void add_row(uint8_t kind, const std::string& name, const R::Activity& a) {
    if (g_out.empty()) g_out.assign(1, 0);
    const size_t n = name.size() > 63 ? 63 : name.size();
    if (g_out.size() + 2 + n + R::kWireBytes > 1000 || g_outCount == 255) flush_rows();
    g_out.push_back(kind);
    g_out.push_back(static_cast<uint8_t>(n));
    g_out.insert(g_out.end(), name.begin(), name.begin() + n);
    uint8_t w[R::kWireBytes]; R::encode(a, w);
    g_out.insert(g_out.end(), w, w + R::kWireBytes);
    ++g_outCount;
}

void capture_one(uint8_t kind, const char* name, uint32_t eid, double now) {
    R::Activity a;
    if (!read_activity(eid, &a)) return;
    c_reads.fetch_add(1);
    if (kind == kKindPlayer) a = R::with_shown(a, g_shown, now < g_shownUntil);
    Sent& s = g_sent[kind == kKindPlayer ? std::string() : lower(name)];
    const bool changed = !s.have || !R::same(s.a, a);
    if (!R::send_due(changed || g_resync, R::none(a), now - s.at)) return;
    if (changed) logf("WO141-CAPTURE %s %s: %s", kind == kKindPlayer ? "player" : "npc", kind == kKindPlayer ? "(local)" : name, describe(a).c_str());
    s.a = a; s.at = now; s.have = true;
    add_row(kind, kind == kKindPlayer ? std::string() : std::string(name), a);
}

struct CapCtx { double now; };
void capture_visit(const char* name, uint32_t eid, void* c) { capture_one(kKindNpc, name, eid, static_cast<CapCtx*>(c)->now); }

void capture_tick(double now) {
    if (!g_readArmed) return;
    const bool npcs = g_captureNpcs.load(), me = g_capturePlayer.load();
    if (!npcs && !me) return;
    g_out.assign(1, 0); g_outCount = 0;
    CapCtx c{now};
    if (npcs) wo138::for_each_tracked(&capture_visit, &c);
    if (me) capture_one(kKindPlayer, "", 0x7777, now);
    g_resync = false;
    flush_rows();
}

void reconcile_tick(double now) {
    if (!g_applyOn.load() || !g_applyArmed) return;
    for (auto it = g_desired.begin(); it != g_desired.end();) {
        Desired& d = it->second;
        void* ent = d.eid ? engine::entity_by_id(d.eid) : nullptr;
        if (d.eid && (!ent || lower(engine::entity_name(ent)) != it->first)) { if (d.held) npcdrive::set_activity_hold(d.eid, false); d.eid = 0; d.held = false; }
        if (!d.eid) {
            if (now - d.resolveAt < 2.0) { ++it; continue; }
            d.resolveAt = now;
            void* e = entity_by_name(d.name.c_str());
            d.eid = e ? engine::entity_id(e) : 0;
            if (!d.eid) { ++it; continue; }
        }
        const bool wantHold = R::owns_position(d.a);
        if (wantHold != d.held) { npcdrive::set_activity_hold(d.eid, wantHold); d.held = wantHold; }
        R::Activity cur;
        if (!read_activity(d.eid, &cur)) { ++it; continue; }
        if (R::same_body(cur, d.a)) {
            if (!d.matched) logf("WO141-APPLY %s in step: %s", d.name.c_str(), describe(d.a).c_str());
            d.matched = true; d.pace.misses = 0;
            if (R::none(d.a)) { if (d.held) npcdrive::set_activity_hold(d.eid, false); it = g_desired.erase(it); continue; }
            ++it; continue;
        }
        d.matched = false;
        if (now < d.pace.nextAt) { ++it; continue; }
        uint8_t res = 0;
        const uint8_t r = apply_now(d.eid, d.a, &res);
        c_applies.fetch_add(1); ++d.applies;
        (r == kApOk ? c_applyOk : c_applyFail).fetch_add(1);
        ++d.pace.misses;
        d.pace.nextAt = now + R::next_delay(d.pace.misses);
        R::Activity after; read_activity(d.eid, &after);
        logf("WO141-APPLY %s -> %s (exec %u, try %d): wanted %s; now %s", d.name.c_str(), applied_name(r), res, d.pace.misses,
             describe(d.a).c_str(), describe(after).c_str());
        ++it;
    }
}

// ---------------------------------------------------------------------------
// Research verbs (kcdmp-w141-<dll name>.txt beside kcd.log; absent = idle; what
// the file holds when the game starts is a baseline and never runs).
// ---------------------------------------------------------------------------
void dump_state(const char* who, const char* which, void* state) {
    void* b = nullptr; void* e = nullptr;
    if (!rd(state, kStateElemsB, &b) || !rd(state, kStateElemsE, &e)) { logf("WO141-R %s %s: unreadable", who, which); return; }
    logf("WO141-R %s %s: %zu slot(s) text=[%s]", who, which, static_cast<size_t>(static_cast<char*>(e) - static_cast<char*>(b)) / 16,
         state_text(state).c_str());
}
void cmd_state(const char* name) {
    void* ent = entity_by_name(name);
    void* ctx = ent ? ctx_of_eid(engine::entity_id(ent)) : nullptr;
    if (!ctx) { logf("WO141-R state %s: no context (entity %p)", name, ent); return; }
    uint32_t req = 0; rd32(ctx, kCtxPending, &req);
    R::Activity a; read_activity(engine::entity_id(ent), &a);
    logf("WO141-R state %s: pendingRequest=%d activity: %s", name, static_cast<int>(req), describe(a).c_str());
    dump_state(name, "current", static_cast<char*>(ctx) + kCtxCurrent);
    dump_state(name, "loaded", static_cast<char*>(ctx) + kCtxLoaded);
}
struct NearCtx { float p[3]; float r2; int n; };
bool near_visit(void* e, void* c) {
    auto* nc = static_cast<NearCtx*>(c);
    float q[3];
    if (!engine::entity_world_pos(e, q)) return false;
    const float dx = q[0] - nc->p[0], dy = q[1] - nc->p[1], dz = q[2] - nc->p[2];
    if (dx * dx + dy * dy + dz * dz > nc->r2) return false;
    const uint32_t eid = engine::entity_id(e);
    R::Activity a;
    if (!read_activity(eid, &a)) return false;
    char nb[80]; rdstr(engine::entity_name(e) ? engine::entity_name(e) : "?", nb, sizeof nb);
    logf("WO141-R near %s d=%.1f %s", nb, std::sqrt(dx * dx + dy * dy + dz * dz), describe(a).c_str());
    return ++nc->n >= 120;
}
void cmd_near(float r) {
    localstate::LocalState ls{};
    if (!localstate::read_local_state(&ls)) { logf("WO141-R near: no player position"); return; }
    NearCtx nc{{ls.x, ls.y, ls.z}, r * r, 0};
    engine::for_each_entity(&near_visit, &nc);
    logf("WO141-R near r=%.0f: %d body/bodies with a context", r, nc.n);
}
// mk <entity> stance <id> <object entity name> | unstance <name> <object entity name> | none
void cmd_mk(const char* name, const char* rest) {
    char kind[32]{}, a[128]{}, b[512]{};
    sscanf_s(rest, "%31s %127s %511[^\n]", kind, 32u, a, 128u, b, 512u);
    void* ent = entity_by_name(name);
    if (!ent) { logf("WO141-R mk %s: no entity", name); return; }
    R::Activity act;
    if (!std::strcmp(kind, "stance")) {
        act.stance = static_cast<uint8_t>(atoi(a));
        void* o = entity_by_name(b);
        act.stanceObj = o ? engine::entity_guid(o) : 0;
    } else if (!std::strcmp(kind, "unstance")) {
        const int id = unstance_id(a);
        act.unstance = static_cast<uint16_t>(id < 0 ? R::kNoUnstance : id);
        void* o = b[0] ? entity_by_name(b) : nullptr;
        act.unstanceObj = o ? engine::entity_guid(o) : 0;
    }
    uint8_t res = 0;
    const uint8_t r = apply_now(engine::entity_id(ent), act, &res);
    R::Activity now; read_activity(engine::entity_id(ent), &now);
    logf("WO141-R mk %s %s: %s (exec %u): wanted %s; now %s", name, kind, applied_name(r), res, describe(act).c_str(), describe(now).c_str());
}
void cmd_want(const char* name, const char* rest) {   // want <entity> <same as mk>: through the reconcile
    char kind[32]{}, a[128]{}, b[512]{};
    sscanf_s(rest, "%31s %127s %511[^\n]", kind, 32u, a, 128u, b, 512u);
    R::Activity act;
    if (!std::strcmp(kind, "stance")) { act.stance = static_cast<uint8_t>(atoi(a)); void* o = entity_by_name(b); act.stanceObj = o ? engine::entity_guid(o) : 0; }
    else if (!std::strcmp(kind, "unstance")) { const int id = unstance_id(a); act.unstance = static_cast<uint16_t>(id < 0 ? R::kNoUnstance : id);
                                               void* o = b[0] ? entity_by_name(b) : nullptr; act.unstanceObj = o ? engine::entity_guid(o) : 0; }
    Desired& d = g_desired[lower(name)];
    d.name = name; d.a = R::normalised(act); d.pace = {}; d.matched = false;
    logf("WO141-R want %s: %s", name, describe(d.a).c_str());
}

bool g_baseline = false;
std::string g_last;
void research_watch() {
    char cwd[MAX_PATH]{};
    if (!GetCurrentDirectoryA(MAX_PATH, cwd) || !cwd[0]) return;
    static char self[64] = {};
    if (!self[0]) {
        HMODULE me = nullptr; char p[MAX_PATH]{};
        GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           reinterpret_cast<LPCSTR>(&research_watch), &me);
        GetModuleFileNameA(me, p, MAX_PATH);
        const char* bs = std::strrchr(p, '\\'); bs = bs ? bs + 1 : p;
        _snprintf_s(self, sizeof self, _TRUNCATE, "%s", bs);
        if (char* dot = std::strrchr(self, '.')) *dot = 0;
    }
    std::string path = std::string(cwd) + (cwd[std::strlen(cwd) - 1] == '\\' ? "" : "\\") + "kcdmp-w141-" + self + ".txt";
    FILE* f = nullptr;
    if (fopen_s(&f, path.c_str(), "rb") != 0 || !f) { g_baseline = true; return; }
    std::string all; char buf[4096]; size_t got;
    while ((got = fread(buf, 1, sizeof buf, f)) > 0) all.append(buf, got);
    fclose(f);
    if (!g_baseline) { g_baseline = true; g_last = all; logf("WO141-RESEARCH %s present at start: baseline, not run", path.c_str()); return; }
    if (all == g_last) return;
    std::string fresh = all.size() > g_last.size() && all.compare(0, g_last.size(), g_last) == 0 ? all.substr(g_last.size()) : all;
    g_last = all;
    size_t pos = 0;
    while (pos < fresh.size()) {
        size_t nl = fresh.find('\n', pos);
        std::string line = fresh.substr(pos, nl == std::string::npos ? std::string::npos : nl - pos);
        pos = nl == std::string::npos ? fresh.size() : nl + 1;
        while (!line.empty() && (line.back() == '\r' || line.back() == ' ')) line.pop_back();
        if (line.empty() || line[0] == '#') continue;
        char verb[32]{}, a1[256]{}, rest[1024]{};
        const int k = sscanf_s(line.c_str(), "%31s %255s %1023[^\n]", verb, 32u, a1, 256u, rest, 1024u);
        logf("WO141-RESEARCH line: %s", line.c_str());
        if (k >= 2 && !std::strcmp(verb, "state")) cmd_state(a1);
        else if (k >= 2 && !std::strcmp(verb, "near")) cmd_near(static_cast<float>(atof(a1)));
        else if (k >= 3 && !std::strcmp(verb, "mk")) cmd_mk(a1, rest);
        else if (k >= 3 && !std::strcmp(verb, "want")) cmd_want(a1, rest);
        else if (!std::strcmp(verb, "applyon")) { g_applyOn = true; logf("WO141-R apply on (research)"); }
        else if (!std::strcmp(verb, "captureon")) { g_captureNpcs = true; g_capturePlayer = true; logf("WO141-R capture on (research)"); }
        else if (!std::strcmp(verb, "stances")) { for (int i = 0; i < 8; ++i) logf("WO141-R stance %d = %s", i, stance_label(i)); }
        else if (k >= 2 && !std::strcmp(verb, "unst")) logf("WO141-R unstance '%s' = %d", a1, unstance_id(a1));
        else if (k >= 3 && !std::strcmp(verb, "frag")) {   // frag <entity> <fragment spec>: the native action path (combat swings')
            void* fe = entity_by_name(a1);
            const int r = fe ? static_cast<int>(rttr::ghost_swing(engine::entity_id(fe), rest)) : -1;
            logf("WO141-R frag %s \"%s\": result %d", a1, rest, r);
        }
        else if (!std::strcmp(verb, "minigames")) {
            for (int i = 0; i < 14; ++i) { const char* s = nullptr; fcall(g_minigameName, &s, static_cast<uint8_t>(i)); logf("WO141-R minigame %d = %s", i, s ? s : "?"); }
        }
        else logf("WO141-RESEARCH unknown or incomplete: %s", line.c_str());
    }
}

std::string status_text() {
    char b[400];
    _snprintf_s(b, sizeof b, _TRUNCATE,
                "read %s apply %s | capture npcs=%d player=%d period=%ums | apply %s desired=%zu | rows %llu reads %llu applies %llu (ok %llu fail %llu) leaves %llu shows %llu",
                g_readArmed ? "armed" : "NOT ARMED", g_applyArmed ? "armed" : "NOT ARMED", g_captureNpcs.load() ? 1 : 0,
                g_capturePlayer.load() ? 1 : 0, g_periodMs.load(), g_applyOn.load() ? "on" : "off", g_desired.size(),
                static_cast<unsigned long long>(c_rowsSent.load()), static_cast<unsigned long long>(c_reads.load()),
                static_cast<unsigned long long>(c_applies.load()), static_cast<unsigned long long>(c_applyOk.load()),
                static_cast<unsigned long long>(c_applyFail.load()), static_cast<unsigned long long>(c_leaves.load()),
                static_cast<unsigned long long>(c_shows.load()));
    return b;
}

void set_desired(const std::string& name, const R::Activity& a) {
    Desired& d = g_desired[lower(name.c_str())];
    const bool changed = d.name.empty() || !R::same(d.a, a);
    d.name = name;
    if (changed) { d.a = R::normalised(a); d.pace = {}; d.matched = false; }
}

} // namespace

// ---------------------------------------------------------------------------

void install() {
    g_xg = GetModuleHandleA("XGenAIModule.dll");
    if (!g_xg) { logf("WO141-BUILD XGenAIModule not loaded -- activities NOT ARMED"); return; }
    char* base = reinterpret_cast<char*>(g_xg);
    g_vftCtx       = anchor::find_vftable(g_xg, ".?AVC_NPCContext@NPCState@xgenaimodule@wh@@");
    g_vftStance    = anchor::find_vftable(g_xg, ".?AVC_StanceElement@NPCState@xgenaimodule@wh@@");
    g_vftStanceReq = anchor::find_vftable(g_xg, ".?AVC_StanceElementRequired@NPCState@xgenaimodule@wh@@");
    g_vftUnstance  = anchor::find_vftable(g_xg, ".?AVC_UnstanceElement@NPCState@xgenaimodule@wh@@");
    g_vftMinigame  = anchor::find_vftable(g_xg, ".?AVC_MinigameElement@NPCState@xgenaimodule@wh@@");
    g_vftRequired  = anchor::find_vftable(g_xg, ".?AVC_NPCRequiredState@NPCState@xgenaimodule@wh@@");
    g_execLoaded   = const_cast<uint8_t*>(anchor::function_by_string(g_xg, "wh::xgenaimodule::NPCState::C_NPCContext::ExecuteStateChangeIntoLoadedState"));
    g_clearSearch  = const_cast<uint8_t*>(anchor::function_by_string(g_xg, "wh::xgenaimodule::NPCState::C_NPCContext::ClearCurrentSearchState"));
    const uint8_t* resetCmd = anchor::function_by_string(g_xg, "wh::xgenaimodule::NPCState::C_NPCStateDebug::ResetCurrentStateElementCommand");
    const uint8_t* byWuid   = anchor::function_by_string(g_xg, "wh::xgenaimodule::C_ScriptBindXGenAIModule::GetEntityByWUID");
    const uint8_t* unstInit = anchor::function_by_string(g_xg, "wh::xgenaimodule::NPCState::C_UnstanceElement::DebugInitFromString");
    void* mgr = base + kRvaNpcMgr; void* svc = base + kRvaWuidSvc; void* udb = base + kRvaUnstanceDb;
    const bool mgrOk = resetCmd && anchor::function_refs(g_xg, resetCmd, mgr);
    const bool svcOk = byWuid && anchor::function_refs(g_xg, byWuid, svc);
    const bool udbOk = unstInit && anchor::function_refs(g_xg, unstInit, udb);
    g_npcMgrSlot  = mgrOk ? static_cast<void**>(mgr) : nullptr;
    g_wuidSvcSlot = svcOk ? static_cast<void**>(svc) : nullptr;
    g_unstDb      = udbOk ? static_cast<const char*>(udb) : nullptr;
    void* ts = base + kRvaStateToString; void* sn = base + kRvaStanceName;
    g_stateToString = prologue_ok(ts, kProStateToString, sizeof kProStateToString) ? ts : nullptr;
    g_stanceName    = prologue_ok(sn, kProStanceName, sizeof kProStanceName) ? sn : nullptr;
    { static const uint8_t pro[] = {0x40,0x53,0x48,0x83,0xEC,0x20,0x0F,0xB6,0xD9}; void* mn = base + 0x5B7400;
      g_minigameName = prologue_ok(mn, pro, sizeof pro) ? mn : nullptr; }
    if (HMODULE cry = GetModuleHandleA("CrySystem.dll")) {
        const auto ex = module_exports(cry);
        g_getByName   = reinterpret_cast<GetByNameFn>(find_export(ex, "?get_by_name@type@rttr@@"));
        g_typeCreate  = reinterpret_cast<TypeCreateFn>(find_export(ex, "?create@type@rttr@@QEBA?AVvariant@2@V?$vector@Vargument@rttr@@"));
        g_variantDtor = reinterpret_cast<VariantDtorFn>(find_export(ex, "??1variant@rttr@@QEAA@XZ"));
    }
    const bool types = g_getByName && bind_type("wh::xgenaimodule::NPCState::StanceElementRequired", &g_tStanceReq) &&
                       bind_type("wh::xgenaimodule::NPCState::UnstanceElement", &g_tUnstance);
    g_readArmed  = g_vftCtx && g_npcMgrSlot && g_wuidSvcSlot && g_vftStance && g_vftUnstance && g_vftMinigame;
    g_applyArmed = g_readArmed && g_execLoaded && g_vftRequired && g_vftStanceReq && types && g_typeCreate && g_variantDtor;
    logf("WO141-BUILD activities: read %s, apply %s (context %s, manager %s, WUID service %s, unstances %s, placement %s, "
         "required state %s, element classes %s, rttr %s; logs: text %s, stance names %s)",
         g_readArmed ? "ARMED" : "NOT ARMED", g_applyArmed ? "ARMED" : "NOT ARMED", g_vftCtx ? "ok" : "MISSING",
         mgrOk ? "ok" : "MISSING", svcOk ? "ok" : "MISSING", udbOk ? "ok" : "MISSING", g_execLoaded ? "ok" : "MISSING",
         g_vftRequired ? "ok" : "MISSING", (g_vftStance && g_vftStanceReq && g_vftUnstance && g_vftMinigame) ? "ok" : "MISSING",
         types ? "ok" : "MISSING", g_stateToString ? "ok" : "-", g_stanceName ? "ok" : "-");
}

bool armed() { return g_readArmed; }

void tick() {
    static unsigned n = 0;
    if ((++n % 15) == 0) research_watch();
    const double now = now_s();
    if (now - g_lastTick < g_periodMs.load() / 1000.0) return;
    g_lastTick = now;
    capture_tick(now);
    reconcile_tick(now);
}

void set_frame_callback(FrameFn fn) { g_frame = fn; }

void on_pipe_closed() {
    g_captureNpcs = false; g_capturePlayer = false; g_applyOn = false;
    for (auto& kv : g_desired) if (kv.second.held && kv.second.eid) npcdrive::set_activity_hold(kv.second.eid, false);
    g_desired.clear();
    g_sent.clear();
    g_shownUntil = -1;
}

uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (!len) return kRBadRequest;
    auto name_at = [&](size_t off, std::string* nm, size_t* next) -> bool {
        if (len < off + 1) return false;
        const size_t nl = body[off];
        if (len < off + 1 + nl) return false;
        nm->assign(reinterpret_cast<const char*>(body + off + 1), nl);
        *next = off + 1 + nl;
        return true;
    };
    switch (body[0]) {
        case kOpConfig: {
            if (len != 5 || cap < 1) return kRBadRequest;
            const bool npcs = (body[1] & 1) != 0, me = (body[1] & 2) != 0, ap = body[2] != 0;
            uint16_t ms = static_cast<uint16_t>(body[3] | (body[4] << 8));
            if (ms < 50) ms = 50;
            if (ms > 2000) ms = 2000;
            const bool changed = npcs != g_captureNpcs.load() || me != g_capturePlayer.load() || ap != g_applyOn.load() || ms != g_periodMs.load();
            g_captureNpcs = npcs && g_readArmed; g_capturePlayer = me && g_readArmed; g_applyOn = ap && g_applyArmed; g_periodMs = ms;
            if (!g_applyOn) {
                for (auto& kv : g_desired) if (kv.second.held && kv.second.eid) npcdrive::set_activity_hold(kv.second.eid, false);
                g_desired.clear();
            }
            if (changed)
                logf("WO141-CONFIG capture npcs=%d player=%d, apply %s, every %u ms (read %s, apply %s)", npcs ? 1 : 0, me ? 1 : 0,
                     ap ? "on" : "off", ms, g_readArmed ? "armed" : "NOT ARMED", g_applyArmed ? "armed" : "NOT ARMED");
            out[0] = static_cast<uint8_t>((g_readArmed ? 1 : 0) | (g_applyArmed ? 2 : 0));
            *outLen = 1;
            return (npcs || me) && !g_readArmed ? kRNotArmed : (ap && !g_applyArmed ? kRNotArmed : kROk);
        }
        case kOpApply: {
            std::string nm; size_t at = 0;
            if (!name_at(1, &nm, &at) || nm.empty() || len != at + R::kWireBytes || cap < 1) return kRBadRequest;
            R::Activity a;
            if (!R::decode(body + at, R::kWireBytes, &a)) return kRBadRequest;
            if (!g_applyOn.load()) { out[0] = 0; *outLen = 1; return kRNotArmed; }
            const bool known = g_desired.count(lower(nm.c_str())) != 0;
            set_desired(nm, a);
            out[0] = known ? 1 : 0; *outLen = 1;
            return kROk;
        }
        case kOpLeave: {
            std::string nm; size_t at = 0;
            if (!name_at(1, &nm, &at) || nm.empty() || len != at || cap < 1) return kRBadRequest;
            auto it = g_desired.find(lower(nm.c_str()));
            out[0] = it != g_desired.end() ? 1 : 0; *outLen = 1;
            if (it != g_desired.end()) { set_desired(nm, R::Activity{}); c_leaves.fetch_add(1); logf("WO141-LEAVE %s: stands up, the writer takes the body back", nm.c_str()); }
            return kROk;
        }
        case kOpStatus: {
            const std::string s = status_text();
            const size_t n = s.size() < cap ? s.size() : cap;
            std::memcpy(out, s.data(), n); *outLen = n;
            return kROk;
        }
        case kOpRead: {
            std::string nm; size_t at = 0;
            if (!name_at(1, &nm, &at) || len != at || cap < 1 + R::kWireBytes) return kRBadRequest;
            void* e = nm.empty() ? engine::entity_by_id(0x7777) : entity_by_name(nm.c_str());
            R::Activity a;
            const bool found = e && read_activity(engine::entity_id(e), &a);
            out[0] = found ? 1 : 0;
            R::encode(found ? a : R::Activity{}, out + 1);
            *outLen = 1 + R::kWireBytes;
            return found ? kROk : kRNotFound;
        }
        case kOpForget: {
            if (cap < 2) return kRBadRequest;
            const size_t n = g_desired.size();
            for (auto& kv : g_desired) if (kv.second.held && kv.second.eid) npcdrive::set_activity_hold(kv.second.eid, false);
            g_desired.clear();
            out[0] = static_cast<uint8_t>(n & 0xFF); out[1] = static_cast<uint8_t>(n >> 8); *outLen = 2;
            if (n) logf("WO141-FORGET %zu desired activit%s dropped (no apply)", n, n == 1 ? "y" : "ies");
            return kROk;
        }
        case kOpResync: {
            g_resync = true;
            return kROk;
        }
        case kOpShow: {
            std::string un, ob; size_t at = 0, at2 = 0;
            if (len < 2 || cap < 1 || !name_at(2, &un, &at) || !name_at(at, &ob, &at2) || len != at2) return kRBadRequest;
            out[0] = 0; *outLen = 1;
            if (!body[1]) { g_shownUntil = -1; out[0] = 1; return kROk; }
            const int id = un.empty() ? -1 : unstance_id(un.c_str());
            void* o = ob.empty() ? nullptr : entity_by_name(ob.c_str());
            if (id < 0 || !o) {
                logf("WO141-SHOW refused: unstance '%s' %s, object '%s' %s", un.c_str(), id < 0 ? "unknown" : "ok", ob.c_str(), o ? "ok" : "not found");
                return kRNotFound;
            }
            R::Activity a;
            a.unstance = static_cast<uint16_t>(id);
            a.unstanceObj = engine::entity_guid(o);
            g_shown = R::normalised(a);
            g_shownUntil = now_s() + body[1] / 10.0;
            c_shows.fetch_add(1);
            logf("WO141-SHOW the player's one-shot as '%s' at %s for %.1f s", un.c_str(), ob.c_str(), body[1] / 10.0);
            out[0] = 1;
            return kROk;
        }
    }
    return kRBadRequest;
}

} // namespace kcdmp::wo141
