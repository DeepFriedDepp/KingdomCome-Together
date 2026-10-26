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
#include "combat_swing.h"
#include "engine.h"
#include "fault_guard.h"
#include "hits.h"
#include "log.h"
#include "motion.h"
#include "npc_drive.h"
#include "npc_scan.h"
#include <windows.h>
#include <share.h>
#include <algorithm>
#include <atomic>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <unordered_map>
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
        char t[512];
        int n = status(t, sizeof t);
        if (n < 0) n = 0;
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
        uint8_t rec[2 + 2 * kWireScope];
        uint64_t sig = cm ? 1 : 2;
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
        out[o] = cm ? 1 : 0; out[o + 1] = static_cast<uint8_t>(nsc); o += 2;
        std::memcpy(out + o, rec + 2, static_cast<size_t>(nsc) * kWireScope); o += static_cast<size_t>(nsc) * kWireScope;
        hb.sig = sig; hb.sentAt = now;
        ++count;
        c_streamBodies.fetch_add(1);
    }
    out[0] = static_cast<uint8_t>(count);
    *outLen = o;
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
};
std::unordered_map<uint32_t, OwnCopy> g_owned;   // main thread
std::atomic<uint32_t> c_oInstalls{0}, c_oRefusals{0}, c_oStale{0}, c_oWithdraws{0}, c_oSteps{0}, c_oPulses{0}, c_oApplied{0}, c_oCopies{0};
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

