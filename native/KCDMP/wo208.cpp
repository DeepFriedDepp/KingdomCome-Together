// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-208: the native half's switch word and status (wo208.h).
#include "wo208.h"
#include "wo218.h"
#include "wo218_rules.h"
#include "wo216.h"
#include "wo217.h"
#include "wo200.h"
#include "wo141.h"
#include "wo208_rules.h"
#include "wo203.h"
#include "wo163.h"
#include "wo187.h"
#include "wo132.h"
#include "wo195.h"   // KUBA 32: a dead NPC ends a partner's duel with it
#include "script_context.h"   // KUBA 33: the duel NPC's brain kept in its fight
#include "wo178.h"            // KUBA 33c: the host's role
#include "wo209.h"
#include "wo209_rules.h"
#include "wo143.h"
#include "wo172.h"
#include "wo221.h"
#include "wo210.h"
#include "wo210_kd.h"
#include "wo210_rules.h"
#include "wo213.h"
#include "wo214.h"
#include "wo215.h"
#include "talk_hold.h"
#include "body_drive.h"
#include "wo141.h"
#include "wo211.h"
#include "wo211_rules.h"
#include "combat_swing.h"
#include "engine.h"
#include "fault_guard.h"
#include "hits.h"
#include "buffs.h"
#include "respawn_actions.h"
#include "log.h"
#include "motion.h"
#include "npc_drive.h"
#include "npc_scan.h"
#include <windows.h>
#include <share.h>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace kcdmp::wo208 {

namespace {

using namespace wo208rules;

std::atomic<uint32_t> g_bits{kBitsDefault};
std::atomic<bool> g_configured{false};

// The bench's twin (KCDMP_TEST_W208_OFF in the game's environment, read once): those bits stay off whatever the agent says.
uint32_t twin_off() {
    static uint32_t s = 0xFFFFFFFFu;
    if (s == 0xFFFFFFFFu) {
        char b[256] = {};
        const DWORD n = GetEnvironmentVariableA("KCDMP_TEST_W208_OFF", b, sizeof b);
        s = (n > 0 && n < sizeof b) ? twin_off_bits(b) : 0;
        if (s) logf("WO208-CFG the bench's twin: bits 0x%X off for this game (KCDMP_TEST_W208_OFF=%s)", s, b);
    }
    return s;
}

uint8_t op(uint8_t opc, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (opc == kOpConfig) {
        if (len != 5 || cap < 4) return wo163::kRBadRequest;
        uint32_t bits = 0;
        std::memcpy(&bits, body + 1, 4);
        const uint32_t was = g_bits.exchange(bits);
        if (was != bits || !g_configured.exchange(true))
            logf("WO208-CFG bits=0x%X (was 0x%X; twin off 0x%X): conv_cam_player_view=%d target_in_fight=%d", bits, was, twin_off(),
                 on(kBitPlayerView) ? 1 : 0, on(kBitTargetInFight) ? 1 : 0);
        const uint32_t eff = bits & ~twin_off();
        std::memcpy(out, &eff, 4);
        *outLen = 4;
        return wo163::kROk;
    }
    if (opc == kOpStatus) {
        char t[768];
        int n = status(t, sizeof t);
        if (n < 0) n = 0;
        if (static_cast<size_t>(n) >= sizeof t) n = static_cast<int>(sizeof t) - 1;   // snprintf's count is the untruncated length
        if (static_cast<size_t>(n) > cap) n = static_cast<int>(cap);
        std::memcpy(out, t, static_cast<size_t>(n));
        *outLen = static_cast<size_t>(n);
        return wo163::kROk;
    }
    return wo163::kRBadRequest;
}

// ---- A.1 the probe (the bench only): one copy driven only from the host's recorded state ---------------------------------------
// The host's DLL (KCDMP_INSTANCE = KCDMP_TEST_W208_FEED_HOST) writes the probe NPC's scopes to KCDMP_TEST_W208_FEED every kFeedEveryS
// (one flushed line a sample, wo208_rules.h FeedSample); the joiner's DLL reads the same file (both copies run on one PC), takes its
// copy of that NPC (KCDMP_TEST_W208_PROBE, the same name on both) away from every other writer (probe_owned), and drives its full and
// upper body from the recorded values only, KCDMP_TEST_W208_FEED_DELAY_MS behind the host (the NPC stream's own delay, so its
// animation stays with its streamed position). Nothing of this runs without those variables in the game's own environment.
constexpr double kFeedEveryS = 0.05;
constexpr uint32_t kPlayerEntity = 0x7777;

double qpc_s() { LARGE_INTEGER q, f; QueryPerformanceCounter(&q); QueryPerformanceFrequency(&f); return double(q.QuadPart) / double(f.QuadPart); }
uint64_t utc_ms() {
    FILETIME ft{};
    GetSystemTimePreciseAsFileTime(&ft);
    const uint64_t t = (static_cast<uint64_t>(ft.dwHighDateTime) << 32) | ft.dwLowDateTime;
    return t / 10000ULL - 11644473600000ULL;
}
std::string env(const char* k) {
    char b[512] = {};
    const DWORD n = GetEnvironmentVariableA(k, b, sizeof b);
    return (n > 0 && n < sizeof b) ? std::string(b) : std::string();
}
template <class T> bool rd(const void* base, size_t off, T* out) {
    KCDMP_FAULT_READ(site, "wo208::read");
    return fault::guarded(site, [&] { std::memcpy(out, static_cast<const char*>(base) + off, sizeof(T)); });
}
bool wr_prio(void* act, int32_t v) {
    KCDMP_FAULT_READ(site, "wo208::own_priority_write");   // our own action's priority (WO-202's step-down; Kind::Read covers both)
    return fault::guarded(site, [&] { *reinterpret_cast<int32_t*>(static_cast<char*>(act) + 0x24) = v; });
}

struct Probe {
    bool tried = false, host = false, joiner = false;
    std::string name, path;
    double delayS = 0.4;
    FILE* out = nullptr;
    HANDLE in = INVALID_HANDLE_VALUE;
    std::string carry;                      // a line the last read cut in two
    std::vector<FeedSample> samples;        // the joiner: what the host's feed said, oldest first
    uint32_t eid = 0;
    double eidAt = -1e9, lastWrite = 0;
    struct Scope { void* act = nullptr; uint64_t hostAct = 0; uint32_t frag = 0; int32_t prio = 0; double queuedAt = 0; bool started = false; };
    Scope own[2];                           // 0 FullBody, 1 UpperBody: the one action of ours on each
    struct Retired { void* act; double at; };
    std::vector<Retired> retired;           // withdrawn actions of ours, their reference kept until they finish
    double fullEmptySince = -1, lastPulse = -1;
    uint32_t installs = 0, refusals = 0, stale = 0, withdraws = 0, steps = 0, pulses = 0, samplesIn = 0, samplesOut = 0, applied = 0, holds = 0;
} g_p;
std::atomic<uint32_t> g_probeEid{0};

const char* kOwnScopes[2] = {"FullBody", "UpperBody"};

// The probe NPC's name: KCDMP_TEST_W208_PROBE, or (empty) the bench's kcdmp-w208-probe.txt beside the game, read every 2 s -- a scenario
// that picks its NPC in the running world (the village walk) names it there. A new name starts the owner afresh.
void probe_name_tick(double now) {
    static double s_at = -1e9;
    if (!env("KCDMP_TEST_W208_PROBE").empty() || now - s_at < 2.0) return;
    s_at = now;
    char b[96] = {};
    FILE* f = nullptr;
    if (fopen_s(&f, "kcdmp-w208-probe.txt", "rb") != 0 || !f) return;
    const size_t n = std::fread(b, 1, sizeof b - 1, f);
    std::fclose(f);
    b[n] = 0;
    for (char* p = b; *p; ++p) if (*p == '\r' || *p == '\n' || *p == ' ') { *p = 0; break; }
    if (!b[0] || g_p.name == b) return;
    for (auto& o : g_p.own) if (o.act) { wr_prio(o.act, kWithdrawnPriority); g_p.retired.push_back({o.act, now}); o = Probe::Scope{}; }
    g_p.name = b;
    g_p.eid = 0; g_p.eidAt = -1e9; g_probeEid = 0;
    logf("WO208-PROBE npc=%s (the bench's file)", g_p.name.c_str());
}

void probe_init() {
    g_p.tried = true;
    g_p.name = env("KCDMP_TEST_W208_PROBE");
    // both copies get the same variables (the rig's KCDMP_RIG_GAME_ENV): the copy whose KCDMP_INSTANCE is KCDMP_TEST_W208_FEED_HOST writes
    const std::string f = env("KCDMP_TEST_W208_FEED"), hostId = env("KCDMP_TEST_W208_FEED_HOST"), me = env("KCDMP_INSTANCE"),
                      d = env("KCDMP_TEST_W208_FEED_DELAY_MS");
    if (!d.empty()) g_p.delayS = std::atof(d.c_str()) / 1000.0;
    if (f.empty() || hostId.empty() || me.empty()) return;
    g_p.path = f;
    if (me == hostId) {
        g_p.out = _fsopen(f.c_str(), "wb", _SH_DENYWR);
        g_p.host = g_p.out != nullptr;
    } else {
        g_p.joiner = true;
    }
    logf("WO208-PROBE %s npc=%s feed=%s delay_ms=%.0f", g_p.host ? "host: recording" : g_p.joiner ? "joiner: the copy is driven from the feed" : "NOT armed",
         g_p.name.c_str(), g_p.path.c_str(), g_p.delayS * 1000.0);
}

uint32_t probe_entity(double now) {
    if (g_p.name.empty()) return 0;
    if (g_p.eid && engine::entity_by_id(g_p.eid)) return g_p.eid;
    if (now - g_p.eidAt < 2.0) return 0;
    g_p.eidAt = now;
    void* e = npcdrive::entity_by_name(g_p.name.c_str());
    g_p.eid = e ? engine::entity_id(e) : 0;
    if (g_p.joiner) g_probeEid = g_p.eid;
    if (g_p.eid) logf("WO208-PROBE %s found: entity %u", g_p.name.c_str(), g_p.eid);
    return g_p.eid;
}

// the host: one sample of the probe NPC's scopes
void probe_write(double now) {
    if (now - g_p.lastWrite < kFeedEveryS) return;
    g_p.lastWrite = now;
    const uint32_t eid = probe_entity(now);
    if (!eid) return;
    wo187::ScopeInfo sc[16];
    const int n = wo187::read_all_scopes(eid, sc, 16);
    if (n <= 0) return;
    bool cm = false;
    motion::combat_mode_of(eid, &cm);
    std::string line;
    char b[256];
    std::snprintf(b, sizeof b, "t %llu cm %d", static_cast<unsigned long long>(utc_ms()), cm ? 1 : 0);
    line = b;
    int written = 0;
    for (int i = 0; i < n && written < 8; ++i) {
        if (!sc[i].ok || sc[i].empty || !sc[i].act) continue;
        uint32_t status = 0;
        uint8_t tags[20]{};
        rd(sc[i].act, 0x28, &status);
        for (int k = 0; k < 20; ++k) rd(sc[i].act, 0x3C + k, &tags[k]);
        char th[41];
        for (int k = 0; k < 20; ++k) std::snprintf(th + 2 * k, 3, "%02x", tags[k]);
        std::snprintf(b, sizeof b, " s %s %s %u %d %u %u %s %llx", sc[i].name, sc[i].action, sc[i].frag, sc[i].prio, status, sc[i].flags, th,
                      static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(sc[i].act)));
        line += b;
        ++written;
    }
    line += "\n";
    std::fwrite(line.data(), 1, line.size(), g_p.out);
    std::fflush(g_p.out);
    ++g_p.samplesOut;
}

// the joiner: the feed's new lines
void probe_read() {
    if (g_p.in == INVALID_HANDLE_VALUE) {
        g_p.in = CreateFileA(g_p.path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr);
        if (g_p.in == INVALID_HANDLE_VALUE) return;
        LARGE_INTEGER sz{};
        if (GetFileSizeEx(g_p.in, &sz)) { LARGE_INTEGER z{}; z.QuadPart = sz.QuadPart; SetFilePointerEx(g_p.in, z, nullptr, FILE_BEGIN); }   // from now on
    }
    char buf[16384];
    DWORD got = 0;
    for (int rounds = 0; rounds < 8 && ReadFile(g_p.in, buf, sizeof buf, &got, nullptr) && got > 0; ++rounds) {
        g_p.carry.append(buf, got);
        size_t at = 0, nl;
        while ((nl = g_p.carry.find('\n', at)) != std::string::npos) {
            FeedSample s;
            if (parse_feed(g_p.carry.c_str() + at, &s)) { g_p.samples.push_back(s); ++g_p.samplesIn; }
            at = nl + 1;
        }
        g_p.carry.erase(0, at);
        if (got < sizeof buf) break;
    }
    if (g_p.samples.size() > 400) g_p.samples.erase(g_p.samples.begin(), g_p.samples.begin() + static_cast<std::ptrdiff_t>(g_p.samples.size() - 400));
}

void retire(Probe::Scope& s, double now, const char* why) {
    if (!s.act) return;
    wr_prio(s.act, kWithdrawnPriority);
    g_p.retired.push_back({s.act, now});
    ++g_p.withdraws;
    if (g_p.withdraws <= 40 || g_p.withdraws % 50 == 0) logf("WO208-OWN withdraw npc=%s frag=%u why=%s (#%u)", g_p.name.c_str(), s.frag, why, g_p.withdraws);
    s = Probe::Scope{};
}

void retired_tick(double now) {
    for (size_t i = 0; i < g_p.retired.size();) {
        uint32_t st = 0;
        const bool alive = g_p.eid && engine::entity_by_id(g_p.eid);
        if (!alive || !rd(g_p.retired[i].act, 0x28, &st) || st >= 4 || now - g_p.retired[i].at > 5.0) {
            if (alive) rttr::release_action(g_p.retired[i].act);   // a body gone may have taken its controller: our reference is left (WO-202's rule)
            g_p.retired.erase(g_p.retired.begin() + static_cast<std::ptrdiff_t>(i));
        } else ++i;
    }
}

void probe_apply(double now) {
    const uint32_t eid = probe_entity(now);
    if (!eid) return;
    // the host's state one stream delay ago: the newest sample at or before it
    const uint64_t at = utc_ms() - static_cast<uint64_t>(g_p.delayS * 1000.0);
    const FeedSample* h = nullptr;
    for (const FeedSample& s : g_p.samples) { if (s.t <= at) h = &s; else break; }
    if (!h) return;
    ++g_p.applied;
    // the stance state: combat mode held while the host's NPC holds it (WO-187's guard hold, the engine's own combat mode)
    if (h->cm) {
        motion::State2 st{};
        st.bits = motion::kBitCombat;
        motion::set_npc_guard_hold(eid, true, &st, npcdrive::now_s());
        ++g_p.holds;
    } else motion::set_npc_guard_hold(eid, false, nullptr, npcdrive::now_s());
    wo187::ScopeInfo cs[16];
    const int cn = wo187::read_all_scopes(eid, cs, 16);
    for (int k = 0; k < 2; ++k) {
        const FeedScope* hs = nullptr;
        for (int i = 0; i < h->n; ++i) if (!std::strcmp(h->sc[i].scope, kOwnScopes[k])) hs = &h->sc[i];
        const Fam fam = hs ? family(hs->cls, hs->frag) : Fam::Empty;
        Probe::Scope& o = g_p.own[k];
        Own view;
        if (o.act) {
            view.have = true; view.hostAct = o.hostAct; view.ageS = now - o.queuedAt;
            rd(o.act, 0x24, &view.prio);
            rd(o.act, 0x28, &view.status);
            if (view.status == 2 || view.status == 3) o.started = true;
            if (o.started && view.status < 2) view.status = 2;
        }
        bool copyRuns = false;
        for (int i = 0; i < cn && hs; ++i)
            if (cs[i].ok && !cs[i].empty && !std::strcmp(cs[i].name, kOwnScopes[k]) && cs[i].frag == hs->frag && cs[i].act != o.act) copyRuns = true;
        const Do d = decide(fam, hs ? hs->act : 0, hs ? hs->prio : 0, copyRuns, view);
        switch (d) {
            case Do::Nothing: break;
            case Do::Withdraw: retire(o, now, copyRuns ? "the copy runs it itself" : "the host's scope is back at its base"); break;
            case Do::Stale:
                ++g_p.stale;
                logf("WO208-OWN stale npc=%s frag=%u not started in %.0f ms -- removed (#%u)", g_p.name.c_str(), o.frag, view.ageS * 1000.0, g_p.stale);
                retire(o, now, "stale");
                break;
            case Do::StepPriority:
                if (wr_prio(o.act, hs->prio)) { ++g_p.steps; o.prio = hs->prio; }
                break;
            case Do::Install: {
                if (o.act) retire(o, now, "the host's next action");
                void* act = nullptr;
                const rttr::SwingResult r = rttr::install_fragment(eid, hs->frag, hs->tags, hs->prio, hs->flags, &act);
                if (r == rttr::SwingResult::Ok && act) {
                    o.act = act; o.hostAct = hs->act; o.frag = hs->frag; o.prio = hs->prio; o.queuedAt = now; o.started = false;
                    ++g_p.installs;
                    if (g_p.installs <= 60 || g_p.installs % 50 == 0)
                        logf("WO208-OWN install npc=%s scope=%s class=%s frag=%u prio=%d (#%u)", g_p.name.c_str(), kOwnScopes[k], hs->cls, hs->frag, hs->prio, g_p.installs);
                } else {
                    ++g_p.refusals;
                    if (g_p.refusals <= 30) logf("WO208-OWN refused npc=%s scope=%s class=%s frag=%u -> %s", g_p.name.c_str(), kOwnScopes[k], hs->cls, hs->frag, rttr::swing_result_name(r));
                }
                break;
            }
        }
    }
    // rule 1: a base always exists -- the full body empty for more than 0.3 s gets the copy's own base again (its guard idle in combat
    // mode, else its locomotion graph), at most once a second
    bool empty = cn > 0 && cs[0].ok && cs[0].empty;
    if (!empty) g_p.fullEmptySince = -1;
    else {
        if (g_p.fullEmptySince < 0) g_p.fullEmptySince = now;
        if (now - g_p.fullEmptySince > 0.3 && now - g_p.lastPulse > 1.0) {
            g_p.lastPulse = now;
            bool cm = false;
            motion::combat_mode_of(eid, &cm);
            const bool ok = (cm && motion::requeue_guard_idle(eid)) || motion::request_idle_pulse(eid);
            ++g_p.pulses;
            logf("WO208-OWN base npc=%s full body empty %.0f ms -> %s (#%u)", g_p.name.c_str(), (now - g_p.fullEmptySince) * 1000.0,
                 ok ? (cm ? "guard idle" : "locomotion") : "no remedy took", g_p.pulses);
        }
    }
    retired_tick(now);
}

// ---- A.2 the host's stream: its NPCs' full and upper body as values (op 132; the agent sends them as NpcAnim) ------------------
// Every NPC within kStreamRadiusM of a player (the local player, the partners' figures; the list walked once a second), its two scopes
// read (WO-190's reader) 10 times a second by the agent's poll; a body goes out when one of them changed (another action object,
// fragment, priority or status) and again every kResendS. [count:1] then per body [nameLen:1][name][cm:1][nsc:1] nsc x kWireScope.
struct HostBody { uint64_t sig = 0; double sentAt = -1e9; };
std::unordered_map<uint32_t, HostBody> g_hostBodies;   // main thread
std::vector<uint32_t> g_streamList;
double g_listAt = -1e9;
std::atomic<uint32_t> c_streamReads{0}, c_streamBodies{0};

void stream_list(double now) {
    if (now - g_listAt < 1.0) return;
    g_listAt = now;
    g_streamList.clear();
    npcscan::Anchor an[9];
    int na = 0;
    void* pe = engine::entity_by_id(kPlayerEntity);
    float p[3]{};
    if (pe && engine::entity_world_pos(pe, p)) an[na++] = {p[0], p[1], p[2]};
    uint32_t av[8]{}; void* souls[8]{};
    const int nav = hits::avatar_list(av, souls, 8);
    for (int i = 0; i < nav && na < 9; ++i) {
        void* e = engine::entity_by_id(av[i]);
        if (e && engine::entity_world_pos(e, p)) an[na++] = {p[0], p[1], p[2]};
    }
    if (!na) return;
    uint32_t walked = 0; uint8_t refuse = 0;
    npcscan::for_each_in_radius(an, na, kStreamRadiusM, [](void* e, const char*, float, float, float, bool isHorse, void* c) {
        auto* v = static_cast<std::vector<uint32_t>*>(c);
        if (!isHorse && v->size() < 64) v->push_back(engine::entity_id(e));
    }, &g_streamList, &walked, &refuse);
    for (auto it = g_hostBodies.begin(); it != g_hostBodies.end();)
        it = std::find(g_streamList.begin(), g_streamList.end(), it->first) == g_streamList.end() ? g_hostBodies.erase(it) : std::next(it);
}

// one scope on the wire; false = nothing installed on it
bool put_scope(const wo187::ScopeInfo& s, uint8_t scope, uint8_t* o, uint64_t* sig) {
    if (!s.ok || s.empty || !s.act) return false;
    uint32_t status = 0, act = static_cast<uint32_t>(reinterpret_cast<uintptr_t>(s.act) >> 4);
    uint8_t tags[20]{};
    rd(s.act, 0x28, &status);
    for (int k = 0; k < 20; ++k) rd(s.act, 0x3C + k, &tags[k]);
    const uint16_t frag = static_cast<uint16_t>(s.frag);
    o[0] = scope; o[1] = class_id(s.action);
    std::memcpy(o + 2, &frag, 2);
    o[4] = static_cast<uint8_t>(static_cast<int8_t>(s.prio)); o[5] = static_cast<uint8_t>(status);
    std::memcpy(o + 6, &s.flags, 4);
    std::memcpy(o + 10, tags, 20);
    std::memcpy(o + 30, &act, 4);
    *sig = (*sig * 1099511628211ULL) ^ (static_cast<uint64_t>(act) << 20) ^ (static_cast<uint64_t>(frag) << 4) ^ static_cast<uint64_t>(s.prio & 0xF) ^ (static_cast<uint64_t>(status) << 40);
    return true;
}

uint8_t op_stream_read(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    if (len != 2 || cap < 1) return wo163::kRBadRequest;
    const int max = body[1] ? body[1] : 1;
    const double now = qpc_s();
    stream_list(now);
    size_t o = 1;
    int count = 0;
    for (uint32_t eid : g_streamList) {
        if (count >= max) break;
        void* e = engine::entity_by_id(eid);
        const char* nm = e ? engine::entity_name(e) : nullptr;
        if (!nm) continue;
        const size_t nl = std::strlen(nm);
        if (nl == 0 || nl > 63) continue;
        wo187::ScopeInfo sc[10];
        const int n = wo187::read_all_scopes(eid, sc, 10);
        c_streamReads.fetch_add(1);
        if (n <= 0) continue;
        bool cm = false;
        motion::combat_mode_of(eid, &cm);
        // WO-210 C.3: the body's knockdown (a human whose physics is no living entity) rides the same byte, bit 1
        const char* fbCls = nullptr;
        for (int i = 0; i < n; ++i) if (sc[i].ok && !sc[i].empty && !std::strcmp(sc[i].name, "FullBody")) fbCls = sc[i].action;
        const bool down = wo210kd::host_down(eid, e, fbCls);
        {   // WO-216 F.2: the host NPC's own knockdowns and get-ups as its stream sees them (the copy's latency is measured from these)
            static std::unordered_map<uint32_t, bool> s_down;
            auto dit = s_down.find(eid);
            if (dit == s_down.end()) s_down.emplace(eid, down);
            else if (dit->second != down) { dit->second = down; logf("WO216-KD host npc=%s down=%d", nm, down ? 1 : 0); }
        }
        uint8_t rec[2 + 2 * kWireScope];
        uint64_t sig = (cm ? 1 : 2) | (down ? 4 : 0);
        int nsc = 0;
        for (int i = 0; i < n && nsc < 2; ++i) {
            const uint8_t which = !std::strcmp(sc[i].name, "FullBody") ? 0 : !std::strcmp(sc[i].name, "UpperBody") ? 1 : 255;
            if (which == 255) continue;
            if (put_scope(sc[i], which, rec + 2 + nsc * kWireScope, &sig)) ++nsc;
        }
        // WO-216 C: the one activity line -- after the two scopes, the body's activity (0xA1: its NPC-state stance or unstance and their
        // objects, the wire's 30 bytes) and its hands (0xA2: the item classes, left and right), read now, in the same body: the joiner
        // applies the activity, the hands and the scopes from this one message (the twin: WO-141's and WO-143's own rows, as before)
        int nx = 0;
        uint8_t rx[2][kWireScope]{};
        if (wo216::on(wo216rules::kBitActivityLine)) {
            // an activity this read cannot see goes out as no record (the joiner keeps what it has): zeros are real ids, not "none"
            if (wo141::host_activity_wire(eid, rx[nx] + 1)) { rx[nx][0] = 0xA1; rx[nx][31] = 0xFF; ++nx; }
            uint8_t l[16]{}, r[16]{};
            if (wo141::host_hands(eid, l, r)) { rx[nx][0] = 0xA2; std::memcpy(rx[nx] + 1, l, 16); std::memcpy(rx[nx] + 17, r, 16); ++nx; }
            for (int q = 0; q < nx; ++q) for (int b = 0; b < kWireScope; ++b) sig = sig * 1099511628211ULL ^ rx[q][b];
        }
        HostBody& hb = g_hostBodies[eid];
        if (sig == hb.sig && now - hb.sentAt < kResendS) continue;
        const size_t need = 1 + nl + 2 + static_cast<size_t>(nsc + nx) * kWireScope;
        if (o + need > cap) break;
        out[o] = static_cast<uint8_t>(nl); std::memcpy(out + o + 1, nm, nl); o += 1 + nl;
        out[o] = static_cast<uint8_t>((cm ? wo210rules::kCmCombat : 0) | (down ? wo210rules::kCmDown : 0)); out[o + 1] = static_cast<uint8_t>(nsc + nx); o += 2;
        std::memcpy(out + o, rec + 2, static_cast<size_t>(nsc) * kWireScope); o += static_cast<size_t>(nsc) * kWireScope;
        for (int q = 0; q < nx; ++q) { std::memcpy(out + o, rx[q], kWireScope); o += kWireScope; }
        hb.sig = sig; hb.sentAt = now;
        ++count;
        c_streamBodies.fetch_add(1);
    }
    out[0] = static_cast<uint8_t>(count);
    *outLen = o;
    return wo163::kROk;
}

