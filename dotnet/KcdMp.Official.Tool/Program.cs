// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

// WO-224 C: the release path's signer (never shipped; docs/OFFICIAL-BUILDS.md). No network call: files only.
//   kct-official sign   <release folder> <version> <build id>   -- writes official-build.txt and official-build.sig into the folder,
//                                                                  with the private key whose path is in KCT_OFFICIAL_KEY.
//                                                                  Exit 3: no key (the build stays unofficial; nothing is written).
//   kct-official check  <release folder> <version> <build id>   -- the launcher's and the agent's own check; prints the words.
//                                                                  Exit 0 official, 1 unofficial.
//   kct-official keygen <private key file>                       -- a new key pair: the private half into the file (it must not
//                                                                  exist, and must not be inside a git work tree), the public
//                                                                  half printed. For the maintainer once, or for a fork's own key.
//   kct-official public                                          -- the public key this build checks against.
static int Usage()
{
    Console.Error.WriteLine("usage: kct-official sign|check <release folder> <version> <build id>  |  keygen <private key file>  |  public");
    return 2;
}

static bool InsideGitTree(string path)
{
    for (var d = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!); d is not null; d = d.Parent)
        if (Directory.Exists(Path.Combine(d.FullName, ".git")) || File.Exists(Path.Combine(d.FullName, ".git"))) return true;
    return false;
}

if (args.Length == 0) return Usage();
switch (args[0])
{
    case "public":
        Console.WriteLine(OfficialBuild.PublicKeyPem);
        return 0;
    case "keygen":
    {
        if (args.Length != 2) return Usage();
        string file = Path.GetFullPath(args[1]);
        if (File.Exists(file)) { Console.Error.WriteLine("that file exists already: a key is never overwritten"); return 2; }
        if (InsideGitTree(file)) { Console.Error.WriteLine("that path is inside a git work tree: a private key never lives in a repository"); return 2; }
        var (priv, pub) = OfficialBuild.NewKeyPair();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, priv + "\n");
        Console.WriteLine($"the private key is in the file you named. Set {OfficialBuild.PrivateKeyEnvVar} to its path on the build machine; keep it out of every repository and backup you share.");
        Console.WriteLine("the public key (a fork puts its own into OfficialBuild.PublicKeyPem and says it is a fork):");
        Console.WriteLine(pub);
        return 0;
    }
    case "sign":
    {
        if (args.Length != 4) return Usage();
        string dir = Path.GetFullPath(args[1]);
        string? keyPath = Environment.GetEnvironmentVariable(OfficialBuild.PrivateKeyEnvVar);
        if (string.IsNullOrWhiteSpace(keyPath) || !File.Exists(keyPath))
        {
            // an earlier signature in a reused folder must not ride along with new binaries
            foreach (string n in new[] { OfficialBuild.ManifestName, OfficialBuild.SignatureName })
                if (File.Exists(Path.Combine(dir, n))) File.Delete(Path.Combine(dir, n));
            Console.WriteLine($"no private key ({OfficialBuild.PrivateKeyEnvVar} is {(string.IsNullOrWhiteSpace(keyPath) ? "not set" : "set, the file is not there")}): this build is not signed and reads \"{OfficialBuild.UnofficialText}\"");
            return 3;
        }
        if (InsideGitTree(keyPath)) { Console.Error.WriteLine("the private key's file is inside a git work tree: move it out of the repository first. Nothing signed."); return 2; }
        string manifest = OfficialBuild.BuildManifest(dir, args[2], args[3]);
        string sig = OfficialBuild.Sign(manifest, File.ReadAllText(keyPath));
        File.WriteAllText(Path.Combine(dir, OfficialBuild.ManifestName), manifest);
        File.WriteAllText(Path.Combine(dir, OfficialBuild.SignatureName), sig + "\n");
        var r = OfficialBuild.Check(dir, args[2], args[3]);
        Console.WriteLine($"signed {manifest.Split('\n').Count(l => l.StartsWith("file ", StringComparison.Ordinal))} files for {args[2]} build {args[3]} -> {r.Text} ({r.Reason})");
        if (!r.Official) { Console.Error.WriteLine("the signature just written does not verify against the public key in this tree: the private key is not this project's key"); return 1; }
        return 0;
    }
    case "check":
    {
        if (args.Length != 4) return Usage();
        var r = OfficialBuild.Check(Path.GetFullPath(args[1]), args[2], args[3]);
        Console.WriteLine($"{r.Text} ({r.Reason})");
        return r.Official ? 0 : 1;
    }
    default:
        return Usage();
}
