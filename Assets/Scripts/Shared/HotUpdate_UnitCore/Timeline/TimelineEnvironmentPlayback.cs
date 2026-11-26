using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>共享场景演出：UI 隐藏、压暗及天空盒；停止和倒放时恢复进入前状态。</summary>
public sealed class TimelineEnvironmentPlayback
{
    private TimelinePlaybackContext context;
    private Dictionary<TimelineClipHideBattleUIConfig, Action> uiRestores = new Dictionary<TimelineClipHideBattleUIConfig, Action>();
    private List<TimelineClipSetSkyboxConfig> skyboxes = new List<TimelineClipSetSkyboxConfig>();
    private Material originalSkybox;
    private bool hasSkybox;
    private Color originalColor;
    private bool hasColor;

    /// <summary>绑定宿主提供的 UI 和预加载材质。</summary>
    public TimelineEnvironmentPlayback(TimelinePlaybackContext context)
    {
        this.context = context;
    }

    /// <summary>按片段内部秒数采样；瞬时 UI 隐藏只在首次进入时执行。</summary>
    public void Sample(TimelineClipConfigBase clip, float time)
    {
        if (clip is TimelineClipHideBattleUIConfig ui && !uiRestores.ContainsKey(ui))
        {
            if (context.HideBattleUI is null)
            {
                throw new ArgumentException("HideBattleUI requires a UI host.");
            }
            uiRestores.Add(ui, context.HideBattleUI(ui.HideLayers));
        }
        else if (clip is TimelineClipSceneDarkenConfig darken)
        {
            if (!hasColor)
            {
                if (!SceneDarkenUtil.TryGetMultiplyColor(out originalColor))
                {
                    throw new ArgumentException("SceneDarken requires SceneDarkenFeature.");
                }
                hasColor = true;
            }
            var strength = TimelineClipSceneDarkenUtil.EvaluateStrength(darken, time, darken.Duration);
            SceneDarkenUtil.SetMultiplyColor(SceneDarkenUtil.ResolveMultiplyColor(strength));
        }
        else if (clip is TimelineClipSetSkyboxConfig skybox)
        {
            if (!hasSkybox)
            {
                originalSkybox = RenderSettings.skybox;
                hasSkybox = true;
            }
            if (!skyboxes.Contains(skybox))
            {
                skyboxes.Add(skybox);
            }
            ApplySkybox(skybox);
        }
    }

    /// <summary>仅恢复勾选结束时重置的 UI 或天空盒；压暗保持终值直到停止。</summary>
    public void Exit(TimelineClipConfigBase clip)
    {
        if (clip is TimelineClipHideBattleUIConfig ui && ui.ResetOnEnd && uiRestores.Remove(ui, out var restore))
        {
            restore();
        }
        if (clip is TimelineClipSetSkyboxConfig skybox && skybox.ResetOnEnd && skyboxes.Remove(skybox))
        {
            if (skyboxes.Count == 0)
            {
                RenderSettings.skybox = originalSkybox;
            }
            else
            {
                ApplySkybox(skyboxes[skyboxes.Count - 1]);
            }
        }
    }

    /// <summary>技能结束、取消、停止或倒放重建时恢复所有状态。</summary>
    public void Reset()
    {
        foreach (var restore in uiRestores.Values)
        {
            restore();
        }
        uiRestores.Clear();
        if (hasSkybox)
        {
            RenderSettings.skybox = originalSkybox;
        }
        skyboxes.Clear();
        hasSkybox = false;
        if (hasColor)
        {
            SceneDarkenUtil.SetMultiplyColor(originalColor);
        }
        hasColor = false;
    }

    private void ApplySkybox(TimelineClipSetSkyboxConfig clip)
    {
        if (string.IsNullOrEmpty(clip.SkyboxMaterialPath))
        {
            return;
        }
        if (context.SkyboxMaterial is null)
        {
            throw new ArgumentException("SetSkybox requires preloaded skybox materials.");
        }
        RenderSettings.skybox = context.SkyboxMaterial(clip.SkyboxMaterialPath);
    }
}
