using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KabuUnityMcp.Editor.Services
{
    /// <summary>
    /// Console log source for MCP console-get-logs.
    /// Reads the Editor Console entry list. The callback buffer is only a fallback
    /// when <c>LogEntries</c> is unavailable; it does not see compile warnings
    /// already sitting in the Console.
    /// </summary>
    [InitializeOnLoad]
    internal static class McpLogCollector
    {
        public sealed class Entry
        {
            public string Message;
            public string StackTrace;
            public LogType Type;
            public DateTime UtcTime;
        }

        private const int Capacity = 2000;
        private static readonly object Gate = new object();
        private static readonly List<Entry> Entries = new List<Entry>(Capacity);

        static McpLogCollector()
        {
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                Application.logMessageReceived -= OnLog;
            };
        }

        public static void Clear()
        {
            lock (Gate)
                Entries.Clear();
            EditorConsoleReader.Clear();
        }

        public static List<Entry> Query(int maxEntries, string typeFilter, bool includeStackTrace, int lastMinutes)
        {
            if (maxEntries < 1)
                maxEntries = 1;

            var consoleEntries = EditorConsoleReader.TryQuery(maxEntries, typeFilter, includeStackTrace);
            if (consoleEntries != null)
                return consoleEntries;

            DateTime? since = null;
            if (lastMinutes > 0)
                since = DateTime.UtcNow.AddMinutes(-lastMinutes);

            LogType? filterType = null;
            if (!string.IsNullOrWhiteSpace(typeFilter) &&
                Enum.TryParse(typeFilter.Trim(), true, out LogType parsed))
                filterType = parsed;

            lock (Gate)
            {
                var result = new List<Entry>(Math.Min(maxEntries, Entries.Count));
                for (var i = Entries.Count - 1; i >= 0 && result.Count < maxEntries; i--)
                {
                    var e = Entries[i];
                    if (since.HasValue && e.UtcTime < since.Value)
                        continue;
                    if (filterType.HasValue && e.Type != filterType.Value)
                        continue;

                    result.Add(new Entry
                    {
                        Message = e.Message,
                        StackTrace = includeStackTrace ? e.StackTrace : null,
                        Type = e.Type,
                        UtcTime = e.UtcTime
                    });
                }

                result.Reverse();
                return result;
            }
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            lock (Gate)
            {
                if (Entries.Count >= Capacity)
                    Entries.RemoveAt(0);

                Entries.Add(new Entry
                {
                    Message = condition,
                    StackTrace = stackTrace,
                    Type = type,
                    UtcTime = DateTime.UtcNow
                });
            }
        }
    }
}
