using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 技能编辑器预览录制：按时间轴采样单位/相机世界 TRS，输出 schema=kabu.timeline.preview-record。
/// 供工具栏「录制播放」与 MCP Host 共用。
/// </summary>
public class TimelinePreviewRecorder
{
    public const string SchemaName = "kabu.timeline.preview-record";
    public const int SchemaVersion = 1;
    public const float DefaultSampleHz = 30f;

    /// <summary>内容终点后再多留一点，保证末尾 Clip 的 Out/采样能落到。</summary>
    public const float AutoStopMarginSeconds = 0.05f;

    private TimelineControl control;
    private float sampleInterval;
    private float nextSampleTime;
    private float lastSampleT = -1f;
    private bool recording;
    private bool untilCutoff;
    private float autoStopTime;
    private Action<string, object> onCompleted;
    private HashSet<string> lastActiveClipKeys = new HashSet<string>();
    private Dictionary<string, EntityPose> lastPoses = new Dictionary<string, EntityPose>();

    private JObject root;
    private JArray samples;
    private JArray events;
    private JArray catalogEntities;
    private JArray catalogClips;
    private Dictionary<string, string> entityIdByInstance = new Dictionary<string, string>();

    public bool IsRecording => recording;

    public bool UntilCutoff => untilCutoff;

    /// <summary>
    /// 自动停播时间：min(CutOff, 内容终点+余量)。untilCutoff=false 时为负，表示不自动停。
    /// </summary>
    public float AutoStopTime => autoStopTime;

    /// <summary>
    /// 录制自动结束时刻。有内容时取 min(cutoff, contentEnd+margin)；无内容则退回 cutoff。
    /// 不改时间轴灰色 CutOff 游标。
    /// </summary>
    public static float ResolveAutoStopTime(TimelineControl control, float cutoffTime)
    {
        var cutoff = Mathf.Max(0f, cutoffTime);
        if (control == null)
        {
            return cutoff;
        }

        var contentEnd = control.ResolvePreviewContentEndTime();
        if (contentEnd <= 1e-4f)
        {
            return cutoff;
        }

        return Mathf.Min(cutoff, contentEnd + AutoStopMarginSeconds);
    }

    /// <summary>开始录制。须在 RunningTime 已回到起点、即将 Play 之前调用。</summary>
    public void Begin(
        TimelineControl control,
        float sampleHz,
        bool untilCutoff,
        Action<string, object> onCompleted = null)
    {
        EndInternal(discard: true);

        this.control = control;
        this.untilCutoff = untilCutoff;
        this.onCompleted = onCompleted;
        sampleHz = sampleHz > 0.1f ? sampleHz : DefaultSampleHz;
        sampleInterval = 1f / sampleHz;
        nextSampleTime = 0f;
        lastSampleT = -1f;
        lastActiveClipKeys.Clear();
        lastPoses.Clear();
        entityIdByInstance.Clear();

        var timeline = control?.Timeline as EditorTimeLine;
        var cutoff = timeline != null ? (float)timeline.CutOffTime : 15f;
        autoStopTime = untilCutoff ? ResolveAutoStopTime(control, cutoff) : -1f;

        root = new JObject
        {
            ["schema"] = SchemaName,
            ["version"] = SchemaVersion
        };
        samples = new JArray();
        events = new JArray();
        catalogEntities = new JArray();
        catalogClips = new JArray();

        BuildMeta(sampleHz, cutoff);
        BuildCatalog();
        root["catalog"] = new JObject
        {
            ["clips"] = catalogClips,
            ["entities"] = catalogEntities
        };
        root["samples"] = samples;
        root["events"] = events;

        recording = true;
        CaptureSample(0f, forceFull: true);
        CaptureClipEvents(0f);
    }

    /// <summary>每帧由 EditorTimeLine 调用；timelineTime 为当前 RunningTime。</summary>
    public void Tick(float timelineTime, bool isPlaying)
    {
        if (!recording)
        {
            return;
        }

        if (timelineTime + 1e-4f >= nextSampleTime)
        {
            CaptureSample(timelineTime, forceFull: false);
            CaptureClipEvents(timelineTime);
            nextSampleTime = timelineTime + sampleInterval;
        }

        if (!untilCutoff || autoStopTime < 0f)
        {
            return;
        }

        if (timelineTime + 1e-3f >= autoStopTime && !isPlaying)
        {
            var reason = Mathf.Abs(autoStopTime - (float)(control?.Timeline?.CutOffTime ?? autoStopTime)) < 1e-3f
                ? "cutoffAuto"
                : "contentAuto";
            Finish(reason);
        }
    }

