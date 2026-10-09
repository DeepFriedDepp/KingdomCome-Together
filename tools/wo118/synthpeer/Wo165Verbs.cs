// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-165 harness verbs for the synthetic peer (tools/wo118, never shipped): the packets each plan verb sends, built here so
// `SynthPeer --selftest-wo165` can check every one against the SHIPPED decoders before a live run.
//
//   swingat <t> <npc> local <rowGuid> <lag_ms> <hit|blocked|pb|broken> <hp> <st> [joinerId=1]
//        host role: the NpcAttack row of the enemy copy <npc> at t, then the host's 0x72 verdict for a blow on the local player
//        (the joiner) after the row's own lag -- swing id known, numbered like the host's ledger
//   verdict <t> <hit|blocked|pb|broken|parried|missed> <hp> <st> <npc|-> [swing] [joinerId] [hid=N]
//        as before (WO-163), with the WO-165 values and an explicit hit id (a duplicate test re-sends an id)
//   avatarfight <t0> <t1> <npc> <every_s> <hp> <st>
//        joiner role: this peer's avatar strikes the host's <npc> every <every_s> from t0 to t1 -- an ATTRIBUTED NpcDamage (0x30,
//        flag 0x04), the host's own lever that makes its NPC fight the avatar (hits.h apply_attributed: TakeDamage(attacker), history,
//        AddSoulToSkirmish(npc, avatar, 1) once per engagement)
//   flee <t0> <npc> <x0> <y0> <z0> <ux> <uy> <speed> <len>
//        host role: <npc> stands at (x0,y0,z0) until t0, then runs along (ux,uy) at <speed> for <len> m and stops
//   teleport <t> <npc|ghost> <dx> <dy>
//        from t on, that mover (or the ghost) is offset by (dx,dy) metres: ONE step of that length, to tag a window as a jump
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using KcdMp.Wire;

static class Wo165Verbs
{
    public static bool TryVerdict(string s, out HitVerdict v) => HitVerdictMsg.TryParseVerdict(s, out v);

    /// <summary>The host's verdict (0x72) for one blow on joiner <paramref name="target"/>.</summary>
    public static byte[] Verdict(uint hid, HitVerdict v, float hp, float st, string npc, uint swing, byte target)
    {
        byte flags = (byte)((swing != 0 ? Protocol.HitFlagSwingKnown : 0) | (npc.Length == 0 ? Protocol.HitFlagNoAttacker : 0));
        return new HitVerdictMsg(hid, v, flags, 0, swing, hp, st, npc).BuildUp(target);
    }

    /// <summary>The host NPC's swing row (an NpcAttack commit on the action channel), stamped with <paramref name="stampMs"/>.</summary>
    public static byte[] Row(KcdMp.Client.ActionOutbox outbox, string npc, Guid row, uint stampMs) =>
        outbox.Build(ActionKind.NpcAttack, ActionPhase.Commit, new RowEvent(stampMs, 0, row, npc).ToBytes());

