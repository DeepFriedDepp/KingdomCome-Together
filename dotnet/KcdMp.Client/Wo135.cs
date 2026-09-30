using System.Text.RegularExpressions;

namespace KcdMp.Client;

// WO-135 -- the avatar is a puppet, knockouts both ways, crouch, outfits, and
// same-build saves for a join (docs/WO-135-findings.md). The pure rules; the
// agent's wiring is GameBridge.Wo135.cs.
public static class Wo135Rules
{
    // ------------------------------------------------------------------ Phase 1

    /// <summary>The avatar's quiet groups (motion.cpp kQuiet*): 1 speech, 2 witness, 4 react, 8 defence.</summary>
    public const byte QuietAll = 0x0F;

    public static string QuietGroups(byte mask)
    {
        var n = new List<string>();
        if ((mask & 1) != 0) n.Add("speech");
        if ((mask & 2) != 0) n.Add("witness");
        if ((mask & 4) != 0) n.Add("react");
        if ((mask & 8) != 0) n.Add("defence");
        return n.Count == 0 ? "none" : string.Join(",", n);
    }

    /// <summary>"quiet=N" from the mod's w135_cfg event; null when malformed.</summary>
    public static byte? ParseQuiet(string? arg)
    {
        foreach (var kv in (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (kv.StartsWith("quiet=", StringComparison.Ordinal) && byte.TryParse(kv[6..], out byte v) && v <= QuietAll) return v;
        return null;
    }

    // ------------------------------------------------------------------ Phase 2

    public static readonly Regex NpcName = new(@"^[A-Za-z0-9_]{1,63}$", RegexOptions.CultureInvariant);
    public static readonly string[] TakedownKinds = ["mercy", "knockout", "stealth"];

    /// <summary>w135_ko "&lt;name&gt; 1|0" (the copy should be down / up).</summary>
    public static bool TryParseKo(string? arg, out string name, out bool down)
    {
        name = ""; down = false;
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length != 2 || !NpcName.IsMatch(f[0]) || f[1] is not ("1" or "0")) return false;
        name = f[0]; down = f[1] == "1";
        return true;
    }

    /// <summary>A takedown ask's text "&lt;name&gt; &lt;mercy|knockout|stealth&gt;" (joiner -> host, and the mod's w135_takedown).</summary>
    public static bool TryParseTakedown(string? text, out string name, out string kind)
    {
        name = ""; kind = "";
        var f = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length != 2 || !NpcName.IsMatch(f[0]) || Array.IndexOf(TakedownKinds, f[1]) < 0) return false;
        name = f[0]; kind = f[1];
        return true;
    }

