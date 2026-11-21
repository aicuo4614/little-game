using System;
using System.Collections.Generic;
using System.IO;
using KabuUnityMcp.Editor.Handlers;
using UnityEditor;
using UnityEngine;

namespace KabuUnityMcp.Editor.Handlers.Assets
{
    /// <summary>Assets 工具业务逻辑（无 WS）。</summary>
    internal static class AssetOps
    {
        public static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            return path.Trim().Replace('\\', '/').TrimEnd('/');
        }

        public static bool IsValidFolderName(string name, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "folderName 不能为空。";
                return false;
            }

            name = name.Trim();
            if (name.IndexOfAny(new[] { '/', '\\', '<', '>', ':', '"', '|', '?', '*' }) >= 0)
            {
                error = "folderName 不能包含路径或非法字符。";
                return false;
            }

            return true;
        }

        public static HandlerResult Find(string filter, string[] folders, int maxResults)
        {
            if (maxResults <= 0)
                return HandlerResult.Fail("maxResults 必须大于 0。", -32602);

            filter ??= string.Empty;
            var guids = folders == null || folders.Length == 0
                ? AssetDatabase.FindAssets(filter)
                : AssetDatabase.FindAssets(filter, folders);

            var list = new List<object>();
            for (var i = 0; i < guids.Length && list.Count < maxResults; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path))
                    continue;
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                list.Add(new
                {
                    path,
                    guid = guids[i],
                    typeName = type != null ? type.Name : null
                });
            }

            return HandlerResult.Success(new
            {
                filter,
                count = list.Count,
                truncated = guids.Length > list.Count,
                assets = list
            });
        }

        public static HandlerResult CreateFolder(string parentPath, string folderName)
        {
            parentPath = NormalizeAssetPath(parentPath);
            if (string.IsNullOrEmpty(parentPath))
                return HandlerResult.Fail("请提供 parentPath（如 Assets）与 folderName。", -32602);
            if (!IsValidFolderName(folderName, out var nameError))
                return HandlerResult.Fail(nameError, -32602);

            folderName = folderName.Trim();
            if (!AssetDatabase.IsValidFolder(parentPath))
                return HandlerResult.Fail($"父文件夹不存在：{parentPath}");

            var target = $"{parentPath}/{folderName}";
            if (AssetDatabase.IsValidFolder(target))
                return HandlerResult.Fail($"文件夹已存在：{target}");

            var guid = AssetDatabase.CreateFolder(parentPath, folderName);
            AssetDatabase.Refresh();
            return HandlerResult.Success(new
            {
                path = target,
                guid,
                message = $"已创建文件夹：{target}"
            });
        }

        public static HandlerResult Copy(string sourcePath, string destinationPath)
        {
            sourcePath = NormalizeAssetPath(sourcePath);
            destinationPath = NormalizeAssetPath(destinationPath);
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath))
                return HandlerResult.Fail("请提供 sourcePath 与 destinationPath。", -32602);

            if (!AssetExists(sourcePath))
                return HandlerResult.Fail($"源资源不存在：{sourcePath}");

            EnsureParentFolder(destinationPath);
            if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
                return HandlerResult.Fail($"复制失败：{sourcePath} → {destinationPath}");

            AssetDatabase.Refresh();
            return HandlerResult.Success(new
            {
                sourcePath,
                destinationPath,
                message = $"已复制：{sourcePath} → {destinationPath}"
            });
        }

        public static HandlerResult Move(string sourcePath, string destinationPath)
        {
            sourcePath = NormalizeAssetPath(sourcePath);
            destinationPath = NormalizeAssetPath(destinationPath);
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath))
                return HandlerResult.Fail("请提供 sourcePath 与 destinationPath。", -32602);

            if (!AssetExists(sourcePath))
                return HandlerResult.Fail($"源资源不存在：{sourcePath}");

            EnsureParentFolder(destinationPath);
            var error = AssetDatabase.MoveAsset(sourcePath, destinationPath);
            if (!string.IsNullOrEmpty(error))
                return HandlerResult.Fail($"移动失败：{error}");

            AssetDatabase.Refresh();
            return HandlerResult.Success(new
            {
                sourcePath,
                destinationPath,
                message = $"已移动/重命名：{sourcePath} → {destinationPath}"
            });
        }

        public static HandlerResult Delete(string path)
        {
            path = NormalizeAssetPath(path);
            if (string.IsNullOrEmpty(path))
                return HandlerResult.Fail("请提供 path。", -32602);

            if (!AssetExists(path))
                return HandlerResult.Fail($"资源不存在：{path}");

            if (!AssetDatabase.DeleteAsset(path))
                return HandlerResult.Fail($"删除失败：{path}");

            AssetDatabase.Refresh();
            return HandlerResult.Success(new
            {
                path,
                deleted = true,
                message = $"已删除：{path}"
            });
        }

        public static HandlerResult Refresh()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var compiling = EditorApplication.isCompiling;
            var compileFailed = EditorUtility.scriptCompilationFailed;
            return HandlerResult.Success(new
            {
                refreshed = true,
                isCompiling = compiling,
                scriptCompilationFailed = compileFailed,
                message = compileFailed
                    ? "已刷新，但存在脚本编译错误。"
                    : compiling
                        ? "已刷新，脚本正在编译中。"
                        : "AssetDatabase 已刷新。"
            });
        }

        public static HandlerResult CreateScript(string path, string contents)
        {
            path = NormalizeAssetPath(path);
            if (string.IsNullOrEmpty(path))
                return HandlerResult.Fail("请提供 path，如 Assets/Scripts/MyBehaviour.cs。", -32602);

            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return HandlerResult.Fail("脚本路径必须以 Assets/ 开头。", -32602);

            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                return HandlerResult.Fail("脚本路径必须以 .cs 结尾。", -32602);

            if (AssetExists(path))
                return HandlerResult.Fail($"文件已存在：{path}");

            if (string.IsNullOrWhiteSpace(contents))
            {
                var className = Path.GetFileNameWithoutExtension(path);
                contents =
                    "using UnityEngine;\n\n" +
                    $"public class {className} : MonoBehaviour\n" +
                    "{\n" +
                    "    void Start()\n" +
                    "    {\n" +
                    "    }\n" +
                    "}\n";
            }

            try
            {
                EnsureParentFolder(path);
                var relative = path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                    ? path.Substring("Assets/".Length)
                    : path;
                var full = Path.Combine(Application.dataPath, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(full) ?? Application.dataPath);
                File.WriteAllText(full, contents);
            }
            catch (Exception ex)
            {
                return HandlerResult.Fail($"写入失败：{ex.Message}");
            }

            AssetDatabase.ImportAsset(path);
            AssetDatabase.Refresh();
            return HandlerResult.Success(new
            {
                path,
                bytes = contents.Length,
                isCompiling = EditorApplication.isCompiling,
                message = $"已创建脚本：{path}（可能触发编译）"
            });
        }

        /// <summary>
        /// 创建非脚本资源。assetType: Material / Texture2D / ScriptableObject 完整类名。
        /// </summary>
        public static HandlerResult CreateAsset(
            string path,
            string assetType,
            string shaderName,
            string color)
        {
            path = NormalizeAssetPath(path);
            if (string.IsNullOrEmpty(path))
                return HandlerResult.Fail("请提供 path，如 Assets/Materials/Red.mat。", -32602);
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return HandlerResult.Fail("资源路径必须以 Assets/ 开头。", -32602);
            if (string.IsNullOrWhiteSpace(assetType))
                return HandlerResult.Fail("请提供 assetType（Material / Texture2D / ScriptableObject 类名）。", -32602);
            if (AssetExists(path))
                return HandlerResult.Fail($"资源已存在：{path}");

            assetType = assetType.Trim();
            UnityEngine.Object asset;
            try
            {
                asset = CreateAssetInstance(assetType, shaderName, color, out var createError);
                if (createError != null)
                    return HandlerResult.Fail(createError, -32602);
            }
            catch (Exception ex)
            {
                return HandlerResult.Fail($"创建实例失败：{ex.Message}");
            }

            var expectedExt = GuessExtension(asset);
            if (!string.IsNullOrEmpty(expectedExt) &&
                !path.EndsWith(expectedExt, StringComparison.OrdinalIgnoreCase))
            {
                UnityEngine.Object.DestroyImmediate(asset);
                return HandlerResult.Fail($"路径扩展名应为 {expectedExt}（当前：{path}）。", -32602);
            }

            try
            {
                EnsureParentFolder(path);
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.SaveAssets();
            }
            catch (Exception ex)
            {
                UnityEngine.Object.DestroyImmediate(asset);
                return HandlerResult.Fail($"保存资源失败：{ex.Message}");
            }

            return HandlerResult.Success(new
            {
                path,
                assetType = asset.GetType().FullName,
                message = $"已创建资源：{path}"
            });
        }

        private static UnityEngine.Object CreateAssetInstance(
            string assetType, string shaderName, string color, out string error)
        {
            error = null;
            if (string.Equals(assetType, "Material", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(assetType, "UnityEngine.Material", StringComparison.OrdinalIgnoreCase))
            {
                var shader = Shader.Find(string.IsNullOrWhiteSpace(shaderName) ? "Standard" : shaderName.Trim());
                if (shader == null)
                    shader = Shader.Find("Universal Render Pipeline/Lit")
                             ?? Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    error = $"找不到 Shader：{shaderName ?? "Standard"}";
                    return null;
                }

                var mat = new Material(shader);
                if (!string.IsNullOrWhiteSpace(color) && TryParseColor(color, out var c))
                {
                    if (mat.HasProperty("_Color"))
                        mat.SetColor("_Color", c);
                    else if (mat.HasProperty("_BaseColor"))
                        mat.SetColor("_BaseColor", c);
                    else
                        mat.color = c;
                }
                return mat;
            }

            if (string.Equals(assetType, "Texture2D", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(assetType, "UnityEngine.Texture2D", StringComparison.OrdinalIgnoreCase))
            {
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var pixels = new Color[16];
                for (var i = 0; i < pixels.Length; i++)
                    pixels[i] = Color.white;
                if (!string.IsNullOrWhiteSpace(color) && TryParseColor(color, out var c))
                {
                    for (var i = 0; i < pixels.Length; i++)
                        pixels[i] = c;
                }
                tex.SetPixels(pixels);
                tex.Apply();
                return tex;
            }

            var type = ResolveUnityObjectType(assetType);
            if (type == null)
            {
                error = $"未找到类型：{assetType}";
                return null;
            }

            if (typeof(ScriptableObject).IsAssignableFrom(type))
                return ScriptableObject.CreateInstance(type);

            error = $"暂不支持创建类型 {type.Name}（支持 Material / Texture2D / ScriptableObject）。";
            return null;
        }

        private static Type ResolveUnityObjectType(string typeName)
        {
            var t = Type.GetType(typeName);
            if (t != null)
                return t;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = assembly.GetType(typeName);
                    if (t != null)
                        return t;
                    if (!typeName.Contains("."))
                    {
                        foreach (var candidate in assembly.GetTypes())
                        {
                            if (candidate.Name == typeName &&
                                typeof(UnityEngine.Object).IsAssignableFrom(candidate))
                                return candidate;
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }
            return null;
        }

        private static string GuessExtension(UnityEngine.Object asset)
        {
            if (asset is Material)
                return ".mat";
            if (asset is Texture2D)
                return ".asset";
            if (asset is ScriptableObject)
                return ".asset";
            return null;
        }

        private static bool TryParseColor(string value, out Color color)
        {
            color = default;
            if (ColorUtility.TryParseHtmlString(value.Trim(), out color))
                return true;
            var parts = value.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
                return false;
            if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var r) ||
                !float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var g) ||
                !float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var b))
                return false;
            var a = 1f;
            if (parts.Length >= 4)
                float.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out a);
            color = new Color(r, g, b, a);
            return true;
        }

        private static bool AssetExists(string path) =>
            AssetDatabase.LoadMainAssetAtPath(path) != null || AssetDatabase.IsValidFolder(path);

        private static void EnsureParentFolder(string assetPath)
        {
            var dir = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(dir) || AssetDatabase.IsValidFolder(dir))
                return;

            if (!dir.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"目标路径必须在 Assets 下：{assetPath}");

            var parts = dir.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
