// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-114 -- the leash (docs/WO-114-findings.md). The world is only alive
/// around the host, so the host decides and the joiner is the one brought back.
///
/// Host (damage authority, shared world): every 250 ms, per joiner in its
/// world, LeashLogic turns the 2D distance and the hold reasons into Leash
/// messages (0x58): a warning past mp_leash_warn_m, a 10 s countdown past
/// mp_leash_pull_m, a cancel, a pull. A host teleport of 200 m or more (a fast
/// travel) pulls every joiner beside it at once, no countdown.
///
/// Joiner: shows the words, runs a pull (dismount first, then the WO-124
/// placement beside the host with the fall damage held; the position read
/// back), and reports its own hold reasons and every pull's result to the host
/// once a second (LeashState 0x5A). Only the host fast-travels: the joiner's
/// fast travel is switched off while it is in the host's world
/// (wh_pl_FastTravelEnabled 0, restored on leaving) and a joiner's clock jump
/// is never reported to the session.
///
/// Both: the other player's position goes to the DLL once a second (pipe
/// 0x20) so a death wakes within the leash (native wake_pick.h, Phase 2).
/// </summary>
public partial class GameBridge
{
    public const bool LeashDefault = true;

    // ---- the host's settings (mirrors of the mod's KCD2MP.w114; Lua emits wo114_cfg) ----
    private volatile bool _leashEnabled = LeashDefault;
    private float _leashWarnM = LeashLogic.WarnDefaultM, _leashPullM = LeashLogic.PullDefaultM;

    // ---- host ----
    private readonly ConcurrentDictionary<byte, LeashLogic> _leashByJoiner = new();
    private readonly ConcurrentDictionary<byte, (LeashState S, DateTime AtUtc)> _leashJoinerState = new();
    private readonly ConcurrentDictionary<byte, ushort> _leashFtRefusalsSeen = new();
    private DateTime _leashCfgSentUtc = DateTime.MinValue;
    private (float X, float Y, DateTime AtUtc)? _leashPrevLocal;
    private DateTime _leashFastTravelUtc = DateTime.MinValue;
    private DateTime _leashJumpQuietUntilUtc = DateTime.MinValue;   // after a respawn/load: the next jump is that, not a travel
    private volatile bool _leashHostTravelling;                      // "FastTravel: started..." seen, "ended" not yet
    private DateTime _leashFtArrivedUtc = DateTime.MinValue;         // "FastTravel: ended...": the joiners come along 1.5 s later
    private (float X, float Y)? _leashJumpStartPos;                  // where the host was when its clock started jumping
    private (bool Valid, float X, float Y, float R, DateTime AtUtc) _leashPartnerSent;

    // ---- both: the mod's answer to KCD2MP_Wo114Busy (dialogue, mounted) ----
    private (bool Dialogue, bool Mounted, DateTime AtUtc) _leashLuaBusy;

    // ---- joiner ----
    private byte _leashHostId = 0xFF;
    private bool _leashHostOn = LeashDefault;
    private float _leashHostWarnM = LeashLogic.WarnDefaultM, _leashHostPullM = LeashLogic.PullDefaultM;
    private DateTime _leashHostCfgUtc = DateTime.MinValue;
    private byte _leashLastPullSeq;
    private byte _leashLastResult;
    private ushort _leashFromM, _leashToM, _leashResidualCm;
    private int _leashPulling;
    private DateTime _leashPulledUtc = DateTime.MinValue;
    private ushort _leashStateSent = 0xFFFF;
    private DateTime _leashStateSentUtc = DateTime.MinValue;
    private DateTime _leashFtRefusedUtc = DateTime.MinValue;
    private bool _leashFtBlocked;

    private const int LeashTickMs = 250;
    private const double LeashFreshS = 5.0;
    private const float LeashJumpM = 200f;          // a teleport, not a gallop (a horse covers ~15 m/s)
    private const float LeashPlaceDistM = 3.0f;     // WO-124's placement distance

    private static string On114(bool v) => v ? "on" : "off";

    // ---------------------------------------------------------------- lifecycle

    private void Wo114OnConnect(Stream stream, CancellationToken ct)
    {
        foreach (var l in _leashByJoiner.Values) l.Reset();
        _leashByJoiner.Clear();
        _leashJoinerState.Clear();
        _leashFtRefusalsSeen.Clear();
        _leashHostId = 0xFF;
        _leashStateSent = 0xFFFF;
        _leashPrevLocal = null;
        Wo147LeashOnConnect();   // WO-147: the joiner's own motion, the partners' positions
        _ = ExecLuaAsync("if KCD2MP_Wo114CfgEmit then KCD2MP_Wo114CfgEmit() end");
        _ = Wo114LoopAsync(ct);
    }

    private async Task Wo114OnDisconnectAsync()
    {
        _leashByJoiner.Clear();
        _leashJoinerState.Clear();
        _leashPartnerSent = default;
        try { await _combat.SetPartnerAsync(false, 0, 0, 0, 0); } catch { }
        if (_leashFtBlocked) await Wo114SetFastTravelBlockAsync(false, "disconnect");
        _ = ExecLuaAsync("if KCD2MP_Wo114Countdown then KCD2MP_Wo114Countdown(0) end");
    }

