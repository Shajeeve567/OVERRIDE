# Ilzam - Core Developer Progress and Handover

Last updated: **21 October 2026**  
Project: **OVERRIDE**  
Modules: **Graphics and Visualization (G&V) + Intelligent Systems (IS)**  
Primary development history: **`Core_Dev`**  
Current working branch: **`feature/warden-gameplay-2`**  
Current work includes the full-game integration baseline plus the standalone Warden gameplay adapter and reusable prefab.  
Ilzam has stated the submission deadline is the **25th**; verify the exact month/time on the official submission portal.

This file is the authoritative local handover for Ilzam's work. Read it before changing the Warden, Core Developer assets, sensors, or integration scenes.

## 1. Ownership and boundaries

### Ilzam owns

- G&V custom assets: Wisp drone, override terminal, player rifle, refined Warden, UVs, textures, Unity materials, import settings, reusable visual prefabs, optimization evidence, and Warden weak points/attachment references.
- IS agent: Warden/Duelist depth-2 Expectimax brain, player-response probability learning, evaluation weights, edge cases, debug scores and technical justification.
- Shared infrastructure: reusable vision sensor and a reusable hearing sensor once the team's real noise contract is integrated.
- Team duties: build/release management and helping edit the three-minute demo video.

### Ilzam does not own

- Main gameplay scene: World Builder owns it.
- Shared `WorldSnapshot`, `AgentCommand`, `IAgentBrain` and `AgentMotor`: Agent Controller owns/stewards these contracts.
- Wisp pathfinding, steering and animation: Agent Controller owns these.
- Player weapon, enemy damage/health, terminals and noise emitters: Systems Engineer owns these.
- Director and final world integration: World Builder owns these.

Do not independently edit team-owned systems merely to make the Warden compile. Use Warden-specific adapters until shared interfaces are agreed.

## 2. Current status summary

| Area | Status | Evidence/current limitation |
|---|---|---|
| Required Wisp, terminal and rifle models | Complete | Blender sources, FBX exports, UVs, textures, materials and prefabs exist. |
| Refined Warden model | Complete | Default refined visual plus preserved legacy prefab. |
| Weak points and attachment references | Complete | Eye/rear colliders and cached eye/muzzle/rear transforms. |
| Reusable vision sensor | Complete | Standalone test scene and live ARGUS integration scene. |
| Pure depth-2 Expectimax | Complete | Seven focused tests pass. |
| Standalone Expectimax demonstration | Complete | Candidate scores, probabilities, invalid reasons and controls are visible. |
| ARGUS/Warden gameplay integration | Complete in isolated test | Uses live player, Director, sensors, movement, firing, reload and authored cover markers. Manual tactical movement was verified by Ilzam. |
| Hearing sensor | Complete | Uses `NoiseBus`, obstruction attenuation and confirmed-event memory; six focused tests pass. |
| Real Warden movement/shooting adapter | Complete as a Warden-specific adapter | Uses `AgentMotor`, `Health` and a Warden-owned weapon without changing shared contracts. Cover markers remain authored, not automatic cover discovery. |
| Design log and assessment evidence | Substantially complete | Technical claims were corrected against the implementation; screenshots/video and final profiler evidence remain manual tasks. |
| Final four-agent gameplay/build | Team integration incomplete | Requires coordinated merges and main-scene work. |

Individual implementation is substantially complete. Remaining work is final main-scene coordination, multi-agent/manual smoke testing, profiling evidence and submission media rather than another major Warden subsystem.

## 3. Graphics work completed

### 3.1 Source and export pipeline

| Asset | Blender source | Unity FBX |
|---|---|---|
| Rifle | `ArtSource/Rifle/Rifle.blend` | `Assets/_Project/Art/Models/Rifle/Rifle.fbx` |
| Terminal | `ArtSource/Terminal/Terminal.blend` | `Assets/_Project/Art/Models/Terminal/Terminal.fbx` |
| Wisp | `ArtSource/Wisp/Wisp.blend` | `Assets/_Project/Art/Models/Wisp/Wisp.fbx` |
| Original Warden | `ArtSource/Warden/Warden.blend` | `Assets/_Project/Art/Models/Warden/Warden.fbx` |
| Refined Warden | `ArtSource/Warden/Warden_Refined.blend` | `Assets/_Project/Art/Models/Warden/Warden_Refined.fbx` |

