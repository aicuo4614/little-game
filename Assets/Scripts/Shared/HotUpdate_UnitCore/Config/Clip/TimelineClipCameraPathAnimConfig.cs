using System;

/// <summary>
/// 相机运动轨迹动画：将 BattleCamera 挂到主角指定节点后，采样指定 AnimationClip。
/// </summary>
[Serializable]
public class TimelineClipCameraPathAnimConfig : TimelineClipConfigBase
{
    /// <summary>
    /// AnimationClip 工程路径（如 Assets/Package_Unit/AnimationClip/xxx.anim）。
    /// </summary>
    public string AnimationClipPath = "Assets/Package_Unit/AnimationClip/CameraMoveTest.anim";

    /// <summary>BattleCamera 挂接到主角的哪个节点；默认单位 Transform。</summary>
    public UnitNodeType AttachNodeType = UnitNodeType.Transform;

    /// <summary>
    /// TeamB 施法时是否对轨迹采样结果做阵营镜像（本地 X 翻转 + yaw 取反、roll 保持）。
    /// 仅相机轨迹；与 Move 的 MirrorByTeam 语义一致。
    /// </summary>
    public bool MirrorByTeam;
}
