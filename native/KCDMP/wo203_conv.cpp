// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-203 area "conv": Act I, join the conversation -- the native half on the STARTER's machine (the HUD's rows, the vote) and on the
// LISTENER's (a line voiced on a body).
//
// The HUD's interface (C_UIHudInteractiveDialog, GUIModule) gets its rows from the game's own dialogue interaction, which refills them
// on every input (rows, SetDialogSequences, then SetActiveSequenceChanged(current, 0)). WO-201's SetDialogSequences detour
// (wo201_vote.cpp) calls before_flush() on the rows just before they go to Flash: they are COPIED there (their loc keys, icons, marks)
// and never written -- WO-203's first live test wrote the vote icon into them and the game's next clear crashed (crash 065554). The
// vote icon is shown by the mod's Lua re-sending the copied rows with "kct_vote" in the voted row and SetActiveSequenceChanged(current)
// (live-proven: the highlight stays, moving it keeps the icon, the next decision clears it). The highlight the game last set is tracked
// by swapping the interface's SetActiveSequenceChanged slot (the first slot after SetDialogSequences' whose function references the
// name "SetActiveSequenceChanged"; logged against the build the research read). WO-204 III.3 sends the copied rows to the listener.
//
// Pipe ops (wo203.h, the conv range):
//   91 ConvPoll [op][on:1] -> [armed:1][current:1 (255 unknown)][voteRow:1 (255 none)][n:1] per row [used:1][iconLen:1][icon]
//      [difficulty:4][value:4][flag:1][textLen:1][text], then (WO-204) [snapSeq:4][onRight:1]   (on=1 arms WO-201's detour if needed)
//      then (WO-207 A.3) [timerSeq:4][timerOn:1][limit:f32][sinceSetMs:4][sinceUpdateMs:4]: the decision timer the starter's HUD shows
//   92 ConvVoteMark [op][index:1 (255 = clear)][nameLen:1][name] -> [result:1] the vote recorded (0), out of range (2), not armed (3)
//   93 ConvSpeak [op][eid:4][durMs:4][pl:1][path] then (WO-207) [flags:1][nl:1][name] -> [result:1] 0 spoken by the body's own speak
//      manager, 1 no actor, 2 no speak manager, 3 not resolved, 4 faulted (the LISTENER: a line of the starter's conversation, voiced
//      the way the engine voices it, so its lip-sync and positional voice run; the actor's speak-manager slot is read out of the
//      engine's own speak callback OnAudioStopFile; the original Speak is called, past WO-201's capture, so the line is never captured
//      back). WO-207 A.4: flags bit 0 -- a body without a speak manager gets one the engine's own way (the slot after the getter: the
//      get-or-create the engine's dialogue and barks call, C_Actor +0x210 on the build the research read); bit 1 -- eid 0 is the
//      entity named <name> here (the host listening: its own NPC, no copy id).
#include "wo203.h"
#include "wo201.h"
#include "motion.h"
#include "npc_drive.h"
#include "engine.h"
#include "wo163.h"
#include "anchors.h"
#include "fault_guard.h"
#include "log.h"
#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>

