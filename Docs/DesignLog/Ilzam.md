# OVERRIDE — Core Developer Design Log & Technical Justification

**Author:** Ilzam (Core Developer)  
**Modules:** Graphics & Visualization (G&V) + Intelligent Systems (IS)  
**Project:** OVERRIDE — Tactical First-Person Infiltration Game (Unity 6 / URP)  
**Branch:** `feature/warden-gameplay-2`  
**Date:** 21 October 2026  

---

## 1. Ownership & Project Scope

As Core Developer, my responsibilities span two core disciplines:

1. **Intelligent Systems (IS)**:
   * Architecture, mathematical design, and pure C# implementation of the **Warden's Depth-2 Expectimax AI**.
   * Real-time **Player-Response Probability Learning** using Laplace smoothing.
   * Autonomous, reusable **Perception Sensors**: 3D geometric `VisionSensor` and listener-based `HearingSensor`.
   * Live gameplay adapter connecting pure algorithmic decisions to team physics, character motors, health contracts, and tactical cover navigation.
2. **Graphics & Visualization (G&V)**:
   * Original 3D hard-surface modeling, topology optimization, UV unwrapping, and PBR texturing for game characters, weapons, and environment props in Blender.
   * Complete Unity URP pipeline integration: FBX hierarchy, materials, normal maps, emission, colliders, and weak points.
   * Asset creation: **Refined Warden**, **Seeker Hunter**, **Wisp Drone**, **Player Rifle**, and **Override Terminal**.

---

## 2. Intelligent Systems Architecture (Warden AI)

### 2.1 Why Expectimax Over Minimax and FSM?

In game AI development, combatants are frequently driven by Finite State Machines (FSMs) or Minimax trees. Both have fundamental flaws in tactical combat:

* **Why not an FSM?** Traditional FSMs (e.g., Patrol $\to$ Chase $\to$ Shoot $\to$ Cover) produce rigid, predictable, and exploitable enemies. Once a player discovers the transition thresholds, the AI can be cheesed easily.
* **Why not Minimax?** Minimax is designed for zero-sum, alternating, deterministic board games (like Chess). In an FPS, assuming the player always makes the *mathematically optimal counter-move* leads to pathologically conservative behavior: the enemy assumes the player will land every headshot or hide instantly, causing the AI to freeze in cover or refuse to take tactical risks.
* **The Expectimax Solution**: Expectimax replaces adversarial Min-nodes with **Chance Nodes** representing modeled human player tendencies. The Warden evaluates its root actions, predicts player responses probabilistically based on actual observed combat history, and chooses the action with the highest **Mathematical Expected Value ($E[V]$)**:

