// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;

namespace KcdMp.Client;

/// <summary>WO-209 (docs/WO-209-findings.md): the agent's pure rules -- the switches, the DLL's switch word, the opponent mirror's wire.</summary>
public static class Wo209Rules
{
    /// <summary>Every WO-209 switch the agent knows (the mod's mp_* commands send "name=on|off" as w209_cfg). New mechanisms ship on.</summary>
    public static readonly string[] SwitchNames = ["copy_opponent_mirror", "copy_attack_ai", "copy_paired_engine", "copy_oneshot_request", "clock_quiet_in_skip", "forced_talk_once", "quest_wake_with_host"];

    /// <summary>The switches that start off; the bench's KCDMP_TEST_W209_ON names one on.</summary>
    public static readonly string[] DefaultOff = [];

    /// <summary>A switch the mod has not set: on, unless it starts off (DefaultOff) and the bench's on-list does not name it, or a twin names it off.</summary>
    public static bool Default(string name, string? twinOff, string? benchOn) =>
        !Wo207Rules.TwinOff(twinOff, name) && (!DefaultOff.Contains(name, StringComparer.Ordinal) || Wo207Rules.TwinOff(benchOn, name));

    /// <summary>The DLL's switch word (op 134), the same bits as wo209_rules.h kBit*.</summary>
    public static uint ConfigBits(Func<string, bool> on) => (on("copy_opponent_mirror") ? 1u : 0u) | (on("copy_attack_ai") ? 2u : 0u)
                                                             | (on("copy_paired_engine") ? 4u : 0u) | (on("copy_oneshot_request") ? 8u : 0u)
                                                             | (on("quest_wake_with_host") ? 16u : 0u);

    /// <summary>"name=on|off" from the mod: false when malformed or not one of <see cref="SwitchNames"/>.</summary>
    public static bool TryParseCfg(string? arg, out string name, out bool on)
    {
        name = ""; on = false;
        if (string.IsNullOrEmpty(arg)) return false;
        int eq = arg.IndexOf('=');
        if (eq <= 0) return false;
        string n = arg[..eq], v = arg[(eq + 1)..];
        if (v is not ("on" or "off") || !SwitchNames.Contains(n, StringComparer.Ordinal)) return false;
        name = n; on = v == "on";
        return true;
    }

    // ---------------------------------------------------------------- A: the opponent mirror

    /// <summary>The host's kinds on the wire (and from the DLL's op 136): nobody, the host's own player, a peer's figure, another NPC.</summary>
    public const byte HostNone = 0, HostPlayer = 1, HostFigure = 2, HostNpc = 3;
    /// <summary>The joiner's kinds for its DLL (op 137): nobody, the local player, an entity here (a figure or a copy).</summary>
    public const byte HereNone = 0, HerePlayer = 1, HereEntity = 2;

