using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LittleGame.Client.Editor
{
    public static class CombatSceneInstaller
    {
        [MenuItem("Little Game/Combat/Open Combat")]
        public static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("先退出Play");
            const string path = "Assets/Scenes/Combat.unity";
            if (!File.Exists(path)) { var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); EditorSceneManager.SaveScene(scene, path); }
            else EditorSceneManager.OpenScene(path);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.productName = "符阵军团";
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            LittleGame.Editor.RenderSetup.Configure();
            SetPortrait(); AssetDatabase.SaveAssets();
        }
        [MenuItem("Little Game/Combat/Prepare Character Assets")]
        public static void PrepareCharacters()
        {
            var report = new System.Text.StringBuilder();
            foreach (var path in Directory.GetFiles("Assets/LittleGame/Resources/CombatCharacters", "*.fbx"))
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.animationType = ModelImporterAnimationType.Legacy; importer.importAnimation = true;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.SaveAndReimport();
                report.AppendLine(path);
                foreach (var item in AssetDatabase.LoadAllAssetsAtPath(path)) if (item is AnimationClip clip)
                {
                    report.AppendLine("  " + clip.name + " legacy=" + clip.legacy + " duration=" + clip.length);
                    if (clip.name == "Idle_A") foreach (var binding in AnimationUtility.GetCurveBindings(clip).Take(6)) report.AppendLine("    binding=" + binding.path);
                }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && path.EndsWith("Knight.fbx")) foreach (var node in prefab.GetComponentsInChildren<Transform>().Take(18)) report.AppendLine("    node=" + node.name);
            }
            Directory.CreateDirectory("Reports/Combat"); File.WriteAllText("Reports/Combat/character-clips.txt", report.ToString());
        }
        [MenuItem("Little Game/Combat/Prepare Courtyard Assets")]
        public static void PrepareCourtyard()
        {
            foreach (var path in Directory.GetFiles("Assets/LittleGame/Resources/Courtyard").Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)))
                AssetDatabase.ImportAsset(path.Replace('\\','/'), ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
        }
        private static void SetPortrait()
        {
            var assembly = typeof(EditorWindow).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = singleton.GetProperty("instance").GetValue(null);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var groupType = sizesType.GetProperty("currentGroupType", flags).GetValue(sizes);
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new[] { groupType });
            var cls = group.GetType(); int count = (int)cls.GetMethod("GetTotalCount").Invoke(group, null); int index = -1;
            for (int i = 0; i < count; i++) { var size = cls.GetMethod("GetGameViewSize").Invoke(group, new object[] { i }); var type = size.GetType(); if ((int)type.GetProperty("width", flags).GetValue(size) == 540 && (int)type.GetProperty("height", flags).GetValue(size) == 960) { index = i; break; } }
            if (index < 0) { var kind = assembly.GetType("UnityEditor.GameViewSizeType"); var type = assembly.GetType("UnityEditor.GameViewSize"); var size = Activator.CreateInstance(type, flags, null, new object[] { Enum.Parse(kind, "FixedResolution"), 540, 960, "Combat Portrait" }, null); cls.GetMethod("AddCustomSize").Invoke(group, new[] { size }); index = count; }
            var window = EditorWindow.GetWindow(assembly.GetType("UnityEditor.GameView")); window.GetType().GetProperty("selectedSizeIndex", flags).SetValue(window, index); window.Repaint();
        }
    }
    internal sealed class CombatCharacterImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (assetPath.StartsWith("Assets/LittleGame/Resources/Courtyard/", StringComparison.Ordinal))
            {
                var sceneImporter = (ModelImporter)assetImporter;
                sceneImporter.isReadable = true; sceneImporter.importAnimation = false;
                sceneImporter.materialImportMode = ModelImporterMaterialImportMode.None;
                sceneImporter.importCameras = sceneImporter.importLights = false;
                return;
            }
            if (!assetPath.StartsWith("Assets/LittleGame/Resources/CombatCharacters/", StringComparison.Ordinal)) return;
            var importer = (ModelImporter)assetImporter; importer.animationType = ModelImporterAnimationType.Legacy;
            importer.importAnimation = true; importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false; importer.importLights = false;
        }
    }
}
