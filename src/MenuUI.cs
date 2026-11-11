using System;
using System.Collections.Generic;
using CatlikeCoding.TextBox;
using SmartInput;
using UnityEngine;

namespace OriVR
{
    // One setting row: a label on the left, an optional value on the right, an explanation for the bottom of the menu,
    // and what left/right (Change, returns false at a limit) or A (Press) does.
    internal class MenuRow
    {
        internal string Label;
        internal Func<string> Value;
        internal Func<string> Tip;
        internal Func<int, bool> Change;
        internal Action Press;
    }

    internal class MenuTab
    {
        internal string Name;
        internal List<MenuRow> Rows = new List<MenuRow>();
    }

    // The in-headset settings menu, laid out like mockup A (2026-11-11): VR SETTINGS, tabs across the top switched with
    // LB/RB, rows with the label on the left and the value on the right, the explanation of the selected row, button hints
    // and a readout line at the bottom, version in the corner. Click both sticks (or press Insert) to open or close it.
    // Drawn with the game's own text system on the HUD layer, so both eyes see it at stage distance. The game is paused and
    // gets no input while it is open.
    internal class MenuUI : MonoBehaviour
    {
        const int HudLayer = 8;
        const int MaxRows = 6;
        static readonly Color Gold = new Color(1f, 0.82f, 0.45f, 1f);
        static readonly Color Dim = new Color(0.75f, 0.78f, 0.8f, 1f);
        static readonly Color Title = new Color(0.72f, 0.9f, 1f, 1f);
        static readonly Color Faint = new Color(0.55f, 0.6f, 0.64f, 1f);

        // Layout, in the menu's own units (the panel is 11.2 wide and 7.4 tall).
        const float TitleY = 3.0f, TabY = 2.32f, TabGap = 2.7f, LineY = 1.98f, FirstRowY = 1.5f, RowStep = 0.5f;
        const float LabelX = -4.2f, ValueX = 4.2f, TipY = -1.62f, Line2Y = -2.3f, HintY = -2.6f, ReadoutY = -2.94f;

        internal XrRig Rig;
        internal MenuSound Sound;
        internal List<MenuTab> Tabs = new List<MenuTab>();
        internal Func<string> Problem;     // why VR did not start, or null
        internal Func<string> Readout;     // the small diagnostic line at the bottom
        internal string Version = "";
        internal Action Closed;
        internal KeyCode OpenKey = KeyCode.Insert;
        internal float Size = 1f, Height;  // placement, from the Menu tab

        // PlayStation pad flags from the native DLL (same values as PadBit there).
        const int PsUp = 1, PsDown = 2, PsLeft = 4, PsRight = 8, PsCross = 16, PsCircle = 32, PsL3 = 256, PsR3 = 512,
                  PsL1 = 8192, PsR1 = 16384, PsL2 = 32768, PsR2 = 65536, PsConnected = 1 << 30;
        enum Device { Xbox, PlayStation, Keyboard }
        Device lastDevice = Device.Xbox;
        Device shownHintFor = (Device)(-1);
        int lastPadKind = -1;
        float nextPadCheck;

        internal bool IsOpen { get; private set; }

        GameObject root;
        Shader shader;
        TextBox titleBox, versionBox, tipBox, hintBox, readoutBox, leftTabKey, rightTabKey;
        TextBox[] tabBoxes = new TextBox[0];
        TextBox[] labelBoxes = new TextBox[MaxRows], valueBoxes = new TextBox[MaxRows];
        string[] shownLabel = new string[MaxRows], shownValue = new string[MaxRows];
        Transform bar, tabBar, arrowLeft, arrowRight;
        string shownTip, shownReadout;
        bool shownProblem;
        int shownTab = -1, shownRow = -1;
        float nextRefresh;
        int tab;
        int[] selectedPerTab = new int[0];
        readonly List<Renderer> hiddenHud = new List<Renderer>();
        bool bothWas, waitRelease, tabLeftWas, tabRightWas;
        float savedScale = 1f;
        float nextRepeat;
        int heldX, heldY;
        bool aWas, bWas;