Completed work includes clean FBX exports, texture relinking, Unity-generated metadata, URP materials, importer remaps and reusable visual prefabs. The terminal's unused material was removed.

### 3.2 Audited model statistics

| Model | Triangles | UV status |
|---|---:|---|
| Rifle | 3,504 | UV mapped |
| Terminal | 1,898 | UV mapped |
| Wisp | 4,142 | UV mapped |
| Original Warden | 4,652 | UV mapped |
| Refined Warden | 5,064 | All seven meshes UV mapped |

The refined Warden is below the planned 10,000-triangle budget.

### 3.3 Refined Warden

The refined Warden was regenerated through the tested `Build_in_Blender.py` process after a manual mesh-only export lost required hierarchy data.

Verified structure:

- Seven meshes, 2,710 vertices and 5,064 triangles.
- Seven pivot empties.
- `VisionEye`, `RearWeakPoint` and `Muzzle` attachment empties.
- Correct base, torso, head, gun-arm and utility-arm parenting.
- Eye under head, muzzle under gun forearm and rear weak point under torso.
- Pivot movement checked for torso, head and gun arm.
- Eye and muzzle forward axes corrected to face the Warden's actual front.

Prefabs:

- Default: `Assets/_Project/Prefabs/Visuals/WardenVisual.prefab`
- Legacy fallback: `Assets/_Project/Prefabs/Visuals/WardenVisualLegacy.prefab`

The original Warden was deliberately preserved rather than overwritten. The default includes seven renderers, cached eye/muzzle/rear references, a configured `VisionSensor`, an eye sphere weak point (`0.16` radius, `2x`) and rear box weak point (`0.42 x 0.36 x 0.20`, `2x`).

### 3.4 Textures and import decisions

- Refined Warden texture size: `1024 x 512`.
- Base colour and emission import as sRGB.
- Metallic, roughness and packed metallic/smoothness maps import as non-sRGB data.
- `WardenRefined_UnityMetallicSmoothness.png` is packed for Unity/URP.
- Compression and mipmaps are enabled.
- Equivalent texture/material work exists for rifle, terminal, Wisp and original Warden.

### 3.5 Current scale decision

- Shared default Warden prefab root scale: `0.39`, approximately `1.0 m` rendered height, from the earlier decision to make the hovering Warden roughly half player height.
- Wisp root scale: `0.70`, approximately `1.03 m` across.
- Rifle remains `1.0`.
- The ARGUS integration scene overrides only its Warden instance to `0.77`, approximately player height (`~2 m`), because `0.39` looked too small beside the live player.
- The shared prefab was intentionally not changed again until Ilzam approves the new size visually.

## 4. Vision sensor

Component: `Assets/Scripts/Sensors/VisionSensor.cs`

Implemented features:

- Configurable eye and target transforms.
- Range, field of view, obstacle mask and `0.2 s` default interval.
- Obstruction raycast.
- `CanSeeTarget`, `HasSeenTarget`, `LastSeenPosition`, `LastSeenTime`.
- Memory updates only on confirmed sight.
- Safe missing/inactive-target handling.
- Cached references; no repeated `Find` calls.
- Range/FOV/current-visibility/last-seen gizmos.

Standalone scene: `Assets/Scenes/WardenVisionTest.unity`

Manually verified: visible target, outside FOV, outside range, wall obstruction, losing/reacquiring sight, and remembered position/time remaining unchanged while hidden.

Historical bug: `VisionSensorTestRig` initially passed a null `MaterialPropertyBlock` into `Renderer.GetPropertyBlock`, causing an `ArgumentNullException`. It was fixed by allocating the block before use.

