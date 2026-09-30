# WO-144 — Field fixes: findings

Kingdom Come: Together. Unofficial; not affiliated with or endorsed by Warhorse
Studios.

**Version: 0.42.2** (the maintainer's number). The build and the gates are in
`docs/WO-144-progress.md`. No GitHub release. 0.42.0 stays the fallback,
bookmarked as the tag `v0.42.0` on `53d3ba0`.

Evidence marks: **(observed)** seen live in the game, on frames of the screen
that matters or in its own log lines; **(synthetic)** the real game against a
scripted partner or a synthetic host, or a console stand-in for a player's
action; **(code-verified)** read in the code, the game's data or binaries, or
pinned by a unit test, but not run live; **(inconclusive)** tried, no clear
answer.

The field material: four log bundles from the first real two-player sessions
on 0.42.0 (session 3: the host and the joiner, before and after the host's
crash). The two earlier groups the work order mentions were not attached; where
an item's evidence lives only there, this page says so. All live runs were solo
on one machine (the Modding Tools game, 1.5.5), on a throwaway playline of save
copies: the real game as the **host** with a scripted partner (runs L0, L1,
L1b, H2), and as a **joiner** of a synthetic host (runs J1–J3). No input was
sent; the camera was aimed and the player moved by console.

## The answer

The first real sessions broke on four things underneath everything else, and
all four are fixed at their root:

1. **A partner who never left** — a joiner whose game restarted stayed a
   partner (`peers=2`) all session; every sleep vote waited for him and timed
   out, so the players turned the vote off, slept apart, and their clocks and
   worlds drifted. The agent now takes partners only from the relay's live
   connections, and the relay replaces a player's old connection the moment he
   connects again. **(observed)**
2. **The host's crash on a load in a session** — our faction write on the
   partner's avatar used the base class's `SetParent`, which moves only the
   child's side of the link; the old faction kept a dead child, and the load's
   faction pass called through it. The node's own `SetParent` is used now; 0.42.0
   crashed on the first in-session load, the fix took ten in a row.
   **(observed)**
3. **The partner shown naked** — a world saved while a partner was connected
   keeps that partner's avatar soul. After a load the game's by-name soul lookup
   answers with that saved soul, not the live avatar, and 0.42.0 dressed the
   saved one: the joiner's agent logged the whole outfit worn while the host's
   avatar stood there bare. Avatars are now found by their own entity key and
   dressed from their own inventory; the outfit is read back every 10 s.
   **(observed)**
4. **The lantern** — an avatar's soul is a real NPC's, and the game's outfitting
   rules give most NPC classes a lamp or a torch; its night behaviour draws it.
   Every light an avatar owns is taken out unless its player's torch is out.
   **(observed)**

| item | status | evidence |
|---|---|---|
| 1.1 phantom partner | fixed | (observed) L0 reproduced `peers=2` on 0.42.0; L1/L1b: `replaces its older connection`, `peers=1`, the vote asked of one joiner and went through |
| 1.2 host crash on an in-session load | fixed at the root | (observed) three identical dumps read; L0 crashed on 0.42.0's first load; L1b: 10 consecutive loads, each with the avatar attached, no crash |
| 1.3 talk hold before the talk | fixed | (observed) J3: the host's NPC held only once the joiner was in the conversation, released at its end; a dropped request holds nothing |
| 1.4 tutorial-era joins | fixed | (synthetic) a file that lists only money joins with a warning; (observed) J1–J3 joined with a tutorial-era save; aborts say why |
| 2.1 clothes both ways | fixed | (observed) J2/J3: the host's avatar dressed on the joiner and stable; H2: the host screen, the outfit watch re-dresses within seconds |
| 2.2 crouch | fixed | (observed) H2: crouched for 3 min, no flip back; the crouched walk is the sneak walk |
| 2.3 horses | partly | (observed) a living horse copy is bound and now shown (it was hidden on the joiner); riding on the host's screen: horse and pose; parked encounter copies with no physics stay unbound (§2.3) |
| 2.4 lantern | fixed | (observed) H2 and J2: the NPC lamp taken out, no light at night, the player's torch shown only while it is out, nothing dropped |
| 3.1 joiner fights animals | precondition fixed | (observed) living animal copies bind and show; a real hit was not run (no input) |
| 3.2 dice against an NPC on the joiner | fixed | (synthetic) the conversation's end waits for the minigame the dice fader leads into |
| 3.3 one clock | fixed | (observed) J1: the joiner's clock stands while the host's does and runs again with it; (code-verified) the tolerance and the pull |
| 3.4 the joiner's red HUD icon | identified, not fixed | (code-verified) the HUD's wanted indicator; clearing it needs the guards' copies to forget (§3.4) |
| 3.5 player sounds | not done | (code-verified) the whistle's action and sound are known; catching the key needs a live key press (§3.5) |
| 4.1 corrections that aren't | fixed | (code-verified) a port that missed the host's value once is not fired again |
| 4.2 a scene that never ends | detection only | (code-verified) the scene's lines are logged now; ending it was not built (§4.2) |
| 4.3 a forced conversation that dies | not done | group 2's logs were not attached (§4.3) |
| 4.4 an escort follows its leader | not done | (§4.4) |
| 4.5 NPC clothing state | half fixed | (observed) a placement or one-shot never changes a copy's clothes now; the host's outfit state is not sent (§4.5) |
| 4.6 journal marker letters | not done | (§4.6) |
| 5 avatar sit-down | fixed | (observed) an NPC body cannot sit on a bed; it lies on it |
| 5 joiner host claim | fixed | (observed) no claim in J1–J3; (code-verified) the claim after each join |
| 5 NullRefs after the game died | fixed | (code-verified) the pipe is never used after the reader dropped it |
| 5 floating NPCs | log line | (code-verified) a copy held 12 m or more above the ground is logged |
| 5 log retention | fixed | (code-verified) the launcher keeps six earlier launches' logs; Report a bug collects them |
| 5 grindstone blade, alchemy anchor, seated tools | not done | (§5) |

## New settings

Every one is on by default: each is proven live (observed) except where the
table says otherwise.

| setting | what it does | off = |
|---|---|---|
| `mp_avatar_dress` | an avatar wears pieces from its own inventory (the mod equips them); its real outfit is read back every 10 s and put right | 0.42.0: REST `EquipItem`, no read-back between packets |
| `mp_avatar_lights` | an avatar holds a light only while its player does; its own NPC lamps and torches are taken out | 0.42.0 |
| `mp_show_animals` | a horse or animal the host streams is shown on the joiner even where this world keeps it hidden | 0.42.0 |

Everything else rides a setting that exists: the partner set and the vote
(`mp_sleep_vote`), the crash fix (the avatar's faction, WO-131), the talk hold
(`mp_quest_talk`), the clock (`mp_shared_world`), crouch and the bed
(`mp_activities`), the kept equipment state (`mp_activities`, `mp_oneshots`),
the corrections (`mp_quest_sync`), the claim (`mp_shared_world`).

---

## Phase 1 — the foundations

### 1.1 The phantom partner

**Cause (code-verified, then observed).** The agent kept a table of partner
names and never removed an entry when the relay said a partner disconnected;
only a lost relay cleared it. Six features used that table as "the partners":
the session-mode announcements, the Henry records, the item and loot counters,
the quest and crime records, and the sleep vote. A joiner whose game or agent
restarted connects again before his old connection times out (10–20 s over
Steam), so the old id stayed a partner for the rest of the session. The field
host: `[disconnect] ghost 2 removed`, then half a second later
`session mode shared-world -> ghost 2`, and `peers=2` in every statistics line
to the end; every vote was `asked of 2 joiner(s)`, one said yes, and it timed
out.

**Fix.** Partners come only from the relay's live connections: a Name packet
adds one, its Disconnect removes it for good (a late frame from a removed id is
dropped: "a removed ghost stays removed"), a lost relay clears all. Every
per-partner loop uses that set. The relay recognises the same player (the same
address and name over TCP, the same Steam identity over Steam) and closes the
older connection at once: `[+] '<joiner>' (id=2) replaces its older connection
id=1 (tcp): the same player connected again.` A vote counts only partners who
are connected and in the host's world (the joiner's own world, a load, or the
menu keep him out, with a log line), and a partner who leaves mid-vote is
dropped from it instead of counting as a no. A joiner's open crime record is
kept by name for two hours, so a reconnect finds it again.

**Proof.** L0 (0.42.0): a stale connection plus the partner reproduced
`peers=2` exactly. L1/L1b (fixed): the relay replaced the stale connection
3.2 s after it came, `peers=1` in every line, and a sleep at a bed went
through — asked of one joiner, his yes, everyone's yes, the hours picker, the
sleep on both sides. **(observed)** Unit tests pin the partner set, the vote
rules and the relay's replacement.

### 1.2 The host's crash on a load in a session

**Cause (dump and code-verified; reproduced live).** Three BugSplat dumps on
this machine carry the same fault — the field crash of 29 September, one on the
27th, one on the 25th: an access violation at `RPGModule+0x4394A4`, in the
faction class's `IsPlayerNode`, which walks a faction's children and calls
each one. One child was a dead weak reference. How it got there: the partner's
avatar is put into the player's faction (WO-131) through the faction node's
`SetParent`. The DLL called the **base class's** exported `SetParent`, which
only writes the child's parent pointer. The NPC faction node overrides it (the
virtual slot): it takes the node out of its old faction's child list first and
puts it into the new one's. With the base call the avatar stayed a child of its
spawn faction; when a load destroyed the avatar, that faction kept a dead
child, and the load's faction pass (or the AI's next relation query) called
through it. L0, on 0.42.0: the first in-session load crashed at the same
address.

