// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;

namespace KcdMp.Client;

// WO-175 (0.49.2) -- S4, the agent's half (docs/WO-175-findings.md; the pure check/repair is Wo175SaveHealth.cs, the Lua half is
// kdcmp.lua's WO-175 block). The 0.49 weapon lock lives in the SAVE: a quest fight whose result stayed "Started" after its
// controller finished keeps the game's unarmed-only rule on, and every load re-applies it. So:
//   * after a load (w175_check load, and mp_repair_combat's w175_check repair): the loaded file is checked; a stuck quest fight
//     -> a repaired copy NEXT TO it (<name>_fixed.whs; the original is never written), the native save list rescanned, the copy
//     loaded, the player told once. Without the plugin (no rescan) the player is told to load the copy after a restart.
//   * after a save (an always-on watch of the saves folder while mp_save_health is on): every new engine-named save is checked;
//     a stuck one gets its repaired copy at once (the next load uses it), and the player is told.
//   WO175-HEALTH lines (Lua) and MP-W175 lines here; MP-W175-STATS counts.
public partial class GameBridge
{
    private volatile bool _w175Health = true;
    private string? _w175LastLoaded;                                    // playlineN/x.whs, from "Loading saved game '...'"
    private readonly ConcurrentDictionary<string, byte> _w175Handled = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _w175SaveAt = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _w175Watcher;
    private long _w175Checks, _w175Stuck, _w175Repaired, _w175Loaded, _w175Failed;

    private void Wo175OnSaveLoadAccepted(string rel)
    {
        _w175LastLoaded = rel;
        Wo175EnsureWatcher();
    }

    private void Wo175OnEvent(string name, string? arg)
    {
        if (name == "w175_cfg")
        {
            if (arg is not null && arg.StartsWith("mp_save_health=", StringComparison.Ordinal))
            {
                _w175Health = arg.EndsWith("=on", StringComparison.Ordinal);
                Console.WriteLine($"MP-W175 mp_save_health {(_w175Health ? "on" : "off")}");
                Wo175EnsureWatcher();
            }
            return;
        }
        // w175_check load|repair|save
        Wo175EnsureWatcher();
        if (!_w175Health) return;
        if (arg is "load" or "repair") _ = Wo175CheckLoadedAsync(arg);
    }

