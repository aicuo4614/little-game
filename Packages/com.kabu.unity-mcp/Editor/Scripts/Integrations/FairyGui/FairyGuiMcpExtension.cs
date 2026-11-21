using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KabuUnityMcp.Editor.Extensions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using R = KabuUnityMcp.Editor.Integrations.OptionalIntegrationReflection;

namespace KabuUnityMcp.Editor.Integrations.FairyGui
{
    /// <summary>Runtime UI inspection; authoring and publishing remain in the FairyGUI editor bridge.</summary>
    internal sealed class FairyGuiMcpExtension : IMcpExtension
    {
        public string Id => "fairygui";
        public string Version => "1.0.0";
        public string Description => "Inspect the existing FairyGUI runtime tree and controllers; never creates GRoot or UI.";
        public string UnavailableReason => R.FindType("FairyGUI.GRoot") == null ? "FairyGUI is not loaded in this Editor." : null;
        public McpExtensionOperation[] Operations => new[]
        {
            new McpExtensionOperation("tree", "Read runtime UI nodes by child-index path (root is empty, e.g. 0/2). Depth 0..6; limit 1..200. Duplicate names are unambiguous. Only realized virtual-list children are reported.", true, Schema()),
            new McpExtensionOperation("controllers", "Read controller names, selected pages and indices at uiPath.", true, Schema())
        };

        [InitializeOnLoadMethod]
        private static void Register() => McpExtensionRegistry.Register(new FairyGuiMcpExtension());

        private static JObject Schema() => new JObject
        {
            ["type"] = "object", ["additionalProperties"] = false,
            ["properties"] = new JObject
            {
                ["uiPath"] = new JObject { ["type"] = "string" },
                ["depth"] = new JObject { ["type"] = "integer" },
                ["offset"] = new JObject { ["type"] = "integer" },
                ["limit"] = new JObject { ["type"] = "integer" }
            }
        };

        public McpHostResult Execute(string operation, JObject args)
        {
            var limit = R.Limit(args);
            var offset = args["offset"]?.Value<int>() ?? 0;
            var depth = args["depth"]?.Value<int>() ?? 2;
            if (depth < 0 || depth > 6)
            {
                throw new ArgumentException("depth must be 0..6.");
            }
            var rootType = R.FindType("FairyGUI.GRoot");
            // GRoot.inst creates Stage. Read the existing root backing field to keep inspection read-only.
            var rootField = rootType.GetField("_inst", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (rootField == null)
            {
                return McpHostResult.Fail("Unsupported FairyGUI version: existing GRoot cannot be inspected without initialization.");
            }
            var node = rootField.GetValue(null);
            if (node == null)
            {
                return McpHostResult.Fail("FairyGUI GRoot is not initialized. Open the application UI first.");
            }
            var path = args["uiPath"]?.Value<string>() ?? "";
            if (path.Length > 512)
            {
                throw new ArgumentException("uiPath exceeds 512 characters.");
            }
            foreach (var segment in path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var count = Convert.ToInt32(R.Get(node, "numChildren"));
                if (!int.TryParse(segment, out var index) || index < 0 || index >= count)
                {
                    throw new ArgumentException("Invalid uiPath child index: " + segment);
                }
                node = node.GetType().GetMethod("GetChildAt", new[] { typeof(int) }).Invoke(node, new object[] { index });
            }
            if (operation == "controllers")
            {
                var values = R.Items(R.Get(node, "Controllers")).Select(item => (object)new
                {
                    name = R.Get(item, "name"), selectedIndex = R.Get(item, "selectedIndex"), selectedPage = R.Get(item, "selectedPage"), pageCount = R.Get(item, "pageCount")
                }).ToArray();
                return McpHostResult.Success(new { total = values.Length, items = values.Skip(offset).Take(limit).ToArray(),
                    nextOffset = offset + limit < values.Length ? offset + limit : (int?)null });
            }
            var rows = new List<object>();
            var skipped = 0;
            Visit(node, path.Trim('/'), depth, offset, limit, rows, ref skipped);
            var more = rows.Count > limit;
            if (more)
            {
                rows.RemoveAt(rows.Count - 1);
            }
            return McpHostResult.Success(new { schema = "kabu.fairygui.tree/v1", uiPath = path, depth, offset,
                nodes = rows, nextOffset = more ? offset + rows.Count : (int?)null,
                note = "Runtime realized objects only. UI paths are child indices and may change when the UI changes." });
        }

        private static void Visit(object node, string path, int depth, int offset, int limit, List<object> rows, ref int skipped)
        {
            if (rows.Count > limit)
            {
                return;
            }
            if (skipped++ >= offset)
            {
                var text = R.Get(node, "text") as string;
                rows.Add(new { uiPath = path, id = R.Get(node, "id"), name = R.Get(node, "name"), type = node.GetType().FullName,
                    x = R.Get(node, "x"), y = R.Get(node, "y"), width = R.Get(node, "width"), height = R.Get(node, "height"),
                    visible = R.Get(node, "visible"), touchable = R.Get(node, "touchable"), grayed = R.Get(node, "grayed"),
                    text = text?.Substring(0, Math.Min(text.Length, 512)),
                    numChildren = R.Get(node, "numChildren"), numItems = R.Get(node, "numItems") });
            }
            if (depth == 0 || rows.Count > limit)
            {
                return;
            }
            var count = Convert.ToInt32(R.Get(node, "numChildren"));
            var getChild = node.GetType().GetMethod("GetChildAt", new[] { typeof(int) });
            for (var index = 0; index < count && rows.Count <= limit; index++)
            {
                var child = getChild.Invoke(node, new object[] { index });
                Visit(child, string.IsNullOrEmpty(path) ? index.ToString() : path + "/" + index, depth - 1, offset, limit, rows, ref skipped);
            }
        }
    }
}
