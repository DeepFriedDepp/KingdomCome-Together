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
        public sealed record MenuChoice(long Seq, string Kind, int Playline = -1, string Name = "", string Join = "");

        public static MenuChoice? Parse(string line)
        {
            const string pfx = "[KCD2-MP-EVT] v1 ";
            int at = line.IndexOf(pfx, StringComparison.Ordinal);
            if (at < 0) return null;
            var p = line[(at + pfx.Length)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 3 || p[1] != "w159" || !long.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out long seq)) return null;
            switch (p[2])
            {
                case "newadv": return new(seq, "newadv");
                case "cancel": return new(seq, "cancel");
                case "join" when p.Length >= 4 && p[3] is "fresh" or "bring": return new(seq, "join", Join: p[3]);
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
                                       string joinState, bool bring, string bringMsg)
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
            b.Append(",join={state=").Append(Lua(joinState)).Append(",bring=").Append(bring ? "true" : "false").Append(",msg=").Append(Lua(bringMsg)).Append('}');
            string body = b.ToString();
            string sig = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)))[..12].ToLowerInvariant();
            return "if KCD2MP_W159Model then KCD2MP_W159Model({sig=" + Lua(sig) + "," + body + "}) end";
        }

        public const string TickCall = "if KCD2MP_W159Tick then KCD2MP_W159Tick() end";

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
