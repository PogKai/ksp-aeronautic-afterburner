# Development history

The afterburner was developed inside WingtipVortex until October 22, 2026, when it became this mod. This log is kept as it was written: its paths (`Source/WingVapor/AfterburnerPlumes.cs`, `tools/aeronautic-check`, `releases/...`) refer to the WingtipVortex repository; here they are `Source/AfterburnerPlumes.cs`, `tools/check` and `unity/`.

The numerical core ports Aeronautic `@aeronautic/afterburner` at673b967. October13 check:5506values,0 failures, worst relative error7.42e-8. Latest upstream HEAD inspected:db04669b94ce7aa81ccf1ab87dd229e18f78456f. Numerical parity does not certify attachment to every KSP engine. See [current native qualification](../history/showcase-su27-nozzles-2026-10-13.md).

## Engine attachment

`Source/WingVapor/AfterburnerPlumes.cs` recognizes engine IDs beginning with `wet` or containing `afterburn`/`reheat`, including wet-primary and standalone wet engines. Active mode comes from the actual primary/secondary ID. An arbitrarily named wet mode still needs explicit identification; do not claim universal compatibility.

The fitter measures the rear mesh lip in each thrust transform's frame, updates every0.05s, and follows animated skinned meshes. Exit position and horizontal/vertical radii drive the plume; elliptical radii are supported, but a rectangular/superellipse aperture is not. Missing/invalid geometry uses an explicitly logged fallback:0.65 of the part radius and the historical mode-based closure interpolation. Measured openings do not use synthetic closure scaling.

