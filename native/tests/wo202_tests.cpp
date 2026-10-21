// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-202: engine-free checks of the native half's rules (crash_rules.h, wo202_rules.h). Linked into KCDMP_NativeTests.
#include <cstdio>
#include <cstring>

#include "crash_rules.h"
#include "wo202_rules.h"

using namespace kcdmp;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo202_tests(int* passed) {
    using namespace crashrules;
    // ---- I.1 crash_capture ----
    char b[64];
    folder_name(b, sizeof b, 2026, 10, 20, 20, 9, 40, 4321);
    WCHECK(!std::strcmp(b, "crash-20261020-200940-4321"), "the crash folder's name: %s", b);
    WCHECK(!std::strcmp(exception_name(0xC0000005u), "access violation"), "0xC0000005 is an access violation");
    WCHECK(!std::strcmp(exception_name(0xC00000FDu), "stack overflow"), "0xC00000FD is a stack overflow");
    WCHECK(!std::strcmp(exception_name(0x12345678u), "exception"), "an unknown code is named plainly");
    WCHECK(is_mod_module("KCDMP.dll") && is_mod_module("kcdmp_w202_3_201500.DLL") && is_mod_module("KCDMP_base.dll"),
           "the mod's DLL and its renamed copies are the mod's");
    WCHECK(!is_mod_module("XGenAIModule.dll") && !is_mod_module("KCDMP") && !is_mod_module("KCDMP.exe") && !is_mod_module(nullptr) &&
           !is_mod_module("KCD.dll"), "the game's modules (and a non-DLL) are not");
    WCHECK(kThunkRetAt == 0x88 && kThunkFrame == 0x90, "the thunk's frame: 4 pushes + 0x68 below the caller's return address");
    WCHECK(kThunkCallEnd == 46, "the thunk's call ends 46 bytes in (pushes 6, sub 4, xmm saves 24, mov rax 10, call 2)");
    WCHECK(classify(false, true, 200, 128) == FrameKind::HookThunk && classify(false, true, 20, 128) == FrameKind::HookTrampoline,
           "a hook page: the thunk at +128, the trampoline below it");
    WCHECK(classify(true, false, 0, 128) == FrameKind::Mod && classify(false, false, 0, 128) == FrameKind::Game, "mod code vs game code");
    WCHECK(std::strstr(frame_tag(FrameKind::Mod), "mod") && !*frame_tag(FrameKind::Game), "only the mod's frames carry a tag");

    // ---- II.4 quest_fail_trace: what counts as a failure (the field's hired hand: session B, 19:15:52) ----
    using namespace wo202rules;
    WCHECK(is_fail_change("Barbora.trosecko.zbranePanaSemina.h.boj_v_tabore.getPacholek", "SetCanceled", 5), "a step cancelled");
    WCHECK(is_fail_change("Barbora.x.y.step", "SetFailed", 3) && is_fail_change("Barbora.x.y.step", "SetAborted", 4), "failed, aborted");
    WCHECK(is_fail_change("Barbora.trosecko.zbranePanaSemina.aftermath.HracSeNaQuestVykaslal", "SetTrue", 1), "the player-bailed flag set");
    WCHECK(is_fail_change("Barbora.trosecko.zbranePanaSemina.h.hrac_se_na_to_vykaslal.vykaslaniSeStav", "SetHracMimo_SaSpokracuji", 4),
           "the player-bailed state moved on");
    WCHECK(!is_fail_change("Barbora.trosecko.zbranePanaSemina.aftermath.HracSeNaQuestVykaslal", "SetFalse", 0), "the flag cleared: no failure");
    WCHECK(!is_fail_change("Barbora.trosecko.zbranePanaSemina.h.boj_v_tabore.pacholek.pacholekState", "SetTimerFinished", 3),
           "the timer's own end is the cause, not the failure");
    WCHECK(!is_fail_change("Barbora.x.y.step", "SetDone", 2) && !is_fail_change(nullptr, nullptr, 1), "a done step and nothing at all");
    WCHECK(!std::strcmp(source_kind(".?AVC_Timer@rpgmodule@wh@@"), "timer") && !std::strcmp(source_kind(".?AVC_TimeOfDayWatch@rpgmodule@wh@@"), "timer"),
           "the clocks are timers");
    WCHECK(!std::strcmp(source_kind(".?AVC_DistanceTrigger@xgenaimodule@wh@@"), "distance-trigger"), "a distance trigger");
    WCHECK(!std::strcmp(source_kind(".?AVC_StateWatch@conceptmodule@wh@@"), "watch") && !std::strcmp(source_kind(""), "unknown"), "a watch; none");

    // ---- II.3 death_engine_only: the sampler's FATAL needs the engine's dead soul ----
    WCHECK(!fatal_is_death(true, true, false, true), "health 0, the soul alive: a hit, not a death (0.61.9 sent FATAL)");
    WCHECK(fatal_is_death(true, true, true, true), "health 0 and the soul dead: the death");
    WCHECK(!fatal_is_death(true, false, false, true), "the soul unread: never a death on a guess");
    WCHECK(fatal_is_death(true, false, false, false), "the switch off: 0.61.9's health rule");
    WCHECK(!fatal_is_death(false, true, true, true), "no zero, no FATAL from this sampler");
    WCHECK(kBitDeathEngineOnly == 512, "the bit matches Wo202Rules.ConfigBits");

    // ---- IV.1 anim_shown: the copy's previous swing steps down as the engine's Withdraw does ----
    WCHECK(lower_previous(true, 2, 5) && lower_previous(true, 3, 5), "installed or exiting at 5: stepped down");
    WCHECK(!lower_previous(true, 1, 5) && !lower_previous(true, 4, 5) && !lower_previous(true, 0, 5), "pending, finished, none: left alone");
    WCHECK(!lower_previous(true, 2, 3) && !lower_previous(true, 2, 9), "already stepped down, or not ours (a hit reaction's 9)");
    WCHECK(!lower_previous(false, 2, 5), "the switch off: 0.61.9");
    WCHECK(kWithdrawPriority < kSwingPriority && kActPriorityOff == 0x24 && kActStatusOff == 0x28, "the base action's fields (0x10BA90)");

    // ---- I.2 near_safe: the figure's NPC is used only once it has been the same object for 2 s ----
    int fa = 0, fb = 0;
    WCHECK(!figure_usable(&fa, &fa, 1000, 2999), "a new figure NPC waits (1.999 s)");
    WCHECK(figure_usable(&fa, &fa, 1000, 3000), "settled after 2 s");
    WCHECK(!figure_usable(&fb, &fa, 0, 100000), "another object than the settled one: never");
    WCHECK(!figure_usable(nullptr, nullptr, 0, 100000), "no figure");
    WCHECK(kBitCrash == 1 && kBitNearSafe == 2 && kBitQuestFail == 4 && kBitAnimShown == 16 && kBitDeathEngineOnly == 512,
           "the bits match Wo202Rules.ConfigBits");

    // ---- V.2 speech_share: the field's own voice path (no extension) is a voice path; WO-201's ".ogg" rule refused it ----
    WCHECK(is_voice_path("Localization/dialog/trosecko/zbranePanaSemina/v_ta_pacholek__ale_dyt_ms_O8Jj"), "the field's path (forward slashes)");
    WCHECK(is_voice_path("Localization\\dialog\\trosecko\\zbranePanaSemina\\v_ta_pacholek__ale_dyt_ms_O8Jj"), "the WO's path (backslashes)");
    WCHECK(is_voice_path("Localization/dialog/open_world/x/line_01.ogg"), "an extension is still fine");
    WCHECK(!is_voice_path("Localization/dialog/") && !is_voice_path("Localization/dialog/a/"), "no file name");
    WCHECK(!is_voice_path("Localization/dialog/../../bin/x") && !is_voice_path("Sounds/dialog/a/b") && !is_voice_path("Localization/music/a/b"),
           "no '..', only the Localization dialog tree");
    WCHECK(!is_voice_path(nullptr) && !is_voice_path("Loc"), "nothing, too short");

    // ---- IV.4 fight_pos_fresh: a fighter near ANY player goes out every tick; a fighting copy runs on 0.3 s at most ----
    WCHECK(fight_fresh(true, false, false, 6.0f, 12.0f), "drawn, alive, 6 m from the partner: every tick");
    WCHECK(!fight_fresh(true, false, false, 13.0f, 12.0f), "beyond the engage range: the normal gate");
    WCHECK(!fight_fresh(false, false, false, 1.0f, 12.0f) && !fight_fresh(true, true, false, 1.0f, 12.0f) && !fight_fresh(true, false, true, 1.0f, 12.0f),
           "sheathed, dead or knocked out: the normal gate");
    WCHECK(extrap_max_s(true, true, 2.0) == 0.3 && extrap_max_s(false, true, 2.0) == 2.0 && extrap_max_s(true, false, 2.0) == 2.0,
           "0.3 s for a fighting copy, the walker's 2 s otherwise or with the switch off");

    *passed = g_pass;
    return g_fail;
}