    private static string? Wo175Full(string? savesDir, string rel) =>
        savesDir is null ? null : Path.Combine(savesDir, rel.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The loaded save: stuck -> repaired copy, rescan, load it. Once per file per agent run (a copy never loops).</summary>
    private async Task Wo175CheckLoadedAsync(string why)
    {
        string? rel = _w175LastLoaded;
        _savesDir ??= ResolveSavesDir();
        string? path = rel is null ? null : Wo175Full(_savesDir, rel);
        if (path is null || !File.Exists(path))
        {
            Console.WriteLine($"MP-W175 check why={why}: the loaded save is not known here (rel={rel ?? "-"})");
            if (why == "repair")
                _ = ExecLuaAsync("if KCD2MP_W175Health then KCD2MP_W175Health(\"repair\", \"ok\", \"loaded-save-unknown\", \"\", false) end");
            return;
        }
        Interlocked.Increment(ref _w175Checks);
        var res = await Task.Run(() => Wo175RepairInto(path));
        if (res.Kind == "ok")
        {
            Console.WriteLine($"MP-W175 check why={why} {rel}: healthy (no stuck quest fight)");
            _ = ExecLuaAsync($"if KCD2MP_W175Health then KCD2MP_W175Health(\"{why}\", \"ok\", \"none\", \"\", false) end");
            return;
        }
        if (res.Kind == "failed")
        {
            Interlocked.Increment(ref _w175Failed);
            Console.WriteLine($"MP-W175 check why={why} {rel}: FAILED {res.Why}");
            _ = ExecLuaAsync($"if KCD2MP_W175Health then KCD2MP_W175Health(\"{why}\", \"failed\", \"{Wo175Lua(res.Why)}\", \"\", false) end");
            return;
        }
        // repaired: list it, then load it
        int pl = int.Parse(Path.GetFileName(Path.GetDirectoryName(path)!)[8..], CultureInfo.InvariantCulture);
        string baseName = Path.GetFileNameWithoutExtension(res.NewPath!);
        string newRel = $"playline{pl}/{baseName}.whs";
        var listed = await _combat.SaveListAsync(1, pl, baseName);
        if (listed is null || !listed.Listed)
        {
            Console.WriteLine($"MP-W175 {newRel} written; the native rescan {(listed is null ? "did not answer (no plugin)" : "does not list it")} -- the player loads it after a restart");
            _ = ExecLuaAsync($"if KCD2MP_W175Health then KCD2MP_W175Health(\"{why}\", \"repaired\", \"stuck-quest-fight:{Wo175Lua(res.What)}\", \"{newRel}\", false) end");
            return;
        }
        Interlocked.Increment(ref _w175Loaded);
        Console.WriteLine($"MP-W175 {newRel} written and listed -- loading the repaired copy");
        await ExecLuaAsync($"if KCD2MP_W175Health then KCD2MP_W175Health(\"{why}\", \"repaired\", \"stuck-quest-fight:{Wo175Lua(res.What)}\", \"{newRel}\", true) end");
        await Task.Delay(2500);   // the toast is read before the loading screen
        if (!await Wo177WaitQuietAsync("w175-repaired")) return;   // WO-177 A5: never a load while a fight is running (the copy stays listed; the player loads it later)
        await ExecLuaAsync($"if KCD2MP_Wo124LoadGame then KCD2MP_Wo124LoadGame({pl}, \"{baseName}\", \"w175-repaired\") end");
    }

    public sealed record Wo175Result(string Kind, string Why, string What, string? NewPath);

    /// <summary>Check one file; stuck -> write the repaired copy beside it (a .part, renamed, read back). Never writes the original.</summary>
    public static Wo175Result Wo175RepairInto(string path)
    {
        byte[] file;
        try { file = File.ReadAllBytes(path); }
        catch (Exception ex) { return new("failed", "unreadable: " + ex.Message, "", null); }
        var rep = Wo175SaveHealth.Check(file);
        if (!rep.Readable) return new("failed", rep.Why, "", null);
        if (rep.Stuck.Count == 0) return new("ok", "", "", null);
        string what = string.Join(",", rep.Stuck.Select(s => s.QuestPath.Split('/').Length > 2
            ? string.Join("/", s.QuestPath.Split('/').TakeLast(2)) : s.QuestPath));
        try
        {
            var fixedBytes = Wo175SaveHealth.Repair(file, out _);
            string dir = Path.GetDirectoryName(path)!;
            string name = Wo175SaveHealth.RepairedName(Path.GetFileName(path), Directory.EnumerateFiles(dir, "*.whs"));
            string dst = Path.Combine(dir, name), part = dst + ".part";
            File.WriteAllBytes(part, fixedBytes);
            File.Move(part, dst, overwrite: false);
            var back = File.ReadAllBytes(dst);
            if (!back.AsSpan().SequenceEqual(fixedBytes) || Wo175SaveHealth.Check(back).Stuck.Count != 0)
                return new("failed", "the repaired copy does not read back", what, null);
            return new("repaired", "", what, dst);
        }
        catch (Exception ex) { return new("failed", "repair: " + ex.Message, what, null); }
    }

    private static string Wo175Lua(string s) => s.Replace("\\", "/").Replace("\"", "'");

    // ------------------------------------------------------------------ after a save: the always-on watch

    private void Wo175EnsureWatcher()
    {
        if (!_w175Health)
        {
            if (_w175Watcher is not null) { _w175Watcher.EnableRaisingEvents = false; _w175Watcher.Dispose(); _w175Watcher = null; }
            return;
        }
        if (_w175Watcher is not null) return;
        _savesDir ??= ResolveSavesDir();
        if (_savesDir is null) return;
        try
        {
            var w = new FileSystemWatcher(_savesDir, "*.whs") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
            w.Created += (_, e) => Wo175OnSaveFile(e.FullPath);
            w.Changed += (_, e) => Wo175OnSaveFile(e.FullPath);
            w.Renamed += (_, e) => Wo175OnSaveFile(e.FullPath);
            w.EnableRaisingEvents = true;
            _w175Watcher = w;
            Console.WriteLine("MP-W175 watching <saves>: every new save is checked for a stuck quest fight (mp_save_health on)");
        }
        catch (Exception ex) { Console.WriteLine($"MP-W175 could not watch the saves folder: {ex.Message}"); }
    }

    private void Wo175OnSaveFile(string path)
    {
        if (WorldSaved.ParsePath(path) is null) return;   // engine names only: never our own _fixed copies
        bool first = !_w175SaveAt.ContainsKey(path);
        _w175SaveAt[path] = DateTime.UtcNow;
        if (first) _ = Wo175SettleAndCheckAsync(path);
    }

    private async Task Wo175SettleAndCheckAsync(string path)
    {
        try
        {
            for (int i = 0; i < 40; i++)   // the writer finished: no event for 800 ms and the file verifies
            {
                await Task.Delay(500);
                if (_w175SaveAt.TryGetValue(path, out var at) && (DateTime.UtcNow - at).TotalMilliseconds < 800) continue;
                if (WhsSave.VerifyFile(path).Ok) break;
            }
            string disp = SaveDisplay(path);
            string key = disp + "@" + File.GetLastWriteTimeUtc(path).Ticks.ToString(CultureInfo.InvariantCulture);
            if (!_w175Handled.TryAdd(key, 0)) return;
            Interlocked.Increment(ref _w175Checks);
            var res = await Task.Run(() => Wo175RepairInto(path));
            if (res.Kind == "ok") { Console.WriteLine($"MP-W175 save {disp}: healthy"); return; }
            if (res.Kind == "failed")
            {
                Interlocked.Increment(ref _w175Failed);
                Console.WriteLine($"MP-W175 save {disp}: check FAILED {res.Why}");
                _ = ExecLuaAsync($"if KCD2MP_W175Health then KCD2MP_W175Health(\"save\", \"failed\", \"{Wo175Lua(res.Why)}\", \"\", false) end");
                return;
            }
            Interlocked.Increment(ref _w175Stuck);
            Interlocked.Increment(ref _w175Repaired);
            Console.WriteLine($"MP-W175 save {disp}: stuck quest fight ({res.What}) -> repaired copy {SaveDisplay(res.NewPath!)}; the original is untouched");
            _ = ExecLuaAsync($"if KCD2MP_W175Health then KCD2MP_W175Health(\"save\", \"repaired\", \"stuck-quest-fight:{Wo175Lua(res.What)}\", \"{SaveDisplay(res.NewPath!)}\", false) end");
        }
        catch (Exception ex) { Console.WriteLine($"MP-W175 save check failed for {SaveDisplay(path)}: {ex.Message}"); }
        finally { _w175SaveAt.TryRemove(path, out _); }
    }

    internal string Wo175StatsText() => FormattableString.Invariant(
        $"MP-W175-STATS health={(_w175Health ? "on" : "off")} checks={_w175Checks} stuck={_w175Stuck} repaired={_w175Repaired} loaded_repaired={_w175Loaded} failed={_w175Failed}");
}
