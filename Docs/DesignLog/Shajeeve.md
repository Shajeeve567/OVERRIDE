# Design log: Shajeeve Balakrishnan

**Roles:** World Builder (SE3032 Graphics & Visualization) and the Director / ARGUS Core agent (SE3062 Intelligent Systems).
**Code:** Director brain in `Assets/Scripts/AI/Director/` (own assembly, `Override.DirectorAI`), its scene side in `Assets/Scripts/Director/`, level contracts in `Assets/Scripts/Level/`, tests in `Assets/Tests/Editor/Director/`, level-building tools in `Assets/Editor/`.

> Lines marked **TODO (Shaj)** still need my own account.

---

## 1. What I built

| Area | Main files | Brief / GDD requirement it serves |
|---|---|---|
| Director brain | `DirectorBrain`, `DirectorCost`, `DirectorPlan`, `DirectorSnapshot`, `PlanSearch` | IS agent: local search (simulated annealing) over spawn and door-lock plans (GDD §6) |
| Director in the scene | `DirectorController`, `ZoneLocator`, `IDirectorDrone` | Builds the snapshot, carries out the plan, reacts to overrides (GDD §7) |
| Level contracts | `DroneBay`, `ILockableDoor`, `WaypointNode` | Spawn points, barricade blocking, lockable doors |
| Director tests | `DirectorBrainTests` (16 edit-mode tests), `Docs/Director_SA_vs_HillDescent.txt` | Edge cases and the algorithm comparison |
| Level shell | `GreyboxBuilder`, `LevelSize` | 4 zones + airlock + corridors + vents + Core ring (G&V role) |
| Materials | `FacilityMaterials` | CC0 PBR textures tiled per metre (GDD §9) |
| Dressing | `WarehouseKitDresser`, `WarehouseKitProps`, `PropBuilder`, `RoofBuilder` | Visual cohesion: one kit, one palette |
| Drone bays | `DroneBayBuilder` | The Director's 10 spawn points, checked against the bay rules |
| Player and menu | `PlayerBuilder`, `StartScreenSetup`, `StartScreenController` | Player prefab, MainMenu scene, build order |
| Integration | `DirectorSetup`, `Greybox_v2.unity` | Main scene owner (workplan §2) |

---

## 2. The Director (IS)

### What it does
ARGUS's Director is the abstract core: it has no body. Its job is **pacing**: keep the player's tension inside a target band, not bored and not overwhelmed. Every 3–5 s it measures tension, works out how much pressure to add, and searches for the best **plan**: how many Wisp drones to launch from each of the 10 drone bays, and which doors to lock.

### Architecture: brain and hands are separate
- **Brain** (`Override.DirectorAI` assembly): plain C# with no MonoBehaviour, no scene access, no `Find`. It takes a `DirectorSnapshot` and returns a `DirectorDecision`.
- **Hands** (`DirectorController`, a MonoBehaviour): fills the snapshot from the game, calls the brain, launches drones and locks doors.
- The brain sits in its own assembly, which can't see the game's code, so the compiler enforces the separation.
- Why: tests build a snapshot in one line with no scene; the same snapshot and seed give the same plan; the brain can only use what it is given.
- The brain still uses `UnityEngine.Vector3` and `Mathf` for maths, but not the engine itself. The comparison tests ran outside Unity with Mono.
- It doesn't use the team's `IAgentBrain` (`AgentCommand Tick(WorldSnapshot)`), because the Director outputs a spawn/lock plan, not a path, look target or fire flag for a body.

### One decision (`DirectorBrain.Decide`)
1. **Tension** (0–1):
   heat = 1 − clamp(secondsSinceFight / 30)
   tension = 0.5·recentDamage + 0.3·alarm/100 + 0.2·heat
2. **Needed pressure** (in "drones arriving now"):
   needed = max(0, (bandCentre − tension) × 6), where bandCentre = 0.5
   - At or above the centre, needed is 0, the empty plan is cheapest, and the Director backs off with no special case.
   - In the final wave, tension is ignored: needed = budget − active drones.
3. **Search:** simulated annealing over plans, minimising `DirectorCost`.
4. **Remember:** keep the plan, so door locks carry over to the next decision.

### The state (`DirectorPlan`)
- `int[] DronesPerBay` (0–2 each; a bay has one hatch) and `bool[] LockDoor`.
- Space: 3¹⁰ = 59,049 spawn combinations × 2^doors.
- Plain arrays, because a neighbour move is "change one entry" and `CopyFrom` reuses memory, so a search creates no garbage.

