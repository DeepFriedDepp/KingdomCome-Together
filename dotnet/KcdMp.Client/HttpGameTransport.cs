// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KcdMp.Client;

/// <summary>
/// The game's debug REST API on localhost:1403, one HTTP round trip per call.
///
/// Two measured facts shape this class:
///
/// **Cost is per round trip, flat.** ~13-42 ms depending on game load, and
/// completely independent of payload -- 20 Lua statements in one call cost the
/// same as one. So statements are batched and flushed together, turning N ghost
/// updates per tick from N round trips into one.
///
/// **A batch aborts at the first error.** With twelve statements and a
/// deliberate fault at the sixth, an unwrapped batch ran only the first five;
/// the same batch with each statement wrapped in its own pcall ran all eleven
/// good ones. So every batched statement is wrapped individually. This also
/// matches the project rule that Lua touching game state goes in a pcall.
///
/// Reading player state is split the way the agent has always done it: position
/// is one round trip per tick, while yaw and mount state come from a slower
/// background loop through the sv_servername CVar. That CVar round trip is the
/// hack WO-1 removes -- see <see cref="LogTailGameTransport"/> -- but it stays
/// here so this remains an honest baseline and a working fallback.
/// </summary>
public sealed partial class HttpGameTransport(string gameApiBase, int timeoutMs = 800, HttpMessageHandler? handler = null) : IGameTransport
{
    /// <summary>How often the background loop refreshes yaw and mount state.</summary>
    private const int RotStateIntervalMs = 80;

    /// <summary>
    /// Flush before the batch gets long. Payload does not affect latency and an
    /// 8000-character chunk was verified to execute, so this is comfortably
    /// conservative rather than a measured ceiling.
    /// </summary>
    // WO-110: the batch is bounded by ENCODED size against the console's
    // measured ceiling (LuaCommandBudget), not by a raw character count. The
    // previous 4,000-raw bound let a full batch be truncated by the engine
    // with a Lua error nobody on this side could see.
    private const int MaxBatchChars = LuaCommandBudget.MaxEncodedCommandChars;

    private readonly HttpClient _http = handler is null
        ? new() { Timeout = TimeSpan.FromMilliseconds(timeoutMs) }
        : new(handler, disposeHandler: false) { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };

    // WO-153 3: the ordered queue behind the batches (see BatchQueue) and the one send in flight at a time.
    private readonly BatchQueue _queue = new();
    private readonly SemaphoreSlim _batchLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private CancellationTokenSource? _rotCts;
    private Task? _rotTask;
    private volatile float _cachedRotZ;
    private volatile bool _cachedIsRiding;

    /// <summary>
    /// When true, <see cref="ExecuteAsync"/> buffers until <see cref="FlushAsync"/>.
    /// </summary>
    public bool BatchingEnabled { get; set; } = true;

    public string Name => "http-debug-api";

    /// <summary>
    /// One: position. Yaw and mount state come from the background loop, so they
    /// are not charged per read. <see cref="ReadPlayerStateUncachedAsync"/> is
    /// the three-round-trip cost of doing it without that loop.
    /// </summary>
    public int RoundTripsPerStateRead => 1;

