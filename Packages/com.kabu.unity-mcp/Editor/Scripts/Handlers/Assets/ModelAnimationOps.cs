using System;
using KabuUnityMcp.Editor.Handlers;
using UnityEditor;

namespace KabuUnityMcp.Editor.Handlers.Assets
{
    /// <summary>
    /// FBX / 模型动画片段导入设置。对应 Inspector → Animation 里 Loop Time 及其下方的根运动参数。
    /// </summary>
    internal static class ModelAnimationOps
    {
        public static HandlerResult ListClips(string assetPath, int offset, int limit)
        {
            if (limit <= 0)
            {
                return HandlerResult.Fail("limit 必须大于 0。", -32602);
            }

            if (offset < 0)
            {
                return HandlerResult.Fail("offset 不能为负。", -32602);
            }

            var importer = LoadImporter(assetPath, out var path, out var error);
            if (importer == null)
            {
                return HandlerResult.Fail(error, -32602);
            }

            var usingDefaults = IsUsingDefaults(importer);
            var clips = GetClips(importer, usingDefaults);
            var total = clips.Length;
            var end = Math.Min(total, offset + limit);
            var page = new object[Math.Max(0, end - offset)];
            for (var i = offset; i < end; i++)
            {
                var clip = clips[i];
                page[i - offset] = new
                {
                    name = clip.name,
                    takeName = clip.takeName,
                    firstFrame = clip.firstFrame,
                    lastFrame = clip.lastFrame,
                    loopTime = clip.loopTime,
                    loopPose = clip.loopPose
                };
            }

            var next = end < total ? end : (int?)null;
            return HandlerResult.Success(new
            {
                path,
                animationType = importer.animationType.ToString(),
                animationCompression = importer.animationCompression.ToString(),
                usingDefaults,
                total,
                returned = page.Length,
                offset,
                nextOffset = next,
                clips = page
            });
        }

        public static HandlerResult GetClip(string assetPath, string clipName, string takeName)
        {
            var importer = LoadImporter(assetPath, out var path, out var error);
            if (importer == null)
            {
                return HandlerResult.Fail(error, -32602);
            }

            var usingDefaults = IsUsingDefaults(importer);
            var clips = GetClips(importer, usingDefaults);
            if (!TryFind(clips, clipName, takeName, out var index, out var findError))
            {
                return HandlerResult.Fail(findError, -32602);
            }

            return HandlerResult.Success(new
            {
                path,
                animationType = importer.animationType.ToString(),
                animationCompression = importer.animationCompression.ToString(),
                usingDefaults,
                clip = ToDetail(clips[index])
            });
        }

        public static HandlerResult SetClip(string assetPath, string clipName, string takeName, ModelClipPatch patch)
        {
            if (patch == null || !patch.HasAny)
            {
                return HandlerResult.Fail("没有要修改的字段。", -32602);
            }

            var importer = LoadImporter(assetPath, out var path, out var error);
            if (importer == null)
            {
                return HandlerResult.Fail(error, -32602);
            }

            var usingDefaults = IsUsingDefaults(importer);
            var clips = GetClips(importer, usingDefaults);
            if (!TryFind(clips, clipName, takeName, out var index, out var findError))
            {
                return HandlerResult.Fail(findError, -32602);
            }

            var applyError = patch.Apply(clips[index]);
            if (applyError != null)
            {
                return HandlerResult.Fail(applyError, -32602);
            }

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            return HandlerResult.Success(new
            {
                path,
                materializedDefaults = usingDefaults,
                reimported = true,
                clip = ToDetail(clips[index]),
                message = usingDefaults
                    ? "已把默认片段写入导入设置并重新导入。Animator 引用的是片段 internalID，若控制器丢失动作，检查片段名是否仍能对上。"
                    : "已更新片段导入设置并重新导入。"
            });
        }

        internal static string ParseRotationBasedUpon(string value, out bool bodyOrientation)
        {
            bodyOrientation = false;
            if (string.IsNullOrWhiteSpace(value))
            {
                return "rootRotationBasedUpon 不能为空。";
            }

            var key = NormalizeKey(value);
            if (key == "original")
            {
                return null;
            }

            if (key == "bodyorientation" || key == "body")
            {
                bodyOrientation = true;
                return null;
            }

            return "rootRotationBasedUpon 只能是 Original 或 BodyOrientation。";
        }

