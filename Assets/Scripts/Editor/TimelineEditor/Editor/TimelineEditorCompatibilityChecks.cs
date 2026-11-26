using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;

/// <summary>技能编排兼容报告与目录限制的回归检查；不创建场景或修改技能源文件。</summary>
public static class TimelineEditorCompatibilityChecks
{
    /// <summary>验证预览类型标记、多命中限制及资源检查与配置检查独立返回。</summary>
    [MenuItem("Kabu/Tests/Skill Editor Compatibility")]
    public static void Run()
    {
        var factory = new TimelineControl();
        var legacy = JObject.FromObject(TimelineEditorMcpCatalog.Describe(factory, TimelineClipType.Force));
        Require(!(bool)legacy["battle"] && ((string)legacy["menu"]).Contains(TimelineBindingEditorText.PreviewOnlySuffix), "legacy menu and battle support");
        Require(!string.IsNullOrEmpty((string)legacy["restrictions"]), "legacy replacement guidance");
        var stop = JObject.FromObject(TimelineEditorMcpCatalog.Describe(factory, TimelineClipType.HitStop));
        Require((string)stop["restrictions"] == TimelineBindingEditorText.HitStopOverlapHelp, "hitstop catalog semantics");
        var config = new TimelineConfig { TimelineId = "compatibility_check" };
        config.Clips.Add(new TimelineClipPlayHitConfig { ClipKey = "hit_a", Duration = 0.1f });
        var single = JObject.FromObject(TimelineEditorMcpValidation.Battle(config));
        Require((bool)single["valid"] && !(bool)single["runtimeVerified"], "single hit configuration is not runtime verification");
        config.Clips.Add(new TimelineClipPlayHitConfig { ClipKey = "hit_b", StartTime = 0.2f, Duration = 0.1f });
        var multiple = JObject.FromObject(TimelineEditorMcpValidation.Battle(config));
        Require(!(bool)multiple["valid"] && ((string)multiple["error"]).Contains("PlayHit"), "multiple hit rejection");
        config.Clips.Clear();
        var effect = factory.CreateEmptyClip(TimelineClipType.PlayEffect).Config;
        effect.ClipKey = "missing_effect";
        ((TimelineClipPlayEffectConfig)effect).EffectPath = "__missing_compatibility_check__";
        config.Clips.Add(effect);
        var missing = JObject.FromObject(TimelineEditorMcpValidation.Battle(config));
        Require((bool)missing["valid"] && !(bool)missing["resources"]["valid"], "missing asset is separate from configuration compatibility");
        Directory.CreateDirectory("Temp");
        File.WriteAllText("Temp/skill-editor-compatibility-checks.txt", "PASS: legacy guidance, hitstop semantics, single/multiple hits, missing resources, runtime verification boundary");
    }

    private static void Require(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Skill editor compatibility check failed: " + description);
        }
    }
}