    /// <summary>结束录制，返回 pretty JSON；并触发 onCompleted。</summary>
    public string Finish(string stopReason)
    {
        if (!recording)
        {
            return null;
        }

        var tEnd = control != null && control.Timeline != null
            ? (float)control.Timeline.RunningTime
            : lastSampleT;
        if (tEnd < 0f)
        {
            tEnd = 0f;
        }

        CaptureSample(tEnd, forceFull: true);
        CaptureClipEvents(tEnd);

        if (root["meta"] is JObject meta)
        {
            meta["tEnd"] = Round3(Mathf.Max(0f, tEnd));
            meta["stopReason"] = stopReason ?? "userStop";
        }

        var json = root.ToString(Formatting.Indented);
        var payload = root.DeepClone();
        var callback = onCompleted;
        EndInternal(discard: false);
        callback?.Invoke(json, payload);
        return json;
    }

    /// <summary>取消录制不产出。</summary>
    public void Cancel()
    {
        EndInternal(discard: true);
    }

    /// <summary>当前帧快照（不依赖录制状态）。</summary>
    public static object BuildSnapshot(TimelineControl control)
    {
        if (control == null)
        {
            return null;
        }

        var t = control.Timeline != null ? (float)control.Timeline.RunningTime : 0f;
        CollectLiveEntities(control, out var catalog, out _);
        var entities = new JObject();
        for (var i = 0; i < catalog.Count; i++)
        {
            var ent = catalog[i] as JObject;
            var id = ent?["id"]?.ToString();
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var pose = ReadLivePose(control, id, ent);
            if (pose != null)
            {
                entities[id] = PoseToJObject(pose, writeAll: true);
            }
        }

        return new JObject
        {
            ["schema"] = SchemaName,
            ["version"] = SchemaVersion,
            ["t"] = Round3(Mathf.Max(0f, t)),
            ["catalog"] = new JObject { ["entities"] = catalog },
            ["entities"] = entities
        };
    }

    private void EndInternal(bool discard)
    {
        recording = false;
        untilCutoff = false;
        autoStopTime = -1f;
        onCompleted = null;
        control = null;
        if (discard)
        {
            root = null;
            samples = null;
            events = null;
            catalogEntities = null;
            catalogClips = null;
        }
    }

    private void BuildMeta(float sampleHz, float cutoff)
    {
        var cfg = control?.Config;
        var timeline = control?.Timeline as EditorTimeLine;
        var contentEnd = control != null ? control.ResolvePreviewContentEndTime() : 0f;
        root["meta"] = new JObject
        {
            ["timelineId"] = cfg != null ? cfg.TimelineId : "",
            ["skillPath"] = timeline != null ? timeline.CurrentTimelineFilePath ?? "" : "",
            ["previewQuality"] = EditorTimelineAssetPathUtil.PreviewQuality.ToString(),
            ["casterTeam"] = control != null ? control.PreviewCasterTeam.ToString() : "TeamA",
            ["sampleHz"] = Round3(sampleHz),
            ["t0"] = 0f,
            ["tEnd"] = 0f,
            ["cutoff"] = Round3(cutoff),
            ["contentEnd"] = Round3(contentEnd),
            ["autoStop"] = untilCutoff ? Round3(autoStopTime) : -1f,
            ["stopReason"] = ""
        };
    }

    private void BuildCatalog()
    {
        catalogClips.Clear();
        catalogEntities.Clear();
        entityIdByInstance.Clear();

        if (control == null)
        {
            return;
        }

        var clips = control.Clips;
        for (var i = 0; i < clips.Count; i++)
        {
            var c = clips[i]?.Config;
            if (c == null)
            {
                continue;
            }

            catalogClips.Add(new JObject
            {
                ["key"] = c.ClipKey ?? "",
                ["type"] = (int)c.Type,
                ["typeName"] = c.Type.ToString(),
                ["label"] = UnitFeatureTypeDisplay.GetClipLabelName(c),
                ["start"] = Round3(c.StartTime),
                ["delay"] = Round3(c.DelayTime),
                ["duration"] = Round3(c.Duration)
            });
        }

        CollectLiveEntities(control, out catalogEntities, out entityIdByInstance);
    }

