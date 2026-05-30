# CryoPod implementation plan

## 1. Product definition

CryoPod is a fullscreen Windows game launcher in the style of Steam Big Picture / Winhanced, built on the current WinUI 3 + .NET 8 app scaffold.

Core user promise:
- discover locally installed games automatically
- present them in a controller-first fullscreen gallery
- enrich game metadata/art from SteamGridDB
- launch games from one UI
- allow the user to press a dedicated "home / suspend" button to return to the launcher while keeping multiple launched games in a resumable state when possible

## 2. Critical constraints to settle early

The "Xbox-style suspend" feature is the hardest and riskiest part. On Windows desktop, generic suspend/resume is not a guaranteed OS feature for arbitrary games.

Practical implications:
- some games can be paused cleanly by minimizing/focus switching only
- some can survive process suspension and resume
- some will break on resume because of anti-cheat, network timeouts, DRM, audio/device loss, or graphics context issues
- online / anti-cheat titles may need to be marked unsupported for suspend

Because of that, CryoPod should be designed around **capability tiers** instead of promising universal suspend:
1. **Return-to-launcher only**: game keeps running in background, CryoPod regains focus.
2. **Window hide/minimize**: game window is hidden/minimized if supported.
3. **Process freeze/resume**: suspend the game process tree and later resume it.
4. **Per-game compatibility rules**: allow disabling suspend or choosing a safer mode for specific games.

## 3. Recommended high-level architecture

### App layers
1. **Shell/UI layer**
   - fullscreen launcher shell
   - controller navigation
   - overlays for running/suspended games
   - settings and first-run flows

2. **Game library layer**
   - discovery from installed launchers and filesystem
   - canonical game model
   - local metadata cache
   - artwork pipeline

3. **Launch/runtime layer**
   - launch game / launcher URIs / executables
   - track running process trees
   - foreground switching
   - suspend/resume orchestration

4. **Platform integration layer**
   - global hotkey / guide-button handling
   - window enumeration and foreground control
   - process management
   - storage and background tasks

### Suggested project structure
- `CryoPod.Core`
  - models, interfaces, app services
- `CryoPod.Infrastructure`
  - Steam/EGS/Xbox discovery, SteamGridDB client, persistence, Windows interop
- `CryoPod.App`
  - WinUI views, view models, shell, controller UX
- `CryoPod.Tests`
  - unit/integration tests for parsing, matching, caching, process policies

The current single-project scaffold can start simple, but this split should happen before runtime/suspend logic becomes large.

## 4. Main feature tracks

### Track A: Fullscreen shell and UX foundation
Goal: make CryoPod feel like a console shell.

Deliverables:
- borderless fullscreen main window
- controller-first focus/navigation system
- virtualized game grid / carousel / details panel
- "recently played", "running", and "suspended" sections
- quick overlay for active games
- settings screen for launchers, artwork, controller mapping, suspend policies

Key technical work:
- WinUI layout system for TV/couch distance
- input abstraction for keyboard + Xbox controller
- visual state system for hover/focus/selection
- startup and resume flows

### Track B: Local game discovery
Goal: find installed PC games reliably.

Initial sources:
1. Steam
2. Xbox / Microsoft Store PC games
3. Epic Games Store
4. GOG Galaxy
5. Battle.net / Ubisoft Connect / EA app later
6. manual add as fallback

Per-source work:
- detect installation roots / manifests / registry entries
- parse launcher-specific metadata
- resolve executable, working directory, launch arguments, launcher URI if needed
- generate stable local game IDs
- identify install state changes

Important note:
- discovery should be incremental and cache results
- manual overrides are essential because PC game installs are messy

### Track C: Metadata enrichment and artwork
Goal: transform raw local installs into polished launcher tiles.

Data pipeline:
1. normalize discovered titles
2. match local game to a canonical title
3. query SteamGridDB
4. download preferred assets
5. cache locally with expiry / refresh rules
6. allow user override if matching is wrong

Needed asset types:
- grid capsule
- hero/banner
- logo
- icon
- optional background video later

Key technical needs:
- SteamGridDB client with rate limiting
- fuzzy matching + confidence score
- artwork cache database
- fallback visuals when no match exists

### Track D: Launch, tracking, and return-to-shell
Goal: reliably launch games and bring the user back to CryoPod.