// WO-210 op 141 (A.3/A.4, the host): a joiner asked for these bodies' state again -- their signatures are forgotten, so the next poll
// sends them; no names: every body. [n:1] n x ([nameLen:1][name])
std::atomic<uint32_t> c_forgot{0};
uint8_t op_stream_forget(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (len < 2) return wo163::kRBadRequest;
    const int n = body[1];
    if (n == 0) { c_forgot.fetch_add(static_cast<uint32_t>(g_hostBodies.size())); g_hostBodies.clear(); }
    size_t o = 2;
    for (int i = 0; i < n; ++i) {
        if (o + 1 > len) return wo163::kRBadRequest;
        const size_t nl = body[o];
        if (nl == 0 || o + 1 + nl > len) return wo163::kRBadRequest;
        const std::string want(reinterpret_cast<const char*>(body + o + 1), nl);
        o += 1 + nl;
        for (auto it = g_hostBodies.begin(); it != g_hostBodies.end(); ++it) {
            void* e = engine::entity_by_id(it->first);
            const char* nm = e ? engine::entity_name(e) : nullptr;
            if (nm && _stricmp(nm, want.c_str()) == 0) { g_hostBodies.erase(it); c_forgot.fetch_add(1); break; }
        }
    }
    if (cap >= 1) { out[0] = static_cast<uint8_t>(n); *outLen = 1; }
    return wo163::kROk;
}

// ---- WO-216 F.4 / G: a host's combat row played by the owner (its full body held for the row) -----------------------------------
constexpr double kRowHoldS = 1.4;   // about one swing of the rows' own timings (a blow lands 0.5-1.0 s in, the recovery after it)
std::unordered_map<uint32_t, double> g_rowHoldUntil;   // main thread
std::atomic<uint32_t> c_rowPlayed{0}, c_rowRefused{0}, c_rowHeldFrames{0};
bool row_holds(uint32_t eid, double now) {
    auto it = g_rowHoldUntil.find(eid);
    if (it == g_rowHoldUntil.end()) return false;
    if (now > it->second) { g_rowHoldUntil.erase(it); return false; }
    return true;
}

// ---- A.2 the joiner: the host's states as they arrive (op 133), kept per copy for the owner --------------------------------------
// [n:1] n x ([eid:4][cm:1][nsc:1] nsc x kWireScope). Each state is applied one stream delay after it arrived (owner_delay_s()).
struct CopyState { double at = 0; int cm = 0; int n = 0; uint8_t sc[2][kWireScope]{}; };
std::unordered_map<uint32_t, std::vector<CopyState>> g_copyStates;   // main thread
std::atomic<uint32_t> c_statesIn{0}, c_statesBad{0};

uint8_t op_stream_in(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (len < 2) return wo163::kRBadRequest;
    const int n = body[1];
    size_t o = 2;
    const double now = qpc_s();
    for (int i = 0; i < n; ++i) {
        if (o + 6 > len) { c_statesBad.fetch_add(1); return wo163::kRBadRequest; }
        uint32_t eid = 0;
        std::memcpy(&eid, body + o, 4);
        CopyState s;
        s.at = now; s.cm = body[o + 4]; s.n = body[o + 5] > 2 ? 2 : body[o + 5];
        o += 6;
        if (o + static_cast<size_t>(body[o - 1]) * kWireScope > len) { c_statesBad.fetch_add(1); return wo163::kRBadRequest; }
        for (int k = 0; k < body[o - 1]; ++k) if (k < 2) std::memcpy(s.sc[k], body + o + static_cast<size_t>(k) * kWireScope, kWireScope);
        o += static_cast<size_t>(body[o - 1]) * kWireScope;
        auto& v = g_copyStates[eid];
        v.push_back(s);
        if (v.size() > 64) v.erase(v.begin(), v.begin() + static_cast<std::ptrdiff_t>(v.size() - 64));
        c_statesIn.fetch_add(1);
    }
    if (cap >= 1) { out[0] = static_cast<uint8_t>(n); *outLen = 1; }
    return wo163::kROk;
}

// ---- A.2 the owner: every copy the host's stream drives (mp_copy_anim_owner) ----------------------------------------------------
// One owner per copy: the only code that queues, steps or withdraws an action on the copy's full and upper body while it owns it -- the
// old writers ask owned() and leave it alone. Each copy is driven by the host NPC's newest state at least one stream delay old (so its
// actions start where its streamed position is), by wo208_rules.h decide(): the base families (idle, locomotion, combat footwork) are
// the copy's own engine's from its position and its held combat mode; every other action is the host's fragment with its tags at its
// priority, installed once per host action, stepped as the host steps it, withdrawn when the host's scope is back at its base, removed
// when it has not started in 300 ms; and a full body empty for 0.3 s gets the copy's own base again. The bench's A.1 (one copy:
// KCDMP_TEST_W208_OWNER_ONLY=<name>) is the same code on one copy.
struct HostScope { uint8_t cls = kClsNone; uint32_t frag = 0; int32_t prio = 0; uint32_t status = 0, flags = 0; uint8_t tags[20]{}; uint64_t act = 0; };
struct OwnCopy {
    std::string name;
    Probe::Scope own[2];
    std::vector<Probe::Retired> retired;
    double fullEmptySince = -1, lastPulse = -1, lastState = 0;
    bool held = false;
    void* baseAct = nullptr;    // rule 1: the host's base fragment, ours, while the copy's own base is missing
    uint32_t baseFrag = 0;
    uint64_t aiHostAct = 0;     // WO-209 B: the host action last started as the copy's own combat attack (the engine runs it to its end)
    // WO-210 A: the host NPC's current FullBody (the state the owner applied last), the copy's last exit path (A.2) and the watchdog's
    // breach stretch (A.5)
    HostScope host{}; bool hostHave = false;
    double baseAt = -1e9; bool baseStarted = false;
    uint8_t lastPath = 0; double lastPathAt = -1e9;
    double watchAt = 0, breachSince = -1, transSince = -1; uint8_t breach = 0; uint32_t transFrag = 0xFFFFFFFFu;
    double askedAt = -1e9, reseatBackoff = 0;
    double overflowAt = -1e9;   // WO-211 C.3: the last clean reseat for an over-full queue
    double stateAt = 0;         // WO-213 E.2: when the host's newest state of this body arrived
    double lateAskAt = -1e9;    // WO-213 E.2: the last time this body was asked for again
    // WO-214 C.2: the combat action holding the full body and since when; when the host's NPC last fought (combat mode or an opponent)
    double w214At = 0, cbSince = 0, hostFightAt = 0, hostBaseSince = -1; const void* cbAct = nullptr;
    double c2Stop[wo214rules::kChurnStops] = {-1e9, -1e9, -1e9}, c2RestUntil = 0;   // WO-214 C.2: the last stops here, a rest after churn
    // WO-214 C.1's instrument: a stretch of an empty full body within 60 m of the local player (the probe saw 6 s of a 3-minute one)
    double emptySince = -1, emptyLogAt = -1e9; float emptyDist = 0;
    // WO-215 C: the host activity this copy's installs have not shown (same host fragment), how often, since when; refused until
    uint32_t c215Frag = 0xFFFFFFFFu; int c215Tries = 0; double c215At = -1e9, c215RefusedUntil = 0; bool c215Missing = false;
    double hostEmptySince = -1, dormantAskAt = -1e9;   // WO-215 D.1: the host NPC's whole body empty since
    wo217::PlaceEp ep217;       // WO-217 A.0: the attempt the host NPC's posture opened on this copy
    bool seatHost = false, seatCopy = false; double seatAt = -1e9;   // WO-217 0.2: the watchdog's newest seat word
    double unclogAt = -1e9;     // WO-217 A.1 #2: the last time its own stale actions were stopped
    double takeoverAt = -1e9;   // WO-218 C.3: the last time its own action was stopped off a placed activity
    uint32_t takeoverFrag = 0xFFFFFFFFu; int takeoverN = 0; double takeoverFirst = -1e9, takeoverMuteUntil = -1e9;   // C.3's back-off
    double kubaWatchAt = 0, kubaEmptySince = -1, kubaFixAt = -1e9, kubaBackoff = 0;   // KUBA 11: the skeleton's own bind-pose watch
    uint32_t kubaFixes = 0;
    bool kubaFeet = false;   // KUBA 16
    const void* kubaTransAct = nullptr; double kubaTransSince = -1; uint64_t kubaTransBlockHost = 0;   // KUBA 13: a walk transition held too long
    const void* kubaTorsoAct = nullptr; double kubaTorsoSince = -1; const void* kubaTorsoStopped = nullptr;   // KUBA 29: a stuck arms overlay
};
std::atomic<uint32_t> c_emptyStretches{0};
std::atomic<uint32_t> c_unclogs{0}, c_unclogStopped{0};   // WO-217 A.1 #2
std::atomic<uint64_t> c_emptyMs{0};
// WO-214 C.1: an empty stretch ends (the body has an action again, the owner let it go, it died or walked out of 60 m) -- told when over 0.3 s
void empty_close(OwnCopy& oc, double now, const char* why) {
    if (oc.emptySince < 0) return;
    const double s = now - oc.emptySince;
    oc.emptySince = -1;
    if (s <= 0.3) return;
    const uint32_t k = c_emptyStretches.fetch_add(1) + 1;
    c_emptyMs.fetch_add(static_cast<uint64_t>(s * 1000.0));
    if (k <= 200 || k % 50 == 0) logf("WO214-EMPTY npc=%s secs=%.1f dist_m=%.1f open=0 why=%s [n=%u]", oc.name.c_str(), s, oc.emptyDist, why, k);
}
std::unordered_set<uint32_t> g_postureHeld;   // WO-213 E.3: copies whose posture holds (the watchdog's last check), main thread
std::unordered_map<uint32_t, OwnCopy> g_owned;   // main thread
std::unordered_map<uint32_t, double> g_lastInstall;   // WO-217 0.2: the owner's last install per copy (main thread)
std::unordered_set<uint32_t> g_readyHeld;          // WO-210 D.3: copies whose combat-mode hold the host NPC's streamed state set
std::atomic<uint32_t> c_readyHolds{0}, c_readyReleases{0};
std::atomic<uint32_t> c_oInstalls{0}, c_oRefusals{0}, c_oStale{0}, c_oWithdraws{0}, c_oSteps{0}, c_oPulses{0}, c_oApplied{0}, c_oCopies{0}, c_oFar{0};
std::string g_ownerOnly;
double g_ownerDelayS = 0.38;
bool g_ownerEnvRead = false;

// ---- the animation's phase (after 0.63.9) ---------------------------------------------------------------------------------------
// A copy's POSITION is rendered one interpolation delay behind the host (npc_drive: 120 ms at the bind, plus the jitter allowance in
// use), and its animation state must be applied on the very frame the body shows that moment -- else the body slides first and the walk
// starts a quarter second later, the host's swing lands on the copy long after its blow, and every stance change arrives late (the
// field, 2026-11-14: positions 120 ms behind, animations 380 ms behind -- a 260 ms phase error on every copy, all the time).
//   g_ownerDelayS stays what it was for the bench's own feed probe (KCDMP_TEST_W208_OWNER_DELAY_MS names it; the A.1 copy has no
// stream of its own), and the live owner asks the writer for the body's own lag instead.
// KCDMP_ANIM_PHASE=off in the game's environment: the owner waits g_ownerDelayS for every copy, as 0.63.9 did.
constexpr double kBindDelayS = 0.12;   // npc_drive's own default (the bind asks for 120 ms: 1.2 x the 10 Hz emit period)
std::atomic<uint32_t> c_phaseBodies{0};
double g_phaseLast = 0;
bool anim_phase_on() {
    static int s = -1;
    if (s < 0) {
        const std::string v = env("KCDMP_ANIM_PHASE");
        s = (v == "off" || v == "OFF" || v == "Off") ? 0 : 1;
        if (!s) logf("WO208-PHASE off for this game (KCDMP_ANIM_PHASE=off): the owner waits %.0f ms for every copy, as 0.63.9", g_ownerDelayS * 1000.0);
    }
    return s == 1;
}
// How long after its arrival a state is applied on this copy: the body's own render lag, else the bench's own delay.
double owner_delay_for(uint32_t eid) {
    if (!anim_phase_on() || !g_ownerOnly.empty()) return g_ownerDelayS;   // the bench's A.1 feed copy keeps its own delay
    // the writer's own lag for a bound body; a body it does not drive (a standing NPC the host streams nothing for, or the agent's own
    // Lua writer) gets the bind's own interpolation delay, which both writers render with
    double lag = kBindDelayS;
    npcdrive::render_lag_s(eid, &lag);
    // one line at the start and when the lag really moved, at most every 10 s (the jitter allowance slews all the time)
    static double s_said = -1e9;
    const double nowS = qpc_s();
    if (c_phaseBodies.fetch_add(1) == 0 || (std::fabs(lag - g_phaseLast) > 0.05 && nowS - s_said > 10.0)) {
        s_said = nowS;
        g_phaseLast = lag;
        logf("WO208-PHASE the owner applies the host's state %.0f ms after it arrives (this body's position is rendered that far behind; was %.0f ms)",
             lag * 1000.0, g_ownerDelayS * 1000.0);
    }
    return lag;
}

const char* cls_name(uint8_t id) {
    switch (id) {
        case kClsCombatAnim: return "C_CombatAnimAction"; case kClsCombatMove: return "C_CombatAnimActionMovement"; case kClsAIMovement: return "CAnimActionAIMovement";
        case kClsMoveTransition: return "CAnimActionMovementTransition"; case kClsIdleState: return "C_AnimActionIdleState"; case kClsVariation: return "C_VariationAction";
        case kClsCallback: return "C_CallbackAction"; case kClsDialogAnim: return "C_DialogAnimAction"; case kClsHoldItem: return "C_HoldItemAction";
        case kClsPlayerMove: return "C_PlayerMovementAction"; case kClsNone: return "-"; default: return "other";
    }
}

std::atomic<uint32_t> c_oStops{0}, c_oShotHolds{0};
std::atomic<uint32_t> c_oAiHolds{0}, c_oQueueFull{0};   // WO-209 B: owner passes that left the full body to the copy's own attack

// ---- WO-210 A: the hand-over (research design: the one rule) ----------------------------------------------------------------------
// The body's neutral idle when the host's state is not known yet: the host's own standing idle as it streamed it for a body of the
// same table (its flags), on this copy's own tags; one template per table, found by the game's name of the fragment (MotionIdle is 5
// in the male table, 0 in the female one).
struct IdleTpl { bool have = false; HostScope s{}; };
IdleTpl g_idleTpl[2];
int idle_table(uint32_t eid) {
    uint32_t id = 0;
    if (!wo187::fragment_id(eid, "MotionIdle", &id)) return -1;
    return id == 5 ? 0 : id == 0 ? 1 : -1;
}
void learn_idle(uint32_t eid, const HostScope& h) {
    if (h.cls != kClsAIMovement && h.cls != kClsIdleState) return;
    char nm[64];
    if (!wo187::fragment_name(eid, h.frag, nm, sizeof nm) || std::strcmp(nm, "MotionIdle") != 0) return;
    const int t = idle_table(eid);
    if (t >= 0 && !g_idleTpl[t].have) { g_idleTpl[t].have = true; g_idleTpl[t].s = h; logf("WO210-STATE neutral idle learned for table %d: class=%u frag=%u flags=0x%X", t, h.cls, h.frag, h.flags); }
}
bool neutral_idle(uint32_t eid, HostScope* out) {
    const int t = idle_table(eid);
    if (t < 0 || !g_idleTpl[t].have) return false;
    *out = g_idleTpl[t].s;
    uint8_t tg[20]{};
    if (wo187::tag_state(eid, tg, sizeof tg)) std::memcpy(out->tags, tg, sizeof tg);   // this copy's own tags
    return true;
}
std::string frag_name_of(uint32_t eid, uint32_t frag) {
    char nm[96];
    return wo187::fragment_name(eid, frag, nm, sizeof nm) ? std::string(nm) : std::string();
}
// WO-215 A: a routed copy's walk start and stop are its own movement system's (the engine's request plays IdleToMove / MoveToIdle itself):
// for such a copy the host's walk transitions are locomotion -- the copy's base -- never an install of ours (E.1's rule is for written copies)
Fam fam_of(uint32_t eid, uint8_t cls, uint32_t frag) {
    if (cls == kClsMoveTransition && walk_transition_frag(frag) && (wo215::walk_own(eid) || bodydrive::driven(eid))) return Fam::Loco;   // WO-221 C
    return family_of_id(cls, frag);
}
wo210rules::PFam pfam_of(uint32_t eid, uint8_t cls, uint32_t frag) {
    return wo210rules::family(cls, frag_name_of(eid, frag).c_str(), frag);
}
// The asks the agent drains (op 140): a copy whose own engine posture the hand-over could not lay over -- released the game's way.
struct Ask { uint8_t kind; std::string name; };
std::vector<Ask> g_asks;   // main thread
std::unordered_set<uint32_t> g_kubaDuelEids;   // KUBA 30: copies in a local duel (main thread)
std::unordered_set<uint32_t> g_kubaRemoteDuel;   // KUBA 30 stage 2 (host): its NPCs a partner's local duel drives (main thread)
std::atomic<uint32_t> c_asksOut{0};
// ---- the late stop (after 0.63.9) ------------------------------------------------------------------------------------------------
// An action of ours withdrawn by its priority (0), or a base of ours handed over and left running (1), keeps its Interruptable flag:
// when another action takes its scope Mannequin puts it back into the body's queue "to resume when possible" -- and at priority 0 or 1,
// under the copy's own base, it never does. The owner dropped its reference after 5 s and forgot it; the action stayed in the queue
// for good. Field (the joiner's animation probe): one more every 30-60 s on a tavern copy (a queue of 1 grew to 7 in 199 s; villagers
// left 25 minutes before still carried four), until the owner's own cap (6 queued: nothing more installed, the copy just stands) and
// then the engine's limit (10: "attempting to queue too many actions") were reached.
// Now every action of ours the owner lets go of unfinished is followed here, one reference kept, until it finishes; one found pending
// after it ran (or pending for 10 s) is ended the game's own way (IAction::Stop: the controller takes it out of the queue).
// KCDMP_LATE_STOP=off in the game's environment: as 0.63.9.
struct LateAct { uint32_t eid; void* act; double at; bool seen; double stoppedAt; };
std::vector<LateAct> g_late;   // main thread
std::atomic<uint32_t> c_lateTaken{0}, c_lateStopped{0}, c_lateFinished{0}, c_lateGone{0}, c_lateExpired{0}, c_lateNoStop{0};
bool late_on() {
    static int s = -1;
    if (s < 0) {
        const std::string v = env("KCDMP_LATE_STOP");
        s = (v == "off" || v == "OFF" || v == "Off") ? 0 : 1;
        if (!s) logf("WO208-LATE off for this game (KCDMP_LATE_STOP=off): a withdrawn action of ours is forgotten after 5 s as in 0.63.9");
    }
    return s == 1;
}
// Takes over the owner's one reference of `act` (the entity is alive: the caller checked).
void late_take(uint32_t eid, void* act) {
    if (!act) return;
    uint32_t st = 0;
    const bool readable = rd(act, 0x28, &st);
    if (!late_on() || !readable || st >= 4 || g_late.size() >= 4096) { rttr::release_action(act); return; }
    g_late.push_back({eid, act, qpc_s(), st == 2 || st == 3, -1.0});
    c_lateTaken.fetch_add(1);
}
void late_tick(double now) {
    static double s_at = 0, s_log = 0;
    if (now - s_at < 0.25) return;
    s_at = now;
    for (size_t i = 0; i < g_late.size();) {
        LateAct& e = g_late[i];
        bool drop = false, release = true;
        uint32_t st = 0;
        void* ent = engine::entity_by_id(e.eid);
        if (!ent) { drop = true; release = false; c_lateGone.fetch_add(1); }   // a body gone may have taken its controller: our reference is left (WO-202's rule)
        else if (!rd(e.act, 0x28, &st) || st >= 4) { drop = true; c_lateFinished.fetch_add(1); }
        else if (e.stoppedAt >= 0) { if (now - e.stoppedAt > 5.0) drop = true; }   // stopped: the controller ends it; our reference goes as the owner's does
        else if (st == 2 || st == 3) { e.seen = true; if (now - e.at > 900.0) { drop = true; c_lateExpired.fetch_add(1); } }
        else if (e.seen || now - e.at > 10.0) {
            // pending: it ran and the engine put it back, or it never started -- nothing of ours waits in a queue we no longer drive
            if (rttr::stop_action(e.act)) {
                e.stoppedAt = now;
                const uint32_t k = c_lateStopped.fetch_add(1) + 1;
                if (k <= 60 || k % 100 == 0) {
                    const char* nm = engine::entity_name(ent);
                    logf("WO208-LATE npc=%s an action of ours was back in its queue %.1f s after the owner let it go (%s): stopped the game's own way [n=%u]",
                         nm ? nm : "?", now - e.at, e.seen ? "it had run: the engine requeued it" : "it never started", k);
                }
            } else { drop = true; c_lateNoStop.fetch_add(1); }
        }
        if (drop) { if (release) rttr::release_action(e.act); g_late.erase(g_late.begin() + static_cast<std::ptrdiff_t>(i)); }
        else ++i;
    }
    if (now - s_log > 60.0 && c_lateTaken.load() > 0) {
        s_log = now;
        logf("WO208-LATE status taken=%u stopped=%u finished=%u body_gone=%u expired=%u no_stop=%u watching=%zu", c_lateTaken.load(), c_lateStopped.load(),
             c_lateFinished.load(), c_lateGone.load(), c_lateExpired.load(), c_lateNoStop.load(), g_late.size());
    }
}

// The moonwalk reseat (after 0.63.9, wo210_rules.h Verdict::Sliding): KCDMP_SLIDE_RESEAT=off in the game's environment leaves the
// watchdog as 0.63.9 had it (a dragged body with a gesture or a work animation on its full body is not a breach).
bool slide_on() {
    static int s = -1;
    if (s < 0) {
        const std::string v = env("KCDMP_SLIDE_RESEAT");
        s = (v == "off" || v == "OFF" || v == "Off") ? 0 : 1;
        if (!s) logf("WO210-STATE the moonwalk reseat is OFF for this game (KCDMP_SLIDE_RESEAT=off): as 0.63.9");
    }
    return s == 1;
}

