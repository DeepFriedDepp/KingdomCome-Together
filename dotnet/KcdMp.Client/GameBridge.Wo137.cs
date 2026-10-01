// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-137: shared quests -- the agent's half (docs/WO-137-findings.md).
//
// HOST (the world holds the story)
//   * the DLL records every quest State change in the host's own graph (native
//     wo137.cpp: the engine's setter, real changes only, in the engine's order);
//     each one that belongs to a quest and is not this machine's own streaming
//     goes to every joiner as QuestHost Change, in that order;
//   * a joiner's request (QuestAsk Request "old -> new" on one State) is judged
//     against the host's own value (already there = counted once; at old =
//     applied through the same Set port; anything else = refused) and answered;
//     an applied request comes back to everyone as ordinary changes;
//   * every 30 s the host's current values of the States that changed this
//     session go out as a Checkpoint (the periodic compare);
//   * mp_quest_sync off (the kill switch) stops all of it at once; a Godwin
//     stretch (Switching to player 1) holds it; a load holds it.
// JOINER (the joiner's copy follows)
//   * the host's changes are applied to this copy in the host's order, never
//     while a world loads (WO-136's hold); journal, HUD and markers follow the
//     game's own way (the State's consumers run);
//   * this copy's own ROOT transitions (the world did them here: examine, pickup,
//     area, a conversation's outcome) go to the host as requests; mirrored and
//     cascade changes never do; a refused one is put back to the host's value;
//   * quest time sets never run here (the DLL's time gate): only the host's
//     clock moves the world (WO-133);
//   * talking: the local player's request resumes the copy he is facing for the
//     conversation (inside wh_dlg_RequestTimeout), the host holds its NPC busy
//     meanwhile, and the copy is paused again when the dialogue ends.
public partial class GameBridge
{
    private volatile bool _w137SyncOn = true;           // mp_quest_sync (default ON: fail-closed pieces, the maintainer's rule)
    private volatile bool _w137HostModeOn = true;       // joiner: the host's announced mode
    private volatile bool _w137HostModeKnown;
    private volatile int _w137HostPlayer;               // the local "Switching to player N" (0 = Henry)
    private volatile bool _w137Connected;
    private int _w137CfgKey = -1;
    private long _w137CfgAtMs;
    private uint _w137Tok;
    private Channel<Func<Task>>? _w137Work;
    private readonly QuestApplyQueue _w137Queue = new();
    private readonly ConcurrentDictionary<string, (int Val, string Port, long AtMs)> _w137HostSeen = new(StringComparer.Ordinal);
    private volatile bool _w137HostSeenDirty;
    private long _w137CheckpointAtMs, _w137ModeAtMs;
    private readonly ConcurrentDictionary<uint, (QuestChange Req, long AtMs)> _w137Asked = new();
    private readonly ConcurrentDictionary<string, string> _w137HostPort = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (byte Peer, DateTime Since, uint Tok)> _w137TalkHolds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<(byte Src, uint Tok, QuestChange Req)> _w137HeldRequests = new();
    private readonly ConcurrentDictionary<string, int> _w137Retries = new(StringComparer.Ordinal);
    private long _w137Out, _w137In, _w137Applied, _w137Unchanged, _w137ApplyFail, _w137AsksOut, _w137AsksIn, _w137Vetoed,
                 _w137Mismatch, _w137Corrected, _w137VerdictApplied, _w137VerdictAlready, _w137VerdictRefused, _w137Checkpoints,
                 _w137TalksOut, _w137TalksIn, _w137Mirrors;
    private readonly ConcurrentDictionary<string, long> _w137VetoCounts = new(StringComparer.Ordinal);

    /// <summary>The host of a shared world (its own mp_shared_world).</summary>
    private bool W137Host => _combatRoleApplied && _isDamageAuthority && _sharedWorld;
    /// <summary>A joiner of a shared-world session (joined or joining: the host's changes are kept).</summary>
    private bool W137JoinerSession => _combatRoleApplied && !_isDamageAuthority && _hostAuthority && JoinerSharedEffective;
    /// <summary>A joiner whose loaded world is the host's: the one that applies and asks.</summary>
    private bool W137Joiner => W137JoinerSession && _joinedWorld;

    private void Wo137OnConnect(CancellationToken ct)
    {
        _w137Connected = true;
        _w137CfgKey = -1;
        _w137HostModeKnown = false;
        _w137HostModeOn = true;
        var ch = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
        _w137Work = ch;
        _combat.OnQuestChange = c => { Interlocked.Increment(ref _w137In); ch.Writer.TryWrite(() => Wo137OnLocalChangeAsync(c)); };
        _ = Wo137WorkLoopAsync(ch.Reader, ct);
        _ = Wo137LoopAsync(ct);
        _ = Wo137DrainLoopAsync(ct);
    }

    private async Task Wo137OnDisconnectAsync()
    {
        _w137Connected = false;
        _combat.OnQuestChange = null;
        _w137Work?.Writer.TryComplete();
        _w137Work = null;
        int dropped = _w137Queue.Reset();
        _w137Asked.Clear();
        _w137HostSeen.Clear();
        foreach (var (npc, h) in _w137TalkHolds.ToArray())
            if (_w137TalkHolds.TryRemove(npc, out _)) _ = ExecLuaAsync($"if KCD2MP_W137HostHold then KCD2MP_W137HostHold(false, \"{npc}\", {h.Peer}, \"disconnect\") end");
        try { await _combat.Wo137ConfigAsync(false, 0, 0); } catch { }
        _w137CfgKey = -1;
        try { await ExecLuaAsync("if KCD2MP_W137Session then KCD2MP_W137Session(false, false, false) end"); } catch { }
        if (dropped > 0) Console.WriteLine($"MP-W137 disconnected: {dropped} host change(s) still queued were dropped (the next join loads the host's world exactly)");
    }

