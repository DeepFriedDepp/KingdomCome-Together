// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Diagnostics;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-203B (docs/WO-203B-findings.md): the listener sees the starter's conversation camera, in the minimal Act I session it needs.
//   THE STARTER (this player's own conversation, from the engine's "New dialogue" line): Announce to the partner(s); a partner's
//   Join gets a Snapshot; once the engine's dialogue camera is up ("Dialog camera activated.") and someone listens, the DLL samples
//   the rendered view relative to the conversation's NPC (op 95) and this sends the samples as ConvCam at most 30 Hz; the
//   conversation's end goes out as End.
//   THE LISTENER: an Announce becomes the "Join conversation" prompt (Lua); the prompt's join goes to the starter; the Snapshot turns
//   the view on (op 94) and the mod's listening state (dialog lock, HUD); ConvCam samples go to the DLL on this machine's clock.
//   Every way out turns the view off first: the starter's End, the listen cancel / combat / a hit (Lua), the DLL's own (3 s of
//   silence, a fault, the view lost), the ESC menu, a load, the starter gone, a shared cutscene (the view yields and comes back).
// Two players (host + joiner) in either role; with more, a conversation is shared between the host and the starter only.
// Switches (Lua, said here as w203_cfg): mp_join_conversation (the whole session), mp_conv_camera (the view; off = no view).
public partial class GameBridge
{
    private sealed class W203Talk   // the STARTER's conversation
    {
        public int Cid;
        public string Kind = "", Anchor = "";
        public string[] Others = [];
        public bool CamUp, Armed;
        public readonly HashSet<byte> Told = [], Listeners = [];
        public CancellationTokenSource? Pump;
        public int Seq;
    }

    private sealed class W203Listen   // the LISTENER's state
    {
        public byte Starter;
        public int Cid;
        public string Anchor = "";
        public bool Joining, Listening, ViewOn, SceneHold;
        public long AskedMs;
        public CancellationTokenSource? Watch;
    }

    private readonly object _w3cLock = new();
    private W203Talk? _w3cTalk;
    private W203Listen? _w3cListen;
    private readonly Dictionary<byte, (int Cid, string Anchor)> _w3cOffers = [];
    private volatile bool _w3cJoinOn = true, _w3cCameraOn = true;
    private long _w207CamHolds;   // WO-207 A.1: the last shot re-sent while the starter's own camera had no dialogue shot
    private long _w3cAnnounced, _w3cJoinsIn, _w3cSnapshots, _w3cCamOut, _w3cSamplesOut, _w3cCamIn, _w3cSamplesIn, _w3cViewOn, _w3cViewRefused,
                 _w3cLeavesOut, _w3cEndsOut, _w3cEndsIn, _w3cNativeOut, _w3cBad;

    private static long QpcMs() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;
    private static long UnixMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private byte W3cPeerOf(byte starterOrListener) => W137Host ? starterOrListener : Protocol.JoinTargetHost;

    // ================================================================ the starter

    private void Wo203CamOnEngineLine(string line)
    {
        if (line.StartsWith("New dialogue '", StringComparison.Ordinal))
        {
            if (Wo203CamRules.ParseTalk(line) is not { } t || !Wo203CamRules.JoinableKind(t.Kind) || !_w3cJoinOn || !W203Partnered) return;
            var talk = new W203Talk { Cid = t.Cid, Kind = t.Kind, Anchor = t.Others[0], Others = t.Others };
            W203Talk? old;
            lock (_w3cLock) { old = _w3cTalk; _w3cTalk = talk; }
            if (old is not null) W3cEndTalk(old, "ended");
            var to = W137Host ? Wo134Peers() : [Protocol.JoinTargetHost];
            lock (_w3cLock) foreach (byte p in to) talk.Told.Add(p);
            Interlocked.Increment(ref _w3cAnnounced);
            Interlocked.Increment(ref _w204TalksAnnounced);
            Console.WriteLine($"WO203B-TALK this player's conversation {t.Cid} ({t.Kind}) with {string.Join(',', t.Others)} -- announced to {to.Count} partner(s)");
            string text = Wo203CamRules.AnnounceText(t.Cid, t.Kind, true, talk.Anchor, t.Others);
            _ = Task.Run(async () => { foreach (byte p in to) await Wo203SendConvAsync(p, Protocol.ConvAnnounce, text); });
            return;
        }
        if (Wo203CamRules.IsCameraActivated(line))
        {
            W203Talk? talk;
            lock (_w3cLock) { talk = _w3cTalk; if (talk is not null) talk.CamUp = true; }
            if (talk is not null) W3cArmIfListened(talk);
            return;
        }
        int end = Wo203ConvRules.ParseEnd(line);
        if (end >= 0)
        {
            W203Talk? talk;
            lock (_w3cLock) { talk = _w3cTalk; if (talk is null || talk.Cid != end) return; _w3cTalk = null; }
            W3cEndTalk(talk, "ended");
        }
    }

