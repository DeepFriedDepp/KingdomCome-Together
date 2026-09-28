#pragma once
// WO-137: shared quests -- the native half (docs/WO-137-findings.md).
//
// Quest progress is concept-graph state: every quest <State> node is a
// conceptmodule C_StateVariable holding an rttr::variant (node+0x68). The
// engine changes it in one place, the setter (ConceptModule +0x29C870 on
// 1.5.5), which compares old and new and -- only when they differ -- calls the
// node's own vftable slot 42 (+0x150) OnStateChanged(old, new, notify); every
// consumer of the change (the objective the journal shows, the On<Value>
// edges) runs from there. A State's Set<Value> in-port (C_InputTriggerPort)
// reaches the setter through C_Node::Execute -> slot 33 (+0x108) ExecuteNode.
//
// So:
//   * the DETECTOR is two slots of C_StateVariable's vftable: slot 33 notes the
//     triggering port, slot 42 records (path, port, old, new) in the engine's
//     own order -- real changes only, every quest, main and side;
//   * the APPLY is the engine's own entry: C_InputTriggerPort::Trigger (vftable
//     slot 15) on the State's Set<Value> port -- the same C_Node::Execute call
//     the graph makes when an edge fires into it, so every consumer runs.
//
// One pipe request, 0x23 [op][...], answered with 0x9D [ok][seq][op][reason][payload]:
//   op 1 Config    [detect:1][role:1][flags:1]    -> []           role 0 none, 1 host, 2 joiner;
//                  flags 1 = the joiner's time gate (quest time sets are the host's: see below)
//   op 2 Apply     [tag:4][pathLen:2][path][portLen:1][port]
//                  -> [result:1][oldOk:1][old:4][newOk:1][new:4][typeLen:1][type]
//   op 3 ReadState [count:1]{[pathLen:2][path]}*   -> [count:1]{[found:1][runtime:1][ok:1][val:4]}*
//   op 4 ReadQuest [pathLen:2][path]
//                  -> [found:1][level:4][count:1]{[nameLen:1][name][type:1][log:1][order:2][last:8]}*
//   op 5 Status    []                              -> [text]
//   op 6 Hud       [on:1]                          -> []           research: the HUD sink proxy
//   op 7 Hold      [on:1]                          -> []           detector records but does not send (a load)
// and one unsolicited frame per recorded change:
//   0x9E QuestChange [seq:4][flags:1][old:4][new:4][portLen:1][port][typeLen:1][type][pathLen:2][path][questLen:2]
//        flags: 1 notify, 2 mirror (caused while an Apply ran), 4 old ok, 8 new ok,
//               16 cascade (made by another State's consumers, not by the world); questLen = the quest
//               root's prefix of path (only States under a C_Quest are recorded)
//
// The TIME GATE (joiner, shared world): a quest's time set is a C_Function
// node calling wh::rpgmodule::AdvanceWorldTime / wh::conceptmodule::PassLongTime
// (179 + 5 nodes in the quests, data-verified); 141 continue a chain through
// their OnExec. With the gate on, C_Function's invoke (vftable slot 12) returns
// the empty variant the engine's own skip path returns and runs nothing --
// OnExec still fires, the chain goes on, the clock does not move: only the
// host's clock moves the world (WO-133); the host's jump reaches the joiner
// through the time sync.
//
// Research (solo): kcdmp-quest.txt in the game folder, one command per line,
// run once per content change:
//   read <questPath> [objective ...]     Q1: the exported C_Quest / C_Objective getters
//   state <nodePath>                      a State's current value
//   pulse <nodePath> <port> FIRE          Q2: one Set<Value> pulse (refused in a session / with an agent)
//   detect on|off                         log every State change
//   hud on|off                            Q5: the pass-through proxy on the quest HUD sink
//
// Everything fails closed: an anchor that does not match leaves that piece off
// (WO137-BUILD lists what armed); nothing is patched that was not verified.

#include <cstddef>
#include <cstdint>

namespace kcdmp::wo137 {

constexpr uint8_t kOpConfig    = 1;
constexpr uint8_t kOpApply     = 2;
constexpr uint8_t kOpReadState = 3;
constexpr uint8_t kOpReadQuest = 4;
constexpr uint8_t kOpStatus    = 5;
constexpr uint8_t kOpHud       = 6;
constexpr uint8_t kOpHold      = 7;

constexpr uint8_t kROk         = 0;
constexpr uint8_t kRBadRequest = 1;
constexpr uint8_t kRUnarmed    = 2;
constexpr uint8_t kRFailed     = 3;

// Apply results (payload byte 0 of op 2's reply).
enum class Applied : uint8_t {
    Changed = 0,      // the value moved; its consumers ran
    Unchanged = 1,    // the State already had that value (the engine's own no-op)
    NoNode = 2,
    NotAState = 3,
    NoPort = 4,
    PortRefused = 5,  // not an In trigger port of the State's own class
    Asleep = 6,       // the node is hibernated / inactive here (the engine would refuse)
    Fault = 7,
    Unarmed = 8,
    NotAQuest = 9,    // not under a C_Quest: never pulsed
};

// Flags on 0x9E.
constexpr uint8_t kFNotify = 1, kFMirror = 2, kFOldOk = 4, kFNewOk = 8, kFCascade = 16;
constexpr uint8_t kCfgTimeGate = 1;

// Off the main thread: resolve anchors, patch the two vftable slots (the
// detector stays silent until Config or the research file turns it on).
void install();

// Every frame (main thread): the research file, the change queue to the agent.
void tick();

// The pipe (main thread, marshalled by the pipe server).
uint8_t handle(const uint8_t* req, size_t n, uint8_t* out, size_t cap, size_t* outN);

// The pipe closed: detection off, the queue dropped, the HUD proxy removed.
void on_disconnect();

// The pipe server's sender for the unsolicited 0x9E frames.
void set_send_callback(void (*fn)(const uint8_t* body, uint16_t len));

} // namespace kcdmp::wo137