### The cost function (`DirectorCost`)
Pressure of one drone from a bay: `1 / (1 + max(0, d − 10) / 20)`. It's 1 within 10 m and 0.5 at 30 m: a drone that arrives late is worth less, because the fight will have moved on. This is computed once per decision for each bay.

| Term | Weight | Why |
|---|---|---|
| Band: (predicted − needed)² | 1 | The goal. Squared so being off by 2 is more than twice as bad as off by 1, and smooth near the target |
| Over the drone budget | 100 per drone | Hard rule |
| Drone from a blocked bay | 100 per drone | Hard rule (barricades) |
| Lock after Terminal 2 | 100 per lock | Hard rule (subsystem lost) |
| Every exit of the player's zone locked | 100 | Hard rule (fairness: the player always has a way out) |
| Spawn closer than 10 m | 2 per drone | Soft fairness: no spawning in the player's face |
| Flank: −spread | 0.4 | Rewards pincers. Spread = 1 − |pressure-weighted direction sum| / total pressure |
| Doors changed since last plan | 0.1 per door | Doors are standing states and shouldn't flicker |
| Per drone | 0.1 | Don't spend a drone that adds almost nothing |
| Per lock | 0.15 | No pointless locks. Kept above Change, so releasing an old lock is cheaper than keeping it |
| Lock pressure | +0.3 per locked exit of the player's zone | A lock only adds pressure if it cuts the player's options right now |

**Why hard rules are penalties, not filters:** local search moves one step at a time. If illegal plans were removed, the legal ones could form islands with no one-step path between them. A penalty of 100, when a good plan costs about 0–5, lets the search pass through an illegal plan, but the best plan returned is never illegal.

### Why these numbers

| Setting | Value | Reason |
|---|---|---|
| Damage weight | 0.5 | Losing health is the most direct sign the player is struggling, so it counts most |
| Alarm weight | 0.3 | ARGUS's awareness should raise tension before any damage is taken, but less than real damage |
| Engagement (heat) weight | 0.2 | A recent fight adds stress, but being in a fight alone doesn't mean the player is losing it |
| Weights sum | 1.0 | Keeps tension between 0 and 1 without extra scaling |
| Heat cooldown | 30 s | About the length of one encounter; after that, the last fight no longer counts |
| Target band | 0.35–0.65 | Centred at 0.5, ±0.15. Wide enough that small changes don't cause a re-plan every decision, narrow enough that boredom and overload both show up |
| Pressure per tension | 6 | Tension 0 asks for 3 drones' worth, which fits under the normal budget of 4 and leaves room for a drone already alive |
| Hard-rule penalty | 100 | About 20× the cost of a good plan (0–5): never chosen, but the search can still step through |
| Too close | 2 per drone | More than one drone's possible gain on the band term, so a face-spawn only wins when no other bay works (e.g. the small Core) |
| Flank | 0.4 (max reward) | Equal to being off by about 0.6 drones (0.63² ≈ 0.4): a pincer breaks ties but never beats sending the right amount |
| Per drone | 0.1 | A drone must reduce the band error by more than 0.1 to be worth sending |
| Per lock / door change | 0.15 / 0.1 | Small tie-breakers; Per lock > Change so an old, useless lock is always released |

### The search (`PlanSearch`)
**Neighbour moves:** add a drone, remove a drone, move a drone from bay i to bay j, toggle a door.

**Simulated annealing (used in the game):**
- Each step tries one random neighbour.
- A worse move is accepted with probability e^(−Δ/T) (Metropolis rule).
- T is multiplied by 0.997 each step. After 2000 steps, 0.997²⁰⁰⁰ ≈ 0.0025, so the last steps only go downhill.
- It returns the best plan ever seen, not the last one.

**Starting temperature from the problem, not a magic number:**
1. Try 30 random moves and average the uphill Δ, leaving out rule-break jumps of 50 or more.
2. Set T₀ = −avg / ln 0.5.
3. Result: about half of worse moves are accepted at the start, whatever the cost scale.

**Hill descent (baseline, F10 in Play mode):** steepest descent over all neighbours, stopping at the first local minimum. Both algorithms share the same moves and cost function, so any difference comes from the algorithm alone.

### Tuning the annealing schedule
Measured with `DirectorBrainTests`: 200 random situations, compared against the brute-force optimum.

