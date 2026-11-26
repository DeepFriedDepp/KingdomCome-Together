// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Security.Cryptography;
using System.Text;

namespace KcdMp.Wire;

/// <summary>
/// WO-224 C: is this build the project's own? (docs/OFFICIAL-BUILDS.md)
///
/// The release path writes a manifest beside the binaries -- the version, the build id and the SHA-256 of each of the build's own
/// binaries (the DLL, the agent, the relay, the launcher and the project's shared assemblies) -- and signs it (ECDSA P-256 over the
/// manifest's bytes) with the maintainer's private key, which is not in the repository: its path is in the environment variable
/// <see cref="PrivateKeyEnvVar"/> on the maintainer's machine. The public key is below. A build made without that key -- any fork's,
/// and the maintainer's own development builds -- has no valid signature and reads "Unofficial build". It still runs and still
/// connects to anyone: nothing is refused.
///
/// The check is offline and reads files only: the manifest, the signature, the files the manifest names. It makes no network call
/// and writes nothing. Do not change it so that a build without the maintainer's signature reads "Official", and do not replace the
/// key to present another build as the project's (AGENTS.md, rule 6). A fork says what it is.
/// </summary>
public static class OfficialBuild
{
    public const string ManifestName = "official-build.txt";
    public const string SignatureName = "official-build.sig";
    /// <summary>The maintainer's machine: the path of the private key file (PEM, PKCS#8). Never in the repository.</summary>
    public const string PrivateKeyEnvVar = "KCT_OFFICIAL_KEY";
    public const string ManifestHead = "kct-official-build 1";

    public const string OfficialPrefix = "Official build ";
    public const string UnofficialText = "Unofficial build — not made by Kingdom Come: Together";
    /// <summary>The same words for a surface that draws ASCII only (the game's debug text).</summary>
    public const string UnofficialTextAscii = "Unofficial build -- not made by Kingdom Come: Together";

    /// <summary>The project's public key (ECDSA P-256, SubjectPublicKeyInfo). Its private half signs official builds.</summary>
    public const string PublicKeyPem =
        "-----BEGIN PUBLIC KEY-----\n" +
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEvtc77hVW+i2oE9KFoAd5Xls3VOe3\n" +
        "XJjo/w4t7oLNTFvAEEM+Gn5/qAWEkp5rXP2iYEsD+sBXVDiBJ0nCyDjPtw==\n" +
        "-----END PUBLIC KEY-----";

    /// <summary>The binaries a manifest must name: the DLL, the agent, the relay, the launcher.</summary>
    public static readonly string[] RequiredFiles = ["KCDMP.dll", "KcdMpClient.dll", "KcdMpServer.dll", "KCDMP_launcher.dll"];

    public sealed record Result(bool Official, string Version, string BuildId, string Reason)
    {
        public string Text => Official ? OfficialPrefix + Version : UnofficialText;
        public string TextAscii => Official ? OfficialPrefix + Version : UnofficialTextAscii;
    }

