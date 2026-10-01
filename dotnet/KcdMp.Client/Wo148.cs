// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-148: who carries what, as this machine knows it -- the pure half of carrying on the
/// other screen (GameBridge.Wo148.cs does the wire and the game).
///
/// One carrier at a time. The carrier owns the body while it is carried. The host's world
/// decides: a joiner's grab goes to the host first; the host refuses one that its world has
/// already given to someone else (the loser's game is put back), and forwards the rest to the
/// other joiners. On a joiner every grab that arrives comes from the host (its own, or one it
/// forwarded), so it always wins over this player's own carry of the same body.
///
/// Ids are ghost ids; this player's own id is <c>me</c>. A carry with no Held for
/// Protocol.CarryLostAfterMs is over (its body is set down where it is).
/// </summary>
public sealed class CarryLedger
{
    public sealed record Entry(byte Carrier, string What, uint Tok, long SinceMs, long HeardMs, float Px, float Py, float Pz);

    private readonly Dictionary<string, Entry> _byBody = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _lostAt = new(StringComparer.Ordinal);

    /// <summary>A Held from this player's own game this soon after it lost the body (the host gave it to someone
    /// else, or refused it) is that lost carry's last word, not a new grab: the live race sent one, the host
    /// refused it, and the player was told twice.</summary>
    public const long LostQuietMs = 5000;

    /// <summary>This player lost the body (LocalLoses, OnRefused) less than LostQuietMs ago.</summary>
    public bool JustLost(string body, long nowMs) => _lostAt.TryGetValue(body, out long at) && nowMs - at < LostQuietMs;

    public int Count => _byBody.Count;
    public IReadOnlyDictionary<string, Entry> All => _byBody;

    public Entry? Of(string body) => _byBody.TryGetValue(body, out var e) ? e : null;

    /// <summary>True when a carry holds this body (anyone's): the stream and the puppet must not move it.</summary>
    public bool Holds(string body) => _byBody.ContainsKey(body);

    public enum Verdict
    {
        /// <summary>Show it: the carrier's avatar picks the body up here.</summary>
        Apply,
        /// <summary>Already shown for this carrier: nothing to do (a Held, a repeat).</summary>
        Same,
        /// <summary>Host: someone else in this world carries it -- refuse the carrier.</summary>
        Refuse,
        /// <summary>Joiner: the host gave it to someone else -- this player's own carry loses (put down, put back), then show theirs.</summary>
        LocalLoses,
        /// <summary>Nothing (this player's own echo, or a stale message).</summary>
        Ignore,
    }

    /// <summary>
    /// This player picked something up. Returns the carrier who held it before (another
    /// player), if any: on the host this player wins; on a joiner the host will decide.
    /// </summary>
    public byte? OnLocalGrab(byte me, string body, string what, uint tok, long nowMs, float x, float y, float z)
    {
        byte? before = _byBody.TryGetValue(body, out var e) && e.Carrier != me ? e.Carrier : null;
        _byBody[body] = new Entry(me, what, tok, nowMs, nowMs, x, y, z);
        return before;
    }

    /// <summary>A Grab (or a Held) from <paramref name="carrier"/> arrived here.</summary>
    public Verdict OnPeerGrab(byte me, bool iAmHost, byte carrier, string body, string what, uint tok, long nowMs,
                              float x, float y, float z, bool isHeld)
    {
        if (carrier == me) return Verdict.Ignore;   // our own, forwarded back
        if (_byBody.TryGetValue(body, out var e))
        {
            if (e.Carrier == carrier)
            {
                _byBody[body] = e with { HeardMs = nowMs, Tok = tok };
                return Verdict.Same;
            }
            if (iAmHost) return Verdict.Refuse;     // this world gave it to someone else first
            // a joiner: the grab arrived from the host (its own or forwarded) -- the host's word
            bool mine = e.Carrier == me;
            _byBody[body] = new Entry(carrier, what, tok, nowMs, nowMs, x, y, z);
            if (mine) _lostAt[body] = nowMs;
            return mine ? Verdict.LocalLoses : Verdict.Apply;
        }
        _byBody[body] = new Entry(carrier, what, tok, nowMs, nowMs, x, y, z);
        return Verdict.Apply;   // a Held for a carry this machine never saw (a reload, a late join) is a grab too
    }

    /// <summary>A Put from <paramref name="carrier"/>: true when that carrier held the body here (show the set-down).</summary>
    public bool OnPut(byte carrier, string body)
    {
        if (!_byBody.TryGetValue(body, out var e) || e.Carrier != carrier) return false;
        _byBody.Remove(body);
        return true;
    }

    /// <summary>This player set its carry down (or lost it).</summary>
    public bool OnLocalPut(byte me, string body) => OnPut(me, body);

    /// <summary>The host refused this player's carry: true when this player still held it (put it down and back).</summary>
    public bool OnRefused(byte me, string body, long nowMs = 0)
    {
        if (!_byBody.TryGetValue(body, out var e) || e.Carrier != me) return false;
        _byBody.Remove(body);
        _lostAt[body] = nowMs;
        return true;
    }

    /// <summary>A player left: every carry of theirs ends here.</summary>
    public List<(string Body, Entry Entry)> DropCarrier(byte carrier)
    {
        var gone = _byBody.Where(kv => kv.Value.Carrier == carrier).Select(kv => (kv.Key, kv.Value)).ToList();
        foreach (var (b, _) in gone) _byBody.Remove(b);
        return gone;
    }

