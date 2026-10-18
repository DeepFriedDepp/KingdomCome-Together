// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-200 I.3 (qitem_mirror): quest items go to both players, and the host's counts decide. In this game a quest item exists exactly
/// while its AddQuestItem effect is active, and every such effect takes IsActive from a quest value (433 of 433 in the data); the engine
/// refuses to create, delete or move a quest item any other way. The item therefore follows the host through the quest-state mirror,
/// and this part measures it: the classes are the game's own IsQuestItem rows (Tables.pak, WO-124's list); the host's script reports its
/// Henry's count of each when it changes (and all it holds every 60 s); the joiner's compares (agreed / differs; "pending" while his own
/// replay may still be confirmed, at most 5 minutes). Only the player's inventory is counted (the stash is the host's).
/// </summary>
public sealed partial class GameBridge
{
    private volatile bool _w200QPushed;
    private long _w200QSent, _w200QIn, _w200LastOwnReplayMs;

    /// <summary>The quest-item classes handed to the game's script once per connection (both roles); chunks below the console ceiling.</summary>
    private async Task Wo200PushQuestClassesAsync()
    {
        if (_w200QPushed) return;
        _w200QPushed = true;
        string? tables = TablesPakPath();
        if (tables is null) { Console.WriteLine("WO200-QITEM Tables.pak not found -- the quest-item mirror has no classes (off in effect)"); return; }
        try { _questClasses ??= WhsSave.QuestClasses(tables); }
        catch (Exception ex) { Console.WriteLine($"WO200-QITEM quest-item classes not read ({ex.GetType().Name}: {ex.Message}) -- the mirror has no classes"); return; }
        var all = _questClasses.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        await ExecLuaAsync("if KCD2MP_W200QClassReset then KCD2MP_W200QClassReset() end");
        for (int i = 0; i < all.Count; i += Wo200Rules.QClassChunk)
            await ExecLuaAsync($"if KCD2MP_W200QClassAdd then KCD2MP_W200QClassAdd(\"{string.Join(' ', all.Skip(i).Take(Wo200Rules.QClassChunk))}\") end");
        Console.WriteLine($"WO200-QITEM {all.Count} quest-item classes (the game's IsQuestItem rows) given to the game's script");
    }

    /// <summary>Every 3 s while a session runs: the host's script reports its changes; the joiner's notes its own (the protection).</summary>
    private async Task Wo200QItemsTickAsync()
    {
        if (!W200("qitem_mirror")) return;
        bool host = W137Host && Wo134Peers().Any();
        if (!host && !W137Joiner) return;
        await Wo200PushQuestClassesAsync();
        await ExecLuaAsync($"if KCD2MP_W200QItemsTick then KCD2MP_W200QItemsTick({B(host)}) end");
    }

    /// <summary>The host's script: "class:count class:count ..." -- each to every joiner as an item count (QuestHost kind 8).</summary>
    private async Task Wo200OnHostQItemsAsync(string? arg)
    {
        if (!W137Host || !W200("qitem_mirror")) return;
        var peers = Wo134Peers().ToList();
        if (peers.Count == 0) return;
        foreach (string pair in (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int c = pair.IndexOf(':');
            if (c <= 0 || !Guid.TryParse(pair.AsSpan(0, c), out Guid cls)
                || !int.TryParse(pair.AsSpan(c + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n > Wo200Rules.QCountMax) continue;
            string text = Wo200Rules.ItemCountText(n, cls);
            foreach (byte g in peers) await Wo137SendAsync(Protocol.QuestHostUp, g, Protocol.QuestHostItemCount, 0, text);
            Interlocked.Increment(ref _w200QSent);
        }
    }

    /// <summary>The joiner: the host's count of one quest-item class; his own replay sent in the last 5 minutes protects his own changes.</summary>
    private void Wo200OnHostItemCount(string text)
    {
        if (!W137Joiner || !W200("qitem_mirror")) return;
        if (!Wo200Rules.TryParseItemCountText(text, out int n, out Guid cls) || n > Wo200Rules.QCountMax) { Wo137Veto("item-count-malformed"); return; }
        Interlocked.Increment(ref _w200QIn);
        long last = Interlocked.Read(ref _w200LastOwnReplayMs);
        bool pending = last != 0 && W200Now() - last < Wo200Rules.SuppressTtl.TotalMilliseconds;
        _ = ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W200QItemHost then KCD2MP_W200QItemHost(\"{cls:D}\", {n}, {(pending ? 1 : 0)}) end"));
    }

    private string Wo200ItemsStats() => FormattableString.Invariant($"qitems_sent={_w200QSent} qitems_in={_w200QIn}");
}
