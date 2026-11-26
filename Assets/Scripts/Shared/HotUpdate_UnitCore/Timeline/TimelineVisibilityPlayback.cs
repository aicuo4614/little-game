using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>按片段进入时的绑定切换单位根节点和战场建筑的显隐。</summary>
public sealed class TimelineVisibilityPlayback
{
    private TimelinePlaybackContext context;
    private Dictionary<TimelineClipVisibilityConfig, HashSet<Object>> targets =
        new Dictionary<TimelineClipVisibilityConfig, HashSet<Object>>();
    private Dictionary<Object, (bool Original, List<TimelineClipVisibilityConfig> Layers)> visibilityStates =
        new Dictionary<Object, (bool, List<TimelineClipVisibilityConfig>)>();

    /// <summary>绑定本次演出的角色和场景。</summary>
    public TimelineVisibilityPlayback(TimelinePlaybackContext context)
    {
        this.context = context;
    }

    /// <summary>片段进入时捕获单位集合。后进入的显隐覆盖先前的设置。</summary>
    public void Enter(TimelineClipVisibilityConfig clip)
    {
        if (targets.ContainsKey(clip))
        {
            return;
        }

        var selected = CollectTargets(clip);
        targets.Add(clip, selected);
        foreach (var target in selected)
        {
            if (!visibilityStates.TryGetValue(target, out var state))
            {
                var original = target is GameObject root ? root.activeSelf : !((Renderer)target).forceRenderingOff;
                state = (original, new List<TimelineClipVisibilityConfig>());
                visibilityStates.Add(target, state);
            }
            state.Layers.Add(clip);
            Apply(target, clip.Show);
        }
    }

    /// <summary>恢复需要在退出时重置的片段，并保留其余仍生效的显隐设置。</summary>
    public void Exit(TimelineClipVisibilityConfig clip)
    {
        if (!clip.ResetOnEnd || !targets.Remove(clip, out var selected))
        {
            return;
        }
        foreach (var target in selected)
        {
            var state = visibilityStates[target];
            state.Layers.Remove(clip);
            Apply(target, state.Layers.Count == 0 ? state.Original : state.Layers[state.Layers.Count - 1].Show);
        }
    }

    /// <summary>演出停止或重建时恢复进入演出前的显隐状态。</summary>
    public void Reset()
    {
        foreach (var pair in visibilityStates)
        {
            Apply(pair.Key, pair.Value.Original);
        }
        visibilityStates.Clear();
        targets.Clear();
    }

    private static void Apply(Object target, bool show)
    {
        if (!target)
        {
            return;
        }
        if (target is GameObject root)
        {
            root.SetActive(show);
        }
        else
        {
            ((Renderer)target).forceRenderingOff = !show;
        }
    }

    /// <summary>约束显隐的对象类型与战场排除位。</summary>
    public static void Validate(TimelineClipVisibilityConfig clip)
    {
        const TimelineVisibilityBattleExcludeType flags = TimelineVisibilityBattleExcludeType.Caster
            | TimelineVisibilityBattleExcludeType.CasterTeammates | TimelineVisibilityBattleExcludeType.AllEnemies
            | TimelineVisibilityBattleExcludeType.CastTargets | TimelineVisibilityBattleExcludeType.EffectSource
            | TimelineVisibilityBattleExcludeType.EffectTargets;
        if (!Enum.IsDefined(typeof(TimelineVisibilityTargetType), clip.TargetType)
            || (clip.BattleExcludeTypes & ~flags) != 0)
        {
            throw new ArgumentException("Invalid visibility target or exclusions: " + clip.ClipKey);
        }
    }

