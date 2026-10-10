# Kingdom Come: Together 0.48.2 — the loot crash, talking, enemies that fight the joiner, weather

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

0.48.0 plus WO-166 (2026-10-10), versioned 0.48.2 by the maintainer. The evidence is `docs/WO-166-findings.md`; what to check live is
`docs/TWO-PLAYER-CHECKLIST.md`, items 212–223 (and the earlier ones still pending).

**Both machines and the relay must run the same build.** It refuses every other version at the handshake.

---

## Changed

- **The loot crash (joiner).** The host's change to a body you were looting rewrote that body's items under the open loot screen
  and could crash the game. Now the change waits until the screen closes (the game's own close event, or the close sound, or you
  walking away) and is applied one frame later; a take the host refused is put back after the close too.
- **Talking to the host's people (joiner).**
  - A talk the mod starts for you (the fallback request, a forced conversation) never blocks your own: it is not made while you are
    in a conversation, just out of one, or waiting for your own; your own press ends any of the mod's that has not started yet.
  - The person you press talk on is resumed first and your press goes on 200 ms later; while your request waits and while the
    conversation runs, the host's stream no longer drags that person along with the host's NPC. `mp_talk_resume_first on|off`
    (default on).
  - Someone talking to the host or fighting there says "They're busy with your partner." instead of leaving a stuck request.
  - A talk that never starts writes one line with the engine's last dialogue state and what the stream did.
- **Quest values (joiner).** A value of a quest part that is asleep (hibernated) is now written — the quest reads it when it wakes —
  and whole-number values the quest files declare unsigned (the smith's sword quality among them) are accepted. The agent also
  reads the reply's type at its real position and checks the type the quest file declares.
- **Enemies that fight the joiner.**
  - A blow whose swing the host never captured is no longer dropped as "stale": the generic lunge plays before its damage, and a
    real swing is accepted later on a slow link (its own timing plus half the measured round trip).
  - The host's enemies count as attacking on the joiner's screen while they swing (the copy's combat state is Striking for the
    swing's own window, only from idle or guard, put back afterwards), so the joiner's game can offer its block and riposte
    prompts. A blow the copy would land in the joiner's own game is put back and counted; the host's game still decides every
    blow. `mp_copy_strikes on|off` (default on).
  - Snapping after long holds: 88 % of the large snaps in the testers' fights came at the end of a long hold. A swing's hold now
    follows the swing's own timing, and a chain of holds is capped at 1.2 s. `mp_snap_fix on|off` (default on).
  - `mp_victim_decides` is **off** again (it was on in the second 0.48.0 build): its proof needs a person holding block.
  - The lock-on's "not set" line is written once per enemy per minute.
- **Weather.** The joiner's rain follows the host's (the host sends its own computed rain; the joiner's game holds it through the
  engine's own rain override for the session).
- **Fast travel.** The "fast travel is blocked" message only shows after you confirm a trip, and once per session.
- **Map pins.** One pin per live partner (a rejoined partner's old pin goes within 2 s), never for yourself, with the companion icon.
- **Enemies near the joiner.** The host keeps streaming the people around the joiner while its own position loop is stopped (dead,
  loading, held black).
- **Respawn.** When a player wakes after a death, the NPCs that struck him in the last two minutes and every NPC still fighting where
  he fell get the game's own "stop fight". The crime record is untouched.
- **Join through Steam.** A friend whose game is still starting shows as "(starting...)"; the list looks again every 10 seconds
  while the dialog is open.
- **For testers:** new log lines `WO166-LOOT`, `WO166-TALK`, `WO166-STRIKE`, `WO166-LOCALHIT`, `WO166-C1`, `WO166-WEATHER`,
  `WO166-FASTTRAVEL`, `WO166-AMNESTY`, `WO166-SCAN`, `WO166-MAPMARK`, `MP-WO166-STATS`; `WO164-QFIX` names `type=` and `type_def=`.

## Known issues

- **Checked unattended with a stand-in partner only** (2026-10-10, a throwaway save, nobody at the machine, the game in the
  background). The checklist's items 212–223 are the proof with two people.
- **Enemies that never swing at the joiner's figure** (seen once in a real session) were not reproduced: in the test, guards struck
  the figure about every 1.5 s whatever the figure's combat automation. A new host line (`WO166-C1`) names what both sides read
  when it happens again.
- **The blacksmith who never answered** and the frozen haggle were not reproduced with scripted presses; the new talk line says why
  when it happens.
- **Not soak-tested** (the maintainer's standing rule, stated in WO-161 on 2026-10-08; the waiver is recorded beside the installer).
  An unattended 30-minute fight with three enemies ran instead: no fault, no blow applied twice. The mod's own cost per frame against
  0.48.0 on the same machine: main menu −1 %, town +2–3 %, a three-enemy fight +3.5 % (0.69 → 0.72 ms). Not signed unless the build
  says so.
- **Respawn and crimes:** the amnesty never touches the crime record. When the joiner's figure assaulted someone, the host is still
  held answerable for it too (joint responsibility since 0.43.0; `mp_crime_mode individual` turns that off), so guards can come for
  the host after the joiner wakes.
