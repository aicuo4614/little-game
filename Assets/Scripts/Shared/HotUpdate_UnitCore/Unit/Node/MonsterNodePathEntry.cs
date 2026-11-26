using System;

/// <summary>单条节点绑定：业务节点类型 → 相对预制体根的 Transform 路径。</summary>
[Serializable]
public class MonsterNodePathEntry
{
    /// <summary>业务节点类型（完整 <see cref="UnitNodeType"/>）。</summary>
    public UnitNodeType NodeType;

    /// <summary>
    /// 相对预制体根的路径；空字符串表示根自身。
    /// 例：<c>active/animator/Bip001/Bip001 Head</c>。
    /// </summary>
    public string Path;
}
