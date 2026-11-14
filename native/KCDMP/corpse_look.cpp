// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
//
// WO-221 E.1: see corpse_look.h. Engine calls made here, all on the main thread (the pipe's ops run there), none under the fault
// guard: the actor's own "dirt helper" accessor (its vftable slot +0x740 -- it makes the helper on first use), the helper's
// SetBloodGlobal (slot 2), and the blood read SetBloodGlobal itself makes (the helper's material object, slot +0x138). They are the
// calls the game's script bind Actor.AddBlood makes on this thread. Pointers are read under the guard and checked first.
#include "corpse_look.h"
#include "wo221.h"
#include "wo203.h"
#include "wo163.h"
#include "anchors.h"
#include "engine.h"
#include "fault_guard.h"
#include "hits.h"
#include "log.h"
#include "motion.h"

#include <windows.h>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>

namespace kcdmp::corpselook {

namespace {

constexpr size_t kActorHelper = 0x2E8;        // the actor's helper, null until first use
constexpr size_t kActorSlotHelper = 0x740;    // C_Actor's accessor (makes it)
constexpr size_t kHelperObj = 0x20;           // the helper's material object
constexpr size_t kObjSlotRead = 0x138;        // float* Read(obj, float* tmp, int zone)
constexpr size_t kHelperSlotSet = 2;          // bool SetBloodGlobal(helper, int zone, float value, bool add)

std::atomic<bool> g_armed{false};
void* const* g_vftHelper = nullptr;
HMODULE g_em = nullptr;
std::string g_why = "not installed";
std::atomic<uint32_t> c_reads{0}, c_sets{0}, c_zonesSet{0}, c_noBody{0}, c_noHelper{0}, c_refused{0};

template <class T> bool rd(const void* base, size_t off, T* out) {
    KCDMP_FAULT_READ(site, "corpse_look::rd");
    if (!base || !fault::plausible_address(reinterpret_cast<uintptr_t>(base) + off)) return false;
    return fault::guarded(site, [&] { *out = *reinterpret_cast<const T*>(static_cast<const char*>(base) + off); });
}

bool has(const void* fn, std::initializer_list<uint8_t> pat) { return anchor::function_has_bytes(g_em, fn, pat.begin(), pat.size()); }

// the body's helper as it is (null: never used), checked to be a C_ActorDirtHelper
void* helper_of(void* actor) {
    void* h = nullptr; void* vp = nullptr;
    if (!rd(actor, kActorHelper, &h) || !h) return nullptr;
    if (!rd(h, 0, &vp) || vp != static_cast<const void*>(g_vftHelper)) return nullptr;
    return h;
}

// the six zones of a body, as the helper's own setter reads them before it adds
bool read_zones(void* helper, float out[kZones]) {
    void* obj = nullptr; void* const* vt = nullptr; void* fn = nullptr;
    if (!rd(helper, kHelperObj, &obj) || !obj || !rd(obj, 0, &vt) || !vt || !rd(vt, kObjSlotRead, &fn) || !fn) return false;
    if (!fault::plausible_address(reinterpret_cast<uintptr_t>(fn))) return false;
    for (int z = 0; z < kZones; ++z) {
        float tmp = 0.0f;
        const float* p = reinterpret_cast<const float* (__fastcall*)(void*, float*, int)>(fn)(obj, &tmp, z);
        float v = 0.0f;
        out[z] = (p && rd(p, 0, &v) && std::isfinite(v)) ? v : 0.0f;
    }
    return true;
}

uint8_t op(uint8_t opc, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (cap < 1 + kZones || len < 3) return wo163::kRBadRequest;
    const uint8_t nl = body[1];
    const size_t want = opc == kOpSet ? static_cast<size_t>(2 + nl + kZones) : static_cast<size_t>(2 + nl);
    if (nl == 0 || nl > 63 || want != len) return wo163::kRBadRequest;
    char name[64]{};
    std::memcpy(name, body + 2, nl);
    std::memset(out, 0, 1 + kZones);
    auto answer = [&](uint8_t code, size_t n) { out[0] = code; *outLen = n; return wo163::kROk; };
    if (!wo221::on(wo221::kBitCorpseLook)) return answer(kOff, 1);
    if (!g_armed.load()) return answer(kNotArmed, 1);
    const uint32_t eid = hits::eid_of_name(name);
    void* actor = eid ? motion::actor_of(eid) : nullptr;
    if (!actor) { c_noBody.fetch_add(1, std::memory_order_relaxed); return answer(kNoBody, 1); }
    if (opc == kOpRead) {
        void* h = helper_of(actor);
        if (!h) { c_noHelper.fetch_add(1, std::memory_order_relaxed); return answer(kNoHelper, 1 + kZones); }
        float z[kZones]{};
        if (!read_zones(h, z)) { c_refused.fetch_add(1, std::memory_order_relaxed); return answer(kNoHelper, 1 + kZones); }
        for (int i = 0; i < kZones; ++i) out[1 + i] = static_cast<uint8_t>((std::min)(255.0f, (std::max)(0.0f, z[i] * 255.0f + 0.5f)));
        c_reads.fetch_add(1, std::memory_order_relaxed);
        return answer(kOk, 1 + kZones);
    }
    // Set: the helper is made the actor's own way when the body never had one; a zone is written only when it differs
    void* const* avt = nullptr; void* accessor = nullptr;
    if (!rd(actor, 0, &avt) || !avt || !rd(avt, kActorSlotHelper, &accessor) || !accessor ||
        !has(accessor, {0x48, 0x83, 0xBB, 0xE8, 0x02, 0x00, 0x00, 0x00})) {   // cmp qword ptr [rbx+2E8h], 0
        c_refused.fetch_add(1, std::memory_order_relaxed);
        return answer(kNotArmed, 1);
    }
    void* h = reinterpret_cast<void* (__fastcall*)(void*)>(accessor)(actor);
    void* vp = nullptr;
    if (!h || !rd(h, 0, &vp) || vp != static_cast<const void*>(g_vftHelper)) { c_refused.fetch_add(1, std::memory_order_relaxed); return answer(kNoHelper, 1); }
    float have[kZones]{};
    read_zones(h, have);
    uint8_t changed = 0;
    auto set = reinterpret_cast<bool (__fastcall*)(void*, int, float, bool)>(g_vftHelper[kHelperSlotSet]);
    for (int z = 0; z < kZones; ++z) {
        const float v = body[2 + nl + z] / 255.0f;
        if (std::fabs(v - have[z]) < 0.02f) continue;
        set(h, z, v, false);
        ++changed;
    }
    c_sets.fetch_add(1, std::memory_order_relaxed);
    c_zonesSet.fetch_add(changed, std::memory_order_relaxed);
    if (changed) logf("WO221-CORPSE npc=%s wounds set as the host's body has them: %u %u %u %u %u %u of 255 (%u zone(s) changed)", name, body[2 + nl],
                      body[3 + nl], body[4 + nl], body[5 + nl], body[6 + nl], body[7 + nl], changed);
    out[1] = changed;
    return answer(kOk, 2);
}

void fail(const char* why) { g_why = why; logf("WO221-CORPSE NOT ARMED: %s -- a dead body's wounds are not carried to the joiner (0.65.0's behaviour)", why); }

void install() {
    g_em = GetModuleHandleA("EntityModule.dll");
    if (!g_em) { fail("EntityModule.dll not loaded"); return; }
    g_vftHelper = anchor::find_vftable(g_em, ".?AVC_ActorDirtHelper@entitymodule@wh@@", 0);
    if (!g_vftHelper) { fail("C_ActorDirtHelper's vftable not found by RTTI"); return; }
    // SetBloodGlobal: the zone bound (cmp dword ptr [rsp+30h], 6), the read through the material object's slot +0x138
    if (!has(g_vftHelper[kHelperSlotSet], {0x83, 0x7C, 0x24, 0x30, 0x06}) || !has(g_vftHelper[kHelperSlotSet], {0xFF, 0x90, 0x38, 0x01, 0x00, 0x00})) {
        fail("C_ActorDirtHelper's SetBloodGlobal is not the function read in this build");
        return;
    }
    g_armed = true; g_why = "armed";
    logf("WO221-CORPSE armed: C_ActorDirtHelper found by RTTI, its SetBloodGlobal checked by bytes");
}

int status(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "armed=%d (%s) reads=%u sets=%u zones_set=%u no_body=%u never_bloodied=%u refused=%u", g_armed.load() ? 1 : 0,
                         g_why.c_str(), c_reads.load(), c_sets.load(), c_zonesSet.load(), c_noBody.load(), c_noHelper.load(), c_refused.load());
}

const bool kReg = wo203::reg_install("corpse_look", &install) && wo203::reg_op(kOpRead, &op) && wo203::reg_op(kOpSet, &op) &&
                  wo221::add_status("corpse_look", &status);

} // namespace

} // namespace kcdmp::corpselook
