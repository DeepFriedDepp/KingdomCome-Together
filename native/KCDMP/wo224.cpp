// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-224: the native half's switch word and status, the clinch instrument, the mod's own clinch start and end (wo224.h).
#include "wo224.h"
#include "wo224_rules.h"
#include "wo203.h"
#include "wo163.h"
#include "wo187.h"
#include "wo197.h"
#include "wo209.h"
#include "wo209_rules.h"
#include "engine.h"
#include "hits.h"
#include "main_thread.h"
#include "motion.h"
#include "npc_drive.h"
#include "npc_scan.h"
#include "wo166.h"
#include "fault_guard.h"
#include "anchors.h"
#include "hook_prologues.h"
#include "inline_hook.h"
#include "log.h"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <unordered_map>

namespace kcdmp::wo224 {
void late_end_tick();                 // A.3: a puppet left in a clinch move (defined with end_clinch)
void late_end_cancel(uint32_t eid);

namespace {
struct N { const char* n; uint32_t b; };
const N kNames[] = {{"w224_clinch", kBitClinch}, {"w224_chains", kBitChains}, {"w224_death", kBitDeath}, {"w224_clinch_log", kBitClinchLog}};
}

uint32_t twin_off_bits(const char* names) {
    if (!names) return 0;
    uint32_t bits = 0;
    const char* p = names;
    while (*p) {
        while (*p == ',' || *p == ' ') ++p;
        const char* e = p;
        while (*e && *e != ',' && *e != ' ') ++e;
        const size_t len = static_cast<size_t>(e - p);
        if (len == 3 && std::strncmp(p, "all", 3) == 0) bits |= kBitsDefault & ~kBitClinchLog;   // the instrument: every run keeps it
        for (const N& x : kNames) if (std::strlen(x.n) == len && std::strncmp(p, x.n, len) == 0) bits |= x.b;
        p = e;
    }
    return bits;
}

namespace {

constexpr uint32_t kPlayerEntity = 0x7777;
std::atomic<uint32_t> g_bits{kBitsDefault};
std::atomic<bool> g_configured{false};
std::atomic<uint8_t> g_role{0};

std::string env(const char* k) {
    char b[256] = {};
    const DWORD n = GetEnvironmentVariableA(k, b, sizeof b);
    return (n > 0 && n < sizeof b) ? std::string(b, n) : std::string();
}

uint32_t twin_off() {
    static uint32_t s = 0xFFFFFFFFu;
    if (s == 0xFFFFFFFFu) {
        const std::string v = env("KCDMP_TEST_W224_OFF");
        s = v.empty() ? 0 : twin_off_bits(v.c_str());
        if (s) logf("W224-CFG the bench's twin: bits 0x%X off for this game (KCDMP_TEST_W224_OFF=%s)", s, v.c_str());
    }
    return s;
}

struct More { const char* area; StatusFn fn; };
More g_more[24];
int g_moreN = 0;

template <class T> bool rd(const void* base, ptrdiff_t off, T* out) {
    KCDMP_FAULT_READ(site, "wo224::rd");
    if (!base || !fault::plausible_address(reinterpret_cast<uintptr_t>(base) + static_cast<uintptr_t>(off))) return false;
    return fault::guarded(site, [&] { *out = *reinterpret_cast<const T*>(static_cast<const char*>(base) + off); });
}

uint32_t player_eid() {
    void* e = engine::entity_by_id(kPlayerEntity);
    return e ? engine::entity_id(e) : 0;
}
const char* name_of(uint32_t eid) {
    void* e = eid ? engine::entity_by_id(eid) : nullptr;
    const char* n = e ? engine::entity_name(e) : nullptr;
    return n ? n : "?";
}
// a combat actor's entity id (its owner C_Actor at +0x2D8, the entity id at +0x30); 0 = unreadable
uint32_t eid_of_ca(const void* ca) {
    void* owner = nullptr; uint32_t eid = 0;
    if (!ca || !rd(ca, 0x2D8, &owner) || !owner || !rd(owner, 0x30, &eid)) return 0;
    return eid;
}
std::string who(const void* ca) {
    if (ca && ca == motion::player_combat_actor()) return "the-local-player";
    const uint32_t e = eid_of_ca(ca);
    return e ? name_of(e) : "?";
}
bool parts(uint32_t eid, void** ca, void** model) {
    if (eid == 0 || eid == player_eid()) return motion::player_combat_parts(ca, model) && *ca;
    return motion::combat_parts(eid, false, ca, model) && *ca;
}

// ---- A.0: the instrument ---------------------------------------------------------------------------------------------------------
// the figures' combat actors, for the hooks' threads (written by the main thread once a second)
std::atomic<const void*> g_figCa[8]{};
bool is_figure_ca(const void* ca) {
    if (!ca) return false;
    for (auto& f : g_figCa) if (f.load(std::memory_order_relaxed) == ca) return true;
    return false;
}

struct SsgRec { int32_t type; const void* m; const void* s; int result; ULONGLONG tick; bool own; };
std::mutex g_ssgMu;
SsgRec g_ssgRing[64];
uint32_t g_ssgHead = 0, g_ssgTail = 0;   // under g_ssgMu
std::atomic<uint32_t> c_ssg[rules::kClasses][4]{};   // [class][result: 0 engine no, 1 engine yes, 2 the mod refused, 3 refused: the clinch is ending]

// the audit: [0 the engine's own clinch starts, 1 its clinch tests][0 on the main thread, 1 on another]
std::atomic<uint32_t> c_thread[2][2]{};
// ... and of those on another thread, how many came while the main thread was inside the mod's own per-frame work
std::atomic<uint32_t> c_beside[2]{};
// the clinch test: [class][asker: 0 the local player / the first body, 1 the other][result 0 no, 1 yes, 2 the mod refused]
std::atomic<uint32_t> c_can[rules::kClasses][2][3]{};
// the local player's last ask, for the main thread's sampler
std::atomic<ULONGLONG> g_meAskTick{0};
std::atomic<int> g_meAskResult{0};
std::atomic<int> g_meAskClass{0};
std::atomic<bool> g_meAskIsAsker{false};

// the sampler's tallies: [class 0 me-copy, 1 me-other][reason]
uint32_t c_reason[2][rules::kReasons]{};
uint32_t c_sampled[2]{}, c_agree[2]{};
double g_distSum[2]{};
uint32_t g_distN[2]{};

// the clinches the mod is ending: a re-applied sync guard (the engine's type 20) for one of these bodies is refused, so the engine's
// own Guard falls back to the plain guard (C_CombatActorActionGuard::EnterImpl's own path when a sync guard cannot be re-established)
struct Ending { std::atomic<const void*> ca{nullptr}; std::atomic<ULONGLONG> until{0}; };
Ending g_ending[8];
bool is_ending(const void* ca) {
    if (!ca) return false;
    const ULONGLONG now = GetTickCount64();
    for (auto& e : g_ending) if (e.ca.load(std::memory_order_relaxed) == ca && now < e.until.load(std::memory_order_relaxed)) return true;
    return false;
}
void mark_ending(const void* ca, unsigned ms) {
    const ULONGLONG now = GetTickCount64();
    Ending* slot = &g_ending[0];
    for (auto& e : g_ending) {
        if (e.ca.load() == ca) { slot = &e; break; }
        if (now >= e.until.load()) slot = &e;
    }
    slot->ca.store(ca);
    slot->until.store(now + ms);
}

thread_local bool t_ownStart = false;
std::atomic<ULONGLONG> g_noProposeUntil{0};
constexpr unsigned kNoProposeMs = 1000;
std::atomic<int> g_benchGate{0};   // the bench's file: 0 the rule, 1 every pair's test and start is the engine's, 2 everything refused as before

int32_t guard_type_of(void* model, char* nameOut, size_t n) {
    int32_t v = -1;
    void* s = nullptr;
    if (nameOut && n) nameOut[0] = 0;
    if (!model || !rd(model, 0x80 + 0x30, &s) || !s) return -1;
    char nm[24]{};
    for (int i = 0; i < 23; ++i) { char ch = 0; if (!rd(s, i, &ch) || !ch) break; nm[i] = ch; }
    if (nameOut && n) std::snprintf(nameOut, n, "%s", nm);
    if (std::strstr(nm, "GuardType") == nullptr) return -1;
    if (!rd(model, 0x80 + 8, &v)) return -1;
    return v;
}

float yaw_of(uint32_t eid) {
    void* e = engine::entity_by_id(eid);
    return e ? npcdrive::entity_yaw(e) : 0.0f;
}
float dist_m(uint32_t a, uint32_t b) {
    void* ea = engine::entity_by_id(a); void* eb = engine::entity_by_id(b);
    float pa[3]{}, pb[3]{};
    if (!ea || !eb || !engine::entity_world_pos(ea, pa) || !engine::entity_world_pos(eb, pb)) return -1.0f;
    return std::sqrt((pa[0] - pb[0]) * (pa[0] - pb[0]) + (pa[1] - pb[1]) * (pa[1] - pb[1]));
}

rules::Side side_of(uint32_t eid, void* ca, void* model, uint32_t wantOpp) {
    rules::Side s{};
    motion::ModelRead m{};
    if (!motion::read_model(eid, &m)) return s;
    s.read = true;
    s.combatMode = (m.valid & motion::kMvCombatMode) && m.combatMode;
    s.opponentIsOther = m.opponentEid != 0 && m.opponentEid == wantOpp;
    s.hasOpponent = m.opponentEid != 0;
    s.guardType = guard_type_of(model, nullptr, 0);
    const wo187::DirAction a = wo187::director_scope(ca, 0);
    std::snprintf(s.action, sizeof s.action, "%s", a.ok ? (a.act ? a.name : "none") : "?");
    return s;
}

// the sampler (main thread, at most ten times a second while the local player's clinch test is being asked)
void sample_tick() {
    static ULONGLONG s_at = 0;
    const ULONGLONG now = GetTickCount64();
    if (now - s_at < 100) return;
    const ULONGLONG asked = g_meAskTick.load(std::memory_order_relaxed);
    if (!asked || now - asked > 150) return;
    s_at = now;
    const int cls = g_meAskClass.load(std::memory_order_relaxed);
    if (cls < 0 || cls > 1) return;
    const uint32_t me = player_eid();
    void* mca = nullptr; void* mm = nullptr;
    if (!me || !motion::player_combat_parts(&mca, &mm) || !mca) return;
    motion::ModelRead pm{};
    if (!motion::read_model(me, &pm)) return;
    const uint32_t opp = pm.opponentEid;
    int reason = rules::kNoOpponent;
    float d = -1;
    if (opp) {
        void* oca = nullptr; void* om = nullptr;
        if (!parts(opp, &oca, &om)) reason = rules::kUnread;
        else {
            const rules::Side a = side_of(me, mca, mm, opp), b = side_of(opp, oca, om, me);
            const float ya = yaw_of(me), yb = yaw_of(opp);
            const float dot = std::cos(ya) * std::cos(yb) + std::sin(ya) * std::sin(yb);   // forward = (-sin, cos): the same dot
            reason = rules::why_not(a, b, dot);
            d = dist_m(me, opp);
        }
    }
    ++c_sampled[cls];
    ++c_reason[cls][reason];
    const bool enginePass = g_meAskResult.load(std::memory_order_relaxed) == 1;
    if ((reason == rules::kPass) == enginePass || g_meAskResult.load() == 2) ++c_agree[cls];
    if (d >= 0) { g_distSum[cls] += d; ++g_distN[cls]; }
}

void watch_add(uint32_t eid, bool keep);
void figures_tick() {
    static ULONGLONG s_at = 0;
    const ULONGLONG now = GetTickCount64();
    if (now - s_at < 1000) return;
    s_at = now;
    uint32_t figs[8]; void* souls[8];
    const int nf = hits::avatar_list(figs, souls, 8);
    for (int i = 0; i < 8; ++i) {
        void* ca = nullptr; void* model = nullptr;
        if (i < nf && motion::combat_parts(figs[i], false, &ca, &model)) g_figCa[i].store(ca, std::memory_order_relaxed);
        else g_figCa[i].store(nullptr, std::memory_order_relaxed);
        if (i < nf) watch_add(figs[i], true);
    }
}

// ---- the clinch timeline (main thread): the local player, every figure and every body a clinch start named are read each frame; a
// body entering or leaving a clinch is one line, so the two machines' logs say when each screen showed it and when it ended there:
//   W224-STATE <name> in a clinch: master=<0|1> guard_type=<g> opponent=<name> scope0=<a> scope1=<b>
//   W224-STATE <name> out of the clinch after <ms> ms: scope0=<a> scope1=<b> guard_type=<g>
struct Watch { uint32_t eid; bool in; ULONGLONG since, lastIn; bool keep; uint32_t opp; };
uint32_t c_figureOut = 0;
// The host: a partner's figure entered or left a clinch in the host's world (its NPC grabbed him, or the host's engine started the
// one he asked for) -- told to the agent as a LocalAction frame of the figure (kind AvatarClinch, phase and role in the flags, the other
// body's name hash in the row's first four bytes), which sends it to that partner: his own game enters or leaves the same clinch.
void figure_event(uint32_t figure, bool in, bool master, uint32_t opp) {
    if (role() != 1 || !on(kBitClinch) || !motion::is_avatar_eid(figure)) return;
    uint8_t g[16]{};
    const uint32_t h = rules::name_hash(name_of(opp));
    std::memcpy(g, &h, 4);
    std::memcpy(g + 4, &opp, 4);
    motion::push_body_action(20 /* ActionKind.AvatarClinch */, static_cast<uint8_t>((in ? 1 : 3) | (master ? 4 : 0)), figure, name_of(figure), g);
    ++c_figureOut;
}
Watch g_watch[24];
int g_watchN = 0;
uint32_t c_stateIn = 0, c_stateOut = 0;
void watch_add(uint32_t eid, bool keep) {
    if (!eid) return;
    for (int i = 0; i < g_watchN; ++i) if (g_watch[i].eid == eid) { g_watch[i].keep |= keep; return; }
    if (g_watchN >= 24) return;
    g_watch[g_watchN++] = Watch{eid, false, 0, GetTickCount64(), keep, 0};
}
void watch_tick() {
    const ULONGLONG now = GetTickCount64();
    const uint32_t me = player_eid();
    watch_add(me, true);
    for (int i = 0; i < g_watchN;) {
        Watch& w = g_watch[i];
        if (!engine::entity_by_id(w.eid)) { g_watch[i] = g_watch[--g_watchN]; continue; }
        const ClinchRead r = read_clinch(w.eid);
        if (r.ok && r.in != w.in) {
            const bool say = on(kBitClinchLog);
            if (r.in) {
                ++c_stateIn;
                if (say) logf("W224-STATE %s in a clinch: master=%d guard_type=%d opponent=%s scope0=%s scope1=%s", w.eid == me ? "the-local-player" : name_of(w.eid), r.master ? 1 : 0,
                     r.guardType, r.opponent == me ? "the-local-player" : name_of(r.opponent), r.scope0, r.scope1);
                watch_add(r.opponent, false);
                w.opp = r.opponent;
                figure_event(w.eid, true, r.master, r.opponent);
            } else {
                figure_event(w.eid, false, false, w.opp);
                ++c_stateOut;
                if (say) logf("W224-STATE %s out of the clinch after %llu ms: scope0=%s scope1=%s guard_type=%d", w.eid == me ? "the-local-player" : name_of(w.eid),
                     static_cast<unsigned long long>(now - w.since), r.scope0, r.scope1, r.guardType);
            }
            w.in = r.in;
            w.since = now;
        }
        if (w.in) w.lastIn = now;
        if (!w.keep && !w.in && now - w.lastIn > 10000) { g_watch[i] = g_watch[--g_watchN]; continue; }
        ++i;
    }
}

std::atomic<bool> g_localStart{false};   // the local player's own new clinch started (wo197's poll sends its "enter")
uint32_t c_ssgLines = 0;
void ssg_drain() {
    SsgRec recs[64];
    int n = 0;
    {
        std::lock_guard<std::mutex> lk(g_ssgMu);
        while (g_ssgTail != g_ssgHead && n < 64) recs[n++] = g_ssgRing[g_ssgTail++ % 64];
    }
    for (int i = 0; i < n; ++i) {
        const SsgRec& r = recs[i];
        if (r.result == 1) { watch_add(eid_of_ca(r.m), false); watch_add(eid_of_ca(r.s), false); }
        if (r.result == 1 && r.type == kActionStartSyncGuard && on(kBitClinch)) {
            // a clinch ended and started again inside one frame shows no edge to the watch: its start is the word
            const bool mFig = is_figure_ca(r.m), sFig = is_figure_ca(r.s);
            if (role() == 1 && (mFig || sFig)) {
                const uint32_t fig = eid_of_ca(mFig ? r.m : r.s), other = eid_of_ca(mFig ? r.s : r.m);
                for (int j = 0; j < g_watchN; ++j) if (g_watch[j].eid == fig) g_watch[j].opp = other;
                figure_event(fig, true, mFig, other);
            }
            const void* me = motion::player_combat_actor();
            // the local player's own new clinch (either role) that his ENGINE started: wo197's poll says "enter" for it now (a
            // start the mod made on the other machine's word is not told back: a13's enter storms, 14 events in a second)
            if (!r.own && me && (r.m == me || r.s == me)) g_localStart.store(true, std::memory_order_relaxed);
        }
        if (!on(kBitClinchLog)) continue;
        const uint32_t k = ++c_ssgLines;
        // a refusal loop asks four times a frame: the first 60 lines, then every started one and one refusal in fifty
        if (!(k <= 60 || r.result == 1 || k % 50 == 0)) continue;
        const uint32_t me = eid_of_ca(r.m), se = eid_of_ca(r.s);
        logf("W224-SSG type=%d master=%s slave=%s -> %s dist_m=%.2f master_scope0=%s slave_scope0=%s weapon_class=%d/%d [n=%u]", r.type, who(r.m).c_str(), who(r.s).c_str(),
             r.result == 1 ? "started" : r.result == 0 ? "refused by the engine" : r.result == 3 ? "refused (the mod is ending this clinch)" : "refused by the mod (WO-178's rule)",
             (me && se) ? dist_m(me, se) : -1.0f, wo187::director_text(const_cast<void*>(r.m), 0).c_str(), wo187::director_text(const_cast<void*>(r.s), 0).c_str(),
             (me && engine::entity_by_id(me)) ? wo166::weapon_class_id(const_cast<void*>(r.m)) : -1, (se && engine::entity_by_id(se)) ? wo166::weapon_class_id(const_cast<void*>(r.s)) : -1, k);
    }
}

// ---- the bench's file (KCDMP_TEST_W224_FILE=1 in the game's environment, never in a player's game): kcdmp-w224.txt beside the game,
// read once a second when its text changed. Lines: "gate <0|1|2>", "start <master> <slave>" (a name, or "me"), "end <name>", "read <name>".
uint32_t eid_of_word(const char* w) {
    if (std::strcmp(w, "me") == 0) return player_eid();
    return hits::eid_of_name(w);
}
void file_tick() {
    static int s_on = -1;
    if (s_on < 0) s_on = env("KCDMP_TEST_W224_FILE").empty() ? 0 : 1;
    if (!s_on) return;
    static ULONGLONG s_at = 0;
    const ULONGLONG now = GetTickCount64();
    if (now - s_at < 500) return;
    s_at = now;
    static std::string s_last;
    char b[1024] = {};
    FILE* f = nullptr;
    if (fopen_s(&f, "kcdmp-w224.txt", "rb") != 0 || !f) return;
    const size_t n = std::fread(b, 1, sizeof b - 1, f);
    std::fclose(f);
    b[n] = 0;
    if (s_last == b) return;
    s_last = b;
    logf("W224-FILE the bench's file changed (%zu bytes)", n);
    char* ctx = nullptr;
    for (char* line = strtok_s(b, "\r\n", &ctx); line; line = strtok_s(nullptr, "\r\n", &ctx)) {
        char a[80]{}, c[80]{};
        int v = 0;
        if (sscanf_s(line, "gate %d", &v) == 1) { g_benchGate.store(v); logf("W224-FILE gate %d", v); }
        else if (sscanf_s(line, "start %79s %79s", a, static_cast<unsigned>(sizeof a), c, static_cast<unsigned>(sizeof c)) == 2) {
            char why[256];
            const int r = start_clinch(eid_of_word(a), eid_of_word(c), "the bench's file", why, sizeof why);
            logf("W224-FILE start %s %s -> %d %s", a, c, r, why);
        } else if (sscanf_s(line, "end %79s", a, static_cast<unsigned>(sizeof a)) == 1) {
            logf("W224-FILE end %s -> %d", a, end_clinch(eid_of_word(a), true, "the bench's file"));
        } else if (sscanf_s(line, "readx %79s", a, static_cast<unsigned>(sizeof a)) == 1) {
            // (the bench only) what the engine's auto-trigger reads for its distance: the model's target and the actor's cached distance
            void* ca = nullptr; void* model = nullptr; void* target = nullptr; void* opp = nullptr; float cached = -9.0f;
            const uint32_t e = eid_of_word(a);
            if (parts(e, &ca, &model)) { rd(model, 0x1108, &target); rd(model, 0x1118, &opp); rd(ca, 0x370, &cached); }
            const ClinchRead r = read_clinch(e);
            logf("W224-FILE readx %s -> ca=%p target=%p opponent=%p cached_dist=%.2f in=%d master=%d guard_type=%d opponent=%s dist_to_it=%.2f scope0=%s scope1=%s", a, ca, target, opp,
                 cached, r.in, r.master, r.guardType, name_of(r.opponent), r.opponent ? dist_m(e ? e : player_eid(), r.opponent) : -1.0f, r.scope0, r.scope1);
        } else if (sscanf_s(line, "read %79s", a, static_cast<unsigned>(sizeof a)) == 1) {
            const ClinchRead r = read_clinch(eid_of_word(a));
            logf("W224-FILE read %s -> ok=%d in=%d master=%d guard_type=%d opponent=%s scope0=%s scope1=%s", a, r.ok, r.in, r.master, r.guardType, name_of(r.opponent), r.scope0, r.scope1);
        }
    }
}

uint32_t c_starts = 0, c_started = 0, c_startRefused = 0, c_startAlready = 0, c_startNoBody = 0, c_ends = 0, c_endBodies = 0, c_startCleaned = 0;

// The body a partner's clinch names by its name's hash, looked for among the NPCs near his figure (the host: the agent keeps no
// table of its own world's NPCs by name). 0 = none, or more than one with that hash.
constexpr float kResolveM = 8.0f;
uint32_t body_by_hash_near(uint32_t nearEid, uint32_t hash) {
    void* e = engine::entity_by_id(nearEid ? nearEid : player_eid());
    float p[3]{};
    if (!e || !hash || !engine::entity_world_pos(e, p)) return 0;
    struct Ctx { uint32_t hash, found; int n; };
    Ctx cx{hash, 0, 0};
    const npcscan::Anchor a{p[0], p[1], p[2]};
    uint32_t walked = 0; uint8_t refuse = 0;
    npcscan::for_each_in_radius(&a, 1, kResolveM, [](void* ent, const char* name, float, float, float, bool isHorse, void* c) {
        auto* k = static_cast<Ctx*>(c);
        if (isHorse || rules::name_hash(name) != k->hash) return;
        const uint32_t id = engine::entity_id(ent);
        if (id != k->found) { k->found = id; ++k->n; }
    }, &cx, &walked, &refuse);
    return cx.n == 1 ? cx.found : 0;
}

// ---- A: a body's weapon classes, as the engine's combat reads them ------------------------------------------------------------------
// In combat mode the engine does not look at a body's hands: its weapon class per hand slot is the combat model's property
// "WeaponClasses" (model+0xBF8), written by ONE function of the combat actor's weapon component ([ca+0x4D8]) -- for both slots it
// computes the class from the hands and sets the property. The engine calls it when its own code changes a body's weapon. A copy's
// weapon is drawn by the mod's script (WO-167 / WO-214): that call never came, and a copy that entered combat mode with a tool or a
// bowl in its hand, or whose host NPC changed its weapon in the fight, read "unarmed" (class 12) for the whole fight -- a 0.8 m reach,
// and no clinch row with an armed body (the host's grabs with that bandit were refused on the joiner's screen, 36 and 39 times in a row).
//   * class-from-hands: the callee of the reach's weapon lookup (wo166's, already resolved) at its `lea rcx,[rbx-18h]; call`;
//   * the refresh: the one function that calls class-from-hands at its +0x5A and starts with the bytes read on this build.
// Both found by their own bytes, or not used. The refresh is asked only when the hands and the property differ (checked, not assumed).
using FromHandsFn = int32_t(__fastcall*)(void* set, int32_t slot);
using WeaponsUpdateFn = void(__fastcall*)(void* set);
FromHandsFn g_fromHands = nullptr;
WeaponsUpdateFn g_weaponsUpdate = nullptr;
// the "WeaponClasses" property's own setter and place, read off the update's bytes (mov r8d,eax; lea rcx,[rsi+imm32]; xor edx,edx; call)
using WeaponClassSetFn = void(__fastcall*)(void* prop, int32_t slot, int32_t cls);
WeaponClassSetFn g_weaponClassSet = nullptr;
ptrdiff_t g_weaponClassesOff = 0;
// the host's word for a copy's weapon class (the agent: its weapon catalogue's class of the item the host's NPC holds; -1 = holds nothing)
struct WeaponHint { uint32_t eid; int cls; ULONGLONG at; };
WeaponHint g_weaponHint[64]{};
uint32_t c_weaponHints = 0, c_weaponHintSet = 0;
int weapon_hint_of(uint32_t eid) {
    for (const WeaponHint& h : g_weaponHint) if (h.eid == eid) return h.cls;
    return -2;   // no word
}
void weapon_hint_set(uint32_t eid, int cls) {
    WeaponHint* slot = nullptr;
    for (WeaponHint& h : g_weaponHint) { if (h.eid == eid) { slot = &h; break; } if (!slot && !h.eid) slot = &h; }
    if (!slot) { slot = &g_weaponHint[0]; for (WeaponHint& h : g_weaponHint) if (h.at < slot->at) slot = &h; }   // the oldest
    *slot = WeaponHint{eid, cls, GetTickCount64()};
    ++c_weaponHints;
}
int g_weaponsState = 0;   // 0 unresolved, 1 found, -1 not on this build
uint32_t c_weaponChecks = 0, c_weaponRefreshed = 0, c_weaponStill = 0, c_weaponBare = 0;
constexpr uint8_t kFromHandsCall[5] = {0x48, 0x8D, 0x4B, 0xE8, 0xE8};                                              // lea rcx,[rbx-18h]; call
constexpr uint8_t kWeaponsUpdatePrologue[12] = {0x40, 0x57, 0x48, 0x81, 0xEC, 0xB0, 0x00, 0x00, 0x00, 0x48, 0x8B, 0xF9};   // push rdi; sub rsp,0B0h; mov rdi,rcx
constexpr ptrdiff_t kWeaponsUpdateFirstCall = 0x5A;
bool resolve_weapons() {
    if (g_weaponsState) return g_weaponsState == 1;
    g_weaponsState = -1;
    const uint8_t* wl = static_cast<const uint8_t*>(wo166::weapon_lookup_fn());
    HMODULE cm = GetModuleHandleA("CombatModule.dll");
    anchor::Range t{};
    if (!wl || !cm || !anchor::section(cm, ".text", &t) || !t.contains(wl)) { logf("W224-WEAPON refresh not armed: the reach's weapon lookup is not resolved"); return false; }
    const uint8_t* fromHands = nullptr;
    for (const uint8_t* p = wl; p + sizeof kFromHandsCall + 4 <= wl + 0x90 && p + 9 <= t.end; ++p) {
        if (std::memcmp(p, kFromHandsCall, sizeof kFromHandsCall) != 0) continue;
        int32_t rel = 0;
        std::memcpy(&rel, p + 5, 4);
        fromHands = p + 9 + rel;
        break;
    }
    if (!fromHands || !t.contains(fromHands)) { logf("W224-WEAPON refresh not armed: the lookup's class-from-hands call is not where it was read"); return false; }
    const uint8_t* upd = nullptr;
    int n = 0;
    for (const uint8_t* p = t.begin + kWeaponsUpdateFirstCall; p + 5 <= t.end; ++p) {
        if (*p != 0xE8) continue;
        int32_t rel = 0;
        std::memcpy(&rel, p + 1, 4);
        if (p + 5 + rel != fromHands) continue;
        const uint8_t* f = p - kWeaponsUpdateFirstCall;
        if (std::memcmp(f, kWeaponsUpdatePrologue, sizeof kWeaponsUpdatePrologue) == 0) { upd = f; ++n; }
    }
    if (n != 1) { logf("W224-WEAPON refresh not armed: %d functions start as the weapon component's update and call class-from-hands at +0x5A", n); return false; }
    g_fromHands = reinterpret_cast<FromHandsFn>(const_cast<uint8_t*>(fromHands));
    g_weaponsUpdate = reinterpret_cast<WeaponsUpdateFn>(const_cast<uint8_t*>(upd));
    g_weaponsState = 1;
    // the property's setter: right behind the update's first class-from-hands call
    static const uint8_t kSetA[] = {0x44, 0x8B, 0xC0, 0x48, 0x8D, 0x8E};   // mov r8d,eax; lea rcx,[rsi+imm32]
    if (upd + 0x70 <= t.end && std::memcmp(upd + 0x5F, kSetA, sizeof kSetA) == 0 && upd[0x69] == 0x33 && upd[0x6A] == 0xD2 && upd[0x6B] == 0xE8) {
        int32_t off = 0, rel2 = 0;
        std::memcpy(&off, upd + 0x65, 4);
        std::memcpy(&rel2, upd + 0x6C, 4);
        const uint8_t* set = upd + 0x70 + rel2;
        if (off > 0 && off < 0x4000 && t.contains(set)) {
            g_weaponClassSet = reinterpret_cast<WeaponClassSetFn>(const_cast<uint8_t*>(set));
            g_weaponClassesOff = off;
        }
    }
    if (!g_weaponClassSet) logf("W224-WEAPON the class property's setter is not where it was read: a copy whose weapon is not in its hand yet reads unarmed until it is");
    char a[64], b[64];
    anchor::describe(fromHands, a, sizeof a);
    anchor::describe(upd, b, sizeof b);
    logf("W224-WEAPON refresh armed: class-from-hands=%s, the weapon component's update=%s -- a copy whose hands and combat weapon class differ is refreshed the engine's way", a, b);
    return true;
}

// ---- A.1 / A.3: the joiner's own clinch follows the host's world (main thread, every frame) -----------------------------------------
// The host's word for this player's figure (the agent hands it over, op 225): in a clinch with <copy> as master or slave, or out of
// every clinch. The local player's own clinch with a copy is brought to that:
//   * the host has him in one and he is not (or with another body): the same clinch is started here the engine's way, asked again
//     every kStartEveryMs while the host's word is young (the engine refuses while the two do not stand at the row's distance);
//   * he is in it with the other role (both engines started it at the same moment, each with its own body as the master): ended and
//     started again with the host's roles, between moves only;
//   * the host has him in none and he is in one with a copy: ended here -- at once when the host said its engine refused the clinch
//     his game started, after kEndGraceMs when the host's clinch ended (the same move usually ended it here in the same frame), and
//     a clinch his own game started is given kProposalMs for the host's word before it is ended as never confirmed.
struct HostWord { bool in = false, iAmMaster = false, refused = false; uint32_t copy = 0; ULONGLONG at = 0; };
HostWord g_host;
ULONGLONG g_meInSince = 0, g_startTryAt = 0, g_rolesAt = 0;
bool g_meWasIn = false, g_hostGaveUp = false, g_hostEntered = false;
constexpr unsigned kStartEveryMs = 100, kStartWindowMs = 800, kEndGraceMs = 200, kProposalMs = 1200, kRolesEveryMs = 400;
uint32_t c_recStarts = 0, c_recStarted = 0, c_recEnds = 0, c_recUnconfirmed = 0, c_recRefused = 0, c_recRoles = 0, c_hostWords = 0, c_recGaveUp = 0;

void reconcile_tick() {
    if (role() != 2 || !on(kBitClinch)) { g_host = HostWord{}; g_meWasIn = false; return; }
    const ULONGLONG now = GetTickCount64();
    const uint32_t me = player_eid();
    if (!me) return;
    const ClinchRead mine = read_clinch(0);
    if (!mine.ok) return;
    const bool withCopy = mine.in && mine.opponent && mine.opponent != me && !motion::is_avatar_eid(mine.opponent);
    if (withCopy && !g_meWasIn) g_meInSince = now;
    g_meWasIn = withCopy;
    if (g_host.in) {
        if (!engine::entity_by_id(g_host.copy)) { g_host.in = false; return; }
        const bool same = withCopy && mine.opponent == g_host.copy;
        if (same) g_hostEntered = true;
        if (!same) {
            // he was in it since this word and is out again: his own engine ended it (his move, a blow); the host's end follows
            if (g_hostEntered) return;
            if (now - g_host.at > kStartWindowMs) {
                // the engine here refused the host's clinch for the whole window (the two never stood at the row's distance, or the
                // copy's hands have no row with his): the host is told once, and ends its own -- never one body in it on one screen
                if (!g_hostGaveUp) {
                    g_hostGaveUp = true;
                    ++c_recGaveUp;
                    logf("W224-CLINCH the host's world has him in a clinch with %s; the engine here refused it for %u ms -- the host is told [n=%u]", name_of(g_host.copy), kStartWindowMs, c_recGaveUp);
                    motion::push_local_action(20 /* ActionKind.AvatarClinch */, static_cast<uint8_t>(3 /* leave */ | 0x10 /* refused */), nullptr, nullptr);
                }
                return;
            }
            if (now - g_startTryAt < kStartEveryMs) return;
            g_startTryAt = now;
            // in a clinch with another body here (both engines picked their own): the host's world decides which
            if (withCopy && std::strcmp(mine.scope0, "Guard") == 0) end_clinch(0, true, "the host's world has him with another body");
            char why[256];
            ++c_recStarts;
            const int r = start_clinch(g_host.iAmMaster ? 0 : g_host.copy, g_host.iAmMaster ? g_host.copy : 0, "the host's world has him in it", why, sizeof why);
            if (r == 1) ++c_recStarted;
            return;
        }
        // in it with the host's body: the roles
        if (mine.master != g_host.iAmMaster && now - g_rolesAt >= kRolesEveryMs && std::strcmp(mine.scope0, "Guard") == 0) {
            const ClinchRead other = read_clinch(g_host.copy);
            if (other.ok && std::strcmp(other.scope0, "Guard") == 0) {
                g_rolesAt = now;
                ++c_recRoles;
                logf("W224-CLINCH roles: here %s leads, in the host's world %s does -- ended and started again the host's way [n=%u]", mine.master ? "the local player" : name_of(g_host.copy),
                     g_host.iAmMaster ? "his figure" : name_of(g_host.copy), c_recRoles);
                end_clinch(0, true, "the host's roles");
                char why[256];
                start_clinch(g_host.iAmMaster ? 0 : g_host.copy, g_host.iAmMaster ? g_host.copy : 0, "the host's roles", why, sizeof why);
            }
        }
        return;
    }
    if (!withCopy) return;
    const bool refused = g_host.refused && g_host.copy == mine.opponent && g_host.at >= g_meInSince;
    const bool unconfirmed = now - g_meInSince >= kProposalMs;
    const bool hostEnded = g_host.at >= g_meInSince && now - g_host.at >= kEndGraceMs;
    if (!refused && !unconfirmed && !hostEnded) return;
    ++c_recEnds;
    if (refused) ++c_recRefused; else if (!hostEnded) ++c_recUnconfirmed;
    g_noProposeUntil.store(now + kNoProposeMs, std::memory_order_relaxed);
    end_clinch(0, true, refused ? "the host's engine refused it" : hostEnded ? "the host's world left it" : "the host's world never confirmed it");
    g_meWasIn = false;
}

int status_reconcile(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "host_words=%u host_says=%s%s starts_asked=%u started=%u could_not_enter_told_to_the_host=%u roles_fixed=%u ends=%u (refused by the host %u, never confirmed %u)", c_hostWords,
                         g_host.in ? "in:" : "out", g_host.in ? name_of(g_host.copy) : "", c_recStarts, c_recStarted, c_recGaveUp, c_recRoles, c_recEnds, c_recRefused, c_recUnconfirmed);
}

// ---- A.1: a paired row of a copy that is in the engine's clinch with the local player ---------------------------------------------
std::unordered_map<uint32_t, ULONGLONG> g_clinchRowAt;   // main thread
uint32_t c_rowMoves = 0, c_rowMovesRefused = 0, c_rowHeld = 0, c_rowMovesRetried = 0, c_rowMovesLapsed = 0;
// a move the engine refused (the copy mid-action): asked again each frame for kMoveRetryMs -- the host's NPC makes it at its own
// engine's moment, the copy here is a link's delay and a blend behind
struct PendingMove { uint32_t copy, frag; int zone; ULONGLONG at; };
PendingMove g_pendingMove[8];
int g_pendingMoveN = 0;
constexpr unsigned kMoveRetryMs = 500;
void moves_tick() {
    const ULONGLONG now = GetTickCount64();
    const uint32_t me = player_eid();
    for (int i = 0; i < g_pendingMoveN;) {
        const PendingMove p = g_pendingMove[i];
        const ClinchRead c = read_clinch(p.copy);
        const bool alive = c.ok && c.in && c.opponent == me;
        bool done = !alive;
        if (alive && wo209::clinch_move(p.copy, p.frag, p.zone)) {
            done = true;
            ++c_rowMoves; ++c_rowMovesRetried;
            g_clinchRowAt[p.copy] = now;
            logf("W224-CLINCH move npc=%s host frag=%u: started on a retry %llu ms after the engine's refusal [n=%u]", name_of(p.copy), p.frag,
                 static_cast<unsigned long long>(now - p.at), c_rowMovesRetried);
        } else if (!done && now - p.at > kMoveRetryMs) {
            done = true;
            ++c_rowMovesLapsed;
            logf("W224-CLINCH move npc=%s host frag=%u: the engine refused it for %u ms -- not played here (the host's blow is the host's verdict) [n=%u]", name_of(p.copy), p.frag,
                 kMoveRetryMs, c_rowMovesLapsed);
        }
        if (done) g_pendingMove[i] = g_pendingMove[--g_pendingMoveN]; else ++i;
    }
}

// ---- B: the host NPC's chain input, told as it is made ------------------------------------------------------------------------------
// The engine's combo input (the combo component's, called by the NPC automation's combo step and by the player's own attack input):
// when the body's ComboSlot is open the running swing chains into a follow-up at its chain point. The host's DLL sees every call
// (a full detour that only reads and records, on the engine's own thread, then runs the engine's function untouched); the main thread
// tells the agent those of an NPC whose opponent is a partner's figure. With the switch off the hook is not installed.
using ComboInputFn = void(__fastcall*)(void* comp, int32_t input);
ComboInputFn g_comboOrig = nullptr;
std::atomic<int> g_comboHook{0};   // 0 not tried, 1 armed, -1 not on this build
std::atomic<ptrdiff_t> g_comboSlotOff{0};
struct ComboRec { const void* ca; void* model; int32_t input; uint8_t slot; ULONGLONG tick; int32_t reqZone, curZone; };
// the combat model's RequestedAtkZone (+0x200) and the running attack's zone (+0x1C0); a property's value at +8 (motion.cpp's table)
constexpr ptrdiff_t kModelReqAtkZone = 0x200 + 8, kModelAtkZone = 0x1C0 + 8;
std::mutex g_comboMu;
ComboRec g_comboRing[64];
uint32_t g_comboHead = 0, g_comboTail = 0;   // under g_comboMu
std::atomic<uint32_t> c_comboCalls{0}, c_comboOverrun{0};
// the audit: the engine's own combo inputs by thread [0 main, 1 another], and those on another thread while the mod's main-thread work ran
std::atomic<uint32_t> c_comboThread[2]{}, c_comboBeside{0};
uint32_t c_comboOpen = 0, c_comboClosed = 0, c_comboNpc = 0, c_comboAtFigure = 0, c_chainTold = 0, c_comboLayout = 0;

void __fastcall combo_detour(void* comp, int32_t input) {
    // the engine's own thread, at its own point: the same two reads the function itself makes next, one byte, a record
    if (comp && g_role.load(std::memory_order_relaxed) == 1) {
        const void* ca = *reinterpret_cast<void* const*>(static_cast<const char*>(comp) + 8);
        void* model = ca ? *reinterpret_cast<void* const*>(static_cast<const char*>(ca) + 0x2F0) : nullptr;
        const ptrdiff_t off = g_comboSlotOff.load(std::memory_order_relaxed);
        if (model && off) {
            const uint8_t slot = *(static_cast<const uint8_t*>(model) + off + 8);
            const int32_t reqZone = *reinterpret_cast<const int32_t*>(static_cast<const char*>(model) + kModelReqAtkZone);
            const int32_t curZone = *reinterpret_cast<const int32_t*>(static_cast<const char*>(model) + kModelAtkZone);
            c_comboCalls.fetch_add(1, std::memory_order_relaxed);
            {
                const bool onMain = GetCurrentThreadId() == main_thread::main_thread_id();
                c_comboThread[onMain ? 0 : 1].fetch_add(1, std::memory_order_relaxed);
                if (!onMain && main_thread::in_mod_section()) c_comboBeside.fetch_add(1, std::memory_order_relaxed);
            }
            std::lock_guard<std::mutex> lock(g_comboMu);
            if (g_comboHead - g_comboTail < 64) g_comboRing[g_comboHead++ % 64] = ComboRec{ca, model, input, slot, GetTickCount64(), reqZone, curZone};
            else c_comboOverrun.fetch_add(1, std::memory_order_relaxed);
        }
    }
    g_comboOrig(comp, input);
}

void chain_arm() {   // main thread, the host, once
    if (g_comboHook.load() != 0) return;
    g_comboHook.store(-1);
    ptrdiff_t slot = 0;
    const void* fn = nullptr;
    if (!wo209::combo_abi(&slot, &fn) || !fn) { logf("W224-CHAIN the host's watch is not armed: the combo's input is not found off the engine's AI attack on this build"); return; }
    // the hooked function's own reads of its combat actor and model, by its bytes: mov rax,[rbx+8]; mov rcx,[rax+2F0h]
    static const uint8_t kReads[] = {0x48, 0x8B, 0x43, 0x08, 0x48, 0x8B, 0x88, 0xF0, 0x02, 0x00, 0x00};
    uint8_t head[0x1B + sizeof kReads]{};
    KCDMP_FAULT_READ(site, "wo224::chain_arm");
    if (!fault::guarded(site, [&] { std::memcpy(head, fn, sizeof head); }) || std::memcmp(head + 0x1B, kReads, sizeof kReads) != 0) {
        logf("W224-CHAIN the host's watch is not armed: the combo input does not read its combat actor and model where it did");
        return;
    }
    g_comboSlotOff.store(slot);
    const char* why = nullptr;
    if (!inlinehook::install_detour(const_cast<void*>(fn), hookpro::kComboInput, sizeof hookpro::kComboInput, reinterpret_cast<void*>(&combo_detour),
                                    reinterpret_cast<void**>(&g_comboOrig), &why)) {
        logf("W224-CHAIN the host's watch is not armed: the patch was refused (%s)", why ? why : "?");
        return;
    }
    g_comboHook.store(1);
    char a[64];
    anchor::describe(fn, a, sizeof a);
    logf("W224-CHAIN the host's watch armed: the combo component's input=%s -- an NPC's chain input at a partner's figure is told as it is made (never refused, never changed)", a);
}

void model_dump_impl(int kind, uint32_t eid, int zone, int input);
void chain_drain() {   // main thread
    ComboRec recs[32];
    int n = 0;
    {
        std::lock_guard<std::mutex> lock(g_comboMu);
        while (g_comboTail != g_comboHead && n < 32) recs[n++] = g_comboRing[g_comboTail++ % 64];
    }
    const uint32_t me = n ? player_eid() : 0;
    const ULONGLONG nowT = GetTickCount64();
    for (int i = 0; i < n; ++i) {
        const ComboRec& r = recs[i];
        bool known = false;
        wo209::combo_slot_open(r.model, &known);   // the layout's check (once); the slot itself was read in the call
        if (!known) { ++c_comboLayout; continue; }
        if (r.slot) ++c_comboOpen; else ++c_comboClosed;
        const uint32_t eid = eid_of_ca(r.ca);
        if (!eid) continue;
        const bool isMe = eid == me, isFig = motion::is_avatar_eid(eid);
        motion::ModelRead m{};
        const bool read = !isMe && !isFig && motion::read_model(eid, &m);
        const uint32_t opp = read ? m.opponentEid : 0;
        const bool atFigure = opp && motion::is_avatar_eid(opp);
        // the zone the NPC had REQUESTED for its next attack when it made the input (its automation sets it before the input); the
        // running swing's own zone is not it (b1/b2 sent that: the next row's zone was another in three of four)
        const int zone = (r.reqZone >= 0 && r.reqZone <= 5) ? r.reqZone : -1;
        if (!isMe && !isFig) { ++c_comboNpc; if (atFigure) ++c_comboAtFigure; }
        const bool tell = rules::chain_tell(r.slot != 0, isMe, isFig, atFigure);
        if (!isMe && !isFig && atFigure && (c_comboAtFigure <= 400 || c_comboAtFigure % 50 == 0))
            logf("W224-CHAIN input npc=%s requested_zone=%d running_zone=%d input=%d slot=%s opponent=%s (%llu ms ago) -> %s [n=%u]", name_of(eid), zone, r.curZone, r.input,
                 r.slot ? "open" : "closed", name_of(opp),
                 static_cast<unsigned long long>(nowT - r.tick), tell ? "told" : "not told: the engine itself dropped it (its slot was closed)", c_comboAtFigure);
        if (!tell) continue;
        uint8_t g[16]{};
        std::memcpy(g, &r.input, 4);
        model_dump_impl(1, eid, zone, r.input);   // (research, the bench only)
        motion::push_body_action(kActionNpcChain, static_cast<uint8_t>(zone < 0 ? 0xFF : zone), eid, name_of(eid), g);
        ++c_chainTold;
    }
}

uint32_t c_modelRecs = 0;
void model_dump_impl(int kind, uint32_t eid, int zone, int input) {
    static int s_on = -1;
    if (s_on < 0) s_on = env("KCDMP_TEST_W224_MODELS").empty() ? 0 : 1;
    if (!s_on || !eid) return;
    void* ca = nullptr; void* model = nullptr; void* comp = nullptr;
    if (!motion::combat_parts(eid, false, &ca, &model) || !ca || !model) return;
    rd(ca, 0x4A0, &comp);
    struct Rec { char magic[4]; uint32_t n, kind, eid, tick; int32_t zone, input; uint32_t nameHash; uint8_t model[0x1300]; uint8_t comp[0x140]; };
    static Rec r;   // main thread only
    std::memset(&r, 0, sizeof r);
    std::memcpy(r.magic, "W224", 4);
    r.n = ++c_modelRecs; r.kind = static_cast<uint32_t>(kind); r.eid = eid; r.tick = static_cast<uint32_t>(GetTickCount64());
    r.zone = zone; r.input = input; r.nameHash = rules::name_hash(name_of(eid));
    KCDMP_FAULT_READ(site, "wo224::model_dump");
    // page by page: the model's allocation may end before 0x1300
    for (size_t off = 0; off < sizeof r.model; off += 0x100)
        if (!fault::guarded(site, [&] { std::memcpy(r.model + off, static_cast<const uint8_t*>(model) + off, 0x100); })) break;
    if (comp) fault::guarded(site, [&] { std::memcpy(r.comp, comp, sizeof r.comp); });
    FILE* f = nullptr;
    if (fopen_s(&f, "kcdmp-w224-models.bin", "ab") != 0 || !f) return;
    std::fwrite(&r, 1, sizeof r, f);
    std::fclose(f);
    logf("W224-MODEL rec=%u kind=%d npc=%s zone=%d input=%d", r.n, kind, name_of(eid), zone, input);
}

int status_chain_host(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "watch=%s combo_inputs_seen=%u (slot open %u, closed %u; lost to a full record %u; layout unconfirmed %u) an_npcs=%u at_a_figure=%u told=%u | "
                         "the engine's own combo inputs by thread: main=%u other=%u, on another thread WHILE the mod's main-thread work ran=%u",
                         g_comboHook.load() == 1 ? "armed" : g_comboHook.load() ? "not on this build" : "not armed (not the host, or the switch off)", c_comboCalls.load(), c_comboOpen,
                         c_comboClosed, c_comboOverrun.load(), c_comboLayout, c_comboNpc, c_comboAtFigure, c_chainTold, c_comboThread[0].load(), c_comboThread[1].load(),
                         c_comboBeside.load());
}
int status_chain_copy(char* out, int n) { return wo209::chain_status(out, n); }

