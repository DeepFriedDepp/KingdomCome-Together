// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>WO-143: one 0xA9 frame the DLL sent (native wo143.cpp), by kind.</summary>
public sealed record Wo143DllFrame(byte Kind, List<ExtraRow> Rows, byte Result = 0, int RequestId = -1)
{
    public const byte KindHands = 1, KindGaits = 2, KindOneShot = 3, KindLooks = 4, KindNeedItem = 5, KindShotDone = 6;

    static bool Text(byte[] b, ref int o, int max, bool allowEmpty, out string s)
    {
        s = "";
        if (o >= b.Length) return false;
        int n = b[o++];
        if (n > max || o + n > b.Length || (!allowEmpty && n == 0)) return false;
        for (int i = 0; i < n; i++) if (b[o + i] < 0x20 || b[o + i] > 0x7E) return false;
        s = Encoding.ASCII.GetString(b, o, n); o += n;
        return true;
    }

    /// <summary>
    /// [kind 1][count]{[nameLen][name][L:16][R:16]} | [kind 2][count]{[nameLen][name][mask:2]} |
    /// [kind 3][nameLen][name][fragLen][frag][tagsLen][tags][alignGuid:8][flags] |
    /// [kind 4][count]{[nameLen][name][targetKind][targetLen][target]} | [kind 5][nameLen][name][class:16] |
    /// [kind 6][nameLen][name][result][requestId:4] -- exact consumption.
    /// </summary>
    public static bool TryParse(byte[] b, out Wo143DllFrame f)
    {
        f = new Wo143DllFrame(0, new());
        if (b.Length < 2) return false;
        byte kind = b[0];
        var rows = new List<ExtraRow>();
        int o = 1;
        switch (kind)
        {
            case KindHands:
            case KindGaits:
            case KindLooks:
            {
                int count = b[o++];
                if (count < 1 || count > Protocol.ExtraMaxRows) return false;
                for (int i = 0; i < count; i++)
                {
                    if (!Text(b, ref o, 63, false, out var name)) return false;
                    if (kind == KindHands)
                    {
                        if (o + 32 > b.Length) return false;
                        rows.Add(new ExtraRow { Name = name, Left = b.AsSpan(o, 16).ToArray(), Right = b.AsSpan(o + 16, 16).ToArray() });
                        o += 32;
                    }
                    else if (kind == KindGaits)
                    {
                        if (o + 2 > b.Length) return false;
                        rows.Add(new ExtraRow { Name = name, Gaits = (ushort)(b[o] | (b[o + 1] << 8)) });
                        o += 2;
                    }
                    else
                    {
                        if (o >= b.Length) return false;
                        byte tk = b[o++];
                        if (tk > Protocol.LookNpc || !Text(b, ref o, 63, true, out var target)) return false;
                        rows.Add(new ExtraRow { Name = name, TargetKind = tk, Target = target });
                    }
                }
                break;
            }
            case KindOneShot:
            {
                if (!Text(b, ref o, 63, false, out var name) || !Text(b, ref o, Protocol.ExtraFragMax, false, out var frag) ||
                    !Text(b, ref o, Protocol.ExtraTagsMax, true, out var tags) || o + 9 > b.Length) return false;
                ulong g = 0; for (int k = 7; k >= 0; k--) g = (g << 8) | b[o + k];
                rows.Add(new ExtraRow { Name = name, Fragment = frag, Tags = tags, AlignGuid = g, Flags = b[o + 8] });
                o += 9;
                break;
            }
            case KindNeedItem:
            {
                if (!Text(b, ref o, 63, false, out var name) || o + 16 > b.Length) return false;
                rows.Add(new ExtraRow { Name = name, Left = b.AsSpan(o, 16).ToArray() });
                o += 16;
                break;
            }
            case KindShotDone:
            {
                if (!Text(b, ref o, 63, false, out var name) || o + 5 > b.Length) return false;
                byte res = b[o];
                int id = BitConverter.ToInt32(b, o + 1);
                o += 5;
                if (o != b.Length) return false;
                f = new Wo143DllFrame(kind, new() { new ExtraRow { Name = name } }, res, id);
                return true;
            }
            default: return false;
        }
        if (o != b.Length) return false;
        f = new Wo143DllFrame(kind, rows);
        return true;
    }
}

