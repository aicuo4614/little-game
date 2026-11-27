using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 材质 Outline 两阶段：在不透明 Forward 之前，用 <c>DrawRenderers</c> 只画
/// LightMode=<see cref="LightModeName"/> 的描边 Pass，便于 SRP Batcher 连批。
/// 须把角色 Shader 的 Outline Pass 从 <c>SRPDefaultUnlit</c> 改成该标签，否则会画两遍或仍交错。
/// </summary>
public sealed class RocoCharacterOutlineFeature : ScriptableRendererFeature
{
    /// <summary>与 Roco/Character*、PartBatch 的 Outline Pass LightMode 一致。</summary>
    public const string LightModeName = "RocoCharacterOutline";

    [SerializeField] private bool featureActive = true;

    [Tooltip("画在不透明 Forward 之前，外壳先写入。")]
    [SerializeField] private RenderPassEvent injectionPoint = RenderPassEvent.BeforeRenderingOpaques;

    [Tooltip("参与材质描边的 Layer；默认全部。")]
    [SerializeField] private LayerMask layerMask = ~0;

    private RocoCharacterOutlinePass outlinePass;
    private ShaderTagId outlineShaderTagId;

    public override void Create()
    {
        outlineShaderTagId = new ShaderTagId(LightModeName);
        if (outlinePass == null)
        {
            outlinePass = new RocoCharacterOutlinePass();
        }

        outlinePass.renderPassEvent = injectionPoint;
        outlinePass.Setup(outlineShaderTagId, layerMask);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (outlinePass != null)
        {
            outlinePass.renderPassEvent = injectionPoint;
            outlinePass.Setup(outlineShaderTagId, layerMask);
        }
    }
#endif

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!featureActive)
        {
            return;
        }

        var cameraType = renderingData.cameraData.cameraType;
        // 仅主战场 Base 相机执行角色描边，跳过 UI Overlay 与拖拽 Overlay 相机以节省渲染开销
        if (cameraType == CameraType.Preview || renderingData.cameraData.renderType != CameraRenderType.Base)
        {
            return;
        }

        // 材质 Outline 也依赖该全局；与 MeshOutline Feature 一并回写防丢失。
        Shader.SetGlobalFloat("_RocoOutlineMaxCameraDistance", 0);
        outlinePass.renderPassEvent = injectionPoint;
        outlinePass.Setup(outlineShaderTagId, layerMask);
        renderer.EnqueuePass(outlinePass);
    }

    private sealed class RocoCharacterOutlinePass : ScriptableRenderPass
    {
        private static readonly ProfilingSampler Sampler = new ProfilingSampler("RocoCharacterOutline");

        private ShaderTagId shaderTagId;
        private FilteringSettings filteringSettings;

        public void Setup(ShaderTagId tagId, LayerMask mask)
        {
            shaderTagId = tagId;
            filteringSettings = new FilteringSettings(RenderQueueRange.opaque, mask);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var sorting = renderingData.cameraData.defaultOpaqueSortFlags;
            var drawing = CreateDrawingSettings(shaderTagId, ref renderingData, sorting);
            // 描边 Unlit：不需要灯光/探针 per-object 数据，减轻 CPU
            drawing.perObjectData = PerObjectData.None;

            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, Sampler))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                context.DrawRenderers(renderingData.cullResults, ref drawing, ref filteringSettings);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