    private async Task Wo114LoopAsync(CancellationToken ct)
    {
        long lastSecond = 0;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(LeashTickMs, ct); } catch { return; }
            try
            {
                long now = LeashNowMs();
                bool second = now - lastSecond >= 1000;
                if (second) lastSecond = now;
                bool host = _combatRoleApplied && _isDamageAuthority;
                bool joiner = _combatRoleApplied && !_isDamageAuthority;

                if (second && _where != GameWhere.Menu)
                    _ = ExecLuaAsync("if KCD2MP_Wo114Busy then KCD2MP_Wo114Busy() end");

                if (host && _sharedWorld) await Wo114HostTickAsync(now, second);
                else if (joiner) await Wo114JoinerTickAsync(second);

                if (second) await Wo114PartnerTickAsync(host, joiner);
            }
            catch (Exception ex) { Console.WriteLine($"MP-LEASH tick failed: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    // ---------------------------------------------------------------- mod events

    /// <summary><c>wo114_cfg leash=on warn=600 pull=650</c> (the host's settings), <c>wo114_busy d=0 m=0</c>, <c>wo114_ft_try</c>.</summary>
    private void Wo114OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "wo114_cfg":
            {
                foreach (var kv in (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = kv[..eq], v = kv[(eq + 1)..];
                    switch (k)
                    {
                        case "leash": _leashEnabled = v == "on"; break;
                        case "warn": if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float w)) _leashWarnM = w; break;
                        case "pull": if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float p)) _leashPullM = p; break;
                    }
                }
                _leashCfgSentUtc = DateTime.MinValue;   // tell the joiners at the next tick
                Console.WriteLine(FormattableString.Invariant(
                    $"MP-LEASH cfg leash={On114(_leashEnabled)} warn_m={_leashWarnM:F0} pull_m={_leashPullM:F0} role={(!_combatRoleApplied ? "unknown" : _isDamageAuthority ? "host (decides)" : "joiner (the host's values apply)")}"));
                return;
            }
            case "wo114_busy":
            {
                bool d = false, m = false;
                foreach (var kv in (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (kv == "d=1") d = true;
                    if (kv == "m=1") m = true;
                }
                _leashLuaBusy = (d, m, DateTime.UtcNow);
                if (d) _w147LastDialogueUtc = DateTime.UtcNow;   // WO-147: a conversation's outcome lands as it ends
                return;
            }
            case "wo114_ft_try":
                // The mod saw the joiner try to fast travel while it is blocked (KCD2MP_Wo114FastTravelTried).
                _leashFtRefusedUtc = DateTime.UtcNow;
                Console.WriteLine($"MP-LEASH joiner: a fast travel was refused -- only the host fast-travels in co-op ({arg})");
                return;
        }
    }

    private bool LuaBusyFresh(out bool dialogue, out bool mounted)
    {
        var b = _leashLuaBusy;
        bool fresh = (DateTime.UtcNow - b.AtUtc).TotalSeconds < 3;
        dialogue = fresh && b.Dialogue;
        mounted = fresh ? b.Mounted : _lastRiding;
        return fresh;
    }

    // ---------------------------------------------------------------- host

    private LeashLogic.Hold HostHold()
    {
        var h = LeashLogic.Hold.None;
        if (_localDowned) h |= LeashLogic.Hold.HostDowned;
        if (_where is GameWhere.Loading or GameWhere.Menu || Wo123HostJoinActive) h |= LeashLogic.Hold.HostLoading;
        if (_localCutsceneActive) h |= LeashLogic.Hold.HostCutscene;
        if (LuaBusyFresh(out bool d, out _) && d) h |= LeashLogic.Hold.HostDialogue;
        if (_localAutoPaused || _localManualPaused) h |= LeashLogic.Hold.HostMenu;
        if (_hostWorldHenry == false) h |= LeashLogic.Hold.NonHenry;
        if (_hostLoadAnnounced) h |= LeashLogic.Hold.HostReloading;
        if (_leashHostTravelling) h |= LeashLogic.Hold.HostTravelling;
        return h;
    }

    private async Task Wo114HostTickAsync(long now, bool second)
    {
        var hostHold = HostHold();
        Wo114HostFastTravelCheck(hostHold);
        if (_leashFtArrivedUtc != DateTime.MinValue && (DateTime.UtcNow - _leashFtArrivedUtc).TotalSeconds >= 1.5)
        {
            _leashFtArrivedUtc = DateTime.MinValue;
            Wo114NoteHostFastTravel("the engine's fast travel ended");
        }

        // The settings, to every joiner in the world (their death wake filter reads them).
        bool sendCfg = (DateTime.UtcNow - _leashCfgSentUtc).TotalSeconds >= 10;
        if (sendCfg) _leashCfgSentUtc = DateTime.UtcNow;

        foreach (var (id, (st, at)) in _leashJoinerState)
        {
            if (!IsLivePeer(id))   // WO-144: gone is gone (was: no name and 30 s)
            {
                _leashJoinerState.TryRemove(id, out _);
                _leashByJoiner.TryRemove(id, out _);
                Console.WriteLine($"MP-LEASH host: joiner {id} gone -- its leash is dropped");
                continue;
            }
            if (sendCfg) await Wo114SendAsync(id, new LeashCommand(Protocol.LeashKindConfig, (byte)(_leashEnabled ? 1 : 0),
                                                         LeashCommand.Metres(_leashWarnM), _lastX, _lastY, _lastZ, LeashCommand.Metres(_leashPullM)));

            if ((DateTime.UtcNow - at).TotalSeconds < LeashFreshS && Wo140HostSkipsLeash(id, st.Flags)) continue;   // WO-140: not in this world
            var logic = _leashByJoiner.GetOrAdd(id, _ => new LeashLogic());
            bool stateFresh = (DateTime.UtcNow - at).TotalSeconds < LeashFreshS;
            var hold = hostHold | (stateFresh ? LeashLogic.JoinerHold(st.Flags) : LeashLogic.Hold.JoinerLoading);
            // WO-147: the joiner's position as the leash may use it -- current on the joiner's own clock
            // (never a backlog's burst of old samples) and read the moment it arrived.
            double? d = _hasPushed ? Wo147LeashDistance(id, _lastX, _lastY) : null;
            List<LeashLogic.Action> acts;
            lock (logic)
            {
                logic.Config = new LeashLogic.Settings(_leashEnabled, _leashWarnM, _leashPullM);
                // WO-147: a pull waits as long as the link needs: this machine's round trip, and how far the
                // joiner's own samples run behind (the field's pull timed out behind a 20 s backlog).
                logic.PullTimeoutMs = Math.Max(LeashLogic.TimeoutForRtt(_clockRttMedianMs), Wo147PeerLagTimeoutMs(id));
                logic.CapMs = _w147LeashCapMs;   // WO-147: mp_leash_cap_s
                acts = logic.Tick(now, d, hold);
            }
            foreach (var a in acts)
                await Wo114HostActAsync(id, logic, a, d);
        }
    }

    /// <summary>A host jump of 200 m or more between two samples, not a respawn or a load = a fast travel (or any teleport): every joiner comes along.</summary>
    private void Wo114HostFastTravelCheck(LeashLogic.Hold hostHold)
    {
        if (!_hasPushed) return;
        var excluded = LeashLogic.Hold.HostDowned | LeashLogic.Hold.HostLoading | LeashLogic.Hold.HostReloading | LeashLogic.Hold.HostTravelling;
        if ((hostHold & excluded) != 0) { _leashPrevLocal = null; _leashJumpQuietUntilUtc = DateTime.UtcNow.AddSeconds(5); return; }
        if (DateTime.UtcNow < _leashJumpQuietUntilUtc) { _leashPrevLocal = null; return; }
        var prev = _leashPrevLocal;
        _leashPrevLocal = (_lastX, _lastY, DateTime.UtcNow);
        if (prev is not { } p) return;
        double jump = LeashLogic.Dist2D(p.X, p.Y, _lastX, _lastY);
        if (jump < LeashJumpM) return;
        Wo114NoteHostFastTravel(FormattableString.Invariant($"this game jumped {jump:F0} m ({p.X:F0},{p.Y:F0}) -> ({_lastX:F0},{_lastY:F0}), a teleport"));
    }

    /// <summary>The host arrived somewhere else (the engine's fast travel, a teleport): every joiner comes along. Deduped for 20 s.</summary>
    private void Wo114NoteHostFastTravel(string why)
    {
        if ((DateTime.UtcNow - _leashFastTravelUtc).TotalSeconds < 20) return;
        _leashFastTravelUtc = DateTime.UtcNow;
        Console.WriteLine($"MP-LEASH host: {why} -- {_leashJoinerState.Count} joiner(s) come along");
        if (!_leashEnabled) { Console.WriteLine("MP-LEASH host: mp_leash is off -- nobody is brought along"); return; }
        foreach (var id in _leashJoinerState.Keys) { var l = _leashByJoiner.GetOrAdd(id, _ => new LeashLogic()); lock (l) l.NoteHostFastTravel(); }
    }

    /// <summary>"FastTravel: started..." / "FastTravel: ended..." on this machine (LogTailGameTransport).</summary>
    private void Wo114OnLocalFastTravel(bool active)
    {
        if (_combatRoleApplied && _isDamageAuthority)
        {
            _leashHostTravelling = active;
            if (active) Console.WriteLine("MP-LEASH host: fast travel started -- the joiners' countdowns hold; they come along on arrival");
            else { _leashFtArrivedUtc = DateTime.UtcNow; Console.WriteLine("MP-LEASH host: fast travel ended -- the joiners come along in 1.5 s"); }
            return;
        }
        if (!_combatRoleApplied) return;
        if (active)
        {
            _leashFtRefusedUtc = DateTime.UtcNow;
            Console.WriteLine($"MP-LEASH joiner: a fast travel STARTED on this game{(_joinedWorld ? " in the host's world, past the block -- only the host fast-travels; the leash brings you back" : " (not in the host's world)")}");
            if (_joinedWorld) _ = Wo114SayAsync(LeashLogic.Text.JoinerFastTravelBlocked);
        }
        else Console.WriteLine("MP-LEASH joiner: this game's fast travel ended");
    }

