# The start save (WO-159)

A host's **New adventure** (the game's main menu, **Start Game**, when Kingdom Come: Together started the game)
begins from the one save in this folder: the launcher copies it into an empty save slot (playline) with a new
playthrough seed, so every new adventure is its own world, and loads it. Setup installs it in `start-save\` beside
the launcher; `tools\Publish-Release.ps1` takes it from here.

**Empty until the maintainer supplies it.** A build without it ships none; the menu then shows New adventure
greyed with "No start save is installed".

## The recipe

1. The **Modding Tools** build of the game (not the regular game).
2. A new game, played **past the prologue**: the player is **Henry** (not `player_bohuta`). The game's own first
   save after the prologue (`permanent002.whs`) is the natural one.
3. **No partner ever joined** that world and the mod did nothing in it (no co-op session, no mod figure saved in it).
4. A calm spot with nothing scripted running (not checkable from the file: by eye).

Check it, and make the copy to put here:

```
powershell -ExecutionPolicy Bypass -File tools\Validate-StartSave.ps1 -Path <the save> -WriteScrubbed assets\start-save\permanent002.whs
powershell -ExecutionPolicy Bypass -File tools\Validate-StartSave.ps1 -Path assets\start-save
```

`-WriteScrubbed` clears the save header's account and machine names and its used-mods list (header metadata only;
the world is untouched). The release build refuses a start save that does not pass.

## What it is

The save is the maintainer's own play data, made with the game: the state of a world (positions, quest flags,
inventory) in the game's own save format, about 1.2 MB. It holds no game asset files and no code. Under which terms
it is distributed is the maintainer's decision (docs/WO-159-findings.md); it is useful only with the game it came from.
