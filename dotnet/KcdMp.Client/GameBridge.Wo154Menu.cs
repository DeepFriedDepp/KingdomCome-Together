// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text.Json.Nodes;

namespace KcdMp.Client;

// WO-154 Phase 7b -- the mod menu, the agent's half (docs/WO-154-findings.md; the player's page docs/MOD-MENU.md).
// The rules are Wo154MenuRules (Wo154Menu.cs); the menu itself is the mod's (kdcmp.lua, KCD2MP.w154menu).
//
//   remembering   w154_menu_set <Key> <value> -> mod-settings.json beside this exe, through SettingsJson (key by
//                 key, the player's other keys kept); MenuKey -> the launcher's settings.json, only when it exists.
//                 Pushed back into the mod (KCD2MP_W154MenuRestore: the console command's own setter) when a
//                 session starts and after every world load; the host's levers only while this player hosts.
//   the session   once a second KCD2MP_W154MenuSession: the role, a partner here, the host's levers as a joiner
//                 knows them, the link, the version, the game's own screens, this player down (WO-113).
//   host levers   the host's crime mode and fast travel go to every joiner as SessionSettings (keys 2 and 3, the
//                 friendly-fire channel of WO-121; the relay forwards them from the host only): on a change, a
//                 new partner, and every 10 s.
//   fast travel   the mod switches wh_pl_FastTravelEnabled itself; the engine's refusal of a fast travel while
//                 the session's is off is said to the player (KCD2MP_W154FastTravelTried).
//   game screens  the game's own menu, inventory, map, skip-time or a cutscene (the log tail's edges) closes the
//                 mod menu at once (KCD2MP_W154MenuGameScreen); the push repeats the state every second.
//   MP-MENU-SAVE <Key>=<value> -> <file> <outcome>   MP-MENU-RESTORE ...   MP-MENU host sent / session ... from=host
public partial class GameBridge
{
    private volatile bool _w154mConnected;
    private volatile bool _w154mFtPref = Wo154MenuRules.FastTravelDefault;   // this mod's mp_fast_travel (the host's decides)
    private volatile bool _w154mHostFtKnown, _w154mHostFt;                   // joiner: the host's fast travel (SessionSetting 3)
    private volatile bool _w154mHostCrimeKnown, _w154mHostCrimeJoint;        // joiner: the host's crime mode (SessionSetting 2)
    private int _w154mEpoch, _w154mRestoredEpoch = -1;                       // a session start or a world load = a new epoch
    private long _w154mEpochAtMs;
    private bool _w154mHostRestored;
    private (bool Ft, bool Crime)? _w154mSent;
    private long _w154mSentAtMs;
    private int _w154mSentPartners;
    private string _w154mActiveKey = KeybindPak.MenuKeyDefault, _w154mStoredKey = KeybindPak.MenuKeyDefault;
    private readonly HashSet<byte> _w154mUnknownKeys = new();
    private long _w154mSaves, _w154mRestores, _w154mHostSends;

    private const long W154mHostResendMs = 10_000;
    private const long W154mRestoreSettleMs = 2_000;   // after "Gameplay started": the world's first pushes go first

    private static string W154mModSettings => Path.Combine(AppContext.BaseDirectory, Wo154MenuRules.ModSettingsFile);
    private static string W154mLauncherSettings => Path.Combine(AppContext.BaseDirectory, Wo154MenuRules.LauncherSettingsFile);

    private void Wo154MenuOnConnect(CancellationToken ct)
    {
        _w154mConnected = true;
        _w154mHostFtKnown = _w154mHostCrimeKnown = false;
        _w154mSent = null;
        _w154mSentPartners = 0;
        _w154mHostRestored = false;
        Interlocked.Increment(ref _w154mEpoch);                       // a session start: the saved choices go in
        _w154mEpochAtMs = Environment.TickCount64 - W154mRestoreSettleMs;
        Wo154MenuReadKeys();
        if (_transport is LogTailGameTransport t)
        {
            t.GameplayStarted += Wo154MenuOnGameplayStarted;
            t.PauseStateChanged += Wo154MenuOnGameScreen;
        }
        _ = Wo154MenuLoopAsync(ct);
    }

    private async Task Wo154MenuOnDisconnectAsync()
    {
        _w154mConnected = false;
        if (_transport is LogTailGameTransport t)
        {
            t.GameplayStarted -= Wo154MenuOnGameplayStarted;
            t.PauseStateChanged -= Wo154MenuOnGameScreen;
        }
        _w154mHostFtKnown = _w154mHostCrimeKnown = false;
        _w154mSent = null;
        // No session any more: the mod gives fast travel back at once (its own backstop would after 10 s).
        try
        {
            await _transport.ExecuteNowAsync(Wo154MenuRules.SessionLua("none", false, null, null, null,
                Wo154MenuRules.LinkWord(AgentConnectionStatus.State), ReleaseVersionInfo.Current, Wo154MenuGameScreenNow(), false));
        }
        catch { }
    }

