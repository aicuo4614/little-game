using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace KabuUnityMcp.Editor.Services
{
    /// <summary>
    /// Reads the Editor Console window via internal <c>UnityEditor.LogEntries</c>.
    /// Compile and import warnings live there, and are not replayed through
    /// <see cref="Application.logMessageReceived"/>.
    /// </summary>
    internal static class EditorConsoleReader
    {
        const int ModeError = 1 << 0;
        const int ModeAssert = 1 << 1;
        const int ModeFatal = 1 << 4;
        const int ModeAssetImportError = 1 << 6;
        const int ModeAssetImportWarning = 1 << 7;
        const int ModeScriptingError = 1 << 8;
        const int ModeScriptingWarning = 1 << 9;
        const int ModeScriptCompileError = 1 << 11;
        const int ModeScriptCompileWarning = 1 << 12;
        const int ModeScriptingException = 1 << 17;
        const int ModeGraphCompileError = 1 << 20;
        const int ModeScriptingAssertion = 1 << 21;
        const int ModeVisualScriptingError = 1 << 22;

        static readonly BindingFlags StaticFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        static bool discovered;
        static Type logEntryType;
        static MethodInfo startMethod;
        static MethodInfo endMethod;
        static MethodInfo getCountMethod;
        static MethodInfo getEntryMethod;
        static MethodInfo clearMethod;
        static FieldInfo messageField;
        static FieldInfo fileField;
        static FieldInfo lineField;
        static FieldInfo columnField;
        static FieldInfo modeField;
        static FieldInfo stackStartField;

        /// <summary>
        /// Current Console rows, newest capped by <paramref name="maxEntries"/>.
        /// Returns null when this Unity build has no usable LogEntries API.
        /// </summary>
        public static List<McpLogCollector.Entry> TryQuery(int maxEntries, string typeFilter, bool includeStackTrace)
        {
            EnsureDiscovered();
            if (getEntryMethod == null)
            {
                return null;
            }

            LogType? filterType = null;
            if (!string.IsNullOrWhiteSpace(typeFilter) &&
                Enum.TryParse(typeFilter.Trim(), true, out LogType parsed))
            {
                filterType = parsed;
            }

            startMethod.Invoke(null, null);
            try
            {
                var count = (int)getCountMethod.Invoke(null, null);
                var entry = Activator.CreateInstance(logEntryType);
                var args = new object[] { 0, entry };
                var result = new List<McpLogCollector.Entry>(Math.Min(maxEntries, Math.Max(count, 0)));
                for (var i = count - 1; i >= 0 && result.Count < maxEntries; i--)
                {
                    args[0] = i;
                    args[1] = entry;
                    if (!(bool)getEntryMethod.Invoke(null, args))
                    {
                        continue;
                    }

                    var filled = args[1];
                    var type = ToLogType((int)modeField.GetValue(filled));
                    if (filterType.HasValue && type != filterType.Value)
                    {
                        continue;
                    }

                    SplitMessage(filled, out var message, out var stack);
                    result.Add(new McpLogCollector.Entry
                    {
                        Message = message,
                        StackTrace = includeStackTrace ? stack : null,
                        Type = type,
                        UtcTime = DateTime.UtcNow
                    });
                }

                result.Reverse();
                return result;
            }
            finally
            {
                endMethod.Invoke(null, null);
            }
        }

        public static void Clear()
        {
            EnsureDiscovered();
            if (clearMethod != null)
            {
                clearMethod.Invoke(null, null);
                return;
            }

            Debug.ClearDeveloperConsole();
        }

        static void EnsureDiscovered()
        {
            if (discovered)
            {
                return;
            }

            discovered = true;
            var editorAssembly = typeof(UnityEditor.Editor).Assembly;
            var logEntriesType = editorAssembly.GetType("UnityEditor.LogEntries");
            logEntryType = editorAssembly.GetType("UnityEditor.LogEntry");
            if (logEntriesType == null || logEntryType == null)
            {
                return;
            }

            startMethod = logEntriesType.GetMethod("StartGettingEntries", StaticFlags);
            endMethod = logEntriesType.GetMethod("EndGettingEntries", StaticFlags);
            getCountMethod = logEntriesType.GetMethod("GetCount", StaticFlags);
            getEntryMethod = logEntriesType.GetMethod("GetEntryInternal", StaticFlags);
            clearMethod = logEntriesType.GetMethod("Clear", StaticFlags);
            messageField = logEntryType.GetField("message");
            fileField = logEntryType.GetField("file");
            lineField = logEntryType.GetField("line");
            columnField = logEntryType.GetField("column");
            modeField = logEntryType.GetField("mode");
            stackStartField = logEntryType.GetField("callstackTextStartUTF16");

            if (startMethod == null || endMethod == null || getCountMethod == null ||
                getEntryMethod == null || messageField == null || modeField == null ||
                fileField == null || lineField == null || columnField == null)
            {
                getEntryMethod = null;
            }
        }

        static void SplitMessage(object filled, out string message, out string stack)
        {
            var raw = messageField.GetValue(filled) as string ?? "";
            var stackStart = stackStartField != null ? (int)stackStartField.GetValue(filled) : 0;
            if (stackStart > 0 && stackStart < raw.Length)
            {
                message = raw.Substring(0, stackStart).TrimEnd('\r', '\n');
                stack = raw.Substring(stackStart);
            }
            else
            {
                message = raw;
                stack = null;
            }

            var file = fileField.GetValue(filled) as string;
            if (string.IsNullOrEmpty(file) || message.IndexOf(file, StringComparison.Ordinal) >= 0)
            {
                return;
            }

            var line = (int)lineField.GetValue(filled);
            var column = (int)columnField.GetValue(filled);
            var location = column > 0 ? file + "(" + line + "," + column + ")" : file + "(" + line + ")";
            message = string.IsNullOrEmpty(message) ? location : location + ": " + message;
        }

        static LogType ToLogType(int mode)
        {
            if ((mode & ModeScriptingException) != 0)
            {
                return LogType.Exception;
            }

            if ((mode & (ModeAssert | ModeScriptingAssertion)) != 0)
            {
                return LogType.Assert;
            }

            const int errorMask = ModeError | ModeFatal | ModeAssetImportError | ModeScriptingError |
                                  ModeScriptCompileError | ModeGraphCompileError | ModeVisualScriptingError;
            if ((mode & errorMask) != 0)
            {
                return LogType.Error;
            }

            const int warningMask = ModeAssetImportWarning | ModeScriptingWarning | ModeScriptCompileWarning;
            if ((mode & warningMask) != 0)
            {
                return LogType.Warning;
            }

            return LogType.Log;
        }
    }
}
