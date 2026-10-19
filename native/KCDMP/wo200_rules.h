// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-200: the pure rules of the two gates (wo200.cpp). Engine-free: native/tests/wo200_tests.cpp pins them.
//
// The standing rule: actions go to the authority; values come back. The host's world decides. The joiner's game shows, and asks; it never
// decides. These rules are where the native half applies it: which out-port pulses the joiner keeps quiet or sends as actions (Gate 1, the
// tap), and which quest nodes do not run on which machine (Gate 2, the node gate).
#pragma once
#include <cstdint>
#include <cstring>

namespace kcdmp::wo200rules {

constexpr uint8_t kRoleUnknown = 0, kRoleHost = 1, kRoleJoiner = 2;

// The gate's class table: what a node IS, by its RTTI class (and a Function node's method).
enum class Kind : uint8_t {
    None = 0,
    Dialogue,         // C_DialogueNode, C_DialogueWrapper: a conversation's own node (its out-ports are the conversation's consequences)
    DialogueStarter,  // C_ForcedDialogueNode, C_FaderDialogueNode: a follow-on player dialogue a consequence starts
    Clock,            // C_Timer, C_TimeOfDayWatch: a clock that decides by itself
    Waiter,           // C_SceneFinishedWaiter: a consequence that arrives later (the departure's OnFinished, 48 s on)
    Behaviour,        // C_EnableBehavior (duels, tours, skirmishes, an NPC urging a dialogue)
    EntityContext,    // C_SetEntityContext, C_SetEntityContextPreset
    SchedulerLink,    // C_SchedulerLinkActivation
    Interrupt,        // C_AddInterruptConceptNode: NPCs_TeleportIngame / OnHorse on NPCs, PlayerAction_* on the player
    Function,         // C_Function / C_MemberFunction / C_EventFunction / C_EventMemberFunction: the method decides (Method)
    View,             // C_FocusCameraNode, C_DisplayTutorial, C_PlayTrackView: one player's own view
    RewardPlayer,     // C_AddReward, C_PlayerOutfitOverride: always the player
    BodyEffect,       // C_BuffEffect, C_ClothingPresetOverride, C_WeaponPresetOverride: whose body is the soul input's
    Count
};

const char* kind_name(Kind k);

// A Function node's method, by the last "::" segment of its MethodName.
enum class Method : uint8_t {
    Other = 0,
    Monologue,     // RequestMonologue: a bark or thought of a soul (Henry's own, 886 of 1,108)
    PlayerReward,  // CreatePlayerReward (rewards, createmoney_player), EquipPlayersItem, UnequipPlayersItem: always the player
    AddBuff,       // AddBuff: a buff on whose body the soul input names
    SetState,      // SetState (HealTo, set health): a soul's health / stamina / ... written
    Damage,        // TakeDamage (KillNpc), Revive: a soul's life
    Items,         // CreateItems, MoveItems (inventories: money when the library around it is a money one)
};

inline const char* last_segment(const char* name) {
    if (!name) return "";
    const char* best = name;
    for (const char* p = name; *p; ++p)
        if (p[0] == ':' && p[1] == ':') best = p + 2;
    return best;
}

inline Method method_kind(const char* methodName) {
    const char* s = last_segment(methodName);
    if (!std::strcmp(s, "RequestMonologue")) return Method::Monologue;
    if (!std::strcmp(s, "CreatePlayerReward") || !std::strcmp(s, "EquipPlayersItem") || !std::strcmp(s, "UnequipPlayersItem"))
        return Method::PlayerReward;
    if (!std::strcmp(s, "AddBuff")) return Method::AddBuff;
    if (!std::strcmp(s, "SetState")) return Method::SetState;
    if (!std::strcmp(s, "TakeDamage") || !std::strcmp(s, "Revive")) return Method::Damage;
    if (!std::strcmp(s, "CreateItems") || !std::strcmp(s, "MoveItems")) return Method::Items;
    return Method::Other;
}

// Who a node's soul inputs name: the local player, someone else (a copy, the partner's figure, an NPC), or nothing readable.
enum class Target : uint8_t { Unknown = 0, Player = 1, Other = 2 };

enum class Verdict : uint8_t {
    Run = 0,
    PuppetRefused,        // I.5: the joiner's graph runs no quest behaviour on a puppet (a host copy or the partner's figure)
    HostDialogue,         // I.2: in a replay's closure the host starts no follow-on player dialogue, no monologue, no urged dialogue
    HostView,             // I.2: in a replay's closure the host shows no focus camera, tutorial or track view
    TalkerOnlyHost,       // I.3: in a replay's closure the host's Henry gets no reward, money or body effect (the talker got it)
    TalkerOnlyJoiner,     // I.3: in a mirror of the host's own conversation the joiner's Henry gets none of these, nor its view
    JoinerStoryBark,      // WO-201 B7: the joiner's own quest logic starts no player monologue, dialogue, urged dialogue or focus camera
    Count
};

const char* verdict_name(Verdict v);

struct Cfg {
    bool joined = false;       // the joiner is in the host's world (role joiner only)
    bool puppets = true;       // joiner_no_puppet_quests
    bool hostClosure = true;   // s1_replay (the host's I.2 rules inside a replay's closure)
    bool route = true;         // s1_route (talker-only, both machines)
    bool storyBarks = true;    // WO-201 B7 joiner_no_story_barks (gate bit 8)
};

// Does the decision for this node need its soul inputs read? (Only then does the caller read them: a read runs the node's data inputs.)
inline bool needs_target(uint8_t role, Kind k, Method m, bool inClosure, bool mirrorTalker, const Cfg& c) {
    const bool soulKind = k == Kind::BodyEffect || k == Kind::Interrupt ||
                          (k == Kind::Function && (m == Method::AddBuff || m == Method::SetState || m == Method::Damage));
    // I.5's puppet kinds (the WO's list) plus a copy's health written by the joiner's graph (II.1: the host's value only)
    if (role == kRoleJoiner && c.joined && c.puppets &&
        (k == Kind::Behaviour || k == Kind::EntityContext || k == Kind::SchedulerLink || k == Kind::Interrupt ||
         (k == Kind::Function && (m == Method::SetState || m == Method::Damage))))
        return true;
    if (role == kRoleJoiner && c.joined && c.route && mirrorTalker && soulKind) return true;
    if (role == kRoleHost && inClosure && c.route && soulKind) return true;
    return false;
}

// The decision. `urgeParent`: a Behaviour node inside an UrgeADialog library (an NPC walks up and starts a dialogue with the player);
// `moneyParent`: an Items function inside a money library (MoveMoney_*, createmoney*).
inline Verdict decide(uint8_t role, Kind k, Method m, Target t, bool inClosure, bool mirrorTalker, bool urgeParent, bool moneyParent,
                      const Cfg& c) {
    if (k == Kind::None) return Verdict::Run;
    const bool playerBody = (k == Kind::BodyEffect || k == Kind::Interrupt ||
                             (k == Kind::Function && (m == Method::AddBuff || m == Method::SetState || m == Method::Damage))) && t == Target::Player;
    const bool alwaysPlayer = k == Kind::RewardPlayer || (k == Kind::Function && m == Method::PlayerReward) ||
                              (k == Kind::Function && m == Method::Items && moneyParent);
    if (role == kRoleJoiner && c.joined) {
        // I.5 first: a quest behaviour on a puppet never runs here, whatever started it (the WO's list: behaviours -- duels, tours,
        // skirmishes --, entity contexts, scheduler links, the teleport interrupts, KillNpc's damage), nor a health written into a copy
        // (II.1: the host's value). The joiner's own Henry is not a puppet. A copy's looks (clothing / weapon presets, buffs) are not
        // refused: no host value replaces them yet (a listed gap) and the mirrored quest state gives them as the host's world does.
        if (c.puppets) {
            const bool puppetKind = k == Kind::Behaviour || k == Kind::EntityContext || k == Kind::SchedulerLink || k == Kind::Interrupt ||
                                    (k == Kind::Function && (m == Method::SetState || m == Method::Damage));
            if (puppetKind && t != Target::Player) return Verdict::PuppetRefused;
        }
        // I.3 on the joiner: the host's own conversation's consequences, mirrored here, reach the host's Henry only.
        if (c.route && mirrorTalker) {
            if (alwaysPlayer || playerBody) return Verdict::TalkerOnlyJoiner;
            if (k == Kind::DialogueStarter || k == Kind::View || (k == Kind::Function && m == Method::Monologue))
                return Verdict::TalkerOnlyJoiner;
        }
        return Verdict::Run;
    }
    if (role == kRoleHost && inClosure) {
        if (c.hostClosure) {
            if (k == Kind::DialogueStarter || (k == Kind::Function && m == Method::Monologue) || (k == Kind::Behaviour && urgeParent))
                return Verdict::HostDialogue;
            if (k == Kind::View) return Verdict::HostView;
        }
        if (c.route && (alwaysPlayer || playerBody)) return Verdict::TalkerOnlyHost;
    }
    return Verdict::Run;
}

// Gate 1 (the tap) on the joiner: a clock's out-port fire is dropped (I.4); a dialogue node's is recorded (I.1) and runs here too.
enum class Tap : uint8_t { Pass = 0, QuietDrop, Capture, Continue };
inline Tap tap(uint8_t role, Kind ownerKind, bool joined, bool quietClocks, bool capture, bool waiterInClosure, bool inClosure) {
    if (role == kRoleJoiner && joined && quietClocks && ownerKind == Kind::Clock) return Tap::QuietDrop;
    // a conversation's node (a forced or fader dialogue is one too): the host marks its own talks as well (I.3's mirror rule)
    if ((ownerKind == Kind::Dialogue || ownerKind == Kind::DialogueStarter) && capture && !inClosure) return Tap::Capture;
    if (ownerKind == Kind::Waiter && waiterInClosure && !inClosure) return Tap::Continue;
    return Tap::Pass;
}

// The closure's life (the WO's 5 minutes): a waiter enqueued inside a closure continues it when it finishes within this.
constexpr uint64_t kClosureTtlMs = 5ull * 60 * 1000;
// A dropped clock fire is logged once per clock per this long (a watch that keeps firing inside its window).
constexpr uint64_t kQuietLogEveryMs = 60ull * 1000;

// Closure ids: the host's replays carry the joiner's id (< 2^31); local captures are marked with the high bit.
constexpr uint32_t kLocalClosureBit = 0x80000000u;
inline bool closure_is_local(uint32_t id) { return (id & kLocalClosureBit) != 0; }

// Name tests on a parent module's instance name (library instances keep their library's name, lower- or mixed-case).
inline bool icontains(const char* s, const char* sub) {
    if (!s || !sub || !*sub) return false;
    const size_t n = std::strlen(sub);
    for (; *s; ++s) {
        size_t i = 0;
        for (; i < n && s[i]; ++i) {
            char a = s[i], b = sub[i];
            if (a >= 'A' && a <= 'Z') a = static_cast<char>(a - 'A' + 'a');
            if (b >= 'A' && b <= 'Z') b = static_cast<char>(b - 'A' + 'a');
            if (a != b) break;
        }
        if (i == n) return true;
    }
    return false;
}
inline bool is_urge_parent(const char* parentName) { return icontains(parentName, "urgeadialog"); }
// WO-201 B7: the player's own interaction monologues (the coal-sack and cart searches: interactiontrigger_monologonplayer_*) stay his.
inline bool is_interaction_parent(const char* parentName) {
    return icontains(parentName, "interactiontrigger") || icontains(parentName, "monologonplayer");
}

// WO-201 B7: does this node start something the story says to the player -- a monologue, a forced / fader dialogue, an NPC urging a
// dialogue, a focus camera? (Needs the parent's name only for a Behaviour.)
inline bool story_starter(Kind k, Method m) {
    return k == Kind::DialogueStarter || k == Kind::View || k == Kind::Behaviour || (k == Kind::Function && m == Method::Monologue);
}

// WO-201 B7, on the joiner in the host's world, after decide() said Run: the host's world decides whether the party strays, who is
// greeted, who is urged to talk -- his own logic starts none of it. Kept: a chain that began in his own conversation (a local closure:
// the tap marked it from his dialogue node's pulse), and his own interaction monologues.
inline bool joiner_story_bark(uint8_t role, Kind k, Method m, bool urgeParent, bool ownChain, bool interactionParent, const Cfg& c) {
    if (role != kRoleJoiner || !c.joined || !c.storyBarks) return false;
    if (!story_starter(k, m)) return false;
    if (k == Kind::Behaviour && !urgeParent) return false;   // other behaviours are I.5's (puppets) or his own
    if (ownChain || interactionParent) return false;
    return true;
}
inline bool is_money_parent(const char* parentName) { return icontains(parentName, "money"); }

// A soul-typed port, by its rttr type name ("wh::rpgmodule::I_Soul*", "wh::rpgmodule::Souls", ...).
inline bool is_soul_type(const char* typeName) {
    return icontains(typeName, "I_Soul") || icontains(typeName, "::Souls") || icontains(typeName, "C_Soul");
}

inline const char* kind_name(Kind k) {
    switch (k) {
        case Kind::Dialogue: return "Dialogue";
        case Kind::DialogueStarter: return "DialogueStarter";
        case Kind::Clock: return "Clock";
        case Kind::Waiter: return "Waiter";
        case Kind::Behaviour: return "Behaviour";
        case Kind::EntityContext: return "EntityContext";
        case Kind::SchedulerLink: return "SchedulerLink";
        case Kind::Interrupt: return "Interrupt";
        case Kind::Function: return "Function";
        case Kind::View: return "View";
        case Kind::RewardPlayer: return "RewardPlayer";
        case Kind::BodyEffect: return "BodyEffect";
        default: return "None";
    }
}

inline const char* verdict_name(Verdict v) {
    switch (v) {
        case Verdict::PuppetRefused: return "puppet";
        case Verdict::HostDialogue: return "host-dialogue";
        case Verdict::HostView: return "host-view";
        case Verdict::TalkerOnlyHost: return "talker-only-host";
        case Verdict::TalkerOnlyJoiner: return "talker-only-joiner";
        case Verdict::JoinerStoryBark: return "joiner-story";
        default: return "run";
    }
}

} // namespace kcdmp::wo200rules
