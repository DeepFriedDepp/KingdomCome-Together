# WO-148 — Attribution, the port-aware setup, and carrying on the other screen: findings

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

**Version: 0.42.7** (the maintainer's number). Status: **Stage A done** (no game,
no window); **Stage B (the live runs) waits for the maintainer's go-ahead.**
0.42.5 is bookmarked as the tag `v0.42.5` on `7dcd01a` (the commit its installer
was built from), pushed before any code.

Evidence marks: **(observed)** seen live in the game; **(synthetic)** the real
game against a scripted partner or a synthetic host, or a console stand-in for a
key; **(code-verified)** read in the code or the game's data, or pinned by a unit
test, not run live; **(pending)** the live run is planned for Stage B.

## The answer

1. **Who made this is now unmistakable.** `AUTHORS` gives the lineage in order —
   the original project `marczukmichal/kcd2-multiplayer` (its author and its
   contributors keep their copyright on their remaining code), then this
   project, `DeepFriedDepp/KingdomCome-Together`, maintained by DeepFriedDepp.
   `NOTICE` carries the copyright, the separation from Warhorse and PLAION, and
   GPLv3 §7 additional terms (b) and (c) for this project's own material only.
   551 of the project's own source files carry a three-line header (four where
   the file has upstream history: 44 files); third-party files are untouched.
   The "official repository" line is at the top of the README, in the new
   release-notes template, in a new About box in the launcher and in
   `docs/DISCORD-TEXT.md`. **(code-verified)**
2. **The audit found Warhorse material in the tree and in every past release.**
   Removed from the tree: the mod's full copies of the game's
   `Libs/Config/defaultProfile.xml` and `keybindSuperactions.xml` (loose and
   inside the tracked `kdcmp.pak`) — the dice keys still work: only our lines
   ship, and Setup and the launcher merge them into the player's own copies
   (`kdcmp_keys.pak`) — and the 626 main-quest objective texts and 32 quest
   titles the agent embedded (now read at run time from the player's own
   localisation). **Kept, at the maintainer's decision, and recorded:** the
   launcher's 35 UI images (game UI textures) and the branding built on the
   official logo and key art. The past releases (all six) carried the config
   copies, the official KCD2 key art as the launcher background and the UI
   textures; 0.18.2's release page also carries a game save
   (`autosave018.whs`). Nothing was deleted or edited on GitHub (section 2).
3. **Every native log line carries its thread id** in one fixed column
   (`[HH:MM:SS.mmm] tNNNNNN text`), and **an inline hook is refused unless its
   patch ends on an instruction boundary** of the exact image, with no relative
   branch and no RIP-relative operand inside. The decoder agrees with capstone
   on all 4,102 decodable test vectors; all seven of the shipped DLL's hook
   prologues pass offline. **(code-verified; that they all arm live: pending)**
4. **Carrying shows on the other screen** — built, unit- and suite-tested, the
   live proof pending. A body: the carrier's avatar picks up this machine's copy
   with the game's own pick-up (`RequestGrabCorpse`), carries it while the stream
   moves it, and sets it down with the game's own call; the body then lands where
   the carrier's game left it, or back where it was picked up when that would be
   in the air or under the ground. The host's world decides who carries; the
   loser's game is put back. Nothing alive is ever moved. A sack: the avatar
   holds the game's own NPC sack and a dropped one lies where it landed (shown
   only). **(synthetic in the Lua suite, code-verified; live: pending)**

| item | status | evidence |
|---|---|---|
| 0 the bookmark `v0.42.5` | done | (observed) `git ls-remote --tags origin v0.42.5` → `7dcd01a…` |
| 1.1 AUTHORS / NOTICE | done | the files |
| 1.2 one header per own source file | done | 551 files; `tools` script in the scratchpad; third-party none in the tree |
| 1.3 "official repository" line | done | README top, `docs/releases/RELEASE-NOTES-TEMPLATE.md`, launcher About box, `docs/DISCORD-TEXT.md` |
| 1.4 GPLv3 §7 (b) (c) for our material only | done | `NOTICE` |
| 1.5 the separation from Warhorse and PLAION next to every copyright line | done | AUTHORS, NOTICE, README, the template, every file header, the About box |
| 1.6 the audit | done; fixes partly (section 2) | the scans in section 2 |
| 2.1 thread id on every native log line | done | (code-verified) `log.h`, a unit test pins the column |
| 2.2 the hook boundary check | done | (code-verified) 4,102 capstone vectors, 7/7 hook prologues; (pending) all arm live |
| 3.1 the carry census | done | `docs/WO-148A-carry-census.md` |
| 3.2 carrying on the other screen | built | (synthetic: the Lua suite, 85 checks; code-verified: 24 agent tests, a relay round trip); (pending) the live runs 3.4 |
| 3.3 quest reactions to a carried body | logged | (code-verified) `MP-CARRY quest-reaction`; (pending) a live burial |

