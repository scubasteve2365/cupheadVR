using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Rewired;
using UnityEngine;
using UnityEngine.UI;

namespace CupheadVR
{
    // The in-headset settings menu, styled after the game's own cards (cream paper, ink text, red selection).
    // Hold both sticks for half a second to open or close it (a quick press still reaches the game); F1 does the same.
    // LB/RB or LT/RT change tab, up and down choose, left and right change, A selects, B closes.
    // While it is open the game is paused and sees no input, and after it closes the game sees nothing until every
    // button is up. It is drawn into a texture that goes to the headset as its own quad, fixed where it opened.
    internal class MenuUI : MonoBehaviour
    {
        internal class Row
        {
            public string Label;
            public Func<string> Value;      // null for a button row
            public Action<int> Change;      // left/right; null if the row has no value to change
            public Action Press;            // A; null to make A step the value up
            public Func<string> Help;
        }

        internal class Tab
        {
            public string Name;
            public List<Row> Rows = new List<Row>();
        }

        const int Layer = 31;
        const float HoldToOpen = 0.5f;
        const int TexW = 1024, TexH = 800, MaxRows = 6;
        static readonly Color Paper = new Color(0.93f, 0.89f, 0.80f, 1f);
        static readonly Color PaperDark = new Color(0.86f, 0.80f, 0.68f, 1f);
        static readonly Color Ink = new Color(0.16f, 0.15f, 0.17f, 1f);
        static readonly Color InkSoft = new Color(0.35f, 0.33f, 0.34f, 1f);
        static readonly Color Red = new Color(0.74f, 0.11f, 0.12f, 1f);
        static readonly Color Border = new Color(0.27f, 0.33f, 0.42f, 1f);

        internal List<Tab> Tabs = new List<Tab>();
        internal string Version = "";
        internal Action Closed;
        internal Func<int> Volume;          // 0 off .. 3 high
        internal bool IsOpen { get; private set; }
        internal RenderTexture Texture { get { return IsOpen ? outRt : null; } }

        RenderTexture rt, outRt;  // outRt is upside down, the way the headset wants it (same as the eye images)
        Camera menuCam;
        GameObject root;
        Text[] tabText, labels, values;
        Image[] tabLine, rowBar, rowRule;
        Text help, footer, version;
        int tab, selected;
        float bothSince = -1f, savedScale = 1f, nextRepeat;
        bool bothFired, waitRelease, aWas, bWas, tabLWas, tabRWas, dirty;
        int heldX, heldY;
        AudioSource audioSource;
        AudioClip clickMove, clickChange, clickTab, clickOpen, clickClose, clickRefuse;

        [StructLayout(LayoutKind.Sequential)]
        struct XState { public uint packet; public ushort buttons; public byte lt, rt; public short lx, ly, rx, ry; }
        [DllImport("xinput1_4.dll")] static extern uint XInputGetState(uint index, out XState state);
        const ushort Up = 0x1, Down = 0x2, Left = 0x4, Right = 0x8, LStick = 0x40, RStick = 0x80, LB = 0x100, RB = 0x200, A = 0x1000, B = 0x2000;

        static ushort Buttons(out short lx, out short ly, out bool lt, out bool rtrig)
        {
            ushort all = 0;
            lx = ly = 0;
            lt = rtrig = false;
            for (uint i = 0; i < 4; i++)
            {
                XState s;
                if (XInputGetState(i, out s) != 0) continue;
                all |= s.buttons;
                lt |= s.lt > 128;
                rtrig |= s.rt > 128;
                if (Math.Abs((int)s.lx) > Math.Abs((int)lx)) lx = s.lx;
                if (Math.Abs((int)s.ly) > Math.Abs((int)ly)) ly = s.ly;
            }
            return all;
        }

        // ---- look ----

