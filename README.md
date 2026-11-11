# Ori VR: Ori and the Blind Forest in VR

True stereo VR for Ori and the Blind Forest: Definitive Edition. Both eyes are drawn from the same game tick, so there is no alternate-eye shimmer and no warping. The game becomes a diorama floating in front of you: its real depth layers stay real, so backgrounds sit behind the stage and the foreground comes out toward you. You play with a gamepad; there are no motion controls.

**Download:** see [Releases](../../releases).

**Support:** Ori VR is free. If you enjoy it, you can [buy me a coffee](https://buymeacoffee.com/scubasteve2365).

## What you need

- The Steam version of Ori and the Blind Forest: Definitive Edition
- A PC VR headset with a 32-bit OpenXR runtime, because this game is 32-bit. Two have been tested:
  - SteamVR 2.17 or newer, set as your OpenXR runtime (SteamVR settings, OpenXR)
  - Virtual Desktop's VDXR, for headsets like the Meta Quest 3
- A gamepad

## Install

1. Unzip the release anywhere.
2. Double click `Install OriVR.bat` and type 1. It finds the game through Steam.
3. Start SteamVR or connect Virtual Desktop, then start Ori from Steam. VR starts by itself.

To uninstall, run the same bat file and type 2. No game files are changed.

The mod is not signed, so Windows may warn you about the bat file. It is plain text, so you can read it first. Windows Smart App Control can block unsigned mod DLLs. If it is on and the game stays flat, that is the likely cause.

If the game stays flat for another reason, click both sticks to open the menu. It tells you if VR did not start and why. Ori is a 32-bit game, so it has its own OpenXR runtime setting, separate from 64-bit games. If that setting is empty or unusable, the mod uses SteamVR for this game when SteamVR 2.17 or newer is installed. Nothing on your PC is changed.

## Settings

Click both sticks at once to open the menu in the headset, on an Xbox pad or a PlayStation DualSense or DualShock 4, or press Insert on the keyboard. The game pauses while it is open. Up and down choose a setting, left and right change it, A (Cross) selects and B (Circle) closes. On the keyboard: arrow keys choose and change, Enter selects, Insert, Esc or Backspace closes. The key can be changed in the config file (`[Menu] OpenKey`). While the menu is open the game gets no input.

PlayStation pads (DualSense, DualSense Edge, DualShock 4): not fully verified yet. The best way is **Steam Input on** for Ori (right-click Ori in Steam, Properties, Controller, Enable Steam Input): Steam presents the pad as an Xbox controller and everything maps correctly. With Steam Input off, the mod reads the pad itself and passes it to the game as an Xbox pad (Cross is A, Circle B, Square X, Triangle Y, Options Start), over USB or Bluetooth; that path has had little testing. The game's own button prompts always show Xbox buttons.

Refresh rate: 72, 80 or 90 Hz. Ori gains nothing above 90 Hz, and over a wireless stream (Virtual Desktop, Steam Link, Air Link) a higher rate mostly adds late frames and hitches. Frame catch-up (in the menu, On by default) keeps the game at normal speed when frames come late.

| Setting | What it does |
|---|---|
| Stage distance, Stage size | Where the stage sits and how big it is |
| Depth | How strong the 3D is. Higher makes the world feel smaller and deeper |
| Pop-out | Brings more of the scene toward you, and eases far backgrounds |
| Sharpen | Crisper edges. Too high looks harsh |
| Resolution | 100% matches your headset. Higher costs speed and adds little in this game |
| Picture | Auto picks Steady on Virtual Desktop and Floating Panel on everything else. Steady draws the stage into each eye's view and holds still under Virtual Desktop. Floating Panel can look a little sharper, but Virtual Desktop squashes and shears it when you turn your head |
| Surround | What you see around the stage. Black is the original look. Green fills it with pure green for Virtual Desktop's chroma-key passthrough, so your room shows around the game. Transparent asks your headset software to show your room itself, where it offers that, otherwise black. Ambient lights the room around the stage with the game's own colours (see below) |
| Frame catch-up | On: when your PC misses a headset frame, the game makes up the lost time on the next one, so it never runs in slow motion. Off: the game slows down a little whenever frames are missed, as the original game does |
| Recenter | Puts the stage in front of you again |

If far backgrounds strain your eyes, lower Depth or raise Pop-out.

### Ambient surround

Surround: Ambient turns the dark around the stage into a room lit by the game, like Ambilight but far bigger. It always uses Steady.

- **Wall glow:** a large virtual wall just behind the stage glows with the colours at the nearest stage edge, bright near the edge and fading into soft clouds several metres out.
- **Room fill and flashes:** the whole room, to your sides and behind you too, takes on the scene's colour. Brightness changes punch through: a part of the picture that suddenly brightens flares its side of the room, and a flash anywhere (Charge Flame, Light Burst) lights the whole room in its colour for a moment.
- **Overscan:** the game is rendered wider than the stage. Past the stage edge the background fades out at once, but effects that stand out from their surroundings (embers, sparks, spirit light, flashes) carry on, so they seem to escape the screen. The HUD stays on the stage. While overscan is on, the game keeps objects switched on a little further outside its own camera.

Its settings are in the `[Ambient]` section of `BepInEx\config\com.scubasteve2365.orivr.cfg` (each is described there): `Strength`, `Spread`, `RoomFill`, `WallDistance`, `Smoothing`, `React`, `AdaptTime`, `Flash`, `Overscan` (1 is off), `OverscanFade`, `OverscanEffects`, `OverscanEffectsReach`, `OverscanEffectsThreshold`. With the default stage size the stage fills much of your view; a smaller or more distant stage leaves more room for the light.

## Good to know

- The game runs one tick per headset frame, so it follows your headset's refresh rate at normal game speed.
- Tested in real headsets on two setups: Pimax Dream Air through SteamVR, and Meta Quest 3 through Virtual Desktop's VDXR. Other headsets and graphics cards should work through OpenXR but have not been tried. Reports are welcome.
- There is no DLSS. The game is 32-bit, and DLSS needs a 64-bit game.
- The mod stops the game's telemetry.
- If something goes wrong, the log is `BepInEx\LogOutput.log` in the game folder. Please attach it to an issue.

## Support

Ori VR is free. If you enjoy it and want to support more VR mods, you can
[buy me a coffee](https://buymeacoffee.com/scubasteve2365). Thank you!

## Building from source

You need the game installed, Visual Studio's C++ tools with CMake, the .NET SDK, and the Khronos [OpenXR loader](https://github.com/KhronosGroup/OpenXR-SDK-Source/releases) for Windows. BepInEx 5 x86 must be installed in the game folder, because the build references it.

```
cmake -S native -B native/build -A Win32 -DOPENXR_DIR=<OpenXR loader folder>
cmake --build native/build --config Release
dotnet build OriVR.csproj -c Release -p:OriDir="<game folder>"
```

The output lands in `..\build`.

## Credits

Built on [BepInEx](https://github.com/BepInEx/BepInEx) (LGPL-2.1) and the Khronos [OpenXR loader](https://github.com/KhronosGroup/OpenXR-SDK-Source) (Apache-2.0). The sharpening pass follows the idea of AMD FidelityFX Contrast Adaptive Sharpening (MIT). Licences are in the release zip.

The Steady picture mode was contributed by [junglejuz](https://github.com/junglejuz).

OriVR is MIT licensed, by scubasteve2365. This is a fan mod, not made or approved by Moon Studios or Xbox Game Studios. It contains no game files.
