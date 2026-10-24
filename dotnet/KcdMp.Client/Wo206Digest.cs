// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace KcdMp.Client;

/// <summary>
/// WO-206 C: the checkpoint digest's pure rules. The algorithm is tools/digest/digest.py's, byte for byte (its analyzer
/// compares two machines' rows): fnv1a64 over UTF-8; an entry's mix is fnv1a64(key + "=" + value); its bucket is
/// fnv1a64(key) mod 256; a bucket's value is the low 32 bits of the wrapping sum of its entries' mixes; only non-zero
/// buckets are written, as {"&lt;bucket&gt;": "&lt;8 hex&gt;"}. Order-independent; one changed value changes one bucket.
/// </summary>
public static class Wo206Digest
{
    public const string Quest = "quest", Dead = "dead", Loot = "loot", Clock = "clock", Join = "join";
    public static readonly string[] Categories = [Quest, Dead, Loot, Clock, Join];

    /// <summary>Seconds between the host's checkpoints (taken only at a quiet point: no load, no join, no time skip).</summary>
    public const int CheckpointEveryS = 5;
    /// <summary>The checkpoint marker: W203 side-channel kind 10, host -> joiner, "v1|&lt;epoch&gt;|&lt;seq&gt;".</summary>
    public const byte MarkerKind = 10;
    /// <summary>The dead set: every shared body within this many metres of the host's Henry that is DEAD, on both machines. Only the dead
    /// are keys (the living flip in and out of any radius at slightly different moments on the two machines -- found on the bench), so a
    /// death on one machine only is a key missing on the other. Each machine's own wildlife (SpawnedAnimal_*) and the dialogue system's
    /// helper bodies (DialogTwin_*) are left out: they are local, never shared.</summary>
    public const float DeadRadiusM = 60f;

    public static ulong Fnv1a64(string s)
    {
        ulong h = 0xcbf29ce484222325UL;
        foreach (byte c in Encoding.UTF8.GetBytes(s)) { h ^= c; h = unchecked(h * 0x100000001b3UL); }
        return h;
    }

    public static ulong Mix(string key, string value) => Fnv1a64(key + "=" + value);
    public static int BucketOf(string key) => (int)(Fnv1a64(key) % 256);

    public static SortedDictionary<int, string> Buckets(IEnumerable<KeyValuePair<string, string>> entries)
    {
        var sums = new Dictionary<int, ulong>();
        foreach (var kv in entries)
        {
            int b = BucketOf(kv.Key);
            sums[b] = unchecked((sums.TryGetValue(b, out var s) ? s : 0UL) + Mix(kv.Key, kv.Value));
        }
        var outp = new SortedDictionary<int, string>();
        foreach (var (b, s) in sums)
            if ((s & 0xFFFFFFFFUL) != 0) outp[b] = (s & 0xFFFFFFFFUL).ToString("x8", CultureInfo.InvariantCulture);
        return outp;
    }

    /// <summary>One digest.jsonl row. <paramref name="full"/> = the whole map (written when it changed since the last row, and on
    /// an epoch's first row); null otherwise.</summary>
    public static string Row(long tMs, string machine, string cat, int epoch, int seq, IReadOnlyDictionary<string, string> map, bool full)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteNumber("t", tMs); w.WriteString("m", machine); w.WriteString("cat", cat);
            w.WriteNumber("epoch", epoch); w.WriteNumber("seq", seq); w.WriteNumber("n", map.Count);
            w.WriteStartObject("b");
            foreach (var (b, v) in Buckets(map)) w.WriteString(b.ToString(CultureInfo.InvariantCulture), v);
            w.WriteEndObject();
            if (full)
            {
                w.WriteStartObject("e");
                foreach (var kv in map.OrderBy(k => k.Key, StringComparer.Ordinal)) w.WriteString(kv.Key, kv.Value);
                w.WriteEndObject();
            }
            else w.WriteNull("e");
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>A quest node's value as the digest writes it: a clock's state byte, a State's int ("?" unreadable).</summary>
    public static string QuestValue(byte kind, int value) =>
        kind == ClockKind ? "clk:" + value.ToString(CultureInfo.InvariantCulture)
                          : "sv:" + (value == -1 ? "?" : value.ToString(CultureInfo.InvariantCulture));

