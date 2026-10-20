using UnityEngine;

namespace Override.Motor
{
    public class WispVisuals : MonoBehaviour
    {
        [Header("Hover Settings")]
        public float hoverAmplitude = 0.2f;
        public float hoverFrequency = 2f;
        
        [Header("Banking Settings")]
        public float bankAmount = 15f;
        public float bankSpeed = 5f;

        private Vector3 startPos;
        private float currentBank = 0f;
        private float lastYRotation;

        private void Start()
        {
            startPos = transform.localPosition;
            lastYRotation = transform.eulerAngles.y;
        }

        private void Update()
        {
            // 1. Hover Bobbing (Sine wave)
            float newY = startPos.y + Mathf.Sin(Time.time * hoverFrequency) * hoverAmplitude;
            transform.localPosition = new Vector3(startPos.x, newY, startPos.z);

            // 2. Banking into turns (Calculated by how fast the drone is rotating)
            float currentY = transform.eulerAngles.y;
            float turnSpeed = Mathf.DeltaAngle(lastYRotation, currentY); // How much did we turn this frame?
            lastYRotation = currentY;
            
            // Map the turn speed to our bank limit
            float targetBank = Mathf.Clamp(turnSpeed * 5f, -bankAmount, bankAmount);
            currentBank = Mathf.Lerp(currentBank, targetBank, Time.deltaTime * bankSpeed);

            // Apply the Z-axis roll
            transform.localRotation = Quaternion.Euler(transform.localEulerAngles.x, transform.localEulerAngles.y, -currentBank);
        }
    }
}