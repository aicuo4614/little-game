using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>解析配置 Clip 的模拟对象，为表现 Clip 提供独立来源和目标。</summary>
public sealed class TimelinePreviewBindingControl
{
    private TimelineControl control;

    /// <summary>当前播放时刻的施法对象，供尚未指定绑定的旧表现使用。</summary>
    public List<UnitMono> Targets => GetTargets(null, false);
    /// <summary>当前播放时刻的效果对象。</summary>
    public List<UnitMono> HitTargets => GetTargets(null, true);

    /// <summary>绑定所属时间轴。</summary>
    public TimelinePreviewBindingControl(TimelineControl control)
    {
        this.control = control;
    }

    /// <summary>取得表现片段引用的模拟对象。</summary>
    public List<UnitMono> GetTargets(TimelineClipConfigBase consumer, bool result, string bindingKey = null)
    {
        var binding = FindBinding(consumer, result, bindingKey);
        var keys = binding is TimelineClipSetCastTargetsConfig cast ? cast.PreviewTargets
            : (binding as TimelineClipSetHitTargetsConfig)?.PreviewTargets;
        var resolved = new List<UnitMono>();
        if (keys is List<string>)
        {
            foreach (var key in keys)
            {
                TimelinePreviewActorKey.Validate(key);
            }
            foreach (var unit in control.UnitsControl.Units)
            {
                if (keys.Contains(GetActorKey(unit)))
                {
                    resolved.Add(unit);
                }
            }
        }
        return resolved;
    }

    /// <summary>效果来源从对应配置读取，不推断 Action 类型。</summary>
    public UnitMono GetSource(TimelineClipConfigBase consumer, string bindingKey = null)
    {
        if (FindBinding(consumer, true, bindingKey) is TimelineClipSetHitTargetsConfig binding)
        {
            TimelinePreviewActorKey.Validate(binding.PreviewSource);
            foreach (var unit in control.UnitsControl.Units)
            {
                if (GetActorKey(unit) == binding.PreviewSource)
                {
                    return unit;
                }
            }
        }
        return null;
    }

    /// <summary>在表现 Inspector 中明确选择一条对象绑定，默认按时间取最近绑定。</summary>
    public void DrawBinding(TimelineClipConfigBase consumer, bool result)
    {
        EditorGUI.BeginChangeCheck();
        var next = DrawBindingSelector(TimelineBindingEditorText.Binding, consumer.TargetBindingKey, result, consumer);
        if (EditorGUI.EndChangeCheck())
        {
            control.Timeline.StopPreView();
            consumer.TargetBindingKey = next;
            if (control.Clips.Find(item => item.Config == consumer) is TimelineClipPlayHit hit)
            {
                hit.RefreshPreviewTargets();
            }
        }
    }

    /// <summary>绘制一个独立的对象绑定选择字段。</summary>
    public string DrawBindingSelector(string label, string key, bool result, TimelineClipConfigBase consumer,
        bool availableAtStart = false)
    {
        var keys = new List<string> { string.Empty };
        var labels = new List<string> { result ? TimelineBindingEditorText.LatestEffectBinding : TimelineBindingEditorText.LatestBinding };
        foreach (var clip in control.Clips)
        {
            if ((result ? clip.Config is TimelineClipSetHitTargetsConfig : clip.Config is TimelineClipSetCastTargetsConfig)
                && (!availableAtStart || clip.GetContentTimelineStartTime() <= GetStart(consumer) + 0.0001f))
            {
                keys.Add(clip.Config.ClipKey);
                labels.Add(string.Format(TimelineBindingEditorText.BindingClip,
                    UnitFeatureTypeDisplay.GetDisplayName(clip.Config.Type), control.Clips.IndexOf(clip) + 1));
            }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            var next = EditorGUILayout.Popup(label, keys.IndexOf(key ?? string.Empty), labels.ToArray());
            var selectedKey = next >= 0 ? keys[next] : key;
            var binding = FindBinding(consumer, result, selectedKey);
            using (new EditorGUI.DisabledScope(binding is null))
            {
                if (GUILayout.Button(TimelineBindingEditorText.Locate, GUILayout.Width(44f)))
                {
                    var clip = control.Clips.Find(item => item.Config == binding);
                    control.Timeline.LocateClip(clip);
                }
            }
            return selectedKey;
        }
    }

