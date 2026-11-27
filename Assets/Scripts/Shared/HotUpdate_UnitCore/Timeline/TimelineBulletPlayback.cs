using System.Collections.Generic;
using UnityEngine;

/// <summary>绝对时间弹道采样（对齐 Demo TimelineBulletPlayback，适配 3D 子弹与特效）。</summary>
public sealed class TimelineBulletPlayback
{
    private readonly TimelineClipPlayBulletConfig config;
    private readonly BattleUnitTimelineActor sourceActor;
    private readonly List<BattleUnitTimelineActor> source;
    private readonly List<BattleUnitTimelineActor> target;
    private readonly TimelineEffectPlayback effect;
    private Vector3 start;
    private Vector3 end;
    private bool flightStarted;
    private readonly float flyDuration;
    private readonly Transform billboardVisual;
    private readonly Quaternion initialVisualRotation;
    private Vector3 visualDirection;

    public TimelineBulletPlayback(
        TimelineClipPlayBulletConfig config,
        BattleUnitTimelineActor sourceActor,
        BattleUnitTimelineActor target,
        GameObject instance,
        EffectPointLightData lightData = null,
        bool incrementalEffects = false)
    {
        this.config = config;
        this.sourceActor = sourceActor;
        source = new List<BattleUnitTimelineActor> { sourceActor };
        this.target = new List<BattleUnitTimelineActor> { target };
        effect = new TimelineEffectPlayback(instance, lightData, incrementalEffects);

        if (config.FacingType == TimelineBulletFacingType.CameraBillboard && instance.transform.childCount > 0)
        {
            billboardVisual = instance.transform.GetChild(0);
            initialVisualRotation = billboardVisual.localRotation;
        }

        TimelineActorTransform.Apply(instance.transform, source, config.StartData, sourceActor.Root.position);
        start = instance.transform.position;
        end = TargetPoint();
        flyDuration = Mathf.Max(
            TimelineClipPlayBulletUtil.MinFlyDuration,
            config.Duration - Mathf.Max(0f, config.MoveDelayTime));
    }

    public GameObject Instance => effect.Instance;

    public void Release()
    {
        if (billboardVisual)
        {
            billboardVisual.localRotation = initialVisualRotation;
        }

        effect.Release();
    }

    public void AlignVisual(Camera camera)
    {
        if (billboardVisual && camera)
        {
            TimelineBulletVisualUtil.Align(
                billboardVisual,
                Instance.transform.position,
                camera,
                visualDirection,
                config.BillboardAngleOffset);
        }
    }

    public void Sample(float time)
    {
        var transform = Instance.transform;
        visualDirection = end - start;
        if (!flightStarted && (time < config.MoveDelayTime || TimelineClipPlayBulletUtil.NeedPerFrameStartApply(config.StartData)))
        {
            TimelineActorTransform.Apply(transform, source, config.StartData, sourceActor.Root.position);
            start = transform.position;
        }

        if (time < config.MoveDelayTime)
        {
            if (TimelineClipPlayBulletUtil.NeedsDelayScaleLerp(config))
            {
                transform.localScale = TimelineClipPlayBulletUtil.EvaluateDelayScale(config, time);
            }
        }
        else
        {
            if (!flightStarted)
            {
                flightStarted = true;
                end = TargetPoint();
                transform.SetParent(null, true);
                if (TimelineClipPlayBulletUtil.NeedsDelayScaleLerp(config))
                {
                    transform.localScale = config.StartData.OffsetScale;
                }
            }

            // 规则的 TrackTarget 语义：保持已排程的到达时刻，终点跟随仍存活的目标挂点。
            var destination = TargetPoint();
            var flightTime = Mathf.Max(0f, time - config.MoveDelayTime);
            transform.position = TimelineClipPlayBulletUtil.EvaluatePosition(config, start, destination, flightTime, flyDuration);
            var previous = TimelineClipPlayBulletUtil.EvaluatePosition(
                config, start, destination, Mathf.Max(0f, flightTime - 0.001f), flyDuration);
            var direction = transform.position - previous;
            visualDirection = direction.sqrMagnitude > 0.00000001f ? direction : destination - start;
            var signs = transform.localScale;
            direction = Vector3.Scale(direction, new Vector3(Mathf.Sign(signs.x), Mathf.Sign(signs.y), Mathf.Sign(signs.z)));
            if (direction.sqrMagnitude > 0.00000001f)
            {
                transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }

            Instance.SetActive(flightTime < flyDuration);
        }

        effect.Sample(time, config.Duration);
    }

    private Vector3 TargetPoint()
    {
        TimelineActorTransform.Evaluate(target, config.EndData, sourceActor.Root.position, out var point, out _, out _);
        return point;
    }
}
