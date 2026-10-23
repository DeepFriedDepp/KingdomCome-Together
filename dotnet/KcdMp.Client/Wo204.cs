// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

/// <summary>
/// WO-204 (docs/WO-204-findings.md): the pure rules of the agent's half.
///   III.1 a dialogue is a conversation (announced, joinable, gestured) only when the engine's own data says it is one.
/// </summary>
public static class Wo204Rules
{
    // ---- the switches (each mechanism ships on, each with its own switch; mp_w204_status prints them) ----
    public static readonly string[] SwitchNames = ["conv_no_barks", "conv_lines", "conv_voice", "conv_options", "conv_trialog_roles", "copy_unstick", "copy_seat_hold", "copy_tpose_watch", "blow_joiner_values"];

    public static bool DefaultOn(string name) => true;

    /// <summary>The DLL's op 115 bits (native/KCDMP/wo204_rules.h): 1 copy_seat_hold, 2 copy_tpose_watch.</summary>
    public static uint ConfigBits(Func<string, bool> on) => (on("copy_seat_hold") ? 1u : 0u) | (on("copy_tpose_watch") ? 2u : 0u);

    /// <summary>"name=on|off" (the Lua's w204_cfg event).</summary>
    public static bool TryParseCfg(string? arg, out string name, out bool on)
    {
        name = ""; on = false;
        if (string.IsNullOrEmpty(arg)) return false;
        int eq = arg.IndexOf('=');
        if (eq <= 0) return false;
        name = arg[..eq];
        string v = arg[(eq + 1)..];
        if (Array.IndexOf(SwitchNames, name) < 0 || v is not ("on" or "off")) return false;
        on = v == "on";
        return true;
    }

    // ---------------------------------------------------------------- IV.1 every join pauses the world until the joiner is ready

    /// <summary>The host's paused join aborts after this long with no message at all from the joiner (his load progress every
    /// <see cref="JoinProgressEveryMs"/>, an ack, their ready).</summary>
    public const int JoinLoadSilenceS = 60;
    public const int JoinProgressEveryMs = 5000;
    public static bool JoinLoadSilent(long quietMs) => quietMs >= JoinLoadSilenceS * 1000L;

    /// <summary>W203 JoinProgress (kind 7): "v1|&lt;joinId hex&gt;|&lt;where&gt;|&lt;seconds since the world arrived&gt;".</summary>
    public static string JoinProgressText(uint joinId, string where, int secs) =>
        FormattableString.Invariant($"v1|{joinId:x8}|{where}|{secs}");

    public static bool TryParseJoinProgress(string text, out uint joinId, out string where, out int secs)
    {
        joinId = 0; where = ""; secs = 0;
        var f = text.Split('|');
        if (f.Length != 4 || f[0] != "v1" || f[1].Length != 8) return false;
        if (!uint.TryParse(f[1], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out joinId)) return false;
        if (f[2].Length is 0 or > 16 || !f[2].All(char.IsAsciiLetter)) return false;
        if (!int.TryParse(f[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out secs)) return false;
        where = f[2];
        return true;
    }

    // ---------------------------------------------------------------- IV.3 the joiner's blows land with what they did

    /// <summary>The joiner's own engine judged the blow blocked: stamina only, no health (session D: "st30.0/hp0.0" -- the host's engine
    /// re-judged them as hits).</summary>
    public static bool JoinerJudgedBlocked(float health, float stamina) => health <= 0.05f && stamina > 0.05f;

    public const int BlowLateMs = 1000;

    /// <summary>A blow that arrived out of a stall of the joiner's link: within 500 ms after a position that came <see cref="BlowLateMs"/>
    /// or more after the one before it -- the blow waited with the stream (its lateness is about that gap).</summary>
    public static bool BlowLate(long gapBeforeMs, long sinceThatPositionMs) => gapBeforeMs >= BlowLateMs && sinceThatPositionMs <= 500;

    // ---------------------------------------------------------------- IV.5 a copy stuck in a state the engine cannot leave

    /// <summary>"[Error] /ai//(NPC)&lt;name&gt;/NPC Context [NPCContext]:NPC state search has failed!" -> the NPC; null otherwise. Session D's
    /// joiner printed it 2,577 times for one copy (ttkc_marusa: held in a milking unstance with its bucket, every mirrored activity
    /// unreachable from there); the host printed it 0 times.</summary>
    public static string? StateSearchFailedNpc(string line)
    {
        const string tail = "/NPC Context [NPCContext]:NPC state search has failed!", npc = "(NPC)";
        int m = line.IndexOf(tail, StringComparison.Ordinal);
        if (m < 0) return null;
        int a = line.LastIndexOf(npc, m, StringComparison.Ordinal);
        if (a < 0 || a + npc.Length >= m) return null;
        string name = line[(a + npc.Length)..m];
        return name.Length is > 0 and <= 64 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? name : null;
    }

    public const int UnstickFailures = 5;          // failed searches ...
    public const int UnstickWindowMs = 60_000;     // ... within this long
    public const int UnstickEveryMs = 120_000;     // one release per copy at most this often

    /// <summary>Release this copy now? Enough failures in the window, not released recently, a host copy (never a partner's figure).</summary>
    public static bool UnstickDue(string name, int failuresInWindow, long sinceLastReleaseMs) =>
        !name.StartsWith("kcd2mp_", StringComparison.Ordinal) && failuresInWindow >= UnstickFailures && sinceLastReleaseMs >= UnstickEveryMs;

    // ---------------------------------------------------------------- Act V: both players' notes on every mark

    /// <summary>The mp_mark event's argument "odd note=&lt;text&gt;" -> the note ("" for none).</summary>
    public static string MarkNoteOf(string? arg)
    {
        string a = (arg ?? "").Trim();
        int i = a.IndexOf(" note=", StringComparison.Ordinal);
        return i < 0 ? "" : CleanNote(a[(i + 6)..]);
    }

    /// <summary>The mod's own note alphabet (kdcmp.lua KCD2MP_Mark): letters, digits, spaces, plain punctuation; 120 characters.</summary>
    public static string CleanNote(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in s) if (char.IsAsciiLetterOrDigit(c) || " .,;:!?'-()/".Contains(c)) sb.Append(c);
        string t = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return t.Length > 120 ? t[..120] : t;
    }