        int Selected { get { return selectedPerTab.Length > tab ? selectedPerTab[tab] : 0; } set { if (selectedPerTab.Length > tab) selectedPerTab[tab] = value; } }
        List<MenuRow> Rows { get { return Tabs.Count > 0 ? Tabs[tab].Rows : new List<MenuRow>(); } }

        internal void Open()
        {
            if (IsOpen || waitRelease) return;
            if (root == null && !Build()) return;
            ApplyPlacement();
            root.SetActive(true);
            HideGameHud();
            IsOpen = true;
            PadBridge.BlockGame = true;
            ForgetShown();
            savedScale = Time.timeScale;
            Time.timeScale = 0f;
            aWas = bWas = true;  // ignore presses already held
            tabLeftWas = tabRightWas = true;
            Refresh();
            if (Sound != null) Sound.Open();
            Plugin.Log.LogInfo("MENU open, tab " + Tabs[tab].Name);
        }

        internal void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (root != null) root.SetActive(false);
            RestoreGameHud();
            waitRelease = true;  // the game stays paused until buttons are up, so it does not see them
            if (Sound != null) Sound.Close();
            Plugin.Log.LogInfo("MENU close");
            if (Closed != null) Closed();
        }

        internal void ApplyPlacement()
        {
            if (root == null) return;
            root.transform.position = new Vector3(0f, Height, -3f);  // in front of the game's own HUD
            root.transform.localScale = Vector3.one * Size;
        }

        void ForgetShown()
        {
            for (int i = 0; i < MaxRows; i++) shownLabel[i] = shownValue[i] = null;
            shownTip = shownReadout = null;
            shownTab = shownRow = -1;
            shownHintFor = (Device)(-1);
        }

        bool Build()
        {
            TextStyleCollection style = null;
            foreach (var box in Resources.FindObjectsOfTypeAll<MessageBox>())
            {
                if (box.TextBox != null && box.TextBox.styleCollection != null)
                {
                    style = box.TextBox.styleCollection;
                    break;
                }
            }
            if (style == null)
            {
                var all = Resources.FindObjectsOfTypeAll<TextStyleCollection>();
                if (all.Length > 0) style = all[0];
            }
            if (style == null)
            {
                Plugin.Log.LogWarning("menu: no text style found yet");
                return false;
            }
            Plugin.Log.LogInfo("menu text style: " + style.name);
            if (selectedPerTab.Length != Tabs.Count) selectedPerTab = new int[Tabs.Count];

            root = new GameObject("OriVR menu");
            DontDestroyOnLoad(root);
            MakePanel(root.transform);
            titleBox = MakeText(root.transform, style, 0f, TitleY, 0.82f, AlignmentMode.Center);
            titleBox.color = Title;
            versionBox = MakeText(root.transform, style, 5.0f, 3.38f, 0.3f, AlignmentMode.Right);
            versionBox.color = Faint;
            Show(versionBox, Version);
            tabBoxes = new TextBox[Tabs.Count];
            for (int i = 0; i < Tabs.Count; i++)
                tabBoxes[i] = MakeText(root.transform, style, (i - (Tabs.Count - 1) * 0.5f) * TabGap, TabY, 0.5f, AlignmentMode.Center);
            leftTabKey = MakeText(root.transform, style, -4.6f, TabY, 0.32f, AlignmentMode.Center);
            rightTabKey = MakeText(root.transform, style, 4.6f, TabY, 0.32f, AlignmentMode.Center);
            leftTabKey.color = rightTabKey.color = Faint;
            for (int i = 0; i < MaxRows; i++)
            {
                labelBoxes[i] = MakeText(root.transform, style, LabelX, RowY(i), 0.52f, AlignmentMode.Left);
                valueBoxes[i] = MakeText(root.transform, style, ValueX, RowY(i), 0.52f, AlignmentMode.Right);
            }
            tipBox = MakeText(root.transform, style, 0f, TipY, 0.36f, AlignmentMode.Center);
            tipBox.width = 9.6f;
            tipBox.color = Title;
            hintBox = MakeText(root.transform, style, 0f, HintY, 0.33f, AlignmentMode.Center);
            hintBox.color = Dim;
            readoutBox = MakeText(root.transform, style, 0f, ReadoutY, 0.29f, AlignmentMode.Center);
            readoutBox.color = Faint;
            SetLayer(root);
            root.SetActive(false);
            return true;
        }

