using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KabuUnityMcp.Editor.Extensions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>技能编辑器的 MCP 编排入口。批量修改先在独立配置上计算，通过校验后才提交给窗口。</summary>
internal static class TimelineEditorMcpAuthoring
{
    private static TimelineEditorMcpHistory history = new TimelineEditorMcpHistory();
    private static TimelineEditorMcpClipboard clipboard = new TimelineEditorMcpClipboard();

    [InitializeOnLoadMethod]
    private static void Register()
    {
        Register("Catalog", Catalog);
        Register("Read", _ => Read(Window()));
        Register("Actions", Actions);
        Register("Edit", Edit);
        Register("Command", Command);
    }

    private static void Register(string name, Func<JToken, object> execute)
    {
        McpMethodRegistry.Register("unity.skillEditorAuthoring" + name, (id, args, client) =>
        {
            try
            {
                _ = client.SendResponseAsync(id, execute(args));
            }
            catch (Exception exception)
            {
                _ = client.SendErrorAsync(id, -32000, exception.Message);
            }
        });
    }

    private static EditorTimeLine Window()
    {
        return Resources.FindObjectsOfTypeAll<EditorTimeLine>().FirstOrDefault(window => window.TimelineControl is not null)
            ?? throw new InvalidOperationException("请先打开技能编辑器。");
    }

    private static object Catalog(JToken args)
    {
        // 类型工厂只生成配置和编辑器包装对象，尚未创建预览实例。
        var factory = new TimelineControl();
        var filter = (string)args?["type"];
        var types = Enum.GetValues(typeof(TimelineClipType)).Cast<TimelineClipType>()
            .Where(type => string.IsNullOrEmpty(filter) || type.ToString().Equals(filter, StringComparison.OrdinalIgnoreCase))
            .Select(type => TimelineEditorMcpCatalog.Describe(factory, type)).ToArray();
        if (types.Length == 0)
        {
            throw new ArgumentException("未知 Clip 类型：" + filter);
        }

        return new
        {
            types,
            edits = TimelineEditorMcpCatalog.Edits,
            commands = TimelineEditorMcpCatalog.Commands,
            newTemplate = "new 与界面新建共用3D模板：配置施法对象 → 配置效果来源和目标 → PlayAnimator，依次连线；默认主角为Archer/Archer，来源为施法者，目标为敌方第1个（$enemy:0）。",
            timelineFields = new
            {
                PreviewCasterAddress = "仅供编辑器预览的单位逻辑名，随 Timeline JSON 保存。空值或缺字段使用Archer/Archer。可用 assets(type=Prefab) 查询单位候选；preview_unit 需要 revision，空 address 恢复默认。切换先校验资源和动画，暂停并保留播放头；录制或定位期间禁用。实战使用实际施法者。"
            },
            eventFields = new
            {
                EventName = "自定义事件名称。",
                Time = "Normal 为内容内秒数，Percent 为内容内 0 到 1 的比例。",
                ValueType = Enum.GetNames(typeof(ValueType)),
                EventKey = "稳定 ID，由新增事件操作生成。",
                TimelineEventType = "CustomEvent 可创建和编辑，AnimationEvent 来自动画资源。"
            },
            notes = "字段名与技能 JSON 一致。时间单位为秒。节点使用 ClipKey，事件使用 EventKey。patch 可提交部分字段，集合字段整组替换。"
        };
    }