// KUBA 10 (joiner 2026-11-15 15:51-15:58: 158 reseats, ~120 of them the same few copies every 1-2 s, every one "installed").
// a) KCDMP_KUBA_FIDGET_OK=off: a copy's work fidget is a missing posture again (wo210_rules.h fidget_of_work).
// b) KCDMP_KUBA_RESEAT_REINSTALL=off: the hand-over takes "ours already plays it" from the host action's id alone again -- an action of
//    ours for that very host action that finished, or that another action pushed off the full body, made every reseat a no-op that
//    still said "the host's state installed"; now it is withdrawn and the host's state installed anew.
std::atomic<uint32_t> c_kubaFidgetOk{0}, c_kubaReinstall{0};
bool kuba_switch(const char* name, int* cache, const char* what) {
    if (*cache < 0) {
        const std::string v = env(name);
        *cache = (v == "off" || v == "OFF" || v == "Off") ? 0 : 1;
        logf("KUBA-10 %s %s (%s=off turns it off)", what, *cache ? "on" : "OFF", name);
    }
    return *cache == 1;
}
bool kuba_fidget_on() { static int s = -1; return kuba_switch("KCDMP_KUBA_FIDGET_OK", &s, "a copy's work fidget counts as its work"); }
// KUBA 11 (KCDMP_KUBA_TPOSE_FIX=off: watch and log only, never repair): a copy whose skeleton plays nothing on layer 0 shows the bind
// pose (the player's "T-pose") while its Mannequin FullBody scope still names an action -- every detector of the mod reads the scope,
// so none saw the field's T-poses (Kuba, 2026-11-15: 5+ marks a session, detectors 0). A clip stopped under a running action
// (the Lua's ent:StopAnimation(0, 0) when a puppet is bound, WO121-GAIT "parked") leaves exactly that. Repair: the action ours withdrawn,
// the host's state installed anew (the hand-over), the locomotion nudged when nothing could be installed.
std::atomic<uint32_t> c_kubaTposeSeen{0}, c_kubaTposeFixed{0}, c_kubaTposeBack{0}, c_kubaTposeUnread{0};
bool kuba_tpose_fix_on() { static int s = -1; return kuba_switch("KCDMP_KUBA_TPOSE_FIX", &s, "a copy in the bind pose (no animation on its skeleton) is repaired"); }
// KUBA 12 (joiner 17:04-17:16 probe: Lying 16281/16294, SittingIdle 12897/12928, EatingMash, CampRazorSharpening, Sunbathing ... installed
// by the owner as a combat action play NOTHING on the skeleton; the same postures as the engine's own idle state play) -- a fragment of
// ours seen in the bind pose is learned and never installed by the owner again: the copy's own engine (its seat, WO-160) or its base shows.
std::unordered_set<uint32_t> g_kubaDeadFrags;   // main thread
std::unordered_map<uint32_t, int> g_kubaEmptyHits;   // main thread: bind-pose sightings per fragment of ours (learned at 3: one was a fluke -- 19:23-19:53 learned Leaning_Back_Loop, 0 of 990 empty before)
std::atomic<uint32_t> c_kubaLearned{0}, c_kubaSkipped{0};
bool kuba_learn_on() { static int s = -1; return kuba_switch("KCDMP_KUBA_LEARN_EMPTY", &s, "a fragment of ours that plays nothing is never installed again"); }
// KUBA 13 (joiner 17:04-17:16 probe: 23 % of the moving copies' samples ran IdleToMove; its stretches median 1-2.6 s, p90 5-12 s, max
// 26 s, ~730 m dragged in the start-to-walk pose -- the moonwalk): a walk transition (IdleToMove / MoveToIdle) on the full body past
// kKubaTransMaxS while the body is dragged at walking speed is stopped, and the walk takes over (the host's base, the locomotion nudge).
// KCDMP_KUBA_TRANS_CUT=off: as before.
constexpr double kKubaTransMaxS = 0.9;
std::atomic<uint32_t> c_kubaTransCut{0};
bool kuba_trans_on() { static int s = -1; return kuba_switch("KCDMP_KUBA_TRANS_CUT", &s, "a walk transition held past 0.9 s on a dragged body is cut"); }
std::atomic<uint32_t> c_kubaSeatAsks{0};
// KUBA 18 is OFF by default since 16.11 (KCDMP_KUBA_SEAT_ASK=on turns it on): with it, no copy sat in two sessions (10:57, 11:20) --
// each release met WO-211's loop guard ("re-seated N ms after its release", "a placement into the seat ... held back") and the seat never
// came; without it (15.11 17:06, 19:23) the copies sat the engine's way (C_CallbackAction SitDown -> C_AnimActionIdleState SittingIdle).
bool kuba_seat_ask_on() {
    static int s = -1;
    if (s < 0) {
        const std::string v = env("KCDMP_KUBA_SEAT_ASK");
        s = (v == "on" || v == "ON" || v == "On" || v == "1") ? 1 : 0;
        logf("KUBA-10 a standing copy of a seated host NPC is re-seated the game's way (KUBA 18) %s (KCDMP_KUBA_SEAT_ASK=on turns it on)", s ? "on" : "OFF");
    }
    return s == 1;
}
// KUBA 22: a copy the ENGINE seated (or laid down) keeps its own posture loop -- the host's next posture loop of the same family (another
// seated idle, the seated dialog pose) is not installed over it. 15.11 19:26: tzel_woman_9 seated by its engine, the host's IdleState
// frag 10 installed over it and never started (30 stale reinstalls, 300 ms apart), then the T-pose and the standing copy; tzel_michal and
// tzel_david: IngameDialogPose_SittingWithOrWithoutTable_Loop installed over their engine seat -> T-pose -> standing. One-shots (beer,
// guest calls: C_CallbackAction) and dialog gestures still play; the host standing up still hands the copy over. KCDMP_KUBA_SEAT_KEEP=off.
bool kuba_seat_keep_on() { static int s = -1; return kuba_switch("KCDMP_KUBA_SEAT_KEEP", &s, "a copy seated by its own engine keeps its seat loop (KUBA 22)"); }
std::atomic<uint32_t> c_kubaSeatKeep{0};
bool kuba_walk_feet_on() { static int s = -1; return kuba_switch("KCDMP_KUBA_WALK_FEET", &s, "an ordinary walking copy's feet get its rendered speed and direction (KUBA 17)"); }
bool kuba_dead_frag(uint32_t frag) {
    if (!kuba_learn_on() || g_kubaDeadFrags.empty() || !g_kubaDeadFrags.count(frag)) return false;
    c_kubaSkipped.fetch_add(1);
    return true;
}
bool kuba_reinstall_on() { static int s = -1; return kuba_switch("KCDMP_KUBA_RESEAT_REINSTALL", &s, "a reseat re-installs ours when it is off the body"); }

void own_retire(OwnCopy& oc, Probe::Scope& s, double now, const char* why);
bool is_ours(const OwnCopy& oc, const void* act) {
    if (!act) return false;
    if (act == oc.baseAct || act == oc.own[0].act || act == oc.own[1].act) return true;
    for (const auto& r : oc.retired) if (r.act == act) return true;
    return false;
}
// WO-215 C/B: the ladder's base on a copy whose host activity cannot be shown here: the copy's own neutral idle (learned from its host's
// standing idles), else the engine's own idle through a locomotion pulse (its AI movement action's MotionIdle) -- never an empty body.
std::atomic<uint32_t> c_w215Bases{0}, c_w215Refused{0}, c_w215RefusedRest{0}, c_w215Dormant{0}, c_w215StaleOurs{0}, c_w215NpcState{0};
bool w215_give_base(uint32_t eid, OwnCopy& oc, double now) {
    HostScope b{};
    if (neutral_idle(eid, &b)) {
        if (oc.baseAct && oc.baseFrag == b.frag) return true;
        void* ba = nullptr;
        if (rttr::install_fragment(eid, b.frag, b.tags, kBasePriority, b.flags, &ba) == rttr::SwingResult::Ok && ba) {
            if (oc.baseAct) { rttr::stop_action(oc.baseAct); oc.retired.push_back({oc.baseAct, now}); }
            oc.baseAct = ba; oc.baseFrag = b.frag; oc.baseAt = now; oc.baseStarted = false;
            c_w215Bases.fetch_add(1, std::memory_order_relaxed);
            return true;
        }
    }
    bool c = false;
    motion::combat_mode_of(eid, &c);
    const bool given = (c && motion::requeue_guard_idle(eid)) || motion::request_idle_pulse(eid);
    if (given) c_w215Bases.fetch_add(1, std::memory_order_relaxed);
    return given;
}

// ---- the queue cap (after 0.63.9, wo209_rules.h) ---------------------------------------------------------------------------------
// KCDMP_QUEUE_CAP=strict in the game's environment: 0.63.9's own cap (any six queued actions, ours or the copy's own, stop the owner).
bool queue_cap_strict() {
    static int s = -1;
    if (s < 0) {
        const std::string v = env("KCDMP_QUEUE_CAP");
        s = (v == "strict" || v == "STRICT" || v == "Strict") ? 1 : 0;
        if (s) logf("WO208-QUEUE the strict queue cap is on for this game (KCDMP_QUEUE_CAP=strict): as 0.63.9");
    }
    return s == 1;
}
int ours_queued(const OwnCopy& oc, const wo187::QueueInfo& qi) {
    int n = 0;
    for (int i = 0; i < qi.nacts; ++i) if (is_ours(oc, qi.acts[i])) ++n;
    return n;
}
// Is there a reason to withdraw our own pending action before installing anything?
bool queue_cap_crowded(const OwnCopy& oc, const wo187::QueueInfo& qi) {
    return queue_cap_strict() ? wo209rules::owner_queue_full(qi.n) : wo209rules::owner_queue_crowded(qi.n, ours_queued(oc, qi));
}
// After that withdraw: is the queue still so long that the engine itself would refuse?
bool queue_cap_refuses(int queued) {
    return queue_cap_strict() ? wo209rules::owner_queue_full(queued) : wo209rules::owner_queue_refuses(queued);
}
std::atomic<uint32_t> c_capRefused{0}, c_capPassed{0};

// The host's current FullBody state installed the owner's way, in the same frame as the release of what it replaces: a base family as
// our base action under the copy's own base (the copy's own base, when it comes, replaces it); any other family at the host's priority
// as our scope action. Unknown host state: the body's neutral idle. Never "withdraw and leave it", never "stop and leave it empty".
bool own_handover(uint32_t eid, OwnCopy& oc, double now, uint8_t path) {
    oc.lastPath = path; oc.lastPathAt = now;
    if (!wo210::on(wo210rules::kBitStateAlways)) return false;
    if (wo172::copy_dead(eid)) return false;   // C.2: a dead copy is never driven again
    // a host scope with nothing in it (d6-waitnight: a wanderer whose host NPC's FullBody was empty, 24 reseats of "the host's state" --
    // fragment 0 with the empty scope's tags, nothing that plays) is no state to hand over: the copy's own neutral idle, never empty
    const HostScope* hs = oc.hostHave && (oc.host.cls != kClsNone || !wo210::fix_on(wo210rules::kFixEmptyHostNeutral)) ? &oc.host : nullptr;
    const Fam fam = hs ? fam_of(eid, hs->cls, hs->frag) : Fam::Empty;
    bool installed = false, neutral = false;
    // WO-211 C.2: a hand-over never queues behind a long queue (the field's seated copy: nine of our actions waiting behind its own look
    // actions, 3,712 overflows) -- ours still pending are withdrawn first (one pending action of ours per copy); still long: nothing new
    if (wo211::on(wo211rules::kBitHandoverQueueCap) && queue_cap_crowded(oc, wo187::queue_info(eid))) {
        for (auto& sc : oc.own) {
            uint32_t st = 0;
            if (sc.act && rd(sc.act, 0x28, &st) && st < 2) own_retire(oc, sc, now, "a long queue: one pending action of ours at a time");
        }
        if (queue_cap_refuses(wo187::queue_info(eid).n)) {
            const uint32_t k = c_capRefused.fetch_add(1) + 1;
            if (k <= 20 || k % 200 == 0)
                logf("WO208-QUEUE npc=%s the hand-over installs nothing: %d actions queued, the engine refuses past %d [n=%u]", oc.name.c_str(),
                     wo187::queue_info(eid).n, wo209rules::kEngineQueueLimit, k);
            wo211::note_handover_queue_full();
            wo210::note_handover(path, false, false);
            return false;
        }
        c_capPassed.fetch_add(1);
    }
    if (hs && !is_base(fam) && fam != Fam::Hold && !kuba_dead_frag(hs->frag)) {
        bool oursOn = oc.own[0].act && oc.own[0].hostAct == hs->act;
        if (oursOn && kuba_reinstall_on()) {
            // KUBA 10b: "ours plays it" only while ours holds the full body, or still waits its turn (decide()'s stale rule has that one)
            uint32_t st = 0;
            const bool readable = rd(oc.own[0].act, 0x28, &st);
            wo187::ScopeInfo cur[16];
            const int ncur = wo187::read_all_scopes(eid, cur, 16);
            const bool onFull = ncur > 0 && cur[0].ok && !cur[0].empty && cur[0].act == oc.own[0].act;
            if (!onFull && !(readable && st < 2)) {
                own_retire(oc, oc.own[0], now, "the reseat: ours is no longer on the full body");
                oursOn = false;
                const uint32_t k = c_kubaReinstall.fetch_add(1) + 1;
                if (k <= 40 || k % 100 == 0)
                    logf("KUBA-10 reinstall npc=%s frag=%u status=%u%s -- the host's state installed anew (#%u)", oc.name.c_str(), hs->frag, st,
                         readable ? "" : "(unreadable)", k);
            }
        }
        if (oursOn) installed = true;   // ours already plays it
        else {
            void* act = nullptr;
            const rttr::SwingResult r = rttr::install_fragment(eid, hs->frag, hs->tags, hs->prio, hs->flags, &act);
            if (r == rttr::SwingResult::Ok && act) {
                if (oc.own[0].act) own_retire(oc, oc.own[0], now, "the hand-over");
                oc.own[0].act = act; oc.own[0].hostAct = hs->act; oc.own[0].frag = hs->frag; oc.own[0].prio = hs->prio; oc.own[0].queuedAt = now; oc.own[0].started = false;
                installed = true;
                wo217::place_note(oc.ep217, wo217::Note::InstallOk);
                g_lastInstall[eid] = now;
            } else if (wo214::on(wo214rules::kBitOwnerBase)) {
                // WO-214 C.1 (owner_base): the host's posture refused here (a work fragment whose tool or object this copy has not) --
                // a base under it in the same frame, the copy's own neutral idle, never an empty body (the field: tsem_man_11 asked for
                // "working" 23 times, refused every time, FullBody empty for 51 s at 53 m). The watchdog still asks for the posture.
                wo214::note_posture_refused();
                wo217::place_note(oc.ep217, wo217::Note::Refused, rttr::swing_result_name(r));
                oc.lastPath = wo210rules::kPathEmptyRefused; oc.lastPathAt = now;
                HostScope b{};
                const bool nk = neutral_idle(eid, &b);
                const wo214rules::Base kind = wo214rules::base_for(false, nk);
                bool given = false;
                if (nk && oc.baseAct && oc.baseFrag == b.frag) given = true;   // ours already plays that idle
                else if (nk) {
                    void* ba = nullptr;
                    const rttr::SwingResult rb = rttr::install_fragment(eid, b.frag, b.tags, kBasePriority, b.flags, &ba);
                    if (rb == rttr::SwingResult::Ok && ba) {
                        if (oc.baseAct) { rttr::stop_action(oc.baseAct); oc.retired.push_back({oc.baseAct, now}); }
                        oc.baseAct = ba; oc.baseFrag = b.frag; oc.baseAt = now; oc.baseStarted = false;
                        given = true;
                    }
                }
                if (!given) { bool c = false; motion::combat_mode_of(eid, &c); given = (c && motion::requeue_guard_idle(eid)) || motion::request_idle_pulse(eid); }
                if (given) { wo214::note_base_given(kind, false); neutral = true; }
                static uint32_t s_refused = 0;
                if (++s_refused <= 30 || s_refused % 100 == 0)
                    logf("WO214-BASE npc=%s the host's posture (class %s frag %u) refused here (%s) -> %s under it [n=%u]", oc.name.c_str(), cls_name(hs->cls), hs->frag,
                         rttr::swing_result_name(r), given ? (kind == wo214rules::Base::NeutralIdle ? "the copy's neutral idle" : "a locomotion nudge") : "nothing could be given", s_refused);
            }
        }
    } else {
        // a fight's base is the copy's own engine's: its combat footwork and its own attacks run from its combat director. The host's
        // footwork installed as ours (d8-fight10: 87 hand-overs at the host's base) left the copies' own attacks unstarted -- the blows on
        // the joiner judged by his game fell from 88.8 % to 44.6 % at the same camp -- so a copy in combat mode, or a host base that is
        // combat footwork, gets nothing installed: ours steps down and its own engine runs it (the twin fight_base_own off: as d8)
        bool cmNow = false;
        motion::combat_mode_of(eid, &cmNow);
        if (wo210::fix_on(wo210rules::kFixFightBaseOwn) && (fam == Fam::CombatMove || cmNow)) {
            if (oc.own[0].act) own_retire(oc, oc.own[0], now, "a fight's base is the copy's own");
            if (oc.baseAct) { rttr::stop_action(oc.baseAct); oc.retired.push_back({oc.baseAct, now}); oc.baseAct = nullptr; oc.baseFrag = 0; }
            wo210::note_handover(path, true, false);
            return true;
        }
        HostScope b{};
        bool have = false;
        if (hs && hs->cls != kClsNone && is_base(fam)) { b = *hs; have = true; }
        else if (neutral_idle(eid, &b)) { have = true; neutral = true; }
        // the copy's own base already queued (an action of its own engine at a base priority, over our withdrawn one): it takes the scope
        // at once -- nothing of ours is queued under it (a fight's every swing would add one)
        bool ownBaseQueued = false;
        {
            const wo187::QueueInfo qi = wo187::queue_info(eid);
            for (int i = 0; i < qi.nacts; ++i) if (qi.acts[i] && !is_ours(oc, qi.acts[i]) && qi.prio[i] >= 2) ownBaseQueued = true;
        }
        if (ownBaseQueued) installed = true;
        else if (have && oc.baseAct && oc.baseFrag == b.frag) installed = true;   // ours already plays that base
        else if (have) {
            void* act = nullptr;
            const rttr::SwingResult r = rttr::install_fragment(eid, b.frag, b.tags, kBasePriority, b.flags, &act);
            if (r == rttr::SwingResult::Ok && act) {
                if (oc.baseAct) { rttr::stop_action(oc.baseAct); oc.retired.push_back({oc.baseAct, now}); }
                oc.baseAct = act; oc.baseFrag = b.frag; oc.baseAt = now; oc.baseStarted = false;
                installed = true;
            }
        }
        if (!installed) {
            bool c = false;
            motion::combat_mode_of(eid, &c);
            installed = (c && motion::requeue_guard_idle(eid)) || motion::request_idle_pulse(eid);
        }
        // ours on the scope (a posture, a gesture) steps down under the base: the base replaces it now
        if (oc.own[0].act) own_retire(oc, oc.own[0], now, "the hand-over to the host's base");
    }
    wo210::note_handover(path, installed, neutral);
    return installed;
}

void own_retire(OwnCopy& oc, Probe::Scope& s, double now, const char* why) {
    if (!s.act) return;
    // rules 2 and 3: an action of ours still pending is removed (the game's own stop: never installed later); a running one steps down,
    // so the copy's own base action installs at once (WO-202's withdraw, as the engine's own)
    uint32_t st = 0;
    if (rd(s.act, 0x28, &st) && st < 2 && rttr::stop_action(s.act)) c_oStops.fetch_add(1);
    else wr_prio(s.act, kWithdrawnPriority);
    oc.retired.push_back({s.act, now});
    const uint32_t k = c_oWithdraws.fetch_add(1) + 1;
    if (k <= 40 || k % 100 == 0) logf("WO208-OWN withdraw npc=%s frag=%u why=%s (#%u)", oc.name.c_str(), s.frag, why, k);
    s = Probe::Scope{};
}

