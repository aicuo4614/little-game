using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    /// <summary>Frame Debugger 业务逻辑（无 WebSocket）。</summary>
    internal static class FrameDebuggerOps
    {
        public static HandlerResult BeginEnable()
        {
            if (!FrameDebuggerApi.Available)
            {
                return HandlerResult.Fail(FrameDebuggerApi.UnavailableReason);
            }

            var paused = false;
            if (EditorApplication.isPlaying && !EditorApplication.isPaused)
            {
                EditorApplication.isPaused = true;
                paused = true;
            }

            if (!FrameDebuggerApi.TryEnableViaWindow(out var error))
            {
                return HandlerResult.Fail("启用 Frame Debugger 失败：" + error);
            }

            return HandlerResult.Success(new
            {
                enabled = true,
                pausedPlayMode = paused,
                message = "已请求启用 Frame Debugger（会打开窗口；Play 中会暂停）。等待若干帧后可读事件。"
            });
        }

        public static HandlerResult SnapshotAfterEnable()
        {
            var count = FrameDebuggerApi.GetCount();
            return HandlerResult.Success(new
            {
                enabled = true,
                eventCount = count,
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                message = count > 0
                    ? $"Frame Debugger 已启用，当前帧 {count} 条事件。"
                    : "Frame Debugger 已启用，但事件数为 0。确认 Game 视图有画面，或稍后再 list。"
            });
        }

        public static HandlerResult Disable()
        {
            if (!FrameDebuggerApi.Available)
            {
                return HandlerResult.Fail(FrameDebuggerApi.UnavailableReason);
            }

            if (!FrameDebuggerApi.TryDisableViaWindow(out var error))
            {
                return HandlerResult.Fail("关闭 Frame Debugger 失败：" + error);
            }

            var resumed = false;
            if (EditorApplication.isPlaying && EditorApplication.isPaused)
            {
                EditorApplication.isPaused = false;
                resumed = true;
            }

            return HandlerResult.Success(new
            {
                enabled = false,
                resumedPlayMode = resumed,
                message = resumed
                    ? "已关闭 Frame Debugger，并恢复播放。"
                    : "已关闭 Frame Debugger。"
            });
        }

        public static HandlerResult List(int offset, int limit, string eventTypeFilter, string nameContains)
        {
            if (!FrameDebuggerApi.Available)
            {
                return HandlerResult.Fail(FrameDebuggerApi.UnavailableReason);
            }

            if (offset < 0)
            {
                offset = 0;
            }

            if (limit <= 0)
            {
                limit = 50;
            }

            if (limit > 200)
            {
                limit = 200;
            }

            var total = FrameDebuggerApi.GetCount();
            if (total <= 0)
            {
                return HandlerResult.Success(new
                {
                    total = 0,
                    offset,
                    limit,
                    returned = 0,
                    nextOffset = (int?)null,
                    events = Array.Empty<object>(),
                    message = "无事件。请先 unity_frame_debugger_enable，并确认已采到帧。"
                });
            }

            var frameEvents = FrameDebuggerApi.GetFrameEvents();
            var typeFilter = string.IsNullOrWhiteSpace(eventTypeFilter)
                ? null
                : eventTypeFilter.Trim();
            var nameFilter = string.IsNullOrWhiteSpace(nameContains)
                ? null
                : nameContains.Trim();

            var matched = new List<object>();
            var matchedIndices = new List<int>();

            for (var i = 0; i < total; i++)
            {
                object desc = null;
                if (frameEvents != null && i < frameEvents.Length)
                {
                    desc = frameEvents.GetValue(i);
                }

                var typeName = FrameDebuggerApi.GetEventTypeName(desc);
                if (typeFilter != null &&
                    (typeName == null ||
                     typeName.IndexOf(typeFilter, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    continue;
                }

                var infoName = FrameDebuggerApi.GetEventInfoName(i);
                if (nameFilter != null &&
                    (infoName == null ||
                     infoName.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    continue;
                }

                matchedIndices.Add(i);
            }

            var filteredTotal = matchedIndices.Count;
            var end = Math.Min(offset + limit, filteredTotal);
            for (var p = offset; p < end; p++)
            {
                var i = matchedIndices[p];
                object desc = null;
                if (frameEvents != null && i < frameEvents.Length)
                {
                    desc = frameEvents.GetValue(i);
                }

                var typeName = FrameDebuggerApi.GetEventTypeName(desc);
                var infoName = FrameDebuggerApi.GetEventInfoName(i);
                var bound = FrameDebuggerApi.GetEventBoundObject(desc)
                            ?? FrameDebuggerApi.GetEventObject(i);
                ResolveObjectNames(bound, out var goName, out var path);

                matched.Add(new
                {
                    index = i,
                    type = typeName,
                    name = ShortenEventName(infoName),
                    gameObject = goName,
                    path
                });
            }

            int? next = end < filteredTotal ? end : (int?)null;
            return HandlerResult.Success(new
            {
                total = filteredTotal,
                rawEventCount = total,
                offset,
                limit,
                returned = matched.Count,
                nextOffset = next,
                events = matched,
                message = $"Frame Debugger 目录 {offset}-{Math.Max(offset, end - 1)} / 过滤后 {filteredTotal}（原始 {total}）。无 shader/pass；用 get_event 取全文。"
            });
        }

        public static bool TrySeek(int index, out string error)
        {
            error = null;
            if (!FrameDebuggerApi.Available)
            {
                error = FrameDebuggerApi.UnavailableReason;
                return false;
            }

            var total = FrameDebuggerApi.GetCount();
            if (total <= 0)
            {
                error = "无事件。请先 enable。";
                return false;
            }

            if (index < 0 || index >= total)
            {
                error = $"index 越界：{index}，有效范围 0..{total - 1}。";
                return false;
            }

            if (!FrameDebuggerApi.SeekToEvent(index))
            {
                error = "无法设置 Frame Debugger limit（回放游标）。";
                return false;
            }

            return true;
        }

        public static HandlerResult BuildEventDetail(int index)
        {
            var frameEvents = FrameDebuggerApi.GetFrameEvents();
            object desc = null;
            if (frameEvents != null && index < frameEvents.Length)
            {
                desc = frameEvents.GetValue(index);
            }

            var typeName = FrameDebuggerApi.GetEventTypeName(desc);
            var infoName = FrameDebuggerApi.GetEventInfoName(index);
            var bound = FrameDebuggerApi.GetEventBoundObject(desc)
                        ?? FrameDebuggerApi.GetEventObject(index);
            ResolveObjectNames(bound, out var goName, out var path);

            var eventData = FrameDebuggerApi.TryGetEventData(index);
            object detail = null;
            if (eventData != null)
            {
                detail = BuildCleanDetail(eventData, typeName);
            }

            return HandlerResult.Success(new
            {
                index,
                type = typeName,
                name = ShortenEventName(infoName),
                gameObject = goName,
                path,
                detail,
                detailAvailable = detail != null,
                message = detail != null
                    ? $"事件 {index} 详情已就绪。"
                    : $"事件 {index} 目录可读，但详情为空（回放未完成或非 Draw 事件）。可重试 get_event。"
            });
        }

        private static object BuildCleanDetail(object eventData, string eventTypeName)
        {
            var vc = FrameDebuggerApi.GetField<int>(eventData, "m_VertexCount", "vertexCount");
            var ic = FrameDebuggerApi.GetField<int>(eventData, "m_IndexCount", "indexCount");
            var dc = FrameDebuggerApi.GetField<int>(eventData, "m_DrawCallCount", "drawCallCount");
            var inst = FrameDebuggerApi.GetField<int>(eventData, "m_InstanceCount", "instanceCount");

            int[] geo = null;
            if (vc > 0 || ic > 0)
            {
                geo = new[] { vc, ic, ic / 3 };
            }

            string meshName = null;
            var meshObj = FrameDebuggerApi.GetFieldObject(eventData, "m_Mesh", "mesh") as Mesh;
            if (meshObj != null)
            {
                meshName = meshObj.name;
            }

            var shader = FrameDebuggerApi.GetField<string>(eventData, "m_RealShaderName", "realShaderName");
            if (string.IsNullOrEmpty(shader))
            {
                shader = FrameDebuggerApi.GetField<string>(eventData, "m_OriginalShaderName", "originalShaderName", "shaderName");
            }

            var pass = FrameDebuggerApi.GetField<string>(eventData, "m_PassName", "passName");
            var lightMode = FrameDebuggerApi.GetField<string>(eventData, "m_PassLightMode", "passLightMode");
            var keywords = FrameDebuggerApi.GetField<string>(eventData, "shaderKeywords", "m_ShaderKeywords");

            object rt = null;
            var rtName = FrameDebuggerApi.GetField<string>(eventData, "m_RenderTargetName", "rtName");
            if (!string.IsNullOrEmpty(rtName))
            {
                var rtW = FrameDebuggerApi.GetField<int>(eventData, "m_RenderTargetWidth", "rtWidth");
                var rtH = FrameDebuggerApi.GetField<int>(eventData, "m_RenderTargetHeight", "rtHeight");
                var rtFmt = FrameDebuggerApi.GetField<int>(eventData, "m_RenderTargetFormat", "rtFormat");
                var isBack = FrameDebuggerApi.GetField<bool>(eventData, "m_RenderTargetIsBackBuffer");
                rt = new
                {
                    name = rtName,
                    size = rtW > 0 && rtH > 0 ? new[] { rtW, rtH } : null,
                    format = rtFmt > 0 ? (int?)rtFmt : null,
                    backBuffer = isBack ? (bool?)true : null
                };
            }

            string batchBreak = null;
            var cause = FrameDebuggerApi.GetField<int>(eventData, "m_BatchBreakCause", "batchBreakCause");
            batchBreak = FrameDebuggerApi.ResolveBatchBreakCause(cause);

            var isDraw = IsDrawEvent(eventTypeName);
            object state = null;
            object props = null;
            if (isDraw)
            {
                state = BuildNonDefaultStates(eventData);
                props = BuildShaderProps(eventData);
            }

            return new
            {
                geo,
                draws = dc > 0 ? (int?)dc : null,
                instances = inst > 0 ? (int?)inst : null,
                mesh = meshName,
                shader = string.IsNullOrEmpty(shader) ? null : shader,
                pass = string.IsNullOrEmpty(pass) ? null : pass,
                lightMode = string.IsNullOrEmpty(lightMode) ? null : lightMode,
                keywords = string.IsNullOrEmpty(keywords) ? null : keywords,
                rt,
                batchBreak,
                state,
                props
            };
        }

        private static bool IsDrawEvent(string eventTypeName)
        {
            if (string.IsNullOrEmpty(eventTypeName))
            {
                return true;
            }

            if (eventTypeName.StartsWith("Clear", StringComparison.Ordinal))
            {
                return false;
            }

            if (eventTypeName == "ResolveRT" || eventTypeName == "ResolveDepth" ||
                eventTypeName == "HierarchyLevelBreak")
            {
                return false;
            }

            return true;
        }

        private static object BuildNonDefaultStates(object eventData)
        {
            var blend = FrameDebuggerApi.GetFieldObject(eventData, "m_BlendState", "blendState");
            var depth = FrameDebuggerApi.GetFieldObject(eventData, "m_DepthState", "depthState");
            var stencil = FrameDebuggerApi.GetFieldObject(eventData, "m_StencilState", "stencilState");
            var raster = FrameDebuggerApi.GetFieldObject(eventData, "m_RasterState", "rasterState");

            object blendObj = null;
            if (blend != null)
            {
                var src = FrameDebuggerApi.GetField<object>(blend, "m_SrcBlend", "srcBlend");
                var dst = FrameDebuggerApi.GetField<object>(blend, "m_DstBlend", "dstBlend");
                var srcA = FrameDebuggerApi.GetField<object>(blend, "m_SrcBlendAlpha", "srcBlendAlpha");
                var dstA = FrameDebuggerApi.GetField<object>(blend, "m_DstBlendAlpha", "dstBlendAlpha");
                blendObj = new
                {
                    src = src != null ? src.ToString() : null,
                    dst = dst != null ? dst.ToString() : null,
                    srcAlpha = srcA != null ? srcA.ToString() : null,
                    dstAlpha = dstA != null ? dstA.ToString() : null
                };
            }

            object depthObj = null;
            if (depth != null)
            {
                var write = FrameDebuggerApi.GetField<object>(depth, "m_DepthWrite", "depthWrite");
                var func = FrameDebuggerApi.GetField<object>(depth, "m_DepthFunc", "depthFunc");
                depthObj = new
                {
                    write = write != null ? write.ToString() : null,
                    func = func != null ? func.ToString() : null
                };
            }

            object stencilObj = null;
            if (stencil != null)
            {
                var enabled = FrameDebuggerApi.GetField<object>(stencil, "m_StencilEnable", "stencilEnable");
                var readMask = FrameDebuggerApi.GetField<object>(stencil, "m_ReadMask", "readMask");
                var writeMask = FrameDebuggerApi.GetField<object>(stencil, "m_WriteMask", "writeMask");
                var stencilRef = FrameDebuggerApi.GetField<int>(eventData, "m_StencilRef", "stencilRef");
                stencilObj = new
                {
                    enable = enabled != null ? enabled.ToString() : null,
                    readMask = readMask != null ? readMask.ToString() : null,
                    writeMask = writeMask != null ? writeMask.ToString() : null,
                    @ref = stencilRef
                };
            }

            object rasterObj = null;
            if (raster != null)
            {
                var cull = FrameDebuggerApi.GetField<object>(raster, "m_CullMode", "cullMode");
                rasterObj = new { cull = cull != null ? cull.ToString() : null };
            }

            if (blendObj == null && depthObj == null && stencilObj == null && rasterObj == null)
            {
                return null;
            }

            return new { blend = blendObj, depth = depthObj, stencil = stencilObj, raster = rasterObj };
        }

        private static object BuildShaderProps(object eventData)
        {
            var shaderInfo = FrameDebuggerApi.GetFieldObject(eventData, "m_ShaderInfo", "shaderInfo");
            if (shaderInfo == null)
            {
                return null;
            }

            var textures = ReadTextureProps(shaderInfo);
            var vectors = ReadNamedVectorProps(shaderInfo, "m_Vectors", "vectors");
            var floats = ReadNamedFloatProps(shaderInfo, "m_Floats", "floats");
            var ints = ReadNamedIntProps(shaderInfo, "m_Ints", "ints");
            var kwArr = FrameDebuggerApi.GetFieldObject(shaderInfo, "m_Keywords", "keywords") as Array;
            List<string> keywordList = null;
            if (kwArr != null && kwArr.Length > 0)
            {
                keywordList = new List<string>();
                for (var i = 0; i < kwArr.Length; i++)
                {
                    var item = kwArr.GetValue(i);
                    var name = FrameDebuggerApi.GetField<string>(item, "m_Name", "name");
                    if (!string.IsNullOrEmpty(name))
                    {
                        keywordList.Add(name);
                    }
                }
            }

            if ((textures == null || textures.Count == 0) &&
                (vectors == null || vectors.Count == 0) &&
                (floats == null || floats.Count == 0) &&
                (ints == null || ints.Count == 0) &&
                (keywordList == null || keywordList.Count == 0))
            {
                return null;
            }

            return new
            {
                keywords = keywordList,
                textures,
                vectors,
                floats,
                ints
            };
        }

        private static List<object> ReadTextureProps(object shaderInfo)
        {
            var arr = FrameDebuggerApi.GetFieldObject(shaderInfo, "m_Textures", "textures") as Array;
            if (arr == null || arr.Length == 0)
            {
                return null;
            }

            var list = new List<object>();
            for (var i = 0; i < arr.Length; i++)
            {
                var item = arr.GetValue(i);
                var name = FrameDebuggerApi.GetField<string>(item, "m_Name", "name");
                var texName = FrameDebuggerApi.GetField<string>(item, "m_TextureName", "textureName");
                var tex = FrameDebuggerApi.GetFieldObject(item, "m_Value", "value") as Texture;
                int[] size = null;
                string fmt = null;
                if (tex != null)
                {
                    size = new[] { tex.width, tex.height };
                    if (tex is Texture2D t2d)
                    {
                        fmt = t2d.format.ToString();
                    }
                    else if (tex is RenderTexture rt)
                    {
                        fmt = rt.graphicsFormat.ToString();
                    }

                    if (string.IsNullOrEmpty(texName))
                    {
                        texName = tex.name;
                    }
                }

                if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(texName))
                {
                    continue;
                }

                list.Add(new { name, tex = texName, size, fmt });
            }

            return list.Count > 0 ? list : null;
        }

        private static List<object> ReadNamedFloatProps(object shaderInfo, params string[] fieldNames)
        {
            var arr = FrameDebuggerApi.GetFieldObject(shaderInfo, fieldNames) as Array;
            if (arr == null || arr.Length == 0)
            {
                return null;
            }

            var list = new List<object>();
            for (var i = 0; i < arr.Length; i++)
            {
                var item = arr.GetValue(i);
                var name = FrameDebuggerApi.GetField<string>(item, "m_Name", "name");
                var value = FrameDebuggerApi.GetField<float>(item, "m_Value", "value");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                list.Add(new { name, v = value });
            }

            return list.Count > 0 ? list : null;
        }

        private static List<object> ReadNamedIntProps(object shaderInfo, params string[] fieldNames)
        {
            var arr = FrameDebuggerApi.GetFieldObject(shaderInfo, fieldNames) as Array;
            if (arr == null || arr.Length == 0)
            {
                return null;
            }

            var list = new List<object>();
            for (var i = 0; i < arr.Length; i++)
            {
                var item = arr.GetValue(i);
                var name = FrameDebuggerApi.GetField<string>(item, "m_Name", "name");
                var value = FrameDebuggerApi.GetField<int>(item, "m_Value", "value");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                list.Add(new { name, v = value });
            }

            return list.Count > 0 ? list : null;
        }

        private static List<object> ReadNamedVectorProps(object shaderInfo, params string[] fieldNames)
        {
            var arr = FrameDebuggerApi.GetFieldObject(shaderInfo, fieldNames) as Array;
            if (arr == null || arr.Length == 0)
            {
                return null;
            }

            var list = new List<object>();
            for (var i = 0; i < arr.Length; i++)
            {
                var item = arr.GetValue(i);
                var name = FrameDebuggerApi.GetField<string>(item, "m_Name", "name");
                var value = FrameDebuggerApi.GetFieldObject(item, "m_Value", "value");
                if (string.IsNullOrEmpty(name) || value == null)
                {
                    continue;
                }

                object vOut;
                if (value is Vector4 vec)
                {
                    vOut = new[] { vec.x, vec.y, vec.z, vec.w };
                }
                else if (value is Color col)
                {
                    vOut = new[] { col.r, col.g, col.b, col.a };
                }
                else
                {
                    vOut = value.ToString();
                }

                list.Add(new { name, v = vOut });
            }

            return list.Count > 0 ? list : null;
        }

        internal static void ResolveObjectNames(UnityEngine.Object bound, out string goName, out string path)
        {
            goName = null;
            path = null;
            if (bound == null)
            {
                return;
            }

            goName = bound.name;
            if (bound is GameObject go)
            {
                path = GetHierarchyPath(go.transform);
                return;
            }

            if (bound is Component comp)
            {
                goName = comp.gameObject.name;
                path = GetHierarchyPath(comp.transform);
            }
        }

        private static string GetHierarchyPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            var p = t.parent;
            while (p != null)
            {
                sb.Insert(0, p.name + "/");
                p = p.parent;
            }

            return sb.ToString();
        }

        private static string ShortenEventName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            // Keep last 3 segments of long SRP paths for readability.
            var parts = name.Split('/');
            if (parts.Length <= 3)
            {
                return name;
            }

            return parts[parts.Length - 3] + "/" + parts[parts.Length - 2] + "/" + parts[parts.Length - 1];
        }
    }
}
