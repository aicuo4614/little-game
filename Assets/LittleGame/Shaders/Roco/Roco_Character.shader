/// <summary>
/// Roco 角色独立 MVP Shader：D 固有�?+ ID 五区染色/AO + M 可�?Mask�?/// 明暗�?N 与光夹角切三阶；Main+Additional + Rendering Layers�?/// 描边：反向外壳；WidthMode 物体空间 / 屏幕像素。平滑法线可烘焙�?Tangent�?/// </summary>
Shader "Roco/Character"
{
    Properties
    {
        [MainTexture] _BaseMap("Diffuse (D)", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)

        [Header(ID Map)]
        _IDMap("ID Map (R=Region G=AO)", 2D) = "white" {}
        _Color0("Region 0 (R~0)", Color) = (1, 1, 1, 1)
        _Color1("Region 1 (R~0.3)", Color) = (1, 0.85, 0.7, 1)
        _Color2("Region 2 (R~0.5)", Color) = (0.75, 0.55, 0.85, 1)
        _Color3("Region 3 (R~0.7)", Color) = (0.45, 0.35, 0.55, 1)
        _Color4("Region 4 (R~1)", Color) = (1, 0.95, 0.8, 1)
        _DyeStrength("Dye Strength", Range(0, 1)) = 0
        _AOStrength("AO Strength", Range(0, 1)) = 0

        [Header(Mask Map)]
        _MaskMap("Mask (G=Smooth B=Metal A=Special)", 2D) = "black" {}
        _MaskSmoothness("Mask Smoothness Strength", Range(0, 1)) = 0.25
        _MaskMetallic("Mask Metallic Strength", Range(0, 1)) = 0
        _SpecialColor("Special Color", Color) = (1, 1, 0.95, 1)
        _SpecialIntensity("Special Intensity", Range(0, 2)) = 0

        [Header(Shading)]
        _ToonLitAngle("Lit Angle", Range(0, 180)) = 81
        _ToonMidAngle("Mid Angle", Range(0, 180)) = 93
        _ToonLit("Toon Lit", Range(0, 1)) = 1
        _ToonMid("Toon Mid", Range(0, 1)) = 0.244
        _ToonShadow("Toon Shadow", Range(0, 1)) = 0
        [Toggle] _ToonMidGradient("Mid Gradient", Float) = 1
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 0.35
        [ToggleOff(_RECEIVE_SHADOWS_OFF)] _ReceiveShadows("Receive Shadows", Float) = 0
        [HideInInspector] _RampMap("Toon Ramp", 2D) = "white" {}

        [Header(Outline Hull)]
        [ToggleUI] _OutlineEnabled("Outline Enabled", Float) = 1
        [ToggleUI] _OutlineUseSmoothNormal("Use Smooth Normal (Tangent)", Float) = 1
        [Enum(Object Space, 0, Screen Pixels, 1)] _OutlineWidthMode("Outline Width Mode", Float) = 1
        _OutlineWidth("Outline Width", Range(0, 20)) = 3
        _OutlineColor("Outline Color", Color) = (0.1803922, 0.1803922, 0.1803922, 1)
        _OutlineZOffset("Outline Z Offset", Range(0, 2)) = 0
        _OutlineNear("Outline Near (Full Width)", Float) = 2
        _OutlineFar("Outline Far (Fade Out)", Float) = 20
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "UniversalMaterialType" = "Lit"
        }

        LOD 200
        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "RocoCharacterOutline" }

            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _Color0;
                half4 _Color1;
                half4 _Color2;
                half4 _Color3;
                half4 _Color4;
                half _DyeStrength;
                half _AOStrength;
                half _MaskSmoothness;
                half _MaskMetallic;
                half4 _SpecialColor;
                half _SpecialIntensity;
                half _ToonLitAngle;
                half _ToonMidAngle;
                half _ToonLit;
                half _ToonMid;
                half _ToonShadow;
                half _ToonMidGradient;
                half _AmbientStrength;
                half _OutlineEnabled;
                half _OutlineUseSmoothNormal;
                half _OutlineWidthMode;
                half _OutlineWidth;
                half4 _OutlineColor;
                half _OutlineZOffset;
                half _OutlineNear;
                half _OutlineFar;
            CBUFFER_END

            #include "RocoCharacterOutline.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings OutlineVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float widthScale = 0.0;
                if (_OutlineEnabled > 0.5h)
                {
                    widthScale = 1.0;
                }
                float3 outlineN = RocoGetOutlineNormalOS(
                    input.normalOS,
                    input.tangentOS,
                    _OutlineUseSmoothNormal);
                output.positionCS = RocoOutlinePositionCS(
                    input.positionOS.xyz,
                    outlineN,
                    widthScale);
                return output;
            }

            half4 OutlineFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return half4(_OutlineColor.rgb, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF

            #include "RocoCharacter.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.shadowCoord = GetShadowCoord(posInputs);
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 albedo;
                half smoothness;
                half metallic;
                half3 emission;
                RocoBuildSurface(input.uv, albedo, smoothness, metallic, emission);

                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                half3 color = RocoShade(
                    albedo,
                    smoothness,
                    metallic,
                    emission,
                    normalWS,
                    input.positionWS,
                    input.shadowCoord);

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
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
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _Color0;
                half4 _Color1;
                half4 _Color2;
                half4 _Color3;
                half4 _Color4;
                half _DyeStrength;
                half _AOStrength;
                half _MaskSmoothness;
                half _MaskMetallic;
                half4 _SpecialColor;
                half _SpecialIntensity;
                half _ToonLitAngle;
                half _ToonMidAngle;
                half _ToonLit;
                half _ToonMid;
                half _ToonShadow;
                half _ToonMidGradient;
                half _AmbientStrength;
                half _OutlineEnabled;
                half _OutlineUseSmoothNormal;
                half _OutlineWidthMode;
                half _OutlineWidth;
                half4 _OutlineColor;
                half _OutlineZOffset;
                half _OutlineNear;
                half _OutlineFar;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 GetShadowPositionHClip(Attributes input)
            {
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
                return positionCS;
            }

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetShadowPositionHClip(input);
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
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _Color0;
                half4 _Color1;
                half4 _Color2;
                half4 _Color3;
                half4 _Color4;
                half _DyeStrength;
                half _AOStrength;
                half _MaskSmoothness;
                half _MaskMetallic;
                half4 _SpecialColor;
                half _SpecialIntensity;
                half _ToonLitAngle;
                half _ToonMidAngle;
                half _ToonLit;
                half _ToonMid;
                half _ToonShadow;
                half _ToonMidGradient;
                half _AmbientStrength;
                half _OutlineEnabled;
                half _OutlineUseSmoothNormal;
                half _OutlineWidthMode;
                half _OutlineWidth;
                half4 _OutlineColor;
                half _OutlineZOffset;
                half _OutlineNear;
                half _OutlineFar;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }


    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