void own_apply(uint32_t eid, OwnCopy& oc, int cm, const HostScope hsc[2], const bool have[2], double now) {
    c_oApplied.fetch_add(1);
    if (!g_kubaDuelEids.empty() && g_kubaDuelEids.count(eid)) {   // KUBA 30: a local duel -- its own brain animates it, the owner waits
        oc.breachSince = -1;
        return;
    }
    if (g_ownerOnly.empty()) {
        // every copy: its combat state stays WO-132's / WO-187's (the engagement, the guard hold from the host's fights) -- not the owner's
    } else if (cm) {
        motion::State2 st{};
        st.bits = motion::kBitCombat;
        motion::set_npc_guard_hold(eid, true, &st, npcdrive::now_s());
        oc.held = true;
    } else if (oc.held) {
        motion::set_npc_guard_hold(eid, false, nullptr, npcdrive::now_s());
        oc.held = false;
    }
    wo187::ScopeInfo cs[16];
    const int cn = wo187::read_all_scopes(eid, cs, 16);
    {   // WO-217 A.0: the attempt line -- first, so this pass's install of a new host action is that attempt's
        const bool hostHave = have[0];
        const wo210rules::PFam hf = hostHave ? pfam_of(eid, hsc[0].cls, hsc[0].frag) : wo210rules::PFam::Empty;
        const bool cok = cn > 0 && cs[0].ok;
        const wo210rules::PFam cf = !cok || cs[0].empty ? wo210rules::PFam::Empty : pfam_of(eid, class_id(cs[0].action), cs[0].frag);
        wo217::place_tick(eid, oc.name.c_str(), oc.ep217, hostHave, hostHave ? hsc[0].act : 0, hostHave ? hsc[0].frag : 0, static_cast<uint8_t>(hf),
                          cok && !cs[0].empty ? cs[0].frag : 0xFFFFFFFFu, static_cast<uint8_t>(cf), cok ? cs[0].action : "-",
                          cok && !cs[0].empty && is_ours(oc, cs[0].act), now);
    }
    // WO-209 C.2 (mp_copy_oneshot_request): the host's work one-shot runs on the copy as the game's own NPC-state request (WO-143's) --
    // the owner holds the full body while it runs: nothing installed over it, no base given under it
    const bool shot = wo209::on(wo209rules::kBitShotRequest) && wo143::shot_in_flight(eid);
    if (shot) c_oShotHolds.fetch_add(1);
    // WO-209 B: the copy's own combat attack owns its full body while it runs -- the owner's state is a stream delay behind, and the host's
    // footwork just before the swing, installed over the attack, cut it after 26 ms (d9-defence: the copy started again late, its blow
    // past the host's hold)
    const bool aiRuns = wo209::ai_owns_body(eid);
    if (aiRuns) c_oAiHolds.fetch_add(1);
    // WO-216 F.4 / G: the host's row the owner played holds the full body for the row's length
    const bool rowRuns = row_holds(eid, now);
    if (rowRuns) c_rowHeldFrames.fetch_add(1, std::memory_order_relaxed);
    // WO-209 C: nothing of the owner's is queued on a copy whose action queue is already long (the engine allows 10). rc1-c2tavern-1: a
    // seated copy's own actions at the head of its queue (its movement, its look pose) never let the owner's installs start, the owner
    // withdrew each as stale and installed the next, and they piled up behind them -- 11,004 queue overflows on one copy, 59,993 on
    // seven the next night, 125,205 in the smoke. (rc2 left a seated copy to the game instead: no overflow, but the beds' sleepers stood
    // -- the owner's own install is what lays them down: lying matched 0 % of 2,716 samples, rc1 ~100 %.)
    const wo187::QueueInfo qi = wo187::queue_info(eid);
    // the cap, after 0.63.9 (wo209_rules.h): the copy's own queued actions are no reason to install nothing -- only the engine's own limit is
    const bool queueFull = wo209::fix_on(wo209rules::kFixOwnerQueueCap) && queue_cap_refuses(qi.n);
    // KUBA 29 (joiner, 17.11 14:17-14:25: "it often gets stuck in a fighting animation of the arms" -- the TorsoDetail overlay held a
    // one-shot for 42 s (CombatAttackFailedPB, SpawnedEnemy_002) and 75 s (WeaponHolsterFull, SpawnedEnemy_001)): a TorsoDetail action
    // that is no loop and holds over 3 s is stopped the game's own way (once per action). KCDMP_KUBA_TORSO_UNSTICK=off.
    {
        static int s_torso = -1;
        if (kuba_switch("KCDMP_KUBA_TORSO_UNSTICK", &s_torso, "a stuck arms overlay (TorsoDetail one-shot over 3 s) is stopped (KUBA 29)")) {
            const void* tAct = nullptr; uint32_t tFrag = 0;
            for (int i = 0; i < cn; ++i)
                if (cs[i].ok && !cs[i].empty && !std::strcmp(cs[i].name, "TorsoDetail")) { tAct = cs[i].act; tFrag = cs[i].frag; break; }
            if (tAct != oc.kubaTorsoAct) { oc.kubaTorsoAct = tAct; oc.kubaTorsoSince = now; }
            else if (tAct && tAct != oc.kubaTorsoStopped && now - oc.kubaTorsoSince > 3.0) {
                const std::string tn = frag_name_of(eid, tFrag);
                const bool loop = tn.find("Loop") != std::string::npos || tn.find("Idle") != std::string::npos || tn.find("VAR") != std::string::npos;
                // 29b (17.11 14:39-14:45: 12 of its 13 stops were WeaponHolsterFull -- the weapon's own holster state, not a stuck move;
                // "no better, a bit worse"): only a combat move (Combat..., no guard pose) is stopped
                const bool combatMove = tn.rfind("Combat", 0) == 0 && tn.find("Guard") == std::string::npos;
                if (!loop && combatMove) {
                    oc.kubaTorsoStopped = tAct;
                    const bool ok = rttr::stop_action(const_cast<void*>(tAct));
                    static uint32_t s_n = 0;
                    if (++s_n <= 60 || s_n % 100 == 0)
                        logf("KUBA-29 npc=%s TorsoDetail %s held %.1f s -> stopped %s [n=%u]", oc.name.c_str(), tn.c_str(), now - oc.kubaTorsoSince,
                             ok ? "the game's way" : "-- the stop is not in this build", s_n);
                }
            }
        }
    }
    if (queueFull) c_oQueueFull.fetch_add(1);
    // KUBA 27 (joiner, 17.11 13:44-13:56: 18 of the host's 23 blocks reached the copy, but up to 2 s late -- each late one while the copy
    // ran its own AI attack at the local player (WO-209), during which the owner installs nothing on the full body): the host NPC's block
    // (CombatBlock...) goes on the copy at once, over its own attack (one priority above it). KCDMP_KUBA_BLOCK_FIRST=off.
    static int s_blockFirst = -1;
    const bool blockFirstOn = kuba_switch("KCDMP_KUBA_BLOCK_FIRST", &s_blockFirst, "the host NPC's block goes on its copy over the copy's own attack (KUBA 27)");
    bool kubaBlockFirst = false;
    if (blockFirstOn && (aiRuns || shot) && have[0]) {
        const std::string bn = frag_name_of(eid, hsc[0].frag);
        kubaBlockFirst = bn.rfind("CombatBlock", 0) == 0;
    }
    for (int k = 0; k < 2; ++k) {
        if ((shot || aiRuns || rowRuns) && k == 0 && !kubaBlockFirst) { wo217::place_note(oc.ep217, wo217::Note::Held, shot ? "one-shot" : aiRuns ? "its-own-attack" : "row"); continue; }
        const HostScope* hs = have[k] ? &hsc[k] : nullptr;
        const Fam fam = hs ? fam_of(eid, hs->cls, hs->frag) : Fam::Empty;
        Probe::Scope& o = oc.own[k];
        Own view;
        if (o.act) {
            view.have = true; view.hostAct = o.hostAct; view.ageS = now - o.queuedAt;
            rd(o.act, 0x24, &view.prio);
            if (k == 0 && kubaBlockFirst && hs && view.prio == hs->prio + 1) view.prio = hs->prio;   // KUBA 27: our +1 is not a step to undo
            rd(o.act, 0x28, &view.status);
            if (view.status == 2 || view.status == 3) o.started = true;
            if (o.started && view.status < 2) view.status = 2;
        }
        bool copyRuns = false;
        for (int i = 0; i < cn && hs; ++i)
            if (cs[i].ok && !cs[i].empty && !std::strcmp(cs[i].name, kOwnScopes[k]) && cs[i].frag == hs->frag && cs[i].act != o.act) copyRuns = true;
        // WO-209 C.1 (mp_copy_paired_engine): a paired action the host's NPC leads against the joiner's figure -- the engine's own way here,
        // with the local player as the partner, once per host action (decide() holds every paired fragment otherwise)
        if (k == 0 && hs && fam == Fam::Hold && hs->cls == kClsCombatAnim && oc.aiHostAct != hs->act && !copyRuns && wo209::paired_wants(eid, hs->frag)) {
            oc.aiHostAct = hs->act;
            if (o.act) own_retire(oc, o, now, "the host's paired action");
            wo209::paired_engine(eid, hs->frag);
            continue;
        }
        switch (decide(fam, hs ? hs->act : 0, hs ? hs->prio : 0, copyRuns, view)) {
            case Do::Nothing: break;
            case Do::Withdraw:
                // WO-210 A (path 15): the host's scope is back at its base -- the host's base is installed in the same frame, under the
                // copy's own base (session E: a withdrawn posture kept looping on a paused copy whose own base never came back)
                if (k == 0 && !copyRuns && wo210::on(wo210rules::kBitStateAlways)) own_handover(eid, oc, now, wo210rules::kPathBase);
                else own_retire(oc, o, now, copyRuns ? "the copy runs it itself" : "the host's scope is back at its base");
                break;
            case Do::Stale: {
                const uint32_t n = c_oStale.fetch_add(1) + 1;
                if (n <= 40 || n % 100 == 0) logf("WO208-OWN stale npc=%s frag=%u not started in %.0f ms -- removed (#%u)", oc.name.c_str(), o.frag, view.ageS * 1000.0, n);
                own_retire(oc, o, now, "stale");
                if (k == 0) wo217::place_note(oc.ep217, wo217::Note::Stale, qi.n > 0 ? qi.first : "an-empty-queue");
                if (k == 0) { oc.lastPath = wo210rules::kPathStale; oc.lastPathAt = now; }   // A.2 path 3: the watchdog charges a breach to it
                break;
            }
            case Do::StepPriority:
                if (wr_prio(o.act, hs->prio)) { c_oSteps.fetch_add(1); o.prio = hs->prio; }
                break;
            case Do::Install: {
                // WO-209 B (mp_copy_attack_ai): the host's NPC attacks the joiner's figure there -- the copy's own combat attack at the local
                // player here (the engine's AI attack: his engine resolves the blow), once per host action; refused -> the fragment as before
                // (started on the state's arrival, owner_tick's early path -- or refused there: the delayed state never asks again)
                if (k == 0 && wo209::attack_ai_wants(eid, hs->cls, hs->frag)) break;
                // WO-217 A.1 #2 (placement): a queue held full by the copy's own never-started look / movement actions is drained the game's
                // way first (IAction::Stop on those only; ours and running ones untouched), at most once a second; the install follows
                if (queueFull && k == 0 && now - oc.unclogAt >= wo217rules::kUnclogEveryS && wo217::on(wo217rules::kBitPlacement)) {
                    oc.unclogAt = now;
                    int stopped = 0; std::string names;
                    for (int i = 0; i < qi.nacts; ++i) {
                        void* a = const_cast<void*>(qi.acts[i]);
                        if (!a) continue;
                        uint32_t st = 0;
                        if (!rd(a, 0x28, &st)) continue;
                        const std::string cls = wo187::action_class(a);
                        if (!wo217rules::unclog(true, true, is_ours(oc, a), st, cls.c_str())) continue;
                        if (rttr::stop_action(a)) { ++stopped; if (names.size() < 80) { if (!names.empty()) names += ","; names += cls; } }
                    }
                    if (stopped) {
                        const uint32_t kk = c_unclogs.fetch_add(1, std::memory_order_relaxed) + 1;
                        c_unclogStopped.fetch_add(static_cast<uint32_t>(stopped), std::memory_order_relaxed);
                        if (kk <= 200 || kk % 50 == 0)
                            logf("WO217-UNCLOG npc=%s stopped=%d (%s) queue=%d -- the copy's own never-started actions held its queue full; the host's activity next [n=%u]",
                                 oc.name.c_str(), stopped, names.c_str(), qi.n, kk);
                    }
                }
                if (queueFull) { if (k == 0) wo217::place_note(oc.ep217, wo217::Note::QueueFull, qi.first); break; }   // the host's action waits for room (the queue cap above)
                if (k == 0 && kuba_dead_frag(hs->frag)) break;   // KUBA 12: plays nothing on a copy
                if (k == 0 && oc.kubaTransBlockHost && hs->act == oc.kubaTransBlockHost) break;   // KUBA 13: the transition just cut
                // KUBA 22: the engine's own seat (or bed) loop stays under the host's next loop of the same posture
                if (k == 0 && kuba_seat_keep_on() && hs->cls != kClsCallback && hs->cls != kClsDialogAnim && cn > 0 && cs[0].ok && !cs[0].empty &&
                    !is_ours(oc, cs[0].act)) {
                    const uint8_t ccls = class_id(cs[0].action);
                    if (ccls == kClsIdleState || ccls == kClsVariation || ccls == kClsCallback) {
                        using wo210rules::PFam;
                        const PFam cf = pfam_of(eid, ccls, cs[0].frag);
                        const PFam hf = pfam_of(eid, hs->cls, hs->frag);
                        if ((cf == PFam::Sitting || cf == PFam::Lying) && hf == cf) {
                            if (o.act) own_retire(oc, o, now, "KUBA-22: the copy's engine posture stays");
                            const uint32_t n = c_kubaSeatKeep.fetch_add(1) + 1;
                            if (n <= 40 || n % 200 == 0)
                                logf("KUBA-22 seat keep npc=%s %s by its engine (%s/%s) -- the host's %s/%s not installed over it [n=%u]", oc.name.c_str(),
                                     wo210rules::pfam_name(cf), cs[0].action, frag_name_of(eid, cs[0].frag).c_str(), cls_name(hs->cls), frag_name_of(eid, hs->frag).c_str(), n);
                            break;
                        }
                    }
                }
                if (o.act) own_retire(oc, o, now, "the host's next action");
                void* act = nullptr;
                const bool overAi = k == 0 && kubaBlockFirst;   // KUBA 27: one priority above the copy's own attack
                const rttr::SwingResult r = rttr::install_fragment(eid, hs->frag, hs->tags, overAi ? hs->prio + 1 : hs->prio, hs->flags, &act);
                if (overAi) {
                    static uint32_t s_n = 0;
                    if (++s_n <= 60 || s_n % 100 == 0)
                        logf("KUBA-27 block first npc=%s frag=%s over its own %s -> %s [n=%u]", oc.name.c_str(), frag_name_of(eid, hs->frag).c_str(),
                             aiRuns ? "AI attack" : "one-shot", rttr::swing_result_name(r), s_n);
                }
                if (r == rttr::SwingResult::Ok && act) {
                    o.act = act; o.hostAct = hs->act; o.frag = hs->frag; o.prio = hs->prio; o.queuedAt = now; o.started = false;
                    if (k == 0) wo217::place_note(oc.ep217, wo217::Note::InstallOk);
                    g_lastInstall[eid] = now;
                    const uint32_t n = c_oInstalls.fetch_add(1) + 1;
                    if (n <= 80 || n % 200 == 0)
                        logf("WO208-OWN install npc=%s scope=%s class=%s frag=%u prio=%d (#%u)", oc.name.c_str(), kOwnScopes[k], cls_name(hs->cls), hs->frag, hs->prio, n);
                } else {
                    if (k == 0) wo217::place_note(oc.ep217, wo217::Note::Refused, rttr::swing_result_name(r));
                    const uint32_t n = c_oRefusals.fetch_add(1) + 1;
                    if (n <= 40) logf("WO208-OWN refused npc=%s scope=%s class=%s frag=%u -> %s", oc.name.c_str(), kOwnScopes[k], cls_name(hs->cls), hs->frag, rttr::swing_result_name(r));
                }
                break;
            }
        }
    }
    // WO-218 C.3 (takeover_stop): while the host's line holds an activity this copy showed, the copy's own later action on its full body (its
    // idle movement, a variation, a scripted callback, its movement transition) is stopped the game's way; the owner's install comes back
    if (wo218::on(wo218::kBitTakeoverStop) && have[0] && cn > 0 && cs[0].ok && !cs[0].empty && !shot && !aiRuns && !rowRuns
        && oc.ep217.open && oc.ep217.shownAt >= 0 && oc.ep217.hostAct == hsc[0].act && cs[0].frag != hsc[0].frag
        && now - oc.takeoverAt >= wo218rules::kTakeoverEveryS && !is_ours(oc, cs[0].act) && wo218rules::takeover_class(cs[0].action)
        && wo218rules::takeover_family(static_cast<uint8_t>(pfam_of(eid, hsc[0].cls, hsc[0].frag)))
        && !(cs[0].frag == oc.takeoverFrag && now < oc.takeoverMuteUntil)) {
        oc.takeoverAt = now;
        // the back-off: the same fragment stopped twice within 10 s comes back at once (the activity's own variation, re-queued by its
        // engine): left alone for 60 s, never a stop-and-requeue loop
        if (cs[0].frag != oc.takeoverFrag || now - oc.takeoverFirst > 10.0) { oc.takeoverFrag = cs[0].frag; oc.takeoverN = 0; oc.takeoverFirst = now; }
        if (++oc.takeoverN > 2) {
            oc.takeoverMuteUntil = now + 60.0;
            logf("WO218-TAKEOVER npc=%s frag=%u came back twice in 10 s (its activity's own variation): left alone for 60 s", oc.name.c_str(), cs[0].frag);
        } else if (rttr::stop_action(const_cast<void*>(cs[0].act))) {
            const uint32_t kk = wo218::note_takeover();
            if (kk <= 200 || kk % 50 == 0)
                logf("WO218-TAKEOVER npc=%s stopped=%s frag=%u -- the copy's own action took the host's activity over (host frag %u); the activity again [n=%u]",
                     oc.name.c_str(), cs[0].action, cs[0].frag, hsc[0].frag, kk);
        }
    }
    // rule 1: a base always exists -- the host's locomotion or idle. A copy's full body empty for more than 0.3 s gets the host's own base
    // fragment (its standing idle, its walk, its footwork: the class the host runs, by fragment and tags) as an action of ours at the
    // bottom priority; the copy's own base action, when it comes back, replaces it (ours is let go). A paused copy never re-queues its
    // own (A.1's tavern probe: the innkeeper's copy stood empty for 12 s, and the nudge of its locomotion "took" nothing).
    const bool empty = cn > 0 && cs[0].ok && cs[0].empty && !shot;
    if (oc.baseAct) {
        uint32_t st = 0;
        const bool readable = rd(oc.baseAct, 0x28, &st);
        if (st == 2 || st == 3) oc.baseStarted = true;
        // WO-210 A: the hand-over installs our base while a withdrawn action of ours may still run above it for a moment -- ours goes when
        // it finished, or when an action that is not ours holds the full body after ours started (the copy's own base is back) or a second
        // after it was queued (another took the scope). Before WO-210 it went as soon as anything else ran (rule 1 only knew an empty body).
        const void* cur = (!empty && cn > 0 && cs[0].ok) ? cs[0].act : nullptr;
        const bool otherHolds = cur && !is_ours(oc, cur);
        const bool legacy = !wo210::on(wo210rules::kBitStateAlways) && !empty && cn > 0 && cs[0].act != oc.baseAct;
        if (!readable || st >= 4 || legacy || (otherHolds && (oc.baseStarted || now - oc.baseAt > 1.0))) {   // finished, or the copy's own base is back: ours goes
            if (st < 4) rttr::stop_action(oc.baseAct);
            oc.retired.push_back({oc.baseAct, now});
            oc.baseAct = nullptr;
        }
    }
    if (!empty) oc.fullEmptySince = -1;
    else {
        if (oc.fullEmptySince < 0) oc.fullEmptySince = now;
        if (now - oc.fullEmptySince > 0.3 && now - oc.lastPulse > 1.0 && !oc.baseAct && !queueFull) {
            oc.lastPulse = now;
            const HostScope* hb = have[0] && is_base(fam_of(eid, hsc[0].cls, hsc[0].frag)) && hsc[0].cls != kClsNone ? &hsc[0] : nullptr;
            const char* how = "no host base to give it";
            HostScope nb{};
            bool c = false;
            motion::combat_mode_of(eid, &c);
            if (hb) {
                void* act = nullptr;
                const rttr::SwingResult r = rttr::install_fragment(eid, hb->frag, hb->tags, kBasePriority, hb->flags, &act);
                if (r == rttr::SwingResult::Ok && act) { oc.baseAct = act; oc.baseFrag = hb->frag; how = "the host's base fragment installed"; }
                else how = rttr::swing_result_name(r);
            } else if (wo214::on(wo214rules::kBitOwnerBase) && !c && neutral_idle(eid, &nb)) {
                // WO-214 C.1 (owner_base): no host base known -- the copy's own neutral idle (the field's 3 "no host base to give it" stood empty)
                void* act = nullptr;
                const rttr::SwingResult r = rttr::install_fragment(eid, nb.frag, nb.tags, kBasePriority, nb.flags, &act);
                if (r == rttr::SwingResult::Ok && act) {
                    oc.baseAct = act; oc.baseFrag = nb.frag; oc.baseAt = now; oc.baseStarted = false;
                    how = "the copy's neutral idle installed (no host base)";
                    wo214::note_base_given(wo214rules::Base::NeutralIdle, false);
                } else if (motion::request_idle_pulse(eid)) how = "locomotion nudged (no host base; its neutral idle refused)";
            } else {
                if ((c && motion::requeue_guard_idle(eid)) || motion::request_idle_pulse(eid)) how = c ? "guard idle (no host base)" : "locomotion nudged (no host base)";
            }
            const uint32_t n = c_oPulses.fetch_add(1) + 1;
            if (n <= 40 || n % 100 == 0)
                logf("WO208-OWN base npc=%s full body empty %.0f ms -> %s%s (#%u)", oc.name.c_str(), (now - oc.fullEmptySince) * 1000.0, how,
                     hb ? (std::string(" class=") + cls_name(hb->cls) + " frag=" + std::to_string(hb->frag)).c_str() : "", n);
        }
    }
    for (size_t i = 0; i < oc.retired.size();) {
        uint32_t st = 0;
        const bool alive = engine::entity_by_id(eid) != nullptr;
        if (!alive || !rd(oc.retired[i].act, 0x28, &st) || st >= 4 || now - oc.retired[i].at > 5.0) {
            if (alive) late_take(eid, oc.retired[i].act);   // the late stop: unfinished -> followed until it ends (finished or unreadable: released there)
            oc.retired.erase(oc.retired.begin() + static_cast<std::ptrdiff_t>(i));
        } else ++i;
    }
    // WO-214 C.2 (combat_end) and C.1's instrument, every 250 ms beside the watchdog
    if (now - oc.w214At >= wo210rules::kWatchEveryS && cn > 0 && cs[0].ok) {
        oc.w214At = now;
        if (cm || wo209::mirrored(eid)) oc.hostFightAt = now;
        const bool hostBase = have[0] && is_base(fam_of(eid, hsc[0].cls, hsc[0].frag));
        if (!hostBase) oc.hostBaseSince = -1; else if (oc.hostBaseSince < 0) oc.hostBaseSince = now;
        // the candidate: the full body's action -- not our base, not a base family itself, not paired, and not ours while it still mirrors
        // the host's current action (that is the owner's own business)
        const Fam curFam = cs[0].empty ? Fam::Empty : fam_of(eid, class_id(cs[0].action), cs[0].frag);
        const bool mirrorsHost = oc.own[0].act && cs[0].act == oc.own[0].act && have[0] && hsc[0].act == oc.own[0].hostAct;
        const bool combatClass = !cs[0].empty && class_id(cs[0].action) == kClsCombatAnim;
        const bool candidate = !cs[0].empty && cs[0].act && cs[0].act != oc.baseAct && !is_base(curFam) && curFam != Fam::Hold && !mirrorsHost
                               && wo214rules::stoppable_class(combatClass) && now >= oc.c2RestUntil
                               && !wo172::copy_dead(eid) && !wo210kd::held_down(eid);
        const bool combatRow = candidate && combatClass && wo214rules::combat_fragment_name(frag_name_of(eid, cs[0].frag).c_str());
        if (!candidate) oc.cbAct = nullptr;
        else if (oc.cbAct != cs[0].act) { oc.cbAct = cs[0].act; oc.cbSince = now; }
        const double heldS = candidate ? now - oc.cbSince : 0.0;
        const double outS = oc.hostFightAt > 0 ? now - oc.hostFightAt : 1e9;
        const double baseS = oc.hostBaseSince >= 0 ? now - oc.hostBaseSince : 0.0;
        const wo214rules::Stuck why = wo214rules::stale_action(wo214::on(wo214rules::kBitCombatEnd), candidate, combatRow, heldS, baseS, outS, qi.n > 0);
        if (why != wo214rules::Stuck::No && !shot && !aiRuns) {
            // the game's own stop, whosever it is: a withdrawn (retired) action of ours had only its priority stepped down, and a looping
            // posture never yields to that
            for (auto& sc : oc.own) if (sc.act && sc.act == cs[0].act) { oc.retired.push_back({sc.act, now}); sc = Probe::Scope{}; }
            rttr::stop_action(const_cast<void*>(cs[0].act));
            wo214::note_combat_withdrawn(why, oc.name.c_str(), heldS, cs[0].frag);
            own_handover(eid, oc, now, wo210rules::kPathAttackEnd);
            oc.cbAct = nullptr;
            for (int i = wo214rules::kChurnStops - 1; i > 0; --i) oc.c2Stop[i] = oc.c2Stop[i - 1];
            oc.c2Stop[0] = now;
            if (wo214rules::churn(oc.c2Stop[wo214rules::kChurnStops - 1], now)) {
                oc.c2RestUntil = now + wo214rules::kChurnRestS;
                wo214::note_combat_churn(oc.name.c_str(), cs[0].frag, now - oc.c2Stop[wo214rules::kChurnStops - 1]);
            }
        }
        float dist = -1.0f;
        {
            float me2[3]{}, cp[3]{};
            void* pe2 = engine::entity_by_id(kPlayerEntity);
            void* ce = engine::entity_by_id(eid);
            if (pe2 && ce && engine::entity_world_pos(pe2, me2) && engine::entity_world_pos(ce, cp))
                dist = std::sqrt((cp[0] - me2[0]) * (cp[0] - me2[0]) + (cp[1] - me2[1]) * (cp[1] - me2[1]));
        }
        const bool emptyNear = cs[0].empty && !wo172::copy_dead(eid) && dist >= 0 && dist <= 60.0f;
        if (emptyNear) {
            if (oc.emptySince < 0) { oc.emptySince = now; oc.emptyDist = dist; }
            else if (now - oc.emptySince >= 10.0 && now - oc.emptyLogAt >= 10.0) {
                oc.emptyLogAt = now;
                logf("WO214-EMPTY npc=%s secs=%.1f dist_m=%.1f open=1 why=still-empty", oc.name.c_str(), now - oc.emptySince, dist);
            }
        } else empty_close(oc, now, !cs[0].empty ? "an-action-again" : wo172::copy_dead(eid) ? "dead" : "beyond-60m");
    }
    // KUBA 11: the skeleton's own bind-pose watch -- every 250 ms, layer 0's animation count; none for 0.3 s on a living, standing copy
    // is the T-pose the player sees. Told (KUBA-TPOSE) with what the Mannequin scope claims, and repaired at most every 1 s (backed off
    // to 4 s while the same stretch lasts).
    if (now - oc.kubaWatchAt >= 0.25) {
        oc.kubaWatchAt = now;
        void* ent = engine::entity_by_id(eid);
        const int f0 = ent ? motion::skel_fifo_count(ent, 0) : -1;
        const bool skip = wo172::copy_dead(eid) || wo210kd::held_down(eid);
        // KUBA 16: the combat footwork's feet (motion.cpp writes the copy's travel speed / angle while its full body runs it)
        // KUBA 17 (Kuba, 20:49 fight: "the NPCs move their legs and no moonwalk -- now the same for ordinary NPCs walking"): the copy's own
        // locomotion (CAnimActionAIMovement: MotionIdle / MotionMovement / turns) gets the same travel speed and angle from its rendered move
        // -- WO-201's walking copies stepped at half rate (their walk's parameters held from its first blend). KCDMP_KUBA_WALK_FEET=off: as 16.
        {
            const bool live = !skip && cn > 0 && cs[0].ok && !cs[0].empty;
            // 17b (21:07-21:13): only a human copy's own walk (fragment MotionMovement) -- a dog's locomotion (no fragment names: 351 samples
            // of nothing on its skeleton) and standing idles (ttkc_woman_15's MotionIdle, 41 s of nothing) are left to the engine
            const bool footwork = live && (std::strcmp(cs[0].action, "C_CombatAnimActionMovement") == 0 ||
                                           (kuba_walk_feet_on() && std::strcmp(cs[0].action, "CAnimActionAIMovement") == 0 &&
                                            frag_name_of(eid, cs[0].frag) == "MotionMovement"));
            if (footwork != oc.kubaFeet) { motion::kuba_set_combat_feet(eid, footwork); oc.kubaFeet = footwork; }
        }
        // KUBA 13: the walk transition's clock
        if (!skip && kuba_trans_on() && cn > 0 && cs[0].ok && !cs[0].empty) {
            const std::string tn = frag_name_of(eid, cs[0].frag);
            const bool walkTrans = tn == "IdleToMove" || tn == "MoveToIdle";
            if (!walkTrans) { oc.kubaTransAct = nullptr; oc.kubaTransSince = -1; }
            else {
                if (oc.kubaTransAct != cs[0].act) { oc.kubaTransAct = cs[0].act; oc.kubaTransSince = now; }
                float mps = 0;
                const bool dragged = motion::body_render_speed(eid, &mps) && mps >= 0.6f;
                if (dragged && now - oc.kubaTransSince >= kKubaTransMaxS) {
                    const bool oursT = is_ours(oc, cs[0].act);
                    if (oursT && oc.own[0].act == cs[0].act) { oc.kubaTransBlockHost = oc.own[0].hostAct; own_retire(oc, oc.own[0], now, "KUBA-13: a walk transition held too long"); }
                    else rttr::stop_action(const_cast<void*>(cs[0].act));
                    bool walked = false;
                    if (oc.hostHave && is_base(family_of_id(oc.host.cls, oc.host.frag))) walked = own_handover(eid, oc, now, wo210rules::kPathUnknown);
                    if (!walked) walked = motion::request_idle_pulse(eid);
                    const uint32_t k = c_kubaTransCut.fetch_add(1) + 1;
                    if (k <= 60 || k % 100 == 0)
                        logf("KUBA-13 cut npc=%s %s (%s) held %.0f ms at %.2f m/s -> %s [n=%u]", oc.name.c_str(), tn.c_str(), oursT ? "ours" : "its own",
                             (now - oc.kubaTransSince) * 1000.0, mps, walked ? "the walk installed" : "stopped only", k);
                    oc.kubaTransAct = nullptr; oc.kubaTransSince = -1;
                }
            }
        }
        if (f0 < 0) c_kubaTposeUnread.fetch_add(1);
        if (f0 != 0 || skip) {
            if (f0 > 0 && oc.kubaEmptySince >= 0 && oc.kubaFixes > 0) {
                const uint32_t k = c_kubaTposeBack.fetch_add(1) + 1;
                if (k <= 60 || k % 100 == 0)
                    logf("KUBA-TPOSE back npc=%s layer0=%d after %.0f ms (%u repair(s)) [n=%u]", oc.name.c_str(), f0, (now - oc.kubaEmptySince) * 1000.0, oc.kubaFixes, k);
            }
            oc.kubaEmptySince = -1; oc.kubaFixes = 0; oc.kubaBackoff = 0;
        } else {
            if (oc.kubaEmptySince < 0) oc.kubaEmptySince = now;
            if (now - oc.kubaEmptySince >= 0.3 && now - oc.kubaFixAt >= 1.0 + oc.kubaBackoff) {
                const bool first = oc.kubaFixes == 0;
                const char* fbCls = (cn > 0 && cs[0].ok && !cs[0].empty) ? cs[0].action : "-";
                const std::string fbName = (cn > 0 && cs[0].ok && !cs[0].empty) ? frag_name_of(eid, cs[0].frag) : std::string("-");
                const bool ours = cn > 0 && cs[0].ok && !cs[0].empty && is_ours(oc, cs[0].act);
                const char* hostFam = oc.hostHave ? wo210rules::pfam_name(pfam_of(eid, oc.host.cls, oc.host.frag)) : "-";
                bool fixed = false;
                if (ours && kuba_learn_on() && cs[0].frag && first && ++g_kubaEmptyHits[cs[0].frag] >= 3 && g_kubaDeadFrags.insert(cs[0].frag).second) {
                    c_kubaLearned.fetch_add(1);
                    logf("KUBA-12 learned frag=%u (%s) plays nothing on a copy -- the owner never installs it again", cs[0].frag, fbName.c_str());
                }
                if (kuba_tpose_fix_on()) {
                    if (oc.own[0].act) own_retire(oc, oc.own[0], now, "KUBA-11: the bind pose (nothing on the skeleton)");
                    if (oc.baseAct) { rttr::stop_action(oc.baseAct); oc.retired.push_back({oc.baseAct, now}); oc.baseAct = nullptr; oc.baseFrag = 0; }
                    fixed = own_handover(eid, oc, now, wo210rules::kPathUnknown);
                    if (!fixed) fixed = motion::request_idle_pulse(eid);
                    if (fixed) c_kubaTposeFixed.fetch_add(1);
                }
                if (first) c_kubaTposeSeen.fetch_add(1);
                const uint32_t k = c_kubaTposeSeen.load();
                if (oc.kubaFixes < 3 && (k <= 200 || k % 50 == 0))
                    logf("KUBA-TPOSE npc=%s layer0=0 for %.0f ms full_body=%s/%s (%s) host=%s queue=%d -> %s (#%u, stretch repair %u)", oc.name.c_str(),
                         (now - oc.kubaEmptySince) * 1000.0, fbCls, fbName.c_str(), ours ? "ours" : "its own", hostFam, qi.n,
                         !kuba_tpose_fix_on() ? "watch only (KCDMP_KUBA_TPOSE_FIX=off)" : fixed ? "the host's state installed anew" : "nothing installed", k, oc.kubaFixes + 1);
                ++oc.kubaFixes;
                oc.kubaFixAt = now;
                oc.kubaBackoff = oc.kubaBackoff <= 0 ? 0.5 : (oc.kubaBackoff * 2 > 3.0 ? 3.0 : oc.kubaBackoff * 2);
            }
        }
    }
    // WO-210 A.5 the watchdog: every 250 ms, the copy's full body against the host NPC's current family (wo210_rules.h check()); a breach
    // that outlasts its threshold is reseated through the hand-over and told (WO210-STATE reseat), charged to the copy's last exit path
    if (wo210::on(wo210rules::kBitStateAlways) && now - oc.watchAt >= wo210rules::kWatchEveryS && cn > 0 && cs[0].ok) {
        oc.watchAt = now;
        using namespace wo210rules;
        const PFam copyF = cs[0].empty ? PFam::Empty : pfam_of(eid, class_id(cs[0].action), cs[0].frag);
        const PFam hostF = have[0] ? pfam_of(eid, hsc[0].cls, hsc[0].frag) : PFam::Empty;
        bool trans = false;
        if (!cs[0].empty && transition_name(frag_name_of(eid, cs[0].frag).c_str())) {
            if (oc.transFrag != cs[0].frag) { oc.transFrag = cs[0].frag; oc.transSince = now; }
            trans = now - oc.transSince <= kTransitionMaxS;
        } else oc.transFrag = 0xFFFFFFFFu;
        const bool mod = shot || aiRuns || row_holds(eid, now) || copyF == PFam::Combat || copyF == PFam::Paired;
        // the moonwalk: is the body being dragged along this screen right now (the gait's own smoothed speed)?
        float slideMps = 0;
        const bool bodyMoving = slide_on() && motion::body_render_speed(eid, &slideMps) && slideMps >= kSlideMps;
        Verdict v = check(copyF, hostF, trans, mod, !wo210::fix_on(kFixPostureOtherGrace), bodyMoving);
        if (have[0] && hsc[0].cls == kClsNone) { if (oc.hostEmptySince < 0) oc.hostEmptySince = now; } else oc.hostEmptySince = -1;   // WO-215 D.1
        if (!have[0] && v != Verdict::Empty) v = Verdict::Ok;   // no host state to judge a posture by: only an empty body counts
        // the same fragment is the same body, whatever class plays it (the owner installs the host's idle-state postures through a combat
        // action: d1's first watchdog took "ours, other" against "the host's, working" for the very same work fragment, 132 reseats)
        if (v != Verdict::Empty && have[0] && !cs[0].empty && cs[0].frag == hsc[0].frag) v = Verdict::Ok;
        // WO-217 0.2: the seat word (the host NPC seated, the copy in that posture) for the writer's one-writer rule
        oc.seatHost = hostF == PFam::Sitting || hostF == PFam::Lying;
        oc.seatCopy = copyF == PFam::Sitting || copyF == PFam::Lying;
        oc.seatAt = now;
        // WO-213 E.3: the copy and its host NPC both in a posture -- the position writer leaves the body alone while it holds
        if (wo213rules::posture_holds(posture(copyF), posture(hostF) || (posture(copyF) && hostF == PFam::Other), true)) g_postureHeld.insert(eid);
        else g_postureHeld.erase(eid);
        // WO-213 E.2: a posture breach while the host's state of this body is late (none past its 2 s resend) is late, not missing:
        // the copy keeps its last posture, nothing is reseated; after a 2 s hold the body is asked for again (op 140, kind 2)
        if (wo213::on(wo213rules::kBitPostureHoldLate) && (v == Verdict::MissingPosture || v == Verdict::StalePosture || v == Verdict::PostureOther)) {
            const wo213rules::Late late = wo213rules::lateness(now - oc.stateAt);
            if (late != wo213rules::Late::Fresh) {
                wo213::note_late_check();
                if (late == wo213rules::Late::AskAgain && now - oc.lateAskAt >= wo213rules::kLateHoldS && g_asks.size() < 64) {
                    oc.lateAskAt = now;
                    g_asks.push_back({2, oc.name});
                    wo213::note_late_ask();
                }
                v = Verdict::Ok;
            }
        }
        if (v == Verdict::MissingPosture) wo213::note_missing_check();
        // KUBA 10a: the copy's work fidget (GuardVAR, SellerLoop_VAR ...) is the work its host NPC does, not a missing posture
        if (v == Verdict::MissingPosture && kuba_fidget_on() && !cs[0].empty &&
            fidget_of_work(class_id(cs[0].action) == kClsVariation, frag_name_of(eid, cs[0].frag).c_str(), hostF)) {
            v = Verdict::Ok;
            c_kubaFidgetOk.fetch_add(1);
        }
        wo210::note_check(static_cast<uint8_t>(v));
        if (v == Verdict::Ok) { oc.breachSince = -1; oc.breach = 0; oc.reseatBackoff = 0; }
        else {
            if (oc.breachSince < 0 || oc.breach != static_cast<uint8_t>(v)) { oc.breachSince = now; oc.breach = static_cast<uint8_t>(v); }
            const double held = now - oc.breachSince;
            if (held >= breach_after_s(v) + oc.reseatBackoff) {
                const bool copysOwn = !cs[0].empty && !is_ours(oc, cs[0].act);
                // a breach no exit explains: the host's posture the owner's install has not shown yet (16), the copy's own engine posture (17)
                uint8_t path = charged_path(oc.lastPath, oc.lastPathAt, now);
                if (path == kPathUnknown && v == Verdict::MissingPosture) path = kPathInstallPending;
                if (path == kPathUnknown && (v == Verdict::StalePosture || v == Verdict::StaleDead) && copysOwn) path = kPathOwnPosture;
                if (path == kPathUnknown && v == Verdict::PostureOther) path = kPathInstallPending;   // the host's gesture not shown yet
                if (path == kPathUnknown && v == Verdict::Sliding && copysOwn) path = kPathOwnPosture;   // the moonwalk: its own animation
                // WO-214 C.1: an empty body no exit of ours explains is the copy's own engine's (its own action ended with nothing after)
                if (path == kPathUnknown && v == Verdict::Empty && wo214::on(wo214rules::kBitOwnerBase)) path = kPathEmptyOwn;
                const bool line215 = wo215::on(wo215rules::kBitActivityLine);
                // WO-215 C: our own posture outlived the host NPC's and no exit was recorded (the field's 8 "path=unknown"): named
                if (path == kPathUnknown && line215 && (v == Verdict::StalePosture || v == Verdict::StaleDead) && !copysOwn) {
                    path = kPathStaleOurs; c_w215StaleOurs.fetch_add(1, std::memory_order_relaxed);
                }
                // WO-215 D.1: the host NPC's whole body empty for 2 s -- the host holds this body dormant (an encounter pool's parked one, the
                // field's Cuman: 42 reseats that could install nothing): no copy should stand here -- the agent parks it (ask kind 3)
                if (wo215::on(wo215rules::kBitDormant) && v == Verdict::Empty && have[0] && hsc[0].cls == kClsNone && oc.hostEmptySince >= 0 &&
                    now - oc.hostEmptySince >= 2.0) {
                    if (now - oc.dormantAskAt >= 5.0 && g_asks.size() < 64) { oc.dormantAskAt = now; g_asks.push_back({3, oc.name}); c_w215Dormant.fetch_add(1); }
                    wo210::note_reseat(oc.name.c_str(), "empty", "empty", kPathDormantHost, held, false);
                    oc.breachSince = now;
                    oc.reseatBackoff = 2.0;
                    return;
                }
                // WO-215 C: the host's activity installed twice and still not on the body is refused HERE (its fragment needs what this copy has
                // not: a tool, an object, a smart object's use): a named refusal, the ladder's base instead, the copy reported as missing it --
                // never the field's endless 16-install-pending loop (231 reseats); asked again after 30 s or when the host's activity changes
                // WO-215 C.0 (one description, one path): the field's "working" and "sitting" loops were bodies whose activity WO-141 held as an
                // NPC-state stance or unstance (cooking, woodchopping, a bench) while this watchdog installed the same activity's fragment over
                // it, again and again. The NPC-state placement is the activity's path when it holds one: the owner leaves the body to it; a
                // placement the game's planner gave up on is a named refusal (the base instead, reported missing).
                if (line215 && (v == Verdict::MissingPosture || v == Verdict::PostureOther) && have[0]) {
                    const int np = wo141::activity_path(oc.name.c_str());
                    // WO-217 A.1 (placement): a copy the neutral rule keeps from the planner is never left to an NPC-state placement that
                    // never comes (WO-216's acceptance: every one of 247 such reseats); the owner installs the host's activity itself
                    if (wo217rules::defer_to_npc_state(wo217::on(wo217rules::kBitPlacement), wo200::copy_neutral_always(), np)) {
                        wo217::place_note(oc.ep217, wo217::Note::Deferred);
                        c_w215NpcState.fetch_add(1, std::memory_order_relaxed);
                        wo210::note_reseat(oc.name.c_str(), cs[0].empty ? "empty" : pfam_name(copyF), pfam_name(hostF), kPathNpcState, held, false);
                        oc.breachSince = now;
                        oc.reseatBackoff = 2.0;
                        return;
                    }
                    if (np == 3 && now >= oc.c215RefusedUntil) {
                        wo217::place_note(oc.ep217, wo217::Note::RefusedHere);
                        oc.c215RefusedUntil = now + 30.0;
                        oc.c215Missing = true;
                        const bool based = wo215::on(wo215rules::kBitLadder) && w215_give_base(eid, oc, now);
                        const uint32_t k = c_w215Refused.fetch_add(1, std::memory_order_relaxed) + 1;
                        if (k <= 200 || k % 50 == 0)
                            logf("WO215-ACTIVITY-MISSING npc=%s host=%s(%s frag %u) npc-state-refused: the game's planner gave up on its placement here -> %s; "
                                 "asked again when the host's activity changes [n=%u]", oc.name.c_str(), pfam_name(hostF), frag_name_of(eid, hsc[0].frag).c_str(),
                                 hsc[0].frag, based ? "the copy's base (its own idle)" : "no base", k);
                        wo210::note_reseat(oc.name.c_str(), cs[0].empty ? "empty" : pfam_name(copyF), pfam_name(hostF), kPathNpcStateRefused, held, based);
                        oc.breachSince = now;
                        oc.reseatBackoff = 4.0;
                        return;
                    }
                    if (oc.c215Frag != hsc[0].frag || now - oc.c215At > 30.0) { oc.c215Frag = hsc[0].frag; oc.c215Tries = 0; oc.c215Missing = false; oc.c215RefusedUntil = 0; }
                    oc.c215At = now;
                    if (now < oc.c215RefusedUntil) {
                        c_w215RefusedRest.fetch_add(1, std::memory_order_relaxed);
                        oc.breachSince = now;
                        oc.reseatBackoff = 4.0;
                        return;
                    }
                    if (++oc.c215Tries > 2) {
                        wo217::place_note(oc.ep217, wo217::Note::RefusedHere);
                        oc.c215RefusedUntil = now + 30.0;
                        oc.c215Missing = true;
                        // B (ladder): the rung under the activity this copy cannot show -- its own idle, else the engine's (the twin: none)
                        const bool based = wo215::on(wo215rules::kBitLadder) && w215_give_base(eid, oc, now);
                        const uint32_t k = c_w215Refused.fetch_add(1, std::memory_order_relaxed) + 1;
                        if (k <= 200 || k % 50 == 0)
                            logf("WO215-ACTIVITY-MISSING npc=%s host=%s(%s frag %u) refused-here: installed %d times, never shown on this body -> %s; asked again in 30 s "
                                 "or when the host's activity changes [n=%u]", oc.name.c_str(), pfam_name(hostF), frag_name_of(eid, hsc[0].frag).c_str(), hsc[0].frag,
                                 oc.c215Tries - 1, based ? "the copy's base (its own idle)" : "no base could be given", k);
                        wo210::note_reseat(oc.name.c_str(), cs[0].empty ? "empty" : pfam_name(copyF), pfam_name(hostF), kPathRefusedHere, held, based);
                        oc.breachSince = now;
                        oc.reseatBackoff = 4.0;
                        return;
                    }
                }
                const bool ok = own_handover(eid, oc, now, path);
                // the copy's own engine posture (its NPC state's bed, seat or workstation: an idle state at priority 3) is over any base
                // of ours: its stance is released the game's own way by the agent (op 140), at most every 5 s
                if ((v == Verdict::StalePosture || v == Verdict::StaleDead || v == Verdict::Sliding) && copysOwn && now - oc.askedAt >= 5.0 && g_asks.size() < 64) {
                    oc.askedAt = now;
                    g_asks.push_back({1, oc.name});
                }
                // KUBA 18 (joiner 21:08-21:13: while the host's NPC sat, its copy stood 72 % of the samples -- ttkc_man_16 "placed=sitting@..."
                // by WO-160 from 21:09:52, standing, reseated every 9 s with nothing that sits it: the seated fragments played through ours
                // show nothing (KUBA 12), and a paused copy never carries out a stance set on it). The reverse case: the host's NPC sits or
                // lies, its copy stands on its own -- its stance is released the game's way (op 140) so WO-160's next placement seats it the
                // engine's own way (WO-211: "the copy was let go, WO-160 sat it down again"), at most every 10 s. KCDMP_KUBA_SEAT_ASK=off.
                else if (v == Verdict::MissingPosture && copysOwn && (hostF == PFam::Sitting || hostF == PFam::Lying) && kuba_seat_ask_on() &&
                         now - oc.askedAt >= 10.0 && g_asks.size() < 64) {
                    oc.askedAt = now;
                    g_asks.push_back({1, oc.name});
                    const uint32_t k = c_kubaSeatAsks.fetch_add(1) + 1;
                    if (k <= 60 || k % 100 == 0)
                        logf("KUBA-18 seat npc=%s stands while its host NPC %s -- its stance released for WO-160 to seat it again [n=%u]", oc.name.c_str(),
                             hostF == PFam::Sitting ? "sits" : "lies", k);
                }
                wo210::note_reseat(oc.name.c_str(), cs[0].empty ? "empty" : (std::string(pfam_name(copyF)) + (copysOwn ? "(its own)" : "(ours)") +
                                   (v == Verdict::Sliding ? ",sliding@" + std::to_string(static_cast<int>(slideMps * 100.0f)) + "cm/s" : "")).c_str(),
                                   pfam_name(hostF), path, held, ok);
                oc.breachSince = now;   // asked again after another threshold, backed off while the same breach outlasts the reseats (1 s .. 8 s)
                oc.reseatBackoff = oc.reseatBackoff <= 0 ? 0.75 : (oc.reseatBackoff * 2 > 8.0 ? 8.0 : oc.reseatBackoff * 2);
            }
        }
    }
}

