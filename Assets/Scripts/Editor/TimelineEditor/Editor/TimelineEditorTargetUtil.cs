using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 技能编辑器：战斗目标（「设置目标」Clip）依赖校验与安全取值，避免空目标导致 NaN / NRE。
/// </summary>
public static class TimelineEditorTargetUtil
{
    public const string MissingTargetClipHint =
        "请先添加配置施法对象 Clip。";


    /// <summary>平移类型是否依赖战斗目标列表。</summary>
    public static bool NeedsBattleTargets(TranslateMovePointType pointType)
    {
        return pointType == TranslateMovePointType.TranslateToTarget
            || pointType == TranslateMovePointType.TranslateToSelfTargetCenter;
    }

    /// <summary>旋转类型是否依赖战斗目标列表。</summary>
    public static bool NeedsBattleTargets(RotateMovePointType pointType)
    {
        return pointType == RotateMovePointType.RotateToTarget
            || pointType == RotateMovePointType.RotateToSelfTargetCenter;
    }

    /// <summary>整段 Clip 是否强依赖「设置目标」（添加时拦截）。</summary>
    public static bool ClipTypeRequiresSetTargets(TimelineClipType type)
    {
        return type == TimelineClipType.RoleRotateToTarget;
    }

    /// <summary>时间轴上是否已配置「设置目标」Clip（与预览运行时目标缓存无关）。</summary>
    public static bool HasSetTargetsClip(TimelineControl control)
    {
        return control.Clips.Exists(clip => clip.Config is TimelineClipSetCastTargetsConfig);
    }

    /// <summary>缺少「设置目标」时弹窗；返回 true 表示已提示且应拦截操作。</summary>
    public static bool ShowMissingSetTargetsDialogIfNeeded(TimelineControl control)
    {
        if (HasSetTargetsClip(control))
        {
            return false;
        }

        EditorUtility.DisplayDialog("缺少预览对象", MissingTargetClipHint, "确定");
        return true;
    }

    /// <summary>尝试写入平移类型；依赖目标且无 Clip 时弹窗并拒绝。</summary>
    public static bool TryAssignTranslateMovePointType(
        TimelineControl control,
        TimelineClipMoveConfig config,
        TranslateMovePointType next)
    {
        if (config == null || config.TranslateMovePointType == next)
        {
            return false;
        }

        if (NeedsBattleTargets(next) && ShowMissingSetTargetsDialogIfNeeded(control))
        {
            return false;
        }

        config.TranslateMovePointType = next;
        return true;
    }

    /// <summary>尝试写入旋转类型；依赖目标且无 Clip 时弹窗并拒绝。</summary>
    public static bool TryAssignRotateMovePointType(
        TimelineControl control,
        TimelineClipMoveConfig config,
        RotateMovePointType next)
    {
        if (config == null || config.RotateMovePointType == next)
        {
            return false;
        }

        if (NeedsBattleTargets(next) && ShowMissingSetTargetsDialogIfNeeded(control))
        {
            return false;
        }

        config.RotateMovePointType = next;
        return true;
    }

    /// <summary>尝试获取当前战斗目标；为空时可选警告并返回 false。</summary>
    public static bool TryGetTargets(
        TimelineControl control,
        out List<UnitMono> targets,
        string context = null,
        bool logWarning = true)
    {
        targets = control.TargetControl.FindTargets();
        if (targets.Count > 0)
        {
            return true;
        }

        // 已有「设置目标」Clip 时，空列表多半是预览未播过 / 保存后 Clear，不算配置缺失
        if (logWarning && !HasSetTargetsClip(control))
        {
            WarnMissingTargets(context);
        }

        return false;
    }

