using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Rewired;

namespace CupheadVR
{
    // DualSense support, C# side. The native part (DualSense.cpp) reads the pad and shows it to the game as an Xbox pad
    // through XInput. Rewired may also see the same pad directly as a raw joystick, which would give a second, wrongly
    // mapped controller (the game's data knows DualShock 4 but not DualSense). While the DualSense is shown as an Xbox
    // pad, that raw joystick is hidden from the game's own controller lists.
    internal static class PadSupport
    {
        internal static Func<int> Slot = () => -1;        // XInput slot the DualSense is shown in, -1 none
        internal static Func<int> Connected = () => 0;    // 0 none, 1 USB, 2 Bluetooth
        static readonly HashSet<int> logged = new HashSet<int>();

        internal static bool PlayStationNames { get { return Slot() >= 0; } }

        static string Describe(Joystick j)
        {
            return "\"" + j.name + "\" hardware \"" + j.hardwareName + "\" id " + j.id;
        }

        // A raw (not XInput) joystick that looks like a PlayStation pad, while the DualSense is in use as an Xbox pad.
        internal static bool HideRaw(Joystick j)
        {
            if (j == null || Slot() < 0) return false;
            string all = ((j.name ?? "") + " " + (j.hardwareName ?? "")).ToLowerInvariant();
            if (all.Contains("xinput")) return false;
            return all.Contains("dualsense") || all.Contains("wireless controller") || all.Contains("sony") || all.Contains("dualshock") || all.Contains("playstation");
        }

        internal static void LogJoysticks()
        {
            try
            {
                if (!ReInput.isReady) return;
                foreach (var j in ReInput.controllers.Joysticks)
                {
                    if (j == null || !logged.Add(j.id)) continue;
                    Plugin.Log.LogInfo("PAD game sees joystick " + Describe(j) + (HideRaw(j) ? ", hidden: the DualSense is shown as an Xbox pad" : ""));
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("PAD joystick list: " + e.Message); }
        }

        // Takes a hidden joystick away from any player it was given to.
        internal static void Unassign()
        {
            try
            {
                if (!ReInput.isReady || Slot() < 0) return;
                foreach (var j in ReInput.controllers.GetJoysticks())
                {
                    if (!HideRaw(j)) continue;
                    foreach (var p in ReInput.players.AllPlayers)
                        if (p.controllers.ContainsController(j))
                        {
                            p.controllers.RemoveController(j);
                            Plugin.Log.LogInfo("PAD took " + Describe(j) + " away from player " + p.id + " (the DualSense is shown as an Xbox pad)");
                        }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("PAD unassign: " + e.Message); }
        }
    }

    // The game walks ReInput.controllers.Joysticks to find and assign pads (CupheadInput, PlayerManager).
    [HarmonyPatch]
    static class JoystickListPatch
    {
        static MethodBase TargetMethod() { return AccessTools.PropertyGetter(typeof(ReInput.ControllerHelper), "Joysticks"); }

        static void Postfix(ref IList<Joystick> __result)
        {
            if (__result == null || PadSupport.Slot() < 0) return;
            bool any = false;
            foreach (var j in __result) if (PadSupport.HideRaw(j)) { any = true; break; }
            if (!any) return;
            var list = new List<Joystick>();
            foreach (var j in __result) if (!PadSupport.HideRaw(j)) list.Add(j);
            __result = list.AsReadOnly();
        }
    }
}
