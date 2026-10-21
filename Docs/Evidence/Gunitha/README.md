# Evidence: Gunitha (Systems Engineer + Hunter)

What each capture shows, and which marking criterion it supports. Numbers are copied into `Docs/DesignLog/Gunitha.md`, section 5.

| File | Shows | Criterion |
|---|---|---|
| `01_rest_bodies.png` | Performance overlay with nothing moving: active dynamic bodies (props sleep) | G&V optimization: Rigidbody sleeping |
| `02_firing_gc.png` | Overlay while holding fire: GC alloc per frame stays near 0 B (pooled effects) | G&V optimization: pooling, non-allocating queries |
| `03a_physics_50hz.png` / `03b_physics_60hz.png` | Physics step time at 50 Hz vs 60 Hz fixed timestep | G&V optimization: fixed timestep |
| `04_hunter_tick.png` | Overlay `Hunter.Tick` + the Seeker's F1 label (tick ms, A* cells expanded) | IS implementation, G&V optimization: time-slicing |
| `05_grid_bake.png` | Console: grid size, walkable / crouch-only cells, link counts, bake time | IS implementation |
| `06_profiler_hunter_tick.png` | Unity Profiler, Hierarchy filtered to `Hunter.Tick` (time + GC Alloc) | Both: proof from Unity's own tool |
| `07_tests.png` | Test Runner: 17 Hunter edit-mode tests passing | IS code quality |
| `08a_heatmap_start.png` | F1 heatmap at the start: spread out (knows nothing) | IS viva: the belief grid |
| `08b_heatmap_two_peaks.png` | Two noises far apart: two peaks (contradictory evidence) | IS edge case |
| `08c_heatmap_contact.png` | Proximity contact: belief collapsed onto the player | IS viva |
| `09_lure_sequence.png` | Canister impact pulls the Seeker; the area then drains | IS creativity ("sound propagation to hunt") |
| `10a_physics_settings.png` | Project Settings > Physics: fixed timestep and layer collision matrix | G&V optimization |
| `10b_canister_rigidbody.png` | Canister Rigidbody: Continuous Dynamic + Interpolate (only on the fast prop) | G&V optimization: collision modes |

The lure **video** clip is kept out of Git (large file): it goes in the team's shared drive for the demo video.
