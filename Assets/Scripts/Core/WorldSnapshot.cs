using UnityEngine;

namespace Override.Core
{
    public class WorldSnapshot
    {
        public float Time;
        public Vector3 PlayerPosition;
        public Vector3 PlayerForward;
        public bool PlayerVisible;
        
        // Data for the Hunter/Seeker
        public List<Vector3> Noises = new List<Vector3>(); 
        public bool CamerasOnline = true;
        
        // You can add Sightings here later when the sensors are built
    }
}