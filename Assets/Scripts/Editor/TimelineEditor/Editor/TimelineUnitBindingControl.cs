using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>编辑器单位绑定与表现适配。UI、角色查找和缓存不进入共用采样器。</summary>
public sealed class TimelineUnitBindingControl
{
    private TimelineControl control;
    private Dictionary<UnitMono, BattleUnitTimelineActor> actors = new Dictionary<UnitMono, BattleUnitTimelineActor>();

    private static string[] simpleSubjects = new[]
    {
        TimelineBindingEditorText.Caster, TimelineBindingEditorText.EffectSource, TimelineBindingEditorText.EffectTargets
    };
    private static string[] referenceSelections = new[] { "最近", "最远", "全体（中心）" };
    private static int[] referenceSelectionValues = new[]
    {
        (int)TimelineMoveReferenceType.Nearest,
        (int)TimelineMoveReferenceType.Farthest,
        (int)TimelineMoveReferenceType.Center,
    };

    internal static TimelineUnitSubjectType DrawSimpleSubject(TimelineUnitSubjectType subject)
    {
        return (TimelineUnitSubjectType)EditorGUILayout.Popup(TimelineBindingEditorText.Subject, (int)subject, simpleSubjects);
    }

    /// <summary>绑定所属时间轴。</summary>
    public TimelineUnitBindingControl(TimelineControl control)
    {
        this.control = control;
    }

    internal static TimelineUnitSubjectType DrawSubjectSelector(TimelineUnitSubjectType subject)
    {
        return (TimelineUnitSubjectType)EditorGUILayout.Popup(TimelineBindingEditorText.Subject,
            (int)subject, new[] { TimelineBindingEditorText.Caster, TimelineBindingEditorText.EffectSource,
                TimelineBindingEditorText.EffectTargets, TimelineBindingEditorText.CastTargets });
    }

    /// <summary>统一绘制作用单位及其独立对象绑定。</summary>
    public void DrawSubject(TimelineClipUnitConfigBase config)
    {
        config.Subject = DrawSubjectSelector(config.Subject);
        if (config.Subject != TimelineUnitSubjectType.Caster)
        {
            config.SubjectBindingKey = control.PreviewBindings.DrawBindingSelector(TimelineBindingEditorText.SubjectBinding,
                config.SubjectBindingKey, config.Subject != TimelineUnitSubjectType.CastTargets, config);
        }
    }

    /// <summary>统一绘制面向参考对象的绑定和多对象取点规则。</summary>
    public void DrawReference(TimelineClipUnitConfigBase config, ref TimelineMoveDestinationType destination,
        ref string key, ref TimelineMoveReferenceType selection, bool positions)
    {
        var labels = positions
            ? new[] { TimelineBindingEditorText.CastTargets, TimelineBindingEditorText.EffectSource, TimelineBindingEditorText.EffectTargets,
                TimelineBindingEditorText.InitialPosition, TimelineBindingEditorText.WorldPosition }
            : new[] { TimelineBindingEditorText.CastTargets, TimelineBindingEditorText.EffectSource, TimelineBindingEditorText.EffectTargets };
        destination = (TimelineMoveDestinationType)EditorGUILayout.Popup(TimelineBindingEditorText.Reference, (int)destination, labels);
        if (destination <= TimelineMoveDestinationType.EffectTargets)
        {
            key = control.PreviewBindings.DrawBindingSelector(TimelineBindingEditorText.EndpointBinding, key,
                destination != TimelineMoveDestinationType.CastTargets, config);
        }
        selection = DrawReferenceSelection(destination, selection);
    }

    internal static TimelineMoveReferenceType DrawReferenceSelection(TimelineMoveDestinationType destination,
        TimelineMoveReferenceType selection)
    {
        if (destination is TimelineMoveDestinationType.CastTargets or TimelineMoveDestinationType.EffectTargets)
        {
            return (TimelineMoveReferenceType)EditorGUILayout.IntPopup(TimelineBindingEditorText.GroupSelection,
                (int)selection, referenceSelections, referenceSelectionValues);
        }
        return selection;
    }

    /// <summary>按消费片段开始时间解析一组单位，不读取全局当前播放时刻的旧目标。</summary>
    public List<UnitMono> ResolveUnits(TimelineClipConfigBase consumer, TimelineUnitSubjectType subject, string key)
    {
        if (subject == TimelineUnitSubjectType.CastTargets || subject == TimelineUnitSubjectType.EffectTargets)
        {
            return control.PreviewBindings.GetTargets(consumer, subject == TimelineUnitSubjectType.EffectTargets, key ?? string.Empty);
        }
        var unit = subject == TimelineUnitSubjectType.Caster
            ? (control.MyTransform ? control.MyTransform.GetComponent<UnitMono>() : null)
            : control.PreviewBindings.GetSource(consumer, key ?? string.Empty);
        var result = new List<UnitMono>();
        if (unit)
        {
            result.Add(unit);
        }
        return result;
    }

    /// <summary>将预览单位适配为共用播放器的角色，缓存其演出前状态。</summary>
    public List<BattleUnitTimelineActor> Resolve(TimelineClipConfigBase consumer, TimelineUnitSubjectType subject, string key)
    {
        var units = ResolveUnits(consumer, subject, key);
        var result = new List<BattleUnitTimelineActor>(units.Count);
        for (var i = 0; i < units.Count; i++)
        {
            result.Add(Adapt(units[i]));
        }
        return result;
    }

    /// <summary>将预览单位适配为共享角色；查询、挂点和动画使用同一实例。</summary>
    public BattleUnitTimelineActor Adapt(UnitMono unit)
    {
        if (!actors.TryGetValue(unit, out var actor))
        {
            var animator = unit.Animator ? unit.Animator : unit.GetComponentInChildren<Animator>(true);
            actor = new BattleUnitTimelineActor(unit.transform, unit.transform,
                animator, CameraRight, unit.FindNodeTransform, unit.BattleOriginPos);
            actor.Team = unit.TeamType;
            actors.Add(unit, actor);
        }
        return actor;
    }

    /// <summary>停止或重建预览时统一恢复，避免多个片段按各自进入快照互相覆盖。</summary>
    public void Restore()
    {
        foreach (var pair in actors)
        {
            if (pair.Key && pair.Value.Visual)
            {
                pair.Value.Restore();
            }
        }
        actors.Clear();
    }

    /// <summary>取得适配角色对应的编辑器单位，用于编辑器定帧映射。</summary>
    public UnitMono FindUnit(BattleUnitTimelineActor actor)
    {
        foreach (var pair in actors)
        {
            if (pair.Value == actor)
            {
                return pair.Key;
            }
        }
        throw new System.ArgumentException("Timeline actor is not registered.");
    }

    private Vector3 CameraRight()
    {
        var root = EditorTimelineUtil.FindBattleCameraGameObject();
        return root && root.GetComponentInChildren<Camera>(true) is Camera camera
            ? camera.transform.right : Quaternion.Euler(BattleSharedConfig.Current.CameraEuler) * Vector3.right;
    }
}
