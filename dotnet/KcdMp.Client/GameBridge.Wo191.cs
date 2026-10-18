// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-191 -- 0.60.7 hotfix: four defects of a one-hour two-player session on 0.60.5 (Wo191Rules has the rules; docs/WO-191-findings.md the
// evidence).
//   1  a relaunched joiner's actions were refused as stale by the host for the rest of the session: the receiver now forgets a sender at
//      every disconnect, re-naming and join (1a, mp_action_forget), every run names itself in the header (1b, ActionGen.Revision), and
//      every refusal is counted per kind (1c, WO191-ACTION).
//   2  a copy that fights is never swept (any trigger): engaged, combat mode, targeted by or targeting a player, or 10 s after
//      (mp_sweep_fight_guard; the Lua gate is KCD2MP_W191Fighting, this side gates the DLL's neutralise).
//   3  an attacker always has a body: the host pins an NPC or animal that targets a partner into the NPC stream at once; the joiner asks
//      for one it has no body for; a blow from a bodiless attacker is counted as ghost damage (mp_attacker_bind; WO191-BIND, WO191-GHOST).
//   4  on the joiner, a fader that belongs to no scene ends the game's own way after 3 s (mp_fader_bound; WO191-FADER).
// Log tags: WO191-ACTION, WO191-SWEEP, WO191-BIND, WO191-GHOST, WO191-FADER, WO191-CFG, WO191-STATS.
public partial class GameBridge
{
    // ================================================================ switches (the mod's w191_cfg)

    private volatile bool _w191Forget = Wo191Rules.DefaultActionForget, _w191FightGuard = Wo191Rules.DefaultFightGuard,
                          _w191AttackerBind = Wo191Rules.DefaultAttackerBind, _w191FaderBound = Wo191Rules.DefaultFaderBound;

    // Harness-only (KCDMP_TEST_W191=1, never set by the launcher): "target <npc> <peer>" feeds the engine's target line to 3b's handler.
    private static readonly bool W191Test = Environment.GetEnvironmentVariable("KCDMP_TEST_W191") == "1";

    private void Wo191OnEvent(string name, string? arg)
    {
        if (name == "w191_test" && W191Test)
        {
            var t = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 3 && t[0] == "target") Wo191OnEngineLine($"[{t[1]}] Set target: kcd2mp_{t[2]}");
            Console.WriteLine($"WO191-TEST {arg}");
            return;
        }
        if (name != "w191_cfg") return;
        foreach (var kv in (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = kv.IndexOf('=');
            if (eq <= 0) continue;
            bool on = kv[(eq + 1)..] == "on";
            switch (kv[..eq])
            {
                case "forget": _w191Forget = on; break;
                case "fightguard": _w191FightGuard = on; break;
                case "attackerbind": _w191AttackerBind = on; break;
                case "faderbound": _w191FaderBound = on; break;
            }
        }
        Console.WriteLine($"WO191-CFG action_forget={On(_w191Forget)} sweep_fight_guard={On(_w191FightGuard)} attacker_bind={On(_w191AttackerBind)} fader_bound={On(_w191FaderBound)} run={_actionOut.Gen.Revision}");
    }

    private void Wo191OnConnect() => _ = ExecLuaAsync("if KCD2MP_W191Cfg then KCD2MP_W191Cfg() end");

    private long _w191Forgets, _w191SweepSkips, _w191Pins, _w191BindAsks, _w191BindAsksIn, _w191Ghosts, _w191FaderReleases;

    private string Wo191StatsLine() => FormattableString.Invariant(
        $"WO191-STATS forgets={_w191Forgets} runs_adopted={_actionIn.RunsAdopted} stale={_actionIn.Stale} expired={_actionIn.Expired} sweep_skips={_w191SweepSkips} pins={_w191Pins} bind_asks_out={_w191BindAsks} bind_asks_in={_w191BindAsksIn} ghost_blows={_w191Ghosts} fader_releases={_w191FaderReleases}");

    // ================================================================ 1: the action channel

    private readonly Wo191Rules.RejectLog _w191Rejects = new();

    /// <summary>1a: the receiver forgets everything it held for this sender (its ordering and its generation).</summary>
    private void Wo191ForgetSender(byte id, string why)
    {
        if (!_w191Forget) return;
        bool had = _actionIn.Forget(id);
        if (!had) return;
        Interlocked.Increment(ref _w191Forgets);
        Console.WriteLine($"WO191-ACTION forgot sender={id} why={why} -- its next action starts a fresh order (a relaunched agent's are never refused as stale)");
    }

