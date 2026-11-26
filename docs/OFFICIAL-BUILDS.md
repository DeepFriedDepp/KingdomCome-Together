# Official builds and unofficial builds

Every build of Kingdom Come: Together says which it is:

- **Official build 0.65.5** (with its own version) — made by the project's maintainer.
- **Unofficial build — not made by Kingdom Come: Together** — everything else: a fork's build, a build somebody changed, and the
  maintainer's own development builds.

The words are shown in the launcher's status line and its About box, in the in-game mod menu, and in the first lines of a bug
report's briefing. An unofficial build still runs and still connects to anyone. Nothing is refused, nothing is switched off, and
nothing is sent anywhere: the check is offline and reads files only.

This is not the code signing of `docs/CODE-SIGNING.md` (the Windows signature on the files, which says who published them to
Windows). The official-build stamp is the project's own, needs no certificate, and is checked by the mod itself.

## How it works

1. The release path (`tools/Build-Installer.ps1`) writes `official-build.txt` beside the binaries: the version, the build id, and
   the SHA-256 of each of the build's own binaries — the DLL (`KCDMP.dll`), the agent, the relay, the launcher and the
   project's shared assemblies.
2. It signs that file (ECDSA P-256 over its bytes) with the maintainer's private key and writes `official-build.sig`.
3. The private key is **not in the repository**. It is a file on the maintainer's machine; its path is in the environment variable
   `KCT_OFFICIAL_KEY`. Without that variable the release path writes no signature and says so: the build is unofficial.
4. The public key is in the repository: `OfficialBuild.PublicKeyPem` in `dotnet/KcdMp.Protocol/OfficialBuild.cs`.
5. The launcher and the agent each check once at their start (`OfficialBuild.Check`): the signature is the project's, it is for
   this version and this build id, and every file it names is on disk with the same hash. Anything missing or different reads
   "Unofficial build", with the reason in the log (`MP-OFFICIAL` in the agent's log; the launcher's status line shows the reason
   as its tooltip). The agent tells the mod; the mod menu shows the line. The bug report's briefing reads the agent's line.

The switch `mp_w224_official off` turns the agent's check off. A build that is not checked reads **unofficial**: the line is
never hidden.

## What it does not do

- It does not stop anyone from building, changing or running the mod. The licence (GPL-3.0-only, with the terms in `NOTICE`)
  allows all of that.
- It does not cover the game's own files, the .NET runtime's files, or the mod's script pak in the game's mod folder (it lives
  outside the install folder the check reads). A changed script pak under official binaries is still a modified version: whoever
  changes it says so.
- The in-game line shows what the agent told the mod. Anyone can type anything into their own game's console; the launcher's line
  and the bug report's line are the check's own.

## For a fork

Forks are welcome. A fork's build reads "Unofficial build — not made by Kingdom Come: Together", and that is correct: it was not.
A fork that wants its own stamp makes its own key pair (`dotnet run --project dotnet/KcdMp.Official.Tool -- keygen <a file outside
the repository>`), puts **its own** public key into `OfficialBuild.PublicKeyPem`, and changes the texts to name the fork — not
this project. What a fork must not do (`AGENTS.md`, rule 6): change the check so a build without the maintainer's signature reads
"Official", remove or hide the "Unofficial build" text, or present its build as this project's.

## For the maintainer

- The key pair was generated once, on the maintainer's machine, outside the repository. Set `KCT_OFFICIAL_KEY` to the private
  key file's full path (a user environment variable) on the machine that makes releases; keep the file out of every repository and
  every backup that is shared.
- `tools/Build-Installer.ps1` prints `official : Official build <version>` or `official : UNOFFICIAL (...)` in its summary.
- To check any install or release folder by hand:
  `dotnet run --project dotnet/KcdMp.Official.Tool -- check <folder> <version> <build id>`.
- If the private key is ever lost or exposed: make a new pair, replace the public key in `OfficialBuild.cs`, and say in the release
  notes that builds before that release will read "Unofficial" to builds after it — each build only ever checks itself, so
  nothing else changes.
