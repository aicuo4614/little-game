using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary><see cref="EffectPointLightData"/> JSON 读写。</summary>
public static class EffectPointLightJsonUtil
{
    static JsonSerializerSettings settings = new JsonSerializerSettings
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Ignore,
    };

    /// <summary>从 JSON 文本解析；空或失败返回默认配置。</summary>
    public static EffectPointLightData FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return CreateDefault();
        }

        var data = JsonConvert.DeserializeObject<EffectPointLightData>(json, settings);
        if (data == null)
        {
            return CreateDefault();
        }

        if (data.Samples == null)
        {
            data.Samples = new List<EffectPointLightSample>();
        }

        EffectPointLightUtil.SortSamples(data.Samples);
        return data;
    }

    /// <summary>从 TextAsset 解析（运行时 Yoo 加载）。</summary>
    public static EffectPointLightData FromTextAsset(TextAsset textAsset)
    {
        if (textAsset == null)
        {
            return CreateDefault();
        }

        return FromJson(textAsset.text);
    }

    /// <summary>加载；文件不存在返回默认配置。</summary>
    public static EffectPointLightData Load(string jsonAssetPath)
    {
        var fullPath = ToFullPath(jsonAssetPath);
        if (!File.Exists(fullPath))
        {
            return CreateDefault();
        }

        var text = File.ReadAllText(fullPath);
        return FromJson(text);
    }

    /// <summary>保存并刷新 AssetDatabase。</summary>
    public static void Save(string jsonAssetPath, EffectPointLightData data)
    {
        if (data.Samples == null)
        {
            data.Samples = new List<EffectPointLightSample>();
        }

        EffectPointLightUtil.SortSamples(data.Samples);

        var fullPath = ToFullPath(jsonAssetPath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var text = JsonConvert.SerializeObject(data, settings);
        File.WriteAllText(fullPath, text);
        Debug.Log($"[EffectPointLight] 已保存: {jsonAssetPath}");
    }

    public static EffectPointLightData CreateDefault()
    {
        return new EffectPointLightData
        {
            Intensity = EffectPointLightUtil.DefaultIntensity,
            Range = EffectPointLightUtil.DefaultRange,
            ColorR = 1f,
            ColorG = 1f,
            ColorB = 1f,
            ColorA = 1f,
            Samples = new List<EffectPointLightSample>(),
        };
    }

    static string ToFullPath(string assetPath)
    {
        if (Path.IsPathRooted(assetPath))
        {
            return assetPath;
        }

        var projectRoot = Path.GetDirectoryName(Application.dataPath);
        return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
    }
}