        bool Build()
        {
            rt = new RenderTexture(TexW, TexH, 24, RenderTextureFormat.ARGB32) { name = "CupheadVR menu" };
            rt.Create();
            outRt = new RenderTexture(TexW, TexH, 0, RenderTextureFormat.ARGB32) { name = "CupheadVR menu out" };
            outRt.Create();
            var camGo = new GameObject("CupheadVR menu camera");
            DontDestroyOnLoad(camGo);
            camGo.transform.position = new Vector3(0f, 50000f, -10f);  // far from any scene, so game cameras never see it
            menuCam = camGo.AddComponent<Camera>();
            menuCam.enabled = false;
            menuCam.orthographic = true;
            menuCam.orthographicSize = TexH / 2f;
            menuCam.cullingMask = 1 << Layer;
            menuCam.clearFlags = CameraClearFlags.SolidColor;
            menuCam.backgroundColor = Border;
            menuCam.nearClipPlane = 0.1f;
            menuCam.farClipPlane = 100f;
            menuCam.targetTexture = rt;

            root = new GameObject("CupheadVR menu");
            DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = menuCam;
            canvas.planeDistance = 5f;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            Font body = Resources.GetBuiltinResource<Font>("Arial.ttf"), heading = null;
            var names = new List<string>();
            foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
            {
                names.Add(f.name);
                if (f.name.IndexOf("Memphis", StringComparison.OrdinalIgnoreCase) >= 0 && f.name.IndexOf("Medium", StringComparison.OrdinalIgnoreCase) >= 0) body = f;
                if (heading == null && f.name.IndexOf("Vogue", StringComparison.OrdinalIgnoreCase) >= 0 && f.name.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0) heading = f;
            }
            if (heading == null) foreach (var f in Resources.FindObjectsOfTypeAll<Font>()) if (f.name.IndexOf("Vogue", StringComparison.OrdinalIgnoreCase) >= 0) { heading = f; break; }
            if (heading == null) heading = body;
            Plugin.Log.LogInfo("menu fonts: body " + body.name + ", heading " + heading.name + " (loaded: " + string.Join(", ", names.ToArray()) + ")");

            // The card: paper with a double ink border, like the game's death and shop cards.
            Img(Paper, 1000, 776, 0, 0);
            Img(Border, 968, 4, 0, 370); Img(Border, 968, 4, 0, -370); Img(Border, 4, 744, -482, 0); Img(Border, 4, 744, 482, 0);
            Img(Border, 952, 1.5f, 0, 361); Img(Border, 952, 1.5f, 0, -361); Img(Border, 1.5f, 726, -473, 0); Img(Border, 1.5f, 726, 473, 0);

            version = Txt(heading, 20, InkSoft, TextAnchor.MiddleRight, 300, 30, 300, 335);
            int n = Math.Max(1, Tabs.Count);
            tabText = new Text[n];
            tabLine = new Image[n];
            float tabW = 900f / n;
            for (int i = 0; i < n; i++)
            {
                float x = -450f + tabW * (i + 0.5f);
                tabText[i] = Txt(heading, 38, Ink, TextAnchor.MiddleCenter, tabW, 54, x, 295);
                tabLine[i] = Img(Red, tabW - 40, 5, x, 262);
            }
            Img(Border, 900, 2, 0, 250);
            labels = new Text[MaxRows];
            values = new Text[MaxRows];
            rowBar = new Image[MaxRows];
            rowRule = new Image[MaxRows];
            for (int i = 0; i < MaxRows; i++)
            {
                float y = RowY(i);
                rowBar[i] = Img(PaperDark, 900, 54, 0, y);
                rowRule[i] = Img(new Color(Border.r, Border.g, Border.b, 0.35f), 880, 1.5f, 0, y - 29);
                labels[i] = Txt(body, 33, Ink, TextAnchor.MiddleLeft, 600, 54, -150, y);
                values[i] = Txt(body, 33, Ink, TextAnchor.MiddleRight, 380, 54, 245, y);
            }
            // Help box with a red stripe, then the button line.
            Img(PaperDark, 900, 120, 0, -258);
            Img(Red, 6, 120, -447, -258);
            help = Txt(body, 25, Ink, TextAnchor.UpperLeft, 860, 110, 8, -262);
            help.horizontalOverflow = HorizontalWrapMode.Wrap;
            footer = Txt(body, 21, InkSoft, TextAnchor.MiddleCenter, 940, 30, 0, -343);
            footer.text = "LB / RB or LT / RT: tab     Up / down: choose     Left / right: change     A: select     B: close";
            SetLayer(root);
            root.SetActive(false);
            MakeSounds();
            return true;
        }

        static float RowY(int row) { return 205f - row * 62f; }

        Image Img(Color c, float w, float h, float x, float y)
        {
            var go = new GameObject("image");
            go.transform.SetParent(root.transform, false);
            var img = go.AddComponent<Image>();
            img.color = c;
            img.rectTransform.sizeDelta = new Vector2(w, h);
            img.rectTransform.anchoredPosition = new Vector2(x, y);
            return img;
        }

        Text Txt(Font font, int size, Color c, TextAnchor anchor, float w, float h, float x, float y)
        {
            var go = new GameObject("text");
            go.transform.SetParent(root.transform, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.color = c;
            t.rectTransform.sizeDelta = new Vector2(w, h);
            t.rectTransform.anchoredPosition = new Vector2(x, y);
            return t;
        }

        static void SetLayer(GameObject go)
        {
            go.layer = Layer;
            foreach (Transform t in go.transform) SetLayer(t.gameObject);
        }

        // ---- sound: short generated tones, no files ----

        void MakeSounds()
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 0f;
            audioSource.ignoreListenerPause = true;
            audioSource.ignoreListenerVolume = true;
            audioSource.playOnAwake = false;
            clickMove = Tone("move", 880f, 880f, 0.035f);
            clickChange = Tone("change", 1250f, 1250f, 0.04f);
            clickTab = Tone("tab", 660f, 990f, 0.07f);
            clickOpen = Tone("open", 520f, 1040f, 0.12f);
            clickClose = Tone("close", 1040f, 520f, 0.12f);
            clickRefuse = Tone("refuse", 190f, 170f, 0.14f);
        }

