using System;
using System.Collections.Generic;
using System.IO;
using KabuUnityMcp.Editor.Extensions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 将技能编辑器能力注册到 kabu-unity-mcp Host，不把 Timeline 类型泄漏进 MCP 包。
/// </summary>
[InitializeOnLoad]
public static class TimelineEditorMcpBootstrap
{
    private static TimelineEditorMcpHost Host = new TimelineEditorMcpHost();

    static TimelineEditorMcpBootstrap()
    {
        McpSkillEditorHostRegistry.Register(Host);
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
    }

    private static void OnBeforeReload()
    {
        McpSkillEditorHostRegistry.Unregister(Host);
    }
}

/// <summary>技能编辑器 MCP Host 实现。</summary>
public sealed class TimelineEditorMcpHost : IMcpSkillEditorHost
{
    private const int MaxInlineJsonChars = 350000;

    public McpHostResult Open()
    {
        EditorTimeLine.DoWindow();
        var window = EditorWindow.GetWindow<EditorTimeLine>(false, "技能编辑器", false);
        return McpHostResult.Success(new
        {
            opened = window != null,
            message = "技能编辑器已打开。"
        });
    }

    public McpHostResult Status()
    {
        var window = FindWindow();
        if (window == null)
        {
            return McpHostResult.Success(new
            {
                windowOpen = false,
                hasHost = true
            });
        }

        var control = window.TimelineControl;
        var contentEnd = control != null ? control.ResolvePreviewContentEndTime() : 0f;
        var autoStop = TimelinePreviewRecorder.ResolveAutoStopTime(control, (float)window.CutOffTime);
        return McpHostResult.Success(new
        {
            windowOpen = true,
            timelineId = control?.Config?.TimelineId,
            skillPath = window.CurrentTimelineFilePath,
            previewCasterAddress = control?.Config?.PreviewCasterAddress,
            effectiveCasterAddress = control?.MyTransformPath,
            casterTeam = control != null ? control.PreviewCasterTeam.ToString() : null,
            previewQuality = EditorTimelineAssetPathUtil.PreviewQuality.ToString(),
            previewLayout = TimelinePortraitPreview.Detail ? "Detail" : "Battle",
            previewAspect = PortraitBattleCameraSettings.Aspect,
            runningTime = window.RunningTime,
            cutoffTime = window.CutOffTime,
            contentEnd,
            autoStop,
            isPlaying = window.IsPlaying,
            isRecording = window.IsRecording,
            hasUnsavedChanges = window.hasUnsavedChanges,
            clipCount = control != null ? control.Clips.Count : 0,
            playbackEngine = "shared:TimelinePlayback/TimelinePlayer"
        });
    }

    public McpHostResult Load(string pathOrTimelineId)
    {
        if (string.IsNullOrWhiteSpace(pathOrTimelineId))
        {
            return McpHostResult.Fail("pathOrTimelineId 不能为空。");
        }

        EnsureWindow();
        var window = FindWindow();
        if (window == null)
        {
            return McpHostResult.Fail("无法打开技能编辑器窗口。");
        }

        if (!window.TryLoadTimelineForMcp(pathOrTimelineId.Trim(), out var error))
        {
            return McpHostResult.Fail(error ?? "加载失败。");
        }

        return McpHostResult.Success(new
        {
            loaded = true,
            timelineId = window.TimelineControl?.Config?.TimelineId,
            skillPath = window.CurrentTimelineFilePath,
            clipCount = window.TimelineControl != null ? window.TimelineControl.Clips.Count : 0
        });
    }