    private void W3cEndTalk(W203Talk talk, string reason)
    {
        byte[] told;
        lock (_w3cLock) { told = [.. talk.Told]; talk.Listeners.Clear(); }
        talk.Pump?.Cancel();
        if (talk.Armed) { talk.Armed = false; _ = W3cNativeAsync(Protocol.Wo203OpConvCamPoll, [0]); }
        string text = Wo203CamRules.EndText(talk.Cid, reason);
        Interlocked.Add(ref _w3cEndsOut, told.Length);
        Console.WriteLine($"WO203B-TALK conversation {talk.Cid} {reason} -- End to {told.Length} partner(s)");
        _ = Task.Run(async () => { foreach (byte p in told) await Wo203SendConvAsync(p, Protocol.ConvEnd, text); });
    }

    private void W3cArmIfListened(W203Talk talk)
    {
        lock (_w3cLock) { if (!talk.CamUp || talk.Armed || talk.Listeners.Count == 0 || !_w3cCameraOn) return; talk.Armed = true; }
        _ = Task.Run(async () =>
        {
            byte[]? arm = Wo203CamRules.NameCmd(4, talk.Anchor, 0);
            var r = arm is null ? null : await W3cNativeAsync(Protocol.Wo203OpConvCamPoll, arm);
            if (r is not { Ok: true } || r.Value.Payload.Length < 1 || r.Value.Payload[0] != 0)
            {
                talk.Armed = false;
                Console.WriteLine($"WO203B-CAMPOLL the sampler did not arm on '{talk.Anchor}' (result {(r is { } x && x.Payload.Length > 0 ? x.Payload[0] : -1)}) -- no camera for the listener");
                return;
            }
            var cts = new CancellationTokenSource();
            talk.Pump = cts;
            Console.WriteLine($"WO203B-CAMPOLL armed on '{talk.Anchor}' for conversation {talk.Cid}");
            await W3cPumpAsync(talk, cts.Token);
        });
    }

