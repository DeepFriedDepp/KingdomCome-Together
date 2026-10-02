// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#include "main_thread.h"
#include "fault_guard.h"
#include "frame_meter.h"
#include "iat_hook.h"
#include "log.h"

#include <windows.h>
#include <algorithm>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdio>
#include <memory>
#include <mutex>
#include <numeric>
#include <string>
#include <vector>

namespace kcdmp::main_thread {

namespace {

// C_ModulesManager::Update(float) -- a non-static member, so the real
// signature is (this, dt). Declared explicitly rather than as a method pointer
// so the calling convention is unambiguous.
using UpdateFn = void (*)(void* self, float dt);

UpdateFn  g_original = nullptr;
HMODULE   g_importer = nullptr;
bool      g_installed = false;

// WO-151: each repeating task carries its own fault site, so a task that faults every
// frame is switched off alone (after 8, fault_guard.h) and the rest keep running.
struct Repeating {
    std::function<void()> fn;
    std::string           siteName;
    const char*           name;
    fault::Site           site;
    Repeating(const char* n, std::function<void()> f)
        : fn(std::move(f)), siteName(std::string("main::task/") + (n ? n : "?")), name(n ? n : "?"),
          site(siteName.c_str(), fault::Kind::Call) {}
};

std::mutex                              g_mutex;
std::vector<std::function<void()>>      g_queue;
std::vector<std::shared_ptr<Repeating>> g_repeating;
std::condition_variable                 g_drained;
std::atomic<unsigned long long>         g_frames{0};
std::atomic<float>                      g_lastDt{0.0f};   // WO-138
DWORD                                   g_main_thread_id = 0;

// One-shot work (the pipe's commands, posted fixes): the calls into the game inside it
// have their own sites; this one catches what escapes them. Never switched off -- it
// carries every command, and switching it off would stop the agent's whole channel.
fault::Site g_queuedSite{"main::queued-task", fault::Kind::Read};

double g_usPerTick = 0;
double now_us() {
    LARGE_INTEGER c;
    QueryPerformanceCounter(&c);
    return static_cast<double>(c.QuadPart) * g_usPerTick;
}

// WO-151 Phase 0.4: the FRAME line every 60 s (frame_meter.h), always on.
framemeter::Window g_frame;

void note_frame_rate(double nowUs, double oursUs) {
    if (!framemeter::add(g_frame, nowUs, oursUs)) return;
    char buf[400];
    int n = framemeter::format(g_frame, buf, sizeof buf);
    const fault::Totals t = fault::totals();
    if (n > 0 && n < static_cast<int>(sizeof buf))
        std::snprintf(buf + n, sizeof buf - n, " faults=%u sites=%u off=%u", t.faults, t.sites, t.off);
    logf("%s", buf);
    framemeter::restart(g_frame);
}

// WO-148: the MAIN-COST meter -- what this hook adds to the game's frame, per
// repeating task and for the queued pipe work, over 10 s windows. The game's own
// profiler cannot tell: it books everything this hook runs after the original to
// the caller's self time (CCryAction::PreSystemUpdate). Main thread only.
// WO-151: behind mp_main_cost (off by default; the hunt's tool, kept for the next one).
std::atomic<bool> g_costOn{false};
struct TaskCost { const char* name = "?"; double sumUs = 0, maxUs = 0; };
std::vector<TaskCost> g_cost;
double   g_winStartUs = 0;
uint32_t g_winFrames = 0, g_winItems = 0;
double   g_origSumUs = 0, g_oursSumUs = 0, g_oursMaxUs = 0, g_batchSumUs = 0;
constexpr double kCostWindowUs = 10e6;

void note_cost(double origUs, double oursUs, double batchUs, uint32_t items, double nowUs) {
    if (g_winStartUs == 0) g_winStartUs = nowUs;
    ++g_winFrames;
    g_winItems += items;
    g_origSumUs += origUs;
    g_oursSumUs += oursUs;
    g_batchSumUs += batchUs;
    if (oursUs > g_oursMaxUs) g_oursMaxUs = oursUs;
    if (nowUs - g_winStartUs < kCostWindowUs) return;
    const double f = g_winFrames ? static_cast<double>(g_winFrames) : 1.0;
    std::vector<size_t> idx(g_cost.size());
    std::iota(idx.begin(), idx.end(), size_t{0});
    std::sort(idx.begin(), idx.end(), [](size_t a, size_t b) { return g_cost[a].sumUs > g_cost[b].sumUs; });
    char buf[1024];
    int n = std::snprintf(buf, sizeof buf,
        "MAIN-COST window_s=%.1f frames=%u ours_us_mean=%.0f ours_us_max=%.0f pipe_work_us_mean=%.0f pipe_items=%u original_us_mean=%.0f | top (mean/max us):",
        (nowUs - g_winStartUs) / 1e6, g_winFrames, g_oursSumUs / f, g_oursMaxUs, g_batchSumUs / f, g_winItems, g_origSumUs / f);
    for (size_t k = 0; k < idx.size() && k < 6 && n > 0 && n < static_cast<int>(sizeof buf) - 48; ++k) {
        const TaskCost& c = g_cost[idx[k]];
        n += std::snprintf(buf + n, sizeof buf - n, " %s=%.0f/%.0f", c.name, c.sumUs / f, c.maxUs);
    }
    logf("%s", buf);
    for (auto& c : g_cost) { c.sumUs = 0; c.maxUs = 0; }
    g_winStartUs = nowUs;
    g_winFrames = g_winItems = 0;
    g_origSumUs = g_oursSumUs = g_oursMaxUs = g_batchSumUs = 0;
}

constexpr const char* kImporter = "WHGame.dll";
constexpr const char* kProvider = "Framework.dll";
constexpr const char* kSymbol   = "?Update@C_ModulesManager@framework@wh@@QEAAXM@Z";
enum class SyncPhase { pending, running, done, cancelled };

// Returns true when the task FAULTED -- or did not run because its site is switched off
// (WO-110 R12: callers that reply over the pipe must not report a faulted task's
// default-constructed result as OK). The fault itself is logged by the guard.
bool run_guarded(fault::Site& site, std::function<void()>* work) {
    return !fault::guarded(site, [&] { (*work)(); });
}

void hooked_update(void* self, float dt) {
    // Original first: queued work should observe the state the frame produced,
    // and if our work throws the game has still updated.
    const double t0 = now_us();
    if (g_original) g_original(self, dt);
    const double t1 = now_us();

    ++g_frames;
    g_lastDt.store(dt, std::memory_order_relaxed);   // WO-138: the frame meter
    if (g_main_thread_id == 0) g_main_thread_id = GetCurrentThreadId();

    // Swap under the lock and run outside it: queued work may post more work,
    // and holding the lock across a call into the game invites a deadlock.
    std::vector<std::function<void()>> batch;
    // Copy under the lock, run outside it. Running them while holding g_mutex
    // would block every post() for the duration of a call into the game, which
    // is exactly the deadlock the comment above warns about.
    std::vector<std::shared_ptr<Repeating>> repeating;
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        if (!g_queue.empty()) batch.swap(g_queue);
        repeating = g_repeating;
    }
    const bool cost = g_costOn.load(std::memory_order_relaxed);
    if (cost && g_cost.size() < repeating.size()) g_cost.resize(repeating.size());
    for (size_t i = 0; i < repeating.size(); ++i) {
        Repeating& r = *repeating[i];
        if (!cost) { run_guarded(r.site, &r.fn); continue; }
        const double a = now_us();
        run_guarded(r.site, &r.fn);
        const double d = now_us() - a;
        TaskCost& c = g_cost[i];
        c.name = r.name;
        c.sumUs += d;
        if (d > c.maxUs) c.maxUs = d;
    }

