// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#include "probe_pe.h"

#include <cstdio>
#include <cstring>

namespace wo146b::pe {

namespace {

// No locals with destructors in anything that sits under __try (C2712).

const IMAGE_NT_HEADERS64* nt_headers(HMODULE mod) {
    auto* base = reinterpret_cast<const uint8_t*>(mod);
    auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (!dos || dos->e_magic != IMAGE_DOS_SIGNATURE) return nullptr;
    auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return nullptr;
    return nt;
}

struct Pdata {
    const RUNTIME_FUNCTION* first = nullptr;
    size_t count = 0;
};

bool pdata_of(HMODULE mod, Pdata* out) {
    const IMAGE_NT_HEADERS64* nt = nt_headers(mod);
    if (!nt) return false;
    const IMAGE_DATA_DIRECTORY& d = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION];
    if (!d.VirtualAddress || !d.Size) return false;
    out->first = reinterpret_cast<const RUNTIME_FUNCTION*>(
        reinterpret_cast<const uint8_t*>(mod) + d.VirtualAddress);
    out->count = d.Size / sizeof(RUNTIME_FUNCTION);
    return true;
}

// .pdata is sorted by BeginAddress: binary search.
const RUNTIME_FUNCTION* entry_for(const Pdata& p, uint32_t rva) {
    size_t lo = 0, hi = p.count;
    while (lo < hi) {
        const size_t mid = lo + (hi - lo) / 2;
        if (p.first[mid].BeginAddress <= rva) lo = mid + 1;
        else hi = mid;
    }
    if (lo == 0) return nullptr;
    const RUNTIME_FUNCTION* e = &p.first[lo - 1];
    return (rva >= e->BeginAddress && rva < e->EndAddress) ? e : nullptr;
}

const RUNTIME_FUNCTION* root_of(HMODULE mod, const RUNTIME_FUNCTION* e) {
    auto* base = reinterpret_cast<const uint8_t*>(mod);
    for (int guard = 0; guard < 16 && e; ++guard) {
        if (e->UnwindInfoAddress & 1) return e;             // not an unwind pointer
        auto* u = base + e->UnwindInfoAddress;
        const uint8_t verFlags = u[0];
        if (!((verFlags >> 3) & UNW_FLAG_CHAININFO)) return e;
        const uint8_t codes = u[2];
        auto* chained = reinterpret_cast<const RUNTIME_FUNCTION*>(
            u + 4 + 2 * ((codes + 1) & ~1));
        if (chained->BeginAddress == e->BeginAddress) return e;
        e = chained;
    }
    return e;
}

// Visit every fragment whose unwind chain leads to `root`. A linear .pdata
// walk: retail has 291,812 entries, which is ~2 ms per call -- acceptable for
// a probe that does this a few dozen times, and it is the only way to find the
// cold fragments PGO splits a function into.
template <class F>
bool for_each_fragment(HMODULE mod, const Pdata& p, const RUNTIME_FUNCTION* root, F&& visit) {
    auto* base = reinterpret_cast<const uint8_t*>(mod);
    Range r{base + root->BeginAddress, base + root->EndAddress};
    if (visit(r)) return true;
    for (size_t i = 0; i < p.count; ++i) {
        const RUNTIME_FUNCTION* e = &p.first[i];
        if (e->BeginAddress == root->BeginAddress) continue;
        const RUNTIME_FUNCTION* rr = root_of(mod, e);
        if (!rr || rr->BeginAddress != root->BeginAddress) continue;
        Range f{base + e->BeginAddress, base + e->EndAddress};
        if (visit(f)) return true;
    }
    return false;
}

bool guarded_section(HMODULE mod, const char* name, Range* out) {
    __try {
        const IMAGE_NT_HEADERS64* nt = nt_headers(mod);
        if (!nt) return false;
        const IMAGE_SECTION_HEADER* s = IMAGE_FIRST_SECTION(nt);
        for (WORD i = 0; i < nt->FileHeader.NumberOfSections; ++i, ++s) {
            char n8[9]{};
            std::memcpy(n8, s->Name, 8);
            if (std::strcmp(n8, name) != 0) continue;
            auto* base = reinterpret_cast<const uint8_t*>(mod);
            const DWORD sz = s->Misc.VirtualSize ? s->Misc.VirtualSize : s->SizeOfRawData;
            out->begin = base + s->VirtualAddress;
            out->end = out->begin + sz;
            return true;
        }
        return false;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

const char* scan_cstring(const Range& r, const char* s, size_t n, int* count) {
    const char* first = nullptr;
    int c = 0;
    __try {
        for (const uint8_t* p = r.begin; p + n + 1 <= r.end; ++p) {
            if (*p != static_cast<uint8_t>(s[0])) continue;
            if (p > r.begin && p[-1] != 0) continue;
            if (std::memcmp(p, s, n) != 0 || p[n] != 0) continue;
            if (!first) first = reinterpret_cast<const char*>(p);
            ++c;
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) { return nullptr; }
    if (count) *count += c;
    return first;
}

// The string ENDS with `suffix`; we return the start of the whole string.
const char* scan_cstring_suffix(const Range& r, const char* suffix, size_t n, int* count) {
    const char* first = nullptr;
    int c = 0;
    __try {
        for (const uint8_t* p = r.begin; p + n + 1 <= r.end; ++p) {
            if (std::memcmp(p, suffix, n) != 0 || p[n] != 0) continue;
            const uint8_t* s = p;
            int back = 0;
            while (s > r.begin && s[-1] != 0 && back < 512) { --s; ++back; }
            if (!first) first = reinterpret_cast<const char*>(s);
            ++c;
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) { return nullptr; }
    if (count) *count += c;
    return first;
}

int scan_bytes(const Range& r, const uint8_t* pat, size_t n, const uint8_t** out, int max) {
    int found = 0;
    __try {
        for (const uint8_t* p = r.begin; p + n <= r.end; ++p) {
            if (*p != pat[0] || std::memcmp(p, pat, n) != 0) continue;
            if (found < max && out) out[found] = p;
            ++found;
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) { return found; }
    return found;
}

// RTTI (x64): TypeDescriptor { void* vftable; void* spare; char name[] } in
// .data; CompleteObjectLocator { u32 sig(=1); u32 offset; u32 cdOffset;
// i32 tdRVA; i32 cdRVA; i32 selfRVA } in .rdata; the qword before a vftable is
// the COL's address.
void* const* scan_vftable(HMODULE mod, const Range& data, const Range& rdata,
                          const char* decorated, uint32_t colOffset, int* count) {
    __try {
        const size_t n = std::strlen(decorated);
        auto* base = reinterpret_cast<const uint8_t*>(mod);
        void* const* found = nullptr;
        int hits = 0;
        for (const uint8_t* p = data.begin; p + n + 1 <= data.end; ++p) {
            if (*p != static_cast<uint8_t>(decorated[0])) continue;
            if (std::memcmp(p, decorated, n) != 0 || p[n] != 0) continue;
            const uint8_t* td = p - 0x10;
            const uint32_t tdRva = static_cast<uint32_t>(td - base);
            for (const uint8_t* c = rdata.begin; c + 24 <= rdata.end; c += 4) {
                uint32_t col[6];
                std::memcpy(col, c, sizeof(col));
                if (col[0] != 1 || col[3] != tdRva || col[1] != colOffset) continue;
                if (col[5] != static_cast<uint32_t>(c - base)) continue;
                const uint64_t colVa = reinterpret_cast<uint64_t>(c);
                for (const uint8_t* v = rdata.begin; v + 16 <= rdata.end; v += 8) {
                    uint64_t q;
                    std::memcpy(&q, v, 8);
                    if (q != colVa) continue;
                    if (!found) found = reinterpret_cast<void* const*>(v + 8);
                    ++hits;
                }
            }
        }
        if (count) *count = hits;
        return (hits == 1) ? found : nullptr;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return nullptr; }
}

bool guarded_function_range(HMODULE mod, const void* addr, Range* out) {
    __try {
        Pdata p{};
        if (!pdata_of(mod, &p)) return false;
        auto* base = reinterpret_cast<const uint8_t*>(mod);
        const auto rva = static_cast<uint32_t>(static_cast<const uint8_t*>(addr) - base);
        const RUNTIME_FUNCTION* e = entry_for(p, rva);
        if (!e) return false;
        const RUNTIME_FUNCTION* r = root_of(mod, e);
        if (!r) return false;
        out->begin = base + r->BeginAddress;
        out->end = base + r->EndAddress;
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

enum class Scan { Bytes, Ref, Find };

bool guarded_scan_function(HMODULE mod, const void* fn, Scan kind,
                           const uint8_t* pat, size_t n, const uint8_t* target,
                           const uint8_t** found) {
    __try {
        Pdata p{};
        if (!pdata_of(mod, &p)) return false;
        auto* base = reinterpret_cast<const uint8_t*>(mod);
        const auto rva = static_cast<uint32_t>(static_cast<const uint8_t*>(fn) - base);
        const RUNTIME_FUNCTION* e = entry_for(p, rva);
        if (!e) return false;
        const RUNTIME_FUNCTION* root = root_of(mod, e);
        if (!root) return false;
        return for_each_fragment(mod, p, root, [&](const Range& fr) {
            if (kind == Scan::Ref) {
                for (const uint8_t* q = fr.begin; q + 7 <= fr.end; ++q) {
                    if ((q[2] & 0xC7) != 0x05) continue;
                    int32_t disp;
                    std::memcpy(&disp, q + 3, 4);
                    if (q + 7 + disp == target) return true;
                }
                return false;
            }
            for (const uint8_t* q = fr.begin; q + n <= fr.end; ++q) {
                if (*q != pat[0] || std::memcmp(q, pat, n) != 0) continue;
                if (found) *found = q;
                return true;
            }
            return false;
        });
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

// Every LEA r64,[rip+disp32] in .text naming `target`; distinct root functions.
const uint8_t* guarded_by_ref(HMODULE mod, const Range& text, const uint8_t* target,
                              int* count, bool leaOnly) {
    __try {
        Pdata p{};
        if (!pdata_of(mod, &p)) return nullptr;
        auto* base = reinterpret_cast<const uint8_t*>(mod);
        const uint8_t* roots[16]{};
        int nroots = 0;
        for (const uint8_t* q = text.begin; q + 7 <= text.end; ++q) {
            if (leaOnly) {
                if ((q[0] & 0xF8) != 0x48 || q[1] != 0x8D || (q[2] & 0xC7) != 0x05) continue;
            } else {
                if ((q[2] & 0xC7) != 0x05) continue;
            }
            int32_t disp;
            std::memcpy(&disp, q + 3, 4);
            if (q + 7 + disp != target) continue;
            const RUNTIME_FUNCTION* e = entry_for(p, static_cast<uint32_t>(q - base));
            const RUNTIME_FUNCTION* r = e ? root_of(mod, e) : nullptr;
            if (!r) continue;
            const uint8_t* rb = base + r->BeginAddress;
            bool seen = false;
            for (int i = 0; i < nroots; ++i) if (roots[i] == rb) { seen = true; break; }
            if (!seen && nroots < 16) roots[nroots++] = rb;
        }
        if (count) *count = nroots;
        return (nroots == 1) ? roots[0] : nullptr;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return nullptr; }
}

} // namespace

bool section(HMODULE mod, const char* name, Range* out) { return guarded_section(mod, name, out); }

bool image(HMODULE mod, Range* out) {
    const IMAGE_NT_HEADERS64* nt = nt_headers(mod);
    if (!nt) return false;
    out->begin = reinterpret_cast<const uint8_t*>(mod);
    out->end = out->begin + nt->OptionalHeader.SizeOfImage;
    return true;
}

const char* find_cstring(HMODULE mod, const char* s, int* count) {
    if (count) *count = 0;
    const char* names[] = {".rdata", "_RDATA", ".data"};
    const char* first = nullptr;
    const size_t n = std::strlen(s);
    for (const char* nm : names) {
        Range r{};
        if (!guarded_section(mod, nm, &r)) continue;
        const char* hit = scan_cstring(r, s, n, count);
        if (hit && !first) first = hit;
    }
    return first;
}

const char* find_cstring_suffix(HMODULE mod, const char* suffix, int* count) {
    if (count) *count = 0;
    const char* names[] = {".rdata", "_RDATA", ".data"};
    const char* first = nullptr;
    const size_t n = std::strlen(suffix);
    for (const char* nm : names) {
        Range r{};
        if (!guarded_section(mod, nm, &r)) continue;
        const char* hit = scan_cstring_suffix(r, suffix, n, count);
        if (hit && !first) first = hit;
    }
    return first;
}

int find_bytes(HMODULE mod, const uint8_t* pat, size_t n, const uint8_t** out, int max) {
    Range t{};
    if (!guarded_section(mod, ".text", &t)) return 0;
    return scan_bytes(t, pat, n, out, max);
}

void* const* find_vftable(HMODULE mod, const char* decorated, uint32_t colOffset, int* count) {
    Range data{}, rdata{};
    if (!guarded_section(mod, ".data", &data) || !guarded_section(mod, ".rdata", &rdata)) return nullptr;
    return scan_vftable(mod, data, rdata, decorated, colOffset, count);
}

bool has_rtti(HMODULE mod, const char* decorated) {
    int n = 0;
    Range data{};
    if (!guarded_section(mod, ".data", &data)) return false;
    scan_cstring(data, decorated, std::strlen(decorated), &n);
    return n > 0;
}

bool function_range(HMODULE mod, const void* addr, Range* out) {
    return guarded_function_range(mod, addr, out);
}

const uint8_t* function_by_string(HMODULE mod, const char* s, int* count) {
    if (count) *count = 0;
    const char* str = find_cstring(mod, s, nullptr);
    if (!str) return nullptr;
    return function_by_ref(mod, str, count);
}

const uint8_t* function_by_ref(HMODULE mod, const void* target, int* count) {
    Range text{};
    if (!guarded_section(mod, ".text", &text)) return nullptr;
    return guarded_by_ref(mod, text, static_cast<const uint8_t*>(target), count, true);
}

bool function_has_bytes(HMODULE mod, const void* fn, const uint8_t* pat, size_t n) {
    return guarded_scan_function(mod, fn, Scan::Bytes, pat, n, nullptr, nullptr);
}

bool function_refs(HMODULE mod, const void* fn, const void* target) {
    return guarded_scan_function(mod, fn, Scan::Ref, nullptr, 0,
                                 static_cast<const uint8_t*>(target), nullptr);
}

bool function_calls_direct(HMODULE mod, const void* fn, const void* target) {
    Range r{};
    if (!function_range(mod, fn, &r)) return false;
    __try {
        for (const uint8_t* q = r.begin; q + 5 <= r.end; ++q) {
            if (*q != 0xE8) continue;
            int32_t rel;
            std::memcpy(&rel, q + 1, 4);
            if (q + 5 + rel == target) return true;
        }
        return false;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

const uint8_t* function_find_bytes(HMODULE mod, const void* fn, const uint8_t* pat, size_t n) {
    const uint8_t* found = nullptr;
    if (!guarded_scan_function(mod, fn, Scan::Find, pat, n, nullptr, &found)) return nullptr;
    return found;
}

const void* rip_target(const uint8_t* insn, size_t dispOffset, size_t insnLen) {
    int32_t disp = 0;
    __try { std::memcpy(&disp, insn + dispOffset, 4); }
    __except (EXCEPTION_EXECUTE_HANDLER) { return nullptr; }
    return insn + insnLen + disp;
}

const uint8_t* first_call_after(const uint8_t* from, size_t window, const uint8_t** at) {
    __try {
        for (size_t i = 0; i < window; ++i) {
            if (from[i] != 0xE8) continue;
            int32_t rel;
            std::memcpy(&rel, from + i + 1, 4);
            if (at) *at = from + i;
            return from + i + 5 + rel;
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) { return nullptr; }
    return nullptr;
}

void describe(HMODULE mod, const void* p, char* out, size_t n) {
    if (!p) { std::snprintf(out, n, "(null)"); return; }
    char name[MAX_PATH]{};
    GetModuleFileNameA(mod, name, MAX_PATH);
    const char* slash = std::strrchr(name, '\\');
    const auto rva = static_cast<uintptr_t>(static_cast<const uint8_t*>(p) -
                                            reinterpret_cast<const uint8_t*>(mod));
    std::snprintf(out, n, "%s+0x%llX", slash ? slash + 1 : name,
                  static_cast<unsigned long long>(rva));
}

bool read_ptr(const void* base, size_t off, void** out) {
    __try { *out = *reinterpret_cast<void* const*>(static_cast<const char*>(base) + off); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool read_u32(const void* base, size_t off, uint32_t* out) {
    __try { *out = *reinterpret_cast<const uint32_t*>(static_cast<const char*>(base) + off); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool read_f32(const void* base, size_t off, float* out) {
    __try { *out = *reinterpret_cast<const float*>(static_cast<const char*>(base) + off); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool read_bytes(const void* src, void* dst, size_t n) {
    __try { std::memcpy(dst, src, n); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

} // namespace wo146b::pe
