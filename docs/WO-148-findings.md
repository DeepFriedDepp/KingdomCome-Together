# WO-148 — Attribution, the port-aware setup, and carrying on the other screen: findings

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

**Version: 0.42.7** (the maintainer's number). Status: **done** — Stage A (no
game, no window) and, after the maintainer's go-ahead, Stage B (live, solo, on
throwaway save copies of the Modding Tools build). 0.42.5 is bookmarked as the
tag `v0.42.5` on `7dcd01a` (the commit its installer was built from), pushed
before any code. **After the WO:** the frame-rate collapse seen in Stage B was
found and fixed, **0.42.8** (section 7).

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
   prologues pass, and **all seven armed on the live game** (six at start, the
   NPC trace's render hook on its first use). **(code-verified; observed)**
4. **Carrying shows on the other screen, both ways** — proven live, solo (the real
   game as the host with a scripted partner, and as the joiner of a synthetic
   host; console stand-ins for the keys). A body, dead or knocked out: the
   carrier's figure picks up this machine's copy with the game's own pick-up,
   carries it on its shoulder while it walks, and sets it down with the game's
   own call; the body then lies where the carrier's game left it. The host's
   world decides who carries: the host's own player's body is refused to a
   partner, a joiner's carry the host refuses is put back, and in a race the
   joiner's game puts its carry down while the host's figure takes it. A partner
   who leaves mid-carry leaves the body lying where the figure stood. Nothing
   alive is ever moved. A sack: the carrier's figure holds the game's sack in its
   right hand and walks with it; a dropped sack lies where it fell (shown only).
   The live runs found six defects, and preparing one found a seventh; each is
   fixed and covered by a test, and five reran live (section 4.4).
   **(observed; frames in `docs/wo148-shots/`)**
5. **The frame-rate collapse was the mod's, and 0.42.8 fixes it** (after the WO).
   WO-147's stamina reading in the DLL handed the game a value that no longer
   existed, for every soul near the player after the first. The game faulted on
   it after pushing onto one of its own stat stacks, the DLL's fault guard hid the
   fault, and the stack grew for good: every stat read in the game then walked all
   of it (4,578 entries: about 4.5 FPS). It is in 0.42.5 and 0.42.7 alike, and any
   two or more souls within 15 m trigger it, not the carry. One declaration moved
   fixes it: in a crowd and in a fight on 0.42.8 the stack stays empty and the
   frame rate holds. **(observed; code-verified; section 7)**

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
| 2.2 the hook boundary check | done | (code-verified) 4,102 capstone vectors, 7/7 hook prologues; (observed) all seven armed live |
| 3.1 the carry census | done | `docs/WO-148A-carry-census.md`; five of its seven open questions answered live, one in part (section 4.4) |
| 3.2 carrying on the other screen | done | (observed) host and joiner runs H1, H2, J1–J3; (synthetic) the Lua suite, 112 checks; (code-verified) 26 agent test methods, two relay round-trip tests |
| 3.3 quest reactions to a carried body | logged | (code-verified) `MP-CARRY quest-reaction`; no save of this machine reaches a burial step (section 4.4) |
| after the WO: the frame rate | found; fixed in 0.42.8 | (observed) the game's stat stack read live: 4,578 entries on 0.42.7, 0 on 0.42.8 in a crowd and in a fight; (code-verified) the DLL's compiled code in 0.42.5, 0.42.7 and 0.42.8 (section 7) |

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
  **(code-verified)**
* Live, on the Modding Tools game (every session): the motion tags
  (EntityModule+0x94750), the dialogue gate (DialogModule+0x98290), the pause gate
  (CryAction+0x86EC0), the trespass listener (GUIModule+0x27E4C0), the sleep gate
  (PlayerModule+0x4C5BF0) and the NPC-state request hook (XGenAIModule) armed at
  start; the NPC trace's render hook (CrySystem+0x20BDD0) armed on the first
  `mp_npc_trace`. None refused; every line carries the thread id column.
  **(observed)**

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
  move a carried body); its own Held arriving after the loss is not a new grab, and
  its running put-down starts no new carry. A carrier who leaves, or whose Held stops for 10 s, sets it down;
  a carry the carrier's own game stops confirming for 2 minutes is set down
  elsewhere and picked up again on the next confirmation.
* **Shown here**: the carrier's avatar runs the game's own `RequestGrabCorpse` on
  this machine's copy — only once the game's `CanGrabCorpse` allows it (asked
  every 0.5 s for up to 6 s; never allowed: not picked up, the set-down still
  lands it) — and `RequestPutCorpse` at the set-down. The game reports the carry
  once the pick-up animation is over (3.5–4.4 s live); the mod asks until 6.5 s. A
  copy more than 2.5 m from the avatar is first fetched to where the carrier picked
  it up (only from within 30 m of it, and only to a spot a body can lie on). While
  carried (and 5 s after) the host's NPC stream, a resync, the puppet tick, the
  silence release and the copy guard leave the body alone. A Held for an avatar
  that dropped it on the way picks it up again (at most three times, with the same
  permission). A set-down of a carry this machine never showed (its grab refused
  here) moves nothing.
* **The landing** (`Wo148Rules`, mirrored in the mod): the carrier's own resting
  spot wins when a body can lie there and the body is more than 0.5 m from it; a
  body that would stay in the air (more than 0.8 m above the first surface under
  it) or under the ground (more than 0.6 m below it) goes back where it was
  picked up; logged as `MP-CARRY land ...`.
