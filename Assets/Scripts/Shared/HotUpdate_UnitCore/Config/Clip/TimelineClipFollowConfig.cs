using System;

[Serializable]
public class TimelineClipFollowConfig : TimelineClipConfigBase
{
    public TimelineMoveEntityType MoveEntityType;
    public string EffectPath;

    /// <summary>跟随目标节点（主角身上）；默认骨骼根节点。</summary>
    public UnitNodeType FollowNodeType = UnitNodeType.Root;

    /// <summary>
    /// 跟随滞后时间（秒）。0 = 瞬间跟上；越大越拖尾。
    /// 内部用 <see cref="UnityEngine.Vector3.SmoothDamp"/>。
    /// </summary>
    public float FollowLagTime;

    /// <summary>
    /// 旧字段：跟随速率（1/秒）。仅用于读档迁移到 <see cref="FollowLagTime"/>，存盘不再写出。
    /// </summary>
    public float LerpValueAfterMoveTo;

    /// <summary>Newtonsoft：不再序列化旧速率字段。</summary>
    public bool ShouldSerializeLerpValueAfterMoveTo()
    {
        return false;
    }

    /// <summary>把旧 LerpValueAfterMoveTo（速率）迁成 FollowLagTime（秒）。</summary>
    public void MigrateFollowLagFromLegacy()
    {
        if (FollowLagTime > 0f)
        {
            LerpValueAfterMoveTo = 0f;
            return;
        }

        if (LerpValueAfterMoveTo > 0f)
        {
            FollowLagTime = 1f / LerpValueAfterMoveTo;
        }

        LerpValueAfterMoveTo = 0f;
    }
}
