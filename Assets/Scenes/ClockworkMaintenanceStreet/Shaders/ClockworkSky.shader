Shader "MusicMidiPlayer/Clockwork/Sky"
{
    Properties
    {
        _TopColor ("Zenith", Color) = (0.23,0.43,0.65,1)
        _HorizonColor ("Horizon", Color) = (0.70,0.78,0.82,1)
        _BodyDirection ("Sun or moon direction", Vector) = (0,0.7,1,0)
        _BodyColor ("Sun or moon disk", Color) = (1,0.92,0.71,1)
        _BodyVisibility ("Horizon fade", Range(0,1)) = 1
        _NightAmount ("Night ornaments", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0
            #include "UnityCG.cginc"
            fixed4 _TopColor, _HorizonColor, _BodyColor;
            float4 _BodyDirection;
            float _BodyVisibility, _NightAmount;
            struct appdata { float4 vertex:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos:SV_POSITION; float3 direction:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos=UnityObjectToClipPos(v.vertex); o.direction=v.vertex.xyz; return o; }
            float hash21(float2 p)
            {
                p=frac(p*float2(123.34,456.21)); p+=dot(p,p+45.32); return frac(p.x*p.y);
            }
            fixed4 frag(v2f i):SV_Target
            {
                float3 d=normalize(i.direction);
                fixed3 c=lerp(_HorizonColor.rgb,_TopColor.rgb,saturate(d.y*.9));
                // A celestial herbarium: drifting cloud ribbons, five-petal constellations
                // and fine astronomical orbits. All in the existing sky pass; no lights.
                float2 uv=float2(atan2(d.x,d.z)/6.2831853+.5,asin(clamp(d.y,-1,1))/3.14159265+.5);
                float drift=_Time.y*.002;
                float ribbons=sin(uv.y*53+sin(uv.x*17+drift)*2.5+sin(uv.x*39-drift)*.8);
                float cloud=smoothstep(.12,.90,ribbons)*(1-smoothstep(.68,.97,uv.y));
                c=lerp(c,lerp(fixed3(.82,.84,.86),fixed3(.12,.18,.29),_NightAmount),cloud*lerp(.22,.33,_NightAmount));
                float2 grid=uv*float2(66,32),cell=floor(grid);
                float seed=hash21(cell),seed2=hash21(cell+17.3);
                float2 p=frac(grid)-.5+(float2(seed,seed2)-.5)*.45;
                float r=length(p),a=atan2(p.y,p.x);
                float petal=.13+.055*cos(a*5+seed*6.2831853+_Time.y*.025);
                float aa=max(fwidth(r),.003);
                float edge=1-smoothstep(.009,.009+aa,abs(r-petal));
                float fill=(1-smoothstep(petal-aa,petal+aa,r))*.12;
                float flower=(edge+fill)*step(.75,seed);
                float quietCenter=lerp(.18,1,smoothstep(.1,.55,length(d.xy-float2(0,.14))));
                float center=1-smoothstep(.021,.021+aa,r);
                float sparkle=(1-smoothstep(.008,.008+aa,r))*(.72+.28*sin(_Time.y*.4+seed*25));
                float3 flowerColor=lerp(float3(.95,.71,.43),lerp(float3(.44,.72,1),float3(.98,.61,.68),seed2),_NightAmount);
                c=lerp(c,flowerColor,saturate(flower)*lerp(.09,.48,_NightAmount)*quietCenter);
                c+=flowerColor*(center*step(.75,seed)*.3+sparkle*.45)*lerp(.15,.8,_NightAmount)*quietCenter;
                float orbit=length(d-normalize(float3(-.25,.28,1)));
                float ringDistance=min(abs(orbit-.20),abs(orbit-.32));
                float rings=1-smoothstep(.0007,.0007+fwidth(orbit),ringDistance);
                float3 gold=lerp(float3(.70,.52,.29),float3(.38,.56,.80),_NightAmount);
                c=lerp(c,gold,rings*lerp(.25,.43,_NightAmount));
                float disk=smoothstep(.9992,.9997,dot(d,normalize(_BodyDirection.xyz)));
                return fixed4(lerp(c,_BodyColor.rgb,disk*_BodyVisibility),1);
            }
            ENDCG
        }
    }
    Fallback Off
}
