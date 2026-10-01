// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-148: an x64 instruction-length decoder, for one question only: does an
// inline hook's patch length end on an instruction boundary of the exact bytes
// being patched, with no relative branch and no RIP-relative operand inside?
//
// Why: WO-146B patched three functions with one blanket length. It was on a
// boundary in the first and inside an instruction in the other two; the hooks
// installed cleanly and the game crashed the first time a fight ran them (the
// trampoline executed half an instruction). A byte compare against expected
// bytes cannot catch that: the expected bytes were simply copied too short.
//
// Scope: 64-bit mode, the legacy one-byte map, 0F, 0F38, 0F3A, and VEX (C4/C5).
// Anything it does not know -- EVEX, XOP, 3DNow!, opcodes invalid in 64-bit
// mode -- decodes as length 0, which the boundary check treats as a refusal:
// fail closed. Engine-free; native/tests/wo148_x64_tests.cpp checks it against
// capstone on generated vectors (tools/wo148/gen_x64_vectors.py).

#include <cstddef>
#include <cstdint>
#include <cstdio>

namespace kcdmp::x64 {

struct Insn {
    uint8_t len = 0;           // 0 = not decoded (unknown, unsupported or truncated)
    bool ripRelative = false;  // a [rip + disp32] memory operand
    bool relBranch = false;    // jmp/jcc/call/loop/jrcxz/xbegin with a relative target
    bool controlFlow = false;  // relBranch, or any other transfer: ret, int, iret, syscall, ud, hlt, call/jmp r/m
};

namespace detail {

// ModRM + SIB + displacement length (64-bit and 32-bit addressing share the layout).
// Returns false when the bytes run out.
inline bool modrm_len(const uint8_t* p, size_t avail, size_t at, size_t* used, bool* rip) {
    if (at >= avail) return false;
    const uint8_t m = p[at];
    const uint8_t mod = m >> 6, rm = m & 7;
    size_t n = 1;
    if (mod != 3) {
        if (rm == 4) {                       // SIB
            if (at + 1 >= avail) return false;
            const uint8_t sib = p[at + 1];
            n += 1;
            if (mod == 0 && (sib & 7) == 5) n += 4;   // no base: disp32
        } else if (mod == 0 && rm == 5) {
            n += 4;                          // RIP-relative disp32
            *rip = true;
        }
        if (mod == 1) n += 1;
        else if (mod == 2) n += 4;
    }
    *used = n;
    return at + n <= avail;
}

} // namespace detail

inline Insn decode(const uint8_t* p, size_t avail) {
    Insn out;
    size_t i = 0;
    bool opsize = false, addrsize = false, rep = false, repne = false;
    uint8_t rex = 0;

    // legacy prefixes (any order, at most 14 before the opcode)
    for (;;) {
        if (i >= avail || i >= 14) return Insn{};
        const uint8_t b = p[i];
        if (b == 0x66) { opsize = true; ++i; continue; }
        if (b == 0x67) { addrsize = true; ++i; continue; }
        if (b == 0xF3) { rep = true; ++i; continue; }
        if (b == 0xF2) { repne = true; ++i; continue; }
        if (b == 0xF0 || b == 0x2E || b == 0x36 || b == 0x3E || b == 0x26 || b == 0x64 || b == 0x65) { ++i; continue; }
        break;
    }
    // REX must be the last byte before the opcode
    if (i < avail && (p[i] & 0xF0) == 0x40) { rex = p[i]; ++i; }
    if (i >= avail) return Insn{};
    const bool rexW = (rex & 0x08) != 0;
    const size_t immZ = (opsize && !rexW) ? 2 : 4;   // Iz: 16 or 32 bits

    uint8_t op = p[i++];
    size_t imm = 0;
    bool modrm = false;
    size_t mlen = 0;
    bool rip = false;

    auto finish = [&](size_t at) -> Insn {
        size_t n = at;
        if (modrm) {
            if (!detail::modrm_len(p, avail, n, &mlen, &rip)) return Insn{};
            n += mlen;
        }
        n += imm;
        if (n > 15 || n > avail) return Insn{};
        out.len = static_cast<uint8_t>(n);
        out.ripRelative = rip;
        if (out.relBranch) out.controlFlow = true;
        return out;
    };
    auto reg_of_modrm = [&](size_t at) -> int { return at < avail ? ((p[at] >> 3) & 7) : -1; };

    // ---- VEX (C5: 2-byte, C4: 3-byte); always VEX in 64-bit mode ----------------------
    if (op == 0xC4 || op == 0xC5) {
        if (rex || opsize || rep || repne) return Insn{};   // #UD before a VEX prefix
        int map = 1;
        if (op == 0xC5) {
            if (i >= avail) return Insn{};
            i += 1;
        } else {
            if (i + 1 >= avail) return Insn{};
            map = p[i] & 0x1F;
            i += 2;
        }
        if (map < 1 || map > 3 || i >= avail) return Insn{};
        const uint8_t vop = p[i++];
        if (map == 1 && vop == 0x77) return finish(i);      // vzeroupper / vzeroall: no ModRM
        modrm = true;
        if (map == 3) imm = 1;
        else if (map == 1 && ((vop >= 0x70 && vop <= 0x73) || vop == 0xC2 || vop == 0xC4 || vop == 0xC5 || vop == 0xC6)) imm = 1;
        return finish(i);
    }

    if (op == 0x0F) {
        if (i >= avail) return Insn{};
        const uint8_t op2 = p[i++];
        if (op2 == 0x38) {                                 // three-byte map 0F38: ModRM, no immediate
            if (i >= avail) return Insn{};
            ++i;
            modrm = true;
            return finish(i);
        }
        if (op2 == 0x3A) {                                 // three-byte map 0F3A: ModRM + imm8
            if (i >= avail) return Insn{};
            ++i;
            modrm = true;
            imm = 1;
            return finish(i);
        }
        if (op2 >= 0x80 && op2 <= 0x8F) { imm = 4; out.relBranch = true; return finish(i); }   // jcc rel32
        switch (op2) {
            case 0x05: case 0x07: case 0x34: case 0x35: case 0x0B:   // syscall, sysret, sysenter, sysexit, ud2
                out.controlFlow = true; return finish(i);
            case 0x06: case 0x08: case 0x09: case 0x0E: case 0x30: case 0x31: case 0x32: case 0x33: case 0x37:
            case 0x77: case 0xA0: case 0xA1: case 0xA2: case 0xA8: case 0xA9: case 0xAA:
                return finish(i);                          // no operand bytes
            case 0x0F: case 0x04: case 0x0A: case 0x0C: case 0x24: case 0x25: case 0x26: case 0x27:
            case 0x36: case 0x39: case 0x3B: case 0x3C: case 0x3D: case 0x3E: case 0x3F:
            case 0x7A: case 0x7B: case 0xA6: case 0xA7:
                return Insn{};                             // 3DNow!, invalid or reserved here
            case 0x70: case 0x71: case 0x72: case 0x73: case 0xA4: case 0xAC: case 0xBA:
            case 0xC2: case 0xC4: case 0xC5: case 0xC6:
                modrm = true; imm = 1; return finish(i);
            case 0x78:                                     // vmread; 66/F2 = extrq/insertq ib,ib
                modrm = true; imm = (opsize || repne) ? 2 : 0; return finish(i);
            case 0xB9: case 0xFF:                          // ud1, ud0
                modrm = true; out.controlFlow = true; return finish(i);
            case 0x20: case 0x21: case 0x22: case 0x23:    // mov cr/dr: the ModRM is always a register form
                if (i >= avail) return Insn{};
                return finish(i + 1);
            default:
                break;
        }
        if (op2 >= 0xC8 && op2 <= 0xCF) return finish(i);  // bswap
        modrm = true;                                      // everything else in 0F takes a ModRM
        return finish(i);
    }

    // ---- the one-byte map -------------------------------------------------------
    if (op < 0x40) {
        const uint8_t lo = op & 7;
        switch (op) {
            case 0x06: case 0x07: case 0x0E: case 0x16: case 0x17: case 0x1E: case 0x1F:
            case 0x27: case 0x2F: case 0x37: case 0x3F:
                return Insn{};                             // invalid in 64-bit mode
            default: break;
        }
        if (lo <= 3) { modrm = true; return finish(i); }  // ALU r/m forms
        if (lo == 4) { imm = 1; return finish(i); }        // ALU al, ib
        if (lo == 5) { imm = immZ; return finish(i); }     // ALU eax, iz
        return Insn{};                                     // (26/2E/36/3E were taken as prefixes)
    }
    if (op >= 0x50 && op <= 0x5F) return finish(i);       // push/pop r64
    if (op >= 0x70 && op <= 0x7F) { imm = 1; out.relBranch = true; return finish(i); }   // jcc rel8
    if (op >= 0x90 && op <= 0x99) return finish(i);       // xchg/nop, cbw/cwd family
    if (op >= 0xB0 && op <= 0xB7) { imm = 1; return finish(i); }
    if (op >= 0xB8 && op <= 0xBF) { imm = rexW ? 8 : immZ; return finish(i); }
    if (op >= 0xD8 && op <= 0xDF) { modrm = true; return finish(i); }   // x87
    if (op >= 0xE0 && op <= 0xE3) { imm = 1; out.relBranch = true; return finish(i); }   // loop*, jrcxz

    switch (op) {
        case 0x63: case 0x84: case 0x85: case 0x86: case 0x87: case 0x88: case 0x89: case 0x8A: case 0x8B:
        case 0x8C: case 0x8D: case 0x8E: case 0xD0: case 0xD1: case 0xD2: case 0xD3:
            modrm = true; return finish(i);
        case 0x8F:                                         // pop r/m; reg != 0 is XOP
            if (reg_of_modrm(i) != 0) return Insn{};
            modrm = true; return finish(i);
        case 0x68: imm = immZ; return finish(i);
        case 0x6A: imm = 1; return finish(i);
        case 0x69: modrm = true; imm = immZ; return finish(i);
        case 0x6B: modrm = true; imm = 1; return finish(i);
        case 0x6C: case 0x6D: case 0x6E: case 0x6F:
        case 0x9B: case 0x9C: case 0x9D: case 0x9E: case 0x9F:
        case 0xA4: case 0xA5: case 0xA6: case 0xA7: case 0xAA: case 0xAB: case 0xAC: case 0xAD: case 0xAE: case 0xAF:
        case 0xC9: case 0xD7: case 0xEC: case 0xED: case 0xEE: case 0xEF:
        case 0xF5: case 0xF8: case 0xF9: case 0xFA: case 0xFB: case 0xFC: case 0xFD:
            return finish(i);
        case 0xA0: case 0xA1: case 0xA2: case 0xA3:        // mov al/eax <-> moffs
            imm = addrsize ? 4 : 8; return finish(i);
        case 0xA8: imm = 1; return finish(i);
        case 0xA9: imm = immZ; return finish(i);
        case 0x80: case 0x83: case 0xC0: case 0xC1: case 0xC6:
            modrm = true; imm = 1; return finish(i);
        case 0x81: modrm = true; imm = immZ; return finish(i);
        case 0xC7:                                         // mov r/m, iz; C7 F8 = xbegin rel
            if (i < avail && p[i] == 0xF8) out.relBranch = true;
            modrm = true; imm = immZ; return finish(i);
        case 0xC2: case 0xCA: imm = 2; out.controlFlow = true; return finish(i);   // ret/retf iw
        case 0xC3: case 0xCB: case 0xCC: case 0xCF: case 0xF1: case 0xF4:          // ret, retf, int3, iret, int1, hlt
            out.controlFlow = true; return finish(i);
        case 0xCD: imm = 1; out.controlFlow = true; return finish(i);              // int ib
        case 0xC8: imm = 3; return finish(i);                                      // enter iw, ib
        case 0xE4: case 0xE5: case 0xE6: case 0xE7: imm = 1; return finish(i);
        case 0xE8: case 0xE9: imm = 4; out.relBranch = true; return finish(i);     // call/jmp rel32
        case 0xEB: imm = 1; out.relBranch = true; return finish(i);                // jmp rel8
        case 0xF6: case 0xF7: {                                                    // test r/m, imm only for /0 /1
            const int reg = reg_of_modrm(i);
            if (reg < 0) return Insn{};
            modrm = true;
            if (reg <= 1) imm = (op == 0xF6) ? 1 : immZ;
            return finish(i);
        }
        case 0xFE: {
            const int reg = reg_of_modrm(i);
            if (reg < 0 || reg > 1) return Insn{};
            modrm = true; return finish(i);
        }
        case 0xFF: {
            const int reg = reg_of_modrm(i);
            if (reg < 0 || reg == 7) return Insn{};
            if (reg >= 2 && reg <= 5) out.controlFlow = true;  // call/jmp r/m (near or far)
            modrm = true; return finish(i);
        }
        default:
            return Insn{};   // 60/61/62(EVEX)/82/9A/C4/C5 handled or invalid, CE/D4/D5/D6/EA invalid in 64-bit
    }
}

enum class Boundary : uint8_t { Ok, TooShort, Undecodable, RipRelative, Branch, NotOnBoundary };

inline const char* boundary_name(Boundary b) {
    switch (b) {
        case Boundary::Ok:            return "ok";
        case Boundary::TooShort:      return "shorter than the 14-byte jump";
        case Boundary::Undecodable:   return "an instruction the decoder does not know";
        case Boundary::RipRelative:   return "a RIP-relative operand inside";
        case Boundary::Branch:        return "a branch, call or return inside";
        case Boundary::NotOnBoundary: return "ends inside an instruction";
    }
    return "?";
}

struct BoundaryResult {
    Boundary verdict = Boundary::Undecodable;
    size_t at = 0;            // offset of the offending instruction, or the first boundary past len
    int count = 0;            // instructions decoded
    uint8_t ends[16] = {};    // the instruction end offsets decoded (first 16)
};

// Decode from p[0] until at least `len` bytes are covered. Ok only when the
// instructions end exactly at `len` and none of them is RIP-relative or a
// control transfer. `avail` = bytes readable at p (>= len).
inline BoundaryResult check_patch(const uint8_t* p, size_t avail, size_t len, size_t minLen = 14) {
    BoundaryResult r;
    if (len < minLen) { r.verdict = Boundary::TooShort; return r; }
    if (avail < len) { r.verdict = Boundary::Undecodable; return r; }
    size_t off = 0;
    while (off < len) {
        const Insn in = decode(p + off, avail - off);
        if (in.len == 0) { r.verdict = Boundary::Undecodable; r.at = off; return r; }
        if (in.ripRelative) { r.verdict = Boundary::RipRelative; r.at = off; return r; }
        if (in.controlFlow) { r.verdict = Boundary::Branch; r.at = off; return r; }
        off += in.len;
        if (r.count < 16) r.ends[r.count] = static_cast<uint8_t>(off);
        ++r.count;
    }
    r.at = off;
    r.verdict = (off == len) ? Boundary::Ok : Boundary::NotOnBoundary;
    return r;
}

// "patch 18: ends inside an instruction (ends 5/10/11/15/20)" -- for a refusal's log line.
inline void describe(const BoundaryResult& r, size_t len, char* out, size_t cap) {
    int n = std::snprintf(out, cap, "patch %zu: %s", len, boundary_name(r.verdict));
    if (n < 0 || static_cast<size_t>(n) >= cap) return;
    if (r.verdict != Boundary::Ok && r.verdict != Boundary::TooShort)
        n += std::snprintf(out + n, cap - n, " at +%zu", r.at);
    if (r.count > 0 && static_cast<size_t>(n) < cap) {
        n += std::snprintf(out + n, cap - n, " (ends");
        for (int k = 0; k < r.count && k < 16 && static_cast<size_t>(n) < cap; ++k)
            n += std::snprintf(out + n, cap - n, "%c%u", k ? '/' : ' ', r.ends[k]);
        if (static_cast<size_t>(n) < cap) std::snprintf(out + n, cap - n, ")");
    }
}

} // namespace kcdmp::x64
