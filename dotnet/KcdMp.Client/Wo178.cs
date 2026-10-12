// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;

namespace KcdMp.Client;

/// <summary>
/// WO-178: the pure rules of 0.50.5's agent half (Part 0 save safety, C1's copy offers, C4's held blows). The bridge is GameBridge.Wo178.cs;
/// the Lua half is kdcmp.lua's WO-178 block; the native half is wo178.cpp.
/// </summary>
public static class Wo178Rules
{
    /// <summary>mp_world_autosave: the mod's scheduled world saves. Off (the maintainer's decision, 2026-10-12): saving is the player's choice.</summary>
    public const bool DefaultWorldAutosave = false;
    /// <summary>mp_quicksave: the host's setting (the quicksave key saves; off for a schnapps-economy run).</summary>
    public const bool DefaultQuicksave = true;
    /// <summary>mp_joiner_lock: the hostility answer for the joiner's own fight.</summary>
    public const bool DefaultJoinerLock = true;
    /// <summary>mp_host_lock_answer: the host's answer instead of WO-165's skirmish pair -- compared live, never the default here.</summary>
    public const bool DefaultHostLockAnswer = false;
    /// <summary>C4 (1): a joiner's blow waits up to 0.8 s for the figure's strike window.</summary>
    public const bool DefaultFallbackHold = true;
    /// <summary>C4 (2): a raw fallback blow costs what the engine's own costs and never floors an NPC.</summary>
    public const bool DefaultFallbackEngineCap = true;

    // ---- the quicksave key (the launcher's settings.json QuickSaveKey; the keys pak binds it at the next game start) ----
    public const string QuickSaveKeySetting = "QuickSaveKey";
    /// <summary>
    /// The default: the work order asked for F10, which this build binds itself (the Modding Tools build's debug map: togglepdrawhelpers),
    /// and every other F key is the game's debug keys or the mod's dice and invite keys -- Home is the nearest key the game binds only in
    /// its own menus (reset a binding), never in play.
    /// </summary>
    public const string QuickSaveKeyDefault = "home";
    public static readonly string[] QuickSaveKeys = { "home", "delete", "np_add", "np_subtract" };

    /// <summary>The setting as the key to bind: one of <see cref="QuickSaveKeys"/>, never the menu's key (that one wins; the next free key).</summary>
    public static string ResolveQuickSaveKey(string? setting, string menuKey, out string note)
    {
        note = "";
        string s = (setting ?? "").Trim().ToLowerInvariant();
        string want = s.Length == 0 ? QuickSaveKeyDefault : Array.IndexOf(QuickSaveKeys, s) >= 0 ? s : QuickSaveKeyDefault;
        if (s.Length > 0 && Array.IndexOf(QuickSaveKeys, s) < 0) note = $"QuickSaveKey {setting} is not one of {string.Join("|", QuickSaveKeys)} -- {QuickSaveKeyDefault} used";
        if (want == menuKey)
        {
            string other = QuickSaveKeys.First(k => k != menuKey);
            note = (note.Length > 0 ? note + "; " : "") + $"QuickSaveKey {want} is the menu key -- {other} used";
            want = other;
        }
        return want;
    }

    // ---- C1: the copy offers the DLL's hostility answer reads ----
    public const byte WhyNone = 0, WhyEngaged = 1, WhyPartnerFight = 2;

    /// <summary>
    /// A host NPC's combat event on the joiner: engaged (its target is this joiner's avatar, or it fights in a way WO-132 engages it with this
    /// player), partner-fight (it fights the host's own Henry: the joiner may help his partner), none (out of combat: withdrawn).
    /// </summary>
    public static byte OfferWhy(bool inCombat, NpcCombatTarget target, byte targetGhost, byte myGhost)
    {
        if (!inCombat) return WhyNone;
        if (target == NpcCombatTarget.Avatar && myGhost != 0xFF && targetGhost == myGhost) return WhyEngaged;
        if (target == NpcCombatTarget.Host) return WhyPartnerFight;
        return WhyEngaged;
    }

    public static string WhyName(byte w) => w switch { WhyEngaged => "engaged", WhyPartnerFight => "partner-fight", _ => "withdrawn" };

    /// <summary>The same offer again within this long is not resent (the DLL keeps an offer 3.5 s).</summary>
    public const long OfferRefreshMs = 1_000;

    // ---- Part 0: the save request ----
    /// <summary>A save the DLL accepted is looked for in the saves folder this long (the engine writes it in well under a second).</summary>
    public const int SaveFileWaitMs = 20_000;

    /// <summary>The Lua's "w178_save" argument: the caller's word (letters, digits, :_-), at most 40 characters.</summary>
    public static bool IsSaveWhy(string? why) =>
        why is { Length: > 0 and <= 40 } && why.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '_' or '-');

    /// <summary>What a SaveManual answer means for the player: requested, refused (the engine cannot save now), not-armed, fault.</summary>
    public static string SaveWord(byte r) => r switch { 1 => "requested", 0 => "refused", 2 => "not-armed", 3 => "fault", _ => "no-answer" };

    // ---- the joiner's quicksave on the host ----
    /// <summary>The host's answer text: "saved", "waited", "refused &lt;why&gt;".</summary>
    public static bool TryParseAnswer(string? text, out string word, out string why)
    {
        word = why = "";
        var f = (text ?? "").Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (f.Length == 0 || f[0] is not ("saved" or "waited" or "refused")) return false;
        word = f[0];
        why = f.Length > 1 ? f[1] : "";
        return why.Length <= 40 && why.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':');
    }

    /// <summary>"quicksave=on|off" (the host's setting to its joiners).</summary>
    public static bool TryParseSetting(string? text, out bool on)
    {
        on = false;
        if (text == "quicksave=on") { on = true; return true; }
        return text == "quicksave=off";
    }

    /// <summary>The joiner's ask: the source word (key|menu|console).</summary>
    public static bool IsAskSource(string? s) => s is "key" or "menu" or "console";

    // ---- C4: a held blow ----
    public const byte AppliedHeld = 4;
    public const int HeldPollMs = 100;
    public const int HeldGiveUpMs = 2_500;   // the DLL runs a held blow within 0.8 s; past this the answer is lost (counted)

    // ---- the w178_cfg line ----
    /// <summary>"name=on|off" from the Lua; the names it may carry.</summary>
    public static bool TryParseCfg(string? arg, out string name, out bool on)
    {
        name = ""; on = false;
        var f = (arg ?? "").Split('=', 2);
        if (f.Length != 2 || f[1] is not ("on" or "off")) return false;
        if (f[0] is not ("world_autosave" or "quicksave" or "joiner_lock" or "host_lock_answer" or "fallback_hold" or "fallback_engine_cap")) return false;
        name = f[0]; on = f[1] == "on";
        return true;
    }

    /// <summary>The marker the launcher's RESTORE SAVES reads: the backup to pick (the newest before this session) and why.</summary>
    public const string PickFileName = "restore-pick.txt";
    public static string PickText(string stamp, string why, DateTime utc) =>
        string.Create(CultureInfo.InvariantCulture, $"{stamp}\n{why}\n{utc:O}\n");
}
