using System.Globalization;
using UnityEngine;

namespace CupheadVR
{
    // Game speed probe (reference v1.9, section 10). Every few seconds of real time: game seconds per real second from
    // Unity's own clock (Time.time), the same for fixed ticks (ticks x tick length), frames and ticks per second, tick
    // length, Unity's time scale, Cuphead's own global speed, the headset period and the game's VSync and frame cap.
    // Lab only for now: switched on with the "speedlog" command.
    internal class SpeedLog : MonoBehaviour
    {
        internal XrRig Rig;
        internal bool On;
        internal float Window = 5f;
        float startReal, startGame;
        int startFrame, ticks;
        float lastStep;

        internal void Begin(float seconds)
        {
            Window = seconds;
            On = true;
            Restart();
            Plugin.Log.LogInfo("SPEEDLOG on, window " + seconds.ToString("F1", CultureInfo.InvariantCulture) + " s");
        }

        void Restart()
        {
            startReal = Time.realtimeSinceStartup;
            startGame = Time.time;
            startFrame = Time.frameCount;
            ticks = 0;
            lastStep = Time.fixedDeltaTime;
        }

        void FixedUpdate() { if (On) ticks++; }

        void Update()
        {
            if (!On) return;
            float real = Time.realtimeSinceStartup - startReal;
            if (real < Window) return;
            float game = Time.time - startGame;
            int frames = Time.frameCount - startFrame;
            float step = Time.fixedDeltaTime;
            float speed = game / real;
            float tickSpeed = ticks * step / real;
            bool settled = Mathf.Abs(step - lastStep) < 1e-6f && Time.timeScale > 0f;
            Plugin.Log.LogInfo(string.Format(CultureInfo.InvariantCulture,
                "GAMESPEED {0:F3} game s per real s, ticks {1:F3}, {2:F1} fps, {3:F1} ticks/s, tick {4:F5} s, timeScale {5:F2}, cuphead speed {6:F2}, headset {7:F1} Hz, vsync {8}, frame cap {9}, picture {10}, over {11:F2} s",
                speed, tickSpeed, frames / real, ticks / real, step, Time.timeScale, CupheadTime.GlobalSpeed,
                Rig != null && Rig.PeriodMs > 0f ? 1000f / Rig.PeriodMs : 0f, QualitySettings.vSyncCount, Application.targetFrameRate,
                Rig != null ? Rig.ActiveLayerName : "none", real));
            if (settled && (Mathf.Abs(speed - 1f) > 0.05f || Mathf.Abs(tickSpeed - 1f) > 0.05f))
                Plugin.Log.LogWarning(string.Format(CultureInfo.InvariantCulture, "GAMESPEED warning: the game is running at {0:F0}% of real time (ticks {1:F0}%)", speed * 100f, tickSpeed * 100f));
            Restart();
        }
    }
}
