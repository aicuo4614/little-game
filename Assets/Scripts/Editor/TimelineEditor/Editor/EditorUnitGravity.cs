using UnityEngine;

public class EditorUnitGravity
{
    private UnitMono unitMono;
    private float floorY => UnitMono.BattleOriginPos.y;
    private Vector3 force;
    private HitTranslateType hitType = HitTranslateType.None;
    public HitTranslateType HitType
    {
        get { return hitType; }
        set
        {
            hitType = value;
        }
    }
    private Vector3 startPos;
    private EditorTimeLine timeline;
    private TimelineControl timelineControl;
    private float forceStartRunningTime;
    private bool affectedByHitStop = true;
    public UnitMono UnitMono => unitMono;

    public void Init(EditorTimeLine timeline, UnitMono unitMono)
    {
        this.timeline = timeline;
        this.unitMono = unitMono;
    }

    public void SetTimelineControl(TimelineControl control)
    {
        timelineControl = control;
    }

    public void OnUpdate(float time)
    {
        if (hitType == HitTranslateType.Attack || hitType == HitTranslateType.HitBack)
        {
            if (force != Vector3.zero)
            {
                UnitMono.SetTransformPosition(TimelineForceMotionUtil.ResolvePositionH(startPos, force, time));
            }
        }
        else if (hitType == HitTranslateType.HitFly)
        {
            UnitMono.SetTransformPosition(
                TimelineForceMotionUtil.ResolvePosition(startPos, force, time, floorY));
        }
    }

    public void OnDraw()
    {
        time = ResolveLogicElapsed();
        OnUpdate(time);
    }

    private float time = 0;

    /// <summary>受力后的逻辑已过时间（勾选受定帧影响时，定帧窗口内不推进）。</summary>
    private float ResolveLogicElapsed()
    {
        var running = (float)timeline.RunningTime;
        if (!affectedByHitStop || timelineControl == null || unitMono == null)
        {
            return running - forceStartRunningTime;
        }

        return TimelineEditorHitStopUtil.GetUnitElapsedFromZero(timelineControl, unitMono, running)
            - TimelineEditorHitStopUtil.GetUnitElapsedFromZero(timelineControl, unitMono, forceStartRunningTime);
    }

    public void SetStartPos(Vector3 startPos, float playTimingIgnored)
    {
        this.startPos = startPos;
        forceStartRunningTime = (float)timeline.RunningTime;
        this.time = 0f;
    }

    public void DisableForceXZ()
    {
        if (this.hitType == HitTranslateType.Attack || this.hitType == HitTranslateType.HitBack)
        {
            force = Vector3.zero;
            hitType = HitTranslateType.None;
        }
    }

    public void SetForce(Vector3 force, HitTranslateType hitType, bool affectedByHitStop = true)
    {
        this.force = force;
        this.hitType = hitType;
        this.affectedByHitStop = affectedByHitStop;
    }
}
