// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-204 III.3 / III.4 (docs/WO-204-findings.md): the joined conversation's lines, options and vote, on WO-203B's session (Announce,
// Join, Snapshot, Cam, Leave, End: GameBridge.Wo203Cam.cs). The pure rules are Wo204Rules.
//   THE STARTER: every line the engine voices in his conversation (the DLL's capture, op 83, a dialogue's own line) goes to the
//   listeners as ConvLine, and the last ones again to a listener who joins late (marked earlier); his HUD's rows, copied by the DLL as
//   they go to the screen (op 91), go out as ConvOptions when a new set appears; the listener's ConvVote shows on the starter's own HUD
//   (the mod's Lua re-sends the copied rows with the vote icon in the voted row and puts the highlight back -- the HUD's live rows are
//   never written: that crashed); the engine's "Topic index 'N' selected." goes out as ConvChosen.
//   THE LISTENER: the line's subtitle through the HUD (@StringName, localized in his language) and its voice on the speaker's body (the
//   DLL's op 93, the body's own speak manager); the rows through the HUD, moved and voted with the mod's own listen actions (in the
//   game's dialog map, on the player's own dialogue keys); the chosen row, "Your pick" / "Their pick".
//   WO204-CONV ...
public partial class GameBridge
{
    private sealed class W204ConvState
    {
        public int Cid, DecId, LineId;
        public uint SnapSeq;
        public bool Open, OnRight;
        public int Current = -1;
        public List<Wo204Rules.OptRow> Rows = [];
        public readonly List<Wo204Rules.ConvLineMsg> Lines = [];
    }

    private W204ConvState? _w204Conv;          // the starter's (one conversation at a time, as WO-203B's talk)
    private int _w204OptPolling;
    private (byte Starter, int Cid, int DecId) _w204ListenDec;   // the listener: the decision his HUD shows
    private long _w204LinesOut, _w204LinesIn, _w204LinesVoiced, _w204LinesNoBody, _w204OptionsOut, _w204OptionsIn, _w204VotesOut, _w204VotesIn,
                 _w204VoteIcons, _w204ChosenOut, _w204ChosenIn, _w204ConvBad;

    private W204ConvState? W204ConvOf(int cid)
    {
        lock (_w3cLock)
        {
            if (_w3cTalk is not { } t || t.Cid != cid) return null;
            if (_w204Conv is not { } c || c.Cid != cid) _w204Conv = c = new W204ConvState { Cid = cid };
            return c;
        }
    }

    // ================================================================ the starter

    partial void Wo204ConvLineCaptured(Wo201Rules.SpeechCaptured l)
    {
        if (!l.FromDialogue || !W204("conv_lines")) return;
        W203Talk? talk;
        byte[] to;
        lock (_w3cLock) { talk = _w3cTalk; to = talk is null ? [] : [.. talk.Listeners]; }
        if (talk is null || W204ConvOf(talk.Cid) is not { } c) return;
        // the conversation's dialogue twins speak for their souls (DialogTwin_<soul>; the player's own twin is Dude's)
        string speaker = l.Name.StartsWith("DialogTwin_", StringComparison.Ordinal) ? l.Name["DialogTwin_".Length..] : l.Name;
        bool me = l.Eid == 0x7777 || speaker == "Dude";
        if (!me && !talk.Others.Contains(speaker, StringComparer.OrdinalIgnoreCase)) return;   // a line of the conversation's own speakers only
        string sn = Wo204Rules.StringNameOf(l.Path);
        if (sn.Length == 0) return;
        Wo204Rules.ConvLineMsg msg;
        lock (c)
        {
            msg = new Wo204Rules.ConvLineMsg(c.Cid, ++c.LineId, me ? "@" : speaker, me ? (config.PlayerName ?? "") : "", me, sn, l.Path, (int)(l.Dur * 1000), false);
            c.Lines.Add(msg);
            if (c.Lines.Count > Wo204Rules.ConvLinesKept) c.Lines.RemoveAt(0);
        }
        if (to.Length == 0) return;
        string text = Wo204Rules.LineText(msg, UnixMs() - l.AgeMs);
        Interlocked.Add(ref _w204LinesOut, to.Length);
        _ = Task.Run(async () => { foreach (byte p in to) await Wo203SendConvAsync(p, Protocol.ConvLine, text); });
    }

