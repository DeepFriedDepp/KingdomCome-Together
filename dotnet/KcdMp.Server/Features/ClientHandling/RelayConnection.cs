using System.Net;
using System.Net.Sockets;

namespace KcdMp.Server.Features.ClientHandling;

/// <summary>
/// WO-127: one client's byte stream, whatever carries it. A TCP socket (the
/// original path, unchanged) or a Steam P2P connection
/// (KcdMp.Steam.SteamConnectionStream). ClientSession reads and writes the
/// same frames on either.
///
/// <see cref="IsLoopback"/> feeds the relay-local authority rule (WO-110 R4),
/// so it is true only for a TCP socket from a loopback address. A Steam peer
/// is never local, whatever machine it is on: a Steam joiner must not take
/// authority from the host's own agent (docs/WO-120-findings.md, "Constraint
/// found for Phase 1").
///
/// <see cref="Remote"/> is what log lines print: the TCP endpoint as before,
/// and only "steam-peer" for Steam (never a SteamID).
/// </summary>
public sealed class RelayConnection : IDisposable
{
    private readonly Action _dispose;
    private int _disposed;

    public Stream Stream { get; }
    public string Remote { get; }
    public bool IsLoopback { get; }
    public string Transport { get; }

    /// <summary>
    /// WO-144: the machine on the other end, for "the same player connected
    /// again" (ClientHandler.SupersededBy): the TCP address without its port, or
    /// a hash of the Steam peer (SteamP2PConnection.PeerKey). Never logged.
    /// </summary>
    public string IdentityKey { get; }

    public RelayConnection(Stream stream, string remote, bool isLoopback, string transport, Action dispose, string identityKey = "")
    {
        Stream = stream;
        Remote = remote;
        IsLoopback = isLoopback;
        Transport = transport;
        _dispose = dispose;
        IdentityKey = identityKey;
    }

    public static RelayConnection FromTcp(TcpClient tcp)
    {
        var ep = tcp.Client.RemoteEndPoint;
        bool loop = ep is IPEndPoint ip && IPAddress.IsLoopback(ip.Address);
        string key = ep is IPEndPoint ip2 ? "tcp:" + (ip2.Address.IsIPv4MappedToIPv6 ? ip2.Address.MapToIPv4() : ip2.Address) : "tcp:?";
        return new RelayConnection(tcp.GetStream(), ep?.ToString() ?? "(unknown)", loop, "tcp", tcp.Dispose, key);
    }

    /// <summary>A Steam P2P stream: never loopback, logged without an id.</summary>
    public static RelayConnection FromSteam(Stream steamStream, string peerKey = "") =>
        new(steamStream, "steam-peer", isLoopback: false, "steam", steamStream.Dispose, "steam:" + peerKey);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _dispose(); } catch { /* closing a dead transport */ }
    }
}