        internal static string ParsePositionYBasedUpon(string value, out bool centerOfMass, out bool feet)
        {
            centerOfMass = false;
            feet = false;
            if (string.IsNullOrWhiteSpace(value))
            {
                return "rootPositionYBasedUpon 不能为空。";
            }

            var key = NormalizeKey(value);
            if (key == "original")
            {
                return null;
            }

            if (key == "centerofmass" || key == "com")
            {
                centerOfMass = true;
                return null;
            }

            if (key == "feet")
            {
                feet = true;
                return null;
            }

            return "rootPositionYBasedUpon 只能是 Original、CenterOfMass 或 Feet。";
        }

        internal static string ParsePositionXZBasedUpon(string value, out bool centerOfMass)
        {
            centerOfMass = false;
            if (string.IsNullOrWhiteSpace(value))
            {
                return "rootPositionXZBasedUpon 不能为空。";
            }

            var key = NormalizeKey(value);
            if (key == "original")
            {
                return null;
            }

            if (key == "centerofmass" || key == "com")
            {
                centerOfMass = true;
                return null;
            }

            return "rootPositionXZBasedUpon 只能是 Original 或 CenterOfMass。";
        }

        private static ModelImporter LoadImporter(string assetPath, out string path, out string error)
        {
            path = AssetOps.NormalizeAssetPath(assetPath);
            error = null;
            if (string.IsNullOrEmpty(path))
            {
                error = "请提供模型路径，如 Assets/Art/Roco/Character/PC1/SKM_PC1.fbx。";
                return null;
            }

            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                error = $"不是模型资源或路径不存在：{path}";
                return null;
            }

            return importer;
        }

        private static bool IsUsingDefaults(ModelImporter importer)
        {
            var explicitClips = importer.clipAnimations;
            return explicitClips == null || explicitClips.Length == 0;
        }

        private static ModelImporterClipAnimation[] GetClips(ModelImporter importer, bool usingDefaults)
        {
            if (usingDefaults)
            {
                return importer.defaultClipAnimations ?? Array.Empty<ModelImporterClipAnimation>();
            }

            return importer.clipAnimations ?? Array.Empty<ModelImporterClipAnimation>();
        }