void tick() {
    file_tick();
    // B: the bench's twin (the switch masked off) keeps the watch -- its inputs are counted and logged, the agent sends none
    if (role() == 1 && (g_bits.load(std::memory_order_relaxed) & kBitChains) && g_comboHook.load(std::memory_order_relaxed) == 0) chain_arm();
    if (g_comboHook.load(std::memory_order_relaxed) == 1) chain_drain();
    if (!on(kBitClinchLog) && !on(kBitClinch)) return;
    figures_tick();
    ssg_drain();          // every start: logged with the log bit; a figure's (the host) and the local player's own (the joiner) told
    if (on(kBitClinchLog)) sample_tick();
    watch_tick();        // a body entering or leaving a clinch: logged (the instrument), a figure's told to the agent (the mechanism)
    reconcile_tick();
    late_end_tick();
    if (g_pendingMoveN) moves_tick();
}

int status_ssg(char* out, int n) {
    int k = 0;
    for (int c = 0; c < rules::kClasses && k < n - 80; ++c)
        k += std::snprintf(out + k, static_cast<size_t>(n - k), "%s%s started=%u engine_no=%u mod_no=%u ending_no=%u", c ? " ; " : "", rules::class_name(c), c_ssg[c][1].load(),
                           c_ssg[c][0].load(), c_ssg[c][2].load(), c_ssg[c][3].load());
    if (k < n - 120)
        k += std::snprintf(out + k, static_cast<size_t>(n - k), " | the mod's own: asked=%u started=%u refused=%u already=%u no_body=%u ends=%u bodies_ended=%u bench_gate=%d figure_events_out=%u stale_sync_guards_cleared=%u", c_starts,
                           c_started, c_startRefused, c_startAlready, c_startNoBody, c_ends, c_endBodies, g_benchGate.load(), c_figureOut, c_startCleaned);
    return k;
}

