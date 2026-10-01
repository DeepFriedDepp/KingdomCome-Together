<p align="center">
  <img src="docs/branding/KCT_txt-900.png" alt="Kingdom Come: Together" width="600">
</p>

<h1 align="center">Kingdom Come: Together</h1>
<p align="center"><em>An <strong>unofficial</strong> co-op mod for Kingdom Come: Deliverance II.</em><br>
<strong>Unofficial. Not affiliated with or endorsed by Warhorse Studios.</strong></p>

<p align="center">
  <a href="docs/releases/RELEASE-NOTES-0.42.5.md"><img alt="main" src="https://img.shields.io/badge/main-0.42.5-b8860b?style=flat-square"></a>
  <a href="https://github.com/DeepFriedDepp/KingdomCome-Together/releases/latest"><img alt="latest release" src="https://img.shields.io/github/v/release/DeepFriedDepp/KingdomCome-Together?label=latest%20release&color=8a3324&style=flat-square"></a>
  <a href="LICENSE"><img alt="License: GPLv3" src="https://img.shields.io/badge/license-GPLv3-2c3e50?style=flat-square"></a>
  <a href="docs/LAUNCHING.md"><img alt="Platform" src="https://img.shields.io/badge/platform-Windows-555555?style=flat-square"></a>
  <a href="https://dsc.gg/kcd2-together"><img alt="Discord" src="https://img.shields.io/badge/Discord-Join-5865F2?logo=discord&logoColor=white&style=flat-square"></a>
</p>

Two or more people play the same open world together at once: you see each
other as ghost NPCs (position, animation, nameplates, and each other's
actual equipped armor *and* weapons — not one fixed costume), hear each
other over proximity voice chat, can land shared damage on each other and on
the world's NPCs, and can play a full relay-authoritative game of Farkle dice
against each other from inside the game itself.

> **Not affiliated with, endorsed by, or supported by Warhorse Studios.**
> Kingdom Come: Deliverance is a trademark of Warhorse Studios; this is a
> non-commercial fan project.

