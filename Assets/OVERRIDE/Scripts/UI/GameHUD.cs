// GameHUD.cs
// The in-game HUD (GDD Section 10), built in code with UI Toolkit in the same look as the main menu:
// dark panels, facility teal, ARGUS red. It replaces the temporary IMGUI overlays (CombatOverlay,
// ObjectivesOverlay), which it switches off when it starts.
//
//   bottom left    health bar
//   bottom right   ammo, reload state
//   centre         crosshair (dot while aiming), hit / weak-point / kill marker, interaction prompt
//   lower centre   hack progress while hacking (red while paused by damage)
//   top right      objectives, alarm meter, final wave
//   top centre     messages: ARGUS announcements and objective updates
//   whole screen   red damage flash; pulsing red edges + "TARGET ACQUIRED" while a camera sees you
//
// Text is only rebuilt when the number behind it changes, so the HUD allocates nothing per frame.

using Override.Combat;
using Override.Core;
using Override.Director;
using Override.Hunter;
using Override.Integration;
using Override.Interaction;
using UnityEngine;
using UnityEngine.UIElements;

namespace Override.UI
{
    public class GameHUD : MonoBehaviour
    {
        [SerializeField] PanelSettings panelSettings;   // Assets/UI/OverridePanelSettings

        static readonly Color Teal = new Color(0f, 0.85f, 0.8f);
        static readonly Color Red = new Color(0.86f, 0.18f, 0.16f);
        static readonly Color TextColour = new Color(0.86f, 0.9f, 0.92f);
        static readonly Color Dim = new Color(0.55f, 0.6f, 0.64f);
        static readonly Color PanelColour = new Color(0.08f, 0.1f, 0.12f, 0.75f);

        HitscanRifle rifle;
        Health health;
        PlayerInteractor interactor;
        DirectorController director;
        HackTerminal[] terminals;

        VisualElement healthFill, alarmFill, hackBar, hackFill, damageFlash, spottedFrame, crossTicks, aimDot, marker;
        Label healthText, ammoText, reloadText, prompt, hackText, message, spottedText, finalWave;
        Label[] terminalRows;

        int shownHealth = -1, shownMag = -1, shownReserve = -1, shownHackKey = -1;
        int[] shownRowKeys;
        string shownPrompt;
        float markerUntil, flashUntil, spottedUntil, messageUntil;

        void Start()
        {
            var p = GameObject.FindWithTag("Player");
            if (p != null)
            {
                rifle = p.GetComponent<HitscanRifle>();
                health = p.GetComponent<Health>();
                interactor = p.GetComponent<PlayerInteractor>();
                if (interactor != null) interactor.ShowPrompt = false;     // the HUD shows it now
                var old = p.GetComponent<CombatOverlay>();
                if (old != null) old.enabled = false;
            }
            var objectives = FindFirstObjectByType<ObjectivesOverlay>();
            if (objectives != null) objectives.enabled = false;

            director = FindFirstObjectByType<DirectorController>();
            terminals = FindObjectsByType<HackTerminal>(FindObjectsSortMode.None);
            System.Array.Sort(terminals, (a, b) => a.Zone.CompareTo(b.Zone));

            Build();
            if (rifle != null) rifle.Fired += OnFired;
            if (director != null) director.Announced += Show;
            Show("Find the terminals and hold E to override them.");
        }

        void OnEnable()
        {
            GameEvents.PlayerDamaged += OnDamaged;
            GameEvents.TerminalOverridden += OnTerminal;
            CameraNetwork.Sighted += OnSpotted;
        }

        void OnDisable()
        {
            GameEvents.PlayerDamaged -= OnDamaged;
            GameEvents.TerminalOverridden -= OnTerminal;
            CameraNetwork.Sighted -= OnSpotted;
            if (rifle != null) rifle.Fired -= OnFired;
            if (director != null) director.Announced -= Show;
        }

        // ---------------------------------------------------------------- events

        void OnFired(ShotResult shot)
        {
            if (!shot.HitTarget) return;
            Color c = shot.Kill ? Red : shot.WeakPoint ? new Color(1f, 0.85f, 0.2f) : Color.white;
            foreach (var bar in marker.Children()) bar.style.backgroundColor = c;
            marker.style.scale = new Scale(Vector3.one * (shot.Kill ? 1.5f : 1f));
            markerUntil = Time.time + (shot.Kill ? 0.4f : 0.15f);
        }

