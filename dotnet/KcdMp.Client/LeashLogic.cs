using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-114: the host's leash, the pure half (docs/WO-114-findings.md). Time,
/// distance and the hold reasons come in; what to tell the joiner comes out.
/// GameBridge.Wo114 owns the wire and the log; this owns the rules, and
/// Wo114Tests pins them.
///
/// Rules (the maintainer's numbers; never moved without the maintainer):
///   * distance = horizontal (2D) host to joiner, the leash recorder's measure;
///   * past WarnM (600): one warning per excursion; it re-arms only after the
///     joiner has been back under WarnM - 50 (550);
///   * past PullM (650): a 10 s countdown, one message a second; back inside
///     PullM cancels it; at zero, a Pull (the joiner is brought beside the host);
///   * any hold reason (either player downed, loading, in a cutscene, a
///     dialogue or a menu; the host in a non-Henry stretch or reloading; no
///     fresh joiner position) freezes the countdown where it was and starts
///     nothing new; it resumes from the same second;
///   * the host fast-travelled: a Pull at once (no countdown) as soon as
///     nothing holds it, unless the joiner is already within 50 m;
///   * a pull that fails (not placed, no answer in 12 s) waits 20 s before
///     the next countdown; three failures in a row pause the pulls (warnings
///     only) -- fail closed. A "busy" refusal (the joiner became busy as the
///     pull arrived) is not a failure.
///
/// WO-147 (the first long two-player session: the pull almost never came):
///   * only a state that makes a pull unsafe for the JOINER holds it: the
///     joiner loading, in a dialogue or a cutscene, downed or respawning, or
///     the host loading (<see cref="Blocking"/>). The host's menus, dialogues
///     and cutscenes no longer hold (the host's world runs behind them since
///     WO-138), nor does the joiner's menu (the pull places him while it is
///     open);
///   * a hold lasts at most <see cref="HoldCapMs"/>; then the countdown runs
///     anyway and its pull is FORCED (<see cref="Protocol.LeashReasonForced"/>):
///     the joiner ends a dialogue first. A load is never capped (no world to
///     be placed in), nor a missing position (nothing to measure);
///   * the pull waits for its answer the link's own round trip, not a flat
///     12 s (<see cref="PullTimeoutMs"/>): a 20 s backlog is not a failure;
///   * three failures pause the pulls for <see cref="FailPauseMs"/>, then they
///     come back by themselves (was: for the rest of the session);
///   * a fast-travel pull is owed at most <see cref="FastTravelOwedMaxMs"/>; then
///     the distance rules take over (the field owed one for 39 minutes, and it
///     would have fired against a partner 250 m away).
/// </summary>
public sealed class LeashLogic
{
    public const int CountdownSeconds = 10;
    public const float RearmMarginM = 50f;
    /// <summary>WO-131 Phase 3: a running countdown is cancelled only back under PullM minus this (650 -> 630): no flapping on the line.</summary>
    public const float CancelMarginM = 20f;
    public const int DefaultPullTimeoutMs = 12_000;
    public const int FailCooldownMs = 20_000;
    public const int MaxFailures = 3;
    /// <summary>WO-147: three failures in a row pause the pulls this long (warnings stay), then they re-arm.</summary>
    public const int FailPauseMs = 180_000;
    public const float FastTravelMinM = 50f;
    public const float WarnDefaultM = 600f, PullDefaultM = 650f;
    /// <summary>A tick gap longer than this (an agent hitch) never eats more countdown than this.</summary>
    public const int MaxTickStepMs = 2000;
    /// <summary>WO-147: no hold lasts longer than this; then the countdown runs and its pull is forced.</summary>
    public const int HoldCapMs = 60_000;
    /// <summary>WO-147: an owed fast-travel pull lapses after this; the distance rules take over.</summary>
    public const int FastTravelOwedMaxMs = 120_000;

