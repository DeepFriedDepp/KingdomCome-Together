// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-200: the pure rules of "actions, not results" (docs/WO-200-findings.md). The bridge is GameBridge.Wo200.cs; the native gates are
/// native/KCDMP/wo200.cpp.
/// </summary>
public static class Wo200Rules
{
    // ---- the switches (each mechanism ships on, each with its own switch) ----
    public static readonly string[] SwitchNames =
    {
        "s1_replay", "s1_route", "qitem_mirror", "joiner_quiet_clocks", "joiner_no_puppet_quests", "joiner_scoped_twins",
        "copy_health_from_host", "rider_from_host", "copy_hands_from_host", "copy_neutral_always", "world_clock_from_host", "stream_dogs",
        // WO-201: joiner_receive_only, figure_is_player and nearest_player_triggers were placeholders here (nothing built); they are
        // WO-201's switches now (Wo201Rules.SwitchNames), under the same console names
    };

    /// <summary>The ship-on rule, with its one exception: V.2's player bit reaches a save, so it ships on only after its decode test passed.</summary>
    public static bool DefaultOn(string name) => true;

    public static bool TryParseCfg(string? arg, out string name, out bool on)
    {
        name = ""; on = false;
        if (string.IsNullOrEmpty(arg)) return false;
        int eq = arg.IndexOf('=');
        if (eq <= 0) return false;
        name = arg[..eq];
        string v = arg[(eq + 1)..];
        if (!SwitchNames.Contains(name)) return false;
        if (v == "on") { on = true; return true; }
        if (v == "off") { on = false; return true; }
        return false;
    }

    // ---- the DLL's config bytes (wo200.h op 71) ----
    public const byte RoleNone = 0, RoleHost = 1, RoleJoiner = 2;
    public static byte TapBits(bool s1, bool quiet) => (byte)((s1 ? 1 : 0) | (quiet ? 2 : 0) | (s1 ? 4 : 0));
    public static byte GateBits(bool puppets, bool s1, bool route) => (byte)((puppets ? 1 : 0) | (s1 ? 2 : 0) | (route ? 4 : 0));

    // ---- texts on the wire (WO-137's join channel; every field checked) ----
    public static string ReplayText(uint closure, uint seq, string port, string path) =>
        string.Create(CultureInfo.InvariantCulture, $"{closure} {seq} {port} {path}");