// the audit: on which thread the ENGINE itself makes its clinch starts and tests, and how many of those on another thread came
// while the main thread was inside the mod's own per-frame work (where the mod makes its own calls)
int status_audit(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "the engine's own clinch starts: main=%u other=%u ; its clinch tests: main=%u other=%u ; on another thread WHILE the mod's "
                         "main-thread work ran: starts=%u tests=%u", c_thread[0][0].load(), c_thread[0][1].load(), c_thread[1][0].load(), c_thread[1][1].load(), c_beside[0].load(),
                         c_beside[1].load());
}
int status_weapons(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "refresh=%s setter=%s checked=%u refreshed=%u refresh_changed_nothing=%u bare_hands_at_a_check=%u host_words=%u class_set_from_the_hosts_word=%u",
                         g_weaponsState == 1 ? "armed" : g_weaponsState ? "not on this build" : "unresolved", g_weaponClassSet ? "found" : "not found", c_weaponChecks,
                         c_weaponRefreshed, c_weaponStill, c_weaponBare, c_weaponHints, c_weaponHintSet);
}

int status_can(char* out, int n) {
    int k = 0;
    for (int c = 0; c < rules::kClasses && k < n - 160; ++c) {
        const uint32_t ay = c_can[c][0][1].load(), an = c_can[c][0][0].load(), am = c_can[c][0][2].load();
        const uint32_t by = c_can[c][1][1].load(), bn = c_can[c][1][0].load(), bm = c_can[c][1][2].load();
        k += std::snprintf(out + k, static_cast<size_t>(n - k), "%s%s %s asks: yes=%u no=%u mod_no=%u (%.1f %% of the engine's) ; %s asks: yes=%u no=%u mod_no=%u (%.1f %%)", c ? " ; " : "",
                           rules::class_name(c), rules::asker_name(c, 0), ay, an, am, (ay + an) ? 100.0 * ay / (ay + an) : 0.0, rules::asker_name(c, 1), by, bn, bm,
                           (by + bn) ? 100.0 * by / (by + bn) : 0.0);
    }
    return k;
}

