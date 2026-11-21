using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    /// <summary>
    /// Memory Snapshot：拍/列/文件级对比。
    /// Take 通过反射调用 MemoryProfiler.TakeSnapshot（不强制包依赖）；
    /// 对比返回文件元数据摘要，不做整文件对象图 dump。
    /// </summary>
    internal static class ProfilerMemorySnapshotOps
    {
        private static Type _memoryProfilerType;
        private static bool _discovered;

        public static bool Available
        {
            get
            {
                EnsureDiscovered();
                return _memoryProfilerType != null;
            }
        }

        public static string UnavailableReason
        {
            get
            {
                EnsureDiscovered();
                return _memoryProfilerType == null
                    ? "MemoryProfiler.TakeSnapshot is unavailable in this Unity version. The Memory Profiler package is optional for capturing, and needed for inspecting .snap files."
                    : null;
            }
        }

        public static string DefaultDirectory
        {
            get
            {
                var dir = Path.Combine(Application.temporaryCachePath, "MemoryCaptures");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static bool IsBusy { get; private set; }
        private static int callbackArrived;
        private static string completedPath;
        private static bool completedOk;
        private static Action<HandlerResult> completion;
        private static double deadline;
        private static object captureTarget;
        private static string captureStartedUtc;
        private static int captureFlags;
        private static int captureTargetId;

        // Shared by MCP and the test panel. Unity's native capture cannot be cancelled.
        public static bool TryBeginTake(string snapshotPath, Action<HandlerResult> onDone, out string error,
            int? expectedTargetId = null, int flags = 7, int timeoutSeconds = 180)
        {
            error = null;
            if (IsBusy) { error = "A native memory capture is still running (including after a request timeout)."; return false; }
            if (flags <= 0 || (flags & ~31) != 0) { error = "captureFlags must be a nonzero combination of bits 1, 2, 4, 8 and 16."; return false; }
            if (timeoutSeconds < 10 || timeoutSeconds > 600) { error = "timeoutSeconds must be between 10 and 600."; return false; }
            if (expectedTargetId.HasValue && expectedTargetId.Value != ProfilerDriver.connectedProfiler)
            { error = "Profiler target changed. Refresh targets and select the intended target before capturing."; return false; }
            EnsureDiscovered();
            if (!Available) { error = UnavailableReason; return false; }
            try
            {
                snapshotPath = NormalizeCapturePath(snapshotPath);
                Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath));
                captureTargetId = ProfilerDriver.connectedProfiler;
                captureTarget = ProfilerMemoryTargetOps.Current();
                captureStartedUtc = DateTime.UtcNow.ToString("o");
                captureFlags = flags;
                completion = onDone;
                deadline = EditorApplication.timeSinceStartup + timeoutSeconds;
                Interlocked.Exchange(ref callbackArrived, 0);
                IsBusy = true;
                EditorApplication.update += PollCapture;
                if (TryInvokeTakeSnapshot(snapshotPath, (path, ok) =>
                {
                    // Native callback may run on a worker thread. All Unity/file/result work is polled on the main thread.
                    completedPath = path;
                    completedOk = ok;
                    Interlocked.Exchange(ref callbackArrived, 1);
                }, out error, flags)) return true;
            }
            catch (Exception ex) { error = "Could not start memory capture: " + ex.GetBaseException().Message; }
            ResetCapture();
            return false;
        }

        internal static string NormalizeCapturePath(string path)
        {
            path = string.IsNullOrWhiteSpace(path)
                ? Path.Combine(DefaultDirectory, $"snapshot_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.snap")
                : path.Trim();
            path = Path.GetFullPath(path);
            if (!string.Equals(Path.GetExtension(path), ".snap", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("snapshotPath must end in .snap.");
            if (File.Exists(path) || File.Exists(path + ".mcp.json"))
                throw new IOException("Snapshot already exists. Choose a new path to preserve existing captures.");
            return path;
        }

        private static void PollCapture()
        {
            if (Interlocked.CompareExchange(ref callbackArrived, 0, 0) == 1)
            {
                var done = completion;
                HandlerResult result;
                try
                {
                    var file = string.IsNullOrWhiteSpace(completedPath) ? null : new FileInfo(completedPath);
                    if (!completedOk || file == null || !file.Exists || file.Length == 0)
                        result = HandlerResult.Fail("Memory capture failed or produced no data. Check the selected Development Player connection.");
                    else
                    {
                        var warnings = new List<string>();
                        if (ProfilerDriver.connectedProfiler != captureTargetId)
                            warnings.Add("Profiler target changed during capture; verify the snapshot target in Memory Profiler.");
                        var metadata = new
                        {
                            schemaVersion = 1, target = captureTarget, startedUtc = captureStartedUtc,
                            completedUtc = DateTime.UtcNow.ToString("o"), captureFlags,
                            requestingEditorUnityVersion = Application.unityVersion,
                            sizeBytes = file.Length,
                            targetChangedDuringCapture = ProfilerDriver.connectedProfiler != captureTargetId
                        };
                        try { File.WriteAllText(file.FullName + ".mcp.json", JsonConvert.SerializeObject(metadata, Formatting.Indented)); }
                        catch (Exception ex) { warnings.Add("Snapshot saved, but metadata could not be saved: " + ex.Message); }
                        result = HandlerResult.Success(new
                        {
                            path = file.FullName, sizeBytes = file.Length,
                            sizeMb = Math.Round(file.Length / (1024.0 * 1024.0), 2),
                            metadata, warnings
                        });
                    }
                }
                catch (Exception ex) { result = HandlerResult.Fail("Could not finalize memory capture: " + ex.Message); }
                ResetCapture();
                done?.Invoke(result);
            }
            else if (completion != null && EditorApplication.timeSinceStartup >= deadline)
            {
                var done = completion;
                completion = null;
                done(HandlerResult.Fail("Memory capture timed out. Native capture cannot be cancelled; it remains busy until its callback arrives. A late successful capture will still be saved and listed."));
            }
        }

        private static void ResetCapture()
        {
            EditorApplication.update -= PollCapture;
            IsBusy = false;
            completion = null;
            completedPath = null;
            Interlocked.Exchange(ref callbackArrived, 0);
        }

        public static HandlerResult List(string searchPath, int offset, int limit)
        {
            offset = Math.Max(0, offset);
            limit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 200);
            var dirs = (string.IsNullOrWhiteSpace(searchPath)
                ? new[] { DefaultDirectory, Path.Combine(Application.dataPath, "..", "MemoryCaptures") }
                : new[] { searchPath.Trim() }).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var files = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            var warnings = new List<string>();
            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    foreach (var path in Directory.GetFiles(dir, "*.snap"))
                    {
                        var fi = new FileInfo(path);
                        files[fi.FullName] = fi;
                    }
                }
                catch (Exception ex) { warnings.Add(dir + ": " + ex.Message); }
            }
            var ordered = files.Values.OrderByDescending(f => f.CreationTimeUtc)
                .ThenBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).ToArray();
            var page = ordered.Skip(offset).Take(limit).Select(DescribeFile).ToArray();
            var next = (long)offset + page.Length;
            return HandlerResult.Success(new
            {
                searchedDirs = dirs, total = ordered.Length, returned = page.Length, offset, limit,
                nextOffset = next < ordered.Length ? (int?)next : null, snapshots = page, warnings
            });
        }

        public static HandlerResult Compare(string snapshotA, string snapshotB)
        {
            if (string.IsNullOrWhiteSpace(snapshotA) || string.IsNullOrWhiteSpace(snapshotB))
            {
                return HandlerResult.Fail("请提供 snapshotA 与 snapshotB 路径。", -32602);
            }

            snapshotA = snapshotA.Trim();
            snapshotB = snapshotB.Trim();
            if (!File.Exists(snapshotA))
                return HandlerResult.Fail($"找不到 snapshotA：{snapshotA}");
            if (!File.Exists(snapshotB))
                return HandlerResult.Fail($"找不到 snapshotB：{snapshotB}");

            var fiA = new FileInfo(snapshotA);
            var fiB = new FileInfo(snapshotB);
            var deltaBytes = fiB.Length - fiA.Length;

            return HandlerResult.Success(new
            {
                message = "Snapshot 文件级对比（摘要）。对象级差异请在 Memory Profiler 窗口打开两份 .snap。",
                analysisKind = "fileMetadataOnly",
                objectAnalysisAvailable = false,
                warning = "Snapshot file size is not allocated memory and cannot establish a leak. Inspect object/type/reference data in Memory Profiler.",
                snapshotA = DescribeFile(fiA),
                snapshotB = DescribeFile(fiB),
                delta = new
                {
                    sizeDeltaBytes = deltaBytes,
                    sizeDeltaMb = Math.Round(deltaBytes / (1024.0 * 1024.0), 3),
                    timeDeltaSeconds = (fiB.CreationTimeUtc - fiA.CreationTimeUtc).TotalSeconds
                },
                note = "MCP 不解析完整对象图，避免把整份 snapshot 喂给模型。"
            });
        }

        private static object DescribeFile(FileInfo fi) => new
        {
            path = fi.FullName,
            sizeBytes = fi.Length,
            sizeMb = Math.Round(fi.Length / (1024.0 * 1024.0), 2),
            createdUtc = fi.CreationTimeUtc.ToString("o"),
            captureMetadata = ReadMetadata(fi)
        };

        private static object ReadMetadata(FileInfo snapshot)
        {
            var metadata = new FileInfo(snapshot.FullName + ".mcp.json");
            if (!metadata.Exists) return null;
            try
            {
                if (metadata.Length > 65536) return new { warning = "Capture metadata exceeds 64 KiB; ignored." };
                var value = JObject.Parse(File.ReadAllText(metadata.FullName));
                if (value["sizeBytes"]?.Value<long?>() != snapshot.Length)
                    return new { warning = "Capture metadata size does not match snapshot; ignored." };
                return value;
            }
            catch (Exception ex) { return new { warning = "Unreadable capture metadata: " + ex.Message }; }
        }

        private static void EnsureDiscovered()
        {
            if (_discovered)
                return;
            _discovered = true;
            _memoryProfilerType =
                Type.GetType("Unity.Profiling.Memory.MemoryProfiler, UnityEngine.CoreModule")
                ?? Type.GetType("UnityEngine.Profiling.Memory.Experimental.MemoryProfiler, UnityEngine.CoreModule")
                ?? Type.GetType("Unity.MemoryProfiler.MemoryProfiler, Unity.MemoryProfiler.Editor");
        }

        private static bool TryInvokeTakeSnapshot(string path, Action<string, bool> callback, out string error, int requestedFlags)
        {
            error = null;
            var debugScreenCaptureType =
                Type.GetType("Unity.Profiling.DebugScreenCapture, UnityEngine.CoreModule")
                ?? Type.GetType("UnityEngine.Profiling.Experimental.DebugScreenCapture, UnityEngine.CoreModule");
            var captureFlagsType =
                Type.GetType("Unity.Profiling.Memory.CaptureFlags, UnityEngine.CoreModule")
                ?? Type.GetType("UnityEngine.Profiling.Memory.Experimental.CaptureFlags, UnityEngine.CoreModule");

            MethodInfo takeMethod = null;
            object[] args = null;
            // ManagedObjects | NativeObjects | NativeAllocations（勿用 0，否则几乎空包）
            var DefaultCaptureFlags = (uint)requestedFlags;

            // Prefer (string, Action<string,bool>, CaptureFlags)
            if (captureFlagsType != null)
            {
                takeMethod = _memoryProfilerType.GetMethod(
                    "TakeSnapshot",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(Action<string, bool>), captureFlagsType },
                    null);
                if (takeMethod != null)
                {
                    var flags = Enum.ToObject(captureFlagsType, DefaultCaptureFlags);
                    args = new object[] { path, callback, flags };
                }
            }

            // (string, Action<string,bool>, Action<string,bool,DebugScreenCapture>, CaptureFlags)
            if (takeMethod == null && debugScreenCaptureType != null && captureFlagsType != null)
            {
                var screenshotCb = typeof(Action<,,>).MakeGenericType(
                    typeof(string), typeof(bool), debugScreenCaptureType);
                takeMethod = _memoryProfilerType.GetMethod(
                    "TakeSnapshot",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(Action<string, bool>), screenshotCb, captureFlagsType },
                    null);
                if (takeMethod != null)
                {
                    var flags = Enum.ToObject(captureFlagsType, DefaultCaptureFlags);
                    args = new object[] { path, callback, null, flags };
                }
            }

            if (takeMethod == null || args == null)
            {
                error = "找不到兼容的 MemoryProfiler.TakeSnapshot 重载。";
                return false;
            }

            takeMethod.Invoke(null, args);
            return true;
        }
    }
}
