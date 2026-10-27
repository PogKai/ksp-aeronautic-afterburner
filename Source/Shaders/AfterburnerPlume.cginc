// The afterburner's jet field and march (Afterburner.shader). Round nozzles; no heat haze or scene fog.
// Ported from aeronautic (github.com/Romainlg29/aeronautic @ 673b967), packages/afterburner/src/
// afterburner-material.ts, tsl/plume.ts, tsl/noise.ts and tsl/blackbody.ts.
// MIT License, (c) 2026 Romain Le Gall; see THIRD-PARTY-NOTICES.md.
#ifndef AFTERBURNER_PLUME_INCLUDED
#define AFTERBURNER_PLUME_INCLUDED
#include "UnityCG.cginc"
#include "AgxTonemap.cginc"

#define PLUME_EPSILON 1e-5
#define FIELD_EXTENT 2.8
#define EDDY_REACH 0.35
#define BARREL_RIM 0.55
#define BARREL_WIDTH 0.06
#define BARREL_HEAT 0.55
#define SOOT_FLAME_LOW 1200.0
#define SOOT_FLAME_HIGH 1500.0
#define REFERENCE_K 2000.0
#define BLACKBODY_MIN_K 500.0
#define BLACKBODY_MAX_K 6000.0
#define BLACKBODY_TEXELS 256.0
#define NOISE_PERIOD 16.0
#define WANDER_GRADE (0.25 / 0.184)
#define WANDER_REACH (0.5 * (0.25 / 0.184))
#define OPAQUE_TRANSMITTANCE 0.01
#define GAUSSIAN_DEPTH 2.13
#define SAMPLES_PER_CELL 5.0
#define SAMPLES_ACROSS_CORE 4.0
#define FINE_STEP_CAP 2.0
#define SHOCK_TRAIN_LENGTHS 3.0
#define TURBINE_DEPTH 2.65
#define PIPE_GLOW_LIP 0.55
#define PIPE_GLOW 0.8
#define MAX_LOOP 384
static const float3 SOOT_SPECTRUM = float3(550.0 / 610.0, 1.0, 550.0 / 465.0);
static const float3 LUMINANCE = float3(0.2126, 0.7152, 0.0722);

sampler2D _Blackbody;
sampler3D _Noise;
UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

// Per nozzle (see AfterburnerPlumes.Write).
float4 _Jet;      // radius, core length, reach, velocity
float4 _Heat;     // temperature, afterburning + kinetic, shock heat, shock length
float4 _Cell;     // shock spacing, first disk, spread near, spread far
float4 _Optics;   // soot (as formed), -, particles, albedo
float4 _Exit;     // lip temperature, compression, soot survival, adaptation
float4 _Band;     // rgb, strength (only while the burner is lit)
float _Blue;      // 0 orange, 1 blue (not upstream)
float4 _Misc;     // turbulence, meander, refraction, seed
float4 _Outer;    // outer far, reheat, budget, thinning
float4 _Glow;     // glow, nozzle radius, glow temperature, pipe flame
float4 _Hull;     // outer near, lod, octaves
// Shared.
float _AmbientK, _Exposure, _SkyLight, _Daylight, _ShockRelaxation, _ActivationK, _EddyStretch, _ShutterS;
// Upstream: 0.35 and 0.6. 0, 0: a straight sheath, no bulge or cooling round each diamond.
float _BarrelBow, _FanCooling, _SteadyFlutter;
float _MeanderScale, _StepSafety, _DitherScale, _PlumeTime, _NearSteps, _MidSteps, _GammaOut;

// The jet as the fragment reads it.
#define J_RADIUS _Jet.x
#define J_CORE _Jet.y
#define J_REACH _Jet.z
#define J_VELOCITY _Jet.w
#define J_TEMPERATURE _Heat.x
#define J_AFTERBURNING _Heat.y
#define J_SHOCK_HEAT _Heat.z
#define J_SHOCK_LENGTH _Heat.w
#define J_SPACING _Cell.x
#define J_FIRST_DISK _Cell.y
#define J_SPREAD_NEAR _Cell.z
#define J_SPREAD_FAR _Cell.w
#define J_SOOT _Optics.x
#define J_PARTICLES _Optics.z
#define J_ALBEDO _Optics.w
#define J_LIP _Exit.x
#define J_COMPRESSION _Exit.y
#define J_SOOT_SURVIVAL _Exit.z
#define J_TURBULENCE _Misc.x
#define J_MEANDER _Misc.y
#define J_SEED _Misc.w
#define J_OUTER_FAR _Outer.x
#define J_REHEAT _Outer.y
#define J_THINNING _Outer.w

