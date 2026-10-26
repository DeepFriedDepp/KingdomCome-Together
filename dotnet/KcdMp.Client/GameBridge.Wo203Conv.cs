// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.IO.Compression;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-203 I.3b (docs/WO-203-findings.md): the bodies act the starter's conversation out. The STARTER's agent reads each
// gesture of this player's own conversation from the engine's line ("DialogCommand-Anim: who=<ROLE>, anim=<ADLG_x>") and sends it as
// a ConvAnim (a joiner to the host, the host to every joiner; the host passes a joiner's on to the other joiners). The LISTENER's
// agent picks the fragment's clip out of the game's own gesture database (Data/Animations.pak, male_adlg.adb / female_adlg.adb; both
// picks go to the mod, which knows the body's gender) and the mod plays it on that body there: the starter's figure or the NPC.
// The pure rules are Wo203ConvRules (Wo203Conv.cs); the mod's half is kdcmp.lua's KCD2MP_W203Anim. Nothing here reads any input.
public partial class GameBridge
{
    private readonly ConcurrentDictionary<int, string[]> _w203OwnTalks = new();   // this player's conversations: cid -> the other souls
    private readonly ConcurrentDictionary<string, int> _w203GestureN = new(StringComparer.Ordinal);   // "starter:cid" -> gestures played
    private Dictionary<string, List<string>>? _w203Male, _w203Female;
    private int _w203DbTried;
    private long _w203AnimOut, _w203AnimUnplaced, _w203AnimIn, _w203AnimPlayed, _w203AnimNoClip, _w203AnimBad, _w203AnimFwd;

    private bool W203Partnered => (W137Host || W137Joiner) && Wo134Peers().Count > 0;

    partial void Wo203ConvOnEngineLine(string line)
    {
        // WO-204 III.1: a combat bark, a war cry, a greeting is never a conversation -- no announce, no offer, no gestures
        if (line.StartsWith("New dialogue '", StringComparison.Ordinal) && !Wo204ConversationAllowed(line)) return;
        Wo203CamOnEngineLine(line);   // WO-203B: the minimal session and the camera (GameBridge.Wo203Cam.cs)
        if (line.StartsWith("New dialogue '", StringComparison.Ordinal))
        {
            if (Wo203ConvRules.ParseOwnConversation(line) is { } c)
            {
                _w203OwnTalks[c.Cid] = c.Others;
                if (c.Others.Length > 1) Wo204LoadRoles(c.Cid, line, c.Others);   // WO-204 III.5: a trialog's roles from its own file
            }
            return;
        }
        int end = Wo203ConvRules.ParseEnd(line);
        if (end >= 0)
        {
            // WO-208 C.3: this player's own talk ended -- the mod notes when and how (its dialog-map watch names it if the map stays on)
            if (_w203OwnTalks.TryRemove(end, out _))
                _ = ExecLuaAsync($"if KCD2MP_W208OwnTalkEnd then KCD2MP_W208OwnTalkEnd({end}, \"{(line.Contains(" Dialog interrupted", StringComparison.Ordinal) ? "interrupted" : "ending")}\") end");
            return;
        }
        if (Wo203ConvRules.ParseAnim(line) is not { } a || !_w203OwnTalks.TryGetValue(a.Cid, out var others) || !W203Partnered) return;
        if ((Wo203ConvRules.Place(a.Who, others) ?? Wo204PlaceByRole(a.Cid, a.Who)) is not { } p) { Interlocked.Increment(ref _w203AnimUnplaced); return; }
        string text = Wo203ConvRules.AnimText(a.Cid, _myGhostId, p.Body, a.Fragment, p.Face);
        Interlocked.Increment(ref _w203AnimOut);
        _ = Task.Run(async () =>
        {
            if (W137Host) foreach (byte j in Wo134Peers()) await Wo203SendConvAsync(j, Protocol.ConvAnim, text);
            else await Wo203SendConvAsync(Protocol.JoinTargetHost, Protocol.ConvAnim, text);
        });
    }

