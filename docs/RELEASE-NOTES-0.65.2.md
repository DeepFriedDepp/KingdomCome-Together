# Kingdom Come: Together 0.65.2 beta

**Official repository: the project's own GitHub repository, KingdomCome-Together** — only builds made from it are this project's
releases; other repositories may carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or PLAION. Kingdom Come: Deliverance II, its assets and its
content belong to Warhorse Studios and PLAION; this project's copyright covers only its own code.

The installer is `KingdomComeTogether-Setup-0.65.2-<build id>.exe`. What to try together is in `docs/TESTER-SCRIPT-0.65.2.md`; what
we know is not right yet is in `docs/KNOWN-ISSUES.md`; the evidence is `docs/WO-221-findings.md`.

**Both players must install the same build.** Builds are told apart by their **build id** (the launcher's About shows it, and so does
the installer's name); a player on a different build is told so at the connection.

## This build: bodies that walk right, and a horse each

Everything below is **on** by default. Each part has its own switch (`mp_w221_status` in the console lists them); the tester
script's last section names them.

### People walk on their own feet

- **A walking villager on the joiner's screen is walked by the game itself.** Until now the mod placed every one of the host's
  people many times a second and played a walking animation on top: feet slid, steps were too slow for the speed, and now and then a
  person was pulled back. Now the joiner's game is told where each walking person is going and walks them there with its own legs —
  the same way it walks its own villagers. On our test bench the step of a walking copy matches the host's person's (it was about
  two thirds too slow), and a planted foot slides about a third as far as before. People who sit, work, fight or stand still are
  placed as before.
- **People the game itself holds somewhere** (a guest it keeps putting back on a seat, a worker at a spot) are left where the game
  holds them instead of being fought over many times a second.

### The fight crash

- **The joiner's game closing during or after a fight** — most often when the players came back to a place where a fight had been —
  had one cause: an attack the mod had queued on a person was run after the game had already put that person's fighting part away.
  The mod now ends every action it queued on a person at the moment the game puts that part away, when the person dies and when the
  session ends. See Known for what our removal test still shows.

### The joiner's talk and trade

- **Shops open for the joiner.** A trade with a shopkeeper who was at work ended with no shop screen in 0.65.0 (five trades of
  twelve on our bench). The joiner's copy of a shopkeeper now keeps the shop the host's shopkeeper keeps.
- **A talk starts even with someone busy.** A person woken for a talk no longer runs off to their own job first; if the plain
  request is not taken within a few seconds the talk is started the game's forced way.
- **A talk no longer ends by itself while you are choosing.**
- **Selling works**: the item leaves the joiner, the money arrives, once.

### A horse for each player

- **The joiner can buy a horse, and it is his.** The purchase runs in the host's world too: the horse is sold there, the price is
  paid once (by the joiner), and the host's own horse is not touched.
- **Each whistle calls that player's own horse**, on its own legs.
- **Riding together**: each player sees the other on the other's horse.
- **Saddlebags**: the owner's horse inventory is the horse's own in the host's world; what the partner puts in or takes out goes
  through the same ledger as loot — one item, one owner, a request with no answer changes nothing.
- **The horse stays the joiner's** when the host saves and loads, and when the joiner leaves and comes back: the host's mod keeps a
  record of which horse belongs to which player, paired with each of the host's saves (in the mod's own data folder, beside the
  chest ledger; the game's save files are not touched).
- **The horse stays where it is** while its owner is away.

### The dead, carts, and drops

- **The dead lie.** A body that died while the joiner was elsewhere lies on the joiner's screen as it lies on the host's, with the
  host's body's wounds and clothes — not standing, not with arms straight out.
- **Carts:** the riders sit, the wheels turn, the horses are in front of the cart.
- **An item dropped out of a horse's inventory** lies on the ground for both players; whoever picks it up first has it.

### The host's frame and the bug report

- **The bug report's recorders write their files on a thread of their own.** The host's worst frames spend about a quarter less time
  in the mod than in 0.65.0; the target we set ourselves is not met (see Known).
- **The bug report's briefing counts only this launch of the game** (it used to add up everything in the log files).
- **The host no longer reads and sends walking routes nobody uses** (`mp_route` is off by default; a joiner who turns it on gets
  them again).

## Fixed

- **The fight crash** on the joiner's side (above).
- **"The trade fails"**: no shop screen with a shopkeeper at work; a talk that never started with a busy person; a talk ended by the
  mod while the joiner was choosing.
- **A joiner's bought horse was lost** at the next join, and his whistle fetched the horse instead of calling it.
- **Dead bodies standing** on the joiner's screen when he had not seen them die.
- **Cart riders standing on the cart, wheels not turning.**

## Known

The short list; the full one is `docs/KNOWN-ISSUES.md`.

KNOWN_SHORT_LIST

## How to report

Press the **period key** (`.`) the moment something looks wrong, then type `mark <what you saw>` in the console (~): the note reaches
the other player's bug report too. After the session **both players** send their bug reports. Every report carries the build id; say
which build you played if you write to us.
