#ifndef ROCO_CHARACTER_INCLUDED
#define ROCO_CHARACTER_INCLUDED

/// <summary>
/// Roco 角色 MVP：D 固有色 + ID.R 五区染色 + ID.G AO + M 可选光滑度/金属度/特殊部位。
/// ID.R 档位对齐贴图离散值：0 / 76 / 127 / 178 / 255。
/// 明暗：夹角切三阶；Mid Gradient 开启时中间角域 Lit→Mid→Shadow 渐变；底光 SampleSH。
/// 灯光：Main + Additional；Rendering Layers 匹配。
/// 描边：反向外壳（仅 Character；Cutout 无描边）；WidthMode 物体空间/屏幕像素。平滑法线可烘焙到 Tangent。
/// Receive Shadows 由 keyword 控制。
/// </summary>

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half4 _Color0;
    half4 _Color1;
    half4 _Color2;
    half4 _Color3;
    half4 _Color4;
    half _DyeStrength;
    half _AOStrength;
    half _MaskSmoothness;
    half _MaskMetallic;
    half4 _SpecialColor;
    half _SpecialIntensity;
    half _ToonLitAngle;
    half _ToonMidAngle;
    half _ToonLit;
    half _ToonMid;
    half _ToonShadow;
    half _ToonMidGradient;
    half _AmbientStrength;
    half _OutlineEnabled;
    half _OutlineUseSmoothNormal;
    half _OutlineWidthMode;
    half _OutlineWidth;
    half4 _OutlineColor;
    half _OutlineZOffset;
    half _OutlineNear;
    half _OutlineFar;
CBUFFER_END

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
TEXTURE2D(_IDMap);
SAMPLER(sampler_IDMap);
TEXTURE2D(_MaskMap);
SAMPLER(sampler_MaskMap);

/// <summary>ID.R 五档中心（0~1），对应贴图像素约 0/76/127/178/255。</summary>
static const half kRocoIdLevels[5] = {
    0.0h,
    76.0h / 255.0h,
    127.0h / 255.0h,
    178.0h / 255.0h,
    1.0h
};

/// <summary>
/// 按 ID.R 最近邻选区，返回该区染色色。
/// </summary>
half3 RocoSelectRegionColor(half idR)
{
    half bestDist = abs(idR - kRocoIdLevels[0]);
    half3 color = _Color0.rgb;
    half d1 = abs(idR - kRocoIdLevels[1]);
    if (d1 < bestDist)
    {
        bestDist = d1;
        color = _Color1.rgb;
    }
    half d2 = abs(idR - kRocoIdLevels[2]);
    if (d2 < bestDist)
    {
        bestDist = d2;
        color = _Color2.rgb;
    }
    half d3 = abs(idR - kRocoIdLevels[3]);
    if (d3 < bestDist)
    {
        bestDist = d3;
        color = _Color3.rgb;
    }
    half d4 = abs(idR - kRocoIdLevels[4]);
    if (d4 < bestDist)
    {
        color = _Color4.rgb;
    }
    return color;
}

/// <summary>
/// D × 染色 × AO，并叠加 M.A 特殊部位色；输出漫反射色与 mask 导出的光滑度/金属度。
/// </summary>
void RocoBuildSurface(
    float2 uv,
    out half3 albedo,
    out half smoothness,
    out half metallic,
    out half3 emission)
{
    half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
    half3 baseColor = baseSample.rgb * _BaseColor.rgb;

    half4 idSample = SAMPLE_TEXTURE2D(_IDMap, sampler_IDMap, uv);
    half3 regionColor = RocoSelectRegionColor(idSample.r);
    half3 dyed = lerp(baseColor, baseColor * regionColor, _DyeStrength);

    half ao = lerp(1.0h, idSample.g, _AOStrength);
    albedo = dyed * ao;

    half4 maskSample = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, uv);
    // M.G → Smoothness；M.B → Metallic（强度可关）
    smoothness = saturate(maskSample.g * _MaskSmoothness);
    metallic = saturate(maskSample.b * _MaskMetallic);
    // M.A → 特殊部位叠加（牙齿等）
    emission = _SpecialColor.rgb * (maskSample.a * _SpecialIntensity);
}

