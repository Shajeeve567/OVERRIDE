// ZoneLocator.cs
// Answers "which zone is this point in?" by reading the greybox's own floors at Play time.
//
// WHY NOT ZONE VOLUMES (yet): the greybox already knows exactly where each room is. Reading its floor
// boxes means the answer always matches the level, even after LevelSize changes, with nothing extra to
// place by hand. Proper trigger volumes can replace this later without the Director noticing.

using System.Collections.Generic;
using UnityEngine;

namespace Override.Director
{
    public sealed class ZoneLocator
    {
        readonly List<(string zone, Bounds box)> rooms = new List<(string, Bounds)>();
        Vector3 coreCentre;
        float coreRadius;   // 0 = no Core found

        public ZoneLocator()
        {
            var greybox = GameObject.Find("OVERRIDE_Greybox");
            if (greybox == null) { Debug.LogWarning("[Director] No OVERRIDE_Greybox: every position reads as 'Unknown'."); return; }

            foreach (string name in new[] { "Airlock", "Labs", "ServerHall", "Maintenance" })
            {
                var floor = greybox.transform.Find(name + "/Floor");
                // Collider bounds, not renderer bounds: the kit dresser switched the floor RENDERERS off,
                // but the colliders are still there (and Collider.bounds is valid in Play mode).
                var col = floor != null ? floor.GetComponent<Collider>() : null;
                if (col != null) rooms.Add((name, col.bounds));
            }

            // The Core is round, so it's a circle test instead of a box.
            var coreFloor = greybox.transform.Find("Core/Floor");
            var coreCol = coreFloor != null ? coreFloor.GetComponent<Collider>() : null;
            if (coreCol != null)
            {
                coreCentre = coreCol.bounds.center;
                coreRadius = coreCol.bounds.extents.x;
            }
        }

        public string ZoneOf(Vector3 p)
        {
            if (coreRadius > 0f)
            {
                Vector2 d = new Vector2(p.x - coreCentre.x, p.z - coreCentre.z);
                if (d.sqrMagnitude <= coreRadius * coreRadius) return "Core";
            }
            foreach (var room in rooms)
                if (p.x >= room.box.min.x && p.x <= room.box.max.x && p.z >= room.box.min.z && p.z <= room.box.max.z)
                    return room.zone;
            return "Corridor";
        }
    }
}
