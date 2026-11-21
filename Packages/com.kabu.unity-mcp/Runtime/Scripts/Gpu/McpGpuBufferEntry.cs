using UnityEngine;
using UnityEngine.Rendering;

namespace KabuUnityMcp.Gpu
{
    /// <summary>
    /// 可供 MCP 发现/读回的 GPU 缓冲描述。工程侧注册；通用包不解析业务布局。
    /// </summary>
    public sealed class McpGpuBufferEntry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string OwnerPath { get; set; }
        /// <summary>布局约定名，如 kabu.gpu.skin-range/v1。</summary>
        public string Schema { get; set; }
        public int Stride { get; set; }
        /// <summary>逻辑元素数；若 ≤0 则用 <see cref="GraphicsBuffer.count"/>。</summary>
        public int ElementCount { get; set; }
        public string Target { get; set; }
        public GraphicsBuffer Buffer { get; set; }
        public bool HasCpuMirror { get; set; }
        public string Notes { get; set; }

        public int ResolvedCount
        {
            get
            {
                if (ElementCount > 0)
                {
                    return ElementCount;
                }

                return Buffer != null ? Buffer.count : 0;
            }
        }

        public long ApproxBytes
        {
            get
            {
                var count = ResolvedCount;
                var stride = Stride > 0 ? Stride : (Buffer != null ? Buffer.stride : 0);
                return (long)count * stride;
            }
        }

        public bool IsAlive => Buffer != null && Buffer.IsValid();

        public object ToListRow()
        {
            return new
            {
                id = Id,
                name = Name,
                ownerPath = OwnerPath,
                schema = Schema,
                stride = Stride > 0 ? Stride : (Buffer != null ? Buffer.stride : 0),
                count = ResolvedCount,
                target = Target ?? (Buffer != null ? Buffer.target.ToString() : null),
                approxBytes = ApproxBytes,
                hasCpuMirror = HasCpuMirror,
                alive = IsAlive,
                notes = Notes
            };
        }
    }
}
