# WO-134 — world items, and the rebrand: progress

Findings: `docs/WO-134-findings.md`. Started from `5ae9cb8` (`origin/main`:
WO-132 and WO-133 both present with their findings docs, plus the maintainer's
art commit), tree clean, fast-forward only. No force-push.

## Log

- Read WO-131 §1c, WO-132, WO-133, WO-125 findings, the co-op decisions, WO-48
  findings; the WO-48 code in `kdcmp.lua`, `GameBridge.cs`, the relay.
- The art is at `docs/branding/` (not `branding/`): used from there; the
  originals stay there only.
- Probes (the real game, a throwaway load, the DLL's pipe for a test soldier's
  death): pickable names and positions across two loads of one save; 1,091
  stashes (classes, master links, restock periods); a test body's inventory,
  strip, create + equip, the loot screen from Lua, timers while it is open, a
  change while it is open; Warhorse's own `PickableItem.lua`, `AnimStash.lua`,
  `PickableArea.lua`, `ItemSlot.lua` and the scriptbind reference.
- Phase 1 first: `Test-WO134DropsSynthetic` (52/52 on the unchanged code),
  `Test-ItemSyncRelay` (11/11 after rebuilding its stale Debug relay), avatarpeer
  and synthpeer `itemdrop` / `itemclaim` verbs, the live `drops.py` driver: H0
  9/9, J0 9/9 on the baseline build.
- Decisions:
  * bodies: the joiner's takes are optimistic (the game's loot screen moves the
    item; the mod sees it within 250 ms and asks the host; `gone` takes it back
    off Henry). A per-click request is impossible without replacing the game's
    screen. Worn items come from the host's EquipmentManager over REST.
  * world items: identity = class + position (names are per-process); a match
    radius of 0.35 m, 2.5 m for an item that fell off a corpse; an unmatched item
    stays per machine (never guessed).
  * chests: ledgers of net takes/puts per container and class; the host's paired
    with its saves, the joiner's with its Henry snapshots (WO-125's rule: loss,
    never duplication); entries expire with the container's restock period.
  * wire: two join-channel types (0x5C–0x5F), text bodies checked field by field;
    no protocol bump (the release check refuses mixed builds).
  * herbs: left per player (a C++ gathering minigame, not a pickup).
- Commit `16b9c77` (phases 1–5, after H1 and J1).
- Found live and fixed: body parts out of order (H1); item slots respawn a
  removed item (H1: `W134.takeAway` through an avatar's inventory); chest
  snapshots across a load (J1: `W134.forgetWorld`, the agent ignores chest
  events while a world loads); container names with `:` and 190+ characters (the
  check and the console-length packing of the apply calls).
- Phase 6a: `tools/Build-Branding.py` (header strip, 128 px logo, 1280 px banner,
  the multi-size icon with the "KC" crop for 16/24/32), the launcher, the
  installer, `Build-Installer.ps1` / `Test-Installer.ps1` /
  `Test-InstallerUpgrade.ps1` / `Verify-Install.ps1`, the in-game toasts, the
  Discord hover text, README, `docs/TEST-0.30.9.md`, the runbook, the release
  notes, `docs/INSTALLER-TESTING.md`, `docs/LAUNCHING.md`.
- `VERSION` → `0.30.9`.
- Final live runs on the final build: H2 (drops 9/9; same-frame races; the slot
  item stays gone), J2 (a restore join after a full restart; the ledgers; drops
  9/9).
- `tools\Build-Installer.ps1`: the first run stopped at the agent unit tests
  (`Wo123Tests.Join_wire_table_is_consistent` had the join channel's range
  hard-coded to 0x5B); widened to 0x5F; the second run green end to end.

## Gates (observed, the last `Build-Installer.ps1` run)

| gate | result |
|---|---|
| relay round trip | 51/51 (new: the loot types cross joiner → host and host → joiner only) |
| agent unit tests | 434/434 (new `Wo134Tests.cs`, 15) |
| synthetic suites | 31 suites, all green (new `Test-WO134DropsSynthetic` 55/55, `Test-WO134Synthetic` 60/60) |
| static checks | console placeholders 7/7, Lua locals 6/6 |
| native unit tests | 53/53 |
| local publish + payload coherence + smoke | pass |
| installer | `release\KingdomComeTogether-Setup-0.30.9.exe`, 96.9 MB, sha256 `59929b9f975dba5a14b9766aa0752f4cb57366cc296c24718b0e550773f307de` |

Outside the release gates: `Test-ItemSyncRelay` 11/11 (baseline). The WO-134
markers are present in the built payload and the shipped pak (checked).

## For the maintainer (not done from this shell)

1. **Install over the existing install** (`release\KingdomComeTogether-Setup-0.30.9.exe`,
   everything closed): Windows' apps list shows one entry, **Kingdom Come:
   Together**; the Start menu shows **Kingdom Come Together** (the old "KCD2
   Multiplayer" shortcuts are gone); settings kept; then `tools\Verify-Install.ps1`.
   Installers are never run from this shell (the AppData sandbox rule).
2. **The launcher window screenshots** for `docs/wo134-shots/`: the title, the
   banner header, the logo and "Kingdom Come: Together v 0.30.9" in the bottom
   bar, the taskbar icon.
3. The two-player session: `docs/TEST-0.30.9.md`.
4. Discord: if the logo gets a new art key on Discord's side, it goes in the
   agent's `DiscordLargeImageKey` setting (the key is unchanged here).

## Side effects on this machine (disclosed)

- **Focus: once.** A capture attempt of the launcher (started hidden, shown
  without activation) still took the foreground for about 12 s: the launcher
  activates its own window on start. Closed at once; not tried again. The game
  window never came to the front (`foreground=False` after every launch and
  load). No key or mouse input.
- Saves: the host agent's world saves (`playline1/autosave078`–`082` in H1,
  `autosave078` in H2) and the joiner's snapshot QuickSaves (moved out by the
  agent itself) — all moved into the session scratchpad; `playline1`–`3` are back
  to their original file sets (checked by listing after every run). Throwaway
  worlds only (`quicksave036` and copies of it).
- The test game's installed `kdcmp.pak` is the WO-134 build (the previous one
  is in the session scratchpad). Test NPCs (autotest souls) were spawned and
  killed at the flat test spot away from settlements; nothing was stolen; the
  bodies, the bread and bowls, and the chest items changed only in throwaway
  worlds, never saved into a real playline.
- `docs/branding/`: the maintainer's originals untouched; `banner-1280.jpg` added.
- `dotnet/KcdMp.Server/bin/Debug` rebuilt for `Test-ItemSyncRelay` (build
  output, not committed).
