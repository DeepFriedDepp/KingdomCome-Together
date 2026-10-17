// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;

namespace KcdMp.Client;

/// <summary>
/// WO-194 (docs/WO-194-findings.md): the pure rules of shared in-game cutscenes (the common kind, played in the engine with "Skip part /
/// Skip all"). GameBridge.Wo194.cs is the bridge; native/KCDMP/wo194.* the DLL's half. Wo194Tests pins these.
///   A  the end never stalls: on the joiner, from OnCutsceneEnd until OnPositioningFinished, every copy's FastForward is stopped (the
///      engine's own per-NPC call) so the scene positioning finishes; his own Henry is then moved about 1 m off the shared tag point.
///   B  both start together, the quest's way: an in-game cutscene this machine's quest enqueues goes on the scene bus; the other machine
///      starts its own copy through its own quest's CutsceneHandler when that handler is idle (else it is a bystander); the second copy's
///      sequence clock is aligned to the first's; the joiner's own trigger, when it fires first, is held at "initialized" for 3 s at most.
///   C  one skip for both: a skip's jump to the next checkpoint (or its stop) is sent; the partner jumps (or stops) too.
///   D  a scene's own time of day runs once, in the host's world; the joiner's copy does not set it (the host's clock gives it).
/// </summary>
public static class Wo194Rules
{
    public const bool DefaultShared = true, DefaultHold = true, DefaultFfStop = true, DefaultTimeGate = true, DefaultNudge = true,
                      DefaultFigureHide = true, DefaultSkipBoth = true;

    // ---- A ----
    /// <summary>A: from OnCutsceneEnd the copies' FastForward is stopped this often until OnPositioningFinished.</summary>
    public const long FfStopEveryMs = 250;
    /// <summary>A: ... for at most this long (the WO's live bound for a scene on copies is 2 s; this is the window's end).</summary>
    public const long FfStopWindowMs = 5_000;
    /// <summary>A: the joiner's Henry is moved this far off the tag point both Henrys are placed on.</summary>
    public const float NudgeM = 1.0f;
    /// <summary>A: the nudge is only for a scene the host played too (only then do both Henrys get the same tag point).</summary>
    public static bool NudgeWanted(bool joiner, bool on, bool hostPlayedIt) => joiner && on && hostPlayedIt;

    // ---- B3: the clock ----
    /// <summary>B3: each side sends its sequence time this often while its shared copy plays.</summary>
    public const long ClockEveryMs = 1_000;
    /// <summary>B3: the first alignment is skipped below this (a frame or two).</summary>
    public const double FirstSetMinS = 0.05;
    /// <summary>B3: after the first alignment, the clock is set again only past this drift (the WO's ~0.2 s).</summary>
    public const double DriftS = 0.2;
    /// <summary>A partner's clock older than this is not used.</summary>
    public const long ClockStaleMs = 3_000;

    /// <summary>B3: where this copy should be now -- the partner's time plus the one-way delay plus the time since it arrived here.</summary>
    public static double AlignTargetS(double partnerS, long oneWayMs, long sinceArrivalMs) =>
        partnerS + Math.Max(0, oneWayMs) / 1000.0 + Math.Max(0, sinceArrivalMs) / 1000.0;

    /// <summary>B3: set the clock now? The first time past a frame or two; after that only past the drift.</summary>
    public static bool NeedsSet(double ownS, double targetS, bool firstDone) =>
        double.IsFinite(ownS) && double.IsFinite(targetS) && ownS >= 0 && targetS >= 0 && Math.Abs(targetS - ownS) > (firstDone ? DriftS : FirstSetMinS);

    /// <summary>B3: this copy follows the partner's clock when the partner's sequence ran first (its "runs" arrived before this one ran).</summary>
    public static bool IsFollower(long ownPlayMs, long partnerRunsArrivedMs) => partnerRunsArrivedMs > 0 && (ownPlayMs == 0 || partnerRunsArrivedMs <= ownPlayMs);

    // ---- C: a skip ----
    /// <summary>C: the sequence time is read this long after a skip key press (the game jumped in the press's own frame).</summary>
    public const long SkipReadDelayMs = 150;
    /// <summary>C: a read this far past where the clock would be is a jump to the next checkpoint (not the clock running).</summary>
    public const double JumpMinS = 0.8;

    public enum SkipSeen { None, Jump, Stopped }

    /// <summary>C: what a skip key press did: the sequence no longer plays = stopped; its time past the prediction = a jump.</summary>
    public static SkipSeen Classify(bool stillPlaying, double afterS, double lastS, long lastAgeMs)
    {
        if (!stillPlaying) return SkipSeen.Stopped;
        if (lastS < 0 || !double.IsFinite(afterS)) return SkipSeen.None;
        double predicted = lastS + Math.Max(0, lastAgeMs) / 1000.0;
        return afterS - predicted > JumpMinS ? SkipSeen.Jump : SkipSeen.None;
    }

    // ---- B2: the answers ----
    public const int AnsBystander = 0, AnsStarted = 1, AnsOwnAlready = 2, AnsRefused = 3;
    public static string AnswerName(int a) => a switch
    {
        AnsStarted => "started its own copy through its quest",
        AnsOwnAlready => "its own quest already plays it",
        AnsBystander => "its quest is not at that step (a bystander)",
        _ => "refused (switched off, not in the world, or the start failed)",
    };

    /// <summary>
    /// B2: why this machine does not start its own copy of the partner's scene (null = it does). Live: a CutsceneHandler goes back to idle
    /// (state 0, awake) after its scene played, so "idle" alone does not say the quest is at that step -- a scene this machine already played
    /// since its last load is never started again (the partner's own replay of it, after a reload, comes with a load here too).
    /// </summary>
    public static string? StartRefusal(bool shared, bool inWorld, bool crimeScene, bool playedSinceLoad) =>
        !shared ? "mp_scene_ingame_shared off"
        : !inWorld ? "not in the world (loading or joining)"
        : crimeScene ? "a crime scene is never shared"
        : playedSinceLoad ? "this machine already played it since its last load (its handler is idle again; not at that step)"
        : null;

    /// <summary>B2: the DLL's start result (op 61) as the answer the partner gets.</summary>
    public static int AnswerOfStart(byte result) => result switch { 1 => AnsStarted, 0 or 2 or 7 => AnsBystander, _ => AnsRefused };

    public static string StartResultName(byte r) => r switch
    {
        1 => "started", 0 => "handler not idle", 2 => "no such CutsceneHandler", 3 => "fault", 4 => "port refused", 5 => "not armed",
        6 => "fired, no enqueue followed", 7 => "hibernated", _ => $"result {r}",
    };

    // ---- the bus text (W164 kind 15, at most Protocol.W164TextMax = 80 characters; appended to WO-179's letters) ----
    public readonly record struct Msg(char Op, int Seq, string Name, string Path, int Code, int TimeMs);

    public static string Started(int seq, string name) => $"C {seq} {name}";
    public static string Handler(int seq, string path) => $"Q {seq} {path}";
    public static string Answer(int seq, int code, string name) => $"A {seq} {code} {name}";
    public static string Runs(int seq, string name) => $"G {seq} {name}";
    public static string Clock(int seq, int timeMs, string name) => FormattableString.Invariant($"P {seq} {timeMs} {name}");
    public static string Jumped(int seq, int timeMs, string name) => FormattableString.Invariant($"J {seq} {timeMs} {name}");
    public static string Stopped(int seq, string name) => $"X {seq} {name}";
    public static string Ended(int seq, string name) => $"Z {seq} {name}";

    /// <summary>The handler's concept path (up to 200 characters) in pieces that each fit one message ("Q seq piece"); the receiver joins
    /// the pieces of one (sender, seq) in order (the relay keeps a sender's order). Empty when the path is not a path.</summary>
    public static List<string> HandlerPieces(int seq, string path)
    {
        var list = new List<string>();
        if (!IsPath(path)) return list;
        int room = 80 - $"Q {seq} ".Length;
        for (int i = 0; i < path.Length; i += room) list.Add(Handler(seq, path.Substring(i, Math.Min(room, path.Length - i))));
        return list;
    }

    public static bool IsIngameOp(string? text) => text is { Length: >= 3 } t && t[1] == ' ' && t[0] is 'C' or 'Q' or 'A' or 'G' or 'P' or 'J' or 'X' or 'Z';

