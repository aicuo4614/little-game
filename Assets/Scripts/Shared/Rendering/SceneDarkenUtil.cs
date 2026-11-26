using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>从 URP RendererData 读写 <see cref="SceneDarkenFeature"/> 的乘色与开关。</summary>
public static class SceneDarkenUtil
{
    private static readonly FieldInfo RendererDataListField = typeof(UniversalRenderPipelineAsset).GetField(
        "m_RendererDataList",
        BindingFlags.Instance | BindingFlags.NonPublic);

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    private static void RegisterPlayModeReset()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        // 退出 Play 时强制清运行时压暗（含 Skybox），避免 Feature / 天空盒残留
        if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
        {
            ResetAllFeatures();
        }
    }
#endif

    /// <summary>清所有 Renderer 上的 SceneDarken 运行时状态。</summary>
    public static void ResetAllFeatures()
    {
        if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset urp)
        {
            return;
        }

        if (RendererDataListField?.GetValue(urp) is not ScriptableRendererData[] rendererDataList)
        {
            return;
        }

        for (var i = 0; i < rendererDataList.Length; i++)
        {
            var rendererData = rendererDataList[i];
            if (rendererData?.rendererFeatures == null)
            {
                continue;
            }

            for (var j = 0; j < rendererData.rendererFeatures.Count; j++)
            {
                if (rendererData.rendererFeatures[j] is SceneDarkenFeature feature)
                {
                    feature.ClearRuntimeDarken();
                }
            }
        }

#if UNITY_EDITOR
        SceneDarkenFeature.ClearEditorPreviewMultiplyColor();
#endif
    }

    public static SceneDarkenFeature GetFeature(int rendererIndex = 0)
    {
        if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset urp)
        {
            return null;
        }

        var rendererData = GetRendererData(urp, rendererIndex);
        if (rendererData?.rendererFeatures == null)
        {
            return null;
        }

        for (var i = 0; i < rendererData.rendererFeatures.Count; i++)
        {
            if (rendererData.rendererFeatures[i] is SceneDarkenFeature feature)
            {
                return feature;
            }
        }

        return null;
    }

    public static bool TryGetMultiplyColor(out Color color, int rendererIndex = 0)
    {
        color = Color.white;
        var feature = GetFeature(rendererIndex);
        if (feature == null)
        {
            return false;
        }

        color = feature.MultiplyColor;
        return true;
    }

    /// <summary>
    /// 设置乘色；非恒等乘色时自动打开 Feature，恢复白色时关闭（默认关、战斗/Timeline 按需开）。
    /// </summary>
    public static bool SetMultiplyColor(Color color, int rendererIndex = 0)
    {
        var feature = GetFeature(rendererIndex);
        if (feature == null)
        {
            return false;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            SceneDarkenFeature.SetEditorPreviewMultiplyColor(color);
            feature.FeatureActive = !SceneDarkenFeature.IsIdentityMultiply(color);
            feature.RefreshMaterialColor();
            return true;
        }
#endif

        feature.MultiplyColor = color;
        feature.FeatureActive = !SceneDarkenFeature.IsIdentityMultiply(color);
        return true;
    }

    /// <summary>强度 0=不压暗，1=完全压暗（与 SceneDarkenFeature 默认乘色一致）。</summary>
    public static Color ResolveMultiplyColor(float strength)
    {
        strength = Mathf.Clamp01(strength);
        return new Color(
            1 - strength,
            1 - strength,
            1 - strength,
            1f);
    }

#if UNITY_EDITOR
    public static void ClearEditorPreview(int rendererIndex = 0)
    {
        SceneDarkenFeature.ClearEditorPreviewMultiplyColor();
        var feature = GetFeature(rendererIndex);
        if (feature == null)
        {
            return;
        }

        feature.ClearRuntimeDarken();
        feature.FeatureActive = false;
        feature.RefreshMaterialColor();
    }
#endif

    private static ScriptableRendererData GetRendererData(UniversalRenderPipelineAsset urp, int rendererIndex)
    {
        if (RendererDataListField?.GetValue(urp) is not ScriptableRendererData[] rendererDataList)
        {
            return null;
        }

        if (rendererIndex < 0 || rendererIndex >= rendererDataList.Length)
        {
            return null;
        }

        return rendererDataList[rendererIndex];
    }
}