    public McpHostResult SetPreview(string casterTeam, string quality)
    {
        var window = RequireWindow(out var fail);
        if (window == null)
        {
            return fail;
        }

        var control = window.TimelineControl;
        if (control == null)
        {
            return McpHostResult.Fail("时间轴未初始化。");
        }

        if (!string.IsNullOrWhiteSpace(casterTeam))
        {
            if (!TryParseTeam(casterTeam, out var team))
            {
                return McpHostResult.Fail("casterTeam 须为 TeamA 或 TeamB。");
            }

            control.SetPreviewCasterTeam(team);
        }

        if (!string.IsNullOrWhiteSpace(quality))
        {
            if (!TryParseQuality(quality, out var q))
            {
                return McpHostResult.Fail("quality 须为 Low 或 HQ。");
            }

            control.SetPreviewQuality(q);
        }

        return Status();
    }

    public McpHostResult Play(bool fromStart)
    {
        var window = RequireWindow(out var fail);
        if (window == null)
        {
            return fail;
        }

        if (fromStart)
        {
            window.StopPreView();
            window.SeekRunningTimeImmediate(0);
        }

        window.PlayPreview();
        return McpHostResult.Success(new { playing = true, runningTime = window.RunningTime });
    }

    public McpHostResult Pause()
    {
        var window = RequireWindow(out var fail);
        if (window == null)
        {
            return fail;
        }

        window.PausePreView();
        return McpHostResult.Success(new { playing = false, runningTime = window.RunningTime });
    }

    public McpHostResult Stop()
    {
        var window = RequireWindow(out var fail);
        if (window == null)
        {
            return fail;
        }

        object recordPayload = null;
        if (window.IsRecording)
        {
            var json = window.FinishRecording("mcpStop");
            recordPayload = BuildRecordResponse(json);
        }

        window.StopPreView();
        return McpHostResult.Success(new
        {
            stopped = true,
            runningTime = window.RunningTime,
            record = recordPayload
        });
    }

    private int seekGeneration;

    public void Seek(float time, string clipKey, Action<McpHostResult> onDone)
    {
        var window = RequireWindow(out var fail);
        if (window == null)
        {
            onDone?.Invoke(fail);
            return;
        }

        var target = time;
        string usedClipKey = null;
        if (!string.IsNullOrWhiteSpace(clipKey))
        {
            if (!TryResolveClipContentStart(window, clipKey.Trim(), out target, out var resolveError))
            {
                onDone?.Invoke(McpHostResult.Fail(resolveError));
                return;
            }

            usedClipKey = clipKey.Trim();
        }

        seekGeneration++;
        var gen = seekGeneration;
        var startedAt = EditorApplication.timeSinceStartup;
        var settledFrames = 0;
        window.SeekPlayheadLikeRulerClick(target);

        void Tick()
        {
            if (gen != seekGeneration)
            {
                EditorApplication.update -= Tick;
                return;
            }

            if (EditorApplication.timeSinceStartup - startedAt > 30)
            {
                EditorApplication.update -= Tick;
                onDone?.Invoke(McpHostResult.Fail("seek 滑动超时（30s）。"));
                return;
            }

            if (window.IsPlayheadLerping)
            {
                settledFrames = 0;
                return;
            }

            settledFrames++;
            if (settledFrames < 2)
            {
                return;
            }

            EditorApplication.update -= Tick;
            onDone?.Invoke(McpHostResult.Success(new
            {
                runningTime = window.RunningTime,
                clipKey = usedClipKey,
                message = "已停播并将播放头滑到目标时间。"
            }));
        }

        EditorApplication.update += Tick;
    }

    private static bool TryResolveClipContentStart(
        EditorTimeLine window,
        string clipKey,
        out float contentStart,
        out string error)
    {
        contentStart = 0f;
        error = null;
        var clips = window.TimelineControl?.Clips;
        if (clips == null)
        {
            error = "时间轴未加载。";
            return false;
        }

        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            if (clip?.Config == null || clip.Config.ClipKey != clipKey)
            {
                continue;
            }

            contentStart = clip.GetContentTimelineStartTime();
            return true;
        }

