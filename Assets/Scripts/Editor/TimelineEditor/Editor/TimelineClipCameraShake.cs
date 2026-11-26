using DG.Tweening;
using UnityEditor;
using UnityEngine;

public class TimelineClipCameraShake : TimelineClipBase
{
    private TimelineClipCameraShakeConfig thisConfig;
    private Transform cameraShakeTrans;
    private Vector3 startLocalPos;
    private Quaternion startLocalRot;
    private Tweener posShakeTween;
    private Tweener rotShakeTween;

    public override void Init(TimelineControl control, TimelineClipConfigBase config)
    {
        thisConfig = config as TimelineClipCameraShakeConfig;
        base.Init(control, config);
        EnsureCamera();
    }

    public override void DrawInspector()
    {
        base.DrawInspector();

        EditorGUILayout.LabelField("相机震动", EditorStyles.boldLabel);
        thisConfig.PositionStrength = EditorGUILayout.Vector3Field("位置强度", thisConfig.PositionStrength);
        thisConfig.RotationStrength = EditorGUILayout.Vector3Field("旋转强度", thisConfig.RotationStrength);
        // Vibrato：持续时间内来回抖几次；Randomness：方向随机程度（0~180，越大越乱）
        thisConfig.Vibrato = EditorGUILayout.IntField("震动次数(Vibrato)", thisConfig.Vibrato);
        thisConfig.Randomness = EditorGUILayout.FloatField("随机度(Randomness)", thisConfig.Randomness);
        thisConfig.EnvelopeType = TimelineEnumDisplay.DrawCameraShakeEnvelopePopup(
            "幅度包络(Envelope)",
            thisConfig.EnvelopeType);
        TimelineEditorUtil.DrawStableHelp(GetEnvelopeHelp(thisConfig.EnvelopeType));
    }

    private static string GetEnvelopeHelp(TimelineCameraShakeEnvelopeType type)
    {
        switch (type)
        {
            case TimelineCameraShakeEnvelopeType.Constant:
                return "恒定：全程满幅度，不衰减。";
            case TimelineCameraShakeEnvelopeType.FadeIn:
                return "增强：从 0 线性拉到满幅度。";
            case TimelineCameraShakeEnvelopeType.FadeInOut:
                return "先强后弱：中间满幅度，两头为 0。";
            case TimelineCameraShakeEnvelopeType.FadeOut:
            default:
                return "衰减：满幅度线性收到 0（旧技能默认）。";
        }
    }

    public override void OnDestroy()
    {
        KillShake();
        base.OnDestroy();
    }

    protected override void OnOutTime()
    {
        base.OnOutTime();
        KillShake();
        if (cameraShakeTrans == null)
        {
            return;
        }

        cameraShakeTrans.localPosition = startLocalPos;
        cameraShakeTrans.localRotation = startLocalRot;
    }

    protected override void OnInTime()
    {
        base.OnInTime();
    }

    protected override void OnLeftIn()
    {
        base.OnLeftIn();

        if (!EnsureCamera())
        {
            return;
        }

        KillShake();
        cameraShakeTrans.localPosition = startLocalPos;
        cameraShakeTrans.localRotation = startLocalRot;

        var duration = Mathf.Max(config.Duration, 0.01f);
        // fadeOut 关掉，幅度包络由 EnvelopeType 在 Goto 后乘到偏移上
        if (thisConfig.PositionStrength.sqrMagnitude > 0f)
        {
            posShakeTween = cameraShakeTrans
                .DOShakePosition(
                    duration,
                    thisConfig.PositionStrength,
                    thisConfig.Vibrato,
                    thisConfig.Randomness,
                    false,
                    false)
                .Pause();
        }

        if (thisConfig.RotationStrength.sqrMagnitude > 0f)
        {
            rotShakeTween = cameraShakeTrans
                .DOShakeRotation(
                    duration,
                    thisConfig.RotationStrength,
                    thisConfig.Vibrato,
                    thisConfig.Randomness,
                    false)
                .Pause();
        }
    }

    protected override void OnInTimeLogic()
    {
        base.OnInTimeLogic();
        var duration = Mathf.Max(config.Duration, 0.01f);
        var sampleTime = Mathf.Clamp(playTiming, 0f, duration);
        if (posShakeTween != null)
        {
            posShakeTween.Goto(sampleTime, true);
        }

        if (rotShakeTween != null)
        {
            rotShakeTween.Goto(sampleTime, true);
        }

        if (cameraShakeTrans == null)
        {
            return;
        }

        var envelope = TimelineClipCameraShakeUtil.EvaluateEnvelope(
            thisConfig.EnvelopeType,
            sampleTime / duration);
        TimelineClipCameraShakeUtil.ApplyEnvelope(
            cameraShakeTrans,
            startLocalPos,
            startLocalRot,
            envelope);
    }

    private void KillShake()
    {
        if (posShakeTween != null)
        {
            posShakeTween.Kill(true);
            posShakeTween = null;
        }

        if (rotShakeTween != null)
        {
            rotShakeTween.Kill(true);
            rotShakeTween = null;
        }

        if (cameraShakeTrans != null)
        {
            cameraShakeTrans.DOKill();
        }
    }

    private bool EnsureCamera()
    {
        if (cameraShakeTrans != null)
        {
            return true;
        }

        // 震动作用在 BattleCamera/ShakeTrans 节点上，不直接抖根节点
        cameraShakeTrans = EditorTimelineUtil.FindBattleCameraShakeTrans();
        if (cameraShakeTrans == null)
        {
            Debug.LogWarning("[TimelineClipCameraShake] 未找到 BattleCamera 下的 ShakeTrans 节点。");
            return false;
        }

        startLocalPos = cameraShakeTrans.localPosition;
        startLocalRot = cameraShakeTrans.localRotation;
        return true;
    }
}
