// MenuBackground.cs
// An animated backdrop for the main menu, drawn with UI Toolkit's Painter2D (vector drawing: no textures,
// no video, no extra assets). It's inserted BEHIND Shajeeve's start-screen UI at runtime, so his UXML and
// USS files stay untouched. Palette from GDD Section 9: dark greys, facility teal, ARGUS red.
//
//   - a perspective floor grid scrolling towards the viewer (moving through the facility)
//   - a scan line sweeping down the screen (ARGUS watching)
//   - a pulsing red ring with rotating segments behind the title (ARGUS's eye)
//   - slowly rising dust specks
//
// Put it on any object in the MainMenu scene.

using UnityEngine;
using UnityEngine.UIElements;

namespace Override.UI
{
    public class MenuBackground : MonoBehaviour
    {
        [SerializeField] Color gridColour = new Color(0f, 0.78f, 0.74f, 0.18f);
        [SerializeField] Color scanColour = new Color(0f, 0.85f, 0.8f, 0.25f);
        [SerializeField] Color eyeColour = new Color(0.86f, 0.16f, 0.16f, 0.5f);
        [SerializeField] Color dustColour = new Color(0.7f, 0.8f, 0.85f, 0.35f);
        [SerializeField] float gridSpeed = 0.25f;     // grid rows per second
        [SerializeField] int dustCount = 60;

        Backdrop backdrop;

        void Start()
        {
            var document = FindFirstObjectByType<UIDocument>();
            if (document == null) { Debug.LogWarning("[MenuBackground] No UIDocument in the scene."); return; }

            var root = document.rootVisualElement;
            var host = root.Q(className: "root") ?? root;   // Shajeeve's full-screen root, if present
            backdrop = new Backdrop(this);
            host.Insert(0, backdrop);                        // first child = drawn underneath the menu
            backdrop.schedule.Execute(backdrop.MarkDirtyRepaint).Every(16);   // about 60 redraws a second
        }

        /// The drawing element. Fills its parent and ignores the mouse, so the buttons still work.
        sealed class Backdrop : VisualElement
        {
            readonly MenuBackground owner;
            readonly Vector2[] dust;
            readonly float[] dustSpeed;

            public Backdrop(MenuBackground owner)
            {
                this.owner = owner;
                pickingMode = PickingMode.Ignore;
                style.position = Position.Absolute;
                style.left = style.right = style.top = style.bottom = 0;

                var rng = new System.Random(7);
                dust = new Vector2[owner.dustCount];
                dustSpeed = new float[owner.dustCount];
                for (int i = 0; i < dust.Length; i++)
                {
                    dust[i] = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());   // 0-1 of the screen
                    dustSpeed[i] = 0.01f + (float)rng.NextDouble() * 0.03f;
                }
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext mgc)
            {
                Rect r = contentRect;
                if (r.width < 1f || r.height < 1f) return;
                var p = mgc.painter2D;
                float t = Time.unscaledTime;

                DrawGrid(p, r, t);
                DrawScanLine(p, r, t);
                DrawEye(p, r, t);
                DrawDust(p, r, t);
            }

            void DrawGrid(Painter2D p, Rect r, float t)
            {
                float horizon = r.height * 0.62f;
                var vanish = new Vector2(r.width * 0.5f, horizon);
                p.strokeColor = owner.gridColour;
                p.lineWidth = 1f;

                // Lines running into the distance, all meeting at the vanishing point.
                p.BeginPath();
                for (int i = -12; i <= 12; i++)
                {
                    p.MoveTo(vanish);
                    p.LineTo(new Vector2(r.width * 0.5f + i * r.width * 0.12f, r.height));
                }
                p.Stroke();

                // Cross lines: evenly spaced in depth, so they bunch up near the horizon (perspective),
                // and scroll towards the viewer.
                float scroll = (t * owner.gridSpeed) % 1f;
                p.BeginPath();
                for (int k = 0; k < 14; k++)
                {
                    float depth = 1f / (k + 1 - scroll + 0.5f);          // 1/z: near rows far apart
                    float y = horizon + (r.height - horizon) * Mathf.Clamp01(depth * 1.2f);
                    p.MoveTo(new Vector2(0f, y));
                    p.LineTo(new Vector2(r.width, y));
                }
                p.Stroke();
            }

            void DrawScanLine(Painter2D p, Rect r, float t)
            {
                float y = (t * 0.12f % 1f) * r.height;
                for (int band = 0; band < 6; band++)   // a soft trail above the bright line
                {
                    var c = owner.scanColour;
                    c.a *= 1f - band / 6f;
                    p.strokeColor = c;
                    p.lineWidth = 2f;
                    p.BeginPath();
                    p.MoveTo(new Vector2(0f, y - band * 3f));
                    p.LineTo(new Vector2(r.width, y - band * 3f));
                    p.Stroke();
                }
            }

            void DrawEye(Painter2D p, Rect r, float t)
            {
                var centre = new Vector2(r.width * 0.5f, r.height * 0.36f);
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 2.2f);

                // Two soft rings that breathe.
                for (int ring = 0; ring < 2; ring++)
                {
                    var c = owner.eyeColour;
                    c.a *= (ring == 0 ? 0.6f : 0.3f) * (0.6f + 0.4f * pulse);
                    p.strokeColor = c;
                    p.lineWidth = ring == 0 ? 2f : 1f;
                    p.BeginPath();
                    p.Arc(centre, 170f + ring * 40f + pulse * 8f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.Stroke();
                }

                // Four arc segments slowly rotating around it, like a scanning sensor.
                p.strokeColor = owner.eyeColour;
                p.lineWidth = 3f;
                float spin = t * 25f;
                for (int s = 0; s < 4; s++)
                {
                    float a = spin + s * 90f;
                    p.BeginPath();
                    p.Arc(centre, 150f, Angle.Degrees(a), Angle.Degrees(a + 35f));
                    p.Stroke();
                }
            }

            void DrawDust(Painter2D p, Rect r, float t)
            {
                p.fillColor = owner.dustColour;
                for (int i = 0; i < dust.Length; i++)
                {
                    float x = dust[i].x * r.width + Mathf.Sin(t * 0.5f + i) * 6f;
                    float y = (1f - (dust[i].y + t * dustSpeed[i]) % 1f) * r.height;   // rises, wraps around
                    p.BeginPath();
                    p.Arc(new Vector2(x, y), 1.2f + (i % 3) * 0.6f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.Fill();
                }
            }
        }
    }
}
