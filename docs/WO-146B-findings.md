# WO-146B — Retail probe, part B: a research build of the plugin, run in the retail game

**Live, native, research only. Nothing was ported.** A throwaway probe DLL (`native/experiments/wo146b_retail_probe/`, never linked into the shipped plugin)
was loaded into the maintainer's own retail game process with the project's existing injector, in single player, in the throwaway playline.
No `VERSION` change, no installer, no tag, no release; the shipped DLL, agent, Lua, launcher and installer are untouched.
Companion: [`WO-146B-progress.md`](WO-146B-progress.md) (runs, everything touched outside the repo, the saves folder before and after, the privacy sweep).
Background: [`WO-145-retail-census.md`](WO-145-retail-census.md) (the static census this part measures) and [`WO-146A-findings.md`](WO-146A-findings.md) (the Lua/pak/console half).

Placeholders: `<RETAIL>` = the retail install (Steam app 1771300, game 1.5.6, `Win64MasterMasterSteamPGO`), `<MT>` = the Modding Tools install (app 2429020, 1.5.5),
`<saves>` = the game's Saved Games `saves` folder, `<scratch>` = the session's scratch folder outside the repo.
Addresses are written as `WHGame+0xRVA`; the probe never uses a hard-coded RVA, it re-finds everything by anchor and logs where it landed.
Evidence marks: **(observed)** seen live in this run; **(code-verified)** read in our code; **(data-verified)** read in a binary; **(inferred)**; **(inconclusive)**.
World: `playline2/autosave028` throughout, the throwaway playline part A names; checked before every step (`wh_sys_LastLoadedSave`). **(observed)**

---

## 0. The answer first

### Go / no-go

**Go with conditions.** Nothing found is a wall, and the part of the port that decides viability — getting our own code to run, on the right thread,
able to see the world — works on retail today. What the probe also measured is that the *layout* half of the port is harder than the census's
class B suggested: the anchors we have do not carry over, and several clusters have to be re-derived from the running game rather than translated.

The three deciding facts:

| | fact | verdict | mark |
|---|---|---|---|
| **Foundation** | The probe loaded, the build-identity gate passed, `C_ModulesManager::Update` was found from a string anchor with no RVA, hooked, and ticked **once per frame on one thread** for twenty minutes (98 fps focused, 30 fps unfocused). `gEnv` was derived from the same anchor chain and every one of its 43 members named by its own RTTI, including the entity system (+0xA0), the console (+0xA8), the job manager (+0x128) and the **main thread id (+0x1A8)**. The entity system answered live: `GetEntity(0x7777)` → `CEntity`, name `Dude` — the same value part A read through Lua. | **works** | **(observed)** |
| **Drift** | The `C_Actor` vtable has **275 slots on retail against 409 on the Modding Tools build**. Slots up to ~95 are unmoved; after that they shift by −24, then −86, then −134. **Not one** of the plugin's four `UpdateMannequinTags` in-body byte checks matches any retail slot, and **not one** of the nine `C_Actor` methods the plugin uses shares a single 7-byte code run with any retail slot function. The tools build's `m_pCombatActor` offset (+0x300) holds a float on retail. | **works with changes — the expensive kind** | **(observed + data-verified)** |
| **NPC-state** | Reachable. `C_NPCContext` still has no RTTI, but every element class does, and the context is found *structurally*: from the entity to `C_NPC@xgenaimodule` (two pointer hops), then the offset whose `+0x90` is a state whose element vector holds objects with the element vftables. On retail that offset is **`C_NPC+0x740`** for the current state and **`+0x7E0`** for the required one, not the tools build's `+0x9C0`. One NPC's stance was read from both sides and agreed. | **works with changes** | **(observed)** |

### The conditions

1. **A retail build is a re-derivation, not a translation.** Every vtable slot, struct offset and prologue in the native layer has to be found again
   on retail by what it *does*, not by what it looked like on the Modding Tools build. Signature-porting from `<MT>` does not work (section 3.3).
