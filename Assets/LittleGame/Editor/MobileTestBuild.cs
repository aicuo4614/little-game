using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LittleGame.Editor
{
    public static class MobileTestBuild
    {
        public const string PackageId = "com.littlegame.teardown.dev";

        [Serializable]
        private sealed class BuildStatus
        {
            public string id;
            public string state;
            public string packageId = PackageId;
            public string apk;
            public string message;
            public long bytes;
        }

        private static BuildStatus status;
        private static string StatusPath => Path.Combine(
            Path.GetDirectoryName(Application.dataPath), "Library", "MobileTesting", "build-status.json");

        [MenuItem("Little Game/Mobile/Build Test APK")]
        public static void QueueBuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("请在编译结束并退出 Play 后构建手机包");
            if (status != null && status.state == "queued")
                throw new InvalidOperationException("已有手机构建任务排队");
            status = new BuildStatus { id = Guid.NewGuid().ToString("N"), state = "queued", message = "等待构建" };
            SaveStatus();
            EditorApplication.update -= Build;
            EditorApplication.update += Build;
        }

        private static void Build()
        {
            EditorApplication.update -= Build;
            var originalName = PlayerSettings.productName;
            try
            {
                status.state = "building";
                status.message = "正在构建 Android 测试包";
                SaveStatus();
                if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                    throw new InvalidOperationException("当前 Unity 缺少 Android Build Support");

                PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, PackageId);
                PlayerSettings.bundleVersion = "1.4.0";
                PlayerSettings.Android.bundleVersionCode = 5;
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel33;
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
                PlayerSettings.productName = "废品回收箱";
                EditorUserBuildSettings.buildAppBundle = false;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = false;

                RenderSetup.Configure();
                EnsureMaterial("DeviceMaterial", "Universal Render Pipeline/Lit");
                var deviceMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/LittleGame/Resources/DeviceMaterial.mat");
                deviceMaterial.EnableKeyword("_EMISSION");
                EditorUtility.SetDirty(deviceMaterial);
                AssetDatabase.SaveAssets();
                var directory = Path.GetFullPath("Builds/Android");
                Directory.CreateDirectory(directory);
                var staging = Path.Combine(directory, status.id + ".apk");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/Scenes/Structure.unity", "Assets/Scenes/Game.unity" },
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    locationPathName = staging,
                    options = BuildOptions.Development
                });
                if (report.summary.result != BuildResult.Succeeded || !File.Exists(staging))
                    throw new InvalidOperationException($"构建失败：{report.summary.result}，{report.summary.totalErrors} 个错误");

                var destination = Path.Combine(directory, "little-game.apk");
                if (File.Exists(destination)) File.Replace(staging, destination, null);
                else File.Move(staging, destination);
                status.apk = destination;
                status.bytes = new FileInfo(destination).Length;
                status.state = "succeeded";
                status.message = "手机测试包已构建";
                Debug.Log($"[MobileTest] 成功：{status.apk}，{status.bytes} 字节");
            }
            catch (Exception exception)
            {
                status.state = "failed";
                status.message = exception.ToString();
                Debug.LogException(exception);
            }
            finally
            {
                PlayerSettings.productName = originalName;
                SaveStatus();
            }
        }

        private static void EnsureMaterial(string name, string shaderName)
        {
            var path = $"Assets/LittleGame/Resources/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException($"缺少 Shader：{shaderName}");
            if (material == null) AssetDatabase.CreateAsset(new Material(shader), path);
            else if (material.shader != shader) { material.shader = shader; EditorUtility.SetDirty(material); }
        }

        private static void SaveStatus()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatusPath));
            var temporary = StatusPath + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(status, true));
            if (File.Exists(StatusPath)) File.Replace(temporary, StatusPath, null);
            else File.Move(temporary, StatusPath);
        }
    }
}
