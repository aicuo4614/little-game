using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP 场景压暗：按 Layer 对 Opaque Mesh 二次乘色（ZTest Equal）。
/// 天空只改 Skybox 材质；禁止全屏乘色（深度失效时会整屏变黑并误伤 Character）。
/// </summary>
public sealed class SceneDarkenFeature : ScriptableRendererFeature
{
    private static readonly int IdMultiplyColor = Shader.PropertyToID("_MultiplyColor");
    private static readonly int IdSkyboxTint = Shader.PropertyToID("_Tint");
    private static readonly int IdSkyboxSkyTint = Shader.PropertyToID("_SkyTint");

    [SerializeField] private bool featureActive;

    [Tooltip("参与 Mesh 压暗二次绘制的 Layer；默认排除 Character。")]
    [SerializeField] private LayerMask sceneLayerMask = ~0;

    [Tooltip("是否压暗天空（只改 Skybox tint/exposure，不做全屏 Blit，也不改 ambientLight）。")]
    [SerializeField] private bool darkenSky = true;

    [Tooltip("Mesh 压暗注入点；建议 AfterRenderingOpaques 之后。")]
    [SerializeField] private RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingOpaques + 5;

    [SerializeField] private Color multiplyColor = Color.white;

    /// <summary>Play 时运行时乘色，不写回序列化字段，避免退出 Play 污染 URP 资源。</summary>
    private Color runtimeMultiplyColor = Color.white;

    /// <summary>Play 时是否用 runtimeMultiplyColor。</summary>
    private bool hasRuntimeMultiply;

    /// <summary>Play 时 Feature 开关覆盖；未覆盖则用序列化 featureActive。</summary>
    private bool? runtimeFeatureActive;

    /// <summary>是否执行压暗 Pass；默认关闭，由业务在需要时打开。</summary>
    public bool FeatureActive
    {
        get => runtimeFeatureActive ?? featureActive;
        set
        {
            if (Application.isPlaying)
            {
                runtimeFeatureActive = value;
            }
            else
            {
                featureActive = value;
            }

            SyncEnvironmentDarken();
        }
    }

    /// <summary>参与 Mesh 压暗的 Layer。</summary>
    public LayerMask SceneLayerMask
    {
        get => sceneLayerMask;
        set => sceneLayerMask = value;
    }

    /// <summary>是否压暗天空（环境光 / Skybox）。</summary>
    public bool DarkenSky
    {
        get => darkenSky;
        set
        {
            darkenSky = value;
            SyncEnvironmentDarken();
        }
    }

    public Color MultiplyColor
    {
        get => hasRuntimeMultiply ? runtimeMultiplyColor : multiplyColor;
        set
        {
            if (Application.isPlaying)
            {
                runtimeMultiplyColor = value;
                hasRuntimeMultiply = true;
            }
            else
            {
                multiplyColor = value;
            }

            ApplyMaterialColor();
            SyncEnvironmentDarken();
        }
    }

#if UNITY_EDITOR
    private static bool s_HasEditorPreviewOverride;
    private static Color s_EditorPreviewMultiplyColor = Color.white;

    public static bool HasEditorPreviewOverride => s_HasEditorPreviewOverride;

    public static void SetEditorPreviewMultiplyColor(Color color)
    {
        s_EditorPreviewMultiplyColor = color;
        s_HasEditorPreviewOverride = true;
    }

    public static void ClearEditorPreviewMultiplyColor()
    {
        s_HasEditorPreviewOverride = false;
    }
#endif

    public void RefreshMaterialColor()
    {
        ApplyMaterialColor();
        SyncEnvironmentDarken();
    }

    [Tooltip("留空则使用 Hidden/Kabu/SceneDarkenOverlay")]
    [SerializeField] private Shader darkenShader;

    private Material material;
    private SceneDarkenMeshPass meshPass;

