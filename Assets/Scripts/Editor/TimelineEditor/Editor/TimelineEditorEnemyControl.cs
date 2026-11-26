using UnityEditor;
using UnityEngine;

/// <summary>
/// 编辑器对面单位：站在施法者的对面阵营，Idle 采样给非施法者单位。
/// </summary>
public class TimelineEditorEnemyControl
{
    private const int OpponentCount = 3;

    private TimelineControl control;
    private int stateHash;
    private UnitTeamType opponentTeam = UnitTeamType.TeamB;

    public TimelineEditorEnemyControl(TimelineControl control)
    {
        this.control = control;
        opponentTeam = control.PreviewOpponentTeam;
        CreateUnitMonos(opponentTeam);
        stateHash = Animator.StringToHash("Idle");
    }

    public void Draw()
    {
        if (control?.Timeline == null)
        {
            return;
        }

        var runningTime = (float)control.Timeline.RunningTime;
        var caster = control.MyTransform;
        var units = this.control.UnitsControl.Units;
        for (var i = 0; i < units.Count; i++)
        {
            // 施法者由 PlayAnimator 等驱动，这里只给对面 / 其它单位铺 Idle 底稿
            if (caster != null && units[i].Transform == caster)
            {
                continue;
            }

            var animator = units[i].Animator;
            if (animator == null || !animator.isActiveAndEnabled)
            {
                continue;
            }

            var clipLength = GetStateClipLength(animator, "Idle");
            if (clipLength <= 0f)
            {
                continue;
            }

            var normalizedTime = runningTime % clipLength / clipLength;
            animator.speed = 0f;
            animator.Play(stateHash, 0, normalizedTime);
            animator.Update(0f);
        }
    }

    private static float GetStateClipLength(Animator animator, string stateName)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            return 0f;
        }

        return EditorAnimatorStateUtil.GetStateLength(animator.runtimeAnimatorController, stateName);
    }

    /// <summary>按对面阵营重建预览单位（施法阵营切换时调用）。</summary>
    public void RebuildOpponents(UnitTeamType nextOpponentTeam)
    {
        if (nextOpponentTeam != UnitTeamType.TeamA && nextOpponentTeam != UnitTeamType.TeamB)
        {
            nextOpponentTeam = UnitTeamType.TeamB;
        }

        ClearUnits(opponentTeam);
        opponentTeam = nextOpponentTeam;
        CreateUnitMonos(opponentTeam);
    }

    private void CreateUnitMonos(UnitTeamType team)
    {
        if (HasTeamUnits(team))
        {
            return;
        }

        var yaw = team == UnitTeamType.TeamB ? -90f : 90f;
        var rotation = Quaternion.Euler(0f, yaw, 0f);
        for (var i = 0; i < OpponentCount; i++)
        {
            this.control.UnitsControl.CreateUnit(
                team,
                "Grunt/Grunt",
                TimelinePortraitPreview.Position(control, team, i),
                rotation,
                Vector3.one);
        }
    }

    public void OnDestroy()
    {
        ClearUnits(opponentTeam);
    }

    private void ClearUnits(UnitTeamType team)
    {
        var units = this.control.UnitsControl.Units;
        for (var i = units.Count - 1; i >= 0; i--)
        {
            if (units[i].TeamType != team)
            {
                continue;
            }

            // 勿清掉施法者（换阵营瞬间施法者可能仍挂在旧阵营）
            if (control.MyTransform != null && units[i].Transform == control.MyTransform)
            {
                continue;
            }

            this.control.UnitsControl.RemoveTransform(units[i].transform, true);
        }
    }

    private bool HasTeamUnits(UnitTeamType teamType)
    {
        var units = this.control.UnitsControl.Units;
        var caster = control.MyTransform;
        for (var i = 0; i < units.Count; i++)
        {
            if (units[i].TeamType != teamType)
            {
                continue;
            }

            if (caster != null && units[i].Transform == caster)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    public class TimelineEnemyData
    {
        public string Path;
        public Vector3 Pos;
        public Vector3 Euler;
    }
}