| Steps | Cooling | Initial acceptance | True optimum found |
|---|---|---|---|
| 400 | 0.98 | 0.8 | 151/200 (worse than hill descent's 182) |
| 1000 | 0.995 | 0.5 | 187/200 |
| **2000** | **0.997** | **0.5** | **194/200 (chosen)** |

With too few steps, T drops before the search has explored, so it freezes early.

### Results (current cost function, Mono, 20 Oct)

| Algorithm | Mean cost | True optimum | Evaluations | ms / decision |
|---|---|---|---|---|
| Simulated annealing | 0.693 | 197/200 | 2031 | 0.704 (Mono) / 0.506 (Unity Editor) |
| Hill descent | 0.698 | 189/200 | 52 | 0.023 / 0.019 |
| Brute force | 0.692 | 200/200 | 3457 | 3.103 / 2.152 |

Annealing was strictly better in 11 situations, hill descent in 3, and they tied in 186. Re-run in the Unity Test Runner on 25 Nov with identical results (`Docs/Evidence/Shajeeve/02_sa_vs_hd_in_unity.jpg`).

### Alternatives considered
- **Hill descent:** implemented and measured. It stops at local minima (189 vs 197 true optima). It is cheaper, but 0.7 ms every 3–5 s is negligible.
- **Brute force:** possible here only because plans with more than 4 drones can be pruned. The space grows as 3^bays × 2^doors, and a final-wave budget of 6 or more bays makes it explode. Local search has a fixed cost set by the iteration cap. I use brute force as the ground truth for testing, not in the game.
- **Genetic algorithm:** needs a whole population scored every cycle. That's much more work per decision for a space this small and smooth.
- **Rule-based spawning:** grows into a large if/else chain that can't trade off pressure, fairness, flanking and stability at the same time.

### How it reacts to the game (`DirectorController`)

| Event | Reaction |
|---|---|
| Player damaged | Recent damage +HP/100 (fades as e^(−t/6 s)); alarm +0.5 per HP |
| Hack started | Alarm +40; immediate re-plan. In the Core, the final wave starts |
| Terminal 1 (Labs) | Cameras offline: the alarm rises at half speed |
| Terminal 2 (Maintenance) | Door control offline: every lock released, locks now cost 100 |
| Terminal 3 (ServerHall) | Drone budget halved (4 → 2, final wave 6 → 3) |
| Core override | ARGUS offline: drones destroyed, doors opened, Director stops |
| Crate in front of a bay | `DroneBay.IsBlocked` (OverlapBox, own crate or ≥ 20 kg): the plan avoids that bay, and the launch rechecks it |

Every override triggers an immediate, visible re-plan (GDD hook). Decisions come at a random 3–5 s interval, so players can't learn the rhythm. In the final wave they come every 1.5 s.

### Edge cases (GDD §6), tested in `DirectorBrainTests`

| Case | Test | Expected |
|---|---|---|
| Player standing idle | `IdlePlayer_GetsDronesSent` | Tension below the band; at least 2 drones |
| Player overwhelmed | `OverwhelmedPlayer_GetsNothing` | 0 drones |
| Every spawn point blocked | `EverySpawnBlocked_LaunchesNothing` | 0 drones, no crash |
| Some bays blocked | `BlockedBaysAreAvoided_OthersStillUsed` | Other bays used |
| Final-wave budget | `FinalWave_UsesTheWholeBudget` | Exactly the budget |
| Subsystem lost mid-plan | `SubsystemLostMidPlan_PlanStaysLegal` | No locks, halved budget kept |
| Halved budget | `HalvedBudget_IsNeverExceeded` | At most 1 extra drone |
| Never trap the player | `NeverLocksEveryExitOfThePlayersZone` | At least one exit open |
| No bays at all | `NoBaysAtAll_DoesNotCrash` | Empty plan |
| Reproducible | `SameSeed_SameDecision` | Identical plans |
| Door relevance | `OnlyLocksOnThePlayersExits_AddPressure`, `FarAwayDoors_AreNeverLocked`, `OldLock_IsReleased_OnceThePlayerHasMovedOn` | Locks only where they matter |
| Spawn choice | `NearbyBaysArePreferred`, `TwoDrones_ComeFromBothFlanks` | Close bays, pincers |
| Algorithm | `Annealing_BeatsHillDescent_AgainstTheTrueOptimum` | SA ≥ HD on mean cost and optimum hits |

**Result:** all 16 tests pass in the Unity Test Runner, in 0.59 s (`Docs/Evidence/Shajeeve/01_director_tests.jpg`).

### Debug tools
- **F3 panel:** tension bar with the band marked, plan, every cost term, evaluations, steps, worse moves accepted, T₀, and the last 8 decisions.
- **Keys:** F4 decide now, F5 damage, F6 hack, F7 override, F8 kill drones, F9 final wave, F10 switch annealing / hill descent.
- **Gizmos:** lines from the chosen bays to the player.

### Known limitations
- Locks are matched to doors by index across decisions. If the set of lockable doors changes between decisions, an old lock can map to a different door. A fix would match by `DoorId`.
- `ActiveDrones` doesn't count drones still waiting for their 0.6 s staggered launch. An immediate re-plan right after a decision can go one drone over budget for a moment.
- Drones already alive only count against the budget, not as pressure. They raise tension once they engage.
- The decision interval uses `UnityEngine.Random`, so only the brain's choices are seeded, not the timing.
- Doors are found with `FindObjectsByType` each decision. That's fine every 3–5 s, but it could be cached.

---

## 3. World Builder (G&V)

### Workflow: the level is generated, not hand-placed
Every builder is an editor menu (`OVERRIDE > …`). It clears its own previous output and rebuilds, so the result is repeatable and the layout lives as numbers in one place.

1. **`GreyboxBuilder`:** floors, walls, corridors, crouch vents (1.2 m) and the round Core, from the GDD's 60 × 50 m plan.
2. **`LevelSize`:** one place for scale. The plan is multiplied by **2.5**. Heights are set separately: rooms 10 m (was 3.5), Core 16 m, corridors 7 m. The extra height lets drones fly above cover. The tunnel (2.2 m) and vents stay cramped.
3. **`FacilityMaterials`:** ambientCG / Poly Haven PBR sets (Color, NormalGL, Roughness, Metalness, AO). Each set is matched to a greybox material by folder name, and metalness/roughness are packed for URP Lit. Textures tile **per metre** instead of stretching across a wall.
4. **`WarehouseKitDresser`:** looks come from the SciFi Warehouse Kit, collision from the greybox. The greybox renderers are switched off but their simple box colliders stay. The kit pieces have their colliders removed, because detailed mesh colliders snag the player and cost physics time.
5. **`WarehouseKitProps` / `PropBuilder`:** props follow rules:
   - floor props only in a band along the walls; the middle is combat space with cover from `PropBuilder`;
   - a 6 m keep-out zone in front of every doorway;
   - no overlaps;
   - solid floor props keep their colliders, wall-mounted props lose theirs;
   - kit Rigidbodies are made kinematic.
   The palette follows the GDD: cool greys and white panels, teal accent, ARGUS red. Each zone has its own ceiling-light material (`Light_Labs`, …), so a zone's lights can change colour by editing one material.
6. **`RoofBuilder`:** the round Core gets a three-layer roof: kit tiles kept inside the circle, a dark disc behind them, and 12 radial beams, with skylights over the pillar. The Airlock gets plain kit tiles. The greybox ceiling boxes are switched back on, invisible, so drones and bullets can't leave the level.
7. **`DroneBayBuilder`:** places the 10 bays and checks each one against the bay rules, printing a report:
   - 10–25 m from the zone's terminal;
   - hidden from it, checked with a line-of-sight raycast from the terminal screen;
   - within 3 m of a waypoint node;
   - closable by its crate: a 1.4 m hatch and a 1.2 m crate.
   The bay crates are 0.8 kg, lighter than the player, so blocking a bay is a quick choice. The corridor barricades stay 80 kg.
8. **`PlayerBuilder`, `StartScreenSetup`:** player prefab; MainMenu scene with a UI Toolkit start screen (PanelSettings scaled for 1920×1080); build order MainMenu → Greybox_v2.

### Lighting
- The kit's lamps are only glowing meshes. Every second fixture gets a real point light (hanging lights: range 16, intensity 2.5; wall lights: range 8, intensity 1.5).
- These lights have **no shadows**: dozens of shadow-casting point lights would cost too much. Shadows are meant only for a few hero lights.
- Baked lightmaps are enabled in Greybox_v2's lighting settings (lightmap resolution 40 texels per unit).
- **Zone lighting switch:** not in the submitted build. I prepared for it by giving each zone its own ceiling-light material (`Light_Labs`, …), starting in ARGUS red, so the switch would be one material change per zone (the workplan's fallback: emissive change only, no rebake). The switch code itself wasn't finished before the 21 Oct deadline.

### Optimization (G&V, evidence in `Docs/Evidence/Shajeeve/`)

**Setup:** measured on 25 Nov in Greybox_v2, Editor Play mode, Game view Stats (1243×453). Every run uses the same view at the Airlock spawn, about 3–4 s after pressing Play, before the first drone wave. The laptop has an RTX 4050.

| Configuration | FPS | CPU main | Render thread | Batches | Saved by batching | SetPass calls | Triangles | Shadow casters | Evidence |
|---|---|---|---|---|---|---|---|---|---|
| Static batching **off**, no occlusion | 98.1 | 10.2 ms | 5.7 ms | 9,357 | 0 | 94 | 582k | 1,360 | `06_stats_batching_off.jpg` |
| Static batching **on** (as shipped) | 113.7 | 8.8 ms | 4.7 ms | 1,281 | 12,927 | 110 | 666k | 1,363 | `03_stats_baseline_batching_on.jpg` |
| Batching on + **occlusion culling baked** | 144.3 | 6.9 ms | 2.7 ms | 287 | 746 | 51 | 89k | 588 | `07_stats_occlusion_baked.jpg` |

**Static batching:**
- Every builder marks level geometry BatchingStatic, so Unity combines static meshes that share a material.
- Batches fell **86%** (9,357 → 1,281), the render thread went from 5.7 to 4.7 ms, and FPS rose 98 → 114.
- Cost: the combined meshes take extra memory.
- SetPass calls rose slightly (94 → 110). Batching cuts draw calls, not material/shader switches.
- Triangle counts differ a little between runs because the scene isn't frozen.

**Occlusion culling:**
- Walls, floors and roofs are Occluders; props are Occludees only, because small objects hide little.
- The facility is closed rooms joined by doors, so most of it is hidden from any one spot.
- Baked with Unity's defaults (smallest occluder 5 m, smallest hole 0.25 m) in under 20 s. The occlusion data is 212 KB.
- From the spawn view:
  - triangles **666k → 89k (−87%)**
  - batches 1,281 → 287
  - shadow casters 1,363 → 588
  - render thread 4.7 → 2.7 ms
  - FPS 114 → **144**
- It was the biggest single gain.

**Lightmaps (measured, not finished):**
- At the scene's resolution of 40 texels per unit, the GPU lightmapper needed **61 lightmaps of 1024×1024**. Unity estimated 23–46 minutes at about 210 Mrays/s, so I cancelled the bake (`08_lightmap_bake_res40.jpg`).
- The cause: the level is scaled 2.5× with 10 m rooms, so its surface area is large, and texel count grows with area × resolution².
- The point lights are realtime, so a bake would mostly add indirect light, ambient and emissive light. That is a lot of bake time and memory for a small visual gain.
- The bake also warned that the kit's `Floor Tile 01` has invalid lightmap UVs, so it is left out of baked lighting.
- **Next step:** about 10 texels per unit would need about 16× fewer texels.

**Director cost:**
- In the Profiler, `DirectorController.Update` is **0.00 ms with 0 B garbage** on ordinary frames (`04_profiler_director_frame.jpg`), because it only does work every 3–5 s.
- A decision costs **0.506 ms** measured in the Editor: 2031 cost evaluations, no allocations in the search. That's under 0.2 ms per second on average.
- On the sampled frame the whole game's PlayerLoop took 1.44 ms (`05_profiler_timeline_playerloop.jpg`).

| Other choices | Why | Measured? |
|---|---|---|
| Cheap collision | Greybox boxes for collision; kit meshes have no colliders | No |
| Shadowless point lights | Each shadowed point light renders the scene 6 extra times (a cube map) | Not measured: it would need every generated light toggled |

---

## 4. Workflow and tools
Unity 6000.3 (URP, Input System, ProBuilder, Profiler, UI Toolkit), C#, NUnit edit-mode tests, Git + Git LFS, branches `world-builder`, `feature/argus-core`, `feature/start-screen`, `feature/game-flow`. CC0 textures from ambientCG / Poly Haven; SciFi Warehouse Kit and Barking Dog modular kit for dressing.

**Development timeline: my Director commits all landed on 20 Oct. Write one or two honest sentences on how the work was developed before that.

**Use of AI tools. AI is used for testing. 