Deliverables:
- launch executable / URI / launcher command
- detect spawned child process and main game process
- detect main game window
- track game session state: not running / launching / running / background / suspended / failed
- return CryoPod to foreground on command

Needed capabilities:
- process tree tracking
- window enumeration and window-to-process correlation
- robust "bring launcher to front" behavior
- timeout/retry rules for slow launchers

### Track E: Suspend/resume system
Goal: provide the closest practical desktop equivalent to Xbox quick resume.

Implementation phases:
1. **Phase E1 - return only**
   - dedicated button brings CryoPod back to foreground
   - game continues running normally

2. **Phase E2 - minimize/hide**
   - attempt to minimize or hide the game window
   - restore window on resume

3. **Phase E3 - experimental process suspension**
   - suspend process tree
   - keep registry of suspended sessions
   - resume tree on user selection

4. **Phase E4 - compatibility policies**
   - per-game suspend mode: disabled / return-only / minimize / freeze
   - warnings for online/anti-cheat games

Technical tasks:
- low-level process suspend/resume interop
- child-process tree discovery
- suspend safety heuristics
- crash detection after resume
- recovery flow if resume fails

Non-negotiable UX requirements:
- never silently claim a game is suspend-safe
- clearly show experimental/unsupported states
- allow force-kill from launcher if a suspended game is stuck

### Track F: Controller and guide-button integration
Goal: make one hardware action always return to CryoPod.

Work items:
- detect standard Xbox controllers
- support keyboard fallback shortcut
- map a dedicated "home" action
- investigate whether the real Xbox guide button is accessible in desktop apps; if not, provide alternatives
- support controller chords if platform APIs block the physical guide button

Likely reality:
- the actual Xbox guide button may not be exposed consistently to normal desktop apps
- CryoPod should plan for fallback bindings such as:
  - Xbox button equivalent via supported API if available
  - `View + Menu`
  - `Guide chord`
  - keyboard global hotkey

### Track G: Persistence, caching, and settings
Goal: preserve library state and runtime state cleanly.

Persist:
- discovered games
- source-specific identifiers
- canonical game match
- downloaded art paths and cache metadata
- user custom titles/art/launch options
- suspend compatibility policy
- last-known running/suspended sessions

Suggested storage:
- SQLite for metadata/state
- local filesystem cache for artwork
- JSON import/export for backups later

### Track H: Packaging, permissions, and startup behavior
Goal: make the app deployable and usable like a shell.

Needed work:
- decide packaged vs unpackaged tradeoffs for Windows APIs
- ensure required permissions and app manifest capabilities
- optional start-with-Windows
- optional kiosk/fullscreen startup mode
- crash recovery on next launch if games were left suspended/running

## 5. Detailed delivery roadmap

### Milestone 0: Research + architecture spike
Outcome: validate feasibility before building the full UX.

Tasks:
- verify WinUI 3 fullscreen shell behavior
- test controller input APIs in WinUI 3
- test game launch + process tree capture
- test focus switching back to CryoPod
- prototype process suspend/resume on 3-5 sample games
- document failure cases by game type

Exit criteria:
- confirmed approach for controller input
- confirmed approach for foreground switching
- suspend capability matrix defined

### Milestone 1: Shell MVP
Outcome: polished fullscreen launcher shell without discovery yet.

Tasks:
- shell window, theming, navigation, grid layout
- controller focus model
- placeholder game cards and details screen
- settings shell

Exit criteria:
- can navigate entire launcher with controller
- app feels stable in fullscreen on desktop/TV

### Milestone 2: Discovery MVP
Outcome: real library from local installs.

Tasks:
- Steam discovery first
- local cache and refresh flow
- manual rescan
- game details page backed by real data

Exit criteria:
- installed Steam games populate automatically
- library survives restart

### Milestone 3: Artwork + metadata MVP
Outcome: launcher looks like a real console library.

Tasks:
- title normalization
- SteamGridDB integration
- asset download/cache
- fallback art and manual correction

Exit criteria:
- most discovered titles display art correctly
- bad matches can be corrected manually

### Milestone 4: Launch + running-state tracking
Outcome: CryoPod can launch and monitor games.

Tasks:
- start game from tile
- detect running state
- maintain "currently running" section
- return launcher to foreground manually

Exit criteria:
- launch/resume-to-shell loop works for local test titles

### Milestone 5: Suspend beta
Outcome: first usable version of the flagship feature.