int status_why(char* out, int n) {
    int k = 0;
    for (int c = 0; c < 2 && k < n - 200; ++c) {
        k += std::snprintf(out + k, static_cast<size_t>(n - k), "%s%s samples=%u agree_with_the_engine=%u mean_dist_m=%.2f:", c ? " ; " : "", rules::class_name(c), c_sampled[c], c_agree[c],
                           g_distN[c] ? g_distSum[c] / g_distN[c] : -1.0);
        for (int r = 0; r < rules::kReasons && k < n - 40; ++r)
            if (c_reason[c][r]) k += std::snprintf(out + k, static_cast<size_t>(n - k), " %s=%u", rules::reason_name(r), c_reason[c][r]);
    }
    return k;
}

int status(char* out, int n) {
    int k = std::snprintf(out, static_cast<size_t>(n), "bits=0x%X twin_off=0x%X role=%u", g_bits.load(), twin_off(), g_role.load());
    for (const N& x : kNames) {
        if (k < 0 || k >= n) break;
        k += std::snprintf(out + k, static_cast<size_t>(n - k), " %s=%d", x.n, on(x.b) ? 1 : 0);
    }
    for (int i = 0; i < g_moreN && k > 0 && k < n - 8; ++i) {
        k += std::snprintf(out + k, static_cast<size_t>(n - k), " | %s: ", g_more[i].area);
        if (k >= n) break;
        const int m = g_more[i].fn(out + k, n - k);
        if (m > 0) k += (m < n - k ? m : n - k - 1);
    }
    return k;
}

