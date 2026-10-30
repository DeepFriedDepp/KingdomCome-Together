# Kingdom Come: Together 0.63.9 beta — tester script

Two players, one host and one joiner, both on the **same 0.63.9 build** (the build id is in the launcher's About and in the installer's
name; a partner on another build is told so at the connection). If anything below looks wrong, press the **period key** at that
moment. To say what you saw, open the console (~) and type `mark <what you saw>`: the note reaches the other player's bug report too.
Send both bug reports after the session; they carry the build id.

The first five steps are the beta's fixes. Please do them first.

## 1. A dropped connection (the joiner)
- **Joiner:** while in the host's world, pull your network cable or turn Wi-Fi off for about 10 seconds, then back on.
- **Expected:** "Connection to your host lost -- reconnecting... (n s)" on your screen; you cannot walk meanwhile; the world's clock
  stands. When the link is back you carry on where you stood -- no loading screen, no jump. On the host's screen your figure stands
  still with "(reconnecting)" over it and carries on when you are back. The host keeps playing.
- **Then once for longer:** keep the network off for more than a minute (or open the co-op menu, Insert, and choose **Leave the host's
  world**). **Expected:** your progress in the host's world is saved first, then your own game loads with your character as it was at
  the drop (your health, your things, where you stood). If that save could not be made, the game tells you so in words.
- **Mark** a load or a jump after a short drop, a character that came home older than he left, or no message.

## 2. The host's menus and a joined talk (both)
- **Host:** open and close the map and the inventory a few times, then start a talk with someone the joiner listens to (the
  blacksmith in Tachov was the field's), vote and pick through it, and open the map again right after.
- **Expected:** no crash. (The field's crash came as the host's map closed: fixed.)

## 3. A tavern (the joiner)
- A few minutes among seated drinkers and dice players, the innkeeper serving.
- **Expected:** the seated stay seated while they sit on the host's screen, through their drinking and talking; nobody stands up and
  sits down again over and over; nobody in a T-pose.
- **Mark** a seated person who keeps getting up and sitting down, or a T-pose.

## 4. Hitting each other with friendly fire off (both)
- With friendly fire **off**, in a town where a guard can see you, hit each other a few times.
- **Expected:** no crime, no fine, for either of you. With **shared crime** on, a crime of your partner's still counts against you too
  -- and the game now says so: "Shared crime is on: the guard holds you to account for your partner's lockpicking" (the field's 60
  groschen fine was the host's own lockpicking, charged to both).
- **Mark** a fine you cannot explain.

## 5. Flying (both)
- Press the Modding Tools' flight key (F3) in a session.
- **Expected:** nothing happens but a message: "Flying is turned off in co-op: it breaks the people around you." Testers who need it:
  `mp_allow_fly on` in the console (and `mp_allow_fly off` after).

## 6. A fight at sword's length, a kill, a loot
- **Joiner:** two bandits on you; block, parry, dodge as in your own game; kill one. **Both:** search the body.
- **Expected:** the bandits fight on both screens alike; the body dies the game's own way and both of you can search it.

## 7. A shared sleep in a village, then the morning
- **Expected:** the sleep ends together, the clocks and the skies agree; in the morning nobody lies in a bed while walking on the
  host's screen.

## 8. Mark anything odd
- The period key and `mark <what you saw>`, as above.

## Known in this build
- Weapons can appear in hands, sometimes late.
- Some fleeing people can T-pose.
- Grabs (clinches) do not happen between players and the host's people.
- Carts do not move on the joiner's screen, and their riders walk.
- In a joined talk the listener can miss his head, and some camera shots are off when the person stands differently.
- The host's memory climbs while the host waits alone for the partner: join within a couple of minutes.
- Flying is off by default (step 5).
