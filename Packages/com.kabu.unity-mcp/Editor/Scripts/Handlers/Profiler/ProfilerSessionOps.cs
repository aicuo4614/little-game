using System;
using System.Collections.Generic;
using System.IO;
using UnityEditorInternal;
using UnityEngine.Profiling;
using UProfiler = UnityEngine.Profiling.Profiler;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    /// <summary>Profiler 开关、区域、清空录制帧。</summary>
    internal static class ProfilerSessionOps
    {
        private static readonly string[] AreaNames = Enum.GetNames(typeof(ProfilerArea));

        public static HandlerResult Enable(
            string logFile,
            bool enableCallstacks,
            IDictionary<string, bool> areas)
        {
            if (areas != null)
            {
                var set = ApplyAreas(areas);
                if (!set.Ok)
                    return set;
            }

            UProfiler.enabled = true;
            // Editor 侧 Hierarchy 依赖 ProfilerDriver 录制；仅 Profiler.enabled 常不够
            try
            {
                ProfilerDriver.enabled = true;
            }
            catch
            {
                // ignore older API gaps
            }

            if (!string.IsNullOrWhiteSpace(logFile))
            {
                var path = logFile.Trim();
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    try
                    {
                        Directory.CreateDirectory(dir);
                    }
                    catch (Exception ex)
                    {
                        return HandlerResult.Fail($"无法创建 log 目录 '{dir}'：{ex.Message}");
                    }
                }

                UProfiler.logFile = path;
                UProfiler.enableBinaryLog = true;
            }

            if (enableCallstacks)
                UProfiler.enableAllocationCallstacks = true;

            return HandlerResult.Success(BuildStatusPayload("Profiler 已启用。副作用：会增加编辑器/Play 开销；用完请 disable。"));
        }

        public static HandlerResult Disable()
        {
            var previousLog = UProfiler.enableBinaryLog ? UProfiler.logFile : null;
            UProfiler.enableBinaryLog = false;
            UProfiler.enableAllocationCallstacks = false;
            UProfiler.enabled = false;
            try
            {
                ProfilerDriver.enabled = false;
            }
            catch
            {
                // ignore
            }

            return HandlerResult.Success(new
            {
                message = "Profiler 已关闭。",
                enabled = false,
                previousLogFile = previousLog
            });
        }

        public static HandlerResult Status()
        {
            return HandlerResult.Success(BuildStatusPayload("Profiler 状态。"));
        }

        public static HandlerResult SetAreas(IDictionary<string, bool> areas)
        {
            if (areas == null || areas.Count == 0)
            {
                return HandlerResult.Fail(
                    $"请提供 areas 对象（areaName → bool）。有效区域：{string.Join(", ", AreaNames)}",
                    -32602);
            }

            var set = ApplyAreas(areas);
            if (!set.Ok)
                return set;

            return HandlerResult.Success(new
            {
                message = "已更新 Profiler 区域。",
                areas = CollectAreas()
            });
        }

        /// <summary>
        /// 确保 Editor ProfilerDriver 正在录帧（打开 Profiler 窗口 + Driver.enabled）。
        /// 不清空已有帧：ClearAllFrames 后在部分环境下会一直 lastFrameIndex=-1。
        /// </summary>
        public static HandlerResult EnsureDriverRecording()
        {
            UProfiler.enabled = true;
            try
            {
                ProfilerDriver.enabled = true;
            }
            catch (Exception ex)
            {
                return HandlerResult.Fail("无法设置 ProfilerDriver.enabled：" + ex.Message);
            }

            var opened = false;
            try
            {
                // 2022+ 菜单；失败再试旧路径
                opened = UnityEditor.EditorApplication.ExecuteMenuItem("Window/Analysis/Profiler")
                         || UnityEditor.EditorApplication.ExecuteMenuItem("Window/Profiler");
            }
            catch
            {
                opened = false;
            }

            // 窗口打开后可能覆盖 recording state，再设一次
            try
            {
                ProfilerDriver.enabled = true;
            }
            catch
            {
                // ignore
            }

            UProfiler.enabled = true;

            return HandlerResult.Success(new
            {
                message = opened
                    ? "已确保 ProfilerDriver 录制（并尝试打开 Profiler 窗口）。"
                    : "已设置 ProfilerDriver.enabled（打开 Profiler 窗口失败，仍继续）。",
                profilerWindowOpened = opened,
                profilerEnabled = UProfiler.enabled,
                driverEnabled = true,
                firstFrameIndex = ProfilerDriver.firstFrameIndex,
                lastFrameIndex = ProfilerDriver.lastFrameIndex
            });
        }

        public static HandlerResult Clear()
        {
            try
            {
                ProfilerDriver.ClearAllFrames();
            }
            catch (Exception ex)
            {
                return HandlerResult.Fail($"清空 Profiler 帧失败：{ex.Message}");
            }

            return HandlerResult.Success(new
            {
                message = "已清空 Profiler 录制帧。",
                firstFrameIndex = ProfilerDriver.firstFrameIndex,
                lastFrameIndex = ProfilerDriver.lastFrameIndex
            });
        }

        private static HandlerResult ApplyAreas(IDictionary<string, bool> areas)
        {
            var updated = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in areas)
            {
                if (!Enum.TryParse(kv.Key, true, out ProfilerArea area))
                {
                    return HandlerResult.Fail(
                        $"未知 area '{kv.Key}'。有效：{string.Join(", ", AreaNames)}",
                        -32602);
                }

                UProfiler.SetAreaEnabled(area, kv.Value);
                updated[kv.Key] = kv.Value;
            }

            return HandlerResult.Success(updated);
        }

        private static Dictionary<string, bool> CollectAreas()
        {
            var areas = new Dictionary<string, bool>(AreaNames.Length);
            foreach (var name in AreaNames)
            {
                if (Enum.TryParse(name, out ProfilerArea area))
                    areas[name] = UProfiler.GetAreaEnabled(area);
            }

            return areas;
        }

        private static object BuildStatusPayload(string message)
        {
            var first = ProfilerDriver.firstFrameIndex;
            var last = ProfilerDriver.lastFrameIndex;
            var hasFrames = first >= 0 && last >= first;
            return new
            {
                message,
                enabled = UProfiler.enabled,
                // recording = binary log；勿理解为「是否已有 Hierarchy 帧」
                recording = UProfiler.enableBinaryLog,
                binaryLogEnabled = UProfiler.enableBinaryLog,
                hasFrames,
                logFile = UProfiler.enableBinaryLog ? UProfiler.logFile : null,
                allocationCallstacks = UProfiler.enableAllocationCallstacks,
                firstFrameIndex = first,
                lastFrameIndex = last,
                areas = CollectAreas()
            };
        }
    }
}