        void OnDamaged(float amount) => flashUntil = Time.time + 0.3f;
        void OnSpotted(Vector3 at) => spottedUntil = Time.time + 0.8f;

        void OnTerminal(ZoneId zone)
        {
            if (zone == ZoneId.Core) return;   // the victory screen takes over
            Show(HackTerminal.TerminalsDone >= 3
                ? "All three terminals overridden: the Core is open. Hold E at the Core console for 30 s."
                : $"{zone} terminal overridden ({HackTerminal.TerminalsDone}/3).");
        }

        void Show(string text)
        {
            message.text = text;
            messageUntil = Time.time + 5f;
        }

        // ---------------------------------------------------------------- per frame

        void Update()
        {
            if (healthFill == null) return;
            float t = Time.time;

            // Health
            if (health != null)
            {
                int hp = Mathf.CeilToInt(health.Current);
                if (hp != shownHealth)
                {
                    shownHealth = hp;
                    healthText.text = "HEALTH  " + hp;
                    float f = health.Fraction;
                    healthFill.style.width = Length.Percent(f * 100f);
                    healthFill.style.backgroundColor = Color.Lerp(Red, Teal, f);
                }
            }

            // Ammo
            if (rifle != null)
            {
                if (rifle.Magazine != shownMag || rifle.Reserve != shownReserve)
                {
                    shownMag = rifle.Magazine;
                    shownReserve = rifle.Reserve;
                    ammoText.text = shownMag + " / " + shownReserve;
                    ammoText.style.color = shownMag <= 5 ? Red : TextColour;
                }
                reloadText.style.display = rifle.IsReloading ? DisplayStyle.Flex : DisplayStyle.None;
                bool aiming = rifle.IsAiming;
                crossTicks.style.display = aiming ? DisplayStyle.None : DisplayStyle.Flex;
                aimDot.style.display = aiming ? DisplayStyle.Flex : DisplayStyle.None;
            }

            marker.style.display = t < markerUntil ? DisplayStyle.Flex : DisplayStyle.None;
            damageFlash.style.opacity = Mathf.Clamp01((flashUntil - t) / 0.3f) * 0.35f;

            bool spotted = t < spottedUntil;
            spottedFrame.style.display = spotted ? DisplayStyle.Flex : DisplayStyle.None;
            spottedText.style.display = spotted ? DisplayStyle.Flex : DisplayStyle.None;
            if (spotted) spottedFrame.style.opacity = 0.5f + 0.4f * Mathf.Sin(t * 12f);

            // Interaction prompt
            string want = null;
            if (interactor != null && interactor.LookedAt != null && interactor.LookedAt.CanInteract && !interactor.IsHolding)
                want = "[E]  " + interactor.LookedAt.Prompt;
            if (want != shownPrompt)
            {
                shownPrompt = want;
                prompt.text = want ?? "";
                prompt.style.display = want != null ? DisplayStyle.Flex : DisplayStyle.None;
            }

            UpdateHack();
            UpdateObjectives();
            message.style.display = t < messageUntil ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void UpdateHack()
        {
            HackTerminal active = null;
            foreach (var term in terminals) if (term.IsHacking) { active = term; break; }
            hackBar.style.display = active != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (active == null) return;
            hackFill.style.width = Length.Percent(active.Progress * 100f);
            hackFill.style.backgroundColor = active.IsPausedByDamage ? Red : Teal;
            int key = (int)active.Zone * 2 + (active.IsPausedByDamage ? 1 : 0);
            if (key != shownHackKey)
            {
                shownHackKey = key;
                hackText.text = active.IsPausedByDamage ? "HACK PAUSED: TAKING FIRE" : "OVERRIDING " + active.Zone.ToString().ToUpperInvariant();
            }
        }

        void UpdateObjectives()
        {
            for (int i = 0; i < terminals.Length; i++)
            {
                var term = terminals[i];
                int key = term.IsOverridden ? 1000 : term.IsLockedByPrerequisites ? 999 : Mathf.RoundToInt(term.Progress * 100f);
                if (key == shownRowKeys[i]) continue;   // unchanged: no new string
                shownRowKeys[i] = key;
                string state = term.IsOverridden ? "DONE" : term.IsLockedByPrerequisites ? "LOCKED"
                             : term.Progress > 0f ? Mathf.RoundToInt(term.Progress * 100f) + "%" : "";
                string line = (term.Zone == ZoneId.Core ? "CORE OVERRIDE" : term.Zone.ToString().ToUpperInvariant() + " TERMINAL")
                              + "   " + state;
                terminalRows[i].text = line;
                terminalRows[i].style.color = term.IsOverridden ? Teal : term.IsLockedByPrerequisites ? Dim : TextColour;
            }
            if (director != null)
            {
                alarmFill.style.width = Length.Percent(director.Alarm);
                finalWave.style.display = director.FinalWave ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // ---------------------------------------------------------------- layout

        void Build()
        {
            var doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = panelSettings;
            doc.sortingOrder = 0;   // below the pause / game-over screens
            var root = doc.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;

            // Whole-screen layers
            damageFlash = Fill(root, Red);
            damageFlash.style.opacity = 0f;
            spottedFrame = Fill(root, Color.clear);
            spottedFrame.style.borderLeftWidth = spottedFrame.style.borderRightWidth =
                spottedFrame.style.borderTopWidth = spottedFrame.style.borderBottomWidth = 14;
            spottedFrame.style.borderLeftColor = spottedFrame.style.borderRightColor =
                spottedFrame.style.borderTopColor = spottedFrame.style.borderBottomColor = Red;
            spottedFrame.style.display = DisplayStyle.None;

            // Health (bottom left)
            var hp = AddBox(root, PanelColour);
            Pin(hp, left: 24, bottom: 24);
            hp.style.width = 300;
            healthText = AddLabel(hp, "HEALTH", 14, TextColour, true);
            var hpBar = Bar(hp, 280, 10, out healthFill);
            hpBar.style.marginTop = 6;

            // Ammo (bottom right)
            var ammo = AddBox(root, PanelColour);
            Pin(ammo, right: 24, bottom: 24);
            ammo.style.alignItems = Align.FlexEnd;
            ammoText = AddLabel(ammo, "30 / 90", 30, TextColour, true);
            reloadText = AddLabel(ammo, "RELOADING", 12, Teal, true);
            reloadText.style.display = DisplayStyle.None;

            // Crosshair, aim dot, hit marker, prompt (centre)
            var centre = new VisualElement { pickingMode = PickingMode.Ignore };
            Pin(centre, left: 0, right: 0, top: 0, bottom: 0);
            centre.style.alignItems = Align.Center;
            centre.style.justifyContent = Justify.Center;
            root.Add(centre);
            var reticle = new VisualElement { pickingMode = PickingMode.Ignore };
            reticle.style.width = reticle.style.height = 40;
            centre.Add(reticle);
            crossTicks = new VisualElement { pickingMode = PickingMode.Ignore };
            Pin(crossTicks, left: 0, top: 0, right: 0, bottom: 0);
            reticle.Add(crossTicks);
            Tick(crossTicks, 4, 19, 10, 2); Tick(crossTicks, 26, 19, 10, 2);
            Tick(crossTicks, 19, 4, 2, 10); Tick(crossTicks, 19, 26, 2, 10);
            aimDot = Tick(reticle, 18.5f, 18.5f, 3, 3);
            aimDot.style.display = DisplayStyle.None;
            marker = new VisualElement { pickingMode = PickingMode.Ignore };
            Pin(marker, left: 0, top: 0, right: 0, bottom: 0);
            reticle.Add(marker);
            foreach (float angle in new[] { 45f, -45f })
            {
                var bar = Tick(marker, 8, 19, 24, 2);
                bar.style.rotate = new Rotate(Angle.Degrees(angle));
            }
            marker.style.display = DisplayStyle.None;
            prompt = AddLabel(centre, "", 15, TextColour, true);
            prompt.style.marginTop = 30;
            prompt.style.backgroundColor = PanelColour;
            prompt.style.paddingLeft = prompt.style.paddingRight = 10;
            prompt.style.paddingTop = prompt.style.paddingBottom = 4;
            prompt.style.display = DisplayStyle.None;

            // Hack progress (lower centre)
            hackBar = AddBox(root, PanelColour);
            Pin(hackBar, bottom: 120);
            hackBar.style.alignSelf = Align.Center;
            hackBar.style.left = Length.Percent(50);
            hackBar.style.translate = new Translate(Length.Percent(-50), 0);
            hackBar.style.width = 420;
            hackBar.style.alignItems = Align.Center;
            hackText = AddLabel(hackBar, "OVERRIDING", 13, Teal, true);
            Bar(hackBar, 400, 12, out hackFill).style.marginTop = 6;
            hackBar.style.display = DisplayStyle.None;

            // Objectives + alarm (top right)
            var obj = AddBox(root, PanelColour);
            Pin(obj, right: 24, top: 24);
            obj.style.width = 280;
            AddLabel(obj, "OBJECTIVES", 12, Dim, true).style.marginBottom = 6;
            terminalRows = new Label[terminals.Length];
            shownRowKeys = new int[terminals.Length];
            for (int i = 0; i < shownRowKeys.Length; i++) shownRowKeys[i] = -1;
            for (int i = 0; i < terminals.Length; i++) terminalRows[i] = AddLabel(obj, "", 13, TextColour, false);
            AddLabel(obj, "ALARM", 12, Dim, true).style.marginTop = 10;
            Bar(obj, 260, 6, out alarmFill).style.marginTop = 4;
            alarmFill.style.backgroundColor = Red;
            finalWave = AddLabel(obj, ">> FINAL WAVE <<", 14, Red, true);
            finalWave.style.marginTop = 8;
            finalWave.style.display = DisplayStyle.None;

            // Messages + spotted (top centre)
            var top = new VisualElement { pickingMode = PickingMode.Ignore };
            Pin(top, left: 0, right: 0, top: 32);
            top.style.alignItems = Align.Center;
            root.Add(top);
            message = AddLabel(top, "", 16, TextColour, true);
            message.style.backgroundColor = PanelColour;
            message.style.paddingLeft = message.style.paddingRight = 16;
            message.style.paddingTop = message.style.paddingBottom = 6;
            spottedText = AddLabel(top, "ARGUS: TARGET ACQUIRED", 18, Red, true);
            spottedText.style.marginTop = 10;
            spottedText.style.letterSpacing = 4;
            spottedText.style.display = DisplayStyle.None;
        }

        // ---------------------------------------------------------------- small builders

        static VisualElement Fill(VisualElement parent, Color colour)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            Pin(e, left: 0, right: 0, top: 0, bottom: 0);
            e.style.backgroundColor = colour;
            parent.Add(e);
            return e;
        }

        static VisualElement AddBox(VisualElement parent, Color colour)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            e.style.backgroundColor = colour;
            e.style.paddingLeft = e.style.paddingRight = 12;
            e.style.paddingTop = e.style.paddingBottom = 8;
            e.style.borderLeftWidth = 2;
            e.style.borderLeftColor = Teal;
            parent.Add(e);
            return e;
        }

        static void Pin(VisualElement e, float? left = null, float? right = null, float? top = null, float? bottom = null)
        {
            e.style.position = Position.Absolute;
            if (left.HasValue) e.style.left = left.Value;
            if (right.HasValue) e.style.right = right.Value;
            if (top.HasValue) e.style.top = top.Value;
            if (bottom.HasValue) e.style.bottom = bottom.Value;
        }

        static Label AddLabel(VisualElement parent, string text, int size, Color colour, bool bold)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.style.fontSize = size;
            l.style.color = colour;
            l.style.letterSpacing = 1;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            parent.Add(l);
            return l;
        }

        static VisualElement Bar(VisualElement parent, float width, float height, out VisualElement fill)
        {
            var back = new VisualElement { pickingMode = PickingMode.Ignore };
            back.style.width = width;
            back.style.height = height;
            back.style.backgroundColor = new Color(1f, 1f, 1f, 0.08f);
            fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.style.height = Length.Percent(100);
            fill.style.width = Length.Percent(100);
            fill.style.backgroundColor = Teal;
            back.Add(fill);
            parent.Add(back);
            return back;
        }

        static VisualElement Tick(VisualElement parent, float x, float y, float w, float h)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            Pin(e, left: x, top: y);
            e.style.width = w;
            e.style.height = h;
            e.style.backgroundColor = new Color(1f, 1f, 1f, 0.9f);
            parent.Add(e);
            return e;
        }
    }
}