Tasks:
- implement return-only mode globally
- implement minimize/hide mode where possible
- add experimental process freeze mode
- build per-game compatibility rules
- add warnings and recovery actions

Exit criteria:
- user can keep multiple test games in known states
- unsupported games fail safely and clearly

### Milestone 6: Multi-launcher expansion
Outcome: broader PC launcher support.

Tasks:
- Epic, GOG, Xbox app
- launcher-specific quirks
- canonical identity merging across sources

### Milestone 7: Polish and hardening
Outcome: stable daily-driver experience.

Tasks:
- performance tuning for large libraries
- error reporting and diagnostics
- startup recovery
- offline behavior
- richer sorting/filtering/search
- accessibility and TV readability

## 6. Recommended technical decisions

### UI pattern
- use MVVM from the start
- keep shell state in view models/services, not code-behind
- design for 10-foot UI readability and controller focus rings

### Data model
Core entities:
- `Game`
- `GameInstall`
- `GameSource`
- `ArtworkSet`
- `LaunchProfile`
- `RuntimeSession`
- `SuspendPolicy`
- `ControllerBinding`

### Process/runtime model
Each launched game should create a tracked runtime session with:
- root launch target
- resolved main process
- child process list
- main window handle
- last active timestamp
- current runtime mode
- safe resume hints

### Matching strategy
- exact source IDs where possible
- title normalization + aliases
- confidence scoring for SteamGridDB match
- user override always wins

## 7. Biggest risks and mitigation

| Risk | Why it matters | Mitigation |
|---|---|---|
| Generic suspend is unreliable | Different engines/launchers behave differently | Ship capability tiers and per-game policies |
| Guide button inaccessible | Desktop APIs may not expose the physical Xbox button | Support alternate controller chords and hotkeys |
| Launcher process vs real game process confusion | Many PC launchers spawn child processes | Track process tree and main window ownership |
| Wrong SteamGridDB matches | Title strings vary heavily | Confidence scores, manual override, alias table |
| Packaged app API limits | Some Win32 behaviors are easier unpackaged | validate packaging strategy in Milestone 0 |
| Anti-cheat/networked games break on suspend | Resume may fail or trigger penalties | default suspend disabled for risky titles |

## 8. Test strategy

### Automated
- manifest/parser tests for each launcher source
- title normalization and matching tests
- cache/persistence tests
- suspend policy selection tests
- state-machine tests for runtime sessions

### Manual compatibility matrix
Build a repeatable matrix across:
- Steam single-player game
- Steam online game
- launcher-spawned game
- Game Pass / Xbox PC game
- Unreal / Unity / emulated / retro title
- anti-cheat title

Validate:
- launch
- return to CryoPod
- minimize/hide
- suspend/resume
- repeated switching between multiple games
- system sleep/reboot/crash recovery

## 9. First implementation slice I recommend

Build the first vertical slice in this order:
1. fullscreen shell
2. Steam-only discovery
3. local library cache
4. SteamGridDB artwork fetch/cache
5. launch game + track running process
6. dedicated "return to CryoPod" action
7. minimize/hide support
8. only then experimental process suspension

This gets a real usable prototype quickly while reducing risk around the hardest feature.

## 10. Immediate next coding tasks

1. Restructure the scaffold into app/core/infrastructure boundaries.
2. Implement fullscreen shell layout and controller navigation skeleton.
3. Add a local data layer (likely SQLite + cache folders).
4. Implement Steam library discovery.
5. Build the canonical game model and library service.
6. Integrate SteamGridDB search/download/cache.
7. Implement launch tracking and return-to-shell.
8. Build suspend capability tiers and start with return-only mode.

## 11. Agentic execution prompts

Use the prompts below as handoff-ready instructions for any coding agent. Replace placeholders such as `<repo_root>`, `<branch_name>`, `<SteamGridDB API key strategy>`, and `<test games>` before use.

### Prompt set A: Main feature tracks

#### Track A prompt: Fullscreen shell and UX foundation