struct v2f
{
    float4 pos : SV_POSITION;
    float3 local : TEXCOORD0;
    float4 screen : TEXCOORD1;
    float3 view : TEXCOORD2;
};

// The unit hull (x 0 to 1, radius 1) opened to the field's extent at each end.
v2f vert(float4 vertex : POSITION)
{
    v2f o;
    float3 local = float3(vertex.x * J_REACH, vertex.yz * lerp(_Hull.x, J_OUTER_FAR, vertex.x));
    o.pos = UnityObjectToClipPos(float4(local, 1));
    o.local = local;
    o.screen = ComputeScreenPos(o.pos);
    o.view = UnityObjectToViewPos(float4(local, 1));
    return o;
}

// ---- noise.ts ----
float HashCell(float3 cell)
{
    float3 p = frac(cell * 0.3183099 + float3(0.1, 0.2, 0.3)) * 17;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float VolumeNoise(float3 p) { return tex3Dlod(_Noise, float4(p / NOISE_PERIOD, 0)).r; }

float VolumeTurbulence(float3 p, float octaves)
{
    float eddies = VolumeNoise(p);
    if (octaves > 1.5) eddies = eddies * 0.65 + VolumeNoise(p * 2.17 + float3(5.3, 1.7, 9.1)) * 0.35;
    if (octaves > 2.5) eddies = eddies * 0.78 + VolumeNoise(p * 4.63 + float3(2.9, 7.1, 3.7)) * 0.22;
    return eddies;
}

float2 VolumeWander(float along, float seed)
{
    float2 rg = tex3Dlod(_Noise, float4(float3(along, seed + along * 0.37, 0.37) / NOISE_PERIOD, 0)).rg;
    return (rg - 0.5) * WANDER_GRADE + 0.5;
}

float Signed(float n) { return (n - 0.5) * 2; }

// ---- blackbody.ts ----
float3 Blackbody(float k)
{
    float along = saturate((k - BLACKBODY_MIN_K) / (BLACKBODY_MAX_K - BLACKBODY_MIN_K));
    float u = (along * (BLACKBODY_TEXELS - 1) + 0.5) / BLACKBODY_TEXELS;
    float4 s = tex2Dlod(_Blackbody, float4(u, 0.5, 0, 0));
    return s.rgb * exp2(s.a);
}

// ---- plume.ts ----
float HalfWidth(float x)
{
    float s = max(x, 0);
    return J_RADIUS + J_SPREAD_NEAR * min(s, J_CORE) + J_SPREAD_FAR * max(s - J_CORE, 0);
}

float Centreline(float x)
{
    float r = max(x, 0) / max(J_CORE, PLUME_EPSILON);
    float r2 = r * r;
    return pow(r2 * r2 + 1, -0.25);
}

float Station(float x)
{
    float core = max(J_CORE, PLUME_EPSILON);
    float s = max(x, 0);
    return s < core ? s : core + (s * s - core * core) / (core * 2);
}

float2 Span(float3 origin, float3 dir, float radius, float reach, float far)
{
    bool parallel = abs(dir.x) < PLUME_EPSILON;
    float capA = -origin.x / dir.x;
    float capB = (reach - origin.x) / dir.x;
    bool outsideCaps = origin.x < 0 || origin.x > reach;
    float nearCaps = parallel ? 0 : max(min(capA, capB), 0);
    float farCaps = parallel ? (outsideCaps ? -1 : far) : min(far, max(capA, capB));
    float a = dot(dir.yz, dir.yz);
    float b = dot(origin.yz, dir.yz) * 2;
    float c = dot(origin.yz, origin.yz) - radius * radius;
    float disc = b * b - a * c * 4;
    float root = sqrt(max(disc, 0));
    bool axial = a < PLUME_EPSILON;
    float nearT = axial ? nearCaps : max(nearCaps, (-b - root) / (a * 2));
    float farT = axial ? (c > 0 ? -1 : farCaps) : (disc < 0 ? -1 : min(farCaps, (-b + root) / (a * 2)));
    return float2(nearT, farT);
}

float Closest(float3 origin, float3 dir, float2 span)
{
    float lateral = dot(dir.yz, dir.yz);
    return lateral > PLUME_EPSILON ? clamp(-dot(origin.yz, dir.yz) / lateral, span.x, span.y) : (span.x + span.y) * 0.5;
}

struct Frame
{
    float3 p;
    float x, core, halfWidth, centre, speed, station, layer;
    float2 lateral;
    float radial;
};

float Streaked(float size, float speed)
{
    return (size + speed * 0.6 * _ShutterS) * J_VELOCITY / max(speed, PLUME_EPSILON);
}

Frame MakeFrame(float3 p)
{
    Frame f;
    f.p = p;
    f.x = p.x;
    f.core = saturate(f.x / max(J_CORE, PLUME_EPSILON));
    f.halfWidth = HalfWidth(f.x);
    f.centre = Centreline(f.x);
    f.speed = J_VELOCITY * f.centre;
    f.station = Station(f.x);
    f.layer = max(f.halfWidth - J_RADIUS * (1 - f.core), J_RADIUS * 0.02);
    float freedom = smoothstep(0, J_CORE * 2, f.x);
    float period = Streaked(f.halfWidth * _MeanderScale, f.speed);
    float phase = (f.station - _PlumeTime * J_VELOCITY * 0.6) / period;
    float2 drift = (VolumeWander(phase, J_SEED) - 0.5) * (J_MEANDER * f.halfWidth * freedom * 2);
    f.lateral = p.yz - drift;
    f.radial = length(f.lateral);
    return f;
}

float Bound(Frame f)
{
    float capped = max(-f.p.x, f.p.x - J_REACH);
    float extent = f.halfWidth * FIELD_EXTENT + f.layer * max(J_TURBULENCE, 0) * EDDY_REACH;
    return max(capped, length(f.lateral) - extent);
}

float BoundCoarse(float3 p)
{
    float x = p.x;
    float capped = max(-x, x - J_REACH);
    float hw = HalfWidth(x);
    float core = saturate(x / max(J_CORE, PLUME_EPSILON));
    float layer = max(hw - J_RADIUS * (1 - core), J_RADIUS * 0.02);
    float extent = hw * FIELD_EXTENT + layer * max(J_TURBULENCE, 0) * EDDY_REACH;
    float freedom = smoothstep(0, J_CORE * 2, x);
    float meander = J_MEANDER * hw * freedom * (2 * WANDER_REACH * 1.41421356);
    return max(capped, length(p.yz) - extent - meander);
}

// plume_field: the temperature, extinction and emission at one point.
void Field(Frame f, float2 footprint, bool turbulent, out float temperature, out float mixture, out float3 extinction, out float3 emission)
{
    float displaced = f.radial;
    float coreRadius = J_RADIUS * (1 - f.core);
    float shearWidth = max(f.halfWidth - coreRadius, J_RADIUS * 0.04);
    float push = J_TURBULENCE * f.layer * EDDY_REACH;
    bool reachesLayer = f.radial + push > coreRadius && f.radial - push < coreRadius + shearWidth * 3;
    float eddy = f.layer * 0.3 + J_RADIUS * 0.03;
    float blur = smoothstep(0.35, 1.2, footprint.x / eddy);
    if (turbulent && reachesLayer && blur < 1)
    {
        float along = (f.station - _PlumeTime * J_VELOCITY * 0.6) / Streaked(eddy * _EddyStretch, f.speed);
        float3 flow = float3(along, f.lateral / eddy) + J_SEED;
        float eddies = lerp(VolumeTurbulence(flow, _Hull.z), 0.5, blur);
        displaced += Signed(eddies) * J_TURBULENCE * f.layer * EDDY_REACH;
    }

    float intoLayer = max(displaced - coreRadius, 0) / shearWidth;
    float shape = exp2(-intoLayer * intoLayer);
    float window = smoothstep(J_REACH, J_REACH * 0.7, f.x) * (f.x < 0 ? 0 : 1);
    mixture = f.centre * shape * window;
    // The blue mode is a collimated exhaust column.  Remove the turbulent centreline wander,
    // hold a constant radius, then dissolve the final 42% gradually instead of exposing the
    // finite volume's square end cap.  Pressure cells vary light inside this straight envelope.
    float blueProgress = saturate(f.x / max(J_REACH, PLUME_EPSILON));
    float blueEnvelopeScale = lerp(1.0, 0.52, smoothstep(0.06, 0.76, blueProgress));
    float blueRadius = J_RADIUS * blueEnvelopeScale;
    float stableRadialDistance = length(f.p.yz);
    // Keep the complete body axis-locked; the Waterfall influence is limited to its soft tail
    // opacity and colour falloff, not Waterfall's lateral flame sway.
    float blueRadialDistance = stableRadialDistance;
    float blueRadial = blueRadialDistance / max(blueRadius, PLUME_EPSILON);
    // A flat-density body keeps the projected outline straight.  The pressure cells below
    // are lighting detail only; they must not repeatedly inflate the visible perimeter.
    float blueShape = 1 - smoothstep(0.58, 1.16, blueRadial);
    float blueCentre = lerp(1, f.centre, 0.42);
    // Finish the density fade before the finite hull cap so no square/pointed end can appear.
    float blueTail = 1 - smoothstep(0.48, 0.96, blueProgress);
    blueTail = pow(saturate(blueTail), 1.20);
    // Waterfall's stock kerosene-afterburner body holds a faint additive tail for longer.
    // Orange borrows that falloff while blue retains its established tail response.
    float waterfallTail = 1 - smoothstep(0.50, 0.985, blueProgress);
    waterfallTail = pow(saturate(waterfallTail), 0.82);
    float modeTail = lerp(waterfallTail, blueTail, _Blue);
    // Both visual modes share one corrected envelope: the same length, wall taper and soft tail.
    // Their pressure-cell spacing and colour treatment remain mode-specific below.
    mixture = blueCentre * blueShape * modeTail * (f.x < 0 ? 0 : 1);

    float ambient = _AmbientK;
    // The Ace Combat-style blue plume uses a denser train of compact pressure cells.
    float spacing = max(J_SPACING * lerp(0.30, 0.34, _Blue), PLUME_EPSILON);
    float expanding = 1 - smoothstep(0, max(J_FIRST_DISK * spacing, PLUME_EPSILON), f.x);
    float coreTemperature = lerp(J_TEMPERATURE, max(J_LIP, J_TEMPERATURE), expanding);
    float mixed = ambient + (coreTemperature - ambient) * mixture + J_AFTERBURNING * mixture * (1 - mixture) * 4;

    // Not upstream's: while the burner is changing (_Hull.w, 0 to 1) the diamonds shiver a little.
    float surge = max(_Hull.w, _SteadyFlutter * smoothstep(J_RADIUS, J_RADIUS * 3, f.x));
    // Blue pressure cells begin at the nozzle lip; orange retains the physical first-disk offset.
    float firstDisk = lerp(0.05, 0.0, _Blue);
    float phaseJitter = 0.015;
    float cells = f.x / spacing - firstDisk + surge * phaseJitter * sin(_PlumeTime * 43.0 + J_SEED * 7.0);
    float behind = frac(cells);
    float inviscid = J_RADIUS * clamp(1 - f.x / max(J_SHOCK_LENGTH, PLUME_EPSILON) * 0.65, 0.12, 1);
    float blueInviscid = blueRadius * 0.98;
    float shockRadius = blueInviscid;
    // Pressure cells stay axis-locked; only the Waterfall-inspired body beyond them wanders.
    float shockRadial = stableRadialDistance;
    float radialShare = shockRadial / max(shockRadius, PLUME_EPSILON);
    float hotLength = clamp(2.5 / max(_ShockRelaxation, 0.5), 0.05, 0.9);
    float lozenge = abs(behind - hotLength * 0.5) / (hotLength * 0.5) + radialShare / 0.85;
    float recovering = 1 - saturate(behind / hotLength) * 0.55;
    float lozengeHot = (1 - smoothstep(0.2, 1.05, lozenge)) * recovering;
    // Blue uses paired compression/expansion shells inside each cell.
    float compressionAlong = saturate(behind / 0.5);
    float expansionAlong = saturate((behind - 0.5) / 0.5);
    // Cone proportions stay constant against the locally tapered blue cylinder.
    float compressionRadius = lerp(0.72, 0.12, compressionAlong);
    float expansionRadius = lerp(0.12, 0.58, expansionAlong);
    // Pressure cells diffuse into the surrounding exhaust downstream instead of retaining
    // equally crisp edges all the way to the tail.
    float coneDissolve = smoothstep(0.30, 0.78, blueProgress);
    float coneSoftness = lerp(1.0, 1.75, coneDissolve);
    float compressionArg = (radialShare - compressionRadius) / (0.22 * coneSoftness);
    float expansionArg = (radialShare - expansionRadius) / (0.24 * coneSoftness);
    float compressionWindow = lerp(smoothstep(0.01, 0.08, behind), 1.0, _Blue)
        * (1 - smoothstep(0.44, 0.51, behind));
    float expansionWindow = smoothstep(0.48, 0.55, behind) * (1 - smoothstep(0.91, 0.99, behind));
    float compressionCone = exp2(-compressionArg * compressionArg) * compressionWindow;
    float expansionCone = exp2(-expansionArg * expansionArg) * expansionWindow * 0.62;
    float coneFillRadius = behind < 0.5 ? compressionRadius : expansionRadius;
    float coneFill = (1 - smoothstep(coneFillRadius, coneFillRadius + 0.15, radialShare)) * 0.05;
    float coneRadial = stableRadialDistance / max(blueRadius, PLUME_EPSILON);
    float coneClip = 1 - smoothstep(0.72, 0.86, coneRadial);
    // Cone light contracts with the envelope and ends before the transparent exhaust tail.
    float coneTrainFade = 1 - smoothstep(0.60, 0.78, blueProgress);
    float coneIgnitionRamp = smoothstep(0.012, 0.065, blueProgress);
    float orangeConeFade = 1 - smoothstep(0.48, 0.68, blueProgress);
    float orangeConeStrength = blueEnvelopeScale * orangeConeFade * coneIgnitionRamp * 0.78;
    float coneStrength = lerp(orangeConeStrength, blueEnvelopeScale * coneTrainFade * coneIgnitionRamp * 0.68, _Blue);
    float coneDetail = max(max(compressionCone, expansionCone), coneFill);
    coneDetail = lerp(coneDetail, coneFill, coneDissolve * 0.68);
    float coneHot = coneDetail * recovering * coneClip * coneStrength;
    float barrel = BARREL_RIM + behind * (1 - behind) * (4 * _BarrelBow);
    float sideways = sqrt(max(footprint.x * footprint.x - footprint.y * footprint.y, 0)) / max(inviscid, PLUME_EPSILON);
    float sheetWidth = max(BARREL_WIDTH, sideways * 0.5);
    float sheetArg = (radialShare - barrel) / sheetWidth;
    float sheet = exp2(-sheetArg * sheetArg) * (BARREL_WIDTH / sheetWidth) * BARREL_HEAT;
    // The blue cones remain inside their cylinder; the legacy outer barrel sheet is orange-only.
    float hot = lerp(max(coneHot, sheet * 0.22), coneHot, _Blue);
    // And pulses run aft down the sheath, a wavelength of about a fifth of the flame, crossing it
    // in about a second: rings dimmer by up to 70% of the sheath's light, never brighter; not the diamonds.
    float pulse = 0.5 + 0.5 * sin(6.2832 * (f.station - _PlumeTime * J_REACH * 0.9) / (J_REACH * 0.2)
        + 0.6 * f.lateral.x / J_RADIUS + 0.9 * f.lateral.y / J_RADIUS);
    float started = smoothstep(-0.15, 0, cells);
    float resolved = 1 - smoothstep(0.15, 0.6, footprint.y / spacing);
    float downstream = max(cells, 0) * spacing;
    float swing = J_SHOCK_HEAT * exp(-downstream / max(J_SHOCK_LENGTH, PLUME_EPSILON)) * resolved * (1 + surge * 0.15 * sin(_PlumeTime * 61.0 + J_SEED * 3.0));
    float cell = hot * started - (1 - hot) * (1 - expanding) * _FanCooling;
    float disked = hot * started * (swing / max(J_SHOCK_HEAT, PLUME_EPSILON));
    float relit = J_REHEAT * disked * mixture;
    float dense = min(lerp(J_THINNING, 1, expanding) * lerp(1, J_COMPRESSION, disked), 1);
    // Blue coverage is a uniform cylinder; shocks affect colour/temperature, not opacity.
    dense = lerp(dense * 0.72, 1, _Blue);
    // Bright ambient conditions wash the whole exhaust out: retain its colour structure but
    // reduce optical depth so aircraft and sky remain visible through it in daylight.
    dense *= lerp(1.0, 0.56, saturate(_Daylight));
    float shocked = mixed + (mixed - ambient) * (pow(swing + 1, cell) - 1) + relit;
    temperature = max(shocked, 1);

    float flame = smoothstep(SOOT_FLAME_LOW, SOOT_FLAME_HIGH, shocked);
    float soot = J_SOOT * dense * mixture * lerp(J_SOOT_SURVIVAL, 1, flame);
    float particles = J_PARTICLES * dense * mixture;
    float3 sootRgb = SOOT_SPECTRUM * soot;
    float3 absorption = sootRgb + particles * (1 - J_ALBEDO);
    extinction = sootRgb + particles;
    float3 thermal = absorption * Blackbody(temperature);
    float activated = exp(min(_ActivationK * (1.0 / REFERENCE_K - 1.0 / temperature), 6));
    float3 band = _Band.rgb * _Band.a * dense * mixture * activated;
    float3 scattered = _SkyLight * particles * J_ALBEDO;
    emission = (thermal + band) * (1 - surge * 0.7 * (1 - pulse) * (1 - saturate(disked * 3))) + scattered;
}

// The radius of a point on the exit plane against the nozzle, meander included (the pipe glow).
float ExitRadial(float2 yz) { return MakeFrame(float3(0, yz)).radial; }

float4 frag(v2f i) : SV_Target
{
    float3 origin = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz;
    float3 ray = i.local - origin;
    float alongRay = length(ray);
    float3 dir = ray / alongRay;

    float rawDepth = SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen));
    float sceneT = LinearEyeDepth(rawDepth) / max(-normalize(i.view).z, 1e-6);

    float2 span = Span(origin, dir, J_OUTER_FAR, J_REACH, sceneT);
    if (span.y <= span.x) discard;

    float tier = _Hull.y;
    bool nearTier = tier < 0.5, farTier = tier > 1.5;
    float crossed = span.y - span.x;
    float closest = Closest(origin, dir, span);
    float lateral = max(length(dir.yz), PLUME_EPSILON);
    float farDepth = min(HalfWidth(origin.x + dir.x * closest) * GAUSSIAN_DEPTH / lateral, crossed);
    float steps = farTier ? 1 : (nearTier ? floor(max(_NearSteps * _Outer.z, _MidSteps)) : _MidSteps);
    float stride = farTier ? farDepth : crossed / steps;

    // The shock train's stretch of ray, marched at a set number of samples per cell.
    float spacing = max(J_SPACING * lerp(0.30, 0.34, _Blue), PLUME_EPSILON);
    float axial = abs(dir.x);
    float trainEnd = min(J_REACH, (J_FIRST_DISK + 1) * spacing + J_SHOCK_LENGTH * SHOCK_TRAIN_LENGTHS);
    float cylinder = J_RADIUS * 1.05 + J_MEANDER * HalfWidth(trainEnd);
    float qa = max(dot(dir.yz, dir.yz), PLUME_EPSILON);
    float qb = dot(origin.yz, dir.yz);
    float qc = dot(origin.yz, origin.yz) - cylinder * cylinder;
    float disc = qb * qb - qa * qc;
    float root = sqrt(max(disc, 0));
    float dx = axial > PLUME_EPSILON ? dir.x : PLUME_EPSILON;
    float xLo = -origin.x / dx, xHi = (trainEnd - origin.x) / dx;
    float segIn = max(max(span.x, (-qb - root) / qa), min(xLo, xHi));
    float segOut = min(min(span.y, (-qb + root) / qa), max(xLo, xHi));
    float segLength = disc > 0 ? max(segOut - segIn, 0) : 0;
    float fineSteps = farTier ? 0 : min(ceil(max(segLength * axial / spacing * SAMPLES_PER_CELL,
        segLength / cylinder * (SAMPLES_ACROSS_CORE * 0.5))), _NearSteps * FINE_STEP_CAP);
    bool hasFine = fineSteps > 0;
    float fineStride = min(stride, segLength / max(fineSteps, 1));
    float budgetSteps = steps + fineSteps + 2;

    float3 entryCell = floor((origin + dir * span.x) * _DitherScale);
    float2 pixel = i.screen.xy / i.screen.w * _ScreenParams.xy;
    float interleaved = frac(frac(dot(pixel, float2(0.06711056, 0.00583715))) * 52.9829189);
    float jitter = frac(HashCell(entryCell) + interleaved);
    float segStart = segIn + jitter * fineStride;
    float firstStride = hasFine && segIn <= span.x ? fineStride : stride;
    float t = farTier ? closest : span.x + jitter * firstStride;
    bool turbulent = nearTier && J_TURBULENCE > 0;

    float3 light = 0;
    float3 transmittance = 1;
    bool outside = true;
    float burnerPattern = 0, burnerVariation = 0;

    [loop]
    for (int n = 0; n < MAX_LOOP; n++)
    {
        if (n >= budgetSteps || t > span.y) break;
        float3 p = origin + dir * t;
        bool beforeTrain = hasFine && t < segStart;
        bool inTrain = hasFine && t >= segStart && t < segOut;
        float toTrain = max(segStart - t, 1e-4);
        float here = inTrain ? fineStride : (beforeTrain ? min(stride, toTrain) : stride);
        float2 footprint = float2(here, here * axial);

        if (outside && !farTier)
        {
            float coarse = BoundCoarse(p);
            if (coarse > 0)
            {
                float leap = max(coarse * _StepSafety, here);
                t += beforeTrain ? min(leap, toTrain) : leap;
                continue;
            }
        }
        Frame f = MakeFrame(p);
        float gap = Bound(f);
        outside = gap > 0;
        if (gap > 0 && !farTier)
        {
            float leap = max(gap * _StepSafety, here);
            t += beforeTrain ? min(leap, toTrain) : leap;
            continue;
        }

        float temperature, mixture;
        float3 extinction, emission;
        Field(f, footprint, turbulent, temperature, mixture, extinction, emission);
        float3 depth = extinction * here;
        float3 through = exp(-depth);
        float3 share = lerp(1 - depth * 0.5, (1 - through) / max(depth, 1e-3), step(1e-3, depth));
        light += transmittance * emission * share * here;
        transmittance *= through;
        if (max(max(transmittance.x, transmittance.y), transmittance.z) < OPAQUE_TRANSMITTANCE) break;
        t += here;
    }

    // Up the nozzle from astern: the dry turbine's glow, or the lit burner's flame in the pipe.
    if (max(_Glow.x, _Glow.w) > 0 && dir.x < -PLUME_EPSILON)
    {
        float exitRadius = max(_Glow.y, PLUME_EPSILON);
        float toExit = origin.x / -dir.x;
        float toTurbine = (origin.x + exitRadius * TURBINE_DEPTH) / -dir.x;
        float atExit = ExitRadial(origin.yz + dir.yz * toExit) / exitRadius;
        float atTurbine = ExitRadial(origin.yz + dir.yz * toTurbine) / exitRadius;
        float throughLip = (1 - smoothstep(0.97, 1, atExit)) * step(0, toExit) * step(toExit, sceneT);
        float blades = smoothstep(0.25, 0.5, atTurbine) * 0.5 + 0.5;
        float turbine = (1 - smoothstep(0.85, 1, atTurbine)) * blades;
        // Recessed burner disk: smaller than the nozzle aperture, placed on the virtual turbine
        // plane and hidden whenever the aircraft model's own interior is in front of that plane.
        float turbineDisk = (1 - smoothstep(0.58, 0.76, atTurbine))
            * step(0, toTurbine) * step(toTurbine, sceneT);
        float deep = saturate((1 - atExit) / max(atTurbine - atExit, PLUME_EPSILON));
        float wall = lerp(PIPE_GLOW_LIP, PIPE_GLOW, deep);
        float seen = lerp(wall, 1, turbine) * throughLip;
        float3 hue = Blackbody(_Glow.z);
        float3 tint = hue / max(dot(hue, LUMINANCE), 1e-6);
        float path = max(toTurbine - toExit, 0) * deep;
        float3 flame = hue * (1 - exp(-_Glow.w * path)) * throughLip;
        // Keep the burner can readable when viewed directly from astern.  The old additive
        // source could exceed the display range before tone mapping and became a flat white
        // disk.  Compress only this line-of-sight source; the separate scene light that casts
        // the plume's glow onto the nozzle and terrain is intentionally left untouched.
        float3 pipeSource = tint * _Glow.x * seen + flame;
        float pipeEnergy = max(dot(pipeSource, LUMINANCE), 1e-5);
        float pipeCompression = 0.46 / (1.0 + pipeEnergy * 0.72);
        // Darker hub, visible turbine annulus and a restrained hot lip give the opening depth
        // without stamping an artificial ring over oblique views.
        float hubShade = lerp(0.48, 1.0, smoothstep(0.16, 0.52, atTurbine));
        float outerShade = 1.0 - 0.30 * smoothstep(0.72, 0.98, atTurbine);
        float bladeShade = lerp(0.78, 1.0, blades);
        pipeSource *= pipeCompression * hubShade * outerShade * bladeShade * turbineDisk;
        light += transmittance * pipeSource;
        float2 burnerUv = (origin.yz + dir.yz * toExit) / exitRadius;
        burnerVariation = VolumeTurbulence(float3(burnerUv * 4, _PlumeTime * 0.35 + J_SEED), 2);
        burnerPattern = throughLip * turbineDisk;
    }

    // In daylight the orange plume is washed warm throughout. At night its cooler, lower-energy
    // shear layer becomes blue-violet while the pressure cells and inner jet remain orange.
    Frame colourFrame = MakeFrame(origin + dir * closest);
    float orangeEdgeShare = colourFrame.radial / max(HalfWidth(colourFrame.x), PLUME_EPSILON);
    // A broad unsaturated ramp colours the low-energy sheath without drawing a neon outline.
    float orangeEdge = smoothstep(0.06, 1.14, orangeEdgeShare);
    float nightFactor = 1 - smoothstep(0.42, 0.92, _Daylight);
    // Even the centreline carries a faint violet background at night; the walls become strongly
    // purple while the high-temperature cone emission remains warm on top of it.
    float nightSheath = nightFactor * lerp(0.38, 1.0, orangeEdge);
    float orangeEnergy = dot(light, LUMINANCE);
    float3 violetSheath = orangeEnergy * float3(0.12, 0.22, 1.58);
    float3 orangeGraded = lerp(light, violetSheath, nightSheath * 0.97);
    // Waterfall-like axial tint: the smooth body cools from peach into a faint rose tail,
    // while the violet night sheath remains visible around it.
    float orangeTailProgress = smoothstep(0.46, 0.98, saturate(colourFrame.x / max(J_REACH, PLUME_EPSILON)));
    float3 roseTail = orangeEnergy * float3(1.0, 0.24, 0.38);
    orangeGraded = lerp(orangeGraded, roseTail, orangeTailProgress * (1 - orangeEdge * 0.55) * 0.34);
    // Do not let the accumulated plume column turn the visible burner can into a white card.
    // Give the direct nozzle view a bounded warm exposure while retaining its turbulent/blade
    // variation; this changes the visible source only, not the exhaust light cast on the scene.
    float3 burnerOrange = lerp(float3(2.0, 0.22, 0.045), float3(1.30, 0.58, 0.16), smoothstep(0.30, 0.76, burnerVariation));
    float orangeBurnerLevel = lerp(0.018, 0.052, burnerVariation) / max(_Exit.w, PLUME_EPSILON);
    orangeGraded = lerp(orangeGraded, orangeBurnerLevel * burnerOrange, burnerPattern * 0.96);

    // Ace Combat-style blue: subdued cobalt/cyan with only a restrained pale-blue highlight.
    float blueEnergy = dot(light, LUMINANCE);
    float blueLevel = blueEnergy * _Exposure * _Exit.w;
    float blueBody = smoothstep(0.03, 0.9, blueLevel);
    // Compress extreme line-of-sight energy so the nozzle face stays blue instead of clipping white.
    float blueRadiance = blueEnergy * 0.48 / (1.0 + blueEnergy * 0.62);
    float3 bluePalette = lerp(float3(0.025, 0.20, 1.65), float3(0.12, 0.68, 1.30), blueBody);
    float3 sukhoiBlue = blueRadiance * bluePalette;
    float3 burnerBlue = lerp(float3(0.08, 0.4, 2.3), float3(0.42, 0.4, 1.4), smoothstep(0.38, 0.72, burnerVariation));
    float burnerLevel = lerp(0.025, 0.075, burnerVariation) / max(_Exit.w, PLUME_EPSILON);
    sukhoiBlue = lerp(sukhoiBlue, burnerLevel * burnerBlue, burnerPattern * 0.98);
    light = lerp(orangeGraded, sukhoiBlue, _Blue);
    float3 exposed = light * _Exposure * _Exit.w;
    float coverage = 1 - dot(transmittance, LUMINANCE);
    if (coverage < 1e-4 && max(max(exposed.x, exposed.y), exposed.z) < 1e-5) discard;
    // Upstream grades linear HDR with AgX; KSP's frame is neither, so both here.
    float3 color = AgxToneMapping(exposed);
    if (_GammaOut > 0.5) color = LinearToSrgb(color);
    return float4(color, coverage);
}

#endif
