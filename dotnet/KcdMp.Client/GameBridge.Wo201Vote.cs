// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-201 Act V F2 dialogue_vote: while one player chooses in a conversation, the other can say what he would pick; the speaker still
// decides. The talker's machine reads the options its own dialogue HUD shows (the DLL's op 85: the rows' texts, never the choice) and,
// when the partner is within mp_vote_radius (default 30 m; 0 = anywhere), sends them (W164 VoteOptions, kind 22). The partner's game
// shows them in a small numbered panel; F2 / F4 / F5 / F6 (the mod's own interaction-map keys, live for the vote only while the panel
// is shown and no dice game is open) vote, and a vote can change (VoteCast, kind 23). The speaker's screen shows the partner's pick
// beside his options. The vote never holds the conversation; a timed decision keeps its own timer. When the decision closes, both
// screens say what was chosen (VoteClose, kind 24: the speaker's next own line, as the engine voiced it).   WO201-VOTE ...
public partial class GameBridge
{
    private float _w201VoteRadius = 30f;
    private uint _w201VoteSeq;          // the DLL's snapshot last read
    private uint _w201VoteOpen;         // the decision shown to the partner (0: none)
    private string[] _w201VoteOptions = Array.Empty<string>();
    private long _w201VoteClosedAt; private uint _w201VoteClosedSeq;
    private (string Text, long At) _w201LastOwnLine;
    private bool _w201VoteRefused;
    private uint _w201VotePartnerSeq;   // on the partner: the decision his panel shows
    private byte _w201VoteTalker;
    private long _w201VoteOut, _w201VoteIn, _w201VoteCastsOut, _w201VoteCastsIn;

    private void Wo201VoteTick()
    {
        if (!_w201Connected || _w201VoteRefused || !W201("dialogue_vote") || !(W137Host || W137Joiner)) return;
        uint fig = 0;
        foreach (var kv in _ghostEntityIds) { if (kv.Value != 0) { fig = kv.Value; break; } }
        if (fig == 0) return;
        _ = Wo201VotePollAsync(fig);
    }

