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
///     the next countdown; three failures in a row stop the pulls for the
///     session (warnings only) -- fail closed. A "busy" refusal (the joiner
///     became busy as the pull arrived) is not a failure.
/// </summary>
public sealed class LeashLogic
{
    public const int CountdownSeconds = 10;
    public const float RearmMarginM = 50f;
    public const int PullTimeoutMs = 12_000;
    public const int FailCooldownMs = 20_000;
    public const int MaxFailures = 3;
    public const float FastTravelMinM = 50f;
    public const float WarnDefaultM = 600f, PullDefaultM = 650f;
    /// <summary>A tick gap longer than this (an agent hitch) never eats more countdown than this.</summary>
    public const int MaxTickStepMs = 2000;

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
    }

    public readonly record struct Settings(bool On, float WarnM, float PullM)
    {
        public static Settings Default => new(true, WarnDefaultM, PullDefaultM);
    }

    public enum Act { Warn, Countdown, Cancel, Pull, Hold, PullResult, Disarmed, AlreadyBeside }

    /// <summary>One thing to do. Countdown: Arg = seconds left. Pull: Arg = pull seq, Reason = LeashReason*. Hold: Arg = seconds left (0 = no countdown running).</summary>
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
    private long _cooldownUntilMs;
    private int _failures;
    private bool _disarmed;

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

    /// <summary>A new joiner, a disconnect, mp_leash back on: everything starts over.</summary>
    public void Reset()
    {
        _warnArmed = true; _remainingMs = null; _lastSentSecond = -1; _holdNoted = Hold.None;
        _pullSentMs = -1; _fastTravelPending = false; _cooldownUntilMs = 0; _failures = 0; _disarmed = false;
    }

    /// <summary>The host arrived from a fast travel: the next free tick pulls the joiner beside it.</summary>
    public void NoteHostFastTravel() => _fastTravelPending = true;

    public List<Action> Tick(long nowMs, double? distM, Hold hold)
    {
        var acts = new List<Action>();
        long dt = _lastMs < 0 ? 0 : Math.Clamp(nowMs - _lastMs, 0, MaxTickStepMs);
        _lastMs = nowMs;

        if (!_cfg.On)
        {
            if (_remainingMs is not null) { _remainingMs = null; acts.Add(new Action(Act.Cancel, Note: "leash-off")); }
            _fastTravelPending = false;
            return acts;
        }
        if (distM is null) hold |= Hold.NoJoinerPosition;

        // A pull is out: wait for the joiner's answer (OnPullResult) or give up.
        if (_pullSentMs >= 0)
        {
            if (nowMs - _pullSentMs <= PullTimeoutMs) return acts;
            Fail(nowMs, "no-answer", acts);
        }

        double d = distM ?? 0;
        bool held = hold != Hold.None;
        if (distM is double dd && dd < _cfg.WarnM - RearmMarginM) _warnArmed = true;

        // The host fast-travelled: straight to a pull, no countdown.
        if (_fastTravelPending)
        {
            _remainingMs = null;
            if (held) { NoteHold(hold, acts); return acts; }
            _holdNoted = Hold.None;
            _fastTravelPending = false;
            if (d < FastTravelMinM) { acts.Add(new Action(Act.AlreadyBeside, Note: $"{d:F0} m")); return acts; }
            if (_disarmed) return acts;
            StartPull(nowMs, Protocol.LeashReasonFastTravel, acts);
            return acts;
        }

        if (_remainingMs is int rem)
        {
            if (!hold.HasFlag(Hold.NoJoinerPosition) && d < _cfg.PullM)
            {
                _remainingMs = null; _holdNoted = Hold.None;
                acts.Add(new Action(Act.Cancel, Note: "back-inside"));
                return acts;
            }
            if (held) { NoteHold(hold, acts); return acts; }   // frozen where it was
            if (_holdNoted != Hold.None) { _holdNoted = Hold.None; _lastSentSecond = -1; dt = 0; }   // resumed: re-send this second
            rem -= (int)dt;
            if (rem <= 0) { _remainingMs = null; StartPull(nowMs, Protocol.LeashReasonDistance, acts); return acts; }
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
            if (_pullReason == Protocol.LeashReasonFastTravel) _fastTravelPending = true;
            acts.Add(new Action(Act.PullResult, seq, _pullReason, "busy"));
            return acts;
        }
        Fail(nowMs, Protocol.LeashResultName(result), acts);
        return acts;
    }

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
                Hold.NoJoinerPosition => "no-joiner-position", _ => f.ToString(),
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
