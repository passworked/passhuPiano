Shader "MusicMidiPlayer/Clockwork/HeritageSurface"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Handcrafted surface", 2D) = "white" {}
        _DetailTex ("RG normal / B occlusion / A polish", 2D) = "gray" {}
        _Smoothness ("Polish", Range(0,1)) = .35
        _Metallic ("Metal", Range(0,1)) = 0
        _BumpStrength ("Relief", Range(0,1)) = .3
        _DetailAmount ("Surface detail", Range(0,1)) = 1
        _Composition ("Architectural shading", Range(0,1)) = 1
        [HDR] _EmissionColor ("Surface readability", Color) = (0,0,0,1)
        _BakedCeilingFill ("Baked ceiling indirect fill", Range(0,4)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 250
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        sampler2D _MainTex, _DetailTex;
        fixed4 _Color;
        half _Smoothness, _Metallic, _BumpStrength, _DetailAmount, _Composition;
        half4 _EmissionColor;
        half _BakedCeilingFill;
        struct Input { float2 uv_MainTex; float4 color:COLOR; float3 worldPos; float3 worldNormal; float3 worldRefl; INTERNAL_DATA };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            half4 detail=tex2D(_DetailTex,IN.uv_MainTex);
            half3 normal=half3((detail.rg*2-1)*_BumpStrength,1);
            o.Normal=normalize(lerp(half3(0,0,1),normal,_DetailAmount));
            half3 n=normalize(WorldNormalVector(IN,o.Normal));
            half3 baseColor=tex2D(_MainTex,IN.uv_MainTex).rgb*_Color.rgb*IN.color.rgb;
            // Broad architectural value grouping; no extra light or per-frame calculation.
            half center=exp(-dot(IN.worldPos.xz-float2(.3,3),IN.worldPos.xz-float2(.3,3))*.008);
            half ceiling=1-.17*smoothstep(9,18,IN.worldPos.y);
            half group=lerp(.82,1.04,center)*ceiling;
            half foot=lerp(.72,1,smoothstep(0,.45,IN.worldPos.y));
            group*=lerp(foot,1,saturate(abs(n.y)*2));
            o.Albedo=baseColor*lerp(1,group,_Composition);
            o.Metallic=_Metallic;
            o.Occlusion=lerp(1,detail.b,_DetailAmount);
            o.Smoothness=_Smoothness*lerp(1,.45+.65*detail.a,_DetailAmount);
            half shape=.40+.60*saturate(dot(n,normalize(float3(-.2,.65,-.7))));
            half reflectedUp=WorldReflectionVector(IN,o.Normal).y;
            half sheen=smoothstep(-.4,.85,reflectedUp);
            // A broad, roughness-dependent surface reflection approximation. No GI/light source.
            half metalReadability=lerp(1,1.8+1.35*sheen*detail.a,_Metallic);
            o.Emission=o.Albedo*_EmissionColor.rgb*shape*metalReadability;
            #ifdef UNITY_PASS_META
            // Only the upper inward roof face supplies neutral indirect illumination.
            // This is baked into lightmaps/probes; runtime surfaces keep their original look.
            o.Emission += _BakedCeilingFill * smoothstep(11,13,IN.worldPos.y)
                * saturate(-n.y) * half3(.86,.90,1);
            #endif
            o.Alpha=1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
