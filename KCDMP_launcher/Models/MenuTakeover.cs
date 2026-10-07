// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KCDMP_launcher.Models
{
    /// <summary>
    /// WO-159: the main menu of a game this launcher started (docs/WO-159-findings.md). The pure rules: what the menu
    /// shows (the model pushed to kdcmp.lua's KCD2MP_W159Model), the menu's choices read back from kcd.log, the
    /// joiner's choice in plain words. No UI types: compiled into the client tests by link.
    ///
    /// How the game knows the launcher started it: the launcher tells it, live, through the game's own console (the
    /// Modding Tools' local API) while that game sits at its main menu. A game started any other way is never told, so
    /// its menu is the game's own -- nothing is left behind (no marker file, no setting, no command line) that a later
    /// game could misread.
    /// </summary>
    public static class MenuTakeoverRule
    {
        /// <summary>The menu's choice, from kdcmp.lua's "[KCD2-MP-EVT] v1 &lt;seq&gt; w159 &lt;what&gt;" line.</summary>
        public sealed record MenuChoice(long Seq, string Kind, int Playline = -1, string Name = "", string Join = "", string Style = "", bool Watch = false);

        /// <summary>The prologue's three playstyles, as the menu and the start-save folders name them.</summary>
        public static readonly string[] Styles = { "soldier", "adviser", "scout" };

        public static MenuChoice? Parse(string line)
        {
            const string pfx = "[KCD2-MP-EVT] v1 ";
            int at = line.IndexOf(pfx, StringComparison.Ordinal);
            if (at < 0) return null;
            var p = line[(at + pfx.Length)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 3 || p[1] != "w159" || !long.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out long seq)) return null;
            switch (p[2])
            {
                case "newadv" when p.Length >= 5 && Styles.Contains(p[3]) && p[4] is "skip" or "watch":
                    return new(seq, "newadv", Style: p[3], Watch: p[4] == "watch");
                case "cancel": return new(seq, "cancel");
                case "join" when p.Length >= 5 && p[3] == "fresh" && Styles.Contains(p[4]): return new(seq, "join", Join: "fresh", Style: p[4]);
                case "join" when p.Length == 4 && p[3] == "bring": return new(seq, "join", Join: "bring");
                case "recap" when p.Length >= 4 && p[3] is "finished" or "skipped": return new(seq, "recap", Name: p[3]);
                case "load" when p.Length >= 5 && int.TryParse(p[3], NumberStyles.None, CultureInfo.InvariantCulture, out int pl)
                                  && pl is >= 0 and <= 4 && IsSaveName(p[4]):
                    return new(seq, "load", pl, p[4]);
            }
            return null;
        }

        /// <summary>An engine save name (no path, no extension): what wh_sys_LoadGame takes.</summary>
        public static bool IsSaveName(string s) => s.Length is > 0 and <= 40 && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

        /// <summary>A Lua string literal (double-quoted, escaped; anything outside printable ASCII becomes a space).</summary>
        public static string Lua(string s)
        {
            var b = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '\\' || c == '"') b.Append('\\').Append(c);
                else if (c < 0x20 || c > 0x7E) b.Append(' ');
                else b.Append(c);
            }
            return b.Append('"').ToString();
        }

        public sealed record WorldEntry(int Playline, string Name, string Label);

        /// <summary>
        /// The joiner's Bring choice before the agent runs: possible only with a Modding Tools save of one's own; when
        /// there is none, the WO-157 words for why (their saves are from the regular game / only copies of a host's
        /// world / none yet). The agent decides for real once the host's world is known (WO-125/135/157).
        /// </summary>
        public static (bool Bring, string Msg) JoinChoice(int usable, int hiddenRegular, int hiddenCopies) =>
            usable > 0 ? (true, "")
            : hiddenRegular > 0 && hiddenCopies == 0 ? (false, "Your saves are from the regular game, not the Modding Tools. Join with a new character.")
            : hiddenCopies > 0 ? (false, "Your only Modding Tools saves are copies of a host's world, not your own character. Join with a new character.")
            : (false, "You have no Modding Tools save of your own yet. Join with a new character.");

        /// <summary>
        /// The whole model as one Lua call (KCD2MP_W159Model{...}), with a signature of its content: the game redraws
        /// only when the signature changes. At most five worlds; labels cut to 48 characters (the console takes about
        /// 2,100 encoded characters per call).
        /// </summary>
        public static string ModelCall(bool hosting, IReadOnlyList<WorldEntry> worlds, string hidden, WorldEntry? newAdv, string newWhy,
                                       string joinState, bool bring, string bringMsg, IReadOnlyCollection<string>? styles = null, string joinStatus = "")
        {
            var b = new StringBuilder();
            b.Append("role=").Append(Lua(hosting ? "host" : "join"));
            b.Append(",saves={");
            foreach (var w in worlds.Take(5))
                b.Append("{pl=").Append(w.Playline.ToString(CultureInfo.InvariantCulture)).Append(",name=").Append(Lua(w.Name))
                 .Append(",label=").Append(Lua(w.Label.Length > 48 ? w.Label[..48] : w.Label)).Append("},");
            b.Append('}');
            b.Append(",hidden=").Append(Lua(hidden));
            if (newAdv is not null) b.Append(",newadv={pl=").Append(newAdv.Playline.ToString(CultureInfo.InvariantCulture)).Append(",name=").Append(Lua(newAdv.Name)).Append('}');
            b.Append(",newwhy=").Append(Lua(newWhy));
            b.Append(",join={state=").Append(Lua(joinState)).Append(",bring=").Append(bring ? "true" : "false").Append(",msg=").Append(Lua(bringMsg))
             .Append(",status=").Append(Lua(joinStatus.Length > 120 ? joinStatus[..120] : joinStatus)).Append('}');
            b.Append(",styles={");
            foreach (var st in Styles) if (styles?.Contains(st) == true) b.Append(st).Append("=true,");
            b.Append('}');
            b.Append(",recapMin=").Append(RecapMinutes.ToString(CultureInfo.InvariantCulture));
            string body = b.ToString();
            string sig = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)))[..12].ToLowerInvariant();
            return "if KCD2MP_W159Model then KCD2MP_W159Model({sig=" + Lua(sig) + "," + body + "}) end";
        }

        public const string TickCall = "if KCD2MP_W159Tick then KCD2MP_W159Tick() end";

        /// <summary>
        /// The prologue's rendered cutscenes up to where Hans and Henry part ways, in story order, with each one's length
        /// from its Bink header (WO-159 findings). No in-engine scene: those hold the conversations and choices.
        /// </summary>
        public static readonly (string Path, double Seconds)[] PrologueVideos =
        {
            ("Videos/m50/cin_m5010k_obranabohuta__siege_intro_start/cin_m5010k_obranabohuta__siege_intro_start.bk2", 199.5),
            ("Videos/m50/cin_m5010k_obranabohuta__siege_intro_end/cin_m5010k_obranabohuta__siege_intro_end.bk2", 122.8),
            ("Videos/m01/cin_m0110t_prepadeni__intro_cutscene/cin_m0110t_prepadeni__intro_cutscene.bk2", 169.7),
            ("Videos/m02/cin_m0210t_zachrana__fall_dream_clip01/cin_m0210t_zachrana__fall_dream_clip01.bk2", 149.9),
            ("Videos/m02/cin_m0250t_zachrana__first_dreaming_clip_01/cin_m0250t_zachrana__first_dreaming_clip_01.bk2", 104.9),
            ("Videos/m02/cin_m0260t_zachrana__second_dreaming_clip01/cin_m0260t_zachrana__second_dreaming_clip01.bk2", 16.3),
            ("Videos/m02/cin_m0260t_zachrana__second_dreaming_clip02/cin_m0260t_zachrana__second_dreaming_clip02.bk2", 25.2),
            ("Videos/m02/cin_m0260t_zachrana__second_dreaming_clip03/cin_m0260t_zachrana__second_dreaming_clip03.bk2", 19.8),
            ("Videos/m03/cin_m0310t_socky__trosky_journey/cin_m0310t_socky__trosky_journey.bk2", 172.7),
        };

        public static int RecapMinutes => (int)Math.Round(PrologueVideos.Sum(v => v.Seconds) / 60.0);

        /// <summary>The recap's start, once the New adventure's world is loaded (kdcmp.lua KCD2MP_W159RecapStart).</summary>
        public static string RecapCall() =>
            "if KCD2MP_W159RecapStart then KCD2MP_W159RecapStart(" +
            Lua(string.Join(";", PrologueVideos.Select(v => ShortName(v.Path) + "|" + v.Seconds.ToString("0.0", CultureInfo.InvariantCulture)))) + ") end";

        /// <summary>"Videos/m01/x/x.bk2" -> "m01/x" (the Lua expands it back; one console call holds all nine).</summary>
        public static string ShortName(string path)
        {
            var p = path.Split('/');
            return p.Length == 4 && p[0] == "Videos" && p[3] == p[2] + ".bk2" ? p[1] + "/" + p[2] : path;
        }

        /// <summary>
        /// The host's CONNECT, pressed by the launcher: only after a load the menu's Start Game asked for, once the game's
        /// own log says the world is loaded and has stayed loaded for <see cref="ConnectGateRule.SettleS"/> (the same rule
        /// the CONNECT button waits for, WO-154), and only while the launch still waits for it. A save loaded any other way
        /// keeps the button the player presses.
        /// </summary>
        public static bool HostAutoConnect(bool hosting, bool menuLoad, GameStage stage, double worldSinceS, double nowS, bool waitingForConnect) =>
            hosting && menuLoad && waitingForConnect && stage == GameStage.World
            && !double.IsNaN(worldSinceS) && nowS - worldSinceS >= ConnectGateRule.SettleS;

        /// <summary>A line on the game's own screen (the HUD's info text), for what the player would otherwise only see in the launcher.</summary>
        public static string HudLine(string text) =>
            "pcall(function() UIAction.CallFunction('hud', -1, 'ShowInfoText', " + Lua(text) + ", 10, 15000, true) end)";

        public const string ConnectFailedText =
            "Kingdom Come: Together could not connect. Switch to the launcher window: it says why and what to do.";

        /// <summary>A load from the menu (the WO-124 call, after the menu's video is stopped: WO-154's freeze).</summary>
        public static string LoadCall(int playline, string name) =>
            "if KCD2MP_W159Disarm then KCD2MP_W159Disarm(\"load\") end; if KCD2MP_Wo124LoadGame then KCD2MP_Wo124LoadGame(" +
            playline.ToString(CultureInfo.InvariantCulture) + "," + Lua(name) + ",\"w159\") end";

        /// <summary>The console's own limit (WO-109: past about 2,100 encoded characters the rest is cut, silently).</summary>
        public const int MaxEncoded = 1900;
    }

    /// <summary>
    /// WO-159: the game's console through the Modding Tools' local API (localhost:1403), the same inbound path the
    /// agent uses. Lua is sent with the console's '#' prefix. The game answers 503 while it is busy: retried briefly.
    /// </summary>
    public sealed class GameConsole : IDisposable
    {
        private readonly HttpClient _http = new() { BaseAddress = new Uri("http://127.0.0.1:1403"), Timeout = TimeSpan.FromSeconds(5) };

        public async Task<bool> LuaAsync(string lua, CancellationToken ct = default) => await CommandAsync("#" + lua, ct);

        public async Task<bool> CommandAsync(string command, CancellationToken ct = default)
        {
            string enc = Uri.EscapeDataString(command);
            if (enc.Length > MenuTakeoverRule.MaxEncoded) throw new ArgumentException($"console command too long ({enc.Length} encoded characters)");
            for (int attempt = 0; attempt < 8 && !ct.IsCancellationRequested; attempt++)
            {
                try
                {
                    using var r = await _http.GetAsync("/api/System/Console/ExecuteString?command=" + enc, ct);
                    if (r.StatusCode == HttpStatusCode.ServiceUnavailable) { await Task.Delay(50 + 50 * attempt, ct); continue; }
                    return r.IsSuccessStatusCode;
                }
                catch (HttpRequestException) { return false; }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return false; }
            }
            return false;
        }

        public void Dispose() => _http.Dispose();
    }
}