**Fix.** Every faction write the DLL makes (the player's faction, the hostile
faction, the detach, the probe) goes through the node's own virtual `SetParent`,
checked before use: the slot's neighbours must be the exported `GetParent` and
`GetId`, the function must lie in the RPG module and must not be the base.
If the check fails the write is refused and logged, never guessed. The agent
also re-attaches an avatar that a load re-created (0.42.0's cached soul record
skipped it).

**Proof.** L1b: ten in-session loads in a row, 16–17 s apart, each with the
avatar attached before it: no crash, twelve attaches read back. **(observed)**

The second crash in the field ("after EntityModuleOnPostLoadGame") fits the
same fault reached from the AI thread; its logs were in the missing group
**(inconclusive)**. The WO-125 promise stands: a load in a session is safe
again, so the tester page does not warn about it.

### 1.3 The talk hold

**Cause (observed, session 3).** The joiner's talk to a copy held the host's
NPC at the key press. When the conversation never started — six times on the
herbalist: `WO137-TALK end ... why=never-started held_s=25` — the host's NPC
stood frozen for 25 s each time. Underneath, the phantom partner (1.1): the
vote kept failing, the players turned it off and slept apart, the joiner's
clock ran hours ahead, and his copies were in their night state and refused to
talk.

**Fix.** The key press holds nothing. The engine's "Attempting to start" is not
a start either — it can still drop the request (J2: `Canceling dialog request
id 359 ... Request timed out`); the host holds its NPC once this player is in
the conversation, and releases it at the end. A dropped request holds nothing
and tells the player: "This person can't talk to you right now."

