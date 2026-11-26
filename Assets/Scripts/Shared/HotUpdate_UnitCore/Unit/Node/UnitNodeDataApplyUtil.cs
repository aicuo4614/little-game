using UnityEngine;

/// <summary>将 <see cref="MonsterNodeDataConfig"/> 应用到 <see cref="UnitMono.UnitNodeDatas"/>（覆盖同类型）。</summary>
public static class UnitNodeDataApplyUtil
{
    /// <summary>
    /// 按 JSON 路径绑定节点；已存在同 <see cref="UnitNodeType"/> 则覆盖 Transform。
    /// 路径找不到时跳过该条并打日志。
    /// </summary>
    public static void Apply(UnitMono unitMono, Transform prefabRoot, MonsterNodeDataConfig config)
    {
        if (unitMono == null || prefabRoot == null || config == null || config.Nodes == null)
        {
            return;
        }

        if (unitMono.UnitNodeDatas == null)
        {
            unitMono.UnitNodeDatas = new System.Collections.Generic.List<UnitNodeData>();
        }

        for (var i = 0; i < config.Nodes.Count; i++)
        {
            var entry = config.Nodes[i];
            if (entry.NodeType == UnitNodeType.None)
            {
                continue;
            }

            var node = UnitNodeDataPathUtil.FindByRelativePath(prefabRoot, entry.Path);
            if (node == null)
            {
                Debug.LogWarning(
                    $"[UnitNodeData] 路径未找到 NodeType={entry.NodeType} Path={entry.Path} root={prefabRoot.name}");
                continue;
            }

            SetOrAdd(unitMono, entry.NodeType, node);
        }

        unitMono.ClearNodeCache();
    }

    static void SetOrAdd(UnitMono unitMono, UnitNodeType nodeType, Transform node)
    {
        for (var i = 0; i < unitMono.UnitNodeDatas.Count; i++)
        {
            var data = unitMono.UnitNodeDatas[i];
            if (data.UnitNodeType != nodeType)
            {
                continue;
            }

            data.UnitNodeTrans = node;
            return;
        }

        unitMono.UnitNodeDatas.Add(new UnitNodeData
        {
            UnitNodeType = nodeType,
            UnitNodeTrans = node,
        });
    }
}