## 5. Warden depth-2 Expectimax

Pure C# implementation: `Assets/Scripts/AI/Warden/`

- `WardenExpectimaxBrain.cs`
- `WardenDecisionTypes.cs`
- `WardenResponseProbabilityModel.cs`
- `Override.WardenAI.asmdef`

The brain has no dependency on visuals, physics, movement or shooting.

### Search convention

Production depth two means: Warden action -> player chance response -> second Warden action -> second player chance response -> leaf evaluation. A one-Warden-turn baseline exists for tests only.

Actions: Hold, Peek/fire, Advance to cover, Flank, Retreat/reload.  
Responses: player Peeks, Pushes or Hides.

Probability learning uses smoothing pseudocounts and accepts only directly reported, monotonically sequenced observations. It rejects stale, duplicate and hidden/inferred reports and does not learn once per frame.

Configurable scoring covers damage dealt/taken, cover, preferred range, ammo and desperation-weighted aggression. Debug results expose every candidate score, invalid reasons, probabilities, depth, leaf count and fallback details.

Handled edge cases include hidden/unknown player, range, empty ammo, missing paths/options, unavailable or occupied cover, invalid numbers, null state and no valid action. The safe fallback is inert `Hold`.

### Automated tests

- `Assets/Tests/Editor/WardenExpectimaxBrainTests.cs`
- `Assets/Tests/Editor/Override.WardenAI.Tests.asmdef`

Seven tests cover expected value, depth expansion, probability learning, invalid filtering, empty-ammo retreat, no-action fallback and invalid/null state.

## 6. Expectimax demonstration scenes

### 6.1 Standalone artificial-input demo

Scene: `Assets/Scenes/WardenExpectimaxTest.unity`  
Rig: `Assets/Scripts/AI/WardenExpectimaxTestRig.cs`

This complete scene displays selected action, all five scores, invalid reasons, learned probabilities, depth, leaf count, tactical controls, forced fallback and reset. It intentionally does not move or shoot.

### 6.2 Live ARGUS integration decision preview

Scene: `Assets/Scenes/WardenArgusIntegrationTest.unity`  
Runtime rig: `Assets/Scripts/AI/WardenArgusIntegrationRig.cs`  
Generator: `Assets/Editor/WardenArgusIntegrationBuilder.cs`

The local integration branch merges `origin/feature/argus-core` and uses a separate copy of `Assets/OVERRIDE/Greybox/Greybox_v2.unity`; the World Builder's source scene is not modified.

The scene contains:

- Complete ARGUS greybox, live player and Director.
- Refined stationary Warden in the Labs.
- Live vision and last-seen memory against the real player.
- Expectimax decisions every `~0.4 s`.
- Candidate scores, invalid reasons, probabilities and selected action.
- Automatic observations: reacquisition -> Peek; visible approach of `1.5 m` -> Push; confirmed loss of sight -> Hide.
- Manual `P/U/H` observations.
- Live desperation from lost ARGUS subsystems/final wave.
- Simulated ammo, cover and paths because real providers do not exist.
- Red/green visibility beacon and two explicit covers.

Controls:

- `WASD` + mouse: player movement/look.
- `F2`: Warden panel; `F3`: Director panel.
- `P/U/H`: Peek/Push/Hide.
- `M`: ammo; `C`: current cover; `G/F/T`: advance/flank/retreat availability.
- `R`: reset learning and simulated inputs.

Limitations:

- Actions are previews only; no movement, firing or damage is executed.
- The Warden is stationary to isolate sensing/decisions.
- Actual player position is used only while visible; hidden decisions use confirmed memory.
- Corrected Labs placement, `0.77` instance scale and `0.08` mouse sensitivity require final Play Mode approval.

## 7. Third-party weapon visuals

- Licensed sources: commit `94d29b5`.
- Reusable prefabs/showcase: commit `00f21b1`.
- Three visuals were manually visible in the showcase.
- The Blender rifle initially faced downward; presentation orientation was corrected without modifying source FBX geometry.

