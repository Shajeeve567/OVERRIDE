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

        [Header("Animation Culling")]
        public float cullDistance = 30f;

        private Vector3 startPos;
        private float currentBank = 0f;
        private float lastYRotation;

        private Transform player;
        private MeshRenderer[] meshRenderers;
        private bool isCulled;

        private void Awake()
        {
            meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
            EnsureWispIsHittable();
        }

        private void EnsureWispIsHittable()
        {
            AgentMotor motor = GetComponentInParent<AgentMotor>();
            GameObject wispRoot = motor != null ? motor.gameObject : transform.root.gameObject;

            if (wispRoot.GetComponent<Collider>() == null)
            {
                wispRoot.AddComponent<CapsuleCollider>();
            }

            if (wispRoot.GetComponent<WispHealth>() == null)
            {
                wispRoot.AddComponent<WispHealth>();
            }
        }

        public void SetVisible(bool visible)
        {
            isCulled = !visible;
            SetRenderersVisible(visible);
        }

        private void Start()
        {
            startPos = transform.localPosition;
            lastYRotation = transform.eulerAngles.y;
            FindPlayer();
        }

        private void Update()
        {
            if (player == null) FindPlayer();

            if (player != null)
            {
                float sqrDistance = (player.position - transform.position).sqrMagnitude;
                bool shouldCull = sqrDistance > cullDistance * cullDistance;

                if (shouldCull != isCulled)
                {
                    isCulled = shouldCull;
                    SetRenderersVisible(!isCulled);
                }

                if (isCulled) return;
            }

            float newY = startPos.y + Mathf.Sin(Time.time * hoverFrequency) * hoverAmplitude;
            transform.localPosition = new Vector3(startPos.x, newY, startPos.z);

            float currentY = transform.eulerAngles.y;
            float turnSpeed = Mathf.DeltaAngle(lastYRotation, currentY);
            lastYRotation = currentY;

            float targetBank = Mathf.Clamp(turnSpeed * 5f, -bankAmount, bankAmount);
            currentBank = Mathf.Lerp(currentBank, targetBank, Time.deltaTime * bankSpeed);

            transform.localRotation = Quaternion.Euler(transform.localEulerAngles.x, transform.localEulerAngles.y, -currentBank);
        }

        private void FindPlayer()
        {
            GameObject playerObject = GameObject.FindWithTag("Player");
            if (playerObject != null) player = playerObject.transform;
        }

        private void SetRenderersVisible(bool visible)
        {
            if (meshRenderers == null) return;

            foreach (MeshRenderer meshRenderer in meshRenderers)
            {
                if (meshRenderer != null) meshRenderer.enabled = visible;
            }
        }
    }
}