    public static string MarkNoteText(string markId, string note) => $"v1|{markId}|{CleanNote(note)}";

    public static bool TryParseMarkNote(string text, out string markId, out string note)
    {
        markId = note = "";
        var f = text.Split('|', 3);
        if (f.Length != 3 || f[0] != "v1" || !KcdMp.Wire.W164Text.IsMarkId(f[1])) return false;
        markId = f[1]; note = CleanNote(f[2]);
        return note.Length > 0;
    }

    // ---------------------------------------------------------------- III.3 / III.4 the listener's lines, options and vote

    private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>A wire field: printable ASCII, never the separators ('|' fields, ';' rows, '~' sub-fields).</summary>
    public static string Field(string? s, int max = 120)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in s ?? "") { if (c >= ' ' && c <= '~' && c != '|' && c != ';' && c != '~') sb.Append(c); else if (c is '|' or ';' or '~') sb.Append('/'); }
        string t = sb.ToString();
        return t.Length > max ? t[..max] : t;
    }

    /// <summary>The line's StringName (what the HUD localizes as "@&lt;StringName&gt;"): the voice file's name after its first '_' (the
    /// speaker's prefix), without a folder or an extension. "Localization/dialog/x/dude_zpet_hejtman_s_tak_co_syn_JIUh" -> "zpet_hejtman_s_tak_co_syn_JIUh".</summary>
    public static string StringNameOf(string voicePath)
    {
        string f = voicePath.Replace('\\', '/');
        int slash = f.LastIndexOf('/');
        if (slash >= 0) f = f[(slash + 1)..];
        int dot = f.LastIndexOf('.');
        if (dot > 0) f = f[..dot];
        int u = f.IndexOf('_');
        return u > 0 && u + 1 < f.Length ? f[(u + 1)..] : f;
    }

    public readonly record struct ConvLineMsg(int Cid, int LineId, string Speaker, string Name, bool IsPlayer, string StringName, string Path, int DurMs, bool Earlier);

    /// <summary>ConvLine (kind 4): "v1|cid|lineId|speakerSoul|speakerName|isPlayer|stringName|voicePath|durMs|startHostMs|earlier" (WO-203's
    /// design §2); speakerSoul "@" = the starter's own Henry (his figure on the listener's screen).</summary>
    public static string LineText(ConvLineMsg l, long startHostMs) => string.Create(Inv,
        $"v1|{l.Cid}|{l.LineId}|{Field(l.Speaker, 64)}|{Field(l.Name, 48)}|{(l.IsPlayer ? 1 : 0)}|{Field(l.StringName, 96)}|{Field(l.Path, 180)}|{l.DurMs}|{startHostMs}|{(l.Earlier ? 1 : 0)}");

    public static bool TryParseLine(string text, out ConvLineMsg l)
    {
        l = default;
        var f = text.Split('|');
        if (f.Length != 11 || f[0] != "v1") return false;
        if (!int.TryParse(f[1], System.Globalization.NumberStyles.None, Inv, out int cid) || !int.TryParse(f[2], System.Globalization.NumberStyles.None, Inv, out int id)) return false;
        if (f[3].Length is 0 or > 64 || f[6].Length is 0 or > 96 || !f[6].All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) return false;
        if (!int.TryParse(f[8], System.Globalization.NumberStyles.None, Inv, out int dur) || dur > 60000) return false;
        if (f[5] is not ("0" or "1") || f[10] is not ("0" or "1")) return false;
        l = new ConvLineMsg(cid, id, f[3], f[4], f[5] == "1", f[6], f[7], dur, f[10] == "1");
        return true;
    }

    /// <summary>One option row as the starter's HUD showed it: its loc key (the listener shows "@key"), or the shown text ("=text").</summary>
    public readonly record struct OptRow(int Index, bool Used, string Icon, int Difficulty, int Value, string Prompt);

    /// <summary>The DLL's op 91 reply: [armed][current][voteRow][n] per row [used][iconLen][icon][diff:4][val:4][flag][textLen][text],
    /// then [snapSeq:4][onRight:1] (WO-204). Null when unreadable.</summary>
    public static List<OptRow>? ParseOptionsPoll(byte[] p, out int current, out uint seq, out bool onRight)
    {
        current = -1; seq = 0; onRight = false;
        if (p.Length < 4) return null;
        current = p[1] == 255 ? -1 : p[1];
        int n = p[3], o = 4;
        var rows = new List<OptRow>();
        for (int i = 0; i < n; i++)
        {
            if (o + 2 > p.Length) return null;
            bool used = p[o] != 0; int il = p[o + 1];
            if (o + 2 + il + 9 + 1 > p.Length) return null;
            string icon = System.Text.Encoding.ASCII.GetString(p, o + 2, il); o += 2 + il;
            int diff = BitConverter.ToInt32(p, o), val = BitConverter.ToInt32(p, o + 4); o += 9;
            int tl = p[o];
            if (o + 1 + tl > p.Length) return null;
            string text = System.Text.Encoding.UTF8.GetString(p, o + 1, tl); o += 1 + tl;
            string prompt = text.StartsWith('@') && text.Length > 1 && text[1..].All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? text[1..] : "=" + PlainText(text);
            rows.Add(new OptRow(i, used, Field(icon, 40), diff, val, Field(prompt, 100)));
        }
        if (o + 5 <= p.Length) { seq = BitConverter.ToUInt32(p, o); onRight = p[o + 4] != 0; }
        return rows;
    }

    /// <summary>A shown text without its HTML marks.</summary>
    public static string PlainText(string html)
    {
        var sb = new System.Text.StringBuilder();
        bool tag = false;
        foreach (char c in html) { if (c == '<') tag = true; else if (c == '>') tag = false; else if (!tag) sb.Append(c); }
        return sb.ToString().Trim();
    }

    /// <summary>ConvOptions (kind 7): "v1|cid|decId|timerS|rows", each row "index~seqName~promptKey~used~icon~difficulty~value~ends" (§2;
    /// seqName and ends are not known on this side: "-" and 0).</summary>
    public static string OptionsText(int cid, int decId, int timerS, IEnumerable<OptRow> rows) => string.Create(Inv,
        $"v1|{cid}|{decId}|{timerS}|{string.Join(';', rows.Select(r => $"{r.Index}~-~{Field(r.Prompt, 100)}~{(r.Used ? 1 : 0)}~{(r.Icon.Length == 0 ? "-" : Field(r.Icon, 40))}~{r.Difficulty}~{r.Value}~0"))}");

    public static bool TryParseOptions(string text, out int cid, out int decId, out List<OptRow> rows)
    {
        cid = decId = 0; rows = [];
        var f = text.Split('|');
        if (f.Length != 5 || f[0] != "v1" || !int.TryParse(f[1], System.Globalization.NumberStyles.None, Inv, out cid)
            || !int.TryParse(f[2], System.Globalization.NumberStyles.None, Inv, out decId)) return false;
        foreach (string r in f[4].Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var g = r.Split('~');
            if (g.Length != 8 || !int.TryParse(g[0], System.Globalization.NumberStyles.None, Inv, out int idx)) return false;
            int.TryParse(g[5], System.Globalization.NumberStyles.Integer, Inv, out int diff);
            int.TryParse(g[6], System.Globalization.NumberStyles.Integer, Inv, out int val);
            rows.Add(new OptRow(idx, g[3] == "1", g[4] == "-" ? "" : g[4], diff, val, g[2]));
        }
        return rows.Count is > 0 and <= 8;
    }

    /// <summary>ConvVote (kind 8): "v1|cid|decId|index" or "v1|cid|decId|clear" (index -1).</summary>
    public static string VoteText(int cid, int decId, int index) => string.Create(Inv, $"v1|{cid}|{decId}|{(index < 0 ? "clear" : index.ToString(Inv))}");

    public static bool TryParseVote(string text, out int cid, out int decId, out int index)
    {
        cid = decId = 0; index = -1;
        var f = text.Split('|');
        if (f.Length != 4 || f[0] != "v1" || !int.TryParse(f[1], System.Globalization.NumberStyles.None, Inv, out cid)
            || !int.TryParse(f[2], System.Globalization.NumberStyles.None, Inv, out decId)) return false;
        if (f[3] == "clear") return true;
        return int.TryParse(f[3], System.Globalization.NumberStyles.None, Inv, out index) && index < 8;
    }

    /// <summary>ConvChosen (kind 9): "v1|cid|decId|index".</summary>
    public static string ChosenText(int cid, int decId, int index) => string.Create(Inv, $"v1|{cid}|{decId}|{index}");

    /// <summary>The engine's "Topic index 'N' selected." (0-based: the HUD's row) -> N; -1 for any other line.</summary>
    public static int TopicSelected(string line)
    {
        const string head = "Topic index '", tail = "' selected.";
        // the log tail splits on LF only: the live line keeps a trailing character (it measured 26 for these 25), so the end is trimmed
        var t = line.AsSpan().TrimEnd();
        while (t.Length > 0 && char.IsControl(t[^1])) t = t[..^1].TrimEnd();
        if (!t.StartsWith(head, StringComparison.Ordinal) || !t.EndsWith(tail, StringComparison.Ordinal)) return -1;
        return int.TryParse(t.Slice(head.Length, t.Length - head.Length - tail.Length), System.Globalization.NumberStyles.None, Inv, out int n) && n < 8 ? n : -1;
    }

    /// <summary>The rows for the mod's Lua: "index~prompt~used~icon~difficulty~value;..." (prompt: a loc key, or "=" and the shown text).</summary>
    public static string RowsForLua(IEnumerable<OptRow> rows) =>
        string.Join(';', rows.Select(r => string.Create(Inv, $"{r.Index}~{Field(r.Prompt, 100)}~{(r.Used ? 1 : 0)}~{(r.Icon.Length == 0 ? "-" : r.Icon)}~{r.Difficulty}~{r.Value}")));

    public const int ConvLinesKept = 40;          // the starter keeps this many of the conversation's lines for a late listener's catch-up

    // ---------------------------------------------------------------- III.5 a trialog's roles mapped to its souls

    /// <summary>The dialogue's own file in the game's Scripts.pak: "Barbora.open_world.shop.x" -> "Quests/Final/Barbora/open_world/shop/x.xml".</summary>
    public static string DialogFile(string path) => "Quests/Final/" + path.Replace('.', '/') + ".xml";

    /// <summary>The roles the dialogue's own file names, in order, with the soul each is bound to when the file binds one
    /// (&lt;SelectedSoul Role="BARA" ... Soul="ttkc_barbora"/&gt;), inside the dialogue element named <paramref name="dialogName"/>.</summary>
    public static List<(string Role, string? Soul)> RolesOf(string xml, string dialogName)
    {
        var o = new List<(string, string?)>();
        try
        {
            var doc = new System.Xml.XmlDocument { XmlResolver = null };
            doc.LoadXml(xml.TrimStart('﻿'));
            System.Xml.XmlNode? dlg = null;
            foreach (System.Xml.XmlNode n in doc.SelectNodes("//*[@Name]")!)
                if (n.Attributes?["Name"]?.Value == dialogName && n.SelectSingleNode("SelectedSouls") is not null) { dlg = n; break; }
            dlg ??= doc.SelectSingleNode("//*[SelectedSouls]");
            if (dlg is null) return o;
            foreach (System.Xml.XmlNode s in dlg.SelectNodes("SelectedSouls/SelectedSoul")!)
            {
                string? role = s.Attributes?["Role"]?.Value, soul = s.Attributes?["Soul"]?.Value;
                if (!string.IsNullOrEmpty(role)) o.Add((role, string.IsNullOrEmpty(soul) ? null : soul));
            }
        }
        catch (System.Xml.XmlException) { }
        return o;
    }

    /// <summary>Each non-player role -> its soul: the one the file binds when the engine's conversation has it, the rest in the order the
    /// engine logged the conversation's souls (the player's own role, HENRY, stays the starter's figure).</summary>
    public static Dictionary<string, string> MapRoles(IReadOnlyList<(string Role, string? Soul)> roles, IReadOnlyList<string> others)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var left = others.ToList();
        foreach (var (role, soul) in roles)
            if (!Wo203ConvRules.IsPlayerRole(role) && soul is not null && left.Contains(soul)) { map[role] = soul; left.Remove(soul); }
        foreach (var (role, _) in roles)
            if (!Wo203ConvRules.IsPlayerRole(role) && !map.ContainsKey(role) && left.Count > 0) { map[role] = left[0]; left.RemoveAt(0); }
        return map;
    }

    // ---------------------------------------------------------------- III.1 combat barks are never conversations

    /// <summary>The engine's "New dialogue '&lt;path&gt; (&lt;kind&gt; - Id: N) (&lt;origin&gt;)' ..." line -> (path, kind, origin); null for any
    /// other line. A dialogue picked from several candidates names them all ("a / b / c"): the path is the first candidate.</summary>
    public static (string Path, string Kind, string Origin)? ParseDialogueHead(string line)
    {
        const string head = "New dialogue '", idTag = " - Id: ";
        if (!line.StartsWith(head, StringComparison.Ordinal)) return null;
        int id = line.IndexOf(idTag, StringComparison.Ordinal);
        if (id < 0) return null;
        int open = line.LastIndexOf('(', id);
        if (open < head.Length) return null;
        string kind = line[(open + 1)..id];
        int close = line.IndexOf(')', id);
        if (close < 0 || close + 2 >= line.Length || line[close + 1] != ' ' || line[close + 2] != '(') return null;
        int oclose = line.IndexOf(')', close + 3);
        if (oclose < 0) return null;
        string origin = line[(close + 3)..oclose];
        string paths = line[head.Length..open].TrimEnd();
        int slash = paths.IndexOf(" / ", StringComparison.Ordinal);
        string path = slash >= 0 ? paths[..slash] : paths;
        return (path, kind, origin);
    }

    /// <summary>The positive list, read from the engine's own lines in the field logs (sessions C and D): the player's real talks are the
    /// engine's full dialogue (kind "normal": the dialogue camera, the options) started by the player's talk (Actor::RequestDialog), a
    /// forced scene (ForceDialogNode), a haggle, or an NPC opening a full dialogue with the player (AI::RequestDialog). Everything else
    /// is a bark: the "ingame" kind (CombatShout, the hit and war-cry barks; AI::RequestDialog gossip), the "chat" kind (DoChat, a
    /// greeting), a monologue.</summary>
    public static readonly string[] ConversationOrigins = ["Actor::RequestDialog", "ForceDialogNode", "Haggle", "AI::RequestDialog"];

    /// <summary>The bark families, never a conversation whatever their kind: combat and its injuries (boj_a_zraneni: "took a hit", the war
    /// cries), the skirmish's surrender and flee chats (combat), horse commands, crime reactions.</summary>
    public static readonly string[] BarkFamilies =
    [
        "Barbora.open_world.boj_a_zraneni.", "Barbora.open_world.combat.", "Barbora.open_world.horse.", "Barbora.open_world.crime_reaction_barks.",
        "Barbora.open_world.samomluva_obecna.",
    ];

    public static bool IsBarkFamily(string path) =>
        BarkFamilies.Any(f => path.StartsWith(f, StringComparison.OrdinalIgnoreCase));

    /// <summary>A dialogue is a conversation only if the engine's data says so: kind "normal", an origin on the positive list, and not a
    /// bark family. (The souls -- the player and another -- are the caller's check.)</summary>
    public static bool IsConversation(string path, string kind, string origin) =>
        kind == "normal" && ConversationOrigins.Contains(origin) && !IsBarkFamily(path);

    /// <summary>The whole line: null when it is no "New dialogue" line; else whether it is a conversation.</summary>
    public static bool? IsConversationLine(string line) =>
        ParseDialogueHead(line) is { } h ? IsConversation(h.Path, h.Kind, h.Origin) : null;
}
