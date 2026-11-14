// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// Who holds a copy's body (WO-221 C.3, mp_holders; docs/DESIGN-AUDIT.md 4.10: "one holder at a time, in one table").
//
// One table, one holder per body at any moment: the driver (the engine moves it by our movement request), the writer (the stream
// places it), a seat, an activity, a fall, a mount, a one-shot. Whoever is not the holder does not set the body's position; a
// hand-over is one entry changing, in one frame, and the new holder starts from where the body stands.
//
// The claims are still found where they always were (the seat rule, the posture rule, the activity hold, the knockdown, a swing's
// hold, the stream's own dead / unconscious / carried bits, a parent); what this replaces is the six separate "who yields" rules of
// the position writer: decide() is the one rule, in one order.
#include <cstdint>

namespace kcdmp::holders {

enum class Holder : uint8_t { None = 0, Writer, Driver, Seat, Activity, Fall, Mount, OneShot, Count };
const char* name(Holder h);

struct Claims {
    bool fall = false;       // dead, unconscious or carried (the stream's bits), or lying for its host NPC's knockdown: the engine's ragdoll / the carrier
    bool mount = false;      // a parent holds it: a horse, a cart's seat
    bool oneShot = false;    // a swing's hold runs: the one-shot's own motion
    bool seat = false;       // its seat holds it (the seat rule's word)
    bool activity = false;   // an activity or a posture holds it
    bool probe = false;      // a research probe owns it (the bench only)
};

// The one rule (pure): the first claim in this order holds the body; with none, the stream does (the writer, or the driver when the
// body walks and can be driven -- the driver asks the table and takes the body from the writer in the writer's own loop).
inline Holder decide(const Claims& c) {
    if (c.fall) return Holder::Fall;
    if (c.mount) return Holder::Mount;
    if (c.oneShot || c.probe) return Holder::OneShot;
    if (c.seat) return Holder::Seat;
    if (c.activity) return Holder::Activity;
    return Holder::Writer;
}

bool on();                                                          // mp_holders (the bench's twin: the writer's own six rules, as 0.65.0)
// Main thread. The body's holder this frame; returns the holder it had before (a change is a hand-over: counted, the first logged).
Holder set(uint32_t eid, const char* npc, Holder h, double now);
Holder of(uint32_t eid);                                            // None: not a bound copy
void forget(uint32_t eid);                                          // the copy is unbound
// May <who> set this body's position now? (every native position setter asks; a body the table does not know is nobody's)
inline bool may_place(uint32_t eid, Holder who) { const Holder h = of(eid); return h == Holder::None || h == who; }

} // namespace kcdmp::holders
