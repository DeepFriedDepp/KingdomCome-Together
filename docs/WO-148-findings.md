# WO-148 — Attribution, the port-aware setup, and carrying on the other screen: findings

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

**Version: 0.42.7** (the maintainer's number). Status: **done** — Stage A (no
game, no window) and, after the maintainer's go-ahead, Stage B (live, solo, on
throwaway save copies of the Modding Tools build). 0.42.5 is bookmarked as the
tag `v0.42.5` on `7dcd01a` (the commit its installer was built from), pushed
before any code.

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
showed physics waits (185 ms), AI (88 ms) and audio (54 ms) per frame, and
removing the test body and loading another save did not restore it. The joiner
sessions ran at 20–25 FPS. The DLL's NPC sender costs 5–7 ms a frame on a host by
itself (its own counter). **Found after the WO: a stat stack in the game left growing by the DLL's
stamina reading (section 4.5), fixed in 0.42.8.**

### 4.5 The frame rate, found (after the WO; fixed in 0.42.8)

At the maintainer's request, after the WO. **The cause is ours:** WO-147's
stamina reading in `rttr::sample_health` (`native/KCDMP/rttr_abi.cpp`), so it is
in 0.42.5 and 0.42.7 alike.

* **What the game was doing.** A sampling profiler on the game's main thread
  (suspend, copy the used stack, resume: about 40 µs; walked offline): 79% of the
  main thread inside RPGModule, under one function (`RPGModule+844e70`: a
  mutex-guarded insert into two flat maps), called from the soul stat getter
  behind `Soul::GetState` (`+7326d0`) — reached from the game's AI, from Lua
  script binds and from the DLL alike. The getter pushes the stat's id onto a
  thread-local stack, records a dependency pair for every id already on it,
  reads the stat and pops. Read from outside, on the main thread, after a fight:
  the stack held 4,578 entries, all the same id (`0x7270688d`), so every stat
  read in the game made 4,578 locked inserts (about 4.5 FPS). Its depth is 0
  between frames when nothing leaks.
* **What left the entries.** The stamina state's value (`st_val`) was declared
  inside a block; the argument built from it (`arg_st`) keeps a pointer to it
  and is used in the loop after the block. In the built DLL — the 0.42.5 build
  (rebuilt from `22b5a90`) and the 0.42.7 one alike — the compiler gave the
  stamina reading the same stack slot (`[rsp+0x38]`). After the first soul
  within 15 m, every `GetState` call got a stamina reading as its state. The game
  read out of range and faulted after its push; the DLL's fault guard
  (`call_invoke1`) swallowed the fault and returned false, so nothing was logged
  and the pop never ran: one entry per wrong read, up to 16 passes a second,
  never removed (a thread-local of the game's main thread: only a restart clears
  it). A single soul near the player leaks nothing (observed: the stack stood
  still with only one body within 15 m).
* **Why it looked like the carry.** The first test bodies came from fights among
  people. The stack grew with every person near the player and the game never
  recovered, so "after the carry" was "after the fight". It is not combat as
  such: any two or more souls near the player.
* **Ruled out on the way.** The machine (reads of fixed cost kept the same µs
  while the frame rate fell 77 → 13; one thread at 93% of a core, the other 85
  idle), the log volume, memory, a Lua timer leak, the DLL's own per-frame tasks
  (0.6 → 2.9 ms of a frame, by a per-task meter), and the game without the mod
  (76 FPS three minutes after a kill without a fight).
* **The fix (0.42.8).** `st_val` is declared beside `arg_st`. In the new build
  the stamina state has its own slot (`[rsp+0x78]`), written once before the
  loop; the reading is stored elsewhere. Native tests 328/328.
* **Live check (0.42.8, one machine, the maintainer playing):**
  * Three commoners spawned 2.5 m around the player (no AI, never saved), 100 s:
    the DLL counted 3–4 souls within 15 m the whole time and read a real stamina
    for every one of them (120.0, 123.3, 120.0; 136.7 and 153.3 for passers-by).
    The game's stat stack stayed at depth 0 (one reading of 2: a nested read in
    progress); 64–70 FPS throughout (69.6 before, 69.9 after).
  * The same with their AI on, 90 s, one of them killed by the player in the
    middle of it: depth 0 before and after the kill, 71–79 FPS.
  * A fight: an unarmed bandit and the three commoners, five souls within 15 m,
    about 100 s of fighting until the bandit won. The player's blows mostly fell
    on his block (no health, 13–17 stamina each: WO-147's stamina-only blows,
    reported as designed). Depth 0 throughout, 67–75 FPS (a dip to 25 during the
    death's wake teleport, 74.6 after).
  * For comparison, the same game on 0.42.7 after one fight among people: depth
    4,578, about 4.5 FPS.
* **Also seen, not investigated:** one soul the DLL tracks within 15 m follows
  the player (through a 316 m wake teleport) with its health and stamina frozen
  at the values the session started with (75.3, 118.0), and no entity of its own
  near: a second, player-like soul. Harmless for the frame rate now; worth a look
  if hits on "an NPC" that is really the player come back (WO-99).

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
* **The frame rate**: found and fixed after the WO, in 0.42.8 (section 4.5).
