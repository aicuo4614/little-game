using System.Text;
using System.Text.Json;

namespace KabuUnityMcp.Server.Tools.Unity.GameObjects;

internal static class GameObjectToolFormat
{
    public static string FormatInfo(string prefix, JsonElement? result)
    {
        if (result == null)
            return $"{prefix}，但 Unity 未返回数据。";

        var el = result.Value;
        if (el.TryGetProperty("gameObject", out var nested))
            el = nested;

        var n = el.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : "?";
        var path = el.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : "?";
        var id = el.TryGetProperty("instanceId", out var idEl) ? idEl.GetInt32().ToString() : "?";
        return $"{prefix}「{n}」\n路径：{path}\ninstanceId：{id}";
    }

    public static string FormatDetail(JsonElement? result)
    {
        if (result == null)
            return "Unity 未返回数据。";

        var el = result.Value;
        var sb = new StringBuilder();
        Append(sb, "名称", el, "name");
        Append(sb, "路径", el, "path");
        Append(sb, "instanceId", el, "instanceId");
        if (el.TryGetProperty("activeSelf", out var a))
            sb.AppendLine($"activeSelf：{a.GetBoolean()}");
        Append(sb, "tag", el, "tag");
        if (el.TryGetProperty("layer", out var layer))
            sb.AppendLine($"layer：{layer.GetInt32()}");
        if (el.TryGetProperty("position", out var pos))
            sb.AppendLine($"position：{Vec(pos)}");
        if (el.TryGetProperty("rotation", out var rot))
            sb.AppendLine($"rotation：{Vec(rot)}");
        if (el.TryGetProperty("localScale", out var scale))
            sb.AppendLine($"localScale：{Vec(scale)}");
        if (el.TryGetProperty("components", out var comps) && comps.ValueKind == JsonValueKind.Array)
        {
            sb.Append("组件：");
            var first = true;
            foreach (var c in comps.EnumerateArray())
            {
                if (!first) sb.Append(", ");
                sb.Append(c.GetString());
                first = false;
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static void Append(StringBuilder sb, string label, JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var v))
            sb.AppendLine($"{label}：{v}");
    }

    private static string Vec(JsonElement v) =>
        $"({v.GetProperty("x").GetSingle():0.###}, {v.GetProperty("y").GetSingle():0.###}, {v.GetProperty("z").GetSingle():0.###})";
}
