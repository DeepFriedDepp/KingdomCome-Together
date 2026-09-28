#pragma once
// WO-138: engine-free rules of the native NPC sender and the pause levers
// (native/KCDMP/wo138.cpp; docs/WO-138-findings.md). Pure functions, so the
// native unit tests (tests/wo138_rules_tests.cpp) check exactly what ships.
//
// The sender's rules are the Lua sender's (kdcmp.lua KCD2MP_NpcSyncTick), moved
// to the DLL's frame hook so they keep running whatever the Lua timers do:
//   * the flags: 1 dead, 2 KO, 4 drawn, 8 swing cue, 32 engaged, 128 not human;
//   * engaged = drawn, alive, not KO, within 12 m (2D) of the local player;
//   * the swing cue = the local player's health just dropped and this drawn
//     NPC is within 4 m (2D);
//   * the cull: past the cull radius from every anchor an NPC is not sent,
//     except inside the far band (a shared world: 150 m), where it still goes
//     out on its heartbeat and on a death / KO change; engaged is never culled;
//   * the send gate: moved more than moveEps on any axis, health changed by
//     more than 0.5, the heartbeat, a KO / drawn / engaged change, a swing
//     cue, or the first dead sample.

#include <cmath>
#include <cstdint>

namespace kcdmp::wo138rules {

constexpr uint8_t kFlagDead     = 0x01;
constexpr uint8_t kFlagKo       = 0x02;
constexpr uint8_t kFlagDrawn    = 0x04;
constexpr uint8_t kFlagSwing    = 0x08;
constexpr uint8_t kFlagEngaged  = 0x20;
constexpr uint8_t kFlagNotHuman = 0x80;

constexpr float kSwingCueRangeM = 4.0f;   // the Lua sender's 16 m^2

struct Config {
    uint16_t emitMs      = 100;
    uint16_t heartbeatMs = 2000;
    float    moveEps     = 0.05f;
    bool     cull        = true;
    float    cullRadius  = 60.0f;
    float    farBand     = 0.0f;    // 0 = no far band (not a shared world)
    float    engageRange = 12.0f;
    uint16_t attrMs      = 200;     // health / dead / KO / drawn refresh (round robin)
};

struct Sample {
    float x = 0, y = 0, z = 0, rot = 0;
    float hp = -1.0f;               // -1 = unreadable (the Lua sender's value too)
    bool  dead = false, ko = false, drawn = false, notHuman = false;
};

// What went out last for one NPC (the Lua tracked entry's t.last* / t.sent*).
struct Sent {
    bool   any = false;
    float  x = 0, y = 0, z = 0, hp = -1.0f;
    double at = 0;
    bool   dead = false, ko = false, drawn = false, engaged = false;
};

inline float dist2d(float ax, float ay, float bx, float by) {
    const float dx = ax - bx, dy = ay - by;
    return std::sqrt(dx * dx + dy * dy);
}

inline bool engaged(const Sample& s, bool havePlayer, float px, float py, float range) {
    return s.drawn && !s.dead && !s.ko && havePlayer && dist2d(s.x, s.y, px, py) <= range;
}

inline bool swing_cue(bool playerHit, const Sample& s, bool havePlayer, float px, float py) {
    return playerHit && s.drawn && havePlayer && dist2d(s.x, s.y, px, py) <= kSwingCueRangeM;
}

inline uint8_t flags(const Sample& s, bool swing, bool eng) {
    return static_cast<uint8_t>((s.dead ? kFlagDead : 0) | (s.ko ? kFlagKo : 0) | (s.drawn ? kFlagDrawn : 0) |
                                (swing ? kFlagSwing : 0) | (eng ? kFlagEngaged : 0) | (s.notHuman ? kFlagNotHuman : 0));
}

enum class Cull : uint8_t { Send = 0, FarBand = 1, Culled = 2 };

// dMin: the 2D distance to the nearest anchor (the local player and the pushed
// partner anchors); a huge value when there is no anchor at all.
inline Cull cull(float dMin, const Config& c, bool eng, bool heartbeatDue, bool firstDead, bool koChanged) {
    if (!c.cull || eng || dMin <= c.cullRadius) return Cull::Send;
    if (dMin <= c.farBand && (heartbeatDue || firstDead || koChanged)) return Cull::FarBand;
    return Cull::Culled;
}

inline bool heartbeat_due(const Sent& t, double now, const Config& c) {
    return !t.any || (now - t.at) * 1000.0 >= c.heartbeatMs;
}

inline bool should_send(const Sample& s, const Sent& t, bool eng, bool swing, double now, const Config& c) {
    if (!t.any) return true;
    const bool moved = std::fabs(s.x - t.x) > c.moveEps || std::fabs(s.y - t.y) > c.moveEps ||
                       std::fabs(s.z - t.z) > c.moveEps;
    const bool hpChanged = t.hp >= 0.0f && s.hp >= 0.0f && std::fabs(s.hp - t.hp) > 0.5f;
    return moved || hpChanged || heartbeat_due(t, now, c) || s.ko != t.ko || s.drawn != t.drawn ||
           eng != t.engaged || swing || (s.dead && !t.dead);
}

// Round robin: how many tracked NPCs get their health / life / drawn state
// refreshed this tick so every one is refreshed once per attrMs.
inline int attr_batch(int tracked, const Config& c) {
    if (tracked <= 0) return 0;
    const int ticks = c.emitMs ? (c.attrMs + c.emitMs - 1) / c.emitMs : 1;
    const int per = ticks > 1 ? (tracked + ticks - 1) / ticks : tracked;
    return per < 1 ? 1 : per;
}

// ---- the frame meter -------------------------------------------------------
// The frame hook gets the game's frame time (C_ModulesManager::Update's dt).
// Over a window of real time, the ratio game/real says whether this machine's
// world runs (the Apse inventory divides the time scale; a PauseGame source
// stops the game timer).
enum class World : uint8_t { Running = 0, Slowed = 1, Frozen = 2 };

inline World classify(double gameS, double realS) {
    if (realS <= 0.0) return World::Running;
    const double r = gameS / realS;
    if (r < 0.05) return World::Frozen;
    if (r < 0.5) return World::Slowed;
    return World::Running;
}

inline uint16_t scale_permille(double gameS, double realS) {
    if (realS <= 0.0) return 1000;
    double r = gameS / realS * 1000.0;
    if (r < 0) r = 0;
    if (r > 60000) r = 60000;
    return static_cast<uint16_t>(r + 0.5);
}

// ---- the PauseGame gate (CCryAction::PauseGame, sources in wo138.cpp) ------
constexpr uint16_t kSrcScriptBind  = 2;
constexpr uint16_t kSrcVideoMode   = 4;
constexpr uint16_t kSrcGameOver    = 5;
constexpr uint16_t kSrcInGameMenu  = 7;
constexpr uint16_t kSrcCount       = 13;
constexpr uint32_t kDefaultMask    = 1u << kSrcInGameMenu;   // only the ESC menu

// A pause request is declined only while the levers are on (a session with a
// partner), only a PAUSE (a resume always runs), only for a source in the mask.
inline bool decline_pause(bool leversOn, bool pause, uint16_t source, uint32_t mask) {
    return leversOn && pause && source < 32 && ((mask >> source) & 1u) != 0;
}

} // namespace kcdmp::wo138rules
