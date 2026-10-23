// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-201 Act V F1, the line capture: the DLL captures each line the engine voices (op 83: the voice file the engine resolved, its
// speaker, its length, the speaker's distance to the partner's figure). WO-202 V.1 reads it (a shared cutscene never jumps through a
// line that is playing). WO-204 III.2 retired the overheard broadcast that sent every line near the partner to him (W164 SpeechLine,
// kind 21): it showed the raw line keys under every character ("text under characters all messed up"). A 0.62.5 partner's lines are
// counted and dropped. A joined conversation's lines travel on the Conv channel (WO-204 III.4).   WO201-SPEECH ...
public partial class GameBridge
{
    private bool _w201SpeechRefused;
    private long _w201SpeechCaptured, _w201SpeechRetiredIn;

    private void Wo201SpeechTick()
    {
        if (!_w201Connected || _w201SpeechRefused) return;
        if (!(W137Host || W137Joiner)) return;
        uint fig = 0;
        // the figure only gives each line its distance; WO-204 III.4's conversation lines need none (0 = no figure: the DLL reports none)
        foreach (var kv in _ghostEntityIds) { if (kv.Value != 0) { fig = kv.Value; break; } }
        _ = Wo201SpeechPollAsync(fig);
    }

    private async Task Wo201SpeechPollAsync(uint fig)
    {
        var a = new byte[5];
        a[0] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(1), fig);
        var r = await _combat.Wo163Async(Protocol.Wo201OpSpeechPoll, a);
        if (r is not { Ok: true } rr)
        {
            _w201SpeechRefused = true;
            Console.WriteLine($"WO201-SPEECH poll refused by the DLL ({(r is { } x ? x.Reason : 255)}) -- the partner hears nothing of this machine's conversations this session");
            return;
        }
        foreach (var l in Wo201Rules.ParseSpeechPoll(rr.Payload))
        {
            Wo202NoteVoice(l.AgeMs, l.Dur);   // WO-202 V.1: a shared cutscene never jumps through a line that is playing
            Interlocked.Increment(ref _w201SpeechCaptured);
            Wo204NoteSpokenLine(l);           // WO-204 III.4: a joined conversation's lines, to its listener
        }
    }

    /// <summary>A 0.62.5 partner's overheard line (W164 SpeechLine): the broadcast is retired (WO-204 III.2) -- counted, dropped.</summary>
    private Task Wo201OnSpeechLineAsync(byte src, string text)
    {
        if (Interlocked.Increment(ref _w201SpeechRetiredIn) == 1)
            Console.WriteLine($"WO201-SPEECH a partner (ghost {src}) still sends overheard lines (an older release): dropped -- the broadcast is retired");
        return Task.CompletedTask;
    }

    private string Wo201SpeechStats() =>
        $"speech_captured={Interlocked.Read(ref _w201SpeechCaptured)} speech_retired_in={Interlocked.Read(ref _w201SpeechRetiredIn)}";
}
