// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-203B area "cam" (docs/WO-203B-findings.md): the listener sees the starter's conversation camera.
//
// The STARTER: every rendered frame of a dialogue shot is read from the engine's own view camera (the camera the frame is drawn
// with) and kept relative to the conversation's main NPC (the anchor); the agent polls them (op 95) and sends them as ConvCam.
// The LISTENER: the samples are played 100 ms behind the starter's clock (wo203_cam_rules.h) and the rendered view follows them --
// position, rotation and field of view -- through CryAction's own view system: a hidden mod-owned TagPoint entity, a view of the
// view system linked to it (the view takes the entity's world transform as the camera, the engine's own path for a view linked to
// an entity with no game-object view), made the active view. Each frame the entity is placed and the view's FOV set. Leaving puts
// the view that was active back, removes the view and the entity. The player's own look input is never read: while listening the
// game's own dialogue lock (an exclusive action map, Lua) keeps the mouse from turning Henry, and the view never looks at input.
//
// How each piece is found (port-aware; nothing here is an address):
//   * ISystem: the gEnv field whose object's vftable is RTTI ".?AVCSystem@@" (CrySystem); CCryAction: the game interface's field
//     (wh::GetGameIface) whose object's vftable is RTTI ".?AVCCryAction@@" (gEnv on 1.5.5 holds none); CViewSystem: the CCryAction
//     field whose object's vftable is RTTI ".?AVCViewSystem@@".
//   * The rendered camera: ISystem::GetViewCamera, the slot the game's own Lua bind System.GetViewCameraPos calls (0x430 on 1.5.5,
//     checked: it returns a CCamera inside the CSystem object whose matrix is finite and whose FOV is plausible).
//   * The view system's and the view's slots (5.7.1's IViewSystem / IView order, overloads reversed by MSVC) are checked by what
//     they do the first time: CreateView returns an object with CView's vftable, LinkTo makes GetLinkedId return the entity's id,
//     GetView(GetViewId(view)) is the view, SetActiveView(id) makes GetActiveViewId return it. A check that fails = the area stays
//     off, logged, and the player's camera is never touched.
//   * The entity's transform: IEntity::SetPosRotScale, checked by reading the entity's own position and rotation back.
// A camera override never outlives its reason: every path out restores (op 94 leave, 3 s of stream silence, the view taken by
// someone else -- a cutscene: the view is kept idle and resumes or is removed when it comes back --, the view or the entity gone --
// a load --, a fault in any call -- the restore runs first, before anything else).
//
// Pipe ops (wo203.h, the conv range):
//   94 ConvView (listener) [op][cmd] ->
//      cmd 1 enter  [anchorEid:4]  -> [result:1] 0 on, 1 not resolved, 2 no entity, 3 view refused, 4 activation refused
//      cmd 2 samples [n:1] n x {t:i64 local QPC ms, pos 3f, quat 4f, fov f, cut:1, the starter's anchor x,y,z,yaw 4f}  -> [kept:1]
//            (relative to the anchor: framed on the listener's own copy of it, else on the starter's anchor; anchor 0 + direct = world)
//      cmd 3 leave  [why:1]         -> [was:1]
//      cmd 4 status                 -> text
//      cmd 5 pose   {pos 3f, quat 4f, fov f}  -> [result:1]  the probe's direct pose: applied every frame, no play-out
//      cmd 6 readback               -> {t:i64, m34 12f, fov f, state:1, active:4, ours:4}  the frame's rendered view
//      cmd 7 enter by name [len:1][name] -> as cmd 1 (the anchor found by its entity name; none = world poses)
//      cmd 8 state                  -> [state:1 (0 off, 1 on, 2 yielded)][lastWhy:1 (255 none)][enters:4][leaves:4][frames:4]
//   95 ConvCamPoll (starter) [op][cmd] ->
//      cmd 1 arm    [anchorEid:4][every:1, optional] -> [result:1] 0 armed, 1 not resolved
//      cmd 0 disarm               -> [was:1]
//      cmd 2 poll                 -> [n:1] n x {t:i64, rel pos 3f, rel quat 4f, fov f, anchor 3f, yaw f, flags:1 (1 cut)}
//                                    WO-207 A.1: served on the pipe's reader thread (the ring under g_sLock); cmd 9 = the same poll on the
//                                    game's main thread (the agent's mp_conv_pipe_first off, the twin)
//      cmd 3 status               -> text
//      cmd 4 arm by name [every:1][len:1][name] -> [result:1] 0 armed, 2 no such entity
#include "wo203.h"
#include "wo203_cam_rules.h"
#include "wo163.h"
#include "engine.h"
#include "anchors.h"
#include "respawn_actions.h"
#include "fault_guard.h"
#include "log.h"
#include <windows.h>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>