uint8_t op(uint8_t opc, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (opc == kOpConfig) {
        if (len != 6 || cap < 4) return wo163::kRBadRequest;
        uint32_t bits = 0;
        std::memcpy(&bits, body + 1, 4);
        const uint8_t wasRole = g_role.exchange(body[5]);
        if (wasRole != body[5]) logf("W224-CFG role %u -> %u (0 none, 1 the host, 2 a joiner in the host's world)", wasRole, body[5]);
        const uint32_t was = g_bits.exchange(bits);
        if (was != bits || !g_configured.exchange(true)) {
            char t[400];
            int k = std::snprintf(t, sizeof t, "bits=0x%X twin_off=0x%X role=%u", bits, twin_off(), g_role.load());
            for (const N& x : kNames) if (k > 0 && k < static_cast<int>(sizeof t)) k += std::snprintf(t + k, sizeof t - static_cast<size_t>(k), " %s=%d", x.n, on(x.b) ? 1 : 0);
            logf("W224-CFG %s (was 0x%X)", t, was);
        }
        const uint32_t eff = bits & ~twin_off();
        std::memcpy(out, &eff, 4);
        *outLen = 4;
        return wo163::kROk;
    }
    if (opc == kOpStatus) {
        char t[1400];
        int n = 0;
        if (len >= 2) {
            const int page = body[1];
            if (page == 0) {
                n = std::snprintf(t, sizeof t, "bits=0x%X twin_off=0x%X role=%u", g_bits.load(), twin_off(), g_role.load());
                for (const N& x : kNames) if (n > 0 && n < static_cast<int>(sizeof t)) n += std::snprintf(t + n, sizeof t - static_cast<size_t>(n), " %s=%d", x.n, on(x.b) ? 1 : 0);
            } else if (page <= g_moreN) {
                n = std::snprintf(t, sizeof t, "%s: ", g_more[page - 1].area);
                const int m = g_more[page - 1].fn(t + n, static_cast<int>(sizeof t) - n);
                if (m > 0) n += (m < static_cast<int>(sizeof t) - n ? m : static_cast<int>(sizeof t) - n - 1);
            }
        } else n = status(t, sizeof t);
        if (n < 0) n = 0;
        if (static_cast<size_t>(n) >= sizeof t) n = static_cast<int>(sizeof t) - 1;
        if (static_cast<size_t>(n) > cap) n = static_cast<int>(cap);
        std::memcpy(out, t, static_cast<size_t>(n));
        *outLen = static_cast<size_t>(n);
        return wo163::kROk;
    }
    if (opc == kOpClinchStart) {
        if (len != 9 || cap < 3) return wo163::kRBadRequest;
        uint32_t m = 0, s = 0;
        std::memcpy(&m, body + 1, 4); std::memcpy(&s, body + 5, 4);
        char why[256];
        const int r = on(kBitClinch) ? start_clinch(m, s, "the agent", why, sizeof why) : -1;
        const ClinchRead rm = read_clinch(m), rs = read_clinch(s);
        out[0] = r < 0 ? 255 : static_cast<uint8_t>(r);
        out[1] = rm.ok ? (rm.master ? 1 : 0) : 255;
        out[2] = rs.ok ? (rs.master ? 1 : 0) : 255;
        *outLen = 3;
        return wo163::kROk;
    }
    if (opc == kOpClinchEnd) {
        if (len != 9 || cap < 1) return wo163::kRBadRequest;
        uint32_t a = 0, b = 0;
        std::memcpy(&a, body + 1, 4); std::memcpy(&b, body + 5, 4);
        int n = 0;
        if (on(kBitClinch)) {
            n = end_clinch(a, b == 0xFFFFFFFFu, "the agent", kEndMinAgeMs);
            if (b != 0xFFFFFFFFu) n += end_clinch(b, false, "the agent", kEndMinAgeMs);
        }
        out[0] = static_cast<uint8_t>(n);
        *outLen = 1;
        return wo163::kROk;
    }
    if (opc == kOpClinchHost) {   // the joiner: the host's word for his own figure [in:1][i am master:1][refused:1][copy eid:4]
        if (len != 8 || cap < 1) return wo163::kRBadRequest;
        HostWord w{};
        w.in = body[1] != 0; w.iAmMaster = body[2] != 0; w.refused = body[3] != 0;
        std::memcpy(&w.copy, body + 4, 4);
        w.at = GetTickCount64();
        ++c_hostWords;
        if (on(kBitClinchLog))
            logf("W224-HOST the host's world: his figure %s%s%s%s [n=%u]", w.in ? "in a clinch with " : "out of the clinch", w.in ? name_of(w.copy) : "",
                 w.in ? (w.iAmMaster ? " (his figure leads)" : " (the other body leads)") : "", w.refused ? " -- its engine refused the one his game started" : "", c_hostWords);
        g_host = w;
        g_hostGaveUp = false;
        g_hostEntered = false;
        out[0] = 1;
        *outLen = 1;
        return wo163::kROk;
    }
    if (opc == kOpWeaponRefresh) {   // [eid:4] -> [result:1][right before:1][right after:1]
        if ((len != 5 && len != 6) || cap < 3) return wo163::kRBadRequest;
        uint32_t e = 0;
        std::memcpy(&e, body + 1, 4);
        // [hint:1]: the host NPC's weapon class by the agent's catalogue (0xFE = it holds nothing, 0xFF = no word)
        if (len == 6 && body[5] != 0xFF && on(kBitClinch) && e) weapon_hint_set(e, body[5] == 0xFE ? -1 : body[5]);
        int before = -1, after = -1;
        const int r = (on(kBitClinch) && e) ? weapon_refresh(e, "its weapon was drawn", &before, &after) : -1;
        out[0] = r < 0 ? 255 : static_cast<uint8_t>(r);
        out[1] = static_cast<uint8_t>(before); out[2] = static_cast<uint8_t>(after);
        *outLen = 3;
        return wo163::kROk;
    }
    if (opc == kOpChainInput) {   // [eid:4][zone:1] -> [result:1]
        if (len != 10 || cap < 1) return wo163::kRBadRequest;
        uint32_t e = 0;
        int32_t input = 0;
        std::memcpy(&e, body + 1, 4);
        std::memcpy(&input, body + 6, 4);
        out[0] = static_cast<uint8_t>((role() == 2 && e) ? wo209::chain_input(e, body[5] == 0xFF ? -1 : body[5], input) : 0);
        *outLen = 1;
        return wo163::kROk;
    }
    if (opc == kOpResolveNear) {   // [near eid:4][name hash:4] -> [eid:4]: the one body within kResolveM of that entity whose name has this hash
        if (len != 9 || cap < 4) return wo163::kRBadRequest;
        uint32_t nearE = 0, hash = 0;
        std::memcpy(&nearE, body + 1, 4); std::memcpy(&hash, body + 5, 4);
        const uint32_t found = body_by_hash_near(nearE, hash);
        std::memcpy(out, &found, 4);
        *outLen = 4;
        return wo163::kROk;
    }
    if (opc == kOpClinchRead) {
        if (len != 5 || cap < 60) return wo163::kRBadRequest;
        uint32_t e = 0;
        std::memcpy(&e, body + 1, 4);
        const ClinchRead r = read_clinch(e);
        out[0] = r.ok ? (r.in ? 1 : 0) : 255;
        out[1] = r.master ? 1 : 0;
        std::memcpy(out + 2, &r.guardType, 4);
        std::memcpy(out + 6, &r.opponent, 4);
        const size_t sl = std::strlen(r.scope0);
        std::memcpy(out + 10, r.scope0, sl);
        *outLen = 10 + sl;
        return wo163::kROk;
    }
    return wo163::kRBadRequest;
}

