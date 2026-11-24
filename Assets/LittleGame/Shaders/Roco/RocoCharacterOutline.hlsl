#ifndef ROCO_CHARACTER_OUTLINE_INCLUDED
#define ROCO_CHARACTER_OUTLINE_INCLUDED

/// <summary>
/// Roco/Character 反向外壳描边：Cull Front，沿法线外扩。
/// WidthMode 0=物体空间（Width×0.01，Near/Far 淡出）；1=屏幕像素（视深换算，无 Near/Far）。
/// Use Smooth Normal 时方向取 Tangent.xyz（需菜单烘焙）。
/// </summary>

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

/// <summary>AppSOConfig.MaxOutlineCameraDistance 写入的全局；≤0 不限制。</summary>
float _RocoOutlineMaxCameraDistance;

/// <summary>
/// 描边挤出方向：勾选平滑法线且 Tangent 有效时用 Tangent.xyz，否则用 Mesh Normal。
/// </summary>
float3 RocoGetOutlineNormalOS(float3 normalOS, float4 tangentOS, half useSmoothNormal)
{
    float3 n = normalOS;
    if (useSmoothNormal > 0.5h)
    {
        float3 smoothN = tangentOS.xyz;
        if (dot(smoothN, smoothN) > 1e-4)
        {
            n = smoothN;
        }
    }
    return normalize(n);
}

/// <summary>
/// SOConfig 最远描边距离闸门：超距返回 0，否则 1。
/// </summary>
float RocoGetOutlineMaxDistanceScale(float3 pivotWS)
{
    float maxD = _RocoOutlineMaxCameraDistance;
    if (maxD <= 0.0)
    {
        return 1.0;
    }

    return distance(pivotWS, _WorldSpaceCameraPos) > maxD ? 0.0 : 1.0;
}

/// <summary>
/// 物体空间方案：近大远小淡出系数（Near 满宽 → Far 收为 0）。
/// </summary>
float RocoGetOutlineDistanceFade()
{
    float3 pivotWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
    float dist = distance(pivotWS, _WorldSpaceCameraPos);
    float nearD = min(_OutlineNear, _OutlineFar);
    float farD = max(_OutlineNear, _OutlineFar);
    return saturate(smoothstep(farD, nearD, dist));
}

/// <summary>
/// Clip 空间 Z 微调，减轻与本体 Z-fight。
/// </summary>
float4 RocoOutlineApplyZOffset(float4 positionCS)
{
#if defined(UNITY_REVERSED_Z)
    positionCS.z += _OutlineZOffset * -0.01 * positionCS.w;
#else
    positionCS.z += _OutlineZOffset * 0.01 * positionCS.w;
#endif
    return positionCS;
}

/// <summary>
/// 按 WidthMode 外扩后转 Clip。
/// 0 ObjectSpace：posOS + n * (Width×0.01×fade)；1 ScreenPixels：视深换像素为世界单位后沿 nWS 外扩。
/// </summary>
float4 RocoOutlinePositionCS(float3 positionOS, float3 outlineNormalOS, float widthScale)
{
    float3 pivotWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
    widthScale *= RocoGetOutlineMaxDistanceScale(pivotWS);
    // 超距/零宽：勿再画 Cull Front 本体壳（会像描边还在），用 w=0 裁掉整三角形。
    if (widthScale <= 1e-5)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }

    // 0 = 物体空间（原方案）
    if (_OutlineWidthMode < 0.5h)
    {
        float fade = RocoGetOutlineDistanceFade();
        float widthOS = _OutlineWidth * 0.01 * fade * widthScale;
        float3 posOS = positionOS + outlineNormalOS * widthOS;
        return RocoOutlineApplyZOffset(TransformObjectToHClip(posOS));
    }

    // 1 = 屏幕像素稳线宽
    float3 positionWS = TransformObjectToWorld(positionOS);
    float3 normalWS = TransformObjectToWorldNormal(outlineNormalOS, true);
    float3 positionVS = TransformWorldToView(positionWS);
    float viewDepth = max(abs(positionVS.z), 1e-3);
    float projY = max(abs(UNITY_MATRIX_P._m11), 1e-5);
    float projectionDepth = lerp(viewDepth, 1.0, unity_OrthoParams.w);
    float worldPerPixel = (2.0 * projectionDepth) / (max(_ScreenParams.y, 1.0) * projY);
    float widthWS = _OutlineWidth * worldPerPixel * widthScale;
    positionWS += normalWS * widthWS;
    return RocoOutlineApplyZOffset(TransformWorldToHClip(positionWS));
}

#endif