/// <summary>WO-143: the pipe bodies of native wo143.h (0x28 ops).</summary>
public static class Wo143Codec
{
    public static byte[] Hands(string name, byte[] left, byte[] right)
    {
        if (left.Length != 16 || right.Length != 16) throw new ArgumentException("class ids are 16 bytes");
        var n = Wo141Codec.Name(name);
        var p = new byte[n.Length + 32];
        n.CopyTo(p, 0); left.CopyTo(p, n.Length); right.CopyTo(p, n.Length + 16);
        return p;
    }

    public static byte[] Gaits(string name, ushort mask)
    {
        var n = Wo141Codec.Name(name);
        var p = new byte[n.Length + 2];
        n.CopyTo(p, 0); p[n.Length] = (byte)(mask & 0xFF); p[n.Length + 1] = (byte)(mask >> 8);
        return p;
    }

    public static byte[] OneShot(string name, string fragment, string tags, ulong alignGuid, byte flags)
    {
        var n = Wo141Codec.Name(name);
        var f = Encoding.ASCII.GetBytes(fragment ?? ""); var t = Encoding.ASCII.GetBytes(tags ?? "");
        if (f.Length is < 1 or > Protocol.ExtraFragMax || t.Length > Protocol.ExtraTagsMax) throw new ArgumentException($"one-shot '{fragment}' / '{tags}'");
        using var ms = new MemoryStream();
        ms.Write(n);
        ms.WriteByte((byte)f.Length); ms.Write(f);
        ms.WriteByte((byte)t.Length); ms.Write(t);
        for (int i = 0; i < 8; i++) ms.WriteByte((byte)(alignGuid >> (8 * i)));
        ms.WriteByte(flags);
        return ms.ToArray();
    }

    /// <summary>A 0xA9 frame (tests; the DLL writes these).</summary>
    public static byte[] Frame(byte kind, IReadOnlyList<ExtraRow> rows)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(kind);
        void Name(string s) { var b = Encoding.ASCII.GetBytes(s); ms.WriteByte((byte)b.Length); ms.Write(b); }
        switch (kind)
        {
            case Wo143DllFrame.KindHands:
            case Wo143DllFrame.KindGaits:
            case Wo143DllFrame.KindLooks:
                ms.WriteByte((byte)rows.Count);
                foreach (var r in rows)
                {
                    Name(r.Name);
                    if (kind == Wo143DllFrame.KindHands) { ms.Write(r.Left); ms.Write(r.Right); }
                    else if (kind == Wo143DllFrame.KindGaits) { ms.WriteByte((byte)(r.Gaits & 0xFF)); ms.WriteByte((byte)(r.Gaits >> 8)); }
                    else { ms.WriteByte(r.TargetKind); Name(r.Target); }
                }
                break;
            case Wo143DllFrame.KindOneShot:
                Name(rows[0].Name); Name(rows[0].Fragment); Name(rows[0].Tags);
                for (int i = 0; i < 8; i++) ms.WriteByte((byte)(rows[0].AlignGuid >> (8 * i)));
                ms.WriteByte(rows[0].Flags);
                break;
            case Wo143DllFrame.KindNeedItem:
                Name(rows[0].Name); ms.Write(rows[0].Left);
                break;
        }
        return ms.ToArray();
    }
}

/// <summary>
/// WO-143: the engine-free rules of activities part 2 (docs/WO-143-findings.md). GameBridge feeds them its
/// live flags; Wo143Tests pins them.
/// </summary>
public static class Wo143Rules
{
    // the DLL's Config bits (native wo143.cpp)
    public const byte BitHands = 1, BitGaits = 2, BitOneShots = 4, BitLooks = 8, BitAvatarShots = 16;
    // the DLL's armed reply bits
    public const byte ArmedHands = 1, ArmedGaits = 2, ArmedOneShots = 4, ArmedHook = 8, ArmedLooks = 16, ArmedTick = 32;

    /// <summary>The five settings (mp_hand_items, mp_activity_gaits, mp_oneshots, mp_player_minigames, mp_idles).</summary>
    public readonly record struct Settings(bool Hands, bool Gaits, bool OneShots, bool Minigames, bool Idles)
    {
        public static Settings AllOn => new(true, true, true, true, true);
    }

    /// <summary>
    /// What the DLL captures (the host: its tracked NPCs) and applies (a joiner: its copies; everyone: the avatars'
    /// minigames). Nothing without WO-141's activities: the hands ride its apply, and the rest is "what else the
    /// activity is". Looks are applied in Lua (the actor's own forced look), so they are never an apply bit.
    /// </summary>
    public static (byte Capture, byte Apply) Masks(Settings s, bool activitiesActive, bool host, bool joiner)
    {
        if (!activitiesActive) return (0, 0);
        byte cap = 0, ap = 0;
        if (host)
        {
            if (s.Hands) cap |= BitHands;
            if (s.Gaits) cap |= BitGaits;
            if (s.OneShots) cap |= BitOneShots;
            if (s.Idles) cap |= BitLooks;
        }
        if (joiner)
        {
            if (s.Hands) ap |= BitHands;
            if (s.Gaits) ap |= BitGaits;
            if (s.OneShots) ap |= BitOneShots;
        }
        if (s.Minigames && (host || joiner)) ap |= BitAvatarShots;
        return (cap, ap);
    }