    /// <summary>The engine refused a fast travel on this machine (the switch at 0): on a blocked joiner, say why.</summary>
    private void Wo114OnFastTravelRefused()
    {
        if (!_leashFtBlocked)
        {
            if (Wo154MenuOnFastTravelRefused()) return;   // WO-154: the session's fast travel is off (mp_fast_travel)
            Console.WriteLine("MP-LEASH the engine refused a fast travel here (not our block)");
            return;
        }
        _leashFtRefusedUtc = DateTime.UtcNow;
        Console.WriteLine("MP-LEASH joiner: fast travel refused by the engine (wh_pl_FastTravelEnabled 0) -- only the host fast-travels in co-op; telling the player");
        _ = ExecLuaAsync("if KCD2MP_Wo114FastTravelTried then KCD2MP_Wo114FastTravelTried(\"engine-refused\") end");
    }

    /// <summary>The host's clock started jumping (the clock-jump watcher): remember where the host stood.</summary>
    private void Wo114OnClockJumpStart()
    {
        if (_combatRoleApplied && _isDamageAuthority && _hasPushed) _leashJumpStartPos = (_lastX, _lastY);
    }

    /// <summary>The host's clock jump settled (reported as a fast travel): a fallback when the log line was missed -- only if the host moved 100 m or more.</summary>
    private void Wo114OnClockJumpSettled()
    {
        var p = _leashJumpStartPos;
        _leashJumpStartPos = null;
        if (!(_combatRoleApplied && _isDamageAuthority) || p is not { } s || !_hasPushed) return;
        double moved = LeashLogic.Dist2D(s.X, s.Y, _lastX, _lastY);
        if (moved >= 100) Wo114NoteHostFastTravel(FormattableString.Invariant($"the clock jumped and this game moved {moved:F0} m (a fast travel)"));
    }