    public static bool TryParseReplayText(string text, out uint closure, out uint seq, out string port, out string path)
    {
        closure = 0; seq = 0; port = ""; path = "";
        var f = text.Split(' ');
        if (f.Length != 4) return false;
        if (!uint.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out closure)) return false;
        if (!uint.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out seq)) return false;
        if (!Wo137Text.IsPort(f[2]) || !Wo137Text.IsPath(f[3])) return false;
        port = f[2]; path = f[3];
        return true;
    }

    public static readonly string[] AckVerdicts = { "ran", "refused-notloaded", "refused-notdialogue", "refused-noport", "fault", "off", "queued", "unarmed" };

    /// <summary>The DLL's op 73 result -> the ack's verdict word.</summary>
    public static string VerdictOf(byte dllResult) => dllResult switch
    {
        1 => "ran", 0 => "refused-notloaded", 2 => "refused-notdialogue", 3 => "refused-noport", 4 => "refused-notloaded", 5 => "fault",
        _ => "unarmed",
    };

    public static string AckText(uint closure, string verdict, int runtime, string path) =>
        string.Create(CultureInfo.InvariantCulture, $"{closure} {verdict} {runtime} {path}");

    public static bool TryParseAckText(string text, out uint closure, out string verdict, out int runtime, out string path)
    {
        closure = 0; verdict = ""; runtime = 0; path = "";
        var f = text.Split(' ');
        if (f.Length != 4) return false;
        if (!uint.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out closure)) return false;
        if (!AckVerdicts.Contains(f[1])) return false;
        if (!Wo137Text.TryInt(f[2], out runtime)) return false;
        if (!Wo137Text.IsPath(f[3])) return false;
        verdict = f[1]; path = f[3];
        return true;
    }

    /// <summary>A replay the host refused falls back to today's requests for that closure; one it ran makes them redundant.</summary>
    public static bool FallsBack(string verdict) => verdict is not ("ran" or "queued");

    public static string ItemCountText(int count, Guid itemClass) => string.Create(CultureInfo.InvariantCulture, $"{count} {itemClass:D}");

    public static bool TryParseItemCountText(string text, out int count, out Guid itemClass)
    {
        count = 0; itemClass = Guid.Empty;
        var f = text.Split(' ');
        return f.Length == 2 && Wo137Text.TryInt(f[0], out count) && count >= 0 && count <= 9999 && Guid.TryParse(f[1], out itemClass) && itemClass != Guid.Empty;
    }

    /// <summary>The host's replay closure id: the joiner's id (its local bit off) under the peer's own byte, so two joiners never share one.</summary>
    public static uint HostClosure(byte peer, uint joinerClosure) => ((uint)(peer & 0x7F) << 24) | (joinerClosure & 0x00FFFFFFu);

    // ---- which captured out-port is this player's own conversation ----
    /// <summary>
    /// One "New dialogue '&lt;name&gt; (&lt;kind&gt; - Id: N)...' is starting. Params: souls = '...'" line: a conversation of this player's (his
    /// soul "Dude" among two or more participants), not a monologue or a bark. Names: the dialogue's own (a wrapper's "A / B" gives both).
    /// </summary>
    public static bool TryParseTalkStart(string line, out string[] names, out int id)
    {
        names = Array.Empty<string>(); id = -1;
        if (!line.StartsWith("New dialogue '", StringComparison.Ordinal)) return false;
        var m = TalkRx.Match(line);
        if (!m.Success) return false;
        string kind = m.Groups["kind"].Value, souls = m.Groups["souls"].Value;
        if (kind.Contains("monolog", StringComparison.OrdinalIgnoreCase)) return false;
        if (!souls.Contains("Ex: Dude", StringComparison.Ordinal) || !souls.Contains(';')) return false;
        if (!int.TryParse(m.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out id)) return false;
        names = m.Groups["name"].Value.Split(" / ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return names.Length > 0;
    }

    private static readonly Regex TalkRx = new(@"^New dialogue '(?<name>.*?) \((?<kind>[^()]*?) - Id: (?<id>\d+)\)[^']*' is starting\. Params: souls = '(?<souls>[^']*)'",
                                               RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>"[ID: 246] Dialog ending [...]" / "Dialog interrupted" -> the dialogue id that ended.</summary>
    public static bool TryParseTalkEnd(string line, out int id)
    {
        id = -1;
        var m = EndRx.Match(line);
        return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out id);
    }
    private static readonly Regex EndRx = new(@"\[ID: (\d+)\] Dialog (?:ending|interrupted)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>A capture's node path belongs to a talk named `name` (the dialogue's path, or a wrapper's that holds the node).</summary>
    public static bool PathMatchesTalk(string capturePath, string name) =>
        capturePath == name || capturePath.EndsWith("." + name, StringComparison.Ordinal) || name.EndsWith("." + capturePath, StringComparison.Ordinal)
        || capturePath.StartsWith(name + ".", StringComparison.Ordinal);

    // ---- II.7: the clock's slew ----
    /// <summary>
    /// The ratio this joiner's clock runs at to close `errSeconds` (game seconds it is AHEAD of the host's; negative = behind): the host's
    /// own ratio within 4 s, a correction of at most 25 % beyond (a 30 s lead closes in ~16 real s), nothing for a gap over 10 minutes
    /// (WO-140 pulls a lead back, WO-133 converges a lag forward).
    /// </summary>
    public static double SlewRatio(double hostRatio, double errSeconds)
    {
        double a = Math.Abs(errSeconds);
        if (a < 4 || a > 600 || !double.IsFinite(errSeconds)) return hostRatio;
        double k = Math.Clamp(errSeconds / 240.0, -0.25, 0.25);
        return Math.Round(hostRatio * (1 - k), 2);
    }

    // ---- the quiet clocks' comparison ----
    public static readonly TimeSpan QuietCompare = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan SuppressTtl = TimeSpan.FromMinutes(5);
    /// <summary>A flagged local change waits this long for its capture's fate (the poll is 4 Hz; the change frame can come first).</summary>
    public static readonly TimeSpan ParkMax = TimeSpan.FromSeconds(3);

    // ---- Act IV: noise the host's world can hear (the game's rpg_sound ids: 1 combat, 2 lockpick, 7 door) ----
    public static readonly string[] NoiseKinds = { "lockpick", "door", "combat" };

    public static string NoiseText(string kind, float x, float y, float z) => string.Create(CultureInfo.InvariantCulture, $"{kind} {x:F2} {y:F2} {z:F2}");

    public static bool TryParseNoise(string? text, out string kind, out float x, out float y, out float z)
    {
        kind = ""; x = y = z = 0;
        var f = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length != 4 || !NoiseKinds.Contains(f[0])) return false;
        if (!float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out x) || !float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out y)
            || !float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out z)) return false;
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || Math.Abs(x) > 100000 || Math.Abs(y) > 100000 || Math.Abs(z) > 10000) return false;
        kind = f[0];
        return true;
    }

    // ---- Act IV: the Join page on a rejoin ----
    /// <summary>"Continue with your character (saved N min ago)" -- the per-world Henry file's age, in words a player reads.</summary>
    public static string RejoinText(TimeSpan age)
    {
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        int min = (int)Math.Round(age.TotalMinutes);
        string ago = min < 1 ? "just now" : min < 120 ? (min == 1 ? "1 min ago" : $"{min} min ago")
                   : age.TotalHours < 48 ? $"{(int)Math.Round(age.TotalHours)} h ago" : $"{(int)Math.Round(age.TotalDays)} days ago";
        return $"Continue with your character (saved {ago})";
    }

    // ---- I.3: the quest-item mirror ----
    /// <summary>Classes per console command (37 chars each plus a space, under the ~2,100-character ceiling after encoding).</summary>
    public const int QClassChunk = 32;
    /// <summary>A count above this is no quest item's (a malformed or hostile frame).</summary>
    public const int QCountMax = 9999;
}