```text
You are implementing Track A for CryoPod in the repository at <repo_root>.

Product context:
- CryoPod is a fullscreen Windows game launcher similar to Steam Big Picture / Winhanced.
- Current stack is WinUI 3 + .NET 8 on Windows.
- The UX must be controller-first, TV-friendly, and optimized for fullscreen use.

Your goal:
Build the fullscreen shell and UX foundation without yet depending on real game discovery.

Required outcomes:
1. Make the main window borderless/fullscreen-ready and suitable for a 10-foot UI.
2. Implement a controller-first navigation skeleton with keyboard fallback.
3. Create a reusable game gallery layout with placeholder data.
4. Add sections for recently played, running, and suspended games, even if they use mock data.
5. Add a details panel or details page for a selected game.
6. Add a settings shell for launcher sources, artwork preferences, controller bindings, and suspend policies.
7. Keep logic in MVVM-style view models/services rather than code-behind where practical.

Constraints:
- Do not over-engineer animations before navigation and layout are stable.
- Follow existing repo conventions and keep changes surgical.
- Reuse existing WinUI patterns if present.
- Preserve keyboard/mouse usability while prioritizing controller flows.

Implementation guidance:
- Introduce app shell layout primitives first, then focus/navigation behavior.
- Create placeholder models/view models only as needed to exercise the UI.
- Prefer composable controls and clear state boundaries.
- Add comments only where logic is not obvious.

Validation:
- Ensure the app builds.
- Ensure the shell can be navigated end-to-end with keyboard and any controller abstraction already available.
- Ensure focus visuals are clear.

Deliverables:
- Code changes
- Brief summary of architecture/UI decisions
- Any follow-up risks or limitations discovered
```

#### Track B prompt: Local game discovery

```text
You are implementing Track B for CryoPod in the repository at <repo_root>.

Goal:
Build the local game discovery layer so CryoPod can find installed games reliably, starting with a clean architecture that can expand to multiple launchers.

Required outcomes:
1. Design a discovery abstraction that supports multiple sources.
2. Implement Steam as the first source.
3. Define stable models for discovered games, installs, source IDs, launch targets, and local metadata.
4. Cache discovery results locally and support refresh/rescan.
5. Support manual add or manual override hooks if a full UI is not yet available.

Sources roadmap:
- Steam first
- Xbox / Microsoft Store
- Epic Games Store
- GOG Galaxy
- Later: Battle.net / Ubisoft Connect / EA app

Constraints:
- Discovery must be incremental and not re-scan everything unnecessarily.
- Avoid source-specific logic leaking throughout the app.
- Keep per-source parsers isolated.
- Plan for messy installs and partial metadata.

Implementation guidance:
- Start with interfaces like IGameDiscoverySource and a coordinating library service.
- Parse launcher manifests/registry/config files robustly.
- Resolve executable path, working directory, and launch arguments when possible.
- Generate stable local IDs and source-specific IDs.

Validation:
- Ensure installed Steam games appear in the library model.
- Ensure refresh/restart preserves results via cache or persistence.
- Add tests for parsing and ID generation where feasible.

Deliverables:
- Code changes
- Summary of supported discovery sources and data model
- Known limitations and next source to implement
```

#### Track C prompt: Metadata enrichment and artwork

```text
You are implementing Track C for CryoPod in the repository at <repo_root>.

Goal:
Turn discovered local games into polished launcher entries by matching them to canonical titles and downloading artwork from SteamGridDB.

Required outcomes:
1. Build a title normalization pipeline.
2. Implement canonical matching with confidence scoring.
3. Integrate SteamGridDB via a clean client abstraction.
4. Download and cache artwork assets locally.
5. Store metadata/artwork associations in persistence.
6. Provide fallback visuals and support for future manual overrides.

Asset priorities:
- grid capsule
- hero/banner
- logo
- icon

Constraints:
- Respect rate limits and network failures.
- Do not block the whole UI on artwork fetches.
- Do not assume title strings match API data exactly.
- User override must remain possible in the model.

Implementation guidance:
- Separate matching logic from the raw API client.
- Add cache expiration/refresh rules.
- Persist both match confidence and chosen asset paths.
- Make the UI resilient when art is missing.

Validation:
- Ensure a representative set of discovered games resolves to artwork where possible.
- Ensure cache survives restart.
- Add tests for normalization and match scoring.

Deliverables:
- Code changes
- Summary of matching pipeline and cache behavior
- Any API/configuration setup required
```

#### Track D prompt: Launch, tracking, and return-to-shell

