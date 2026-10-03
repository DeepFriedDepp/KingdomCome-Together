// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;

namespace KcdMp.Client;

/// <summary>
/// WO-154 Phase 1: a quest State that each world re-derives by itself.
///
/// Until 0.44.0 a quest change made on an AI worker thread (a distance trigger, a duel's state, a
/// farmhand's evening) carried no port, so it was never mirrored either way: the joiner's mirror
/// applied 5 of 54 of the host's changes in the field evening, and none of his own fight steps
/// reached the host. With the port recorded on every thread (native wo137.cpp) those changes now
/// mirror both ways -- and a State whose value each world computes from its OWN player (how close
/// he stands to something) would bounce: the joiner's value goes to the host, the host's world puts
/// its own value back, that comes to the joiner, his world puts his back, and so on.
///
/// The rule: when the other world's value was put on a State here (a host change applied on a
/// joiner, a joiner's request applied on the host) and this world's own graph puts the State
/// straight back to the value it had before, within <see cref="WindowMs"/>, the State is
/// CONTESTED for the rest of the session: each world keeps its own value, nothing about it is
/// applied, asked or corrected any more. A story step is never put back like that (a counter that
/// moves on is progress, not a return), so only per-player States are caught; the first bounce is
/// the cost of finding one.
/// </summary>
public sealed class Wo154Contest
{
    public const long WindowMs = 30_000;
    public const int MaxTracked = 2000;

    private readonly object _lock = new();
    private readonly Dictionary<string, (int Before, int After, long AtMs)> _imposed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _contested = new(StringComparer.Ordinal);

    public int Count { get { lock (_lock) return _contested.Count; } }

    /// <summary>The other world's value was put on this State here.</summary>
    public void NoteImposed(string path, int before, int after, long nowMs)
    {
        if (before == after || string.IsNullOrEmpty(path)) return;
        lock (_lock)
        {
            if (_contested.ContainsKey(path)) return;
            _imposed[path] = (before, after, nowMs);
            if (_imposed.Count > MaxTracked)
                foreach (var k in _imposed.Where(kv => nowMs - kv.Value.AtMs > WindowMs).Select(kv => kv.Key).ToList())
                    _imposed.Remove(k);
        }
    }

    /// <summary>
    /// This world's own change (a root, never a mirror). True when it is a NEW contest: the State went
    /// straight back from the imposed value to the one before it, within the window.
    /// </summary>
    public bool NoteLocal(string path, int oldVal, int newVal, long nowMs, out string why)
    {
        why = "";
        lock (_lock)
        {
            if (_contested.ContainsKey(path) || !_imposed.TryGetValue(path, out var im)) return false;
            if (nowMs - im.AtMs > WindowMs) { _imposed.Remove(path); return false; }
            if (oldVal != im.After || newVal != im.Before) return false;
            why = string.Format(CultureInfo.InvariantCulture, "this world put it back from {0} to {1} {2:F1} s after the other world's value arrived",
                                im.After, im.Before, (nowMs - im.AtMs) / 1000.0);
            _contested[path] = why;
            _imposed.Remove(path);
            return true;
        }
    }

    public bool IsContested(string path) { lock (_lock) return _contested.ContainsKey(path); }

    /// <summary>The other side found it contested. True when it is new here.</summary>
    public bool Mark(string path, string why)
    {
        lock (_lock)
        {
            _imposed.Remove(path);
            return _contested.TryAdd(path, why);
        }
    }

    public void Reset()
    {
        lock (_lock) { _imposed.Clear(); _contested.Clear(); }
    }
}

/// <summary>WO-154: the pure rules of this WO (docs/WO-154-findings.md).</summary>
public static class Wo154Rules
{
    /// <summary>The host's verdict for a joiner's request on a contested State (the joiner stops asking and correcting it).</summary>
    public const string VerdictContested = "contested";
}