        TextBox MakeText(Transform parent, TextStyleCollection style, float x, float y, float size, AlignmentMode align)
        {
            var go = new GameObject("text");
            go.layer = HudLayer;
            go.transform.parent = parent;
            go.transform.localPosition = new Vector3(x, y, 0f);
            go.transform.localScale = Vector3.one;
            var box = go.AddComponent<TextBox>();
            box.alignment = align;
            box.horizontalAnchor = align == AlignmentMode.Left ? HorizontalAnchorMode.Left : align == AlignmentMode.Right ? HorizontalAnchorMode.Right : HorizontalAnchorMode.Center;
            box.verticalAnchor = VerticalAnchorMode.Middle;
            box.width = align == AlignmentMode.Center ? 12f : 6f;
            box.maxHeight = 2f;
            box.size = size;
            box.color = Color.white;
            box.SetStyleCollection(style);
            return box;
        }

        static void Show(TextBox box, string text)
        {
            box.SetText(text ?? "");
            box.RenderText();
            SetLayer(box.gameObject);
        }

        static void SetLayer(GameObject go)
        {
            go.layer = HudLayer;
            foreach (Transform t in go.transform) SetLayer(t.gameObject);
        }

        // A dark panel with soft edges, blue glow bars for the selected tab and row, thin lines, and two small arrows
        // beside the value being changed (drawn, since the game's text system reads < and > as tags).
        void MakePanel(Transform parent)
        {
            string[] names = { "Sprites/Default", "Unlit/Transparent", "UI/Default", "GUI/Text Shader", "Hidden/Internal-GUITexture" };
            foreach (var n in names)
            {
                shader = Shader.Find(n);
                if (shader != null) { Plugin.Log.LogInfo("menu panel shader: " + n); break; }
            }
            if (shader == null) { Plugin.Log.LogWarning("menu: no panel shader, text only"); return; }
            MakeQuad(parent, "panel", 5.6f, 3.7f, 0.5f, SoftBox(128, 128, new Color(0.015f, 0.04f, 0.075f), 0.97f, 0.12f, 0.12f));
            tabBar = MakeQuad(parent, "tab bar", 1.25f, 0.24f, 0.25f, SoftBox(128, 16, new Color(0.2f, 0.5f, 0.85f), 0.45f, 0.5f, 0.5f));
            bar = MakeQuad(parent, "bar", 4.7f, 0.22f, 0.25f, SoftBox(128, 16, new Color(0.2f, 0.5f, 0.85f), 0.55f, 0.5f, 0.5f));
            var soft = SoftBox(128, 4, new Color(0.45f, 0.75f, 1f), 0.8f, 0.5f, 0f);
            MakeQuad(parent, "line", 3.6f, 0.018f, 0.25f, soft).localPosition = new Vector3(0f, LineY, 0.25f);
            MakeQuad(parent, "line 2", 3.6f, 0.012f, 0.25f, SoftBox(128, 4, new Color(0.45f, 0.75f, 1f), 0.35f, 0.5f, 0f)).localPosition = new Vector3(0f, Line2Y, 0.25f);
            arrowLeft = MakeQuad(parent, "arrow left", 0.09f, 0.11f, 0.2f, Triangle(true));
            arrowRight = MakeQuad(parent, "arrow right", 0.09f, 0.11f, 0.2f, Triangle(false));
        }

