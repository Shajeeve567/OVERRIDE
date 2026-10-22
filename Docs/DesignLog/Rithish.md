# Design log: Rithish

**Roles:** Intelligent Systems (IS) — Wisp drone AI, with shared gameplay motor and visual feedback.  
**Main code:** `Assets/Scripts/AI/PathfinderBrain.cs`, `Assets/Scripts/Motor/AgentMotor.cs`, `Assets/Scripts/Motor/WispVisuals.cs`, `Assets/Scripts/Motor/WispHealth.cs`, `Assets/Scripts/AI/WaypointGraph.cs`, `Assets/Scripts/Core/`.  
**Editor tooling:** `Assets/Editor/WaypointGraphValidator.cs`, `Assets/Editor/AutoWaypointGenerator.cs`.  
**Branch:** `fix/wisp-glitches`  
**Date:** 21 October 2026

---

## 1. What I built

The Wisp is the Director's fast flying pressure unit. My work covers the whole runtime loop:

| Area | Main files | Responsibility |
|---|---|---|
| Decision layer | `PathfinderBrain`, `IAgentBrain`, `AgentCommand`, `WorldSnapshot` | Choose a path, look target, fire state and animation intent |
| Navigation | `PathfinderBrain`, `WaypointGraph`, `WaypointNode` | Exposure-weighted A*, direct-chase fallback, bay exit |
| Senses | `PathfinderBrain`, `NoiseBus` | 25 m / 120° vision, 10 m hearing, last-known position |
| Movement | `AgentMotor` | Hover, path following, rotation, stuck detection, shared Warden safety |
| Combat | `AgentMotor`, `WispHealth` | Chest-target hitscan, beam, damage, graceful death |
| Presentation | `WispVisuals`, `AgentMotor` | Procedural spawn, attack, hurt, death, hover hum, shot audio |
| Validation | `WaypointGraphValidator`, `AutoWaypointGenerator` | Detect graph faults; generate and link grid waypoints |

The four-team contracts stayed intact: the Wisp implements `IAgentBrain`, uses the shared `AgentCommand`/`WorldSnapshot` data, and does not require changes to the Director's `IDirectorDrone` launch contract.

---

## 2. Why the AI and motor are separate

`PathfinderBrain` decides **what** to do. `AgentMotor` decides **how** to move.

This split matters because:

- The same motor can be reused by other agents (the Warden already uses `AgentMotor`).
- The brain can be tested or replaced without changing CharacterController movement.
- The Director only needs `IDirectorDrone.Launch(waypoint, player)`, not knowledge of Wisp behaviour.

The command is intentionally small:

```csharp
Path, LookTarget, Fire, AnimationIntent
```

`AnimationIntent` now also carries `"ExitBay"`, which lets the motor make a physical launch decision without adding another shared contract field.

---

## 3. Navigation

### 3.1 Waypoint graph

The original graph builder reads manual `WaypointNode` links and produces a directed adjacency list. A* uses those links directly.

The manually authored greybox graph is still the main navigation limitation:

- only **10 nodes**, all `WP_Bay_*`;
- only **3 edges**: `0↔1`, `2↔3`, `7↔9`;
- nodes `4`, `5`, `6` and `8` are isolated;
- that is **7 disconnected components**.

So A* can only route inside one of those small pairs. It cannot cross the map. The runtime therefore needs a reliable direct-chase fallback, and the level needs room/corridor nodes before the Wisp can navigate the full facility.

Two editor tools now make that manageable:

`WaypointGraphValidator` — **`Tools > OVERRIDE > Validate Waypoint Graph`** — reports isolated nodes and missing reciprocal edges without editing the scene.

`AutoWaypointGenerator` — **`Tools > OVERRIDE > Generate Grid Waypoints`** — samples a 7 m grid across the `OVERRIDE_Greybox` bounds, raycasts down from 0.5 m above the floor, and accepts only upward-facing floor hits. It places a `WaypointNode` 2.5 m above each hit when a 0.5 m sphere is clear. All generated nodes live under one `AutoWaypoints` parent, which is replaced on each run; other waypoint nodes are never moved or deleted.

Generated pairs within 10 m are linked when a 0.45 m sphere cast is clear. Door colliders (`IDoor`/`ILockableDoor`) and non-kinematic rigidbodies are ignored. Each existing waypoint is also linked to its nearest clear generated node, and every link is added in both directions. The generator uses Undo, marks the scene dirty and logs the node/link counts.

