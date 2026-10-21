# Design log: Gunitha Rathnayake

**Roles:** Systems Engineer (SE3032 Graphics & Visualization) and the Hunter / Seeker agent (SE3062 Intelligent Systems).
**Code:** `Assets/OVERRIDE/Scripts/` (Player, Interaction, Combat, Hunter, Flow, Integration, Core) and the editor tools in `Assets/OVERRIDE/Editor/`.

---

## 1. What I built

| Area | Main files | Brief / GDD requirement it serves |
|---|---|---|
| First-person movement | `FirstPersonController`, `PlayerInputReader`, `FootstepNoise` | Player physics (G&V role) |
| Doors, barricades, throwing | `PhysicsDoor`, `SlidingDoor`, `LockableDoor`, `CrateBarricade`, `ScrapCanister`, `PlayerCarry`, `PlayerInteractor` | "Move barricades, open doors, throw objects" (G&V brief) |
| Terminals | `HackTerminal`, `ProgressRing` | Core loop (GDD §3) |
| Combat | `Health`, `Hitbox`, `HitscanRifle`, `RifleEffects`, `WeaponMotion`, `CombatOverlay`, pickups | Shooting theme; weapon feel (GDD §5) |
| Game flow | `GameFlow` | Pause, death + respawn at the last terminal, victory (GDD §3) |
| Hunter agent | `Hunter/Core/*`, `HunterBrain`, `HunterBody`, `HunterGridBaker`, `SecurityCameraSensor`, `HunterDebugView` | IS agent: Bayesian belief grid + A* |
| Integration | `DirectorBridge`, `IntegrationSetup`, `ModelSetup`, `HunterSetup` | Connects the four members' work in Greybox_v2 |

---

## 2. Player physics and interactions (G&V)

### Why a CharacterController for the player, but Rigidbodies for props
A CharacterController moves exactly where told, never slides or tips, and handles steps and slopes. That gives precise FPS control. Physical objects still react: light bodies are pushed in `OnControllerColliderHit`; heavy crates use the hold-E push. Doors, crates and the canister are Rigidbodies so they collide and block each other physically.

### Numbers
| Setting | Value | Source / reason |
|---|---|---|
| Walk / sprint / crouch | 4 / 7 / 2 m/s | GDD |
| Footstep noise radius | 6 / 12 / 1.5 m | GDD noise table |
| Footstep interval | 0.5 / 0.33 / 0.6 s | Starting value: roughly a step rhythm at each speed |
| Gravity | −19.6 m/s² | 2g, snappier than real, standard for FPS |
| Jump | 1.0 m | GDD: feel only |
| Crouch height / camera | 1.0 m / 0.9 m | Fits under the 1.2 m vents |
| Door | 30 kg, HingeJoint spring 300, damper 30, opens 95° | Spring = door returns to its target; damper stops bouncing |
| Crate | 80 kg, rotation frozen, damping 3, pushed at ≤ 2 m/s | Heavy enough that walking into it doesn't move it |
| Canister | 2 kg, Continuous Dynamic, throw 14 m/s + 2 m/s up | Fast throw would tunnel through walls with Discrete |
| Canister damping | angular 3, linear 0.3, friction 0.7, bounce 0.15 | Added after playtesting: a round canister rolled across the whole room |
| Terminal | 8 s hold (Core 30 s), beep 20 m every 2 s, damage pauses 1 s | GDD |

### Interaction problems I solved
- **Crate barely moved.** Floor friction (μ·g·Δt ≈ 0.12 m/s per step) plus damping removed more speed per physics step than my capped push added. Fix: the push budget covers friction, and damping is switched off while held.
- **Crate stuck on walls.** Pushing straight into a wall every step made a large normal force, so wall friction held it. Fix: a `Rigidbody.SweepTest` ahead; the part of the push into the wall is removed (`ProjectOnPlane`), so it slides along walls. Freezing yaw stops it wedging.
- **Crate never moved at first.** The player's *actual* velocity toward the crate is zero, because the crate blocks them. Fix: push with the player's *desired* velocity.
- **Door could trap the player.** It always swung one way. Fix: it swings away from whoever opens it. The hinge's angle sign isn't documented, so the door learns it the first time it moves.
- **Sliding door:** kinematic body moved with `MovePosition`, with a sweep ahead each step. A crate or the player in the doorway stops it, so a barricade can jam a door open.

---

## 3. Combat (G&V)

