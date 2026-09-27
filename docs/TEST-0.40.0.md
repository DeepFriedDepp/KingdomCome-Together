<p align="center"><img src="branding/banner-1280.jpg" alt="Kingdom Come: Together" width="640"></p>

# Testing 0.40.0 (both players)

**Kingdom Come: Together.** Unofficial. Not affiliated with or endorsed by
Warhorse Studios.

One page for the host and the partner. About an hour. Play normally apart from
the checks in section 4. If something goes wrong, note the time and carry on or
stop, then send what section 6 lists.

0.40.0 is WO-135: **the partner's avatar is a puppet** (seen, never heard),
**knocked-out enemies are shared**, **crouching shows**, **outfits reach both
screens**, and **joins only use saves of your host's game version**.

## 1. Install

Both of you: run `KingdomComeTogether-Setup-0.40.0.exe` (the maintainer sends
it; it is not on GitHub). It installs over 0.30.9. Both computers need it:
0.40.0 refuses every other version.

## 2. Your saves must be from the Modding Tools build

The regular game and the Modding Tools build share one save folder, but they
are different game versions, and a character is only brought into a world of
the **same** version. If you only have saves from the regular game, the launcher
now says so before anything is asked of the host:

> Your saves are from game version X, but your host's game is version Y. Start
> or load a game in the Modding Tools build and save once, then join again.

Do exactly that: start the game through the Modding Tools entry, load (or start)
your game, save once, quit, and join again. If only one of **Bring my
character** / **Start fresh** can work with your saves, only that button is
shown.

## 3. Connect

As before (`docs/TEST-0.30.9.md`, section 2). Then, on both computers, in the
console (`~`): `mp_leash_trace on`.

## 4. The checklist — what each screen should show

**A. The partner's avatar is a puppet** (the host looks at it)

1. **It never talks.** Walk around a village, fight near it, crouch next to it:
   it never barks, greets, shouts when hit, or comments on anything.
2. **It is never a witness.** The host hits an NPC next to it (outside town, a
   bandit): the avatar says nothing and no guard turns on the host because of it.
3. **A guard reacts to the partner only when he is in the fight.**
4. **A guard's (or bandit's) hits hurt the partner.** Partner: your health goes
   down when you are hit, not just your stamina; the avatar on the host's
   screen never blocks unless you blocked.
5. **No T-pose.** In a long fight, the avatar never stands with its arms out.
6. **It never lies in the ground.** If it is knocked down it gets up.

**B. Knocked-out enemies**

7. **Knock an enemy out** (either of you): it goes down on **both** screens,
   in the same place.
8. **Loot it together:** either of you can loot it; what one takes is gone for
   the other.
9. **It wakes on both screens** at the same time, wearing what it has left.
10. **The partner finishes a knocked-out enemy** (the "kill" action on the lying
    body): on the host's screen the partner's avatar does the finishing move,
    and the body dies on both screens.

**C. Crouching**

11. Host crouches: the host's avatar crouches on the partner's screen. Partner
    crouches: the partner's avatar crouches on the host's screen. Both stand
    again when you stand.

**D. Outfits**

12. Each of you changes two pieces (for example a gambeson and plate over it):
    the other screen shows both, within a few seconds, also in the middle of a
    fight.

**E. Joining**

13. A partner with only regular-game saves gets the plain message from
    section 2, and the host is never paused for it.

## 5. Expected not to work yet

Shared crime (the partner's own crimes, fines, jail), sleep voting, quest sync.

## 6. If it goes wrong / afterwards

Both machines, before relaunching the game: `kcd.log`, the game folder's
`kcdmp-native.mirror.log` and the launcher's log bundle (**Report a bug**).
Useful lines: `WO135-DIALOG`, `WO135-QUIET`, `WO135-KO`, `WO135-TAKEDOWN`,
`WO135-CROUCH`, `WO135-STANDUP`, `[appearance]`, `MP-HENRY joiner`.
