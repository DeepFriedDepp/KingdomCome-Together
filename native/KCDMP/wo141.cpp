// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#include "wo141.h"
#include "fault_guard.h"
#include "wo141_rules.h"
#include "wo143_rules.h"
#include "npcstate.h"
#include "anchors.h"
#include "combat_swing.h"
#include "engine.h"
#include "hits.h"
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
#include <unordered_set>
#include <vector>

namespace kcdmp::wo141 {

namespace R = kcdmp::wo141rules;
namespace KH = kcdmp::wo143rules;

namespace {

// ---------------------------------------------------------------------------
// SEH-isolated helpers (no destructible locals in any __try frame).
// ---------------------------------------------------------------------------
bool rd(const void* base, size_t off, void** out) {
    KCDMP_FAULT_READ(site, "wo141::rd");
    return fault::guarded(site, [&] { *out = *reinterpret_cast<void* const*>(static_cast<const char*>(base) + off); });
}
bool rd64(const void* base, size_t off, uint64_t* out) {
    KCDMP_FAULT_READ(site, "wo141::rd64");
    return fault::guarded(site, [&] { *out = *reinterpret_cast<const uint64_t*>(static_cast<const char*>(base) + off); });
}
bool rd32(const void* base, size_t off, uint32_t* out) {
    KCDMP_FAULT_READ(site, "wo141::rd32");
    return fault::guarded(site, [&] { *out = *reinterpret_cast<const uint32_t*>(static_cast<const char*>(base) + off); });
}
bool rd8(const void* base, size_t off, uint8_t* out) {
    KCDMP_FAULT_READ(site, "wo141::rd8");
    return fault::guarded(site, [&] { *out = *(static_cast<const uint8_t*>(base) + off); });
}
bool rdstr(const char* p, char* out, size_t n) {
    KCDMP_FAULT_READ(site, "wo141::rdstr");
    if (fault::guarded(site, [&] {
        size_t i = 0;
        for (; i + 1 < n && p[i]; ++i) out[i] = (p[i] >= 32 && p[i] < 127) ? p[i] : '?';
        out[i] = 0;
    })) return true;
    out[0] = 0;
    return false;
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
    KCDMP_FAULT_CALL(site, "wo141::vcall");
    return fault::guarded(site, [&] { *out = reinterpret_cast<R (*)(void*, A...)>(fn)(obj, a...); });
}
template <typename R, typename... A>
bool fcall(void* fn, R* out, A... a) {
    if (!fn) return false;
    KCDMP_FAULT_CALL(site, "wo141::fcall");
    return fault::guarded(site, [&] { *out = reinterpret_cast<R (*)(A...)>(fn)(a...); });
}
template <typename... A>
bool fcall_void(void* fn, A... a) {
    if (!fn) return false;
    KCDMP_FAULT_CALL(site, "wo141::fcall_void");
    return fault::guarded(site, [&] { reinterpret_cast<void (*)(A...)>(fn)(a...); });
}
bool ilock_add(void* p, long d) {
    KCDMP_FAULT_READ(site, "wo141::ilock_add");
    return fault::guarded(site, [&] { InterlockedAdd(static_cast<volatile long*>(p), d); });
}
long ilock_add_get(void* p, long d, bool* ok) {
    KCDMP_FAULT_READ(site, "wo141::ilock_add_get");
    long r = 1;
    *ok = fault::guarded(site, [&] { r = InterlockedAdd(static_cast<volatile long*>(p), d); });
    return *ok ? r : 1;
}
bool wr32(void* base, size_t off, uint32_t v) {
    KCDMP_FAULT_READ(site, "wo141::wr32");
    return fault::guarded(site, [&] { *reinterpret_cast<uint32_t*>(static_cast<char*>(base) + off) = v; });
}
bool wr64(void* base, size_t off, uint64_t v) {
    KCDMP_FAULT_READ(site, "wo141::wr64");
    return fault::guarded(site, [&] { *reinterpret_cast<uint64_t*>(static_cast<char*>(base) + off) = v; });
}
bool wr8(void* base, size_t off, uint8_t v) {
    KCDMP_FAULT_READ(site, "wo141::wr8");
    return fault::guarded(site, [&] { *(static_cast<uint8_t*>(base) + off) = v; });
}
bool wr_slot(void* s, void* p, void* ctrl) {
    KCDMP_FAULT_READ(site, "wo141::wr_slot");
    return fault::guarded(site, [&] {
        *reinterpret_cast<void**>(s) = p;
        *reinterpret_cast<void**>(static_cast<char*>(s) + 8) = ctrl;
    });
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
void* const* g_vftHand = nullptr;      // WO-143: C_HandContentElement (current state)
void* const* g_vftHandReq = nullptr;   // WO-143: C_HandContentElementRequired (the loaded state's)
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
RType g_tStanceReq{}, g_tUnstance{}, g_tHandReq{};
bool g_handsArmed = false;   // WO-143: hand content rides this apply

bool bind_type(const char* name, RType* out) {
    std::string_view nm(name);
    void* r = nullptr;
    return fcall(reinterpret_cast<void*>(g_getByName), &r, out, static_cast<const std::string_view*>(&nm)) && out->data;
}

struct SharedPtr { void* p = nullptr; void* ctrl = nullptr; };

// WO-144 2.1 / 4.5: the NPC state's equipment slot (ChangeEquipment) is kept through a placement
constexpr unsigned kSlotEquipment = 4;
bool g_keepEquipment = true;
std::atomic<uint64_t> c_equipKept{0};

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

// WO-143: an item class id as the game prints it (Lua ItemManager.GetItem(..).class).
std::string class_text(const KH::ClassId& c) {
    if (c.empty()) return "-";
    uint32_t d1 = 0; uint16_t d2 = 0, d3 = 0;
    std::memcpy(&d1, c.b, 4); std::memcpy(&d2, c.b + 4, 2); std::memcpy(&d3, c.b + 6, 2);
    char b[48];
    _snprintf_s(b, sizeof b, _TRUNCATE, "%08x-%04x-%04x-%02x%02x-%02x%02x%02x%02x%02x%02x", d1, d2, d3, c.b[8], c.b[9], c.b[10], c.b[11],
                c.b[12], c.b[13], c.b[14], c.b[15]);
    return b;
}
std::string hands_text(const KH::Hands& h) { return " hands L=" + class_text(h.left) + " R=" + class_text(h.right); }

// ---------------------------------------------------------------------------
// Apply: the loaded state := the activity; the game's own post-load placement.
// ---------------------------------------------------------------------------
enum Applied : uint8_t { kApOk = 0, kApNoBody = 1, kApNoObject = 2, kApBuild = 3, kApExec = 4, kApNotArmed = 5 };
const char* applied_name(uint8_t r) {
    switch (r) { case kApOk: return "ok"; case kApNoBody: return "no-body"; case kApNoObject: return "object-not-here";
                 case kApBuild: return "build-failed"; case kApExec: return "execute-faulted"; case kApNotArmed: return "not-armed"; }
    return "?";
}

// ---- WO-143: hand content ------------------------------------------------------
// C_HandContentElement / ...Required: +0x28 the item's WUID, +0x48 the hand
// (1 left, 2 right) -- its GameLoad reads exactly these (docs/WO-143-findings.md).
constexpr size_t kHandWuid = 0x28;
constexpr size_t kHandType = 0x48;
constexpr size_t kUnstHandRight = 0x60;   // C_UnstanceElement's hand items (its text function, XGenAI +0x187D540)
constexpr size_t kUnstHandLeft  = 0x68;

bool read_hand_class(void* state, unsigned slot, KH::ClassId* out) {
    *out = KH::ClassId{};
    void* b = nullptr; void* e = nullptr; void* el = nullptr;
    if (!rd(state, kStateElemsB, &b) || !rd(state, kStateElemsE, &e) || !b) return false;
    if (static_cast<size_t>(static_cast<char*>(e) - static_cast<char*>(b)) / 16 <= slot) return false;
    if (!rd(b, slot * 16, &el) || !el) return true;                       // an empty hand
    if (!is_a(el, g_vftHand) && !is_a(el, g_vftHandReq)) return true;
    uint64_t w = 0;
    if (!rd64(el, kHandWuid, &w) || !wuid_valid(w)) return true;
    void* item = actions::item_by_wuid(w);
    if (item) actions::item_class_id(item, out->b);
    return true;
}

bool read_hands(uint32_t eid, KH::Hands* out) {
    *out = KH::Hands{};
    void* ctx = ctx_of_eid(eid);
    if (!ctx || !g_vftHand) return false;
    void* st = static_cast<char*>(ctx) + kCtxCurrent;
    return read_hand_class(st, KH::kSlotLeft, &out->left) && read_hand_class(st, KH::kSlotRight, &out->right);
}

using NeedItemFn = void (*)(const char* name, const KH::ClassId& cls);
NeedItemFn g_needItem = nullptr;
// WO-143: told after every apply that carried tools (a paused copy's NPC state
// then settles the take under WO-143's tick)
using HandAppliedFn = void (*)(uint32_t eid);
HandAppliedFn g_handApplied = nullptr;

// An item of this class the body itself holds (its own inventory -- never a
// world item, never anyone else's): its WUID, or 0.
uint64_t own_item_of_class(uint32_t eid, const KH::ClassId& cls) {
    void* soul = hits::soul_of_eid(eid);
    void* inv = soul ? actions::soul_inventory(soul) : nullptr;
    void* item = inv ? actions::inventory_find_class(inv, cls.b) : nullptr;
    return item ? actions::item_wuid(item) : 0;
}

// The body's loaded state (the context's search state) holds this activity and
// these tools, nothing else. hands: null = no hand element (WO-141 as before).
uint8_t build_loaded(uint32_t eid, const R::Activity& want, const KH::Hands* hands, const char* name, KH::Hands* missing,
                     void** ctxOut) {
    if (missing) *missing = KH::Hands{};
    if (!g_applyArmed) return kApNotArmed;
    void* ctx = ctx_of_eid(eid);
    if (!ctx) return kApNoBody;
    *ctxOut = ctx;
    void* loaded = static_cast<char*>(ctx) + kCtxLoaded;
    if (!is_a(loaded, g_vftRequired)) return kApBuild;
    const R::Activity a = R::normalised(want);
    uint64_t sW = 0, uW = 0;
    if (a.stance && a.stanceObj) { sW = wuid_of_guid(a.stanceObj); if (!sW) return kApNoObject; }
    if (a.unstance != R::kNoUnstance && a.unstanceObj) { uW = wuid_of_guid(a.unstanceObj); if (!uW) return kApNoObject; }
    SharedPtr s{}, u{}, hl{}, hr{};
    if (a.stance && !sp_create(g_tStanceReq, g_vftStanceReq, &s)) return kApBuild;
    if (a.unstance != R::kNoUnstance && !sp_create(g_tUnstance, g_vftUnstance, &u)) { sp_release(s.ctrl); return kApBuild; }
    if (s.p) { wr32(s.p, kElId, a.stance); wr64(s.p, kElObj, sW); wr8(s.p, kElSlot, a.cart); }
    if (u.p) { wr32(u.p, kElId, a.unstance); wr64(u.p, kElObj, uW); }
    // WO-143: the tool in each hand -- an item of that class the body owns; a
    // class it does not own is reported (the mod gives it a temporary one) and
    // this apply goes on without it
    uint64_t handW[2] = {0, 0};
    if (hands && g_handsArmed) {
        const KH::ClassId* want2[2] = {&hands->left, &hands->right};
        SharedPtr* el[2] = {&hl, &hr};
        for (int i = 0; i < 2; ++i) {
            if (want2[i]->empty()) continue;
            const uint64_t iw = own_item_of_class(eid, *want2[i]);
            if (!iw) {
                if (missing) (i == 0 ? missing->left : missing->right) = *want2[i];
                if (g_needItem && name) g_needItem(name, *want2[i]);
                continue;
            }
            if (!sp_create(g_tHandReq, g_vftHandReq, el[i])) continue;
            wr64(el[i]->p, kHandWuid, iw);
            wr8(el[i]->p, kHandType, i == 0 ? KH::kHandLeft : KH::kHandRight);
            handW[i] = iw;
        }
    }
    // The object use names the tools it is done with (C_UnstanceElement +0x60
    // right, +0x68 left: its own text, "Hand items: R: .. L: .."). Left at the
    // invalid WUID, a tool trade finds no path (J1: the carpenter at the
    // debarking bench, the sawyer, the scribe -- "can't find a path from actions").
    if (u.p) {
        if (handW[1]) wr64(u.p, kUnstHandRight, handW[1]);
        if (handW[0]) wr64(u.p, kUnstHandLeft, handW[0]);
    }
    // the save's leftovers go (contexts, links, buffs, equipment of whatever the
    // body was loaded in); the stance and unstance are ours
    fcall_void(vslot(loaded, kStateClear), loaded);
    bool ok = state_set_slot(loaded, 0, s) && state_set_slot(loaded, 3, u);
    if (hl.p) ok = state_set_slot(loaded, KH::kSlotLeft, hl) && ok;
    if (hr.p) ok = state_set_slot(loaded, KH::kSlotRight, hr) && ok;
    // WO-144 2.1 / 4.5: the body's own equipment element (slot 4: an NPC's sleep undress, an outfit
    // change) goes into the loaded state as it is -- a placement or a one-shot never changes clothes.
    // Cleared, the game looked for a way back into the default outfit and found none: "Execution of
    // action ChangeEquipmentFromDefault has failed! Action for request 'KCDMP one-shot'", "Couldn't
    // find actions to get NPC into game loaded state" with "ChangeEquipment ... sleepUnequip" in the
    // current state (the field: 102 / 60 / 40 lines on three copies).
    if (ok && g_keepEquipment) {
        void* cur = static_cast<char*>(ctx) + kCtxCurrent;
        void* cb = nullptr; void* ce = nullptr;
        if (rd(cur, kStateElemsB, &cb) && rd(cur, kStateElemsE, &ce) && cb
            && static_cast<size_t>(static_cast<char*>(ce) - static_cast<char*>(cb)) / 16 > kSlotEquipment) {
            SharedPtr eq{};
            rd(static_cast<char*>(cb) + kSlotEquipment * 16, 0, &eq.p);
            rd(static_cast<char*>(cb) + kSlotEquipment * 16, 8, &eq.ctrl);
            if (eq.p && eq.ctrl && ilock_add(static_cast<char*>(eq.ctrl) + 8, 1)) {   // the loaded state's own reference
                if (state_set_slot(loaded, kSlotEquipment, eq)) c_equipKept.fetch_add(1, std::memory_order_relaxed);
            }
        }
    }
    return ok ? kApOk : kApBuild;
}

// hands: null = WO-141's apply exactly as before (no hand element).
uint8_t apply_now(uint32_t eid, const R::Activity& want, uint8_t* execResult, const KH::Hands* hands = nullptr,
                  const char* name = nullptr, KH::Hands* missing = nullptr) {
    *execResult = 0;
    void* ctx = nullptr;
    const uint8_t b = build_loaded(eid, want, hands, name, missing, &ctx);
    if (b != kApOk) return b;
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
    // WO-143: the tool in each hand (handsSet = the host sends them for this body)
    KH::Hands hands;
    bool handsSet = false;
    KH::LogPace logPace;
    bool handsDropped = false;   // the game refused its tools with this activity: shown without them (WO-141's apply)
    int handsTries = 0;          // applies with every tool owned that did not bring the body in step
};
std::unordered_set<std::string> g_seatedHandsNoted;   // bodies told once: seated, their hands stay as they are
std::unordered_set<uint64_t> g_bedSitNoted;          // WO-144 5: beds told once (a player's bed-edge sit shown lying)

// WO-143: tools ride the apply only for a body that does not sit, lie or kneel on
// an object (J1: a seated guest given his tankard is stood up by the game --
// "Execution of 3 actions from load couldn't reach the loaded state", then
// ForceIdleState clears 'sitting' -- and sat down again by the reconcile), and
// not once the game has refused them three times with this activity (it is then
// shown exactly as WO-141 shows it).
bool hands_ride(const Desired& d) { return g_handsArmed && R::hands_ride(d.handsSet, d.handsDropped, d.a.stance); }
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
    if (kind == kKindPlayer) a = R::without_crouch(R::with_shown(a, g_shown, now < g_shownUntil));   // WO-144 2.2: crouch rides the state block
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
        // The writer stays off a body the game places (in step, or while it is tried). One the game keeps
        // refusing here is written again once the retries back off (WO-143 J1: held, it stood wherever the
        // stream had left it, and a forced look turned it bodily); each backed-off retry is held again.
        const bool owns = R::owns_position(d.a);
        auto hold_to = [&](bool want) { if (want != d.held) { npcdrive::set_activity_hold(d.eid, want); d.held = want; } };
        R::Activity cur;
        if (!read_activity(d.eid, &cur)) { hold_to(R::hold_wanted(owns, d.matched, d.pace.misses, true, d.a.stance)); ++it; continue; }
        if (R::is_avatar_name(d.name.c_str())) {
            cur = R::without_crouch(cur); d.a = R::without_crouch(d.a);   // WO-144 2.2
            if (d.a.stance == R::kSitting && d.a.stanceObj) {             // WO-144 5: a bed's edge -> lying on it
                void* so = engine::entity_by_guid(d.a.stanceObj);
                char sn[96] = "";
                if (so && engine::entity_name(so)) rdstr(engine::entity_name(so), sn, sizeof sn);
                if (R::is_bed_name(sn)) {
                    d.a = R::bed_sit_as_lying(d.a, true);
                    if (g_bedSitNoted.insert(d.a.stanceObj).second)
                        logf("WO141-APPLY %s: its player sits on a bed (%s) -- an NPC body has no sit-down there; it lies on it", d.name.c_str(), sn);
                }
            }
        }
        KH::Hands curHands;
        if (d.handsSet && !d.hands.empty() && R::object_stance(d.a.stance) && g_seatedHandsNoted.insert(it->first).second)
            logf("WO143-HANDS %s sits, lies or kneels: its hands stay as they are (a seated copy's take would stand it up)", d.name.c_str());
        const bool useHands = hands_ride(d);
        const bool handsOk = !useHands || (read_hands(d.eid, &curHands) && curHands == d.hands);
        if (R::same_body(cur, d.a) && handsOk) {
            hold_to(owns);
            if (!d.matched) logf("WO141-APPLY %s in step: %s%s", d.name.c_str(), describe(d.a).c_str(), useHands ? hands_text(d.hands).c_str() : "");
            d.matched = true; d.pace.misses = 0; KH::log_reset(d.logPace); d.handsTries = 0;
            if (R::none(d.a) && (!d.handsSet || d.hands.empty())) { if (d.held) npcdrive::set_activity_hold(d.eid, false); it = g_desired.erase(it); continue; }
            ++it; continue;
        }
        d.matched = false;
        const bool waiting = now < d.pace.nextAt;
        hold_to(R::hold_wanted(owns, false, d.pace.misses, waiting, d.a.stance));   // (J3: a cart stance stays held)
        if (waiting) { ++it; continue; }
        uint8_t res = 0;
        KH::Hands missing;
        if (useHands && d.handsTries >= R::kHandsTriesBeforeDrop && !d.hands.empty()) {
            d.handsDropped = true;
            logf("WO143-HANDS %s: the game refuses its tools with this activity (%d tries) -- shown without them, as WO-141 shows it",
                 d.name.c_str(), d.handsTries);
            ++it; continue;   // the next tick applies the activity alone
        }
        const uint8_t r = apply_now(d.eid, d.a, &res, useHands ? &d.hands : nullptr, d.name.c_str(), &missing);
        if (useHands && missing.empty()) ++d.handsTries;   // a temporary tool still on its way does not count
        c_applies.fetch_add(1); ++d.applies;
        if (r == kApOk && useHands && g_handApplied) g_handApplied(d.eid);
        (r == kApOk ? c_applyOk : c_applyFail).fetch_add(1);
        ++d.pace.misses;
        d.pace.nextAt = now + R::next_delay(d.pace.misses);
        // WO-143 Phase 7: the first three tries, then once a minute with the count
        int suppressed = 0;
        if (KH::log_due(d.logPace, now, &suppressed)) {
            R::Activity after; read_activity(d.eid, &after);
            KH::Hands afterHands; if (useHands) read_hands(d.eid, &afterHands);
            char more[64] = "";
            if (suppressed) _snprintf_s(more, sizeof more, _TRUNCATE, " (%d more tries since the last line)", suppressed);
            logf("WO141-APPLY %s -> %s (exec %u, try %d%s): wanted %s%s%s; now %s%s", d.name.c_str(), applied_name(r), res, d.pace.misses, more,
                 describe(d.a).c_str(), useHands ? hands_text(d.hands).c_str() : "",
                 missing.empty() ? "" : " (a tool it does not own was asked for)", describe(after).c_str(),
                 useHands ? hands_text(afterHands).c_str() : "");
        }
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
                "read %s apply %s | capture npcs=%d player=%d period=%ums | apply %s desired=%zu | rows %llu reads %llu applies %llu (ok %llu fail %llu) leaves %llu shows %llu equip_kept %llu",
                g_readArmed ? "armed" : "NOT ARMED", g_applyArmed ? "armed" : "NOT ARMED", g_captureNpcs.load() ? 1 : 0,
                g_capturePlayer.load() ? 1 : 0, g_periodMs.load(), g_applyOn.load() ? "on" : "off", g_desired.size(),
                static_cast<unsigned long long>(c_rowsSent.load()), static_cast<unsigned long long>(c_reads.load()),
                static_cast<unsigned long long>(c_applies.load()), static_cast<unsigned long long>(c_applyOk.load()),
                static_cast<unsigned long long>(c_applyFail.load()), static_cast<unsigned long long>(c_leaves.load()),
                static_cast<unsigned long long>(c_shows.load()), static_cast<unsigned long long>(c_equipKept.load()));
    return b;
}

void set_desired(const std::string& name, const R::Activity& a) {
    Desired& d = g_desired[lower(name.c_str())];
    const bool changed = d.name.empty() || !R::same(d.a, a);
    d.name = name;
    if (changed) { d.a = R::normalised(a); d.pace = {}; d.matched = false; d.handsDropped = false; d.handsTries = 0; }
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
    // WO-143: hand content rides this apply (the loaded state's HandContentElementRequired)
    g_vftHand    = anchor::find_vftable(g_xg, ".?AVC_HandContentElement@NPCState@xgenaimodule@wh@@");
    g_vftHandReq = anchor::find_vftable(g_xg, ".?AVC_HandContentElementRequired@NPCState@xgenaimodule@wh@@");
    g_handsArmed = g_applyArmed && g_vftHand && g_vftHandReq && g_getByName &&
                   bind_type("wh::xgenaimodule::NPCState::HandContentElementRequired", &g_tHandReq);
    logf("WO141-BUILD activities: read %s, apply %s (context %s, manager %s, WUID service %s, unstances %s, placement %s, "
         "required state %s, element classes %s, rttr %s; logs: text %s, stance names %s)",
         g_readArmed ? "ARMED" : "NOT ARMED", g_applyArmed ? "ARMED" : "NOT ARMED", g_vftCtx ? "ok" : "MISSING",
         mgrOk ? "ok" : "MISSING", svcOk ? "ok" : "MISSING", udbOk ? "ok" : "MISSING", g_execLoaded ? "ok" : "MISSING",
         g_vftRequired ? "ok" : "MISSING", (g_vftStance && g_vftStanceReq && g_vftUnstance && g_vftMinigame) ? "ok" : "MISSING",
         types ? "ok" : "MISSING", g_stateToString ? "ok" : "-", g_stanceName ? "ok" : "-");
}

bool armed() { return g_readArmed; }

// ---- WO-143: the pieces WO-143 builds on (npcstate.h) ------------------------
namespace x {
HMODULE xgenai() { return g_xg; }
void* npc_manager() { void* m = nullptr; return g_npcMgrSlot && rd(g_npcMgrSlot, 0, &m) ? m : nullptr; }
void* context_of(uint32_t eid) { return ctx_of_eid(eid); }
void* entity_named(const char* name) { return entity_by_name(name); }
void* entity_of(uint64_t wuid) { return entity_of_wuid(wuid); }
uint64_t guid_of(uint64_t wuid) { return wuid_valid(wuid) ? guid_of_wuid(wuid) : 0; }
uint64_t wuid_of(uint64_t guid) { return wuid_of_guid(guid); }
bool state_element(void* state, unsigned slot, void** el) {
    void* b = nullptr; void* e = nullptr;
    *el = nullptr;
    if (!rd(state, kStateElemsB, &b) || !rd(state, kStateElemsE, &e) || !b) return false;
    if (static_cast<size_t>(static_cast<char*>(e) - static_cast<char*>(b)) / 16 <= slot) return false;
    return rd(b, slot * 16, el);
}
bool bind_rttr(const char* name, void** typeData) { RType t{}; if (!g_getByName || !bind_type(name, &t)) return false; *typeData = t.data; return true; }
bool create_element(void* typeData, void* const* vft, void** p, void** ctrl) {
    SharedPtr sp{}; RType t{typeData};
    if (!sp_create(t, vft, &sp)) return false;
    *p = sp.p; *ctrl = sp.ctrl;
    return true;
}
void release(void* ctrl) { sp_release(ctrl); }
bool apply_armed() { return g_applyArmed; }
void* loaded_state(void* ctx) { void* l = ctx ? static_cast<char*>(ctx) + kCtxLoaded : nullptr; return l && is_a(l, g_vftRequired) ? l : nullptr; }
void* current_state(void* ctx) { return ctx ? static_cast<char*>(ctx) + kCtxCurrent : nullptr; }
bool clear_loaded(void* loaded) { return loaded && fcall_void(vslot(loaded, kStateClear), loaded); }
bool set_loaded_slot(void* loaded, unsigned slot, void* p, void* ctrl) { return state_set_slot(loaded, slot, SharedPtr{p, ctrl}); }
bool execute_loaded(void* ctx, uint8_t* res) { *res = 0; return g_execLoaded && ctx && fcall(g_execLoaded, res, ctx); }
std::string describe_state(void* state) { return state_text(state); }
const char* unstance_label(uint16_t id, char* buf, size_t n) { return unstance_name(id, buf, n); }
int unstance_index(const char* name) { return unstance_id(name); }

bool hands_armed() { return g_handsArmed; }
bool read_body_hands(uint32_t eid, KH::Hands* out) { return read_hands(eid, out); }
std::string hand_classes_text(const KH::Hands& h) { return hands_text(h); }
std::string class_id_text(const KH::ClassId& c) { return class_text(c); }
bool apply_on() { return g_applyOn.load(); }

// The body with this name holds these tools (reconciled with its activity; a
// body with no activity row gets "none" beside them). Empty hands put the
// tools away (and then the entry goes, once in step).
void set_hands(const std::string& name, const KH::Hands& h) {
    Desired& d = g_desired[lower(name.c_str())];
    if (d.name.empty()) { d.name = name; d.a = R::normalised(R::Activity{}); }
    const bool changed = !d.handsSet || d.hands != h;
    d.handsSet = true; d.hands = h;
    if (changed) { d.pace = {}; d.matched = false; KH::log_reset(d.logPace); d.handsDropped = false; d.handsTries = 0; }
}
// mp_hand_items off: every body puts its tools away (the reconcile does it).
int clear_all_hands() {
    int n = 0;
    for (auto& kv : g_desired) if (kv.second.handsSet && !kv.second.hands.empty()) { kv.second.hands = KH::Hands{}; kv.second.pace = {}; kv.second.matched = false; ++n; }
    return n;
}
// What the reconcile wants for a body's hands (for the agent's temporary items).
bool desired_hands(const std::string& name, KH::Hands* out) {
    auto it = g_desired.find(lower(name.c_str()));
    if (it == g_desired.end() || !it->second.handsSet) return false;
    *out = it->second.hands;
    return true;
}
void set_need_item_callback(void (*fn)(const char* name, const KH::ClassId& cls)) { g_needItem = fn; }
void set_hand_applied_callback(void (*fn)(uint32_t eid)) { g_handApplied = fn; }
// Research: the same tools taken again (put away, then taken) on the next ticks.
bool retake_hands(const std::string& name) {
    auto it = g_desired.find(lower(name.c_str()));
    if (it == g_desired.end() || !it->second.handsSet || !it->second.eid) return false;
    Desired& d = it->second;
    uint8_t res = 0;
    const uint8_t r1 = apply_now(d.eid, d.a, &res, nullptr, d.name.c_str(), nullptr);
    d.pace = {}; d.matched = false;   // the reconcile takes them again at once
    return r1 == kApOk;
}

// A one-shot's request (or its stop) makes the body keep only what its search
// state requires -- empty, it drops the tool in its hands (H2: the avatar's sword
// went at the request). The game's own requesters fill it first (the player's
// state handler adds its elements right before RequestStateChange); so does
// this: what WO-141 wants on the body, else what the body has now. Nothing is
// executed here; false = nothing filled (the request as before).
bool prepare_request(uint32_t eid, std::string* kept) {
    if (kept) kept->clear();
    if (!g_applyArmed || !eid) return false;
    const Desired* d = nullptr;
    for (const auto& kv : g_desired) if (kv.second.eid == eid) { d = &kv.second; break; }
    R::Activity a; KH::Hands h; bool useHands = false;
    if (d) { a = d->a; useHands = hands_ride(*d); if (useHands) h = d->hands; }
    else {
        if (!read_activity(eid, &a)) return false;
        useHands = g_handsArmed && read_hands(eid, &h) && !h.empty();
    }
    void* ctx = nullptr;
    if (build_loaded(eid, a, useHands ? &h : nullptr, nullptr, nullptr, &ctx) != kApOk) return false;
    if (kept) *kept = describe(a) + (useHands ? hands_text(h) : std::string());
    return true;
}
} // namespace x

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
