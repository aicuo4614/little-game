using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// YooAsset 本地下载缓存菜单：打开 / 清空（Editor：<c>{Project}/yoo</c>；另含 <c>persistentDataPath/yoo</c>）。
/// </summary>
public static class ClearResourceCacheMenu
{
    const string OpenMenuPath = "Kabu/Resource/打开缓存文件夹";
    const string ClearMenuPath = "Kabu/Resource/清空资源缓存";
    const string DefaultYooFolderName = "yoo";

    /// <summary>确认后清空当前工程在编辑器中的全部 PlayerPrefs，并立即保存。</summary>
    [MenuItem("Kabu/Resource/PlayerPrefs.DeleteAll", priority = 51)]
    static void ClearPlayerPrefs()
    {
        if (!EditorUtility.DisplayDialog(
                "清空 PlayerPrefs",
                "将清空当前工程在 Unity 编辑器中的全部 PlayerPrefs（本地设置、登录记录等），此操作不可撤销。",
                "清空",
                "取消"))
        {
            return;
        }

        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        Debug.Log("[Kabu] 已清空当前工程在编辑器中的全部 PlayerPrefs。");
    }

    /// <summary>在资源管理器中打开已存在的 Yoo 缓存根目录（工程下 + persistentDataPath）。</summary>
    [MenuItem(OpenMenuPath, priority = 49)]
    static void OpenResourceCache()
    {
        var folderName = ReadDefaultYooFolderName();
        var targets = CollectCacheRoots(folderName);
        var opened = 0;
        for (var i = 0; i < targets.Count; i++)
        {
            var path = targets[i];
            if (!Directory.Exists(path))
            {
                continue;
            }

            EditorUtility.RevealInFinder(path);
            opened++;
        }

        if (opened > 0)
        {
            Debug.Log($"[Kabu] 已打开缓存文件夹（{opened} 个）");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("缓存目录尚不存在（未热更下载过）：");
        for (var i = 0; i < targets.Count; i++)
        {
            sb.AppendLine("  " + targets[i]);
        }

        sb.AppendLine();
        sb.Append("是否在资源管理器中显示工程根目录？");
        if (EditorUtility.DisplayDialog("打开缓存文件夹", sb.ToString(), "显示工程根", "取消"))
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            EditorUtility.RevealInFinder(projectRoot);
        }
    }

    [MenuItem(ClearMenuPath, priority = 50)]
    static void ClearResourceCache()
    {
        if (!EditorUtility.DisplayDialog(
                "清空资源缓存",
                "将删除 YooAsset 本地下载缓存。\n\n"
                + "下次进入 Play 会重新走热更（版本检查 + 下载）。\n"
                + "若当前正在 Play，会先退出 Play 模式。",
                "清空",
                "取消"))
        {
            return;
        }

        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += ExecuteClearAfterPlayStopped;
            return;
        }

        ExecuteClear(showDialog: true);
    }

    static void ExecuteClearAfterPlayStopped()
    {
        EditorApplication.delayCall -= ExecuteClearAfterPlayStopped;
        ExecuteClear(showDialog: true);
    }

    static void ExecuteClear(bool showDialog)
    {
        var folderName = ReadDefaultYooFolderName();
        var targets = CollectCacheRoots(folderName);
        var deleted = new List<string>();
        var skipped = new List<string>();
        var failed = new List<string>();

        for (var i = 0; i < targets.Count; i++)
        {
            var path = targets[i];
            if (!Directory.Exists(path))
            {
                skipped.Add(path);
                continue;
            }

            try
            {
                Directory.Delete(path, recursive: true);
                deleted.Add(path);

                var metaPath = path + ".meta";
                if (File.Exists(metaPath))
                {
                    File.Delete(metaPath);
                }
            }
            catch (System.Exception ex)
            {
                failed.Add($"{path}\n  {ex.Message}");
            }
        }

        AssetDatabase.Refresh();

        if (showDialog)
        {
            ShowResultDialog(deleted, skipped, failed);
        }
    }

    static string ReadDefaultYooFolderName()
    {
        var settings = Resources.Load<ScriptableObject>("YooAssetSettings");
        if (settings == null)
        {
            return DefaultYooFolderName;
        }

        var so = new SerializedObject(settings);
        var prop = so.FindProperty("DefaultYooFolderName");
        if (prop == null || string.IsNullOrEmpty(prop.stringValue))
        {
            return DefaultYooFolderName;
        }

        return prop.stringValue;
    }

    static List<string> CollectCacheRoots(string folderName)
    {
        if (string.IsNullOrEmpty(folderName))
        {
            folderName = DefaultYooFolderName;
        }

        var list = new List<string>(2);
        var projectRoot = Path.GetDirectoryName(Application.dataPath);
        list.Add(Path.Combine(projectRoot, folderName));
        list.Add(Path.Combine(Application.persistentDataPath, folderName));
        return list;
    }

    static void ShowResultDialog(List<string> deleted, List<string> skipped, List<string> failed)
    {
        var sb = new StringBuilder();
        if (deleted.Count > 0)
        {
            sb.AppendLine("已删除：");
            for (var i = 0; i < deleted.Count; i++)
            {
                sb.AppendLine("  " + deleted[i]);
            }
        }

        if (skipped.Count > 0)
        {
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }

            sb.AppendLine("不存在（跳过）：");
            for (var i = 0; i < skipped.Count; i++)
            {
                sb.AppendLine("  " + skipped[i]);
            }
        }

        if (failed.Count > 0)
        {
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }

            sb.AppendLine("删除失败：");
            for (var i = 0; i < failed.Count; i++)
            {
                sb.AppendLine("  " + failed[i]);
            }
        }

        if (sb.Length == 0)
        {
            sb.AppendLine("没有可清理的缓存目录。");
        }

        var title = failed.Count > 0 ? "清空资源缓存（部分失败）" : "清空资源缓存";
        EditorUtility.DisplayDialog(title, sb.ToString(), "确定");
        Debug.Log("[Kabu] 清空资源缓存\n" + sb);
    }
}
