# The start saves (WO-159)

A host's **New adventure** and a joiner's **Join with a new character** (the game's main menu, when Kingdom Come:
Together started the game) begin from these saves, one per playstyle:

```
assets/start-save/soldier/<one save>.whs
assets/start-save/adviser/<one save>.whs
assets/start-save/scout/<one save>.whs
```

The launcher copies the chosen one into an empty save slot (playline) with a new playthrough seed, so every new
adventure is its own world, and loads it; a joiner's agent takes its Henry from it. Setup installs them in
`start-save\<playstyle>\` beside the launcher; `tools\Publish-Release.ps1` takes them from here and refuses one that
does not pass the check below.

**Empty until the maintainer supplies them.** A playstyle without its save is greyed on the menu; with none, New
adventure says "No start save is installed".

## The recipe

1. The **Modding Tools** build of the game (not the regular game).
2. A new game, played **until Hans and Henry part ways**: main quest M03 ("Laboratores": the bar fight, the pillory)
   is done, and nothing of M05 ("Wedding Crashers", which the game starts at that moment) is done yet.
3. **The playstyle** chosen in the prologue's ride (Soldier, Adviser or Scout) is the folder's. Make the three from the
   **same choices otherwise** (the same answers in every conversation), so the three differ only in the playstyle.
4. **No partner ever joined** that world and the mod did nothing in it (no co-op session, no mod figure saved in it).
5. A **calm spot with nothing scripted running** — no conversation, no cutscene starting (not checkable from the file:
   by eye).

Check each, and make the copy to put here:

```
powershell -ExecutionPolicy Bypass -File tools\Validate-StartSave.ps1 -Path <the save> -Style soldier -WriteScrubbed assets\start-save\soldier\permanent002.whs
powershell -ExecutionPolicy Bypass -File tools\Validate-StartSave.ps1 -Path assets\start-save\soldier -Style soldier
```

`-WriteScrubbed` clears the save header's account and machine names and its used-mods list (header metadata only; the
world is untouched). The checks: Modding Tools build, Henry, a playthrough seed, no mod data in the world, a clean
header, **where Hans and Henry part ways** (M03 done, nothing of M05 done), and **the playstyle** (Henry's own playstyle
skills carry the most experience of the three).

## What they are

The saves are the maintainer's own play data, made with the game: the state of a world (positions, quest flags,
inventory) in the game's own save format, about 1.2 MB each. They hold no game asset files and no code. Under which
terms they are distributed is the maintainer's decision (docs/WO-159-findings.md); they are useful only with the game
they came from.