    /// <summary>An avatar's attributed blow on the host's NPC: NpcDamageUp (0x30) [nameLen][name][st:4f][hp:4f][flags].</summary>
    public static byte[] AttributedHit(string npc, float st, float hp)
    {
        var nb = Encoding.UTF8.GetBytes(npc);
        int len = 1 + nb.Length + Protocol.NpcDamageFixedTail;
        var p = new byte[3 + len];
        p[0] = Protocol.NpcDamageUp;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(1), (ushort)len);
        p[3] = (byte)nb.Length; nb.CopyTo(p, 4);
        int o = 4 + nb.Length;
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o), st);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o + 4), hp);
        p[o + 8] = Protocol.NpcDamageFlagAttributed;
        return p;
    }

    /// <summary>flee as keyed positions (stand, then run, then stop): (t, x, y, z).</summary>
    public static List<(double t, float x, float y, float z)> FleeKeys(double t0, float x0, float y0, float z0, float ux, float uy, float speed, float len)
    {
        double n = Math.Sqrt(ux * ux + uy * uy);
        if (n < 1e-6 || speed <= 0 || len <= 0) throw new ArgumentException("flee needs a direction, a speed and a length");
        float dx = (float)(ux / n * len), dy = (float)(uy / n * len);
        return [(0, x0, y0, z0), (t0, x0, y0, z0), (t0 + len / speed, x0 + dx, y0 + dy, z0)];
    }

    /// <summary>The offset a teleport list gives at stream time t (every teleport due so far, summed).</summary>
    public static (float dx, float dy) Offset(IEnumerable<(double T, float Dx, float Dy)> tps, double t)
    {
        float x = 0, y = 0;
        foreach (var (T, Dx, Dy) in tps) if (t >= T) { x += Dx; y += Dy; }
        return (x, y);
    }

    // ---------------------------------------------------------------- the self-test (harness gate, not shipped)

    public static int SelfTest()
    {
        int fail = 0, pass = 0;
        void Check(bool c, string what) { if (c) pass++; else { fail++; Console.WriteLine($"FAIL  {what}"); } }

        // verdict names, every one the plan accepts
        foreach (var (s, want) in new[] { ("hit", HitVerdict.Hit), ("blocked", HitVerdict.Blocked), ("pb", HitVerdict.PerfectBlock), ("broken", HitVerdict.Broken) })
            Check(TryVerdict(s, out var v) && v == want, $"verdict name {s}");
        Check(!TryVerdict("dodge", out _), "an unknown verdict name is refused");

        // the verdict packets decode with the shipped decoder, each value and the swing/attacker flags intact
        foreach (var v in new[] { HitVerdict.Hit, HitVerdict.Blocked, HitVerdict.PerfectBlock, HitVerdict.Broken })
        {
            var pkt = Verdict(7, v, v == HitVerdict.Hit ? 12.5f : 0f, 18f, "ttkc_man_23", 4, 1);
            Check(pkt[0] == Protocol.HitVerdictUp, $"{v}: type 0x72");
            int len = BinaryPrimitives.ReadUInt16LittleEndian(pkt.AsSpan(1));
            Check(len == pkt.Length - 3, $"{v}: the frame length");
            var payload = pkt.AsSpan(3);
            Check(payload[0] == 1, $"{v}: to joiner 1");
            uint hid = BinaryPrimitives.ReadUInt32LittleEndian(payload[1..]);
            Check(HitVerdictMsg.TryDecode(hid, payload[5..], out var m) && m.HitId == 7 && m.Verdict == v && m.SwingId == 4 && m.SwingKnown &&
                  m.Attacker == "ttkc_man_23" && m.Stamina == 18f, $"{v}: decodes back");
        }
        var anon = Verdict(9, HitVerdict.Hit, 5, 0, "", 0, 1);
        Check(HitVerdictMsg.TryDecode(9, anon.AsSpan(3 + 5), out var am) && am.NoAttacker && !am.SwingKnown, "a nameless verdict is NoAttacker, no swing");

        // the row: an NpcAttack commit the agent's own parser reads back
        var outbox = new KcdMp.Client.ActionOutbox();
        var rowId = Guid.Parse("5810bf9a-45e9-e838-ad87-b5471c939c53");
        var rp = Row(outbox, "ttkc_man_23", rowId, 123456);
        Check(rp.Length > 3 + 16, "the row packet carries a body");
        Check(rp.AsSpan(3).IndexOf(rowId.ToByteArray()) >= 0 && rp.AsSpan(3).IndexOf(Encoding.ASCII.GetBytes("ttkc_man_23")) >= 0, "the row carries the GUID and the NPC");

        // the attributed hit: 0x30, the shipped fixed tail, the attributed flag
        var ah = AttributedHit("ttkc_man_23", 4f, 9f);
        Check(ah[0] == Protocol.NpcDamageUp && BinaryPrimitives.ReadUInt16LittleEndian(ah.AsSpan(1)) == ah.Length - 3, "the attributed hit's frame");
        Check(ah[3] == 11 && ah[^1] == Protocol.NpcDamageFlagAttributed, "name length and the attributed flag (0x04)");
        Check(BinaryPrimitives.ReadSingleLittleEndian(ah.AsSpan(4 + 11)) == 4f && BinaryPrimitives.ReadSingleLittleEndian(ah.AsSpan(8 + 11)) == 9f, "stamina then health");

        // flee keys and teleport offsets
        var k = FleeKeys(10, 100, 200, 50, 3, 4, 5, 25);
        Check(k.Count == 3 && k[1].t == 10 && Math.Abs(k[2].t - 15) < 1e-9 && Math.Abs(k[2].x - 115) < 1e-4 && Math.Abs(k[2].y - 220) < 1e-4, "flee: stand to t0, then 25 m in 5 s along (3,4)");
        var tps = new List<(double, float, float)> { (5, 30, 0), (9, 0, -40) };
        Check(Offset(tps, 4) == (0, 0) && Offset(tps, 5) == (30, 0) && Offset(tps, 9.5) == (30, -40), "teleport offsets add up from their times on");

        // a WO-165 outcome the joiner sends back parses on the host
        string txt = W164Text.HitOutcome(12, HitVerdict.PerfectBlock, 0, 7.5f);
        Check(W164Text.TryParseHitOutcome(txt, out uint oh, out var ov, out float hp2, out float st2) && oh == 12 && ov == HitVerdict.PerfectBlock && hp2 == 0 && st2 == 7.5f, $"the outcome text round-trips ({txt})");
        Check(!W164Text.TryParseHitOutcome("0 hit 1 1", out _, out _, out _, out _) && !W164Text.TryParseHitOutcome("3 parried 0 0", out _, out _, out _, out _) &&
              !W164Text.TryParseHitOutcome("3 hit NaN 0", out _, out _, out _, out _), "id 0, a reserved verdict and NaN are refused");

        Console.WriteLine($"WO165 synth verbs self-test: {pass} passed, {fail} failed");
        return fail;
    }

    public static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    public static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
}
