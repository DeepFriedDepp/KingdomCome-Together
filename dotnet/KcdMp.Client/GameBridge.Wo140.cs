// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-140: sleeping together, and the "own world" trap -- the agent's half
// (docs/WO-140-findings.md).
//
// ONE CLOCK: THE HOST'S. SLEEP TOGETHER, OR NOT AT ALL.
//   * The asker: the mod holds the bed's "Sleep" before the lie-down (w140_ask);
//     the DLL's gate holds every other way in (the Wait key, sleeping from a bed
//     one sits on, reading): 0xA5 Held. Nothing is lost: on everyone's yes the
//     bed's own flow runs (the lie-down, the picker) or the kept picker opens
//     again; on a no or no answer in 30 s nothing happens and he is told
//     "Other players are not ready to sleep yet!".
//   * When the asker confirms the length (the DLL's "began" edge with the hours)
//     every accepter's game starts its OWN sleep for those hours: C_SkipTime's
//     StartSkipTime with the Sleep id -- the game's sleep screen with the clock,
//     wherever he stands, and the game's rest (observed: exhaust and health
//     restored). Refused by the game (trespass, combat): the forced sleep screen
//     (id 14) still runs, so the clock and the screen still match.
//   * The host's skip is the world's (its own time-skip report, kind sleep); a
//     joiner's own skip moves only his copy and is then pinned to the host's.
//     Whoever wakes first among the host and the asker wakes the others (Stop).
//   * Fall-safe: a joiner's clock ahead of the host's (any source) is pulled
//     back to the host's at once (native, the joiner's own copy only).
//   * The host's save on sleep: the game makes it itself when the host sleeps in
//     a bed that saves; a joiner's bed that would save -> the host makes the
//     game's own rest save after the shared sleep (a joiner never saves).
// The host coordinates: a joiner's ask goes to it; it asks its own player and
// every other joiner and answers the asker once everyone has.
//
// THE OWN-WORLD TRAP: a joiner who loaded his own save and connected from inside
// it is NOT in the host's world. While that lasts, nothing of the host's world is
// applied to his (Wo140Rules.DroppedWhenSeparate: NPCs, their hits, deaths and
// actions, items, loot, quests, crime, the leash), none of his own world goes to
// the host, the host does not leash him, and he is told plainly -- in the game
// every few seconds, in the launcher once -- how to join.
public partial class GameBridge
{
    private volatile bool _w140On = true;              // mp_sleep_vote (default ON: fail-closed, the maintainer's rule)
    private volatile bool _w140Connected;
    private int _w140CfgKey = -1;
    private long _w140CfgAtMs;
    private uint _w140Tok;
    private readonly object _w140Lock = new();
    private Wo140Rules.Vote? _w140Mine;                // this player's own ask
    private string _w140MineSource = "";               // "lua" (the bed, held before the lie-down) or "native" (the gate)
    private bool _w140MineApproved;
    private long _w140MineApprovedMs;
    private Wo140Rules.Vote? _w140Collect;             // host: a joiner's ask, collected here
    private (uint Id, byte Asker, string Kind, byte ReplyTo, long DeadlineMs)? _w140Prompt;   // on this screen now
    private (uint Id, byte Asker, string Kind, long UntilMs)? _w140Accepted;                // said yes, waiting for the Begin
    private readonly ConcurrentDictionary<byte, uint> _w140AcceptedJoiners = new();         // host: joiners who said yes to a vote
    private volatile bool _w140LocalSkipping;          // this game's C_SkipTime runs (the DLL's edges)
    private long _w140SharedUntilMs;                   // a shared sleep is under way (quiet toasts, the host's skip kind)
    private string _w140SharedKind = "sleep";
    private byte _w140SharedAsker = 0xFF;
    private bool _w140SaveAfter;                       // host: the game's rest save after this shared sleep
    private uint _w140HostClock;                       // joiner: the host's last reported clock (world s)
    private DateTime _w140HostClockUtc = DateTime.MinValue;
    private DateTime _w140HostSkipSinceUtc = DateTime.MinValue;   // joiner: the host's skip began (no done yet)
    private DateTime _w140PullQuietUntilUtc = DateTime.MinValue;
    private DateTime _w140SharedStartUtc = DateTime.MinValue;       // the current shared sleep's Begin
    // the own-world trap
    private volatile bool _w140Separate;
    private long _w140SeparateLogMs, _w140SeparateSayMs;
    private readonly ConcurrentDictionary<int, long> _w140SepDrops = new();
    private readonly ConcurrentDictionary<byte, bool> _w140SepJoinersTold = new();
    private long _w140Asks, _w140AsksIn, _w140Yes, _w140No, _w140Timeouts, _w140Begins, _w140BeginsIn, _w140Starts, _w140StartFallbacks,
                 _w140Stops, _w140Pulls, _w140HeldNative, _w140SepDropped, _w140SepOutDropped, _w140RestSaves;

    private const uint W140ApproveMs = 30_000;
    private const long W140AcceptWaitMs = 180_000;      // an accepter waits this long for the asker to lie down and choose
    private const long W140ApprovedWaitMs = 120_000;    // the asker's picker must open and confirm within this

    private bool W140VoteRequired => Wo140Rules.VoteRequired(_w140On, _combatRoleApplied, _isDamageAuthority, _sharedWorld,
                                                             JoinerSharedEffective, _joinedWorld, Wo134Peers().Count);
    private bool W140JoinBusy => _jj is not null || _joinRx is not null || _joinOutId != 0 || _leaveInProgress || _rejoinPending
                                 || _joinFromWorldOnce || _ownLoadExpected || _rewinding;
    private bool Wo140SeparateNow => Wo140Rules.Separate(_combatRoleApplied, _isDamageAuthority, _hostModeKnown, _hostSharedWorld,
                                                         _where == GameWhere.World, _joinedWorld, W140JoinBusy);
    /// <summary>A shared sleep is under way on this machine (the vote's skip): toasts quiet, the host's report says sleep.</summary>
    private bool W140SharedSkip => Environment.TickCount64 < Interlocked.Read(ref _w140SharedUntilMs);

