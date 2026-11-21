using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KabuUnityMcp.Gpu
{
    /// <summary>
    /// 运行时可注册的 GraphicsBuffer 表。Editor MCP 只读此表；工程/Host 负责写入。
    /// </summary>
    public static class McpGpuBufferRegistry
    {
        private static readonly Dictionary<string, McpGpuBufferEntry> Entries =
            new Dictionary<string, McpGpuBufferEntry>(StringComparer.Ordinal);

        public static int Count
        {
            get
            {
                lock (Entries)
                {
                    return Entries.Count;
                }
            }
        }

        public static void Register(McpGpuBufferEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                throw new ArgumentException("Id is required", nameof(entry));
            }

            lock (Entries)
            {
                Entries[entry.Id.Trim()] = entry;
            }
        }

        public static void Register(
            string id,
            GraphicsBuffer buffer,
            string name = null,
            string ownerPath = null,
            string schema = null,
            int stride = 0,
            int elementCount = 0,
            bool hasCpuMirror = false,
            string notes = null)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            Register(new McpGpuBufferEntry
            {
                Id = id,
                Name = name ?? id,
                OwnerPath = ownerPath,
                Schema = schema,
                Stride = stride > 0 ? stride : buffer.stride,
                ElementCount = elementCount,
                Target = buffer.target.ToString(),
                Buffer = buffer,
                HasCpuMirror = hasCpuMirror,
                Notes = notes
            });
        }

        public static bool Unregister(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            lock (Entries)
            {
                return Entries.Remove(id.Trim());
            }
        }

        public static void UnregisterPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return;
            }

            lock (Entries)
            {
                var doomed = new List<string>();
                foreach (var key in Entries.Keys)
                {
                    if (key.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        doomed.Add(key);
                    }
                }

                for (var i = 0; i < doomed.Count; i++)
                {
                    Entries.Remove(doomed[i]);
                }
            }
        }

        public static void Clear()
        {
            lock (Entries)
            {
                Entries.Clear();
            }
        }

        public static bool TryGet(string id, out McpGpuBufferEntry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            lock (Entries)
            {
                return Entries.TryGetValue(id.Trim(), out entry);
            }
        }

        public static int PruneDead()
        {
            lock (Entries)
            {
                var doomed = new List<string>();
                foreach (var kv in Entries)
                {
                    if (kv.Value == null || !kv.Value.IsAlive)
                    {
                        doomed.Add(kv.Key);
                    }
                }

                for (var i = 0; i < doomed.Count; i++)
                {
                    Entries.Remove(doomed[i]);
                }

                return doomed.Count;
            }
        }

        public static List<McpGpuBufferEntry> Snapshot()
        {
            lock (Entries)
            {
                return new List<McpGpuBufferEntry>(Entries.Values);
            }
        }
    }
}
