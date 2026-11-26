using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>攻击移动使用施法绑定，击退击飞使用效果来源和目标绑定。</summary>
public sealed class TimelineClipForce : TimelineClipBase
{
    private TimelineClipForceConfig forceConfig;
    private Dictionary<UnitMono, Vector3> starts = new Dictionary<UnitMono, Vector3>();

    public override void Init(TimelineControl control, TimelineClipConfigBase config)
    {
        forceConfig = (TimelineClipForceConfig)config;
        config.Duration = 0f;
        base.Init(control, config);
    }

    public override void Draw()
    {
        config.Duration = 0f;
        base.Draw();
    }

    public override void DrawInspector()
    {
        base.DrawInspector();
        forceConfig.HitTranslateType = TimelineEnumDisplay.DrawHitTranslatePopup("力类型", forceConfig.HitTranslateType);
        control.PreviewBindings.DrawBinding(config, forceConfig.HitTranslateType != HitTranslateType.Attack);
        forceConfig.AffectedByHitStop = DrawAffectedByHitStopToggle(forceConfig.AffectedByHitStop);
        if (forceConfig.HitTranslateType == HitTranslateType.Attack)
        {
            forceConfig.Force.x = EditorGUILayout.FloatField(TimelineBindingEditorText.SidewaysForce, forceConfig.Force.x);
            forceConfig.Force.z = EditorGUILayout.FloatField(TimelineBindingEditorText.ForwardForce, forceConfig.Force.z);
        }
        else
        {
            forceConfig.Force.x = EditorGUILayout.FloatField(TimelineBindingEditorText.HorizontalForce, forceConfig.Force.x);
            if (forceConfig.HitTranslateType == HitTranslateType.HitFly)
            {
                forceConfig.Force.y = EditorGUILayout.FloatField(TimelineBindingEditorText.VerticalForce, forceConfig.Force.y);
            }
        }
    }

    public override bool TryNudgeEditableDuration(float frameStep, int frameDelta)
    {
        return false;
    }

    protected override void SetRect()
    {
        base.SetRect();
        rect.width = 60f;
    }

    protected override void OnPass(bool leftToRight)
    {
        if (forceConfig.HitTranslateType == HitTranslateType.Attack)
        {
            var targets = control.PreviewBindings.GetTargets(config, false);
            if (control.MyTransform && targets.Count > 0)
            {
                var caster = control.MyTransform.GetComponent<UnitMono>();
                ApplyForce(caster, targets[0], true, leftToRight);
            }
        }
        else if (forceConfig.HitTranslateType == HitTranslateType.HitBack
            || forceConfig.HitTranslateType == HitTranslateType.HitFly)
        {
            var source = control.PreviewBindings.GetSource(config);
            var targets = control.PreviewBindings.GetTargets(config, true);
            if (source)
            {
                for (var i = 0; i < targets.Count; i++)
                {
                    ApplyForce(targets[i], source, false, leftToRight);
                }
            }
        }
        base.OnPass(leftToRight);
    }

    private void ApplyForce(UnitMono subject, UnitMono reference, bool attack, bool leftToRight)
    {
        var gravity = control.UnitsControl.GetUnitGravity(subject);
        if (gravity is not EditorUnitGravity)
        {
            return;
        }
        if (!starts.TryGetValue(subject, out var start) || isFirstLeftIn)
        {
            start = subject.GetTransformPosition();
            starts[subject] = start;
        }
        var direction = attack
            ? reference.GetTransformPosition() - subject.GetTransformPosition()
            : subject.GetTransformPosition() - reference.GetTransformPosition();
        direction.y = 0f;
        direction = direction.normalized;
        Vector3 force;
        if (attack)
        {
            force = direction * forceConfig.Force.z
                + Vector3.Cross(Vector3.up, direction) * forceConfig.Force.x;
            subject.SetAttackForce(force);
        }
        else
        {
            force = direction * (reference.CurAttackForce.magnitude + forceConfig.Force.x);
            if (forceConfig.HitTranslateType == HitTranslateType.HitFly)
            {
                force.y = forceConfig.Force.y;
            }
        }
        gravity.SetStartPos(start, playTiming);
        gravity.SetForce(force, forceConfig.HitTranslateType, forceConfig.AffectedByHitStop);
        if (!leftToRight)
        {
            gravity.DisableForceXZ();
        }
    }

    public override void OnStopPreview()
    {
        starts.Clear();
        base.OnStopPreview();
    }
}