```text
You are implementing Track D for CryoPod in the repository at <repo_root>.

Goal:
Allow CryoPod to launch games, detect the real running process/window, track runtime state, and return the user to the launcher on demand.

Required outcomes:
1. Launch games via executable path, URI, or launcher command.
2. Track launch sessions from initial command to resolved main game process.
3. Detect child process spawning and main window ownership.
4. Record runtime state transitions: not running, launching, running, background, suspended, failed.
5. Implement reliable "bring CryoPod to foreground" behavior.
6. Surface running games in the UI.

Constraints:
- PC launchers often spawn a bootstrapper before the real game process.
- Window handles may appear late.
- Focus switching on Windows can be finicky.
- Avoid brittle one-off heuristics where reusable tracking logic is possible.

Implementation guidance:
- Create a runtime session service and process/window correlation helpers.
- Add timeouts/retries for slow launchers.
- Track enough metadata to support later suspend/resume work.
- Keep launcher-specific quirks isolated.

Validation:
- Verify the loop of launch game -> detect running session -> return to CryoPod works for local test titles.
- Ensure session state updates are visible and persisted if needed.

Deliverables:
- Code changes
- Summary of runtime model
- Known edge cases and unsupported scenarios
```

#### Track E prompt: Suspend/resume system

```text
You are implementing Track E for CryoPod in the repository at <repo_root>.

Goal:
Build the closest practical Windows desktop equivalent to Xbox quick resume, while being honest about compatibility limits.

Important product rule:
Do NOT promise universal suspend. Implement capability tiers:
1. return-to-launcher only
2. minimize/hide
3. experimental process freeze/resume
4. per-game compatibility rules

Required outcomes:
1. Introduce a suspend policy model and per-game capability selection.
2. Implement return-only mode first.
3. Implement minimize/hide when supported by the game window.
4. Implement experimental process-tree suspension/resume behind explicit policy.
5. Add recovery actions, warnings, and clear unsupported/experimental states.
6. Persist suspended session metadata so CryoPod can recover state on restart if possible.

Constraints:
- Some games will break on suspend due to anti-cheat, networking, DRM, or graphics/audio device issues.
- Never silently mark a game safe.
- Force-kill/recovery must exist for bad resume states.

Implementation guidance:
- Track process trees, windows, and session state carefully.
- Keep low-level Windows interop isolated.
- Make policy selection data-driven.
- Default risky titles to safer modes.

Validation:
- Exercise multiple local test games across single-player and risky categories.
- Confirm return-only works broadly before treating freeze/resume as usable.
- Ensure unsupported cases fail clearly rather than silently.

Deliverables:
- Code changes
- Compatibility policy model
- Summary of tested suspend modes and observed limitations
```

#### Track F prompt: Controller and guide-button integration

```text
You are implementing Track F for CryoPod in the repository at <repo_root>.

Goal:
Provide a hardware action that reliably returns the user to CryoPod, with practical fallbacks if the physical Xbox guide button is not exposed to desktop apps.

Required outcomes:
1. Detect standard Xbox-compatible controllers using the best available Windows/.NET approach in this codebase.
2. Add a controller input abstraction that supports a dedicated "home" action.
3. Support keyboard fallback for the same action.
4. Investigate whether the real Xbox guide button is available; if not, implement alternate bindings/chords.
5. Make bindings configurable or at least model them for future settings UI.

Constraints:
- Desktop apps may not reliably receive the real guide button.
- The solution must not depend on unsupported assumptions about controller APIs.
- The home action must integrate with runtime session handling.

Implementation guidance:
- Prefer an abstraction that can later support remapping.
- Keep polling/event logic efficient.
- Document clearly which binding is default vs ideal vs fallback.

Validation:
- Ensure the home action can return to CryoPod from the shell and from a running game context where applicable.
- Confirm keyboard fallback always works.

Deliverables:
- Code changes
- Summary of controller API choice and guide-button limitations
- Default binding recommendations
```

#### Track G prompt: Persistence, caching, and settings

```text
You are implementing Track G for CryoPod in the repository at <repo_root>.

Goal:
Create a durable local data layer for library state, artwork cache metadata, runtime sessions, user overrides, and settings.

Required outcomes:
1. Introduce a local persistence strategy, preferably SQLite plus filesystem cache folders.
2. Persist discovered games, source identifiers, canonical matches, artwork metadata, launch options, suspend policies, and last-known runtime session state.
3. Add repository-safe abstractions for reading/writing settings and cache entries.
4. Support startup reload of cached library state.
5. Prepare for future import/export without building it fully unless already needed.

Constraints:
- Keep persistence schema explicit and versionable.
- Avoid scattering raw file I/O and SQL throughout UI code.
- Cache corruption or missing files should degrade gracefully and visibly.

Implementation guidance:
- Separate repositories/stores from domain services.
- Add schema initialization/migration support suitable for the app stage.
- Use stable paths under app-local storage/cache directories.

Validation:
- Ensure data survives restart.
- Ensure artwork paths and settings reload correctly.
- Add tests for schema/model mapping where feasible.

Deliverables:
- Code changes
- Summary of persistence layout and storage locations
- Known migration or cache cleanup concerns
```