## 1. Attribution

* **AUTHORS** — the lineage in order, the official repository, and the
  separation. Other repositories with the same name (forks of early versions) are
  mentioned generically, not by name: only the repository above is this project.
  Individual contributors other than the two names the lineage needs are not
  listed; "every contributor is recorded in the git history".
* **NOTICE** — the copyright line (`Copyright (C) 2026 the Kingdom Come: Together
  contributors`), "portions" for the original project, the separation, the GPL's
  warranty disclaimer, and the §7 additional terms: (b) preserve the author
  attributions and the "official repository" notice; (c) do not misrepresent the
  origin; a modified version must say it is modified and not the official one. The
  terms state plainly that they cover only material added by this project, not
  the original author's code.
* **File headers** — three lines: the copyright line with the SPDX identifier
  `GPL-3.0-only`, a pointer to the §7 terms in NOTICE (GPLv3 §7 requires such a
  notice in the files it covers), and the separation in one sentence. Files whose
  history reaches the original project (44: the launcher's Blazor shell, the
  relay's first services, the agent's `GameBridge.cs`, `Program.cs` and
  `VoiceChat.cs`, `kdcmp.lua`, the clothing-preset table) get a fourth line:
  "Portions from the original project, marczukmichal/kcd2-multiplayer; its author
  keeps their copyright (AUTHORS)." Styles: `//` (C#, C++, Java, the `[Code]`-included
  `SteamDetect.iss`), `;` (Inno), `--` (Lua), `#` (PowerShell, Python, CMake),
  `@rem` (cmd), Razor and XML comments; inserted after a BOM, a shebang or an XML
  declaration; line endings and encodings kept. Not touched: JSON, CSS, HTML,
  generated files, third-party files (none are vendored in the tree).
