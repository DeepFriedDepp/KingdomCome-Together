// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text.RegularExpressions;

namespace KcdMp.Client;

/// <summary>
/// WO-203 I.3b (docs/WO-203-findings.md): the pure rules of the bodies acting the starter's conversation out. The bridge is
/// GameBridge.Wo203Conv.cs. The starter's engine names every gesture of its conversation in kcd.log; the starter's agent sends each as
/// a ConvAnim; the listener's agent picks the fragment's clip out of the game's own gesture database and the mod plays it on the body.
/// </summary>
public static class Wo203ConvRules
{
    /// <summary>The figure of the starter (the partner's figure on the listener) in a ConvAnim's body / face field.</summary>
    public const string Starter = "@";

    /// <summary>The roles the engine gives this player's own Henry in a conversation.</summary>
    public static bool IsPlayerRole(string who) => who is "HENRY" or "HRAC";

    private static readonly Regex AnimLine = new(@"^\[ID: (\d+)\] DialogCommand-Anim: dialogId=\d+, who=([A-Z0-9_]+), anim=(ADLG_[A-Za-z0-9_]+)", RegexOptions.Compiled);
    private static readonly Regex Soul = new(@"^[A-Za-z0-9_]{1,64}$", RegexOptions.Compiled);
    private static readonly Regex Frag = new(@"^ADLG_[A-Za-z0-9_]{1,60}$", RegexOptions.Compiled);

