// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;

namespace KcdMp.Client;

/// <summary>
/// WO-179 (0.50.7, docs/WO-179-findings.md): the pure rules of shared scenes. WO-183 (0.50.9): videos only -- the relocation, fallback,
/// settle-window, jump-trigger and time-skip rules are gone (the landing's rules are Wo183Rules). GameBridge.Wo179.cs is the bridge; native/KCDMP/wo179.* the
/// DLL's eyes (the fade edges, the relocation watch, the partner's own fade); the Lua half acts (the movie player, the ladder, the ignore
/// window). Wo179Tests pins these.
///   Part A  the scene guard (mp_scene_guard, WO-151's switch): an end-positioning wait bounded at 2 s past the host's (or this machine's)
///           content end, then the black lifted; a conversation's scene that never ends released 2 s after the conversation (WO-167 T4 was
///           20 s); a dialogue that makes no twins in 3.5 s released the game's own way (the field: 20 s each, 6 of 6 on the joiner).
///   Part B  the scene bus (W164 kind 15): which shared scene runs, who triggered it, where Henry ends up, how it ended.
///   Part C  videos both watch (C1), relocations bring the partner along (C2), the quiet exit ladder (C3), the landing and the ignore window (C4).
///   WO-179B one skip ends the video for both (mp_scene_skip_both), the partner is safe while he watches (mp_scene_watch_safe), and he is moved
///           after the video, to where the triggerer settles (the settle report), behind a fade bounded at 3 s.
/// </summary>
public static class Wo179Rules
{
    public const bool DefaultBus = true, DefaultVideo = true, DefaultBring = true, DefaultIgnored = true, DefaultFightClear = true;
    public const bool DefaultSkipBoth = true, DefaultWatchSafe = true;

    // ---- Part A ----
    /// <summary>A1: an end-positioning wait lasts at most this long past the later of the host's and this machine's content end.</summary>
    public const long PositioningBoundMs = 2_000;
    /// <summary>A3 / T4: a conversation's scene still running this long after the conversation ended is released (the WO's ~2 s).</summary>
    public const long ConversationSceneBoundMs = 2_000;
    /// <summary>A3 / T4: after asking the engine, its end is waited this long before the black is lifted instead.</summary>
    public const long ConversationEndWaitMs = 1_000;
    /// <summary>A3: a conversation with this player that made no sign of running in this long cannot start here: released.</summary>
    public const long TwinsBoundMs = 3_500;
    /// <summary>WO-183 2: a sign of a conversation (a twin, a command) seen up to this long before its start attempt still counts (the log tail is not ordered to the ms).</summary>
    public const long SignLeadMs = 1_500;

    /// <summary>
    /// WO-183 2: this player's own dialogue id from the engine's "New dialogue '...(kind - Id: N)...' is starting. Params: souls = 'Ex: Dude; ..."
    /// line, or -1 (another dialogue, another line).
    /// </summary>
    public static int DudeDialogId(string? line)
    {
        if (line is null || !line.StartsWith("New dialogue '", StringComparison.Ordinal)) return -1;
        int s = line.IndexOf("souls = 'Ex: Dude", StringComparison.Ordinal);
        if (s < 0) return -1;
        int id = line.IndexOf(" - Id: ", StringComparison.Ordinal);
        if (id < 0 || id > s) return -1;
        int i = id + 7, n = 0, d = 0;
        while (i < line.Length && char.IsAsciiDigit(line[i]) && d < 9) { n = n * 10 + (line[i] - '0'); i++; d++; }
        return d > 0 ? n : -1;
    }

