using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 读取工程根目录 conf.json（本地构建/调试用 CDN 配置）。
/// cdn_root_folder 支持绝对路径，或相对工程根目录的相对路径。
/// 缺省/空值时使用工程同级目录 LocalCdn（即 ../LocalCdn）。
/// </summary>
public static class BuildConf
{
    /// <summary>相对工程根的默认 CDN 目录（Demo 同级 LocalCdn，即 demo/LocalCdn）。</summary>
    public const string DefaultCdnRootRelative = "Library/LocalCdn";

    [Serializable]
    private class ConfData
    {
        public string cdn_root_folder;
        public int port = 8080;
    }

    private static string projectRoot = Path.GetFullPath(
        Path.Combine(Application.dataPath, ".."));

    private static string confPath = Path.Combine(projectRoot, "conf.json");

    private static ConfData LoadOrDefault()
    {
        if (!File.Exists(confPath))
        {
            Debug.LogWarning($"[BuildConf] conf.json 不存在，使用默认 CDN：{DefaultCdnRootRelative}");
            return new ConfData { cdn_root_folder = DefaultCdnRootRelative, port = 8080 };
        }

        var conf = JsonUtility.FromJson<ConfData>(File.ReadAllText(confPath));
        if (string.IsNullOrEmpty(conf.cdn_root_folder))
        {
            Debug.LogWarning($"[BuildConf] cdn_root_folder 为空，使用默认 CDN：{DefaultCdnRootRelative}");
            conf.cdn_root_folder = DefaultCdnRootRelative;
        }

        return conf;
    }

    /// <summary>
    /// CDN 根目录（已解析为绝对路径）。不存在则自动创建。
    /// 默认：工程同级 LocalCdn（client-demo/../LocalCdn）。
    /// </summary>
    public static string CdnRootFolder
    {
        get
        {
            var conf = LoadOrDefault();
            var path = conf.cdn_root_folder.Trim();
            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(projectRoot, path);
            }

            path = Path.GetFullPath(path);
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                Debug.Log($"[BuildConf] 已创建 CDN 根目录: {path}");
            }

            return path;
        }
    }

    /// <summary>本地 HTTP 服务端口，来自 conf.json 的 port 字段</summary>
    public static int Port => LoadOrDefault().port;
}
