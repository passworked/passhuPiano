Shader "MusicMidiPlayer/WaterfallSoftSpark"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _Intensity ("Colored Spark Energy", Range(0,3)) = 1.35
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
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
            fixed4 _Color;
            half _Intensity;
            struct appdata { float4 vertex:POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; UNITY_FOG_COORDS(1) UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color * _Color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            half4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p = i.uv * 2.0 - 1.0;
                half halo = exp2(-dot(p,p) * 6.5) * 0.14;
                half streak = exp2(-(p.x*p.x*42.0 + p.y*p.y*4.0)) * 0.72;
                half core = pow(saturate(1.0 - length(p) * 1.6), 3.0) * 0.45;
                half border = saturate((1.0 - max(abs(p.x), abs(p.y))) * 8.0);
                half4 color = half4(i.color.rgb * _Intensity, saturate(halo + streak + core) * border * i.color.a);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, color, half4(0,0,0,0));
                return color;
            }
            ENDCG
        }
    }
}