**Proof.** J3, by day: the player's request timed out, the villager's own
greeting chat took over, the hold went out only then (`QUESTASK talk on`),
and off at the end 14.9 s later. **(observed)** The WO-137 suite: 89/89.

### 1.4 Joins with a tutorial-era save

**Cause (code-verified).** The join's Henry check compared the file's items
with the live Henry's. A tutorial-era save lists only the money item; the live
Henry wears his clothes; the check called it a mismatch and sent the player
home without a word.

**Fix.** Money decides (a different amount, or an unreadable Henry, still
aborts); items that differ are a warning in the log. A file that lists nothing
but money is logged as "tutorial-era Henry". Every abort shows a plain message:
"Your character could not be read after loading your host's world." or "Your
character arrived in your host's world with different money than you have."

**Proof.** Unit tests (the tutorial-era shape, no item list, other money).
J1–J3 joined with a tutorial-era save as the character: `Henry check: MATCH`
(the live Henry carried nothing either, so the warning path itself is unit-only).
**(synthetic)**

---

## Phase 2 — the other player's body

### 2.1 Clothes, both ways

Four causes, all fixed:

1. **The wrong soul (observed, J1).** The joiner's REST lookup
   `SoulsByName/kcd2mp_0` answered with a soul at another place in the world, in
   a villager's clothing preset: the host's world save carried an avatar soul of
   that name from an earlier session, and the game's by-name table keeps the
   first soul registered. The live avatar stood in front of the player with its
   own key. 0.42.0 dressed and read the saved soul — the field joiner's
   `ghost 0: +10 -6`, then silence for ten minutes while he saw the host naked.
   Now the agent asks the mod for the live avatar's entity key (the soul key's
   last eight bytes) and sends every read and write to that soul:
   `kcd2mp_0 is another soul too (a saved one ...) -- every read and write goes
   to the live avatar's soul`.