    [Flags]
    public enum Hold
    {
        None = 0,
        HostDowned = 1 << 0, JoinerDowned = 1 << 1,
        HostLoading = 1 << 2, JoinerLoading = 1 << 3,
        HostCutscene = 1 << 4, JoinerCutscene = 1 << 5,
        HostDialogue = 1 << 6, JoinerDialogue = 1 << 7,
        HostMenu = 1 << 8, JoinerMenu = 1 << 9,
        NonHenry = 1 << 10, HostReloading = 1 << 11,
        NoJoinerPosition = 1 << 12,
        HostTravelling = 1 << 13,   // the host's fast travel is running (the pull follows on arrival)
    }

    /// <summary>
    /// WO-147: the holds that stop a pull -- a pull is unsafe for the joiner (loading, a dialogue or a
    /// cutscene, downed or respawning), the host's world is not there (loading, reloading, a fast travel
    /// under way, a non-Henry stretch), or the joiner's position is unknown. Every other reason (the host's
    /// menu, dialogue, cutscene or down; the joiner's menu) is reported by the callers but holds nothing.
    /// </summary>
    public const Hold Blocking = Hold.JoinerLoading | Hold.JoinerDialogue | Hold.JoinerCutscene | Hold.JoinerDowned
                               | Hold.HostLoading | Hold.HostReloading | Hold.HostTravelling | Hold.NonHenry | Hold.NoJoinerPosition;

    /// <summary>WO-147: the holds <see cref="HoldCapMs"/> never ends: a load (no world to be placed in) and a missing position.</summary>
    public const Hold Uncapped = Hold.JoinerLoading | Hold.HostLoading | Hold.HostReloading | Hold.NonHenry | Hold.NoJoinerPosition;

    public readonly record struct Settings(bool On, float WarnM, float PullM)
    {
        public static Settings Default => new(true, WarnDefaultM, PullDefaultM);
    }

    public enum Act { Warn, Countdown, Cancel, Pull, Hold, PullResult, Disarmed, AlreadyBeside, HoldCapped, Rearmed, FastTravelExpired }

    /// <summary>
    /// One thing to do. Countdown: Arg = seconds left. Pull: Arg = pull seq, Reason = LeashReason* (| LeashReasonForced).
    /// Hold: Arg = seconds left (0 = no countdown running). HoldCapped: Arg = the seconds it was held.
    /// FastTravelExpired: Arg = the seconds it was owed.
    /// </summary>
    public readonly record struct Action(Act Kind, int Arg = 0, ushort Reason = 0, string Note = "");

    private Settings _cfg = Settings.Default;
    private bool _warnArmed = true;
    private int? _remainingMs;
    private int _lastSentSecond = -1;
    private long _lastMs = -1;
    private Hold _holdNoted = Hold.None;
    private byte _pullSeq;
    private long _pullSentMs = -1;
    private ushort _pullReason;
    private bool _fastTravelPending;
    private long _fastTravelSinceMs = -1;   // WO-147: the first tick that owed it
    private long _cooldownUntilMs;
    private int _failures;
    private bool _disarmed;
    private long _pausedUntilMs;
    private long _heldSinceMs = -1;   // WO-147: when the current (blocking) hold began
    private bool _capped;             // WO-147: the current hold ran past HoldCapMs -- it holds nothing now

    public Settings Config
    {
        get => _cfg;
        set
        {
            bool wasOn = _cfg.On;
            _cfg = value;
            if (value.On && !wasOn) Reset();   // turning it back on clears a disarm
        }
    }

    public bool WarnArmed => _warnArmed;
    public bool CountdownActive => _remainingMs.HasValue;
    public int SecondsLeft => _remainingMs is int r ? (r + 999) / 1000 : 0;
    public bool PullInFlight => _pullSentMs >= 0;
    public byte PullSeq => _pullSeq;
    public ushort PullReason => _pullReason;
    public bool FastTravelPending => _fastTravelPending;
    public bool Disarmed => _disarmed;
    public int Failures => _failures;
    /// <summary>WO-147: the current hold ran past <see cref="HoldCapMs"/> and holds nothing now.</summary>
    public bool HoldIsCapped => _capped;

