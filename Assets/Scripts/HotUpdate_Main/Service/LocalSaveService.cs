using Newtonsoft.Json;
using System.IO;
using UnityEngine;

/// <summary>
/// 本机键值存档。业务读写走这里，底层仍是 <see cref="PlayerPrefs"/>。
/// </summary>
public sealed class LocalSaveService : IService
{
    /// <summary>读取既有文件存档；不改变玩家现有路径和存档格式。</summary>
    public T ReadFile<T>(string path) where T : class
    {
        return File.Exists(path) ? JsonConvert.DeserializeObject<T>(File.ReadAllText(path))
            ?? throw new InvalidDataException("空存档") : null;
    }

    /// <summary>同卷临时文件替换，保留上一份有效存档；失败不删除原档。</summary>
    public void WriteFile(string path, object data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string staging = path + ".tmp";
        File.WriteAllText(staging, JsonConvert.SerializeObject(data, Formatting.Indented));
        if (File.Exists(path)) File.Replace(staging, path, path + ".bak");
        else File.Move(staging, path);
    }

    /// <summary>是否已有该键。</summary>
    public bool HasKey(string key)
    {
        return PlayerPrefs.HasKey(key);
    }

    /// <summary>删除键并立刻落盘。</summary>
    public void DeleteKey(string key)
    {
        PlayerPrefs.DeleteKey(key);
        Flush();
    }

    /// <summary>把已写入的键值刷到磁盘。</summary>
    public void Flush()
    {
        PlayerPrefs.Save();
    }

    /// <summary>读字符串。无键时返回 <paramref name="defaultValue"/>。</summary>
    public string GetString(string key, string defaultValue = "")
    {
        return PlayerPrefs.GetString(key, defaultValue);
    }

    /// <summary>写字符串并立刻落盘。</summary>
    public void SetString(string key, string value)
    {
        PlayerPrefs.SetString(key, value);
        Flush();
    }

    /// <summary>读整数。无键时返回 <paramref name="defaultValue"/>。</summary>
    public int GetInt(string key, int defaultValue = 0)
    {
        return PlayerPrefs.GetInt(key, defaultValue);
    }

    /// <summary>写整数并立刻落盘。</summary>
    public void SetInt(string key, int value)
    {
        PlayerPrefs.SetInt(key, value);
        Flush();
    }

    /// <summary>
    /// 按 Newtonsoft 读 JSON。无键或空串返回 null。
    /// </summary>
    public T GetJson<T>(string key) where T : class
    {
        if (!HasKey(key))
        {
            return null;
        }

        var json = GetString(key, string.Empty);
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        return JsonConvert.DeserializeObject<T>(json);
    }

    /// <summary>按 Newtonsoft 写 JSON 并立刻落盘。</summary>
    public void SetJson(string key, object value)
    {
        SetString(key, JsonConvert.SerializeObject(value));
    }
}
