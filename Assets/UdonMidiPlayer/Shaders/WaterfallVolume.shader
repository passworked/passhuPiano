Shader "MusicMidiPlayer/WaterfallVolume"
{
    Properties
    {
        _Color ("MIDI Hue", Color) = (0.16, 0.65, 1, 1)
        _BaseColor ("Base Hue", Color) = (0.16, 0.65, 1, 1)
        [HDR] _EmissionColor ("Colored Radiance", Color) = (0.32, 1.3, 2, 1)
        _Saturation ("MIDI Color Saturation", Range(1,1.8)) = 1.25
        _Metallic ("Tinted Reflection", Range(0,1)) = 0.12
        _Smoothness ("Polished Crystal", Range(0,1)) = 0.91
        _EdgeStrength ("Crystal Edge Radiance", Range(0,1)) = 0.23
        _BodyGlow ("Inner Color Glow", Range(0,0.3)) = 0.09
        _FacetStrength ("Gem Cut Depth", Range(0,0.6)) = 0.38
        _EndBevelRatio ("End Bevel / Note Width", Range(0.02,0.3)) = 0.12
        _BreathAmount ("Hit Breathing", Range(0,1)) = 0
        [HideInInspector] _NoteOrder ("Stable Note Order", Float) = 0
        [HideInInspector] _GpuAnimation ("Time Driven Notes", Float) = 0
        [HideInInspector] _PianoImpact ("Velocity and Landing Radiance", Float) = 0
        [HideInInspector] _RollOverDrum ("Notes follow a drum arc", Float) = 0
        [HideInInspector] _DrumContactAngle ("Comb contact angle", Float) = -1.02
        [HideInInspector] _DrumArcSweep ("Cylinder arc sweep", Float) = 2.04
        [HideInInspector] _DrumNoteThicknessRatio ("Note thickness / cylinder radius", Float) = 0.0465
        [HideInInspector] _VisualTime ("Local Music Seconds", Float) = 0
        [HideInInspector] _WindowSeconds ("Visible Seconds", Float) = 3
        [HideInInspector] _MinimumFraction ("Minimum Visible Length", Float) = 0.005
        [HideInInspector] _NoteEmission ("Note Emission", Float) = 3
        [HideInInspector] _NoteTiming ("Start End Live", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "DisableBatching"="True" }
        ZWrite On
        LOD 100
        Pass
        {
        Name "UNLIT_CRYSTAL"
        Tags { "LightMode"="Always" }
        CGPROGRAM
        #pragma target 3.0
        #pragma vertex vert
        #pragma fragment frag
        #pragma multi_compile_fog
        #pragma multi_compile_instancing
        #include "UnityCG.cginc"

        UNITY_INSTANCING_BUFFER_START(NoteData)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_DEFINE_INSTANCED_PROP(float4, _NoteTiming)
            UNITY_DEFINE_INSTANCED_PROP(float, _NoteOrder)
        UNITY_INSTANCING_BUFFER_END(NoteData)
        half4 _EmissionColor;
        half _Metallic, _Smoothness, _EdgeStrength, _Saturation, _BodyGlow, _FacetStrength;
        float _EndBevelRatio;
        half _BreathAmount;
        float _GpuAnimation, _RollOverDrum, _DrumContactAngle, _DrumArcSweep, _VisualTime, _WindowSeconds, _MinimumFraction, _NoteEmission;
        float _DrumNoteThicknessRatio;
        float _PianoImpact;
        struct v2f
        {
            float4 noteLocal : TEXCOORD0;  // xyz: deformed local position, w: normalized cap depth
            float4 noteNormal : TEXCOORD1; // xyz: deformed local normal, w: height / width
            float3 viewDir : TEXCOORD2;
            float3 worldPos : TEXCOORD3;
            float4 pos : SV_POSITION;
            UNITY_FOG_COORDS(4)
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        v2f vert(appdata_full v)
        {
            v2f o;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_OUTPUT(v2f, o);
            UNITY_TRANSFER_INSTANCE_ID(v, o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            float width = max(length(mul((float3x3)unity_ObjectToWorld, float3(1,0,0))), 0.000001);
            float height = max(length(mul((float3x3)unity_ObjectToWorld, float3(0,1,0))), 0.000001);
            float lengthFraction = 1.0, centerFraction = 0.5;
            if (_GpuAnimation > 0.5)
            {
                float4 timing = UNITY_ACCESS_INSTANCED_PROP(NoteData, _NoteTiming);
                float window = max(_WindowSeconds, 0.001);
                float low, high;
                if (timing.z > 0.5)
                {
                    low = timing.y < 0.0 ? 0.0 : saturate((_VisualTime - timing.y) / window);
                    high = saturate((_VisualTime - timing.x) / window);
                    lengthFraction = min(1.0 - low, max(_MinimumFraction, high - low));
                    centerFraction = low + lengthFraction * 0.5;
                }
                else
                {
                    low = saturate((timing.x - _VisualTime) / window);
                    high = saturate((timing.y - _VisualTime) / window);
                    lengthFraction = min(1.0, max(_MinimumFraction, high - low));
                    centerFraction = clamp((low + high) * 0.5, lengthFraction * 0.5, 1.0 - lengthFraction * 0.5);
                }
                if (_RollOverDrum > 0.5)
                {
                    // Short crystal markers stay above the pins even on long sustained notes.
                    lengthFraction = min(lengthFraction, 0.08);
                    centerFraction = clamp(low + lengthFraction * 0.5, lengthFraction * 0.5, 1.0 - lengthFraction * 0.5);
                    height *= _DrumArcSweep;
                }
                height *= max(lengthFraction, 0.000001);
            }
            // Local, bounded sizing: no CPU overlap search or per-vertex note list.
            // Equal-height bars use their pool order to avoid identical depth surfaces.
            float shortness = rcp(1.0 + height / width);
            float sizeBoost = shortness + saturate(UNITY_ACCESS_INSTANCED_PROP(NoteData, _NoteOrder)) * 0.02;
            float widen = 1.0 + 0.18 * sizeBoost;
            float deepen = 1.0 + 0.8 * sizeBoost;
            width *= widen;
            // Stretch only the middle of the closed unit mesh. Both cap depths follow
            // note width, independent of duration; very short notes keep a central body.
            float cap = min(max(_EndBevelRatio, 0.001) * width / height, 0.25);
            float seam = 0.5 - cap;
            float bodyScale = seam / 0.465;
            float capScale = cap / 0.035;
            float y = abs(v.vertex.y);
            // The inner profile follows the cylinder even at the cut ends. Its solid
            // depth grows outwards from the metal, independently of note length.
            float drumDepth = max(0.0, 0.5 - 0.12 * saturate((y - 0.465) / 0.035) - v.vertex.z);
            v.vertex.y = sign(v.vertex.y) * (y <= 0.465
                ? y * bodyScale : seam + (y - 0.465) * capScale);
            // Duplicated flat-face vertices let the two affine regions use their exact
            // inverse-transpose normals, including corner faces.
            float faceScale = abs(v.normal.y) > 0.0001 ? capScale : bodyScale;
            v.normal.y /= faceScale;
            v.normal.x /= widen;
            v.normal.z /= deepen;
            v.normal = normalize(v.normal);
            o.noteLocal = float4(v.vertex.xyz, cap);
            o.noteNormal = float4(v.normal, height / width);
            // The CPU Transform covers the complete visible column. All animated
            // vertices remain inside its vertical bounds, including long held notes.
            v.vertex.y = v.vertex.y * lengthFraction + centerFraction - 0.5;
            v.vertex.x *= widen;
            v.vertex.z *= deepen;
            float2 rollBasis = float2(0,1);
            if (_RollOverDrum > 0.5)
            {
                float angle = _DrumContactAngle + (v.vertex.y + 0.5) * _DrumArcSweep;
                sincos(angle, rollBasis.x, rollBasis.y);
                float radius = 1.0 + drumDepth * _DrumNoteThicknessRatio;
                v.vertex.yz = rollBasis.yx * radius;
            }
            o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
            float3 view = UnityWorldSpaceViewDir(o.worldPos);
            // Remove note stretch from the viewing basis, preserving glints on long notes.
            o.viewDir = float3(dot(view, normalize(unity_ObjectToWorld._m00_m10_m20)),
                dot(view, normalize(unity_ObjectToWorld._m01_m11_m21)),
                dot(view, normalize(unity_ObjectToWorld._m02_m12_m22)));
            if (_RollOverDrum > 0.5)
                o.viewDir.yz = float2(dot(o.viewDir.yz, float2(-rollBasis.x, rollBasis.y)),
                    -dot(o.viewDir.yz, rollBasis.yx));
            o.pos = UnityObjectToClipPos(v.vertex);
            UNITY_TRANSFER_FOG(o, o.pos);
            return o;
        }
        half3 SaturatePreservingHueValue(half3 rgb)
        {
            half peak = max(max(rgb.r, rgb.g), rgb.b);
            half chroma = peak - min(min(rgb.r, rgb.g), rgb.b);
            half gain = min(_Saturation, peak / max(chroma, 0.0001));
            return max(half3(0,0,0), peak - (peak - rgb) * gain);
        }
        half4 frag(v2f i) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(i);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            half3 normal = normalize(i.noteNormal.xyz);
            half front = step(0.999h, abs(normal.z));
            // Broad emerald-cut planes stay legible on narrow notes. Keep the closed mesh
            // with normals in note-local space (front/back differ).
            float2 p = i.noteLocal.xy / float2(0.38, 0.5 - i.noteLocal.w);
            // Locate the upper/lower internal cuts by distance from the cap seam in
            // note-width units, so these facets also retain their size on long notes.
            float halfBody = (0.5 - i.noteLocal.w) * i.noteNormal.w;
            float fromEnd = max(0.0, (0.5 - i.noteLocal.w - abs(i.noteLocal.y)) * i.noteNormal.w);
            float endProximity = saturate(1.0 - fromEnd / max(min(0.60, halfBody * 0.90), 0.000001));
            half shoulder = step(0.52, abs(p.x) + endProximity * 0.22) * front;
            half endCut = step(fromEnd, min(0.24 + abs(p.x) * 0.18, halfBody * 0.70)) * front;
            half3 cutNormal = normalize(normal + half3(
                sign(p.x) * (shoulder + 0.35h * endCut),
                sign(p.y) * 0.70h * endCut, 0) * _FacetStrength);
            half3 localView = normalize(i.viewDir);
            half rim = 1.0h - saturate(dot(localView, cutNormal));
            half fresnel = rim * rim; fresnel *= fresnel;
            // A broad internal reflection moves with the viewer, with no screen grab,
            // transparent sorting, scrolling texture, or subpixel sparkle pattern.
            half reflectionBand = 1.0h - smoothstep(0.10h, 0.32h,
                abs(p.x * 0.80h + p.y * 0.35h + localView.x * 0.40h - localView.y * 0.25h));
            half3 reflection = reflect(-localView, cutNormal);
            half innerGlint = pow(saturate(dot(reflection, normalize(half3(-0.35h, 0.60h, normal.z)))), 20.0h);
            half bevel = 1.0h - front;
            half facetTone = 0.52h - shoulder * sign(p.x) * 0.10h + endCut * sign(p.y) * 0.08h;
            half3 noteColor = UNITY_ACCESS_INSTANCED_PROP(NoteData, _Color).rgb;
            half hit = _BreathAmount;
            half3 emissionColor = _EmissionColor.rgb;
            half landingPulse = 0.0h;
            if (_GpuAnimation > 0.5)
            {
                float4 timing = UNITY_ACCESS_INSTANCED_PROP(NoteData, _NoteTiming);
                hit = timing.z < 0.5 ? step(timing.x, _VisualTime) : 0.0;
                if (_RollOverDrum > 0.5)
                    hit = step(timing.x, _VisualTime) * saturate(1.0 - (_VisualTime - timing.x) / 0.12);
                noteColor = lerp(noteColor, lerp(noteColor, half3(1,1,1), 0.25h), hit);
                emissionColor = noteColor * lerp(max(0.0, _NoteEmission), max(1.0, _NoteEmission * 2.5), hit);
                if (_PianoImpact > 0.5)
                {
                    half strength = pow(saturate(timing.w), 1.35h);
                    float age = max(0.0, _VisualTime - timing.x);
                    half attack = smoothstep(0.0, 0.025, age) * exp2(-max(0.0, age - 0.025) / .28);
                    attack *= 1.0 - smoothstep(1.0, 1.4, age);
                    // Measure from the actual landing plane, in note-width units. Long
                    // notes retain their crystal body instead of turning into white lamps.
                    float fromLanding = max(0.0, i.noteLocal.y + 0.5) * i.noteNormal.w;
                    half nearLanding = exp2(-fromLanding * 1.8);
                    half touching = timing.z < 0.5 ? step(timing.x, _VisualTime) : (timing.y < 0.0 ? 1.0h : 0.0h);
                    landingPulse = (0.65h + nearLanding * .35h) * step(timing.x, _VisualTime) * attack * strength * max(0.0, _NoteEmission);
                    noteColor = UNITY_ACCESS_INSTANCED_PROP(NoteData, _Color).rgb;
                    emissionColor = noteColor * max(0.0, _NoteEmission) * lerp(0.80h, 1.12h, strength);
                }
            }
            // All keyboard instruments share the same velocity envelope, including live notes.
            if (_GpuAnimation > .5 && _PianoImpact < .5)
            {
                float4 timing = UNITY_ACCESS_INSTANCED_PROP(NoteData, _NoteTiming);
                float age = max(0.0, _VisualTime - timing.x);
                half envelope = smoothstep(0.0, .025, age) * exp2(-max(0.0, age - .025) / .28);
                envelope *= 1.0 - smoothstep(1.0, 1.4, age);
                landingPulse = step(timing.x, _VisualTime) * envelope * pow(saturate(timing.w), 1.35) * max(0.0, _NoteEmission);
                noteColor = UNITY_ACCESS_INSTANCED_PROP(NoteData, _Color).rgb;
                emissionColor = noteColor * max(0.0, _NoteEmission);
            }
            half3 hue = SaturatePreservingHueValue(noteColor);
            half3 emissionHue = SaturatePreservingHueValue(emissionColor);
            // Fixed studio shading and one static cubemap sample replace real lights.
            half studio = 0.48h + 0.32h * saturate(dot(cutNormal, normalize(half3(-0.35h, 0.6h, 0.72h))));
            half3 worldNormal = normalize(
                normalize(unity_ObjectToWorld._m00_m10_m20) * cutNormal.x
                + normalize(unity_ObjectToWorld._m01_m11_m21) * cutNormal.y
                + normalize(unity_ObjectToWorld._m02_m12_m22) * cutNormal.z);
            half3 worldView = normalize(UnityWorldSpaceViewDir(i.worldPos));
            half3 worldReflection = reflect(-worldView, worldNormal);
            half4 environment = UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, worldReflection, (1.0h - _Smoothness) * 6.0h);
            half3 reflected = DecodeHDR(environment, unity_SpecCube0_HDR);
            half3 body = hue * lerp(0.40h, facetTone, front) * studio;
            half3 glow = emissionHue * (_BodyGlow
                + front * _FacetStrength * (0.16h * reflectionBand + 0.26h * innerGlint)
                + _EdgeStrength * (0.40h * bevel + 0.45h * fresnel));
            half4 color = half4(body + glow + reflected * lerp(0.035h, 0.18h, _Metallic) * (0.35h + fresnel) * lerp(half3(1,1,1), hue, _Metallic), 1);
            color.rgb += lerp(hue, half3(1,1,1), 0.20h) * landingPulse;
            half breath = 0.825h + 0.175h * cos(_Time.y * 5.235987756);
            color.rgb *= lerp(1.0h, breath, _GpuAnimation > .5 ? 0.0h : hit);
            UNITY_APPLY_FOG(i.fogCoord, color);
            return color;
        }
        ENDCG
        }
    }
    FallBack Off
}

