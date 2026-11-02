// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace KcdMp.Wire;

/// <summary>
/// WO-213 D.3: the relay's compact NpcStateDown on the link to one client (NpcStateQ, 0x83), and the client's way back to the 0x27
/// payload. Per link, each NPC gets an id; a message carries only the field groups that changed since this link's last message of
/// that NPC, positions quantized (centimetres within a 655.36 m cell, height to 2 cm, yaw to 0.1 degrees, health to 0.1), and every
/// FullEveryMs (and the first FirstFull messages of an id) a full one with the name -- a lost delta (state travels unreliably, C.2)
/// is healed by the next full one, and a client that missed the name drops the id's messages until then.
///
///   [src:1][id:2][mask:1] then, by mask: 0x01 [nameLen][name] 0x02 [cx:1][cy:1][x:2][y:2] 0x04 [z:2] 0x08 [rot:2] 0x10 [hp:2]
///   0x20 [flags:1]; always [seq:2][senderMs:4]
/// </summary>
public static class NpcCompact
{
    public const byte GName = 0x01, GPos = 0x02, GZ = 0x04, GRot = 0x08, GHp = 0x10, GFlags = 0x20, GAll = 0x3F;
    public const double Cell = 655.36;
    public const int FullEveryMs = 1000, FirstFull = 3;

    public readonly record struct Fields(float X, float Y, float Z, float Rot, float Hp, byte Flags, ushort Seq, uint SenderMs);

    public readonly record struct Quant(byte Cx, byte Cy, ushort X, ushort Y, ushort Z, ushort Rot, ushort Hp)
    {
        public static bool TryOf(in Fields f, out Quant q)
        {
            q = default;
            if (!float.IsFinite(f.X) || !float.IsFinite(f.Y) || !float.IsFinite(f.Z) || !float.IsFinite(f.Rot)) return false;
            if (f.X < 0 || f.Y < 0 || f.X >= 256 * Cell || f.Y >= 256 * Cell || f.Z < -100 || f.Z > 1210) return false;
            int cx = (int)(f.X / Cell), cy = (int)(f.Y / Cell);
            double rot = f.Rot % (2 * Math.PI); if (rot < 0) rot += 2 * Math.PI;
            ushort hp = f.Hp < 0 ? (ushort)0xFFFF : (ushort)Math.Min(0xFFFE, Math.Round(f.Hp * 10));
            q = new Quant((byte)cx, (byte)cy, (ushort)Math.Min(65535, Math.Round((f.X - cx * Cell) * 100)), (ushort)Math.Min(65535, Math.Round((f.Y - cy * Cell) * 100)),
                          (ushort)Math.Round((f.Z + 100) * 50), (ushort)(Math.Round(rot * 1800 / Math.PI) % 3600), hp);
            return true;
        }

        public Fields Back(byte flags, ushort seq, uint ms) => new(
            (float)(Cx * Cell + X / 100.0), (float)(Cy * Cell + Y / 100.0), (float)(Z / 50.0 - 100), (float)(Rot * Math.PI / 1800.0),
            Hp == 0xFFFF ? -1f : Hp / 10f, flags, seq, ms);
    }

    public static bool TryReadDown(ReadOnlySpan<byte> p, out byte src, out string name, out Fields f)
    {
        src = 0; name = ""; f = default;
        if (p.Length < 2 + Protocol.NpcStateFixedTail || p.Length != 2 + p[1] + Protocol.NpcStateFixedTail) return false;
        src = p[0]; name = Encoding.UTF8.GetString(p.Slice(2, p[1]));
        int o = 2 + p[1];
        f = new Fields(BitConverter.ToSingle(p[o..]), BitConverter.ToSingle(p[(o + 4)..]), BitConverter.ToSingle(p[(o + 8)..]), BitConverter.ToSingle(p[(o + 12)..]),
                       BitConverter.ToSingle(p[(o + 16)..]), p[o + Protocol.NpcStateFlagsOffset],
                       BinaryPrimitives.ReadUInt16LittleEndian(p[(o + Protocol.NpcStateSeqOffset)..]), BinaryPrimitives.ReadUInt32LittleEndian(p[(o + Protocol.NpcStateSenderMsOffset)..]));
        return true;
    }