    /// <summary>
    /// WO-147: how long a pull waits for its answer. The host sets it from the link's own round trip each
    /// tick (<see cref="TimeoutForRtt"/>): the field's 12 s flat timeout counted a 20 s backlog as a failure.
    /// </summary>
    public int PullTimeoutMs { get; set; } = DefaultPullTimeoutMs;

    /// <summary>WO-147: the hold cap in use (mp_leash_cap_s on the host); 0 = a hold is never capped.</summary>
    public int CapMs { get; set; } = HoldCapMs;

    /// <summary>WO-147: 12 s, or three round trips plus 2 s when the link is slower than that (capped at 90 s).</summary>
    public static int TimeoutForRtt(double? rttMs) =>
        rttMs is double r && double.IsFinite(r) && r > 0 ? (int)Math.Clamp(3 * r + 2000, DefaultPullTimeoutMs, 90_000) : DefaultPullTimeoutMs;

    /// <summary>A new joiner, a disconnect, mp_leash back on: everything starts over.</summary>
    public void Reset()
    {
        _warnArmed = true; _remainingMs = null; _lastSentSecond = -1; _holdNoted = Hold.None;
        _pullSentMs = -1; _fastTravelPending = false; _cooldownUntilMs = 0; _failures = 0; _disarmed = false;
        _pausedUntilMs = 0; _heldSinceMs = -1; _capped = false; _fastTravelSinceMs = -1;
    }

    /// <summary>The host arrived from a fast travel: the next free tick pulls the joiner beside it.</summary>
    public void NoteHostFastTravel() { _fastTravelPending = true; _fastTravelSinceMs = -1; }

