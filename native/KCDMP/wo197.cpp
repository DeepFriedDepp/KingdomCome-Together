// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-197: the animation-sync release's native half (wo197.h).
#include "wo197.h"
#include "wo208.h"
#include "wo197_rules.h"
#include "wo163.h"
#include "wo166.h"
#include "wo187.h"

#include "anchors.h"
#include "combat_swing.h"
#include "engine.h"
#include "fault_guard.h"
#include "log.h"
#include "motion.h"
#include "pe_exports.h"

#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <string>

namespace kcdmp::wo197 {

namespace {

using namespace kcdmp::wo197rules;

constexpr uint32_t kPlayerEntity = 0x7777;
constexpr unsigned kLogFirst = 30;

std::atomic<bool> g_recoil{true}, g_paired{true}, g_clinch{true}, g_guardEngine{true};
std::atomic<uint8_t> g_cats{0x7F};   // Part C: every category on

std::atomic<uint32_t> c_recoils{0}, c_recoilPb{0}, c_recoilBlock{0}, c_recoilUnknown{0}, c_recoilRow{0}, c_contacts{0}, c_blocked{0},
    c_blockedStab{0}, c_blockedStabNoFa{0}, c_blockedSlash{0},
    c_clinchEnter{0}, c_clinchMove{0}, c_clinchLeave{0}, c_clinchRows{0}, c_pairs{0}, c_pairRefused{0}, c_pairVictimOk{0}, c_pairAttackerOk{0},
    c_syncGuards{0}, c_syncGuardOk{0}, c_ensure{0}, c_ensureOk{0}, c_ensureRefused{0};

template <class T> bool rd(const void* base, ptrdiff_t off, T* out) {
    if (!base) return false;
    KCDMP_FAULT_READ(site, "wo197::read");
    if (!fault::enabled(site)) return false;
    const uintptr_t a = reinterpret_cast<uintptr_t>(base) + static_cast<uintptr_t>(off);
    if (!fault::plausible_address(a)) return false;
    return fault::guarded(site, [&] { std::memcpy(out, reinterpret_cast<const void*>(a), sizeof(T)); });
}

// An object's class by its RTTI (the complete object locator's type descriptor), "" when unreadable.
std::string rtti(const void* obj) {
    void* vt = nullptr; void* col = nullptr;
    if (!obj || !rd(obj, 0, &vt) || !vt || !rd(vt, -8, &col) || !col) return "";
    uint32_t sig = 0; int32_t tdRva = 0, selfRva = 0;
    if (!rd(col, 0, &sig) || sig != 1 || !rd(col, 12, &tdRva) || !rd(col, 20, &selfRva)) return "";
    const char* base = static_cast<const char*>(col) - selfRva;
    const char* td = base + tdRva + 16;
    char nm[112]{};
    for (int i = 0; i < 111; ++i) { char ch = 0; if (!rd(td, i, &ch) || !ch) break; nm[i] = ch; }
    return nm;
}

const char* name_of(uint32_t eid) {
    void* e = eid ? engine::entity_by_id(eid) : nullptr;
    const char* n = e ? engine::entity_name(e) : nullptr;
    return n ? n : "?";
}
uint32_t player_eid() {
    void* e = engine::entity_by_id(kPlayerEntity);
    return e ? engine::entity_id(e) : 0;
}

void guid_text(const uint8_t g[16], char* out, size_t n) {
    uint32_t d1; uint16_t d2, d3;
    std::memcpy(&d1, g, 4); std::memcpy(&d2, g + 4, 2); std::memcpy(&d3, g + 6, 2);
    std::snprintf(out, n, "%08x-%04x-%04x-%02x%02x-%02x%02x%02x%02x%02x%02x", d1, d2, d3, g[8], g[9], g[10], g[11], g[12], g[13], g[14], g[15]);
}

// A row object's GUID: the combat action data rows keep their mn_fragment_guid at +0x7C (the hit and the sync-attack rows, WO-151 / WO-163
// live); accepted only when the row object's RTTI names the expected data type -- the agent checks the GUID against the game's tables again.
constexpr size_t kRowGuidOff = 0x7C;
int g_rowDumps = 0;
bool row_guid(const void* row, const char* rttiPart, uint8_t out[16]) {
    if (!row) return false;
    const std::string r = rtti(row);
    if (g_rowDumps < 3) {
        ++g_rowDumps;
        uint8_t b[0xA0]{};
        for (size_t j = 0; j < sizeof b; j += 8) { uint64_t v = 0; if (rd(row, static_cast<ptrdiff_t>(j), &v)) std::memcpy(b + j, &v, 8); }
        char hex[0x150]{}; size_t k = 0;
        for (size_t i = 0; i < sizeof b && k + 3 < sizeof hex; ++i) k += std::snprintf(hex + k, sizeof hex - k, "%02x", b[i]);
        logf("WO197-ROWDUMP rtti=%s want=%s +000: %s", r.empty() ? "?" : r.c_str(), rttiPart, hex);
    }
    if (r.find(rttiPart) == std::string::npos) return false;
    uint64_t g0 = 0, g1 = 0;
    if (!rd(row, kRowGuidOff, &g0) || !rd(row, kRowGuidOff + 8, &g1) || (g0 == 0 && g1 == 0)) return false;
    std::memcpy(out, &g0, 8); std::memcpy(out + 8, &g1, 8);
    return true;
}

// ---- B5: EnsureGuardAction ----------------------------------------------------------------------------------------------------------
// The one direct callee of StartSyncGuard (found by its label) whose body imports Framework's C_ActionDirector::TerminateInternal (WO-196
// R4's port note): it terminates scope 0 and installs a new Guard, returning it (null = refused).
constexpr char kSsgLabel[] = "wh::combatmodule::C_CombatActorClinch::StartSyncGuard";
constexpr char kTerminateInternal[] = "?TerminateInternal@C_ActionDirector@framework@wh@@IEAAXH@Z";
using EnsureFn = void*(__fastcall*)(void* ca);
using SsgFn = bool(__fastcall*)(int32_t type, void* master, void* slave, void* r9, void* fifth);
EnsureFn g_ensure = nullptr;
SsgFn g_ssg = nullptr;
bool g_resolved = false;
const char* g_why = "not resolved";

// The live IAT slot of `symbol` imported from `from` in `importer` (read-only), null when absent.
const void* iat_slot(HMODULE importer, const char* from, const char* symbol) {
    if (!importer) return nullptr;
    auto* base = reinterpret_cast<const BYTE*>(importer);
    auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return nullptr;
    auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return nullptr;
    const auto& dir = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (!dir.VirtualAddress) return nullptr;
    for (auto* d = reinterpret_cast<const IMAGE_IMPORT_DESCRIPTOR*>(base + dir.VirtualAddress); d->Name; ++d) {
        if (_stricmp(reinterpret_cast<const char*>(base + d->Name), from) != 0) continue;
        auto* o = reinterpret_cast<const IMAGE_THUNK_DATA64*>(base + (d->OriginalFirstThunk ? d->OriginalFirstThunk : d->FirstThunk));
        auto* l = reinterpret_cast<const IMAGE_THUNK_DATA64*>(base + d->FirstThunk);
        for (; o->u1.AddressOfData; ++o, ++l) {
            if (o->u1.Ordinal & IMAGE_ORDINAL_FLAG64) continue;
            auto* bn = reinterpret_cast<const IMAGE_IMPORT_BY_NAME*>(base + o->u1.AddressOfData);
            if (std::strcmp(bn->Name, symbol) == 0) return &l->u1.Function;
        }
    }
    return nullptr;
}

bool resolve() {
    if (g_resolved) return g_ensure != nullptr;
    g_resolved = true;
    HMODULE cm = GetModuleHandleA("CombatModule.dll");
    int n = 0;
    const uint8_t* ssg = cm ? anchor::function_by_string(cm, kSsgLabel, &n) : nullptr;
    const void* slot = cm ? iat_slot(cm, "Framework.dll", kTerminateInternal) : nullptr;
    if (!ssg || !slot) {
        g_why = !ssg ? "StartSyncGuard's label is not referenced by exactly one function" : "CombatModule imports no C_ActionDirector::TerminateInternal";
        logf("WO197-GUARD not armed: %s", g_why);
        return false;
    }
    g_ssg = reinterpret_cast<SsgFn>(const_cast<uint8_t*>(ssg));
    const uint8_t* tg[64]{};
    const int nt = anchor::function_call_targets(cm, ssg, tg, 64);
    const uint8_t* found = nullptr; int hits = 0;
    for (int i = 0; i < nt; ++i)
        if (anchor::function_refs(cm, tg[i], slot)) { if (tg[i] != found) ++hits; found = tg[i]; }
    if (hits != 1) {
        g_why = "no single callee of StartSyncGuard imports TerminateInternal";
        logf("WO197-GUARD not armed: %s (%d of %d callees)", g_why, hits, nt);
        return false;
    }
    g_ensure = reinterpret_cast<EnsureFn>(const_cast<uint8_t*>(found));
    g_why = "armed";
    char a[64], b[64];
    anchor::describe(found, a, sizeof a);
    anchor::describe(ssg, b, sizeof b);
    logf("WO197-GUARD armed: EnsureGuardAction=%s (StartSyncGuard=%s's callee that imports TerminateInternal) -- a copy's leftover ends the engine's way",
         a, b);
    return true;
}

// ---- B1 / B3: the local player's polls ------------------------------------------------------------------------------------------------
const void* g_lastFa = nullptr;
const void* g_lastSt = nullptr;
Clinch g_clinchWas = Clinch::None;
char g_scope4Before[48] = "none";
// B1: the player's State on the last read, and his opponent as last read while he was striking (State 8) -- the engine reads the
// opponent's block at the contact itself; by the 0x10 read a blocker often reads his own withdraw (0x20) already
int32_t g_meWas = 0;
bool g_preOk = false, g_prePb = false;
int32_t g_preState = 0;
// one blocked blow, watched for kProbeMs after its contact: did a FailedAttack run, what did scope 0 do
struct Blow {
    bool on = false, fa = false, frames = false;
    uint64_t t0 = 0;
    int lines = 0;
    uint8_t kind = 0;
    int32_t zone = -1, type = -1, weapon = -1;
    uint32_t opp = 0;
    char seq[120] = "";
    const void* lastS0 = nullptr;
};
Blow g_blow;
unsigned g_probeWindows = 0;

void blow_end() {
    Blow& b = g_blow;
    b.on = false;
    const bool rows = recoil_rows_for_zone(b.zone);
    if (rows) { c_blockedStab.fetch_add(1); if (!b.fa) c_blockedStabNoFa.fetch_add(1); }
    else c_blockedSlash.fetch_add(1);
    const uint32_t k = c_blocked.load();
    if (k <= kLogFirst || k % 50 == 0 || (rows && !b.fa))
        logf("WO197-B1BLOW kind=%s zone=%d type=%d weapon=%d failed_attack=%s scope0=%s opponent=%s -- %s (#%u)",
             (b.kind & kRecoilPerfect) ? "pb" : "block", b.zone, b.type, b.weapon, b.fa ? "ran" : "none", b.seq[0] ? b.seq : "?",
             b.opp ? name_of(b.opp) : "?",
             rows ? (b.fa ? "a stab: the engine's recoil ran, sent as AvatarRecoil" : "a stab with no FailedAttack: no row for this weapon, or the director refused it")
                  : "not a stab: the game has no failed-attack row (rows are stabs only, attack zone 5), the Attack runs on to its withdraw",
             k);
}

void poll_player() {
    void* ca = motion::player_combat_actor();
    if (!ca) return;
    const wo187::DirAction s4 = wo187::director_scope(ca, 4);
    if (!s4.ok) return;
    const wo187::DirAction s0 = wo187::director_scope(ca, 0);
    // the player and his opponent, read only while a main action runs (a fight)
    motion::ModelRead me{}, opp{};
    bool meOk = false, oppOk = false, oppPb = false;
    if (g_recoil.load() && s0.act) {
        const uint32_t pe = player_eid();
        meOk = pe && motion::read_model(pe, &me) && (me.valid & motion::kMvState);
        oppOk = meOk && me.opponentEid && motion::read_model(me.opponentEid, &opp) && (opp.valid & motion::kMvState);
        oppPb = oppOk && (opp.valid & motion::kMvPerfect) && opp.perfectBlock;
    }
    // B1: the contact of the player's own blow (8 -> 0x10, a hit and a block alike); a blocked one is watched for kProbeMs
    if (meOk && contact_edge(g_meWas, me.state)) {
        c_contacts.fetch_add(1);
        const uint8_t kind = recoil_flags_at_contact(oppOk, oppPb, opp.state, g_preOk, g_prePb, g_preState);
        if (g_blow.on) blow_end();
        if (kind & kRecoilKnown) {
            c_blocked.fetch_add(1);
            Blow& b = g_blow;
            b = Blow{};
            b.on = true;
            b.t0 = GetTickCount64();
            b.kind = kind;
            b.zone = (me.valid & motion::kMvAtkZone) ? me.atkZone : -1;
            b.type = (me.valid & motion::kMvAtkType) ? me.atkType : -1;
            b.weapon = wo166::weapon_class_id(ca);
            b.opp = me.opponentEid;
            b.frames = g_probeWindows < static_cast<unsigned>(kProbeWindows);
            if (b.frames) ++g_probeWindows;
        }
    }
    if (meOk) {
        if (me.state == kStateStriking) { g_preOk = oppOk; g_prePb = oppPb; g_preState = opp.state; }
        else if (me.state != kStateFailedAttack) g_preOk = false;
        g_meWas = me.state;
    }
    // B1: a new FailedAttack in scope 4
    if (g_recoil.load() && new_recoil(g_lastFa, s4.act, s4.name)) {
        if (!meOk) {
            const uint32_t pe = player_eid();
            meOk = pe && motion::read_model(pe, &me);
            oppOk = meOk && me.opponentEid && motion::read_model(me.opponentEid, &opp) && (opp.valid & motion::kMvState);
            oppPb = oppOk && (opp.valid & motion::kMvPerfect) && opp.perfectBlock;
        }
        uint8_t flags = recoil_flags_at_contact(oppOk, oppPb, opp.state, g_preOk, g_prePb, g_preState);
        if (g_blow.on) g_blow.fa = true;
        uint8_t g[16]{};
        void* row = nullptr;
        if (rd(s4.act, 0x58, &row) && row_guid(row, "FailedAttack", g)) { flags |= kRecoilRow; c_recoilRow.fetch_add(1); }
        motion::push_local_action(kKindAvatarRecoil, flags, (flags & kRecoilRow) ? g : nullptr, nullptr);
        const uint32_t k = c_recoils.fetch_add(1) + 1;
        ((flags & kRecoilPerfect) ? c_recoilPb : (flags & kRecoilKnown) ? c_recoilBlock : c_recoilUnknown).fetch_add(1);
        if (k <= kLogFirst || k % 50 == 0) {
            char gt[40] = "-";
            if (flags & kRecoilRow) guid_text(g, gt, sizeof gt);
            logf("WO197-RECOIL kind=%s row=%s opponent=%s opp_state=0x%X opp_pb=%d zone=%d weapon=%d scope4_before=%s (#%u)",
                 (flags & kRecoilPerfect) ? "pb" : (flags & kRecoilKnown) ? "block" : "unknown", gt, oppOk ? name_of(me.opponentEid) : "?",
                 static_cast<unsigned>(opp.state), opp.perfectBlock, (me.valid & motion::kMvAtkZone) ? me.atkZone : -1, wo166::weapon_class_id(ca),
                 g_scope4Before, k);
        }
    }
    if (s4.act != g_lastFa) {
        g_lastFa = s4.act;
        std::snprintf(g_scope4Before, sizeof g_scope4Before, "%s", s4.act ? s4.name : "none");
    }
    // B1: the blocked blow's window -- a FailedAttack in scope 4 at any read, scope 0's changes; the first windows log every frame
    if (g_blow.on) {
        Blow& b = g_blow;
        const uint64_t dt = GetTickCount64() - b.t0;
        if (is_failed_attack(s4.act ? s4.name : "")) b.fa = true;
        if (s0.act != b.lastS0) {
            b.lastS0 = s0.act;
            const size_t n = std::strlen(b.seq);
            if (n + 24 < sizeof b.seq) std::snprintf(b.seq + n, sizeof b.seq - n, "%s%s", n ? ">" : "", s0.act ? s0.name : "none");
        }
        if (b.frames && b.lines < kProbeLines) {
            ++b.lines;
            const wo187::DirAction s1p = wo187::director_scope(ca, 1);
            logf("WO197-B1PROBE dt=%llums state=0x%X s0=%s s1=%s s4=%s opp_state=0x%X opp_pb=%d", static_cast<unsigned long long>(dt),
                 meOk ? static_cast<unsigned>(me.state) : 0xFFFFu, s0.act ? s0.name : "none", s1p.act ? s1p.name : "none", s4.act ? s4.name : "none",
                 oppOk ? static_cast<unsigned>(opp.state) : 0xFFFFu, oppPb ? 1 : 0);
        }
        if (dt >= static_cast<uint64_t>(kProbeMs)) blow_end();
    }
    // B3: the clinch
    if (!g_clinch.load()) return;
    const wo187::DirAction s1 = wo187::director_scope(ca, 1);
    const Clinch now = clinch_of(s0.act ? s0.name : "", s1.act ? s1.name : "");
    const bool newMove = now == Clinch::Move && s0.act != g_lastSt;
    const uint8_t ev = clinch_event(g_clinchWas, now, newMove);
    if (now == Clinch::Move) g_lastSt = s0.act;
    else if (now == Clinch::None) g_lastSt = nullptr;
    if (ev) {
        // the role: the clinch object (combat actor +0x4E0, C_CombatActorClinch) +0x18 = master; for a move, the SyncTransition's +0x68
        uint8_t master = 0;
        void* clinch = nullptr;
        if (rd(ca, 0x4E0, &clinch) && clinch && rtti(clinch).find("C_CombatActorClinch") != std::string::npos) rd(clinch, 0x18, &master);
        uint8_t flags = ev;
        uint8_t gm[16]{}, gs[16]{};
        bool rows = false;
        if (ev == kClinchMove) {
            uint8_t isMaster = 0;
            if (rd(s0.act, 0x68, &isMaster)) master = isMaster;
            void* mr = nullptr; void* sr = nullptr;
            rows = rd(s0.act, 0x58, &mr) && row_guid(mr, "SyncTransition", gm);
            if (rows && master) rows = rd(s0.act, 0x60, &sr) && row_guid(sr, "SyncTransition", gs);
            if (rows) { flags |= kClinchRows; c_clinchRows.fetch_add(1); }
        }
        if (master) flags |= kClinchMaster;
        motion::push_local_action(kKindAvatarClinch, flags, rows ? gm : nullptr, (rows && master) ? gs : nullptr);
        (ev == kClinchEnter ? c_clinchEnter : ev == kClinchMove ? c_clinchMove : c_clinchLeave).fetch_add(1);
        const uint32_t k = c_clinchEnter.load() + c_clinchMove.load() + c_clinchLeave.load();
        if (k <= kLogFirst || k % 50 == 0) {
            char a[40] = "-", b[40] = "-";
            if (rows) { guid_text(gm, a, sizeof a); if (master) guid_text(gs, b, sizeof b); }
            logf("WO197-CLINCH local %s role=%s row=%s slave_row=%s scope0=%s scope1=%s", ev == kClinchEnter ? "enter" : ev == kClinchMove ? "move" : "leave",
                 master ? "master" : "slave", a, b, s0.act ? s0.name : "none", s1.act ? s1.name : "none");
        }
    }
    g_clinchWas = now;
}

// ---- B2 / B3: a pair on the watcher -------------------------------------------------------------------------------------------------
bool body_ok(uint32_t eid) { return eid && eid != player_eid() && engine::entity_by_id(eid); }

uint8_t pair(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    if (len < 12 || cap < 7) return wo163::kRBadRequest;
    uint32_t a = 0, v = 0;
    std::memcpy(&a, body + 1, 4); std::memcpy(&v, body + 5, 4);
    const uint8_t flags = body[9];
    const uint8_t lenA = body[10];
    if (len < static_cast<size_t>(11 + lenA + 1)) return wo163::kRBadRequest;
    const uint8_t lenV = body[11 + lenA];
    if (!pair_request_ok(len, lenA, lenV)) return wo163::kRBadRequest;
    const std::string specA(reinterpret_cast<const char*>(body + 11), lenA);
    const std::string specV(reinterpret_cast<const char*>(body + 12 + lenA), lenV);
    if (wo208::probe_owned(a) || wo208::probe_owned(v)) return wo163::kRNoActor;   // WO-208 A.1: the probe's copy has one owner
    // WO-202 IV.3: a = 0xFFFFFFFF, a query: this machine's own player's combat opponent -- [eid:4][255][255][0][nameLen:1][name] -- so the
    // player's clinch can name the body it clinched (the joiner's copy of it is found by that name; the field: 4 of the host's clinches
    // reached the joiner and none played, "his figure has no opponent body here")
    if (a == 0xFFFFFFFFu && pair_is_query(lenA, lenV)) {
        motion::ModelRead pm{};
        const uint32_t pe = player_eid();
        const uint32_t opp = (pe && motion::read_model(pe, &pm)) ? pm.opponentEid : 0;
        if (!body_ok(opp)) return wo163::kRNoActor;
        const char* nm = name_of(opp);
        const size_t nl = std::strlen(nm) < 63 ? std::strlen(nm) : 63;
        if (cap < 8 + nl) return wo163::kRBadRequest;
        std::memcpy(out, &opp, 4); out[4] = out[5] = 255; out[6] = 0; out[7] = static_cast<uint8_t>(nl);
        std::memcpy(out + 8, nm, nl);
        *outLen = 8 + nl;
        return wo163::kROk;
    }
    if (!pair_is_query(lenA, lenV)) c_pairs.fetch_add(1);
    if (!(g_paired.load() || g_clinch.load()) || !body_ok(a)) { c_pairRefused.fetch_add(1); return wo163::kRNoActor; }
    if (!v) { motion::ModelRead m{}; if (motion::read_model(a, &m)) v = m.opponentEid; }
    if (!body_ok(v)) { c_pairRefused.fetch_add(1); return wo163::kRNoActor; }   // never the local player: his own body is his input's
    if (pair_is_query(lenA, lenV)) { std::memcpy(out, &v, 4); out[4] = out[5] = 255; out[6] = 0; *outLen = 7; return wo163::kROk; }
    void* aca = nullptr; void* am = nullptr; void* vca = nullptr; void* vm = nullptr;
    uint8_t targets = 0;
    if (motion::combat_parts(a, false, &aca, &am) && wo187::set_logical_target(aca, engine::entity_by_id(v))) targets |= 1;
    if (motion::combat_parts(v, false, &vca, &vm) && wo187::set_logical_target(vca, engine::entity_by_id(a))) targets |= 2;
    uint8_t rv = 255, ra = 255;
    auto play = [](uint32_t eid, const std::string& spec) {
        const rttr::SwingResult r = rttr::ghost_swing(eid, spec.c_str());
        if (r == rttr::SwingResult::Ok) wo187::note_move(eid);
        return static_cast<uint8_t>(r);
    };
    if (flags & kPairVictimFirst) { rv = play(v, specV); if (lenA) ra = play(a, specA); }
    else { if (lenA) ra = play(a, specA); rv = play(v, specV); }
    if (rv == static_cast<uint8_t>(rttr::SwingResult::Ok)) c_pairVictimOk.fetch_add(1);
    if (ra == static_cast<uint8_t>(rttr::SwingResult::Ok)) c_pairAttackerOk.fetch_add(1);
    const uint32_t k = c_pairs.load();
    if (k <= kLogFirst || k % 50 == 0)
        logf("WO197-PAIR attacker=%s victim=%s targets=%u victim_row=\"%s\" -> %u attacker_row=\"%s\" -> %u", name_of(a), name_of(v), targets, specV.c_str(), rv,
             specA.c_str(), ra);
    std::memcpy(out, &v, 4); out[4] = rv; out[5] = ra; out[6] = targets;
    *outLen = 7;
    return wo163::kROk;
}

uint8_t sync_guard(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    if (len != 13 || cap < 3) return wo163::kRBadRequest;
    int32_t type = 0; uint32_t m = 0, s = 0;
    std::memcpy(&type, body + 1, 4); std::memcpy(&m, body + 5, 4); std::memcpy(&s, body + 9, 4);
    out[0] = 2; out[1] = out[2] = 255; *outLen = 3;
    if (!resolve() || !g_ssg) return wo163::kROk;
    if (!g_clinch.load() || !body_ok(m) || !body_ok(s)) return wo163::kRNoActor;
    if (wo208::probe_owned(m) || wo208::probe_owned(s)) return wo163::kRNoActor;   // WO-208 A.1
    void* mca = nullptr; void* mm = nullptr; void* sca = nullptr; void* sm = nullptr;
    if (!motion::combat_parts(m, false, &mca, &mm) || !motion::combat_parts(s, false, &sca, &sm)) return wo163::kRNoActor;
    wo187::set_logical_target(mca, engine::entity_by_id(s));
    wo187::set_logical_target(sca, engine::entity_by_id(m));
    bool r = false;
    c_syncGuards.fetch_add(1);
    KCDMP_FAULT_CALL(site, "wo197::start_sync_guard");
    if (!fault::guarded(site, [&] { r = g_ssg(type, mca, sca, nullptr, nullptr); })) return wo163::kRNoActor;
    if (r) c_syncGuardOk.fetch_add(1);
    uint8_t mf = 255, sf = 255;
    void* mc = nullptr; void* sc = nullptr;
    if (rd(mca, 0x4E0, &mc) && mc) rd(mc, 0x18, &mf);
    if (rd(sca, 0x4E0, &sc) && sc) rd(sc, 0x18, &sf);
    out[0] = r ? 1 : 0; out[1] = mf; out[2] = sf;
    logf("WO197-SYNCGUARD type=%d master=%s slave=%s -> %s (master flag %u, slave flag %u)", type, name_of(m), name_of(s), r ? "started" : "refused", mf, sf);
    return wo163::kROk;
}

} // namespace

int start_sync_guard(int32_t type, uint32_t master, uint32_t slave, char* why, size_t whyN) {
    if (why && whyN) why[0] = 0;
    if (!resolve() || !g_ssg) { if (why) std::snprintf(why, whyN, "StartSyncGuard not armed"); return -1; }
    void* mca = nullptr; void* mm = nullptr; void* sca = nullptr; void* sm = nullptr;
    if (!motion::combat_parts(master, false, &mca, &mm) || !motion::combat_parts(slave, false, &sca, &sm)) { if (why) std::snprintf(why, whyN, "no combat actor"); return -1; }
    // the engine's own refusals, read beforehand so a refused start says which (C_CombatActorClinch::StartSyncGuard: the master's guard
    // stance unset, a scope-0 action that is not a plain Guard on either; the rows' absence is the engine's own log line)
    const std::string md = wo187::director_text(mca, 0), sd = wo187::director_text(sca, 0);
    bool r = false;
    c_syncGuards.fetch_add(1);
    KCDMP_FAULT_CALL(site, "wo197::start_sync_guard_owner");
    if (!fault::guarded(site, [&] { r = g_ssg(type, mca, sca, nullptr, nullptr); })) { if (why) std::snprintf(why, whyN, "faulted"); return -1; }
    if (r) c_syncGuardOk.fetch_add(1);
    else if (why) std::snprintf(why, whyN, "refused by the engine (master scope 0 %s, slave scope 0 %s; a missing guard_sync_movement row or an unset guard stance is the engine's own kcd.log line)",
                                md.empty() ? "?" : md.c_str(), sd.empty() ? "?" : sd.c_str());
    return r ? 1 : 0;
}

bool cat_on(uint8_t bit) { return (g_cats.load(std::memory_order_relaxed) & bit) != 0; }
bool activity_now(bool minigame, bool stance, bool unstance) {
    const uint8_t want = minigame ? kCatWork : stance ? kCatSeats : unstance ? kCatRoutines : kCatSeats;   // "none" (standing up) rides the seats switch
    return cat_on(want);
}
bool recoil_on() { return g_recoil.load(); }
bool paired_on() { return g_paired.load(); }
bool clinch_on() { return g_clinch.load(); }
bool guard_engine_on() { return g_guardEngine.load(); }

int ensure_guard(void* ca) {
    if (!ca || !resolve() || !g_ensure) return -1;
    void* r = nullptr;
    c_ensure.fetch_add(1);
    KCDMP_FAULT_CALL(site, "wo197::ensure_guard");
    if (!fault::guarded(site, [&] { r = g_ensure(ca); })) { c_ensureRefused.fetch_add(1); return 0; }
    if (!r) { c_ensureRefused.fetch_add(1); return 0; }
    c_ensureOk.fetch_add(1);
    return 1;
}

void tick() {
    static bool s_resolved = false;
    if (!s_resolved) { s_resolved = true; resolve(); }   // the armed line at startup (EnsureGuardAction, StartSyncGuard)
    if (!g_recoil.load() && !g_clinch.load()) return;
    poll_player();
}

uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    switch (op) {
        case kOpStatus: {
            const int n = status_text(reinterpret_cast<char*>(out), static_cast<int>(cap));
            *outLen = n <= 0 || cap == 0 ? 0 : (static_cast<size_t>(n) < cap ? static_cast<size_t>(n) : cap - 1);
            return wo163::kROk;
        }
        case kOpConfig: {
            constexpr int kN = 4;
            if (len < 1 || len > 1 + kN + 1 || cap < kN + 1) return wo163::kRBadRequest;
            if (len == 1 + kN + 1 && body[1 + kN] != 255) {   // Part C's category mask
                const uint8_t m = body[1 + kN] & 0x7F;
                if (g_cats.exchange(m) != m) logf("WO197-CONFIG categories 0x%02X (doors, seats, items, work, riding, routines, emotes)", m);
            }
            std::atomic<bool>* sw[kN] = {&g_recoil, &g_paired, &g_clinch, &g_guardEngine};
            const char* nm[kN] = {"mp_anim_recoil", "mp_anim_paired", "mp_anim_clinch", "mp_copy_guard_engine"};
            for (size_t i = 0; i + 1 < len && i < kN; ++i) {
                const uint8_t b = body[1 + i];
                if (b == 255) continue;
                if (sw[i]->exchange(b != 0) != (b != 0)) logf("WO197-CONFIG %s %s", nm[i], b ? "on" : "off");
            }
            for (int i = 0; i < kN; ++i) out[i] = sw[i]->load() ? 1 : 0;
            out[kN] = g_cats.load();
            *outLen = kN + 1;
            return wo163::kROk;
        }
        case kOpPair: return pair(body, len, out, cap, outLen);
        case kOpSyncGuard: return sync_guard(body, len, out, cap, outLen);
        case kOpEnsureGuard: {
            if (len != 5 || cap < 2) return wo163::kRBadRequest;
            uint32_t eid = 0;
            std::memcpy(&eid, body + 1, 4);
            void* ca = nullptr; void* m = nullptr;
            if (!body_ok(eid) || wo208::probe_owned(eid) || !motion::combat_parts(eid, false, &ca, &m)) return wo163::kRNoActor;
            const std::string before = wo187::director_text(ca, 0);
            const int r = ensure_guard(ca);
            const std::string after = wo187::director_text(ca, 0);
            logf("WO197-GUARD npc=%s scope0 %s -> EnsureGuardAction -> %s (now %s)", name_of(eid), before.empty() ? "?" : before.c_str(),
                 r == 1 ? "a Guard runs" : r == 0 ? "refused" : "not armed", after.empty() ? "?" : after.c_str());
            out[0] = static_cast<uint8_t>(r < 0 ? 2 : r);
            const size_t n = before.size() < cap - 1 ? before.size() : cap - 1;
            std::memcpy(out + 1, before.data(), n);
            *outLen = 1 + n;
            return wo163::kROk;
        }
        default: return wo163::kRBadRequest;
    }
}

