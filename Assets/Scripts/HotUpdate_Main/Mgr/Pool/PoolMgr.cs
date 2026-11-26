using UnityEngine;

/// <summary>为每个房间或战斗创建独立的 GameObject 池组。</summary>
/// <remarks>来源：ClientExpress 的 HotUpdate_Main/Mgr/Pool，提交 f36279bc584f0c786a9b3909d837ddfcc982bcac。</remarks>
public sealed class PoolMgr : MgrBase<PoolMgr>
{
    /// <summary>创建随宿主场景移动的池组；宿主退出时负责 Dispose。</summary>
    public PoolGroup CreateGroup(Transform owner)
    {
        return new PoolGroup(owner);
    }
}