    /// <summary>
    /// What the avatar plays for a player's minigame (C_MinigameElement +0x28, WO-141's row), from the game's own
    /// fragment table (male class, Tables.pak animation/anim_fragment.xml): an entry (aligned where the game's own is:
    /// the avatar is put where the game puts the player, at the same object), the loop, an out. A seated kind (reading,
    /// dice) keeps WO-141's seat and takes the seated tags; dice's entry is left out (the seat already places him).
    /// Stone throwing is refused by the game on an avatar (its fragment needs the thrower's stance: r1/H1) and pickpocketing,
    /// archery, distract and forge building have no body loop of their own: null, the avatar stands (as before).
    /// </summary>
    public sealed record MinigameShow(byte Type, string Name, string? Entry, string EntryTags, bool EntryAligned,
                                      string Loop, string LoopTags, bool LoopAligned, string? Out, string OutTags);

    public static MinigameShow? ShowFor(byte type, bool seated) => type switch
    {
        1 => new(1, "grindstone", "SharpeningMinigameIn", "sword", true, "SharpeningMinigame", "sword", false, "SharpeningMinigameOut", "sword"),
        2 => new(2, "reading", "ReadingBookIn", seated ? "sittingNoTable+book" : "book", false, "ReadingBook", seated ? "sittingNoTable+book" : "book", false,
                 "ReadingBookOut", seated ? "sittingNoTable+book" : "book"),
        3 => new(3, "alchemy", null, "", false, "AlchemyIdle", "", true, null, ""),
        4 => new(4, "herb gathering", null, "", false, "PickingHerbs", "", false, null, ""),
        5 => new(5, "lockpicking", "LockpickingIn", "", true, "LockpickingIdle", "", false, "LockpickingIdleOut", ""),
        6 => new(6, "grave digging", null, "", false, "Digging", "", false, null, ""),
        7 => new(7, "dice", null, "", false, "DiceGameIdle", "sitting", false, null, ""),
        12 => new(12, "smithing", "BlacksmithingToAnvil", "sword+forgeBag", true, "BlacksmithingAnvilIdle", "sword", false, null, ""),
        _ => null,
    };

    public static MinigameShow? ShowFor(ActivityState a) =>
        a.Minigame == ActivityState.NoMinigame ? null : ShowFor(a.Minigame, a.Stance == ActivityState.Sitting);

    /// <summary>
    /// WO-153 1: herb gathering, the one minigame whose loop ended both of the joiner's 0.43.0 crashes: the avatar's
    /// `PickingHerbs` loop stopped while the local player's own herb minigame was a second old. Nothing of ours ties that
    /// loop to a plant (no object, no alignment, no tags -- the field's `MP-W143 ... plays PickingHerbs where he stands`),
    /// so the engine's own fragment is what breaks. Until an engine-side look says why, the avatar stands while its player
    /// gathers herbs (`mp_avatar_herbs`, off by default); every other minigame is unchanged.
    /// </summary>
    public const byte HerbMinigame = 4;

    /// <summary>What the avatar plays for this player row, with the herb switch applied (null: the avatar stands).</summary>
    public static MinigameShow? AvatarShow(ActivityState a, bool herbsOn)
    {
        var show = ShowFor(a);
        return show is { Type: HerbMinigame } && !herbsOn ? null : show;
    }

    /// <summary>
    /// WO-153 1: only a station's minigame (grindstone, alchemy, lockpicking, smithing) may ever be aligned at the object
    /// the player stands at, and only under `mp_minigame_align`. A pick-up (herbs) or a dig is never aligned to, attached
    /// to or referenced against a world object: it plays where the avatar stands.
    /// </summary>
    public static bool MayAlignAt(byte type) => type is 1 or 3 or 5 or 12;

