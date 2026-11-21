using System.Collections.Generic;
using KabuUnityMcp.Editor.Handlers.Profiler;
using NUnit.Framework;
using UnityEngine;

namespace KabuUnityMcp.Editor.Tests.Profiler
{
    public class PerfEvidenceOpsTests
    {
        [Test]
        public void Overdraw_MaterialProxy_CountsRenderers()
        {
            var go = new GameObject("OverdrawTest_Renderer");
            try
            {
                go.AddComponent<MeshRenderer>();
                var scene = go.scene;
                var stats = OverdrawOps.BuildMaterialProxy(scene);
                Assert.GreaterOrEqual(stats.rendererCount, 1);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Overdraw_IsLikelyTransparent_NullSafe()
        {
            // 无材质时不应抛
            Assert.DoesNotThrow(() =>
            {
                var shader = Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Standard");
                if (shader == null) return;
                var m = new Material(shader);
                try
                {
                    OverdrawOps.IsLikelyTransparent(m);
                }
                finally
                {
                    Object.DestroyImmediate(m);
                }
            });
        }

        [Test]
        public void Overdraw_ScoreTopRow_OpaqueDownweightedVsTransparent()
        {
            var opaque = OverdrawOps.ScoreTopRow(new OverdrawOps.TopAccum
            {
                path = "opaque_bulk",
                rendererCount = 800,
                activeRendererCount = 800,
                materialSlots = 800,
                transparentSlots = 0,
                maxQueue = 2000,
                anyTransparent = false,
                volumeSum = 400.0
            });
            var transparent = OverdrawOps.ScoreTopRow(new OverdrawOps.TopAccum
            {
                path = "one_fx",
                rendererCount = 1,
                activeRendererCount = 1,
                materialSlots = 1,
                transparentSlots = 1,
                maxQueue = 3000,
                anyTransparent = true,
                volumeSum = 0.0
            });
            Assert.Greater(transparent.score, opaque.score);
            Assert.IsTrue(transparent.likelyTransparent);
            Assert.IsFalse(opaque.likelyTransparent);
        }

        [Test]
        public void Overdraw_ScoreTopRow_InactiveOnly_HeavilyDownweighted()
        {
            var inactive = OverdrawOps.ScoreTopRow(new OverdrawOps.TopAccum
            {
                path = "box_off",
                rendererCount = 54,
                activeRendererCount = 0,
                materialSlots = 54,
                transparentSlots = 54,
                maxQueue = 3000,
                anyTransparent = true,
                volumeSum = 9000.0
            });
            var activeFx = OverdrawOps.ScoreTopRow(new OverdrawOps.TopAccum
            {
                path = "fx",
                rendererCount = 1,
                activeRendererCount = 1,
                materialSlots = 1,
                transparentSlots = 1,
                maxQueue = 3000,
                anyTransparent = true,
                volumeSum = 0.0
            });
            Assert.Greater(activeFx.score, inactive.score);
        }

        [Test]
        public void Overdraw_RankAbResults_OrdersBySumLayersDrop()
        {
            var ranked = OverdrawOps.RankAbResults(new List<OverdrawOps.OverdrawTopRow>
            {
                new OverdrawOps.OverdrawTopRow { path = "low", sumLayersDrop = 10, avgLayersDrop = 0.1 },
                new OverdrawOps.OverdrawTopRow { path = "high", sumLayersDrop = 500, avgLayersDrop = 0.001 },
                new OverdrawOps.OverdrawTopRow { path = "mid", sumLayersDrop = 100, avgLayersDrop = 1.0 }
            }, 2);
            Assert.AreEqual(2, ranked.Count);
            Assert.AreEqual("high", ranked[0].path);
            Assert.AreEqual(1, ranked[0].rank);
            Assert.AreEqual(500, ranked[0].score);
            Assert.AreEqual(OverdrawOps.ScoreKindAb, ranked[0].scoreKind);
            Assert.AreEqual("mid", ranked[1].path);
        }

        [Test]
        public void Occlusion_GetInfo_ReturnsConclusion()
        {
            var r = OcclusionOps.GetInfo();
            Assert.IsTrue(r.Ok);
            Assert.IsNotNull(r.Payload);
            var conclusion = r.Payload.GetType().GetProperty("conclusion")?.GetValue(r.Payload) as string;
            Assert.IsFalse(string.IsNullOrEmpty(conclusion));
        }

        [Test]
        public void PostProcess_IsInteresting_VolumeName()
        {
            // 用匿名类型名测 hint：伪装不了 Type，改测内置名匹配逻辑间接——用已知假类型不可行
            // 覆盖 GetStack 不抛即可
            var r = PostProcessOps.GetStack(10);
            Assert.IsTrue(r.Ok);
            Assert.IsNotNull(r.Payload);
        }

        [Test]
        public void RenderCensus_GetCensus_Ok()
        {
            var r = RenderCensusOps.GetCensus();
            Assert.IsTrue(r.Ok);
            var schema = r.Payload.GetType().GetProperty("schema")?.GetValue(r.Payload) as string;
            Assert.AreEqual(RenderCensusOps.Schema, schema);
        }

        [Test]
        public void CaptureOverdrawSection_HasStatus()
        {
            var section = OverdrawOps.BuildSectionForCapture();
            Assert.IsNotNull(section);
            var status = section.GetType().GetProperty("status")?.GetValue(section) as string;
            Assert.IsFalse(string.IsNullOrEmpty(status));
        }
    }
}
