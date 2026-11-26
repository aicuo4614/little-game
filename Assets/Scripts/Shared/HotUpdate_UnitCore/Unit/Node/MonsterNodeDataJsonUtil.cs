using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary><see cref="MonsterNodeDataConfig"/> 的 JSON 解析（运行时 / 编辑器共用）。</summary>
public static class MonsterNodeDataJsonUtil
{
    static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Ignore,
    };

    /// <summary>从 JSON 文本解析；空或失败返回空配置（非 null）。</summary>
    public static MonsterNodeDataConfig FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new MonsterNodeDataConfig();
        }

        var config = JsonConvert.DeserializeObject<MonsterNodeDataConfig>(json, Settings);
        if (config == null)
        {
            return new MonsterNodeDataConfig();
        }

        if (config.Nodes == null)
        {
            config.Nodes = new List<MonsterNodePathEntry>();
        }

        return config;
    }

    /// <summary>从 TextAsset 解析。</summary>
    public static MonsterNodeDataConfig FromTextAsset(TextAsset textAsset)
    {
        if (textAsset == null)
        {
            return new MonsterNodeDataConfig();
        }

        return FromJson(textAsset.text);
    }

    /// <summary>序列化为 JSON 文本（编辑器落盘用）。</summary>
    public static string ToJson(MonsterNodeDataConfig config)
    {
        if (config == null)
        {
            config = new MonsterNodeDataConfig();
        }

        if (config.Nodes == null)
        {
            config.Nodes = new List<MonsterNodePathEntry>();
        }

        return JsonConvert.SerializeObject(config, Settings);
    }
}
