# Design log: Rithish

**Roles:** Intelligent Systems (IS) — Wisp drone AI, with shared gameplay motor and visual feedback.  
**Main code:** `Assets/Scripts/AI/PathfinderBrain.cs`, `Assets/Scripts/Motor/AgentMotor.cs`, `Assets/Scripts/Motor/WispVisuals.cs`, `Assets/Scripts/Motor/WispHealth.cs`, `Assets/Scripts/AI/WaypointGraph.cs`, `Assets/Scripts/Core/`.  
**Editor tooling:** `Assets/Editor/WaypointGraphValidator.cs`.  
**Branch:** `fix/wisp-glitches`  
**Date:** 21 October 2026

---

## 1. What I built

The Wisp is the Director's fast flying pressure unit. My work covers the whole runtime loop:

| Area | Main files | Responsibility |
|---|---|---|
| Decision layer | `PathfinderBrain`, `IAgentBrain`, `AgentCommand`, `WorldSnapshot` | Choose a path, look target, fire state and animation intent |
| Navigation | `PathfinderBrain`, `WaypointGraph`, `WaypointNode` | Exposure-weighted A*, direct-chase fallback, bay exit |
| Movement | `AgentMotor` | Hover, path following, rotation, stuck detection |
| Combat | `AgentMotor`, `WispHealth` | Chest-target hitscan, beam, damage, death |
| Presentation | `WispVisuals` | Procedural spawn, attack, hurt, death, banking, lean, hover audio |
| Validation | `WaypointGraphValidator` | Detect isolated nodes and one-way links |

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

The current greybox graph is the main limitation:

- only **10 nodes**, all `WP_Bay_*`;
- only **3 edges**: `0↔1`, `2↔3`, `7↔9`;
- nodes `4`, `5`, `6` and `8` are isolated;
- that is **7 disconnected components**.

So A* can only route inside one of those small pairs. It cannot cross the map. The runtime therefore needs a reliable direct-chase fallback, and the level needs more room/corridor nodes before the Wisp can navigate the full facility.

`WaypointGraphValidator` was added to make this visible:

**`Tools > OVERRIDE > Validate Waypoint Graph`**

It reports:

- isolated nodes with zero neighbours;
- missing reciprocal edges (`A → B` exists but `B → A` does not).

This is a diagnostic tool only; it does not edit the scene.

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

1. **LOS present:** update `lastSeenPlayerPosition`; `timeSinceLosLost = 0`.
2. **LOS lost for under 2 s:** chase `lastSeenPlayerPosition`.
3. **LOS lost for 2 s or more and player within 15 m of a terminal:** target that terminal.
4. **Target mode changes** are limited by a **1.5 s cooldown**, so the Wisp cannot flip between player and terminal every frame.
5. **A* failure:** chase the last-seen position only if a direct sphere cast is clear. If not, hold position. It never falls back to the terminal.

This keeps the terminal ambush as a deliberate behaviour rather than an oscillation.

---

## 4. Bay launch and movement

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
| Fire interval | 0.33 s | Three shots per second; prefab's old 1 s value is migrated at Awake |
| Damage | 6 | Roughly 18 DPS before misses |

Path smoothing uses a sphere cast toward the next waypoint. If the line is clear, the Wisp advances its path index early. The probe ignores the player and other `IDamageable` agents, so an ally does not look like a wall.

Stuck detection tracks the best distance reached toward the current path point. If there is no improvement for 3 s and the Wisp is not firing or leaving a bay, it destroys itself so the Director can replace it.

---

## 5. Combat

The Wisp is a flying harassment unit, not a boss:

- **60 HP**, so precise player fire removes it quickly.
- **15 m** attack range.
- Fire requires LOS.
- **0.33 s** between shots, **6 damage** per hit.
- Aim point is the player's chest centre.
- The beam is a short-lived `LineRenderer`; the old version only drew a Scene-view debug line.
- The shot ignores other agents but stops on real blockers.
- Damage is applied through the shared `IDamageable` contract.

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