    public List<Action> Tick(long nowMs, double? distM, Hold hold)
    {
        var acts = new List<Action>();
        long dt = _lastMs < 0 ? 0 : Math.Clamp(nowMs - _lastMs, 0, MaxTickStepMs);
        _lastMs = nowMs;

        if (!_cfg.On)
        {
            if (_remainingMs is not null) { _remainingMs = null; acts.Add(new Action(Act.Cancel, Note: "leash-off")); }
            _fastTravelPending = false; _fastTravelSinceMs = -1;
            _heldSinceMs = -1; _capped = false;
            return acts;
        }
        if (distM is null) hold |= Hold.NoJoinerPosition;
        // WO-147: only what makes a pull unsafe holds it; the rest is the callers' to report.
        hold &= Blocking;

        // WO-147: the pause after three failures ends by itself.
        if (_disarmed && nowMs >= _pausedUntilMs)
        {
            _disarmed = false; _failures = 0;
            acts.Add(new Action(Act.Rearmed, FailPauseMs / 1000));
        }

        // A pull is out: wait for the joiner's answer (OnPullResult) or give up.
        if (_pullSentMs >= 0)
        {
            if (nowMs - _pullSentMs <= PullTimeoutMs) return acts;
            Fail(nowMs, "no-answer", acts);
        }

        // WO-147: the hold cap. A hold that has lasted HoldCapMs holds nothing any more (a load or a
        // missing position excepted); the countdown runs, and its pull is forced.
        if (hold == Hold.None) { _heldSinceMs = -1; _capped = false; }
        else
        {
            if (_heldSinceMs < 0) _heldSinceMs = nowMs;
            if (!_capped && CapMs > 0 && (hold & Uncapped) == Hold.None && nowMs - _heldSinceMs >= CapMs)
            {
                _capped = true;
                acts.Add(new Action(Act.HoldCapped, (int)((nowMs - _heldSinceMs) / 1000), Note: HoldText(hold)));
            }
            if (_capped && (hold & Uncapped) != Hold.None) _capped = false;   // a load began meanwhile: it holds
        }

        double d = distM ?? 0;
        bool held = hold != Hold.None && !_capped;
        if (distM is double dd && dd < _cfg.WarnM - RearmMarginM) _warnArmed = true;

        // The host fast-travelled: straight to a pull, no countdown.
        if (_fastTravelPending)
        {
            _remainingMs = null;
            if (_fastTravelSinceMs < 0) _fastTravelSinceMs = nowMs;
            if (nowMs - _fastTravelSinceMs >= FastTravelOwedMaxMs)
            {
                // WO-147: too old to be "come along" any more -- the distance rules decide from here.
                acts.Add(new Action(Act.FastTravelExpired, (int)((nowMs - _fastTravelSinceMs) / 1000), Note: $"{d:F0} m"));
                _fastTravelPending = false; _fastTravelSinceMs = -1; _holdNoted = Hold.None;
                return acts;
            }
            if (held) { NoteHold(hold, acts); return acts; }
            _holdNoted = Hold.None;
            _fastTravelPending = false;
            if (d < FastTravelMinM) { acts.Add(new Action(Act.AlreadyBeside, Note: $"{d:F0} m")); return acts; }
            if (_disarmed) return acts;
            StartPull(nowMs, (ushort)(Protocol.LeashReasonFastTravel | ForcedBit), acts);
            return acts;
        }

        if (_remainingMs is int rem)
        {
            if (!hold.HasFlag(Hold.NoJoinerPosition) && d < _cfg.PullM - CancelMarginM)
            {
                _remainingMs = null; _holdNoted = Hold.None;
                acts.Add(new Action(Act.Cancel, Note: "back-inside"));
                return acts;
            }
            if (held) { NoteHold(hold, acts); return acts; }   // frozen where it was
            if (_holdNoted != Hold.None) { _holdNoted = Hold.None; _lastSentSecond = -1; dt = 0; }   // resumed: re-send this second
            rem -= (int)dt;
            if (rem <= 0) { _remainingMs = null; StartPull(nowMs, (ushort)(Protocol.LeashReasonDistance | ForcedBit), acts); return acts; }
            _remainingMs = rem;
            int sec = (rem + 999) / 1000;
            if (sec != _lastSentSecond) { _lastSentSecond = sec; acts.Add(new Action(Act.Countdown, sec)); }
            return acts;
        }

        if (held)
        {
            if (d >= _cfg.PullM) NoteHold(hold, acts);   // a pull would be due: say why it is not
            else _holdNoted = Hold.None;
            return acts;
        }
        _holdNoted = Hold.None;
        if (nowMs < _cooldownUntilMs) return acts;

        if (d >= _cfg.PullM)
        {
            if (_warnArmed) { _warnArmed = false; acts.Add(new Action(Act.Warn, (int)_cfg.WarnM)); }
            if (_disarmed) return acts;
            _remainingMs = CountdownSeconds * 1000;
            _lastSentSecond = CountdownSeconds;
            acts.Add(new Action(Act.Countdown, CountdownSeconds));
            return acts;
        }
        if (d >= _cfg.WarnM && _warnArmed)
        {
            _warnArmed = false;
            acts.Add(new Action(Act.Warn, (int)_cfg.WarnM));
        }
        return acts;
    }

    /// <summary>The joiner's answer to pull <paramref name="seq"/> (LeashState result).</summary>
    public List<Action> OnPullResult(long nowMs, byte seq, byte result)
    {
        var acts = new List<Action>();
        if (_pullSentMs < 0 || seq != _pullSeq || result == Protocol.LeashResultNone) return acts;
        _pullSentMs = -1;
        if (result == Protocol.LeashResultPlaced)
        {
            _failures = 0;
            acts.Add(new Action(Act.PullResult, seq, _pullReason, "placed"));
            return acts;
        }
        if (result == Protocol.LeashResultBusy)
        {
            // Not a failure: the joiner became busy as it landed. A fast-travel pull
            // is owed again; a distance pull restarts its countdown when free.
            if (Protocol.LeashReasonBase(_pullReason) == Protocol.LeashReasonFastTravel) _fastTravelPending = true;
            acts.Add(new Action(Act.PullResult, seq, _pullReason, "busy"));
            return acts;
        }
        Fail(nowMs, Protocol.LeashResultName(result), acts);
        return acts;
    }

    /// <summary>WO-147: a pull after the hold cap carries the forced bit (the joiner ends a dialogue first).</summary>
    private ushort ForcedBit => _capped ? Protocol.LeashReasonForced : (ushort)0;

