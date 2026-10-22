# Evidence: Gunitha (Systems Engineer + Hunter)

What each capture shows, and which marking criterion it supports. Numbers are copied into `Docs/DesignLog/Gunitha.md`, section 5.

| File | Shows | Criterion |
|---|---|---|
| `01_rest_bodies.png` | Performance overlay with nothing moving: active dynamic bodies (props sleep) | G&V optimization: Rigidbody sleeping |
| `02_firing_gc.png` | Overlay while holding fire: effects recycled, frame rate and main-thread time (pooled effects) | G&V optimization: pooling, non-allocating queries |
| `03a_physics_50hz.png` / `03b_physics_60hz.png` | Physics step time at 50 Hz vs 60 Hz fixed timestep | G&V optimization: fixed timestep |
| `04_hunter_tick.png` | Overlay `Hunter.Tick` + the Seeker's F1 label (tick ms, A* cells expanded) | IS implementation, G&V optimization: time-slicing |
| `05_grid_bake.png` | Console: grid size, walkable / crouch-only cells, link counts, bake time | IS implementation |
| `07_tests.png` | Test Runner: Hunter edit-mode tests passing | IS code quality |
| `08_lure_sequence.png` | Canister impact pulls the Seeker; the area then drains | IS creativity ("sound propagation to hunt") |
| `09a_physics_settings.png` | Project Settings > Physics: fixed timestep and layer collision matrix | G&V optimization |
| `09b_canister_rigidbody.png` | Canister Rigidbody: Continuous Dynamic + Interpolate (only on the fast prop) | G&V optimization: collision modes |

The lure **video** clip is kept out of Git (large file): it goes in the team's shared drive for the demo video.
