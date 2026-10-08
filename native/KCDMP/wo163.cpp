// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-163: the native half's pipe ops (wo163.h).
#include "wo163.h"
#include "engine.h"
#include "hits.h"
#include "log.h"

#include <atomic>
#include <cstdio>
#include <cstring>
#include <string>

namespace kcdmp::wo163 {

namespace {

std::atomic<uint32_t> c_asked{0}, c_answeredHostile{0}, c_answeredNot{0}, c_unanswered{0};

uint32_t player_eid() {
    void* e = engine::entity_by_id(0x7777);
    return e ? engine::entity_id(e) : 0;
}

// An authored entity name: letters, digits, '_', '-', '.', never a control or Lua-shaped character.
bool name_ok(const char* s, size_t n) {
    if (n == 0 || n > 63) return false;
    for (size_t i = 0; i < n; ++i) {
        const char c = s[i];
        const bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.';
        if (!ok) return false;
    }
    return true;
}

} // namespace

int status_text(char* out, int n) {
    return std::snprintf(out, n, "skirmish_relation=%s asked=%u hostile=%u not_hostile=%u unanswered=%u",
                         hits::skirmish_relation_armed() ? "armed" : "off", c_asked.load(), c_answeredHostile.load(), c_answeredNot.load(), c_unanswered.load());
}

uint8_t handle(const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (!len) return kRBadRequest;
    switch (body[0]) {
        case kOpSkirmishHostile: {
            if (len < 2 || cap < 2) return kRBadRequest;
            const size_t n = body[1];
            if (len != 2 + n || !name_ok(reinterpret_cast<const char*>(body + 2), n)) return kRBadRequest;
            const std::string name(reinterpret_cast<const char*>(body + 2), n);
            c_asked.fetch_add(1);
            // every pointer is looked up here, in this frame, and used here
            const uint32_t veid = hits::eid_of_name(name.c_str());
            if (!veid) { c_unanswered.fetch_add(1); return kRNoActor; }
            void* hostSoul = hits::soul_of_eid(player_eid());
            void* victimSoul = hits::soul_of_eid(veid);
            if (!hostSoul || !victimSoul) { c_unanswered.fetch_add(1); return kRNoSoul; }
            bool answered = false, hostile = false;
            if (!hits::skirmish_hostile(hostSoul, victimSoul, &answered, &hostile) || !answered) { c_unanswered.fetch_add(1); return kRFailed; }
            (hostile ? c_answeredHostile : c_answeredNot).fetch_add(1);
            logf("WO163-RELATION host vs npc=%s -> %s (the engine's own skirmish relation test, read-only)", name.c_str(),
                 hostile ? "hostile opponents in one skirmish" : "not hostile opponents in one skirmish");
            out[0] = 1; out[1] = hostile ? 1 : 0;
            *outLen = 2;
            return kROk;
        }
        case kOpStatus: {
            const int n = status_text(reinterpret_cast<char*>(out), static_cast<int>(cap));
            *outLen = (n > 0 && static_cast<size_t>(n) < cap) ? static_cast<size_t>(n) : 0;
            return kROk;
        }
        default:
            return kRBadRequest;
    }
}

} // namespace kcdmp::wo163