Historical builder issue: saving a preview/already-open scene caused `EditorSceneManager.SaveScene` failures. The builder was changed to use a normal saved scene and avoid overwriting an open destination. If it recurs, close the showcase scene, open a saved scene, then use `OVERRIDE > Core Developer > Rebuild Third-Party Weapon Visuals`.

## 8. Repository and integration state

### Branches

- `Core_Dev` and `origin/Core_Dev`: `bec4d73`.
- Local integration branch: `integration/warden-argus`.
- Integration merge commit `57d8d18` combines Core work with `origin/feature/argus-core` at `98bc161`.
- Integration test commit: `b34f18a` (`[AI] Add Warden ARGUS integration test`).
- Nothing from the newest integration work had been pushed when this handover was updated.

### Pull requests checked on 20 October 2026

- PR #1: `feature/pathfinder-astar -> dev`, clean/mergeable. It changes shared contracts/namespaces: `https://github.com/Shajeeve567/OVERRIDE/pull/1`
- PR #2: `Core_Dev -> dev`, open at `bec4d73`; GitHub reported mergeability as unknown: `https://github.com/Shajeeve567/OVERRIDE/pull/2`
- PR #3: `world-builder -> dev`, open but dirty/conflicting: `https://github.com/Shajeeve567/OVERRIDE/pull/3`
- `feature/interactions` contains required noise contracts but has no open PR.

Recommended integration discipline:

1. Owners/reviewers merge or resolve team PRs into `dev`.
2. Merge refreshed `dev` into the relevant feature/integration branch.
3. Resolve namespace/GUID conflicts carefully.
4. Compile and rerun tests after every merge.
5. World Builder alone places the verified Warden in the main scene.

### Local stashes and unrelated change

- `stash@{0}`: `integration-prep-warden-material-editor-change`.
- `stash@{1}`: `integration-prep-ilzam-local-editor-files`.
- `Assets/_Project/Art/Materials/WardenRefined_Atlas.mat` is currently modified again with the same material-style change: remove `_EMISSION` and assign `_MainTex`. Keep it separate from the integration commit unless explicitly approved.
- `Ilzam's Agent Context/` is ignored by `.gitignore`; this progress file is a local agent handover unless deliberately force-added by the team.

## 9. Verification results

Latest verification after generating the corrected integration scene:

- Unity 6000.3.25f1 compilation: passed.
- Integration generator reference validation: passed.
- Combined Edit Mode tests: **23/23 passed** (Warden 7/7, Director 16/16).
- Test XML: `C:/tmp/override-warden-argus-tests.xml`.
- Logs: `Logs/WardenArgusIntegrationBuild.log` and `Logs/WardenArgusIntegrationTests.log`.

Still requires manual verification:

- Corrected Labs scene feels navigable.
- Mouse look no longer shakes at sensitivity `0.08`.
- Warden at `0.77` looks appropriate beside player.
- Beacon/HUD switches through visible, blocked, outside-FOV and reacquired states.
- Automatic Peek/Push/Hide observations alter probabilities sensibly.

## 10. Known gotchas and lessons

1. **Unity metadata must be Unity-generated.** Let Unity import assets rather than inventing GUIDs.
2. **Do not export only selected meshes when Blender hierarchy empties are required.** Use the tested Warden builder/export workflow.
3. **Refined and legacy Wardens must coexist.** Do not delete the fallback while integration remains unfinished.
4. **Greybox colliders were on `Default`, while VisionSensor expected `VisionObstacle`.** The integration generator changes layers only in its copied test scene; source Greybox remains untouched.
5. **The first integration placement used the airlock and obstructed its exit.** Corrected scene uses Labs with a clear centre route.
6. **PlayerLook reads raw mouse delta.** Prefab sensitivity `1.2` felt unstable; test-scene instance uses `0.08`. Do not change the team prefab without its owner.
7. **Weak-point colliders are solid.** The player cannot walk through the Warden; expected. Blocking the only route is not expected.
8. **Default and test scale intentionally differ.** Shared prefab `0.39`; pending player-sized integration instance `0.77`.
9. **Unity headless processes may outlive the launching shell.** Check `Unity.exe` and logs before another run. A stale `bee_backend` lock referenced dead PID `21984`; stopping only the agent-started headless process and retrying fixed it.
10. **Compile success is not Play Mode verification.** Visibility, navigation feel, proportions and input need human observation.
11. **Do not invent a second noise contract.** Use `Override.Core.NoiseEvent` and `NoiseBus` from `feature/interactions` after coordinated integration.
12. **Do not give the brain hidden information.** Hidden decisions use confirmed memory, not the target's current transform.

