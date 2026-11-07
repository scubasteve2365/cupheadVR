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
    // Build 2: the game on a stage in front of a seated player, in native stereo with depth from the game's own parallax
    // layers, and an in-headset menu (hold both sticks). VR starts by itself unless VR.AutoStart is off; if OpenXR cannot
    // start, the game stays flat. Lab probes from build 1 stay available behind Lab.Commands.
    [BepInPlugin("com.scubasteve2365.cupheadvr", "CupheadVR", "0.0.2")]
    public class Plugin : BaseUnityPlugin
    {
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ConfigEntry<bool> BlockSaves;
        internal static string OutDir;
        internal static bool RigActive { get { return instance != null && instance.rig.Active; } }
        static Plugin instance;

        const float DefaultWidth = 1.85f, DefaultDistance = 1.0f, DefaultDepth = 1.0f, DefaultSky = 0.85f, DefaultSharpen = 0.3f;
        ConfigEntry<bool> labCommands, autoDump, autoStart;
        ConfigEntry<float> stageWidth, stageDistance, depth, popOut, skyDepth, sharpen, resolution;
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
            depth = Config.Bind("VR", "Depth", DefaultDepth, "Depth strength, 1 is natural.");
            popOut = Config.Bind("VR", "PopOut", 0f, "0 keeps the play plane on the stage. Higher brings the scene toward you.");
            skyDepth = Config.Bind("VR", "SkyDepth", DefaultSky, "How far back skies without their own parallax sit, 0 on the stage, 1 very far.");
            sharpen = Config.Bind("VR", "Sharpen", DefaultSharpen, "Strength of the sharpening pass, 0 is off.");
            resolution = Config.Bind("VR", "ResolutionScale", 1.0f, "1 matches the headset's pixel density across the stage.");
            commandFile = Path.Combine(Paths.GameRootPath, "vrlab_command.txt");
            OutDir = Path.Combine(Paths.GameRootPath, "vrlab_dumps");
            probes = gameObject.AddComponent<Probes>();
            rig = gameObject.AddComponent<XrRig>();
            rig.PluginDir = Path.GetDirectoryName(Info.Location);
            rig.ShotDir = Path.Combine(Paths.GameRootPath, "vrlab_shots");
            menu = gameObject.AddComponent<MenuUI>();
            menu.RowText = MenuRowText;
            menu.Adjust = MenuAdjust;
            menu.Activate = MenuActivate;
            menu.Closed = () => { rig.ApplyStage(); rig.UpdateResolution(); };
            rig.Menu = menu;
            PushSettings();
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                Log.LogInfo("SCENE loaded " + scene.name + " mode " + mode + " frame " + Time.frameCount);
                if (autoDump.Value) dumpAt = Time.realtimeSinceStartup + 4f;
            };
            new Harmony("com.scubasteve2365.cupheadvr").PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("CupheadVR 0.0.2 (build 2) loaded. Unity " + Application.unityVersion + ", pointer size " + IntPtr.Size);
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

        string MenuRowText(int row)
        {
            switch (row)
            {
                case 0: return string.Format(CultureInfo.InvariantCulture, "Stage distance: {0:F2} m", stageDistance.Value);
                case 1: return string.Format(CultureInfo.InvariantCulture, "Stage size: {0:F2} m wide", stageWidth.Value);
                case 2: return string.Format(CultureInfo.InvariantCulture, "Depth: {0:F0}%", depth.Value * 100f);
                case 3: return string.Format(CultureInfo.InvariantCulture, "Pop-out: {0:F0}%", popOut.Value * 100f);
                case 4: return string.Format(CultureInfo.InvariantCulture, "Sky distance: {0:F0}%", skyDepth.Value * 100f);
                case 5: return string.Format(CultureInfo.InvariantCulture, "Sharpen: {0:F0}%", sharpen.Value * 100f);
                case 6: return string.Format(CultureInfo.InvariantCulture, "Resolution: {0:F0}%   ({1} pixels wide)", resolution.Value * 100f, rig.WantedWidth());
                case 7: return "Recenter";
                case 8: return "Reset to defaults";
                default: return "Close";
            }
        }

        void MenuAdjust(int row, int dir)
        {
            if (row == 0) stageDistance.Value = Mathf.Clamp(stageDistance.Value + dir * 0.05f, 0.5f, 10f);
            else if (row == 1) stageWidth.Value = Mathf.Clamp(stageWidth.Value + dir * 0.05f, 0.5f, 10f);
            else if (row == 2) depth.Value = Mathf.Clamp(depth.Value + dir * 0.05f, 0f, 2f);
            else if (row == 3) popOut.Value = Mathf.Clamp(popOut.Value + dir * 0.05f, 0f, 1f);
            else if (row == 4) skyDepth.Value = Mathf.Clamp(skyDepth.Value + dir * 0.05f, 0f, 1f);
            else if (row == 5) sharpen.Value = Mathf.Clamp(sharpen.Value + dir * 0.1f, 0f, 1f);
            else if (row == 6) resolution.Value = Mathf.Clamp(resolution.Value + dir * 0.1f, 0.5f, 1.5f);
            else return;
            PushSettings();
            Log.LogInfo(string.Format(CultureInfo.InvariantCulture, "MENU stage {0:F2} m wide at {1:F2} m, depth {2:F2}, pop-out {3:F2}, sky {4:F2}, sharpen {5:F2}, resolution {6:F2}",
                stageWidth.Value, stageDistance.Value, depth.Value, popOut.Value, skyDepth.Value, sharpen.Value, resolution.Value));
        }

        void MenuActivate(int row)
        {
            if (row == 7) { rig.Recenter(); Log.LogInfo("MENU recenter"); }
            else if (row == 8)
            {
                stageDistance.Value = DefaultDistance;
                stageWidth.Value = DefaultWidth;
                depth.Value = DefaultDepth;
                popOut.Value = 0f;
                skyDepth.Value = DefaultSky;
                sharpen.Value = DefaultSharpen;
                resolution.Value = 1f;
                PushSettings();
                Log.LogInfo("MENU defaults");
            }
            else if (row == 9) menu.Close();
        }

        void PushSettings()
        {
            rig.StageWidth = stageWidth.Value;
            rig.StageDistance = stageDistance.Value;
            rig.Separation = depth.Value;
            rig.PopOut = popOut.Value;
            rig.SkyDepth = skyDepth.Value;
            rig.Sharpen = sharpen.Value;
            rig.Sharpness = resolution.Value;
            rig.ApplyStage();
        }

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
                case "popout": popOut.Value = F(arg); PushSettings(); break;
                case "sky": skyDepth.Value = F(arg); PushSettings(); break;
                case "sharpen": sharpen.Value = F(arg); PushSettings(); break;
                case "stage": stageWidth.Value = F(a[0]); stageDistance.Value = F(a[1]); PushSettings(); rig.UpdateResolution(); break;
                case "track": probes.Track(F(arg)); break;
                case "match": rig.MatchRefresh = arg == "1"; break;
                case "recenter": rig.Recenter(); break;
                case "menu": if (arg.Length == 0) { if (menu.IsOpen) menu.Close(); else menu.Open(); } else menu.LabKey(arg); break;
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
}