namespace kcdmp::wo203::cam {

namespace {

using wo203cam::Pose;
using wo203cam::Quat;
using wo203cam::Sample;
using wo203cam::Play;

constexpr uint8_t kOpView = 94, kOpPoll = 95;

// Slots (5.7.1 order on 1.5.5; each checked by behaviour before it is trusted, see the header).
constexpr size_t kSysGetViewCamera = 0x430;
constexpr size_t kVsCreateView = 0x08, kVsRemoveViewId = 0x10, kVsSetActiveViewId = 0x20, kVsSetActiveViewPtr = 0x28,
                 kVsGetView = 0x30, kVsGetViewId = 0x48, kVsGetActiveViewId = 0x50;
constexpr size_t kViewLinkToEntity = 0x18, kViewGetLinkedId = 0x28, kViewGetCurrentParams = 0x38;
constexpr size_t kParamsFov = 0x30;   // SViewParams: position 0x00, rotation 0x0C, ..., fov 0x30 (DialogModule's own view params agree)
constexpr size_t kEntGetWorldTM = 0x100, kEntGetPos = 0x140, kEntGetRotation = 0x150, kEntSetPosRotScale = 0x168;
constexpr size_t kCameraFov = 0x30;   // CCamera: Matrix34 at 0x00, fov at 0x30
constexpr uint32_t kNoView = ~0u;
constexpr char kCamClass[] = "TagPoint";
constexpr char kCamName[] = "kcd2mp_convcam";

fault::Site g_readSite{"wo203cam::read", fault::Kind::Read};
fault::Site g_writeSite{"wo203cam::write", fault::Kind::Read};
fault::Site g_callSite{"wo203cam::call", fault::Kind::Call};
fault::Site g_restoreSite{"wo203cam::restore", fault::Kind::Read};   // never switched off: the way back always runs

template <class T> bool rd(const void* base, ptrdiff_t off, T* out) {
    if (!base) return false;
    const uintptr_t a = reinterpret_cast<uintptr_t>(base) + static_cast<uintptr_t>(off);
    if (!fault::plausible_address(a)) return false;
    return fault::guarded(g_readSite, [&] { std::memcpy(out, reinterpret_cast<const void*>(a), sizeof(T)); });
}
template <class T> bool wr(void* base, ptrdiff_t off, const T& v) {
    if (!base) return false;
    const uintptr_t a = reinterpret_cast<uintptr_t>(base) + static_cast<uintptr_t>(off);
    if (!fault::plausible_address(a)) return false;
    return fault::guarded(g_writeSite, [&] { std::memcpy(reinterpret_cast<void*>(a), &v, sizeof(T)); });
}
void* vslot(void* obj, size_t off) {
    void* vt = nullptr; void* fn = nullptr;
    if (!obj || !rd(obj, 0, &vt) || !vt || !rd(vt, static_cast<ptrdiff_t>(off), &fn)) return nullptr;
    return fn;
}
bool is_a(void* obj, void* const* vft) {
    void* vp = nullptr;
    return obj && vft && rd(obj, 0, &vp) && vp == static_cast<const void*>(vft);
}
template <typename R, typename... A>
bool vcall(fault::Site& site, void* obj, size_t off, R* out, A... a) {
    void* fn = vslot(obj, off);
    if (!fn) return false;
    return fault::guarded(site, [&] { *out = reinterpret_cast<R (*)(void*, A...)>(fn)(obj, a...); });
}
template <typename... A>
bool vcall_void(fault::Site& site, void* obj, size_t off, A... a) {
    void* fn = vslot(obj, off);
    if (!fn) return false;
    return fault::guarded(site, [&] { reinterpret_cast<void (*)(void*, A...)>(fn)(obj, a...); });
}

// The harness's readback (Part 0.3, research): with KCDMP_W203B_READBACK=<file> in the game's environment, every frame while the
// view is on -- and for 3 s after each way out -- one line: local ms, state, the rendered view (pos, quat, fov), the anchor used.
// Unset (every player): nothing is opened or written.
struct Trace {
    bool tried = false;
    FILE* f = nullptr;
    int64_t until = 0;
    int64_t lastFlush = 0;
} g_trace;

int64_t now_ms() {
    static LARGE_INTEGER f{};
    if (!f.QuadPart) QueryPerformanceFrequency(&f);
    LARGE_INTEGER c;
    QueryPerformanceCounter(&c);
    return static_cast<int64_t>(c.QuadPart * 1000 / f.QuadPart);
}
double now_us() {
    static LARGE_INTEGER f{};
    if (!f.QuadPart) QueryPerformanceFrequency(&f);
    LARGE_INTEGER c;
    QueryPerformanceCounter(&c);
    return double(c.QuadPart) * 1e6 / double(f.QuadPart);
}

// ---------------------------------------------------------------- the engine's pieces

struct Eng {
    bool tried = false, ok = false;
    const char* why = "not resolved";
    void* sys = nullptr;
    void* vs = nullptr;
    void* const* vftView = nullptr;
    size_t camOff = 0;   // the rendered CCamera inside CSystem
} g_eng;

void* field_of_type(void* obj, size_t span, void* const* vft) {
    for (size_t off = 0; off < span; off += 8) {
        void* p = nullptr;
        if (rd(obj, static_cast<ptrdiff_t>(off), &p) && p && fault::plausible_address(reinterpret_cast<uintptr_t>(p)) && is_a(p, vft)) return p;
    }
    return nullptr;
}

bool resolve() {
    if (g_eng.ok) return true;
    if (g_eng.tried) return false;
    if (!engine::ready()) return false;   // the engine's own resolver first (gEnv); retried until it is
    g_eng.tried = true;
    auto fail = [](const char* why) { g_eng.why = why; logf("WO203B-CAM resolve FAILED -- %s; the conversation camera stays off", why); return false; };
    HMODULE ca = GetModuleHandleA("CryAction.dll"), cs = GetModuleHandleA("CrySystem.dll");
    if (!ca || !cs) return fail("CryAction.dll / CrySystem.dll not loaded");
    void* const* vSys = anchor::find_vftable(cs, ".?AVCSystem@@");
    void* const* vAct = anchor::find_vftable(ca, ".?AVCCryAction@@");
    void* const* vVs = anchor::find_vftable(ca, ".?AVCViewSystem@@");
    g_eng.vftView = anchor::find_vftable(ca, ".?AVCView@@");
    if (!vSys || !vAct || !vVs || !g_eng.vftView) return fail("RTTI CSystem / CCryAction / CViewSystem / CView vftable not unique");
    void* env = engine::genv();
    if (!env) return fail("no gEnv");
    g_eng.sys = field_of_type(env, 0x400, vSys);
    if (!g_eng.sys) return fail("gEnv holds no CSystem");
    // the game's interface holds the framework (1.5.5: its +8, the IGameFramework the dialogue input uses); gEnv's copy, if any
    void* gi = engine::game_iface();
    void* fw = gi ? field_of_type(gi, 0x200, vAct) : nullptr;
    if (!fw) fw = field_of_type(env, 0x1000, vAct);
    if (!fw) return fail("neither the game interface nor gEnv holds the CCryAction");
    g_eng.vs = field_of_type(fw, 0x2000, vVs);
    if (!g_eng.vs) return fail("CCryAction holds no CViewSystem");
    void* cam = nullptr;
    if (!vcall(g_callSite, g_eng.sys, kSysGetViewCamera, &cam) || !cam) return fail("ISystem::GetViewCamera call failed");
    const uintptr_t s = reinterpret_cast<uintptr_t>(g_eng.sys), c = reinterpret_cast<uintptr_t>(cam);
    float m[12]{}, fov = 0;
    if (c <= s || c - s > 0x8000 || !rd(cam, 0, &m) || !rd(cam, kCameraFov, &fov) || !std::isfinite(m[3]) || !(fov > 0.1f && fov < 3.0f))
        return fail("ISystem slot 0x430 is not GetViewCamera (no CCamera inside CSystem)");
    g_eng.camOff = c - s;
    char d1[160], d2[160];
    anchor::describe(g_eng.vs, d1, sizeof d1);
    anchor::describe(cam, d2, sizeof d2);
    logf("WO203B-CAM resolved: view system %s (CCryAction field), rendered camera CSystem+0x%zx (fov %.3f), by RTTI and the "
         "Lua bind's slot", d1, g_eng.camOff, fov);
    g_eng.ok = true;
    g_eng.why = "ok";
    return true;
}

// The frame's rendered view: m34 + fov.
bool read_camera(float m[12], float* fov) {
    if (!g_eng.ok) return false;
    const void* cam = static_cast<const char*>(g_eng.sys) + g_eng.camOff;
    return rd(cam, 0, reinterpret_cast<float(*)[12]>(m)) && rd(cam, kCameraFov, fov) && std::isfinite(m[3]) && std::isfinite(*fov);
}

bool entity_tm(uint32_t eid, float m[12]) {
    void* e = engine::entity_by_id(eid);
    const float* tm = nullptr;
    if (!e || !vcall(g_callSite, e, kEntGetWorldTM, &tm) || !tm) return false;
    return rd(tm, 0, reinterpret_cast<float(*)[12]>(m)) && std::isfinite(m[3]) && std::isfinite(m[7]);
}

// The entity of that exact name (the anchor NPC: the same name on both machines -- the starter's NPC, the listener's own copy of it).
uint32_t entity_named(const char* name) {
    if (!name || !*name) return 0;
    struct Ctx { const char* name; uint32_t id; } c{name, 0};
    engine::for_each_entity([](void* e, void* ctx) -> bool {
        auto* x = static_cast<Ctx*>(ctx);
        const char* n = engine::entity_name(e);
        if (!n || std::strcmp(n, x->name) != 0) return false;
        x->id = engine::entity_id(e);
        return x->id != 0;
    }, &c);
    return c.id;
}

bool get_name(const uint8_t* b, size_t len, size_t* k, char* out, size_t cap) {
    if (*k >= len) return false;
    const size_t n = b[(*k)++];
    if (n == 0 || n >= cap || *k + n > len) return false;
    for (size_t i = 0; i < n; ++i) {
        const char ch = static_cast<char>(b[*k + i]);
        if (ch < 0x21 || ch > 0x7E) return false;
        out[i] = ch;
    }
    out[n] = 0;
    *k += n;
    return true;
}

uint32_t active_view() {
    uint32_t id = kNoView;
    vcall(g_callSite, g_eng.vs, kVsGetActiveViewId, &id);
    return id;
}
void* view_by_id(uint32_t id) {
    void* v = nullptr;
    if (id == kNoView || !vcall(g_callSite, g_eng.vs, kVsGetView, &v, id)) return nullptr;
    return v;
}

// ---------------------------------------------------------------- the LISTENER's view

constexpr int kRing = 48;

enum class St : uint8_t { Off = 0, On, Yielded };

struct View {
    St st = St::Off;
    bool direct = false;
    uint32_t anchorEid = 0, camEid = 0, viewId = kNoView, prevViewId = kNoView;
    void* view = nullptr;
    Sample s[kRing];
    int n = 0;
    int64_t enteredMs = 0, lastArrivalMs = 0, lastOffsetLogMs = 0;
    Pose directPose{};
    float anchor[3]{}, yaw = 0;
    bool haveAnchor = false;
    Pose last{};
    bool haveLast = false;
    // counts
    uint32_t frames = 0, playN[5] = {}, samplesIn = 0, samplesLate = 0, cuts = 0, yields = 0, resumes = 0, frameOffsetLogs = 0, ageOverDelay = 0;
    int64_t ageMax = -1000000;
    double ageSum = 0;
    double sumUs = 0, maxUs = 0;
    // the readback: the frame's rendered view against the pose set on the frame before (the view system draws it after our tick)
    uint32_t rbN = 0, rbOver = 0;
    float rbPosMax = 0, rbDegMax = 0, rbFovMax = 0;
    char lastWhy[24] = "-";
} g_v;
uint8_t g_lastWhyCode = 255;   // the code of the last way out (255 none yet): the agent's watchdog reads it with op 94 cmd 8
SRWLOCK g_ringLock = SRWLOCK_INIT;   // the sample ring and the on/off edge (see add_sample)

std::atomic<uint32_t> c_enters{0}, c_leaves{0}, c_faults{0}, c_refused{0};
std::atomic<uint32_t> c_lateFirst{0};   // WO-207 A.6: views whose first sample came after kSilenceMs

// WO-207 A.6 (cam_first_grace): until a view's first sample the silence rule waits kFirstSampleMs -- the pair's three silence drops
// (d17) all came 3.0-3.3 s after the view went on, before any sample: the talk's first dialogue shot was later. The twin
// (KCDMP_TEST_W207_OFF names cam_first_grace in the game's own environment, the bench only): 3 s from the view's start, as before.
bool first_grace_off() {
    static int v = -1;
    if (v < 0) {
        char b[256]{};
        const DWORD n = GetEnvironmentVariableA("KCDMP_TEST_W207_OFF", b, sizeof b);
        v = (n > 0 && n < sizeof b && std::strstr(b, "cam_first_grace")) ? 1 : 0;
    }
    return v == 1;
}

const char* kWhy[] = {"leave", "end", "silence", "combat", "scene", "load", "menu", "disconnect", "fault", "lost", "taken", "reenter",
                      "hit", "off"};
const char* why_name(uint8_t w) { return w < sizeof kWhy / sizeof kWhy[0] ? kWhy[w] : "other"; }
constexpr uint8_t kWhySilence = 2, kWhyFault = 8, kWhyLost = 9, kWhyReenter = 11;

bool set_entity_pose(uint32_t eid, const Pose& p) {
    void* e = engine::entity_by_id(eid);
    if (!e) return false;
    const float pos[3] = {p.p[0], p.p[1], p.p[2]};
    const float q[4] = {p.q.x, p.q.y, p.q.z, p.q.w};
    const float sc[3] = {1, 1, 1};
    return vcall_void(g_callSite, e, kEntSetPosRotScale, pos, q, sc, uint32_t(0));
}

bool set_view_fov(void* view, float fov) {
    char* params = nullptr;
    if (!vcall(g_callSite, view, kViewGetCurrentParams, &params) || !params) return false;
    return wr(params, kParamsFov, fov);
}

// Every step guarded on its own: one that faults does not stop the next (the player's camera first).
void restore(uint8_t why, bool putBack) {
    if (g_v.st == St::Off) return;
    const uint32_t ours = g_v.viewId;
    bool activeOurs = false, back = false, removed = false, gone = false;
    fault::guarded(g_restoreSite, [&] {
        activeOurs = active_view() == ours;
        if (putBack && activeOurs) {
            void* prev = view_by_id(g_v.prevViewId);
            if (prev) vcall_void(g_restoreSite, g_eng.vs, kVsSetActiveViewId, g_v.prevViewId);
            else vcall_void(g_restoreSite, g_eng.vs, kVsSetActiveViewPtr, static_cast<void*>(nullptr));
            back = active_view() != ours;
        }
    });
    fault::guarded(g_restoreSite, [&] {
        if (view_by_id(ours) == g_v.view && g_v.view) { vcall_void(g_restoreSite, g_eng.vs, kVsRemoveViewId, ours); removed = true; }
    });
    fault::guarded(g_restoreSite, [&] { gone = g_v.camEid && engine::remove(g_v.camEid); });
    const double mean = g_v.frames ? g_v.sumUs / g_v.frames : 0;
    logf("WO203B-VIEW off why=%s active_was_ours=%d put_back=%d view_removed=%d entity_removed=%d frames=%u interp=%u snap=%u extrap=%u "
         "hold=%u samples=%u late=%u age_ms mean=%.0f max=%lld over_delay=%u cuts=%u cost_us mean=%.1f max=%.1f readback n=%u over=%u max_m=%.4f max_deg=%.3f max_fov=%.4f", why_name(why), activeOurs ? 1 : 0, back ? 1 : 0, removed ? 1 : 0,
         gone ? 1 : 0, g_v.frames, g_v.playN[1], g_v.playN[2], g_v.playN[3], g_v.playN[4], g_v.samplesIn, g_v.samplesLate,
         g_v.samplesIn ? g_v.ageSum / g_v.samplesIn : 0.0, static_cast<long long>(g_v.ageMax), g_v.ageOverDelay, g_v.cuts, mean, g_v.maxUs, g_v.rbN, g_v.rbOver,
         g_v.rbPosMax, g_v.rbDegMax, g_v.rbFovMax);
    std::snprintf(g_v.lastWhy, sizeof g_v.lastWhy, "%s", why_name(why));
    g_lastWhyCode = why;
    g_trace.until = now_ms() + 3000;
    AcquireSRWLockExclusive(&g_ringLock);
    g_v.st = St::Off;
    g_v.n = 0;
    ReleaseSRWLockExclusive(&g_ringLock);
    g_v.view = nullptr;
    g_v.viewId = g_v.prevViewId = kNoView;
    g_v.camEid = 0;
    g_v.direct = false;
    c_leaves.fetch_add(1, std::memory_order_relaxed);
}

uint8_t enter(uint32_t anchorEid) {
    if (!resolve()) { c_refused.fetch_add(1); return 1; }
    if (g_v.st != St::Off) restore(kWhyReenter, true);
    float m[12]{}, fov = 1.0f;
    if (!read_camera(m, &fov)) { c_refused.fetch_add(1); return 1; }
    const Pose now = wo203cam::pose_from_camera(m, fov);
    void* e = engine::spawn(kCamClass, kCamName, now.p, engine::kFlagNoSave, 0);
    const uint32_t eid = e ? engine::entity_id(e) : 0;
    if (!eid) { c_refused.fetch_add(1); logf("WO203B-VIEW enter refused: no %s entity", kCamClass); return 2; }
    // SetPosRotScale, checked by reading the entity's own position and rotation back
    const float* rp = nullptr; const float* rq = nullptr;
    float gp[3]{}, gq[4]{};
    const bool posed = set_entity_pose(eid, now) && vcall(g_callSite, e, kEntGetPos, &rp) && vcall(g_callSite, e, kEntGetRotation, &rq) &&
                       rd(rp, 0, &gp) && rd(rq, 0, &gq);
    if (!posed || wo203cam::dist(gp, now.p) > 0.01f || std::fabs(gq[0] * now.q.x + gq[1] * now.q.y + gq[2] * now.q.z + gq[3] * now.q.w) < 0.999f) {
        engine::remove(eid);
        c_refused.fetch_add(1);
        logf("WO203B-VIEW enter refused: IEntity slot 0x%zx did not place the camera entity (read back %.2f %.2f %.2f)", kEntSetPosRotScale, gp[0], gp[1], gp[2]);
        return 2;
    }
    void* view = nullptr;
    uint32_t linked = 0, id = kNoView;
    void* again = nullptr;
    const uint32_t prev = active_view();
    const bool made = vcall(g_callSite, g_eng.vs, kVsCreateView, &view) && is_a(view, g_eng.vftView) &&
                      vcall_void(g_callSite, view, kViewLinkToEntity, e) && vcall(g_callSite, view, kViewGetLinkedId, &linked) && linked == eid &&
                      vcall(g_callSite, g_eng.vs, kVsGetViewId, &id, view) && (again = view_by_id(id)) == view;
    if (!made) {
        if (view && id != kNoView && view_by_id(id) == view) vcall_void(g_callSite, g_eng.vs, kVsRemoveViewId, id);
        engine::remove(eid);
        c_refused.fetch_add(1);
        logf("WO203B-VIEW enter refused: view system slots did not behave (view %p is_view %d linked %u/%u id %u)", view, is_a(view, g_eng.vftView) ? 1 : 0, linked, eid, id);
        return 3;
    }
    set_view_fov(view, now.fov);
    vcall_void(g_callSite, g_eng.vs, kVsSetActiveViewId, id);
    if (active_view() != id) {
        vcall_void(g_callSite, g_eng.vs, kVsRemoveViewId, id);
        engine::remove(eid);
        c_refused.fetch_add(1);
        logf("WO203B-VIEW enter refused: SetActiveView(%u) left %u active", id, active_view());
        return 4;
    }
    AcquireSRWLockExclusive(&g_ringLock);
    g_v = View{};
    g_v.st = St::On;
    g_v.anchorEid = anchorEid;
    g_v.camEid = eid;
    g_v.view = view;
    g_v.viewId = id;
    g_v.prevViewId = prev;
    g_v.enteredMs = now_ms();
    g_v.last = now;
    g_v.haveLast = true;
    ReleaseSRWLockExclusive(&g_ringLock);
    c_enters.fetch_add(1, std::memory_order_relaxed);
    logf("WO203B-VIEW on anchor=%u camera_entity=%u view=%u previous_view=%u fov=%.3f at %.2f %.2f %.2f", anchorEid, eid, id, prev, now.fov,
         now.p[0], now.p[1], now.p[2]);
    return 0;
}

// The sample ring is filled on the pipe's reader thread (op 94 cmd 2 is served off the main thread: the main thread serves the pipe
// about once a frame, and every other agent request shares it -- below ~30 fps the samples came too late to play) and read by the
// frame tick. g_ringLock guards g_v.s / g_v.n / the arrival fields and the state's on/off edge; nothing inside it calls the engine
// or can fault (explicit acquire/release: no RAII across a guarded frame).

bool add_sample(const Sample& x) {
    const int64_t now = now_ms();
    AcquireSRWLockExclusive(&g_ringLock);
    if (g_v.st == St::Off) { ReleaseSRWLockExclusive(&g_ringLock); return false; }
    ++g_v.samplesIn;
    if (!g_v.lastArrivalMs && now - g_v.enteredMs > wo203cam::kSilenceMs) {
        c_lateFirst.fetch_add(1);
        logf("WO207-CAM the view's first sample came %lld ms after it went on (later than the old 3 s rule)", static_cast<long long>(now - g_v.enteredMs));
    }
    g_v.lastArrivalMs = now;
    const int64_t age = now - x.tMs;   // how old the sample is when it arrives (the play-out plays it at +kDelayMs)
    g_v.ageSum += double(age);
    if (age > g_v.ageMax) g_v.ageMax = age;
    if (age > wo203cam::kDelayMs) ++g_v.ageOverDelay;
    bool kept = false;
    if (g_v.n > 0 && x.tMs <= g_v.s[g_v.n - 1].tMs) ++g_v.samplesLate;   // out of order or repeated: dropped
    else {
        if (x.cut) ++g_v.cuts;
        if (g_v.n == kRing) { std::memmove(&g_v.s[0], &g_v.s[1], sizeof(Sample) * (kRing - 1)); --g_v.n; }
        g_v.s[g_v.n++] = x;
        kept = true;
    }
    ReleaseSRWLockExclusive(&g_ringLock);
    return kept;
}

void trim(int64_t playT) {
    // keep one sample at or before the play-out time (the interpolation's left end) and everything after it
    int drop = 0;
    while (drop + 1 < g_v.n && g_v.s[drop + 1].tMs <= playT) ++drop;
    if (drop > 0) { std::memmove(&g_v.s[0], &g_v.s[drop], sizeof(Sample) * size_t(g_v.n - drop)); g_v.n -= drop; }
}

void tick_view() {
    if (g_v.st == St::Off) return;
    const double t0 = now_us();
    const int64_t now = now_ms();
    // the view and the entity still exist (a load may have destroyed both: nothing cached is touched before this check)
    if (view_by_id(g_v.viewId) != g_v.view || !engine::entity_by_id(g_v.camEid)) { restore(kWhyLost, true); return; }
    const uint32_t act = active_view();
    if (g_v.st == St::On && act != g_v.viewId) {
        // someone else took the camera (a cutscene's own camera): never fought; the view waits idle and comes back with it
        g_v.st = St::Yielded;
        ++g_v.yields;
        logf("WO203B-VIEW yielded: view %u active (ours %u) -- the conversation camera waits", act, g_v.viewId);
        return;
    }
    if (g_v.st == St::Yielded) {
        if (act != g_v.viewId) return;
        g_v.st = St::On;
        ++g_v.resumes;
        logf("WO203B-VIEW resumed: our view %u is active again", g_v.viewId);
    }
    if (g_v.frames > 0) {
        float rm[12]{}, rf = 0;
        if (read_camera(rm, &rf)) {
            const Pose r = wo203cam::pose_from_camera(rm, rf);
            const float dp = wo203cam::dist(r.p, g_v.last.p), dd = wo203cam::qangle_deg(r.q, g_v.last.q), df = std::fabs(r.fov - g_v.last.fov);
            ++g_v.rbN;
            if (dp > g_v.rbPosMax) g_v.rbPosMax = dp;
            if (dd > g_v.rbDegMax) g_v.rbDegMax = dd;
            if (df > g_v.rbFovMax) g_v.rbFovMax = df;
            if (dp > 0.01f || dd > 0.5f || df > 0.01f) ++g_v.rbOver;
        }
    }
    if (!g_v.direct) {
        AcquireSRWLockShared(&g_ringLock);
        const int64_t since = g_v.lastArrivalMs ? now - g_v.lastArrivalMs : now - g_v.enteredMs;
        const int64_t limit = (g_v.lastArrivalMs || first_grace_off()) ? wo203cam::kSilenceMs : wo203cam::kFirstSampleMs;
        ReleaseSRWLockShared(&g_ringLock);
        if (since > limit) { restore(kWhySilence, true); return; }
    }
    Pose rel{};
    Play how = Play::None;
    float sa[4] = {0, 0, 0, 0};
    bool haveSa = false;
    if (g_v.direct) { rel = g_v.directPose; how = Play::Hold; }
    else {
        const int64_t playT = now - wo203cam::kDelayMs;
        AcquireSRWLockExclusive(&g_ringLock);
        how = wo203cam::play(g_v.s, g_v.n, playT, &rel);
        trim(playT);
        if (g_v.n > 0) { std::memcpy(sa, g_v.s[0].anchor, sizeof sa); haveSa = true; }   // the starter's anchor of the shot being played
        ReleaseSRWLockExclusive(&g_ringLock);
    }
    Pose w = g_v.last;
    if (how != Play::None) {
        if (g_v.direct) {
            w = rel;
        } else {
            float am[12]{};
            if (g_v.anchorEid && entity_tm(g_v.anchorEid, am)) {
                g_v.anchor[0] = am[3]; g_v.anchor[1] = am[7]; g_v.anchor[2] = am[11];
                g_v.yaw = wo203cam::yaw_from_m34(am);
                g_v.haveAnchor = true;
            }
            if (g_v.haveAnchor) {
                // B.3: the copy stands elsewhere than the starter's anchor did -- logged, and the shot still frames the copy
                if (haveSa && wo203cam::dist(sa, g_v.anchor) > wo203cam::kFrameOffsetMetres && now - g_v.lastOffsetLogMs > 5000) {
                    g_v.lastOffsetLogMs = now;
                    ++g_v.frameOffsetLogs;
                    logf("WO203B-FRAME offset %.2f m: the copy of the anchor at %.2f %.2f %.2f, the starter's at %.2f %.2f %.2f -- framed on the copy",
                         wo203cam::dist(sa, g_v.anchor), g_v.anchor[0], g_v.anchor[1], g_v.anchor[2], sa[0], sa[1], sa[2]);
                }
                w = wo203cam::world(rel, g_v.anchor, g_v.yaw);   // the copy gone a moment: its last transform
            } else if (haveSa) {
                w = wo203cam::world(rel, sa, sa[3]);              // no copy here: where the starter's anchor stood
            }
        }
        g_v.playN[static_cast<int>(how)]++;
    }
    const bool ok = set_entity_pose(g_v.camEid, w) && set_view_fov(g_v.view, w.fov);
    if (!ok) { c_faults.fetch_add(1); restore(kWhyFault, true); return; }
    g_v.last = w;
    ++g_v.frames;
    const double us = now_us() - t0;
    g_v.sumUs += us;
    if (us > g_v.maxUs) g_v.maxUs = us;
}

// ---------------------------------------------------------------- the STARTER's sampler

constexpr int kOutRing = 64;
constexpr int64_t kSendEveryMs = 33;   // at most 30 Hz; a cut always at once
constexpr int64_t kOrphanMs = 5000;
constexpr int64_t kFeetEveryMs = 250;

struct Sampler {
    bool armed = false, every = false;   // every: each rendered frame queued (the harness's recordings), not 30 Hz
    uint32_t anchorEid = 0;
    struct S { int64_t t; Pose rel; float anchor[3]; float yaw; uint8_t flags; } q[kOutRing];
    int n = 0;
    bool havePrev = false, prevQueued = true, cutPending = false;
    S prev{};            // the previous rendered frame (queued or not)
    int64_t lastQueuedMs = 0;
    int64_t lastPollMs = 0;              // nobody polled for kOrphanMs: the agent went away -- disarmed
    int64_t feetAtMs = 0;                // the player's position, read at most every kFeetEveryMs (it is the costly read)
    float feet[3] = {0, 0, 0};
    bool haveFeet = false;
    uint32_t frames = 0, queued = 0, cuts = 0, inHead = 0, noAnchor = 0, dropped = 0, polled = 0;
    double sumUs = 0, maxUs = 0;
} g_s;
// WO-207 A.1: the ring (q, n), polled and lastPollMs are shared with the off-main poll (cmd 2); a whole-sampler reset (arm, disarm) too
SRWLOCK g_sLock = SRWLOCK_INIT;
std::atomic<uint32_t> c_pollsOffMain{0};

void queue(const Sampler::S& x) {
    AcquireSRWLockExclusive(&g_sLock);
    if (g_s.n == kOutRing) { std::memmove(&g_s.q[0], &g_s.q[1], sizeof(Sampler::S) * (kOutRing - 1)); --g_s.n; ++g_s.dropped; }
    g_s.q[g_s.n++] = x;
    ++g_s.queued;
    g_s.lastQueuedMs = x.t;
    ReleaseSRWLockExclusive(&g_sLock);
}

void tick_sampler() {
    if (!g_s.armed) return;
    const double t0 = now_us();
    float m[12]{}, fov = 1.0f, am[12]{};
    if (!read_camera(m, &fov)) return;
    if (!entity_tm(g_s.anchorEid, am)) { ++g_s.noAnchor; return; }
    const Pose cam = wo203cam::pose_from_camera(m, fov);
    const int64_t nowMs = now_ms();
    AcquireSRWLockShared(&g_sLock);
    const int64_t lastPoll = g_s.lastPollMs;
    ReleaseSRWLockShared(&g_sLock);
    if (nowMs - lastPoll > kOrphanMs) {
        g_s.armed = false;
        logf("WO203B-CAMPOLL disarmed: not polled for %lld ms (the agent went away) frames=%u queued=%u", static_cast<long long>(nowMs - lastPoll), g_s.frames, g_s.queued);
        return;
    }
    if (nowMs - g_s.feetAtMs >= kFeetEveryMs) {
        g_s.feetAtMs = nowMs;
        g_s.haveFeet = actions::player_position(g_s.feet);
    }
    if (g_s.haveFeet && wo203cam::in_player_head(cam.p, g_s.feet)) {
        // B.6: the starter's own first-person camera (before the first shot, after the last): never sent; the next shot is a cut
        ++g_s.inHead;
        g_s.cutPending = true;
        g_s.havePrev = false;
        return;
    }
    Sampler::S x{};
    x.t = now_ms();
    x.anchor[0] = am[3]; x.anchor[1] = am[7]; x.anchor[2] = am[11];
    x.yaw = wo203cam::yaw_from_m34(am);
    x.rel = wo203cam::relative(cam, x.anchor, x.yaw);
    const bool jump = g_s.havePrev && wo203cam::is_jump(g_s.prev.rel, x.rel);
    if (jump || g_s.cutPending) {
        x.flags |= 1;
        ++g_s.cuts;
        if (jump && !g_s.prevQueued) queue(g_s.prev);   // the last frame of the shot before, so the listener holds it to the cut
        queue(x);
        g_s.cutPending = false;
        g_s.prevQueued = true;
    } else if (g_s.every || x.t - g_s.lastQueuedMs >= kSendEveryMs) {
        queue(x);
        g_s.prevQueued = true;
    } else {
        g_s.prevQueued = false;
    }
    g_s.prev = x;
    g_s.havePrev = true;
    ++g_s.frames;
    const double us = now_us() - t0;
    g_s.sumUs += us;
    if (us > g_s.maxUs) g_s.maxUs = us;
}

void trace_frame() {
    if (!g_trace.tried) {
        g_trace.tried = true;
        char path[512] = {};
        if (GetEnvironmentVariableA("KCDMP_W203B_READBACK", path, sizeof path) > 0 && path[0]) {
            g_trace.f = std::fopen(path, "a");
            logf("WO203B-READBACK %s %s", g_trace.f ? "writing" : "could not open", path);
        }
    }
    if (!g_trace.f || !g_eng.ok) return;
    const int64_t now = now_ms();
    if (g_v.st == St::Off && now > g_trace.until) return;
    float m[12]{}, fov = 0;
    if (!read_camera(m, &fov)) return;
    const Pose r = wo203cam::pose_from_camera(m, fov);
    std::fprintf(g_trace.f, "%lld %d %.4f %.4f %.4f %.5f %.5f %.5f %.5f %.4f %.3f %.3f %.3f %.4f %d\n", static_cast<long long>(now), static_cast<int>(g_v.st),
                 r.p[0], r.p[1], r.p[2], r.q.x, r.q.y, r.q.z, r.q.w, r.fov, g_v.anchor[0], g_v.anchor[1], g_v.anchor[2], g_v.yaw, g_v.haveAnchor ? 1 : 0);
    if (now - g_trace.lastFlush > 200) { std::fflush(g_trace.f); g_trace.lastFlush = now; }
}

// The harness's B.5 "a fault inside the override" (research): only with the readback trace on, a file kcdmp-w203b-fault.txt beside
// the game makes the next frame of the view fault inside its guard (deleted first, so it fires once).
bool fault_test_due() {
    if (!g_trace.f) return false;
    static int64_t lastCheck = 0;
    const int64_t now = now_ms();
    if (now - lastCheck < 250) return false;
    lastCheck = now;
    if (GetFileAttributesA("kcdmp-w203b-fault.txt") == INVALID_FILE_ATTRIBUTES) return false;
    DeleteFileA("kcdmp-w203b-fault.txt");
    logf("WO203B-VIEW test: a deliberate fault inside the override");
    return true;
}

void tick() {
    fault::guarded_silent([] { trace_frame(); });
    if (g_v.st != St::Off) {
        const bool injected = fault_test_due();
        const bool ok = fault::guarded(g_callSite, [injected] {
            if (injected) *static_cast<volatile int*>(nullptr) = 1;
            tick_view();
        });
        if (!ok) { c_faults.fetch_add(1); restore(kWhyFault, true); }
    }
    if (g_s.armed) {
        const bool ok = fault::guarded(g_callSite, [] { tick_sampler(); });
        if (!ok) { g_s.armed = false; logf("WO203B-CAMPOLL sampler FAULTED -- disarmed"); }
    }
}

// ---------------------------------------------------------------- pipe ops

template <class T> void put(uint8_t* out, size_t* k, const T& v) { std::memcpy(out + *k, &v, sizeof(T)); *k += sizeof(T); }
template <class T> bool get(const uint8_t* b, size_t len, size_t* k, T* v) {
    if (*k + sizeof(T) > len) return false;
    std::memcpy(v, b + *k, sizeof(T));
    *k += sizeof(T);
    return true;
}
bool get_pose(const uint8_t* b, size_t len, size_t* k, Pose* p) {
    return get(b, len, k, &p->p[0]) && get(b, len, k, &p->p[1]) && get(b, len, k, &p->p[2]) && get(b, len, k, &p->q.x) && get(b, len, k, &p->q.y) &&
           get(b, len, k, &p->q.z) && get(b, len, k, &p->q.w) && get(b, len, k, &p->fov) && std::isfinite(p->p[0]) && std::isfinite(p->p[1]) &&
           std::isfinite(p->p[2]) && std::isfinite(p->fov) && p->fov > 0.05f && p->fov < 3.0f;
}

int view_status(char* out, int n) {
    const double mean = g_v.frames ? g_v.sumUs / g_v.frames : 0;
    const double smean = g_s.frames ? g_s.sumUs / g_s.frames : 0;
    const char* st = g_v.st == St::On ? "on" : g_v.st == St::Yielded ? "yielded" : "off";
    return std::snprintf(out, size_t(n),
                         "resolve=%s view=%s%s anchor=%u view_id=%u prev=%u active=%u n=%d frames=%u interp=%u snap=%u extrap=%u hold=%u samples=%u "
                         "late=%u age_ms mean=%.0f max=%lld over_delay=%u cuts=%u yields=%u resumes=%u frame_offsets=%u cost_us=%.1f/%.1f readback n=%u over=%u max_m=%.4f max_deg=%.3f max_fov=%.4f "
                         "enters=%u leaves=%u refused=%u faults=%u late_first=%u last_why=%s | sampler=%s "
                         "frames=%u queued=%u cuts=%u in_head=%u no_anchor=%u dropped=%u polled=%u cost_us=%.1f/%.1f",
                         g_eng.why, st, g_v.direct ? "(direct)" : "", g_v.anchorEid, g_v.viewId, g_v.prevViewId,
                         g_eng.ok ? active_view() : kNoView, g_v.n, g_v.frames, g_v.playN[1], g_v.playN[2], g_v.playN[3], g_v.playN[4], g_v.samplesIn,
                         g_v.samplesLate, g_v.samplesIn ? g_v.ageSum / g_v.samplesIn : 0.0, static_cast<long long>(g_v.ageMax), g_v.ageOverDelay, g_v.cuts, g_v.yields, g_v.resumes, g_v.frameOffsetLogs, mean, g_v.maxUs, g_v.rbN, g_v.rbOver, g_v.rbPosMax, g_v.rbDegMax,
                         g_v.rbFovMax, c_enters.load(), c_leaves.load(), c_refused.load(),
                         c_faults.load(), c_lateFirst.load(), g_v.lastWhy, g_s.armed ? "armed" : "off", g_s.frames, g_s.queued, g_s.cuts, g_s.inHead, g_s.noAnchor,
                         g_s.dropped, g_s.polled, smean, g_s.maxUs);
}

uint8_t op_view(uint8_t, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    if (len < 2) return wo163::kRBadRequest;
    const uint8_t cmd = body[1];
    size_t k = 2;
    *outLen = 0;
    switch (cmd) {
        case 1: {
            uint32_t a = 0;
            if (!get(body, len, &k, &a)) return wo163::kRBadRequest;
            out[0] = enter(a);
            *outLen = 1;
            return wo163::kROk;
        }
        case 7: {
            char name[96];
            if (!get_name(body, len, &k, name, sizeof name)) return wo163::kRBadRequest;
            const uint32_t a = resolve() ? entity_named(name) : 0;
            if (!a) logf("WO203B-VIEW enter: no entity named '%s' -- the view frames world poses held where the starter's were", name);
            out[0] = enter(a);
            *outLen = 1;
            return wo163::kROk;
        }
        case 2: {
            uint8_t cnt = 0, kept = 0;
            if (!get(body, len, &k, &cnt)) return wo163::kRBadRequest;
            for (uint8_t i = 0; i < cnt; ++i) {
                Sample x{};
                uint8_t cut = 0;
                if (!get(body, len, &k, &x.tMs) || !get_pose(body, len, &k, &x.pose) || !get(body, len, &k, &cut) || !get(body, len, &k, &x.anchor[0]) ||
                    !get(body, len, &k, &x.anchor[1]) || !get(body, len, &k, &x.anchor[2]) || !get(body, len, &k, &x.anchor[3]))
                    return wo163::kRBadRequest;
                x.pose.q = wo203cam::qnorm(x.pose.q);
                x.cut = cut != 0;
                if (add_sample(x)) ++kept;
            }
            out[0] = kept;
            *outLen = 1;
            return wo163::kROk;
        }
        case 3: {
            uint8_t why = 0;
            get(body, len, &k, &why);
            out[0] = g_v.st != St::Off ? 1 : 0;
            restore(why, true);
            *outLen = 1;
            return wo163::kROk;
        }
        case 4: {
            char t[900];
            int m = view_status(t, sizeof t);
            if (m < 0) m = 0;
            if (size_t(m) > cap) m = int(cap);
            std::memcpy(out, t, size_t(m));
            *outLen = size_t(m);
            return wo163::kROk;
        }
        case 5: {
            Pose p{};
            if (!get_pose(body, len, &k, &p)) return wo163::kRBadRequest;
            p.q = wo203cam::qnorm(p.q);
            out[0] = 1;
            if (g_v.st != St::Off) { g_v.direct = true; g_v.directPose = p; out[0] = 0; }
            *outLen = 1;
            return wo163::kROk;
        }
        case 6: {
            if (!resolve() || cap < 8 + 52 + 9) return wo163::kRFailed;
            float m[12]{}, fov = 0;
            if (!read_camera(m, &fov)) return wo163::kRFailed;
            size_t o = 0;
            put(out, &o, now_ms());
            for (float f : m) put(out, &o, f);
            put(out, &o, fov);
            put(out, &o, static_cast<uint8_t>(g_v.st));
            put(out, &o, active_view());
            put(out, &o, g_v.viewId);
            *outLen = o;
            return wo163::kROk;
        }
        case 8: {
            size_t o = 0;
            put(out, &o, static_cast<uint8_t>(g_v.st));
            put(out, &o, g_lastWhyCode);
            put(out, &o, c_enters.load());
            put(out, &o, c_leaves.load());
            put(out, &o, g_v.frames);
            *outLen = o;
            return wo163::kROk;
        }
        default: return wo163::kRBadRequest;
    }
}

uint8_t op_poll(uint8_t, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    if (len < 2) return wo163::kRBadRequest;
    const uint8_t cmd = body[1];
    size_t k = 2;
    *outLen = 0;
    switch (cmd) {
        case 0:
            out[0] = g_s.armed ? 1 : 0;
            if (g_s.armed) logf("WO203B-CAMPOLL disarmed frames=%u queued=%u cuts=%u in_head=%u no_anchor=%u dropped=%u polled=%u off_main=%u cost_us mean=%.1f max=%.1f",
                                g_s.frames, g_s.queued, g_s.cuts, g_s.inHead, g_s.noAnchor, g_s.dropped, g_s.polled, c_pollsOffMain.load(),
                                g_s.frames ? g_s.sumUs / g_s.frames : 0, g_s.maxUs);
            AcquireSRWLockExclusive(&g_sLock);
            g_s.armed = false;
            g_s.n = 0;
            ReleaseSRWLockExclusive(&g_sLock);
            *outLen = 1;
            return wo163::kROk;
        case 1: {
            uint32_t a = 0;
            if (!get(body, len, &k, &a)) return wo163::kRBadRequest;
            if (!resolve() || !a) { out[0] = 1; *outLen = 1; return wo163::kROk; }
            uint8_t every = 0;
            get(body, len, &k, &every);
            AcquireSRWLockExclusive(&g_sLock);
            g_s = Sampler{};
            g_s.armed = true;
            g_s.every = every != 0;
            g_s.anchorEid = a;
            g_s.lastPollMs = now_ms();
            g_s.cutPending = true;   // the first shot is a cut
            ReleaseSRWLockExclusive(&g_sLock);
            logf("WO203B-CAMPOLL armed anchor=%u%s", a, g_s.every ? " (every frame)" : "");
            out[0] = 0;
            *outLen = 1;
            return wo163::kROk;
        }
        case 2:
        case 9: {
            constexpr size_t kEach = 8 + 32 + 16 + 1;
            const int fit = int((cap - 1) / kEach);
            AcquireSRWLockExclusive(&g_sLock);
            const int cnt = g_s.n < fit ? g_s.n : fit;
            size_t o = 0;
            put(out, &o, static_cast<uint8_t>(cnt));
            for (int i = 0; i < cnt; ++i) {
                const auto& x = g_s.q[i];
                put(out, &o, x.t);
                for (float f : x.rel.p) put(out, &o, f);
                put(out, &o, x.rel.q.x); put(out, &o, x.rel.q.y); put(out, &o, x.rel.q.z); put(out, &o, x.rel.q.w);
                put(out, &o, x.rel.fov);
                for (float f : x.anchor) put(out, &o, f);
                put(out, &o, x.yaw);
                put(out, &o, x.flags);
            }
            if (cnt > 0) { std::memmove(&g_s.q[0], &g_s.q[cnt], sizeof(Sampler::S) * size_t(g_s.n - cnt)); g_s.n -= cnt; }
            ++g_s.polled;
            g_s.lastPollMs = now_ms();
            ReleaseSRWLockExclusive(&g_sLock);
            *outLen = o;
            return wo163::kROk;
        }
        case 4: {
            uint8_t every = 0;
            char name[96];
            if (!get(body, len, &k, &every) || !get_name(body, len, &k, name, sizeof name)) return wo163::kRBadRequest;
            const uint32_t a = resolve() ? entity_named(name) : 0;
            if (!a) { logf("WO203B-CAMPOLL arm refused: no entity named '%s'", name); out[0] = 2; *outLen = 1; return wo163::kROk; }
            AcquireSRWLockExclusive(&g_sLock);
            g_s = Sampler{};
            g_s.armed = true;
            g_s.every = every != 0;
            g_s.anchorEid = a;
            g_s.lastPollMs = now_ms();
            g_s.cutPending = true;
            ReleaseSRWLockExclusive(&g_sLock);
            logf("WO203B-CAMPOLL armed anchor=%s (%u)%s", name, a, g_s.every ? " (every frame)" : "");
            out[0] = 0;
            *outLen = 1;
            return wo163::kROk;
        }
        case 3: {
            char t[900];
            int m = view_status(t, sizeof t);
            if (m < 0) m = 0;
            if (size_t(m) > cap) m = int(cap);
            std::memcpy(out, t, size_t(m));
            *outLen = size_t(m);
            return wo163::kROk;
        }
        default: return wo163::kRBadRequest;
    }
}

int status(char* out, int n) { return view_status(out, n); }

// Op 94 cmd 2 (samples) on the pipe's reader thread; every other command goes to the main thread as before.
bool op_view_off_main(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen, uint8_t* reason) {
    if (len < 2 || body[1] != 2) return false;
    *reason = op_view(op, body, len, out, cap, outLen);
    return true;
}

// WO-207 A.1: op 95 cmd 2 (the starter's poll) on the pipe's reader thread -- it only reads this file's own ring (g_sLock), no engine
// call. At 27-45 fps the pipe's one request a frame left the camera samples 0.2-2.5 s late and the listener's view ended for silence.
bool op_poll_off_main(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen, uint8_t* reason) {
    if (len < 2 || body[1] != 2) return false;
    c_pollsOffMain.fetch_add(1, std::memory_order_relaxed);
    *reason = op_poll(op, body, len, out, cap, outLen);
    return true;
}

} // namespace

const bool kReg = wo203::reg_tick("cam", &tick) && wo203::reg_status("cam", &status) && wo203::reg_op(kOpView, &op_view) &&
                  wo203::reg_op(kOpPoll, &op_poll) && wo203::reg_op_off_main(kOpView, &op_view_off_main) &&
                  wo203::reg_op_off_main(kOpPoll, &op_poll_off_main);

} // namespace kcdmp::wo203::cam
