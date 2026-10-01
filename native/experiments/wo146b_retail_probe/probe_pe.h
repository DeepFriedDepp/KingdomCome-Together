// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-146B research probe -- the anchor toolkit, over ONE loaded module.
//
// Same idea as the shipped plugin's native/KCDMP/anchors.h (re-find engine code
// by what it IS, never by an RVA), rewritten here as a standalone file so the
// probe links nothing from the shipped DLL and can log a thread id from inside
// every scan. Retail is a single 89 MB WHGame.dll instead of 40 module DLLs,
// so every entry point takes the module explicitly and the scans are written
// to survive a 60 MB .text.
//
// Everything is a READ of the loaded image. The only writes this probe makes
// are the two capture hooks, and they live in probe_hook.cpp.

#include <windows.h>
#include <cstdint>
#include <cstddef>

namespace wo146b::pe {

struct Range {
    const uint8_t* begin = nullptr;
    const uint8_t* end   = nullptr;
    size_t size() const { return begin && end ? static_cast<size_t>(end - begin) : 0; }
    bool contains(const void* p) const {
        auto b = static_cast<const uint8_t*>(p);
        return b >= begin && b < end;
    }
};

// A named section of a loaded module.
bool section(HMODULE mod, const char* name, Range* out);

// Module base and SizeOfImage.
bool image(HMODULE mod, Range* out);

// The address of an exact NUL-terminated string (preceded by a NUL or the
// section start) in .rdata / .data / _RDATA. `count` receives the number of
// exact copies; the first is returned.
const char* find_cstring(HMODULE mod, const char* s, int* count = nullptr);

// Same, but matching only the END of a NUL-terminated string (the string must
// also start after a NUL). Used where the literal we know is a suffix of the
// one the build actually embeds.
const char* find_cstring_suffix(HMODULE mod, const char* suffix, int* count = nullptr);

// Every occurrence of a byte pattern in .text. Returns the number found (up to
// `max`), writing the first `max` into `out`.
int find_bytes(HMODULE mod, const uint8_t* pat, size_t n, const uint8_t** out, int max);

// RTTI: the vftable of `decorated` (e.g. ".?AVC_Actor@entitymodule@wh@@") for
// the sub-object at `colOffset`. Null when absent or ambiguous; `count`
// receives how many matched.
void* const* find_vftable(HMODULE mod, const char* decorated, uint32_t colOffset = 0,
                          int* count = nullptr);
// Does this RTTI type exist at all (type descriptor present)?
bool has_rtti(HMODULE mod, const char* decorated);

// .pdata: the primary function range holding `addr`, following
// UNW_FLAG_CHAININFO back to the root.
bool function_range(HMODULE mod, const void* addr, Range* out);

// The primary entry of the one function that references the exact string `s`
// through a `lea r64,[rip+disp32]`. Null unless exactly one function does;
// `count` receives the number of distinct referencing functions.
const uint8_t* function_by_string(HMODULE mod, const char* s, int* count = nullptr);
// As above for an address rather than a string.
const uint8_t* function_by_ref(HMODULE mod, const void* target, int* count = nullptr);

// Does the function whose primary entry holds `fn` (every fragment chained to
// it) contain this byte pattern / a RIP-relative reference to `target`?
bool function_has_bytes(HMODULE mod, const void* fn, const uint8_t* pat, size_t n);
bool function_refs(HMODULE mod, const void* fn, const void* target);
// Does the function contain a direct `call rel32` (E8) to `target`?
bool function_calls_direct(HMODULE mod, const void* fn, const void* target);

// The address of the first occurrence of `pat` in the function; null when
// absent. `fragments` (optional) receives how many fragments were walked.
const uint8_t* function_find_bytes(HMODULE mod, const void* fn, const uint8_t* pat, size_t n);

// For an instruction at `insn` whose RIP-relative disp32 starts at
// `dispOffset` and whose length is `insnLen`, the absolute address it names.
const void* rip_target(const uint8_t* insn, size_t dispOffset = 3, size_t insnLen = 7);

// The first direct `call rel32` (E8) at or after `from`, within `window`
// bytes: returns its target, and *at (optional) the call instruction.
const uint8_t* first_call_after(const uint8_t* from, size_t window, const uint8_t** at = nullptr);

// "<module>+0xRVA" for logs.
void describe(HMODULE mod, const void* p, char* out, size_t n);

// Safe reads. False on a fault instead of taking the process down.
bool read_ptr(const void* base, size_t off, void** out);
bool read_u32(const void* base, size_t off, uint32_t* out);
bool read_f32(const void* base, size_t off, float* out);
bool read_bytes(const void* src, void* dst, size_t n);

} // namespace wo146b::pe
