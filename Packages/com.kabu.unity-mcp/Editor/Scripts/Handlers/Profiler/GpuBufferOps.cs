using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using KabuUnityMcp.Editor.Extensions;
using KabuUnityMcp.Gpu;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    /// <summary>
    /// unity_gpu_list_buffers / unity_gpu_read_buffer：注册表发现 + AsyncGPUReadback 切片摘要。
    /// </summary>
    internal static class GpuBufferOps
    {
        public const string ListSchema = "kabu.gpu.buffer-list/v1";
        public const string ReadSchema = "kabu.gpu.buffer-read/v1";
        public const int DefaultLimit = 32;
        public const int MaxLimit = 256;
        public const int MaxListLimit = 200;

        public static HandlerResult List(int offset, int limit, string nameContains, bool refreshHost)
        {
            if (offset < 0) offset = 0;
            if (limit <= 0) limit = DefaultLimit;
            if (limit > MaxListLimit) limit = MaxListLimit;

            object hostRefresh = null;
            if (refreshHost && McpGpuDebugHostRegistry.HasHost)
            {
                var hr = McpGpuDebugHostRegistry.Current.Refresh();
                if (!hr.Ok)
                {
                    return HandlerResult.Fail(hr.Error ?? "GpuDebug Host Refresh 失败。", hr.ErrorCode);
                }

                hostRefresh = hr.Payload;
            }

            var pruned = McpGpuBufferRegistry.PruneDead();
            var all = McpGpuBufferRegistry.Snapshot();
            all.Sort((a, b) => string.CompareOrdinal(a?.Id, b?.Id));

            if (!string.IsNullOrWhiteSpace(nameContains))
            {
                var needle = nameContains.Trim();
                all.RemoveAll(e =>
                    e == null
                    || ((e.Id == null || e.Id.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                        && (e.Name == null || e.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                        && (e.OwnerPath == null || e.OwnerPath.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)));
            }

            var total = all.Count;
            if (offset > total) offset = total;
            var take = Math.Min(limit, total - offset);
            var items = new List<object>(take);
            for (var i = 0; i < take; i++)
            {
                items.Add(all[offset + i].ToListRow());
            }

            var next = offset + take;
            return HandlerResult.Success(new
            {
                schema = ListSchema,
                ok = true,
                hostRegistered = McpGpuDebugHostRegistry.HasHost,
                hostRefresh,
                prunedDead = pruned,
                total,
                returned = items.Count,
                offset,
                limit = take,
                nextOffset = next < total ? (int?)next : null,
                items
            });
        }

        public static HandlerResult ReadCpu(string bufferId, int offset, int limit)
        {
            if (string.IsNullOrWhiteSpace(bufferId))
            {
                return HandlerResult.Fail("请提供 bufferId（来自 unity_gpu_list_buffers）。", -32602);
            }

            if (!McpGpuDebugHostRegistry.HasHost)
            {
                return HandlerResult.Fail("GpuDebug Host 未注册，无法读 CPU 镜像。请在工程中注册 IMcpGpuDebugHost。");
            }

            NormalizeSlice(ref offset, ref limit);
            var hr = McpGpuDebugHostRegistry.Current.ReadCpuMirror(bufferId.Trim(), offset, limit);
            if (!hr.Ok)
            {
                return HandlerResult.Fail(hr.Error ?? "CPU 镜像读取失败。", hr.ErrorCode);
            }

            return HandlerResult.Success(hr.Payload);
        }

        public static bool TryBeginGpuRead(
            string bufferId,
            int offset,
            int limit,
            out AsyncGPUReadbackRequest request,
            out McpGpuBufferEntry entry,
            out string error)
        {
            request = default;
            entry = null;
            error = null;

            if (string.IsNullOrWhiteSpace(bufferId))
            {
                error = "请提供 bufferId（来自 unity_gpu_list_buffers）。";
                return false;
            }

            if (McpGpuDebugHostRegistry.HasHost)
            {
                var hr = McpGpuDebugHostRegistry.Current.Refresh();
                if (!hr.Ok)
                {
                    error = hr.Error ?? "GpuDebug Host Refresh 失败。";
                    return false;
                }
            }

            McpGpuBufferRegistry.PruneDead();
            if (!McpGpuBufferRegistry.TryGet(bufferId.Trim(), out entry) || entry == null)
            {
                error = "未找到 bufferId: " + bufferId;
                return false;
            }

            if (!entry.IsAlive)
            {
                error = "缓冲已失效: " + bufferId;
                return false;
            }

            NormalizeSlice(ref offset, ref limit);
            var count = entry.ResolvedCount;
            if (count <= 0)
            {
                error = "缓冲元素数为 0: " + bufferId;
                return false;
            }

            if (offset >= count)
            {
                error = "offset 超出 count=" + count;
                return false;
            }

            var take = Math.Min(limit, count - offset);
            var stride = entry.Stride > 0 ? entry.Stride : entry.Buffer.stride;
            if (stride <= 0)
            {
                error = "stride 无效: " + bufferId;
                return false;
            }

            var byteOffset = offset * stride;
            var byteSize = take * stride;
            try
            {
                request = AsyncGPUReadback.Request(entry.Buffer, byteSize, byteOffset);
            }
            catch (Exception ex)
            {
                error = "AsyncGPUReadback 发起失败: " + ex.Message;
                return false;
            }

            return true;
        }

        public static HandlerResult CompleteGpuRead(
            McpGpuBufferEntry entry,
            AsyncGPUReadbackRequest request,
            int offset,
            int limit)
        {
            if (request.hasError)
            {
                return HandlerResult.Fail(
                    "AsyncGPUReadback 失败（缓冲 target/平台可能不支持 readback）: " + (entry?.Id ?? "?"));
            }

            NormalizeSlice(ref offset, ref limit);
            var stride = entry.Stride > 0 ? entry.Stride : entry.Buffer.stride;
            var count = entry.ResolvedCount;
            var take = Math.Min(limit, Math.Max(0, count - offset));
            var data = request.GetData<byte>();
            if (!data.IsCreated || data.Length < take * stride)
            {
                return HandlerResult.Fail("readback 字节数不足: 期望 " + (take * stride) + "，实际 " + data.Length);
            }

            return HandlerResult.Success(BuildReadPayload(
                entry,
                source: "gpu",
                offset,
                take,
                count,
                stride,
                data));
        }

        public static object BuildReadPayload(
            McpGpuBufferEntry entry,
            string source,
            int offset,
            int returned,
            int totalCount,
            int stride,
            NativeArray<byte> data)
        {
            var stats = SummarizeBytes(data, stride, returned);
            var items = DecodeItems(entry?.Schema, data, stride, returned);

            return new
            {
                schema = ReadSchema,
                ok = true,
                source,
                id = entry?.Id,
                name = entry?.Name,
                ownerPath = entry?.OwnerPath,
                layoutSchema = entry?.Schema,
                stride,
                count = totalCount,
                offset,
                returned,
                nextOffset = offset + returned < totalCount ? (int?)(offset + returned) : null,
                stats,
                items,
                capturedAt = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture)
            };
        }

        public static object BuildCpuReadPayload(
            McpGpuBufferEntry entry,
            int offset,
            int returned,
            int totalCount,
            int stride,
            IList<object> items,
            object stats)
        {
            return new
            {
                schema = ReadSchema,
                ok = true,
                source = "cpu",
                id = entry?.Id,
                name = entry?.Name,
                ownerPath = entry?.OwnerPath,
                layoutSchema = entry?.Schema,
                stride,
                count = totalCount,
                offset,
                returned,
                nextOffset = offset + returned < totalCount ? (int?)(offset + returned) : null,
                stats,
                items,
                capturedAt = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture)
            };
        }

        public static void NormalizeSlice(ref int offset, ref int limit)
        {
            if (offset < 0) offset = 0;
            if (limit <= 0) limit = DefaultLimit;
            if (limit > MaxLimit) limit = MaxLimit;
        }

        internal static object SummarizeBytes(NativeArray<byte> data, int stride, int elementCount)
        {
            var floatCount = 0;
            var nanCount = 0;
            var infCount = 0;
            var min = float.PositiveInfinity;
            var max = float.NegativeInfinity;
            var absMax = 0f;

            for (var e = 0; e < elementCount; e++)
            {
                var baseIndex = e * stride;
                for (var o = 0; o + 4 <= stride; o += 4)
                {
                    var bits = data[baseIndex + o]
                        | (data[baseIndex + o + 1] << 8)
                        | (data[baseIndex + o + 2] << 16)
                        | (data[baseIndex + o + 3] << 24);
                    var f = IntBitsToFloat(bits);
                    floatCount++;
                    if (float.IsNaN(f))
                    {
                        nanCount++;
                        continue;
                    }

                    if (float.IsInfinity(f))
                    {
                        infCount++;
                        continue;
                    }

                    if (f < min) min = f;
                    if (f > max) max = f;
                    var abs = Mathf.Abs(f);
                    if (abs > absMax) absMax = abs;
                }
            }

            string hash;
            using (var md5 = MD5.Create())
            {
                var bytes = new byte[data.Length];
                data.CopyTo(bytes);
                hash = BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            }

            return new
            {
                floatCount,
                nanCount,
                infCount,
                min = float.IsInfinity(min) ? (float?)null : min,
                max = float.IsInfinity(max) ? (float?)null : max,
                absMax,
                md5 = hash
            };
        }

        internal static List<object> DecodeItems(string schema, NativeArray<byte> data, int stride, int count)
        {
            var items = new List<object>(count);
            for (var i = 0; i < count; i++)
            {
                var baseIndex = i * stride;
                items.Add(DecodeOne(schema, data, baseIndex, stride, i));
            }

            return items;
        }

        static object DecodeOne(string schema, NativeArray<byte> data, int baseIndex, int stride, int index)
        {
            if (string.Equals(schema, "kabu.gpu.skin-range/v1", StringComparison.Ordinal)
                && stride >= 16)
            {
                return new
                {
                    index,
                    Base = ReadInt(data, baseIndex),
                    Count = ReadInt(data, baseIndex + 4),
                    Start = ReadInt(data, baseIndex + 8),
                    Pad = ReadInt(data, baseIndex + 12)
                };
            }

            if (string.Equals(schema, "kabu.gpu.instance-seat/v1", StringComparison.Ordinal)
                && stride >= 16)
            {
                return new
                {
                    index,
                    seat = ReadFloat(data, baseIndex),
                    layer = ReadFloat(data, baseIndex + 4),
                    z = ReadFloat(data, baseIndex + 8),
                    w = ReadFloat(data, baseIndex + 12)
                };
            }

            if (string.Equals(schema, "kabu.gpu.skinned-vertex/v1", StringComparison.Ordinal)
                && stride >= 64)
            {
                return new
                {
                    index,
                    position = ReadFloat3(data, baseIndex),
                    normal = ReadFloat3(data, baseIndex + 12),
                    tangent = ReadFloat4(data, baseIndex + 24),
                    uv = ReadFloat2(data, baseIndex + 40),
                    seat = ReadFloat4(data, baseIndex + 48)
                };
            }

            if (string.Equals(schema, "kabu.gpu.batch-vertex/v1", StringComparison.Ordinal)
                && stride >= 96)
            {
                return new
                {
                    index,
                    position = ReadFloat3(data, baseIndex),
                    normal = ReadFloat3(data, baseIndex + 12),
                    tangent = ReadFloat4(data, baseIndex + 24),
                    uv = ReadFloat2(data, baseIndex + 40),
                    seat = ReadFloat4(data, baseIndex + 48),
                    bone01 = ReadFloat4(data, baseIndex + 64),
                    bone23 = ReadFloat4(data, baseIndex + 80)
                };
            }

            // 通用：按 float 数组截断展示
            var floats = new List<float>();
            for (var o = 0; o + 4 <= stride && floats.Count < 16; o += 4)
            {
                floats.Add(ReadFloat(data, baseIndex + o));
            }

            return new { index, floats };
        }

        static int ReadInt(NativeArray<byte> data, int offset)
        {
            return data[offset]
                | (data[offset + 1] << 8)
                | (data[offset + 2] << 16)
                | (data[offset + 3] << 24);
        }

        static float ReadFloat(NativeArray<byte> data, int offset)
        {
            return IntBitsToFloat(ReadInt(data, offset));
        }

        static float IntBitsToFloat(int bits)
        {
            var bytes = BitConverter.GetBytes(bits);
            return BitConverter.ToSingle(bytes, 0);
        }

        static object ReadFloat2(NativeArray<byte> data, int offset) =>
            new { x = ReadFloat(data, offset), y = ReadFloat(data, offset + 4) };

        static object ReadFloat3(NativeArray<byte> data, int offset) =>
            new
            {
                x = ReadFloat(data, offset),
                y = ReadFloat(data, offset + 4),
                z = ReadFloat(data, offset + 8)
            };

        static object ReadFloat4(NativeArray<byte> data, int offset) =>
            new
            {
                x = ReadFloat(data, offset),
                y = ReadFloat(data, offset + 4),
                z = ReadFloat(data, offset + 8),
                w = ReadFloat(data, offset + 12)
            };
    }
}