// WO-210 op 140: the asks the agent does the game's own way: [max:1] -> [n:1] n x ([kind:1][nameLen:1][name])
uint8_t op_asks(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    if (len != 2 || cap < 1) return wo163::kRBadRequest;
    const size_t max = body[1] ? body[1] : 1;
    size_t o = 1, n = 0;
    while (!g_asks.empty() && n < max) {
        const Ask& a = g_asks.front();
        const size_t nl = a.name.size() > 63 ? 63 : a.name.size();
        if (o + 2 + nl > cap) break;
        out[o] = a.kind; out[o + 1] = static_cast<uint8_t>(nl); std::memcpy(out + o + 2, a.name.data(), nl);
        o += 2 + nl; ++n;
        g_asks.erase(g_asks.begin());
        c_asksOut.fetch_add(1);
    }
    out[0] = static_cast<uint8_t>(n);
    *outLen = o;
    return wo163::kROk;
}

void own_release(uint32_t eid, OwnCopy& oc, double now, const char* why, uint8_t path = wo210rules::kPathUnknown) {
    wo217::place_close(eid, oc.name.c_str(), oc.ep217, now, "let go");
    g_postureHeld.erase(eid);   // WO-213 E.3: a copy the owner lets go of is the position writer's again
    empty_close(oc, now, "let-go");   // WO-214 C.1's instrument
    if (oc.kubaFeet) { motion::kuba_set_combat_feet(eid, false); oc.kubaFeet = false; }   // KUBA 16
    const bool alive = engine::entity_by_id(eid) != nullptr;
    // WO-210 A.2 (paths 2, 12, 13, 14): the host's current base -- or the body's neutral idle when the host's state is a posture or not
    // known -- installed now under the copy's own base and left running when we let go: the copy's own engine replaces it once it queues
    // its own base, and until then the host's last base plays (session E: the owner switched off live left every copy empty, "hard core
    // T posing"; a let-go copy kept looping the host's last posture). A dead copy is left as it lies (C.2).
    const bool hand = alive && path != wo210rules::kPathDeath && wo210::on(wo210rules::kBitStateAlways) && !wo172::copy_dead(eid);
    if (hand) {
        const bool baseKnown = oc.hostHave && oc.host.cls != kClsNone && is_base(fam_of(eid, oc.host.cls, oc.host.frag));
        const HostScope keep = oc.host; const bool keepHave = oc.hostHave;
        if (!baseKnown) oc.hostHave = false;
        own_handover(eid, oc, now, path);
        oc.host = keep; oc.hostHave = keepHave;
    }
    // WO-221 A: a dead body's animation is the engine's own (its death takes the controller apart): the owner gives its references
    // back and touches nothing else -- no stop, no priority (rm4: the joiner crashed in the controller's update 1.0 s after the
    // let-go of a dying copy stopped its base action)
    const bool deadBody = wo221::on(wo221::kBitCombatActionEnd) && (path == wo210rules::kPathDeath || wo172::copy_dead(eid));
    for (auto& s : oc.own) if (s.act) {
        if (alive) {
            uint32_t st = 0;
            if (deadBody) { /* only the reference */ }
            else if (hand && rd(s.act, 0x28, &st) && st < 2) rttr::stop_action(s.act); else wr_prio(s.act, kWithdrawnPriority);
            if (deadBody) rttr::release_action(s.act); else late_take(eid, s.act);   // KUBA: the late stop
        }
        s = Probe::Scope{};
    }
    if (oc.baseAct) { if (alive) { if (!hand && !deadBody) rttr::stop_action(oc.baseAct); if (deadBody) rttr::release_action(oc.baseAct); else late_take(eid, oc.baseAct); } oc.baseAct = nullptr; }
    for (auto& r : oc.retired) if (alive) { if (deadBody) rttr::release_action(r.act); else late_take(eid, r.act); }
    oc.retired.clear();
    if (oc.held && alive) motion::set_npc_guard_hold(eid, false, nullptr, npcdrive::now_s());
    wo210::note_release(path);
    logf("WO208-OWN let go npc=%s (%s)%s", oc.name.c_str(), why, hand ? " -- the host's base handed over" : "");
}

