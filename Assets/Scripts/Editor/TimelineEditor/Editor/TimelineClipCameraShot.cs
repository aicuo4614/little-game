using System;
using System.Linq;
using DG.Tweening;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 相机运镜模板（编辑器预览）：按 Push-in / Orbit 采样 BattleCamera.RootTrans，可选 FOV。
/// </summary>
public class TimelineClipCameraShot : TimelineClipBase
{
    private static readonly string[] TruckSideLabels = { "向右", "向左" };
    private static readonly string[] HeightDirectionLabels = { "升高", "降低" };

    private TimelineClipCameraShotConfig thisConfig;
    private static UnitNodeType[] cameraNodes = Enum.GetValues(typeof(UnitNodeType)).Cast<UnitNodeType>()
        .Where(TimelineCameraPivotPlayback.IsNode).Distinct().ToArray();
    private static string[] cameraNodeLabels = cameraNodes.Select(node => node == UnitNodeType.BodyCenter
        ? "身体中心" : TimelineEnumDisplay.GetDisplayName(node)).ToArray();

    public override void Init(TimelineControl control, TimelineClipConfigBase config)
    {
        thisConfig = (TimelineClipCameraShotConfig)config;
        base.Init(control, config);
    }

    public override void DrawInspector()
    {
        base.DrawInspector();

        EditorGUILayout.LabelField("相机运镜", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "勾「从当前机位起」可沿用上一条运镜的机位坐标。\n"
                + "不勾则按下面的距离/高度摆绝对起点。正倒 scrub 都不抓实时机位。\n"
                + "镜间接驳请用「运镜过渡」。离开 Clip 不复位机位。",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();
        thisConfig.ShotType = TimelineEnumDisplay.DrawCameraShotTypePopup("运镜类型", thisConfig.ShotType);
        thisConfig.Use2DComposition = EditorGUILayout.Toggle("2D 构图", thisConfig.Use2DComposition);
        var planar = thisConfig.Use2DComposition;
        if (planar)
        {
            EditorGUILayout.HelpBox("锁定战斗机位的水平朝向，俯仰仍可对准注视点。3D 接回 2D 时可用「运镜过渡」平滑转回。", MessageType.None);
        }
        var shotType = thisConfig.ShotType;
        var isHold = shotType == TimelineCameraShotType.Hold;
        if (!isHold)
        {
            thisConfig.FromCurrentPose = EditorGUILayout.Toggle("从当前机位起", thisConfig.FromCurrentPose);
        }

        DrawPivot(false, "注视枢轴");
        if (TimelineCameraPivotPlayback.UsesFrom(thisConfig))
        {
            DrawPivot(true, shotType == TimelineCameraShotType.Ots ? "肩位枢轴" : "起始枢轴");
        }

        var fromCurrent = thisConfig.FromCurrentPose;
        if (!planar && !isHold && thisConfig.FromCurrentPose)
        {
            EditorGUILayout.HelpBox(
                "起点 = 上一条运镜终点（没有则用默认战斗机位）。不抓实时机位，正倒 scrub 一致。",
                MessageType.None);
        }

        // 2D 模式的横移以米为单位，3D 继续使用原环绕角参数。
        switch (shotType)
        {
            case TimelineCameraShotType.PushIn:
            case TimelineCameraShotType.DollyZoom:
                EditorGUILayout.HelpBox(
                    planar ? "沿固定朝向推近，枢轴随角色移动时机位一起跟随。"
                        : shotType == TimelineCameraShotType.DollyZoom
                        ? (fromCurrent
                            ? "沿起点→第 0 帧枢轴收近，同时强制驱动 FOV 对冲（推近拉宽 / 拉远收窄）。"
                            : "沿看向轴收放距离，同时强制驱动 FOV 对冲。")
                        : (fromCurrent
                            ? "沿起点→第 0 帧枢轴的直线收近到终点距离。枢轴锁定，不会绕。"
                            : "沿看向轴从起点距离收到终点距离。"),
                    MessageType.None);
                if (!fromCurrent)
                {
                    thisConfig.StartDistance = EditorGUILayout.FloatField("起点距离", thisConfig.StartDistance);
                    thisConfig.Height = EditorGUILayout.FloatField("高度", thisConfig.Height);
                }

                thisConfig.Distance = EditorGUILayout.FloatField("终点距离", thisConfig.Distance);
                break;
            case TimelineCameraShotType.PullOut:
                EditorGUILayout.HelpBox(
                    planar ? "沿固定朝向拉远，并跟随枢轴平移。"
                        : fromCurrent
                        ? "在当前水平距离上再拉开。水平角、高度保持起点。"
                        : "沿看向轴从起点距离再拉开。",
                    MessageType.None);
                if (!fromCurrent)
                {
                    thisConfig.StartDistance = EditorGUILayout.FloatField("起点距离", thisConfig.StartDistance);
                    thisConfig.Height = EditorGUILayout.FloatField("高度", thisConfig.Height);
                }

                thisConfig.MoveAmount = Mathf.Abs(EditorGUILayout.FloatField(
                    "拉开距离",
                    Mathf.Abs(thisConfig.MoveAmount)));
                break;
            case TimelineCameraShotType.BoomUp:
            case TimelineCameraShotType.BoomDown:
                EditorGUILayout.HelpBox(
                    fromCurrent ? "水平角和距离不变，只改高度。" : "停在指定距离和高度上，再升降。",
                    MessageType.None);
                if (!fromCurrent)
                {
                    thisConfig.Distance = EditorGUILayout.FloatField("水平距离", thisConfig.Distance);
                    thisConfig.Height = EditorGUILayout.FloatField("起始高度", thisConfig.Height);
                }

                thisConfig.MoveAmount = Mathf.Abs(EditorGUILayout.FloatField(
                    shotType == TimelineCameraShotType.BoomUp ? "升起高度" : "降下高度",
                    Mathf.Abs(thisConfig.MoveAmount)));
                break;
            case TimelineCameraShotType.Truck:
            {
                EditorGUILayout.HelpBox(
                    planar ? "沿固定机位的左右方向平移。阵营镜像会左右对调。"
                        : fromCurrent
                        ? "沿看向枢轴时的左右平移。高度不变。阵营镜像会左右对调。"
                        : "先摆到指定距离和高度，再左右平移。阵营镜像会左右对调。",
                    MessageType.None);
                if (!fromCurrent)
                {
                    thisConfig.Distance = EditorGUILayout.FloatField("水平距离", thisConfig.Distance);
                    thisConfig.Height = EditorGUILayout.FloatField("高度", thisConfig.Height);
                }

                var toLeft = thisConfig.MoveAmount < 0f;
                var truckAbs = Mathf.Abs(thisConfig.MoveAmount);
                var nextSide = EditorGUILayout.Popup("横移方向", toLeft ? 1 : 0, TruckSideLabels);
                truckAbs = EditorGUILayout.FloatField("横移距离", truckAbs);
                thisConfig.MoveAmount = nextSide == 1 ? -truckAbs : truckAbs;
                break;
            }
            case TimelineCameraShotType.Helix:
            case TimelineCameraShotType.DollyBoom:
            {
                if (planar)
                {
                    DrawPlanarTravel(fromCurrent);
                }
                else
                {
                    EditorGUILayout.HelpBox("同时转水平角、改半径和高度。", MessageType.None);
                    if (!fromCurrent)
                    {
                        thisConfig.OrbitStartYaw = EditorGUILayout.FloatField("起始水平角", thisConfig.OrbitStartYaw);
                        thisConfig.StartDistance = EditorGUILayout.FloatField("起点半径", thisConfig.StartDistance);
                        thisConfig.Height = EditorGUILayout.FloatField("起始高度", thisConfig.Height);
                    }
                    thisConfig.OrbitAngle = EditorGUILayout.FloatField("环绕角度", thisConfig.OrbitAngle);
                    thisConfig.Distance = EditorGUILayout.FloatField("终点半径", thisConfig.Distance);
                }
                var heightDown = thisConfig.MoveAmount < 0f;
                var heightAbs = Mathf.Abs(thisConfig.MoveAmount);
                var nextHeight = EditorGUILayout.Popup("高度方向", heightDown ? 1 : 0, HeightDirectionLabels);
                heightAbs = EditorGUILayout.FloatField("高度变化", heightAbs);
                thisConfig.MoveAmount = nextHeight == 1 ? -heightAbs : heightAbs;
                break;
            }
            case TimelineCameraShotType.PivotBlend:
                EditorGUILayout.HelpBox(
                    planar ? "通过平移机位，把起始枢轴平滑切换到终点枢轴。"
                        : fromCurrent
                        ? "机位锁在起点，注视点从起始枢轴插到终点枢轴。起始枢轴应对齐上一条的注视，避免第 0 帧拧一下。"
                        : "先摆到相对终点枢轴的距离/高度，再把注视点从起始插到终点。",
                    MessageType.None);
                if (!fromCurrent)
                {
                    thisConfig.Distance = EditorGUILayout.FloatField("水平距离", thisConfig.Distance);
                    thisConfig.Height = EditorGUILayout.FloatField("高度", thisConfig.Height);
                }

                break;
            case TimelineCameraShotType.WhipPan:
                EditorGUILayout.HelpBox(
                    planar ? "以固定朝向快速平移到终点枢轴。"
                        : fromCurrent
                        ? "机位锁在起点，朝向从进入机位扫到终点枢轴。起始枢轴此时不用。"
                        : "相对终点枢轴摆绝对机位，朝向从起始枢轴扫到终点枢轴。水平角 0=正后方，正=逆时针绕枢轴。",
                    MessageType.None);
                if (!fromCurrent)
                {
                    if (!planar)
                    {
                        thisConfig.OrbitStartYaw = EditorGUILayout.FloatField("机位水平角", thisConfig.OrbitStartYaw);
                        EditorGUILayout.HelpBox("0=正后方看向枢轴；±90≈侧面；±180≈正面。", MessageType.None);
                    }
                    thisConfig.Distance = EditorGUILayout.FloatField("水平距离", thisConfig.Distance);
                    thisConfig.Height = EditorGUILayout.FloatField("高度", thisConfig.Height);
                }

                break;
            case TimelineCameraShotType.Ots:
            {
                EditorGUILayout.HelpBox(
                    planar ? "以肩位枢轴摆放相机，保持水平朝向，只向注视枢轴调整俯仰。"
                        : fromCurrent
                        ? "从当前机位接到过肩构图：肩后看向注视枢轴（默认目标）。需「设置目标」。"
                        : "整段落在过肩构图。需「设置目标」。",
                    MessageType.None);
                thisConfig.Distance = EditorGUILayout.FloatField("身后距离", thisConfig.Distance);
                thisConfig.Height = EditorGUILayout.FloatField("相对肩位高度", thisConfig.Height);
                var otsLeft = thisConfig.MoveAmount < 0f;
                var otsAbs = Mathf.Abs(thisConfig.MoveAmount);
                var otsSide = EditorGUILayout.Popup("肩侧", otsLeft ? 1 : 0, TruckSideLabels);
                otsAbs = EditorGUILayout.FloatField("肩侧偏移", otsAbs);
                thisConfig.MoveAmount = otsSide == 1 ? -otsAbs : otsAbs;
                break;
            }
            case TimelineCameraShotType.Crane:
            {
                EditorGUILayout.HelpBox(
                    planar ? "沿固定机位的右向横移，同时升降。"
                        : fromCurrent
                        ? "从起点沿机位右向横移 + 世界上升降（平移，不是绕枢轴转）。阵营镜像会左右对调。"
                        : "相对枢轴摆绝对起点，再沿机位右向横移 + 升降。水平角 0=正后方。",
                    MessageType.None);
                if (!fromCurrent)
                {
                    if (!planar)
                    {
                        thisConfig.OrbitStartYaw = EditorGUILayout.FloatField("机位水平角", thisConfig.OrbitStartYaw);
                        EditorGUILayout.HelpBox("0=正后方看向枢轴；±90≈侧面；±180≈正面。", MessageType.None);
                    }
                    thisConfig.Distance = EditorGUILayout.FloatField("水平距离", thisConfig.Distance);
                    thisConfig.Height = EditorGUILayout.FloatField("起始高度", thisConfig.Height);
                }

                var craneLeft = thisConfig.MoveAmount < 0f;
                var craneTruckAbs = Mathf.Abs(thisConfig.MoveAmount);
                var craneSide = EditorGUILayout.Popup("横移方向", craneLeft ? 1 : 0, TruckSideLabels);
                craneTruckAbs = EditorGUILayout.FloatField("横移距离", craneTruckAbs);
                thisConfig.MoveAmount = craneSide == 1 ? -craneTruckAbs : craneTruckAbs;
                var craneDown = thisConfig.BoomAmount < 0f;
                var craneBoomAbs = Mathf.Abs(thisConfig.BoomAmount);
                var craneHeight = EditorGUILayout.Popup("高度方向", craneDown ? 1 : 0, HeightDirectionLabels);
                craneBoomAbs = EditorGUILayout.FloatField("高度变化", craneBoomAbs);
                thisConfig.BoomAmount = craneHeight == 1 ? -craneBoomAbs : craneBoomAbs;
                break;
            }
            case TimelineCameraShotType.Hold:
                thisConfig.HoldKeepEntryPose = EditorGUILayout.Toggle(
                    "保持进入机位",
                    thisConfig.HoldKeepEntryPose);
                if (thisConfig.HoldKeepEntryPose)
                {
                    EditorGUILayout.HelpBox(
                        planar ? "沿用进入时的机位坐标，保持水平朝向并调整俯仰。"
                            : "不填机位坐标。机位停在上一条运镜终点（没有则默认战斗机位）。仍 LookAt 枢轴；若枢轴会动，请再勾「锁定第一帧机位」。",
                        MessageType.None);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        planar ? "机位坐标是相对注视枢轴的世界轴偏移（米），阵营镜像时翻转 X。"
                            : "机位随枢轴、按世界轴偏移摆放（米），阵营镜像时翻转 X。",
                        MessageType.None);
                    thisConfig.HoldOffset = EditorGUILayout.Vector3Field("机位坐标", thisConfig.HoldOffset);
                }

                thisConfig.HoldLockFirstFramePose = EditorGUILayout.Toggle(
                    "锁定第一帧机位",
                    thisConfig.HoldLockFirstFramePose);
                if (thisConfig.HoldLockFirstFramePose)
                {
                    EditorGUILayout.HelpBox(
                        "注视基点锁在本条第 0 帧；之后角色再动也不跟。枢轴偏移、机位坐标仍用当前值相对锁住的基点重算，改完立刻可见。FOV 仍可按时间插值。",
                        MessageType.None);
                }

                break;
            case TimelineCameraShotType.Orbit:
            default:
                if (planar)
                {
                    DrawPlanarTravel(fromCurrent);
                    break;
                }
                thisConfig.Distance = EditorGUILayout.FloatField("环绕半径", thisConfig.Distance);
                EditorGUI.BeginDisabledGroup(fromCurrent);
                thisConfig.OrbitStartYaw = EditorGUILayout.FloatField("起始水平角", thisConfig.OrbitStartYaw);
                EditorGUI.EndDisabledGroup();
                thisConfig.OrbitAngle = EditorGUILayout.FloatField("环绕角度", thisConfig.OrbitAngle);
                EditorGUILayout.HelpBox("正=逆时针，负=顺时针。", MessageType.None);
                thisConfig.Height = EditorGUILayout.FloatField("高度", thisConfig.Height);
                break;
        }
        thisConfig.PathEase = (Ease)EditorGUILayout.EnumPopup("路径缓动", thisConfig.PathEase);
        thisConfig.MirrorByTeam = EditorGUILayout.Toggle("阵营镜像", thisConfig.MirrorByTeam);
        if (!planar)
        {
            thisConfig.LockCameraUpright = EditorGUILayout.Toggle("锁定水平", thisConfig.LockCameraUpright);
        }

        var isDollyZoom = shotType == TimelineCameraShotType.DollyZoom;
        if (isDollyZoom)
        {
            thisConfig.AnimateFov = true;
            EditorGUILayout.LabelField("驱动FOV", "强制开启（变焦推）");
            EditorGUILayout.HelpBox(
                "推近时把 FOV终点 调大（拉宽），拉远时调小（收窄），才有眩晕对冲。默认 FOV起点→终点请按距离反向设。",
                MessageType.None);
        }
        else
        {
            thisConfig.AnimateFov = EditorGUILayout.Toggle("驱动FOV", thisConfig.AnimateFov);
        }

        if (thisConfig.AnimateFov || isDollyZoom)
        {
            var fovFromEntry = (thisConfig.FromCurrentPose && !isHold)
                || (isHold && thisConfig.HoldKeepEntryPose);
            EditorGUI.BeginDisabledGroup(fovFromEntry);
            thisConfig.FovStart = EditorGUILayout.FloatField("FOV起点", thisConfig.FovStart);
            EditorGUI.EndDisabledGroup();
            thisConfig.FovEnd = EditorGUILayout.FloatField("FOV终点", thisConfig.FovEnd);
            if (fovFromEntry)
            {
                EditorGUILayout.HelpBox("FOV 从起点机位接到 FOV终点。", MessageType.None);
            }
        }

        if (EditorGUI.EndChangeCheck())
        {
            RefreshPreview();
        }
    }

