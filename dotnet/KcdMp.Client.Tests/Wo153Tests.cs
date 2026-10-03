// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Net;
using System.Text.RegularExpressions;

namespace KcdMp.Client.Tests;

/// <summary>WO-153: dropped commands (phase 3) -- a batch the game does not answer is kept and sent again in order.</summary>
public class Wo153Tests
{
    // ---------------------------------------------------------------- the classification

    [Theory]
    [InlineData("KCD2MP_UpdateGhost(\"0\",1.5,2.5,3.5,0.5,false)", BatchKind.Level, "KCD2MP_UpdateGhost|\"0\"")]
    [InlineData("if KCD2MP_W131Tick then KCD2MP_W131Tick(1, 2) end", BatchKind.Level, "KCD2MP_W131Tick|1")]
    [InlineData("if KCD2MP_ApplyNpcState then KCD2MP_ApplyNpcState(\"ttkc_man_2\",1.000,2.000,3.000,0.5,100.0,0,1,2,3) end", BatchKind.Level, "KCD2MP_ApplyNpcState|\"ttkc_man_2\"")]
    [InlineData("if KCD2MP_ShowNativeToast then KCD2MP_ShowNativeToast(\"hello\") end", BatchKind.Edge, null)]
    [InlineData("if KCD2MP_W151DoorApply then KCD2MP_W151DoorApply(1,2,3) end", BatchKind.Edge, null)]
    [InlineData("if KCD2MP_W137TalkDropped then KCD2MP_W137TalkDropped(48) end", BatchKind.Stale, null)]
    [InlineData("if KCD2MP_ApplyNativeScan then KCD2MP_ApplyNativeScan(\"a:1:2:3:4:0\",5,1,2) end", BatchKind.Stale, null)]
    // two calls in one statement: another statement's first call cannot stand in for it
    [InlineData("if KCD2MP_Wo102Set then KCD2MP_Wo102Set(\"authority_host\", true, \"agent\") KCD2MP_Wo102Set(\"pos_native\", true, \"agent\") end", BatchKind.Edge, null)]
    // not a call of the function it guards: not understood, so kept
    [InlineData("if KCD2MP_A then KCD2MP_UpdateGhost(\"0\",1) end", BatchKind.Edge, null)]
    [InlineData("System.LogAlways(\"x\")", BatchKind.Edge, null)]
    public void A_statement_is_a_re_sent_state_an_edge_or_stale(string lua, BatchKind kind, string? key)
    {
        var (k, key2) = BatchPolicy.Classify(lua);
        Assert.Equal(kind, k);
        Assert.Equal(key, key2);
    }

    // ---------------------------------------------------------------- the queue

    private static BatchQueue.Entry E(string lua, long at = 0, int enc = 100)
    {
        var (kind, key) = BatchPolicy.Classify(lua);
        return new BatchQueue.Entry { Lua = lua, Text = lua, Kind = kind, Key = key, Encoded = enc, EnqueuedMs = at, OnceId = kind == BatchKind.Edge ? "e-" + lua.GetHashCode() : null };
    }

    private static string Ghost(string id, int n) => $"KCD2MP_UpdateGhost(\"{id}\",{n},0,0,0,false)";
    private static string Toast(string s) => $"if KCD2MP_ShowNativeToast then KCD2MP_ShowNativeToast(\"{s}\") end";

    [Fact]
    public void A_newer_state_replaces_an_older_one_in_place_of_queueing_both_and_order_is_kept()
    {
        var q = new BatchQueue();
        q.Add(E(Ghost("0", 1))); q.Add(E(Toast("a"))); q.Add(E(Ghost("1", 1))); q.Add(E(Ghost("0", 2)));
        Assert.Equal(1, q.Superseded);
        var b = q.TakeBatch(10_000, 0);
        Assert.Equal(new[] { Toast("a"), Ghost("1", 1), Ghost("0", 2) }, b.Select(e => e.Lua));   // the newer ghost 0 queues behind the others
    }

    [Fact]
    public void An_edge_is_never_replaced_even_by_its_twin()
    {
        var q = new BatchQueue();
        q.Add(E(Toast("same"))); q.Add(E(Toast("same")));
        Assert.Equal(2, q.TakeBatch(10_000, 0).Count);
        Assert.Equal(0, q.Superseded);
    }

    [Fact]
    public void A_batch_is_cut_at_the_encoded_budget_and_the_rest_waits()
    {
        var q = new BatchQueue();
        for (int i = 0; i < 5; i++) q.Add(E(Toast("t" + i), enc: 400));
        var b1 = q.TakeBatch(1900, 0);
        Assert.Equal(4, b1.Count);
        Assert.Single(q.TakeBatch(1900, 0));
    }

