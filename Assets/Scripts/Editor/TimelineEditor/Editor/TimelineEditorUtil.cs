using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TimelineEditorUtil
{
    /// <summary>
    /// 稳定高度的说明文字。避免 EditorGUILayout.HelpBox（尤其 MessageType.None）
    /// 在 ScrollView 内 Layout/Repaint 高度不一致，导致下方勾选框点击热区相对绘制错位。
    /// </summary>
    public static void DrawStableHelp(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        var content = new GUIContent(message);
        var width = EditorGUIUtility.currentViewWidth;
        if (width < 80f)
        {
            width = 200f;
        }

        // 预留滚动条与内边距，Layout/Repaint 用同一宽度算高
        var textWidth = Mathf.Max(40f, width - 36f);
        var height = EditorStyles.helpBox.CalcHeight(content, textWidth);
        var rect = EditorGUILayout.GetControlRect(false, height);
        EditorGUI.LabelField(rect, content, EditorStyles.helpBox);
    }

    public static void PointDataInspector(
        TimelineControl control,
        string title,
        TimelineClipPointData pointData,
        out bool isBigChange,
        out bool isSmallChange,
        bool showSectionHeader = true,
        bool showUseOriginalOptions = false,
        bool showDontFollowOption = false,
        UnitMono nodeHost = null,
        bool stopPreviewOnChange = true)
    {
        isBigChange = false;
        isSmallChange = false;
        if (showSectionHeader)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        EditorGUI.BeginChangeCheck();
        var nextNodeType = TimelineEnumDisplay.DrawNodePopup("参考节点", pointData.OffsetNodeType);
        if (EditorGUI.EndChangeCheck())
        {
            if (TryAssignOffsetNodeType(pointData, nextNodeType, nodeHost))
            {
                isBigChange = true;
                if (stopPreviewOnChange)
                {
                    control.Timeline.StopPreView();
                }
            }
        }

        var canSetParent = pointData.OffsetNodeType != UnitNodeType.World
            && pointData.OffsetNodeType != UnitNodeType.GroupCenter
            && pointData.OffsetNodeType != UnitNodeType.UnitOriginPos;
        EditorGUI.BeginChangeCheck();
        EditorGUI.BeginDisabledGroup(!canSetParent);
        pointData.NeedSetParent = EditorGUILayout.Toggle("挂载到节点", pointData.NeedSetParent);
        EditorGUI.EndDisabledGroup();
        if (!canSetParent)
        {
            pointData.NeedSetParent = false;
        }

        if (pointData.NeedSetParent)
        {
            pointData.DontFollow = false;
        }

        // 始终占位，避免勾选挂载后「不跟随」消失导致下方控件 ID 错位
        if (showDontFollowOption)
        {
            EditorGUI.BeginDisabledGroup(pointData.NeedSetParent);
            pointData.DontFollow = EditorGUILayout.Toggle("不跟随", pointData.DontFollow);
            EditorGUI.EndDisabledGroup();
        }

        if (EditorGUI.EndChangeCheck())
        {
            isBigChange = true;
            if (stopPreviewOnChange)
                {
                    control.Timeline.StopPreView();
                }
        }

        if (showUseOriginalOptions)
        {
            EditorGUI.BeginChangeCheck();
            pointData.UseOriginalRotation = EditorGUILayout.Toggle("重置旋转", pointData.UseOriginalRotation);
            // 始终占位，避免勾选后插入「叠加角色朝向」导致下方控件错位
            EditorGUI.BeginDisabledGroup(!pointData.UseOriginalRotation);
            EditorGUI.indentLevel++;
            pointData.OverlayCharacterFacing = EditorGUILayout.Toggle(
                "叠加角色朝向",
                pointData.OverlayCharacterFacing);
            EditorGUI.indentLevel--;
            EditorGUI.EndDisabledGroup();

            pointData.UseOriginalScale = EditorGUILayout.Toggle("重置缩放", pointData.UseOriginalScale);
            if (EditorGUI.EndChangeCheck())
            {
                isBigChange = true;
                if (stopPreviewOnChange)
                {
                    control.Timeline.StopPreView();
                }
            }

            DrawStableHelp(
                "勾选「重置旋转」：从 (0,0,0) 起算再叠相对欧拉；勾选「重置缩放」：从 (1,1,1) 起算再乘相对缩放。"
                + (pointData.UseOriginalRotation
                    ? "「叠加角色朝向」勾选时：最终旋转 = (0,0,0) * 作用角色Y朝向 * 相对欧拉。"
                    : string.Empty));
        }

        EditorGUI.BeginChangeCheck();
        switch (pointData.OffsetNodeType)
        {
            case UnitNodeType.World:
                pointData.OffsetPos = EditorGUILayout.Vector3Field("世界位置", pointData.OffsetPos);
                pointData.OffsetEuler = EditorGUILayout.Vector3Field(
                    pointData.UseOriginalRotation && showUseOriginalOptions ? "相对旋转(从0叠)" : "世界旋转",
                    pointData.OffsetEuler);
                break;
            case UnitNodeType.GroupCenter:
                pointData.OffsetPos = EditorGUILayout.Vector3Field("相对偏移", pointData.OffsetPos);
                pointData.OffsetEuler = EditorGUILayout.Vector3Field(
                    pointData.UseOriginalRotation && showUseOriginalOptions ? "相对旋转(从0叠)" : "相对旋转",
                    pointData.OffsetEuler);
                break;
            default:
                pointData.OffsetPos = EditorGUILayout.Vector3Field("相对位置", pointData.OffsetPos);
                pointData.OffsetEuler = EditorGUILayout.Vector3Field(
                    pointData.UseOriginalRotation && showUseOriginalOptions ? "相对旋转(从0叠)" : "相对旋转",
                    pointData.OffsetEuler);
                break;
        }

        pointData.OffsetScale = EditorGUILayout.Vector3Field(
            pointData.UseOriginalScale && showUseOriginalOptions ? "缩放(从1乘)" : "缩放",
            pointData.OffsetScale);
        if (EditorGUI.EndChangeCheck())
        {
            isSmallChange = true;
        }
    }

    /// <summary>虚拟节点（不依赖 UnitNodeDatas）始终可选；骨骼节点需在 host 上能 Find 到 Transform。</summary>
    public static bool IsVirtualOffsetNode(UnitNodeType nodeType)
    {
        return nodeType == UnitNodeType.None
            || nodeType == UnitNodeType.World
            || nodeType == UnitNodeType.GroupCenter
            || nodeType == UnitNodeType.UnitOriginPos;
    }

    /// <summary>
    /// 尝试写入参考节点；找不到挂点时弹窗并拒绝修改。
    /// </summary>
    /// <returns>是否已写入新值。</returns>
    public static bool TryAssignOffsetNodeType(TimelineClipPointData pointData, UnitNodeType nextNodeType, UnitMono nodeHost)
    {
        if (pointData == null || pointData.OffsetNodeType == nextNodeType)
        {
            return false;
        }

        if (IsVirtualOffsetNode(nextNodeType))
        {
            pointData.OffsetNodeType = nextNodeType;
            return true;
        }

        if (nodeHost == null)
        {
            EditorUtility.DisplayDialog(
                "无法选择参考节点",
                $"当前没有可校验的单位，无法选择「{TimelineEnumDisplay.GetDisplayName(nextNodeType)}」。\n请确认已绑定施法者，且目标查询能解析到单位（依赖目标时请先添加「设置目标」Clip）。",
                "确定");
            return false;
        }

        if (nodeHost.FindNodeTransform(nextNodeType) == null)
        {
            EditorUtility.DisplayDialog(
                "参考节点不存在",
                $"单位「{nodeHost.name}」上未绑定「{TimelineEnumDisplay.GetDisplayName(nextNodeType)}」。\n请先在怪物编辑器绑定并保存 *_Node_Data.json，或改选已有节点。",
                "确定");
            return false;
        }

        pointData.OffsetNodeType = nextNodeType;
        return true;
    }

    /// <summary>受击特效挂点使用明确选择的预览受击对象。</summary>
    public static UnitMono ResolveHitEffectNodeHost(TimelineControl control, TimelineClipConfigBase consumer = null)
    {
        var targets = control.PreviewBindings.GetTargets(consumer, true);
        return targets.Count > 0 ? targets[0] : null;
    }

    /// <summary>施法者 UnitMono。</summary>
    public static UnitMono ResolveCasterNodeHost(TimelineControl control)
    {
        return control.MyTransform != null ? control.MyTransform.GetComponent<UnitMono>() : null;
    }

    private static List<UnitMono> pointDataHandleUnits = new List<UnitMono>();

    /// <summary>
    /// Scene 手柄编辑 <see cref="TimelineClipPointData"/> 的相对位置 / 旋转 / 缩放。
    /// 平移：世界轴 PositionHandle + 球体 FreeMove；旋转：世界轴 Disc；缩放：ScaleHandle。
    /// <see cref="UnitNodeType.UnitOriginPos"/> 位置固定在单位原点，不可拖位置。
    /// </summary>
    /// <returns>是否改写了任意偏移字段。</returns>
    public static bool DrawPointDataSceneHandles(
        TimelineClipPointData pointData,
        UnitMono host,
        string label)
    {
        if (pointData == null || host == null)
        {
            return false;
        }

        pointDataHandleUnits.Clear();
        pointDataHandleUnits.Add(host);
        TimelineTransformUtil.GetTransformationFromUnitNodePointData(
            host.transform.position,
            pointDataHandleUnits,
            pointData,
            out var worldPos,
            out var queryRot,
            out var queryScale);

        // 与 ApplyEffectTransform 一致：勾选重置旋转/缩放时手柄显示生效位姿
        var worldRot = pointData.UseOriginalRotation
            ? Quaternion.Euler(TimelineTransformUtil.ResolveOriginalRotationFacingOverlayEuler(pointData, host))
                * Quaternion.Euler(pointData.OffsetEuler)
            : queryRot;
        var worldScale = pointData.UseOriginalScale ? pointData.OffsetScale : queryScale;

        if (!IsFinite(worldPos) || !IsFinite(worldRot) || !IsFinite(worldScale))
        {
            return false;
        }

        var suppressId = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(suppressId);

        var handleSize = HandleUtility.GetHandleSize(worldPos);
        var markerColor = new Color(0.3f, 0.8f, 1f, 1f);
        Handles.color = markerColor;
        Handles.SphereHandleCap(0, worldPos, Quaternion.identity, handleSize * 0.12f, EventType.Repaint);
        Handles.Label(worldPos + Vector3.up * handleSize * 0.28f, label ?? "挂点");

        var changed = false;
        var canMovePos = pointData.OffsetNodeType != UnitNodeType.UnitOriginPos;

        if (!canMovePos)
        {
            Handles.color = Color.yellow;
            Handles.Label(
                worldPos + Vector3.up * handleSize * 0.42f,
                "单位原点：位置固定，可调旋转/缩放");
        }
        else
        {
            EditorGUI.BeginChangeCheck();
            var freePos = Handles.FreeMoveHandle(
                worldPos,
                handleSize * 0.16f,
                Vector3.zero,
                Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck() && IsFinite(freePos))
            {
                WriteBackPointDataOffsetPos(pointData, host, freePos);
                worldPos = freePos;
                changed = true;
            }

            EditorGUI.BeginChangeCheck();
            var axisPos = Handles.PositionHandle(worldPos, Quaternion.identity);
            if (EditorGUI.EndChangeCheck() && IsFinite(axisPos))
            {
                WriteBackPointDataOffsetPos(pointData, host, axisPos);
                worldPos = axisPos;
                changed = true;
            }
        }

        Handles.Label(worldPos + Vector3.up * handleSize * 0.5f, "相对旋转(世界轴)");
        EditorGUI.BeginChangeCheck();
        var newWorldRot = DoWorldAxisRotationHandle(worldRot, worldPos);
        if (EditorGUI.EndChangeCheck() && IsFinite(newWorldRot))
        {
            if (pointData.UseOriginalRotation)
            {
                var facing = Quaternion.Euler(
                    TimelineTransformUtil.ResolveOriginalRotationFacingOverlayEuler(pointData, host));
                pointData.OffsetEuler = (Quaternion.Inverse(facing) * newWorldRot).eulerAngles;
            }
            else
            {
                WriteBackPointDataOffsetEuler(pointData, host, worldPos, newWorldRot);
            }

            worldRot = newWorldRot;
            changed = true;
        }

        Handles.Label(worldPos + Vector3.up * handleSize * 0.68f, "缩放");
        EditorGUI.BeginChangeCheck();
        var newScale = Handles.ScaleHandle(worldScale, worldPos, worldRot, handleSize);
        if (EditorGUI.EndChangeCheck() && IsFinite(newScale))
        {
            if (pointData.UseOriginalScale)
            {
                pointData.OffsetScale = newScale;
            }
            else
            {
                WriteBackPointDataOffsetScale(pointData, host, newScale);
            }

            changed = true;
        }

        return changed;
    }

    /// <summary>世界坐标旋转手柄：绕世界 X/Y/Z 圆环 + 自由旋转球。</summary>
    public static Quaternion DoWorldAxisRotationHandle(Quaternion worldRot, Vector3 position)
    {
        var size = HandleUtility.GetHandleSize(position);
        var newRot = worldRot;
        var oldColor = Handles.color;

        Handles.color = Handles.xAxisColor;
        newRot = Handles.Disc(newRot, position, Vector3.right, size, true, EditorSnapSettings.rotate);
        Handles.color = Handles.yAxisColor;
        newRot = Handles.Disc(newRot, position, Vector3.up, size, true, EditorSnapSettings.rotate);
        Handles.color = Handles.zAxisColor;
        newRot = Handles.Disc(newRot, position, Vector3.forward, size, true, EditorSnapSettings.rotate);
        Handles.color = Handles.centerColor;
        newRot = Handles.FreeRotateHandle(newRot, position, size * 0.2f);

        Handles.color = oldColor;
        return newRot;
    }

    private static void WriteBackPointDataOffsetPos(
        TimelineClipPointData pointData,
        UnitMono host,
        Vector3 worldPos)
    {
        switch (pointData.OffsetNodeType)
        {
            case UnitNodeType.World:
                pointData.OffsetPos = worldPos;
                break;
            case UnitNodeType.GroupCenter:
            {
                pointDataHandleUnits.Clear();
                pointDataHandleUnits.Add(host);
                var teamCenter = TimelineTransformUtil.GetTeamCenter(pointDataHandleUnits);
                var direction = host.transform.position - teamCenter;
                if (direction.sqrMagnitude < 0.0001f)
                {
                    pointData.OffsetPos = worldPos - teamCenter;
                }
                else
                {
                    var rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                    pointData.OffsetPos = Quaternion.Inverse(rotation) * (worldPos - teamCenter);
                }

                break;
            }
            case UnitNodeType.UnitOriginPos:
                break;
            default:
            {
                var node = host.FindNodeTransform(pointData.OffsetNodeType);
                if (node == null)
                {
                    node = host.Transform;
                }

                pointData.OffsetPos = node.InverseTransformPoint(worldPos);
                break;
            }
        }
    }

    private static void WriteBackPointDataOffsetEuler(
        TimelineClipPointData pointData,
        UnitMono host,
        Vector3 worldPos,
        Quaternion worldRot)
    {
        switch (pointData.OffsetNodeType)
        {
            case UnitNodeType.World:
            case UnitNodeType.UnitOriginPos:
                pointData.OffsetEuler = worldRot.eulerAngles;
                break;
            case UnitNodeType.GroupCenter:
            {
                pointDataHandleUnits.Clear();
                pointDataHandleUnits.Add(host);
                var teamCenter = TimelineTransformUtil.GetTeamCenter(pointDataHandleUnits);
                var toCenter = teamCenter - worldPos;
                if (toCenter.sqrMagnitude < 0.0001f)
                {
                    pointData.OffsetEuler = worldRot.eulerAngles;
                }
                else
                {
                    var basis = Quaternion.LookRotation(toCenter.normalized, Vector3.up);
                    pointData.OffsetEuler = (Quaternion.Inverse(basis) * worldRot).eulerAngles;
                }

                break;
            }
            default:
            {
                var node = host.FindNodeTransform(pointData.OffsetNodeType);
                if (node == null)
                {
                    node = host.Transform;
                }

                pointData.OffsetEuler = (Quaternion.Inverse(node.rotation) * worldRot).eulerAngles;
                break;
            }
        }
    }

    private static void WriteBackPointDataOffsetScale(
        TimelineClipPointData pointData,
        UnitMono host,
        Vector3 worldScale)
    {
        switch (pointData.OffsetNodeType)
        {
            case UnitNodeType.World:
            case UnitNodeType.GroupCenter:
            case UnitNodeType.UnitOriginPos:
                pointData.OffsetScale = worldScale;
                break;
            default:
            {
                var node = host.FindNodeTransform(pointData.OffsetNodeType);
                if (node == null)
                {
                    node = host.Transform;
                }

                var lossy = node.lossyScale;
                pointData.OffsetScale = new Vector3(
                    DivideScaleAxis(worldScale.x, lossy.x),
                    DivideScaleAxis(worldScale.y, lossy.y),
                    DivideScaleAxis(worldScale.z, lossy.z));
                break;
            }
        }
    }

    private static float DivideScaleAxis(float world, float parent)
    {
        if (Mathf.Abs(parent) < 0.0001f)
        {
            return world;
        }

        return world / parent;
    }

    private static bool IsFinite(Vector3 v)
    {
        return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
            || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }

    private static bool IsFinite(Quaternion q)
    {
        return !(float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w)
            || float.IsInfinity(q.x) || float.IsInfinity(q.y) || float.IsInfinity(q.z) || float.IsInfinity(q.w));
    }
}
