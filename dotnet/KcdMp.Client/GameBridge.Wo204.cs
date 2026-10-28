// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-204 (docs/WO-204-findings.md): the bridge's shared half -- the switches (Lua w204_cfg), mp_w204_status, the counters every part
// adds to. The pure rules are Wo204Rules (Wo204.cs). Act I (nearest_player_v2) is GameBridge.Wo201Near.cs + native wo201_near.cpp.
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, bool> _w204On = new(StringComparer.Ordinal);
    private bool W204(string name) => _w204On.TryGetValue(name, out bool v) ? v : Wo204Rules.DefaultOn(name);

    // III.1: the engine's dialogues this player was in that the positive list kept out (barks), and the conversations it let through
    private long _w204BarksKept, _w204TalksAnnounced;
    private long _w204RetiredVoteIn;   // III.2: an older partner's F2 vote-panel messages, dropped
    private long _w204JoinSilenceAborts, _w204JoinProgressIn, _w204JoinProgressOut;
    private long _w204JoinProgressLastMs, _w204JoinWorldAtMs;
    private uint _w204JoinProgressFor;

    private async Task Wo204PushConfigAsync()
    {
        try { await _combat.Wo163Async(Protocol.Wo204OpConfig, BitConverter.GetBytes(Wo204Rules.ConfigBits(W204))); }
        catch (Exception ex) { Console.WriteLine($"MP-W204 config not sent to the DLL: {ex.Message}"); }
    }

    private async Task Wo204NativeStatusAsync()
    {
        var r = await _combat.Wo163Async(Protocol.Wo204OpStatus, []);
        if (r is { Ok: true } rr) Console.WriteLine($"MP-W204-NATIVE {System.Text.Encoding.ASCII.GetString(rr.Payload)}");
    }

    /// <summary>The 4 Hz session tick (Wo201Tick).</summary>
    private void Wo204Tick(long n)
    {
        if (n % 8 == 5) _ = Wo204PushConfigAsync();   // every 2 s: a DLL that came back (a rejoin, a new game) has the switches
        Wo204ConvPollTick();                          // III.3: the starter's HUD rows, while someone listens
        // IV.1, the joiner: from the world's arrival to his ready, his load is reported to the host every 5 s -- the host's paused
        // world waits for it (60 s of silence aborts there)
        uint jid = _joinReceivedId;
        if (jid == 0 || W137Host) { _w204JoinProgressFor = 0; return; }
        long now = Environment.TickCount64;
        if (_w204JoinProgressFor != jid) { _w204JoinProgressFor = jid; _w204JoinWorldAtMs = now; _w204JoinProgressLastMs = 0; }
        if (now - _w204JoinProgressLastMs < Wo204Rules.JoinProgressEveryMs) return;
        _w204JoinProgressLastMs = now;
        string where = _where.ToString().ToLowerInvariant();
        string text = Wo204Rules.JoinProgressText(jid, where, (int)((now - _w204JoinWorldAtMs) / 1000));
        Interlocked.Increment(ref _w204JoinProgressOut);
        _ = Wo203SendSideAsync(Protocol.JoinTargetHost, Protocol.W203JoinProgress, text);
    }

    /// <summary>IV.1, the host: the joiner's load progress renews the paused join's deadline and shows on the host's bar.</summary>
    partial void Wo203JoinOnFrame(byte src, LootMsg m)
    {
        if (m.Kind != Protocol.W203JoinProgress || !Wo204Rules.TryParseJoinProgress(m.Text, out uint jid, out string where, out int secs)) return;
        if (!Wo206JoinMessageAdmitted(Protocol.W203Down, jid, src, "progress")) return;   // WO-206 E: a late "loading" of a closed attempt is never shown
        if (_hostJoin is not { } j || j.JoinId != jid || j.Joiner != src) return;
        Interlocked.Increment(ref _w204JoinProgressIn);
        j.LastJoinerMsgMs = Environment.TickCount64;
        try { j.DeadlineCts?.CancelAfter(TimeSpan.FromSeconds(Wo204Rules.JoinLoadSilenceS + 5)); } catch (ObjectDisposedException) { }
        _ = ExecLuaAsync($"if KCD2MP_JoinProgress then KCD2MP_JoinProgress(\"{jid:x8}\", 100, \"loading\") end");
        if (secs % 30 < 5) Console.WriteLine($"MP-JOIN host: join 0x{jid:x8}: {j.Partner} is loading ({where}, {secs} s) -- the world stays paused until they are ready");
    }

    private void Wo204OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w204_cfg":
                if (!Wo204Rules.TryParseCfg(arg, out string cfg, out bool on)) { Console.WriteLine($"MP-W204 cfg refused: '{arg}'"); return; }
                _w204On[cfg] = on;
                Console.WriteLine($"MP-W204 cfg {cfg}={(on ? "on" : "off")}");
                _ = Wo204PushConfigAsync();
                return;
            case "w204_vote":
                Wo204ConvOnVoteEvent(arg);
                return;
            case "w204_status":
                _ = Wo204NativeStatusAsync();
                Console.WriteLine($"MP-W204-STATS {string.Join(' ', Wo204Rules.SwitchNames.Select(n => $"{n}={(W204(n) ? "on" : "off")}"))} " +
                                  $"barks_kept_out={Interlocked.Read(ref _w204BarksKept)} talks_announced={Interlocked.Read(ref _w204TalksAnnounced)} retired_vote_in={Interlocked.Read(ref _w204RetiredVoteIn)} " +
                                  $"join_progress_in={Interlocked.Read(ref _w204JoinProgressIn)} join_progress_out={Interlocked.Read(ref _w204JoinProgressOut)} join_silence_aborts={Interlocked.Read(ref _w204JoinSilenceAborts)} unstick_releases={Interlocked.Read(ref _w204UnstickReleases)} " +
                                  $"blows={Interlocked.Read(ref _w204Blows)} blows_late={Interlocked.Read(ref _w204BlowsLate)} blows_joiner_values={Interlocked.Read(ref _w204BlowsJoinerValues)} | " + Wo204ConvStats());
                return;
        }
    }

    // IV.3: each partner's last position arrival and the gap before it (the reader's stamp), to tell a blow that waited out a stall
    private readonly ConcurrentDictionary<byte, (long GapMs, long AtMs)> _w204PosGap = new();
    private readonly ConcurrentDictionary<byte, long> _w204PosLast = new();
    private long _w204Blows, _w204BlowsLate, _w204BlowsJoinerValues;

    /// <summary>The reader (Wo147NotePositionAtRead): a partner's position just arrived.</summary>
    private void Wo204NotePosition(byte ghost)
    {
        long now = Environment.TickCount64;
        if (_w204PosLast.TryGetValue(ghost, out long last)) _w204PosGap[ghost] = (now - last, now);
        _w204PosLast[ghost] = now;
    }

    /// <summary>IV.3 (host): one line per blow of a joiner on an NPC -- what his engine said, how it is applied, whether it waited out a
    /// stall of his link.</summary>
    private void Wo204NoteBlow(byte source, string npc, float health, float stamina, string applied)
    {
        long now = Environment.TickCount64;
        bool late = _w204PosGap.TryGetValue(source, out var g) && Wo204Rules.BlowLate(g.GapMs, now - g.AtMs);
        long n = Interlocked.Increment(ref _w204Blows);
        if (late) Interlocked.Increment(ref _w204BlowsLate);
        Console.WriteLine(FormattableString.Invariant(
            $"WO204-BLOW {(late ? "late " : "")}npc={npc} by_ghost={source} joiner_said=st{stamina:F1}/hp{health:F1} applied={applied}{(late ? $" after_stall_ms={g.GapMs}" : "")} [n={n}]"));
    }

    /// <summary>III.4: every line the engine voiced here (the DLL's capture, 4 Hz): kept while this player's own conversation runs, for
    /// its listener.</summary>
    private void Wo204NoteSpokenLine(Wo201Rules.SpeechCaptured l) => Wo204ConvLineCaptured(l);
    partial void Wo204ConvLineCaptured(Wo201Rules.SpeechCaptured l);

    // IV.5: the joiner's copies the engine's state search keeps failing on (their failure times), and when each was last released
    private readonly ConcurrentDictionary<string, Queue<long>> _w204SearchFails = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _w204Unstuck = new(StringComparer.Ordinal);
    private long _w204UnstickReleases;

    /// <summary>The forwarded engine lines (Wo144OnEngineLine) -- never consumes.</summary>
    private void Wo204OnEngineLine(string line)
    {
        if (Wo210Rules.TopicActivated(line) >= 0) { Wo210OnTopicActivated(); return; }   // WO-210 F.2a: the starter's cursor moved
        int topic = Wo204Rules.TopicSelected(line);   // III.3: the starter chose -- the listeners' options close
        if (topic >= 0) { Wo204ConvOnTopicSelected(topic); return; }
        if (!W137Joiner || !W204("copy_unstick") || Wo204Rules.StateSearchFailedNpc(line) is not string npc) return;
        long now = Environment.TickCount64;
        var q = _w204SearchFails.GetOrAdd(npc, _ => new Queue<long>());
        int n;
        lock (q)
        {
            q.Enqueue(now);
            while (q.Count > 0 && now - q.Peek() > Wo204Rules.UnstickWindowMs) q.Dequeue();
            n = q.Count;
        }
        long last = _w204Unstuck.TryGetValue(npc, out long l) ? l : long.MinValue / 2;
        if (!Wo204Rules.UnstickDue(npc, n, now - last)) return;
        _w204Unstuck[npc] = now;
        lock (q) q.Clear();
        Interlocked.Increment(ref _w204UnstickReleases);
        Console.WriteLine($"WO204-UNSTICK npc={npc} the engine's state search failed {n} times in {Wo204Rules.UnstickWindowMs / 1000} s -- its stance and unstance released the engine's own way (wh_ai_NPCStateResetElement); the host's activity is asked again");
        _ = ExecLuaAsync($"if KCD2MP_W160Release then KCD2MP_W160Release(\"{EscapeLua(npc)}\", \"state-search-stuck\") end");
    }

    /// <summary>III.1: may this "New dialogue" line be handled as this player's conversation (announced, offered, gestured)? True for
    /// any other line. Counts what it keeps out.</summary>
    private bool Wo204ConversationAllowed(string line)
    {
        if (!W204("conv_no_barks")) return true;
        if (Wo204Rules.IsConversationLine(line) is not bool conv || conv) return true;
        if (Wo203ConvRules.ParseOwnConversation(line) is not null) Interlocked.Increment(ref _w204BarksKept);
        return false;
    }
}