2. **Two builds must not share one set of expected layouts.** The plugin's fail-closed pattern already exists; a retail build needs its own table
   and its own identity gate (the probe's gate is the shape: hash the game DLL, arm nothing on a mismatch).
3. **The combat capture is unproven.** Its hook points resolve and install; no combat could be produced from the console in the time available, so
   whether they capture is still open (section 5).

---

## 1. Setup and the safety gate

The probe is one DLL with its own project. It writes only next to itself (outside both game installs), is driven by a text command file polled by its
own worker thread, logs the **thread id on every line**, and arms nothing until the game DLL on disk hashes to the value WO-145 recorded. **(code-verified)**

```
ID: module base 00007FFE173C0000, SizeOfImage 0x5B2D000
ID: file size 89180672 (expected 89180672)
ID: sha256   bdf8f9e4a11257a72b64c84700e284c29e4c4ccaf5b8d4bfa7d0b2a7294479f7
ID: MATCH -- the retail 1.5.6 build WO-145 measured
ID: section .text 0x3A01DA6 | .rdata 0xECF17A | .data 0xDB6650 | .pdata 0x356EB0 | _RDATA 0x13210
```

Loading it was the project's existing injector, unchanged (`--pid <pid> --dll <path>`): `injected ... into pid <pid>`, exit 0. **(observed)**

**The game did not refuse the plugin and behaved as if nothing had happened**: no error, no dialog, no check fired, no log line about the injected module.
The only crash in the session was the probe's own bug (section 6). Nothing about DRM, Steam, entitlements or the network anti-cheat classes was read or touched. **(observed)**

---

## 2. Step 1 — it ticks (census D-007). **Verdict: works**

### 2.1 The anchor chain, with no RVA in it

| step | how | result | mark |
|---|---|---|---|
| `C_ModulesManager::ProcessMessage` | the one function in `.text` that references the string `wh::framework::C_ModulesManager::ProcessMessage` (exactly 1 referencing function) | `WHGame+0x479BEC` | observed |
| `gEnv` | the first `mov rcx,[rip+d]` inside that function names `gEnv.pLog`; the Modding Tools build loads `gEnv` and then reads `[gEnv+0xC0]`, retail folds both into one absolute load, so `gEnv = that address − 0xC0` | `WHGame+0x492D800` | observed |
| `C_ModulesManager::Update` | the **one** place in `.text` holding the module-update loop `mov rcx,[rbx]; movaps xmm1,xmm6; mov rax,[rcx]; call [rax+0x20]` (1 hit), taken back to its `.pdata` root, then cross-checked: the function must itself reference `gEnv+0xF0` | `WHGame+0x532F7C`, 121 bytes, cross-check **yes** | observed |
| the prologue | 18 bytes, compared byte for byte before patching | `48 89 5C 24 08 57 48 83 EC 30 48 8B F9 0F 29 74 24 20` — as expected | observed |

Both the `gEnv` base and the `Update` address were also derived statically from the two binaries before the run and matched exactly. **(data-verified)**
The chain was re-run in a second, fresh game process later in the session and produced the same three addresses. **(observed)**

### 2.2 The tick

```
S1: frame hook INSTALLED (ok)
TICK: first frame -- C_ModulesManager this=000002B1AC3B2B80
TICK: frame 1 .. frame 10          (all on one thread)
TICK: second  1 -- 99 frames (99 in this second)
TICK: second  2 -- 197 frames (98)
TICK: second  3 -- 295 frames (98)
TICK: second  4 -- 393 frames (98)
TICK: second  5 -- 491 frames (98)
```

* **Once per frame, one thread, for the whole run.** The first ten frames and every per-second count came from the same thread id; the plugin's
  whole main-thread model (every queued task, every `run_sync`, the outbound sampler) rides exactly this. **(observed)**
* 98 frames/s with the window focused, 30/s with it pushed down (the game throttles itself). The second process ticked at 30/s throughout. **(observed)**
* The hook survived twenty minutes, a level's worth of NPC activity and several hundred queued reads without a fault. **(observed)**

**What this replaces.** On the Modding Tools build this is an IAT patch on `WHGame.dll`'s import of `Framework.dll!?Update@C_ModulesManager@...`.
Retail has no such import and no `C_ModulesManager` RTTI, so it is an inline detour on a function found by a code-shape anchor. The start-up gate the
plugin uses today (`GetModuleHandleA("CrySystem.dll")` polled for 60 s) never passes on retail and has to be replaced by this identity check. **(code-verified)**

---

## 3. Step 2 — it can look around (D-183, D-010). **Verdict: works with changes**

### 3.1 `gEnv`: better than expected

Every member that carries RTTI was named by reading its vptr → complete-object locator → type descriptor. 43 of them resolved. **(observed)**

| offset | class | why it matters |
|--:|---|---|
| +0x090 | `C_Game@game@wh@@` | the game object |
| **+0x0A0** | `CEntitySystem` | what the plugin's `engine.cpp` already assumes |
| **+0x0A8** | `CXConsole` | same |
| +0x0C0 / +0x0C8 | `CSystem` | |
| +0x0E0 | `CLog` | |
| +0x0F0 | `CMovieSystem` | the value `C_ModulesManager::Update` reads, which cross-checks the base |
| +0x108 | `CD3D9Renderer` | |
| **+0x128** | `CJobManager@JobManager@@` | the job system |
| +0x160 | `CThreadManager` | |
| **+0x1A8** | — | **the main thread id** (a dword equal to the thread the frame hook runs on) |

Also present and named: `C3DEngine`, `CNetwork`, `CScriptSystem`, `CPhysicalWorld`, `CFlowSystem`, `CDXInput`, `CCryPak`, `CParticleManager`,
`CTimer`, `CCryFont`, `CAISystem`, `CharacterManager`, `CAudioSystem`, `CNameTable`, `CHardwareMouse`, `CMaterialEffects`, `CFlashUI`,
`CScaleformHelper`, `CRemoteCommandManager`, `CServiceNetwork` and the plugin manager.

**The two offsets the plugin hard-codes (+0xA0, +0xA8) are right on retail.** The `gEnv` lift the plugin uses today is not: it reads the slot out of
`C_PlayerModule::ValidateAlcoTeleportPoints`, found by a string that does not exist in retail (WO-145 §3.5). The `ProcessMessage` route replaces it. **(observed)**

### 3.2 The entity system answers

```
S2: gEnv+0xA0 -> IEntitySystem 000002B2B3260040 (.?AVCEntitySystem@@)
S2: GetEntity(0x7777) -> 000002B1BC74FF00 (.?AVCEntity@@) name=Dude
S2: entity id 675 -> ttkc_home8Hub_1 | 1587 -> ttkc_dog_4 | 2049 -> ttkc_woman_8 ...
```

`IEntitySystem` vtable slot 0x70 (`GetEntity`) and `IEntity` slot 0x90 (`GetName`) both do on retail what the plugin expects, and the player entity id
`0x7777` is the same magic value. The name matches part A's Lua read exactly. **(observed)**

### 3.3 Reflection: the registry is readable; the entry points are not where we look

This is the part that took three probe builds, and the failures are the useful result.

1. **The registration-string route does not land on `get_by_name`.** The string `wh::rpgmodule::Soul` exists once and is referenced by exactly one
   function (`WHGame+0x1D56E0`, the type's `RTTR_REGISTRATION`). The first call after the string's own `lea` is **not** `type::get_by_name` but
   `rttr::detail::type_register::custom_name(type, string_view)` — the two compile to the same shape (fetch the registry singleton, forward two
   arguments). Called as if it were `get_by_name`, it takes an access violation at `WHGame+0x4F3574` reading `[*arg2+0xC0]`, which is `type::is_valid`'s
   body: the argument is an *input* type, not an output buffer. **(observed; the cause read from the disassembly, data-verified)**
2. **The singleton accessor is the hub, and it works.** `WHGame+0x4FA050`, called and returning the registry, a static object in `.data`. **(observed)**
3. **A code-shape discriminator picked the wrong map.** The one function in the image with `lea rdi,[rax+0xD0]; mov rcx,rdi` that also calls the
   accessor (`WHGame+0x6A6DF0`, 89 bytes) is a name lookup — but of the registry's **global-item** map: 148 entries, keys like
   `wh::playermodule::GameOver`, `wh::conceptmodule::PassLongTime`, `wh::rpgmodule::GetGameMode`, `ReplacePlayerHorse`. Every type name we tried
   returned the invalid-type sentinel from it. **(observed)**
4. **The type map reads directly, and that is the answer.** `registry+0x10` is a flat map: keys `std::string` at a 40-byte stride, values `type_data*`,
   **7,358–7,369 entries** (it varies with the session). Reading it directly finds the type without calling the game at all:

   ```
   S2: custom-name map: 7369 entries, key stride 40 bytes
   S2:   "wh::rpgmodule::Soul" is entry 5818 -> type_data 000002B1A939A1B0
   ```

   The `type_data` it yields has the layout the plugin's hand-modelled ABI already assumes: the validity byte at **+0xC0** reads 1, the name length
   `0x13` (= 19 = `strlen("wh::rpgmodule::Soul")`) sits at +0x28, and the metadata `std::function` at +0xA8 names itself through RTTI. **(observed)**

**What this means for the 71-row reflection cluster.** The data is all there and the layouts match. What is *not* there is most of the API: of 22
entry points the plugin resolves by export on the Modding Tools build, only **3** have a standalone function in retail that a masked code signature
from `<MT>` finds (`get_by_name`'s shape, `variant::create_associative_view`, the associative-view iterator's `operator*`). The rest — `type::is_valid`,
`get_property`, `get_property_value`, `variant::is_valid`, `~variant`, `property::get_name`, `argument`'s converting constructor — have no standalone
function to call: they are header-inline in RTTR and the retail build inlined them into their call sites. **(data-verified; the "because they are
header-inline" explanation is inferred.)** A retail build therefore reimplements the RTTR *client* against the data layout instead of calling wrappers.
That is a different job from the one the census priced, and on the evidence here a smaller and safer one.

**Not done:** reading an NPC's health through reflection. The name was read through the entity system (above) and matches part A; the health read needs
the property path, which is exactly the reimplementation just described. **(inconclusive)**

---

## 4. Step 3 — layout drift (D-095, D-415, D-369). **Verdict: the anchors do not port**

### 4.1 The vtable

`C_Actor`'s RTTI resolves to exactly one primary vftable on retail (`WHGame+0x3E74198`) — and it has **275 slots where the Modding Tools build has 409**. **(observed)**

Matching the two vtables slot by slot on the strings each slot's function references (static, both builds) gives the shape of the shift: **(data-verified)**

| region (Modding Tools slot) | retail slot | shift |
|---|---|---|
| 7, 13, 15, 48, 95 | same | **0** |
| 183 | 159 | −24 |
| 296, 298 | 210, 212 | −86 |
| 382, 384, 392 | 248, 250, 258 | −134 |

Only 11 of 410 slots matched at all by that method; the other 399 share no usable string. The Modding Tools build carries 134 virtuals retail does not
(editor and test surface, **inferred** from the size difference and from which modules the extra classes live in).

### 4.2 The plugin's own checks, measured against retail

The drift table the work order asks for. "Exact check" is the byte string the shipped `motion.cpp` requires; "relaxed" is the same instruction with the
displacement left free, which reports what the offset *became*. **(observed)**

| what the plugin anchors | Modding Tools | exact check on any retail slot | relaxed | reading |
|---|---|---|---|---|
| `SetPseudoSpeed` — `mov rax,[rbx+0x7E8]` + `movss [rax+0x18],xmm6` | slot 137 (+0x448) | **no match anywhere** | first slot with `mov rax,[rbx+d]` is slot 8, d=0x250 (4 matches) | not identified |
| `ExpHolder` — `mov rax,[rbx+0x308]` | slot 304 (+0x980) | **no match** | same ambiguous head | not identified |
| `UpdateMannequinTags` — `mov rdx,[rcx+0x450]` | slot 403 (+0xC98) | **no match** | 27 ambiguous matches | not identified |
| … its `movss xmm1,[rdi+0x574]` (requested velocity) | | **no match** | **slot 151, d=0x4D8, exactly 1 match** | the only non-ambiguous hit in the table |
| … its `movsd xmm8,[rdi+0x614]` (move vector) | | **no match** | no slot has that instruction form at all | |
| … its `lea rcx,[rdi+0x850]` (stance manager) | | **no match** | 4 ambiguous matches | |

And a stronger test, run offline: for each of the **nine** `C_Actor` methods the plugin uses (`GetAnimatedActor`, `SetPseudoSpeed`, `GetName`, `GetSoul`,
`GetOrCreateCombatActor`, `ExpHolder`, `IsCrouched`, the stance component, `UpdateMannequinTags`), take every fixed 7-byte-or-longer run of the Modding
Tools function with relocatable operands masked out, and look for it in **any** of the 275 retail slot functions. **Zero matches, for all nine.** **(data-verified)**

> **The conclusion for the port.** The Modding Tools build and retail do not share code at the byte level for this class. Slot numbers cannot be carried
> over; neither can the byte checks that verify them. Every one of these rows is re-derivation from the running game, not translation. The census called
> these rows class B ("a new anchor is clear"); on this evidence they behave like class C with a good starting point.

### 4.3 The one candidate, and what it turned out to be

Slot 151 (`WHGame+0x8AB488`, 1441 bytes) is the only retail slot with the requested-velocity read, and its size is close to the Modding Tools
`UpdateMannequinTags` (1683 bytes). Hooked capture-only for five minutes it fired **34 times across 9 threads** — the main thread (11 calls) and eight
job workers. **(observed)**

* 34 calls in five minutes is not a per-frame per-actor update, so **slot 151 is not `UpdateMannequinTags`**. The gait feature's anchor does not port and
  the function has to be found another way. **(observed)**
* But the threading answer is real and matters on its own: **an actor-level `C_Actor` vtable method on retail runs on the main thread and on eight job
  workers**, exactly the fan-out WO-145 §7.1 inferred for the gait detour and the hit slots. Any retail port inherits the same thread-safety problem the
  Modding Tools build has. **(observed)**

### 4.4 The live layout, named by RTTI

Reading a real NPC's actor and naming every field that points at an RTTI-bearing object is how the layout was measured rather than guessed. The entity →
actor hop itself had to be re-found: retail reaches it through `CEntity`'s component array at **entity+0xB8 .. +0xC0**, where the actor is one entry
(`C_NPCActor@entitymodule@wh@@` for an NPC, `C_Player@entitymodule@wh@@` for the player). The plugin's own hop is a fixed `EntityModule` RVA and does not port. **(observed)**

| retail offset | what is there |
|--:|---|
| +0x030 | the entity id (2049 — the same value the entity system reports) |
| +0x038 | `CEntity` |
| +0x068 | `C_HumanStateMovement` |
| +0x180 | `C_ActorMovementController` |
| +0x240 / +0x258 | `C_HitDeathReactions` / `C_ActorConditionController` |
| +0x268 | `C_AnimatedHuman@animationmodule` |
| +0x280 | `C_ActionActor` |
| **+0x300** | **the float 1.0f** — the Modding Tools build's `m_pCombatActor` is not here |
| **+0x668** | **`C_Soul@rpgmodule`** |
| +0x678 / +0x680 / +0x688 | equipment handler / clothing owner / attachment manager |
| +0x760 / +0xA50 | `SMannequinActorStateParams` / `SMannequinHumanParams` |
| +0x990 / +0x9A8 | `C_ActorModel` / `C_MovementControllerAdapter@xgenaimodule` |
| +0x9D0 / +0xA58 | `C_ItemAttachmentManager` / `C_HumanHandHelper` |

**D-095 is not settled.** `+0x300` holds a float on retail, so the tools-build offset is gone; `+0x278` (the census's candidate) was **null**, but the
NPC was idle and `m_pCombatActor` is null until combat begins, so a null there proves nothing. Settling it needs an actor in combat, which is step 4's
open item. **(inconclusive)**

---

## 5. Step 4 — one swing, one hit (D-365, D-343). **Verdict: inconclusive**

What resolved, on both the first and the second game process: **(observed)**

| | |
|---|---|
| `C_CombatActorActionAttack` RTTI → **1** primary vftable | `WHGame+0x3A61C58` |
| its `EnterImpl` slot 0x1C8 | → `WHGame+0xB27308` |
| `C_CombatSoul` RTTI → **1** primary vftable | `WHGame+0x3F45670` |
| its melee hit slot 0x150 | → `WHGame+0x726DF4` |
| capture hooks on both | installed, prologues copied at instruction-aligned lengths (20 and 22 bytes) |

**Neither fired**, because no combat happened. The console stand-ins available on retail did not produce a fight: the game's own attack interrupt
(`crime:attackInitiatedByConcept` through `XGenAIModule.SendMessageToEntityData`, the path the plugin's WO-139 guard pursuit uses) was accepted by both a
plain NPC and a guard (`sent=true`), but neither entered combat mode within 40 s — the nearest human NPCs were 78–150 m from the player and the only
bodies near the player were sheep. The combat test commands the plugin uses on the Modding Tools build (`C_SetRequestedAttackZone` and friends) do not
exist in retail at all (WO-145 §3.5). **(observed)**

So: the hook points are where the census said, they are unique, and they install cleanly. Whether they capture the right thing, and on which thread, is
**open** — it needs one swing by a player standing next to something. That is a five-minute follow-up, not a work order.

---

## 6. The one crash, and why it is worth writing down

**The probe crashed the game once, at 13:19, and it was the probe's bug.** The first arming pass copied a blanket **18** prologue bytes into the
trampoline for all three hook targets. 18 is right for `C_ModulesManager::Update` (an instruction ends exactly there — which is why step 1 ran for
twenty minutes) and **wrong for both combat slots**, where it cuts an instruction in half: **(data-verified, from the disassembly)**

| target | instruction boundaries | 18 is |
|---|---|---|
| `C_ModulesManager::Update` (+0x532F7C) | 5, 6, 10, 13, **18**, 21, 24 | on a boundary |
| `EnterImpl` (+0xB27308) | 5, 10, 11, **15**, **20**, 23, 28 | inside `cmp qword [rcx+0x58],0` |
| hit slot (+0x726DF4) | 2, 3, 4, 5, 7, **15**, **22** | inside `sub rsp,0x1A0` |
| `C_Actor` slot 151 (+0x8AB488) | 3, 7, 11, **15**, 16, **23**, 30 | inside `lea rbp,[rax-0x168]` |

The game ran normally for twenty minutes with all three patched and died the moment a fight made one of the two combat functions run — the signature of
a corrupted trampoline, not of a detection. Saves were untouched (locks held, verified by hash before and after); the vendor crash reporter opened a
dialog which was **left alone, unsent** (progress page). The probe now takes the length per target and logs the bytes it is about to copy.

**The rule this buys for every future work order, on either build:** a prologue length is a property of the specific function, and it must come from a
disassembly of the exact image being patched. The shipped plugin already does this (`motion.cpp`'s `kTagsPrologue[18]` is for one named function); the
probe generalised it carelessly. The shipped `inline_hook.cpp` cannot catch this — it checks that the bytes are the *expected* bytes, not that the length
ends on an instruction boundary. **(code-verified)**

---

## 7. Step 5 — the NPC-state context (D-553, D-516). **Verdict: works with changes**

### 7.1 What retail keeps

| class | RTTI | vftable |
|---|---|---|
| `C_StanceElement` | yes | 1, `WHGame+0x4001990` |
| `C_StanceElementRequired` | yes | 1, `WHGame+0x3A54E08` |
| `C_UnstanceElement` | yes | 1, `WHGame+0x3A54FB8` |
| `C_HandContentElement` / `…Required` | yes | 1 each |
| `C_MinigameElement` | yes | 1 |
| `C_NPCCurrentState` / `C_NPCRequiredState` | **yes** | 1 each |
| `C_NPC@xgenaimodule` | **yes** | 1 |
| `C_NPCContext` | **no** | — |
| `C_UnstanceElementRequired` | **no** | — |

**(observed)** The census said the context has no RTTI and the `C_NPCContext::…` strings are gone; both hold. What it did not know is that the *states*
keep theirs, and that is enough.

### 7.2 Reaching the context without an anchor on it

The route, entirely structural: **(observed)**

1. entity → `C_NPC@xgenaimodule` (found by RTTI, two pointer hops: `entity+0x188 → +0xA8`).
2. Inside `C_NPC`, scan for the offset `K` where `K+0x90` looks like a state: an element vector at `+0x10/+0x18` whose entries carry one of the element
   vftables. That test needs no RTTI on the context at all.
3. On retail `K = 0x740` (the current state) and `K = 0x7E0` (the required state). **The tools build's `+0x9C0` is not it.**

What was read live from one NPC (`ttkc_woman_8`):

```
S5: C_NPC at entity+0x188 -> +0x0A8
S5: candidate context at +0x740 (current state +0x90 holds 13 element(s))
S5:   current element[0]  C_StanceElement
S5:   current element[8]  C_ChangeEquipmentElement
S5:   current element[12] C_AddScriptContextElement
S5:   current element[18] C_AddBuffElement
S5: candidate context at +0x7E0 (current state +0x90 holds 13 element(s))
S5:   current element[0]  C_StanceElementRequired
```

**Stance and unstance, read.** Both the current `C_StanceElement` and the required `C_StanceElementRequired` carry the same value at **element+0x20**
(`0x0800000000000641` — low dword `0x641` the stance, high dword 8), i.e. the NPC is standing in the stance it is supposed to be in. No
`C_UnstanceElement` was in the list, which is what an NPC that is not using an object looks like. The plugin reads this id at element+0x28 on the
Modding Tools build, so the element layout has moved too. **(observed)**

**One caveat.** Repeating the walk later, on a second process, the same NPC no longer had a `C_NPC` object at all — it had streamed down to
`C_AIPuppet` / `C_LODAgentSO`. The context is reachable **when the NPC is fully simulated**; a retail port has to treat its absence as normal, exactly as
the plugin already treats a missing brain. **(observed)**

---

## 8. Re-estimating the port

WO-145 estimated **12–24 work orders**, front-loaded with uncertainty, and said the go/no-go depended on three facts. All three are now measured, and
part A settled the Lua, pak, console and save questions. The total does not move much; the **shape** does.

| cluster | WO-145 | now | why |
|---|---|--:|---|
| Foundation (start-up gate, frame hook, `gEnv`, engine services) | 3–5 | **1–2** | Done and proven in this probe: the anchor chain, the tick, `gEnv` with every member named, the entity system live. What is left is productionising it. **cheaper** |
| Reflection ABI (71 rows) | part of the above | **2–3** | The registry and `type_data` read directly and the layouts match; but 19 of 22 entry points have no function to call, so the client side is a reimplementation. **changed in kind, roughly the same size** |
| Combat / animation (36 rows) | 4–6 | **5–8** | No slot, offset or byte check ports, and nothing can be signature-matched from `<MT>`. Each anchor is re-derivation against a running game, and the capture is still unproven. **harder** |
| NPC-state / activities (36 rows) | part of 5–8 | **2–3** | Reachable structurally through classes retail keeps; needs one new offset and an element-layout pass. **cheaper and, more importantly, no longer a risk** |
| Struct-field and vtable-slot rows across `C_Actor` and friends (≈142 rows) | part of 5–8 | **3–5** | The RTTI-named field walk makes this mechanical but not free: one pass per class, on a live body. **about as expected, now with a method** |
| Quest / concept layer (46 rows) | part of 5–8 | **unmeasured** | Not probed. WO-145's static result stands. |
| Launcher, installer, Lua degradation | 3–4 | **3–4** | Unchanged; part A's retail flag list (stripped commands, no `QuickSave`, no load route) is the specification. |
| **Total** | **12–24** | **~16–25** | |

**Which clusters got cheaper:** the foundation (now a known quantity rather than the thing that decides viability), and the NPC-state context — the
census's hardest cluster, which turned out to be reachable by a route that does not depend on anything retail stripped.

**Which got harder:** everything anchored on a `C_Actor` vtable slot or a struct offset. The census's class B for those rows assumed a new anchor would
be *clear*; the probe shows the anchors we have are worthless on retail and there is no mechanical translation. That is 162 rows whose cost is now
"re-derive on a live body", which is bounded but not quick.

**What did not change:** game data is identical, the Lua tier works as-is, and there is no protection, no integrity check and no detection.

---

## 9. Port-aware rules for future work orders on the Modding Tools build

Everything below is drawn from what worked and what failed in this run. Following it does not slow the current build down; it makes each future work
order's output portable instead of throwaway.

**Prefer, in this order:**

1. **A class's RTTI name.** Every RTTI-anchored thing the probe looked for was present and unique on retail: `C_Actor`, `C_CombatActorActionAttack`,
   `C_CombatSoul`, `CEntity`, `CEntitySystem`, `CXConsole`, the seven NPC-state element classes, `C_NPC`. **Five for five in the probe set.** RTTI is the
   one anchor that ported without exception. **(observed)**
2. **Identify an object by its vptr, and a field by what it points at.** Naming every pointer field through its RTTI turned three "unknown offset"
   questions into a table in one pass (sections 4.4 and 7.2). Prefer "find the field whose target is a `C_Soul`" over "read +0x668".
3. **A structural property of the data.** "The offset whose +0x90 holds a vector of element-vftable objects" found the NPC context on a build where the
   context class has no RTTI, no strings and a different offset. This is the strongest technique the probe used.
4. **A long, distinctive code shape in one function**, cross-checked against a second independent fact. The frame hook's 12-byte module-loop pattern was
   unique in 60 MB of `.text` and was still cross-checked against `gEnv+0xF0` before anything was patched.
5. **A string the engine logs or a cvar name.** Cvar names and help text survive; `__FUNCTION__`-style labels mostly do not (16 of our 18 are gone),
   except profiler/`ProcessMessage` labels — and one of those carried the whole foundation.

**Avoid, or write down as "this will not port":**

* **A vtable slot number.** 275 against 409 slots, shifting by 0, −24, −86 and −134 in different regions.
* **A struct offset.** `m_pCombatActor` +0x300 → a float; the NPC context +0x9C0 → +0x740; a stance element's id +0x28 → +0x20; the requested velocity
  +0x574 → +0x4D8. Record how an offset was *derived*, not just its value.
* **A byte check that embeds a displacement.** All four of `motion.cpp`'s `UpdateMannequinTags` checks match nothing on retail — the instruction forms
  are there, the displacements are not. Splitting each check into "the instruction form" and "the displacement it should carry" would have let the same
  code find the function and report the moved offset.
* **A module name, an export, an IAT entry, a fixed RVA.** Retail is one DLL, exports two C++ symbols, and has no `Framework.dll` to import from.
* **Signature-porting from one build to the other.** Nine `C_Actor` methods, zero shared 7-byte runs. Do not plan on it.

**Two operational rules, both learned the hard way here:**

* **A hook's prologue length must come from a disassembly of the image being patched** and must end on an instruction boundary with no RIP-relative
  operand and no relative branch inside. A wrong length can run for twenty minutes before the function is first called. Consider adding that check to
  `inline_hook.cpp` itself (it validates the bytes, not the boundary).
* **Put the thread id in the log.** Every threading claim in WO-145 was an inference from code shape; one line of logging turned "engine-any" into
  "main plus eight job workers, measured". The shipped `logf` should carry it.

---

## 10. What the probe could not settle, and what would settle it

| open question | why it is open | what would settle it |
|---|---|---|
| **Does the combat capture work?** (`EnterImpl` slot 0x1C8, `C_CombatSoul` hit slot 0x150) | The hooks install; no combat could be produced from the console, and the retail build has none of the combat test commands the Modding Tools build offers. | One swing by a player standing next to a body, with the two hooks armed. Five minutes, attended. |
| **Where is `m_pCombatActor` on retail?** (D-095) | `+0x300` is a float there and `+0x278` was null on an *idle* actor, where the field is null by design. | The same RTTI field walk on an actor **in combat**: the combat actor will appear as the one field pointing at a `C_CombatActor`/`C_CombatPlayer`. Rides on the answer above. |
| **Where is `UpdateMannequinTags`?** (D-415) | No byte check matches; the single structural candidate fires 34 times in five minutes, so it is something else. | Hook a handful of candidate slots at once and keep the one that fires per actor per frame; or find it from its caller (`C_ActorMovementController::Update`, whose class does have RTTI on retail). |
| **Can a property be read through reflection?** | `type_data` resolves, but `get_property_value` and friends are inlined away. | Walk `type_data`'s own property list (the plugin's ABI notes put it behind vtable slot 0xB8, offsets +0x50/+0x58) and call a wrapper's `get_value` through its vtable. One probe session. |
| **Where is the real `type::get_by_name`?** | The discriminator used selects the global-item map's lookup. | Among the 24 functions that call the registry accessor, the one that searches `registry+0x10`. Or skip it: reading the map directly is fewer moving parts. |
| **The quest / concept cluster (46 rows)** | Not probed at all. | Its own session, same method. |
| **Does a retail port survive a game patch?** | One build was measured. Retail's `WHGame.dll` has not changed since June. | Nothing until the next patch; the identity gate makes the failure safe either way. |
| **Is anything watching?** | Nothing observed: the injection, three hooks, a crash and a vendor crash dialog produced no reaction from the game. The network anti-cheat classes were deliberately not read (out of scope). | Not a question this project should pursue further; a co-op mod that never opens an engine network session should never reach them (**inferred**, WO-145 §3.1). |

---

## Decisions made unattended

1. **The playline gate.** Checked before every step and at every relaunch (`wh_sys_LastLoadedSave` = `playline2/autosave028.whs`, part A's throwaway world). It held each time.
2. **A power cut interrupted the session** before any code ran. The saves were re-hashed against the pre-session backup (318 files, identical) before anything continued.
3. **Two game launches.** The first process was already running when the session resumed; my `steam -applaunch … +wh_sys_AutoLoadLastSave 1` was a no-op against it, and part A's untried auto-load cvar is now **disproved as a route**: Steam drops extra arguments after `-applaunch`, and the process command line shows only the app's own launch options. The maintainer loaded the world both times, and said so.
4. **Starting a fight.** Step 4 needs a combat action and input is ruled out, so I used the game's own attack interrupt between two **NPCs** rather than at the player — a player fight risks a Game Over, which freezes Lua timers and would have ended the session. It did not produce combat either time.
5. **The crash and the crash reporter.** Diagnosed to my own prologue bug (section 6), fixed, and the run repeated. The vendor crash dialog was left untouched: sending a crash report is an outward-facing action and the dialog is the maintainer's to answer.
6. **Six probe builds.** Each rebuild got a new file name because a second `LoadLibrary` of the same name is a no-op and a second copy cannot re-patch a prologue the first already replaced. The versions that could not install the frame hook (the function was already patched by an earlier copy in the same process) ran their read-only work on the probe's own worker thread and said so in the log; every read there is SEH-guarded, so a race reports "unreadable" rather than faulting.
7. **What I did not hook.** Nothing was hooked that the work order did not name, and no hook changes behaviour: every callback counts and logs.
8. **Privacy.** The one anchor that would have been convenient — a vendor build-agent source path that survives in retail — is not used and not reproduced; the probe finds its function by a code-shape anchor instead. No user paths, account names, host names or build-machine names appear here.