The generator has been implemented and compiled but **not executed on the greybox yet**, so the scene still contains the original 10-node graph.

### 3.2 Exposure-weighted A*

A* cost is:

```text
g(n) = g(parent) + distance(parent, n) + 3 * exposure(n)
```

Exposure is derived from the player's facing direction and the node direction. Nodes in the player's forward arc cost more, encouraging the Wisp to approach from the side or behind.

The heuristic is the Euclidean distance to the target node. Since every edge cost is at least the straight-line distance, the heuristic is admissible and A* remains optimal for the configured cost model.

Path refresh is normally **0.5 s**, but a target change forces an immediate refresh.

### 3.3 Line of sight and direct chase

The Wisp does not fire through walls or props.

- LOS raycast goes from the Wisp to the **player's CharacterController bounds centre**, not the player's feet.
- Hits on the Wisp's own hierarchy are ignored.
- Other `IDamageable` agents are ignored, so a Wisp can shoot past another drone.
- The nearest player hit is compared with the nearest blocker hit.

If LOS is clear and a **0.45 m sphere cast** from the Wisp to the player is also clear, the brain uses a direct path to the player instead of spending an A* search.

This fixed the original point-blank-only shooting bug: the Wisp had been aiming at the player's feet, so the floor or nearby props blocked almost every shot.

### 3.4 Target memory and terminal ambush

The old targeting rule switched to the nearest terminal as soon as LOS broke. If the player was near a terminal, the Wisp could repeatedly run back to it and return when LOS was restored.

The new rule is:

1. **Player seen:** update `lastKnownPlayerPosition`; `timeSinceLosLost = 0`.
2. **Not seen for under 2 s:** chase `lastKnownPlayerPosition`.
3. **Not seen for 2 s or more and player within 15 m of a terminal:** target that terminal.
4. **Terminal ambush requires that the Wisp cannot actually see the player.** Raw LOS alone is not enough.
5. **Target mode changes** are limited by a **1.5 s cooldown**, so the Wisp cannot flip between player and terminal every frame.
6. **A* failure:** chase the last-known position only if a direct sphere cast is clear. If not, hold position. It never falls back to the terminal.

This keeps the terminal ambush as a deliberate behaviour rather than an oscillation.

### 3.5 Vision and hearing

The GDD senses are now implemented against the Wisp's own state:

- **Vision — 25 m, 120° total cone.** `CanSeePlayer()` rejects targets outside 25 m or outside ±60° of the Wisp's forward vector, then runs the existing `HasLineOfSight` check.
- **Hearing — 10 m.** `PathfinderBrain` subscribes to `NoiseBus.Emitted`. Any noise within 10 m updates `lastKnownPlayerPosition`, clears terminal ambush and forces a path refresh.
- **Launch.** The Director's player transform seeds `lastKnownPlayerPosition`, so every new Wisp starts with a valid investigation target even before its first sighting.

Hearing updates the last-known position but does not count as sight, so `timeSinceLosLost` still tracks actual vision loss.

---

## 4. Bay launch and movement

`AgentMotor` is shared with the Warden. The Wisp-only movement features are gated behind `[SerializeField] bool isFlying`:

- capsule shrink in `Awake`;
- hover-height adjustment and vertical movement;
- the stuck timer.

Only `Wisp.prefab` has `Is Flying` enabled. The Warden leaves it disabled, so its grounded movement, collider and stuck behaviour are unchanged.

### 4.1 The 1.4 m hatch problem

The bay hatch is **1.4 m** high. The Wisp prefab had a **2.0 m** CharacterController and the new hover target is **2.5 m**. On launch it tried to climb while still inside the booth, hit the lintel/roof, and could be destroyed by the stuck timer before it ever left.

The fix has three parts:

- The Wisp uses a compact **1.2 m** collider (`radius 0.45`, `skinWidth 0.04`) while other `AgentMotor` users keep their own dimensions.
- While `AnimationIntent == "ExitBay"`, the motor holds its current altitude instead of climbing.
- Stuck detection is suspended during the bay exit.

The Wisp still leaves under its own power; it is not teleported to the waypoint.

### 4.2 Hover and path following

| Setting | Value | Reason |
|---|---|---|
| Hover height | 2.5 m | Keeps the drone visibly airborne |
| Move speed | 5 m/s | Fast pressure unit, still readable |
| Rotation speed | 10 | Smooth turn toward the path |
| Stuck timeout | 3 s | Long enough for an exit/avoidance, short enough to let the Director respawn |
| Path probe radius | 0.9 × controller radius | Skip a waypoint only when the Wisp fits through the gap |
| Fire interval | 0.75 s | Inspector-authored on `Wisp.prefab`; no runtime override |
| Damage | 6 | Roughly 8 DPS before misses |

