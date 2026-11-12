<p align="center">
  <img src="docs/branding/KCT_txt.png" alt="Kingdom Come: Together" width="600">
</p>

<p align="center">
  <em>Play Kingdom Come: Deliverance II with a friend. The whole game, in one world.</em><br>
  <sub>An unofficial, free fan mod. Not affiliated with or endorsed by Warhorse Studios or PLAION.</sub>
</p>

<p align="center">
  <a href="https://github.com/DeepFriedDepp/KingdomCome-Together/releases/latest"><img alt="Latest release" src="https://img.shields.io/badge/latest%20release-v0.63.9-8a3324?style=flat-square"></a>
  <img alt="Platform: Windows" src="https://img.shields.io/badge/platform-Windows-555555?style=flat-square">
  <a href="LICENSE"><img alt="License: GPLv3" src="https://img.shields.io/badge/license-GPLv3-2c3e50?style=flat-square"></a>
  <a href="https://dsc.gg/kcd2-together"><img alt="Discord" src="https://img.shields.io/badge/Discord-Join-5865F2?logo=discord&logoColor=white&style=flat-square"></a>
</p>

<p align="center">
  <a href="#install-and-play"><strong>Install</strong></a> ·
  <a href="docs/QUICKSTART.md"><strong>Quick start</strong></a> ·
  <a href="docs/KNOWN-ISSUES.md"><strong>Known issues</strong></a> ·
  <a href="https://dsc.gg/kcd2-together"><strong>Discord</strong></a>
</p>

---

## What it is

Kingdom Come: Together is co-op for the single-player game. One of you hosts your world; your friend joins it with
their own Henry. From there you play the game you already know, side by side: the same villages and people, the same
quests, the same fights, the same clock... experienced TOGETHER.

It is not a server platform and there are no lobbies of strangers. It is the campaign, shared, with only who you want to enjoy it with.

## Where it's at

**Open beta.** You can play for hours and get real quests done together. You will also see things that look wrong,
and now and then something will break. New builds come often.

| Works well | Works, but rough | Not yet |
|---|---|---|
| Joining a friend's world with your own character, or a new one | Joining each other's conversations: you listen in and vote on answers, but the camera and keys can misbehave | Playing far apart (you are kept within about 650 m) |
| Seeing each other: walking, riding, clothes, weapons | Villagers glide when they walk on the partner's screen | Carts moving on the partner's screen |
| Fighting the same enemies: hits, knockdowns and deaths show for both | Some villagers stand idle, or work without their tools, where the host sees them busy | Grabs in fights |
| Shared quests: either of you can move a quest forward | Cutscenes play for both, but one can leave the partner on a black screen for a while | Shared chests and shop stock (each player has their own) |
| Looting bodies and dropping items for each other | Trading as the partner can close by itself | Road encounters matching on both screens |
| Sleeping together, one shared clock | Paired moves and weapon draws in fights don't always show | Stolen marks on shared items |
| Crime: the host's guards judge both of you | Bows and crossbows: the shot shows, the draw and aim don't | The regular (non-Modding-Tools) game |
| | Lip sync, voice chat and dice between players are unpolished | |

<sub>As of release 0.63.9.</sub>

The full, current list lives in **[Known issues](docs/KNOWN-ISSUES.md)**. What changed in each build is on the
**[releases page](https://github.com/DeepFriedDepp/KingdomCome-Together/releases)**.

## What you need

- Windows and Steam
- Kingdom Come: Deliverance II on Steam
- The game's free **Modding Tools** (a separate entry in your Steam library; the launcher installs it for you)
- A friend with the same build of the mod

The mod runs on the Modding Tools version of the game, not the regular one. You start it from the launcher; starting
the game from Steam still gives you normal single-player.

## Install and play

1. Download **`KingdomComeTogether-Setup`** from the [releases page](https://github.com/DeepFriedDepp/KingdomCome-Together/releases/latest) and run it. Both of you.
2. Open the launcher. The first time, it shows a short checklist and sets everything up for you.
3. **Host:** click **HOST GAME**, click **PLAY**, then pick **Start Game** on the game's main menu.
4. **Partner:** click **JOIN THROUGH STEAM**, select Find Friends, choose Join on your Friend's name, click **PLAY**, then pick **Join Game** in the main menu.
IF PROMPTED as the Joiner, select "Start a New Save" if this is your first time.

That's it.

Playing from different houses? Use the Steam code, or see **[Networking](docs/NETWORKING.md)** for the other ways.
Every screen, step by step, is in the **[Quick start](docs/QUICKSTART.md)**.

**Updating:** run the new Setup over the old one. Your settings stay. Both players must be on the same build.

## Good to know

- **Insert** opens the mod menu in the game: every setting in plain words.
- **Period (`.`)** marks the moment something looks wrong, so it is easy to find in a bug report.
- Start past the prologue if you can. **New adventure → Skip the prologue** does this for you.
- No accounts and no telemetry. The mod never changes your Windows security settings.

## Something broke?

Click **REPORT BUG** in the launcher. It gathers the logs into one file with personal details removed. Send that file
on [Discord](https://dsc.gg/kcd2-together) or in a [GitHub issue](https://github.com/DeepFriedDepp/KingdomCome-Together/issues),
and say whether you were hosting or joining and what you were doing.

## More reading

| | |
|---|---|
| [Quick start](docs/QUICKSTART.md) | From nothing to playing together |
| [Known issues](docs/KNOWN-ISSUES.md) | What is not right yet |
| [Networking](docs/NETWORKING.md) | Ways to connect over the internet |
| [The mod menu](docs/MOD-MENU.md) | Every setting explained |
| [Dice between players](docs/DICE.md) | How to play Farkle against your friend |
| [Installing by hand](docs/LAUNCHING.md) | If the installer misbehaves |
| [Building from source](docs/BUILDING.md) | For developers: layout, build, tests |

## Credits and license

Kingdom Come: Together continues [`marczukmichal/kcd2-multiplayer`](https://github.com/marczukmichal/kcd2-multiplayer)
with the original developer's permission. Credit for the original idea and implementation is theirs. See
[AUTHORS](AUTHORS).

Licensed under [GPLv3](LICENSE) with two added terms in [NOTICE](NOTICE): keep the credits, and don't present a
modified version as the official one. This repository is the official one.

Kingdom Come: Deliverance II, its assets and its content belong to Warhorse Studios and PLAION. This project's
copyright covers only its own code.