## 11. What remains and what blocks it

### Priority A - Manually approve corrected integration scene

Unblocked and immediate:

- Open `Assets/Scenes/WardenArgusIntegrationTest.unity`.
- Confirm scale, mouse feel and navigation.
- Walk behind both covers, outside FOV/range and back into sight.
- Observe beacon, memory, probabilities and candidate scores.

### Priority B - Assessment evidence and design log

Unblocked and high-value. Create `Docs/DesignLog/Ilzam.md` with:

- Blender solid/wireframe screenshots for each owned model.
- UV layouts, topology/edge-flow notes and triangle counts.
- Texture maps, import settings and compression/mipmap evidence.
- Original versus refined Warden.
- Pivot hierarchy, muzzle/eye references and weak-point colliders.
- Scale decisions and reasoning.
- Expectimax depth diagram, evaluation equation/weights and leaf counts.
- Scores/probabilities before and after repeated observations.
- Edge cases: hidden player, range, empty ammo, missing path, occupied/destroyed cover and fallback.
- Why Expectimax instead of Minimax or Utility AI.
- AI-assisted workflow disclosure and manual verification.

This directly supports G&V role quality, workflow/tools, optimization justification and Git cleanliness, plus IS originality, complexity and viva justification.

### Priority C - Hearing sensor

The code dependency exists on `origin/feature/interactions` at `85ee449`:

- `Assets/OVERRIDE/Scripts/Core/NoiseEvent.cs`
- `Assets/OVERRIDE/Scripts/Core/NoiseBus.cs`
- `Assets/OVERRIDE/Scripts/Player/FootstepNoise.cs`

Blocker: the branch is not merged and has no open PR. Ask the Systems Engineer to open/merge it, or explicitly authorize a focused integration/cherry-pick.

After integration, implement configurable radius (`~15 m` Warden default), safe subscription/unsubscription, type/radius/position/time handling, last-heard memory, no repeated searches, and tests for radius, stale event, disabled listener and supported obstruction rules.

### Priority D - Real Warden gameplay adapter

Partially blocked by missing/unstable providers:

- Semantic cover availability, occupancy and destruction from World Builder.
- Path/motor execution from Agent Controller.
- Enemy ammo/reload/fire and health/damage from Systems Engineer.
- Stable shared namespaces after PR #1.
- Terminal progression/desperation from Director/main game.

The World Builder branch has physical cover prefabs and waypoints but no semantic cover-point service. The integrated `Gun` is player-oriented and exposes no Warden enemy weapon API. Full chase/shoot now would invent team contracts and is not recommended.

When providers exist, add a Warden-specific adapter that builds `WardenDecisionState` every `~0.4 s`, records legitimate observations, sends actions to team APIs and exposes debug results. Keep the pure brain unchanged.

### Priority E - Team integration and release

- Get PR #2 reviewed/merged to `dev`, never directly to `main`.
- Resolve PR #3 through its owner.
- Reintegrate latest `dev` and rerun tests.
- World Builder wires the approved Warden into main scene.
- Verify all four agents simultaneously.
- Smoke test terminal hacking, death/respawn and win/lose loop.
- Profile on weakest laptop.
- Perform clean-clone/import and final build checks.
- Capture/edit the three-minute demo.