    public static bool IsPath(string? s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > 200) return false;
        foreach (char c in s) if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.')) return false;
        return true;
    }

    public static bool TryParse(string? text, out Msg m)
    {
        m = default;
        var f = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length < 3 || f[0].Length != 1 || !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seq) || seq < 0) return false;
        char op = f[0][0];
        switch (op)
        {
            case 'C': case 'G': case 'X': case 'Z':
                if (f.Length != 3 || !Wo179Rules.IsName(f[2])) return false;
                m = new Msg(op, seq, f[2], "", 0, 0); return true;
            case 'Q':
                if (f.Length != 3 || !IsPath(f[2])) return false;
                m = new Msg(op, seq, "", f[2], 0, 0); return true;
            case 'A':
                if (f.Length != 4 || !int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int code) || code < 0 || code > 3 || !Wo179Rules.IsName(f[3])) return false;
                m = new Msg(op, seq, f[3], "", code, 0); return true;
            case 'P': case 'J':
                if (f.Length != 4 || !int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int t) || t < 0 || t > 3_600_000 || !Wo179Rules.IsName(f[3])) return false;
                m = new Msg(op, seq, f[3], "", 0, t); return true;
            default:
                return false;
        }
    }

    // ---- the DLL's events (op 60) ----
    public static List<CombatPipe.Wo194Event> ParsePollEvents(byte[] p)
    {
        var list = new List<CombatPipe.Wo194Event>();
        if (p.Length < 5) return list;
        int at = 5;
        string Str()
        {
            if (at >= p.Length) { at = p.Length + 1; return ""; }
            int n = p[at];
            string v = at + 1 + n <= p.Length ? System.Text.Encoding.ASCII.GetString(p, at + 1, n) : "";
            at += 1 + n;
            return v;
        }
        for (int i = 0; i < p[4] && at + 9 <= p.Length; i++)
        {
            uint seq = BitConverter.ToUInt32(p, at); byte kind = p[at + 4]; uint age = BitConverter.ToUInt32(p, at + 5);
            at += 9;
            string name = Str(), path = Str(), det = Str();
            if (at > p.Length) break;
            list.Add(new CombatPipe.Wo194Event(seq, kind, age, name, path, det));
        }
        return list;
    }

    // ---- B2: the data mapping (cutscene name -> holder -> the quest's CutsceneHandler) ----
    public readonly record struct HandlerRow(string Name, string Holder, string Module, string Node, string Path);

    private static readonly Dictionary<string, List<HandlerRow>> Handlers = LoadHandlers();

    public static int HandlerCount => Handlers.Values.Sum(l => l.Count);

    public static IReadOnlyList<HandlerRow> HandlersOf(string name) => Handlers.TryGetValue(name, out var l) ? l : [];

    /// <summary>A handler's node path (FindNode's canonical form) is made of the graph's instance names -- each submodule's instantiating node,
    /// not its module's type (live: seminArrival's handler is "...h.na_semine.na_semine.cutscenehandler4", its module type cin_s0150t_...);
    /// the table carries it. Rows without one fall back to the module's own path (right only where instance and type names agree).</summary>
    public static string HandlerPath(HandlerRow h)
    {
        if (h.Path.Length > 0) return h.Path;
        string m = h.Module.StartsWith("brambora::", StringComparison.Ordinal) ? h.Module["brambora::".Length..] : h.Module;
        return m.Replace("::", ".", StringComparison.Ordinal) + "." + h.Node;
    }

    private static Dictionary<string, List<HandlerRow>> LoadHandlers()
    {
        var d = new Dictionary<string, List<HandlerRow>>(StringComparer.Ordinal);
        foreach (var line in HandlerTable.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = line.Split('|');
            if (p.Length is not (4 or 5)) continue;
            if (!d.TryGetValue(p[0], out var l)) d[p[0]] = l = [];
            l.Add(new HandlerRow(p[0], p[1], p[2], p[3], p.Length == 5 ? p[4] : ""));
        }
        return d;
    }

    // ---- D: the scenes with their own time of day (the cutscene table's Time column) ----
    private static readonly Dictionary<string, string> Times = LoadTimes();

    public static int TimeCount => Times.Count;

    public static bool TryTime(string name, out string time) => Times.TryGetValue(name, out time!);

    private static Dictionary<string, string> LoadTimes()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in TimeTable.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = line.Split('|');
            if (p.Length == 2) d[p[0]] = p[1];
        }
        return d;
    }

    // generated by tools/Build-CutsceneHandlers.py from the game's data (cutscene table, level objects, quest graphs): name|holder|module|node|path
    private const string HandlerTable = """
        artefakt_lootingArtifact_henryDefeated|artefakt_lootingArtifact_henryDefeated_CH|brambora::Barbora::klaster::klaster_hibernate_mode::artefakt::hledani_hrobu::cin_s9047r_monastery__henry_defeated|artefakt_lootingArtifact_henryDefeated|Barbora.klaster.dlc3.artefakt.hledani_hrobu.cin_s9047r_monastery__henry_defeated.artefakt_lootingArtifact_henryDefeated
        bohutovaVlozka_bohutaNebakov|bohutovaVlozka_bohutaNebakovCutscene|brambora::Barbora::trosecko::bohutovaVlozka::hibernation::bohutova_cast::nebakov::cin_m1020t_bohutovavlozka__bohuta_nebakov|nebakovArrival_cutsceneHandler|Barbora.trosecko.bohutovaVlozka.hibernation.bohutova_cast.nebakov.cin_m1020t_bohutovavlozka__bohuta_nebakov.nebakovArrival_cutsceneHandler
        bohutovaVlozka_intro|bohutovaVlozka_bohutuvPrijzedDoZelejovaCutscene|brambora::Barbora::trosecko::bohutovaVlozka::hibernation::bohutova_cast::v_zelejove::cin_m1010t_bohutovavlozka__bohuta_arrival|bohutaComesToZelejov|Barbora.trosecko.bohutovaVlozka.hibernation.bohutova_cast.v_zelejove.cin_m1010t_bohutovavlozka__bohuta_arrival.bohutaComesToZelejov
        bohutovaVlozka_nebakovJail|bohutovaVlozka_prisonCutscene|brambora::Barbora::trosecko::bohutovaVlozka::hibernation::henryho_cast::cin_m1040t_bohutovavlozka__nebakov_jail|cutscenehandler1|Barbora.trosecko.bohutovaVlozka.hibernation.henryho_cast.cin_m1040t_bohutovavlozka__nebakov_jail.cutscenehandler1
        bohutovaVlozka_pistaRelease|bohutovaVlozka_pistaReleaseCutscene|brambora::Barbora::trosecko::bohutovaVlozka::hibernation::henryho_cast::cin_m1050t_bohutovavlozka__pista_release|cutscenehandler1|Barbora.trosecko.bohutovaVlozka.hibernation.henryho_cast.cin_m1050t_bohutovavlozka__pista_release.cutscenehandler1
        bohutovaVlozka_standoffZizka|bohutovaVlozka_zizkaStandoffCutscene|brambora::Barbora::trosecko::bohutovaVlozka::hibernation::bohutova_cast::nebakov::cin_m1030t_bohutovavlozka__standoff_zizka|cutscenehandler1|Barbora.trosecko.bohutovaVlozka.hibernation.bohutova_cast.nebakov.cin_m1030t_bohutovavlozka__standoff_zizka.cutscenehandler1
        bohutovaVlozka_zizkasEye|bohutovaVlozka_zizkasEyeCutscene|brambora::Barbora::trosecko::bohutovaVlozka::hibernation::henryho_cast::cin_m1060t_bohutovavlozka__zizka_eye|cutscenehandler1|Barbora.trosecko.bohutovaVlozka.hibernation.henryho_cast.cin_m1060t_bohutovavlozka__zizka_eye.cutscenehandler1
        bratrizcimburka_cutscene_epichandshake_inBattle|bratriZCimburka_epicHandshake_inBattle|brambora::Barbora::kutnohorsko::bratriZCimburka::cin_s3350k_batrizcimburka__brothers_reunited|cutscenehandler27|Barbora.kutnohorsko.bratriZCimburka.balsan_se_vita_s_miroslavem.cutscenehandler27
        bratrizcimburka_cutscene_epichandshake_notInBattle|bratriZCimburka_epicHandshake_notInBattle|brambora::Barbora::kutnohorsko::bratriZCimburka::cin_s3350k_batrizcimburka__brothers_reunited|cutscenehandler27_1|Barbora.kutnohorsko.bratriZCimburka.balsan_se_vita_s_miroslavem.cutscenehandler27_1
        budovaniLazni_velkaParty|budovaniLazni_velkaParty_cutscene|brambora::Barbora::kutnohorsko::budovaniLazni::cin_s4150k_budovanilazni__bathhouse_party|cutscenehandler1|Barbora.kutnohorsko.budovaniLazni.velka_party.cutscenehandler1
        budovaniLazni_velkaParty_censored|budovaniLazni_velkaParty_cutsceneCensored|brambora::Barbora::kutnohorsko::budovaniLazni::cin_s4150k_budovanilazni__bathhouse_party|cutscenehandler1|Barbora.kutnohorsko.budovaniLazni.velka_party.cutscenehandler1
        cin_m4420k_zikmundtabor__cuman_murder|zikmunduvTabor_cin_murder|brambora::Barbora::kutnohorsko::zikmunduvTabor::hib::chlastacka__vrazda::cin_m4420k_zikmundtabor__cuman_murder|cutscenehandler1|Barbora.kutnohorsko.zikmunduvTabor.hib.chlastacka__vrazda.cin_m4420k_zikmundtabor__cuman_murder.cutscenehandler1
        cin_m4430k_zikmundtabor__ambush_praguers_boomVar|zikmunduvTabor_ambushCin_boom_new|brambora::Barbora::kutnohorsko::zikmunduvTabor::hib::odjezd_a_prepadeni::cin_m4430k_zikmundtabor__ambush_praguers|cutsceneVar_explosion|Barbora.kutnohorsko.zikmunduvTabor.hib.odjezd_a_prepadeni.cin_m4430k_zikmundtabor__ambush_praguers.cutsceneVar_explosion
        cin_m4430k_zikmundtabor__ambush_praguers_no_boomVar|zikmunduvTabor_ambushCin_noBoom_new|brambora::Barbora::kutnohorsko::zikmunduvTabor::hib::odjezd_a_prepadeni::cin_m4430k_zikmundtabor__ambush_praguers|cutsceneVar_no_explosion|Barbora.kutnohorsko.zikmunduvTabor.hib.odjezd_a_prepadeni.cin_m4430k_zikmundtabor__ambush_praguers.cutsceneVar_no_explosion
        drak_alchemist|drak_cutscene_alchemist|brambora::Barbora::kutnohorsko::drak::h::farar_a_fistfight::cin_s3050k_drak__introduction_alchemist|cutscenehandler183|Barbora.kutnohorsko.drak.h.farar_a_fistfight.prichod_alchymisty1.cutscenehandler183
        drak_standOff_full|drak_cutscene_standOff_full|brambora::Barbora::kutnohorsko::drak::h::standoff::standoff_u_naleziste_kosti::cin_s3060k_drak__archesite_standoff|cutscenehandler1333|Barbora.kutnohorsko.drak.h.standoff.standoff_u_naleziste_kosti.prichod_akteru_na_scenu1.cutscenehandler1333
        dvojityAgent_uncoveringPetrsBody|dvojityAgent_uncoveringPetrsBody_cutscene|brambora::Barbora::kutnohorsko::dvojityAgent::v_zikmundove_tabore::patrani_po_zajatci::petrovo_telo_v_tabore::odkryti_petrova_tela|cutscenehandler1|Barbora.kutnohorsko.dvojityAgent.dvojityAgent_inZikmundsCamp.patrani_po_zajatci.petrovo_telo_v_tabore.odkryti_petrova_tela.cutscenehandler1
        erik_nocNaHradbach|erik_nocNaHradbach_cutscene|brambora::Barbora::kutnohorsko::erik::hibernables::cin_m4710k_erik__night_walls|nocNaHradbach|Barbora.kutnohorsko.erik.hibernables.cin_m4710k_erik__night_walls.nocNaHradbach
        erik_odjezdMestanuAPrijezdKubenky|erik_odjezdMestanuAPrijezdKubenky_cutscene|brambora::Barbora::kutnohorsko::erik::hibernables::cin_m4720k_erik__kubenka_returns|odjezdMestanuAPrijezdKubenky|Barbora.kutnohorsko.erik.hibernables.cin_m4720k_erik__kubenka_returns.odjezdMestanuAPrijezdKubenky
        erik_odjezdMestanuAPrijezdKubenky_rozaRomance|erik_odjezdMestanuAPrijezdKubenky_roza_cutscene|brambora::Barbora::kutnohorsko::erik::hibernables::cin_m4720k_erik__kubenka_returns|odjezdMestanuAPrijezdKubenky_1|Barbora.kutnohorsko.erik.hibernables.cin_m4720k_erik__kubenka_returns.odjezdMestanuAPrijezdKubenky_1
        erik_porazkaErikaAPrijezdArmady|erik_porazkaErikaAPrijezdArmady_cutscene|brambora::Barbora::kutnohorsko::erik::hibernables::hrac_se_rozhodne_pro_souboj_s_erikem_nebo_zustane_na_hrade::cin_m4740k_erik__erik_loses|cutscenehandler1|Barbora.kutnohorsko.erik.hibernables.hrac_se_rozhodne_pro_souboj_s_erikem_nebo_zustane_na_hrade_1.cin_m4740k_erik__erik_loses.cutscenehandler1
        erik_prazaneTahnouNaSuchdol_erik|erik_prazaneTahnouNaSuchdol_erikVerze_cutscene|brambora::Barbora::kutnohorsko::erik::hibernables::cin_m4750k_erik__suchdol_siege|prazaneTahnouNaSuchdol|Barbora.kutnohorsko.erik.hibernables.cin_m4750k_erik__suchdol_siege.prazaneTahnouNaSuchdol
        erik_prazaneTahnouNaSuchdol_noErik|erik_prazaneTahnouNaSuchdol_noErikVerze_cutscene|brambora::Barbora::kutnohorsko::erik::hibernables::cin_m4750k_erik__suchdol_siege|prazaneTahnouNaSuchdol_1|Barbora.kutnohorsko.erik.hibernables.cin_m4750k_erik__suchdol_siege.prazaneTahnouNaSuchdol_1
        erik_prijezdNaKopec|erik_prijezdNaKopec_cutscene|brambora::Barbora::kutnohorsko::erik::hibernables::hrac_se_rozhodne_pro_souboj_s_erikem_nebo_zustane_na_hrade::prijezd_na_kopec_a_souboj_s_erikem::cin_m4730k_erik__arrival_duel|prijezdNaKopec|Barbora.kutnohorsko.erik.hibernables.hrac_se_rozhodne_pro_souboj_s_erikem_nebo_zustane_na_hrade_1.prijezd_na_kopec_a_souboj_s_erikem.cin_m4730k_erik__arrival_duel.prijezdNaKopec
        finale_acceptSwordEndingScene|finale_acceptSwordEndingCutsceneHolder|brambora::Barbora::kutnohorsko::finale::hibernation::cin_m5150k_finale__ending_scene|cutscenehandler_acceptSword|Barbora.kutnohorsko.finale.hibernation.cin_m5150k_finale__ending_scene.cutscenehandler_acceptSword
        finale_radzigSwordCriticallyBad|finale_radzigSwordCriticallyBadCutsceneHolder|brambora::Barbora::kutnohorsko::finale::hibernation::cin_m5140k_finale__radzig_sword|cutscenehandler1|Barbora.kutnohorsko.finale.hibernation.cin_m5140k_finale__radzig_sword.cutscenehandler1
        finale_radzigSwordGood|finale_radzigSwordGoodCutsceneHolder|brambora::Barbora::kutnohorsko::finale::hibernation::cin_m5140k_finale__radzig_sword|cutscenehandler1|Barbora.kutnohorsko.finale.hibernation.cin_m5140k_finale__radzig_sword.cutscenehandler1
        finale_radzigSwordSad|finale_radzigSwordSadCutsceneHolder|brambora::Barbora::kutnohorsko::finale::hibernation::cin_m5140k_finale__radzig_sword|cutscenehandler1|Barbora.kutnohorsko.finale.hibernation.cin_m5140k_finale__radzig_sword.cutscenehandler1
        finale_refuseSwordEndingScene|finale_refuseSwordEndingCutsceneHolder|brambora::Barbora::kutnohorsko::finale::hibernation::cin_m5150k_finale__ending_scene|cutscenehandler_acceptSword|Barbora.kutnohorsko.finale.hibernation.cin_m5150k_finale__ending_scene.cutscenehandler_acceptSword
        finale_treeDream|finale_treeDreamCutsceneHolder|brambora::Barbora::kutnohorsko::finale::hibernation::cin_m5130k_finale__tree_dream|cutscenehandler1|Barbora.kutnohorsko.finale.hibernation.cin_m5130k_finale__tree_dream.cutscenehandler1
        finale_victoryGathering|finale_victoryGatheringCutsceneHolder|brambora::Barbora::kutnohorsko::finale::hibernation::cin_m5112k_finale__victory_gathering|victoryGatheringCutsceneHandler|Barbora.kutnohorsko.finale.hibernation.cin_m5112k_finale__victory_gathering.victoryGatheringCutsceneHandler
        finale_zikmundPissed|finale_zikmundPissedCutsceneHolder|brambora::Barbora::kutnohorsko::finale::hibernation::cin_m5160k_finale__zikmund_pissed|cutscenehandler1|Barbora.kutnohorsko.finale.hibernation.cin_m5160k_finale__zikmund_pissed.cutscenehandler1
        finale_zizkasEye|finale_zizkasEyeCutsceneHolder|brambora::Barbora::kutnohorsko::finale::hibernation::cin_m5155k_finale__zizkas_eye|cutscenehandler1|Barbora.kutnohorsko.finale.hibernation.cin_m5155k_finale__zizkas_eye.cutscenehandler1
        havirskyTurnaj_competition_lost|havirskyTurnaj_competition_lost|brambora::Barbora::kutnohorsko::havirskyTurnaj::hibernovana_gameplay::strelecka_soutez::minihra__strelba_ku_ptaku::cin_s9860k_havirskyturnaj__tournament_result|cutscenehandler2_1|Barbora.kutnohorsko.havirskyTurnaj.hibernovana_gameplay.strelecka_soutez.minihra__strelba_ku_ptaku.cin_s9860k_havirskyturnaj__tournament_result.cutscenehandler2_1
        havirskyTurnaj_competition_win|havirskyTurnaj_competition_win|brambora::Barbora::kutnohorsko::havirskyTurnaj::hibernovana_gameplay::strelecka_soutez::minihra__strelba_ku_ptaku::cin_s9860k_havirskyturnaj__tournament_result|cutscenehandler2|Barbora.kutnohorsko.havirskyTurnaj.hibernovana_gameplay.strelecka_soutez.minihra__strelba_ku_ptaku.cin_s9860k_havirskyturnaj__tournament_result.cutscenehandler2
        hladAZmar_bohuta_preaching|hladAZmar_bohutaPreaching_cutscene|brambora::Barbora::kutnohorsko::hladAZmar::h::bohuta_kaze::cin_m4860k_oblehanisuchdol__bohuta_preaching|cutscenehandler1|Barbora.kutnohorsko.hladAZmar.h.bohuta_kaze.cin_m4860k_oblehanisuchdol__bohuta_preaching.cutscenehandler1
        hladAZmar_bohuta_preaching_after|hladAZmar_bohutaPreaching_cutsceneAfter|brambora::Barbora::kutnohorsko::hladAZmar::h::bohuta_kaze::cin_m4860k_oblehanisuchdol__bohuta_preaching|cutscenehandler8|Barbora.kutnohorsko.hladAZmar.h.bohuta_kaze.cin_m4860k_oblehanisuchdol__bohuta_preaching.cutscenehandler8
        hladAZmar_bohuta_preaching_after_cryingWoman|hladAZmar_bohutaPreaching_cutsceneAfter_cryingWoman|brambora::Barbora::kutnohorsko::hladAZmar::h::bohuta_kaze::cin_m4860k_oblehanisuchdol__bohuta_preaching|cutscenehandler8|Barbora.kutnohorsko.hladAZmar.h.bohuta_kaze.cin_m4860k_oblehanisuchdol__bohuta_preaching.cutscenehandler8
        hladAZmar_court_siezed|hladAZmar_prepareToLeave_courtSiezed_cutscene|brambora::Barbora::kutnohorsko::hladAZmar::h::cin_m4880k_oblehanisuchdol__court_seized|cutscenehandler1|Barbora.kutnohorsko.hladAZmar.h.cin_m4880k_oblehanisuchdol__court_seized.cutscenehandler1
        hladAZmar_hunger_despair|hladAZmar_beforeSermon_hungerDespair_cutscene|brambora::Barbora::kutnohorsko::hladAZmar::h::cin_m4850k_oblehanisuchdol__hunger_despair|cutscenehandler1|Barbora.kutnohorsko.hladAZmar.h.cin_m4850k_oblehanisuchdol__hunger_despair.cutscenehandler1
        hladAZmar_katerina_romance|hladAZmar_prepareToLeave_katerinaRomance_cutscene|brambora::Barbora::kutnohorsko::hladAZmar::h::cin_m4890k_oblehanisuchdol__katerina_romance|cutscenehandler1|Barbora.kutnohorsko.hladAZmar.h.cin_m4890k_oblehanisuchdol__katerina_romance_1.cutscenehandler1
        hladAZmar_katerina_romance_JP|hladAZmar_prepareToLeave_katerinaRomanceJP_cutscene|brambora::Barbora::kutnohorsko::hladAZmar::h::cin_m4890k_oblehanisuchdol__katerina_romance|cutscenehandler1|Barbora.kutnohorsko.hladAZmar.h.cin_m4890k_oblehanisuchdol__katerina_romance_1.cutscenehandler1
        hladAZmar_ptacek_romance|hladAZmar_prepareToLeave_romanceSPtackem_cutscene|brambora::Barbora::kutnohorsko::hladAZmar::h::cin_m4895k_oblehanisuchdol__ptacek_romance|cutscenehandler1|Barbora.kutnohorsko.hladAZmar.h.cin_m4895k_oblehanisuchdol__ptacek_romance.cutscenehandler1
        hladAZmar_ptacek_romance_JP|hladAZmar_prepareToLeave_romanceSPtackem_JP_cutscene|brambora::Barbora::kutnohorsko::hladAZmar::h::cin_m4895k_oblehanisuchdol__ptacek_romance|cutscenehandler1|Barbora.kutnohorsko.hladAZmar.h.cin_m4895k_oblehanisuchdol__ptacek_romance.cutscenehandler1
        hladAZmar_tower_seized|hladAZmar_battle_towerSeized|brambora::Barbora::kutnohorsko::hladAZmar::h::celni_utok_prazskeho_vojska::cin_m4870k_oblehanisuchdol__tower_seized|cutscenehandler1|Barbora.kutnohorsko.hladAZmar.h.hladAZmar_battleSO.cin_m4870k_oblehanisuchdol__tower_seized.cutscenehandler1
        hledaniLichtenstejna_courtTrap|hledaniLichtenstejna_courtTrap_cutscene|brambora::Barbora::kutnohorsko::hledaniLichtenstejna::samuel::opusteny_dvorek::hrac_prichazi_do_pasti::cin_m3310k_hledanilichta__court_trap|cutscenehandler1|Barbora.kutnohorsko.hledaniLichtenstejna.hledaniLichtenstejna_dealingWithSamuel.opusteny_dvorek.hrac_prichazi_do_pasti.cin_m3310k_hledanilichta__court_trap.cutscenehandler1
        hledaniLichtenstejna_courtTrap_inside|hledaniLichtenstejna_courtTrap_cutscene2|brambora::Barbora::kutnohorsko::hledaniLichtenstejna::samuel::opusteny_dvorek::hrac_prichazi_do_pasti::cin_m3310k_hledanilichta__court_trap|cutscenehandler24|Barbora.kutnohorsko.hledaniLichtenstejna.hledaniLichtenstejna_dealingWithSamuel.opusteny_dvorek.hrac_prichazi_do_pasti.cin_m3310k_hledanilichta__court_trap.cutscenehandler24
        hledaniLichtenstejna_meetLichtenstejn|hledaniLichtenstejna_meetLichtenstejn_cutscene|brambora::Barbora::kutnohorsko::hledaniLichtenstejna::samuel::nalezeni_lichtenstejna::cin_m3340k_hledanilichta__meet_licht|cutscenehandler1|Barbora.kutnohorsko.hledaniLichtenstejna.hledaniLichtenstejna_dealingWithSamuel.nalezeni_lichtenstejna.cin_m3340k_hledanilichta__meet_licht.cutscenehandler1
        hledaniLichtenstejna_samuelLost|hledaniLichtenstejna_samuelLost_cutscene|brambora::Barbora::kutnohorsko::hledaniLichtenstejna::samuel::opusteny_dvorek::hrac_prichazi_do_pasti::cin_m3330k_hledanilichta__samuel_lost|cutscenehandler1|Barbora.kutnohorsko.hledaniLichtenstejna.hledaniLichtenstejna_dealingWithSamuel.opusteny_dvorek.hrac_prichazi_do_pasti.cin_m3330k_hledanilichta__samuel_lost.cutscenehandler1
        hledaniLichtenstejna_samuelWins|hledaniLichtenstejna_samuelWins_cutscene|brambora::Barbora::kutnohorsko::hledaniLichtenstejna::samuel::opusteny_dvorek::hrac_prichazi_do_pasti::cin_m3320k_hledanilichta__samuel_wins|cutscenehandler1|Barbora.kutnohorsko.hledaniLichtenstejna.hledaniLichtenstejna_dealingWithSamuel.opusteny_dvorek.hrac_prichazi_do_pasti.cin_m3320k_hledanilichta__samuel_wins.cutscenehandler1
        hledaniPsa_meetingVorech_bow|hledaniPsa_meetingMutt3|brambora::Barbora::trosecko::hledaniPsa::hledaniPsa_control::druha_smecka::druha_psi_smecka__script::cs|cutscenehandler1275958_1_2|Barbora.trosecko.hledaniPsa.hledaniPsa_control.druha_smecka.hledaniPsa_secondPackLiquidation.cs.cutscenehandler1275958_1_2
        hledaniPsa_meetingVorech_mace|hledaniPsa_meetingMutt2|brambora::Barbora::trosecko::hledaniPsa::hledaniPsa_control::druha_smecka::druha_psi_smecka__script::cs|cutscenehandler1275958_1|Barbora.trosecko.hledaniPsa.hledaniPsa_control.druha_smecka.hledaniPsa_secondPackLiquidation.cs.cutscenehandler1275958_1
        hledaniPsa_meetingVorech_sword|hledaniPsa_meetingMutt|brambora::Barbora::trosecko::hledaniPsa::hledaniPsa_control::druha_smecka::druha_psi_smecka__script::cs|cutscenehandler1275958_1_1|Barbora.trosecko.hledaniPsa.hledaniPsa_control.druha_smecka.hledaniPsa_secondPackLiquidation.cs.cutscenehandler1275958_1_1
        kejkliri_playingTheSong|kejkliri_playingTheSong|brambora::Barbora::trosecko::kejkliri::hibernovana_cast::perform_pisnicky::cin_s1950t_kejkliri__michaldavid_song|cutscenehandler5|Barbora.trosecko.kejkliri.hibernovana_cast.perform_pisnicky.pisen_michala_davida.cutscenehandler5
        kocovnickaCest_campCelebration|kocovnickaCest_campCelebration_cutscene|brambora::Barbora::trosecko::kocovnickaCest::hib::rozuzleni_a_konec_questu::cin_s2560t_kocovnickacest__camp_celebration|cutscenehandler1|Barbora.trosecko.kocovnickaCest.kocovnickaCest_hib.kocovnickaCest_questEnd.cin_s2560t_kocovnickacest__camp_celebration.cutscenehandler1
        kralovskeStribro_fightAtRuthardsMansion|kralovskeStribro_fightAtRuthardsMansion|brambora::Barbora::kutnohorsko::kralovskeStribro::main_hibernable::sidlo_ruthardovcu::cin_m3410k_kralovskestribro__vavak_quarrel|cutscenehandler1|Barbora.kutnohorsko.kralovskeStribro.main_hibernable.sidlo_ruthardovcu.cin_m3410k_kralovskestribro__vavak_quarrel.cutscenehandler1
        kubaParalu_MurdererRevealed_ingame|kubaParalu_cutscene_trap_ingame_end|brambora::Barbora::kutnohorsko::kubaParalu::past::hrac_ma_podezreni_a_chce_udelat_past::cin_s3660k_kubaparalu__murderer_revealed|cutscenehandler9|Barbora.kutnohorsko.kubaParalu.past.hrac_ma_podezreni_a_chce_udelat_past.cin_s3660k_kubaparalu__murderer_revealed.cutscenehandler9
        kutnohorskyTurnaj_startTourney_VariantA|kutnohorskyTurnaj_cs_tourneyStart_variantA|brambora::Barbora::kutnohorsko::kutnohorskyTurnaj::opakujici_se::stream_a_cs_turnaje::cin_s9850k_turnaj__tournament_start|cutscenehandler_turnaj_1|Barbora.kutnohorsko.kutnohorskyTurnaj.opakujici_se.stream_a_cs_turnaje.cin_s9850k_turnaj__tournament_start.cutscenehandler_turnaj_1
        kutnohorskyTurnaj_startTourney_VariantB|kutnohorskyTurnaj_cs_tourneyStart_variantB|brambora::Barbora::kutnohorsko::kutnohorskyTurnaj::opakujici_se::stream_a_cs_turnaje::cin_s9850k_turnaj__tournament_start|cutscenehandler_turnaj_1|Barbora.kutnohorsko.kutnohorskyTurnaj.opakujici_se.stream_a_cs_turnaje.cin_s9850k_turnaj__tournament_start.cutscenehandler_turnaj_1
        kutnohorskyTurnaj_startTourney_VariantC|kutnohorskyTurnaj_cs_tourneyStart_variantC|brambora::Barbora::kutnohorsko::kutnohorskyTurnaj::opakujici_se::stream_a_cs_turnaje::cin_s9850k_turnaj__tournament_start|cutscenehandler_turnaj_1|Barbora.kutnohorsko.kutnohorskyTurnaj.opakujici_se.stream_a_cs_turnaje.cin_s9850k_turnaj__tournament_start.cutscenehandler_turnaj_1
        kutnohorskyTurnaj_startTourney_WithoutHenry|kutnohorskyTurnaj_cs_tourneyStart_withoutHenry|brambora::Barbora::kutnohorsko::kutnohorskyTurnaj::opakujici_se::stream_a_cs_turnaje::cin_s9850k_turnaj__tournament_start|cutscenehandler_turnaj_1|Barbora.kutnohorsko.kutnohorskyTurnaj.opakujici_se.stream_a_cs_turnaje.cin_s9850k_turnaj__tournament_start.cutscenehandler_turnaj_1
        malir_firstMeeting_ingame|malir_cutsceneFirstMeeting|brambora::Barbora::trosecko::brushes_with_death_trosecko::malir::zacatek_questu::prvni_setkani_s_malirem_s_cutscenou|cutscenehandler24|Barbora.trosecko.brushes_with_death_trosecko.malir.zacatek_questu.prvni_setkani_s_malirem_s_cutscenou.cutscenehandler24
        mlynaruvUcen_contraband_sack_cutscene|mlynaruvUcen_contraband_sack_cutscene|brambora::Barbora::trosecko::mlynaruvUcen::hlavni_gameplay_questu::cin_s0950t_mlynaruvucen__contraband_sack|cutscenehandler9|Barbora.trosecko.mlynaruvUcen.hlavni_gameplay_questu.roztrzeny_pytel.cutscenehandler9
        mucirna_vypaleniSemina_arrival|mucirna_arrivalCutscene|brambora::Barbora::trosecko::mucirna::hibernation::na_semine::cin_m0810t_vypalenisemina__bergow_arrives::cin_m0810t_vypalenisemina__bergow_arrives|cutscenehandler1|Barbora.trosecko.mucirna.hibernation.na_semine.cin_m0810t_vypalenisemina__bergow_arrives.cin_m0810t_vypalenisemina__bergow_arrives.cutscenehandler1
        mucirna_vypaleniSemina_seminBurnDownBezPosil|mucirna_seminBurnDownBezPosil|brambora::Barbora::trosecko::mucirna::hibernation::na_semine::cin_m0820t_vypalenisemina__semin_torched::cin_m0820t_vypalenisemina__semin_torched|seminBezPosil|Barbora.trosecko.mucirna.hibernation.na_semine.cin_m0820t_vypalenisemina__semin_torched.cin_m0820t_vypalenisemina__semin_torched.seminBezPosil
        mucirna_vypaleniSemina_seminBurnDownPosily|mucirna_seminBurnDownPosily|brambora::Barbora::trosecko::mucirna::hibernation::na_semine::cin_m0820t_vypalenisemina__semin_torched::cin_m0820t_vypalenisemina__semin_torched|seminSPosilama|Barbora.trosecko.mucirna.hibernation.na_semine.cin_m0820t_vypalenisemina__semin_torched.cin_m0820t_vypalenisemina__semin_torched.seminSPosilama
        nalazeniDelnici__killing_herbalist|nalezeniDelnici_Vrazda|brambora::Barbora::klaster::klaster_hibernate_mode::nalezeniDelnici::cin_s9030r_monastery__killing_herbalist|cutscenehandler2|Barbora.klaster.dlc3.nalezeniDelnici.cin_s9030r_monastery__killing_herbalist.cutscenehandler2
        nalazeniDelnici__shack_fire|nalezeniDelnici_dumHori|brambora::Barbora::klaster::klaster_hibernate_mode::nalezeniDelnici::cin_s9025r_monastery__shack_fire|cutscenehandler1|Barbora.klaster.dlc3.nalezeniDelnici.cin_s9025r_monastery__shack_fire.cutscenehandler1
        nalazeniDelnici__zachary_confrontation|nalezeniDelnici_ToJeOtrava|brambora::Barbora::klaster::klaster_hibernate_mode::nalezeniDelnici::cin_s9035r_monastery__zachary_confrontation|cutscenehandler1|Barbora.klaster.dlc3.nalezeniDelnici.cin_s9035r_monastery__zachary_confrontation.cutscenehandler1
        naTroskach_endPrison|cs_endPrison|brambora::Barbora::trosecko::naTroskach::probuzeni::cin_m0610t_natroskach__jail_release|cutscenehandler1|Barbora.trosecko.naTroskach.probuzeni.cin_m0610t_natroskach__jail_release.cutscenehandler1
        naTroskach_ptacekExecution|cs_poprava|brambora::Barbora::trosecko::naTroskach::cin_m0620t_natroskach__ptacek_execution|cutscenehandler1|Barbora.trosecko.naTroskach.cin_m0620t_natroskach__ptacek_execution.cutscenehandler1
        naTroskach_ptacekExecution_kratsi|cs_popravaKratsi|brambora::Barbora::trosecko::naTroskach::cin_m0620t_natroskach__ptacek_execution|cutscenehandler1|Barbora.trosecko.naTroskach.cin_m0620t_natroskach__ptacek_execution.cutscenehandler1
        nebakovObrana_enemyArmy|nebakovObrana_beforeFightCutscene|brambora::Barbora::trosecko::nebakovObrana::nebakovske_noci_a_rana::cin_m1120t_nebakovobrana__enemy_army|cutscenehandler1|Barbora.trosecko.nebakovObrana.nebakovske_noci_a_rana.cin_m1120t_nebakovobrana__enemy_army.cutscenehandler1
        nebakovObrana_gateOpen|nebakovObrana_gateOpen|brambora::Barbora::trosecko::nebakovObrana::bitva_o_tvrz_nebakov::utok_na_branu::gate_break|cutscenehandler84|Barbora.trosecko.nebakovObrana.bitva_o_tvrz_nebakov.utok_na_branu.gate_break.cutscenehandler84
        nebakovObrana_godFinger|nebakovObrana_godFingerCS|brambora::Barbora::trosecko::nebakovObrana::bitva_o_tvrz_nebakov::cin_m1140t_nebakovobrana__god_finger|cutscenehandler1|Barbora.trosecko.nebakovObrana.bitva_o_tvrz_nebakov.cin_m1140t_nebakovobrana__god_finger.cutscenehandler1
        nebakovObrana_godFinger|nebakovObrana_godFingerCS|brambora::Barbora::trosecko::nebakovObrana::last_cs_haste|cutscenehandler1|Barbora.trosecko.nebakovObrana.last_cs_haste.cutscenehandler1
        nebakovObrana_godFingerShorter|nebakovObrana_godFingerCS3|brambora::Barbora::trosecko::nebakovObrana::bitva_o_tvrz_nebakov::cin_m1140t_nebakovobrana__god_finger|cutscenehandler2|Barbora.trosecko.nebakovObrana.bitva_o_tvrz_nebakov.cin_m1140t_nebakovobrana__god_finger.cutscenehandler2
        nebakovObrana_startBattle|nebakovObrana_fightStartCutscene|brambora::Barbora::trosecko::nebakovObrana::bitva_o_tvrz_nebakov|cutscenehandler1|Barbora.trosecko.nebakovObrana.bitva_o_tvrz_nebakov.cutscenehandler1
        oblehaniSuchdole_odrazeniUtokuZPochodu|oblehaniSuchdole_odrazeniUtokuZpochodu|brambora::Barbora::kutnohorsko::oblehaniSuchdole::prazane_utoci_z_pochodu_na_predhradi::cin_m4810k_oblehanisuchdol__deflect_attack|cutscenehandler1|Barbora.kutnohorsko.oblehaniSuchdole.prazane_utoci_z_pochodu_na_predhradi.cin_m4810k_oblehanisuchdol__deflect_attack.cutscenehandler1
        papezskyLegat_nocSRozou|papezskyLegat_nocSRozou|brambora::Barbora::kutnohorsko::papezskyLegat::cin_m4530k_papezlegat__roza_romance|cutscenehandler1|Barbora.kutnohorsko.papezskyLegat.cin_m4530k_papezlegat__roza_romance.cutscenehandler1
        papezskyLegat_nocSRozou_noSex|papezskyLegat_nocSRozou_noSex|brambora::Barbora::kutnohorsko::papezskyLegat::cin_m4532k_papezlegat__roza_romance_nosex|cutscenehandler1|Barbora.kutnohorsko.papezskyLegat.cin_m4532k_papezlegat__roza_romance_nosex.cutscenehandler1
        papezskyLegat_prepadeniLegata|papezskyLegat_prepadeniLegata|brambora::Barbora::kutnohorsko::papezskyLegat::cin_m4510k_papezlegat__robbery_legat|cutscenehandler3|Barbora.kutnohorsko.papezskyLegat.cin_m4510k_papezlegat__robbery_legat.cutscenehandler3
        papezskyLegat_smrtLegata|papezskyLegat_zabitiLegata|brambora::Barbora::kutnohorsko::papezskyLegat::cin_m4520k_papezlegat__legat_killed|cutscenehandler3|Barbora.kutnohorsko.papezskyLegat.cin_m4520k_papezlegat__legat_killed.cutscenehandler3
        pogrom_blockadeFire|pogrom_blockadeFire|brambora::Barbora::kutnohorsko::pogrom::hibernace::synagoga_a_jine_hity::cin_m4220k_pogrom__blockade_fire|cutscenehandler1|Barbora.kutnohorsko.pogrom.hibernace.synagoga_a_jine_hity.cin_m4220k_pogrom__blockade_fire.cutscenehandler1
        pogrom_defendSynagogue|pogrom_defendSynagogue|brambora::Barbora::kutnohorsko::pogrom::hibernace::synagoga_a_jine_hity::cin_m4210k_pogrom__defend_synagogue|cutscenehandler1|Barbora.kutnohorsko.pogrom.hibernace.synagoga_a_jine_hity.cin_m4210k_pogrom__defend_synagogue.cutscenehandler1
        pracharna_contrabant_sack|pracharna_cutscene|brambora::Barbora::trosecko::pracharna::cin_s1150t_pracharna__gunpowder_test|cutscenehandler1|Barbora.trosecko.pracharna.test_strelneho_prachu.cutscenehandler1
        predaniVChramu_duelSeZachariasem_bad|predaniVChramu_duelSeZachariasemBad_cutscene|brambora::Barbora::klaster::klaster_hibernate_mode::predaniVChramu::predani::pronasledovani::na_povrchu::cin_s9060r_monastery__zachary_duel|cutscenehandler1_1|Barbora.klaster.dlc3.predaniVChramu.predani.pronasledovani.na_povrchu.cin_s9060r_monastery__zachary_duel.cutscenehandler1_1
        predaniVChramu_duelSeZachariasem_good|predaniVChramu_duelSeZachariasemGood_cutscene|brambora::Barbora::klaster::klaster_hibernate_mode::predaniVChramu::predani::pronasledovani::na_povrchu::cin_s9060r_monastery__zachary_duel|cutscenehandler1|Barbora.klaster.dlc3.predaniVChramu.predani.pronasledovani.na_povrchu.cin_s9060r_monastery__zachary_duel.cutscenehandler1
        predaniVChramu_healed_ending|predaniVChramu_healedEnding_cutscene|brambora::Barbora::klaster::klaster_hibernate_mode::predaniVChramu::predani::epilog::cutsceny::cin_s9065r_monastery__healed_ending|cutscenehandler1|Barbora.klaster.dlc3.predaniVChramu.predani.epilog.cutsceny.cin_s9065r_monastery__healed_ending.cutscenehandler1
        predaniVChramu_prichaziOpat|predaniVChramu_prichaziOpat_cutscene|brambora::Barbora::klaster::klaster_hibernate_mode::predaniVChramu::predani::v_chramu::cin_s9055r_monastery__abbot_arrival|cutscenehandler1|Barbora.klaster.dlc3.predaniVChramu.predani.v_chramu.cin_s9055r_monastery__abbot_arrival.cutscenehandler1
        predaniVChramu_prichodZachariase|predaniVChramu_arrivalZacharias_cutsceneHolder|brambora::Barbora::klaster::klaster_hibernate_mode::predaniVChramu::predani::v_chramu::cin_s9050r_monastery__zachary_encounter|cutscenehandler1|Barbora.klaster.dlc3.predaniVChramu.predani.v_chramu.cin_s9050r_monastery__zachary_encounter.cutscenehandler1
        predaniVChramu_sick_ending|predaniVChramu_sickEnding_cutscene|brambora::Barbora::klaster::klaster_hibernate_mode::predaniVChramu::predani::epilog::cutsceny::cin_s9070r_monastery__sick_ending|cutscenehandler1|Barbora.klaster.dlc3.predaniVChramu.predani.epilog.cutsceny.cin_s9070r_monastery__sick_ending.cutscenehandler1
        prepadeni_armorLake|prepadeni_armorLake|brambora::Barbora::trosecko::prepadeni::hibernovana_cast::cin_m0130t_prepadeni__armor_lake|cutscenehandler1|Barbora.trosecko.prepadeni.hibernovana_cast.koupani.cutscenehandler1
        prepadeni_henryFalls|prepadeni_henryFalls|brambora::Barbora::trosecko::prepadeni::hibernovana_cast::cin_m0180t_prepadeni__henry_falls|cutscenehandler1|Barbora.trosecko.prepadeni.hibernovana_cast.prurva_a_zachrana_psem.cutscenehandler1
        prepadeni_lakeMassacre|prepadeni_lakeMassacre|brambora::Barbora::trosecko::prepadeni::hibernovana_cast::cin_m0140t_prepadeni__lake_massacre|cutscenehandler1|Barbora.trosecko.prepadeni.hibernovana_cast.masakr.cutscenehandler1
        prepadeni_lakeMassacreDiscoverd|prepadeni_lakeMassacreDiscoverd|brambora::Barbora::trosecko::prepadeni::hibernovana_cast::cin_m0140t_prepadeni__lake_massacre|cutscenehandler1|Barbora.trosecko.prepadeni.hibernovana_cast.masakr.cutscenehandler1
        prepadeni_meetingWithSheriff|prepadeni_meetingWithSheriff|brambora::Barbora::trosecko::prepadeni::hibernovana_cast::cin_m0120t_prepadeni__bergow_riders|cutscenehandler1|Barbora.trosecko.prepadeni.hibernovana_cast.setkani_s_hejtmanem_od_bergova.cutscenehandler1
        prepadeni_roadToCamp|prepadeni_roadToCamp|brambora::Barbora::trosecko::prepadeni::hibernovana_cast::prijezd_do_tabora2|cutscenehandler1|Barbora.trosecko.prepadeni.hibernovana_cast.prijezd_do_tabora2.cutscenehandler1
        prepadeniVlasskehoDvora_brabantTreason|prepadeniVlasskehoDvora_cutscene_brabantTreason|brambora::Barbora::kutnohorsko::prepadeniVlasskehoDvora::h::jindra__skirmish_v_ruthardce::cin_m4655k_prepadenidvora__brabant_treason|cutscenehandler1|Barbora.kutnohorsko.prepadeniVlasskehoDvora.h.jindra__skirmish_v_ruthardce.cin_m4655k_prepadenidvora__brabant_treason.cutscenehandler1
        prepadeniVlasskehoDvora_cellarHole|prepadeniVlasskehoDvora_cutscene_cellarHole|brambora::Barbora::kutnohorsko::prepadeniVlasskehoDvora::h::stealth::jindra__propojeni_chodby::cin_m4620k_prepadenidvora__cellar_hole|cutscenehandler3|Barbora.kutnohorsko.prepadeniVlasskehoDvora.h.stealth.jindra__propojeni_chodby.cin_m4620k_prepadenidvora__cellar_hole.cutscenehandler3
        prepadeniVlasskehoDvora_courtHall|prepadeniVlasskehoDvora_cutscene_courtHall|brambora::Barbora::kutnohorsko::prepadeniVlasskehoDvora::h::bohuta__cirkevni_rada::cin_m4610k_prepadenidvora__court_hall|cutscenehandler1|Barbora.kutnohorsko.prepadeniVlasskehoDvora.h.bohuta__cirkevni_rada.cin_m4610k_prepadenidvora__court_hall.cutscenehandler1
        prepadeniVlasskehoDvora_erikArrives|prepadeniVlasskehoDvora_CS_erikArrives|brambora::Barbora::kutnohorsko::prepadeniVlasskehoDvora::h::jindra__obrana_vlasskeho_dvora::cin_m4640k_prepadenidvora__erik_arrives|CH_erikAntePortas|Barbora.kutnohorsko.prepadeniVlasskehoDvora.h.jindra__obrana_vlasskeho_dvora.cin_m4640k_prepadenidvora__erik_arrives.CH_erikAntePortas
        prepadeniVlasskehoDvora_explosiveTrap|prepadeniVlasskehoDvora_CS_escape|brambora::Barbora::kutnohorsko::prepadeniVlasskehoDvora::h::jindra__obrana_vlasskeho_dvora::cin_m4650k_prepadenidvora__explosive_trap|CH_explosiveTrap|Barbora.kutnohorsko.prepadeniVlasskehoDvora.h.jindra__obrana_vlasskeho_dvora.cin_m4650k_prepadenidvora__explosive_trap.CH_explosiveTrap
        prepadeniVlasskehoDvora_komarDeath|prepadeniVlasskehoDvora_cutscene_komarDeath|brambora::Barbora::kutnohorsko::prepadeniVlasskehoDvora::h::jindra__skirmish_v_ruthardce::cin_m4660k_prepadenidvora__komar_death|cutscenehandler1|Barbora.kutnohorsko.prepadeniVlasskehoDvora.h.jindra__skirmish_v_ruthardce.cin_m4660k_prepadenidvora__komar_death.cutscenehandler1
        prepadeniVlasskehoDvora_lords|prepadeniVlasskehoDvora_cutscene_ow_lords|brambora::Barbora::kutnohorsko::prepadeniVlasskehoDvora::h::jindra__vlassky_dvur_ow::osvobozeni_panu::cin_m4630k_prepadenidvora__rescue_lords|cutscenehandler1|Barbora.kutnohorsko.prepadeniVlasskehoDvora.h.jindra__vlassky_dvur_ow.osvobozeni_panu.cin_m4630k_prepadenidvora__rescue_lords.cutscenehandler1
        prepadeniVlasskehoDvora_lordsBrotherDead|prepadeniVlasskehoDvora_cutscene_ow_lordsBrotherDead|brambora::Barbora::kutnohorsko::prepadeniVlasskehoDvora::h::jindra__vlassky_dvur_ow::osvobozeni_panu::cin_m4630k_prepadenidvora__rescue_lords|cutscenehandler1_1|Barbora.kutnohorsko.prepadeniVlasskehoDvora.h.jindra__vlassky_dvur_ow.osvobozeni_panu.cin_m4630k_prepadenidvora__rescue_lords.cutscenehandler1_1
        prepadeniVlasskehoDvora_saveHenry|prepadeniVlasskehoDvora_CS_henrySaved|brambora::Barbora::kutnohorsko::prepadeniVlasskehoDvora::h::jindra__obrana_vlasskeho_dvora::cin_m4645k_prepadenidvora__henry_caught|CH_henryCaught|Barbora.kutnohorsko.prepadeniVlasskehoDvora.h.jindra__obrana_vlasskeho_dvora.cin_m4645k_prepadenidvora__henry_caught.CH_henryCaught
        prepadeniVlasskehoDvora_saveHenry|prepadeniVlasskehoDvora_CS_henrySaved|brambora::Barbora::kutnohorsko::prepadeniVlasskehoDvora::hastes|cutscenehandler11|Barbora.kutnohorsko.prepadeniVlasskehoDvora.hastes.cutscenehandler11
        prepadeniVlasskehoDvora_sex|prepadeniVlasskehoDvora_cutscene_sex|brambora::Barbora::kutnohorsko::prepadeniVlasskehoDvora::h::stealth::jindra__zachrana_komara::cin_m4615k_prepadenidvora__cook_sex|cutscenehandler1|Barbora.kutnohorsko.prepadeniVlasskehoDvora.h.stealth.jindra__zachrana_komara.cin_m4615k_prepadenidvora__cook_sex.cutscenehandler1
        prepadeniVlasskehoDvora_sexCensured|prepadeniVlasskehoDvora_cutscene_sexCensured|brambora::Barbora::kutnohorsko::prepadeniVlasskehoDvora::h::stealth::jindra__zachrana_komara::cin_m4615k_prepadenidvora__cook_sex|cutscenehandler1_1|Barbora.kutnohorsko.prepadeniVlasskehoDvora.h.stealth.jindra__zachrana_komara.cin_m4615k_prepadenidvora__cook_sex.cutscenehandler1_1
        prepadeniVlasskehoDvora_treasury|prepadeniVlasskehoDvora_cutscene_ow_treasury|brambora::Barbora::kutnohorsko::prepadeniVlasskehoDvora::h::jindra__vlassky_dvur_ow::otevreni_pokladnice::cin_m4635k_prepadenidvora__open_treasury|cutscenehandler1|Barbora.kutnohorsko.prepadeniVlasskehoDvora.h.jindra__vlassky_dvur_ow.otevreni_pokladnice.cin_m4635k_prepadenidvora__open_treasury.cutscenehandler1
        prepadeniVlasskehoDvora_wagonChase|prepadeniVlasskehoDvora_cutscene_wagonChase|brambora::Barbora::kutnohorsko::prepadeniVlasskehoDvora::h::cin_m4665k_prepadenidvora__wagon_chase|cutscenehandler1|Barbora.kutnohorsko.prepadeniVlasskehoDvora.h.cin_m4665k_prepadenidvora__wagon_chase.cutscenehandler1
        prepadeniVlasskehoDvora_wagonChase|prepadeniVlasskehoDvora_cutscene_wagonChase|brambora::Barbora::kutnohorsko::prepadeniVlasskehoDvora::hastes|cutscenehandler11_1|Barbora.kutnohorsko.prepadeniVlasskehoDvora.hastes.cutscenehandler11_1
        prepadeniVlasskehoDvora_zizkaRobbery|prepadeniVlasskehoDvora_cutscene_zizkaRobbery|brambora::Barbora::kutnohorsko::prepadeniVlasskehoDvora::h::stealth::jindra__propojeni_chodby::cin_m4625k_prepadenidvora__zizka_robbery|cutscenehandler3|Barbora.kutnohorsko.prepadeniVlasskehoDvora.h.stealth.jindra__propojeni_chodby.cin_m4625k_prepadenidvora__zizka_robbery.cutscenehandler3
        prijezdNaSuchdol_arrivalCutscene|prijezdNaSuchdol_arrivalCutscene|brambora::Barbora::kutnohorsko::prijezdNaSuchdol::prijezd::cin_m3110k_prijezdnasuchdol__arrival_suchdol::leave_arrival_area_trigger|cutscenehandler3|Barbora.kutnohorsko.prijezdNaSuchdol.prijezd.cin_m3110k_prijezdnasuchdol__arrival_suchdol.leave_arrival_area_trigger.cutscenehandler3
        prijezdNaSuchdol_jostCutscenePart1|prijezdNaSuchdol_jostAudiencePartOne|brambora::Barbora::kutnohorsko::prijezdNaSuchdol::na_tvrzi::audience::cin_m3120k_prijezdnasuchdol__jost_audience|cutscenehandler3|Barbora.kutnohorsko.prijezdNaSuchdol.na_tvrzi.audience.cin_m3120k_prijezdnasuchdol__jost_audience.cutscenehandler3
        prijezdNaSuchdol_jostCutscenePart2|prijezdNaSuchdol_jostAudiencePartTwo|brambora::Barbora::kutnohorsko::prijezdNaSuchdol::na_tvrzi::audience::cin_m3140k_prijezdnasuchdol__audience_zizka|cutscenehandler3|Barbora.kutnohorsko.prijezdNaSuchdol.na_tvrzi.audience.cin_m3140k_prijezdnasuchdol__audience_zizka.cutscenehandler3
        puvodNemoci_closingMonastery|closingMonasteryCutscene|brambora::Barbora::klaster::klaster_hibernate_mode::puvodNemoci::cin_s9020r_monastery__closing_monastery|cutscenehandler1|Barbora.klaster.dlc3.puvodNemoci.cin_s9020r_monastery__closing_monastery.cutscenehandler1
        puvodNemoci_initialMonasteryCutscene|initialMonasteryCutscene|brambora::Barbora::klaster::klaster_hibernate_mode::puvodNemoci::cin_s9010r_monastery__monastery_intro|cutscenehandler1|Barbora.klaster.dlc3.puvodNemoci.cin_s9010r_monastery__monastery_intro.cutscenehandler1
        rutinaAVypad_cutscene_assaultResult|rutinaAVypad_cutscene_assaultResult|brambora::Barbora::kutnohorsko::rutinaAVypad::hibernace::rutina_oblehani__open_world_a_vypad::cin_m4840k_oblehanisuchdol__assault_result|cutscenehandler1|Barbora.kutnohorsko.rutinaAVypad.hibernace.rutina_oblehani__open_world_a_vypad.cin_m4840k_oblehanisuchdol__assault_result.cutscenehandler1
        rutinaAVypad_cutscene_siegeContinues|rutinaAVypad_cutscene_siegeContinues|brambora::Barbora::kutnohorsko::rutinaAVypad::hibernace::cin_m4830k_oblehanisuchdol__siege_continues|cutscenehandler1|Barbora.kutnohorsko.rutinaAVypad.hibernace.cin_m4830k_oblehanisuchdol__siege_continues.cutscenehandler1
        sedmStatecnych2_cutscene_hangmanHill|sedmStatecnych2_hangmanHill|brambora::Barbora::kutnohorsko::sedmStatecnych2::h::komar::na_paloucku::cin_m3810k_sedmstatecnychdva__hangman_hill|cutscenehandler1|Barbora.kutnohorsko.sedmStatecnych2.h.komar.na_paloucku.prichod_na_paloucek.cutscenehandler1
        sedmStatecnych2_cutscene_komarHanged|sedmStatecnych2_komarHanged|brambora::Barbora::kutnohorsko::sedmStatecnych2::h::komar::na_paloucku::cin_m3820k_sedmstatecnychdva__komar_hanged|cutscenehandler2|Barbora.kutnohorsko.sedmStatecnych2.h.komar.na_paloucku.obeseni_komara.cutscenehandler2
        sedmStatecnych2_cutscene_komarHanged_shot|sedmStatecnych2_komarHanged_shot|brambora::Barbora::kutnohorsko::sedmStatecnych2::h::komar::na_paloucku::cin_m3820k_sedmstatecnychdva__komar_hanged|cutscenehandler2_1|Barbora.kutnohorsko.sedmStatecnych2.h.komar.na_paloucku.obeseni_komara.cutscenehandler2_1
        sedmStatecnych_assault|sedmStatecnych_assault|brambora::Barbora::kutnohorsko::sedmStatecnych::h::zachrana_certa::cin_m3220k_sedmstatecnych__ambush_cert|cutscenehandler1|Barbora.kutnohorsko.sedmStatecnych.h.zachrana_certa.cin_m3220k_sedmstatecnych__ambush_cert.cutscenehandler1
        sedmStatecnych_meetingWithKubenka|sedmStatecnych_meetingWithKubenka|brambora::Barbora::kutnohorsko::sedmStatecnych::h::prijezd_do_certovky::cin_m3210k_sedmstatecnych__meet_kubenka|cutscenehandler1|Barbora.kutnohorsko.sedmStatecnych.h.prijezd_do_certovky.cin_m3210k_sedmstatecnych__meet_kubenka.cutscenehandler1
        sermiri_fencersFight|sermiri_fencersFight|brambora::Barbora::kutnohorsko::sermiri::souboje::turnajova_cast::turnaj::turnaj::cin_s3970k_sermiri__fencers_fight|arne_mikulda_fight|Barbora.kutnohorsko.sermiri.souboje.turnajova_cast.turnaj.turnaj.soubojova_cs.arne_mikulda_fight
        sermiri_firstConfrontation|sermiri_firstCutscene|brambora::Barbora::kutnohorsko::sermiri::cin_s3950k_sermiri__first_confrontation|cutscenehandler1|Barbora.kutnohorsko.sermiri.cs_prvni_konfrontace.cutscenehandler1
        sermiri_townhallChallenge|sermiri_secondCutscene|brambora::Barbora::kutnohorsko::sermiri::cin_s3960k_sermiri__townhall_challenge|cutscenehandler1|Barbora.kutnohorsko.sermiri.vyzva_na_radnici.cutscenehandler1
        setkaniVRatbori1_retinueLeave_cutscene|setkaniVRatbori1_retinueLeave_CS|brambora::Barbora::kutnohorsko::setkaniVRatbori1::quest_init::odstreamuj_uvodni_skupinu|cutscenehandler1|Barbora.kutnohorsko.setkaniVRatbori1.quest_init.odstreamuj_uvodni_skupinu.cutscenehandler1
        setkaniVRatbori1_rozporVKoalici|setkaniVRatbori_rat1_rozporCS|brambora::Barbora::kutnohorsko::setkaniVRatbori1::h::bohuta_na_ratbori::cin_m3740k_setkaniratbor__coalition_dispute|cutscenehandler1|Barbora.kutnohorsko.setkaniVRatbori1.h.bohuta_na_ratbori_1.cin_m3740k_setkaniratbor__coalition_dispute.cutscenehandler1
        setkaniVRatbori1_start_cutscene|setkaniVRatbori1_start_cutscene|brambora::Barbora::kutnohorsko::setkaniVRatbori1::quest_init::cin_m3710k_setkaniratbor__rattay_gathering|cutscenehandler1|Barbora.kutnohorsko.setkaniVRatbori1.quest_init.cin_m3710k_setkaniratbor__rattay_gathering.cutscenehandler1
        setkaniVRatbori1_transferCouncil|setkaniVRatbori1_kh2_trasferCouncilCutscene|brambora::Barbora::kutnohorsko::setkaniVRatbori1::h::kunohorsky_snem_2::cin_m3760k_setkaniratbor__transfer_council|cutscenehandler1|Barbora.kutnohorsko.setkaniVRatbori1.h.kunohorsky_snem_2_2.cin_m3760k_setkaniratbor__transfer_council.cutscenehandler1
        setkaniVRatbori1_transferRatbor|setkaniVRatbori_rat1_startCH|brambora::Barbora::kutnohorsko::setkaniVRatbori1::h::bohuta_na_ratbori::cin_m3730k_setkaniratbor__transfer_ratbor|cutscenehandler4|Barbora.kutnohorsko.setkaniVRatbori1.h.bohuta_na_ratbori_1.cin_m3730k_setkaniratbor__transfer_ratbor.cutscenehandler4
        setkaniVRatbori1_zaverRady|setkaniVRatbori_rat1_zaverRady|brambora::Barbora::kutnohorsko::setkaniVRatbori1::h::bohuta_na_ratbori::cin_m3750k_setkaniratbor__rattay_leaving|cutscenehandler1|Barbora.kutnohorsko.setkaniVRatbori1.h.bohuta_na_ratbori_1.cin_m3750k_setkaniratbor__rattay_leaving.cutscenehandler1
        setkaniVRatbori1_zikmundIntro|setkaniVRatbori1_zikmundIntroCutscene|brambora::Barbora::kutnohorsko::setkaniVRatbori1::h::kunohorsky_snem_1::cin_m3720k_setkaniratbor__zikmund_intro|cutscenehandler1|Barbora.kutnohorsko.setkaniVRatbori1.h.kunohorsky_snem_1.cin_m3720k_setkaniratbor__zikmund_intro.cutscenehandler1
        setkaniVRatbori2_henryArrives_cutscene|setkaniVRatbori2_henryArrival|brambora::Barbora::kutnohorsko::setkaniVRatbori2::hibernable::cin_m3790k_setkaniratbordva__henry_arrives|cutscenehandler1|Barbora.kutnohorsko.setkaniVRatbori2.hibernable.cin_m3790k_setkaniratbordva__henry_arrives.cutscenehandler1
        setkaniVRatbori2_partyStart_cutscene|setkaniVRatbori2_party|brambora::Barbora::kutnohorsko::setkaniVRatbori2::hibernable::cin_m3770k_setkaniratbor__night_ratbor|cutscenehandler1|Barbora.kutnohorsko.setkaniVRatbori2.hibernable.cin_m3770k_setkaniratbor__night_ratbor.cutscenehandler1
        setkaniVRatbori2_ratborAttack_cutscene|setkaniVRatbori2_ratborAttack|brambora::Barbora::kutnohorsko::setkaniVRatbori2::hibernable::cin_m3780k_setkaniratbordva__ratbor_attack|cutscenehandler1|Barbora.kutnohorsko.setkaniVRatbori2.hibernable.cin_m3780k_setkaniratbordva__ratbor_attack.cutscenehandler1
        socky_2_gate|socky_cs_2_gate|brambora::Barbora::trosecko::socky::hibernable::cin_m0320t_socky__gate_dialogue|cutscenehandler1|Barbora.trosecko.socky.hibernable.cin_m0320t_socky__gate_dialogue.cutscenehandler1
        socky_3_tavern|socky_cs_3_tavern|brambora::Barbora::trosecko::socky::hibernable::cin_m0330t_socky__tavern_dialogue|cutscenehandler1|Barbora.trosecko.socky.hibernable.cin_m0330t_socky__tavern_dialogue.cutscenehandler1
        socky_4_katerina|socky_cs_4_katerina|brambora::Barbora::trosecko::socky::hibernable::v_hospode::s_katerinou::cin_m0340t_socky__meet_katerina|cutscenehandler1|Barbora.trosecko.socky.hibernable.v_hospode.s_katerinou.cin_m0340t_socky__meet_katerina.cutscenehandler1
        socky_5_departure|socky_cs_5_departure|brambora::Barbora::trosecko::socky::hibernable::v_hospode::cin_m0350t_socky__katerina_leaves|cutscenehandler1|Barbora.trosecko.socky.hibernable.v_hospode.cin_m0350t_socky__katerina_leaves.cutscenehandler1
        socky_6_pillary|socky_cs_6_pillory|brambora::Barbora::trosecko::socky::hibernable::pranyr::cin_m0360t_socky__stocks_dialogue|cutscenehandler1|Barbora.trosecko.socky.hibernable.pranyr.cin_m0360t_socky__stocks_dialogue.cutscenehandler1
        socky_7_bergov|socky_cs_7_bergov|brambora::Barbora::trosecko::socky::hibernable::pranyr::cin_m0370t_socky__stocks_release|cutscenehandler1|Barbora.trosecko.socky.hibernable.pranyr.cin_m0370t_socky__stocks_release.cutscenehandler1
        spizovaciOddil_roundUp|cin_spizovaciOddil_roundUp|brambora::Barbora::kutnohorsko::spizovaciOddil::qAct::cesta_do_vesnice::cin_s3150k_spizovacioddil__ransack_village|cutscenehandler121145124|Barbora.kutnohorsko.spizovaciOddil.qAct.cesta_do_vesnice.prijezd_do_vesnice.cutscenehandler121145124
        stareKosti_tombOpening|stareKosti_cutscene_tombEntrance|brambora::Barbora::kutnohorsko::stareKosti::hibernation::hledani_kosti_a_zajimava_mista::katakomby::cin_s3750k_starekosti__catacombs_fall|cutscenehandler1|Barbora.kutnohorsko.stareKosti.hibernation.hledani_kosti_a_zajimava_mista.katakomby.henry_spadne_do_katakomb.cutscenehandler1
        stealthMiseZaJindru_aulitzBrutal|stealthMiseZaJindru_aulitzBrutal|brambora::Barbora::kutnohorsko::stealthMiseZaJindru::hibernable::aulitzuv_dum::cin_m4950k_stealthmise__aulitz_brutal|cutscenehandler1|Barbora.kutnohorsko.stealthMiseZaJindru.hibernable.aulitzuv_dum.cin_m4950k_stealthmise__aulitz_brutal.cutscenehandler1
        stealthMiseZaJindru_aulitzIntro|stealthMiseZaJindru_aulitzIntro|brambora::Barbora::kutnohorsko::stealthMiseZaJindru::hibernable::aulitzuv_dum::cin_m4930k_stealthmise__aulitz_intro|cutscenehandler1|Barbora.kutnohorsko.stealthMiseZaJindru.hibernable.aulitzuv_dum.cin_m4930k_stealthmise__aulitz_intro.cutscenehandler1
        stealthMiseZaJindru_aulitzKill|stealthMiseZaJindru_aulitzKill|brambora::Barbora::kutnohorsko::stealthMiseZaJindru::hibernable::aulitzuv_dum::cin_m4940k_stealthmise__aulitz_kill|cutscenehandler1|Barbora.kutnohorsko.stealthMiseZaJindru.hibernable.aulitzuv_dum.cin_m4940k_stealthmise__aulitz_kill.cutscenehandler1
        stealthMiseZaJindru_aulitzSpare|stealthMiseZaJindru_aulitzSpare|brambora::Barbora::kutnohorsko::stealthMiseZaJindru::hibernable::aulitzuv_dum::cin_m4960k_stealthmise__aulitz_spare|cutscenehandler1|Barbora.kutnohorsko.stealthMiseZaJindru.hibernable.aulitzuv_dum.cin_m4960k_stealthmise__aulitz_spare.cutscenehandler1
        stealthMiseZaJindru_finishWithoutSam|stealthMiseZaJindru_finishWithoutSam|brambora::Barbora::kutnohorsko::stealthMiseZaJindru::hibernable::kone_a_odjezd::cin_m4920k_stealthmise__leaving_suchdol|cutscenehandler4|Barbora.kutnohorsko.stealthMiseZaJindru.hibernable.kone_a_odjezd.cin_m4920k_stealthmise__leaving_suchdol.cutscenehandler4
        stealthMiseZaJindru_finishWithSam|stealthMiseZaJindru_finishWithSam|brambora::Barbora::kutnohorsko::stealthMiseZaJindru::hibernable::kone_a_odjezd::cin_m4920k_stealthmise__leaving_suchdol|cutscenehandler4_1|Barbora.kutnohorsko.stealthMiseZaJindru.hibernable.kone_a_odjezd.cin_m4920k_stealthmise__leaving_suchdol.cutscenehandler4_1
        stealthMiseZaJindru_openingCutscene|stealthMiseZaJindru_openingCutscene|brambora::Barbora::kutnohorsko::stealthMiseZaJindru::hibernable::cin_m4910k_stealthmise__abseil_down|cutscenehandler1|Barbora.kutnohorsko.stealthMiseZaJindru.hibernable.cin_m4910k_stealthmise__abseil_down.cutscenehandler1
        svatba_fightWedding|svatba_fightWedding|brambora::Barbora::trosecko::svatba::hibernovana_cast::cin_m0520t_svatba__fight_wedding|cutscenehandler1|Barbora.trosecko.svatba.hibernovana_cast.rvacka_na_svatbe.cutscenehandler1
        svatba_guardsArrival|svatba_guardsArrival|brambora::Barbora::trosecko::svatba::hibernovana_cast::cin_m0530t_svatba__guards_arrival|cutscenehandler1|Barbora.trosecko.svatba.hibernovana_cast.prichod_strazi.cutscenehandler1
        svatba_intermezzoBohuta_censored|svatba_intermezzoBohuta_censored|brambora::Barbora::trosecko::svatba::hibernovana_cast::prvni_bohutova_vlozka_v_uzicich::cin_m0540t_svatba__intermezzo_bohuta|cutscenehandler1|Barbora.trosecko.svatba.hibernovana_cast.prvni_bohutova_vlozka_v_uzicich.prichod_racka_a_hanuse.cutscenehandler1
        svatba_intermezzoBohuta_uncensored|svatba_intermezzoBohuta_uncensored|brambora::Barbora::trosecko::svatba::hibernovana_cast::prvni_bohutova_vlozka_v_uzicich::cin_m0540t_svatba__intermezzo_bohuta|cutscenehandler1|Barbora.trosecko.svatba.hibernovana_cast.prvni_bohutova_vlozka_v_uzicich.prichod_racka_a_hanuse.cutscenehandler1
        svatba_weddingCeremony|svatba_weddingCeremony|brambora::Barbora::trosecko::svatba::hibernovana_cast::cin_m0510t_svatba__wedding_ceremony|cutscenehandler2|Barbora.trosecko.svatba.hibernovana_cast.obrad.cutscenehandler2
        svatyne_painting|svatyne_paintingCutscene|brambora::Barbora::trosecko::brushes_with_death_trosecko::svatyne::malir__konec_questu|cutscenehandler19|Barbora.trosecko.brushes_with_death_trosecko.svatyne.malir__konec_questu.cutscenehandler19
        tarasMura_exitMines_both|tarasMura_leavingMines_both_CS|brambora::Barbora::kutnohorsko::tarasMura::hlavni_cast_questu::debrief_po_vylezu_z_dolu::cin_s4060k_tarasmura__exit_mines|cutscenehandler1_1_1|Barbora.kutnohorsko.tarasMura.hlavni_cast_questu.debrief_po_vylezu_z_dolu.vylez_z_dolu.cutscenehandler1_1_1
        tarasMura_exitMines_henry|tarasMura_leavingMines_henry_CS|brambora::Barbora::kutnohorsko::tarasMura::hlavni_cast_questu::debrief_po_vylezu_z_dolu::cin_s4060k_tarasmura__exit_mines|cutscenehandler1_1|Barbora.kutnohorsko.tarasMura.hlavni_cast_questu.debrief_po_vylezu_z_dolu.vylez_z_dolu.cutscenehandler1_1
        tarasMura_exitMines_taras|tarasMura_leavingMines_taras_CS|brambora::Barbora::kutnohorsko::tarasMura::hlavni_cast_questu::debrief_po_vylezu_z_dolu::cin_s4060k_tarasmura__exit_mines|cutscenehandler1_1_2|Barbora.kutnohorsko.tarasMura.hlavni_cast_questu.debrief_po_vylezu_z_dolu.vylez_z_dolu.cutscenehandler1_1_2
        tarasMura_shaftFall_noquest_end|tarasMura_padDoDolu_noquest_end_CS2|brambora::Barbora::kutnohorsko::tarasMura::hlavni_cast_questu::cin_s4050k_tarasmura__shaft_fall|cutscenehandler1_1_1|Barbora.kutnohorsko.tarasMura.hlavni_cast_questu.jindrich_pada_do_sachty.cutscenehandler1_1_1
        tarasMura_shaftFall_quest|tarasMura_padDoDolu_quest_CS|brambora::Barbora::kutnohorsko::tarasMura::hlavni_cast_questu::cin_s4050k_tarasmura__shaft_fall|cutscenehandler1_1|Barbora.kutnohorsko.tarasMura.hlavni_cast_questu.jindrich_pada_do_sachty.cutscenehandler1_1
        truhlari_BurnPainting_ingame|truhlari_CutsceneBurnPainting|brambora::Barbora::kutnohorsko::brushes_with_death_kutnohorsko::truhlari::konec_questu::vysledne_cutsceny|cutscenehandler1_1|Barbora.kutnohorsko.brushes_with_death_kutnohorsko.truhlari.konec_questu.vysledne_cutsceny.cutscenehandler1_1
        truhlari_FinishPaintingHappy_ingame|truhlari_CutsceneFinishPaintingHappyHolder|brambora::Barbora::kutnohorsko::brushes_with_death_kutnohorsko::truhlari::konec_questu::vysledne_cutsceny|cutscenehandler1_1|Barbora.kutnohorsko.brushes_with_death_kutnohorsko.truhlari.konec_questu.vysledne_cutsceny.cutscenehandler1_1
        truhlari_FinishPaintingSad_ingame|truhlari_CutsceneFinishPaintingSadHolder|brambora::Barbora::kutnohorsko::brushes_with_death_kutnohorsko::truhlari::konec_questu::vysledne_cutsceny|cutscenehandler1_1|Barbora.kutnohorsko.brushes_with_death_kutnohorsko.truhlari.konec_questu.vysledne_cutsceny.cutscenehandler1_1
        uchazec_forgeFlashback|uchazec_introCS|brambora::Barbora::kutnohorsko::legacy_of_the_forge::uchazec::h::uvod::cin_s8010k_forge__forge_flashback|cutscenehandler4|Barbora.kutnohorsko.legacy_of_the_forge.uchazec.h.uvod.cin_s8010k_forge__forge_flashback.cutscenehandler4
        utokNaMalesov_cannonFired|utokNaMalesov_cannonFired|brambora::Barbora::kutnohorsko::utokNaMalesov::hibernovana_cast::tvrz::vnejsek::intermezzo_na_predhradi::cin_m4450k_malesov__cannon_fired|cutscenehandler15|Barbora.kutnohorsko.utokNaMalesov.hibernovana_cast.tvrz.vnejsek.intermezzo_na_predhradi.cin_m4450k_malesov__cannon_fired.cutscenehandler15
        utokNaMalesov_duelWithCertEnd|utokNaMalesov_duelWithCertEnd|brambora::Barbora::kutnohorsko::utokNaMalesov::hibernovana_cast::ves_malesov::cin_m4440k_malesov__cert_duel|cutscenehandler1_2|Barbora.kutnohorsko.utokNaMalesov.hibernovana_cast.ves_malesov.cin_m4440k_malesov__cert_duel.cutscenehandler1_2
        utokNaMalesov_duelWithCertLost|utokNaMalesov_duelWithCertLost|brambora::Barbora::kutnohorsko::utokNaMalesov::hibernovana_cast::ves_malesov::cin_m4440k_malesov__cert_duel|cutscenehandler1|Barbora.kutnohorsko.utokNaMalesov.hibernovana_cast.ves_malesov.cin_m4440k_malesov__cert_duel.cutscenehandler1
        utokNaMalesov_duelWithCertWon|utokNaMalesov_duelWithCertWon|brambora::Barbora::kutnohorsko::utokNaMalesov::hibernovana_cast::ves_malesov::cin_m4440k_malesov__cert_duel|cutscenehandler1_1|Barbora.kutnohorsko.utokNaMalesov.hibernovana_cast.ves_malesov.cin_m4440k_malesov__cert_duel.cutscenehandler1_1
        utokNaMalesov_killingVillager|utokNaMalesov_killingVillager|brambora::Barbora::kutnohorsko::utokNaMalesov::hibernovana_cast::ves_malesov::cin_m4437k_malesov__killing_villager|cutscenehandler1|Barbora.kutnohorsko.utokNaMalesov.hibernovana_cast.ves_malesov.cin_m4437k_malesov__killing_villager.cutscenehandler1
        utokNaMalesov_malesovConqueredNoRozaRomance|utokNaMalesov_malesovConqueredNoRozaRomance|brambora::Barbora::kutnohorsko::utokNaMalesov::hibernovana_cast::tvrz::finalni_boj_o_hrad::cin_m4480k_malesov__malesov_conquered|cutscenehandler1|Barbora.kutnohorsko.utokNaMalesov.hibernovana_cast.tvrz.finalni_boj_o_hrad.cin_m4480k_malesov__malesov_conquered.cutscenehandler1
        utokNaMalesov_malesovConqueredRozaRomance|utokNaMalesov_malesovConqueredRozaRomance|brambora::Barbora::kutnohorsko::utokNaMalesov::hibernovana_cast::tvrz::finalni_boj_o_hrad::cin_m4480k_malesov__malesov_conquered|cutscenehandler1|Barbora.kutnohorsko.utokNaMalesov.hibernovana_cast.tvrz.finalni_boj_o_hrad.cin_m4480k_malesov__malesov_conquered.cutscenehandler1
        utokNaNebakov_cutscene_march|utokNaNebakov_cutscene_march|brambora::Barbora::trosecko::utokNaNebakov::hibernovana_gameplay::cin_m0910t_utoknebakov__nebakov_march|cutscenehandler1|Barbora.trosecko.utokNaNebakov.hibernovana_gameplay.cin_m0910t_utoknebakov__nebakov_march.cutscenehandler1
        utokNaNebakov_valley_afterDuel|utokNaNebakov_valley_afterDuel|brambora::Barbora::trosecko::utokNaNebakov::hibernovana_gameplay::prepadeni_v_rokli::souboj_se_zizkou::cin_m0930t_utoknebakov__after_duel|cutscenehandler1_1|Barbora.trosecko.utokNaNebakov.hibernovana_gameplay.prepadeni_v_rokli.souboj_se_zizkou.cin_m0930t_utoknebakov__after_duel.cutscenehandler1_1
        utokNaNebakov_valley_beforeDuel|utokNaNebakov_valley_beforeDuel|brambora::Barbora::trosecko::utokNaNebakov::hibernovana_gameplay::prepadeni_v_rokli::souboj_se_zizkou::cin_m0920t_utoknebakov__zizka_duel|cutscenehandler1|Barbora.trosecko.utokNaNebakov.hibernovana_gameplay.prepadeni_v_rokli.souboj_se_zizkou.cin_m0920t_utoknebakov__zizka_duel.cutscenehandler1
        vezniNaTroskach_erikLeaves|vezniNaTroskach_erikLeavesCutscene|brambora::Barbora::trosecko::vezniNaTroskach::hibernable::trosky_pred_bouri::cin_m1230t_vezninatroskach__erik_leaves|cutscenehandler4|Barbora.trosecko.vezniNaTroskach.hibernable.trosky_pred_bouri.cin_m1230t_vezninatroskach__erik_leaves.cutscenehandler4
        vezniNaTroskach_katerinaInterrupt|vezniNaTroskach_katerinaInterruptCutscene|brambora::Barbora::trosecko::vezniNaTroskach::hibernable::v_mucirne::cin_m1220t_vezninatroskach__katerina_interrupt|cutscenehandler1|Barbora.trosecko.vezniNaTroskach.hibernable.v_mucirne.cin_m1220t_vezninatroskach__katerina_interrupt.cutscenehandler1
        vezniNaTroskach_pistaChamberIntro|vezniNaTroskach_pistaChamberIntroCutscene|brambora::Barbora::trosecko::vezniNaTroskach::vezninatroskachlibrary::souls_areatriggeredcutscene|cutscenehandler1|Barbora.trosecko.vezniNaTroskach.hibernable.trosky_pred_bouri.baba.pista.cin_m1240t_vezninatroskach__pista_chamber.souls_areatriggeredcutscene.cutscenehandler1
        vezniNaTroskach_pistaDefeatDuel_alternativeWeapon|vezniNaTroskach_pistaDefeatDuel_alternativeMeleeWeapon_Cutscene|brambora::Barbora::trosecko::vezniNaTroskach::hibernable::trosky_pred_bouri::baba::pista::cin_m1250t_vezninatroskach__pista_defeat|cutscenehandler25_1|Barbora.trosecko.vezniNaTroskach.hibernable.trosky_pred_bouri.baba.pista.cin_m1250t_vezninatroskach__pista_defeat_1.cutscenehandler25_1
        vezniNaTroskach_pistaDefeatDuel_sword|vezniNaTroskach_pistaDefeatDuel_sword_Cutscene|brambora::Barbora::trosecko::vezniNaTroskach::hibernable::trosky_pred_bouri::baba::pista::cin_m1250t_vezninatroskach__pista_defeat|cutscenehandler25|Barbora.trosecko.vezniNaTroskach.hibernable.trosky_pred_bouri.baba.pista.cin_m1250t_vezninatroskach__pista_defeat_1.cutscenehandler25
        vezniNaTroskach_pistaDefeatDuel_unarmedOrRanged|vezniNaTroskach_pistaDefeatDuel_unarmedOrRanged_Cutscene|brambora::Barbora::trosecko::vezniNaTroskach::hibernable::trosky_pred_bouri::baba::pista::cin_m1250t_vezninatroskach__pista_defeat|cutscenehandler36|Barbora.trosecko.vezniNaTroskach.hibernable.trosky_pred_bouri.baba.pista.cin_m1250t_vezninatroskach__pista_defeat_1.cutscenehandler36
        vezniNaTroskach_pistaDefeatWindow|vezniNaTroskach_pistaDefeatWindowCutscene|brambora::Barbora::trosecko::vezniNaTroskach::hibernable::trosky_pred_bouri::baba::pista::cin_m1250t_vezninatroskach__pista_defeat|cutscenehandler1|Barbora.trosecko.vezniNaTroskach.hibernable.trosky_pred_bouri.baba.pista.cin_m1250t_vezninatroskach__pista_defeat_1.cutscenehandler1
        vezniNaTroskach_prisonersConvoy_ingame|vezniNaTroskach_prisonersConvoyCutscene_ingame|brambora::Barbora::trosecko::vezniNaTroskach::hibernable::cin_m1210t_vezninatroskach__prisoners_convoy|cutscenehandler1|Barbora.trosecko.vezniNaTroskach.hibernable.cin_m1210t_vezninatroskach__prisoners_convoy.cutscenehandler1
        vezniNaTroskach_zikmundLetter|vezniNaTroskach_zikmundLetterCutscene|brambora::Barbora::trosecko::vezniNaTroskach::hibernable::tajna_chodba::cin_m1280t_vezninatroskach__zikmund_letter|readingZikmundLettersCS|Barbora.trosecko.vezniNaTroskach.hibernable.tajna_chodba.cin_m1280t_vezninatroskach__zikmund_letter.readingZikmundLettersCS
        vlasskaTransmise_RepairBeginning|vlasskaTransmise_repairBeginningCS|brambora::Barbora::kutnohorsko::legacy_of_the_forge::vlasskaTransmise::oprava_orloje::u_rychtare::cin_s8020k_forge__clocktower_intro|cutscenehandler1|Barbora.kutnohorsko.legacy_of_the_forge.vlasskaTransmise.oprava_orloje.u_rychtare.cin_s8020k_forge__clocktower_intro.cutscenehandler1
        vlasskaTransmise_RepairFinnish|vlasskaTransmise_repairFinnishCS|brambora::Barbora::kutnohorsko::legacy_of_the_forge::vlasskaTransmise::oprava_orloje::u_rychtare::cin_s8030k_forge__clocktower_reconstructed|cutscenehandler1|Barbora.kutnohorsko.legacy_of_the_forge.vlasskaTransmise.oprava_orloje.u_rychtare.cin_s8030k_forge__clocktower_reconstructed.cutscenehandler1
        zachrana_cestaPoBrehu|zachrana_cutscene_cestaPoBrehu|brambora::Barbora::trosecko::zachrana::hibernace::cutsceny_na_zacatku_questu::cin_m0210t_zachrana__fall_dream|cutscenehandler10_2|Barbora.trosecko.zachrana.hibernace.cutsceny_na_zacatku_questu.probuzeni_ve_skalach.cutscenehandler10_2
        zachrana_prespani|zachrana_cutscene_prespani|brambora::Barbora::trosecko::zachrana::hibernace::treti_den::cutscena_a_dialog_s_dcerou|cutscenehandler152024541|Barbora.trosecko.zachrana.hibernace.treti_den.cutscena_a_dialog_s_dcerou.cutscenehandler152024541
        zachrana_prichodKeKorenarce|zachrana_cutscene_prichodKeKorenarce|brambora::Barbora::trosecko::zachrana::hibernace::cutsceny_na_zacatku_questu::cin_m0240t_zachrana__herbalist_hut|cutscenehandler10_1|Barbora.trosecko.zachrana.hibernace.cutsceny_na_zacatku_questu.prichod_ke_korenarce.cutscenehandler10_1
        zachrana_probuzeni|zachrana_cutscene_probuzeni|brambora::Barbora::trosecko::zachrana::hibernace::cutsceny_na_zacatku_questu::cin_m0250t_zachrana__first_dreaming|cutscenehandler10|Barbora.trosecko.zachrana.hibernace.cutsceny_na_zacatku_questu.henryho_prvni_sneni_a_probuzeni_u_korenarky.cutscenehandler10
        zachranaPtacka_caponSaved|zachranaPtacka_caponSaved|brambora::Barbora::kutnohorsko::zachranaPtacka::vysvobozeni_ptacka::malesov::cin_m3520k_zachranaptacka__capon_saved|cutscenehandler3|Barbora.kutnohorsko.zachranaPtacka.vysvobozeni_ptacka.malesov.setkani_s_ptackem.cutscenehandler3
        zachranaPtacka_enemiesGather|zachranaPtacka_enemiesGather|brambora::Barbora::kutnohorsko::zachranaPtacka::vysvobozeni_ptacka::malesov::cin_m3510k_zachranaptacka__enemies_gather|cutscenehandler1|Barbora.kutnohorsko.zachranaPtacka.vysvobozeni_ptacka.malesov.jednani_na_malesove.cutscenehandler1
        zachranaPtacka_escapingMalesovThroughGate|zachranaPtacka_malesovEscapeThroughGate|brambora::Barbora::kutnohorsko::zachranaPtacka::vysvobozeni_ptacka::utek_z_malesova_na_ratbor::cin_m3530k_zachranaptacka__malesov_escape|cutscenehandler17|Barbora.kutnohorsko.zachranaPtacka.vysvobozeni_ptacka.utek_z_malesova_na_ratbor.utek_z_malesova.cutscenehandler17
        zachranaPtacka_escapingMalesovThroughSecretPassage|zachranaPtacka_malesovEscapeThroughSecretPassage|brambora::Barbora::kutnohorsko::zachranaPtacka::vysvobozeni_ptacka::utek_z_malesova_na_ratbor::cin_m3530k_zachranaptacka__malesov_escape|cutscenehandler7|Barbora.kutnohorsko.zachranaPtacka.vysvobozeni_ptacka.utek_z_malesova_na_ratbor.utek_z_malesova.cutscenehandler7
        zbranePanaSemina_beforeSecondFight|cutscene_beforeSecondFight|brambora::Barbora::trosecko::zbranePanaSemina::h::cin_s0170t_zbranesemina__raubritter_camp|cutscenehandler1|Barbora.trosecko.zbranePanaSemina.h.v_tabore.cutscenehandler1
        zbranePanaSemina_playerSukDialog|zbranePSemina_cutscenePlayerSukDialog|brambora::Barbora::trosecko::zbranePanaSemina::h::na_semine::cin_s0160t_zbranesemina__meet_semin|cutscenehandler4|Barbora.trosecko.zbranePanaSemina.h.na_semine.seznameni_se_seminem1.cutscenehandler4
        zbranePanaSemina_playerSukDialog2|zbranePSemina_cutscenePlayerSukDialog2|brambora::Barbora::trosecko::zbranePanaSemina::h::na_semine::cin_s0160t_zbranesemina__meet_semin|cutscenehandler4_1|Barbora.trosecko.zbranePanaSemina.h.na_semine.seznameni_se_seminem1.cutscenehandler4_1
        zbranePanaSemina_playerSukDialogFull|zbranePSemina_cutscenePlayerSukDialogFull|brambora::Barbora::trosecko::zbranePanaSemina::h::na_semine::cin_s0160t_zbranesemina__meet_semin|cutscenehandler4_2|Barbora.trosecko.zbranePanaSemina.h.na_semine.seznameni_se_seminem1.cutscenehandler4_2
        zbranePanaSemina_seminArrival|zbranePSemina_seminArrival|brambora::Barbora::trosecko::zbranePanaSemina::h::na_semine::cin_s0150t_zbranesemina__arrival_semin|cutscenehandler4|Barbora.trosecko.zbranePanaSemina.h.na_semine.na_semine.cutscenehandler4
        zoufalaObranaZaBohutu_initialCutscene|zoufalaObranaZaBohutu_initialCutscene|brambora::Barbora::kutnohorsko::zoufalaObranaZaBohutu::hibernable::q::bohuta_se_opije::cin_m4910k_stealthmise__abseil_down2|cutscenehandler4_4|Barbora.kutnohorsko.zoufalaObranaZaBohutu.hibernable.q.bohuta_se_opije.cin_m4910k_stealthmise__abseil_down2.cutscenehandler4_4
""";

    // generated from the game's Libs/Tables/ui/cutscene.xml (the IngameCutscene rows with a Time): name|time
    private const string TimeTable = """
        bohutovaVlozka_pistaRelease|17h30m
        budovaniLazni_velkaParty|23h30m
        budovaniLazni_velkaParty_censored|23h30m
        erik_nocNaHradbach|3h
        erik_odjezdMestanuAPrijezdKubenky|10h
        erik_odjezdMestanuAPrijezdKubenky_rozaRomance|10h
        erik_prijezdNaKopec|15h30m
        erik_porazkaErikaAPrijezdArmady|16h
        finale_victoryGathering|10h
        finale_treeDream|19h
        finale_acceptSwordEndingScene|19h30m
        finale_refuseSwordEndingScene|19h30m
        finale_zizkasEye|19h45m
        finale_zikmundPissed|2h
        havirskyTurnaj_competition_lost|9h20m
        havirskyTurnaj_competition_win|9h20m
        hladAZmar_hunger_despair|11h
        hladAZmar_bohuta_preaching|12h30m
        hladAZmar_bohuta_preaching_after_cryingWoman|12h30m
        hladAZmar_bohuta_preaching_after|12h30m
        hladAZmar_tower_seized|17h
        hladAZmar_court_siezed|17h
        hladAZmar_katerina_romance|22h30m
        hladAZmar_katerina_romance_JP|22h30m
        hladAZmar_ptacek_romance|23h
        hladAZmar_ptacek_romance_JP|23h
        hledaniLichtenstejna_courtTrap|3h
        hledaniLichtenstejna_courtTrap_inside|3h
        hledaniLichtenstejna_samuelWins|3h
        hledaniLichtenstejna_samuelLost|3h
        hledaniLichtenstejna_meetLichtenstejn|3h
        kubaParalu_MurdererRevealed_ingame|18h00m
        mucirna_vypaleniSemina_arrival|18h30m
        nebakovObrana_enemyArmy|6h09m
        nebakovObrana_startBattle|8h
        nebakovObrana_godFinger|8h
        nebakovObrana_godFingerShorter|8h
        papezskyLegat_nocSRozou|19h30m
        papezskyLegat_nocSRozou_noSex|19h30m
        papezskyLegat_prepadeniLegata|6h
        papezskyLegat_smrtLegata|6h15m
        pogrom_defendSynagogue|6h
        pogrom_blockadeFire|6h
        prepadeniVlasskehoDvora_courtHall|19h30m
        prepadeniVlasskehoDvora_erikArrives|23h
        prepadeniVlasskehoDvora_saveHenry|23h
        prepadeniVlasskehoDvora_explosiveTrap|23h
        prepadeniVlasskehoDvora_brabantTreason|23h
        prepadeniVlasskehoDvora_komarDeath|23h
        prepadeniVlasskehoDvora_wagonChase|22h45m
        rutinaAVypad_cutscene_siegeContinues|16h
        rutinaAVypad_cutscene_assaultResult|16h30m
        setkaniVRatbori1_start_cutscene|6h
        setkaniVRatbori1_retinueLeave_cutscene|6h
        setkaniVRatbori1_zikmundIntro|14h30m
        setkaniVRatbori1_transferRatbor|14h30m
        setkaniVRatbori1_transferCouncil|14h30m
        setkaniVRatbori1_rozporVKoalici|14h30m
        setkaniVRatbori1_zaverRady|14h30m
        setkaniVRatbori2_partyStart_cutscene|23h
        setkaniVRatbori2_ratborAttack_cutscene|23h
        setkaniVRatbori2_henryArrives_cutscene|23h
        socky_2_gate|15h
        socky_3_tavern|10h
        socky_4_katerina|10h
        socky_6_pillary|12h50m
        socky_6_pillory_ch01|12h50m
        socky_7_bergov|07h30m
        utokNaMalesov_pistaDreamGood|0h
        utokNaMalesov_pistaDreamEvil|0h
        utokNaNebakov_cutscene_march|7h30m
        utokNaNebakov_valley_beforeDuel|8h05m
        utokNaNebakov_valley_afterDuel|8h05m
        vezniNaTroskach_prisonersConvoy_ingame|19h30m
        vezniNaTroskach_katerinaInterrupt|20h
        cin_m4420k_zikmundtabor__cuman_murder|23h
        cin_m4430k_zikmundtabor__ambush_praguers_boomVar|7h30m
        cin_m4430k_zikmundtabor__ambush_praguers_no_boomVar|7h30m
        crime_beating_trosecko|8h
        crime_branding_trosecko|8h
        crime_beating_kutnahora|8h
        crime_branding_kutnahora|8h
        crime_beating_suchdol|8h
        crime_branding_suchdol|8h
        crime_beating_malesov|8h
        crime_branding_malesov|8h
        crime_beating_miskovice|8h
        crime_branding_miskovice|8h
        crime_beating_zikmundtabor|8h
        crime_branding_zikmundtabor|8h
        vlasskaTransmise_RepairBeginning|14h30m
        vlasskaTransmise_RepairFinnish|14h30m
        test_cin_soul_death_trackview|12h
""";
}
