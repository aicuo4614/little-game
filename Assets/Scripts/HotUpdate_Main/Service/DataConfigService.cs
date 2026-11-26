using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 配置表与 JSON 数据加载服务（对齐 Demo DataConfigService 入口，本地优先走 YooAsset / Resources）。
/// 通过 <c>MVC.DataConfigService</c> 访问。
/// </summary>
public sealed class DataConfigService : IService
{
    private readonly Dictionary<string, object> cache = new Dictionary<string, object>(StringComparer.Ordinal);

    public T LoadJson<T>(string location, ResourcePackageName packageName = ResourcePackageName.MainPackage) where T : class
    {
        var key = packageName + "|" + location + "|" + typeof(T).FullName;
        if (cache.TryGetValue(key, out var existing) && existing is T typed)
        {
            return typed;
        }

        var asset = MVC.ResourceService.LoadAsset<TextAsset>(location, packageName);
        if (asset == null)
        {
            return null;
        }

        try
        {
            var data = JsonConvert.DeserializeObject<T>(asset.text);
            if (data != null)
            {
                cache[key] = data;
            }

            return data;
        }
        finally
        {
            MVC.ResourceService.Release(location, packageName);
        }
    }

    public void Clear()
    {
        cache.Clear();
    }
}
