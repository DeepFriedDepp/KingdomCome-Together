# WO-154 Phase 7b — the mod menu: findings

The in-game mod menu and the fast-travel switch it exposes. The player's page is
[MOD-MENU.md](MOD-MENU.md). This file is this stream's record; it is meant to be
folded into `docs/WO-154-findings.md` when the WO's streams come together (the
Lua section header already points there).

Evidence words as in the other findings: **observed** (seen in a game),
**code-verified** (read in our code or the game's files), **synthetic** (the
MoonSharp suite or a unit test), **not live-tested**.

## 1. What was built

| Piece | Where |
|---|---|
| The menu (state, items, drawing, keys, rules, the four new switches, fast travel) | `kdcmp/Data/Scripts/Startup/kdcmp.lua`, section `-- ===== WO-154: the mod menu` (namespace `KCD2MP.w154menu`, 3 block locals) |
| Small hooks in shared spots | `mp_draw_row` (1 line), `KCD2MP_ShowNativeToast` (1), `KCD2MP_LabelTick` (badges + ping), `KCD2MP_DrawInteractionUI` (1, the 1 Hz backstop), `handleAction` (5), `KCD2MP_Wo114FastTravelBlock` (2 guarded lines), the console registration list (one block) |
| The keys | `kdcmp/ConfigPatch/*.xml` (four `kcd2mp_menu_*` actions), `dotnet/KcdMp.Client/KeybindPak.cs` (MenuKey) |
| The agent | `dotnet/KcdMp.Client/GameBridge.Wo154Menu.cs`, rules in `Wo154Menu.cs`; one line each in `GameBridge.cs` (connect, disconnect, the event switch), `GameBridge.Wo121.cs` (SessionSetting dispatch), `GameBridge.Wo114.cs` (the engine's refusal), `BatchQueue.cs` (a Level push) |
| The wire | `SessionSettingKey.CrimeMode = 2`, `FastTravel = 3` (`ProtocolV8.cs`; append-only, no protocol bump) |
| Tests | `tools/Test-WO154Synthetic.lua/.ps1` (223), `dotnet/KcdMp.Client.Tests/Wo154MenuTests.cs` (43) + `Wo154MenuKeysTests.cs` (17) |

Nothing in the WO-123/124/125/131/132/135/137/147/151 sections was edited; the
menu only calls their functions.

## 2. The keys (code-verified, not live-tested)

* **Insert** opens and closes, **PgUp / PgDn** choose, **End** changes: four new
  actions `kcd2mp_menu_toggle|up|down|change` in the `interaction` actionmap,
  press only — the same shape as the dice keys (WO-6/148), which are unchanged
  (a test pins all ten keys, whatever the menu key).
* The game's own two files (`Libs/Config/defaultProfile.xml`,
  `keybindSuperactions.xml`, read out of `IPL_GameData.pak` of the 1.5.5 retail
  install and the Modding Tools install on the build machine — **byte-identical**)
  name none of `insert`, `pgup`, `pgdn`, `end`, `np_add`, `np_subtract` in any
  `input=` or `keyboard=` value. They do name `home`, `delete`, `slash`, `minus`,
  and the `debug` map binds `f3 f4 f8 f10 f11 7 j np_0 np_multiply np_divide space c`,
  the `haste` map every letter and `h`.
