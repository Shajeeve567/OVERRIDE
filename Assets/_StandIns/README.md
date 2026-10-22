# Stand-ins (temporary, delete before submission or after merging)

These let the Director be play-tested before teammates' systems are merged.
Everything temporary lives in **this folder**, plus **one scene object** (`_STANDINS`) and **one
Inspector slot** (the Director's *Drone Prefab*).

## What's here, and what replaces it

| Stand-in | What it does | Replaced by |
|---|---|---|
| `Scripts/StandInDrone.cs` + `Prefabs/StandInDrone.prefab` | Red sphere: flies out of its bay, hovers after the player, 5 damage/s in range, dies after 3 bullet hits | Agent Controller's **Wisp** prefab (assign it to the Director's *Drone Prefab* slot) |
| `Scripts/StandInPlayerHealth.cs` | 100 HP, regen, respawn at the last terminal done | Gunitha's `Health` / `PlayerDeath` (feature/rifle) |
| `Scripts/StandInTerminal.cs` | Hold E 8 s (Core 30 s), damage pauses it, Core needs T1-T3 | Gunitha's `HackTerminal` (feature/interactions) |
| `Scripts/StandInDoor.cs` | Sliding corridor doors, ARGUS lock + Core seal | Gunitha's `SlidingDoor` / `PhysicsDoor` (+ `ILockableDoor`) |
| `Scripts/StandInGame.cs` | Adds all of the above at Play time; HUD (health, objectives, hack bar, messages) | The real HUD / game flow |
| `Editor/StandInSetup.cs` | Menus: *Set Up Test Game*, *Remove From Scene* | (nothing) |

## Hooks into the permanent code (keep these)

The permanent Director code never references a stand-in. The stand-ins call the Director's public methods:

| Stand-in call | After the merge, wire instead |
|---|---|
| `director.ReportPlayerDamaged(dmg)` | `GameEvents.PlayerDamaged += director.ReportPlayerDamaged;` |
| `director.ReportHackStarted(zone)` | `GameEvents.HackStarted += z => director.ReportHackStarted(z.ToString());` |
| `director.ReportTerminalOverridden(zone)` | `GameEvents.TerminalOverridden += z => director.ReportTerminalOverridden(z.ToString());` |
| `director.ReportEngagement()` | call it when the player hits an ARGUS unit |
| `StandInDoor : ILockableDoor` | add `ILockableDoor` to the real door scripts |
| `StandInDrone : IDirectorDrone` | optional on the Wisp (gives it its bay's waypoint and the player) |

## How to remove

1. `OVERRIDE > Stand-ins > Remove From Scene` (deletes `_STANDINS`, empties the Director's drone slot).
2. Delete the `Assets/_StandIns` folder **in Unity** (so the `.meta` files go too).
3. Save the scene.
