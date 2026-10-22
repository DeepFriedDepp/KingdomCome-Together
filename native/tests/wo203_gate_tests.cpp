// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-203 I.5: engine-free checks of the node gate's scene rules (wo200_rules.h) -- session C's hijack, fixed at both ends. Linked into
// KCDMP_NativeTests.
#include <cstdio>
#include <cstring>
#include <initializer_list>

#include "wo200_rules.h"

using namespace kcdmp;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo203_gate_tests(int* passed) {
    using namespace wo200rules;
    Cfg on;   // everything on (the shipped defaults), the joiner in the host's world
    on.joined = true;
    const uint32_t local = kLocalClosureBit | 1;   // the joiner's capture of his talk's OnSpoke (session C: 80000001)
    const uint32_t replay = 0x01000001;            // the host's replay closure of it (peer 1, closure 1)

    // ---- the class table grows at its end only (op 74 reports the kind's number; the stats line reads the verdicts by index) ----
    WCHECK(static_cast<int>(Kind::BodyEffect) == 12 && static_cast<int>(Kind::SceneStarter) == 13 && static_cast<int>(Kind::Count) == 14,
           "Kind::SceneStarter appended before Count");
    WCHECK(static_cast<int>(Verdict::JoinerStoryBark) == 6 && static_cast<int>(Verdict::HostScene) == 7 && static_cast<int>(Verdict::Count) == 8,
           "Verdict::HostScene appended before Count");
    WCHECK(std::strcmp(kind_name(Kind::SceneStarter), "SceneStarter") == 0, "its kind word");
    WCHECK(std::strcmp(verdict_name(Verdict::HostScene), "host-scene") == 0, "its verdict word");
    WCHECK(std::strcmp(scene_name(Scene::Player), "fader-or-ingame") == 0 && std::strcmp(scene_name(Scene::Other), "other") == 0 &&
           std::strcmp(scene_name(Scene::NoStart), "busy") == 0 && std::strcmp(scene_name(Scene::Unknown), "unknown") == 0, "the scene words");

    // ---- Gate 1: a scene handler a closure started carries it to its out-ports (the node gate's view only) ----
    WCHECK(tap(kRoleJoiner, Kind::SceneStarter, true, true, true, true, false) == Tap::ContinueLocal,
           "the joiner: a marked handler's AfterPlay, outside any closure, continues it locally");
    WCHECK(tap(kRoleHost, Kind::SceneStarter, true, true, true, true, false) == Tap::ContinueLocal,
           "the host: a marked handler's out-port continues the replay's closure too (the safety net)");
    WCHECK(tap(kRoleJoiner, Kind::SceneStarter, true, true, true, false, false) == Tap::Pass,
           "a handler no closure started: passes (its scene is nobody's chain)");
    WCHECK(tap(kRoleJoiner, Kind::SceneStarter, true, true, true, true, true) == Tap::Pass,
           "inside a closure already: the outer one holds");
    WCHECK(tap(kRoleJoiner, Kind::SceneStarter, true, true, false, true, false) == Tap::ContinueLocal,
           "the capture bit does not decide a continuation (the caller asks for the mark only under the continue bit)");
    WCHECK(tap(kRoleJoiner, Kind::Waiter, true, true, true, true, false) == Tap::Continue,
           "a waiter's continuation is unchanged (it still goes to the agent)");
    WCHECK(tap(kRoleJoiner, Kind::Clock, true, true, true, true, false) == Tap::QuietDrop, "a clock's fire is still dropped first");
    WCHECK(tap(kRoleJoiner, Kind::DialogueStarter, true, true, true, false, false) == Tap::Capture,
           "the forced trialog's own out-ports (its consequences, later) are still captured");

    // ---- the closure the node gate sees: the thread's own, else a scene's carry ----
    WCHECK(gate_closure(0, 0) == 0 && gate_closure(local, 0) == local && gate_closure(0, local) == local, "own closure, else the carry");
    WCHECK(gate_closure(kLocalClosureBit | 9, local) == (kLocalClosureBit | 9), "a capture inside a carry: its own closure wins");
    WCHECK(own_chain(local) && !own_chain(replay) && !own_chain(0), "own chain = a local closure");
    WCHECK(in_replay_closure(replay) && !in_replay_closure(local) && !in_replay_closure(0), "a replay closure = the joiner's id on the host");

    // ---- the handler's state: may this Execute start a scene? (WO-193: an enqueue is accepted at 0 only) ----
    WCHECK(handler_may_start(0), "idle: an enqueue starts a scene");
    WCHECK(!handler_may_start(1) && !handler_may_start(2) && !handler_may_start(3) && !handler_may_start(4) && !handler_may_start(5),
           "queued / ready / playing / ended / finishing: no new scene (a Play / Finish input, or an enqueue the engine refuses itself)");
    WCHECK(handler_may_start(-1), "not read: treated as a start (fail closed)");
    WCHECK(handler_may_start(6) && handler_may_start(0x7FFF), "not a state the build has: treated as a start (fail closed)");

    // ---- the host skip list inside a replay's closure (decide()'s host branch) ----
    WCHECK(decide(kRoleHost, Kind::SceneStarter, Method::Other, Target::Unknown, true, false, false, false, on, Scene::Player) == Verdict::HostScene,
           "session C: the fader zbranePanaSemina_crossroadSecondTrialog in replay 01000001: not on the host");
    WCHECK(decide(kRoleHost, Kind::SceneStarter, Method::Other, Target::Unknown, true, false, false, false, on, Scene::Unknown) == Verdict::HostScene,
           "a scene not readable (no configuration yet): refused anyway inside a host replay closure");
    WCHECK(decide(kRoleHost, Kind::SceneStarter, Method::Other, Target::Unknown, true, false, false, false, on) == Verdict::HostScene,
           "the default scene is unknown: fail closed");
    WCHECK(decide(kRoleHost, Kind::SceneStarter, Method::Other, Target::Unknown, true, false, false, false, on, Scene::Other) == Verdict::Run,
           "a rendered video / track view / text in the closure: runs (WO-179's own path)");
    WCHECK(decide(kRoleHost, Kind::SceneStarter, Method::Other, Target::Unknown, true, false, false, false, on, Scene::NoStart) == Verdict::Run,
           "a busy handler's input (the host's own scene's Play / Finish): runs");
    WCHECK(decide(kRoleHost, Kind::Dialogue, Method::Other, Target::Unknown, true, false, false, false, on) == Verdict::HostDialogue,
           "a dialogue / dialogue-wrapper start executed in the closure: not on the host");
    WCHECK(decide(kRoleHost, Kind::DialogueStarter, Method::Other, Target::Unknown, true, false, false, false, on) == Verdict::HostDialogue,
           "the forced / fader dialogue in the closure: still not on the host");
    for (Scene s : {Scene::Player, Scene::Unknown, Scene::Other, Scene::NoStart}) {
        WCHECK(decide(kRoleHost, Kind::SceneStarter, Method::Other, Target::Unknown, false, false, false, false, on, s) == Verdict::Run,
               "the host's own scene (%s) outside a closure: runs", scene_name(s));
    }
    WCHECK(decide(kRoleHost, Kind::Dialogue, Method::Other, Target::Unknown, false, false, false, false, on) == Verdict::Run,
           "the host's own conversation outside a closure: runs");
    WCHECK(decide(kRoleHost, Kind::DialogueStarter, Method::Other, Target::Unknown, false, false, false, false, on) == Verdict::Run,
           "the host's own forced dialogue outside a closure: runs");
    Cfg noReplay = on; noReplay.hostClosure = false;
    WCHECK(decide(kRoleHost, Kind::SceneStarter, Method::Other, Target::Unknown, true, false, false, false, noReplay, Scene::Player) == Verdict::Run &&
           decide(kRoleHost, Kind::Dialogue, Method::Other, Target::Unknown, true, false, false, false, noReplay) == Verdict::Run,
           "s1_replay's rules off: today's behaviour");
    for (Scene s : {Scene::Player, Scene::Unknown, Scene::Other, Scene::NoStart}) {
        WCHECK(decide(kRoleJoiner, Kind::SceneStarter, Method::Other, Target::Unknown, false, false, false, false, on, s) == Verdict::Run,
               "the joiner's own scene (%s): runs on his machine (the scene bus shares it)", scene_name(s));
        WCHECK(decide(kRoleJoiner, Kind::SceneStarter, Method::Other, Target::Unknown, false, true, false, false, on, s) == Verdict::Run,
               "the joiner's mirror of the host's conversation: a scene is not talker-only (%s)", scene_name(s));
    }
    WCHECK(!story_starter(Kind::SceneStarter, Method::Other), "B7 does not refuse scenes (only what starts a bark or a conversation)");
    WCHECK(!needs_target(kRoleHost, Kind::SceneStarter, Method::Other, true, false, on), "a scene's decision reads no souls");

    // ---- B7 on the joiner, with the closure the scene carried ----
    {
        // the trialog's C_ForcedDialogueNode, EnqueueDialogue pulsed by cutscenehandler3.AfterPlay
        const uint32_t cid = gate_closure(0, local);   // the carry: AfterPlay continued the talk's local closure
        const Verdict v = decide(kRoleJoiner, Kind::DialogueStarter, Method::Other, Target::Unknown, in_replay_closure(cid), false, false, false, on);
        WCHECK(v == Verdict::Run, "the joiner's decide() runs a forced dialogue outside a mirror");
        WCHECK(!joiner_story_bark(kRoleJoiner, Kind::DialogueStarter, Method::Other, false, own_chain(cid), false, on),
               "with the carried local closure: his own chain -- the trialog starts on his machine");
        const uint32_t none = gate_closure(0, 0);
        WCHECK(joiner_story_bark(kRoleJoiner, Kind::DialogueStarter, Method::Other, false, own_chain(none), false, on),
               "without a closure (the host's world started it): joiner-story, unchanged");
        WCHECK(joiner_story_bark(kRoleJoiner, Kind::DialogueStarter, Method::Other, false, own_chain(gate_closure(0, replay)), false, on),
               "a carry of a non-local closure is not his own chain");
    }
    // ---- the host, the safety net: a scene that ran in a replay closure anyway (a rendered one): its AfterPlay's dialogue is refused ----
    {
        const uint32_t cid = gate_closure(0, replay);
        WCHECK(decide(kRoleHost, Kind::DialogueStarter, Method::Other, Target::Unknown, in_replay_closure(cid), false, false, false, on) == Verdict::HostDialogue,
               "the forced trialog the carried replay closure starts: not for the host's Henry");
        const uint32_t own = gate_closure(0, kLocalClosureBit | 3);   // the host's own talk's scene
        WCHECK(decide(kRoleHost, Kind::DialogueStarter, Method::Other, Target::Unknown, in_replay_closure(own), false, false, false, on) == Verdict::Run,
               "the host's own talk's scene carries the host's own closure: his forced dialogue runs");
    }

    // ---- the handler's mark after the gate's decision ----
    WCHECK(scene_mark(local, Scene::Player, Verdict::Run) == SceneMark::Mark, "a scene started inside a closure: marked");
    WCHECK(scene_mark(local, Scene::Unknown, Verdict::Run) == SceneMark::Mark && scene_mark(local, Scene::Other, Verdict::Run) == SceneMark::Mark,
           "any scene started inside a closure carries it");
    WCHECK(scene_mark(0, Scene::Player, Verdict::Run) == SceneMark::Forget, "a scene started outside any closure: the handler's old mark goes");
    WCHECK(scene_mark(replay, Scene::Player, Verdict::HostScene) == SceneMark::Forget,
           "refused (no scene starts): no mark, so the host's own later scene on this handler is never the replay's");
    WCHECK(scene_mark(replay, Scene::NoStart, Verdict::Run) == SceneMark::Keep && scene_mark(0, Scene::NoStart, Verdict::Run) == SceneMark::Keep,
           "a busy handler's input: the mark of the scene it belongs to stands");

    // ---- the refusal log: the cap stays for routine refusals; conversations and scenes kept from starting are always logged ----
    WCHECK(always_logged(Verdict::JoinerStoryBark, Kind::DialogueStarter), "session C's five joiner refusals: logged");
    WCHECK(always_logged(Verdict::HostScene, Kind::SceneStarter) && always_logged(Verdict::HostDialogue, Kind::Dialogue) &&
           always_logged(Verdict::HostDialogue, Kind::DialogueStarter), "the host's scene and dialogue refusals: logged");
    WCHECK(!always_logged(Verdict::JoinerStoryBark, Kind::Function) && !always_logged(Verdict::HostDialogue, Kind::Function) &&
           !always_logged(Verdict::HostDialogue, Kind::Behaviour), "monologue barks and urged dialogues: routine (capped)");
    WCHECK(!always_logged(Verdict::HostView, Kind::View) && !always_logged(Verdict::PuppetRefused, Kind::Behaviour) &&
           !always_logged(Verdict::Run, Kind::DialogueStarter), "the rest: routine");
    WCHECK(kStoryLogEveryMs == 10000, "once per node per 10 s");
    WCHECK(log_due(0, 5000, kStoryLogEveryMs), "never logged: due");
    WCHECK(!log_due(5000, 14999, kStoryLogEveryMs) && log_due(5000, 15000, kStoryLogEveryMs), "again after 10 s");

    *passed = g_pass;
    return g_fail;
}
