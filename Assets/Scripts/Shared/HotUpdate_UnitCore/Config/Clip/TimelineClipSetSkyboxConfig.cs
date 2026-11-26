using System;

/// <summary>换天空盒：进入时替换 <see cref="UnityEngine.RenderSettings.skybox"/>。</summary>
[Serializable]
public class TimelineClipSetSkyboxConfig : TimelineClipConfigBase
{
    /// <summary>天空盒材质工程路径（如 Assets/Package_Scene/Skybox/{Name}/{Name}.mat）。</summary>
    public string SkyboxMaterialPath;

    /// <summary>
    /// 勾选后 Clip 结束时还原进条前的天空盒；默认不勾，结束后保持换后的天空盒。
    /// </summary>
    public bool ResetOnEnd;
}