Unity2019 `BakeMesh` includes scale. The fitter uses position and rotation for baked vertices and the full transform for static vertices. Applying scale twice made the half-size Falcon aperture quarter-size and displaced its exit. `NozzleBakeProbe.Run` validates24 animated vertices across0.5/1/2 and nonuniform scales, rotated transforms and two opening poses. [Unity2019 API](https://docs.unity3d.com/2019.4/Documentation/ScriptReference/SkinnedMeshRenderer.BakeMesh.html).

Stock wet emitter objects and wet Waterfall throttle controllers are hidden while the replacement runs. Their original active/override values restore on dry switch, disabling, unloading or destruction. Shared smokePoint objects remain active so dry effects survive.

## Look and deliberate departures

The CPU computes jet state per nozzle; the shader ports the pressure/temperature field, advected noise and optical march. `AfterburnerLook` is the authoritative tuning source:

- Burner follows actual wet thrust/max thrust, with first-zone ignition, staged growth and faster shutdown. Empty output produces no replacement flame.
- Exposure40, pressure ratio1.7, additional afterburning150K, turbulence0.8 and meander0.12. Optical thickness adjusts with nozzle size.
- Flame length3–14 exit diameters and shock train5.5cells. Speed-dependent length grows from half at rest to full at300m/s; inlet expansion uses a150m/s cap to avoid oversized KSP jets.
- Barrel bow0.18 and fan cooling0.12 retain some cell structure. Upstream0.35/0.6 was too dim in the existing KSP comparison.
- Custom steady flutter0.25 keeps sheath motion at constant reheat; extra surge pulses occur while output changes. The potential core and shock train are comparatively steady upstream. Do not make diamonds drift randomly to disguise this.
- No heat haze, scene fog or particle sun/sky scattering. One draw per nozzle rather than an instanced draw.

## Qualification

Use `dotnet run --project tools/aeronautic-check -c Release -- --afterburner tools/aeronautic-check/afterburner-ref.json` for numerical parity. Unity `AfterburnerHarness.Render` compares size, opening, ellipse and temporal variants at1440p. Native mode cycles must verify nozzle attachment, dry restoration, wet reopening and full-output flowing tail with the installed models.

October13 first native cycle: Su27/Panther and MiG21/Panther completed720frames each; Phantom/Falcon exposed the scale bug; Raptor cycle ended early from low-throttle flight instability. Corrected Falcon attachment was subsequently inspected in Phantom Quarter, Eagle and Hornet side footage: aperture radii and exit offsets follow the animated nozzle instead of the twice-scaled mesh. Phantom's 2.6s replacement is ready for review. The final four-aircraft constant-reheat run recorded240frames each with zero uneven ticks. Keep these qualifications distinct from an incomplete Raptor wet/dry cycle or arbitrary installed wet modes. A fallback or a stable-looking photograph alone is not proof of animated lip fit.
## Current appearance — October 13 rollback

Sawyer requested the pre-today afterburner appearance during the Omega edit. Runtime plume placement/sizing now uses the previous thrust-transform origin, circular cross-section, fixed `0.65 * PartRadius` estimate, and synthetic wet/dry opening interpolation from60% to100%. Animated lip fitting, measured exit offsets and elliptical runtime scaling are disabled. The plume profile/shader tuning was already the pre-today look and remains unchanged. Wet-primary/standalone mode recognition and original stock/Waterfall state restoration are retained as compatibility fixes; this is an appearance rollback, not a byte-for-byte rollback of those safety fixes.

The rollback DLL built and was deployed only to Desktop with a matching SHA256. Afterburner numerical check:5506values,0failures,worst7.42e-8. No shader bundle change required. No fresh native flight was launched for this restoration; prior legacy footage and build/math checks are the evidence. Wing-vapor/rope changes remain intact. The measured fitter and its tests remain available offline but are not used by `AfterburnerPlumes`. Backup source before rollback: `releases/archive/reviews/afterburner-rollback/AfterburnerPlumes-nozzle-fit.cs.txt`.

Recorded October13 retake clips and the Omega montage retain their capture-time nozzle-fit effects. A runtime rollback cannot change those video pixels. The attachment audit below describes the superseded fitter and its limitations, not the active appearance.


## October 13, evening: measured exit restored, output-driven jet

Sawyer asked that plumes not clip out of the nozzle and that they grow and shrink with the engine. Checked against upstream `@aeronautic/afterburner` (README and `types.ts`): `nozzleRadiusM` is the real exit radius, the plume starts at the exit, and throttle (0 to 1.1) drives exit pressure, temperature and burner staging, with the plume easing toward it.

- `AfterburnerPlumes.cs` again uses the measured lip fitter (the archived version in `releases/archive/reviews/afterburner-rollback/`), so position and radius follow the animated nozzle mesh. The rollback's fixed `0.65 * part radius` remains only as the fallback when no mesh is measured.
- The jet is now computed at the engine's eased output (thrust over maximum) instead of always at full throttle; the unmeasured fallback opening also scales with it.
- Numerical parity unchanged: 5,506 values, 0 failures, worst 7.42e-8. Native cycle `20261013-225047-27036` (Su-27, Raptor, F-4, MiG-21): the log shows measured lips following the nozzle (Panther 0.277 m dry to about 0.46 m wet); the MiG-21 frame shows the plume emerging inside the nozzle aperture. The Su-27 and Raptor tail cameras sat too close to show the plume, so those two are not visually confirmed, and the Raptor cycle ended at 194 frames.
- The earlier appearance request still stands as a decision for Sawyer: this restores the fitted look. Backup of the replaced file: `..\WingtipVertex _testing-backup-2026-10-13\repo\Source\WingVapor\AfterburnerPlumes.cs`.

### Same evening: overfill and gimbal flattening

Sawyer's screenshots showed the plume wider than the nozzle and, when the engine gimballed, flattened. Causes: the jet's expanded radius (up to 6x the exit radius) was drawn from the exit, and the elliptical lip fit became lopsided when the gimballed nozzle rotated against the housing. Now the plume is round, its radius is capped at 0.92 of the larger fitted exit radius, and the exit uses only the fitted depth along the thrust axis (clamped to 0.5 m). Checked on the MiG-21 from above in run `20261013-230830-44912`: plume width matches the nozzle. A gimballed view is not yet checked.

### Follow-up evidence: detached root under gimbal

Sawyer supplied NVIDIA screenshots `2026.10.13 - 23.15.13.80` and `23.15.05.53`: the plume has a sharply bounded root at the nozzle; Sawyer reports an exposed gap during gimbal deflection. This is not qualified as fixed. The current draw rotates with the thrust transform, but placement discards the fitter's `Up` and `Side` offsets and clamps `Aft` to +/-0.5 m. `NozzleLip.Fit` also takes the furthest aft mesh vertex and a narrow depth band; a tilted lip can make that band describe one edge rather than the complete exit plane. These are code-supported suspects, not a reproduced diagnosis.

Next attachment check: measure the lip center and plane at neutral and both extremes of pitch/yaw, from side and rear cameras. Distinguish fitting error, discarded center offsets and stale fit timing (0.05 s updates). Fix the attachment geometry before softening the visual root; a feathered edge alone must not conceal a detached plume. Preserve the round aperture and output-driven behavior while testing.

### October 14 qualification: fitted plane and attached root

The old narrow aft-depth band failed tilted-aperture regression cases. The fitter now selects the forward rim around angular sectors, fits its plane, and measures all vertices on that plane. The draw uses the complete measured center, without the previous +/-0.5 m axial clamp. Its root is inset by the plane's tilt across the radius plus a 4% overlap; the craft depth hides the interior segment. Round sizing and output-driven staging remain. Gimbal orientation changes refresh the fit immediately; unchanged poses retain the 0.05 s cadence.

79 focused geometry checks pass, including neutral/combined pitch-yaw tilt, four radii (0.1–1 m), aperture closure, and full-rim overlap. Vapor parity: 387,018 values; afterburner parity: 5,506 values, zero failures. GPU field/depth suite passes. All platform bundles rebuilt.

Native checks: MiG-21/F-4 side cycles in `20261013-233857-40568`, Su-27 attached-rear cycle in `20261013-235301-47716`, and final Raptor attached-rear cycle `20261014-000400-28404`. Each qualified clip has 720 native frames at 2560x1440/60 fps, zero uneven physics ticks; the inspected wet roots remain attached and dry mode restores the stock effect. Native deflections are those generated by the test's flight controller; full combined tilt coverage is numerical, not a claim of native testing at every possible gimbal setting.

The Raptor initially spun under the legacy solo engine-cycle control; this was rejected. Engine cycling now uses `FormationLead` in Level mode with an explicit throttle override. A 245 m/s reactive cutoff overshot to 258 m/s because of engine spool delay; the override now respects a 1.2 s acceleration prediction at 240 m/s and brake hysteresis at 235/231 m/s. Cycling ends directly after filming, avoiding an unnecessary recovery. Final Raptor: 1.2 g peak, 240 m/s top, bank -14.01 to 0.12 degrees, maximum axial rate 7.12 degrees/s. This qualifies the diagnostic, not the wider dogfight/winner program.

Evidence: `releases/diagnostics/mig-gimbal-2026-10-14`; native proof clips: `releases/clips/nozzle-validation-2026-10-13`. The montage was not rebuilt. Final deployed Desktop DLL SHA256: `410787033673532D326622696DB0510172CB108E0C9B900F54CCAED6DD4D2492`.

