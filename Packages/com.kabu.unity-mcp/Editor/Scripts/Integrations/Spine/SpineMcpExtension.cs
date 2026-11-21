using System;
using System.Linq;
using KabuUnityMcp.Editor.Extensions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using R = KabuUnityMcp.Editor.Integrations.OptionalIntegrationReflection;

namespace KabuUnityMcp.Editor.Integrations.Spine
{
    /// <summary>Spine inspection in the main package, without a compile-time Spine dependency.</summary>
    internal sealed class SpineMcpExtension : IMcpExtension
    {
        public string Id => "spine";
        public string Version => "1.0.0";
        public string Description => "Read Spine assets, animation tracks and sampled bone positions. Does not initialize, advance or modify skeletons.";
        public string UnavailableReason => R.FindType("Spine.Unity.SkeletonRenderer") == null
            && R.FindType("Spine.Unity.SkeletonGraphic") == null ? "Spine runtime is not loaded in this Editor." : null;
        public McpExtensionOperation[] Operations => new[]
        {
            new McpExtensionOperation("inspect", "Read initialized skeleton, skin, flip/scale, tracks and facing against an optional cameraInstanceId. Target the exact component GameObject.", true, InspectSchema()),
            new McpExtensionOperation("catalog", "Page animations, skins, slots, bones or event definitions from initialized skeleton data; assetPath may target a SkeletonDataAsset without a scene object.", true, R.TargetSchema("kind", "assetPath")),
            new McpExtensionOperation("bones", "Page current bone local/skeleton/world positions; optional boneName selects an exact bone. No transform-name fallback.", true, R.TargetSchema("boneName"))
        };

        [InitializeOnLoadMethod]
        private static void Register() => McpExtensionRegistry.Register(new SpineMcpExtension());

        private static JObject InspectSchema()
        {
            var schema = R.TargetSchema();
            schema["properties"]["cameraInstanceId"] = new JObject { ["type"] = "integer" };
            return schema;
        }

