using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace CupheadVR
{
    // Renders the game's camera stack once per eye from one game tick and hands both images to CupheadVRNative.
    // Ported from OriVR's XrRig (MIT, scubasteve2365); the stereo method is new for Cuphead.
    //
    // The stage: a flat 16:9 rectangle fixed in the room shows the game. Cuphead draws flat sprites with orthographic
    // cameras, and gives every background and foreground layer a parallax speed s (s = 0 moves with the world, s = 1 with
    // the camera, s < 0 in front). A layer with speed s behaves like a layer at depth d/(1-s) behind a plane at distance d,
    // so seen from an eye offset by x from the stage centre it shifts by x * s on the stage. Each eye moves every layer by
    // that much, renders, and puts it back. The play plane and the HUD stay on the stage.
    internal class XrRig : MonoBehaviour
    {
        const string Dll = "CupheadVRNative";
        const int EventInit = 1, EventSubmit = 2, EventShutdown = 3, EventResize = 4;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct EyeView { public float px, py, pz, qx, qy, qz, qw, left, right, up, down; }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct FrameInfo
        {
            public int running, shouldRender, viewsValid, sessionState;
            public float periodMs;
            public EyeView eye0, eye1;
        }

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)] static extern IntPtr LoadLibraryA(string path);
        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)] static extern IntPtr GetProcAddress(IntPtr module, string name);

        // Unity's Mono will not bind a DllImport to a DLL in the plugin folder, so the functions are fetched by hand.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr GetRenderEventFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int StateFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetStageFn(float cx, float cy, float cz, float width, float height, float qy, float qw);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetSizeFn(int width, int height);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetSharpenFn(float amount);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DumpNextFn([MarshalAs(UnmanagedType.LPStr)] string basePath);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int RecommendedWidthFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void StartFn(IntPtr left, IntPtr right, int width, int height);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int BeginFrameFn(out FrameInfo info, IntPtr left, IntPtr right);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int PopLogFn(StringBuilder buffer, int size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetMenuFn(IntPtr tex, float cx, float cy, float cz, float width, float height, float qy, float qw);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void StatsFn(out uint frames, out uint layers, out uint skipped);

        GetRenderEventFn xGetRenderEvent;
        StateFn xState, xTakeRecenter;
        SetStageFn xSetStage;
        SetSizeFn xSetSize;
        SetSharpenFn xSetSharpen;
        DumpNextFn xDumpNext;
        RecommendedWidthFn xRecommendedWidth;
        StartFn xStart;
        BeginFrameFn xBeginFrame;
        PopLogFn xPopLog;
        StatsFn xStats;
        SetMenuFn xSetMenu;

        static T Fn<T>(IntPtr module, string name) where T : class
        {
            var p = GetProcAddress(module, name);
            if (p == IntPtr.Zero) throw new EntryPointNotFoundException(name);
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        internal string PluginDir, ShotDir;
        internal float StageWidth = 1.85f, StageDistance = 1.0f, Separation = 1.0f;
        internal float Sharpness = 1.0f;  // resolution scale: 1 matches the headset's pixel density across the stage
        internal float PopOut;            // 0 keeps the play plane on the stage; higher brings everything toward you
        internal float SkyDepth = 0.85f;
        internal float CharacterPop = 1.0f;  // play-plane layering by the game's sorting layers, 1 = default spread  // depth given to skies and scrolling clouds that have no parallax speed of their own
        internal float Sharpen = 0.3f;
        internal int EyeWidth = 2560, EyeHeight = 1440;
        internal bool FlipY = true, Stamp;
        internal MenuUI Menu;
        internal bool MatchRefresh;
        internal bool Smooth = true;  // draw things the game moves in its 60 Hz steps in between steps  // one fixed game step per headset frame (Ori's way to keep motion smooth)
        float savedFixed = -1f;

        Vector3 viewPoint;
        Quaternion viewTurn = Quaternion.identity;
        bool wantRecenter = true;
        float eyeTanSpan;
        bool nativeLoaded, active, wantShot, loggedPose;
        IntPtr renderEvent;
        readonly RenderTexture[] eyeRender = new RenderTexture[2];
        readonly RenderTexture[] eyeOut = new RenderTexture[2];
        Material copyMat, blendMat;
        Texture2D stampTex;
        int savedTargetFps, savedVsync;
        readonly StringBuilder logBuf = new StringBuilder(512);
        uint renderedPairs;
        int lastState = -1, eyeCheckFrames;

        internal bool Active { get { return active; } }

        bool LoadNative()
        {
            if (nativeLoaded) return true;
            var path = Path.Combine(PluginDir, Dll + ".dll");
            var module = LoadLibraryA(path);
            if (module == IntPtr.Zero)
            {
                Plugin.Log.LogError("could not load " + path + ", error " + Marshal.GetLastWin32Error());
                return false;
            }
            xGetRenderEvent = Fn<GetRenderEventFn>(module, "CupXR_GetRenderEvent");
            xState = Fn<StateFn>(module, "CupXR_State");
            xTakeRecenter = Fn<StateFn>(module, "CupXR_TakeRecenter");
            xSetStage = Fn<SetStageFn>(module, "CupXR_SetStage");
            xSetSize = Fn<SetSizeFn>(module, "CupXR_SetSize");
            xSetSharpen = Fn<SetSharpenFn>(module, "CupXR_SetSharpen");
            xDumpNext = Fn<DumpNextFn>(module, "CupXR_DumpNext");
            xRecommendedWidth = Fn<RecommendedWidthFn>(module, "CupXR_RecommendedWidth");
            xStart = Fn<StartFn>(module, "CupXR_Start");
            xBeginFrame = Fn<BeginFrameFn>(module, "CupXR_BeginFrame");
            xPopLog = Fn<PopLogFn>(module, "CupXR_PopLog");
            xStats = Fn<StatsFn>(module, "CupXR_Stats");
            xSetMenu = Fn<SetMenuFn>(module, "CupXR_SetMenu");
            renderEvent = xGetRenderEvent();
            nativeLoaded = true;
            return true;
        }

        internal void Begin()
        {
            if (active || !LoadNative()) return;
            var shader = Shader.Find("Hidden/BlitCopy");
            var blend = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            if (shader == null || blend == null) { Plugin.Log.LogError("VR: missing shader BlitCopy " + (shader != null) + " blend " + (blend != null)); return; }
            copyMat = new Material(shader);
            blendMat = new Material(blend);
            MakeTextures(EyeWidth, EyeHeight);
            stampTex = new Texture2D(32, 1, TextureFormat.RGB24, false);
            stampTex.filterMode = FilterMode.Point;
            savedTargetFps = Application.targetFrameRate;
            savedVsync = QualitySettings.vSyncCount;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;  // xrWaitFrame paces the game now
            ApplyStage();
            xStart(eyeOut[0].GetNativeTexturePtr(), eyeOut[1].GetNativeTexturePtr(), EyeWidth, EyeHeight);
            GL.IssuePluginEvent(renderEvent, EventInit);
            active = true;
            Plugin.Log.LogInfo(string.Format("VR starting: eye {0}x{1}, stage {2} m wide at {3} m, depth x{4}", EyeWidth, EyeHeight, StageWidth, StageDistance, Separation));
            StartCoroutine(Loop());
            StartCoroutine(SnapshotSteps());
        }

        internal void End()
        {
            if (!active) return;
            active = false;
            Application.targetFrameRate = savedTargetFps;
            QualitySettings.vSyncCount = savedVsync;
            GL.IssuePluginEvent(renderEvent, EventShutdown);
            Plugin.Log.LogInfo("VR stopped after " + renderedPairs + " eye pairs");
        }

        void MakeTextures(int width, int height)
        {
            for (int i = 0; i < 2; i++)
            {
                var r = eyeRender[i];
                var o = eyeOut[i];
                eyeRender[i] = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "CupheadVR eye render " + i };
                eyeRender[i].Create();
                eyeOut[i] = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) { name = "CupheadVR eye out " + i };
                eyeOut[i].Create();
                if (r != null) Destroy(r);  // frames already queued hold their own reference to the old texture
                if (o != null) Destroy(o);
            }
        }

        internal void Shot() { wantShot = true; }
        internal void Recenter() { wantRecenter = true; }
        internal void Dump(string basePath) { if (nativeLoaded) xDumpNext(basePath); }
        internal void EyeCheck() { eyeCheckFrames = 4; }

        internal int WantedWidth()
        {
            int rec = xRecommendedWidth != null ? xRecommendedWidth() : 0;
            if (rec <= 0 || eyeTanSpan <= 0f) return EyeWidth;
            int w = Mathf.RoundToInt(rec * (StageWidth / StageDistance) / eyeTanSpan * Sharpness);
            return Mathf.Clamp(w / 16 * 16, 1280, 6144);
        }

        // Rebuilds the eye textures when the stage or resolution calls for a size more than 3 percent away.
        internal void UpdateResolution()
        {
            if (!active || xState() != 2) return;
            int w = WantedWidth();
            if (Mathf.Abs(w - EyeWidth) <= EyeWidth * 0.03f) return;
            int h = w * 9 / 16;
            MakeTextures(w, h);
            EyeWidth = w;
            EyeHeight = h;
            xSetSize(w, h);
            GL.IssuePluginEvent(renderEvent, EventResize);
            ApplyStage();
            Plugin.Log.LogInfo(string.Format("RESOLUTION eye {0}x{1}, stage {2:F2} m at {3:F2} m, scale {4:F2}", w, h, StageWidth, StageDistance, Sharpness));
        }

        void MatchTick(float periodMs)
        {
            float want = MatchRefresh && periodMs > 0f ? periodMs / 1000f : (savedFixed > 0f ? savedFixed : -1f);
            if (want <= 0f || Mathf.Abs(Time.fixedDeltaTime - want) < 0.00002f) return;
            if (savedFixed < 0f) savedFixed = Time.fixedDeltaTime;
            Time.fixedDeltaTime = want;
            Plugin.Log.LogInfo(string.Format("REFRESH fixed step {0:F5} s ({1:F2} Hz)", want, 1f / want));
        }

        FrameInfo lastInfo;
        bool menuWasOpen;
        Vector3 menuPos;
        Quaternion menuTurn = Quaternion.identity;
        internal float MenuDistance = 0.75f, MenuWidth = 0.8f;

        // The menu is its own quad layer, fixed in the room where it opened: in front of the head, a little low.
        void PushMenu()
        {
            bool open = Menu != null && Menu.IsOpen && Menu.Texture != null;
            if (open && !menuWasOpen)
            {
                var a = new Vector3(lastInfo.eye0.px, lastInfo.eye0.py, lastInfo.eye0.pz);
                var b = new Vector3(lastInfo.eye1.px, lastInfo.eye1.py, lastInfo.eye1.pz);
                var head = (a + b) * 0.5f;
                var fwd = new Quaternion(lastInfo.eye0.qx, lastInfo.eye0.qy, lastInfo.eye0.qz, lastInfo.eye0.qw) * new Vector3(0f, 0f, -1f);
                if (lastInfo.eye0.qw == 0f && lastInfo.eye0.qy == 0f) fwd = new Vector3(0f, 0f, -1f);
                menuTurn = Quaternion.Euler(0f, Mathf.Atan2(-fwd.x, -fwd.z) * Mathf.Rad2Deg, 0f);
                float dist = Mathf.Min(MenuDistance, StageDistance - 0.15f);
                menuPos = head + menuTurn * new Vector3(0f, -0.08f, -Mathf.Max(0.45f, dist));
                Plugin.Log.LogInfo(string.Format("MENU placed at ({0:F2},{1:F2},{2:F2}), {3:F2} m wide", menuPos.x, menuPos.y, menuPos.z, MenuWidth));
            }
            menuWasOpen = open;
            if (!open) { xSetMenu(IntPtr.Zero, 0, 0, 0, 0, 0, 0, 1); return; }
            var tex = Menu.Texture;
            float h = MenuWidth * tex.height / tex.width;
            xSetMenu(tex.GetNativeTexturePtr(), menuPos.x, menuPos.y, menuPos.z, MenuWidth, h, menuTurn.y, menuTurn.w);
        }

        void TakeView(FrameInfo info)
        {
            var a = new Vector3(info.eye0.px, info.eye0.py, info.eye0.pz);
            var b = new Vector3(info.eye1.px, info.eye1.py, info.eye1.pz);
            viewPoint = (a + b) * 0.5f;
            var forward = new Quaternion(info.eye0.qx, info.eye0.qy, info.eye0.qz, info.eye0.qw) * new Vector3(0f, 0f, -1f);
            viewTurn = Quaternion.Euler(0f, Mathf.Atan2(-forward.x, -forward.z) * Mathf.Rad2Deg, 0f);
            eyeTanSpan = Mathf.Tan(info.eye0.right) - Mathf.Tan(info.eye0.left);
            ApplyStage();
            Plugin.Log.LogInfo("headset recommends " + xRecommendedWidth() + " wide over " + eyeTanSpan.ToString("F2") + " of flat view");
            Plugin.Log.LogInfo(string.Format("RECENTER view ({0:F2},{1:F2},{2:F2}), turned {3:F0} degrees", viewPoint.x, viewPoint.y, viewPoint.z, viewTurn.eulerAngles.y));
        }

        internal void ApplyStage()
        {
            if (!nativeLoaded) return;
            xSetSharpen(Sharpen);
            var c = viewPoint + viewTurn * new Vector3(0f, 0f, -StageDistance);
            xSetStage(c.x, c.y, c.z, StageWidth, StageWidth * 9f / 16f, viewTurn.y, viewTurn.w);
        }

        void Update()
        {
            if (!nativeLoaded) return;
            while (xPopLog(logBuf, logBuf.Capacity) != 0)
            {
                Plugin.Log.LogInfo("XR: " + logBuf);
                logBuf.Length = 0;
            }
            logBuf.Length = 0;
        }

        IEnumerator Loop()
        {
            var wait = new WaitForEndOfFrame();
            float nextStat = Time.realtimeSinceStartup + 5f;
            int framesAtStat = Time.frameCount;
            while (active)
            {
                yield return wait;
                if (!active) break;
                int state = xState();
                if (state != lastState)
                {
                    lastState = state;
                    Plugin.Log.LogInfo("XR native state " + state);
                }
                if (state == 3) { End(); yield break; }
                if (state != 2) continue;
                // The game sets 60 fps with vsync again on every level load (seen live); the headset paces it instead.
                if (Application.targetFrameRate != -1 || QualitySettings.vSyncCount != 0)
                {
                    Plugin.Log.LogInfo("VR: game set targetFrameRate " + Application.targetFrameRate + " vsync " + QualitySettings.vSyncCount + ", lifted");
                    Application.targetFrameRate = -1;
                    QualitySettings.vSyncCount = 0;
                }
                for (int i = 0; i < 2; i++)
                {
                    if (!eyeRender[i].IsCreated()) eyeRender[i].Create();
                    if (!eyeOut[i].IsCreated()) eyeOut[i].Create();
                }
                PushMenu();
                FrameInfo info;
                if (xBeginFrame(out info, eyeOut[0].GetNativeTexturePtr(), eyeOut[1].GetNativeTexturePtr()) == 0) continue;
                if (xTakeRecenter() != 0) wantRecenter = true;
                MatchTick(info.periodMs);
                if (info.viewsValid != 0 && wantRecenter)
                {
                    wantRecenter = false;
                    TakeView(info);
                    UpdateResolution();
                }
                if (info.viewsValid != 0) lastInfo = info;
                if (info.shouldRender != 0) RenderEyes(info);
                GL.IssuePluginEvent(renderEvent, EventSubmit);

                float now = Time.realtimeSinceStartup;
                if (now >= nextStat)
                {
                    uint frames, layers, skipped;
                    xStats(out frames, out layers, out skipped);
                    float span = now - (nextStat - 5f);
                    Plugin.Log.LogInfo(string.Format("XRSTAT state {0} shouldRender {1} period {2:F2} ms, unity {3:F1} fps, submitted {4}, with images {5}, skipped {6}, pairs {7}, eye {8}x{9}, smoothed {10} of {11}, restore errors {12}",
                        info.sessionState, info.shouldRender, info.periodMs, (Time.frameCount - framesAtStat) / span, frames, layers, skipped, renderedPairs, EyeWidth, EyeHeight, Smoothed, SmoothCandidates, RestoreErrors));
                    nextStat = now + 5f;
                    framesAtStat = Time.frameCount;
                }
            }
        }

        // The layers that get a depth, refreshed a few times a second because levels spawn and remove scenery.
        readonly List<KeyValuePair<Transform, float>> depthLayers = new List<KeyValuePair<Transform, float>>();
        // Play-plane things that get a small depth from the game's sorting layer (Character pop), by sorting layer name.
        static readonly Dictionary<string, float> PopBySortingLayer = new Dictionary<string, float>
        {
            { "Background", 0.05f }, { "Enemies", -0.05f }, { "Player", -0.06f }, { "Projectiles", -0.08f },
            { "Effects", -0.08f }, { "Foreground", -0.12f }, { "ForegroundEffects", -0.15f },
        };
        const float PopMarker = 1000f;  // values at or above this are a Character pop value + PopMarker
        int layersFrame = -1000;
        int skyCount, popCount;
        readonly HashSet<Transform> popTaken = new HashSet<Transform>();

        void CollectLayers()
        {
            if (Time.frameCount - layersFrame < 20) return;
            layersFrame = Time.frameCount;
            depthLayers.Clear();
            skyCount = 0;
            var taken = new HashSet<Transform>();
            foreach (var p in FindObjectsOfType<PlatformingLevelParallax>())
            {
                var props = Traverse.Create(p).Property("LayerProperties");
                object speed = props.GetValue() != null ? props.Field("speed").GetValue() : null;
                if (speed is float && taken.Add(p.transform)) depthLayers.Add(new KeyValuePair<Transform, float>(p.transform, (float)speed));
            }
            foreach (var p in FindObjectsOfType<ParallaxLayer>())
            {
                if (!taken.Add(p.transform)) continue;
                // MinMax layers scroll inside a range of their own (Root Pack clouds), so their percentage is not a depth.
                bool sky = p.type == ParallaxLayer.Type.MinMax && Mathf.Abs(p.percentage) < 0.001f;
                if (sky) skyCount++;
                depthLayers.Add(new KeyValuePair<Transform, float>(p.transform, sky ? float.NaN : p.percentage));
            }
            foreach (var p in FindObjectsOfType<MapParallaxLayer>())
                if (taken.Add(p.transform)) depthLayers.Add(new KeyValuePair<Transform, float>(p.transform, p.percentage));
            int parallaxCount = depthLayers.Count;
            // Backdrops with no parallax at all, drawn behind everything else (Root Pack sky, sorting order -1000).
            foreach (var r in FindObjectsOfType<SpriteRenderer>())
            {
                if (r.sortingLayerName != "Background" || r.sortingOrder > -500) continue;
                if (HasTaken(r.transform, taken)) continue;
                taken.Add(r.transform);
                skyCount++;
                depthLayers.Add(new KeyValuePair<Transform, float>(r.transform, float.NaN));
            }
            // Character pop: sprites on the play plane, by sorting layer. Particles keep their world positions, so they stay.
            popCount = 0;
            foreach (var r in FindObjectsOfType<Renderer>())
            {
                if (r is ParticleSystemRenderer || !r.enabled) continue;
                float pop;
                if (!PopBySortingLayer.TryGetValue(r.sortingLayerName, out pop)) continue;
                if (HasTaken(r.transform, taken)) continue;
                if (!popTaken.Add(r.transform)) continue;
                depthLayers.Add(new KeyValuePair<Transform, float>(r.transform, PopMarker + pop));
                popCount++;
            }
            popTaken.Clear();
            // Parents before children, so each child lands exactly at its own target and is put back exactly.
            var depthOf = new Dictionary<Transform, int>();
            foreach (var kv in depthLayers)
            {
                int d = 0;
                for (var c = kv.Key.parent; c != null; c = c.parent) d++;
                depthOf[kv.Key] = d;
            }
            depthLayers.Sort((x, y) => depthOf[x.Key].CompareTo(depthOf[y.Key]));
        }

        // Depth above 100% spreads the layers apart without ever passing infinity: a background layer with speed s sits at
        // 1 - (1 - s)^Depth, foreground layers scale with Depth. Skies use Sky distance. Character pop is its own control.
        float EffectiveSpeed(float v)
        {
            if (float.IsNaN(v)) return SkyDepth;
            if (v >= PopMarker * 0.5f) return (v - PopMarker) * CharacterPop;
            if (v > 0f) return 1f - Mathf.Pow(1f - Mathf.Min(v, 1f), Separation);
            return v * Separation;
        }

        static bool HasTaken(Transform t, HashSet<Transform> taken)
        {
            for (var cur = t; cur != null; cur = cur.parent) if (taken.Contains(cur)) return true;
            return false;
        }

        internal string LayerSummary()
        {
            CollectLayers();
            return depthLayers.Count + " shifted, " + skyCount + " sky, " + popCount + " pop";
        }

        // Smooth motion. Cuphead moves players, enemies and bullets in FixedUpdate, 60 steps a second, and about 40 of
        // its scripts count steps rather than time, so stepping faster would speed the game up. Instead the game keeps
        // its 60 Hz steps, and each rendered frame draws what moved in the last step part way between its last two
        // positions (one step behind, like Unity's own rigidbody interpolation). Things moved in Update or LateUpdate
        // (camera, parallax, animation) are already smooth and are left alone: only a transform still exactly where the
        // last step left it is moved.
        class Track { public Transform t; public int depth; public Vector3 prev, curr; public int steps; }
        readonly List<Track> tracks = new List<Track>();
        readonly Dictionary<Transform, Track> trackOf = new Dictionary<Transform, Track>();
        int tracksFrame = -1000;
        internal int Smoothed, SmoothCandidates, RestoreErrors;

        IEnumerator SnapshotSteps()
        {
            var wait = new WaitForFixedUpdate();
            while (active)
            {
                yield return wait;
                if (Time.frameCount - tracksFrame >= 30) RefreshTracks();
                for (int i = 0; i < tracks.Count; i++)
                {
                    var k = tracks[i];
                    if (k.t == null) continue;
                    k.prev = k.steps > 0 ? k.curr : k.t.position;
                    k.curr = k.t.position;
                    k.steps++;
                }
            }
        }

        void RefreshTracks()
        {
            tracksFrame = Time.frameCount;
            var seen = new HashSet<Transform>();
            foreach (var r in FindObjectsOfType<Renderer>())
            {
                if (r == null || !r.enabled) continue;
                // The renderer and every parent: the player's motor moves a root that draws nothing itself.
                for (var t = r.transform; t != null; t = t.parent)
                {
                    if (!seen.Add(t)) break;
                    if (trackOf.ContainsKey(t)) continue;
                    int depth = 0;
                    for (var c = t.parent; c != null; c = c.parent) depth++;
                    var k = new Track { t = t, depth = depth };
                    trackOf[t] = k;
                    tracks.Add(k);
                }
            }
            tracks.RemoveAll(k => k.t == null || !seen.Contains(k.t));
            var dead = trackOf.Keys.Where(t => t == null || !seen.Contains(t)).ToList();
            foreach (var t in dead) trackOf.Remove(t);
            tracks.Sort((a, b) => a.depth.CompareTo(b.depth));  // parents first, so children land where they should
        }

        // Moves tracked things to their in-between place for this render. Returns what to put back.
        readonly List<KeyValuePair<Transform, Vector3>> smoothUndo = new List<KeyValuePair<Transform, Vector3>>();

        void ApplySmooth()
        {
            smoothUndo.Clear();
            Smoothed = 0;
            SmoothCandidates = tracks.Count;
            if (!Smooth || Time.fixedDeltaTime <= 0f) return;
            float alpha = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            for (int i = 0; i < tracks.Count; i++)
            {
                var k = tracks[i];
                if (k.t == null || k.steps < 2) continue;
                var now = k.t.position;
                if (now != k.curr || k.prev == k.curr) continue;  // moved outside the steps, or not moving
                if ((k.curr - k.prev).sqrMagnitude > 400f * 400f) continue;  // a jump, not motion
                smoothUndo.Add(new KeyValuePair<Transform, Vector3>(k.t, now));
                k.t.position = Vector3.Lerp(k.prev, k.curr, alpha);
                Smoothed++;
            }
        }

        void UndoSmooth()
        {
            for (int i = 0; i < smoothUndo.Count; i++) if (smoothUndo[i].Key != null) smoothUndo[i].Key.position = smoothUndo[i].Value;
            smoothUndo.Clear();
        }

        // Lab: how evenly the fastest tracked thing moves from rendered frame to rendered frame.
        int evenFrames;
        Transform evenTarget;
        readonly List<float> evenX = new List<float>();
        internal void MeasureEven(int frames) { evenFrames = frames; evenTarget = null; evenX.Clear(); }

        void SampleEven()
        {
            if (evenFrames <= 0) return;
            if (evenTarget == null)
            {
                float best = 0f;
                foreach (var k in tracks)
                {
                    if (k.t == null || k.steps < 2) continue;
                    float d = Mathf.Abs(k.curr.x - k.prev.x);
                    bool stepMoved = true;
                    if (stepMoved && d > best && d < 400f) { best = d; evenTarget = k.t; }
                }
                if (evenTarget == null) return;
                var tk = trackOf[evenTarget];
                Plugin.Log.LogInfo(string.Format(System.Globalization.CultureInfo.InvariantCulture, "EVEN target {0} now {1:F2} curr {2:F2} prev {3:F2} steps {4} smoothed now {5}",
                    evenTarget.name, evenTarget.position.x, tk.curr.x, tk.prev.x, tk.steps, smoothUndo.Any(u => u.Key == evenTarget)));
            }
            evenX.Add(evenTarget.position.x);
            if (--evenFrames > 0) return;
            var steps = new List<float>();
            for (int i = 1; i < evenX.Count; i++) steps.Add(Mathf.Abs(evenX[i] - evenX[i - 1]));
            float mean = steps.Count > 0 ? steps.Average() : 0f;
            float sd = steps.Count > 0 ? Mathf.Sqrt(steps.Select(v => (v - mean) * (v - mean)).Average()) : 0f;
            int zero = steps.Count(v => v < 0.01f);
            Plugin.Log.LogInfo(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "EVEN smooth {0} target {1}: {2} frames, step mean {3:F2} sd {4:F2} (sd/mean {5:F2}), frames with no motion {6}, smoothed {7} of {8}",
                Smooth, evenTarget != null ? evenTarget.name : "none", steps.Count, mean, sd, mean > 0 ? sd / mean : 0f, zero, Smoothed, SmoothCandidates));
        }

        static Camera WorldCamera()
        {
            foreach (var c in Camera.allCameras) if (c.GetComponent<AbstractCupheadGameCamera>() != null) return c;
            return null;
        }

        void RenderEyes(FrameInfo info)
        {
            if (info.viewsValid != 0 && !loggedPose)
            {
                loggedPose = true;
                Plugin.Log.LogInfo(string.Format("XRVIEW left pos ({0:F3},{1:F3},{2:F3}) fov L {3:F3} R {4:F3} U {5:F3} D {6:F3}; right pos ({7:F3},{8:F3},{9:F3})",
                    info.eye0.px, info.eye0.py, info.eye0.pz, info.eye0.left, info.eye0.right, info.eye0.up, info.eye0.down, info.eye1.px, info.eye1.py, info.eye1.pz));
            }
            CollectLayers();
            var world = WorldCamera();
            float viewWidth = world != null ? world.orthographicSize * 2f * 16f / 9f : 1280f;
            float unitsPerMetre = viewWidth / StageWidth;
            bool checking = eyeCheckFrames > 0;
            bool compareNow = eyeCheckFrames == 1;
            if (eyeCheckFrames > 0) eyeCheckFrames--;
            var cams = Camera.allCameras.OrderBy(c => c.depth).ToArray();
            // Lab check that every render puts the game back exactly where it was.
            var before = new Vector3[depthLayers.Count];
            for (int i = 0; i < depthLayers.Count; i++) before[i] = depthLayers[i].Key != null ? depthLayers[i].Key.position : Vector3.zero;
            // Smooth first, so the depth shifts below start from the in-between places and undo back to them.
            ApplySmooth();
            SampleEven();
            var saved = new Vector3[depthLayers.Count];
            for (int i = 0; i < depthLayers.Count; i++) saved[i] = depthLayers[i].Key != null ? depthLayers[i].Key.position : Vector3.zero;
            Vector3 worldSaved = world != null ? world.transform.position : Vector3.zero;
            // Floating window: the nearest thing on the stage, as a speed relative to the stage frame (pop-out included).
            // A layer with relative speed f moves by shift * f in an eye's image, so near the edges each eye would show
            // a strip of picture the other eye does not have. Each eye's outer edge is trimmed by that much, so both
            // eyes see the same picture at the edges and the frame itself sits at the depth of the nearest layer.
            float nearest = -PopOut;
            if (EdgeTrim)
                for (int i = 0; i < depthLayers.Count; i++)
                    if (depthLayers[i].Key != null) nearest = Mathf.Min(nearest, EffectiveSpeed(depthLayers[i].Value) - PopOut);
            if (!EdgeTrim) nearest = 0f;

            for (int eye = 0; eye < 2; eye++)
            {
                var e = eye == 0 ? info.eye0 : info.eye1;
                // Without valid tracking, stand at the stage's normal viewing spot with a fixed eye gap.
                var local = checking ? Vector3.zero : info.viewsValid != 0
                    ? Quaternion.Inverse(viewTurn) * (new Vector3(e.px, e.py, e.pz) - viewPoint)
                    : new Vector3(eye == 0 ? -0.0315f : 0.0315f, 0f, 0f);
                var shift = new Vector2(local.x, local.y) * unitsPerMetre;
                for (int i = 0; i < depthLayers.Count; i++)
                {
                    var t = depthLayers[i].Key;
                    if (t == null) continue;
                    float s = EffectiveSpeed(depthLayers[i].Value);
                    t.position = saved[i] + new Vector3(shift.x * s, shift.y * s, 0f);
                }
                // Pop-out moves the zero-depth plane back: the whole world shifts toward you, the HUD stays.
                if (world != null) world.transform.position = worldSaved + new Vector3(shift.x * PopOut, shift.y * PopOut, 0f);
                RenderStack(cams, eyeRender[eye]);
                TrimEdges(eyeRender[eye], shift * nearest / viewWidth, eye);
                Finish(eye);
            }
            for (int i = 0; i < depthLayers.Count; i++) if (depthLayers[i].Key != null) depthLayers[i].Key.position = saved[i];
            if (world != null) world.transform.position = worldSaved;
            UndoSmooth();
            for (int i = 0; i < depthLayers.Count; i++)
                if (depthLayers[i].Key != null && (depthLayers[i].Key.position - before[i]).sqrMagnitude > 1e-6f) RestoreErrors++;
            renderedPairs++;
            if (wantShot) { wantShot = false; SaveEyes(); }
            if (compareNow) CompareEyes();
        }

        internal bool EdgeTrim = true;
        internal readonly float[] TrimShare = new float[4];  // last trims as a share of the width or height: left eye x, y, right eye x, y

        // d is how far the nearest layer moves in this eye, as a share of the view (x of the width, y of the height
        // after aspect). Moving right (d.x > 0) shows extra picture at the left edge, so the left edge is covered.
        void TrimEdges(RenderTexture target, Vector2 d, int eye)
        {
            float dx = d.x, dy = d.y * 16f / 9f;
            TrimShare[eye * 2] = dx;
            TrimShare[eye * 2 + 1] = dy;
            if (Mathf.Abs(dx) < 1e-5f && Mathf.Abs(dy) < 1e-5f) return;
            var old = RenderTexture.active;
            RenderTexture.active = target;
            GL.PushMatrix();
            GL.LoadOrtho();
            copyMat.mainTexture = Texture2D.blackTexture;
            copyMat.SetPass(0);
            GL.Begin(GL.QUADS);
            if (dx > 0f) Quad(0f, 0f, dx, 1f); else if (dx < 0f) Quad(1f + dx, 0f, 1f, 1f);
            if (dy > 0f) Quad(0f, 0f, 1f, dy); else if (dy < 0f) Quad(0f, 1f + dy, 1f, 1f);
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = old;
        }

        static void Quad(float x0, float y0, float x1, float y1)
        {
            GL.TexCoord2(0f, 0f); GL.Vertex3(x0, y0, 0f);
            GL.TexCoord2(0f, 1f); GL.Vertex3(x0, y1, 0f);
            GL.TexCoord2(1f, 1f); GL.Vertex3(x1, y1, 0f);
            GL.TexCoord2(1f, 0f); GL.Vertex3(x1, y0, 0f);
        }

        static void RenderStack(Camera[] cams, RenderTexture target)
        {
            var old = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, Color.black);
            foreach (var cam in cams)
            {
                if (cam == null || !cam.enabled) continue;
                var prev = cam.targetTexture;
                cam.targetTexture = target;
                cam.Render();
                cam.targetTexture = prev;
            }
            RenderTexture.active = old;
        }

        // Copies the eye image to the texture the native side sends on, upside down if asked, with the frame number.
        void Finish(int i)
        {
            var old = RenderTexture.active;
            RenderTexture.active = eyeOut[i];
            copyMat.mainTexture = eyeRender[i];
            GL.PushMatrix();
            GL.LoadOrtho();
            copyMat.SetPass(0);
            float top = FlipY ? 0f : 1f, bottom = 1f - top;
            GL.Begin(GL.QUADS);
            GL.TexCoord2(0f, bottom); GL.Vertex3(0f, 0f, 0f);
            GL.TexCoord2(0f, top); GL.Vertex3(0f, 1f, 0f);
            GL.TexCoord2(1f, top); GL.Vertex3(1f, 1f, 0f);
            GL.TexCoord2(1f, bottom); GL.Vertex3(1f, 0f, 0f);
            GL.End();
            if (Stamp)
            {
                // 32 blocks, least significant bit first, along the image's lower left edge.
                int frame = Time.frameCount;
                for (int b = 0; b < 32; b++) stampTex.SetPixel(b, 0, ((frame >> b) & 1) != 0 ? Color.white : Color.black);
                stampTex.Apply();
                copyMat.mainTexture = stampTex;
                copyMat.SetPass(0);
                float w = 32f * 12f / eyeOut[i].width, h = 12f / eyeOut[i].height;
                GL.Begin(GL.QUADS);
                GL.TexCoord2(0f, 0f); GL.Vertex3(0f, 0f, 0f);
                GL.TexCoord2(0f, 1f); GL.Vertex3(0f, h, 0f);
                GL.TexCoord2(1f, 1f); GL.Vertex3(w, h, 0f);
                GL.TexCoord2(1f, 0f); GL.Vertex3(w, 0f, 0f);
                GL.End();
            }
            GL.PopMatrix();
            RenderTexture.active = old;
        }

        Color32[] ReadEye(int i, string file)
        {
            var old = RenderTexture.active;
            RenderTexture.active = eyeOut[i];
            var tex = new Texture2D(eyeOut[i].width, eyeOut[i].height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            if (file != null)
            {
                Directory.CreateDirectory(ShotDir);
                File.WriteAllBytes(Path.Combine(ShotDir, file), tex.EncodeToPNG());
            }
            var px = tex.GetPixels32();
            Destroy(tex);
            return px;
        }

        void CompareEyes()
        {
            var a = ReadEye(0, "check_0.png");
            var b = ReadEye(1, "check_1.png");
            int differing = 0, max = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int d = Math.Max(Math.Abs(a[i].r - b[i].r), Math.Max(Math.Abs(a[i].g - b[i].g), Math.Abs(a[i].b - b[i].b)));
                if (d > 0) differing++;
                if (d > max) max = d;
            }
            Plugin.Log.LogInfo(string.Format("EYECHECK frame {0}: differing pixels {1} of {2}, max {3}", Time.frameCount, differing, a.Length, max));
        }

        void SaveEyes()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            var name = scene + "_f" + Time.frameCount;
            ReadEye(0, name + "_L.png");
            ReadEye(1, name + "_R.png");
            Plugin.Log.LogInfo(string.Format(System.Globalization.CultureInfo.InvariantCulture, "TRIM left eye x {0:F4} y {1:F4}, right eye x {2:F4} y {3:F4}", TrimShare[0], TrimShare[1], TrimShare[2], TrimShare[3]));
            Plugin.Log.LogInfo("XRSHOT " + name + " (" + LayerSummary() + ", depth " + Separation + ", sky " + SkyDepth + ", pop-out " + PopOut + ")");
        }
    }
}
