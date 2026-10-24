// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.IO.Pipes;
using KcdMp.Client;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-206 A.2: two copies on one PC never cross pipes or files. Two stand-in DLLs (pipe servers under the per-copy
/// names) and two agents' pipe clients: each client's frames arrive at its own server only. The twin is the players'
/// single name for both copies: the second copy's agent cannot reach a DLL at all, which is the collision the per-copy
/// names remove. (Test-only ids; never the game's "kcdmp".)
/// </summary>
public class Wo206BenchTests
{
    [Fact]
    public void Names_follow_the_DLLs_rule()
    {
        Assert.Equal("kcdmp", BenchInstance.PipeNameFor(null));
        Assert.Equal("kcdmp-B", BenchInstance.PipeNameFor(BenchInstance.Valid("B")));
        Assert.Null(BenchInstance.Valid(@"..\x"));
        Assert.Null(BenchInstance.Valid("a b"));
        Assert.Null(BenchInstance.Valid(new string('a', 25)));
        Assert.Equal(new string('a', 24), BenchInstance.Valid(new string('a', 24)));
        Assert.Null(BenchInstance.Id);   // the test process has no id: every lookup is a player's
    }

    [Fact]
    public async Task Two_copies_never_cross_pipes()
    {
        string tag = Guid.NewGuid().ToString("N")[..8];
        string a = BenchInstance.PipeNameFor("tA" + tag), b = BenchInstance.PipeNameFor("tB" + tag);
        var (gotA, gotB) = await TwoAgents(a, b);
        Assert.Equal(("A", "B"), (gotA, gotB));
    }

    [Fact]
    public async Task Twin_one_shared_name_leaves_the_second_copy_without_its_DLL()
    {
        string shared = BenchInstance.PipeNameFor("tS" + Guid.NewGuid().ToString("N")[..8]);
        var (gotA, gotB) = await TwoAgents(shared, shared);
        Assert.Equal("A", gotA);
        Assert.Null(gotB);   // the per-copy names are what makes the second copy reachable
    }

    /// <summary>Two stand-in DLLs, each serving its own name once; two agents send one frame each. Returns which agent's
    /// frame each server saw (null = that server got no client).</summary>
    private static async Task<(string? A, string? B)> TwoAgents(string nameA, string nameB)
    {
        var serverA = new NamedPipeServerStream(nameA, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        NamedPipeServerStream? serverB = null;
        try { serverB = new NamedPipeServerStream(nameB, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous); }
        catch (IOException) { }   // one name, one instance: the second "DLL" cannot even listen
        try
        {
            var acceptA = serverA.WaitForConnectionAsync();
            var acceptB = serverB?.WaitForConnectionAsync();
            await using var agentA = new CombatPipe(nameA);
            await using var agentB = new CombatPipe(nameB);
            Assert.True(await agentA.EnsureConnectedAsync());
            await acceptA;
            bool bUp = await agentB.EnsureConnectedAsync();
            if (bUp && acceptB is not null) await acceptB;

            // each agent sends a frame whose body names it; each server reads what reached it
            _ = agentA.Wo138Async(4, "A"u8.ToArray());
            if (bUp) _ = agentB.Wo138Async(4, "B"u8.ToArray());
            string? seenA = await ReadTag(serverA);
            string? seenB = bUp && serverB is not null ? await ReadTag(serverB) : null;
            return (seenA, seenB);
        }
        finally
        {
            serverA.Dispose();
            serverB?.Dispose();
        }
    }

    private static async Task<string?> ReadTag(Stream s)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var head = new byte[3];
        if (!await ReadExactly(s, head, cts.Token)) return null;
        var body = new byte[head[1] | (head[2] << 8)];
        if (!await ReadExactly(s, body, cts.Token)) return null;
        return body.Length > 0 && (body[^1] == (byte)'A' || body[^1] == (byte)'B') ? ((char)body[^1]).ToString() : "?";
    }

    private static async Task<bool> ReadExactly(Stream s, byte[] buf, CancellationToken ct)
    {
        int got = 0;
        while (got < buf.Length)
        {
            int n = await s.ReadAsync(buf.AsMemory(got), ct);
            if (n <= 0) return false;
            got += n;
        }
        return true;
    }

    [Fact]
    public void A_bench_copy_reads_its_own_log_and_saves_folder()
    {
        string root = Path.Combine(Path.GetTempPath(), "w206-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "kcd.log"), "old\n");
            File.SetLastWriteTimeUtc(Path.Combine(root, "kcd.log"), DateTime.UtcNow.AddMinutes(-10));
            File.WriteAllText(Path.Combine(root, "kcd(1).log"), "x\nUser folder is 'D:\\Profiles\\p\\Saved Games\\KingdomCome2_B(1)'\ny\n");
            File.WriteAllText(Path.Combine(root, "kcd_launcher.log"), "not a game log\n");
            string? log = BenchInstance.NewestLog(root);
            Assert.EndsWith("kcd(1).log", log);
            Assert.Equal(@"D:\Profiles\p\Saved Games\KingdomCome2_B(1)\saves", BenchInstance.SavesDirFromLog(log));
            Assert.Null(BenchInstance.SavesDirFromLog(Path.Combine(root, "kcd.log")));
        }
        finally { Directory.Delete(root, true); }
    }
}
