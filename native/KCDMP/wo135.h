// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-135: the avatar is seen, never heard (docs/WO-135-findings.md Phase 1).
//
// Every dialogue, bark and monologue on this build starts in one DialogModule
// function -- the one that prints "New dialogue '%s' is starting. Params:
// souls = '%s' ..." -- with the request in rdx. The request lists its speakers
// ("Ex", executors) at request+0x58..+0x60 and its listeners ("Nx") at
// +0x70..+0x78, each an 8-byte soul id (the builder of that log line resolves
// them through the soul manager to the names it prints). The function's own
// failures return 0 before anything starts.
//
// The gate: an entry hook that returns 0 -- the function's own "not started"
// -- when an avatar's soul is among the SPEAKERS. Listeners are left alone (an
// NPC may still shout at the avatar). No script context reaches these: the
// combat shouts (COMBAT_VICTIM_SCREAM_RECEIVED_HIT and the rest) are quest
// dialogues cast by metarole, and speech_mute / RestrictDialog did not stop
// them (observed, WO-135 run H1: each still loaded its voice file).
//
// Anchored by the string's single reference, the function start by its unwind
// entry, and the exact 20 prologue bytes; any miss = not armed (logged).

#include <cstdint>

namespace kcdmp::wo135 {

// Call once from a thread that is not the game's main thread (the patch
// suspends every other thread while it writes).
void install();
bool armed();

// The avatars whose dialogues are refused (motion.cpp, the quiet group
// "speech"). `soul` is the avatar's soul; its id is read at soul+0x40.
void set_speaker_blocked(void* soul, bool on);

// "wo135 dialog_gate=armed|off blocked=<n> refused=<n> seen=<n>"
int status_text(char* out, int n);

} // namespace kcdmp::wo135
