// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-210: engine-free checks of wo210_rules.h. Linked into KCDMP_NativeTests; wo210_tests() returns the number of failures.
#include <cstdio>
#include <cstring>

#include "wo210_rules.h"

using namespace kcdmp::wo210rules;
namespace w8 = kcdmp::wo208rules;

namespace {
int g_fail = 0, g_pass = 0;
#define WCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo210_tests(int* passed) {
    // ---- the switch word and the bench's twin names
    WCHECK((kBitsDefault & kBitStateAlways) && (kBitsDefault & kBitOneRequest) && !(kBitsDefault & kBitClinchV2), "every WO-210 switch ships on; clinch v2 (B.2/B.3) is reserved, not in this build");
    WCHECK(twin_off_bits("copy_state_always") == kBitStateAlways, "a twin name turns its bit off");
    WCHECK(twin_off_bits("x,clinch_refusal_final") == kFixClinchFinal && (kFixClinchFinal & kBitsDefault) == 0, "a fix's twin, never in the agent's word");
    WCHECK(twin_off_bits("copy_state") == 0, "only a whole name");
    WCHECK(twin_off_bits("empty_host_neutral") == kFixEmptyHostNeutral && (kFixEmptyHostNeutral & (kBitsDefault | kFixClinchFinal)) == 0,
           "A: the empty host scope's fix has its own twin bit");

    // ---- A: the family by the game's fragment names (E2's copies: the bedtime idle state the men and women lay in)
    WCHECK(posture_of_name("Lying") == PFam::Lying && posture_of_name("LyingTurn") == PFam::Lying && posture_of_name("WoundedLying_3") == PFam::Lying,
           "lying by name");
    WCHECK(posture_of_name("SleepOnTable") == PFam::Lying && posture_of_name("Party_Lying_Drunk_Resting") == PFam::Lying && posture_of_name("BlendRagdoll") == PFam::Lying,
           "asleep at a table, drunk on the floor, a knockdown's ragdoll: lying");
    WCHECK(posture_of_name("SittingIdle") == PFam::Sitting && posture_of_name("SittingIdle_VAR") == PFam::Sitting && posture_of_name("Quest_DrinkingOnBench") == PFam::Sitting,
           "sitting by name");
    WCHECK(posture_of_name("PrayKneeling") == PFam::Kneeling && posture_of_name("Surrender") == PFam::Kneeling, "kneeling, yielded");
    WCHECK(posture_of_name("CombatDeathGen") == PFam::Dead && posture_of_name("DeadPose") == PFam::Dead && posture_of_name("HitDeath") == PFam::Dead, "death");
    WCHECK(posture_of_name("CombatClinchStartGuardMasterGen") == PFam::Paired && posture_of_name("CombatAttackSyncGen") == PFam::Paired, "paired halves");
    WCHECK(posture_of_name("MotionIdle") == PFam::Standing && posture_of_name("MotionMovement") == PFam::Moving, "the base by name");
    WCHECK(posture_of_name("CombatAttackGen") == PFam::Combat && posture_of_name("Bartender_PutStein") == PFam::Other && posture_of_name("") == PFam::Other,
           "an attack is combat; a work one-shot says no posture");

