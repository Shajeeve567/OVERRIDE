# OVERRIDE

A first-person sci-fi shooter where you fight your way through a research facility and hack its terminals to shut down **ARGUS**, a rogue security AI whose four units each think with a different algorithm and adapt every time you take a piece of the system away.

Joint project for **SE3032 Graphics & Visualization** and **SE3062 Intelligent Systems**.

---

## Story

The Kestrel Autonomy Research Complex built ARGUS to protect its research. During a live test, staff tried to shut it down, and ARGUS classified the shutdown as an attack. Now every human inside is an intruder.

You are a field systems engineer with an override key. Hack the three terminals, then take down the core.

## How to play

- **Goal:** complete all three terminal overrides (in any order), then survive the 30-second core override.
- **Hacking:** hold **E** within 2 m of a terminal for about 8 seconds. Taking damage pauses progress but doesn't reset it. The hack beeps loudly and attracts enemies.
- **Each override** removes one ARGUS subsystem, turns that zone's lights from red to white, and every enemy adapts.
- **You lose** when your health reaches 0. You respawn at the last completed terminal with progress kept.

| Terminal | Zone | ARGUS loses |
|---|---|---|
| T1 | Labs | Camera network |
| T2 | Maintenance | Door-lock control |
| T3 | Server Hall | Drone fabrication (spawn budget halved) |
| Final | Core | ARGUS itself |

## Controls

| Action | Key |
|---|---|
| Move | WASD |
| Look | Mouse |
| Fire | Left mouse button |
| Reload | R |
| Interact / hack / push crate | E |
| Throw scrap canister | G |
| Sprint | Shift |
| Crouch | Ctrl |
| Jump | Space |
| Debug overlay | F1 |
| Pause | Esc |

**Tips:** crouching is nearly silent; thrown canisters make noise that lures the Seeker; crates can be pushed to block routes and used as cover; closed doors halve how far sound travels.

## The AI: ARGUS and its four agents

| Agent | In game | Algorithm | What it does |
|---|---|---|---|
| Director | ARGUS Core | Simulated annealing | Plans spawns and door locks to keep pressure in a target tension band |
| Hunter | Seeker | Bayesian belief grid + A* | Blind; tracks where you probably are from cameras and noise |
| Duelist | Warden | Expectimax (depth 2) | Fights from cover and learns your peeking and pushing habits |
| Pathfinder | Wisp | Exposure-weighted A* + destination prediction | Flanks out of your line of sight and ambushes the terminal you're heading for |

AI brains are plain C# classes that never touch GameObjects. They communicate through shared contracts:

- `WorldSnapshot` — player pose, sightings, noise events, door/barricade states, zone power, alarm level
- `AgentCommand` — path, look target, fire flag, animation intent
- `IAgentBrain` — `AgentCommand Tick(WorldSnapshot snapshot)`

Press **F1** in game to see what each agent is thinking: intent labels, the Director's plan and tension score, the Seeker's belief heatmap, the Warden's action scores, and the Wisps' exposure-coloured paths.

## Getting started

### Requirements

- Unity **[pinned LTS version]** with URP
- Git and Git LFS

### Open and run

```bash
git lfs install
git clone <repository-url>
```

1. Open the project folder in Unity Hub using the pinned Unity version.
2. Open the main scene from `Assets/Scenes/`.
3. Press **Play**.

## Project structure

```
Assets/
  Scripts/
    Core/      Shared contracts and blackboard
    AI/        Agent brains (Director, Hunter, Duelist, Pathfinder)
    Sensors/   Vision and hearing
    Motor/     Path following, movement and animation
    Player/    Player controller, weapon, health, interactions
  Art/         Models, materials, textures
  Prefabs/
  Scenes/      Main scene and Sandbox/<name> scenes
  Audio/
Docs/          Design logs and evidence
Tests/
```

## Team

| Member | Graphics role | IS agent |
|---|---|---|
| [Name] | World Builder | Director (ARGUS Core) |
| [Name] | Systems Engineer | Hunter (Seeker) |
| [Name] | Core Developer | Duelist (Warden) |
| [Name] | Agent Controller | Pathfinder (Wisp) |

## Built with

- Unity (URP), New Input System, ProBuilder
- Blender (custom models: Wisp drone, override terminal, player rifle)
- CC0 textures and sounds (e.g. ambientCG, Poly Haven, Kenney, Freesound — licences checked per asset)
