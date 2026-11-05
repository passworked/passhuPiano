Shader "MusicMidiPlayer/PianoKeyImpact"
{
    Properties
    {
        _LightColor ("White-key hue", Color) = (0.16,0.65,1,1)
        _DarkColor ("Black-key hue", Color) = (0.09,0.36,0.55,1)
        [HideInInspector] _VisualTime ("Music or local performance seconds", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent-5" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }
        Cull Off ZWrite Off ZTest LEqual
        Blend One One
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            float4 _ImpactData[128]; // attack, velocity response, release (-1 = held), reserved
            float4 _ImpactColors[128];
            float _VisualTime;
            half4 _LightColor, _DarkColor;
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 pitch : TEXCOORD1; // MIDI pitch, black key, upright flare
                float3 center : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 tint : COLOR;
                half2 energy : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                UNITY_VERTEX_OUTPUT_STEREO
            };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float4 data = _ImpactData[(int)v.pitch.x];
                float age = max(0.0, _VisualTime - data.x);
                float released = data.z < 0.0 ? 0.0 : max(0.0, _VisualTime - data.z);
                half gate = data.z < 0.0 ? 1.0h : exp2(-released * 14.0) * saturate(1.0 - released * 2.0);
                half pulse = smoothstep(0.0, 0.025, age) * exp2(-max(0.0, age - 0.025) / .28);
                pulse *= 1.0 - smoothstep(1.0, 1.4, age);
                half strength = saturate(data.y);
                half energy = pulse * strength * 1.4h;
                // Collapse unused pitches before rasterization; the entire keyboard is one draw.
                float scale = energy > 0.0005 ? lerp(0.72, 1.0, strength) : 0.0;
                v.vertex.xyz = v.center + (v.vertex.xyz - v.center) * scale;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.tint = _ImpactColors[(int)v.pitch.x];
                o.energy = half2(energy, v.pitch.z);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p = i.uv * 2.0 - 1.0;
                half edge = saturate((1.0 - abs(p.x)) * 5.0) * saturate((1.0 - abs(p.y)) * 5.0);
                half halo = exp2(-dot(p,p) * 4.5) * 0.32h;
                half core = exp2(-(p.x*p.x*11.0 + p.y*p.y*25.0));
                half flare = exp2(-p.x*p.x*13.0) * pow(saturate(1.0 - i.uv.y), 2.0) * 0.55h;
                half shape = lerp(halo + core * 0.65h, flare, i.energy.y) * edge;
                half3 hue = lerp(i.tint.rgb, half3(1,1,1), core * (1.0h - i.energy.y) * 0.23h);
                half4 color = half4(hue * shape * i.energy.x, 0);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, color, half4(0,0,0,0));
                return color;
            }
            ENDCG
        }
    }
    FallBack Off
}