    [Fact]
    public void A_failed_batch_goes_back_in_front_in_order_and_a_replaced_state_is_not_resent()
    {
        var q = new BatchQueue();
        q.Add(E(Toast("one"))); q.Add(E(Ghost("0", 1))); q.Add(E(Toast("two"))); q.Add(E(Ghost("1", 1)));
        var sent = q.TakeBatch(10_000, 0);
        // while it was out, a newer sample of ghost 0 and a new edge arrived
        q.Add(E(Ghost("0", 9))); q.Add(E(Toast("three")));
        q.Requeue(sent);
        var next = q.TakeBatch(10_000, 0).Select(e => e.Lua).ToArray();
        Assert.Equal(new[] { Toast("one"), Toast("two"), Ghost("1", 1), Ghost("0", 9), Toast("three") }, next);   // ghost 0's old sample is gone; nothing overtook an edge
        Assert.Equal(1, q.Superseded);
    }

    [Fact]
    public void A_stale_relay_is_dropped_not_replayed_late()
    {
        var q = new BatchQueue();
        q.Add(E("if KCD2MP_W137TalkDropped then KCD2MP_W137TalkDropped(7) end")); q.Add(E(Toast("keep")));
        var sent = q.TakeBatch(10_000, 0);
        q.Requeue(sent);
        Assert.Equal(new[] { Toast("keep") }, q.TakeBatch(10_000, 0).Select(e => e.Lua));
        Assert.Equal(1, q.StaleDropped);
    }

    [Fact]
    public void Time_to_live_and_the_bound_are_enforced_and_counted()
    {
        var q = new BatchQueue { LevelTtlMs = 15_000, EdgeTtlMs = 120_000, MaxEntries = 4 };
        q.Add(E(Ghost("0", 1), at: 0)); q.Add(E(Toast("old"), at: 0));
        var b = q.TakeBatch(10_000, 16_000);                       // the state is 16 s old (gone), the edge lives 120 s
        Assert.Equal(new[] { Toast("old") }, b.Select(e => e.Lua));
        Assert.Equal(1, q.Expired);
        q.Requeue(b);
        Assert.Empty(q.TakeBatch(10_000, 121_000));                 // an edge outlives nothing past 120 s
        Assert.Equal(2, q.Expired);
        // over the bound: the oldest state goes first, an edge only when there is no state left
        q.Add(E(Toast("e1"))); q.Add(E(Ghost("a", 1))); q.Add(E(Toast("e2"))); q.Add(E(Toast("e3"))); q.Add(E(Toast("e4")));
        Assert.Equal(1, q.Overflowed);
        Assert.Equal(new[] { Toast("e1"), Toast("e2"), Toast("e3"), Toast("e4") }, q.TakeBatch(10_000, 0).Select(e => e.Lua));
        q.Add(E(Toast("f1"))); q.Add(E(Toast("f2"))); q.Add(E(Toast("f3"))); q.Add(E(Toast("f4"))); q.Add(E(Toast("f5")));
        Assert.Equal(2, q.Overflowed);
        Assert.Equal(new[] { Toast("f2"), Toast("f3"), Toast("f4"), Toast("f5") }, q.TakeBatch(10_000, 0).Select(e => e.Lua));
    }

    // ---------------------------------------------------------------- the transport against a game that stops answering

    private sealed class FakeGame : HttpMessageHandler
    {
        public readonly List<string> Commands = new();     // every command the game ACTUALLY ran (decoded), in order
        public int Calls;
        public Func<int, bool> Answers = _ => true;       // by call number (1-based): false = the game does not answer (a timeout)
        public bool RunLateOnTimeout;                      // the game runs the command it did not answer in time (the field's "late")
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            int n = Interlocked.Increment(ref Calls);
            string cmd = Uri.UnescapeDataString(Regex.Match(req.RequestUri!.Query, "command=([^&]*)").Groups[1].Value);
            lock (Commands)
            {
                if (Answers(n)) { Commands.Add(cmd); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") }); }
                if (RunLateOnTimeout) Commands.Add(cmd);
            }
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 0.8 seconds elapsing.");
        }
        public string[] Run { get { lock (Commands) return Commands.ToArray(); } }
    }

    private static HttpGameTransport Transport(FakeGame g) => new("http://localhost:1403", 800, g) { RecoveryIntervalMs = 15 };

    private static async Task WaitFor(Func<bool> cond, int ms = 3000)
    {
        var until = Environment.TickCount64 + ms;
        while (!cond() && Environment.TickCount64 < until) await Task.Delay(5);
        Assert.True(cond(), "timed out waiting");
    }

    [Fact]
    public async Task A_healthy_game_gets_every_statement_in_order_in_one_command()
    {
        var g = new FakeGame();
        await using var t = Transport(g);
        await t.ExecuteAsync(Ghost("0", 1)); await t.ExecuteAsync(Toast("a")); await t.ExecuteAsync(Ghost("1", 1));
        await t.FlushAsync();
        var cmd = Assert.Single(g.Run);
        Assert.True(cmd.IndexOf("UpdateGhost(\"0\"") < cmd.IndexOf("ShowNativeToast(\"a\")"));
        Assert.True(cmd.IndexOf("ShowNativeToast(\"a\")") < cmd.IndexOf("UpdateGhost(\"1\""));
        Assert.Equal(0, t.BatchesDropped);
        Assert.Equal(0, t.BatchesRetried);
    }

