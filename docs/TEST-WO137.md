# What changed in WO-137 (for both players)

**Kingdom Come: Together.** Unofficial. Not affiliated with or endorsed by
Warhorse Studios.

This comes with the next installer (WO-140). The checks to do together are in
`docs/TWO-PLAYER-CHECKLIST.md`, section WO-137. In plain words:

- **Quests are shared.** Every quest — main quests and side quests like
  "Find Mutt!" — lives in the host's world. When either of you makes a step
  (examines something, picks something up, finishes a conversation), both
  journals update.
- **The partner can talk to people.** Talking to someone in the host's world
  now works for the partner: the conversation plays on their screen, and what
  is decided in it counts for both. While the partner talks to someone, that
  person waits for them in the host's world too.
- **No step twice.** If you both do the same thing, it counts once.
- **Dead stays dead.** A body lying dead in the host's world lies dead for the
  partner too — also bodies the story placed, and people the story kills.
- **Rewards are your own.** Each of you gets what a step gives on your own
  character.
- **Off switch:** the host can type `mp_quest_sync off` in the console (`~`)
  to stop sharing quests at once, and `mp_quest_sync on` to start again.

Still known: a surrendering enemy does not kneel on the partner's screen yet;
a conversation topic you have used can still be offered to the other player
(asking again changes nothing in the quest); cutscenes play only for the
player who triggers them.