    private static bool ValidName(ReadOnlySpan<byte> nm)
    {
        if (nm.Length == 0 || nm.Length > Wire.Protocol.MaxNpcNameLen) return false;
        foreach (byte c in nm) if (!(c is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z' or >= (byte)'0' and <= (byte)'9' or (byte)'_')) return false;
        return true;
    }

    /// <summary>
    /// The host: the DLL's op 136 reply ([count] per body [nameLen][name][kind][oppEid:4][oppNameLen][oppName]) as the wire's body
    /// ([count] per body [nameLen][name][kind][ghost][oppNameLen][oppName]): a figure's entity becomes its peer's ghost id; a figure no
    /// peer owns is told as nobody (the joiner cannot name it). False when the DLL's bytes are malformed.
    /// </summary>
    public static bool TryHostWire(ReadOnlySpan<byte> dll, Func<uint, byte> ghostOfEntity, out byte[] wire, out int bodies)
    {
        wire = []; bodies = 0;
        if (dll.Length < 1) return false;
        int count = dll[0], o = 1;
        var ms = new MemoryStream();
        ms.WriteByte(0);
        for (int i = 0; i < count; i++)
        {
            if (o + 1 > dll.Length) return false;
            int nl = dll[o];
            if (o + 1 + nl + 6 > dll.Length || !ValidName(dll.Slice(o + 1, nl))) return false;
            var nm = dll.Slice(o + 1, nl);
            o += 1 + nl;
            byte kind = dll[o];
            uint oppEid = BinaryPrimitives.ReadUInt32LittleEndian(dll.Slice(o + 1, 4));
            int onl = dll[o + 5];
            o += 6;
            if (o + onl > dll.Length || kind > HostNpc) return false;
            var on = dll.Slice(o, onl);
            o += onl;
            byte ghost = 0xFF;
            if (kind == HostFigure) { ghost = ghostOfEntity(oppEid); if (ghost == 0xFF) kind = HostNone; }
            if (kind == HostNpc && !ValidName(on)) kind = HostNone;
            ms.WriteByte((byte)nl); ms.Write(nm);
            ms.WriteByte(kind); ms.WriteByte(ghost);
            if (kind == HostNpc) { ms.WriteByte((byte)on.Length); ms.Write(on); } else ms.WriteByte(0);
            bodies++;
        }
        if (o != dll.Length) return false;
        wire = ms.ToArray();
        wire[0] = (byte)bodies;
        return true;
    }

    /// <summary>
    /// The joiner: the host's wire body as the DLL's op 137 ([n] per copy [copyEid:4][kind:1][targetEid:4]), in this world's entities:
    /// the host's own player is the host's figure here (hostGhost's), the joiner's own figure there is the local player here, another
    /// peer's figure is that figure here, another NPC is its copy here. A body with no copy here, or an opponent this world cannot name,
    /// is left out (unknown counts them). False when malformed.
    /// </summary>
    public static bool TryJoinerOp(ReadOnlySpan<byte> wire, byte myGhost, byte hostGhost, Func<string, uint> copyOf, Func<byte, uint> figureOf,
                                   out byte[] op, out int known, out int unknown)
    {
        op = []; known = 0; unknown = 0;
        if (wire.Length < 1) return false;
        int count = wire[0], o = 1;
        var ms = new MemoryStream();
        ms.WriteByte(0);
        Span<byte> rec = stackalloc byte[9];
        for (int i = 0; i < count; i++)
        {
            if (o + 1 > wire.Length) return false;
            int nl = wire[o];
            if (o + 1 + nl + 3 > wire.Length || !ValidName(wire.Slice(o + 1, nl))) return false;
            string name = System.Text.Encoding.ASCII.GetString(wire.Slice(o + 1, nl));
            o += 1 + nl;
            byte kind = wire[o], ghost = wire[o + 1];
            int onl = wire[o + 2];
            o += 3;
            if (o + onl > wire.Length || kind > HostNpc) return false;
            string opp = onl > 0 && ValidName(wire.Slice(o, onl)) ? System.Text.Encoding.ASCII.GetString(wire.Slice(o, onl)) : "";
            o += onl;
            uint copy = copyOf(name);
            byte here = HereNone; uint target = 0;
            switch (kind)
            {
                case HostPlayer: target = hostGhost != 0xFF ? figureOf(hostGhost) : 0; here = target != 0 ? HereEntity : HereNone; break;
                case HostFigure when ghost == myGhost && myGhost != 0xFF: here = HerePlayer; break;
                case HostFigure: target = figureOf(ghost); here = target != 0 ? HereEntity : HereNone; break;
                case HostNpc: target = opp.Length > 0 ? copyOf(opp) : 0; here = target != 0 ? HereEntity : HereNone; break;
            }
            // an opponent this world cannot name: nothing is told for this copy (its last word stands until it goes stale)
            if (copy == 0 || (kind != HostNone && here == HereNone) || known >= 255) { unknown++; continue; }
            BinaryPrimitives.WriteUInt32LittleEndian(rec, copy);
            rec[4] = here;
            BinaryPrimitives.WriteUInt32LittleEndian(rec[5..], target);
            ms.Write(rec);
            known++;
        }
        if (o != wire.Length) return false;
        op = ms.ToArray();
        op[0] = (byte)known;
        return true;
    }

    /// <summary>
    /// A ("adds only, never a removal"): WO-132's release of an engaged copy whose host NPC still fights (it turned to the host or another
    /// body) took it out of the joiner's skirmish -- with the mirror on it stays in, and only its opponent changes. The fight's end (the
    /// host's NPC out of combat with no target), a death and a knockout still release it. Off (the twin): released as before.
    /// </summary>
    public static bool KeepEngaged(bool mirrorOn, bool hostNpcInCombat, bool hostNpcHasTarget) => mirrorOn && (hostNpcInCombat || hostNpcHasTarget);

    /// <summary>The wire body's bodies: each NPC's name and its host kind (no translation) -- the joiner's own record of whom each host NPC
    /// fights. Empty when malformed.</summary>
    public static List<(string Name, byte Kind)> WireKinds(ReadOnlySpan<byte> wire)
    {
        var out_ = new List<(string, byte)>();
        if (wire.Length < 1) return out_;
        int count = wire[0], o = 1;
        for (int i = 0; i < count; i++)
        {
            if (o + 1 > wire.Length) return [];
            int nl = wire[o];
            if (o + 1 + nl + 3 > wire.Length || !ValidName(wire.Slice(o + 1, nl))) return [];
            string name = System.Text.Encoding.ASCII.GetString(wire.Slice(o + 1, nl));
            o += 1 + nl;
            byte kind = wire[o];
            int onl = wire[o + 2];
            o += 3 + onl;
            if (o > wire.Length || kind > HostNpc) return [];
            out_.Add((name, kind));
        }
        return o == wire.Length ? out_ : [];
    }

    /// <summary>
    /// B: the host's verdict of a copy's blow (its fallback: no outcome within its 450 ms hold) waits this long on the joiner -- the copy's own
    /// attack here comes a stream delay after the host NPC's, and its blow may still land (d2-defence-early: a fallback, then the local hit
    /// 160 ms later: the joiner took both). A judgement here in the wait skips the host's verdict; this engine's "none" ends the wait.
    /// </summary>
    public const int VerdictWaitMs = 500;
    public static bool VerdictWaits(bool attackAi, bool defenceLocal, bool knownAttacker) => attackAi && defenceLocal && knownAttacker;

    /// <summary>The mirror's word on a copy counts as the host's fight for this long (the stream resends every 1.5 s while one fights).</summary>
    public const long MirrorFightFreshMs = 4000;

    /// <summary>
    /// A ("adds only"): WO-132's sweep lets an engaged copy go when no host combat state came for 3 s (d1-defence-early: a fighting copy was
    /// taken out of the joiner's skirmish this way and re-added by the mirror 0.1 s later). With the mirror on, a copy whose host NPC still
    /// fights someone by the mirror's stream stays. Off (the twin): released as before.
    /// </summary>
    public static bool StaleKeeps(bool mirrorOn, byte lastHostKind, long sinceMs) => mirrorOn && lastHostKind != HostNone && sinceMs >= 0 && sinceMs <= MirrorFightFreshMs;

    // ---------------------------------------------------------------- D.1 the joiner's sleep

    /// <summary>
    /// D.1 (mp_clock_quiet_in_skip): may the joiner's clock correction (WO-200) write a ratio while this machine's own time-lapse runs (a sleep
    /// or a wait)? No: the game runs its skip on its own ratio, and WO-200's reset to 15 three seconds into the smoke's 9 h sleep slowed the
    /// time-lapse until the joiner's sleep ended 56.5 s after the host's. Off (the twin): the reset is written as before.
    /// </summary>
    public static bool RatioWriteAllowed(bool skipping, bool on) => !(on && skipping);

    // ---------------------------------------------------------------- D.2 the duplicate forced talk

    /// <summary>D.2: two starts of one forced dialogue this close (the host's clock) are a tie, and the host's yields: its own quest flow forced
    /// it from a State the joiner's talk set there (the bench: the same second).</summary>
    public const long ForcedTieMs = 1500;

    /// <summary>D.2: the announce's ninth field (WO-203's unused "-") carries a forced talk's dialogue and its start: "path@hostMs".</summary>
    public static string WithForcedTag(string announce, string path, long startHostMs)
    {
        var f = announce.Split('|');
        if (f.Length != 9 || string.IsNullOrEmpty(path) || path.Contains('|') || path.Contains('@')) return announce;
        f[8] = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{path}@{startHostMs}");
        return string.Join('|', f);
    }

    public static bool TryParseForcedTag(string announce, out string path, out long startHostMs)
    {
        path = ""; startHostMs = 0;
        var f = announce.Split('|');
        if (f.Length != 9 || f[0] != "v1" || f[4] != Wo203CamRules.ForcedMark) return false;
        int at = f[8].LastIndexOf('@');
        if (at <= 0 || !long.TryParse(f[8][(at + 1)..], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out startHostMs)) return false;
        path = f[8][..at];
        return path.Length > 0;
    }

    /// <summary>
    /// D.2 (mp_forced_talk_once): this machine's forced talk and the partner's are the same dialogue -- does this one give way? The later start
    /// gives way (it is the duplicate: a quest flow woke by the other's State); a tie (within ForcedTieMs) is the host's to give. The one
    /// that gives way ends the game's own way and its player listens to the partner's (C.4's join). Off (the twin): both run.
    /// </summary>
    public static bool ForcedTalkYields(bool on, string myPath, string partnerPath, long myStartMs, long partnerStartMs, bool iAmHost) =>
        on && myPath.Length > 0 && string.Equals(myPath, partnerPath, StringComparison.Ordinal)
        && (myStartMs > partnerStartMs + ForcedTieMs || (Math.Abs(myStartMs - partnerStartMs) <= ForcedTieMs && iAmHost));
}
