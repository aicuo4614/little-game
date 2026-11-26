using UnityEngine;

/// <summary>单位节点绑定：Yoo 地址与相对路径查找（运行时无 Editor API）。</summary>
public static class UnitNodeDataPathUtil
{
    /// <summary>JSON 文件名后缀（含扩展名，编辑器用）。</summary>
    public const string JsonSuffix = "_Node_Data.json";

    /// <summary>Yoo 地址后缀（无扩展名）：prefab <c>Unit/Low/mt_001/mt_001</c> → <c>Unit/Low/mt_001/mt_001_Node_Data</c>。</summary>
    public const string AddressSuffix = "_Node_Data";

    /// <summary>由预制体 Yoo 地址推导 Node_Data 地址。</summary>
    public static string GetAddressFromPrefabAddress(string prefabAddress)
    {
        if (string.IsNullOrEmpty(prefabAddress))
        {
            return string.Empty;
        }

        return prefabAddress + AddressSuffix;
    }

    /// <summary>计算 <paramref name="target"/> 相对 <paramref name="root"/> 的路径；target 为 root 时返回空串。</summary>
    public static string GetRelativePath(Transform root, Transform target)
    {
        if (target == root)
        {
            return string.Empty;
        }

        var parts = new System.Collections.Generic.List<string>(8);
        var cur = target;
        while (cur != null && cur != root)
        {
            parts.Add(cur.name);
            cur = cur.parent;
        }

        if (cur != root)
        {
            return string.Empty;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }

    /// <summary>按相对路径在预制体根下查找 Transform；空路径返回 root。</summary>
    public static Transform FindByRelativePath(Transform root, string path)
    {
        if (root == null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(path))
        {
            return root;
        }

        var found = root.Find(path);
        if (found != null)
        {
            return found;
        }

        // Transform.Find 不搜 inactive；回退全量匹配完整相对路径
        var all = root.GetComponentsInChildren<Transform>(true);
        for (var i = 0; i < all.Length; i++)
        {
            var t = all[i];
            if (t == root)
            {
                continue;
            }

            if (GetRelativePath(root, t) == path)
            {
                return t;
            }
        }

        return null;
    }
}
