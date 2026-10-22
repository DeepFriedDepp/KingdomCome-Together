# WO-203B tester script — joining your partner's conversation (0.62.5)

Two players, one host and one joiner, both on 0.62.5, standing near each other in the same world. If anything below looks wrong,
press the **period key** at that moment and say in a few words what you saw. Both games mark the moment.

How it works:
- **Joining:** when your partner talks to someone, look at your partner or at the person they talk to and press your talk key on
  **Join conversation**.
- **What you see:** your partner's dialogue camera, cut for cut, with black bars at the top and bottom.
- **Leaving:** press Back (Esc).

## 1. The host talks to Barbora (or anyone with a longer conversation)
- **Host:** talk to the NPC as usual.
- **Joiner:** walk within a few steps, look at the host or the NPC, press talk on **Join conversation** at the start.
- **Expected (joiner):** black bars, no compass or health bar, and the same shots as the host's screen at the same moments: the
  same angle, the same person in the same part of the frame, each cut at the same time (within a blink). Say out loud when your
  shot changes; the other player confirms.

## 2. The joiner talks to an NPC; the host joins in the middle
- **Joiner:** start a conversation.
- **Host:** after a few lines, walk up and press talk on **Join conversation**.
- **Expected (host):** within a moment your view jumps to the joiner's current shot, then follows every cut.

## 3. A three-person conversation (Semine and Gnarly), started by either player
- **Expected:** the listener's shots follow the starter's through all three speakers. The framing stays steady when the speaker
  changes (it never jumps to a different NPC as its centre).

## 4. The listener tries to look around with the mouse
- **Expected:** nothing moves. Not the view, not your own character.

## 5. Leave mid-conversation with Back (Esc)
- **Expected:** your own first-person camera at once, the bars gone, the compass and health bar back, and mouse-look working right
  away. The pause menu does not open on that press.

## 6. A fight near a conversation (or get hit while listening)
- **Expected:** the listener's own camera comes back at once.

## 7. A shared cutscene during a conversation (if one comes up)
- **Expected:** the cutscene plays, then the conversation's shots come back if the conversation is still going.

## Known in this build
- If your own character is in one of the shots, he appears **without a head**. Press the period key when you see it, so we know how
  often it happens.
- No subtitles, choices or votes on the listener's screen yet: you see the camera only.
- The NPC may stand somewhere else on the listener's screen than on the starter's: the camera still matches.
