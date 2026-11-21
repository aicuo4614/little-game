using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KabuUnityMcp.Editor.Extensions;
using KabuUnityMcp.Editor.Models;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using KabuUnityMcp;

namespace KabuUnityMcp.Editor.Services
{
    /// <summary>
    /// Per-project JSON at Assets/kabu-unity-mcp/settings.json (port + tool toggles).
    /// </summary>
    internal static class McpProjectSettingsStore
    {
        private const string Folder = "Assets/kabu-unity-mcp";
        public const string JsonPath = Folder + "/settings.json";

        private static McpLocalSettingsData _cached;

        public static McpLocalSettingsData GetOrCreate()
        {
            if (_cached != null)
                return _cached;

            EnsureFolder();

            if (File.Exists(JsonPath))
            {
                try
                {
                    var text = File.ReadAllText(JsonPath);
                    _cached = JsonConvert.DeserializeObject<McpLocalSettingsData>(text)
                              ?? new McpLocalSettingsData();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[kabu-unity-mcp] Failed to read {JsonPath}: {ex.Message}, using defaults.");
                    _cached = new McpLocalSettingsData();
                }
            }
            else
            {
                _cached = new McpLocalSettingsData();
            }

            Normalize(_cached);
            Save(); // write merged defaults so file exists in project
            return _cached;
        }

        public static void Save()
        {
            if (_cached == null)
                _cached = new McpLocalSettingsData();

            Normalize(_cached);
            var absolute = EnsureFolder();
            var json = JsonConvert.SerializeObject(_cached, Formatting.Indented);
            File.WriteAllText(Path.Combine(absolute, "settings.json"), json);
            AssetDatabase.ImportAsset(JsonPath);
        }

        public static void InvalidateCache() => _cached = null;

        public static bool IsToolEnabled(string toolName)
        {
            var data = GetOrCreate();
            if (data.tools != null && data.tools.TryGetValue(toolName, out var enabled))
                return enabled;
            return true;
        }

        public static void SetToolEnabled(string toolName, bool enabled)
        {
            var data = GetOrCreate();
            data.tools[toolName] = enabled;
            Save();
        }

        public static void SetPort(int port)
        {
            var data = GetOrCreate();
            data.port = Mathf.Clamp(port, 1024, 65535);
            Save();
        }

        public static IReadOnlyList<string> GetEnabledToolNames()
        {
            var hasSkillEditor = McpSkillEditorHostRegistry.HasHost;
            return McpToolCatalog.All
                .Where(t => IsToolEnabled(t.Name))
                .Where(t => hasSkillEditor || !t.Name.StartsWith("unity_skill_editor_", StringComparison.Ordinal))
                .Select(t => t.Name)
                .ToList();
        }

        private static void Normalize(McpLocalSettingsData data)
        {
            if (data.port < 1024 || data.port > 65535)
                data.port = McpEndpoints.DefaultPort;
            if (string.IsNullOrWhiteSpace(data.host))
                data.host = McpEndpoints.DefaultHost;
            if (!data.autoConnectOnEditorStart.HasValue)
                data.autoConnectOnEditorStart = true;
            if (data.tools == null)
                data.tools = new Dictionary<string, bool>();

            foreach (var entry in McpToolCatalog.All)
            {
                if (!data.tools.ContainsKey(entry.Name))
                    data.tools[entry.Name] = true;
            }
        }

        /// <summary>冷启动是否自动启服并连接（缺省 true）。</summary>
        public static bool IsAutoConnectOnEditorStart()
        {
            return GetOrCreate().autoConnectOnEditorStart ?? true;
        }

        public static void SetAutoConnectOnEditorStart(bool enabled)
        {
            var data = GetOrCreate();
            data.autoConnectOnEditorStart = enabled;
            Save();
        }

        /// <summary>Creates Assets/kabu-unity-mcp on disk. Returns the absolute folder path.</summary>
        private static string EnsureFolder()
        {
            var absolute = Path.Combine(Application.dataPath, "kabu-unity-mcp");
            Directory.CreateDirectory(absolute);
            return absolute;
        }
    }
}