int status_reg(char* out, int n) { return status(out, n); }
const bool kReg = wo203::reg_op(kOpConfig, &op) && wo203::reg_op(kOpStatus, &op) && wo203::reg_op(kOpClinchStart, &op) && wo203::reg_op(kOpClinchEnd, &op) &&
                  wo203::reg_op(kOpClinchRead, &op) && wo203::reg_op(kOpClinchHost, &op) && wo203::reg_op(kOpResolveNear, &op) && wo203::reg_op(kOpWeaponRefresh, &op) && wo203::reg_op(kOpChainInput, &op) && wo203::reg_status("w224", &status_reg) && wo203::reg_tick("w224", &tick);
const bool kRegRec = add_status("A the joiner's own clinch against the host's word", &status_reconcile);
int status_rows(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "moves_through_the_engine=%u (on a retry %u) refused_at_first=%u never_started=%u halves_left_to_the_engine=%u", c_rowMoves, c_rowMovesRetried, c_rowMovesRefused,
                         c_rowMovesLapsed, c_rowHeld);
}
const bool kRegRows = add_status("A a copy's rows inside a clinch with the local player", &status_rows);
const bool kRegSsg = add_status("A clinch starts", &status_ssg);
const bool kRegChainHost = add_status("B the host's NPCs' chain inputs", &status_chain_host);
const bool kRegChainCopy = add_status("B a chain input on its copy", &status_chain_copy);
const bool kRegAudit = add_status("A audit, threads", &status_audit);
const bool kRegWeapons = add_status("A a copy's weapon classes", &status_weapons);
const bool kRegCan = add_status("A clinch test", &status_can);
const bool kRegWhy = add_status("A the local player's test, why not", &status_why);

} // namespace

bool add_status(const char* area, StatusFn fn) {
    if (g_moreN >= 24) return false;
    g_more[g_moreN++] = More{area, fn};
    return true;
}
uint8_t role() { return g_role.load(); }
bool on(uint32_t bit) { return (g_bits.load(std::memory_order_relaxed) & ~twin_off() & bit) != 0; }

void note_ssg(int32_t type, const void* masterCa, const void* slaveCa, bool otherListed, int result) {
    if (result < 0 || result > 3) return;
    // the audit: the thread the ENGINE's own clinch starts come on (the mod's own are made on the main thread)
    if (!t_ownStart) {
        const bool onMain = GetCurrentThreadId() == main_thread::main_thread_id();
        c_thread[0][onMain ? 0 : 1].fetch_add(1, std::memory_order_relaxed);
        if (!onMain && main_thread::in_mod_section()) c_beside[0].fetch_add(1, std::memory_order_relaxed);
    }
    const void* me = motion::player_combat_actor();
    const bool meIn = me && (masterCa == me || slaveCa == me);
    const int cls = rules::pair_class(meIn, otherListed, is_figure_ca(masterCa) || is_figure_ca(slaveCa));
    c_ssg[cls][result].fetch_add(1, std::memory_order_relaxed);
    if (!on(kBitClinchLog) && !on(kBitClinch)) return;   // (the records are the mechanism's too: a figure's start, the local player's)
    if (cls == rules::kClassOthers && result != 1) return;   // two NPCs of the host's own: only its started clinches are told
    std::lock_guard<std::mutex> lk(g_ssgMu);
    if (g_ssgHead - g_ssgTail >= 64) return;
    g_ssgRing[g_ssgHead++ % 64] = SsgRec{type, masterCa, slaveCa, result, GetTickCount64(), t_ownStart};
}

