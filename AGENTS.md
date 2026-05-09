# Codex Instructions for Los Santos RED Co-op Refactor

## Main Goal

Implement a separate RAGECOOP-based co-op mode for Los Santos RED.

Do not redesign Los Santos RED gameplay.

The goal is to make existing Los Santos RED systems actor-aware, network-routed, and server-persistent while preserving visible single-player behavior.

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

## Existing Requirements Rule

Los Santos RED already contains many requirements/checks/gates that define behavior.

Do not replace these with new co-op-specific behavior.

Find existing requirements and make them actor-aware by passing a co-op character/context into them.

If a requirement currently assumes Game.LocalPlayer or the single Player object, identify the minimum change needed so it can evaluate for a specific LSR co-op character.

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

## Character Creation

If a player joins with no profile:

- freeze/safe-state the player
- reuse LSR’s existing ped/customizer menu if possible
- do not build a new character creator unless required
- save the resulting character into the server world profile
- one character per player for v1

## Customization Permissions

Normal players:
- can use LSR clothing/customization behavior where LSR allows it
- can preview locally
- final Apply must be validated/saved

Admin-only:
- full ped swap
- model change after creation
- identity edits after creation unless explicitly allowed
- money edits
- criminal record edits
- debug/admin menus

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

## RAGECOOP Rule

Keep RAGECOOP-specific code isolated behind transport/adapters.

Core LSR gameplay should not directly depend on RAGECOOP APIs unless there is a clear adapter boundary.

## Current Project State

Workspace:
- Repo path: `C:\Users\melke\Documents\LSRED_LSRP_COOP_PROJECT`
- GTA V path: `O:\SteamLibrary\steamapps\common\Grand Theft Auto V`
- Upstream/basic LSR repo: `https://github.com/thatoneguy650/Los-Santos-RED`
- LSRP fork used for this work: `https://github.com/cpks97/Los-Santos-RED-Project`

Build/deploy safety:
- Do not write normal LSR build output directly to the live GTA `Plugins` folder.
- `Los Santos RED.csproj` should output to local repo `bin` folders.
- Only copy a test DLL into GTA `Plugins` when explicitly requested.
- Live GTA `Plugins\Los Santos RED.dll` was restored to `1.0.0.513`.
- A stale `Plugins\Los Santos RED.pdb` from local builds may make RPH stack traces point at this repo even when the live DLL is restored.

Version/XML compatibility:
- Live XML is from the newer `1.0.0.513` line.
- Older source around `1.0.0.484` cannot read all live XML types.
- Local source was adapted to recognize live XML-backed types such as `MansionInterior`, `MoneyEntitySet`, `AudioEmitter`, and `AudioEmitterInteract`.
- Keep preserving existing XML/settings behavior; do not remove or rewrite live XML assumptions.

Co-op scaffold status:
- Core co-op types exist under `Los Santos RED/lsr/Coop/Core/`.
- Actor context, local character manager, null transport, persistence models, permission service, requirement adapter, and appearance state/service scaffolds exist.
- Minimal `LsrCoop.Server` and `LsrCoop.Client` RAGECOOP resources exist.
- Server has role config, active host selection, compatibility checks, player registration, one-profile-per-player storage, and appearance event scaffolding.
- Client resource logs load, sends ping/compatibility report, receives profile/session state, and can report `CharacterCreateRequired`.

Observed RAGECOOP test result:
- RAGECOOP server/client resource loading worked.
- Client became compatible with required co-op build/config and LSR `1.0.0.513`.
- LSR gameplay itself is not safe in RAGECOOP yet because normal single-player simulation still starts immediately.
- Recent LSR crash after loading in RAGECOOP came from `GameLocation.HandlePriceRefreshes()` during vendor/location simulation, not from the co-op transport.
- This is expected until LSR simulation is gated so only the active TrustedHost runs live simulation.

Next safe implementation step:
- Add a co-op/RAGECOOP startup gate so normal LSR simulation does not run in co-op unless the client is the active simulation host.
- Keep single-player startup unchanged.
- Do not rewrite police/gang/store behavior yet; first prevent uncontrolled solo simulation from running in the multiplayer session.

## Overall implementation order

Phase 0  - Repo survey, no code
Phase 1  - Add co-op character abstractions, no behavior change
Phase 2  - Add character manager, still single-player only
Phase 3  - Split local-only vs character-state concepts
Phase 4  - Add co-op transport abstraction, no RAGECOOP dependency yet
Phase 5  - Add RAGECOOP adapter project/layer
Phase 6  - Add player registration and remote character records
Phase 7  - Add per-player appearance/clothing state
Phase 8  - Add safe clothing sync for all players
Phase 9  - Add permission system: host/admin ped swap only
Phase 10 - Add per-player gang reputation
Phase 11 - Add crime ownership events
Phase 12 - Add per-player wanted/investigation state
Phase 13 - Add hard police target selection
Phase 14 - Add arrest/death handling per player
Phase 15 - Add co-op save files
Phase 16 - Integration testing and compatibility cleanup

## Output Format

For every task, output only:

1. Files changed
2. What changed
3. Why
4. Build/test result
5. Next recommended task

Keep summaries concise.