    /// <summary>配置 Clip 的多选模拟目标 UI。</summary>
    public void DrawTargets(List<string> selection)
    {
        foreach (var unit in control.UnitsControl.Units)
        {
            var key = GetActorKey(unit);
            var selected = selection.Contains(key);
            var next = EditorGUILayout.ToggleLeft(GetActorLabel(unit), selected);
            if (next != selected)
            {
                control.Timeline.StopPreView();
                if (next)
                {
                    selection.Add(key);
                }
                else
                {
                    selection.Remove(key);
                }
                NotifyChanged();
            }
        }
    }

    /// <summary>配置模拟效果来源。</summary>
    public void DrawSource(TimelineClipSetHitTargetsConfig config)
    {
        var keys = new List<string>();
        var labels = new List<string>();
        foreach (var unit in control.UnitsControl.Units)
        {
            keys.Add(GetActorKey(unit));
            labels.Add(GetActorLabel(unit));
        }
        EditorGUI.BeginChangeCheck();
        var next = EditorGUILayout.Popup(TimelineBindingEditorText.Source, keys.IndexOf(config.PreviewSource), labels.ToArray());
        if (EditorGUI.EndChangeCheck() && next >= 0)
        {
            control.Timeline.StopPreView();
            config.PreviewSource = keys[next];
            NotifyChanged();
        }
    }

    internal string GetActorLabel(UnitMono unit)
    {
        if (unit.transform == control.MyTransform)
        {
            return TimelineBindingEditorText.Caster;
        }
        var index = 0;
        foreach (var candidate in control.UnitsControl.Units)
        {
            if (candidate.TeamType == unit.TeamType && candidate.transform != control.MyTransform)
            {
                index++;
                if (candidate == unit)
                {
                    break;
                }
            }
        }
        var caster = control.MyTransform ? control.MyTransform.GetComponent<UnitMono>() : null;
        var allyTeam = caster ? caster.TeamType : control.PreviewCasterTeam;
        return string.Format(unit.TeamType == allyTeam
            ? TimelineBindingEditorText.AllyActor : TimelineBindingEditorText.EnemyActor, index);
    }

    internal string GetActorKey(UnitMono unit)
    {
        if (unit.transform == control.MyTransform)
        {
            return "$caster";
        }
        var index = 0;
        foreach (var candidate in control.UnitsControl.Units)
        {
            if (candidate.TeamType == unit.TeamType && candidate.transform != control.MyTransform)
            {
                if (candidate == unit)
                {
                    return (unit.TeamType == control.PreviewCasterTeam ? "$ally:" : "$enemy:") + index;
                }
                index++;
            }
        }
        return string.Empty;
    }

    private TimelineClipConfigBase FindBinding(TimelineClipConfigBase consumer, bool result, string bindingKey = null)
    {
        var explicitKey = bindingKey ?? consumer?.TargetBindingKey;
        TimelineClipConfigBase best = null;
        var latest = float.NegativeInfinity;
        var time = consumer is TimelineClipConfigBase
            ? GetStart(consumer) : (float)control.Timeline.RunningTime;
        foreach (var clip in control.Clips)
        {
            var config = clip.Config;
            if (!(result ? config is TimelineClipSetHitTargetsConfig : config is TimelineClipSetCastTargetsConfig))
            {
                continue;
            }
            if (!string.IsNullOrEmpty(explicitKey))
            {
                if (config.ClipKey == explicitKey && GetStart(config) <= time + 0.0001f)
                {
                    return config;
                }
                continue;
            }
            var start = GetStart(config);
            if (start <= time + 0.0001f && start >= latest)
            {
                best = config;
                latest = start;
            }
        }
        return best;
    }

    private float GetStart(TimelineClipConfigBase config)
    {
        var clip = control.Clips.Find(item => item.Config.ClipKey == config.ClipKey);
        return clip is TimelineClipBase ? clip.GetContentTimelineStartTime() : config.StartTime + config.DelayTime;
    }

    private void NotifyChanged()
    {
        foreach (var clip in control.Clips)
        {
            if (clip is TimelineClipPlayHit hit)
            {
                hit.RefreshPreviewTargets();
            }
        }
        control.Timeline.Repaint();
    }
}