    /// <summary>
    /// 编辑态取目标：优先用预览缓存；若为空则按 <paramref name="atTimelineTime"/> 之前最近的「设置目标」干跑 Query（不依赖播放）。
    /// </summary>
    /// <param name="atTimelineTime">通常传依赖方 Clip 的内容开始时间。</param>
    /// <returns>是否拿到至少一个目标。返回的列表可能是内部缓冲，勿跨帧缓存引用。</returns>
    public static bool TryGetTargetsForAuthoring(
        TimelineControl control,
        float atTimelineTime,
        out List<UnitMono> targets,
        string context = null,
        bool logWarning = false)
    {
        targets = control.TargetControl.FindTargets();
        if (targets.Count > 0)
        {
            return true;
        }


        if (logWarning && !HasSetTargetsClip(control))
        {
            WarnMissingTargets(context);
        }

        return false;
    }

    /// <summary>编辑态取目标队伍中心（含设置目标干跑）。</summary>
    public static bool TryGetBattleTargetCenterForAuthoring(
        TimelineControl control,
        float atTimelineTime,
        out Vector3 center,
        string context = null,
        bool logWarning = false)
    {
        center = Vector3.zero;
        if (!TryGetTargetsForAuthoring(control, atTimelineTime, out var targets, context, logWarning))
        {
            return false;
        }

        return TryGetTeamCenter(targets, out center);
    }

    /// <summary>解释当前时间无法解析施法对象的原因。</summary>
    public static string GetAuthoringTargetFailureHint(TimelineControl control, float atTimelineTime)
    {
        if (!HasSetTargetsClip(control))
        {
            return MissingTargetClipHint;
        }

        if (control.Clips.Exists(clip => clip.Config is TimelineClipSetCastTargetsConfig
            && clip.GetContentTimelineStartTime() <= atTimelineTime + 1e-4f))
        {
            return "请检查施法对象绑定中的预览单位。";
        }

        return "施法对象绑定的起点晚于本片段。";
    }

    /// <summary>尝试获取第一个战斗目标；为空时可选警告并返回 false。</summary>
    public static bool TryGetFirstTarget(
        TimelineControl control,
        out UnitMono target,
        string context = null,
        bool logWarning = true)
    {
        target = control.TargetControl.GetFirstTarget();
        if (target != null)
        {
            return true;
        }

        if (logWarning && !HasSetTargetsClip(control))
        {
            WarnMissingTargets(context);
        }

        return false;
    }

    /// <summary>尝试计算目标队伍中心；空列表返回 false，绝不产生 NaN。</summary>
    public static bool TryGetTeamCenter(IReadOnlyList<UnitMono> units, out Vector3 center)
    {
        if (units == null || units.Count == 0)
        {
            center = Vector3.zero;
            return false;
        }

        center = TimelineTransformUtil.GetTeamCenter(units);
        return true;
    }

    /// <summary>
    /// 从控制台取目标并算中心；无目标时可选警告并返回 false。
    /// </summary>
    public static bool TryGetBattleTargetCenter(
        TimelineControl control,
        out Vector3 center,
        string context = null,
        bool logWarning = true)
    {
        center = Vector3.zero;
        if (!TryGetTargets(control, out var targets, context, logWarning))
        {
            return false;
        }

        return TryGetTeamCenter(targets, out center);
    }

    /// <summary>
    /// 选项依赖目标，且时间轴上还没有「设置目标」Clip 时画 HelpBox。
    /// 不看运行时 FindTargets：保存 / 停预览会 Clear，容易误报。
    /// </summary>
    public static void DrawTargetDependencyHelp(TimelineControl control, bool optionNeedsTarget)
    {
        if (!optionNeedsTarget)
        {
            return;
        }

        if (HasSetTargetsClip(control))
        {
            return;
        }

        EditorGUILayout.HelpBox(MissingTargetClipHint, MessageType.Warning);
    }

    static void WarnMissingTargets(string context)
    {
        if (string.IsNullOrEmpty(context))
        {
            Debug.LogWarning($"[Timeline] {MissingTargetClipHint}");
            return;
        }

        Debug.LogWarning($"[Timeline][{context}] {MissingTargetClipHint}");
    }
}
