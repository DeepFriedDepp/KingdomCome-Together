// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// A copy that walks is DRIVEN (WO-221 C, mp_drive; docs/DESIGN-AUDIT.md 4.2, docs/WO-220-findings.md 2 and 6).
//
// Every frame the copy's own movement controller (EntityModule's C_ActorMovementController, the object the AI's proxy hands a
// movement request to every frame) is given a request made of its host NPC's streamed velocity plus a pull toward where the host's
// NPC is now. The engine's own walk then moves the body -- its step, its planted foot, its start and stop, the ground -- and the
// position writer does not write it.
//
// Who is driven (C.1): a bound, living, human copy on the ground whose host NPC walks or runs, that this machine's engine still
// updates (the actor's own "monster level of detail" bit is clear -- the engine's flag, read each frame, never a distance of ours),
// and that no seat, activity, fall, mount or one-shot holds (holders.h). Everyone else is the writer's. A body that is asked to
// walk and does not move (something is in the way, or the engine does not run it after all) is handed to the writer for a while.
//
// The arrival (C.2): when the host's NPC stops, the driver walks the body to within half a metre of the stop and gives it to the
// writer, which places the last half metre and the facing with its blend, never a snap. A body further than the snap distance
// behind is placed once by the writer and driven on.
//
// Where the call runs: RequestMovement is the call CryAction's CAIProxy::Update makes on the main thread once a frame for every AI
// body; ours is made on the same thread at the DLL's frame hook, after the proxy's and before the controller's next read (frame
// order measured in WO-116 and WO-220). The function merges the request field by field into the controller's own state: it takes
// no engine lock, allocates nothing (the actor-target flag is never set here) and leaves no plan in flight.
#include <cstdint>

namespace kcdmp::bodydrive {

enum class Out : uint8_t {
    Writer = 0,   // not the driver's this frame: the writer writes it
    Driven = 1,   // the engine moves it by our request: nothing writes it
    Snap = 2,     // left behind: the writer places it once, without a blend, and it is driven on
    Arrive = 3,   // at its host NPC's stop: the writer takes it from where it stands (the last half metre and the facing, blended)
};

struct In {
    uint32_t eid = 0;
    void* ent = nullptr;
    const char* name = nullptr;
    float render[4]{};       // the stream's pose at the play-out time (x, y, z, yaw)
    float newest[4]{};       // the stream's newest sample
    float newestAgeS = 0;    // how old the newest sample is
    float vx = 0, vy = 0;    // the host NPC's planar velocity
    float mps = 0;           // its planar speed (0 when the stream says it stopped)
    float vz = 0;            // its vertical speed (a fall, a jump)
    bool figure = false, beast = false, parented = false, human = true, physics = true, fight = false;
    double now = 0;
};

// The writer asks before it writes a bound body it holds (main thread, the frame hook).
Out frame(const In& in);
// The body left the driver (dropped, held by something else): its request is ended (no move target, no speed).
void release(uint32_t eid, const char* why);
// This body's gait, start and stop are the engine's own this frame (the gait inputs and the owner's walk family stay off it).
bool driven(uint32_t eid);
bool armed();

} // namespace kcdmp::bodydrive
