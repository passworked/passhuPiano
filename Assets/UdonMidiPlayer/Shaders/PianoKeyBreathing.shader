Shader "MusicMidiPlayer/PianoKeyBreathing"
{
    Properties
    {
        _Color ("Song Color", Color) = (0.16, 0.9, 1, 1)
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            half4 _Color, _EmissionColor;
            struct v2f
            {
                float4 pos : SV_POSITION;
                UNITY_FOG_COORDS(0)
                UNITY_VERTEX_OUTPUT_STEREO
            };
            v2f vert(appdata_base v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // Shared clock keeps held keys and landed notes in phase, with no CPU updates.
                half breath = 0.825h + 0.175h * cos(_Time.y * 5.235987756);
                half4 color = half4((_Color.rgb + _EmissionColor.rgb) * breath, 1);
                UNITY_APPLY_FOG(i.fogCoord, color);
                return color;
            }
            ENDCG
        }
    }
    FallBack Off
}
