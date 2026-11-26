// Transparent effects for the navigation experience.
// _Mode: 0 = glass (lift shaft, doors), 1 = route guide (flowing chevrons along a LineRenderer),
//        2 = beacon column (vertical fade + pulse), 3 = floor ring (pulsing halo on a quad), 4 = solid glow
Shader "Mall/Effects"
{
    Properties
    {
        [Enum(Glass,0,Route,1,Beacon,2,Ring,3,Glow,4)] _Mode ("Mode", Float) = 0
        _Color ("Color", Color) = (0.05, 0.85, 1, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.8
        _FlowSpeed ("Flow Speed", Float) = 1.2
        _Intensity ("Intensity", Float) = 1.6
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Effects"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Mode;
                half4 _Color;
                half _Opacity;
                float _FlowSpeed;
                half _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float t = _Time.y;
                half3 colour = _Color.rgb;
                half alpha = _Opacity;

                if (_Mode < 0.5)
                {
                    // Glass: faint tint, brighter at grazing angles, with a soft vertical sheen.
                    float3 n = normalize(frontFace ? input.normalWS : -input.normalWS);
                    float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                    half fresnel = pow(1.0 - saturate(abs(dot(n, v))), 3.0);
                    half sheen = 0.5 + 0.5 * sin(input.positionWS.y * 0.8 + input.positionWS.x * 0.6);
                    colour = lerp(_Color.rgb, half3(0.9, 0.95, 1.0), fresnel * 0.7) + sheen * 0.03;
                    alpha = saturate(_Opacity * 0.35 + fresnel * 0.45);
                }
                else if (_Mode < 1.5)
                {
                    // Route guide: chevrons flowing toward the destination along the line (uv.x = distance).
                    float along = input.uv.x;
                    float across = abs(input.uv.y - 0.5) * 2.0;
                    float phase = frac(along * 0.9 - t * _FlowSpeed);
                    float chevron = frac(phase + across * 0.35);
                    half arrow = smoothstep(0.0, 0.08, chevron) * (1.0 - smoothstep(0.32, 0.42, chevron));
                    half edge = 1.0 - smoothstep(0.75, 1.0, across);
                    half core = 0.35 + 0.65 * arrow;
                    colour = _Color.rgb * _Intensity * (0.8 + 0.6 * arrow);
                    alpha = _Opacity * edge * core * input.color.a;
                }
                else if (_Mode < 2.5)
                {
                    // Beacon: column that fades out upward and breathes slowly.
                    float height = saturate(input.positionOS.y * 0.5 + 0.5); // Unity cylinder spans -1..1
                    half pulse = 0.75 + 0.25 * sin(t * 2.4);
                    half bands = 0.85 + 0.15 * sin(input.positionWS.y * 6.0 - t * 4.0);
                    colour = _Color.rgb * _Intensity * bands;
                    alpha = _Opacity * pow(1.0 - height, 1.6) * pulse;
                }
                else if (_Mode < 3.5)
                {
                    // Floor ring: expanding halo on a quad (uv 0..1).
                    float r = length(input.uv - 0.5) * 2.0;
                    float wave = frac(t * 0.6);
                    half ring = exp(-pow((r - wave) * 9.0, 2.0)) * (1.0 - wave);
                    half rim = exp(-pow((r - 0.55) * 14.0, 2.0)) * 0.9;
                    half disc = (1.0 - smoothstep(0.0, 0.5, r)) * 0.18;
                    colour = _Color.rgb * _Intensity;
                    alpha = _Opacity * saturate(ring + rim + disc) * step(r, 1.0);
                }
                else
                {
                    colour = _Color.rgb * _Intensity;
                }

                return half4(colour, saturate(alpha));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
