using System;
using System.IO;
using System.Linq;
using KabuUnityMcp.Editor.Extensions;
using LittleGame.Client.SkillTimeline;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace LittleGame.Client.Editor
{
    /// <summary>技能时间轴 JSON 与单位绑定的专用 MCP 扩展，供编辑器与智能体直接编排、校验和热重载。</summary>
    [InitializeOnLoad]
    public sealed class SkillTimelineMcp : IMcpExtension
    {
        private static readonly SkillTimelineMcp instance = new SkillTimelineMcp();
        static SkillTimelineMcp()
        {
            McpExtensionRegistry.Register(instance);
            AssemblyReloadEvents.beforeAssemblyReload += () => McpExtensionRegistry.Unregister(instance);
        }

        public string Id => "little-game.skill-timeline";
        public string Version => "2.0.0";
        public string Description => "技能规则时序、二维演出预览、单位绑定、运行实例与热重载。";
        public string UnavailableReason => null;

        private static JObject Schema(string properties = "", string required = "") =>
            JObject.Parse("{'type':'object','properties':{" + properties + "},'required':[" + required + "],'additionalProperties':false}");

        public McpExtensionOperation[] Operations => new[]
        {
            new McpExtensionOperation("configure_preview", "设置二维预览距离、角度、移动速度和目标数量；仅演出预览，不替代正常战斗验收", false, Schema("'timelineId':{'type':'string'},'distance':{'type':'number','minimum':0.6,'maximum':7},'angle':{'type':'number'},'targetSpeed':{'type':'number','minimum':-2,'maximum':2},'targetCount':{'type':'integer','minimum':1,'maximum':5},'time':{'type':'number','minimum':0}", "'timelineId'")),
            new McpExtensionOperation("runtime_instances", "读取真实技能实例阶段、锁定目标、释放/命中/结束时间和中断原因", true, Schema()),
            new McpExtensionOperation("list_timelines", "列出全部技能 JSON（含总时长、片段数）、单位绑定表和校验结果", true, Schema()),
            new McpExtensionOperation("get_timeline", "读取指定技能 JSON 及解析后的片段起止调度表", true, Schema("'timelineId':{'type':'string'}", "'timelineId'")),
            new McpExtensionOperation("save_timeline", "校验并保存技能 JSON（非法时间/循环依赖会拒绝），并自动热重载到当前战斗", false, Schema("'timeline':{'type':'object'}", "'timeline'")),
            new McpExtensionOperation("get_bindings", "读取 UnitSkillBindings.json 全部单位技能挂钩规则", true, Schema()),
            new McpExtensionOperation("save_bindings", "校验并保存单位技能挂钩表（要求引用的 TimelineId 全部存在），并热重载", false, Schema("'bindings':{'type':'object'}", "'bindings'")),
            new McpExtensionOperation("validate_all", "一键校验全部技能 JSON 与单位绑定关系", true, Schema()),
            new McpExtensionOperation("preview_timeline", "按指定时间点试算技能时间轴（返回该时刻活跃片段），若在 Play 模式下可选在战场上实际触发一次演出", true, Schema("'timelineId':{'type':'string'},'time':{'type':'number','minimum':0},'triggerInPlay':{'type':'boolean'}", "'timelineId'"))
        };

        public McpHostResult Execute(string operation, JObject args)
        {
            var lib = new SkillTimelineLibrary();
            lib.Reload();

            switch (operation)
            {
                case "runtime_instances":
                    return ClientBootstrap.Current?.App != null ? McpHostResult.Success(new { instances = ClientBootstrap.Current.App.Session.SkillInstances.Select(a => new { id = a.Id, source = a.Source, target = a.Target, trigger = a.Trigger, timelineId = a.TimelineId, phase = a.Phase.ToString(), startedAt = a.StartedAt, launchAt = a.LaunchAt, impactAt = a.ImpactAt, endAt = a.EndAt, cancelReason = a.CancelReason }) }) : McpHostResult.Fail("请先进入 Combat Play");
                case "configure_preview":
                {
                    if (Application.isPlaying) return McpHostResult.Fail("二维独立预览需要先退出 Play");
                    string id = (string)args["timelineId"];
                    if (lib.Get(id) == null) return McpHostResult.Fail("技能不存在: " + id);
                    if (!Timeline.TimelinePreviewPlayback.OpenPreviewSceneIfNeeded()) return McpHostResult.Fail("无法打开预览场景");
                    var window = EditorWindow.GetWindow<Timeline.EditorTimeLine>();
                    window.ReloadLibrary(id);
                    return McpHostResult.Success(window.ConfigurePreview((float?)args["distance"] ?? 3.5f, (float?)args["angle"] ?? 90f, (float?)args["targetSpeed"] ?? 0f, (int?)args["targetCount"] ?? 1, (float?)args["time"] ?? 0f));
                }
                case "list_timelines":
                {
                    var report = lib.ValidateAll();
                    var items = lib.Timelines.Values.OrderBy(t => t.TimelineId, StringComparer.Ordinal).Select(t =>
                    {
                        float duration = 0f;
                        string error = null;
                        try { duration = new SkillTimelineSchedule(t).TotalDuration; }
                        catch (Exception ex) { error = ex.Message; }
                        return new
                        {
                            timelineId = t.TimelineId,
                            displayName = t.DisplayName,
                            clipCount = t.Clips?.Count ?? 0,
                            duration,
                            valid = error == null,
                            error
                        };
                    }).ToArray();
                    return McpHostResult.Success(new
                    {
                        valid = report.Valid,
                        errors = report.Errors,
                        warnings = report.Warnings,
                        timelines = items,
                        bindings = lib.Bindings.Bindings
                    });
                }

                case "get_timeline":
                {
                    string id = (string)args["timelineId"];
                    var cfg = lib.Get(id);
                    if (cfg == null) return McpHostResult.Fail("未找到技能时间轴: " + id);
                    var schedule = new SkillTimelineSchedule(cfg);
                    return McpHostResult.Success(new
                    {
                        timeline = cfg,
                        totalDuration = schedule.TotalDuration,
                        ruleTiming = schedule.BuildDefinition(cfg),
                        scheduled = schedule.OrderedClips.Select(c => new
                        {
                            clipKey = c.Config.ClipKey,
                            type = c.Config.Type.ToString(),
                            start = c.Start,
                            end = c.End,
                            duration = c.ResolvedDuration
                        }).ToArray()
                    });
                }

                case "save_timeline":
                {
                    var token = args["timeline"];
                    if (token == null) return McpHostResult.Fail("缺少 timeline 对象");
                    SkillTimelineConfig cfg;
                    try { cfg = SkillTimelineJson.Deserialize<SkillTimelineConfig>(token.ToString()); }
                    catch (Exception ex) { return McpHostResult.Fail("JSON 解析失败: " + ex.Message); }
                    if (cfg != null && (cfg.TimelineId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || cfg.TimelineId.Contains("..") || cfg.TimelineId.Contains("/") || cfg.TimelineId.Contains("\\"))) return McpHostResult.Fail("TimelineId 必须是安全的文件名");
                    if (cfg == null || string.IsNullOrWhiteSpace(cfg.TimelineId))
                        return McpHostResult.Fail("TimelineId 不能为空");

                    SkillTimelineSchedule schedule;
                    try { schedule = new SkillTimelineSchedule(cfg); }
                    catch (Exception ex) { return McpHostResult.Fail("时间轴校验失败: " + ex.Message); }

                    Directory.CreateDirectory(SkillTimelineLibrary.SkillsAssetFolder);
                    string path = Path.Combine(SkillTimelineLibrary.SkillsAssetFolder, cfg.TimelineId + ".json");
                    File.WriteAllText(path, SkillTimelineJson.Serialize(cfg) + "\n");
                    AssetDatabase.ImportAsset(path.Replace('\\', '/'));
                    ReloadLiveDirector();
                    return McpHostResult.Success(new
                    {
                        saved = true,
                        path = path.Replace('\\', '/'),
                        timelineId = cfg.TimelineId,
                        totalDuration = schedule.TotalDuration,
                        clipCount = cfg.Clips.Count,
                        reloadedLive = Application.isPlaying && ClientBootstrap.Current != null
                    });
                }

                case "get_bindings":
                    return McpHostResult.Success(lib.Bindings);

                case "save_bindings":
                {
                    var token = args["bindings"];
                    if (token == null) return McpHostResult.Fail("缺少 bindings 对象");
                    UnitSkillBindingsConfig cfg;
                    try { cfg = SkillTimelineJson.Deserialize<UnitSkillBindingsConfig>(token.ToString()); }
                    catch (Exception ex) { return McpHostResult.Fail("JSON 解析失败: " + ex.Message); }
                    if (cfg?.Bindings == null || cfg.Bindings.Count == 0)
                        return McpHostResult.Fail("Bindings 不能为空");

                    foreach (var b in cfg.Bindings)
                    {
                        if (b == null || string.IsNullOrWhiteSpace(b.UnitKind) || string.IsNullOrWhiteSpace(b.Trigger))
                            return McpHostResult.Fail("绑定条目缺少 UnitKind 或 Trigger");
                        if (string.IsNullOrWhiteSpace(b.TimelineId) || lib.Get(b.TimelineId) == null)
                            return McpHostResult.Fail("引用的 TimelineId 不存在: " + b?.TimelineId);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(SkillTimelineLibrary.BindingsAssetPath));
                    File.WriteAllText(SkillTimelineLibrary.BindingsAssetPath, SkillTimelineJson.Serialize(cfg) + "\n");
                    AssetDatabase.ImportAsset(SkillTimelineLibrary.BindingsAssetPath);
                    ReloadLiveDirector();
                    return McpHostResult.Success(new
                    {
                        saved = true,
                        bindingCount = cfg.Bindings.Count,
                        reloadedLive = Application.isPlaying && ClientBootstrap.Current != null
                    });
                }

                case "validate_all":
                    return McpHostResult.Success(lib.ValidateAll());

                case "preview_timeline":
                {
                    string id = (string)args["timelineId"];
                    float time = (float?)args["time"] ?? 0f;
                    bool triggerInPlay = (bool?)args["triggerInPlay"] ?? false;
                    var cfg = lib.Get(id);
                    if (cfg == null) return McpHostResult.Fail("未找到技能时间轴: " + id);

                    var schedule = new SkillTimelineSchedule(cfg);
                    var activeAtTime = schedule.OrderedClips
                        .Where(c => c.ResolvedDuration <= 0.0001f ? Math.Abs(time - c.Start) <= 0.01f : (time >= c.Start && time < c.End))
                        .Select(c => new
                        {
                            clipKey = c.Config.ClipKey,
                            type = c.Config.Type.ToString(),
                            localTime = Math.Max(0f, time - c.Start),
                            duration = c.ResolvedDuration
                        }).ToArray();

                    bool triggered = false;
                    if (triggerInPlay && Application.isPlaying && ClientBootstrap.Current?.View?.TimelineDirector != null)
                    {
                        var ctx = new SkillTriggerContext(0, 0, 1, "Preview", "Preview", "", 1, new Vector3(0f, 0.14f, 1.2f));
                        triggered = ClientBootstrap.Current.View.TimelineDirector.PlayTimeline(id, ctx);
                    }

                    return McpHostResult.Success(new
                    {
                        timelineId = id,
                        sampleTime = time,
                        totalDuration = schedule.TotalDuration,
                        activeClips = activeAtTime,
                        triggeredInPlay = triggered
                    });
                }

                default:
                    return McpHostResult.Fail("未知技能时间轴操作: " + operation);
            }
        }

        private static void ReloadLiveDirector()
        {
            if (Application.isPlaying && ClientBootstrap.Current?.View?.TimelineDirector != null)
                ClientBootstrap.Current.View.TimelineDirector.ReloadLibrary();
        }
    }
}