    private static void CollectLiveEntities(
        TimelineControl control,
        out JArray catalog,
        out Dictionary<string, string> idByInstance)
    {
        catalog = new JArray();
        idByInstance = new Dictionary<string, string>();

        var caster = control.MyTransform != null
            ? control.MyTransform.GetComponent<UnitMono>()
            : null;
        if (caster != null)
        {
            var id = "unit:caster";
            idByInstance[InstanceKey(caster)] = id;
            catalog.Add(BuildUnitCatalogEntry(id, "caster", caster, control.MyTransformPath));
        }

        var oppIndex = 0;
        var units = control.UnitsControl != null ? control.UnitsControl.Units : null;
        if (units != null)
        {
            for (var i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (unit == null || unit == caster)
                {
                    continue;
                }

                var id = "unit:opp:" + oppIndex;
                oppIndex++;
                idByInstance[InstanceKey(unit)] = id;
                catalog.Add(BuildUnitCatalogEntry(
                    id,
                    "opponent",
                    unit,
                    "Grunt/Grunt"));
            }
        }

        var camGo = EditorTimelineUtil.FindBattleCameraGameObject();
        if (camGo != null)
        {
            var mono = BattleCameraMonoBinder.EnsureBound(camGo);
            var root = mono.RootTrans != null ? mono.RootTrans : camGo.transform;
            var id = "camera:battle";
            idByInstance[InstanceKey(root)] = id;
            var fov = mono.MyCamera != null ? mono.MyCamera.fieldOfView : BattleSharedConfig.Current.CameraFov;
            catalog.Add(new JObject
            {
                ["id"] = id,
                ["kind"] = "camera",
                ["origin"] = new JObject
                {
                    ["pos"] = VecArray(root.position),
                    ["euler"] = VecArray(root.rotation.eulerAngles),
                    ["fov"] = Round3(fov),
                    ["orthographic"] = mono.MyCamera != null && mono.MyCamera.orthographic,
                    ["orthographicSize"] = mono.MyCamera != null ? mono.MyCamera.orthographicSize : 0f,
                    ["aspect"] = mono.MyCamera != null ? mono.MyCamera.aspect : 0f
                }
            });
        }
    }

    private static JObject BuildUnitCatalogEntry(string id, string role, UnitMono unit, string prefab)
    {
        var t = unit.transform;
        return new JObject
        {
            ["id"] = id,
            ["kind"] = "unit",
            ["role"] = role,
            ["prefab"] = prefab ?? "",
            ["team"] = unit.TeamType.ToString(),
            ["origin"] = new JObject
            {
                ["pos"] = VecArray(unit.BattleOriginPos),
                ["euler"] = VecArray(unit.BattleOriginRotation.eulerAngles),
                ["scale"] = VecArray(t.localScale)
            }
        };
    }

    private void CaptureSample(float t, bool forceFull)
    {
        if (samples == null || control == null)
        {
            return;
        }

        t = Mathf.Max(0f, t);
        if (!forceFull && lastSampleT >= 0f && Mathf.Abs(t - lastSampleT) < 1e-4f)
        {
            return;
        }

        var eObj = new JObject();
        for (var i = 0; i < catalogEntities.Count; i++)
        {
            var ent = catalogEntities[i] as JObject;
            var id = ent?["id"]?.ToString();
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var pose = ReadLivePose(control, id, ent);
            if (pose == null)
            {
                continue;
            }

            EntityPose prev;
            var hasPrev = lastPoses.TryGetValue(id, out prev);
            var writeAll = forceFull || !hasPrev;
            if (writeAll || !pose.ApproximatelyEquals(prev))
            {
                eObj[id] = PoseToJObject(pose, writeAll, hasPrev ? prev : null);
            }

            lastPoses[id] = pose;
        }

        if (eObj.Count == 0 && !forceFull)
        {
            lastSampleT = t;
            return;
        }

        samples.Add(new JObject
        {
            ["t"] = Round3(t),
            ["e"] = eObj
        });
        lastSampleT = t;
    }

    private void CaptureClipEvents(float t)
    {
        if (control == null || events == null)
        {
            return;
        }

        var active = new HashSet<string>();
        var clips = control.Clips;
        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            var cfg = clip?.Config;
            if (cfg == null || string.IsNullOrEmpty(cfg.ClipKey))
            {
                continue;
            }

            var start = cfg.StartTime + cfg.DelayTime;
            var end = control.ResolveClipContentEndTime(cfg);
            if (t + 1e-4f >= start && t < end - 1e-6f)
            {
                active.Add(cfg.ClipKey);
                if (!lastActiveClipKeys.Contains(cfg.ClipKey))
                {
                    events.Add(new JObject
                    {
                        ["t"] = Round3(Mathf.Max(0f, t)),
                        ["kind"] = "clipEnter",
                        ["clipKey"] = cfg.ClipKey,
                        ["type"] = (int)cfg.Type
                    });
                }
            }
        }

        foreach (var key in lastActiveClipKeys)
        {
            if (!active.Contains(key))
            {
                var type = FindClipType(key);
                events.Add(new JObject
                {
                    ["t"] = Round3(Mathf.Max(0f, t)),
                    ["kind"] = "clipExit",
                    ["clipKey"] = key,
                    ["type"] = type
                });
            }
        }

