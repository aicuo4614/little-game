using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>在独立的 Timeline 草稿上执行编排动作，批次结束后统一校验引用和时间。</summary>
internal sealed class TimelineEditorMcpBatch
{
    private TimelineConfig draft;
    private TimelineControl control;
    private TimelineEditorMcpClipboard clipboard;
    private (string ClipKey, TimelineEventConfigBase Point)[] generatedEvents;

    /// <summary>本批临时别名及其生成的持久键。</summary>
    public Dictionary<string, string> Aliases { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>预览对象仅用于查询帧率和类型默认值。</summary>
    public TimelineEditorMcpBatch(TimelineConfig draft, TimelineControl control, TimelineEditorMcpClipboard clipboard,
        (string ClipKey, TimelineEventConfigBase Point)[] generatedEvents)
    {
        this.draft = draft;
        this.control = control;
        this.clipboard = clipboard;
        this.generatedEvents = generatedEvents;
    }

    /// <summary>应用单个动作。标识字段支持引用同批较早动作的别名。</summary>
    public void Apply(JObject args)
    {
        var action = (string)args["action"];
        var key = Key(args["key"]);
        switch (action)
        {
            case "patch_timeline":
            {
                var patch = Patch(args);
                foreach (var field in patch.Properties())
                {
                    switch (field.Name)
                    {
                        case "TimelineId": { draft.TimelineId = RequiredString(field.Value, field.Name); break; }
                        case "Description": { draft.Description = (string)field.Value; break; }
                        case "PreviewCasterAddress":
                        {
                            var logical = EditorTimelineAssetPathUtil.ToUnitLogicalName((string)field.Value);
                            draft.PreviewCasterAddress = string.IsNullOrEmpty(logical) ? null : logical;
                            break;
                        }
                        case "DurationBindClipKey": { draft.DurationBindClipKey = Key(field.Value); break; }
                        default: { throw new ArgumentException("未知 Timeline 字段：" + field.Name); }
                    }
                }
                break;
            }
            case "add_clip":
            {
                var requested = RequiredString(args["type"], "type");
                if (!Enum.TryParse(requested, true, out TimelineClipType type) || !control.CanAddClip(type))
                {
                    throw new ArgumentException("该 Clip 类型不可新建：" + requested);
                }
                var clip = control.CreateEmptyClip(type).Config;
                Insert(UpdateClip(clip, args["patch"] as JObject), (int?)args["index"], args);
                break;
            }
            case "patch_clip":
            {
                var clip = Clip(key);
                draft.Clips[draft.Clips.IndexOf(clip)] = UpdateClip(clip, Patch(args));
                break;
            }
            case "move_clip":
            {
                var clip = Clip(key);
                var seconds = (float?)args["seconds"]
                    ?? ((int?)args["frames"] ?? throw new ArgumentException("需要 seconds 或 frames。"))
                    / control.Timeline._frameRate;
                if (string.IsNullOrEmpty(clip.PreKey))
                {
                    clip.StartTime = Mathf.Max(0f, clip.StartTime + seconds);
                }
                else
                {
                    clip.DelayTime = Mathf.Max(0f, clip.DelayTime + seconds);
                }
                break;
            }
            case "resize_clip":
            {
                var clip = Clip(key);
                var frames = (int?)args["frames"] ?? throw new ArgumentException("需要 frames。");
                Resize(clip, frames);
                break;
            }
            case "layer_up":
            case "layer_down":
            case "reorder_clip":
            {
                var clip = Clip(key);
                var from = draft.Clips.IndexOf(clip);
                var to = (int?)args["index"] ?? from + (action == "layer_up" ? -1 : 1);
                if (to < 0 || to >= draft.Clips.Count)
                {
                    throw new ArgumentException("目标层超出范围。");
                }
                draft.Clips.RemoveAt(from);
                draft.Clips.Insert(to, clip);
                break;
            }
            case "duplicate_clip":
            case "paste_clip":
            {
                var copy = action == "paste_clip"
                    ? clipboard.Paste()
                    : TimelineJsonUtil.FromTimelineClipConfigJson(TimelineJsonUtil.ToJson(Clip(key)));
                if (!control.CanAddClip(copy.Type))
                {
                    throw new ArgumentException("该 Clip 类型不可粘贴。");
                }
                TimelineControl.PreparePastedClipConfig(copy, (float?)args["startTime"] ?? copy.StartTime);
                // 复制只携带片段及其事件，跨文档引用由调用方重新绑定。
                copy.TargetBindingKey = null;
                copy.EndByOtherClipEnd = null;
                if (copy is TimelineClipCameraShotBlendConfig blend)
                {
                    blend.NextCameraShotClipKey = null;
                }
                if (copy is TimelineClipUnitConfigBase unitCopy)
                {
                    unitCopy.SubjectBindingKey = null;
                }
                if (copy is TimelineClipRoleRotateToTargetConfig faceCopy)
                {
                    faceCopy.DestinationBindingKey = null;
                }
                if (copy is TimelineClipTranslateConfig translate)
                {
                    translate.SubjectBindingKey = null;
                    translate.DestinationBindingKey = null;
                }
                Insert(UpdateClip(copy, args["patch"] as JObject), (int?)args["index"], args);
                break;
            }
            case "remove_clip":
            {
                var clip = Clip(key);
                draft.Clips.Remove(clip);
                ClearReferences(key);
                foreach (var point in (IEnumerable<TimelineEventConfigBase>)clip.EventConfigs ?? Enumerable.Empty<TimelineEventConfigBase>())
                {
                    ClearReferences(point.EventKey);
                }
                foreach (var pair in generatedEvents.Where(entry => entry.ClipKey == key))
                {
                    ClearReferences(pair.Point.EventKey);
                }
                break;
            }
            case "add_event":
            {
                var clip = Clip(key);
                var point = new TimelineEventConfigBase
                {
                    EventKey = TimelineKeys.GenerateEventKey(),
                    TimelineEventType = TimelineEventType.CustomEvent,
                    ValueType = ValueType.Normal,
                    EventName = "hit"
                };
                UpdateEvent(point, args["patch"] as JObject);
                clip.EventConfigs ??= new List<TimelineEventConfigBase>();
                clip.EventConfigs.Add(point);
                AddAlias(args, point.EventKey);
                break;
            }
            case "patch_event":
            case "remove_event":
            {
                if (generatedEvents.Any(pair => pair.Point.EventKey == key))
                {
                    throw new ArgumentException("动画资源生成的事件只读。");
                }
                var owner = draft.Clips.Find(clip => clip.EventConfigs?.Exists(point => point.EventKey == key) == true)
                    ?? throw new ArgumentException("找不到事件：" + key);
                var point = owner.EventConfigs.Find(item => item.EventKey == key);
                if (point.TimelineEventType != TimelineEventType.CustomEvent)
                {
                    throw new ArgumentException("动画事件只读。");
                }
                if (action == "patch_event")
                {
                    UpdateEvent(point, Patch(args));
                }
                else
                {
                    owner.EventConfigs.Remove(point);
                    ClearReferences(key);
                }
                break;
            }
            case "connect":
            {
                Clip(Key(args["to"])).PreKey = Key(args["from"])
                    ?? throw new ArgumentException("需要前置 ClipKey 或 EventKey。");
                break;
            }
            case "disconnect_input":
            {
                Clip(key).PreKey = "";
                break;
            }
            case "disconnect_outputs":
            {
                if (string.IsNullOrEmpty(key))
                {
                    throw new ArgumentException("需要 ClipKey 或 EventKey。");
                }
                foreach (var clip in draft.Clips.Where(item => item.PreKey == key))
                {
                    clip.PreKey = "";
                }
                break;
            }
            default:
            {
                throw new ArgumentException("未知编排动作：" + action);
            }
        }
    }

    private TimelineClipConfigBase UpdateClip(TimelineClipConfigBase clip, JObject patch)
    {
        if (patch is null)
        {
            return clip;
        }

        var serializer = JsonSerializer.Create(TimelineJsonUtil.ConcreteConfig);
        serializer.MissingMemberHandling = MissingMemberHandling.Error;
        serializer.NullValueHandling = NullValueHandling.Include;
        var contract = (Newtonsoft.Json.Serialization.JsonObjectContract)serializer.ContractResolver.ResolveContract(clip.GetType());
        var json = JObject.Parse(TimelineJsonUtil.ToJson(clip));
        foreach (var field in patch.Properties())
        {
            if (!TimelineEditorMcpCatalog.IsPatchField(clip.Type, field.Name)
                || contract.Properties.GetProperty(field.Name, StringComparison.Ordinal)?.Writable != true)
            {
                throw new ArgumentException("未知或只读 Clip 字段：" + field.Name);
            }
            if (field.Name.EndsWith("Key", StringComparison.Ordinal) || field.Name == "EndByOtherClipEnd")
            {
                json[field.Name] = new JValue(Key(field.Value));
            }
            else
            {
                var value = field.Value.DeepClone();
                if (clip is TimelineClipCameraShotConfig shot && field.Name is "LookPivot" or "FromPivot"
                    && value is JObject pivot)
                {
                    if (pivot["BindingKey"]?.Type == JTokenType.String)
                    {
                        pivot["BindingKey"] = Key(pivot["BindingKey"]);
                    }
                    if (json[field.Name] is not JObject)
                    {
                        json[field.Name] = JObject.FromObject(
                            TimelineCameraPivotPlayback.Data(shot, field.Name == "FromPivot"), serializer);
                    }
                }
                if (json[field.Name] is JObject current && value is JObject changes)
                {
                    current.Merge(changes, new JsonMergeSettings
                    {
                        MergeArrayHandling = MergeArrayHandling.Replace,
                        MergeNullValueHandling = MergeNullValueHandling.Merge
                    });
                }
                else
                {
                    json[field.Name] = value;
                }
            }
        }
        return (TimelineClipConfigBase)json.ToObject(clip.GetType(), serializer);
    }

    private void Resize(TimelineClipConfigBase clip, int frames)
    {
        var rate = control.Timeline._frameRate;
        if (frames == 0 || Math.Abs((long)frames) > 36000 || !string.IsNullOrEmpty(clip.EndByOtherClipEnd))
        {
            throw new ArgumentException("需要有效帧数及可编辑的时长。");
        }
        var duration = Mathf.Max(clip.Type == TimelineClipType.Move ? 0 : 1,
            Mathf.RoundToInt(clip.Duration * rate) + frames) / rate;
        switch (clip)
        {
            case TimelineClipTranslateConfig translate when translate.DurationType == DurationType.Time:
            {
                translate.DurationPraram = duration;
                break;
            }
            case TimelineClipRotateConfig rotate when rotate.DurationType == DurationType.Time:
            {
                rotate.DurationPraram = duration;
                break;
            }
            case TimelineClipScaleConfig scale when scale.DurationType == DurationType.Time:
            {
                scale.DurationPraram = duration;
                break;
            }
            case TimelineClipTranslateConfig or TimelineClipRotateConfig or TimelineClipScaleConfig:
            {
                throw new ArgumentException("速度模式下请修改 DurationPraram。");
            }
            case TimelineClipPlayHitConfig hit when string.IsNullOrEmpty(hit.EffectPath)
                && (!string.IsNullOrEmpty(hit.AnimatorParamName) || !string.IsNullOrEmpty(hit.SpineAnimationName)):
            {
                throw new ArgumentException("纯动画受击时长由动画决定。");
            }
            default:
            {
                var wrapper = control.CreateEmptyClip(clip.Type);
                wrapper.SetConfig(clip);
                if (!(clip is TimelineClipMoveConfig || clip is TimelineClipPlayHitConfig)
                    && !wrapper.TryNudgeEditableDuration(1f / rate, frames))
                {
                    throw new ArgumentException("该 Clip 的时长自动计算或不可编辑。");
                }
                break;
            }
        }
        clip.Duration = duration;
    }

    private static void UpdateEvent(TimelineEventConfigBase point, JObject patch)
    {
        if (patch is null)
        {
            return;
        }
        var oldMode = point.ValueType;
        foreach (var field in patch.Properties())
        {
            switch (field.Name)
            {
                case "Time": { point.Time = (float)field.Value; break; }
                case "EventName": { point.EventName = RequiredString(field.Value, field.Name); break; }
                case "ValueType":
                {
                    if (!Enum.TryParse((string)field.Value, true, out ValueType mode) || !Enum.IsDefined(typeof(ValueType), mode))
                    {
                        throw new ArgumentException("未知事件计时方式。");
                    }
                    point.ValueType = mode;
                    break;
                }
                default: { throw new ArgumentException("未知或只读事件字段：" + field.Name); }
            }
        }
        if (oldMode != point.ValueType && patch["Time"] is null)
        {
            throw new ArgumentException("切换事件计时方式时须同时指定 Time。");
        }
    }

    private TimelineClipConfigBase Clip(string key)
        => draft.Clips.Find(clip => clip.ClipKey == key) ?? throw new ArgumentException("找不到 Clip：" + key);

    private static JObject Patch(JObject args)
        => args["patch"] as JObject ?? throw new ArgumentException("需要 patch 对象。");

    private static string RequiredString(JToken token, string name)
    {
        var value = (string)token;
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("需要 " + name + "。") : value;
    }

    private string Key(JToken token)
    {
        var key = (string)token;
        if (key?.StartsWith("$", StringComparison.Ordinal) == true)
        {
            if (!Aliases.TryGetValue(key.Substring(1), out var resolved))
            {
                throw new ArgumentException("未知编辑别名：" + key);
            }
            return resolved;
        }
        return key;
    }

    private void AddAlias(JObject args, string key)
    {
        var alias = (string)args["alias"];
        if (alias is not null)
        {
            if (string.IsNullOrWhiteSpace(alias) || alias.StartsWith("$", StringComparison.Ordinal) || Aliases.ContainsKey(alias))
            {
                throw new ArgumentException("alias 为空、含 $ 前缀或已重复。");
            }
            Aliases.Add(alias, key);
        }
    }

    private void Insert(TimelineClipConfigBase clip, int? index, JObject args)
    {
        if (TimelineEditorTargetUtil.ClipTypeRequiresSetTargets(clip.Type)
            && !draft.Clips.Exists(item => item.Type == TimelineClipType.SetCastTargets))
        {
            throw new ArgumentException(TimelineEditorTargetUtil.MissingTargetClipHint);
        }
        draft.Clips.Insert(index ?? draft.Clips.Count, clip);
        AddAlias(args, clip.ClipKey);
    }

    private void ClearReferences(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }
        if (draft.DurationBindClipKey == key)
        {
            draft.DurationBindClipKey = null;
        }
        foreach (var clip in draft.Clips)
        {
            if (clip.PreKey == key)
            {
                clip.PreKey = "";
            }
            if (clip.TargetBindingKey == key)
            {
                clip.TargetBindingKey = null;
            }
            if (clip is TimelineClipVisibilityConfig visibility && visibility.EffectBindingKey == key)
            {
                visibility.EffectBindingKey = null;
            }
            if (clip.EndByOtherClipEnd == key)
            {
                clip.EndByOtherClipEnd = null;
            }
            if (clip is TimelineClipCameraShotConfig shot)
            {
                ClearPivotBindings(shot, key);
            }
            if (clip is TimelineClipCameraShotBlendConfig blend && blend.NextCameraShotClipKey == key)
            {
                blend.NextCameraShotClipKey = null;
            }
            if (clip is TimelineClipUnitConfigBase unit && unit.SubjectBindingKey == key)
            {
                unit.SubjectBindingKey = null;
            }
            if (clip is TimelineClipRoleRotateToTargetConfig face && face.DestinationBindingKey == key)
            {
                face.DestinationBindingKey = null;
            }
            if (clip is TimelineClipTranslateConfig translate)
            {
                if (translate.SubjectBindingKey == key)
                {
                    translate.SubjectBindingKey = null;
                }
                if (translate.DestinationBindingKey == key)
                {
                    translate.DestinationBindingKey = null;
                }
            }
        }
    }
    internal static void ClearPivotBindings(TimelineClipCameraShotConfig shot, string key = null)
    {
        if (shot.LookPivot is TimelineCameraPivotData look && (key is null || look.BindingKey == key))
        {
            look.BindingKey = null;
        }
        if (shot.FromPivot is TimelineCameraPivotData from && (key is null || from.BindingKey == key))
        {
            from.BindingKey = null;
        }
    }
}
