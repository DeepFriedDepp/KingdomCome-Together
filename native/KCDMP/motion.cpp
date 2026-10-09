// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-121 -- movement and combat on native-written bodies. See motion.h.
#include "motion.h"
#include "wo163_rules.h"
#include "fault_guard.h"

#include <windows.h>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include "wo135.h"
#include "wo143.h"
#include "wo143_rules.h"
#include "mannequin_read.h"
#include "npc_drive.h"
#include "rttr_abi.h"
#include <unordered_map>
#include <vector>

#include "anchors.h"
#include "buffs.h"
#include "engine.h"
#include "gait_logic.h"
#include "hook_prologues.h"
#include "inline_hook.h"
#include "log.h"
#include "hits.h"
#include "pe_exports.h"
#include "script_context.h"

namespace kcdmp::motion {
namespace {

// ---- layout (WO-119 s7, re-verified by anchor at install) ----------------------
constexpr size_t kActorSetPseudoSpeed = 0x448;   // C_Actor vftable slot (body: mov rax,[rbx+0x7E8] .. movss [rax+0x18],xmm6)
constexpr size_t kActorGetSoul        = 0x6E0;
constexpr size_t kActorExpHolder      = 0x980;   // returns actor+0x308, the state-expansion holder
// WO-136 Phase 6: the actor's OWN "am I crouched" -- stance component (actor
// +0xAC0 via slot 0xB78) state == 1. What the game itself asks: the player's
// per-frame update feeds its `player_in_crouch` stat from it, RequestJump
// checks it. The crouch key (toggle_crouch, EntityModule C_EntityActions+0x50)
// goes through the actor action system (C_ActorActionCrouch), which changes
// this stance and never the expansion's desire byte WO-135 read -- the
// field's 0 crouch edges with real crouching.
constexpr size_t kActorIsCrouched     = 0xAF8;
constexpr size_t kActorCombatActor    = 0x970;   // GetOrCreateCombatActor (what every combat test command calls)
constexpr size_t kActorPseudoComp     = 0x7E8;   // AI-animation component; pseudo-speed at +0x18
constexpr size_t kActorReqVel         = 0x574;   // requested velocity x,y,z (FinalizeMovementRequest caches it)
constexpr size_t kActorCombatField    = 0x300;   // m_pCombatActor (mannequin_read.cpp)
constexpr size_t kHolderGetExt        = 0x70;    // holder->vtbl[0x70](holder, 1) = the extension
constexpr size_t kExpSetCrouch        = 0xE8;
constexpr size_t kExpGetCrouch        = 0xF0;
constexpr size_t kExpRequestJump      = 0x100;
constexpr size_t kCaTryStartCombat    = 0x360;
constexpr size_t kCaModel             = 0x2F0;
constexpr size_t kCaOwnerEntity       = 0x2D8;   // the owning C_Actor (WO-42 s9.5), not a CEntity
constexpr size_t kActorGetName        = 0x490;   // C_Actor vftable: GetName() -> const char*
constexpr size_t kActorEntityId       = 0x30;    // C_Actor: the entity id (u32; WO-42 s4.4)
constexpr size_t kModelFlags          = 0xEE8;   // the guard-request flag set; SetFlag(this, index, value)
constexpr size_t kModelOpponent       = 0x1118;
constexpr size_t kModelBlockMax       = 0x865;   // max over the five per-scope block-mode bytes
constexpr int    kGuardRequestScope   = 4;
constexpr size_t kActionEnterImpl     = 0x1C8;
constexpr size_t kActionDescriptor    = 0x60;
constexpr size_t kActionCombatActor   = 0x78;    // C_CombatActorActionAttack ctor: mov [rbx+0x78], rbp (rbp = ca)
constexpr size_t kDescRowGuid         = 0x84;    // live, WO-121 session 1: mn_fragment_guid, Windows byte order

// WO-129 -- the gait at the engine's own tag update (docs/WO-129-findings.md s1).
// C_ActorMovementController::Update (EntityModule 0xB18D0) calls SetPseudoSpeed
// with its movement request's value on every actor every frame, AFTER our write
// at the frame hook and BEFORE C_Actor::UpdateMannequinTags reads it (observed:
// 0.00 at every tag update of a written avatar). With no movement request the
// requested velocity is 0 too, so neither a pace nor a direction tag was ever
// set: the body slid. Our inputs are re-applied at UpdateMannequinTags' entry.
constexpr size_t kActorUpdateTags     = 0xC98;   // C_Actor vftable slot: C_Actor::UpdateMannequinTags
constexpr size_t kActorMoveVec        = 0x614;   // Vec2 the tag update prefers for the direction tag
constexpr size_t kActorSpeedType      = 0x860;   // stance manager (actor+0x850) +0x10: the logical-speed table kind
constexpr size_t kGiSpeedHolder       = 0x138;   // GetGameIface()+0x138 -> vtbl[0x100]() = the logical-speed manager
constexpr size_t kHolderGetSpeedMgr   = 0x100;
constexpr size_t kSpeedMgrCount       = 0x08;    // count(soul, kind): how many logical speeds this body has
constexpr size_t kSpeedMgrMap         = 0x78;    // id = (int)(pseudo + 0.5) - 1 (checked by bytes at install)
constexpr auto& kTagsPrologue = hookpro::kMotionTags;   // hook_prologues.h (WO-148)

// Combat-model properties, each names itself at +0x30 (WO-119 s7); value at +8.
struct Prop { size_t off; const char* name; };
constexpr Prop kPropCombatMode{0x000, "CombatMode"};
constexpr Prop kPropGuardStance{0x100, "GuardStance"};
constexpr Prop kPropGuardZone{0x140, "GuardZone"};
constexpr Prop kPropReqAtkZone{0x200, "RequestedAtkZone"};
constexpr Prop kPropAttackType{0x2C0, "AttackType"};
constexpr Prop kPropReqInputClass{0x300, "RequestedInputClass"};
// WO-163 (read_model; the names and offsets are docs/WO-100 s10's table, each verified by the block's own name at +0x30)
constexpr Prop kPropState{0x040, "State"};
constexpr Prop kPropBlockZone{0x7C0, "BlockZoneId"};
constexpr Prop kPropBlockHand{0x800, "BlockHandSlot"};
constexpr Prop kPropBlockMode{0x868, "BlockMode"};
constexpr Prop kPropPerfectBlock{0x8A8, "PerfectBlockState"};
constexpr Prop kPropAtkZone{0x1C0, "AttackZone"};
constexpr Prop kPropAtkStrength{0x280, "AttackStrength"};
constexpr Prop kPropAtkHand{0x240, "AttackHandSlot"};

// ---- SEH-isolated primitives (no destructible locals) --------------------------
// WO-153 5: an address no user-mode pointer can hold (the null page, or above the canonical range) is refused before the
// read. The field's 3,556 faults a minute were `reading 0xffffffffffffffff`: a pointer slot that held data, read every frame.
inline bool plausible_addr(uintptr_t a) { return fault::plausible_address(a); }
template <class T> bool rd(const void* base, size_t off, T* out) {
    KCDMP_FAULT_READ(site, "motion::rd");
    if (!plausible_addr(reinterpret_cast<uintptr_t>(base) + off)) return false;
    return fault::guarded(site, [&] { *out = *reinterpret_cast<const T*>(static_cast<const char*>(base) + off); });
}
void* vslot(void* obj, size_t off) {
    void* vt = nullptr; void* fn = nullptr;
    if (!obj || !rd(obj, 0, &vt) || !vt || !rd(vt, off, &fn)) return nullptr;
    return fn;
}
bool is_a(void* obj, void* const* vft) { void* vp = nullptr; return obj && vft && rd(obj, 0, &vp) && vp == static_cast<const void*>(vft); }
bool call_p0(void* fn, void* self, void** out) {
    KCDMP_FAULT_CALL(site, "motion::call_p0");
    return fault::guarded(site, [&] { *out = reinterpret_cast<void* (__fastcall*)(void*)>(fn)(self); });
}
bool call_p1u(void* fn, void* self, uint32_t a, void** out) {
    KCDMP_FAULT_CALL(site, "motion::call_p1u");
    return fault::guarded(site, [&] { *out = reinterpret_cast<void* (__fastcall*)(void*, uint32_t)>(fn)(self, a); });
}
bool call_p1b(void* fn, void* self, bool a, void** out) {
    KCDMP_FAULT_CALL(site, "motion::call_p1b");
    return fault::guarded(site, [&] { *out = reinterpret_cast<void* (__fastcall*)(void*, bool)>(fn)(self, a); });
}
bool call_f(void* fn, void* self, float v) {
    KCDMP_FAULT_CALL(site, "motion::call_f");
    return fault::guarded(site, [&] { reinterpret_cast<void (__fastcall*)(void*, float)>(fn)(self, v); });
}
bool call_bb(void* fn, void* self, bool a, bool b) {
    KCDMP_FAULT_CALL(site, "motion::call_bb");
    return fault::guarded(site, [&] { reinterpret_cast<void (__fastcall*)(void*, bool, bool)>(fn)(self, a, b); });
}
bool call_ret_b(void* fn, void* self, bool* out) {
    KCDMP_FAULT_CALL(site, "motion::call_ret_b");
    return fault::guarded(site, [&] { *out = reinterpret_cast<bool (__fastcall*)(void*)>(fn)(self); });
}
bool call_jump(bool (__fastcall* fn)(void*), void* self, bool* out) {
    KCDMP_FAULT_CALL(site, "motion::call_jump");
    return fault::guarded(site, [&] { *out = fn(self); });
}
bool call_ret_u8(void* fn, void* self, uint8_t* out) {
    KCDMP_FAULT_CALL(site, "motion::call_ret_u8");
    return fault::guarded(site, [&] { *out = reinterpret_cast<uint8_t (__fastcall*)(void*)>(fn)(self); });
}
bool call_count(void* fn, void* mgr, void* soul, int32_t kind, uint64_t* out) {
    KCDMP_FAULT_CALL(site, "motion::call_count");
    return fault::guarded(site, [&] { *out = reinterpret_cast<uint64_t (__fastcall*)(void*, void*, int32_t)>(fn)(mgr, soul, kind); });
}
bool call_trystart(void* fn, void* ca, uint64_t* out) {
    struct { uint8_t has; uint8_t pad[3]; int32_t v; } opt{};   // optional<int>{has=false}
    KCDMP_FAULT_CALL(site, "motion::call_trystart");
    return fault::guarded(site, [&] { *out = reinterpret_cast<uint64_t (__fastcall*)(void*, void*)>(fn)(ca, &opt); });
}
bool call_auto(void* fn, void* cmd, void* ca, bool enable) {
    KCDMP_FAULT_CALL(site, "motion::call_auto");
    return fault::guarded(site, [&] { reinterpret_cast<void (__fastcall*)(void*, void*, char)>(fn)(cmd, ca, enable ? 1 : 0); });
}
bool call_setflag(void* fn, void* flags, int index, uint8_t value) {
    KCDMP_FAULT_CALL(site, "motion::call_setflag");
    return fault::guarded(site, [&] { reinterpret_cast<void (__fastcall*)(void*, int, uint8_t)>(fn)(flags, index, value); });
}
bool call_setguardzone(void* fn, void* ca, int zone, int stance) {
    KCDMP_FAULT_CALL(site, "motion::call_setguardzone");
    return fault::guarded(site, [&] { reinterpret_cast<char (__fastcall*)(void*, int, int, char)>(fn)(ca, zone, stance, 0); });
}
bool call_setatkzone(void* fn, void* ca, int zone) {
    KCDMP_FAULT_CALL(site, "motion::call_setatkzone");
    return fault::guarded(site, [&] { reinterpret_cast<void (__fastcall*)(void*, int)>(fn)(ca, zone); });
}
bool call_setblock(void* fn, void* ca, bool on, unsigned scope) {
    KCDMP_FAULT_CALL(site, "motion::call_setblock");
    return fault::guarded(site, [&] { reinterpret_cast<void (__fastcall*)(void*, char, unsigned)>(fn)(ca, on ? 1 : 0, scope); });
}
bool copy_cstr(const void* p, char* out, size_t n) {
    KCDMP_FAULT_READ(site, "motion::copy_cstr");
    if (!plausible_addr(reinterpret_cast<uintptr_t>(p))) { out[0] = 0; return false; }   // WO-153 5
    if (fault::guarded(site, [&] {
        const char* s = static_cast<const char*>(p);
        size_t i = 0;
        for (; i + 1 < n && s[i]; ++i) out[i] = s[i];
        out[i] = 0;
    })) return true;
    out[0] = 0;
    return false;
}

// ---- anchors -------------------------------------------------------------------
struct Anchors {
    void* const* vftActor = nullptr;
    void* const* vftExp = nullptr;
    void* const* vftCa = nullptr;
    void* const* vftCa8 = nullptr;   // WO-129: C_CombatActor's secondary base (+8): an interface pointer to it carries this vptr
    void* const* vftCp = nullptr;    // WO-131: C_CombatPlayer, the PLAYER's combat actor (a C_CombatActor subclass, own vftables)
    void* const* vftCp8 = nullptr;
    void* fnSetPseudo = nullptr, *fnSetCrouch = nullptr, *fnGetCrouch = nullptr, *fnRequestJump = nullptr;
    void* fnTryStart = nullptr, *fnAuto = nullptr, *fnSetFlag = nullptr;
    void* fnSetGuardZone = nullptr, *fnSetAtkZone = nullptr, *fnSetBlock = nullptr;
    void* const* vftAttack = nullptr, *const* vftDodge = nullptr, *const* vftPerfect = nullptr, *const* vftBlock = nullptr;
    void* const* vftHit = nullptr, *const* vftSyncAttack = nullptr;   // WO-151: an NPC's hit reaction, an animal's paired bite
    bool actorLookup = false;   // gi+0x188 -> vtbl[0x18](eid): the engine's own route (the test commands)
};
Anchors A;
std::atomic<bool> g_gait{false}, g_moves{false}, g_combat{false}, g_capture{false};
std::string g_whyGait = "not installed", g_whyMoves = "not installed", g_whyCombat = "not installed", g_whyCapture = "not installed";

// ---- config (agent) --------------------------------------------------------------
std::atomic<bool> g_cfgAvatarGait{true}, g_cfgNpcGait{true}, g_cfgMoves{true}, g_cfgCombat{true}, g_cfgNpcRows{true};
std::atomic<bool> g_cfgGaitHyst{true};   // WO-154 5: mp_gait_hysteresis
std::atomic<bool> g_cfgChanged{false};
// WO-135: which groups of the avatar's puppet contexts are set (MotionConfig byte 5;
// kQuiet* below). Default: all of them.
std::atomic<uint8_t> g_cfgQuiet{0x0F};

// ---- per-body state (main thread) ------------------------------------------------
struct Body {
    uint32_t eid = 0;
    void* ent = nullptr;
    void* actor = nullptr;
    void* ca = nullptr;
    void* exp = nullptr;
    bool avatar = false;
    std::string key;
    // gait
    float speedEma = 0;
    bool gaitWritten = false;
    float velX = 0, velY = 0;     // WO-129: smoothed rendered planar velocity (world), the direction tag's input
    float cls = 0;                // WO-129: the logical speed class written (0 still, 1 walk, 2 run, 3 sprint)
    int   range = -1;             // WO-129: the body's own class count (engine), -1 unknown
    double rangeAt = -1;
    int   slot = -1;              // WO-129: index into g_gaitTable, -1 none
    // moves
    bool crouchApplied = false;
    // combat
    bool automationOff = false, combatHeld = false, blockApplied = false;
    int appliedGz = -2, appliedGs = -2, appliedAz = -2;
    double caBadUntil = 0;        // WO-153 5: no combat read on this body until then (its combat actor read back wrong)
    double lastCombatAssert = 0, lastBuffCheck = 0, lastGaitLog = 0;
    uint32_t combatStarts = 0;
    bool ctxApplied = false;      // the WO-121 avatar contexts (kAvatarContexts) are set on its soul
    uint8_t quietApplied = 0;     // WO-135: the kQuiet* groups set on its soul
    double attachAt = -1;         // WO-155: when this body was first driven (a fresh spawn, a re-bind after a fall)
    bool nudged = false;          // WO-155: its locomotion graph has been started (a walk-class pulse, or it walked)
};
std::unordered_map<uint32_t, Body> g_bodies;
constexpr double kNudgeAfterS = 0.1, kNudgeS = 0.6;
constexpr float kNudgeMps = 0.3f;   // WO-155: the T-pose pulse (body_frame)

// WO-132: NPC copies engaged on a joiner -- the host NPC's combat state (a
// synthesized State2: combat, guard zone/stance, attack zone, block) held on
// the copy exactly as an avatar's stream is. Main thread only.
struct Engage { State2 st{}; double at = 0; };
std::unordered_map<uint32_t, Engage> g_engage;
constexpr double kEngageStaleS = 3.0;   // no fresh host state for this long: the hold lets go
std::atomic<uint32_t> c_engageHolds{0}, c_engageReleases{0};

// ---- WO-129: the gait table (main thread writes, any thread reads) --------------
// UpdateMannequinTags runs on the main thread and on job workers, so the hook
// reads gait::Table (fixed, open-addressed atomics); only the main thread
// inserts and removes. A slot older than kGaitStaleTicks frames is ignored.
constexpr uint32_t kGaitStaleTicks = 30;
gait::Table<256> g_gaitTable;
std::atomic<uint32_t> g_gaitTick{0};
std::atomic<bool> g_tags{false};
std::string g_whyTags = "not installed";
std::atomic<uint32_t> c_tagApplied{0};
void* g_fnSpeedMap = nullptr;          // the manager's vtbl[0x78] body, byte-checked (the round(x)-1 mapper)

bool write_tag_inputs(void* actor, float cls, float vx, float vy) {
    KCDMP_FAULT_READ(site, "motion::write_tag_inputs");
    return fault::guarded(site, [&] {
        void* comp = *reinterpret_cast<void**>(static_cast<char*>(actor) + kActorPseudoComp);
        if (comp) *reinterpret_cast<float*>(static_cast<char*>(comp) + 0x18) = cls;
        float* rv = reinterpret_cast<float*>(static_cast<char*>(actor) + kActorReqVel);
        rv[0] = vx; rv[1] = vy; rv[2] = 0.0f;
        float* mv = reinterpret_cast<float*>(static_cast<char*>(actor) + kActorMoveVec);
        mv[0] = vx; mv[1] = vy;
    });
}

// UpdateMannequinTags entry (any thread). Cheap when nothing is driven.
void on_update_tags(void* actor) {
    if (g_gaitTable.live() <= 0 || !g_tags.load(std::memory_order_relaxed)) return;
    float cls, vx, vy;
    if (!g_gaitTable.read(actor, g_gaitTick.load(std::memory_order_relaxed), kGaitStaleTicks, &cls, &vx, &vy)) return;
    if (write_tag_inputs(actor, cls, vx, vy)) c_tagApplied.fetch_add(1, std::memory_order_relaxed);
}

// The engine reads pseudo-speed as a logical speed CLASS, not m/s: the manager's
// mapper is id = (int)(pseudo + 0.5) - 1, and this body's table holds `range`
// classes (3 on the avatars seen: walk, run, sprint). Streams are in m/s (the
// sender's requested velocity), so they are classed (gait::speed_class).

// Pending avatar events (pipe thread -> main thread).
struct PendingEvent { uint8_t kind; uint32_t eid; };
std::mutex g_evMutex;
std::vector<PendingEvent> g_events;

// ---- captured actions (any thread -> main thread) --------------------------------
struct Captured {
    uint8_t kind, flags; int8_t ic, zone, type; uint8_t guid[16]; uint32_t eid; char name[64];
    uint8_t alt[16]; bool hasAlt;   // WO-163 (A1): a sync attack's legacy-offset read, for the agent's both-offsets check
};
std::mutex g_capMutex;
std::vector<Captured> g_captured;
std::atomic<void*> g_playerCa{nullptr};
std::atomic<void*> g_playerExp{nullptr};
std::atomic<ActionFn> g_actionFn{nullptr};

// Counters for the status reply.
std::atomic<uint32_t> c_gaitWrites{0}, c_crouch{0}, c_jumps{0}, c_jumpFail{0}, c_combatStarts{0}, c_autoOff{0},
    c_guardZone{0}, c_atkZone{0}, c_block{0}, c_capAttack{0}, c_capNpc{0}, c_capJump{0}, c_capOther{0}, c_capDropped{0},
    c_faults{0}, c_capHit{0}, c_capSync{0},
    // WO-129: why a capture was dropped (the first two-player session: cap_dropped 5 / 11, cap_attack 0)
    c_dropNotCa{0}, c_dropNoDesc{0}, c_dropNoGuid{0}, c_dropNoOwner{0}, c_capViaBase8{0}, c_capOurs{0};

void* g_autoCmd = nullptr;   // a zeroed stand-in for the test command the automation function reads (+0x79..+0x7B)
void* g_autoCmdOn = nullptr; // the same with every enable byte set

bool prop_named(void* model, const Prop& p) {
    void* s = nullptr;
    char buf[32]{};
    return rd(model, p.off + 0x30, &s) && s && copy_cstr(s, buf, sizeof(buf)) && std::strcmp(buf, p.name) == 0;
}
template <class T> bool prop_value(void* model, const Prop& p, T* out) {
    return prop_named(model, p) && rd(model, p.off + 8, out);
}

void* actor_by_eid(uint32_t eid) {
    if (!A.actorLookup) return nullptr;
    void* gi = engine::game_iface();
    void* am = nullptr;
    if (!gi || !rd(gi, 0x188, &am) || !am) return nullptr;
    void* fn = vslot(am, 0x18);
    void* actor = nullptr;
    if (!fn || !call_p1u(fn, am, eid, &actor)) return nullptr;
    return actor;
}

void* expansion_of(void* actor) {
    void* holderFn = vslot(actor, kActorExpHolder);
    void* holder = nullptr;
    if (!holderFn || !call_p0(holderFn, actor, &holder) || !holder) return nullptr;
    void* getExt = vslot(holder, kHolderGetExt);
    void* ext = nullptr;
    if (!getExt || !call_p1b(getExt, holder, true, &ext) || !ext) return nullptr;
    return is_a(ext, A.vftExp) ? ext : nullptr;   // the extension IS a C_ActorStateExpansion, or nothing
}

// WO-129: a combat-actor pointer as the engine hands it out, normalised to the
// object's start. C_CombatActor has a secondary base at +8 (RTTI: vftables at
// offsets 0 and 8), so a pointer typed as that base is the object + 8 and
// carries the +8 vptr; comparing it to the primary vftable (the WO-121 code)
// rejects it. Nothing else is accepted.
// WO-131: the player's own combat actor is a C_CombatPlayer (RTTI: a
// C_CombatActor subclass with its own vftables at 0 and 8). The exact-vptr
// test rejected it -- the field's `owner-not-a-combat-actor ...
// CombatModule+0x612B28` IS C_CombatPlayer's primary vftable (resolved
// offline from the COL), so no player swing was ever captured and
// g_playerCa read null. Both classes are accepted now, nothing else.
void* as_combat_actor(void* p) {
    if (!p) return nullptr;
    if (is_a(p, A.vftCa) || is_a(p, A.vftCp)) return p;
    void* base = static_cast<char*>(p) - 8;
    if (A.vftCa8 && is_a(p, A.vftCa8) && is_a(base, A.vftCa)) return base;
    if (A.vftCp8 && is_a(p, A.vftCp8) && is_a(base, A.vftCp)) return base;
    return nullptr;
}

// WO-131: the class name behind a vptr, from its RTTI Complete Object Locator
// (vptr[-1]: signature, offset, cdOffset, TD rva, CHD rva, self rva; the
// module base = COL - self rva; the name is TD + 0x10). Logs only.
bool rtti_name_of(const void* vptr, char* out, size_t n) {
    if (!vptr || n < 2) return false;
    const void* col = nullptr;
    if (!rd(vptr, static_cast<size_t>(-8), &col) || !col) return false;
    uint32_t sig = 0, tdRva = 0, selfRva = 0;
    if (!rd(col, 0, &sig) || sig != 1 || !rd(col, 12, &tdRva) || !rd(col, 20, &selfRva)) return false;
    const char* base = static_cast<const char*>(col) - selfRva;
    const char* name = base + tdRva + 0x10;
    size_t i = 0;
    for (; i + 1 < n; ++i) { char ch = 0; if (!rd(name, i, &ch) || !ch) break; out[i] = ch; }
    out[i] = 0;
    return i > 0;
}

void* combat_actor_of(void* actor, bool create) {
    void* ca = nullptr;
    if (!create) { if (!rd(actor, kActorCombatField, &ca)) return nullptr; }
    else {
        void* fn = vslot(actor, kActorCombatActor);
        if (!fn || !call_p0(fn, actor, &ca)) return nullptr;
    }
    return as_combat_actor(ca);
}

void* player_actor() {
    static void* s_inst = nullptr; static void* s_fn = nullptr;
    if (!s_fn) {
        HMODULE em = GetModuleHandleA("EntityModule.dll");
        if (!em) return nullptr;
        auto exps = module_exports(em);
        s_inst = find_export(exps, "?m_Instance@C_EntityModule@entitymodule@wh@@");
        s_fn = find_export(exps, "?GetPlayerActor@C_EntityModule@entitymodule@wh@@");
        if (!s_inst || !s_fn) { s_fn = nullptr; return nullptr; }
    }
    void* inst = nullptr;
    if (!rd(s_inst, 0, &inst) || !inst) return nullptr;
    void* actor = nullptr;
    if (!call_p0(s_fn, inst, &actor)) return nullptr;
    return actor;
}

// WO-135: the local player's crouch as the capture reads it. Every edge is
// logged and counted (cap_crouch), and a read that cannot happen says why once
// -- the field's `crouch=0` could not tell a capture that never saw a crouch
// from an apply that never ran.
std::atomic<uint32_t> c_capCrouch{0}, c_capStance{0};
bool g_localCrouch = false;
bool g_crouchWhyLogged = false;

// WO-136 Phase 6: actor slot 0xAF8 (validated once: EntityModule code that calls
// [rax+0xB78], the stance component getter, and compares its state with 1).
void* g_fnIsCrouched = nullptr;
int g_isCrouchedState = 0;   // 0 unknown, 1 validated, -1 refused
bool actor_is_crouched(void* actor, bool* out) {
    void* fn = vslot(actor, kActorIsCrouched);
    if (!fn) return false;
    if (g_isCrouchedState == 0 || fn != g_fnIsCrouched) {
        HMODULE em = GetModuleHandleA("EntityModule.dll");
        static const uint8_t kCallStance[] = {0xFF, 0x90, 0x78, 0x0B, 0x00, 0x00};   // call [rax+0xB78] (the stance component)
        static const uint8_t kCmpOne[] = {0x3C, 0x01};                                 // cmp al,1 (state 1 = crouched)
        const bool ok = em && anchor::function_has_bytes(em, fn, kCallStance, sizeof kCallStance)
                        && anchor::function_has_bytes(em, fn, kCmpOne, sizeof kCmpOne);
        g_fnIsCrouched = fn;
        g_isCrouchedState = ok ? 1 : -1;
        char d[64]{};
        anchor::describe(fn, d, sizeof d);
        logf("WO136-CROUCH the actor's own crouch query (slot 0xAF8) = %s -> %s", d, ok ? "armed (the stance the crouch key changes)" : "REFUSED (not the disassembled stance check)");
    }
    return g_isCrouchedState == 1 && call_ret_b(fn, actor, out);
}
void note_local_crouch(void* actor, void* exp, bool readOk, bool crouched, const char* src) {
    if (!readOk) {
        if (g_crouchWhyLogged) return;
        g_crouchWhyLogged = true;
        void* holderFn = vslot(actor, kActorExpHolder);
        void* holder = nullptr; void* ext = nullptr;
        if (holderFn && call_p0(holderFn, actor, &holder) && holder) {
            void* getExt = vslot(holder, kHolderGetExt);
            if (getExt) call_p1b(getExt, holder, true, &ext);
        }
        char cls[128] = "?";
        void* vt = nullptr;
        if (ext && rd(ext, 0, &vt)) rtti_name_of(vt, cls, sizeof cls);
        logf("WO135-CROUCH local capture CANNOT read: exp=%s ext=%p class=%s -- the local crouch is never sent", exp ? "yes" : "no", ext, cls);
        return;
    }
    if (crouched == g_localCrouch) return;
    g_localCrouch = crouched;
    if (crouched) c_capCrouch.fetch_add(1);
    if (crouched && std::strstr(src, "stance")) c_capStance.fetch_add(1);
    logf("WO135-CROUCH local crouch=%d (source: %s -- stance = the actor's own crouch query (WO-136), byte = the expansion's desire, tag = the Mannequin stealth stance)", crouched ? 1 : 0, crouched ? src : "-");
}

// ---- the capture hooks -----------------------------------------------------------
// WO-151: kClsHit -- an NPC's own hit reaction (C_CombatActorActionHit: CombatHit rows,
// combat_action_hit.xml), sent as ActionKind.NpcHit so the joiner's copy shows the host's
// reaction; kClsSyncAttack -- a paired attack (C_CombatActorActionSyncAttack: the wolves' bite
// on a man, CombatAttackSyncGen rows, combat_action_sync_attack.xml), sent as an NPC attack.
// NPCs only; the player's own are not captured.
enum Cls : uint8_t { kClsAttack = 0, kClsDodge = 1, kClsPerfect = 2, kClsBlock = 3, kClsHit = 4, kClsSyncAttack = 5, kClsCount = 6 };
constexpr uint8_t kKindNpcHit = 15;   // ActionKind.NpcHit (Protocol.cs)
void* g_origEnter[kClsCount]{};
uint8_t* g_thunks = nullptr;

// WO-129: one line for the first drops of each reason (then counters only).
void note_drop(std::atomic<uint32_t>& counter, const char* why, uint8_t cls, const void* raw) {
    c_capDropped.fetch_add(1);
    if (counter.fetch_add(1) >= 2) return;
    void* vp = nullptr; rd(raw, 0, &vp);
    char d[64]{}; anchor::describe(vp, d, sizeof d);
    char rn[96]{}; if (!rtti_name_of(vp, rn, sizeof rn)) std::snprintf(rn, sizeof rn, "?");
    logf("WO129-CAPTURE drop reason=%s class=%u owner_ptr_vptr=%s rtti=%s (C_CombatActor / C_CombatPlayer, primary or +8, expected)", why, cls, d, rn);
}

// WO-151: the first hit actions, dumped (guarded reads only) so the row behind them can be found by its
// GUID offline: the action's first 0x100 bytes, and for every plausible pointer in it, 0x100 bytes there.
std::atomic<int> g_hitDumps{0};
void dump_hit_action(void* action) {
    if (g_hitDumps.fetch_add(1) >= 4) return;
    auto hex = [](const uint8_t* b, size_t n, char* out, size_t cap) {
        size_t k = 0;
        for (size_t i = 0; i < n && k + 3 < cap; ++i) k += std::snprintf(out + k, cap - k, "%02x", b[i]);
        out[k < cap ? k : cap - 1] = 0;
    };
    uint8_t a[0x100]{};
    for (size_t i = 0; i < sizeof a; i += 8) { uint64_t q = 0; if (rd(action, i, &q)) std::memcpy(a + i, &q, 8); }
    char line[0x300]{};
    hex(a, sizeof a, line, sizeof line);
    logf("WO151-HITDUMP action %p +000: %s", action, line);
    for (size_t i = 0; i < sizeof a; i += 8) {
        uint64_t q = 0; std::memcpy(&q, a + i, 8);
        if (q < 0x10000 || q > 0x00007FFFFFFFFFFFull || (q & 7)) continue;
        uint8_t b[0x100]{};
        bool any = false;
        for (size_t j = 0; j < sizeof b; j += 8) { uint64_t v = 0; if (rd(reinterpret_cast<void*>(q), j, &v)) { std::memcpy(b + j, &v, 8); any = true; } }
        if (!any) continue;
        hex(b, sizeof b, line, sizeof line);
        logf("WO151-HITDUMP action+0x%02zX -> %p: %s", i, reinterpret_cast<void*>(q), line);
    }
}

// WO-151: where each class keeps its committed row. An attack (and its kin): the descriptor at
// action+0x60, the row GUID at +0x84 (WO-121, live). A hit action keeps the combat actor of the
// one who hit it at +0x60; its row is at +0x58 with the GUID at +0x7C (live, L3: 4 of 4 dumps held a
// combat_action_hit.xml GUID exactly there). The row object has its own vftable, so for the new
// classes the row is accepted only when its RTTI class name names the row type (checked once per
// vptr, then by vptr equality); the agent checks every GUID against the game's tables as well.
struct RowPath { size_t descOff; size_t guidOff; const char* rtti; };
constexpr RowPath kPathAttack{kActionDescriptor, kDescRowGuid, nullptr};
constexpr RowPath kPathHit{0x58, 0x7C, "CombatActionHitData"};
// WO-163 (A1): the sync row's GUID is at +0x7C (WO-162 Q3.1/Q3.3: 16 of 16 field dumps); +0x84, the attack class's offset, read half
// of it. The legacy offset stays as a check (capture() below), never as the row.
constexpr RowPath kPathSync{0x60, kcdmp::wo163::kSyncGuidOff, "CombatActionSyncAttackData"};
const RowPath& row_path(uint8_t cls) { return cls == kClsHit ? kPathHit : cls == kClsSyncAttack ? kPathSync : kPathAttack; }
std::atomic<void*> g_rowVptrOk[kClsCount]{};
std::atomic<int> g_rowRttiLogged[kClsCount]{};
std::atomic<uint32_t> c_dropRowType{0};

// The row object behind `desc` is the type `path` names (or the path needs no check).
bool row_type_ok(uint8_t cls, const RowPath& path, void* desc) {
    if (!path.rtti) return true;
    void* vp = nullptr;
    if (!rd(desc, 0, &vp) || !vp) return false;
    if (vp == g_rowVptrOk[cls].load(std::memory_order_relaxed)) return true;
    char name[192]{};
    const bool named = rtti_name_of(vp, name, sizeof name);
    const bool ok = named && std::strstr(name, path.rtti) != nullptr;
    if (ok) g_rowVptrOk[cls].store(vp);
    if (g_rowRttiLogged[cls].fetch_add(1) < 3)
        logf("WO151-CAPTURE row type class=%u at action+0x%zX: %s -> %s", cls, path.descOff, named ? name : "(no RTTI)",
             ok ? "accepted" : "REFUSED (not the row type)");
    return ok;
}

// WO-151 probe: a sync attack's row object (its RTTI is S_CombatActionSyncAttackData, live L4), its first
// 0x200 bytes -- its GUID is not at +0x84 (the GUIDs read there were in no table).
std::atomic<int> g_rowDumps{0};
void dump_row(void* desc) {
    if (g_rowDumps.fetch_add(1) >= 3) return;
    uint8_t b[0x200]{};
    for (size_t j = 0; j < sizeof b; j += 8) { uint64_t v = 0; if (rd(desc, j, &v)) std::memcpy(b + j, &v, 8); }
    char line[0x500]{};
    size_t k = 0;
    for (size_t i = 0; i < sizeof b && k + 3 < sizeof line; ++i) k += std::snprintf(line + k, sizeof line - k, "%02x", b[i]);
    logf("WO151-SYNCDUMP row %p: %s", desc, line);
}

void capture(uint8_t cls, void* action) {
    void* raw = nullptr;
    if (!rd(action, kActionCombatActor, &raw) || !raw) return;
    void* ca = as_combat_actor(raw);
    void* playerCa = g_playerCa.load(std::memory_order_relaxed);
    const bool isPlayer = ca && ca == playerCa;
    // WO-163 (A2): an NPC's perfect-block-class action is read too -- its master strike (the blocker's counter) is a swing; the agent
    // sorts it by the row's table (flags bit 0 marks the class) and drops the block itself.
    const bool npcKind = cls == kClsAttack || cls == kClsHit || cls == kClsSyncAttack || cls == kClsPerfect;
    if (isPlayer && (cls == kClsHit || cls == kClsSyncAttack)) return;   // WO-151: NPCs only
    if (!isPlayer && !(npcKind && g_cfgNpcRows.load(std::memory_order_relaxed))) return;
    if (!ca) { note_drop(c_dropNotCa, "owner-not-a-combat-actor", cls, raw); return; }
    if (ca != raw) c_capViaBase8.fetch_add(1);
    void* desc = nullptr;
    Captured c{};
    const RowPath& path = row_path(cls);
    if (!rd(action, path.descOff, &desc) || !desc) { note_drop(c_dropNoDesc, "no-descriptor", cls, raw); return; }
    if (!row_type_ok(cls, path, desc)) {
        c_dropRowType.fetch_add(1);
        if (cls == kClsHit || cls == kClsSyncAttack) dump_hit_action(action);   // the first 4: where the row is, by content
        return;
    }
    if (cls == kClsSyncAttack) dump_row(desc);   // WO-151 probe: where the sync row keeps its GUID (the first 3)
    uint64_t g0 = 0, g1 = 0;
    if (!rd(desc, path.guidOff, &g0) || !rd(desc, path.guidOff + 8, &g1) || (g0 == 0 && g1 == 0)) { note_drop(c_dropNoGuid, "no-row-guid", cls, raw); return; }
    std::memcpy(c.guid, &g0, 8); std::memcpy(c.guid + 8, &g1, 8);
    if (cls == kClsSyncAttack) {
        uint64_t a0 = 0, a1 = 0;
        if (rd(desc, kcdmp::wo163::kSyncGuidLegacyOff, &a0) && rd(desc, kcdmp::wo163::kSyncGuidLegacyOff + 8, &a1) && (a0 || a1)) {
            std::memcpy(c.alt, &a0, 8); std::memcpy(c.alt + 8, &a1, 8); c.hasAlt = true;
        }
    }
    c.kind = cls == kClsAttack || cls == kClsSyncAttack || (cls == kClsPerfect && !isPlayer) ? 1 : cls == kClsHit ? kKindNpcHit : cls == kClsDodge ? 7 : 6;
    c.flags = cls == kClsPerfect ? 0x01 : 0;
    c.ic = -1; c.zone = -1; c.type = -1;
    void* model = nullptr;
    if (rd(ca, kCaModel, &model) && model) {
        int32_t v = -1;
        if (prop_value(model, kPropReqInputClass, &v)) c.ic = static_cast<int8_t>(v);
        if (prop_value(model, kPropReqAtkZone, &v)) c.zone = static_cast<int8_t>(v);
        if (prop_value(model, kPropAttackType, &v)) c.type = static_cast<int8_t>(v);
    }
    if (isPlayer) {
        c.eid = 0;
        (cls == kClsAttack ? c_capAttack : c_capOther).fetch_add(1);
    } else {
        // WO-131: ca+0x2D8 is the owning C_ACTOR (WO-42 s9.5 round trip), not
        // a CEntity -- engine::entity_name refused it on every NPC swing (the
        // field's `no-owner-name ... CombatModule+0x5BF030`, a plain
        // C_CombatActor), so npc_rows_out stayed 0. The name is C_Actor
        // vtbl[0x490] GetName, the entity id the u32 at actor+0x30 (WO-42 s4.4),
        // cross-checked against the entity system before it is trusted.
        void* owner = nullptr;
        if (!rd(ca, kCaOwnerEntity, &owner) || !owner) { note_drop(c_dropNoOwner, "no-owner-entity", cls, raw); return; }
        uint32_t oeid = 0;
        void* ent = rd(owner, kActorEntityId, &oeid) && oeid ? engine::entity_by_id(oeid) : nullptr;
        const char* n = ent ? engine::entity_name(ent) : nullptr;
        if (!n) {
            void* fn = vslot(owner, kActorGetName);
            void* np = nullptr;
            if (fn && call_p0(fn, owner, &np)) n = static_cast<const char*>(np);
            ent = nullptr;
        }
        if (!n || !copy_cstr(n, c.name, sizeof(c.name)) || !c.name[0]) { note_drop(c_dropNoOwner, "no-owner-name", cls, raw); return; }
        if (_strnicmp(c.name, "kcd2mp_", 7) == 0 || _strnicmp(c.name, "DialogTwin_", 11) == 0) { c_capOurs.fetch_add(1); return; }   // never ours
        c.eid = ent ? engine::entity_id(ent) : oeid;
        c_capNpc.fetch_add(1);
        if (cls == kClsHit) c_capHit.fetch_add(1);
        else if (cls == kClsSyncAttack) c_capSync.fetch_add(1);
    }
    std::lock_guard<std::mutex> lock(g_capMutex);
    if (g_captured.size() < 256) g_captured.push_back(c); else c_capDropped.fetch_add(1);
}

extern "C" void __cdecl wo121_enter_pre(uint64_t cls, void* action) {
    // SEH is not allowed around code with destructible locals; capture() has
    // none on the hot path except the lock_guard at the end, which is fine
    // because every read above it is SEH-isolated in rd().
    capture(static_cast<uint8_t>(cls), action);
}

// Register-preserving pre-call thunk (the WO-116 pattern): saves the four
// argument registers and xmm0-3, calls wo121_enter_pre(cls, rcx), restores,
// jumps to the original. Nothing about the original call changes.
void* make_thunk(uint8_t* p, uint64_t cls, void* orig) {
    uint8_t* s = p;
    auto emit = [&](std::initializer_list<uint8_t> b) { for (auto x : b) *p++ = x; };
    auto emit64 = [&](uint64_t v) { std::memcpy(p, &v, 8); p += 8; };
    emit({0x51, 0x52, 0x41, 0x50, 0x41, 0x51});                 // push rcx, rdx, r8, r9
    emit({0x48, 0x83, 0xEC, 0x68});                             // sub rsp, 0x68
    emit({0xF3, 0x0F, 0x7F, 0x44, 0x24, 0x20});                 // movdqu [rsp+0x20], xmm0
    emit({0xF3, 0x0F, 0x7F, 0x4C, 0x24, 0x30});
    emit({0xF3, 0x0F, 0x7F, 0x54, 0x24, 0x40});
    emit({0xF3, 0x0F, 0x7F, 0x5C, 0x24, 0x50});
    emit({0x48, 0x8B, 0xD1});                                   // mov rdx, rcx (the action)
    emit({0x48, 0xB9}); emit64(cls);                            // mov rcx, cls
    emit({0x48, 0xB8}); emit64(reinterpret_cast<uint64_t>(&wo121_enter_pre));
    emit({0xFF, 0xD0});                                         // call rax
    emit({0xF3, 0x0F, 0x6F, 0x44, 0x24, 0x20});
    emit({0xF3, 0x0F, 0x6F, 0x4C, 0x24, 0x30});
    emit({0xF3, 0x0F, 0x6F, 0x54, 0x24, 0x40});
    emit({0xF3, 0x0F, 0x6F, 0x5C, 0x24, 0x50});
    emit({0x48, 0x83, 0xC4, 0x68});
    emit({0x41, 0x59, 0x41, 0x58, 0x5A, 0x59});                 // pop r9, r8, rdx, rcx
    emit({0xFF, 0x25, 0, 0, 0, 0}); emit64(reinterpret_cast<uint64_t>(orig));   // jmp [rip+0] -> orig
    return s;
}

bool patch_slot(void* const* vft, size_t slot, void* fn, void** orig) {
    void** at = const_cast<void**>(vft) + slot / 8;
    void* cur = nullptr;
    if (!rd(at, 0, &cur) || !cur) return false;
    DWORD old = 0;
    if (!VirtualProtect(at, 8, PAGE_READWRITE, &old)) return false;
    *orig = cur;
    InterlockedExchangePointer(at, fn);
    VirtualProtect(at, 8, old, &old);
    return true;
}

using RequestJumpFn = bool (__fastcall*)(void*);
RequestJumpFn g_origJump = nullptr;
bool __fastcall hk_request_jump(void* self) {
    const bool r = g_origJump(self);
    if (r && self == g_playerExp.load(std::memory_order_relaxed)) {
        Captured c{};
        c.kind = 2; c.ic = c.zone = c.type = -1;
        c_capJump.fetch_add(1);
        std::lock_guard<std::mutex> lock(g_capMutex);
        if (g_captured.size() < 256) g_captured.push_back(c);
    }
    return r;
}

// ---- install helpers -------------------------------------------------------------
void* slot_fn(void* const* vft, size_t off) { return vft ? vft[off / 8] : nullptr; }

bool has(HMODULE m, const void* fn, std::initializer_list<uint8_t> pat) {
    std::vector<uint8_t> v(pat);
    return anchor::function_has_bytes(m, fn, v.data(), v.size());
}

// The one call target an anchored test command's Execute makes that no other
// test command's Execute also makes (the shared helpers drop out).
const uint8_t* unique_target(HMODULE m, const void* exec, const std::vector<const void*>& others) {
    const uint8_t* mine[64]{};
    int n = anchor::function_call_targets(m, exec, mine, 64);
    const uint8_t* pick = nullptr; int picks = 0;
    for (int i = 0; i < n; ++i) {
        bool shared = false;
        for (const void* o : others) if (o != exec && anchor::function_calls(m, o, mine[i])) { shared = true; break; }
        if (!shared) { pick = mine[i]; ++picks; }
    }
    return picks == 1 ? pick : nullptr;
}

void log_piece(const char* name, bool armed, const std::string& why) {
    logf("WO121-MOTION piece=%s %s%s%s", name, armed ? "armed" : "NOT armed", why.empty() ? "" : " -- ", why.c_str());
}

// ---- appliers (main thread) --------------------------------------------------------
void apply_gait(Body& b, float speed) {
    void* fn = vslot(b.actor, kActorSetPseudoSpeed);
    if (fn != A.fnSetPseudo) return;   // the body's own vtable must dispatch to the anchored function
    if (!call_f(fn, b.actor, speed)) { c_faults.fetch_add(1); g_gait = false; g_whyGait = "SetPseudoSpeed faulted"; logf("WO121-MOTION gait DISARMED -- SetPseudoSpeed faulted on %s", b.key.c_str()); return; }
    b.gaitWritten = true;
    c_gaitWrites.fetch_add(1, std::memory_order_relaxed);
}

void unpublish_gait(Body& b);

void release_gait(Body& b) {
    unpublish_gait(b);   // WO-129: the tag update is the engine's own again from the next frame
    if (!b.gaitWritten || !b.actor) return;
    void* fn = vslot(b.actor, kActorSetPseudoSpeed);
    if (fn == A.fnSetPseudo) call_f(fn, b.actor, 0.0f);
    b.gaitWritten = false;
}

std::atomic<uint32_t> c_crouchApplyFail{0};

void apply_crouch(Body& b, bool want) {
    if (!b.exp) b.exp = expansion_of(b.actor);
    if (!b.exp) { if (c_crouchApplyFail.fetch_add(1) < 5) logf("WO135-CROUCH body=%s apply refused: no state expansion", b.key.c_str()); return; }
    void* fn = vslot(b.exp, kExpSetCrouch);
    if (fn != A.fnSetCrouch) { if (c_crouchApplyFail.fetch_add(1) < 5) logf("WO135-CROUCH body=%s apply refused: SetCrouch slot mismatch", b.key.c_str()); return; }
    if (!call_bb(fn, b.exp, want, false)) { c_faults.fetch_add(1); g_moves = false; g_whyMoves = "SetCrouch faulted"; return; }
    b.crouchApplied = want;
    c_crouch.fetch_add(1);
    logf("WO121-MOTION body=%s crouch=%d", b.key.c_str(), want ? 1 : 0);
}

void set_guard_flag(Body& b, uint8_t v) {
    void* model = nullptr;
    if (!rd(b.ca, kCaModel, &model) || !model) return;
    call_setflag(A.fnSetFlag, static_cast<char*>(model) + kModelFlags, kGuardRequestScope, v);
}

void apply_combat(Body& b, const State2* st, double now) {
    const bool want = st && (st->bits & kBitCombat);
    if (now < b.caBadUntil) return;
    // WO-153 5: the combat actor is read through its owner every frame. It was cached once, and a body whose combat
    // actor the engine freed or rebuilt (a time skip, streaming) kept reading through the old pointer: ca+0x2F0 -> model
    // -> name, one fault per frame for minutes (field: motion::rd / motion::copy_cstr, 78-110 a second).
    void* cur = combat_actor_of(b.actor, false);
    if (b.ca && cur != b.ca) {
        b.ca = nullptr;
        b.automationOff = b.combatHeld = b.blockApplied = false;
        b.appliedGz = b.appliedGs = b.appliedAz = -2;
    }
    if (!b.ca) b.ca = cur ? cur : combat_actor_of(b.actor, true);
    if (!b.ca) return;
    void* model = nullptr;
    if (!rd(b.ca, kCaModel, &model) || !model || !prop_named(model, kPropCombatMode)) {
        b.ca = nullptr; b.caBadUntil = now + 1.0;   // read back wrong: try again in a second, from the owner
        return;
    }
    if (want) {
        if (!b.automationOff) {
            if (!call_auto(A.fnAuto, g_autoCmd, b.ca, false)) { c_faults.fetch_add(1); g_combat = false; g_whyCombat = "automation call faulted"; return; }
            b.automationOff = true;
            c_autoOff.fetch_add(1);
            logf("WO121-MOTION body=%s combat automation OFF (combat_EnableAutomation path)", b.key.c_str());
        }
        uint8_t mode = 0;
        rd(model, kPropCombatMode.off + 8, &mode);
        if (!mode && now - b.lastCombatAssert > 0.25) {
            b.lastCombatAssert = now;
            uint64_t r = 0;
            if (!call_trystart(A.fnTryStart, b.ca, &r)) { c_faults.fetch_add(1); g_combat = false; g_whyCombat = "TryStartCombatMode faulted"; return; }
            set_guard_flag(b, 1);
            ++b.combatStarts;
            c_combatStarts.fetch_add(1);
            if (b.combatStarts <= 3 || (b.combatStarts % 50) == 0)
                logf("WO121-MOTION body=%s combat mode start #%u -> %llu (guard-request flag scope %d set)", b.key.c_str(), b.combatStarts,
                     static_cast<unsigned long long>(r & 0xFF), kGuardRequestScope);
        }
        b.combatHeld = true;
        const int gz = static_cast<int>(st->guardZone) - 1, gs = static_cast<int>(st->guardStance) - 1, az = static_cast<int>(st->atkZone) - 1;
        if (gz >= 0 && (gz != b.appliedGz || gs != b.appliedGs)) {
            if (call_setguardzone(A.fnSetGuardZone, b.ca, gz, gs)) { b.appliedGz = gz; b.appliedGs = gs; c_guardZone.fetch_add(1); }
        }
        if (az >= 0 && az != b.appliedAz) {
            if (call_setatkzone(A.fnSetAtkZone, b.ca, az)) { b.appliedAz = az; c_atkZone.fetch_add(1); }
        }
        const bool block = (st->bits & kBitBlock) != 0;
        if (block != b.blockApplied) {
            if (call_setblock(A.fnSetBlock, b.ca, block, 0)) { b.blockApplied = block; c_block.fetch_add(1); }
            logf("WO121-MOTION body=%s block=%d", b.key.c_str(), block ? 1 : 0);
        }
    } else if (b.combatHeld) {
        if (b.blockApplied) { call_setblock(A.fnSetBlock, b.ca, false, 0); b.blockApplied = false; }
        set_guard_flag(b, 0);   // the engine's own PostUpdate then ends combat when nothing else holds it
        b.combatHeld = false;
        b.appliedGz = b.appliedGs = b.appliedAz = -2;
        logf("WO121-MOTION body=%s combat mode released", b.key.c_str());
    }
}

void set_avatar_contexts(Body& b, bool on);
void set_quiet(Body& b, uint8_t want);

void release_body(Body& b, const char* why) {
    release_gait(b);
    // WO-154 2: an avatar's reaction contexts and its speech gate are its identity, not the writer's: they
    // stay through every unbind (riding, a stale stream, a fallen body, a dropped writer). 0.44.0 cleared them
    // here, and every avatar bark of the field evening with the DLL present fell in those windows (95 s on the
    // host, 75 s on the joiner). They go when the session ends (on_pipe_closed) or a switch turns a group off.
    if (!b.avatar) {
        if (b.ctxApplied) set_avatar_contexts(b, false);
        if (b.quietApplied) set_quiet(b, 0);
    }
    if (b.crouchApplied && b.exp && vslot(b.exp, kExpSetCrouch) == A.fnSetCrouch) { call_bb(A.fnSetCrouch, b.exp, false, false); b.crouchApplied = false; }
    if (b.ca && is_a(b.ca, A.vftCa)) {
        if (b.blockApplied) call_setblock(A.fnSetBlock, b.ca, false, 0);
        if (b.combatHeld) set_guard_flag(b, 0);
        if (b.automationOff && A.fnAuto && g_autoCmdOn) call_auto(A.fnAuto, g_autoCmdOn, b.ca, true);
    }
    if (b.automationOff || b.combatHeld || b.crouchApplied)
        logf("WO121-MOTION body=%s released (%s) -- automation back on, flag cleared", b.key.c_str(), why);
    b.automationOff = b.combatHeld = b.blockApplied = false;
}

bool is_avatar_key(const char* key) {
    if (_strnicmp(key, "kcd2mp_", 7) != 0) return false;
    const char* d = key + 7;
    if (!*d) return false;
    for (; *d; ++d) if (*d < '0' || *d > '9') return false;
    return true;
}

// WO-121 Phase 6: the avatar is a puppet of another player, so its own brain
// must not react to THIS player. Tables.pak :: Libs/Tables/ai/ScriptContext.xml,
// all Class="Entity" rows on this build. Session 3 (observed): Henry drew a
// sword near the avatar -> it barked crime_reaction_barks.vytazena_zbran
// ("feels threatened by the player") and changed weapon on its own.
// Set only while the avatar's combat is ours (mp_avatar_combat on), so the
// legacy preset keeps the 0.28.x reactive ghost.
constexpr const char* kAvatarContexts[] = {
    "crime_ignorePlayersDrawnWeapon",       // the drawn-weapon bark and threat reaction
    "crime_disableHitFromPlayerReaction",   // a hit from the player starts no reaction
    "crime_suppressBehavioralReaction",     // new information starts no behaviour
    "crime_suppressFightStartBark",
    "combat_disableAllSkirmishBarks",
    // NOT combat_suppressFriendlyFire: with it set, a player's sword hit on
    // the avatar did no damage at all (session 5, unarmoured, buff off) --
    // nothing to measure, so nothing for friendly fire to forward.
};
std::atomic<uint32_t> c_ctxSet{0}, c_ctxFail{0};

void* body_soul(const Body& b) {
    void* soul = nullptr;
    void* fn = b.actor ? vslot(b.actor, kActorGetSoul) : nullptr;
    return fn && call_p0(fn, b.actor, &soul) ? soul : nullptr;
}

void set_avatar_contexts(Body& b, bool on) {
    void* soul = body_soul(b);
    if (!soul) return;
    int ok = 0, bad = 0;
    for (const char* n : kAvatarContexts) (kcdmp::sctx::set_soul_context(soul, n, on) >= 0 ? ok : bad)++;
    b.ctxApplied = on;
    c_ctxSet.fetch_add(ok); c_ctxFail.fetch_add(bad);
    logf("WO121-MOTION body=%s avatar contexts %s: %d ok, %d failed", b.key.c_str(), on ? "set" : "cleared", ok, bad);
}

// WO-135: the avatar is a puppet -- seen, never heard. Its own brain perceives
// and reacts like any NPC's (the 0.30.9 field log: 56 assault-witness barks at
// the host's attacks, recognition and torch barks, greetings, 90 hit screams,
// and blocks of its own: a guard's hits measured hp -0.0 st -30). Other NPCs
// perceive and target it (WO-131: never AI-ignorant, the player's faction);
// what it may no longer do is react. Each group is a set of the game's own
// per-soul script contexts (Tables :: Libs/Tables/ai/ScriptContext.xml,
// Class="Entity"), each one checked by the brain on ITSELF (target="" /
// $this.id in Scripts.pak :: AI/npc/basic/switch/*.xml), so setting them on
// the avatar changes only the avatar's own reactions -- nobody else's view of
// it. Why each group is needed: docs/WO-135-findings.md Phase 1.
constexpr uint8_t kQuietSpeech  = 0x01;
constexpr uint8_t kQuietWitness = 0x02;
constexpr uint8_t kQuietReact   = 0x04;
constexpr uint8_t kQuietDefence = 0x08;
struct QuietGroup { uint8_t bit; const char* name; const char* const* ctx; size_t n; };
// The speech group is no script context: speech_mute (SideEffect muteDialogue),
// RestrictDialog and the combat chat switches left every avatar bark starting
// and voiced (WO-135 run H1). It is the native dialogue-start gate (wo135.cpp):
// no dialogue with the avatar among its speakers starts at all.
constexpr const char* const* kQuietSpeechCtx = nullptr;
constexpr const char* kQuietWitnessCtx[] = {
    "crime_ignorePlayerPerception",         // handleAwareness: no awareness of the player -> no recognition, no crime seen
    "crime_ignoreNPCHitVolumes",            // handleAwareness_hitVolume: never witnesses an NPC being hit (the assault barks)
    "crime_ignoreAnimalHitVolumes",
    "crime_ignoreCombatSounds",
    "crime_ignorePlayersSounds",
    "crime_ignoreCorpses",
    "crime_ignoreUnconsciousBodies",
    "crime_ignoreThefts",
    "crime_ignorePickpocketing",
    "crime_ignoreLockpicking",
    "crime_disableCrimeInformationEmit",    // never spreads a crime to others
    "crime_disableReport",                  // SideEffect crimeDisableReport: never reports to a guard
    "crime_dontCreateInformationsWhenHit",  // a hit on it creates no crime information (never turns a guard on the host)
    "switch_disabledInformationReaction",   // SideEffect disableInformationReaction
};
constexpr const char* kQuietReactCtx[] = {
    "switch_disabledPerceptionReaction",
    "switch_disabledHearingReaction",
    "switch_disabledHitReaction",           // a hit starts no brain reaction (its body still takes the hit)
    "switch_disabledHitBehavioralReaction",
    "switch_disabledNearMissReaction",
    "crime_ignoreCrouchingPlayer",          // NPC_VIDI_HRACE_V_CROUCHI
    "crime_ignorePlayerWithoutTorch",       // NPC_REAGUJE_NA_HRACE_BEZ_POCHODNE
    "crime_doNotReactToEnemiesOnSight",
    "combat_neverSurrenderOrFlee",          // no flee/surrender of its own (WO-119: SKIRMISH_SOULFLEE)
};
constexpr const char* kQuietDefenceCtx[] = {
    // interrupt_attack.xml wraps a fight in Melee{Offense,Defense,Guard}AutomationDecorator
    // active = NOT these contexts: the brain re-arms, every fight, the automation that
    // WO-119's native switch turns off. These are the brain's own off switch.
    "combat_disableMeleeDefenseAutomation", // the blocks it made by itself
    "combat_disableGuardAutomation",
    "combat_disableOffenseAutomation",
    "combat_disableCombatMovement",         // its position is the stream's
};
constexpr QuietGroup kQuiet[] = {
    { kQuietSpeech,  "speech",  kQuietSpeechCtx,  0 },
    { kQuietWitness, "witness", kQuietWitnessCtx, sizeof(kQuietWitnessCtx) / sizeof(*kQuietWitnessCtx) },
    { kQuietReact,   "react",   kQuietReactCtx,   sizeof(kQuietReactCtx) / sizeof(*kQuietReactCtx) },
    { kQuietDefence, "defence", kQuietDefenceCtx, sizeof(kQuietDefenceCtx) / sizeof(*kQuietDefenceCtx) },
};
std::atomic<uint32_t> c_quietSet{0}, c_quietFail{0};

void set_quiet(Body& b, uint8_t want) {
    void* soul = body_soul(b);
    if (!soul) return;
    for (const auto& g : kQuiet) {
        const bool on = (want & g.bit) != 0, had = (b.quietApplied & g.bit) != 0;
        if (on == had) continue;
        int ok = 0, bad = 0;
        std::string missing;
        for (size_t i = 0; i < g.n; ++i) {
            const int r = kcdmp::sctx::set_soul_context(soul, g.ctx[i], on);
            if (r >= 0) ++ok; else { ++bad; if (missing.size() < 160) { missing += ' '; missing += g.ctx[i]; } }
        }
        if (g.bit == kQuietSpeech) { kcdmp::wo135::set_speaker_blocked(soul, on); ok = kcdmp::wo135::armed() ? 1 : 0; bad = ok ? 0 : 1; if (bad) missing = " the dialogue gate is not armed"; }
        c_quietSet.fetch_add(ok); c_quietFail.fetch_add(bad);
        logf("WO135-QUIET body=%s group=%s %s: %d ok, %d failed%s%s", b.key.c_str(), g.name, on ? "set" : "cleared", ok, bad,
             bad ? " --" : "", missing.c_str());
    }
    b.quietApplied = want;
}

// kcdmp_avatar_guard (buff__kcdmp.xml): imm=1 upr=1, non-persistent -- an
// avatar can never die or be knocked out in this world, whatever hits it.
unsigned char g_avatarGuard[16]{};
bool g_avatarGuardOk = false;
std::atomic<uint32_t> c_buffAdds{0};

void ensure_avatar_guard(Body& b, double now) {
    if (now - b.lastBuffCheck < 5.0) return;
    b.lastBuffCheck = now;
    // WO-154 2: on every avatar, always (0.44.0: only while the combat path was armed and mp_avatar_combat on).
    const bool wantCtx = true;
    if (wantCtx != b.ctxApplied) set_avatar_contexts(b, wantCtx);
    const uint8_t wantQuiet = g_cfgQuiet.load();   // WO-135: mp_avatar_quiet's groups
    if (wantQuiet != b.quietApplied) set_quiet(b, wantQuiet);
    if (!g_avatarGuardOk) return;
    void* soul = nullptr;
    void* fn = vslot(b.actor, kActorGetSoul);
    if (!fn || !call_p0(fn, b.actor, &soul) || !soul) return;
    if (buffs::has(soul, g_avatarGuard) > 0) return;
    if (buffs::add(soul, g_avatarGuard)) { c_buffAdds.fetch_add(1); logf("WO121-MOTION body=%s avatar guard applied (imm+upr)", b.key.c_str()); }
}

// WO-154 2: the avatar's identity at spawn, by its soul's guid (the agent's per-spawn isolate call, pipe 0x07):
// the WO-121 contexts and mp_avatar_quiet's groups (speech gate included) go on before the writer ever binds it,
// and stay until the session ends. Main thread.
struct KeptIdentity { unsigned char guid[16]; uint8_t quiet; };
std::vector<KeptIdentity> g_kept;

void set_identity_on_soul(void* soul, uint8_t quiet, bool on, const char* who) {
    int ok = 0, bad = 0;
    for (const char* n : kAvatarContexts) (kcdmp::sctx::set_soul_context(soul, n, on) >= 0 ? ok : bad)++;
    for (const auto& g : kQuiet) {
        if (!on || (quiet & g.bit)) {
            for (size_t i = 0; i < g.n; ++i) (kcdmp::sctx::set_soul_context(soul, g.ctx[i], on) >= 0 ? ok : bad)++;
            if (g.bit == kQuietSpeech) { kcdmp::wo135::set_speaker_blocked(soul, on); (kcdmp::wo135::armed() ? ok : bad)++; }
        }
    }
    c_ctxSet.fetch_add(ok); c_ctxFail.fetch_add(bad);
    logf("WO154-IDENTITY avatar soul %p %s: %d ok, %d failed (quiet=0x%X, %s)", soul, on ? "SET at spawn" : "cleared", ok, bad, quiet, who);
}

void avatar_identity(const unsigned char guid[16], bool on) {
    void* soul = rttr::find_soul_by_guid(guid);
    auto it = std::find_if(g_kept.begin(), g_kept.end(), [&](const KeptIdentity& k) { return std::memcmp(k.guid, guid, 16) == 0; });
    if (!soul) {
        logf("WO154-IDENTITY avatar soul not found by its guid (%s) -- the bind applies it later", on ? "spawn" : "removal");
        if (!on && it != g_kept.end()) g_kept.erase(it);
        return;
    }
    if (on) {
        const uint8_t quiet = g_cfgQuiet.load();
        set_identity_on_soul(soul, quiet, true, "the isolate call");
        if (it == g_kept.end()) { KeptIdentity k{}; std::memcpy(k.guid, guid, 16); k.quiet = quiet; g_kept.push_back(k); }
        else it->quiet = quiet;
    } else {
        set_identity_on_soul(soul, 0x0F, false, "the isolate call");
        if (it != g_kept.end()) g_kept.erase(it);
    }
}

void release_identities(const char* why) {
    for (const auto& k : g_kept)
        if (void* soul = rttr::find_soul_by_guid(k.guid)) set_identity_on_soul(soul, 0x0F, false, why);
    if (!g_kept.empty()) logf("WO154-IDENTITY %zu avatar identit%s released (%s)", g_kept.size(), g_kept.size() == 1 ? "y" : "ies", why);
    g_kept.clear();
}

// WO-129: the body's own class count from the engine's logical-speed manager
// (GetGameIface()+0x138 -> vtbl[0x100]; count = vtbl[0x08](soul, kind)), re-read
// every 0.5 s because the kind (actor+0x860) follows the body's state. The
// manager's mapper (vtbl[0x78]) is byte-checked as the round(x)-1 function
// before anything is trusted. -1 = unreadable (the caller then clamps to 3).
int body_range(Body& b, double now) {
    if (b.rangeAt >= 0 && now - b.rangeAt < 0.5) return b.range;
    b.rangeAt = now;
    b.range = -1;
    void* gi = engine::game_iface();
    void* holder = nullptr;
    if (!gi || !rd(gi, kGiSpeedHolder, &holder) || !holder) return -1;
    void* getMgr = vslot(holder, kHolderGetSpeedMgr);
    void* mgr = nullptr;
    if (!getMgr || !call_p0(getMgr, holder, &mgr) || !mgr) return -1;
    void* map = vslot(mgr, kSpeedMgrMap);
    if (!map) return -1;
    if (map != g_fnSpeedMap) {
        HMODULE rpg = GetModuleHandleA("RPGModule.dll");
        if (!rpg || !has(rpg, map, {0xF3, 0x0F, 0x2C, 0xC6}) || !has(rpg, map, {0xFF, 0xC8})) return -1;
        g_fnSpeedMap = map;
    }
    void* cnt = vslot(mgr, kSpeedMgrCount);
    void* soul = body_soul(b);
    int32_t kind = -1;
    uint64_t n = 0;
    if (!cnt || !soul || !rd(b.actor, kActorSpeedType, &kind) || !call_count(cnt, mgr, soul, kind, &n) || n > 16) return -1;
    b.range = static_cast<int>(n);
    return b.range;
}

// WO-129: publish this frame's gait inputs for the tag-update hook.
void publish_gait(Body& b, float cls, float vx, float vy) {
    if (!g_gaitTable.holds(b.slot, b.actor)) b.slot = g_gaitTable.insert(b.actor, g_gaitTick.load(std::memory_order_relaxed));
    if (b.slot < 0) return;
    g_gaitTable.publish(b.slot, cls, vx, vy, g_gaitTick.load(std::memory_order_relaxed));
}
void publish_gait(Body& b, float cls) { publish_gait(b, cls, b.velX, b.velY); }

void unpublish_gait(Body& b) {
    if (g_gaitTable.holds(b.slot, b.actor)) g_gaitTable.remove(b.slot);
    b.slot = -1;
}

} // namespace

// ================================================================================
void install() {
    HMODULE em = GetModuleHandleA("EntityModule.dll");
    HMODULE cm = GetModuleHandleA("CombatModule.dll");
    if (!em || !cm) { logf("WO121-MOTION DISARMED -- EntityModule/CombatModule not loaded"); return; }

    // ---- gait: C_Actor vftable slot 0x448 -------------------------------------
    A.vftActor = anchor::find_vftable(em, ".?AVC_Actor@entitymodule@wh@@", 0);
    A.fnSetPseudo = slot_fn(A.vftActor, kActorSetPseudoSpeed);
    if (!A.fnSetPseudo) g_whyGait = "RTTI C_Actor vftable not unique";
    else if (!has(em, A.fnSetPseudo, {0x48, 0x8B, 0x83, 0xE8, 0x07, 0x00, 0x00}) || !has(em, A.fnSetPseudo, {0xF3, 0x0F, 0x11, 0x70, 0x18}))
        g_whyGait = "C_Actor slot 0x448 lacks the pseudo-speed write (actor+0x7E8 -> +0x18)";
    else { g_gait = true; g_whyGait.clear(); }

    // ---- crouch / jump: C_ActorStateExpansion ----------------------------------
    A.vftExp = anchor::find_vftable(em, ".?AVC_ActorStateExpansion@entitymodule@wh@@", 0);
    A.fnSetCrouch = slot_fn(A.vftExp, kExpSetCrouch);
    A.fnGetCrouch = slot_fn(A.vftExp, kExpGetCrouch);
    A.fnRequestJump = slot_fn(A.vftExp, kExpRequestJump);
    void* holderFn = slot_fn(A.vftActor, kActorExpHolder);
    if (!A.fnSetCrouch || !A.fnGetCrouch || !A.fnRequestJump) g_whyMoves = "RTTI C_ActorStateExpansion vftable not unique";
    else if (!has(em, A.fnSetCrouch, {0x40, 0x88, 0x7B, 0x18}) || !has(em, A.fnGetCrouch, {0x0F, 0xB6, 0x43, 0x18}))
        g_whyMoves = "SetCrouch/GetCrouch lack the crouch-desire byte (+0x18)";
    else if (!has(em, A.fnRequestJump, {0xFF, 0x90, 0xF8, 0x0A, 0x00, 0x00}))
        g_whyMoves = "RequestJump lacks its crouch check (actor vtbl 0xAF8)";
    else if (!holderFn || !has(em, holderFn, {0x48, 0x8B, 0x83, 0x08, 0x03, 0x00, 0x00}))
        g_whyMoves = "actor slot 0x980 does not return the expansion holder (actor+0x308)";
    else { g_moves = true; g_whyMoves.clear(); }

    // ---- combat: C_CombatActor + the shipped test commands' Execute ------------
    A.vftCa = anchor::find_vftable(cm, ".?AVC_CombatActor@combatmodule@wh@@", 0);
    A.vftCa8 = anchor::find_vftable(cm, ".?AVC_CombatActor@combatmodule@wh@@", 8);
    A.vftCp = anchor::find_vftable(cm, ".?AVC_CombatPlayer@combatmodule@wh@@", 0);
    A.vftCp8 = anchor::find_vftable(cm, ".?AVC_CombatPlayer@combatmodule@wh@@", 8);
    A.fnTryStart = slot_fn(A.vftCa, kCaTryStartCombat);
    auto exec_of = [&](const char* rtti) -> const void* {
        void* const* v = anchor::find_vftable(cm, rtti, 0);
        return v ? v[0xE0 / 8] : nullptr;
    };
    const void* exZone = exec_of(".?AVC_SetRequestedAttackZone@combattests@combatmodule@wh@@");
    const void* exGuard = exec_of(".?AVC_SetGuardZone@combattests@combatmodule@wh@@");
    const void* exBlock = exec_of(".?AVC_SetBlockMode@combatmodule@wh@@");
    const void* exAuto = exec_of(".?AVC_EnableAutomation@combatmodule@wh@@");
    const void* exSetGuard = exec_of(".?AVC_SetGuard@combattests@combatmodule@wh@@");
    const std::vector<const void*> execs{exZone, exGuard, exBlock, exAuto};
    std::string why;
    if (!A.vftCa || !A.fnTryStart) why = "RTTI C_CombatActor vftable not unique";
    else if (!has(cm, A.fnTryStart, {0xFF, 0x90, 0xD0, 0x06, 0x00, 0x00})) why = "slot 0x360 does not call StartCombatMode (+0x6D0)";
    else if (!exZone || !exGuard || !exBlock || !exAuto || !exSetGuard) why = "a combat test command's RTTI vftable is missing";
    else {
        A.fnSetAtkZone = const_cast<uint8_t*>(unique_target(cm, exZone, execs));
        A.fnSetGuardZone = const_cast<uint8_t*>(unique_target(cm, exGuard, execs));
        A.fnSetBlock = const_cast<uint8_t*>(unique_target(cm, exBlock, execs));
        // EnableAutomation's own target: the one that fetches the automation manager (ca vtbl 0x2C8).
        const uint8_t* at[64]{}; int nat = anchor::function_call_targets(cm, exAuto, at, 64);
        for (int i = 0; i < nat; ++i)
            if (has(cm, at[i], {0xFF, 0x92, 0xC8, 0x02, 0x00, 0x00}) || has(cm, at[i], {0xFF, 0x90, 0xC8, 0x02, 0x00, 0x00})) { A.fnAuto = const_cast<uint8_t*>(at[i]); break; }
        // SetFlag: called by C_SetGuard::Execute AND by the string-anchored ClearStandardGuard.
        const uint8_t* clear = anchor::function_by_string(cm, "Player standard guard request cleared");
        const uint8_t* sg[64]{}; int nsg = anchor::function_call_targets(cm, exSetGuard, sg, 64);
        for (int i = 0; clear && i < nsg; ++i)
            if (anchor::function_calls(cm, clear, sg[i]) && has(cm, sg[i], {0x48, 0x63, 0xEA})) { A.fnSetFlag = const_cast<uint8_t*>(sg[i]); break; }
        // Actor lookup: the Execute bodies resolve gi+0x188 -> vtbl[0x18](eid) -> vtbl[0x970].
        A.actorLookup = has(cm, exGuard, {0x48, 0x8B, 0x88, 0x88, 0x01, 0x00, 0x00}) && has(cm, exGuard, {0xFF, 0x50, 0x18})
                     && has(cm, exGuard, {0x48, 0x8B, 0x91, 0x70, 0x09, 0x00, 0x00});
        if (!A.fnSetAtkZone || !A.fnSetGuardZone || !A.fnSetBlock) why = "a setter is not the unique call of its test command";
        else if (!A.fnAuto) why = "no EnableAutomation target fetches the automation manager (ca vtbl 0x2C8)";
        else if (!A.fnSetFlag) why = "SetFlag is not the common call of C_SetGuard::Execute and ClearStandardGuard";
        else if (!A.actorLookup) why = "the test command's actor lookup (gi+0x188 -> 0x18 -> 0x970) did not verify";
    }
    if (why.empty()) {
        g_autoCmd = VirtualAlloc(nullptr, 0x100, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        g_autoCmdOn = VirtualAlloc(nullptr, 0x100, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (g_autoCmdOn) { auto* c = static_cast<uint8_t*>(g_autoCmdOn); c[0x79] = c[0x7A] = c[0x7B] = 1; }
        if (!g_autoCmd || !g_autoCmdOn) why = "no memory for the automation stand-in";
    }
    if (why.empty()) { g_combat = true; g_whyCombat.clear(); } else g_whyCombat = why;
    if (!A.actorLookup) { g_gait = false; if (g_whyGait.empty()) g_whyGait = "actor lookup did not verify"; g_moves = false; if (g_whyMoves.empty()) g_whyMoves = "actor lookup did not verify"; }

    // ---- WO-129: the tag-update hook (C_Actor vftable slot 0xC98) --------------
    // Without it a written body slides whatever SetPseudoSpeed we call (observed),
    // so the gait piece is armed only with it: otherwise the Lua clip walk stays.
    {
        const void* tags = slot_fn(A.vftActor, kActorUpdateTags);
        std::string whyT;
        if (!tags) whyT = "RTTI C_Actor vftable has no slot 0xC98";
        else if (!has(em, tags, {0x48, 0x8B, 0x91, 0x50, 0x04, 0x00, 0x00}))          // mov rdx,[rcx+0x450] (GetPseudoSpeed)
            whyT = "slot 0xC98 does not read GetPseudoSpeed (+0x450)";
        else if (!has(em, tags, {0xF3, 0x0F, 0x10, 0x8F, 0x74, 0x05, 0x00, 0x00}))     // movss xmm1,[rdi+0x574] (requested velocity)
            whyT = "slot 0xC98 does not read the requested velocity (+0x574)";
        else if (!has(em, tags, {0xF2, 0x44, 0x0F, 0x10, 0x87, 0x14, 0x06, 0x00, 0x00})) // movsd xmm8,[rdi+0x614] (move vector)
            whyT = "slot 0xC98 does not read the move vector (+0x614)";
        else if (!has(em, tags, {0x48, 0x8D, 0x8F, 0x50, 0x08, 0x00, 0x00}))          // lea rcx,[rdi+0x850] (stance manager)
            whyT = "slot 0xC98 does not reach the stance manager (+0x850)";
        else {
            const char* why = nullptr;
            if (inlinehook::install_this(const_cast<void*>(tags), kTagsPrologue, sizeof(kTagsPrologue), &on_update_tags, &why)) g_tags = true;
            else whyT = std::string("hook refused: ") + (why ? why : "?");
        }
        g_whyTags = g_tags ? "" : whyT;
        char dt[64]{}; anchor::describe(tags, dt, sizeof dt);
        logf("WO129-GAIT tag hook %s at %s%s%s", g_tags ? "installed" : "NOT installed", dt, whyT.empty() ? "" : " -- ", whyT.c_str());
        if (!g_tags && g_gait) { g_gait = false; g_whyGait = "no tag-update hook (" + whyT + ")"; }
    }

    // ---- capture: EnterImpl on the four action classes, RequestJump ------------
    A.vftAttack = anchor::find_vftable(cm, ".?AVC_CombatActorActionAttack@combatmodule@wh@@", 0);
    A.vftDodge = anchor::find_vftable(cm, ".?AVC_CombatActorActionDodge@combatmodule@wh@@", 0);
    A.vftPerfect = anchor::find_vftable(cm, ".?AVC_CombatActorActionPerfectBlock@combatmodule@wh@@", 0);
    A.vftBlock = anchor::find_vftable(cm, ".?AVC_CombatActorActionBlock@combatmodule@wh@@", 0);
    A.vftHit = anchor::find_vftable(cm, ".?AVC_CombatActorActionHit@combatmodule@wh@@", 0);
    A.vftSyncAttack = anchor::find_vftable(cm, ".?AVC_CombatActorActionSyncAttack@combatmodule@wh@@", 0);
    // WO-151: the two new classes are hooked only when their own constructor stores the combat
    // actor at +0x78 (the field capture() reads): checked like the attack's, by the function
    // that writes the class's vftable and holds the store (CombatModule 1.5.5: the hit ctor's
    // base part `mov [rbx+0x78], rbp`, the sync attack's `mov [rbx+0x78], rsi`).
    auto ctorStoresActor = [&](void* const* vftClass) -> bool {
        static const uint8_t kStoreRbp[] = {0x48, 0x89, 0x6B, 0x78};   // mov [rbx+0x78], rbp
        static const uint8_t kStoreRsi[] = {0x48, 0x89, 0x73, 0x78};   // mov [rbx+0x78], rsi
        anchor::Range tx{};
        if (!vftClass || !anchor::section(cm, ".text", &tx)) return false;
        const auto* target = reinterpret_cast<const uint8_t*>(vftClass);
        for (const uint8_t* q = tx.begin; q + 7 <= tx.end; ++q) {
            if ((q[0] & 0xF8) != 0x48 || q[1] != 0x8D || (q[2] & 0xC7) != 0x05) continue;
            int32_t d; std::memcpy(&d, q + 3, 4);
            if (q + 7 + d != target) continue;
            if (anchor::function_has_bytes(cm, q, kStoreRbp, sizeof kStoreRbp) || anchor::function_has_bytes(cm, q, kStoreRsi, sizeof kStoreRsi))
                return true;
        }
        return false;
    };
    const bool hitOk = ctorStoresActor(A.vftHit), syncOk = ctorStoresActor(A.vftSyncAttack);
    logf("WO151-CAPTURE hit reactions: C_CombatActorActionHit %s, C_CombatActorActionSyncAttack %s",
         !A.vftHit ? "has no RTTI vftable" : hitOk ? "armed (its ctor stores the combat actor at +0x78)" : "REFUSED (no ctor store at +0x78)",
         !A.vftSyncAttack ? "has no RTTI vftable" : syncOk ? "armed (its ctor stores the combat actor at +0x78)" : "REFUSED (no ctor store at +0x78)");
    void* const* vfts[kClsCount] = {A.vftAttack, A.vftDodge, A.vftPerfect, A.vftBlock, hitOk ? A.vftHit : nullptr, syncOk ? A.vftSyncAttack : nullptr};
    std::string whyCap;
    if (!A.vftAttack || !A.vftCa) whyCap = "RTTI C_CombatActorActionAttack / C_CombatActor vftable missing";
    else {
        // The attack ctor stores the combat actor at +0x78: the one fact the
        // owner check rests on, verified in the class's own constructor.
        const uint8_t* ctor = nullptr;
        // (the ctor is the function that writes this vftable; find it by the vftable reference + the store)
        static const uint8_t kStore[] = {0x48, 0x89, 0x6B, 0x78};   // mov [rbx+0x78], rbp
        HMODULE m = cm;
        const uint8_t* cands[1]{};
        (void)cands; (void)ctor;
        bool ctorOk = false;
        // Search .text for the one function that references the vftable and holds the store.
        anchor::Range text{};
        if (anchor::section(m, ".text", &text)) {
            const auto* vft = reinterpret_cast<const uint8_t*>(A.vftAttack);
            for (const uint8_t* q = text.begin; q + 7 <= text.end && !ctorOk; ++q) {
                if ((q[0] & 0xF8) != 0x48 || q[1] != 0x8D || (q[2] & 0xC7) != 0x05) continue;
                int32_t d; std::memcpy(&d, q + 3, 4);
                if (q + 7 + d != vft) continue;
                if (anchor::function_has_bytes(m, q, kStore, sizeof(kStore))) ctorOk = true;
            }
        }
        if (!ctorOk) whyCap = "no C_CombatActorActionAttack constructor stores the combat actor at +0x78";
    }
    if (whyCap.empty()) {
        g_thunks = static_cast<uint8_t*>(VirtualAlloc(nullptr, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
        if (!g_thunks) whyCap = "no memory for the capture thunks";
    }
    int patched = 0;
    if (whyCap.empty()) {
        for (int c = 0; c < kClsCount; ++c) {
            if (!vfts[c]) continue;
            void* cur = vfts[c][kActionEnterImpl / 8];
            if (!cur) continue;
            uint8_t* th = g_thunks + c * 128;
            make_thunk(th, static_cast<uint64_t>(c), cur);
            FlushInstructionCache(GetCurrentProcess(), th, 128);
            void* orig = nullptr;
            if (patch_slot(vfts[c], kActionEnterImpl, th, &orig)) { g_origEnter[c] = orig; ++patched; }
        }
        if (!g_origEnter[kClsAttack]) whyCap = "the attack EnterImpl slot could not be patched";
    }
    if (whyCap.empty() && g_moves) {
        void* orig = nullptr;
        if (patch_slot(A.vftExp, kExpRequestJump, reinterpret_cast<void*>(&hk_request_jump), &orig)) g_origJump = reinterpret_cast<RequestJumpFn>(orig);
    }
    if (whyCap.empty()) { g_capture = true; g_whyCapture.clear(); } else g_whyCapture = whyCap;

    g_avatarGuardOk = buffs::parse_guid("4b43444d-7121-4d67-b1a5-9e2f6d8c0a15", g_avatarGuard) && buffs::ready();

    char dp[64]{}, dc[64]{}, dj[64]{}, dt[64]{}, da[64]{}, df[64]{}, dg[64]{}, dz[64]{}, db[64]{};
    anchor::describe(A.fnSetPseudo, dp, sizeof dp); anchor::describe(A.fnSetCrouch, dc, sizeof dc); anchor::describe(A.fnRequestJump, dj, sizeof dj);
    anchor::describe(A.fnTryStart, dt, sizeof dt); anchor::describe(A.fnAuto, da, sizeof da); anchor::describe(A.fnSetFlag, df, sizeof df);
    anchor::describe(A.fnSetGuardZone, dg, sizeof dg); anchor::describe(A.fnSetAtkZone, dz, sizeof dz); anchor::describe(A.fnSetBlock, db, sizeof db);
    log_piece("gait", g_gait, g_whyGait);
    log_piece("crouch_jump", g_moves, g_whyMoves);
    log_piece("combat", g_combat, g_whyCombat);
    log_piece("capture", g_capture, g_whyCapture);
    logf("WO121-MOTION gait=%s moves=%s combat=%s capture=%s(enter_patched=%d jump_hook=%s) avatar_guard=%s "
         "set_pseudo=%s set_crouch=%s request_jump=%s try_start=%s automation=%s set_flag=%s set_guard_zone=%s set_atk_zone=%s set_block=%s",
         g_gait ? "armed" : "off", g_moves ? "armed" : "off", g_combat ? "armed" : "off", g_capture ? "armed" : "off", patched,
         g_origJump ? "on" : "off", g_avatarGuardOk ? "ready" : "unavailable", dp, dc, dj, dt, da, df, dg, dz, db);
}

uint8_t on_config(const uint8_t* body, size_t len) {
    if (len < 5 || len > 7) return 8;
    g_cfgAvatarGait = body[0] != 0; g_cfgNpcGait = body[1] != 0; g_cfgMoves = body[2] != 0;
    g_cfgCombat = body[3] != 0; g_cfgNpcRows = body[4] != 0;
    if (len >= 6) g_cfgQuiet = static_cast<uint8_t>(body[5] & 0x0F);   // WO-135
    if (len >= 7) g_cfgGaitHyst = body[6] != 0;                         // WO-154 5
    g_cfgChanged = true;
    logf("WO121-MOTION config avatar_gait=%d npc_gait=%d avatar_moves=%d avatar_combat=%d npc_rows=%d quiet=0x%X gait_hysteresis=%d",
         body[0] != 0, body[1] != 0, body[2] != 0, body[3] != 0, body[4] != 0, g_cfgQuiet.load(), g_cfgGaitHyst.load() ? 1 : 0);
    return 0;
}

void rearm_nudge(uint32_t eid, const char* why) {
    auto it = g_bodies.find(eid);
    if (it == g_bodies.end() || !it->second.avatar) return;
    Body& b = it->second;
    b.nudged = false;
    b.attachAt = npcdrive::now_s() - kNudgeAfterS;   // the pulse starts at once and runs kNudgeS
    logf("WO160-NUDGE body=%s: %s -- the walk-class pulse runs again (an avatar's locomotion graph can stand in a T-pose after a loop ends)",
         b.key.c_str(), why ? why : "?");
}

uint8_t on_avatar_event(const uint8_t* body, size_t len) {
    if (len != 5) return 8;
    PendingEvent e{ body[0], 0 };
    std::memcpy(&e.eid, body + 1, 4);
    std::lock_guard<std::mutex> lock(g_evMutex);
    if (g_events.size() < 64) g_events.push_back(e);
    return 0;
}

void body_frame(const char* key, void* ent, uint32_t eid, float renderSpeedMps, float renderVx, float renderVy,
                const State2* st, double stAgeS, double now) {
    Body& b = g_bodies[eid];
    if (b.ent != ent || b.eid != eid) {
        unpublish_gait(b);   // WO-129: never leave a slot naming a body we no longer drive
        b = Body{};
        b.eid = eid; b.ent = ent; b.key = key; b.avatar = is_avatar_key(key);
        b.attachAt = now;
        b.actor = actor_by_eid(eid);
        if (b.actor && b.avatar) b.ca = combat_actor_of(b.actor, true);
        if (b.actor) b.exp = expansion_of(b.actor);
        logf("WO121-MOTION body=%s eid=0x%X attach avatar=%d actor=%s ca=%s exp=%s", key, eid, b.avatar ? 1 : 0,
             b.actor ? "yes" : "NO", b.ca ? "yes" : "no", b.exp ? "yes" : "no");
        if (b.avatar && b.actor) {
            void* soul = nullptr;
            void* fn = vslot(b.actor, kActorGetSoul);
            if (fn && call_p0(fn, b.actor, &soul) && soul) hits::note_avatar(eid, soul, true);
        }
    }
    if (!b.actor) return;
    const bool fresh = st && stAgeS < 1.5;
    // gait
    const bool gaitOn = g_gait && (b.avatar ? g_cfgAvatarGait.load() : g_cfgNpcGait.load());
    if (gaitOn) {
        float s = (b.avatar && fresh) ? st->speedCm / 100.0f : renderSpeedMps;
        // A render step faster than any gait (> 9 m/s) is a snap or a teleport,
        // never a pace: it keeps the previous speed instead of a sprint burst.
        const bool snap = !(renderVx * renderVx + renderVy * renderVy < 81.0f);
        if (!(b.avatar && fresh) && s > 9.0f) s = b.speedEma;
        if (!(b.avatar && fresh)) {
            // The rendered speed is per-frame noisy at 60+ fps: smooth it (tau ~0.15 s).
            b.speedEma += (s - b.speedEma) * 0.2f;
            s = b.speedEma < 0.05f ? 0.0f : b.speedEma;
        }
        // WO-129: the direction tag's input, the rendered velocity (tau ~0.12 s).
        if (!snap) { b.velX += (renderVx - b.velX) * 0.25f; b.velY += (renderVy - b.velY) * 0.25f; }
        // WO-129: pseudo-speed is a logical speed CLASS; clamp it to this body's own range.
        const int range = body_range(b, now);
        // WO-154 5: with hysteresis (a pace on a boundary keeps its class)
        float cls = gait::clamp_class(g_cfgGaitHyst.load() ? gait::speed_class_hyst(s, b.cls) : gait::speed_class(s), range);
        // WO-143: a copy the host shows hoeing creeps along its row (H1: 0.08-0.10 m/s, under the walking
        // floor; read as standing, it stood with its hoe). While it creeps it walks, and the tags see the
        // pace the game hoes at (J1: 0.4 m/s showed the hoeing walk; the stream still places the body).
        float tvx = b.velX, tvy = b.velY;
        if (!b.avatar && wo143::activity_locomotion(eid) && wo143rules::hoe_tags(b.velX, b.velY, &tvx, &tvy) && cls < 1.0f)
            cls = gait::clamp_class(1.0f, range);
        // WO-155: an avatar bound to this writer stands in a T-pose for 5-8 s (live: a fresh spawn, and the body of a figure
        // that fell, taken back): its locomotion graph does not start until something moves it, and a standing stream
        // never does. A walk-class pulse of 0.6 s at 0.3 m/s, 0.1 s after the bind, starts it (live: the same pulse from a
        // partner's 0.6 s step cleared the pose at once and the idle that followed was normal). Only an avatar that is
        // still, once per bind; a partner already walking never needs it.
        if (b.avatar && !b.nudged) {
            const double age = now - b.attachAt;
            if (cls >= 1.0f) b.nudged = true;
            else if (age >= kNudgeAfterS && age < kNudgeAfterS + kNudgeS) {
                if (b.cls < 1.0f) logf("WO155-NUDGE body=%s: a %.2f s walk-class pulse starts its locomotion graph (a freshly bound avatar stands in a T-pose until then)", b.key.c_str(), kNudgeS);
                cls = gait::clamp_class(1.0f, range);
                // along the body's own facing: the direction tag reads a velocity, and none reads as no gait at all
                const float yaw = kcdmp::npcdrive::entity_yaw(b.ent);
                tvx = -std::sin(yaw) * kNudgeMps; tvy = std::cos(yaw) * kNudgeMps;
            } else if (age >= kNudgeAfterS + kNudgeS) b.nudged = true;
        }
        b.cls = cls;
        apply_gait(b, cls);
        publish_gait(b, cls, tvx, tvy);
        if (b.avatar && now - b.lastGaitLog >= 2.0) {
            b.lastGaitLog = now;
            void* comp = nullptr; float back = -1.0f;
            if (rd(b.actor, kActorPseudoComp, &comp) && comp) rd(comp, 0x18, &back);
            logf("WO121-GAIT body=%s state=%s age_s=%.2f stream_cm_s=%u render_mps=%.2f speed_mps=%.2f class=%.0f range=%d readback=%.2f tags_applied=%u",
                 b.key.c_str(), st ? (fresh ? "fresh" : "stale") : "none", st ? stAgeS : -1.0, st ? st->speedCm : 0,
                 renderSpeedMps, s, cls, range, back, c_tagApplied.load(std::memory_order_relaxed));
        }
    } else if (b.gaitWritten || b.slot >= 0) release_gait(b);
    if (!b.avatar) {
        // WO-132: an engaged copy holds the host NPC's combat state (never its
        // own brain's: the copy stays suspended and driven).
        auto eg = g_engage.find(eid);
        const bool hold = eg != g_engage.end() && now - eg->second.at < kEngageStaleS && g_combat;
        if (hold) {
            if (!b.combatHeld) c_engageHolds.fetch_add(1);
            apply_combat(b, &eg->second.st, now);
        } else if (b.automationOff || b.combatHeld) {
            release_body(b, "engagement over");
            c_engageReleases.fetch_add(1);
        }
        return;
    }
    ensure_avatar_guard(b, now);
    // crouch
    if (g_moves && g_cfgMoves) {
        const bool want = fresh && (st->bits & kBitCrouch);
        if (want != b.crouchApplied) apply_crouch(b, want);
    } else if (b.crouchApplied) apply_crouch(b, false);
    // combat
    if (g_combat && g_cfgCombat) apply_combat(b, fresh ? st : nullptr, now);
    else if (b.automationOff || b.combatHeld) release_body(b, "toggle off");
}

void identity(const unsigned char guid[16], bool on) { avatar_identity(guid, on); }
void on_pipe_closed() { release_identities("the agent went away"); }

void body_released(const char* key, uint32_t eid) {
    auto it = g_bodies.find(eid);
    if (it == g_bodies.end()) return;
    if (it->second.avatar) hits::note_avatar(eid, nullptr, false);
    unpublish_gait(it->second);   // WO-129: even when the entity is already gone
    if (it->second.actor && engine::entity_by_id(eid) == it->second.ent) release_body(it->second, key);
    g_bodies.erase(it);
}

// WO-154 2: the local player's body is down when its physics is no living entity: the ragdoll of a
// knockdown (TakeDamage's own, WO-151 L2: on his back until he stands) or of a knockout. The partner's
// screen shows the avatar fall, lie and stand up on this bit's edges. Logged on each edge.
// Debounced here, where every read sees it: down after 150 ms of no living physics, up after 300 ms of
// living physics again (the state block travels only on a change, so the receiver cannot debounce).
static bool s_down = false;                  // WO-155: read by hits.cpp (the friendly-fire knockdown window)
static double s_lastDownEdge = -1e9;
bool local_down_now() { return s_down; }
double local_last_down_edge_s() { return s_lastDownEdge; }

void note_local_downed(State2* out) {
    static double s_since = -1;   // when the raw reading last started to differ from s_down
    void* pe = engine::entity_by_id(0x7777);
    kcdmp::npcdrive::PhysicsStatus ps{};
    if (pe && kcdmp::npcdrive::physics_status(pe, &ps) && ps.present) {
        const bool raw = !ps.living;
        const double now = kcdmp::npcdrive::now_s();
        if (raw == s_down) s_since = -1;
        else {
            if (s_since < 0) s_since = now;
            if (now - s_since >= (raw ? 0.15 : 0.30)) {
                s_down = raw; s_since = -1;
                if (raw) s_lastDownEdge = now;
                logf("WO154-DOWN the local player is %s (physics %s)", raw ? "DOWN" : "up again", ps.living ? "living" : "not a living entity");
            }
        }
    }
    if (s_down) out->bits |= kBitDowned;
}

bool read_local_state2(State2* out, float facingYaw) {
    *out = State2{};
    void* actor = player_actor();
    if (!actor) return false;
    float v[3]{};
    if (!rd(actor, kActorReqVel, &v[0]) || !rd(actor, kActorReqVel + 4, &v[1])) return false;
    const float speed = std::sqrt(v[0] * v[0] + v[1] * v[1]);
    if (!std::isfinite(speed)) return false;
    out->speedCm = static_cast<uint16_t>(speed * 100.0f > 65535.0f ? 65535 : speed * 100.0f + 0.5f);
    // Facing is the yaw local_state read from the entity matrix in the same
    // call; heading is the requested velocity's, in the same convention
    // (forward = (-sin yaw, cos yaw)).
    if (speed > 0.05f && std::isfinite(facingYaw)) {
        const float facing = facingYaw;
        const float heading = std::atan2(-v[0], v[1]);
        float d = heading - facing;
        while (d > 3.14159265f) d -= 6.2831853f;
        while (d <= -3.14159265f) d += 6.2831853f;
        int q = static_cast<int>(std::lround(d * 128.0f / 3.14159265f));
        if (q > 127) q -= 256;
        out->moveDir = static_cast<int8_t>(q);
    }
    void* ca = combat_actor_of(actor, false);
    g_playerCa = ca;
    note_local_downed(out);   // WO-154 2
    if (A.vftExp) {
        void* exp = expansion_of(actor);
        g_playerExp = exp;
        uint8_t cr = 0;
        const bool readOk = exp && vslot(exp, kExpGetCrouch) == A.fnGetCrouch && call_ret_u8(A.fnGetCrouch, exp, &cr);
        // WO-135: and the engine's rendered stance -- the Mannequin Stance tag
        // `stealth` a crouch sets (WO-119) -- whichever setter the key used.
        kcdmp::mannequin::BodyState bs{};
        const bool tagOk = kcdmp::mannequin::read_body_state(true, 0, &bs);
        const bool tagCrouch = tagOk && bs.stance == kcdmp::mannequin::kStanceStealth;
        // WO-136: the actor's own crouch (the key's path), first.
        bool stance = false;
        const bool stanceOk = actor_is_crouched(actor, &stance);
        const bool byteC = readOk && cr != 0;
        const bool any = (stanceOk && stance) || byteC || tagCrouch;
        if (any) out->bits |= kBitCrouch;
        char src[32];
        std::snprintf(src, sizeof src, "%s%s%s%s%s", (stanceOk && stance) ? "stance" : "", ((stanceOk && stance) && byteC) ? "+" : "",
                      byteC ? "byte" : "", (((stanceOk && stance) || byteC) && tagCrouch) ? "+" : "", tagCrouch ? "tag" : "");
        note_local_crouch(actor, exp, readOk || tagOk || stanceOk, any, src);
    }
    if (ca) {
        void* model = nullptr;
        if (rd(ca, kCaModel, &model) && model) {
            uint8_t cm = 0; int32_t gz = -1, gs = -1, az = -1; uint8_t blk = 0; void* opp = nullptr;
            if (prop_value(model, kPropCombatMode, &cm) && cm) out->bits |= kBitCombat;
            if (prop_value(model, kPropGuardZone, &gz)) out->guardZone = static_cast<uint8_t>(gz + 1 < 0 ? 0 : gz + 1);
            if (prop_value(model, kPropGuardStance, &gs)) out->guardStance = static_cast<uint8_t>(gs + 1 < 0 ? 0 : gs + 1);
            if (prop_value(model, kPropReqAtkZone, &az)) out->atkZone = static_cast<uint8_t>(az + 1 < 0 ? 0 : az + 1);
            if (rd(model, kModelBlockMax, &blk) && blk) out->bits |= kBitBlock;
            if (rd(model, kModelOpponent, &opp) && opp) out->bits |= kBitLocked;
        }
    }
    return true;
}

void tick() {
    g_gaitTick.fetch_add(1, std::memory_order_relaxed);   // WO-129: the gait table's freshness clock
    // Keep the player's combat actor / expansion fresh for the capture hooks
    // (a load replaces them). Cheap: two virtual calls a frame.
    if (void* actor = player_actor()) {
        g_playerCa = combat_actor_of(actor, false);
        if (A.vftExp) g_playerExp = expansion_of(actor);
    }
    std::vector<PendingEvent> evs;
    { std::lock_guard<std::mutex> lock(g_evMutex); evs.swap(g_events); }
    for (const auto& e : evs) {
        if (e.kind == 2) { rearm_nudge(e.eid, "a loop was stopped (the agent)"); continue; }   // WO-160
        if (e.kind != 1) continue;
        if (!g_moves || !g_cfgMoves) continue;
        auto it = g_bodies.find(e.eid);
        void* actor = it != g_bodies.end() ? it->second.actor : actor_by_eid(e.eid);
        void* exp = it != g_bodies.end() && it->second.exp ? it->second.exp : (actor ? expansion_of(actor) : nullptr);
        bool ok = false;
        if (exp && vslot(exp, kExpRequestJump) == reinterpret_cast<void*>(&hk_request_jump) && g_origJump) {
            if (!call_jump(g_origJump, exp, &ok)) { ok = false; c_faults.fetch_add(1); }
        } else if (exp && vslot(exp, kExpRequestJump) == A.fnRequestJump) {
            call_ret_b(A.fnRequestJump, exp, &ok);
        }
        (ok ? c_jumps : c_jumpFail).fetch_add(1);
        logf("WO121-MOTION eid=0x%X jump -> %s", e.eid, ok ? "accepted" : "refused");
    }
    std::vector<Captured> caps;
    { std::lock_guard<std::mutex> lock(g_capMutex); caps.swap(g_captured); }
    if (ActionFn fn = g_actionFn.load())
        for (const auto& c : caps) fn(c.kind, 1, c.ic, c.zone, c.type, c.flags, c.guid, c.eid, c.name, c.hasAlt ? c.alt : nullptr);
    if (g_cfgChanged.exchange(false) && !(g_cfgCombat && g_cfgAvatarGait && g_cfgMoves)) {
        for (auto& kv : g_bodies) {
            Body& b = kv.second;
            if (!b.avatar) continue;
            if (!g_cfgCombat && (b.automationOff || b.combatHeld)) release_body(b, "toggle off");
            if (!g_cfgAvatarGait && b.gaitWritten) release_gait(b);
            if (!g_cfgMoves && b.crouchApplied) apply_crouch(b, false);
        }
    }
}

void set_action_callback(ActionFn fn) { g_actionFn.store(fn); }

int status_text_motion(char* out, int n);

int status_text(char* out, int n) {
    int m = status_text_motion(out, n);
    if (m > 0 && m < n - 2) { out[m++] = ' '; m += kcdmp::wo135::status_text(out + m, n - m); }   // WO-135: the dialogue gate
    return m;
}

int status_text_motion(char* out, int n) {
    return std::snprintf(out, n,
        "gait=%s moves=%s combat=%s attack_capture=%s cfg=%d%d%d%d%d bodies=%zu gait_writes=%u crouch=%u jumps=%u/%u "
        "combat_starts=%u automation_off=%u guard_zone=%u atk_zone=%u block=%u cap_attack=%u cap_npc=%u cap_jump=%u cap_other=%u "
        "cap_dropped=%u buff_adds=%u ctx_set=%u ctx_fail=%u faults=%u tags=%s tags_applied=%u gait_slots=%d "
        "cap_drop_notca=%u cap_drop_nodesc=%u cap_drop_noguid=%u cap_drop_noowner=%u cap_via_base8=%u cap_ours=%u "
        "engaged=%zu engage_holds=%u engage_releases=%u cap_crouch=%u cap_stance=%u crouch_query=%s crouch_fail=%u quiet=0x%X quiet_set=%u quiet_fail=%u "
        "cap_hit=%u cap_sync=%u cap_drop_rowtype=%u",
        g_gait ? "armed" : "off", g_moves ? "armed" : "off", g_combat ? "armed" : "off", g_capture ? "armed" : "off",
        g_cfgAvatarGait.load(), g_cfgNpcGait.load(), g_cfgMoves.load(), g_cfgCombat.load(), g_cfgNpcRows.load(), g_bodies.size(),
        c_gaitWrites.load(), c_crouch.load(), c_jumps.load(), c_jumpFail.load(), c_combatStarts.load(), c_autoOff.load(),
        c_guardZone.load(), c_atkZone.load(), c_block.load(), c_capAttack.load(), c_capNpc.load(), c_capJump.load(), c_capOther.load(),
        c_capDropped.load(), c_buffAdds.load(), c_ctxSet.load(), c_ctxFail.load(), c_faults.load(),
        g_tags ? "armed" : "off", c_tagApplied.load(), g_gaitTable.live(),
        c_dropNotCa.load(), c_dropNoDesc.load(), c_dropNoGuid.load(), c_dropNoOwner.load(), c_capViaBase8.load(), c_capOurs.load(),
        g_engage.size(), c_engageHolds.load(), c_engageReleases.load(),
        c_capCrouch.load(), c_capStance.load(), g_isCrouchedState == 1 ? "armed" : g_isCrouchedState < 0 ? "refused" : "unread",
        c_crouchApplyFail.load(), g_cfgQuiet.load(), c_quietSet.load(), c_quietFail.load(),
        c_capHit.load(), c_capSync.load(), c_dropRowType.load());
}

// WO-135 test verb: the player's own crouch setter -- the function the crouch
// key reaches (WO-119: toggle_crouch -> state expansion slot 0xE8 SetCrouch),
// called with the key's arguments. No input is sent anywhere.
bool player_set_crouch(bool on) {
    void* actor = player_actor();
    void* exp = actor ? expansion_of(actor) : nullptr;
    if (!exp || vslot(exp, kExpSetCrouch) != A.fnSetCrouch) return false;
    return call_bb(A.fnSetCrouch, exp, on, false);
}

bool test_fight(uint32_t eid) {
    if (!g_combat) return false;
    void* actor = actor_by_eid(eid);
    void* ca = actor ? combat_actor_of(actor, true) : nullptr;
    if (!ca) return false;
    uint64_t r = 0;
    if (!call_trystart(A.fnTryStart, ca, &r)) return false;
    void* model = nullptr;
    if (rd(ca, kCaModel, &model) && model) call_setflag(A.fnSetFlag, static_cast<char*>(model) + kModelFlags, kGuardRequestScope, 1);
    return call_auto(A.fnAuto, g_autoCmdOn, ca, true);
}

bool player_automation(bool on) {
    if (!g_combat) return false;
    void* actor = player_actor();
    void* ca = actor ? combat_actor_of(actor, true) : nullptr;
    if (!ca) return false;
    void* model = nullptr;
    const bool haveModel = rd(ca, kCaModel, &model) && model;
    if (on) {
        uint64_t r = 0;
        if (!call_trystart(A.fnTryStart, ca, &r)) return false;
        if (haveModel) call_setflag(A.fnSetFlag, static_cast<char*>(model) + kModelFlags, kGuardRequestScope, 1);
        return call_auto(A.fnAuto, g_autoCmdOn, ca, true);
    }
    if (haveModel) call_setflag(A.fnSetFlag, static_cast<char*>(model) + kModelFlags, kGuardRequestScope, 0);
    return call_auto(A.fnAuto, g_autoCmd, ca, false);
}

bool player_block(bool on) {
    void* pca = g_playerCa.load();
    if (!g_combat || !pca || !A.fnSetBlock) return false;
    return call_setblock(A.fnSetBlock, pca, on, 0);
}

void set_npc_engage(uint32_t eid, bool on, const State2* st, double now) {
    if (!on) { g_engage.erase(eid); return; }
    Engage& e = g_engage[eid];
    if (st) e.st = *st;
    e.st.bits |= kBitCombat;
    e.at = now;
}

bool npc_engaged(uint32_t eid) { return g_engage.count(eid) != 0; }
size_t npc_engaged_count() { return g_engage.size(); }

bool read_npc_combat(uint32_t eid, NpcCombat* out) {
    *out = NpcCombat{};
    void* actor = actor_by_eid(eid);
    if (!actor) return false;
    void* ca = combat_actor_of(actor, false);
    if (!ca) return true;   // no combat actor yet: not in a fight
    out->hasCa = 1;
    void* model = nullptr;
    if (!rd(ca, kCaModel, &model) || !model) return true;
    uint8_t mode = 0;
    if (prop_named(model, kPropCombatMode)) rd(model, kPropCombatMode.off + 8, &mode);
    out->combat = mode ? 1 : 0;
    int32_t v = -1;
    if (prop_value(model, kPropGuardZone, &v)) out->guardZone = static_cast<int8_t>(v);
    if (prop_value(model, kPropGuardStance, &v)) out->guardStance = static_cast<int8_t>(v);
    if (prop_value(model, kPropReqAtkZone, &v)) out->atkZone = static_cast<int8_t>(v);
    uint8_t blk = 0;
    rd(model, kModelBlockMax, &blk);
    out->block = blk ? 1 : 0;
    void* opp = nullptr;
    if (rd(model, kModelOpponent, &opp) && opp) {
        if (void* oca = as_combat_actor(opp)) {
            void* pca = g_playerCa.load();
            if (pca && oca == pca) out->opponentIsPlayer = 1;
            void* owner = nullptr;
            uint32_t oeid = 0;
            if (rd(oca, kCaOwnerEntity, &owner) && owner && rd(owner, kActorEntityId, &oeid)) out->opponentEid = oeid;
        }
    }
    return true;
}

bool read_model(uint32_t eid, ModelRead* out) {
    *out = ModelRead{};
    void* actor = actor_by_eid(eid);
    if (!actor) return false;
    void* ca = combat_actor_of(actor, false);
    if (!ca) return true;
    out->hasCa = 1;
    void* pca = g_playerCa.load();
    out->isPlayerCa = (pca && ca == pca) ? 1 : 0;
    void* model = nullptr;
    if (!rd(ca, kCaModel, &model) || !model) return true;
    out->hasModel = 1;
    auto i32 = [&](const Prop& p, int32_t* dst, uint16_t bit) { if (prop_value(model, p, dst)) out->valid |= bit; };
    i32(kPropState, &out->state, kMvState);
    i32(kPropGuardZone, &out->guardZone, kMvGuardZone);
    i32(kPropBlockZone, &out->blockZone, kMvBlockZone);
    i32(kPropBlockHand, &out->blockHand, kMvBlockHand);
    i32(kPropBlockMode, &out->blockMode, kMvBlockMode);
    i32(kPropAtkZone, &out->atkZone, kMvAtkZone);
    i32(kPropAttackType, &out->atkType, kMvAtkType);
    i32(kPropAtkHand, &out->atkHand, kMvAtkHand);
    // the one-byte bools and the float: the width is part of the map (WO-100 s10.5)
    if (prop_value(model, kPropPerfectBlock, &out->perfectBlock)) out->valid |= kMvPerfect;
    if (prop_value(model, kPropCombatMode, &out->combatMode)) out->valid |= kMvCombatMode;
    if (prop_value(model, kPropAtkStrength, &out->atkStrength)) out->valid |= kMvAtkStrength;
    void* opp = nullptr;
    if (rd(model, kModelOpponent, &opp) && opp) {
        if (void* oca = as_combat_actor(opp)) {
            if (pca && oca == pca) out->opponentIsPlayer = 1;
            void* owner = nullptr;
            uint32_t oeid = 0;
            if (rd(oca, kCaOwnerEntity, &owner) && owner && rd(owner, kActorEntityId, &oeid)) out->opponentEid = oeid;
        }
    }
    return true;
}

bool is_avatar_eid(uint32_t eid) {
    auto it = g_bodies.find(eid);
    return it != g_bodies.end() && it->second.avatar;
}

void* player_combat_actor() { return g_playerCa.load(); }

// ---- WO-165: the attacker's four attack fields (the replay's precondition) ------------------------------------------------
namespace {
template <class T> bool wr(void* base, size_t off, const T& v) {
    KCDMP_FAULT_READ(site, "motion::wr");
    if (!plausible_addr(reinterpret_cast<uintptr_t>(base) + off)) return false;
    return fault::guarded(site, [&] { *reinterpret_cast<T*>(static_cast<char*>(base) + off) = v; });
}
}   // namespace

bool combat_parts(uint32_t eid, bool create, void** ca, void** model) {
    *ca = nullptr; *model = nullptr;
    void* actor = actor_by_eid(eid);
    if (!actor) return false;
    void* c = combat_actor_of(actor, false);
    if (!c && create) c = combat_actor_of(actor, true);
    if (!c) return false;
    void* m = nullptr;
    if (!rd(c, kCaModel, &m) || !m) return false;
    *ca = c; *model = m;
    return true;
}

bool player_combat_parts(void** ca, void** model) {
    *ca = nullptr; *model = nullptr;
    void* c = g_playerCa.load();
    if (!c || !as_combat_actor(c)) return false;
    void* m = nullptr;
    if (!rd(c, kCaModel, &m) || !m) return false;
    *ca = c; *model = m;
    return true;
}

bool read_attack_fields(void* model, AttackFields* out) {
    *out = AttackFields{};
    return model && prop_value(model, kPropAttackType, &out->type) && prop_value(model, kPropAtkZone, &out->zone) &&
           prop_value(model, kPropAtkHand, &out->hand) && prop_value(model, kPropAtkStrength, &out->strength);
}

bool write_attack_fields(void* model, const AttackFields& f) {
    // every block names itself first (the reader's own check): a layout that moved writes nothing at all
    if (!model || !prop_named(model, kPropAttackType) || !prop_named(model, kPropAtkZone) || !prop_named(model, kPropAtkHand) ||
        !prop_named(model, kPropAtkStrength))
        return false;
    return wr(model, kPropAttackType.off + 8, f.type) && wr(model, kPropAtkZone.off + 8, f.zone) && wr(model, kPropAtkHand.off + 8, f.hand) &&
           wr(model, kPropAtkStrength.off + 8, f.strength);
}

} // namespace kcdmp::motion
