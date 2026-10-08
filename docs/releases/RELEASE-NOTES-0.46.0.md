# Kingdom Come: Together 0.46.0 — The joiner's NPC copies keep their context

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

0.45.8 plus the fixes from its first two-player session (WO-160, 2026-10-07). The installer,
`KingdomComeTogether-Setup-0.46.0.exe`, comes from the maintainer. The evidence is `docs/WO-160-findings.md`; what to
check live is `docs/TWO-PLAYER-CHECKLIST.md`, items 150–161.

**Both machines and the relay must run the same build.** It refuses every other version at the handshake.

---

## Fixed

- **The joiner's NPC copies no longer lose their shop, labels and contexts.** Every planner error in the 0.45.8 joiner log
  was on a body the mod had placed: the placement cleared the body's loaded state and then asked the game's planner for a way
  out of a work activity it does not have. The loaded state is kept now, and a placed copy's stance and unstance are released
  the game's own way before a new activity. In a solo town test 13 of 13 bodies were placed with no planner error (the old
  code: 2 of 13) and every shop was kept, including across an 8-hour skip. After a time skip the copies are placed again.
- **No false "You loaded your own save" when joining.** The mod now needs its own recent word that a world is loaded, and is
  silent for 60 s around a join. Loading your own save first and then pressing CONNECT is still caught at once.
- **NPCs are no longer left naked on the joiner's screen, and talks to them start.** A copy is paused, so it never ran its
  own morning: through a night or a wait it kept the game's sleep undress and sleeping state, and was stood up in it —
  naked, and a conversation never started. A copy shown awake whose state still holds the night is now dressed by the game's
  own planner. A placement the game refused no longer leaves behind a demand a talk then waited 20 s for.
- **A conversation stands the NPC still on the other screen.** When one of you talks to an NPC, it stops on the other
  player's screen for the whole conversation.
- **A joiner's own horse comes when he whistles.** A horse bought or bonded by the joiner is told to the host, and his
  whistle puts it beside his avatar (placed, not galloping).
- **Herb gathering** shows a plain bend-and-pick clip on the other screen again (never the minigame's own fragment); mounting
  waits (up to 5 s) for the horse to be ready; a figure that stands in a T-pose after a clip ends gets its walk pulse again.

## Known issues

- Whether an enemy's **sword** shows on the joiner's screen was not investigated (it probably shares the night-undress cause;
  unverified).
- The dressing of naked copies and the talk fixes are verified solo only (a throwaway save); the testers' items 150, 153 and
  154 are their proof.
- None of the two-player fixes has been seen in a live session yet; the herb clip's look, the mount wait and the horse fetch
  were not run live at all.
- Those of 0.45.5–0.45.8 (`docs/releases/`).
- **Not soak-tested** (the maintainer's decision; the waiver is recorded beside the installer). Not signed yet.