    /// <summary>"Gameplay started": a world (re)loaded -- the saved choices go in again once it has settled.</summary>
    private void Wo154MenuOnGameplayStarted()
    {
        Interlocked.Increment(ref _w154mEpoch);
        _w154mEpochAtMs = Environment.TickCount64;
        _w154mHostRestored = false;
    }

    /// <summary>The log tail's aggregate (the game's menu, inventory, map, skip-time, a cutscene): the mod menu closes.</summary>
    private void Wo154MenuOnGameScreen(bool on) =>
        _ = ExecLuaAsync($"if KCD2MP_W154MenuGameScreen then KCD2MP_W154MenuGameScreen({(on ? "true" : "false")}, \"agent\") end");

    private bool Wo154MenuGameScreenNow()
    {
        if (_transport is LogTailGameTransport t) return t.MenuOpen || t.InventoryOpen || t.SkipTimeActive || t.CutsceneActive;
        return _localAutoPaused;
    }

    private async Task Wo154MenuLoopAsync(CancellationToken ct)
    {
        long lastStats = Environment.TickCount64, statsKey = 0;
        while (!ct.IsCancellationRequested && _w154mConnected)
        {
            try { await Task.Delay(1000, ct); } catch { return; }
            try
            {
                if (Environment.TickCount64 - lastStats >= 60_000)
                {
                    lastStats = Environment.TickCount64;
                    long key = Interlocked.Read(ref _w154mSaves) + Interlocked.Read(ref _w154mRestores) + Interlocked.Read(ref _w154mHostSends);
                    if (key != statsKey) { statsKey = key; Console.WriteLine(Wo154MenuStatsLine()); }
                }
                bool host = _combatRoleApplied && _isDamageAuthority, joiner = _combatRoleApplied && !_isDamageAuthority;
                int partners = LivePartners().Count;
                if (host) await Wo154MenuHostSendAsync(partners);
                if (_where is GameWhere.Menu or GameWhere.Loading) continue;   // the mod is not in a world: nothing to tell it

                await ExecLuaAsync(Wo154MenuRules.SessionLua(host ? "host" : joiner ? "joiner" : "none", partners > 0,
                    joiner && _w154mHostFtKnown ? _w154mHostFt : null,
                    joiner && _w154mHostCrimeKnown ? _w154mHostCrimeJoint : null,
                    joiner && _leashHostCfgUtc != DateTime.MinValue ? _leashHostOn : null,
                    Wo154MenuRules.LinkWord(AgentConnectionStatus.State), ReleaseVersionInfo.Current, Wo154MenuGameScreenNow(), _localDowned));

                // The saved choices: once per epoch (a session start, a world load), after the world settled; the
                // host's levers again whenever this player becomes the host within an epoch.
                int epoch = Volatile.Read(ref _w154mEpoch);
                if (epoch != _w154mRestoredEpoch && Environment.TickCount64 - _w154mEpochAtMs >= W154mRestoreSettleMs)
                {
                    _w154mRestoredEpoch = epoch;
                    _w154mHostRestored = host;
                    await Wo154MenuRestoreAsync(host, own: true, host ? "host" : joiner ? "joiner" : "unknown role");
                    await ExecLuaAsync(Wo154MenuRules.KeysLua(_w154mActiveKey, _w154mStoredKey, ""));
                }
                else if (host && !_w154mHostRestored && epoch == _w154mRestoredEpoch)
                {
                    _w154mHostRestored = true;
                    await Wo154MenuRestoreAsync(true, own: false, "became the host");
                }
                if (!host) _w154mHostRestored = false;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Console.WriteLine($"MP-MENU tick failed: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    /// <summary>The saved choices into the mod (each through the console command's own setter, in the mod).</summary>
    private async Task Wo154MenuRestoreAsync(bool hosting, bool own, string why)
    {
        JsonObject? file = SettingsJson.Read(W154mModSettings);
        var plan = Wo154MenuRules.RestorePlan(file, hosting, own);
        if (plan.Count == 0)
        {
            if (own) Console.WriteLine($"MP-MENU-RESTORE nothing saved to push ({(file is null ? "no " + Wo154MenuRules.ModSettingsFile + " yet" : "no choice in it")}; {why})");
            return;
        }
        foreach (var (key, word) in plan) await ExecLuaAsync(Wo154MenuRules.RestoreLua(key, word));
        Interlocked.Add(ref _w154mRestores, plan.Count);
        Console.WriteLine($"MP-MENU-RESTORE {plan.Count} saved choice(s) pushed into the mod ({why}{(hosting ? "" : "; the host's levers are the host's")}): {string.Join(" ", plan.Select(p => p.Key + "=" + p.Word))}");
    }

    // ---------------------------------------------------------------- the mod's events

    /// <summary><c>w154_menu_set &lt;Key&gt; &lt;value&gt;</c> (a menu choice to remember), <c>w154_menu_cfg fast_travel=on|off</c>.</summary>
    private void Wo154MenuOnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w154_menu_cfg":
            {
                bool? ft = null;
                foreach (var kv in (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (kv is "fast_travel=on" or "fast_travel=off") ft = kv == "fast_travel=on";
                if (ft is not bool v) { Console.WriteLine($"MP-MENU malformed w154_menu_cfg '{W154mTrunc(arg)}'"); return; }
                bool changed = v != _w154mFtPref;
                _w154mFtPref = v;
                if (changed)
                    Console.WriteLine($"MP-MENU cfg fast_travel={On(v)} role={(!_combatRoleApplied ? "unknown" : _isDamageAuthority ? "host (decides for the session)" : "joiner (the host's value applies)")}");
                return;
            }
            case "w154_menu_set":
                Wo154MenuSave(arg);
                return;
        }
    }

    private void Wo154MenuSave(string? arg)
    {
        if (!Wo154MenuRules.TryParseSet(arg, out string key, out string word, out JsonNode? value))
        {
            Console.WriteLine($"MP-MENU-SAVE refused: '{W154mTrunc(arg)}' is not a menu choice this agent remembers");
            return;
        }
        if (key == KeybindPak.MenuKeySetting) { Wo154MenuSaveKey(word, value); return; }
        var r = SettingsJson.Update(W154mModSettings, new[] { SettingsJson.Edit.Set(key, value) }, createIfMissing: true);
        Interlocked.Increment(ref _w154mSaves);
        Console.WriteLine($"MP-MENU-SAVE {key}={word} -> {Wo154MenuRules.ModSettingsFile} {r.Outcome.ToString().ToLowerInvariant()}"
                          + (r.Ok ? "" : $" ({Wo154MenuRules.Scrub(r.Why, AppContext.BaseDirectory)})"));
    }

    /// <summary>The menu key goes into the launcher's settings.json (it builds the keys pak from it at the next game
    /// start) -- only when that file exists: this never creates the launcher's file.</summary>
    private void Wo154MenuSaveKey(string key, JsonNode? value)
    {
        var r = SettingsJson.Update(W154mLauncherSettings, new[] { SettingsJson.Edit.Set(KeybindPak.MenuKeySetting, value) }, createIfMissing: false);
        bool ok = r.Outcome is SettingsJson.Outcome.Written or SettingsJson.Outcome.Unchanged;
        if (ok) _w154mStoredKey = key;
        Console.WriteLine($"MP-MENU-SAVE MenuKey={key} -> {Wo154MenuRules.LauncherSettingsFile} {r.Outcome.ToString().ToLowerInvariant()}"
                          + (ok ? " -- the keys pak binds it at the next game start" : $" ({Wo154MenuRules.Scrub(r.Why, AppContext.BaseDirectory)}) -- not saved"));
        _ = ExecLuaAsync(Wo154MenuRules.KeysLua(_w154mActiveKey, _w154mStoredKey, ok ? "stored" : "not-saved"));
    }

    /// <summary>The key bound in this game run (read back from the keys pak the launcher built) and the one stored for
    /// the next start (settings.json). File names only in the log.</summary>
    private void Wo154MenuReadKeys()
    {
        string stored = KeybindPak.ResolveMenuKey(KeybindPak.ReadMenuKeySetting(W154mLauncherSettings), out string note);
        string? bound = null;
        try
        {
            if (KcdLogLocator.Find() is string log && Path.GetDirectoryName(log) is string root)
                bound = KeybindPak.BoundMenuKeyInPak(Path.Combine(root, "Mods", "kdcmp", "Data", KeybindPak.PakFileName));
        }
        catch { }
        _w154mStoredKey = stored;
        _w154mActiveKey = bound is not null && Array.IndexOf(KeybindPak.MenuKeys, bound) >= 0 ? bound : stored;
        Console.WriteLine($"MP-MENU keys: bound={_w154mActiveKey} ({(bound is null ? "the keys pak was not readable: settings.json's" : KeybindPak.PakFileName)}) stored={stored} ({Wo154MenuRules.LauncherSettingsFile}{(note.Length > 0 ? ": " + note : "")})");
    }

    // ---------------------------------------------------------------- the host's levers on the wire

    /// <summary>Host: crime mode and fast travel to every partner -- on a change, a new partner, every 10 s.</summary>
    private async Task Wo154MenuHostSendAsync(int partners)
    {
        if (partners == 0 || _wo121Stream is not Stream s) { _w154mSentPartners = partners; return; }
        bool ft = _w154mFtPref, crime = _w151CrimeJoint;
        long now = Environment.TickCount64;
        bool changed = _w154mSent is not { } p || p.Ft != ft || p.Crime != crime;
        bool newPartner = partners > _w154mSentPartners;
        _w154mSentPartners = partners;
        if (!changed && !newPartner && now - _w154mSentAtMs < W154mHostResendMs) return;
        try
        {
            await WritePacketAsync(s, _actionOut.Build(ActionKind.SessionSetting, ActionPhase.Commit, [SessionSettingKey.FastTravel, (byte)(ft ? 1 : 0)]), _wo121Ct);
            await WritePacketAsync(s, _actionOut.Build(ActionKind.SessionSetting, ActionPhase.Commit, [SessionSettingKey.CrimeMode, (byte)(crime ? 1 : 0)]), _wo121Ct);
            _w154mSent = (ft, crime);
            _w154mSentAtMs = now;
            Interlocked.Increment(ref _w154mHostSends);
            if (changed || newPartner)
                Console.WriteLine($"MP-MENU host sent fast_travel={On(ft)} crime_mode={(crime ? "joint" : "individual")} why={(changed ? "change" : "new-partner")} partners={partners}");
        }
        catch (Exception ex) { Console.WriteLine($"MP-MENU host send failed: {ex.Message}"); }
    }

    /// <summary>Joiner: a SessionSetting that is not friendly fire (WO-121 handles that one).</summary>
    private void Wo154MenuOnSessionSetting(byte key, byte value, byte src)
    {
        bool v = value != 0;
        switch (key)
        {
            case SessionSettingKey.FastTravel:
                if (!_w154mHostFtKnown || _w154mHostFt != v) Console.WriteLine($"MP-MENU session fast_travel={On(v)} from=host ghost={src}");
                _w154mHostFt = v;
                _w154mHostFtKnown = true;
                return;
            case SessionSettingKey.CrimeMode:
                if (!_w154mHostCrimeKnown || _w154mHostCrimeJoint != v) Console.WriteLine($"MP-MENU session crime_mode={(v ? "joint" : "individual")} from=host ghost={src}");
                _w154mHostCrimeJoint = v;
                _w154mHostCrimeKnown = true;
                return;
            default:
                lock (_w154mUnknownKeys)
                    if (_w154mUnknownKeys.Add(key)) Console.WriteLine($"MP-MENU session setting {SessionSettingKey.Name(key)}={value} from ghost {src} -- not one this build knows: ignored");
                return;
        }
    }

    /// <summary>
    /// The engine refused a fast travel here and it was not WO-114's joiner block: when the session's fast travel is
    /// off (this host's mp_fast_travel, or the host's value on a joiner) the player is told why. True = said.
    /// </summary>
    private bool Wo154MenuOnFastTravelRefused()
    {
        bool host = _combatRoleApplied && _isDamageAuthority;
        bool session = _combatRoleApplied && LivePartners().Count > 0;
        bool allowed = host ? _w154mFtPref : _w154mHostFtKnown && _w154mHostFt;
        if (!session || allowed) return false;
        Console.WriteLine($"MP-MENU the engine refused a fast travel: the session's fast travel is off ({(host ? "this host's mp_fast_travel" : "the host's")}) -- telling the player");
        _ = ExecLuaAsync("if KCD2MP_W154FastTravelTried then KCD2MP_W154FastTravelTried(\"engine-refused\") end");
        return true;
    }

    private string Wo154MenuStatsLine() =>
        $"MP-MENU-STATS saves={Interlocked.Read(ref _w154mSaves)} restores={Interlocked.Read(ref _w154mRestores)} host_sends={Interlocked.Read(ref _w154mHostSends)} fast_travel_pref={On(_w154mFtPref)} host_fast_travel={(_w154mHostFtKnown ? On(_w154mHostFt) : "?")} host_crime={(_w154mHostCrimeKnown ? (_w154mHostCrimeJoint ? "joint" : "individual") : "?")} key_bound={_w154mActiveKey} key_stored={_w154mStoredKey}";

    private static string W154mTrunc(string? s) => s is null ? "" : s.Length > 60 ? s[..60] + "..." : s;
}
