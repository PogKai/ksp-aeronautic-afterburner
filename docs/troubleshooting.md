# Troubleshooting

[Back to the README](../README.md)

## No replacement plume

Enable **Afterburner plumes** in the flight toolbar and switch the engine to its wet mode. A running dry engine is not enough. Only engine IDs starting with `wet` or containing `afterburn` or `reheat` are recognised.

Keep all three supplied bundles in `GameData/AeronauticAfterburner/Shaders/` and the DLL in its `Plugins/` subfolder. Search `KSP.log` for `[AFTERBURNER]`: a missing or unsupported shader disables the replacement without hiding the original engine effect. Check for duplicate DLLs before reinstalling.

## Flame size or placement

Reset the length to 100% first. Plumes follow actual engine thrust, not just the throttle lever, so spool-up and flameout affect their size. Low output has a short flame without full shock diamonds.

The fitter measures the engine's own meshes. Unusual geometry can require a fallback radius, and rectangular apertures are still rendered round. To collect nozzle-fit diagnostics, create an empty `verbose.txt` in `GameData/AeronauticAfterburner/`, restart KSP, reproduce the issue and inspect `[AFTERBURNER] nozzle lip` in `KSP.log`. Remove the file afterwards to stop detailed logging.

## Settings do not persist

The mod needs write access to `PluginData/settings.cfg`. Look for `[AFTERBURNER] settings` warnings in `KSP.log`. With KSP closed, back up and remove that file to reset to enabled, orange, 100% length; it is recreated when settings are saved.

## Overlapping exhaust effects

Stock and Waterfall wet effects are suppressed only while the replacement is active. Other effect mods or nonstandard controller names may need separate compatibility work. Disable this mod's plumes to restore the stored original effect states, and report the engine/mod name and relevant log messages.
