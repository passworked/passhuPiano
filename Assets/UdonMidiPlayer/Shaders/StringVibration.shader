Shader "MusicMidiPlayer/StringVibration"
{
    Properties
    {
        _Color ("String", Color) = (0.62,0.55,0.35,1)
        _GlowColor ("MIDI glow", Color) = (0.3,0.8,0.9,1)
        _Amount ("Displacement", Float) = 0
        _GlowStrength ("Sounding string", Range(0,1)) = 0
        _MusicTime ("Music time", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 150
        CGPROGRAM
        #pragma surface surf Standard vertex:vert
        #pragma target 3.0
        fixed4 _Color, _GlowColor;
        float _Amount, _MusicTime, _GlowStrength;
        struct Input { float3 worldPos; };
        void vert(inout appdata_full v)
        {
            float envelope=sin(saturate(v.texcoord.y)*3.14159265);
            v.vertex.x += _Amount*envelope*sin(_MusicTime*75.0+v.texcoord.y*6.2831853);
            // The existing tube mesh has inward-facing normals; expand opposite to them.
            v.vertex.xyz -= v.normal * (.0009 * saturate(_GlowStrength));
        }
        void surf(Input IN,inout SurfaceOutputStandard o)
        {
            o.Albedo=_Color.rgb; o.Metallic=.75; o.Smoothness=.7; o.Alpha=1;
            o.Emission=_GlowColor.rgb*saturate(_GlowStrength)*2.5;
        }
        ENDCG
    }
    Fallback "Standard"
}
