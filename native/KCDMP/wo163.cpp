// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-163: the native half's pipe ops (wo163.h).
#include "wo163.h"
#include "engine.h"
#include "hits.h"
#include "log.h"
#include "motion.h"
#include "wo165.h"
#include "wo166.h"

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
            if (!veid) { c_unanswered.fetch_add(1); logf("WO163-RELATION host vs npc=%s NOT answered: no such entity", name.c_str()); return kRNoActor; }
            void* hostSoul = hits::soul_of_eid(player_eid());
            void* victimSoul = hits::soul_of_eid(veid);
            if (!hostSoul || !victimSoul) {
                c_unanswered.fetch_add(1);
                logf("WO163-RELATION host vs npc=%s NOT answered: no soul (host %s, npc %s)", name.c_str(), hostSoul ? "yes" : "no", victimSoul ? "yes" : "no");
                return kRNoSoul;
            }
            bool answered = false, hostile = false;
            const char* why = "";
            if (!hits::skirmish_hostile(hostSoul, victimSoul, &answered, &hostile, &why) || !answered) {
                c_unanswered.fetch_add(1);
                logf("WO163-RELATION host vs npc=%s NOT answered: %s", name.c_str(), why);
                return kRFailed;
            }
            (hostile ? c_answeredHostile : c_answeredNot).fetch_add(1);
            logf("WO163-RELATION host vs npc=%s -> %s (the engine's own skirmish relation test, read-only)", name.c_str(),
                 hostile ? "hostile opponents in one skirmish" : "not hostile opponents in one skirmish");
            out[0] = 1; out[1] = hostile ? 1 : 0;
            *outLen = 2;
            return kROk;
        }
        case kOpModelRead: {
            if (len < 2 || cap < 190) return kRBadRequest;
            const size_t n0 = body[1];
            if (len != 2 + n0 || (n0 && !name_ok(reinterpret_cast<const char*>(body + 2), n0))) return kRBadRequest;
            uint32_t eid = 0;
            if (n0 == 0) eid = player_eid();
            else { const std::string nm(reinterpret_cast<const char*>(body + 2), n0); eid = hits::eid_of_name(nm.c_str()); }
            motion::ModelRead m{};
            if (!eid || !motion::read_model(eid, &m)) return kRNoActor;
            const unsigned v = m.valid;
            const int n = std::snprintf(reinterpret_cast<char*>(out), cap,
                "ca=%u pca=%u model=%u state=%d(0x%X)%s gz=%d%s bz=%d%s bh=%d%s bm=%d%s pb=%u%s az=%d%s at=%d%s as=%.3f%s ah=%d%s cm=%u%s opp=0x%X%s",
                m.hasCa, m.isPlayerCa, m.hasModel, m.state, static_cast<unsigned>(m.state), (v & motion::kMvState) ? "" : "!",
                m.guardZone, (v & motion::kMvGuardZone) ? "" : "!", m.blockZone, (v & motion::kMvBlockZone) ? "" : "!",
                m.blockHand, (v & motion::kMvBlockHand) ? "" : "!", m.blockMode, (v & motion::kMvBlockMode) ? "" : "!",
                m.perfectBlock, (v & motion::kMvPerfect) ? "" : "!", m.atkZone, (v & motion::kMvAtkZone) ? "" : "!",
                m.atkType, (v & motion::kMvAtkType) ? "" : "!", m.atkStrength, (v & motion::kMvAtkStrength) ? "" : "!",
                m.atkHand, (v & motion::kMvAtkHand) ? "" : "!", m.combatMode, (v & motion::kMvCombatMode) ? "" : "!",
                m.opponentEid, m.opponentIsPlayer ? "(me)" : "");
            *outLen = (n > 0 && static_cast<size_t>(n) < cap) ? static_cast<size_t>(n) : 0;
            return kROk;
        }
        case kOpSkirmishPair: {
            if (len < 4 || cap < 1) return kRBadRequest;
            const bool on = body[1] != 0;
            const uint8_t ovr = body[2];
            const size_t n0 = body[3];
            if (len != 4 + n0 || (on && n0 == 0) || (n0 && !name_ok(reinterpret_cast<const char*>(body + 4), n0))) return kRBadRequest;
            uint32_t eid = 0;
            if (n0) { const std::string nm(reinterpret_cast<const char*>(body + 4), n0); eid = hits::eid_of_name(nm.c_str()); }
            if (on && !eid) return kRNoActor;
            const uint32_t peid = player_eid();
            void* hostSoul = peid ? hits::soul_of_eid(peid) : nullptr;
            void* npcSoul = eid ? hits::soul_of_eid(eid) : nullptr;
            if (!hostSoul || (on && !npcSoul)) return kRNoSoul;
            uint64_t rv = 0;
            const bool done = on ? hits::skirmish_add(hostSoul, npcSoul, ovr, &rv) : hits::skirmish_remove(hostSoul, &rv);
            logf("WO163-PAIR host %s npc=0x%X override=%u -> %s", on ? "joins the skirmish of" : "leaves its skirmish; was against", eid, ovr, done ? "done" : "FAILED");
            out[0] = done ? 1 : 0;
            *outLen = 1;
            return done ? kROk : kRFailed;
        }
        case kOpReplay: {   // WO-165
            if (len < 10 || cap < 120) return kRBadRequest;
            wo165::Request rq{};
            rq.flags = body[1];
            rq.fields.type = static_cast<int8_t>(body[2]);
            rq.fields.zone = static_cast<int8_t>(body[3]);
            rq.fields.hand = static_cast<int8_t>(body[4]);
            std::memcpy(&rq.fields.strength, body + 5, 4);
            if (!(rq.fields.strength >= 0.0f && rq.fields.strength <= 4.0f)) return kRBadRequest;   // NaN refused too
            const size_t an = body[9];
            if (len < 10 + an + 1 || !name_ok(reinterpret_cast<const char*>(body + 10), an)) return kRBadRequest;
            const size_t vn = body[10 + an];
            if (len != 11 + an + vn || (vn && !name_ok(reinterpret_cast<const char*>(body + 11 + an), vn))) return kRBadRequest;
            const std::string an_s(reinterpret_cast<const char*>(body + 10), an);
            rq.attackerEid = hits::eid_of_name(an_s.c_str());
            if (!rq.attackerEid) return kRNoActor;
            if (vn) {
                const std::string vn_s(reinterpret_cast<const char*>(body + 11 + an), vn);
                rq.victimEid = hits::eid_of_name(vn_s.c_str());
                if (!rq.victimEid) return kRNoActor;
            }
            const wo165::Result r = wo165::replay(rq);
            int n = 0;
            if (r.reason != wo165::kOk)
                n = std::snprintf(reinterpret_cast<char*>(out), cap, "refused=%s", wo165::reason_name(r.reason));
            else
                n = std::snprintf(reinterpret_cast<char*>(out), cap, "seq=%u engine=%s returned=%d core=%s blk=%u pb=%u dmgflag=%u broken=%u zm=%u vstate=0x%X opp_me=%u",
                                  r.seq, wo165::outcome_name(r.outcome), r.returned ? 1 : 0, r.seen ? "ran" : "not-run", r.flags[0], r.flags[3], r.flags[2],
                                  r.recBroken, r.recZoneMismatch, static_cast<unsigned>(r.victimModel.state), r.victimModel.opponentIsPlayer);
            *outLen = (n > 0 && static_cast<size_t>(n) < cap) ? static_cast<size_t>(n) : 0;
            return r.reason == wo165::kOk ? kROk : r.reason == wo165::kFault ? kRFailed : kRNoActor;
        }
        case kOpReplayDamage: {   // WO-165
            if (len != 5 || cap < 10) return kRBadRequest;
            uint32_t seq = 0;
            std::memcpy(&seq, body + 1, 4);
            const wo165::Damage d = wo165::damage_of(seq);
            out[0] = d.state;
            std::memcpy(out + 1, &d.health, 4);
            std::memcpy(out + 5, &d.stamina, 4);
            out[9] = d.victimLive ? 1 : 0;
            *outLen = 10;
            return kROk;
        }
        case kOpHostLock: {   // WO-165 C1
            if (len != 2 || cap < 1) return kRBadRequest;
            wo165::set_host_lock(body[1] != 0);
            out[0] = wo165::host_lock() ? 1 : 0;
            *outLen = 1;
            return kROk;
        }
        case kOpReplayStatus: {   // WO-165
            const int n = wo165::status_text(reinterpret_cast<char*>(out), static_cast<int>(cap));
            *outLen = (n > 0 && static_cast<size_t>(n) < cap) ? static_cast<size_t>(n) : 0;
            return kROk;
        }
        case kOpStatus: {
            const int n = status_text(reinterpret_cast<char*>(out), static_cast<int>(cap));
            *outLen = (n > 0 && static_cast<size_t>(n) < cap) ? static_cast<size_t>(n) : 0;
            return kROk;
        }
        case kcdmp::wo166::kOpStrike:   // WO-166 C3
        case kcdmp::wo166::kOpStatus:
        case kcdmp::wo166::kOpConfig:
            return kcdmp::wo166::handle(body[0], body, len, out, cap, outLen);
        default:
            return kRBadRequest;
    }
}

} // namespace kcdmp::wo163