    // the class first: the copy's own base, the owner's posture install (a C_CombatAnimAction carrying the host's fragment)
    WCHECK(family(w8::kClsAIMovement, "MotionIdle", 5) == PFam::Standing && family(w8::kClsAIMovement, "MotionMovement", 4) == PFam::Moving, "the AI movement");
    WCHECK(family(w8::kClsAIMovement, "", 0) == PFam::Standing && family(w8::kClsAIMovement, "", 4) == PFam::Moving, "no name read: the idle by number");
    WCHECK(family(w8::kClsIdleState, "Lying", 45) == PFam::Lying && family(w8::kClsIdleState, "Lying", 11) == PFam::Lying, "the bedtime idle state, male 45 / female 11");
    WCHECK(family(w8::kClsCombatAnim, "Lying", 45) == PFam::Lying, "the owner's install of the host's lying fragment is lying (E2: kept after the wake)");
    WCHECK(family(w8::kClsIdleState, "Cooking", 5) == PFam::Working && family(w8::kClsIdleState, "MotionIdle", 5) == PFam::Standing,
           "the female table's 5 is Cooking: by name it is work, not the standing idle");
    WCHECK(family(w8::kClsVariation, "MotionIdleVAR1", 0) == PFam::Standing && family(w8::kClsVariation, "SittingIdle_VAR", 121) == PFam::Sitting, "a fidget of its posture");
    WCHECK(family(w8::kClsCombatMove, "CombatMovement", 10) == PFam::Combat && family(w8::kClsNone, "", 0) == PFam::Empty, "footwork; nothing installed");
    WCHECK(family(w8::kClsCombatAnim, "", 915) == PFam::Paired, "a paired fragment with no name read");

    WCHECK(transition_name("GetUp") && transition_name("StandUp") && transition_name("LyingDialogIn") && transition_name("SleepOnTableOut") &&
           transition_name("IdleToMove") && transition_name("SurrenderToMove"), "the game's ways into and out of a posture");
    WCHECK(!transition_name("Lying") && !transition_name("SittingIdle") && !transition_name("MotionIdle") && !transition_name(""), "postures and idles are not transitions");

    // ---- A.5 the watchdog's verdicts
    WCHECK(check(PFam::Empty, PFam::Standing, false, false) == Verdict::Empty && check(PFam::Empty, PFam::Empty, false, true) == Verdict::Empty,
           "an empty full body is a breach whatever else holds");
    WCHECK(check(PFam::Lying, PFam::Moving, false, false) == Verdict::StalePosture, "E2: lying where the host's NPC walks");
    WCHECK(check(PFam::Lying, PFam::Standing, false, false) == Verdict::StalePosture && check(PFam::Sitting, PFam::Combat, false, false) == Verdict::StalePosture,
           "sitting while the NPC fights");
    WCHECK(check(PFam::Lying, PFam::Lying, false, false) == Verdict::Ok && check(PFam::Lying, PFam::Moving, true, false) == Verdict::Ok,
           "the same posture, or the game's own get-up on its way");
    WCHECK(check(PFam::Standing, PFam::Lying, false, false) == Verdict::MissingPosture, "the NPC lies, the copy stands");
    WCHECK(check(PFam::Dead, PFam::Standing, false, false) == Verdict::StaleDead && check(PFam::Dead, PFam::Dead, false, false) == Verdict::Ok, "a dead pose on a living NPC");
    WCHECK(check(PFam::Combat, PFam::Standing, false, true) == Verdict::Ok && check(PFam::Combat, PFam::Moving, false, false) == Verdict::Ok,
           "the copy's own attack (a mod action the host named), footwork against a walk: not a posture breach");
    WCHECK(check(PFam::Lying, PFam::Paired, false, false) == Verdict::Ok, "a host paired half: the clinch's own rules");
    WCHECK(check(PFam::Sitting, PFam::Other, false, false) == Verdict::PostureOther && breach_after_s(Verdict::PostureOther) >= 2.0,
           "a seated copy against its host NPC's unnamed fragment (a gesture in the seat): the longer grace, never 0.3 s");
    WCHECK(check(PFam::Sitting, PFam::Other, false, false, true) == Verdict::StalePosture, "the twin (posture_other_grace off): a breach at once");
    WCHECK(check(PFam::Sitting, PFam::Standing, false, false) == Verdict::StalePosture, "a base family still breaches a posture at once");
    WCHECK(twin_off_bits("posture_other_grace") == kFixPostureOtherGrace && !std::strcmp(verdict_name(Verdict::PostureOther), "posture-vs-other"),
           "the grace's twin and its name");
    WCHECK(twin_off_bits("no_raw_window") == kFixNoRawWindow && (kFixNoRawWindow & (kFixPostureOtherGrace | kFixEmptyHostNeutral | kFixClinchFinal | kBitsDefault)) == 0,
           "C.4: the strike-window fix has its own twin bit");
    WCHECK(twin_off_bits("fight_base_own,kd_by_action") == (kFixFightBaseOwn | kFixKdByAction) && (kFixFightBaseOwn & kFixKdByAction) == 0,
           "A / C.3: the fight base and the knockdown-by-action fixes have their own twin bits");
    WCHECK(breach_after_s(Verdict::StalePosture) <= 0.3 && breach_after_s(Verdict::Empty) <= 0.3 && breach_after_s(Verdict::MissingPosture) >= 0.5,
           "A.1: a stale posture at most 0.3 s; the owner's own posture install first");
    WCHECK(kWatchEveryS <= 0.25 && kWatchRadiusM >= 60.0f, "A.5: every 250 ms within 60 m");

