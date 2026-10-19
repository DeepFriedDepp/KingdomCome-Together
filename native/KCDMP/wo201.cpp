// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-201's native half (wo201.h). Every read is SEH-guarded and fails closed; every engine call runs on the main thread.
#include "wo201.h"
#include "wo201_rules.h"
#include "wo163.h"
#include "anchors.h"
#include "engine.h"
#include "fault_guard.h"
#include "log.h"
#include "npcstate.h"
#include "wo132.h"
#include "wo187.h"
#include "wo195_rules.h"
#include "motion.h"
#include "hits.h"
#include "buffs.h"
#include "respawn_actions.h"
#include "npc_drive.h"
#include "rttr_abi.h"
#include "inline_hook.h"
#include "hook_prologues.h"
#include "wo143.h"
#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>

namespace kcdmp::wo201 {

namespace {

using namespace wo201rules;

constexpr uint32_t kPlayerEntity = 0x7777;    // the local player's entity id (WO-125)
constexpr size_t kMgrNpcOf = 0x20;            // C_NPCManager vtable: the C_NPC of an entity id (WO-189's read, used by WO-194 too)
constexpr char kSaveFFText[] =
    "FastForward synchronization is going on while save lock is requested, which is not allowed! Cancelling FF for all NPCs until the "
    "save takes place. Fix this by requesting the save at different time.";

std::atomic<uint8_t> g_role{0}, g_bits{1};
std::atomic<bool> g_joined{false};
std::atomic<uint32_t> c_walks{0}, c_stopped{0}, c_episodes{0}, c_noNpc{0}, c_combatEnds{0}, c_saveSouls{0}, c_saveClears{0}, c_dude2Sets{0};
// V.3 Dude2
constexpr char kSaveSoulText[] = "Saving soul '%s', GUID: %s, WUID: %s, entity GUID: %s, IsPlayer: %d, IsLocalPlayer: %d";
std::atomic<int64_t> g_flagOff{-1};
std::atomic<int> g_bitPlayer{-1}, g_bitLocal{-1};
std::atomic<void*> g_dude2{nullptr};     // the soul (C_Soul) that holds the IsPlayer bit for the partner's figure
std::atomic<uint32_t> g_dude2Eid{0};
void* g_saveSoulFn = nullptr;
using SaveSoulFn = uint64_t (*)(void* ctx, void* chunk, void* a3, void* a4);   // every argument register and the return passed through
SaveSoulFn g_saveSoulOrig = nullptr;
bool g_saveGateArmed = false;
const char* g_dude2Why = "not resolved";
std::atomic<size_t> g_stopSlot{0};
std::atomic<int64_t> g_runField{-1};
void* const* g_vftNpc = nullptr;
void* const* g_vftNpcMgr = nullptr;
const char* g_why = "not installed";
uint64_t g_lastWalk = 0, g_episodeAt = 0;
int32_t g_episodeFirst = 0;
uint32_t g_episodeStops = 0;
bool g_inEpisode = false;

template <class T> bool rd(const void* base, ptrdiff_t off, T* out) {
    if (!base) return false;
    KCDMP_FAULT_READ(site, "wo201::read");
    if (!fault::enabled(site)) return false;
    const uintptr_t a = reinterpret_cast<uintptr_t>(base) + static_cast<uintptr_t>(off);
    if (!fault::plausible_address(a)) return false;
    return fault::guarded(site, [&] { std::memcpy(out, reinterpret_cast<const void*>(a), sizeof(T)); });
}

bool rd_bytes(const void* p, uint8_t* out, size_t n) {
    KCDMP_FAULT_READ(site, "wo201::read_bytes");
    if (!p || !fault::enabled(site) || !fault::plausible_address(reinterpret_cast<uintptr_t>(p))) return false;
    return fault::guarded(site, [&] { std::memcpy(out, p, n); });
}

bool is_a(const void* obj, void* const* vft) {
    void* vp = nullptr;
    return obj && vft && rd(obj, 0, &vp) && vp == static_cast<const void*>(vft);
}

void* vslot(const void* obj, size_t off) {
    void* vt = nullptr; void* fn = nullptr;
    return obj && rd(obj, 0, &vt) && vt && rd(vt, static_cast<ptrdiff_t>(off), &fn) ? fn : nullptr;
}

uint64_t now_ms() { return GetTickCount64(); }

// B1: the save path's cancel, out of ValidateFastForward's own code (see wo201_rules.h).
void resolve_ff() {
    HMODULE xg = GetModuleHandleA("XGenAIModule.dll");
    if (!xg) { g_why = "XGenAIModule.dll not loaded"; return; }
    g_vftNpc = anchor::find_vftable(xg, ".?AVC_NPC@xgenaimodule@wh@@", 0);
    g_vftNpcMgr = anchor::find_vftable(xg, ".?AVC_NPCManager@xgenaimodule@wh@@", 0);
    anchor::Range text{}, rdata{};
    if (!anchor::section(xg, ".text", &text) || !anchor::section(xg, ".rdata", &rdata)) { g_why = "XGenAI sections not found"; return; }
    int nfn = 0;
    const uint8_t* vff = anchor::function_by_string(xg, kSaveFFText, &nfn);
    if (!vff || nfn != 1) { g_why = "the save path's FastForward text is referenced by no (or more than one) function"; return; }
    uint8_t code[0x700]{};
    const size_t avail = static_cast<size_t>(text.end - vff);
    const size_t n = avail < sizeof code ? avail : sizeof code;
    if (!rd_bytes(vff, code, n)) { g_why = "ValidateFastForward unreadable"; return; }
    const int64_t field = ff_running_field(code, 0x40 < n ? 0x40 : n);
    size_t leas[16];
    const int k = lea_rax_stores(code, n, leas, 16);
    int64_t slot = -1;
    for (int i = 0; i < k && slot < 0; ++i) {
        const uint8_t* insn = vff + leas[i];
        int32_t d = 0; std::memcpy(&d, code + leas[i] + 3, 4);
        const uint8_t* vt = insn + 7 + d;
        if (!rdata.contains(vt)) continue;
        void* call = nullptr;
        if (!rd(vt, 0x10, &call) || !text.contains(call)) continue;   // std::function's _Do_call (slot 2)
        uint8_t body[0x40]{};
        if (!rd_bytes(call, body, sizeof body)) continue;
        slot = stop_slot_in_lambda(body, sizeof body);
    }
    if (field < 0 || slot < 0) { g_why = field < 0 ? "ValidateFastForward's running check not found" : "its per-NPC cancel not found"; return; }
    void* fn = nullptr;
    if (g_vftNpc) rd(g_vftNpc, static_cast<ptrdiff_t>(slot), &fn);
    if (!fn || !text.contains(fn)) { g_why = "C_NPC has no function at the resolved slot"; return; }
    g_runField = field;
    g_stopSlot = static_cast<size_t>(slot);
    g_why = "armed";
    char a[96]; anchor::describe(vff, a, sizeof a);
    logf("WO201-FF resolver: the save path's own cancel (C_NPCManager::ValidateFastForward %s, by its error text) = C_NPC slot +0x%llX, "
         "running field +0x%llX -- %s the build WO-188 read", a, static_cast<unsigned long long>(slot), static_cast<unsigned long long>(field),
         (slot == kKnownStopSlot && field == kKnownRunningField) ? "the same as" : "DIFFERENT from (the resolved values are used)");
}

// V.3: the flag word, out of the per-soul save step's own code (wo201_rules.h).
void resolve_flags() {
    HMODULE rpg = GetModuleHandleA("RPGModule.dll");
    if (!rpg) { g_dude2Why = "RPGModule.dll not loaded"; return; }
    int nfn = 0;
    const uint8_t* fn = anchor::function_by_string(rpg, kSaveSoulText, &nfn);
    if (!fn || nfn != 1) { g_dude2Why = "the per-soul save text is referenced by no (or more than one) function"; return; }
    uint8_t code[0x80]{};
    if (!rd_bytes(fn, code, sizeof code)) { g_dude2Why = "the per-soul save step unreadable"; return; }
    const FlagWord f = flag_word(code, sizeof code);
    if (f.off < 0) { g_dude2Why = "its flag-word read not found"; return; }
    g_flagOff = f.off; g_bitPlayer = f.playerBit; g_bitLocal = f.localBit;
    g_saveSoulFn = const_cast<uint8_t*>(fn);
    g_dude2Why = "resolved";
    char a[96]; anchor::describe(fn, a, sizeof a);
    logf("WO201-DUDE2 resolver: the soul's flag word +0x%llX (IsPlayer bit %d, IsLocalPlayer bit %d), read out of the per-soul save step %s by its trace "
         "text -- %s the build WO-199 read", static_cast<unsigned long long>(f.off), f.playerBit, f.localBit, a,
         f.off == kKnownFlagOff ? "the same as" : "DIFFERENT from (the resolved offset is used)");
}

bool flags_of(void* soul, uint32_t* out) { return soul && g_flagOff.load() > 0 && rd(soul, static_cast<ptrdiff_t>(g_flagOff.load()), out); }
bool write_flags(void* soul, uint32_t v) {
    if (!soul || g_flagOff.load() <= 0) return false;
    KCDMP_FAULT_CALL(site, "wo201::write_flags");
    const uintptr_t a = reinterpret_cast<uintptr_t>(soul) + static_cast<uintptr_t>(g_flagOff.load());
    if (!fault::plausible_address(a)) return false;
    return fault::guarded(site, [&] { std::memcpy(reinterpret_cast<void*>(a), &v, 4); });
}

// The save gate: the soul the step writes is **ctx (mov rdi,rcx; mov rax,[rdi]; mov rdx,[rax]); Dude2's IsPlayer bit is 0 while it is
// written and set again right after -- on every save path (manual, quest SaveGame nodes, autosaves, the schnapps save, join saves).
uint64_t save_soul_detour(void* ctx, void* chunk, void* a3, void* a4) {
    c_saveSouls.fetch_add(1, std::memory_order_relaxed);
    void* soul = nullptr;
    void* p1 = nullptr;
    const bool got = rd(ctx, 0, &p1) && p1 && rd(p1, 0, &soul);
    void* d2 = g_dude2.load();
    bool cleared = false;
    uint32_t f = 0;
    if (got && d2 && soul == d2 && flags_of(soul, &f) && ((f >> g_bitPlayer.load()) & 1u)) {
        cleared = write_flags(soul, f & ~(1u << g_bitPlayer.load()));
        if (cleared) c_saveClears.fetch_add(1, std::memory_order_relaxed);
    }
    const uint64_t ret = g_saveSoulOrig(ctx, chunk, a3, a4);
    if (cleared) {
        uint32_t now = 0;
        if (flags_of(soul, &now)) write_flags(soul, now | (1u << g_bitPlayer.load()));
    }
    return ret;
}

bool arm_save_gate(const char** why) {
    if (g_saveGateArmed) return true;
    if (!g_saveSoulFn) { *why = "the per-soul save step is not resolved"; return false; }
    void* orig = nullptr;
    if (!inlinehook::install_detour(g_saveSoulFn, hookpro::kSaveSoul, sizeof hookpro::kSaveSoul, reinterpret_cast<void*>(&save_soul_detour), &orig, why))
        return false;
    g_saveSoulOrig = reinterpret_cast<SaveSoulFn>(orig);
    g_saveGateArmed = true;
    logf("WO201-DUDE2 save gate ARMED: the per-soul save step writes Dude2 with IsPlayer 0 (set again right after it)");
    return true;
}

struct WalkCtx { void* mgr; void* get; int64_t field; uint32_t stopped; uint32_t noNpc; char first[8][48]; int nFirst; };

bool walk_one(void* e, void* x) {
    auto* c = static_cast<WalkCtx*>(x);
    const uint32_t eid = engine::entity_id(e);
    const char* nm = engine::entity_name(e);
    if (!stop_allowed(eid, nm, kPlayerEntity)) return false;
    void* npc = nullptr;
    KCDMP_FAULT_CALL(gsite, "wo201::npc_of");
    uint32_t id = eid;
    if (!fault::guarded(gsite, [&] { npc = reinterpret_cast<void* (*)(void*, const uint32_t*)>(c->get)(c->mgr, &id); }) || !is_a(npc, g_vftNpc)) {
        return false;   // not an NPC (an item, a light, the world)
    }
    void* stop = vslot(npc, g_stopSlot.load());
    KCDMP_FAULT_CALL(ssite, "wo201::StopFastForward");
    if (!stop || !fault::guarded(ssite, [&] { reinterpret_cast<void (*)(void*, uint64_t)>(stop)(npc, 0); })) { ++c->noNpc; return false; }
    ++c->stopped;
    int32_t now = -1;
    return rd(c->mgr, static_cast<ptrdiff_t>(c->field), &now) && now == 0;   // the manager reads 0 again: stop walking
}

} // namespace

// ===========================================================================
size_t ff_stop_slot() { return g_stopSlot.load(); }

void install() {
    resolve_ff();
    resolve_flags();
    if (g_flagOff.load() < 0) logf("WO201-DUDE2 resolver: NOT resolved (%s) -- the figure can never be a player", g_dude2Why);
    if (g_stopSlot.load() == 0) logf("WO201-FF resolver: NOT armed (%s) -- a joiner's scene may wait for the host's copies", g_why);
    wo201near::install();
    wo201speech::install();
    wo201vote::install();
    wo201ranged::install();
    // no silent guard: a part that did not resolve says so once (its status line repeats why)
    char t[600];
    if (wo201speech::status_text(t, sizeof t) > 0 && !std::strstr(t, "speech=resolved")) logf("WO201-SPEECH resolver: NOT resolved -- %s", t);
    if (wo201vote::status_text(t, sizeof t) > 0 && !std::strstr(t, "vote=resolved")) logf("WO201-VOTE resolver: NOT resolved -- %s", t);
    if (wo201ranged::status_text(t, sizeof t) > 0 && !std::strstr(t, "ranged=resolved")) logf("WO201-RANGED resolver: NOT resolved -- %s", t);
}

void tick() {
    wo201near::tick();
    wo201ranged::tick();
    const bool on = (g_bits.load() & 1) != 0 && g_stopSlot.load() != 0;
    const bool joinerIn = g_role.load() == kRoleJoiner && g_joined.load();
    if (!on || !joinerIn) { g_inEpisode = false; return; }
    void* mgr = wo141::x::npc_manager();
    if (!is_a(mgr, g_vftNpcMgr)) return;
    const int64_t field = g_runField.load();
    int32_t running = 0;
    if (!rd(mgr, static_cast<ptrdiff_t>(field), &running)) return;
    const uint64_t t = now_ms();
    if (running <= 0) {
        if (g_inEpisode) {
            g_inEpisode = false;
            logf("WO201-FF positioning freed after %.2f s: %u host copies' FastForward cancelled (the save path's own call), the manager's count "
                 "%d -> 0 -- the copies stay where the host's stream puts them; no save requested [episodes=%u]",
                 (t - g_episodeAt) / 1000.0, g_episodeStops, g_episodeFirst, c_episodes.load());
        }
        return;
    }
    if (!walk_due(on, joinerIn, running, t, g_lastWalk)) return;
    g_lastWalk = t;
    void* get = vslot(mgr, kMgrNpcOf);
    if (!get) return;
    if (!g_inEpisode) { g_inEpisode = true; g_episodeAt = t; g_episodeFirst = running; g_episodeStops = 0; c_episodes.fetch_add(1); }
    WalkCtx c{mgr, get, field, 0, 0, {}, 0};
    engine::for_each_entity(&walk_one, &c);
    c_walks.fetch_add(1);
    c_stopped.fetch_add(c.stopped);
    c_noNpc.fetch_add(c.noNpc);
    g_episodeStops += c.stopped;
    int32_t after = -1;
    rd(mgr, static_cast<ptrdiff_t>(field), &after);
    if (c.stopped) logf("WO201-FF a FastForward runs on the joiner (count %d): %u NPCs cancelled the engine's own way -> count %d", running, c.stopped, after);
}

void on_pipe_closed() {
    g_role = kRoleNone; g_joined = false;
    wo201ranged::on_pipe_closed();
    uint8_t off[6] = {82, 0, 0, 0, 0, 0}; uint8_t o[16]; size_t ol = 0;
    wo201near::handle(off, sizeof off, o, sizeof o, &ol);   // V.2 off
    uint8_t soff[6] = {83, 0, 0, 0, 0, 0}; uint8_t so[64]; size_t sol = 0;
    wo201speech::handle(83, soff, sizeof soff, so, sizeof so, &sol);   // speech capture off
    uint8_t voff[10] = {85, 0, 0, 0, 0, 0, 0, 0, 0, 0}; uint8_t vo[64]; size_t vol = 0;
    wo201vote::handle(voff, sizeof voff, vo, sizeof vo, &vol);   // vote capture off
    if (g_dude2.load()) {
        void* d2 = g_dude2.load(); uint32_t f = 0;
        if (flags_of(d2, &f)) write_flags(d2, f & ~(1u << g_bitPlayer.load()));   // V.3: the bit never outlives the session
        g_dude2 = nullptr; g_dude2Eid = 0;
    }
    logf("WO201 session off (the agent went away): the scene stop, the speech and vote capture, the ranged read and V.2/V.3 stand down");
}

int status_text(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n),
        "WO201 role=%u joined=%d bits=%u ff_resolver=%s stop_slot=0x%zX running_field=0x%llX walks=%u stopped=%u episodes=%u no_npc=%u combat_ends=%u "
        "dude2=%s flag_off=0x%llX save_gate=%d save_souls=%u save_clears=%u dude2_sets=%u",
        g_role.load(), g_joined.load() ? 1 : 0, g_bits.load(), g_why, g_stopSlot.load(), static_cast<long long>(g_runField.load()),
        c_walks.load(), c_stopped.load(), c_episodes.load(), c_noNpc.load(), c_combatEnds.load(),
        g_dude2Why, static_cast<long long>(g_flagOff.load()), g_saveGateArmed ? 1 : 0, c_saveSouls.load(), c_saveClears.load(), c_dude2Sets.load());
}

uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    switch (op) {
        case kOpStatus: {
            char t[1400];
            int n = status_text(t, sizeof t);
            if (n > 0 && static_cast<size_t>(n) + 2 < sizeof t) {
                t[n++] = ' ';
                const int m2 = wo201near::status_text(t + n, static_cast<int>(sizeof t) - n);
                if (m2 > 0) n += m2 < static_cast<int>(sizeof t) - n ? m2 : static_cast<int>(sizeof t) - n - 1;
            }
            if (n > 0 && static_cast<size_t>(n) + 2 < sizeof t) {
                t[n++] = ' ';
                const int m3 = wo201speech::status_text(t + n, static_cast<int>(sizeof t) - n);
                if (m3 > 0) n += m3 < static_cast<int>(sizeof t) - n ? m3 : static_cast<int>(sizeof t) - n - 1;
            }
            if (n > 0 && static_cast<size_t>(n) + 2 < sizeof t) {
                t[n++] = ' ';
                const int m4 = wo201vote::status_text(t + n, static_cast<int>(sizeof t) - n);
                if (m4 > 0) n += m4 < static_cast<int>(sizeof t) - n ? m4 : static_cast<int>(sizeof t) - n - 1;
            }
            if (n > 0 && static_cast<size_t>(n) + 2 < sizeof t) {
                t[n++] = ' ';
                const int m5 = wo201ranged::status_text(t + n, static_cast<int>(sizeof t) - n);
                if (m5 > 0) n += m5 < static_cast<int>(sizeof t) - n ? m5 : static_cast<int>(sizeof t) - n - 1;
            }
            const size_t m = n > 0 ? (static_cast<size_t>(n) < cap ? static_cast<size_t>(n) : cap) : 0;
            std::memcpy(out, t, m);
            *outLen = m;
            return wo163::kROk;
        }
        case kOpConfig: {
            if (len < 4 || cap < 3) return wo163::kRBadRequest;
            if (body[1] != 255) g_role = body[1];
            if (body[2] != 255) g_joined = body[2] != 0;
            if (body[3] != 255) g_bits = body[3];
            npcdrive::set_walk_from_host((g_bits.load() & 2) != 0);   // B10
            wo143::set_tick_hz((g_bits.load() & 4) != 0 ? 10 : 0);   // B9: the one-shot tick cap
            static int last = -1;
            const int key = (g_role.load() << 16) | (g_joined.load() << 8) | g_bits.load();
            if (key != last) {
                last = key;
                logf("WO201-CONFIG role=%u joined=%d bits=%u -> scene no-position %s, walk from host %s", g_role.load(), g_joined.load() ? 1 : 0, g_bits.load(),
                     (g_bits.load() & 1) ? (g_stopSlot.load() ? "on" : "on (NOT armed)") : "off", (g_bits.load() & 2) ? "on" : "off");
            }
            out[0] = g_role.load(); out[1] = g_joined.load() ? 1 : 0; out[2] = g_bits.load();
            *outLen = 3;
            return wo163::kROk;
        }
        case kOpCombatEnd: {
            // [op][eid:4] or [op][0:4][nl:1][name] (the agent knows no id for that copy: the body of that name)
            if (len < 5 || cap < 2) return wo163::kRBadRequest;
            uint32_t eid = 0;
            std::memcpy(&eid, body + 1, 4);
            if (!eid && len > 6) {
                const size_t nl = body[5];
                if (len != 6 + nl || nl == 0 || nl > 63) return wo163::kRBadRequest;
                char nm[64]; std::memcpy(nm, body + 6, nl); nm[nl] = 0;
                void* ne = wo141::x::entity_named(nm);
                eid = ne ? engine::entity_id(ne) : 0;
            }
            if (!eid || eid == kPlayerEntity || !engine::entity_by_id(eid)) return wo163::kRNoActor;
            const char* why = "the host's NPC left combat (WO-201)";
            uint8_t held = 0;
            if (wo132::release_copy(eid, why)) held |= wo195rules::kHeldEngage;
            held |= wo187::drop_wish(eid, why);
            held |= motion::drop_fight_holds(eid, why);
            bool stopped = false;
            if (actions::stop_fight_available()) {
                if (void* soul = hits::soul_of_eid(eid)) {
                    void* cs = buffs::as_c_soul(soul) ? buffs::as_c_soul(soul) : soul;
                    stopped = actions::stop_fight(cs);
                }
            }
            c_combatEnds.fetch_add(1);
            const char* nm = engine::entity_name(engine::entity_by_id(eid));
            logf("WO201-COMBAT end npc=%s eid=0x%X held=0x%02X stop_fight=%s -- its fight ends the engine's own way (the host's world decides who fights) [n=%u]",
                 nm ? nm : "?", eid, held, stopped ? "called" : "not available", c_combatEnds.load());
            out[0] = held; out[1] = stopped ? 1 : 0;
            *outLen = 2;
            return wo163::kROk;
        }
        case kOpDude2: {
            // [op][eid:4][on:1] -> [ok][flagsBefore:4][flagsAfter:4]
            if (len != 6 || cap < 9) return wo163::kRBadRequest;
            uint32_t eid = 0; std::memcpy(&eid, body + 1, 4);
            const bool on = body[5] != 0;
            if (g_flagOff.load() <= 0) return wo163::kRFailed;
            void* soul = nullptr;
            if (on || eid) {
                if (!eid || eid == kPlayerEntity || !engine::entity_by_id(eid)) return wo163::kRNoActor;
                void* s = hits::soul_of_eid(eid);
                soul = s ? (buffs::as_c_soul(s) ? buffs::as_c_soul(s) : s) : nullptr;
                if (!soul) return wo163::kRNoActor;
            } else {
                soul = g_dude2.load();
            }
            uint32_t before = 0, after = 0;
            if (soul && !flags_of(soul, &before)) return wo163::kRFailed;
            void* ps = rttr::read_player_soul();
            void* psc = ps && buffs::as_c_soul(ps) ? buffs::as_c_soul(ps) : ps;
            bool ok = true;
            if (on) {
                if (!dude2_allowed(soul == ps || soul == psc, before, g_bitLocal.load())) {
                    logf("WO201-DUDE2 refused eid=0x%X flags=0x%X -- the player's own soul, a copy or a local player is never Dude2", eid, before);
                    out[0] = 0; std::memcpy(out + 1, &before, 4); std::memcpy(out + 5, &before, 4); *outLen = 9;
                    return wo163::kROk;
                }
                const char* why = "";
                if (!arm_save_gate(&why)) { logf("WO201-DUDE2 save gate NOT armed (%s) -- the bit is never set", why); return wo163::kRFailed; }
                void* old = g_dude2.load();
                if (old && old != soul) { uint32_t of = 0; if (flags_of(old, &of)) write_flags(old, of & ~(1u << g_bitPlayer.load())); }
                ok = write_flags(soul, before | (1u << g_bitPlayer.load()));
                if (ok) { g_dude2 = soul; g_dude2Eid = eid; c_dude2Sets.fetch_add(1); }
            } else if (soul) {
                ok = write_flags(soul, before & ~(1u << g_bitPlayer.load()));
                if (soul == g_dude2.load()) { g_dude2 = nullptr; g_dude2Eid = 0; }
            }
            if (soul) flags_of(soul, &after);
            logf("WO201-DUDE2 %s eid=0x%X flags 0x%X -> 0x%X%s", on ? "set" : "cleared", eid, before, after, ok ? "" : " (FAILED)");
            out[0] = ok ? 1 : 0; std::memcpy(out + 1, &before, 4); std::memcpy(out + 5, &after, 4);
            *outLen = 9;
            return wo163::kROk;
        }
        case kOpDude2Census: {
            // [op] -> [souls:4][withPlayerBit:4][withLocalBit:4][playerSoulHasBit:1][firstOtherEid:4]   the save-decode check's read: after the
            // engine loads a save, the only soul with IsPlayer is the player's own (Dude2's bit was never written)
            if (cap < 17 || g_flagOff.load() <= 0) return g_flagOff.load() <= 0 ? wo163::kRFailed : wo163::kRBadRequest;
            struct C { void* ps; void* psc; uint32_t souls = 0, pl = 0, lo = 0, firstOther = 0; uint8_t psBit = 0; } c;
            c.ps = rttr::read_player_soul();
            c.psc = c.ps && buffs::as_c_soul(c.ps) ? buffs::as_c_soul(c.ps) : c.ps;
            engine::for_each_entity([](void* e, void* vc) -> bool {
                C& c = *static_cast<C*>(vc);
                const uint32_t eid = engine::entity_id(e);
                void* s = eid ? hits::soul_of_eid(eid) : nullptr;
                void* sc = s && buffs::as_c_soul(s) ? buffs::as_c_soul(s) : s;
                uint32_t f = 0;
                if (!sc || !flags_of(sc, &f)) return false;
                ++c.souls;
                const bool pb = (f >> g_bitPlayer.load()) & 1u;
                if (pb) ++c.pl;
                if ((f >> g_bitLocal.load()) & 1u) ++c.lo;
                if (sc == c.psc || s == c.ps) { if (pb) c.psBit = 1; }
                else if (pb && !c.firstOther) c.firstOther = eid;
                return false;
            }, &c);
            std::memcpy(out, &c.souls, 4); std::memcpy(out + 4, &c.pl, 4); std::memcpy(out + 8, &c.lo, 4); out[12] = c.psBit;
            std::memcpy(out + 13, &c.firstOther, 4);
            *outLen = 17;
            logf("WO201-DUDE2 census: %u souls, %u with IsPlayer, %u with IsLocalPlayer, the player's own %s, first other with IsPlayer eid=0x%X",
                 c.souls, c.pl, c.lo, c.psBit ? "has it" : "LACKS it", c.firstOther);
            return wo163::kROk;
        }
        case kOpDude2Check: {
            if (cap < 12) return wo163::kRBadRequest;
            void* d2 = g_dude2.load();
            void* ps = rttr::read_player_soul();
            uint32_t pf = 0, df = 0;
            const bool psOk = ps != nullptr && ps != d2 && (!buffs::as_c_soul(ps) || buffs::as_c_soul(ps) != d2);
            const bool dOk = d2 && flags_of(d2, &df);
            const uint32_t eid = g_dude2Eid.load();
            // the soul behind the figure's entity still the one we flagged (a load replaces souls): else forgotten, never written again
            if (d2 && eid) {
                void* s = engine::entity_by_id(eid) ? hits::soul_of_eid(eid) : nullptr;
                void* sc = s && buffs::as_c_soul(s) ? buffs::as_c_soul(s) : s;
                if (sc != d2) { g_dude2 = nullptr; g_dude2Eid = 0; logf("WO201-DUDE2 the figure's soul is gone or replaced (a load?) -- forgotten"); }
            }
            (void)pf;
            out[0] = g_saveGateArmed ? 1 : 0;
            out[1] = psOk ? 1 : 0;
            out[2] = dOk && ((df >> g_bitPlayer.load()) & 1u) ? 1 : 0;
            out[3] = dOk && ((df >> g_bitLocal.load()) & 1u) ? 1 : 0;
            const uint32_t fo = static_cast<uint32_t>(g_flagOff.load()), sc2 = c_saveClears.load();
            std::memcpy(out + 4, &fo, 4); std::memcpy(out + 8, &sc2, 4);
            *outLen = 12;
            return wo163::kROk;
        }
        case kOpNear:
            return wo201near::handle(body, len, out, cap, outLen);
        case kOpSpeechPoll:
        case kOpSpeechPlay:
            return wo201speech::handle(op, body, len, out, cap, outLen);
        case kOpVotePoll:
            return wo201vote::handle(body, len, out, cap, outLen);
        case kOpRangedPoll:
            return wo201ranged::handle(body, len, out, cap, outLen);
        default:
            return wo163::kRBadRequest;
    }
}

} // namespace kcdmp::wo201
