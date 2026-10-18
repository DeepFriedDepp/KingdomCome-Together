// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-200: engine-free checks of the two gates' rules (wo200_rules.h). Linked into KCDMP_NativeTests.
#include <cstdio>
#include <cstring>
#include <initializer_list>

#include "wo200_rules.h"
#include "hook_prologues.h"

using namespace kcdmp;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo200_tests(int* passed) {
    using namespace wo200rules;
    Cfg on;   // everything on (the shipped defaults), the joiner in the host's world
    on.joined = true;
    Cfg notJoined = on; notJoined.joined = false;

    // ---- method names (the last "::" segment) ----
    WCHECK(method_kind("wh::xgenaimodule::RequestMonologue") == Method::Monologue, "RequestMonologue");
    WCHECK(method_kind("wh::entitymodule::CreatePlayerReward") == Method::PlayerReward, "CreatePlayerReward (rewards, createmoney_player)");
    WCHECK(method_kind("EquipPlayersItem") == Method::PlayerReward && method_kind("UnequipPlayersItem") == Method::PlayerReward, "player items");
    WCHECK(method_kind("wh::rpgmodule::AddBuff") == Method::AddBuff && method_kind("SetState") == Method::SetState, "body effects by soul");
    WCHECK(method_kind("TakeDamage") == Method::Damage && method_kind("Revive") == Method::Damage, "KillNpc's TakeDamage, Revive");
    WCHECK(method_kind("CreateItems") == Method::Items && method_kind("MoveItems") == Method::Items, "inventories");
    WCHECK(method_kind("math::boolean::Or") == Method::Other && method_kind("GetState") == Method::Other, "pure logic and reads run");
    WCHECK(method_kind(nullptr) == Method::Other && method_kind("") == Method::Other, "no name: other");

    // ---- I.5: no quest behaviours on puppets (the joiner in the host's world) ----
    for (Kind k : {Kind::Behaviour, Kind::EntityContext, Kind::SchedulerLink, Kind::Interrupt}) {
        WCHECK(decide(kRoleJoiner, k, Method::Other, Target::Other, false, false, false, false, on) == Verdict::PuppetRefused,
               "%s on a copy or the figure: refused on the joiner", kind_name(k));
        WCHECK(decide(kRoleJoiner, k, Method::Other, Target::Unknown, false, false, false, false, on) == Verdict::PuppetRefused,
               "%s on no readable soul: refused (the host decides NPCs)", kind_name(k));
        WCHECK(decide(kRoleJoiner, k, Method::Other, Target::Player, false, false, false, false, on) == Verdict::Run,
               "%s on the joiner's own Henry (PlayerAction_*, a player context): runs", kind_name(k));
        WCHECK(decide(kRoleJoiner, k, Method::Other, Target::Other, false, false, false, false, notJoined) == Verdict::Run,
               "%s: a joiner not in the host's world runs his own game", kind_name(k));
        WCHECK(decide(kRoleHost, k, Method::Other, Target::Other, false, false, false, false, on) == Verdict::Run,
               "%s on the host outside a closure: the host's world runs it", kind_name(k));
    }
    WCHECK(decide(kRoleJoiner, Kind::Function, Method::Damage, Target::Other, false, false, false, false, on) == Verdict::PuppetRefused,
           "KillNpc's TakeDamage on a copy: refused on the joiner (the host's world kills)");
    WCHECK(decide(kRoleJoiner, Kind::Function, Method::SetState, Target::Other, false, false, false, false, on) == Verdict::PuppetRefused,
           "a copy's health written by the joiner's graph: refused (II.1: values come from the host)");
    WCHECK(decide(kRoleJoiner, Kind::Function, Method::SetState, Target::Player, false, false, false, false, on) == Verdict::Run,
           "the joiner's own Henry's SetState: runs");
    WCHECK(decide(kRoleJoiner, Kind::Function, Method::AddBuff, Target::Other, false, false, false, false, on) == Verdict::Run,
           "a buff on a copy (its looks, no host value yet): runs as the mirrored quest state gives it");
    WCHECK(decide(kRoleJoiner, Kind::BodyEffect, Method::Other, Target::Other, false, false, false, false, on) == Verdict::Run,
           "a clothing / weapon preset on a copy: runs (a listed gap: no host value replaces it)");
    Cfg noPuppets = on; noPuppets.puppets = false;
    WCHECK(decide(kRoleJoiner, Kind::Behaviour, Method::Other, Target::Other, false, false, false, false, noPuppets) == Verdict::Run,
           "joiner_no_puppet_quests off: today's behaviour");
    WCHECK(decide(kRoleJoiner, Kind::Clock, Method::Other, Target::Unknown, false, false, false, false, on) == Verdict::Run,
           "a clock node executing (Start/Stop) is not the gate's: the tap keeps its fire quiet");
    WCHECK(decide(kRoleJoiner, Kind::Dialogue, Method::Other, Target::Unknown, false, false, false, false, on) == Verdict::Run,
           "the joiner's dialogue nodes stay awake");

    // ---- I.2: in a replay's closure the host runs nothing its player didn't do ----
    WCHECK(decide(kRoleHost, Kind::DialogueStarter, Method::Other, Target::Unknown, true, false, false, false, on) == Verdict::HostDialogue,
           "ForcedDialog / FaderDialog in the closure: not on the host");
    WCHECK(decide(kRoleHost, Kind::Function, Method::Monologue, Target::Unknown, true, false, false, false, on) == Verdict::HostDialogue,
           "RequestMonologue in the closure: not on the host");
    WCHECK(decide(kRoleHost, Kind::Behaviour, Method::Other, Target::Other, true, false, true, false, on) == Verdict::HostDialogue,
           "UrgeADialog (an NPC urging a dialogue) in the closure: not on the host");
    WCHECK(decide(kRoleHost, Kind::Behaviour, Method::Other, Target::Other, true, false, false, false, on) == Verdict::Run,
           "any other behaviour in the closure (the lords' tour): runs on the host");
    WCHECK(decide(kRoleHost, Kind::View, Method::Other, Target::Unknown, true, false, false, false, on) == Verdict::HostView,
           "FocusCam / DisplayTutorial / PlayTrackView in the closure: not on the host");
    WCHECK(decide(kRoleHost, Kind::View, Method::Other, Target::Unknown, false, false, false, false, on) == Verdict::Run,
           "the host's own view outside a closure: runs");
    WCHECK(decide(kRoleHost, Kind::DialogueStarter, Method::Other, Target::Unknown, false, false, false, false, on) == Verdict::Run,
           "the host's own follow-on dialogue outside a closure: runs");
    Cfg noReplay = on; noReplay.hostClosure = false;
    WCHECK(decide(kRoleHost, Kind::View, Method::Other, Target::Unknown, true, false, false, false, noReplay) == Verdict::Run, "s1_replay's rules off");

    // ---- I.3: talker-only (rewards, money, body effects reach the talker) ----
    WCHECK(decide(kRoleHost, Kind::RewardPlayer, Method::Other, Target::Unknown, true, false, false, false, on) == Verdict::TalkerOnlyHost,
           "AddReward in the joiner's closure: not on the host's Henry");
    WCHECK(decide(kRoleHost, Kind::Function, Method::PlayerReward, Target::Unknown, true, false, false, false, on) == Verdict::TalkerOnlyHost,
           "CreatePlayerReward / createmoney_player: not on the host's Henry");
    WCHECK(decide(kRoleHost, Kind::Function, Method::Items, Target::Unknown, true, false, false, true, on) == Verdict::TalkerOnlyHost,
           "MoveMoney_* (a money library's MoveItems): not on the host");
    WCHECK(decide(kRoleHost, Kind::Function, Method::Items, Target::Unknown, true, false, false, false, on) == Verdict::Run,
           "a quest item's MoveItems (no money library): runs on the host -- quest items go to both");
    WCHECK(decide(kRoleHost, Kind::BodyEffect, Method::Other, Target::Player, true, false, false, false, on) == Verdict::TalkerOnlyHost,
           "a buff / outfit on the host's Henry from the joiner's talk: not run");
    WCHECK(decide(kRoleHost, Kind::BodyEffect, Method::Other, Target::Other, true, false, false, false, on) == Verdict::Run,
           "the same effect on an NPC (a drunk Semine): runs on the host");
    WCHECK(decide(kRoleHost, Kind::Interrupt, Method::Other, Target::Player, true, false, false, false, on) == Verdict::TalkerOnlyHost,
           "PlayerAction_* on the host's Henry from the joiner's talk: not run");
    WCHECK(decide(kRoleHost, Kind::RewardPlayer, Method::Other, Target::Unknown, false, false, false, false, on) == Verdict::Run,
           "the host's own rewards outside a closure: his");
    Cfg noRoute = on; noRoute.route = false;
    WCHECK(decide(kRoleHost, Kind::RewardPlayer, Method::Other, Target::Unknown, true, false, false, false, noRoute) == Verdict::Run,
           "s1_route off: the host's Henry gets it too (today's behaviour)");
    // the joiner's mirror of the host's own conversation
    WCHECK(decide(kRoleJoiner, Kind::RewardPlayer, Method::Other, Target::Unknown, false, true, false, false, on) == Verdict::TalkerOnlyJoiner,
           "the host talked: the joiner's mirror gives his Henry no reward");
    WCHECK(decide(kRoleJoiner, Kind::Function, Method::Monologue, Target::Unknown, false, true, false, false, on) == Verdict::TalkerOnlyJoiner,
           "the host talked: no monologue on the joiner's Henry");
    WCHECK(decide(kRoleJoiner, Kind::View, Method::Other, Target::Unknown, false, true, false, false, on) == Verdict::TalkerOnlyJoiner,
           "the host talked: no focus camera on the joiner");
    WCHECK(decide(kRoleJoiner, Kind::RewardPlayer, Method::Other, Target::Unknown, false, false, false, false, on) == Verdict::Run,
           "the joiner's own conversation (not a mirror): his reward runs on his machine");
    WCHECK(decide(kRoleJoiner, Kind::View, Method::Other, Target::Unknown, false, false, false, false, on) == Verdict::Run,
           "the joiner's own view: runs");

    // ---- which decisions read the souls (only those) ----
    WCHECK(needs_target(kRoleJoiner, Kind::Behaviour, Method::Other, false, false, on), "joiner behaviour: read");
    WCHECK(!needs_target(kRoleJoiner, Kind::Behaviour, Method::Other, false, false, notJoined), "not joined: no read");
    WCHECK(!needs_target(kRoleHost, Kind::Behaviour, Method::Other, false, false, on), "host outside a closure: no read");
    WCHECK(needs_target(kRoleHost, Kind::BodyEffect, Method::Other, true, false, on), "host closure body effect: read");
    WCHECK(!needs_target(kRoleHost, Kind::RewardPlayer, Method::Other, true, false, on), "AddReward is always the player: no read");
    WCHECK(!needs_target(kRoleHost, Kind::Function, Method::Other, true, false, on), "pure functions: no read");
    WCHECK(needs_target(kRoleJoiner, Kind::Function, Method::AddBuff, false, true, on), "joiner mirror AddBuff: read");
    WCHECK(!needs_target(kRoleJoiner, Kind::BodyEffect, Method::Other, false, false, on), "joiner, no mirror: a body effect is not read");

    // ---- Gate 1: the tap ----
    WCHECK(tap(kRoleJoiner, Kind::Clock, true, true, true, false, false) == Tap::QuietDrop, "I.4: the joiner's clock fire is dropped");
    WCHECK(tap(kRoleJoiner, Kind::Clock, false, true, true, false, false) == Tap::Pass, "not joined: the joiner's own world");
    WCHECK(tap(kRoleJoiner, Kind::Clock, true, false, true, false, false) == Tap::Pass, "joiner_quiet_clocks off");
    WCHECK(tap(kRoleHost, Kind::Clock, true, true, true, false, false) == Tap::Pass, "the host's clocks decide");
    WCHECK(tap(kRoleJoiner, Kind::Dialogue, true, true, true, false, false) == Tap::Capture, "I.1: a dialogue out-port is recorded");
    WCHECK(tap(kRoleJoiner, Kind::DialogueStarter, true, true, true, false, false) == Tap::Capture, "a forced dialogue's out-port too");
    WCHECK(tap(kRoleHost, Kind::Dialogue, true, true, true, false, false) == Tap::Capture, "the host marks its own talks (I.3's mirror rule)");
    WCHECK(tap(kRoleJoiner, Kind::Dialogue, true, true, true, false, true) == Tap::Pass, "inside a closure already: the outer one holds");
    WCHECK(tap(kRoleJoiner, Kind::Dialogue, true, true, false, false, false) == Tap::Pass, "s1_replay off: no capture");
    WCHECK(tap(kRoleHost, Kind::Waiter, true, true, true, true, false) == Tap::Continue, "a waiter enqueued in a closure continues it");
    WCHECK(tap(kRoleHost, Kind::Waiter, true, true, true, false, false) == Tap::Pass, "a waiter no closure enqueued: passes");
    WCHECK(tap(kRoleJoiner, Kind::Behaviour, true, true, true, false, false) == Tap::Pass, "any other node's pulse passes");

    // ---- names, closure ids ----
    WCHECK(is_urge_parent("AfterSeeingPlayer_UrgeADialog_Advanced") && is_urge_parent("urgeadialog_pocestny"), "UrgeADialog instances");
    WCHECK(!is_urge_parent("tour_advanced") && !is_urge_parent(nullptr), "not urge");
    WCHECK(is_money_parent("movemoney") && is_money_parent("MoveMoney_SoulToSoul") && is_money_parent("createmoney_player"), "money libraries");
    WCHECK(!is_money_parent("quest_items"), "not money");
    WCHECK(is_soul_type("wh::rpgmodule::I_Soul*") && is_soul_type("wh::rpgmodule::Souls"), "soul port types");
    WCHECK(!is_soul_type("wh::xgenaimodule::LinkableObjects") && !is_soul_type("bool") && !is_soul_type(nullptr), "not soul types");
    WCHECK(closure_is_local(kLocalClosureBit | 5) && !closure_is_local(5), "local closures carry the high bit");
    WCHECK(kClosureTtlMs == 300000, "the WO's 5 minutes");
    WCHECK(std::strcmp(last_segment("a::b::RequestMonologue"), "RequestMonologue") == 0 && std::strcmp(last_segment("X"), "X") == 0, "last segment");

    // ---- the node gate's prologue (registry) ----
    WCHECK(sizeof hookpro::kNodeExecute == 17 && hookpro::kNodeExecute[0] == 0x40 && hookpro::kNodeExecute[16] == 0xF2,
           "C_Node::Execute's 17 bytes: push rbx/rsi/rdi, sub rsp,80h, mov rbx,rcx, mov rsi,rdx");

    *passed = g_pass;
    return g_fail;
}
