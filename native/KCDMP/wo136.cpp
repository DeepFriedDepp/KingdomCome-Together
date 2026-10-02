// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-136 -- see wo136.h.
#include "wo136.h"
#include "fault_guard.h"

#include <windows.h>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <unordered_map>
#include <vector>

#include "engine.h"
#include "hits.h"
#include "log.h"
#include "motion.h"
#include "npc_drive.h"
#include "script_context.h"
#include "wo136_rules.h"

namespace kcdmp::wo136 {
namespace {

using wo136rules::kPlayer;
using wo136rules::kNone;
constexpr float kSwingReachM = 4.0f;         // an avatar swing's target: the nearest fighter this close
constexpr float kHandoverReachM = 25.0f;     // the host's down: an avatar this close to the NPC takes over
constexpr float kHandoverScanM = 40.0f;      // ... NPCs this close to the downed player

struct NpcThreat { std::vector<wo136rules::Ev> evs; double lastSwitch = -1e9; };
std::unordered_map<uint32_t, NpcThreat> g_threat;   // main thread only

// A switch the engine has not shown yet. The combat model takes a new opponent
// a frame or more after the skirmish call (observed, H4: leave + re-add ->
// TargetChanged kcd2mp_1 and the wolf bit the avatar, while the same-frame read
// still showed the host), so every switch is read back from tick() for 3 s.
// An NPC in a fight with the player ignores a bare add (H2: overrides 0-3) and
// can be put straight back on him (H2, once); so a switch not shown after
// 0.6 s is done again (leave + re-add), at most 3 times.
enum class Kind : uint8_t { Switch, Handover, Combatant };
struct Pending { uint32_t npc, to; Kind kind; double t0, until, next, redo; int redos; char why[40]; };
std::vector<Pending> g_pending;                     // main thread only
constexpr double kPendingS = 3.0, kPendingStepS = 0.15, kRedoS = 0.6;
constexpr int kMaxRedos = 3;

// The game's own "fight this one": the Relation context combat_forcedTarget
// (NPC -> target), which the battle controller and the quest fights set on
// their fighters (Scripts.pak: AI/battles/battlegroupcontroller.xml, the
// a03 fight utility, 43 trees). Set through the WO-68 context manager (slot
// [4]); refcounted, so only a pair this module set itself is ever cleared.
constexpr const char* kForcedTarget = "combat_forcedTarget";
double now_s();
struct Forced { uint32_t to; void* npcSoul; void* toSoul; double checked; double idleSince; };
std::unordered_map<uint32_t, Forced> g_forced;     // npc -> the pair we set
std::atomic<uint32_t> c_forcedSet{0}, c_forcedClear{0}, c_forcedFail{0};

void unforce(uint32_t npc, const char* why) {
    auto it = g_forced.find(npc);
    if (it == g_forced.end()) return;
    void* ns = engine::entity_by_id(npc) ? hits::soul_of_eid(npc) : nullptr;
    void* ts = engine::entity_by_id(it->second.to) ? hits::soul_of_eid(it->second.to) : nullptr;
    // A body gone takes its store row with it (souls are keyed by wuid; the
    // count stays with a soul that is never seen again).
    if (ns && ts && ns == it->second.npcSoul && ts == it->second.toSoul) {
        const int r = sctx::set_soul_relation(ns, ts, kForcedTarget, false);
        if (r >= 0) c_forcedClear.fetch_add(1);
        logf("WO136-FORCED npc eid=0x%X -> 0x%X cleared (%s) -- %s", npc, it->second.to, r > 0 ? "read back" : r == 0 ? "already clear" : "FAILED", why);
    }
    g_forced.erase(it);
}

// Forced target npc -> to. to = the local player: nothing forced (the engine's
// own choice is the host), only a previous avatar pair cleared.
void force(uint32_t npc, uint32_t to, void* ns, void* ts, const char* why) {
    auto it = g_forced.find(npc);
    if (it != g_forced.end() && it->second.to == to) return;
    unforce(npc, "a new target");
    if (to == kPlayer || !ns || !ts) return;
    const int r = sctx::set_soul_relation(ns, ts, kForcedTarget, true);
    if (r == 1) { g_forced[npc] = {to, ns, ts, now_s(), 0}; c_forcedSet.fetch_add(1); }
    else if (r < 0) c_forcedFail.fetch_add(1);
    logf("WO136-FORCED npc eid=0x%X -> 0x%X %s -- %s", npc, to,
         r == 1 ? "set (read back)" : r == 0 ? "already set by the game (left to it)" : "NOT set (the context manager refused; see SCTX)", why);
}

std::atomic<bool> g_on{true};
std::atomic<uint32_t> c_threat{0}, c_switch{0}, c_switchFail{0}, c_swing{0}, c_swingNone{0}, c_handover{0}, c_handoverNone{0}, c_combatant{0},
    c_handoverLate{0}, c_handoverLost{0};

double now_s() { LARGE_INTEGER q, f; QueryPerformanceCounter(&q); QueryPerformanceFrequency(&f); return double(q.QuadPart) / double(f.QuadPart); }

uint32_t player_eid() {
    void* e = engine::entity_by_id(0x7777);
    return e ? engine::entity_id(e) : 0;
}

bool copy_str(const char* s, char* out, size_t n) {
    size_t i = 0;
    KCDMP_FAULT_READ(site, "wo136::copy_str");
    if (!fault::guarded(site, [&] { for (; i + 1 < n && s[i]; ++i) out[i] = s[i]; })) i = 0;
    out[i] = 0;
    return i > 0;
}

void name_of(uint32_t eid, char* out, size_t n) {
    out[0] = 0;
    void* e = eid ? engine::entity_by_id(eid) : nullptr;
    const char* s = e ? engine::entity_name(e) : nullptr;
    if (!s || !copy_str(s, out, n)) std::snprintf(out, n, "0x%X", eid);
}

const char* src_name(uint32_t src, char* buf, size_t n) {
    if (src == kPlayer) return "host";
    if (src == kNone) return "none";
    std::snprintf(buf, n, "avatar:0x%X", src);
    return buf;
}

bool pos_of(uint32_t eid, float p[3]) {
    void* e = eid ? engine::entity_by_id(eid) : nullptr;
    return e && npcdrive::entity_pos(e, p);
}

float dist(const float a[3], const float b[3]) {
    const float dx = a[0] - b[0], dy = a[1] - b[1], dz = a[2] - b[2];
    return std::sqrt(dx * dx + dy * dy + dz * dz);
}

bool is_avatar(uint32_t eid) {
    uint32_t e[16]; void* s[16];
    const int n = hits::avatar_list(e, s, 16);
    for (int i = 0; i < n; ++i) if (e[i] == eid) return true;
    return false;
}

void* soul_for(uint32_t src) { return src == kPlayer ? hits::soul_of_eid(player_eid()) : hits::soul_of_eid(src); }

uint32_t current_opponent(const motion::NpcCombat& c);

// The engine's own target switch, as the skirmish manager exposes it: an NPC
// already in a skirmish ignores AddSoulToSkirmish's override (observed, H1:
// no TargetChanged, the wolf kept the host), so it leaves its skirmish first
// (RemoveSoulFromSkirmish, WO-132) and is added back against the new target
// (override 1 -- sets the opponent of an NPC that is not fighting, WO-119).
// Read back: the combat model's opponent. method: 1 add only, 2 remove + add.
bool leave_and_add(void* ns, void* targetSoul) {
    uint64_t rv = 0;
    hits::skirmish_remove(ns, &rv);
    return hits::skirmish_add(ns, targetSoul, 1, &rv);
}

bool shows(uint32_t npc, uint32_t to) {
    motion::NpcCombat c{};
    return motion::read_npc_combat(npc, &c) && current_opponent(c) == to;
}

// true: the combat model shows it at once. false: asked (leave + re-add) and
// queued for tick()'s read back; *asked false = the calls themselves failed.
bool retarget(uint32_t npc, uint32_t to, void* targetSoul, bool* asked, bool forceIt = true) {
    *asked = false;
    void* ns = hits::soul_of_eid(npc);
    if (!ns || !targetSoul) return false;
    if (forceIt) force(npc, to, ns, targetSoul, "retarget");
    uint64_t rv = 0;
    if (hits::skirmish_add(ns, targetSoul, 1, &rv) && shows(npc, to)) { *asked = true; return true; }
    *asked = leave_and_add(ns, targetSoul);
    return *asked && shows(npc, to);
}

void queue(uint32_t npc, uint32_t to, Kind k, const char* why) {
    const double now = now_s();
    for (auto it = g_pending.begin(); it != g_pending.end(); ++it) if (it->npc == npc) { g_pending.erase(it); break; }
    if (g_pending.size() >= 64) g_pending.erase(g_pending.begin());
    Pending pd{npc, to, k, now, now + kPendingS, now + kPendingStepS, now + kRedoS, 0, {}};
    copy_str(why, pd.why, sizeof pd.why);
    g_pending.push_back(pd);
}

uint32_t current_opponent(const motion::NpcCombat& c) {
    if (c.opponentIsPlayer) return kPlayer;
    if (c.opponentEid) return c.opponentEid;
    return kNone;
}

void decide(uint32_t npc, NpcThreat& t, double now, const char* why) {
    for (auto it = t.evs.begin(); it != t.evs.end();) it = (now - it->t > wo136rules::kWindowS) ? t.evs.erase(it) : ++it;
    if (t.evs.empty()) return;
    motion::NpcCombat c{};
    if (!motion::read_npc_combat(npc, &c) || !c.hasCa) return;
    const uint32_t cur = current_opponent(c);
    // Only the fight between the players and this NPC is arbitrated: an NPC
    // busy with a third party is the engine's own business.
    if (cur != kNone && cur != kPlayer && !is_avatar(cur)) return;
    const auto p = wo136rules::pick(t.evs.data(), t.evs.size(), now, cur, t.lastSwitch);
    if (p.to == kNone) return;
    void* ts = soul_for(p.to);
    char nb[64], b1[24], b2[24];
    name_of(npc, nb, sizeof nb);
    bool asked = false;
    const bool ok = retarget(npc, p.to, ts, &asked);
    t.lastSwitch = now;
    if (ok) c_switch.fetch_add(1);
    else if (asked) queue(npc, p.to, Kind::Switch, why);
    else c_switchFail.fetch_add(1);
    logf("WO136-TARGET npc=%s %s -> %s threat=%d/%d why=%s -> %s", nb, src_name(cur, b1, sizeof b1), src_name(p.to, b2, sizeof b2),
         p.toScore, p.curScore, why, ok ? "switched (read back)" : asked ? "asked (leave + re-add; read back over the next frames)" : "NOT switched (no soul / the skirmish call failed)");
}

// for_each_entity: the nearest fighting NPC within reach of a point.
struct Near { float p[3]; float reach; uint32_t skip1, skip2; uint32_t best = 0; float bestD = 1e9f; bool needFight = true; };
bool near_visit(void* e, void* ctx) {
    auto* n = static_cast<Near*>(ctx);
    float p[3];
    if (!npcdrive::entity_pos(e, p)) return false;
    const float d = dist(p, n->p);
    if (d > n->reach || d >= n->bestD) return false;
    const uint32_t eid = engine::entity_id(e);
    if (!eid || eid == n->skip1 || eid == n->skip2 || is_avatar(eid)) return false;
    motion::NpcCombat c{};
    if (!motion::read_npc_combat(eid, &c) || !c.hasCa) return false;
    if (n->needFight && !c.combat) return false;
    n->best = eid; n->bestD = d;
    return false;
}

struct Hand { float pp[3]; uint32_t player; std::vector<uint32_t> npcs; };
bool hand_visit(void* e, void* ctx) {
    auto* h = static_cast<Hand*>(ctx);
    float p[3];
    if (!npcdrive::entity_pos(e, p) || dist(p, h->pp) > kHandoverScanM) return false;
    const uint32_t eid = engine::entity_id(e);
    if (!eid || eid == h->player || is_avatar(eid)) return false;
    motion::NpcCombat c{};
    if (motion::read_npc_combat(eid, &c) && c.hasCa && c.opponentIsPlayer) h->npcs.push_back(eid);
    return false;
}

} // namespace

void set_enabled(bool on) { g_on.store(on); }
bool enabled() { return g_on.load(); }

void note_threat(uint32_t npcEid, uint32_t srcEid, int weight, const char* why) {
    if (!g_on.load() || !npcEid) return;
    // Only a fight a partner is part of: the host's own hits count only once
    // an avatar has touched this NPC (a solo fight is left to the engine).
    auto it = g_threat.find(npcEid);
    if (it == g_threat.end()) {
        if (srcEid == kPlayer) return;
        if (g_threat.size() > 64) g_threat.clear();
        it = g_threat.emplace(npcEid, NpcThreat{}).first;
    }
    const double now = now_s();
    it->second.evs.push_back({srcEid, now, weight});
    if (it->second.evs.size() > 64) it->second.evs.erase(it->second.evs.begin());
    c_threat.fetch_add(1);
    decide(npcEid, it->second, now, why);
}

bool test_host_threat(uint32_t npcEid, int weight) {
    auto it = g_threat.find(npcEid);
    if (it == g_threat.end()) it = g_threat.emplace(npcEid, NpcThreat{}).first;
    const double now = now_s();
    it->second.evs.push_back({kPlayer, now, weight});
    decide(npcEid, it->second, now, "test-host-threat");
    return true;
}

uint32_t avatar_swing(uint32_t avatarEid) {
    if (!g_on.load() || !avatarEid) return 0;
    c_swing.fetch_add(1);
    Near n{};
    if (!pos_of(avatarEid, n.p)) return 0;
    n.reach = kSwingReachM; n.skip1 = avatarEid; n.skip2 = player_eid();
    engine::for_each_entity(&near_visit, &n);
    if (!n.best) { n.needFight = false; n.reach = 2.5f; engine::for_each_entity(&near_visit, &n); }
    if (!n.best) { c_swingNone.fetch_add(1); return 0; }
    // The avatar is a combatant in this fight: the NPC is ITS opponent.
    motion::NpcCombat ac{};
    const bool readA = motion::read_npc_combat(avatarEid, &ac);
    bool added = false;
    if (!(readA && ac.opponentEid == n.best)) {
        void* ns = hits::soul_of_eid(n.best);
        bool asked = false;
        added = retarget(avatarEid, n.best, ns, &asked, /*forceIt=*/false);
        if (added) c_combatant.fetch_add(1);
        else if (asked) { queue(avatarEid, n.best, Kind::Combatant, "avatar-swing"); added = true; }
    }
    char nb[64];
    name_of(n.best, nb, sizeof nb);
    static uint32_t s_logged = 0;
    if (s_logged++ < 40 || added)
        logf("WO136-SWING avatar eid=0x%X at npc=%s (%.1f m) skirmish=%s -- the avatar fights it; the swing threatens it", avatarEid, nb, n.bestD,
             added ? "added (its opponent now)" : "kept");
    note_threat(n.best, avatarEid, 1, "avatar-swing");
    return n.best;
}

int handover_fights(bool removePlayer, const char* why) {
    if (!g_on.load()) return 0;
    Hand h{};
    h.player = player_eid();
    if (!h.player || !pos_of(h.player, h.pp)) return 0;
    engine::for_each_entity(&hand_visit, &h);
    uint32_t ae[16]; void* as[16];
    const int na = hits::avatar_list(ae, as, 16);
    int handed = 0;
    struct Pk { uint32_t npc, to; float d; } picks[32];
    int np_ = 0;
    for (uint32_t npc : h.npcs) {
        float np[3];
        char nb[64];
        name_of(npc, nb, sizeof nb);
        if (!pos_of(npc, np)) continue;
        float d[16];
        for (int i = 0; i < na; ++i) { float ap[3]; d[i] = pos_of(ae[i], ap) ? dist(ap, np) : -1.0f; }
        const int bi = wo136rules::handover_pick(d, na, kHandoverReachM);
        if (bi < 0) {
            c_handoverNone.fetch_add(1);
            logf("WO136-HANDOVER npc=%s -> none (no avatar within %.0f m) -- %s", nb, kHandoverReachM, why);
            continue;
        }
        picks[np_++] = {npc, ae[bi], d[bi]};
        if (np_ == 32) break;
    }
    if (np_ == 0) return 0;   // solo, or nobody near a partner: the wake's own leave (WO-132) as before
    // The player leaves first: an NPC still fighting him cannot be turned.
    if (removePlayer) {
        uint64_t rv = 0;
        void* ps = hits::soul_of_eid(h.player);
        const bool ok = ps && hits::skirmish_remove(ps, &rv);
        logf("WO136-HANDOVER the downed player leaves the skirmish first -> %s (rv=0x%llX); %d of %zu NPC(s) fighting him go to a partner",
             ok ? "removed" : "FAILED", static_cast<unsigned long long>(rv), np_, h.npcs.size());
    }
    const double now = now_s();
    for (int i = 0; i < np_; ++i) {
        char nb[64];
        name_of(picks[i].npc, nb, sizeof nb);
        bool asked = false;
        const bool ok = retarget(picks[i].npc, picks[i].to, hits::soul_of_eid(picks[i].to), &asked);
        g_threat[picks[i].npc].lastSwitch = now;
        if (ok) { ++handed; c_handover.fetch_add(1); }
        else if (asked) queue(picks[i].npc, picks[i].to, Kind::Handover, why);
        else c_handoverLost.fetch_add(1);
        logf("WO136-HANDOVER npc=%s -> avatar eid=0x%X (%.1f m) %s -- %s: the fight goes on with the partner", nb, picks[i].to, picks[i].d,
             ok ? "handed over (read back)" : asked ? "asked (leave + re-add; read back over the next frames)" : "NOT asked (no soul / the skirmish call failed)", why);
    }
    return np_;
}

void tick() {
    const double now = now_s();
    // A forced pair lives while its NPC still fights: with no opponent for
    // 2 s (an animal never reads combat=1 -- H6 -- so the opponent is the
    // test), or a body gone, it is cleared (checked twice a second).
    for (auto it = g_forced.begin(); it != g_forced.end();) {
        if (now - it->second.checked < 0.5) { ++it; continue; }
        it->second.checked = now;
        motion::NpcCombat c{};
        const uint32_t npc = it->first;
        const bool alive = engine::entity_by_id(npc) && engine::entity_by_id(it->second.to);
        const bool fighting = alive && motion::read_npc_combat(npc, &c) && current_opponent(c) != kNone;
        if (fighting) it->second.idleSince = 0;
        else if (alive && it->second.idleSince == 0) it->second.idleSince = now;
        if (!alive || (!fighting && now - it->second.idleSince > 2.0)) { ++it; unforce(npc, alive ? "its fight ended" : "a body is gone"); it = g_forced.begin(); continue; }
        ++it;
    }
    if (g_pending.empty()) return;
    for (auto it = g_pending.begin(); it != g_pending.end();) {
        if (now < it->next) { ++it; continue; }
        char nb[64];
        name_of(it->npc, nb, sizeof nb);
        void* ns = engine::entity_by_id(it->npc) ? hits::soul_of_eid(it->npc) : nullptr;
        void* ts = engine::entity_by_id(it->to) ? hits::soul_of_eid(it->to) : nullptr;
        motion::NpcCombat c{};
        const bool readable = ns && ts && motion::read_npc_combat(it->npc, &c);
        const char* what = it->kind == Kind::Handover ? "WO136-HANDOVER" : it->kind == Kind::Switch ? "WO136-TARGET" : "WO136-SWING";
        const int ms = static_cast<int>((now - it->t0) * 1000.0);
        if (readable && current_opponent(c) == it->to) {
            if (it->kind == Kind::Handover) { c_handover.fetch_add(1); c_handoverLate.fetch_add(1); }
            else if (it->kind == Kind::Switch) c_switch.fetch_add(1);
            else c_combatant.fetch_add(1);
            logf("%s %s=%s -> 0x%X taken (read back after %d ms, %d redo(s)) -- %s", what, it->kind == Kind::Combatant ? "avatar" : "npc", nb, it->to, ms, it->redos, it->why);
            it = g_pending.erase(it);
            continue;
        }
        if (!readable || now > it->until) {
            if (it->kind == Kind::Handover) c_handoverLost.fetch_add(1);
            else if (it->kind == Kind::Switch) c_switchFail.fetch_add(1);
            logf("%s %s=%s -> 0x%X NOT taken after %d ms, %d redo(s) (%s) -- %s", what, it->kind == Kind::Combatant ? "avatar" : "npc", nb, it->to, ms, it->redos,
                 readable ? "the engine kept its own target" : "a body is gone", it->why);
            it = g_pending.erase(it);
            continue;
        }
        if (now >= it->redo && it->redos < kMaxRedos) {
            leave_and_add(ns, ts);
            ++it->redos;
            it->redo = now + kRedoS;
        }
        it->next = now + kPendingStepS;
        ++it;
    }
}

int status_text(char* out, int n) {
    return std::snprintf(out, n,
        "wo136 fights=%s threat=%u switch=%u switch_fail=%u swing=%u swing_none=%u combatant=%u handover=%u handover_late=%u handover_lost=%u handover_none=%u tracked=%zu pending=%zu forced=%zu forced_set=%u forced_clear=%u forced_fail=%u",
        g_on.load() ? "on" : "off", c_threat.load(), c_switch.load(), c_switchFail.load(), c_swing.load(), c_swingNone.load(),
        c_combatant.load(), c_handover.load(), c_handoverLate.load(), c_handoverLost.load(), c_handoverNone.load(), g_threat.size(), g_pending.size(),
        g_forced.size(), c_forcedSet.load(), c_forcedClear.load(), c_forcedFail.load());
}

} // namespace kcdmp::wo136