    /// <summary>WO-203B's Join was answered with the Snapshot: the lines so far (marked earlier) and the open options follow.</summary>
    private void Wo204OnConvJoined(W203Talk talk, byte listener)
    {
        if (W204ConvOf(talk.Cid) is not { } c) return;
        List<Wo204Rules.ConvLineMsg> earlier;
        string? opts = null;
        lock (c)
        {
            earlier = [.. c.Lines];
            if (c.Open && c.Rows.Count > 0) opts = Wo204Rules.OptionsText(c.Cid, c.DecId, 0, c.Rows);
        }
        _ = Task.Run(async () =>
        {
            foreach (var e in earlier) await Wo203SendConvAsync(W3cPeerOf(listener), Protocol.ConvLine, Wo204Rules.LineText(e with { Earlier = true }, 0));
            if (opts is not null) await Wo203SendConvAsync(W3cPeerOf(listener), Protocol.ConvOptions, opts);
            Console.WriteLine($"WO204-CONV partner {listener} joined conversation {talk.Cid}: {earlier.Count} earlier line(s){(opts is null ? "" : ", the open options")} sent");
        });
    }

    /// <summary>4 Hz (Wo204Tick): while someone listens, the HUD's rows as the DLL copied them -- a new set is a new decision.</summary>
    private void Wo204ConvPollTick()
    {
        W203Talk? talk;
        lock (_w3cLock) talk = _w3cTalk is { } t && t.Listeners.Count > 0 ? t : null;
        if (talk is null || !W204("conv_options") || Interlocked.CompareExchange(ref _w204OptPolling, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var r = await W3cNativeAsync(91, [91, 1]);
                if (r is not { Ok: true } x || Wo204Rules.ParseOptionsPoll(x.Payload, out int cur, out uint seq, out bool right) is not { } rows) return;
                if (W204ConvOf(talk.Cid) is not { } c) return;
                string? text = null;
                byte[] to;
                lock (c)
                {
                    c.Current = cur; c.OnRight = right;
                    if (rows.Count > 0 && seq != 0 && seq != c.SnapSeq)
                    {
                        c.SnapSeq = seq; c.Rows = rows; c.Open = true; ++c.DecId;
                        text = Wo204Rules.OptionsText(c.Cid, c.DecId, 0, rows);
                    }
                }
                lock (_w3cLock) to = [.. talk.Listeners];
                if (text is null || to.Length == 0) return;
                Interlocked.Increment(ref _w204OptionsOut);
                Console.WriteLine($"WO204-CONV options of conversation {talk.Cid} (decision {c.DecId}, {rows.Count} rows) -> {to.Length} listener(s)");
                foreach (byte p in to) await Wo203SendConvAsync(p, Protocol.ConvOptions, text);
            }
            finally { Interlocked.Exchange(ref _w204OptPolling, 0); }
        });
    }

    /// <summary>The engine's "Topic index 'N' selected." while this player's conversation shows options to a listener.</summary>
    private void Wo204ConvOnTopicSelected(int index)
    {
        W203Talk? talk;
        byte[] to;
        lock (_w3cLock) { talk = _w3cTalk; to = talk is null ? [] : [.. talk.Listeners]; }
        if (talk is null || W204ConvOf(talk.Cid) is not { } c)
        {
            Console.WriteLine(talk is null ? $"WO204-CONV topic {index} selected outside this player's shared conversation" : $"WO204-CONV topic {index} selected: conversation {talk.Cid} has no state -- no choice sent");
            return;
        }
        string text;
        lock (c)
        {
            if (!c.Open)
            {
                Console.WriteLine($"WO204-CONV topic {index} selected in conversation {c.Cid}: no open options (decision {c.DecId}) -- no choice sent");
                return;
            }
            c.Open = false;
            text = Wo204Rules.ChosenText(c.Cid, c.DecId, index);
        }
        if (to.Length == 0) { Console.WriteLine($"WO204-CONV topic {index} selected in conversation {c.Cid}: nobody listens -- no choice sent"); return; }
        Interlocked.Increment(ref _w204ChosenOut);
        _ = Task.Run(async () => { foreach (byte p in to) await Wo203SendConvAsync(p, Protocol.ConvChosen, text); });
    }

    private void Wo204ConvOnVote(byte src, int cid, int decId, int index)
    {
        if (W204ConvOf(cid) is not { } c) return;
        string rows; int cur; bool right;
        lock (c)
        {
            if (!c.Open || c.DecId != decId || index >= c.Rows.Count) return;
            rows = Wo204Rules.RowsForLua(c.Rows); cur = c.Current; right = c.OnRight;
        }
        Interlocked.Increment(ref _w204VotesIn);
        Console.WriteLine($"WO204-CONV partner {src} votes {(index < 0 ? "clear" : $"row {index}")} on decision {decId} of conversation {cid} -- shown in this player's own options (this player decides)");
        _ = Task.Run(async () =>
        {
            byte[] mark = [92, (byte)(index < 0 ? 255 : index), 0];
            await W3cNativeAsync(92, mark);
            await ExecLuaAsync($"if KCD2MP_W204VoteIcon then KCD2MP_W204VoteIcon(\"{EscapeLua(rows)}\", {index}, {cur}, {(right ? "true" : "false")}) end");
            Interlocked.Increment(ref _w204VoteIcons);
        });
    }

    /// <summary>The conversation's last listener left (his leave, his link gone, this player's disconnect): a vote icon he put in this
    /// player's own options would stay with nobody behind it -- cleared the way his Back clears it.</summary>
    private void Wo204ConvOnListenersGone(int cid)
    {
        W204ConvState? c;
        lock (_w3cLock) c = _w204Conv is { } x && x.Cid == cid ? x : null;
        if (c is null) return;
        string rows; int cur; bool right;
        lock (c)
        {
            if (!c.Open || c.Rows.Count == 0) return;
            rows = Wo204Rules.RowsForLua(c.Rows); cur = c.Current; right = c.OnRight;
        }
        Console.WriteLine($"WO204-CONV conversation {cid}: no listener left -- the vote icon cleared");
        _ = Task.Run(async () =>
        {
            await W3cNativeAsync(92, [92, 255, 0]);
            await ExecLuaAsync($"if KCD2MP_W204VoteIcon then KCD2MP_W204VoteIcon(\"{EscapeLua(rows)}\", -1, {cur}, {(right ? "true" : "false")}) end");
        });
    }

    // ================================================================ the listener

    private void Wo204OnConvFrame(byte src, LootMsg m)
    {
        W203Listen? l = _w3cListen;
        bool mine = l is { Listening: true } && l.Starter == src;
        switch (m.Kind)
        {
            case Protocol.ConvLine when Wo204Rules.TryParseLine(m.Text, out var line):
                Interlocked.Increment(ref _w204LinesIn);
                if (!mine || l!.Cid != line.Cid) return;
                _ = ExecLuaAsync(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"if KCD2MP_W204Line then KCD2MP_W204Line({src}, {line.Cid}, \"{EscapeLua(line.Name)}\", {(line.IsPlayer ? "true" : "false")}, \"{EscapeLua(line.StringName)}\", {line.DurMs}, {(line.Earlier ? "true" : "false")}) end"));
                if (!line.Earlier && W204("conv_voice")) _ = Wo204VoiceAsync(src, line);
                return;
            case Protocol.ConvOptions when Wo204Rules.TryParseOptions(m.Text, out int ocid, out int dec, out var rows):
                Interlocked.Increment(ref _w204OptionsIn);
                if (!mine || l!.Cid != ocid) return;
                _w204ListenDec = (src, ocid, dec);
                _ = ExecLuaAsync($"if KCD2MP_W204Options then KCD2MP_W204Options({src}, {ocid}, {dec}, \"{EscapeLua(Wo204Rules.RowsForLua(rows))}\") end");
                return;
            case Protocol.ConvChosen when Wo203CamRules.TryParseCid(m.Text, out int ccid, out string rest):
            {
                Interlocked.Increment(ref _w204ChosenIn);
                var f = rest.Split('|');
                if (!mine || l!.Cid != ccid || f.Length != 2 || !int.TryParse(f[0], out int cdec) || !int.TryParse(f[1], out int cidx)) return;
                _ = ExecLuaAsync($"if KCD2MP_W204Chosen then KCD2MP_W204Chosen({src}, {ccid}, {cdec}, {cidx}) end");
                return;
            }
            case Protocol.ConvVote when Wo204Rules.TryParseVote(m.Text, out int vcid, out int vdec, out int vidx):
                Wo204ConvOnVote(src, vcid, vdec, vidx);
                return;
            default:
                Interlocked.Increment(ref _w204ConvBad);
                return;
        }
    }

    /// <summary>The line's voice on the speaker's body here: the starter's figure for his own Henry, the NPC's copy (or the host's own NPC)
    /// for the others -- the body's own speak manager (op 93), so its lips move and the voice comes from it.</summary>
    private async Task Wo204VoiceAsync(byte starter, Wo204Rules.ConvLineMsg line)
    {
        uint eid = 0;
        if (line.Speaker == "@") _ghostEntityIds.TryGetValue(starter.ToString(System.Globalization.CultureInfo.InvariantCulture), out eid);
        else _npcEntityIds.TryGetValue(line.Speaker, out eid);
        var pb = System.Text.Encoding.UTF8.GetBytes(line.Path);
        if (eid == 0 || pb.Length is 0 or > 200) { Interlocked.Increment(ref _w204LinesNoBody); return; }
        var a = new byte[10 + pb.Length];
        a[0] = 93;
        BitConverter.GetBytes(eid).CopyTo(a, 1);
        BitConverter.GetBytes((uint)line.DurMs).CopyTo(a, 5);
        a[9] = (byte)pb.Length; pb.CopyTo(a, 10);
        var r = await W3cNativeAsync(93, a);
        if (r is { Ok: true } x && x.Payload.Length > 0 && x.Payload[0] == 0) Interlocked.Increment(ref _w204LinesVoiced);
        else Interlocked.Increment(ref _w204LinesNoBody);
    }

    /// <summary>The Lua's w204_vote: "&lt;starter&gt; &lt;cid&gt; &lt;decId&gt; &lt;index|clear&gt;" -- this player's vote on the decision their HUD shows.</summary>
    private void Wo204ConvOnVoteEvent(string? arg)
    {
        var f = (arg ?? "").Split(' ');
        if (f.Length != 4 || !byte.TryParse(f[0], out byte st) || !int.TryParse(f[1], out int cid) || !int.TryParse(f[2], out int dec)) return;
        int idx = f[3] == "clear" ? -1 : int.TryParse(f[3], out int v) ? v : -2;
        if (idx < -1 || _w204ListenDec != (st, cid, dec)) return;
        Interlocked.Increment(ref _w204VotesOut);
        Console.WriteLine($"WO204-CONV this player votes {(idx < 0 ? "clear" : $"row {idx}")} on decision {dec} of {st}:{cid}");
        _ = Wo203SendConvAsync(W3cPeerOf(st), Protocol.ConvVote, Wo204Rules.VoteText(cid, dec, idx));
    }

    // ================================================================ III.5 a trialog's roles (the starter's gestures)

    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Dictionary<string, string>> _w204Roles = new();
    private long _w204RoleMaps, _w204RoleMapsMissing, _w204RolePlaced;

    /// <summary>The conversation's own file (the game's Scripts.pak, beside kcd.log) names its roles and the souls it binds; with the soul
    /// order the engine logged, each role's gesture goes on the right body (WO-203 sent only HENRY's with three or more people).</summary>
    private void Wo204LoadRoles(int cid, string line, string[] others)
    {
        if (!W204("conv_trialog_roles") || Wo204Rules.ParseDialogueHead(line) is not { } h) return;
        _ = Task.Run(() =>
        {
            try
            {
                if (KcdLogLocator.Find() is not string log || Path.GetDirectoryName(log) is not string root) return;
                using var pak = System.IO.Compression.ZipFile.OpenRead(Path.Combine(root, "Data", "Scripts.pak"));
                string name = h.Path[(h.Path.LastIndexOf('.') + 1)..];
                var e = pak.GetEntry(Wo204Rules.DialogFile(h.Path))
                        ?? pak.Entries.FirstOrDefault(x => x.FullName.EndsWith("/" + name + ".xml", StringComparison.OrdinalIgnoreCase));
                if (e is null) { Interlocked.Increment(ref _w204RoleMapsMissing); return; }
                using var r = new StreamReader(e.Open());
                var map = Wo204Rules.MapRoles(Wo204Rules.RolesOf(r.ReadToEnd(), name), others);
                if (map.Count == 0) { Interlocked.Increment(ref _w204RoleMapsMissing); return; }
                _w204Roles[cid] = map;
                if (_w204Roles.Count > 20) foreach (var k in _w204Roles.Keys.Take(10)) _w204Roles.TryRemove(k, out _);
                Interlocked.Increment(ref _w204RoleMaps);
                Console.WriteLine($"WO204-CONV roles of conversation {cid}: {string.Join(", ", map.Select(kv => kv.Key + "=" + kv.Value))}");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Interlocked.Increment(ref _w204RoleMapsMissing);
            }
        });
    }

    private (string Body, string Face)? Wo204PlaceByRole(int cid, string who)
    {
        if (!_w204Roles.TryGetValue(cid, out var map) || !map.TryGetValue(who, out string? soul)) return null;
        Interlocked.Increment(ref _w204RolePlaced);
        return (soul, Wo203ConvRules.Starter);
    }

    private string Wo204ConvStats() =>
        $"conv lines_out={Interlocked.Read(ref _w204LinesOut)} lines_in={Interlocked.Read(ref _w204LinesIn)} voiced={Interlocked.Read(ref _w204LinesVoiced)} voice_no_body={Interlocked.Read(ref _w204LinesNoBody)}"
        + $" options_out={Interlocked.Read(ref _w204OptionsOut)} options_in={Interlocked.Read(ref _w204OptionsIn)} votes_out={Interlocked.Read(ref _w204VotesOut)} votes_in={Interlocked.Read(ref _w204VotesIn)}"
        + $" vote_icons={Interlocked.Read(ref _w204VoteIcons)} chosen_out={Interlocked.Read(ref _w204ChosenOut)} chosen_in={Interlocked.Read(ref _w204ChosenIn)} bad={Interlocked.Read(ref _w204ConvBad)}"
        + $" role_maps={Interlocked.Read(ref _w204RoleMaps)} role_maps_missing={Interlocked.Read(ref _w204RoleMapsMissing)} role_placed={Interlocked.Read(ref _w204RolePlaced)}";
}
