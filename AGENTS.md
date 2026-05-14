# Codex Instructions for Los Santos RED Co-op Refactor

## Main Goal

Implement a separate RAGECOOP-based co-op mode for Los Santos RED.

Do not redesign Los Santos RED gameplay.

The goal is to make existing Los Santos RED systems actor-aware, network-routed, and server-persistent while preserving visible single-player behavior.

---

## Non-Negotiable Rules

- Preserve existing LSR gameplay behavior wherever possible.
- Preserve existing LSR XML/settings/config behavior.
- Do not duplicate police, crime, gang, store, property, vehicle, arrest, or wanted logic into new co-op-specific rule systems.
- Reuse existing LSR requirements/checks/gates.
- Do not invent crew ownership, key-sharing, trading, or new roleplay mechanics unless LSR already supports them.
- Keep normal single-player LSR separate and unchanged.
- Co-op mode must be opt-in and gated behind config/transport mode.
- Make small, buildable patches.
- Do not implement future phases early.
- Do not paste full files unless explicitly asked.
- Prefer patches/diffs and concise summaries.
- Touch the fewest files practical.
- Build after every code change when possible.
- Before adding new compatibility code, inspect existing LSR XML/settings/config options that may already control the behavior.
- Test relevant existing settings before creating new workaround systems.
- Do not add custom compatibility patches until relevant existing settings have been identified, tested, and ruled out.
- Example: `PedSwapSettings.AliasPedAsMainCharacter` affects non-primary ped compatibility with vanilla GTA systems such as shops.
---
## Current RAGECOOP Test Server Settings

For LSR co-op testing, use:

- KickSpamming=true
- SpamLimit=300

LSR FullSimulation creates enough world entities that RAGECOOP's default 100 spam limit may kick the active host. A co-op warmup/throttle reduces startup crashes, but 300 is currently the stable test value.
## Current Design

Co-op session model:

- 24/7 RAGECOOP server owns persistent data.
- One shared server world save.
- Player profiles exist inside the server world only.
- One character per player.
- A connected TrustedHost becomes active simulation host.
- Admin/WorldOwner is separate from TrustedHost.
- Active host runs live LSR world simulation.
- Server saves persistent truth.
- Clients handle local UI, menu navigation, previews, prompts, and local effects.

Roles:

- Admin: owns server/world config and admin-only actions.
- TrustedHost: can become active simulation host.
- Player: normal participant.

Authority:

Client-local:

- UI navigation
- local prompts
- clothing preview before Apply
- camera/screen effects
- local-only animations where safe

Active-host-authoritative:

- crimes
- witnesses
- dispatch
- police/gang reactions
- store/purchase validation
- vehicle/property interaction validation
- death/arrest outcomes

Server-authoritative:

- profiles
- character save
- money
- inventory
- weapons
- owned vehicles
- properties
- gang reputation
- criminal history
- role lists
- persistent world save

---

## Existing Requirements Rule

Los Santos RED already contains many requirements/checks/gates that define behavior.

Do not replace these with new co-op-specific behavior.

Find existing requirements and make them actor-aware by passing a co-op character/context into them.

If a requirement currently assumes `Game.LocalPlayer` or the single `Player` object, identify the minimum change needed so it can evaluate for a specific LSR co-op character.

---

## Actor-Aware Rule

Old assumption:

Player == Game.LocalPlayer.Character

Target architecture:

LsrCoopCharacter == one connected co-op participant

Every existing behavior should eventually receive or resolve:

- actor character
- actor ped
- actor vehicle
- actor profile id
- local/remote state
- active-host/server authority state

---

## Character Creation

If a player joins with no profile:

- freeze/safe-state the player
- reuse LSR’s existing ped/customizer menu
- do not build a new character creator unless absolutely required
- save the resulting character into the server world profile
- one character per player for v1

BootstrapOnly exists specifically to allow the existing LSR character creation/customizer flow without starting full world/police/gang/dispatch simulation.

---

## Customization Permissions

Normal players:

- can fully use existing LSR character creation/customizer during BootstrapOnly / first character creation
- can change model, gender, components, props, clothes, and appearance during first character creation
- can use normal LSR clothing/customization behavior where LSR allows it
- can preview locally
- final Apply/save must be validated/saved through the co-op profile flow where applicable

Admin-only in co-op:

- debug menus
- normal/general Ped Swap menu access from the LSR menu
- admin-style arbitrary ped swapping outside first character creation
- money edits
- criminal record edits
- other debug/admin tools

Important:

- Do not block model selection during first character creation.
- Do not restrict existing LSR character creator internals unless the action is truly a debug/admin entry point.
- Restrict menu entry points, not normal character creation behavior.

---

## Persistence Rule

Persist long-term data:

- character
- appearance
- money
- inventory
- weapons
- owned vehicles
- properties
- gang reputation
- criminal history
- long-term LSR records/assets

Do not persist temporary live incidents:

