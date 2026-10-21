// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-202 I.1 crash_capture: every crash leaves its evidence.
//
// Not one crash of the 2026-10-20 field sessions left a trace: no FAULT line from this DLL and no engine report in kcd.log -- the
// logs just stop. On a crash this writes, into the game folder's kcdmp-crashes\crash-<date>-<time>-<pid>\ (next to kcd.log):
//   * crash.dmp   a minidump (threads, thread info, unloaded modules, the memory the stacks point at);
//   * crash.txt   the exception, the faulting module and offset, the faulting thread (the main thread or a worker), its stack
//                 unwound frame by frame with the mod's own frames and hook pages marked, the mod addresses a scan of that stack
//                 finds, the registers, every hook this DLL placed, the last pipe requests and the last 200 native log lines.
// It never replaces the game's own handler (the Modding Tools build's BugSplat): the filter is chained -- ours writes, then the
// one that was installed before ours runs, exactly as without the mod. Another module that installs its filter later is chained
// too (the tick re-asserts ours every 5 s and keeps the newcomer as the one to call). The writing happens on a thread made at
// install, so a stack overflow (the faulting thread has almost no stack left) is caught the same way.
// Not catchable in-process: a fast-fail (__fastfail, /GS, heap-corruption termination) never reaches a filter; the log bundle
// collects the Windows error reports and the Application event log for those (the launcher's LogBundle).
// Switch: mp_crash_capture (default on; off = the filter passes every crash straight to the game's own).

#include <cstddef>
#include <cstdint>

namespace kcdmp::crash {

void install();                           // plugin_main, first thing: the writer thread, the filter, the module table
void tick();                              // main thread: re-asserts the filter every 5 s, refreshes the module table every 10 s
void note_pipe(uint8_t type, uint8_t op, uint16_t len);   // the pipe server: the request it is about to serve
void set_on(bool on);
bool on();
int status_text(char* out, int n);
// research only (KCDMP_TEST_CRASH): the main thread's frame hook faults outside every guard when this is true (once)
bool test_main_fault_due();

} // namespace kcdmp::crash
