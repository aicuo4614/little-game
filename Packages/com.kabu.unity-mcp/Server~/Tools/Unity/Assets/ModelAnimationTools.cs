using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using KabuUnityMcp.Server.Services;

namespace KabuUnityMcp.Server.Tools.Unity.Assets;

[McpServerToolType]
public class ModelListAnimationClipsTool(ILogger<ModelListAnimationClipsTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_model_list_animation_clips")]
    [Description(
        "分页列出模型（FBX 等）Animation 页的片段。只读，不重新导入。" +
        "返回 animationType（Generic/Humanoid）、是否仍用默认片段 usingDefaults，以及每条的 loopTime / loopPose。" +
        "usingDefaults=true 表示还没在导入设置里单独保存过片段，Loop Time 等是 Unity 默认值。")]
    [return: Description("片段目录 JSON")]
    public async Task<string> UnityModelListAnimationClipsAsync(
        [Description("模型资源路径，如 Assets/Art/Roco/Character/PC1/SKM_PC1.fbx")] string path,
        [Description("跳过条数，默认 0")] int offset = 0,
        [Description("每页条数，默认 50")] int limit = 50)
    {
        logger.LogInformation("List model clips {Path}", path);
        return await SendAsync("unity.modelListAnimationClips", new { path, offset, limit }, "列出模型动画失败");
    }

    private async Task<string> SendAsync(string method, object payload, string failPrefix)
    {
        try
        {
            var result = await hub.SendRequestAsync(method, payload);
            if (result == null)
            {
                return "Unity 未返回数据。";
            }

            return JsonSerializer.Serialize(result.Value, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"{failPrefix}：{ex.Message}";
        }
    }
}

[McpServerToolType]
public class ModelGetAnimationClipTool(ILogger<ModelGetAnimationClipTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_model_get_animation_clip")]
    [Description(
        "读取一条模型动画片段的导入设置，只读。" +
        "字段对应 Inspector → Animation：Loop Time、Loop Pose、Cycle Offset，" +
        "以及 Root Transform Rotation / Position (Y) / Position (XZ) 的 Bake Into Pose、Based Upon、Offset。" +
        "不返回 Mask、Events、Curves。")]
    [return: Description("片段导入设置 JSON")]
    public async Task<string> UnityModelGetAnimationClipAsync(
        [Description("模型资源路径")] string path,
        [Description("片段名，如 SK_PC1|Root_World_Run_1")] string clipName,
        [Description("同名片段时再传 take 名")] string? takeName = null)
    {
        logger.LogInformation("Get model clip {Clip}", clipName);
        try
        {
            var result = await hub.SendRequestAsync("unity.modelGetAnimationClip", new
            {
                path,
                clipName,
                takeName = string.IsNullOrWhiteSpace(takeName) ? null : takeName
            });
            if (result == null)
            {
                return "Unity 未返回数据。";
            }

            return JsonSerializer.Serialize(result.Value, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"读取模型动画失败：{ex.Message}";
        }
    }
}

[McpServerToolType]
public class ModelSetAnimationClipTool(ILogger<ModelSetAnimationClipTool> logger, UnityWebSocketHub hub)
{
    [McpServerTool(Name = "unity_model_set_animation_clip")]
    [Description(
        "修改一条模型动画片段的导入设置并重新导入该模型。只改传入的字段。" +
        "loopTime=Loop Time；loopPose=Loop Pose（不要为了循环去开它，跑步摇头常与它有关）；" +
        "cycleOffset=Cycle Offset。" +
        "rootRotationBakeIntoPose / rootPositionYBakeIntoPose / rootPositionXZBakeIntoPose = 对应 Bake Into Pose。" +
        "rootRotationBasedUpon=Original|BodyOrientation；" +
        "rootPositionYBasedUpon=Original|CenterOfMass|Feet；" +
        "rootPositionXZBasedUpon=Original|CenterOfMass。" +
        "rootRotationOffset、rootPositionYOffset 是角度/高度偏移。mirror、firstFrame、lastFrame 可选。" +
        "若片段还在默认状态，会先写入 clipAnimations 再改。会触发模型重新导入。")]
    [return: Description("更新后的片段设置 JSON")]
    public async Task<string> UnityModelSetAnimationClipAsync(
        [Description("模型资源路径")] string path,
        [Description("片段名")] string clipName,
        [Description("Loop Time")] bool? loopTime = null,
        [Description("Loop Pose。循环播放用 loopTime，不要顺手打开此项")] bool? loopPose = null,
        [Description("Cycle Offset，0–1")] float? cycleOffset = null,
        [Description("Root Transform Rotation → Bake Into Pose")] bool? rootRotationBakeIntoPose = null,
        [Description("Original 或 BodyOrientation")] string? rootRotationBasedUpon = null,
        [Description("Root Transform Rotation → Offset（度）")] float? rootRotationOffset = null,
        [Description("Root Transform Position (Y) → Bake Into Pose")] bool? rootPositionYBakeIntoPose = null,
        [Description("Original、CenterOfMass 或 Feet")] string? rootPositionYBasedUpon = null,
        [Description("Root Transform Position (Y) → Offset")] float? rootPositionYOffset = null,
        [Description("Root Transform Position (XZ) → Bake Into Pose")] bool? rootPositionXZBakeIntoPose = null,
        [Description("Original 或 CenterOfMass")] string? rootPositionXZBasedUpon = null,
        [Description("Mirror")] bool? mirror = null,
        [Description("起始帧")] float? firstFrame = null,
        [Description("结束帧")] float? lastFrame = null,
        [Description("同名片段时再传 take 名")] string? takeName = null)
    {
        logger.LogInformation("Set model clip {Clip} loopTime={Loop}", clipName, loopTime);
        try
        {
            var result = await hub.SendRequestAsync("unity.modelSetAnimationClip", new
            {
                path,
                clipName,
                takeName = string.IsNullOrWhiteSpace(takeName) ? null : takeName,
                loopTime,
                loopPose,
                cycleOffset,
                rootRotationBakeIntoPose,
                rootRotationBasedUpon,
                rootRotationOffset,
                rootPositionYBakeIntoPose,
                rootPositionYBasedUpon,
                rootPositionYOffset,
                rootPositionXZBakeIntoPose,
                rootPositionXZBasedUpon,
                mirror,
                firstFrame,
                lastFrame
            });
            if (result == null)
            {
                return "Unity 未返回数据。";
            }

            return JsonSerializer.Serialize(result.Value, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"修改模型动画失败：{ex.Message}";
        }
    }
}
