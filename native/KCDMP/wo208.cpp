// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-208: the native half's switch word and status (wo208.h).
#include "wo208.h"
#include "wo208_rules.h"
#include "wo203.h"
#include "wo163.h"
#include "wo187.h"
#include "wo132.h"
#include "wo209.h"
#include "wo209_rules.h"
#include "wo143.h"
#include "wo172.h"
#include "wo210.h"
#include "wo210_kd.h"
#include "wo210_rules.h"
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
        uint8_t rec[2 + 2 * kWireScope];
        uint64_t sig = (cm ? 1 : 2) | (down ? 4 : 0);
        int nsc = 0;
        for (int i = 0; i < n && nsc < 2; ++i) {
            const uint8_t which = !std::strcmp(sc[i].name, "FullBody") ? 0 : !std::strcmp(sc[i].name, "UpperBody") ? 1 : 255;
            if (which == 255) continue;
            if (put_scope(sc[i], which, rec + 2 + nsc * kWireScope, &sig)) ++nsc;
        }
        HostBody& hb = g_hostBodies[eid];
        if (sig == hb.sig && now - hb.sentAt < kResendS) continue;
        const size_t need = 1 + nl + 2 + static_cast<size_t>(nsc) * kWireScope;
        if (o + need > cap) break;
        out[o] = static_cast<uint8_t>(nl); std::memcpy(out + o + 1, nm, nl); o += 1 + nl;
        out[o] = static_cast<uint8_t>((cm ? wo210rules::kCmCombat : 0) | (down ? wo210rules::kCmDown : 0)); out[o + 1] = static_cast<uint8_t>(nsc); o += 2;
        std::memcpy(out + o, rec + 2, static_cast<size_t>(nsc) * kWireScope); o += static_cast<size_t>(nsc) * kWireScope;
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
};
std::unordered_map<uint32_t, OwnCopy> g_owned;   // main thread
std::unordered_set<uint32_t> g_readyHeld;          // WO-210 D.3: copies whose combat-mode hold the host NPC's streamed state set
std::atomic<uint32_t> c_readyHolds{0}, c_readyReleases{0};
std::atomic<uint32_t> c_oInstalls{0}, c_oRefusals{0}, c_oStale{0}, c_oWithdraws{0}, c_oSteps{0}, c_oPulses{0}, c_oApplied{0}, c_oCopies{0}, c_oFar{0};
std::string g_ownerOnly;
double g_ownerDelayS = 0.38;
bool g_ownerEnvRead = false;

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
wo210rules::PFam pfam_of(uint32_t eid, uint8_t cls, uint32_t frag) {
    return wo210rules::family(cls, frag_name_of(eid, frag).c_str(), frag);
}
// The asks the agent drains (op 140): a copy whose own engine posture the hand-over could not lay over -- released the game's way.
struct Ask { uint8_t kind; std::string name; };
std::vector<Ask> g_asks;   // main thread
std::atomic<uint32_t> c_asksOut{0};
void own_retire(OwnCopy& oc, Probe::Scope& s, double now, const char* why);
bool is_ours(const OwnCopy& oc, const void* act) {
    if (!act) return false;
    if (act == oc.baseAct || act == oc.own[0].act || act == oc.own[1].act) return true;
    for (const auto& r : oc.retired) if (r.act == act) return true;
    return false;
}
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
    const Fam fam = hs ? family_of_id(hs->cls, hs->frag) : Fam::Empty;
    bool installed = false, neutral = false;
    if (hs && !is_base(fam) && fam != Fam::Hold) {
        if (oc.own[0].act && oc.own[0].hostAct == hs->act) installed = true;   // ours already plays it
        else {
            void* act = nullptr;
            const rttr::SwingResult r = rttr::install_fragment(eid, hs->frag, hs->tags, hs->prio, hs->flags, &act);
            if (r == rttr::SwingResult::Ok && act) {
                if (oc.own[0].act) own_retire(oc, oc.own[0], now, "the hand-over");
                oc.own[0].act = act; oc.own[0].hostAct = hs->act; oc.own[0].frag = hs->frag; oc.own[0].prio = hs->prio; oc.own[0].queuedAt = now; oc.own[0].started = false;
                installed = true;
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
    // WO-209 C.2 (mp_copy_oneshot_request): the host's work one-shot runs on the copy as the game's own NPC-state request (WO-143's) --
    // the owner holds the full body while it runs: nothing installed over it, no base given under it
    const bool shot = wo209::on(wo209rules::kBitShotRequest) && wo143::shot_in_flight(eid);
    if (shot) c_oShotHolds.fetch_add(1);
    // WO-209 B: the copy's own combat attack owns its full body while it runs -- the owner's state is a stream delay behind, and the host's
    // footwork just before the swing, installed over the attack, cut it after 26 ms (d9-defence: the copy started again late, its blow
    // past the host's hold)
    const bool aiRuns = wo209::ai_owns_body(eid);
    if (aiRuns) c_oAiHolds.fetch_add(1);
    // WO-209 C: nothing of the owner's is queued on a copy whose action queue is already long (the engine allows 10). rc1-c2tavern-1: a
    // seated copy's own actions at the head of its queue (its movement, its look pose) never let the owner's installs start, the owner
    // withdrew each as stale and installed the next, and they piled up behind them -- 11,004 queue overflows on one copy, 59,993 on
    // seven the next night, 125,205 in the smoke. (rc2 left a seated copy to the game instead: no overflow, but the beds' sleepers stood
    // -- the owner's own install is what lays them down: lying matched 0 % of 2,716 samples, rc1 ~100 %.)
    const wo187::QueueInfo qi = wo187::queue_info(eid);
    const bool queueFull = wo209::fix_on(wo209rules::kFixOwnerQueueCap) && wo209rules::owner_queue_full(qi.n);
    if (queueFull) c_oQueueFull.fetch_add(1);
    for (int k = 0; k < 2; ++k) {
        if ((shot || aiRuns) && k == 0) continue;
        const HostScope* hs = have[k] ? &hsc[k] : nullptr;
        const Fam fam = hs ? family_of_id(hs->cls, hs->frag) : Fam::Empty;
        Probe::Scope& o = oc.own[k];
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
                if (queueFull) break;   // the host's action waits for room (the queue cap above)
                if (o.act) own_retire(oc, o, now, "the host's next action");
                void* act = nullptr;
                const rttr::SwingResult r = rttr::install_fragment(eid, hs->frag, hs->tags, hs->prio, hs->flags, &act);
                if (r == rttr::SwingResult::Ok && act) {
                    o.act = act; o.hostAct = hs->act; o.frag = hs->frag; o.prio = hs->prio; o.queuedAt = now; o.started = false;
                    const uint32_t n = c_oInstalls.fetch_add(1) + 1;
                    if (n <= 80 || n % 200 == 0)
                        logf("WO208-OWN install npc=%s scope=%s class=%s frag=%u prio=%d (#%u)", oc.name.c_str(), kOwnScopes[k], cls_name(hs->cls), hs->frag, hs->prio, n);
                } else {
                    const uint32_t n = c_oRefusals.fetch_add(1) + 1;
                    if (n <= 40) logf("WO208-OWN refused npc=%s scope=%s class=%s frag=%u -> %s", oc.name.c_str(), kOwnScopes[k], cls_name(hs->cls), hs->frag, rttr::swing_result_name(r));
                }
                break;
            }
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
            const HostScope* hb = have[0] && is_base(family_of_id(hsc[0].cls, hsc[0].frag)) && hsc[0].cls != kClsNone ? &hsc[0] : nullptr;
            const char* how = "no host base to give it";
            if (hb) {
                void* act = nullptr;
                const rttr::SwingResult r = rttr::install_fragment(eid, hb->frag, hb->tags, kBasePriority, hb->flags, &act);
                if (r == rttr::SwingResult::Ok && act) { oc.baseAct = act; oc.baseFrag = hb->frag; how = "the host's base fragment installed"; }
                else how = rttr::swing_result_name(r);
            } else {
                bool c = false;
                motion::combat_mode_of(eid, &c);
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
            if (alive) rttr::release_action(oc.retired[i].act);
            oc.retired.erase(oc.retired.begin() + static_cast<std::ptrdiff_t>(i));
        } else ++i;
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
        const bool mod = shot || aiRuns || copyF == PFam::Combat || copyF == PFam::Paired;
        Verdict v = check(copyF, hostF, trans, mod, !wo210::fix_on(kFixPostureOtherGrace));
        if (!have[0] && v != Verdict::Empty) v = Verdict::Ok;   // no host state to judge a posture by: only an empty body counts
        // the same fragment is the same body, whatever class plays it (the owner installs the host's idle-state postures through a combat
        // action: d1's first watchdog took "ours, other" against "the host's, working" for the very same work fragment, 132 reseats)
        if (v != Verdict::Empty && have[0] && !cs[0].empty && cs[0].frag == hsc[0].frag) v = Verdict::Ok;
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
                const bool ok = own_handover(eid, oc, now, path);
                // the copy's own engine posture (its NPC state's bed, seat or workstation: an idle state at priority 3) is over any base
                // of ours: its stance is released the game's own way by the agent (op 140), at most every 5 s
                if ((v == Verdict::StalePosture || v == Verdict::StaleDead) && copysOwn && now - oc.askedAt >= 5.0 && g_asks.size() < 64) {
                    oc.askedAt = now;
                    g_asks.push_back({1, oc.name});
                }
                wo210::note_reseat(oc.name.c_str(), cs[0].empty ? "empty" : (std::string(pfam_name(copyF)) + (copysOwn ? "(its own)" : "(ours)")).c_str(),
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
    const bool alive = engine::entity_by_id(eid) != nullptr;
    // WO-210 A.2 (paths 2, 12, 13, 14): the host's current base -- or the body's neutral idle when the host's state is a posture or not
    // known -- installed now under the copy's own base and left running when we let go: the copy's own engine replaces it once it queues
    // its own base, and until then the host's last base plays (session E: the owner switched off live left every copy empty, "hard core
    // T posing"; a let-go copy kept looping the host's last posture). A dead copy is left as it lies (C.2).
    const bool hand = alive && path != wo210rules::kPathDeath && wo210::on(wo210rules::kBitStateAlways) && !wo172::copy_dead(eid);
    if (hand) {
        const bool baseKnown = oc.hostHave && oc.host.cls != kClsNone && is_base(family_of_id(oc.host.cls, oc.host.frag));
        const HostScope keep = oc.host; const bool keepHave = oc.hostHave;
        if (!baseKnown) oc.hostHave = false;
        own_handover(eid, oc, now, path);
        oc.host = keep; oc.hostHave = keepHave;
    }
    for (auto& s : oc.own) if (s.act) {
        if (alive) {
            uint32_t st = 0;
            if (hand && rd(s.act, 0x28, &st) && st < 2) rttr::stop_action(s.act); else wr_prio(s.act, kWithdrawnPriority);
            rttr::release_action(s.act);
        }
        s = Probe::Scope{};
    }
    if (oc.baseAct) { if (alive) { if (!hand) rttr::stop_action(oc.baseAct); rttr::release_action(oc.baseAct); } oc.baseAct = nullptr; }
    for (auto& r : oc.retired) if (alive) rttr::release_action(r.act);
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
        // WO-209 (the owner's village runs): the host streams every body within 40 m of either player, so copies by the host 130 m and more
        // from the local player were owned here with no full-body action the engine plays at that distance (empty: a T-pose as they come
        // into view). The owner drives the copies near the local player; a farther one is its own engine's and the old writers', as with
        // the owner off (owner_near_only)
        if (nearOnly) {
            float cp[3]{};
            const float d = engine::entity_world_pos(e, cp) ? std::sqrt((cp[0] - me[0]) * (cp[0] - me[0]) + (cp[1] - me[1]) * (cp[1] - me[1])) : -1.0f;
            const auto ow = g_owned.find(eid);
            if (d >= 0 && !wo209rules::owner_keeps(ow != g_owned.end(), d)) {
                if (ow != g_owned.end()) { own_release(eid, ow->second, now, "far from the local player", wo210rules::kPathFar); g_owned.erase(ow); c_oFar.fetch_add(1); }
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
        int pick = -1;
        for (int i = 0; i < static_cast<int>(v.size()); ++i) if (now - v[i].at >= g_ownerDelayS) pick = i;
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
        const CopyState& s = v[pick];
        // WO-210 C.3 (mp_knockdown_sync): the host NPC's knockdown -- the copy falls on its down edge and is stood up on its up edge, the
        // engine's way; while it lies the engine's ragdoll has the body (the owner installs nothing, the position stream writes nothing)
        bool kdHeld = false;
        KCDMP_FAULT_CALL(siteKd, "wo208::kd_tick");
        fault::guarded(siteKd, [&] { kdHeld = wo210kd::copy_tick(eid, oc.name.c_str(), (v.back().cm & wo210rules::kCmDown) != 0, now); });   // the newest word: a fall starts at once
        if (kdHeld) {
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
    { const double t = qpc_s(); fault::guarded_silent([t] { owner_tick(t); }); }   // A.2
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

bool probe_owned(uint32_t eid) {
    if (!eid) return false;
    if (eid == g_probeEid.load(std::memory_order_relaxed)) return true;
    return on(kBitOwner) && g_owned.count(eid) != 0;   // A.2: the owner's copies (main thread: every writer that asks runs there)
}

std::atomic<uint32_t> c_targetRefused{0}, c_fightHeals{0};
bool in_player_fight(uint32_t copy) {
    const bool sw = on(kBitTargetInFight);
    if (!sw) return true;
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
    return g_ownerDelayS + kStreamEveryS / 2;   // the state arrives within one stream period of the row, then waits the stream delay
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
    return std::snprintf(out, static_cast<size_t>(n), "w208 bits=0x%X twin_off=0x%X configured=%d | target_violations=%u bodies=%u | probe %s npc=%s installs=%u "
                         "refusals=%u stale=%u withdraws=%u | owner copies=%zu ever=%u applied=%u installs=%u refusals=%u stale=%u withdraws=%u stops=%u steps=%u base=%u shot_holds=%u ai_holds=%u queue_full=%u far=%u "
                         "stream reads=%u bodies=%u states_in=%u bad=%u forgot=%u | W210 ready_holds=%u ready_releases=%u asks=%u", g_bits.load(), twin_off(), g_configured.load() ? 1 : 0, c_violations.load(), c_violationBodies.load(),
                         g_p.host ? "host" : g_p.joiner ? "joiner" : "off", g_p.name.c_str(), g_p.installs, g_p.refusals, g_p.stale, g_p.withdraws, g_owned.size(),
                         c_oCopies.load(), c_oApplied.load(), c_oInstalls.load(), c_oRefusals.load(), c_oStale.load(), c_oWithdraws.load(), c_oStops.load(), c_oSteps.load(),
                         c_oPulses.load(), c_oShotHolds.load(), c_oAiHolds.load(), c_oQueueFull.load(), c_oFar.load(), c_streamReads.load(), c_streamBodies.load(), c_statesIn.load(), c_statesBad.load(),
                         c_forgot.load(), c_readyHolds.load(), c_readyReleases.load(), c_asksOut.load());
}

namespace {
int status_reg(char* out, int n) { return status(out, n); }
void tick_reg() { probe_tick(); }
uint8_t stream_read_reg(uint8_t, const uint8_t* b, size_t l, uint8_t* o, size_t c, size_t* ol) { return op_stream_read(b, l, o, c, ol); }
uint8_t stream_in_reg(uint8_t, const uint8_t* b, size_t l, uint8_t* o, size_t c, size_t* ol) { return op_stream_in(b, l, o, c, ol); }
uint8_t asks_reg(uint8_t, const uint8_t* b, size_t l, uint8_t* o, size_t c, size_t* ol) { return op_asks(b, l, o, c, ol); }
uint8_t forget_reg(uint8_t, const uint8_t* b, size_t l, uint8_t* o, size_t c, size_t* ol) { return op_stream_forget(b, l, o, c, ol); }
const bool kReg = wo203::reg_op(kOpConfig, &op) && wo203::reg_op(kOpStatus, &op) && wo203::reg_status("w208", &status_reg)
               && wo203::reg_tick("w208", &tick_reg) && wo203::reg_op(kOpStreamRead, &stream_read_reg) && wo203::reg_op(kOpStreamIn, &stream_in_reg)
               && wo203::reg_op(kOpW210Asks, &asks_reg) && wo203::reg_op(kOpW210StreamForget, &forget_reg);
} // namespace

} // namespace kcdmp::wo208
