using System;
using System.Runtime.InteropServices;
using Rewired;
using UnityEngine;
using UnityEngine.UI;

namespace CupheadVR
{
    // The in-headset settings menu. Hold both sticks for half a second to open or close it (a quick press still reaches
    // the game). F1 does the same from a keyboard. While it is open the game is paused and sees no input, and after it
    // closes the game sees nothing until every button is up. Drawn into a texture that the rig places in front of the
    // stage in both eyes, so it floats at its own depth.
    internal class MenuUI : MonoBehaviour
    {
        const int Layer = 31;
        const float HoldToOpen = 0.5f;
        static readonly Color Gold = new Color(1f, 0.82f, 0.45f, 1f);
        static readonly Color Dim = new Color(0.82f, 0.82f, 0.8f, 1f);

        internal Func<int, string> RowText;
        internal Action<int, int> Adjust;  // row, direction
        internal Action<int> Activate;     // row
        internal Action Closed;
        internal int Rows = 11;
        internal bool IsOpen { get; private set; }
        internal RenderTexture Texture { get { return IsOpen ? rt : null; } }

        RenderTexture rt;
        Camera menuCam;
        GameObject root;
        Text[] rows;
        Image bar;
        int selected;
        float bothSince = -1f, savedScale = 1f, nextRepeat;
        bool bothFired, waitRelease, aWas, bWas;
        int heldX, heldY;
        bool dirty;

        // XInput, read directly so the menu works whatever the game's own input is doing.
        [StructLayout(LayoutKind.Sequential)]
        struct XState { public uint packet; public ushort buttons; public byte lt, rt; public short lx, ly, rx, ry; }
        [DllImport("xinput1_4.dll")] static extern uint XInputGetState(uint index, out XState state);
        const ushort Up = 0x1, Down = 0x2, Left = 0x4, Right = 0x8, LStick = 0x40, RStick = 0x80, A = 0x1000, B = 0x2000;

        static ushort Buttons(out short lx, out short ly)
        {
            ushort all = 0;
            lx = ly = 0;
            for (uint i = 0; i < 4; i++)
            {
                XState s;
                if (XInputGetState(i, out s) != 0) continue;
                all |= s.buttons;
                if (Math.Abs((int)s.lx) > Math.Abs((int)lx)) lx = s.lx;
                if (Math.Abs((int)s.ly) > Math.Abs((int)ly)) ly = s.ly;
            }
            return all;
        }

        bool Build()
        {
            rt = new RenderTexture(1024, 720, 24, RenderTextureFormat.ARGB32) { name = "CupheadVR menu" };
            rt.Create();
            var camGo = new GameObject("CupheadVR menu camera");
            DontDestroyOnLoad(camGo);
            camGo.transform.position = new Vector3(0f, 50000f, -10f);  // far from any scene, so game cameras never see it
            menuCam = camGo.AddComponent<Camera>();
            menuCam.enabled = false;
            menuCam.orthographic = true;
            menuCam.orthographicSize = 360f;
            menuCam.cullingMask = 1 << Layer;
            menuCam.clearFlags = CameraClearFlags.SolidColor;
            menuCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            menuCam.nearClipPlane = 0.1f;
            menuCam.farClipPlane = 100f;
            menuCam.targetTexture = rt;

            root = new GameObject("CupheadVR menu");
            DontDestroyOnLoad(root);
            root.layer = Layer;
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = menuCam;
            canvas.planeDistance = 5f;
            Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
            {
                if (f.name.IndexOf("Vogue", StringComparison.OrdinalIgnoreCase) >= 0 || f.name.IndexOf("Cuphead", StringComparison.OrdinalIgnoreCase) >= 0) { font = f; break; }
            }
            Plugin.Log.LogInfo("menu font: " + (font != null ? font.name : "none"));

            var panel = MakeImage(root.transform, new Color(0.06f, 0.05f, 0.04f, 0.93f), new Vector2(1000f, 700f), Vector2.zero);
            panel.gameObject.name = "panel";
            MakeImage(root.transform, new Color(0.85f, 0.75f, 0.55f, 1f), new Vector2(960f, 3f), new Vector2(0f, 270f));
            var title = MakeText(root.transform, font, 46, new Vector2(0f, 310f));
            title.text = "CUPHEAD VR";
            title.color = Gold;
            bar = MakeImage(root.transform, new Color(0.55f, 0.15f, 0.12f, 0.9f), new Vector2(900f, 44f), Vector2.zero);
            rows = new Text[Rows];
            for (int i = 0; i < Rows; i++) rows[i] = MakeText(root.transform, font, 30, new Vector2(0f, RowY(i)));
            var hint = MakeText(root.transform, font, 22, new Vector2(0f, -320f));
            hint.text = "Up and down: choose.   Left and right: change.   A: select.   B: close";
            hint.color = Dim;
            SetLayer(root);
            root.SetActive(false);
            return true;
        }