    partial void Wo203ConvOnFrame(byte src, LootMsg m)
    {
        if (m.Kind != Protocol.ConvAnim) { Wo203CamOnFrame(src, m); return; }   // WO-203B: every other kind
        if (!Wo203ConvRules.TryParseAnim(m.Text, out int cid, out byte starter, out string body, out string frag, out string face)
            || (starter != src && W137Host))   // a joiner sends only its own; only the host passes others' on (unchanged)
        { Interlocked.Increment(ref _w203AnimBad); return; }
        Interlocked.Increment(ref _w203AnimIn);
        if (W137Host)
            foreach (byte j in Wo134Peers())
                if (j != src) { Interlocked.Increment(ref _w203AnimFwd); _ = Wo203SendConvAsync(j, Protocol.ConvAnim, m.Text); }
        if (starter == _myGhostId) return;
        W203LoadGestures();
        if (_w203Male is null || _w203Female is null) { Interlocked.Increment(ref _w203AnimNoClip); return; }
        int n = _w203GestureN.AddOrUpdate($"{starter}:{cid}", 0, (_, v) => v + 1);
        string? male = Wo203ConvRules.Pick(_w203Male, frag, n), female = Wo203ConvRules.Pick(_w203Female, frag, n);
        if (male is null && female is null) { Interlocked.Increment(ref _w203AnimNoClip); return; }
        string Ref(string b) => b == Wo203ConvRules.Starter ? $"#{starter}" : b;
        Interlocked.Increment(ref _w203AnimPlayed);
        _ = ExecLuaAsync($"if KCD2MP_W203Anim then KCD2MP_W203Anim(\"{starter}:{cid}\", \"{EscapeLua(Ref(body))}\", \"{EscapeLua(male ?? female!)}\", "
                         + $"\"{EscapeLua(Ref(face))}\", nil, nil, nil, \"{EscapeLua(female ?? male!)}\") end");
    }

    /// <summary>The game's own gesture database, read once per agent run from the install's Data/Animations.pak (next to kcd.log).</summary>
    private void W203LoadGestures()
    {
        if (_w203Male is not null || Interlocked.Exchange(ref _w203DbTried, 1) == 1) return;
        try
        {
            if (KcdLogLocator.Find() is not string log || Path.GetDirectoryName(log) is not string root) return;
            using var pak = ZipFile.OpenRead(Path.Combine(root, "Data", "Animations.pak"));
            Dictionary<string, List<string>> Read(string g)
            {
                var e = pak.GetEntry($"Animations/Mannequin/ADB/{g}_adlg.adb") ?? throw new FileNotFoundException($"{g}_adlg.adb");
                using var r = new StreamReader(e.Open());
                return Wo203ConvRules.ParseAdb(r.ReadToEnd());
            }
            var female = Read("female");
            _w203Male = Read("male");
            _w203Female = female;
            Console.WriteLine($"WO203-GESTURES database: male={_w203Male.Count} female={_w203Female.Count} fragments");
        }
        catch (Exception ex) { Console.WriteLine($"WO203-GESTURES database not read: {ex.Message} -- the partner's conversations show no gestures here"); }
    }

    partial void Wo203ConvOnDisconnect() { _w203OwnTalks.Clear(); _w203GestureN.Clear(); Wo203CamOnDisconnect(); }

    partial void Wo203ConvOnEvent(string name, string? arg) => Wo203CamOnEvent(name, arg);   // WO-203B: w203_join / w203_leave / w203_cfg

    partial void Wo203ConvOnConnect(CancellationToken ct) => Wo203CamOnConnect();   // WO-203B: the mod's switches said to this agent

    partial void Wo203ConvStats(ref string line)
    {
        line += $" | conv anim out={Interlocked.Read(ref _w203AnimOut)} unplaced={Interlocked.Read(ref _w203AnimUnplaced)} in={Interlocked.Read(ref _w203AnimIn)}"
              + $" played={Interlocked.Read(ref _w203AnimPlayed)} no_clip={Interlocked.Read(ref _w203AnimNoClip)} bad={Interlocked.Read(ref _w203AnimBad)}"
              + $" forwarded={Interlocked.Read(ref _w203AnimFwd)} own_talks={_w203OwnTalks.Count}";
        Wo203CamStats(ref line);   // WO-203B
    }
}
