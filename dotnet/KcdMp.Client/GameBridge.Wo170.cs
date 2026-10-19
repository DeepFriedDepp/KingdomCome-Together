// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;

namespace KcdMp.Client;

// WO-170 (0.48.6) -- the agent's half (docs/WO-170-findings.md; the pure rules are Wo170.cs).
//   F1  mp_figure_guard: the switch the DLL acts on (Wo166OnCfg / Wo166PushConfigAsync carry it); WO170-AGGRO names why an NPC took a
//       partner's figure on (the engine's own combat shout and the NPC's last bark).
//   F2  the inbound Attack count: MP-ACTION section=inbound ... dispatch=swing-queued (GameBridge.Wo121.cs) and kinds= in the summary line.
//   F4  the interactive scenes the engine runs (WO-167 T4's counters) are told to the Lua (KCD2MP_W170Scene): a talk's end does not
//       re-pause a copy while its dialogue's scene runs.
//   WO170-AGGRO npc=<n> target=figure ghost=<id> reason=<engine meta> shout=<dialogue> last_bark=<dialogue>@<s>s   MP-WO170-STATS ...
public partial class GameBridge
{
    private volatile bool _w170FigureGuard = Wo170Rules.DefaultFigureGuard;
    private long _w170Aggro, _w170ScenePushes;
    private long _w170LastScenes = -1;

    // the last bark the engine started per NPC (the "last dialogue/bark" of an AGGRO line), and the last AGGRO said per NPC
    private readonly ConcurrentDictionary<string, (string Dialogue, long AtMs)> _w170Barks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _w170AggroAt = new(StringComparer.Ordinal);

    /// <summary>Every engine line (Wo167OnEngineLine's first call): a bark is remembered; a combat shout against a figure is an NPC's aggression.</summary>
    private void Wo170OnEngineLine(string line)
    {
        if (!line.StartsWith("New dialogue '", StringComparison.Ordinal)) return;
        try
        {
            if (Wo170Rules.AggroAgainstFigure(line) is { } a)
            {
                Wo201OnCopyShout(a.Npc);   // WO-201 B4: a copy never starts a fight by itself
                long now = Environment.TickCount64;
                // one line per NPC per 10 s (a fight's shouts repeat every few seconds)
                if (_w170AggroAt.TryGetValue(a.Npc, out long at) && now - at < 10_000) return;
                _w170AggroAt[a.Npc] = now;
                if (_w170AggroAt.Count > 256) foreach (var k in _w170AggroAt.Where(kv => now - kv.Value > 60_000).Select(kv => kv.Key).ToList()) _w170AggroAt.TryRemove(k, out _);
                Interlocked.Increment(ref _w170Aggro);
                string last = _w170Barks.TryGetValue(a.Npc, out var b) && now - b.AtMs < 120_000
                    ? FormattableString.Invariant($"{b.Dialogue}@{(now - b.AtMs) / 1000}s") : "-";
                Console.WriteLine(FormattableString.Invariant(
                    $"WO170-AGGRO npc={a.Npc} target=figure ghost={a.Ghost} reason={Wo170Rules.AggroReason(a.NpcMeta, a.FigureMeta)} shout={a.Dialogue} last_bark={last} -- the engine's own combat shout: this NPC took the figure on"));
                return;
            }
            if (Wo170Rules.ParseBark(line) is { } bark && bark.Kind != "CombatShout" && Wo170Rules.FigureGhost(bark.Speaker) is null)
            {
                _w170Barks[bark.Speaker] = (bark.Dialogue, Environment.TickCount64);
                if (_w170Barks.Count > 512)
                    foreach (var k in _w170Barks.Where(kv => Environment.TickCount64 - kv.Value.AtMs > 120_000).Select(kv => kv.Key).ToList()) _w170Barks.TryRemove(k, out _);
            }
        }
        catch (Exception ex) { Console.WriteLine($"WO170-AGGRO line not read ({ex.GetType().Name})"); }
    }

    /// <summary>F4: the Lua is told how many interactive scenes run, on a change only (EnqueueScene / EndScene / Interrupt, a load's reset).</summary>
    private void Wo170PushScenes()
    {
        long n = Wo170Rules.ScenesRunning(Interlocked.Read(ref _w167IsmEnq), Interlocked.Read(ref _w167IsmEnd));
        if (Interlocked.Exchange(ref _w170LastScenes, n) == n) return;
        Interlocked.Increment(ref _w170ScenePushes);
        _ = ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W170Scene then KCD2MP_W170Scene({n}) end"));
    }

    private string Wo170StatsText() =>
        FormattableString.Invariant(
            $"figure_guard={(_w170FigureGuard ? "on" : "off")} aggro={Interlocked.Read(ref _w170Aggro)} scenes_running={Wo170Rules.ScenesRunning(Interlocked.Read(ref _w167IsmEnq), Interlocked.Read(ref _w167IsmEnd))} scene_pushes={Interlocked.Read(ref _w170ScenePushes)}");
}