$$E[V(a)] = \sum_{r \in \text{Responses}} P(r \mid \text{history}) \cdot V(s')$$

```
                       [Warden Turn 1 (Max)]
                           /     |     \
               Peek & Fire    Flank    Retreat & Reload
                  /              |              \
           [Chance Node]   [Chance Node]   [Chance Node]
           /    |    \     /    |    \     /    |    \
        Peek  Push  Hide  Peek Push Hide  Peek Push Hide
          |     |     |     |    |     |    |    |     |
                       [Warden Turn 2 (Max)]
                                 ...
                          [Evaluation Leaf]
```

### 2.2 Depth-2 Tree Search Formulation

The Warden uses a depth of two full turns:
1. **Turn 1 (Max)**: Warden considers 5 candidate actions:
   * `Hold`: Preserve current cover, prepare reactions, reload if empty.
   * `PeekAndFire`: Step out and deliver an accurate 3-round burst (cost: 3 ammo).
   * `AdvanceToCover`: Push aggressively to a forward tactical vantage point.
   * `Flank`: Reposition laterally around cover to circumvent the player's firing angle.
   * `RetreatAndReload`: Fall back to distant cover and complete a full weapon reload.
2. **Turn 1 (Chance)**: Player responds with one of 3 archetypes:
   * `Peek`: Player looks out to shoot.
   * `Push`: Player charges forward aggressively ($\Delta d \approx -4\text{ m}$).
   * `Hide`: Player retreats behind full cover to break sight.
3. **Turn 2 (Max)**: Warden evaluates future second-turn counter-actions.
4. **Turn 2 (Chance)**: Second player response simulation.
5. **Leaf Evaluation ($V$)**: Multi-objective heuristic score:

$$V(s) = w_{\text{damage}} \cdot \Delta \text{Dmg} - w_{\text{taken}} \cdot \text{DmgTaken} + w_{\text{cover}} \cdot Q_{\text{cover}} + w_{\text{range}} \cdot S_{\text{range}} + w_{\text{ammo}} \cdot \frac{\text{Ammo}}{\text{MaxAmmo}} + w_{\text{desp}} \cdot (\text{Desperation} \cdot \text{Aggression})$$

#### Evaluation Weights:
* $\text{DamageDealtWeight} = 1.0$, $\text{DamageTakenWeight} = 1.25$
* $\text{CoverWeight} = 8.0$, $\text{RangeWeight} = 5.0$, $\text{AmmoWeight} = 4.0$
* $\text{PreferredRange} = 18.0\text{ m}$ (calculated via normalized error: $1 - \frac{|d - 18|}{18}$)
* $\text{DesperationAggressionWeight} = 6.0$ (scaled by ARGUS Director alarms and compromised subsystems)

### 2.3 Dynamic Player-Response Probability Learning

Rather than hardcoding player tendencies, the Warden learns the player's personal playstyle in real time using **Laplace Smoothing (Pseudocounts)**:

$$P(r) = \frac{C(r) + k}{\sum_{i} C(i) + 3k}$$

* **Prior Counts ($k = 1$)**: Initial distribution begins balanced: $P(\text{Peek}) = 33.3\%$, $P(\text{Push}) = 33.3\%$, $P(\text{Hide}) = 33.3\%$.
* **Monotonic Sequencing**: Observations are sequence-validated to prevent double-counting or processing stale sensor events.
* **Observation Rules**:
  * Sight reacquisition $\to$ `Peek`
  * Approach while visible ($\Delta d \ge 1.5\text{ m}$) $\to$ `Push`
  * Confirmed loss of line of sight $\to$ `Hide`
* **Tactical Adaptation**: If the player frequently ducks behind cover (`Hide`), $P(\text{Hide})$ rises. The Warden dynamically shifts probability weights away from `PeekAndFire` and begins executing proactive `Flank` and `AdvanceToCover` maneuvers.

---

## 3. Autonomous Perception Sensors

### 3.1 Vision Sensor (`VisionSensor.cs`)
* **3D Field of View (FOV)**: Configurable cone; the Warden visual prefab is configured to $100^\circ$ and $35\text{ m}$. It is evaluated using normalized dot products:
  $$\vec{d} = \frac{\vec{T} - \vec{E}}{\|\vec{T} - \vec{E}\|}, \quad \vec{F}_{\text{eye}} \cdot \vec{d} \ge \cos\left(\frac{\text{FOV}}{2}\right)$$
* **Layer-Masked Occlusion Raycasting**: `Physics.Raycast` against `ObstacleLayers`, verifying target root identity to prevent false occlusions.
* **Memory Retention**: Tracks `LastSeenPosition` and `LastSeenTime` after sight is broken, allowing the AI to deliberate using confirmed memory without cheating.

### 3.2 Hearing Sensor (`HearingSensor.cs`)
* **NoiseBus Integration**: Subscribes to global sound events emitted by `FootstepNoise.cs` and weapon discharges.
* **Configurable sensitivity**: The isolated gameplay test uses a $25\text{ m}$ cap and $2.5\times$ multiplier; the reusable prefab keeps the project baseline of $15\text{ m}$ and $1.0\times$ so test balance does not leak into the main game.
* **Obstacle attenuation**: Raycasts between emitter and listener; obstructed noise is attenuated rather than completely silenced. The isolated test uses $0.8\times$ while the reusable prefab uses the baseline $0.5\times$.
* **Eye-Level Listener**: Positioned at $Y = 1.9\text{ m}$ (head height) to eliminate floor raycast clipping.

---

## 4. Live Gameplay Adapter & Combat Dynamics

### 4.1 Continuous Threat Facing & Strafing
* Previously, the Warden faced only in his movement path direction, causing him to turn his back to the player when falling back.
* **Solution**: `WardenGameplayController.UpdateFacing()` runs continuously in `LateUpdate()`. While repositioning or backpedaling to cover, the current implementation rotates the Warden root towards the player's last known position:
  $$\vec{D} = \vec{P}_{\text{lastKnown}} - \vec{W}_{\text{pos}}, \quad \text{Rotation} = \text{Slerp}(\text{rot}, \text{LookRotation}(\vec{D}), \omega_{\text{aim}} \cdot \Delta t)$$
* **Result**: The $100^\circ$ Warden FOV remains directed towards confirmed threat memory while moving. Reacquisition is interval-based (vision checks and AI decisions), so it is responsive but not zero-latency.

### 4.2 Tactical Cover Balancing
Cover points are balanced to incentivize proactive, intelligent combat:
* **Advance Cover**: Quality `0.85`, forward positioning ($d \approx 5\text{ m}$).
* **Flank Cover**: Quality `0.85`, lateral angle ($d \approx 8\text{ m}$).
* **Current Cover**: Quality `0.75`.
* **Retreat Cover**: Quality `0.70`, fallback distance ($d \approx 13\text{ m}$).
* **Demonstrated loop**: In the isolated test layout, the higher-quality advance/flank markers make the Warden reposition when the player hides. These are authored semantic markers, not automatic geometry or NavMesh cover analysis. Retreat/reload is score-dependent, with a safe reload fallback whenever another action is selected with fewer than three rounds.

### 4.3 Weapon Self-Damage Filter
* In `WardenWeapon.cs`, firing rays use `Physics.RaycastAll` sorted by distance and ignore colliders inside the firing Warden's controller hierarchy. This prevents self-damage without accidentally ignoring another Warden under a shared scene parent.

---

## 5. Graphics & Visualization (3D Modeling Pipeline)

### 5.1 Pipeline & Standards
* **DCC Tool**: Blender 4.x / 3.6 LTS.
* **Export Protocol**: FBX format, $+Z$ forward, $+Y$ up, scale $1.0$, transform rotations applied.
* **Texture Atlasing**: Imported models contain UV layers and use the documented 2048x1024 / 1024x512 PBR atlas workflow. UV-layer presence and material wiring were verified; non-overlap should be evidenced separately with Blender UV screenshots.
* **URP Lit Shaders**: Albedo (`_BaseMap`), Metallic/Smoothness (`_MetallicGlossMap`), Tangent-space Normal (`_BumpMap`), and Emission (`_EmissionMap`).

### 5.2 Model Statistics & Poly-Budget Audit

All assets conform strictly to the assignment's 10,000-triangle budget:

| Asset | Blender Source | FBX Export | Triangles | UV Status | Budget Conformance |
|---|---|---|---|---|---|
| **Refined Warden** | `ArtSource/Warden/Warden_Refined.blend` | `Assets/_Project/Art/Models/Warden/Warden_Refined.fbx` | **5,064** | UV layer present | **-49.4%** under budget |
| **Seeker Hunter** | `ArtSource/Seeker/Seeker_Hunter.blend` | `Assets/.../Seeker_Hunter.fbx` | **6,448** | Fully unwrapped | **-35.5%** under budget |
| **Wisp Drone** | `ArtSource/Wisp/Wisp.blend` | `Assets/.../Wisp.fbx` | **4,142** | Fully unwrapped | **-58.6%** under budget |
| **Player Rifle** | `ArtSource/Rifle/Rifle.blend` | `Assets/.../Rifle.fbx` | **3,504** | Fully unwrapped | **-65.0%** under budget |
| **Override Terminal** | `ArtSource/Terminal/Terminal.blend` | `Assets/.../Terminal.fbx` | **1,898** | Fully unwrapped | **-81.0%** under budget |

### 5.3 Hierarchy Rigging & Weak Points
* **Refined Warden**: 7 articulation meshes, pivot empties for torso, head, gun arm, and utility arm.
* **Weak Points**:
  * Head / Eye: Sphere collider ($r = 0.16\text{ m}$), $2\times$ critical damage multiplier.
  * Rear Power Core: Box collider ($0.42 \times 0.36 \times 0.20\text{ m}$), $2\times$ critical damage multiplier.
* **Seeker Hunter**: 13 child meshes, cached eye, thrusters, claws, and central chassis weak point.

---

## 6. Automated Verification & Testing

The implementation is verified by **45 automated EditMode tests** passing in a clean isolated Unity batch run on 21 October 2026:

* `DirectorBrainTests` (16 tests): Validates ARGUS director progression and cost scoring.
* `WardenExpectimaxBrainTests` (7 tests): Validates depth-2 expansions, probability learning, empty-ammo fallback, and tactical weight equations.
* `HearingSensorTests` (6 tests): Validates radius thresholds, wall attenuation, stale event rejection, and noise filtering.
* `SeekerAssetTests` (3 tests): Validates Seeker hierarchy, weak point multipliers, and asset builder.
* `WardenGameplayAdapterTests` (13 tests):
  * Weapon self-damage immunity.
  * Noise-driven look rotation while hidden.
  * Continuous threat facing while moving along path.
  * Scene balance is preserved while external references and the vision target are wired.
  * Empty and one/two-round magazine reload handling.
  * Immediate weapon cancellation on Warden death.
  * Two Wardens under one shared parent can still damage one another.

---

## 7. Viva Talking Points & Examiner Defense

1. **"Why use Expectimax when the player is an opponent?"**  
   *Minimax assumes optimal adversarial counterplay. In video games, players make mistakes, hesitate, or panic. Modeling the player as a chance node with real-time learned probabilities allows the enemy to exploit player habits without freezing under worst-case assumptions.*
2. **"How does the AI prevent cheating?"**  
   *The Expectimax brain has zero access to Unity transforms or physics raycasts. When the player is hidden, decisions are made purely on confirmed sensor memory (`LastSeenPosition` or `LastHeardPosition`). If the player sneaks away silently, the Warden inspects where the player was last detected.*
3. **"How did you optimize your 3D models for runtime performance?"**  
   *The submitted assets remain below the 10,000-triangle budget, import with UV layers, and use reusable URP materials and atlas textures. Topology and UV non-overlap are demonstrated with Blender evidence rather than inferred from the Unity importer alone.*
4. **"How do you ensure smooth coordination with other developers' branches?"**  
   *I used an adapter pattern (`WardenGameplayController`) that reads team contracts (`AgentMotor`, `Health`, `NoiseBus`) without mutating shared core interfaces, keeping all work merge-safe and isolated in unit-tested scenes.*
