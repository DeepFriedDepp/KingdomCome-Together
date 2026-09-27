# WO-134 — world items, and the rebrand: findings

Unattended, solo, one machine: the Modding Tools game, the throwaway save
`playline1/quicksave036` (and a host-world copy made from it), a local relay on
its own port. The real game ran as the **host** with `tools/wo121/avatarpeer`
as the joiner (runs H0, H1, H2; H1 also with WO-123's synthetic joiner), and as
the **joiner** of `tools/wo118/synthpeer --join-host125` (runs J0 in its own
world; J1, J2 after real joins into the synthetic host's world). Evidence marks:
(observed), (code-verified), (synthetic), (inconclusive). `VERSION` is
**0.30.9** (the maintainer's string). Screens: `docs/wo134-shots/`. Progress
and side effects: `docs/WO-134-progress.md`.

## 0. Answer first

| item | result |
|---|---|
| **drops for each other (the top rule)** | **unchanged, green before and after.** Nothing in WO-48's code path was edited. Baseline on the pre-WO-134 build: synthetic 52/52, relay 11/11, live as host 9/9 (H0), live as joiner 9/9 (J0). After: synthetic 55/55 (the same 52 plus 3 checks that the new hooks leave a drop alone), live as host 9/9 twice (H1, H2 on the final build), live as joiner inside the host's world 9/9 twice (J1, J2 on the final build) (observed / synthetic) |
| how a drop is recognised by every new rule | an entity is a player's drop when its name is the ground copy of any tracked WO-48 drop (mine, or a peer's materialized one), a materializer anchor, or any `kcd2mp_`/`kcdmp_` entity (`W134.isDrop`). World-item requests, host removals and "gone" removals all skip them; chest and body rules never touch a pickable (code-verified; synthetic D6, I6, I7, I11, I13) |
| **NPC bodies, shared** | **built, observed on both sides.** The joiner's loot is a request to the host: its copy is set to the host body's own items (worn ones put on) and the game's loot screen opens on them (sheet 2). Each take goes to the host, which moves it out of its one body: ok, or "gone" and the item is taken back off Henry with "Someone already took that." Same-frame double take: ok then gone (observed). The host's looting reaches the joiner's copy: inventory and look (clothed → stripped, sheet 1) (observed). Pickpocketing stays blocked (synthetic; WO-131 path unchanged) |
| **loose world items, per world** | **built, observed.** A joiner's pickup asks the host first; the host matches by item class + position (≤ 0.35 m; 2.5 m for an item that fell off a corpse), never by name; ok → the host's copy is gone and the joiner picks his own up; gone → removed here with the line; unknown → picked up here as before (per machine). The host's pickups remove the joiner's copy. All four observed on the joiner, ok/gone/unknown on the host |
| item identity | entity names are **not** machine-stable: the numeric suffix is a per-process spawn counter (`egg000352` → `egg000766` on a reload of the same save). Class + position is: every resting item matched at **0.000 m** across two loads (46 items); two eggs 6 cm apart stay distinct; items NPCs carry or use moved 0.5–17 m and do not match (left per machine) (observed) |
| found: item slots respawn | a plain `System.RemoveEntity` is not a take to the engine: an item belonging to an item slot (food on a table, tools on racks — most authored loose items) came back at 0.000 m within 3 s. Moving the item into an avatar's inventory is the engine's own take: no respawn after 25 s (observed). Every removal now goes that way (`W134.takeAway`) |
| herbs | not an item pickup: `PickableArea.Gather` → `Minigame.StartHerbGathering`, on a patch the engine activates. **Left per player** (code-verified from the game's scripts) |
| **chests, per player, across rejoins** | **built, observed.** Takes and puts in reach are recorded; the host's ledger is kept per world and paired with every host save; the joiner's is stored beside every Henry snapshot (WO-125's store) and comes back with it. After a join: the host's takes are put back, the joiner's own takes removed. Live: the host took a cap from chest A, saved; a first join → A full for the joiner (the cap back at the host's condition). The joiner took the pork from B; host save → snapshot + ledger; host reload → rejoin → B still empty for him, A still full, Henry still holds the pork; again after a full game and agent restart (J2) (observed). The host's B is untouched by construction (the joiner's takes never leave his machine) (code-verified) |
| containers the game refills | every stash has a restock period (`nRestockPeriodDays`, most 7, 385 of 1,091 never); an entry older than its container's restock period is left to the engine (not applied); restock 0 never expires. Only 1 of 1,091 containers is master-linked (shared inventory); graves (`StashCorpse`) keep WO-113's own rule (observed; synthetic) |
| **rebrand** | **done for every user-facing surface** except the launcher screenshots (§5). Installer display name, apps-list entry, shortcuts, icon, file name, launcher window title / banner / logo / bottom bar, in-game messages, Discord hover text, README, tester page, runbook, release notes; the disclaimer on each. AppId, install folder, mod folder and ID, pak, `KCD2MP_` Lua names, `mp_` commands, `kcd2mp_` entities, log tags: unchanged (code-verified) |
| installer | `release\KingdomComeTogether-Setup-0.30.9.exe` built by `tools\Build-Installer.ps1`, every gate green (§7). Not installed from this shell (the AppData sandbox rule): the upgrade-in-place check is the maintainer's (§5) |

## 1. Phase 1 — how a drop reaches the other player today (WO-48, unchanged)

* **The message.** The mod's 750 ms item tick sees a new `PickableItem` within
  8 m of the player **and** that class's count in the player's inventory went
  down since the last tick (both halves required). It emits `item_drop <class>
  <amount> <health> <x> <y> <z> <entity>`. The agent mints a random 32-bit drop
  id and sends **ItemDropUp 0x32** `[dropId][class:16][amount][health][x][y][z]`
  (38 B), resends open drops every 30 s, and hands the id back to the mod
  (`KCD2MP_ItemDropRegistered`), which tracks the ground entity by name.
* **The other side.** The relay forwards it as **ItemDropDown 0x33** to every
  other client. The receiving mod holds it pending until its player is within
  70 m, then creates the item in an avatar's inventory and places it with the
  avatar's `human:PlaceItem` at a throwaway anchor: the engine mints a real,
  bound pickable at the drop's position; the mod adopts it and deletes the anchor.
* **A pickup.** A tracked drop's entity vanishing (within 80 m) without the mod
  having removed it = someone here took it → `item_claim` → **ItemClaimUp
  0x34**. The relay echoes **ItemClaimDown 0x35** to **every** client, the
  claimant included, in arrival order: the first echo settles the drop
  everywhere. The winner keeps it; a loser's pickup is rolled back by its
  recorded item id ("Too slow -- someone already took that"); a copy still on
  the ground is removed. The relay's TCP order is the whole arbiter.
* **Regression tests** (each: drop → appears → picked up → gone on both, both
  directions):
  * `tools/Test-WO134DropsSynthetic.lua` (new): the real `kdcmp.lua`, three
    session configurations (solo, the joiner of a shared world with the copy
    guard active, the host of a shared world); D1–D5 are WO-48's paths, D6
    checks the new hooks against a tracked drop. The pickup goes through
    `PickableItem.OnUsed`, the function the game's use action calls, so a
    wrapper that broke drops would fail it;
  * `tools/Test-ItemSyncRelay.ps1` (WO-48): the relay's broadcast, echo-to-all
    and race order;
  * live: `drops.py` (session scratch) as host with avatarpeer and as joiner
    with the synthetic host. D1 the peer drops an onion 2 m away → materialized
    → picked up through `PickableItem.OnUsed` → the claim reaches the peer →
    gone from the ground, Henry +1; D2 Henry drops (`PlaceItem`, the transition
    the UI drop makes) → the peer receives it → the peer claims → the ground copy
    here is removed, Henry −1.

| run | build | synthetic | relay | live host | live joiner |
|---|---|---|---|---|---|
| baseline | pre-WO-134 (`f29941c`+) | 52/52 | 11/11 | 9/9 (H0) | 9/9 (J0, own world) |
| after | WO-134 | 55/55 | — | 9/9 (H1), 9/9 (H2, final) | 9/9 (J1, host world), 9/9 (J2, final) |

The relay suite first failed its handshake: its Debug relay binary predated
two protocol bumps. Rebuilt, 11/11; not a code change.

Found, **not fixed** (the top rule): two same-class drops placed within 3 m in
the same tick both adopt one engine entity (seen when two test apples were
materialized at once). A WO-48 edge case, pre-existing (inconclusive how often
it happens in play: players drop one thing at a time).

## 2. Phase 2 — NPC bodies

### 2.1 What the engine allows (probes, observed)

| probe | result |
|---|---|
| a body's inventory | `e.inventory:GetInventoryTable()` + `ItemManager.GetItem` → class, amount, condition (24 items on a test soldier, its clothes included) |
| delete worn items from a corpse | `DeleteItem` → the body is stripped at once (sheet 3) |
| create + equip on a corpse | `CreateItem(class, condition, 1)` then `actor:EquipInventoryItem` → worn (sheet 4) |
| what it wears | the host's REST `SoulsByName/<body>/EquipmentManager/EquippedArmorsByClassId` (the read the agent already uses for appearance) |
| quality | follows the condition passed to `CreateItem` (three coats: 73 % / 56 % / 100 % → badges III / II / III) |
| the loot screen | `BasicAIActions.OnLoot` → `actor:RequestItemExchange(player.id)`; opened from Lua it is the game's own screen |
| timers while it is open | a 250 ms `Script.SetTimer` chain kept running (48 beats) — the loot screen does not freeze Lua |
| a change while it is open | a body item deleted from Lua vanished from the open screen (sheet 5) |
| `inventory:MoveItemOfClass` | returned nil and moved nothing (not used) |

### 2.2 As built

* **Joiner.** WO-131's loot wrapper now hands a host-owned body to
  `KCD2MP_W134LootRequest` (pickpocketing still gets the block). The mod asks
  (`w134_open` → **LootAsk BodyOpen**); the host's mod answers with the body's
  items in parts of 10 (`w134_bstate` → **LootHost BodyState**, worn items marked
  by the host's agent); the joiner's copy is set to that list with the least
  change (an item matching class + amount + condition within 0.01 stays, so a
  worn item stays worn; the rest deleted; the missing created and, if the host's
  body wears it, put on), then the game's loot screen opens on it.
* **Takes.** While a loot session runs (≤ 5 m, 10 min) the mod compares the body
  every 250 ms: an item gone from it went to Henry → **BodyTake** (optimistic:
  the item is already in Henry's pack, as the game's screen moved it); an item
  added → **BodyPut** (the host creates it in its body). The host's answer:
  `ok` (kept) or `gone` (taken back off Henry — the newest matching item — and
  "Someone already took that."). A host update that predates a pending take
  never puts the item back in the copy. Why optimistic and not per click: the
  loot screen is the game's own, it cannot be held until the host answers; the
  end state is the one the WO asks for (the host's body loses it, Henry gains it
  once, a stale take is undone).
* **Host.** `KCD2MP_W134HostTake` checks its body: enough pieces → deleted →
  `ok`; else `gone`. Every loop second, dead bodies within 25 m of the host or
  any avatar whose items changed go to every joiner as BodyState `update`
  (its own looting, a joiner's take). A copy not dead yet keeps the list and
  gets it at its death.
* Never duplicated: one body, the host's; every take is decided there (code-verified; synthetic B11–B13; observed H2).

### 2.3 Live

| check | result | mark |
|---|---|---|
| H1: avatarpeer asks for a dead body | 24 items in 3 parts, worn ones marked from the host's equipment | observed |
| H1: a take of 20 arrows | ok; the host body 20 → 0; the same take again: gone | observed |
| H1: the host loots a worn plate | the next update to the joiner lacks it | observed |
| found in H1 | the three parts arrived 3, 2, 1 and the joiner's reassembly reset on part 1. Fixed: parts in any order (Lua) and a body's parts sent one message at a time (agent); synthetic X1 | observed → fixed |
| J1: the host strips the body (its worn items) | the joiner's copy 22 → 16 items, nothing worn left; frames: clothed → stripped (sheet 1) | observed |
| J1: the joiner's loot | a request, the host's 16 items, the loot screen opened on them (sheet 2) | observed |
| J1: a take the host has | ok; Henry holds it once (0 → 1) | observed |
| J1: a take the host no longer has | gone; taken back off Henry within the same second | observed |
| H2: two takes of one item in the same frame | ok, then gone | observed |

The takes in these runs were made by script (the item moved from the body to
Henry, the end state of the game's transfer), not by a player's click (no input).

## 3. Phase 3 — loose world items

* **Joiner.** `PickableItem.OnUsed` and `OnUsedHold` (the functions the game's
  use and steal actions call; looked up fresh each time the player aims) are
  wrapped. A world item is not picked up: `w134_item` → **LootAsk ItemTake**
  (class, position, fell-off-a-corpse flag). The answer: `ok` → the game's own
  pickup runs; `gone` → the item is taken away here, "Someone already took
  that."; `unknown` → the game's own pickup (per machine, as before); no answer
  in 4 s → "The host didn't answer -- try again." and nothing happens.
* **Host.** Matches the nearest world item of that class within 0.35 m (2.5 m
  for an item that belongs to a dead body); found → taken away (through the
  asker's avatar), `ok`, and every other joiner gets ItemGone; else `gone` if it
  was taken this session, else `unknown`. Its own pickup (the wrapper, confirmed
  on the next pass) → **ItemGone** to every joiner.
* **Taking away** (`W134.takeAway`): the item goes into an avatar's inventory
  (a mod entity, never saved) and is deleted there; `RemoveEntity` only when
  there is no avatar. Reason: §0, item slots.

| check | result | mark |
|---|---|---|
| H1: the joiner asks for a bread on a table | ok, matched 0.000 m; asked again 3 s later: ok again — the slot had respawned it (the finding) | observed |
| the respawn: `RemoveEntity` | the same bread back at 0.000 m within 3 s | observed |
| an avatar's `item:OnUsed` | refused (false) | observed |
| into the avatar's inventory, deleted | gone, no respawn after 25 s | observed |
| H2 (fixed): two asks in the same frame | ok, then gone; nothing at the spot 25 s later | observed |
| H1: the host picks up an unowned item | ItemGone reached the joiner | observed |
| J1: the bread the host took in H1 | absent from the joiner's world after the join (one world, through the host's save) | observed |
| J1: the host's pickup of a soup bowl (5 on a table) | 5 → 4 on the joiner, still 4 after 10 s | observed |
| J1: a stale ask (the steal path) | gone in 80 ms, removed (4 → 3), not picked up, no crime | observed |
| J1: ok | picked up by the game's own pickup | observed |
| J1: unknown (0.38 m off) | picked up here, per machine | observed |
| J1: an item 0.1 m from one already taken | gone (the match radius) | observed |

Not guessed: an item the host cannot match (moved, spawned on one machine only,
fell elsewhere) stays per machine — the host keeps its copy. Items NPCs carry
and put down move between loads (0.5–17 m) and fall in that class (observed).

## 4. Phase 4 — chests

* **Recording.** Every container of class `Stash`, `CartStash`, `Nest` or
  `DestroStash` within 3 m of the player is snapshotted on arrival; any change
  while in reach is the player's: a take (+n) or a put (−n), with the world time
  and the container's restock period → `w134_chest`. Never a grave
  (`StashCorpse`) or a mod entity. Only in a shared world (the host with or
  without a peer; the joiner only while in the host's world).
* **The host's ledger** (`<data>/chests/<world>/current.json`): every host
  world save stores a copy paired with the save's footer MD5 (`pair-<md5>.json`,
  the newest 100 kept); loading a save brings its paired ledger back; a save
  with no pair keeps the running ledger (logged). Takes before the world is
  identified are held and added at the identify. Sent to the joiner at its Ready
  (**LootHost Ledger**, parts within the wire's text limit).
* **The joiner's ledger**: live in memory while joined; stored beside every
  Henry snapshot (`chests-<snapshot>.json`, pruned with it); a restore brings
  the ledger of the same snapshot back — loss, never duplication, exactly like
  the Henry. A first join starts empty.
* **After a join**: rows = the host's entries (+n: put back) + the joiner's
  (−n: taken out); the mod applies each (`KCD2MP_W134ChestApply`), leaving an
  entry past its restock period to the engine; the applied chests are
  re-snapshotted so the apply is never recorded as the joiner's.
* **Found live and fixed (J1):** a host reload with the joiner beside a chest:
  the restarted loop compared its old snapshot with the reloaded chest and
  recorded a take and a put nobody made (the restore replaced that live ledger,
  so nothing was stored). Now the loop forgets every snapshot when it restarts
  after a load, and the agent ignores chest events while a join or reload
  loads (synthetic X3).

| check | result | mark |
|---|---|---|
| H1: the host takes a cap from chest A | `WO134-CHEST take`, ledger 1 entry, `current.json` written | observed |
| H1: host world saves | each paired (`pair-<md5>.json`), the last with the take (1 entry) | observed |
| H1: WO-123's synthetic joiner reaches Ready | the host sent its ledger: `1 1 stash[Chest/chest3_…]|<cap>|1|0.2397|760953|7` | observed |
| J1: first join (Bring) | the host's ledger arrived after Ready; `apply rows=1 applied=1`; chest A holds the cap again at 0.2397 | observed |
| J1: the joiner takes the pork from chest B | recorded, ledger 1 | observed |
| J1: host save | Henry snapshot paired; its chest file holds the take; the join save's file is empty | observed |
| J1: host reload → rejoin | joiner ledger 1 entry from the restored snapshot, host ledger 1; `applied=2`; A: veil + cap; B lacks the pork; Henry still has the pork | observed |
| J2: game and agent restarted, rejoin (restore, no question) | the same: joiner ledger from the restored snapshot, `applied=2`, the same chest | observed |
| restock expiry, a missing container, the grave rule, a put | C4–C7 | synthetic |

Merchants: their trading is out of scope; their storage chests are ordinary
stashes with a restock period and follow the same rule. The engine's own
restock of a put-back item may add a second piece in the joiner's copy after the
period (by design chests may differ per player) (inconclusive). Host takes made
before WO-134 (or with no agent running) are unknown to the ledger: those chests
arrive as the host left them.

## 5. Phase 6a — the rebrand

| surface | now |
|---|---|
| launcher window title, page title | **Kingdom Come: Together** |
| launcher title and background | the title above the server list is the text **Kingdom Come: Together** (the site's own gold Cinzel); the page background is the maintainer's `Banner3_Filter-background.png` at 1920 × 1080 (`wwwroot/img/background.jpg`, 459 KB, under the site's existing dark overlay), replacing `kcd2_bg.jpg`. (A first version used a strip of the banner as a header image; replaced at the maintainer's request) |
| launcher bottom bar | the square logo (128 px copy) + "Kingdom Come: Together v <version>"; the footer adds "Unofficial. Not affiliated with or endorsed by Warhorse Studios." |
| icon (`KCDMP_launcher/app.ico`) | 16, 24, 32, 48, 64, 128, 256. **16, 24 and 32 px are the "KC" letters alone** (a square crop takes in the plaque's top, unreadable there): the white letters (they span x 14–86 %, y 17–61 % of the logo) at 84 % of the width, centred on a blurred, darkened fill of the same crop. The first version ran them edge to edge and the taskbar clipped them (maintainer's report). 48 px and up are the whole logo. Built by `tools/Build-Branding.py` (sheet 6) |
| installer | the shortcuts name `{app}\app.ico` directly and Setup refreshes Explorer's icons at the end (`ChangesAssociations`): the exe keeps its path, so Windows' icon cache showed the old logo on an upgraded shortcut (maintainer's report). Display name and apps-list entry **Kingdom Come: Together**; publisher "Kingdom Come: Together contributors"; its own icon; Start menu folder and shortcuts **Kingdom Come Together** (a colon cannot be in a file name); `UsePreviousGroup=no` and `[InstallDelete]` of the old "KCD2 Multiplayer" shortcuts, so an upgrade shows one name; the disclaimer on the welcome and finish pages and in the shortcuts' comment; file `KingdomComeTogether-Setup-<version>.exe` (`Build-Installer.ps1`, `Test-Installer.ps1`, `Test-InstallerUpgrade.ps1`, which also finds an older `KCDMP-Setup` as the previous build) |
| in game | the mod's six on-screen warnings that named it ("KCD2-MP: …" → "Kingdom Come: Together -- …") |
| Discord | the hover text; the app name and art key are the maintainer's (unchanged key) |
| docs | README (the banner, the disclaimer, the renamed repo links, a "Loot together" row), `docs/TEST-0.30.9.md`, the runbook, `docs/INSTALLER-TESTING.md`, `docs/LAUNCHING.md`, the release notes; older release notes keep their names (history) |
| art | originals only in `docs/branding/` (where the maintainer committed them); the build carries the sized copies |
| never changed | AppId `{88C5B9F1-…}`, `{localappdata}\KCDMP`, `HKCU\Software\KCDMP`, the `kcdmp` folder and mod ID, `kdcmp.pak`, `KCD2MP_` names, `mp_` commands, `kcd2mp_` entities, log tags (the launcher's log line keeps "KCD2 MP Launcher") |

**Not done here, for the maintainer:** installing over an existing install (one
apps-list entry, the new name, settings kept) — installers are never run from
this shell (the AppData sandbox rule); and the launcher window screenshots —
the launcher activates its window when it starts, which the no-focus rule
forbids. One capture attempt did take the foreground for about 12 s (disclosed
in the progress file); it showed a bare build folder's version-mismatch box over
the banner and is not used.

## 6. Runs

| run | the real game | build | what |
|---|---|---|---|
| probes | solo + DLL | pre-WO-134 | pickable names and positions across a reload, stashes, a test body (inventory, strip, create + equip, loot screen, timers) |
| H0 / J0 | host / joiner (own world) | pre-WO-134 | the drop baseline |
| H1 | host (avatarpeer; WO-123 synthetic joiner) | WO-134 | drops; body answer, takes, host loot; world item asks (the respawn found); host pickup; chest ledger; ledger at Ready |
| J1 | joiner (Host125, first join, then a rejoin) | WO-134 + fixes | ledger apply; drops; body strip, loot screen, takes; loose items (gone, ok, unknown, host's pickup); chest take, snapshot, rejoin |
| H2 | host (avatarpeer) | final | drops; same-frame races; the slot item stays gone |
| J2 | joiner (Host125, restore join after a restart) | final | ledgers across a restart; drops |

## 7. Gates

Every `tools\Build-Installer.ps1` gate green, then the installer (the numbers are
in `docs/WO-134-progress.md`). New: `Wo134Tests.cs` (15: the wire, the checks, the
ledgers, the stores, the console-length limits), the relay round-trip test for
the loot types, `Test-WO134DropsSynthetic` (55), `Test-WO134Synthetic` (60). One
older test (`Wo123Tests.Join_wire_table_is_consistent`) had the join channel's
range hard-coded to 0x5B; widened to 0x5F. `Verify-Install.ps1` knows the WO-134
markers (agent `KCD2MP_W134BodyState`, `MP-WO134-STATS`; protocol `loot-host`;
launcher "Kingdom Come: Together"; pak `KCD2MP_W134LootRequest`,
`KCD2MP_W134HostItem`, `KCD2MP_W134ChestApply`, `W134.takeAway`).

## 8. Wire

`LootAsk` 0x5C/0x5D (joiner → host) and `LootHost` 0x5E/0x5F (host → one
joiner) on WO-123's join channel: `[target][joinId=0][kind][tok:4][ASCII text ≤
1400]`, every field checked on both ends (`Wo134Rules`). The relay routes them by
the JoinWire rows (no relay code). No protocol bump (still v10): the release
check already refuses mixed builds (`ProtocolWo134.cs`).

## 9. The combined two-player checklist (132 + 133 + 134)

`docs/TEST-0.30.9.md` section 4 — **A** looting together (drops both ways
first, then bodies, loose items, chests across a rejoin), **B** WO-132's nine
fight items, **C** WO-133's three; each item says what each screen should show.

## 10. Carried forward

1. Two real players: every WO-134 item (tester page A), the launcher screenshots
   and the upgrade-in-place install check.
2. The WO-48 edge case in §1 (two same-class drops in one tick within 3 m).
3. Crime stays per machine (a joiner's steal is his own crime; out of scope).
4. Items that fall off a corpse match within 2.5 m; a corpse's weapon that
   landed elsewhere on each machine stays per machine (inconclusive how often).
5. A stand-in copy (WO-131, a road encounter) wears an approximate soul: a loot
   request sets its items to the host's, created ones put on — its look follows
   the host's list (synthetic; no live road encounter).
6. Restock of a put-back chest item after the period: possibly a second piece in
   the joiner's copy (inconclusive).
