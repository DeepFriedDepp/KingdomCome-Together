// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-210 F (docs/WO-210-findings.md): the shared dialogue, complete. F.2a: the starter's cursor row goes to each listener (ConvHover, conv
// kind 14 on the conv channel, at most 10 a second) and is drawn there as a soft mark; F.2b: the pick shows on the listener's rows for the
// second before its line plays (the line waits here, in order, at most 1.4 s); F.1's wheel and mouse confirm and F.2c's mark over a row's own
// icon are the Lua's (kdcmp.lua's W204 block).
public partial class GameBridge
{
    // ---------------------------------------------------------------- F.2a the starter
    private int _w210HoverRow = int.MinValue, _w210HoverCid, _w210HoverDec, _w210HoverPending;
    private long _w210HoverSentMs, _w210HoverSeq, _w210HoversOut, _w210HoversIn;

    /// <summary>The starter's 4 Hz options poll read the game's highlighted row (op 91 "current"): a new row goes to the listeners.</summary>
    private void Wo210NoteCurrent(int cid, int dec, int cur, bool open)
    {
        if (!open || !W210("conv_hover")) return;
        if (cur == _w210HoverRow && cid == _w210HoverCid && dec == _w210HoverDec) return;
        _w210HoverRow = cur; _w210HoverCid = cid; _w210HoverDec = dec;
        if (Interlocked.CompareExchange(ref _w210HoverPending, 1, 0) != 0) return;   // a send is already on its way: it takes the newest row
        _ = Task.Run(async () =>
        {
            try
            {
                long wait = _w210HoverSentMs + Wo210Rules.HoverMinGapMs - Environment.TickCount64;
                if (wait > 0) await Task.Delay((int)wait);
                Interlocked.Exchange(ref _w210HoverPending, 0);
                W203Talk? talk;
                lock (_w3cLock) talk = _w3cTalk is { } t && t.Listeners.Count > 0 ? t : null;
                if (talk is null || talk.Cid != _w210HoverCid) return;
                byte[] to;
                lock (_w3cLock) to = [.. talk.Listeners];
                long seq = Interlocked.Increment(ref _w210HoverSeq);
                string text = Wo210Rules.HoverText(_w210HoverCid, _w210HoverDec, _w210HoverRow, seq);
                _w210HoverSentMs = Environment.TickCount64;
                foreach (byte p in to) await Wo203SendConvAsync(p, Protocol.ConvHover, text);
                long n = Interlocked.Increment(ref _w210HoversOut);
                if (n <= 40 || n % 100 == 0) Console.WriteLine($"WO210-HOVER sent cid={_w210HoverCid} dec={_w210HoverDec} row={_w210HoverRow} -> {to.Length} listener(s) [n={n}]");
            }
            catch (Exception ex) { Interlocked.Exchange(ref _w210HoverPending, 0); Console.WriteLine($"MP-W210 hover not sent: {ex.GetType().Name}"); }
        });
    }

    /// <summary>The engine's "Topic index 'N' activated." on the starter's machine: the cursor moved -- the options poll runs now, not at
    /// its next quarter second (the listener's mark within 200 ms of the starter's move).</summary>
    private void Wo210OnTopicActivated()
    {
        if (!W210("conv_hover")) return;
        Wo204ConvPollTick();
    }

    // ---------------------------------------------------------------- F.2a the listener
    private void Wo210OnConvHover(byte src, int cid, int dec, int row, long seq)
    {
        Interlocked.Increment(ref _w210HoversIn);
        if (!W210("conv_hover")) return;
        if (_w210HoverSeqIn.TryGetValue(src, out long last) && seq <= last) return;   // an older one overtaken
        _w210HoverSeqIn[src] = seq;
        _ = ExecLuaAsync(string.Create(CultureInfo.InvariantCulture, $"if KCD2MP_W210Hover then KCD2MP_W210Hover({src}, {cid}, {dec}, {row}) end"));
    }
    private readonly System.Collections.Concurrent.ConcurrentDictionary<byte, long> _w210HoverSeqIn = new();

    // ---------------------------------------------------------------- F.2b the pick's second, on the listener
    private (byte Src, int Cid, int Dec, long At)? _w210Open;    // a decision shown, no pick yet
    private (byte Src, int Cid, long At)? _w210Pick;             // the last pick and when it came
    private readonly SemaphoreSlim _w210LineGate = new(1, 1);
    private long _w210LinesHeld, _w210LineHoldMsSum;

    private void Wo210NoteOptions(byte src, int cid, int dec) => _w210Open = (src, cid, dec, Environment.TickCount64);
    private void Wo210NotePick(byte src, int cid, int dec) { _w210Open = null; _w210Pick = (src, cid, Environment.TickCount64); }

    /// <summary>A line of the starter's talk: shown and voiced here in order; with mp_conv_pick_hold, a line that comes while a decision is
    /// up waits up to 400 ms for its pick (the field's line came 70 ms before the pick), and every line within the pick's second waits for
    /// the second to end -- nothing waits longer than 1.4 s.</summary>
    private async Task Wo210DeliverLineAsync(byte src, Wo204Rules.ConvLineMsg line)
    {
        await _w210LineGate.WaitAsync();
        try
        {
            long t0 = Environment.TickCount64;
            if (W210("conv_pick_hold") && !line.Earlier)
            {
                while (_w210Open is { } o && o.Src == src && o.Cid == line.Cid && Environment.TickCount64 - t0 < Wo210Rules.PickWaitMs) await Task.Delay(20);
                if (_w210Pick is { } p && p.Src == src && p.Cid == line.Cid)
                {
                    long until = Wo210Rules.LineReleaseMs(p.At, t0);
                    long wait = until - Environment.TickCount64;
                    if (wait > 0) await Task.Delay((int)wait);
                }
                long held = Environment.TickCount64 - t0;
                if (held > 30)
                {
                    long k = Interlocked.Increment(ref _w210LinesHeld);
                    Interlocked.Add(ref _w210LineHoldMsSum, held);
                    if (k <= 10) Console.WriteLine($"WO210-LINE held ms={held} cid={line.Cid} (the pick first) [n={k}]");
                }
            }
            await ExecLuaAsync(string.Create(CultureInfo.InvariantCulture,
                $"if KCD2MP_W204Line then KCD2MP_W204Line({src}, {line.Cid}, \"{EscapeLua(line.Name)}\", {(line.IsPlayer ? "true" : "false")}, \"{EscapeLua(line.StringName)}\", {line.DurMs}, {(line.Earlier ? "true" : "false")}) end"));
            if (!line.Earlier && W204("conv_voice")) _ = Wo204VoiceAsync(src, line);
        }
        finally { _w210LineGate.Release(); }
    }

    private string Wo210DialogueStats() =>
        $"hovers_out={Interlocked.Read(ref _w210HoversOut)} hovers_in={Interlocked.Read(ref _w210HoversIn)} lines_held={Interlocked.Read(ref _w210LinesHeld)} line_hold_ms={Interlocked.Read(ref _w210LineHoldMsSum)}";
}
