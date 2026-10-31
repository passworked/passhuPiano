Shader "MusicMidiPlayer/WaterfallImpactGlow"
{
    Properties
    {
        _Color ("Impact Hue and Opacity", Color) = (0.1,0.65,1,0)
        [HDR] _EmissionColor ("Impact Energy", Color) = (0.1,0.65,1,1)
        _Intensity ("Glow Energy", Range(0,2)) = 0.75
        _RingStrength ("Delicate Ring", Range(0,0.5)) = 0.18
    }
    SubShader
    {
        Tags { "Queue"="Transparent-5" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha One
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            half4 _Color, _EmissionColor;
            half _Intensity, _RingStrength;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; UNITY_FOG_COORDS(1) UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            half4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p = (i.uv - 0.5) * 2.0;
                half radius = length(p);
                half disc = exp2(-radius * radius * 5.2) * 0.48;
                half ring = exp2(-abs(radius - 0.62) * 45.0) * _RingStrength;
                half fade = 1.0 - smoothstep(0.78, 1.0, radius);
                half4 color = half4((_Color.rgb * 0.75 + _EmissionColor.rgb * 0.25) * _Intensity, (disc + ring) * fade * _Color.a);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, color, half4(0,0,0,0));
                return color;
            }
            ENDCG
        }
    }
}
