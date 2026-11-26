using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>预览和实战提供的场景依赖；不包含编辑器 API、网络或资源包实现。</summary>
public sealed class TimelinePlaybackContext
{
    /// <summary>隐藏选中的战斗 UI 层并返回恢复句柄；预览宿主可提供空恢复句柄。</summary>
    public Func<TimelineHideBattleUILayerType, Action> HideBattleUI;
    /// <summary>按配置路径取得已预加载的天空盒材质；宿主负责工程路径与资源地址转换。</summary>
    public Func<string, Material> SkyboxMaterial;
    /// <summary>按消费片段解析施法者、施法目标或效果来源/目标。</summary>
    public Func<TimelineClipConfigBase, TimelineUnitSubjectType, string, List<BattleUnitTimelineActor>> Resolve;
    /// <summary>可选的受击表现条件，逐片段、逐目标调用；未提供时正常播放，不影响命中通知。</summary>
    public Func<TimelineClipPlayHitConfig, BattleUnitTimelineActor, bool> CanPlayHit;
    /// <summary>返回本次演出的所有候选单位，供统一查询和定帧使用。</summary>
    public Func<List<BattleUnitTimelineActor>> AllActors;
    /// <summary>「整个战斗」显隐使用的环境渲染根，可随宿主场景缺省。</summary>
    public Func<Transform> VisibilityEnvironment;
    /// <summary>由逻辑名创建已预加载的特效实例；宿主负责画质和资源地址转换。</summary>
    public Func<string, GameObject> SpawnEffect;
    /// <summary>归还一个特效实例。</summary>
    public Action<GameObject> DespawnEffect;
    /// <summary>可选的特效点光源曲线；宿主从工程资源或资源包读取。</summary>
    public Func<string, EffectPointLightData> EffectLight;
    /// <summary>运镜写入的世界坐标根，与震屏子节点分开。</summary>
    public Transform CameraRoot;
    /// <summary>运镜使用的相机及其 FOV。</summary>
    public Camera Camera;
    /// <summary>相机震屏节点；没有相机的测试宿主可不提供。</summary>
    public Transform ShakeTransform;
    /// <summary>历史编辑器专用片段的采样适配；实战不提供，遇到未支持类型立即报错。</summary>
    public Action<TimelineClipConfigBase, float> SampleEditorClip;
    /// <summary>历史编辑器片段离开时间区间时释放表现。</summary>
    public Action<TimelineClipConfigBase> ExitEditorClip;
    /// <summary>停止或倒放重建时清理历史编辑器专用状态。</summary>
    public Action ResetEditorPlayback;
}