void owner_tick(double now) {
    if (!g_ownerEnvRead) {
        g_ownerEnvRead = true;
        g_ownerOnly = env("KCDMP_TEST_W208_OWNER_ONLY");
        const std::string d = env("KCDMP_TEST_W208_OWNER_DELAY_MS");
        if (!d.empty()) g_ownerDelayS = std::atof(d.c_str()) / 1000.0;
        if (!g_ownerOnly.empty()) logf("WO208-OWN the bench's A.1: the owner drives only %s (delay %.0f ms)", g_ownerOnly.c_str(), g_ownerDelayS * 1000.0);
    }
    // the bench's A.1 with an NPC picked in the running world (the village walk): KCDMP_TEST_W208_OWNER_ONLY=file names it in
    // kcdmp-w208-probe.txt beside the game, read every 2 s
    static std::string s_fileMode;
    if (g_ownerOnly == "file" || !s_fileMode.empty()) {
        if (s_fileMode.empty()) { s_fileMode = "file"; g_ownerOnly = "-"; }
        static double s_at = -1e9;
        if (now - s_at >= 2.0) {
            s_at = now;
            char b[96] = {};
            FILE* f = nullptr;
            if (fopen_s(&f, "kcdmp-w208-probe.txt", "rb") == 0 && f) {
                const size_t n = std::fread(b, 1, sizeof b - 1, f);
                std::fclose(f);
                b[n] = 0;
                for (char* q = b; *q; ++q) if (*q == '\r' || *q == '\n' || *q == ' ') { *q = 0; break; }
                if (b[0] && g_ownerOnly != b) { g_ownerOnly = b; logf("WO208-OWN the bench's A.1: the owner drives only %s (the bench's file)", b); }
            }
        }
    }
    if (!on(kBitOwner)) {
        for (auto& kv : g_owned) own_release(kv.first, kv.second, now, "mp_copy_anim_owner off", wo210rules::kPathSwitch);
        g_owned.clear(); g_copyStates.clear();
        return;
    }
    float me[3]{};
    void* pe = engine::entity_by_id(kPlayerEntity);
    const bool haveMe = pe && engine::entity_world_pos(pe, me);
    const bool nearOnly = haveMe && wo209::fix_on(wo209rules::kFixOwnerNear);
    for (auto it = g_copyStates.begin(); it != g_copyStates.end();) {
        const uint32_t eid = it->first;
        auto& v = it->second;
        void* e = engine::entity_by_id(eid);
        if (!e || v.empty()) { it = g_copyStates.erase(it); continue; }
        const char* nm = engine::entity_name(e);
        if (!g_ownerOnly.empty() && (!nm || _stricmp(nm, g_ownerOnly.c_str()) != 0)) { it = g_copyStates.erase(it); continue; }
        // WO-215 A.0: the probe owns its body for its run -- the owner lets it go (its installs released) and leaves it alone meanwhile
        if (wo215::probe_owned(eid)) {
            const auto ow = g_owned.find(eid);
            if (ow != g_owned.end()) { own_release(eid, ow->second, now, "the A.0 probe owns it", wo210rules::kPathSwitch); g_owned.erase(ow); }
            v.clear();
            ++it;
            continue;
        }
        // WO-221 B (mp_talk_hold): a copy in a conversation is asked for nothing -- the owner lets it go for the talk (its installs
        // released once) and neither installs nor drains its queue meanwhile; the host's states wait here for the talk's end
        if (talkhold::holds(eid, talkhold::Lane::Owner)) {
            const auto ow = g_owned.find(eid);
            if (ow != g_owned.end()) { own_release(eid, ow->second, now, "it is in a conversation", wo210rules::kPathSwitch); g_owned.erase(ow); }
            if (v.size() > 1) v.erase(v.begin(), v.end() - 1);   // the newest of the host's states is the one the talk's end applies
            ++it;
            continue;
        }
        // WO-209 (the owner's village runs): the host streams every body within 40 m of either player, so copies by the host 130 m and more
        // from the local player were owned here with no full-body action the engine plays at that distance (empty: a T-pose as they come
        // into view). The owner drives the copies near the local player; a farther one is its own engine's and the old writers', as with
        // the owner off (owner_near_only)
        if (nearOnly && !g_kubaRemoteDuel.count(eid)) {   // KUBA 30 stage 2: a partner's duel is driven at any distance from this player
            float cp[3]{};
            const float d = engine::entity_world_pos(e, cp) ? std::sqrt((cp[0] - me[0]) * (cp[0] - me[0]) + (cp[1] - me[1]) * (cp[1] - me[1])) : -1.0f;
            const auto ow = g_owned.find(eid);
            // WO-215 B (letgo_cull): an owned copy is let go only at the cull range (the stream's 150 m), never at WO-209's 70 m -- the route
            // and the ladder keep a base on it out there; the twin: 70 m
            if (d >= 0 && !wo215rules::owner_keeps(wo215::on(wo215rules::kBitLetGoCull), ow != g_owned.end(), d, wo209rules::kOwnerNearM)) {
                if (ow != g_owned.end()) {
                    // WO-214 C.1: the distance of each let-go, against the cull and the bands (the field: 114 let-gos)
                    char why[64]; std::snprintf(why, sizeof why, "far from the local player: %.0f m", d);
                    own_release(eid, ow->second, now, why, wo210rules::kPathFar); g_owned.erase(ow); c_oFar.fetch_add(1);
                }
                v.clear();
                ++it;
                continue;
            }
        }
        // WO-210 D.3 (mp_copy_ready): the host NPC's combat mode as its newest state carries it, held on the copy at once -- its draw and its
        // stance come with the host's (WO-209: a copy's first swings came while still out of combat, the host's npc-combat report 1.5-2 s
        // later); the host's NPC out of combat lets go of the hold this set (the mirror's own hold on a copy in the joiner's fight stays)
        if (wo210::on(wo210rules::kBitCopyReady) && !wo172::copy_dead(eid) && !wo210kd::held_down(eid)) {
            const CopyState& nw = v.back();
            if (nw.cm & wo210rules::kCmCombat) {
                motion::State2 st{};
                st.bits = motion::kBitCombat;
                motion::set_npc_guard_hold(eid, true, &st, npcdrive::now_s());
                if (g_readyHeld.insert(eid).second) c_readyHolds.fetch_add(1);
            } else if (g_readyHeld.erase(eid) && !wo209::mirror_holds_player(eid)) {
                motion::set_npc_guard_hold(eid, false, nullptr, npcdrive::now_s());
                c_readyReleases.fetch_add(1);
            }
        }
        // WO-209 B: the host's NPC's attack at the joiner's figure starts the copy's own attack as soon as its state arrives -- its blow is
        // the joiner's engine's to judge, and the host holds its own verdict at most 450 ms (the stream delay below would spend most of it)
        {
            const CopyState& nw = v.back();
            const uint8_t* fb = nullptr;
            for (int k = 0; k < nw.n; ++k) if (nw.sc[k][0] == 0) fb = nw.sc[k];
            if (fb && !wo210kd::held_down(eid)) {   // WO-210 C.3: never an attack from a copy that lies
                uint16_t frag = 0; std::memcpy(&frag, fb + 2, 2);
                uint32_t act = 0; std::memcpy(&act, fb + 30, 4);
                if (wo209::attack_ai_wants(eid, fb[1], frag)) {
                    auto ins0 = g_owned.try_emplace(eid);
                    OwnCopy& oc0 = ins0.first->second;
                    if (ins0.second) { oc0.name = nm ? nm : "?"; c_oCopies.fetch_add(1); oc0.lastState = now; }
                    // one host attack, one copy attack: the row path usually started it already (the row comes before the state) -- the
                    // host action is marked as the copy's then too, or its state still showing it 0.6 s on asked for it again (d6-defence:
                    // a combo's 897 started twice, every later swing of the combo a second behind the host's)
                    const bool fresh = oc0.aiHostAct != act;
                    oc0.aiHostAct = act;
                    // and only while no attack of the copy's runs: a combo finisher's host action changes inside one fragment (d7: a
                    // second 937 started over the row's and cut it at 263 ms, its blow never landing)
                    if (fresh && !wo209::ai_attack_recent(eid, 0.6) && !wo209::ai_attack_running(eid)) {
                        KCDMP_FAULT_CALL(site0, "wo208::own_ai_attack_early");
                        fault::guarded(site0, [&] {
                            if (oc0.own[0].act) own_retire(oc0, oc0.own[0], now, "the host's attack (the copy's own)");
                            wo209::ai_attack(eid, frag);
                        });
                    }
                }
            }
        }
        // the newest state at least one stream delay old
        // WO-213 E.1: the owner applies a state the same delay after its arrival as the copy's position is played out (the old 0.38 s
        // when the play-out is off or not known yet)
        double ownerDelay = owner_delay_for(eid);   // KUBA: the anim phase (WO-213 E.1 below overrides it when on)
        if (wo213::on(wo213rules::kBitPlayoutAdaptive)) { const double pd = npcdrive::playout_delay_s(eid); if (pd > 0) ownerDelay = pd; }
        int pick = -1;
        for (int i = 0; i < static_cast<int>(v.size()); ++i) if (now - v[i].at >= ownerDelay) pick = i;
        if (pick < 0) { ++it; continue; }
        // WO-210 C.2: a dead copy is never driven again -- it lies as it died; the owner lets go of it once, without a hand-over
        if (wo210::on(wo210rules::kBitDeathEngine) && wo172::copy_dead(eid)) {
            const auto dw = g_owned.find(eid);
            if (dw != g_owned.end()) { own_release(eid, dw->second, now, "dead: it lies as it died", wo210rules::kPathDeath); g_owned.erase(dw); }
            v.clear();
            ++it;
            continue;
        }
        auto ins = g_owned.try_emplace(eid);
        OwnCopy& oc = ins.first->second;
        if (ins.second) { oc.name = nm ? nm : "?"; c_oCopies.fetch_add(1); }
        oc.lastState = now;
        oc.stateAt = v.back().at;   // WO-213 E.2: the newest state's arrival, applied or not yet
        const CopyState& s = v[pick];
        // WO-210 C.3 (mp_knockdown_sync): the host NPC's knockdown -- the copy falls on its down edge and is stood up on its up edge, the
        // engine's way; while it lies the engine's ragdoll has the body (the owner installs nothing, the position stream writes nothing)
        bool kdHeld = false;
        KCDMP_FAULT_CALL(siteKd, "wo208::kd_tick");
        fault::guarded(siteKd, [&] { kdHeld = wo210kd::copy_tick(eid, oc.name.c_str(), (v.back().cm & wo210rules::kCmDown) != 0, now); });   // the newest word: a fall starts at once
        if (kdHeld) {
            { auto kh = g_owned.find(eid); if (kh != g_owned.end()) wo217::place_note(kh->second.ep217, wo217::Note::Held, "knockdown"); }
            if (pick > 0) v.erase(v.begin(), v.begin() + pick);
            ++it;
            continue;
        }
        HostScope hsc[2]{}; bool have[2]{false, false};
        for (int k = 0; k < s.n; ++k) {
            const uint8_t* p = s.sc[k];
            const int which = p[0] == 0 ? 0 : p[0] == 1 ? 1 : -1;
            if (which < 0) continue;
            HostScope& h = hsc[which];
            h.cls = p[1];
            uint16_t frag = 0; std::memcpy(&frag, p + 2, 2); h.frag = frag;
            h.prio = static_cast<int8_t>(p[4]); h.status = p[5];
            std::memcpy(&h.flags, p + 6, 4); std::memcpy(h.tags, p + 10, 20);
            uint32_t act = 0; std::memcpy(&act, p + 30, 4); h.act = act;
            have[which] = true;
        }
        // WO-210 A: the host's current FullBody for the hand-over; its standing idle teaches this table's neutral idle
        if (have[0]) { oc.host = hsc[0]; oc.hostHave = true; learn_idle(eid, hsc[0]); }
        KCDMP_FAULT_CALL(site, "wo208::own_apply");
        if (!fault::guarded(site, [&] { own_apply(eid, oc, s.cm & wo210rules::kCmCombat, hsc, have, now); })) {
            logf("WO208-OWN apply FAULTED npc=%s", oc.name.c_str());
            own_handover(eid, oc, now, wo210rules::kPathFault);   // A.2 path 13: a fault inside the owner hands the host's state over
        }
        if (pick > 0) v.erase(v.begin(), v.begin() + pick);
        ++it;
    }
    // a copy the stream has not spoken of for 3 s is let go (its own engine and the old writers again)
    for (auto it = g_owned.begin(); it != g_owned.end();) {
        if (now - it->second.lastState > 3.0 || !engine::entity_by_id(it->first)) { own_release(it->first, it->second, now, "the host's stream is quiet", wo210rules::kPathFar); it = g_owned.erase(it); }
        else ++it;
    }
}

// ---- F.1 the invariant behind the joiner's fight crash: no copy has the local player as its opponent outside his fight ------------
// Every 0.5 s, every copy this DLL drives: its combat model's opponent read (read-only), and when it is the local player, the engine's own
// relation test. A copy with him as its opponent outside his skirmish is the state the crash came from (wo208_rules.h F.1); counted and
// told (WO208-TARGET violation). The twin (mp_target_in_fight off) lets WO-187 write him back after WO-132's release: violations > 0.
std::atomic<uint32_t> c_violations{0}, c_violationBodies{0};
void target_watch_tick() {
    static double s_at = 0;
    const double now = qpc_s();
    if (now - s_at < 0.5) return;
    s_at = now;
    void* pe = engine::entity_by_id(kPlayerEntity);
    const uint32_t me = pe ? engine::entity_id(pe) : 0;
    void* ps = me ? hits::soul_of_eid(me) : nullptr;
    if (!me || !ps) return;
    static std::vector<uint32_t> s_seen;   // the copies told once this stretch
    npcdrive::BoundBody bb[160];
    const int n = npcdrive::bound_bodies(bb, 160);
    std::vector<uint32_t> now_bad;
    for (int i = 0; i < n; ++i) {
        if (motion::is_avatar_eid(bb[i].eid)) continue;
        motion::ModelRead m{};
        if (!motion::read_model(bb[i].eid, &m) || !m.hasModel || m.opponentEid != me) continue;
        void* cs = hits::soul_of_eid(bb[i].eid);
        bool answered = false, hostile = false;
        if (cs) hits::skirmish_hostile(cs, ps, &answered, &hostile);
        const bool held = wo132::engaged(bb[i].eid);
        if (held && answered && hostile) continue;
        now_bad.push_back(bb[i].eid);
        const uint32_t k = c_violations.fetch_add(1) + 1;
        bool told = false;
        for (uint32_t e : s_seen) if (e == bb[i].eid) told = true;
        if (!told) {
            c_violationBodies.fetch_add(1);
            void* ent = engine::entity_by_id(bb[i].eid);
            const char* nm = ent ? engine::entity_name(ent) : nullptr;
            logf("WO208-TARGET violation npc=%s: its opponent is the local player outside his fight (%s, combat_mode=%u) [n=%u]", nm ? nm : "?",
                 !held ? "not held in his fight by the mod (WO-132)" : answered ? "the engine's relation test" : "unanswered", m.combatMode, k);
        }
    }
    s_seen.swap(now_bad);
}

void probe_tick() {
    fault::guarded_silent([] { target_watch_tick(); });
    fault::guarded_silent([] { kuba_remote_tick(); });   // KUBA 32 (host): the partners' duels on their NPCs
    { const double t = qpc_s(); fault::guarded_silent([t] { owner_tick(t); }); }   // A.2
    { const double t = qpc_s(); fault::guarded_silent([t] { late_tick(t); }); }    // the late stop
    if (!g_p.tried) probe_init();
    if (!g_p.host && !g_p.joiner) return;
    const double now = qpc_s();
    probe_name_tick(now);
    if (g_p.host) { fault::guarded_silent([now] { probe_write(now); }); return; }
    probe_read();
    KCDMP_FAULT_CALL(site, "wo208::probe_apply");
    if (!fault::guarded(site, [now] { probe_apply(now); })) logf("WO208-OWN apply FAULTED");
    static double s_lastLog = 0;
    if (now - s_lastLog > 30.0) {
        s_lastLog = now;
        logf("WO208-OWN status npc=%s eid=%u samples_in=%u applied=%u installs=%u refusals=%u stale=%u withdraws=%u steps=%u base_pulses=%u combat_holds=%u retired=%zu",
             g_p.name.c_str(), g_p.eid, g_p.samplesIn, g_p.applied, g_p.installs, g_p.refusals, g_p.stale, g_p.withdraws, g_p.steps, g_p.pulses, g_p.holds,
             g_p.retired.size());
    }
}

} // namespace

bool on(uint32_t bit) { return (g_bits.load(std::memory_order_relaxed) & ~twin_off() & bit) != 0; }

bool host_fullbody(uint32_t eid, uint8_t* cls, uint32_t* frag) {
    const auto it = g_owned.find(eid);
    if (it == g_owned.end() || !it->second.hostHave) return false;
    if (cls) *cls = it->second.host.cls;
    if (frag) *frag = it->second.host.frag;
    return true;
}

// WO-211 C.3 (main thread): the engine said this copy's action queue is over-full -- every action of ours on it withdrawn the engine's way
// (a pending one stopped, never installed; a running one stepped down), then one clean hand-over. 1 reseated, 2 not an owned copy,
// 3 too soon after the last.
bool posture_holds(uint32_t eid) { return g_postureHeld.count(eid) != 0; }   // WO-213 E.3
void w215_counters(uint32_t out[6], uint32_t* missingNow) {
    out[0] = c_w215Bases.load(); out[1] = c_w215Refused.load(); out[2] = c_w215RefusedRest.load(); out[3] = c_w215Dormant.load(); out[4] = c_w215StaleOurs.load();
    out[5] = c_w215NpcState.load();
    uint32_t m = 0;
    const double now = npcdrive::now_s();
    for (const auto& kv : g_owned) if (kv.second.c215Missing && now < kv.second.c215RefusedUntil + 5.0) ++m;
    if (missingNow) *missingNow = m;
}

uint8_t overflow_reseat(uint32_t eid, const char* name, double everyS) {
    auto it = eid ? g_owned.find(eid) : g_owned.end();
    if (it == g_owned.end()) { wo211::note_overflow_reseat(false, false); return 2; }
    OwnCopy& oc = it->second;
    const double now = npcdrive::now_s();
    if (now - oc.overflowAt < everyS) return 3;
    oc.overflowAt = now;
    for (auto& sc : oc.own) if (sc.act) own_retire(oc, sc, now, "the engine's queue is over-full");
    if (oc.baseAct) { rttr::stop_action(oc.baseAct); oc.retired.push_back({oc.baseAct, now}); oc.baseAct = nullptr; oc.baseFrag = 0; }
    const wo187::QueueInfo qi = wo187::queue_info(eid);
    const bool ok = own_handover(eid, oc, now, wo210rules::kPathOverflow);
    wo211::note_overflow_reseat(true, ok);
    logf("WO211-OVERFLOW npc=%s queue=%d first=%s: ours withdrawn, %s", name ? name : oc.name.c_str(), qi.n, qi.first,
         ok ? "a clean hand-over installed" : "nothing installed (the queue is still long, or no state)");
    return 1;
}

int owner_play_row(uint32_t eid, const char* spec) {
    const double now = npcdrive::now_s();
    auto it = g_owned.find(eid);
    if (it != g_owned.end() && it->second.own[0].act) own_retire(it->second, it->second.own[0], now, "the host's combat row");
    const rttr::SwingResult r = rttr::ghost_swing(eid, spec);
    if (r == rttr::SwingResult::Ok) {
        g_rowHoldUntil[eid] = now + kRowHoldS;
        const uint32_t k = c_rowPlayed.fetch_add(1, std::memory_order_relaxed) + 1;
        if (k <= 60 || k % 200 == 0) logf("WO216-ROW played npc=%s row \"%s\" through the owner (its full body held %.1f s) [n=%u]",
                                          it != g_owned.end() ? it->second.name.c_str() : "?", spec, kRowHoldS, k);
    } else {
        const uint32_t k = c_rowRefused.fetch_add(1, std::memory_order_relaxed) + 1;
        if (k <= 60 || k % 200 == 0) logf("WO216-ROW refused npc=%s row \"%s\" -> %s [n=%u]", it != g_owned.end() ? it->second.name.c_str() : "?",
                                          spec, rttr::swing_result_name(r), k);
    }
    return static_cast<int>(r);
}
int owner_rows_status(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "F.4/G owner_rows played=%u refused=%u held_frames=%u", c_rowPlayed.load(), c_rowRefused.load(),
                         c_rowHeldFrames.load());
}

// KUBA 30 stage 2, op 142 (the joiner): its copies in a local duel, as the host's NPCs are streamed (op 132) plus their position --
// every call, every duel body (the host drives its NPC from this). [count:1] then per body [nameLen:1][name][x:4f][y:4f][z:4f][yaw:4f]
// [cm:1][nsc:1] nsc x kWireScope. body: [op][max]
std::atomic<uint32_t> c_kubaDuelReads{0};
uint8_t op_kuba_duel_read(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    if (len != 2 || cap < 1) return wo163::kRBadRequest;
    const int max = body[1] ? body[1] : 1;
    size_t o = 1;
    int count = 0;
    for (uint32_t eid : g_kubaDuelEids) {
        if (count >= max) break;
        void* e = engine::entity_by_id(eid);
        const char* nm = e ? engine::entity_name(e) : nullptr;
        float p[3]{};
        if (!nm || !engine::entity_world_pos(e, p)) continue;
        const size_t nl = std::strlen(nm);
        if (nl == 0 || nl > 63) continue;
        const float yaw = npcdrive::entity_yaw(e);
        bool cm = false;
        motion::combat_mode_of(eid, &cm);
        wo187::ScopeInfo sc[10];
        const int n = wo187::read_all_scopes(eid, sc, 10);
        uint8_t rec[2 * kWireScope];
        uint64_t sig = 0;
        int nsc = 0;
        for (int i = 0; i < n && nsc < 2; ++i) {
            const uint8_t which = !std::strcmp(sc[i].name, "FullBody") ? 0 : !std::strcmp(sc[i].name, "UpperBody") ? 1 : 255;
            if (which == 255) continue;
            if (put_scope(sc[i], which, rec + nsc * kWireScope, &sig)) ++nsc;
        }
        const size_t need = 1 + nl + 16 + 2 + static_cast<size_t>(nsc) * kWireScope + kKubaDuelExt;
        if (o + need > cap) break;
        out[o] = static_cast<uint8_t>(nl); std::memcpy(out + o + 1, nm, nl); o += 1 + nl;
        std::memcpy(out + o, p, 12); std::memcpy(out + o + 12, &yaw, 4); o += 16;
        out[o] = static_cast<uint8_t>(cm ? wo210rules::kCmCombat : 0); out[o + 1] = static_cast<uint8_t>(nsc); o += 2;
        std::memcpy(out + o, rec, static_cast<size_t>(nsc) * kWireScope); o += static_cast<size_t>(nsc) * kWireScope;
        // KUBA 32: the copy's own guard -- its block held, its guard and attack zones (wire: table id + 1, 0 = none), as its brain has them
        motion::NpcCombat nc{};
        const bool ncOk = motion::read_npc_combat(eid, &nc) && nc.hasCa;
        out[o] = static_cast<uint8_t>(ncOk && nc.block ? 1 : 0);
        out[o + 1] = static_cast<uint8_t>(ncOk && nc.guardZone >= 0 ? nc.guardZone + 1 : 0);
        out[o + 2] = static_cast<uint8_t>(ncOk && nc.guardStance >= 0 ? nc.guardStance + 1 : 0);
        out[o + 3] = static_cast<uint8_t>(ncOk && nc.atkZone >= 0 ? nc.atkZone + 1 : 0);
        out[o + 4] = 0xFF; out[o + 5] = 0;
        o += kKubaDuelExt;
        ++count;
    }
    c_kubaDuelReads.fetch_add(1);
    out[0] = static_cast<uint8_t>(count);
    *outLen = o;
    return wo163::kROk;
}

// KUBA 30 stage 2, op 143 (the host): its NPC <name> is in a partner's local duel (on) or no more (off) -- the owner drives it from the
// partner's states (op 133, any distance), the swing guard and the block roll leave it alone. body: [op][on:1][nameLen:1][name] ->
// [eid:4] (0 = no such NPC here)
// ---- KUBA 32 (host): the brain goes where the fight is --------------------------------------------------------------------------
// Kuba, 19.11 ("not two brains beating each other in a random sequence: one fight, the same on both screens, like a normal game"): an
// NPC fighting only a partner has ONE brain -- his copy's, on his machine (KUBA 30, no lag for the player who fights). This world's NPC
// is that brain's body here: its own combat automation off (it neither swings nor blocks on its own -- the writer's engagement hold, the
// figures' own path), its brain NOT paused (its target, its weapon, its senses stay), its position the partner's copy's (the native
// writer, bound by the agent), its combat mode, guard zone and block his copy's (State2 through the engagement hold), and every move his
// copy's brain starts -- an attack, a combo's next swing, a feint, a dodge -- installed here as the very same fragment with the same tags
// and priority, one render delay after it arrived (the delay the position is shown with, so move and place stay together). Hit and
// block reactions are not copied: the partner's blows come with his engine's verdict and the host's replay plays them (KUBA 30 stage 2).
// The host player's own blow on such an NPC, or its brain turning to him, takes it back at once: automation on, its target the host
// player the engine's way, the agent unbinds it and tells the partner (W203 kind 13) to end his duel.
//   KCDMP_KUBA_REMOTE_REPLAY=off: the partner's moves are not installed (position and guard only).
namespace {
struct KubaRState { double at = 0; uint8_t cm = 0, flags = 0, gz = 0, gs = 0, az = 0; bool haveFb = false; uint8_t fb[kWireScope]{}; };
struct KubaRemote {
    std::string name;
    uint8_t src = 0;
    uint32_t figEid = 0;                 // KUBA 33: the partner's figure here (held in combat mode, the NPC's target)
    uint32_t retargets = 0;
    double startAt = 0, lastFrameAt = 0, logAt = 0, oppAt = 0, tgtAt = 0;
    std::vector<KubaRState> q;
    uint32_t lastPartnerAct = 0;
    void* ours = nullptr; uint32_t oursFrag = 0;
    std::vector<Probe::Retired> retired;
    uint32_t installs = 0, refusals = 0, skipped = 0, frames = 0, blockEdges = 0;
    bool blockWas = false, haveState = false;
    KubaRState last{};
};
std::unordered_map<uint32_t, KubaRemote> g_kubaRemote;   // main thread
std::vector<std::string> g_kubaTaken;                     // main thread: NPCs taken back here, for the agent (op 153's answer)
std::mutex g_kubaHostBlowMx;
std::vector<uint32_t> g_kubaHostBlows;                    // any thread -> main thread
std::unordered_set<uint32_t> g_kubaReleased;              // joiner, main thread: copies the host took back
std::atomic<uint32_t> c_kubaRInstalls{0}, c_kubaRRefusals{0}, c_kubaRTaken{0}, c_kubaRFrames{0};

bool kuba_replay_on() {
    static int s = -1;
    if (s < 0) {
        const std::string v = env("KCDMP_KUBA_REMOTE_REPLAY");
        s = (v == "off" || v == "OFF" || v == "Off" || v == "0") ? 0 : 1;
        logf("KUBA-32 a partner's duel copy's own moves are installed on this world's NPC %s (KCDMP_KUBA_REMOTE_REPLAY=off turns it off)", s ? "on" : "OFF");
    }
    return s == 1;
}

void kuba_retire_ours(uint32_t eid, KubaRemote& r, double now) {
    if (!r.ours) return;
    uint32_t st = 0;
    if (rd(r.ours, 0x28, &st) && st < 2) rttr::stop_action(r.ours);   // still waiting: never starts
    else if (st < 4) wr_prio(r.ours, kWithdrawnPriority);              // running: the next move takes the body now (a combo's next swing)
    r.retired.push_back({r.ours, now});
    r.ours = nullptr; r.oursFrag = 0;
    (void)eid;
}

// The NPC is this world's own again. aggroHost: its target becomes the host player (his blow, or its brain's own turn to him).
void kuba_remote_end(uint32_t eid, const char* why, bool aggroHost) {
    auto it = g_kubaRemote.find(eid);
    const bool alive = engine::entity_by_id(eid) != nullptr;
    std::string name = it != g_kubaRemote.end() ? it->second.name : std::string("?");
    double held = it != g_kubaRemote.end() ? qpc_s() - it->second.startAt : 0;
    uint32_t inst = 0, refused = 0, skipped = 0, frames = 0, figEid = 0, retargets = 0;
    if (it != g_kubaRemote.end()) {
        KubaRemote& r = it->second;
        inst = r.installs; refused = r.refusals; skipped = r.skipped; frames = r.frames; figEid = r.figEid; retargets = r.retargets;
        motion::kuba_hold_figure_combat(r.figEid, false);   // KUBA 33
        if (alive) {
            if (r.ours) late_take(eid, r.ours);   // a swing in flight finishes as it is
            for (auto& x : r.retired) late_take(eid, x.act);
        }
        r.ours = nullptr; r.retired.clear();
        g_kubaRemote.erase(it);
    }
    g_kubaRemoteDuel.erase(eid);
    g_copyStates.erase(eid);
    motion::kuba_set_combat_feet(eid, false);
    // its own combat AI back BEFORE the hold goes (the hold's release clears the guard request; the AI holds the fight itself then -- no
    // sheathe and draw again), then the hold
    bool autoOn = false, target = false;
    if (alive) {
        void* ca = nullptr; void* model = nullptr;
        if (motion::combat_parts(eid, false, &ca, &model)) {
            autoOn = motion::kuba_npc_automation(eid, true);
            if (aggroHost) {
                // KUBA 33c: pinned to him the game's way too (the brain's own pick does not take him back from the figure)
                target = kuba_aggro_to(eid, kPlayerEntity, "the host player took it from the partner's duel");
            } else if (figEid) {
                // KUBA 33: the partner's duel over (his copy's brain is back in the stream) -- its own brain takes the same fight on, with him
                void* fe = engine::entity_by_id(figEid);
                target = fe && wo187::set_logical_target(ca, fe);
            }
        }
    }
    kuba_duel_mind(eid, false, "host");   // KUBA 33
    motion::set_npc_engage(eid, false, nullptr, 0);
    if (aggroHost) { g_kubaTaken.push_back(name); c_kubaRTaken.fetch_add(1); }
    logf("KUBA-32 host duel end npc=%s eid=0x%X after %.1f s: %s -- its own brain again (automation %s%s); %u moves of the partner's copy shown, "
         "%u refused, %u not shown, %u frames, %u re-target(s)", name.c_str(), eid, held, why, autoOn ? "on" : "NOT given back",
         aggroHost ? (target ? ", its target the host player" : ", the host player NOT set as its target")
                   : (figEid ? (target ? ", its target the partner's figure" : ", the partner's figure NOT set as its target") : ""),
         inst, refused, skipped, frames, retargets);
}
}  // namespace (KUBA 32)

