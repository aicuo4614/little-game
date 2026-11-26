using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary><see cref="TimelineClipConfigBase"/> 读写：按 <see cref="TimelineClipConfigBase.Type"/> 反序列化具体配置类型。</summary>
public sealed class TimelineClipConfigConverter : JsonConverter
{
    public override bool CanConvert(Type objectType) => objectType == typeof(TimelineClipConfigBase);

    public override object ReadJson(
        JsonReader reader,
        Type objectType,
        object existingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;

        var root = JObject.Load(reader);
        if (root["Config"] is JObject nested)
        {
            var merged = new JObject();
            foreach (var prop in root.Properties())
            {
                if (prop.Name == "Config")
                    continue;

                merged[prop.Name] = prop.Value;
            }

            foreach (var prop in nested.Properties())
                merged[prop.Name] = prop.Value;

            root = merged;
        }

        var type = ReadType(root);
        var configType = TimelineJsonUtil.GetConfigType(type);
        if (configType == null)
        {
            Debug.LogWarning($"UnitFeatureConfigJsonConverter 不支持的 Feature 类型: {type}");
            return null;
        }

        var config = (TimelineClipConfigBase)JsonConvert.DeserializeObject(
            root.ToString(),
            configType,
            TimelineJsonUtil.ConcreteConfig);
        if (config != null)
            config.Type = type;

        return config;
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        var json = JsonConvert.SerializeObject(value, value.GetType(), TimelineJsonUtil.ConcreteConfig);
        writer.WriteRawValue(json);
    }

    private static TimelineClipType ReadType(JObject root)
    {
        if (root.Value<int?>("Type") is int typeInt)
            return (TimelineClipType)typeInt;

        return root.Value<TimelineClipType?>("Type") ?? default;
    }
}