/// <summary>
/// WO-200 I.1: an out-port's downstream closure, read from the quest data (Scripts.pak's Skald modules: nodes list their INCOMING edges,
/// "src.port" -> "toPort"; a From without a dot is the module's own In port; an Output node feeds the module's Out ports). The closure
/// follows trigger edges through States (all their out-ports), logic and waiters (a SceneFinishedWaiter's OnFinished is just another
/// out-port here: the departure's arrives 48 s later), into sub-module instances by their In ports and out of a module through its
/// Output. It over-approximates (logic nodes pass every out-port) and is bounded. Cached per out-port.
/// </summary>
public sealed class Wo200QuestGraph
{
    public sealed record Closure(string Path, string Port, IReadOnlyList<string> States, IReadOnlyDictionary<string, int> NodeTypes, int Visited, bool Truncated);

    private readonly Func<string, string?> _readModule;   // module path ("Barbora.x.y") -> its XML text, or null
    private readonly Dictionary<string, Module?> _modules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Closure> _cache = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    public const int MaxVisited = 600;

    public Wo200QuestGraph(Func<string, string?> readModule) { _readModule = readModule; }

    /// <summary>The installed game's quest modules (Scripts.pak, Quests/Final), read on demand.</summary>
    public static Wo200QuestGraph? FromScriptsPak(string? pak)
    {
        if (pak is null || !File.Exists(pak)) return null;
        var zip = ZipFile.OpenRead(pak);
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var e in zip.Entries)
        {
            string n = e.FullName.Replace('\\', '/');
            const string root = "Quests/Final/";
            if (!n.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
            entries[n[root.Length..^4].Replace('/', '.')] = e;
        }
        return new Wo200QuestGraph(path =>
        {
            lock (zip)
            {
                if (!entries.TryGetValue(path, out var e)) return null;
                using var s = e.Open();
                using var r = new StreamReader(s, Encoding.UTF8);
                return r.ReadToEnd();
            }
        });
    }

    private sealed class Node { public string Name = "", Tag = ""; public List<(string From, string To)> In = new(); }
    private sealed class Module
    {
        public string Path = "";
        public Dictionary<string, Node> Nodes = new(StringComparer.Ordinal);
        public Dictionary<string, List<(Node Target, string ToPort)>> BySource = new(StringComparer.Ordinal);   // "src.port" or "inPort" -> targets
    }

