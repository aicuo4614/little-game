using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>特效点光源：路径、查找节点、按采样关键帧求值；兼 EffectPackage 地址归一。</summary>
public static class EffectPointLightUtil
{
    public const string NodeName = "Point Light";
    public const string JsonSuffix = "_PointLight_Data.json";

    /// <summary>Yoo 地址后缀（无扩展名）：<c>Effect/xxx</c> → <c>Effect/xxx_PointLight_Data</c>。</summary>
    public const string AddressSuffix = "_PointLight_Data";

    public const string DefaultCullingLayerName = "Character";
    public const float DefaultIntensity = 2f;
    public const float DefaultRange = 12f;

    /// <summary>同 Time 判定阈值（秒）。</summary>
    public const float SampleTimeEpsilon = 1e-4f;

    /// <summary>由特效 prefab 工程路径推导同目录 JSON 路径。</summary>
    public static string GetJsonPathFromPrefab(string prefabAssetPath)
    {
        if (string.IsNullOrEmpty(prefabAssetPath))
        {
            return string.Empty;
        }

        var dir = Path.GetDirectoryName(prefabAssetPath);
        if (string.IsNullOrEmpty(dir))
        {
            return string.Empty;
        }

        dir = dir.Replace('\\', '/');
        var name = Path.GetFileNameWithoutExtension(prefabAssetPath);
        return dir + "/" + name + JsonSuffix;
    }

