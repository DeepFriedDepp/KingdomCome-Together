// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-148: engine-free checks of the inline hooks' boundary check (native/KCDMP/x64_len.h),
// of every shipped hook's expected prologue (native/KCDMP/hook_prologues.h), and of the
// native log's thread-id column (native/KCDMP/log.h).
// Linked into KCDMP_NativeTests; wo148_x64_tests() returns the number of failures.
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#include "hook_prologues.h"
#include "log.h"
#include "x64_len.h"

using namespace kcdmp;

namespace {
int g_fail = 0, g_pass = 0;
#define XCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)

const char* const kVectors[] = {
#include "wo148_x64_vectors.inc"
};

struct Vec { std::vector<uint8_t> code; int len; bool rip, rel, ctl; };

bool parse(const char* s, Vec* v) {
    const char* bar = std::strchr(s, '|');
    if (!bar) return false;
    for (const char* p = s; p + 1 < bar; p += 2) {
        char h[3] = {p[0], p[1], 0};
        v->code.push_back(static_cast<uint8_t>(std::strtoul(h, nullptr, 16)));
    }
    v->len = std::atoi(bar + 1);
    const char* f = std::strchr(bar + 1, '|');
    if (!f) return false;
    v->rip = std::strchr(f, 'r') != nullptr;
    v->rel = std::strchr(f, 'b') != nullptr;
    v->ctl = std::strchr(f, 'c') != nullptr;
    return true;
}

// Encodings this decoder refuses on purpose: EVEX (62), XOP (8F with reg != 0), 3DNow! (0F 0F).
bool deliberately_refused(const std::vector<uint8_t>& c) {
    size_t i = 0;
    while (i < c.size() && (c[i] == 0x66 || c[i] == 0x67 || c[i] == 0xF2 || c[i] == 0xF3 || c[i] == 0xF0 ||
                            c[i] == 0x2E || c[i] == 0x36 || c[i] == 0x3E || c[i] == 0x26 || c[i] == 0x64 || c[i] == 0x65)) ++i;
    if (i < c.size() && (c[i] & 0xF0) == 0x40) ++i;
    if (i >= c.size()) return false;
    if (c[i] == 0x62) return true;
    if (c[i] == 0x8F && i + 1 < c.size() && ((c[i + 1] >> 3) & 7) != 0) return true;
    if (c[i] == 0x0F && i + 1 < c.size() && c[i + 1] == 0x0F) return true;
    return false;
}

x64::Boundary verdict(std::initializer_list<uint8_t> bytes, size_t len) {
    std::vector<uint8_t> b(bytes);
    b.resize(b.size() + 16, 0xCC);
    return x64::check_patch(b.data(), b.size(), len).verdict;
}
} // namespace