    private async Task Wo137WorkLoopAsync(ChannelReader<Func<Task>> reader, CancellationToken ct)
    {
        try
        {
            await foreach (var work in reader.ReadAllAsync(ct))
            {
                try { await work(); }
                catch (Exception ex) { Console.WriteLine($"MP-W137 work item failed: {ex.GetType().Name}: {ex.Message}"); }
            }
        }
        catch (OperationCanceledException) { }
        catch (ChannelClosedException) { }
    }

    private void Wo137Post(Func<Task> work)
    {
        if (_w137Work is { } w && !w.Writer.TryWrite(work)) Console.WriteLine("MP-W137 work queue closed -- item dropped");
    }

    // ---------------------------------------------------------------- the 1 s loop

    private async Task Wo137LoopAsync(CancellationToken ct)
    {
        long lastStats = Environment.TickCount64;
        bool wasHolding = false;
        while (!ct.IsCancellationRequested && _w137Connected)
        {
            try { await Task.Delay(1000, ct); } catch { return; }
            try
            {
                bool holding = Wo136Holding;
                await Wo137PushConfigAsync(holding);
                bool host = W137Host, joiner = W137Joiner;
                bool joinerActive = joiner && Wo137Active(holding);
                if (joinerActive && !_w137JoinerWasActive)
                {
                    // this copy is the host's world again (a join, a reload, the sync back on): compare it now
                    await Wo137SendAsync(Protocol.QuestAskUp, Protocol.JoinTargetHost, Protocol.QuestAskResync, 0, _w137JoinerEverActive ? "resumed" : "joined");
                    Console.WriteLine($"MP-W137 joiner: the mirror runs{(_w137JoinerEverActive ? " again" : "")} -- asked the host for a checkpoint now");
                    _w137JoinerEverActive = true;
                }
                _w137JoinerWasActive = joinerActive;
                _ = ExecLuaAsync($"if KCD2MP_W137Session then KCD2MP_W137Session({B(host)}, {B(joiner)}, {B(Wo137Active(holding))}) end");
                if (wasHolding && !holding && host) Wo137ReleaseHeldRequests();
                wasHolding = holding;
                long now = Environment.TickCount64;
                if (host && LivePartners().Count > 0)
                {
                    if (now - _w137ModeAtMs >= 10_000) { _w137ModeAtMs = now; await Wo137SendModeAsync("heartbeat"); }
                    if (!holding && _w137SyncOn && (now - _w137CheckpointAtMs) >= 30_000 && (_w137HostSeenDirty || now - _w137CheckpointAtMs >= 120_000))
                    {
                        _w137CheckpointAtMs = now;
                        _w137HostSeenDirty = false;
                        Wo137Post(Wo137SendCheckpointAsync);
                    }
                    foreach (var (npc, h) in _w137TalkHolds.ToArray())
                        if (DateTime.UtcNow - h.Since > TimeSpan.FromMinutes(5) && _w137TalkHolds.TryRemove(npc, out _))
                        {
                            Console.WriteLine($"MP-W137 host: {npc} was held for ghost {h.Peer}'s conversation for 5 min -- released (safety)");
                            _ = ExecLuaAsync($"if KCD2MP_W137HostHold then KCD2MP_W137HostHold(false, \"{npc}\", {h.Peer}, \"timeout\") end");
                        }
                }
                // asks older than a minute: the host never answered (a mixed build, a lost frame)
                foreach (var (tok, a) in _w137Asked.ToArray())
                    if (now - a.AtMs > 60_000 && _w137Asked.TryRemove(tok, out _))
                        Console.WriteLine($"MP-W137 joiner: request #{tok} {a.Req.Path} {a.Req.Port} got no answer in 60 s -- dropped (the checkpoint corrects this copy toward the host)");
                if (now - lastStats >= 60_000)
                {
                    lastStats = now;
                    Console.WriteLine(Wo137StatsLine());
                }
            }
            catch (Exception ex) { Console.WriteLine($"MP-W137 tick failed: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    /// <summary>The quest mirror runs: a role, the kill switch on, the host's mode on (joiner), Henry (host), no load.</summary>
    private bool Wo137Active(bool holding) =>
        Wo137Rules.MirrorActive(holding, _w137SyncOn, W137Host, W137Joiner, _w137HostPlayer, LivePartners().Count, _w137HostModeOn);

    private async Task Wo137PushConfigAsync(bool holding)
    {
        bool active = Wo137Active(holding);
        byte role = !active ? (byte)0 : W137Host ? (byte)1 : (byte)2;
        byte flags = role == 2 ? (byte)1 : (byte)0;   // the joiner's time gate
        int key = (active ? 1 : 0) | (role << 1) | (flags << 4);
        long now = Environment.TickCount64;
        if (key == _w137CfgKey && now - _w137CfgAtMs < 10_000) return;
        bool changed = key != _w137CfgKey;
        var ok = await _combat.Wo137ConfigAsync(active, role, flags);
        if (ok != true) { if (changed) Console.WriteLine($"MP-W137 config {(active ? "on" : "off")} role={role} NOT taken by the DLL ({(ok is null ? "no answer (a world loading, or no plugin)" : "refused: the detector is not armed yet")}) -- tried again"); return; }
        _w137CfgKey = key; _w137CfgAtMs = now;
        if (changed)
            Console.WriteLine($"MP-W137 quest sync {(active ? "ON" : "off")} -- {(role == 1 ? "host: every quest change goes to the joiners" : role == 2 ? "joiner: the host's changes are applied here, this copy's own steps go to the host, quest time sets are the host's" : Wo137OffWhy(holding))}");
    }

    private string Wo137OffWhy(bool holding) =>
        !_combatRoleApplied ? "no session role yet"
      : holding ? "a world is loading (WO-136's hold)"
      : !_w137SyncOn ? "mp_quest_sync off"
      : _w137HostPlayer != 0 ? $"this game plays player {_w137HostPlayer} (not Henry)"
      : W137Host ? "no joiner connected"
      : W137JoinerSession && !_joinedWorld ? "not in the host's world yet"
      : W137JoinerSession && !_w137HostModeOn ? "the host's quest sync is off"
      : "not a shared-world session";

    private string Wo137StatsLine()
    {
        var vetoes = string.Join(",", _w137VetoCounts.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key}:{k.Value}"));
        return FormattableString.Invariant(
            $"MP-WO137-STATS role={(W137Host ? "host" : W137Joiner ? "joiner" : W137JoinerSession ? "joiner-not-joined" : "none")} sync={On(_w137SyncOn)} host_mode={On(_w137HostModeOn)} player={_w137HostPlayer} in={_w137In} out={_w137Out} queue={_w137Queue.Count} applied={_w137Applied} unchanged={_w137Unchanged} apply_fail={_w137ApplyFail} mirrors={_w137Mirrors} asks_out={_w137AsksOut} asks_in={_w137AsksIn} verdicts=applied:{_w137VerdictApplied},already:{_w137VerdictAlready},refused:{_w137VerdictRefused} checkpoints={_w137Checkpoints} mismatch={_w137Mismatch} corrected={_w137Corrected} talks=out:{_w137TalksOut},in:{_w137TalksIn} holds={_w137TalkHolds.Count} vetoes={vetoes}");
    }

    private void Wo137Veto(string why) => _w137VetoCounts.AddOrUpdate(why, 1, (_, n) => n + 1);

    // ---------------------------------------------------------------- this game's own changes (the DLL)

    private async Task Wo137OnLocalChangeAsync(QuestChange c)
    {
        // A record the DLL made before the config push reached it (the kill switch, a load, a
        // Godwin stretch, the host's mode going off) is dropped here: "at once" means at once.
        if (!Wo137Active(Wo136Holding)) { Wo137Veto("inactive"); return; }
        if (W137Host)
        {
            if (Wo137Rules.HostSendVeto(c) is { } veto) { Interlocked.Increment(ref _w137Vetoed); Wo137Veto("host-" + veto); return; }
            _w137HostSeen[c.Path] = (c.New, c.Port, Environment.TickCount64);
            if (_w137HostSeen.Count > 4000) Wo137TrimSeen();
            _w137HostSeenDirty = true;
            string text = Wo137Rules.ChangeText(c);
            var peers = Wo134Peers();
            foreach (byte g in peers) await Wo137SendAsync(Protocol.QuestHostUp, g, Protocol.QuestHostChange, 0, text);
            Wo148NoteQuestChange($"#{c.Seq} {c.Path} {c.Old}->{c.New}");   // WO-148 3.3: a quest reacting to a partner's carry is logged
            Console.WriteLine(FormattableString.Invariant(
                $"MP-W137 host change #{c.Seq} {c.Path} {(c.Port.Length > 0 ? c.Port : "-")} {c.Old}->{c.New} ({c.Type}{(c.Cascade ? ", cascade" : ", root")}{(c.Mirror ? ", from a request" : "")}) -> {peers.Count} joiner(s)"));
            return;
        }
        if (W137JoinerSession)
        {
            if (c.Mirror) { Interlocked.Increment(ref _w137Mirrors); return; }   // the host's own change, applied here: not asked back
            if (!W137Joiner) { Wo137Veto("joiner-not-joined"); return; }
            if (Wo137Rules.JoinerAskVeto(c) is { } veto)
            {
                Interlocked.Increment(ref _w137Vetoed); Wo137Veto("joiner-" + veto);
                if (veto is not ("cascade" or "silent" or "per-machine"))
                    Console.WriteLine(FormattableString.Invariant($"MP-W137 joiner: local change {c.Path} {(c.Port.Length > 0 ? c.Port : "-")} {c.Old}->{c.New} not sent to the host ({veto})"));
                return;
            }
            uint tok = Interlocked.Increment(ref _w137Tok);
            _w137Asked[tok] = (c, Environment.TickCount64);
            Wo147LearnPortValue(c.Path, c.Port, c.New, c.Type);   // WO-147: what this port produced here
            bool conv = Wo147InOwnConversation();                  // WO-147: a conversation's outcome is this player's
            var sent = conv ? c with { Flags = (byte)(c.Flags | Wo147Rules.FlagConversation) } : c;
            await Wo137SendAsync(Protocol.QuestAskUp, Protocol.JoinTargetHost, Protocol.QuestAskRequest, tok, Wo137Rules.RequestText(sent));
            Interlocked.Increment(ref _w137AsksOut);
            Console.WriteLine(FormattableString.Invariant($"MP-W137 joiner -> host request #{tok}: {c.Path} {c.Port} {c.Old}->{c.New} ({c.Type}{(conv ? ", from this player's conversation" : "")}) -- this game's own step, the host applies it to the world"));
        }
    }

    private void Wo137TrimSeen()
    {
        foreach (var k in _w137HostSeen.OrderBy(kv => kv.Value.AtMs).Take(_w137HostSeen.Count - 3000).Select(kv => kv.Key).ToList())
            _w137HostSeen.TryRemove(k, out _);
    }

    private async Task Wo137SendAsync(byte type, byte target, byte kind, uint tok, string text)
    {
        try
        {
            await WriteJoinAsync(new LootMsg(kind, tok, text).BuildUp(type, target));
            if (type == Protocol.QuestHostUp) Interlocked.Increment(ref _w137Out);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"MP-W137 {(type == Protocol.QuestHostUp ? Protocol.QuestHostName(kind) : Protocol.QuestAskName(kind))} not sent: {ex.Message}");
        }
    }

    private async Task Wo137SendModeAsync(string why)
    {
        string text = $"{(_w137SyncOn && _w137HostPlayer == 0 ? "on" : "off")} {(_w137SyncOn ? (_w137HostPlayer == 0 ? why : "not-henry") : "mp_quest_sync-off")}";
        foreach (byte g in Wo134Peers()) await Wo137SendAsync(Protocol.QuestHostUp, g, Protocol.QuestHostMode, 0, text);
    }

    // ---------------------------------------------------------------- frames from the relay

    private async Task Wo137OnFrameAsync(int type, byte src, byte[] body)
    {
        if (!LootMsg.TryDecode(body, out var m)) { Wo137Veto("malformed"); return; }
        if (type == Protocol.QuestHostDown)
        {
            if (!W137JoinerSession) { Wo137Veto("host-frame-not-joiner"); return; }
            switch (m.Kind)
            {
                case Protocol.QuestHostChange when Wo137Rules.TryParseChangeText(m.Text, out var c):
                    if (Wo137Rules.IsDlc(c.Path) || c.QuestLen <= 0) { Wo137Veto("host-change-refused"); return; }
                    if (!Wo137Rules.KeepHostChange(_joinedWorld, _jj?.Phase)) { Wo137Veto("host-change-not-in-host-world"); return; }
                    if (_w137HostModeKnown && !_w137HostModeOn) { Wo137Veto("host-change-while-host-off"); return; }
                    if (c.Port.Length > 0) _w137HostPort[c.Path] = c.Port;
                    if (c.Seq != 0 && _w137LastHostSeq != 0 && c.Seq <= _w137LastHostSeq)
                    {
                        // TCP keeps the host's order and nothing is sent twice: a number that does not
                        // grow is a host game that started again (its DLL counts from 1).
                        int n = _w137Queue.Reset();
                        Console.WriteLine($"MP-W137 joiner: the host's change numbers started again ({_w137LastHostSeq} -> {c.Seq}) -- a new host game; {n} queued change(s) of the old one dropped");
                    }
                    _w137LastHostSeq = c.Seq;
                    _w137Queue.Enqueue(c);
                    return;
                case Protocol.QuestHostResult when Wo137Rules.TryParseResultText(m.Text, out var verdict, out int hv, out string hp, out string path):
                    Wo137Post(() => Wo137OnResultAsync(m.Tok, verdict, hv, hp, path));
                    return;
                case Protocol.QuestHostCheckpoint when Wo137Rules.TryParseCheckpointText(m.Text, out int part, out int nparts, out var entries):
                    Wo137Post(() => Wo137OnCheckpointAsync(part, nparts, entries));
                    return;
                case Protocol.QuestHostMode when Wo137Rules.TryParseModeText(m.Text, out bool on, out string why):
                    if (!_w137HostModeKnown || on != _w137HostModeOn)
                        Console.WriteLine($"MP-W137 joiner: the host's quest sync is {(on ? "ON" : "OFF")} ({why})");
                    _w137HostModeKnown = true;
                    _w137HostModeOn = on;
                    if (!on) { int n = _w137Queue.Reset(); if (n > 0) Console.WriteLine($"MP-W137 joiner: {n} queued host change(s) dropped -- the host's quest sync is off"); }
                    return;
                case Protocol.QuestHostHold when Wo137Rules.TryParseTalkText(m.Text, out bool hon, out string npc):
                    Console.WriteLine($"MP-W137 joiner: the host {(hon ? "holds" : "released")} {npc} {(hon ? "for this conversation (busy on the host)" : "")}");
                    return;
                default:
                    Wo137Veto("host-frame-unparsed");
                    Console.WriteLine($"MP-W137 joiner: {Protocol.QuestHostName(m.Kind)} from ghost {src} refused (malformed text)");
                    return;
            }
        }
        if (type == Protocol.QuestAskDown)
        {
            if (!W137Host) { Wo137Veto("ask-not-host"); return; }
            switch (m.Kind)
            {
                case Protocol.QuestAskRequest when Wo137Rules.TryParseRequestText(m.Text, out var req):
                    Interlocked.Increment(ref _w137AsksIn);
                    Wo137Post(() => Wo137HostRequestAsync(src, m.Tok, req));
                    return;
                case Protocol.QuestAskTalk when Wo137Rules.TryParseTalkText(m.Text, out bool on, out string npc):
                    Interlocked.Increment(ref _w137TalksIn);
                    Wo137Post(() => Wo137HostTalkAsync(src, m.Tok, on, npc));
                    return;
                case Protocol.QuestAskResync when Wo137Rules.TryParseResyncText(m.Text, out string rwhy):
                    if (!_w137SyncOn || Wo136Holding) { Console.WriteLine($"MP-W137 host: ghost {src} asked for a checkpoint ({rwhy}) -- not now ({(!_w137SyncOn ? "mp_quest_sync off" : "this world is loading")}; the periodic one follows)"); return; }
                    _w137HostSeenDirty = true;
                    Wo137Post(() => Wo137SendCheckpointAsync(src, rwhy));
                    return;
                default:
                    Wo137Veto("ask-unparsed");
                    Console.WriteLine($"MP-W137 host: {Protocol.QuestAskName(m.Kind)} from ghost {src} refused (malformed text)");
                    return;
            }
        }
        await Task.CompletedTask;
    }

    private uint _w137LastHostSeq;
    private bool _w137JoinerWasActive, _w137JoinerEverActive;

    /// <summary>
    /// WO-124's join enters "loading": the world that arrives is the host's as it is now, so every host
    /// change queued before it is already in it (replaying one could set a State back). Dropped here.
    /// </summary>
    private void Wo137OnJoinLoading(uint joinId)
    {
        int n = _w137Queue.Reset();
        _w137LastHostSeq = 0;
        _w137Retries.Clear();
        _w137Asked.Clear();
        _w137JoinerWasActive = false;
        Console.WriteLine($"MP-W137 joiner: join 0x{joinId:x8} loads the host's world{(n > 0 ? $" -- {n} host change(s) queued before it dropped (already in that world)" : "")}; changes from now on are applied once it is up");
    }

    // ---------------------------------------------------------------- joiner: the apply queue

    private async Task Wo137DrainLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _w137Connected)
        {
            try { await Task.Delay(50, ct); } catch { return; }
            try
            {
                int budget = 64;   // at most this many applies per 50 ms (a quest cascade is ~10-20)
                while (budget-- > 0 && W137Joiner && Wo137Active(Wo136Holding) && _w137Queue.TryPeek(out var c))
                {
                    if (!await Wo137ApplyOneAsync(c)) break;   // not now (the DLL did not answer, the node is asleep): the next tick tries again
                    _w137Queue.Remove(c);   // exactly this one: a correction put in front meanwhile stays
                }
            }
            catch (Exception ex) { Console.WriteLine($"MP-W137 drain failed: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    /// <summary>One host change onto this copy. False = keep it (try again next tick).</summary>
    private async Task<bool> Wo137ApplyOneAsync(QuestChange c)
    {
        if (Wo137Rules.PerMachine(c)) { Wo137Veto("apply-per-machine"); return true; }
        // WO-147: a correction fires only while this copy still differs from the host's value (the field's
        // "carryingBags SetCart 4->2 (the host had 4)": an earlier correction's cascade had already put it on 4).
        if (c.Seq == 0 && c.Port.Length > 0 && await Wo147CorrectionStillNeededAsync(c) == false) return true;
        if (c.Port.Length == 0)
        {
            Interlocked.Increment(ref _w137ApplyFail); Wo137Veto("apply-no-port");
            Console.WriteLine(FormattableString.Invariant($"MP-W137 joiner: host change #{c.Seq} {c.Path} {c.Old}->{c.New} came through no port -- not applicable here (the checkpoint compares it)"));
            return true;
        }
        var r = await _combat.Wo137ApplyAsync(c.Seq, c.Path, c.Port);
        if (r is not { } a)
        {
            int n = _w137Retries.AddOrUpdate(c.Path, 1, (_, k) => k + 1);
            if (n <= 20) return false;
            _w137Retries.TryRemove(c.Path, out _);
            Interlocked.Increment(ref _w137ApplyFail);
            Console.WriteLine($"MP-W137 joiner: host change #{c.Seq} {c.Path} {c.Port}: the DLL did not answer 20 times -- dropped");
            return true;
        }
        if (Wo137Rules.Retryable(a.Result))
        {
            int n = _w137Retries.AddOrUpdate(c.Path, 1, (_, k) => k + 1);
            if (n == 1) Console.WriteLine(FormattableString.Invariant($"MP-W137 joiner: host change #{c.Seq} {c.Path} {c.Port} waits: {Wo137Rules.AppliedName(a.Result)} here (tries again)"));
            if (n <= 600) return false;   // ~30 s of ticks
            _w137Retries.TryRemove(c.Path, out _);
            Interlocked.Increment(ref _w137ApplyFail);
            Console.WriteLine(FormattableString.Invariant($"MP-W137 joiner: host change #{c.Seq} {c.Path} {c.Port} still {Wo137Rules.AppliedName(a.Result)} after 30 s -- dropped (the checkpoint compares it)"));
            return true;
        }
        _w137Retries.TryRemove(c.Path, out _);
        switch (a.Result)
        {
            case 0:
                Interlocked.Increment(ref _w137Applied);
                Wo147LearnPortValue(c.Path, c.Port, a.New, a.Type);   // WO-147
                Console.WriteLine(FormattableString.Invariant(
                    $"MP-W137 joiner applied host change #{c.Seq} {c.Path} {c.Port} {a.Old}->{a.New}{(a.New != c.New ? $" (the host had {c.New})" : "")}"));
                if (c.Seq == 0 && a.New != c.New) Wo144CorrectionMissed(c.Path, c.Port, c.New, a.New);   // WO-144 4.1
                break;
            case 1:
                Interlocked.Increment(ref _w137Unchanged);   // already there: counted once
                // WO-147: a correction that moved nothing is remembered like a miss (the field fired the same no-op
                // at every checkpoint: waitingForReactors x7, konfrontace.state30 x10, takedown tutorials x11).
                if (c.Seq == 0 && a.New != c.New) Wo144CorrectionMissed(c.Path, c.Port, c.New, a.New);
                break;
            default:
                Interlocked.Increment(ref _w137ApplyFail);
                Console.WriteLine(FormattableString.Invariant($"MP-W137 joiner: host change #{c.Seq} {c.Path} {c.Port} not applied: {Wo137Rules.AppliedName(a.Result)}"));
                break;
        }
        return true;
    }

    // ---------------------------------------------------------------- joiner: results, checkpoints

    private async Task Wo137OnResultAsync(uint tok, string verdict, int hostVal, string hostPort, string path)
    {
        _w137Asked.TryRemove(tok, out var asked);
        string what = asked.Req.Path is { Length: > 0 } ? $"{asked.Req.Path} {asked.Req.Port} {asked.Req.Old}->{asked.Req.New}" : path;
        Console.WriteLine(FormattableString.Invariant($"MP-W137 joiner: request #{tok} {what}: the host says {verdict} (host value {hostVal})"));
        if (verdict is "refused" or "failed")
        {
            // WO-147: only a port known to produce the host's value (was: the host's last port, which the
            // field showed landing elsewhere).
            string? port = await Wo147CorrectionPortAsync(path, hostVal, asked.Req.Type);
            if (port is null) { Console.WriteLine($"MP-W137 joiner: {path} cannot be put back to the host's value {hostVal} (no port known to produce it) -- the next join loads it exactly"); return; }
            _w137Queue.EnqueueFront(new QuestChange(0, QuestChange.FNotify, asked.Req.New, hostVal, port, "", path, asked.Req.QuestLen));
            Interlocked.Increment(ref _w137Corrected);
            Console.WriteLine(FormattableString.Invariant($"MP-W137 joiner: {path} goes back toward the host's value {hostVal} ({port}) -- this copy never keeps a step the host's world refused"));
        }
        await Task.CompletedTask;
    }

    private async Task Wo137OnCheckpointAsync(int part, int nparts, List<Wo137Rules.CheckpointEntry> entries)
    {
        if (!W137Joiner || !Wo137Active(Wo136Holding)) return;
        if (_w137Queue.Count > 0 || !_w137Asked.IsEmpty) return;   // not caught up yet: compared next time
        var local = await _combat.Wo137ReadStatesAsync(entries.Select(e => e.Path).ToList());
        if (local is null || local.Count != entries.Count) return;
        Interlocked.Increment(ref _w137Checkpoints);
        int mism = 0, fixedN = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i]; var l = local[i];
            if (!l.Found || !l.Ok || l.Val == e.Val) continue;
            mism++;
            Interlocked.Increment(ref _w137Mismatch);
            // WO-147: only a port known to produce the host's value (the field: "carryingBags SetCart 4->2
            // (the host had 3)" -- the host's last port, not the one for its value).
            string? port = await Wo147CorrectionPortAsync(e.Path, e.Val, null);
            // WO-144 4.1: a port that did not land on the host's value last time is not fired again (the
            // field re-ran SetAroundBoulder's consequences every 30 s: 0 -> 3 while the host had 15)
            if (port is not null && Wo144CorrectionSkipped(e.Path, port, e.Val)) continue;
            Console.WriteLine(FormattableString.Invariant($"MP-W137 MISMATCH {e.Path}: this copy {l.Val}, the host {e.Val}{(port is null ? " -- no port to correct it (the next join loads it exactly)" : $" -- corrected toward the host ({port})")}"));
            if (port is null) continue;
            _w137Queue.EnqueueFront(new QuestChange(0, QuestChange.FNotify, l.Val, e.Val, port, "", e.Path, 0));
            fixedN++;
            Interlocked.Increment(ref _w137Corrected);
        }
        if (mism > 0 || part == nparts)
            Console.WriteLine($"MP-W137 joiner: checkpoint part {part}/{nparts}: {entries.Count} State(s) compared, {mism} mismatch(es), {fixedN} corrected");
    }

    // ---------------------------------------------------------------- host: requests, checkpoints, talk

    private async Task Wo137HostRequestAsync(byte src, uint tok, QuestChange req)
    {
        string head = FormattableString.Invariant($"MP-W137 host: request #{tok} from ghost {src}: {req.Path} {req.Port} {req.Old}->{req.New}");
        if (!_w137SyncOn) { await Wo137ReplyAsync(src, tok, "off", 0, "", req.Path); Console.WriteLine($"{head}: refused (mp_quest_sync off)"); return; }
        if (_w137HostPlayer != 0) { await Wo137ReplyAsync(src, tok, "held", 0, "", req.Path); Console.WriteLine($"{head}: held (this game plays player {_w137HostPlayer}, not Henry)"); return; }
        if (Wo136Holding)
        {
            _w137HeldRequests.Enqueue((src, tok, req));
            Console.WriteLine($"{head}: waits (the host's world is loading)");
            return;
        }
        if (Wo137Rules.IsDlc(req.Path) || Wo137Rules.PerMachine(req)) { await Wo137ReplyAsync(src, tok, "notquest", 0, "", req.Path); Console.WriteLine($"{head}: refused (not a shared quest State)"); return; }
        var reads = await _combat.Wo137ReadStatesAsync([req.Path]);
        if (reads is not { Count: 1 } || !reads[0].Found)
        {
            await Wo137ReplyAsync(src, tok, "notquest", 0, "", req.Path);
            Console.WriteLine($"{head}: refused (no such quest State in the host's world)");
            return;
        }
        var h = reads[0];
        string hostPort = _w137HostSeen.TryGetValue(req.Path, out var seen) ? seen.Port : "";
        var v = Wo137Rules.Judge(h.Ok, h.Val, req.Old, req.New);
        switch (v)
        {
            case Wo137Rules.Verdict.Already:
                Interlocked.Increment(ref _w137VerdictAlready);
                await Wo137ReplyAsync(src, tok, "already", h.Val, hostPort, req.Path);
                Console.WriteLine(FormattableString.Invariant($"{head}: already done in the host's world (host value {h.Val}) -- counted once"));
                return;
            case Wo137Rules.Verdict.Refused:
                Interlocked.Increment(ref _w137VerdictRefused);
                await Wo137ReplyAsync(src, tok, "refused", h.Val, hostPort, req.Path);
                Console.WriteLine(FormattableString.Invariant($"{head}: refused -- the host's world is at {h.Val}, not {req.Old} (a step already past, or not reached)"));
                return;
        }
        // WO-147: a step that fails a quest, cancels an objective or marks someone dead or down is checked
        // against this world first (the field: the joiner's copy of a fist-fight opponent "died" there
        // while he was only down here, and SetNpcIsDead / SetNone / SetFailed failed the quest here).
        if (await Wo147DestructiveGateAsync(src, tok, req, h.Val, hostPort, head)) return;
        await Wo137ApplyRequestAsync(src, tok, req, h.Val, hostPort, head);
    }

    /// <summary>The apply half of a joiner's request (WO-147: also after a destructive step's check).</summary>
    private async Task Wo137ApplyRequestAsync(byte src, uint tok, QuestChange req, int hostVal, string hostPort, string head)
    {
        var h = (Val: hostVal, Ok: true);
        var a = await _combat.Wo137ApplyAsync(tok, req.Path, req.Port);
        if (a is { Result: 0 })
        {
            Interlocked.Increment(ref _w137VerdictApplied);
            await Wo137ReplyAsync(src, tok, "applied", a.Value.New, req.Port, req.Path);
            Console.WriteLine(FormattableString.Invariant($"{head}: APPLIED to the host's world ({a.Value.Old}->{a.Value.New}); its consequences reach every joiner as changes"));
        }
        else if (a is { Result: 1 })
        {
            Interlocked.Increment(ref _w137VerdictAlready);
            await Wo137ReplyAsync(src, tok, "already", a.Value.New, req.Port, req.Path);
            Console.WriteLine($"{head}: already (the engine's own no-op)");
        }
        else
        {
            await Wo137ReplyAsync(src, tok, "failed", h.Val, hostPort, req.Path);
            Console.WriteLine($"{head}: FAILED on the host ({(a is { } x ? Wo137Rules.AppliedName(x.Result) : "no answer from the DLL")})");
        }
    }

    private void Wo137ReleaseHeldRequests()
    {
        int n = 0;
        while (_w137HeldRequests.TryDequeue(out var r)) { var rr = r; Wo137Post(() => Wo137HostRequestAsync(rr.Src, rr.Tok, rr.Req)); n++; }
        if (n > 0) Console.WriteLine($"MP-W137 host: the world has loaded -- {n} joiner request(s) that waited are judged now, in order");
    }

    private Task Wo137ReplyAsync(byte peer, uint tok, string verdict, int hostVal, string hostPort, string path) =>
        Wo137SendAsync(Protocol.QuestHostUp, peer, Protocol.QuestHostResult, tok, Wo137Rules.ResultText(verdict, hostVal, hostPort, path));

    private Task Wo137SendCheckpointAsync() => Wo137SendCheckpointAsync(null, "periodic");

    private async Task Wo137SendCheckpointAsync(byte? onlyPeer, string why)
    {
        if (!W137Host) return;
        var paths = _w137HostSeen.OrderByDescending(kv => kv.Value.AtMs).Take(600).Select(kv => kv.Key).ToList();
        var peers = onlyPeer is byte one ? new List<byte> { one } : Wo134Peers();
        if (paths.Count == 0)
        {
            if (onlyPeer is not null) Console.WriteLine($"MP-W137 host: {why} checkpoint for ghost {onlyPeer}: no quest State changed this session -- nothing to compare");
            return;
        }
        var reads = await _combat.Wo137ReadStatesAsync(paths);
        if (reads is null || reads.Count != paths.Count) return;
        var entries = new List<Wo137Rules.CheckpointEntry>(paths.Count);
        int inFlight = 0;
        for (int i = 0; i < paths.Count; i++)
        {
            bool seen = _w137HostSeen.TryGetValue(paths[i], out var s);
            if (Wo137Rules.CheckpointConsistent(seen, s.Val, reads[i].Found && reads[i].Ok, reads[i].Val))
                entries.Add(new Wo137Rules.CheckpointEntry(paths[i], s.Val, s.Port));
            else if (reads[i].Found && reads[i].Ok) inFlight++;   // a newer value than the last one sent: on its way, compared next time
        }
        var texts = Wo137Rules.CheckpointTexts(entries);
        foreach (byte g in peers)
            foreach (var t in texts) await Wo137SendAsync(Protocol.QuestHostUp, g, Protocol.QuestHostCheckpoint, 0, t);
        Interlocked.Increment(ref _w137Checkpoints);
        Console.WriteLine($"MP-W137 host: {why} checkpoint of {entries.Count} changed quest State(s) in {texts.Count} part(s) -> {peers.Count} joiner(s){(inFlight > 0 ? $" ({inFlight} still on their way left out)" : "")}");
    }

    private async Task Wo137HostTalkAsync(byte src, uint tok, bool on, string npc)
    {
        if (on) _w137TalkHolds[npc] = (src, DateTime.UtcNow, tok);
        else _w137TalkHolds.TryRemove(npc, out _);
        await ExecLuaAsync($"if KCD2MP_W137HostHold then KCD2MP_W137HostHold({B(on)}, \"{npc}\", {src}, \"talk\") end");
        await Wo137SendAsync(Protocol.QuestHostUp, src, Protocol.QuestHostHold, tok, Wo137Rules.TalkText(on, npc));
        Console.WriteLine($"MP-W137 host: ghost {src} {(on ? "talks to" : "finished talking to")} {npc} -- the host's {npc} is {(on ? "HELD (busy: no second conversation here)" : "free again")}");
    }

    // ---------------------------------------------------------------- the engine's lines, the mod's events

    private void Wo137OnQuestLine(string line)
    {
        if (!Wo137Rules.TryParseQuestLine(line, out var q)) return;
        switch (q.Kind)
        {
            case "player":
                if (q.Player != _w137HostPlayer)
                {
                    _w137HostPlayer = q.Player;
                    Console.WriteLine(q.Player == 0
                        ? "MP-W137 this game plays Henry again -- the quest sync resumes"
                        : $"MP-W137 this game now plays player {q.Player} (not Henry) -- the quest sync HOLDS (a Godwin stretch is the host's; joins are refused there too)");
                    if (_combatRoleApplied && _isDamageAuthority) _ = Wo137SendModeAsync("player-switch");
                }
                return;
            case "request":
                if (W137Joiner) _ = ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W137TalkRequest then KCD2MP_W137TalkRequest({q.Id}) end"));
                return;
            case "attempt":
                if (q.Bark) { Wo137Veto("attempt-bark"); return; }   // WO-147: a combat shout or a bark is no conversation
                if (W137Joiner && q.Souls.Contains("Dude"))
                    _ = ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W137TalkAttempt then KCD2MP_W137TalkAttempt({q.Id}, \"{string.Join(' ', q.Souls)}\") end"));
                return;
            case "end":
                if (W137JoinerSession && q.Souls.Length > 0)
                    _ = ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W137DialogEnd then KCD2MP_W137DialogEnd({q.Id}, \"{string.Join(' ', q.Souls)}\") end"));
                return;
        }
    }

    private void Wo137OnEvent(string name, string? arg)
    {
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (name)
        {
            case "w137_sync":      // on|off: the kill switch (mp_quest_sync)
                if (f.Length >= 1 && f[0] is "on" or "off")
                {
                    bool on = f[0] == "on";
                    bool was = _w137SyncOn;
                    _w137SyncOn = on;
                    Console.WriteLine($"MP-W137 mp_quest_sync {(on ? "ON" : "OFF")}{(was == on ? " (unchanged)" : "")} -- {(on ? "quests are shared both ways" : "the mirror and the requests stop at once; a host reload or a rejoin still re-syncs exactly")}");
                    if (!on) { int n = _w137Queue.Reset(); if (n > 0) Console.WriteLine($"MP-W137 {n} queued host change(s) dropped (sync off)"); }
                    _w137CfgKey = -1;
                    if (_combatRoleApplied && _isDamageAuthority) _ = Wo137SendModeAsync(on ? "mp_quest_sync-on" : "mp_quest_sync-off");
                }
                return;
            case "w137_talk":      // on|off <npc> <dialogId>: the joiner resumed / re-paused a host copy for a conversation
                if (f.Length >= 2 && f[0] is "on" or "off" && Wo137Text.IsNpc(f[1])) Wo141OnTalk(f[0] == "on", f[1]);   // WO-141: its activity waits
                if (f.Length >= 2 && f[0] is "on" or "off" && Wo137Text.IsNpc(f[1]) && W137Joiner)
                {
                    uint tok = Interlocked.Increment(ref _w137Tok);
                    Interlocked.Increment(ref _w137TalksOut);
                    _ = Wo137SendAsync(Protocol.QuestAskUp, Protocol.JoinTargetHost, Protocol.QuestAskTalk, tok, Wo137Rules.TalkText(f[0] == "on", f[1]));
                }
                return;
            case "w137_status":
                Console.WriteLine(Wo137StatsLine());
                _ = Task.Run(async () => { var s = await _combat.Wo137StatusAsync(); Console.WriteLine($"MP-W137 native: {s ?? "no answer"}"); });
                return;
        }
    }
}
