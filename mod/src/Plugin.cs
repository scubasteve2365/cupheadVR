using System;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CupheadVR
{
    // CupheadVR 1.0: the game on a stage in front of a seated player, in native stereo with depth from the game's own parallax
    // layers, smooth motion at any headset rate, and an in-headset menu (hold both sticks). VR starts by itself unless
    // VR.AutoStart is off; if OpenXR cannot start, the game stays flat. Lab probes stay available behind Lab.Commands.
    [BepInPlugin("com.scubasteve2365.cupheadvr", "CupheadVR", "1.0.2")]
    public class Plugin : BaseUnityPlugin
    {
        internal const string Version = "1.02";
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ConfigEntry<bool> BlockSaves;
        internal static ConfigEntry<float> Grain, Fringe;
        internal static string OutDir;
        internal static bool RigActive { get { return instance != null && instance.rig.Active; } }
        static Plugin instance;

        // Defaults are Steve's own settings from his headset tests (RigA, 2026-11-07).
        const float DefaultWidth = 2.15f, DefaultDistance = 1.25f, DefaultDepth = 1.3f, DefaultSky = 0.75f, DefaultSharpen = 1.0f,
            DefaultPop = 1.2f, DefaultPopOut = 0.75f, DefaultGrain = 0f, DefaultFringe = 0f;
        ConfigEntry<bool> labCommands, autoDump, autoStart, smooth, edgeMatch;
        ConfigEntry<float> stageWidth, stageDistance, depth, popOut, skyDepth, sharpen, resolution, characterPop, lean;
        ConfigEntry<int> clickVolume;
        ConfigEntry<string> picture;
        string commandFile;
        int nextCommandCheck;
        float dumpAt = -1f;
        bool triedAutoStart;
        Probes probes;
        XrRig rig;
        MenuUI menu;

        void Awake()
        {
            Log = Logger;
            instance = this;
            labCommands = Config.Bind("Lab", "Commands", false, "Developer test commands read from vrlab_command.txt in the game folder. Leave off.");
            autoDump = Config.Bind("Lab", "AutoDump", false, "Dump cameras and renderers 4 seconds after each scene loads.");
            BlockSaves = Config.Bind("Lab", "BlockSaves", false, "Never write save files. Lab only: with this on, progress is lost.");
            autoStart = Config.Bind("VR", "AutoStart", true, "Start VR by itself once the game is running.");
            stageWidth = Config.Bind("VR", "StageWidth", DefaultWidth, "Width of the stage in metres.");
            stageDistance = Config.Bind("VR", "StageDistance", DefaultDistance, "Distance to the stage in metres.");
            depth = Config.Bind("VR", "LayerDepth", DefaultDepth, "How far apart the background layers spread. 1 is the game's own spacing; higher spreads them without passing the horizon.");
            popOut = Config.Bind("VR", "PopOut", DefaultPopOut, "0 keeps the play plane on the stage. Higher brings the scene toward you.");
            characterPop = Config.Bind("VR", "CharacterPop", DefaultPop, "Lifts characters, bullets and effects off the play plane by the game's own drawing layers. 0 is flat.");
            lean = Config.Bind("VR", "LeanParallax", 0f, "How much leaning your head moves the layers against each other. 0 keeps Cuphead on his ground whatever your head does.");
            skyDepth = Config.Bind("VR", "SkyDepth", DefaultSky, "How far back skies without their own parallax sit, 0 on the stage, 1 at the horizon.");
            smooth = Config.Bind("VR", "SmoothMotion", true, "Draw moving things between the game's 60 Hz steps, so motion is smooth at any headset rate. Game speed does not change.");
            edgeMatch = Config.Bind("VR", "EdgeMatch", true, "Trim each eye's outer edge so both eyes see the same picture at the stage edges.");
            sharpen = Config.Bind("VR", "Sharpen", DefaultSharpen, "Strength of the sharpening pass, 0 is off.");
            resolution = Config.Bind("VR", "ResolutionScale", 1.0f, "1 matches the headset's pixel density across the stage.");
            Grain = Config.Bind("Picture", "FilmGrain", DefaultGrain, "Film grain and scratches, 1 is the game's own amount, 0 is off.");
            Fringe = Config.Bind("Picture", "ColorFringe", DefaultFringe, "The game's colour fringing at the edges, 1 is the game's own amount, 0 is off.");
            picture = Config.Bind("Picture", "Picture", "Auto", "How the stage reaches the headset: Auto, Steady or Panel. Auto uses Steady on Virtual Desktop and Panel on everything else.");
            clickVolume = Config.Bind("Menu", "ClickVolume", 2, "Menu click sounds: 0 off, 1 low, 2 medium, 3 high.");
            commandFile = Path.Combine(Paths.GameRootPath, "vrlab_command.txt");
            OutDir = Path.Combine(Paths.GameRootPath, "vrlab_dumps");
            probes = gameObject.AddComponent<Probes>();
            rig = gameObject.AddComponent<XrRig>();
            rig.PluginDir = Path.GetDirectoryName(Info.Location);
            rig.ShotDir = Path.Combine(Paths.GameRootPath, "vrlab_shots");
            menu = gameObject.AddComponent<MenuUI>();
            menu.Version = "CUPHEAD VR " + Version;
            menu.Volume = () => clickVolume.Value;
            menu.Closed = () => { rig.ApplyStage(); rig.UpdateResolution(); };
            BuildMenu();
            rig.Menu = menu;
            PushSettings();
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                Log.LogInfo("SCENE loaded " + scene.name + " mode " + mode + " frame " + Time.frameCount);
                if (autoDump.Value) dumpAt = Time.realtimeSinceStartup + 4f;
            };
            new Harmony("com.scubasteve2365.cupheadvr").PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("CupheadVR " + Version + " loaded. Unity " + Application.unityVersion + ", pointer size " + IntPtr.Size);
        }

        void Update()
        {
            if (Time.frameCount >= nextCommandCheck)
            {
                nextCommandCheck = Time.frameCount + 15;
                ReadCommands();
            }
            if (dumpAt > 0f && Time.realtimeSinceStartup >= dumpAt)
            {
                dumpAt = -1f;
                probes.Dump("auto");
            }
            if (!triedAutoStart && autoStart.Value && Time.frameCount >= 120)
            {
                triedAutoStart = true;
                rig.Begin();
            }
        }

        // ---- menu ----

        static string Pct(float v) { return Mathf.RoundToInt(v * 100f) + "%"; }
        static string M(float v) { return v.ToString("F2", CultureInfo.InvariantCulture) + " m"; }

        MenuUI.Row Value(string label, ConfigEntry<float> e, float step, float min, float max, Func<string> show, string help)
        {
            return new MenuUI.Row
            {
                Label = label,
                Value = show,
                Change = dir => { e.Value = Mathf.Clamp((float)Math.Round(e.Value + dir * step, 3), min, max); PushSettings(); LogSettings(); },
                Help = () => help,
            };
        }

        void BuildMenu()
        {
            var stage = new MenuUI.Tab { Name = "Stage" };
            stage.Rows.Add(new MenuUI.Row { Label = "Recenter", Press = () => { rig.Recenter(); Log.LogInfo("MENU recenter"); },
                Help = () => "Press A to put the stage straight ahead of where you face now. SteamVR's own recenter does the same." });
            stage.Rows.Add(Value("Stage distance", stageDistance, 0.05f, 0.5f, 10f, () => M(stageDistance.Value), "How far away the stage stands. The picture keeps its depth; only where it sits changes."));
            stage.Rows.Add(Value("Stage size", stageWidth, 0.05f, 0.5f, 10f, () => M(stageWidth.Value) + " wide", "How wide the stage is. Bigger is like sitting closer to a cinema screen."));
            stage.Rows.Add(new MenuUI.Row { Label = "Reset the stage", Press = () => { stageDistance.Value = DefaultDistance; stageWidth.Value = DefaultWidth; PushSettings(); rig.Recenter(); LogSettings(); },
                Help = () => "Press A to put the stage back to 2.15 m wide at 1.25 m, straight ahead." });

            var depthTab = new MenuUI.Tab { Name = "Depth" };
            depthTab.Rows.Add(Value("Layer depth", depth, 0.1f, 0.5f, 3f, () => Pct(depth.Value), "How far apart the background layers spread. 100% is the game's own spacing. Higher pushes the middle layers further back, but never past the horizon."));
            depthTab.Rows.Add(Value("Character pop", characterPop, 0.1f, 0f, 3f, () => Pct(characterPop.Value), "Lifts Cuphead, enemies, bullets and effects off the ground, using the game's own drawing order, so they stand out like cut-outs. 0% keeps them flat."));
            depthTab.Rows.Add(Value("Pop-out", popOut, 0.05f, 0f, 1f, () => Pct(popOut.Value), "Brings the whole scene toward you, so the play area floats in front of the stage and the background sits on it."));
            depthTab.Rows.Add(new MenuUI.Row { Label = "Edge match", Value = () => edgeMatch.Value ? "On" : "Off",
                Change = dir => { edgeMatch.Value = !edgeMatch.Value; PushSettings(); LogSettings(); },
                Help = () => edgeMatch.Value ? "On: each eye's outer edge is trimmed, so both eyes see the same picture at the stage edges. The stage frame then floats at the depth of the nearest layer."
                                             : "Off: at the stage edges each eye sees a strip of picture the other does not, which can look odd with a lot of pop-out." });
            depthTab.Rows.Add(Value("Lean parallax", lean, 0.1f, 0f, 1f, () => Pct(lean.Value), "How much leaning your head slides the layers against each other, like a real diorama. 0% keeps Cuphead lined up with the ground and his hits wherever your head is."));
            depthTab.Rows.Add(Value("Sky distance", skyDepth, 0.05f, 0f, 1f, () => Pct(skyDepth.Value), "How far back painted skies sit when the game gives them no layer of their own. 100% is the horizon."));

            var pictureTab = new MenuUI.Tab { Name = "Picture" };
            pictureTab.Rows.Add(new MenuUI.Row { Label = "Picture", Value = PictureText, Change = CyclePicture, Help = PictureHelp });
            pictureTab.Rows.Add(Value("Film grain", Grain, 0.1f, 0f, 1.5f, () => Pct(Grain.Value), "The old film grain, dust and scratches. 100% is how the game looks. Lower is cleaner in the headset."));
            pictureTab.Rows.Add(Value("Colour fringe", Fringe, 0.1f, 0f, 1.5f, () => Pct(Fringe.Value), "The red and blue fringes the game adds around the picture's edges. 100% is how the game looks."));
            pictureTab.Rows.Add(Value("Sharpen", sharpen, 0.1f, 0f, 1f, () => Pct(sharpen.Value), "Crisper line art. Too high makes edges glow."));
            pictureTab.Rows.Add(Value("Resolution", resolution, 0.1f, 0.5f, 1.5f, () => Pct(resolution.Value) + "  (" + rig.WantedWidth() + " px)", "100% matches your headset's sharpness across the stage. The new size takes effect when the menu closes."));
            pictureTab.Rows.Add(new MenuUI.Row { Label = "Smooth motion", Value = () => smooth.Value ? "On" : "Off",
                Change = dir => { smooth.Value = !smooth.Value; PushSettings(); LogSettings(); },
                Help = () => smooth.Value ? "On: things move smoothly at your headset's refresh rate. The game itself still runs at its normal speed."
                                          : "Off: things move in the game's own 60 steps a second, which can look juddery above 60 Hz." });

            var options = new MenuUI.Tab { Name = "Options" };
            options.Rows.Add(new MenuUI.Row { Label = "Click sounds", Value = () => new[] { "Off", "Low", "Medium", "High" }[Mathf.Clamp(clickVolume.Value, 0, 3)],
                Change = dir => clickVolume.Value = Mathf.Clamp(clickVolume.Value + dir, 0, 3), Help = () => "How loud the menu clicks are." });
            options.Rows.Add(new MenuUI.Row { Label = "Reset everything", Press = ResetAll, Help = () => "Press A to put every setting back to its default." });
            options.Rows.Add(new MenuUI.Row { Label = "Close", Press = () => menu.Close(), Help = () => "Close the menu. B or holding both sticks also closes it." });
            menu.Tabs.Add(stage);
            menu.Tabs.Add(depthTab);
            menu.Tabs.Add(pictureTab);
            menu.Tabs.Add(options);
        }

        static int PictureCode(string value) { return value == "Steady" ? 1 : value == "Panel" ? 2 : 0; }

        string PictureText()
        {
            return picture.Value == "Steady" || picture.Value == "Panel" ? picture.Value : "Auto (" + rig.ActiveLayerName + ")";
        }

        string PictureHelp()
        {
            return "Auto uses Steady on Virtual Desktop and Panel on everything else. Steady holds still when you turn your head on Virtual Desktop. Panel can look a little sharper."
                 + (rig.RuntimeName.Length > 0 ? " Detected: " + rig.RuntimeName + "." : "");
        }

        void CyclePicture(int dir)
        {
            var order = new[] { "Auto", "Steady", "Panel" };
            int i = Math.Max(0, Array.IndexOf(order, picture.Value));
            picture.Value = order[(i + dir + order.Length) % order.Length];
            PushSettings();
            rig.LogPicture();
        }

        void ResetAll()
        {
            stageDistance.Value = DefaultDistance;
            stageWidth.Value = DefaultWidth;
            depth.Value = DefaultDepth;
            popOut.Value = DefaultPopOut;
            characterPop.Value = DefaultPop;
            lean.Value = 0f;
            skyDepth.Value = DefaultSky;
            sharpen.Value = DefaultSharpen;
            resolution.Value = 1f;
            smooth.Value = true;
            edgeMatch.Value = true;
            picture.Value = "Auto";
            Grain.Value = DefaultGrain;
            Fringe.Value = DefaultFringe;
            clickVolume.Value = 2;
            PushSettings();
            Log.LogInfo("MENU defaults");
        }

        void LogSettings()
        {
            Log.LogInfo(string.Format(CultureInfo.InvariantCulture,
                "MENU stage {0:F2} m wide at {1:F2} m, layer depth {2:F2}, character pop {3:F2}, pop-out {4:F2}, sky {5:F2}, grain {6:F2}, fringe {7:F2}, sharpen {8:F2}, resolution {9:F2}, smooth {10}, edge match {11}",
                stageWidth.Value, stageDistance.Value, depth.Value, characterPop.Value, popOut.Value, skyDepth.Value, Grain.Value, Fringe.Value, sharpen.Value, resolution.Value, smooth.Value, edgeMatch.Value));
        }

        void PushSettings()
        {
            rig.StageWidth = stageWidth.Value;
            rig.StageDistance = stageDistance.Value;
            rig.Separation = depth.Value;
            rig.PopOut = popOut.Value;
            rig.SkyDepth = skyDepth.Value;
            rig.CharacterPop = characterPop.Value;
            rig.LeanParallax = lean.Value;
            rig.Sharpen = sharpen.Value;
            rig.Sharpness = resolution.Value;
            rig.Smooth = smooth.Value;
            rig.PictureMode = PictureCode(picture.Value);
            rig.EdgeTrim = edgeMatch.Value;
            rig.ApplyStage();
            menu.Refresh();
        }

        // ---- lab ----

        void ReadCommands()
        {
            if (!labCommands.Value || !File.Exists(commandFile)) return;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(commandFile);
                File.Delete(commandFile);
            }
            catch (IOException) { return; } // still being written: try again next check
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                Log.LogInfo("command: " + line);
                try { Run(line); }
                catch (Exception e) { Log.LogError("command failed: " + line + ": " + e); }
            }
        }

        static float F(string s) { return float.Parse(s, CultureInfo.InvariantCulture); }

        void Run(string line)
        {
            var arg = line.IndexOf(' ') > 0 ? line.Substring(line.IndexOf(' ') + 1).Trim() : "";
            var a = arg.Split(' ');
            switch (line.Split(' ')[0])
            {
                case "playerpos":
                {
                    var pl = PlayerManager.GetPlayer(PlayerId.PlayerOne);
                    if (pl == null) { Log.LogInfo("PLAYERPOS none"); break; }
                    var sb = new System.Text.StringBuilder("PLAYERPOS root " + pl.transform.position.ToString("F3"));
                    foreach (var r in pl.GetComponentsInChildren<SpriteRenderer>(true))
                        if (r.enabled && r.sprite != null) sb.Append(" | ").Append(r.name).Append(" local ").Append(r.transform.localPosition.ToString("F4"));
                    Log.LogInfo(sb.ToString());
                    break;
                }
                case "probe": probes.Probe(); Log.LogInfo("RIG active " + rig.Active + " " + rig.LayerSummary() + " menu " + menu.IsOpen); break;
                case "dump": probes.Dump(arg.Length > 0 ? arg : "manual"); break;
                case "autodump": autoDump.Value = arg == "1"; break;
                case "zerodiff": probes.StartZeroDiff(arg.Length > 0 ? arg : "zd"); break;
                case "stereo": probes.StartStereoShot(F(a[0]), a.Length > 1 ? a[1] : "st"); break;
                case "slot": probes.EnterSlot(int.Parse(arg, CultureInfo.InvariantCulture)); break;
                case "level": probes.LoadLevel(arg); break;
                case "scene": probes.LoadScene(arg); break;
                case "timescale": Time.timeScale = F(arg); break;
                case "vr": if (arg == "on") rig.Begin(); else rig.End(); break;
                case "xrshot": rig.Shot(); break;
                case "eyecheck": rig.EyeCheck(); break;
                case "xrdump": rig.Dump(Path.Combine(rig.ShotDir, arg.Length > 0 ? arg : "xrdump")); break;
                case "stamp": rig.Stamp = arg == "1"; break;
                case "depth": depth.Value = F(arg); PushSettings(); break;
                case "pop": characterPop.Value = F(arg); PushSettings(); break;
                case "fakehead": rig.LabHead = new Vector3(F(a[0]), F(a[1]), 0f); break;
                case "lean": lean.Value = F(arg); PushSettings(); break;
                case "popout": popOut.Value = F(arg); PushSettings(); break;
                case "sky": skyDepth.Value = F(arg); PushSettings(); break;
                case "grain": Grain.Value = F(arg); break;
                case "fringe": Fringe.Value = F(arg); break;
                case "sharpen": sharpen.Value = F(arg); PushSettings(); break;
                case "stage": stageWidth.Value = F(a[0]); stageDistance.Value = F(a[1]); PushSettings(); rig.UpdateResolution(); break;
                case "recenter": rig.Recenter(); break;
                case "menu": if (arg.Length == 0) { if (menu.IsOpen) menu.Close(); else menu.Open(); } else menu.LabKey(arg); break;
                case "menushot": probes.SaveTexture(menu.Texture, "menu_" + (arg.Length > 0 ? arg : "shot")); break;
                case "match": rig.MatchRefresh = arg == "1"; break;
                case "edge": edgeMatch.Value = arg == "1"; PushSettings(); break;
                case "smooth": smooth.Value = arg == "1"; PushSettings(); break;
                case "even": rig.MeasureEven(int.Parse(arg, CultureInfo.InvariantCulture)); break;
                case "teleport": { var pl = PlayerManager.GetPlayer(PlayerId.PlayerOne); if (pl != null) { pl.transform.position += new Vector3(F(a[0]), a.Length > 1 ? F(a[1]) : 0f, 0f); Log.LogInfo("TELEPORT player to " + pl.transform.position); } break; }
                case "picture":
                    picture.Value = arg == "steady" ? "Steady" : arg == "panel" ? "Panel" : "Auto";
                    PushSettings();
                    rig.LogPicture();
                    break;
                case "picturetest":
                    foreach (var name in new[] { "SteamVR/OpenXR", "VirtualDesktopXR", "VirtualDesktopXR 1.1.0", "Virtual Desktop", "VDXR", "Oculus", "Windows Mixed Reality Runtime", "Pimax", "" })
                        Log.LogInfo("PICTURETEST runtime \"" + name + "\": auto picks " + (XrRig.AutoPicksSteady(name) ? "Steady" : "Panel"));
                    break;
                case "track": probes.Track(F(arg)); break;
                default: Log.LogWarning("unknown command " + line); break;
            }
        }
    }

    // The game writes saves through OnlineInterfaceSteam.SaveCloudData. In the lab, report success and write nothing.
    [HarmonyPatch(typeof(OnlineInterfaceSteam), "SaveCloudData")]
    static class BlockSavePatch
    {
        static bool Prefix(SaveCloudDataHandler handler)
        {
            if (!Plugin.BlockSaves.Value) return true;
            Plugin.Log.LogInfo("SAVE blocked (Lab.BlockSaves)");
            if (handler != null) handler(true);
            return false;
        }
    }

    // Film grain and colour fringe: the game's full-screen pass (ChromaticAberrationFilmGrain.OnRenderImage) reads its
    // own intensity field and the chromatic aberration setting. Both are scaled for the pass and put back after it,
    // so the game's saved settings never change.
    [HarmonyPatch(typeof(ChromaticAberrationFilmGrain), "OnRenderImage")]
    static class GrainPatch
    {
        static void Prefix(ChromaticAberrationFilmGrain __instance, out float[] __state)
        {
            var data = SettingsData.Data;
            __state = new[] { __instance.intensity, data != null ? data.chromaticAberration : 0f };
            __instance.intensity = __state[0] * Plugin.Grain.Value;
            if (data != null) data.chromaticAberration = __state[1] * Plugin.Fringe.Value;
        }

        static void Postfix(ChromaticAberrationFilmGrain __instance, float[] __state)
        {
            __instance.intensity = __state[0];
            var data = SettingsData.Data;
            if (data != null) data.chromaticAberration = __state[1];
        }
    }
}