    /// <summary>
    /// 由特效路径推导 prefab 的 Yoo 地址（无扩展名），始终为 <c>Effect/...</c>。
    /// 兼容 <c>Assets/Package_Effect/Base|OnDemand/xxx.prefab</c>、
    /// 旧 <c>Assets/Package_Res/Effect/xxx.prefab</c> 与已是 <c>Effect/xxx</c> 的地址。
    /// </summary>
    public static string GetPrefabAddressFromEffectPath(string effectPath)
    {
        if (string.IsNullOrEmpty(effectPath))
        {
            return string.Empty;
        }

        var path = effectPath.Replace('\\', '/');
        const string effectRoot = "Assets/Package_Effect/";
        const string resRoot = "Assets/Package_Res/";
        const string monsterRoot = "Assets/Package_Unit/";

        if (path.StartsWith(effectRoot, StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(effectRoot.Length);
            if (path.StartsWith("Base/", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring("Base/".Length);
            }
            else if (path.StartsWith("OnDemand/", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring("OnDemand/".Length);
            }

            path = StripExtension(path);
            if (path.StartsWith("Effect/", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            return "Effect/" + path;
        }

        if (path.StartsWith(resRoot, StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(resRoot.Length);
        }
        else if (path.StartsWith(monsterRoot, StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(monsterRoot.Length);
            path = StripMonsterCollectLayer(path);
        }

        path = StripExtension(path);

        // 旧 Package_Res/Effect/... 剥根后已是 Effect/...
        if (path.StartsWith("Effect/", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return path;
    }

    static string StripExtension(string path)
    {
        if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
        {
            return path.Substring(0, path.Length - ".prefab".Length);
        }

        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return path.Substring(0, path.Length - ".json".Length);
        }

        return path;
    }

    /// <summary>
    /// 去掉 Monster <c>Unit|Skill|Timeline</c> 下 Base/OnDemand 采集层，与 Address 规则一致；
    /// 根目录 <c>Base/</c>（原 Global）保留。
    /// </summary>
    static string StripMonsterCollectLayer(string relativePath)
    {
        if (TryStripTypeCollectLayer(relativePath, "Unit", out var unitPath))
        {
            return unitPath;
        }

        if (TryStripTypeCollectLayer(relativePath, "Skill", out var skillPath))
        {
            return skillPath;
        }

        if (TryStripTypeCollectLayer(relativePath, "Timeline", out var timelinePath))
        {
            return timelinePath;
        }

        return relativePath;
    }

    static bool TryStripTypeCollectLayer(string relativePath, string typeFolder, out string stripped)
    {
        var basePrefix = typeFolder + "/Base/";
        if (relativePath.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase))
        {
            stripped = typeFolder + "/" + relativePath.Substring(basePrefix.Length);
            return true;
        }

        var onDemandPrefix = typeFolder + "/OnDemand/";
        if (relativePath.StartsWith(onDemandPrefix, StringComparison.OrdinalIgnoreCase))
        {
            stripped = typeFolder + "/" + relativePath.Substring(onDemandPrefix.Length);
            return true;
        }

        stripped = relativePath;
        return false;
    }

    /// <summary>由特效路径推导 PointLight JSON 的 Yoo 地址。</summary>
    public static string GetAddressFromEffectPath(string effectPath)
    {
        var prefabAddress = GetPrefabAddressFromEffectPath(effectPath);
        if (string.IsNullOrEmpty(prefabAddress))
        {
            return string.Empty;
        }

        if (prefabAddress.EndsWith(AddressSuffix, StringComparison.Ordinal))
        {
            return prefabAddress;
        }

        return prefabAddress + AddressSuffix;
    }

    /// <summary>在根节点直接子级中按名字查找 Point Light。</summary>
    public static Light FindPointLight(Transform root)
    {
        for (var i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            if (child.name != NodeName)
            {
                continue;
            }

            return child.GetComponent<Light>();
        }

        return null;
    }

    /// <summary>按 Time 升序排序采样列表（原地）。</summary>
    public static void SortSamples(List<EffectPointLightSample> samples)
    {
        if (samples == null || samples.Count <= 1)
        {
            return;
        }

        samples.Sort(CompareSampleTime);
    }

    static int CompareSampleTime(EffectPointLightSample a, EffectPointLightSample b)
    {
        if (a.Time < b.Time)
        {
            return -1;
        }

        if (a.Time > b.Time)
        {
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// 在指定时间写入或覆盖采样；同 Time（阈值内）覆盖。调用后保证按 Time 升序。
    /// </summary>
    public static void UpsertSample(EffectPointLightData data, float time, float intensity, float range, Color color)
    {
        if (data.Samples == null)
        {
            data.Samples = new List<EffectPointLightSample>();
        }

        time = Mathf.Max(0f, time);
        for (var i = 0; i < data.Samples.Count; i++)
        {
            if (Mathf.Abs(data.Samples[i].Time - time) <= SampleTimeEpsilon)
            {
                var existing = data.Samples[i];
                existing.Time = time;
                existing.Intensity = intensity;
                existing.Range = range;
                existing.ColorR = color.r;
                existing.ColorG = color.g;
                existing.ColorB = color.b;
                existing.ColorA = color.a;
                SortSamples(data.Samples);
                return;
            }
        }

        data.Samples.Add(new EffectPointLightSample
        {
            Time = time,
            Intensity = intensity,
            Range = range,
            ColorR = color.r,
            ColorG = color.g,
            ColorB = color.b,
            ColorA = color.a,
        });
        SortSamples(data.Samples);
    }

    /// <summary>
    /// 按播放时间求 Intensity / Range / Color：相邻关键帧线性插值；
    /// 无采样用默认；早于首帧从 (0,默认) 插到首帧；晚于末帧保持末帧。
    /// </summary>
    public static void EvaluateAtTime(EffectPointLightData data, float time, out float intensity, out float range, out Color color)
    {
        var defaultIntensity = data.Intensity;
        var defaultRange = data.Range;
        var defaultColor = data.GetColor();
        var samples = data.Samples;

        if (samples == null || samples.Count == 0)
        {
            intensity = defaultIntensity;
            range = defaultRange;
            color = defaultColor;
            return;
        }

        if (time < 0f)
        {
            time = 0f;
        }

        // 假定已按 Time 升序；编辑器保存 / Upsert 会排序。
        var first = samples[0];
        if (time <= first.Time)
        {
            // 首帧就在 0：直接用首帧；否则从 (0, 默认) 插到首帧
            if (first.Time <= SampleTimeEpsilon)
            {
                intensity = first.Intensity;
                range = first.Range;
                color = SampleToColor(first);
                return;
            }

            var u = time / first.Time;
            intensity = Mathf.Lerp(defaultIntensity, first.Intensity, u);
            range = Mathf.Lerp(defaultRange, first.Range, u);
            color = Color.Lerp(defaultColor, SampleToColor(first), u);
            return;
        }

        var last = samples[samples.Count - 1];
        if (time >= last.Time)
        {
            intensity = last.Intensity;
            range = last.Range;
            color = SampleToColor(last);
            return;
        }

        for (var i = 0; i < samples.Count - 1; i++)
        {
            var a = samples[i];
            var b = samples[i + 1];
            if (time > b.Time)
            {
                continue;
            }

            var span = b.Time - a.Time;
            var u = span <= 1e-8f ? 1f : (time - a.Time) / span;
            intensity = Mathf.Lerp(a.Intensity, b.Intensity, u);
            range = Mathf.Lerp(a.Range, b.Range, u);
            color = Color.Lerp(SampleToColor(a), SampleToColor(b), u);
            return;
        }

        intensity = last.Intensity;
        range = last.Range;
        color = SampleToColor(last);
    }

    static Color SampleToColor(EffectPointLightSample sample)
    {
        return new Color(sample.ColorR, sample.ColorG, sample.ColorB, sample.ColorA);
    }

    /// <summary>默认只照 Character 层。</summary>
    public static int GetDefaultCullingMask()
    {
        return 1 << LayerMask.NameToLayer(DefaultCullingLayerName);
    }

    /// <summary>把默认/静态属性写到 Light（不含时间轴求值）。</summary>
    public static void ApplyStaticProperties(Light light, EffectPointLightData data)
    {
        light.type = LightType.Point;
        // lightmapBakeType 仅为 Editor API；Player / 打 Bundle 编译没有该成员，烘焙模式在 prefab 上由编辑器写死。
#if UNITY_EDITOR
        light.lightmapBakeType = LightmapBakeType.Mixed;
#endif
        light.cullingMask = GetDefaultCullingMask();
        light.range = data.Range;
        light.color = data.GetColor();
        light.intensity = data.Intensity;
    }

    /// <summary>按时间应用采样求值后的 Intensity / Range / Color，并同步 CullingMask。</summary>
    public static void ApplyAtTime(Light light, EffectPointLightData data, float time)
    {
        light.type = LightType.Point;
#if UNITY_EDITOR
        light.lightmapBakeType = LightmapBakeType.Mixed;
#endif
        light.cullingMask = GetDefaultCullingMask();
        EvaluateAtTime(data, time, out var intensity, out var range, out var color);
        light.intensity = intensity;
        light.range = range;
        light.color = color;
    }
}