The death sequence in `WispHealth` disables control and colliders, then spins and drops the root for 2 s before destroying it.

---

## 7. Bugs found and fixes

| Symptom | Cause | Fix |
|---|---|---|
| Wisp only damaged the player at point-blank range | Aimed at the player's feet; floor/props blocked the ray | Aim at `CharacterController.bounds.center`; sort hits; ignore own hierarchy |
| Wisp did not leave the bay | 2 m collider could not fit the 1.4 m hatch; hover climb wedged it | Compact Wisp collider, hold altitude on `ExitBay`, suspend stuck detection during exit |
| Wisp returned to the terminal repeatedly | Target switched to terminal immediately on LOS loss; graph is disconnected | 2 s last-seen grace, 1.5 s target cooldown, last-seen fallback instead of terminal |
| Wisp fired through other agents | All colliders counted as blockers | Ignore colliders with an `IDamageable` parent |
| Wisp followed stale waypoints into cover | No path smoothing/validation | Sphere-cast path probe before skipping a node |
| Wisp could remain stuck forever | No stuck handling | 3 s distance-progress watchdog and self-destroy for Director respawn |
| Wisp prefab kept the old 1 s fire rate | Prefab serialised `fireInterval: 1` | Awake migrates exactly `1` to `0.33` |
| Graph problems were invisible | No editor validation | Menu item reports isolated nodes and missing reciprocal edges |

---

## 8. Verification

- Runtime compilation: **passed** using the Unity Roslyn compiler against the full `Assembly-CSharp` response file.
- Editor compilation: **passed** for `WaypointGraphValidator.cs` against `Assembly-CSharp-Editor`.
- Manual Play-mode checks confirmed the bay exit, chest-target shooting and the reduced terminal-return behaviour.
- No automated EditMode tests currently cover the Wisp brain or motor. The Director and Hunter have their own test suites; Wisp testing is still manual.
- The graph limitation is verified from the scene data: **10 nodes, 3 edges, 7 components**.

### Current limitations

1. **The greybox graph is not a full navigation graph.** It contains only bay anchors. A* cannot route between rooms until room, doorway and corridor nodes are authored.
2. **No Wisp unit tests.** The targeting hysteresis and bay-exit rules are currently protected by compile checks and play testing, not automated regression tests.
3. **Target cooldown gates player/terminal mode changes, not every positional update.** This keeps player tracking responsive while preventing mode flapping.
4. **The server-hall/maintenance rooms still depend on direct chase** when the graph has no route. This works in open sight lines but is not a substitute for a connected graph.

---

## 9. Viva talking points

1. **"Why not just chase the player directly?"**  
   *Direct chase is used when LOS and a sphere cast are clear. Walls, bay geometry and cover need A* or the last-seen fallback. The fallback is deliberately conservative: it never teleports and never walks through geometry.*

2. **"Why does the Wisp go to a terminal when it loses the player?"**  
   *Only after 2 seconds without LOS and only when the player is within 15 m of a terminal. This makes the terminal an ambush destination instead of a reflex. The 1.5 s cooldown prevents target flapping.*

3. **"Why is the graph so small?"**  
   *It currently contains only the 10 bay anchor nodes, not a full navigation mesh. The validator exposes the disconnected components. Adding room and doorway nodes is the next navigation step, not a runtime hack.*

4. **"Why did the Wisp get stuck in the bay?"**  
   *It was physically too tall for the 1.4 m hatch and tried to climb to 2.5 m before exiting. The Wisp now uses a 1.2 m collider and holds its launch altitude until it is outside.*

5. **"Why separate the motor and brain?"**  
   *The brain is deterministic decision logic; the motor owns Unity physics and CharacterController movement. Shared contracts let the Warden reuse the motor and let the Director launch the Wisp without knowing its AI implementation.*
