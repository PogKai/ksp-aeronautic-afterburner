// Runs aeronautic's afterburner physics (AERONAUTIC_DIR at 673b967) for the C# port's check.
//   AERONAUTIC_DIR=... node --import ./register.mjs afterburner-ref.ts
import { writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const upstream = resolve(here, process.env.AERONAUTIC_DIR ?? "../../../aeronautic");
const src = (file: string) => import(pathToFileURL(resolve(upstream, "packages/afterburner/src", file)).href);

const profile = await src("plume-profile.ts");
const presets = await src("presets.ts");
const blackbody = await src("blackbody.ts");
const noise = await src("noise-volume.ts");

const cases = [];
for (const throttle of [0, 0.5, 1, 1.01, 1.05, 1.1])
  for (const altitudeM of [0, 900, 3000, 11000])
    for (const airspeedMPerS of [0, 150, 300, 500])
      for (const nozzleRadiusM of [0.3, 0.5]) {
        const params = presets.resolve_afterburner_params({ nozzleRadiusM }, "afterburner");
        const prof = presets.resolve_afterburner_profile({ altitudeM, airspeedMPerS }, "afterburner");
        cases.push({ throttle, altitudeM, airspeedMPerS, nozzleRadiusM, state: profile.jet_state(params, throttle, prof) });
      }

const lut = [];
for (const k of [500, 900, 1500, 2000, 3000, 6000]) lut.push({ k, rgb: blackbody.blackbody_rgb(k), lum: blackbody.blackbody_luminance(k) });
const red = noise.build_noise_volume(64, 16, 0), green = noise.build_noise_volume(64, 16, 1);
const sample = (b: Uint8Array) => Array.from({ length: 64 }, (_, i) => b[i * 4099 % b.length]);
writeFileSync(resolve(here, "afterburner-ref.json"), JSON.stringify({ cases, lut, red: sample(red), green: sample(green) }));
console.log(cases.length, "cases");
