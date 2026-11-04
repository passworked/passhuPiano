Shader "MusicMidiPlayer/Clockwork/Atmosphere"
{
    Properties
    {
        _Color ("Tint", Color) = (1,.82,.55,.04)
        _Dust ("Dust sprite", Range(0,1)) = 0
        [HideInInspector] _ClockworkDaylight ("Daylight", Float) = 1
        [HideInInspector] _ClockworkAltitude ("Altitude", Float) = 1
        [HideInInspector] _ClockworkSourceDirection ("Sole source", Vector) = (0,.69,.72,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" "ForceNoShadowCasting"="True" }
        Blend SrcAlpha One
        ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0
            #include "UnityCG.cginc"
            fixed4 _Color;
            half _Dust;
            float _ClockworkDaylight, _ClockworkAltitude;
            float4 _ClockworkSourceDirection;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float3 world:TEXCOORD1; fixed4 color:COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 p=mul(unity_ObjectToWorld,v.vertex).xyz;
                if(_Dust<.5)
                {
                    // Each ribbon starts on the dial; endpoints follow the sole source's direction.
                    p.xz-=_ClockworkSourceDirection.xz/max(.12,_ClockworkSourceDirection.y)*v.uv.y;
                }
                o.pos=mul(UNITY_MATRIX_VP,float4(p,1));o.world=p;o.uv=v.uv;o.color=v.color;return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float edge=pow(saturate(1-abs(i.uv.x*2-1)),2.5);
                float lengthFade=smoothstep(0,.45,i.uv.y)*(1-smoothstep(3.5,9.5,i.uv.y));
                float beam=edge*lengthFade*saturate(i.world.y*.7);
                float2 p=i.uv-.5;
                float dust=pow(saturate(1-dot(p,p)*4),3);
                float nearFade=smoothstep(.2,1.2,distance(_WorldSpaceCameraPos,i.world));
                float presence=lerp(.18,1,_ClockworkDaylight)*smoothstep(.08,.5,_ClockworkAltitude);
                float3 tint=lerp(float3(.40,.57,1),_Color.rgb,_ClockworkDaylight);
                return float4(tint,lerp(beam,dust,_Dust)*_Color.a*i.color.a*nearFade*presence);
            }
            ENDCG
        }
    }
    Fallback Off
}