* **The launcher's About box** (a new ABOUT button beside REPORT BUG): the
  official repository with a button that opens it, the lineage, the licence and
  its two §7 terms in one sentence, and the separation. The rest of the launcher
  is unchanged (the maintainer's instruction during the WO: "keep the launcher the
  way it is").

## 2. The audit

Method: a hash database of every entry of the game's `Scripts.pak`,
`IPL_GameData.pak`, `Tables.pak` and `Localization/*.pak` (git-blob SHA-1 and a
normalised-text hash), compared with the current tree, every blob in the whole
history (3,412 blobs in 711 commits, plus 499 entries inside 130 zip/pak blobs),
and every tracked image against the game's UI textures (whole images and crops);
docs scanned for runs of game lines and game text; the GitHub releases read
through the public API (metadata; no asset was downloaded) and each release's
tree read from its tag. The scanner lives in the session scratchpad.

### 2.1 The current tree (HEAD `22b5a90` before this WO)

| what | Warhorse | what this WO did |
|---|---|---|
| `kdcmp/Data/Libs/Config/defaultProfile.xml` | yes: every one of the game's 1,586 lines plus our 18 | **removed**; our lines are `kdcmp/ConfigPatch/defaultProfile.interaction.xml` |
| `kdcmp/Data/Libs/Config/keybindSuperactions.xml` | yes: the game's 817 lines plus our 74 | **removed**; our lines are `kdcmp/ConfigPatch/keybindSuperactions.append.xml` |
| `kdcmp/Data/kdcmp.pak` (tracked, shipped) | yes: 2 of its 5 entries are the copies above | **rebuilt** with 3 entries (the Lua and our two table patches) |
| `dotnet/KcdMp.Client/mainquest-objectives.json` (embedded in the agent, shipped) | yes: 626 objective texts and 32 quest titles verbatim | **regenerated with keys only** (same registry id `b6b917b72323`); titles read at run time from the player's own `Localization/English_xml.pak` |
| `docs/WO-96-mainquest-objectives.csv` | yes: the English and Czech texts | **regenerated** without the two text columns |
| `KCDMP_launcher/wwwroot/img/ui/*.png` (35, shipped) | yes: 17 are the game's own UI textures (`Libs/UI/Textures/Apse`), 18 are exact crops of its atlases; 12 are used | **kept** (the maintainer's decision); recorded here |
| branding: `docs/branding/Banner3_Filter-background.png` | yes: built on the KCD1 key art | kept (the maintainer's decision); recorded |
| branding: `Banner_Filter.png`, `banner-1280.jpg`, `KCDMP_launcher/wwwroot/img/background.jpg` (shipped), `KCT_txt*.png`, `Logo_Filter.png`, `logo-128.png` (shipped), `app.ico` (shipped), `kcd2-mp-logo.png` = `npp_kcd_mp_color.png` | likely / partly: the "Kingdom Come" wordmark uses the official logo's letterforms and sun rays; the riders' illustration is in the official key-art style (its provenance could not be checked offline) | kept (the maintainer's decision); recorded |
| `docs/WO-94-mainquest-registry.csv`, `kdcmp.lua`, a few tests | traces: 31 and 17 short quest titles as names | kept (no long text) |
| the 105 docs screenshots | captures of the game, not game files | kept |
| everything else asked about (`tools/dice-probe-*.xml`, the soul-identity CSVs, the probe results, the run logs, the test plans, the dependency CSV, the CryEngine reference) | no | kept |

No byte-identical game file, no extracted pak, no game Lua and no game save is
in the tree. Docs quote at most four consecutive lines of game XML or Lua and a
few phrases; no dialogue, quest or book text. The CryEngine reference has no
verbatim line of the CRYENGINE 5.7.1 source.

### 2.2 History only (in past commits; still on `origin`)

* `item_dump.xml`, `clothing_preset_dump.xml`, `InventoryPreset_dump.xml` — the
  game's own item, clothing-preset and inventory-preset tables, committed by the
  original project on 2026-02-25 and removed on 2026-02-28; reachable from every
  branch and tag.
* `KCDMP_launcher/wwwroot/img/kcd2_bg.jpg` — the official KCD2 key art with the
  official logo, the launcher's background from the original launcher
  (2026-03-15) until WO-134 (2026-09-27).
* an older `background.jpg` (the KCD1 key art with our wordmark) and an older
  banner — both 2026-09-27.
* 100 older builds of `kdcmp.pak` with the two config copies (from WO-6,
  2026-07-30), and an older loose `keybindSuperactions.xml`.

A new commit cannot take these off GitHub. Purging them needs a history rewrite
(for example `git filter-repo --invert-paths` for the three dumps and
`kcd2_bg.jpg`, a blob replacement for the paks and images), a force-push of every
branch and tag, and a request to GitHub to drop cached views; forks and clones
keep copies. **Not done: the maintainer's decision.**

### 2.3 The past GitHub releases (read through the API)

Six releases, all by the maintainer's account: `v0.8.0`, `0.8.5`, `0.9.2`,
`0.10.0`, `0.11.5`, `0.18.2`.

| release | assets | Warhorse material in them |
|---|---|---|
| 0.18.2 | `KCDMP-Setup-0.18.2.exe`, **`autosave018.whs`** | **a game save** (1,407,119 bytes, the "save file to test" the release notes offer); the installer: the config copies in `kdcmp.pak`, `kcd2_bg.jpg`, the UI textures |
| 0.11.5 | `KCDMP-Setup-0.11.5.exe`, `KCDMP-DirectInstall-0.11.5.zip` | the same three in both |
| 0.10.0 | Setup, DirectInstall zip | the same three |
| 0.9.2 | Setup, DirectInstall zip | the same three |
| 0.8.5 | Setup, `KCDMP-Update-0.8.5-Beta.zip` | the same three (the update zip: the pak) |
| v0.8.0 | Setup | the same three |

From each release's tag (code-verified; the assets themselves were not
downloaded): `kdcmp/Data/kdcmp.pak` holds `Libs/Config/keybindSuperactions.xml`
and `defaultProfile.xml` in all six; the launcher's `wwwroot/img` holds
`kcd2_bg.jpg` and 36 UI images in all six. The release notes quote no game text;
the 0.18.2 notes thank nine pre-release testers by their handles (left as the
maintainer wrote them). **What would need removing, for the maintainer to
decide:** the 0.18.2 save `autosave018.whs`; and every installer and zip of the
six releases (or the releases themselves), since all carry the config copies,
the official key art and the UI textures. Nothing on GitHub was deleted or
edited.

### 2.4 Privacy (found by the same scan; counts only)

Not Warhorse material, and not changed by this WO (the maintainer's call):
the WO-38 and WO-40 tester logs under `docs/` hold two testers' Windows profile
names, two Steam names and one public server address; LAN addresses appear in
31 files; the build machine's user name appears in 30 files (mostly profile
paths in older docs); one doc names a machine; one doc uses a real-looking
public address as a port-forward example.

## 3. The port-aware setup

### 3.1 The thread id on every native log line

`log.h` writes `[HH:MM:SS.mmm] tNNNNNN text`: the writing thread's id, six digits,
zero-padded, right after the time, in both the DLL's log and its mirror beside
`kcd.log`; the text starts at column 23 (`kLogPrefixLen`). Every parser of these
lines in the repo searches for a substring (the launcher's injection check, the
live tools, the E2E scripts), so none needed a change; the native unit test pins
the column. (code-verified)

### 3.2 The patch-boundary check

`native/KCDMP/x64_len.h` decodes x64 instruction lengths (the one-byte map, 0F,
0F38, 0F3A, VEX; EVEX, XOP and 3DNow! refused), flagging RIP-relative operands
(also `[eip+disp]`), relative branches and every other control transfer.
`inline_hook.cpp` reads the live prologue of the exact image, decodes it, and
refuses the hook — logged with where the instructions end — unless the patch
length ends exactly on a boundary with no RIP-relative operand, branch, call or
return inside. The seven hooks' expected bytes moved into one header,
`hook_prologues.h`, which the hooks and the tests both read.

* Against capstone 5.0.7 on 6,000 generated vectors (random instruction shapes,
  not taken from any game binary; `tools/wo148/gen_x64_vectors.py`): all 4,102
  decodable ones agree (length, RIP-relative, branch); none refused that capstone
  decodes. **(code-verified)**
* All seven hook prologues pass (lengths 18, 14, 20, 15, 16, 16, 24, on capstone's
  boundaries in the game's DLL files: EntityModule, CrySystem, DialogModule,
  CryAction, GUIModule, PlayerModule, XGenAIModule). WO-146B's crash case (a
  length that cuts an instruction) is refused; so are a RIP-relative operand, a
  relative call, a short jump, a `ret`, an already-patched entry and EVEX.
  **(code-verified; live: pending)**