    private bool hasSavedEnvironment;
    private Color savedSkyboxTint;
    private Color savedSkyboxSkyTint;
    private Color savedSkyboxGroundColor;
    private float savedSkyboxExposure = 1f;
    private bool hasSkyboxTint;
    private bool hasSkyboxSkyTint;
    private bool hasSkyboxGroundColor;
    private bool hasSkyboxExposure;
    private Material savedSkyboxMaterial;

    public override void Create()
    {
        if (meshPass == null)
        {
            meshPass = new SceneDarkenMeshPass();
        }

        if (darkenShader == null)
        {
            darkenShader = Shader.Find("Hidden/Kabu/SceneDarkenOverlay");
        }

        if (darkenShader != null && (material == null || material.shader != darkenShader))
        {
            if (material != null)
            {
                CoreUtils.Destroy(material);
            }

            material = CoreUtils.CreateEngineMaterial(darkenShader);
        }

        ApplyMaterialColor();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!FeatureActive || meshPass == null || material == null)
        {
            return;
        }

        ApplyMaterialColor();

        var cameraType = renderingData.cameraData.cameraType;
        if (cameraType != CameraType.Game && cameraType != CameraType.SceneView)
        {
            return;
        }

        var color = GetActiveMultiplyColor();
        if (IsIdentityMultiply(color))
        {
            return;
        }

        if (sceneLayerMask.value == 0)
        {
            return;
        }

        meshPass.renderPassEvent = injectionPoint;
        meshPass.Setup(material, sceneLayerMask);
        renderer.EnqueuePass(meshPass);
    }

    /// <summary>
    /// 退出 Play / 战斗结束：清运行时乘色与开关，并还原 Skybox。
    /// 同时把序列化默认值拨回关闭/白色（修复历史上被 Play 写脏的 Feature 资源）。
    /// </summary>
    public void ClearRuntimeDarken()
    {
        hasRuntimeMultiply = false;
        runtimeMultiplyColor = Color.white;
        runtimeFeatureActive = null;
        featureActive = false;
        multiplyColor = Color.white;
        RestoreEnvironmentDarken();
        ApplyMaterialColor();
        SyncEnvironmentDarken();
    }

    protected override void Dispose(bool disposing)
    {
        RestoreEnvironmentDarken();

        if (material != null)
        {
            CoreUtils.Destroy(material);
            material = null;
        }

        meshPass = null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ApplyMaterialColor();
        SyncEnvironmentDarken();
    }

    private void Reset()
    {
        var characterLayer = LayerMask.NameToLayer("Character");
        if (characterLayer >= 0)
        {
            sceneLayerMask = ~(1 << characterLayer);
        }

        darkenSky = true;
        featureActive = false;
        multiplyColor = Color.white;
    }