Path smoothing uses a sphere cast toward the next waypoint. If the line is clear, the Wisp advances its path index early. The probe ignores the player and other `IDamageable` agents, so an ally does not look like a wall.

Stuck detection tracks the best distance reached toward the current path point. If there is no improvement for 3 s and the Wisp is not firing or leaving a bay, it calls `WispHealth.Kill()` so the existing crash sequence plays. A bare `Destroy` remains only as a fallback when no health component exists.

---

## 5. Combat

The Wisp is a flying harassment unit, not a boss:

- **60 HP**, so precise player fire removes it quickly.
- **15 m** attack range.
- Fire requires LOS.
- **0.75 s** between shots, **6 damage** per hit. This is an Inspector value on the prefab, not a hard-coded runtime rate.
- Aim point is the player's chest centre.
- The beam is a short-lived `LineRenderer`; the old version only drew a Scene-view debug line.
- The shot ignores other agents but stops on real blockers.
- Damage is applied through the shared `IDamageable` contract.
- Each shot plays a short synthesized 3D sound from a runtime-created `AudioSource`.

The Wisp stops pathing while firing (`Path = { transform.position }`), turns to the player, and resumes navigation when the player leaves range or LOS.

---

## 6. Visual and audio feedback

`WispVisuals` is fully procedural; no animation clips are required.

| State | Behaviour |
|---|---|
| Spawn | Scale from 0.15 to 1 and spin into place over 0.35 s |
| Hover | Sinusoidal vertical bob, speed-dependent amplitude |
| Banking | Roll from yaw change |
| Lean | Pitch/roll from local planar velocity |
| Attack | Recoil, pitch kick and scale pulse over 0.18 s |
| Hurt | Random positional shake over 0.15 s |
| Death | Scale down, tilt and fade glow over 0.6 s |
| Culling | Hide renderers/trail/light beyond 30 m and mute audio |

The hover hum is generated at runtime as a looping 110 Hz/220 Hz tone, so no audio asset is required. Audio is muted while culled and stopped on death.

Shots use a separate `ShotAudio` child created by `AgentMotor`, with a 0.12 s synthesized descending tone and `PlayOneShot`. The source is 3D, null-checked and independent of the hover hum, which remains owned by `WispVisuals`.

The death sequence in `WispHealth` disables control and colliders, then spins and drops the root for 2 s before destroying it. `Kill()` enters that sequence directly, so a stuck Wisp crashes visibly instead of disappearing.

---

## 7. Bugs found and fixes

| Symptom | Cause | Fix |
|---|---|---|
| Wisp only damaged the player at point-blank range | Aimed at the player's feet; floor/props blocked the ray | Aim at `CharacterController.bounds.center`; sort hits; ignore own hierarchy |
| Wisp did not leave the bay | 2 m collider could not fit the 1.4 m hatch; hover climb wedged it | Compact Wisp collider, hold altitude on `ExitBay`, suspend stuck detection during exit |
| Wisp returned to the terminal repeatedly | Target switched to terminal immediately on LOS loss; graph is disconnected | 2 s last-known grace, sight-gated terminal, 1.5 s target cooldown |
| Wisp fired through other agents | All colliders counted as blockers | Ignore colliders with an `IDamageable` parent |
| Wisp followed stale waypoints into cover | No path smoothing/validation | Sphere-cast path probe before skipping a node |
| Warden floated and died from Wisp-only movement | Shared `AgentMotor` applied hover and stuck logic to every agent | Gate capsule, vertical movement and stuck timer behind `isFlying` |
| Wisp could remain stuck forever | No stuck handling | 3 s distance-progress watchdog; `WispHealth.Kill()` for a visible crash |
| Wisp fire rate ignored the Inspector | `Awake()` forced `fireInterval` to 0.33 | Removed the override; prefab now sets 0.75 |
| Wisp had no sight or hearing model | Decisions used raw LOS only | 25 m / 120° vision plus 10 m `NoiseBus` hearing |
| Shot had no sound | No shot audio source | Runtime-created 3D `ShotAudio` with synthesized clip |
| Shot blockers spammed the Console | Test `Debug.Log` per blocked shot | Removed test logging |
| Graph problems were invisible and manual | No validation or generation | Validator plus `AutoWaypointGenerator` with reciprocal links |