    /// <summary>
    /// Remembers the agent's token. The background yaw/mount-state refresh starts with the first
    /// <see cref="ReadPlayerStateAsync"/>: in log-tail mode (the default) that is never called, and the loop's two
    /// REST round trips about every 100 ms (a `System.SetCVar` ExecuteString plus a GET, 12-16 requests a second for the
    /// agent's whole life, WO-153 3) fed nothing.
    /// </summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        _rotToken = ct;
        return Task.CompletedTask;
    }

    private CancellationToken _rotToken;
    private readonly object _rotStart = new();

    private void EnsureRotLoop()
    {
        if (_rotTask is not null) return;
        lock (_rotStart)
        {
            if (_rotTask is not null) return;
            _rotCts = CancellationTokenSource.CreateLinkedTokenSource(_rotToken);
            _rotTask = Task.Run(() => RotStateLoopAsync(_rotCts.Token), CancellationToken.None);
        }
    }

    private async Task RotStateLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var rot = await ReadRotStateAsync(ct);
            if (rot is not null)
            {
                _cachedRotZ = rot.Value.rotZ;
                _cachedIsRiding = rot.Value.isRiding;
            }
            try { await Task.Delay(RotStateIntervalMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task<bool> IsGameReadyAsync(CancellationToken ct = default)
    {
        try
        {
            var xml = await _http.GetStringAsync($"{gameApiBase}/api/rpg/Calendar?depth=1", ct);
            var m = GameTimeRegex().Match(xml);
            return m.Success
                && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float t)
                && t > 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// WO-124: (the debug API answers, the Calendar's GameTime). GameTime is 0
    /// at the main menu (docs/kcd2_lua_api.md) -- a joiner connects there.
    /// </summary>
    public async Task<(bool Up, float GameTime)> ReadGameTimeAsync(CancellationToken ct = default)
    {
        try
        {
            var xml = await _http.GetStringAsync($"{gameApiBase}/api/rpg/Calendar?depth=1", ct);
            var m = GameTimeRegex().Match(xml);
            return (true, m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float t) ? t : 0f);
        }
        catch { return (false, 0f); }
    }

    /// <summary>
    /// WO-124: is a world loaded, asked of the game itself: the player entity
    /// "Dude" exists. GameTime alone cannot tell: after a failed load the
    /// engine is back at the main menu with the Calendar still counting
    /// (observed). Rides on sv_servername like ReadRotStateAsync; a value
    /// that is not ours (the yaw loop wrote in between) is asked again.
    /// Null when the answer cannot be read.
    /// </summary>
    public async Task<bool?> ReadWorldLoadedAsync(CancellationToken ct = default)
    {
        for (int i = 0; i < 4; i++)
        {
            try
            {
                await SendNowAsync(@"System.SetCVar(""sv_servername"",System.GetEntityByName(""Dude"") and ""kcdmp-where=world"" or ""kcdmp-where=menu"")", ct);
                var xml = await _http.GetStringAsync($"{gameApiBase}/api/System/Console/GetCvarValue?name=sv_servername", ct);
                var m = CvarValueRegex().Match(xml);
                if (m.Success && m.Groups[1].Value.StartsWith("kcdmp-where=", StringComparison.Ordinal))
                    return m.Groups[1].Value == "kcdmp-where=world";
            }
            catch { }
            await Task.Delay(100, ct);
        }
        return null;
    }

    /// <summary>Position this tick, plus the most recent cached yaw/mount state.</summary>
    public async Task<PlayerState?> ReadPlayerStateAsync(CancellationToken ct = default)
    {
        EnsureRotLoop();
        var pos = await ReadPositionOnlyAsync(ct);
        if (pos is null) return null;
        var (x, y, z) = pos.Value;
        return new PlayerState(x, y, z, _cachedRotZ, _cachedIsRiding);
    }

    /// <summary>
    /// Full state without the cache: three round trips. Only used to measure
    /// what the CVar hack actually costs.
    /// </summary>
    public async Task<PlayerState?> ReadPlayerStateUncachedAsync(CancellationToken ct = default)
    {
        var pos = await ReadPositionOnlyAsync(ct);
        if (pos is null) return null;
        var rot = await ReadRotStateAsync(ct);
        var (x, y, z) = pos.Value;
        return new PlayerState(x, y, z, rot?.rotZ ?? 0f, rot?.isRiding ?? false);
    }

    /// <summary>One round trip: scrape Position from the player soul XML.</summary>
    public async Task<(float x, float y, float z)?> ReadPositionOnlyAsync(CancellationToken ct = default)
    {
        try
        {
            var xml = await _http.GetStringAsync($"{gameApiBase}/api/rpg/SoulList/PlayerSoul?depth=1", ct);
            var m = PosRegex().Match(xml);
            if (!m.Success) return null;

            var parts = m.Groups[1].Value.Split(',');
            if (parts.Length < 3) return null;

            return (float.Parse(parts[0], CultureInfo.InvariantCulture),
                    float.Parse(parts[1], CultureInfo.InvariantCulture),
                    float.Parse(parts[2], CultureInfo.InvariantCulture));
        }
        catch { return null; }
    }

    /// <summary>
    /// Two round trips: have Lua pack yaw and mount state into sv_servername,
    /// then read it back. Sent immediately -- buffering the write would leave
    /// the read fetching a stale value.
    ///
    /// The riding flag is computed in the interp tick rather than here, because
    /// Terrain is not available in the console context; this only collects what
    /// the tick already cached in KCD2MP.isRiding.
    /// </summary>
    public async Task<(float rotZ, bool isRiding)?> ReadRotStateAsync(CancellationToken ct = default)
    {
        try
        {
            await SendNowAsync(
                @"System.SetCVar(""sv_servername"",(function()" +
                // WO-124: no player at the main menu (the agent connects there now)
                @"local r=player and player:GetWorldAngles().z or 0;" +
                @"local ride=KCD2MP and KCD2MP.isRiding and 'r' or 's';" +
                @"return string.format('%.4f,%s',r,ride)end)())", ct);

            var xml = await _http.GetStringAsync(
                $"{gameApiBase}/api/System/Console/GetCvarValue?name=sv_servername", ct);

            var m = CvarValueRegex().Match(xml);
            if (!m.Success) return null;

            var parts = m.Groups[1].Value.Split(',');
            float rot = 0f;
            if (parts.Length >= 1)
                float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out rot);

            return (rot, parts.Length >= 2 && parts[1].Trim() == "r");
        }
        catch { return null; }
    }

    /// <summary>
    /// WO-153 3: true (the default) = a batch the game does not answer within the timeout is kept and sent again, in
    /// order, once the game answers (see <see cref="BatchQueue"/>). False = the 0.43.0 behaviour: it is dropped.
    /// </summary>
    public bool RetryBatches { get; set; } = true;

    /// <summary>The clock for the queue's time to live and the recovery's pace (tests replace it).</summary>
    public Func<long> NowMs { get; set; } = () => Environment.TickCount64;
    /// <summary>How often a held transport tries the game again.</summary>
    public int RecoveryIntervalMs { get; set; } = 1000;
    /// <summary>Consecutive unanswered batches before the transport stops sending and waits (the hold).</summary>
    public int FailuresBeforeHold { get; set; } = 2;

    private static readonly string OnceEpoch = Random.Shared.Next(0x1000, 0xFFFF).ToString("x");   // one agent run; ids never collide with an earlier run's
    private long _onceSeq;
    private int _consecutiveFailures;
    private volatile bool _held;
    private long _heldSinceMs;
    private Task? _recovery;
    private CancellationTokenSource? _recoveryCts;

    /// <summary>True while the game does not answer and sends wait in the queue (WO-153 3).</summary>
    public bool Held => _held;
    /// <summary>Statements waiting in the queue (tests, status).</summary>
    public int Queued { get { lock (_queueLock) return _queue.Count; } }
    private readonly object _queueLock = new();

    /// <summary>WO-153 3: batches that got no answer and were kept.</summary>
    public long BatchesRetried { get; private set; }
    /// <summary>WO-153 3: how many times the transport stopped sending and waited for the game.</summary>
    public long Holds { get; private set; }
    public long Superseded { get { lock (_queueLock) return _queue.Superseded; } }
    public long Expired { get { lock (_queueLock) return _queue.Expired; } }
    public long Overflowed { get { lock (_queueLock) return _queue.Overflowed; } }
    public long StaleDropped { get { lock (_queueLock) return _queue.StaleDropped; } }

    private static string Wrap(string lua) => "pcall(function() " + lua + " end)\n";
    private static string WrapOnce(string lua, string id) =>
        "pcall(function() local f=function() " + lua + " end if KCD2MP_Once then KCD2MP_Once(\"" + id + "\",f) else f() end end)\n";

    public async Task ExecuteAsync(string lua, CancellationToken ct = default)
    {
        if (!BatchingEnabled)
        {
            await SendNowAsync(lua, ct);
            return;
        }

        // Each statement gets its own pcall so one failure cannot swallow the rest of the batch (measured: unwrapped, a
        // fault at statement 6 of 12 lost everything after it). WO-153 3: a statement that must run once -- an Edge,
        // anything that is not a re-sent state -- also carries its once-guard, so a batch the game ran late AND we sent
        // again cannot run it twice.
        var (kind, key) = RetryBatches ? BatchPolicy.Classify(lua) : (BatchKind.Level, (string?)null);
        string? onceId = kind == BatchKind.Edge ? OnceEpoch + "-" + Interlocked.Increment(ref _onceSeq) : null;
        string text = onceId is null ? Wrap(lua) : WrapOnce(lua, onceId);
        int enc = LuaCommandBudget.EncodedLength(text);
        if (enc > MaxBatchChars && onceId is not null)
        {
            // too big with its guard but whole without it: sent plain (a retry of this one could run twice)
            onceId = null; text = Wrap(lua); enc = LuaCommandBudget.EncodedLength(text);
        }
        if (enc > MaxBatchChars)
        {
            // Cannot ever be sent whole: the engine would truncate it and fail
            // every statement around it. Said out loud, once per 5 s, counted.
            OversizeDropped++;
            var now = DateTime.UtcNow;
            if ((now - _lastOversizeLogUtc) >= TimeSpan.FromSeconds(5))
            {
                _lastOversizeLogUtc = now;
                Console.WriteLine($"MP-BATCH-DROP reason=oversize encoded={enc} budget={MaxBatchChars} total={OversizeDropped} first=\"{(lua.Length > 100 ? lua[..100] + "..." : lua)}\"");
            }
            return;
        }

        // The decision and the add happen under ONE lock acquisition. Callers fire-and-forget from several loops at once
        // (the native-scan push queues its chunks in a burst).
        bool full;
        await _batchLock.WaitAsync(ct);
        try
        {
            lock (_queueLock)
            {
                _queue.Add(new BatchQueue.Entry { Lua = lua, Text = text, Kind = kind, Key = key, OnceId = onceId, Encoded = enc, EnqueuedMs = NowMs() });
                full = _queue.QueuedEncoded >= MaxBatchChars - 200;   // full enough: do not wait for the loop
            }
        }
        finally { _batchLock.Release(); }

        if (full) await FlushAsync(ct);
    }

    /// <summary>WO-110: statements that could never fit one ExecuteString and were dropped (see LuaCommandBudget).</summary>
    public long OversizeDropped { get; private set; }
    private DateTime _lastOversizeLogUtc = DateTime.MinValue;

    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (_held) return;   // the recovery task owns sending until the game answers again
        await _sendLock.WaitAsync(ct);
        try
        {
            // at most 8 batches a call: a burst drains over a few ticks, never one unbounded stall
            for (int i = 0; i < 8 && !_held; i++)
            {
                List<BatchQueue.Entry> batch;
                lock (_queueLock) batch = _queue.TakeBatch(MaxBatchChars, NowMs());
                if (batch.Count == 0) break;
                if (!await SendEntriesAsync(batch, ct)) break;
            }
        }
        finally { _sendLock.Release(); }
    }

    /// <summary>One command with these statements. True when the game answered; false when it did not (the statements are kept, or dropped under RetryBatches=false).</summary>
    private async Task<bool> SendEntriesAsync(List<BatchQueue.Entry> batch, CancellationToken ct)
    {
        var sb = new StringBuilder();
        foreach (var e in batch) sb.Append(e.Text);
        try
        {
            await SendNowAsync(sb.ToString(), ct);
            _consecutiveFailures = 0;
            return true;
        }
        catch (Exception ex)
        {
            bool transient = RetryBatches && !ct.IsCancellationRequested && IsNoAnswer(ex);
            if (!transient)
            {
                // WO-110 Phase 6: this is the 0.43.0 path, still the one for a hard error or RetryBatches=false: a failed
                // flush drops EVERY statement in the batch (one Lua syntax error fails the whole ExecuteString before any
                // per-statement pcall runs). Logged with the count and the head, throttled to one line per 5 s.
                BatchesDropped++;
                StatementsDropped += batch.Count;
                LogDrop(batch, ex);
                return false;
            }
            lock (_queueLock) _queue.Requeue(batch);
            BatchesRetried++;
            int n = ++_consecutiveFailures;
            LogRetry(batch, ex);
            if (n >= FailuresBeforeHold) EnterHold(n);
            return false;
        }
    }

    /// <summary>The game did not answer: a timeout, or no connection (it is loading, hung or gone).</summary>
    private static bool IsNoAnswer(Exception ex) =>
        ex is TaskCanceledException or TimeoutException or OperationCanceledException or HttpRequestException;

    private void EnterHold(int failures)
    {
        if (_held) return;
        _held = true;
        _heldSinceMs = NowMs();
        Holds++;
        int queued; lock (_queueLock) queued = _queue.Count;
        Console.WriteLine($"MP-BATCH-HOLD down: the game did not answer {failures} batches in a row -- {queued} statement(s) wait in the queue (bounded, a stale state is replaced by a newer one); trying again every {RecoveryIntervalMs} ms");
        _recoveryCts = new CancellationTokenSource();
        var tok = _recoveryCts.Token;
        _recovery = Task.Run(() => RecoverAsync(tok), CancellationToken.None);
    }

    private async Task RecoverAsync(CancellationToken ct)
    {
        while (_held && !ct.IsCancellationRequested)
        {
            try { await Task.Delay(RecoveryIntervalMs, ct); } catch { return; }
            try
            {
                await _sendLock.WaitAsync(ct);
                try
                {
                    List<BatchQueue.Entry> batch;
                    lock (_queueLock) batch = _queue.TakeBatch(MaxBatchChars, NowMs());
                    bool answered;
                    if (batch.Count == 0)
                    {
                        // nothing waits: a harmless statement tells whether the game answers again
                        try { await SendNowAsync(Wrap("local _ = 1").TrimEnd('\n'), ct); answered = true; }
                        catch (Exception ex) when (IsNoAnswer(ex)) { answered = false; }
                    }
                    else
                    {
                        // a failure here is expected while the game is away: the statements go back, nothing is counted twice
                        var sb = new StringBuilder();
                        foreach (var e in batch) sb.Append(e.Text);
                        try { await SendNowAsync(sb.ToString(), ct); answered = true; }
                        catch (Exception ex) when (IsNoAnswer(ex)) { answered = false; lock (_queueLock) _queue.Requeue(batch); }
                    }
                    if (answered)
                    {
                        long downMs = NowMs() - _heldSinceMs;
                        _consecutiveFailures = 0;
                        _held = false;
                        int queued; lock (_queueLock) queued = _queue.Count;
                        Console.WriteLine($"MP-BATCH-HOLD up after {downMs / 1000.0:F1} s: {queued} statement(s) still queued, {Superseded} replaced by a newer state, {Expired} expired, {Overflowed} over the bound, {StaleDropped} stale (retried batches {BatchesRetried})");
                        return;
                    }
                }
                finally { _sendLock.Release(); }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Console.WriteLine($"MP-BATCH-HOLD recovery attempt failed: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    private void LogRetry(List<BatchQueue.Entry> batch, Exception ex)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastRetryLogUtc) < TimeSpan.FromSeconds(5)) return;
        _lastRetryLogUtc = now;
        string head = batch[0].Lua.Length > 120 ? batch[0].Lua[..120] + "..." : batch[0].Lua;
        Console.WriteLine($"MP-BATCH-RETRY statements={batch.Count} total_batches={BatchesRetried} why={ex.GetType().Name}: {ex.Message} -- kept, sent again in order when the game answers; first=\"{head}\"");
    }

    private void LogDrop(List<BatchQueue.Entry> batch, Exception ex)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastDropLogUtc) < TimeSpan.FromSeconds(5)) return;
        _lastDropLogUtc = now;
        string head = batch[0].Lua.Length > 120 ? batch[0].Lua[..120] + "..." : batch[0].Lua;
        Console.WriteLine($"MP-BATCH-DROP statements={batch.Count} total_batches={BatchesDropped} total_statements={StatementsDropped} why={ex.GetType().Name}: {ex.Message} first=\"{head}\"");
    }

    /// <summary>WO-110 Phase 6: batches whose ExecuteString failed for good (a hard error, or RetryBatches off).</summary>
    public long BatchesDropped { get; private set; }
    /// <summary>WO-110 Phase 6: statements lost inside those batches.</summary>
    public long StatementsDropped { get; private set; }
    private DateTime _lastDropLogUtc = DateTime.MinValue;
    private DateTime _lastRetryLogUtc = DateTime.MinValue;

    /// <summary>Sends immediately, bypassing the batch buffer.</summary>
    private async Task SendNowAsync(string lua, CancellationToken ct = default)
    {
        var cmd = Uri.EscapeDataString($"#{lua}");
        await _http.GetStringAsync($"{gameApiBase}/api/System/Console/ExecuteString?command={cmd}", ct);
    }

    // -------------------------------------------------------------------------
    // WO-144 2.1: a live avatar's own soul
    // -------------------------------------------------------------------------

    /// <summary>
    /// WO-144 2.1: the soul a REST call about an avatar goes to. The game's SoulsByName holds ONE soul
    /// per name, the first registered: a world saved while a partner was connected carries that
    /// avatar's soul (kcd2mp_N) in its soul list, and after a load SoulsByName/kcd2mp_N is that saved
    /// soul -- not the live avatar (observed on a joiner: a second kcd2mp_0 at another place, in a
    /// villager's preset). 0.42.0 dressed and read that soul: the partner stood there naked while the
    /// agent's read-back said everything was worn. The bridge maps an avatar to its live soul's key
    /// (SoulsByGuid) once it has found it; unmapped names keep SoulsByName.
    /// </summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, Guid> LiveSouls { get; } = new(StringComparer.Ordinal);

    private string SoulPath(string soulName) =>
        LiveSouls.TryGetValue(soulName, out var g) ? $"SoulsByGuid/{g}" : $"SoulsByName/{Uri.EscapeDataString(soulName)}";

    /// <summary>Every soul key (SoulsByGuid, keys only: ~100 KB), or null when the read failed.</summary>
    public async Task<List<Guid>?> ReadSoulKeysAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            var xml = await new HttpClient { Timeout = TimeSpan.FromSeconds(10) }.GetStringAsync($"{gameApiBase}/api/rpg/SoulList/SoulsByGuid?depth=1", cts.Token);
            var keys = new List<Guid>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(xml, "Key=\"([0-9a-fA-F-]{36})\""))
                if (Guid.TryParse(m.Groups[1].Value, out var g)) keys.Add(g);
            return keys;
        }
        catch { return null; }
    }

    /// <summary>A soul's Name and Position by its key (null when the read failed).</summary>
    public async Task<(string Name, string Position)?> ReadSoulNameAtAsync(Guid key, CancellationToken ct = default)
    {
        try
        {
            var xml = await _http.GetStringAsync($"{gameApiBase}/api/rpg/SoulList/SoulsByGuid/{key}?depth=1&exclude=DerivedStatsByName,Buffs,Roles,StaticData,PersistentData,Archetype,Inventory,CombatSoul,CompanionManager,EquipmentManager,FactionNode,SoulClass,SocialClass,StormDebug", ct);
            var n = System.Text.RegularExpressions.Regex.Match(xml, "Name=\"([^\"]*)\"");
            var p = System.Text.RegularExpressions.Regex.Match(xml, "Position=\"([^\"]*)\"");
            return (n.Success ? n.Groups[1].Value : "", p.Success ? p.Groups[1].Value : "");
        }
        catch { return null; }
    }

    // -------------------------------------------------------------------------
    // Appearance (WO-9)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Two round trips: every ItemClass in the player's
    /// EquipmentManager.EquippedArmorsByClassId AND EquippedWeaponsByClassId
    /// maps, merged. This is the real per-slot equipment state -- proven in
    /// WO-9 Phase 0 (armor) and WO-10 (weapons, identical shape, confirmed
    /// live) to track a player who equipped by hand, unlike
    /// BaseClothingPreset, which reads all-zero the moment a player stops
    /// matching the preset they spawned with. One merged read rather than two
    /// separate wire messages because EquipItem/UnequipItem do not care which
    /// map a class came from -- see the diff/apply path in GameBridge.
    /// </summary>
    public async Task<Guid[]?> ReadEquippedItemClassesAsync(CancellationToken ct = default)
    {
        var armor = await ReadItemClassMapAsync(
            $"{gameApiBase}/api/rpg/SoulList/PlayerSoul/EquipmentManager/EquippedArmorsByClassId?depth=1", ct);
        var weapons = await ReadItemClassMapAsync(
            $"{gameApiBase}/api/rpg/SoulList/PlayerSoul/EquipmentManager/EquippedWeaponsByClassId?depth=1", ct);
        // WO-59: a failed half is not an empty half. Under load the game's
        // REST API times out one endpoint while the other answers (host at
        // 15 fps, WO-54 §5.1) -- merging a failed armor read with a good
        // weapon read used to produce a REAL-looking smaller set, which the
        // appearance loop then sent as a genuine outfit change and every
        // peer unequipped half the player's clothes. Null means "don't know",
        // and the caller skips the poll instead of acting on it.
        if (armor is null || weapons is null) return null;
        return [.. armor, .. weapons];
    }

    /// <summary>
    /// Reads a named ghost's own EquippedArmorsByClassId and
    /// EquippedWeaponsByClassId, merged. Not used by the normal apply path --
    /// GameBridge tracks what it last applied to each ghost itself, so it
    /// never needs to ask the game what is currently equipped -- but kept for
    /// verification retries, diagnostics and the manual test procedure.
    /// </summary>
    public async Task<Guid[]?> ReadGhostEquippedItemClassesAsync(string ghostSoulName, CancellationToken ct = default)
    {
        string soul = Uri.EscapeDataString(ghostSoulName);
        var armor = await ReadItemClassMapAsync(
            $"{gameApiBase}/api/rpg/SoulList/{SoulPath(ghostSoulName)}/EquipmentManager/EquippedArmorsByClassId?depth=1", ct);
        var weapons = await ReadItemClassMapAsync(
            $"{gameApiBase}/api/rpg/SoulList/{SoulPath(ghostSoulName)}/EquipmentManager/EquippedWeaponsByClassId?depth=1", ct);
        // WO-59: same null discipline as the player read above. The verify
        // path used to take a timed-out read for "nothing is equipped" and
        // mass-blacklist a whole batch of perfectly equippable items.
        if (armor is null || weapons is null) return null;
        return [.. armor, .. weapons];
    }

    private async Task<Guid[]?> ReadItemClassMapAsync(string url, CancellationToken ct)
    {
        try
        {
            var xml = await _http.GetStringAsync(url, ct);
            return ParseItemClasses(xml);
        }
        catch { return null; }
    }

    public async Task EquipItemOnGhostAsync(string ghostSoulName, Guid itemClass, bool createIfMissing, CancellationToken ct = default)
    {
        string soul = Uri.EscapeDataString(ghostSoulName);
        string cls = itemClass.ToString();

        if (createIfMissing)
        {
            // Fire-and-forget the descriptor: CreateItems returns an
            // ItemClassDescriptor with no readable properties at this depth,
            // and the only verification that matters is the equip that
            // follows actually taking effect.
            await _http.GetStringAsync(
                $"{gameApiBase}/api/rpg/SoulList/{SoulPath(ghostSoulName)}/Inventory/CreateItems" +
                $"?ItemClass={cls}&Amount=1&ShowUINotification=false", ct);
        }

        await _http.GetStringAsync(
            $"{gameApiBase}/api/rpg/SoulList/{SoulPath(ghostSoulName)}/EquipmentManager/EquipItem?itemClassId={cls}", ct);
    }

    public async Task UnequipItemOnGhostAsync(string ghostSoulName, Guid itemClass, CancellationToken ct = default)
    {
        string soul = Uri.EscapeDataString(ghostSoulName);
        await _http.GetStringAsync(
            $"{gameApiBase}/api/rpg/SoulList/{SoulPath(ghostSoulName)}/EquipmentManager/UnequipItem?itemClassId={itemClass}", ct);
    }

    /// <summary>
    /// Reads a ghost's own Soul.Guid (WO-17). This is NOT the same field as
    /// SharedSoulGuid used elsewhere for cross-client damage matching -- a
    /// locally-spawned ghost proxy carries SharedSoulGuid=0, so Guid is the
    /// identity that actually resolves through the DLL's SoulsByGuid lookup
    /// for it. Depth=1 with a heavy-subtree exclude list keeps this cheap,
    /// same idiom as Get-KcdSoulSnapshot in tools\KcdApi.ps1. Null if the
    /// ghost is not (yet) a real soul the game will answer for.
    /// </summary>
    public async Task<Guid?> ReadGhostSoulGuidAsync(string ghostSoulName, CancellationToken ct = default)
    {
        string soul = Uri.EscapeDataString(ghostSoulName);
        try
        {
            var xml = await _http.GetStringAsync(
                $"{gameApiBase}/api/rpg/SoulList/{SoulPath(ghostSoulName)}?depth=1&exclude=" +
                "DerivedStatsByName,Buffs,Roles,StaticData,PersistentData,Archetype,Inventory," +
                "CombatSoul,CompanionManager,EquipmentManager,FactionNode,SoulClass,SocialClass,StormDebug", ct);
            var m = SoulGuidRegex().Match(xml);
            return m.Success ? Guid.Parse(m.Groups[1].Value) : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Reads the soul NAME for a per-save Soul.Guid (WO-40 Phase 5). The DLL
    /// resolves damage targets through a SoulsByGuid lookup; the reflection
    /// API exposes the same container. Two route spellings are tried since
    /// only SoulsByName has ever been exercised from this side; a route that
    /// 404s on this build degrades to null (sender falls back to 0x12).
    /// </summary>
    public async Task<string?> ReadSoulNameByGuidAsync(Guid soulGuid, CancellationToken ct = default)
    {
        foreach (var route in new[] { "SoulsByGuid", "SoulsById" })
        {
            try
            {
                var xml = await _http.GetStringAsync(
                    $"{gameApiBase}/api/rpg/SoulList/{route}/{soulGuid}?depth=1&exclude=" +
                    "DerivedStatsByName,Buffs,Roles,StaticData,PersistentData,Archetype,Inventory," +
                    "CombatSoul,CompanionManager,EquipmentManager,FactionNode,SoulClass,SocialClass,StormDebug", ct);
                var m = SoulNameRegex().Match(xml);
                if (m.Success) return m.Groups[1].Value;
            }
            catch { /* try the next spelling */ }
        }
        return null;
    }

    /// <summary>
    /// WO-99 Phase 0: the local player's soul guid + name from the same
    /// PlayerSoul route the position read uses, one round trip. Same
    /// attribute regexes as the SoulsByName/SoulsByGuid reads (the route
    /// returns one Soul object; the first Name= on it is the soul's own).
    /// </summary>
    public async Task<(Guid? Guid, string? Name)> ReadPlayerSoulIdentityAsync(CancellationToken ct = default)
    {
        try
        {
            var xml = await _http.GetStringAsync(
                $"{gameApiBase}/api/rpg/SoulList/PlayerSoul?depth=1&exclude=" +
                "DerivedStatsByName,Buffs,Roles,StaticData,PersistentData,Archetype,Inventory," +
                "CombatSoul,CompanionManager,EquipmentManager,FactionNode,SoulClass,SocialClass,StormDebug", ct);
            var g = SoulGuidRegex().Match(xml);
            var n = SoulNameRegex().Match(xml);
            Guid? guid = g.Success && Guid.TryParse(g.Groups[1].Value, out var parsed) && parsed != Guid.Empty ? parsed : null;
            string? name = n.Success ? n.Groups[1].Value : null;
            return (guid, name);
        }
        catch { return (null, null); }
    }

    private static Guid[] ParseItemClasses(string xml)
    {
        var matches = ItemClassRegex().Matches(xml);
        var result = new Guid[matches.Count];
        for (int i = 0; i < matches.Count; i++)
            result[i] = Guid.Parse(matches[i].Groups[1].Value);
        return result;
    }

    // -------------------------------------------------------------------------
    // Unbatched Lua (WO-13)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runs one Lua statement immediately. Same wire path as the batched
    /// sender, minus the buffer -- so it inherits the property WO-12 s0.4
    /// proved and this depends on: an ExecuteString-driven statement executes
    /// straight away even while a menu has focus and Script.SetTimer is
    /// frozen.
    /// </summary>
    public Task ExecuteNowAsync(string lua, CancellationToken ct = default)
    {
        int enc = LuaCommandBudget.EncodedLength(lua) + LuaCommandBudget.WrapperEncodedChars;
        if (enc > MaxBatchChars)
        {
            OversizeDropped++;
            Console.WriteLine($"MP-BATCH-DROP reason=oversize-now encoded={enc} budget={MaxBatchChars} total={OversizeDropped} first=\"{(lua.Length > 100 ? lua[..100] + "..." : lua)}\"");
            return Task.CompletedTask;
        }
        return SendNowAsync($"pcall(function() {lua} end)", ct);
    }

    public async ValueTask DisposeAsync()
    {
        try { await FlushAsync(); } catch { }

        _recoveryCts?.Cancel();
        if (_recovery is not null)
        {
            try { await _recovery; } catch { }
        }
        _recoveryCts?.Dispose();
        _rotCts?.Cancel();
        if (_rotTask is not null)
        {
            try { await _rotTask; } catch { }
        }
        _rotCts?.Dispose();
        _batchLock.Dispose();
        _sendLock.Dispose();
        _http.Dispose();
    }

    [GeneratedRegex(@"GameTime=""([^""]+)""")]
    private static partial Regex GameTimeRegex();

    [GeneratedRegex(@"Position=""([^""]+)""")]
    private static partial Regex PosRegex();

    [GeneratedRegex(@">([^<]*)<")]
    private static partial Regex CvarValueRegex();

    [GeneratedRegex(@"ItemClass=""([0-9a-fA-F-]{36})""")]
    private static partial Regex ItemClassRegex();

    // Negative lookbehind for "Soul" so this matches the Soul element's own
    // Guid="..." attribute but not SharedSoulGuid="..." -- both end in
    // "Guid=", only the latter is preceded by "Soul".
    [GeneratedRegex(@"(?<!Soul)Guid=""([0-9a-fA-F-]{36})""")]
    private static partial Regex SoulGuidRegex();

    // The soul element's Name attribute -- restricted to the authored-name
    // charset because the value crosses onto the wire and into Lua string
    // literals downstream.
    [GeneratedRegex(@"\bName=""([A-Za-z0-9_]+)""")]
    private static partial Regex SoulNameRegex();
}