2. **REST equips (observed, H2).** REST `EquipItem(class)` makes a new piece on
   every call (the avatar's inventory grew by one per call) and, in the state
   below, returned success while nothing landed. The mod now equips an item of
   that class from the avatar's own inventory (made once if it has none) through
   the actor.
3. **The engine undressing the avatar (observed, H2).** Twice in four tries the
   whole outfit came off in one frame at the end of a crouched walk, with no log
   line; equips then failed for minutes on the REST path. The trigger was not
   isolated **(inconclusive)**. The agent now reads each avatar's real outfit back
   every 10 s and dresses it again: `8 piece(s) came off since the last check
   (its player changed nothing) -- dressed again`, back on within about six
   seconds.
4. **Layers and refusals (code-verified).** Under-layers go on first (the field's
   plate refused: `It requires 'body_cloth_padded' slot to be filled`), a refused
   piece is tried again after 20 s, 60 s, 3 min, then every 10 min, and the log
   quotes the game's own reason. Placements never touch clothes (4.5).

**Proof.** J2/J3: the host's avatar on the joiner's screen dressed, the read
through its live soul matching, no re-dress for minutes. H2: the host's screen,
the watch's re-dress. **(observed)** Unit tests: the key match, the layer order,
the back-off, the reason.

### 2.2 Crouch

**Cause (observed, session 3).** The joiner's activity reconcile (WO-141) saw a
crouched avatar where "none" was wanted and stood it up (`CrouchUp` from the
load state) — 18 times in session 3. **Fix.** An avatar's crouch belongs to the
motion path (WO-121); the activity reconcile never touches it. **Proof.** H2: the
avatar crouched for three minutes, in step, the crouched walk the game's sneak
walk. **(observed)**

### 2.3 Horses and riding

**Found (observed).** A horse copy on the joiner is often **hidden**: J2's
`ttac_horse_1` was bound and written by the stream but `hidden=true active=false`
— nothing on screen. Shown (`Hide(0)`, `Activate(1)`), it is visible and stays.
The field's refused binds were almost all a different case: copies with **no
physics at all** — the random encounters' parked horses, cumans and a spawner's
wild dogs and boars (38, 30, 20, 16, 14 ... binds refused `not-living ... no
physics`). On the joiner those encounters never ran; the game keeps the copies
hidden and re-hides them. Only two refusals were bodies whose physics was not a
living entity, which the DLL now writes as horse-or-animal.

**Fixed:** a hidden horse or animal copy the host streams is shown
(`mp_show_animals`, observed J3: `WO144-SHOW ttac_horse_1 (Horse) was hidden
here -- shown`); a horse or animal whose physics is not a living entity is
written like any body (code-verified). The partner riding on the host's
screen: on its horse, in the riding pose, moving. **(observed)**

**Not fixed:** a parked encounter copy with no physics. Showing it needs the
encounter's own activation on the joiner (or a stand-in spawned in its place,
WO-136's way); forcing it visible fights the game. A mounted host on the
joiner's screen was not run (the synthetic host does not ride).

### 2.4 The lantern

**Cause (observed).** An avatar's soul is a real NPC soul (WO-83's roster). The
game's outfitting rules give most NPC classes a lamp or a torch
(`inventory_additive_lamp`, `_torch`, `_lampFancy`), and the mirrored outfit
carried the player's own torch too. At night the NPC behaviour draws any light
it owns: H2's avatar held a lit torch with its player's torch off; J2's host
avatar carried the soul's own `lamp_tool` — the field's lantern. Drawing the
player's torch into that hand dropped the lamp on the ground.

**Fix.** Lights are never outfit pieces. Every two seconds each avatar's lights
are put away and taken out of its inventory, except the player's torch while it
is out; the torch goes into an empty hand; put away and taken out when it goes.

**Proof.** H2 at night: no light (frame 7), the torch on with its player's
(frame 8), off again, nothing on the ground. J2: `WO144-LIGHT avatar id=0 took
... (lamp) out -- the NPC soul's own lamp`. **(observed)**

---

## Phase 3 — the joiner's gameplay

### 3.1 Fighting animals

The joiner's hits on a copy are forwarded only when the copy is bound (WO-131);
the field's one dropped hit was `not-bound`. Living animal copies bind now and
show (J2: a wolf copy streamed beside the player, visible). **(observed)** A real
hit was not run — it needs a swing, and no input was sent. The parked encounter
animals (wild dogs, boars) are the no-physics case of §2.3 and are not bound yet.

### 3.2 Dice against an NPC

