// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-202 -- crashes, quests that fail on their own, desync (docs/WO-202-findings.md). The native half; the pure rules are wo202_rules.h,
// the agent's half GameBridge.Wo202*.cs. Every mechanism has its own bit in the agent's switch word (op 88), all on until the agent says.
//
// Pipe ops in the WO-163 family (0x2B -> 0xAC), main thread:
//   87 Status -> text
//   88 Config [bits:4] -> [bits:4]   1 crash_capture, 2 near_safe, 4 quest_fail_trace, 16 anim_shown, 32 recoil_clinch, 64 fight_pos_fresh,
//        512 death_engine_only
//   89 FailPoll [lastSeq:4] -> [more:1] then one record: [seq:4][ageMs:4][old:4][new:4][cascade:1][ranMs:4][pathLen:2][path][portLen:1]
//        [port][clsLen:1][cls][srcPathLen:2][srcPath][srcPortLen:1][srcPort]   II.4 quest_fail_trace (host): a quest step that failed, was
//        aborted or cancelled, or set a "player bailed" value, with the node whose out-port pulse made it (a timer, a watch, a trigger, a
//        behaviour, a conversation) and how long that node had run since it was last started. WO202-QUESTFAIL.
#pragma once
#include <cstddef>
#include <cstdint>

namespace kcdmp::wo202 {

constexpr uint8_t kOpStatus = 87, kOpConfig = 88, kOpFailPoll = 89;

void install();
void tick();      // main thread, every frame
uint8_t handle(uint8_t op, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen);
int status_text(char* out, int n);
bool on(uint32_t bit);   // wo202_rules.h kBit*

// II.4: C_Node::Execute (wo137's node hook, any thread): when each node last started
void note_exec(const void* node);
// II.4: wo137's change detector, on the thread the State changed on (inside the engine's own call)
void note_change(const char* path, const char* port, int32_t oldI, int32_t newI, uint32_t tid, bool cascade);

} // namespace kcdmp::wo202
