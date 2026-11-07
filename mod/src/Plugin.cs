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
    // Build 1: read-only probes for the feasibility check. Dumps cameras and every renderer's layer, sorting order, z and
    // parallax speed, jumps to levels through the game's own SceneLoader, and runs a zero-difference control of a
    // re-render of the game's camera stack. Nothing here changes what the game draws.
    [BepInPlugin("com.scubasteve2365.cupheadvr", "CupheadVR", "0.0.1")]
    public class Plugin : BaseUnityPlugin
    {
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ConfigEntry<bool> BlockSaves;
        internal static string OutDir;

        ConfigEntry<bool> labCommands, autoDump;
        string commandFile;
        int nextCommandCheck;
        float dumpAt = -1f;
        Probes probes;

        void Awake()
        {
            Log = Logger;
            labCommands = Config.Bind("Lab", "Commands", true, "Developer test commands read from vrlab_command.txt in the game folder.");
            autoDump = Config.Bind("Lab", "AutoDump", false, "Dump cameras and renderers 4 seconds after each scene loads.");
            BlockSaves = Config.Bind("Lab", "BlockSaves", true, "Never write save files (keeps the lab's copy of the saves unchanged).");
            commandFile = Path.Combine(Paths.GameRootPath, "vrlab_command.txt");
            OutDir = Path.Combine(Paths.GameRootPath, "vrlab_dumps");
            Directory.CreateDirectory(OutDir);
            probes = gameObject.AddComponent<Probes>();
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                Log.LogInfo("SCENE loaded " + scene.name + " mode " + mode + " frame " + Time.frameCount);
                if (autoDump.Value) dumpAt = Time.realtimeSinceStartup + 4f;
            };
            new Harmony("com.scubasteve2365.cupheadvr").PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("CupheadVR 0.0.1 (build 1, probes only) loaded. Unity " + Application.unityVersion + ", pointer size " + IntPtr.Size);
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

        void Run(string line)
        {
            var arg = line.IndexOf(' ') > 0 ? line.Substring(line.IndexOf(' ') + 1).Trim() : "";
            var word = line.Split(' ')[0];
            switch (word)
            {
                case "probe": probes.Probe(); break;
                case "dump": probes.Dump(arg.Length > 0 ? arg : "manual"); break;
                case "autodump": autoDump.Value = arg == "1"; break;
                case "stereo": { var a = arg.Split(' '); probes.StartStereoShot(float.Parse(a[0], CultureInfo.InvariantCulture), a.Length > 1 ? a[1] : "st"); break; }
                case "zerodiff": probes.StartZeroDiff(arg.Length > 0 ? arg : "zd"); break;
                case "slot": probes.EnterSlot(int.Parse(arg, CultureInfo.InvariantCulture)); break;
                case "level": probes.LoadLevel(arg); break;
                case "scene": probes.LoadScene(arg); break;
                case "timescale": Time.timeScale = float.Parse(arg, CultureInfo.InvariantCulture); break;
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
