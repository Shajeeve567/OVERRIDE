// ProgressRing.cs
// A world-space ring drawn with a LineRenderer that fills clockwise from 0 to 1 and always faces
// the camera. Used for the terminal hack (GDD: "world-space progress ring"). Needs no UI canvas.

using UnityEngine;

namespace Override.Interaction
{
    [RequireComponent(typeof(LineRenderer))]
    public class ProgressRing : MonoBehaviour
    {
        [SerializeField] float radius = 0.35f;
        [SerializeField] float width = 0.05f;
        [SerializeField] int segments = 64;     // points on a full circle

        LineRenderer line;
        Transform cam;
        float shownProgress = -1f;

        void Awake()
        {
            line = GetComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = false;
            line.widthMultiplier = width;
            line.positionCount = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            // Sprites/Default uses the line's colour and needs no lighting.
            if (line.sharedMaterial == null) line.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        }

        public void SetVisible(bool visible)
        {
            if (line.enabled != visible) line.enabled = visible;
        }

        public void SetColour(Color c)
        {
            line.startColor = c;
            line.endColor = c;
        }

        public void SetProgress(float t)
        {
            t = Mathf.Clamp01(t);
            if (Mathf.Approximately(t, shownProgress)) return;   // only rebuild when it changes
            shownProgress = t;

            int count = Mathf.Max(2, Mathf.CeilToInt(segments * t) + 1);
            line.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                // Start at 12 o'clock and go clockwise.
                float angle = Mathf.PI / 2f - (2f * Mathf.PI * t) * i / (count - 1);
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }
        }

        void LateUpdate()
        {
            if (!line.enabled) return;
            if (cam == null && Camera.main != null) cam = Camera.main.transform;
            // Face the camera (+Z toward it) so the fill reads clockwise from the player's view.
            if (cam != null) transform.rotation = Quaternion.LookRotation(cam.position - transform.position);
        }
    }
}