#### Track H prompt: Packaging, permissions, and startup behavior

```text
You are implementing Track H for CryoPod in the repository at <repo_root>.

Goal:
Make CryoPod deployable and practical as a Windows launcher shell, including packaging strategy, permissions, startup behavior, and crash recovery considerations.

Required outcomes:
1. Evaluate packaged vs unpackaged tradeoffs for WinUI 3 and the Windows APIs CryoPod needs.
2. Update app manifest/configuration as needed for chosen capabilities.
3. Add optional start-with-Windows or document how it should be added safely.
4. Support startup into fullscreen launcher mode.
5. Add crash/restart recovery behavior for previously tracked running or suspended sessions.

Constraints:
- Suspend/process/window management may behave differently depending on packaging model.
- Do not choose a packaging strategy without documenting the tradeoff.
- Any startup behavior must be opt-in or clearly controlled.

Implementation guidance:
- Base decisions on the actual APIs needed for process/window/controller integration.
- Keep recovery conservative and honest when session state is stale.

Validation:
- Ensure the chosen configuration still builds and runs.
- Verify startup behavior and recovery messaging are sensible.

Deliverables:
- Code/config changes
- Packaging recommendation with rationale
- Startup/recovery behavior summary
```

### Prompt set B: Milestone-by-milestone execution

#### Milestone 0 prompt: Research + architecture spike

```text
You are performing Milestone 0 for CryoPod in <repo_root>.

Mission:
Run a feasibility and architecture spike before full implementation. Focus on de-risking fullscreen shell behavior, controller input, launch tracking, foreground switching, and suspend experiments on Windows.

Tasks:
1. Verify WinUI 3 fullscreen shell behavior.
2. Test controller input APIs viable for this app.
3. Test launching a game and capturing the real process tree.
4. Test switching focus back to CryoPod from a running game.
5. Prototype process suspend/resume on 3-5 representative games or sample processes.
6. Document failures by game type and define capability tiers.

Required output:
- Concrete feasibility findings, not generic advice.
- Recommended architecture boundaries for shell, discovery, runtime, and interop.
- A suspend compatibility matrix and risk summary.
- Any code spikes needed to prove the approach, but keep them clean or isolated.

Success criteria:
- Controller approach selected
- Foreground switching approach selected
- Suspend capability tiers defined with evidence
```

#### Milestone 1 prompt: Shell MVP

```text
You are implementing Milestone 1 for CryoPod in <repo_root>.

Mission:
Deliver a polished fullscreen shell MVP using placeholder data, with controller-first navigation and TV-friendly visuals.

Tasks:
1. Build shell window behavior and theming.
2. Implement grid layout and navigation.
3. Add placeholder game cards and a details view.
4. Add settings shell/navigation.

Requirements:
- Fullscreen-first feel
- Keyboard fallback
- Clear focus states
- MVVM-friendly structure

Success criteria:
- Entire launcher can be navigated with controller/keyboard
- App feels stable in fullscreen on desktop/TV
```

#### Milestone 2 prompt: Discovery MVP

```text
You are implementing Milestone 2 for CryoPod in <repo_root>.

Mission:
Replace placeholder library data with real local game discovery, starting with Steam.

Tasks:
1. Implement Steam discovery.
2. Add local cache and refresh flow.
3. Add manual rescan support.
4. Bind the real library into the UI and details page.

Requirements:
- Stable game IDs
- Clean source abstraction for later launcher support
- Persistence across restart

Success criteria:
- Installed Steam games populate automatically
- Library survives restart and supports rescan
```

#### Milestone 3 prompt: Artwork + metadata MVP

