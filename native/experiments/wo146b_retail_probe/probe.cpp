// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-146B research probe -- KCD2 retail (app 1771300, game 1.5.6).
//
// WHAT THIS IS. A throwaway measuring instrument for docs/WO-146B-findings.md.
// It runs inside the maintainer's own retail game process, in single player,
// and answers three questions the static census (docs/WO-145-retail-census.md)
// could not: does the plugin's foundation work on retail, how far have the
// struct layouts moved, and is the NPC-state context reachable.
//
// WHAT IT IS NOT. It is not the plugin, it is not linked into the plugin, and
// nothing here ships. Every hook is capture-only (it logs and returns; the
// original runs unchanged) except the frame hook, which is what gives the probe
// a main thread to do its reads on. Nothing about DRM, Steam, entitlements or
// the game's network code is read, touched or worked around; if the game
// refuses the probe the probe stops and says so.
//
// HOW IT IS DRIVEN. A text file next to the DLL (<dll>.cmd), polled by the
// probe's own worker thread; one verb per line, consumed after reading.
// Everything that touches game state is queued onto the frame hook.
//
// IDENTITY. Nothing arms until the retail WHGame.dll on disk hashes to the
// sha256 WO-145 recorded for game 1.5.6.

#include "probe_log.h"
#include "probe_pe.h"
#include "probe_hook.h"

#include <windows.h>
#include <bcrypt.h>

#include <atomic>
#include <cstdio>
#include <cstring>
#include <functional>
#include <mutex>
#include <string>
#include <vector>

#pragma comment(lib, "bcrypt.lib")

namespace wo146b {
namespace {

// ---------------------------------------------------------------------------
// The build this probe is allowed to run in (docs/WO-145 appendix A).
// ---------------------------------------------------------------------------
constexpr const char* kGameModule = "WHGame.dll";
constexpr const char* kExpectedSha256 =
    "bdf8f9e4a11257a72b64c84700e284c29e4c4ccaf5b8d4bfa7d0b2a7294479f7";
constexpr unsigned long long kExpectedSize = 89180672ull;

HMODULE g_wh = nullptr;            // retail WHGame.dll
const uint8_t* g_base = nullptr;
bool  g_identityOk = false;

// ---------------------------------------------------------------------------
// Small helpers
// ---------------------------------------------------------------------------
std::string self_dir() {
    char buf[MAX_PATH]{};
    HMODULE self = nullptr;
    GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                       GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                       reinterpret_cast<LPCSTR>(&self_dir), &self);
    GetModuleFileNameA(self, buf, MAX_PATH);
    std::string p(buf);
    const size_t slash = p.find_last_of("\\/");
    return slash == std::string::npos ? std::string(".") : p.substr(0, slash);
}

std::string self_stem() {
    char buf[MAX_PATH]{};
    HMODULE self = nullptr;
    GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                       GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                       reinterpret_cast<LPCSTR>(&self_dir), &self);
    GetModuleFileNameA(self, buf, MAX_PATH);
    std::string p(buf);
    const size_t dot = p.find_last_of('.');
    if (dot != std::string::npos) p.resize(dot);
    return p;
}

std::string sha256_of_file(const char* path, unsigned long long* sizeOut) {
    HANDLE h = CreateFileA(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                           nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return {};
    LARGE_INTEGER sz{};
    GetFileSizeEx(h, &sz);
    if (sizeOut) *sizeOut = static_cast<unsigned long long>(sz.QuadPart);

    BCRYPT_ALG_HANDLE alg = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    std::string out;
    if (BCryptOpenAlgorithmProvider(&alg, BCRYPT_SHA256_ALGORITHM, nullptr, 0) == 0 &&
        BCryptCreateHash(alg, &hash, nullptr, 0, nullptr, 0, 0) == 0) {
        std::vector<unsigned char> buf(1 << 20);
        DWORD got = 0;
        while (ReadFile(h, buf.data(), static_cast<DWORD>(buf.size()), &got, nullptr) && got)
            BCryptHashData(hash, buf.data(), got, 0);
        unsigned char digest[32]{};
        if (BCryptFinishHash(hash, digest, sizeof(digest), 0) == 0) {
            char hex[65]{};
            for (int i = 0; i < 32; ++i) std::snprintf(hex + 2 * i, 3, "%02x", digest[i]);
            out = hex;
        }
    }
    if (hash) BCryptDestroyHash(hash);
    if (alg) BCryptCloseAlgorithmProvider(alg, 0);
    CloseHandle(h);
    return out;
}

// The RTTI class name of whatever `obj`'s vptr describes, or nullptr.
// vptr[-1] is the complete-object locator; its pTypeDescriptor is an RVA from
// the module base (x64), and the descriptor's name starts at +0x10.
const char* rtti_name_of(const void* obj) {
    if (!obj || !g_base) return nullptr;
    void* vptr = nullptr;
    if (!pe::read_ptr(obj, 0, &vptr) || !vptr) return nullptr;
    pe::Range img{};
    if (!pe::image(g_wh, &img) || !img.contains(vptr)) return nullptr;
    void* col = nullptr;
    if (!pe::read_ptr(vptr, static_cast<size_t>(0) - 8, &col) || !col) return nullptr;
    if (!img.contains(col)) return nullptr;
    uint32_t sig = 0, tdRva = 0;
    if (!pe::read_u32(col, 0, &sig) || sig != 1) return nullptr;
    if (!pe::read_u32(col, 12, &tdRva) || !tdRva || tdRva >= img.size()) return nullptr;
    const char* name = reinterpret_cast<const char*>(g_base + tdRva + 0x10);
    char probe[4]{};
    if (!pe::read_bytes(name, probe, 4)) return nullptr;
    return (probe[0] == '.') ? name : nullptr;
}

// Print an object's first `n` bytes as qwords, naming anything that turns out
// to be an RTTI-bearing object. This is how layout drift is MEASURED here
// instead of guessed.
void dump_object(const char* tag, const void* obj, size_t n) {
    if (!obj) { logf("%s: (null)", tag); return; }
    logf("%s: object %p (%s)", tag, obj, rtti_name_of(obj) ? rtti_name_of(obj) : "no RTTI");
    for (size_t off = 0; off < n; off += 8) {
        void* v = nullptr;
        if (!pe::read_ptr(obj, off, &v)) { logf("%s   +0x%03zX  <unreadable>", tag, off); break; }
        const char* nm = rtti_name_of(v);
        uint32_t lo = static_cast<uint32_t>(reinterpret_cast<uintptr_t>(v));
        if (nm) logf("%s   +0x%03zX  %p  %s", tag, off, v, nm);
        else if (v && reinterpret_cast<uintptr_t>(v) > 0x10000 &&
                 reinterpret_cast<uintptr_t>(v) < 0x7FFFFFFFFFFFull)
            logf("%s   +0x%03zX  %p", tag, off, v);
        else if (lo)
            logf("%s   +0x%03zX  0x%016llX", tag, off,
                 static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(v)));
    }
}