    /// <summary>
    /// WO-183 2: does this raw engine line show this player's conversation really running (so the A3 guard must not end it)?
    ///   * any line naming a dialogue twin ('DialogTwin_&lt;soul&gt;' -- MasterSlaveManager's), or
    ///   * a "] DialogCommand-" line of this player's dialogue (its id, or the player's alias HRAC; never a gossip's ambient speakers), or
    ///   * an "Interrupting dialog" line of a dialogue with the player (Ex0: Dude) in a state other than WAITING_FOR_TWINS.
    /// The conversations that really stay in WAITING_FOR_TWINS (the field's ttkc_emerich haggles) show none of these.
    /// </summary>
    public static bool ConversationRunning(string? line, int dudeDialogId)
    {
        if (string.IsNullOrEmpty(line)) return false;
        if (line.Contains("DialogTwin_", StringComparison.Ordinal)) return true;
        if (line.Contains("] DialogCommand-", StringComparison.Ordinal))
        {
            if (line.Contains("GOSSIP_", StringComparison.Ordinal) || line.Contains("SOUSEDSKY_", StringComparison.Ordinal)) return false;
            if (line.Contains("HRAC", StringComparison.Ordinal)) return true;
            if (dudeDialogId >= 0 && line.StartsWith("[ID: ", StringComparison.Ordinal))
            {
                int e = line.IndexOf(']');
                if (e > 5 && int.TryParse(line.AsSpan(5, e - 5), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) return id == dudeDialogId;
            }
            return false;
        }
        if (line.StartsWith("Interrupting dialog '", StringComparison.Ordinal) && line.Contains("[Ex0: Dude ", StringComparison.Ordinal))
        {
            int st = line.IndexOf(" state: ", StringComparison.Ordinal);
            if (st < 0) return false;
            int from = st + 8, end = from;
            while (end < line.Length && (char.IsAsciiLetterOrDigit(line[end]) || line[end] == '_')) end++;
            return end > from && !line.AsSpan(from, end - from).SequenceEqual("WAITING_FOR_TWINS");
        }
        return false;
    }

    // ---- Part C ----
    /// <summary>C1: the partner's game waits this long for its own copy of the video (the shared quest graph usually plays it) before it plays one.</summary>
    public const long VideoOwnCopyWaitMs = 1_000;
    /// <summary>C4: the ignore window's length (the WO's 10 minutes); it also ends when the partner walks 60 m away (the Lua).</summary>
    public const int IgnoreWindowS = 600;
    /// <summary>C3: a dead or downed partner is waited for this long (then he is brought along when he stands); the scene does not wait.</summary>
    public const long DownWaitMaxMs = 180_000;
    /// <summary>The partner's own fade around the move (the move itself is ~1 frame; the fade hides the ladder).</summary>
    public const float OwnFadeS = 0.5f;

    // ---- WO-179B ----
    /// <summary>The partner's black after the video: never longer than this in total.</summary>
    public const long PartnerFadeMaxMs = 3_000;
    /// <summary>The partner's black after the video: at most this long past the triggerer's settle report (its own post-video fade + 1 s).</summary>
    public const long PartnerFadePastSettleMs = 1_000;
    /// <summary>The partner's fade to black at a video's end: as short as the fader takes (a flash of at most a frame or two).</summary>
    public const float VideoEndFadeS = 0.05f;
    /// <summary>The fade back in after the landing.</summary>
    public const float VideoLandFadeInS = 0.3f;
    /// <summary>A mod move of the local player (the DLL's own-teleport mark) holds the copy's skip check this long, then the pose is taken again.</summary>
    public const long OwnMoveHoldMs = 1_000;
    /// <summary>The ground under the landed partner is confirmed by two reads this far apart with the height steady to this much.</summary>
    public const long GroundReadGapMs = 150;
    public const float GroundSteadyM = 0.2f;

    /// <summary>When the partner's black must lift: 3 s after it began, or 1 s past the settle report if that came first (0 = not yet).</summary>
    public static long PartnerFadeDeadline(long fadeStartMs, long settleArrivedMs) =>
        settleArrivedMs > 0 ? Math.Min(fadeStartMs + PartnerFadeMaxMs, Math.Max(settleArrivedMs, fadeStartMs) + PartnerFadePastSettleMs) : fadeStartMs + PartnerFadeMaxMs;

    /// <summary>Item 1: the game's own skip key arms on the first press and skips on a second press within this long (WHGame's cutscene
    /// interaction, a fixed 4.0 s; read statically, not live-proven) -- a first press alone is no skip.</summary>
    public const long SkipArmMs = 4_000;

    /// <summary>Item 1: this press is the game's skip (a second press within SkipArmMs of the one before it).</summary>
    public static bool SkipConfirmed(long prevPressMs, long nowMs) => prevPressMs > 0 && nowMs - prevPressMs >= 0 && nowMs - prevPressMs <= SkipArmMs;

    /// <summary>Item 3: the copy ended because of the triggerer (its end, its skip, the length) -- the partner's black and the move follow.</summary>
    public static bool CopyEndFades(string why) => why is "partner-ended" or "ended";

    /// <summary>A crime punishment (an arrest, the pillory, jail) is out of scope: detected and logged only.</summary>
    public static bool IsCrimeScene(string? name) => name is not null && name.StartsWith("crime_", StringComparison.OrdinalIgnoreCase);

    /// <summary>The kind letter on the wire: V = a pre-rendered video (R, a scene that moved Henry, is read from an older build and ignored).</summary>
    public static char KindOf(string type) => string.Equals(type, "Rendered", StringComparison.Ordinal) ? 'V' : 'R';

    // ---- the bus text (W164 kind 15, at most Protocol.W164TextMax = 80 characters) ----
    public readonly record struct Msg(char Op, int Seq, char Kind, string Name, float X, float Y, float Z, string How, double Value);

    public static string Start(int seq, char kind, string name) => $"S {seq} {kind} {name}";
    public static string Dest(int seq, float x, float y, float z) => FormattableString.Invariant($"R {seq} {x:F1} {y:F1} {z:F1}");
    public static string End(int seq, string how) => $"E {seq} {how}";
    public static string SkipKey(int seq) => $"K {seq}";
    public static string Landed(int seq, float x, float y, float z) => FormattableString.Invariant($"L {seq} {x:F1} {y:F1} {z:F1}");
    /// <summary>WO-179B: the joiner watches the host's video here -- the host's world protects his figure (fight clear + ignore).</summary>
    public static string Watch(int seq, float x, float y, float z) => FormattableString.Invariant($"W {seq} {x:F1} {y:F1} {z:F1}");

    public static bool IsName(string? s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > 64) return false;
        foreach (char c in s) if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.')) return false;
        return true;
    }

