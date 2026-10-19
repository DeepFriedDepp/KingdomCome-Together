// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;

namespace KcdMp.Client;

// WO-201 Act I: scenes and conversations on the joiner (docs/WO-201-findings.md).
//
//   B2 scene_queue_stale: a scene of this player's (his talk or request just before it) that the engine puts into its waiting queue is
//      watched; one that waited more than 10 s never starts late -- the moment its conversation starts it is ended the game's own way
//      (InterruptDialogs) and the player is told "That conversation was cancelled - talk again." (the field: 336 s late, 209 m away).
//   B6 monologue_rescue_exempt: his own monologue (only the player in it) starts no rescue bound; a rescue's toast only for a talk he
//      pressed himself (the engine's "OnChatWithFocus: Dude->x").
//   B8 twins_grace: each of this player's dialogue requests that names other souls is told to the Lua until the engine ends, interrupts
//      or cancels it -- a scoped twin it names is never paused meanwhile (KCD2MP_W201TwinWanted).
public partial class GameBridge
{
    private long _w201OwnPressMs, _w201DudeAskMs;
    private long _w201TwinWants, _w201QueueSeen, _w201QueueStale, _w201QueueCancelled;
    private readonly ConcurrentDictionary<string, long> _w201Queued = new(StringComparer.Ordinal);

    /// <summary>B6: the conversation the rescue ends now was asked for by the player himself.</summary>
    private bool Wo201OwnTalk() => Wo201Rules.OwnTalk(Volatile.Read(ref _w201OwnPressMs), Volatile.Read(ref _w200AttemptMs));

    /// <summary>Wo137OnQuestLine's parsed lines (request / attempt / end), joiner.</summary>
    private void Wo201OnQuestLine(in Wo137Rules.QuestLineInfo q)
    {
        if (!W137Joiner) return;
        if (q.Kind is "request" or "attempt") Volatile.Write(ref _w201DudeAskMs, Environment.TickCount64);
        if (q.Kind == "attempt" && q.Souls.Contains("Dude") && q.Souls.Length >= 2 && !q.Bark)
        {
            Interlocked.Increment(ref _w201TwinWants);
            _ = ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W201TwinWant then KCD2MP_W201TwinWant({q.Id}, \"{string.Join(' ', q.Souls)}\", 1) end"));
        }
        else if (q.Kind == "end")
            _ = ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W201TwinWant then KCD2MP_W201TwinWant({q.Id}, \"\", 0) end"));
    }

    private void Wo201ScenesOnEngineLine(string line)
    {
        long now = Environment.TickCount64;
        if (line.StartsWith("OnChatWithFocus: Dude->", StringComparison.Ordinal)) { Volatile.Write(ref _w201OwnPressMs, now); return; }
        if (!W137Joiner) return;
        int id = Wo201Rules.ClosedDialogId(line);
        if (id < 0) id = Wo201Rules.CancelledRequestId(line);
        if (id >= 0) { _ = ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W201TwinWant then KCD2MP_W201TwinWant({id}, \"\", 0) end")); return; }
        // B2: this player's scene into the engine's waiting queue (his talk or request within 3 s before it)
        if (Wo201Rules.WaitingScene(line) is { } scene)
        {
            if (scene.Length == 0 || now - Volatile.Read(ref _w201DudeAskMs) > 3_000) return;
            _w201Queued[scene] = now;
            Interlocked.Increment(ref _w201QueueSeen);
            Console.WriteLine($"WO201-QUEUE waiting scene='{scene}' -- this player's scene waits behind another (watched: it never starts more than {Wo201Rules.StaleQueueMs / 1000} s late)");
            return;
        }
        if (Wo201Rules.EndedScene(line) is { } ended && ended.Length > 0)
        {
            if (_w201Queued.TryRemove(ended, out long at) && now - at <= Wo201Rules.StaleQueueMs)
                Console.WriteLine(FormattableString.Invariant($"WO201-QUEUE done scene='{ended}' after {(now - at) / 1000.0:F1} s (in time)"));
            return;
        }
        if (Wo201Rules.TryDudeDialogueStart(line, out string path, out int did))
        {
            foreach (var kv in _w201Queued)
            {
                if (!Wo201Rules.DialogueOfScene(path, kv.Key)) continue;
                _w201Queued.TryRemove(kv.Key, out _);
                long waited = now - kv.Value;
                if (waited <= Wo201Rules.StaleQueueMs) { Console.WriteLine(FormattableString.Invariant($"WO201-QUEUE started scene='{kv.Key}' dialogue={did} after {waited / 1000.0:F1} s (in time)")); break; }
                Interlocked.Increment(ref _w201QueueStale);
                if (!W201("scene_queue_stale"))
                {
                    Console.WriteLine(FormattableString.Invariant($"WO201-QUEUE stale scene='{kv.Key}' dialogue={did} waited {waited / 1000.0:F1} s -- left to run (mp_scene_queue_stale off)"));
                    break;
                }
                Interlocked.Increment(ref _w201QueueCancelled);
                Console.WriteLine(FormattableString.Invariant(
                    $"WO201-QUEUE stale scene='{kv.Key}' dialogue={did} waited {waited / 1000.0:F1} s in the engine's queue -- never started late: ended the game's own way (InterruptDialogs), the player told"));
                _ = ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W201StaleTalk then KCD2MP_W201StaleTalk({did}, {waited / 1000.0:F1}) end"));
                break;
            }
        }
    }

    /// <summary>B2: a waiting scene nobody ended is forgotten after 15 minutes (a load ends every scene).</summary>
    private void Wo201ScenesTick(long now)
    {
        foreach (var kv in _w201Queued)
            if (now - kv.Value > 900_000) _w201Queued.TryRemove(kv.Key, out _);
    }

    private void Wo201ScenesReset() => _w201Queued.Clear();

    private string Wo201ScenesStats() => FormattableString.Invariant(
        $"twin_wants={Interlocked.Read(ref _w201TwinWants)} queue_seen={Interlocked.Read(ref _w201QueueSeen)} queue_stale={Interlocked.Read(ref _w201QueueStale)} queue_cancelled={Interlocked.Read(ref _w201QueueCancelled)}");
}
