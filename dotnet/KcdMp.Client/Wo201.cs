// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;

namespace KcdMp.Client;

/// <summary>
/// WO-201: every bug from the first 0.61.5 field session, at its root (docs/WO-201-findings.md). The pure rules; the bridge is
/// GameBridge.Wo201.cs, the game-side halves are kdcmp.lua's WO-201 section, the DLL's half native/KCDMP/wo201.cpp.
/// </summary>
public static class Wo201Rules
{
    // ---- the switches (each mechanism ships on, each with its own switch) ----
    public static readonly string[] SwitchNames =
    {
        "inv_screen_guard", "joiner_scene_noposition", "scene_queue_stale", "twins_grace", "monologue_rescue_exempt", "joiner_no_story_barks",
        "scene_toast_truth", "copy_combat_from_host", "copy_look_from_host", "walk_from_host", "figure_ranged", "clock_catchup",
        "host_load_toast", "mark_note", "both_are_henry", "figure_is_player",
        "nearest_player_triggers", "joiner_receive_only", "knockout_engine", "oneshot_tick_cap", "copy_no_idle_turn",
    };

    /// <summary>Every mechanism ships on. V.3 figure_is_player reaches a save: on only while its save-decode check has passed
    /// (Dude2DecodePassed, set from the release's own check -- docs/WO-201-findings.md). WO-204 I.5: nearest_player_triggers (rebuilt as
    /// nearest_player_v2) is on only once its time-skip soak (S1) has passed (NearestPlayerV2SoakPassed).</summary>
    public static bool DefaultOn(string name) => name switch
    {
        "figure_is_player" => Dude2DecodePassed,
        "nearest_player_triggers" => NearestPlayerV2SoakPassed,
        _ => true,
    };
    public const bool Dude2DecodePassed = false;
    public const bool NearestPlayerV2SoakPassed = true;   // WO-204 S1 (2026-10-22): 60 time skips, the override on, 0 crashes

    /// <summary>The DLL's op 77 bits byte: 1 joiner_scene_noposition, 2 walk_from_host, 4 oneshot_tick_cap, 8 copy_no_idle_turn (the B9
    /// follow-up: a standing copy never turns in place by itself).</summary>
    public static byte ConfigBits(Func<string, bool> on) => (byte)((on("joiner_scene_noposition") ? 1 : 0) | (on("walk_from_host") ? 2 : 0)
        | (on("oneshot_tick_cap") ? 4 : 0) | (on("copy_no_idle_turn") ? 8 : 0));

    // ---- B11: the crossbow and bow steps shown on the figure ----
    public static string RangedPhaseName(byte p) => p switch
    {
        0 => "none", 1 => "charging", 2 => "aiming", 3 => "firing", 4 => "reloading", 5 => "uncharging", 6 => "leaving", _ => "?",
    };

    /// <summary>The game's own fragment (Animations/Mannequin/ADB/kcd_male_database.adb) a figure plays for the step, "" = not shown: the
    /// draw and the aim hold need the shooting's own tags (prime / charge), which only the game's shooting sets.</summary>
    public static string RangedFragment(byte p) => p switch { 3 => "Fire", 4 => "Load", 5 => "Uncharge", _ => "" };

    /// <summary>The fragment tags: the weapon's (a crossbow's reload with the goat's foot).</summary>
    public static string RangedTags(byte p, bool bow) => bow ? "l_bow" : p == 4 ? "l_crossbow+medium+goatsfoot" : "l_crossbow";

    // ---- V.1 joiner_receive_only ----
    /// <summary>Is a joiner's own root step his to ask for? Only what his own conversation decided, or his own interaction (the
    /// coal-sack search: an interactiontrigger_* of this player); anything else his game's quest logic decided by itself (a distance,
    /// an area, a profile, a clock) is the host's world's to decide -- never sent.</summary>
    public static bool ReceiveOnlyAsks(bool fromOwnConversation, string path) =>
        fromOwnConversation || path.Contains(".interactiontrigger_", StringComparison.OrdinalIgnoreCase);

    // ---- Act V F1: the line capture (the DLL's op 83) ----
    public readonly record struct SpeechCaptured(uint Eid, float Dur, bool FromDialogue, float Dist, int AgeMs, string Path, string Text, string Name);