    // ---- A.2 the paths
    WCHECK(!std::strcmp(path_name(kPathDrift), "1-drift") && !std::strcmp(path_name(kPathBase), "15-host-base") && !std::strcmp(path_name(kPathUnknown), "unknown"),
           "the path names");
    WCHECK(charged_path(kPathBase, 10.0, 12.0) == kPathBase && charged_path(kPathBase, 10.0, 10.0 + kPathWindowS + 0.1) == kPathUnknown && charged_path(0, 10.0, 10.5) == kPathUnknown,
           "a reseat is charged to the copy's last exit within the window, else unknown");

    // ---- B.1 the clinch-loop detector
    {
        Burst b;
        WCHECK(!burst_ask(b, 1.00) && !burst_ask(b, 1.01) && !burst_ask(b, 1.02), "three asks are no loop");
        WCHECK(burst_ask(b, 1.03), "the fourth within a second is (the field: 4 a frame)");
        WCHECK(!burst_ask(b, 1.04) && !burst_ask(b, 1.20), "told once per stretch");
        WCHECK(!burst_ask(b, 1.20 + kLoopQuietS + 0.1), "a quiet stretch starts afresh");
        WCHECK(!burst_ask(b, 4.0) && !burst_ask(b, 4.5) && !burst_ask(b, 5.3), "asks spread over seconds are no loop");
    }

    // ---- C.3 knockdowns by edges
    {
        WCHECK((kCmCombat & kCmDown) == 0, "the stream byte's two bits are distinct");
        KdState s;
        WCHECK(kd_step(s, true, false, false, false, 1.0) == KdAct::None && !s.held, "up and up: nothing");
        WCHECK(kd_step(s, true, true, false, false, 2.0) == KdAct::Fall && s.held, "the host's down edge: the copy falls, held");
        WCHECK(kd_step(s, true, true, true, false, 2.1) == KdAct::None && s.held, "lying with its host NPC: held, nothing more");
        WCHECK(kd_step(s, true, false, true, false, 4.0) == KdAct::StandUp && !s.held, "the host's up edge: the copy is stood up");
        WCHECK(kd_step(s, true, false, false, false, 4.1) == KdAct::None, "standing again: nothing");
        KdState t;
        kd_step(t, true, true, false, false, 10.0);
        WCHECK(kd_step(t, true, true, false, false, 10.5) == KdAct::None && t.held, "not ragdolled yet half a second after the fall: still held");
        WCHECK(kd_step(t, true, true, false, false, 10.0 + kKdOwnUpGraceS) == KdAct::Release && !t.held, "its own engine stood it up: let go");
        WCHECK(kd_step(t, true, true, false, false, 12.0) == KdAct::None, "never knocked down twice for one host fall");
        WCHECK(kd_step(t, true, false, false, false, 13.0) == KdAct::None, "the host's up edge after its own get-up: nothing");
        KdState u;
        WCHECK(kd_step(u, true, true, true, false, 20.0) == KdAct::None && u.held, "already lying from a local blow: held as it is");
        WCHECK(kd_step(u, true, true, true, true, 20.5) == KdAct::Release && !u.held, "C.2: a copy that died is let go, never stood up");
        WCHECK(kd_step(u, true, false, true, true, 21.0) == KdAct::None, "and a dead copy's up edge does nothing");
        KdState w;
        kd_step(w, true, true, false, false, 30.0);
        WCHECK(kd_step(w, false, true, true, false, 30.5) == KdAct::StandUp && !w.held, "switched off live while it lies: the body is given back on its feet");
        WCHECK(kd_step(w, false, true, true, false, 31.0) == KdAct::None, "off: the copy's own engine and the old writers");
        KdState x;
        WCHECK(kd_step(x, false, true, false, false, 40.0) == KdAct::None && !x.held, "the twin (off): no fall");
        WCHECK(!std::strcmp(kd_act_name(KdAct::StandUp), "stand-up"), "the act names");
    }

