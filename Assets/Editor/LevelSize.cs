// LevelSize.cs
// One place for the level's size. GreyboxBuilder, PropBuilder and PlayerBuilder all read from here,
// so the walls, the props and the player spawn can never drift out of step.
//
// HOW THE SCALE WORKS
//   Every floor-plan coordinate in the builders is still written in the ORIGINAL design units
//   (the 60 x 50 m plan from the GDD and the zone mockups). Plan multiplies them all, so the
//   layout keeps the same shape and doorway positions, only bigger. Change Plan and rebuild.
//
//   Heights are NOT multiplied by Plan: a room twice as wide doesn't need to be twice as tall.
//   They are set directly below instead.

namespace Override.EditorTools
{
    public static class LevelSize
    {
        // Floor-plan multiplier. 1.0 = the original ~60 x 58 m plan; 2.5 = ~150 x 145 m.
        // Room for Wisps (25 m vision, 20 m attack range) to flank instead of spawning on top of you.
        public const float Plan = 2.5f;

        // Turns a design-unit number from the mockups into metres in the built level.
        public static float S(float designUnits) => designUnits * Plan;

        // Heights in metres. Raised so drones can fly ABOVE cover, which is what makes them
        // flank in 3D instead of just walking around crates.
        // Tall enough that a drone hovering near the ceiling is still clearly in view from the floor.
        public const float RoomHeight        = 10.0f; // Labs, Server Hall, Maintenance (was 3.5)
        public const float AirlockHeight     = 5.0f;  // small start room stays modest (was 3.0)
        public const float CoreHeight        = 16.0f; // final arena (was 6.0)
        public const float CorridorHeight    = 7.0f;  // drones can follow you through (was 3.0)
        public const float AirlockDoorHeight = 4.0f;  // (was 2.5)
        public const float TunnelHeight      = 2.2f;  // unchanged: the dark tunnel stays cramped
        public const float VentHeight        = 1.2f;  // unchanged: still crouch-only
    }
}
