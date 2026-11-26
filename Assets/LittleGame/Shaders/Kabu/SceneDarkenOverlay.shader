Shader "Hidden/Kabu/SceneDarkenOverlay"
{
    Properties
    {
        _MultiplyColor("Multiply Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        // 仅 Mesh 二次乘色。ZTest Equal：镂空不补洞，也不会乘到角色深度所在像素。
        // 禁止全屏 Pass：深度不可靠时会整屏乘黑（含 Character）。
        Pass
        {
            Name "SceneDarkenMesh"
            Tags { "LightMode" = "UniversalForward" }

            Blend DstColor Zero
            ZWrite Off
            ZTest Equal
            Cull Off

            HLSLPROGRAM
            #pragma vertex MeshVert
            #pragma fragment MeshFrag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _MultiplyColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings MeshVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 MeshFrag(Varyings input) : SV_Target
            {
                return _MultiplyColor;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