    private void StartPull(long nowMs, ushort reason, List<Action> acts)
    {
        _pullSeq = (byte)(_pullSeq == 255 ? 1 : _pullSeq + 1);
        _pullSentMs = nowMs;
        _pullReason = reason;
        acts.Add(new Action(Act.Pull, _pullSeq, reason));
    }

    private void Fail(long nowMs, string why, List<Action> acts)
    {
        _pullSentMs = -1;
        _failures++;
        _cooldownUntilMs = nowMs + FailCooldownMs;
        acts.Add(new Action(Act.PullResult, _pullSeq, _pullReason, "failed:" + why));
        if (_failures >= MaxFailures && !_disarmed)
        {
            _disarmed = true;
            _pausedUntilMs = nowMs + FailPauseMs;   // WO-147: a pause, not the rest of the session
            _remainingMs = null;
            acts.Add(new Action(Act.Disarmed, _failures, Note: why));
        }
    }

    private void NoteHold(Hold hold, List<Action> acts)
    {
        if (hold == _holdNoted) return;
        _holdNoted = hold;
        acts.Add(new Action(Act.Hold, SecondsLeft, Note: HoldText(hold)));
    }

    public static string HoldText(Hold h)
    {
        if (h == Hold.None) return "none";
        var parts = new List<string>();
        foreach (Hold f in Enum.GetValues<Hold>())
            if (f != Hold.None && h.HasFlag(f)) parts.Add(f switch
            {
                Hold.HostDowned => "host-downed", Hold.JoinerDowned => "joiner-downed",
                Hold.HostLoading => "host-loading", Hold.JoinerLoading => "joiner-loading",
                Hold.HostCutscene => "host-cutscene", Hold.JoinerCutscene => "joiner-cutscene",
                Hold.HostDialogue => "host-dialogue", Hold.JoinerDialogue => "joiner-dialogue",
                Hold.HostMenu => "host-menu", Hold.JoinerMenu => "joiner-menu",
                Hold.NonHenry => "non-henry", Hold.HostReloading => "host-reloading",
                Hold.NoJoinerPosition => "no-joiner-position", Hold.HostTravelling => "host-travelling", _ => f.ToString(),
            });
        return string.Join(',', parts);
    }

    /// <summary>Joiner LeashState flags -> the joiner's hold reasons (not in the host's world = loading).</summary>
    public static Hold JoinerHold(ushort flags)
    {
        var h = Hold.None;
        if ((flags & Protocol.LeashFlagInWorld) == 0 || (flags & Protocol.LeashFlagLoading) != 0) h |= Hold.JoinerLoading;
        if ((flags & Protocol.LeashFlagDowned) != 0) h |= Hold.JoinerDowned;
        if ((flags & Protocol.LeashFlagCutscene) != 0) h |= Hold.JoinerCutscene;
        if ((flags & Protocol.LeashFlagDialogue) != 0) h |= Hold.JoinerDialogue;
        if ((flags & Protocol.LeashFlagMenu) != 0) h |= Hold.JoinerMenu;
        return h;
    }

    /// <summary>Horizontal distance, the leash recorder's measure (LeashRowBuilder.Dist2D).</summary>
    public static double Dist2D(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    /// <summary>
    /// The on-screen words (plain, as the WO sketches them). Joiner lines for
    /// Warn/Countdown/Cancel/Pull; the host's own line for a warning.
    /// </summary>
    public static class Text
    {
        public const string JoinerWarn = "You're getting far from your host. Head back, or you'll be brought back.";
        public static string JoinerCountdown(int s) => $"Bringing you back to your host in {s}...";
        public const string JoinerCancel = "You're back near your host.";
        public const string JoinerHold = "Bringing you back to your host once you're free.";
        public const string JoinerPulledDistance = "You were brought back to your host.";
        public const string JoinerPulledFastTravel = "Your host fast-travelled.";
        public const string JoinerFastTravelBlocked = "Only the host can fast travel in co-op.";
        public static string HostWarn(string partner) => $"{partner} is getting far away.";
        public static string HostPulled(string partner) => $"{partner} was brought back to you.";
    }
}