    private void Wo140OnConnect(CancellationToken ct)
    {
        _w140Connected = true;
        _w140CfgKey = -1;
        _w140Separate = false;
        _combat.OnSleepFrame = f => { _ = Task.Run(() => Wo140OnFrameFromDllAsync(f)); };
        _ = Wo140LoopAsync(ct);
    }

    private async Task Wo140OnDisconnectAsync()
    {
        _w140Connected = false;
        _combat.OnSleepFrame = null;
        lock (_w140Lock) { _w140Mine = null; _w140Collect = null; _w140Prompt = null; _w140Accepted = null; }
        _w140AcceptedJoiners.Clear();
        _w140LocalSkipping = false;
        _w140SaveAfter = false;
        try { await _combat.Wo140ConfigAsync(false); } catch { }
        _w140CfgKey = -1;
        try { await ExecLuaAsync("if KCD2MP_W140Session then KCD2MP_W140Session(false) end"); } catch { }
        try { await ExecLuaAsync("if KCD2MP_W140Separate then KCD2MP_W140Separate(false, \"\") end"); } catch { }
        if (_w140Separate) { _w140Separate = false; if (_joinUiState == "own-world") SetJoinUi("idle", ""); }
    }

    // ---------------------------------------------------------------- the 1 s loop

    private async Task Wo140LoopAsync(CancellationToken ct)
    {
        long lastStats = Environment.TickCount64;
        while (!ct.IsCancellationRequested && _w140Connected)
        {
            try { await Task.Delay(1000, ct); } catch { return; }
            try
            {
                bool holding = Wo136Holding;
                bool vote = W140VoteRequired && !holding;
                await Wo140PushConfigAsync(vote);
                _ = ExecLuaAsync($"if KCD2MP_W140Session then KCD2MP_W140Session({B(vote)}) end");
                await Wo140TimeoutsAsync();
                await Wo140SeparateTickAsync();
                long now = Environment.TickCount64;
                if (now - lastStats >= 60_000) { lastStats = now; Console.WriteLine(Wo140StatsLine()); }
            }
            catch (Exception ex) { Console.WriteLine($"MP-W140 tick failed: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    private async Task Wo140PushConfigAsync(bool on)
    {
        int key = on ? 1 : 0;
        long now = Environment.TickCount64;
        if (key == _w140CfgKey && now - _w140CfgAtMs < 10_000) return;
        bool changed = key != _w140CfgKey;
        var cfg = await _combat.Wo140ConfigAsync(on);
        if (cfg is null)
        {
            if (changed && on) Console.WriteLine("MP-W140 the sleep gate did not answer (no plugin, or not armed) -- the bed is still held by the mod; tried again");
            if (!on) { _w140CfgKey = key; _w140CfgAtMs = now; }
            return;
        }
        _w140CfgKey = key; _w140CfgAtMs = now;
        if (changed && !on && _w140LocalSkipping)
        {
            // the gate went off mid-skip: no more edges will come -- nothing may stay held behind it
            _w140LocalSkipping = false;
            ApplyPendingTimeSkipIfAny();
        }
        if (changed)
            Console.WriteLine(on
                ? $"MP-W140 sleeping together ON -- a sleep or a wait waits for everyone's yes (sleep gate {(cfg.Value.Armed ? "armed" : "NOT ARMED: only the bed is held")})"
                : "MP-W140 sleeping together off (no partner, or not the shared world) -- sleep and wait as the game does");
    }

    private string Wo140StatsLine() => FormattableString.Invariant(
        $"MP-WO140-STATS vote={On(_w140On)} required={On(W140VoteRequired)} separate={On(_w140Separate)} asks_out={_w140Asks} asks_in={_w140AsksIn} yes={_w140Yes} no={_w140No} timeouts={_w140Timeouts} begins_out={_w140Begins} begins_in={_w140BeginsIn} starts={_w140Starts} start_fallbacks={_w140StartFallbacks} stops={_w140Stops} pulls={_w140Pulls} held_native={_w140HeldNative} rest_saves={_w140RestSaves} separate_dropped={_w140SepDropped} separate_out_dropped={_w140SepOutDropped} woke_kept={Interlocked.Read(ref _w157WokeKept)}");

    private async Task Wo140SendAsync(byte target, byte kind, uint tok, string text)
    {
        try { await WriteJoinAsync(new LootMsg(kind, tok, text).BuildUp(Protocol.SleepVoteUp, target)); }
        catch (Exception ex) { Console.WriteLine($"MP-W140 {Protocol.SleepVoteName(kind)} not sent: {ex.Message}"); }
    }

    private string W140Name(byte id) => id == _myGhostId ? "you" : _ghostNames.TryGetValue(id, out var n) ? n : $"player {id}";

    // ---------------------------------------------------------------- the asker

    /// <summary>This player picked Sleep at a bed (source lua) or the gate held a picker (native).</summary>
    private async Task Wo140LocalAskAsync(string kind, bool save, string source)
    {
        if (!W140VoteRequired)
        {
            // no vote (solo, a partner left in between): let it carry on as the game does
            Console.WriteLine($"MP-W140 {kind} from {source}: no vote needed now -- carries on as the game does");
            await Wo140GoLocalAsync(source);
            return;
        }
        (uint Id, byte Asker, string Kind, byte ReplyTo, long DeadlineMs)? theirs;
        lock (_w140Lock)
        {
            if (_w140Mine is not null) { Console.WriteLine($"MP-W140 {kind} from {source}: a vote of this player's is already open -- nothing new asked"); return; }
            theirs = _w140Prompt;
        }
        if (theirs is { } t)
        {
            // they asked first: this player wants to sleep too -- that is a yes to theirs
            Console.WriteLine($"MP-W140 {kind} from {source} while {W140Name(t.Asker)}'s ask is on screen: taken as YES to theirs -- you sleep when they choose how long");
            await Wo140AnswerLocalAsync(t.Id, "yes", "asked too");
            await Wo140DropLocalAsync(source, $"{W140Name(t.Asker)} asked first. You'll sleep together when they choose how long.");
            return;
        }
        var v = new Wo140Rules.Vote
        {
            Id = Wo140Rules.VoteId(_myGhostId, Interlocked.Increment(ref _w140Tok)), Asker = _myGhostId, Kind = kind, Save = save, Mine = true,
            DeadlineMs = Environment.TickCount64 + Protocol.SleepVoteTimeoutSeconds * 1000L,
        };
        bool host = _isDamageAuthority;
        if (host) foreach (byte g in VotingPartners()) v.Members.Add(g);   // WO-144: connected and in this world
        else v.Members.Add(_hostModeFrom != 0xFF ? _hostModeFrom : (byte)0xFF);
        lock (_w140Lock) { _w140Mine = v; _w140MineSource = source; _w140MineApproved = false; }
        Interlocked.Increment(ref _w140Asks);
        // the waiting line FIRST: an answer can come back before a later ExecuteString (observed: a
        // fast no dropped the hold, then "Waiting for other players..." went up and stayed)
        await ExecLuaAsync($"if KCD2MP_W140Waiting then KCD2MP_W140Waiting(true, \"{EscapeLua(Wo140Rules.WaitingText)}\") end");
        string text = Wo140Text.Ask(kind, _myGhostId, save);
        if (host) foreach (byte g in v.Members) await Wo140SendAsync(g, Protocol.SleepAsk, v.Id, text);
        else await Wo140SendAsync(Protocol.JoinTargetHost, Protocol.SleepAsk, v.Id, text);
        Console.WriteLine($"MP-W140 this player wants to {kind} (from {source}{(save ? ", a bed that saves" : "")}): vote 0x{v.Id:X8} asked of {(host ? $"{v.Members.Count} joiner(s)" : "the host")} -- nothing happens until everyone says yes ({Protocol.SleepVoteTimeoutSeconds} s)");
    }

    /// <summary>Everyone said yes: the held bed carries on (the lie-down, the picker), or the kept picker opens again.</summary>
    private async Task Wo140GoLocalAsync(string source)
    {
        bool? replayed = await _combat.Wo140ApproveAsync(W140ApproveMs);
        await ExecLuaAsync("if KCD2MP_W140Go then KCD2MP_W140Go() end");
        if (source == "native")
            Console.WriteLine($"MP-W140 the kept picker {(replayed == true ? "opened again" : replayed == false ? "did NOT open again (the game said no)" : "-- the DLL did not answer")}");
    }

    private async Task Wo140DropLocalAsync(string source, string message)
    {
        await _combat.Wo140DropAsync();
        await ExecLuaAsync($"if KCD2MP_W140Drop then KCD2MP_W140Drop(\"{EscapeLua(message)}\", {B(source == "native")}) end");
    }

    private async Task Wo140EvaluateMineAsync()
    {
        Wo140Rules.Vote? v; string src;
        lock (_w140Lock) { v = _w140Mine; src = _w140MineSource; }
        if (v is null || _w140MineApproved) return;
        var verdict = v.Evaluate(Environment.TickCount64);
        if (verdict == Wo140Rules.Verdict.Pending) return;
        if (verdict == Wo140Rules.Verdict.Go)
        {
            lock (_w140Lock) { _w140MineApproved = true; _w140MineApprovedMs = Environment.TickCount64; }
            Interlocked.Increment(ref _w140Yes);
            Console.WriteLine($"MP-W140 vote 0x{v.Id:X8}: everyone said yes -- this player's {v.Kind} carries on (the lie-down and the picker); the others' sleep starts when the length is chosen");
            await ExecLuaAsync("if KCD2MP_W140Waiting then KCD2MP_W140Waiting(false, \"\") end");
            await Wo140GoLocalAsync(src);
            return;
        }
        if (v.NoReason == "timeout") Interlocked.Increment(ref _w140Timeouts); else Interlocked.Increment(ref _w140No);
        lock (_w140Lock) { if (ReferenceEquals(_w140Mine, v)) _w140Mine = null; }
        Console.WriteLine($"MP-W140 vote 0x{v.Id:X8}: {v.NoReason} -- nothing happens: no sleep, no time skip, no rest; this player is back as he was");
        foreach (var (who, a) in v.Answers)
            if (a == "yes") await Wo140SendCancelAsync(who, v, "refused");
        await Wo140DropLocalAsync(src, Wo140Rules.NotReadyText);
    }

    private async Task Wo140SendCancelAsync(byte member, Wo140Rules.Vote v, string why)
    {
        if (member == Wo140Rules.LocalMember) return;
        await Wo140SendAsync(_isDamageAuthority ? member : Protocol.JoinTargetHost, Protocol.SleepCancel, v.Id, Wo140Text.Cancel(why, v.Asker));
    }

    // ---------------------------------------------------------------- the others

    private async Task Wo140OnFrameAsync(byte src, byte[] body)
    {
        if (!LootMsg.TryDecode(body, out var m)) { Console.WriteLine($"MP-W140 malformed sleep vote from ghost {src} (dropped)"); return; }
        switch (m.Kind)
        {
            case Protocol.SleepAsk:
                if (Wo140Text.TryAsk(m.Text, out var kind, out byte asker, out bool save)) await Wo140OnAskAsync(src, m.Tok, kind, asker, save);
                return;
            case Protocol.SleepAnswer:
                if (Wo140Text.TryAnswer(m.Text, out var ans, out byte a2)) await Wo140OnAnswerAsync(src, m.Tok, ans, a2);
                return;
            case Protocol.SleepBegin:
                if (Wo140Text.TryBegin(m.Text, out var bk, out byte ba, out float hours, out bool bs)) await Wo140OnBeginAsync(src, m.Tok, bk, ba, hours, bs);
                return;
            case Protocol.SleepCancel:
                if (Wo140Text.TryCancel(m.Text, out var why, out byte ca)) await Wo140OnCancelAsync(src, m.Tok, why, ca);
                return;
        }
        Console.WriteLine($"MP-W140 unknown sleep vote kind {m.Kind} from ghost {src} (dropped)");
    }

    private async Task Wo140OnAskAsync(byte src, uint id, string kind, byte asker, bool save)
    {
        Interlocked.Increment(ref _w140AsksIn);
        bool host = _isDamageAuthority;
        if (!W140VoteRequired || Wo136Holding)
        {
            // loading, or not in the shared world: this player cannot sleep with them now
            Console.WriteLine($"MP-W140 {W140Name(asker)} asks to {kind} (vote 0x{id:X8}) -- this game cannot join now ({(Wo136Holding ? "loading" : "not in the shared world")}): busy");
            await Wo140SendAsync(host ? src : Protocol.JoinTargetHost, Protocol.SleepAnswer, id, Wo140Text.Answer("busy", asker));
            return;
        }
        if (host)
        {
            // a joiner asks: the host collects everyone's answer (its own player + every other joiner)
            var v = new Wo140Rules.Vote
            {
                Id = id, Asker = asker, Kind = kind, Save = save, ReplyTo = src,
                DeadlineMs = Environment.TickCount64 + Protocol.SleepVoteTimeoutSeconds * 1000L,
            };
            v.Members.Add(Wo140Rules.LocalMember);
            foreach (byte g in VotingPartners()) if (g != src) v.Members.Add(g);   // WO-144
            lock (_w140Lock) _w140Collect = v;
            foreach (byte g in v.Members) if (g != Wo140Rules.LocalMember) await Wo140SendAsync(g, Protocol.SleepAsk, id, Wo140Text.Ask(kind, asker, save));
            Console.WriteLine($"MP-W140 {W140Name(asker)} wants to {kind} (vote 0x{id:X8}) -- asking this player{(v.Members.Count > 1 ? $" and {v.Members.Count - 1} other joiner(s)" : "")}");
        }
        else Console.WriteLine($"MP-W140 {W140Name(asker)} wants to {kind} (vote 0x{id:X8}) -- asking this player");
        Wo140Rules.Vote? mine;
        lock (_w140Lock) mine = _w140Mine;
        if (mine is not null && !_w140MineApproved)
        {
            // both want to sleep: yes to theirs; this player's own ask gets their yes the same way
            await Wo140AnswerLocalAsync(id, "yes", "this player asked too", asker, kind, host ? (byte)0xFF : Protocol.JoinTargetHost);
            return;
        }
        if (_w140LocalSkipping) { await Wo140AnswerLocalAsync(id, "busy", "this game's own skip is running", asker, kind, host ? (byte)0xFF : Protocol.JoinTargetHost); return; }
        lock (_w140Lock) _w140Prompt = (id, asker, kind, host ? (byte)0xFF : Protocol.JoinTargetHost, Environment.TickCount64 + Protocol.SleepVoteTimeoutSeconds * 1000L);
        await ExecLuaAsync(FormattableString.Invariant(
            $"if KCD2MP_W140Prompt then KCD2MP_W140Prompt({id}, \"{EscapeLua(Wo140Rules.PromptText(W140Name(asker), kind))}\", {Protocol.SleepVoteTimeoutSeconds}) end"));
    }

    /// <summary>This player's answer to a prompt (F11 / F12, mp_sleep_yes / mp_sleep_no, or the timeout).</summary>
    private async Task Wo140AnswerLocalAsync(uint id, string answer, string why, byte? askerArg = null, string? kindArg = null, byte? replyArg = null)
    {
        (uint Id, byte Asker, string Kind, byte ReplyTo, long DeadlineMs)? p;
        lock (_w140Lock)
        {
            p = _w140Prompt;
            if (p is { } cur && cur.Id == id) _w140Prompt = null;
            else if (askerArg is byte aa) p = (id, aa, kindArg ?? "sleep", replyArg ?? Protocol.JoinTargetHost, 0);
            else return;
        }
        var pr = p!.Value;
        await ExecLuaAsync($"if KCD2MP_W140PromptHide then KCD2MP_W140PromptHide({id}) end");
        Console.WriteLine($"MP-W140 this player: {answer.ToUpperInvariant()} to {W140Name(pr.Asker)}'s {pr.Kind} (vote 0x{id:X8}; {why})");
        if (answer == "yes")
            lock (_w140Lock) _w140Accepted = (id, pr.Asker, pr.Kind, Environment.TickCount64 + W140AcceptWaitMs);
        Wo140Rules.Vote? col;
        lock (_w140Lock) col = _w140Collect;
        if (_isDamageAuthority && col is not null && col.Id == id)
        {
            col.Answer(Wo140Rules.LocalMember, answer);
            await Wo140EvaluateCollectAsync();
            return;
        }
        await Wo140SendAsync(_isDamageAuthority ? pr.Asker : Protocol.JoinTargetHost, Protocol.SleepAnswer, id, Wo140Text.Answer(answer, pr.Asker));
        if (answer == "yes")
            await Wo140SayAsync($"You'll sleep when {W140Name(pr.Asker)} chooses how long.");
    }

    private async Task Wo140OnAnswerAsync(byte src, uint id, string answer, byte asker)
    {
        Wo140Rules.Vote? mine, col;
        lock (_w140Lock) { mine = _w140Mine; col = _w140Collect; }
        if (mine is not null && mine.Id == id)
        {
            // a joiner asker gets one answer: the host's, for everyone
            byte member = _isDamageAuthority ? src : mine.Members.First();
            mine.Answer(member, answer);
            if (answer == "yes" && _isDamageAuthority) _w140AcceptedJoiners[src] = id;
            Console.WriteLine($"MP-W140 vote 0x{id:X8}: {W140Name(src)} says {answer}{(_isDamageAuthority ? "" : " (the host, for everyone)")}");
            await Wo140EvaluateMineAsync();
            return;
        }
        if (_isDamageAuthority && col is not null && col.Id == id)
        {
            col.Answer(src, answer);
            if (answer == "yes") _w140AcceptedJoiners[src] = id;
            Console.WriteLine($"MP-W140 vote 0x{id:X8} ({W140Name(col.Asker)}'s): {W140Name(src)} says {answer}");
            await Wo140EvaluateCollectAsync();
            return;
        }
        Console.WriteLine($"MP-W140 an answer to vote 0x{id:X8} from ghost {src} ({answer}) -- no such open vote here (late)");
    }

    private async Task Wo140EvaluateCollectAsync()
    {
        Wo140Rules.Vote? v;
        lock (_w140Lock) v = _w140Collect;
        if (v is null) return;
        var verdict = v.Evaluate(Environment.TickCount64);
        if (verdict == Wo140Rules.Verdict.Pending) return;
        lock (_w140Lock) if (ReferenceEquals(_w140Collect, v)) _w140Collect = null;
        string answer = verdict == Wo140Rules.Verdict.Go ? "yes" : v.NoReason is "no" or "timeout" or "busy" ? v.NoReason : "no";
        await Wo140SendAsync(v.ReplyTo, Protocol.SleepAnswer, v.Id, Wo140Text.Answer(answer, v.Asker));
        Console.WriteLine($"MP-W140 vote 0x{v.Id:X8} ({W140Name(v.Asker)}'s): {(verdict == Wo140Rules.Verdict.Go ? "everyone said yes" : v.NoReason)} -- told the asker");
        if (verdict != Wo140Rules.Verdict.Go)
        {
            foreach (var (who, a) in v.Answers)
                if (a == "yes" && who != Wo140Rules.LocalMember) await Wo140SendAsync(who, Protocol.SleepCancel, v.Id, Wo140Text.Cancel("refused", v.Asker));
            lock (_w140Lock) if (_w140Accepted is { } acc && acc.Id == v.Id) _w140Accepted = null;
            foreach (var (j, vid) in _w140AcceptedJoiners.ToArray()) if (vid == v.Id) _w140AcceptedJoiners.TryRemove(j, out _);
            (uint Id, byte Asker, string Kind, byte ReplyTo, long DeadlineMs)? pr;
            lock (_w140Lock) { pr = _w140Prompt; if (pr is { } q && q.Id == v.Id) _w140Prompt = null; }
            if (pr is { } q2 && q2.Id == v.Id) await ExecLuaAsync($"if KCD2MP_W140PromptHide then KCD2MP_W140PromptHide({v.Id}) end");
        }
    }

    // ---------------------------------------------------------------- the Begin: everyone's sleep screen

    /// <summary>The asker confirmed the length (the DLL's "began"): everyone who said yes starts theirs.</summary>
    private async Task Wo140OnLocalBeganAsync(Wo140Frame f)
    {
        _w140LocalSkipping = true;
        Wo140Rules.Vote? v; bool approved;
        lock (_w140Lock) { v = _w140Mine; approved = _w140MineApproved; }
        if (v is null || !approved)
        {
            if (W140SharedSkip) Console.WriteLine(FormattableString.Invariant($"MP-W140 this game's own sleep screen runs ({Wo140Rules.KindOfSkipId(f.Id)}, {f.Hours:0.##} h) -- the shared sleep"));
            return;
        }
        lock (_w140Lock) _w140Mine = null;
        if (Wo140Rules.HoursOk(f.Hours) is not float hours)
        {
            Console.WriteLine(FormattableString.Invariant($"MP-W140 vote 0x{v.Id:X8}: the picker began with {f.Hours} h -- not a length the others can follow (nothing sent)"));
            return;
        }
        Wo140MarkShared(v.Kind, _myGhostId, hours);
        _w140SaveAfter = false;   // the host's own bed saves by itself (the game's own sleep)
        Interlocked.Increment(ref _w140Begins);
        string text = Wo140Text.Begin(v.Kind, _myGhostId, hours, v.Save);
        if (_isDamageAuthority) foreach (byte g in v.Members) await Wo140SendAsync(g, Protocol.SleepBegin, v.Id, text);
        else await Wo140SendAsync(Protocol.JoinTargetHost, Protocol.SleepBegin, v.Id, text);
        Console.WriteLine(FormattableString.Invariant($"MP-W140 vote 0x{v.Id:X8}: this player chose {hours:0.##} h -- everyone's sleep starts now"));
    }

    private void Wo140MarkShared(string kind, byte asker, float hours)
    {
        _w140SharedStartUtc = DateTime.UtcNow;
        _w140SharedKind = kind;
        _w140SharedAsker = asker;
        // generous: a skip of h hours takes well under h * 40 s here; cleared on the end edge
        Interlocked.Exchange(ref _w140SharedUntilMs, Environment.TickCount64 + 60_000 + (long)(hours * 40_000));
    }

    private async Task Wo140OnBeginAsync(byte src, uint id, string kind, byte asker, float hours, bool save)
    {
        Interlocked.Increment(ref _w140BeginsIn);
        bool host = _isDamageAuthority;
        if (host)
        {
            // a joiner asker's Begin: every other joiner who said yes starts too
            foreach (var (j, vid) in _w140AcceptedJoiners.ToArray())
                if (vid == id && j != src) await Wo140SendAsync(j, Protocol.SleepBegin, id, Wo140Text.Begin(kind, asker, hours, save));
            foreach (var (j, vid) in _w140AcceptedJoiners.ToArray()) if (vid == id) _w140AcceptedJoiners.TryRemove(j, out _);
        }
        (uint Id, byte Asker, string Kind, long UntilMs)? acc;
        lock (_w140Lock) { acc = _w140Accepted; if (acc is { } a && a.Id == id) _w140Accepted = null; }
        if (acc is not { } ok || ok.Id != id)
        {
            Console.WriteLine(FormattableString.Invariant($"MP-W140 {W140Name(asker)} began a {hours:0.##} h {kind} (vote 0x{id:X8}) -- this player did not say yes to it: nothing here"));
            return;
        }
        Wo140MarkShared(kind, asker, hours);
        _w140SaveAfter = host && save;   // a joiner's bed that saves: the host makes the game's rest save afterwards
        await Wo140StartOwnAsync(kind, hours, $"{W140Name(asker)} chose {hours:0.##} h");
    }

    /// <summary>This game's own sleep screen for the vote's hours, wherever the player stands.</summary>
    private async Task Wo140StartOwnAsync(string kind, float hours, string why)
    {
        byte id = Wo140Rules.AccepterSkipId(kind);
        byte? r = await _combat.Wo140StartAsync(id, hours);
        string how;
        if (r == 1) { how = kind == "wait" ? "the game's own wait" : "the game's own sleep (its screen, its rest)"; Interlocked.Increment(ref _w140Starts); }
        else if (r == 0)
        {
            // the game said no (trespass, combat, ...): its forced sleep screen still runs, so the clock and the screen match
            byte? r2 = await _combat.Wo140StartAsync(14, hours);
            how = r2 == 1 ? "the game refused a real sleep here (trespass, a fight...): its forced sleep screen runs instead, without the rest"
                          : $"the game refused (sleep {r}, forced screen {r2?.ToString(CultureInfo.InvariantCulture) ?? "no answer"}) -- the clock follows the host's anyway";
            if (r2 == 1) Interlocked.Increment(ref _w140StartFallbacks);
        }
        else how = r == 2 ? "this game's own skip is already running" : r is null ? "the DLL did not answer -- the clock follows the host's anyway" : $"refused ({r})";
        Console.WriteLine(FormattableString.Invariant($"MP-W140 {why}: this player's {kind} for {hours:0.##} h -- {how}"));
    }

    private async Task Wo140OnCancelAsync(byte src, uint id, string why, byte asker)
    {
        bool host = _isDamageAuthority;
        if (why == "woke")
        {
            // the host or the asker woke: everyone wakes now
            if (host) foreach (byte g in Wo134Peers()) if (g != src) await Wo140SendAsync(g, Protocol.SleepCancel, id, Wo140Text.Cancel("woke", asker));
            if (_w140LocalSkipping)
            {
                // WO-157 3b.5: one player waking never cuts another's rest short. Each game skips at its own speed: the
                // field's host finished its 12 h in 9.1 s and its "woke" stopped the joiner's sleep 14 s in, at about
                // 10.5 of 12 hours, and his clock was then set forward while he was awake (food and energy fell with no
                // rest). The sleep here runs to its own end; the host's clock is met after it (held clock writes).
                Interlocked.Increment(ref _w157WokeKept);
                Console.WriteLine($"MP-W140 {W140Name(src)} woke -- this player's own {_w140SharedKind} runs to its end (WO-157: a partner waking never cuts this rest short); the clock meets the host's after it");
            }
            return;
        }
        if (host)
        {
            foreach (var (j, vid) in _w140AcceptedJoiners.ToArray())
                if (vid == id && j != src) { await Wo140SendAsync(j, Protocol.SleepCancel, id, Wo140Text.Cancel(why, asker)); _w140AcceptedJoiners.TryRemove(j, out _); }
            lock (_w140Lock) if (_w140Collect is { } c && c.Id == id) _w140Collect = null;
        }
        (uint Id, byte Asker, string Kind, byte ReplyTo, long DeadlineMs)? pr;
        lock (_w140Lock)
        {
            if (_w140Accepted is { } a && a.Id == id) _w140Accepted = null;
            pr = _w140Prompt; if (pr is { } p && p.Id == id) _w140Prompt = null;
        }
        if (pr is { } p2 && p2.Id == id) await ExecLuaAsync($"if KCD2MP_W140PromptHide then KCD2MP_W140PromptHide({id}) end");
        Console.WriteLine($"MP-W140 {W140Name(asker)}'s vote 0x{id:X8} is over ({why}) -- nothing happens here");
    }

    // ---------------------------------------------------------------- the DLL's edges

    private async Task Wo140OnFrameFromDllAsync(Wo140Frame f)
    {
        if (f.Kind == Wo140Frame.KindHeld)
        {
            Interlocked.Increment(ref _w140HeldNative);
            string kind = Wo140Rules.KindOfSkipId(f.Id);
            Console.WriteLine($"MP-W140 the game's {kind} picker (id {f.Id}) was held by the gate -- a vote first");
            await Wo140LocalAskAsync(kind, false, "native");
            return;
        }
        switch (f.Edge)
        {
            case Wo140Frame.EdgeOpened:
                Console.WriteLine($"MP-W140 this game's {Wo140Rules.KindOfSkipId(f.Id)} picker is open");
                return;
            case Wo140Frame.EdgeBegan:
                _w154SkipBeganKind = Wo140Rules.KindOfSkipId(f.Id);   // WO-154 6.3: the ended edge reads id -1 (live L6)
                _ = ExecLuaAsync($"if KCD2MP_W157RestLine then KCD2MP_W157RestLine('start', {f.Id}) end");   // WO-157: rest measured
                await Wo140OnLocalBeganAsync(f);
                return;
            case Wo140Frame.EdgeBackedOut:
            {
                Wo140Rules.Vote? v; bool approved;
                lock (_w140Lock) { v = _w140Mine; approved = _w140MineApproved; if (approved) _w140Mine = null; }
                if (v is not null && approved)
                {
                    Console.WriteLine($"MP-W140 vote 0x{v.Id:X8}: this player backed out of the picker -- nobody sleeps");
                    foreach (byte m in v.Members) await Wo140SendCancelAsync(m, v, "backed-out");
                }
                return;
            }
            case Wo140Frame.EdgeEnded:
                _w140LocalSkipping = false;
                _ = ExecLuaAsync("if KCD2MP_W157RestLine then KCD2MP_W157RestLine('end') end");     // WO-157: rest measured
                Wo154NoteLocalSkipEnded(_w154SkipBeganKind ?? Wo140Rules.KindOfSkipId(f.Id));   // WO-154 6.3: a set-back right after it is told
                _w154SkipBeganKind = null;
                await Wo140OnLocalEndedAsync();
                return;
        }
    }

    private async Task Wo140OnLocalEndedAsync()
    {
        ApplyPendingTimeSkipIfAny();   // the host's clock that arrived during this game's own skip (held, see ApplyTimeSkipAsync)
        if (!W140SharedSkip) return;
        Interlocked.Exchange(ref _w140SharedUntilMs, Environment.TickCount64 + 15_000);   // the host's report and the pin still to come
        bool host = _isDamageAuthority;
        uint tok = 0;
        // the host's clock is the world's, and the asker chose the length: either one waking wakes everyone
        if (host) foreach (byte g in Wo134Peers()) await Wo140SendAsync(g, Protocol.SleepCancel, tok, Wo140Text.Cancel("woke", _w140SharedAsker));
        else if (_w140SharedAsker == _myGhostId) await Wo140SendAsync(Protocol.JoinTargetHost, Protocol.SleepCancel, tok, Wo140Text.Cancel("woke", _myGhostId));
        Console.WriteLine($"MP-W140 this player woke ({_w140SharedKind}){(host ? " -- the others wake too; the world's clock is this one" : _w140SharedAsker == _myGhostId ? " -- the others wake too" : "")}");
        if (host && _w140SaveAfter)
        {
            _w140SaveAfter = false;
            Interlocked.Increment(ref _w140RestSaves);
            Console.WriteLine("MP-W140 the sleeper's bed saves: the host makes the game's own rest save now (the joiner's snapshot pairs with it)");
            await ExecLuaAsync("if KCD2MP_W140RestSave then KCD2MP_W140RestSave() end");
        }
        if (!host) _ = ExecLuaAsync("if KCD2MP_ReportWorldTime then KCD2MP_ReportWorldTime() end");   // the pin: the next reading meets the host's clock
    }

    private async Task Wo140TimeoutsAsync()
    {
        long now = Environment.TickCount64;
        (uint Id, byte Asker, string Kind, byte ReplyTo, long DeadlineMs)? p;
        lock (_w140Lock) p = _w140Prompt;
        if (p is { } pr && pr.DeadlineMs > 0 && now >= pr.DeadlineMs)
        {
            Interlocked.Increment(ref _w140Timeouts);
            await Wo140AnswerLocalAsync(pr.Id, "timeout", $"no answer in {Protocol.SleepVoteTimeoutSeconds} s");
        }
        await Wo140EvaluateMineAsync();
        await Wo140EvaluateCollectAsync();
        Wo140Rules.Vote? v; bool approved; long at; string src;
        lock (_w140Lock) { v = _w140Mine; approved = _w140MineApproved; at = _w140MineApprovedMs; src = _w140MineSource; }
        if (v is not null && approved && now - at > W140ApprovedWaitMs)
        {
            lock (_w140Lock) if (ReferenceEquals(_w140Mine, v)) _w140Mine = null;
            Console.WriteLine($"MP-W140 vote 0x{v.Id:X8}: approved but no length was chosen in {W140ApprovedWaitMs / 1000} s -- the others are released");
            foreach (byte m in v.Members) await Wo140SendCancelAsync(m, v, "backed-out");
        }
        lock (_w140Lock) if (_w140Accepted is { } a && now > a.UntilMs) { Console.WriteLine($"MP-W140 vote 0x{a.Id:X8}: {W140Name(a.Asker)} never chose a length -- released"); _w140Accepted = null; }
        // members who left: their answer can never come
        foreach (var vote in new[] { v, _w140Collect })
            if (vote is not null)
                foreach (byte m in vote.Members.ToArray())
                    if (m != Wo140Rules.LocalMember && m != 0xFF && !Wo144StillVoting(m))   // WO-144: left, or loading / in its own world now
                    {
                        vote.Left(m);
                        Console.WriteLine($"MP-W140 vote 0x{vote.Id:X8}: {W140Name(m)} {(IsLivePeer(m) ? "is not in this world any more" : "left")} -- dropped from the vote (the others' answers decide)");
                    }
        // WO-144: a joiner's ask whose asker left has nobody to answer: dropped
        Wo140Rules.Vote? col;
        lock (_w140Lock) col = _w140Collect;
        if (col is not null && col.ReplyTo != 0xFF && !IsLivePeer(col.ReplyTo))
        {
            lock (_w140Lock) if (ReferenceEquals(_w140Collect, col)) _w140Collect = null;
            Console.WriteLine($"MP-W140 vote 0x{col.Id:X8}: the asker (ghost {col.ReplyTo}) left -- the vote is dropped");
            foreach (var (who, a) in col.Answers)
                if (a == "yes" && who != Wo140Rules.LocalMember) await Wo140SendAsync(who, Protocol.SleepCancel, col.Id, Wo140Text.Cancel("backed-out", col.Asker));
            (uint Id, byte Asker, string Kind, byte ReplyTo, long DeadlineMs)? cpr;
            lock (_w140Lock) { cpr = _w140Prompt; if (cpr is { } cq && cq.Id == col.Id) _w140Prompt = null; }
            if (cpr is { } cq2 && cq2.Id == col.Id) await ExecLuaAsync($"if KCD2MP_W140PromptHide then KCD2MP_W140PromptHide({col.Id}) end");
        }
    }

    // ---------------------------------------------------------------- events from the mod

    private void Wo140OnEvent(string name, string? arg)
    {
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (name)
        {
            case "w140_ask":        // <sleep|wait> <save 0|1>
                if (f.Length >= 2 && Wo140Text.IsKind(f[0])) _ = Wo140LocalAskAsync(f[0], f[1] == "1", "lua");
                return;
            case "w140_answer":     // <voteId> <yes|no>
                if (f.Length >= 2 && uint.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out uint vid) && f[1] is "yes" or "no")
                    _ = Wo140AnswerLocalAsync(vid, f[1], "this player's key");
                return;
            case "w140_cfg":        // vote=on|off (mp_sleep_vote)
                if (f.Length >= 1 && f[0].StartsWith("vote=", StringComparison.Ordinal))
                {
                    bool on = f[0] == "vote=on";
                    if (on != _w140On) Console.WriteLine($"MP-W140 mp_sleep_vote {(on ? "ON" : "OFF")}");
                    _w140On = on;
                    _w140CfgKey = -1;
                }
                return;
            case "w140_status":
                _ = Task.Run(async () =>
                {
                    Console.WriteLine(Wo140StatsLine());
                    Console.WriteLine(await _combat.Wo140StatusAsync() ?? "WO140-NATIVE: no answer");
                });
                return;
        }
    }

    // ---------------------------------------------------------------- one clock (the joiner)

    /// <summary>The TimeSkipDown handler: the host's clock as it reported it (done / sync), and its skips.</summary>
    private void Wo140NoteHostClock(byte source, byte phase, uint worldTime)
    {
        if (_isDamageAuthority || !_combatRoleApplied) return;
        int hostId = _hostModeKnown && _hostModeFrom != 0xFF ? _hostModeFrom : -1;
        if (hostId >= 0 && source != hostId) return;
        if (phase == Protocol.TimeSkipPhaseStart) { _w140HostSkipSinceUtc = DateTime.UtcNow; return; }
        if (worldTime == 0) return;
        _w140HostSkipSinceUtc = DateTime.MinValue;
        _w140HostClock = worldTime;
        _w140HostClockUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Every clock reading on a joiner in the host's world: ahead of the host's = pulled back at
    /// once (native, this copy only). Returns true when the reading was replaced by the pulled clock.
    /// </summary>
    private bool Wo140CheckClock(uint worldTime)
    {
        if (!_joinedWorld || _isDamageAuthority || !_combatRoleApplied || _w140LocalSkipping || _localSkipActive || Wo136Holding) return false;
        if (DateTime.UtcNow < _w140PullQuietUntilUtc) return false;
        // a shared sleep: the host's own skip runs too, and its result is still to come (observed: the
        // joiner's sleep ended first and was pulled back to the host's pre-sleep clock). No pull until the
        // host has reported after the Begin, or the shared window is over.
        if (W140SharedSkip && _w140HostClockUtc < _w140SharedStartUtc) return false;
        bool hostSkipping = _w140HostSkipSinceUtc != DateTime.MinValue && (DateTime.UtcNow - _w140HostSkipSinceUtc).TotalMinutes < 10;
        double age = (DateTime.UtcNow - _w140HostClockUtc).TotalSeconds;
        if (Wo140Rules.PullBackTarget(worldTime, _w140HostClock, age, hostSkipping, WorldTimeRatio) is not uint target) return false;
        _w140PullQuietUntilUtc = DateTime.UtcNow.AddSeconds(20);
        _ = Task.Run(async () =>
        {
            var r = await _combat.Wo140PullAsync(worldTime, target);
            if (r is { Result: 1 } ok)
            {
                Interlocked.Increment(ref _w140Pulls);
                _lastPolledWorldTime = ok.After;
                _lastPollUtc = DateTime.UtcNow;
                _suppressJumpUntilUtc = DateTime.UtcNow.AddSeconds(30);
                Console.WriteLine(FormattableString.Invariant($"MP-W140 this clock was {ok.Before - target} s ahead of the host's -- pulled back {ok.Before} -> {ok.After} (one clock: the host's)"));
                Wo154AfterPull(ok.Before - target);   // WO-154 6.3: this player's own wait or sleep set back: told on screen
            }
            else Console.WriteLine(FormattableString.Invariant($"MP-W140 this clock is ahead of the host's ({worldTime} > {target}) but the pull {(r is null ? "got no answer (not armed?)" : r.Value.Result == 2 ? "was refused: the calendar went back past this reading (a load)" : r.Value.Result == 3 ? "waits: this game's own skip is running (tried again after it)" : "was not needed")}"));
        });
        return true;
    }

    // ---------------------------------------------------------------- the own-world trap

    private async Task Wo140SeparateTickAsync()
    {
        W160NoteJoinActivity();   // WO-160 2: a join's activity keeps the verdict quiet for 60 s after it ends
        // WO-160 2: the verdict needs the mod's own, recent "world" answer and no join activity -- a stale "World" kept from a
        // game that crashed, a game at its main menu, a join's load: silent.
        bool sep = Wo140SeparateNow && await W160SeparateAllowedAsync();
        long now = Environment.TickCount64;
        if (sep != _w140Separate)
        {
            _w140Separate = sep;
            if (sep)
            {
                Console.WriteLine(Wo140Rules.SeparateLogLine + " -- nothing of the host's world is applied here (no NPC stream, puppets, copy guard, stand-ins, leash, loot, quests or crime); the ghosts still show each other");
                _w140SeparateLogMs = now;
                SetJoinUi("own-world", Wo140Rules.SeparateText);
                await Wo140SendLeashStateSoonAsync();
            }
            else
            {
                Console.WriteLine($"MP-JOIN joiner: no longer in its own world while connected ({(_joinedWorld ? "joined the host's world" : "not in a world, or a join runs")}) -- the separate mode ends");
                if (_joinUiState == "own-world") SetJoinUi("idle", "");
                await ExecLuaAsync("if KCD2MP_W140Separate then KCD2MP_W140Separate(false, \"\") end");
            }
        }
        if (!sep) return;
        if (now - _w140SeparateLogMs >= 60_000)
        {
            _w140SeparateLogMs = now;
            string drops = string.Join(",", _w140SepDrops.Select(kv => $"{Wo140Rules.TypeName(kv.Key)}:{kv.Value}"));
            Console.WriteLine($"{Wo140Rules.SeparateLogLine} (still; dropped from the host: {(drops.Length > 0 ? drops : "none")}; not sent to it: {_w140SepOutDropped})");
        }
        if (now - _w140SeparateSayMs >= 5_000)   // the game's centered text lasts 5 s: always on screen
        {
            _w140SeparateSayMs = now;
            await ExecLuaAsync($"if KCD2MP_W140Separate then KCD2MP_W140Separate(true, \"{EscapeLua(Wo140Rules.SeparateText)}\") end");
        }
    }

    private async Task Wo140SendLeashStateSoonAsync()
    {
        try { await Wo114SendStateAsync(JoinerLeashFlags()); } catch { }
    }

    /// <summary>A frame of the host's world while this joiner is in its own: dropped (counted). At the reader and the processor.</summary>
    private bool Wo140DropSeparate(int type)
    {
        if (!_w140Separate || !Wo140Rules.DroppedWhenSeparate(type)) return false;
        Interlocked.Increment(ref _w140SepDropped);
        _w140SepDrops.AddOrUpdate(type, 1, (_, n) => n + 1);
        return true;
    }

    /// <summary>This joiner's own world is not the host's: none of it goes to the host (hits, loot asks).</summary>
    private bool Wo140HoldOutbound(string what)
    {
        if (!_w140Separate) return false;
        long n = Interlocked.Increment(ref _w140SepOutDropped);
        if (n == 1 || n % 50 == 0) Console.WriteLine($"MP-W140 {what} not sent to the host: this game is in its own world, not the host's ({n})");
        return true;
    }

    /// <summary>The host: a joiner in its own world is not leashed (it is not in this world).</summary>
    private bool Wo140HostSkipsLeash(byte joiner, ushort flags)
    {
        bool sep = (flags & Protocol.LeashFlagSeparate) != 0;
        if (sep && _w140SepJoinersTold.TryAdd(joiner, true))
            Console.WriteLine($"MP-LEASH host: joiner {joiner} is in its own world (connected from its own save) -- not leashed, no pull held; it is told how to join");
        else if (!sep && _w140SepJoinersTold.TryRemove(joiner, out _))
            Console.WriteLine($"MP-LEASH host: joiner {joiner} is no longer in its own world -- the leash applies again");
        return sep;
    }

    private Task Wo140SayAsync(string text) =>
        ExecLuaAsync($"if KCD2MP_ShowNativeToast then KCD2MP_ShowNativeToast(\"{EscapeLua(text)}\") end");
}
