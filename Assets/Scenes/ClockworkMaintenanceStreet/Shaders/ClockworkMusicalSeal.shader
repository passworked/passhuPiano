Shader "MusicMidiPlayer/Clockwork/MusicalSeal"
{
    Properties
    {
        _Color ("Palette ink", Color) = (1,1,1,1)
        _Activation ("Visibility / radial reveal / charge / excitation", Vector) = (0,0,0,0)
        _Rotation ("Layer rotation", Float) = 1
        _StrokeScale ("Independent stroke width", Float) = 1
        _MinPixels ("Minimum stroke pixels", Float) = 2.4
        _Contrast ("Dark ink border", Range(0,1)) = .88
    }
    SubShader
    {
        Tags { "Queue"="Transparent+4" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            fixed4 _Color; float4 _Activation; float _Rotation, _StrokeScale, _MinPixels, _Contrast;
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float2 center:TEXCOORD1; float2 layer:TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float2 radial:TEXCOORD1; float reveal:TEXCOORD2; UNITY_FOG_COORDS(3) UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float speed=lerp(-.025,.035,v.color.r)*_Rotation;
                float angle=_Time.y*speed;
                float s,c; sincos(angle,s,c);
                // Every complete geometric band gets its own ordered reveal; no radial clipping through later bands.
                float reveal=smoothstep(v.layer.x*.1,v.layer.x*.1+.075,_Activation.y);
                float2 center=v.center*lerp(.82,1,reveal);
                float2 offset=(v.vertex.xy-v.center)*_StrokeScale;
                center=float2(center.x*c-center.y*s,center.x*s+center.y*c);
                offset=float2(offset.x*c-offset.y*s,offset.x*s+offset.y*c);
                float4 centerClip=UnityObjectToClipPos(float4(center,0,1));
                float4 edgeClip=UnityObjectToClipPos(float4(center+offset,0,1));
                float2 screenOffset=(edgeClip.xy/max(.001,edgeClip.w)-centerClip.xy/max(.001,centerClip.w))*_ScreenParams.xy*.5;
                // Keep distant ink legible in each eye. Radius and stroke thickness are independent.
                float widen=max(1,_MinPixels/max(.001,2*length(screenOffset)));
                o.pos=UnityObjectToClipPos(float4(center+offset*widen,0,1)); o.uv=v.uv;
                o.radial=float2(length(v.center),v.color.g); o.reveal=reveal;
                UNITY_TRANSFER_FOG(o,o.pos); return o;
            }
            half4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float edge=1-abs(i.uv.y*2-1);
                float aa=max(fwidth(edge),.015);
                float stroke=smoothstep(0,aa,edge);
                float core=smoothstep(.28-aa,.28+aa,edge);
                float wave=exp2(-pow((i.radial.x-frac(_Time.y*.25)) * 18,2))*_Activation.z;
                float ink=.82+_Activation.z*.10+_Activation.w*.08+wave*.06;
                // Ordinary alpha ink: no additive blend, emission, bloom or scene illumination.
                half3 palette=lerp(_Color.rgb,half3(.81,.83,.88),.18+i.radial.y*.10);
                half3 border=palette*.16+half3(.018,.022,.032);
                half4 color=half4(lerp(palette,border,(1-core)*_Contrast),stroke*i.reveal*_Activation.x*saturate(ink));
                UNITY_APPLY_FOG(i.fogCoord,color); return color;
            }
            ENDCG
        }
    }
    Fallback Off
}
