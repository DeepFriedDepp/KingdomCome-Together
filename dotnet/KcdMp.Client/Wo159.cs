// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KcdMp.Client;

/// <summary>
/// WO-159: the main menu's Start Game without the prologue, the agent's half (docs/WO-159-findings.md). Run by the
/// launcher as one-shot helpers (<c>--w159 saves|stage|unstage|used|validate</c>, one tagged JSON line each), so the
/// save reader and the WO-125 / WO-157 classifier stay in one place.
///
/// <list type="bullet">
/// <item><b>saves</b>: the host's worlds for Start Game -- per playline the newest save that is this player's own
///   Modding Tools Henry (not a regular-game save, not a copy of a world this install only joined, not the prologue).</item>
/// <item><b>stage</b>: the bundled start save into the lowest FREE playline (no save in it at all), with a new
///   playthrough seed (the world's identity: two new adventures are two worlds) and the description's account and
///   machine names cleared. Staged before the game starts: the engine lists saves once, at start (WO-112 s3.5), and the host has no
///   plugin at the menu to rescan.</item>
/// <item><b>unstage</b>: a staged file never played is taken away again (only while it is still the playline's only
///   save and still the bytes written); one that was loaded stays as a normal save.</item>
/// <item><b>validate</b>: the start-save recipe, checked (tools/Validate-StartSave.ps1).</item>
/// </list>
/// </summary>
public static class Wo159
{
    public const string CliTag = "W159";

    // ------------------------------------------------------------------ the host's worlds

    public sealed record MenuSave(int Playline, string Name, string Label, long SaveTime);

    public sealed record SavesReport(List<MenuSave> Usable, int HiddenRegular, int HiddenCopies, int HiddenPrologue, List<int> Free)
    {
        /// <summary>The greyed note under the list, in plain words ("" when nothing is hidden).</summary>
        public string Hidden
        {
            get
            {
                var parts = new List<string>();
                if (HiddenRegular > 0) parts.Add($"{HiddenRegular} from the regular game (not the Modding Tools)");
                if (HiddenCopies > 0) parts.Add($"{HiddenCopies} {(HiddenCopies == 1 ? "is a copy" : "are copies")} of a host's world you joined");
                if (HiddenPrologue > 0) parts.Add($"{HiddenPrologue} still in the prologue");
                return parts.Count == 0 ? "" : "Not shown: " + string.Join("; ", parts) + ".";
            }
        }
    }

    /// <summary>"Playline 2 (Oct 07 14:02)": the slot and the newest save's local time.</summary>
    public static string Label(int playline, long saveTime) =>
        $"Playline {playline} ({DateTimeOffset.FromUnixTimeSeconds(saveTime).ToLocalTime().ToString("MMM dd HH:mm", CultureInfo.InvariantCulture)})";

    /// <summary>
    /// One entry per playline, newest world first. <paramref name="copyOfJoined"/>: a seed tag of a world this install
    /// only joined (its Henry store has it, it never hosted it). <paramref name="isHenry"/> reads the player (an inflate):
    /// only called for the few saves that get that far. <paramref name="skip"/>: files the menu offers otherwise (a staged
    /// start save not played yet).
    /// </summary>
    public static SavesReport MenuSaves(string saves, Func<string, bool> copyOfJoined, Func<string, bool> isHenry, ISet<string>? skip = null)
    {
        var usable = new List<MenuSave>();
        int regular = 0, copies = 0, prologue = 0;
        var all = GameBridge.ListOwnSaves(saves);
        for (int pl = 0; pl <= 4; pl++)
        {
            var here = all.Where(s => s.Playline == pl && (skip is null || !skip.Contains(s.FullPath))).ToList();   // newest first
            if (here.Count == 0) continue;
            string? why = null;
            MenuSave? pick = null;
            foreach (var s in here.Take(12))
            {
                if (Wo154Rules.IsRegularGameBuild(WhsSave.ReadBuildFromFile(s.FullPath))) { why ??= "regular"; continue; }
                if (WhsSave.ReadSeedFromFile(s.FullPath) is uint seed && copyOfJoined(WhsSave.SeedTag(seed))) { why ??= "copy"; continue; }
                if (!isHenry(s.FullPath)) { why ??= "prologue"; continue; }
                pick = new MenuSave(pl, s.Base, Label(pl, s.SaveTime), s.SaveTime);
                break;
            }
            if (pick is not null) usable.Add(pick);
            else if (why == "regular") regular++;
            else if (why == "copy") copies++;
            else if (why == "prologue") prologue++;
        }
        return new SavesReport(usable.OrderByDescending(u => u.SaveTime).ToList(), regular, copies, prologue, FreePlaylines(saves));
    }

