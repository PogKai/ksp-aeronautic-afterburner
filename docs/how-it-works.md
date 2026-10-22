# How it works

[← Back to the README](../README.md)

## The model

The plume is a port of [aeronautic](https://github.com/Romainlg29/aeronautic)'s `@aeronautic/afterburner` by Romain Le Gall (MIT), at commit `673b967`. For each nozzle the CPU works out the jet's state from the engine's output, the flight speed and the air (`Source/PlumeModel.cs`): exit pressure and temperature, the potential core, the shock-cell (diamond) spacing and how far the flame reaches. The GPU then marches through the pressure and temperature field with advected noise and draws the glowing gas (`unity/Assets/Shaders/AfterburnerPlume.cginc`). The numbers match upstream's TypeScript to within 1e-7 (`tools/check`, 5,506 values).

## Which engines get a plume

Any engine in a wet mode: a mode whose name starts with `wet` or contains `afterburn` or `reheat` (KSP's stock multi-mode jets, and most mod jets). An oddly named afterburner mode is not recognised.

While the plume is drawn, the engine's own wet effect is hidden: stock wet emitters and Waterfall's wet throttle controllers. They come back exactly as they were when the engine switches to dry, the plumes are switched off, or the craft unloads. Dry modes keep their own effects.

## Fitting the nozzle

The plume starts at the nozzle's exit, measured from the engine's own mesh: the forward rim of the exhaust, its plane and its radius, following animated and gimballed nozzles (refreshed every 0.05 s, at once when the gimbal moves). The plume is round and no wider than the exit. Without a usable mesh it falls back to 0.65 of the part's radius and says so in the log.

## The look

`AfterburnerLook` in `Source/PlumeDraw.cs` holds the tuning:

* The burner follows the engine's actual output (thrust over maximum): the first zone lights at once, the rest stages in over about 1.6 s, and it goes out faster than it lights. No output, no flame
* Flame length 3 to 14 exit diameters and about 5.5 shock cells; the length grows from half at rest to full at 300 m/s. The jet's expansion reads airspeed only up to 150 m/s, so a fast KSP jet does not swell without limit
* The sheath flows and the diamonds shiver while the output changes, and settle at a steady reheat
* Departures from upstream: brighter cells (barrel bow 0.18, fan cooling 0.12 against upstream's 0.35 and 0.6, too dim in KSP), a steady flutter of 0.25 on the sheath, and one draw per nozzle. No heat haze, scene fog or sun scattering

## Limits

* Rectangular or two-dimensional nozzles are drawn round
* It needs `Shaders/afterburner-<platform>.bundle` and a graphics API that can run it (Direct3D 11, OpenGL Core, Metal or Vulkan); without it the mod logs `[AFTERBURNER] no shader` and draws nothing
