using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace OriVR
{
    // Build 18 (release 1.2.2): OpenXR diorama with a settings menu. VR starts by itself once the game has a camera, unless VR.AutoStart is false, and if
    // OpenXR cannot start the game stays flat. Telemetry is stopped, because the lab keeps this game offline.
    [BepInPlugin("com.scubasteve2365.orivr", "OriVR", "1.2.2")]
    public class Plugin : BaseUnityPlugin
    {
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ConfigEntry<bool> BlockTelemetry;
        internal static int TelemetryBlocked;

        string commandFile;
        RenderTests tests;
        XrRig rig;
        SpeedTest speed;
        internal static int Ticks;
        internal static bool RigActive { get { return instance != null && instance.rig.Active; } }
        static Plugin instance;
        ConfigEntry<bool> autoStart, labCommands;
        ConfigEntry<float> stageWidth, stageDistance, separation;
        ConfigEntry<int> eyeWidth;
        bool triedAutoStart;
        ConfigEntry<float> sharpness, popOut, sharpen;
        LabTools lab;
        const float DefaultWidth = 1.85f, DefaultDistance = 1.0f, DefaultDepth = 1.5f, DefaultSharpen = 0.3f;
        ConfigEntry<bool> matchRefresh;
        ConfigEntry<string> picture, surround;
        ConfigEntry<bool> catchUp;
        MenuUI menu;
        int ticks;
        int nextCommandCheck;
        int autoProbeFrame = 600;

        void Awake()
        {
            Log = Logger;
            instance = this;
            BlockTelemetry = Config.Bind("Network", "BlockTelemetry", true, "Stop the game sending telemetry to its server.");
            commandFile = Path.Combine(Paths.GameRootPath, "vrlab_command.txt");
            tests = gameObject.AddComponent<RenderTests>();
            tests.ShotDir = Path.Combine(Paths.GameRootPath, "vrlab_shots");
            speed = gameObject.AddComponent<SpeedTest>();
            rig = gameObject.AddComponent<XrRig>();
            rig.PluginDir = Path.GetDirectoryName(Info.Location);
            rig.ShotDir = tests.ShotDir;
            labCommands = Config.Bind("Lab", "Commands", false, "Developer test commands read from a file in the game folder. Leave off.");
            autoStart = Config.Bind("VR", "AutoStart", true, "Start VR by itself once the game has a camera.");
            stageWidth = Config.Bind("VR", "StageWidth", DefaultWidth, "Width of the stage in metres.");
            stageDistance = Config.Bind("VR", "StageDistance", DefaultDistance, "Distance to the stage in metres.");
            separation = Config.Bind("VR", "Separation", DefaultDepth, "Depth strength, 1 is natural.");
            eyeWidth = Config.Bind("VR", "EyeWidth", 2560, "Starting eye image width in pixels. VR works out the real size from the headset and the stage.");
            sharpness = Config.Bind("VR", "ResolutionScale", 1.0f, "1 matches the headset's pixel density across the stage.");
            popOut = Config.Bind("VR", "PopOut", 0f, "0 keeps the gameplay layer on the stage. Higher brings more of the scene toward you.");
            sharpen = Config.Bind("VR", "Sharpen", DefaultSharpen, "Strength of the sharpening pass, 0 is off.");
            rig.PopOut = popOut.Value;
            rig.Sharpen = sharpen.Value;
            lab = gameObject.AddComponent<LabTools>();
            matchRefresh = Config.Bind("VR", "MatchRefresh", true, "Run one game tick per headset frame, at whatever rate OpenXR reports.");
            rig.Sharpness = sharpness.Value;
            rig.MatchRefresh = matchRefresh.Value;
            catchUp = Config.Bind("VR", "FrameCatchUp", true, "When a frame comes late, run up to three game ticks so the game keeps its normal speed. Off: the game slows down a little whenever frames are missed, as the original game does.");
            rig.CatchUp = catchUp.Value;
            picture = Config.Bind("VR", "Picture", "Auto", "How the stage reaches the headset: Auto, Steady or Panel. Auto uses Steady on Virtual Desktop and Panel on everything else.");
            rig.PictureMode = PictureCode(picture.Value);
            surround = Config.Bind("VR", "Surround", "Black", "What you see around the stage: Black, Green (for Virtual Desktop's chroma-key passthrough) or Transparent (asks the headset software to show your room, where it offers that).");
            rig.Surround = SurroundCode(surround.Value);
            menu = gameObject.AddComponent<MenuUI>();
            menu.Rig = rig;
            var menuKey = Config.Bind("Menu", "OpenKey", "Insert", "Keyboard key that opens and closes the mod menu. Arrow keys choose and change, Enter selects, Esc or Backspace closes. Any Unity KeyCode name, for example Insert or F1.");
            try { menu.OpenKey = (KeyCode)Enum.Parse(typeof(KeyCode), menuKey.Value, true); }
            catch (Exception) { Log.LogWarning("Menu.OpenKey '" + menuKey.Value + "' is not a key name, Insert used"); }
            rig.LoadNative();  // early, so the PlayStation pad reader runs before VR starts
            PadBridge.Rig = rig;
            menu.RowText = MenuRowText;
            menu.RowTip = MenuRowTip;
            menu.Problem = () => rig.StartProblem;
            menu.Adjust = MenuAdjust;
            menu.Activate = MenuActivate;
            menu.Closed = () => { rig.ApplyStage(); rig.UpdateResolution(); };
            rig.StageWidth = stageWidth.Value;
            rig.StageDistance = stageDistance.Value;
            rig.Separation = separation.Value;
            rig.EyeWidth = eyeWidth.Value;
            rig.EyeHeight = eyeWidth.Value * 9 / 16;
            new Harmony("com.scubasteve2365.orivr").PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("OriVR 1.2.2 (build 18) loaded. Unity " + Application.unityVersion + ", pointer size " + IntPtr.Size);
        }

        void FixedUpdate() { ticks++; Ticks = ticks; }

        void Update()
        {
            if (Time.frameCount >= nextCommandCheck)
            {
                nextCommandCheck = Time.frameCount + 15;
                ReadCommands();
            }
            if (!triedAutoStart && autoStart.Value && Time.frameCount >= 300 && Game.UI.Cameras.Current != null)
            {
                triedAutoStart = true;
                rig.Begin();
            }
            if (autoProbeFrame > 0 && Time.frameCount >= autoProbeFrame)
            {
                autoProbeFrame = 0;
                Probe();
            }
        }

        // Lab test: made-up reports in each layout with Cross, L3, R3 and d-pad up pressed, left stick full left.
        void PadTest()
        {
            var cases = new[]
            {
                new { Name = "DualSense USB 0x01", Kind = 2, Report = Report(64, 0x01, 1, 8) },
                new { Name = "DualSense Bluetooth 0x31", Kind = 2, Report = Report(78, 0x31, 2, 9) },
                new { Name = "DualSense Bluetooth short 0x01", Kind = 2, Report = Report(10, 0x01, 1, 5) },
                new { Name = "DualShock 4 USB 0x01", Kind = 1, Report = Report(64, 0x01, 1, 5) },
                new { Name = "DualShock 4 Bluetooth 0x11", Kind = 1, Report = Report(78, 0x11, 3, 7) },
            };
            foreach (var c in cases)
            {
                int v = rig.PadParseTest(c.Kind, c.Report);
                bool ok = v == ((1 << 30) | 1 | 16 | 256 | 512);
                Log.LogInfo(string.Format("PADTEST {0}: flags {1}, {2}", c.Name, v, ok ? "ok" : "WRONG"));
            }
        }

        static byte[] Report(int length, byte id, int stick, int buttons)
        {
            var r = new byte[length];
            r[0] = id;
            r[stick] = 0; r[stick + 1] = 128; r[stick + 2] = 128; r[stick + 3] = 128;
            r[buttons] = 0x00 | 0x20;           // d-pad up (0), Cross
            r[buttons + 1] = 0x40 | 0x80;       // L3, R3
            return r;
        }

        string MenuRowTip(int row)
        {
            switch (row)
            {
                case 0: return "How far away the stage floats. Closer fills more of your view.";
                case 1: return "How wide the stage is, in metres. Bigger fills more of your view.";
                case 2: return "How strong the 3D is. Higher makes the world feel smaller and deeper. If far backgrounds strain your eyes, lower it or raise Pop-out.";
                case 3: return "Brings more of the scene out toward you. It also eases far backgrounds.";
                case 4: return "Crisper edges. Too high looks harsh. 0% turns it off.";
                case 5: return "100% matches your headset at this stage size. Higher costs speed and adds little in this game. Applies when the menu closes.";
                case 6:
                    return "Auto picks for your headset software: Steady on Virtual Desktop, Floating Panel on everything else. "
                         + "Steady draws the stage into each eye's view and stays still. "
                         + "Floating Panel can look a little sharper, but Virtual Desktop squashes and shears it when you turn your head."
                         + (rig.RuntimeName.Length > 0 ? " Detected: " + rig.RuntimeName + "." : "");
                case 7: return SurroundTip();
                case 8: return "On: when your PC misses a headset frame, the game makes up the lost time on the next one, so it never runs in slow motion. "
                             + "Off: the game slows down a little whenever frames are missed, as the original game does. Leave it On unless motion looks jumpy.";
                case 9: return "Puts the stage straight in front of where you are looking now.";
                case 10: return "Puts every setting on this page back to how it started.";
                default: return "Closes this menu and goes back to the game. Clicking both sticks or pressing " + menu.OpenKey + " also opens and closes it.";
            }
        }

        string PictureText()
        {
            string name = picture.Value == "Steady" ? "Steady" : picture.Value == "Panel" ? "Floating Panel" : "Auto";
            return picture.Value == "Auto" || !(picture.Value == "Steady" || picture.Value == "Panel")
                ? "Picture: " + name + " (" + rig.ActiveLayerName + ")"
                : "Picture: " + name;
        }

        void CyclePicture(int dir)
        {
            var order = new[] { "Auto", "Steady", "Panel" };
            int i = Array.IndexOf(order, picture.Value);
            if (i < 0) i = 0;
            picture.Value = order[(i + dir + order.Length) % order.Length];
        }

        string SurroundText()
        {
            if (surround.Value == "Green") return "Surround: Green";
            if (surround.Value == "Transparent")
                return rig.SeeThroughKnown && !rig.CanSeeThrough ? "Surround: Transparent (not offered, black)" : "Surround: Transparent";
            return "Surround: Black";
        }

        string SurroundTip()
        {
            if (surround.Value == "Green")
                return "Green fills everything around the stage with pure green. In Virtual Desktop, turn on passthrough with the chroma key set to green, and your room shows around the game.";
            if (surround.Value == "Transparent")
                return rig.SeeThroughKnown && !rig.CanSeeThrough
                    ? "Transparent: your headset software does not offer see-through for games, so the surround stays black. Try Green with Virtual Desktop's chroma key instead."
                    : "Transparent asks your headset software to show your room around the game itself. Not every headset software offers it.";
            return "What you see around the stage. Black is the original look. Green is for Virtual Desktop's chroma-key passthrough. Transparent asks your headset software to show your room.";
        }

        void CycleSurround(int dir)
        {
            var order = new[] { "Black", "Green", "Transparent" };
            int i = Math.Max(0, Array.IndexOf(order, surround.Value));
            surround.Value = order[(i + dir + order.Length) % order.Length];
        }

        static int SurroundCode(string value) { return value == "Green" ? 1 : value == "Transparent" ? 2 : 0; }

        static int PictureCode(string value) { return value == "Steady" ? 1 : value == "Panel" ? 2 : 0; }

        string MenuRowText(int row)
        {
            switch (row)
            {
                case 0: return string.Format("Stage distance: {0:F2} m", stageDistance.Value);
                case 1: return string.Format("Stage size: {0:F2} m", stageWidth.Value);
                case 2: return string.Format("Depth: {0:F0}%", separation.Value * 100f);
                case 3: return string.Format("Pop-out: {0:F0}%", popOut.Value * 100f);
                case 4: return string.Format("Sharpen: {0:F0}%", sharpen.Value * 100f);
                case 5: return string.Format("Resolution: {0:F0}%   ({1} pixels wide)", sharpness.Value * 100f, rig.WantedWidth());
                case 6: return PictureText();
                case 7: return SurroundText();
                case 8: return "Frame catch-up: " + (catchUp.Value ? "On" : "Off");
                case 9: return "Recenter";
                case 10: return "Reset to defaults";
                default: return "Close";
            }
        }

        void MenuAdjust(int row, int dir)
        {
            if (row == 0) stageDistance.Value = Mathf.Clamp(stageDistance.Value + dir * 0.05f, 0.5f, 10f);
            else if (row == 1) stageWidth.Value = Mathf.Clamp(stageWidth.Value + dir * 0.05f, 0.5f, 10f);
            else if (row == 2) separation.Value = Mathf.Clamp(separation.Value + dir * 0.05f, 0f, 3f);
            else if (row == 3) popOut.Value = Mathf.Clamp(popOut.Value + dir * 0.05f, 0f, 1f);
            else if (row == 4) sharpen.Value = Mathf.Clamp(sharpen.Value + dir * 0.1f, 0f, 1f);
            else if (row == 5) sharpness.Value = Mathf.Clamp(sharpness.Value + dir * 0.1f, 0.5f, 1.5f);
            else if (row == 6) CyclePicture(dir);
            else if (row == 7) CycleSurround(dir);
            else if (row == 8) catchUp.Value = !catchUp.Value;
            else return;
            PushSettings();
            Log.LogInfo(string.Format("MENU stage {0:F2} m wide at {1:F2} m, depth {2:F2}, pop-out {3:F2}, sharpen {4:F2}, resolution {5:F2}, picture {6}, surround {7}", stageWidth.Value, stageDistance.Value, separation.Value, popOut.Value, sharpen.Value, sharpness.Value, picture.Value, surround.Value));
            if (row == 6) rig.LogPicture();
        }

        void MenuActivate(int row)
        {
            if (row == 6 || row == 7 || row == 8)
            {
                MenuAdjust(row, 1);
                menu.Refresh();
            }
            else if (row == 9)
            {
                rig.Recenter();
                Log.LogInfo("MENU recenter");
            }
            else if (row == 10)
            {
                stageDistance.Value = DefaultDistance;
                stageWidth.Value = DefaultWidth;
                separation.Value = DefaultDepth;
                popOut.Value = 0f;
                sharpen.Value = DefaultSharpen;
                sharpness.Value = 1f;
                picture.Value = "Auto";
                surround.Value = "Black";
                catchUp.Value = true;
                PushSettings();
                menu.Refresh();
                Log.LogInfo("MENU defaults");
            }
            else if (row == 11) menu.Close();
        }

        void PushSettings()
        {
            rig.StageWidth = stageWidth.Value;
            rig.StageDistance = stageDistance.Value;
            rig.Separation = separation.Value;
            rig.Sharpness = sharpness.Value;
            rig.PopOut = popOut.Value;
            rig.Sharpen = sharpen.Value;
            rig.PictureMode = PictureCode(picture.Value);
            rig.Surround = SurroundCode(surround.Value);
            rig.CatchUp = catchUp.Value;
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
            catch (IOException) { return; } // the harness is still writing it: try again next check
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                Log.LogInfo("command: " + line);
                if (line == "probe") Probe();
                else if (line == "compare") tests.Compare();
                else if (line == "sizes") tests.Sizes();
                else if (line == "compare2") tests.CompareSameSway();
                else if (line == "vr on") rig.Begin();
                else if (line == "vr off") rig.End();
                else if (line == "xrshot") rig.Shot();
                else if (line == "eyecheck") rig.EyeCheck();
                else if (line == "fixes 0" || line == "fixes 1") rig.StereoFixes = line == "fixes 1";
                else if (line.StartsWith("dump ")) rig.Dump(Path.Combine(tests.ShotDir, line.Substring(5)));
                else if (line.StartsWith("sharpen ")) { sharpen.Value = float.Parse(line.Substring(8), System.Globalization.CultureInfo.InvariantCulture); PushSettings(); }
                else if (line.StartsWith("popout ")) { popOut.Value = float.Parse(line.Substring(7), System.Globalization.CultureInfo.InvariantCulture); PushSettings(); }
                else if (line.StartsWith("depth ")) { separation.Value = float.Parse(line.Substring(6), System.Globalization.CultureInfo.InvariantCulture); PushSettings(); }
                else if (line.StartsWith("stage ")) { var v = line.Split(' '); stageWidth.Value = float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture); stageDistance.Value = float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture); PushSettings(); rig.UpdateResolution(); }
                else if (line.StartsWith("scenes ")) lab.ListScenes(line.Substring(7));
                else if (line.StartsWith("goto ")) lab.GoTo(line.Substring(5));
                else if (line.StartsWith("crossfade ")) lab.CrossFade(float.Parse(line.Substring(10), System.Globalization.CultureInfo.InvariantCulture));
                else if (line == "catchup 0" || line == "catchup 1") { catchUp.Value = line == "catchup 1"; PushSettings(); }
                else if (line == "surround black" || line == "surround green" || line == "surround transparent")
                {
                    surround.Value = line == "surround green" ? "Green" : line == "surround transparent" ? "Transparent" : "Black";
                    PushSettings();
                }
                else if (line == "picture auto" || line == "picture steady" || line == "picture panel")
                {
                    picture.Value = line == "picture steady" ? "Steady" : line == "picture panel" ? "Panel" : "Auto";
                    PushSettings();
                    rig.LogPicture();
                }
                else if (line.StartsWith("xrjson ")) rig.TestRuntime(line == "xrjson none" ? "" : line.Substring(7), false);
                else if (line.StartsWith("xrnofallback ")) rig.TestRuntime(line == "xrnofallback none" ? "" : line.Substring(13), true);
                else if (line == "padtest") PadTest();
                else if (line == "picturetest")
                {
                    foreach (var name in new[] { "SteamVR/OpenXR", "VirtualDesktopXR", "Virtual Desktop", "virtual-desktop", "VisionALVR (VDXR)", "Oculus", "Windows Mixed Reality Runtime", "Pimax", "", null })
                        Log.LogInfo("PICTURETEST runtime \"" + name + "\": auto picks " + (XrRig.AutoPicksSteady(name) ? "Steady" : "Floating Panel"));
                }
                else if (line == "menu") { if (menu.IsOpen) menu.Close(); else menu.Open(); }
                else if (line.StartsWith("rate "))
                {
                    var parts = line.Split(' ');
                    speed.SetRate(int.Parse(parts[1]), parts.Length > 2 ? int.Parse(parts[2]) : 0);
                }
                else if (line == "gamesync 0" || line == "gamesync 1") { SyncFramesTest.EnabledForceFixedUpdate = line == "gamesync 1"; Log.LogInfo("game frame sync nudging " + (SyncFramesTest.EnabledForceFixedUpdate ? "allowed" : "blocked")); }
                else if (line == "vsync 0" || line == "vsync 1") QualitySettings.vSyncCount = line == "vsync 1" ? 1 : 0;
                else if (line.StartsWith("speed ")) speed.Run(float.Parse(line.Substring(6), System.Globalization.CultureInfo.InvariantCulture));
                else if (line == "force 0" || line == "force 1") rig.ForceRender = line == "force 1";
                else if (line == "flip 0" || line == "flip 1") rig.FlipY = line == "flip 1";
                else if (line == "stamp 0" || line == "stamp 1") rig.Stamp = line == "stamp 1";
                else if (line == "freeze 1" || line == "freeze 0")
                {
                    RenderTests.FreezeTurbulenceSetting = line == "freeze 1";
                    Log.LogInfo("freeze turbulence during our renders: " + RenderTests.FreezeTurbulenceSetting);
                }
                else if (line.StartsWith("stereo "))
                {
                    float sep;
                    try { sep = float.Parse(line.Substring(7), System.Globalization.CultureInfo.InvariantCulture); }
                    catch (System.FormatException) { Log.LogWarning("bad number: " + line); continue; }
                    tests.Stereo(sep);
                }
                else Log.LogWarning("unknown command: " + line);
            }
        }

        void Probe()
        {
            Log.LogInfo(string.Format("PROBE frame {0} ticks {1} time {2:F2} fixedDelta {3:F5} maxDelta {4:F5} targetFps {5} vsync {6} screen {7}x{8} telemetryBlocked {9}",
                Time.frameCount, ticks, Time.time, Time.fixedDeltaTime, Time.maximumDeltaTime, Application.targetFrameRate,
                QualitySettings.vSyncCount, Screen.width, Screen.height, TelemetryBlocked));
            foreach (var cam in Camera.allCameras)
            {
                var p = cam.transform.position;
                Log.LogInfo(string.Format("CAM {0} depth {1} ortho {2} fov {3:F2} aspect {4:F3} near {5} far {6} pos ({7:F2},{8:F2},{9:F2}) rot {10} mask {11:X8} clear {12} target {13}",
                    cam.name, cam.depth, cam.orthographic, cam.fieldOfView, cam.aspect, cam.nearClipPlane, cam.farClipPlane,
                    p.x, p.y, p.z, cam.transform.rotation.eulerAngles, cam.cullingMask, cam.clearFlags,
                    cam.targetTexture != null ? cam.targetTexture.name : "screen"));
            }
            var cur = Game.UI.Cameras.Current;
            if (cur != null)
            {
                var c = cur.Camera;
                var slots = FindObjectOfType<SaveSlotsUI>();
                Log.LogInfo("SLOTUI " + (slots != null ? slots.CurrentSlotIndex.ToString() : "none"));
                Log.LogInfo("GAMEPLAY state " + (GameStateMachine.Instance != null ? GameStateMachine.Instance.CurrentState.ToString() : "none") + " ori " + (Game.Characters.Current != null ? Game.Characters.Current.Position.ToString() : "none") + " camera " + c.name + " widthUnits " + cur.CameraWidthWorldUnits.ToString("F2") + " bounds " + cur.CameraBoundingBox);
                Log.LogInfo("VIEW\n" + c.worldToCameraMatrix);
                Log.LogInfo("PROJ\n" + c.projectionMatrix);
            }
            else Log.LogInfo("GAMEPLAY camera not set yet");
        }
    }

    // SteamTelemetry.Awake sends an event at once, then deaths and achievements follow (decomp SteamTelemetry.cs).
    // Steam achievements and stats go through Steam and are not touched by this.
    [HarmonyPatch(typeof(SteamTelemetry), "Send")]
    static class TelemetryPatch
    {
        static bool Prefix()
        {
            if (!Plugin.BlockTelemetry.Value) return true;
            Plugin.TelemetryBlocked++;
            return false;
        }
    }

    // TurbulenceManager.OnRenderObject moves the plant sway on by real time at every render of the main camera
    // (decomp TurbulenceManager.cs). The second eye must see the sway the first eye saw, so our extra renders skip it.
    [HarmonyPatch(typeof(TurbulenceManager), "OnRenderObject")]
    static class TurbulencePatch
    {
        static bool Prefix() { return !RenderTests.SkipTurbulenceNow && !(XrRig.SuppressFlat && !XrRig.InEyeRender); }
    }

    // With VR on, the flat render draws nothing and post-processing is skipped, so the eyes can use their own size.
    [HarmonyPatch(typeof(UberPostProcess), "OnPreRender")]
    static class FlatPreRenderPatch
    {
        static bool Prefix(UberPostProcess __instance) { return __instance.IsSubCam || !XrRig.SuppressFlat || XrRig.InEyeRender; }
    }

    [HarmonyPatch(typeof(UberPostProcess), "OnRenderImage")]
    static class FlatImagePatch
    {
        static bool Prefix(UberPostProcess __instance, RenderTexture source, RenderTexture destination)
        {
            if (__instance.IsSubCam || !XrRig.SuppressFlat || XrRig.InEyeRender) return true;
            Graphics.Blit(source, destination);
            return false;
        }
    }

    // SteamVR takes window focus. The game would pause and mute, so in VR it is told it still has focus.
    [HarmonyPatch(typeof(GameController), "OnApplicationFocus")]
    static class FocusPatch
    {
        static void Prefix(ref bool focusStatus)
        {
            if (Plugin.RigActive) focusStatus = true;
        }
    }

    // Cross fades alternate two views frame by frame and blend with the last one kept in a buffer. With VR on, each eye
    // needs its own buffer, the empty flat render must not take part, and the alternation moves on once per frame.
    [HarmonyPatch(typeof(UberPostProcessingCrossFade), "OnRenderImage")]
    static class CrossFadePatch
    {
        static readonly RenderTexture[] Buffers = new RenderTexture[2];
        internal static readonly bool[] Used = new bool[2];
        static readonly System.Reflection.FieldInfo CurrentField = AccessTools.Field(typeof(UberPostProcessingCrossFade), "m_currentFrame");
        static readonly System.Reflection.FieldInfo NextField = AccessTools.Field(typeof(UberPostProcessingCrossFade), "m_nextFrame");
        static readonly System.Reflection.FieldInfo CounterField = AccessTools.Field(typeof(UberPostProcessingCrossFade), "m_frameCounter");

        internal static int CurrentView(UberPostProcessingCrossFade fade) { return (int)CurrentField.GetValue(fade) == 1 ? 1 : 0; }

        static bool Prefix(UberPostProcessingCrossFade __instance, RenderTexture from, RenderTexture to)
        {
            if (!XrRig.SuppressFlat) return true;
            if (!XrRig.InEyeRender)
            {
                Graphics.Blit(from, to);
                return false;
            }
            int eye = XrRig.EyeIndex;
            var buffer = Buffers[eye];
            if (buffer == null || buffer.width != from.width || buffer.height != from.height)
            {
                if (buffer != null) UnityEngine.Object.Destroy(buffer);
                buffer = new RenderTexture(from.width, from.height, 0, RenderTextureFormat.ARGB32);
                Buffers[eye] = buffer;
                Used[eye] = false;
            }
            int current = (int)CurrentField.GetValue(__instance);
            int next = (int)NextField.GetValue(__instance);
            var mat = __instance.Material;
            float alpha = current == 1 ? 1f - __instance.TweenTime : __instance.TweenTime;
            mat.SetFloat(UberPostCacheIds.Alpha, Used[eye] ? alpha : 0f);
            mat.SetTexture(UberPostCacheIds.Buf, buffer);
            Graphics.Blit(from, to, mat);
            if (current != next)
            {
                Graphics.Blit(from, buffer);
                Used[eye] = true;
            }
            if (eye == 1)
            {
                CurrentField.SetValue(__instance, next);
                CounterField.SetValue(__instance, (int)CounterField.GetValue(__instance) + 1);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(UberPostProcessingCrossFade), "StartCrossFade")]
    static class CrossFadeStartPatch
    {
        static void Postfix()
        {
            CrossFadePatch.Used[0] = false;
            CrossFadePatch.Used[1] = false;
        }
    }
}
