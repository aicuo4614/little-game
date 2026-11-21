using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using KabuUnityMcp.Editor.Handlers;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KabuUnityMcp.Editor.Handlers.Profiler
{
    /// <summary>
    /// unity_perf_get_overdraw 的采样/候选/回包拼装。
    /// Play+Game：Additive RT 得整屏层数；物体贡献由 A/B（关掉后层数下降）给出。
    /// proxy shortlist 仅用于限制 A/B 候选数量。
    /// </summary>
    internal static class OverdrawOps
    {
        public const string Schema = "kabu.perf.overdraw/v1";
        public const string ScoreKindAb = "play_ab_layers";
        public const string ScoreKindProxy = "proxy";
        public const int DefaultTopN = 20;
        public const int MaxTopN = 50;
        public const int DefaultCandidateLimit = 30;
        public const int MaxCandidateLimit = 80;
        public const int DefaultSettleFrames = 12;

        /// <summary>Game 相机 Additive 层数采样结果（R 通道累加即层数）。</summary>
        internal struct OverdrawSample
        {
            public double avgOverdraw;
            public double maxOverdraw;
            public double sumLayers;
            public int width;
            public int height;
            public int drawnRenderers;
            public string note;
        }

        public static object BuildSectionForCapture()
        {
            if (!EditorApplication.isPlaying)
            {
                return new
                {
                    ok = false,
                    status = "unavailable",
                    error = "overdraw section 需 Play；完整 Top 请调 unity_perf_get_overdraw",
                    mode = "need_play",
                    data = (object)null
                };
            }

            if (!TrySampleGameOverdraw(out var sample, out var error))
            {
                return new
                {
                    ok = false,
                    status = "unavailable",
                    error = error ?? "game overdraw sample failed",
                    mode = "play_game_failed",
                    data = (object)null
                };
            }

            return new
            {
                ok = true,
                status = "ok",
                mode = "play_game_layers",
                error = (string)null,
                data = new
                {
                    pixelSample = ToPixelSampleObject(sample),
                    scoreKind = ScoreKindAb,
                    note = "capture_frame 仅含整屏层数；每物体 avgLayersDrop 见 unity_perf_get_overdraw"
                }
            };
        }

        public static object ToPixelSampleObject(OverdrawSample sample)
        {
            return new
            {
                sample.avgOverdraw,
                sample.maxOverdraw,
                sample.sumLayers,
                resolution = $"{sample.width}x{sample.height}",
                drawnRenderers = sample.drawnRenderers,
                note = sample.note
            };
        }

        /// <summary>
        /// 用 Game 相机 VP，对激活 Renderer DrawRenderer Additive 到 Float RT，R=层数。
        /// 非真机 TBDR；与 URP 透明排序不完全一致，但 Play 态可对比 A/B。
        /// </summary>
        public static bool TrySampleGameOverdraw(out OverdrawSample sample, out string error)
        {
            sample = default;
            error = null;

            if (!EditorApplication.isPlaying)
            {
                error = "需在 Play Mode 下采样 Game 过绘";
                return false;
            }

            var cam = FindGameCamera();
            if (cam == null)
            {
                error = "未找到可用 Game 相机（Camera.main / 已启用相机）";
                return false;
            }

            Material mat = null;
            RenderTexture rt = null;
            Texture2D tex = null;
            CommandBuffer cmd = null;
            try
            {
                mat = CreateAdditiveOverdrawMaterial();
                if (mat == null)
                {
                    error = "无法创建 Additive Overdraw Material（缺 Hidden/Internal-Colored）";
                    return false;
                }

                var width = Mathf.Clamp(cam.pixelWidth > 0 ? cam.pixelWidth : 512, 64, 512);
                var height = Mathf.Clamp(cam.pixelHeight > 0 ? cam.pixelHeight : 288, 64, 512);
                rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGBFloat);
                rt.filterMode = FilterMode.Point;

                cmd = new CommandBuffer { name = "kabu.overdraw.sample" };
                cmd.SetRenderTarget(rt);
                cmd.ClearRenderTarget(true, true, Color.black);
                cmd.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);

                var scene = SceneManager.GetActiveScene();
                var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                var drawn = 0;
                foreach (var r in renderers)
                {
                    if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (r.gameObject.scene != scene) continue;
                    if (r.forceRenderingOff) continue;
                    try
                    {
                        if (!GeometryUtility.TestPlanesAABB(planes, r.bounds))
                            continue;
                    }
                    catch
                    {
                        /* Particle bounds 偶发异常：仍尝试画 */
                    }

                    var count = 1;
                    try { count = Math.Max(1, r.sharedMaterials?.Length ?? 1); }
                    catch { count = 1; }

                    for (var si = 0; si < count; si++)
                    {
                        try
                        {
                            cmd.DrawRenderer(r, mat, si);
                            drawn++;
                        }
                        catch
                        {
                            /* 某些 Renderer 不支持 DrawRenderer */
                        }
                    }
                }

                Graphics.ExecuteCommandBuffer(cmd);

                tex = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply(false, false);
                RenderTexture.active = prev;

                var pixels = tex.GetPixels();
                double sum = 0;
                double max = 0;
                for (var i = 0; i < pixels.Length; i++)
                {
                    var v = (double)pixels[i].r;
                    if (v < 0) v = 0;
                    sum += v;
                    if (v > max) max = v;
                }

                var avg = pixels.Length > 0 ? sum / pixels.Length : 0;
                sample = new OverdrawSample
                {
                    avgOverdraw = Math.Round(avg, 4),
                    maxOverdraw = Math.Round(max, 4),
                    sumLayers = Math.Round(sum, 2),
                    width = width,
                    height = height,
                    drawnRenderers = drawn,
                    note = "Play/Game 相机 Additive DrawRenderer（ARGBFloat R=层数）；编辑器近似，非真机 TBDR。"
                };
                return drawn > 0 || avg > 0;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                cmd?.Release();
                if (rt != null)
                    RenderTexture.ReleaseTemporary(rt);
                if (tex != null)
                    UnityEngine.Object.DestroyImmediate(tex);
                if (mat != null)
                    UnityEngine.Object.DestroyImmediate(mat);
            }
        }

        internal static Material CreateAdditiveOverdrawMaterial()
        {
            // 自建 Additive：保证 Float RT 上每片元 +1，不依赖内置 shader 的 Blend 属性是否暴露
            var shader = Shader.Find("Hidden/KabuMcpOverdrawAdd");
            if (shader == null)
            {
                const string src =
                    "Shader \"Hidden/KabuMcpOverdrawAdd\"{\n" +
                    "SubShader{Tags{\"Queue\"=\"Transparent\"\"RenderType\"=\"Transparent\"}\n" +
                    "Pass{ZWrite Off ZTest LEqual Cull Off Blend One One\n" +
                    "CGPROGRAM\n" +
                    "#pragma vertex vert\n" +
                    "#pragma fragment frag\n" +
                    "#include \"UnityCG.cginc\"\n" +
                    "struct v2f{float4 pos:SV_POSITION;};\n" +
                    "v2f vert(float4 v:POSITION){v2f o;o.pos=UnityObjectToClipPos(v);return o;}\n" +
                    "float4 frag(v2f i):SV_Target{return float4(1,1,1,1);}\n" +
                    "ENDCG\n}}\n}";
                try
                {
                    shader = ShaderUtil.CreateShaderAsset(src, false);
                    if (shader != null)
                        shader.hideFlags = HideFlags.HideAndDontSave;
                }
                catch
                {
                    shader = null;
                }
            }

            if (shader == null)
            {
                shader = Shader.Find("Hidden/Internal-Colored")
                         ?? Shader.Find("Unlit/Color");
            }

            if (shader == null)
                return null;

            var mat = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", Color.white);
            mat.SetInt("_SrcBlend", (int)BlendMode.One);
            mat.SetInt("_DstBlend", (int)BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)RenderQueue.Transparent + 100;
            return mat;
        }

        internal static Camera FindGameCamera()
        {
            try
            {
                if (Camera.main != null && Camera.main.enabled)
                    return Camera.main;
            }
            catch { /* ignore */ }

            Camera best = null;
            var bestDepth = float.NegativeInfinity;
            foreach (var c in Camera.allCameras)
            {
                if (c == null || !c.enabled) continue;
                if (c.cameraType == CameraType.SceneView || c.cameraType == CameraType.Preview)
                    continue;
                if ((c.hideFlags & HideFlags.HideAndDontSave) != 0 && c.name.Contains("SceneView"))
                    continue;
                if (c.depth >= bestDepth)
                {
                    bestDepth = c.depth;
                    best = c;
                }
            }

            return best;
        }

        /// <summary>A/B 候选：激活透明优先，不足再用弱不透明补齐，最多 candidateLimit。</summary>
        internal static List<AbCandidate> BuildAbCandidates(
            Scene scene,
            string rootPath,
            int candidateLimit,
            out int scannedRenderers)
        {
            scannedRenderers = 0;
            candidateLimit = Math.Clamp(candidateLimit, 1, MaxCandidateLimit);
            var root = string.IsNullOrWhiteSpace(rootPath) ? null : rootPath.Trim().Replace('\\', '/');
            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            var byPath = new Dictionary<string, AbCandidate>(StringComparer.Ordinal);
            foreach (var r in renderers)
            {
                if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (r.gameObject.scene != scene) continue;
                var path = GetPath(r.transform);
                if (root != null && !PathUnderRoot(path, root)) continue;

                scannedRenderers++;
                if (!byPath.TryGetValue(path, out var cand))
                {
                    cand = new AbCandidate { path = path };
                    byPath[path] = cand;
                }

                cand.Renderers.Add(r);
                cand.activeRendererCount++;

                Material[] mats = null;
                try { mats = r.sharedMaterials; }
                catch { /* ignore */ }
                if (mats == null) continue;
                foreach (var m in mats)
                {
                    if (m == null) continue;
                    cand.materialSlots++;
                    if (IsLikelyTransparent(m))
                    {
                        cand.transparentSlots++;
                        cand.likelyTransparent = true;
                    }
                }

                try
                {
                    var b = r.bounds;
                    cand.volumeSum += Math.Max(0.0001, (double)b.size.x * b.size.y * b.size.z);
                }
                catch { /* ignore */ }
            }

            // 透明优先，再按 proxy 分
            return byPath.Values
                .Select(c =>
                {
                    c.proxyScore = ScoreTopRow(new TopAccum
                    {
                        path = c.path,
                        rendererCount = c.activeRendererCount,
                        activeRendererCount = c.activeRendererCount,
                        materialSlots = c.materialSlots,
                        transparentSlots = c.transparentSlots,
                        maxQueue = c.likelyTransparent ? 3000 : 2000,
                        anyTransparent = c.likelyTransparent,
                        volumeSum = c.volumeSum
                    }).score;
                    return c;
                })
                .OrderByDescending(c => c.likelyTransparent)
                .ThenByDescending(c => c.proxyScore)
                .Take(candidateLimit)
                .ToList();
        }

        internal static List<OverdrawTopRow> RankAbResults(List<OverdrawTopRow> rows, int topN)
        {
            topN = Math.Clamp(topN <= 0 ? DefaultTopN : topN, 1, MaxTopN);
            // 主看 sumLayersDrop：整屏 avg 会被分辨率稀释，动画噪声也更明显
            return (rows ?? new List<OverdrawTopRow>())
                .OrderByDescending(r => r.sumLayersDrop)
                .ThenByDescending(r => r.avgLayersDrop)
                .ThenByDescending(r => r.maxLayersDrop)
                .ThenByDescending(r => r.likelyTransparent)
                .Take(topN)
                .Select((r, i) =>
                {
                    r.rank = i + 1;
                    r.score = r.sumLayersDrop;
                    r.scoreKind = ScoreKindAb;
                    return r;
                })
                .ToList();
        }

        internal static OverdrawTopRow BuildAbRow(
            AbCandidate cand,
            OverdrawSample baseline,
            OverdrawSample without)
        {
            var sumDrop = Math.Round(baseline.sumLayers - without.sumLayers, 2);
            var avgDrop = Math.Round(baseline.avgOverdraw - without.avgOverdraw, 4);
            return new OverdrawTopRow
            {
                path = cand.path,
                scoreKind = ScoreKindAb,
                rendererCount = cand.activeRendererCount,
                activeRendererCount = cand.activeRendererCount,
                transparentSlots = cand.transparentSlots,
                materialSlots = cand.materialSlots,
                likelyTransparent = cand.likelyTransparent,
                volumeApprox = Math.Round(cand.volumeSum, 3),
                baselineAvg = baseline.avgOverdraw,
                withoutAvg = without.avgOverdraw,
                avgLayersDrop = avgDrop,
                maxLayersDrop = Math.Round(baseline.maxOverdraw - without.maxOverdraw, 4),
                sumLayersDrop = sumDrop,
                score = sumDrop
            };
        }

        internal static string BuildAbMarkdownTable(List<OverdrawTopRow> top)
        {
            if (top == null || top.Count == 0)
                return "";

            var sb = new StringBuilder();
            sb.AppendLine("| # | 路径 | 累计降层 | 均层降 | 透明 |");
            sb.AppendLine("|---|---|---:|---:|---:|");
            foreach (var row in top)
            {
                var path = row.path ?? "";
                var shortPath = path.Length > 64
                    ? path.Substring(0, 28) + "…" + path.Substring(path.Length - 28)
                    : path;
                sb.Append("| ").Append(row.rank).Append(" | `").Append(shortPath.Replace("|", "/"))
                    .Append("` | ").Append(row.sumLayersDrop).Append(" | ").Append(row.avgLayersDrop)
                    .Append(" | ").Append(row.transparentSlots).AppendLine(" |");
            }

            return sb.ToString().TrimEnd();
        }

        public static object BuildFinalPayload(
            OverdrawSample baseline,
            List<OverdrawTopRow> top,
            MaterialProxyStats materialProxy,
            int topN,
            int candidateLimit,
            int settleFrames,
            int scanned,
            int candidateCount,
            string rootPath,
            bool autoEnteredPlay,
            List<string> extraGaps)
        {
            var scene = SceneManager.GetActiveScene();
            var gaps = new List<string>
            {
                "scoreKind=play_ab_layers：主看 sumLayersDrop（关掉后少画的层×像素总和）；avgLayersDrop 会被整屏稀释",
                "非真机 TBDR；粒子动画会导致小幅正负噪声",
                $"候选预筛 candidateLimit={candidateLimit}（透明优先），再 A/B"
            };
            if (extraGaps != null)
                gaps.AddRange(extraGaps);

            var ok = baseline.drawnRenderers > 0 || baseline.avgOverdraw > 0 || (top != null && top.Count > 0);
            return new
            {
                schema = Schema,
                ok,
                status = ok ? "ok" : "unavailable",
                mode = "play_game_ab_layers",
                error = ok ? null : "game overdraw sample empty",
                gaps,
                capturedAt = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
                scene = new { name = scene.name, path = scene.path },
                play = new
                {
                    isPlaying = EditorApplication.isPlaying,
                    autoEnteredPlay
                },
                data = new
                {
                    pixelSample = ToPixelSampleObject(baseline),
                    materialProxy = new
                    {
                        materialProxy.rendererCount,
                        materialProxy.uniqueMaterialCount,
                        materialProxy.transparentRendererCount,
                        materialProxy.transparentMaterialSlots
                    },
                    topN,
                    candidateLimit,
                    settleFrames,
                    scannedRenderers = scanned,
                    candidateCount,
                    includeInactive = false,
                    rootPath = string.IsNullOrWhiteSpace(rootPath) ? null : rootPath.Trim(),
                    scoreKind = ScoreKindAb,
                    top
                }
            };
        }

        // BuildAbMarkdownTable 保留给测试/手工；capture 回包不再带 markdownTable（显示层自建表）

        internal static MaterialProxyStats BuildMaterialProxy(Scene scene)
        {
            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            var inScene = renderers.Where(x => x && x.gameObject.scene == scene).ToArray();
            var transparentRenderers = 0;
            var transparentMaterials = 0;
            var uniqueMats = new HashSet<int>();
            foreach (var r in inScene)
            {
                var mats = r.sharedMaterials;
                if (mats == null) continue;
                var anyTransparent = false;
                foreach (var m in mats)
                {
                    if (m == null) continue;
                    uniqueMats.Add(m.GetInstanceID());
                    if (IsLikelyTransparent(m))
                    {
                        transparentMaterials++;
                        anyTransparent = true;
                    }
                }

                if (anyTransparent) transparentRenderers++;
            }

            return new MaterialProxyStats
            {
                rendererCount = inScene.Length,
                uniqueMaterialCount = uniqueMats.Count,
                transparentRendererCount = transparentRenderers,
                transparentMaterialSlots = transparentMaterials
            };
        }

        internal static bool IsLikelyTransparent(Material m)
        {
            try
            {
                if (m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f)
                    return true;
                if (m.HasProperty("_Mode") && m.GetFloat("_Mode") >= 2f)
                    return true;
                var tag = m.GetTag("RenderType", false);
                if (string.Equals(tag, "Transparent", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tag, "TransparentCutout", StringComparison.OrdinalIgnoreCase))
                    return true;
                var queue = m.renderQueue;
                return queue >= (int)RenderQueue.Transparent - 50;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>proxy 分仅用于候选预筛；透明强、不透明弱。</summary>
        internal static OverdrawTopRow ScoreTopRow(TopAccum a)
        {
            var volFactor = Math.Sqrt(Math.Max(0.0, a.volumeSum));
            double score;
            if (a.anyTransparent)
            {
                score = a.transparentSlots * 100.0
                        + 40.0
                        + Math.Max(0, a.maxQueue - (int)RenderQueue.Geometry) * 0.05
                        + volFactor * 2.0
                        + a.activeRendererCount * 5.0;
            }
            else
            {
                score = volFactor * 0.02 + a.activeRendererCount * 0.02;
            }

            if (a.activeRendererCount <= 0)
                score *= 0.05;

            return new OverdrawTopRow
            {
                path = a.path,
                score = Math.Round(score, 2),
                rendererCount = a.rendererCount,
                transparentSlots = a.transparentSlots,
                materialSlots = a.materialSlots,
                maxRenderQueue = a.maxQueue,
                likelyTransparent = a.anyTransparent,
                volumeApprox = Math.Round(a.volumeSum, 3),
                activeRendererCount = a.activeRendererCount,
                scoreKind = ScoreKindProxy
            };
        }

        internal static string GetPath(Transform t)
        {
            if (t == null) return "";
            var stack = new Stack<string>();
            while (t != null)
            {
                stack.Push(t.name);
                t = t.parent;
            }

            return string.Join("/", stack);
        }

        private static bool PathUnderRoot(string path, string root)
        {
            if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
                return true;
            return path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
        }

        internal struct MaterialProxyStats
        {
            public int rendererCount;
            public int uniqueMaterialCount;
            public int transparentRendererCount;
            public int transparentMaterialSlots;
        }

        internal sealed class TopAccum
        {
            public string path;
            public int rendererCount;
            public int activeRendererCount;
            public int materialSlots;
            public int transparentSlots;
            public int maxQueue;
            public bool anyTransparent;
            public double volumeSum;
        }

        internal sealed class AbCandidate
        {
            public string path;
            public readonly List<Renderer> Renderers = new List<Renderer>();
            public int activeRendererCount;
            public int transparentSlots;
            public int materialSlots;
            public bool likelyTransparent;
            public double volumeSum;
            public double proxyScore;
        }

        [Serializable]
        internal sealed class OverdrawTopRow
        {
            public int rank;
            public string path;
            public double score;
            public string scoreKind;
            public int rendererCount;
            public int activeRendererCount;
            public int transparentSlots;
            public int materialSlots;
            public int maxRenderQueue;
            public bool likelyTransparent;
            public double volumeApprox;
            public double avgLayersDrop;
            public double maxLayersDrop;
            public double sumLayersDrop;
            public double baselineAvg;
            public double withoutAvg;
        }
    }
}