    /// <summary>The playlines with no save at all (folder missing, or no .whs in it): where a new adventure may go.</summary>
    public static List<int> FreePlaylines(string saves)
    {
        var o = new List<int>();
        for (int pl = 0; pl <= 4; pl++)
        {
            string dir = Path.Combine(saves, $"playline{pl}");
            if (!Directory.Exists(dir) || !Directory.EnumerateFiles(dir, "*.whs").Any()) o.Add(pl);
        }
        return o;
    }

    // ------------------------------------------------------------------ re-key

    private static readonly Regex NamesRx = new("(UserName|BuildComputer)=\"[^\"]*\"", RegexOptions.CultureInvariant);
    private static readonly Regex UsedModsRx = new(@"<UsedMods>.*?</UsedMods>", RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>The description with the account and machine names cleared and the used-mods list emptied (metadata only).</summary>
    public static string ScrubDescription(string desc) =>
        UsedModsRx.Replace(NamesRx.Replace(desc, m => m.Groups[1].Value + "=\"\""), "<UsedMods>\n\t</UsedMods>");

    /// <summary>A copy of <paramref name="file"/> with playthrough seed <paramref name="seed"/> (body 0x01FB) and a scrubbed description; re-signed.</summary>
    public static byte[] Rekey(byte[] file, uint seed)
    {
        var c = WhsSave.Inflate(file);
        var raw = c.Raw.ToArray();
        if (WhsSave.PathGet(raw, 0x01F4, 0x01FB) is not WhsSave.Node n || n.Len != 4) throw new InvalidDataException("the save has no playthrough seed");
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(n.PayloadOff), seed);
        var desc = Encoding.UTF8.GetBytes(ScrubDescription(c.Desc));
        return WhsSave.Deflate(desc, raw, c.FooterTail);
    }

    // ------------------------------------------------------------------ the ledger of staged start saves

    public sealed class Staged
    {
        public string Path { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public string SeedTag { get; set; } = "";
        public bool CreatedDir { get; set; }
        public DateTime StagedUtc { get; set; }
    }

    /// <summary>&lt;data&gt;/w159-staged.json, beside the Henry store (KCDMP_DATA_DIR or %LOCALAPPDATA%\KCDMP).</summary>
    public static string LedgerPath(string? dataRoot = null) =>
        System.IO.Path.Combine(dataRoot ?? System.IO.Path.GetDirectoryName(HenryStore.DefaultRoot())!, "w159-staged.json");

    public static List<Staged> ReadLedger(string ledger)
    {
        try { return File.Exists(ledger) ? JsonSerializer.Deserialize<List<Staged>>(File.ReadAllText(ledger)) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or JsonException) { return []; }
    }

