// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KcdMp.Client;

/// <summary>
/// WO-203B (docs/WO-203B-findings.md): the pure rules of the minimal Act I session and the conversation camera. The bridge is
/// GameBridge.Wo203Cam.cs; the native half is wo203_cam.cpp (ops 94 / 95); the mod's half is kdcmp.lua's area "cam".
/// Wire texts (Conv channel, ProtocolWo203.cs; fields never hold '|', lists ';', sub-fields '~'):
///   1 Announce  v1|cid|kind|joinable|-|anchor|participants(soul;soul)|0,0,0|-
///   2 Join      v1|cid|auto
///   3 Snapshot  v1|cid|kind|anchor|participants|lines=0|open=0|timer=0/0
///   5 Cam       v1|cid|s1;s2;s3   each seq~tHostMs~px,py,pz~qx,qy,qz,qw~fov~cut~alias~ax,ay,az,yaw (relative to the anchor; the
///               last sub-field, appended by WO-203B, is the starter's anchor in the world -- the listener's B.3 framing check)
///  11 Leave     v1|cid|why
///  12 End       v1|cid|reason
/// cid is the starter's dialogue runtime id (as ConvAnim's); the starter is the frame's source.
/// </summary>
public static class Wo203CamRules
{
    /// <summary>The conversations a partner may join: the player's own talks (the engine's "normal" and "ingame" kinds); never a
    /// chat (an NPC's greeting), a monologue or a combat shout.</summary>
    public static bool JoinableKind(string kind) => kind is "normal" or "ingame";

    /// <summary>"New dialogue 'x (normal - Id: 233) (...)' ... souls = 'Ex: Dude; Ex: ttkc_barbora'" -> (233, normal, [ttkc_barbora]).</summary>
    public static (int Cid, string Kind, string[] Others)? ParseTalk(string line)
    {
        if (Wo203ConvRules.ParseOwnConversation(line) is not { } c) return null;
        int id = line.IndexOf(" - Id: ", StringComparison.Ordinal);
        int open = id > 0 ? line.LastIndexOf('(', id) : -1;
        if (open < 0) return null;
        return (c.Cid, line[(open + 1)..id], c.Others);
    }

    /// <summary>The engine's dialogue camera took the starter's view (C_DialogCameraManager::Activated's own line).</summary>
    public static bool IsCameraActivated(string line) => line.StartsWith("Dialog camera activated.", StringComparison.Ordinal);

