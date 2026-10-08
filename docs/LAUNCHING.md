# How to launch

> **The normal way is `KingdomComeTogether-Setup-<version>.exe`** — download it, run it,
> done. It finds the game through Steam, deploys the mod, installs the
> launcher and pre-fills the game path. See the Install section of
> `README.md`. Everything below is the **fallback**: the manual flow, kept
> because it is what every test in `tools\` assumes and what to fall back to
> when the installer misbehaves or a Steam setup is unusual.

## The manual flow (fallback)

Doing by hand what the installer does for you: put `kdcmp/` into
`<ModdingTools>\Mods\kdcmp\`, unzip a release folder somewhere, and point the
launcher's `GamePath` at the Modding Tools `KingdomCome.exe`. Or skip the
launcher entirely and drive the pieces directly:

1. **Relay** — on one machine (either player's PC or a dedicated box):
   `KcdMpServer.exe` (defaults to port 7778)
2. **Each player** — launch KCD2 via **Modding Tools**, load a save. Confirm
   `[KCD2-MP] === MOD INIT ===` appears in `kcd.log`.
   *(Stale since WO-159 for the launcher flow: a game the launcher starts shows Start Game / Join Game on its
   main menu instead — see `docs/QUICKSTART.md` step 4 and `docs/WO-159-findings.md`. A game started by hand, as
   here, keeps the game's own menu.)*
3. **Each player** — inject the plugin into the running game. Since WO-157 the launcher does this itself
   (`dotnet\KcdMp.Setup\GameInjector.cs`) and no injector exe ships; for development, the
   `native\KCDMP_LauncherInjector` tool (built, never shipped):
   `KCDMP_LauncherInjector.exe --pid <pid> --dll <path>\KCDMP.dll`
4. **Each player** — `KcdMpClient.exe --host <relay ip>`

The agent finds the game, picks its transport, connects to the relay, and starts
syncing. Settings live in `kcdmp-client.json` next to the executable.

**Launch through Modding Tools, not the base game.** That is not a preference.
The debug REST API on `localhost:1403` exists only in that build, and it is the
entire channel between the game and `KcdMpClient.exe`. Retail is also monolithic:
`Framework.dll` and `CrySystem.dll` are separate modules only in the Modding
Tools build, and the plugin needs both — the IAT hook rewrites `WHGame.dll`'s
import of `Framework.dll`'s `C_ModulesManager::Update`, and the rttr reflection
ABI is exported from `CrySystem.dll`. Retail ships 6 DLLs beside the executable
and none of them are these; Modding Tools ships 45.

## KCDMP_launcher

The launcher now drives the real system. It was written as scaffolding for a
DLL-injection design before that design existed, and every assumption in it has
since been settled by the native-plugin work.

`LaunchGame` starts the Modding Tools build, waits until `WHGame.dll` is loaded
in the game process; the connect step (done by Start Game / Join Game on the game's main menu since 0.45.5; nobody clicks CONNECT) loads `KCDMP.dll` into it from the launcher's own process
(WO-157: the game it started, the shipped DLL by sha256, x64 -- the separate
`KCDMP_LauncherInjector.exe` was removed by antivirus programs on fresh installs), and then starts
`KcdMpClient.exe --host <ip> --port <port>`.

What changed and why:

| Was | Now |
|---|---|
| Launched whatever `GamePath` pointed at | Refuses anything without `Framework.dll` + `CrySystem.dll` beside the exe |
| `+map <MapName>`, guarded by a level-directory check | Dropped — KCD2 loads a save; there is no level to boot into |
| Injected 3 s after `Process.Start` | Waits for `WHGame.dll` to load, up to `InjectDelaySeconds` (default 20) |
| Never started the agent | Starts `KcdMpClient.exe`, without which nothing reaches the relay |
| Ignored the injector's exit code | Reports a failed injection instead of continuing |

New settings: `AgentPath`, `InjectDelaySeconds`, `ServerInfoPort`.

**Verified:** the launcher has driven every tester session since 0.45.0 (WO-154): the game start, the wait for
`WHGame.dll`, the injection and the agent, as the testers' logs show.

## The master server

The master server is the C# `dotnet/KcdMp.MasterServer/`: see `docs/MASTER-SERVER.md` and `docs/WO-35-findings.md`.
The Flask service this page once described was never run and is gone (the note below).

### WO-35: replaced by a C# master server

Everything this page once said about the master server described the Flask
service, which was never run on any machine that touched this project — the
"never verified" caveat throughout that section was the actual cause of the launcher's confusing "Master Server
not found" error for non-technical users. `kcd2_master_server/` is gone;
`dotnet/KcdMp.MasterServer/` replaces it, contributed by a community member
and adopted after a full contract comparison, safety review, and live
end-to-end test (relay → master → launcher, all three with their real
production code) — see `docs/WO-35-findings.md` and `docs/MASTER-SERVER.md`.

It is not a drop-in: the transport changed from HTTP POST/GET polling to one
WebSocket held open per relay, the listing JSON changed from a bare
snake_case array to a wrapped, versioned, camelCase object, and expiry changed
from a 5-minute `last_seen` staleness window to immediate delisting when the
relay's connection closes (live-verified at ~1 second). `MasterServerUrl`'s
default changed to `http://localhost:5100` (a base URL now, not an endpoint
path) and `MasterRegistrationService` was replaced by `MasterAnnounceService`.