    private static readonly Regex NodeRx = new(@"<(?<tag>[A-Za-z_][A-Za-z0-9_]*) Name=""(?<name>[^""]+)""(?<rest>[^>]*?)(?<self>/?)>", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex EdgeRx = new(@"<Edge From=""(?<from>[^""]+)"" To=""(?<to>[^""]+)""", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private Module? Load(string path)
    {
        if (_modules.TryGetValue(path, out var m)) return m;
        string? xml = _readModule(path);
        if (xml is null) { _modules[path] = null; return null; }
        m = new Module { Path = path };
        int nodesAt = xml.IndexOf("<Nodes>", StringComparison.Ordinal);
        int nodesEnd = nodesAt < 0 ? -1 : xml.IndexOf("</Nodes>", nodesAt, StringComparison.Ordinal);
        if (nodesAt >= 0 && nodesEnd > nodesAt)
        {
            string body = xml.Substring(nodesAt + 7, nodesEnd - nodesAt - 7);
            // top-level node elements: a depth-1 scan (children are Edge / Asset / Constant / Vertex ...)
            int depth = 0; Node? cur = null;
            foreach (Match t in Regex.Matches(body, @"<(/?)([A-Za-z_][A-Za-z0-9_]*)([^>]*?)(/?)>"))
            {
                bool close = t.Groups[1].Value == "/", self = t.Groups[4].Value == "/";
                string tag = t.Groups[2].Value;
                if (close) { depth--; if (depth == 0) cur = null; continue; }
                if (depth == 0)
                {
                    var nm = Regex.Match(t.Groups[3].Value, @"Name=""([^""]+)""");
                    if (nm.Success)
                    {
                        cur = new Node { Name = nm.Groups[1].Value, Tag = tag };
                        m.Nodes[cur.Name] = cur;
                    }
                    if (!self) depth++;
                    else cur = null;
                    continue;
                }
                if (tag == "Edge" && cur is not null)
                {
                    var e = EdgeRx.Match(t.Value);
                    if (e.Success) cur.In.Add((e.Groups["from"].Value, e.Groups["to"].Value));
                }
                if (!self) depth++;
            }
        }
        foreach (var n in m.Nodes.Values)
            foreach (var (from, to) in n.In)
            {
                if (!m.BySource.TryGetValue(from, out var l)) m.BySource[from] = l = new();
                l.Add((n, to));
            }
        _modules[path] = m;
        return m;
    }

    private static string Parent(string path) { int i = path.LastIndexOf('.'); return i > 0 ? path[..i] : ""; }
    private static string Last(string path) { int i = path.LastIndexOf('.'); return i >= 0 ? path[(i + 1)..] : path; }

    /// <summary>The closure of `nodePath`'s out-port `port` (nodePath = module path + "." + node name).</summary>
    public Closure Of(string nodePath, string port)
    {
        string key = nodePath + "|" + port;
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var hit)) return hit;
            var states = new List<string>();
            var types = new Dictionary<string, int>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var work = new Queue<(string Module, string Source)>();   // Source: "node.port" | "node.*" | "inPort"
            work.Enqueue((Parent(nodePath), Last(nodePath) + "." + port));
            int visited = 0; bool truncated = false;
            while (work.Count > 0)
            {
                var (mp, src) = work.Dequeue();
                if (!seen.Add(mp + "|" + src)) continue;
                if (++visited > MaxVisited) { truncated = true; break; }
                var mod = Load(mp);
                if (mod is null) continue;
                IEnumerable<(Node Target, string ToPort)> targets;
                if (src.EndsWith(".*", StringComparison.Ordinal))
                {
                    string prefix = src[..^1];
                    targets = mod.BySource.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal)).SelectMany(kv => kv.Value).ToList();
                }
                else targets = mod.BySource.TryGetValue(src, out var l) ? l : Enumerable.Empty<(Node, string)>();
                foreach (var (t, toPort) in targets)
                {
                    types[t.Tag] = types.GetValueOrDefault(t.Tag) + 1;
                    if (t.Tag == "State") { string sp = mp + "." + t.Name; if (!states.Contains(sp)) states.Add(sp); work.Enqueue((mp, t.Name + ".*")); continue; }
                    if (t.Tag == "Output") { string parent = Parent(mp); if (parent.Length > 0) work.Enqueue((parent, Last(mp) + "." + toPort)); continue; }
                    string sub = mp + "." + t.Name;
                    if (Load(sub) is not null) { work.Enqueue((sub, toPort)); continue; }   // a sub-module instance: in through its In port
                    work.Enqueue((mp, t.Name + ".*"));
                }
            }
            var c = new Closure(nodePath, port, states, types, visited, truncated);
            _cache[key] = c;
            return c;
        }
    }
}
