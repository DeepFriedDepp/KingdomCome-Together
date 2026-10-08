// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Text;

namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-161 -- the hit verdict (docs/WO-161-findings.md).
//
// One message rides the WO-123 join channel (same header, same routing, a row in Protocol.JoinWire, so the relay's gate
// needs no code of its own):
//
//   type up/down  name         up body (after [target:1][hitId:4])                          sent by
//   0x72 / 0x73   HitVerdict   [ver:1][verdict:1][flags:1][zone:1][swing:4][hp:4f][st:4f]   the host -> one joiner
//                              [nameLen:1][name:0..64]                                      (17..81 B)
//
// hitId (the join header's id slot) numbers the host's hits on avatars from 1, per host session: the receiver applies each
// id once ("no double damage"), whatever the network does. swing is the host's swing id (0 = no swing was captured for
// this hit), name the NPC that struck (empty on the fallback path that cannot name one). zone is the WireZone of the
// attack the host's NPC was committed to (0 = unknown). verdict:
//   1 Hit      health was lost
//   2 Blocked  the avatar's guard absorbed it: no health lost, stamina paid
//   3 Parried  reserved -- the victim's own verdict (never sent by 0.46.5: the victim cannot hit-test a puppet's swing)
//   4 Missed   reserved -- likewise
// flags: 1 SwingKnown (the host's ledger had a swing of this NPC just before the hit), 2 Missile, 4 NoAttacker.
//
// A verdict replaces the 0.46.0 PlayerHit (0x21/0x22) for an NPC's hit on an avatar; the old pair stays for the agents'
// fallback (mp: HitVerdictEnabled = false) and for the synthetic peers. A mixed release is refused at the relay
// (WO-110 R9), so a peer that does not know 0x72 never shares a session with one that sends it. No protocol bump.
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    public const byte HitVerdictUp = 0x72, HitVerdictDown = 0x73;   // WO-161

    public const byte HitVerdictWire = 1;
    public const int HitVerdictFixedLen = 1 + 1 + 1 + 1 + 4 + 4 + 4 + 1;
    public const int HitVerdictBodyMax = HitVerdictFixedLen + MaxNpcNameLen;
    /// <summary>A health or stamina figure outside 0..this is not a hit (the engine's own pools are far below it).</summary>
    public const float HitVerdictStatMax = 1000f;

    public const byte HitFlagSwingKnown = 1, HitFlagMissile = 2, HitFlagNoAttacker = 4;
}

/// <summary>WO-161: the verdict of one hit on an avatar (APPEND-ONLY).</summary>
public enum HitVerdict : byte { None = 0, Hit = 1, Blocked = 2, Parried = 3, Missed = 4 }

/// <summary>WO-161: one HitVerdict message (the body after the join header; the join header's id slot is the hit id).</summary>
public readonly record struct HitVerdictMsg(uint HitId, HitVerdict Verdict, byte Flags, byte Zone, uint SwingId, float Health, float Stamina, string Attacker)
{
    public bool SwingKnown => (Flags & Protocol.HitFlagSwingKnown) != 0;
    public bool Missile => (Flags & Protocol.HitFlagMissile) != 0;
    public bool NoAttacker => (Flags & Protocol.HitFlagNoAttacker) != 0;

    public static string VerdictName(HitVerdict v) => v switch
    {
        HitVerdict.Hit => "hit", HitVerdict.Blocked => "blocked", HitVerdict.Parried => "parried", HitVerdict.Missed => "missed",
        _ => $"unknown-{(byte)v}",
    };

    /// <summary>The framed Up packet to <paramref name="target"/>: [type][len:2][target][hitId:4][body].</summary>
    public byte[] BuildUp(byte target)
    {
        if (HitId == 0) throw new ArgumentOutOfRangeException(nameof(HitId), "hit ids start at 1");
        if (Verdict == HitVerdict.None || (byte)Verdict > (byte)HitVerdict.Missed) throw new ArgumentOutOfRangeException(nameof(Verdict));
        if (!StatOk(Health) || !StatOk(Stamina)) throw new ArgumentOutOfRangeException(nameof(Health), "health and stamina are finite, 0..1000");
        string name = Attacker ?? "";
        if (name.Length > 0 && !CarryText.IsName(name)) throw new ArgumentException("an attacker is an authored entity name ([A-Za-z0-9_], 1..64) or empty", nameof(Attacker));
        var nb = Encoding.ASCII.GetBytes(name);
        var body = new byte[Protocol.HitVerdictFixedLen + nb.Length];
        body[0] = Protocol.HitVerdictWire;
        body[1] = (byte)Verdict;
        body[2] = Flags;
        body[3] = Zone;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), SwingId);
        BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(8), Health);
        BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(12), Stamina);
        body[16] = (byte)nb.Length;
        nb.CopyTo(body, Protocol.HitVerdictFixedLen);
        return Protocol.BuildJoinUp(Protocol.HitVerdictUp, target, HitId, body);
    }

    /// <summary>
    /// Decodes the body after the join header. Strict: the version, a known verdict, finite in-range figures, the name length
    /// exact and the name an entity name -- anything else is refused (a hostile or damaged frame never reaches a damage call).
    /// </summary>
    public static bool TryDecode(uint hitId, ReadOnlySpan<byte> body, out HitVerdictMsg m)
    {
        m = default;
        if (hitId == 0 || body.Length < Protocol.HitVerdictFixedLen || body.Length > Protocol.HitVerdictBodyMax) return false;
        if (body[0] != Protocol.HitVerdictWire) return false;
        var verdict = (HitVerdict)body[1];
        if (verdict == HitVerdict.None || verdict > HitVerdict.Missed) return false;
        byte flags = body[2];
        if ((flags & ~(Protocol.HitFlagSwingKnown | Protocol.HitFlagMissile | Protocol.HitFlagNoAttacker)) != 0) return false;
        byte zone = body[3];
        if (zone > (byte)WireZone.Lower) return false;
        uint swing = BinaryPrimitives.ReadUInt32LittleEndian(body[4..]);
        float hp = BinaryPrimitives.ReadSingleLittleEndian(body[8..]);
        float st = BinaryPrimitives.ReadSingleLittleEndian(body[12..]);
        if (!StatOk(hp) || !StatOk(st)) return false;
        int n = body[16];
        if (body.Length != Protocol.HitVerdictFixedLen + n || n > Protocol.MaxNpcNameLen) return false;
        string name = "";
        if (n > 0)
        {
            var nb = body.Slice(Protocol.HitVerdictFixedLen, n);
            foreach (byte c in nb) if (!(char.IsAsciiLetterOrDigit((char)c) || c == (byte)'_')) return false;
            name = Encoding.ASCII.GetString(nb);
        }
        // the flag and the name must agree: NoAttacker means no name, and a named hit is not NoAttacker
        if (((flags & Protocol.HitFlagNoAttacker) != 0) != (n == 0)) return false;
        m = new HitVerdictMsg(hitId, verdict, flags, zone, swing, hp, st, name);
        return true;
    }

    /// <summary>Decodes a whole Down payload ([source][target][hitId:4][body]).</summary>
    public static bool TryDecodeDown(ReadOnlySpan<byte> downPayload, out byte source, out HitVerdictMsg m)
    {
        m = default;
        if (!Protocol.TrySplitJoinDown(downPayload, out source, out _, out uint hitId, out var body)) return false;
        return TryDecode(hitId, body, out m);
    }

    private static bool StatOk(float v) => float.IsFinite(v) && v >= 0f && v <= Protocol.HitVerdictStatMax;
}
