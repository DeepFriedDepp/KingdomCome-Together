// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace KcdMp.Client;

// WO-178: the bridge. The rules are Wo178Rules (Wo178.cs); the Lua half is kdcmp.lua's WO-178 block; the native half is wo178.cpp.
//
//   Part 0 every save the mod asks for (the Lua's w178_save: the quicksave key, the menu's Save now, mp_quicksave, the scheduled world save
//      while mp_world_autosave is on, the shared sleep's save) is the DLL's op 27 -- the engine's ManualSave, what a Saviour Schnapps asks
//      for, no item used. The new file is looked for in the saves folder and verified; the Lua is told (KCD2MP_W178SaveDone). The host's
//      folder watch (WO-122) still announces every save -- the game's own ones too -- so a joiner's Henry snapshot follows (WO-125).
//      A joiner's quicksave asks the host (W164 kind 12); the host's game saves the shared world and answers (kind 13); the host's
//      mp_quicksave reaches its joiners (kind 14). The draw probe's "stuck" marks the newest launcher backup (restore-pick.txt).
//        MP-W178 save why=<w> result=<requested|refused|not-armed|fault|busy> ...     MP-W178 save why=<w> file=<playlineN/x.whs> verified
//   C1 every host NPC combat event on a joiner offers its copy to the DLL's hostility answer (op 24): engaged | partner-fight | withdrawn.
//   C4 a blow the DLL holds for the figure's strike window (applied = 4) is finished in the background (op 28): the outcome still goes to
//      the joiner whose blow it was.
public partial class GameBridge
{
    private volatile bool _w178WorldAutosave = Wo178Rules.DefaultWorldAutosave;
    private volatile bool _w178Quicksave = Wo178Rules.DefaultQuicksave;
    private volatile bool _w178JoinerLock = Wo178Rules.DefaultJoinerLock;
    private volatile bool _w178HostLockAnswer = Wo178Rules.DefaultHostLockAnswer;
    private volatile bool _w178Hold = Wo178Rules.DefaultFallbackHold;
    private volatile bool _w178EngineCap = Wo178Rules.DefaultFallbackEngineCap;
    private volatile bool _w178PlayerRecoil = Wo178Rules.DefaultPlayerRecoil;
    private volatile bool _w178HitReach = Wo178Rules.DefaultHitReach;
    private volatile bool _w178ClinchGuard = Wo178Rules.DefaultClinchGuard;
    private volatile bool _w178OwnBlock = Wo178Rules.DefaultOwnBlock;
    private volatile bool _w178CopyEngineState = Wo178Rules.DefaultCopyEngineState;
    private volatile bool _w178PbAtContact = Wo178Rules.DefaultPbAtContact;
    private volatile bool _w178FigureEnemies = Wo178Rules.DefaultFigureEnemies;   // mp_figure_enemies (the mod sends it with mp_figure_soul)
    private int _w178SaveBusy;
    private byte _w178LastAsker = 0xFF;
    private readonly ConcurrentDictionary<uint, (byte Why, byte Flags, long At)> _w178Offered = new();
    private long _w178Saves, _w178SaveRefused, _w178NoFile, _w178Asks, _w178AsksIn, _w178Answers, _w178Offers, _w178Withdrawn, _w178Held,
                 _w178HeldDone, _w178HeldLost, _w178Picks, _w178TickN;
    private string _w178QsActiveKey = Wo178Rules.QuickSaveKeyDefault, _w178QsStoredKey = Wo178Rules.QuickSaveKeyDefault;