    /// <summary>Is this file of a release folder one of the build's own binaries (listed in the manifest)? The .NET runtime's and
    /// third parties' files are not ours to vouch for.</summary>
    public static bool IsOwnBinary(string fileName)
    {
        string ext = Path.GetExtension(fileName);
        if (!ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        string n = Path.GetFileNameWithoutExtension(fileName);
        return n.Equals("KCDMP", StringComparison.OrdinalIgnoreCase) || n.StartsWith("KCDMP_", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("KcdMp", StringComparison.OrdinalIgnoreCase);
    }

    public static string Sha256Hex(string path)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
    }

    /// <summary>The manifest of a release folder: one line each, '\n' ends, the files in ordinal order of their names.</summary>
    public static string BuildManifest(string dir, string version, string buildId)
    {
        if (version.AsSpan().IndexOfAny(" \r\n") >= 0 || buildId.AsSpan().IndexOfAny(" \r\n") >= 0 || version.Length == 0 || buildId.Length == 0)
            throw new ArgumentException("a version and a build id without blanks are needed");
        var names = Directory.GetFiles(dir).Select(Path.GetFileName).Where(n => n is not null && IsOwnBinary(n)).Select(n => n!).OrderBy(n => n, StringComparer.Ordinal).ToList();
        foreach (string r in RequiredFiles)
            if (!names.Contains(r, StringComparer.OrdinalIgnoreCase)) throw new FileNotFoundException($"the release folder has no {r}: not a complete build", Path.Combine(dir, r));
        var sb = new StringBuilder();
        sb.Append(ManifestHead).Append('\n').Append("version ").Append(version).Append('\n').Append("build ").Append(buildId).Append('\n');
        foreach (string n in names) sb.Append("file ").Append(Sha256Hex(Path.Combine(dir, n))).Append(' ').Append(n).Append('\n');
        return sb.ToString();
    }

    /// <summary>The signature of a manifest's bytes (base64), by a private key in PEM.</summary>
    public static string Sign(string manifest, string privateKeyPem)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        return Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(manifest), HashAlgorithmName.SHA256));
    }

    /// <summary>The public half of a private key (PEM), for a fork's own key or a test's.</summary>
    public static string PublicPemOf(string privateKeyPem)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        return key.ExportSubjectPublicKeyInfoPem();
    }

    public static (string PrivatePem, string PublicPem) NewKeyPair()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportPkcs8PrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
    }

    /// <summary>
    /// What the agent tells the mod's menu through the game's console: official only when the check is on and passed (the switch
    /// off = not checked, and a build that is not checked reads unofficial); the version only if it is a version; the reason in
    /// letters the console's command takes (letters, digits, blanks and a few marks -- no quote, no backslash).
    /// </summary>
    public static (bool Official, string Version, string Why) ConsoleWord(bool switchOn, Result r)
    {
        string why = switchOn ? r.Reason : "mp_w224_official is off: the build is not checked";
        var sb = new StringBuilder(why.Length);
        foreach (char c in why) sb.Append(char.IsAsciiLetterOrDigit(c) || c is ' ' or '.' or ',' or ':' or '-' or '_' or '(' or ')' or '\'' ? c : ' ');
        string version = r.Version.Length > 0 && r.Version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-') ? r.Version : "";
        return (switchOn && r.Official && version.Length > 0, version, sb.ToString());
    }

    /// <summary>
    /// The check: is the build in <paramref name="dir"/> signed by the project's key, for this version and build id, with every
    /// file it names unchanged? Offline; reads only. Anything missing, unreadable or different is "Unofficial", with the reason in
    /// plain words (never an exception).
    /// </summary>
    public static Result Check(string dir, string ownVersion, string ownBuildId, string? publicKeyPem = null)
    {
        Result No(string why) => new(false, ownVersion, ownBuildId, why);
        try
        {
            string mp = Path.Combine(dir, ManifestName), sp = Path.Combine(dir, SignatureName);
            if (!File.Exists(mp) || !File.Exists(sp)) return No("no signature beside the binaries: a build made without the project's key");
            byte[] manifest = File.ReadAllBytes(mp);
            if (manifest.Length == 0 || manifest.Length > 64 * 1024) return No("the manifest is not a manifest");
            byte[] sig;
            try { sig = Convert.FromBase64String(File.ReadAllText(sp).Trim()); }
            catch (FormatException) { return No("the signature file is not a signature"); }
            using (var key = ECDsa.Create())
            {
                key.ImportFromPem(publicKeyPem ?? PublicKeyPem);
                if (!key.VerifyData(manifest, sig, HashAlgorithmName.SHA256)) return No("the signature is not the project's (the manifest was changed, or another key signed it)");
            }
            string[] lines = Encoding.UTF8.GetString(manifest).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 3 || lines[0] != ManifestHead || !lines[1].StartsWith("version ", StringComparison.Ordinal) || !lines[2].StartsWith("build ", StringComparison.Ordinal))
                return No("the manifest is not in the form this build reads");
            string version = lines[1]["version ".Length..], build = lines[2]["build ".Length..];
            if (version != ownVersion || build != ownBuildId) return No($"the signature is for {version} build {build}, this is {ownVersion} build {ownBuildId}");
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string l in lines.Skip(3))
            {
                string[] p = l.Split(' ', 3);
                if (p.Length != 3 || p[0] != "file" || p[1].Length != 64 || p[2].Length == 0 || p[2].IndexOfAny(['/', '\\', ':']) >= 0 || p[2] == "." || p[2] == "..")
                    return No("the manifest is not in the form this build reads");
                string path = Path.Combine(dir, p[2]);
                if (!File.Exists(path)) return No($"{p[2]} is missing");
                if (!Sha256Hex(path).Equals(p[1], StringComparison.OrdinalIgnoreCase)) return No($"{p[2]} is not the file that was signed");
                listed.Add(p[2]);
            }
            foreach (string r in RequiredFiles) if (!listed.Contains(r)) return No($"the manifest does not name {r}");
            return new Result(true, version, build, "signed by the project's key; every file as signed");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            return No($"the check could not be made ({ex.GetType().Name})");
        }
    }
}