    double batchUs = 0;
    const uint32_t items = static_cast<uint32_t>(batch.size());
    if (!batch.empty()) {
        const double b0 = now_us();
        for (auto& work : batch) {
            run_guarded(g_queuedSite, &work);
        }
        batchUs = now_us() - b0;
        g_drained.notify_all();
    }
    const double t2 = now_us();
    fault::tick(t2 / 1e6);                 // WO-151: the 60 s FAULT-SUM line
    note_frame_rate(t1, t2 - t1);          // WO-151: the 60 s FRAME line
    if (cost) note_cost(t1 - t0, t2 - t1, batchUs, items, t2);
}

} // namespace

bool install() {
    if (g_installed) return true;

    LARGE_INTEGER freq;
    QueryPerformanceFrequency(&freq);
    g_usPerTick = 1e6 / static_cast<double>(freq.QuadPart);

    g_importer = GetModuleHandleA(kImporter);
    if (!g_importer) {
        logf("MAIN: %s not loaded", kImporter);
        return false;
    }

    void* previous = hook_iat(g_importer, kProvider, kSymbol,
                              reinterpret_cast<void*>(&hooked_update));
    if (!previous) {
        logf("MAIN: IAT entry for %s not found in %s", kSymbol, kImporter);
        return false;
    }

    g_original  = reinterpret_cast<UpdateFn>(previous);
    g_installed = true;
    logf("MAIN: hooked %s!IAT[%s] original=%p", kImporter, "C_ModulesManager::Update", previous);
    return true;
}