/// <summary>
/// 按 N·L 夹角 θ（度）切三阶，强度用 ToonLit / ToonMid / ToonShadow。
/// Mid Gradient 开启时：中间角域内 Lit→Mid→Shadow 平滑过渡；关闭则为硬切三阶。
/// </summary>
half RocoToonShadeByAngle(half NdotL)
{
    half litAngle = min(_ToonLitAngle, _ToonMidAngle);
    half midAngle = max(_ToonLitAngle, _ToonMidAngle);
    half cosLit = cos(litAngle * PI / 180.0h);
    half cosMid = cos(midAngle * PI / 180.0h);

    if (_ToonMidGradient > 0.5h)
    {
        // 亮面硬切、暗面硬切；中间角域 t:0→1 做 Lit→Mid→Shadow
        if (NdotL >= cosLit)
        {
            return _ToonLit;
        }
        if (NdotL < cosMid)
        {
            return _ToonShadow;
        }
        half denom = max(cosLit - cosMid, 0.0001h);
        half t = saturate((cosLit - NdotL) / denom);
        if (t < 0.5h)
        {
            return lerp(_ToonLit, _ToonMid, t * 2.0h);
        }
        return lerp(_ToonMid, _ToonShadow, (t - 0.5h) * 2.0h);
    }

    half shade = _ToonShadow;
    if (NdotL >= cosMid)
    {
        shade = _ToonMid;
    }
    if (NdotL >= cosLit)
    {
        shade = _ToonLit;
    }
    return shade;
}

/// <summary>
/// Rendering Layers 开启时校验灯与 Mesh 是否匹配；未开启则始终通过。
/// </summary>
bool RocoLightLayerMatches(Light light, uint meshRenderingLayers)
{
#if defined(_LIGHT_LAYERS)
    return IsMatchingLightLayer(light.layerMask, meshRenderingLayers);
#else
    return true;
#endif
}

/// <summary>
/// 单盏灯的 toon 漫反射：方向夹角三阶 × 光色（含衰减/可选阴影）。
/// </summary>
half3 RocoToonDiffuseFromLight(Light light, half3 albedo, half3 normalWS, half useShadowAtten)
{
    half3 lightColor = light.color * light.distanceAttenuation;
    lightColor *= lerp(1.0h, light.shadowAttenuation, useShadowAtten);
    half NdotL = dot(normalWS, light.direction);
    half toonShade = RocoToonShadeByAngle(NdotL);
    return albedo * toonShade * lightColor;
}

/// <summary>
/// Main + Additional 累加 toon；仅 layer 匹配的灯生效。高光取第一盏匹配灯。
/// </summary>
half3 RocoShade(
    half3 albedo,
    half smoothness,
    half metallic,
    half3 emission,
    half3 normalWS,
    float3 positionWS,
    float4 shadowCoord)
{
    uint meshRenderingLayers = GetMeshRenderingLayer();
#if defined(_RECEIVE_SHADOWS_OFF)
    half useShadowAtten = 0.0h;
#else
    half useShadowAtten = 1.0h;
#endif

    half3 diffuse = 0.0h;
    Light keyLight = (Light)0;
    bool hasKeyLight = false;

#if defined(_RECEIVE_SHADOWS_OFF)
    Light mainLight = GetMainLight();
#else
    Light mainLight = GetMainLight(shadowCoord);
#endif
    if (RocoLightLayerMatches(mainLight, meshRenderingLayers))
    {
        diffuse += RocoToonDiffuseFromLight(mainLight, albedo, normalWS, useShadowAtten);
        keyLight = mainLight;
        hasKeyLight = true;
    }

#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();
    half4 shadowMask = half4(1.0h, 1.0h, 1.0h, 1.0h);
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light addLight = GetAdditionalLight(lightIndex, positionWS, shadowMask);
        if (RocoLightLayerMatches(addLight, meshRenderingLayers))
        {
            diffuse += RocoToonDiffuseFromLight(addLight, albedo, normalWS, useShadowAtten);
            if (!hasKeyLight)
            {
                keyLight = addLight;
                hasKeyLight = true;
            }
        }
    LIGHT_LOOP_END
#endif

    half3 specular = 0.0h;
    if (hasKeyLight)
    {
        half3 keyLightColor = keyLight.color * keyLight.distanceAttenuation;
        keyLightColor *= lerp(1.0h, keyLight.shadowAttenuation, useShadowAtten);
        half3 viewDir = GetWorldSpaceNormalizeViewDir(positionWS);
        half3 halfDir = normalize(keyLight.direction + viewDir);
        half NdotH = saturate(dot(normalWS, halfDir));
        half specPower = exp2(1.0h + smoothness * 8.0h);
        half3 specColor = lerp(half3(1.0h, 1.0h, 1.0h), albedo, metallic);
        specular = specColor * pow(NdotH, specPower) * smoothness * keyLightColor;
    }

    half3 ambient = SampleSH(normalWS) * albedo * _AmbientStrength;
    return diffuse + specular + ambient + emission;
}

#endif