    /// <summary>The first request of a minigame on the avatar: (fragment, tags, align at the object, the aligned-entry guard, the phase it starts).</summary>
    public static (string Fragment, string Tags, bool Aligned, int Phase) FirstStep(MinigameShow m, ulong obj)
    {
        if (!MayAlignAt(m.Type)) obj = 0;
        if (m.Entry is { } e && (!m.EntryAligned || obj != 0)) return (e, m.EntryTags, m.EntryAligned && obj != 0, PhaseEntry);
        if (m.LoopAligned && obj != 0) return (m.Loop, m.LoopTags, true, PhaseLoop);
        return (m.Loop, m.LoopTags, false, PhaseLoop);
    }

    public const int PhaseNone = 0, PhaseEntry = 1, PhaseLoop = 2, PhaseOut = 3;
    public const byte ResultFailed = 0, ResultDone = 2, ResultInterrupted = 3, ResultMovedAway = 0xFD, ResultGaveUp = 0xFE;

    /// <summary>
    /// What follows a finished request on the avatar: after the entry, the loop in place (the game has put the body at
    /// the object); a loop the game finished by itself (after at least a second), the loop again; interrupted (a newer
    /// request took over): nothing; refused, carried away or given up while aligned: the loop in place, once, with the
    /// hold released; refused in place: stand (nothing more).
    /// </summary>
    public static (string Action, int Phase) AfterStep(int phase, byte result, bool wasAligned, long ranMs) => (phase, result) switch
    {
        (_, ResultInterrupted) => ("none", phase),
        (PhaseEntry, ResultDone) => ("loop", PhaseLoop),
        (PhaseLoop, ResultDone) when ranMs >= 1000 => ("loop", PhaseLoop),
        (PhaseLoop, ResultDone) => ("stand", PhaseNone),
        (PhaseOut, _) => ("release", PhaseNone),
        (PhaseEntry or PhaseLoop, _) when wasAligned => ("loop-in-place", PhaseLoop),
        _ => ("stand", PhaseNone),
    };

    public static string MinigameName(byte type) => type switch
    {
        1 => "grindstone", 2 => "reading", 3 => "alchemy", 4 => "herb gathering", 5 => "lockpicking", 6 => "grave digging", 7 => "dice",
        8 => "pickpocketing", 9 => "stone throwing", 10 => "battle archery", 11 => "distract", 12 => "smithing", 13 => "forge building",
        ActivityState.NoMinigame => "none", _ => $"minigame-{type}",
    };

    /// <summary>The loop the avatar ends up in for this player row (null: none) -- the minigame's own loop and tags.</summary>
    public static (string Fragment, string Tags)? AvatarLoop(ActivityState a) => ShowFor(a) is { } m ? (m.Loop, m.LoopTags) : null;

    /// <summary>
    /// Who a copy looks at on this machine: the host's own player is the host's avatar here, a player's avatar is
    /// this machine's own player when it is this one, else that player's avatar here; an NPC is its copy. Null =
    /// nobody (the forced look is cleared).
    /// </summary>
    public static string? LookTargetHere(byte kind, string target, byte hostGhostId, byte myGhostId) => kind switch
    {
        Protocol.LookHostPlayer => Wo141Rules.AvatarName(hostGhostId),
        Protocol.LookPeer when byte.TryParse(target, out byte id) => id == myGhostId ? "player" : Wo141Rules.AvatarName(id),
        Protocol.LookNpc when target.Length > 0 => target,
        _ => null,
    };

    /// <summary>A copy the host's NPC is fighting, talking with or has down plays no one-shot and forces no look (its own brain or the stream has it).</summary>
    public static bool Quiet(string? blockedWhy) => blockedWhy is not null;

    /// <summary>
    /// WO-153 5: a temporary tool that no row asks for any more is kept this long before it is taken back, and a row that
    /// asks for it again in that time cancels the take. The host's hand rows flap (one villager changed hands 235 times in
    /// 1,055 s), and the 0.43.0 release fired 2.5 s after each empty row, so tools were given and taken again and again.
    /// </summary>
    public const long TempHoldMs = 90_000;

    /// <summary>The pending takes that are due at <paramref name="nowMs"/>.</summary>
    public static List<(string Name, string Cls)> DueReleases(IEnumerable<KeyValuePair<(string Name, string Cls), long>> pending, long nowMs) =>
        pending.Where(kv => kv.Value <= nowMs).Select(kv => kv.Key).ToList();

    /// <summary>Temporary tools: a copy's temp item of a class goes once its hands no longer want that class.</summary>
    public static IEnumerable<string> TempsToRelease(IReadOnlyCollection<string> temps, byte[] left, byte[] right)
    {
        string l = ExtraRow.ClassText(left), r = ExtraRow.ClassText(right);
        return temps.Where(c => c != l && c != r).ToList();
    }
}