int status_text(char* out, int n) {
    return std::snprintf(out, n,
        "wo197 recoil=%s paired=%s clinch=%s guard_engine=%s guard=%s recoils=%u pb=%u block=%u unknown=%u recoil_rows=%u contacts=%u blocked=%u blocked_stab=%u blocked_stab_no_fa=%u blocked_slash=%u "
        "clinch_enter=%u clinch_move=%u clinch_leave=%u clinch_rows=%u pairs=%u pair_refused=%u pair_victim_ok=%u pair_attacker_ok=%u sync_guards=%u "
        "sync_guard_ok=%u ensure=%u ensure_ok=%u ensure_refused=%u",
        g_recoil.load() ? "on" : "off", g_paired.load() ? "on" : "off", g_clinch.load() ? "on" : "off", g_guardEngine.load() ? "on" : "off",
        g_ensure ? "armed" : (g_resolved ? "not-armed" : "unresolved"), c_recoils.load(), c_recoilPb.load(), c_recoilBlock.load(), c_recoilUnknown.load(),
        c_recoilRow.load(), c_contacts.load(), c_blocked.load(), c_blockedStab.load(), c_blockedStabNoFa.load(), c_blockedSlash.load(), c_clinchEnter.load(), c_clinchMove.load(), c_clinchLeave.load(), c_clinchRows.load(),
        c_pairs.load(), c_pairRefused.load(), c_pairVictimOk.load(), c_pairAttackerOk.load(), c_syncGuards.load(), c_syncGuardOk.load(), c_ensure.load(),
        c_ensureOk.load(), c_ensureRefused.load());
}

} // namespace kcdmp::wo197
