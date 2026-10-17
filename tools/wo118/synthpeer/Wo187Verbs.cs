// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-187 C3 (tools/wo118, never shipped): the echo partner. The relay forwards the local player's own stream to this peer like to any
// partner (his Position as a Ghost frame, his fight rows as ActionDown); the echo re-sends both as this peer's own, `delay` seconds
// later, standing `ahead` metres in front of where he stood and facing him -- so his own fight plays back on the partner's figure in
// front of him, and every move he made can be checked on the watcher's side (the figure played the same row; between moves it stood in
// guard). Plan verbs:
//   echo <delay_s> <ahead_m>                        echo every other peer's position, state block and fight rows
//   fmove <t_s> <attack|block|pblock|dodge|hit> <rowGuid>   (the input-free variant) this peer's figure plays that row at stream time t
//   (the figure's combat bit between moves: gcombat <t0> <t1> [gz]; its attack rows: jattack <t> <row>)
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using KcdMp.Wire;

static class Wo187Verbs
{
    public sealed class Echo
    {
        public double DelayMs, Ahead;
        public byte MyId = 0xFF;
        public readonly ConcurrentQueue<(double Due, byte[] Pkt, string Note)> Out = new();
        public long Positions, Actions, Dropped;
    }

    static uint Now() => unchecked((uint)(Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency));