void note_can(const void* askerCa, const void* oppCa, bool askerIsMe, bool oppIsMe, bool otherListed, int result) {
    if (result < 0 || result > 2) return;
    {
        const bool onMain = GetCurrentThreadId() == main_thread::main_thread_id();
        c_thread[1][onMain ? 0 : 1].fetch_add(1, std::memory_order_relaxed);
        if (!onMain && main_thread::in_mod_section()) c_beside[1].fetch_add(1, std::memory_order_relaxed);
    }
    const bool meIn = askerIsMe || oppIsMe;
    const int cls = rules::pair_class(meIn, otherListed, !meIn && (is_figure_ca(askerCa) || is_figure_ca(oppCa)));
    // which side asked: the local player (or, with no local player in it, the figure) is side 0
    const int side = meIn ? (askerIsMe ? 0 : 1) : (cls == rules::kClassFigure ? (is_figure_ca(askerCa) ? 0 : 1) : 0);
    c_can[cls][side][result].fetch_add(1, std::memory_order_relaxed);
    if (meIn) {
        g_meAskResult.store(result, std::memory_order_relaxed);
        g_meAskClass.store(cls, std::memory_order_relaxed);
        g_meAskIsAsker.store(askerIsMe, std::memory_order_relaxed);
        g_meAskTick.store(GetTickCount64(), std::memory_order_relaxed);
    }
}

bool own_start_running() { return t_ownStart; }

bool can_test_is_engines(bool askerIsMe) {
    const int g = g_benchGate.load(std::memory_order_relaxed);
    if (g == 1) return true;
    if (g == 2) return false;
    // (for kNoProposeMs after the mod ended his clinch -- the host's world left it, refused it or never confirmed it -- his own test
    // is refused: still within reach, his clinch component grabbed the copy again in the very next frame, on his screen alone)
    if (GetTickCount64() < g_noProposeUntil.load(std::memory_order_relaxed)) return false;
    return on(kBitClinch) && role() == 2 && askerIsMe;
}
bool take_local_start() { return g_localStart.exchange(false, std::memory_order_relaxed); }

// The host: a partner's figure is a body his own game moves -- whether HE grabs somebody is his game's to say (his clinch arrives
// as AvatarClinch and the mod starts it here). The figure's own clinch test on the host, asked every frame by its clinch component,
// started clinches he was not in: once he had stepped back the figure was still in reach by the link's delay. Refused; an NPC's own
// test with the figure (the NPC grabs him) stays the engine's.
//
// The joiner: a clinch between a copy and a partner's figure is the host's world's alone -- it comes as his clinch event and the mod
// starts it. Left to the joiner's own engine a copy's clinch component grabbed the figure by itself (a5: a clinch of 8 s on the
// joiner's screen that the host's world never had). Both bodies' own tests and starts are refused there.
bool figure_test_refused(const void* askerCa, const void* oppCa) {
    if (!on(kBitClinch) || g_benchGate.load(std::memory_order_relaxed) == 1) return false;
    const uint8_t r = role();
    if (r == 1) return is_figure_ca(askerCa);
    if (r == 2) return is_figure_ca(askerCa) || is_figure_ca(oppCa);
    return false;
}
bool figure_start_refused(int32_t type, const void* masterCa, const void* slaveCa) {
    if (type != kActionStartSyncGuard || t_ownStart || !on(kBitClinch) || g_benchGate.load(std::memory_order_relaxed) == 1) return false;
    const uint8_t r = role();
    if (r == 1) return is_figure_ca(masterCa);
    if (r == 2) return is_figure_ca(masterCa) || is_figure_ca(slaveCa);
    return false;
}

bool start_is_engines(bool masterIsMe) {
    if (t_ownStart) return true;
    const int g = g_benchGate.load(std::memory_order_relaxed);
    if (g == 1) return true;
    if (g == 2) return false;
    return on(kBitClinch) && role() == 2 && masterIsMe;
}

ClinchRead read_clinch(uint32_t eid) {
    ClinchRead r{};
    void* ca = nullptr; void* model = nullptr;
    if (!parts(eid, &ca, &model)) return r;
    const wo187::DirAction s0 = wo187::director_scope(ca, 0), s1 = wo187::director_scope(ca, 1);
    if (!s0.ok) return r;
    r.ok = true;
    std::snprintf(r.scope0, sizeof r.scope0, "%s", s0.act ? s0.name : "none");
    std::snprintf(r.scope1, sizeof r.scope1, "%s", s1.act ? s1.name : "none");
    r.guardType = guard_type_of(model, nullptr, 0);
    r.in = rules::in_clinch(r.scope0, r.scope1, r.guardType);
    void* clinch = nullptr;
    uint8_t master = 0;
    if (rd(ca, 0x4E0, &clinch) && clinch && rd(clinch, 0x18, &master)) r.master = master != 0;
    motion::ModelRead m{};
    const uint32_t e = eid ? eid : player_eid();
    if (e && motion::read_model(e, &m)) r.opponent = m.opponentEid;
    return r;
}

int start_clinch(uint32_t masterEid, uint32_t slaveEid, const char* who_, char* why, size_t whyN) {
    if (why && whyN) why[0] = 0;
    ++c_starts;
    const uint32_t me = player_eid();
    const uint32_t m = masterEid ? masterEid : me, s = slaveEid ? slaveEid : me;
    void* mca = nullptr; void* mm = nullptr; void* sca = nullptr; void* sm = nullptr;
    if (!m || !s || m == s || !parts(m, &mca, &mm) || !parts(s, &sca, &sm)) {
        ++c_startNoBody;
        if (why) std::snprintf(why, whyN, "no combat actor for one of the two");
        return -1;
    }
    const ClinchRead rm = read_clinch(m), rs = read_clinch(s);
    if (rm.in && rs.in && rm.opponent == s && rs.opponent == m) {
        ++c_startAlready;
        if (why) std::snprintf(why, whyN, "the two are in a clinch with each other already");
        return 2;
    }
    // the rows are asked by weapon class: a copy's is made the engine's own word for what is in its hands first
    if (m != me) weapon_refresh(m, "a clinch start", nullptr, nullptr);
    if (s != me) weapon_refresh(s, "a clinch start", nullptr, nullptr);
    // a body still in a sync guard that is not this clinch (its partner left it: the engine then refuses a new start on it, "guardType
    // is not specified") gets a fresh guard first
    if (rm.ok && rules::sync_guard_type(rm.guardType) && !(rs.in && rs.opponent == m)) { mark_ending(mca, 600); wo197::force_guard(mca); ++c_startCleaned; }
    if (rs.ok && rules::sync_guard_type(rs.guardType) && !(rm.in && rm.opponent == s)) { mark_ending(sca, 600); wo197::force_guard(sca); ++c_startCleaned; }
    // each body's opponent must be the other (the engine's rows are asked for the pair): a puppet's is set the engine's way; the local
    // player's own is his lock's -- never written here
    if (rm.opponent != s && m != me) wo187::set_logical_target(mca, engine::entity_by_id(s));
    if (rs.opponent != m && s != me) wo187::set_logical_target(sca, engine::entity_by_id(m));
    const float d = dist_m(m, s);
    bool r = false;
    // the engine's own entry (wo197's resolved StartSyncGuard); not under the fault guard: both actors were resolved from live
    // entities this frame, on the main thread, at the mod's point in the frame (after the engine's own module update)
    t_ownStart = true;
    const int rr = wo197::start_sync_guard_raw(kActionStartSyncGuard, mca, sca);
    t_ownStart = false;
    r = rr == 1;
    if (rr < 0) {
        ++c_startNoBody;
        if (why) std::snprintf(why, whyN, "StartSyncGuard not armed on this build");
        return -1;
    }
    if (r) { ++c_started; late_end_cancel(m); late_end_cancel(s); } else ++c_startRefused;
    const uint32_t k = c_started + c_startRefused;
    if (why) std::snprintf(why, whyN, "dist_m=%.2f master scope0=%s guard_type=%d opponent=%s weapon_class=%d ; slave scope0=%s guard_type=%d opponent=%s weapon_class=%d", d, rm.scope0,
                           rm.guardType, name_of(rm.opponent), wo166::weapon_class_id(mca), rs.scope0, rs.guardType, name_of(rs.opponent), wo166::weapon_class_id(sca));
    if (k <= 60 || k % 20 == 0)
        logf("W224-CLINCH start by %s: master=%s slave=%s -> %s (%s) [n=%u]", who_, m == me ? "the-local-player" : name_of(m), s == me ? "the-local-player" : name_of(s),
             r ? "started the engine's way" : "refused by the engine", why ? why : "", k);
    return r ? 1 : 0;
}

// A.3: a puppet (a partner's figure; on the joiner also a copy) that is in a clinch MOVE when its clinch is ended is left to finish
// it -- but nothing here ends a puppet's move when its partner body has gone its own way: a13, the joiner's figure stood in a
// SyncTransition for 4.3 s after his own screen had left the clinch (the host's NPC had gone into its own strike). 0.7 s after
// the end it is looked at again, and a puppet still in the clinch gets its fresh guard. A new start for it cancels the look.
uint32_t c_endTooFresh = 0;
struct LateEnd { uint32_t eid; ULONGLONG due; };
LateEnd g_lateEnd[8]{};
uint32_t c_lateEnds = 0;
constexpr unsigned kLateEndMs = 700;
bool is_puppet_here(uint32_t eid) { return eid && eid != player_eid() && (motion::is_avatar_eid(eid) || role() == 2); }
void late_end_add(uint32_t eid) {
    LateEnd* slot = nullptr;
    for (LateEnd& l : g_lateEnd) { if (l.eid == eid) { slot = &l; break; } if (!slot && !l.eid) slot = &l; }
    if (slot) *slot = LateEnd{eid, GetTickCount64() + kLateEndMs};
}
void late_end_cancel(uint32_t eid) {
    for (LateEnd& l : g_lateEnd) if (l.eid == eid) l = LateEnd{};
}
void late_end_tick() {
    const ULONGLONG now = GetTickCount64();
    for (LateEnd& l : g_lateEnd) {
        if (!l.eid || now < l.due) continue;
        const uint32_t e = l.eid;
        l = LateEnd{};
        const ClinchRead r = read_clinch(e);
        void* ca = nullptr; void* model = nullptr;
        if (!r.ok || !r.in || !parts(e, &ca, &model)) continue;
        mark_ending(ca, 600);
        const int g = wo197::force_guard(ca);
        ++c_lateEnds; ++c_endBodies;
        const ClinchRead after = read_clinch(e);
        logf("W224-CLINCH late end: %s was still in the clinch %u ms after it ended on its player's machine (scope0=%s scope1=%s guard_type=%d) -> a fresh guard=%d now scope0=%s "
             "scope1=%s guard_type=%d [n=%u]", name_of(e), kLateEndMs, r.scope0, r.scope1, r.guardType, g, after.scope0, after.scope1, after.guardType, c_lateEnds);
    }
}