        error = "找不到 Clip: " + clipKey;
        return false;
    }

    public McpHostResult ListClips(int offset, int limit)
    {
        var window = RequireWindow(out var fail);
        if (window == null)
        {
            return fail;
        }

        if (offset < 0)
        {
            offset = 0;
        }

        if (limit <= 0)
        {
            limit = 50;
        }

        if (limit > 200)
        {
            limit = 200;
        }

        var clips = window.TimelineControl?.Clips;
        var total = clips != null ? clips.Count : 0;
        var items = new List<object>();
        var end = Math.Min(total, offset + limit);
        for (var i = offset; i < end; i++)
        {
            var cfg = clips[i]?.Config;
            if (cfg == null)
            {
                continue;
            }

            items.Add(new
            {
                clipKey = cfg.ClipKey,
                type = (int)cfg.Type,
                typeName = cfg.Type.ToString(),
                label = UnitFeatureTypeDisplay.GetClipLabelName(cfg),
                start = cfg.StartTime,
                delay = cfg.DelayTime,
                duration = cfg.Duration,
                preKey = cfg.PreKey
            });
        }

        return McpHostResult.Success(new
        {
            total,
            returned = items.Count,
            offset,
            nextOffset = end < total ? end : (int?)null,
            clips = items
        });
    }

    public McpHostResult GetClip(string clipKey)
    {
        if (string.IsNullOrWhiteSpace(clipKey))
        {
            return McpHostResult.Fail("clipKey 不能为空。");
        }

        var window = RequireWindow(out var fail);
        if (window == null)
        {
            return fail;
        }

        var clips = window.TimelineControl?.Clips;
        if (clips == null)
        {
            return McpHostResult.Fail("时间轴未加载。");
        }

        for (var i = 0; i < clips.Count; i++)
        {
            var cfg = clips[i]?.Config;
            if (cfg == null || cfg.ClipKey != clipKey)
            {
                continue;
            }

            object shotExtra = null;
            if (cfg is TimelineClipCameraShotConfig shot)
            {
                shotExtra = new
                {
                    shotType = shot.ShotType.ToString(),
                    use2DComposition = shot.Use2DComposition,
                    planarTravel = shot.PlanarTravel,
                    mirrorByTeam = shot.MirrorByTeam,
                    lookAtType = shot.LookAtType.ToString(),
                    lookPivot = DescribePivot(TimelineCameraPivotPlayback.Data(shot, false)),
                    fromPivot = TimelineCameraPivotPlayback.UsesFrom(shot)
                        ? DescribePivot(TimelineCameraPivotPlayback.Data(shot, true)) : null,
                    fromCurrentPose = shot.FromCurrentPose,
                    pivotOffset = new[] { shot.PivotOffset.x, shot.PivotOffset.y, shot.PivotOffset.z },
                    distance = shot.Distance,
                    height = shot.Height,
                    moveAmount = shot.MoveAmount,
                    boomAmount = shot.BoomAmount,
                    orbitAngle = shot.OrbitAngle,
                    orbitStartYaw = shot.OrbitStartYaw
                };
            }

            return McpHostResult.Success(new
            {
                clipKey = cfg.ClipKey,
                type = (int)cfg.Type,
                typeName = cfg.Type.ToString(),
                label = UnitFeatureTypeDisplay.GetClipLabelName(cfg),
                start = cfg.StartTime,
                delay = cfg.DelayTime,
                duration = cfg.Duration,
                preKey = cfg.PreKey,
                cameraShot = shotExtra,
                configJson = TimelineJsonUtil.ToJson(cfg, true)
            });
        }

        return McpHostResult.Fail("未找到 clipKey: " + clipKey);
    }

    public McpHostResult Snapshot()
    {
        var window = RequireWindow(out var fail);
        if (window == null)
        {
            return fail;
        }

        if (window.TimelineControl == null)
        {
            return McpHostResult.Fail("时间轴未加载。");
        }

        return McpHostResult.Success(TimelinePreviewRecorder.BuildSnapshot(window.TimelineControl));
    }

    public void RecordPlay(
        string casterTeam,
        string quality,
        float sampleHz,
        bool untilCutoff,
        Action<McpHostResult> onDone)
    {
        var window = RequireWindow(out var fail);
        if (window == null)
        {
            onDone?.Invoke(fail);
            return;
        }

        if (!string.IsNullOrWhiteSpace(casterTeam) || !string.IsNullOrWhiteSpace(quality))
        {
            var set = SetPreview(casterTeam, quality);
            if (!set.Ok)
            {
                onDone?.Invoke(set);
                return;
            }
        }

        window.StartRecordingPlay(
            sampleHz > 0.1f ? sampleHz : TimelinePreviewRecorder.DefaultSampleHz,
            untilCutoff,
            (json, _) =>
            {
                onDone?.Invoke(McpHostResult.Success(BuildRecordResponse(json)));
            });
    }

    private static object BuildRecordResponse(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return new { empty = true };
        }

        EditorGUIUtility.systemCopyBuffer = json;

        if (json.Length <= MaxInlineJsonChars)
        {
            JToken parsed = null;
            try
            {
                parsed = JToken.Parse(json);
            }
            catch (Exception)
            {
                parsed = null;
            }

            return new
            {
                copiedToClipboard = true,
                charCount = json.Length,
                record = parsed,
                recordJson = parsed == null ? json : null
            };
        }

        var path = Path.Combine(Path.GetTempPath(), "kabu-skill-preview-record-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
        File.WriteAllText(path, json);
        return new
        {
            copiedToClipboard = true,
            charCount = json.Length,
            recordFilePath = path,
            message = "录制 JSON 过大，已写入临时文件，请用 Read 读取。"
        };
    }

    private static EditorTimeLine FindWindow()
    {
        var windows = Resources.FindObjectsOfTypeAll<EditorTimeLine>();
        return windows != null && windows.Length > 0 ? windows[0] : null;
    }

    private static void EnsureWindow()
    {
        if (FindWindow() == null)
        {
            EditorTimeLine.DoWindow();
        }
    }

    private static EditorTimeLine RequireWindow(out McpHostResult fail)
    {
        EnsureWindow();
        var window = FindWindow();
        if (window == null)
        {
            fail = McpHostResult.Fail("技能编辑器窗口不可用。");
            return null;
        }

        fail = default;
        return window;
    }

    private static bool TryParseTeam(string raw, out UnitTeamType team)
    {
        team = UnitTeamType.TeamA;
        if (string.Equals(raw, "TeamA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "A", StringComparison.OrdinalIgnoreCase))
        {
            team = UnitTeamType.TeamA;
            return true;
        }

        if (string.Equals(raw, "TeamB", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "B", StringComparison.OrdinalIgnoreCase))
        {
            team = UnitTeamType.TeamB;
            return true;
        }

        return false;
    }

    private static bool TryParseQuality(string raw, out TimelinePreviewQualityType quality)
    {
        quality = TimelinePreviewQualityType.Low;
        if (string.Equals(raw, "Low", StringComparison.OrdinalIgnoreCase))
        {
            quality = TimelinePreviewQualityType.Low;
            return true;
        }

        if (string.Equals(raw, "HQ", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "High", StringComparison.OrdinalIgnoreCase))
        {
            quality = TimelinePreviewQualityType.HQ;
            return true;
        }

        return false;
    }
    private static object DescribePivot(TimelineCameraPivotData pivot) => new
    {
        subject = pivot.Subject.ToString(),
        bindingKey = pivot.BindingKey,
        selection = pivot.Selection.ToString(),
        nodeType = pivot.NodeType.ToString(),
        includeCaster = pivot.IncludeCaster,
        offset = new[] { pivot.Offset.x, pivot.Offset.y, pivot.Offset.z },
        lockAtEntry = pivot.LockAtEntry
    };
}
