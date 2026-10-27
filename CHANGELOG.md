# Changelog

All notable changes to Aeronautic Afterburner.

---

## 1.1.0 — Refined afterburner plumes

* Blue mode now uses a collimated, nearly cylindrical exhaust column with tightly packed, roughly one-nozzle-diameter axisymmetric compression/expansion cells, plus subtle blue-violet structure on the burner face
* Default plume reach is reduced from 3–14 to 2.5–8 nozzle diameters; the 50–200% player length control remains available
* Blue plumes now keep a straight, fixed-radius axis and use a long transparent terminal fade instead of ending on a visible square volume cap
* Blue pressure cells are confined inside a flat-density cylindrical envelope so their arches no longer create a bubbled outer silhouette
* Blue shock highlights are now discrete transverse annular disks with dark gaps instead of connected compression/expansion cone ribbons
* Orange mode now shares the corrected tapered envelope, softened downstream cells and gradual tail dissipation, with a stronger blue-violet night sheath around the warm core
* Burner illumination is colour-correct in blue mode, while the orange burner source is smaller, recessed behind the nozzle lip and depth-tested against modeled engine interiors
* Wet/dry transitions retain the selected orange or blue mode without briefly flashing orange, and pressure cells remain stable during engine startup

## 1.0.0 — First release

Standalone afterburner plumes, previously previewed alongside WingtipVortex.

* Raymarched exhaust based on aeronautic's `@aeronautic/afterburner` at `673b967`
* Output-driven light-up, shrinkage, shock diamonds and wet-to-dry transitions
* Measured nozzle placement that follows animated and gimballed exhausts
* Replacement of stock and optional Waterfall wet effects, with original states restored afterwards
* In-flight toolbar window: plumes on/off, orange or Sukhoi-style blue, and 50–200% maximum length
* Blue mode uses cobalt edges, a cyan body and an icy-white hot core, rather than swapping orange colour channels
* Saved settings, a generated toolbar icon and bundled Windows, macOS and Linux shaders
* Clean runtime-only installation ZIP, current documentation and upstream license notices
* Explicit local KSP build path and release binaries without embedded workstation debug paths