    public static byte[] BuildDown(byte src, string name, in Fields f)
    {
        var nb = Encoding.UTF8.GetBytes(name);
        var p = new byte[2 + nb.Length + Protocol.NpcStateFixedTail];
        p[0] = src; p[1] = (byte)nb.Length; nb.CopyTo(p, 2);
        int o = 2 + nb.Length;
        BitConverter.TryWriteBytes(p.AsSpan(o), f.X); BitConverter.TryWriteBytes(p.AsSpan(o + 4), f.Y); BitConverter.TryWriteBytes(p.AsSpan(o + 8), f.Z);
        BitConverter.TryWriteBytes(p.AsSpan(o + 12), f.Rot); BitConverter.TryWriteBytes(p.AsSpan(o + 16), f.Hp);
        p[o + Protocol.NpcStateFlagsOffset] = f.Flags;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o + Protocol.NpcStateSeqOffset), f.Seq);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(o + Protocol.NpcStateSenderMsOffset), f.SenderMs);
        return p;
    }

    /// <summary>The relay's side, one per link: NpcStateDown frames in, NpcStateQ frames out.</summary>
    public sealed class Encoder
    {
        private sealed class Last { public ushort Id; public Quant Q; public byte Flags; public int Sent; public long FullTs; }
        private readonly Dictionary<string, Last> _byBody = new(StringComparer.Ordinal);
        private ushort _nextId = 1;
        public long Frames, Bytes, BytesBefore, Fulls;

        /// <summary>The compact frame for a 0x27 frame (header included); the frame unchanged when it does not fit the format.</summary>
        public byte[] Encode(byte[] frame)
        {
            if (frame.Length < 3 || frame[0] != Protocol.NpcStateDown || !TryReadDown(frame.AsSpan(3), out byte src, out string name, out var f)
                || !Quant.TryOf(f, out var q)) return frame;
            long now = Stopwatch.GetTimestamp();
            string key = src + "|" + name;
            if (!_byBody.TryGetValue(key, out var last))
            {
                if (_nextId == 0) return frame;   // 65535 bodies on one link: the rest go full-size
                _byBody[key] = last = new Last { Id = _nextId++ };
            }
            bool full = last.Sent < FirstFull || Stopwatch.GetElapsedTime(last.FullTs, now).TotalMilliseconds >= FullEveryMs;
            byte mask = full ? GAll : (byte)((q.Cx != last.Q.Cx || q.Cy != last.Q.Cy || q.X != last.Q.X || q.Y != last.Q.Y ? GPos : 0)
                                            | (q.Z != last.Q.Z ? GZ : 0) | (q.Rot != last.Q.Rot ? GRot : 0) | (q.Hp != last.Q.Hp ? GHp : 0)
                                            | (f.Flags != last.Flags ? GFlags : 0));
            var nb = (mask & GName) != 0 ? Encoding.UTF8.GetBytes(name) : [];
            int len = 1 + 2 + 1 + ((mask & GName) != 0 ? 1 + nb.Length : 0) + ((mask & GPos) != 0 ? 6 : 0) + ((mask & GZ) != 0 ? 2 : 0)
                    + ((mask & GRot) != 0 ? 2 : 0) + ((mask & GHp) != 0 ? 2 : 0) + ((mask & GFlags) != 0 ? 1 : 0) + 6;
            var o = new byte[3 + len];
            o[0] = Protocol.NpcStateQDown; BinaryPrimitives.WriteUInt16LittleEndian(o.AsSpan(1), (ushort)len);
            int i = 3;
            o[i++] = src; BinaryPrimitives.WriteUInt16LittleEndian(o.AsSpan(i), last.Id); i += 2; o[i++] = mask;
            if ((mask & GName) != 0) { o[i++] = (byte)nb.Length; nb.CopyTo(o, i); i += nb.Length; }
            if ((mask & GPos) != 0) { o[i++] = q.Cx; o[i++] = q.Cy; BinaryPrimitives.WriteUInt16LittleEndian(o.AsSpan(i), q.X); BinaryPrimitives.WriteUInt16LittleEndian(o.AsSpan(i + 2), q.Y); i += 4; }
            if ((mask & GZ) != 0) { BinaryPrimitives.WriteUInt16LittleEndian(o.AsSpan(i), q.Z); i += 2; }
            if ((mask & GRot) != 0) { BinaryPrimitives.WriteUInt16LittleEndian(o.AsSpan(i), q.Rot); i += 2; }
            if ((mask & GHp) != 0) { BinaryPrimitives.WriteUInt16LittleEndian(o.AsSpan(i), q.Hp); i += 2; }
            if ((mask & GFlags) != 0) o[i++] = f.Flags;
            BinaryPrimitives.WriteUInt16LittleEndian(o.AsSpan(i), f.Seq); BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(i + 2), f.SenderMs);
            last.Q = q; last.Flags = f.Flags; last.Sent++;
            if (full) { last.FullTs = now; Fulls++; }
            Frames++; Bytes += o.Length; BytesBefore += frame.Length;
            return o;
        }
    }

    /// <summary>The client's side, one per link: NpcStateQ payloads in, 0x27 payloads out.</summary>
    public sealed class Decoder
    {
        private sealed class Known { public string Name = ""; public Quant Q; public byte Flags; public bool HaveAll; }
        private readonly Dictionary<int, Known> _byId = new();
        public long Decoded, UnknownId, Malformed;

        /// <summary>The 0x27 payload for a 0x83 payload, or null (an id whose name has not come yet, or a malformed body).</summary>
        public byte[]? Decode(ReadOnlySpan<byte> p)
        {
            if (p.Length < 4 + 6) { Malformed++; return null; }
            byte src = p[0]; ushort id = BinaryPrimitives.ReadUInt16LittleEndian(p[1..]); byte mask = p[3];
            int i = 4, key = src << 16 | id;
            _byId.TryGetValue(key, out var k);
            if ((mask & GName) != 0)
            {
                if (i >= p.Length || i + 1 + p[i] > p.Length) { Malformed++; return null; }
                string name = Encoding.UTF8.GetString(p.Slice(i + 1, p[i]));
                i += 1 + p[i];
                if (k is null || k.Name != name) _byId[key] = k = new Known { Name = name };
            }
            if (k is null) { UnknownId++; return null; }
            var q = k.Q; byte flags = k.Flags;
            int need = ((mask & GPos) != 0 ? 6 : 0) + ((mask & GZ) != 0 ? 2 : 0) + ((mask & GRot) != 0 ? 2 : 0) + ((mask & GHp) != 0 ? 2 : 0) + ((mask & GFlags) != 0 ? 1 : 0) + 6;
            if (i + need != p.Length) { Malformed++; return null; }
            if ((mask & GPos) != 0) { q = q with { Cx = p[i], Cy = p[i + 1], X = BinaryPrimitives.ReadUInt16LittleEndian(p[(i + 2)..]), Y = BinaryPrimitives.ReadUInt16LittleEndian(p[(i + 4)..]) }; i += 6; }
            if ((mask & GZ) != 0) { q = q with { Z = BinaryPrimitives.ReadUInt16LittleEndian(p[i..]) }; i += 2; }
            if ((mask & GRot) != 0) { q = q with { Rot = BinaryPrimitives.ReadUInt16LittleEndian(p[i..]) }; i += 2; }
            if ((mask & GHp) != 0) { q = q with { Hp = BinaryPrimitives.ReadUInt16LittleEndian(p[i..]) }; i += 2; }
            if ((mask & GFlags) != 0) flags = p[i++];
            ushort seq = BinaryPrimitives.ReadUInt16LittleEndian(p[i..]); uint ms = BinaryPrimitives.ReadUInt32LittleEndian(p[(i + 2)..]);
            if ((mask & (GPos | GZ | GRot | GHp | GFlags)) == (GPos | GZ | GRot | GHp | GFlags)) k.HaveAll = true;
            k.Q = q; k.Flags = flags;
            if (!k.HaveAll) { UnknownId++; return null; }   // a delta before this link's first full message: nothing to complete it with
            Decoded++;
            return BuildDown(src, k.Name, q.Back(flags, seq, ms));
        }
    }
}