    /// <summary>"[ID: 233] DialogCommand-Anim: dialogId=233, who=BARA, anim=ADLG_Nod" -> (233, BARA, ADLG_Nod); null for any other line.</summary>
    public static (int Cid, string Who, string Fragment)? ParseAnim(string line)
    {
        var m = AnimLine.Match(line);
        if (!m.Success || !int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int cid)) return null;
        return (cid, m.Groups[2].Value, m.Groups[3].Value);
    }

    /// <summary>
    /// "New dialogue '... (normal - Id: 233) (...)' is starting. Params: souls = 'Ex: Dude; Ex: ttkc_barbora' ..." -> (233, [Dude,
    /// ttkc_barbora]) when this player (Dude) is one of its souls and at least one other soul is; null otherwise (an NPC's own talk, a
    /// monologue, any other line).
    /// </summary>
    public static (int Cid, string[] Others)? ParseOwnConversation(string line)
    {
        const string head = "New dialogue '", idTag = " - Id: ", souls = "souls = '";
        if (!line.StartsWith(head, StringComparison.Ordinal)) return null;
        int i = line.IndexOf(idTag, StringComparison.Ordinal);
        if (i < 0) return null;
        int e = line.IndexOf(')', i);
        if (e < 0 || !int.TryParse(line.AsSpan(i + idTag.Length, e - i - idTag.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int cid)) return null;
        int s = line.IndexOf(souls, e, StringComparison.Ordinal);
        if (s < 0) return null;
        int q = line.IndexOf('\'', s + souls.Length);
        if (q < 0) return null;
        bool me = false;
        var others = new List<string>();
        foreach (string part in line[(s + souls.Length)..q].Split(';'))
        {
            string p = part.Trim();
            int c = p.IndexOf(": ", StringComparison.Ordinal);
            if (c < 0) continue;
            string name = p[(c + 2)..];
            int sp = name.IndexOf(' ');
            if (sp >= 0) name = name[..sp];
            if (name == "Dude") me = true;
            else if (Soul.IsMatch(name)) others.Add(name);
        }
        return me && others.Count > 0 ? (cid, others.ToArray()) : null;
    }

    /// <summary>The conversation ended: "[ID: N] Dialog ending ..." / "[ID: N] Dialog interrupted ..." -> N; -1 otherwise.</summary>
    public static int ParseEnd(string line)
    {
        if (!line.StartsWith("[ID: ", StringComparison.Ordinal)) return -1;
        int b = line.IndexOf(']');
        if (b < 0) return -1;
        string rest = line[(b + 1)..];
        if (!rest.StartsWith(" Dialog ending", StringComparison.Ordinal) && !rest.StartsWith(" Dialog interrupted", StringComparison.Ordinal)) return -1;
        return int.TryParse(line.AsSpan(5, b - 5), NumberStyles.None, CultureInfo.InvariantCulture, out int id) ? id : -1;
    }

    /// <summary>
    /// One gesture's body and the body it faces. HENRY's gestures are the starter's figure, facing the conversation's first other soul;
    /// any other role is that soul (two-person conversations: the engine's log gives roles, not souls, so with more souls a role
    /// cannot be told apart and only the starter's own gestures go). Null when the gesture cannot be placed.
    /// </summary>
    public static (string Body, string Face)? Place(string who, string[] others)
    {
        if (others.Length == 0) return null;
        if (IsPlayerRole(who)) return (Starter, others[0]);
        return others.Length == 1 ? (others[0], Starter) : null;
    }

    // ---------------------------------------------------------------- the wire text: v1|cid|starter|body|fragment|face

    public static string AnimText(int cid, byte starter, string body, string fragment, string face) =>
        string.Create(CultureInfo.InvariantCulture, $"v1|{cid}|{starter}|{body}|{fragment}|{face}");

    public static bool TryParseAnim(string text, out int cid, out byte starter, out string body, out string fragment, out string face)
    {
        cid = 0; starter = 0; body = fragment = face = "";
        var f = text.Split('|');
        if (f.Length != 6 || f[0] != "v1") return false;
        if (!int.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out cid)) return false;
        if (!byte.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out starter)) return false;
        bool BodyOk(string s) => s == Starter || Soul.IsMatch(s);
        if (!BodyOk(f[3]) || !BodyOk(f[5]) || !Frag.IsMatch(f[4]) || (f[3] == Starter) == (f[5] == Starter)) return false;
        body = f[3]; fragment = f[4]; face = f[5];
        return true;
    }

    // ---------------------------------------------------------------- the clip of a fragment

    private static readonly Regex AdbFragment = new(@"<(ADLG_[A-Za-z0-9_]+)>(.*?)</\1>", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex AdbAnimation = new(@"<Fragment[^>]*>\s*<AnimLayer>\s*<Blend[^>]*/>\s*<Animation name=""([^""]+)""", RegexOptions.Compiled);

    /// <summary>A gesture database (male_adlg.adb / female_adlg.adb text) -> every fragment's clips in the file's order.</summary>
    public static Dictionary<string, List<string>> ParseAdb(string adb)
    {
        var out_ = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (Match m in AdbFragment.Matches(adb))
        {
            var list = out_.TryGetValue(m.Groups[1].Value, out var l) ? l : out_[m.Groups[1].Value] = [];
            foreach (Match a in AdbAnimation.Matches(m.Groups[2].Value)) list.Add(a.Groups[1].Value);
        }
        return out_;
    }

    /// <summary>
    /// The clip a fragment plays: a standing variant (never a bench one -- a standing body squats), not a weapon / injured / drunk one,
    /// not an upper-body-only one (it plays on the base layer), a neutral one when there is one; the conversation's n-th gesture takes
    /// variant n of them. The rule the maintainer watched live (2026-10-22, "convincing"). Null when the fragment has no standing clip.
    /// </summary>
    public static string? Pick(IReadOnlyDictionary<string, List<string>> db, string fragment, int n)
    {
        if (!db.TryGetValue(fragment, out var all)) return null;
        var good = all.Where(a => a.Contains("_stand") && !a.Contains("bench") && !a.Contains("weapon") && !a.Contains("injured")
                                  && !a.Contains("drunk") && !a.EndsWith("_upper", StringComparison.Ordinal)).ToList();
        if (good.Count == 0) good = all.Where(a => a.Contains("_stand") && !a.Contains("bench")).ToList();
        if (good.Count == 0) return null;
        var neutral = good.Where(a => a.Contains("neutral")).ToList();
        var pool = neutral.Count > 0 ? neutral : good;
        return pool[(int)((uint)n % (uint)pool.Count)];
    }
}