    private static readonly Regex Soul = new(@"^[A-Za-z0-9_]{1,64}$", RegexOptions.Compiled);
    private static readonly Regex Kind = new(@"^[a-z ]{1,24}$", RegexOptions.Compiled);
    private static readonly Regex Word = new(@"^[a-z]{1,16}$", RegexOptions.Compiled);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ---------------------------------------------------------------- the prompt's text (a localization key of the mod's own)

    public const string LocFileName = "kct_conv.xml";
    public const string JoinKey = "ui_hud_kct_join_conversation";
    public const string LocXml = "<Table>\n<Row><Cell>" + JoinKey + "</Cell><Cell>Join conversation</Cell><Cell>Join conversation</Cell></Row>\n</Table>\n";

    // ---------------------------------------------------------------- texts

    public static string AnnounceText(int cid, string kind, bool joinable, string anchor, IEnumerable<string> others) =>
        string.Create(Inv, $"v1|{cid}|{kind}|{(joinable ? 1 : 0)}|-|{anchor}|{string.Join(';', others)}|0,0,0|-");

    public static bool TryParseAnnounce(string text, out int cid, out string kind, out bool joinable, out string anchor)
    {
        cid = 0; kind = anchor = ""; joinable = false;
        var f = text.Split('|');
        if (f.Length != 9 || f[0] != "v1" || !int.TryParse(f[1], NumberStyles.None, Inv, out cid) || !Kind.IsMatch(f[2])) return false;
        if (f[3] is not ("0" or "1") || !Soul.IsMatch(f[5])) return false;
        kind = f[2]; joinable = f[3] == "1"; anchor = f[5];
        return true;
    }

    public static string JoinText(int cid, bool auto) => string.Create(Inv, $"v1|{cid}|{(auto ? 1 : 0)}");

    public static bool TryParseCid(string text, out int cid, out string rest)
    {
        cid = 0; rest = "";
        var f = text.Split('|', 3);
        if (f.Length < 2 || f[0] != "v1" || !int.TryParse(f[1], NumberStyles.None, Inv, out cid)) return false;
        rest = f.Length == 3 ? f[2] : "";
        return true;
    }

    public static string SnapshotText(int cid, string kind, string anchor, IEnumerable<string> others) =>
        string.Create(Inv, $"v1|{cid}|{kind}|{anchor}|{string.Join(';', others)}|lines=0|open=0|timer=0/0");

    public static bool TryParseSnapshot(string text, out int cid, out string anchor)
    {
        cid = 0; anchor = "";
        var f = text.Split('|');
        if (f.Length != 8 || f[0] != "v1" || !int.TryParse(f[1], NumberStyles.None, Inv, out cid) || !Soul.IsMatch(f[3])) return false;
        anchor = f[3];
        return true;
    }

    public static string LeaveText(int cid, string why) => string.Create(Inv, $"v1|{cid}|{(Word.IsMatch(why) ? why : "other")}");
    public static string EndText(int cid, string reason) => string.Create(Inv, $"v1|{cid}|{(Word.IsMatch(reason) ? reason : "other")}");

    /// <summary>A Leave / End text's reason ("ended" when absent: an older sender).</summary>
    public static bool TryParseReason(string text, out int cid, out string reason)
    {
        reason = "ended";
        if (!TryParseCid(text, out cid, out string rest)) return false;
        if (rest.Length > 0) { if (!Word.IsMatch(rest)) return false; reason = rest; }
        return true;
    }

    // ---------------------------------------------------------------- camera samples

    public readonly record struct CamSample(int Seq, long T, float Px, float Py, float Pz, float Qx, float Qy, float Qz, float Qw, float Fov,
                                            bool Cut, float Ax, float Ay, float Az, float Yaw);

    public const int MaxSamplesPerMessage = 3;

    /// <summary>The most samples one op 94 call carries to the DLL (its ring keeps 48; the newest are what plays).</summary>
    public const int MaxSamplesPerCall = 32;

    private static string F(float v, string fmt) => v.ToString(fmt, Inv);

    public static string CamSampleText(CamSample s) =>
        string.Create(Inv, $"{s.Seq}~{s.T}~{F(s.Px, "F3")},{F(s.Py, "F3")},{F(s.Pz, "F3")}~{F(s.Qx, "F5")},{F(s.Qy, "F5")},{F(s.Qz, "F5")},{F(s.Qw, "F5")}"
                                + $"~{F(s.Fov, "F4")}~{(s.Cut ? 1 : 0)}~-~{F(s.Ax, "F2")},{F(s.Ay, "F2")},{F(s.Az, "F2")},{F(s.Yaw, "F4")}");

    public static string CamText(int cid, IEnumerable<CamSample> samples) =>
        string.Create(Inv, $"v1|{cid}|{string.Join(';', samples.Select(CamSampleText))}");

    public static bool TryParseCam(string text, out int cid, out List<CamSample> samples)
    {
        samples = [];
        if (!TryParseCid(text, out cid, out string rest) || rest.Length == 0) return false;
        foreach (string one in rest.Split(';'))
        {
            var f = one.Split('~');
            if (f.Length < 7) return false;
            if (!int.TryParse(f[0], NumberStyles.None, Inv, out int seq) || !long.TryParse(f[1], NumberStyles.AllowLeadingSign, Inv, out long t)) return false;
            if (!Floats(f[2], 3, out var p) || !Floats(f[3], 4, out var q) || !float.TryParse(f[4], NumberStyles.Float, Inv, out float fov) || f[5] is not ("0" or "1")) return false;
            if (!float.IsFinite(fov) || fov <= 0.05f || fov >= 3.0f) return false;
            float[] a = [0, 0, 0, 0];
            if (f.Length >= 8 && !Floats(f[7], 4, out a)) return false;   // WO-203B's anchor sub-field (absent: an older sender)
            samples.Add(new CamSample(seq, t, p[0], p[1], p[2], q[0], q[1], q[2], q[3], fov, f[5] == "1", a[0], a[1], a[2], a[3]));
        }
        return samples.Count is > 0 and <= MaxSamplesPerMessage;
    }

    private static bool Floats(string s, int n, out float[] v)
    {
        v = new float[n];
        var f = s.Split(',');
        if (f.Length != n) return false;
        for (int i = 0; i < n; i++)
            if (!float.TryParse(f[i], NumberStyles.Float, Inv, out v[i]) || !float.IsFinite(v[i])) return false;
        return true;
    }

    // ---------------------------------------------------------------- the clocks

    /// <summary>The starter: a sample taken at <paramref name="sampleQpcMs"/> on this machine's QPC clock, on the host's clock
    /// (this machine's Unix ms plus the WO-98 offset to the host; 0 on the host itself).</summary>
    public static long HostMs(long sampleQpcMs, long nowQpcMs, long nowUnixMs, double? offsetMs) =>
        nowUnixMs + (long)Math.Round(offsetMs is double o && double.IsFinite(o) ? o : 0.0) - (nowQpcMs - sampleQpcMs);

    /// <summary>The listener: a host-clock time on this machine's QPC clock (what the DLL plays against).</summary>
    public static long LocalQpcMs(long hostMs, long nowQpcMs, long nowUnixMs, double? offsetMs) =>
        nowQpcMs - (nowUnixMs + (long)Math.Round(offsetMs is double o && double.IsFinite(o) ? o : 0.0) - hostMs);

    // ---------------------------------------------------------------- the DLL's payloads (wo203_cam.cpp)

    /// <summary>Op 95 cmd 2's reply: [n] n x {t:i64, rel pos 3f, rel quat 4f, fov f, anchor 3f, yaw f, flags:1}.</summary>
    public static List<(long QpcMs, CamSample Sample)> DecodePoll(byte[] p)
    {
        var out_ = new List<(long, CamSample)>();
        if (p.Length < 1) return out_;
        int n = p[0], o = 1;
        const int each = 8 + 4 * 12 + 1;
        for (int i = 0; i < n && o + each <= p.Length; i++, o += each)
        {
            long t = BinaryPrimitives.ReadInt64LittleEndian(p.AsSpan(o));
            int at = o;
            float R(int k) => BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(at + 8 + 4 * k));
            out_.Add((t, new CamSample(0, t, R(0), R(1), R(2), R(3), R(4), R(5), R(6), R(7), (p[o + each - 1] & 1) != 0, R(8), R(9), R(10), R(11))));
        }
        return out_;
    }

    /// <summary>Op 94 cmd 2's body after [op][cmd]: [n] n x {t:i64 local QPC ms, pos 3f, quat 4f, fov f, cut:1, anchor x,y,z,yaw 4f}.</summary>
    public static byte[] EncodeSamples(IReadOnlyList<(long LocalQpcMs, CamSample S)> xs)
    {
        const int each = 8 + 4 * 8 + 1 + 4 * 4;
        var b = new byte[2 + 1 + xs.Count * each];
        b[0] = 2;   // cmd samples (the op byte is the pipe call's)
        b[1] = (byte)xs.Count;
        int o = 2;
        foreach (var (t, s) in xs)
        {
            BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(o), t); o += 8;
            foreach (float v in new[] { s.Px, s.Py, s.Pz, s.Qx, s.Qy, s.Qz, s.Qw, s.Fov }) { BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(o), v); o += 4; }
            b[o++] = (byte)(s.Cut ? 1 : 0);
            foreach (float v in new[] { s.Ax, s.Ay, s.Az, s.Yaw }) { BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(o), v); o += 4; }
        }
        return b.AsSpan(0, o).ToArray();
    }

    /// <summary>[cmd][len][name] -- op 94 cmd 7 (enter by name) / with [every] before the name, op 95 cmd 4 (arm by name).</summary>
    public static byte[]? NameCmd(byte cmd, string name, byte? every = null)
    {
        if (!Soul.IsMatch(name)) return null;
        byte[] n = Encoding.ASCII.GetBytes(name);
        var b = new List<byte> { cmd };
        if (every is byte e) b.Add(e);
        b.Add((byte)n.Length);
        b.AddRange(n);
        return b.ToArray();
    }

    // ---------------------------------------------------------------- the ways out (the DLL's codes, wo203_cam.cpp kWhy)

    public static readonly string[] WhyNames = ["leave", "end", "silence", "combat", "scene", "load", "menu", "disconnect", "fault", "lost", "taken", "reenter", "hit", "off"];

    public static byte WhyCode(string why) => why switch
    {
        "cancel" => 0,
        "ended" or "end" or "cancelled" => 1,
        _ => Array.IndexOf(WhyNames, why) is int i and >= 0 ? (byte)i : (byte)0,
    };

    public static string WhyName(byte code) => code < WhyNames.Length ? WhyNames[code] : "other";
}
