using HarmonyLib;
using J2i.Net.XInputWrapper;
using SmartInput;
using UnityEngine;

namespace OriVR
{
    // PlayStation pads in the game itself. Ori only knows Xbox (XInput) pads, plus a generic joystick mapping that puts a
    // DualSense's buttons in the wrong places. When the mod reads a DualSense or DualShock 4 and no Xbox pad is connected,
    // the game's two pad functions (XboxControllerInput.GetButton and GetAxis, which every game input goes through) answer
    // from the PlayStation pad as if it were an Xbox pad: Cross is A, Circle B, Square X, Triangle Y, Options Start,
    // Create Back. With Steam Input on, Steam presents an Xbox pad and the game uses that, so nothing here changes.
    // These patches also hold every game input off while the mod menu is open and until every button is released.
    internal static class PadBridge
    {
        internal static XrRig Rig;
        internal static bool BlockGame;    // mod menu open, or closing and waiting for every button and key to be released
        internal static bool MenuReading;  // the mod menu itself is reading the game's pad functions

        const int Up = 1, Down = 2, Left = 4, Right = 8, Cross = 16, Circle = 32, Square = 64, Triangle = 128,
                  L3 = 256, R3 = 512, Options = 1024, Create = 2048, L1 = 8192, R1 = 16384, L2 = 32768, R2 = 65536, Connected = 1 << 30;
        static bool wasActive;

        // A PlayStation pad is read and no Xbox pad is connected.
        internal static bool PsOnly
        {
            get { return Rig != null && Rig.PadKind != 0 && !XboxController.RetrieveController(0).IsConnected; }
        }

        static bool Active(out int ps)
        {
            ps = 0;
            bool active = PsOnly && ((ps = Rig.PadButtons) & Connected) != 0;
            if (active != wasActive)
            {
                wasActive = active;
                Plugin.Log.LogInfo(active ? "PAD the PlayStation pad drives the game as an Xbox pad (no Xbox pad connected)" : "PAD the game reads its own pads again");
            }
            return active;
        }

        static bool Button(int ps, XboxControllerInput.Button b)
        {
            switch (b)
            {
                case XboxControllerInput.Button.ButtonA: return (ps & Cross) != 0;
                case XboxControllerInput.Button.ButtonB: return (ps & Circle) != 0;
                case XboxControllerInput.Button.ButtonX: return (ps & Square) != 0;
                case XboxControllerInput.Button.ButtonY: return (ps & Triangle) != 0;
                case XboxControllerInput.Button.LeftShoulder: return (ps & L1) != 0;
                case XboxControllerInput.Button.RightShoulder: return (ps & R1) != 0;
                case XboxControllerInput.Button.LeftTrigger: return (ps & L2) != 0;
                case XboxControllerInput.Button.RightTrigger: return (ps & R2) != 0;
                case XboxControllerInput.Button.LeftStick: return (ps & L3) != 0;
                case XboxControllerInput.Button.RightStick: return (ps & R3) != 0;
                case XboxControllerInput.Button.Start: return (ps & Options) != 0;
                case XboxControllerInput.Button.Select: return (ps & Create) != 0;
                default: return false;
            }
        }

        static float Stick(int value) { return Mathf.Clamp((value - 128) / 127f, -1f, 1f); }

        static float Axis(int ps, int stick, XboxControllerInput.Axis a)
        {
            switch (a)
            {
                case XboxControllerInput.Axis.LeftStickX: return Stick(stick & 0xff);
                case XboxControllerInput.Axis.LeftStickY: return -Stick((stick >> 8) & 0xff);  // the pad's y grows downward
                case XboxControllerInput.Axis.RightStickX: return Stick((stick >> 16) & 0xff);
                case XboxControllerInput.Axis.RightStickY: return -Stick((stick >> 24) & 0xff);
                case XboxControllerInput.Axis.DpadX: return ((ps & Right) != 0 ? 1f : 0f) - ((ps & Left) != 0 ? 1f : 0f);
                case XboxControllerInput.Axis.DpadY: return ((ps & Up) != 0 ? 1f : 0f) - ((ps & Down) != 0 ? 1f : 0f);
                default: return 0f;
            }
        }

        [HarmonyPatch(typeof(XboxControllerInput), "GetButton")]
        static class ButtonPatch
        {
            static bool Prefix(XboxControllerInput.Button button, ref bool __result)
            {
                if (MenuReading) return true;
                if (BlockGame) { __result = false; return false; }
                int ps;
                if (!Active(out ps)) return true;
                __result = Button(ps, button);
                return false;
            }
        }

        [HarmonyPatch(typeof(XboxControllerInput), "GetAxis")]
        static class AxisPatch
        {
            static bool Prefix(XboxControllerInput.Axis axis, ref float __result)
            {
                if (MenuReading) return true;
                if (BlockGame) { __result = 0f; return false; }
                int ps;
                if (!Active(out ps)) return true;
                __result = Axis(ps, Rig.PadStick, axis);
                return false;
            }
        }

        // The game's keyboard, mouse buttons and joystick reads all go through MoonInput. Held off while the menu is open.
        [HarmonyPatch(typeof(MoonInput), "GetKey", new[] { typeof(KeyCode) })]
        static class KeyPatch { static bool Prefix(ref bool __result) { if (!BlockGame) return true; __result = false; return false; } }

        [HarmonyPatch(typeof(MoonInput), "GetKeyDown", new[] { typeof(KeyCode) })]
        static class KeyDownPatch { static bool Prefix(ref bool __result) { if (!BlockGame) return true; __result = false; return false; } }

        [HarmonyPatch(typeof(MoonInput), "GetKeyDown", new[] { typeof(string) })]
        static class KeyDownNamePatch { static bool Prefix(ref bool __result) { if (!BlockGame) return true; __result = false; return false; } }

        [HarmonyPatch(typeof(MoonInput), "GetButton", new[] { typeof(string) })]
        static class NamedButtonPatch { static bool Prefix(ref bool __result) { if (!BlockGame || MenuReading) return true; __result = false; return false; } }

        [HarmonyPatch(typeof(MoonInput), "GetButtonDown", new[] { typeof(string) })]
        static class NamedButtonDownPatch { static bool Prefix(ref bool __result) { if (!BlockGame) return true; __result = false; return false; } }

        [HarmonyPatch(typeof(MoonInput), "GetAxis", new[] { typeof(string) })]
        static class NamedAxisPatch { static bool Prefix(ref float __result) { if (!BlockGame || MenuReading) return true; __result = 0f; return false; } }

        [HarmonyPatch(typeof(MoonInput), "GetMouseButton", new[] { typeof(int) })]
        static class MousePatch { static bool Prefix(ref bool __result) { if (!BlockGame) return true; __result = false; return false; } }
    }
}
