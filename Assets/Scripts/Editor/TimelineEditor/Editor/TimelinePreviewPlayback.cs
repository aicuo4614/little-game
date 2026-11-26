using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>编辑器的场景适配层；时间推进和通用片段执行全部交给 shared TimelinePlayback。</summary>
public sealed class TimelinePreviewPlayback
{
    private TimelineControl control;
    private TimelinePlayback playback;
    private string document;
    private string rejectedDocument;
    private float sampledTime = -1f;
    private bool dirty = true;
    public object Diagnostics => new { sampledTime, playerTime = playback?.Player.Time, duration = playback?.Player.Duration,
        dirty, rejected = rejectedDocument != null, spawnCount };
    private int spawnCount;
    private Dictionary<string, TimelineClipBase> clips = new Dictionary<string, TimelineClipBase>();

    /// <summary>绑定画布，不创建第二套片段播放器。</summary>
    public TimelinePreviewPlayback(TimelineControl control)
    {
        this.control = control;
    }

    /// <summary>准备参数修改后的新演出；连续播放复用实例，倒放由共享调度器重建。</summary>
    public void Evaluate()
    {
        var time = (float)control.Timeline.RunningTime;
        if (time < 0f || control.Clips.Count == 0)
        {
            Stop();
            return;
        }
        // 画布包含尚未保存的新增、删除和事件草稿；与 MCP 读回共用快照，不能播放旧 Config。
        // 只在失效或首次播放时捕获，连续播放及空闲帧复用现有文档。
        var current = dirty || playback is null ? TimelineJsonUtil.ToJson(control.CaptureDocument()) : document;
        dirty = false;
        if (playback is null || document != current)
        {
            Stop();
            if (current == rejectedDocument)
            {
                return;
            }
            try
            {
                clips.Clear();
                foreach (var clip in control.Clips)
                {
                    clip.PreparePreviewSample();
                    clips.Add(clip.Config.ClipKey, clip);
                }
                var config = control.CaptureDocument();
                document = TimelineJsonUtil.ToJson(config);
                // 动画资源派生事件只存在于画布；补入本次播放快照，使用相同事件时间解析器。
                foreach (var generated in TimelineEditorMcpValidation.GeneratedEvents(control))
                {
                    var owner = config.Clips.Find(clip => clip.ClipKey == generated.ClipKey);
                    owner.EventConfigs ??= new List<TimelineEventConfigBase>();
                    if (!owner.EventConfigs.Exists(point => point.EventKey == generated.Point.EventKey))
                    {
                        owner.EventConfigs.Add(generated.Point);
                    }
                }
                var cameraObject = EditorTimelineUtil.FindBattleCameraGameObject();
                var camera = cameraObject ? BattleCameraMonoBinder.EnsureBound(cameraObject) : null;
                playback = new TimelinePlayback(config, new TimelinePlaybackContext
                {
                    // 美术预览没有战斗 UI；场景片段仍由共享播放器执行。
                    HideBattleUI = _ => () => { },
                    SkyboxMaterial = path => AssetDatabase.LoadAssetAtPath<Material>(path),
                    Resolve = control.UnitBindings.Resolve,
                    AllActors = AllActors,
                    VisibilityEnvironment = () => TimelineVisibilityPlayback.FindEnvironment(
                        UnityEngine.SceneManagement.SceneManager.GetActiveScene()),
                    SpawnEffect = Spawn,
                    DespawnEffect = instance => UnityEngine.Object.DestroyImmediate(instance),
                    EffectLight = EffectLight,
                    CameraRoot = camera?.RootTrans,
                    Camera = camera?.MyCamera,
                    ShakeTransform = EditorTimelineUtil.FindBattleCameraShakeTrans(),
                    SampleEditorClip = (clip, localTime) => clips[clip.ClipKey].SampleFromPlayer(localTime),
                    ExitEditorClip = clip => clips[clip.ClipKey].ExitFromPlayer(),
                    ResetEditorPlayback = ResetEditorPlayback
                });
                rejectedDocument = null;
            }
            catch (Exception exception)
            {
                rejectedDocument = current;
                Debug.LogException(exception);
                Stop();
                return;
            }
        }
        if (sampledTime == time)
        {
            return;
        }
        try
        {
            control.EnemyControl.Draw();
            playback.Player.Evaluate(time);
            // 运镜在共享采样中才写入相机；必须随后对齐根节点，避免沿用上一帧机位。
            // 只改共同坐标根，保留 Spine 局部演出旋转、缩放及左右翻转。
            control.UnitsControl.UpdateSpineFacing();
            sampledTime = time;
        }
        catch (Exception exception)
        {
            rejectedDocument = document;
            Debug.LogException(exception);
            Stop();
            control.Timeline.PausePreView();
        }
    }

    /// <summary>停止、切换文档、画质或关闭窗口前释放共同播放器。</summary>
    public void Stop()
    {
        playback?.Player.Stop();
        playback = null;
        sampledTime = -1f;
        control.UnitBindings.Restore();
        control.UnitsControl.UpdateSpineFacing();
    }

    /// <summary>编排或 Inspector 改动后合并一次文档检查，空闲更新不序列化整份技能。</summary>
    public void Invalidate()
    {
        dirty = true;
    }

    /// <summary>场景手柄读取 shared 播放器实际捕获的起点、参考点和终点。</summary>
    public IReadOnlyList<TimelineUnitClipState> GetUnitStates(string clipKey)
    {
        return playback is not null ? playback.GetUnitStates(clipKey) : Array.Empty<TimelineUnitClipState>();
    }

    /// <summary>读取共享播放器已采样的注视点。</summary>
    public bool TryGetCameraPivot(string key, bool from, out Vector3 point)
    {
        point = default;
        return playback is not null && playback.TryGetCameraPivot(key, from, out point);
    }

    private List<BattleUnitTimelineActor> AllActors()
    {
        var result = new List<BattleUnitTimelineActor>();
        foreach (var unit in control.UnitsControl.Units)
        {
            result.Add(control.UnitBindings.Adapt(unit));
        }
        return result;
    }

    private void ResetEditorPlayback()
    {
        foreach (var clip in control.Clips)
        {
            if (!TimelinePlayback.Supports(clip.Config))
            {
                clip.OnStopPreview();
                clip.ResetPreviewSample();
            }
        }
        control.UnitsControl.OnStopPreView();
        control.UnitBindings.Restore();
    }

    private GameObject Spawn(string logicalName)
    {
        spawnCount++;
        var prefab = EditorTimelineAssetPathUtil.LoadEffectPrefab(logicalName);
        if (!prefab)
        {
            throw new ArgumentException("Timeline effect prefab not found: " + logicalName);
        }
        var instance = UnityEngine.Object.Instantiate(prefab);
        instance.hideFlags = HideFlags.DontSave;
        return instance;
    }

    private EffectPointLightData EffectLight(string logicalName)
    {
        var path = EffectPointLightUtil.GetJsonPathFromPrefab(EditorTimelineAssetPathUtil.ResolveEffectPrefabAssetPath(logicalName));
        var asset = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        return asset ? EffectPointLightJsonUtil.FromTextAsset(asset) : null;
    }
}
