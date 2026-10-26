# Kingdom Come: Together 0.63.7 — fewer fight crashes, joined conversations that stay joined, the world clock in step

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

Everything on `main` from WO-207 to WO-208 (2026-10-25/26). The installer, `KingdomComeTogether-Setup-0.63.7.exe`, comes from the
maintainer. The evidence is `docs/WO-208-findings.md`; what to try together is `docs/WO-208-tester-script.md`.

**Both machines and the relay must run the same build.** 0.63.7 refuses every other version at the handshake.

---

## Fixed

- **The joiner's game crashed in some fights.** An enemy that had just been released from the joiner's fight was given the joiner
  back as its opponent a moment later, outside any fight of theirs; the game then crashed evaluating its blow. An enemy now takes the
  joiner as its opponent only while the mod holds it in the joiner's fight and the game agrees they are fighting. `mp_target_in_fight`
  (on).
- **The partner was dropped from a conversation for silence.** Some conversations (the bailiff's) have no dialogue camera the game will
  use; the starter's game then sent no picture and the listener's view ended after 3 seconds. Such a talk now shows the starter's own
  view. `mp_conv_cam_player_view` (on).
- **Keys pressed twice while listening; Back opening the pause menu.** A player still inside a conversation of their own no longer joins
  the partner's (their own talk first). `mp_listen_own_talk_first` (on).
- **Your own head while listening.** The listener's own character keeps its head in the shots. `mp_listen_show_head` (on).
- **The joiner's clock ran ahead while the host talked.** The host's clock stands during its conversations; the joiner's now stands with
  it instead of running on and being pulled back in jumps. `mp_clock_stands_with_host` (on).
- **Quest steps arriving minutes late on the joiner.** One quest step that could not apply yet held every later one for 30 seconds
  each; now it waits aside and the rest arrive in order. `mp_quest_wait_aside` (on).

## New

- **The joiner's own blocks against the host's enemies** (`mp_defence_local`, on): a blow that lands on the joiner's own character is
  judged by the joiner's own game (block, perfect block, guard broken) and the host takes that judgement; a blocked stab makes the enemy recoil
  on the host's screen too (`mp_defence_recoil`, on). Most enemy blows still reach the joiner as the host's game judged them (see Known).
- **Conversations that start by themselves take the partner in** (`mp_conv_autojoin`, on): when a quest or a character starts a
  conversation with one player, the other player within 30 m listens in without pressing anything (not while in a conversation of
  their own or a fight). Back leaves it, as before.

## Known

- **Enemies fighting the joiner.** On the joiner's screen the host's enemies still aim their own fighting at the host's character, and
  their swings at the joiner are shown as animations: few of their blows land on the joiner's own character, so most blocks are still
  judged by the host's game, and lock-on rarely takes an enemy that fights the joiner.
- **The host's people on the joiner's screen.** Sleeping and sitting people can stand on the joiner's screen, and work animations
  (an innkeeper serving) often do not play there. The single animation owner that fixes most of this is built but off
  (`mp_copy_anim_owner`): its test got close but missed its bar.
- **Quest areas.** A place a quest waits for you to reach counts only the host's character.
- **After a long shared sleep** the joiner's sleep screen can run on for up to a minute after the host's; the clocks meet
  afterwards.
- **A follow-up conversation twice.** When the joiner's conversation leads straight into a conversation the quest starts by itself,
  the host can get the same follow-up on their own screen at the same moment; each player then has it on their own machine.
- **No dialogue camera.** A conversation the game shows without its dialogue camera now shows the listener the starter's own view.