    private void DrawPlanarTravel(bool fromCurrent)
    {
        if (!fromCurrent)
        {
            thisConfig.StartDistance = EditorGUILayout.FloatField("起点水平距离", thisConfig.StartDistance);
            thisConfig.Height = EditorGUILayout.FloatField("起始高度", thisConfig.Height);
        }
        thisConfig.Distance = EditorGUILayout.FloatField("终点水平距离", thisConfig.Distance);
        thisConfig.PlanarTravel = EditorGUILayout.FloatField("横移量（米，右正左负）", thisConfig.PlanarTravel);
    }

    private void DrawPivot(bool from, string label)
    {
        var pivot = TimelineCameraPivotPlayback.Data(thisConfig, from);
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        var previousSubject = pivot.Subject;
        pivot.Subject = (TimelineUnitSubjectType)EditorGUILayout.Popup(TimelineBindingEditorText.Subject,
            (int)pivot.Subject, new[] { TimelineBindingEditorText.Caster, TimelineBindingEditorText.EffectSource,
                TimelineBindingEditorText.EffectTargets, TimelineBindingEditorText.CastTargets });
        if (pivot.Subject != previousSubject)
        {
            pivot.BindingKey = null;
            pivot.IncludeCaster = false;
        }
        if (pivot.Subject != TimelineUnitSubjectType.Caster)
        {
            pivot.BindingKey = control.PreviewBindings.DrawBindingSelector(TimelineBindingEditorText.SubjectBinding,
                pivot.BindingKey, pivot.Subject != TimelineUnitSubjectType.CastTargets, config);
            pivot.IncludeCaster = EditorGUILayout.Toggle("取与施法者的中点", pivot.IncludeCaster);
        }
        if (pivot.Subject is TimelineUnitSubjectType.CastTargets or TimelineUnitSubjectType.EffectTargets)
        {
            pivot.Selection = TimelineUnitBindingControl.DrawReferenceSelection(
                TimelineMoveDestinationType.CastTargets, pivot.Selection);
        }
        var nodeIndex = EditorGUILayout.Popup("注视节点", Array.IndexOf(cameraNodes, pivot.NodeType), cameraNodeLabels);
        if (nodeIndex >= 0)
        {
            pivot.NodeType = cameraNodes[nodeIndex];
        }
        pivot.Offset = EditorGUILayout.Vector3Field("世界轴偏移（米）", pivot.Offset);
        pivot.LockAtEntry = EditorGUILayout.Toggle("锁定片段起点", pivot.LockAtEntry);
        if (EditorGUI.EndChangeCheck())
        {
            SavePivot(from, pivot);
            GUI.changed = true;
        }

        var actors = control.UnitBindings.Resolve(config, pivot.Subject, pivot.BindingKey);
        if (actors.Count == 0 && pivot.Subject != TimelineUnitSubjectType.Caster)
        {
            EditorGUILayout.HelpBox(string.IsNullOrEmpty(pivot.BindingKey)
                ? "当前没有匹配的预览对象，取施法者位置。"
                : "所选绑定没有可用的预览对象。", MessageType.Warning);
        }
        if (pivot.IncludeCaster)
        {
            actors.AddRange(control.UnitBindings.Resolve(config, TimelineUnitSubjectType.Caster, null));
        }
        foreach (var actor in actors.Distinct())
        {
            if (!actor.HasCameraNode(pivot.NodeType))
            {
                EditorGUILayout.HelpBox($"{actor.Root.name} 缺少注视节点 {pivot.NodeType}，使用身体中心。", MessageType.Warning);
            }
        }
    }