        static float RowY(int row) { return 228f - row * 46f; }

        static Image MakeImage(Transform parent, Color color, Vector2 size, Vector2 pos)
        {
            var go = new GameObject("image");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            var r = img.rectTransform;
            r.sizeDelta = size;
            r.anchoredPosition = pos;
            return img;
        }

        static Text MakeText(Transform parent, Font font, int size, Vector2 pos)
        {
            var go = new GameObject("text");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.color = Dim;
            var r = t.rectTransform;
            r.sizeDelta = new Vector2(960f, size * 1.5f);
            r.anchoredPosition = pos;
            return t;
        }

        static void SetLayer(GameObject go)
        {
            go.layer = Layer;
            foreach (Transform t in go.transform) SetLayer(t.gameObject);
        }

        internal void Open()
        {
            if (IsOpen || waitRelease) return;
            if (root == null && !Build()) return;
            root.SetActive(true);
            IsOpen = true;
            selected = 0;
            savedScale = Time.timeScale;
            Time.timeScale = 0f;
            SetGameInput(false);
            aWas = bWas = true;  // ignore presses already held
            Refresh();
            Plugin.Log.LogInfo("MENU open");
        }

        internal void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (root != null) root.SetActive(false);
            waitRelease = true;  // the game stays paused and deaf until every button is up
            Plugin.Log.LogInfo("MENU close");
            if (Closed != null) Closed();
        }

        static void SetGameInput(bool on)
        {
            try
            {
                if (!ReInput.isReady) return;
                foreach (var p in ReInput.players.AllPlayers) p.controllers.maps.SetAllMapsEnabled(on);
            }
            catch (Exception e) { Plugin.Log.LogWarning("menu: could not switch game input: " + e.Message); }
        }

        internal void Refresh() { dirty = true; }

        void Redraw()
        {
            dirty = false;
            for (int i = 0; i < Rows; i++)
            {
                rows[i].text = RowText(i);
                rows[i].color = i == selected ? Gold : Dim;
            }
            bar.rectTransform.anchoredPosition = new Vector2(0f, RowY(selected));
            menuCam.Render();
        }

        void Update()
        {
            short lx, ly;
            ushort btn = Buttons(out lx, out ly);
            bool both = (btn & LStick) != 0 && (btn & RStick) != 0;
            float now = Time.realtimeSinceStartup;
            if (both)
            {
                if (bothSince < 0f) bothSince = now;
                if (!bothFired && now - bothSince >= HoldToOpen)
                {
                    bothFired = true;
                    if (IsOpen) Close(); else Open();
                }
            }
            else
            {
                bothSince = -1f;
                bothFired = false;
            }
            if (Input.GetKeyDown(KeyCode.F1)) { if (IsOpen) Close(); else Open(); }

            if (waitRelease)
            {
                if (btn == 0 && Mathf.Abs(lx) < 8000 && Mathf.Abs(ly) < 8000)
                {
                    waitRelease = false;
                    Time.timeScale = savedScale;
                    SetGameInput(true);
                }
                return;
            }
            if (!IsOpen) return;

            bool a = (btn & A) != 0 || Input.GetKey(KeyCode.Return);
            bool b = (btn & B) != 0 || Input.GetKey(KeyCode.Escape);
            if (b && !bWas) { bWas = true; aWas = a; Close(); return; }
            if (a && !aWas) { Activate(selected); Refresh(); }
            aWas = a;
            bWas = b;

            int y = (btn & Up) != 0 || ly > 16000 || Input.GetKey(KeyCode.UpArrow) ? 1 : (btn & Down) != 0 || ly < -16000 || Input.GetKey(KeyCode.DownArrow) ? -1 : 0;
            int x = (btn & Right) != 0 || lx > 16000 || Input.GetKey(KeyCode.RightArrow) ? 1 : (btn & Left) != 0 || lx < -16000 || Input.GetKey(KeyCode.LeftArrow) ? -1 : 0;
            if (y != heldY || x != heldX)
            {
                nextRepeat = now + 0.35f;
                Step(x, y);
            }
            else if ((x != 0 || y != 0) && now >= nextRepeat)
            {
                nextRepeat = now + 0.12f;
                Step(x, y);
            }
            heldX = x;
            heldY = y;
        }

        void LateUpdate()
        {
            if (IsOpen && dirty) Redraw();
        }

        void Step(int x, int y)
        {
            if (y != 0) selected = (selected - y + Rows) % Rows;
            else if (x != 0) Adjust(selected, x);
            else return;
            Refresh();
        }

        // Lab: drive the menu from the command file.
        internal void LabKey(string key)
        {
            if (!IsOpen) return;
            if (key == "up") Step(0, 1);
            else if (key == "down") Step(0, -1);
            else if (key == "left") Step(-1, 0);
            else if (key == "right") Step(1, 0);
            else if (key == "a") { Activate(selected); Refresh(); }
            else if (key == "b") Close();
        }
    }
}
