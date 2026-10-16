// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text.RegularExpressions;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-195 -- the pure rules of three field fixes (Wo195Tests pins them; Wo195ReplayTests runs the 0.60.5 joiner's bundle through them;
/// docs/WO-195-findings.md has the evidence).
///   1  mp_dead_release: a copy that dies is released from every fight hold at once (the engagement, the guard hold, the opponent wish
///      and its pin, the writer's combat mode) and never engaged, held or wished again; the game's own death takes the body.
///   2  mp_stealth_witness: the joiner's crime report carries his stealth (crouch, light, the game's own visibility / noise /
///      conspicuousness derived stats, his stealth skill); the host's witness rule uses it, and an NPC asleep (IsSleeping, or lying on
///      its bed in the host's own activity read) never witnesses. The rule itself is the mod's Lua (KCD2MP_W195*).
///   3  mp_body_loot_host: the key the game's loot screen puts into a body as it opens (the NPC's home key -- no inventory holds it,
///      so the host's body "never held it" and WO-136 took it back off the joiner) is a take the host's world grants once.
/// </summary>
public static class Wo195Rules
{
    // ================================================================ switches (the mod's w195_cfg; all ship on)

    public const bool DefaultDeadRelease = true, DefaultStealthWitness = true, DefaultBodyLootHost = true;

    // ================================================================ 1: dead copies

    /// <summary>One release per copy per this long (every [npcdeath] path of one death arrives within ~0.2 s).</summary>
    public const long ReleaseDedupeMs = 5_000;
    /// <summary>The check after a release: the body on the ground (combat mode off) and this player's combat state.</summary>
    public const int FallCheckMs = 400, PlayerCheckMs = 2_000;

    /// <summary>
    /// The 1 s check (op 57): a copy that reads dead while its physics is still a living entity stands upright -- the mod's ApplyDeath gives
    /// it no death reaction (live, 0.60.9 and this build: frozen in the dead action's first pose, the head 1.4 m up). It gets the game's own
    /// ragdoll (Entity:RagDollize), then the body is read again after this long.
    /// </summary>
    public static bool Upright(byte dead, byte living) => dead == 1 && living == 1;
    public const int RagdollSettleMs = 400;

    public static bool ReleaseDue(bool on, long lastReleaseMs, long nowMs) => on && (lastReleaseMs <= 0 || nowMs - lastReleaseMs >= ReleaseDedupeMs);

    /// <summary>A copy marked dead is never engaged, guard-held or wished again (until its host stream says it lives: a reload).</summary>
    public static bool Blocks(bool on, bool markedDead) => on && markedDead;

    /// <summary>The death apply's paths, as ApplyRemoteNpcDeathAsync's <c>via</c> names them (and "local-fatal" for this player's own blow).</summary>
    public static string PathTag(string via) =>
        via.StartsWith("owner-death", StringComparison.Ordinal) ? "owner-death"
        : via.Contains("FATAL", StringComparison.Ordinal) ? "0x31-fatal"
        : via.Contains("resync", StringComparison.Ordinal) ? "0x27-resync"
        : via.Contains("0x27", StringComparison.Ordinal) ? "0x27-transition"
        : via == "local-fatal" ? "local-fatal" : "other";

    /// <summary>The WO195-RELEASE answer's held mask (native wo195_rules.h kHeld*) as text.</summary>
    public const byte HeldEngage = 0x01, HeldGuard = 0x02, HeldWish = 0x04, HeldPin = 0x08, HeldCombat = 0x10;

    public static string HeldText(byte m)
    {
        var p = new List<string>();
        if ((m & HeldEngage) != 0) p.Add("engage");
        if ((m & HeldGuard) != 0) p.Add("guard");
        if ((m & HeldWish) != 0) p.Add("wish");
        if ((m & HeldPin) != 0) p.Add("pin");
        if ((m & HeldCombat) != 0) p.Add("combat");
        return p.Count == 0 ? "none" : string.Join('+', p);
    }

    public static string Tri(byte v, string on, string off) => v == 255 ? "?" : v != 0 ? on : off;

    // ---------------------------------------------------------------- the replay (Wo195ReplayTests)

    private static readonly Regex DeathLine = new(@"\[npcdeath\] in: (?:'(?<n>[A-Za-z0-9_\-\.]+)' via (?<via>.+?) from ghost \d+ -> ApplyDeath applied|0x27 from ghost \d+ says '(?<n2>[A-Za-z0-9_\-\.]+)' is dead .* -- witnessed transition)", RegexOptions.Compiled);
    private static readonly Regex EngageOn = new(@"MP-W132 engage on (?<n>[A-Za-z0-9_\-\.]+) \(eid 0x(?<e>[0-9A-F]+)", RegexOptions.Compiled);
    private static readonly Regex EngageOff = new(@"MP-W132 engage off (?<n>[A-Za-z0-9_\-\.]+):", RegexOptions.Compiled);