* **Never alive**: a grab of a body that is alive here is refused (`alive`), a
  body that woke on the way is never moved, and this player's own carry of a
  living NPC (a quest's `CarryLivingActor`) is not sent at all.
* **Sacks**: the pile and ground pick-up and deposit callbacks are wrapped, the
  `put_item` key marks a drop; the carrier's avatar plays the game's
  `CarryItemPickup` / `CarryItemPlace` one-shots and holds the game's sack model
  (a prop: no physics, never saved) on its right hand by the game's own
  `Human.AttachEntityToHand`, so it walks with the hand; a dropped sack is the same
  prop where it landed, removed when the carrier picks it up again. Nothing of the
  other world's piles changes (the quest side). (Stage A's `sack_miller` hand
  content is refused by the DLL on a host; the live run showed empty hands.)
* **Switches**: `mp_carry_sync` (bodies and the whole layer) and
  `mp_carry_objects` (sacks), both **on**: each ran live solo, both ways, and
  fails closed (the work order's "default on only when proven", read as WO-147
  read it: proven live, solo); `mp_carry_test grab <body> | put | status` (the
  console stand-ins); `mp_carry_status`; checklist markers `mark_carry`,
  `mark_carry_host`, `mark_carry_both`, `mark_sack`, `mark_bury`,
  `mark_dice_keys`.
* **Quest reactions (3.3)**: on the host, a quest change made by its own world
  while a partner carries a body, or within 30 s of a set-down, is logged as
  `MP-CARRY quest-reaction ...`; WO-147's quest safety still guards destructive
  steps. Quests counting the joiner's carry and crime for a carried body are the
  next phase (not built).

### 4.3 The kinds, shown and not shown

