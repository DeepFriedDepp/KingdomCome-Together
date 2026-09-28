// WO-139: engine-free checks of the crime DLL half (native/KCDMP/wo139_rules.h)
// and the punishment scope of the WO-137 time hook (wo137_rules.h): the
// trespass levels and the HUD's own "trespassing" rule, the context allow-list
// of op 4, engine names, and which quest nodes are the punishment's.
// Linked into KCDMP_NativeTests; wo139_rules_tests() returns the number of failures.
#include <cstdio>

#include "wo137_rules.h"
#include "wo139_rules.h"

using namespace kcdmp::wo139rules;

namespace {
int g_fail = 0, g_pass = 0;
#define RCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo139_rules_tests(int* passed) {
    // the concept type trespassLevel, in order
    RCHECK(std::strcmp(trespass_level_name(0), "public") == 0, "0 = public");
    RCHECK(std::strcmp(trespass_level_name(1), "semipublic") == 0, "1 = semipublic");
    RCHECK(std::strcmp(trespass_level_name(2), "semipersonal") == 0, "2 = semipersonal");
    RCHECK(std::strcmp(trespass_level_name(3), "personal") == 0, "3 = personal (observed live: a Zelejov house)");
    RCHECK(std::strcmp(trespass_level_name(4), "prohibited") == 0, "4 = prohibited");
    RCHECK(std::strcmp(trespass_level_name(0xFF), "none-yet") == 0, "0xFF = nothing told yet");
    RCHECK(std::strcmp(trespass_level_name(9), "unknown") == 0, "anything else = unknown");

    // the HUD's own rule: the warning for personal and prohibited only
    RCHECK(!is_trespassing(0) && !is_trespassing(1) && !is_trespassing(2), "public, semipublic, semipersonal: no trespass (the HUD maps 1 and 2 to 0)");
    RCHECK(is_trespassing(3) && is_trespassing(4), "personal and prohibited: trespassing");
    RCHECK(!is_trespassing(0xFF) && !is_trespassing(5), "none-yet and out of range: not trespassing");

    // op 4 writes only these (each checked by the crime trees on the entity itself)
    RCHECK(context_allowed("crime_suppressMeleeStealthHitReaction"), "the stealth-hit branch's own off switch");
    RCHECK(context_allowed("crime_ignoredCorpse") && context_allowed("crime_ignoredUnconsciousBody"), "the found-body contexts");
    RCHECK(context_allowed("crime_ignoredNPCHitVolume"), "the witnessed-hit context (WO-68)");
    RCHECK(context_allowed("crime_ignoredHorseTheft_Horse"), "a legal horse (the lent-horse quests' own lever)");
    RCHECK(!context_allowed("combat_forcedTarget"), "a Relation context is not an entity one (op 3 owns it)");
    RCHECK(!context_allowed("crime_isAuthority") && !context_allowed("crime_disableReport"), "nothing else, not even another crime context");
    RCHECK(!context_allowed("") && !context_allowed(nullptr), "empty / null refused");
    RCHECK(!context_allowed("crime_ignoredCorpseX") && !context_allowed("xcrime_ignoredCorpse"), "exact names only");

    // engine names
    RCHECK(is_engine_name("tzel_man_7", 10), "a guard's name");
    RCHECK(is_engine_name("tzel_horse_1", 12), "a horse's name");
    RCHECK(!is_engine_name("tzel man", 8) && !is_engine_name("a;b", 3) && !is_engine_name("a\"b", 3), "spaces, separators and quotes refused");
    RCHECK(!is_engine_name("", 0) && !is_engine_name(nullptr, 3), "empty refused");
    {
        char longName[80]; for (int i = 0; i < 64; ++i) longName[i] = 'a'; longName[64] = 0;
        RCHECK(!is_engine_name(longName, 64) && is_engine_name(longName, 63), "63 characters at most");
    }

    // the punishment's nodes (the WO-139 scope of the WO-137 time hook)
    using kcdmp::wo137rules::is_punishment_path;
    RCHECK(is_punishment_path("Barbora.open_world.nextnextgenpunishment"), "the punishment module itself");
    RCHECK(is_punishment_path("Barbora.open_world.nextnextgenpunishment.utils.playpunishment_cutscenebuffsmonolog.execute_cutscene.skiptime_fasttravel_or_nothing.advanceworldtime7"),
           "the fast travel's AdvanceWorldTime 10h (data-verified path)");
    RCHECK(is_punishment_path("Barbora.open_world.nextnextgenpunishment.second_arrest_before_new_punishment.fasttravel_if_needed.advanceworldtime1"),
           "the second arrest's 9h");
    RCHECK(is_punishment_path("brambora.Barbora.open_world.nextnextgenpunishment.x"), "a database segment above Barbora");
    RCHECK(!is_punishment_path("Barbora.open_world.nextnextgenpunishmentX.y"), "a longer module name is another module");
    RCHECK(!is_punishment_path("Barbora.trosecko.hledaniPsa.h.advanceworldtime1"), "a quest's own time set is not the punishment's");
    RCHECK(!is_punishment_path("Barbora.utils.crime.punishment.punishment_executecutscenes"), "the legacy utils module (referenced by nobody) is not in scope");
    RCHECK(!is_punishment_path(nullptr) && !is_punishment_path(""), "null / empty");

    *passed = g_pass;
    return g_fail;
}
