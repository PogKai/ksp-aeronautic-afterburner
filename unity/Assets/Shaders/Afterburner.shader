// The afterburner plume, raymarched inside a hull fitted to the jet. Object frame: the nozzle exit at
// the origin, +x aft down the plume, metres.
// Ported from aeronautic (github.com/Romainlg29/aeronautic @ 673b967), packages/afterburner/src/
// afterburner-material.ts, tsl/plume.ts, tsl/noise.ts and tsl/blackbody.ts.
// MIT License, (c) 2026 Romain Le Gall; see THIRD-PARTY-NOTICES.md.
Shader "AeronauticAfterburner/Plume"
{
    Properties
    {
        _Blackbody ("Blackbody", 2D) = "black" {}
        _Noise ("Noise", 3D) = "gray" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+40" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Front
            ZWrite Off
            ZTest Always
            Blend One OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "AfterburnerPlume.cginc"
            ENDCG
        }
    }
}