int wo148_x64_tests(int* passed) {
    // ---- the decoder against capstone --------------------------------------------
    {
        int agree = 0, capDecoded = 0, gaps = 0, lenient = 0, mismatch = 0, shown = 0;
        for (const char* s : kVectors) {
            Vec v;
            if (!parse(s, &v)) { XCHECK(false, "vector unparsable: %s", s); continue; }
            const x64::Insn in = x64::decode(v.code.data(), v.code.size());
            if (v.len == 0) { if (in.len) ++lenient; continue; }
            ++capDecoded;
            if (in.len == 0) {
                if (!deliberately_refused(v.code)) {
                    ++gaps;
                    if (shown++ < 12) std::printf("  note: refused what capstone decodes: %s\n", s);
                }
                continue;
            }
            // A control transfer is refused wherever it sits in a patch, so only its flag matters
            // (capstone and the SDM disagree on ud0/ud1's ModRM); every other length must match.
            const bool transfer = v.ctl || in.controlFlow;
            bool ok = transfer ? in.controlFlow && (!v.rel || in.relBranch)
                               : in.len == v.len && in.ripRelative == v.rip;
            if (!ok) {
                ++mismatch;
                if (mismatch <= 20)
                    std::printf("  MISMATCH %s -> len %u rip %d rel %d ctl %d\n", s, in.len, in.ripRelative, in.relBranch, in.controlFlow);
            } else ++agree;
        }
        XCHECK(mismatch == 0, "every length the decoder gives equals capstone's, with the same RIP-relative and branch flags (%d mismatches of %d)", mismatch, capDecoded);
        XCHECK(capDecoded > 3000, "enough decodable vectors (%d)", capDecoded);
        XCHECK(gaps * 100 <= capDecoded * 3, "the decoder covers at least 97%% of what capstone decodes (%d gaps of %d)", gaps, capDecoded);
        std::printf("  x64: %d vectors capstone decodes, %d agree, %d refused (fail closed), %d decoded that capstone refuses\n",
                    capDecoded, agree, gaps, lenient);
    }

    // ---- every shipped hook's expected prologue passes -------------------------------
    for (const auto& h : hookpro::kAll) {
        std::vector<uint8_t> b(h.bytes, h.bytes + h.len);
        b.resize(b.size() + 16, 0xCC);   // what follows the copied bytes does not matter
        const x64::BoundaryResult r = x64::check_patch(b.data(), b.size(), h.len);
        char text[160];
        x64::describe(r, h.len, text, sizeof text);
        XCHECK(r.verdict == x64::Boundary::Ok, "%s: %s", h.name, text);
    }
    XCHECK(sizeof hookpro::kAll / sizeof hookpro::kAll[0] == 7, "seven inline hooks in the shipped DLL");

    // ---- the refusals ------------------------------------------------------------
    // mov [rsp+8],rbx; mov [rsp+18h],rsi; mov [rsp+20h],rdi = ends 5/10/15
    XCHECK(verdict({0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x74, 0x24, 0x18, 0x48, 0x89, 0x7C, 0x24, 0x20}, 15) == x64::Boundary::Ok, "15 on the boundary");
    // WO-146B's crash: a blanket 18 cut the instruction that ends at 20
    XCHECK(verdict({0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x74, 0x24, 0x18, 0x48, 0x89, 0x7C, 0x24, 0x20, 0x48, 0x83, 0xEC, 0x20, 0x55}, 18) == x64::Boundary::NotOnBoundary,
           "a length inside an instruction is refused (the WO-146B crash)");
    {
        std::vector<uint8_t> b = {0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x74, 0x24, 0x18, 0x48, 0x89, 0x7C, 0x24, 0x20, 0x48, 0x83, 0xEC, 0x20};
        b.resize(b.size() + 16, 0xCC);
        const x64::BoundaryResult r = x64::check_patch(b.data(), b.size(), 18);
        char text[160];
        x64::describe(r, 18, text, sizeof text);
        XCHECK(r.count == 4 && r.ends[3] == 19 && r.at == 19, "the refusal names where the instructions end (%s)", text);
        XCHECK(std::strstr(text, "ends 5/10/15/19") != nullptr && std::strstr(text, "inside an instruction") != nullptr, "describe: %s", text);
    }
    // mov [rsp+8],rbx; mov rax,[rip+disp32]; sub rsp,20h -- RIP-relative inside
    XCHECK(verdict({0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x8B, 0x05, 0x10, 0x20, 0x30, 0x40, 0x48, 0x83, 0xEC, 0x20}, 16) == x64::Boundary::RipRelative,
           "a RIP-relative operand inside is refused");
    // lea rcx,[rip+x] at the very end of the copy is still inside
    XCHECK(verdict({0x48, 0x83, 0xEC, 0x28, 0x48, 0x89, 0x5C, 0x24, 0x08, 0x90, 0x48, 0x8D, 0x0D, 1, 2, 3, 4}, 17) == x64::Boundary::RipRelative,
           "a lea [rip+] as the last copied instruction is refused");
    // sub rsp,28h; call rel32 -- a relative call inside
    XCHECK(verdict({0x48, 0x83, 0xEC, 0x28, 0xE8, 1, 2, 3, 4, 0x48, 0x89, 0x5C, 0x24, 0x08, 0x90}, 15) == x64::Boundary::Branch,
           "a relative call inside is refused");
    // a function that is already hooked (jmp [rip+0]; abs64) or jmps to another hook
    XCHECK(verdict({0xFF, 0x25, 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8}, 14) != x64::Boundary::Ok, "an already-patched jmp [rip+0] is refused");
    XCHECK(verdict({0xE9, 1, 2, 3, 4, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90}, 14) == x64::Boundary::Branch, "a jmp rel32 at the entry is refused");
    XCHECK(verdict({0x48, 0x83, 0xEC, 0x28, 0x74, 0x05, 0x48, 0x89, 0x5C, 0x24, 0x08, 0x90, 0x90, 0x90}, 14) == x64::Boundary::Branch, "a short jcc inside is refused");
    XCHECK(verdict({0x48, 0x83, 0xEC, 0x28, 0xC3, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90}, 14) == x64::Boundary::Branch, "a ret inside is refused");
    XCHECK(verdict({0x62, 0xF1, 0x7C, 0x48, 0x10, 0x04, 0x24, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90}, 14) == x64::Boundary::Undecodable, "EVEX is refused (fail closed)");
    XCHECK(verdict({0x90, 0x90}, 2) == x64::Boundary::TooShort, "a patch under 14 bytes is refused");
    // a 14-byte push/sub/mov run that the decoder must accept
    XCHECK(verdict({0x40, 0x53, 0x48, 0x83, 0xEC, 0x20, 0x48, 0x8B, 0xD9, 0x48, 0x8B, 0x49, 0x08, 0x90}, 14) == x64::Boundary::Ok, "push rbx; sub rsp,20h; mov rbx,rcx; mov rcx,[rcx+8]; nop");
    // VEX and SSE stores of the non-volatile xmm registers, as prologues spill them
    XCHECK(verdict({0x0F, 0x29, 0x74, 0x24, 0x40, 0xC5, 0xF8, 0x29, 0x7C, 0x24, 0x30, 0x44, 0x0F, 0x29, 0x44, 0x24, 0x20}, 17) == x64::Boundary::Ok,
           "movaps [rsp+40h],xmm6; vmovaps [rsp+30h],xmm7; movaps [rsp+20h],xmm8");

    // ---- the log line: the thread id in one fixed column ---------------------------
    {
        char a[64], b[64], c[64];
        const int na = format_log_prefix(a, sizeof a, 9, 5, 7, 42, 4u);
        const int nb = format_log_prefix(b, sizeof b, 23, 59, 59, 999, 14820u);
        const int nc = format_log_prefix(c, sizeof c, 0, 0, 0, 0, 999999u);
        XCHECK(na == kLogPrefixLen && nb == kLogPrefixLen && nc == kLogPrefixLen, "the prefix is always %d characters (%d %d %d)", kLogPrefixLen, na, nb, nc);
        XCHECK(std::strcmp(a, "[09:05:07.042] t000004 ") == 0, "prefix: '%s'", a);
        XCHECK(std::strcmp(b, "[23:59:59.999] t014820 ") == 0, "prefix: '%s'", b);
        XCHECK(a[15] == 't' && b[15] == 't' && c[15] == 't', "the thread id starts at column 16");
    }

    *passed = g_pass;
    return g_fail;
}