    /// <summary>Every 33 ms: the DLL's new samples, on the host's clock, at most 3 to a message, to every listener.</summary>
    private async Task W3cPumpAsync(W203Talk talk, CancellationToken ct)
    {
        var pending = new List<Wo203CamRules.CamSample>();
        // WO-207 A.1 (mp_conv_pipe_first): the pump's calls go first at the pipe's gate, and its poll is the DLL's off-main one (cmd 2: read
        // from the sampler's ring on the pipe's thread); off (the twin): a background caller, the poll on the game's main thread (cmd 9)
        bool first = W207("conv_pipe_first");
        if (first) Wo207Trace.Kind.Value = Wo207Trace.ConvKind;
        // WO-207 A.1 (mp_conv_cam_hold): the starter's own camera can leave the dialogue's shots while the talk goes on (a trade's screen, the
        // bench's innkeeper) -- the sampler never sends a first-person view, and after 3 s the listener's view ended for silence. While the
        // talk runs, a second without a new sample re-sends the last shot at the current time: the listener holds it.
        bool hold = W207("conv_hold");
        Wo203CamRules.CamSample? last = null;
        long lastSentMs = Environment.TickCount64;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(33, ct); } catch (OperationCanceledException) { break; }
            var r = await W3cNativeAsync(Protocol.Wo203OpConvCamPoll, [first ? (byte)2 : (byte)9]);
            if (r is not { Ok: true }) continue;
            long qpc = QpcMs(), unix = UnixMs();
            foreach (var (t, s) in Wo203CamRules.DecodePoll(r.Value.Payload))
                pending.Add(s with { Seq = ++talk.Seq, T = Wo203CamRules.HostMs(t, qpc, unix, ClockOffsetMs) });
            if (pending.Count == 0 && hold && last is { } l && Environment.TickCount64 - lastSentMs >= 1000)
            {
                pending.Add(l with { Seq = ++talk.Seq, T = Wo203CamRules.HostMs(qpc, qpc, unix, ClockOffsetMs), Cut = false });
                Interlocked.Increment(ref _w207CamHolds);
            }
            if (pending.Count == 0) continue;
            last = pending[^1];
            lastSentMs = Environment.TickCount64;
            byte[] to;
            lock (_w3cLock) to = [.. talk.Listeners];
            if (to.Length == 0) { pending.Clear(); continue; }
            // the DLL queued at most 30 a second (a cut at once); a slow poll (a low frame rate) brings more: every one goes, three a message
            for (int i = 0; i < pending.Count; i += Wo203CamRules.MaxSamplesPerMessage)
            {
                string text = Wo203CamRules.CamText(talk.Cid, pending.Skip(i).Take(Wo203CamRules.MaxSamplesPerMessage));
                Interlocked.Increment(ref _w3cCamOut);
                foreach (byte p in to) await Wo203SendConvAsync(p, Protocol.ConvCam, text);
            }
            Interlocked.Add(ref _w3cSamplesOut, pending.Count);
            pending.Clear();
        }
        Console.WriteLine($"WO203B-CAMPOLL pump of conversation {talk.Cid} stopped");
    }

    // ================================================================ frames (either role)

    private void Wo203CamOnFrame(byte src, LootMsg m)
    {
        switch (m.Kind)
        {
            case Protocol.ConvAnnounce:
                if (!Wo203CamRules.TryParseAnnounce(m.Text, out int acid, out _, out bool joinable, out string anchor)) { Interlocked.Increment(ref _w3cBad); return; }
                lock (_w3cLock) { if (joinable) _w3cOffers[src] = (acid, anchor); else _w3cOffers.Remove(src); }
                Console.WriteLine($"WO203B-OFFER partner {src}: conversation {acid} with '{anchor}' {(joinable ? "joinable" : "not joinable")}");
                _ = ExecLuaAsync($"if KCD2MP_W203Offer then KCD2MP_W203Offer({src}, {acid}, {(joinable ? 1 : 0)}, \"{EscapeLua(anchor)}\") end");
                break;
            case Protocol.ConvJoin:
            {
                if (!Wo203CamRules.TryParseCid(m.Text, out int jcid, out _)) { Interlocked.Increment(ref _w3cBad); return; }
                W203Talk? talk;
                lock (_w3cLock) { talk = _w3cTalk; if (talk is null || talk.Cid != jcid) talk = null; else talk.Listeners.Add(src); }
                if (talk is null)
                {
                    Console.WriteLine($"WO203B-JOIN partner {src} asked for conversation {jcid}: not running here -- End");
                    _ = Wo203SendConvAsync(W3cPeerOf(src), Protocol.ConvEnd, Wo203CamRules.EndText(jcid, "ended"));
                    return;
                }
                Interlocked.Increment(ref _w3cJoinsIn);
                Interlocked.Increment(ref _w3cSnapshots);
                Console.WriteLine($"WO203B-JOIN partner {src} listens to conversation {jcid} -- snapshot sent{(talk.CamUp ? ", sampler arming" : "; the camera starts with the dialogue's")}");
                _ = Wo203SendConvAsync(W3cPeerOf(src), Protocol.ConvSnapshot, Wo203CamRules.SnapshotText(talk.Cid, talk.Kind, talk.Anchor, talk.Others));
                W3cArmIfListened(talk);
                Wo204OnConvJoined(talk, src);   // WO-204 III.4: the lines so far and the open options
                break;
            }
            case Protocol.ConvLeave:
            {
                if (!Wo203CamRules.TryParseReason(m.Text, out int lcid, out string why)) { Interlocked.Increment(ref _w3cBad); return; }
                W203Talk? talk;
                bool none = false;
                lock (_w3cLock) { talk = _w3cTalk; if (talk is not null && talk.Cid == lcid) { talk.Listeners.Remove(src); none = talk.Listeners.Count == 0; } else talk = null; }
                Console.WriteLine($"WO203B-JOIN partner {src} left conversation {lcid} ({why})");
                if (talk is not null && none) Wo204ConvOnListenersGone(lcid);
                if (talk is not null && none && talk.Armed)
                {
                    talk.Armed = false;
                    talk.Pump?.Cancel();
                    _ = W3cNativeAsync(Protocol.Wo203OpConvCamPoll, [0]);
                }
                break;
            }
            case Protocol.ConvSnapshot:
                if (!Wo203CamRules.TryParseSnapshot(m.Text, out int scid, out string sanchor)) { Interlocked.Increment(ref _w3cBad); return; }
                _ = W3cEnterAsync(src, scid, sanchor);
                break;
            case Protocol.ConvCam:
                if (!Wo203CamRules.TryParseCam(m.Text, out int ccid, out var samples)) { Interlocked.Increment(ref _w3cBad); return; }
                W3cOnCam(src, ccid, samples);
                break;
            case Protocol.ConvEnd:
                if (!Wo203CamRules.TryParseReason(m.Text, out int ecid, out string reason)) { Interlocked.Increment(ref _w3cBad); return; }
                Interlocked.Increment(ref _w3cEndsIn);
                lock (_w3cLock) if (_w3cOffers.TryGetValue(src, out var o) && o.Cid == ecid) _w3cOffers.Remove(src);
                _ = ExecLuaAsync($"if KCD2MP_W203Gone then KCD2MP_W203Gone({src}, {ecid}) end");
                _ = W3cOutAsync(src, ecid, reason == "ended" ? "end" : reason, tellStarter: false, luaReason: reason);
                break;
            default:
                Wo204OnConvFrame(src, m);   // WO-204 III.3 / III.4: lines, options, votes, the chosen row
                break;
        }
    }

    // ================================================================ the listener

    private void Wo203CamOnEvent(string name, string? arg)
    {
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (name)
        {
            case "w203_cfg" when f.Length == 2:
                bool on = f[1] == "1";
                if (f[0] == "join") _w3cJoinOn = on;
                else if (f[0] == "camera") _w3cCameraOn = on;
                Console.WriteLine($"WO203B-TOGGLE {f[0]} {(on ? "on" : "off")}");
                break;
            case "w203_join" when f.Length == 2 && byte.TryParse(f[0], out byte starter) && int.TryParse(f[1], out int cid):
            {
                bool ok;
                lock (_w3cLock)
                {
                    ok = _w3cJoinOn && _w3cOffers.TryGetValue(starter, out var o) && o.Cid == cid && _w3cListen is not { Listening: true };
                    if (ok) _w3cListen = new W203Listen { Starter = starter, Cid = cid, Anchor = _w3cOffers[starter].Anchor, Joining = true, AskedMs = Environment.TickCount64 };
                }
                Console.WriteLine($"WO203B-JOIN asking partner {starter} to join conversation {cid}{(ok ? "" : " -- refused here (no such offer / already listening / switched off)")}");
                if (ok) _ = Wo203SendConvAsync(W3cPeerOf(starter), Protocol.ConvJoin, Wo203CamRules.JoinText(cid, false));
                break;
            }
            case "w203_leave" when f.Length >= 2 && byte.TryParse(f[0], out byte st) && int.TryParse(f[1], out int lcid):
                _ = W3cOutAsync(st, lcid, f.Length > 2 ? f[2] : "cancel", tellStarter: true, luaReason: null);   // Lua restored its side already
                break;
        }
    }

    private async Task W3cEnterAsync(byte starter, int cid, string anchor)
    {
        W203Listen? l;
        lock (_w3cLock)
        {
            l = _w3cListen;
            if (l is null || l.Starter != starter || l.Cid != cid || !l.Joining) { l = null; }
            else { l.Joining = false; l.Listening = true; l.Anchor = anchor; }
        }
        if (l is null) { Console.WriteLine($"WO203B-JOIN snapshot of {starter}:{cid} not asked for -- ignored"); return; }
        if (_w3cCameraOn && !await W3cViewOnAsync(l))
        {
            await W3cOutAsync(starter, cid, "fault", tellStarter: true, luaReason: null);
            _ = ExecLuaAsync("if KCD2MP_ShowNativeToast then KCD2MP_ShowNativeToast(\"Your partner's conversation could not be shown.\") end");
            return;
        }
        await ExecLuaAsync($"if KCD2MP_W203Enter then KCD2MP_W203Enter({starter}, {cid}, \"{EscapeLua(anchor)}\") end");
        var cts = new CancellationTokenSource();
        l.Watch = cts;
        _ = W3cWatchAsync(l, cts.Token);
    }

    private async Task<bool> W3cViewOnAsync(W203Listen l)
    {
        byte[]? cmd = Wo203CamRules.NameCmd(7, l.Anchor);
        var r = cmd is null ? null : await W3cNativeAsync(Protocol.Wo203OpConvView, cmd);
        bool on = r is { Ok: true } x && x.Payload.Length > 0 && x.Payload[0] == 0;
        if (on) { l.ViewOn = true; Interlocked.Increment(ref _w3cViewOn); }
        else Interlocked.Increment(ref _w3cViewRefused);
        Console.WriteLine($"WO203B-VIEW {(on ? "on" : "REFUSED")} for {l.Starter}:{l.Cid} anchor '{l.Anchor}' (result {(r is { } y && y.Payload.Length > 0 ? y.Payload[0] : -1)})");
        return on;
    }

    // The samples on their way to the DLL: one pipe call in flight at a time, carrying everything that came meanwhile. The DLL serves
    // its pipe on the game's main thread, about one call a frame: one call per message would queue without end below 30 fps (live:
    // 27 fps behind an unfocused window, every sample played late) -- batched, the rate follows the frame rate.
    private readonly List<(long, Wo203CamRules.CamSample)> _w3cToDll = [];
    private bool _w3cDllBusy;
    private long _w3cDllCalls;

    private void W3cOnCam(byte src, int cid, List<Wo203CamRules.CamSample> samples)
    {
        W203Listen? l = _w3cListen;
        Interlocked.Increment(ref _w3cCamIn);
        if (l is not { Listening: true, ViewOn: true } || l.Starter != src || l.Cid != cid) return;
        long qpc = QpcMs(), unix = UnixMs();
        Interlocked.Add(ref _w3cSamplesIn, samples.Count);
        lock (_w3cToDll)
        {
            foreach (var s in samples) _w3cToDll.Add((Wo203CamRules.LocalQpcMs(s.T, qpc, unix, ClockOffsetMs), s));
            if (_w3cDllBusy) return;
            _w3cDllBusy = true;
        }
        _ = Task.Run(async () =>
        {
            while (true)
            {
                List<(long, Wo203CamRules.CamSample)> batch;
                lock (_w3cToDll)
                {
                    if (_w3cToDll.Count == 0) { _w3cDllBusy = false; return; }
                    batch = _w3cToDll.Count > Wo203CamRules.MaxSamplesPerCall ? _w3cToDll.GetRange(_w3cToDll.Count - Wo203CamRules.MaxSamplesPerCall, Wo203CamRules.MaxSamplesPerCall) : [.. _w3cToDll];
                    _w3cToDll.Clear();
                }
                Interlocked.Increment(ref _w3cDllCalls);
                await W3cNativeAsync(Protocol.Wo203OpConvView, Wo203CamRules.EncodeSamples(batch));
            }
        });
    }

    /// <summary>Every 250 ms while listening: the DLL ended the view by itself (3 s of silence, a fault, the view lost)?</summary>
    private async Task W3cWatchAsync(W203Listen l, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(250, ct); } catch (OperationCanceledException) { return; }
            if (!l.ViewOn || l.SceneHold) continue;
            var r = await W3cNativeAsync(Protocol.Wo203OpConvView, [8]);
            if (r is not { Ok: true } x || x.Payload.Length < 2 || x.Payload[0] != 0) continue;   // still on (or yielded)
            string why = Wo203CamRules.WhyName(x.Payload[1]);
            l.ViewOn = false;
            Console.WriteLine($"WO203B-VIEW the DLL ended the view of {l.Starter}:{l.Cid} by itself ({why})");
            await W3cOutAsync(l.Starter, l.Cid, why == "silence" ? "silence" : why, tellStarter: true, luaReason: why);
            return;
        }
    }

    /// <summary>Every way out of listening: the view off first, then the mod's state (unless it went first), then the starter told.</summary>
    private async Task W3cOutAsync(byte starter, int cid, string why, bool tellStarter, string? luaReason)
    {
        W203Listen? l;
        lock (_w3cLock)
        {
            l = _w3cListen;
            if (l is null || l.Starter != starter || l.Cid != cid) return;
            _w3cListen = null;
        }
        l.Watch?.Cancel();
        if (l.ViewOn || l.SceneHold) await W3cNativeAsync(Protocol.Wo203OpConvView, [3, Wo203CamRules.WhyCode(why)]);
        l.ViewOn = false;
        if (luaReason is not null) await ExecLuaAsync($"if KCD2MP_W203End then KCD2MP_W203End({starter}, {cid}, \"{EscapeLua(luaReason)}\") end");
        Console.WriteLine($"WO203B-LISTEN {starter}:{cid} over ({why}){(tellStarter ? " -- Leave sent" : "")}");
        if (tellStarter && l.Listening)
        {
            Interlocked.Increment(ref _w3cLeavesOut);
            await Wo203SendConvAsync(W3cPeerOf(starter), Protocol.ConvLeave, Wo203CamRules.LeaveText(cid, why));
        }
    }

    // ---- the agent's own ways out (hooked from GameBridge.Wo123 / Wo138 / Wo144 / Wo194) ----

    /// <summary>The ESC menu opened: the listening ends (the player's own camera under the menu).</summary>
    private void Wo203CamOnMenu(bool open)
    {
        if (!open || _w3cListen is not { Listening: true } l) return;
        _ = Task.Run(() => W3cOutAsync(l.Starter, l.Cid, "menu", tellStarter: true, luaReason: "menu"));
    }

    /// <summary>A load starts here: listening ends (the view first); this player's own conversation ends with the load.</summary>
    private void Wo203CamOnLoad()
    {
        if (_w3cListen is { } l) _ = Task.Run(() => W3cOutAsync(l.Starter, l.Cid, "load", tellStarter: true, luaReason: "load"));
        W203Talk? talk;
        lock (_w3cLock) { talk = _w3cTalk; _w3cTalk = null; }
        if (talk is not null) W3cEndTalk(talk, "lost");
    }

    /// <summary>A shared cutscene (WO-194) starts / ends here: the cutscene owns the camera; the view comes back after it if the
    /// conversation still runs.</summary>
    private void Wo203CamOnScene(bool playing)
    {
        if (_w3cListen is not { Listening: true } l) return;
        _ = Task.Run(async () =>
        {
            if (playing && l.ViewOn && !l.SceneHold)
            {
                l.SceneHold = true;
                l.ViewOn = false;
                await W3cNativeAsync(Protocol.Wo203OpConvView, [3, Wo203CamRules.WhyCode("scene")]);
                Console.WriteLine($"WO203B-VIEW yields to a shared cutscene ({l.Starter}:{l.Cid})");
            }
            else if (!playing && l.SceneHold && ReferenceEquals(_w3cListen, l))
            {
                l.SceneHold = false;
                Console.WriteLine($"WO203B-VIEW the cutscene ended -- the conversation's view again ({l.Starter}:{l.Cid})");
                if (_w3cCameraOn) await W3cViewOnAsync(l);
            }
        });
    }

    private void Wo203CamOnPeerGone(byte id)
    {
        lock (_w3cLock) _w3cOffers.Remove(id);
        if (_w3cListen is { } l && l.Starter == id) _ = Task.Run(() => W3cOutAsync(l.Starter, l.Cid, "disconnect", tellStarter: false, luaReason: "disconnect"));
        W203Talk? talk;
        bool none = false;
        lock (_w3cLock) { talk = _w3cTalk; if (talk is not null) { talk.Listeners.Remove(id); talk.Told.Remove(id); none = talk.Listeners.Count == 0; } }
        if (talk is not null && none) Wo204ConvOnListenersGone(talk.Cid);
        if (talk is not null && none && talk.Armed) { talk.Armed = false; talk.Pump?.Cancel(); _ = W3cNativeAsync(Protocol.Wo203OpConvCamPoll, [0]); }
    }

    private void Wo203CamOnDisconnect()
    {
        if (_w3cListen is { } l) _ = Task.Run(() => W3cOutAsync(l.Starter, l.Cid, "disconnect", tellStarter: false, luaReason: "disconnect"));
        W203Talk? talk;
        lock (_w3cLock) { talk = _w3cTalk; _w3cTalk = null; _w3cOffers.Clear(); }
        if (talk is not null) { talk.Pump?.Cancel(); if (talk.Armed) _ = W3cNativeAsync(Protocol.Wo203OpConvCamPoll, [0]); }
        if (talk is not null) Wo204ConvOnListenersGone(talk.Cid);
    }

    /// <summary>A load finished: the mod's listening state is put back if anything of it survived the load (belt and braces).</summary>
    private void Wo203CamOnGameplayStarted() => _ = ExecLuaAsync("if KCD2MP_W203AfterLoad then KCD2MP_W203AfterLoad() end");

    private void Wo203CamOnConnect()
    {
        _ = ExecLuaAsync("if KCD2MP_W203CamEmit then KCD2MP_W203CamEmit() end");
        // the prompt's text: a localization key of the mod's own (WO-171's way -- a loose file in the install's Mods folder, loaded by the
        // mod once per game run); the mod shows the game's own "Talk" until it is loaded
        try
        {
            if (KcdLogLocator.Find() is not string log || Path.GetDirectoryName(log) is not string root) return;
            string dir = Path.Combine(root, "Mods", "kdcmp", "Localization"), file = Path.Combine(dir, Wo203CamRules.LocFileName);
            Directory.CreateDirectory(dir);
            if (!File.Exists(file) || File.ReadAllText(file) != Wo203CamRules.LocXml)
                File.WriteAllText(file, Wo203CamRules.LocXml, new System.Text.UTF8Encoding(false));
            _ = ExecLuaAsync($"if KCD2MP_W203LoadLoc then KCD2MP_W203LoadLoc(\"Mods/kdcmp/Localization/{Wo203CamRules.LocFileName}\") end");
        }
        catch (Exception ex) { Console.WriteLine($"WO203B-PROMPT the prompt's text file was not written ({ex.GetType().Name}): the prompt reads 'Talk'"); }
    }

    private async Task<(bool Ok, byte Reason, byte[] Payload)?> W3cNativeAsync(byte op, byte[] args)
    {
        try { return await _combat.Wo163Async(op, args); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or ObjectDisposedException)
        {
            Interlocked.Increment(ref _w3cNativeOut);
            return null;
        }
    }

    private void Wo203CamStats(ref string line) =>
        line += $" | cam announced={Interlocked.Read(ref _w3cAnnounced)} joins_in={Interlocked.Read(ref _w3cJoinsIn)} snapshots={Interlocked.Read(ref _w3cSnapshots)}"
              + $" cam_out={Interlocked.Read(ref _w3cCamOut)}/{Interlocked.Read(ref _w3cSamplesOut)} cam_in={Interlocked.Read(ref _w3cCamIn)}/{Interlocked.Read(ref _w3cSamplesIn)} cam_holds={Interlocked.Read(ref _w207CamHolds)}"
              + $" dll_calls={Interlocked.Read(ref _w3cDllCalls)} view_on={Interlocked.Read(ref _w3cViewOn)} view_refused={Interlocked.Read(ref _w3cViewRefused)} leaves_out={Interlocked.Read(ref _w3cLeavesOut)}"
              + $" ends_out={Interlocked.Read(ref _w3cEndsOut)} ends_in={Interlocked.Read(ref _w3cEndsIn)} native_failed={Interlocked.Read(ref _w3cNativeOut)} bad={Interlocked.Read(ref _w3cBad)}"
              + $" listening={(_w3cListen is { } l ? $"{l.Starter}:{l.Cid}{(l.ViewOn ? "+view" : "")}" : "-")} talk={(_w3cTalk is { } t ? $"{t.Cid}/{t.Listeners.Count}{(t.Armed ? "+armed" : "")}" : "-")}";
}
