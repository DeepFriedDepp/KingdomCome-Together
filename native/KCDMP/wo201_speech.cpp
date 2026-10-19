// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-201 Act V F1 speech_share: the partner hears the conversations held near him, from the right bodies.
//   * Capture (both machines): every line the engine voices goes through C_ActorSpeakMan::Speak (EntityModule; found by its own trace
//     name), with the voice file the engine already resolved. The detour copies the line (the speaker's entity, the file, the text, the
//     duration, whether a dialogue instance said it) into a small queue and calls the original; the agent polls the queue (op 83) and
//     sends what its partner should hear.
//   * Play (the partner): the file on the right body's own audio object -- IEntityAudioProxy::PlayFile, the call the speak manager
//     makes (op 84), positional with the game's own attenuation. A played line never reaches Speak, so it is never captured back.
// Every slot and field is read out of the engine's code: PlaySoundInternal (its "Unable to create sound" text) gives the entity's
// audio-proxy getter (mov edx,3; call [rax+slot]) and PlayFile's slot (mov r8,[rcx+slot]; call r8); Speak's prologue is checked
// against the hook-prologue registry before its detour goes in (lazily: the first poll with the switch on).   WO201-SPEECH.
#include "wo201.h"
#include "wo163.h"
#include "anchors.h"
#include "engine.h"
#include "fault_guard.h"
#include "log.h"
#include "inline_hook.h"
#include "hook_prologues.h"
#include "npcstate.h"
#include <windows.h>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <vector>