    /// <summary>1c: a refused inbound action, logged once per kind per minute with the count in between.</summary>
    private void Wo191OnActionReject(ActionReject why)
    {
        var kind = _actionIn.LastRejectKind;
        if (_w191Rejects.Note(kind, why, Environment.TickCount64) is not { } c) return;
        Console.WriteLine(FormattableString.Invariant(
            $"WO191-ACTION rejected kind={(kind?.ToString() ?? "?")} why={why} {_actionIn.LastRejectDetail} total={c.Total} since_last_line={c.Since}"));
    }

    // ================================================================ 2: never sweep a copy that fights

    private readonly Wo191Rules.FightGuard _w191Fight = new();
    private readonly ConcurrentDictionary<string, long> _w191FightTold = new(StringComparer.Ordinal);

    /// <summary>2: a sign that this copy fights; the Lua gate hears it at most once a second per copy.</summary>
    private void Wo191NoteFight(string npc, string why)
    {
        if (string.IsNullOrEmpty(npc) || !NpcNamePattern.IsMatch(npc)) return;
        long now = Environment.TickCount64;
        _w191Fight.Note(npc, now, why);
        if (_w191FightTold.TryGetValue(npc, out long at) && now - at < 1000) return;
        _w191FightTold[npc] = now;
        _ = ExecLuaAsync($"if KCD2MP_W191FightMark then KCD2MP_W191FightMark(\"{EscapeLua(npc)}\", \"{EscapeLua(why)}\") end");
    }

    /// <summary>2: the DLL's half of a sweep (the neutralise) is refused for a copy that fights; true = refused (told once per fight).</summary>
    private bool Wo191SweepRefused(string npc, string trigger)
    {
        if (!_w191FightGuard) return false;
        bool engaged = _w132Engaged.ContainsKey(npc) || _w147Local.ContainsKey(npc);
        bool targeted = _w147DrawnSinceMs >= 0 && string.Equals(_npcTarget, npc, StringComparison.Ordinal);   // this player faces it, armed
        bool combat = trigger != "hostconv" && _w183Left.Zero(npc, out _);   // after the host's talk the copy's own combat flag is WO-167 T2's stale state
        string? why = _w191Fight.Blocks(npc, Environment.TickCount64, engaged, combat, targeted, false);
        if (why is null) return false;
        Interlocked.Increment(ref _w191SweepSkips);
        if (_w191Fight.TellSkip(npc))
            Console.WriteLine($"WO191-SWEEP npc={npc} trigger={trigger} SKIPPED why={why} -- a copy that fights is never reset (told once per fight)");
        return true;
    }

    // ================================================================ 3: an attacker always has a body

    private readonly ConcurrentDictionary<string, long> _w191BindAskedAt = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _w191PinnedAt = new(StringComparer.Ordinal);

    /// <summary>3b, the host: the engine's "[npc] Set target: kcd2mp_N" -- an NPC or animal turned on a partner's figure.</summary>
    private void Wo191OnEngineLine(string line)
    {
        if (!Wo191Rules.TryParseTargetLine(line, out string npc, out byte peer)) return;
        if (!W137Host || !_w191AttackerBind) return;
        Wo191Pin(npc, $"targets-player-{peer}");
    }

    /// <summary>3b, the host: the NPC goes into the stream now (the Lua side keeps it there 60 s after the last sign) -- at most once a second.</summary>
    private void Wo191Pin(string npc, string why)
    {
        if (!NpcNamePattern.IsMatch(npc) || Protocol.IsNeverSyncedNpcName(npc)) return;
        long now = Environment.TickCount64;
        if (_w191PinnedAt.TryGetValue(npc, out long at) && now - at < 1000) return;
        _w191PinnedAt[npc] = now;
        Interlocked.Increment(ref _w191Pins);
        _ = ExecLuaAsync($"if KCD2MP_W191PinTrack then KCD2MP_W191PinTrack(\"{EscapeLua(npc)}\", \"{EscapeLua(why)}\") end");
    }

    /// <summary>3b, the joiner: no body here for an NPC that attacks (or targets) this player -- the host is asked for it (rate-limited per NPC).</summary>
    private void Wo191AskBody(string npc, string why)
    {
        if (!W137Joiner || _resyncStream is not Stream s || !NpcNamePattern.IsMatch(npc)) return;
        long now = Environment.TickCount64;
        bool have = _npcEntityIds.ContainsKey(npc);
        if (!Wo191Rules.AskBind(have, _w191AttackerBind, _w191BindAskedAt.GetValueOrDefault(npc), now)) return;
        _w191BindAskedAt[npc] = now;
        Interlocked.Increment(ref _w191BindAsks);
        Console.WriteLine($"WO191-BIND ask npc={npc} why={why} -- no body for it here: the host streams it at once");
        _ = Task.Run(async () =>
        {
            try { await WritePacketAsync(s, _actionOut.Build(ActionKind.NpcResync, ActionPhase.Commit, Wo191Rules.AttackerResyncPayload(npc)), CancellationToken.None); }
            catch (Exception ex) { Console.WriteLine($"WO191-BIND ask npc={npc} failed: {ex.GetType().Name}: {ex.Message}"); }
        });
    }