    /// <summary>One death of a copy in an agent log, and whether this player's engagement with it was still on when it died.</summary>
    public readonly record struct ReplayDeath(string Npc, string Path, bool WasEngaged, int Line);

    /// <summary>
    /// Every death apply of a copy in a joiner's agent.log, in order, first path only (one release per death); WasEngaged = an engage on
    /// with no engage off before the death (0.60.5 never logged the death's own release: Wo131BeforeDeathAsync removed it quietly).
    /// </summary>
    public static List<ReplayDeath> ReplayDeaths(IEnumerable<string> lines)
    {
        var engaged = new HashSet<string>(StringComparer.Ordinal);
        var lastDeath = new Dictionary<string, int>(StringComparer.Ordinal);
        var outList = new List<ReplayDeath>();
        int i = 0;
        foreach (var l in lines)
        {
            ++i;
            var m = EngageOn.Match(l);
            if (m.Success) { engaged.Add(m.Groups["n"].Value); continue; }
            m = EngageOff.Match(l);
            if (m.Success) { engaged.Remove(m.Groups["n"].Value); continue; }
            m = DeathLine.Match(l);
            if (!m.Success) continue;
            string n = m.Groups["n"].Success ? m.Groups["n"].Value : m.Groups["n2"].Value;
            string via = m.Groups["via"].Success ? m.Groups["via"].Value : "0x27 dead transition";
            if (lastDeath.TryGetValue(n, out int at) && i - at < 400) continue;   // the same death's other paths
            lastDeath[n] = i;
            outList.Add(new ReplayDeath(n, PathTag(via), engaged.Remove(n), i));
        }
        return outList;
    }

    // ================================================================ 2: the joiner's stealth

    /// <summary>
    /// The joiner's stealth at a crime, as his mod reads it (the w139_crime event's 8th field): c crouch 0|1, l a light in hand 0|1,
    /// v / n / s the game's own visibility / noise / conspicuousness derived stats (soul:GetDerivedStat "vis" "noi" "con"; -99 = unread),
    /// k the stealth skill level (-1 = unread). Text: "c=1,l=0,v=0.80,n=1.10,s=0.50,k=7".
    /// </summary>
    public readonly record struct Stealth(bool Crouch, bool Light, float Vis, float Noise, float Consp, int Skill)
    {
        public static readonly Stealth Unknown = new(false, false, -99f, -99f, -99f, -1);

        public string ToText() => FormattableString.Invariant(
            $"c={(Crouch ? 1 : 0)},l={(Light ? 1 : 0)},v={Vis:F2},n={Noise:F2},s={Consp:F2},k={Skill}");
    }

    private static readonly Regex StealthText = new(@"^c=[01],l=[01],v=-?\d{1,3}(\.\d{1,3})?,n=-?\d{1,3}(\.\d{1,3})?,s=-?\d{1,3}(\.\d{1,3})?,k=-?\d{1,2}$", RegexOptions.Compiled);

    /// <summary>The 8th field, checked whole (it is spliced into a Lua literal on the host).</summary>
    public static bool TryParseStealth(string? text, out Stealth s)
    {
        s = Stealth.Unknown;
        if (string.IsNullOrEmpty(text) || text.Length > 64 || !StealthText.IsMatch(text)) return false;
        var kv = text.Split(',').Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);
        float F(string k) => float.Parse(kv[k], NumberStyles.Float, CultureInfo.InvariantCulture);
        s = new Stealth(kv["c"] == "1", kv["l"] == "1", F("v"), F("n"), F("s"), int.Parse(kv["k"], NumberStyles.Integer, CultureInfo.InvariantCulture));
        return true;
    }

    /// <summary>
    /// 2a: the host's own activity read of its NPC (WO-141's capture): lying on an object is lying on its bed (the game's sleeping stance
    /// is the Lying stance on a bed's smart object; a lying body with no object is a corpse or a knockout, not a sleeper).
    /// </summary>
    public static bool AsleepByActivity(ActivityState a)
    {
        var n = a.Normalised();
        return n.Stance == ActivityState.Lying && n.StanceObj != 0;
    }

    // ================================================================ 3: the loot screen's key

    /// <summary>A body take's text from the joiner: "body cls amt hp" or, for an item the loot screen created, "body cls amt hp screen".</summary>
    public static bool IsScreenMark(string s) => s == "screen";
}