    public static bool TryParse(string? text, out Msg m)
    {
        m = default;
        var f = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length < 2 || f[0].Length != 1 || !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seq) || seq < 0) return false;
        var inv = CultureInfo.InvariantCulture;
        switch (f[0][0])
        {
            case 'S':
                if (f.Length != 4 || f[2] is not ("V" or "R") || !IsName(f[3])) return false;
                m = new Msg('S', seq, f[2][0], f[3], 0, 0, 0, "", 0); return true;
            case 'R':
            case 'L':
            case 'W':
                if (f.Length != 5 || !float.TryParse(f[2], NumberStyles.Float, inv, out float x) || !float.TryParse(f[3], NumberStyles.Float, inv, out float y)
                    || !float.TryParse(f[4], NumberStyles.Float, inv, out float z) || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) return false;
                m = new Msg(f[0][0], seq, ' ', "", x, y, z, "", 0); return true;
            case 'E':
                if (f.Length != 3 || f[2] is not ("end" or "skip" or "interrupt" or "load")) return false;
                m = new Msg('E', seq, ' ', "", 0, 0, 0, f[2], 0); return true;
            case 'K':
                if (f.Length != 2) return false;
                m = new Msg('K', seq, ' ', "", 0, 0, 0, "", 0); return true;
            case 'T':
                if (f.Length != 3 || !double.TryParse(f[2], NumberStyles.Float, inv, out double d) || d < 0 || d > 7 * 86400) return false;
                m = new Msg('T', seq, ' ', "", 0, 0, 0, "", d); return true;
            default:
                return false;
        }
    }

    public static float Dist2D(float ax, float ay, float bx, float by) => MathF.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    /// <summary>A1: when the end-positioning wait is over (the later content end + the bound).</summary>
    public static long PositioningDeadline(long ownEndMs, long hostEndMs) => Math.Max(ownEndMs, hostEndMs) + PositioningBoundMs;

    // ---- C1: the game's own videos (the cutscene table's RenderedCutscene rows: name -> Root/FileName.bk2, length from the Bink header) ----
    private static readonly Dictionary<string, (string Path, double Secs)> Videos = Load();

    public static bool TryVideo(string name, out string path, out double secs)
    {
        path = ""; secs = 0;
        if (!Videos.TryGetValue(name, out var v)) return false;
        path = v.Path; secs = v.Secs;
        return true;
    }

    public static int VideoCount => Videos.Count;

    private static Dictionary<string, (string, double)> Load()
    {
        var d = new Dictionary<string, (string, double)>(StringComparer.Ordinal);
        foreach (var line in VideoTable.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = line.Split('|');
            if (p.Length == 3 && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double s)) d[p[0]] = (p[1], s);
        }
        return d;
    }

    // generated from the game's Libs/Tables/ui/cutscene.xml and the videos' Bink headers (menu and startup videos left out)
    private const string VideoTable = """
        intro_new_game|Videos/m50/cin_m5010k_obranabohuta__siege_intro_start/cin_m5010k_obranabohuta__siege_intro_start.bk2|199.5
        story_switch_to_trosecko|Videos/m01/cin_m0110t_prepadeni__intro_cutscene/cin_m0110t_prepadeni__intro_cutscene.bk2|169.7
        m03_trosky_journey|Videos/m03/cin_m0310t_socky__trosky_journey/cin_m0310t_socky__trosky_journey.bk2|172.7
        bohutovaVlozka_renderedIntroPart|Videos/m10/cin_m1010t_bohutovavlozka__bohuta_arrival/cin_m1010t_bohutovavlozka__bohuta_arrival.bk2|70.4
        bohutovaVlozka_renderedPistaReleasePart|Videos/m10/cin_m1050t_bohutovavlozka__pista_release_clip01/cin_m1050t_bohutovavlozka__pista_release_clip01.bk2|20.7
        erik_nocNaHradbach_renderPart1|Videos/m47/cin_m4710k_erik__night_walls_clip01/cin_m4710k_erik__night_walls_clip01.bk2|7.5
        erik_nocNaHradbach_renderPart2|Videos/m47/cin_m4710k_erik__night_walls_clip02/cin_m4710k_erik__night_walls_clip02.bk2|15.1
        erik_prijezdNaKopec_renderPart1|Videos/m47/cin_m4730k_erik__arrival_duel_clip01/cin_m4730k_erik__arrival_duel_clip01.bk2|14.6
        erik_porazkaErikaAPrijezdArmady_renderPart1|Videos/m47/cin_m4740k_erik__erik_loses_clip01/cin_m4740k_erik__erik_loses_clip01.bk2|12.2
        erik_porazkaErikaAPrijezdArmady_renderPart2|Videos/m47/cin_m4740k_erik__erik_loses_clip02/cin_m4740k_erik__erik_loses_clip02.bk2|2.6
        erik_porazkaErikaAPrijezdArmady_renderPart3|Videos/m47/cin_m4740k_erik__erik_loses_clip03/cin_m4740k_erik__erik_loses_clip03.bk2|2.6
        erik_prazaneTahnouNaSuchdol_renderPart1|Videos/m47/cin_m4880k_oblehanisuchdol__court_seized_clip01/cin_m4880k_oblehanisuchdol__court_seized_clip01.bk2|25.6
        erik_prazaneTahnouNaSuchdol_renderPart2|Videos/m47/cin_m4880k_oblehanisuchdol__court_seized_clip02/cin_m4880k_oblehanisuchdol__court_seized_clip02.bk2|13.8
        erik_prazaneTahnouNaSuchdol_renderPart3|Videos/m47/cin_m4880k_oblehanisuchdol__court_seized_clip03/cin_m4880k_oblehanisuchdol__court_seized_clip03.bk2|22.0
        erik_prazaneTahnouNaSuchdol_renderPart4|Videos/m47/cin_m4880k_oblehanisuchdol__court_seized_clip04/cin_m4880k_oblehanisuchdol__court_seized_clip04.bk2|12.3
        erik_prazaneTahnouNaSuchdol_renderPart5|Videos/m47/cin_m4880k_oblehanisuchdol__court_seized_clip05/cin_m4880k_oblehanisuchdol__court_seized_clip05.bk2|20.1
        erik_prazaneTahnouNaSuchdol_renderPart6|Videos/m47/cin_m4880k_oblehanisuchdol__court_seized_clip06/cin_m4880k_oblehanisuchdol__court_seized_clip06.bk2|9.1
        erik_prazaneTahnouNaSuchdol_renderPart7|Videos/m47/cin_m4880k_oblehanisuchdol__court_seized_clip07/cin_m4880k_oblehanisuchdol__court_seized_clip07.bk2|6.6
        erik_prazaneTahnouNaSuchdol_renderPart8|Videos/m47/cin_m4880k_oblehanisuchdol__court_seized_clip08/cin_m4880k_oblehanisuchdol__court_seized_clip08.bk2|13.9
        finale_jostArmy|Videos/m51/cin_m5110k_finale__jost_army/cin_m5110k_finale__jost_army.bk2|319.1
        finale_acceptSwordEndingScene_renderPart|Videos/m51/cin_m5150k_finale__ending_scene/cin_m5150k_finale__ending_scene.bk2|13.8
        finale_henryReturnsToTheresa|Videos/m51/cin_m5165k_finale__theresa_ending/cin_m5165k_finale__theresa_ending.bk2|37.7
        kubaparalu_MurdererRevealed_rendered|Videos/s36/cin_s3660k_kubaparalu__murderer_revealed/cin_s3660k_kubaparalu__murderer_revealed.bk2|13.9
        naTroskach_ptacekExecution_renderPart1|Videos/m06/cin_m0620t_natroskach__ptacek_execution_p02/cin_m0620t_natroskach__ptacek_execution_p02.bk2|72.0
        naTroskach_ptacekExecution_renderPart2|Videos/m06/cin_m0620t_natroskach__ptacek_execution_p03/cin_m0620t_natroskach__ptacek_execution_p03.bk2|11.7
        nebakovObrana_nightmare_render|Videos/m11/cin_m1110t_nebakovobrana__nebakov_dream/cin_m1110t_nebakovobrana__nebakov_dream.bk2|49.9
        nebakovObrana_enemyArmy_renderPart2|Videos/m11/cin_m1120t_nebakovobrana__enemy_army_p02/cin_m1120t_nebakovobrana__enemy_army_p02.bk2|25.6
        nebakovObrana_enemyArmy_renderPart4|Videos/m11/cin_m1120t_nebakovobrana__enemy_army_p04/cin_m1120t_nebakovobrana__enemy_army_p04.bk2|13.3
        nebakovObrana_startBattle_renderPart1|Videos/m11/cin_m1130t_nebakovobrana__battle_start_clip01/cin_m1130t_nebakovobrana__battle_start_clip01.bk2|24.0
        nebakovObrana_godFinger_renderPart1|Videos/m11/cin_m1140t_nebakovobrana__god_finger_clip01/cin_m1140t_nebakovobrana__god_finger_clip01.bk2|7.5
        nebakovObrana_godFinger_renderPart1b|Videos/m11/cin_m1140t_nebakovobrana__god_finger_clip01b/cin_m1140t_nebakovobrana__god_finger_clip01b.bk2|4.3
        nebakovObrana_godFinger_renderPart2|Videos/m11/cin_m1140t_nebakovobrana__god_finger_clip02/cin_m1140t_nebakovobrana__god_finger_clip02.bk2|8.8
        nebakovObrana_godFinger_renderPart3|Videos/m11/cin_m1140t_nebakovobrana__god_finger_clip03/cin_m1140t_nebakovobrana__god_finger_clip03.bk2|2.0
        nebakovObrana_godFinger_renderPart4|Videos/m11/cin_m1140t_nebakovobrana__god_finger_clip04/cin_m1140t_nebakovobrana__god_finger_clip04.bk2|2.7
        nebakovObrana_godFinger_renderPart5|Videos/m11/cin_m1140t_nebakovobrana__god_finger_clip05/cin_m1140t_nebakovobrana__god_finger_clip05.bk2|5.5
        nebakovObrana_godFinger_renderPart6|Videos/m11/cin_m1140t_nebakovobrana__god_finger_clip06/cin_m1140t_nebakovobrana__god_finger_clip06.bk2|1.8
        oblehaniSuchdole_zacinaOblehaniCasosber|Videos/m48/cin_m4820k_oblehanisuchdol__siege_begins/cin_m4820k_oblehanisuchdol__siege_begins.bk2|18.0
        prijezdNaSuchdol_arrivalCutscene_renderPart|Videos/m31/cin_m3110k_prijezdnasuchdol__arrival_suchdol_clip01/cin_m3110k_prijezdnasuchdol__arrival_suchdol_clip01.bk2|64.4
        prijezdNaSuchdol_pistaFlashback|Videos/m31/cin_m3115k_prijezdnasuchdol__dream_pista/cin_m3115k_prijezdnasuchdol__dream_pista.bk2|93.6
        svatba_intermezzoBohuta_renderPart|Videos/m05/cin_m0540t_svatba__intermezzo_bohuta/cin_m0540t_svatba__intermezzo_bohuta.bk2|16.9
        truhlari_BurnPainting_render|Videos/s70/cin_s7030k_brushes__burning_altarpiece/cin_s7030k_brushes__burning_altarpiece.bk2|12.0
        uchazec_forgeFlashbackRender|Videos/s80/cin_s8010k_forge__forge_flashback/cin_s8010k_forge__forge_flashback_clip01.bk2|45.3
        utokNaMalesov_pistaDreamGood_render|Videos/m44/cin_m4460k_malesov__dream_good/cin_m4460k_malesov__dream_good.bk2|117.2
        utokNaMalesov_pistaDreamEvil_render|Videos/m44/cin_m4470k_malesov__dream_evil/cin_m4470k_malesov__dream_evil.bk2|129.3
        utokNaNebakov_cutscene_march_render|Videos/m09/cin_m0910t_utoknebakov__nebakov_march/cin_m0910t_utoknebakov__nebakov_march.bk2|89.9
        utokNaNebakov_valley_afterDuel_render|Videos/m09/cin_m0930t_utoknebakov__after_duel/cin_m0930t_utoknebakov__after_duel.bk2|44.2
        vezniNaTroskach_prisonersConvoy_rendered|Videos/m12/cin_m1210t_vezninatroskach__prisoners_convoy/cin_m1210t_vezninatroskach__prisoners_convoy_clip01.bk2|9.2
        vezniNaTroskach_pistaChamberIntro_renderPart|Videos/m12/cin_m1240t_vezninatroskach__pista_chamber_01/cin_m1240t_vezninatroskach__pista_chamber_01.bk2|5.8
        vezniNaTroskach_stormDream|Videos/m12/cin_m1290t_vezninatroskach__storm_dream/cin_m1290t_vezninatroskach__storm_dream.bk2|295.2
        zachrana_probuzeni_data1|Videos/m02/cin_m0250t_zachrana__first_dreaming_clip_01/cin_m0250t_zachrana__first_dreaming_clip_01.bk2|104.9
        zachrana_prespani_data1|Videos/m02/cin_m0260t_zachrana__second_dreaming_clip01/cin_m0260t_zachrana__second_dreaming_clip01.bk2|16.3
        zachrana_prespani_data2|Videos/m02/cin_m0260t_zachrana__second_dreaming_clip02/cin_m0260t_zachrana__second_dreaming_clip02.bk2|25.2
        zachrana_prespani_data3|Videos/m02/cin_m0260t_zachrana__second_dreaming_clip03/cin_m0260t_zachrana__second_dreaming_clip03.bk2|19.8
        zachrana_fall_dream|Videos/m02/cin_m0210t_zachrana__fall_dream_clip01/cin_m0210t_zachrana__fall_dream_clip01.bk2|149.9
        zoufalaObranaZaBohutu_battleOpeningCutsceneGameBeginning|Videos/m50/cin_m5010k_obranabohuta__siege_intro_start/cin_m5010k_obranabohuta__siege_intro_start.bk2|199.5
        zoufalaObranaZaBohutu_battleOpeningCutsceneGameEnd|Videos/m50/cin_m5010k_obranabohuta__siege_intro_end/cin_m5010k_obranabohuta__siege_intro_end.bk2|122.8
        zoufalaObranaZaBohutu_battleEndingCutsceneGameBeginning|Videos/m50/cin_m5020k_obranabohuta__siege_finale_start/cin_m5020k_obranabohuta__siege_finale_start.bk2|123.3
        zoufalaObranaZaBohutu_battleEndingCutsceneGameEnd|Videos/m50/cin_m5020k_obranabohuta__siege_finale_end/cin_m5020k_obranabohuta__siege_finale_end.bk2|190.3
        crime_pillory_trosecko_firstRun|Videos/s99/cin_s9903t_crime__pillory_troskovice/cin_s9903t_crime__pillory_troskovice.bk2|57.9
        crime_pillory_trosecko_var1|Videos/s99/cin_s9903t_crime__pillory_troskovice/cin_s9903t_crime__pillory_troskovice.bk2|57.9
        crime_pillory_trosecko_var2|Videos/s99/cin_s9903t_crime__pillory_troskovice/cin_s9903t_crime__pillory_troskovice.bk2|57.9
        crime_pillory_trosecko_var3|Videos/s99/cin_s9903t_crime__pillory_troskovice/cin_s9903t_crime__pillory_troskovice.bk2|57.9
        crime_pillory_trosecko_var4|Videos/s99/cin_s9903t_crime__pillory_troskovice/cin_s9903t_crime__pillory_troskovice.bk2|57.9
        crime_execution_trosecko|Videos/s99/cin_s9912t_crime__beheading_troskovice/cin_s9912t_crime__beheading_troskovice.bk2|105.8
        crime_pillory_kutnahora_firstRun|Videos/s99/cin_s9915k_crime__pillory_kutnahora/cin_s9915k_crime__pillory_kutnahora.bk2|57.9
        crime_pillory_kutnahora_var1|Videos/s99/cin_s9915k_crime__pillory_kutnahora/cin_s9915k_crime__pillory_kutnahora.bk2|57.9
        crime_execution_kutnahora|Videos/s99/cin_s9924k_crime__beheading_kutnahora/cin_s9924k_crime__beheading_kutnahora.bk2|105.8
        crime_pillory_suchdol_firstRun|Videos/s99/cin_s9927k_crime__pillory_suchdol/cin_s9927k_crime__pillory_suchdol.bk2|57.9
        crime_pillory_suchdol_var1|Videos/s99/cin_s9927k_crime__pillory_suchdol/cin_s9927k_crime__pillory_suchdol.bk2|57.9
        crime_pillory_malesov_firstRun|Videos/s99/cin_s9939k_crime__pillory_cervenepecky/cin_s9939k_crime__pillory_cervenepecky.bk2|57.9
        crime_pillory_malesov_var1|Videos/s99/cin_s9939k_crime__pillory_cervenepecky/cin_s9939k_crime__pillory_cervenepecky.bk2|57.9
        crime_pillory_miskovice_firstRun|Videos/s99/cin_s9951k_crime__pillory_miskovice/cin_s9951k_crime__pillory_miskovice.bk2|57.9
        crime_pillory_miskovice_var1|Videos/s99/cin_s9951k_crime__pillory_miskovice/cin_s9951k_crime__pillory_miskovice.bk2|57.9
        crime_pillory_zikmundtabor_firstRun|Videos/s99/cin_s9963k_crime__pillory_zikmundtabor/cin_s9963k_crime__pillory_zikmundtabor.bk2|57.9
        crime_pillory_zikmundtabor_var1|Videos/s99/cin_s9963k_crime__pillory_zikmundtabor/cin_s9963k_crime__pillory_zikmundtabor.bk2|57.9
        puvodNemoci_initialMonasteryCutscene_clip01|Videos/s90/cin_s9010r_monastery__monastery_intro_clip01/cin_s9010r_monastery__monastery_intro_clip01.bk2|10.1
        puvodNemoci_initialMonasteryCutscene_clip02|Videos/s90/cin_s9010r_monastery__monastery_intro_clip02/cin_s9010r_monastery__monastery_intro_clip02.bk2|8.1
        puvodNemoci_closingMonastery_clip01|Videos/s90/cin_s9020r_monastery__closing_monastery_clip01/cin_s9020r_monastery__closing_monastery_clip01.bk2|183.5
        predaniVChramu_sick_ending_clip01|Videos/s90/cin_s9070r_monastery__sick_ending_clip01/cin_s9070r_monastery__sick_ending_clip01.bk2|10.2
        predaniVChramu_sick_ending_clip02|Videos/s90/cin_s9070r_monastery__sick_ending_clip02/cin_s9070r_monastery__sick_ending_clip02.bk2|5.7
        predaniVChramu_sick_ending_clip03|Videos/s90/cin_s9070r_monastery__sick_ending_clip03/cin_s9070r_monastery__sick_ending_clip03.bk2|4.8
        predaniVChramu_healed_ending_clip01|Videos/s90/cin_s9065r_monastery__healed_ending/cin_s9065r_monastery__healed_ending_p03.bk2|16.9
""";
}