- active police chase
- spawned police
- roadblocks
- current witnesses
- temporary peds
- random street vehicles
- live combat state

---

## RAGECOOP Rule

Keep RAGECOOP-specific code isolated behind transport/adapters.

Core LSR gameplay should not directly depend on RAGECOOP APIs unless there is a clear adapter boundary.

RPH Los Santos RED and RAGECOOP/SHVDN client resources do not reliably share static callback state. Do not use static callbacks for cross-runtime communication.

Use neutral bridge files/events for LSR-to-RAGECOOP low-frequency gameplay events.

Keep RPH types out of `LsrCoop.Client`.

Keep SHVDN/GTA types out of LSR core.

---

## Current Project State

Workspace:

- Repo path: `C:\Users\melke\Documents\LSRED_LSRP_COOP_PROJECT`
- GTA V path: `O:\SteamLibrary\steamapps\common\Grand Theft Auto V`
- Upstream/basic LSR repo: `https://github.com/thatoneguy650/Los-Santos-RED`
- LSRP fork used for this work: `https://github.com/cpks97/Los-Santos-RED-Project`
- Co-op repo: `https://github.com/Kiira666999/LsCOOP`

Build/deploy safety:

- Do not write normal LSR build output directly to the live GTA `Plugins` folder.
- `Los Santos RED.csproj` should output to local repo `bin` folders.
- Only copy a test DLL into GTA `Plugins` when explicitly requested.
- RAGECOOP client/server resource DLLs may be built/deployed into the local `RageCoopServer/Resources` test folders when explicitly part of the task.
- Be careful with stale `.pdb` files: RPH stack traces may point at this repo even when the live DLL is restored.

Version/XML compatibility:

- Live XML is from the newer `1.0.0.513` line.
- Older source around `1.0.0.484` cannot read all live XML types.
- Local source was adapted to recognize live XML-backed types such as `MansionInterior`, `MoneyEntitySet`, `AudioEmitter`, and `AudioEmitterInteract`.
- Preserve existing XML/settings behavior; do not remove or rewrite live XML assumptions.

Implemented co-op foundation:

- Core co-op types exist under `Los Santos RED/lsr/Coop/Core/`.
- RAGECOOP server and client resource projects exist.
- Server has role config, Admin/TrustedHost/Player role handling, compatibility checks, player registration, profile storage, character snapshots, active host selection, and soft handoff.
- Client sends ping/compatibility, receives character/profile/session state, writes startup bridge state, and forwards bridge events to the server.
- Startup is gated through co-op startup modes.
- BootstrapOnly exists for existing LSR character creation/customizer.
- FullSimulation exists for the active TrustedHost.
- Character creation uses existing LSR PedCustomizer/PedSwap flow.
- Character creation completion uses a file bridge, not static cross-runtime callbacks.
- Saved co-op character model is hydrated before full LSR startup.
- BootstrapOnly-to-FullSimulation transition preserves the created model and avoids unnecessary PLAYER_ONE reset.
- Single-player/co-op-disabled behavior must remain unchanged.

Known current runtime behavior:

- No-character TrustedHost can enter BootstrapOnly, use the existing LSR customizer, save a character, ACK the snapshot, and become eligible for FullSimulation.
- Returning character snapshots load with saved model.
- FullSimulation runs on the active TrustedHost.
- Disconnect/reconnect and active-host release are working in basic single-client testing.
- Money/inventory persistence works after an existing LSR purchase hook completes.
- A tested drink purchase updates money, saves the inventory/money snapshot, and reloads correctly after reconnect.

Bridge architecture:

- Do not rely on static callbacks between the RPH Los Santos RED plugin and the RAGECOOP/SHVDN client resource for cross-runtime events.
- Use neutral file/event bridge payloads for LSR-to-RAGECOOP low-frequency gameplay events.
- Use nonce dedupe, process/profile filtering, atomic writes, and clear logs.
- Keep RPH types out of `LsrCoop.Client`.
- Keep SHVDN/GTA types out of LSR core.

Current known issues / not-yet-final systems:

- Gang reputation bridge reaches the server, but snapshots may be empty even when LSR logs reputation changes. This likely means the adapter is not yet reading the true LSR gang reputation source. Defer until the gang reputation phase.
- Gameplay action timestamp schema must remain consistent, especially `RequestedUtc` / action timestamps.
- ClientMode for character-ready non-active players still needs to be added or verified before real multi-client play.
- Store purchase persistence works for at least one simple item, but remaining purchase paths need staged testing.
- Crime, police, gang, vehicle, property, death/arrest actor routing are not complete.

---

## Current Startup Modes

Expected startup modes:

Disabled:
  normal single-player LSR

Blocked:
  co-op enabled, but this client is not allowed to load LSR systems yet

BootstrapOnly:
  no character yet
  load existing LSR character creation/customizer only
  do not start full world/police/gang/dispatch simulation