    private async Task Wo201VotePollAsync(uint fig)
    {
        var a = new byte[9];
        a[0] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(1), _w201VoteSeq);
        BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(5), fig);
        var r = await _combat.Wo163Async(Protocol.Wo201OpVotePoll, a);
        if (r is not { Ok: true } rr || rr.Payload.Length < 9)
        {
            _w201VoteRefused = true;
            Console.WriteLine($"WO201-VOTE poll refused by the DLL ({(r is { } x ? x.Reason : 255)}) -- the partner cannot vote on this machine's conversations");
            return;
        }
        long now = Environment.TickCount64;
        uint seq = BinaryPrimitives.ReadUInt32LittleEndian(rr.Payload);
        float dist = BitConverter.ToSingle(rr.Payload, 4);
        bool inRange = Wo201Rules.VoteInRange(dist, _w201VoteRadius);
        // a decision the partner sees closes when it changes, when this player walks out of range, or when the HUD clears it
        if (_w201VoteOpen != 0 && (!inRange || (seq != _w201VoteSeq && rr.Payload[8] != 255)))
        {
            _w201VoteClosedSeq = _w201VoteOpen; _w201VoteClosedAt = now; _w201VoteOpen = 0;
            if (!inRange) { await Wo201VoteSendCloseAsync(_w201VoteClosedSeq, "", "out of range"); _w201VoteClosedSeq = 0; }
        }
        if (_w201VoteClosedSeq != 0)
        {
            // the choice: this player's own next line (the engine voices the chosen option right after the pick); 2 s at most
            bool said = _w201LastOwnLine.At >= _w201VoteClosedAt && _w201LastOwnLine.Text.Length > 0;
            if (said || now - _w201VoteClosedAt > 2000)
            {
                await Wo201VoteSendCloseAsync(_w201VoteClosedSeq, said ? _w201LastOwnLine.Text : "", said ? "chosen" : "closed");
                _w201VoteClosedSeq = 0;
            }
        }
        if (rr.Payload[8] == 255) return;
        _w201VoteSeq = seq;
        var rows = Wo201Rules.ParseVoteRows(rr.Payload, 9);
        if (rows.Count == 0 || !inRange) return;
        _w201VoteOpen = seq;
        _w201VoteOptions = rows.ToArray();
        string wire = Wo201Rules.VoteOptionsWire(seq, rows);
        foreach (byte g in Wo134Peers()) await W164SendAsync(g, Protocol.W164VoteOptions, wire);
        long n = Interlocked.Increment(ref _w201VoteOut);
        Console.WriteLine(FormattableString.Invariant($"WO201-VOTE out decision={seq} options={rows.Count} partner={dist:F1} m -- the partner can vote; this player decides [n={n}]"));
        await ExecLuaAsync("if KCD2MP_W201VoteSpeakerOpen then KCD2MP_W201VoteSpeakerOpen() end");
    }

    private async Task Wo201VoteSendCloseAsync(uint seq, string chosen, string why)
    {
        string wire = Wo201Rules.VoteCloseWire(seq, chosen);
        foreach (byte g in Wo134Peers()) await W164SendAsync(g, Protocol.W164VoteClose, wire);
        Console.WriteLine($"WO201-VOTE close decision={seq} ({why})");
        string me = string.IsNullOrWhiteSpace(config.PlayerName) ? "You" : config.PlayerName!;
        await ExecLuaAsync($"if KCD2MP_W201VoteChosen then KCD2MP_W201VoteChosen(\"{EscapeLua(me)}\", \"{EscapeLua(chosen)}\", false) end");
    }

    /// <summary>The speech poll's own lines (Wo201SpeechPollAsync): the last line this player's Henry said in a dialogue.</summary>
    private void Wo201VoteNoteOwnLine(string text) { if (text.Length > 0) _w201LastOwnLine = (text, Environment.TickCount64); }

    // ---- the partner ----
    private async Task Wo201OnVoteOptionsAsync(byte src, string text)
    {
        if (!W201("dialogue_vote") || !Wo201Rules.TryParseVoteOptions(text, out uint seq, out var opts)) return;
        Interlocked.Increment(ref _w201VoteIn);
        _w201VotePartnerSeq = seq; _w201VoteTalker = src;
        string who = Wo201PartnerName(src);
        var sb = new System.Text.StringBuilder();
        sb.Append("if KCD2MP_W201VoteShow then KCD2MP_W201VoteShow(").Append(seq.ToString(CultureInfo.InvariantCulture)).Append(", \"").Append(EscapeLua(who)).Append("\", {");
        foreach (var o in opts) sb.Append('"').Append(EscapeLua(o)).Append("\",");
        sb.Append("}) end");
        await ExecLuaAsync(sb.ToString());
    }

    private async Task Wo201OnVoteCloseAsync(byte src, string text)
    {
        if (!Wo201Rules.TryParseVoteClose(text, out uint seq, out string chosen)) return;
        if (seq != _w201VotePartnerSeq) return;
        _w201VotePartnerSeq = 0;
        await ExecLuaAsync($"if KCD2MP_W201VoteChosen then KCD2MP_W201VoteChosen(\"{EscapeLua(Wo201PartnerName(src))}\", \"{EscapeLua(chosen)}\", true) end");
    }

    /// <summary>The Lua's w201_vote event: "seq idx" -- this player's vote on the partner's decision.</summary>
    private async Task Wo201OnVoteKeyAsync(string? arg)
    {
        var f = (arg ?? "").Split(' ');
        if (f.Length != 2 || !uint.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint seq) || seq != _w201VotePartnerSeq) return;
        if (!int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx) || idx < 1 || idx > 8) return;
        string me = string.IsNullOrWhiteSpace(config.PlayerName) ? "Your partner" : config.PlayerName!;
        await W164SendAsync(_w201VoteTalker, Protocol.W164VoteCast, Wo201Rules.VoteCastWire(seq, idx, me));
        Interlocked.Increment(ref _w201VoteCastsOut);
        Console.WriteLine($"WO201-VOTE cast decision={seq} option={idx} -> the talker's screen");
    }

    private async Task Wo201OnVoteCastAsync(byte src, string text)
    {
        if (!Wo201Rules.TryParseVoteCast(text, out uint seq, out int idx, out string voter) || seq != _w201VoteOpen) return;
        if (idx > _w201VoteOptions.Length) return;
        Interlocked.Increment(ref _w201VoteCastsIn);
        await ExecLuaAsync($"if KCD2MP_W201VoteMark then KCD2MP_W201VoteMark({idx}, \"{EscapeLua(voter)}\", \"{EscapeLua(_w201VoteOptions[idx - 1])}\") end");
        Console.WriteLine($"WO201-VOTE in decision={seq} option={idx} from={src} -- shown beside this player's options (he decides)");
    }

    private string Wo201PartnerName(byte src) => "Your partner";

    private void Wo201VoteCfg(string? arg)
    {
        if (!float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) || v < 0 || v > 500) return;
        _w201VoteRadius = v;
        Console.WriteLine(FormattableString.Invariant($"MP-W201 vote radius={(v == 0 ? "anywhere" : v.ToString("F0", CultureInfo.InvariantCulture) + " m")}"));
    }

    private string Wo201VoteStats() =>
        $"vote_out={Interlocked.Read(ref _w201VoteOut)} vote_in={Interlocked.Read(ref _w201VoteIn)} casts_out={Interlocked.Read(ref _w201VoteCastsOut)} casts_in={Interlocked.Read(ref _w201VoteCastsIn)}";
}
