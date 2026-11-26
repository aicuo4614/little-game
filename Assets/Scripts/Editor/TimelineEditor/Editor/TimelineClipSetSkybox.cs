using UnityEditor;
using UnityEngine;

/// <summary>
/// 编辑器预览：进入 Clip 时替换天空盒材质。
/// 勾选「结束时重置」时离开还原；未勾选则条结束保持，整段预览停止 / 销毁时仍还原以免污染编辑器场景。
/// </summary>
public class TimelineClipSetSkybox : TimelineClipBase
{
    private TimelineClipSetSkyboxConfig thisConfig;
    private Material savedSkybox;
    private bool hasSavedSkybox;

    public override void Init(TimelineControl control, TimelineClipConfigBase config)
    {
        thisConfig = config as TimelineClipSetSkyboxConfig;
        base.Init(control, config);
    }

    public override void DrawInspector()
    {
        base.DrawInspector();

        EditorGUILayout.LabelField("换天空盒", EditorStyles.boldLabel);
        var current = string.IsNullOrEmpty(thisConfig.SkyboxMaterialPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<Material>(thisConfig.SkyboxMaterialPath);
        var next = (Material)EditorGUILayout.ObjectField("天空盒材质", current, typeof(Material), false);
        if (next != current)
        {
            thisConfig.SkyboxMaterialPath = next == null ? string.Empty : AssetDatabase.GetAssetPath(next);
            if (hasSavedSkybox)
            {
                ApplySkybox();
            }
        }

        thisConfig.ResetOnEnd = EditorGUILayout.Toggle("结束时重置", thisConfig.ResetOnEnd);
    }

    protected override void OnInTime()
    {
        base.OnInTime();
        if (!hasSavedSkybox)
        {
            savedSkybox = RenderSettings.skybox;
            hasSavedSkybox = true;
        }

        ApplySkybox();
    }

    protected override void OnInTimeLogic()
    {
        base.OnInTimeLogic();
        ApplySkybox();
    }

    protected override void OnOutTime()
    {
        if (thisConfig != null && thisConfig.ResetOnEnd)
        {
            RestoreSkybox();
        }

        base.OnOutTime();
    }

    public override void OnStopPreview()
    {
        RestoreSkybox();
        base.OnStopPreview();
    }

    public override void OnDestroy()
    {
        RestoreSkybox();
        base.OnDestroy();
    }

    private void ApplySkybox()
    {
        if (string.IsNullOrEmpty(thisConfig.SkyboxMaterialPath))
        {
            return;
        }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(thisConfig.SkyboxMaterialPath);
        if (mat == null)
        {
            return;
        }

        RenderSettings.skybox = mat;
    }

    private void RestoreSkybox()
    {
        if (!hasSavedSkybox)
        {
            return;
        }

        RenderSettings.skybox = savedSkybox;
        hasSavedSkybox = false;
        savedSkybox = null;
    }
}