    private static string Revision(EditorTimeLine window, TimelineConfig config)
    {
        using (var hash = SHA256.Create())
        {
            var value = window.GetInstanceID() + "\n" + window.CurrentTimelineFilePath + "\n" + TimelineJsonUtil.ToJson(config, false);
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "");
        }
    }

    private static TimelineConfig Capture(TimelineControl control) => control.CaptureDocument();

    internal static JObject CaptureReloadHistory(EditorTimeLine window, TimelineConfig current)
        => history.Capture(window, Revision(window, current));

    internal static void RestoreReloadHistory(EditorTimeLine window, JObject state)
        => history.Restore(window, Revision(window, Capture(window.TimelineControl)), state);

    private static object Read(EditorTimeLine window)
    {
        var control = window.TimelineControl;
        var config = Capture(control);
        return new
        {
            timelineId = config.TimelineId,
            description = config.Description,
            previewCasterAddress = config.PreviewCasterAddress,
            effectiveCasterAddress = control.MyTransformPath,
            durationBindClipKey = config.DurationBindClipKey,
            path = window.CurrentTimelineFilePath,
            revision = Revision(window, config),
            window.hasUnsavedChanges,
            selectedClipKey = control.GetSelectedClip()?.Config.ClipKey,
            selectedEventKey = control.GetSelectedEvent()?.Config.EventKey,
            selectedNode = control.GetSelectedNode()?.Kind.ToString(),
            clips = control.Clips.Select((clip, index) => new
            {
                index,
                row = index + 1,
                clipKey = clip.Config.ClipKey,
                type = clip.Config.Type.ToString(),
                label = UnitFeatureTypeDisplay.GetClipLabelName(clip.Config),
                config = JObject.Parse(TimelineJsonUtil.ToJson(config.Clips[index])),
                contentStart = clip.GetContentTimelineStartTime(),
                contentEnd = control.ResolveClipContentEndTime(clip.Config),
                nodes = new[] { "head", "tail" },
                events = clip.GetAllEvents().Select(point => new
                {
                    key = point.Config.EventKey,
                    name = point.Config.EventName,
                    type = point.Config.TimelineEventType.ToString(),
                    valueType = point.Config.ValueType.ToString(),
                    time = point.Config.Time,
                    timelineTime = clip.GetContentTimelineStartTime() + TimelineSchedule.GetEventOffset(clip.Config, point.Config)
                }).ToArray()
            }).ToArray(),
            links = config.Clips.Where(clip => !string.IsNullOrEmpty(clip.PreKey))
                .Select(clip => new { from = clip.PreKey, to = clip.ClipKey }).ToArray()
        };
    }

    private static object Actions(JToken args)
    {
        var control = Window().TimelineControl;
        var target = (string)args?["target"] ?? "background";
        var key = (string)args?["key"];
        var clip = control.Clips.Find(item => item.Config.ClipKey == key);
        var point = control.Clips.SelectMany(item => item.GetAllEvents()).FirstOrDefault(item => item.Config.EventKey == key);
        switch (target)
        {
            case "background":
            {
                return new
                {
                    target,
                    actions = new[] { Action("add_clip"), Action("paste_clip", clipboard.HasClip ? null : "剪贴板为空。") },
                    types = UnitFeatureTypeDisplay.Types.Select(type => Action(type.ToString(),
                        TimelineEditorTargetUtil.ClipTypeRequiresSetTargets(type) && !TimelineEditorTargetUtil.HasSetTargetsClip(control)
                            ? TimelineEditorTargetUtil.MissingTargetClipHint : null)).ToArray()
                };
            }
            case "clip" when clip is not null:
            {
                var index = control.Clips.IndexOf(clip);
                return new { target, key, actions = new[]
                {
                    Action("add_event"), Action("patch_clip"), Action("move_clip"), Action("copy_clip"),
                    Action("layer_up", index == 0 ? "已在最上层。" : null),
                    Action("layer_down", index == control.Clips.Count - 1 ? "已在最下层。" : null),
                    Action("remove_clip")
                } };
            }
            case "event" when point is not null:
            {
                var reason = point.Config.TimelineEventType == TimelineEventType.AnimationEvent ? "动画事件由资源提供。" : null;
                return new { target, key, actions = new[]
                {
                    Action("connect"), Action("disconnect_outputs", OutputReason(control, key)),
                    Action("patch_event", reason), Action("remove_event", reason)
                } };
            }
            case "head" when clip is not null:
            {
                return new { target, key, actions = new[]
                {
                    Action("connect"), Action("disconnect_input", string.IsNullOrEmpty(clip.Config.PreKey) ? "当前没有入边。" : null)
                } };
            }
            case "tail" when clip is not null:
            {
                return new { target, key, actions = new[] { Action("connect"), Action("disconnect_outputs", OutputReason(control, key)) } };
            }
            default:
            {
                throw new ArgumentException("未知目标类型或目标已不存在。");
            }
        }
    }

    private static object Action(string id, string reason = null) => new { id, enabled = reason is null, reason };

    private static string OutputReason(TimelineControl control, string key)
        => control.Clips.Exists(item => item.Config.PreKey == key) ? null : "当前没有出边。";

    private static object Edit(JToken args)
    {
        var window = Window();
        var before = Capture(window.TimelineControl);
        CheckRevision(window, before, (string)args?["revision"]);
        if (args?["operations"] is not JArray operations || operations.Count == 0 || operations.Count > 256)
        {
            throw new ArgumentException("operations 必须包含 1 到 256 项。");
        }

        var draft = TimelineJsonUtil.FromJson(TimelineJsonUtil.ToJson(before, false));
        var generatedEvents = TimelineEditorMcpValidation.GeneratedEvents(window.TimelineControl);
        var batch = new TimelineEditorMcpBatch(draft, window.TimelineControl, clipboard, generatedEvents);
        for (var index = 0; index < operations.Count; index++)
        {
            try
            {
                batch.Apply((JObject)operations[index]);
            }
            catch (Exception exception)
            {
                throw new ArgumentException($"operations[{index}] 失败：{exception.Message}", exception);
            }
        }
        TimelineEditorMcpValidation.ValidatePreviewCaster(draft);
        TimelineEditorMcpValidation.ResolveSpineDurations(draft, draft.PreviewCasterAddress);
        TimelineEditorMcpValidation.Editor(draft, generatedEvents, true);
        var dryRun = (bool?)args["dryRun"] ?? true;
        if (!dryRun)
        {
            RequireIdle(window);
            ApplyDraft(window, before, draft);
            history.Record(window, before, Revision(window, before), Revision(window, Capture(window.TimelineControl)));
        }

        return new
        {
            applied = !dryRun,
            revision = dryRun ? Revision(window, before) : Revision(window, Capture(window.TimelineControl)),
            aliases = batch.Aliases,
            clips = draft.Clips.Count,
            preview = dryRun ? JObject.Parse(TimelineJsonUtil.ToJson(draft, false)) : null,
            battle = TimelineEditorMcpValidation.Battle(draft, draft.PreviewCasterAddress)
        };
    }

    private static void ApplyDraft(EditorTimeLine window, TimelineConfig before, TimelineConfig draft)
    {
        try
        {
            window.ReplaceTimelineForMcp(draft, false);
        }
        catch
        {
            window.ReplaceTimelineForMcp(before, false);
            throw;
        }
    }

    private static object Command(JToken request)
    {
        var id = (string)request?["command"];
        var args = request?["arguments"] as JObject ?? new JObject();
        if (id == "assets")
        {
            return FindAssets(args);
        }

        var window = Window();
        var control = window.TimelineControl;
        var before = Capture(control);
        switch (id)
        {
            case "preview_stage":
                return TimelinePortraitPreview.Configure(window, args);
            case "capture":
                return TimelinePortraitPreview.Capture(window, args);
            case "diagnostics":
                return TimelinePortraitPreview.Diagnostics(window);
            case "validate":
            {
                return TimelineEditorMcpValidation.Report(before, control);
            }
            case "copy_clip":
            {
                clipboard.Copy(args, before);
                return new { copied = true };
            }
            case "animations":
            {
                return Animations(control, args);
            }
            case "select":
            {
                Select(control, args);
                return new { selected = true };
            }
            case "locate_clip":
            {
                var key = (string)args["key"];
                var clip = control.Clips.Find(item => item.Config.ClipKey == key)
                    ?? throw new ArgumentException("找不到 Clip：" + key);
                window.LocateClip(clip);
                return new { locatedClipKey = key, row = control.Clips.IndexOf(clip) + 1 };
            }
            case "select_next":
            {
                var query = (string)args["query"] ?? "";
                var start = control.Clips.IndexOf(control.GetSelectedClip());
                var selected = Enumerable.Range(1, control.Clips.Count)
                    .Select(offset => control.Clips[(start + offset) % control.Clips.Count])
                    .FirstOrDefault(clip => UnitFeatureTypeDisplay.GetClipLabelName(clip.Config)
                        .IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? throw new ArgumentException("未找到匹配的 Clip。");
                control.SelectClipProgrammatically(selected);
                return new { selectedClipKey = selected.Config.ClipKey };
            }
            case "step":
            {
                RequireIdle(window);
                var frames = (int?)args["frames"] ?? 1;
                if (frames == 0 || Math.Abs((long)frames) > 240)
                {
                    throw new ArgumentException("frames 必须介于 -240 和 240 之间，且非零。");
                }
                window.RunningFrame += frames;
                window.Repaint();
                return new { window.RunningFrame, window.RunningTime };
            }
            case "preview_unit":
            {
                CheckRevision(window, before, (string)args["revision"]);
                control.SetPreviewCaster((string)args["address"]);
                history.Record(window, before, Revision(window, before), Revision(window, Capture(control)));
                return new { address = control.MyTransformPath, previewCasterAddress = control.Config.PreviewCasterAddress };
            }
            case "preview_units":
            {
                return new
                {
                    casterAddress = control.MyTransformPath,
                    units = control.UnitsControl.Units.Select(unit => new
                    {
                        key = control.PreviewBindings.GetActorKey(unit),
                        label = control.PreviewBindings.GetActorLabel(unit),
                        name = unit.name,
                        team = unit.TeamType.ToString(),
                        caster = unit.Transform == control.MyTransform,
                        position = Vector(unit.Transform.position),
                        euler = Vector(unit.Transform.eulerAngles),
                        scale = Vector(unit.Transform.localScale)
                    }).ToArray()
                };
            }
            case "timeline_view":
            {
                RequireIdle(window);
                return ConfigureView(window, args);
            }
            case "load":
            {
                RequireIdle(window);
                if (!window.TryLoadTimelineForMcp((string)args["path"], (bool?)args["discardChanges"] ?? false, out var error))
                {
                    throw new InvalidOperationException(error);
                }
                history.Clear();
                return Read(window);
            }
            case "new":
            {
                RequireIdle(window);
                CheckRevision(window, before, (string)args["revision"]);
                window.RefreshUnsavedChangesFlag();
                if (window.hasUnsavedChanges && (bool?)args["discardChanges"] != true)
                {
                    throw new InvalidOperationException("当前技能有未保存改动。");
                }
                var timelineId = (string)args["timelineId"];
                if (string.IsNullOrWhiteSpace(timelineId))
                {
                    throw new ArgumentException("请提供 timelineId。");
                }
                window.ReplaceTimelineForMcp(EditorTimeLine.CreateDefaultTimelineConfig(timelineId), true);
                history.Clear();
                return Read(window);
            }
            case "save":
            case "save_as":
            {
                RequireIdle(window);
                CheckRevision(window, before, (string)args["revision"]);
                var path = (string)args["path"];
                if (id == "save_as" && string.IsNullOrWhiteSpace(path))
                {
                    throw new ArgumentException("另存为需要 path。");
                }
                TimelineEditorMcpValidation.RequireEditor(before, control);
                window.SaveTimelineForMcp(path, (bool?)args["overwrite"] ?? false);
                history.Clear();
                return Read(window);
            }
            case "undo":
            case "redo":
            {
                RequireIdle(window);
                var revision = Revision(window, before);
                CheckRevision(window, before, (string)args["revision"]);
                var draft = history.Peek(window, revision, id == "redo");
                ApplyDraft(window, before, draft);
                history.Advance(before, Revision(window, Capture(window.TimelineControl)), id == "redo");
                return Read(window);
            }
            default:
            {
                throw new ArgumentException("未知命令：" + id);
            }
        }
    }

    private static void CheckRevision(EditorTimeLine window, TimelineConfig before, string revision)
    {
        if (revision != Revision(window, before))
        {
            throw new InvalidOperationException("Timeline 已变更或 revision 缺失，请重新读取后再提交。");
        }
    }

    private static void RequireIdle(EditorTimeLine window)
    {
        if (window.IsPlaying || window.IsRecording || window.IsPlayheadLerping)
        {
            throw new InvalidOperationException("请停止播放、录制或等待定位结束。");
        }
    }

    private static object FindAssets(JObject args)
    {
        var type = (string)args["type"] ?? "Prefab";
        if (type != "Prefab" && type != "AnimationClip" && type != "Material" && type != "TextAsset")
        {
            throw new ArgumentException("type 支持 Prefab、AnimationClip、Material、TextAsset。");
        }
        var query = (string)args["query"] ?? "";
        var scope = (string)args["scope"] ?? "runtime";
        if (scope != "runtime" && scope != "all") throw new ArgumentException("scope 为 runtime 或 all。");
        var offset = Math.Max(0, (int?)args["offset"] ?? 0);
        var limit = Mathf.Clamp((int?)args["limit"] ?? 50, 1, 200);
        var paths = AssetDatabase.FindAssets("t:" + type, new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => scope == "all" || path.StartsWith("Assets/Package_", StringComparison.OrdinalIgnoreCase))
            .Where(path => path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new
        {
            total = paths.Length,
            offset,
            items = paths.Skip(offset).Take(limit).Select(path => new
            {
                path,
                address = path.StartsWith("Assets/Package_Unit/", StringComparison.OrdinalIgnoreCase) && type == "Prefab"
                    ? EditorTimelineAssetPathUtil.UnitLogicalFromAssetPath(path)
                    : path.StartsWith("Assets/Package_Effect/", StringComparison.OrdinalIgnoreCase) && type == "Prefab"
                        ? EditorTimelineAssetPathUtil.EffectLogicalFromAssetPath(path)
                        : null
            }).ToArray()
        };
    }

    private static object ConfigureView(EditorTimeLine window, JObject args)
    {
        var rate = (float?)args["frameRate"] ?? window._frameRate;
        if (rate != 24 && rate != 30 && rate != 50 && rate != 60)
        {
            throw new ArgumentException("frameRate 支持 24、30、50 或 60。");
        }
        var cutoff = (double?)args["cutoffSeconds"] ?? window.CutOffTime;
        if (!(cutoff > 0d) || double.IsInfinity(cutoff))
        {
            throw new ArgumentException("cutoffSeconds 必须为有限正数。");
        }
        window._frameRate = rate;
        window._timeInFrames = (bool?)args["displayFrames"] ?? window._timeInFrames;
        window._frameSnap = (bool?)args["frameSnap"] ?? window._frameSnap;
        window._edgeSnaps = (bool?)args["edgeSnap"] ?? window._edgeSnaps;
        window.CutOffTime = cutoff;
        window.Repaint();
        return new { frameRate = rate, displayFrames = window._timeInFrames, frameSnap = window._frameSnap, edgeSnap = window._edgeSnaps, cutoffSeconds = cutoff };
    }

    private static float[] Vector(Vector3 value) => new[] { value.x, value.y, value.z };

    private static object Animations(TimelineControl control, JObject args)
    {
        var unit = control.MyTransform;
        var key = (string)args["clipKey"];
        if (!string.IsNullOrEmpty(key))
        {
            var clip = control.Clips.Find(item => item.Config.ClipKey == key)
                ?? throw new ArgumentException("找不到 Clip：" + key);
            unit = clip.Instance ? clip.Instance.transform : unit;
        }
        if (!unit)
        {
            throw new InvalidOperationException("当前没有预览单位。");
        }
        var animator = unit.GetComponentInChildren<Animator>(true);
        object[] animatorStates = null;
        if (animator && animator.runtimeAnimatorController)
        {
            var names = new List<string>();
            EditorAnimatorStateUtil.CopyStateNames(animator.runtimeAnimatorController, names);
            if (names.Count > 0)
            {
                animatorStates = names.Select(stateName => new
                {
                    name = stateName,
                    seconds = EditorAnimatorStateUtil.GetStateLength(animator.runtimeAnimatorController, stateName)
                }).Cast<object>().ToArray();
            }
            else
            {
                animatorStates = animator.runtimeAnimatorController.animationClips
                    .Select(animation => new { name = animation.name, seconds = animation.length })
                    .Cast<object>().ToArray();
            }
        }
        return new
        {
            spine = (object)null,
            animator = animatorStates
        };
    }

    private static void Select(TimelineControl control, JObject args)
    {
        var key = (string)args["key"];
        var target = (string)args["target"] ?? "clip";
        if (target == "event")
        {
            var point = control.Clips.SelectMany(clip => clip.GetAllEvents()).FirstOrDefault(item => item.Config.EventKey == key)
                ?? throw new ArgumentException("找不到事件：" + key);
            control.SelectEventProgrammatically(point);
            return;
        }
        var selected = control.Clips.Find(clip => clip.Config.ClipKey == key
            || (key is null && UnitFeatureTypeDisplay.GetClipLabelName(clip.Config)
                .IndexOf((string)args["query"] ?? "", StringComparison.OrdinalIgnoreCase) >= 0))
            ?? throw new ArgumentException("未找到匹配的 Clip。");
        if (target == "head" || target == "tail")
        {
            var node = selected.Nodes.Find(item => (item.Kind == TimelineNodeKind.End) == (target == "tail"))
                ?? throw new ArgumentException("没有对应节点。");
            control.SelectNodeProgrammatically(node);
        }
        else
        {
            control.SelectClipProgrammatically(selected);
        }
    }
}
