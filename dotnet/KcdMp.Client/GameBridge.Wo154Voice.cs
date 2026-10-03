// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

/// <summary>
/// WO-154 (Phase 6.7): the mod's voice chat is off unless the player chose it
/// (KcdMp.Wire.VoiceSetting; docs/WO-154-voice-decision.md). The launcher passes
/// --voice or --no-voice; the in-game mod menu flips it at runtime:
/// <list type="bullet">
/// <item>the mod emits <c>w154_voice on|off</c> (the log-tail event path, any time,
///   connected or not);</item>
/// <item>the agent opens or closes the microphone at once -- off disposes the capture and
///   the playback device: nothing is captured, sent or played, and a frame captured before
///   the close is dropped, never sent;</item>
/// <item>the choice goes into settings.json in the install folder, both keys as the
///   player's own (SettingsJson Set) -- a missing file is not created;</item>
/// <item>the mod is told the state, <c>KCD2MP_W154VoiceState(true|false)</c>, after every
///   change, once per session start and after its Lua restarts.</item>
/// </list>
/// </summary>
public partial class GameBridge
{
    private readonly object _w154VoiceLock = new();
    private bool _w154InSession;   // a relay session is up: voice runs when chosen (under the lock)

    /// <summary>"on" / "off" (the mod menu's switch); null for anything else.</summary>
    public static bool? ParseVoiceSwitch(string? arg) => (arg ?? "").Trim() switch
    {
        "on" => true,
        "off" => false,
        _ => null,
    };

    /// <summary>
    /// The player's choice into a settings file: both keys as Set edits; a missing file is not
    /// created (the launcher's working directory is the install folder; another one has no file
    /// here, and the launcher passes the flag at the next start either way). The outcome, in
    /// words for the log -- never a path.
    /// </summary>
    public static string Wo154PersistVoice(string settingsPath, bool on)
    {
        if (!File.Exists(settingsPath)) return "not found (not created)";
        var r = SettingsJson.Update(settingsPath, VoiceSetting.ChoiceEdits(on), createIfMissing: false);
        return r.Outcome switch
        {
            SettingsJson.Outcome.Written => "updated",
            SettingsJson.Outcome.Unchanged => "had it already",
            _ => "not written (" + (r.Why.IndexOf(':') is int c and > 0 ? r.Why[..c] : r.Why) + ")",
        };
    }

    /// <summary>Session start (ConnectAndRunAsync): voice on if the player chose it; the mod is told either way.</summary>
    private void Wo154VoiceSessionStart()
    {
        lock (_w154VoiceLock)
        {
            _w154InSession = true;
            if (config.VoiceChatEnabled) Wo154StartVoiceLocked("session start");
            else Console.WriteLine("[voice] Off -- the microphone will not be opened (the launcher's setting or the mod menu turns it on).");
        }
        _ = Wo154PushVoiceStateAsync("session start");
    }

    /// <summary>Session end: the microphone closes.</summary>
    private void Wo154VoiceSessionEnd()
    {
        lock (_w154VoiceLock)
        {
            _w154InSession = false;
            Wo154StopVoiceLocked("session end");
        }
    }

    /// <summary>The mod menu's switch.</summary>
    private void Wo154OnVoiceEvent(string? arg)
    {
        if (ParseVoiceSwitch(arg) is not bool on)
        {
            Console.WriteLine($"MP-VOICE malformed w154_voice '{arg}'");
            return;
        }
        string applied;
        lock (_w154VoiceLock)
        {
            config.VoiceChatEnabled = on;
            if (!_w154InSession) applied = "not connected -- applies when the session starts";
            else if (on) applied = Wo154StartVoiceLocked("mod menu") ? "microphone open" : "the microphone could not be opened";
            else { Wo154StopVoiceLocked("mod menu"); applied = "microphone closed"; }
        }
        string saved = Wo154PersistVoice(Path.Combine(AppContext.BaseDirectory, "settings.json"), on);
        Console.WriteLine($"MP-VOICE {(on ? "on" : "off")} from the mod menu: {applied}; settings.json {saved}");
        _ = Wo154PushVoiceStateAsync("mod menu");
    }

    private bool Wo154StartVoiceLocked(string why)
    {
        if (_voice is not null) return true;
        var voice = new VoiceChat(frame => _voiceQueue.Enqueue(frame));
        try
        {
            voice.Start();
            _voice = voice;
            Console.WriteLine($"MP-VOICE microphone open ({why})");
            return true;
        }
        catch (Exception ex)
        {
            try { voice.Dispose(); } catch { }
            Console.WriteLine($"[voice] Failed to start ({why}): {ex.Message}");
            return false;
        }
    }

    private void Wo154StopVoiceLocked(string why)
    {
        var voice = _voice;
        if (voice is null) return;
        _voice = null;                               // first: the loops stop using it
        try { voice.Stop(); } catch { }
        try { voice.Dispose(); } catch { }           // the microphone and the output device close
        while (_voiceQueue.TryDequeue(out _)) { }    // a frame captured before the close is never sent
        Console.WriteLine($"MP-VOICE microphone closed ({why})");
    }

    /// <summary>The mod's menu shows what runs: in a session, whether the microphone is open; outside one, the choice.</summary>
    private async Task Wo154PushVoiceStateAsync(string why)
    {
        bool state;
        lock (_w154VoiceLock) state = _w154InSession ? _voice is not null : config.VoiceChatEnabled;
        try { await ExecLuaAsync($"if KCD2MP_W154VoiceState then KCD2MP_W154VoiceState({(state ? "true" : "false")}) end"); }
        catch (Exception ex) { Console.WriteLine($"MP-VOICE the state did not reach the mod ({why}): {ex.GetType().Name}"); }
    }
}
