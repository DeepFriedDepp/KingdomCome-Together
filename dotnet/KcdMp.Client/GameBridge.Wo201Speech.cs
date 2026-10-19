// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-201 Act V F1 speech_share: when either player talks to someone, the other hears it if he is close, from the right bodies; the
// host's NPC chatter reaches the joiner the same way. The DLL captures each line the engine voices (op 83: the voice file the engine
// resolved, its speaker, its length, the speaker's distance to the partner's figure); this machine sends the partner only what he
// can hear (mp_speech_radius, default 20 m) as a W164 SpeechLine (kind 21): ids and the file name, plus the line's text only inside
// the subtitle radius (mp_speech_subtitles_m, default 8 m), the whole line capped under 200 bytes. The partner's machine plays it on
// the right body (op 84: the body's own audio object, positional): an NPC's line from that NPC (its copy, or the host's own NPC), the
// talker's Henry lines from the talker's figure.
//   No doubles, no loops: a played line never reaches the engine's speech call (never captured back); a line is played once per
//   machine; a line older than its own length when it arrives is dropped. The joiner sends his own conversation's lines only (a
//   copy's chatter is the host's to say).
//   WO201-SPEECH out|in|play|drop ...
public partial class GameBridge
{
    private float _w201SpeechRadius = 20f, _w201SubtitleRadius = 8f;
    private bool _w201SpeechRefused;
    private readonly ConcurrentDictionary<string, long> _w201SpeechSeen = new(StringComparer.Ordinal);
    private long _w201SpeechOut, _w201SpeechIn, _w201SpeechPlayed, _w201SpeechDropped, _w201SpeechNoBody;

    private void Wo201SpeechTick()
    {
        if (!_w201Connected || _w201SpeechRefused || !W201("speech_share")) return;
        if (!(W137Host || W137Joiner)) return;
        uint fig = 0;
        foreach (var kv in _ghostEntityIds) { if (kv.Value != 0) { fig = kv.Value; break; } }
        if (fig == 0) return;
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
            bool isMe = l.Eid == 0x7777;
            if (isMe && l.FromDialogue) Wo201VoteNoteOwnLine(l.Text);   // F2: what this player chose, as he says it
            if (!Wo201Rules.SpeechSendable(W137Host, isMe, l.FromDialogue, l.Dist, _w201SpeechRadius)) continue;
            string speaker = isMe ? "@me" : l.Name;
            if (speaker.Length == 0) continue;
            string text = l.Dist >= 0 && l.Dist <= _w201SubtitleRadius ? l.Text : "";
            string wire = Wo201Rules.SpeechWire(speaker, (int)(l.Dur * 1000), l.AgeMs, l.Path, text);
            foreach (byte g in Wo134Peers()) await W164SendAsync(g, Protocol.W164SpeechLine, wire);
            long n = Interlocked.Increment(ref _w201SpeechOut);
            if (n <= 20 || n % 50 == 0)
                Console.WriteLine(FormattableString.Invariant($"WO201-SPEECH out speaker={speaker} dist={l.Dist:F1} dur={l.Dur:F1} file={Path.GetFileName(l.Path)} text={(text.Length > 0 ? "yes" : "no")} bytes={Encoding.UTF8.GetByteCount(wire)} [n={n}]"));
        }
    }

    private async Task Wo201OnSpeechLineAsync(byte src, string text)
    {
        if (!W201("speech_share")) return;
        if (!Wo201Rules.TryParseSpeechWire(text, out var w)) return;
        Interlocked.Increment(ref _w201SpeechIn);
        long now = Environment.TickCount64;
        if (w.AgeMs > w.DurMs) { Interlocked.Increment(ref _w201SpeechDropped); return; }   // older than its own length: never late
        string key = src + "|" + w.Speaker + "|" + w.Path;
        if (_w201SpeechSeen.TryGetValue(key, out long at) && now - at < Math.Max(2000, w.DurMs)) return;   // once per machine
        _w201SpeechSeen[key] = now;
        if (_w201SpeechSeen.Count > 200) foreach (var kv in _w201SpeechSeen) if (now - kv.Value > 60000) _w201SpeechSeen.TryRemove(kv.Key, out _);
        uint eid = 0; string body = w.Speaker;
        if (w.Speaker == "@me")
        {
            if (!_ghostEntityIds.TryGetValue(src.ToString(CultureInfo.InvariantCulture), out eid) || eid == 0) { Interlocked.Increment(ref _w201SpeechNoBody); return; }
            body = "kcd2mp_" + src.ToString(CultureInfo.InvariantCulture);
        }
        await ExecLuaAsync($"if KCD2MP_W201SpeechPrep then KCD2MP_W201SpeechPrep(\"{EscapeLua(body)}\") end");
        var pb = Encoding.UTF8.GetBytes(w.Path);
        var nb = eid == 0 ? Encoding.UTF8.GetBytes(body) : Array.Empty<byte>();
        if (pb.Length > 190 || nb.Length > 63) return;
        var a = new byte[4 + 1 + pb.Length + (eid == 0 ? 1 + nb.Length : 0)];
        BinaryPrimitives.WriteUInt32LittleEndian(a, eid);
        a[4] = (byte)pb.Length; pb.CopyTo(a, 5);
        if (eid == 0) { a[5 + pb.Length] = (byte)nb.Length; nb.CopyTo(a, 6 + pb.Length); }
        var r = await _combat.Wo163Async(Protocol.Wo201OpSpeechPlay, a);
        bool ok = r is { Ok: true } rr && rr.Payload.Length >= 1 && rr.Payload[0] == 1;
        if (ok) Interlocked.Increment(ref _w201SpeechPlayed); else Interlocked.Increment(ref _w201SpeechNoBody);
        long n = Interlocked.Read(ref _w201SpeechIn);
        if (n <= 20 || n % 50 == 0)
            Console.WriteLine($"WO201-SPEECH in from={src} speaker={w.Speaker} body={body} file={Path.GetFileName(w.Path)} -> {(ok ? "played" : "NOT played (no body or no audio object here)")} [n={n}]");
        if (ok && w.Text.Length > 0)
            await ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W201Subtitle then KCD2MP_W201Subtitle(\"{EscapeLua(w.Text)}\", {(w.Speaker == "@me" ? "true" : "false")}, {w.DurMs / 1000.0:F1}) end"));
    }

    private void Wo201SpeechCfg(string name, string? arg)
    {
        if (!float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) || v < 0 || v > 200) return;
        if (name == "w201_speech_radius") _w201SpeechRadius = v; else _w201SubtitleRadius = v;
        Console.WriteLine(FormattableString.Invariant($"MP-W201 speech radius={_w201SpeechRadius:F0} m subtitles={_w201SubtitleRadius:F0} m"));
    }

    private string Wo201SpeechStats() =>
        $"speech_out={Interlocked.Read(ref _w201SpeechOut)} speech_in={Interlocked.Read(ref _w201SpeechIn)} speech_played={Interlocked.Read(ref _w201SpeechPlayed)} speech_late={Interlocked.Read(ref _w201SpeechDropped)} speech_no_body={Interlocked.Read(ref _w201SpeechNoBody)}";
}