    /// <summary>两端手柄分别回写世界轴偏移，并与共享播放器的注视点对齐。</summary>
    public override void OnSceneGUI(SceneView sceneView)
    {
        base.OnSceneGUI(sceneView);
        var casters = control.UnitBindings.Resolve(config, TimelineUnitSubjectType.Caster, null);
        if (casters.Count == 0)
        {
            return;
        }

        var caster = casters[0];
        var mirror = TimelineTransformUtil.NeedsTeamCameraMirror(thisConfig.MirrorByTeam, caster.Team);
        DrawHandle(false, "注视枢轴", Color.red);
        if (TimelineCameraPivotPlayback.UsesFrom(thisConfig))
        {
            DrawHandle(true, thisConfig.ShotType == TimelineCameraShotType.Ots ? "肩位枢轴" : "起始枢轴", Color.yellow);
        }

        void DrawHandle(bool from, string label, Color color)
        {
            var data = TimelineCameraPivotPlayback.Data(thisConfig, from);
            if (!control.SharedPlayback.TryGetCameraPivot(config.ClipKey, from, out var point))
            {
                try
                {
                    point = new TimelineCameraPivotPlayback(data, config, control.UnitBindings.Resolve, caster, mirror).Point;
                }
                catch (ArgumentException)
                {
                    return;
                }
            }

            using (new Handles.DrawingScope(color))
            {
                var size = HandleUtility.GetHandleSize(point);
                Handles.Label(point + Vector3.up * size * 0.22f, label);
                EditorGUI.BeginChangeCheck();
                var next = Handles.PositionHandle(point, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    data.Offset += TimelineTransformUtil.ToAuthoringPos(next - point, mirror);
                    SavePivot(from, data);
                    RefreshPreview();
                }
            }
        }
    }

    private void SavePivot(bool from, TimelineCameraPivotData data)
    {
        if (from)
        {
            thisConfig.FromPivot = data;
        }
        else
        {
            thisConfig.LookPivot = data;
        }
    }

    private void RefreshPreview()
    {
        control.SharedPlayback.Invalidate();
        control.Timeline.Repaint();
        SceneView.RepaintAll();
    }
}
