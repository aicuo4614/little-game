using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class EditorTimelineUtil
{

    /// <summary>Scene 小窗预览占 Scene 高度比例（对齐 Unity Camera Preview）。</summary>
    private const float CameraEndPreviewNormalizedHeight = 0.2f;

    private static Camera cameraEndPreviewCamera;
    private static RenderTexture cameraEndPreviewRt;
    private static Material cameraEndPreviewBlitMaterial;
    private static bool cameraEndPreviewReloadHooked;
    private static Vector3 cameraEndPreviewCachedPos;
    private static Quaternion cameraEndPreviewCachedRot;
    private static Vector2 cameraEndPreviewCachedSize;
    private static double cameraEndPreviewLastRenderTime;
    /// <summary>终点预览 RT 最长多久强制重渲一次（秒）。</summary>
    private const double CameraEndPreviewMinInterval = 0.25;
    /// <summary>预览 RT 相对小窗像素的缩放，降低 URP Render 成本。</summary>
    private const float CameraEndPreviewRtScale = 0.5f;
    /// <summary>在 MonoBehaviour.OnDrawGizmos 回调中绘制世界空间标记点。</summary>
    public static void DrawCirclePoint(Color color, Vector3 worldPoint, float radius)
    {
        var preColor = Gizmos.color;
        Gizmos.color = color;
        Gizmos.DrawSphere(worldPoint, radius);
        Gizmos.color = preColor;
    }

    /// <summary>在 OnDrawGizmos 中绘制四元数朝向：红=X，绿=Y，蓝=Z。</summary>
    public static void DrawRotationOrientation(Vector3 position, Quaternion rotation, float axisLength = 1f)
    {
        var preColor = Gizmos.color;

        Gizmos.color = Color.red;
        Gizmos.DrawRay(position, rotation * Vector3.right * axisLength);

        Gizmos.color = Color.green;
        Gizmos.DrawRay(position, rotation * Vector3.up * axisLength);

        Gizmos.color = Color.blue;
        Gizmos.DrawRay(position, rotation * Vector3.forward * axisLength);

        Gizmos.color = preColor;
    }

    /// <summary>在 OnDrawGizmos 中绘制带箭头的方向向量。</summary>
    public static void DrawDirectionVector(Color color, Vector3 origin, Vector3 direction, float length = 1f)
    {
        if (direction.sqrMagnitude < 1e-8f || length <= 0f)
        {
            return;
        }

        var dir = direction.normalized;
        var tip = origin + dir * length;
        var headLength = Mathf.Min(length * 0.25f, 0.5f);
        const float headAngle = 22.5f;

        var preColor = Gizmos.color;
        Gizmos.color = color;

        Gizmos.DrawLine(origin, tip);
        DrawArrowHead(tip, dir, headLength, headAngle);

        Gizmos.color = preColor;
    }

    private static void DrawArrowHead(Vector3 tip, Vector3 dir, float headLength, float headAngle)
    {
        var look = Quaternion.LookRotation(dir);
        var rightWing = look * Quaternion.Euler(0f, 180f + headAngle, 0f) * Vector3.forward;
        var leftWing = look * Quaternion.Euler(0f, 180f - headAngle, 0f) * Vector3.forward;
        Gizmos.DrawLine(tip, tip + rightWing * headLength);
        Gizmos.DrawLine(tip, tip + leftWing * headLength);
    }

    /// <summary>
    /// SceneGUI：绘制移动轨迹。
    /// 底层粗实线保可见；上层虚线沿起点→终点缓慢流动（依赖 Scene 正常重绘，不主动死循环 Repaint）。
    /// </summary>
    /// <param name="startPos">运动起点（通常为 Clip 进入时采样位姿）。</param>
    /// <param name="endPos">运动终点。</param>
    /// <param name="endForward">终点最终朝向（含旋转偏移后的 forward）。</param>
    /// <param name="hasLookAt">是否有旋转基点（朝向自身/目标等）。</param>
    /// <param name="lookAtWorld">旋转基点世界坐标；喇叭会按到该点的距离拉长，便于看出对准谁。</param>
    public static void DrawMoveTrajectory(
        Vector3 startPos,
        Vector3 endPos,
        Vector3 endForward,
        Color pathColor,
        Color startColor,
        Color endColor,
        bool hasLookAt,
        Vector3 lookAtWorld)
    {
        var time = (float)EditorApplication.timeSinceStartup;
        var startSize = HandleUtility.GetHandleSize(startPos);
        var endSize = HandleUtility.GetHandleSize(endPos);
        var delta = endPos - startPos;
        var pathLen = delta.magnitude;

        // 起终点同一套球：尺寸、颜色、呼吸一致，仅靠文字区分
        var pulse = 0.85f + 0.15f * (0.5f + 0.5f * Mathf.Sin(time * 4f));
        var markerColor = startColor;
        var startMarker = startSize * 0.16f * pulse;
        var endMarker = endSize * 0.16f * pulse;
        Handles.color = markerColor;
        Handles.SphereHandleCap(0, startPos, Quaternion.identity, startMarker, EventType.Repaint);
        Handles.Label(startPos + Vector3.up * startSize * 0.28f, "起点");
        Handles.SphereHandleCap(0, endPos, Quaternion.identity, endMarker, EventType.Repaint);
        Handles.Label(endPos + Vector3.up * endSize * 0.28f, "终点");

        if (pathLen >= 1e-4f)
        {
            var dir = delta / pathLen;
            var lineWidth = Mathf.Clamp(Mathf.Min(startSize, endSize) * 8f, 4f, 12f);

            // 1) 底层半透明粗实线
            var baseColor = pathColor;
            baseColor.a *= 0.35f;
            Handles.color = baseColor;
            Handles.DrawAAPolyLine(lineWidth, startPos, endPos);

            // 2) 上层虚线沿 start→end 缓慢滚动
            var dashWorld = Mathf.Max(pathLen * 0.06f, Mathf.Min(startSize, endSize) * 0.35f);
            var gapWorld = dashWorld * 0.85f;
            var marchSpeed = Mathf.Clamp(pathLen * 0.08f, 0.15f, 0.45f);
            var phase = time * marchSpeed;
            DrawAnimatedDashedLine(startPos, dir, pathLen, pathColor, lineWidth * 1.15f, dashWorld, gapWorld, phase);
        }

        if (hasLookAt)
        {
            // 旋转基点引导：终点 → 自身/目标，避免短喇叭看不出对准谁
            var guideColor = new Color(1f, 0.85f, 0.2f, 0.9f);
            var lookDelta = lookAtWorld - endPos;
            var lookDist = lookDelta.magnitude;
            Handles.color = guideColor;
            Handles.SphereHandleCap(
                0,
                lookAtWorld,
                Quaternion.identity,
                HandleUtility.GetHandleSize(lookAtWorld) * 0.1f,
                EventType.Repaint);
            Handles.Label(lookAtWorld + Vector3.up * HandleUtility.GetHandleSize(lookAtWorld) * 0.2f, "旋转基点");
            if (lookDist > 1e-4f)
            {
                DrawStaticDashedLine(
                    endPos,
                    lookAtWorld,
                    guideColor,
                    Mathf.Clamp(endSize * 3f, 2f, 5f),
                    Mathf.Max(lookDist * 0.04f, 0.08f),
                    Mathf.Max(lookDist * 0.03f, 0.06f));
            }
        }

        // 最终朝向喇叭：有旋转基点时按距离拉长，否则用 handle 尺寸
        var facingLen = -1f;
        if (hasLookAt)
        {
            facingLen = Mathf.Max(Vector3.Distance(endPos, lookAtWorld), endSize * 0.8f);
        }

        DrawHandlesFacingConeDashed(endPos, endForward, endColor, endSize, facingLen);
    }

    /// <summary>世界空间虚线，相位 <paramref name="phase"/> 滚动形成流动感。</summary>
    private static void DrawAnimatedDashedLine(
        Vector3 start,
        Vector3 dir,
        float pathLen,
        Color color,
        float width,
        float dashLen,
        float gapLen,
        float phase)
    {
        if (dashLen <= 1e-5f || pathLen <= 1e-5f)
        {
            return;
        }

        Handles.color = color;
        var period = dashLen + gapLen;
        // 与 phase 同号推进：phase 增大时短划沿 +dir 移动
        var t = Mathf.Repeat(phase, period) - period;
        while (t < pathLen)
        {
            var t0 = Mathf.Max(0f, t);
            var t1 = Mathf.Min(pathLen, t + dashLen);
            if (t1 > t0 + 1e-5f)
            {
                Handles.DrawAAPolyLine(width, start + dir * t0, start + dir * t1);
            }

            t += period;
        }
    }

    /// <summary>
    /// SceneGUI：用静态虚线锥表示注视朝向（中轴 + 若干母线 + 口沿圆，无动画）。
    /// </summary>
    /// <param name="length">锥长；&lt;=0 时用 handleSize 推算短锥。</param>
    public static void DrawHandlesFacingConeDashed(
        Vector3 origin,
        Vector3 forward,
        Color color,
        float handleSize,
        float length = -1f)
    {
        if (forward.sqrMagnitude < 1e-8f)
        {
            return;
        }

        var dir = forward.normalized;
        if (length <= 1e-4f)
        {
            length = handleSize * 1.1f;
        }

        var tip = origin + dir * length;
        var radius = Mathf.Min(handleSize * 0.28f, length * 0.12f);
        radius = Mathf.Max(radius, handleSize * 0.08f);
        var dash = Mathf.Max(Mathf.Min(length * 0.05f, handleSize * 0.08f), 0.05f);
        var gap = dash * 0.75f;
        var width = Mathf.Clamp(handleSize * 3.5f, 2f, 6f);

        var rot = Quaternion.LookRotation(dir);
        var right = rot * Vector3.right;
        var up = rot * Vector3.up;

        Handles.color = color;
        DrawStaticDashedLine(origin, tip, color, width, dash, gap);

        const int rayCount = 8;
        for (var i = 0; i < rayCount; i++)
        {
            var ang = i * Mathf.PI * 2f / rayCount;
            var rim = tip + (right * Mathf.Cos(ang) + up * Mathf.Sin(ang)) * radius;
            DrawStaticDashedLine(origin, rim, color, width * 0.85f, dash, gap);
        }

        const int rimSeg = 16;
        for (var i = 0; i < rimSeg; i++)
        {
            var a0 = i * Mathf.PI * 2f / rimSeg;
            var a1 = (i + 1) * Mathf.PI * 2f / rimSeg;
            var p0 = tip + (right * Mathf.Cos(a0) + up * Mathf.Sin(a0)) * radius;
            var p1 = tip + (right * Mathf.Cos(a1) + up * Mathf.Sin(a1)) * radius;
            DrawStaticDashedLine(p0, p1, color, width * 0.75f, dash * 0.7f, gap * 0.7f);
        }

        Handles.Label(tip + up * handleSize * 0.12f, "朝向");
    }

    /// <summary>静态世界空间虚线（不滚动）。</summary>
    private static void DrawStaticDashedLine(
        Vector3 a,
        Vector3 b,
        Color color,
        float width,
        float dashLen,
        float gapLen)
    {
        var delta = b - a;
        var pathLen = delta.magnitude;
        if (pathLen < 1e-5f || dashLen <= 1e-5f)
        {
            return;
        }

        var dir = delta / pathLen;
        DrawAnimatedDashedLine(a, dir, pathLen, color, width, dashLen, gapLen, phase: 0f);
    }

    public static void DrawEffectPathField(ref string effectPath)
    {
        EditorGUI.BeginChangeCheck();
        var nextEffectPath = EditorGUILayout.TextField("逻辑名", effectPath ?? string.Empty);
        if (EditorGUI.EndChangeCheck())
        {
            effectPath = EditorTimelineAssetPathUtil.ToEffectLogicalName(nextEffectPath);
        }

        var prefabObj = string.IsNullOrEmpty(effectPath)
            ? null
            : EditorTimelineAssetPathUtil.LoadEffectPrefab(effectPath);
        EditorGUI.BeginChangeCheck();
        var newPrefab = (GameObject)EditorGUILayout.ObjectField("Prefab", prefabObj, typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck() && newPrefab != null)
        {
            effectPath = EditorTimelineAssetPathUtil.EffectLogicalFromAssetPath(AssetDatabase.GetAssetPath(newPrefab));
        }
    }

    /// <summary>
    /// 编辑器预览：保证 BattleCamera.ContentTrans 上的相机为 URP Base，Game 视图可直接看到战斗画面。
    /// 不依赖场景 Main Camera，也不挂 Overlay 栈。
    /// </summary>
    public static void EnsureBattleCameraGameViewStack()
    {
        var battleCameraGo = FindBattleCameraGameObject();
        if (battleCameraGo == null)
        {
            Debug.LogWarning("[EditorTimelineUtil] 未找到 BattleCamera，Game 视图看不到战斗预览。");
            return;
        }

        var battleCamera = BattleCameraMonoBinder.EnsureBound(battleCameraGo);
        if (battleCamera.MyCamera == null)
        {
            Debug.LogWarning("[EditorTimelineUtil] BattleCamera.ContentTrans 上没有 Camera。");
            return;
        }

        var urpData = battleCamera.MyCamera.GetUniversalAdditionalCameraData();
        urpData.renderType = CameraRenderType.Base;
    }

    /// <summary>
    /// 在 Scene 视图右下角画「相机落到终点」的小窗预览（对齐选中 Camera 时的 Camera Preview）。
    /// 不挪动真实 BattleCamera，仅用隐藏预览相机按 Root 终点位姿推算 ContentTrans 机位后 Render。
    /// </summary>
    /// <param name="sceneView">当前 SceneView。</param>
    /// <param name="endRootPos">移动终点：BattleCamera.RootTrans 世界坐标。</param>
    /// <param name="endRootRot">移动终点：BattleCamera.RootTrans 世界旋转。</param>
    public static void DrawBattleCameraEndPoseScenePreview(
        SceneView sceneView,
        Vector3 endRootPos,
        Quaternion endRootRot)
    {
        if (sceneView == null)
        {
            return;
        }

        var battleCameraGo = FindBattleCameraGameObject();
        if (battleCameraGo == null)
        {
            return;
        }

        var battleCamera = BattleCameraMonoBinder.EnsureBound(battleCameraGo);
        var sourceCamera = battleCamera.MyCamera;
        var rootTrans = battleCamera.RootTrans;
        if (sourceCamera == null || rootTrans == null)
        {
            return;
        }

        var previewSize = ResolveCameraEndPreviewSize(sceneView, sourceCamera);
        if (previewSize.x < 2f || previewSize.y < 2f)
        {
            return;
        }

        // duringSceneGui 的 GUI 坐标系相对 Scene 内容区；用 cameraViewport 避免画到窗口外
        var viewRect = sceneView.cameraViewport;
        if (viewRect.width < 8f || viewRect.height < 8f)
        {
            viewRect = new Rect(0f, 0f, sceneView.position.width, sceneView.position.height);
        }

        var pad = 10f;
        var labelH = 18f;
        var rect = new Rect(
            viewRect.xMax - previewSize.x - pad,
            viewRect.yMax - previewSize.y - pad,
            previewSize.x,
            previewSize.y);
        var labelRect = new Rect(rect.x, rect.y - labelH, rect.width, labelH);
        var frameRect = new Rect(rect.x - 1f, labelRect.y - 1f, rect.width + 2f, rect.height + labelH + 2f);

        Handles.BeginGUI();
        EditorGUI.DrawRect(frameRect, new Color(0f, 0f, 0f, 0.75f));
        GUI.Label(labelRect, "终点构图预览", EditorStyles.whiteMiniLabel);

        if (Event.current.type == EventType.Repaint)
        {
            EnsureCameraEndPreviewReloadHook();
            var now = EditorApplication.timeSinceStartup;
            var poseDirty = (cameraEndPreviewCachedPos - endRootPos).sqrMagnitude > 1e-6f
                || Quaternion.Angle(cameraEndPreviewCachedRot, endRootRot) > 0.05f
                || (cameraEndPreviewCachedSize - previewSize).sqrMagnitude > 0.25f
                || cameraEndPreviewRt == null;
            var intervalDue = now - cameraEndPreviewLastRenderTime >= CameraEndPreviewMinInterval;
            var needRender = poseDirty || intervalDue || cameraEndPreviewRt == null;

            if (needRender)
            {
                var previewCamera = EnsureCameraEndPreviewCamera();
                previewCamera.CopyFrom(sourceCamera);
                previewCamera.enabled = false;
                previewCamera.cameraType = CameraType.Game;
                previewCamera.scene = sourceCamera.scene;
                previewCamera.clearFlags = sourceCamera.clearFlags;
                previewCamera.backgroundColor = sourceCamera.backgroundColor;
                previewCamera.allowHDR = false;
                previewCamera.allowMSAA = false;

                var sourceUrp = sourceCamera.GetUniversalAdditionalCameraData();
                var previewUrp = previewCamera.GetUniversalAdditionalCameraData();
                previewUrp.renderType = CameraRenderType.Base;
                // 预览关后处理/阴影，显著降低每帧 URP 成本
                previewUrp.renderPostProcessing = false;
                previewUrp.volumeLayerMask = sourceUrp.volumeLayerMask;
                previewUrp.renderShadows = false;
                previewUrp.cameraStack.Clear();

                var camTrans = sourceCamera.transform;
                var localPos = rootTrans.InverseTransformPoint(camTrans.position);
                var localRot = Quaternion.Inverse(rootTrans.rotation) * camTrans.rotation;
                previewCamera.transform.SetPositionAndRotation(
                    endRootPos + endRootRot * localPos,
                    endRootRot * localRot);

                var rtW = Mathf.Max(2, Mathf.RoundToInt(previewSize.x * CameraEndPreviewRtScale));
                var rtH = Mathf.Max(2, Mathf.RoundToInt(previewSize.y * CameraEndPreviewRtScale));
                EnsureCameraEndPreviewRt(rtW, rtH);
                previewCamera.targetTexture = cameraEndPreviewRt;

                var drawGizmo = sceneView.drawGizmos;
                sceneView.drawGizmos = false;
                previewCamera.Render();
                sceneView.drawGizmos = drawGizmo;
                previewCamera.targetTexture = null;

                cameraEndPreviewCachedPos = endRootPos;
                cameraEndPreviewCachedRot = endRootRot;
                cameraEndPreviewCachedSize = previewSize;
                cameraEndPreviewLastRenderTime = now;
            }

            if (cameraEndPreviewRt != null)
            {
                var blitMat = GetCameraEndPreviewBlitMaterial();
                if (blitMat != null)
                {
                    Graphics.DrawTexture(
                        rect,
                        cameraEndPreviewRt,
                        new Rect(0f, 0f, 1f, 1f),
                        0,
                        0,
                        0,
                        0,
                        GUI.color,
                        blitMat);
                }
                else
                {
                    GUI.DrawTexture(rect, cameraEndPreviewRt, ScaleMode.StretchToFill, false);
                }
            }
        }

        Handles.EndGUI();
    }

    private static Vector2 ResolveCameraEndPreviewSize(SceneView sceneView, Camera sourceCamera)
    {
        var viewRect = sceneView.cameraViewport;
        var viewW = viewRect.width > 8f ? viewRect.width : sceneView.position.width;
        var viewH = viewRect.height > 8f ? viewRect.height : sceneView.position.height;

        var previewSize = Handles.GetMainGameViewSize();
        if (previewSize.x < 0f)
        {
            previewSize.x = viewW;
            previewSize.y = viewH;
        }

        var normalizedViewPortRect = sourceCamera.rect;
        previewSize.x *= Mathf.Max(normalizedViewPortRect.width, 0f);
        previewSize.y *= Mathf.Max(normalizedViewPortRect.height, 0f);
        if (previewSize.x <= 0f || previewSize.y <= 0f)
        {
            return Vector2.zero;
        }

        var aspect = previewSize.x / previewSize.y;
        previewSize.y = CameraEndPreviewNormalizedHeight * viewH;
        previewSize.x = previewSize.y * aspect;
        if (previewSize.y > viewH * 0.5f)
        {
            previewSize.y = viewH * 0.5f;
            previewSize.x = previewSize.y * aspect;
        }

        if (previewSize.x > viewW * 0.5f)
        {
            previewSize.x = viewW * 0.5f;
            previewSize.y = previewSize.x / aspect;
        }

        return previewSize;
    }

    private static Camera EnsureCameraEndPreviewCamera()
    {
        if (cameraEndPreviewCamera != null)
        {
            return cameraEndPreviewCamera;
        }

        var go = EditorUtility.CreateGameObjectWithHideFlags(
            "TimelineEditor_CameraEndPreview",
            HideFlags.HideAndDontSave,
            typeof(Camera));
        cameraEndPreviewCamera = go.GetComponent<Camera>();
        cameraEndPreviewCamera.enabled = false;
        cameraEndPreviewCamera.cameraType = CameraType.Game;
        return cameraEndPreviewCamera;
    }

    private static void EnsureCameraEndPreviewRt(int width, int height)
    {
        width = Mathf.Max(width, 2);
        height = Mathf.Max(height, 2);
        if (cameraEndPreviewRt != null
            && cameraEndPreviewRt.width == width
            && cameraEndPreviewRt.height == height)
        {
            return;
        }

        if (cameraEndPreviewRt != null)
        {
            cameraEndPreviewRt.Release();
            UnityEngine.Object.DestroyImmediate(cameraEndPreviewRt);
            cameraEndPreviewRt = null;
        }

        cameraEndPreviewRt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 1,
            hideFlags = HideFlags.HideAndDontSave,
            name = "TimelineEditor_CameraEndPreviewRT",
        };
        cameraEndPreviewRt.Create();
    }

    private static Material GetCameraEndPreviewBlitMaterial()
    {
        if (cameraEndPreviewBlitMaterial != null)
        {
            return cameraEndPreviewBlitMaterial;
        }

        var shader = EditorGUIUtility.LoadRequired("SceneView/GUITextureBlit2SRGB.shader") as Shader;
        if (shader == null)
        {
            return null;
        }

        cameraEndPreviewBlitMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave,
        };
        cameraEndPreviewBlitMaterial.SetFloat(
            "_ManualTex2SRGB",
            QualitySettings.activeColorSpace == ColorSpace.Linear ? 1.0f : 0.0f);
        return cameraEndPreviewBlitMaterial;
    }

    private static void EnsureCameraEndPreviewReloadHook()
    {
        if (cameraEndPreviewReloadHooked)
        {
            return;
        }

        cameraEndPreviewReloadHooked = true;
        AssemblyReloadEvents.beforeAssemblyReload -= ReleaseCameraEndPreviewResources;
        AssemblyReloadEvents.beforeAssemblyReload += ReleaseCameraEndPreviewResources;
    }

    private static void ReleaseCameraEndPreviewResources()
    {
        if (cameraEndPreviewCamera != null)
        {
            UnityEngine.Object.DestroyImmediate(cameraEndPreviewCamera.gameObject);
            cameraEndPreviewCamera = null;
        }

        if (cameraEndPreviewRt != null)
        {
            cameraEndPreviewRt.Release();
            UnityEngine.Object.DestroyImmediate(cameraEndPreviewRt);
            cameraEndPreviewRt = null;
        }

        if (cameraEndPreviewBlitMaterial != null)
        {
            UnityEngine.Object.DestroyImmediate(cameraEndPreviewBlitMaterial);
            cameraEndPreviewBlitMaterial = null;
        }
    }

    /// <summary>
    /// 查找战斗相机：优先 Tag <c>BattleCamera</c>，未注册 Tag 或找不到时按名称回退。
    /// </summary>
    public static GameObject FindBattleCameraGameObject()
    {
        if (cachedBattleCameraGo != null)
        {
            return cachedBattleCameraGo;
        }

        // 优先按根节点名找，避免子节点也挂了 BattleCamera Tag 时找错
        var byName = GameObject.Find("BattleCamera");
        if (byName != null)
        {
            cachedBattleCameraGo = byName;
            return cachedBattleCameraGo;
        }

        try
        {
            cachedBattleCameraGo = GameObject.FindGameObjectWithTag("BattleCamera");
            return cachedBattleCameraGo;
        }
        catch (UnityException)
        {
            return null;
        }
    }

    private static GameObject cachedBattleCameraGo;

    /// <summary>在 root 下按节点名深度查找（含自身）。</summary>
    public static Transform FindChildTransform(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (root.name == name)
        {
            return root;
        }

        for (var i = 0; i < root.childCount; i++)
        {
            var found = FindChildTransform(root.GetChild(i), name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>取得 BattleCamera 下的 ShakeTrans 节点（震动作用点）。</summary>
    public static Transform FindBattleCameraShakeTrans()
    {
        var battleCameraGo = FindBattleCameraGameObject();
        if (battleCameraGo == null)
        {
            return null;
        }

        var battleCamera = BattleCameraMonoBinder.EnsureBound(battleCameraGo);
        if (battleCamera.ShakeTrans != null)
        {
            return battleCamera.ShakeTrans;
        }

        // Mono 引用丢失时，按层级约定直接找子节点 ShakeTrans
        return FindChildTransform(battleCameraGo.transform, "ShakeTrans");
    }

    public static bool TryGetCameraFollowerTransform(out Transform followerTrans)
    {
        var battleCameraGo = FindBattleCameraGameObject();
        if (battleCameraGo == null)
        {
            followerTrans = null;
            return false;
        }

        var battleCamera = BattleCameraMonoBinder.EnsureBound(battleCameraGo);
        followerTrans = battleCamera.RootTrans;
        return followerTrans != null;
    }

    public static Transform EnsureEditorEffectInstance(ref GameObject effectInstance, string effectPath)
    {
        if (string.IsNullOrEmpty(effectPath))
        {
            return null;
        }

        if (effectInstance == null)
        {
            var prefab = EditorTimelineAssetPathUtil.LoadEffectPrefab(effectPath);
            if (prefab == null)
            {
                return null;
            }

            effectInstance = Object.Instantiate(prefab);
        }

        return effectInstance.transform;
    }

    public static void SetupEffectParticlePreview(
        GameObject effectInstance,
        ref List<ParticleSystem> particleSystems,
        ref List<ParticleSystem> particleSystemRoots)
    {
        particleSystems = null;
        particleSystemRoots = null;
        if (effectInstance == null)
        {
            return;
        }

        particleSystems = effectInstance.GetComponentsInChildren<ParticleSystem>(true).ToList();
        for (var i = 0; i < particleSystems.Count; i++)
        {
            const uint randomSeed = 12345u;
            var ps = particleSystems[i];
            // playOnAwake 的特效 Instantiate 后已在播，必须先停净才能改 randomSeed
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            ps.randomSeed = randomSeed;
        }

        particleSystemRoots = CollectRootParticleSystems(particleSystems);
    }

    /// <summary>
    /// 收集「根」粒子：自身不是其它 ParticleSystem 的子节点。
    /// 同级多根（如 quan 与 ju）需分别 Simulate，只播第一个会漏兄弟。
    /// </summary>
    public static List<ParticleSystem> CollectRootParticleSystems(List<ParticleSystem> particleSystems)
    {
        var roots = new List<ParticleSystem>();
        if (particleSystems == null || particleSystems.Count == 0)
        {
            return roots;
        }

        for (var i = 0; i < particleSystems.Count; i++)
        {
            var ps = particleSystems[i];
            var isChildOfOther = false;
            for (var j = 0; j < particleSystems.Count; j++)
            {
                if (i == j)
                {
                    continue;
                }

                if (ps.transform.IsChildOf(particleSystems[j].transform))
                {
                    isChildOfOther = true;
                    break;
                }
            }

            if (!isChildOfOther)
            {
                roots.Add(ps);
            }
        }

        return roots;
    }

    /// <summary>对所有根粒子 Simulate（withChildren），覆盖兄弟子树。</summary>
    public static void SimulateEffectParticles(List<ParticleSystem> particleSystemRoots, float playTiming)
    {
        if (particleSystemRoots == null || particleSystemRoots.Count == 0)
        {
            return;
        }

        for (var i = 0; i < particleSystemRoots.Count; i++)
        {
            var root = particleSystemRoots[i];
            if (root == null)
            {
                continue;
            }

            root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            root.Simulate(playTiming, withChildren: true, restart: true, fixedTimeStep: false);
            root.Pause(true);
        }
    }

    public static void ReleaseEditorEffectInstance(
        ref GameObject effectInstance,
        ref List<ParticleSystem> particleSystems,
        ref List<ParticleSystem> particleSystemRoots)
    {
        particleSystems = null;
        particleSystemRoots = null;
        if (effectInstance == null)
        {
            return;
        }

        Object.DestroyImmediate(effectInstance);
        effectInstance = null;
    }
}
