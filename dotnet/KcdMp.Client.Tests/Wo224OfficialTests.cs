// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-224 C: a build says whether it is the project's own. The key here is made by the test; the project's private key is
/// never on a test machine.</summary>
public class Wo224OfficialTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kct-official-" + Guid.NewGuid().ToString("N"));
    private readonly string _priv, _pub;

    public Wo224OfficialTests()
    {
        Directory.CreateDirectory(_dir);
        (_priv, _pub) = OfficialBuild.NewKeyPair();
        foreach (string n in OfficialBuild.RequiredFiles.Concat(["KcdMpClient.exe", "KcdMp.Protocol.dll", "System.Text.Json.dll", "appsettings.json"]))
            File.WriteAllText(Path.Combine(_dir, n), "the bytes of " + n);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private void SignIt(string version = "0.65.5", string build = "abc1234-2611270000")
    {
        string m = OfficialBuild.BuildManifest(_dir, version, build);
        File.WriteAllText(Path.Combine(_dir, OfficialBuild.ManifestName), m);
        File.WriteAllText(Path.Combine(_dir, OfficialBuild.SignatureName), OfficialBuild.Sign(m, _priv) + "\n");
    }

    private OfficialBuild.Result Check(string version = "0.65.5", string build = "abc1234-2611270000") => OfficialBuild.Check(_dir, version, build, _pub);

    [Fact]
    public void ASignedBuildReadsOfficialWithItsVersion()
    {
        SignIt();
        var r = Check();
        Assert.True(r.Official, r.Reason);
        Assert.Equal("Official build 0.65.5", r.Text);
        Assert.Equal("Official build 0.65.5", r.TextAscii);
    }

    [Fact]
    public void TheSameTreeWithoutTheKeyReadsUnofficial()
    {
        var r = Check();   // nothing signed it
        Assert.False(r.Official);
        Assert.Equal("Unofficial build — not made by Kingdom Come: Together", r.Text);
        Assert.Equal("Unofficial build -- not made by Kingdom Come: Together", r.TextAscii);
        Assert.Contains("no signature", r.Reason);
    }

    [Theory]
    [InlineData("KCDMP.dll")]
    [InlineData("KcdMpClient.dll")]
    [InlineData("KcdMpServer.dll")]
    [InlineData("KCDMP_launcher.dll")]
    [InlineData("KcdMp.Protocol.dll")]
    [InlineData("KcdMpClient.exe")]
    public void OneFileSwappedReadsUnofficial(string name)
    {
        SignIt();
        File.WriteAllText(Path.Combine(_dir, name), "somebody else's " + name);
        var r = Check();
        Assert.False(r.Official);
        Assert.Contains(name, r.Reason);
        Assert.Contains("not the file that was signed", r.Reason);
    }

    [Fact]
    public void AMissingFileReadsUnofficial()
    {
        SignIt();
        File.Delete(Path.Combine(_dir, "KcdMpServer.dll"));
        Assert.Contains("KcdMpServer.dll is missing", Check().Reason);
    }

    [Fact]
    public void AnotherKeysSignatureReadsUnofficial()
    {
        SignIt();
        Assert.True(Check().Official);
        // the same folder checked against the project's real public key: this test's key is not the project's
        var r = OfficialBuild.Check(_dir, "0.65.5", "abc1234-2611270000");
        Assert.False(r.Official);
        Assert.Contains("not the project's", r.Reason);
    }

    [Fact]
    public void AChangedManifestReadsUnofficial()
    {
        SignIt();
        string mp = Path.Combine(_dir, OfficialBuild.ManifestName);
        File.WriteAllText(mp, File.ReadAllText(mp).Replace("version 0.65.5", "version 0.65.6"));
        Assert.False(Check("0.65.6").Official);
    }

    [Fact]
    public void ASignatureForAnotherBuildReadsUnofficial()
    {
        SignIt();
        Assert.False(Check(build: "fffffff-2611280000").Official);   // the binaries of another build under this signature
        Assert.False(Check(version: "0.65.6").Official);
        Assert.Contains("the signature is for 0.65.5", Check(version: "0.65.6").Reason);
    }

    [Fact]
    public void AManifestThatLeavesOutARequiredBinaryIsNotEnough()
    {
        // a manifest signed by the right key that does not name the relay: not a whole build
        string m = string.Join('\n', OfficialBuild.BuildManifest(_dir, "0.65.5", "abc1234-2611270000").Split('\n').Where(l => !l.EndsWith(" KcdMpServer.dll", StringComparison.Ordinal)));
        File.WriteAllText(Path.Combine(_dir, OfficialBuild.ManifestName), m);
        File.WriteAllText(Path.Combine(_dir, OfficialBuild.SignatureName), OfficialBuild.Sign(m, _priv));
        var r = Check();
        Assert.False(r.Official);
        Assert.Contains("does not name KcdMpServer.dll", r.Reason);
    }

    [Fact]
    public void AManifestNeverReachesOutsideItsFolder()
    {
        string m = OfficialBuild.BuildManifest(_dir, "0.65.5", "abc1234-2611270000") + "file " + new string('0', 64) + " ..\\elsewhere.dll\n";
        File.WriteAllText(Path.Combine(_dir, OfficialBuild.ManifestName), m);
        File.WriteAllText(Path.Combine(_dir, OfficialBuild.SignatureName), OfficialBuild.Sign(m, _priv));
        Assert.Contains("not in the form", Check().Reason);
    }

    [Fact]
    public void OnlyTheBuildsOwnBinariesAreListed()
    {
        Assert.True(OfficialBuild.IsOwnBinary("KCDMP.dll") && OfficialBuild.IsOwnBinary("KCDMP_launcher.exe") && OfficialBuild.IsOwnBinary("KcdMpSetup.exe") && OfficialBuild.IsOwnBinary("KcdMp.Protocol.dll"));
        Assert.False(OfficialBuild.IsOwnBinary("System.Text.Json.dll") || OfficialBuild.IsOwnBinary("appsettings.json") || OfficialBuild.IsOwnBinary("kdcmp.pak") || OfficialBuild.IsOwnBinary("KcdMpClient.pdb"));
        string m = OfficialBuild.BuildManifest(_dir, "0.65.5", "x");
        Assert.DoesNotContain("System.Text.Json.dll", m);
        Assert.Contains(" KcdMp.Protocol.dll\n", m);
        Assert.StartsWith("kct-official-build 1\nversion 0.65.5\nbuild x\nfile ", m);
    }

    [Fact]
    public void AnIncompleteFolderIsNotSigned()
    {
        File.Delete(Path.Combine(_dir, "KCDMP.dll"));
        Assert.Throws<FileNotFoundException>(() => OfficialBuild.BuildManifest(_dir, "0.65.5", "x"));
    }

    [Fact]
    public void TheProjectsPublicKeyIsAKeyAndTheCheckMakesNoNetworkCall()
    {
        // the embedded key parses (a check against it never throws)
        Assert.False(OfficialBuild.Check(_dir, "0.65.5", "x").Official);
        Assert.StartsWith("-----BEGIN PUBLIC KEY-----", OfficialBuild.PublicPemOf(_priv));
        // the check's source names nothing of the network: files and a signature only (the rule: no network calls, ever)
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "dotnet", "KcdMp.Protocol", "OfficialBuild.cs"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        string src = File.ReadAllText(Path.Combine(root!, "dotnet", "KcdMp.Protocol", "OfficialBuild.cs"));
        foreach (string banned in new[] { "System.Net", "HttpClient", "Socket", "Dns.", "WebRequest", "TcpClient", "UdpClient", "http://", "https://" })
            Assert.DoesNotContain(banned, src);
    }

    // ---- what the agent tells the mod's menu ----
    [Fact]
    public void TheModIsToldOfficialOnlyForACheckedSignedBuild()
    {
        var signed = new OfficialBuild.Result(true, "0.65.5", "abc1234-2611270000", "signed by the project's key; every file as signed");
        var (ok, version, why) = OfficialBuild.ConsoleWord(true, signed);
        Assert.True(ok);
        Assert.Equal("0.65.5", version);
        Assert.Equal("signed by the project's key  every file as signed", why);   // (the console's letters only: the ';' is a blank)
    }

    [Fact]
    public void TheSwitchOffIsNotCheckedAndReadsUnofficial()
    {
        var signed = new OfficialBuild.Result(true, "0.65.5", "abc1234-2611270000", "signed by the project's key; every file as signed");
        var (ok, _, why) = OfficialBuild.ConsoleWord(false, signed);
        Assert.False(ok);
        Assert.Contains("not checked", why);
    }

    [Fact]
    public void AnUnsignedBuildIsToldUnofficialWithItsReasonInConsoleLetters()
    {
        var no = new OfficialBuild.Result(false, "0.65.5", "dev", "the signature is for 0.65.5 build x\"y, this is 0.65.5 build dev");
        var (ok, version, why) = OfficialBuild.ConsoleWord(true, no);
        Assert.False(ok);
        Assert.Equal("0.65.5", version);
        Assert.DoesNotContain("\"", why);
        Assert.DoesNotContain("\\", why);
        // a version that is not a version is never put into the console's command
        Assert.False(OfficialBuild.ConsoleWord(true, new OfficialBuild.Result(true, "0.65.5\") os.exit(", "x", "y")).Official);
        Assert.Equal("", OfficialBuild.ConsoleWord(true, new OfficialBuild.Result(true, "0.65.5\") os.exit(", "x", "y")).Version);
    }

    [Fact]
    public void TheSwitchIsListedWithTheOthers()
    {
        Assert.Contains(W224Switch.Official, W224Switch.Names);
        Assert.DoesNotContain(W224Switch.Official, W224Switch.NativeNames);   // the agent's own: no native bit
    }
}