    /// <summary>Other players' carries not heard from for <paramref name="lostAfterMs"/>: over.</summary>
    public List<(string Body, Entry Entry)> Expire(byte me, long nowMs, long lostAfterMs)
    {
        var gone = _byBody.Where(kv => kv.Value.Carrier != me && nowMs - kv.Value.HeardMs > lostAfterMs)
                          .Select(kv => (kv.Key, kv.Value)).ToList();
        foreach (var (b, _) in gone) _byBody.Remove(b);
        return gone;
    }

    /// <summary>This player's game confirmed its carry (the mod's held, every 2 s): false when no carry of this player's
    /// holds the body here any more (the caller takes it as a new grab).</summary>
    public bool OnLocalHeld(byte me, string body, long nowMs, float x, float y, float z)
    {
        if (!_byBody.TryGetValue(body, out var e) || e.Carrier != me) return false;
        _byBody[body] = e with { HeardMs = nowMs };
        return true;
    }

    /// <summary>This player's own carries the game has not confirmed for <paramref name="lostAfterMs"/>: over (a dead
    /// mod chain, a crash in the middle of a load). A later confirmation is a new grab.</summary>
    public List<(string Body, Entry Entry)> ExpireMine(byte me, long nowMs, long lostAfterMs)
    {
        var gone = _byBody.Where(kv => kv.Value.Carrier == me && nowMs - kv.Value.HeardMs > lostAfterMs)
                          .Select(kv => (kv.Key, kv.Value)).ToList();
        foreach (var (b, _) in gone) _byBody.Remove(b);
        return gone;
    }

    /// <summary>This player's own carries (for the Held heartbeat).</summary>
    public List<(string Body, Entry Entry)> Mine(byte me) =>
        _byBody.Where(kv => kv.Value.Carrier == me).Select(kv => (kv.Key, kv.Value)).ToList();

    public void Clear() { _byBody.Clear(); _lostAt.Clear(); }
}

/// <summary>WO-148: the landing rule, shared by the agent's tests and mirrored in kdcmp.lua (KCD2MP_W148Landing).</summary>
public static class Wo148Rules
{
    /// <summary>A set-down further than this from where the carrier's own game left it is moved there.</summary>
    public const float LandSnapM = 0.5f;
    /// <summary>The ground under a resting body must be within this below it ...</summary>
    public const float GroundBelowM = 0.8f;
    /// <summary>... and not more than this above it (a body under the ground).</summary>
    public const float GroundAboveM = 0.6f;
    /// <summary>A carried copy further than this from the carrier's avatar is not picked up from where it lies.</summary>
    public const float GrabReachM = 2.5f;   // live: the game refuses a pick-up from farther (CanGrabCorpse false)
    /// <summary>... but a copy within this of where the carrier picked it up is moved there first (the same body, drifted).</summary>
    public const float GrabFetchM = 30.0f;

    public enum Land { Keep, MoveToCarrier, PutBack }

    /// <summary>
    /// Where a body this machine set down for another player should end up.
    /// <paramref name="groundAtRest"/>/<paramref name="groundAtCarrier"/>: the height of the
    /// first surface under each point (null = none found within reach). The carrier's own spot
    /// wins when it is reachable here; else where it came to rest, if reachable; else back
    /// where it was picked up.
    /// </summary>
    public static Land Decide(float restX, float restY, float restZ, float? groundAtRest,
                              float carX, float carY, float carZ, float? groundAtCarrier)
    {
        bool restOk = Reachable(restZ, groundAtRest);
        bool carOk = Reachable(carZ, groundAtCarrier);
        float dx = restX - carX, dy = restY - carY, dz = restZ - carZ;
        float d2 = dx * dx + dy * dy + dz * dz;
        if (carOk && d2 > LandSnapM * LandSnapM) return Land.MoveToCarrier;
        if (restOk) return Land.Keep;
        if (carOk) return Land.MoveToCarrier;
        return Land.PutBack;
    }

    /// <summary>A body at <paramref name="z"/> with the first surface under it at <paramref name="ground"/>:
    /// lying on it (not floating above it, not sunk below it).</summary>
    public static bool Reachable(float z, float? ground)
    {
        if (ground is not float g || !float.IsFinite(g) || !float.IsFinite(z)) return false;
        return z - g <= GroundBelowM && g - z <= GroundAboveM;
    }
}

/// <summary>
/// WO-148: the mod's w148_carry event (kdcmp.lua: KCD2MP_W148Tick and the object tick):
/// "grab &lt;what&gt; &lt;name&gt; x y z", "held &lt;what&gt; &lt;name&gt; x y z" (the game's 2 s
/// confirmation) or "put &lt;how&gt; &lt;what&gt; &lt;name&gt; x y z". The first live run found the
/// agent counting these one word short, so every one of them was "malformed" and this player's
/// own carry never left the machine; the format lives here, pinned by a test with the strings
/// the game logged.
/// </summary>
public sealed record CarryLocalEvent(string Op, string How, string What, string Name, float X, float Y, float Z)
{
    public bool IsGrab => Op == "grab";
    public bool IsHeld => Op == "held";
    public bool IsPut => Op == "put";

    public static bool TryParse(string? arg, out CarryLocalEvent ev)
    {
        ev = null!;
        var p = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int o;   // the index of <what>
        string how = "";
        if (p.Length == 6 && (p[0] == "grab" || p[0] == "held")) o = 1;
        else if (p.Length == 7 && p[0] == "put") { o = 2; how = p[1]; }
        else return false;
        string what = p[o], name = p[o + 1];
        if (!float.TryParse(p[o + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            || !float.TryParse(p[o + 3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            || !float.TryParse(p[o + 4], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
            || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)
            || !CarryText.IsWhat(what) || !CarryText.IsName(name) || (o == 2 && !CarryText.IsHow(how)))
            return false;
        ev = new CarryLocalEvent(p[0], how, what, name, x, y, z);
        return true;
    }
}
