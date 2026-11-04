Shader "MusicMidiPlayer/Clockwork/DialGlass"
{
    Properties
    {
        _Tint ("Glass tint and opacity", Color) = (0.92,0.88,0.76,0.32)
        _DaylightTint ("Transmitted daylight tint", Color) = (0.85,0.82,0.70,1)
        _MusicTint ("Musical ink tint", Color) = (1,1,1,1)
        _MusicCharge ("Musical charge", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "ForceNoShadowCasting"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            fixed4 _Tint, _DaylightTint, _MusicTint;
            half _MusicCharge;
            struct appdata { float4 vertex:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos:SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos=UnityObjectToClipPos(v.vertex); return o; }
            fixed4 frag(v2f i):SV_Target { return fixed4(lerp(_Tint.rgb * _DaylightTint.rgb, _MusicTint.rgb * .65, _MusicCharge * .38), _Tint.a + _MusicCharge * .06); }
            ENDCG
        }
    }
    Fallback Off
}