### Lower-priority optimization

- Record Expectimax decision time as well as leaf count.
- Capture texture compression/import evidence.
- Decide whether LODs are worth late risk; stable evidence is more valuable than rushed LOD generation.

## 12. Recommended next sequence

1. Manually approve corrected Warden ARGUS scene.
2. Capture integration screenshots/video immediately.
3. Write `Docs/DesignLog/Ilzam.md` and viva notes.
4. Coordinate noise-contract integration and implement hearing/tests.
5. Refresh from `dev` after PR decisions and resolve conflicts.
6. Implement movement/shooting only against confirmed interfaces.
7. World Builder integrates prefab into main scene.
8. Run four-agent smoke/performance/clean-build tests and finish video.

Avoid further model redesign unless a serious defect appears. The largest remaining marks are in visible intelligent behaviour, evidence, technical justification and stable group integration.

## 13. Definition of done

Ilzam's work is complete when:

- Core work is reviewed and merged to `dev`.
- Refined Warden appears at an approved scale in integrated game.
- Vision works against live player and real obstacles.
- Depth-2 decisions run at the planned interval.
- Learned probabilities and candidate scores are visible.
- Actions safely reach real movement/weapon systems, or the missing dependency is formally documented if scope is cut.
- Hearing works with the real noise contract, or the unmerged dependency is formally evidenced.
- Relevant tests pass after final merges.
- Graphics evidence, design log and viva notes are complete.
- All four agents coexist in a stable build.
- Final smoke test, profiler evidence, clean build and three-minute video are complete.

## 14. Seeker and Warden Gameplay Debugging Update (21 October 2026)

### 14.1 Active Branch
- Branch: **`feature/warden-gameplay-2`** (branched from `feature/warden-gameplay-integration`)
- Target: Full playable Warden gameplay loop and asset handover.

### 14.2 Seeker Asset Pipeline
- **Blender source**: `ArtSource/Seeker/Seeker_Hunter.blend`
- **Unity FBX**: `Assets/_Project/Art/Models/Seeker/Seeker_Hunter.fbx`
- **Material & Textures**: Single material atlas `Assets/_Project/Art/Materials/Seeker_Atlas.mat` (URP Lit) referencing `T_Seeker_Atlas_Albedo.png`, `T_Seeker_Atlas_MetallicSmoothness.png`, `T_Seeker_Atlas_Normal.png`, and `T_Seeker_Atlas_Emission.png`.
- **Mesh statistics**: 13 child meshes, 6,448 triangles, fully UV mapped without overlap.
- **Components & Prefabs**:
  - `Assets/Scripts/Sensors/SeekerVisualReferences.cs` (cached head, thruster, claws, eye, weak point).
  - `Assets/Scripts/Sensors/SeekerWeakPoint.cs` (`IDamageable` multiplier with critical damage routing).
  - `Assets/_Project/Prefabs/Visuals/SeekerVisual.prefab`.
- **Builder**: `Assets/Editor/SeekerAssetBuilder.cs` with CLI method `Override.EditorTools.SeekerAssetBuilder.BuildFromCommandLine`.

### 14.3 Warden Gameplay Debugging (Bugs A, B, C Fixed)
1. **Bug A (Player HP "Unavailable" in Debug Panel)**:
   - **Root cause**: Test scene copied `Player_Legacy.prefab`, which lacked the Systems Engineer's `Health` component.
   - **Fix**: Updated `WardenGameplayIntegrationBuilder.cs` to instantiate the real `Assets/OVERRIDE/Prefabs/Player.prefab` (with `Health`, `CombatOverlay`, and `FootstepNoise`) and wire references.
2. **Bug B (Warden Not Turning Toward Heard Footsteps While Hidden)**:
   - **Root cause**: `Player_Legacy` lacked `FootstepNoise.cs` so no events were emitted to `NoiseBus`. In addition, `AgentMotor.cs` ignores `command.LookTarget` when stationary (`command.Path == null`), leaving stationary rotation unhandled.
   - **Fix**: Decoupled `WardenGameplayController.UpdateFacing(deltaTime)` for smooth rotation toward confirmed memory/sound events, and updated `UpdateConfirmedKnowledge()` to prioritize newer sound events over older sight memory.
