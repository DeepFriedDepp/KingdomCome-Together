// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace KcdMp.Client;

// WO-202 II.4 quest_fail_trace (host): every quest step that failed, was aborted or cancelled, or set a "player bailed" value, as one
// plain line with its cause: the DLL names the node whose pulse made it and how long that node had run (op 89); this half adds what the
// mod did in that window (a join's hold, a reload, a load) and where both players were.   WO202-QUESTFAIL ...
public partial class GameBridge
{
    private readonly ConcurrentQueue<(DateTime At, string What)> _w202Events = new();
    private uint _w202FailSeq;
    private long _w202FailsLogged;

    /// <summary>A session event a quest failure's window may contain (a join's hold, a reload, a load).</summary>
    private void Wo202NoteEvent(string what)
    {
        _w202Events.Enqueue((DateTime.Now, what));
        while (_w202Events.Count > 200) _w202Events.TryDequeue(out _);
    }

    private async Task Wo202LoopAsync(CancellationToken ct)
    {
        long n = 0;
        while (!ct.IsCancellationRequested && _w202Connected)
        {
            try { await Task.Delay(2000, ct); } catch { break; }
            n++;
            try
            {
                if (_isDamageAuthority && W202("quest_fail_trace")) await Wo202FailPollAsync(ct);
                if (n % 3 == 1) await Wo202KeysTickAsync();   // V.3: every 6 s
                if (n % 30 == 7) Wo202Status();
            }
            catch (Exception ex) { Console.WriteLine($"MP-W202 loop: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    private async Task Wo202FailPollAsync(CancellationToken ct)
    {
        for (int i = 0; i < 8; i++)
        {
            var r = await _combat.Wo163Async(KcdMp.Wire.Protocol.Wo202OpFailPoll, BitConverter.GetBytes(_w202FailSeq), ct);
            if (r is not { Ok: true } x || x.Payload.Length < 1) return;
            if (!Wo202Rules.TryParseFail(x.Payload, out var f, out bool more)) return;
            _w202FailSeq = f.Seq;
            Wo202LogFail(f);
            if (!more) return;
        }
    }

    private void Wo202LogFail(Wo202Rules.QuestFail f)
    {
        Interlocked.Increment(ref _w202FailsLogged);
        DateTime at = DateTime.Now.AddMilliseconds(-f.AgeMs);
        DateTime from = f.RanMs >= 0 ? at.AddMilliseconds(-f.RanMs) : at.AddMinutes(-10);
        var inWindow = _w202Events.Where(e => e.At >= from && e.At <= at).Select(e => $"{e.At:HH:mm:ss} {e.What}").ToList();
        var me = (X: _lastX, Y: _lastY, Z: _lastZ);
        var others = _ghostLastPos.Select(kv => $"partner {kv.Key} at ({kv.Value.X:F0}, {kv.Value.Y:F0}), {Math.Sqrt(Sq(kv.Value.X - me.X) + Sq(kv.Value.Y - me.Y)):F0} m from the host").ToList();
        string plain = Wo202Rules.FailInWords(f, inWindow.Count);
        string head = string.Create(CultureInfo.InvariantCulture,
            $"WO202-QUESTFAIL {at:HH:mm:ss.fff} {f.Path} {f.Port} -> {f.New} (from {f.Old}){(f.Cascade ? " cascade" : " root")} source={Wo202Rules.SourceKind(f.Class)} {f.SourcePath} :: {f.SourcePort} ran_s={(f.RanMs >= 0 ? (f.RanMs / 1000.0).ToString("F1", CultureInfo.InvariantCulture) : "?")}");
        string where = string.Create(CultureInfo.InvariantCulture, $"host at ({me.X:F0}, {me.Y:F0}); {(others.Count > 0 ? string.Join("; ", others) : "no partner")}");
        Console.WriteLine($"{head} {where} | window: {(inWindow.Count > 0 ? string.Join(", ", inWindow) : "nothing the mod did (no join, reload or load)")} | {plain}");
        static double Sq(double v) => v * v;
    }
}
