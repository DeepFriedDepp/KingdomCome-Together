// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace KcdMp.Client;

/// <summary>
/// WO-148 (the content audit): the dice keys -- F2, F4-F9 to mark, F9 cast/invite, F11 bank/accept,
/// F12 yield/decline, U cancel -- without shipping Warhorse's files.
///
/// The game registers an action only if both Libs/Config/defaultProfile.xml (the action) and
/// Libs/Config/keybindSuperactions.xml (its key) name it (WO-6), and a mod can only replace those
/// two files whole. Until 0.42.5 the mod's pak carried full copies of the game's two files with ten
/// lines each of ours added. Now the repo and Setup carry only our lines (kdcmp/ConfigPatch/*.xml,
/// embedded here), and this builds the two files on the player's machine: the player's own copies,
/// read out of the game's Data paks, with our lines inserted at a structural place (the end of the
/// "interaction" actionmap; the end of the keybinds root). The result goes into a second pak beside
/// kdcmp.pak, Mods\kdcmp\Data\kdcmp_keys.pak (the game opens every *.pak in a mod's folder). Setup
/// runs it after the copy, the launcher before every game start (a game update changes the source;
/// the pak is rewritten only when its content would change). A failure costs the keys only: the
/// console commands (mp_dice_*, mp_invite, mp_accept, mp_decline, mp_quest_*, mp_menu) still work.
///
/// WO-154: the mod menu's four actions ride the same two files -- Insert opens and closes it (the
/// player's settings.json <c>MenuKey</c>: one of <see cref="MenuKeys"/>, read when the pak is built;
/// anything else is Insert, and the outcome says so), PgUp / PgDn choose, End changes. The dice keys
/// are exactly as before.
/// </summary>
public static class KeybindPak
{
    public const string PakFileName = "kdcmp_keys.pak";
    public const string ProfileEntry = "Libs/Config/defaultProfile.xml";
    public const string SuperactionsEntry = "Libs/Config/keybindSuperactions.xml";

    /// <summary>The actions our lines add (both files name each once): WO-148's ten dice actions, WO-154's four menu actions.</summary>
    public static readonly string[] Actions =
    {
        "kcd2mp_dice_mark_1", "kcd2mp_dice_mark_2", "kcd2mp_dice_mark_3", "kcd2mp_dice_mark_4",
        "kcd2mp_dice_mark_5", "kcd2mp_dice_mark_6", "kcd2mp_dice_cast", "kcd2mp_dice_bank",
        "kcd2mp_dice_yield", "kcd2mp_dice_cancel",
        "kcd2mp_menu_toggle", "kcd2mp_menu_up", "kcd2mp_menu_down", "kcd2mp_menu_change",
    };

    /// <summary>WO-148's dice keys. Nothing after WO-148 changes them (WO-154's tests pin it).</summary>
    public static readonly (string Action, string Key)[] DiceKeys =
    {
        ("kcd2mp_dice_mark_1", "f2"), ("kcd2mp_dice_mark_2", "f4"), ("kcd2mp_dice_mark_3", "f5"), ("kcd2mp_dice_mark_4", "f6"),
        ("kcd2mp_dice_mark_5", "f7"), ("kcd2mp_dice_mark_6", "f8"), ("kcd2mp_dice_cast", "f9"), ("kcd2mp_dice_bank", "f11"),
        ("kcd2mp_dice_yield", "f12"), ("kcd2mp_dice_cancel", "u"),
    };

    /// <summary>WO-154: the action that opens and closes the mod menu (its key is the player's choice).</summary>
    public const string MenuToggleAction = "kcd2mp_menu_toggle";
    public const string MenuKeyDefault = "insert";
    public const string MenuKeySetting = "MenuKey";

    /// <summary>
    /// WO-154: the keys a player may give the menu (settings.json <c>MenuKey</c>). Each is named nowhere in the
    /// game's own two files (1.5.5, retail and Modding Tools identical), none is used by the mod's other keys,
    /// and none is one CryEngine keeps for itself (ScrollLock, Pause, the console key). Short on purpose.
    /// </summary>
    public static readonly string[] MenuKeys = { "insert", "np_add", "np_subtract" };