    /// <summary>The DLL's op 83 reply: [n] then per line [eid:4][dur:f32][flags:1][dist:f32][age:2][pl:1][path][tl:1][text][nl:1][name].</summary>
    public static List<SpeechCaptured> ParseSpeechPoll(byte[] p)
    {
        var o = new List<SpeechCaptured>();
        if (p.Length < 1) return o;
        int n = p[0], i = 1;
        for (int k = 0; k < n; k++)
        {
            if (i + 16 > p.Length) break;
            uint eid = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(i));
            float dur = BitConverter.ToSingle(p, i + 4);
            bool dlg = (p[i + 8] & 1) != 0;
            float dist = BitConverter.ToSingle(p, i + 9);
            int age = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(i + 13));
            int pl = p[i + 15];
            if (i + 16 + pl + 1 > p.Length) break;
            string path = System.Text.Encoding.UTF8.GetString(p, i + 16, pl);
            int tl = p[i + 16 + pl];
            if (i + 17 + pl + tl + 1 > p.Length) break;
            string text = System.Text.Encoding.UTF8.GetString(p, i + 17 + pl, tl);
            int nl = p[i + 17 + pl + tl];
            if (i + 18 + pl + tl + nl > p.Length) break;
            string name = System.Text.Encoding.UTF8.GetString(p, i + 18 + pl + tl, nl);
            o.Add(new SpeechCaptured(eid, dur, dlg, dist, age, path, text, name));
            i += 18 + pl + tl + nl;
        }
        return o;
    }

    // WO-204 III.2: the overheard broadcast (SpeechSendable / SpeechWire) and the F2 vote panel (the Vote* wires) are retired; the
    // capture above stays.

    // ---- V.3 Dude2: the watchdog and the save-decode check ----
    /// <summary>Does the watchdog's read trip V.3? (The save gate not armed while the bit is held, the player's own soul not the
    /// PlayerSoul slot's any more, or Dude2 with IsLocalPlayer.)</summary>
    public static bool Dude2Trips(bool gateArmed, bool playerSoulOk, bool dude2Local, out string why)
    {
        why = !gateArmed ? "the save gate is not armed" : !playerSoulOk ? "the PlayerSoul slot is not the player's own soul" : dude2Local ? "the figure's soul has IsLocalPlayer" : "";
        return why.Length > 0;
    }

    /// <summary>The census after a load: PASS when the only soul the engine decoded with IsPlayer is the player's own.</summary>
    public static string CensusVerdict(uint withPlayer, uint withLocal, bool playerSoulHasBit, bool dude2Held)
    {
        if (!playerSoulHasBit) return "FAIL (the player's own soul lacks IsPlayer)";
        if (withLocal != 1) return $"FAIL ({withLocal} souls with IsLocalPlayer)";
        uint expect = dude2Held ? 2u : 1u;
        if (withPlayer == expect) return dude2Held ? "PASS (the player and Dude2, in play)" : "PASS (the player's own only)";
        return $"FAIL ({withPlayer} souls with IsPlayer, {expect} expected)";
    }

    public static bool TryParseCfg(string? arg, out string name, out bool on)
    {
        name = ""; on = false;
        if (string.IsNullOrEmpty(arg)) return false;
        int eq = arg.IndexOf('=');
        if (eq <= 0) return false;
        name = arg[..eq];
        string v = arg[(eq + 1)..];
        if (Array.IndexOf(SwitchNames, name) < 0) return false;
        if (v == "on") { on = true; return true; }
        if (v == "off") { on = false; return true; }
        return false;
    }

    // ---- B5: the engine's own lines about this machine's transfer / loot / trade screen ----
    public const string LootScreenInPrefix = "PlayAudio: ui_inv_screen_in";
    public const string LootScreenOutPrefix = "PlayAudio: ui_inv_screen_out";
    public const string OnLootPrefix = "OnLoot: Dude->";

    public enum ScreenSignal { None, In, Out, Intent }

    /// <summary>B5: what an engine line says about an inventory screen; <paramref name="subject"/> is the body an OnLoot line names.</summary>
    public static ScreenSignal ScreenLine(string line, out string subject)
    {
        subject = "";
        if (line.StartsWith(LootScreenInPrefix, StringComparison.Ordinal)) return ScreenSignal.In;
        if (line.StartsWith(LootScreenOutPrefix, StringComparison.Ordinal)) return ScreenSignal.Out;
        if (line.StartsWith("PlayAudio: ApseOpen", StringComparison.Ordinal)) return ScreenSignal.In;
        if (line.StartsWith("PlayAudio: ApseClose", StringComparison.Ordinal)) return ScreenSignal.Out;
        if (line.StartsWith(OnLootPrefix, StringComparison.Ordinal))
        {
            string n = line[OnLootPrefix.Length..].Trim();
            if (n.Length == 0 || n.Length > 96 || !IsEntityName(n)) return ScreenSignal.None;
            subject = n;
            return ScreenSignal.Intent;
        }
        return ScreenSignal.None;
    }

    /// <summary>An entity name safe to put into a Lua string literal.</summary>
    public static bool IsEntityName(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or '[' or ']' or ':' or '/')) return false;
        return true;
    }

    // ---- B6 / B8: this player's dialogues ----

    /// <summary>
    /// B6: a "New dialogue ... souls = 'Ex: Dude'" start (or an "Attempting to start new dialogue ... with souls 'Ex: Dude'" attempt) whose
    /// only participant is the player: his own monologue, a bark or a focus-camera line -- no conversation rescue ever touches it.
    /// </summary>
    public static bool IsSoloDudeDialogue(string line)
    {
        int s = line.IndexOf("souls = '", StringComparison.Ordinal);
        int k = 9;
        if (s < 0) { s = line.IndexOf("with souls '", StringComparison.Ordinal); k = 12; }
        if (s < 0) return false;
        int e = line.IndexOf('\'', s + k);
        string souls = e > s ? line[(s + k)..e] : line[(s + k)..];
        if (souls.Contains(';')) return false;
        string t = souls.Trim();
        if (t.StartsWith("Ex: ", StringComparison.Ordinal) || t.StartsWith("Nx: ", StringComparison.Ordinal)) t = t[4..];
        int sp = t.IndexOf(' ');
        if (sp > 0) t = t[..sp];
        return t == "Dude";
    }

    /// <summary>B6: the player asked to talk (the engine's "OnChatWithFocus: Dude-&gt;x") this long before the attempt = his own talk.</summary>
    public const long OwnTalkWindowMs = 30_000;

    public static bool OwnTalk(long pressMs, long attemptMs) => pressMs > 0 && attemptMs >= pressMs && attemptMs - pressMs <= OwnTalkWindowMs;

    /// <summary>B8: "Canceling dialog request id N ..." -> N, else -1.</summary>
    public static int CancelledRequestId(string line)
    {
        const string p = "Canceling dialog request id ";
        if (!line.StartsWith(p, StringComparison.Ordinal)) return -1;
        int i = p.Length, n = 0, d = 0;
        while (i < line.Length && char.IsAsciiDigit(line[i]) && d < 9) { n = n * 10 + (line[i] - '0'); i++; d++; }
        return d > 0 ? n : -1;
    }

    /// <summary>B8: "[ID: N] Dialog interrupted..." / "[ID: N] Dialog end..." -> N, else -1.</summary>
    public static int ClosedDialogId(string line)
    {
        if (!line.StartsWith("[ID: ", StringComparison.Ordinal)) return -1;
        int i = 5, n = 0, d = 0;
        while (i < line.Length && char.IsAsciiDigit(line[i]) && d < 9) { n = n * 10 + (line[i] - '0'); i++; d++; }
        if (d == 0) return -1;
        string rest = line[i..];
        return rest.StartsWith("] Dialog interrupted", StringComparison.Ordinal) || rest.StartsWith("] Dialog end", StringComparison.Ordinal) ? n : -1;
    }

    // ---- B2: a stale queued scene never starts late ----

    /// <summary>B2: this long in the engine's scene queue and a scene of the player's is stale: it never starts.</summary>
    public const long StaleQueueMs = 10_000;

    /// <summary>B2: "Scene 'X' was addded into waiting players ..." (the engine's spelling) -> X ("" for an unnamed one), null otherwise.</summary>
    public static string? WaitingScene(string line)
    {
        const string p = "Scene '";
        if (!line.StartsWith(p, StringComparison.Ordinal)) return null;
        int q = line.IndexOf('\'', p.Length);
        if (q < 0 || !line.AsSpan(q).StartsWith("' was addded into waiting players", StringComparison.Ordinal)) return null;
        return line[p.Length..q];
    }

    /// <summary>B2: "InteractiveSceneManager::EndScene called for X" / "...::CleanupScene called for X" -> X, null otherwise.</summary>
    public static string? EndedScene(string line)
    {
        foreach (var p in new[] { "InteractiveSceneManager::EndScene called for ", "InteractiveSceneManager::CleanupScene called for " })
            if (line.StartsWith(p, StringComparison.Ordinal)) return line[p.Length..].Trim();
        return null;
    }

    /// <summary>
    /// B2: the quest module a waiting scene belongs to: its name without a trailing speak trigger ("...zpet_u_semina.SpeakTrigger.speaktrigger"
    /// -> "...zpet_u_semina"); a dialogue of that module that starts later is that scene's conversation.
    /// </summary>
    public static string SceneModule(string scene)
    {
        var parts = scene.Split('.');
        int n = parts.Length;
        while (n > 1 && parts[n - 1].Equals("speaktrigger", StringComparison.OrdinalIgnoreCase)) n--;
        return string.Join('.', parts, 0, n);
    }

    /// <summary>B2: does the starting dialogue (its node path) belong to the waiting scene?</summary>
    public static bool DialogueOfScene(string dialoguePath, string scene)
    {
        if (string.IsNullOrEmpty(scene) || string.IsNullOrEmpty(dialoguePath)) return false;
        if (dialoguePath == scene) return true;
        string m = SceneModule(scene);
        return m.Length > 0 && dialoguePath.StartsWith(m + ".", StringComparison.Ordinal);
    }

    /// <summary>B2: "New dialogue 'PATH (kind - Id: N) ...' is starting. Params: souls = 'Ex: Dude..." -> (PATH, N) when Dude is in it.</summary>
    public static bool TryDudeDialogueStart(string line, out string path, out int id)
    {
        path = ""; id = -1;
        const string p = "New dialogue '";
        if (!line.StartsWith(p, StringComparison.Ordinal) || !line.Contains("souls = 'Ex: Dude", StringComparison.Ordinal)) return false;
        int sp = line.IndexOf(" (", p.Length, StringComparison.Ordinal);
        if (sp < 0) return false;
        path = line[p.Length..sp];
        int ix = line.IndexOf(" - Id: ", sp, StringComparison.Ordinal);
        if (ix < 0) return false;
        int i = ix + 7, n = 0, d = 0;
        while (i < line.Length && char.IsAsciiDigit(line[i]) && d < 9) { n = n * 10 + (line[i] - '0'); i++; d++; }
        if (d == 0) return false;
        id = n;
        return true;
    }

    // ---- B3: the engine's own mount lines (the host) ----

    /// <summary>
    /// B3: "Actor 'X' starts mounting horse 'H'[ instantly]" -> (X, H, true); "Actor 'X' dismounting horse 'H'" -> (X, H, false). Dialogue
    /// twins and the mod's own figures are never a host NPC's mount.
    /// </summary>
    public static bool TryMountLine(string line, out string actor, out string horse, out bool mounting)
    {
        actor = ""; horse = ""; mounting = false;
        const string p = "Actor '";
        if (!line.StartsWith(p, StringComparison.Ordinal)) return false;
        int q = line.IndexOf('\'', p.Length);
        if (q < 0) return false;
        actor = line[p.Length..q];
        string rest = line[(q + 1)..];
        string verb;
        if (rest.StartsWith(" starts mounting horse '", StringComparison.Ordinal)) { verb = " starts mounting horse '"; mounting = true; }
        else if (rest.StartsWith(" dismounting horse '", StringComparison.Ordinal)) verb = " dismounting horse '";
        else return false;
        int h0 = verb.Length, h1 = rest.IndexOf('\'', h0);
        if (h1 < 0) return false;
        horse = rest[h0..h1];
        if (actor.StartsWith("DialogTwin_", StringComparison.Ordinal) || actor.StartsWith("kcd2mp", StringComparison.Ordinal)) return false;
        return IsEntityName(actor) && IsEntityName(horse);
    }

    /// <summary>B3: a mounted rider's row is sent again this often (a joiner who connected later, a row lost in a load).</summary>
    public const long MountReassertMs = 10_000;

    // ---- B4: combat is a value ----

    /// <summary>B4: the host's NPC left combat this long ago (WO-183's debounce: a retarget is no leave) -> its copy's fight ends.</summary>
    public const long CombatEndAfterMs = 1_600;
    /// <summary>B4: a copy's own combat shout is refused at most this often per NPC.</summary>
    public const long LocalShoutEndEveryMs = 2_000;

    /// <summary>B4: does the host's word say this NPC fights? (in combat, or a target -- the host, a figure)</summary>
    public static bool HostFights(bool inCombat, bool hasTarget) => inCombat || hasTarget;

    /// <summary>
    /// B4: a copy's own combat shout at a figure. The host's word decides: refused (its fight ended) when the host's NPC is not fighting --
    /// no word yet, or "not fighting" for at least the debounce.
    /// </summary>
    public static bool ShoutRefused(bool haveWord, bool hostFights, long sinceWordMs) =>
        !haveWord || (!hostFights && sinceWordMs >= CombatEndAfterMs);

    // ---- B13: the clock catches up ----

    /// <summary>
    /// B13: the joiner's clock ratio for `errSeconds` (game seconds AHEAD of the host's; negative = behind). Beyond 30 game s behind it
    /// catches up at up to +50 % and keeps doing so until within 5 (the field stayed 45-48 s behind at WO-200's 25 %); otherwise WO-200's
    /// slew. Never a jump, never more than 10 minutes (WO-133 / WO-140 handle those).
    /// </summary>
    public static double CatchUpRatio(double hostRatio, double errSeconds, ref bool catchingUp)
    {
        if (!double.IsFinite(errSeconds) || Math.Abs(errSeconds) > 600) { catchingUp = false; return hostRatio; }
        if (errSeconds < -30) catchingUp = true;
        if (catchingUp && errSeconds >= -5) catchingUp = false;
        if (catchingUp)
        {
            double k = Math.Clamp(errSeconds / 120.0, -0.5, -0.1);
            return Math.Round(hostRatio * (1 - k), 2);
        }
        return Wo200Rules.SlewRatio(hostRatio, errSeconds);
    }

    public static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