**The log disagrees with the work order.** The `INVALID WUID ... not valid for
minigame` lines in session 3 belong to herb gathering and the grindstone, not to
dice. The dice failure was different: the dice conversation ended, the game's
pre-minigame fader played — and WO-137 paused the NPC's copy at the
conversation's end, so its side never started the game (the host's did).

**Fix and decision.** The NPC's copy runs its own brain for the game, as for the
talk (WO-137): a conversation's end waits 2.5 s, and if a minigame follows (the
fader, or the player's minigame edge) the copy keeps its brain until the
minigame ends (8 s after, at most 30 min). The host's NPC stays held all that
time. **(synthetic)**

### 3.3 One clock

**Cause (observed).** The joiner's clock ran ahead and was pulled back again and
again; some pulls were refused. The host's dialogue stands the host's clock
only, so the joiner's ran on.

**Decision: the joiner's clock pauses with the host's** (the alternative, not
pausing the host's world in its dialogue, changes the game's own rules for the
host). The host announces its clock's stand and run as a pause reason; the
joiner sets its clock ratio to 0 and back (the lever WO-112 T1 proved: the
clock stands, NPCs and timers run), never for more than 15 minutes on one
pause, and never after the host or the relay is gone. The pull-back tolerance
is one game-minute (was five); a pull is refused only when the calendar went
backwards (a load), and waits while the game's own skip runs.

**Proof.** J1: the synthetic host's pause → `WO144-CLOCK follow on
ratio_was=15 ratio_now=0`, the clock still over five seconds; the resume →
`follow off ... ratio_now=15`. **(observed)** The host's side reads the game's
own `IsWorldTimePaused`; a live host dialogue edge was not caught (the
`SetWorldTimePaused` console stand-in is not registered on this build)
**(code-verified)**.

### 3.4 The joiner's red HUD icon

**Identified (code-verified, not seen).** The HUD's top-centre indicators are
three: crime, **wanted**, trespass (`SetWantedState`). With the joiner's crime
record open all session (a guard assault whose stops ended `nostop` and
`talked`, witnessed lockpicks), it is the wanted indicator: the joiner's own copy
of the world believes him wanted. **Not fixed.** The game's forget message
(`crime:forgetCrimesData`, its only content the receiver's own key) runs only in
a running brain, and the joiner's guard copies are paused. A follow-up needs:
when the host's record for the joiner closes, each guard copy that holds a
memory of him is resumed for a moment, sent the message, and paused again — and
one screenshot of the icon to confirm which one it is.

### 3.5 Player sounds

**Not done.** The whistle is the actor action `call` and the sound
`v_horse_whistle` (`event:/voice/player/horse_whistle`); the game's Lua
`Player:OnAction` is the likely place to see it, but whether the engine hands
`call` to it needs one live key press, and no input was sent. The receiving side
is small (the sound on the avatar through its audio proxy). **(code-verified)**

---

## Phase 4 — quests and scenes

### 4.1 Checkpoint corrections that aren't

**Fix (code-verified).** A checkpoint correction fires the host's last port for a
State this copy has wrong. When that port lands on another value than the
host's (the field: `SetAroundBoulder` took the copy 0 → 3, the host had 15, and
the next checkpoint fired it again with its consequences every 30 s), that
(State, port, host value) is never fired again: `MP-W144 MISMATCH ...: the host
has 15; SetAroundBoulder does not produce it here -- skipped (logged once)`.
The next join loads the State exactly. Unit-tested.

### 4.2 A joiner's scene that never ends

**Detection only (code-verified).** The engine's own lines — a scene added to the
waiting players, the fader faded out while a scene runs — are now in the agent's
log (`MP-W144 engine: ...`, throttled). **Not built:** ending a stuck scene. The
game's movie system has `StopAllCutScenes`, but the stuck scene was the
interactive scene manager's; which call ends it without breaking the quest was
not found, and group 2's logs (where it happened) were not attached.

### 4.3 A forced conversation that dies

**Not done.** The evidence (`Dialog interrupted ... WAITING_FOR_TWINS`, the joiner
unable to move) is in group 2's logs, which were not attached; session 3 has
no such conversation. The lines are logged now (`Dialog interrupted.`, `Dialog
ends but no response was played`), so the next bundle carries them.

### 4.4 An escort follows its leader

**Not done.** It needs a host-side follow order on the host's NPC toward the
joiner's avatar (WO-139's guard lever) driven by the joiner's escort State
changes, and the joiner's copy left to the stream. The save at the bait step was
not prepared.

### 4.5 NPC clothing state

**Half fixed (observed).** The NPC-state placement (WO-141) and the one-shots
(WO-143) cleared the copy's equipment slot; the game then looked for a way back
into the default outfit and found none (`ChangeEquipmentFromDefault has failed!
Action for request 'KCDMP one-shot'`; in session 3, 102, 60 and 40 lines of
`Couldn't find actions to get NPC into game loaded state` on copies undressed for
the night). The copy's own equipment element now goes into the placement as it
is. J2: a copy undressed for the night placed in its bed at once, in step, no
refusal. **Not done:** a copy wearing what the host's NPC wears — the host's
equipment state is not sent (it needs the slot on the WO-141 wire).

