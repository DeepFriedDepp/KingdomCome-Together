// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Setup;

namespace KcdMp.Setup.Tests;

/// <summary>WO-159 Phase 4: the first-run pages, read from the active profile -- present, missing, stale, no profile; never written.</summary>
public class Wo159ConsentTests : IDisposable
{
    private readonly string _user = Path.Combine(Path.GetTempPath(), "kcdmp-wo159c-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_user, true); } catch { }
    }

    private void Profile(string active, string? attrs, string? settingsName = "ActiveProfile")
    {
        Directory.CreateDirectory(Path.Combine(_user, "profiles", active));
        if (settingsName is not null)
            File.WriteAllText(Path.Combine(_user, "profiles", "settings.xml"),
                $"<?xml version=\"1.0\" encoding=\"us-ascii\"?>\n<settings>\n\t<attr name=\"{settingsName}\" value=\"{active}\" />\n</settings>");
        if (attrs is not null)
            File.WriteAllText(Path.Combine(_user, "profiles", active, "attributes.xml"),
                $"<?xml version=\"1.0\" encoding=\"us-ascii\"?>\n<Attributes Version=\"31\">\n\t<Attr name=\"console_mode_chosen\" value=\"0\" />\n{attrs}</Attributes>");
    }

    private static string Eula(int v) => $"\t<Attr name=\"eula_confirmed_version\" value=\"{v}\" />\n";
    private const string Tel = "\t<Attr name=\"g_telemetry\" value=\"false\" />\n\t<Attr name=\"telemetry_confirmed\" value=\"1\" />\n";

    [Fact]
    public void Accepted_both_pages_on_the_active_profile()
    {
        Profile("default", Eula(2) + Tel);
        var r = ConsentFlags.Read(_user);
        Assert.Equal(ConsentFlags.State.Accepted, r.State);
        Assert.Contains("version 2", r.Detail);
        Assert.Equal(ConsentFlags.State.Accepted, ConsentFlags.Read(Fresh(Eula(3) + Tel)).State);   // a newer acceptance
    }

    private string Fresh(string attrs)
    {
        Dispose();
        Profile("default", attrs);
        return _user;
    }

    [Fact]
    public void Missing_pages_are_named()
    {
        Assert.Equal(ConsentFlags.State.Missing, ConsentFlags.Read(_user).State);   // never started: no profiles folder
        Profile("default", null);
        Assert.Contains("no attributes.xml", ConsentFlags.Read(_user).Detail);
        Assert.Contains("neither page", ConsentFlags.Read(Fresh("")).Detail);
        Assert.Contains("telemetry", ConsentFlags.Read(Fresh(Eula(2))).Detail);
        Assert.Contains("licence", ConsentFlags.Read(Fresh(Tel)).Detail);
        Assert.Equal(ConsentFlags.State.Missing, ConsentFlags.Read(Fresh(Eula(2) + "\t<Attr name=\"telemetry_confirmed\" value=\"0\" />\n")).State);
    }

    [Fact]
    public void An_older_licence_version_is_stale()
    {
        var r = ConsentFlags.Read(Fresh(Eula(1) + Tel));
        Assert.Equal(ConsentFlags.State.Stale, r.State);
        Assert.Contains("version 1", r.Detail);
    }

    [Fact]
    public void No_active_profile_is_said_and_never_guessed()
    {
        Assert.Equal(ConsentFlags.State.NoProfile, ConsentFlags.Read(null).State);
        Profile("default", Eula(2) + Tel, settingsName: "SomethingElse");
        Assert.Equal(ConsentFlags.State.NoProfile, ConsentFlags.Read(_user).State);
        Dispose();
        Profile("default", Eula(2) + Tel, settingsName: null);   // no settings.xml at all
        Assert.Equal(ConsentFlags.State.NoProfile, ConsentFlags.Read(_user).State);
        File.WriteAllText(Path.Combine(_user, "profiles", "settings.xml"), "<settings><attr name=\"ActiveProfile\" value=\"..\" /></settings>");
        Assert.Equal(ConsentFlags.State.NoProfile, ConsentFlags.Read(_user).State);
        File.WriteAllText(Path.Combine(_user, "profiles", "settings.xml"), "<settings><attr");
        Assert.Contains("unreadable", ConsentFlags.Read(_user).Detail);
    }

    [Fact]
    public void Reading_writes_nothing()
    {
        Profile("default", Eula(1));
        string attrs = Path.Combine(_user, "profiles", "default", "attributes.xml");
        var before = (File.ReadAllBytes(attrs), File.GetLastWriteTimeUtc(attrs));
        ConsentFlags.Read(_user);
        Assert.Equal(before.Item1, File.ReadAllBytes(attrs));
        Assert.Equal(before.Item2, File.GetLastWriteTimeUtc(attrs));
        Assert.Equal(2, Directory.GetFiles(_user, "*", SearchOption.AllDirectories).Length);
    }
}
