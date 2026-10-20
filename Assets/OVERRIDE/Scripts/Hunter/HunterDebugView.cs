// HunterDebugView.cs
// The Seeker's F1 debug view (GDD Section 10):
//   - the belief heatmap on the floor: one texture pixel per cell, dark = unlikely, red = likely;
//     purple tint = crouch-only cells (vents) where the player can hide but the Seeker can't follow
//   - the planned path as a line, and the goal cell as a ring
//   - a label above the Seeker: intent, goal probability mass, and how long the last tick took
// The heatmap is ONE quad and ONE texture (one draw call), updated only while F1 is on.

using Override.Core;
using UnityEngine;

namespace Override.Hunter
{
    [RequireComponent(typeof(HunterBody))]
    public class HunterDebugView : MonoBehaviour
    {
        [SerializeField] float heatmapLift = 0.05f;   // metres above the floor, so it doesn't flicker
        [SerializeField] Color cold = new Color(0.05f, 0.05f, 0.2f, 0.25f);
        [SerializeField] Color hot = new Color(1f, 0.1f, 0.05f, 0.8f);
        [SerializeField] Color ventTint = new Color(0.5f, 0.1f, 0.6f, 0.35f);

        HunterBody seeker;
        GameObject quad;
        Texture2D texture;
        Color32[] pixels;
        LineRenderer pathLine;
        Material material;

        void Start()
        {
            seeker = GetComponent<HunterBody>();
            var grid = seeker.Brain.Grid;
            material = new Material(Shader.Find("Sprites/Default"));   // unlit, uses texture alpha

            texture = new Texture2D(grid.Width, grid.Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,     // crisp cells, no blur between them
                wrapMode = TextureWrapMode.Clamp,
            };
            pixels = new Color32[grid.Width * grid.Height];

            // A quad lying flat over the whole grid. Rotating +90° about X turns its front face up and
            // maps texture u to world +X and v to world +Z, so pixel (x, z) sits over cell (x, z).
            quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Seeker Belief Heatmap (F1)";
            Destroy(quad.GetComponent<Collider>());
            float w = grid.Width * grid.CellSize, h = grid.Height * grid.CellSize;
            quad.transform.SetPositionAndRotation(
                new Vector3(grid.Origin.x + w / 2f, seeker.FloorTop + heatmapLift, grid.Origin.y + h / 2f),
                Quaternion.Euler(90f, 0f, 0f));
            quad.transform.localScale = new Vector3(w, h, 1f);
            var r = quad.GetComponent<MeshRenderer>();
            r.sharedMaterial = new Material(material) { mainTexture = texture };   // its own copy, with the heatmap
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            var lineGo = new GameObject("Seeker Path (F1)");
            pathLine = lineGo.AddComponent<LineRenderer>();
            pathLine.sharedMaterial = material;
            pathLine.widthMultiplier = 0.12f;
            pathLine.startColor = pathLine.endColor = new Color(1f, 0.85f, 0.2f);
            pathLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Show(DebugOverlay.Visible);
            DebugOverlay.Changed += Show;
        }

        void OnDestroy()
        {
            DebugOverlay.Changed -= Show;
            if (quad != null) Destroy(quad);
            if (pathLine != null) Destroy(pathLine.gameObject);
        }

        void OnDisable() => Show(false);   // e.g. the Seeker was destroyed: hide its view
        void OnEnable() { if (quad != null) Show(DebugOverlay.Visible); }

        void Show(bool on)
        {
            if (quad != null) quad.SetActive(on && isActiveAndEnabled);
            if (pathLine != null) pathLine.gameObject.SetActive(on && isActiveAndEnabled);
        }

        void LateUpdate()
        {
            if (!DebugOverlay.Visible || quad == null) return;
            PaintBelief();
            DrawPath();
        }

        void PaintBelief()
        {
            var grid = seeker.Brain.Grid;
            var belief = seeker.Brain.Belief;
            float max = Mathf.Max(belief.Max, 1e-9f);
            for (int i = 0; i < pixels.Length; i++)
            {
                if (!grid.PlayerWalkable[i]) { pixels[i] = default; continue; }   // transparent: walls, outside
                // Square root stretches the low end, so faint probability is still visible.
                Color c = Color.Lerp(cold, hot, Mathf.Sqrt(belief[i] / max));
                if (!grid.HunterWalkable[i]) c = Color.Lerp(c, ventTint, 0.5f);
                pixels[i] = c;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        void DrawPath()
        {
            var path = seeker.CurrentPath;
            int n = path != null ? path.Length : 0;
            pathLine.positionCount = n + 1;
            pathLine.SetPosition(0, transform.position);
            for (int i = 0; i < n; i++) pathLine.SetPosition(i + 1, path[i] + Vector3.up * 0.3f);
        }

        void OnGUI()
        {
            if (!DebugOverlay.Visible || seeker == null || Camera.main == null) return;
            Vector3 screen = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 1.2f);
            if (screen.z < 0f) return;   // behind the camera
            var brain = seeker.Brain;
            string text = $"SEEKER: {brain.Intent}\ngoal mass {brain.GoalMass:0.00}  peak {brain.Belief.Max:0.000}\n" +
                          $"tick {seeker.LastTickMilliseconds:0.00} ms  A* {brain.CellsExpandedLastPlan} cells";
            GUI.Box(new Rect(screen.x - 110f, Screen.height - screen.y - 60f, 220f, 52f), text);
        }

        void OnDrawGizmos()
        {
            if (!Application.isPlaying || seeker == null || seeker.Brain == null) return;
            int goal = seeker.Brain.GoalCell;
            if (goal < 0) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(seeker.Brain.Grid.Centre(goal) + Vector3.up * 0.3f, seeker.Brain.Grid.CellSize * 0.5f);
        }
    }
}
