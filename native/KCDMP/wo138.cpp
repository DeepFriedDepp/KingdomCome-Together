// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-138: no pausing, and the host's NPC stream in the DLL. See wo138.h.
#include "wo138.h"
#include "wo138_rules.h"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <algorithm>
#include <atomic>
#include <cctype>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

#include "anchors.h"
#include "engine.h"
#include "hits.h"
#include "hook_prologues.h"
#include "inline_hook.h"
#include "log.h"
#include "main_thread.h"
#include "npc_drive.h"
#include "npc_scan.h"
#include "rttr_abi.h"

namespace kcdmp::wo138 {

namespace R = wo138rules;

namespace {

double now_s() {
    static LARGE_INTEGER f{};
    if (!f.QuadPart) QueryPerformanceFrequency(&f);
    LARGE_INTEGER c;
    QueryPerformanceCounter(&c);
    return static_cast<double>(c.QuadPart) / static_cast<double>(f.QuadPart);
}

std::atomic<FrameFn> g_frameFn{nullptr};
void emit(uint8_t type, const uint8_t* body, size_t len) {
    if (FrameFn fn = g_frameFn.load()) fn(type, body, static_cast<uint16_t>(len));
}
constexpr uint8_t kNpcStream = 0xA0;
constexpr uint8_t kWorld     = 0xA1;

// ---------------------------------------------------------------------------
// The PauseGame gate.
//
// CCryAction::PauseGame on this build (CryAction.dll, found by its own log
// string) takes (this, bool pause, uint16 source, bool force, uint fadeMs) and
// keeps one counter per source at this+8+source*4: a pause from a source bumps
// its counter, the game is paused while any counter is non-zero. The source
// names (the function that formats them, read off the image): 0 FlowGraphNode,
// 1 FlowGraphDebugger, 2 ScriptBind (Lua Game.PauseGame), 3 ProfileManager,
// 4 VideoMode, 5 GameOver, 6 CCET_PauseGame, 7 InGameMenu (the ESC menu),
// 10 RenderViewPort, 11 XboxPlatform, 12 Photomode (8 and 9 are unnamed).
// ---------------------------------------------------------------------------
constexpr const char* kPauseString = "CCryAction::PauseGame(), source:%d pause:%c, nFadeOutInMS:%d";
// mov [rsp+8],rbx; mov [rsp+18h],rsi; mov [rsp+20h],rdi -- no RIP-relative operand.
constexpr auto& kPrologue = hookpro::kPauseGame;   // hook_prologues.h (WO-148)
constexpr size_t kCountersOff = 8;

const char* source_name(uint16_t s) {
    static const char* const k[] = { "FlowGraphNode", "FlowGraphDebugger", "ScriptBind", "ProfileManager",
                                     "VideoMode", "GameOver", "CCET_PauseGame", "InGameMenu", "src8", "src9",
                                     "RenderViewPort", "XboxPlatform", "Photomode" };
    return s < sizeof(k) / sizeof(k[0]) ? k[s] : "unknown";
}

std::atomic<bool>     g_gateArmed{false};
std::atomic<bool>     g_leversOn{false};
std::atomic<uint32_t> g_mask{R::kDefaultMask};
std::atomic<uint32_t> g_declined{0};
std::atomic<uint32_t> g_pauseCalls{0};
std::atomic<uint16_t> g_lastSource{0xFFFF};
std::atomic<uint8_t>  g_lastPause{0};
std::atomic<void*>    g_cryAction{nullptr};
const char*           g_gateWhy = "not installed";
const uint8_t*        g_pauseFn = nullptr;   // the hooked entry (op 9 calls it)

// The CCryAction instance before any PauseGame call was seen, found through the
// image itself: the vtable slot in CryAction.dll's .rdata that holds the very
// PauseGame entry the gate hooked, walked back to the vtable's start (the run of
// code pointers), then the pointer in CryAction.dll's .data (the singleton)
// whose object carries that vtable. Read-only; main thread.
void* scan_cryaction() {
    HMODULE ca = GetModuleHandleA("CryAction.dll");
    anchor::Range rdata{}, data{}, text{};
    if (!ca || !g_pauseFn || !anchor::section(ca, ".rdata", &rdata) || !anchor::section(ca, ".data", &data) ||
        !anchor::section(ca, ".text", &text)) return nullptr;
    __try {
        const uint64_t want = reinterpret_cast<uint64_t>(g_pauseFn);
        for (const uint8_t* q = rdata.begin; q + 8 <= rdata.end; q += 8) {
            if (*reinterpret_cast<const uint64_t*>(q) != want) continue;
            const uint8_t* start = q;
            int slot = 0;
            while (start - 8 >= rdata.begin && text.contains(*reinterpret_cast<void* const*>(start - 8))) { start -= 8; ++slot; }
            for (const uint8_t* d = data.begin; d + 8 <= data.end; d += 8) {
                void* obj = *reinterpret_cast<void* const*>(d);
                if (!obj || reinterpret_cast<uintptr_t>(obj) < 0x10000 || (reinterpret_cast<uintptr_t>(obj) & 7)) continue;
                if (IsBadReadPtr(obj, 8)) continue;
                if (*reinterpret_cast<const uint8_t* const*>(obj) == start) {
                    logf("WO138-PAUSE the CCryAction instance: CryAction.dll .data+0x%zX, PauseGame at vtable slot %d",
                         static_cast<size_t>(d - data.begin), slot);
                    return obj;
                }
            }
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {}
    return nullptr;
}

using PauseFn = void (*)(void* self, bool pause, uint16_t source, bool force, uint32_t fadeMs);
bool call_pause(void* self, bool pause, uint16_t source, bool force) {
    if (!g_pauseFn || !self) return false;
    __try { reinterpret_cast<PauseFn>(const_cast<uint8_t*>(g_pauseFn))(self, pause, source, force, 0); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

bool __fastcall pause_gate(void* self, void* a2, void* a3, void* a4) {
    const bool pause = (reinterpret_cast<uintptr_t>(a2) & 0xFF) != 0;
    const uint16_t source = static_cast<uint16_t>(reinterpret_cast<uintptr_t>(a3) & 0xFFFF);
    const bool force = (reinterpret_cast<uintptr_t>(a4) & 0xFF) != 0;
    if (self) g_cryAction.store(self, std::memory_order_relaxed);
    const uint32_t n = g_pauseCalls.fetch_add(1, std::memory_order_relaxed) + 1;
    g_lastSource.store(source, std::memory_order_relaxed);
    g_lastPause.store(pause ? 1 : 0, std::memory_order_relaxed);
    const bool decline = R::decline_pause(g_leversOn.load(std::memory_order_relaxed), pause, source,
                                          g_mask.load(std::memory_order_relaxed));
    if (decline) g_declined.fetch_add(1, std::memory_order_relaxed);
    if (n <= 200 || n % 100 == 0)
        logf("WO138-PAUSE #%u source=%u(%s) pause=%d force=%d -> %s", n, source, source_name(source), pause ? 1 : 0,
             force ? 1 : 0, decline ? "DECLINED (a session: the world keeps running)" : "runs");
    return decline;
}

// The sources holding the game paused, from the instance's own counters. 0xFFFF
// when the instance is not known yet (no PauseGame call seen since injection).
uint16_t held_mask() {
    void* ca = g_cryAction.load(std::memory_order_relaxed);
    if (!ca) return 0xFFFF;
    uint16_t m = 0;
    __try {
        const int32_t* c = reinterpret_cast<const int32_t*>(static_cast<const char*>(ca) + kCountersOff);
        for (uint16_t s = 0; s < R::kSrcCount; ++s) {
            const int32_t v = c[s];
            if (v < 0 || v > 1000) return 0xFFFF;   // not the counters we think they are
            if (v) m = static_cast<uint16_t>(m | (1u << s));
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) { return 0xFFFF; }
    return m;
}

// ---------------------------------------------------------------------------
// The frame meter.
// ---------------------------------------------------------------------------
struct Window { double start = 0, game = 0; uint32_t frames = 0; };
Window   g_q, g_sec;
std::atomic<uint8_t>  g_world{0};
std::atomic<uint16_t> g_scale{1000};
std::atomic<uint16_t> g_fps{0};
uint16_t g_lastHeld = 0;
uint8_t  g_lastSentWorld = 0xFF;
uint16_t g_lastSentHeld = 0xFFFE;
double   g_lastWorldSendAt = 0;

void meter(double now, float dtIn) {
    double dt = dtIn;
    if (!std::isfinite(dt) || dt < 0) dt = 0;
    if (dt > 1.0) dt = 1.0;
    if (g_q.start == 0) g_q.start = now;
    if (g_sec.start == 0) g_sec.start = now;
    g_q.game += dt; ++g_q.frames;
    g_sec.game += dt; ++g_sec.frames;
    if (now - g_q.start >= 0.25) {
        static double s_lastScan = 0;
        if (!g_cryAction.load(std::memory_order_relaxed) && g_gateArmed && now - s_lastScan >= 5.0) {
            s_lastScan = now;
            if (void* c = scan_cryaction()) g_cryAction = c;
        }
        const uint16_t held = held_mask();
        R::World w = R::classify(g_q.game, now - g_q.start);
        if (held != 0xFFFF && held != 0) w = R::World::Frozen;   // a source holds the game timer
        g_world.store(static_cast<uint8_t>(w), std::memory_order_relaxed);
        g_lastHeld = held;
        const uint16_t scale = R::scale_permille(g_q.game, now - g_q.start);
        // A change goes out at once; the state again every 2 s (the agent's liveness).
        if (static_cast<uint8_t>(w) != g_lastSentWorld || held != g_lastSentHeld || now - g_lastWorldSendAt >= 2.0) {
            if (static_cast<uint8_t>(w) != g_lastSentWorld)
                logf("WO138-WORLD %s (game/real %.3f over %.2f s, %u frames, held=0x%X)",
                     w == R::World::Frozen ? "FROZEN" : w == R::World::Slowed ? "SLOWED" : "running",
                     g_q.game / (now - g_q.start), now - g_q.start, g_q.frames, held);
            uint8_t b[5];
            b[0] = static_cast<uint8_t>(w);
            std::memcpy(b + 1, &scale, 2);
            std::memcpy(b + 3, &held, 2);
            emit(kWorld, b, sizeof b);
            g_lastSentWorld = static_cast<uint8_t>(w);
            g_lastSentHeld = held;
            g_lastWorldSendAt = now;
        }
        g_q = Window{ now, 0, 0 };
    }
    if (now - g_sec.start >= 1.0) {
        g_scale.store(R::scale_permille(g_sec.game, now - g_sec.start), std::memory_order_relaxed);
        g_fps.store(static_cast<uint16_t>(std::min<uint32_t>(g_sec.frames, 0xFFFF)), std::memory_order_relaxed);
        g_sec = Window{ now, 0, 0 };
    }
}

// ---------------------------------------------------------------------------
// The native NPC sender (host).
// ---------------------------------------------------------------------------
constexpr uint32_t kPlayerEid = 0x7777;   // Dude
constexpr double   kResolveRetryS = 5.0;
constexpr double   kResolveWalkMinS = 3.0;   // one full entity walk costs ~14 ms (66 names, observed): at most every 3 s
constexpr size_t   kMaxFrame = 1000;
constexpr int      kMaxAnchors = 8;

struct Tracked {
    std::string name;
    std::string lower;
    bool        notHuman = false;
    uint32_t    eid = 0;
    double      resolveAt = -1e9;   // the last failed resolve
    R::Sample   s;
    bool        haveAttr = false;
    R::Sent     sent;
    bool        culled = false;
};

std::atomic<bool> g_on{false};
R::Config g_cfg;
std::vector<Tracked> g_tracked;
size_t   g_attrCursor = 0;
struct A3 { float x, y, z; };
std::vector<A3> g_anchors;
double   g_lastTick = 0;
double   g_lastWalk = -1e9;
uint32_t g_ticks = 0, g_rowsTotal = 0, g_rowsSec = 0, g_rowsLastSec = 0;
double   g_rowsSecAt = 0;
float    g_playerHp = -1.0f;
double   g_lastStatusLog = 0;
uint16_t g_resolved = 0;

// Pending Track parts (op 2), per generation.
uint16_t g_pendGen = 0xFFFF;
int      g_pendParts = 0;
std::vector<bool> g_pendGot;
std::vector<std::pair<std::string, uint8_t>> g_pendNames;

// The last 100 costs (us), for the status.
struct Ring { uint16_t v[100]{}; int n = 0, i = 0;
    void add(double us) { v[i] = static_cast<uint16_t>(std::min(us, 65535.0)); i = (i + 1) % 100; if (n < 100) ++n; }
    uint16_t p50() const { if (!n) return 0; uint16_t t[100]; std::memcpy(t, v, sizeof t); std::sort(t, t + n); return t[n / 2]; }
    uint16_t top() const { uint16_t m = 0; for (int k = 0; k < n; ++k) m = std::max(m, v[k]); return m; }
};
Ring g_cost, g_attrCost;

std::string lower_of(const std::string& s) {
    std::string o(s);
    for (auto& c : o) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    return o;
}

bool name_is(void* e, const std::string& lower) {
    const char* n = e ? engine::entity_name(e) : nullptr;
    if (!n) return false;
    __try {
        size_t i = 0;
        for (; i < lower.size(); ++i) {
            const char c = n[i];
            if (!c || static_cast<char>(std::tolower(static_cast<unsigned char>(c))) != lower[i]) return false;
        }
        return n[i] == 0;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

struct WalkCtx { std::unordered_map<std::string, uint32_t>* want; int found; };
// Separate from walk_visit: __try cannot share a frame with the std::string
// the lookup below builds (C2712).
bool lower_name(void* e, char* buf, size_t cap) {
    const char* n = engine::entity_name(e);
    if (!n) return false;
    size_t i = 0;
    __try { for (; i + 1 < cap && n[i]; ++i) buf[i] = static_cast<char>(std::tolower(static_cast<unsigned char>(n[i]))); }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
    buf[i] = 0;
    return i > 0;
}
bool walk_visit(void* e, void* ctx) {
    auto* w = static_cast<WalkCtx*>(ctx);
    char buf[64]{};
    if (!lower_name(e, buf, sizeof buf)) return false;
    auto it = w->want->find(buf);
    if (it != w->want->end() && !it->second) { it->second = engine::entity_id(e); ++w->found; }
    return w->found >= static_cast<int>(w->want->size());
}

// One entity walk for every unresolved name (never more than once a second,
// a name that was not found waits kResolveRetryS).
void resolve_missing(double now) {
    if (now - g_lastWalk < kResolveWalkMinS) return;
    std::unordered_map<std::string, uint32_t> want;
    for (auto& t : g_tracked) if (!t.eid && now - t.resolveAt >= kResolveRetryS) want.emplace(t.lower, 0u);
    if (want.empty()) return;
    g_lastWalk = now;
    WalkCtx ctx{ &want, 0 };
    engine::for_each_entity(&walk_visit, &ctx);
    for (auto& t : g_tracked) {
        if (t.eid) continue;
        auto it = want.find(t.lower);
        if (it == want.end()) continue;
        if (it->second) { t.eid = it->second; t.haveAttr = false; }
        else t.resolveAt = now;
    }
}

void read_attrs(Tracked& t) {
    const double t0 = now_s();
    void* soul = hits::soul_of_eid(t.eid);
    float hp = -1.0f;
    bool dead = false, ko = false, drawn = false;
    if (soul) {
        if (!rttr::soul_state(soul, "health", &hp)) hp = -1.0f;
        rttr::soul_bool(soul, "IsDead", &dead);
        rttr::soul_bool(soul, "IsUnconscious", &ko);
        if (!t.notHuman) rttr::combat_bool(soul, "HasWeaponInHand", &drawn);
    }
    t.s.hp = hp; t.s.dead = dead; t.s.ko = ko; t.s.drawn = drawn;
    t.haveAttr = true;
    g_attrCost.add((now_s() - t0) * 1e6);
}

struct Row { uint8_t flags; float x, y, z, rot, hp; const std::string* name; };

void flush_rows(const std::vector<Row>& rows) {
    uint8_t buf[kMaxFrame];
    size_t i = 0;
    while (i < rows.size()) {
        size_t n = 1;
        uint8_t count = 0;
        while (i < rows.size() && count < 255) {
            const Row& r = rows[i];
            const size_t nl = std::min<size_t>(r.name->size(), 63);
            const size_t need = 1 + 20 + 1 + nl;
            if (n + need > sizeof buf) break;
            buf[n] = r.flags;
            std::memcpy(buf + n + 1, &r.x, 4);
            std::memcpy(buf + n + 5, &r.y, 4);
            std::memcpy(buf + n + 9, &r.z, 4);
            std::memcpy(buf + n + 13, &r.rot, 4);
            std::memcpy(buf + n + 17, &r.hp, 4);
            buf[n + 21] = static_cast<uint8_t>(nl);
            std::memcpy(buf + n + 22, r.name->data(), nl);
            n += need;
            ++count;
            ++i;
        }
        if (!count) { ++i; continue; }
        buf[0] = count;
        emit(kNpcStream, buf, n);
    }
}

void sender_tick(double now) {
    if (!g_on.load(std::memory_order_relaxed) || g_tracked.empty()) return;
    if (now - g_lastTick < g_cfg.emitMs / 1000.0) return;
    g_lastTick = now;
    const double t0 = now_s();
    ++g_ticks;

    resolve_missing(now);

    float pp[3]{};
    void* pe = engine::entity_by_id(kPlayerEid);
    const bool havePlayer = pe && engine::entity_world_pos(pe, pp);
    bool playerHit = false;
    if (void* ps = hits::soul_of_eid(kPlayerEid)) {
        float hp = -1.0f;
        if (rttr::soul_state(ps, "health", &hp)) {
            if (g_playerHp >= 0 && hp < g_playerHp - 0.5f) playerHit = true;
            g_playerHp = hp;
        }
    }

    // Round robin: every NPC's health / life / drawn state once per attrMs.
    const int batch = R::attr_batch(static_cast<int>(g_tracked.size()), g_cfg);
    std::vector<size_t> attrIdx;
    for (int k = 0; k < batch && !g_tracked.empty(); ++k) {
        attrIdx.push_back(g_attrCursor % g_tracked.size());
        g_attrCursor = (g_attrCursor + 1) % g_tracked.size();
    }

    std::vector<Row> rows;
    uint16_t resolved = 0;
    for (size_t idx = 0; idx < g_tracked.size(); ++idx) {
        Tracked& t = g_tracked[idx];
        if (!t.eid) continue;
        void* e = engine::entity_by_id(t.eid);
        if (!e || !name_is(e, t.lower)) { t.eid = 0; t.haveAttr = false; t.resolveAt = -1e9; continue; }
        float x, y, z, yaw;
        if (!npcscan::read_entity(e, &x, &y, &z, &yaw)) continue;
        ++resolved;
        t.s.x = x; t.s.y = y; t.s.z = z; t.s.rot = yaw; t.s.notHuman = t.notHuman;
        if (!t.haveAttr || std::find(attrIdx.begin(), attrIdx.end(), idx) != attrIdx.end()) read_attrs(t);

        const bool eng = R::engaged(t.s, havePlayer, pp[0], pp[1], g_cfg.engageRange);
        const bool swing = R::swing_cue(playerHit, t.s, havePlayer, pp[0], pp[1]);
        float dMin = 1e9f;
        if (havePlayer) dMin = R::dist2d(x, y, pp[0], pp[1]);
        for (const auto& a : g_anchors) dMin = std::min(dMin, R::dist2d(x, y, a.x, a.y));
        const R::Cull c = R::cull(dMin, g_cfg, eng, R::heartbeat_due(t.sent, now, g_cfg), t.s.dead && !t.sent.dead,
                                  t.s.ko != t.sent.ko);
        if (c == R::Cull::Culled) { t.culled = true; continue; }
        t.culled = false;
        if (!R::should_send(t.s, t.sent, eng, swing, now, g_cfg)) continue;

        if (t.s.dead && !t.sent.dead)
            logf("WO138-SEND %s: the dead bit goes out (hp=%.1f)", t.name.c_str(), t.s.hp);
        rows.push_back(Row{ R::flags(t.s, swing, eng), x, y, z, yaw, t.s.hp, &t.name });
        t.sent.any = true;
        t.sent.x = x; t.sent.y = y; t.sent.z = z; t.sent.hp = t.s.hp; t.sent.at = now;
        t.sent.dead = t.s.dead; t.sent.ko = t.s.ko; t.sent.drawn = t.s.drawn; t.sent.engaged = eng;
    }
    g_resolved = resolved;
    if (!rows.empty()) flush_rows(rows);
    g_rowsTotal += static_cast<uint32_t>(rows.size());
    g_rowsSec += static_cast<uint32_t>(rows.size());
    if (now - g_rowsSecAt >= 1.0) { g_rowsLastSec = g_rowsSec; g_rowsSec = 0; g_rowsSecAt = now; }
    g_cost.add((now_s() - t0) * 1e6);
}

int status_text(char* out, size_t n) {
    const double now = now_s();
    const uint16_t held = g_lastHeld;
    return std::snprintf(out, n,
        "WO138 sender=%s tracked=%zu resolved=%u rows/s=%u total=%u ticks=%u age=%.0fms cost p50=%uus max=%uus "
        "attr p50=%uus | world=%s scale=%.3f fps=%u held=0x%X | gate=%s levers=%s mask=0x%X declined=%u calls=%u last=%s/%d | hold=%d",
        g_on ? "on" : "off", g_tracked.size(), g_resolved, g_rowsLastSec, g_rowsTotal, g_ticks,
        g_lastTick > 0 ? (now - g_lastTick) * 1000.0 : -1.0, g_cost.p50(), g_cost.top(), g_attrCost.p50(),
        g_world == 2 ? "FROZEN" : g_world == 1 ? "SLOWED" : "running", g_scale / 1000.0, g_fps.load(), held,
        g_gateArmed ? "armed" : g_gateWhy, g_leversOn ? "on" : "off", g_mask.load(), g_declined.load(),
        g_pauseCalls.load(), g_lastSource == 0xFFFF ? "-" : source_name(g_lastSource), g_lastPause.load(),
        npcdrive::hold_all() ? 1 : 0);
}

template <class T> bool rd(const uint8_t* b, size_t len, size_t off, T* v) {
    if (off + sizeof(T) > len) return false;
    std::memcpy(v, b + off, sizeof(T));
    return true;
}

uint8_t op_track(const uint8_t* b, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    uint16_t gen = 0;
    if (len < 5 || !rd(b, len, 0, &gen)) return kRBadRequest;
    const uint8_t part = b[2], parts = b[3], count = b[4];
    if (!parts || part >= parts) return kRBadRequest;
    std::vector<std::pair<std::string, uint8_t>> names;
    size_t o = 5;
    for (uint8_t i = 0; i < count; ++i) {
        if (o + 2 > len) return kRBadRequest;
        const uint8_t fl = b[o], nl = b[o + 1];
        if (!nl || nl > 63 || o + 2 + nl > len) return kRBadRequest;
        names.emplace_back(std::string(reinterpret_cast<const char*>(b + o + 2), nl), fl);
        o += 2 + nl;
    }
    if (o != len) return kRBadRequest;
    if (gen != g_pendGen || parts != g_pendParts) {
        g_pendGen = gen; g_pendParts = parts;
        g_pendGot.assign(parts, false);
        g_pendNames.clear();
    }
    if (!g_pendGot[part]) {
        g_pendGot[part] = true;
        g_pendNames.insert(g_pendNames.end(), names.begin(), names.end());
    }
    const bool complete = std::all_of(g_pendGot.begin(), g_pendGot.end(), [](bool x) { return x; });
    if (complete) {
        std::unordered_map<std::string, Tracked> old;
        for (auto& t : g_tracked) old.emplace(t.lower, std::move(t));
        std::vector<Tracked> next;
        std::unordered_set<std::string> seen;
        for (auto& nf : g_pendNames) {
            std::string lw = lower_of(nf.first);
            if (!seen.insert(lw).second) continue;
            auto it = old.find(lw);
            Tracked t = it != old.end() ? std::move(it->second) : Tracked{};
            t.name = nf.first; t.lower = lw; t.notHuman = (nf.second & 1) != 0;
            next.push_back(std::move(t));
        }
        const size_t before = g_tracked.size();
        g_tracked = std::move(next);
        g_attrCursor = 0;
        g_pendGen = 0xFFFF; g_pendParts = 0; g_pendGot.clear(); g_pendNames.clear();
        static size_t s_logged = static_cast<size_t>(-1);
        if (g_tracked.size() != s_logged) {
            s_logged = g_tracked.size();
            logf("WO138-TRACK gen %u: %zu names (was %zu)", gen, g_tracked.size(), before);
        }
    }
    if (cap >= 3) {
        out[0] = complete ? 1 : 0;
        const uint16_t sz = static_cast<uint16_t>(g_tracked.size());
        std::memcpy(out + 1, &sz, 2);
        *outLen = 3;
    }
    return kROk;
}

} // namespace

void set_frame_callback(FrameFn fn) { g_frameFn = fn; }

void for_each_tracked(void (*fn)(const char* name, uint32_t eid, void* ctx), void* ctx) {
    for (const auto& t : g_tracked) if (t.eid) fn(t.name.c_str(), t.eid, ctx);
}

void install() {
    HMODULE ca = GetModuleHandleA("CryAction.dll");
    if (!ca) { g_gateWhy = "CryAction.dll not loaded"; logf("WO138-GATE NOT armed -- %s", g_gateWhy); return; }
    int count = 0;
    const uint8_t* fn = anchor::function_by_string(ca, kPauseString, &count);
    if (!fn || count != 1) {
        g_gateWhy = "the PauseGame string has no single referencing function";
        logf("WO138-GATE NOT armed -- %s (%d)", g_gateWhy, count);
        return;
    }
    const char* why = nullptr;
    if (!inlinehook::install_gate4(const_cast<uint8_t*>(fn), kPrologue, sizeof kPrologue, &pause_gate, &why)) {
        g_gateWhy = why ? why : "install failed";
        logf("WO138-GATE NOT armed -- %s", g_gateWhy);
        return;
    }
    g_gateArmed = true; g_gateWhy = "armed";
    g_pauseFn = fn;
    // The instance now (read-only), so the meter knows the held sources from the start.
    if (void* c = scan_cryaction()) g_cryAction = c;
    char where[96];
    anchor::describe(fn, where, sizeof where);
    logf("WO138-GATE armed at %s: in a session, a pause from a masked source (default: the ESC menu) is declined; instance %p", where, g_cryAction.load());
}

bool gate_armed() { return g_gateArmed.load(); }

void tick() {
    const double now = now_s();
    meter(now, main_thread::last_dt());
    sender_tick(now);
    if (g_on && now - g_lastStatusLog >= 10.0) {
        g_lastStatusLog = now;
        char line[512];
        status_text(line, sizeof line);
        logf("%s", line);
    }
}

void on_pipe_closed() {
    if (g_on.exchange(false)) logf("WO138-SEND off (the agent went away)");
    if (g_leversOn.exchange(false)) logf("WO138-LEVERS off (the agent went away): menus pause again");
    npcdrive::set_hold_all(false);
}

uint8_t handle(const uint8_t* b, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (!len) return kRBadRequest;
    const uint8_t op = b[0];
    const uint8_t* p = b + 1;
    const size_t n = len - 1;
    switch (op) {
    case kOpConfig: {
        if (n != 15) return kRBadRequest;
        R::Config c;
        uint16_t epsMm = 0, cullR = 0, farB = 0;
        const bool on = p[0] != 0;
        std::memcpy(&c.emitMs, p + 1, 2);
        std::memcpy(&c.heartbeatMs, p + 3, 2);
        std::memcpy(&epsMm, p + 5, 2);
        c.cull = p[7] != 0;
        std::memcpy(&cullR, p + 8, 2);
        std::memcpy(&farB, p + 10, 2);
        c.engageRange = p[12];
        std::memcpy(&c.attrMs, p + 13, 2);
        c.moveEps = epsMm / 1000.0f;
        c.cullRadius = cullR;
        c.farBand = farB;
        if (c.emitMs < 20) c.emitMs = 20;
        if (c.heartbeatMs < 200) c.heartbeatMs = 200;
        if (c.attrMs < c.emitMs) c.attrMs = c.emitMs;
        const bool changed = on != g_on || c.emitMs != g_cfg.emitMs || c.heartbeatMs != g_cfg.heartbeatMs ||
                             c.cull != g_cfg.cull || c.cullRadius != g_cfg.cullRadius || c.farBand != g_cfg.farBand ||
                             c.moveEps != g_cfg.moveEps || c.engageRange != g_cfg.engageRange || c.attrMs != g_cfg.attrMs;
        g_cfg = c;
        if (on && !g_on) { for (auto& t : g_tracked) t.sent = R::Sent{}; }   // a fresh start sends everyone once
        g_on = on;
        if (changed)
            logf("WO138-SEND %s: every %u ms, heartbeat %u ms, eps %.3f m, cull %s %.0f m, far band %.0f m, engage %.0f m, attrs every %u ms",
                 on ? "ON" : "off", c.emitMs, c.heartbeatMs, c.moveEps, c.cull ? "on" : "off", c.cullRadius, c.farBand,
                 c.engageRange, c.attrMs);
        return kROk;
    }
    case kOpTrack:
        return op_track(p, n, out, cap, outLen);
    case kOpAnchors: {
        if (n < 1 || p[0] > kMaxAnchors || n != 1u + p[0] * 12u) return kRBadRequest;
        g_anchors.clear();
        for (uint8_t i = 0; i < p[0]; ++i) {
            A3 a;
            std::memcpy(&a, p + 1 + i * 12, 12);
            if (std::isfinite(a.x) && std::isfinite(a.y) && std::isfinite(a.z)) g_anchors.push_back(a);
        }
        return kROk;
    }
    case kOpStatus: {
        if (cap < sizeof(Status)) return kRFailed;
        Status s{};
        const double now = now_s();
        s.on = g_on ? 1 : 0;
        s.world = g_world;
        s.tracked = static_cast<uint16_t>(g_tracked.size());
        s.resolved = g_resolved;
        s.rowsLastSec = static_cast<uint16_t>(std::min<uint32_t>(g_rowsLastSec, 0xFFFF));
        s.rowsTotal = g_rowsTotal;
        s.ticks = g_ticks;
        s.lastTickAgeMs = g_lastTick > 0 ? static_cast<uint16_t>(std::min((now - g_lastTick) * 1000.0, 65534.0)) : 0xFFFF;
        s.costP50Us = g_cost.p50();
        s.costMaxUs = g_cost.top();
        s.scalePermille = g_scale;
        s.fps = g_fps;
        s.gateArmed = g_gateArmed ? 1 : 0;
        s.gateOn = g_leversOn ? 1 : 0;
        s.declined = g_declined;
        s.pauseCalls = g_pauseCalls;
        s.heldMask = g_lastHeld;
        s.lastSource = g_lastSource;
        s.lastPause = g_lastPause;
        s.holdAll = npcdrive::hold_all() ? 1 : 0;
        s.attrP50Us = g_attrCost.p50();
        s.frames = static_cast<uint32_t>(main_thread::frame_count());
        std::memcpy(out, &s, sizeof s);
        *outLen = sizeof s;
        return kROk;
    }
    case kOpLevers: {
        if (n != 5) return kRBadRequest;
        const bool on = p[0] != 0;
        uint32_t mask = 0;
        std::memcpy(&mask, p + 1, 4);
        if (!mask) mask = R::kDefaultMask;
        const bool was = g_leversOn.exchange(on);
        const uint32_t oldMask = g_mask.exchange(mask);
        if (was != on || oldMask != mask)
            logf("WO138-LEVERS %s mask=0x%X (gate %s)", on ? "ON: a masked pause is declined" : "off: menus pause as usual",
                 mask, g_gateArmed ? "armed" : g_gateWhy);
        if (cap >= 1) { out[0] = g_gateArmed ? 1 : 0; *outLen = 1; }
        return g_gateArmed ? kROk : kRNotArmed;
    }
    case kOpHold:
        if (n != 1) return kRBadRequest;
        npcdrive::set_hold_all(p[0] != 0);
        return kROk;
    case kOpText: {
        const int w = status_text(reinterpret_cast<char*>(out), cap);
        *outLen = w > 0 ? std::min<size_t>(static_cast<size_t>(w), cap - 1) : 0;
        return kROk;
    }
    case kOpRead: {
        if (n < 2 || p[0] == 0 || n != 1u + p[0]) return kRBadRequest;
        const std::string name(reinterpret_cast<const char*>(p + 1), p[0]);
        const double t0 = now_s();
        Tracked t;
        t.name = name; t.lower = lower_of(name);
        t.eid = hits::eid_of_name(name.c_str());
        void* e = t.eid ? engine::entity_by_id(t.eid) : nullptr;
        float x = 0, y = 0, z = 0, yaw = 0;
        const bool found = e && npcscan::read_entity(e, &x, &y, &z, &yaw);
        if (found) read_attrs(t);
        const uint32_t us = static_cast<uint32_t>((now_s() - t0) * 1e6);
        if (cap < 28) return kRFailed;
        out[0] = found ? 1 : 0;
        const float v[5] = { x, y, z, yaw, t.s.hp };
        std::memcpy(out + 1, v, 20);
        out[21] = t.s.dead ? 1 : 0; out[22] = t.s.ko ? 1 : 0; out[23] = t.s.drawn ? 1 : 0;
        std::memcpy(out + 24, &us, 4);
        *outLen = 28;
        return found ? kROk : kRNotFound;
    }
    case kOpPause: {
        if (n != 4) return kRBadRequest;
        uint16_t src = 0;
        std::memcpy(&src, p + 1, 2);
        void* ca = g_cryAction.load();
        if (!ca && (ca = scan_cryaction()) != nullptr) { g_cryAction = ca; logf("WO138-PAUSE the CCryAction instance found through gEnv: %p", ca); }
        if (!g_gateArmed || !ca) return kRNotFound;
        logf("WO138-PAUSE live check: PauseGame(pause=%d, source=%u(%s), force=%d) through the hooked entry", p[0] ? 1 : 0, src,
             source_name(src), p[3] ? 1 : 0);
        if (!call_pause(ca, p[0] != 0, src, p[3] != 0)) return kRFailed;
        const uint16_t held = held_mask();
        if (cap >= 2) { std::memcpy(out, &held, 2); *outLen = 2; }
        return kROk;
    }
    default:
        return kRBadRequest;
    }
}

} // namespace kcdmp::wo138