// Guarded virtual/pointer calls (no C++ objects in the __try frames).
bool call0(void* fn, void* self, void** out) {
    __try { *out = reinterpret_cast<void* (__fastcall*)(void*)>(fn)(self); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool call1(void* fn, void* self, void* a, void** out) {
    __try { *out = reinterpret_cast<void* (__fastcall*)(void*, void*)>(fn)(self, a); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool call1u(void* fn, void* self, uint32_t a, void** out) {
    __try { *out = reinterpret_cast<void* (__fastcall*)(void*, uint32_t)>(fn)(self, a); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
void* vslot(void* obj, size_t off) {
    void* vt = nullptr; void* fn = nullptr;
    if (!obj || !pe::read_ptr(obj, 0, &vt) || !vt || !pe::read_ptr(vt, off, &fn)) return nullptr;
    return fn;
}
bool vcall0(void* obj, size_t off, void** out) {
    void* fn = vslot(obj, off);
    return fn && call0(fn, obj, out);
}
bool vcall1u(void* obj, size_t off, uint32_t a, void** out) {
    void* fn = vslot(obj, off);
    return fn && call1u(fn, obj, a, out);
}
bool vcall1(void* obj, size_t off, void* a, void** out) {
    void* fn = vslot(obj, off);
    return fn && call1(fn, obj, a, out);
}
std::string safe_str(const char* s, size_t max = 96) {
    if (!s) return "(null)";
    char buf[128]{};
    if (!pe::read_bytes(s, buf, max < sizeof(buf) ? max : sizeof(buf) - 1)) return "(unreadable)";
    buf[sizeof(buf) - 1] = 0;
    for (char& c : buf) if (c && (c < 32 || c > 126)) { c = 0; break; }
    return buf;
}
std::string at(const void* p) {
    char d[96]{};
    pe::describe(g_wh, p, d, sizeof(d));
    return d;
}

// ---------------------------------------------------------------------------
// Frame hook (step 1) and the main-thread queue everything else rides.
// ---------------------------------------------------------------------------
std::mutex g_qmutex;
std::vector<std::function<void()>> g_queue;
std::atomic<unsigned long long> g_frames{0};
std::atomic<unsigned long>      g_mainTid{0};
std::atomic<bool>               g_tickInstalled{false};
void*                           g_modulesManager = nullptr;   // the `this` the hook sees
const uint8_t*                  g_genv = nullptr;

unsigned long long g_frameAtSecond[64]{};
std::atomic<int>   g_secondsLogged{0};
ULONGLONG          g_tickStart = 0;
int                g_firstFramesLogged = 0;

bool run_guarded(std::function<void()>* work) {
    __try { (*work)(); return false; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return true; }
}

void drain_queue() {
    std::vector<std::function<void()>> batch;
    {
        std::lock_guard<std::mutex> lock(g_qmutex);
        if (g_queue.empty()) return;
        batch.swap(g_queue);
    }
    for (auto& w : batch)
        if (run_guarded(&w)) logf("QUEUE: a task faulted on the game thread -- swallowed");
}

void on_frame(void* self, void*, void*, void*) {
    const unsigned long long n = ++g_frames;
    const unsigned long tid = GetCurrentThreadId();
    if (g_mainTid.load(std::memory_order_relaxed) == 0) {
        g_mainTid.store(tid, std::memory_order_relaxed);
        g_modulesManager = self;
        g_tickStart = GetTickCount64();
        logf("TICK: first frame -- C_ModulesManager this=%p", self);
    }
    if (g_firstFramesLogged < 10) {
        ++g_firstFramesLogged;
        logf("TICK: frame %llu", n);
    }
    // one line per second for the first minute
    const int sec = static_cast<int>((GetTickCount64() - g_tickStart) / 1000);
    if (sec > 0 && sec <= 60 && g_secondsLogged.load(std::memory_order_relaxed) < sec) {
        const int prev = g_secondsLogged.exchange(sec);
        if (prev < sec) {
            logf("TICK: second %2d -- %llu frames so far (%llu in this second)",
                 sec, n, n - g_frameAtSecond[prev & 63]);
            g_frameAtSecond[sec & 63] = n;
        }
    }
    drain_queue();
}

// Queue work for the game thread. When this copy of the probe did not install
// the frame hook (a previous copy in the same process already patched the
// function, and the patch is refused a second time by design) there is no game
// thread to queue onto, so the work runs here instead and the log says so:
// every read it makes is SEH-guarded, so the worst a race can do is report a
// field as unreadable.
void post(std::function<void()> w) {
    if (!g_tickInstalled.load()) {
        logf("QUEUE: no frame hook in this copy -- running on the worker thread (reads are guarded)");
        if (run_guarded(&w)) logf("QUEUE: the task faulted -- swallowed");
        return;
    }
    std::lock_guard<std::mutex> lock(g_qmutex);
    g_queue.push_back(std::move(w));
}

// ---------------------------------------------------------------------------
// STEP 0 -- identity
// ---------------------------------------------------------------------------
bool step0_identity() {
    g_wh = GetModuleHandleA(kGameModule);
    if (!g_wh) { logf("ID: %s is not loaded -- refusing to arm anything", kGameModule); return false; }
    g_base = reinterpret_cast<const uint8_t*>(g_wh);

    char path[MAX_PATH]{};
    GetModuleFileNameA(g_wh, path, MAX_PATH);
    unsigned long long size = 0;
    const std::string sha = sha256_of_file(path, &size);
    pe::Range img{};
    pe::image(g_wh, &img);
    logf("ID: module base %p, SizeOfImage 0x%zX", g_base, img.size());
    logf("ID: file size %llu (expected %llu)", size, kExpectedSize);
    logf("ID: sha256 %s", sha.c_str());
    logf("ID: expected %s", kExpectedSha256);
    const bool ok = (sha == kExpectedSha256) && (size == kExpectedSize);
    logf("ID: %s", ok ? "MATCH -- the retail 1.5.6 build WO-145 measured" : "MISMATCH -- nothing will be armed");

    for (const char* s : {".text", ".rdata", ".data", ".pdata", "_RDATA"}) {
        pe::Range r{};
        if (pe::section(g_wh, s, &r))
            logf("ID: section %-7s %p .. %p (0x%zX)", s, r.begin, r.end, r.size());
    }
    g_identityOk = ok;
    return ok;
}

// ---------------------------------------------------------------------------
// STEP 1 -- it ticks (census D-007)
// ---------------------------------------------------------------------------
// Anchors, in order, none of them an RVA:
//   1. the string wh::framework::C_ModulesManager::ProcessMessage  -> the one
//      function that references it IS C_ModulesManager::ProcessMessage;
//   2. its first `mov rcx,[rip+d]` names gEnv.pLog, and pLog sits at gEnv+0xC0,
//      so gEnv = that address - 0xC0 (the Modding Tools build loads gEnv and
//      then reads [gEnv+0xC0]; retail folds the two into one absolute load);
//   3. C_ModulesManager::Update is the one function in .text holding the module
//      update loop `mov rcx,[rbx]; movaps xmm1,xmm6; mov rax,[rcx]; call
//      [rax+0x20]`, cross-checked by its own reference to gEnv+0xF0.
constexpr const char* kProcessMessageStr = "wh::framework::C_ModulesManager::ProcessMessage";
const uint8_t kMovRcxRip[3] = {0x48, 0x8B, 0x0D};
const uint8_t kUpdateLoop[12] = {0x48, 0x8B, 0x0B, 0x0F, 0x28, 0xCE,
                                 0x48, 0x8B, 0x01, 0xFF, 0x50, 0x20};
const uint8_t kUpdatePrologue[18] = {0x48, 0x89, 0x5C, 0x24, 0x08, 0x57, 0x48, 0x83, 0xEC, 0x30,
                                     0x48, 0x8B, 0xF9, 0x0F, 0x29, 0x74, 0x24, 0x20};
constexpr size_t kEnvLog = 0xC0;
constexpr size_t kEnvF0  = 0xF0;

const uint8_t* g_processMessage = nullptr;
const uint8_t* g_update = nullptr;

bool step1_find() {
    int n = 0;
    g_processMessage = pe::function_by_string(g_wh, kProcessMessageStr, &n);
    logf("S1: string %s -> %d referencing function(s)", kProcessMessageStr, n);
    if (!g_processMessage) { logf("S1: BLOCKED -- ProcessMessage not uniquely anchored"); return false; }
    logf("S1: C_ModulesManager::ProcessMessage at %s", at(g_processMessage).c_str());

    const uint8_t* mov = pe::function_find_bytes(g_wh, g_processMessage, kMovRcxRip, sizeof(kMovRcxRip));
    if (!mov) { logf("S1: no `mov rcx,[rip+d]` in ProcessMessage -- gEnv not derivable here"); return false; }
    const uint8_t* pLogSlot = static_cast<const uint8_t*>(pe::rip_target(mov));
    g_genv = pLogSlot - kEnvLog;
    logf("S1: gEnv.pLog slot at %s -> gEnv = %s", at(pLogSlot).c_str(), at(g_genv).c_str());

    const uint8_t* hits[8]{};
    const int nl = pe::find_bytes(g_wh, kUpdateLoop, sizeof(kUpdateLoop), hits, 8);
    logf("S1: module-update loop pattern -> %d hit(s)", nl);
    if (nl != 1) { logf("S1: BLOCKED -- the loop pattern is not unique"); return false; }
    pe::Range r{};
    if (!pe::function_range(g_wh, hits[0], &r)) { logf("S1: BLOCKED -- no .pdata entry for the loop"); return false; }
    g_update = r.begin;
    const bool refsEnv = pe::function_refs(g_wh, g_update, g_genv + kEnvF0);
    logf("S1: C_ModulesManager::Update at %s (%zu bytes), references gEnv+0xF0: %s",
         at(g_update).c_str(), r.size(), refsEnv ? "yes" : "NO");
    if (!refsEnv) { logf("S1: BLOCKED -- the candidate does not cross-check against gEnv"); return false; }

    char got[64]{};
    if (pe::read_bytes(g_update, got, sizeof(kUpdatePrologue))) {
        char hex[160]{};
        for (size_t i = 0; i < sizeof(kUpdatePrologue); ++i) std::snprintf(hex + 3 * i, 4, "%02x ", (unsigned char)got[i]);
        logf("S1: prologue %s(%s)", hex,
             std::memcmp(got, kUpdatePrologue, sizeof(kUpdatePrologue)) == 0 ? "as expected" : "DIFFERS");
    }
    return true;
}

void step1_hook() {
    if (g_tickInstalled.load()) { logf("S1: already hooked"); return; }
    if (!g_update) { logf("S1: nothing to hook"); return; }
    const char* why = nullptr;
    const bool ok = hook::install(const_cast<uint8_t*>(g_update), kUpdatePrologue,
                                  sizeof(kUpdatePrologue), &on_frame, &why);
    logf("S1: frame hook %s (%s)", ok ? "INSTALLED" : "REFUSED", why ? why : "?");
    if (ok) g_tickInstalled.store(true);
}

void step1_report() {
    logf("S1: %llu frames since the hook went in; main thread id %lu (probe loaded on %lu)",
         g_frames.load(), g_mainTid.load(), GetCurrentThreadId());
}

// ---------------------------------------------------------------------------
// STEP 2 -- it can look around (D-183, D-010)
// ---------------------------------------------------------------------------
constexpr const char* kSoulTypeName = "wh::rpgmodule::Soul";

struct RttrType { void* data; };
using GetByNameFn = void* (*)(RttrType* ret, const std::string_view* name);
GetByNameFn g_getByName = nullptr;
void*       g_registry = nullptr;

// MSVC std::string: a 16-byte union (inline buffer, or a pointer when the
// capacity at +0x18 is 16 or more), size at +0x10.
const char* msvc_string(const void* s, uint64_t* lenOut) {
    uint64_t size = 0, cap = 0;
    if (!pe::read_ptr(s, 0x10, reinterpret_cast<void**>(&size)) ||
        !pe::read_ptr(s, 0x18, reinterpret_cast<void**>(&cap))) return nullptr;
    if (size > 512) return nullptr;
    if (lenOut) *lenOut = size;
    if (cap >= 16) {
        void* p = nullptr;
        if (!pe::read_ptr(s, 0, &p)) return nullptr;
        return static_cast<const char*>(p);
    }
    return static_cast<const char*>(s);
}

// The RTTR type registry's CUSTOM-NAME map, read directly instead of through
// the engine's own lookup: keys are std::string (40 bytes each with the pair's
// padding), values are type_data* (8 bytes), both sorted the same way. Reading
// it is what proves the reflection data is reachable on retail.
void* rttr_type_by_name(const char* want, int* countOut, int printFirst) {
    if (!g_registry) { logf("S2: no registry"); return nullptr; }
    void *kb = nullptr, *ke = nullptr, *vb = nullptr, *ve = nullptr;
    if (!pe::read_ptr(g_registry, 0x10, &kb) || !pe::read_ptr(g_registry, 0x18, &ke) ||
        !pe::read_ptr(g_registry, 0x28, &vb) || !pe::read_ptr(g_registry, 0x30, &ve)) return nullptr;
    const size_t kbytes = static_cast<const uint8_t*>(ke) - static_cast<const uint8_t*>(kb);
    const size_t vbytes = static_cast<const uint8_t*>(ve) - static_cast<const uint8_t*>(vb);
    const size_t n = vbytes / 8;
    const size_t stride = n ? kbytes / n : 0;
    logf("S2: custom-name map: %zu entries, key stride %zu bytes", n, stride);
    if (!n || stride < 32) return nullptr;
    void* found = nullptr;
    int shown = 0;
    const size_t wlen = std::strlen(want);
    for (size_t i = 0; i < n; ++i) {
        const void* k = static_cast<const char*>(kb) + i * stride;
        uint64_t len = 0;
        const char* s = msvc_string(k, &len);
        if (!s) continue;
        if (shown < printFirst) {
            logf("S2:   type[%4zu] \"%s\"", i, safe_str(s, static_cast<size_t>(len)).c_str());
            ++shown;
        }
        if (len == wlen) {
            char buf[128]{};
            if (len < sizeof(buf) && pe::read_bytes(s, buf, static_cast<size_t>(len)) &&
                std::memcmp(buf, want, wlen) == 0) {
                void* v = nullptr;
                if (pe::read_ptr(vb, i * 8, &v)) {
                    found = v;
                    logf("S2:   \"%s\" is entry %zu -> type_data %p", want, i, v);
                }
            }
        }
    }
    if (countOut) *countOut = static_cast<int>(n);
    if (!found) logf("S2:   \"%s\" is NOT in the custom-name map", want);
    return found;
}

// The registry's name map, read directly: a flat map whose key vector is at
// registry+off (begin/end/capacity) and whose value vector follows at +0x18.
// Printing the keys is the only way to say whether a lookup that returns the
// invalid type means "the name is not registered" or "the map is not this one".
void rttr_names(size_t off, size_t stride, int max) {
    if (!g_registry) { logf("S2: no registry -- run step2rttr first"); return; }
    void *kb = nullptr, *ke = nullptr, *vb = nullptr, *ve = nullptr;
    if (!pe::read_ptr(g_registry, off, &kb) || !pe::read_ptr(g_registry, off + 8, &ke) ||
        !pe::read_ptr(g_registry, off + 0x18, &vb) || !pe::read_ptr(g_registry, off + 0x20, &ve)) {
        logf("S2: registry+0x%zX is not readable", off);
        return;
    }
    const size_t kbytes = static_cast<const uint8_t*>(ke) - static_cast<const uint8_t*>(kb);
    const size_t vbytes = static_cast<const uint8_t*>(ve) - static_cast<const uint8_t*>(vb);
    logf("S2: registry+0x%zX keys %p..%p (%zu bytes, %zu at stride %zu); values %p..%p (%zu bytes, %zu)",
         off, kb, ke, kbytes, stride ? kbytes / stride : 0, stride, vb, ve, vbytes, vbytes / 8);
    int shown = 0;
    for (size_t i = 0; i + stride <= kbytes && shown < max; i += stride) {
        void* p = nullptr;
        uint64_t len = 0;
        if (!pe::read_ptr(kb, i, &p)) break;
        pe::read_ptr(kb, i + 8, reinterpret_cast<void**>(&len));
        if (!p || len == 0 || len > 200) continue;
        logf("S2:   key[%3zu] len %3llu \"%s\"", i / stride, (unsigned long long)len,
             safe_str(static_cast<const char*>(p), static_cast<size_t>(len)).c_str());
        ++shown;
    }
}

// The fault, when there is one, is the measurement: record the exception code
// and the address so the findings can say WHERE retail's RTTR entry point goes
// wrong rather than just "it faulted".
unsigned long g_lastExcCode = 0;
void*         g_lastExcAddr = nullptr;

int record_exception(EXCEPTION_POINTERS* ep) {
    g_lastExcCode = ep->ExceptionRecord->ExceptionCode;
    g_lastExcAddr = ep->ExceptionRecord->ExceptionAddress;
    return EXCEPTION_EXECUTE_HANDLER;
}

bool call_get_by_name(RttrType* out, const std::string_view* sv) {
    __try { g_getByName(out, sv); return true; }
    __except (record_exception(GetExceptionInformation())) { return false; }
}

using NoArgFn = void* (*)();
bool call_noarg(void* fn, void** out) {
    __try { *out = reinterpret_cast<NoArgFn>(fn)(); return true; }
    __except (record_exception(GetExceptionInformation())) { return false; }
}

void log_exception(const char* tag) {
    logf("%s: exception 0x%08lX at %s", tag, g_lastExcCode,
         g_lastExcAddr ? at(g_lastExcAddr).c_str() : "(null)");
}

void log_bytes(const char* tag, const void* p, size_t n) {
    unsigned char b[48]{};
    if (n > sizeof(b)) n = sizeof(b);
    if (!pe::read_bytes(p, b, n)) { logf("%s: <unreadable>", tag); return; }
    char hex[160]{};
    for (size_t i = 0; i < n; ++i) std::snprintf(hex + 3 * i, 4, "%02x ", b[i]);
    logf("%s: %s", tag, hex);
}

void step2_genv() {
    if (!g_genv) { logf("S2: no gEnv"); return; }
    logf("S2: gEnv at %s -- naming every member that carries RTTI", at(g_genv).c_str());
    for (size_t off = 0; off < 0x200; off += 8) {
        void* v = nullptr;
        if (!pe::read_ptr(g_genv, off, &v) || !v) continue;
        const char* nm = rtti_name_of(v);
        if (nm) logf("S2:   gEnv+0x%03zX  %p  %s", off, v, nm);
    }
    const unsigned long tid = g_mainTid.load();
    if (tid) {
        for (size_t off = 0; off < 0x400; off += 4) {
            uint32_t v = 0;
            if (pe::read_u32(g_genv, off, &v) && v == tid)
                logf("S2:   gEnv+0x%03zX  == the main thread id (%lu)", off, tid);
        }
    } else {
        logf("S2:   main thread id unknown (the frame hook has not fired)");
    }
}

void step2_rttr() {
    int n = 0;
    const char* s = pe::find_cstring(g_wh, kSoulTypeName, &n);
    logf("S2: RTTR type name %s -> %d copy/ies", kSoulTypeName, n);
    if (!s) { logf("S2: BLOCKED -- the registration string is absent"); return; }
    int nf = 0;
    const uint8_t* reg = pe::function_by_ref(g_wh, s, &nf);
    logf("S2: referenced by %d function(s); registration at %s", nf, reg ? at(reg).c_str() : "(ambiguous)");
    if (!reg) return;
    // Inside the registration the string_view {ptr,len} is built and handed to
    // rttr::type::get_by_name: the first direct call after the string's own lea.
    const uint8_t* lea = pe::function_find_bytes(g_wh, reg, reinterpret_cast<const uint8_t*>(&s), 0);
    // find the lea by scanning for the reference again, this time for its site
    const uint8_t* site = nullptr;
    {
        pe::Range fr{};
        if (pe::function_range(g_wh, reg, &fr)) {
            for (const uint8_t* q = fr.begin; q + 7 <= fr.end; ++q) {
                if ((q[0] & 0xF8) != 0x48 || q[1] != 0x8D || (q[2] & 0xC7) != 0x05) continue;
                if (pe::rip_target(q) == s) { site = q; break; }
            }
        }
    }
    (void)lea;
    if (!site) { logf("S2: BLOCKED -- the string's own lea was not found in the registration"); return; }
    const uint8_t* callAt = nullptr;
    const uint8_t* target = pe::first_call_after(site, 64, &callAt);
    logf("S2: string lea at %s, first call at %s -> %s",
         at(site).c_str(), callAt ? at(callAt).c_str() : "?", target ? at(target).c_str() : "?");
    if (!target) { logf("S2: BLOCKED -- no call follows the string"); return; }
    // The call right after the type name is rttr::detail::type_register::
    // custom_name(type, string_view), NOT type::get_by_name: the two compile to
    // the same shape (fetch the registry singleton, forward both arguments) and
    // the first probe run called custom_name with an empty type and took an
    // access violation inside the registry. What the call IS good for is the
    // singleton accessor it calls, which is the hub the whole RTTR API hangs
    // off. (WO-146B, live.)
    const uint8_t* customName = target;
    logf("S2: that call is type_register::custom_name (it takes an ALREADY VALID type) at %s",
         at(customName).c_str());
    const uint8_t* accAt = nullptr;
    const uint8_t* acc = pe::first_call_after(customName, 32, &accAt);
    if (!acc) { logf("S2: BLOCKED -- no registry accessor call inside custom_name"); return; }
    void* registry = nullptr;
    const bool regOk = call_noarg(const_cast<uint8_t*>(acc), &registry);
    logf("S2: registry accessor at %s -> %s registry=%p (%s)", at(acc).c_str(),
         regOk ? "ok" : "FAULTED", registry,
         registry ? (rtti_name_of(registry) ? rtti_name_of(registry) : "no RTTI") : "-");
    if (!regOk) { log_exception("S2"); return; }

    // type::get_by_name is the accessor's caller that looks the name up in the
    // registry's custom-name map and copies the mapped value into its return
    // buffer. Among every function that calls the accessor, exactly one holds
    // `lea rdi,[rax+0xD0]; mov rcx,rdi` (the map, then the find) -- checked
    // unique in this image before it is used.
    const uint8_t kMapFind[10] = {0x48, 0x8D, 0xB8, 0xD0, 0x00, 0x00, 0x00, 0x48, 0x8B, 0xCF};
    const uint8_t* mhits[8]{};
    const int nm = pe::find_bytes(g_wh, kMapFind, sizeof(kMapFind), mhits, 8);
    logf("S2: the custom-name-map find pattern -> %d hit(s)", nm);
    if (nm != 1) { logf("S2: BLOCKED -- get_by_name's discriminator is not unique"); return; }
    pe::Range gr{};
    if (!pe::function_range(g_wh, mhits[0], &gr)) { logf("S2: BLOCKED -- no .pdata entry"); return; }
    const bool callsAcc = pe::function_calls_direct(g_wh, gr.begin, acc);
    logf("S2: rttr::type::get_by_name at %s (%zu bytes), calls the registry accessor: %s",
         at(gr.begin).c_str(), gr.size(), callsAcc ? "yes" : "NO");
    if (!callsAcc) { logf("S2: BLOCKED -- the candidate does not cross-check"); return; }
    g_getByName = reinterpret_cast<GetByNameFn>(const_cast<uint8_t*>(gr.begin));

    g_registry = registry;

    RttrType t{}, bogus{};
    const std::string_view soul{kSoulTypeName, std::strlen(kSoulTypeName)};
    const std::string_view none{"wh::rpgmodule::NoSuchTypeWo146b", 31};
    const bool ok1 = call_get_by_name(&t, &soul);
    const bool ok2 = call_get_by_name(&bogus, &none);
    logf("S2: get_by_name(\"%s\") -> %s data=%p", kSoulTypeName, ok1 ? "ok" : "FAULTED", t.data);
    if (!ok1) log_exception("S2");
    logf("S2: get_by_name(<nonexistent>) -> %s data=%p  (must be null for the positive to mean anything)",
         ok2 ? "ok" : "FAULTED", bogus.data);
    if (!ok2) log_exception("S2");
    if (ok1 && ok2 && t.data && t.data != bogus.data) {
        logf("S2: RTTR registry REACHABLE -- the lookup discriminates");
        dump_object("S2-type", t.data, 0xE0);
    } else {
        logf("S2: the two lookups agree (%p) -- that value is the invalid-type sentinel",
             t.data);
        // Which names DOES this map answer? Try the ones the plugin and the
        // agent use, then print the map's own keys.
        for (const char* n : {"Soul", "wh::rpgmodule::C_Soul", "SoulList", "StopFight",
                              "wh::rpgmodule::SoulState", "wh::xgenaimodule::NPCState::UnstanceElement"}) {
            RttrType r{};
            const std::string_view sv{n, std::strlen(n)};
            const bool ok = call_get_by_name(&r, &sv);
            logf("S2:   get_by_name(\"%s\") -> %s data=%p%s", n, ok ? "ok" : "FAULTED", r.data,
                 (ok && r.data && r.data != bogus.data) ? "   <-- RESOLVED" : "");
        }
        // The map that function searches is the registry's GLOBAL-ITEM map
        // (free functions), not the type map. Read the type map directly.
        int n = 0;
        void* soul = rttr_type_by_name(kSoulTypeName, &n, 6);
        if (soul) {
            logf("S2: RTTR type data for %s READ DIRECTLY from the registry (%d types registered)",
                 kSoulTypeName, n);
            dump_object("S2-type", soul, 0xE0);
        }
    }
}

// ---------------------------------------------------------------------------
// The entity system, needed by steps 2, 3 and 5.
// ---------------------------------------------------------------------------
constexpr size_t kEnvEntitySystem = 0xA0;
constexpr size_t kEsGetEntity     = 0x70;
constexpr size_t kEntGetId        = 0x08;
constexpr size_t kEntGetName      = 0x90;
constexpr size_t kEntGetWorldPos  = 0x170;
constexpr uint32_t kPlayerEntityId = 0x7777;

void* entity_system() {
    void* es = nullptr;
    if (!g_genv || !pe::read_ptr(g_genv, kEnvEntitySystem, &es)) return nullptr;
    return es;
}

void* entity_by_id(uint32_t id) {
    void* es = entity_system();
    void* e = nullptr;
    if (!es || !vcall1u(es, kEsGetEntity, id, &e)) return nullptr;
    return e;
}

const char* entity_name(void* e) {
    void* r = nullptr;
    if (!e || !vcall0(e, kEntGetName, &r)) return nullptr;
    return static_cast<const char*>(r);
}

void step2_entities() {
    void* es = entity_system();
    logf("S2: gEnv+0x%zX -> IEntitySystem %p (%s)", kEnvEntitySystem, es,
         rtti_name_of(es) ? rtti_name_of(es) : "no RTTI");
    if (!es) return;
    void* p = entity_by_id(kPlayerEntityId);
    logf("S2: GetEntity(0x%X) -> %p (%s) name=%s", kPlayerEntityId, p,
         rtti_name_of(p) ? rtti_name_of(p) : "no RTTI", safe_str(entity_name(p)).c_str());
    int named = 0;
    for (uint32_t id = 1; id < 40000 && named < 12; ++id) {
        void* e = entity_by_id(id);
        if (!e) continue;
        const char* nm = entity_name(e);
        if (!nm) continue;
        const std::string s = safe_str(nm);
        if (s.rfind("ttkc_", 0) == 0 || s == "Dude") {
            logf("S2: entity id %u -> %p name=%s", id, e, s.c_str());
            ++named;
        }
    }
}

// ---------------------------------------------------------------------------
// STEP 3 -- layout drift (D-095, D-415, D-369)
// ---------------------------------------------------------------------------
constexpr const char* kActorRtti = ".?AVC_Actor@entitymodule@wh@@";
void* const* g_vftActor = nullptr;
int g_actorSlots = 0;

// The plugin's own in-body byte checks, with the displacement kept so a hit
// proves BOTH the slot and the struct offset (native/KCDMP/motion.cpp).
struct SlotCheck {
    const char* label;
    size_t      mtByteOffset;       // where the Modding Tools build has it
    const uint8_t* pat; size_t n;   // the exact check
    const uint8_t* head; size_t hn; // the same instruction with the displacement free
    size_t      mtDisp;             // what the displacement is on the Modding Tools build
    const char* field;
};
const uint8_t kPseudoA[7]  = {0x48, 0x8B, 0x83, 0xE8, 0x07, 0x00, 0x00};   // mov rax,[rbx+0x7E8]
const uint8_t kPseudoAH[3] = {0x48, 0x8B, 0x83};
const uint8_t kPseudoB[5]  = {0xF3, 0x0F, 0x11, 0x70, 0x18};              // movss [rax+0x18],xmm6
const uint8_t kHolder[7]   = {0x48, 0x8B, 0x83, 0x08, 0x03, 0x00, 0x00};  // mov rax,[rbx+0x308]
const uint8_t kTagsA[7]    = {0x48, 0x8B, 0x91, 0x50, 0x04, 0x00, 0x00};  // mov rdx,[rcx+0x450]
const uint8_t kTagsAH[3]   = {0x48, 0x8B, 0x91};
const uint8_t kTagsB[8]    = {0xF3, 0x0F, 0x10, 0x8F, 0x74, 0x05, 0x00, 0x00};
const uint8_t kTagsBH[4]   = {0xF3, 0x0F, 0x10, 0x8F};
const uint8_t kTagsC[9]    = {0xF2, 0x44, 0x0F, 0x10, 0x87, 0x14, 0x06, 0x00, 0x00};
const uint8_t kTagsCH[5]   = {0xF2, 0x44, 0x0F, 0x10, 0x87};
const uint8_t kTagsD[7]    = {0x48, 0x8D, 0x8F, 0x50, 0x08, 0x00, 0x00};  // lea rcx,[rdi+0x850]
const uint8_t kTagsDH[3]   = {0x48, 0x8D, 0x8F};

// What the plugin reads out of a C_Actor, for the live field scan.
struct FieldGuess { const char* label; size_t mtOffset; };
const FieldGuess kActorFields[] = {
    {"m_pCombatActor (WO-44, +0x300)", 0x300},
    {"the +0x278 candidate",           0x278},
    {"aiAnim component (+0x7E8)",      0x7E8},
    {"expansion holder (+0x308)",      0x308},
};

void scan_slot(const char* label, size_t mtOff,
               const uint8_t* pat, size_t n,
               const uint8_t* head, size_t hn, size_t mtDisp) {
    if (!g_vftActor) return;
    const size_t mtSlot = mtOff / 8;
    // (a) exact check across every retail slot
    int exactSlot = -1, exactCount = 0;
    for (int i = 0; i < g_actorSlots; ++i) {
        void* fn = g_vftActor[i];
        if (!fn) continue;
        if (pe::function_has_bytes(g_wh, fn, pat, n)) { if (exactSlot < 0) exactSlot = i; ++exactCount; }
    }
    if (exactSlot >= 0) {
        logf("S3: %-22s MT slot %3zu (+0x%03zX) -> retail slot %3d (+0x%03X)%s  [exact check, %d match(es)]",
             label, mtSlot, mtOff, exactSlot, exactSlot * 8,
             (size_t)exactSlot == mtSlot ? " UNMOVED" : "", exactCount);
        return;
    }
    // (b) the same instruction with any displacement -- reports the NEW offset
    int relSlot = -1, relCount = 0; size_t newDisp = 0;
    for (int i = 0; i < g_actorSlots && head; ++i) {
        void* fn = g_vftActor[i];
        if (!fn) continue;
        const uint8_t* m = pe::function_find_bytes(g_wh, fn, head, hn);
        if (!m) continue;
        uint32_t d = 0;
        if (!pe::read_u32(m, hn, &d)) continue;
        if (relSlot < 0) { relSlot = i; newDisp = d; }
        ++relCount;
    }
    if (relSlot >= 0)
        logf("S3: %-22s MT slot %3zu (+0x%03zX) -> retail slot %3d (+0x%03X), field 0x%zX -> 0x%zX  [relaxed, %d match(es)]",
             label, mtSlot, mtOff, relSlot, relSlot * 8, mtDisp, newDisp, relCount);
    else
        logf("S3: %-22s MT slot %3zu (+0x%03zX) -> NOT FOUND in the retail vtable", label, mtSlot, mtOff);
}

void step3_vtable() {
    int n = 0;
    g_vftActor = pe::find_vftable(g_wh, kActorRtti, 0, &n);
    logf("S3: RTTI %s -> %d primary vftable(s)", kActorRtti, n);
    if (!g_vftActor) { logf("S3: BLOCKED -- C_Actor vftable not unique"); return; }
    // slot count: consecutive entries that are .text function starts
    pe::Range text{};
    pe::section(g_wh, ".text", &text);
    int slots = 0;
    while (slots < 2048) {
        void* fn = nullptr;
        if (!pe::read_ptr(g_vftActor, static_cast<size_t>(slots) * 8, &fn)) break;
        if (!fn || !text.contains(fn)) break;
        ++slots;
    }
    g_actorSlots = slots;
    logf("S3: C_Actor vftable at %s, %d slots (0x%X bytes); the Modding Tools build has 409",
         at(g_vftActor).c_str(), slots, slots * 8);

    scan_slot("SetPseudoSpeed",      0x448, kPseudoA, sizeof(kPseudoA), kPseudoAH, sizeof(kPseudoAH), 0x7E8);
    scan_slot("SetPseudoSpeed(b)",   0x448, kPseudoB, sizeof(kPseudoB), nullptr, 0, 0x18);
    scan_slot("ExpHolder",           0x980, kHolder,  sizeof(kHolder),  kPseudoAH, sizeof(kPseudoAH), 0x308);
    scan_slot("UpdateMannequinTags", 0xC98, kTagsA,   sizeof(kTagsA),   kTagsAH, sizeof(kTagsAH), 0x450);
    scan_slot("UpdateTags reqVel",   0xC98, kTagsB,   sizeof(kTagsB),   kTagsBH, sizeof(kTagsBH), 0x574);
    scan_slot("UpdateTags moveVec",  0xC98, kTagsC,   sizeof(kTagsC),   kTagsCH, sizeof(kTagsCH), 0x614);
    scan_slot("UpdateTags stance",   0xC98, kTagsD,   sizeof(kTagsD),   kTagsDH, sizeof(kTagsDH), 0x850);
}

void* actor_via_components(void* e, size_t* offOut, const char** nameOut);

// The live half: find a C_Actor from an entity and name what its fields point at.
void* actor_of_entity(void* e, size_t* offOut) {
    if (!e || !g_vftActor) return nullptr;
    for (size_t off = 8; off < 0x600; off += 8) {
        void* v = nullptr;
        if (!pe::read_ptr(e, off, &v) || !v) continue;
        void* vp = nullptr;
        if (!pe::read_ptr(v, 0, &vp)) continue;
        if (vp == static_cast<const void*>(g_vftActor)) { if (offOut) *offOut = off; return v; }
        const char* nm = rtti_name_of(v);
        if (nm && std::strstr(nm, "C_Actor")) { if (offOut) *offOut = off; return v; }
    }
    return nullptr;
}

void step3_live(uint32_t entityId) {
    void* e = entity_by_id(entityId);
    if (!e) { logf("S3: entity %u not found", entityId); return; }
    logf("S3: entity %u = %p name=%s", entityId, e, safe_str(entity_name(e)).c_str());
    size_t off = 0;
    const char* cls = nullptr;
    void* actor = actor_via_components(e, &off, &cls);
    if (!actor) actor = actor_of_entity(e, &off);
    logf("S3: the actor from the entity: %p at component[%zu] (%s)", actor, off / 8,
         actor ? (cls ? cls : (rtti_name_of(actor) ? rtti_name_of(actor) : "no RTTI")) : "-");
    if (!actor) return;
    uint32_t eid = 0;
    if (pe::read_u32(actor, 0x30, &eid)) logf("S3: actor+0x30 (entity id on the tools build) = %u", eid);
    for (const auto& f : kActorFields) {
        void* v = nullptr;
        if (!pe::read_ptr(actor, f.mtOffset, &v)) { logf("S3: actor+0x%03zX unreadable", f.mtOffset); continue; }
        const char* nm = rtti_name_of(v);
        logf("S3: actor+0x%03zX  %-32s = %p  %s", f.mtOffset, f.label, v, nm ? nm : "(no RTTI)");
    }
    logf("S3: -- every C_Actor field that points at an RTTI-bearing object --");
    for (size_t o = 8; o < 0xC00; o += 8) {
        void* v = nullptr;
        if (!pe::read_ptr(actor, o, &v) || !v) continue;
        const char* nm = rtti_name_of(v);
        if (nm) logf("S3:   actor+0x%03zX  %p  %s", o, v, nm);
    }
}

// One retail vtable slot, in full: where it is, and what displacement each of
// the plugin's four UpdateMannequinTags instruction forms carries there. This
// is how a moved slot is CONFIRMED rather than guessed from one lucky head.
void step3_slot(int slot) {
    if (!g_vftActor) { logf("S3: no C_Actor vftable -- run step3vt first"); return; }
    if (slot < 0 || slot >= g_actorSlots) { logf("S3: slot %d out of range (0..%d)", slot, g_actorSlots - 1); return; }
    void* fn = g_vftActor[slot];
    pe::Range r{};
    pe::function_range(g_wh, fn, &r);
    logf("S3: slot %d (+0x%03X) -> %s, root %zu bytes", slot, slot * 8, at(fn).c_str(), r.size());
    log_bytes("S3:   prologue", fn, 24);
    struct H { const char* what; const uint8_t* head; size_t n; size_t mt; };
    const H heads[] = {
        {"GetPseudoSpeed vslot (mov rdx,[rcx+d])", kTagsAH, sizeof(kTagsAH), 0x450},
        {"requested velocity (movss xmm1,[rdi+d])", kTagsBH, sizeof(kTagsBH), 0x574},
        {"move vector (movsd xmm8,[rdi+d])",        kTagsCH, sizeof(kTagsCH), 0x614},
        {"stance manager (lea rcx,[rdi+d])",        kTagsDH, sizeof(kTagsDH), 0x850},
        {"aiAnim (mov rax,[rbx+d])",                kPseudoAH, sizeof(kPseudoAH), 0x7E8},
    };
    for (const H& h : heads) {
        const uint8_t* m = pe::function_find_bytes(g_wh, fn, h.head, h.n);
        if (!m) { logf("S3:   %-42s absent", h.what); continue; }
        uint32_t d = 0;
        pe::read_u32(m, h.n, &d);
        logf("S3:   %-42s +0x%X  (the tools build: +0x%zX)", h.what, d, h.mt);
    }
}

// Every object reachable from `root` within two pointer hops that carries RTTI
// whose name contains `filter`. This is how C_Actor and the XGenAI object are
// FOUND on retail instead of assumed: the plugin's entity->actor hop is an RVA
// on the tools build and does not port.
void reach(const char* tag, void* root, const char* filter, int depth, size_t span) {
    if (!root) { logf("%s: (null root)", tag); return; }
    for (size_t off = 0; off < span; off += 8) {
        void* v = nullptr;
        if (!pe::read_ptr(root, off, &v) || !v) continue;
        if (reinterpret_cast<uintptr_t>(v) < 0x10000) continue;
        const char* nm = rtti_name_of(v);
        if (nm && (!filter || std::strstr(nm, filter)))
            logf("%s: +0x%03zX -> %p %s", tag, off, v, nm);
        if (depth > 1) {
            char sub[64];
            std::snprintf(sub, sizeof(sub), "%s+0x%03zX", tag, off);
            // one more hop, shallower window
            for (size_t o2 = 0; o2 < 0x120; o2 += 8) {
                void* w = nullptr;
                if (!pe::read_ptr(v, o2, &w) || !w) continue;
                if (reinterpret_cast<uintptr_t>(w) < 0x10000) continue;
                const char* n2 = rtti_name_of(w);
                if (n2 && (!filter || std::strstr(n2, filter)))
                    logf("%s: +0x%03zX -> %p %s", sub, o2, w, n2);
            }
        }
    }
}

void step3_reach(uint32_t entityId, const char* filter) {
    void* e = entity_by_id(entityId);
    if (!e) { logf("S3: entity %u not found", entityId); return; }
    logf("S3: reach from entity %u (%s), filter '%s'", entityId, safe_str(entity_name(e)).c_str(),
         filter ? filter : "(any)");
    reach("S3-reach", e, filter, 2, 0x300);
}

// UpdateMannequinTags callers (D-415): who calls it, on which threads.
std::atomic<unsigned long long> g_tagCalls{0};
unsigned long g_tagThreads[16]{};
unsigned long long g_tagThreadCount[16]{};
std::mutex g_tagMutex;

void on_update_tags(void* self, void*, void*, void*) {
    ++g_tagCalls;
    const unsigned long tid = GetCurrentThreadId();
    std::lock_guard<std::mutex> lock(g_tagMutex);
    for (int i = 0; i < 16; ++i) {
        if (g_tagThreads[i] == tid) { ++g_tagThreadCount[i]; return; }
        if (g_tagThreads[i] == 0) { g_tagThreads[i] = tid; g_tagThreadCount[i] = 1; return; }
    }
}

void step3_tag_hook(int retailSlot, size_t len) {
    if (!g_vftActor || retailSlot < 0 || retailSlot >= g_actorSlots) { logf("S3: no tag slot to hook"); return; }
    void* fn = g_vftActor[retailSlot];
    uint8_t pro[24]{};
    if (!pe::read_bytes(fn, pro, sizeof(pro))) { logf("S3: prologue unreadable"); return; }
    char hex[80]{};
    for (int i = 0; i < 18; ++i) std::snprintf(hex + 3 * i, 4, "%02x ", pro[i]);
    logf("S3: UpdateMannequinTags candidate at slot %d = %s, prologue %s", retailSlot, at(fn).c_str(), hex);
    const char* why = nullptr;
    if (len < 14) { logf("S3: no instruction-aligned prologue length given -- not hooking"); return; }
    logf("S3: copying %zu prologue bytes into the trampoline", len);
    const bool ok = hook::install(fn, pro, len, &on_update_tags, &why);
    logf("S3: tag-update capture %s (%s)", ok ? "INSTALLED" : "REFUSED", why ? why : "?");
}

void step3_tag_report() {
    std::lock_guard<std::mutex> lock(g_tagMutex);
    logf("S3: UpdateMannequinTags calls %llu", g_tagCalls.load());
    for (int i = 0; i < 16 && g_tagThreads[i]; ++i)
        logf("S3:   thread %5lu -> %llu call(s)%s", g_tagThreads[i], g_tagThreadCount[i],
             g_tagThreads[i] == g_mainTid.load() ? "   (the main thread)" : "");
}

// ---------------------------------------------------------------------------
// STEP 4 -- one swing, one hit (D-365, D-343), capture-only
// ---------------------------------------------------------------------------
constexpr const char* kAttackRtti = ".?AVC_CombatActorActionAttack@combatmodule@wh@@";
constexpr const char* kCombatSoulRtti = ".?AVC_CombatSoul@rpgmodule@wh@@";
constexpr size_t kActionEnterImpl = 0x1C8;
constexpr size_t kSlotMelee = 0x150;

std::atomic<unsigned long long> g_enterCalls{0}, g_hitCalls{0};
unsigned long g_enterTid = 0, g_hitTid = 0;

void on_enter_impl(void* self, void* a2, void*, void*) {
    if (++g_enterCalls <= 20) {
        g_enterTid = GetCurrentThreadId();
        logf("S4: EnterImpl this=%p (%s) a2=%p", self, rtti_name_of(self) ? rtti_name_of(self) : "?", a2);
    }
}
void on_hit(void* self, void* a2, void* a3, void* a4) {
    if (++g_hitCalls <= 20) {
        g_hitTid = GetCurrentThreadId();
        logf("S4: hit slot this=%p (%s) a2=%p a3=%p a4=%p",
             self, rtti_name_of(self) ? rtti_name_of(self) : "?", a2, a3, a4);
    }
}

// The prologue lengths are instruction-aligned values read off a disassembly
// of THIS image (WO-146B): EnterImpl at +0xB27308 ends instructions at 15, 20,
// 23, 28; the C_CombatSoul hit slot at +0x726DF4 at 15 and 22 (a RIP-relative
// load follows). A blanket 18 splits an instruction in both and crashes the
// game the moment combat runs one of them.
void step4(bool arm, size_t lenEnter, size_t lenHit) {
    int na = 0, ns = 0;
    void* const* vftAttack = pe::find_vftable(g_wh, kAttackRtti, 0, &na);
    void* const* vftSoul = pe::find_vftable(g_wh, kCombatSoulRtti, 0, &ns);
    logf("S4: RTTI %s -> %d vftable(s); %s -> %d", kAttackRtti, na, kCombatSoulRtti, ns);
    if (vftAttack) {
        void* fn = nullptr;
        pe::read_ptr(vftAttack, kActionEnterImpl, &fn);
        logf("S4: C_CombatActorActionAttack vftable %s, slot 0x1C8 -> %s",
             at(vftAttack).c_str(), at(fn).c_str());
        uint8_t pro[24]{};
        if (fn && pe::read_bytes(fn, pro, sizeof(pro))) {
            char hex[80]{};
            for (int i = 0; i < 18; ++i) std::snprintf(hex + 3 * i, 4, "%02x ", pro[i]);
            logf("S4: slot 0x1C8 prologue %s", hex);
            if (arm && lenEnter >= 14) {
                const char* why = nullptr;
                logf("S4: copying %zu prologue bytes into the trampoline", lenEnter);
                const bool ok = hook::install(fn, pro, lenEnter, &on_enter_impl, &why);
                logf("S4: EnterImpl capture %s (%s)", ok ? "INSTALLED" : "REFUSED", why ? why : "?");
            }
        }
    }
    if (vftSoul) {
        void* fn = nullptr;
        pe::read_ptr(vftSoul, kSlotMelee, &fn);
        logf("S4: C_CombatSoul vftable %s, melee hit slot 0x150 -> %s", at(vftSoul).c_str(), at(fn).c_str());
        uint8_t pro[24]{};
        if (fn && pe::read_bytes(fn, pro, sizeof(pro))) {
            char hex[80]{};
            for (int i = 0; i < 18; ++i) std::snprintf(hex + 3 * i, 4, "%02x ", pro[i]);
            logf("S4: hit slot prologue %s", hex);
            if (arm && lenHit >= 14) {
                const char* why = nullptr;
                logf("S4: copying %zu prologue bytes into the trampoline", lenHit);
                const bool ok = hook::install(fn, pro, lenHit, &on_hit, &why);
                logf("S4: hit capture %s (%s)", ok ? "INSTALLED" : "REFUSED", why ? why : "?");
            }
        }
    }
}

void step4_report() {
    logf("S4: EnterImpl calls %llu (thread %lu), hit calls %llu (thread %lu); main thread %lu",
         g_enterCalls.load(), g_enterTid, g_hitCalls.load(), g_hitTid, g_mainTid.load());
}

// ---------------------------------------------------------------------------
// STEP 5 -- the NPC-state context (D-553, D-516)
// ---------------------------------------------------------------------------
const char* kElementRtti[] = {
    ".?AVC_StanceElement@NPCState@xgenaimodule@wh@@",
    ".?AVC_StanceElementRequired@NPCState@xgenaimodule@wh@@",
    ".?AVC_UnstanceElement@NPCState@xgenaimodule@wh@@",
    ".?AVC_UnstanceElementRequired@NPCState@xgenaimodule@wh@@",
    ".?AVC_HandContentElement@NPCState@xgenaimodule@wh@@",
    ".?AVC_HandContentElementRequired@NPCState@xgenaimodule@wh@@",
    ".?AVC_MinigameElement@NPCState@xgenaimodule@wh@@",
    ".?AVC_NPCContext@NPCState@xgenaimodule@wh@@",
    ".?AVC_NPCRequiredState@NPCState@xgenaimodule@wh@@",
    ".?AVC_NPCCurrentState@NPCState@xgenaimodule@wh@@",
    ".?AVC_NPC@xgenaimodule@wh@@",
};
void* const* g_vftElem[16]{};
int g_nElem = 0;

void step5_classes() {
    g_nElem = 0;
    for (const char* r : kElementRtti) {
        int n = 0;
        void* const* v = pe::find_vftable(g_wh, r, 0, &n);
        const bool present = pe::has_rtti(g_wh, r);
        logf("S5: %-58s rtti=%s vftables=%d %s", r, present ? "yes" : "NO", n,
             v ? at(v).c_str() : "");
        if (v && g_nElem < 16) g_vftElem[g_nElem++] = v;
    }
}

// Does `p` look like an NPC state? Its element vector [+0x10,+0x18) must hold
// shared_ptrs whose objects carry one of the element vftables.
bool looks_like_state(void* p, int* elems) {
    void *b = nullptr, *e = nullptr;
    if (!pe::read_ptr(p, 0x10, &b) || !pe::read_ptr(p, 0x18, &e)) return false;
    if (!b || !e || b > e) return false;
    const size_t span = static_cast<const uint8_t*>(e) - static_cast<const uint8_t*>(b);
    if (span == 0 || span > 0x400 || (span % 8) != 0) return false;
    int found = 0, n = 0;
    for (size_t i = 0; i < span; i += 8) {
        void* sp = nullptr;
        if (!pe::read_ptr(b, i, &sp) || !sp) continue;
        ++n;
        void* vp = nullptr;
        if (!pe::read_ptr(sp, 0, &vp)) continue;
        for (int k = 0; k < g_nElem; ++k)
            if (vp == static_cast<const void*>(g_vftElem[k])) { ++found; break; }
        const char* nm = rtti_name_of(sp);
        if (nm && std::strstr(nm, "Element")) ++found;
    }
    if (elems) *elems = found;
    return found > 0 && n > 0;
}

// Scan one object for an embedded NPC context: the offset K where K+0x90 looks
// like a current state (an element vector whose objects carry the element
// vftables). The tools build has K = 0x9C0.
void ctx_scan(void* obj, size_t span) {
    if (!obj) { logf("S5: (null object)"); return; }
    if (g_nElem == 0) { logf("S5: run step5classes first (no element vftables)"); return; }
    logf("S5: scanning %p (%s) for an embedded context, 0..0x%zX", obj,
         rtti_name_of(obj) ? rtti_name_of(obj) : "no RTTI", span);
    int found = 0;
    for (size_t k = 0; k < span; k += 8) {
        void* cand = static_cast<char*>(obj) + k;
        int elems = 0;
        if (!looks_like_state(static_cast<char*>(cand) + 0x90, &elems)) continue;
        ++found;
        logf("S5: candidate context at +0x%zX (current state +0x90 holds %d element(s))%s",
             k, elems, k == 0x9C0 ? "   <-- the Modding Tools offset" : "");
        int e2 = 0;
        if (looks_like_state(static_cast<char*>(cand) + 0x180, &e2))
            logf("S5:   the loaded state at +0x180 holds %d element(s)", e2);
        uint32_t pending = 0;
        if (pe::read_u32(cand, 0x5E8, &pending)) logf("S5:   pending change id (+0x5E8) = %d", (int)pending);
        void *b = nullptr, *en = nullptr;
        void* cur = static_cast<char*>(cand) + 0x90;
        if (pe::read_ptr(cur, 0x10, &b) && pe::read_ptr(cur, 0x18, &en) && b && en) {
            const size_t sp = static_cast<const uint8_t*>(en) - static_cast<const uint8_t*>(b);
            for (size_t i = 0; i < sp && i < 0x100; i += 8) {
                void* el = nullptr;
                if (!pe::read_ptr(b, i, &el) || !el) continue;
                logf("S5:   current element[%zu] %p %s", i / 8, el,
                     rtti_name_of(el) ? rtti_name_of(el) : "(no RTTI)");
            }
        }
        if (found >= 4) break;
    }
    if (!found) logf("S5: no embedded context found in that range");
}

// The actor of an entity, through CEntity's component array (entity+0xB8 ..
// +0xC0 on retail, pairs of {component, refcount}); the plugin's own hop is a
// fixed EntityModule RVA and does not port.
void* actor_via_components(void* e, size_t* offOut, const char** nameOut) {
    void *b = nullptr, *en = nullptr;
    if (!e || !pe::read_ptr(e, 0xB8, &b) || !pe::read_ptr(e, 0xC0, &en) || !b || !en) return nullptr;
    const size_t span = static_cast<const uint8_t*>(en) - static_cast<const uint8_t*>(b);
    for (size_t i = 0; i < span && i < 0x200; i += 8) {
        void* c = nullptr;
        if (!pe::read_ptr(b, i, &c) || !c) continue;
        const char* nm = rtti_name_of(c);
        if (!nm) continue;
        if (std::strstr(nm, "C_Player@entitymodule") || std::strstr(nm, "Actor@entitymodule")) {
            if (offOut) *offOut = i;
            if (nameOut) *nameOut = nm;
            return c;
        }
    }
    return nullptr;
}

void step5_live(uint32_t entityId) {
    void* e = entity_by_id(entityId);
    if (!e) { logf("S5: entity %u not found", entityId); return; }
    logf("S5: entity %u = %p name=%s", entityId, e, safe_str(entity_name(e)).c_str());
    // The NPC's XGenAI object is two pointer hops off the entity (retail has no
    // exported NPC-manager anchor and C_NPCContext has no RTTI, so the object is
    // found by walking to the one whose RTTI says C_NPC@xgenaimodule).
    void* npc = nullptr;
    for (size_t off = 0; off < 0x300 && !npc; off += 8) {
        void* v = nullptr;
        if (!pe::read_ptr(e, off, &v) || !v || reinterpret_cast<uintptr_t>(v) < 0x10000) continue;
        const char* nm = rtti_name_of(v);
        if (nm && std::strstr(nm, "C_NPC@xgenaimodule")) { npc = v; logf("S5: C_NPC at entity+0x%03zX", off); break; }
        for (size_t o2 = 0; o2 < 0x140; o2 += 8) {
            void* w = nullptr;
            if (!pe::read_ptr(v, o2, &w) || !w || reinterpret_cast<uintptr_t>(w) < 0x10000) continue;
            const char* n2 = rtti_name_of(w);
            if (n2 && std::strstr(n2, "C_NPC@xgenaimodule")) {
                npc = w;
                logf("S5: C_NPC at entity+0x%03zX -> +0x%03zX = %p", off, o2, w);
                break;
            }
        }
    }
    if (!npc) { logf("S5: no C_NPC@xgenaimodule object is reachable from this entity"); return; }
    ctx_scan(npc, 0x2000);
}

// ---------------------------------------------------------------------------
// The command file
// ---------------------------------------------------------------------------
volatile bool g_stop = false;

void handle(const std::string& line) {
    logf("CMD: %s", line.c_str());
    auto arg = [&](int i) -> uint32_t {
        size_t p = 0; int seen = 0;
        while (p < line.size()) {
            while (p < line.size() && line[p] != ' ') ++p;
            while (p < line.size() && line[p] == ' ') ++p;
            if (++seen == i) break;
        }
        if (p >= line.size()) return 0;
        return static_cast<uint32_t>(std::strtoul(line.c_str() + p, nullptr, 0));
    };
    if (!g_identityOk) { logf("CMD: refused -- the build identity did not match"); return; }

    // Image-only work runs HERE, on the probe's own worker thread: it reads the
    // mapped module and nothing else, and the main-thread queue does not exist
    // until step1 has hooked the frame. Anything that touches live game objects
    // is posted to the frame hook instead.
    if (line.rfind("step1find", 0) == 0)        step1_find();
    else if (line.rfind("step1hook", 0) == 0)   step1_hook();            // must NOT run on the game thread
    else if (line.rfind("step1report", 0) == 0) step1_report();
    else if (line.rfind("step2rttr", 0) == 0)   post([] { step2_rttr(); });
    else if (line.rfind("rttrnames", 0) == 0)   { const uint32_t off = arg(1), st = arg(2), n = arg(3);
                                                  post([off, st, n] { rttr_names(off ? off : 0xD0, st ? st : 24, n ? (int)n : 40); }); }
    else if (line.rfind("step2", 0) == 0)       { step2_genv(); post([] { step2_rttr(); step2_entities(); }); }
    else if (line.rfind("step3vt", 0) == 0)     step3_vtable();
    else if (line.rfind("step3slot", 0) == 0)    step3_slot(static_cast<int>(arg(1)));
    else if (line.rfind("step3live", 0) == 0)   { const uint32_t id = arg(1); post([id] { step3_live(id ? id : kPlayerEntityId); }); }
    else if (line.rfind("reachactor", 0) == 0)  { const uint32_t id = arg(1); post([id] { step3_reach(id, "C_Actor"); }); }
    else if (line.rfind("reachai", 0) == 0)     { const uint32_t id = arg(1); post([id] { step3_reach(id, "xgenai"); }); }
    else if (line.rfind("reachall", 0) == 0)    { const uint32_t id = arg(1); post([id] { step3_reach(id, nullptr); }); }
    else if (line.rfind("dumpmem", 0) == 0)     { const uint32_t a = arg(1); (void)a;
                                                  unsigned long long addr = std::strtoull(line.c_str() + line.find(' ') + 1, nullptr, 0);
                                                  const uint32_t len = arg(2);
                                                  post([addr, len] { dump_object("MEM", reinterpret_cast<void*>(addr), len ? len : 0x100); }); }
    else if (line.rfind("step3taghook", 0) == 0){ step3_tag_hook(static_cast<int>(arg(1)), arg(2)); }
    else if (line.rfind("step3tagreport", 0) == 0) step3_tag_report();
    else if (line.rfind("step4arm", 0) == 0)    step4(true, arg(1), arg(2));
    else if (line.rfind("step4report", 0) == 0) step4_report();
    else if (line.rfind("step4", 0) == 0)       step4(false, 0, 0);
    else if (line.rfind("step5classes", 0) == 0) step5_classes();
    else if (line.rfind("step5live", 0) == 0)   { const uint32_t id = arg(1); post([id] { step5_live(id); }); }
    else if (line.rfind("ctxscan", 0) == 0)     { unsigned long long a = std::strtoull(line.c_str() + line.find(' ') + 1, nullptr, 0);
                                                  post([a] { ctx_scan(reinterpret_cast<void*>(a), 0x2000); }); }
    else if (line.rfind("entities", 0) == 0)    post([] { step2_entities(); });
    else if (line.rfind("dumpentity", 0) == 0)  { const uint32_t id = arg(1); post([id] { dump_object("DUMP", entity_by_id(id), 0x400); }); }
    else if (line.rfind("bye", 0) == 0)         g_stop = true;
    else logf("CMD: unknown verb");
}

DWORD WINAPI worker(LPVOID) {
    logf("PROBE: worker thread up");
    const std::string cmd = self_stem() + ".cmd";
    logf("PROBE: command file %s", cmd.c_str());
    step0_identity();
    while (!g_stop) {
        HANDLE h = CreateFileA(cmd.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr,
                               OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (h != INVALID_HANDLE_VALUE) {
            char buf[4096]{};
            DWORD got = 0;
            ReadFile(h, buf, sizeof(buf) - 1, &got, nullptr);
            SetFilePointer(h, 0, nullptr, FILE_BEGIN);
            SetEndOfFile(h);
            CloseHandle(h);
            std::string all(buf, got);
            size_t pos = 0;
            while (pos < all.size()) {
                size_t nl = all.find('\n', pos);
                if (nl == std::string::npos) nl = all.size();
                std::string line = all.substr(pos, nl - pos);
                while (!line.empty() && (line.back() == '\r' || line.back() == ' ')) line.pop_back();
                if (!line.empty()) handle(line);
                pos = nl + 1;
            }
        }
        Sleep(200);
    }
    logf("PROBE: worker thread out");
    return 0;
}

} // namespace
} // namespace wo146b

BOOL APIENTRY DllMain(HMODULE mod, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(mod);
        wo146b::logf("PROBE: WO-146B retail probe attached to pid %lu", GetCurrentProcessId());
        CreateThread(nullptr, 0, &wo146b::worker, nullptr, 0, nullptr);
    }
    return TRUE;
}