```text
You are implementing Milestone 3 for CryoPod in <repo_root>.

Mission:
Enrich discovered games with canonical metadata and SteamGridDB artwork so the launcher looks production-like.

Tasks:
1. Normalize discovered titles.
2. Implement SteamGridDB search/match.
3. Download and cache preferred artwork.
4. Add fallback art and support manual correction hooks.

Requirements:
- Confidence scoring
- Cache persistence
- UI resilience when art is missing

Success criteria:
- Most discovered titles display useful artwork
- Wrong matches can be corrected or overridden
```

#### Milestone 4 prompt: Launch + running-state tracking

```text
You are implementing Milestone 4 for CryoPod in <repo_root>.

Mission:
Launch games from the CryoPod UI, detect the actual running game session, and show runtime state in the launcher.

Tasks:
1. Launch from tile/details view.
2. Detect running state and main process/window.
3. Maintain currently running section.
4. Add manual return-to-launcher behavior.

Requirements:
- Process-tree-aware tracking
- Launcher bootstrapper support
- Reliable foreground switching

Success criteria:
- Local test titles can be launched and tracked
- User can return to CryoPod while the game remains active
```

#### Milestone 5 prompt: Suspend beta

```text
You are implementing Milestone 5 for CryoPod in <repo_root>.

Mission:
Ship the first usable beta of CryoPod's flagship suspend-related behavior using capability tiers instead of pretending every game supports quick resume.

Tasks:
1. Implement return-only mode globally.
2. Implement minimize/hide where possible.
3. Add experimental process freeze mode.
4. Add per-game compatibility rules.
5. Add warnings, unsupported states, and recovery actions.

Requirements:
- Honest UX for risky titles
- Force-kill/recovery path
- State persistence where sensible

Success criteria:
- Multiple test games can be kept in known states
- Unsupported games fail safely and clearly
```

#### Milestone 6 prompt: Multi-launcher expansion

```text
You are implementing Milestone 6 for CryoPod in <repo_root>.

Mission:
Expand CryoPod beyond Steam to support additional Windows PC launchers cleanly.

Tasks:
1. Add discovery for Epic, GOG, and Xbox app.
2. Handle launcher-specific quirks.
3. Merge canonical identities across sources so the same title is not duplicated incorrectly.

Requirements:
- Keep source-specific code isolated
- Reuse the existing library/discovery abstractions
- Preserve user overrides and metadata choices

Success criteria:
- Multiple launcher ecosystems populate in one library
- Duplicate identity handling is predictable and correct
```

#### Milestone 7 prompt: Polish and hardening

```text
You are implementing Milestone 7 for CryoPod in <repo_root>.

Mission:
Harden CryoPod into a stable daily-driver experience.

Tasks:
1. Improve performance for large libraries.
2. Add diagnostics and error reporting.
3. Improve startup recovery.
4. Improve offline behavior.
5. Add richer sorting/filtering/search.
6. Improve accessibility and TV readability.

Requirements:
- Do not regress the core launch/suspend flows
- Prefer measured improvements over speculative ones

Success criteria:
- Large libraries remain responsive
- Recovery and diagnostics are useful
- UI is clearer and more resilient
```

### Prompt set C: Immediate next coding tasks

#### Task 1 prompt: Restructure scaffold into app/core/infrastructure boundaries

```text
You are implementing immediate task 1 for CryoPod in <repo_root>.

Goal:
Refactor the current WinUI 3 scaffold into clearer architectural boundaries before feature complexity grows.

Do the following:
1. Inspect the current project structure.
2. Introduce app/core/infrastructure boundaries appropriate for this repository's stage.
3. Move models, interfaces, services, and Windows-specific code toward the right layers.
4. Keep the app building throughout.
5. Update namespaces, references, and bootstrap wiring cleanly.

Constraints:
- Keep changes surgical and coherent.
- Do not add abstractions with no near-term use.
- Favor a structure that directly supports discovery, runtime tracking, artwork, and suspend work.

Deliver:
- Code changes
- Short explanation of why the chosen project/module split is appropriate now
```

#### Task 2 prompt: Implement fullscreen shell layout and controller navigation skeleton

```text
You are implementing immediate task 2 for CryoPod in <repo_root>.

Goal:
Create the first real fullscreen shell with controller-oriented navigation skeleton.

Do the following:
1. Make the main window fullscreen/borderless-ready.
2. Build a root shell layout with navigation regions and placeholder game cards.
3. Add focus management and navigation flow that works with keyboard and controller abstraction.
4. Add obvious focus/selection states.
5. Keep logic structured for MVVM growth.

Deliver:
- Code changes
- Brief note on how to extend the navigation model later
```

