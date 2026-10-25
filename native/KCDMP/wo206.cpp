// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-206 -- the digest sweep and the animation probe (wo206.h). Read-only: nothing here writes to the game.
#include "wo206.h"
#include "wo206_rules.h"
#include "wo163.h"
#include "wo172.h"
#include "wo187.h"
#include "wo200.h"
#include "wo203.h"
#include "engine.h"
#include "fault_guard.h"
#include "log.h"
#include "main_thread.h"
#include "motion.h"
#include "npc_drive.h"
#include "npc_scan.h"
#include <windows.h>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <unordered_map>
#include <vector>

namespace kcdmp::wo206 {

namespace {

using namespace kcdmp::wo206rules;

double now_s() { LARGE_INTEGER q, f; QueryPerformanceCounter(&q); QueryPerformanceFrequency(&f); return double(q.QuadPart) / double(f.QuadPart); }
uint64_t utc_ms() {
    FILETIME ft{};
    GetSystemTimePreciseAsFileTime(&ft);
    const uint64_t t = (static_cast<uint64_t>(ft.dwHighDateTime) << 32) | ft.dwLowDateTime;
    return t / 10000ULL - 11644473600000ULL;
}
void put_u32(uint8_t* p, uint32_t v) { std::memcpy(p, &v, 4); }
uint16_t get_u16(const uint8_t* p) { uint16_t v; std::memcpy(&v, p, 2); return v; }

constexpr uint32_t kPlayerEntity = 0x7777;

// ---- C: the digest sweep (main thread only: the ops and the tick both run there) -----------------------------------------------
std::vector<std::string> g_paths;
std::vector<Entry> g_entries;
Sweep g_sweep;
bool g_armed = false;
size_t g_dirtyScan = 0;   // where the next DigestRead starts looking for dirty entries
uint64_t c_reads = 0, c_found = 0, c_faults = 0;

void digest_tick(double now) {
    if (!g_armed || g_paths.empty()) return;
    size_t first = 0;
    const size_t n = sweep_take(g_sweep, g_paths.size(), kPerFrame, now, kMinPassGapS, &first);
    for (size_t i = first; i < first + n; ++i) {
        uint8_t kind = 0; int32_t value = -1; bool found = false;
        KCDMP_FAULT_CALL(site, "wo206::digest_read");
        if (!fault::guarded(site, [&] { found = wo200::read_value(g_paths[i].c_str(), &kind, &value); })) { ++c_faults; continue; }
        ++c_reads;
        if (found) ++c_found;
        entry_apply(g_entries[i], found, kind, value);
    }
}

// ---- D: the probe ---------------------------------------------------------------------------------------------------------------
std::atomic<bool> g_probe{false};
uint8_t g_machine = 0;
double g_hz = kDefaultHz;
float g_radius = kDefaultRadiusM;
int g_scopeReads = kDefaultScopeReads;
FILE* g_file = nullptr;
bool g_truncated = false;
std::string g_path;
uint64_t g_fileBytes = 0;
double g_lastSample = 0, g_lastFlush = 0;
uint64_t c_lines = 0, c_bodies = 0, c_scopeReads = 0, c_skipped = 0, c_rotations = 0;
// the probe's own cost on the main thread (the bench: the probe alone took ~6 % of the frame rate), per part, logged every 10 s
double c_tGather = 0, c_tScopes = 0, c_tLine = 0, g_lastCostLog = 0;
uint64_t c_costSamples = 0;
size_t g_rr = 0;   // the round-robin of the scope budget

struct ScopeCache { double at = -1; int n = 0; wo187::ScopeInfo sc[kMaxScopesOut]; };
std::unordered_map<uint32_t, ScopeCache> g_scopes;

struct Body { uint32_t eid; const char* kind; };

void open_file() {
    if (g_file) return;
    char cwd[MAX_PATH]{};
    if (!GetCurrentDirectoryA(MAX_PATH, cwd) || !cwd[0]) return;
    std::string p(cwd);
    if (p.back() != '\\') p += '\\';
    p += "kcdmp-anim.jsonl";
    g_path = p;
    g_file = _fsopen(p.c_str(), g_truncated ? "ab" : "wb", _SH_DENYWR);   // one file per game process: the first start truncates
    g_truncated = true;
    g_fileBytes = 0;
    if (g_file) logf("WO206-PROBE writing %s (machine=%s %.0f Hz %.0f m, %d scope reads a sample)", p.c_str(), machine_name(g_machine), g_hz,
                     g_radius, g_scopeReads);
}

void gather(std::vector<Body>& out, double now) {
    out.clear();
    void* pe = engine::entity_by_id(kPlayerEntity);
    float pp[3]{};
    if (!pe || !engine::entity_world_pos(pe, pp)) return;
    const uint32_t me = engine::entity_id(pe);
    if (me) out.push_back({me, "player"});
    npcdrive::BoundBody bb[160];
    const int nb = npcdrive::bound_bodies(bb, 160);
    for (int i = 0; i < nb && static_cast<int>(out.size()) < kMaxBodies; ++i) {
        void* e = engine::entity_by_id(bb[i].eid);
        float p[3]{};
        if (!e || !engine::entity_world_pos(e, p)) continue;
        const float d = std::sqrt((p[0] - pp[0]) * (p[0] - pp[0]) + (p[1] - pp[1]) * (p[1] - pp[1]) + (p[2] - pp[2]) * (p[2] - pp[2]));
        if (d > g_radius) continue;
        out.push_back({bb[i].eid, motion::is_avatar_eid(bb[i].eid) ? "figure" : "copy"});
    }
    // the NPCs around the player: the entity walk once a second (kWalkEveryS); between walks its list is re-checked by position
    static std::vector<uint32_t> s_walked;
    static double s_lastWalk = -1e9;
    if (now - s_lastWalk >= kWalkEveryS) {
        s_lastWalk = now;
        s_walked.clear();
        const npcscan::Anchor a{pp[0], pp[1], pp[2]};
        uint32_t walked = 0; uint8_t refuse = 0;
        npcscan::for_each_in_radius(&a, 1, g_radius, [](void* e, const char*, float, float, float, bool isHorse, void* c) {
            auto* w = static_cast<std::vector<uint32_t>*>(c);
            if (isHorse || static_cast<int>(w->size()) >= kMaxBodies) return;
            w->push_back(engine::entity_id(e));
        }, &s_walked, &walked, &refuse);
    }
    for (const uint32_t eid : s_walked) {
        if (static_cast<int>(out.size()) >= kMaxBodies) break;
        bool dup = false;
        for (const Body& b : out) if (b.eid == eid) { dup = true; break; }
        if (dup) continue;
        void* e = engine::entity_by_id(eid);   // gone since the walk: left out (an id is never dereferenced, only looked up)
        float p[3]{};
        if (!e || !engine::entity_world_pos(e, p)) continue;
        if ((p[0] - pp[0]) * (p[0] - pp[0]) + (p[1] - pp[1]) * (p[1] - pp[1]) + (p[2] - pp[2]) * (p[2] - pp[2]) > g_radius * g_radius) continue;
        out.push_back({eid, "npc"});
    }
}

// WO-207 G.1: when the probe first saw each queued action of a body (the action object compared, never dereferenced); the oldest wait in
// ms among the actions the engine's own queue trim does not keep (flag 4: the bench's fights hold movement actions requeued behind a
// combat action for minutes), -1 = the queue unreadable; ages[] = every queued action's wait (the "qd" field, with its flags). A body
// not seen for 10 s is forgotten.
struct QueueAge { const void* act; double first; };
std::unordered_map<uint32_t, std::vector<QueueAge>> g_qages;
std::unordered_map<uint32_t, double> g_qagesSeen;

int queue_age_ms(uint32_t eid, const wo187::QueueInfo& q, double now, int* ages = nullptr) {
    if (q.n < 0) return -1;
    std::vector<QueueAge>& v = g_qages[eid];
    g_qagesSeen[eid] = now;
    std::vector<QueueAge> next;
    next.reserve(static_cast<size_t>(q.nacts));
    double oldest = now;
    for (int i = 0; i < q.nacts; ++i) {
        double first = now;
        for (const QueueAge& a : v) if (a.act == q.acts[i]) { first = a.first; break; }
        next.push_back({q.acts[i], first});
        if (ages) ages[i] = static_cast<int>((now - first) * 1000.0);
        if (first < oldest && (q.flags[i] == 0xFFFFFFFFu || !(q.flags[i] & 4u))) oldest = first;
    }
    v.swap(next);
    if (g_qagesSeen.size() > 256) {
        for (auto it = g_qagesSeen.begin(); it != g_qagesSeen.end();) {
            if (now - it->second > 10.0) { g_qages.erase(it->first); it = g_qagesSeen.erase(it); } else ++it;
        }
    }
    return static_cast<int>((now - oldest) * 1000.0);
}

void probe_tick(double now) {
    if (!g_probe.load(std::memory_order_relaxed)) return;
    if (now - g_lastSample < 1.0 / g_hz) return;
    g_lastSample = now;
    open_file();
    if (!g_file) return;
    static std::vector<Body> bodies;
    const double t0 = now_s();
    gather(bodies, now);
    const double t1 = now_s();
    c_tGather += t1 - t0;
    if (bodies.empty()) return;
    // the scope budget: g_scopeReads bodies a sample, round-robin, so every body is read at least every n/budget samples
    for (int k = 0; k < g_scopeReads && k < static_cast<int>(bodies.size()); ++k) {
        const Body& b = bodies[(g_rr + static_cast<size_t>(k)) % bodies.size()];
        ScopeCache& c = g_scopes[b.eid];
        const int n = wo187::read_all_scopes(b.eid, c.sc, kMaxScopesOut);
        ++c_scopeReads;
        if (n > 0) { c.n = n; c.at = now; }
    }
    g_rr = (g_rr + static_cast<size_t>(g_scopeReads)) % bodies.size();
    // WO-207 E.1: a body the probe has not read for a minute leaves the cache (every body ever seen used to stay for the session)
    static double s_lastPrune = 0;
    if (now - s_lastPrune > 30.0) {
        s_lastPrune = now;
        for (auto it = g_scopes.begin(); it != g_scopes.end();) it = (it->second.at >= 0 && now - it->second.at > 60.0) ? g_scopes.erase(it) : std::next(it);
    }
    const double t2 = now_s();
    c_tScopes += t2 - t1;

    std::string line;
    line.reserve(4096);
    char buf[768];   // WO-207: a body's line with its tags and queue detail (qd) runs past 512
    std::snprintf(buf, sizeof buf, "{\"t\":%llu,\"m\":\"%s\",\"f\":%llu,\"a\":[", static_cast<unsigned long long>(utc_ms()), machine_name(g_machine),
                  main_thread::frame_count());
    line += buf;
    bool firstBody = true;
    for (const Body& b : bodies) {
        auto it = g_scopes.find(b.eid);
        if (it == g_scopes.end() || it->second.at < 0 || now - it->second.at > kScopeMaxAgeS) { ++c_skipped; continue; }   // never "empty" by default
        void* e = engine::entity_by_id(b.eid);
        float p[3]{};
        if (!e || !engine::entity_world_pos(e, p)) continue;
        const char* nm = engine::entity_name(e);
        char name[128];
        json_escape(nm ? nm : "?", name, sizeof name);
        const ScopeCache& c = it->second;
        const char* fb = nullptr;
        for (int i = 0; i < c.n; ++i) if (c.sc[i].ok && !c.sc[i].empty && std::strcmp(c.sc[i].name, "FullBody") == 0) fb = c.sc[i].action;
        const wo187::QueueInfo q = wo187::queue_info(b.eid);
        // WO-207 G.1: how long the oldest queued action has waited without starting (the same action object still in the queue); G.2: the
        // body's Mannequin tags (the fragment family a comparison needs)
        int ages[16]{};
        const int qa = queue_age_ms(b.eid, q, now, ages);
        char qd[240] = "";   // [[flags,age_ms,priority],...] the first 6 queued actions
        for (int i = 0, o = 0; i < q.nacts && i < 6 && o < static_cast<int>(sizeof qd) - 40; ++i)
            o += std::snprintf(qd + o, sizeof qd - static_cast<size_t>(o), "%s[%u,%d,%d]", i ? "," : "", q.flags[i], ages[i], q.prio[i]);
        // WO-207 G.1: a body whose physics is no living entity lies (a corpse, a knockdown's ragdoll), whatever its FullBody still holds
        npcdrive::PhysicsStatus ps{};
        const bool lying = npcdrive::physics_status(e, &ps) && ps.present && !ps.living;
        uint8_t tg[20]{};
        char tgh[41] = "";
        if (wo187::tag_state(b.eid, tg, sizeof tg)) for (int i = 0; i < 20; ++i) std::snprintf(tgh + 2 * i, 3, "%02x", tg[i]);
        std::snprintf(buf, sizeof buf, "%s{\"n\":\"%s\",\"k\":\"%s\",\"p\":[%.3f,%.3f,%.3f],\"r\":[0,0,%.4f],\"dead\":%d,\"rag\":%d,\"q\":%d,\"qa\":%d,\"qd\":[%s],\"tg\":\"%s\",\"sa\":%.0f,\"sc\":[",
                      firstBody ? "" : ",", name, b.kind, p[0], p[1], p[2], npcdrive::entity_yaw(e), wo172::copy_dead(b.eid) ? 1 : 0,
                      (ragdoll_class(fb) || lying) ? 1 : 0, q.n < 0 ? 0 : q.n, qa, qd, tgh, (now - c.at) * 1000.0);
        line += buf;
        bool firstScope = true;
        for (int i = 0; i < c.n; ++i) {
            const wo187::ScopeInfo& s = c.sc[i];
            if (!s.ok || s.empty) continue;   // an empty scope is not listed (the format's rule)
            char sn[48], an[96];
            json_escape(s.name, sn, sizeof sn);
            json_escape(s.action, an, sizeof an);
            std::snprintf(buf, sizeof buf, "%s{\"s\":\"%s\",\"c\":\"%s\",\"fr\":%u,\"pr\":%d}", firstScope ? "" : ",", sn, an, s.frag, s.prio);
            line += buf;
            firstScope = false;
        }
        line += "]}";
        firstBody = false;
        ++c_bodies;
    }
    line += "]}\n";
    std::fwrite(line.data(), 1, line.size(), g_file);
    ++c_lines;
    c_tLine += now_s() - t2;
    if (++c_costSamples, now - g_lastCostLog > 10.0) {
        const double n = static_cast<double>(c_costSamples);
        logf("WO206-PROBE cost per sample (avg of %llu): gather %.0f us, scopes %.0f us, line+write %.0f us; %zu bodies",
             static_cast<unsigned long long>(c_costSamples), c_tGather / n * 1e6, c_tScopes / n * 1e6, c_tLine / n * 1e6, bodies.size());
        c_tGather = c_tScopes = c_tLine = 0; c_costSamples = 0; g_lastCostLog = now;
    }
    g_fileBytes += line.size();
    if (g_fileBytes > kMaxFileBytes) {   // a long session keeps this file and the one before it (the bundle's size stays bounded)
        std::fclose(g_file); g_file = nullptr;
        const std::string prev = g_path.substr(0, g_path.size() - 6) + ".1.jsonl";
        MoveFileExA(g_path.c_str(), prev.c_str(), MOVEFILE_REPLACE_EXISTING);
        g_truncated = false;
        ++c_rotations;
        return;
    }
    if (now - g_lastFlush > 1.0) { std::fflush(g_file); g_lastFlush = now; }
    if (g_scopes.size() > 512) g_scopes.clear();
}

} // namespace

void tick() {
    const double now = now_s();
    digest_tick(now);
    probe_tick(now);
}

void on_pipe_closed() {
    g_armed = false;
    g_probe = false;
    if (g_file) { std::fflush(g_file); }
}

uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    using namespace kcdmp::wo163;
    *outLen = 0;
    switch (op) {
        case kOpStatus: {
            const int n = status_text(reinterpret_cast<char*>(out), static_cast<int>(cap));
            *outLen = n > 0 ? static_cast<size_t>(n) : 0;
            return kROk;
        }
        case kOpDigestPaths: {
            if (len < 3 || cap < 4) return kRBadRequest;
            if (body[1]) { g_paths.clear(); g_entries.clear(); g_sweep = Sweep{}; g_dirtyScan = 0; }
            size_t o = 3;
            for (int k = 0; k < body[2]; ++k) {
                if (o + 2 > len) return kRBadRequest;
                const uint16_t pl = get_u16(body + o);
                if (pl == 0 || pl > kMaxPathLen || o + 2 + pl > len || g_paths.size() >= kMaxPaths) return kRBadRequest;
                g_paths.emplace_back(reinterpret_cast<const char*>(body + o + 2), pl);
                g_entries.emplace_back();
                o += 2 + pl;
            }
            put_u32(out, static_cast<uint32_t>(g_paths.size()));
            *outLen = 4;
            return kROk;
        }
        case kOpDigestArm: {
            if (len < 2 || cap < 8) return kRBadRequest;
            const bool on = body[1] != 0;
            if (on != g_armed) logf("WO206-DIGEST sweep %s (%zu paths, %u passes so far)", on ? "armed" : "disarmed", g_paths.size(), g_sweep.passes);
            g_armed = on;
            if (!on) g_sweep.inPass = false;   // a pass cut by a load starts again from the top
            put_u32(out, g_sweep.passes); put_u32(out + 4, static_cast<uint32_t>(g_paths.size()));
            *outLen = 8;
            return kROk;
        }
        case kOpDigestRead: {
            if (len < 2 || cap < 13) return kRBadRequest;
            const size_t want = body[1] ? body[1] : 1;
            const size_t room = (cap - 13) / 10;
            const size_t max = want < room ? want : room;
            uint8_t* rows = out + 13;
            size_t n = 0;
            const size_t total = g_entries.size();
            for (size_t step = 0; step < total && n < max; ++step) {
                const size_t i = (g_dirtyScan + step) % total;
                Entry& e = g_entries[i];
                if (!e.dirty) continue;
                put_u32(rows, static_cast<uint32_t>(i));
                rows[4] = e.found ? 1 : 0; rows[5] = e.kind;
                std::memcpy(rows + 6, &e.value, 4);
                rows += 10; ++n;
                e.dirty = false;
                g_dirtyScan = i + 1;
            }
            uint32_t left = 0;
            for (const Entry& e : g_entries) left += e.dirty ? 1u : 0u;
            put_u32(out, g_sweep.passes); put_u32(out + 4, static_cast<uint32_t>(total)); put_u32(out + 8, left);
            out[12] = static_cast<uint8_t>(n);
            *outLen = 13 + n * 10;
            return kROk;
        }
        case kOpProbe: {
            if (len < 2 || cap < 5) return kRBadRequest;
            const bool on = body[1] != 0;
            if (len > 2 && body[2] != 255) g_machine = body[2];
            if (len > 3 && body[3] != 255 && body[3] > 0) g_hz = body[3];
            if (len > 4 && body[4] != 255 && body[4] > 0) g_radius = body[4];
            if (len > 5 && body[5] != 255 && body[5] > 0) g_scopeReads = body[5];
            if (on != g_probe.load()) logf("WO206-PROBE %s (machine=%s)", on ? "on" : "off", machine_name(g_machine));
            g_probe = on;
            if (!on && g_file) std::fflush(g_file);
            out[0] = on ? 1 : 0; put_u32(out + 1, static_cast<uint32_t>(c_lines));
            *outLen = 5;
            return kROk;
        }
        default: return kRBadRequest;
    }
}