uint8_t op_kuba_remote_duel(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (len < 4 || cap < 4) return wo163::kRBadRequest;
    const bool on = body[1] != 0;
    const size_t nl = body[2];
    if (nl == 0 || nl > 63 || (3 + nl != len && 4 + nl != len)) return wo163::kRBadRequest;
    const std::string name(reinterpret_cast<const char*>(body + 3), nl);
    const uint8_t src = 4 + nl == len ? body[3 + nl] : 0xFF;   // KUBA 32: the partner whose copy fights it
    void* e = npcdrive::entity_by_name(name.c_str());
    const uint32_t eid = e ? engine::entity_id(e) : 0;
    const char* how = !eid ? "no such NPC here" : on ? "the partner's brain fights with it: driven from his stream" : "its own again";
    if (eid) {
        if (on) {
            g_kubaRemoteDuel.insert(eid);
            auto ins = g_kubaRemote.try_emplace(eid);
            KubaRemote& r = ins.first->second;
            if (ins.second) {
                r.name = name; r.src = src; r.startAt = qpc_s(); r.lastFrameAt = r.startAt;
                // its fight is with the partner's figure here, the engine's way (the brain keeps whatever else it knows)
                bool target = false;
                if (src != 0xFF) {
                    char fig[24]; std::snprintf(fig, sizeof fig, "kcd2mp_%u", static_cast<unsigned>(src));
                    void* fe = npcdrive::entity_by_name(fig);
                    r.figEid = fe ? engine::entity_id(fe) : 0;
                    motion::kuba_hold_figure_combat(r.figEid, true);   // KUBA 33: his figure is never "out of combat" for its brain
                    void* ca = nullptr; void* model = nullptr;
                    if (fe && motion::combat_parts(eid, true, &ca, &model)) target = wo187::set_logical_target(ca, fe);
                }
                r.tgtAt = qpc_s();
                kuba_duel_mind(eid, true, "host");   // KUBA 33: no flight, no crime seen, while the partner's brain fights with it
                kuba_replay_on();
                motion::kuba_set_combat_feet(eid, true);   // KUBA 16's feet: its footwork steps with the written place
                logf("KUBA-32 host duel start npc=%s eid=0x%X partner=%u -- his copy's brain fights; here its moves, guard and place%s",
                     name.c_str(), eid, static_cast<unsigned>(src), target ? " (its target: his figure)" : "");
            }
        } else if (g_kubaRemote.count(eid)) {
            kuba_remote_end(eid, "the partner's duel frames ended", false);
            how = "its own again";
        } else {
            g_kubaRemoteDuel.erase(eid);
            g_copyStates.erase(eid);
            auto it = g_owned.find(eid);
            if (it != g_owned.end()) { own_release(eid, it->second, qpc_s(), "KUBA-30: the partner's duel ended", wo210rules::kPathSwitch); g_owned.erase(it); }
        }
    }
    logf("KUBA-30 remote duel %s npc=%s eid=0x%X -- %s", on ? "on" : "off", name.c_str(), eid, how);
    std::memcpy(out, &eid, 4);
    *outLen = 4;
    return wo163::kROk;
}

// op 153 (the host): one partner's duel frame, as op 150 read it on his machine (after [op]: [count:1] per body [nameLen:1][name][x][y][z]
// [yaw][cm:1][nsc:1] nsc x kWireScope [ext: kKubaDuelExt]) -> [n:1] n x ([nameLen:1][name]) = the NPCs taken back here since the last call
uint8_t op_kuba_remote_frame(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (len < 2 || cap < 1) return wo163::kRBadRequest;
    const double now = qpc_s();
    const int n = body[1];
    size_t o = 2;
    for (int i = 0; i < n; ++i) {
        if (o + 1 > len) return wo163::kRBadRequest;
        const size_t nl = body[o];
        if (nl == 0 || nl > 63 || o + 1 + nl + 18 > len) return wo163::kRBadRequest;
        const std::string name(reinterpret_cast<const char*>(body + o + 1), nl);
        o += 1 + nl + 16;
        KubaRState s;
        s.at = now; s.cm = body[o];
        const int nsc = body[o + 1];
        o += 2;
        if (nsc > 2 || o + static_cast<size_t>(nsc) * kWireScope + kKubaDuelExt > len) return wo163::kRBadRequest;
        for (int k = 0; k < nsc; ++k) {
            const uint8_t* sc = body + o + static_cast<size_t>(k) * kWireScope;
            if (sc[0] == 0) { s.haveFb = true; std::memcpy(s.fb, sc, kWireScope); }
        }
        o += static_cast<size_t>(nsc) * kWireScope;
        s.flags = body[o]; s.gz = body[o + 1]; s.gs = body[o + 2]; s.az = body[o + 3];
        o += kKubaDuelExt;
        void* e = npcdrive::entity_by_name(name.c_str());
        const uint32_t eid = e ? engine::entity_id(e) : 0;
        auto it = eid ? g_kubaRemote.find(eid) : g_kubaRemote.end();
        if (it == g_kubaRemote.end()) continue;   // not (or no longer) driven by a partner here
        KubaRemote& r = it->second;
        r.q.push_back(s);
        if (r.q.size() > 64) r.q.erase(r.q.begin(), r.q.begin() + static_cast<std::ptrdiff_t>(r.q.size() - 64));
        r.lastFrameAt = now;
        ++r.frames;
        c_kubaRFrames.fetch_add(1);
    }
    size_t w = 1, told = 0;
    while (!g_kubaTaken.empty()) {
        const std::string& nm = g_kubaTaken.front();
        const size_t l = nm.size() > 63 ? 63 : nm.size();
        if (w + 1 + l > cap) break;
        out[w] = static_cast<uint8_t>(l); std::memcpy(out + w + 1, nm.data(), l);
        w += 1 + l; ++told;
        g_kubaTaken.erase(g_kubaTaken.begin());
    }
    out[0] = static_cast<uint8_t>(told);
    *outLen = w;
    return wo163::kROk;
}

// op 154 (the joiner): [op][nameLen][name] -- the host took this NPC back (its player's aggro): the duel with its copy ends now
uint8_t op_kuba_duel_release(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (len < 3) return wo163::kRBadRequest;
    const size_t nl = body[1];
    if (nl == 0 || nl > 63 || 2 + nl != len) return wo163::kRBadRequest;
    const std::string name(reinterpret_cast<const char*>(body + 2), nl);
    uint32_t hit = 0;
    for (uint32_t eid : g_kubaDuelEids) {
        void* e = engine::entity_by_id(eid);
        const char* nm = e ? engine::entity_name(e) : nullptr;
        if (nm && _stricmp(nm, name.c_str()) == 0) { hit = eid; break; }
    }
    if (hit) g_kubaReleased.insert(hit);
    logf("KUBA-32 the host took npc=%s back (its player's aggro) -- %s", name.c_str(), hit ? "its duel here ends now" : "no duel with it here");
    if (cap >= 1) { out[0] = hit ? 1 : 0; *outLen = 1; }
    return wo163::kROk;
}

void kuba_note_host_blow(uint32_t victimEid) {
    if (!victimEid) return;
    std::lock_guard<std::mutex> lock(g_kubaHostBlowMx);
    if (g_kubaHostBlows.size() < 64) g_kubaHostBlows.push_back(victimEid);
}

bool kuba_release_take(uint32_t eid) {
    if (g_kubaReleased.empty()) return false;
    return g_kubaReleased.erase(eid) != 0;
}

void kuba_remote_tick() {
    std::vector<uint32_t> blows;
    {
        std::lock_guard<std::mutex> lock(g_kubaHostBlowMx);
        blows.swap(g_kubaHostBlows);
    }
    kuba_aggro_tick(qpc_s());   // KUBA 33c
    for (uint32_t v : blows) {
        if (g_kubaRemote.count(v)) { kuba_remote_end(v, "the host player's blow landed on it", true); continue; }
        kuba_host_blow_free(v);   // KUBA 33c: an NPC fighting a partner's figure outside a duel
    }
    if (g_kubaRemote.empty()) return;
    const double now = qpc_s();
    const double dnow = npcdrive::now_s();
    std::vector<std::pair<uint32_t, const char*>> ends;
    for (auto& kv : g_kubaRemote) {
        const uint32_t eid = kv.first;
        KubaRemote& r = kv.second;
        if (!engine::entity_by_id(eid)) { ends.push_back({eid, "the NPC is gone"}); continue; }
        // its brain turned to the host player on its own (it saw him, his arrow, ...): his fight now
        if (now - r.oppAt >= 0.25) {
            r.oppAt = now;
            if (wo195::dead_gate(eid, "kuba-duel")) { ends.push_back({eid, "the NPC is dead"}); continue; }   // the game's own death has it
            motion::NpcCombat nc{};
            if (motion::read_npc_combat(eid, &nc) && nc.opponentIsPlayer) { ends.push_back({eid, "its own brain turned to the host player"}); continue; }
            // KUBA 33: its brain lost the partner's figure as its opponent (it ended its fight, or picked nobody): the figure again, at most
            // once a second -- at the duel's end its own brain goes on with the fight it is in
            if (r.figEid && nc.opponentEid != r.figEid && now - r.tgtAt >= 1.0) {
                r.tgtAt = now;
                void* fe = engine::entity_by_id(r.figEid);
                void* ca = nullptr; void* model = nullptr;
                if (fe && motion::combat_parts(eid, false, &ca, &model) && wo187::set_logical_target(ca, fe)) {
                    const uint32_t k = ++r.retargets;
                    if (k <= 5 || k % 20 == 0)
                        logf("KUBA-33 host duel npc=%s its brain had lost the partner's figure (opponent %s) -- his figure is its target again [n=%u]",
                             r.name.c_str(), nc.opponentEid ? "another" : "none", k);
                }
            }
        }
        // the newest state at least one render delay old: the move starts where the written position shows that moment
        double delay = kBindDelayS;
        npcdrive::render_lag_s(eid, &delay);
        int pick = -1;
        for (int i = 0; i < static_cast<int>(r.q.size()); ++i) if (now - r.q[i].at >= delay) pick = i;
        if (pick >= 0) { r.last = r.q[pick]; r.haveState = true; }
        if (r.haveState) {
            // the partner copy's guard, every frame (the engagement hold: combat mode, automation off, guard / attack zone, block)
            motion::State2 st{};
            st.bits = static_cast<uint8_t>(motion::kBitCombat | ((r.last.flags & 1) ? motion::kBitBlock : 0));
            st.guardZone = r.last.gz; st.guardStance = r.last.gs; st.atkZone = r.last.az;
            motion::set_npc_engage(eid, true, &st, dnow);
            const bool blk = (r.last.flags & 1) != 0;
            if (blk != r.blockWas) { r.blockWas = blk; ++r.blockEdges; }
        }
        if (pick >= 0) {
            const KubaRState& s = r.q[pick];
            if (s.haveFb && kuba_replay_on()) {
                const uint8_t* p = s.fb;
                uint32_t act = 0; std::memcpy(&act, p + 30, 4);
                if (act != r.lastPartnerAct) {
                    r.lastPartnerAct = act;
                    const uint8_t cls = p[1];
                    uint16_t frag = 0; std::memcpy(&frag, p + 2, 2);
                    const int32_t prio = static_cast<int8_t>(p[4]);
                    uint32_t flags = 0; std::memcpy(&flags, p + 6, 4);
                    const std::string fn = frag_name_of(eid, frag);
                    const bool baseCls = cls == kClsNone || cls == kClsCombatMove || cls == kClsAIMovement || cls == kClsMoveTransition || cls == kClsIdleState;
                    if (!baseCls && kuba_mirror_frag(fn.c_str())) {
                        kuba_retire_ours(eid, r, now);
                        void* a = nullptr;
                        const rttr::SwingResult res = rttr::install_fragment(eid, frag, p + 10, prio, flags, &a);
                        if (res == rttr::SwingResult::Ok && a) {
                            r.ours = a; r.oursFrag = frag; ++r.installs;
                            const uint32_t k = c_kubaRInstalls.fetch_add(1) + 1;
                            if (k <= 80 || k % 100 == 0)
                                logf("KUBA-32 host duel npc=%s shows the partner copy's %s (frag %u, %s, prio %d) %.0f ms after it arrived [n=%u]", r.name.c_str(),
                                     fn.c_str(), frag, cls_name(cls), prio, (now - s.at) * 1000.0, k);
                        } else {
                            ++r.refusals;
                            const uint32_t k = c_kubaRRefusals.fetch_add(1) + 1;
                            if (k <= 40 || k % 100 == 0)
                                logf("KUBA-32 host duel npc=%s the partner copy's %s (frag %u) refused here: %s [n=%u]", r.name.c_str(), fn.c_str(), frag,
                                     rttr::swing_result_name(res), k);
                        }
                    } else if (!baseCls) {
                        ++r.skipped;
                        if (r.skipped <= 12) logf("KUBA-32 host duel npc=%s the partner copy's %s/%s left to this NPC (a reaction or its base)", r.name.c_str(),
                                                  cls_name(cls), fn.empty() ? "?" : fn.c_str());
                    }
                }
            }
            if (pick > 0) r.q.erase(r.q.begin(), r.q.begin() + pick);
        }
        for (size_t i = 0; i < r.retired.size();) {
            uint32_t st = 0;
            if (!rd(r.retired[i].act, 0x28, &st) || st >= 4 || now - r.retired[i].at > 5.0) {
                late_take(eid, r.retired[i].act);
                r.retired.erase(r.retired.begin() + static_cast<std::ptrdiff_t>(i));
            } else ++i;
        }
        if (now - r.logAt >= 2.0) {
            r.logAt = now;
            motion::NpcCombat nc{};
            motion::read_npc_combat(eid, &nc);
            void* oe = nc.opponentEid ? engine::entity_by_id(nc.opponentEid) : nullptr;
            const char* on = oe ? engine::entity_name(oe) : nullptr;
            logf("KUBA-32 host duel npc=%s %.1f s: frames=%u shown=%u refused=%u not_shown=%u block_edges=%u combat=%u block=%u opponent=%s last_frame=%.0f ms ago delay=%.0f ms",
                 r.name.c_str(), now - r.startAt, r.frames, r.installs, r.refusals, r.skipped, r.blockEdges, nc.combat, nc.block, on ? on : "none",
                 (now - r.lastFrameAt) * 1000.0, delay * 1000.0);
        }
    }
    for (auto& x : ends) kuba_remote_end(x.first, x.second, std::strcmp(x.second, "its own brain turned to the host player") == 0);
}

void kuba_push_ask(uint8_t kind, const std::string& name) {
    if (g_asks.size() < 64) g_asks.push_back({kind, name});
}
void kuba_set_duel(uint32_t eid, bool on) {
    if (on) g_kubaDuelEids.insert(eid); else g_kubaDuelEids.erase(eid);
}
bool kuba_in_duel(uint32_t eid) { return eid && !g_kubaDuelEids.empty() && g_kubaDuelEids.count(eid) != 0; }

// KUBA 33 (20.11, "after the brain switch the enemies ran away instead of fighting, and when the brain switched back they ran at me and
// attacked, then it switched and they ran away, round and round"): the brain that took a duel over is a whole NPC brain, and it judged
// the scene on its own -- a bandit next to his mates' bodies saw the player as a murderer (SoulFlee + "npc_obvinuje_hrace__vrazda", 0.3 s
// after its brain resumed on the joiner), a villager in a fistfight left it when the player had a weapon in hand (combat termination,
// weapon away) -- and the other machine's brain then ran back into the fight. The game's own per-soul switches (Tables :: ScriptContext.xml,
// Class="Entity", each read by the brain on itself) keep it in the fight it was given: on = set while the duel lasts, off = cleared while
// it lasts. Refcount-aware (sctx): only what this wrote is put back at the end. KCDMP_KUBA_DUEL_MIND=off.
namespace {
struct MindCtx { const char* name; bool want; };
constexpr MindCtx kMind[] = {
    {"combat_neverSurrenderOrFlee", true},                  // no flee or surrender of its own (SKIRMISH_SOULFLEE)
    {"combat_alwaysJoinSkirmish", true},                    // it stays in the skirmish it is in
    {"crime_ignoreCorpses", true},                          // the bodies around a fight are no murder it saw
    {"crime_ignoreUnconsciousBodies", true},
    {"combat_ignoreMurdered", true},
    {"combat_ignoreMurderedByPlayer", true},
    {"combat_ignoreKnockedOutByPlayer", true},
    {"crime_ignoreNPCHitVolumes", true},                    // a blow on another NPC is no assault it witnessed
    {"crime_ignorePlayersDrawnWeapon", true},
    {"crime_disableReport", true},                          // never runs off to a guard
    {"crime_disableCrimeInformationEmit", true},
    {"switch_disabledInformationReaction", true},           // new crime information starts no behaviour (the witness's flight)
    {"combat_fleeOnDrawnWeaponInUnarmedCombat", false},     // a fistfight is not left for the player's drawn weapon
    {"combat_alwaysWithdrawSkirmish", false},
    {"combat_flee", false},
    {"combat_neverMercyAlwaysFlee", false},
};
constexpr int kMindN = static_cast<int>(sizeof(kMind) / sizeof(*kMind));
static_assert(kMindN <= 32, "the written set is a 32-bit mask");
std::unordered_map<uint32_t, uint32_t> g_kubaMind;   // main thread: eid -> the contexts this wrote
std::unordered_map<uint32_t, bool> g_kubaArmed;      // KUBA 37, main thread: eid -> combat_forceArmed written by this (false = already set)
std::atomic<bool> g_kubaPlayerArmed{false};          // KUBA 37: the agent's word (op 157)
bool kuba_armed_on() {
    static int s = -1;
    if (s < 0) {
        const std::string v = env("KCDMP_KUBA_DUEL_ARMED");
        s = (v == "off" || v == "OFF" || v == "Off" || v == "0") ? 0 : 1;
        logf("KUBA-37 a duel NPC fights armed while this player holds a weapon (the game's combat_forceArmed) %s (KCDMP_KUBA_DUEL_ARMED=off turns it off)",
             s ? "on" : "OFF");
    }
    return s == 1;
}
std::atomic<uint32_t> c_kubaMindSet{0}, c_kubaMindFail{0};
bool kuba_mind_on() {
    static int s = -1;
    if (s < 0) {
        const std::string v = env("KCDMP_KUBA_DUEL_MIND");
        s = (v == "off" || v == "OFF" || v == "Off" || v == "0") ? 0 : 1;
        logf("KUBA-33 a duel NPC's brain kept in its fight (no flight, no crime seen) %s (KCDMP_KUBA_DUEL_MIND=off turns it off)", s ? "on" : "OFF");
    }
    return s == 1;
}
}  // namespace

void kuba_duel_mind(uint32_t eid, bool on, const char* who) {
    if (!eid) return;
    if (on) {
        if (!kuba_mind_on() || g_kubaMind.count(eid)) return;
        void* soul = hits::soul_of_eid(eid);
        if (!soul) { logf("KUBA-33 npc eid=0x%X (%s): no soul -- its brain judges the scene on its own", eid, who); return; }
        uint32_t wrote = 0;
        int ok = 0, already = 0, bad = 0;
        std::string missing;
        for (int i = 0; i < kMindN; ++i) {
            const int r = sctx::set_soul_context_quiet(soul, kMind[i].name, kMind[i].want);
            if (r == 1) { wrote |= 1u << i; ++ok; }
            else if (r == 0) ++already;
            else { ++bad; if (missing.size() < 200) { missing += ' '; missing += kMind[i].name; } }
        }
        g_kubaMind[eid] = wrote;
        c_kubaMindSet.fetch_add(static_cast<uint32_t>(ok)); c_kubaMindFail.fetch_add(static_cast<uint32_t>(bad));
        void* e = engine::entity_by_id(eid);
        const char* nm = e ? engine::entity_name(e) : nullptr;
        logf("KUBA-33 npc=%s (%s) kept in its fight: %d switch(es) written, %d already so, %d failed%s%s", nm ? nm : "?", who, ok, already, bad,
             bad ? " --" : "", missing.c_str());
        return;
    }
    auto ai = g_kubaArmed.find(eid);   // KUBA 37: only what this wrote is cleared
    if (ai != g_kubaArmed.end()) {
        const bool wroteArmed = ai->second;
        g_kubaArmed.erase(ai);
        void* s = wroteArmed && engine::entity_by_id(eid) ? hits::soul_of_eid(eid) : nullptr;
        if (s) {
            const int r = sctx::set_soul_context_quiet(s, "combat_forceArmed", false);
            logf("KUBA-37 npc eid=0x%X (%s) duel over: combat_forceArmed cleared (%d)", eid, who, r);
        }
    }
    auto it = g_kubaMind.find(eid);
    if (it == g_kubaMind.end()) return;
    const uint32_t wrote = it->second;
    g_kubaMind.erase(it);
    void* soul = engine::entity_by_id(eid) ? hits::soul_of_eid(eid) : nullptr;
    if (!soul) return;   // gone: its contexts went with it
    int back = 0;
    for (int i = 0; i < kMindN; ++i)
        if (wrote & (1u << i)) { if (sctx::set_soul_context_quiet(soul, kMind[i].name, !kMind[i].want) >= 0) ++back; }
    logf("KUBA-33 npc eid=0x%X (%s) its own judgement again: %d switch(es) put back", eid, who, back);
}

// KUBA 33c (host, 20.11: "the aggro practically cannot be taken over: as player 2 I hit an NPC beating player 1 and it never turned to
// me, and the other way round"): the last player to hit an NPC is its opponent -- the engine's SetLogicalTarget and the game's own "fight
// this one" (combat_forcedTarget, the battles' and quest fights' lever), pinned to him until 30 s after his last blow, then the NPC's own
// judgement again. Player 2's blow arrives as his NpcDamage (the agent's op 155), the host player's through the hit hook. A pin from
// another player is lifted first. KCDMP_KUBA_AGGRO=off.
namespace {
struct KubaPin { uint32_t target = 0; bool wrote = false; double at = 0; };
std::unordered_map<uint32_t, KubaPin> g_kubaPins;   // main thread: NPC eid -> its pinned opponent (entity id)
std::unordered_map<uint32_t, double> g_kubaHostBlowAt;
std::atomic<uint32_t> c_kubaAggroSwitch{0};
constexpr double kKubaPinS = 30.0;
bool kuba_aggro_on() {
    static int s = -1;
    if (s < 0) {
        const std::string v = env("KCDMP_KUBA_AGGRO");
        s = (v == "off" || v == "OFF" || v == "Off" || v == "0") ? 0 : 1;
        logf("KUBA-33c the last player to hit an NPC takes its aggro %s (KCDMP_KUBA_AGGRO=off turns it off)", s ? "on" : "OFF");
    }
    return s == 1;
}
void kuba_unpin(uint32_t eid, KubaPin& p) {
    if (p.wrote) {
        void* soul = engine::entity_by_id(eid) ? hits::soul_of_eid(eid) : nullptr;
        void* ts = engine::entity_by_id(p.target) ? hits::soul_of_eid(p.target) : nullptr;
        if (soul && ts) sctx::set_soul_relation(soul, ts, "combat_forcedTarget", false);
    }
    p = KubaPin{};
}
}  // namespace