3. **Bug C (Stuck at 0/12 Ammo When Cover Unavailable)**:
   - **Root cause**: `_weapon.StartReload()` was only triggered when `RetreatAndReload` was selected. When no retreat cover was available, retreat and firing were marked `INVALID`, leaving the brain in `Hold` with no reload trigger, causing permanent ammo starvation.
   - **Fix**: Added safe in-cover and zero-ammo reload triggers to the default `Hold` action in `WardenGameplayController.ApplyDecision()`.

### 14.4 Automated Batchmode Test Results (All 39 Passing)
Full Unity batchmode EditMode test run completed with **exit code 0 (Ok)**:
- `DirectorBrainTests`: 16/16 passed
- `SeekerAssetTests`: 3/3 passed
- `HearingSensorTests`: 6/6 passed
- `WardenExpectimaxBrainTests`: 7/7 passed
- `WardenGameplayAdapterTests`: 7/7 passed (including new tests for empty ammo reload, noise rotation, and player health binding)
- **Total**: **39/39 tests passed (100%)**, 0 failed, 0 skipped.

### 14.5 Gameplay Loop, Movement Logic & Health Polish (21 October 2026)
1. **Weapon Self-Damage & Disappearance Eliminated**:
   - `WardenWeapon.cs` switched from single raycast to `Physics.RaycastAll` filtering out the shooter's own root hierarchy (`candidate.collider.transform.root == transform.root`).
   - `Health.disableOnDeath` set to `false` on Warden; death now halts movement and weapon triggers cleanly while keeping the refined 3D model and diagnostic GUI active.
2. **Hearing Sensitivity & Obstruction Boost**:
   - Added `HearingSensor.SensitivityMultiplier` (2.5x), `HearingRange = 25m`, and `ObstructedRangeMultiplier = 0.8f` to allow hearing walking footsteps (~6m radius) through cover up to 12m away.
3. **Player (200 HP) and Warden (350 HP) Runtime Enforcement**:
   - Implemented `WardenGameplayController.ApplyRuntimeConfiguration()` in `Awake()`, `Start()`, and `CachePlayerReferences()`. Even if the active scene was saved with older defaults, health values and hearing parameters are enforced at runtime to ensure immediate player display of `HP 200` and Warden `350 HP`.
4. **Logical Movement & Continuous Threat Facing**:
   - **Facing during movement**: Removed `(_command.Path != null && _command.Path.Length > 0)` restriction from `UpdateFacing()`. The Warden now strafes/backpedals while maintaining aim at `_lastKnownPosition`. This prevents him from showing his back to the player, keeps his 120° FOV pointed at the player's cover, and enables instant reacquisition the moment the player peeks out.
   - **Cover quality rebalancing**: Previous setup assigned retreat cover quality `0.90` (higher than advance `0.80` and flank `0.75`), causing Expectimax to mathematically favor `RetreatAndReload` on every line-of-sight break and freeze permanently in `Hold` at the back wall. Rebalanced cover qualities: `AdvanceCover = 0.85`, `FlankCover = 0.85`, `CurrentCover = 0.75`, `RetreatCover = 0.70`. The Warden now aggressively advances or flanks when the player ducks, retreats only when out of ammo, and pushes forward again once reloaded.
5. **New Unit Tests**:
   - `WardenWeapon_WhenOwnColliderInPath_IgnoresSelfAndDamagesTarget`
   - `UpdateFacing_WhilePathIsActive_StillFacesKnownPlayerPosition`
   - `ApplyRuntimeConfiguration_ConfiguresHealthAndCoverQualities`

