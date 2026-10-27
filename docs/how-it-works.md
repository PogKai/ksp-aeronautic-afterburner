# How it works

[← Back to the README](../README.md)

## The model

The plume is a port of [aeronautic](https://github.com/Romainlg29/aeronautic)'s `@aeronautic/afterburner` by Romain Le Gall (MIT), at commit `673b967`. For each nozzle the CPU derives exit pressure and temperature, the potential core, shock-cell spacing and flame reach from engine output, flight speed and ambient air. The GPU marches through that field with advected noise and draws the glowing gas. Blue mode is a KSP-specific artistic colour palette, not a separate physical combustion model.

## Which engines get a plume

Any engine in a wet mode: a mode whose name starts with `wet` or contains `afterburn` or `reheat` (KSP's stock multi-mode jets, and most mod jets). An oddly named afterburner mode is not recognised.

While the plume is drawn, the engine's own wet effect is hidden: stock wet emitters and Waterfall's wet throttle controllers. They come back exactly as they were when the engine switches to dry, the plumes are switched off, or the craft unloads. Dry modes keep their own effects.

## Fitting the nozzle

The plume starts at the nozzle's exit, measured from the engine's own mesh: the forward rim of the exhaust, its plane and its radius, following animated and gimballed nozzles (refreshed every 0.05 s, at once when the gimbal moves). The plume is round and no wider than the exit. Without a usable mesh it falls back to 0.65 of the part's radius and says so in the log.

## The look

`AfterburnerLook` in `Source/PlumeDraw.cs` holds the tuning:

* The burner follows the engine's actual output (thrust over maximum): the first zone lights at once, the rest stages in over about 1.6 s, and it goes out faster than it lights. No output, no flame
* Flame length 2.5 to 8 exit diameters and about 5.5 shock cells; the length grows from half at rest to full at 300 m/s. The jet's expansion reads airspeed only up to 150 m/s, so a fast KSP jet does not swell without limit
* The player can pick a Sukhoi-style blue flame: low-emission edges grade to deep cobalt, the body stays a nearly cylindrical cyan-blue column, and paired axisymmetric compression/reflection cones repeat about once per nozzle diameter without turbulent density breakup. The burner face has subtle blue-violet structure. The maximum length can be scaled from 50% to 200%
* The sheath flows and the diamonds shiver while the output changes, and settle at a steady reheat
* Departures from upstream: brighter cells (barrel bow 0.18, fan cooling 0.12 against upstream's 0.35 and 0.6, too dim in KSP), a steady flutter of 0.25 on the sheath, and one draw per nozzle. No heat haze, scene fog or sun scattering

## Limits

* Rectangular or two-dimensional nozzles are drawn round
* It needs `Shaders/afterburner-<platform>.bundle` and a graphics API that can run it (Direct3D 11, OpenGL Core, Metal or Vulkan); without it the mod logs `[AFTERBURNER] no shader` and draws nothing

## Source map

The [source repository](https://github.com/PogKai/ksp-aeronautic-afterburner/tree/main/Source) contains:

| File | Responsibility |
| --- | --- |
| `AfterburnerPlumes.cs` | Engine discovery, wet/dry transitions, nozzle placement and original-effect restoration |
| `AfterburnerSettings.cs` | Saved settings, flight window and generated toolbar icon |
| `PlumeModel.cs` | CPU jet state, blackbody lookup and noise generation |
| `Atmosphere.cs` | Standard-atmosphere temperature and pressure |
| `NozzleLip.cs` | Aperture rim, centre, radius and plane fitting |
| `PlumeDraw.cs` | KSP visual tuning, shared mesh, material and per-nozzle GPU parameters |
| `PlumeShaders.cs` | Platform bundle loading and shader availability |
| `AssemblyInfo.cs` | Assembly identity and KSP version metadata |
| `Shaders/Afterburner.shader` | Hull-rendering pass and shader entry points |
| `Shaders/AfterburnerPlume.cginc` | Raymarch, emission, shock cells and blue palette |
| `Shaders/AgxTonemap.cginc` | Display tone mapping |

Nozzle meshes are refreshed every 0.05 seconds, or immediately after a thrust-transform rotation change. The full fitted centre is retained. A small tilt-dependent inset overlaps the aperture rim; the craft's depth texture hides the interior part of the plume.

Colour changes blend over time, while the length control scales the existing airspeed-dependent range. Settings do not replace the engine's thrust or fuel model. Wet effect states are saved before suppression and restored on dry-mode changes, disabling the mod or unloading a tracked craft.