        static AudioClip Tone(string name, float f0, float f1, float seconds)
        {
            const int rate = 44100;
            int n = (int)(rate * seconds);
            var data = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                phase += 2 * Math.PI * Mathf.Lerp(f0, f1, t) / rate;
                float env = Mathf.Min(1f, i / (rate * 0.004f)) * (1f - t) * (1f - t);
                data[i] = (float)Math.Sin(phase) * env * 0.5f;
            }
            var clip = AudioClip.Create("CupheadVR " + name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void Play(AudioClip clip)
        {
            int v = Volume != null ? Volume() : 2;
            if (audioSource == null || clip == null || v <= 0) return;
            audioSource.PlayOneShot(clip, v == 1 ? 0.25f : v == 2 ? 0.5f : 1f);
        }

        // ---- behaviour ----

        internal void Open()
        {
            if (IsOpen || waitRelease || Tabs.Count == 0) return;
            if (root == null && !Build()) return;
            root.SetActive(true);
            IsOpen = true;
            selected = 0;
            savedScale = Time.timeScale;
            Time.timeScale = 0f;
            SetGameInput(false);
            aWas = bWas = tabLWas = tabRWas = true;  // ignore presses already held
            Refresh();
            Play(clickOpen);
            Plugin.Log.LogInfo("MENU open");
        }

        internal void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (root != null) root.SetActive(false);
            waitRelease = true;  // the game stays paused and deaf until every button is up
            Play(clickClose);
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
            version.text = Version;
            for (int i = 0; i < tabText.Length; i++)
            {
                bool on = i == tab;
                tabText[i].text = i < Tabs.Count ? Tabs[i].Name.ToUpperInvariant() : "";
                tabText[i].color = on ? Red : InkSoft;
                tabLine[i].enabled = on;
            }
            var rows = Tabs[tab].Rows;
            for (int i = 0; i < MaxRows; i++)
            {
                bool used = i < rows.Count;
                bool sel = used && i == selected;
                labels[i].text = used ? (sel ? "▸ " : "   ") + rows[i].Label : "";
                values[i].text = used && rows[i].Value != null ? rows[i].Value() : "";
                labels[i].color = sel ? Red : Ink;
                values[i].color = sel ? Red : Ink;
                rowBar[i].enabled = sel;
                rowRule[i].enabled = used && !sel;
            }
            help.text = selected < rows.Count && rows[selected].Help != null ? rows[selected].Help() : "";
            menuCam.Render();
            Graphics.Blit(rt, outRt, new Vector2(1f, -1f), new Vector2(0f, 1f));
        }

        void Update()
        {
            short lx, ly;
            bool lt, rtrig;
            ushort btn = Buttons(out lx, out ly, out lt, out rtrig);
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
                if (btn == 0 && !lt && !rtrig && Mathf.Abs(lx) < 8000 && Mathf.Abs(ly) < 8000)
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
            bool tabL = (btn & LB) != 0 || lt || Input.GetKey(KeyCode.PageUp);
            bool tabR = (btn & RB) != 0 || rtrig || Input.GetKey(KeyCode.PageDown);
            if (b && !bWas) { bWas = true; aWas = a; Close(); return; }
            if (a && !aWas) PressRow();
            if (tabL && !tabLWas) ChangeTab(-1);
            if (tabR && !tabRWas) ChangeTab(1);
            aWas = a;
            bWas = b;
            tabLWas = tabL;
            tabRWas = tabR;

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

        void ChangeTab(int dir)
        {
            tab = (tab + dir + Tabs.Count) % Tabs.Count;
            selected = 0;
            Play(clickTab);
            Refresh();
        }

        void PressRow()
        {
            var rows = Tabs[tab].Rows;
            if (selected >= rows.Count) return;
            var r = rows[selected];
            if (r.Press != null) { r.Press(); Play(clickChange); }
            else if (r.Change != null) ChangeRow(r, 1);
            else Play(clickRefuse);
            Refresh();
        }

        void ChangeRow(Row r, int dir)
        {
            if (r.Change == null) { Play(clickRefuse); return; }
            string before = r.Value != null ? r.Value() : null;
            r.Change(dir);
            Play(r.Value != null && r.Value() == before ? clickRefuse : clickChange);  // at a limit nothing changes
        }

        void Step(int x, int y)
        {
            var rows = Tabs[tab].Rows;
            if (y != 0)
            {
                selected = (selected - y + rows.Count) % rows.Count;
                Play(clickMove);
            }
            else if (x != 0 && selected < rows.Count) ChangeRow(rows[selected], x);
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
            else if (key == "a") PressRow();
            else if (key == "b") Close();
            else if (key == "lb") ChangeTab(-1);
            else if (key == "rb") ChangeTab(1);
        }
    }
}