### 14.6 Individual Design Log & Standalone Prefab Export
1. **Design Log (`Docs/DesignLog/Ilzam.md`)**:
   - Completed authoritative design log and viva defense document covering Expectimax vs Minimax/FSM, Laplace probability learning, perception sensors (Vision/Hearing), combat loop integration, 3D model poly audits, and examiner Q&A defense.
2. **Team Prefab Export**:
   - `WardenGameplayIntegrationBuilder.cs` exports `Assets/_Project/Prefabs/WardenGameplay.prefab` directly for the World Builder to drop into the team's main level (`GameScene.unity` / `Greybox_v2.unity`).

## 15. Verified Warden Portability and Regression Pass (21 October 2026)

This section supersedes stale counts and runtime-tuning claims in section 14.

### 15.1 Runtime fixes

- `WardenGameplayController` no longer forces player health, Warden health, hearing sensitivity or cover quality during `Awake`, `Start` or normal ticks. The isolated integration scene owns its 200/350 HP and 25 m hearing test balance.
- The reusable Warden prefab uses the project baseline: 200 Warden HP, 15 m hearing, sensitivity 1.0 and obstruction multiplier 0.5.
- `InitializeExternalReferences(...)` supplies the player, Director and four scene-owned cover markers without modifying shared contracts. Assigning a player also wires `VisionSensor.Target`; tagged-player fallback does the same.
- Any non-retreat action falls back to reloading when ammo is below the three-round burst requirement, preventing visible-player 0/12, 1/12 or 2/12 deadlocks. Retreat/reload keeps its explicit combined behaviour.
- Warden death immediately clears movement and cancels firing/reloading through `Health.Died`.
- Weapon self-filtering now ignores only the firing Warden controller hierarchy, so separate Wardens under a common scene parent can still damage one another.

### 15.2 Portable prefab

- Generated asset: `Assets/_Project/Prefabs/WardenGameplay.prefab` with Unity-generated `.meta`.
- The builder saves a clean clone and validates that eye/muzzle references remain internal while player, Director, vision target and cover references are null.
- Cover markers intentionally remain scene-owned because they represent fixed world locations. The World Builder or a spawner must call `InitializeExternalReferences(...)` for movement tactics. If no covers are supplied, the Warden can hold, perceive and fire but cannot advance/flank/retreat.
- Do not copy the test scene's 200-player/350-Warden HP values into the main game. The GDD Warden baseline is 200 HP.

### 15.3 Automated verification

- Full isolated Unity EditMode run: **45/45 passed**, 0 failed, 0 skipped.
- Breakdown: Director 16, Seeker 3, Sensors 6, Warden Expectimax 7, Warden gameplay 13.
- The earlier moving-facing failure was a test bug: `AgentCommand.Path` is a public field, but the test attempted `GetProperty("Path")`. It now uses `GetField("Path")` and passes.
- New regressions cover partial-ammo reload, immediate death cancellation, preserved scene balance/vision wiring and two Wardens beneath one parent.

### 15.4 Honest limitations and remaining coordination

- Expectimax is a genuine two-Warden-turn search, but its transition model can evaluate the same cover option again on the second simulated turn. It is intentionally approximate.
- Warden health is not currently part of `WardenDecisionState`; low health does not independently alter action scores. Do not describe the AI as health-aware unless this is added later.
- Cover is semantic and authored (`WardenCoverPoint` quality/availability/occupancy). It does not perform NavMesh reachability, geometry exposure or destroyed-cover analysis.
- Facing currently rotates the full Warden root in `LateUpdate`, not a separate upper-body rig. Vision and decision intervals mean reacquisition is responsive, not zero-latency.
- Ilzam manually verified the isolated behaviour: the Warden moves, changes position when the player takes cover, regains a firing angle and shoots. This proves the complete adapter loop in that authored layout, not generalized automatic cover reasoning.
- Still required with the team: World Builder places and initializes scene cover markers; run a two-Warden Play Mode smoke test; verify main-scene navigation/clearance; capture Profiler evidence and final video/screenshots. Do not edit the team main scene or shared `AgentMotor` merely to complete these checks.