namespace kcdmp::wo203::conv {

namespace {

constexpr size_t kRowSize = 0x28;
constexpr int kMaxRows = 8;
constexpr char kVoteIcon[] = "kct_vote";
constexpr char kHudRtti[] = ".?AVC_UIHudInteractiveDialog@guimodule@wh@@";
constexpr char kActiveName[] = "SetActiveSequenceChanged";

// A CryString the engine may keep: header {refcount, len, cap} before the text; a large positive refcount is never freed.
struct CryStr {
    int32_t ref, len, cap;
    char data[200];
    void set(const char* s) {
        size_t n = std::strlen(s);
        if (n + 1 > sizeof data) n = sizeof data - 1;
        ref = 0x40000000; len = cap = static_cast<int32_t>(n);
        std::memcpy(data, s, n);
        data[n] = 0;
    }
};

CryStr g_iconStr;           // "kct_vote"
CryStr g_labelStr[2];       // the label row's text (two, alternating)
int g_labelNext = 0;

const char* g_why = "not resolved";
using ActiveFn = void (*)(void* self, int32_t index, int32_t stopper);
using SetFn = void (*)(void* self, uint64_t isOnRight);
ActiveFn g_origActive = nullptr;
int g_setSlot = -1, g_activeSlot = -1;
std::atomic<void*> g_activeSelf{nullptr};
std::atomic<int32_t> g_current{-1};

struct Vote {
    bool on = false;
    void* self = nullptr;
    int index = -1;
    char text[200] = {};        // the voted row's own text (without our label)
    char name[48] = {};         // the listener's in-game name (the label path)
    bool label = false;
    // what the row held before we wrote it (restored on a move or a clear while the same row object is there)
    char* rowAt = nullptr;
    char* origIcon = nullptr;
    char* origText = nullptr;
};
Vote g_vote;   // main thread only

// I.3c: the voice on the listener
size_t g_speakManSlot = 0;
CryStr g_speakPath[16];
int g_speakNext = 0;
CryStr g_emptyStr, g_purposeStr;
std::atomic<uint32_t> c_spoken{0}, c_speakNoActor{0}, c_speakNoMan{0}, c_speakFault{0};
std::atomic<uint32_t> c_speakMade{0}, c_speakMakeRefused{0}, c_speakByName{0}, c_speakNoName{0};   // WO-207 A.4
fault::Site g_speakSite{"wo203conv::speak", fault::Kind::Call};

std::atomic<uint32_t> c_marks{0}, c_icons{0}, c_labels{0}, c_reflush{0}, c_reflushFault{0}, c_clears{0}, c_stale{0}, c_active{0};

fault::Site g_readSite{"wo203conv::read", fault::Kind::Read};
fault::Site g_writeSite{"wo203conv::write", fault::Kind::Read};
fault::Site g_callSite{"wo203conv::reflush", fault::Kind::Call};

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
bool rd_str(const char* p, char* out, size_t n) {
    out[0] = 0;
    if (!p || !fault::plausible_address(reinterpret_cast<uintptr_t>(p))) return false;
    return fault::guarded(g_readSite, [&] {
        size_t i = 0;
        for (; i + 1 < n && p[i]; ++i) out[i] = p[i];
        out[i] = 0;
    });
}

bool rows(void* self, char** b, size_t* n) {
    int ob = -1, oe = -1;
    if (!wo201vote::rows_offsets(&ob, &oe)) return false;
    char* e = nullptr;
    if (!rd(self, ob, b) || !rd(self, oe, &e) || !*b || e < *b) return false;
    *n = static_cast<size_t>(e - *b) / kRowSize;
    return true;
}

void hk_active(void* self, int32_t index, int32_t stopper) {
    g_activeSelf = self;
    g_current = index;
    c_active.fetch_add(1, std::memory_order_relaxed);
    g_origActive(self, index, stopper);
}

// WO-207 A.3: the decision timer. The HUD interface's SetDialogTimeLimit() / UpdateDialogTimeLimit(float 1 -> 0) / ClearDialogTimeLimit()
// (each named by the Flash function it calls; WO-203's map: +0x28 / +0x30 / +0x38) are swapped like the highlight above: each call is
// noted and the game's own runs unchanged. Read-only towards the game: the listener's agent draws the same timer on its own HUD.
using TlFn = void (*)(void* self);
using TlUpdFn = void (*)(void* self, float limit);
TlFn g_origSetTl = nullptr, g_origClearTl = nullptr;
TlUpdFn g_origUpdTl = nullptr;
int g_setTlSlot = -1, g_updTlSlot = -1, g_clearTlSlot = -1;
std::atomic<uint32_t> g_tlSeq{0};
std::atomic<int> g_tlOn{0};
std::atomic<float> g_tlLimit{1.0f};
std::atomic<uint64_t> g_tlSetAt{0}, g_tlUpdAt{0};
std::atomic<uint32_t> c_tlSets{0}, c_tlUpdates{0}, c_tlClears{0};

void hk_set_tl(void* self) {
    const uint64_t now = GetTickCount64();
    g_tlLimit = 1.0f; g_tlSetAt = now; g_tlUpdAt = now; g_tlOn = 1;
    g_tlSeq.fetch_add(1);
    c_tlSets.fetch_add(1, std::memory_order_relaxed);
    g_origSetTl(self);
}
void hk_upd_tl(void* self, float limit) {
    g_tlLimit = limit; g_tlUpdAt = GetTickCount64();
    c_tlUpdates.fetch_add(1, std::memory_order_relaxed);
    g_origUpdTl(self, limit);
}
void hk_clear_tl(void* self) {
    if (g_tlOn.exchange(0)) g_tlSeq.fetch_add(1);
    c_tlClears.fetch_add(1, std::memory_order_relaxed);
    g_origClearTl(self);
}

// Put back what our vote wrote into a row, when that row object is still the one we wrote.

// The rows as they go to Flash, copied while they are valid (they are not after the flush: WO-203's first live test wrote into them
// later and the game's next clear crashed). Read-only: nothing of the game's is ever written.
struct SnapRow { uint8_t used; char text[160]; char icon[48]; int32_t diff, val; uint8_t flag; };
SnapRow g_snap[kMaxRows];
int g_snapN = 0;
uint32_t g_snapSeq = 0;
uint8_t g_snapOnRight = 0;

void before_flush(void* self) {
    char* b = nullptr; size_t n = 0;
    int k = 0;
    if (rows(self, &b, &n)) {
        for (size_t i = 0; i < n && k < kMaxRows; ++i, ++k) {
            char* row = b + i * kRowSize;
            SnapRow& r = g_snap[k];
            char* text = nullptr; char* icon = nullptr;
            r = SnapRow{};
            rd(row, 0, &text); rd(row, 0x08, &r.used); rd(row, 0x10, &icon); rd(row, 0x18, &r.diff); rd(row, 0x1C, &r.val); rd(row, 0x20, &r.flag);
            rd_str(text, r.text, sizeof r.text); rd_str(icon, r.icon, sizeof r.icon);
        }
    }
    g_snapN = k;
    g_snapOnRight = wo201vote::last_on_right() ? 1 : 0;
    ++g_snapSeq;
    if (g_vote.on) { g_vote.on = false; c_clears.fetch_add(1); }   // a new set of rows: the old vote is over
}

// The HUD interface: the last flush's, or (before any flush since the detour was armed) the last highlight's
void* hud_self() {
    void* s = wo201vote::last_self();
    return s ? s : g_activeSelf.load();
}


uint8_t op_poll(uint8_t, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (len < 2 || cap < 4) return wo163::kRBadRequest;
    bool armed = wo201vote::entry() != nullptr;
    if (body[1]) {
        const char* why = "";
        armed = wo201vote::arm_now(&why);
        if (!armed) { static bool told = false; if (!told) { told = true; logf("WO203-CONV the HUD's flush is not armed (%s) -- no vote icon on this machine", why); } }
    }
    void* self = hud_self();
    const int32_t cur = (g_activeSelf.load() == self) ? g_current.load() : -1;
    out[0] = armed ? 1 : 0;
    out[1] = static_cast<uint8_t>(cur >= 0 && cur < 255 ? cur : 255);
    out[2] = static_cast<uint8_t>(g_vote.on && g_vote.index >= 0 ? g_vote.index : 255);
    out[3] = 0;
    size_t o = 4;
    uint8_t k = 0;
    for (int i = 0; i < g_snapN; ++i) {
        const SnapRow& r = g_snap[i];
        const size_t tl = std::strlen(r.text), il = std::strlen(r.icon);
        if (o + 3 + tl + il + 9 > cap) break;
        out[o] = r.used ? 1 : 0; out[o + 1] = static_cast<uint8_t>(il); std::memcpy(out + o + 2, r.icon, il); o += 2 + il;
        std::memcpy(out + o, &r.diff, 4); std::memcpy(out + o + 4, &r.val, 4); out[o + 8] = r.flag ? 1 : 0; o += 9;
        out[o] = static_cast<uint8_t>(tl); std::memcpy(out + o + 1, r.text, tl); o += 1 + tl;
        ++k;
    }
    out[3] = k;
    // WO-204 III.3: the snapshot's sequence (a new set of rows = a new decision) and the side it was shown on, after the rows
    if (o + 5 <= cap) { std::memcpy(out + o, &g_snapSeq, 4); out[o + 4] = g_snapOnRight; o += 5; }
    // WO-207 A.3: the decision timer as the starter's HUD shows it (after the snapshot's tail, so an older agent stops before it)
    if (o + 17 <= cap) {
        const uint64_t now = GetTickCount64();
        const uint32_t seq = g_tlSeq.load(), sinceSet = static_cast<uint32_t>(now - g_tlSetAt.load()), sinceUpd = static_cast<uint32_t>(now - g_tlUpdAt.load());
        const float lim = g_tlLimit.load();
        std::memcpy(out + o, &seq, 4); out[o + 4] = static_cast<uint8_t>(g_tlOn.load() ? 1 : 0);
        std::memcpy(out + o + 5, &lim, 4); std::memcpy(out + o + 9, &sinceSet, 4); std::memcpy(out + o + 13, &sinceUpd, 4);
        o += 17;
    }
    *outLen = o;
    return wo163::kROk;
}

uint8_t op_mark(uint8_t, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (len < 3 || cap < 1) return wo163::kRBadRequest;
    const uint8_t index = body[1];
    const uint8_t nl = body[2];
    if (len != 3u + nl) return wo163::kRBadRequest;
    *outLen = 1;
    if (!wo201vote::entry() || !g_origActive) { out[0] = 3; return wo163::kROk; }
    if (index == 255) { g_vote.on = false; c_clears.fetch_add(1); out[0] = 0; return wo163::kROk; }
    if (index >= g_snapN) { g_vote.on = false; out[0] = 2; return wo163::kROk; }
    g_vote = Vote{};
    g_vote.on = true; g_vote.index = index;
    std::strcpy(g_vote.text, g_snap[index].text);
    const size_t cn = nl < sizeof g_vote.name - 1 ? nl : sizeof g_vote.name - 1;
    std::memcpy(g_vote.name, body + 3, cn); g_vote.name[cn] = 0;
    c_marks.fetch_add(1);
    out[0] = g_snap[index].icon[0] ? 1 : 0;   // 1: the row has an icon of its own (the label path)
    return wo163::kROk;
}

// WO-207 A.4: the slot after the speak-manager getter is its get-or-create only when both read the same field: the getter
// "mov rax,[rbx+F]" and the maker "cmp qword ptr [rbx+F],0", each in its first 0x30 bytes (C_Actor, F = 0x210 on the build the
// research read). Anything else is never called.
bool make_matches_getter(void* getter, void* make) {
    uint8_t g[0x30]{}, m[0x30]{};
    if (!fault::guarded(g_readSite, [&] { std::memcpy(g, getter, sizeof g); std::memcpy(m, make, sizeof m); })) return false;
    int32_t field = -1;
    for (size_t i = 0; i + 7 <= sizeof g; ++i)
        if (g[i] == 0x48 && g[i + 1] == 0x8B && g[i + 2] == 0x83) { std::memcpy(&field, g + i + 3, 4); break; }
    if (field <= 0 || field > 0x2000) return false;
    for (size_t i = 0; i + 8 <= sizeof m; ++i) {
        int32_t f2 = 0;
        if (m[i] == 0x48 && m[i + 1] == 0x83 && m[i + 2] == 0xBB && m[i + 7] == 0x00) { std::memcpy(&f2, m + i + 3, 4); if (f2 == field) return true; }
    }
    return false;
}

uint8_t op_speak(uint8_t, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (len < 10 || cap < 1) return wo163::kRBadRequest;
    uint32_t eid = 0, durMs = 0;
    std::memcpy(&eid, body + 1, 4); std::memcpy(&durMs, body + 5, 4);
    const uint8_t pl = body[9];
    if (!pl || len < 10u + pl) return wo163::kRBadRequest;
    uint8_t flags = 0;
    char name[64] = "";
    if (len != 10u + pl) {   // WO-207 A.4: the tail
        if (len < 12u + pl) return wo163::kRBadRequest;
        flags = body[10 + pl];
        const uint8_t nl = body[11 + pl];
        if (len != 12u + pl + nl || nl >= sizeof name) return wo163::kRBadRequest;
        std::memcpy(name, body + 12 + pl, nl); name[nl] = 0;
    }
    *outLen = 1;
    using SpeakFn = void (*)(void* speakMan, void* params);
    SpeakFn speak = reinterpret_cast<SpeakFn>(wo201speech::speak_fn());
    if (!speak || !g_speakManSlot) { out[0] = 3; return wo163::kROk; }
    if (eid == 0 && (flags & 2) && name[0]) {   // the host's own NPC: found by its name here (a full walk, main thread; lines are few)
        void* e = npcdrive::entity_by_name(name);
        eid = e ? engine::entity_id(e) : 0;
        if (eid) c_speakByName.fetch_add(1); else c_speakNoName.fetch_add(1);
    }
    void* actor = eid ? motion::actor_of(eid) : nullptr;
    if (!actor) { c_speakNoActor.fetch_add(1); out[0] = 1; return wo163::kROk; }
    void* vt = nullptr; void* getMan = nullptr; void* man = nullptr;
    if (!rd(actor, 0, &vt) || !rd(vt, static_cast<ptrdiff_t>(g_speakManSlot), &getMan) || !getMan) { c_speakNoMan.fetch_add(1); out[0] = 2; return wo163::kROk; }
    using GetFn = void* (*)(void*);
    if (!fault::guarded(g_speakSite, [&] { man = reinterpret_cast<GetFn>(getMan)(actor); })) { c_speakNoMan.fetch_add(1); out[0] = 2; return wo163::kROk; }
    // WO-207 A.4: the listener's bodies (the partner's figure, the host's NPC copies) never spoke in a dialogue of this machine, so the
    // engine never made their speak manager; the engine's own get-or-create (the next slot) makes it the way its dialogue does
    if (!man && (flags & 1)) {
        void* make = nullptr;
        if (rd(vt, static_cast<ptrdiff_t>(g_speakManSlot + 8), &make) && make && make_matches_getter(getMan, make)) {
            if (fault::guarded(g_speakSite, [&] { man = reinterpret_cast<GetFn>(make)(actor); }) && man) c_speakMade.fetch_add(1);
        } else c_speakMakeRefused.fetch_add(1);
    }
    if (!man) { c_speakNoMan.fetch_add(1); out[0] = 2; return wo163::kROk; }
    char path[200];
    const size_t n = pl < sizeof path - 1 ? pl : sizeof path - 1;
    std::memcpy(path, body + 10, n); path[n] = 0;
    CryStr& ps = g_speakPath[g_speakNext]; g_speakNext = (g_speakNext + 1) % 16;
    ps.set(path);
    // S_SpeakParams as the engine builds it: +0 the voice file, +8 the text (empty: the HUD's subtitle is the listener's own), +0x10
    // the purpose, +0x19 = 1, +0x1B = 1 (a speaker the engine would not let speak still speaks: a puppet), +0x20 no dialogue instance,
    // +0x2C / +0x30 the duration, +0x34 -1.0
    alignas(8) uint8_t params[0x40]{};
    const char* p0 = ps.data; const char* p8 = g_emptyStr.data; const char* p10 = g_purposeStr.data;
    std::memcpy(params + 0x00, &p0, 8); std::memcpy(params + 0x08, &p8, 8); std::memcpy(params + 0x10, &p10, 8);
    params[0x19] = 1; params[0x1B] = 1;
    const float dur = durMs > 50 && durMs < 60000 ? static_cast<float>(durMs) / 1000.0f : 4.0f, minus1 = -1.0f;
    std::memcpy(params + 0x2C, &dur, 4); std::memcpy(params + 0x30, &dur, 4); std::memcpy(params + 0x34, &minus1, 4);
    if (!fault::guarded(g_speakSite, [&] { speak(man, params); })) { c_speakFault.fetch_add(1); out[0] = 4; return wo163::kROk; }
    c_spoken.fetch_add(1);
    out[0] = 0;
    return wo163::kROk;
}

int status(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "hud=%s slots=+0x%X/+0x%X marks=%u icons=%u labels=%u reflush=%u faults=%u clears=%u stale=%u active=%u "
                         "speak_slot=+0x%zX spoken=%u no_actor=%u no_man=%u speak_faults=%u made=%u make_refused=%u by_name=%u no_name=%u "
                         "timer_slots=+0x%X/+0x%X/+0x%X timer_sets=%u timer_updates=%u timer_clears=%u",
                         g_why, g_setSlot >= 0 ? g_setSlot * 8 : 0, g_activeSlot >= 0 ? g_activeSlot * 8 : 0, c_marks.load(), c_icons.load(),
                         c_labels.load(), c_reflush.load(), c_reflushFault.load(), c_clears.load(), c_stale.load(), c_active.load(),
                         g_speakManSlot, c_spoken.load(), c_speakNoActor.load(), c_speakNoMan.load(), c_speakFault.load(),
                         c_speakMade.load(), c_speakMakeRefused.load(), c_speakByName.load(), c_speakNoName.load(),
                         g_setTlSlot >= 0 ? g_setTlSlot * 8 : 0, g_updTlSlot >= 0 ? g_updTlSlot * 8 : 0, g_clearTlSlot >= 0 ? g_clearTlSlot * 8 : 0,
                         c_tlSets.load(), c_tlUpdates.load(), c_tlClears.load());
}

// The actor's speak-manager getter slot, read out of the engine's own speak callback (OnAudioStopFile: GetActor(eid), then
// "mov rcx,[rax]; mov rdx,[rcx+disp32]; mov rcx,rax; call rdx").
void resolve_speak() {
    g_emptyStr.set("");
    g_purposeStr.set("100");
    HMODULE em = GetModuleHandleA("EntityModule.dll");
    int n = 0;
    const uint8_t* fn = em ? anchor::function_by_string(em, "wh::entitymodule::C_ActorSpeakCallbackExtension::OnAudioStopFile", &n) : nullptr;
    if (!fn || n != 1) { logf("WO203-CONV voice: the speak callback not found (%d) -- the listener's lines fall back to the plain play", n); return; }
    uint8_t code[0x80]{};
    if (!fault::guarded(g_readSite, [&] { std::memcpy(code, fn, sizeof code); })) return;
    for (size_t i = 0; i + 13 <= sizeof code; ++i) {
        if (code[i] == 0x48 && code[i + 1] == 0x8B && code[i + 2] == 0x08 && code[i + 3] == 0x48 && code[i + 4] == 0x8B && code[i + 5] == 0x91 &&
            code[i + 10] == 0x48 && code[i + 11] == 0x8B && code[i + 12] == 0xC8) {
            int32_t d; std::memcpy(&d, code + i + 6, 4);
            if (d > 0x100 && d < 0x2000 && d % 8 == 0) { g_speakManSlot = static_cast<size_t>(d); break; }
        }
    }
    if (!g_speakManSlot) { logf("WO203-CONV voice: the speak-manager slot not found in the callback's code -- the plain play stays"); return; }
    logf("WO203-CONV voice resolver: the actor's speak manager slot +0x%zX -- %s the build the research read", g_speakManSlot,
         g_speakManSlot == 0x4D8 ? "the same as" : "DIFFERENT from (the found slot is used)");
}

// WO-207 A.3: each timer slot is the one after SetDialogSequences' whose function references its Flash name; a slot not found (or not
// writable) leaves that call alone and the listener's timer off.
void install_timer(HMODULE gm, void* const* vt) {
    struct T { const char* name; int* slot; void* hook; void** orig; };
    const T ts[] = {{"SetDialogTimeLimit", &g_setTlSlot, reinterpret_cast<void*>(&hk_set_tl), reinterpret_cast<void**>(&g_origSetTl)},
                    {"UpdateDialogTimeLimit", &g_updTlSlot, reinterpret_cast<void*>(&hk_upd_tl), reinterpret_cast<void**>(&g_origUpdTl)},
                    {"ClearDialogTimeLimit", &g_clearTlSlot, reinterpret_cast<void*>(&hk_clear_tl), reinterpret_cast<void**>(&g_origClearTl)}};
    for (const T& t : ts) {
        const char* name = anchor::find_cstring(gm, t.name);
        if (!name) { logf("WO207-TIMER the name %s not found -- that call is not mirrored", t.name); continue; }
        for (int s = g_setSlot + 1; s < g_setSlot + 24 && *t.slot < 0; ++s) {
            void* f = nullptr;
            if (!rd(vt, s * 8, &f) || !f) break;
            if (s != g_activeSlot && anchor::function_refs(gm, f, name)) *t.slot = s;
        }
        if (*t.slot < 0) { logf("WO207-TIMER %s's slot not found -- that call is not mirrored", t.name); continue; }
        void** at = const_cast<void**>(vt) + *t.slot;
        void* cur = nullptr;
        DWORD old = 0;
        if (!rd(at, 0, &cur) || !cur || !VirtualProtect(at, 8, PAGE_READWRITE, &old)) { logf("WO207-TIMER %s's slot not writable", t.name); *t.slot = -1; continue; }
        *t.orig = cur;
        InterlockedExchangePointer(at, t.hook);
        VirtualProtect(at, 8, old, &old);
    }
    logf("WO207-TIMER resolver: SetDialogTimeLimit +0x%X, UpdateDialogTimeLimit +0x%X, ClearDialogTimeLimit +0x%X -- %s the build the research read",
         g_setTlSlot * 8, g_updTlSlot * 8, g_clearTlSlot * 8,
         (g_setTlSlot * 8 == 0x28 && g_updTlSlot * 8 == 0x30 && g_clearTlSlot * 8 == 0x38) ? "the same as" : "DIFFERENT from (the found slots are used)");
}

void install() {
    resolve_speak();
    g_iconStr.set(kVoteIcon);
    HMODULE gm = GetModuleHandleA("GUIModule.dll");
    void* set = wo201vote::entry();
    if (!gm || !set) { g_why = "GUIModule or the HUD's flush not resolved"; logf("WO203-CONV not armed: %s", g_why); return; }
    const char* name = anchor::find_cstring(gm, kActiveName);
    if (!name) { g_why = "the highlight function's name not found"; logf("WO203-CONV not armed: %s", g_why); return; }
    // the interface's vtable: the one of the class's vtables that holds SetDialogSequences
    void* const* vt = nullptr;
    for (uint32_t off = 0; off <= 0x200 && !vt; off += 8) {
        void* const* t = anchor::find_vftable(gm, kHudRtti, off);
        if (!t) continue;
        for (int s = 0; s < 48; ++s) {
            void* f = nullptr;
            if (!rd(t, s * 8, &f)) break;
            if (f == set) { vt = t; g_setSlot = s; break; }
        }
    }
    if (!vt) { g_why = "the HUD interface's vtable not found"; logf("WO203-CONV not armed: %s", g_why); return; }
    for (int s = g_setSlot + 1; s < g_setSlot + 24; ++s) {
        void* f = nullptr;
        if (!rd(vt, s * 8, &f) || !f) break;
        if (anchor::function_refs(gm, f, name)) { g_activeSlot = s; break; }
    }
    if (g_activeSlot < 0) { g_why = "the highlight slot not found"; logf("WO203-CONV not armed: %s", g_why); return; }
    void** at = const_cast<void**>(vt) + g_activeSlot;
    void* cur = nullptr;
    DWORD old = 0;
    if (!rd(at, 0, &cur) || !cur || !VirtualProtect(at, 8, PAGE_READWRITE, &old)) { g_why = "the highlight slot not writable"; return; }
    g_origActive = reinterpret_cast<ActiveFn>(cur);
    InterlockedExchangePointer(at, reinterpret_cast<void*>(&hk_active));
    VirtualProtect(at, 8, old, &old);
    wo201vote::set_before_flush(&before_flush);
    g_why = "armed";
    install_timer(gm, vt);
    logf("WO203-CONV resolver: the HUD interface's SetDialogSequences slot +0x%X, SetActiveSequenceChanged slot +0x%X -- %s the build the research read",
         g_setSlot * 8, g_activeSlot * 8, (g_setSlot * 8 == 0x10 && g_activeSlot * 8 == 0x18) ? "the same as" : "DIFFERENT from (the found slots are used)");
}

const bool kReg = wo203::reg_install("conv", &install) && wo203::reg_status("conv", &status) &&
                  wo203::reg_op(91, &op_poll) && wo203::reg_op(92, &op_mark) && wo203::reg_op(93, &op_speak);

} // namespace

} // namespace kcdmp::wo203::conv
