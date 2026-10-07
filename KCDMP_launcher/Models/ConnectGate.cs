// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace KCDMP_launcher.Models
{
    /// <summary>Where the game this launcher started is, by its own kcd.log.</summary>
    public enum GameStage { Starting, Menu, Loading, World, Quitting }

    /// <summary>What the host's relay says about the host (the joiner's connection test).</summary>
    public enum HostAnswer { Pending, NotRunning, Ready, Unknown }

    public readonly record struct GateView(bool Open, string Reason);

    /// <summary>
    /// WO-154 (Phase 4.3): CONNECT waits until it can work, and says why in plain words.
    ///
    /// The rule:
    /// <list type="bullet">
    /// <item>The host's CONNECT opens once the game's own log says a world is loaded ("Gameplay
    ///   started", the line the agent also waits for) and nothing has loaded since for
    ///   <see cref="SettleS"/> seconds -- the agent's own settle after a load (a join asked of the
    ///   host sooner is deferred by the agent: GameBridge.Wo123 JoinSettleAfterLoadS). At the main
    ///   menu, or before it: "Load your save first."; while a save loads, and during the settle:
    ///   "Waiting for your world to finish loading...".</item>
    /// <item>The joiner's CONNECT opens once the host's relay says the host's own agent is in
    ///   (the connection test's host flag) -- and the host's agent is only started by the host's
    ///   own gate, so that means the host is in the world. Until then: "Waiting for your host to be
    ///   ready...". A joiner otherwise connects first and the relay makes the joiner the authority
    ///   (2026-10-02: a joiner's agent at the main menu reported role=host for eight minutes
    ///   before its host pressed CONNECT).</item>
    /// <item>It blocks only on evidence. The game's log not readable within
    ///   <see cref="UnreadableOpenAfterS"/> s, or a relay that cannot say: the button opens
    ///   (logged) -- never a dead button.</item>
    /// </list>
    /// Before WO-154 CONNECT was enabled the moment WHGame.dll loaded, with a line asking the host
    /// to wait until the character can move. Too early, the plugin can find no ticking game and
    /// only a game restart helps (VerifyInjectionAsync); at the main menu the host's agent connects
    /// with no world for a joiner to receive.
    /// </summary>
    public static class ConnectGateRule
    {
        public const double SettleS = 10;
        public const double UnreadableOpenAfterS = 45;

        public const string LoadSaveFirst = "Load your save first.";
        public const string WorldLoading = "Waiting for your world to finish loading...";
        public const string HostNotReady = "Waiting for your host to be ready...";

        public static GateView ForHost(GameStage stage, double worldSinceS, bool logRead, double sinceGameStartS, double nowS)
        {
            if (!logRead)
                return sinceGameStartS >= UnreadableOpenAfterS ? new GateView(true, "") : new GateView(false, LoadSaveFirst);
            return stage switch
            {
                GameStage.Loading => new GateView(false, WorldLoading),
                GameStage.World when nowS - worldSinceS < SettleS => new GateView(false, WorldLoading),
                GameStage.World => new GateView(true, ""),
                _ => new GateView(false, LoadSaveFirst),   // Starting, Menu, Quitting
            };
        }

        public static GateView ForJoiner(HostAnswer host) => host switch
        {
            HostAnswer.Pending or HostAnswer.NotRunning => new GateView(false, HostNotReady),
            _ => new GateView(true, ""),   // ready -- or the relay cannot say: the agent's own flow takes it from there
        };

        /// <summary>The joiner's connection test, as the gate reads it: only "relay up, no host agent" holds the button.</summary>
        public static HostAnswer FromTest(bool reachable, string kind, bool? hostConnected)
        {
            if (reachable && hostConnected == true) return HostAnswer.Ready;
            if (reachable && (hostConnected == false || kind == "HostNotRunning")) return HostAnswer.NotRunning;
            return HostAnswer.Unknown;
        }
    }

    /// <summary>
    /// WO-154: the stage the game is at, from the lines of its kcd.log -- the same markers the
    /// agent reads (LogTailGameTransport): the main menu's video, a save load's three steps,
    /// "Gameplay started", the failed-load return to the menu, the quit.
    /// </summary>
    public sealed class GameLogStage
    {
        public GameStage Stage { get; private set; } = GameStage.Starting;
        /// <summary>When the last "Gameplay started" was read (seconds on the caller's clock); NaN before.</summary>
        public double WorldSinceS { get; private set; } = double.NaN;

        public void Feed(string line, double nowS)
        {
            if (line.StartsWith("PlayVideoOnly 'main_menu", StringComparison.Ordinal)
                || line.StartsWith("Exiting to main menu because save game loading failed", StringComparison.Ordinal))
            {
                Stage = GameStage.Menu;
                WorldSinceS = double.NaN;
            }
            else if (line.StartsWith("Loading saved game", StringComparison.Ordinal)
                     || line.StartsWith("[CryAction] LoadGame:", StringComparison.Ordinal)
                     || (line.StartsWith("====", StringComparison.Ordinal) && line.Contains(" Loading level ", StringComparison.Ordinal)))
            {
                Stage = GameStage.Loading;
            }
            else if (line.Length < 40 && line.StartsWith("Gameplay started", StringComparison.Ordinal))
            {
                Stage = GameStage.World;
                WorldSinceS = nowS;
            }
            else if (line.StartsWith("CSystem::Quit invoked", StringComparison.Ordinal))
            {
                Stage = GameStage.Quitting;
            }
        }
    }

    /// <summary>
    /// WO-154: follows the kcd.log of the game this launcher started. The engine starts a new
    /// kcd.log at each game start; until the file in place is this game's ("Log Started at" no
    /// earlier than the start, less <see cref="StartSlack"/>), nothing is read from it -- the
    /// previous game's log ends at its main menu or in its world and would open the gate.
    /// </summary>
    public sealed class KcdLogFollower
    {
        public static readonly TimeSpan StartSlack = TimeSpan.FromSeconds(30);
        private static readonly Regex Header = new(@"Log Started at (\d{4}-\d{2}-\d{2})\s+(\d{2}:\d{2}:\d{2})", RegexOptions.CultureInvariant);

        private readonly string _path;
        private readonly DateTime _gameStartLocal;
        private long _offset;
        private bool _ours;
        private readonly StringBuilder _partial = new();
        private string? _tailFed;   // the unfinished last line, as already read
        private readonly Decoder _utf8 = Encoding.UTF8.GetDecoder();

        public GameLogStage State { get; private set; } = new();
        /// <summary>WO-159: every line read (after the stage has seen it), for the menu's choices.</summary>
        public Action<string>? OnLine { get; set; }
        /// <summary>This game's log has been found and is being read.</summary>
        public bool Reading => _ours;

        public KcdLogFollower(string path, DateTime gameStartLocal)
        {
            _path = path;
            _gameStartLocal = gameStartLocal;
        }

        /// <summary>The "Log Started at" time in the head of a kcd.log, or null.</summary>
        public static DateTime? StartedAt(string head)
        {
            var m = Header.Match(head);
            if (!m.Success) return null;
            return DateTime.TryParseExact($"{m.Groups[1].Value} {m.Groups[2].Value}", "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var t) ? t : null;
        }

        /// <summary>Reads what the game added since the last poll. Never throws; an unreadable file is tried again next time.</summary>
        public void Poll(double nowS)
        {
            try
            {
                using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length < _offset) Restart();   // a new file (the engine started over)
                if (!_ours)
                {
                    var head = new byte[(int)Math.Min(fs.Length, 8192)];
                    int got = fs.Read(head, 0, head.Length);
                    var started = StartedAt(Encoding.UTF8.GetString(head, 0, got));
                    if (started is null || started.Value < _gameStartLocal - StartSlack) return;   // not this game's (yet)
                    _ours = true;
                    _offset = 0;
                }
                fs.Seek(_offset, SeekOrigin.Begin);
                var buf = new byte[64 * 1024];
                var chars = new char[Encoding.UTF8.GetMaxCharCount(buf.Length)];
                int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    _offset += n;
                    int c = _utf8.GetChars(buf, 0, n, chars, 0);
                    for (int i = 0; i < c; i++)
                    {
                        if (chars[i] == '\n')
                        {
                            string line = _partial.ToString().TrimEnd('\r');
                            if (line != _tailFed) { State.Feed(line, nowS); OnLine?.Invoke(line); }
                            _tailFed = null;
                            _partial.Clear();
                        }
                        else _partial.Append(chars[i]);
                    }
                }
                // The engine writes a line's end only when it writes the next line (WO-124): the last line is
                // read as it stands, once -- the markers are line beginnings, so a cut-off line matches none
                // or the one it is.
                string tail = _partial.ToString().TrimEnd('\r');
                if (tail.Length > 0 && tail != _tailFed)
                {
                    State.Feed(tail, nowS);
                    OnLine?.Invoke(tail);
                    _tailFed = tail;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        private void Restart()
        {
            _ours = false;
            _offset = 0;
            _partial.Clear();
            _tailFed = null;
            _utf8.Reset();
            State = new GameLogStage();
        }
    }
}