        Transform MakeQuad(Transform parent, string name, float w, float h, float z, Texture2D tex)
        {
            var go = new GameObject(name);
            go.layer = HudLayer;
            go.transform.parent = parent;
            go.transform.localPosition = new Vector3(0f, 0f, z);
            var mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-w, -h, 0f), new Vector3(-w, h, 0f), new Vector3(w, h, 0f), new Vector3(w, -h, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mat = new Material(shader);
            mat.mainTexture = tex;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        // A texture that fades to clear at its edges. fadeX and fadeY are the fraction of each half used for the fade.
        static Texture2D SoftBox(int w, int h, Color color, float alpha, float fadeX, float fadeY)
        {
            var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float ex = 1f - Mathf.Abs((x + 0.5f) / w * 2f - 1f);
                    float ey = 1f - Mathf.Abs((y + 0.5f) / h * 2f - 1f);
                    float ax = fadeX > 0f ? Mathf.SmoothStep(0f, 1f, ex / fadeX) : 1f;
                    float ay = fadeY > 0f ? Mathf.SmoothStep(0f, 1f, ey / fadeY) : 1f;
                    tex.SetPixel(x, y, new Color(color.r, color.g, color.b, alpha * ax * ay));
                }
            }
            tex.Apply();
            return tex;
        }

        // A small gold triangle pointing left or right, softened at its edge.
        static Texture2D Triangle(bool left)
        {
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.ARGB32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = Mathf.Abs((y + 0.5f) / n * 2f - 1f);
                    float along = left ? u : 1f - u;           // 0 at the point, 1 at the base
                    float inside = along - v;                  // positive inside the triangle
                    float a = Mathf.Clamp01(inside * n * 0.5f);
                    tex.SetPixel(x, y, new Color(Gold.r, Gold.g, Gold.b, a));
                }
            }
            tex.Apply();
            return tex;
        }

        static float RowY(int row) { return FirstRowY - row * RowStep; }

        internal void Refresh()
        {
            if (!IsOpen || Tabs.Count == 0) return;
            var rows = Rows;
            if (Selected >= rows.Count) Selected = Math.Max(0, rows.Count - 1);
            bool tabChanged = shownTab != tab, rowChanged = shownRow != Selected || tabChanged;
            if (tabChanged)
            {
                for (int i = 0; i < tabBoxes.Length; i++)
                {
                    tabBoxes[i].color = i == tab ? Gold : Dim;
                    Show(tabBoxes[i], Tabs[i].Name);
                }
                if (tabBar != null) tabBar.localPosition = new Vector3((tab - (Tabs.Count - 1) * 0.5f) * TabGap, TabY, 0.25f);
                for (int i = 0; i < MaxRows; i++) shownLabel[i] = shownValue[i] = null;
            }
            for (int i = 0; i < MaxRows; i++)
            {
                bool used = i < rows.Count;
                string label = used ? rows[i].Label : "";
                string value = used && rows[i].Value != null ? rows[i].Value() : "";
                Color c = i == Selected ? Gold : Dim;
                if (label != shownLabel[i] || rowChanged)
                {
                    shownLabel[i] = label;
                    labelBoxes[i].color = c;
                    Show(labelBoxes[i], label);
                }
                if (value != shownValue[i] || rowChanged)
                {
                    shownValue[i] = value;
                    valueBoxes[i].color = c;
                    Show(valueBoxes[i], value);
                }
            }
            if (bar != null) bar.localPosition = new Vector3(0f, RowY(Selected), 0.25f);
            PlaceArrows(rows);
            if (shownHintFor != lastDevice && hintBox != null)
            {
                shownHintFor = lastDevice;
                Show(hintBox, lastDevice == Device.PlayStation ? "L1 R1: tabs     Up Down: choose     Left Right: change     Cross: select     Circle: close"
                    : lastDevice == Device.Keyboard ? "Page Up Down: tabs     Arrow keys: choose and change     Enter: select     " + OpenKey + " or Esc: close"
                    : "LB RB: tabs     Up Down: choose     Left Right: change     A: select     B: close");
                Show(leftTabKey, lastDevice == Device.PlayStation ? "L1" : lastDevice == Device.Keyboard ? "PgUp" : "LB");
                Show(rightTabKey, lastDevice == Device.PlayStation ? "R1" : lastDevice == Device.Keyboard ? "PgDn" : "RB");
            }
            var problem = Problem != null ? Problem() : null;
            if ((problem != null) != shownProblem || titleBox.LineCount == 0)
            {
                shownProblem = problem != null;
                Show(titleBox, shownProblem ? "VR DID NOT START" : "VR SETTINGS");
            }
            var tip = problem ?? (rows.Count > 0 && rows[Selected].Tip != null ? rows[Selected].Tip() : "");
            if (tip != shownTip)
            {
                shownTip = tip;
                Show(tipBox, tip);
            }
            var readout = Readout != null ? Readout() : "";
            if (readout != shownReadout)
            {
                shownReadout = readout;
                Show(readoutBox, readout);
            }
            shownTab = tab;
            shownRow = Selected;
        }

        // Arrows either side of the selected value, only on rows that change with left and right.
        void PlaceArrows(List<MenuRow> rows)
        {
            if (arrowLeft == null) return;
            bool show = rows.Count > Selected && rows[Selected].Change != null && valueBoxes[Selected].LineCount > 0;
            arrowLeft.gameObject.SetActive(show);
            arrowRight.gameObject.SetActive(show);
            if (!show) return;
            float width = TextWidth(valueBoxes[Selected]);
            if (width <= 0f || width > 8f) width = 1.2f;
            float y = RowY(Selected);
            arrowRight.localPosition = new Vector3(ValueX + 0.22f, y, 0.2f);
            arrowLeft.localPosition = new Vector3(ValueX - width - 0.22f, y, 0.2f);
        }

        // Width of the drawn text in menu units, from its renderers' bounds.
        float TextWidth(TextBox box)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var r in box.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return any ? b.size.x / Mathf.Max(0.0001f, root.transform.lossyScale.x) : 0f;
        }

        // The game's own HUD (hint messages, spirit and energy orbs) is on the same layer and showed through the menu.
        // While the menu is open the game is paused, so its HUD is hidden and comes back on close.
        void HideGameHud()
        {
            hiddenHud.Clear();
            foreach (var r in FindObjectsOfType<Renderer>())
            {
                if (!r.enabled || r.gameObject.layer != HudLayer || r.transform.IsChildOf(root.transform)) continue;
                r.enabled = false;
                hiddenHud.Add(r);
            }
        }

        void RestoreGameHud()
        {
            foreach (var r in hiddenHud) if (r != null) r.enabled = true;
            hiddenHud.Clear();
        }

        // The menu reads the game's pad functions too; PadBridge lets those reads through while it holds the game off.
        void Update()
        {
            PadBridge.MenuReading = true;
            try { Tick(); }
            finally { PadBridge.MenuReading = false; }
        }

        void Tick()
        {
            int ps = Rig != null ? Rig.PadButtons : 0;
            if (Time.realtimeSinceStartup >= nextPadCheck)
            {
                nextPadCheck = Time.realtimeSinceStartup + 1f;
                int kind = Rig != null ? Rig.PadKind : 0;
                if (kind != lastPadKind)
                {
                    if (lastPadKind >= 0 || kind != 0)
                        Plugin.Log.LogInfo(string.Format("[at {0:F1} s] ", Time.realtimeSinceStartup) + (kind == 0 ? "PAD no PlayStation pad read directly" : "PAD " + (kind == 2 ? "DualSense" : "DualShock 4") + " ready for the mod menu: both sticks open it, Cross selects, Circle closes"));
                    lastPadKind = kind;
                }
            }
            // The game's pad system does not exist yet during boot. With only a PlayStation pad, the game's own joystick
            // mapping would read its buttons in the wrong places, so the menu reads that pad directly instead.
            bool xboxReady = XboxLiveController.Instance != null && !PadBridge.PsOnly;
            bool xboxBoth = xboxReady && XboxControllerInput.GetButton(XboxControllerInput.Button.LeftStick)
                                      && XboxControllerInput.GetButton(XboxControllerInput.Button.RightStick);
            bool psBoth = (ps & PsL3) != 0 && (ps & PsR3) != 0;
            bool both = xboxBoth || psBoth;
            bool key = Input.GetKeyDown(OpenKey);
            if ((both && !bothWas) || key)
            {
                lastDevice = key ? Device.Keyboard : psBoth ? Device.PlayStation : Device.Xbox;
                if (IsOpen) Close();
                else Open();
                bothWas = both;
                return;
            }
            bothWas = both;

            if (waitRelease)
            {
                bool anyHeld = both || (ps & (PsCross | PsCircle | PsL3 | PsR3 | PsL1 | PsR1 | PsL2 | PsR2)) != 0
                                    || Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter) || Input.GetKey(KeyCode.Backspace) || Input.GetKey(KeyCode.Escape) || Input.GetKey(OpenKey)
                                    || Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow)
                                    || Input.GetKey(KeyCode.PageUp) || Input.GetKey(KeyCode.PageDown)
                                    || (xboxReady && (XboxControllerInput.GetButton(XboxControllerInput.Button.ButtonA)
                                    || XboxControllerInput.GetButton(XboxControllerInput.Button.ButtonB)
                                    || XboxControllerInput.GetButton(XboxControllerInput.Button.LeftStick)
                                    || XboxControllerInput.GetButton(XboxControllerInput.Button.RightStick)
                                    || XboxControllerInput.GetButton(XboxControllerInput.Button.LeftShoulder)
                                    || XboxControllerInput.GetButton(XboxControllerInput.Button.RightShoulder)));
                if (!anyHeld)
                {
                    waitRelease = false;
                    PadBridge.BlockGame = false;
                    Time.timeScale = savedScale;
                }
                return;
            }
            if (!IsOpen) return;
            if (Time.realtimeSinceStartup >= nextRefresh)
            {
                nextRefresh = Time.realtimeSinceStartup + 0.5f;  // live values (Picture in use, resolution, readout)
                Refresh();
            }

            // Tabs: LB/RB or LT/RT, L1/R1 or L2/R2, Page Up/Down or Q/E.
            bool xl = xboxReady && (XboxControllerInput.GetButton(XboxControllerInput.Button.LeftShoulder) || XboxControllerInput.GetButton(XboxControllerInput.Button.LeftTrigger));
            bool xr = xboxReady && (XboxControllerInput.GetButton(XboxControllerInput.Button.RightShoulder) || XboxControllerInput.GetButton(XboxControllerInput.Button.RightTrigger));
            bool pl = (ps & (PsL1 | PsL2)) != 0, pr = (ps & (PsR1 | PsR2)) != 0;
            bool kl = Input.GetKey(KeyCode.PageUp) || Input.GetKey(KeyCode.Q), kr = Input.GetKey(KeyCode.PageDown) || Input.GetKey(KeyCode.E);
            bool tl = xl || pl || kl, tr = xr || pr || kr;
            if ((tl && !tabLeftWas) || (tr && !tabRightWas))
            {
                lastDevice = (pl || pr) ? Device.PlayStation : (kl || kr) ? Device.Keyboard : Device.Xbox;
                SwitchTab(tl && !tabLeftWas ? -1 : 1);
            }
            tabLeftWas = tl;
            tabRightWas = tr;

            bool xa = xboxReady && XboxControllerInput.GetButton(XboxControllerInput.Button.ButtonA);
            bool xb = xboxReady && XboxControllerInput.GetButton(XboxControllerInput.Button.ButtonB);
            bool pa = (ps & PsCross) != 0, pb = (ps & PsCircle) != 0;
            bool ka = Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter), kb = Input.GetKey(KeyCode.Backspace) || Input.GetKey(KeyCode.Escape);
            bool a = xa || pa || ka;
            bool b = xb || pb || kb;
            if ((a && !aWas) || (b && !bWas)) lastDevice = (pa || pb) ? Device.PlayStation : (ka || kb) ? Device.Keyboard : Device.Xbox;
            if (b && !bWas) { bWas = true; aWas = a; Close(); return; }
            if (a && !aWas) PressRow();
            aWas = a;
            bWas = b;

            int y = xboxReady ? Direction(XboxControllerInput.Axis.LeftStickY, XboxControllerInput.Axis.DpadY) : 0;
            int x = xboxReady ? Direction(XboxControllerInput.Axis.LeftStickX, XboxControllerInput.Axis.DpadX) : 0;
            if (y == 0 && x == 0 && (ps & PsConnected) != 0)
            {
                int stick = Rig.PadStick, sx = (stick & 0xff) - 128, sy = ((stick >> 8) & 0xff) - 128;
                y = (ps & PsUp) != 0 || sy < -64 ? 1 : (ps & PsDown) != 0 || sy > 64 ? -1 : 0;
                x = (ps & PsRight) != 0 || sx > 64 ? 1 : (ps & PsLeft) != 0 || sx < -64 ? -1 : 0;
                if (x != 0 || y != 0) lastDevice = Device.PlayStation;
            }
            if (y == 0 && x == 0)
            {
                y = Input.GetKey(KeyCode.UpArrow) ? 1 : Input.GetKey(KeyCode.DownArrow) ? -1 : 0;
                x = Input.GetKey(KeyCode.RightArrow) ? 1 : Input.GetKey(KeyCode.LeftArrow) ? -1 : 0;
                if (x != 0 || y != 0) lastDevice = Device.Keyboard;
            }
            else if (lastDevice == Device.Keyboard && (ps & (PsUp | PsDown | PsLeft | PsRight)) == 0) lastDevice = Device.Xbox;
            float now = Time.realtimeSinceStartup;
            if (y != heldY || x != heldX)
            {
                nextRepeat = now + 0.35f;
                Step(x, y);
            }
            else if ((x != 0 || y != 0) && now >= nextRepeat)
            {
                nextRepeat = now + 0.14f;
                Step(x, y);
            }
            heldX = x;
            heldY = y;
        }

        static int Direction(XboxControllerInput.Axis stick, XboxControllerInput.Axis dpad)
        {
            float v = XboxControllerInput.GetAxis(stick);
            if (Mathf.Abs(v) < 0.5f) v = XboxControllerInput.GetAxis(dpad);
            return v > 0.5f ? 1 : (v < -0.5f ? -1 : 0);
        }

        internal void SwitchTab(int dir)
        {
            if (Tabs.Count < 2) return;
            tab = (tab + dir + Tabs.Count) % Tabs.Count;
            if (Sound != null) Sound.Tab();
            Plugin.Log.LogInfo("MENU tab " + Tabs[tab].Name);
            Refresh();
        }

        void PressRow()
        {
            var rows = Rows;
            if (rows.Count == 0) return;
            var row = rows[Selected];
            if (row.Press != null) { row.Press(); if (Sound != null) Sound.Change(); }
            else if (row.Change != null) { if (row.Change(1)) { if (Sound != null) Sound.Change(); } else if (Sound != null) Sound.Refused(); }
            Refresh();
        }

        void Step(int x, int y)
        {
            var rows = Rows;
            if (rows.Count == 0) return;
            if (y != 0)
            {
                Selected = (Selected - y + rows.Count) % rows.Count;
                if (Sound != null) Sound.Move();
            }
            else if (x != 0)
            {
                var row = rows[Selected];
                if (row.Change == null) return;
                bool changed = row.Change(x);
                if (Sound != null) { if (changed) Sound.Change(); else Sound.Refused(); }
            }
            else return;
            Refresh();
        }

        // Lab test: select a tab and row by number, for screenshots.
        internal void Select(int tabIndex, int rowIndex)
        {
            if (Tabs.Count == 0) return;
            tab = Mathf.Clamp(tabIndex, 0, Tabs.Count - 1);
            if (selectedPerTab.Length != Tabs.Count) selectedPerTab = new int[Tabs.Count];
            Selected = Mathf.Clamp(rowIndex, 0, Math.Max(0, Rows.Count - 1));
            Refresh();
        }
    }
}