std::atomic<uint32_t> c_oStops{0};
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
    for (int k = 0; k < 2; ++k) {
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
        switch (decide(fam, hs ? hs->act : 0, hs ? hs->prio : 0, copyRuns, view)) {
            case Do::Nothing: break;
            case Do::Withdraw: own_retire(oc, o, now, copyRuns ? "the copy runs it itself" : "the host's scope is back at its base"); break;
            case Do::Stale: {
                const uint32_t n = c_oStale.fetch_add(1) + 1;
                if (n <= 40 || n % 100 == 0) logf("WO208-OWN stale npc=%s frag=%u not started in %.0f ms -- removed (#%u)", oc.name.c_str(), o.frag, view.ageS * 1000.0, n);
                own_retire(oc, o, now, "stale");
                break;
            }
            case Do::StepPriority:
                if (wr_prio(o.act, hs->prio)) { c_oSteps.fetch_add(1); o.prio = hs->prio; }
                break;
            case Do::Install: {
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
    const bool empty = cn > 0 && cs[0].ok && cs[0].empty;
    if (oc.baseAct) {
        uint32_t st = 0;
        const bool ours = !empty && cn > 0 && cs[0].act == oc.baseAct;
        if (!rd(oc.baseAct, 0x28, &st) || st >= 4 || (!empty && !ours)) {   // finished, or the copy's own base is back: ours goes
            if (st < 4) rttr::stop_action(oc.baseAct);
            oc.retired.push_back({oc.baseAct, now});
            oc.baseAct = nullptr;
        }
    }
    if (!empty) oc.fullEmptySince = -1;
    else {
        if (oc.fullEmptySince < 0) oc.fullEmptySince = now;
        if (now - oc.fullEmptySince > 0.3 && now - oc.lastPulse > 1.0 && !oc.baseAct) {
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
}

void own_release(uint32_t eid, OwnCopy& oc, double now, const char* why) {
    const bool alive = engine::entity_by_id(eid) != nullptr;
    for (auto& s : oc.own) if (s.act) { if (alive) { wr_prio(s.act, kWithdrawnPriority); rttr::release_action(s.act); } s = Probe::Scope{}; }
    if (oc.baseAct) { if (alive) { rttr::stop_action(oc.baseAct); rttr::release_action(oc.baseAct); } oc.baseAct = nullptr; }
    for (auto& r : oc.retired) if (alive) rttr::release_action(r.act);
    oc.retired.clear();
    if (oc.held && alive) motion::set_npc_guard_hold(eid, false, nullptr, npcdrive::now_s());
    logf("WO208-OWN let go npc=%s (%s)", oc.name.c_str(), why);
    (void)now;
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
        for (auto& kv : g_owned) own_release(kv.first, kv.second, now, "mp_copy_anim_owner off");
        g_owned.clear(); g_copyStates.clear();
        return;
    }
    for (auto it = g_copyStates.begin(); it != g_copyStates.end();) {
        const uint32_t eid = it->first;
        auto& v = it->second;
        void* e = engine::entity_by_id(eid);
        if (!e || v.empty()) { it = g_copyStates.erase(it); continue; }
        const char* nm = engine::entity_name(e);
        if (!g_ownerOnly.empty() && (!nm || _stricmp(nm, g_ownerOnly.c_str()) != 0)) { it = g_copyStates.erase(it); continue; }
        // the newest state at least one stream delay old
        int pick = -1;
        for (int i = 0; i < static_cast<int>(v.size()); ++i) if (now - v[i].at >= g_ownerDelayS) pick = i;
        if (pick < 0) { ++it; continue; }
        auto ins = g_owned.try_emplace(eid);
        OwnCopy& oc = ins.first->second;
        if (ins.second) { oc.name = nm ? nm : "?"; c_oCopies.fetch_add(1); }
        oc.lastState = now;
        const CopyState& s = v[pick];
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
        KCDMP_FAULT_CALL(site, "wo208::own_apply");
        if (!fault::guarded(site, [&] { own_apply(eid, oc, s.cm, hsc, have, now); })) logf("WO208-OWN apply FAULTED npc=%s", oc.name.c_str());
        if (pick > 0) v.erase(v.begin(), v.begin() + pick);
        ++it;
    }
    // a copy the stream has not spoken of for 3 s is let go (its own engine and the old writers again)
    for (auto it = g_owned.begin(); it != g_owned.end();) {
        if (now - it->second.lastState > 3.0 || !engine::entity_by_id(it->first)) { own_release(it->first, it->second, now, "the host's stream is quiet"); it = g_owned.erase(it); }
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

bool probe_owned(uint32_t eid) {
    if (!eid) return false;
    if (eid == g_probeEid.load(std::memory_order_relaxed)) return true;
    return on(kBitOwner) && g_owned.count(eid) != 0;   // A.2: the owner's copies (main thread: every writer that asks runs there)
}

std::atomic<uint32_t> c_targetRefused{0};
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

int status(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "w208 bits=0x%X twin_off=0x%X configured=%d | target_violations=%u bodies=%u | probe %s npc=%s installs=%u "
                         "refusals=%u stale=%u withdraws=%u | owner copies=%zu ever=%u applied=%u installs=%u refusals=%u stale=%u withdraws=%u stops=%u steps=%u base=%u "
                         "stream reads=%u bodies=%u states_in=%u bad=%u", g_bits.load(), twin_off(), g_configured.load() ? 1 : 0, c_violations.load(), c_violationBodies.load(),
                         g_p.host ? "host" : g_p.joiner ? "joiner" : "off", g_p.name.c_str(), g_p.installs, g_p.refusals, g_p.stale, g_p.withdraws, g_owned.size(),
                         c_oCopies.load(), c_oApplied.load(), c_oInstalls.load(), c_oRefusals.load(), c_oStale.load(), c_oWithdraws.load(), c_oStops.load(), c_oSteps.load(),
                         c_oPulses.load(), c_streamReads.load(), c_streamBodies.load(), c_statesIn.load(), c_statesBad.load());
}

namespace {
int status_reg(char* out, int n) { return status(out, n); }
void tick_reg() { probe_tick(); }
uint8_t stream_read_reg(uint8_t, const uint8_t* b, size_t l, uint8_t* o, size_t c, size_t* ol) { return op_stream_read(b, l, o, c, ol); }
uint8_t stream_in_reg(uint8_t, const uint8_t* b, size_t l, uint8_t* o, size_t c, size_t* ol) { return op_stream_in(b, l, o, c, ol); }
const bool kReg = wo203::reg_op(kOpConfig, &op) && wo203::reg_op(kOpStatus, &op) && wo203::reg_status("w208", &status_reg)
               && wo203::reg_tick("w208", &tick_reg) && wo203::reg_op(kOpStreamRead, &stream_read_reg) && wo203::reg_op(kOpStreamIn, &stream_in_reg);
} // namespace

} // namespace kcdmp::wo208