    private static void WriteLedger(string ledger, List<Staged> l)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ledger)!);
        File.WriteAllText(ledger + ".part", JsonSerializer.Serialize(l, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(ledger + ".part", ledger, overwrite: true);
    }

    // ------------------------------------------------------------------ stage / unstage

    public sealed record StageResult(bool Ok, int Playline, string Name, string SeedTag, string Why);

    /// <summary>The one engine-named .whs of a start-save folder (or the file itself), or null with the reason.</summary>
    public static string? SourceFile(string source, out string why)
    {
        why = "";
        if (File.Exists(source)) return source;
        if (!Directory.Exists(source)) { why = "no start save is installed"; return null; }
        var f = Directory.EnumerateFiles(source, "*.whs").ToList();
        if (f.Count == 1) return f[0];
        // the start-save root with one folder per playstyle: the first one installed (the menu swaps in the chosen one)
        if (f.Count == 0)
            foreach (var st in Wo159Rules.Styles)
                if (StyleFile(source, st) is string sf) return sf;
        why = f.Count == 0 ? "no start save is installed" : $"the start-save folder holds {f.Count} saves (one is expected)";
        return null;
    }

    /// <summary>
    /// The start save into the lowest free playline under its own (engine) file name, re-keyed with a new seed,
    /// written .part + rename and read back. Never touches a playline that holds any save.
    /// </summary>
    public static StageResult Stage(string saves, string source, string ledger, Func<uint>? newSeed = null)
    {
        if (SourceFile(source, out string why) is not string src) return new(false, -1, "", "", why);
        byte[] bytes;
        try { bytes = WhsSave.ReadShared(src); } catch (IOException ex) { return new(false, -1, "", "", "the start save cannot be read: " + ex.Message); }
        var v = WhsSave.Verify(bytes);
        if (!v.Ok) return new(false, -1, "", "", "the start save does not verify: " + v.Reason);
        string name = System.IO.Path.GetFileName(src).ToLowerInvariant();
        if (WorldSaved.ParsePath(System.IO.Path.Combine("playline0", name)) is null) name = "permanent002.whs";
        var free = FreePlaylines(saves);
        if (free.Count == 0)
            return new(false, -1, "", "", "All five save slots (playlines) hold saves. A new adventure needs an empty one: start the game normally and delete a playline you no longer play in its Load Game list.");
        int pl = free[0];
        uint old = WhsSave.ReadSeed(WhsSave.Inflate(bytes).Raw) ?? 0;
        uint seed;
        do seed = newSeed?.Invoke() ?? BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));
        while (seed == 0 || seed == old);
        byte[] outBytes;
        try { outBytes = Rekey(bytes, seed); }
        catch (InvalidDataException ex) { return new(false, -1, "", "", "the start save cannot be re-keyed: " + ex.Message); }
        string dir = System.IO.Path.Combine(saves, $"playline{pl}");
        bool created = !Directory.Exists(dir);
        Directory.CreateDirectory(dir);
        string final = System.IO.Path.Combine(dir, name), part = final + ".part";
        using (var fs = new FileStream(part, FileMode.CreateNew, FileAccess.Write)) fs.Write(outBytes);
        File.Move(part, final);
        var back = WhsSave.ReadShared(final);
        if (!back.AsSpan().SequenceEqual(outBytes) || !WhsSave.Verify(back).Ok || WhsSave.ReadSeedFromFile(final) != seed)
        {
            try { File.Delete(final); if (created) Directory.Delete(dir); } catch (IOException) { }
            return new(false, -1, "", "", "the staged save did not read back as written");
        }
        string tag = WhsSave.SeedTag(seed);
        var l = ReadLedger(ledger);
        l.Add(new Staged { Path = final, Sha256 = Convert.ToHexString(SHA256.HashData(outBytes)).ToLowerInvariant(), SeedTag = tag, CreatedDir = created, StagedUtc = DateTime.UtcNow });
        WriteLedger(ledger, l);
        return new(true, pl, System.IO.Path.GetFileNameWithoutExtension(name), tag, "");
    }

    /// <summary>The one save of a playstyle's folder (start-save/&lt;style&gt;/), or null.</summary>
    public static string? StyleFile(string root, string style)
    {
        if (!Wo159Rules.Styles.Contains(style)) return null;
        string dir = System.IO.Path.Combine(root, style);
        if (!Directory.Exists(dir)) return null;
        var f = Directory.EnumerateFiles(dir, "*.whs").ToList();
        return f.Count == 1 ? f[0] : null;
    }

    /// <summary>The playstyles whose start save is installed under <paramref name="root"/>.</summary>
    public static List<string> InstalledStyles(string? root) =>
        root is null ? [] : Wo159Rules.Styles.Where(s => StyleFile(root, s) is not null).ToList();

    /// <summary>
    /// The chosen playstyle's start save written over the staged one, just before the load: the game lists a save at its
    /// start and reads the file only when it loads it (WO-159 probe: a replaced file is what loads). A new seed again, the
    /// header scrubbed; .part + replace, read back; the ledger entry follows the new bytes.
    /// </summary>
    public static StageResult Swap(string stagedPath, string sourceFile, string ledger, Func<uint>? newSeed = null)
    {
        var l = ReadLedger(ledger);
        var e = l.FirstOrDefault(x => string.Equals(x.Path, stagedPath, StringComparison.OrdinalIgnoreCase));
        if (e is null || !File.Exists(stagedPath)) return new(false, -1, "", "", "that file is not a staged start save");
        byte[] bytes;
        try { bytes = WhsSave.ReadShared(sourceFile); } catch (IOException ex) { return new(false, -1, "", "", "the start save cannot be read: " + ex.Message); }
        if (!WhsSave.Verify(bytes).Ok) return new(false, -1, "", "", "the start save does not verify");
        uint old = WhsSave.ReadSeed(WhsSave.Inflate(bytes).Raw) ?? 0, seed;
        do seed = newSeed?.Invoke() ?? BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));
        while (seed == 0 || seed == old);
        var outBytes = Rekey(bytes, seed);
        string part = stagedPath + ".part";
        File.WriteAllBytes(part, outBytes);
        File.Move(part, stagedPath, overwrite: true);
        var back = WhsSave.ReadShared(stagedPath);
        if (!back.AsSpan().SequenceEqual(outBytes) || WhsSave.ReadSeedFromFile(stagedPath) != seed)
            return new(false, -1, "", "", "the swapped save did not read back as written");
        e.Sha256 = Convert.ToHexString(SHA256.HashData(outBytes)).ToLowerInvariant();
        e.SeedTag = WhsSave.SeedTag(seed);
        WriteLedger(ledger, l);
        var pp = WorldSaved.ParsePath(stagedPath);
        return new(true, pp?.Playline ?? -1, System.IO.Path.GetFileNameWithoutExtension(stagedPath), e.SeedTag, "");
    }

    public sealed record UnstageResult(int Removed, int Kept, List<string> Lines);

    /// <summary>
    /// Every staged start save: removed when it is still the bytes written AND its playline's only save (never played);
    /// kept as a normal save otherwise (it was loaded: the game saved beside it, or the launcher said so with
    /// <see cref="MarkUsed"/>). Either way it leaves the ledger.
    /// </summary>
    public static UnstageResult Unstage(string ledger)
    {
        var l = ReadLedger(ledger);
        int removed = 0, kept = 0;
        var lines = new List<string>();
        foreach (var e in l)
        {
            string disp = $"{System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(e.Path))}/{System.IO.Path.GetFileName(e.Path)}";
            if (!File.Exists(e.Path)) { lines.Add($"{disp}: gone already"); continue; }
            string dir = System.IO.Path.GetDirectoryName(e.Path)!;
            bool alone = Directory.EnumerateFiles(dir, "*.whs").All(f => string.Equals(f, e.Path, StringComparison.OrdinalIgnoreCase));
            bool same;
            try { same = Convert.ToHexString(SHA256.HashData(WhsSave.ReadShared(e.Path))).ToLowerInvariant() == e.Sha256; }
            catch (IOException) { same = false; }
            if (alone && same)
            {
                File.Delete(e.Path);
                if (e.CreatedDir && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
                removed++;
                lines.Add($"{disp}: never played -- removed");
            }
            else { kept++; lines.Add($"{disp}: {(alone ? "changed" : "played (other saves beside it)")} -- kept as a normal save"); }
        }
        WriteLedger(ledger, []);
        return new(removed, kept, lines);
    }

    /// <summary>The launcher loaded this staged save: it is a normal save from now on (out of the ledger).</summary>
    public static bool MarkUsed(string ledger, string path)
    {
        var l = ReadLedger(ledger);
        int n = l.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        if (n > 0) WriteLedger(ledger, l);
        return n > 0;
    }

    // ------------------------------------------------------------------ validate (the start-save recipe)

    public sealed record Check(string Name, bool Pass, string Detail);

    /// <summary>
    /// Where the story stands: M03 ("socky", the bar fight and the pillory) Done -- Hans and Henry have parted -- and
    /// nothing of M05 ("svatba", which the game starts at once) Done yet. From the save's ConceptState tree.
    /// </summary>
    public static (bool Ok, string Detail) CutPoint(byte[] raw)
    {
        System.Xml.XmlDocument? doc;
        try { doc = SaveGameReader.ParseConceptState(raw); } catch (System.Xml.XmlException) { doc = null; }
        if (doc is null) return (false, "the save holds no quest tree");
        var tro = doc.SelectSingleNode("/Roots/_Barbora/Nodes/_trosecko/Nodes");
        string State(string q) => tro?.SelectSingleNode($"_{q}//_questProgress")?.Attributes?["value"]?.Value ?? "none";
        string m03 = State("socky"), m05 = State("svatba");
        int m05Done = 0;
        if (tro?.SelectSingleNode("_svatba") is { } sv)
            foreach (System.Xml.XmlNode n in sv.SelectNodes(".//Logs") ?? (System.Xml.XmlNodeList)new System.Xml.XmlDocument().ChildNodes)
                if (n.LastChild?.Name is "Done" or "Completed") m05Done++;
        string d = $"M03 (the bar fight, the pillory) {m03}, M05 (Wedding Crashers) {m05} with {m05Done} objective(s) done";
        bool ok = m03 == "Done" && m05 is not ("Done" or "Failed") && m05Done == 0;
        return (ok, ok ? d : d + ": the start save must be made right after Hans and Henry part ways");
    }

    /// <summary>
    /// Which of the prologue's three playstyles this Henry took: the one whose own skills (each preset's skills no other
    /// preset raises) carry the most experience. Null when none of them has any.
    /// </summary>
    public static (string? Style, string Detail) PlaystyleOf(byte[] raw)
    {
        var rec = WhsSave.FindSoul(raw, WhsSave.HenrySoul);
        if (rec is not WhsSave.Node r) return (null, "no Henry record");
        var soul = WhsSave.DecodePlayerSoul(raw, r);
        var xp = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var kv in (soul.Scalars.GetValueOrDefault("skill_xp") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = kv.IndexOf('=');
            if (eq > 0 && long.TryParse(kv[(eq + 1)..], out long v)) xp[kv[..eq]] = v;
        }
        var sums = Wo159Rules.StyleSkills.ToDictionary(s => s.Key, s => s.Value.Sum(k => xp.GetValueOrDefault(k)));
        var best = sums.OrderByDescending(s => s.Value).First();
        string d = string.Join(", ", sums.Select(s => $"{s.Key} {s.Value}"));
        return (best.Value > 0 ? best.Key : null, d);
    }

    public static List<Check> Validate(byte[] file, string? style = null)
    {
        var o = new List<Check>();
        var v = WhsSave.Verify(file);
        o.Add(new("reads and verifies", v.Ok, v.Reason));
        if (!v.Ok) return o;
        var c = WhsSave.Inflate(file);
        string? build = WhsSave.DescriptionSummary(c.Desc).GetValueOrDefault("BuildInfo");
        o.Add(new("Modding Tools build", build is not null && !Wo154Rules.IsRegularGameBuild(build),
            build is null ? "no build in the header" : Wo154Rules.IsRegularGameBuild(build) ? $"{build} is a regular-game build" : build));
        var who = WhsSave.PlayerOf(c.Raw);
        o.Add(new("after the prologue, as Henry", who.IsHenry, who.IsHenry ? "the player is player_henry" : $"the player is {who.Player} (the prologue)"));
        o.Add(new("a playthrough seed", WhsSave.ReadSeed(c.Raw) is not null, ""));
        int hits = CountAscii(c.Raw, "kcd2mp");
        o.Add(new("no partner / mod data in the world", hits == 0, hits == 0 ? "" : $"{hits} mod name(s) in the world (a partner's figure, a mod entity): save again in a world nobody joined, with the mod idle"));
        bool user = Regex.IsMatch(c.Desc, "(UserName|BuildComputer)=\"[^\"]+\"");
        o.Add(new("no account or machine name in the header", !user, user ? "the header names the account or machine that saved it: supply the copy -WriteScrubbed makes" : ""));
        bool mods = c.Desc.Contains("S_ModInfo", StringComparison.Ordinal);
        o.Add(new("no mods listed in the header", !mods, mods ? "the header lists mods used: supply the copy -WriteScrubbed makes (it empties the list)" : ""));
        var cut = CutPoint(c.Raw);
        o.Add(new("where Hans and Henry part ways", cut.Ok, cut.Detail));
        if (style is not null)
        {
            var ps = PlaystyleOf(c.Raw);
            o.Add(new($"the {style} playstyle", ps.Style == style,
                ps.Style is null ? $"no playstyle skill has experience ({ps.Detail})" : $"its skills look like {ps.Style} ({ps.Detail})"));
        }
        return o;
    }

    private static int CountAscii(byte[] raw, string needle)
    {
        var n = Encoding.ASCII.GetBytes(needle);
        int count = 0;
        for (int i = 0; i + n.Length <= raw.Length; i++)
        {
            int k = 0;
            while (k < n.Length && (raw[i + k] | 0x20) == n[k]) k++;
            if (k == n.Length) { count++; i += n.Length - 1; }
        }
        return count;
    }

    // ------------------------------------------------------------------ CLI

    /// <summary>
    /// <c>--w159 saves [--saves DIR]</c> · <c>stage --source DIR|FILE [--saves DIR]</c> · <c>unstage</c> ·
    /// <c>used --path FILE</c> · <c>validate --file FILE [--write-scrubbed OUT]</c>. One line: <c>W159 {json}</c>.
    /// </summary>
    public static int RunCli(string[] args, TextWriter w)
    {
        string? Opt(string k) { int i = Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        int at = Array.IndexOf(args, "--w159");
        string cmd = at >= 0 && at + 1 < args.Length ? args[at + 1] : "";
        string? saves = Opt("--saves") ?? GameBridge.ResolveSavesDir();
        string ledger = LedgerPath(Opt("--data"));
        void Out(object o) => w.WriteLine(CliTag + " " + JsonSerializer.Serialize(o));
        switch (cmd)
        {
            case "saves":
            {
                if (saves is null) { Out(new { ok = false, why = "no saves folder" }); return 1; }
                var henry = new HenryStore(Path.Combine(Path.GetDirectoryName(ledger)!, "henry"), log: _ => { });
                var hosted = henry.Hosted();
                var joined = henry.Worlds().Select(x => henry.BaseTagOf(x.Tag)).ToHashSet(StringComparer.Ordinal);
                var staged = ReadLedger(ledger).Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var r = MenuSaves(saves, t => joined.Contains(t) && !hosted.Contains(t), p =>
                {
                    try { return WhsSave.PlayerOf(WhsSave.Inflate(WhsSave.ReadShared(p)).Raw).IsHenry; }
                    catch (Exception ex) when (ex is IOException or InvalidDataException) { return false; }
                }, staged);
                // a staged start save never played: what New adventure loads
                var st = ReadLedger(ledger).LastOrDefault(e => File.Exists(e.Path));
                Out(new
                {
                    ok = true,
                    usable = r.Usable.Select(u => new { pl = u.Playline, name = u.Name, label = u.Label }),
                    hidden = r.Hidden,
                    hiddenRegular = r.HiddenRegular,
                    hiddenCopies = r.HiddenCopies,
                    hiddenPrologue = r.HiddenPrologue,
                    free = r.Free,
                    staged = st is null ? null : new { pl = WorldSaved.ParsePath(st.Path)?.Playline ?? 255, name = Path.GetFileNameWithoutExtension(st.Path), path = st.Path, seedTag = st.SeedTag },
                });
                return 0;
            }
            case "stage":
            {
                if (saves is null) { Out(new { ok = false, why = "no saves folder" }); return 1; }
                var r = Stage(saves, Opt("--source") ?? "", ledger);
                Out(new { ok = r.Ok, pl = r.Playline, name = r.Name, seedTag = r.SeedTag, why = r.Why });
                return r.Ok ? 0 : 1;
            }
            case "swap":
            {
                // the chosen playstyle's start save over the staged one, just before the menu's load
                string root = Opt("--source") ?? "", style = Opt("--style") ?? "";
                if (StyleFile(root, style) is not string sf) { Out(new { ok = false, why = $"the {style} start save is not installed" }); return 1; }
                var r = Swap(Opt("--path") ?? "", sf, ledger);
                Out(new { ok = r.Ok, pl = r.Playline, name = r.Name, seedTag = r.SeedTag, why = r.Why });
                return r.Ok ? 0 : 1;
            }
            case "unstage":
            {
                var r = Unstage(ledger);
                Out(new { ok = true, removed = r.Removed, kept = r.Kept, lines = r.Lines });
                return 0;
            }
            case "used":
            {
                bool ok = MarkUsed(ledger, Opt("--path") ?? "");
                Out(new { ok });
                return ok ? 0 : 1;
            }
            case "validate":
            {
                string f = Opt("--file") ?? "";
                byte[] b;
                try { b = WhsSave.ReadShared(f); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Out(new { ok = false, pass = false, checks = new[] { new { name = "reads", pass = false, detail = ex.Message } } }); return 1; }
                var checks = Validate(b, Opt("--style"));
                bool pass = checks.All(x => x.Pass);
                string? scrubbed = null;
                if (Opt("--write-scrubbed") is string outPath && checks.Take(4).All(x => x.Pass))
                {
                    var c = WhsSave.Inflate(b);
                    var o = WhsSave.Deflate(Encoding.UTF8.GetBytes(ScrubDescription(c.Desc)), c.Raw, c.FooterTail);
                    using (var fs = new FileStream(outPath, FileMode.CreateNew, FileAccess.Write)) fs.Write(o);
                    scrubbed = outPath;
                }
                Out(new { ok = true, pass, checks = checks.Select(x => new { name = x.Name, pass = x.Pass, detail = x.Detail }), scrubbed });
                return pass ? 0 : 1;
            }
        }
        Out(new { ok = false, why = "usage: --w159 saves|stage|swap|unstage|used|validate" });
        return 2;
    }
}

/// <summary>WO-159: the small rules shared by the agent, its IPC and the launcher's words.</summary>
public static class Wo159Rules
{
    /// <summary>The prologue's three playstyles (M01's stat presets: fighter, diplomat, scout), as the menu names them.</summary>
    public static readonly string[] Styles = ["soldier", "adviser", "scout"];

    /// <summary>The skills only that preset raises (WO-159 findings: the three stat presets in M01).</summary>
    public static readonly Dictionary<string, string[]> StyleSkills = new(StringComparer.Ordinal)
    {
        ["soldier"] = ["heavy_weapons", "weapon_large", "weapon_unarmed"],
        ["adviser"] = ["alchemy", "scholarship", "drinking"],
        ["scout"] = ["marksmanship", "survival", "stealth", "thievery"],
    };

    /// <summary>bring | fresh | fresh:&lt;playstyle&gt; (the launcher's /join-choice).</summary>
    public static bool TryParseJoinChoice(string c, out string choice, out string? style)
    {
        c = (c ?? "").Trim().ToLowerInvariant();
        choice = ""; style = null;
        if (c is "bring" or "fresh") { choice = c; return true; }
        if (c.StartsWith("fresh:", StringComparison.Ordinal) && Styles.Contains(c[6..])) { choice = "fresh"; style = c[6..]; return true; }
        return false;
    }

    /// <summary>The mod's busy reason: "prologue 7" = the reason "prologue" and 7 minutes left (the status packet's arg).</summary>
    public static (string Name, ushort Arg) BusyReason(string busy)
    {
        var p = (busy ?? "").Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (p.Length == 2 && ushort.TryParse(p[1], out ushort a)) return (p[0], a);
        return (busy ?? "", 0);
    }

    /// <summary>What a deferred joiner reads.</summary>
    public static string DeferredText(string reason, ushort arg) => reason == "prologue"
        ? $"Your host is watching the prologue (about {Math.Max(1, (int)arg)} min left). You'll join as soon as it ends or they skip it."
        : "Your host is busy, you'll join in a moment.";

    /// <summary>KCDMP_START_SAVE (tests) or start-save beside this agent (Setup puts it beside the launcher and the agent).</summary>
    public static string? StartSaveRoot()
    {
        string root = Environment.GetEnvironmentVariable("KCDMP_START_SAVE") is { Length: > 0 } e ? e : Path.Combine(AppContext.BaseDirectory, "start-save");
        return Directory.Exists(root) ? root : null;
    }

    /// <summary>
    /// A new player whose game has run (its user folder exists) but who never saved has no saves folder yet: it is made,
    /// empty, so a first join has somewhere to put the host's world. Null when the game never ran here.
    /// </summary>
    public static string? CreateSavesDirIfGameRan()
    {
        if (GameBridge.Kcd2UserFolder() is not string user || !Directory.Exists(user)) return null;
        string saves = Path.Combine(user, "saves");
        try { Directory.CreateDirectory(saves); return saves; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