| Setting | Value | Source |
|---|---|---|
| Rifle | 15 dmg, 8 shots/s, 60 m, 30 + 90 rounds, 1.1 s reload, 30 m noise | GDD (reload was 2.0 s; shortened after playtesting because it felt sluggish) |
| Spread | 0.5° still, 2.0° moving, 0.2° aiming | Starting values |
| Recoil | +1°/shot, ±0.3° yaw, recovers 8°/s after 0.15 s | Delay makes sustained fire climb (GDD) |
| Aim down sights | FOV 60→42°, recoil/move/look ×0.6 | Added from the GDD nice-to-have list |
| Weak points | ×2 (our `Hitbox`, Ilzam's `WardenWeakPoint`/`SeekerWeakPoint`) | GDD |
| Player HP | 100, no regeneration | GDD |

**Hitscan, not physical bullets:** a raycast is exact, instant, and can't tunnel at high speed. It also costs one query per shot instead of a moving Rigidbody per bullet.

**Procedural gun motion** (kick, sway, bob, sprint pose, reload dip, aim pose) is computed in code from the player's state. No rig or animation clips were needed.

---

## 4. The Hunter (IS)

### What it does
The Seeker is **blind**. It keeps a probability for every 2 m cell of where the player might be, updates it from what it **hears** (and from security cameras until the Labs terminal takes them offline), and uses A* to go where the player most likely is. Its job in the game is to punish noise; a thrown canister lures it away. It attacks with a 25-damage lunge.

### One tick (every 0.25 s, and immediately on a heard noise)
1. **Predict.** The player may have moved, so each cell keeps (1 − α) of its probability and shares α equally with the neighbours it is linked to:
   b′(c) = (1 − α)·b(c) + Σ over linked neighbours n of α·b(n) / |N(n)|
2. **Update (Bayes).** Each piece of evidence at position x multiplies every cell by its likelihood, then the grid is normalised:
   L(c) = ε + (1 − ε)·exp(−d(c, x)² / 2σ²)
3. **Negative evidence.** If the 3 m proximity sense touches nothing, cells inside it are multiplied by (1 − p_d), so searched areas drain.
4. **Goal.** score(c) = M(c)·exp(−λ·distance), where M(c) is the probability within 4 m of c, from a summed-area table, so it's O(1) per cell.
5. **Plan.** A* to the best goal. If that fails (crate in the way), the next two goals are tried.

### Balance decisions
| Decision | Value | Why |
|---|---|---|
| Seeker health | 250 HP (GDD 150) | Aim down sights (0.2° spread) wasn't in the GDD. At 150 HP an aimed player killed the Seeker in about 1.2 s, or 0.6 s on its ×2 eye: over before it could create any tension, for an enemy that never respawns. 250 HP gives about 2 s on the body and still about 1 s on the eye, so precise shooting stays rewarded |
| Number of Seekers | 1 | GDD: one Seeker, starting in Maintenance. A second would swarm the Maintenance hack (beeps every 2 s); escalation already comes from the Director's drones, and the final-phase recall gives the Seeker its finale role |

### Parameters
| Symbol | Value | Why |
|---|---|---|
| Cell size | 2 m | A sprinting player covers 7 × 0.25 = 1.75 m per tick, less than one cell |
| α (diffusion) | 0.3 | A fraction of the probability moves one cell per tick, matching that speed |
| σ noise / muffled | 3 m / 6 m | Sound gives a rough place; through a closed door, rougher |
| ε noise | 0.02 | One misleading noise can never rule out the rest of the map |
| σ / ε camera | 1 m / 0.001 | Cameras see the player directly |
| σ contact | 0.5 m | Near-certain |
| p_d | 0.9 | The proximity sense rarely misses a player who is there |
| λ | 0.05 per m | A cell 20 m away needs e^1 ≈ 2.7× the mass to win over a near one |
| Alarm reach | λ × (1 − 0.5·alarm/100) | High alarm = willing to search further (GDD §7) |
| Hearing | 1.5 × noise radius, halved through a closed door | GDD |
| Recall σ / floor | 10 m / 0.15 | Final phase (GDD §7): ARGUS's recall to the Core is applied as wide, weak evidence each tick, so the Seeker drifts to the Core but real noises still win |

Values changed after testing are listed under Balance decisions above and in §2–3 (reload 1.1 s, canister damping).

### A* details
8-connected; straight step 1 cell, diagonal √2; no corner cutting. **Octile heuristic** h = (max − min) + √2·min (in cells). It equals the cost of the best obstacle-free path, so it never overestimates (**admissible**) and changes by at most one step's cost between neighbours (**consistent**). A* therefore returns a shortest path and closes each cell once. Arrays are reused between searches (generation stamps), so a search allocates nothing.

### The grid, without a NavMesh
The level had no NavMesh, and a NavMesh describes one agent size. Each cell is tested with physics instead:
- a ray down finds the floor
- a 1.0 m capsule decides whether the crouching **player** fits (vents included)
- a 2.0 m capsule decides whether the hovering **Seeker** fits (vents excluded)

So the "player in a vent" case needs no hand-placed volumes.

**Bug and fix:** the Seeker flew through walls after a gunshot. A thin wall can run *between* two cell centres without touching either. Fix: for each pair of neighbouring cells, a sphere cast at each agent's height is baked once into 8 link bits per cell. A* and the belief's spreading only use open links. Cells test *space*; links test *connectivity*.

### Alternatives considered
- **Last-known-position state machine:** can't hold uncertainty or several possible places; a lure would fool it for good.
- **Particle filter:** works, but random from run to run and harder to show; with about 5,500 cells an exact grid is cheap.
- **Straight to the latest noise:** forgets everything heard before and never searches.

### Why it isn't a "glorified if/else"
The intent label (Patrol / Search / Investigate / Pursue / Attack) only describes the belief. Behaviour comes from the maths: contact collapses the belief onto the player, so pursuit falls out of the same goal choice. Cameras going offline removes an evidence source; no new branch is added.

### Edge cases (GDD §6)
| Case | Expected | Result |
|All seven play-tested edge cases passed (21 Oct), and the Hunter core has **17 edit-mode unit tests, all passing** (`Assets/Tests/Editor/Hunter/HunterCoreTests.cs`, evidence `07_tests.png`), including contradictory evidence, the vent mouth, a crate-blocked corner and underflow.

---|---|---|
| Two noises far apart | Two peaks; picks the bigger, distance-weighted | Pass |
| Fake lure (canister) | Goes to impact, drains it, returns to older evidence | Pass |
| Player in a vent | Waits at the vent mouth | Pass |
| Noise through a closed door | Range halved, σ doubled, no path through the door | Pass |
| Cameras offline (Labs done) | Sightings stop changing the heatmap | Pass |
| Goal blocked by a crate | Next-best goal used | Pass |
| Stuck 2 s | Next cell avoided 3 s, replans | Pass |
| Belief underflow | Reset to uniform | Guard in `BeliefGrid.Normalize` |

---

## 5. Optimization (G&V, with evidence in `Docs/Evidence/Gunitha/`)

Measured in Greybox_v2 in the Editor on 21 Oct, with the in-game performance overlay (`` ` ``).

| What | How | Evidence |
|---|---|---|
| Rigidbody sleeping | Crates, canisters and doors are never woken every frame | **1** active dynamic body with everything at rest (`01_rest_bodies.png`) |
| Pooled effects | Sparks, bullet holes, tracer and flash are created once and reused; the HUD caches its text and marker bars | No objects are created while firing: the effects are recycled (`02_firing_gc.png`, firing at 193 FPS, main thread 5.8 ms) |
| Non-allocating queries | `RaycastNonAlloc`, `OverlapCapsuleNonAlloc`, `OverlapBoxNonAlloc`, reused buffers | — |
| Fixed timestep | Compared 50 Hz and 60 Hz live (overlay keys − and =) | Physics step **0.02 ms** at both (`03a`/`03b`, settings in `09a_physics_settings.png`): with props asleep there's almost nothing to simulate. 60 Hz would cost 20% more steps per second (1.2 vs 1.0 ms/s) for no visible gain, so I kept Unity's default **50 Hz** |
| Collision detection | Continuous Dynamic only on the canister (thrown at 14 m/s); Discrete on doors and crates | `09b_canister_rigidbody.png`; the canister never passes through walls |
| Interpolation | Only the canister, the Seeker and sliding doors (seen moving up close) | — |
| Kinematic movers | Sliding doors and the Seeker are kinematic Rigidbodies, not moved static colliders | — |
| Hunter time-slicing | Decides every 0.25 s, not every frame; 2 m cells (a quarter of the cells of 1 m) | **Hunter.Tick 0.92 ms** per decision, about 3.7 ms per second, or about 0.06 ms per frame at 60 fps (`04_hunter_tick.png`). A* expanded **5 cells** on a typical short replan, thanks to the admissible octile heuristic |
| Hunter allocations | Belief, A* and goal arrays allocated once; only a new path array on each replan (at most 4 per second) | — |
| Grid baked once | Floor, clearance and neighbour links tested once at Play start | **75 × 73** cells: **2625** player-walkable, **2612** Seeker-walkable, **13** crouch-only (vents); **18,198** player / **18,122** Seeker links; **baked in 13–16 ms** (`05_grid_bake.png`). An earlier bake gave only 2240 walkable: oversized Warden weak-point colliders were blocking cells around each Warden. Once those were corrected, the full count came back, so the bake also catches stray colliders |

## 6. Workflow and tools
Unity 6000.3 (URP, Input System, Profiler, Physics Debugger), C# in VS Code, Git + Git LFS with feature branches (`feature/player-controller`, `feature/interactions`, `feature/rifle`, `feature/hunter-belief-grid`, `feature/game-flow`) and integration branches. Editor tools I wrote do repeatable scene wiring: installer, model fitting, Seeker prefab builder.
