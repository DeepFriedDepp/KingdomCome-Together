using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-144 1.1: who the partners are. The relay's live connections are the only
/// source: a Name packet (the relay sends one for every client that becomes
/// ready, and every existing one to a client that just connected) adds an id,
/// its Disconnect removes it, and a lost relay clears everything. Nothing a
/// partner sends -- a position, a heartbeat, a status, a held frame replayed
/// after a load -- ever adds one back: a removed id stays removed until the
/// relay names it again (a new connection reusing the id).
///
/// The field bug this replaces: the agent's name table was never cleared at a
/// Disconnect, and six features used it as the partner set -- so a joiner's
/// leftover Steam connection (his game or agent restarted; the old one times
/// out ~1-20 s later) stayed a partner for the whole session: `peers=2`, every
/// vote "asked of 2 joiner(s)" and ending in a timeout.
/// </summary>
public sealed class PeerSet
{
    private readonly object _lock = new();
    private readonly Dictionary<byte, string> _live = new();
    private readonly HashSet<byte> _removed = new();

    /// <summary>The relay named this id: a connected partner (again, if the id was reused).</summary>
    public bool Connected(byte id, string name)
    {
        lock (_lock)
        {
            _removed.Remove(id);
            bool fresh = !_live.ContainsKey(id);
            _live[id] = name;
            return fresh;
        }
    }

    /// <summary>The relay's Disconnect: gone, and stays gone. Returns the name it had (null if it was never named).</summary>
    public string? Disconnected(byte id)
    {
        lock (_lock)
        {
            _removed.Add(id);
            return _live.Remove(id, out var n) ? n : null;
        }
    }

    public void Clear() { lock (_lock) { _live.Clear(); _removed.Clear(); } }

    public bool IsLive(byte id) { lock (_lock) return _live.ContainsKey(id); }

    /// <summary>Removed by the relay and not named since: anything from it is stale and must not resurrect it.</summary>
    public bool IsRemoved(byte id) { lock (_lock) return _removed.Contains(id); }

    public string? NameOf(byte id) { lock (_lock) return _live.TryGetValue(id, out var n) ? n : null; }

    /// <summary>Every connected partner but this machine, in id order.</summary>
    public List<byte> Partners(byte self)
    {
        lock (_lock) return _live.Keys.Where(k => k != self).OrderBy(k => k).ToList();
    }

    /// <summary>Other live ids that carry the same player name (the relay replaces them; this is the agent's view).</summary>
    public List<byte> SameName(byte id, string name)
    {
        lock (_lock) return _live.Where(kv => kv.Key != id && string.Equals(kv.Value, name, StringComparison.Ordinal)).Select(kv => kv.Key).ToList();
    }
}

public static class Wo144Rules
{
    // ---------------------------------------------------------------- 2.1 the live avatar's soul

    /// <summary>
    /// The soul keys whose last 8 bytes are this entity key (16 hex digits, as the mod writes it from
    /// the entity GUID's four 16-bit parts, little-endian). Observed: the live kcd2mp_0's key
    /// 026314f3-d093-856b-b659-fc773649cb90 for the entity parts 0x59B6 0x77FC 0x4936 0x90CB.
    /// </summary>
    public static List<Guid> SoulsWithEntityKey(IEnumerable<Guid> keys, string entityKey16)
    {
        string want = (entityKey16 ?? "").Trim().ToLowerInvariant();
        if (want.Length != 16) return [];
        return keys.Where(k => k.ToString("N")[16..] == want).ToList();
    }

    // ---------------------------------------------------------------- 4.1 checkpoint corrections

    /// <summary>
    /// A checkpoint correction fires the host's last port on a State this copy has wrong. A port whose
    /// result depends on the State's history can land elsewhere (the field: SetAroundBoulder took this
    /// copy 0 -> 3, the host had 15), and the next checkpoint fired it again, with its consequences,
    /// every 30 s. A (State, port, host value) that missed once is skipped from then on.
    /// </summary>
    public sealed class CorrectionLedger
    {
        private readonly object _lock = new();
        private readonly Dictionary<(string, string, int), bool> _missed = new();   // value: told

        /// <summary>Records a miss; true the first time (the one line).</summary>
        public bool Missed(string path, string port, int hostVal, int got)
        {
            if (got == hostVal) return false;
            lock (_lock) return _missed.TryAdd((path, port, hostVal), false);
        }

        /// <summary>True when this correction missed before; first = the first skip (its one line).</summary>
        public bool Skips(string path, string port, int hostVal, out bool first)
        {
            first = false;
            lock (_lock)
            {
                if (!_missed.TryGetValue((path, port, hostVal), out bool told)) return false;
                if (!told) { first = true; _missed[(path, port, hostVal)] = true; }
                return true;
            }
        }
    }

    // ---------------------------------------------------------------- 2.4 lights

