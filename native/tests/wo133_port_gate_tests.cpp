// WO-133 (hazard H3): engine-free checks of the file-armed port trigger's
// session gate (native/KCDMP/port_gate.h). Linked into KCDMP_NativeTests;
// wo133_port_gate_tests() returns the number of failures.
#include <cstdio>
#include <cstring>

#include "port_gate.h"

using namespace kcdmp::conceptread;

namespace {
int g_fail = 0, g_pass = 0;
#define PCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo133_port_gate_tests(int* passed) {
    PCHECK(port_trigger_gate(false, false) == PortGate::Allowed, "solo, no agent: the research trigger still works");
    PCHECK(port_trigger_gate(true, true) == PortGate::Session, "a session with the agent attached: refused");
    PCHECK(port_trigger_gate(true, false) == PortGate::Session, "SetSession still on after a pipe drop race: refused");
    PCHECK(port_trigger_gate(false, true) == PortGate::Agent, "an agent attached before its first heartbeat: refused");
    PCHECK(std::strstr(port_gate_text(PortGate::Session), "session") != nullptr, "the refusal names the session");
    PCHECK(std::strstr(port_gate_text(PortGate::Agent), "agent") != nullptr, "the refusal names the agent");
    if (passed) *passed = g_pass;
    return g_fail;
}
