#include "wo143.h"
#include "wo143_rules.h"
#include "npcstate.h"
#include "anchors.h"
#include "engine.h"
#include "hits.h"
#include "inline_hook.h"
#include "log.h"
#include "npc_drive.h"
#include "respawn_actions.h"
#include "script_context.h"
#include "wo138.h"

#include <windows.h>
#include <array>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <functional>
#include <memory>
#include <mutex>
#include <new>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace kcdmp::wo143 {

namespace X = kcdmp::wo141::x;
namespace K = kcdmp::wo143rules;

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
bool wr64(void* base, size_t off, uint64_t v) {
    __try { *reinterpret_cast<uint64_t*>(static_cast<char*>(base) + off) = v; return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool wr8(void* base, size_t off, uint8_t v) {
    __try { *(static_cast<uint8_t*>(base) + off) = v; return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool rdstr(const char* p, char* out, size_t n) {
    __try {
        size_t i = 0;
        for (; i + 1 < n && p && p[i]; ++i) out[i] = (p[i] >= 32 && p[i] < 127) ? p[i] : '?';
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
bool prologue_ok(const void* fn, const uint8_t* want, size_t n) {
    for (size_t i = 0; i < n; ++i) { uint8_t b = 0; if (!rd8(fn, i, &b) || b != want[i]) return false; }
    return true;
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
std::string hexdump(const void* p, size_t n) {
    std::string s; char b[4];
    for (size_t i = 0; i < n; ++i) {
        uint8_t v = 0;
        if (!rd8(p, i, &v)) { s += "??"; continue; }
        _snprintf_s(b, sizeof b, _TRUNCATE, "%02X", v); s += b;
        if ((i & 7) == 7) s += ' ';
    }
    return s;
}
// An item class id is laid out as a Windows GUID (Data1..3 little-endian,
// Data4 in byte order): the text is the one Lua's ItemManager.GetItem(..).class
// prints (observed: the woodworker's saw, both reads).
std::string guid_text(const uint8_t g[16]) {
    K::ClassId c; std::memcpy(c.b, g, 16);
    return X::class_id_text(c);
}
bool parse_guid(const char* s, uint8_t out[16]) {
    unsigned d1 = 0, d2 = 0, d3 = 0, b[8]{};
    if (sscanf_s(s, "%8x-%4x-%4x-%2x%2x-%2x%2x%2x%2x%2x%2x", &d1, &d2, &d3, &b[0], &b[1], &b[2], &b[3], &b[4], &b[5], &b[6], &b[7]) != 11)
        return false;
    const uint32_t v1 = d1; const uint16_t v2 = static_cast<uint16_t>(d2), v3 = static_cast<uint16_t>(d3);
    std::memcpy(out, &v1, 4); std::memcpy(out + 4, &v2, 2); std::memcpy(out + 6, &v3, 2);
    for (int i = 0; i < 8; ++i) out[8 + i] = static_cast<uint8_t>(b[i]);
    return true;
}

// A CryStringT<char> the engine keeps a pointer to: header {refcount, len, cap}
// at data-12, a LARGE POSITIVE refcount (a negative one is swapped for "" by
// the engine's copy path -- WO-97's trap), and NEVER reused: the anim action
// shares the pointer (research r1: a reused buffer made a queued action play the
// next call's fragment). So every distinct text is interned once, for good.
struct CryStr {
    int32_t ref, len, cap;
    char    data[1];
};
const char* intern(const std::string& s) {
    static std::mutex mu;
    static std::unordered_map<std::string, CryStr*> pool;
    std::lock_guard<std::mutex> lk(mu);
    auto it = pool.find(s);
    if (it != pool.end()) return it->second->data;
    if (pool.size() > 4096) return nullptr;   // a runaway: refuse, never grow without bound
    void* mem = ::operator new(sizeof(CryStr) + s.size() + 1);
    auto* c = static_cast<CryStr*>(mem);
    c->ref = 0x40000000; c->len = c->cap = static_cast<int32_t>(s.size());
    std::memcpy(c->data, s.c_str(), s.size() + 1);
    pool.emplace(s, c);
    return c->data;
}

// ---------------------------------------------------------------------------
// Anchors
// ---------------------------------------------------------------------------
HMODULE g_xg = nullptr;
void* const* g_vftHand = nullptr;        // C_HandContentElement (current state)
void* const* g_vftHandReq = nullptr;     // C_HandContentElementRequired (loaded state)
void* const* g_vftAnimAction = nullptr;  // C_AnimAction (a one-shot)
void* const* g_vftLookTarget = nullptr;  // C_NPCLookTarget
void* g_tHandReq = nullptr;              // rttr type data
void* g_reqChange = nullptr;             // C_NPCContext::RequestStateChange
void* g_mkAnimAction = nullptr;          // the player state handler's anim action builder (+0x18CABA0)
void* g_mkEventCtx = nullptr;            // make_shared<C_AnimEventContext> (+0xDA6B20)
void* g_initReqBlock = nullptr;          // the request's required block ctor (+0x506710)
void* g_ctxPreUpdate = nullptr;          // C_NPC::Update's three NPC-state steps (a paused copy's tick):
void* g_execUpdate = nullptr;            //   ctx +0x18887C0 (the finished request's callback), executor +0x1625D20,
void* g_ctxUpdate = nullptr;             //   ctx +0x1887AC0 (starts the search result)
const uint64_t* g_invalidAlign = nullptr;   // XGenAI+0x2E416B0: "no align object" (the builder's own test)
const uint64_t* g_invalidTrigger = nullptr; // XGenAI+0x2E3BCA0: the params' default trigger id
uint32_t g_lookDisp = 0;                 // the look-target component's offset in the NPC object (its getter's lea)
bool g_handArmed = false, g_animArmed = false, g_tickArmed = false, g_hookArmed = false, g_lookArmed = false;

constexpr size_t kRvaRequestStateChange = 0x1881090;
constexpr size_t kRvaMakeAnimAction     = 0x18CABA0;
constexpr size_t kRvaMakeEventCtx       = 0xDA6B20;
constexpr size_t kRvaInitReqBlock       = 0x506710;
constexpr size_t kRvaCtxPreUpdate       = 0x18887C0;
constexpr size_t kRvaExecUpdate         = 0x1625D20;
constexpr size_t kRvaCtxUpdate          = 0x1887AC0;
constexpr size_t kRvaInvalidAlign       = 0x2E416B0;
constexpr size_t kRvaInvalidTrigger     = 0x2E3BCA0;
const uint8_t kProReqChange[24] = {0x40,0x55,0x53,0x56,0x57,0x41,0x55,0x41,0x56,0x48,0x8D,0xAC,0x24,0x98,0xFE,0xFF,0xFF,0x48,0x81,0xEC,0x68,0x02,0x00,0x00};
const uint8_t kProMakeAnim[21]  = {0x48,0x89,0x5C,0x24,0x18,0x55,0x56,0x41,0x56,0x48,0x8D,0x6C,0x24,0xB9,0x48,0x81,0xEC,0xB0,0x00,0x00,0x00};
const uint8_t kProMakeEvCtx[11] = {0x40,0x56,0x41,0x56,0x48,0x83,0xEC,0x28,0x4C,0x8B,0xF1};
const uint8_t kProInitBlock[24] = {0x48,0x89,0x5C,0x24,0x08,0x57,0x48,0x83,0xEC,0x20,0x33,0xFF,0x48,0x8B,0xD9,0x48,0x89,0x39,0x41,0xB8,0x03,0x00,0x00,0x00};
const uint8_t kProCtxPre[12]    = {0x40,0x53,0x48,0x81,0xEC,0x60,0x01,0x00,0x00,0x48,0x8B,0x05};
const uint8_t kProExecUpd[12]   = {0x40,0x53,0x48,0x83,0xEC,0x30,0x48,0x8B,0xD9,0x48,0x8D,0x0D};
const uint8_t kProCtxUpd[19]    = {0x40,0x55,0x41,0x54,0x48,0x8D,0xAC,0x24,0x58,0xFF,0xFF,0xFF,0x48,0x81,0xEC,0xA8,0x01,0x00,0x00};

constexpr size_t kHandWuid = 0x28;
constexpr size_t kHandType = 0x48;
constexpr size_t kAnimFragment = 0x1A8;
constexpr size_t kAnimTags     = 0x1B0;
constexpr size_t kAnimAlign    = 0x1B8;
constexpr size_t kReqAction    = 0x48;
constexpr size_t kReqEventCtx  = 0x60;
constexpr size_t kReqBlock     = 0x70;
constexpr size_t kReqName      = 0x118;
constexpr size_t kReqSize      = 0x120;
constexpr size_t kNpcLookGetter = 0x210;   // the NPC object's look-target getter (the BT Look node's call)
constexpr size_t kLookKind = 0x30;
constexpr size_t kLookWuid = 0x38;
constexpr size_t kObjCtx   = 0x9C0;
constexpr size_t kObjExec  = 0x1048;       // C_NPC::Update: FUN(param_1 + 0x209) = the action executor
constexpr size_t kObjWuid  = 0x10;         // the NPC's own WUID (the player handler's anim-event context reads it)

void* npc_object(uint32_t eid) {
    void* mgr = X::npc_manager(); void* o = nullptr;
    if (!mgr || !eid) return nullptr;
    if (!vcall(mgr, 0x20, &o, static_cast<const uint32_t*>(&eid))) return nullptr;
    return o;
}

// lea rax, [rbx + disp32] behind the JMC check: 40 53 48 83 EC 20 48 8B D9 48 8D 0D
// <rel32> E8 <rel32> 48 8D 83 <disp32> 48 83 C4 20 5B C3 (the look getter, read in r1)
bool getter_disp(const void* fn, uint32_t* disp) {
    uint8_t b[34]{};
    for (int i = 0; i < 34; ++i) if (!rd8(fn, i, &b[i])) return false;
    const uint8_t head[12] = {0x40,0x53,0x48,0x83,0xEC,0x20,0x48,0x8B,0xD9,0x48,0x8D,0x0D};
    if (std::memcmp(b, head, 12) || b[16] != 0xE8 || b[21] != 0x48 || b[22] != 0x8D || b[23] != 0x83 ||
        b[28] != 0x48 || b[29] != 0x83 || b[30] != 0xC4 || b[31] != 0x20 || b[32] != 0x5B || b[33] != 0xC3) return false;
    std::memcpy(disp, b + 24, 4);
    return *disp > 0 && *disp < 0x4000;
}

void* look_component(uint32_t eid) {
    if (!g_lookArmed) return nullptr;
    void* obj = npc_object(eid);
    if (!obj || !X::context_of(eid)) return nullptr;
    void* lt = static_cast<char*>(obj) + g_lookDisp;
    return is_a(lt, g_vftLookTarget) ? lt : nullptr;
}

// ---------------------------------------------------------------------------
// Settings (the agent's Config) and counters
// ---------------------------------------------------------------------------
constexpr uint8_t kBitHands = 1, kBitGaits = 2, kBitOneShots = 4, kBitLooks = 8, kBitAvatarShots = 16;
std::atomic<uint8_t> g_capture{0}, g_apply{0};
FrameFn g_frame = nullptr;
std::atomic<uint64_t> c_handRows{0}, c_gaitRows{0}, c_lookRows{0}, c_shotsOut{0}, c_shotsIn{0}, c_shotsDone{0}, c_shotsFailed{0},
    c_ticks{0}, c_gaitSet{0}, c_gaitClear{0}, c_needItem{0}, c_hooked{0}, c_animSeen{0}, c_shotsThrottled{0}, c_prepared{0};

double now_s() { return npcdrive::now_s(); }

// ---------------------------------------------------------------------------
// Frames (0xA9): [kind][...]
// ---------------------------------------------------------------------------
constexpr uint8_t kFrameHands = 1, kFrameGaits = 2, kFrameOneShot = 3, kFrameLooks = 4, kFrameNeedItem = 5, kFrameShotDone = 6;

void put_name(std::vector<uint8_t>& v, const std::string& s, size_t maxLen = 63) {
    const size_t n = s.size() > maxLen ? maxLen : s.size();
    v.push_back(static_cast<uint8_t>(n));
    v.insert(v.end(), s.begin(), s.begin() + n);
}

struct RowBatch {
    uint8_t kind;
    std::vector<uint8_t> out;
    uint8_t count = 0;
    explicit RowBatch(uint8_t k) : kind(k) { reset(); }
    void reset() { out.assign({kind, 0}); count = 0; }
    void flush() {
        if (count && g_frame) { out[1] = count; g_frame(out.data(), static_cast<uint16_t>(out.size())); }
        reset();
    }
    void room(size_t need) { if (out.size() + need > 900 || count == 12) flush(); }
};

// ---------------------------------------------------------------------------
// Host: the tracked NPCs' hands, gaits and looks
// ---------------------------------------------------------------------------
struct Tracked { std::string name; uint32_t eid; };
std::vector<Tracked> g_tracked;
std::unordered_set<uint32_t> g_trackedEids;
void collect_tracked(const char* name, uint32_t eid, void* ctx) {
    auto* v = static_cast<std::vector<Tracked>*>(ctx);
    if (name && *name && eid) v->push_back({name, eid});
}

struct SentHands { K::Hands h; double at = -1e9; bool have = false; };
struct SentGaits { uint16_t m = 0; double at = -1e9; bool have = false; };
struct SentLook { uint8_t kind = 0; std::string target; double at = -1e9; bool have = false; };
std::unordered_map<std::string, SentHands> g_sentHands;
std::unordered_map<std::string, SentGaits> g_sentGaits;
std::unordered_map<std::string, SentLook> g_sentLooks;
bool g_resync = false;
double g_lastHands = 0, g_lastGaits = 0, g_lastLooks = 0;
K::LookBudget g_lookBudget;

uint16_t read_gaits(uint32_t eid, bool* ok) {
    *ok = false;
    void* soul = hits::soul_of_eid(eid);
    if (!soul) return 0;
    uint16_t m = 0;
    for (int i = 0; i < K::kGaitCount; ++i) {
        const int v = sctx::has_soul_context(soul, K::gait_name(i));
        if (v < 0) return 0;
        if (v == 1) m = static_cast<uint16_t>(m | (1u << i));
    }
    *ok = true;
    return m;
}

bool player_positions(std::vector<std::array<float, 3>>* out) {
    out->clear();
    float p[3];
    if (void* pe = engine::entity_by_id(0x7777); pe && engine::entity_world_pos(pe, p)) out->push_back({p[0], p[1], p[2]});
    uint32_t eids[8]; void* souls[8];
    const int n = hits::avatar_list(eids, souls, 8);
    for (int i = 0; i < n; ++i) if (void* e = engine::entity_by_id(eids[i]); e && engine::entity_world_pos(e, p)) out->push_back({p[0], p[1], p[2]});
    return !out->empty();
}

// Who is at this WUID, as the other machine names it.
void look_identity(uint64_t wuid, uint8_t* kind, std::string* target) {
    *kind = K::kTargetNone; target->clear();
    void* e = wuid ? X::entity_of(wuid) : nullptr;
    if (!e) return;
    const uint32_t id = engine::entity_id(e);
    if (id == 0x7777) { *kind = K::kTargetHostPlayer; return; }
    char nb[80]; rdstr(engine::entity_name(e) ? engine::entity_name(e) : "", nb, sizeof nb);
    if (!std::strncmp(nb, "kcd2mp_", 7) && nb[7]) { *kind = K::kTargetPeer; *target = nb + 7; return; }
    if (nb[0]) { *kind = K::kTargetNpc; *target = nb; }
}

void capture_tick(double now) {
    const uint8_t cap = g_capture.load();
    if (!cap) return;
    g_tracked.clear();
    wo138::for_each_tracked(&collect_tracked, &g_tracked);
    g_trackedEids.clear();
    for (auto& t : g_tracked) g_trackedEids.insert(t.eid);
    // hands, every 250 ms
    if ((cap & kBitHands) && X::hands_armed() && now - g_lastHands >= 0.25) {
        g_lastHands = now;
        RowBatch rb(kFrameHands);
        for (auto& t : g_tracked) {
            K::Hands h;
            if (!X::read_body_hands(t.eid, &h)) continue;
            SentHands& s = g_sentHands[lower(t.name.c_str())];
            if (!s.have && h.empty() && !g_resync) { s.have = true; s.at = now; continue; }   // first sight, nothing in hand: nothing to say
            const bool changed = !s.have || s.h != h;
            if (!K::send_due(changed || g_resync, h.empty(), now - s.at)) continue;
            if (changed) logf("WO143-HANDS capture %s:%s", t.name.c_str(), X::hand_classes_text(h).c_str());
            s.h = h; s.at = now; s.have = true;
            rb.room(2 + t.name.size() + 32);
            put_name(rb.out, t.name);
            rb.out.insert(rb.out.end(), h.left.b, h.left.b + 16);
            rb.out.insert(rb.out.end(), h.right.b, h.right.b + 16);
            ++rb.count; c_handRows.fetch_add(1);
        }
        rb.flush();
    }
    // gaits, every second
    if ((cap & kBitGaits) && sctx::isolation_enabled() && now - g_lastGaits >= 1.0) {
        g_lastGaits = now;
        RowBatch rb(kFrameGaits);
        for (auto& t : g_tracked) {
            bool ok = false;
            const uint16_t m = read_gaits(t.eid, &ok);
            if (!ok) continue;
            SentGaits& s = g_sentGaits[lower(t.name.c_str())];
            if (!s.have && m == 0 && !g_resync) { s.have = true; s.at = now; continue; }         // first sight, no gait: nothing to say
            const bool changed = !s.have || s.m != m;
            if (!K::send_due(changed || g_resync, m == 0, now - s.at)) continue;
            if (changed && (m || s.have)) logf("WO143-GAIT capture %s: mask 0x%03X", t.name.c_str(), m);
            s.m = m; s.at = now; s.have = true;
            rb.room(2 + t.name.size() + 2);
            put_name(rb.out, t.name);
            rb.out.push_back(static_cast<uint8_t>(m & 0xFF)); rb.out.push_back(static_cast<uint8_t>(m >> 8));
            ++rb.count; c_gaitRows.fetch_add(1);
        }
        rb.flush();
    }
    // looks, every 0.5 s, NPCs near a player only, a few changes a second
    if ((cap & kBitLooks) && g_lookArmed && now - g_lastLooks >= K::kLookPeriodS) {
        g_lastLooks = now;
        std::vector<std::array<float, 3>> players;
        player_positions(&players);
        RowBatch rb(kFrameLooks);
        for (auto& t : g_tracked) {
            void* e = engine::entity_by_id(t.eid);
            float p[3];
            if (!e || !engine::entity_world_pos(e, p)) continue;
            bool isNear = false;
            for (auto& q : players) {
                const float dx = p[0] - q[0], dy = p[1] - q[1], dz = p[2] - q[2];
                if (dx * dx + dy * dy + dz * dz <= K::kLookRangeM * K::kLookRangeM) { isNear = true; break; }
            }
            SentLook& s = g_sentLooks[lower(t.name.c_str())];
            uint8_t kind = K::kTargetNone; std::string target;
            if (isNear) {
                void* lt = look_component(t.eid);
                uint32_t k = 0; uint64_t w = 0;
                if (lt && rd32(lt, kLookKind, &k) && k == K::kLookEntity && rd64(lt, kLookWuid, &w)) look_identity(w, &kind, &target);
            }
            if (kind == K::kTargetNpc && lower(target.c_str()) == lower(t.name.c_str())) { kind = K::kTargetNone; target.clear(); }
            const bool changed = !s.have ? kind != K::kTargetNone : (s.kind != kind || s.target != target);
            if (!changed && !(g_resync && kind != K::kTargetNone)) continue;
            if (!K::look_allowed(g_lookBudget, now)) break;
            s.kind = kind; s.target = target; s.at = now; s.have = true;
            rb.room(3 + t.name.size() + target.size());
            put_name(rb.out, t.name);
            rb.out.push_back(kind);
            put_name(rb.out, target);
            ++rb.count; c_lookRows.fetch_add(1);
        }
        rb.flush();
    }
    g_resync = false;
}

// ---------------------------------------------------------------------------
// Host: one-shots, caught at RequestStateChange (any thread) and sent from the
// main thread for the tracked NPCs
// ---------------------------------------------------------------------------
struct Captured { void* ctx; bool anim; char vft[48]; char frag[96]; char tags[160]; uint64_t align; char name[64]; };
std::mutex g_capMu;
std::vector<Captured> g_cap;
std::atomic<bool> g_capLog{false};
std::unordered_map<uint32_t, double> g_lastShot;   // per entity
K::OneShotBudget g_shotBudget;

const char kOurRequester[] = "KCDMP one-shot";

bool on_request(void* ctx, void* req) {
    c_hooked.fetch_add(1, std::memory_order_relaxed);
    const bool wantShots = (g_capture.load(std::memory_order_relaxed) & kBitOneShots) != 0;
    const bool research = g_capLog.load(std::memory_order_relaxed);
    if (!wantShots && !research) return false;
    void* act = nullptr;
    if (!req || !rd(req, kReqAction, &act) || !act) return false;
    const bool anim = is_a(act, g_vftAnimAction);
    if (!anim && !research) return false;
    Captured c{};
    c.ctx = ctx; c.anim = anim;
    vwhere(act, c.vft, sizeof c.vft);
    if (anim) {
        c_animSeen.fetch_add(1, std::memory_order_relaxed);
        void* f = nullptr; void* tg = nullptr;
        if (rd(act, kAnimFragment, &f)) rdstr(static_cast<const char*>(f), c.frag, sizeof c.frag);
        if (rd(act, kAnimTags, &tg)) rdstr(static_cast<const char*>(tg), c.tags, sizeof c.tags);
        rd64(act, kAnimAlign, &c.align);
    }
    void* nm = nullptr;
    if (rd(req, kReqName, &nm)) rdstr(static_cast<const char*>(nm), c.name, sizeof c.name);
    std::lock_guard<std::mutex> lk(g_capMu);
    if (g_cap.size() < 256) g_cap.push_back(c);
    return false;   // always run the original
}

std::string entity_label(uint64_t wuid) {
    if (!wuid) return "-";
    void* e = X::entity_of(wuid);
    const char* n = e ? engine::entity_name(e) : nullptr;
    char b[96]; rdstr(n ? n : "?", b, sizeof b);
    return b;
}

void drain_captured(double now) {
    std::vector<Captured> v;
    { std::lock_guard<std::mutex> lk(g_capMu); v.swap(g_cap); }
    if (v.empty()) return;
    const bool wantShots = (g_capture.load() & kBitOneShots) != 0;
    uint64_t noAlign = 0; rd64(g_invalidAlign, 0, &noAlign);
    for (auto& c : v) {
        void* obj = c.ctx ? static_cast<char*>(c.ctx) - kObjCtx : nullptr;
        uint64_t w = 0; if (obj) rd64(obj, kObjWuid, &w);
        void* ent = w ? X::entity_of(w) : nullptr;
        const uint32_t eid = ent ? engine::entity_id(ent) : 0;
        if (g_capLog.load())
            logf("WO143-R request by %s (npc wuid 0x%016llX): action %s frag='%s' tags='%s' align=%s requester='%s'", entity_label(w).c_str(),
                 static_cast<unsigned long long>(w), c.vft, c.frag, c.tags, entity_label(c.align).c_str(), c.name);
        if (!wantShots || !c.anim || !c.frag[0] || !eid || !g_trackedEids.count(eid)) continue;
        if (!std::strcmp(c.name, kOurRequester)) continue;          // never echo what this DLL played
        double& last = g_lastShot[eid];
        if (!K::oneshot_allowed(g_shotBudget, last, now)) { c_shotsThrottled.fetch_add(1); continue; }
        last = now;
        char nb[80]; rdstr(engine::entity_name(ent) ? engine::entity_name(ent) : "", nb, sizeof nb);
        const bool aligned = c.align && c.align != noAlign;
        const uint64_t alignGuid = aligned ? X::guid_of(c.align) : 0;
        if (aligned && !alignGuid) continue;                        // an object the other machine cannot name
        std::vector<uint8_t> out{kFrameOneShot};
        put_name(out, nb);
        put_name(out, c.frag, 95);
        put_name(out, c.tags, 159);
        for (int i = 0; i < 8; ++i) out.push_back(static_cast<uint8_t>(alignGuid >> (8 * i)));
        out.push_back(aligned ? 1 : 0);
        if (g_frame) g_frame(out.data(), static_cast<uint16_t>(out.size()));
        c_shotsOut.fetch_add(1);
        logf("WO143-SHOT capture %s '%s' tags '%s'%s%s", nb, c.frag, c.tags, aligned ? " at " : "", aligned ? entity_label(c.align).c_str() : "");
    }
}

// ---------------------------------------------------------------------------
// Apply: one-shots through the NPC-state request (PlayerStateHandler's shape)
// ---------------------------------------------------------------------------
// The scriptbind's parameter block (C_PlayerStateHandlerScriptBind::PlayAnimationAction's
// locals, read by the builder at +0x18CABA0: [0] fragment, [1] tags, [2] align,
// [3] resource override, [4] slave entity id, [5] trigger id, [6] trigger point,
// [7], [8], +0x44).
struct AnimParams {
    const char* fragment;      // +0x00 CryString
    const char* tags;          // +0x08 CryString
    uint64_t    align;         // +0x10 WUID (the invalid WUID = no alignment)
    const char* resource;      // +0x18 CryString ("" = the action's own)
    uint32_t    slaveEid;      // +0x20
    uint32_t    pad0;
    uint64_t    trigger;       // +0x28
    const char* triggerPoint;  // +0x30 CryString
    uint64_t    u38;           // +0x38
    uint32_t    u40;           // +0x40
    uint8_t     u44;           // +0x44
    uint8_t     pad1[3];
    const char* s48;           // +0x48 CryString
    uint8_t     b50, b51, b52; // +0x50
    uint8_t     pad2[5];
};
static_assert(sizeof(AnimParams) == 0x58, "the scriptbind's block");

// A one-shot this DLL started: until its request's callback (or a limit), a
// paused body's NPC-state machine is ticked by this DLL, and an aligned one
// holds the writer off the body.
struct InFlight {
    uint32_t eid = 0; uint32_t seq = 0; int reqId = -1;
    std::string name; std::string frag;
    double started = 0; bool done = false; uint8_t result = 0xFF;
    bool heldByUs = false; bool ticked = false; uint32_t ticks = 0;
    bool guard = false; float from[3]{};   // an avatar's aligned entry: undone if it carries the body away
    std::string kept;                      // what its search state held (the seat, the object, the tools)
};
constexpr uint8_t kResultMovedAway = 0xFD;   // the guard stopped it
constexpr float   kGuardM = 2.5f;
std::unordered_set<uint32_t> g_minigameHolds;   // bodies op 9 holds (only these are released by op 9)
std::vector<InFlight> g_flight;
uint32_t g_seq = 0;

void on_shot_done(uint32_t eid, uint32_t seq, unsigned int id, uint8_t result) {
    for (auto& f : g_flight)
        if (f.eid == eid && f.seq == seq) { f.done = true; f.result = result; f.reqId = static_cast<int>(id); return; }
}
// The request's callback: small enough for std::function's inline storage, so
// the engine's copies call back into this DLL's code only (MSVC ABI, as the
// engine's own C_PlayerStateHandler binder). Called on the main thread by the
// context's pre-update (+0x18887C0), with the result as a 1-byte enum.
struct DoneFn {
    uint32_t eid; uint32_t seq;
    void operator()(unsigned int id, int res) const { on_shot_done(eid, seq, id, static_cast<uint8_t>(res & 0xFF)); }
};
using ResultFn = std::function<void(unsigned int, int)>;
static_assert(sizeof(ResultFn) == 0x40, "MSVC std::function: 7 words of storage + the impl pointer");

struct SP { void* p = nullptr; void* ctrl = nullptr; };
struct ReqBuf { alignas(16) uint8_t b[kReqSize + 0x20]; };

int call_request(void* ctx, void* req) {
    int id = -1;
    if (!fcall(g_reqChange, &id, ctx, req)) return -2;
    return id;
}

// Play fragment/tags on the body with this entity id, the way PlayAnimationAction
// does it for the player; action == false: a request with no extra action (ends
// whatever one-shot or loop the body plays). alignWuid 0 = in place.
int request_on(uint32_t eid, bool action, const char* fragment, const char* tags, uint64_t alignWuid, uint32_t seq, char* why, size_t whyN,
               std::string* kept = nullptr) {
    why[0] = 0;
    if (!g_animArmed) { _snprintf_s(why, whyN, _TRUNCATE, "not armed"); return -3; }
    void* ctx = X::context_of(eid);
    void* obj = npc_object(eid);
    if (!ctx || !obj) { _snprintf_s(why, whyN, _TRUNCATE, "no NPC-state context"); return -3; }
    uint64_t noAlign = 0, noTrig = 0;
    rd64(g_invalidAlign, 0, &noAlign); rd64(g_invalidTrigger, 0, &noTrig);
    const char* empty = intern("");
    const char* nm = intern(kOurRequester);
    SP act{}, ev{};
    uint64_t align = alignWuid ? alignWuid : noAlign;
    if (action) {
        const char* f = intern(fragment ? fragment : "");
        const char* t = intern(tags ? tags : "");
        if (!f || !t || !empty) { _snprintf_s(why, whyN, _TRUNCATE, "text pool full"); return -3; }
        AnimParams prm{};
        prm.fragment = f; prm.tags = t; prm.align = align;
        prm.resource = empty; prm.trigger = noTrig; prm.triggerPoint = empty; prm.s48 = empty;
        void* r = nullptr;
        if (!fcall(g_mkAnimAction, &r, static_cast<void*>(nullptr), static_cast<void*>(&act), static_cast<void*>(&prm)) || !act.p) {
            _snprintf_s(why, whyN, _TRUNCATE, "the anim action was not created (the action database template)");
            return -4;
        }
        if (!fcall(g_mkEventCtx, &r, static_cast<void*>(&ev)) || !ev.p) {
            X::release(act.ctrl);
            _snprintf_s(why, whyN, _TRUNCATE, "no anim-event context");
            return -4;
        }
        uint64_t npcW = 0; rd64(obj, kObjWuid, &npcW);
        wr64(ev.p, 0, npcW); wr64(ev.p, 8, align);
    }
    ReqBuf rb{};
    std::memset(rb.b, 0, sizeof rb.b);
    uint8_t* req = rb.b;
    ResultFn* cb = new (req) ResultFn(DoneFn{eid, seq});
    req[0x40] = 0;                                        // urgency (the player's)
    if (act.p) {
        std::memcpy(req + kReqAction, &act.p, 8); std::memcpy(req + kReqAction + 8, &act.ctrl, 8);
        ilock_add(static_cast<char*>(act.ctrl) + 8, 1);   // the request's own reference
        req[0x5C] = 1; req[0x5D] = 1;
        std::memcpy(req + kReqEventCtx, &ev.p, 8); std::memcpy(req + kReqEventCtx + 8, &ev.ctrl, 8);
        ilock_add(static_cast<char*>(ev.ctrl) + 8, 1);
    }
    void* rr = nullptr;
    fcall(g_initReqBlock, &rr, static_cast<void*>(req + kReqBlock));
    std::memcpy(req + kReqName, &nm, 8);
    // what the body keeps through it (its seat, its object, its tools): its search
    // state, filled right before the request as the game's own requesters do
    std::string keptText;
    if (X::prepare_request(eid, &keptText)) c_prepared.fetch_add(1, std::memory_order_relaxed);
    if (kept) *kept = keptText;
    const int id = call_request(ctx, req);
    // our request goes: the callback, the references it held, what the block may hold
    cb->~ResultFn();
    if (act.p) { X::release(act.ctrl); X::release(ev.ctrl); }
    void* impl2 = nullptr; std::memcpy(&impl2, req + kReqBlock + 0xA0, 8);
    if (impl2) fcall_void(vslot(impl2, 0x20), impl2, impl2 != static_cast<void*>(req + kReqBlock + 0x68));
    void* blkCtrl = nullptr; std::memcpy(&blkCtrl, req + kReqBlock + 0x40, 8);
    if (blkCtrl) X::release(blkCtrl);
    if (act.p) { X::release(act.ctrl); X::release(ev.ctrl); }   // ours
    if (id < 0) _snprintf_s(why, whyN, _TRUNCATE, id == -2 ? "the request faulted" : "the request was refused (-1)");
    return id;
}

bool body_paused(uint32_t eid) {
    void* e = engine::entity_by_id(eid);
    uint64_t w = 0; int st = -1, mk = -1;
    return e && actions::brain_state(e, &w, &st, &mk) && st > 0;
}

// Start a one-shot (or, fragment empty, end the running one) on a body.
int start_shot(const std::string& name, const char* frag, const char* tags, uint64_t alignWuid, bool holdBody, char* why, size_t whyN,
               bool guard = false) {
    void* e = X::entity_named(name.c_str());
    const uint32_t eid = e ? engine::entity_id(e) : 0;
    if (!eid) { _snprintf_s(why, whyN, _TRUNCATE, "no body named %s here", name.c_str()); return -3; }
    const uint32_t seq = ++g_seq;
    InFlight f{};
    f.eid = eid; f.seq = seq; f.name = name; f.frag = frag ? frag : ""; f.started = now_s();
    g_flight.push_back(f);   // before the call: a synchronous callback finds it
    std::string kept;
    const int id = request_on(eid, frag && *frag, frag, tags, alignWuid, seq, why, whyN, &kept);
    InFlight& me = g_flight.back();
    if (id < 0) { g_flight.pop_back(); return id; }
    me.reqId = id; me.kept = kept;
    me.ticked = body_paused(eid) && g_tickArmed;
    if (guard) { me.guard = engine::entity_world_pos(e, me.from); }
    if (holdBody && !npcdrive::activity_held(eid)) { npcdrive::set_activity_hold(eid, true); me.heldByUs = true; }
    return id;
}

// A paused body's NPC-state machine, ticked like C_NPC::Update does it (its
// three NPC-state steps only -- the brain stays paused).
void tick_body(uint32_t eid) {
    void* obj = npc_object(eid);
    void* ctx = X::context_of(eid);
    if (!obj || !ctx) return;
    fcall_void(g_ctxPreUpdate, ctx);
    fcall_void(g_execUpdate, static_cast<void*>(static_cast<char*>(obj) + kObjExec));
    fcall_void(g_ctxUpdate, ctx);
}

void flight_tick(double now) {
    for (auto it = g_flight.begin(); it != g_flight.end();) {
        InFlight& f = *it;
        // the limit bounds this DLL's own ticking of a paused copy; a body the game ticks (an avatar) keeps
        // its request until the game ends it (a minigame loop runs until the next request stops it)
        const bool expired = f.ticked ? now - f.started > K::kOneShotTickLimitS : now - f.started > K::kOneShotKeepS;
        if (!f.done && !expired && f.ticked && engine::entity_by_id(f.eid)) { tick_body(f.eid); ++f.ticks; c_ticks.fetch_add(1); }
        if (!f.done && f.guard) {
            float p[3];
            void* e = engine::entity_by_id(f.eid);
            if (e && engine::entity_world_pos(e, p)) {
                const float dx = p[0] - f.from[0], dy = p[1] - f.from[1], dz = p[2] - f.from[2];
                if (dx * dx + dy * dy + dz * dz > kGuardM * kGuardM) {
                    logf("WO143-SHOT %s '%s' carried the body %.1f m from where it stood -- undone (the stream takes it back)", f.name.c_str(),
                         f.frag.c_str(), std::sqrt(dx * dx + dy * dy + dz * dz));
                    f.done = true; f.result = kResultMovedAway;
                    char why[96];
                    request_on(f.eid, false, "", "", 0, ++g_seq, why, sizeof why);
                    if (g_minigameHolds.erase(f.eid)) npcdrive::set_activity_hold(f.eid, false);
                }
            }
        }
        if (f.done || expired || !engine::entity_by_id(f.eid)) {
            if (f.heldByUs) npcdrive::set_activity_hold(f.eid, false);
            if (f.done) { (f.result == 2 ? c_shotsDone : c_shotsFailed).fetch_add(1); }
            else c_shotsFailed.fetch_add(1);
            logf("WO143-SHOT %s '%s' %s (request %d, result %u, %.1f s%s%s%s)", f.name.c_str(), f.frag.empty() ? "(stop)" : f.frag.c_str(),
                 f.done ? "finished" : "gave up", f.reqId, f.result, now - f.started,
                 f.ticked ? (", on a paused body, ticked " + std::to_string(f.ticks) + "x").c_str() : "",
                 f.kept.empty() ? "" : ", keeping ", f.kept.c_str());
            if (g_frame) {
                std::vector<uint8_t> out{kFrameShotDone};
                put_name(out, f.name);
                out.push_back(f.done ? f.result : 0xFE);
                const int32_t id = f.reqId;
                for (int i = 0; i < 4; ++i) out.push_back(static_cast<uint8_t>(static_cast<uint32_t>(id) >> (8 * i)));
                g_frame(out.data(), static_cast<uint16_t>(out.size()));
            }
            it = g_flight.erase(it);
            continue;
        }
        ++it;
    }
}

// A paused copy's NPC state does not run: a take WO-141's apply started (a tool
// into a hand) never settles there -- the game logs "couldn't reach the loaded
// state in 3 updates" and the hand item is left half taken. It settles under
// the same three NPC-state steps a one-shot gets, for kSettleS after the apply.
std::unordered_map<uint32_t, double> g_settleUntil;
std::atomic<bool> g_settleOn{true};
std::atomic<uint64_t> c_settleTicks{0};
std::unordered_set<uint32_t> g_researchHolds;

void on_hand_applied(uint32_t eid) {
    if (!g_settleOn.load() || !g_tickArmed || !eid || !body_paused(eid)) return;
    g_settleUntil[eid] = now_s() + K::kSettleS;
}

bool flying_ticked(uint32_t eid) {
    for (const auto& f : g_flight) if (f.eid == eid && f.ticked && !f.done) return true;
    return false;
}
std::unordered_set<uint32_t> g_hoeing;   // (the gaits' section keeps it)

void settle_tick(double now) {
    // WO-143 J2: a hoer's hoe goes wrong after the hoeing walk on a paused copy
    // (it flips between two grips every frame once she stands) and comes right
    // under six seconds of the NPC-state steps: a copy the host shows hoeing is
    // ticked while it hoes, and kHoeSettleS after
    if (g_tickArmed && g_settleOn.load())
        for (uint32_t eid : g_hoeing) {
            if (!engine::entity_by_id(eid) || !body_paused(eid) || flying_ticked(eid)) continue;
            tick_body(eid); c_settleTicks.fetch_add(1, std::memory_order_relaxed);
        }
    for (auto it = g_settleUntil.begin(); it != g_settleUntil.end();) {
        const uint32_t eid = it->first;
        if (now > it->second || !engine::entity_by_id(eid)) { it = g_settleUntil.erase(it); continue; }
        if (!flying_ticked(eid) && !g_hoeing.count(eid)) { tick_body(eid); c_settleTicks.fetch_add(1, std::memory_order_relaxed); }
        ++it;
    }
}

// ---------------------------------------------------------------------------
// Apply: gaits (the copy's soul; only pairs this DLL set)
// ---------------------------------------------------------------------------
struct GaitState { uint16_t host = 0; uint16_t weSet = 0; };
std::unordered_map<std::string, GaitState> g_gaits;   // lower name

// Copies the host shows hoeing (forcedHoeing, bit 0): their work is a creep
// along the row, well under the gait writer's 0.1 m/s walking floor (H1: 4.5 m
// in about 74 s). Read as standing, the copy would stand with its hoe; the gait
// writer keeps them at the walking class while the host's mask says hoeing.

void apply_gaits(const std::string& name, uint16_t host, uint16_t* setOut, uint16_t* clearedOut) {
    *setOut = *clearedOut = 0;
    GaitState& g = g_gaits[lower(name.c_str())];
    g.host = host;
    void* e = X::entity_named(name.c_str());
    void* soul = e ? hits::soul_of_eid(engine::entity_id(e)) : nullptr;
    if (e) {
        const uint32_t eid = engine::entity_id(e);
        if (K::hoeing(host)) g_hoeing.insert(eid);
        else if (g_hoeing.erase(eid) && g_tickArmed && body_paused(eid)) g_settleUntil[eid] = now_s() + K::kHoeSettleS;   // the hoe settles
    }
    if (!soul) return;
    bool ok = false;
    const uint16_t has = read_gaits(engine::entity_id(e), &ok);
    if (!ok) return;
    const K::GaitPlan plan = K::gait_plan(host, has, g.weSet);
    for (int i = 0; i < K::kGaitCount; ++i) {
        const uint16_t bit = static_cast<uint16_t>(1u << i);
        if (plan.set & bit) {
            if (sctx::set_soul_context_quiet(soul, K::gait_name(i), true) == 1) { g.weSet |= bit; *setOut |= bit; c_gaitSet.fetch_add(1); }
        } else if (plan.clear & bit) {
            if (sctx::set_soul_context_quiet(soul, K::gait_name(i), false) == 1) { g.weSet = static_cast<uint16_t>(g.weSet & ~bit); *clearedOut |= bit; c_gaitClear.fetch_add(1); }
        }
    }
    // a context the copy's world set on its own, which the host's NPC also has, is left alone and never counted as ours
    g.weSet = static_cast<uint16_t>(g.weSet & (host | ~plan.clear));
    if (*setOut || *clearedOut) {
        std::string s;
        for (int i = 0; i < K::kGaitCount; ++i) {
            const uint16_t bit = static_cast<uint16_t>(1u << i);
            if (*setOut & bit) s += std::string(" +") + K::gait_name(i);
            if (*clearedOut & bit) s += std::string(" -") + K::gait_name(i);
        }
        logf("WO143-GAIT %s:%s (the host's mask 0x%03X)", name.c_str(), s.c_str(), host);
    }
}

int clear_all_gaits(const char* why) {
    int n = 0;
    if (g_tickArmed) for (uint32_t eid : g_hoeing) if (body_paused(eid)) g_settleUntil[eid] = now_s() + K::kHoeSettleS;
    g_hoeing.clear();
    for (auto& kv : g_gaits) {
        if (!kv.second.weSet) continue;
        void* e = X::entity_named(kv.first.c_str());
        void* soul = e ? hits::soul_of_eid(engine::entity_id(e)) : nullptr;
        for (int i = 0; i < K::kGaitCount && soul; ++i)
            if (kv.second.weSet & (1u << i)) { sctx::set_soul_context_quiet(soul, K::gait_name(i), false); ++n; }
        kv.second.weSet = 0;
    }
    if (n) logf("WO143-GAIT %d context(s) this DLL set cleared (%s)", n, why);
    g_gaits.clear();
    return n;
}

// ---------------------------------------------------------------------------
// Apply: hands -- WO-141's reconcile carries them; a class the copy does not
// own goes to the agent (the mod gives it a temporary one)
// ---------------------------------------------------------------------------
std::unordered_map<std::string, double> g_needAsked;   // "name|class" -> when
void on_need_item(const char* name, const K::ClassId& cls) {
    const std::string key = lower(name) + "|" + X::class_id_text(cls);
    const double now = now_s();
    auto it = g_needAsked.find(key);
    if (it != g_needAsked.end() && now - it->second < 10.0) return;
    g_needAsked[key] = now;
    c_needItem.fetch_add(1);
    logf("WO143-HANDS %s does not own a %s: asking the mod for a temporary one", name, X::class_id_text(cls).c_str());
    if (!g_frame) return;
    std::vector<uint8_t> out{kFrameNeedItem};
    put_name(out, name);
    out.insert(out.end(), cls.b, cls.b + 16);
    g_frame(out.data(), static_cast<uint16_t>(out.size()));
}

// ---------------------------------------------------------------------------
// Research verbs (kcdmp-w143-<dll name>.txt beside kcd.log; what the file holds
// when the game starts is a baseline and never runs)
// ---------------------------------------------------------------------------
uint32_t eid_named(const char* name) {
    if (!name || !*name) return 0;
    if (!std::strcmp(name, "player")) return 0x7777;
    void* e = X::entity_named(name);
    return e ? engine::entity_id(e) : 0;
}

void cmd_hands(const char* name) {
    const uint32_t eid = eid_named(name);
    void* ctx = eid ? X::context_of(eid) : nullptr;
    if (!ctx) { logf("WO143-R hands %s: no context", name); return; }
    for (int which = 0; which < 2; ++which) {
        void* st = which ? X::loaded_state(ctx) : X::current_state(ctx);
        for (unsigned s = 1; s <= 2; ++s) {
            void* el = nullptr; char v[64] = "-";
            if (st && X::state_element(st, s, &el) && el) vwhere(el, v, sizeof v);
            uint64_t w = 0; uint8_t hand = 0; uint8_t cls[16]{}; bool have = false;
            if (el && (is_a(el, g_vftHand) || is_a(el, g_vftHandReq))) {
                rd64(el, kHandWuid, &w); rd8(el, kHandType, &hand);
                if (void* item = actions::item_by_wuid(w)) have = actions::item_class_id(item, cls);
            }
            logf("WO143-R hands %s %s[%u] %s wuid=0x%016llX hand=%u class=%s bytes %s", name, which ? "loaded" : "current", s, v,
                 static_cast<unsigned long long>(w), hand, have ? guid_text(cls).c_str() : "-", el ? hexdump(el, 0x50).c_str() : "-");
        }
        logf("WO143-R hands %s %s text=[%s]", name, which ? "loaded" : "current", st ? X::describe_state(st).c_str() : "-");
    }
}

void cmd_inv(const char* name) {
    const uint32_t eid = eid_named(name);
    void* soul = eid ? hits::soul_of_eid(eid) : nullptr;
    void* inv = soul ? actions::soul_inventory(soul) : nullptr;
    if (!inv) { logf("WO143-R inv %s: no inventory (soul %p)", name, soul); return; }
    void* items[64];
    const int n = actions::inventory_items(inv, items, 64);
    logf("WO143-R inv %s: %d item(s)", name, n);
    for (int i = 0; i < n; ++i) {
        uint8_t c[16]; const bool ok = actions::item_class_id(items[i], c);
        logf("WO143-R inv %s [%d] wuid=0x%016llX class=%s", name, i, static_cast<unsigned long long>(actions::item_wuid(items[i])),
             ok ? guid_text(c).c_str() : "-");
    }
}

void cmd_ctx(const char* name) {
    const uint32_t eid = eid_named(name);
    bool ok = false;
    const uint16_t m = eid ? read_gaits(eid, &ok) : 0;
    logf("WO143-R ctx %s: %s mask 0x%03X", name, ok ? "read" : "NOT READ", m);
}

void cmd_look(const char* name) {
    const uint32_t eid = eid_named(name);
    void* lt = look_component(eid);
    if (!lt) { logf("WO143-R look %s: no look-target component", name); return; }
    uint32_t kind = 0; uint64_t w = 0;
    rd32(lt, kLookKind, &kind); rd64(lt, kLookWuid, &w);
    uint8_t tk = 0; std::string tn;
    if (kind == K::kLookEntity) look_identity(w, &tk, &tn);
    logf("WO143-R look %s: kind=%u wuid=0x%016llX (%s) -> target kind %u '%s' raw %s", name, kind, static_cast<unsigned long long>(w),
         entity_label(w).c_str(), tk, tn.c_str(), hexdump(static_cast<char*>(lt) + 0x30, 0x20).c_str());
}

void cmd_anim(const char* name, const char* rest) {
    char frag[128]{}, tags[160]{}, obj[256]{};
    sscanf_s(rest, "%127s %159s %255[^\n]", frag, 128u, tags, 160u, obj, 256u);
    uint64_t alignW = 0;
    if (obj[0] && std::strcmp(obj, "-")) { void* o = X::entity_named(obj); alignW = o ? actions::entity_wuid(o) : 0; }
    char why[128];
    const bool stop = !std::strcmp(frag, "-");
    const int id = start_shot(name, stop ? "" : frag, std::strcmp(tags, "-") ? tags : "", alignW, alignW != 0, why, sizeof why);
    logf("WO143-R anim %s '%s' tags '%s' align %s (0x%016llX): request %d %s", name, frag, tags, obj[0] ? obj : "-",
         static_cast<unsigned long long>(alignW), id, why);
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
    std::string path = std::string(cwd) + (cwd[std::strlen(cwd) - 1] == '\\' ? "" : "\\") + "kcdmp-w143-" + self + ".txt";
    FILE* f = nullptr;
    if (fopen_s(&f, path.c_str(), "rb") != 0 || !f) { g_baseline = true; return; }
    std::string all; char buf[4096]; size_t got;
    while ((got = fread(buf, 1, sizeof buf, f)) > 0) all.append(buf, got);
    fclose(f);
    if (!g_baseline) { g_baseline = true; g_last = all; logf("WO143-RESEARCH %s present at start: baseline, not run", path.c_str()); return; }
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
        logf("WO143-RESEARCH line: %s", line.c_str());
        if (k >= 2 && !std::strcmp(verb, "hands")) cmd_hands(a1);
        else if (k >= 2 && !std::strcmp(verb, "inv")) cmd_inv(a1);
        else if (k >= 2 && !std::strcmp(verb, "ctx")) cmd_ctx(a1);
        else if (k >= 2 && !std::strcmp(verb, "look")) cmd_look(a1);
        else if (k >= 3 && !std::strcmp(verb, "anim")) cmd_anim(a1, rest);
        else if (k >= 3 && !std::strcmp(verb, "sethands")) {   // sethands <npc> <left guid|-> <right guid|->
            char l[64]{}, r[64]{}; sscanf_s(rest, "%63s %63s", l, 64u, r, 64u);
            K::Hands h; if (std::strcmp(l, "-")) parse_guid(l, h.left.b); if (std::strcmp(r, "-")) parse_guid(r, h.right.b);
            X::set_hands(a1, h);
            logf("WO143-R sethands %s:%s (WO-141's apply %s)", a1, X::hand_classes_text(h).c_str(), X::apply_on() ? "on" : "OFF");
        }
        else if (k >= 3 && !std::strcmp(verb, "setgaits")) {   // setgaits <npc> <mask hex>
            uint16_t s = 0, c = 0; apply_gaits(a1, static_cast<uint16_t>(strtoul(rest, nullptr, 16)), &s, &c);
            logf("WO143-R setgaits %s: set 0x%03X cleared 0x%03X", a1, s, c);
        }
        else if (k >= 2 && !std::strcmp(verb, "guid")) {   // guid <entity>: its level entity GUID and this machine's WUID
            void* e = X::entity_named(a1);
            logf("WO143-R guid %s: entity %u guid 0x%016llX wuid 0x%016llX", a1, e ? engine::entity_id(e) : 0,
                 static_cast<unsigned long long>(e ? engine::entity_guid(e) : 0), static_cast<unsigned long long>(e ? actions::entity_wuid(e) : 0));
        }
        else if (k >= 3 && !std::strcmp(verb, "tick")) {   // tick <npc> <seconds>: its NPC state runs under this DLL's tick
            const uint32_t eid = eid_named(a1);
            const double secs = atof(rest);
            if (eid && secs > 0) g_settleUntil[eid] = now_s() + (secs > 30 ? 30 : secs);
            logf("WO143-R tick %s (eid 0x%X) for %.1f s (paused %d, tick %s)", a1, eid, secs, eid ? (body_paused(eid) ? 1 : 0) : -1, g_tickArmed ? "armed" : "NOT armed");
        }
        else if (k >= 2 && !std::strcmp(verb, "settle")) { g_settleOn = atoi(a1) != 0; logf("WO143-R settle after hand applies %s", g_settleOn.load() ? "on" : "off"); }
        else if (k >= 2 && !std::strcmp(verb, "retake")) { logf("WO143-R retake %s: %s", a1, X::retake_hands(a1) ? "put away (the reconcile takes them again)" : "no hands wanted for it"); }
        else if (k >= 3 && !std::strcmp(verb, "hold")) {   // hold <npc> 0|1: the per-frame writer stays off it (research)
            const uint32_t eid = eid_named(a1);
            const bool on = atoi(rest) != 0;
            if (eid) { npcdrive::set_activity_hold(eid, on); if (on) g_researchHolds.insert(eid); else g_researchHolds.erase(eid); }
            logf("WO143-R hold %s (eid 0x%X) %s", a1, eid, on ? "on" : "off");
        }
        else if (k >= 2 && !std::strcmp(verb, "hook")) { g_capLog = atoi(a1) != 0; logf("WO143-R request log %s (hooked %llu so far)", g_capLog.load() ? "on" : "off", static_cast<unsigned long long>(c_hooked.load())); }
        else logf("WO143-RESEARCH unknown or incomplete: %s", line.c_str());
    }
}

std::string status_text() {
    char b[480];
    _snprintf_s(b, sizeof b, _TRUNCATE,
                "hands %s gaits %s oneshots %s tick %s hook %s looks %s | capture 0x%02X apply 0x%02X | out hands %llu gaits %llu looks %llu shots %llu (throttled %llu) | in shots %llu done %llu failed %llu kept %llu ticks %llu settle %llu gait+ %llu gait- %llu need-item %llu inflight %zu",
                X::hands_armed() ? "armed" : "NOT ARMED", sctx::isolation_enabled() ? "armed" : "NOT ARMED", g_animArmed ? "armed" : "NOT ARMED",
                g_tickArmed ? "armed" : "NOT ARMED", g_hookArmed ? "on" : "off", g_lookArmed ? "armed" : "NOT ARMED", g_capture.load(), g_apply.load(),
                static_cast<unsigned long long>(c_handRows.load()), static_cast<unsigned long long>(c_gaitRows.load()),
                static_cast<unsigned long long>(c_lookRows.load()), static_cast<unsigned long long>(c_shotsOut.load()),
                static_cast<unsigned long long>(c_shotsThrottled.load()), static_cast<unsigned long long>(c_shotsIn.load()),
                static_cast<unsigned long long>(c_shotsDone.load()), static_cast<unsigned long long>(c_shotsFailed.load()),
                static_cast<unsigned long long>(c_prepared.load()), static_cast<unsigned long long>(c_ticks.load()),
                static_cast<unsigned long long>(c_settleTicks.load()), static_cast<unsigned long long>(c_gaitSet.load()),
                static_cast<unsigned long long>(c_gaitClear.load()), static_cast<unsigned long long>(c_needItem.load()), g_flight.size());
    return b;
}

void release_minigame_holds() {
    for (uint32_t eid : g_minigameHolds) npcdrive::set_activity_hold(eid, false);
    g_minigameHolds.clear();
}

void stop_everything(const char* why) {
    g_capture = 0;
    clear_all_gaits(why);
    release_minigame_holds();
    for (auto& f : g_flight) if (f.heldByUs) npcdrive::set_activity_hold(f.eid, false);
    g_flight.clear();
    g_settleUntil.clear();
    for (uint32_t e : g_researchHolds) npcdrive::set_activity_hold(e, false);
    g_researchHolds.clear();
    g_sentHands.clear(); g_sentGaits.clear(); g_sentLooks.clear(); g_lastShot.clear(); g_needAsked.clear();
}

} // namespace

// ---------------------------------------------------------------------------

void install() {
    g_xg = GetModuleHandleA("XGenAIModule.dll");
    if (!g_xg) { logf("WO143-BUILD XGenAIModule not loaded -- NOT ARMED"); return; }
    char* base = reinterpret_cast<char*>(g_xg);
    g_vftHand         = anchor::find_vftable(g_xg, ".?AVC_HandContentElement@NPCState@xgenaimodule@wh@@");
    g_vftHandReq      = anchor::find_vftable(g_xg, ".?AVC_HandContentElementRequired@NPCState@xgenaimodule@wh@@");
    g_vftAnimAction   = anchor::find_vftable(g_xg, ".?AVC_AnimAction@NPCState@xgenaimodule@wh@@");
    g_vftLookTarget   = anchor::find_vftable(g_xg, ".?AVC_NPCLookTarget@xgenaimodule@wh@@");
    const bool tHand = X::bind_rttr("wh::xgenaimodule::NPCState::HandContentElementRequired", &g_tHandReq);
    g_handArmed = g_vftHand && g_vftHandReq && tHand && X::hands_armed();
    // the request route: each function by its RVA, its exact first bytes, and
    // (RequestStateChange) the string that makes it what it is
    void* rsc = base + kRvaRequestStateChange;
    const char* rscName = anchor::find_cstring(g_xg, "wh::xgenaimodule::NPCState::C_NPCContext::RequestStateChange");
    const bool rscOk = prologue_ok(rsc, kProReqChange, sizeof kProReqChange) && rscName && anchor::function_refs(g_xg, rsc, rscName);
    void* mk = base + kRvaMakeAnimAction; void* ev = base + kRvaMakeEventCtx; void* ib = base + kRvaInitReqBlock;
    const bool mkOk = prologue_ok(mk, kProMakeAnim, sizeof kProMakeAnim);
    const bool evOk = prologue_ok(ev, kProMakeEvCtx, sizeof kProMakeEvCtx);
    const bool ibOk = prologue_ok(ib, kProInitBlock, sizeof kProInitBlock);
    g_reqChange = rscOk ? rsc : nullptr; g_mkAnimAction = mkOk ? mk : nullptr; g_mkEventCtx = evOk ? ev : nullptr; g_initReqBlock = ibOk ? ib : nullptr;
    g_invalidAlign = reinterpret_cast<const uint64_t*>(base + kRvaInvalidAlign);
    g_invalidTrigger = reinterpret_cast<const uint64_t*>(base + kRvaInvalidTrigger);
    g_animArmed = g_reqChange && g_mkAnimAction && g_mkEventCtx && g_initReqBlock && g_vftAnimAction;
    // a paused copy's tick: the three NPC-state steps of C_NPC::Update, each by its first bytes
    void* pu = base + kRvaCtxPreUpdate; void* eu = base + kRvaExecUpdate; void* cu = base + kRvaCtxUpdate;
    const bool puOk = prologue_ok(pu, kProCtxPre, sizeof kProCtxPre), euOk = prologue_ok(eu, kProExecUpd, sizeof kProExecUpd),
               cuOk = prologue_ok(cu, kProCtxUpd, sizeof kProCtxUpd);
    const uint8_t* startUpcoming = anchor::function_by_string(g_xg, "wh::xgenaimodule::NPCState::C_NPCContext::StartUpcomingSearchResult");
    g_ctxPreUpdate = puOk ? pu : nullptr; g_execUpdate = euOk ? eu : nullptr;
    g_ctxUpdate = cuOk && startUpcoming == static_cast<const uint8_t*>(cu) ? cu : nullptr;
    g_tickArmed = g_animArmed && g_ctxPreUpdate && g_execUpdate && g_ctxUpdate;
    if (g_reqChange && g_vftAnimAction) {
        const char* why = nullptr;
        g_hookArmed = inlinehook::install_gate(g_reqChange, kProReqChange, sizeof kProReqChange, &on_request, &why);
        if (!g_hookArmed) logf("WO143-BUILD the one-shot capture hook NOT installed: %s", why ? why : "?");
    }
    // the look target: the NPC object's getter at vtbl[0x210] is `lea rax, [rbx + disp]` -- read the disp
    // out of the function any NPC object's vtable holds (checked again on every read: vftable C_NPCLookTarget)
    if (g_vftLookTarget) {
        void* const* vftNpc = anchor::find_vftable(g_xg, ".?AVC_NPC@xgenaimodule@wh@@");
        void* getter = vftNpc ? vftNpc[kNpcLookGetter / 8] : nullptr;
        g_lookArmed = getter && getter_disp(getter, &g_lookDisp);
    }
    X::set_need_item_callback(&on_need_item);
    X::set_hand_applied_callback(&on_hand_applied);
    logf("WO143-BUILD hands %s (current C_HandContentElement %s, loaded HandContentElementRequired %s), one-shots %s "
         "(RequestStateChange %s, anim action builder %s, event context %s, required block %s, C_AnimAction %s), a paused copy's tick %s, "
         "capture hook %s, look targets %s (+0x%X), gait contexts through the WO-68 manager",
         g_handArmed ? "ARMED" : "NOT ARMED", g_vftHand ? "ok" : "MISSING", (g_vftHandReq && tHand) ? "ok" : "MISSING",
         g_animArmed ? "ARMED" : "NOT ARMED", rscOk ? "ok" : "MISSING", mkOk ? "ok" : "MISSING", evOk ? "ok" : "MISSING",
         ibOk ? "ok" : "MISSING", g_vftAnimAction ? "ok" : "MISSING", g_tickArmed ? "ARMED" : "NOT ARMED",
         g_hookArmed ? "installed" : "NOT installed", g_lookArmed ? "ARMED" : "NOT ARMED", g_lookDisp);
}

void tick() {
    static unsigned n = 0;
    if ((++n % 15) == 0) research_watch();
    const double now = now_s();
    capture_tick(now);
    drain_captured(now);
    flight_tick(now);
    settle_tick(now);
}

void set_frame_callback(FrameFn fn) { g_frame = fn; }

bool activity_locomotion(uint32_t eid) {
    return eid && (g_apply.load(std::memory_order_relaxed) & kBitGaits) && g_hoeing.count(eid) != 0;
}

void on_pipe_closed() {
    stop_everything("the agent is gone");
    g_apply = 0;
    X::clear_all_hands();
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
        case 1: {   // Config [capture][apply] -> [armed]
            if (len != 3 || cap < 1) return kRBadRequest;
            const uint8_t was = g_capture.load(), wasA = g_apply.load();
            g_capture = body[1]; g_apply = body[2];
            if ((wasA & kBitGaits) && !(body[2] & kBitGaits)) clear_all_gaits("mp_activity_gaits off");
            if ((wasA & kBitHands) && !(body[2] & kBitHands)) { const int n = X::clear_all_hands(); if (n) logf("WO143-HANDS mp_hand_items off: %d body/bodies put their tools away", n); }
            if (was != body[1] || wasA != body[2])
                logf("WO143-CONFIG capture 0x%02X apply 0x%02X (1 hands, 2 gaits, 4 one-shots, 8 looks, 16 avatars' minigames)", body[1], body[2]);
            out[0] = static_cast<uint8_t>((g_handArmed ? 1 : 0) | (sctx::isolation_enabled() ? 2 : 0) | (g_animArmed ? 4 : 0) |
                                          (g_hookArmed ? 8 : 0) | (g_lookArmed ? 16 : 0) | (g_tickArmed ? 32 : 0));
            *outLen = 1;
            return kROk;
        }
        case 2: {   // Hands [nameLen][name][L:16][R:16]
            std::string nm; size_t at = 0;
            if (!name_at(1, &nm, &at) || nm.empty() || len != at + 32) return kRBadRequest;
            if (!(g_apply.load() & kBitHands) || !g_handArmed) return kRNotArmed;
            K::Hands h; std::memcpy(h.left.b, body + at, 16); std::memcpy(h.right.b, body + at + 16, 16);
            X::set_hands(nm, h);
            return kROk;
        }
        case 3: {   // Gaits [nameLen][name][mask:2] -> [set:2][cleared:2]
            std::string nm; size_t at = 0;
            if (!name_at(1, &nm, &at) || nm.empty() || len != at + 2 || cap < 4) return kRBadRequest;
            if (!(g_apply.load() & kBitGaits) || !sctx::isolation_enabled()) return kRNotArmed;
            uint16_t s = 0, c = 0;
            apply_gaits(nm, static_cast<uint16_t>(body[at] | (body[at + 1] << 8)), &s, &c);
            out[0] = static_cast<uint8_t>(s); out[1] = static_cast<uint8_t>(s >> 8); out[2] = static_cast<uint8_t>(c); out[3] = static_cast<uint8_t>(c >> 8);
            *outLen = 4;
            return kROk;
        }
        case 4: {   // OneShot [nameLen][name][fragLen][frag][tagsLen][tags][alignGuid:8][flags] -> [requestId:4]
            std::string nm, frag, tags; size_t a1 = 0, a2 = 0, a3 = 0;
            if (!name_at(1, &nm, &a1) || nm.empty() || !name_at(a1, &frag, &a2) || !name_at(a2, &tags, &a3) || len != a3 + 9 || cap < 4) return kRBadRequest;
            const bool avatar = !std::strncmp(nm.c_str(), "kcd2mp_", 7);
            if (!(g_apply.load() & (avatar ? kBitAvatarShots : kBitOneShots)) || !g_animArmed) return kRNotArmed;
            uint64_t g = 0; for (int i = 7; i >= 0; --i) g = (g << 8) | body[a3 + i];
            const uint8_t flags = body[a3 + 8];
            uint64_t alignW = 0;
            // a copy plays its fragment where the stream has it: the host's game aligned the host's NPC there
            // already (J4: aligned at the object, the writer's place kept the entry from ever finishing)
            if (!avatar) g = 0;
            if (g) { alignW = X::wuid_of(g); if (!alignW) { logf("WO143-SHOT %s '%s': the object is not in this world, skipped", nm.c_str(), frag.c_str()); return kRNotFound; } }
            char why[128];
            c_shotsIn.fetch_add(1);
            // a copy is never held for its one-shot: the host's stream already carries the aligned place (J3: the
            // innkeeper, held for an aligned table cleaning, walked off under the tick on her own pending walk and
            // was snapped back); an avatar's aligned minigame entry holds through op 9
            const int id = start_shot(nm, frag.c_str(), tags.c_str(), alignW, false, why, sizeof why, (flags & 2) != 0 && alignW != 0);
            if (id < 0) logf("WO143-SHOT %s '%s' tags '%s' not started: %s", nm.c_str(), frag.c_str(), tags.c_str(), why);
            const uint32_t u = static_cast<uint32_t>(id);
            out[0] = static_cast<uint8_t>(u); out[1] = static_cast<uint8_t>(u >> 8); out[2] = static_cast<uint8_t>(u >> 16); out[3] = static_cast<uint8_t>(u >> 24);
            *outLen = 4;
            return id >= 0 ? kROk : kRFailed;
        }
        case 5: {   // Status -> text
            const std::string s = status_text();
            const size_t n = s.size() < cap ? s.size() : cap;
            std::memcpy(out, s.data(), n); *outLen = n;
            return kROk;
        }
        case 6: {   // Forget (a load): the gait bookkeeping and the running one-shots go (hands are WO-141's Forget)
            const size_t n = g_gaits.size() + g_flight.size();
            for (auto& f : g_flight) if (f.heldByUs) npcdrive::set_activity_hold(f.eid, false);
            g_flight.clear(); g_gaits.clear(); g_needAsked.clear(); g_settleUntil.clear(); g_hoeing.clear();
            release_minigame_holds();
            if (cap >= 2) { out[0] = static_cast<uint8_t>(n); out[1] = static_cast<uint8_t>(n >> 8); *outLen = 2; }
            return kROk;
        }
        case 7: {   // Resync: the host sends every hand, gait and look row again on its next tick
            g_resync = true;
            g_sentLooks.clear();
            return kROk;
        }
        case 9: {   // Hold [nameLen][name][on]: the writer stays off this body while its minigame lasts (only what op 9 set is released)
            std::string nm; size_t at = 0;
            if (!name_at(1, &nm, &at) || nm.empty() || len != at + 1) return kRBadRequest;
            void* e = X::entity_named(nm.c_str());
            const uint32_t eid = e ? engine::entity_id(e) : 0;
            if (!eid) return kRNotFound;
            if (body[at]) {
                if (!npcdrive::activity_held(eid)) { npcdrive::set_activity_hold(eid, true); g_minigameHolds.insert(eid); }
            } else if (g_minigameHolds.erase(eid)) {
                npcdrive::set_activity_hold(eid, false);
            }
            return kROk;
        }
        case 8: {   // Stop [nameLen][name]: a request with no extra action ends the body's running one-shot or loop
            std::string nm; size_t at = 0;
            if (!name_at(1, &nm, &at) || nm.empty() || len != at) return kRBadRequest;
            if (!g_animArmed) return kRNotArmed;
            char why[128];
            const int id = start_shot(nm, "", "", 0, false, why, sizeof why);
            if (id < 0) logf("WO143-SHOT %s stop not requested: %s", nm.c_str(), why);
            return id >= 0 ? kROk : kRFailed;
        }
    }
    return kRBadRequest;
}

} // namespace kcdmp::wo143