        public McpHostResult Execute(string operation, JObject args)
        {
            var limit = R.Limit(args);
            var offset = args["offset"]?.Value<int>() ?? 0;
            if (operation == "catalog" && !string.IsNullOrEmpty(args["assetPath"]?.Value<string>()))
            {
                var type = R.FindType("Spine.Unity.SkeletonDataAsset");
                var asset = type == null ? null : AssetDatabase.LoadAssetAtPath(args["assetPath"].Value<string>(), type);
                if (!asset)
                {
                    return McpHostResult.Fail("SkeletonDataAsset not found.", -32602);
                }
                // Loads asset data only, never initializes or changes a scene component.
                var data = type.GetMethod("GetSkeletonData", new[] { typeof(bool) })?.Invoke(asset, new object[] { true });
                return Catalog(data, args, offset, limit);
            }
            var go = R.Target(args);
            var components = go.GetComponents<Component>().Where(item => item && IsSkeleton(item.GetType())).ToArray();
            var index = args["componentIndex"]?.Value<int>() ?? 0;
            if (index < 0 || index >= components.Length)
            {
                return McpHostResult.Fail("No Spine component at componentIndex on this GameObject. Select the exact SkeletonAnimation/SkeletonGraphic object.", -32602);
            }
            var component = components[index];
            // SkeletonGraphic.Skeleton may initialize; read its backing field instead.
            var skeletonField = component.GetType().GetField("skeleton", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var skeleton = skeletonField?.GetValue(component);
            if (skeleton == null)
            {
                return McpHostResult.Fail("Skeleton is not initialized. Inspection does not initialize it; use catalog(assetPath) to inspect asset data.");
            }
            if (operation == "catalog")
            {
                return Catalog(R.Get(skeleton, "Data"), args, offset, limit);
            }
            if (operation == "bones")
            {
                var boneName = args["boneName"]?.Value<string>();
                var bones = R.Items(R.Get(skeleton, "Bones")).Where(bone => string.IsNullOrEmpty(boneName) || (string)R.Named(R.Get(bone, "Data")) == boneName).ToArray();
                if (!string.IsNullOrEmpty(boneName) && bones.Length == 0)
                {
                    return McpHostResult.Fail("Bone not found: " + boneName + ". No fallback was applied.", -32602);
                }
                return Page(bones.Select(bone => Bone(component.transform, bone)).ToArray(), offset, limit);
            }
            var state = R.Get(component, "AnimationState");
            var tracks = R.Items(R.Get(state, "Tracks")).Select((entry, track) => new
            {
                track, animation = R.Named(R.Get(entry, "Animation")), time = R.Get(entry, "TrackTime"),
                animationTime = R.Get(entry, "AnimationTime"), loop = R.Get(entry, "Loop"),
                timeScale = R.Get(entry, "TimeScale"), alpha = R.Get(entry, "Alpha"),
                mixTime = R.Get(entry, "MixTime"), mixDuration = R.Get(entry, "MixDuration"),
                mixingFrom = R.Named(R.Get(R.Get(entry, "MixingFrom"), "Animation")),
                nextAnimation = R.Named(R.Get(R.Get(entry, "Next"), "Animation"))
            }).ToArray();
            var cameraId = args["cameraInstanceId"]?.Value<int?>();
            Camera camera = null;
            if (cameraId.HasValue)
            {
                var target = EditorUtility.InstanceIDToObject(cameraId.Value);
                camera = target as Camera ?? (target as GameObject)?.GetComponent<Camera>();
                if (!camera)
                {
                    return McpHostResult.Fail("cameraInstanceId does not identify a Camera or camera GameObject.", -32602);
                }
            }
            return McpHostResult.Success(new
            {
                schema = "kabu.spine.inspect/v1", instanceId = go.GetInstanceID(), componentIndex = index,
                componentType = component.GetType().FullName, initialized = true, sampledFrame = Time.frameCount,
                timeScale = R.Get(component, "timeScale"), stateTimeScale = R.Get(state, "TimeScale"),
                skin = R.Named(R.Get(skeleton, "Skin")), defaultSkin = R.Named(R.Get(R.Get(skeleton, "Data"), "DefaultSkin")),
                scaleX = R.Get(skeleton, "ScaleX"), scaleY = R.Get(skeleton, "ScaleY"),
                flipX = R.Get(skeleton, "FlipX"), flipY = R.Get(skeleton, "FlipY"),
                enabled = (component as Behaviour)?.enabled, active = go.activeInHierarchy,
                rotation = Vec(go.transform.eulerAngles), localScale = Vec(go.transform.localScale),
                cameraRotationErrorDegrees = camera ? Quaternion.Angle(go.transform.rotation, camera.transform.rotation) : (float?)null,
                facingNote = "Raw component/camera quaternion angle; project billboard rules and semantic parent roots are not inferred.",
                tracks = tracks.Skip(offset).Take(limit).ToArray(), totalTracks = tracks.Length,
                nextOffset = offset + limit < tracks.Length ? offset + limit : (int?)null
            });
        }

        private static bool IsSkeleton(Type type)
        {
            for (; type != null; type = type.BaseType)
            {
                if (type.FullName == "Spine.Unity.SkeletonRenderer" || type.FullName == "Spine.Unity.SkeletonGraphic")
                {
                    return true;
                }
            }
            return false;
        }

        private static McpHostResult Catalog(object data, JObject args, int offset, int limit)
        {
            if (data == null)
            {
                return McpHostResult.Fail("Spine data is unavailable or incompatible with this runtime version.");
            }
            var kind = args["kind"]?.Value<string>() ?? "animations";
            var member = kind == "animations" ? "Animations" : kind == "skins" ? "Skins" : kind == "slots" ? "Slots" : kind == "bones" ? "Bones" : kind == "events" ? "Events" : null;
            if (member == null)
            {
                return McpHostResult.Fail("kind must be animations/skins/slots/bones/events.", -32602);
            }
            var collection = R.Get(data, member);
            if (collection == null)
            {
                return McpHostResult.Fail("Spine data member is unavailable in this runtime version: " + member);
            }
            var values = R.Items(collection).Select(item => (object)new
            {
                name = R.Named(item), seconds = R.Get(item, "Duration"), parent = R.Named(R.Get(item, "Parent")),
                bone = R.Named(R.Get(item, "BoneData"))
            }).ToArray();
            return Page(values, offset, limit);
        }

        private static object Bone(Transform transform, object bone)
        {
            var worldX = R.Get(bone, "WorldX");
            var worldY = R.Get(bone, "WorldY");
            if (worldX == null || worldY == null)
            {
                throw new InvalidOperationException("Spine bone world position is unavailable in this runtime version.");
            }
            var x = Convert.ToSingle(worldX);
            var y = Convert.ToSingle(worldY);
            return new { name = R.Named(R.Get(bone, "Data")), local = new { x = R.Get(bone, "X"), y = R.Get(bone, "Y"), rotation = R.Get(bone, "Rotation") },
                skeletonPosition = new { x, y }, worldPosition = Vec(transform.TransformPoint(new Vector3(x, y, 0))) };
        }

        private static object Vec(Vector3 value) => new { x = value.x, y = value.y, z = value.z };

        private static McpHostResult Page(object[] values, int offset, int limit) => McpHostResult.Success(new
        {
            schema = "kabu.spine.page/v1", total = values.Length, offset, limit,
            items = values.Skip(offset).Take(limit).ToArray(), nextOffset = offset + limit < values.Length ? offset + limit : (int?)null
        });
    }
}