    {
        // ---- E.2: RPGProcessHit's own code (the Modding Tools 1.5.5 CombatModule, RVA 0x372120; read this WO): the eight windows the parser
        // reads, at their offsets from the entry, in a buffer of int3 -- every piece once, in order, with its targets
        struct Win { uint32_t off; int len; uint8_t b[48]; };
        static const Win kWins[] = {
        {0x02D, 38, {0x48, 0x8B, 0x45, 0x08, 0x4C, 0x8B, 0xB8, 0xF0, 0x02, 0x00, 0x00, 0x49, 0x8B, 0x87, 0xC0, 0x02, 0x00, 0x00, 0x49, 0x8D, 0x8F, 0xC0, 0x02, 0x00, 0x00, 0xFF, 0x50, 0x08, 0x3B, 0x05, 0x39, 0xAA, 0x49, 0x00, 0x75, 0x07, 0xB0, 0x01}},   // type test
        {0x071, 9, {0x48, 0x8B, 0x5F, 0x18, 0x48, 0x8D, 0x4B, 0x24, 0xE8}},   // victim id
        {0x0D4, 20, {0xE8, 0xC7, 0x03, 0xE0, 0xFF, 0x0F, 0x57, 0xD2, 0x48, 0x8B, 0xCB, 0xF3, 0x0F, 0x10, 0x88, 0x90, 0x03, 0x00, 0x00, 0xE8}},   // repeat timer
        {0x140, 29, {0x48, 0x8B, 0x55, 0x08, 0x4C, 0x8D, 0x05, 0xA5, 0x7F, 0x2A, 0x00, 0x48, 0x8D, 0x4C, 0x24, 0x68, 0xE8, 0xAB, 0x51, 0xD6, 0xFF, 0xC6, 0x84, 0x24, 0xB0, 0x00, 0x00, 0x00, 0x00}},   // actor scope
        {0x15D, 23, {0x49, 0x8D, 0x8F, 0x80, 0x02, 0x00, 0x00, 0x49, 0x8B, 0x87, 0x80, 0x02, 0x00, 0x00, 0xFF, 0x50, 0x08, 0xF3, 0x0F, 0x11, 0x44, 0x24, 0x40}},   // strength
        {0x185, 43, {0x4C, 0x89, 0x64, 0x24, 0x38, 0x4C, 0x8D, 0x8C, 0x24, 0xB0, 0x00, 0x00, 0x00, 0x89, 0x44, 0x24, 0x30, 0x4C, 0x8D, 0x44, 0x24, 0x40, 0xC6, 0x44, 0x24, 0x28, 0x01, 0x49, 0x8B, 0xD5, 0x48, 0x8B, 0xCD, 0x48, 0x89, 0x7C, 0x24, 0x20, 0xE8, 0x60, 0x00, 0x00, 0x00}},   // core call
        {0x1BD, 25, {0x49, 0x8D, 0x8F, 0x80, 0x02, 0x00, 0x00, 0x74, 0x05, 0x0F, 0x57, 0xC9, 0xEB, 0x06, 0xF3, 0x0F, 0x10, 0x4C, 0x24, 0x40, 0xE8, 0x1A, 0x49, 0xD1, 0xFF}},   // strength setter
        {0x1D6, 12, {0x48, 0x8D, 0x4C, 0x24, 0x68, 0xE8, 0xC0, 0x52, 0xD6, 0xFF, 0xB0, 0x01}},   // unscope
        };
        uint8_t code[0x210];
        std::memset(code, 0xCC, sizeof code);
        for (const Win& w : kWins) std::memcpy(code + w.off, w.b, static_cast<size_t>(w.len));
        const uintptr_t at = 0x180372120ull;
        RphSites s{};
        WCHECK(parse_rph(code, sizeof code, at, &s), "RPGProcessHit's code parses");
        WCHECK(s.modelOff == 0x2F0 && s.typeOff == 0x2C0 && s.strOff == 0x280 && s.timeoutOff == 0x390, "model / type / strength / timeout offsets");
        WCHECK(s.detOff == 0x18 && s.vidOff == 0x24, "the hit-in's details and the victim's id in them");
        WCHECK(s.core == 0x180372330ull && s.setter == 0x180086C10ull && s.tuning == 0x1801725C0ull, "the core, the strength setter, the tuning getter");
        WCHECK(s.scopeCtor == 0x1800D7420ull && s.scopeDtor == 0x1800D75C0ull && s.scopeLabel == 0x18061A210ull, "the actor scope, its end and its label");
        WCHECK(s.noneType == 0x18080CBA8ull, "the none attack type's global");
        RphSites s2{};
        uint8_t moved[0x210];
        std::memcpy(moved, code, sizeof moved);
        moved[0x185 + 26] = 0x00;   // apply = 0 at RPGProcessHit's own call: not the code read
        WCHECK(!parse_rph(moved, sizeof moved, at, &s2), "a core call with another apply byte refuses");
        std::memcpy(moved, code, sizeof moved);
        moved[0x15D + 3] = 0x84;    // the strength read at another offset than the setter's
        WCHECK(!parse_rph(moved, sizeof moved, at, &s2), "the strength offset read twice the same, and the setter's");
        std::memcpy(moved, code, sizeof moved);
        std::memcpy(moved + 0x200, code + 0x1D6, 12);   // the unscope twice
        WCHECK(!parse_rph(moved, sizeof moved, at, &s2), "a piece found twice refuses");
        uint8_t swapped[0x210];
        std::memset(swapped, 0xCC, sizeof swapped);
        for (const Win& w : kWins) std::memcpy(swapped + (w.off == 0x071 ? 0x0E8 : w.off), w.b, static_cast<size_t>(w.len));
        WCHECK(!parse_rph(swapped, sizeof swapped, at, &s2), "the victim id read after the repeat timer: out of order, refused");
        static const uint8_t kDry[] = {0x48, 0x8D, 0x45, 0xA8, 0xC6, 0x44, 0x24, 0x28, 0x00, 0x4C, 0x8D, 0x44, 0x24, 0x50};
        static const uint8_t kVar[] = {0x40, 0x88, 0x7C, 0x24, 0x28, 0x4C, 0x8D, 0x44, 0x24, 0x44};
        WCHECK(dry_run_call(kDry, sizeof kDry) && !dry_run_call(kVar, sizeof kVar) && !dry_run_call(code + 0x185, 43), "an engine dry run is apply 0 by a literal");
        // the repeat window: one swing hits a body once per timeout; byte 0 set skips it; the cap stops a flood
        RepeatWin w{};
        WCHECK(!repeat_filtered(w, 7, false, 10.0, 0.5), "the first contact counts");
        WCHECK(repeat_filtered(w, 7, false, 10.2, 0.5), "the same body 0.2 s later: the engine's repeat filter");
        WCHECK(!repeat_filtered(w, 8, false, 10.25, 0.5), "another body in the same swing counts");
        WCHECK(!repeat_filtered(w, 7, false, 10.6, 0.5), "after the timeout it counts again");
        WCHECK(!repeat_filtered(w, 7, true, 10.61, 0.5), "byte 0 set: the engine skips its filter");
        RepeatWin f{};
        int counted = 0;
        for (int i = 0; i < 20; ++i) if (!repeat_filtered(f, 100 + static_cast<uint32_t>(i), false, 20.0 + i * 0.01, 0.5)) ++counted;
        WCHECK(counted == kHitCapPerS, "the cap: at most %d a second (got %d)", kHitCapPerS, counted);
        WCHECK(!repeat_filtered(f, 999, false, 21.5, 0.5), "a second later the cap is new");
        RepeatWin g{};
        for (uint32_t i = 0; i < 40; ++i) repeat_filtered(g, i, false, 30.0 + i, 0.5);
        WCHECK(!repeat_filtered(g, 1000, false, 80.0, 0.5) && repeat_filtered(g, 1000, false, 80.1, 0.5), "slots recycle: a new body after many");
        // the impact's edit: material effects only, handled, never lethal
        uint8_t r[0xA0];
        std::memset(r, 0x5A, sizeof r);
        fx_edit(r, 0x78, 0x75, 0x40, 0x41);
        uint32_t fl = 0; std::memcpy(&fl, r + 0x78, 4);
        WCHECK(fl == 4 && r[0x75] == 1 && r[0x40] == 0 && r[0x41] == 0 && r[0x28] == 0x5A, "flags 4, handled, lethal cleared, the rest untouched");
        WCHECK(twin_off_bits("hit_fx,no_joiner_values") == (kFixHitFx | kFixNoJoinerValues) && ((kFixHitFx | kFixNoJoinerValues) & kBitsDefault) == 0,
               "the E.2 and C.4 fixes have their twins, never in the agent's word");
        // E.3 fight_heal: a stretch of refusals over kHealAfterS (gaps under kHealGapS) heals, at most every kHealEveryS
        HealTrack h{};
        WCHECK(!heal_step(h, 10.0, -1e9), "the first refusal starts a stretch");
        WCHECK(!heal_step(h, 10.4, -1e9) && !heal_step(h, 10.8, -1e9), "under a second: not yet");
        WCHECK(heal_step(h, 11.0, -1e9), "refused for a second without a gap: healed");
        HealTrack hg{};
        heal_step(hg, 20.0, -1e9);
        WCHECK(!heal_step(hg, 20.9, -1e9), "a gap over kHealGapS starts the stretch again");
        WCHECK(!heal_step(hg, 21.3, -1e9) && !heal_step(hg, 21.7, -1e9) && heal_step(hg, 21.95, -1e9), "the new stretch heals after its own second");
        HealTrack hr{};
        bool early = false;
        for (double t = 30.0; t < 31.9; t += 0.4) early = early || heal_step(hr, t, 27.0);
        WCHECK(!early && heal_step(hr, 32.0, 27.0), "at most one heal every kHealEveryS");
        WCHECK(twin_off_bits("fight_heal") == kFixFightHeal && (kFixFightHeal & kBitsDefault) == 0, "the heal's twin");
        // E.1 ai_in_fight: the copy's own attack claims the host's only inside his fight; out of it the owner shows the host's attack
        WCHECK(ai_claims_attack(true, true, true), "on him, in his fight: the copy's own attack");
        WCHECK(!ai_claims_attack(true, true, false), "held, but out of his fight by the engine's relation: the owner's way");
        WCHECK(ai_claims_attack(true, false, false), "the twin: claimed as before (refused later by F.1, nothing shown)");
        WCHECK(!ai_claims_attack(false, true, true) && !ai_claims_attack(false, false, true), "not wanted: never claimed");
        WCHECK(twin_off_bits("ai_in_fight") == kFixAiInFight && (kFixAiInFight & (kBitsDefault | kFixFightHeal | kFixHitFx)) == 0,
               "the fallback's twin, a bit of its own");
    }

    *passed = g_pass;
    return g_fail;
}
