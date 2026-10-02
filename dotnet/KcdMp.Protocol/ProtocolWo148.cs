// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;

namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-148 -- carrying on the other screen (docs/WO-148-findings.md).
//
// The carrier owns the body while it is carried (WO-119 s3.4, the settled rule):
// the carrier's own game carries it as the game does; every other game makes the
// carrier's avatar pick up its own copy of the body (the game's own pick-up on
// the avatar: actor:RequestGrabCorpse), carry it while the stream moves the
// avatar, and set it down where the carrier set it down. The host's world
// decides who carries: a joiner's grab goes to the host first.
//
// One message rides the WO-123 join channel (same header, same routing, a row
// in Protocol.JoinWire; "Either": a joiner's goes to the host (target 0xFF), the
// host's to one joiner by id -- the relay needs no code of its own):
//
//   type up/down  name    up body (after [target:1][joinId:4])
//   0x70 / 0x71   Carry   [kind:1][tok:4][text:1..CarryTextMax]
//
// tok = the carry's id, chosen by the carrier. The carrier's ghost id rides the
// text, so a carry the host forwards to the other joiners still names its carrier.
//
// Kinds (APPEND-ONLY):
//   1 Grab    "<carrier> <what> <name> <x> <y> <z>"         picked up; x y z = where the body lay
//   2 Held    "<carrier> <what> <name> <x> <y> <z>"         still carrying (every 2 s; recovers a lost grab)
//   3 Put     "<carrier> <how> <what> <name> <x> <y> <z>"   set down; x y z = where it came to rest
//   4 Refuse  "<carrier> <name> <why>"                       the host's world said no: the carrier's
//                                                            game puts it down and back where it lay
// what = dead | ko | object;  how = put | drop | throw | lost (the carry ended
// without a set-down the carrier saw: a load, a death, a cutscene).
//
// No protocol bump: one new type on the join channel. A mixed release is refused
// at the relay (WO-110 R9), so a peer that does not know it never shares a
// session with one that sends it.
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    public const byte CarryUp = 0x70, CarryDown = 0x71;   // WO-148

    public const int CarryTextMax = 160;

    // ---- kinds (APPEND-ONLY) ----
    public const byte CarryGrab = 1, CarryHeld = 2, CarryPut = 3, CarryRefuse = 4;

    /// <summary>The carrier repeats a Held this often while it carries.</summary>
    public const int CarryHeldEveryMs = 2000;
    /// <summary>No Held for this long: the carry is taken as over (the body is set down where it is).</summary>
    public const int CarryLostAfterMs = 10_000;

    public static string CarryKindName(byte k) => k switch
    {
        CarryGrab => "grab", CarryHeld => "held", CarryPut => "put", CarryRefuse => "refuse", _ => $"unknown-{k}",
    };
}

/// <summary>WO-148: one carry message's text, both ways.</summary>
public readonly record struct CarryText(byte Carrier, string How, string What, string Name, float X, float Y, float Z, string Why)
{
    // WO-151 3.2: "living" -- a living NPC carried through the game's own quest carry (CarryLivingActor)
    public static readonly string[] Whats = { "dead", "ko", "object", "living" };
    public static readonly string[] Hows = { "put", "drop", "throw", "lost" };
    public static readonly string[] Whys = { "carried", "no-body", "alive", "far", "off", "no-avatar", "not-shared", "name" };

    public static bool IsWhat(string? s) => s is not null && Array.IndexOf(Whats, s) >= 0;
    public static bool IsHow(string? s) => s is not null && Array.IndexOf(Hows, s) >= 0;
    public static bool IsWhy(string? s) => s is not null && Array.IndexOf(Whys, s) >= 0;

    /// <summary>An authored entity name the stream also accepts ([A-Za-z0-9_], 1..64).</summary>
    public static bool IsName(string? s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > Protocol.MaxNpcNameLen) return false;
        foreach (char c in s) if (!(char.IsAsciiLetterOrDigit(c) || c == '_')) return false;
        return true;
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Grab(byte carrier, string what, string name, float x, float y, float z) =>
        $"{carrier} {what} {name} {F(x)} {F(y)} {F(z)}";
    public static string Held(byte carrier, string what, string name, float x, float y, float z) => Grab(carrier, what, name, x, y, z);
    public static string Put(byte carrier, string how, string what, string name, float x, float y, float z) =>
        $"{carrier} {how} {what} {name} {F(x)} {F(y)} {F(z)}";
    public static string Refuse(byte carrier, string name, string why) => $"{carrier} {name} {why}";

    /// <summary>Parses the text of one kind; false for anything malformed (never throws).</summary>
    public static bool TryParse(byte kind, string? text, out CarryText t)
    {
        t = default;
        if (string.IsNullOrEmpty(text) || text.Length > Protocol.CarryTextMax) return false;
        string[] p = text.Split(' ');
        static bool Num(string s, out float v) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && float.IsFinite(v) && Math.Abs(v) < 1e6f;
        switch (kind)
        {
            case Protocol.CarryGrab:
            case Protocol.CarryHeld:
            {
                if (p.Length != 6 || !byte.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out byte c)) return false;
                if (!IsWhat(p[1]) || !IsName(p[2])) return false;
                if (!Num(p[3], out float x) || !Num(p[4], out float y) || !Num(p[5], out float z)) return false;
                t = new CarryText(c, "", p[1], p[2], x, y, z, "");
                return true;
            }
            case Protocol.CarryPut:
            {
                if (p.Length != 7 || !byte.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out byte c)) return false;
                if (!IsHow(p[1]) || !IsWhat(p[2]) || !IsName(p[3])) return false;
                if (!Num(p[4], out float x) || !Num(p[5], out float y) || !Num(p[6], out float z)) return false;
                t = new CarryText(c, p[1], p[2], p[3], x, y, z, "");
                return true;
            }
            case Protocol.CarryRefuse:
            {
                if (p.Length != 3 || !byte.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out byte c)) return false;
                if (!IsName(p[1]) || !IsWhy(p[2])) return false;
                t = new CarryText(c, "", "", p[1], 0, 0, 0, p[2]);
                return true;
            }
            default:
                return false;
        }
    }
}
