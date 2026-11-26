using System;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>Vector3 仅序列化 x/y/z，避免 magnitude、normalized 等冗余字段。</summary>
public sealed class UnityVector3JsonConverter : JsonConverter<Vector3>
{
    public override void WriteJson(JsonWriter writer, Vector3 value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("x");
        writer.WriteValue(value.x);
        writer.WritePropertyName("y");
        writer.WriteValue(value.y);
        writer.WritePropertyName("z");
        writer.WriteValue(value.z);
        writer.WriteEndObject();
    }

    public override Vector3 ReadJson(
        JsonReader reader,
        Type objectType,
        Vector3 existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return Vector3.zero;
        }

        var x = 0f;
        var y = 0f;
        var z = 0f;

        while (reader.Read())
        {
            if (reader.TokenType == JsonToken.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonToken.PropertyName)
            {
                continue;
            }

            var prop = (string)reader.Value;
            reader.Read();
            switch (prop)
            {
                case "x":
                    x = Convert.ToSingle(reader.Value);
                    break;
                case "y":
                    y = Convert.ToSingle(reader.Value);
                    break;
                case "z":
                    z = Convert.ToSingle(reader.Value);
                    break;
                default:
                    SkipPropertyValue(reader);
                    break;
            }
        }

        return new Vector3(x, y, z);
    }

    private static void SkipPropertyValue(JsonReader reader)
    {
        if (reader.TokenType == JsonToken.StartObject || reader.TokenType == JsonToken.StartArray)
        {
            var depth = reader.Depth;
            while (reader.Read() && reader.Depth > depth)
            {
            }
        }
    }
}
