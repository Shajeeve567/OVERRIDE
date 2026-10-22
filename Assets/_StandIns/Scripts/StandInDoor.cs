// STAND-IN (delete with the _StandIns folder) -> replaced by Gunitha's SlidingDoor / PhysicsDoor
// (feature/interactions), which would also implement ILockableDoor.
//
// A slab across a corridor that slides up into the ceiling when the player comes near, unless it's
// locked. Two kinds of lock:
//   - gameLocked     : the level's own rule (the Core's doors stay sealed until Terminals 1-3 are done).
//   - directorLocked : ARGUS's lock, set by the Director's plan (ILockableDoor.SetDirectorLock).
// The strip on the slab shows the state: teal = free, red = ARGUS lock, hazard yellow = sealed.
// Built at Play time by StandInGame; nothing is saved into the scene.

using Override.Level;
using UnityEngine;

namespace Override.StandIns
{
    public sealed class StandInDoor : MonoBehaviour, ILockableDoor
    {
        public int id;
        public string zoneA, zoneB;
        public bool gameLocked;
        public float openDistance = 4f;
        public float slideSpeed = 4f;   // metres per second

        public Transform slab;
        public Renderer strip;
        public Material freeMaterial, directorMaterial, sealedMaterial;
        public float height;

        bool directorLocked;
        Transform player;
        Vector3 closedLocal;

        public int DoorId => id;
        public Vector3 Position => transform.position;
        public bool Connects(string zone) => zone == zoneA || zone == zoneB;
        public bool CanBeLocked => !gameLocked;
        public void SetDirectorLock(bool locked) => directorLocked = locked;
        public bool IsLocked => gameLocked || directorLocked;

        void Start()
        {
            var p = GameObject.FindWithTag("Player");
            if (p != null) player = p.transform;
            closedLocal = slab.localPosition;
        }

        void Update()
        {
            bool near = player != null && Vector3.Distance(player.position, transform.position) < openDistance;
            bool open = near && !IsLocked;

            // Slide up by the door's height when open, back down when closed.
            Vector3 target = closedLocal + (open ? Vector3.up * (height - 0.2f) : Vector3.zero);
            slab.localPosition = Vector3.MoveTowards(slab.localPosition, target, slideSpeed * Time.deltaTime);

            var mat = gameLocked ? sealedMaterial : directorLocked ? directorMaterial : freeMaterial;
            if (strip != null && strip.sharedMaterial != mat) strip.sharedMaterial = mat;
        }
    }
}
