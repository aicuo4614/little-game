using UnityEditor;
using UnityEngine;

/// <summary>角色定帧 Clip：全局红光标继续，绑定的单位动画/位移逻辑时间暂停。</summary>
public class TimelineClipHitStop : TimelineClipBase
{
    public override void DrawInspector()
    {
        base.DrawInspector();

        EditorGUILayout.LabelField("Hit Stop（角色定帧）", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            TimelineBindingEditorText.HitStopOverlapHelp,
            MessageType.Info);

        EditorGUI.BeginChangeCheck();
        var stop = (TimelineClipHitStopConfig)config;
        var previous = stop.Subject;
        stop.Subject = TimelineUnitBindingControl.DrawSubjectSelector(stop.Subject);
        if (previous != stop.Subject)
        {
            stop.TargetBindingKey = string.Empty;
        }
        if (stop.Subject != TimelineUnitSubjectType.Caster)
        {
            control.PreviewBindings.DrawBinding(stop, stop.Subject != TimelineUnitSubjectType.CastTargets);
        }
        if (EditorGUI.EndChangeCheck())
        {
            control.Timeline.StopPreView();
            control.Timeline.Repaint();
        }

        var previewUnits = control.UnitBindings.ResolveUnits(stop, stop.Subject, stop.TargetBindingKey);
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.IntField("将定帧单位数", previewUnits.Count);
        EditorGUI.EndDisabledGroup();
    }
}
