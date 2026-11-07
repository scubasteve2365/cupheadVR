using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CupheadVR
{
    // Read-only probes. Lab only.
    internal class Probes : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        internal void Probe()
        {
            Plugin.Log.LogInfo(string.Format(Inv,
                "PROBE frame {0} time {1:F2} scene {2} sceneLoader {3} level {4} dataInit {5} slot {6} inGame {7} fixedDelta {8:F5} maxDelta {9:F5} targetFps {10} vsync {11} screen {12}x{13} timeScale {14}",
                Time.frameCount, Time.time, SceneManager.GetActiveScene().name, SceneLoader.SceneName, SceneLoader.CurrentLevel,
                PlayerData.Initialized, PlayerData.CurrentSaveFileIndex, PlayerData.inGame, Time.fixedDeltaTime, Time.maximumDeltaTime,
                Application.targetFrameRate, QualitySettings.vSyncCount, Screen.width, Screen.height, Time.timeScale));
            foreach (var cam in Camera.allCameras.OrderBy(c => c.depth)) Plugin.Log.LogInfo(CameraLine(cam));
        }

        static string CameraLine(Camera cam)
        {
            var p = cam.transform.position;
            var comps = string.Join(",", cam.GetComponents<Behaviour>().Where(b => !(b is Camera)).Select(b => b.GetType().Name + (b.enabled ? "" : "(off)")).ToArray());
            return string.Format(Inv,
                "CAM {0} depth {1} ortho {2} size {3:F3} fov {4:F2} near {5} far {6} pos ({7:F3},{8:F3},{9:F3}) rot {10} rect {11} mask {12} clear {13} target {14} comps [{15}]",
                cam.name, cam.depth, cam.orthographic, cam.orthographicSize, cam.fieldOfView, cam.nearClipPlane, cam.farClipPlane,
                p.x, p.y, p.z, cam.transform.rotation.eulerAngles, cam.rect, MaskNames(cam.cullingMask), cam.clearFlags,
                cam.targetTexture != null ? cam.targetTexture.name : "screen", comps);
        }

        static string MaskNames(int mask)
        {
            var names = new List<string>();
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                var n = LayerMask.LayerToName(i);
                names.Add(string.IsNullOrEmpty(n) ? i.ToString(Inv) : n);
            }
            return names.Count == 32 ? "all" : string.Join("|", names.ToArray());
        }

        // One CSV row per renderer, plus camera and canvas lines in the log.
        internal void Dump(string tag)
        {
            var scene = SceneManager.GetActiveScene().name;
            var file = System.IO.Path.Combine(Plugin.OutDir, scene + "_" + tag + "_f" + Time.frameCount + ".csv");
            Probe();
            foreach (var canvas in FindObjectsOfType<Canvas>())
            {
                if (!canvas.isRootCanvas) continue;
                Plugin.Log.LogInfo(string.Format(Inv, "CANVAS {0} mode {1} camera {2} planeDist {3} sortingLayer {4} order {5} layer {6} active {7}",
                    Path(canvas.transform), canvas.renderMode, canvas.worldCamera != null ? canvas.worldCamera.name : "none", canvas.planeDistance,
                    canvas.sortingLayerName, canvas.sortingOrder, LayerMask.LayerToName(canvas.gameObject.layer), canvas.isActiveAndEnabled));
            }
            var sb = new StringBuilder("path,type,active,visible,layer,sortingLayer,sortingLayerValue,sortingOrder,x,y,z,sizeX,sizeY,parallax,parallaxValue,parallaxExtra,shader\n");
            int rows = 0;
            foreach (var r in FindObjectsOfType<Renderer>())
            {
                var t = r.transform;
                var pos = t.position;
                var size = r.bounds.size;
                string pType, pExtra;
                float pValue;
                FindParallax(t, out pType, out pValue, out pExtra);
                var mat = r.sharedMaterial;
                sb.Append(Csv(Path(t))).Append(',').Append(r.GetType().Name).Append(',')
                  .Append(r.enabled && r.gameObject.activeInHierarchy ? 1 : 0).Append(',').Append(r.isVisible ? 1 : 0).Append(',')
                  .Append(Csv(LayerMask.LayerToName(r.gameObject.layer))).Append(',').Append(Csv(r.sortingLayerName)).Append(',')
                  .Append(SortingLayer.GetLayerValueFromID(r.sortingLayerID)).Append(',').Append(r.sortingOrder).Append(',')
                  .Append(pos.x.ToString("F3", Inv)).Append(',').Append(pos.y.ToString("F3", Inv)).Append(',').Append(pos.z.ToString("F3", Inv)).Append(',')
                  .Append(size.x.ToString("F2", Inv)).Append(',').Append(size.y.ToString("F2", Inv)).Append(',')
                  .Append(pType).Append(',').Append(pValue.ToString("F4", Inv)).Append(',').Append(Csv(pExtra)).Append(',')
                  .Append(Csv(mat != null && mat.shader != null ? mat.shader.name : "")).Append('\n');
                rows++;
            }
            var layers = string.Join(" ", SortingLayer.layers.Select(l => l.name + "=" + l.value).ToArray());
            File.WriteAllText(file, sb.ToString());
            Plugin.Log.LogInfo("SORTINGLAYERS " + layers);
            Plugin.Log.LogInfo("DUMP " + rows + " renderers to " + file);
        }

        static void FindParallax(Transform t, out string type, out float value, out string extra)
        {
            for (var cur = t; cur != null; cur = cur.parent)
            {
                foreach (var c in cur.GetComponents<MonoBehaviour>())
                {
                    if (c == null) continue;
                    var name = c.GetType().Name;
                    if (name.IndexOf("Parallax", StringComparison.Ordinal) < 0) continue;
                    type = name;
                    extra = cur == t ? "self" : "parent:" + cur.name;
                    value = float.NaN;
                    var pl = c as PlatformingLevelParallax;
                    if (pl != null)
                    {
                        var props = Traverse.Create(pl).Property("LayerProperties");
                        object speed = props.GetValue() != null ? props.Field("speed").GetValue() : null;
                        value = speed is float ? (float)speed : float.NaN;
                        extra += " side:" + pl.Side + " layer:" + pl.Layer;
                        return;
                    }
                    var plx = c as ParallaxLayer;
                    if (plx != null)
                    {
                        value = plx.percentage;
                        extra += " mode:" + plx.type;
                        return;
                    }
                    var f = Traverse.Create(c).Field("percentage");
                    if (f.FieldExists()) value = Convert.ToSingle(f.GetValue(), Inv);
                    return;
                }
            }
            type = "none"; value = 0f; extra = "";
        }

        static string Path(Transform t)
        {
            var parts = new List<string>();
            for (var cur = t; cur != null; cur = cur.parent) parts.Add(cur.name);
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        static string Csv(string s)
        {
            if (s == null) return "";
            return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        // Zero-difference control: re-render every enabled camera, in depth order, into an off-screen target with the game's own
        // settings, and compare with the frame the game just showed. Then render the stack a second time and compare the two
        // re-renders (the eye check: the same view rendered twice in one frame must match).
        internal void StartZeroDiff(string tag) { StartCoroutine(ZeroDiff(tag)); }

        IEnumerator ZeroDiff(string tag)
        {
            yield return new WaitForEndOfFrame();
            int w = Screen.width, h = Screen.height;
            int frame = Time.frameCount;
            var game = new Texture2D(w, h, TextureFormat.RGB24, false);
            game.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            game.Apply();
            var a = RenderStack(w, h);
            var b = RenderStack(w, h);
            var scene = SceneManager.GetActiveScene().name;
            Report("ZERODIFF game-vs-rerender", game, a, scene + "_" + tag + "_f" + frame);
            Report("ZERODIFF rerender-vs-rerender", a, b, null);
            File.WriteAllBytes(Path2(scene + "_" + tag + "_f" + frame + "_game.png"), game.EncodeToPNG());
            File.WriteAllBytes(Path2(scene + "_" + tag + "_f" + frame + "_rerender.png"), a.EncodeToPNG());
            Destroy(game); Destroy(a); Destroy(b);
        }

        static string Path2(string name) { return System.IO.Path.Combine(Plugin.OutDir, name); }

        static Texture2D RenderStack(int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.black);
            foreach (var cam in Camera.allCameras.OrderBy(c => c.depth))
            {
                var prev = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prev;
            }
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        static void Report(string what, Texture2D x, Texture2D y, string maskName)
        {
            var px = x.GetPixels32();
            var py = y.GetPixels32();
            int w = x.width, diff = 0, maxd = 0, minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            var mask = maskName != null ? new Color32[px.Length] : null;
            for (int i = 0; i < px.Length; i++)
            {
                int d = Math.Max(Math.Abs(px[i].r - py[i].r), Math.Max(Math.Abs(px[i].g - py[i].g), Math.Abs(px[i].b - py[i].b)));
                if (mask != null) mask[i] = d == 0 ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255);
                if (d == 0) continue;
                diff++;
                if (d > maxd) maxd = d;
                int xx = i % w, yy = i / w;
                minX = Math.Min(minX, xx); maxX = Math.Max(maxX, xx); minY = Math.Min(minY, yy); maxY = Math.Max(maxY, yy);
            }
            Plugin.Log.LogInfo(string.Format(Inv, "{0} frame {1} differing {2} of {3} maxdiff {4} box x {5}..{6} y {7}..{8}",
                what, Time.frameCount, diff, px.Length, maxd, diff > 0 ? minX : 0, maxX, diff > 0 ? minY : 0, maxY));
            if (mask != null)
            {
                var m = new Texture2D(x.width, x.height, TextureFormat.RGB24, false);
                m.SetPixels32(mask);
                m.Apply();
                File.WriteAllBytes(Path2(maskName + "_diffmask.png"), m.EncodeToPNG());
                Destroy(m);
            }
        }

        // Stereo test shot, no OpenXR yet. The game's parallax speed s says how far a layer is: s = 0 moves with the world (the
        // play plane), s = 1 moves with the camera (infinitely far), s < 0 is in front. A camera at distance d sees a layer at
        // depth d/(1-s), so per eye a layer shifts by s times the eye shift. Camera and HUD stay put (zero parallax on the play
        // plane). sep is the far-layer (s = 1) disparity as a share of the view width. Saves left, right, side by side.
        internal void StartStereoShot(float sep, string tag) { StartCoroutine(StereoShot(sep, tag)); }

        IEnumerator StereoShot(float sep, string tag)
        {
            yield return new WaitForEndOfFrame();
            int w = Screen.width, h = Screen.height, frame = Time.frameCount;
            var layers = new List<KeyValuePair<Transform, float>>();
            foreach (var p in FindObjectsOfType<PlatformingLevelParallax>())
            {
                var props = Traverse.Create(p).Property("LayerProperties");
                object speed = props.GetValue() != null ? props.Field("speed").GetValue() : null;
                if (speed is float) layers.Add(new KeyValuePair<Transform, float>(p.transform, (float)speed));
            }
            foreach (var p in FindObjectsOfType<ParallaxLayer>()) layers.Add(new KeyValuePair<Transform, float>(p.transform, p.percentage));
            foreach (var p in FindObjectsOfType<MapParallaxLayer>()) layers.Add(new KeyValuePair<Transform, float>(p.transform, p.percentage));
            Camera main = null;
            foreach (var c in Camera.allCameras) if (c.GetComponent<AbstractCupheadGameCamera>() != null) main = c;
            float viewWidth = main != null ? main.orthographicSize * 2f * 16f / 9f : 1280f;
            float e = sep * viewWidth * 0.5f;
            var eyes = new Texture2D[2];
            for (int eye = 0; eye < 2; eye++)
            {
                float sign = eye == 0 ? -1f : 1f;
                var saved = new Vector3[layers.Count];
                for (int i = 0; i < layers.Count; i++)
                {
                    saved[i] = layers[i].Key.position;
                    layers[i].Key.position = saved[i] + new Vector3(sign * e * layers[i].Value, 0f, 0f);
                }
                eyes[eye] = RenderStack(w, h);
                for (int i = 0; i < layers.Count; i++) layers[i].Key.position = saved[i];
            }
            var name = SceneManager.GetActiveScene().name + "_" + tag + "_f" + frame;
            File.WriteAllBytes(Path2(name + "_L.png"), eyes[0].EncodeToPNG());
            File.WriteAllBytes(Path2(name + "_R.png"), eyes[1].EncodeToPNG());
            Plugin.Log.LogInfo(string.Format(Inv, "STEREO frame {0} layers {1} sep {2} eyeShift {3:F2} world units of view width {4:F1} files {5}_L/R.png",
                frame, layers.Count, sep, e, viewWidth, name));
            Destroy(eyes[0]); Destroy(eyes[1]);
        }

        // Game speed test: player one's position over a few seconds (run distance, highest point).
        internal void Track(float seconds) { StartCoroutine(TrackRoutine(seconds)); }

        IEnumerator TrackRoutine(float seconds)
        {
            var p = PlayerManager.GetPlayer(PlayerId.PlayerOne);
            if (p == null) { Plugin.Log.LogWarning("track: no player"); yield break; }
            var start = p.transform.position;
            float t0 = Time.realtimeSinceStartup, maxY = start.y;
            int frames = 0, f0 = Time.frameCount;
            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                yield return null;
                if (p == null) yield break;
                maxY = Mathf.Max(maxY, p.transform.position.y);
                frames++;
            }
            var end = p.transform.position;
            float span = Time.realtimeSinceStartup - t0;
            Plugin.Log.LogInfo(string.Format(Inv, "TRACK {0:F2} s real, {1} frames ({2:F1} fps), fixed {3:F5}: dx {4:F1} ({5:F1}/s) rise {6:F1}",
                span, frames, frames / span, Time.fixedDeltaTime, end.x - start.x, (end.x - start.x) / span, maxY - start.y));
        }

        internal void SaveTexture(RenderTexture rt, string name)
        {
            if (rt == null) { Plugin.Log.LogWarning("no texture to save"); return; }
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            File.WriteAllBytes(Path2(name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            Plugin.Log.LogInfo("TEXSHOT " + name);
        }

        internal void EnterSlot(int slot)
        {
            PlayerData.CurrentSaveFileIndex = slot;
            PlayerManager.player1IsMugman = PlayerData.GetDataForSlot(slot).isPlayer1Mugman;
            Level.ResetPreviousLevelInfo();
            PlayerData.inGame = true;
            Plugin.Log.LogInfo("SLOT " + slot + " map " + PlayerData.Data.CurrentMap);
            SceneLoader.LoadScene(PlayerData.Data.CurrentMap, SceneLoader.Transition.Fade, SceneLoader.Transition.Iris);
        }

        internal void LoadLevel(string name)
        {
            var level = (Levels)Enum.Parse(typeof(Levels), name, true);
            PlayerData.inGame = true;
            Plugin.Log.LogInfo("LEVEL " + level);
            SceneLoader.LoadLevel(level, SceneLoader.Transition.Fade);
        }

        internal void LoadScene(string name)
        {
            var scene = (Scenes)Enum.Parse(typeof(Scenes), name, true);
            PlayerData.inGame = true;
            Plugin.Log.LogInfo("SCENE request " + scene);
            SceneLoader.LoadScene(scene, SceneLoader.Transition.Fade, SceneLoader.Transition.Iris);
        }
    }
}