int status_text(char* out, int n) {
    if (!out || n <= 0) return 0;
    const int k = std::snprintf(out, static_cast<size_t>(n),
        "wo206 digest armed=%d paths=%zu passes=%u reads=%llu found=%llu faults=%llu | probe on=%d machine=%s lines=%llu bodies=%llu scope_reads=%llu skipped=%llu"
        " | memory scope_cache=%zu queue_ages=%zu",   // WO-207 E.1: the probe's own long-lived containers (entries)
        g_armed ? 1 : 0, g_paths.size(), g_sweep.passes, static_cast<unsigned long long>(c_reads), static_cast<unsigned long long>(c_found),
        static_cast<unsigned long long>(c_faults), g_probe.load() ? 1 : 0, machine_name(g_machine), static_cast<unsigned long long>(c_lines),
        static_cast<unsigned long long>(c_bodies), static_cast<unsigned long long>(c_scopeReads), static_cast<unsigned long long>(c_skipped),
        g_scopes.size(), g_qages.size());
    return k < 0 ? 0 : (k >= n ? n - 1 : k);
}

namespace {
const bool kReg = wo203::reg_op(kOpStatus, &handle) && wo203::reg_op(kOpDigestPaths, &handle) && wo203::reg_op(kOpDigestArm, &handle)
               && wo203::reg_op(kOpDigestRead, &handle) && wo203::reg_op(kOpProbe, &handle) && wo203::reg_tick("w206", &tick)
               && wo203::reg_status("w206", &status_text);
} // namespace

} // namespace kcdmp::wo206