## 4. Carrying on the other screen

### 4.1 The census (3.1)

`docs/WO-148A-carry-census.md`: three systems — a body on the shoulder (dead,
unconscious, or a quest's living NPC), the stealth hold that leads into it, and
carry-items (sacks, jugs, piles) — plus the quest carries and burials built on
them. **There is no throw** for a body or an item in the game; "drop" is the
set-down. The body fragments are not player-only, so an NPC (the avatar) can play
them; sacks on NPCs are hand content.

### 4.2 What is built

* **This player's carry** (the mod, 5 Hz): the game's grab-body interaction is
  wrapped to name the body; `IsCarryingCorpse` tells start and end; the stealth
  route is found by the game's `carriedBody` link (or the nearest dead or
  unconscious body); the set-down is read after the put-down animation settles
  (1.6 s), with where the body came to rest. Every 2 s the game confirms the
  carry to the agent.
* **The wire**: one new join-channel message, Carry 0x70/0x71 (Grab, Held, Put,
  Refuse), text-shaped like WO-140's sleep vote; no protocol bump (a mixed
  release is refused at the relay anyway).
* **One carrier at a time** (the agent's ledger): a joiner's grab goes to the
  host; the host refuses a body its world has given to someone else (the host's
  own player, another joiner), shows the rest on the joiner's avatar and forwards
  them to the other joiners. On a joiner every grab that arrives is the host's
  word, and its own carry of the same body loses: put down, and back where it was
  picked up — unless the winner's figure has it here by then (the put-back would
  move a carried body). A carrier who leaves, or whose Held stops for 10 s, sets it down;
  a carry the carrier's own game stops confirming for 2 minutes is set down
  elsewhere and picked up again on the next confirmation.