### 4.6 Journal marker letters

**Not done.** The joiner's letters run two ahead of the host's: his game lettered
two objectives the host's world never had. A fix needs the journal's lettering
(the UI's order of tracked objectives) read on both sides; not investigated.

---

## Phase 5 — the small things

| item | outcome |
|---|---|
| Avatar sit-down | **Fixed (observed).** The player sits on a bed's edge before he lies down; an NPC body has no way into that (`Couldn't find actions ... sitting Using object: smartObject[Bed/...]`, every try). Lying on the same bed is in step at once, so an avatar is shown lying on it. |
| Joiner host claim | **Fixed.** Session 3 shows no joiner claim (the host's only); the rule allowed one: a non-hosting agent in its own world with the shared world on claimed, including in the moment between a join's end and its joined flag. Now an agent that has heard a host's session mode never claims, and one that has not waits 10 s after connecting. J1–J3: no claim. **(observed)** |
| NullRefs | **Fixed (code-verified).** After the game died, every tick failed with `NullReferenceException`: a command waiting for the pipe used it after the reader had dropped it. It is now read once and treated as a closed pipe; "lost the connection" is logged once. |
| Floating NPCs | **A log line (code-verified).** Every 5 s each streamed copy is compared with the terrain under it; one held 12 m or more above it is logged once per 2 minutes with the stream's height: `WO144-FLOAT npc=... z=... ground=... above_m=...`. Not seen in the runs. |
| Log retention | **Fixed (code-verified).** Before the launcher starts the game it copies the previous launch's `kcd.log` and the DLL's logs into `log-history\launch-<time>` beside its own logs, keeping six launches; Report a bug collects them all. |
| Grindstone blade | **Not done** (no time; WO-143's minigame loop shows the avatar at the wheel without the player's blade). |
| Alchemy anchor | **Not done.** |
| Seated NPC tools | **Kept the rule** (WO-143): a seated copy given a tool is stood up by the game. |

---

## Decisions made unattended

1. **The joiner's clock stands with the host's** (3.3), rather than the host's
   world not pausing in its dialogue: the host's own game keeps its rules.
2. **The pull-back tolerance is one game-minute** (was five).
3. **A talk holds the host's NPC only once the player is in the conversation**;
   "Attempting to start" is not a start.
4. **Dice: the NPC's copy runs its own brain for the game**, the host's NPC held
   meanwhile.
5. **A partner who leaves mid-vote is dropped from the vote**, not counted as a
   no; one with no word yet about where he is stays asked.
6. **A joiner's crime record is kept by name for two hours** after he
   disconnects, so a reconnect finds it.
7. **The WO-137 suite's talk checks now expect the new rule** (told once the
   player is in the conversation).
8. **No quiesce at a load's start** was added for 1.2: the root cause was found
   and fixed; nothing pointed at another one.
9. **The outfit watch reads every 10 s** (two REST reads per avatar).
10. **A copy's equipment state is kept through placements** rather than sent
    from the host (4.5), until the wire carries it.
11. **A hidden horse or animal copy is shown; a parked encounter copy with no
    physics is left alone.**
12. **The bed-edge sit is shown lying.**
13. **The host clock was moved forward** in H2 for daylight frames, and **an
    in-session load drops the save lock**: the L1 load series left four
    autosaves in the throwaway playline (no real playline was touched).

## Where the log disagrees with the work order

* 3.2: the `INVALID WUID` lines are herb gathering and the grindstone, not dice.
* 5, joiner host claim: session 3 has no joiner claim; the rule allowed one, and
  the claim is closed either way.
* 2.3: the refused binds are mostly copies with no physics at all, not bodies
  whose physics is not a living entity.
