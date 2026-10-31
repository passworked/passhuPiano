Shader "MusicMidiPlayer/WaterfallMusicSymbol"
{
    Properties
    {
        _MainTex ("White outline and glow atlas", 2D) = "white" {}
        _Color ("Current song theme", Color) = (1,1,1,1)
        _Opacity ("Outline Opacity", Range(0,1)) = 1
        _Softness ("Vector Edge Softness", Range(0.1,1)) = 0.85
        [HideInInspector] _EdgeDarkening ("Unused Legacy Edge Darkening", Float) = 0
        _EmissionStrength ("Light Emission", Range(0,1)) = 0.35
    }
    SubShader
    {
        Tags { "Queue"="Transparent+30" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            half4 _Color;
            half _Opacity, _Softness, _EmissionStrength;
            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 position : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position = UnityObjectToClipPos(v.vertex);
                o.color = half4(_Color.rgb, v.color.a * _Color.a);
                o.uv = v.uv;
                return o;
            }
            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // Outline and soft halo are white in the atlas. Both receive the same MIDI tint.
                half coverage = tex2D(_MainTex, i.uv).a;
                half alpha = coverage * saturate(i.color.a) * _Opacity;
                // Exact song tint for both outline and halo. Particle color controls only lifetime alpha.
                half3 radiance = i.color.rgb;
                return half4(radiance * alpha, alpha);
            }
            ENDCG
        }
    }
    FallBack Off
}
