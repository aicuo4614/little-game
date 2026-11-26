using UnityEditor;

/// <summary>共享显隐片段的编排界面。</summary>
public class TimelineClipVisibility : TimelineClipBase
{
    private static string[] targetTypeLabels =
    {
        TimelineBindingEditorText.Caster,
        TimelineBindingEditorText.VisibilityCasterTeammates,
        TimelineBindingEditorText.VisibilityOtherUnits,
        TimelineBindingEditorText.CastTargets,
        TimelineBindingEditorText.VisibilityCastTargetTeammates,
        TimelineBindingEditorText.EffectSource,
        TimelineBindingEditorText.VisibilityEffectSourceTeammates,
        TimelineBindingEditorText.EffectTargets,
        TimelineBindingEditorText.EffectTargetTeammates,
        TimelineBindingEditorText.VisibilityBattle,
    };

    private static int[] targetTypeValues =
    {
        (int)TimelineVisibilityTargetType.Caster,
        (int)TimelineVisibilityTargetType.CasterTeammates,
        (int)TimelineVisibilityTargetType.OtherUnits,
        (int)TimelineVisibilityTargetType.CastTargets,
        (int)TimelineVisibilityTargetType.CastTargetTeammates,
        (int)TimelineVisibilityTargetType.EffectSource,
        (int)TimelineVisibilityTargetType.EffectSourceTeammates,
        (int)TimelineVisibilityTargetType.EffectTargets,
        (int)TimelineVisibilityTargetType.EffectTargetTeammates,
        (int)TimelineVisibilityTargetType.Battle,
    };

    private TimelineClipVisibilityConfig thisConfig;
    public override void Init(TimelineControl control, TimelineClipConfigBase config)
    {
        thisConfig = config as TimelineClipVisibilityConfig;
        base.Init(control, config);
    }

    public override void DrawInspector()
    {
        base.DrawInspector();

        EditorGUILayout.LabelField("显隐", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        var nextShow = EditorGUILayout.Toggle("是否显示", thisConfig.Show);
        var nextType = (TimelineVisibilityTargetType)EditorGUILayout.IntPopup(
            "作用对象", (int)thisConfig.TargetType, targetTypeLabels, targetTypeValues);

        var nextExclude = TimelineVisibilityBattleExcludeType.None;
        if (nextType == TimelineVisibilityTargetType.Battle)
        {
            nextExclude = thisConfig.TargetType == TimelineVisibilityTargetType.Battle
                ? thisConfig.BattleExcludeTypes
                : TimelineClipVisibilityConfig.DefaultBattleExcludeTypes;
            EditorGUILayout.LabelField("排除类型（多选）");
            using (new EditorGUI.IndentLevelScope())
            {
                nextExclude = DrawBattleExcludeToggle(nextExclude, TimelineVisibilityBattleExcludeType.Caster,
                    TimelineBindingEditorText.Caster);
                nextExclude = DrawBattleExcludeToggle(nextExclude, TimelineVisibilityBattleExcludeType.CastTargets,
                    TimelineBindingEditorText.CastTargets);
                nextExclude = DrawBattleExcludeToggle(nextExclude, TimelineVisibilityBattleExcludeType.EffectSource,
                    TimelineBindingEditorText.EffectSource);
                nextExclude = DrawBattleExcludeToggle(nextExclude, TimelineVisibilityBattleExcludeType.EffectTargets,
                    TimelineBindingEditorText.EffectTargets);
                nextExclude = DrawBattleExcludeToggle(nextExclude, TimelineVisibilityBattleExcludeType.CasterTeammates,
                    TimelineBindingEditorText.VisibilityCasterTeammates);
                nextExclude = DrawBattleExcludeToggle(nextExclude, TimelineVisibilityBattleExcludeType.AllEnemies,
                    "所有敌方");
            }
        }

        var (subject, _) = TimelineVisibilityPlayback.GetSubject(nextType);
        var castBinding = thisConfig.TargetBindingKey;
        if (subject == TimelineUnitSubjectType.CastTargets
            || (nextExclude & TimelineVisibilityBattleExcludeType.CastTargets) != 0)
        {
            castBinding = control.PreviewBindings.DrawBindingSelector(TimelineBindingEditorText.CastTargetBinding,
                castBinding, false, thisConfig, availableAtStart: true);
        }

        var effectBinding = thisConfig.EffectBindingKey;
        if (subject is TimelineUnitSubjectType.EffectSource or TimelineUnitSubjectType.EffectTargets
            || (nextExclude & (TimelineVisibilityBattleExcludeType.EffectSource | TimelineVisibilityBattleExcludeType.EffectTargets)) != 0)
        {
            effectBinding = control.PreviewBindings.DrawBindingSelector(TimelineBindingEditorText.EffectBinding,
                effectBinding, true, thisConfig, availableAtStart: true);
        }

        thisConfig.Show = nextShow;
        thisConfig.TargetType = nextType;
        thisConfig.BattleExcludeTypes = nextExclude;
        thisConfig.TargetBindingKey = castBinding;
        thisConfig.EffectBindingKey = effectBinding;
        thisConfig.ResetOnEnd = EditorGUILayout.Toggle("结束时重置", thisConfig.ResetOnEnd);

        if (EditorGUI.EndChangeCheck())
        {
            control.SharedPlayback.Invalidate();
        }
    }

    private static TimelineVisibilityBattleExcludeType DrawBattleExcludeToggle(
        TimelineVisibilityBattleExcludeType current,
        TimelineVisibilityBattleExcludeType flag,
        string label)
    {
        var on = (current & flag) != 0;
        var nextOn = EditorGUILayout.ToggleLeft(label, on);
        if (nextOn == on)
        {
            return current;
        }

        if (nextOn)
        {
            return current | flag;
        }

        return current & ~flag;
    }

}
