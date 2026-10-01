// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-133 (hazard H3, docs/WO-126A-quest-map.md s6.3): the file-armed concept
// port trigger (kcdmp-concept.txt + FIRE, concept_read.cpp port_watch) is a
// solo research tool. It needed no session and no role, so on a host it could
// fire a quest port into the world the host saves. In a session it never
// fires: a session is the agent's SetSession heartbeat (respawn::session_active)
// or simply an agent attached to the pipe. Probe and read stay (read-only).
// Engine-free: native/tests/wo133_port_gate_tests.cpp pins it.

#include <cstdint>

namespace kcdmp::conceptread {

enum class PortGate : uint8_t { Allowed, Session, Agent };

inline PortGate port_trigger_gate(bool sessionOn, bool agentAttached) {
    if (sessionOn) return PortGate::Session;
    if (agentAttached) return PortGate::Agent;
    return PortGate::Allowed;
}

inline const char* port_gate_text(PortGate g) {
    switch (g) {
        case PortGate::Session: return "a session is running (SetSession on)";
        case PortGate::Agent:   return "an agent is attached to the pipe";
        default:                return "no session, no agent";
    }
}

} // namespace kcdmp::conceptread