    /// <summary>3b, the host: a joiner's NpcResync naming an attacker -- pinned (no burst: that one NPC is what is missing). False = not that kind.</summary>
    private bool Wo191OnResyncIn(InboundAction a)
    {
        string? npc = Wo191Rules.AttackerOf(a.Payload);
        if (npc is null) return false;
        Interlocked.Increment(ref _w191BindAsksIn);
        if (!_hostAuthority || !_isDamageAuthority || !_w191AttackerBind)
        {
            Console.WriteLine($"WO191-BIND ask in from={a.SourceGhostId} npc={npc} refused ({(!_w191AttackerBind ? "mp_attacker_bind off" : "not the owner")})");
            return true;
        }
        Console.WriteLine($"WO191-BIND ask in from={a.SourceGhostId} npc={npc} -- pinned into the NPC stream");
        _w191PinnedAt.TryRemove(npc, out _);
        Wo191Pin(npc, $"asked-by-{a.SourceGhostId}");
        return true;
    }

    /// <summary>3c, the joiner: a blow on this player from an attacker with no body here -- ghost damage, counted, and its body asked for.</summary>
    private void Wo191OnHitOnMe(string attacker, bool applied)
    {
        if (attacker.Length == 0 || _npcEntityIds.ContainsKey(attacker)) return;
        long n = Interlocked.Increment(ref _w191Ghosts);
        Console.WriteLine($"WO191-GHOST by={attacker} applied={(applied ? "yes" : "no")} n={n} -- a blow from an attacker with no body on this machine (ghost damage)");
        Wo191AskBody(attacker, "ghost-blow");
    }

    // ================================================================ 4: a fader with no content (the joiner)

    private long _w191FadeSeq;
    private readonly Wo191Rules.PendingConversation _w191Conv = new();

    /// <summary>4: the engine lines (Wo144OnEngineLine): this player's conversation waiting for its NPC; the host's target lines (3b).</summary>
    private void Wo191OnEngineLineAll(string line)
    {
        _w191Conv.OnLine(line, Environment.TickCount64);
        Wo191OnEngineLine(line);
    }

    /// <summary>4b: WO-179's fade watch (on / off); a fader that is still black and shows nothing at 3 s is ended the game's own way.</summary>
    private void Wo191OnFade(bool on, long whenMs)
    {
        long seq = Interlocked.Increment(ref _w191FadeSeq);
        if (!on || !W137Joiner || !_w191FaderBound) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay((int)Wo191Rules.FaderBoundMs);
            if (Interlocked.Read(ref _w191FadeSeq) != seq || _w179FadeOnAt == 0 || !W137Joiner || !_w191FaderBound) return;   // ended, or another edge came
            string label = _w179FadeScene != "-" ? _w179FadeScene : Wo179SceneLabel();   // a scene at the fade's start, or one since
            string? content = _w192WakeHolding ? "the wake waiting for the host (WO-192)"
                             : Wo191Rules.FaderContent(label, Wo138PauseState(), _w167FtActive, W179NotInWorld, _sentDeathForThisLife);
            long held = Environment.TickCount64 - whenMs;
            if (content is not null)
            {
                Console.WriteLine(FormattableString.Invariant($"WO191-FADER held {held / 1000.0:F1} s, left alone: it shows {content}"));
                return;
            }
            int waiting = _w191Conv.Waiting;
            Interlocked.Increment(ref _w191FaderReleases);
            Console.WriteLine(FormattableString.Invariant($"WO191-FADER released after {held / 1000.0:F1} s ({Wo191Rules.HolderText(waiting)})"));
            if (waiting >= 0)
            {
                _w179TwinReleasedFor = W200("joiner_scoped_twins") ? Volatile.Read(ref _w200AttemptMs) : _w179DudeStartMs;   // WO-179 A3 would end the same conversation at 3.5 s (WO-200 I.6: its bound's start)
                await ExecLuaAsync("if KCD2MP_W179ReleaseDialog then KCD2MP_W179ReleaseDialog(\"wo191-fader-3s\") end");
            }
            else
            {
                Volatile.Write(ref _w167FaderHeldMs, W167NowMs());
                await ExecLuaAsync("if KCD2MP_W167SceneReleased then KCD2MP_W167SceneReleased(false, \"wo191-fader-3s\") end");
            }
        });
    }
}