> **Two version numbers**. `main` (this
> repo's source) is ahead of the last published installer; the feature list
> below describes `main`. Installing from the
> [releases page](https://github.com/DeepFriedDepp/KingdomCome-Together/releases)
> gets you that published build's feature set, not everything described
> here — [Building from source](#building-from-source) gets you current
> `main`.

<!-- screenshot/gif here -->

## Contents

- [Features](#features)
- [How to play with a friend](#how-to-play-with-a-friend)
- [Install](#install)
- [How to play dice](#how-to-play-dice)
- [Architecture](#architecture)
- [Repository layout](#repository-layout)
- [Building from source](#building-from-source)
- [Testing](#testing)
- [What's left, and reporting bugs](#whats-left-undone-what-still-needs-a-human-and-reporting-bugs)
- [License and provenance](#license-and-provenance)

## Features

The partner joins the host's world from the main menu with their own
character, and both play in that one world: the host's people, quests, clock
and weather. Each feature below says how far it is proven:

- ✅ **Proven with two players** — seen working on a real evening of two people
  on two computers.
- 🧪 **Works with a scripted partner** — tested in the real game on one
  computer, against a scripted second player; not yet with two people.
- ⚠️ **Not yet proven** — built, but not seen working with a second player yet.

### Playing together

| Feature | What you get | Evidence |
|---|---|---|
| Joining | The partner waits at the main menu, clicks CONNECT and lands beside the host, with their own character, money and gear | ✅ |
| Seeing each other | Position, walking, running, riding, nameplates; each player has their own face | ✅ |
| Clothes and weapons | The other player's figure wears what they wear and holds what they hold | ✅ |
| Crouching, sneaking, torches | The figure crouches and sneaks with its player and holds a light only when its player does | 🧪 |
| Riding | The rider sits on a horse on both screens | ✅ |
| Nobody's menu stops the other | The host's world keeps running while either player is in a menu, the map or the inventory | ✅ |
| Voice chat | Speech by distance | ⚠️ starts every session; nobody has confirmed hearing the other yet |

### Fighting

| Feature | What you get | Evidence |
|---|---|---|
| Fighting the host's enemies together | Both players' hits count; an enemy dies on both screens | ✅ |
| Enemies hurt the partner | An enemy's blow on the partner's figure hurts the partner | ✅ |
| The partner fights on their own | Bandits and wild animals near the partner fight the partner even when the host is not fighting them: lock on, block, hit; friendly people never join in | 🧪 new in 0.42.5 |
| Blocks and tiring blows | A blow that only tires an enemy counts too | 🧪 new in 0.42.5 |
| Animals | Wolves and dogs bite both players; their bites hurt | ✅ |
| Knockouts and takedowns | A knocked-out NPC is down on both screens; takedowns go through the host | 🧪 |
| Dying | A grave with your things, and you wake up nearby; nobody's world reloads | ✅ |

### The world

| Feature | What you get | Evidence |
|---|---|---|
| The host's people | Where the host's villagers walk, sit, work and sleep, on the partner's screen too | ✅ |
| One clock | The partner's time follows the host's | ✅ |
| Weather | The same weather for both | ✅ |
| Sleeping together | One player lies down, the other is asked, and both sleep | ✅ |
| Crime and guards | The host's guards judge the partner's crimes; hitting a bandit is no crime | ✅ |
| Looting | Bodies are the host's (first come, first served); loose items exist once; chests are per player | ✅ |
| Dropping items for each other | What one drops, the other can pick up; the first pickup wins | ✅ |
| Fast travel | Only the host fast travels; the partner is told why | ✅ |
| The leash | A partner more than 650 m from the host is brought back after a countdown | 🧪 fixed in 0.42.5 (in the field it never brought anyone back) |

### Quests and conversations

| Feature | What you get | Evidence |
|---|---|---|
| Shared quests | The partner's quest steps reach the host's world, the host's reach the partner's | ✅ |
| Talking to the host's people | They wait for the partner while the partner talks to them | ✅ |
| The host's quests are safe | A partner's step that would fail a quest or count someone as dead happens only if the host's world agrees | 🧪 new in 0.42.5 |
| Dice with villagers | The dice game after a conversation starts on the partner's screen | 🧪 |

### Dice between players, and the tools

| Feature | What you get | Evidence |
|---|---|---|
| Dice against each other | A full game of Farkle in the game, with wagers | 🧪 against a scripted opponent |
| Launcher | Host or join, version check, Report a bug (collects the logs) | ✅ |
| Installer | One Setup for everything; checks its own files | ✅ |
| Server browser | Find a host's session in the launcher | 🧪 |

### Not built yet, and known gaps

- **Carrying** a body or an object shows only on the carrier's screen.
- **Animals' positions:** where the host's animals are (a wolf pack, a deer)
  can differ on the partner's screen until they come close, and an animal
  from a random encounter on the host's side may be missing there.
- **Cutscenes** play for each player separately; on the partner's screen one
  can stay black until the host's next step.
- **Escorts** (the sheep in "Find Mutt!") follow only the player who leads
  them, on that player's screen.
- **Whistling** is not heard by the other player.
- The journal's **marker letters** can differ between the two players.
- At the **grindstone** the blade is not in the other figure's hands.
- **Emotes** and **duels** are not built.

Details per feature, with the evidence: the findings pages in `docs/`; the
earlier detailed status table is kept in
[`docs/FEATURE-HISTORY.md`](docs/FEATURE-HISTORY.md).

## How to play with a friend

1. Both of you install the mod (below) and the launcher.
2. One of you clicks **HOST GAME** in the launcher — it starts a relay and
   shows you the address to share.
3. The other clicks **ADD SERVER**, enters that address, and clicks **JOIN**.
4. Both of you click through to load into the game world; the launcher
   handles injecting the plugin and starting your agent once you confirm
   you're actually in-game.
5. Once both loaded, ALT+TAB into the launcher and choose the "Connect" option

Same Wi-Fi/LAN: that's it. Different houses: see
**[docs/NETWORKING.md](docs/NETWORKING.md)** for the two ways to connect
over the internet (a VPN overlay like Tailscale — recommended — or port
forwarding), including exactly what address and port to share.

## Install

1. Download **`KingdomComeTogether-Setup-<version>.exe`** from the
   [releases page](https://github.com/DeepFriedDepp/KingdomCome-Together/releases).
2. Run it.
3. That's it — use Host or Join as above.

> **One-time step done by Steam/Warhorse's own tools, not ours — do this
> before your first launch, regardless of the order you install things in.**
> The KCD2 Modding Tools app does not ship with its own copy of the game's
> data (animations, characters, tables, scripts, cinematics, and more) — only
> a `Developer.pak`. The first time you install it, launch it **once through
> Steam itself** (its own Play button, or the shortcut Steam creates for it)
> and let **Workspace Setup**
> (`Tools\ModdingWorkspaceSetup\WorkspaceSetup.exe`) run — it copies the
> missing data from your base **Kingdom Come: Deliverance II** install into
> the Modding Tools folder. You need to own and have the base game installed
> too, since that's where the copy comes from.
>
> Skip this and the game will start, then immediately crash:
> *"Database system error — 114 tables are not loaded. See log for details.
> Ensure you have latest tables."* This is **not** this mod and **not** our
> installer — reproduced with the mod entirely removed, same crash. Our
> `Setup.exe` currently has no way to detect it; see "Not done," below.

The installer finds your game through Steam, deploys the mod into it,
installs the launcher, writes the game path into the launcher's settings so
there is nothing to configure, and puts a shortcut on your desktop. If the
free **KCD2 Modding Tools** are not installed it will say so, offer a button
that starts that download in Steam, and refuse to continue until they are
there — the retail game genuinely cannot run this mod, it lacks the debug
API and the module layout the plugin needs.

You need Kingdom Come: Deliverance II on Steam, plus that Modding Tools
entry (free, a separate item in your Steam library). Nothing else: the
launcher, agent and relay carry their own .NET runtime, and the installer
fetches Microsoft's WebView2 runtime if your Windows doesn't already have it.

Installing by hand still works and is documented in
[docs/LAUNCHING.md](docs/LAUNCHING.md) — use it if the installer misbehaves
or your Steam setup is unusual.

### Already installed? Update in 2 steps — no reinstall needed

Everyone you play with needs the same version, not just the host — an old and
a new build won't connect to each other.

Download **`KCDMP-DirectInstall-<version>.zip`** from the
[releases page](https://github.com/DeepFriedDepp/KingdomCome-Together/releases)
and unzip it. It contains two folders, `App` and `Mod`:

1. Copy the **`App`** folder's contents into your existing install folder
   (`%LocalAppData%\KCDMP` — paste that into Explorer's address bar),
   overwriting when asked. Your `settings.json` is not in the zip, so the game
   path you already have is left alone.
2. Copy the **`Mod`** folder's contents into your game's mod folder
   (`<your KCD2 Modding Tools folder>\Mods\kdcmp`), overwriting when asked —
   that's `mod.manifest` and `Data\kdcmp.pak`, the same two files Setup.exe
   deploys there and the only two that belong there.

Close the launcher first. That's it — no need to run Setup.exe again, and
nothing else on your PC is touched. Prefer a full reinstall? Running
`KingdomComeTogether-Setup-<version>.exe` over the top does that and keeps your settings
too.

<details>
<summary>Building the installer yourself</summary>

```
powershell -ExecutionPolicy Bypass -File tools\Build-Installer.ps1
powershell -ExecutionPolicy Bypass -File tools\Build-DirectInstall.ps1 -SkipPublish
```

The first publishes everything self-contained and compiles
`release\KingdomComeTogether-Setup-<version>.exe`; the second zips that same published
output into `release\KCDMP-DirectInstall-<version>.zip` (`App\` + `Mod\`, as
above), reusing the publish the first one just did. Building the installer
needs [Inno Setup 6](https://jrsoftware.org/isinfo.php)
(`winget install --id JRSoftware.InnoSetup`); the zip needs nothing extra.

Both read the version from the `VERSION` file at the repo root and from
nowhere else, so they cannot disagree about it. That number is chosen by hand,
never bumped automatically — see [docs/VERSIONING.md](docs/VERSIONING.md).

</details>

## How to play dice

Farkle, played against another real player. This is **not** the vanilla dice
minigame — it is a separate relay-authoritative engine with its own board
drawn over the game, so the rules are settled by the relay and neither player
can desync the other. The vanilla NPC minigame is untouched and still works
normally; nothing here interferes with it.

**Before you start:** both of you must be connected (green in the launcher)
and standing near each other in the same part of the world. Dice needs your
two characters close enough for "nearest player" to mean each other.

### 1. Open the game console

Every step that has no keybind yet is a console command. Open the developer
console with **`~`** (the key left of `1`; the Modding Tools build has the
console enabled — retail does not, which is one more reason the mod requires
that build). Type the command, press Enter, press `~` again to close.

### 2. Challenge someone

Walk up to a dice table, sit down if you like, and run:

```
mp_dice
```

That invites the nearest player. If you get *"Sit at a table first"*, the
table gate is on and there is no dice table in range — either move to a real
table, or turn the gate off with `mp_dice_gate off` (it ships **off** by
default, so you should not normally see this).

`mp_dice_table` reports the nearest table it can see, and `mp_dice_seat`
reports the seat under you — both are for working out why a table is not
being recognised.

Want groschen on the line? Run `mp_dice_wager <amount>` before `mp_dice` — it
stakes that amount on the *next* invite you send (0, the default, plays for
score only). The other player sees the stake before accepting; the winner's
inventory is credited and the loser's debited automatically when the match
ends.

### 3. Accept the challenge

The other player sees the invite prompt and runs:

```
mp_accept
```

or `mp_decline` to refuse. The board appears for both of you once accepted.

### 4. Play

Once the board is up, dice is played on **real keys** — no console needed:

| Key | Does |
|---|---|
| `F2` `F4` `F5` `F6` `F7` `F8` | Mark die 1–6 (the numbers under the dice on the board) |
| `F9` | Cast — rolls, or sets aside the dice you marked, depending on the phase |
| `U` | Clear every mark without rolling |
| `F11` *(hold)* | Bank your points and end your turn |
| `F12` *(hold)* | Yield the match |

Bank and yield are **hold-to-confirm**, deliberately — they are irreversible
and a stray tap should not end your turn or the match.

A turn goes: press `F9` to roll → mark the scoring dice you want to keep →
`F9` again to set them aside → roll the rest, or `F11` to bank what you have.
Roll nothing scoring and you **bust**: the board shows what you rolled and
says so, and your turn ends with nothing banked. First to the target score
wins.

Every key above has a console equivalent if a key ever fails you:
`mp_dice_mark 1`…`6`, `mp_dice_cast`, `mp_dice_unmark_all`, `mp_dice_bank`,
`mp_dice_yield`.

### If the board misbehaves

| Command | Use when |
|---|---|
| `mp_dice_redraw` | The board is stale — forces it to re-push |
| `mp_dice_flush` | The board is stuck or flickering — clears every queued panel |
| `mp_dice_close` | You want it gone |

<details>
<summary>Known dice rough edges</summary>

- **Inviting and accepting have no keybind.** `mp_dice`, `mp_accept` and
  `mp_decline` are the reliable path. Key bindings for these exist in the code
  but are built on **guessed** action names (`dialog_answer3/4`,
  `dialog_answer1/2`) that have never been confirmed to fire — treat them as
  not working.
- **A full two-human match has never been played.** Everything above was
  verified against a scripted opponent (`tools\Bot-DiceOpponent.ps1`) on one
  machine.

</details>

## Architecture

```
PC1: [KCD2 + Mod] ←localhost→ [KcdMpClient.exe] ──TCP──┐
                                                          ├── [KcdMpServer]
PC2: [KCD2 + Mod] ←localhost→ [KcdMpClient.exe] ──TCP──┘
```

- **`KcdMpServer`** — the relay. Whoever hosts runs this; everyone else
  connects to its address. Binds all interfaces by default, so LAN and
  VPN-overlay play need no extra config — see `docs/NETWORKING.md`.
- **`KcdMpClient.exe`** — the per-player agent. Reads your local game state
  and pushes it to the relay; receives everyone else's state and drives your
  local ghosts/voice/combat.
- **`KCDMP.dll`** — the native plugin, injected into the game process. Talks
  to the agent over a local named pipe; reads and writes game state through
  the engine's own RTTR reflection layer (exported by `CrySystem.dll`), not
  offsets or signature scans.
- **`KCDMP_launcher`** — the desktop app you actually run: server
  browser/host/join UI, launches the game, drives injection, starts the
  agent.

Each agent only ever talks to its own machine's game — there is no
cross-machine game-API traffic, only the relay TCP connection.

## Repository layout

| Path | What it is |
|---|---|
| `kdcmp/` | The game mod — Lua and the built `kdcmp.pak` |
| `dotnet/KcdMp.Client/` | `KcdMpClient.exe`, the per-PC agent |
| `dotnet/KcdMp.Server/` | `KcdMpServer.exe`, the relay |
| `dotnet/KcdMp.Protocol/` | Shared wire protocol |
| `dotnet/KcdMp.Farkle/` | The dice engine (pure state machine, no external deps) |
| `native/KCDMP/` | The injected plugin (C++) |
| `native/KCDMP_LauncherInjector/` | The injector executable |
| `KCDMP_launcher/` | The desktop launcher (Photino/Blazor) |
| `dotnet/KcdMp.MasterServer/` | `KcdMpMasterServer.exe`, the server-discovery backend — see `docs/MASTER-SERVER.md` |
| `docs/` | Design notes and session handoffs — start with `docs/PROJECT-STATE.md` |

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download) and, for
the native plugin, VS Build Tools with the C++ workload (CMake + Ninja,
located automatically via `vswhere`).

```powershell
dotnet build KCD2-MP.sln
powershell -File native\Build-Native.ps1
```

To assemble a self-contained release folder (no .NET runtime required on
the target machine — the native plugin already links its C++ runtime
statically, so there's nothing extra to bundle there):

```powershell
powershell -File tools\Publish-Release.ps1
```

Run things directly during development:

```powershell
dotnet run --project dotnet\KcdMp.Server
dotnet run --project dotnet\KcdMp.Client -- --host localhost --name PC1
dotnet run --project KCDMP_launcher
dotnet run --project dotnet\KcdMp.MasterServer
```

The master server (`dotnet/KcdMp.MasterServer/`) is optional — a relay and
launcher with each other's address connect directly and never touch it. See
`docs/MASTER-SERVER.md` for how to point a relay and the launcher at one.

## Testing

```powershell
dotnet run --project dotnet\KcdMp.Server -- --port 7778   # in one window
powershell -File tools\Test-Combat.ps1
powershell -File tools\Test-Sessions.ps1 -IncludeTimeout
powershell -File tools\Test-Dice.ps1
powershell -File tools\Test-PlayerCombat.ps1
dotnet test dotnet\KcdMp.Farkle.Tests
powershell -File tools\Test-WO94Synthetic.ps1                      # Shared Quests, no game needed
powershell -File tools\Test-WO96Synthetic.ps1                      # divergence-gated prompt / WAITING FOR PEER (WO-96), no game needed
```

`tools\Test-Pipe.ps1` additionally needs the real game running with the
plugin injected — see the script header. `tools\Test-AppearanceE2E.ps1` and
`tools\Test-PlayerVitalsE2E.ps1` need the real game and a running agent, but
**not** the native plugin — appearance sync and player vitals never touch the
DLL or the pipe, only the reflection debug API. `tools\Test-ReloadBehaviour.ps1`
needs all of that plus a human to reload a save mid-test. Every synthetic-peer
script derives its wire-protocol version from `tools\ProtocolVersion.ps1`
rather than hardcoding it — never add a new literal version byte to a script.

## What's left undone, what still needs a human, and reporting bugs

<details>
<summary><strong>Not done</strong> — genuinely missing or impossible, as opposed to untested</summary>

- **Emotes** — not implemented.
- **Duelling** — not implemented. The wire protocol reserves an
  `InteractionKind.Duel` value and nothing at all sits behind it; it reads
  like a shipped feature to anyone skimming `Protocol.cs` and it is not one.
- **Dice invite / accept / decline keybinds** — the console commands
  (`mp_dice`, `mp_accept`, `mp_decline`) are the working path. The key
  bindings in the code are built on guessed engine action names that have
  never been confirmed to fire.
- **Ranged weapon swings on ghosts** — melee is fully animated; bow/crossbow
  ranged action names are recorded but not yet wired.
- **Appearance sync's write latency is genuinely variable** — equipping or
  unequipping one item on a ghost usually lands within a second, but under
  heavier load it has taken up to a couple of minutes, and it self-heals via
  a 30-second resend rather than promising a fixed time. `mp_sync_appearance`
  forces an immediate resync if you don't want to wait. Hairstyle, face and
  beard do not sync — no reflected engine surface exposes them at all.
  Weapons *do* sync, through the same mechanism as armor.
- **Unequips are not verified the way equips are** — the agent retries what
  should now be *on* a ghost, but never confirms that what should now be
  *gone* actually went. A weapon unequipped several steps earlier was once
  observed back in a ghost's equipped map, reproducibly, minutes later. Not
  root-caused: it may be the game refilling an empty weapon slot from the
  ghost's own inventory rather than a failed write.
- **The soul walk right after injection still gives up after one 5s try** —
  the tick-liveness check above it now polls and retries; the step after it
  does not. Inject in the narrow window where the game has started ticking
  but a save has not finished loading and that injected instance declines to
  start the pipe, with no second attempt.
- **Reading the vanilla dice minigame's live state isn't possible** — its
  in-progress state isn't reachable from outside it. That's why dice is a
  separate relay-authoritative engine instead of mirroring the vanilla
  minigame.
- **Auto-detecting "you are actually in the world"** — the launcher still asks
  you to confirm you have loaded your save before it injects. Injecting too
  early is no longer fatal (the plugin waits for the game's tick instead of
  giving up on it), but the launcher cannot yet decide for you when you are
  really in the world, so it asks.
- **Nameplates are hidden while *your own* menu is open** — ghost bodies keep
  moving during your menu, but the `System.DrawLabel`/`DrawText` calls that
  draw names are immediate-mode, one frame per call, and the update pump is
  not frame-locked to the renderer. Pumping them would strobe rather than
  render, so they stay off.
- **The installer does not detect an incomplete Modding Tools data
  install** — it verifies `Framework.dll`/`CrySystem.dll` exist beside
  `KingdomCome.exe`, which proves the *engine binaries* are the right build.
  It does not check for the actual game data (`Data\Tables.pak` and the
  other mandatory paks), so a Modding Tools install that has never had its
  one-time **Workspace Setup** step run (see Install, above) passes the gate
  and then crashes the game with "114 tables are not loaded" — a real,
  reproduced failure, not a hypothetical one.
- **Installer code signing** — Setup.exe is unsigned, so a first download
  shows SmartScreen's "Windows protected your PC". Click *More info* →
  *Run anyway*.
- **A moved game is only half-handled** — re-running Setup finds the new path
  and re-deploys the mod, but will not overwrite a `GamePath` already set in
  your `settings.json`. The launcher notices the stale path at startup and
  opens Settings; it does not fix it for you.
- **`settings.json` follows the working directory, not the launcher** — the
  shortcuts the installer creates set it correctly. Launch
  `KCDMP_launcher.exe` from some other directory and it will read and write
  its settings there instead.
- **A claimed NPC's health still converges by damage events only** —
  simultaneous kill/loot arbitration between two players fighting the same
  NPC remains unhandled.
- **Animals don't sync** — chickens, dogs, deer and the like are out of scope
  by design, not a bug.

</details>

### Reporting a bug

The launcher's status bar has a **REPORT BUG** button — it opens either the
GitHub Issues page or this project's Discord directly, whichever you'd
rather use.

Say which side you were on (host or join), what you were doing, and what you
expected instead. Then attach whatever of these exists — the paths are exact,
and `%LocalAppData%` / `%AppData%` / `%Temp%` can be pasted straight into
Explorer's address bar:

| What | Where | Attach it when |
|---|---|---|
| Launcher log | `%AppData%\KCDMP_Launcher\app<YYYYMMDD>.log` | Always. This is the first one to grab |
| Launcher settings | `%LocalAppData%\KCDMP\settings.json` | Always — it is three lines and it says which game it found |
| Native plugin log | `%LocalAppData%\KCDMP\kcdmp-native.log` | Injection, ghosts, combat, or dice not appearing in game |
| Game log | `<ModdingTools>\kcd.log`, e.g. `...\steamapps\common\KCD2Mod\kcd.log` | Anything in-game. Look for `[KCD2-MP]` lines |
| Agent log | `%LocalAppData%\KCDMP\agent.log` (`.prev.log`/`.prev2.log` for the last two runs, since WO-39) | Connected but nothing syncs |
| Relay output | The `KcdMpServer.exe` console window on the **host's** PC | Nobody can join, or people get dropped |
| Installer log | `%Temp%\Setup Log <date> #NNN.txt` — newest one | Anything that went wrong during install |

Also useful: your version (Add/Remove Programs → *Kingdom Come: Together*), whether
both players are on the same network, and — for anything that looks like the
game ignoring the mod — confirmation that `<ModdingTools>\Mods\kdcmp\` exists
and that you launched through the launcher rather than through Steam.

Please redact your public IP from logs if you were playing over the internet.

## License and provenance

Licensed under the [GNU General Public License v3.0](LICENSE).

This is a fork of [`marczukmichal/kcd2-multiplayer`](https://github.com/marczukmichal/kcd2-multiplayer),
**continued here with the original developer's permission**, including
permission to upstream changes back if the two projects converge. All
credit for the original concept and implementation goes to marczukmichal —
this fork exists to keep building on that work, not to replace it. The
upstream repository carries no license of its own; that's a fact about the
upstream project, not a claim that it was itself GPL-licensed. This fork's
own code, from the point of forking onward, is licensed under GPLv3 as
stated above.
