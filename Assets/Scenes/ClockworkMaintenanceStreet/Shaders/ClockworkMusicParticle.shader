Shader "MusicMidiPlayer/Clockwork/MusicParticle"
{
    Properties
    {
        _MainTex ("Particle silhouette", 2D) = "white" {}
        _Intensity ("Ink strength", Range(0,2)) = 1
        _Butterfly ("Animated butterfly silhouette", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent+5" "RenderType"="Transparent" "IgnoreProjector"="True" }
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
            sampler2D _MainTex;
            half _Intensity, _Butterfly;
            struct appdata { float4 vertex:POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float3 world:TEXCOORD1; UNITY_FOG_COORDS(2) UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color;
                o.world=mul(unity_ObjectToWorld,v.vertex).xyz; UNITY_TRANSFER_FOG(o,o.pos); return o;
            }
            float ellipse(float2 p,float2 center,float2 radius)
            {
                return length((p-center)/radius)-1;
            }
            half4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 tex=tex2D(_MainTex,i.uv);
                half mask=tex.r*tex.a;
                half4 c=half4(saturate(i.color.rgb*_Intensity), i.color.a*mask);
                if(_Butterfly>.5)
                {
                    float2 p=(i.uv-.5)*2;
                    // Five wingbeats per second, with a slight asymmetry on the closing stroke.
                    float beat=sin(_Time.y*31.416+i.world.y*.35+i.world.x*.2);
                    float span=lerp(.16,1,pow(beat*.5+.5,.65));
                    float2 wing=float2(abs(p.x)/span,p.y);
                    float upper=ellipse(wing,float2(.40,.26),float2(.40,.48));
                    float lower=ellipse(wing,float2(.30,-.36),float2(.27,.34));
                    float body=ellipse(p,float2(0,-.04),float2(.046,.51));
                    float head=ellipse(p,float2(0,.48),float2(.068,.075));
                    float antenna=max(abs(abs(p.x)-(.04+(p.y-.49)*.32))-.013,abs(p.y-.60)-.12)*12;
                    float wings=min(upper,lower);
                    float silhouette=min(min(wings,body),min(head,antenna));
                    float aa=max(fwidth(silhouette),.015);
                    mask=1-smoothstep(-aa,aa,silhouette);
                    float border=1-smoothstep(-.19,-.08,wings);
                    float veins=pow(saturate(cos(atan2(wing.y,wing.x)*14+length(wing)*3)),16)*.28;
                    half3 ink=lerp(half3(.025,.032,.045),saturate(i.color.rgb*_Intensity),border);
                    ink*=1-veins;
                    if(min(body,head)<0)ink=half3(.035,.027,.05);
                    c=half4(ink,i.color.a*mask);
                }
                UNITY_APPLY_FOG(i.fogCoord,c); return c;
            }
            ENDCG
        }
    }
}