    private void Wo178OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w178_save":
                if (!Wo178Rules.IsSaveWhy(arg)) { Console.WriteLine($"MP-W178 save refused: '{arg}' is not a save word"); return; }
                _ = Wo178SaveAsync(arg!);
                return;
            case "w178_cfg":
                if (!Wo178Rules.TryParseCfg(arg, out string cfg, out bool on)) { Console.WriteLine($"MP-W178 cfg refused: '{arg}'"); return; }
                switch (cfg)
                {
                    case "world_autosave": _w178WorldAutosave = on; break;
                    case "quicksave": _w178Quicksave = on; _ = Wo178SendHostSettingAsync(force: true); break;
                    case "joiner_lock": _w178JoinerLock = on; break;
                    case "host_lock_answer": _w178HostLockAnswer = on; break;
                    case "fallback_hold": _w178Hold = on; break;
                    case "fallback_engine_cap": _w178EngineCap = on; break;
                    case "player_recoil": _w178PlayerRecoil = on; break;
                    case "hit_reach": _w178HitReach = on; break;
                    case "clinch_guard": _w178ClinchGuard = on; break;
                    case "own_block": _w178OwnBlock = on; break;
                    case "copy_engine_state": _w178CopyEngineState = on; break;
                    case "pb_at_contact": _w178PbAtContact = on; break;
                    case "figure_enemies": _w178FigureEnemies = on; break;
                }
                Console.WriteLine($"MP-W178 cfg {cfg}={(on ? "on" : "off")}");
                _ = Wo178PushConfigAsync();
                return;
            case "w178_qs_ask":       // joiner: the quicksave key -> the host's game saves the shared world
                if (!Wo178Rules.IsAskSource(arg)) return;
                if (!W137JoinerSession) { Console.WriteLine("MP-W178 quicksave ask: not a joiner in a session -- nothing sent"); return; }
                Interlocked.Increment(ref _w178Asks);
                Console.WriteLine($"MP-W178 quicksave ask ({arg}) -> the host");
                _ = W164SendAsync(Protocol.JoinTargetHost, Protocol.W164QuickSaveAsk, arg!);
                return;
            case "w178_qs_answer":    // host: the Lua's answer to the last asker
                if (!W137Host || !Wo178Rules.TryParseAnswer(arg, out _, out _) || _w178LastAsker == 0xFF) return;
                Interlocked.Increment(ref _w178Answers);
                _ = W164SendAsync(_w178LastAsker, Protocol.W164QuickSaveAnswer, arg!);
                return;
            case "w178_stuck":        // the draw probe could not free the weapons: the newest launcher backup is the one to restore
                Wo178MarkPick(arg ?? "draw-refused");
                return;
            case "w178_status":
                _ = Wo178StatusAsync();
                return;
            case "w178_trace":        // research: the DLL's duel trace for <secs>
                if (!ushort.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort secs)) return;
                _ = Task.Run(async () => { try { await _combat.Wo163Async(Protocol.Wo178OpTrace, [(byte)(secs & 0xFF), (byte)(secs >> 8)]); } catch { } });
                Console.WriteLine($"MP-W178 duel trace {secs} s asked");
                return;
        }
    }

    /// <summary>W164 kinds 12-14 (the dispatch in GameBridge.Wo164.cs).</summary>
    private async Task Wo178OnW164Async(byte src, byte kind, string text)
    {
        switch (kind)
        {
            case Protocol.W164QuickSaveAsk:
                if (!W137Host || !Wo178Rules.IsAskSource(text)) { Interlocked.Increment(ref _w164Malformed); return; }
                Interlocked.Increment(ref _w178AsksIn);
                _w178LastAsker = src;
                Console.WriteLine($"MP-W178 quicksave asked by player {src} ({text}) -- this game saves the shared world");
                await ExecLuaAsync($"if KCD2MP_W178PartnerQuickSave then KCD2MP_W178PartnerQuickSave({src}) end");
                return;
            case Protocol.W164QuickSaveAnswer:
                if (W137Host || !Wo178Rules.TryParseAnswer(text, out string word, out string why)) { Interlocked.Increment(ref _w164Malformed); return; }
                await ExecLuaAsync($"if KCD2MP_W178PartnerAnswer then KCD2MP_W178PartnerAnswer(\"{word}\", \"{EscapeLua(why)}\") end");
                return;
            case Protocol.W164QuickSaveSetting:
                if (W137Host || !Wo178Rules.TryParseSetting(text, out bool on)) { Interlocked.Increment(ref _w164Malformed); return; }
                await ExecLuaAsync($"if KCD2MP_W178HostSetting then KCD2MP_W178HostSetting({B(on)}) end");
                return;
        }
    }

    private bool? _w178SettingSent;
    private long _w178SettingSentMs;
    private async Task Wo178SendHostSettingAsync(bool force)
    {
        if (!W137Host) return;
        long now = Environment.TickCount64;
        if (!force && _w178SettingSent == _w178Quicksave && now - _w178SettingSentMs < 10_000) return;
        _w178SettingSent = _w178Quicksave;
        _w178SettingSentMs = now;
        foreach (byte j in Wo134Peers()) await W164SendAsync(j, Protocol.W164QuickSaveSetting, _w178Quicksave ? "quicksave=on" : "quicksave=off");
    }

    /// <summary>Once a second (the WO-151 loop): the DLL's switches every 10 s, the host's quicksave setting to its joiners.</summary>
    private async Task Wo178TickAsync()
    {
        long n = ++_w178TickN;
        if (n % 10 == 1) await Wo178PushConfigAsync();
        await Wo178SendHostSettingAsync(force: false);
        if (n % 60 == 0 && Interlocked.Read(ref _w178Offers) + Interlocked.Read(ref _w178Saves) > 0) await Wo178StatusAsync();
    }

    private async Task Wo178PushConfigAsync()
    {
        try
        {
            byte host = (byte)(_combatRoleApplied ? (_isDamageAuthority ? 1 : 0) : 255);
            var r = await _combat.Wo163Async(Protocol.Wo178OpConfig, [(byte)(_w178JoinerLock ? 1 : 0), (byte)(_w178HostLockAnswer ? 1 : 0),
                                                                      (byte)(_w178Hold ? 1 : 0), (byte)(_w178EngineCap ? 1 : 0), host,
                                                                      (byte)(_w178HitReach ? 1 : 0), (byte)(_w178ClinchGuard ? 1 : 0),
                                                                      (byte)(_w178OwnBlock ? 1 : 0), (byte)(_w178CopyEngineState ? 1 : 0),
                                                                      (byte)(_w178PbAtContact ? 1 : 0)]);
            if (r is { Ok: true, Payload.Length: >= 5 } x && (x.Payload[0] != (_w178JoinerLock ? 1 : 0) || x.Payload[1] != (_w178HostLockAnswer ? 1 : 0)))
                Console.WriteLine($"MP-W178 the DLL holds joiner_lock={x.Payload[0]} host_lock_answer={x.Payload[1]}");
        }
        catch (Exception ex) { Console.WriteLine($"MP-W178 config not pushed ({ex.GetType().Name})"); }
    }

    private async Task Wo178StatusAsync()
    {
        string? n = null;
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo178OpStatus, []);
            if (r is { Ok: true } x) n = Encoding.ASCII.GetString(x.Payload);
        }
        catch { }
        Console.WriteLine(FormattableString.Invariant(
            $"MP-W178-STATS world_autosave={On(_w178WorldAutosave)} quicksave={On(_w178Quicksave)} joiner_lock={On(_w178JoinerLock)} host_lock_answer={On(_w178HostLockAnswer)} hold={On(_w178Hold)} engine_cap={On(_w178EngineCap)} player_recoil={On(_w178PlayerRecoil)} hit_reach={On(_w178HitReach)} clinch_guard={On(_w178ClinchGuard)} own_block={On(_w178OwnBlock)} copy_engine_state={On(_w178CopyEngineState)} pb_at_contact={On(_w178PbAtContact)} saves={Interlocked.Read(ref _w178Saves)} refused={Interlocked.Read(ref _w178SaveRefused)} no_file={Interlocked.Read(ref _w178NoFile)} asks={Interlocked.Read(ref _w178Asks)} asks_in={Interlocked.Read(ref _w178AsksIn)} answers={Interlocked.Read(ref _w178Answers)} offers={Interlocked.Read(ref _w178Offers)} withdrawn={Interlocked.Read(ref _w178Withdrawn)} held={Interlocked.Read(ref _w178Held)} held_done={Interlocked.Read(ref _w178HeldDone)} held_lost={Interlocked.Read(ref _w178HeldLost)} picks={Interlocked.Read(ref _w178Picks)} native: {n ?? "no answer"}"));
    }

    // ================================================================ Part 0: the schnapps save

    private async Task Wo178SaveAsync(string why)
    {
        if (Interlocked.CompareExchange(ref _w178SaveBusy, 1, 0) != 0)
        {
            Console.WriteLine($"MP-W178 save why={why} result=busy -- a save is already waiting for its file");
            await ExecLuaAsync($"if KCD2MP_W178SaveDone then KCD2MP_W178SaveDone(\"busy\", \"\", \"{why}\") end");
            return;
        }
        try
        {
            string? dir = _savesDir ??= ResolveSavesDir();
            DateTime since = DateTime.UtcNow.AddSeconds(-1);
            byte[] w = Encoding.ASCII.GetBytes(why);
            var a = new byte[1 + w.Length];
            a[0] = (byte)w.Length;
            w.CopyTo(a, 1);
            (bool Ok, byte Reason, byte[] Payload)? r = null;
            try { r = await _combat.Wo163Async(Protocol.Wo178OpSaveManual, a); } catch (Exception ex) { Console.WriteLine($"MP-W178 save why={why} not asked ({ex.GetType().Name})"); }
            byte res = r is { Ok: true, Payload.Length: >= 1 } x ? x.Payload[0] : (byte)255;
            string word = Wo178Rules.SaveWord(res);
            Console.WriteLine($"MP-W178 save why={why} result={word} (the engine's ManualSave, no item used)");
            if (res != 1)
            {
                Interlocked.Increment(ref _w178SaveRefused);
                await ExecLuaAsync($"if KCD2MP_W178SaveDone then KCD2MP_W178SaveDone(\"{word}\", \"\", \"{why}\") end");
                return;
            }
            string? file = dir is null ? null : await Wo178WaitNewSaveAsync(dir, since);
            if (file is null)
            {
                Interlocked.Increment(ref _w178NoFile);
                Console.WriteLine($"MP-W178 save why={why} result=no-file -- no new verified save within {Wo178Rules.SaveFileWaitMs / 1000} s");
                await ExecLuaAsync($"if KCD2MP_W178SaveDone then KCD2MP_W178SaveDone(\"no-file\", \"\", \"{why}\") end");
                return;
            }
            Interlocked.Increment(ref _w178Saves);
            Console.WriteLine($"MP-W178 save why={why} file={file} verified");
            await ExecLuaAsync($"if KCD2MP_W178SaveDone then KCD2MP_W178SaveDone(\"saved\", \"{EscapeLua(file)}\", \"{why}\") end");
        }
        finally { Volatile.Write(ref _w178SaveBusy, 0); }
    }

    /// <summary>The first engine-named save written after <paramref name="since"/> that verifies, as playlineN/x.whs; null when none in time.</summary>
    private static async Task<string?> Wo178WaitNewSaveAsync(string dir, DateTime since)
    {
        long until = Environment.TickCount64 + Wo178Rules.SaveFileWaitMs;
        while (Environment.TickCount64 < until)
        {
            await Task.Delay(500);
            string? newest = null;
            DateTime newestAt = since;
            try
            {
                foreach (string f in Directory.EnumerateFiles(dir, "*.whs", SearchOption.AllDirectories))
                {
                    if (WorldSaved.ParsePath(f) is null) continue;
                    DateTime t = File.GetLastWriteTimeUtc(f);
                    if (t > newestAt) { newestAt = t; newest = f; }
                }
            }
            catch { continue; }
            if (newest is null) continue;
            if ((DateTime.UtcNow - newestAt).TotalMilliseconds < 600) continue;   // still being written
            var v = WhsSave.VerifyFile(newest);
            if (v.Ok) return SaveDisplay(newest);
        }
        return null;
    }

    // ================================================================ 0.c: the backup to pick

    /// <summary>The newest launcher backup (WO-175 S2, made before this session started) is marked as the one RESTORE SAVES offers first.</summary>
    private void Wo178MarkPick(string why)
    {
        try
        {
            string root = Environment.GetEnvironmentVariable("KCDMP_BACKUP_DIR") is { Length: > 0 } d
                ? d
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KingdomComeTogether", "save-backups");
            if (!Directory.Exists(root)) { Console.WriteLine("MP-W178 stuck: no launcher backups folder -- nothing to mark"); return; }
            string? stamp = Directory.EnumerateDirectories(root)
                .Select(Path.GetFileName)
                .Where(n => n is { Length: 15 } && n[8] == '-' && n.Remove(8, 1).All(char.IsAsciiDigit) && File.Exists(Path.Combine(root, n, "manifest.sha256")))
                .OrderByDescending(n => n, StringComparer.Ordinal)
                .FirstOrDefault();
            if (stamp is null) { Console.WriteLine("MP-W178 stuck: no finished backup to mark"); return; }
            File.WriteAllText(Path.Combine(root, Wo178Rules.PickFileName), Wo178Rules.PickText(stamp, why, DateTime.UtcNow));
            Interlocked.Increment(ref _w178Picks);
            Console.WriteLine($"MP-W178 stuck: backup {stamp} marked as the one RESTORE SAVES offers ({why})");
        }
        catch (Exception ex) { Console.WriteLine($"MP-W178 stuck: the backup could not be marked ({ex.GetType().Name})"); }
    }

    // ================================================================ C1: the copies the joiner may lock on to

    /// <summary>A host NPC's combat event on the joiner: its copy is offered (or withdrawn) to the DLL's hostility answer.</summary>
    private void Wo178OfferFor(NpcCombatEvent ev, uint eid, bool down)
    {
        if (eid == 0 || !Wo131JoinerActive) return;
        byte why = Wo178Rules.OfferWhy(ev.InCombat, ev.Target, ev.TargetGhost, _myGhostId);
        byte flags = (byte)(down ? 1 : 0);
        long now = Environment.TickCount64;
        if (_w178Offered.TryGetValue(eid, out var last) && last.Why == why && last.Flags == flags && now - last.At < Wo178Rules.OfferRefreshMs) return;
        if (why == Wo178Rules.WhyNone && !_w178Offered.ContainsKey(eid)) return;
        if (why == Wo178Rules.WhyNone) { _w178Offered.TryRemove(eid, out _); Interlocked.Increment(ref _w178Withdrawn); }
        else
        {
            bool fresh = !_w178Offered.ContainsKey(eid) || last.Why != why;
            _w178Offered[eid] = (why, flags, now);
            Interlocked.Increment(ref _w178Offers);
            if (fresh) Console.WriteLine($"MP-W178 offer npc={ev.Name} eid=0x{eid:X} why={Wo178Rules.WhyName(why)}{(down ? " (knocked out: excluded)" : "")}");
        }
        var a = new byte[6];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(a, eid);
        a[4] = why;
        a[5] = flags;
        _ = Task.Run(async () => { try { await _combat.Wo163Async(Protocol.Wo178OpOffer, a); } catch { } });
    }

    // ================================================================ C4: a held blow

    /// <summary>The DLL held a joiner's blow for the figure's strike window: its answer is fetched in the background and the outcome goes to
    /// the joiner as for any real hit.</summary>
    private void Wo178FinishHeld(uint id, byte source, string npc, float stamina, float health, Wo166Rules.Strike? f)
    {
        Interlocked.Increment(ref _w178Held);
        _ = Task.Run(async () =>
        {
            long until = Environment.TickCount64 + Wo178Rules.HeldGiveUpMs;
            var a = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(a, id);
            while (Environment.TickCount64 < until)
            {
                await Task.Delay(Wo178Rules.HeldPollMs);
                (bool Ok, byte Reason, byte[] Payload)? r = null;
                try { r = await _combat.Wo163Async(Protocol.Wo178OpHeldResult, a); } catch { }
                if (r is not { Ok: true, Payload.Length: >= 34 } x || x.Payload[0] == 1) continue;
                if (x.Payload[0] != 2) break;
                var h = Wo173Hit.TryDecode(true, 0, x.Payload[1..]);
                if (h is not { } hit) break;
                Interlocked.Increment(ref _w178HeldDone);
                if (hit.ByEngine) Interlocked.Increment(ref _w173Engine);
                else if (hit.AppliedAny) Interlocked.Increment(ref _w173Raw);
                else Interlocked.Increment(ref _w173Failed);
                var verdict = hit.ByEngine ? Wo173Rules.VerdictOf(hit.Outcome) : HitVerdict.None;
                if ((int)verdict is > 0 and < 7) Interlocked.Increment(ref _w173Verdicts[(int)verdict]);
                Console.WriteLine(FormattableString.Invariant(
                    $"MP-W173 hit npc={npc} by_ghost={source} applied={(hit.ByEngine ? "engine" : hit.AppliedAny ? "raw" : "none")} verdict={(verdict == HitVerdict.None ? "-" : HitVerdictMsg.VerdictName(verdict))} reaction={Wo173Rules.ReactName(hit.React)} joiner_said=st{stamina:F1}/hp{health:F1} raw_applied=st{hit.StApplied:F1}/hp{hit.HpApplied:F1} row={(f is null ? "none" : $"type{f.Value.Type}/zone{f.Value.Zone}")} fallback={Wo173Rules.FallbackName(hit.Fallback)} seq={hit.Seq} held=yes"));
                if (W164Text.IsNpcName(npc))
                {
                    Interlocked.Increment(ref _w173OutSent);
                    await W164SendAsync(source, Protocol.W164RealHit, W164Text.RealHit(npc, verdict, hit.React, hit.ByEngine, health, stamina));
                }
                return;
            }
            Interlocked.Increment(ref _w178HeldLost);
            Console.WriteLine($"MP-W178 held blow id={id} npc={npc}: no answer from the DLL in {Wo178Rules.HeldGiveUpMs} ms (the joiner hears nothing for this blow)");
        });
    }

    // ================================================================ the quicksave key (settings.json QuickSaveKey)

    private void Wo178SaveKey(string key, System.Text.Json.Nodes.JsonNode? value)
    {
        var r = SettingsJson.Update(W154mLauncherSettings, new[] { SettingsJson.Edit.Set(Wo178Rules.QuickSaveKeySetting, value) }, createIfMissing: false);
        bool ok = r.Outcome is SettingsJson.Outcome.Written or SettingsJson.Outcome.Unchanged;
        if (ok) _w178QsStoredKey = key;
        Console.WriteLine($"MP-MENU-SAVE QuickSaveKey={key} -> {Wo154MenuRules.LauncherSettingsFile} {r.Outcome.ToString().ToLowerInvariant()}"
                          + (ok ? " -- the keys pak binds it at the next game start" : " -- not saved"));
        _ = ExecLuaAsync($"if KCD2MP_W178QsKeys then KCD2MP_W178QsKeys(\"{_w178QsActiveKey}\", \"{_w178QsStoredKey}\", \"{(ok ? "stored" : "not-saved")}\") end");
    }

    /// <summary>The quicksave key bound in this game run (read back from the keys pak) and the one stored for the next start.</summary>
    private void Wo178ReadKeys()
    {
        string menu = _w154mActiveKey;
        string stored = Wo178Rules.ResolveQuickSaveKey(KeybindPak.ReadSetting(W154mLauncherSettings, Wo178Rules.QuickSaveKeySetting), menu, out _);
        string? bound = null;
        try
        {
            if (KcdLogLocator.Find() is string log && Path.GetDirectoryName(log) is string root)
                bound = KeybindPak.BoundQuickSaveKeyInPak(Path.Combine(root, "Mods", "kdcmp", "Data", KeybindPak.PakFileName));
        }
        catch { }
        _w178QsStoredKey = stored;
        _w178QsActiveKey = bound is not null && Array.IndexOf(Wo178Rules.QuickSaveKeys, bound) >= 0 ? bound : stored;
        _ = ExecLuaAsync($"if KCD2MP_W178QsKeys then KCD2MP_W178QsKeys(\"{_w178QsActiveKey}\", \"{_w178QsStoredKey}\", \"\") end");
    }
}