namespace kcdmp::wo201speech {

namespace {

constexpr char kSpeakTrace[] = "wh::entitymodule::C_ActorSpeakMan::Speak";
constexpr char kPlayText[] = "Unable to create sound %s on entity %s";
constexpr uint32_t kPlayerEntity = 0x7777;

const char* g_why = "not resolved";
void* g_speak = nullptr;
size_t g_proxySlot = 0, g_playSlot = 0;
int g_actorOff = -1;      // C_ActorSpeakMan's actor field (PlaySoundInternal: mov rcx,[rbx+disp8])
int g_actorEidOff = -1;   // the actor's entity id (PlaySoundInternal: mov edx,[rcx+disp8])
using SpeakFn = void (*)(void* self, void* params);
SpeakFn g_speakOrig = nullptr;
bool g_hooked = false;
std::atomic<bool> g_on{false};
std::atomic<uint32_t> c_captured{0}, c_dropped{0}, c_played{0}, c_playFailed{0}, c_noProxy{0};

struct Line { uint32_t eid; float dur; uint8_t flags; char path[192]; char text[160]; uint64_t at; };
std::mutex g_lock;
std::vector<Line> g_q;   // under g_lock, at most 24

template <class T> bool rd(const void* base, ptrdiff_t off, T* out) {
    if (!base) return false;
    KCDMP_FAULT_READ(site, "wo201speech::read");
    if (!fault::enabled(site)) return false;
    const uintptr_t a = reinterpret_cast<uintptr_t>(base) + static_cast<uintptr_t>(off);
    if (!fault::plausible_address(a)) return false;
    return fault::guarded(site, [&] { std::memcpy(out, reinterpret_cast<const void*>(a), sizeof(T)); });
}
bool rd_bytes(const void* p, uint8_t* out, size_t n) {
    KCDMP_FAULT_READ(site, "wo201speech::read_bytes");
    if (!p || !fault::enabled(site) || !fault::plausible_address(reinterpret_cast<uintptr_t>(p))) return false;
    return fault::guarded(site, [&] { std::memcpy(out, p, n); });
}
bool rd_str(const char* p, char* out, size_t n) {
    out[0] = 0;
    if (!p) return false;
    KCDMP_FAULT_READ(site, "wo201speech::read_str");
    if (!fault::enabled(site) || !fault::plausible_address(reinterpret_cast<uintptr_t>(p))) return false;
    return fault::guarded(site, [&] {
        size_t i = 0;
        for (; i + 1 < n && p[i]; ++i) out[i] = p[i];
        out[i] = 0;
    });
}
void* vslot(const void* obj, size_t off) {
    void* vt = nullptr; void* fn = nullptr;
    return obj && rd(obj, 0, &vt) && vt && rd(vt, static_cast<ptrdiff_t>(off), &fn) ? fn : nullptr;
}
bool ends_ogg(const char* s) {
    const size_t n = std::strlen(s);
    return n > 4 && (s[n - 4] == '.') && (s[n - 3] == 'o' || s[n - 3] == 'O') && (s[n - 2] == 'g' || s[n - 2] == 'G') && (s[n - 1] == 'g' || s[n - 1] == 'G');
}

// S_SpeakParams (DialogInstance's and wh_snd_playfile's builders agree): +0 the voice file, +8 the text, +0x20 the dialogue instance
// (null for anything else), +0x2C the duration. A line whose +0 is not an .ogg path is not taken (a different layout fails closed).
void speak_detour(void* self, void* params) {
    if (g_on.load()) {
        Line l{};
        const char* path = nullptr; const char* text = nullptr; void* dlg = nullptr; float dur = 0; void* actor = nullptr;
        if (rd(params, 0, &path) && rd_str(path, l.path, sizeof l.path) && ends_ogg(l.path) && rd(self, g_actorOff, &actor) && actor &&
            rd(actor, g_actorEidOff, &l.eid) && l.eid) {
            if (rd(params, 8, &text)) rd_str(text, l.text, sizeof l.text);
            rd(params, 0x20, &dlg);
            if (!rd(params, 0x2C, &dur) || !(dur > 0.05f && dur < 60.0f)) dur = 4.0f;
            l.dur = dur; l.flags = dlg ? 1 : 0; l.at = GetTickCount64();
            std::lock_guard<std::mutex> lk(g_lock);
            if (g_q.size() >= 24) { g_q.erase(g_q.begin()); c_dropped.fetch_add(1, std::memory_order_relaxed); }
            g_q.push_back(l);
            c_captured.fetch_add(1, std::memory_order_relaxed);
        }
    }
    g_speakOrig(self, params);
}

bool arm(const char** why) {
    if (g_hooked) return true;
    if (!g_speak) { *why = g_why; return false; }
    void* orig = nullptr;
    if (!inlinehook::install_detour(g_speak, hookpro::kActorSpeak, sizeof hookpro::kActorSpeak, reinterpret_cast<void*>(&speak_detour), &orig, why))
        return false;
    g_speakOrig = reinterpret_cast<SpeakFn>(orig);
    g_hooked = true;
    logf("WO201-SPEECH armed: C_ActorSpeakMan::Speak detoured (the lines the engine voices are shared with a partner near them)");
    return true;
}

} // namespace

void install() {
    HMODULE em = GetModuleHandleA("EntityModule.dll");
    if (!em) { g_why = "EntityModule.dll not loaded"; return; }
    int n = 0;
    const uint8_t* sp = anchor::function_by_string(em, kSpeakTrace, &n);
    if (!sp || n != 1) { g_why = "Speak's trace name is referenced by no (or more than one) function"; return; }
    uint8_t pro[sizeof hookpro::kActorSpeak]{};
    if (!rd_bytes(sp, pro, sizeof pro) || std::memcmp(pro, hookpro::kActorSpeak, sizeof pro) != 0) { g_why = "Speak's prologue is not the registry's"; return; }
    const uint8_t* ps = anchor::function_by_string(em, kPlayText, &n);
    if (!ps || n != 1) { g_why = "PlaySoundInternal's text is referenced by no (or more than one) function"; return; }
    uint8_t code[0x90]{};
    if (!rd_bytes(ps, code, sizeof code)) { g_why = "PlaySoundInternal unreadable"; return; }
    // its first relative call after the JMC probe is the proxy getter; then "mov edx,[rcx+disp8]" (the actor's entity id) and
    // "mov r8,[rcx+disp32]; ...; call r8" (PlayFile's slot)
    const uint8_t* getter = nullptr;
    int calls = 0;
    for (size_t i = 0; i + 5 <= sizeof code; ++i) {
        if (code[i] != 0xE8) continue;
        if (++calls == 2) { int32_t rel; std::memcpy(&rel, code + i + 1, 4); getter = ps + i + 5 + rel; break; }
    }
    for (size_t i = 0; i + 4 <= sizeof code && g_actorOff < 0; ++i)
        if (code[i] == 0x48 && code[i + 1] == 0x8B && code[i + 2] == 0x4B) g_actorOff = code[i + 3];
    for (size_t i = 0; i + 3 <= sizeof code && g_actorEidOff < 0; ++i)
        if (code[i] == 0x8B && code[i + 1] == 0x51) g_actorEidOff = code[i + 2];
    for (size_t i = 0; i + 7 <= sizeof code && !g_playSlot; ++i)
        if (code[i] == 0x4C && code[i + 1] == 0x8B && code[i + 2] == 0x81) { int32_t d; std::memcpy(&d, code + i + 3, 4); if (d > 0 && d < 0x400 && d % 8 == 0) g_playSlot = static_cast<size_t>(d); }
    uint8_t gc[0x40]{};
    if (getter && rd_bytes(getter, gc, sizeof gc))
        for (size_t i = 0; i + 5 <= sizeof gc && !g_proxySlot; ++i)
            if (gc[i] == 0xBA && gc[i + 1] == 3 && gc[i + 2] == 0 && gc[i + 3] == 0 && gc[i + 4] == 0)
                for (size_t j = i + 5; j + 6 <= sizeof gc && j < i + 20; ++j)
                    if (gc[j] == 0xFF && gc[j + 1] == 0x90) { int32_t d; std::memcpy(&d, gc + j + 2, 4); if (d > 0 && d < 0x800 && d % 8 == 0) { g_proxySlot = static_cast<size_t>(d); break; } }
    if (!g_proxySlot || !g_playSlot || g_actorEidOff < 0 || g_actorOff < 0) { g_why = "the proxy getter, PlayFile's slot or the actor's entity id not found in PlaySoundInternal"; return; }
    g_speak = const_cast<uint8_t*>(sp);
    g_why = "resolved";
    logf("WO201-SPEECH resolver: proxy getter slot +0x%zX, PlayFile slot +0x%zX, actor entity id +0x%X -- %s the build the research read",
         g_proxySlot, g_playSlot, g_actorEidOff, (g_proxySlot == 0x250 && g_playSlot == 0xE0 && g_actorEidOff == 0x30) ? "the same as" : "DIFFERENT from (the resolved values are used)");
}

// op 83 Poll [on:1][figureEid:4] -> [n:1] per line [eid:4][dur:f32][flags:1][dist:f32][age_ms:2][pl:1][path][tl:1][text][nl:1][name]
// dist: the speaker to the figure (the partner's body on this machine), -1 when unknown. Lines over the out buffer stay queued.
// op 84 Play [eid:4][pl:1][path]([nl:1][name]: eid 0 = the body of that name) -> [ok:1]
uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (op == 83) {
        if (len != 6 || cap < 1) return wo163::kRBadRequest;
        const bool on = body[1] != 0;
        uint32_t fe = 0; std::memcpy(&fe, body + 2, 4);
        if (on && !g_hooked) {
            const char* why = "";
            if (!arm(&why)) { logf("WO201-SPEECH NOT armed (%s) -- nobody hears the partner's conversations", why); return wo163::kRFailed; }
        }
        g_on = on;
        float fp[3]{}; void* fig = fe ? engine::entity_by_id(fe) : nullptr;
        const bool haveFig = fig && engine::entity_world_pos(fig, fp);
        std::vector<Line> take;
        {
            std::lock_guard<std::mutex> lk(g_lock);
            take.swap(g_q);
        }
        size_t o = 1; uint8_t k = 0;
        const uint64_t now = GetTickCount64();
        for (size_t i = 0; i < take.size(); ++i) {
            const Line& l = take[i];
            const size_t pl = std::strlen(l.path), tl = std::strlen(l.text);
            char nm[64] = "";
            void* se = engine::entity_by_id(l.eid);
            if (se) rd_str(engine::entity_name(se), nm, sizeof nm);
            const size_t nl = std::strlen(nm);
            if (o + 19 + pl + tl + nl > cap) {   // the rest stays for the next poll
                std::lock_guard<std::mutex> lk(g_lock);
                g_q.insert(g_q.begin(), take.begin() + static_cast<ptrdiff_t>(i), take.end());
                break;
            }
            float d = -1.0f;
            float sp[3];
            if (haveFig && se && engine::entity_world_pos(se, sp)) d = std::sqrt((sp[0] - fp[0]) * (sp[0] - fp[0]) + (sp[1] - fp[1]) * (sp[1] - fp[1]) + (sp[2] - fp[2]) * (sp[2] - fp[2]));
            const uint16_t age = static_cast<uint16_t>(now - l.at > 60000 ? 60000 : now - l.at);
            std::memcpy(out + o, &l.eid, 4); std::memcpy(out + o + 4, &l.dur, 4); out[o + 8] = l.flags; std::memcpy(out + o + 9, &d, 4);
            std::memcpy(out + o + 13, &age, 2); out[o + 15] = static_cast<uint8_t>(pl); std::memcpy(out + o + 16, l.path, pl);
            out[o + 16 + pl] = static_cast<uint8_t>(tl); std::memcpy(out + o + 17 + pl, l.text, tl);
            out[o + 17 + pl + tl] = static_cast<uint8_t>(nl); std::memcpy(out + o + 18 + pl + tl, nm, nl);
            o += 18 + pl + tl + nl; ++k;
        }
        out[0] = k; *outLen = o;
        return wo163::kROk;
    }
    if (op == 84) {
        if (len < 7 || cap < 1) return wo163::kRBadRequest;
        uint32_t eid = 0; std::memcpy(&eid, body + 1, 4);
        const size_t pl = body[5];
        if (len < 6 + pl || pl < 5 || pl > 190) return wo163::kRBadRequest;
        char path[192]; std::memcpy(path, body + 6, pl); path[pl] = 0;
        if (!eid && len > 7 + pl) {
            const size_t nl = body[6 + pl];
            if (len != 7 + pl + nl || nl == 0 || nl > 63) return wo163::kRBadRequest;
            char nm[64]; std::memcpy(nm, body + 7 + pl, nl); nm[nl] = 0;
            void* ne = wo141::x::entity_named(nm);
            eid = ne ? engine::entity_id(ne) : 0;
        }
        if (!ends_ogg(path) || std::strstr(path, "..")) return wo163::kRBadRequest;   // a dialog voice file, nothing else
        if (!g_proxySlot || !g_playSlot) return wo163::kRFailed;
        void* e = eid && eid != kPlayerEntity ? engine::entity_by_id(eid) : nullptr;
        if (!e) return wo163::kRNoActor;
        void* get = vslot(e, g_proxySlot);
        void* proxy = nullptr;
        KCDMP_FAULT_CALL(gsite, "wo201speech::GetProxy");
        if (!get || !fault::guarded(gsite, [&] { proxy = reinterpret_cast<void* (*)(void*, int)>(get)(e, 3); }) || !proxy) {
            c_noProxy.fetch_add(1); out[0] = 0; *outLen = 1; return wo163::kROk;
        }
        void* play = vslot(proxy, g_playSlot);
        struct CallbackInfo { void* obj; uint64_t eid; void* user; uint32_t flags; } cb{nullptr, eid, nullptr, 0};
        KCDMP_FAULT_CALL(psite, "wo201speech::PlayFile");
        const bool ok = play && fault::guarded(psite, [&] {
            reinterpret_cast<void (*)(void*, const char*, float, uint32_t, const CallbackInfo&)>(play)(proxy, path, 0.0f, 1u, cb);
        });
        (ok ? c_played : c_playFailed).fetch_add(1);
        out[0] = ok ? 1 : 0; *outLen = 1;
        return wo163::kROk;
    }
    return wo163::kRBadRequest;
}

int status_text(char* out, int n) {
    return std::snprintf(out, static_cast<size_t>(n), "speech=%s on=%d captured=%u dropped=%u played=%u play_failed=%u no_proxy=%u", g_why,
                         g_on.load() ? 1 : 0, c_captured.load(), c_dropped.load(), c_played.load(), c_playFailed.load(), c_noProxy.load());
}

} // namespace kcdmp::wo201speech
