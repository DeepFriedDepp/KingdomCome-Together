// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using ILogger = Serilog.ILogger;

namespace KcdMp.Server.Features.ClientHandling;

/// <summary>
/// WO-213 A.1/A.3/A.4: the relay's own view of each client's link, in relay.log. Every 10 s per ready client
///   MP-LINK relay client=&lt;id&gt; role=&lt;host|joiner&gt; via=&lt;tcp|steam|shaped&gt; &lt;its queue&gt; | &lt;what crossed it, per kind&gt;
/// and every 2 s for a Steam client its connection's own numbers (MP-LINK steam). Roles and ids only, never a name.
/// </summary>
public sealed class LinkReportService : BackgroundService
{
    private readonly ILogger _logger;
    private readonly ClientHandler _clients;

    public LinkReportService(ILogger logger, ClientHandler clients)
    {
        _logger = logger;
        _clients = clients;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        long n = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(2000, ct);
                n++;
                foreach (var c in _clients.GetClients())
                {
                    if (!c.IsReady) continue;
                    string role = _clients.IsDamageAuthority(c) ? "host" : "joiner";
                    if (c.SteamConnection is { } sc && sc.RealTimeStatus() is { } s)
                        _logger.Information("{Line:l}", string.Create(CultureInfo.InvariantCulture,
                            $"MP-LINK steam who=relay client={c.Id} role={role} ping={s.PingMs} q_local={s.QualityLocal:F2} q_remote={s.QualityRemote:F2} out_pps={s.OutPacketsPerSec:F0} out_Bps={s.OutBytesPerSec:F0} in_Bps={s.InBytesPerSec:F0} send_rate={s.SendRateBytesPerSec} pend_unrel={s.PendingUnreliable} pend_rel={s.PendingReliable} unacked={s.SentUnackedReliable} queue_ms={KcdMp.Steam.SteamLinkStatus.QueueMsText(s.QueueTimeUsec)} lanes={s.Lanes.Length} unrel_sent={Interlocked.Read(ref sc.UnreliableSent)} unrel_refused={Interlocked.Read(ref sc.UnreliableRefused)}"));
                    if (n % 5 != 0) continue;
                    string via = c.LinkPathTransport;
                    _logger.Information("{Line:l}", $"MP-LINK relay client={c.Id} role={role} via={via} {c.TakeQueueText()}{c.SchedulerText()} | {c.Meter.TakeLine("relay-" + role)}");
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}