// The NPC `eid` fights `targetEid` now (an entity id: the host player or a partner's figure). True = it was set (or already so).
bool kuba_aggro_to(uint32_t eid, uint32_t targetEid, const char* why) {
    if (!eid || !targetEid || !kuba_aggro_on()) return false;
    void* e = engine::entity_by_id(eid);
    void* te = engine::entity_by_id(targetEid);
    if (!e || !te || wo195::dead_gate(eid, "kuba-aggro")) return false;
    const double now = qpc_s();
    KubaPin& p = g_kubaPins[eid];
    if (p.target == targetEid) { p.at = now; return true; }   // his already: the hold refreshed
    if (p.target) kuba_unpin(eid, p);
    void* ca = nullptr; void* model = nullptr;
    const bool slt = motion::combat_parts(eid, true, &ca, &model) && wo187::set_logical_target(ca, te);
    void* soul = hits::soul_of_eid(eid);
    void* ts = hits::soul_of_eid(targetEid);
    const int r = (soul && ts) ? sctx::set_soul_relation(soul, ts, "combat_forcedTarget", true) : -1;
    p.target = targetEid; p.wrote = r == 1; p.at = now;
    const uint32_t k = c_kubaAggroSwitch.fetch_add(1) + 1;
    const char* nm = engine::entity_name(e);
    const char* tn = engine::entity_name(te);
    if (k <= 100 || k % 50 == 0)
        logf("KUBA-33c npc=%s fights %s now (%s): logical target %s, forced target %s [n=%u]", nm ? nm : "?", tn ? tn : "?", why,
             slt ? "set" : "NOT set", r == 1 ? "pinned" : r == 0 ? "already" : "NOT pinned", k);
    return slt || r >= 0;
}

// An NPC of this world fighting a partner's figure (no duel of his yet, or his stream-driven fight) or pinned to him: the host player's
// blow takes it -- at most once a second per NPC. Main thread.
void kuba_host_blow_free(uint32_t v) {
    if (!wo178::is_host_role()) return;   // the joiner's own blows on copies are the host's to judge
    const double t = qpc_s();
    if (g_kubaHostBlowAt.size() > 256) g_kubaHostBlowAt.clear();
    double& at = g_kubaHostBlowAt[v];
    if (t - at < 1.0) return;
    at = t;
    motion::NpcCombat nc{};
    const bool fightsFigure = motion::read_npc_combat(v, &nc) && nc.opponentEid && !nc.opponentIsPlayer && motion::is_avatar_eid(nc.opponentEid);
    const auto pin = g_kubaPins.find(v);
    const bool pinnedElsewhere = pin != g_kubaPins.end() && pin->second.target && pin->second.target != kPlayerEntity;
    if (fightsFigure || pinnedElsewhere) kuba_aggro_to(v, kPlayerEntity, "the host player's blow landed on it");
}

// Main thread, every frame: pins 30 s after the last blow go, so do pins on NPCs gone or dead.
void kuba_aggro_tick(double now) {
    for (auto it = g_kubaPins.begin(); it != g_kubaPins.end();) {
        const bool gone = !engine::entity_by_id(it->first);
        if (!it->second.target || gone || now - it->second.at > kKubaPinS || wo195::dead_gate(it->first, "kuba-aggro")) {
            if (!gone) kuba_unpin(it->first, it->second);
            it = g_kubaPins.erase(it);
        } else ++it;
    }
}

// op 155 (the host): [op][src][nameLen][name] -- player `src`'s blow on this NPC landed (his NpcDamage, applied here): his figure is its
// opponent now -> [r:1] 1 switched (or already his), 2 a partner's duel has it, 0 nothing
uint8_t op_kuba_aggro(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (len < 4 || cap < 1) return wo163::kRBadRequest;
    const uint8_t src = body[1];
    const size_t nl = body[2];
    if (nl == 0 || nl > 63 || 3 + nl != len) return wo163::kRBadRequest;
    const std::string name(reinterpret_cast<const char*>(body + 3), nl);
    void* e = npcdrive::entity_by_name(name.c_str());
    const uint32_t eid = e ? engine::entity_id(e) : 0;
    uint8_t r = 0;
    if (eid && g_kubaRemote.count(eid)) r = 2;   // its brain is his copy's already
    else if (eid) {
        char fig[24]; std::snprintf(fig, sizeof fig, "kcd2mp_%u", static_cast<unsigned>(src));
        void* fe = npcdrive::entity_by_name(fig);
        const uint32_t feid = fe ? engine::entity_id(fe) : 0;
        if (feid && kuba_aggro_to(eid, feid, "the partner's blow landed on it")) r = 1;
    }
    out[0] = r; *outLen = 1;
    return wo163::kROk;
}
bool kuba_remote_duel(uint32_t eid) { return eid && !g_kubaRemoteDuel.empty() && g_kubaRemoteDuel.count(eid) != 0; }

// KUBA 34 (20.11: the host played on the original mod -- the joiner's copies fought with their own brain while the host's NPCs fought
// another fight, a quest's allies among them): the agent says when the host answered its hello (and when it stopped answering)
namespace {
std::atomic<bool> g_kubaHostOk{false};
bool kuba_host_check_on() {
    static int s = -1;
    if (s < 0) {
        const std::string v = env("KCDMP_KUBA_HOST_CHECK");
        s = (v == "off" || v == "OFF" || v == "Off" || v == "0") ? 0 : 1;
        logf("KUBA-34 a copy's brain fights here only with a host on this fork (it answered the hello) %s (KCDMP_KUBA_HOST_CHECK=off turns it off)",
             s ? "on" : "OFF");
    }
    return s == 1;
}
uint8_t op_kuba_host_ok(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (len < 2 || cap < 1) return wo163::kRBadRequest;
    const bool on = body[1] != 0;
    if (g_kubaHostOk.exchange(on) != on)
        logf("KUBA-34 the host %s -- a copy's own brain %s", on ? "runs this fork (it answered the hello)" : "no longer answers the hello (or another host)",
             on ? "may fight its duel here again" : "fights no new duel here (the stream drives every copy)");
    out[0] = on ? 1 : 0; *outLen = 1;
    return wo163::kROk;
}
uint8_t op_kuba_player_armed(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {   // KUBA 37
    *outLen = 0;
    if (len < 2 || cap < 1) return wo163::kRBadRequest;
    const bool on = body[1] != 0;
    if (g_kubaPlayerArmed.exchange(on) != on)
        logf("KUBA-37 this player %s", on ? "holds a weapon -- a duel NPC's brain fights armed" : "holds no weapon -- a new duel keeps the brain's own choice (fists against fists)");
    out[0] = on ? 1 : 0; *outLen = 1;
    return wo163::kROk;
}
}  // namespace
bool kuba_host_ok() { return !kuba_host_check_on() || g_kubaHostOk.load(); }

// KUBA 37 (joiner, main thread, the duel's tick): once this player holds a weapon during the duel, the copy's brain fights armed for the
// rest of it (the vanilla brain never goes back to its fists either)
void kuba_duel_armed_tick(uint32_t eid, const char* name) {
    if (!eid || !g_kubaPlayerArmed.load() || g_kubaArmed.count(eid) || !kuba_armed_on()) return;
    void* soul = hits::soul_of_eid(eid);
    if (!soul) return;
    const int r = sctx::set_soul_context_quiet(soul, "combat_forceArmed", true);
    g_kubaArmed[eid] = (r == 1);
    static int s_logs = 0;
    if (s_logs < 60) {
        ++s_logs;
        logf("KUBA-37 npc=%s this player holds a weapon: combat_forceArmed %s -- its brain draws and keeps its weapon", name ? name : "?",
             r == 1 ? "set" : r == 0 ? "already set" : "NOT possible");
    }
}

// KUBA 30 stage 2: the local engine's verdicts on the local player's blows on duel copies (the hit hook's thread -> the agent's op 152)
namespace { std::mutex g_kubaVerdictMx; std::unordered_map<uint32_t, std::pair<uint8_t, ULONGLONG>> g_kubaVerdicts; }
void kuba_note_duel_verdict(uint32_t eid, uint8_t outcome) {
    if (!eid || outcome == 0) return;
    std::lock_guard<std::mutex> lock(g_kubaVerdictMx);
    if (g_kubaVerdicts.size() > 64) g_kubaVerdicts.clear();
    g_kubaVerdicts[eid] = {outcome, GetTickCount64()};
}
// op 152 (the joiner's agent, before it sends a blow on a duel copy): [op][nameLen][name] -> [outcome:1] (0 = none in the last 1.5 s);
// the verdict is consumed
uint8_t op_kuba_take_verdict(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (len < 3 || cap < 1) return wo163::kRBadRequest;
    const size_t nl = body[1];
    if (nl == 0 || nl > 63 || 2 + nl != len) return wo163::kRBadRequest;
    const std::string name(reinterpret_cast<const char*>(body + 2), nl);
    uint8_t outcome = 0;
    for (uint32_t eid : g_kubaDuelEids) {
        void* e = engine::entity_by_id(eid);
        const char* nm = e ? engine::entity_name(e) : nullptr;
        if (!nm || _stricmp(nm, name.c_str()) != 0) continue;
        std::lock_guard<std::mutex> lock(g_kubaVerdictMx);
        auto it = g_kubaVerdicts.find(eid);
        if (it != g_kubaVerdicts.end()) {
            if (GetTickCount64() - it->second.second <= 1500) outcome = it->second.first;
            g_kubaVerdicts.erase(it);
        }
        break;
    }
    out[0] = outcome;
    *outLen = 1;
    return wo163::kROk;
}

bool probe_owned(uint32_t eid) {
    if (!eid) return false;
    if (eid == g_probeEid.load(std::memory_order_relaxed)) return true;
    return on(kBitOwner) && g_owned.count(eid) != 0;   // A.2: the owner's copies (main thread: every writer that asks runs there)
}

std::atomic<uint32_t> c_targetRefused{0}, c_fightHeals{0};
bool in_player_fight(uint32_t copy) {
    const bool sw = on(kBitTargetInFight);
    if (!sw) return true;
    if (kuba_in_duel(copy)) return true;   // KUBA 36: a duel copy fights him with its own brain
    void* pe = engine::entity_by_id(kPlayerEntity);
    void* cs = hits::soul_of_eid(copy);
    void* ps = pe ? hits::soul_of_eid(engine::entity_id(pe)) : nullptr;
    bool answered = false, hostile = false;
    if (cs && ps) hits::skirmish_hostile(cs, ps, &answered, &hostile);
    return target_player_allowed(sw, wo132::engaged(copy), answered, hostile);
}
bool target_player_ok(uint32_t copy, const char* site) {
    const bool sw = on(kBitTargetInFight);
    if (!sw) return true;
    // KUBA 36 (20.11 15:05: "NPCs fighting player 2 sometimes act as if they lost their brain"): a copy in this player's local duel
    // fights him with its own brain -- he is its target whatever the skirmish relation reads; the heal below (StopFight + every copy let
    // go of his fight) ended its fight and cleared the pins of the other duel copies: each left combat 5 s later and sheathed its weapon.
    if (kuba_in_duel(copy)) return true;
    void* pe = engine::entity_by_id(kPlayerEntity);
    void* cs = hits::soul_of_eid(copy);
    void* ps = pe ? hits::soul_of_eid(engine::entity_id(pe)) : nullptr;
    bool answered = false, hostile = false;
    if (cs && ps) hits::skirmish_hostile(cs, ps, &answered, &hostile);
    const bool held = wo132::engaged(copy);   // the mod's own word: WO-132 holds it in his fight (released: never written back)
    if (target_player_allowed(sw, held, answered, hostile)) return true;
    // WO-210 E.3 (fight_heal): held by the mod, out of the engine's skirmish relation. In 0.63.8 the joiner's own hits on a copy re-added
    // it through WO-136's leave-and-re-add (gone: E.6, a crash route), which also pulled a copy that drifted into another skirmish back
    // into his; an add alone does not move it. Healed the engine's way: its stale fight ended (StopFight on its soul, as WO-201's combat
    // end) and every copy's hold in his fight let go (WO-209's own recovery when he leaves his fight) -- the mirror engages each again.
    if (held && answered && !hostile && wo210::fix_on(wo210rules::kFixFightHeal)) {
        static std::unordered_map<uint32_t, wo210rules::HealTrack> s_heal;
        static double s_lastHeal = -1e9;
        const double now = npcdrive::now_s();
        if (wo210rules::heal_step(s_heal[copy], now, s_lastHeal)) {
            s_lastHeal = now;
            s_heal.clear();
            bool stopped = false;
            if (actions::stop_fight_available()) {
                void* c2 = buffs::as_c_soul(cs) ? buffs::as_c_soul(cs) : cs;
                stopped = actions::stop_fight(c2);
            }
            wo132::local_player_left_fight("a copy held in it was out of the engine's skirmish relation (WO-210 fight_heal)");
            const uint32_t k = c_fightHeals.fetch_add(1) + 1;
            void* e = engine::entity_by_id(copy);
            const char* nm = e ? engine::entity_name(e) : nullptr;
            logf("WO210-HEAL npc=%s out of the local player's skirmish relation for %.1f s: its fight ended the engine's way (stop_fight=%s), every "
                 "copy engaged again by the mirror [n=%u]", nm ? nm : "?", wo210rules::kHealAfterS, stopped ? "called" : "not available", k);
            return false;
        }
    }
    const uint32_t k = c_targetRefused.fetch_add(1) + 1;
    if (k <= 20 || k % 100 == 0) {
        void* e = engine::entity_by_id(copy);
        const char* nm = e ? engine::entity_name(e) : nullptr;
        logf("WO208-TARGET npc=%s is not in a skirmish fight with the local player (%s) -- he is not written as its opponent (%s) [n=%u]", nm ? nm : "?",
             !held ? "not held in his fight by the mod: WO-132 released it or never engaged it" : answered ? "the engine's relation test" : "unanswered",
             site ? site : "?", k);
    }
    return false;
}
bool probe_only(uint32_t eid) {
    if (!eid) return false;
    if (eid == g_probeEid.load(std::memory_order_relaxed)) return true;
    return on(kBitOwner) && !g_ownerOnly.empty() && g_owned.count(eid) != 0;   // the bench's A.1 through the stream (OWNER_ONLY)
}

double owner_shift_s(uint32_t eid) {
    if (!eid || !on(kBitOwner) || !g_ownerOnly.empty() || g_owned.count(eid) == 0) return 0.0;
    return owner_delay_for(eid) + kStreamEveryS / 2;   // the state arrives within one stream period of the row, then waits that body's own lag
}

bool probe_owned_name(const char* name) {
    if (!name) return false;
    if (g_p.joiner && !g_p.name.empty() && _stricmp(name, g_p.name.c_str()) == 0) return true;
    if (!on(kBitOwner)) return false;
    for (const auto& kv : g_owned) if (_stricmp(kv.second.name.c_str(), name) == 0) return true;
    return false;
}

bool probe_only_name(const char* name) {
    if (!name) return false;
    if (g_p.joiner && !g_p.name.empty() && _stricmp(name, g_p.name.c_str()) == 0) return true;
    if (!on(kBitOwner) || g_ownerOnly.empty()) return false;
    for (const auto& kv : g_owned) if (_stricmp(kv.second.name.c_str(), name) == 0) return true;
    return false;
}

int status(char* out, int n) {
    char kuba16[96] = "";
    motion::kuba_feet_status(kuba16, sizeof kuba16);
    return std::snprintf(out, static_cast<size_t>(n), "w208 bits=0x%X twin_off=0x%X configured=%d | target_violations=%u bodies=%u | probe %s npc=%s installs=%u "
                         "refusals=%u stale=%u withdraws=%u | owner copies=%zu ever=%u applied=%u installs=%u refusals=%u stale=%u withdraws=%u stops=%u steps=%u base=%u shot_holds=%u ai_holds=%u queue_full=%u far=%u "
                         "stream reads=%u bodies=%u states_in=%u bad=%u forgot=%u | W210 ready_holds=%u ready_releases=%u asks=%u | phase %s last_ms=%.0f picks=%u | queue_cap %s refused=%u passed=%u | kuba10 fidget_ok=%u reinstall=%u | kuba11 tpose_seen=%u fixed=%u back=%u unreadable=%u | kuba12 learned=%u skipped=%u | kuba13 trans_cut=%u | %s", g_bits.load(), twin_off(), g_configured.load() ? 1 : 0, c_violations.load(), c_violationBodies.load(),
                         g_p.host ? "host" : g_p.joiner ? "joiner" : "off", g_p.name.c_str(), g_p.installs, g_p.refusals, g_p.stale, g_p.withdraws, g_owned.size(),
                         c_oCopies.load(), c_oApplied.load(), c_oInstalls.load(), c_oRefusals.load(), c_oStale.load(), c_oWithdraws.load(), c_oStops.load(), c_oSteps.load(),
                         c_oPulses.load(), c_oShotHolds.load(), c_oAiHolds.load(), c_oQueueFull.load(), c_oFar.load(), c_streamReads.load(), c_streamBodies.load(), c_statesIn.load(), c_statesBad.load(),
                         c_forgot.load(), c_readyHolds.load(), c_readyReleases.load(), c_asksOut.load(),
                         anim_phase_on() ? "on" : "off", g_phaseLast * 1000.0, c_phaseBodies.load(), queue_cap_strict() ? "strict" : "engine-limit",
                         c_capRefused.load(), c_capPassed.load(), c_kubaFidgetOk.load(), c_kubaReinstall.load(),
                         c_kubaTposeSeen.load(), c_kubaTposeFixed.load(), c_kubaTposeBack.load(), c_kubaTposeUnread.load(),
                         c_kubaLearned.load(), c_kubaSkipped.load(), c_kubaTransCut.load(), kuba16);
}

namespace {
int status_reg(char* out, int n) { return status(out, n); }
void tick_reg() { probe_tick(); }
uint8_t stream_read_reg(uint8_t, const uint8_t* b, size_t l, uint8_t* o, size_t c, size_t* ol) { return op_stream_read(b, l, o, c, ol); }
uint8_t stream_in_reg(uint8_t, const uint8_t* b, size_t l, uint8_t* o, size_t c, size_t* ol) { return op_stream_in(b, l, o, c, ol); }
uint8_t asks_reg(uint8_t, const uint8_t* b, size_t l, uint8_t* o, size_t c, size_t* ol) { return op_asks(b, l, o, c, ol); }
uint8_t kuba_duel_read_reg(uint8_t, const uint8_t* b, size_t l, uint8_t* o, size_t c, size_t* ol) { return op_kuba_duel_read(b, l, o, c, ol); }
uint8_t kuba_remote_duel_reg(uint8_t, const uint8_t* b, size_t l, uint8_t* o, size_t c, size_t* ol) { return op_kuba_remote_duel(b, l, o, c, ol); }
uint8_t kuba_take_verdict_reg(uint8_t, const uint8_t* b, size_t l, uint8_t* o, size_t c, size_t* ol) { return op_kuba_take_verdict(b, l, o, c, ol); }
uint8_t forget_reg(uint8_t, const uint8_t* b, size_t l, uint8_t* o, size_t c, size_t* ol) { return op_stream_forget(b, l, o, c, ol); }
uint8_t kuba_remote_frame_reg(uint8_t, const uint8_t* b, size_t l, uint8_t* o, size_t c, size_t* ol) { return op_kuba_remote_frame(b, l, o, c, ol); }
uint8_t kuba_duel_release_reg(uint8_t, const uint8_t* b, size_t l, uint8_t* o, size_t c, size_t* ol) { return op_kuba_duel_release(b, l, o, c, ol); }
uint8_t kuba_aggro_reg(uint8_t, const uint8_t* b, size_t l, uint8_t* o, size_t c, size_t* ol) { return op_kuba_aggro(b, l, o, c, ol); }   // KUBA 33c
uint8_t kuba_host_ok_reg(uint8_t, const uint8_t* b, size_t l, uint8_t* o, size_t c, size_t* ol) { return op_kuba_host_ok(b, l, o, c, ol); }   // KUBA 34
uint8_t kuba_player_armed_reg(uint8_t, const uint8_t* b, size_t l, uint8_t* o, size_t c, size_t* ol) { return op_kuba_player_armed(b, l, o, c, ol); }   // KUBA 37
const bool kReg = wo203::reg_op(kOpConfig, &op) && wo203::reg_op(kOpStatus, &op) && wo203::reg_status("w208", &status_reg)
               && wo203::reg_tick("w208", &tick_reg) && wo203::reg_op(kOpStreamRead, &stream_read_reg) && wo203::reg_op(kOpStreamIn, &stream_in_reg)
               && wo203::reg_op(kOpW210Asks, &asks_reg) && wo203::reg_op(kOpW210StreamForget, &forget_reg)
               && wo203::reg_op(kOpKubaDuelRead, &kuba_duel_read_reg) && wo203::reg_op(kOpKubaRemoteDuel, &kuba_remote_duel_reg)
               && wo203::reg_op(kOpKubaTakeVerdict, &kuba_take_verdict_reg)   // KUBA 30
               && wo203::reg_op(kOpKubaRemoteFrame, &kuba_remote_frame_reg) && wo203::reg_op(kOpKubaDuelRelease, &kuba_duel_release_reg)   // KUBA 32
               && wo203::reg_op(kOpKubaAggro, &kuba_aggro_reg)   // KUBA 33c
               && wo203::reg_op(kOpKubaHostOk, &kuba_host_ok_reg)   // KUBA 34
               && wo203::reg_op(kOpKubaPlayerArmed, &kuba_player_armed_reg);   // KUBA 37
} // namespace

// ---- WO-217 (main thread) ----
bool fragment_name_of(uint32_t eid, uint32_t frag, char* out, size_t n) {
    if (!out || !n) return false;
    out[0] = 0;
    if (frag == 0xFFFFFFFFu) { std::snprintf(out, n, "-"); return false; }
    const std::string s = frag_name_of(eid, frag);
    std::snprintf(out, n, "%s", s.empty() ? "?" : s.c_str());
    return !s.empty();
}
const char* pfam_text(uint8_t fam) { return wo210rules::pfam_name(static_cast<wo210rules::PFam>(fam)); }
bool pfam_is_posture(uint8_t fam) { return wo210rules::posture(static_cast<wo210rules::PFam>(fam)); }
void place_context(uint32_t eid, const char* name, char* act, size_t n, int* path, bool* neutral, float* dist, float* objDist) {
    uint64_t og = 0;
    if (!wo141::activity_text(name, act, n, &og) && act && n) std::snprintf(act, n, "none");
    *path = wo141::activity_path(name);
    *neutral = wo200::copy_neutral_always();
    *dist = -1; *objDist = -1;
    float cp[3]{}, me[3]{};
    void* ce = engine::entity_by_id(eid);
    if (!ce || !engine::entity_world_pos(ce, cp)) return;
    void* pe = engine::entity_by_id(kPlayerEntity);
    if (pe && engine::entity_world_pos(pe, me)) *dist = std::sqrt((cp[0] - me[0]) * (cp[0] - me[0]) + (cp[1] - me[1]) * (cp[1] - me[1]));
    float op[3]{};
    void* oe = og ? engine::entity_by_guid(og) : nullptr;
    if (oe && engine::entity_world_pos(oe, op))
        *objDist = std::sqrt((cp[0] - op[0]) * (cp[0] - op[0]) + (cp[1] - op[1]) * (cp[1] - op[1]) + (cp[2] - op[2]) * (cp[2] - op[2]));
}
bool ask_release(uint32_t eid, const char* name) {
    static std::unordered_map<uint32_t, double> s_at;
    const double now = npcdrive::now_s();
    double& at = s_at[eid];
    if (now - at < 3.0 || g_asks.size() >= 64 || !name || !*name) return false;
    at = now;
    g_asks.push_back({1, name});
    return true;
}
double last_install_age_s(uint32_t eid) {
    const auto it = g_lastInstall.find(eid);
    return it == g_lastInstall.end() ? 1e9 : npcdrive::now_s() - it->second;
}
uint8_t host_family(uint32_t eid) {
    const auto it = g_owned.find(eid);
    if (it == g_owned.end() || !it->second.hostHave) return 0xFF;
    return static_cast<uint8_t>(fam_of(eid, it->second.host.cls, it->second.host.frag));
}
bool seat_state(uint32_t eid, bool* hostSeated, bool* copySeated) {
    const auto it = g_owned.find(eid);
    if (it == g_owned.end() || npcdrive::now_s() - it->second.seatAt > 1.0) return false;
    *hostSeated = it->second.seatHost; *copySeated = it->second.seatCopy;
    return true;
}

} // namespace kcdmp::wo208