        private static bool TryFind(
            ModelImporterClipAnimation[] clips,
            string clipName,
            string takeName,
            out int index,
            out string error)
        {
            index = -1;
            error = null;
            if (string.IsNullOrWhiteSpace(clipName))
            {
                error = "请提供 clipName。";
                return false;
            }

            clipName = clipName.Trim();
            takeName = string.IsNullOrWhiteSpace(takeName) ? null : takeName.Trim();
            var match = -1;
            for (var i = 0; i < clips.Length; i++)
            {
                if (!string.Equals(clips[i].name, clipName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (takeName != null
                    && !string.Equals(clips[i].takeName, takeName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (match >= 0)
                {
                    error = $"片段名重复：{clipName}。请再传 takeName。";
                    return false;
                }

                match = i;
            }

            if (match < 0)
            {
                error = takeName == null
                    ? $"找不到片段：{clipName}"
                    : $"找不到片段：{clipName}（take {takeName}）";
                return false;
            }

            index = match;
            return true;
        }

        private static object ToDetail(ModelImporterClipAnimation clip)
        {
            return new
            {
                name = clip.name,
                takeName = clip.takeName,
                firstFrame = clip.firstFrame,
                lastFrame = clip.lastFrame,
                loopTime = clip.loopTime,
                loopPose = clip.loopPose,
                cycleOffset = clip.cycleOffset,
                rootRotationBakeIntoPose = clip.lockRootRotation,
                rootRotationBasedUpon = clip.keepOriginalOrientation ? "Original" : "BodyOrientation",
                rootRotationOffset = clip.rotationOffset,
                rootPositionYBakeIntoPose = clip.lockRootHeightY,
                rootPositionYBasedUpon = PositionYBasedUpon(clip),
                rootPositionYOffset = clip.heightOffset,
                rootPositionXZBakeIntoPose = clip.lockRootPositionXZ,
                rootPositionXZBasedUpon = clip.keepOriginalPositionXZ ? "Original" : "CenterOfMass",
                mirror = clip.mirror
            };
        }

        private static string PositionYBasedUpon(ModelImporterClipAnimation clip)
        {
            if (clip.heightFromFeet)
            {
                return "Feet";
            }

            if (clip.keepOriginalPositionY)
            {
                return "Original";
            }

            return "CenterOfMass";
        }

        private static string NormalizeKey(string value)
        {
            return value.Trim().Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
        }
    }

    /// <summary>只填需要改的字段。null 表示不动。</summary>
    internal sealed class ModelClipPatch
    {
        public bool? LoopTime;
        public bool? LoopPose;
        public float? CycleOffset;
        public bool? RootRotationBakeIntoPose;
        public string RootRotationBasedUpon;
        public float? RootRotationOffset;
        public bool? RootPositionYBakeIntoPose;
        public string RootPositionYBasedUpon;
        public float? RootPositionYOffset;
        public bool? RootPositionXZBakeIntoPose;
        public string RootPositionXZBasedUpon;
        public bool? Mirror;
        public float? FirstFrame;
        public float? LastFrame;

        public bool HasAny =>
            LoopTime.HasValue
            || LoopPose.HasValue
            || CycleOffset.HasValue
            || RootRotationBakeIntoPose.HasValue
            || RootRotationBasedUpon != null
            || RootRotationOffset.HasValue
            || RootPositionYBakeIntoPose.HasValue
            || RootPositionYBasedUpon != null
            || RootPositionYOffset.HasValue
            || RootPositionXZBakeIntoPose.HasValue
            || RootPositionXZBasedUpon != null
            || Mirror.HasValue
            || FirstFrame.HasValue
            || LastFrame.HasValue;

        public string Apply(ModelImporterClipAnimation clip)
        {
            if (LoopTime.HasValue)
            {
                clip.loopTime = LoopTime.Value;
            }

            if (LoopPose.HasValue)
            {
                clip.loopPose = LoopPose.Value;
            }

            if (CycleOffset.HasValue)
            {
                clip.cycleOffset = CycleOffset.Value;
            }

            if (RootRotationBakeIntoPose.HasValue)
            {
                clip.lockRootRotation = RootRotationBakeIntoPose.Value;
            }

            if (RootRotationBasedUpon != null)
            {
                var error = ModelAnimationOps.ParseRotationBasedUpon(RootRotationBasedUpon, out var body);
                if (error != null)
                {
                    return error;
                }

                clip.keepOriginalOrientation = !body;
            }

            if (RootRotationOffset.HasValue)
            {
                clip.rotationOffset = RootRotationOffset.Value;
            }

            if (RootPositionYBakeIntoPose.HasValue)
            {
                clip.lockRootHeightY = RootPositionYBakeIntoPose.Value;
            }

            if (RootPositionYBasedUpon != null)
            {
                var error = ModelAnimationOps.ParsePositionYBasedUpon(
                    RootPositionYBasedUpon,
                    out var centerOfMass,
                    out var feet);
                if (error != null)
                {
                    return error;
                }

                clip.heightFromFeet = feet;
                clip.keepOriginalPositionY = !centerOfMass && !feet;
            }

            if (RootPositionYOffset.HasValue)
            {
                clip.heightOffset = RootPositionYOffset.Value;
            }

            if (RootPositionXZBakeIntoPose.HasValue)
            {
                clip.lockRootPositionXZ = RootPositionXZBakeIntoPose.Value;
            }

            if (RootPositionXZBasedUpon != null)
            {
                var error = ModelAnimationOps.ParsePositionXZBasedUpon(RootPositionXZBasedUpon, out var centerOfMass);
                if (error != null)
                {
                    return error;
                }

                clip.keepOriginalPositionXZ = !centerOfMass;
            }

            if (Mirror.HasValue)
            {
                clip.mirror = Mirror.Value;
            }

            if (FirstFrame.HasValue)
            {
                clip.firstFrame = FirstFrame.Value;
            }

            if (LastFrame.HasValue)
            {
                clip.lastFrame = LastFrame.Value;
            }

            if (clip.lastFrame < clip.firstFrame)
            {
                return "lastFrame 不能小于 firstFrame。";
            }

            return null;
        }
    }
}