ClientMode:
  character-ready player who is not active simulation host
  local/client-safe LSR systems only
  no world simulation authority

FullSimulation:
  active TrustedHost with character ready
  full live LSR world simulation

ClientMode is the current major missing/next architecture piece if not already implemented.

---

## Next Safe Implementation Step

Current priority:

- Add or verify ClientMode for character-ready players who are not the active simulation host.

ClientMode goal:

- Every co-op player loads LSR locally.
- Only the active TrustedHost runs FullSimulation.
- Non-active players run ClientMode with local/client-safe LSR systems only.

ClientMode must NOT start:

- police dispatch
- civilian/world simulation
- gang simulation
- roadblocks
- full dispatcher
- world spawning
- authoritative store/property/vehicle validation
- full task groups that mutate the world

ClientMode may initialize:

- local player wrapper
- local UI shell where safe
- local prompts
- local customization/appearance support
- co-op request/bridge services

After ClientMode:

1. Test one active FullSimulation client plus one ClientMode client.
2. Verify only one FullSimulation host exists at a time.
3. Continue purchase coverage carefully.
4. Then move to simple crime actor routing and passive criminal history.

---

## Updated Implementation Order

Completed / mostly completed:

1. Co-op core abstractions
2. RAGECOOP server/client resource scaffolding
3. Role config: Admin, TrustedHost, Player
4. Active host selection
5. Compatibility checks
6. Player registration and server-world profiles
7. Character readiness gating
8. BootstrapOnly startup mode
9. Existing LSR character creation/customizer flow
10. File bridge for character creation completion
11. Saved character model hydration into full LSR startup
12. BootstrapOnly-to-FullSimulation model preservation
13. FullSimulation for active TrustedHost
14. Basic permission gating pattern
15. Money/inventory persistence after a simple LSR purchase

Current next phase:

16. Add/verify ClientMode for character-ready non-active players

Near-term phases:

17. Multi-client lifecycle testing:
    - one active TrustedHost in FullSimulation
    - one normal player in ClientMode
    - second TrustedHost in ClientMode until promoted
    - active host disconnect / soft handoff

18. Expand purchase coverage carefully:
    - simple item purchase already tested
    - test weapon purchase path
    - test vehicle item purchase path only as purchase/inventory event, not full owned-vehicle system yet

19. Passive profile hydration/resync:
    - money
    - inventory
    - weapons snapshot
    - no trading

20. Simple crime actor routing:
    - one low-risk crime path
    - correct offender profile
    - existing LSR crime logic remains source of truth

21. Passive criminal history persistence:
    - long-term records only
    - do not persist active chases/searches/witnesses/roadblocks

22. Gang reputation mapping:
    - investigate actual LSR reputation source of truth
    - make adapter read real LSR gang reputation state
    - no crew reputation
    - no new gang rules

23. Owned vehicle routing:
    - use existing LSR owned vehicle behavior
    - no key-sharing
    - no crew vehicles

24. Property routing:
    - use existing LSR property behavior
    - private ownership only unless LSR already supports more

25. Death/arrest per player:
    - existing LSR death/arrest/busted/wasted behavior
    - affected player only
    - other players continue

26. Host handoff hardening:
    - persistent state saves
    - temporary live incidents discarded
    - no seamless live entity migration

27. Integration testing and cleanup

---

## Important Runtime Lessons Learned

- RPH Los Santos RED and RAGECOOP/SHVDN client resources do not reliably share static callback state.
- Do not use static callbacks for cross-runtime communication.
- Character creation originally failed because LSR called a static callback that the RAGECOOP client could not receive.
- Use file/event bridges for LSR-to-RAGECOOP events.
- RAGECOOP can crash if local player model changes at unsafe times.
- Avoid unnecessary model swaps during BootstrapOnly-to-FullSimulation transitions.
- Do not reset the player model to PLAYER_ONE during co-op BootstrapOnly transition.
- Before applying a saved model, check whether the current model already matches the saved model.
- Keep all model-transition fixes gated to co-op BootstrapOnly/FullSimulation startup paths.
- Do not let fixes for co-op model transitions change normal single-player cleanup behavior.


---

## Things Codex Must Not Do

Do not:

- rewrite police behavior
- rewrite wanted behavior
- rewrite gang behavior
- create crew reputation
- create crew ownership
- create key-sharing
- create direct trading
- create offline progression
- create new store rules
- create a new character creator while existing LSR character creation can be used
- make RAGECOOP a hard dependency of normal single-player LSR
- persist live police chases
- persist spawned cops
- persist roadblocks
- persist witnesses
- persist random ambient vehicles
- persist live combat state
- allow multiple clients to run FullSimulation at the same time
- assume TrustedHost equals Admin
- use static callbacks for cross-runtime RPH ↔ RAGECOOP communication



## Output Format

For every task, output only:

1. Files changed
2. What changed
3. Why
4. Build/test result
5. Next recommended task

Keep summaries concise.
