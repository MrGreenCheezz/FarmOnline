// Земля фермы: обычное URP-освещение плюс тропинки, вытоптанные прямо в материале.
//
// Своим шейдером, а не Lit, по одной причине: раньше проплешина была отдельной плитой,
// лежащей на земле. Плита квадратная и жёсткая, а рельеф волнистый — на стыках торчали
// углы и швы. Тропинка не предмет на земле, а состояние самой земли: у пятна в текстуре
// нет ни краёв, ни толщины, ни предела в сто сорок штук.
//
// Где натоптано, шейдер узнаёт из глобальной карты, которую пишет FootpathLayer.
// Глобальной, а не свойством материала, — чтобы ту же карту потом могли спросить трава
// и всё остальное, что должно расступаться перед тропинкой.
Shader "Farm/Ground"
{
    Properties
    {
        [MainColor] _BaseColor("Цвет травы", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Трава", 2D) = "white" {}
        _Smoothness("Гладкость", Range(0, 1)) = 0
        _Metallic("Металличность", Range(0, 1)) = 0

        [Header(Tropinki)]
        _PathColor("Цвет вытоптанной земли", Color) = (0.58, 0.47, 0.34, 1)
        _PathWidth("Ширина тропинки", Range(0, 1)) = 0.45
        _PathEdge("Размытость края", Range(0.01, 0.6)) = 0.1

        // Рванина должна быть мельче самой тропинки. При зубце в метр она откусывает не край,
        // а всю ленту разом, и тропинка распадается на пятна — проверено.
        _PathFray("Рваность края", Range(0, 2)) = 0.55
        _PathFrayTiling("Крупность рванины, повторов на метр", Float) = 3

        _PathTiling("Крупность земли, повторов на метр", Float) = 1.6
        _PathContrast("Пестрота земли", Range(0, 1)) = 0.35
        _PathHalo("Ширина вытоптанной каймы", Range(0, 0.5)) = 0.15
        _PathHaloBlend("Насколько кайма отдана земле", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // Один буфер на все проходы — иначе SRP Batcher отвалится.
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Smoothness;
            half _Metallic;
            half4 _PathColor;
            float _PathWidth;
            float _PathEdge;
            float _PathFray;
            float _PathFrayTiling;
            float _PathTiling;
            float _PathContrast;
            float _PathHalo;
            float _PathHaloBlend;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS

            // Проект собран на Forward+. Без этого ключевого слова кластерный проход света
            // проходит мимо шейдера: направленный свет ещё доходит через _MainLight, а любой
            // костёр — уже нет. В URP этой версии _FORWARD_PLUS объявлен устаревшим.
            #pragma multi_compile_fragment _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // Карта натоптанного и её место в мире. Объявлены вне UnityPerMaterial намеренно:
            // это глобальные значения, их ставит FootpathLayer через Shader.SetGlobal*, а не
            // материал. Свойство материала перебило бы глобальное, и карта бы не доехала.
            TEXTURE2D(_FootpathMap);
            SAMPLER(sampler_FootpathMap);
            float4 _FootpathArea;   // xy — угол области в мире (x, z); z — 1/сторона; w — 1, пока слой жив

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            /// Шум для края тропинки — процедурный, а не из текстуры. GroundNoise почти без
            /// контраста (СКО 0.015 при среднем 0.2): её хватает траве, но краю тропинки от
            /// неё достаётся дрожь в четверть процента, то есть ничего.
            float PathHash(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 34.53);
                return frac(p.x * p.y * 43758.5453);
            }

            float PathNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = PathHash(cell);
                float b = PathHash(cell + float2(1, 0));
                float c = PathHash(cell + float2(0, 1));
                float d = PathHash(cell + float2(1, 1));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            /// Цвет земли в точке: трава, а где натоптано — земля.
            half3 GroundAlbedo(float3 positionWS, float2 uv)
            {
                half3 grass = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb * _BaseColor.rgb;

                // Слоя нет — сцена без тропинок, режим редактора, превью материала. Ветка
                // одинакова для всех пикселей кадра, поэтому ничего не стоит.
                if (_FootpathArea.w < 0.5) return grass;

                float2 maskUV = (positionWS.xz - _FootpathArea.xy) * _FootpathArea.z;
                float worn = SAMPLE_TEXTURE2D(_FootpathMap, sampler_FootpathMap, maskUV).r;

                // За краем области карта повторила бы крайний пиксель полосой через всю ферму.
                worn *= all(maskUV == saturate(maskUV)) ? 1.0 : 0.0;

                // Насколько мелко видно землю в этом пикселе — пригодится, чтобы погасить
                // рванину вдали. Считается до всякого ветвления: производные внутри ветки
                // берутся от мусора у тех пикселей четвёрки, что в ветку не вошли.
                float2 footprint = fwidth(positionWS.xz);

                // Порог, ниже которого никакая рванина уже не дотянет до каймы. Земля вокруг
                // фермы — это почти весь кадр, и здесь она уходит, не считая шума.
                float quiet = (1.0 - _PathWidth) - _PathHalo - _PathEdge - _PathFray * 0.5;
                if (worn <= max(quiet, 0.0001)) return grass;

                // Две ступени шума: крупная задаёт извив края, мелкая — зубцы. Обе сдвинуты
                // к нулевому среднему, чтобы шум гулял краем, а не двигал ширину тропинки.
                float2 frayUV = positionWS.xz * _PathFrayTiling;
                float fray = (PathNoise(frayUV) - 0.5) + (PathNoise(frayUV * 2.7 + 5.3) - 0.5) * 0.5;

                // Вдали пиксель шире зубца, и рванина превратилась бы в мерцание. Гасим её
                // ровно там, где она перестаёт быть видна как форма.
                float sharp = saturate(1.0 - max(footprint.x, footprint.y) * _PathFrayTiling * 3.0);

                float field = worn + fray * _PathFray * sharp;

                float threshold = 1.0 - _PathWidth;
                float dirt = smoothstep(threshold - _PathEdge, threshold + _PathEdge, field);
                float halo = smoothstep(threshold - _PathHalo - _PathEdge,
                                        threshold - _PathHalo + _PathEdge, field);

                // Земля пёстрая своей крупностью, иначе тропинка выглядит залитой одним цветом.
                float speck = (PathNoise(positionWS.xz * _PathTiling + 17.3) - 0.5) * 2.0;
                half3 dirtColor = _PathColor.rgb * (1.0 + speck * _PathContrast);

                // Кайма — трава, наполовину отданная земле. Без неё граница читается как
                // наклейка: у настоящей тропинки края не обрываются, а сходят на нет.
                return lerp(grass, dirtColor, max(dirt, halo * _PathHaloBlend));
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // Координату в карте теней считаем только когда карта вообще есть: иначе
                // выборка идёт из мусора и земля приходит полностью затенённой.
                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #else
                    inputData.shadowCoord = float4(0, 0, 0, 0);
                #endif

                inputData.fogCoord = input.fogCoord;
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = GroundAlbedo(input.positionWS, input.uv);
                surfaceData.alpha = 1;
                surfaceData.metallic = _Metallic;
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion = 1;
                surfaceData.normalTS = half3(0, 0, 1);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
