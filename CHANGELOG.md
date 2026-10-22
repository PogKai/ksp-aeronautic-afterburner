# Changelog

All notable changes to Aeronautic Afterburner.

---

## 1.0.0 — First release

Split out of WingtipVortex, where it was previewed in pre-release builds, as a standalone mod.

* Raymarched afterburner plumes for engines in a wet mode, ported from aeronautic's `@aeronautic/afterburner` (`673b967`)
* The jet follows the engine's actual output, airspeed and the air; staged light-up, shock diamonds at full reheat
* Plumes start at the measured exit of the engine's own mesh, following animated and gimballed nozzles
* The engine's stock or Waterfall afterburner effect is hidden while the plume is drawn and restored afterwards
* A flight toolbar button opens a window: plumes on or off, **orange or blue flame**, and the flame's **maximum length** from 50% to 200%; the choices are saved