* **MenuKey** (the launcher's `settings.json`) may be `insert` (default),
  `np_add` or `np_subtract`; anything else is Insert and the keys-pak outcome says
  so (`menu key insert (MenuKey 'x' is not one of insert|np_add|np_subtract --
  insert used)`; a value that is not a key name is not repeated). Left out on
  purpose: ScrollLock and Pause (CryEngine's own), the console key, the WO-6 traps
  (F1/F3/F10, `np_multiply`, `np_divide` — it crashed the game —, `H`), every key
  the mod already uses.
* **Residual risk, for the live check:** WO-6 recorded "the Numpad operator keys"
  as debug toggles in the Modding Tools build. The game's XML shows only `*` and
  `/` in the debug map; `+` and `-` are bound nowhere, but they were never pressed
  live one at a time. The first live item below presses all six keys.
* The menu shows the key bound **in this game run**: the agent reads it back from
  the installed `kdcmp_keys.pak` (`KeybindPak.BoundMenuKeyInPak`), the stored one
  from `settings.json`; a change made in the menu says "(from the next game
  start)" until then.

## 3. Never in the game's own screens — what it relies on

1. **The game's own menu actions reach the hook** before the screen takes the
   keys (WO-12 observed `open_apse_inventory_keyboard` and `open_menu` in
   `Player.OnAction`): `open_menu`, `open_pause_menu` (Esc, Backspace),
   `open_apse_inventory_keyboard`, `open_apse_player`, `open_apse_questlog`,
   `open_apse_map_keyboard`, `open_apse_codex`, `open_codex_new_entry`,
   `open_skiptime` close the mod menu; they are never consumed. (code-verified
   names, synthetic)
2. **The agent's log-tail edge** (`PauseStateChanged`: the ESC menu's
   `sqc_ptag_menu`, the inventory/map/journal's `ApseOpen/ApseClose`,
   skip-time, a rendered cutscene — WO-11/80, live-verified then) closes it at
   once and refuses opening while it holds; repeated every second in the session
   push. Needed because WO-138's levers keep the world (and the Lua timers)
   running behind a menu in a session.
3. **Frames that stood still** over a second (a menu without the levers, a load):
   the menu closes when they come back; a frame chain a load killed is noticed at
   the next key press and the menu opens fresh.
4. **Our keys do not arrive** while the pause menu has focus (WO-12: no callback
   at all) and the inventory re-routes keys to its own actions (`focus_prev`).
5. Plus: `KCD2MP.cutsceneActive`, `KCD2MP_W131InConversation()` (a bark is not a
   conversation), WO-136's load hold, dead / knocked out
   (`KCD2MP_ReadSelfVitals`) and the agent's downed flag (WO-113), checked four
   times a second while open.

## 4. Drawing

* `System.DrawText(x, y, text, size)` only — the 4-argument call this build draws
  with; no colour arguments are passed (unverified there), the chosen row is
  marked with `>`. Its own frame chain (4 ms timer: every frame up to 250 fps; the
  label loop's 8 ms skips every second frame above ~125 fps), only while open, so
  the menu works without the agent too.
* Placed at 62 % of the width, 10 % of the height (`System.GetViewport()`,
  proven in WO-6; 1920×1080 if it fails) — the right side, never the centre.
  Sizes 1.7 / 1.5 / 1.35, 22 px lines. **Not seen on a screen yet**; tunable live
  without a rebuild: `#KCD2MP.w154menu.LAYOUT.xFrac = 0.6` etc.
* Rows: title `Kingdom Come: Together -- mod menu (Insert to close)`, the clean
  screen's notice when on, group headers `-- Display --`, items
  `> Name: Value  [set by the host]`, the chosen item's explanation (wrapped at
  60 characters), a note line (8 s), and `PgUp/PgDn choose  End change  Insert close`.

## 5. Items, whose they are, what they call

Every changeable item calls the function its console command's template names
(the synthetic suite reads each template and compares), with the console's own
argument (`on`/`off`, `joint`/`individual`).

