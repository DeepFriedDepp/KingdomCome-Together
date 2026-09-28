#pragma once
// WO-139: the engine-free rules of the crime DLL half (wo139.cpp).
// native/tests/wo139_rules_tests.cpp pins them.

#include <cstddef>
#include <cstdint>
#include <cstring>

namespace kcdmp::wo139rules {

// The concept type trespassLevel (IPL_GameData Libs/concept/definitions.xml), in order.
inline const char* trespass_level_name(uint32_t lv) {
    switch (lv) {
        case 0: return "public";
        case 1: return "semipublic";
        case 2: return "semipersonal";
        case 3: return "personal";
        case 4: return "prohibited";
        case 0xFF: return "none-yet";
    }
    return "unknown";
}

// The HUD's own rule (GUIModule's listener maps 1 and 2 to 0 before it draws):
// the warning shows for personal and prohibited areas only.
inline bool is_trespassing(uint32_t lv) { return lv == 3 || lv == 4; }

// The entity contexts op 4 may write (and nothing else): each is checked by the
// game's crime trees on the entity itself (Tables ai/ScriptContext.xml; the
// trees: handleHitReaction's stealth branch, handleAwareness_corpse /
// unconsciousBody, handleAwareness_playerMount / playerMountedVolume).
inline bool context_allowed(const char* c) {
    if (!c) return false;
    const char* ok[] = { "crime_suppressMeleeStealthHitReaction", "crime_ignoredCorpse", "crime_ignoredUnconsciousBody",
                         "crime_ignoredNPCHitVolume", "crime_ignoredHorseTheft_Horse" };
    for (const char* k : ok) if (std::strcmp(c, k) == 0) return true;
    return false;
}

// Engine names only (the agent validates too).
inline bool is_engine_name(const char* s, size_t n) {
    if (!s || n < 1 || n > 63) return false;
    for (size_t i = 0; i < n; ++i) {
        const char c = s[i];
        if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_')) return false;
    }
    return true;
}

} // namespace kcdmp::wo139rules