* **Shown here**: the carrier's avatar runs the game's own `RequestGrabCorpse` on
  this machine's copy and `RequestPutCorpse` at the set-down. A copy far from the
  avatar is fetched only from within 30 m of where the carrier picked it up, and
  only to a spot a body can lie on. While carried (and 5 s after) the host's NPC
  stream, a resync, the puppet tick, the silence release and the copy guard leave
  the body alone. A Held for an avatar that dropped it on the way picks it up
  again (at most three times).
* **The landing** (`Wo148Rules`, mirrored in the mod): the carrier's own resting
  spot wins when a body can lie there and the body is more than 0.5 m from it; a
  body that would stay in the air (more than 0.8 m above the first surface under
  it) or under the ground (more than 0.6 m below it) goes back where it was
  picked up; logged as `MP-CARRY land ...`.
* **Never alive**: a grab of a body that is alive here is refused (`alive`), and a
  body that woke on the way is never moved.
* **Sacks**: the pile and ground pick-up and deposit callbacks are wrapped, the
  `put_item` key marks a drop; the avatar holds `sack_miller` (the DLL's WO-143
  hand content) with the game's `CarryItemPickup` / `CarryItemPlace` one-shots; a
  dropped sack is a prop (the game's sack model, no physics, never saved) where it
  landed, removed when the carrier picks it up again. Nothing of the other world's
  piles changes (the quest side).
* **Switches**: `mp_carry_sync` (bodies and the whole layer) and
  `mp_carry_objects` (sacks), both on in this build pending the live proof (the
  work order: "default on only when proven" — a part not proven in Stage B goes
  off); `mp_carry_test grab <body> | put | status` (the console stand-ins);
  `mp_carry_status`; checklist markers `mark_carry`, `mark_carry_host`,
  `mark_carry_both`, `mark_sack`, `mark_bury`, `mark_dice_keys`.
* **Quest reactions (3.3)**: on the host, a quest change made by its own world
  while a partner carries a body, or within 30 s of a set-down, is logged as
  `MP-CARRY quest-reaction ...`; WO-147's quest safety still guards destructive
  steps. Quests counting the joiner's carry and crime for a carried body are the
  next phase (not built).

### 4.3 The kinds, shown and not shown

| kind | shown | evidence |
|---|---|---|
| a dead body: pick-up, carry walk, set-down | built | (synthetic: suite (a)–(f)); (pending) H-runs and J-runs |
| an unconscious body | built (the same path) | (synthetic); (pending) |
| a quest's living NPC (`CarryLivingActor`) | not shown: nothing alive is moved | (code-verified) |
| the stealth hold, then "pick up body" | built as a body carry (the link route) | (synthetic: suite (h)); (pending) |
| a sack from a pile, a deposit, a drop | built | (synthetic: suite (g)); (pending: needs a save in a sack task) |
| a throw | none exists in the game | (code-verified: the census) |
| the burial's "bury" step | not shown: the quest's | (code-verified) |

## 5. Decisions made unattended (Stage A)

* **The dice keys without the game's files**: the mod ships only our lines; Setup
  (after a verified install), the launcher (before every game start) and the dev
  script merge them into the player's own copies, read from the game's Data paks,
  into `Mods\kdcmp\Data\kdcmp_keys.pak` (the game opens every pak in a mod
  folder: the engine's `[Mod] Opening paks in %s` with `/*.pak`). Built from this
  machine's game, the result is byte-identical to the copies the mod used to ship
  except one trailing tab the game's own file has. A failure costs the keys only
  (the console commands still work) and never fails Setup; `Verify-Install.ps1`
  reports a missing pak.
* **The licence identifier** is `GPL-3.0-only`: the README says "GNU General
  Public License v3.0", not "or later".
* **Quest titles**: the registry carries keys; the agent reads the English titles
  from the player's own localisation pak (no pak: internal names, as before).
* **Not changed after the maintainer's instruction** ("keep the launcher the way it
  is"): the launcher's UI textures, its background and logo, the branding images,
  the launcher's footer text, its Report Bug link; the mod's manifest; Setup's
  texts; the payload's file set. They are recorded above for the maintainer.
* **No history rewrite, no release edits, no log scrubbing**: recorded for the
  maintainer.
* **The carry layer's defaults** are on until Stage B says otherwise.

## 6. Stage B

Planned live runs (one machine, throwaway save copies, the Modding Tools build,
the game started minimized and never brought to the front, no key pressed): see
`docs/WO-148-progress.md`, "Stage B plan".