| Item | Command (function) | Whose |
|---|---|---|
| Partner's name badge | `mp_name_badges` (`KCD2MP_W154SetNameBadges`) — new | own |
| Ping and clock line | `mp_ping_line` (`KCD2MP_W154SetPingLine`) — new | own |
| Clean screen | `mp_clean_screen` (`KCD2MP_W154SetCleanScreen`) — new | own |
| Friendly fire | `mp_friendly_fire` (`KCD2MP_Wo121SetFriendlyFire`) | host (shown: `ffSession`) |
| Crime | `mp_crime_mode` (`KCD2MP_W151SetCrimeMode`) | host (only the host's `crimeJoint` is ever read) |
| Fast travel | `mp_fast_travel` (`KCD2MP_W154SetFastTravel`) — new | host |
| Keep players together | `mp_leash` (`KCD2MP_SetLeash`) | host (WO-114: the host's agent decides the leash) |
| Sleep and wait together | `mp_sleep_vote` (`KCD2MP_SetSleepVote`) | own (`Wo140Rules.VoteRequired` reads this machine's switch) |
| Hear each other's whistle | `mp_whistle` (`KCD2MP_W151SetWhistle`) | own (gates sending and playing) |
| Partner's herb picking | `mp_avatar_herbs` (`KCD2MP_SetAvatarHerbs`) | own (how the partner looks here) |
| Voice chat | `w154_voice on|off` → the agent answers `KCD2MP_W154VoiceState` | own (the agent's side is the launcher stream's) |
| Menu key | `w154_menu_set MenuKey <key>` → the launcher's `settings.json` | own |
| I'm stuck | `mp_unstuck` (`KCD2MP_W151Unstuck`, soft) | — |
| Something's wrong here | `mark_odd` (`KCD2MP_Mark('odd')`) + the "send your logs with Report a bug before you restart" line | — |
| Version, Connection | information (the agent's push) | — |

Not added: `mp_respawn` (off = a vanilla Game Over in a shared world: unsafe for a
menu), and every switch whose help says test, probe, legacy or a WO number only.

## 6. Remembered between sessions

* A change in the menu emits `w154_menu_set <Key> <value>`; the agent writes it
  into `mod-settings.json` beside it (the install folder, `%LocalAppData%\KCDMP`)
  with `SettingsJson` only (key by key, the player's other keys and layout kept,
  atomic). Keys: `NameBadges PingLine CleanScreen FriendlyFire CrimeMode FastTravel
  Leash SleepVote Whistle PartnerHerbs` (JSON `true/false`, CrimeMode `"joint"` /
  `"individual"`). Never a game save. A console command is not remembered (a test
  stays a test).
* At every session start and 2 s after every "Gameplay started" the agent pushes
  the saved values back (`KCD2MP_W154MenuRestore`, which calls the same setter).
  The host's levers only while this player hosts (also when he becomes the host
  later in the same world); a joiner never applies them (the agent leaves them out,
  and the mod refuses them).
* A restored clean screen says once, through the clean screen, why nothing of the
  mod shows and how to turn it off.
* `MenuKey` goes into the launcher's `settings.json` with `SettingsJson`, only when
  that file exists (never created).

## 7. The host's levers on a joiner

Friendly fire already crossed (WO-121's SessionSetting key 1). Crime mode (key 2)
and fast travel (key 3) now ride the same channel: the host's agent sends both on
a change, to a new partner at once, and every 10 s; the relay forwards a
SessionSetting from the host only (unchanged); an older agent ignores a key it
does not know, so no protocol bump. The leash's value comes from WO-114's Leash
config message. The joiner's agent pushes them to the mod once a second
(`KCD2MP_W154MenuSession`); the menu shows them locked with `[set by the host]`,
"Waiting for the host" until heard.

## 8. Fast travel (`mp_fast_travel`, the host's, default OFF for 0.45.0)

* While a co-op session is up (a role from the relay and a partner connected) and
  the session's value is off, the mod holds the engine's own switch
  `wh_pl_FastTravelEnabled` at 0 — on both machines; a joiner follows the host's
  value (not heard yet: held, fail-closed). Re-applied every second (the cvar does
  not survive a game restart, WO-114). The value from before is given back when the
  session ends (the agent's disconnect, the partner gone, or the agent silent for
  10 s: the label loop's backstop).
* Said once per session, plainly (the game's own HUD text): host — "Fast travel is
  off in this co-op session (you can turn it on in the mod menu)"; joiner — "Fast
  travel is off in this co-op session (your host can turn it on in the mod menu)"
  (after the host's value has arrived). A change during the session is said on both
  machines ("... (set by the host)" on the joiner). The engine's refusal line, while
  held, says it again (the agent's log tail).
* **WO-114's joiner block shares the switch** and is unchanged in behaviour:
  whichever of the two holds it first remembers the original; whichever lets go
  last gives it back (both orders pinned by the suite). WO-114's own suite: 60/60.
* On the host this means fast travel is off as soon as a partner is connected,
  until the host turns it on in the menu (a saved choice is restored).

## 9. The clean screen

Hides every `mp_draw_row` row (they log `MP-SCREEN ... text=""` as gone), the ping
line, the name badges (the `DrawLabel` above the partner) and the mod's native
toasts (`KCD2MP_ShowNativeToast`; still logged as `MP-TOAST`). Kept, on purpose:
the dice board of a match being played (`ShowTutorial`) and its own say lines — it
is the game being played. The menu still opens; its first row says the clean
screen is on.

## 10. Tests and gates (this branch)

| Gate | Result |
|---|---|
| `tools/Test-WO154Synthetic.ps1` | 223 passed, 0 failed |
| every `tools/Test-*Synthetic.ps1` (45 suites) | all 0 failed |
| `dotnet test dotnet/KcdMp.Client.Tests -c Release` | 935 passed, 0 failed (60 of them new) |
| `dotnet test dotnet/KcdMp.Relay.Tests -c Release` (the protocol gained two constants) | 62 passed, 0 failed |
| `tools/Test-WO106ConsolePlaceholder.ps1` | 7 passed |
| `tools/Test-WO110LuaLocals.ps1` | 134 main-chunk locals (unchanged), 6 passed |
| `tools/Build-And-Install-Mod.ps1 -NoInstall` | the pak builds (not installed; the rebuilt pak is not committed) |

## 11. The live check (the proof) — for the next session

Markers are console commands (`mark_<word>`); throwaway saves only.

1. **The keys, one at a time** (solo, `mp_log_actions on`): press Insert, PgUp,
   PgDn, End, Numpad +, Numpad - and watch for **anything** the game does by itself
   (WO-6's rule). Expected: `ACT 'kcd2mp_menu_toggle' a=press` etc.; nothing else
   happens. Then `mp_log_actions off`. Marker: `mark_menukey`.
2. **The menu in the world, both roles.** Each of you: Insert, PgDn a few times,
   Insert. Frames of the menu open on both screens, please. Then once more on a
   horse and once seated (the `interaction` map was seen live while seated, WO-6;
   riding is unproven: `horse_mounted` is an exclusive map). Marker: `mark_menu`.
   kcd.log: `MP-MENU open by=key`, `MP-MENU select`, `MP-MENU close (its key)`.
3. **Esc closes it**: open it, press Esc; open it, press I. Both close it.
4. **The name badges off and back on** (either of you, from the menu). Marker:
   `mark_badges`. The partner's name disappears and comes back; the other screen is
   unchanged.
5. **Each switch = its console command.** For each switch, toggle it in the menu
   and compare with the console command's effect (or the line it logs).
6. **A host lever locked on the joiner.** The joiner opens the menu: friendly
   fire, crime, fast travel and keeping together show the host's values with
   `[set by the host]`; End on one changes nothing ("Set by the host"). The host
   changes crime to Individual: the joiner's menu shows it within ~1 s. Marker:
   `mark_menu_locked`.
7. **Fast travel.** At the session start both get the one-line notice; the host
   tries a fast travel on the map (refused, said again). The host turns fast travel
   on in the menu: the host travels, the joiner is brought along. After the session
   (disconnect) fast travel works as before. Marker: `mark_fasttravel`. kcd.log:
   `WO154-FASTTRAVEL off ...`, `... given back ...`.
8. **The clean screen** on, a screenshot, off. Marker: `mark_clean`.
9. **Remembered.** Change a few things, quit, start again: the menu shows them.
   `agent.log`: `MP-MENU-SAVE`, `MP-MENU-RESTORE`.
10. **The menu key.** Choose Numpad + in the menu, restart the game through the
    launcher: Numpad + opens the menu (the launcher log: `keys pak: written ...
    menu key np_add`).

Driving it without the keys (for frames on a machine nobody is at): `mp_menu open`,
`mp_menu down`, `mp_menu select fast_travel`, `mp_menu change`, `mp_menu status`,
`mp_menu close`.

## 12. Open items

* **Nothing of this has run in the game.** The live check above is the gate.
* The launcher stream: (a) the launcher's Save still serialises its whole
  settings object over `settings.json` (0.44.0) and would drop `MenuKey` — keep it
  (SettingsJson); (b) the agent's answer to `w154_voice on|off`
  (`KCD2MP_W154VoiceState`); (c) optionally `mod-settings.json` in the Report a bug
  bundle (only booleans).
* The release: `Verify-Install` markers for the menu, the README row, the tester
  page (fold section 11 in), the version — not touched here.