    /// <summary>A quest node's digest entry, or null for a clock at rest (state 0): the quest map holds every State and the RUNNING
    /// clocks, so a timer started on one machine only is a key missing on the other (the Jaunt's shape).</summary>
    public static string? QuestEntry(byte kind, int value) => kind == ClockKind && value == 0 ? null : QuestValue(kind, value);

    /// <summary>The WO-200 gate's Kind for C_Timer / C_TimeOfDayWatch (native wo200_rules.h: None, Dialogue, DialogueStarter, Clock).</summary>
    public const byte ClockKind = 3;

    /// <summary>The world clock as the digest writes it: minutes rounded down to 15 -- two machines a frame apart agree, and the joiner's
    /// gentle clock correction (WO-200: a ratio, never a jump) stays inside one step.</summary>
    public static string ClockValue(uint worldSeconds) => ((worldSeconds / 60 / 15) * 15).ToString(CultureInfo.InvariantCulture);

    public static string MarkerText(int epoch, int seq) => FormattableString.Invariant($"v1|{epoch}|{seq}");

    /// <summary>The arm reply's path count: zero means the DLL has none (it came back) and the keyspace must be sent again. A load
    /// keeps them (the bench: re-sending after every load cost the host 7 minutes of pipe round trips).</summary>
    public static bool KeyspaceLost(uint dllPaths) => dllPaths == 0;

    public static bool TryParseMarker(string text, out int epoch, out int seq)
    {
        epoch = seq = 0;
        var p = text.Trim().Split('|');
        return p.Length >= 3 && p[0] == "v1" && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out epoch)
               && int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out seq) && seq > 0;
    }

    /// <summary>The Lua dead scan around (x, y, z): every soul's name and 1 (dead) or 0, sorted, back as the w206_dead event. Death is
    /// read the way the mod's own death observer reads it: actor:IsDead(), the soul's health at or below 0 as a fallback. (The bench:
    /// soul:IsDead() does not exist in this game's Lua -- the pcall swallowed the error and every body read alive, the dead set empty.)</summary>
    public static string DeadScanLua(float x, float y, float z, int seq) => FormattableString.Invariant(
        $"pcall(function() local p={{x={x:F2},y={y:F2},z={z:F2}}}; local t={{}}; for _,e in ipairs(System.GetEntitiesInSphere(p,{DeadRadiusM:F0}) or {{}}) do if e.soul and e.GetName and e.id~=player.id and not string.find(e:GetName(),'kcd2mp_',1,true) and not string.find(e:GetName(),'SpawnedAnimal_',1,true) then local d=0; pcall(function() if e.actor and e.actor:IsDead() then d=1 end end); if d==0 then pcall(function() if e.soul:GetState('health')<=0 then d=1 end end) end; t[#t+1]=e:GetName()..':'..d end end; table.sort(t); System.LogAlways('[KCD2-MP-EVT] v1 0 w206_dead {seq} '..table.concat(t,',')) end)");

    /// <summary>The Lua dead scan's reply ("&lt;seq&gt; name:0,name:1,..."): the seq and name -> "dead"/"alive".</summary>
    public static bool TryParseDeadScan(string arg, out int seq, out Dictionary<string, string> map)
    {
        map = new Dictionary<string, string>(StringComparer.Ordinal);
        seq = 0;
        var t = arg.Trim();
        int sp = t.IndexOf(' ');
        string head = sp < 0 ? t : t[..sp];
        if (!int.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out seq)) return false;
        if (sp < 0) return true;
        foreach (var item in t[(sp + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int c = item.LastIndexOf(':');
            if (c <= 0) continue;
            if (item[(c + 1)..] == "1" && !item.StartsWith("DialogTwin_", StringComparison.Ordinal)) map[item[..c]] = "dead";   // the dead only
        }
        return true;
    }
}
