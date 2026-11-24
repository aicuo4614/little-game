Shader "Scene/Simple"
{
    // 房间用纯漫反射：仅 Albedo × Color，保留烘焙、主光阴影、雾和渲染层。
    // 不采样法线或 Mask，不计算高光与反射探针。
    Properties
	{
        [MainTexture] _Albedo("Albedo", 2D) = "white" {}
        [MainColor] _Color("Color", Color) = (1, 1, 1, 1)
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

		HLSLINCLUDE
        // URP 写 Rendering Layers 要求 4.5；且不走 multi_compile，避免 AB 剥离 ON 变体
        #pragma target 4.5

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Albedo_ST;
            half4 _Color;
        CBUFFER_END

        TEXTURE2D(_Albedo);
        SAMPLER(sampler_Albedo);
		ENDHLSL
		
		Pass
		{
			Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

			HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
			#pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
			#pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            // 烘焙光照：原版 ASE 有，漏掉会导致房子/墙明显变暗
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
			#pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
			#pragma multi_compile _ SHADOWS_SHADOWMASK

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			struct Attributes
			{
				float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 staticLightmapUV : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

            struct Varyings
			{
                float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                half fogFactor : TEXCOORD5;
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 6);
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
                output.uv = TRANSFORM_TEX(input.uv, _Albedo);
                output.shadowCoord = GetShadowCoord(posInputs);
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
                OUTPUT_SH(normalInputs.normalWS, output.vertexSH);
				return output;
			}

            half4 Frag(Varyings input, out float4 outRenderingLayers : SV_Target1) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // 始终写出层，供 Decal Layers 过滤（不依赖 keyword / 变体收集）
                uint renderingLayers = GetMeshRenderingLayer();
                outRenderingLayers = float4(EncodeMeshRenderingLayer(renderingLayers), 0, 0, 0);

                half4 albedoTex = SAMPLE_TEXTURE2D(_Albedo, sampler_Albedo, input.uv);
                half3 albedo = albedoTex.rgb * _Color.rgb;

                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);

				InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                // 纯漫反射不需要视线方向。
                // 级联阴影必须逐像素选择级联，避免大三角形插值跨越不同阴影区域。
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                inputData.shadowCoord = input.shadowCoord;
                #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
                #else
                inputData.shadowCoord = float4(0, 0, 0, 0);
                #endif
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.occlusion = 1.0h;
                surfaceData.alpha = 1.0h;
                // 未启用 _SPECULAR_COLOR / _SPECGLOSSMAP，URP 此路径仅计算漫反射。
                // 内部保留 Mixed 光照混合、ShadowMask 与 SSAO，避免烘焙主光重复叠加。
                half4 color = UniversalFragmentBlinnPhong(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);

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

            float3 _LightDirection;
            float3 _LightPosition;
            float4 _ShadowBias;

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

            float3 ApplyShadowBiasSimple(float3 positionWS, float3 normalWS, float3 lightDirectionWS)
            {
                float invNdotL = 1.0 - saturate(dot(lightDirectionWS, normalWS));
                positionWS += lightDirectionWS * _ShadowBias.x;
                positionWS += normalWS * (invNdotL * _ShadowBias.y);
                return positionWS;
            }

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
				#if _CASTING_PUNCTUAL_LIGHT_SHADOW
					float3 lightDirectionWS = normalize(_LightPosition - positionWS);
				#else
					float3 lightDirectionWS = _LightDirection;
				#endif
                positionWS = ApplyShadowBiasSimple(positionWS, normalWS, lightDirectionWS);
                output.positionCS = TransformWorldToHClip(positionWS);
				#if UNITY_REVERSED_Z
                output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
				#else
                output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
				#endif
				return output;
			}

            half4 ShadowFrag(Varyings input) : SV_TARGET
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
			ColorMask 0
            Cull Back

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

            half4 DepthFrag(Varyings input) : SV_TARGET
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
            Cull Back

			HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
			#pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                float2 uv : TEXCOORD0;
				half3 normalWS : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = posInputs.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _Albedo);
                output.normalWS = normalInputs.normalWS;
				return output;
			}

            half4 DepthNormalsFrag(Varyings input, out float4 outRenderingLayers : SV_Target1) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                uint renderingLayers = GetMeshRenderingLayer();
                outRenderingLayers = float4(EncodeMeshRenderingLayer(renderingLayers), 0, 0, 0);
                return half4(normalWS, 0.0);
			}
			ENDHLSL
		}
        // 为后续重新烘焙输出 Albedo × Color；不参与运行时绘制。
        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex MetaVert
            #pragma fragment MetaFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
                float2 dynamicLightmapUV : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings MetaVert(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityMetaVertexPosition(input.positionOS.xyz, input.lightmapUV, input.dynamicLightmapUV);
                output.uv = TRANSFORM_TEX(input.uv, _Albedo);
                return output;
            }

            half4 MetaFrag(Varyings input) : SV_Target
            {
                MetaInput metaInput = (MetaInput)0;
                metaInput.Albedo = SAMPLE_TEXTURE2D(_Albedo, sampler_Albedo, input.uv).rgb * _Color.rgb;
                return UnityMetaFragment(metaInput);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