int end_clinch(uint32_t eid, bool withPartner, const char* who_, unsigned minAgeMs) {
    const uint32_t me = player_eid();
    const uint32_t a = eid ? eid : me;
    const ClinchRead ra = read_clinch(a);
    if (!ra.ok) return 0;
    // a "leave" from the other machine is the word for the clinch it knew: a clinch this machine's engine started a moment ago (a
    // re-grab while that word was on its way) is not that clinch, and its own end will be told
    if (minAgeMs && ra.in) {
        const ULONGLONG now = GetTickCount64();
        for (int j = 0; j < g_watchN; ++j)
            if (g_watch[j].eid == a && g_watch[j].in && now - g_watch[j].since < minAgeMs) {
                const uint32_t k = ++c_endTooFresh;
                if (k <= 30 || k % 25 == 0)
                    logf("W224-CLINCH end by %s not taken: %s entered this clinch %llu ms ago -- the word is older than it [n=%u]", who_, a == me ? "the-local-player" : name_of(a),
                         static_cast<unsigned long long>(now - g_watch[j].since), k);
                return 0;
            }
    }
    int n = 0;
    uint32_t bodies[2] = {a, (withPartner && ra.opponent) ? ra.opponent : 0};
    for (uint32_t b : bodies) {
        if (!b) continue;
        const ClinchRead rb = b == a ? ra : read_clinch(b);
        if (!rb.ok || !rb.in) continue;
        // (a body whose guard type is back at the plain guard is in the move that takes it out: cutting that move showed the clinch
        // snapping apart on the host's screen, and its NPC grabbed the figure again in the same frame)
        if (!rules::held_in_sync_guard(rb.scope1, rb.guardType)) { if (is_puppet_here(b)) late_end_add(b); continue; }
        if (b != a && rb.opponent != a) continue;   // the partner is in a clinch with another body: not ours to end
        void* ca = nullptr; void* model = nullptr;
        if (!parts(b, &ca, &model)) continue;
        // the engine's own way out of a sync guard that cannot be re-established: its next Guard asks for the sync guard again
        // (type 20), is refused (the detour, while the body is marked), and falls back to the plain guard and guard type main
        mark_ending(ca, 600);
        const int g = wo197::force_guard(ca);
        ++n;
        ++c_endBodies;
        const ClinchRead after = read_clinch(b);
        logf("W224-CLINCH end by %s: %s was scope0=%s scope1=%s guard_type=%d -> a fresh guard=%d now scope0=%s scope1=%s guard_type=%d", who_,
             b == me ? "the-local-player" : name_of(b), rb.scope0, rb.scope1, rb.guardType, g, after.scope0, after.scope1, after.guardType);
    }
    if (n) ++c_ends;
    return n;
}

} // namespace kcdmp::wo224

namespace kcdmp::wo224 {
// A body's combat weapon classes against its hands (main thread; not the local player's: his are the engine's own). In combat mode
// only -- out of it the engine reads the hands itself. 1 = they differed and the engine's refresh was asked, 0 = the same (nothing
// done), -1 = no such body / not in combat mode / not armed on this build.
int weapon_refresh(uint32_t eid, const char* why, int* beforeRight, int* afterRight) {
    if (beforeRight) *beforeRight = -1;
    if (afterRight) *afterRight = -1;
    void* ca = nullptr; void* model = nullptr; void* set = nullptr;
    if (!eid || eid == player_eid() || !resolve_weapons() || !motion::combat_parts(eid, false, &ca, &model) || !ca || !rd(ca, 0x4D8, &set) || !set) return -1;
    motion::ModelRead m{};
    if (!motion::read_model(eid, &m) || !(m.valid & motion::kMvCombatMode) || !m.combatMode) return -1;
    int r0 = -1, l0 = -1;
    if (!wo166::weapon_classes(ca, &r0, &l0)) return -1;
    ++c_weaponChecks;
    // the engine's own class-from-hands for both slots (reads only); not under the fault guard
    const int hr = g_fromHands(set, 0), hl = g_fromHands(set, 1);
    if (beforeRight) *beforeRight = r0;
    if (afterRight) *afterRight = r0;
    // No item object in the hand (the engine's "unarmed", 12) while the host's NPC holds a weapon: a weapon the copy did not own is
    // created by the script and is "held" by the script's reading at once, but the engine's combat finds no item there for seconds
    // (its object arrives later; three grabs in a row were refused in that time, a13). Until it is there the copy's combat weapon
    // class is the host NPC's own, set through the property's own setter; the engine's refresh takes over when the item arrives.
    const int hint = hr == 12 ? weapon_hint_of(eid) : -2;
    if (hint >= 0 && hint != 12 && g_weaponClassSet && g_weaponClassesOff) {
        if (r0 == hint) return 0;
        g_weaponClassSet(static_cast<char*>(model) + g_weaponClassesOff, 0, hint);
        int r2 = -1, l2 = -1;
        wo166::weapon_classes(ca, &r2, &l2);
        if (afterRight) *afterRight = r2;
        const uint32_t k = ++c_weaponHintSet;
        if (k <= 40 || k % 25 == 0)
            logf("W224-WEAPON npc=%s reads %d with no item in its hand yet (%s); the host's NPC holds a weapon of class %d -> its combat weapon class set to that: now %d [n=%u]",
                 name_of(eid), r0, why, hint, r2, k);
        return 1;
    }
    if (hr == r0 && hl == l0) {
        // bare hands by both readings and no word of a weapon from the host
        if (r0 == 12 && ++c_weaponBare <= 12)
            logf("W224-WEAPON npc=%s reads unarmed (%s) and its hands hold nothing (the host's word: %s) [n=%u]", name_of(eid), why,
                 hint == -1 ? "it holds nothing" : hint == -2 ? "none" : "a weapon, the setter not found", c_weaponBare);
        return 0;
    }
    g_weaponsUpdate(set);
    int r1 = -1, l1 = -1;
    wo166::weapon_classes(ca, &r1, &l1);
    if (afterRight) *afterRight = r1;
    if (r1 == r0 && l1 == l0) {   // the engine's refresh changed nothing: the two readings name an empty hand differently, or its guard is closed
        if (++c_weaponStill <= 12)
            logf("W224-WEAPON npc=%s its combat weapon classes read %d/%d, its hands hold %d/%d (%s) -> the engine's own refresh left them as they were [n=%u]", name_of(eid), r0,
                 l0, hr, hl, why, c_weaponStill);
        return 0;
    }
    ++c_weaponRefreshed;
    if (c_weaponRefreshed <= 60 || c_weaponRefreshed % 25 == 0)
        logf("W224-WEAPON npc=%s its combat weapon classes read %d/%d, its hands hold %d/%d (%s) -> the engine's own refresh: now %d/%d [n=%u]", name_of(eid), r0, l0, hr, hl, why,
             r1, l1, c_weaponRefreshed);
    return 1;
}

// The pipe's swing handler asks first (main thread): `spec` is the host NPC's row ("FragmentName, tags"). When the copy is in the
// engine's clinch with the local player and the row is a paired one, it is not played as one body's animation:
//   * a move or a sync attack the copy leads -> the copy's own attack request (the engine builds the paired action and starts the
//     local player's half in the same call), with the row's own attack zone;
//   * the clinch's start or its guard, or a half the local player leads -> nothing: the engine's clinch here already runs it.
// 1 = handled (played the engine's way, or rightly not played), 0 = not a clinch row of such a copy: the caller's usual path,
// -1 = the engine refused the move (the caller's usual path shows the row as before).
int clinch_row(uint32_t copy, const char* spec) {
    if (!spec || role() != 2 || !on(kBitClinch) || motion::is_avatar_eid(copy)) return 0;
    const uint32_t me = player_eid();
    const ClinchRead c = read_clinch(copy);
    if (!me || !c.ok || !c.in || c.opponent != me) return 0;
    const ClinchRead mine = read_clinch(0);
    if (!mine.ok || !mine.in || mine.opponent != copy) return 0;
    char name[64]{};
    size_t i = 0;
    for (; spec[i] && spec[i] != ',' && i < sizeof name - 1; ++i) name[i] = spec[i];
    uint32_t frag = 0;
    if (!wo187::fragment_id(copy, name, &frag)) return 0;
    const wo209rules::Paired role = wo209rules::paired_role(frag);
    if (role == wo209rules::Paired::No) return 0;
    if (role != wo209rules::Paired::MasterMove) { ++c_rowHeld; return 1; }
    const int zone = wo209rules::row_zone(spec);
    const bool ok = wo209::clinch_move(copy, frag, zone);
    if (ok) { ++c_rowMoves; g_clinchRowAt[copy] = GetTickCount64(); }
    else {
        ++c_rowMovesRefused;
        int at = -1;
        for (int j = 0; j < g_pendingMoveN; ++j) if (g_pendingMove[j].copy == copy) at = j;
        if (at < 0 && g_pendingMoveN < 8) at = g_pendingMoveN++;
        if (at >= 0) g_pendingMove[at] = PendingMove{copy, frag, zone, GetTickCount64()};
    }
    const uint32_t k = c_rowMoves + c_rowMovesRefused;
    if (k <= 60 || k % 25 == 0)
        logf("W224-CLINCH move npc=%s row \"%s\" -> %s [n=%u]", name_of(copy), spec, ok ? "the copy's own attack request: the engine builds the paired move and starts the local player's half"
                                                                                        : "the engine refused it (busy): asked again each frame for a moment", k);
    return 1;   // never the one-body animation over a clinch the engine runs
}
// wo208's owner asks before it routes the same host action through the engine again (its state stream is a delay behind the row)
bool clinch_row_recent(uint32_t copy) {
    auto it = g_clinchRowAt.find(copy);
    return it != g_clinchRowAt.end() && GetTickCount64() - it->second < 1500;
}
}

// wo178's start detour asks (any thread): a re-applied sync guard for a body whose clinch the mod is ending is refused
namespace kcdmp::wo224 { void model_dump(int kind, uint32_t eid, int zone, int input) { model_dump_impl(kind, eid, zone, input); } }
namespace kcdmp::wo224 { bool ending_refuses(int32_t type, const void* masterCa, const void* slaveCa) { return type != kActionStartSyncGuard && (is_ending(masterCa) || is_ending(slaveCa)); } }