    /// <summary>A Ghost frame (another peer's position) -> this peer's echoed Position, due `DelayMs` later.</summary>
    public static void OnGhost(Echo e, byte[] g, double nowMs)
    {
        if (g.Length < 18 || g[0] == e.MyId) return;
        float x = BinaryPrimitives.ReadSingleLittleEndian(g.AsSpan(1)), y = BinaryPrimitives.ReadSingleLittleEndian(g.AsSpan(5));
        float z = BinaryPrimitives.ReadSingleLittleEndian(g.AsSpan(9)), rot = BinaryPrimitives.ReadSingleLittleEndian(g.AsSpan(13));
        byte flags = g[17];
        bool st2 = (flags & Protocol.PositionFlagBodyState2) != 0 && g.Length >= 18 + Protocol.BodyState2Len;
        // in front of him (the game's forward: -sin(yaw), cos(yaw)), facing him
        float ex = x - MathF.Sin(rot) * (float)e.Ahead, ey = y + MathF.Cos(rot) * (float)e.Ahead;
        float eyaw = rot + MathF.PI; if (eyaw > MathF.PI) eyaw -= 2 * MathF.PI;
        int len = Protocol.PositionPayloadLen + (st2 ? Protocol.BodyState2Len : 0) + Protocol.SenderMsLen;
        var p = new byte[3 + len];
        p[0] = Protocol.Position; BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(1), (ushort)len);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(3), ex); BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(7), ey);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(11), z); BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(15), eyaw);
        p[19] = (byte)(Protocol.PositionFlagSenderMs | (st2 ? Protocol.PositionFlagBodyState2 : 0) | (flags & Protocol.PositionFlagRiding));
        int o = 20;
        if (st2) { g.AsSpan(18, Protocol.BodyState2Len).CopyTo(p.AsSpan(o)); o += Protocol.BodyState2Len; }
        // the sender stamp is written when it is sent (Restamp)
        e.Out.Enqueue((nowMs + e.DelayMs, p, ""));
        e.Positions++;
    }

    /// <summary>An ActionDown (another peer's fight row) -> the same action as this peer's ActionUp, due `DelayMs` later.</summary>
    public static void OnAction(Echo e, KcdMp.Client.ActionOutbox ch, byte[] d, double nowMs)
    {
        // [src][kind][seq:2][phase][gen:4][len][body]
        if (d.Length < 1 + Protocol.ActionUpHeaderLen || d[0] == e.MyId) return;
        var kind = (ActionKind)d[1];
        if (kind is not (ActionKind.Attack or ActionKind.BlockImpulse or ActionKind.Dodge or ActionKind.Jump or ActionKind.AvatarHit
                      or ActionKind.AvatarRecoil or ActionKind.AvatarClinch)) { e.Dropped++; return; }   // WO-197: his recoil and clinch echo too
        var phase = (ActionPhase)d[4];
        int blen = d[9];
        if (d.Length < 10 + blen) return;
        var body = d.AsSpan(10, blen).ToArray();
        e.Out.Enqueue((nowMs + e.DelayMs, ch.Build(kind, phase, body), FormattableString.Invariant($"ECHO {kind} (from peer {d[0]})")));
        e.Actions++;
    }

    /// <summary>At send time: a Position's sender stamp (its last 4 bytes); an action's body stamp (its first 4 bytes: AttackEvent / RowEvent / Jump).</summary>
    public static byte[] Restamp(byte[] p)
    {
        if (p[0] == Protocol.Position) BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(p.Length - 4), Now());
        else if (p[0] == Protocol.ActionUp && p.Length >= 3 + Protocol.ActionUpHeaderLen + 4)
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(3 + Protocol.ActionUpHeaderLen), Now());
        return p;
    }

    /// <summary>fmove: this peer's figure plays a row (an Attack, or a BlockImpulse / Dodge row event), stamped when it is sent.</summary>
    public static byte[] Move(KcdMp.Client.ActionOutbox ch, string what, Guid row) => what switch
    {
        "attack" => ch.Build(ActionKind.Attack, ActionPhase.Commit, new AttackEvent(Now(), 1, WireZone.UpperRight, 0, 0, row).ToBytes()),
        "block" => ch.Build(ActionKind.BlockImpulse, ActionPhase.Commit, new RowEvent(Now(), 0, row, "").ToBytes()),
        "pblock" => ch.Build(ActionKind.BlockImpulse, ActionPhase.Commit, new RowEvent(Now(), RowEvent.FlagPerfect, row, "").ToBytes()),
        "dodge" => ch.Build(ActionKind.Dodge, ActionPhase.Commit, new RowEvent(Now(), 0, row, "").ToBytes()),
        "hit" => ch.Build(ActionKind.AvatarHit, ActionPhase.Commit, new RowEvent(Now(), 0, row, "").ToBytes()),   // C1: his own hit reaction
        // WO-197 B1: his recoil (row = the engine's failed-attack row, or Guid.Empty: the receiver picks by his last swing's weapon)
        "recoil" => ch.Build(ActionKind.AvatarRecoil, ActionPhase.Commit, new RowEvent(Now(), (byte)(0x02 | (row != Guid.Empty ? 0x04 : 0)), row, "").ToBytes()),
        "precoil" => ch.Build(ActionKind.AvatarRecoil, ActionPhase.Commit, new RowEvent(Now(), (byte)(0x03 | (row != Guid.Empty ? 0x04 : 0)), row, "").ToBytes()),
        // WO-197 B3: his clinch (enter as master; leave)
        "clinchenter" => ch.Build(ActionKind.AvatarClinch, ActionPhase.Commit, new RowEvent(Now(), 0x01 | 0x04, Guid.Empty, "").ToBytes()),
        // WO-197 Part C: his whistle (Emote [ms][1])
        "whistle" => ch.Build(ActionKind.Emote, ActionPhase.Commit, Emote(Now(), EmoteId.Whistle)),
        "clinchleave" => ch.Build(ActionKind.AvatarClinch, ActionPhase.Commit, new RowEvent(Now(), 0x03 | 0x04, Guid.Empty, "").ToBytes()),
        _ => throw new ArgumentException($"fmove: unknown move '{what}'"),
    };

    private static byte[] Emote(uint ms, byte id) { var b = new byte[EmoteId.PayloadLen]; BinaryPrimitives.WriteUInt32LittleEndian(b, ms); b[4] = id; return b; }

    /// <summary>WO-197 B3: fclinch -- a clinch move with both rows (the master's and the slave's), as master (or slave).</summary>
    public static byte[] ClinchMove(KcdMp.Client.ActionOutbox ch, Guid masterRow, Guid slaveRow, bool master) =>
        ch.Build(ActionKind.AvatarClinch, ActionPhase.Commit, new RowEvent(Now(), (byte)(0x02 | 0x08 | (master ? 0x04 : 0)), masterRow, slaveRow.ToString("N")).ToBytes());
}