#### Task 3 prompt: Add a local data layer

```text
You are implementing immediate task 3 for CryoPod in <repo_root>.

Goal:
Add a local persistence and cache foundation, likely SQLite plus cache folders, to support the launcher's upcoming data needs.

Do the following:
1. Choose and wire an appropriate local storage mechanism for this WinUI/.NET app.
2. Add schema/models/repositories for discovered games, artwork metadata, user overrides, and runtime session state scaffolding.
3. Add filesystem cache path handling for downloaded assets.
4. Add initialization/bootstrap logic and basic migration/versioning support.

Constraints:
- Keep persistence out of the UI layer.
- Design for future discovery/artwork/runtime features without overbuilding.

Deliver:
- Code changes
- Storage layout summary
```

#### Task 4 prompt: Implement Steam library discovery

```text
You are implementing immediate task 4 for CryoPod in <repo_root>.

Goal:
Discover locally installed Steam games and feed them into CryoPod's library.

Do the following:
1. Locate Steam install/manifests on Windows.
2. Parse Steam library folders and app manifests.
3. Resolve stable game identifiers and launch metadata.
4. Expose results through the app's library/discovery service.
5. Add refresh/rescan capability.

Constraints:
- Keep Steam-specific parsing isolated from generic library logic.
- Handle missing or malformed manifests gracefully.

Deliver:
- Code changes
- Summary of what Steam metadata is captured and any known gaps
```

#### Task 5 prompt: Build the canonical game model and library service

```text
You are implementing immediate task 5 for CryoPod in <repo_root>.

Goal:
Create the core domain model for games and a library service that the UI and discovery/artwork/runtime systems can all depend on.

Do the following:
1. Define canonical entities such as Game, GameInstall, GameSource, ArtworkSet, LaunchProfile, RuntimeSession, and SuspendPolicy as appropriate.
2. Create a library service that merges discovery results with persistence and exposes UI-friendly library state.
3. Ensure the model supports later multi-source identity merging and user overrides.

Constraints:
- Keep the domain model clean and not UI-specific.
- Avoid duplicating concepts across discovery and runtime code.

Deliver:
- Code changes
- Summary of the canonical model and why it will scale
```

#### Task 6 prompt: Integrate SteamGridDB search/download/cache

```text
You are implementing immediate task 6 for CryoPod in <repo_root>.

Goal:
Connect discovered games to SteamGridDB so CryoPod can fetch and cache polished artwork.

Do the following:
1. Add a SteamGridDB client abstraction.
2. Implement title normalization and search/match logic.
3. Download preferred artwork variants and store them in cache.
4. Persist artwork metadata and bind it into the UI.
5. Handle API configuration, rate limits, and missing matches cleanly.

Constraints:
- Do not hardcode secrets.
- Add sensible fallback behavior when no art is found.

Deliver:
- Code changes
- Summary of API setup requirements and cache behavior
```

#### Task 7 prompt: Implement launch tracking and return-to-shell

```text
You are implementing immediate task 7 for CryoPod in <repo_root>.

Goal:
Launch games from CryoPod, track their runtime session, and let the user jump back to CryoPod without closing the game.

Do the following:
1. Launch using the stored launch profile.
2. Detect the real game process and main window, including child-process cases.
3. Update runtime state in the app.
4. Implement a user action that returns focus to CryoPod.
5. Surface running sessions in the UI.

Constraints:
- Windows foreground activation behavior may need careful handling.
- Avoid brittle heuristics when tracking bootstrapper launchers.

Deliver:
- Code changes
- Summary of how runtime sessions are tracked
```

#### Task 8 prompt: Build suspend capability tiers and start with return-only mode

```text
You are implementing immediate task 8 for CryoPod in <repo_root>.

Goal:
Lay the foundation for CryoPod's suspend system by introducing capability tiers and shipping return-only mode first.

Do the following:
1. Define suspend capability tiers and per-game policy models.
2. Implement return-only mode end to end.
3. Prepare the runtime/session model for later minimize/hide and process-freeze modes.
4. Add UI states and warnings that make the current support level explicit.

Constraints:
- Do not imply universal suspend support.
- Keep the design ready for minimize/hide and experimental freeze later.

Deliver:
- Code changes
- Summary of policy tiers and how future suspend modes will plug in
```
