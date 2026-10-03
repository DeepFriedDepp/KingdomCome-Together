// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace KCDMP_launcher.Models
{
    /// <summary>A process listening on a port the relay needs. <c>Ours</c>: its executable is this install's relay.</summary>
    public sealed record PortHolder(int Port, int Pid, string? ProcessName, bool Ours);

    /// <summary>What the running relay says about itself (GET api/local/status, loopback only).</summary>
    public sealed record RelayAnswer(string Release, string SteamState, uint SteamAppId);

    public enum RelayPlan { StartNew, Reuse, Replace, Refuse }

    public sealed record RelayDecision(RelayPlan Plan, IReadOnlyList<int> Pids, string Why, int BusyPort = 0, string? OtherProgram = null)
    {
        public int Pid => Pids.Count > 0 ? Pids[0] : 0;

        public string LogLine => Plan switch
        {
            RelayPlan.Reuse => $"MP-RELAY reuse pid={Pid}",
            RelayPlan.Replace => $"MP-RELAY replaced old pid={string.Join(",", Pids)} ({Why})",
            RelayPlan.Refuse => $"MP-RELAY port {BusyPort} busy: another program ({OtherProgram ?? "unknown"}, pid {Pid}) -- nothing stopped",
            _ => "MP-RELAY ports free",
        };

        /// <summary>For the Host window when another program holds a port (plain words, no path).</summary>
        public string PlayerMessage => Plan != RelayPlan.Refuse ? "" :
            $"Port {BusyPort} is in use by another program{(OtherProgram is null ? "" : $" ({OtherProgram})")}, so hosting can't start. " +
            "Close that program (or restart the computer), or choose another Host Port in Settings, then try again.";
    }

    /// <summary>
    /// WO-154 (Phase 7): Host when the relay's ports are taken. A launcher closed with the window's X
    /// leaves its relay running (it outlives the window on purpose: a session goes on without the
    /// launcher), and the next launcher's relay then failed to bind 7778 and 5273 and died -- six
    /// times in the 2026-10-02 bundles (five on one tester's machine, one on another). The host then
    /// connected to the old relay without its host claim. Now:
    /// <list type="bullet">
    /// <item>this install's own relay, answering as the same release with the same Steam settings: used again;</item>
    /// <item>this install's own relay of another release, other Steam settings, another port, or not answering:
    ///   that one process is stopped and ours is started;</item>
    /// <item>anything else on the port: nothing is stopped; the player is told which port and that another program uses it.</item>
    /// </list>
    /// </summary>
    public static class RelayReuse
    {
        public static RelayDecision Decide(IReadOnlyList<PortHolder> holders, int tcpPort, RelayAnswer? answer,
                                           string myRelease, bool allowSteam, uint steamAppId)
        {
            if (holders.Count == 0) return new RelayDecision(RelayPlan.StartNew, Array.Empty<int>(), "ports free");

            if (holders.FirstOrDefault(h => !h.Ours) is { } other)
                return new RelayDecision(RelayPlan.Refuse, new[] { other.Pid }, "another program", other.Port, other.ProcessName);

            var pids = holders.Select(h => h.Pid).Distinct().ToList();
            RelayDecision Replace(string why) => new(RelayPlan.Replace, pids, why);

            if (pids.Count > 1) return Replace("more than one relay of this install");
            if (!holders.Any(h => h.Port == tcpPort)) return Replace($"it does not listen on port {tcpPort}");
            if (answer is null) return Replace("it does not answer");
            if (answer.Release != myRelease) return Replace($"release {answer.Release}, this install is {myRelease}");
            bool steamOn = !string.Equals(answer.SteamState, "off", StringComparison.Ordinal);
            if (steamOn != allowSteam) return Replace(allowSteam ? "Steam is off in it" : "Steam is on in it");
            if (allowSteam && answer.SteamAppId != 0 && answer.SteamAppId != steamAppId)
                return Replace($"Steam app {answer.SteamAppId}, Settings say {steamAppId}");
            return new RelayDecision(RelayPlan.Reuse, pids, "same release, same settings");
        }

        /// <summary>The same file, by full path (case-insensitive, as Windows compares them).</summary>
        public static bool SamePath(string? a, string? b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        }

        /// <summary>Who listens on these ports: each listener's pid, name, and whether its executable is <paramref name="ourRelayPath"/>.</summary>
        public static List<PortHolder> Holders(string ourRelayPath, params int[] ports)
        {
            var list = new List<PortHolder>();
            foreach (int port in ports.Distinct())
                foreach (int pid in PortOwners.ListeningPids(port))
                {
                    var (name, path) = Describe(pid);
                    list.Add(new PortHolder(port, pid, name, SamePath(path, ourRelayPath)));
                }
            return list;
        }

        /// <summary>A process's name and executable path; nulls when it is gone or not ours to read.</summary>
        public static (string? Name, string? Path) Describe(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                string? path = null;
                try { path = p.MainModule?.FileName; } catch { }
                return (p.ProcessName, path);
            }
            catch { return (null, null); }
        }
    }

    /// <summary>The TCP listeners of a port, by owning process (iphlpapi GetExtendedTcpTable, IPv4 and IPv6).</summary>
    public static class PortOwners
    {
        private const int AfInet = 2, AfInet6 = 23;
        private const int TcpTableOwnerPidListener = 3;
        private const uint ErrorInsufficientBuffer = 122;

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, uint reserved);

        /// <summary>The pids listening on <paramref name="port"/>; empty when none, or when the table cannot be read.</summary>
        public static IReadOnlyList<int> ListeningPids(int port)
        {
            var pids = new List<int>();
            Read(AfInet, port, pids);
            Read(AfInet6, port, pids);
            return pids.Distinct().ToList();
        }

        private static void Read(int af, int port, List<int> into)
        {
            // MIB_TCPROW_OWNER_PID: state, local addr, local port, remote addr, remote port, pid (6 DWORDs).
            // MIB_TCP6ROW_OWNER_PID: local addr[16], scope, local port, remote addr[16], scope, remote port, state, pid.
            int rowSize = af == AfInet ? 24 : 56, portAt = af == AfInet ? 8 : 20, pidAt = af == AfInet ? 20 : 52;
            int size = 0;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                uint r = GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, TcpTableOwnerPidListener, 0);
                if (r != ErrorInsufficientBuffer && r != 0) return;
                IntPtr buf = Marshal.AllocHGlobal(Math.Max(size, 4));
                try
                {
                    r = GetExtendedTcpTable(buf, ref size, false, af, TcpTableOwnerPidListener, 0);
                    if (r == ErrorInsufficientBuffer) continue;   // the table grew in between: ask again
                    if (r != 0) return;
                    int n = Marshal.ReadInt32(buf);
                    for (int i = 0; i < n; i++)
                    {
                        IntPtr row = buf + 4 + i * rowSize;
                        int raw = Marshal.ReadInt32(row, portAt);
                        int p = ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);   // network byte order in the low word
                        if (p == port) into.Add(Marshal.ReadInt32(row, pidAt));
                    }
                    return;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
    }
}
