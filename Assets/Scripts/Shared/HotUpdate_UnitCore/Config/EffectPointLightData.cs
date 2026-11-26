using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 特效点光源配置（与 prefab 同目录 JSON：{名}_PointLight_Data.json）。
/// 根字段为默认/初始参数；时间轴由 <see cref="Samples"/> 关键关键帧描述。
/// </summary>
[Serializable]
public class EffectPointLightData
{
    /// <summary>默认 Intensity（初始参数；无采样或 t 在首帧前时的起点）。</summary>
    public float Intensity = 2f;

    /// <summary>默认 Range。</summary>
    public float Range = 12f;

    /// <summary>默认颜色 RGBA。</summary>
    public float ColorR = 1f;
    public float ColorG = 1f;
    public float ColorB = 1f;
    public float ColorA = 1f;

    /// <summary>按绝对秒排序的采样关键帧；空则整段使用默认参数。</summary>
    public List<EffectPointLightSample> Samples = new List<EffectPointLightSample>();

    /// <summary>最后一帧时间；无采样时为 0。</summary>
    public float LastSampleTime
    {
        get
        {
            if (Samples == null || Samples.Count == 0)
            {
                return 0f;
            }

            var last = 0f;
            for (var i = 0; i < Samples.Count; i++)
            {
                var t = Samples[i].Time;
                if (t > last)
                {
                    last = t;
                }
            }

            return last;
        }
    }

    public Color GetColor()
    {
        return new Color(ColorR, ColorG, ColorB, ColorA);
    }

    public void SetColor(Color color)
    {
        ColorR = color.r;
        ColorG = color.g;
        ColorB = color.b;
        ColorA = color.a;
    }
}