    [Fact]
    public async Task A_batch_the_game_does_not_answer_is_kept_and_arrives_after_the_hold_in_order_with_an_edge_once()
    {
        var g = new FakeGame { Answers = n => n > 6 };   // the first six calls (two flushes and the first recoveries) get no answer
        await using var t = Transport(g);
        await t.ExecuteAsync(Toast("first"));
        await t.ExecuteAsync(Ghost("0", 1));
        await t.FlushAsync();                              // no answer: kept
        Assert.Equal(1, t.BatchesRetried);
        await t.ExecuteAsync(Ghost("0", 2));               // a newer sample while the old one waits
        await t.ExecuteAsync(Toast("second"));
        await t.FlushAsync();                              // no answer again: the hold begins
        Assert.True(t.Held);
        Assert.Equal(1, t.Holds);
        await t.ExecuteAsync(Toast("third"));
        await t.FlushAsync();                              // held: returns at once, sends nothing
        await WaitFor(() => !t.Held);
        await t.FlushAsync();
        var all = string.Join("\n", g.Run);
        // every edge ran exactly once, in order; the state ran in its newest form only
        Assert.Equal(1, Regex.Matches(all, "ShowNativeToast\\(\"first\"\\)").Count);
        Assert.Equal(1, Regex.Matches(all, "ShowNativeToast\\(\"second\"\\)").Count);
        Assert.Equal(1, Regex.Matches(all, "ShowNativeToast\\(\"third\"\\)").Count);
        Assert.True(all.IndexOf("\"first\"") < all.IndexOf("\"second\"") && all.IndexOf("\"second\"") < all.IndexOf("\"third\""));
        Assert.DoesNotContain("UpdateGhost(\"0\",1,", all);
        Assert.Contains("UpdateGhost(\"0\",2,", all);
        Assert.Contains("KCD2MP_Once(\"", all);
        Assert.Equal(0, t.BatchesDropped);
        Assert.Equal(0, t.Queued);
    }

    [Fact]
    public async Task An_edge_carries_the_same_once_id_on_every_attempt_so_a_late_run_cannot_run_twice()
    {
        var g = new FakeGame { Answers = n => n > 1, RunLateOnTimeout = true };   // call 1 is not answered but the game runs it anyway
        await using var t = Transport(g);
        await t.ExecuteAsync(Toast("once"));
        await t.FlushAsync();
        await t.FlushAsync();
        await WaitFor(() => !t.Held && t.Queued == 0);
        await t.FlushAsync();
        var runs = g.Run.Where(c => c.Contains("ShowNativeToast(\"once\")")).ToArray();
        Assert.Equal(2, runs.Length);                       // the game really got it twice ...
        var ids = runs.Select(c => Regex.Match(c, "KCD2MP_Once\\(\"([^\"]+)\"").Groups[1].Value).ToArray();
        Assert.False(string.IsNullOrEmpty(ids[0]));
        Assert.Equal(ids[0], ids[1]);                       // ... under one id, which the mod's guard runs once
    }

    [Fact]
    public async Task With_retry_off_a_batch_with_no_answer_is_dropped_as_in_0_43_0()
    {
        var g = new FakeGame { Answers = n => n > 1 };
        await using var t = Transport(g);
        t.RetryBatches = false;
        await t.ExecuteAsync(Toast("lost"));
        await t.FlushAsync();
        Assert.Equal(1, t.BatchesDropped);
        Assert.Equal(1, t.StatementsDropped);
        Assert.Equal(0, t.Queued);
        Assert.False(t.Held);
        Assert.DoesNotContain("KCD2MP_Once", string.Join("\n", g.Run));
    }

    [Fact]
    public async Task A_stale_talk_relay_is_not_replayed_after_the_game_comes_back()
    {
        var g = new FakeGame { Answers = n => n > 3 };
        await using var t = Transport(g);
        await t.ExecuteAsync("if KCD2MP_W137TalkDropped then KCD2MP_W137TalkDropped(48) end");
        await t.ExecuteAsync(Toast("keep"));
        await t.FlushAsync();
        await t.FlushAsync();
        await WaitFor(() => !t.Held && t.Queued == 0);
        var all = string.Join("\n", g.Run);
        Assert.DoesNotContain("TalkDropped", all);
        Assert.Contains("ShowNativeToast(\"keep\")", all);
        Assert.Equal(1, t.StaleDropped);
    }

    [Fact]
    public async Task A_game_that_never_answers_is_held_not_hammered_and_the_queue_stays_bounded()
    {
        var g = new FakeGame { Answers = _ => false };
        await using var t = Transport(g);
        for (int i = 0; i < 40; i++) { await t.ExecuteAsync(Ghost("0", i)); await t.ExecuteAsync(Toast("e" + i)); }
        await t.FlushAsync(); await t.FlushAsync();
        Assert.True(t.Held);
        int callsAtHold = g.Calls;
        for (int i = 0; i < 400; i++) { await t.ExecuteAsync(Toast("x" + i)); await t.FlushAsync(); }
        Assert.True(t.Queued <= 256);
        Assert.True(t.Overflowed > 0);
        Assert.True(g.Calls - callsAtHold < 40, $"a held transport probes about once per recovery interval, not per flush ({g.Calls - callsAtHold} calls)");
    }
}
