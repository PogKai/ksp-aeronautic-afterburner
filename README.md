# Aeronautic Afterburner

Raymarched afterburner plumes for Kerbal Space Program aircraft: a glowing, flowing jet with shock diamonds that grows and shrinks with the engine, in place of the engine's own afterburner effect.

## What it does

* **Every afterburning jet** gets a plume in its wet mode: stock multi-mode jets and most mod jets. Dry modes keep their own effects
* **Follows the engine**: lights in stages, grows with thrust and airspeed, shows shock diamonds at full reheat, and goes out when you throttle back or switch to dry
* **Fits the nozzle**: starts at the measured exit of the engine's own mesh, following animated and gimballed nozzles
* **Replaces, doesn't stack**: the engine's stock or Waterfall afterburner effect is hidden while the plume is drawn, and restored afterwards

## Install

1. Download the zip from the latest release
2. Copy the `AeronauticAfterburner` folder (inside the zip's `GameData`) into your KSP `GameData/`
3. Fly

KSP 1.12.x · no dependencies · works on its own or alongside [WingtipVortex](https://github.com/PogKai/ksp-wingtip-vortex) · to remove, delete the folder.

## Settings

In flight, the flame button on the app launcher turns the plumes on (lit) and off. The choice is saved to `PluginData/settings.cfg` in the mod's folder. An empty file named `verbose.txt` next to `Plugins` logs each nozzle fit to `KSP.log` (search for `[AFTERBURNER]`).

## Learn more

* [How it works](docs/how-it-works.md): the model, which engines get a plume, the nozzle fit and the look
* [Changelog](CHANGELOG.md)

## Building from source

* The DLL: `dotnet build -c Release -p:KSPDir=<your KSP folder>` builds `bin/Release/AeronauticAfterburner.dll`
* The shader bundles (Unity 2019.4.18f1, KSP 1.12's version): `unity/build.ps1` writes `Shaders/afterburner-{windows,macos,linux}.bundle`
* Offline checks (no KSP or Unity): `cd tools/check; dotnet run -c Release -- --reference afterburner-ref.json` (against upstream's TypeScript), `-- --nozzle` (the nozzle fit)
* The look, outside KSP: `unity/plume.ps1 -Variants 'now:;dim:exposure=40'` renders panels to `unity/Bundles/plume/`
* The release zip: `python tools/package.py`

## Credits

The plume model and shader are ported from [aeronautic](https://github.com/Romainlg29/aeronautic) by Romain Le Gall, used under the MIT License: see [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md). Each ported file names its upstream source.

## License

* **PogKai-authored material:** [CC BY-NC-SA 4.0](license.md). Non-commercial use, modification and redistribution are permitted with attribution, a license link and an indication of changes. Shared adaptations of that material must use the same license.
* **Upstream aeronautic portions:** retain the **MIT License**, including its permission for commercial use, subject to preserving the copyright and license notices. The CC non-commercial and share-alike conditions do not replace those upstream permissions.

See [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md) and the notices in the ported source files.

## AI disclosure

Built with help from Claude, Anthropic's AI assistant. The ideas, direction, KSP integration and all in-game testing are PogKai's.