        lastActiveClipKeys = active;
    }

    private int FindClipType(string clipKey)
    {
        var clips = control.Clips;
        for (var i = 0; i < clips.Count; i++)
        {
            var c = clips[i]?.Config;
            if (c != null && c.ClipKey == clipKey)
            {
                return (int)c.Type;
            }
        }

        return 0;
    }

    private static EntityPose ReadLivePose(TimelineControl control, string id, JObject catalogEntry)
    {
        if (id == "unit:caster")
        {
            var tr = control.MyTransform;
            if (tr == null)
            {
                return null;
            }

            var unit = tr.GetComponent<UnitMono>();
            return EntityPose.FromTransform(tr, unit != null ? IsUnitActive(unit) : tr.gameObject.activeInHierarchy, null);
        }

        if (id != null && id.StartsWith("unit:opp:", StringComparison.Ordinal))
        {
            if (!int.TryParse(id.Substring("unit:opp:".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                return null;
            }

            var caster = control.MyTransform != null
                ? control.MyTransform.GetComponent<UnitMono>()
                : null;
            var units = control.UnitsControl.Units;
            var opp = 0;
            for (var i = 0; i < units.Count; i++)
            {
                if (units[i] == null || units[i] == caster)
                {
                    continue;
                }

                if (opp == index)
                {
                    return EntityPose.FromTransform(
                        units[i].transform,
                        IsUnitActive(units[i]),
                        null);
                }

                opp++;
            }

            return null;
        }

        if (id == "camera:battle")
        {
            var camGo = EditorTimelineUtil.FindBattleCameraGameObject();
            if (camGo == null)
            {
                return null;
            }

            var mono = BattleCameraMonoBinder.EnsureBound(camGo);
            var root = mono.RootTrans != null ? mono.RootTrans : camGo.transform;
            var fov = mono.MyCamera != null ? mono.MyCamera.fieldOfView : BattleSharedConfig.Current.CameraFov;
            return EntityPose.FromTransform(root, true, fov);
        }

        return null;
    }

    private static bool IsUnitActive(UnitMono unit)
    {
        var root = unit.ActiveRoot ? unit.ActiveRoot : unit.transform;
        if (!root.gameObject.activeInHierarchy)
        {
            return false;
        }
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            if (renderer.enabled && !renderer.forceRenderingOff && renderer.gameObject.activeInHierarchy)
            {
                return true;
            }
        }
        return renderers.Length == 0;
    }

    private static JObject PoseToJObject(EntityPose pose, bool writeAll, EntityPose prev = null)
    {
        var o = new JObject();
        if (writeAll || prev == null || !Near(pose.Pos, prev.Pos))
        {
            o["pos"] = VecArray(pose.Pos);
        }

        if (writeAll || prev == null || !Near(pose.Euler, prev.Euler))
        {
            o["euler"] = VecArray(pose.Euler);
        }

        if (writeAll || prev == null || !Near(pose.Scale, prev.Scale))
        {
            o["scale"] = VecArray(pose.Scale);
        }

        if (pose.Fov.HasValue && (writeAll || prev == null || !prev.Fov.HasValue || Mathf.Abs(pose.Fov.Value - prev.Fov.Value) > 1e-3f))
        {
            o["fov"] = Round3(pose.Fov.Value);
        }

        if (pose.Active.HasValue && (writeAll || prev == null || pose.Active != prev.Active))
        {
            o["active"] = pose.Active.Value;
        }

        return o;
    }

    private static JArray VecArray(Vector3 v)
    {
        return new JArray(Round3(v.x), Round3(v.y), Round3(v.z));
    }

    private static float Round3(float v)
    {
        return (float)Math.Round(v, 3);
    }

    private static bool Near(Vector3 a, Vector3 b)
    {
        return (a - b).sqrMagnitude < 1e-6f;
    }

    private static string InstanceKey(UnityEngine.Object obj)
    {
        return obj != null ? obj.GetInstanceID().ToString() : "";
    }

    private sealed class EntityPose
    {
        public Vector3 Pos;
        public Vector3 Euler;
        public Vector3 Scale;
        public float? Fov;
        public bool? Active;

        public static EntityPose FromTransform(Transform t, bool active, float? fov)
        {
            return new EntityPose
            {
                Pos = t.position,
                Euler = t.rotation.eulerAngles,
                Scale = t.localScale,
                Fov = fov,
                Active = fov.HasValue ? (bool?)null : active
            };
        }

        public bool ApproximatelyEquals(EntityPose other)
        {
            if (other == null)
            {
                return false;
            }

            if (!Near(Pos, other.Pos) || !Near(Euler, other.Euler) || !Near(Scale, other.Scale))
            {
                return false;
            }

            if (Fov.HasValue != other.Fov.HasValue)
            {
                return false;
            }

            if (Fov.HasValue && Mathf.Abs(Fov.Value - other.Fov.Value) > 1e-3f)
            {
                return false;
            }

            if (Active != other.Active)
            {
                return false;
            }

            return true;
        }
    }
}