void uninstall() {
    if (!g_installed) return;
    hook_iat(g_importer, kProvider, kSymbol, reinterpret_cast<void*>(g_original));
    g_installed = false;
    logf("MAIN: unhooked");
}

void post_repeating(std::function<void()> work) {
    post_repeating("?", std::move(work));
}

void post_repeating(const char* name, std::function<void()> work) {
    auto r = std::make_shared<Repeating>(name, std::move(work));
    std::lock_guard<std::mutex> lock(g_mutex);
    g_repeating.push_back(std::move(r));
}

void post(std::function<void()> work) {
    std::lock_guard<std::mutex> lock(g_mutex);
    g_queue.push_back(std::move(work));
}

bool run_sync(std::function<void()> work, unsigned timeout_ms) {
    return run_sync(std::move(work), timeout_ms, nullptr);
}

bool run_sync(std::function<void()> work, unsigned timeout_ms, bool* faulted) {
    if (faulted) *faulted = false;
    // Already on the main thread: run inline. Queueing here would deadlock,
    // because the drain that would run it is the frame we are inside.
    if (g_main_thread_id != 0 && GetCurrentThreadId() == g_main_thread_id) {
        const bool f = run_guarded(g_queuedSite, &work);
        if (faulted) *faulted = f;
        return true;
    }

    struct State {
        std::mutex mutex;
        std::condition_variable cv;
        std::function<void()> work;
        SyncPhase phase = SyncPhase::pending;
        bool faulted = false;   // WO-110 R12
    };

    auto state = std::make_shared<State>();
    state->work = std::move(work);

    post([state] {
        {
            std::lock_guard<std::mutex> lock(state->mutex);
            if (state->phase != SyncPhase::pending) return;
            state->phase = SyncPhase::running;
        }

        // Guard here as well as in hooked_update so an SEH fault cannot skip
        // the completion signal and leave the waiting pipe thread blocked.
        bool f = false;
        try {
            f = run_guarded(g_queuedSite, &state->work);
        } catch (...) {
            logf("MAIN: task raised a C++ exception -- swallowed");
            f = true;
        }
        state->faulted = f;

        {
            std::lock_guard<std::mutex> lock(state->mutex);
            state->work = {};
            state->phase = SyncPhase::done;
        }
        state->cv.notify_all();
    });

    std::unique_lock<std::mutex> lock(state->mutex);
    if (state->cv.wait_for(lock, std::chrono::milliseconds(timeout_ms), [&] {
            return state->phase == SyncPhase::done;
        })) {
        if (faulted) *faulted = state->faulted;
        return true;
    }

    if (state->phase == SyncPhase::pending) {
        // The frame never picked the task up. Cancel it while its captured
        // caller state is still alive; the queued wrapper becomes a no-op.
        state->phase = SyncPhase::cancelled;
        state->work = {};
        return false;
    }

    // Once execution has started, returning would invalidate references held
    // by the caller's lambda. Finish safely even if that crosses the timeout.
    state->cv.wait(lock, [&] { return state->phase == SyncPhase::done; });
    return true;
}

unsigned long long frame_count() { return g_frames.load(std::memory_order_relaxed); }
float last_dt() { return g_lastDt.load(std::memory_order_relaxed); }

void set_cost_meter(bool on) {
    const bool was = g_costOn.exchange(on);
    if (was != on) logf("MAIN-COST mp_main_cost=%s (the per-task frame-cost meter, 10 s windows)", on ? "on" : "off");
}
bool cost_meter() { return g_costOn.load(); }

} // namespace kcdmp::main_thread
