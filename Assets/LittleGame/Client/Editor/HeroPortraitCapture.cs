using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LittleGame.Client.Editor
{
    /// <summary>从实战预制体离屏拍摄头像，预览场景与战斗场景隔离。</summary>
    public static class HeroPortraitCapture
    {
        private const string Output = "Assets/Package_Unit/Portrait/Base";
        private static readonly string[] Heroes =
        {
            "Knight", "Ranger", "Mage", "Rogue",
            "Skeleton_Warrior", "Skeleton_Minion", "Skeleton_Mage", "Skeleton_Rogue"
        };

        [MenuItem("Little Game/Art/Capture Hero Portraits")]
        public static void CaptureAll()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("请退出 Play 后拍摄英雄头像。");
            Directory.CreateDirectory(Output);
            var sheet = new Texture2D(1024, 512, TextureFormat.RGBA32, false);
            try
            {
                for (int i = 0; i < Heroes.Length; i++)
                {
                    var portrait = Capture(Heroes[i]);
                    try
                    {
                        File.WriteAllBytes(Output + "/" + Heroes[i] + ".png", portrait.EncodeToPNG());
                        sheet.SetPixels((i % 4) * 256, (1 - i / 4) * 256, 256, 256,
                            portrait.GetPixels());
                    }
                    finally { UnityEngine.Object.DestroyImmediate(portrait); }
                }
                sheet.Apply();
                Directory.CreateDirectory("Reports/Art/HeroPortraits");
                File.WriteAllBytes("Reports/Art/HeroPortraits/hero-portraits.png", sheet.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(sheet); }
            AssetDatabase.Refresh();
            foreach (string hero in Heroes)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(Output + "/" + hero + ".png");
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 256;
                importer.SaveAndReimport();
            }
            Debug.Log("Hero portraits captured: " + string.Join(", ", Heroes));
        }

        private static Texture2D Capture(string hero)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Package_Unit/Low/Base/" + hero + "/" + hero + ".prefab");
            if (prefab == null) throw new FileNotFoundException("缺少英雄预制体：" + hero);
            var preview = new PreviewRenderUtility();
            try
            {
                var instance = UnityEngine.Object.Instantiate(prefab);
                preview.AddSingleGO(instance);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var renderers = instance.GetComponentsInChildren<Renderer>()
                    .Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
                if (renderers.Length == 0) throw new InvalidOperationException(hero + " 没有可见模型");
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                // 用头部实际网格边界定取景中心，保留头顶帽子和肩部轮廓。
                var heads = renderers.Where(r => r.name.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                var headBounds = heads.Length > 0 ? heads[0].bounds : bounds;
                foreach (var head in heads) headBounds.Encapsulate(head.bounds);
                float frame = bounds.size.y * .65f;
                var headBone = instance.GetComponentsInChildren<Transform>()
                    .FirstOrDefault(t => string.Equals(t.name, "head", StringComparison.OrdinalIgnoreCase));
                var target = new Vector3(headBone != null ? headBone.position.x : headBounds.center.x,
                    bounds.max.y - frame * .44f,
                    headBone != null ? headBone.position.z : headBounds.center.z);
                preview.camera.orthographic = true;
                preview.camera.orthographicSize = frame * .5f;
                preview.camera.nearClipPlane = .01f;
                preview.camera.farClipPlane = 100f;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(.12f, .105f, .17f, 1f);
                // 模型正面朝 +Z，统一轻微三分之四侧角，镜头略高于眼睛。
                preview.camera.transform.position = target + new Vector3(.26f, .10f, 1f).normalized * bounds.size.y * 3f;
                preview.camera.transform.LookAt(target);
                preview.lights[0].intensity = 1.25f;
                preview.lights[0].transform.rotation = Quaternion.Euler(35f, -35f, 0f);
                preview.lights[1].intensity = .8f;
                preview.lights[1].transform.rotation = Quaternion.Euler(315f, 145f, 0f);
                preview.ambientColor = new Color(.55f, .55f, .60f);
                preview.BeginPreview(new Rect(0, 0, 768, 768), GUIStyle.none);
                preview.Render(true);
                var rendered = preview.EndPreview();
                var previous = RenderTexture.active;
                var resized = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32);
                try
                {
                    // 高分辨率拍摄后缩小，改善小尺寸下的轮廓锯齿。
                    Graphics.Blit(rendered, resized);
                    RenderTexture.active = resized;
                    var image = new Texture2D(256, 256, TextureFormat.RGBA32, false);
                    image.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                    image.Apply();
                    Debug.Log("Portrait " + hero + " bounds=" + bounds + " head=" + headBounds);
                    return image;
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(resized);
                }
            }
            finally { preview.Cleanup(); }
        }
    }
}