    /// <summary>从指定场景查找战场建筑，兼容旧编辑器场景的 BattleEnv。</summary>
    public static Transform FindEnvironment(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return null;
        }
        Transform fallback = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "AllBuildings")
                {
                    return child;
                }
                if (child.name == "BattleEnv")
                {
                    fallback = child;
                }
            }
        }
        return fallback;
    }

    private HashSet<Object> CollectTargets(TimelineClipVisibilityConfig clip)
    {
        var result = new HashSet<Object>();
        if (clip.TargetType == TimelineVisibilityTargetType.OtherUnits)
        {
            var casters = Resolve(clip, TimelineUnitSubjectType.Caster);
            foreach (var actor in context.AllActors())
            {
                if (!casters.Contains(actor))
                {
                    result.Add(actor.Root.gameObject);
                }
            }
            return result;
        }
        if (clip.TargetType == TimelineVisibilityTargetType.Battle)
        {
            var environment = context.VisibilityEnvironment?.Invoke();
            if (environment)
            {
                result.UnionWith(environment.GetComponentsInChildren<Renderer>(true));
            }
            var caster = Resolve(clip, TimelineUnitSubjectType.Caster);
            var excluded = new HashSet<BattleUnitTimelineActor>();
            var flags = clip.BattleExcludeTypes;
            Exclude(TimelineVisibilityBattleExcludeType.CastTargets, TimelineUnitSubjectType.CastTargets);
            Exclude(TimelineVisibilityBattleExcludeType.EffectSource, TimelineUnitSubjectType.EffectSource);
            Exclude(TimelineVisibilityBattleExcludeType.EffectTargets, TimelineUnitSubjectType.EffectTargets);
            foreach (var actor in context.AllActors())
            {
                var category = caster.Contains(actor) ? TimelineVisibilityBattleExcludeType.Caster
                    : caster.Count > 0 && actor.Team == caster[0].Team ? TimelineVisibilityBattleExcludeType.CasterTeammates
                    : TimelineVisibilityBattleExcludeType.AllEnemies;
                if ((flags & category) == 0 && !excluded.Contains(actor))
                {
                    result.Add(actor.Root.gameObject);
                }
            }
            return result;

            void Exclude(TimelineVisibilityBattleExcludeType flag, TimelineUnitSubjectType subject)
            {
                if ((flags & flag) != 0)
                {
                    excluded.UnionWith(Resolve(clip, subject));
                }
            }
        }

        var (subject, teammates) = GetSubject(clip.TargetType);
        var selected = Resolve(clip, subject);
        if (teammates)
        {
            var teams = new HashSet<UnitTeamType>();
            foreach (var actor in selected)
            {
                teams.Add(actor.Team);
            }
            foreach (var actor in context.AllActors())
            {
                if (teams.Contains(actor.Team) && !selected.Contains(actor))
                {
                    result.Add(actor.Root.gameObject);
                }
            }
        }
        else
        {
            foreach (var actor in selected)
            {
                result.Add(actor.Root.gameObject);
            }
        }
        return result;
    }

    private List<BattleUnitTimelineActor> Resolve(TimelineClipVisibilityConfig clip, TimelineUnitSubjectType subject)
    {
        var key = subject == TimelineUnitSubjectType.CastTargets ? clip.TargetBindingKey : clip.EffectBindingKey;
        return context.Resolve(clip, subject, key);
    }

    /// <summary>把显隐对象映射为通用绑定和同队筛选，供播放和编排界面使用。</summary>
    public static (TimelineUnitSubjectType Subject, bool Teammates) GetSubject(TimelineVisibilityTargetType type)
    {
        return type switch
        {
            TimelineVisibilityTargetType.CasterTeammates => (TimelineUnitSubjectType.Caster, true),
            TimelineVisibilityTargetType.CastTargets => (TimelineUnitSubjectType.CastTargets, false),
            TimelineVisibilityTargetType.CastTargetTeammates => (TimelineUnitSubjectType.CastTargets, true),
            TimelineVisibilityTargetType.EffectSource => (TimelineUnitSubjectType.EffectSource, false),
            TimelineVisibilityTargetType.EffectSourceTeammates => (TimelineUnitSubjectType.EffectSource, true),
            TimelineVisibilityTargetType.EffectTargets => (TimelineUnitSubjectType.EffectTargets, false),
            TimelineVisibilityTargetType.EffectTargetTeammates => (TimelineUnitSubjectType.EffectTargets, true),
            _ => (TimelineUnitSubjectType.Caster, false)
        };
    }
}