#endif

    private void ApplyMaterialColor()
    {
        if (material != null)
        {
            material.SetColor(IdMultiplyColor, GetActiveMultiplyColor());
        }
    }

    private Color GetActiveMultiplyColor()
    {
#if UNITY_EDITOR
        if (s_HasEditorPreviewOverride && !Application.isPlaying)
        {
            return s_EditorPreviewMultiplyColor;
        }
#endif
        return hasRuntimeMultiply ? runtimeMultiplyColor : multiplyColor;
    }

    /// <summary>乘色接近白色时视为不压暗。</summary>
    public static bool IsIdentityMultiply(Color color)
    {
        return color.r >= 0.999f && color.g >= 0.999f && color.b >= 0.999f;
    }

    private void SyncEnvironmentDarken()
    {
        var color = GetActiveMultiplyColor();
        var shouldDarken = FeatureActive && darkenSky && !IsIdentityMultiply(color);
        if (shouldDarken)
        {
            ApplyEnvironmentDarken(color);
        }
        else
        {
            RestoreEnvironmentDarken();
        }
    }

    /// <summary>
    /// 只改 Skybox 材质，不动 ambientLight（否则角色环境光会被乘黑）。
    /// </summary>
    private void ApplyEnvironmentDarken(Color multiply)
    {
        var skybox = RenderSettings.skybox;
        if (skybox == null)
        {
            return;
        }

        if (!hasSavedEnvironment || savedSkyboxMaterial != skybox)
        {
            RestoreEnvironmentDarken();
            savedSkyboxMaterial = skybox;
            hasSkyboxTint = false;
            hasSkyboxSkyTint = false;
            hasSkyboxGroundColor = false;
            hasSkyboxExposure = false;

            if (skybox.HasProperty(IdSkyboxTint))
            {
                savedSkyboxTint = skybox.GetColor(IdSkyboxTint);
                hasSkyboxTint = true;
            }

            if (skybox.HasProperty(IdSkyboxSkyTint))
            {
                savedSkyboxSkyTint = skybox.GetColor(IdSkyboxSkyTint);
                hasSkyboxSkyTint = true;
            }

            if (skybox.HasProperty("_GroundColor"))
            {
                savedSkyboxGroundColor = skybox.GetColor("_GroundColor");
                hasSkyboxGroundColor = true;
            }

            if (skybox.HasProperty("_Exposure"))
            {
                savedSkyboxExposure = skybox.GetFloat("_Exposure");
                hasSkyboxExposure = true;
            }

            hasSavedEnvironment = true;
        }

        if (hasSkyboxTint)
        {
            skybox.SetColor(IdSkyboxTint, MultiplyRgb(savedSkyboxTint, multiply));
        }

        if (hasSkyboxSkyTint)
        {
            skybox.SetColor(IdSkyboxSkyTint, MultiplyRgb(savedSkyboxSkyTint, multiply));
        }

        if (hasSkyboxGroundColor)
        {
            skybox.SetColor("_GroundColor", MultiplyRgb(savedSkyboxGroundColor, multiply));
        }

        if (hasSkyboxExposure)
        {
            var lum = (multiply.r + multiply.g + multiply.b) / 3f;
            skybox.SetFloat("_Exposure", savedSkyboxExposure * lum);
        }
    }

    private void RestoreEnvironmentDarken()
    {
        if (!hasSavedEnvironment)
        {
            return;
        }

        if (savedSkyboxMaterial != null)
        {
            if (hasSkyboxTint)
            {
                savedSkyboxMaterial.SetColor(IdSkyboxTint, savedSkyboxTint);
            }

            if (hasSkyboxSkyTint)
            {
                savedSkyboxMaterial.SetColor(IdSkyboxSkyTint, savedSkyboxSkyTint);
            }

            if (hasSkyboxGroundColor)
            {
                savedSkyboxMaterial.SetColor("_GroundColor", savedSkyboxGroundColor);
            }

            if (hasSkyboxExposure)
            {
                savedSkyboxMaterial.SetFloat("_Exposure", savedSkyboxExposure);
            }
        }

        hasSavedEnvironment = false;
        savedSkyboxMaterial = null;
    }

    private static Color MultiplyRgb(Color a, Color b)
    {
        return new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a);
    }

    private sealed class SceneDarkenMeshPass : ScriptableRenderPass
    {
        private static readonly ShaderTagId UniversalForward = new ShaderTagId("UniversalForward");

        private Material mat;
        private LayerMask mask;

        public void Setup(Material material, LayerMask layerMask)
        {
            mat = material;
            mask = layerMask;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (mat == null || mask.value == 0)
            {
                return;
            }

            var sortingSettings = new SortingSettings(renderingData.cameraData.camera)
            {
                criteria = SortingCriteria.CommonOpaque
            };

            var drawingSettings = new DrawingSettings(UniversalForward, sortingSettings)
            {
                overrideMaterial = mat,
                overrideMaterialPassIndex = 0,
                enableInstancing = true,
                enableDynamicBatching = false,
                perObjectData = PerObjectData.None
            };

            var filteringSettings = new FilteringSettings(RenderQueueRange.opaque, mask);

            var renderStateBlock = new RenderStateBlock(RenderStateMask.Depth);
            renderStateBlock.depthState = new DepthState(writeEnabled: false, compareFunction: CompareFunction.Equal);

            context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings, ref renderStateBlock);
        }
    }
}
