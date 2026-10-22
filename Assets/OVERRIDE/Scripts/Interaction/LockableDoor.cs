// LockableDoor.cs
// Adds the two kinds of lock to a SlidingDoor or PhysicsDoor on the same object:
//   - Sealed:   the level's own rule. The Core's doors stay shut until Terminals 1-3 are overridden.
//   - Director: ARGUS's lock (Shajeeve's ILockableDoor), set by the Director's plan.
// A locked door closes and ignores E. An optional indicator strip shows the state:
// teal = free, red = ARGUS lock, yellow = sealed (the colours of Shajeeve's stand-in doors).

using System.Collections.Generic;
using Override.Core;
using Override.Level;
using UnityEngine;

namespace Override.Interaction
{
    public class LockableDoor : MonoBehaviour, ILockableDoor
    {
        [SerializeField] int doorId;
        [Tooltip("The two zones this door joins, e.g. Labs and ServerHall (names as in ZoneId).")]
        [SerializeField] string zoneA = "Labs";
        [SerializeField] string zoneB = "ServerHall";
        [Tooltip("Shut until Labs, Maintenance and ServerHall terminals are all overridden (the Core's doors).")]
        [SerializeField] bool sealedUntilTerminals;

        [Header("Indicator (optional)")]
        [SerializeField] Renderer indicator;
        [Tooltip("If set, the strip swaps between these (e.g. Shajeeve's glow materials); otherwise it's tinted.")]
        [SerializeField] Material freeMaterial, directorMaterial, sealedMaterial;
        [SerializeField] Color freeColour = new Color(0.2f, 0.9f, 0.8f);
        [SerializeField] Color directorColour = new Color(1f, 0.15f, 0.1f);
        [SerializeField] Color sealedColour = new Color(1f, 0.8f, 0.1f);

        static readonly ZoneId[] SealTerminals = { ZoneId.Labs, ZoneId.Maintenance, ZoneId.ServerHall };
        readonly HashSet<ZoneId> overridden = new HashSet<ZoneId>();

        IDoor door;
        bool directorLocked;
        bool isSealed;
        MaterialPropertyBlock block;

        public int DoorId => doorId;
        public Vector3 Position => transform.position;
        public bool Connects(string zone) => zone == zoneA || zone == zoneB;
        public bool CanBeLocked => !isSealed;   // the Director leaves sealed doors out of its plans
        public bool IsLocked => isSealed || directorLocked;
        public bool IsSealed => isSealed;
        public bool IsDirectorLocked => directorLocked;

        /// Set up from code (the integration tool) before the scene is saved.
        public void Configure(int id, string a, string b, bool sealUntilTerminals, Renderer strip,
                              Material free = null, Material director = null, Material sealedMat = null)
        {
            freeMaterial = free;
            directorMaterial = director;
            sealedMaterial = sealedMat;
            doorId = id;
            zoneA = a;
            zoneB = b;
            sealedUntilTerminals = sealUntilTerminals;
            indicator = strip;
        }

        void Awake()
        {
            door = GetComponent<IDoor>();
            isSealed = sealedUntilTerminals;
        }

        void OnEnable() => GameEvents.TerminalOverridden += OnTerminalOverridden;
        void OnDisable() => GameEvents.TerminalOverridden -= OnTerminalOverridden;

        void Start()
        {
            if (IsLocked) door?.SetOpen(false);
            RefreshIndicator();
        }

        public void SetDirectorLock(bool locked)
        {
            if (locked == directorLocked) return;
            directorLocked = locked;
            if (IsLocked) door?.SetOpen(false);
            RefreshIndicator();
        }

        void OnTerminalOverridden(ZoneId zone)
        {
            overridden.Add(zone);
            if (!isSealed) return;
            foreach (var z in SealTerminals)
                if (!overridden.Contains(z)) return;
            isSealed = false;
            RefreshIndicator();
        }

        void RefreshIndicator()
        {
            if (indicator == null) return;
            Material m = isSealed ? sealedMaterial : directorLocked ? directorMaterial : freeMaterial;
            if (m != null)
            {
                if (indicator.sharedMaterial != m) indicator.sharedMaterial = m;
                return;
            }
            block ??= new MaterialPropertyBlock();
            Color c = isSealed ? sealedColour : directorLocked ? directorColour : freeColour;
            indicator.GetPropertyBlock(block);
            block.SetColor("_BaseColor", c);   // URP Lit/Unlit
            block.SetColor("_Color", c);       // built-in shaders, just in case
            indicator.SetPropertyBlock(block);
        }
    }
}