    private static readonly HashSet<Guid> Lights =
    [
        Guid.Parse("4cea28a0-0814-405a-bf24-4fd711f7eb63"),   // torch_weapon (the player's torch)
        Guid.Parse("cfec1446-ce8d-4c9c-aa9a-56fc8b10bc0e"),   // the other torch weapon
        Guid.Parse("e95bb3ae-38ce-41fd-948a-e471673b47e5"),   // torch_tool
        Guid.Parse("bdf14d9c-7264-434c-96af-748ff2779c1b"),   // lamp_tool
        Guid.Parse("d1a6946e-4184-42b7-bc15-1172e0c7de93"),   // lamp_toolFancy
    ];

    /// <summary>A light (a torch or a lamp): the torch sync's, never an outfit piece (the NPC's night behaviour draws any it owns).</summary>
    public static bool IsLight(Guid c) => Lights.Contains(c);

    // ---------------------------------------------------------------- 2.1 clothes

    /// <summary>
    /// WO-144 5: a non-hosting agent in its own world claims the session host only when no host has
    /// spoken 10 s after it connected (a host announces its session mode to a new connection within a
    /// second or two). The field's joiner claimed after each join.
    /// </summary>
    public static bool ClaimGraceOver(DateTime connectedUtc, DateTime nowUtc) =>
        connectedUtc != DateTime.MinValue && (nowUtc - connectedUtc).TotalSeconds >= 10;

    /// <summary>How often an avatar's real outfit is read back between its player's outfit packets.</summary>
    public const int OutfitCheckMs = 10_000;

    /// <summary>A refused piece is tried again after 20 s, 60 s, 3 min, then every 10 min.</summary>
    public static long RefusalBackoffMs(int marks) => marks switch { <= 1 => 20_000, 2 => 60_000, 3 => 180_000, _ => 600_000 };

    /// <summary>
    /// The layer a piece goes on in (under-layers first): the game refuses a piece whose
    /// under-layer is empty ("Can't equip armor 'ArmPlate01_m01_C2'. It requires
    /// 'body_cloth_padded' slot to be filled"). By the item's name, the game's own families.
    /// </summary>
    public static int EquipLayer(string itemName)
    {
        string n = itemName ?? "";
        static bool P(string s, params string[] pre) => pre.Any(p => s.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        if (P(n, "Shirt", "Tunic", "Hose", "Braies", "Undershirt", "LegsCloth", "F_Smock", "F_SimpleDress", "Chemise", "Pants")) return 0;
        if (P(n, "Gambeson", "Caftan", "Pourpoint", "Aketon", "LegsPadded", "CoifSmall", "CoifLarge", "CoifPadded", "CollarPadded", "Coif0", "CoifCap")) return 1;
        if (P(n, "Mail", "Chain", "CoifMail", "CollarMail", "LegsMail", "Habergeon", "Hauberk")) return 2;
        if (P(n, "Cuirass", "Brigandine", "ArmPlate", "ArmBrigandine", "ArmMail", "LegsPlate", "LegsBrigandine", "Plate", "Gauntlet", "Bascinet", "KettleHat", "SkullCap", "Helmet", "Sallet", "Barbute")) return 3;
        if (P(n, "Coat", "Waffenrock", "Habit", "Tabard", "Surcoat", "Cloak", "Jupon", "Hood", "Cap", "Hat", "Bonnet", "F_Hood", "F_Veil", "F_Hat")) return 4;
        if (P(n, "Boots", "Shoes", "Gloves", "Belt", "belt", "Ring", "Necklace", "Spurs")) return 5;
        return 3;
    }

    /// <summary>"Can't equip armor 'X'. It requires 'Y' slot to be filled." -> (X, the game's reason).</summary>
    public static (string Item, string Reason)? ParseCantEquip(string line)
    {
        var m = System.Text.RegularExpressions.Regex.Match(line ?? "", @"Can't equip armor '([^']+)'\.\s*(.+?)\s*$");
        return m.Success ? (m.Groups[1].Value, m.Groups[2].Value) : null;
    }

    /// <summary>
    /// A sleep or wait vote counts a partner only if it is connected and in the
    /// host's world. The joiner says where it is every second (WO-114's leash
    /// state); a fresh state that says loading, in its own world (WO-140), or
    /// not in the host's world keeps it out of the vote. No state yet, or a stale
    /// one, keeps it in (asked, as before): a one-sided time skip is worse than
    /// a vote that times out.
    /// </summary>
    public static bool CountsInVote(bool live, bool stateFresh, ushort flags)
    {
        if (!live) return false;
        if (!stateFresh) return true;
        if ((flags & Protocol.LeashFlagSeparate) != 0) return false;
        if ((flags & Protocol.LeashFlagLoading) != 0) return false;
        return (flags & Protocol.LeashFlagInWorld) != 0;
    }

    /// <summary>Why a partner is out of a vote, for the log line (null = it counts).</summary>
    public static string? VoteExclusion(bool live, bool stateFresh, ushort flags)
    {
        if (!live) return "not connected";
        if (!stateFresh) return null;
        if ((flags & Protocol.LeashFlagSeparate) != 0) return "in its own world";
        if ((flags & Protocol.LeashFlagLoading) != 0) return "loading";
        if ((flags & Protocol.LeashFlagInWorld) == 0) return "not in this world";
        return null;
    }
}
