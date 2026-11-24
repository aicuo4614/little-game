using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LittleGame.Editor
{
    public static class RenderSetup
    {
        private const string Folder = "Assets/LittleGame/Rendering";

        [MenuItem("Little Game/Configure URP")]
        public static void Configure()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/LittleGame", "Rendering");
            var rendererPath = Folder + "/MobileRenderer.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }
            var pipelinePath = Folder + "/MobilePipeline.asset";
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }
            pipeline.supportsHDR = false;
            pipeline.msaaSampleCount = 2;
            pipeline.renderScale = 1;
            pipeline.supportsCameraDepthTexture = false;
            pipeline.supportsCameraOpaqueTexture = false;
            pipeline.shadowDistance = 30;
            pipeline.shadowCascadeCount = 1;
            var pipelineSettings = new SerializedObject(pipeline);
            pipelineSettings.FindProperty("m_SoftShadowsSupported").boolValue = true;
            pipelineSettings.ApplyModifiedPropertiesWithoutUndo();
            pipeline.useSRPBatcher = true;
            GraphicsSettings.defaultRenderPipeline = pipeline;
            var quality = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(quality, false);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            GraphicsSettings.lightsUseLinearIntensity = true;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/LittleGame" }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material.shader.name != "Standard") continue;
                var color = material.color;
                material.shader = shader;
                material.color = color;
                EditorUtility.SetDirty(material);
            }
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();
            Debug.Log("[RenderSetup] URP 14 + Linear，所有质量档位已绑定；Android IL2CPP 已配置。");
        }
    }
}
