// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.IO.Pipes;
using KcdMp.Client;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-154 6.6: the field's "tick failed: ChannelClosedException" at every game exit. A command waits for the DLL's
/// answer; the game quits; the pipe reader exits and its Drop completes the reply channel. The waiting command must
/// come back "not connected", not throw. (A server under a test-only pipe name: never the game's "kcdmp".)
/// </summary>
public class Wo154PipeTests
{
    [Fact]
    public async Task A_command_waiting_when_the_game_quits_comes_back_not_connected()
    {
        string name = "kcdmp-test-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var accept = server.WaitForConnectionAsync();
        await using var pipe = new CombatPipe(name);
        Assert.True(await pipe.EnsureConnectedAsync());
        await accept;

        // the command goes out and waits; the "game" reads its whole frame (the write is flushed: the command is
        // waiting for its answer, not writing), then goes away without an answer
        var waiting = pipe.Wo138Async(4, []);
        var head = await ReadExactly(server, 3);
        await ReadExactly(server, head[1] | (head[2] << 8));
        await Task.Delay(200);   // the command is in its wait
        server.Disconnect();

        var r = await waiting.WaitAsync(TimeSpan.FromSeconds(4));   // well inside the 5 s reply deadline
        Assert.Null(r);
        Assert.False(pipe.IsConnected);
    }

    private static async Task<byte[]> ReadExactly(Stream s, int len)
    {
        var buf = new byte[len];
        int got = 0;
        while (got < len)
        {
            int n = await s.ReadAsync(buf.AsMemory(got));
            Assert.True(n > 0);
            got += n;
        }
        return buf;
    }
}