    /// <summary>WO-154: the menu's fixed navigation keys (only while it is open).</summary>
    public static readonly (string Action, string Key)[] MenuNavKeys =
    {
        ("kcd2mp_menu_up", "pgup"), ("kcd2mp_menu_down", "pgdn"), ("kcd2mp_menu_change", "end"),
    };

    public sealed record Outcome(bool Ok, string Action, string Detail, string? SourcePak = null)
    {
        /// <summary>One machine-read line for Setup and the launcher; file names only, never a path.</summary>
        public string ToJson() => JsonSerializer.Serialize(new
        {
            ok = Ok,
            action = Action,
            detail = Detail,
            source = SourcePak is null ? null : Path.GetFileName(SourcePak),
        });
    }

    // ---- the pure half (unit-tested) -------------------------------------------------------------

    /// <summary>The lines between the kdcmp_patch tags of a patch file, each ending in CRLF.</summary>
    public static string PatchBody(string patchFile)
    {
        string[] lines = patchFile.Replace("\r\n", "\n").Split('\n');
        int open = Array.FindIndex(lines, l => l.TrimStart().StartsWith("<kdcmp_patch", StringComparison.Ordinal));
        int close = Array.FindLastIndex(lines, l => l.Trim() == "</kdcmp_patch>");
        if (open < 0 || close <= open) throw new InvalidDataException("not a kdcmp_patch file");
        var sb = new StringBuilder();
        for (int i = open + 1; i < close; i++) sb.Append(lines[i]).Append("\r\n");
        return sb.ToString();
    }

    private static readonly Regex InteractionOpen = new(@"<actionmap\s+name\s*=\s*""interaction""", RegexOptions.CultureInvariant);

    /// <summary>The game's defaultProfile.xml with our actions as the last lines of its "interaction"
    /// actionmap; null (and why) when the file does not have the shape this relies on.</summary>
    public static string? MergeProfile(string vanilla, string body, out string why)
    {
        if (!Wellformed(vanilla, out why)) { why = "the game's defaultProfile.xml does not parse: " + why; return null; }
        if (ContainsAny(vanilla)) { why = "the game's defaultProfile.xml already names a kcd2mp action"; return null; }
        MatchCollection open = InteractionOpen.Matches(vanilla);
        if (open.Count != 1) { why = $"{open.Count} actionmaps named \"interaction\" (expected 1)"; return null; }
        int start = open[0].Index;
        int close = vanilla.IndexOf("</actionmap>", start, StringComparison.Ordinal);
        if (close < 0) { why = "the \"interaction\" actionmap never closes"; return null; }
        int nested = vanilla.IndexOf("<actionmap", start + 1, StringComparison.Ordinal);
        if (nested >= 0 && nested < close) { why = "an actionmap inside \"interaction\""; return null; }
        return InsertAtLineStart(vanilla, close, body, ProfileEntry, out why);
    }

    /// <summary>The game's keybindSuperactions.xml with our superactions as the last lines of its root.</summary>
    public static string? MergeSuperactions(string vanilla, string body, out string why)
    {
        if (!Wellformed(vanilla, out why)) { why = "the game's keybindSuperactions.xml does not parse: " + why; return null; }
        if (ContainsAny(vanilla)) { why = "the game's keybindSuperactions.xml already names a kcd2mp action"; return null; }
        int close = vanilla.LastIndexOf("</keybinds>", StringComparison.Ordinal);
        if (close < 0) { why = "no keybinds root"; return null; }
        return InsertAtLineStart(vanilla, close, body, SuperactionsEntry, out why);
    }

    private static string? InsertAtLineStart(string vanilla, int at, string body, string entry, out string why)
    {
        int lineStart = vanilla.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
        if (!vanilla.Contains("\r\n")) body = body.Replace("\r\n", "\n");   // keep the file's own line endings
        string merged = vanilla.Insert(lineStart, body);
        if (!Wellformed(merged, out why)) { why = entry + " no longer parses with our lines: " + why; return null; }
        foreach (string a in Actions)
        {
            // exactly what our lines add: the game's file named none of them (checked above)
            string named = "name=\"" + a + "\"";
            int want = Regex.Matches(body, named).Count, n = Regex.Matches(merged, named).Count;
            if (want == 0 || n != want) { why = $"{entry}: {a} named {n} times after the merge (ours: {want})"; return null; }
        }
        why = "ok";
        return merged;
    }

