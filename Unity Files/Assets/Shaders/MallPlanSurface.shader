// Surfaces of the reconstructed mall, driven by the per-floor plan textures
// (Assets/Textures/MallPlan/floor_<n>_plan.png, built by "Floor Plans Image Processing/build_unity_floor_textures.py").
//
// Plan texture: RGB = shop category colour or corridor colour, A = region type
// (1.0 corridor, ~0.63 shop, ~0.24 outline, 0 outside the building).
// The plan is mapped in world space, exactly like the store centroids in SearchStoreUI:
//   world x = -pixelX * MallWorldScale.WorldUnitsPerPixel, world z = pixelY * WorldUnitsPerPixel,
//   floor n sits at y = n * 5. _PlanWorldSize (set by MallVisualSetup) is the plan size in metres.
//
// _Surface: 0 = floor, 1 = walls / shopfronts, 2 = ceiling
Shader "Mall/Plan Surface"
{
    Properties
    {
        [Enum(Floor,0,Walls,1,Ceiling,2)] _Surface ("Surface", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2

        [NoScaleOffset] _Plan0 ("Plan Ground Floor", 2D) = "white" {}
        [NoScaleOffset] _Plan1 ("Plan First Floor", 2D) = "white" {}
        [NoScaleOffset] _Plan2 ("Plan Second Floor", 2D) = "white" {}
        [NoScaleOffset] _Plan3 ("Plan Third Floor", 2D) = "white" {}
        [NoScaleOffset] _AtriumMask ("Atrium / Skylight Mask", 2D) = "black" {}
        _SkyColor ("Skylight Sky", Color) = (0.72, 0.86, 1.0, 1)

        _CorridorColor ("Corridor Stone", Color) = (0.93, 0.91, 0.87, 1)
        _GroutColor ("Tile Grout", Color) = (0.70, 0.68, 0.64, 1)
        _TileSize ("Corridor Tile Size (m)", Float) = 1.2
        _OutsideColor ("Outside Paving", Color) = (0.20, 0.21, 0.23, 1)

        _GlassColor ("Shopfront Glass", Color) = (0.10, 0.16, 0.20, 1)
        _FrameColor ("Shopfront Frame", Color) = (0.55, 0.57, 0.60, 1)
        _PlasterColor ("Upper Wall", Color) = (0.94, 0.93, 0.90, 1)
        _SkirtingColor ("Skirting", Color) = (0.22, 0.22, 0.24, 1)
        _MullionSpacing ("Mullion Spacing (m)", Float) = 1.6

        _CeilingColor ("Ceiling", Color) = (0.95, 0.95, 0.94, 1)
        _LightPanelColor ("Ceiling Light", Color) = (1.0, 0.97, 0.90, 1)
        _LightPanelSpacing ("Ceiling Light Spacing (m)", Float) = 3.0

        _FillLight ("Interior Fill Light", Range(0, 1)) = 0.45
        _PlanWorldSize ("Plan Size (m)", Vector) = (317.5, 146.58, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _Surface;
            float _Cull;
            half4 _CorridorColor;
            half4 _GroutColor;
            float _TileSize;
            half4 _OutsideColor;
            half4 _GlassColor;
            half4 _FrameColor;
            half4 _PlasterColor;
            half4 _SkirtingColor;
            float _MullionSpacing;
            half4 _CeilingColor;
            half4 _LightPanelColor;
            float _LightPanelSpacing;
            half _FillLight;
            float4 _PlanWorldSize;
            half4 _SkyColor;
        CBUFFER_END

        TEXTURE2D(_AtriumMask);
        SAMPLER(sampler_AtriumMask);

        float2 PlanUV(float3 positionWS)
        {
            return float2(-positionWS.x / _PlanWorldSize.x, 1.0 - positionWS.z / _PlanWorldSize.y);
        }

        // R: void in the floor-1 slab, G: void in the floor-2 slab,
        // B: skylight above the atrium (ceiling of floor 2), A: skylights on the top floor ceiling.
        half4 SampleAtrium(float3 positionWS)
        {
            float2 uv = PlanUV(positionWS);
            if (any(uv < 0.0) || any(uv > 1.0))
                return 0;
            return SAMPLE_TEXTURE2D_LOD(_AtriumMask, sampler_AtriumMask, uv, 0);
        }

        int StoreyOf(float3 positionWS)
        {
            float y = _Surface > 1.5 ? positionWS.y - 0.5 : positionWS.y + 0.02;
            return clamp((int)floor(y / 5.0), 0, 3);
        }

        // Openings of the atrium: no floor slab (and no ceiling below it) inside the void.
        bool InAtriumVoid(float3 positionWS, int storey)
        {
            half4 atrium = SampleAtrium(positionWS);
            if (_Surface < 0.5)
                return (storey == 1 && atrium.r > 0.5) || (storey == 2 && atrium.g > 0.5);
            if (_Surface > 1.5)
                return (storey == 0 && atrium.r > 0.5) || (storey == 1 && atrium.g > 0.5);
            return false;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Plan0); TEXTURE2D(_Plan1); TEXTURE2D(_Plan2); TEXTURE2D(_Plan3);
            SAMPLER(sampler_linear_clamp);

            static const float kFloorHeight = 5.0;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
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
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half4 SamplePlan(float3 positionWS, int floorIndex)
            {
                float2 uv = PlanUV(positionWS);
                if (any(uv < 0.0) || any(uv > 1.0))
                    return half4(0, 0, 0, 0);
                if (floorIndex <= 0) return SAMPLE_TEXTURE2D_LOD(_Plan0, sampler_linear_clamp, uv, 0);
                if (floorIndex == 1) return SAMPLE_TEXTURE2D_LOD(_Plan1, sampler_linear_clamp, uv, 0);
                if (floorIndex == 2) return SAMPLE_TEXTURE2D_LOD(_Plan2, sampler_linear_clamp, uv, 0);
                return SAMPLE_TEXTURE2D_LOD(_Plan3, sampler_linear_clamp, uv, 0);
            }

            // Grid line mask: 1 on the line, 0 elsewhere, antialiased with screen-space derivatives.
            float GridLine(float2 coord, float width)
            {
                float2 d = abs(frac(coord - 0.5) - 0.5);
                float2 aa = fwidth(coord) * 1.2;
                float2 line2 = 1.0 - smoothstep(width - aa, width + aa, d);
                return saturate(max(line2.x, line2.y));
            }

            half3 FloorColour(float3 positionWS, int floorIndex, out half gloss)
            {
                half4 plan = SamplePlan(positionWS, floorIndex);
                float2 xz = positionWS.xz;
                gloss = 0.0;

                if (plan.a < 0.1)
                {
                    // Outside the building: dark paving.
                    float2 slab = xz / 2.0;
                    half v = 0.9 + 0.1 * Hash21(floor(slab));
                    return _OutsideColor.rgb * v * (1.0 - 0.35 * GridLine(slab, 0.02));
                }

                if (plan.a < 0.4)
                    return plan.rgb; // shop outline

                if (plan.a < 0.85)
                {
                    // Inside a shop: the category colour on a fine tiled finish.
                    float2 tile = xz / 0.6;
                    half v = 0.95 + 0.05 * Hash21(floor(tile));
                    gloss = 0.15;
                    return plan.rgb * v * (1.0 - 0.12 * GridLine(tile, 0.015));
                }

                // Corridor: large polished stone tiles with subtle variation and grout lines.
                float2 tile = xz / _TileSize;
                float2 cell = floor(tile);
                half variation = Hash21(cell + floorIndex * 17.0);
                half3 stone = _CorridorColor.rgb * (0.94 + 0.08 * variation);
                // Soft veining
                half vein = sin((xz.x * 1.7 + xz.y * 0.9 + variation * 6.0) + sin(xz.y * 2.3) * 1.5);
                stone *= 1.0 - 0.04 * smoothstep(0.85, 1.0, vein);
                gloss = 0.6;
                return lerp(stone, _GroutColor.rgb, GridLine(tile, 0.012));
            }

            half3 WallColour(float3 positionWS, float3 normalWS, int floorIndex, float height, out half gloss, out half emission)
            {
                gloss = 0.0;
                emission = 0.0;

                if (abs(normalWS.y) > 0.5)
                    return _PlasterColor.rgb; // top caps

                float3 horizontal = normalize(float3(normalWS.x, 0, normalWS.z));
                // The shop is on one side of the wall: pick whichever side of the plan is a shop.
                half4 sideA = SamplePlan(positionWS - horizontal * 1.1, floorIndex);
                half4 sideB = SamplePlan(positionWS + horizontal * 1.1, floorIndex);
                bool aIsShop = sideA.a > 0.4 && sideA.a < 0.85;
                bool bIsShop = sideB.a > 0.4 && sideB.a < 0.85;
                half3 shopColour = aIsShop ? sideA.rgb : (bIsShop ? sideB.rgb : _PlasterColor.rgb);
                bool facesCorridor = sideA.a > 0.85 || sideB.a > 0.85;

                if (height < 0.12)
                    return _SkirtingColor.rgb;

                if (height > 4.05)
                    return _PlasterColor.rgb;

                // Pilasters where one shop ends and the next begins, so neighbouring units read separately.
                float3 tangent = normalize(cross(float3(0, 1, 0), horizontal));
                float3 shopSide = aIsShop ? -horizontal : horizontal;
                half4 left = SamplePlan(positionWS + shopSide * 1.6 - tangent * 0.4, floorIndex);
                half4 right = SamplePlan(positionWS + shopSide * 1.6 + tangent * 0.4, floorIndex);
                bool boundary = (left.a < 0.4 && left.a > 0.1) || (right.a < 0.4 && right.a > 0.1) ||
                                distance(left.rgb, right.rgb) > 0.15;
                if (facesCorridor && boundary && height < 4.05)
                    return _PlasterColor.rgb * 0.97;

                if (height > 3.25)
                {
                    // Fascia band in the shop's category colour with a thin metal trim at the bottom.
                    if (height < 3.32)
                    {
                        gloss = 0.7;
                        return _FrameColor.rgb;
                    }
                    emission = 0.12;
                    return shopColour;
                }

                if (!facesCorridor)
                {
                    // Walls between shops: plain painted partition.
                    return lerp(_PlasterColor.rgb, shopColour, 0.25);
                }

                // Shopfront: tinted glass with mullions and a transom rail.
                float along = dot(positionWS, tangent) / _MullionSpacing;
                float mullion = GridLine(float2(along, 0.5), 0.025);
                float rail = 1.0 - smoothstep(0.03, 0.05, abs(height - 2.6));
                float frame = saturate(max(mullion, rail));

                half3 glass = lerp(_GlassColor.rgb, shopColour * 0.55, 0.35);
                // Lit shop interior glow behind the glass, brighter towards the top.
                glass += shopColour * 0.12 * smoothstep(0.2, 2.4, height);
                // Soft diagonal reflections that make the panes read as glass.
                float streak = frac(along * 0.5 + height * 0.18);
                glass += 0.06 * smoothstep(0.0, 0.15, streak) * (1.0 - smoothstep(0.15, 0.45, streak));
                gloss = lerp(0.9, 0.6, frame);
                return lerp(glass, _FrameColor.rgb, frame);
            }

            half3 CeilingColour(float3 positionWS, int floorIndex, out half emission)
            {
                emission = 0.0;
                half4 plan = SamplePlan(positionWS, floorIndex);
                clip(plan.a - 0.1); // no ceiling outside the building

                half4 atrium = SampleAtrium(positionWS);
                if ((floorIndex == 2 && atrium.b > 0.5) || (floorIndex == 3 && atrium.a > 0.5))
                {
                    // Skylight: daylight through glazing with slim structural bars.
                    float2 bars = positionWS.xz / 1.8;
                    float frame = GridLine(bars, 0.03);
                    half3 sky = lerp(_SkyColor.rgb, half3(1, 1, 1), 0.35 + 0.15 * sin(positionWS.x * 0.05));
                    emission = (1.0 - frame) * 0.8;
                    return lerp(sky, half3(0.85, 0.87, 0.9), frame);
                }

                float2 grid = positionWS.xz / _LightPanelSpacing;
                float2 local = abs(frac(grid) - 0.5) * _LightPanelSpacing;
                float panel = 1.0 - smoothstep(0.32, 0.36, max(local.x, local.y));
                half3 tiles = _CeilingColor.rgb * (1.0 - 0.08 * GridLine(positionWS.xz / 0.6, 0.01));
                emission = panel;
                return lerp(tiles, _LightPanelColor.rgb, panel);
            }

            half4 Frag(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 positionWS = input.positionWS;
                float3 normalWS = normalize(input.normalWS);
                if (!frontFace)
                    normalWS = -normalWS;

                // Which storey this fragment belongs to, and its height above that storey's floor.
                int floorIndex = StoreyOf(positionWS);
                if (InAtriumVoid(positionWS, floorIndex))
                    discard;
                float height = positionWS.y - floorIndex * kFloorHeight;

                half gloss = 0.0;
                half emission = 0.0;
                half3 albedo;
                if (_Surface < 0.5)
                    albedo = FloorColour(positionWS, floorIndex, gloss);
                else if (_Surface < 1.5)
                    albedo = WallColour(positionWS, normalWS, floorIndex, height, gloss, emission);
                else
                    albedo = CeilingColour(positionWS, floorIndex, emission);

                // Simple, stable indoor lighting: main light + sky ambient + a constant fill,
                // since the mall interior is lit by its own fixtures rather than the sun.
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction) * 0.5 + 0.5);
                half3 ambient = SampleSH(normalWS);
                half3 lighting = mainLight.color * ndotl * 0.55 + ambient + _FillLight;

                float3 viewDirWS = normalize(GetWorldSpaceViewDir(positionWS));
                // Light panels overhead give polished floors and glass a soft sheen.
                float3 halfDir = normalize(float3(0, 1, 0) + viewDirWS);
                half specular = pow(saturate(dot(normalWS, halfDir)), 48.0) * gloss * 0.35;
                half fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), 4.0) * gloss * 0.25;

                half3 colour = albedo * lighting + specular + fresnel * half3(0.8, 0.85, 0.9);
                colour = lerp(colour, albedo * 1.15, saturate(emission));
                colour = MixFog(colour, input.fogFactor);
                return half4(colour, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                if (InAtriumVoid(input.positionWS, StoreyOf(input.positionWS)))
                    discard;
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                if (InAtriumVoid(input.positionWS, StoreyOf(input.positionWS)))
                    discard;
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