    /// <summary>A takedown result's text "&lt;ok|refused&gt; &lt;name&gt; &lt;kind&gt;" (host -> joiner).</summary>
    public static bool TryParseTakedownResult(string? text, out string res, out string name, out string kind)
    {
        res = name = kind = "";
        var f = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length != 3 || f[0] is not ("ok" or "refused") || !NpcName.IsMatch(f[1]) || Array.IndexOf(TakedownKinds, f[2]) < 0) return false;
        res = f[0]; name = f[1]; kind = f[2];
        return true;
    }

    // ------------------------------------------------------------------ Phase 4

    /// <summary>What the avatar wears that the peer does not: take it off.</summary>
    public static List<Guid> AppearanceRemovals(IReadOnlyCollection<Guid> actual, IReadOnlySet<Guid> target)
        => actual.Where(c => !target.Contains(c)).Distinct().ToList();

    /// <summary>What the peer wears that the avatar does not: put it on.</summary>
    public static List<Guid> AppearanceAdditions(IReadOnlyCollection<Guid> actual, IReadOnlySet<Guid> target)
    {
        var a = actual as IReadOnlySet<Guid> ?? new HashSet<Guid>(actual);
        return target.Where(c => !a.Contains(c)).ToList();
    }

    /// <summary>
    /// The classes the game refused on this avatar for its CURRENT outfit. A new
    /// outfit (any other set) clears it, so nothing is ever skipped for longer
    /// than the outfit it failed in -- and never the whole outfit.
    /// </summary>
    public sealed class Unwearable
    {
        private HashSet<Guid>? _target;
        private readonly HashSet<Guid> _marked = [];
        // WO-144 2.1: a refusal is retried on a timer, not only when the outfit changes (the field's
        // six pieces stayed off until the joiner changed clothes): class -> (marks, retry-at ms).
        private readonly Dictionary<Guid, (int Marks, long RetryAtMs)> _timed = [];

        public void OnTarget(IReadOnlySet<Guid> target)
        {
            if (_target is not null && _target.SetEquals(target)) return;
            _target = [.. target];
            _marked.Clear();
            _timed.Clear();
        }

        /// <summary>Skipped for this outfit for good (no time given), or still backing off.</summary>
        public bool Skips(Guid c) => _marked.Contains(c) || _timed.ContainsKey(c);

        /// <summary>WO-144: skipped for good, or until its back-off ends (then it is tried again).</summary>
        public bool Skips(Guid c, long nowMs) =>
            _marked.Contains(c) || (_timed.TryGetValue(c, out var t) && nowMs < t.RetryAtMs);

        /// <summary>True the first time a class is marked for this outfit (the one log line).</summary>
        public bool Mark(Guid c) => _marked.Add(c);

        /// <summary>WO-144: a refusal that is retried after 20 s, 60 s, 3 min, then every 10 min. True on the first.</summary>
        public bool MarkTimed(Guid c, long nowMs)
        {
            int n = _timed.TryGetValue(c, out var t) ? t.Marks + 1 : 1;
            _timed[c] = (n, nowMs + Wo144Rules.RefusalBackoffMs(n));
            return n == 1;
        }

        public int Marks(Guid c) => _timed.TryGetValue(c, out var t) ? t.Marks : 0;

        public int Count => _marked.Count + _timed.Count;
    }

    // ------------------------------------------------------------------ Phase 5

    public static readonly Regex BuildText = new(@"^[A-Za-z0-9._\-]{1,64}$", RegexOptions.CultureInvariant);

    /// <summary>"1.5.6-15693-release_1_5" -> "1.5.6 (build 15693)"; "1.5.5-release_1_5" -> "1.5.5".</summary>
    public static string PlainBuild(string? build)
    {
        if (string.IsNullOrEmpty(build)) return "unknown";
        var m = Regex.Match(build, @"^(\d+(?:\.\d+)+)(?:-(\d+))?");
        if (!m.Success) return build;
        return m.Groups[2].Success ? $"{m.Groups[1].Value} (build {m.Groups[2].Value})" : m.Groups[1].Value;
    }

    /// <summary>A save's build matches the host world's (the rule: a character and a world from the same build only).</summary>
    public static bool SameBuild(string? save, string? host) => !string.IsNullOrEmpty(save) && string.Equals(save, host, StringComparison.Ordinal);

    public static string WrongBuildMessage(string? theirs, string host) =>
        $"Your saves are from game version {PlainBuild(theirs)}, but your host's game is version {PlainBuild(host)}. " +
        "Start or load a game in the Modding Tools build and save once, then join again.";

    public enum Offer { Both, BringOnly, FreshOnly, None }

    public static Offer OfferFor(bool bring, bool fresh) => bring && fresh ? Offer.Both : bring ? Offer.BringOnly : fresh ? Offer.FreshOnly : Offer.None;

    /// <summary>The launcher state and its message for a first join, once the host's build is known.</summary>
    public static (string State, string Message) ChooseUi(Offer o, string? theirs, string host) => o switch
    {
        Offer.Both => ("choose", "First time in this world: bring your character, or start fresh?"),
        Offer.BringOnly => ("choose-bring", "First time in this world: bring your character. (Start fresh needs a new game's first save from your host's game version.)"),
        Offer.FreshOnly => ("choose-fresh", $"First time in this world: start fresh. (Your own saves with Henry are from another game version than your host's {PlainBuild(host)}.)"),
        _ => ("wrong-build", WrongBuildMessage(theirs, host)),
    };
}

public static partial class WhsSave
{
    /// <summary>WO-135: a save's game build (its description's BuildInfo), from the header alone -- no inflate.</summary>
    public static string? ReadBuildFromFile(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var head = new byte[8];
            if (fs.Read(head, 0, 8) != 8 || System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(head) != 0xFFFFFFFFu) return null;
            int descLen = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(4));
            if (descLen <= 0 || descLen > 1 << 20) return null;
            var desc = new byte[descLen];
            int got = 0;
            while (got < descLen) { int n = fs.Read(desc, got, descLen - got); if (n <= 0) return null; got += n; }
            return DescriptionSummary(System.Text.Encoding.UTF8.GetString(desc)).GetValueOrDefault("BuildInfo");
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