| kind | shown | evidence |
|---|---|---|
| a dead body: pick-up, carry walk, set-down, on the host's screen (a partner's) | **shown** | (observed) H1, H2: frame 1; the body 1.4 m up on the figure's shoulder through a 7 m walk, landed at the partner's spot |
| the same on the joiner's screen (the host's) | **shown** | (observed) J1–J3: frame 4; on the shoulder through the walk (0.14–0.20 m off the figure's centre) |
| an unconscious body | **shown** | (observed) H2: frame 2; still knocked out where it landed |
| an unconscious body that wakes while carried | never moved alive | (observed) H2: the game ended the carry itself, the NPC stood up beside the figure, the set-down left it alone |
| one carrier at a time: the host's own player has it | the partner's grab refused | (observed) H2 |
| a joiner's carry the host refuses | put down and back | (observed) J1 |
| both grab the same body | the joiner loses, the host's figure takes it | (observed) J1, J3 (after the fix): frame 5 |
| a partner who leaves mid-carry | the body lies where the figure stood | (observed) H2 |
| a drifted copy / a copy far from both | fetched to the pick-up spot / refused, not moved | (observed) H2 |
| a living NPC (a hare, live; a quest's `CarryLivingActor`) | never moved | (observed) H2 for a partner's grab; (synthetic) this player's own carry is not sent |
| the stealth hold, then "pick up body" | as a body carry (the game's link, which live names the body) | (synthetic: suite (h)); the link (observed) H2; the takedown itself not run (it needs input) |
| a sack: the carrier's figure holds it, walks with it, drops it | **shown** | (observed) H2 and J1: frames 3 and 6; the drop logged where it fell (dusk and grass hid it in the frame) |
| this player's sack from a quest pile, into the quest's wagon | sent and received | (observed) H2 in the sack task's save (`socky`): the partner received Grab, Held, Put |
| a throw | none exists in the game | (code-verified: the census) |
| the burial's "bury" step | the quest's (not shown) | (code-verified); no save of this machine reaches it |

### 4.4 The live runs (Stage B)

All solo on one machine, the Modding Tools game, saves copied into a throwaway
`playline4` (Modding Tools saves only: the 1.1.1 saves of `playline0` are the
retail game's, see the progress page), the game started minimized and never
brought to the front, no key or mouse input (the console stand-ins
`mp_carry_test` and the game's own callbacks), frames by window capture.

| run | what | build |
|---|---|---|
| H1 | the real game hosts, a scripted partner (`avatarpeer`): a test bandit (a bandit soul: no crime) killed by the partner's blow, the partner's pick-up, walk, put-down; the host's own carry | w148a |
| H2 | again with the fixes: the same; the host's carry refused to the partner, the knocked-out body, the wake on the way, a living animal, a drifted copy fetched, a far copy refused, the partner leaving mid-carry, the sack on the figure's hand; the NPC trace (the seventh hook); the sack task's save: this player's sack from the quest's pile into its wagon | w148b + the mod's section loaded into the running game |
| J1 | the real game joins a synthetic host from the menu: the host's carry of a streamed dead body, the joiner's own carry refused, the race, the host's sack | w148c (+ the section) |
| J2, J3 | the race again on the final builds | w148d, w148e |

**Defects found live, each fixed, tested and rerun:**

1. **The agent dropped this player's own carry** (`MP-CARRY local event malformed`):
   it counted the mod's event words one short, so a carry never left the
   machine. The parser is a tested function now (`CarryLocalEvent`, pinned with
   the logged strings). (H1 → H2)
2. **The avatar's pick-up was judged at 2.5 s**, before the game reports it
   (about 3.5 s): every carry logged "NOT carrying". Asked until 6.5 s. (H1 → H2)
3. **A set-down of a carry refused here moved the body**: after a far refusal the
   put moved it 41 m. Such a set-down moves nothing now. (H2, rerun H2)
4. **The sack never showed**: the DLL's hand content refuses on a host. The game's
   sack model on the avatar's hand instead. (H2, rerun H2 and J1)
5. **In the race the avatar picked up while the loser still held the body**
   (`can=false`): on the joiner's screen the host's figure took the carry pose and
   the body hung beside it or over its head (the maintainer's frames). The pick-up
   waits for the game's permission now, and a copy beyond 2.5 m is fetched first.
   (J1, J2 → J3: the body on the shoulder through the race and the walk)
6. **The loser's late Held became a new grab**, refused by the host, and the
   player was told twice. The ledger remembers a loss for 5 s. (J1; unit-tested;
   the races of J2 and J3 told the player once, but their Held came before the
   loss, so this path did not run live again)
7. **This player's carry of a living NPC would go out as "dead"**: not sent now
   (found while preparing the quest's hunter save; the other screens refused it
   anyway).

**The census's open questions, answered live:** (1) `FindLinks(player,
'carriedBody')` names the carried body for the player (none for an avatar);
(2) `RequestGrabCorpse` works on an avatar on both sides when `CanGrabCorpse`
allows it (false while another actor holds or puts down the body); (3)
`RequestPutCorpse` leaves the body at the avatar's feet, 0.5–1.6 m from the
carrier's own spot, so the landing rule moves it there; (4) `GetItemInHand`
returns a null handle for a borrowed sack, and hand content on an avatar is
refused on a host (the sack model on the hand works); (5) the carry pose and walk
play on an avatar the stream moves; (7, in part) a carried knocked-out body that
wakes ends the carry itself. Not reached: (6) a quest's reaction to a body a
joiner carried (no save at a burial step), and the rest of (7) (a save or a heavy
hit mid-carry).

**The frame rate (for the maintainer; not investigated further, at the
maintainer's instruction):** in both host sessions the game started at about 75 FPS and fell
within minutes of the first test body being killed and carried (H1: 74 → 6 FPS in
8 minutes; H2: 75 → 23 in 1.5 minutes, 4 later). Not the agent, the partner, the
DLL's NPC sender (each A/B'd off: no change), the inactive-window throttle
(foreground: 8.8 FPS) or memory (14 GB free, no paging); the engine's profile
showed physics waits (185 ms), AI (88 ms) and audio (54 ms) per frame (symptoms,
section 7.3), and
removing the test body and loading another save did not restore it. The joiner
sessions ran at 20–25 FPS. The DLL's NPC sender costs 5–7 ms a frame on a host by
itself (its own counter). **Found after the WO: a stat stack in the game left growing by the DLL's
stamina reading (section 7), fixed in 0.42.8.**

### 4.5 The frame rate

Found after the WO and fixed in 0.42.8. It was not the carry: WO-147's stamina
reading in the DLL left an entry on one of the game's own stat stacks at every
wrong read, and every stat read in the game then walked all of them. The whole
story is section 7: the hunt, the game's mechanism, the bug, the fix, the live
check, what the investigation did in the maintainer's game, the lessons and the
proposed safeguards.

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

**Stage B (live):**

* **Only Modding Tools saves**: the Stage A plan named the 1.1.1 saves of
  `playline0`; their headers lack the Modding Tools build's own `Configuration`
  attribute — they are the retail game's, which the standing rules forbid — so
  the throwaway saves came from `playline1` (1.5.5, the Modding Tools build).
* **The defaults stay on** (section 4.2).
* **The sack on the avatar's hand** is the game's sack model as a prop on the hand
  (`Human.AttachEntityToHand`), not the DLL's hand content (refused on a host).
* **The reach** for a pick-up where the copy lies is 2.5 m (was 6 m): the game
  refuses a pick-up from farther, and a farther copy is fetched to the carrier's
  pick-up spot first.
* **The fixes were proven by loading the mod's section into the running game**
  (WO-48's method) and then on fresh builds (J2, J3).

## 6. Not done

* **A quest counting a partner's carry** (a burial, sacks delivered) and **crime
  for a carried body**: the next phase, as the work order says; the host's quest
  reactions are logged (`MP-CARRY quest-reaction`), but no save of this machine
  reaches a burial step, and the sack task's counter is not readable from the log.
* **The stealth takedown** that leads into a carry needs input; its "pick up body"
  rides the game's link, which works live.
* **A save or a heavy hit in the middle of a carry**: not run.
* **The dice keys**: the pak is built and the game opens it (observed); a key
  press is the checklist's (`mark_dice_keys`).
* **The frame rate**: found and fixed after the WO, in 0.42.8 (section 7).
* **The safeguards against another such bug**: no silent fault guards, reflection
  arguments that own their value and a frame-rate soak before every installer
  (required), a watchdog on the game's stat stack, the frame rate in the logs and
  the hunt's diagnostics kept (recommended): the next WO's scope, section 7.12.

## 7. The frame rate, after the WO: found, fixed and verified (0.42.8)

Found and fixed after the WO, at the maintainer's request, on the maintainer's
machine: the maintainer played, the game was read from outside. **The collapse
was the mod's.** WO-147's stamina reading in the DLL left an entry on one of the
game's own stat stacks at every read that went wrong, and from then on every stat
read in the game walked all of them. The bug is in 0.42.5 and 0.42.7 alike; 0.42.8
(`37ccea0`) fixes it. **(observed; code-verified)**

### 7.1 What was seen, and what the maintainer asked

* **Stage B (H1, H2):** the host fell from about 75 FPS to single digits within
  minutes of the first test body (section 4.4). During Stage B the maintainer
  called the frames horrific and, when asked, said to move on and deal with the
  frame rate later; 0.42.7 was built with it as a known issue.
* **After the WO** the maintainer noticed that the frames dropped "as soon as
  someone picked up a body" and asked whether a real session would do the same,
  asked to make sure it would not stay that way, to find what was flooding the
  logs if it dropped, and whether the maintainer's own PC (a browser with tabs and
  a chat app open) was to blame, and asked for a second kind of test: a bandit
  spawned for the maintainer to fight by hand.
* The maintainer **reinstalled 0.42.7 with its own installer** first (to rule out
  stale files), loaded a save and fought an unarmed test bandit: the frames fell
  **during the fight, before the kill**. In the slowed game the maintainer asked
  whether a PC restart would fix it, noted that 0.42.5 had not shown it, and
  suggested the spawned bandit as the cause. Section 7.11 answers each question.

### 7.2 The hunt, step by step

| step | what was measured | result | what it showed |
|---|---|---|---|
| Stage B | the frame rate over time; the agent, the partner and the DLL's NPC sender each switched off; the window in front; memory | 74 → 6 FPS (H1), 75 → 23 → 4 (H2); no change with each switched off; 8.8 FPS in front; 14 GB free | not those |
| Stage B | the engine's profile (`profile 1`) | physics waits 185 ms, AI 88 ms, audio 54 ms a frame | symptoms (7.3): every system that reads stats was slow |
| H3, H4 | a kill without a carry (the scripted partner's blow) | the same fall | not the carry |
| V1 | the game without the mod: a scripted kill (`DealDamage`), no fight | 76 FPS three minutes later | the game alone did not degrade, but with no fight and no crowd this control proved less than it seemed (7.9) |
| the logs | `kcd.log` lines, warnings and errors per 7-second window; the engine's trace file | the log's volume **fell** with the frame rate (about 600 lines a window at 75 FPS, 160 at 12 FPS), most of it the game's own behaviour-tree errors, present at full speed too; the trace grew 6 KB/s (the mod's data lines and the game's ambient monologues) | not a flood |
| the machine | per-thread CPU in the slowed game; the DLL's reads that cost the same every time | one thread at 93% of a core (the game's main thread: the id in the DLL's log column), the other 85 idle; in the per-task meter's run (next row) the fixed-cost reads kept their time (139–159 µs, 89–105 µs, 61–70 µs) while the frame rate fell 77 → 13 | not the machine; specific work was growing |
| the DLL's own cost | a per-task meter in the DLL's frame hook (a test build in this machine's own run, not shipped) | all of the DLL's per-frame work 0.6 → 2.9 ms, but `rttr::sample_health` 65 → 655 µs and the NPC sender's tick 68 → 1,490 µs grew 10–20×, and so did the game's own update under the hook (1.0 → 2.8 ms) | work that reads the game's souls grows |
| the slowed game's profile | `profile 1` in the maintainer's slowed 0.42.7 session | `CCryAction::PreSystemUpdate` self time 92 ms, the Lua timers 46 ms, collision avoidance 12 ms, `C_Actor::Update` 7 ms a frame | the time is in code the profiler does not label |
| Lua | every timer callback timed; the NPC-sync tick's parts timed | the 100 ms timer (`KCD2MP_NpcSyncTick`) took 53–61 ms a call over 82 tracked NPCs; its own parts 0–3 ms; NPC sync paused: 4.3 → 4.9 FPS | every engine call in the tick had slowed: a symptom |
| sampling | the main thread's call stacks (7.3) | 79% inside RPGModule, under one dependency-insert function | a growing stat stack |
| the stack | read from outside on the main thread | 4,578 entries, all `0x7270688d` | leaked, for good |
| the queries | `IsCarryingCorpse`, `IsDead`, `IsUnconscious` and `GetState('health')`, ten calls each from the console | no growth | not WO-148's 5 Hz carry query |
| the DLL | every read that can hand `GetState` a wrong state | `sample_health`'s stamina argument (7.4) | the cause |

### 7.3 The game's side: a stat-evaluation stack

Modding Tools build 1.5.5; the addresses are RPGModule offsets.

* `+7326d0` is the getter behind `Soul::GetState`. Through RTTR it is a
  one-argument float method: CrySystem's `rttr::method::invoke` → `+7bb650` →
  `+7c2840` (which follows the argument's pointer and reads the 32-bit state behind
  it) → `+7326d0`, which:
  1. pushes `state + 0x117` onto a `thread_local std::vector<uint32_t>` (RPGModule's
     TLS block `+0x1e0`, its `_tls_index` at `+0x1136EAC`);
  2. when the stack holds two or more ids, calls `+844e70(top, id)` for every id
     below the top;
  3. reads `[soul + 0x780 + state × 4]`;
  4. pops.
* `+844e70(a, b)` takes a mutex (`_Mtx_lock`, an SRW lock) and inserts `a` into
  `A[b]` and `b` into `B[a]`: two global flat maps (sorted 32-byte entries keyed by
  id, each holding a sorted vector of ids), a registry of which stat depends on
  which.
* `+13caf0` (derived values, `index + 0x3d`) and the other evaluators use the same
  stack in the same way: 66 function chunks in RPGModule touch it.
* **Nothing pops on a fault.** None of these functions cleans up on an exception,
  so a fault between the push and the pop leaves the entry for good. The stack
  belongs to the main thread, which lives as long as the game: no load, save or
  menu clears it.
* **The cost:** every stat read in the game makes one locked insert per entry on the
  stack. At 4,578 entries the sampler (1,610 samples of the main thread in 8 s,
  each suspending it for about 40 µs) found:
  * 79% of the main thread inside RPGModule, 78% under `+844e70`;
  * the hottest code in its two lookups `+8acd00` (27%) and `+897040` (25%), then the
    module's Just-My-Code check on every function entry (`+bc1014`,
    `__CheckForDebuggerJustMyCode`, 9%), `_Mtx_lock`/`_Mtx_unlock` and the SRW lock
    itself (about 11%), and waits for the lock held by another thread (2%);
  * every reader of stats on the way in: the AI (CryAISystem → XGenAIModule →
    `+5c7d10` → `+13caf0`: 16% of the samples), Lua script binds (EntityModule →
    `+73dab0` / `+73da80`, the death and health checks → `+7326d0`: 20%), the DLL's
    own reflection reads (15%) and the game's update; the engine's own sleep 12%.
* That is why everything slowed at once, and why the engine's profile pointed at
  physics, AI, audio and unlabelled time in `PreSystemUpdate`.

### 7.4 Our side: the stamina argument (WO-147)

`sample_health` in `native/KCDMP/rttr_abi.cpp`, 0.42.5 to 0.42.7:

```cpp
alignas(8) unsigned char arg_st[32];
{
    // ...
    if (call_name_to_value(api.name_to_value, &en, &v_st, &sn)) {
        uint64_t st_val = 0;                          // lives in this block only
        std::memcpy(&st_val, v_st.data, sizeof(st_val));
        call_variant_dtor(api.variant_dtor, &v_st);
        build_argument(arg_st, &st_val, t_state);     // arg_st keeps &st_val
        have_st = true;
    }
}
for (int i = 0; i < g_tracked_count; ++i) {           // st_val no longer exists
    // ...
    if (have_st && t.nearPlayer) {
        // ...
        if (call_invoke1(api.invoke1, &m, &rs, inst.bytes, arg_st)) {   // reads it anyway
```

**The compiled code** (the shipped 0.42.7 DLL; the 0.42.5 DLL rebuilt from `22b5a90`
is the same, 0x30 bytes earlier): `st_val` is `[rsp+0x38]`. It is written at
`KCDMP.dll+a1bf` and its address put into `arg_st` at `+a1c9`, and in the loop the
stamina reading is stored into the same slot (`+a375`, `+a38d`; read back at
`+a3a2`).

**What one sampling pass did** (every 60 ms on the main thread, once a frame below
16 FPS), for the souls within 15 m (`kStaminaRadius`):

1. the first soul: `GetState(stamina)`, right; its reading (a float) lands in
   `[rsp+0x38]`;
2. the next: `GetState(<that float's bits>)`. The getter pushes `bits + 0x117` and
   reads gigabytes past the soul: either the memory is mapped (a garbage
   "stamina", stored as the next index) or the read faults. The guard in
   `call_invoke1` catches the fault and returns false, nothing is logged, and the
   entry stays;
3. every later soul in the pass: the same index, the same fault, one more entry.

So one soul near the player leaks nothing (observed: no growth with only one body
near), and n souls leak up to n − 1 entries a pass. With four people near at 60+
FPS that is up to about 50 a second: thousands within minutes.

* **Why always `0x7270688d`:** that is state `0x72706776`, which as a float is
  4.8 × 10³⁰, no soul's stamina. Most likely the first garbage read (step 2)
  returned the same bytes every time for the same souls, and that became the index
  that faulted. Not proven; it does not change the fix.
* **Why the frame rate never came back:** the entries stay on the main thread's
  stack (7.3).
* **Why it looked like the carry, then like combat:** the test bodies came from
  fights among people, with the bandit, the partner's figure and bystanders near. A
  crowd does the same without a fight.
* **Why 0.42.5 seemed fine:** the 0.42.5 DLL has the identical compiled bug. It needs
  two or more souls within 15 m for minutes, and the fall is gradual: in these
  scenes each entry cost about 0.04 ms a frame, so a few hundred take 75 FPS to
  about 40, and a session spent mostly alone on the road barely shows it. It does
  not mean 0.42.5's testers never hit it.
* **The same bug's side effects:** the stamina of the second and later souls near
  the player was never read right, so WO-147's stamina-only blows on them could be
  missed (a faulted read) or invented (a garbage reading compared with the last
  one). Their health readings were right: that argument's value lives as long as
  the loop.

### 7.5 The fix and the release (0.42.8)

* **The fix:** `st_val` is declared beside `arg_st` (one line moved, with a comment
  that says why). In the new build the stamina state has its own slot,
  `[rsp+0x78]`: written before the loop (`+a17d`, `+a1c4`), its address taken
  (`+a1ce`), never written again, and the reading goes to `[rsp+0x38]`. The DLL in
  the installer's payload shows the same three uses. **(code-verified)**
* **The other arguments:** every other `build_argument` call in the DLL (the
  other 20 of its 21 sites, all in `rttr_abi.cpp`) keeps its value alive across
  the call.
* **The commits on `main`:**
  * `37ccea0`: the fix, VERSION 0.42.8 (the maintainer's number), the README badge,
    the release notes, the tester page and the first version of this section. It
    sits on top of the maintainer's two README edits made on GitHub, pulled first.
  * `1f0782b`: the live check.
  * The per-task frame-cost meter used in the hunt is not in the release (not
    committed).
* **The gates**, in a fresh clone of `origin/main` at `37ccea0`: all 41 synthetic
  suites, both static checks (7/7, 6/6), the relay round trip 60/60, the agent
  tests 769/769, the native tests 328/328, and the payload smoke
  (`RELAY-SMOKE ok ... protocol=v10 release=0.42.8`).
* **The installer:** `release\KingdomComeTogether-Setup-0.42.8.exe`, 100,459,063
  bytes, SHA-256 `2dd2c994adf16b2dadb0fe4b3ecee807d2c8fae21b086f4dd7f909d4808066d7`.
  It is local only: no GitHub release, no tag. Its 1,026 files were swept like
  0.42.7's, and none of ours carries a private name, path or address (the only
  hits: the NAudio DLLs' own build path, and the documented example address in
  the master server's settings).

### 7.6 The live check (0.42.8)

One machine, the maintainer playing. Read from outside: the stack, and the DLL's
own list of tracked souls (`g_tracked`: which souls it counts as within 15 m, and
the stamina it last read for each).

| time | what | souls within 15 m (the DLL's list) | stat stack | FPS |
|---|---|---|---|---|
| 14:20 | baseline | 0 | 0 | 75.4 |
| 14:21–14:22 | three commoners spawned 2.5 m around the player (AI on, never saved), 90 s; one of them killed by the player at 14:22:43 (a fatal blow, logged by the DLL) | not read in this run | 0 throughout (one reading of 2: a nested read in progress) | 71–79 |
| 14:24–14:26 | the same with their AI off, 100 s | 3–4 (the three at 2.5 m, and passers-by), every one with a real stamina (120.0, 123.3, 120.0; 136.7, 153.3) | 0 | 64.5–68.8 |
| 14:29–14:31 | a fight: an unarmed bandit (all 12 of its items removed) among the three commoners | up to 5 | 0 | 67.7–75.2 |
| 14:31 | the player dies; the mod's respawn (a grave, the wake 316 m away, 6.8 s) | none (the 316 m wake) | 0 | 57.0, then 24.6 during the wake teleport |
| 14:32 | after, the test figures removed | none | 0 | 74.6 |
| 13:38 (0.42.7) | the maintainer's session after one fight among people | (not read) | **4,578** | **about 4.5** |

* **The check really ran the faulty path.** The proof is the DLL's own list: on
  0.42.7 only the first soul near the player could get a real stamina, and on
  0.42.8 every one did. The same spawned crowd was not run on 0.42.7: the
  maintainer installed 0.42.8 directly.
* **Not covered:** a two-player session (the tester page's checks 1 and 2).

### 7.7 Also seen

* **A second, player-like soul in the DLL's list.** It stays within 15 m of the
  player all the time, even through the 316 m wake teleport, with its health and
  stamina frozen at the values the session started with (75.3, 118.0), and with no
  entity of its own near: the only soul-carrying entity within 15 m was the
  player. Harmless for the frame rate now; worth a look if blows on "an NPC" that
  is really the player come back (WO-99). Not investigated.
* **"Bandits have a million health."** Of the last 15 blows the DLL reported before
  the player's death, 13 did no health and 13–17 stamina each (the bandit's
  block: WO-147's stamina-only blows, now read for every soul near), and two did
  0.98 and 7.09; his health went 84 → 61. The respawn's classifier logged the
  player as starving at the time, which weakens the player's blows. This is the
  game's own combat, not the mod.
* **The game's own log errors** (the behaviour-tree crime nodes of the village's
  NPCs, 100–120 per 7-second window) are there at full speed too, before and after
  the fix.

### 7.8 What the investigation did in the maintainer's game

* **Read from outside:** memory reads, and stack samples of the main thread (each
  sample suspends it for about 40 µs; 1,610 samples once); console queries; three
  Lua timing wrappers in the 0.42.7 session, put back afterwards.
* **Spawned, all of them never saved and all removed afterwards:**
  * the bandit `wo148_manual_1` in the 0.42.7 session: it killed the player once
    (the mod's respawn), then the maintainer killed it; its body was left in that
    session;
  * the three commoners `wo148_repro_1` to `_3` in the 0.42.8 session, twice; the
    player killed one;
  * the bandit `wo148_manual_2` in the 0.42.8 session: it killed the player.
* **The saves.** Both sessions ran on the maintainer's own `playline2`, not on a
  throwaway copy: the 0.42.7 session loaded `autosave027` and the 0.42.8 session
  `autosave038`. The investigation did not check which save was loaded before it
  spawned test figures.
  * The game autosaved every five minutes in both sessions: `playline2/autosave029`
    to `038` (13:13–13:59, on 0.42.7) and `039` to `041` (14:24–14:34, on 0.42.8),
    all of them after the first test death.
  * They carry the tests' consequences: the two deaths (each time the mod's grave
    at the fight's spot holding the inventory, 34 items, and the wake spot), and
    possibly a crime for the killed commoner (from `039` on). The spawned figures
    themselves were never saved.
  * **Nothing older changed:** all 336 save files hashed at the start of WO-148
    still match their SHA-256; the other new files are this WO's five throwaway
    copies in `playline4`. `autosave027` (2026-09-26, the save loaded at 13:05) is
    intact.
  * Whether to keep `029` to `041` is the maintainer's call; nothing was deleted.
* **One mistake earlier in the hunt:** a launch meant for this machine's own
  throwaway session partly ran while the maintainer's game was up (12:56). It added
  a save lock, injected a test DLL beside the launcher's, and started a relay and
  an agent. They were stopped and the lock removed within minutes, the maintainer
  restarted everything, and nothing of it remained (checked).
* **The logs** of the slowed 0.42.7 session were copied before the next launch (a
  launch overwrites them); they are kept outside the repo.

### 7.9 Lessons

1. **A swallowed fault is not safe.** The DLL's guard turned a crash into silent
   damage to the game's own state, a little more every second, with no line in any
   log. The guard was meant as protection; inside game code it hid the bug and let
   the bug damage the game.
2. **A lifetime rule in a comment is not a rule.** "Values must outlive the
   arguments that point at them" is written beside the earlier calls in the same
   file. The WO-147 change broke it, and nothing (not the compiler, not a test)
   could notice.
3. **Nothing measured the frame rate over time.** The checks test features; in
   WO-148 the drop was blamed on the test setup and shipped as a known issue.
4. **The baseline is the game without the mod, with the same trigger.** A
   comparison with the previous release would have shown nothing (0.42.5 has the
   same bug), and the no-mod control here (V1) was a scripted kill without a fight
   or a crowd, so it proved less than it seemed.
5. **The engine's profiler shows where time is billed, not why.** It pointed at
   physics, AI, audio and unlabelled time in `PreSystemUpdate`; the A/B switches
   and the per-task meter could only rule things out. A sampling profiler with
   whole call stacks found the cause in minutes.
6. **The first correlation was wrong twice:** "after the carry", then "in combat".
   The trigger was two or more souls within 15 m.
7. **A test in the maintainer's own session needs the loaded save checked first.**
   The test figures went into a real playline whose autosaves now carry their
   consequences (7.8).

### 7.10 Safeguards proposed (not built; the next WO's scope is 7.12)

| # | safeguard | what it would have caught | notes |
|---|---|---|---|
| 1 | Fault guards that are not silent: log the first fault at each call site (rate-limited), count faults in the periodic status line, and switch the failing read off after a few | the first 0.42.5 session's log would have said `GetState faulted (stamina)` | 303 guards in 36 files; the calls into game code first |
| 2 | Arguments that own their value (an `Arg<T>` holding the value and the RTTR argument together) instead of pointing at the caller's variable; the 21 call sites converted | this bug, and every future one of its kind | a refactor of `rttr_abi.cpp` |
| 3 | A watchdog on the game's stat stack: at the frame hook, where it must be empty, read its depth and log loudly when it is not; optionally clear it | the same symptom from any cause, ours or the game's | port-specific (this build's TLS index and offset), so verified at start like the hooks; log-only first |
| 4 | A frame-rate soak before every installer: 10 minutes with spawned people within 15 m and a fight, scripted as in 7.6; it passes when the frame rate stays within about 10% of where it started and the stat stack stays empty, against the game without the mod | this release blocker, before any tester | a tool and a gate in the build |
| 5 | The frame rate in the logs: the periodic heartbeat line carries the frame rate and the frame time | a slow decline in any tester's bug-report zip, even when nobody notices it | small |
| 6 | Keep the diagnostics: the main-thread sampler, the stack reader and the near-list reader as read-only tools in the repo | the next hunt starts where this one ended | the per-task meter could ride behind a switch |
| — | Process: an unexplained frame-rate drop of this size blocks the installer, and the assistant says so before building, even when told to move on; before any test action in the maintainer's own session, the loaded save is checked | — | adopted |

### 7.11 The maintainer's questions, answered

* **"Is it my PC?"** No. With the same programs open the game ran at 71–77 FPS
  until a fight. In the slowed game one thread ran at 93% of a core while 85 others
  idled, and the DLL's fixed-cost reads kept their speed. A PC restart does not
  help; a game restart clears it (until the next crowd, on 0.42.7).
* **"Is it for sure the mod?"** Yes: the DLL's stamina argument (7.4), fixed in
  0.42.8 (7.5, 7.6).
* **"It didn't happen in 0.42.5."** The 0.42.5 DLL has the same compiled bug. It
  needs several souls near the player for a while (7.4).
* **"Could it be the bandit you spawned?"** Only as one of the souls near the
  player: any two or more do it, spawned or not.
* **"Is it the carry?"** No: a kill without a carry did it (H3), and the frames fell
  before the kill in the maintainer's own fight.
* **"Should carrying animations actually work?"** Yes, as in section 4.3. The
  carrier's figure picks the body up with the game's own pick-up, carries it on its
  shoulder through the walk in the game's own carry pose, and sets it down with
  the game's own call; a sack sits in the figure's right hand with the game's own
  pick-up and place animations. This was seen live on one machine (a scripted
  partner and a synthetic host), not yet with two real players; quests counting a
  partner's carry are not built (section 6).
* **"What lessons, so we don't break the game again?"** Sections 7.9 and 7.10;
  what the next WO builds is 7.12.

### 7.12 For the next WO: the safeguards to build, in order

The recommended scope. The maintainer decides the WO, its number and its version.
Items 1, 2 and 4 are **required**: each would have stopped this release on its
own. Item 5 makes a decline visible in the field, and items 3 and 6 make the next
hunt short. Every new behaviour gets an `mp_` switch like the others, and its
default is the maintainer's call under the standing rule "default on only when
proven". Nothing proven by WO-131 to WO-148 (or by 0.42.8's fix) may regress.

**1. No silent fault guards (required).**

* **Scope:** the DLL's 303 `__try` guards in 36 files, 36 of them in
  `rttr_abi.cpp`. 239 of their handlers are one line that returns quietly. The
  guards around calls into game code come first (reflection invokes, property
  reads, method calls, the hooks' trampolines), then those around our own reads
  of game memory.
* **Build:** one guard helper, used everywhere, carrying a site name.
  * Its filter records the exception code and the faulting address as
    module+offset.
  * The first fault at a site is logged at once (for example `FAULT
    rttr::sample_health/GetState(stamina): 0xC0000005 at RPGModule+0x7327df
    (1st)`), and again at the 10th and the 100th.
  * While any count is non-zero, a line every 60 s sums them.
  * After 8 faults at one site in a session, that site is switched off (its call
    returns "failed" without calling the game) and the log says so. A fault inside
    game code may have left the game's own state half-changed: that is this
    hunt's whole lesson.
* **Switch:** the logging always runs. Switching a site off is the new behaviour;
  recommended on, since it only stops our own reads.
* **Done when:**
  * native unit tests drive the helper with a deliberate fault: the counts, the
    lines' text, the switch-off at the 8th, the reset in a new session;
  * the gate script fails a build that adds a raw `__try` outside the helper;
  * live, a crowd and a fight (7.6) log no `FAULT` line.

**2. Reflection arguments that own their value (required).**

* **Scope:** `build_argument` (`rttr_abi.h`) and its 21 call sites, all in
  `rttr_abi.cpp`. They make 11 reflected calls: `GetState` ×3 (the health and
  stamina sampler, `soul_state`), `SetState`, `TakeDamage` ×4, and
  `HasCombatHistoryWithSoul` ×2 (two probes and the WO-147 combat history), and
  `GetFaction`.
* **Build:** an argument object that holds a copy of the value next to the RTTR
  argument that points at it, neither copyable nor movable. The pointer-taking
  `build_argument` goes away. An argument can then no longer outlive its value:
  they are one object, and the compiler refuses any use of it outside its scope.
* **Done when:**
  * all 21 sites are converted;
  * a unit test pins that the argument points into its own object;
  * the gate fails a build with a raw `build_argument(`;
  * live, with WO-119's combat harness, everything it touches still works:
    * the health and stamina reads (the crowd of 7.6);
    * a blow and a stamina-only blow, both reported;
    * the partner's damage applied (`TakeDamage`);
    * a faction read.
* **Risk:** it touches every reflection call the DLL makes, combat damage
  included. The live check above is not optional.

**3. A watchdog on the game's stat stack (recommended).**

* **Build:** at the DLL's frame hook the main thread is inside no stat evaluation,
  so the stack must be empty there.
  * Read its depth (RPGModule's TLS index, and the vector at its block `+0x1e0`;
    see 7.3).
  * When it is not 0, log it (for example `STATSTACK depth 12, top id 0x...:
    leaking`), then again every 10 s while it grows.
  * Port-aware, like the hooks: the TLS index's address is found from the code of
    the stack's accessor (`+844e00`), checked against its expected bytes at start.
    Another build (retail, a patch) leaves the watchdog off and logs why.
* **Optional, behind its own switch:** clear the stack when it is not empty at the
  start of a frame (it must be empty there). This would have given the frame rate
  back without a restart; it writes game memory, so it is proven before it is on.
* **Done when:**
  * a unit test covers the byte check and the depth arithmetic;
  * live, in a crowd and a fight, the depth is 0 and the watchdog logs nothing;
  * a test-only console command that leaks one entry on purpose (an invalid state
    through the guarded invoke) is logged within a second; with the clearing on,
    the depth goes back to 0.

**4. A frame-rate soak before every installer (required).**

* **Build:** a script grown from this hunt's crowd and fight checks (7.6). On a
  throwaway save, for 10 minutes, it:
  * puts three commoners within 15 m (no AI, never saved), then starts a fight
    with an unarmed bandit;
  * every 10 s reads the frame rate, the stat stack (item 3, or the outside reader)
    and the native log's `FAULT` lines, and writes them as a table;
  * removes everything it spawned.
* **Pass:**
  * the frame rate over the last 2 minutes is within 10% of the first 2 minutes,
    and of the same scene in the game without the mod (no pak, no DLL);
  * the stat stack is 0 throughout;
  * no `FAULT` line.
* **The rule around it:**
  * the progress page records the table;
  * `Build-Installer.ps1` is not run before the soak passes;
  * the script checks the loaded save first (7.8) and runs only on a throwaway
    copy.

**5. The frame rate in the logs (recommended, small).**

* **Build:** a line every 60 s from the DLL's frame hook with the frame count and
  the mean and worst frame times, reusing the frame accounting of the hunt's
  per-task meter. The launcher's Report Bug zip carries the native log, so any
  tester's zip shows a decline even when nobody noticed one.
* **Done when:** a unit test of the arithmetic passes, and the line appears in a
  live session.

**6. Keep the diagnostics (recommended).**

* **The tools:** this hunt's read-only tools go into `tools/perf/` with a README,
  cleaned of machine paths:
  * the main-thread sampler and its call-tree analysis;
  * the stat-stack reader;
  * the reader of the DLL's near list, with its offsets taken from the DLL's own
    code instead of hard-coded;
  * the string and TLS-user finders.
* **The meter:** the per-task frame-cost meter in the DLL (built and tested in the
  hunt, not committed) goes behind an `mp_` switch, off by default.
* **Done when:** each tool runs against a live game, and the privacy scan is clean.

**Process, adopted now and to be written into the next WO's rules:**

* An unexplained frame-rate drop of this size blocks the installer. The assistant
  says so before building, even when told to move on.
* Before any test action in a session this machine did not start, the loaded save
  is checked; a real playline means asking first.
* A performance comparison is against the game without the mod, with the same
  trigger.

**Not in it:** the second, player-like soul (7.7) needs its own look, and the hit
sampler's other reads are out of scope beyond what items 1 and 2 touch.