    private string LeashPartnerName(byte id) => _ghostNames.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n) ? n : "Your partner";

    private async Task Wo114HostActAsync(byte id, LeashLogic logic, LeashLogic.Action a, double? d)
    {
        string ds = d is double dv ? dv.ToString("F0", CultureInfo.InvariantCulture) : "?";
        ushort dm = LeashCommand.Metres(d ?? 0);
        switch (a.Kind)
        {
            case LeashLogic.Act.Warn:
                Console.WriteLine($"MP-LEASH host: joiner {id} past warn={a.Arg} m (d={ds} m) -- warned");
                await Wo114SendAsync(id, new LeashCommand(Protocol.LeashKindWarn, 0, (ushort)a.Arg, _lastX, _lastY, _lastZ, dm));
                await Wo114SayAsync(LeashLogic.Text.HostWarn(LeashPartnerName(id)));
                break;
            case LeashLogic.Act.Countdown:
                Console.WriteLine($"MP-LEASH host: joiner {id} countdown {a.Arg} s (d={ds} m, pull at {_leashPullM:F0} m)");
                await Wo114SendAsync(id, new LeashCommand(Protocol.LeashKindCountdown, 0, (ushort)a.Arg, _lastX, _lastY, _lastZ, dm));
                break;
            case LeashLogic.Act.Cancel:
                Console.WriteLine($"MP-LEASH host: joiner {id} countdown cancelled ({a.Note}, d={ds} m)");
                await Wo114SendAsync(id, new LeashCommand(Protocol.LeashKindCancel, 0, 0, _lastX, _lastY, _lastZ, dm));
                break;
            case LeashLogic.Act.Hold:
                Console.WriteLine($"MP-LEASH host: joiner {id} held ({a.Note}) seconds_left={a.Arg}{(logic.FastTravelPending ? " fast-travel pull owed" : "")} d={ds} m -- no pull while it lasts ({(logic.CapMs > 0 ? $"at most {logic.CapMs / 1000} s" : "no limit: mp_leash_cap_s 0")})");
                if (a.Arg > 0 || logic.FastTravelPending)
                    await Wo114SendAsync(id, new LeashCommand(Protocol.LeashKindHold, 0, (ushort)a.Arg, _lastX, _lastY, _lastZ, dm));
                break;
            case LeashLogic.Act.HoldCapped:   // WO-147
                Console.WriteLine($"MP-LEASH host: joiner {id} held {a.Arg} s ({a.Note}) -- the hold is over: the countdown runs and its pull is forced (a dialogue is ended first)");
                break;
            case LeashLogic.Act.Pull:
                Console.WriteLine(FormattableString.Invariant(
                    $"MP-LEASH host: pull #{a.Arg} joiner {id} reason={Protocol.LeashReasonName(a.Reason)} d={ds} m -> beside ({_lastX:F1}, {_lastY:F1}, {_lastZ:F1})"));
                await Wo114SendAsync(id, new LeashCommand(Protocol.LeashKindPull, (byte)a.Arg, a.Reason, _lastX, _lastY, _lastZ, dm));
                break;
            case LeashLogic.Act.PullResult:
                Console.WriteLine($"MP-LEASH host: pull #{a.Arg} joiner {id} result={a.Note} (d now {ds} m)");
                if (a.Note == "placed") await Wo114SayAsync(LeashLogic.Text.HostPulled(LeashPartnerName(id)));
                break;
            case LeashLogic.Act.Disarmed:
                Console.WriteLine($"MP-LEASH host: {a.Arg} pulls in a row failed (last: {a.Note}) -- the pulls pause {LeashLogic.FailPauseMs / 1000} s, warnings stay (mp_leash off, then on, re-arms at once)");
                break;
            case LeashLogic.Act.Rearmed:      // WO-147
                Console.WriteLine($"MP-LEASH host: joiner {id}: the {a.Arg} s pause after failed pulls is over -- pulls again");
                break;
            case LeashLogic.Act.AlreadyBeside:
                Console.WriteLine($"MP-LEASH host: fast travel -- joiner {id} is already beside the host ({a.Note}); no pull");
                break;
            case LeashLogic.Act.FastTravelExpired:   // WO-147
                Console.WriteLine($"MP-LEASH host: joiner {id}: the fast-travel pull was owed {a.Arg} s and lapsed (d={a.Note}) -- the distance rules decide from here");
                break;
        }
    }

    private async Task Wo114SendAsync(byte target, LeashCommand c)
    {
        try { await WriteJoinAsync(c.Build(target)); }
        catch (Exception ex) { Console.WriteLine($"MP-LEASH {Protocol.LeashKindName(c.Kind)} to {target} not sent: {ex.Message}"); }
    }

    private Task Wo114SayAsync(string text) =>
        ExecLuaAsync($"if KCD2MP_Wo114Msg then KCD2MP_Wo114Msg(\"{EscapeLua(text)}\") end");

    /// <summary>LeashState from a joiner: its hold reasons, and a pull's result.</summary>
    private void Wo114OnLeashStateIn(byte src, byte[] body)
    {
        if (!(_combatRoleApplied && _isDamageAuthority)) return;
        if (!LeashState.TryDecode(body, out var s)) return;
        bool first = !_leashJoinerState.ContainsKey(src);
        var prev = first ? default : _leashJoinerState[src].S;
        _leashJoinerState[src] = (s, DateTime.UtcNow);
        if (first || prev.Flags != s.Flags)
            Console.WriteLine($"MP-LEASH host: joiner {src} state {Protocol.LeashFlagsText(s.Flags)}");
        if (first) _leashCfgSentUtc = DateTime.MinValue;
        if (s.Result != Protocol.LeashResultNone && _leashByJoiner.TryGetValue(src, out var logic))
        {
            List<LeashLogic.Action>? acts = null;
            lock (logic)   // WO-147: the leash lane and the leash loop share it
            {
                if (logic.PullInFlight && s.PullSeq == logic.PullSeq)
                {
                    Console.WriteLine(FormattableString.Invariant(
                        $"MP-LEASH host: joiner {src} reports pull #{s.PullSeq} {Protocol.LeashResultName(s.Result)} from={s.FromM} m to={s.ToM} m residual={s.ResidualCm / 100.0:F2} m"));
                    acts = logic.OnPullResult(LeashNowMs(), s.PullSeq, s.Result);
                }
            }
            if (acts is not null)
                foreach (var a in acts)
                    _ = Wo114HostActAsync(src, logic, a, s.ToM);
        }
        if ((s.Flags & Protocol.LeashFlagFastTravelRefused) != 0 && (prev.Flags & Protocol.LeashFlagFastTravelRefused) == 0)
            Console.WriteLine($"MP-LEASH host: joiner {src} tried to fast travel -- refused on its side (only the host fast-travels)");
        if ((s.Flags & Protocol.LeashFlagFlying) != (prev.Flags & Protocol.LeashFlagFlying))   // WO-147
            Console.WriteLine((s.Flags & Protocol.LeashFlagFlying) != 0
                ? $"MP-LEASH host: joiner {src} is flying (faster than any horse, no fast travel -- the developer fly mode): its distance changes that fast, the leash still counts it"
                : $"MP-LEASH host: joiner {src} is on its feet again");
    }

    // ---------------------------------------------------------------- joiner

    private ushort JoinerLeashFlags()
    {
        ushort f = 0;
        if (_joinedWorld) f |= Protocol.LeashFlagInWorld;
        if (_w140Separate) f |= Protocol.LeashFlagSeparate;   // WO-140: in its own world -- the host does not leash it
        if (_localDowned) f |= Protocol.LeashFlagDowned;
        if (_jj is not null || _rewinding || _where is GameWhere.Loading or GameWhere.Menu) f |= Protocol.LeashFlagLoading;
        if (_localCutsceneActive) f |= Protocol.LeashFlagCutscene;
        LuaBusyFresh(out bool dialogue, out bool mounted);
        if (dialogue) f |= Protocol.LeashFlagDialogue;
        if (_localAutoPaused || _localManualPaused) f |= Protocol.LeashFlagMenu;
        if (mounted) f |= Protocol.LeashFlagMounted;
        if ((DateTime.UtcNow - _leashFtRefusedUtc).TotalSeconds < 5) f |= Protocol.LeashFlagFastTravelRefused;
        if (Wo147OwnFlying) f |= Protocol.LeashFlagFlying;   // WO-147
        return f;
    }

    private async Task Wo114JoinerTickAsync(bool second)
    {
        // Only the host fast-travels: switched off while this game is in the host's world.
        bool wantBlock = _joinedWorld && _leashHostOn && JoinerSharedEffective;
        if (wantBlock != _leashFtBlocked || (wantBlock && second && DateTime.UtcNow.Second % 5 == 0))
            await Wo114SetFastTravelBlockAsync(wantBlock, wantBlock ? "in the host's world" : "left the host's world");

        Wo147JoinerMotionCheck();   // WO-147: a jump, a flight or a fast travel -- told apart (was Wo114JoinerJumpCheck)

        ushort f = JoinerLeashFlags();
        bool changed = f != _leashStateSent;
        if (!(changed || (second && (DateTime.UtcNow - _leashStateSentUtc).TotalMilliseconds >= 900))) return;
        await Wo114SendStateAsync(f);
    }

    private async Task Wo114SendStateAsync(ushort flags)
    {
        if (_wo122Stream is null) return;
        _leashStateSent = flags;
        _leashStateSentUtc = DateTime.UtcNow;
        try { await WriteJoinAsync(new LeashState(flags, _leashLastPullSeq, _leashLastResult, _leashFromM, _leashToM, _leashResidualCm).Build()); }
        catch (Exception ex) { Console.WriteLine($"MP-LEASH joiner: state not sent: {ex.Message}"); }
    }

    private async Task Wo114SetFastTravelBlockAsync(bool block, string why)
    {
        bool was = _leashFtBlocked;
        _leashFtBlocked = block;
        if (was != block)
            Console.WriteLine($"MP-LEASH joiner: fast travel {(block ? "switched OFF" : "given back")} ({why})");
        try { await ExecLuaAsync($"if KCD2MP_Wo114FastTravelBlock then KCD2MP_Wo114FastTravelBlock({(block ? "true" : "false")}, \"{EscapeLua(why)}\") end"); }
        catch { }
    }

    /// <summary>The host's Leash message.</summary>
    private void Wo114OnLeashIn(byte src, byte[] body)
    {
        if (_combatRoleApplied && _isDamageAuthority) return;   // the host never obeys a leash
        if (!LeashCommand.TryDecode(body, out var c)) return;
        _leashHostId = src;
        switch (c.Kind)
        {
            case Protocol.LeashKindConfig:
            {
                bool on = (c.Seq & 1) != 0;
                bool changed = on != _leashHostOn || Math.Abs(c.Arg - _leashHostWarnM) > 0.5f || Math.Abs(c.DistM - _leashHostPullM) > 0.5f
                               || _leashHostCfgUtc == DateTime.MinValue;
                _leashHostOn = on; _leashHostWarnM = c.Arg; _leashHostPullM = c.DistM; _leashHostCfgUtc = DateTime.UtcNow;
                if (changed) Console.WriteLine($"MP-LEASH joiner: the host's leash is {On114(on)} (warn {c.Arg} m, pull {c.DistM} m)");
                return;
            }
            case Protocol.LeashKindWarn:
                Console.WriteLine($"MP-LEASH joiner: warned by the host (d={c.DistM} m, warn={c.Arg} m)");
                _ = Wo114SayAsync(LeashLogic.Text.JoinerWarn);
                return;
            case Protocol.LeashKindCountdown:
                Console.WriteLine($"MP-LEASH joiner: countdown {c.Arg} (d={c.DistM} m)");
                _ = ExecLuaAsync($"if KCD2MP_Wo114Countdown then KCD2MP_Wo114Countdown({c.Arg}) end");
                return;
            case Protocol.LeashKindCancel:
                Console.WriteLine($"MP-LEASH joiner: countdown cancelled (d={c.DistM} m)");
                _ = ExecLuaAsync("if KCD2MP_Wo114Countdown then KCD2MP_Wo114Countdown(0) end");
                _ = Wo114SayAsync(LeashLogic.Text.JoinerCancel);
                return;
            case Protocol.LeashKindHold:
                Console.WriteLine($"MP-LEASH joiner: countdown held at {c.Arg} s by the host");
                _ = ExecLuaAsync("if KCD2MP_Wo114Countdown then KCD2MP_Wo114Countdown(0) end");
                return;
            case Protocol.LeashKindPull:
                _ = Wo114PullAsync(c);
                return;
        }
    }

    /// <summary>
    /// Bring this player beside the host: dismount first; read the position back.
    /// WO-147: refused only while a pull is unsafe for this player (a load, a dialogue, a cutscene, a down);
    /// a menu is no reason (the world runs behind it since WO-138: the player is simply there when it
    /// closes). A FORCED pull (the host's hold cap ran out) is refused only by a load: a dialogue is ended
    /// first. Where the game has no ground beside the host (the area is not loaded here yet: the field's
    /// "no ground in 8 directions", 1.2 and 1.9 km out) the player goes to a spot the host just stood on,
    /// and to the ground beside the host once the area has loaded.
    /// </summary>
    private async Task Wo114PullAsync(LeashCommand c)
    {
        if (Interlocked.Exchange(ref _leashPulling, 1) == 1) { Console.WriteLine($"MP-LEASH joiner: pull #{c.Seq} ignored -- a pull is already running"); return; }
        string reason = Protocol.LeashReasonName(c.Arg);
        bool forced = (c.Arg & Protocol.LeashReasonForced) != 0;
        byte result = Protocol.LeashResultNone;
        // WO-147: the newest host position this machine has (the command's may have waited in a backlog).
        var (hx, hy, hz) = Wo147PullTargetHost(c);
        double from = _hasPushed ? LeashLogic.Dist2D(_lastX, _lastY, hx, hy) : -1, to = from;
        float residual = -1;
        try
        {
            _ = ExecLuaAsync("if KCD2MP_Wo114Countdown then KCD2MP_Wo114Countdown(0) end");
            // A fresh read of the dialogue/mount state right before acting.
            string busy = await AskModAsync("KCD2MP_Wo114BusyNow", 2000);
            bool dlg = busy.Contains("d=1"), mounted = busy.Contains("m=1") || (!busy.Contains("m=0") && _lastRiding);
            ushort f = JoinerLeashFlags();
            if (dlg) f |= Protocol.LeashFlagDialogue;
            var why = Wo147Rules.JoinerRefusesPull(LeashLogic.JoinerHold(f), forced);
            if (why != LeashLogic.Hold.None)
            {
                result = Protocol.LeashResultBusy;
                Console.WriteLine($"MP-LEASH joiner: pull #{c.Seq} ({reason}) refused -- {LeashLogic.HoldText(why)}; never pulled while busy");
                return;
            }
            if (forced && dlg)
            {
                string ended = await AskModAsync("KCD2MP_W147EndDialog", 2000);   // WO-147: the hold ran out
                Console.WriteLine($"MP-LEASH joiner: pull #{c.Seq} is forced (the host's hold ran out) -- the conversation is ended first: {ended}");
                await Task.Delay(500);
            }
            if (mounted)
            {
                // WO-124 6a: dismount first and read it back; no teleport while still mounted.
                // The engine's teleport-with-horse is a quest-graph behaviour only
                // (PlayerAction_TeleportOnHorse, a tag-point destination): not reachable here.
                // WO-147: decided -- the player is dismounted cleanly and the horse stays where it
                // stood; its owner calls it back the game's own way.
                bool off = false;
                for (int i = 0; i < 6 && !off; i++)
                {
                    string r = await AskModAsync("KCD2MP_Wo114Dismount", 2000);
                    Console.WriteLine($"MP-LEASH joiner: pull #{c.Seq} dismount try {i + 1}: {r}");
                    off = r.Contains("mounted=no");
                    if (!off) await Task.Delay(500);
                }
                if (!off)
                {
                    result = Protocol.LeashResultMounted;
                    Console.WriteLine($"MP-LEASH joiner: pull #{c.Seq} NOT done -- still mounted after 6 dismount tries (never teleported on a horse)");
                    return;
                }
                await Task.Delay(300);
            }
            var pr = await _combat.JoinPlaceAsync(hx, hy, hz, LeashPlaceDistM);
            if (pr is null)
            {
                result = Protocol.LeashResultNoPlugin;
                Console.WriteLine($"MP-LEASH joiner: pull #{c.Seq} -- no answer from the plugin (not placed)");
                return;
            }
            bool fallback = false;
            if (!pr.Ok && !pr.Snapped)
            {
                // WO-147: no ground beside the host here (not loaded this far out): a spot the host itself
                // stood on, placed exactly (the DLL's dist 0), the fall damage held.
                var spot = Wo147PullFallbackSpot(hx, hy, hz);
                var fb = await _combat.JoinPlaceAsync(spot.X, spot.Y, spot.Z, 0f);
                Console.WriteLine(FormattableString.Invariant(
                    $"MP-LEASH joiner: pull #{c.Seq}: no ground beside the host here yet -- placed on a spot the host stood on ({spot.X:F1}, {spot.Y:F1}, {spot.Z:F1}): {(fb is { Ok: true } ? "placed" : "NOT placed")}"));
                if (fb is not null) { pr = fb; fallback = true; }
            }
            residual = pr.Residual;
            to = LeashLogic.Dist2D(pr.After[0], pr.After[1], hx, hy);
            if (from < 0) from = LeashLogic.Dist2D(pr.Before[0], pr.Before[1], hx, hy);
            result = pr.Ok ? Protocol.LeashResultPlaced : Protocol.LeashResultNotPlaced;
            _leashPulledUtc = DateTime.UtcNow;
            Console.WriteLine(FormattableString.Invariant(
                $"MP-LEASH pulled from={from:F0} to={to:F1} residual={pr.Residual:F2} -- pull #{c.Seq} reason={reason} {(pr.Ok ? "placed" : "NOT placed")}{(fallback ? " (on the host's own spot)" : "")} snapped={On114(pr.Snapped)} fall_held={On114(pr.FallHeld)} mounted_before={On114(mounted)} at ({pr.After[0]:F1}, {pr.After[1]:F1}, {pr.After[2]:F1})"));
            if (pr.Ok)
                await Wo114SayAsync(Protocol.LeashReasonBase(c.Arg) == Protocol.LeashReasonFastTravel ? LeashLogic.Text.JoinerPulledFastTravel : LeashLogic.Text.JoinerPulledDistance);
            if (pr.Ok && fallback) _ = Wo147SettleBesideHostAsync(c.Seq);   // WO-147: the ground beside the host once it has loaded
            // Read it back once more after the landing settles (the emitter's position, 1.5 s on).
            _ = Task.Delay(1500).ContinueWith(_ => Console.WriteLine(FormattableString.Invariant(
                $"MP-LEASH joiner: pull #{c.Seq} settled at ({_lastX:F1}, {_lastY:F1}, {_lastZ:F1}) -- {LeashLogic.Dist2D(_lastX, _lastY, hx, hy):F1} m from the host's spot")));
        }
        catch (Exception ex)
        {
            if (result == Protocol.LeashResultNone) result = Protocol.LeashResultNotPlaced;
            Console.WriteLine($"MP-LEASH joiner: pull #{c.Seq} failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _leashLastPullSeq = c.Seq;
            _leashLastResult = result;
            _leashFromM = LeashCommand.Metres(from);
            _leashToM = LeashCommand.Metres(to);
            _leashResidualCm = (ushort)Math.Clamp(Math.Round(residual < 0 ? 0 : residual * 100), 0, ushort.MaxValue);
            Interlocked.Exchange(ref _leashPulling, 0);
            await Wo114SendStateAsync(JoinerLeashFlags());
        }
    }

    // ---------------------------------------------------------------- both: the partner for the DLL's wake choice

    private async Task Wo114PartnerTickAsync(bool host, bool joiner)
    {
        bool valid = false;
        float x = 0, y = 0, z = 0, radius = 0;
        if (host && _sharedWorld && _leashEnabled)
        {
            // The joiner in this world (fresh state, in-world), else any peer with a fresh position.
            byte? pick = null;
            foreach (var (id, (st, at)) in _leashJoinerState.OrderBy(k => k.Key))
                if ((st.Flags & Protocol.LeashFlagInWorld) != 0 && (DateTime.UtcNow - at).TotalSeconds < LeashFreshS) { pick = id; break; }
            pick ??= _ghostLastPos.Where(k => (DateTime.UtcNow - k.Value.AtUtc).TotalSeconds < LeashFreshS).Select(k => (byte?)k.Key).OrderBy(k => k).FirstOrDefault();
            if (pick is byte p && _ghostLastPos.TryGetValue(p, out var gp) && (DateTime.UtcNow - gp.AtUtc).TotalSeconds < LeashFreshS)
            { valid = true; x = gp.X; y = gp.Y; z = gp.Z; radius = _leashWarnM; }
        }
        else if (joiner && _joinedWorld && _leashHostOn)
        {
            byte hid = _leashHostId != 0xFF ? _leashHostId : _hostModeFrom;
            if (hid != 0xFF && _ghostLastPos.TryGetValue(hid, out var gp) && (DateTime.UtcNow - gp.AtUtc).TotalSeconds < LeashFreshS)
            { valid = true; x = gp.X; y = gp.Y; z = gp.Z; radius = _leashHostWarnM; }
        }
        // Only on a change (or 5 m of movement) and every 4 s: the DLL forgets a partner after 10 s of silence.
        var last = _leashPartnerSent;
        bool due = valid != last.Valid || (DateTime.UtcNow - last.AtUtc).TotalSeconds >= 4
                   || (valid && (LeashLogic.Dist2D(x, y, last.X, last.Y) > 5 || Math.Abs(radius - last.R) > 0.5f));
        if (!due) return;
        _leashPartnerSent = (valid, x, y, radius, DateTime.UtcNow);
        try { await _combat.SetPartnerAsync(valid, x, y, z, radius); } catch { }
    }
}