    private static bool ContainsAny(string text) => text.Contains("kcd2mp_", StringComparison.Ordinal);

    // ---- WO-154: the menu key (pure, unit-tested) ---------------------------------------------------

    /// <summary>
    /// The settings.json <c>MenuKey</c> as the key to bind: one of <see cref="MenuKeys"/> (any case, trimmed), else
    /// Insert. <paramref name="note"/> is "" for a known key or none; for anything else it says what was there and
    /// that Insert is used (a value that is not a plain key name is not repeated: it may be anything).
    /// </summary>
    public static string ResolveMenuKey(string? setting, out string note)
    {
        note = "";
        string s = (setting ?? "").Trim().ToLowerInvariant();
        if (s.Length == 0) return MenuKeyDefault;
        if (Array.IndexOf(MenuKeys, s) >= 0) return s;
        string shown = Regex.IsMatch(s, "^[a-z0-9_]{1,16}$") ? "'" + s + "'" : "(not a key name)";
        note = $"MenuKey {shown} is not one of {string.Join("|", MenuKeys)} -- {MenuKeyDefault} used";
        return MenuKeyDefault;
    }

    private static readonly Regex ToggleBlock = new(
        @"(<superaction\s+name=""" + MenuToggleAction + @"""[^>]*>)(.*?)(</superaction>)",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private static readonly Regex ControlInput = new(@"(<control\s+input="")([^""]*)("")", RegexOptions.CultureInvariant);

    /// <summary>Our superactions with the menu toggle's one control set to <paramref name="key"/> (one of
    /// <see cref="MenuKeys"/>); every other line, the dice keys included, byte for byte as it was.</summary>
    public static string WithMenuKey(string superactionsBody, string key)
    {
        if (Array.IndexOf(MenuKeys, key) < 0) throw new ArgumentException("not a menu key: " + key, nameof(key));
        var blocks = ToggleBlock.Matches(superactionsBody);
        if (blocks.Count != 1) throw new InvalidDataException($"{blocks.Count} {MenuToggleAction} superactions in our lines (expected 1)");
        var b = blocks[0];
        string inner = b.Groups[2].Value;
        if (ControlInput.Matches(inner).Count != 1) throw new InvalidDataException($"{MenuToggleAction} must have exactly one control");
        string replaced = ControlInput.Replace(inner, m => m.Groups[1].Value + key + m.Groups[3].Value);
        return superactionsBody[..b.Groups[2].Index] + replaced + superactionsBody[(b.Groups[2].Index + b.Groups[2].Length)..];
    }

    /// <summary>The key a keybindSuperactions.xml binds to one action (its superaction's first control), or null.</summary>
    public static string? BoundKey(string superactionsXml, string action)
    {
        var block = new Regex(@"<superaction\s+name=""" + Regex.Escape(action) + @"""[^>]*>(.*?)</superaction>",
                              RegexOptions.CultureInvariant | RegexOptions.Singleline).Match(superactionsXml);
        if (!block.Success) return null;
        var c = ControlInput.Match(block.Groups[1].Value);
        return c.Success ? c.Groups[2].Value : null;
    }

    /// <summary>settings.json's MenuKey as written (null: no file, not one JSON object, no such key, not a string).
    /// Reading never changes the file.</summary>
    public static string? ReadMenuKeySetting(string settingsPath)
    {
        try { return SettingsJson.Read(settingsPath)?[MenuKeySetting] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue(out string? s) ? s : null; }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>The menu key a built kdcmp_keys.pak binds (what this game run uses), or null when it cannot be read.</summary>
    public static string? BoundMenuKeyInPak(string pakPath)
    {
        try
        {
            using ZipArchive z = ZipFile.OpenRead(pakPath);
            ZipArchiveEntry? s = Find(z, SuperactionsEntry);
            return s is null ? null : BoundKey(ReadText(s), MenuToggleAction);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { return null; }
    }

    private static bool Wellformed(string xml, out string why)
    {
        try
        {
            var doc = new XmlDocument { XmlResolver = null };
            using var r = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            doc.Load(r);
            why = "ok";
            return true;
        }
        catch (XmlException ex) { why = ex.Message; return false; }
    }

    // ---- the files --------------------------------------------------------------------------------

    public static string EmbeddedPatch(string name)
    {
        using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream("KcdMp.Client.ConfigPatch." + name);
        if (s is null) throw new InvalidOperationException("missing embedded patch " + name);
        using var r = new StreamReader(s, Encoding.ASCII);
        return r.ReadToEnd();
    }

    /// <summary>The paks of the game's Data folder, in the order to look for a file: patch paks first
    /// (newest name first), then IPL_GameData.pak (where 1.5.5 keeps Libs/Config), then the rest.</summary>
    public static IEnumerable<string> SearchOrder(IEnumerable<string> paks)
    {
        var all = paks.Select(p => (path: p, name: Path.GetFileName(p))).ToList();
        foreach (var p in all.Where(x => x.name.StartsWith("patch", StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.name, StringComparer.OrdinalIgnoreCase))
            yield return p.path;
        foreach (var p in all.Where(x => x.name.Equals("IPL_GameData.pak", StringComparison.OrdinalIgnoreCase)))
            yield return p.path;
        foreach (var p in all.Where(x => !x.name.StartsWith("patch", StringComparison.OrdinalIgnoreCase) && !x.name.Equals("IPL_GameData.pak", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase))
            yield return p.path;
    }

    /// <summary>Both files from the first pak that holds both (never from Mods: only the game's own).</summary>
    public static bool ReadGameFiles(string gameRoot, out string profile, out string superactions, out string? sourcePak, out string why)
    {
        profile = superactions = "";
        sourcePak = null;
        string data = Path.Combine(gameRoot, "Data");
        if (!Directory.Exists(data)) { why = "no Data folder in the game root"; return false; }
        foreach (string pak in SearchOrder(Directory.EnumerateFiles(data, "*.pak", SearchOption.TopDirectoryOnly)))
        {
            try
            {
                using ZipArchive z = ZipFile.OpenRead(pak);
                ZipArchiveEntry? p = Find(z, ProfileEntry), s = Find(z, SuperactionsEntry);
                if (p is null || s is null) continue;
                profile = ReadText(p);
                superactions = ReadText(s);
                sourcePak = pak;
                why = "ok";
                return true;
            }
            catch (InvalidDataException) { }   // not a zip we can read: try the next
            catch (IOException) { }
        }
        why = "no pak in the game's Data folder holds both " + ProfileEntry + " and " + SuperactionsEntry;
        return false;
    }

    private static ZipArchiveEntry? Find(ZipArchive z, string entry) =>
        z.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/'), entry, StringComparison.OrdinalIgnoreCase));

    private static string ReadText(ZipArchiveEntry e)
    {
        using Stream s = e.Open();
        using var r = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return r.ReadToEnd();
    }

    /// <summary>Writes the pak unless one with exactly these entries is already there.
    /// Returns "written", "unchanged", or throws (the caller reports it).</summary>
    public static string WritePak(string pakPath, IReadOnlyList<(string name, byte[] data)> entries)
    {
        if (File.Exists(pakPath) && SameContent(pakPath, entries)) return "unchanged";
        string tmp = pakPath + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        using (ZipArchive z = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            foreach (var (name, data) in entries)
            {
                ZipArchiveEntry e = z.CreateEntry(name, CompressionLevel.Optimal);
                using Stream s = e.Open();
                s.Write(data, 0, data.Length);
            }
        }
        File.Move(tmp, pakPath, overwrite: true);
        return "written";
    }

    private static bool SameContent(string pakPath, IReadOnlyList<(string name, byte[] data)> entries)
    {
        try
        {
            using ZipArchive z = ZipFile.OpenRead(pakPath);
            if (z.Entries.Count != entries.Count) return false;
            foreach (var (name, data) in entries)
            {
                ZipArchiveEntry? e = z.GetEntry(name);
                if (e is null || e.Length != data.Length) return false;
                using Stream s = e.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                if (!ms.ToArray().AsSpan().SequenceEqual(data)) return false;
            }
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>The whole job: the game's files + our lines -> modDir\Data\kdcmp_keys.pak. WO-154:
    /// <paramref name="menuKeySetting"/> is settings.json's MenuKey as written (null = none: Insert).</summary>
    public static Outcome Build(string gameRoot, string modDir, string? menuKeySetting = null)
    {
        try
        {
            if (!Directory.Exists(modDir)) return new Outcome(false, "none", "the mod folder does not exist");
            if (!ReadGameFiles(gameRoot, out string profile, out string supa, out string? source, out string why))
                return new Outcome(false, "none", why);
            string? p = MergeProfile(profile, PatchBody(EmbeddedPatch("defaultProfile.interaction.xml")), out why);
            if (p is null) return new Outcome(false, "none", why, source);
            string menuKey = ResolveMenuKey(menuKeySetting, out string keyNote);
            string? s = MergeSuperactions(supa, WithMenuKey(PatchBody(EmbeddedPatch("keybindSuperactions.append.xml")), menuKey), out why);
            if (s is null) return new Outcome(false, "none", why, source);
            if (BoundKey(s, MenuToggleAction) != menuKey) return new Outcome(false, "none", "the menu key did not land in " + SuperactionsEntry, source);
            string data = Path.Combine(modDir, "Data");
            Directory.CreateDirectory(data);
            // The game's files are plain ASCII; written back without a byte-order mark.
            var enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            string action = WritePak(Path.Combine(data, PakFileName), new[]
            {
                (ProfileEntry, enc.GetBytes(p)),
                (SuperactionsEntry, enc.GetBytes(s)),
            });
            return new Outcome(true, action, $"{Actions.Length} actions in both files; menu key {menuKey}{(keyNote.Length > 0 ? " (" + keyNote + ")" : "")}", source);
        }
        catch (Exception ex)
        {
            return new Outcome(false, "none", ex.GetType().Name + ": " + NoPaths(ex.Message, modDir, gameRoot));
        }
    }

    /// <summary>The launcher logs the outcome and its logs go into bug reports: an exception's
    /// message (which may name a file) keeps no folder of this machine.</summary>
    public static string NoPaths(string text, string modDir, string gameRoot)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var (dir, label) in new[] { (modDir, "<mod>"), (gameRoot, "<game>"), (home, "<home>") })
        {
            string d = (dir ?? "").TrimEnd('\\', '/');
            if (d.Length >= 3) text = text.Replace(d, label, StringComparison.OrdinalIgnoreCase);
        }
        return text;
    }

    /// <summary>--keys-pak --game-root DIR [--mod-dir DIR] [--settings FILE]: prints "KEYS-PAK {json}"; exit 0 when
    /// the pak is in place. WO-154: the menu key comes from the launcher's settings.json (MenuKey), by default the
    /// one beside this exe -- where the launcher keeps it.</summary>
    public static int RunCli(string[] args, TextWriter output)
    {
        string? Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        string? root = Arg("--game-root");
        if (string.IsNullOrWhiteSpace(root))
        {
            output.WriteLine("KEYS-PAK " + new Outcome(false, "none", "usage: --keys-pak --game-root <dir> [--mod-dir <dir>] [--settings <settings.json>]").ToJson());
            return 2;
        }
        string mod = Arg("--mod-dir") ?? Path.Combine(root, "Mods", "kdcmp");
        string settings = Arg("--settings") ?? Path.Combine(AppContext.BaseDirectory, "settings.json");
        Outcome o = Build(root, mod, ReadMenuKeySetting(settings));
        output.WriteLine("KEYS-PAK " + o.ToJson());
        return o.Ok ? 0 : 1;
    }
}
