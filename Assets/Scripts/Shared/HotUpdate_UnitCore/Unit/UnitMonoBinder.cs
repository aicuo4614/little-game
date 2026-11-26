using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 为模型预制体挂载 <see cref="UnitMono"/> 并按骨骼命名解析节点（不依赖战斗逻辑 / 动画事件）。
/// 运行时动画事件由 <c>UnitMonoRuntime</c>（HotUpdate_Main）补齐。
/// </summary>
public static class UnitMonoBinder
{
    /// <summary>确保实例上已绑定 UnitMono 与节点；已有组件则补齐引用。</summary>
    public static UnitMono EnsureBound(GameObject instance)
    {
        var unitMono = instance.GetComponent<UnitMono>();
        if (unitMono == null)
        {
            unitMono = instance.AddComponent<UnitMono>();
        }

        if (unitMono.UnitNodeDatas == null)
        {
            unitMono.UnitNodeDatas = new List<UnitNodeData>();
        }

        var animator = instance.GetComponent<Animator>();
        if (animator == null)
        {
            animator = instance.GetComponentInChildren<Animator>(true);
        }

        unitMono.Animator = animator;

        if (unitMono.ActiveRoot == null)
        {
            unitMono.ActiveRoot = ResolveActiveRoot(instance, animator);
        }

        BindNodes(unitMono, instance);

        return unitMono;
    }

    /// <summary>
    /// 按与 <see cref="EnsureBound"/> 相同的骨骼命名规则，在 root 子树中解析指定节点。
    /// 供怪物编辑器「自动绑定」等编辑器工具复用。
    /// </summary>
    public static Transform FindNode(Transform root, UnitNodeType nodeType)
    {
        if (root == null)
        {
            throw new System.ArgumentException("root is null", nameof(root));
        }

        if (nodeType == UnitNodeType.None
            || nodeType == UnitNodeType.GroupCenter
            || nodeType == UnitNodeType.World
            || nodeType == UnitNodeType.UnitOriginPos)
        {
            // None / 虚拟节点不是可绑定骨骼
            return null;
        }

        if (nodeType == UnitNodeType.Transform)
        {
            return root;
        }

        var byNorm = BuildNormMap(root);
        if (nodeType == UnitNodeType.Root)
        {
            return ResolveRootTransform(byNorm, root);
        }

        return ResolveByCandidates(byNorm, GetCandidates(nodeType));
    }

    static Transform ResolveActiveRoot(GameObject instance, Animator animator)
    {
        var named = FindChildByNames(instance.transform, "active", "ActiveRoot", "character");
        if (named != null)
        {
            return named;
        }

        if (animator != null)
        {
            return animator.transform;
        }

        return instance.transform;
    }

    static void BindNodes(UnitMono unitMono, GameObject instance)
    {
        var byNorm = BuildNormMap(instance.transform);

        EnsureNode(unitMono, UnitNodeType.Root, ResolveRoot(unitMono, byNorm));
        EnsureNode(unitMono, UnitNodeType.LeftHand, ResolveByCandidates(byNorm, GetCandidates(UnitNodeType.LeftHand)));
        EnsureNode(unitMono, UnitNodeType.RightHand, ResolveByCandidates(byNorm, GetCandidates(UnitNodeType.RightHand)));
        EnsureNode(unitMono, UnitNodeType.Spine, ResolveByCandidates(byNorm, GetCandidates(UnitNodeType.Spine)));
        EnsureNode(unitMono, UnitNodeType.Head, ResolveByCandidates(byNorm, GetCandidates(UnitNodeType.Head)));
    }

    /// <summary>各节点类型的规范化候选名（已小写、无分隔符）。</summary>
    static string[] GetCandidates(UnitNodeType nodeType)
    {
        switch (nodeType)
        {
            case UnitNodeType.Root:
                return new[] { "root", "character", "animator", "activeroot" };
            case UnitNodeType.LeftHand:
                // Bip001-L-Hand → lhand；mixamorig:LeftHand → lefthand；Hand_L → handl
                return new[] { "lefthand", "lhand", "handl", "lefthandattach", "lhandattach" };
            case UnitNodeType.RightHand:
                return new[] { "righthand", "rhand", "handr", "righthandattach", "rhandattach" };
            case UnitNodeType.Spine:
                return new[] { "spine", "spine1", "spine01" };
            case UnitNodeType.Head:
                return new[] { "head", "headnub", "headtopend" };
            case UnitNodeType.Custom1:
                return new[] { "custom1" };
            case UnitNodeType.Custom2:
                return new[] { "custom2" };
            case UnitNodeType.Custom3:
                return new[] { "custom3" };
            default:
                return new[] { NormalizeBoneName(nodeType.ToString()) };
        }
    }

    static Dictionary<string, Transform> BuildNormMap(Transform root)
    {
        var transforms = root.GetComponentsInChildren<Transform>(true);
        var byNorm = new Dictionary<string, Transform>(transforms.Length);
        for (var i = 0; i < transforms.Length; i++)
        {
            var t = transforms[i];
            var key = NormalizeBoneName(t.name);
            if (key.Length == 0)
            {
                continue;
            }

            if (!byNorm.ContainsKey(key))
            {
                byNorm.Add(key, t);
            }
        }

        return byNorm;
    }

    static Transform ResolveRoot(UnitMono unitMono, Dictionary<string, Transform> byNorm)
    {
        var byName = ResolveByCandidates(byNorm, GetCandidates(UnitNodeType.Root));
        if (byName != null)
        {
            return byName;
        }

        return unitMono.ActiveRoot != null ? unitMono.ActiveRoot : unitMono.transform;
    }

    static Transform ResolveRootTransform(Dictionary<string, Transform> byNorm, Transform fallback)
    {
        var byName = ResolveByCandidates(byNorm, GetCandidates(UnitNodeType.Root));
        if (byName != null)
        {
            return byName;
        }

        return fallback;
    }

    static void EnsureNode(UnitMono unitMono, UnitNodeType nodeType, Transform node)
    {
        if (node == null)
        {
            return;
        }

        for (var i = 0; i < unitMono.UnitNodeDatas.Count; i++)
        {
            var data = unitMono.UnitNodeDatas[i];
            if (data.UnitNodeType != nodeType)
            {
                continue;
            }

            if (data.UnitNodeTrans == null)
            {
                data.UnitNodeTrans = node;
            }

            return;
        }

        unitMono.UnitNodeDatas.Add(new UnitNodeData
        {
            UnitNodeType = nodeType,
            UnitNodeTrans = node,
        });
    }

    static Transform ResolveByCandidates(Dictionary<string, Transform> byNorm, params string[] candidates)
    {
        if (candidates == null || candidates.Length == 0)
        {
            return null;
        }

        // 1) 精确命中规范化名：Bip001-L-Hand → lhand
        for (var i = 0; i < candidates.Length; i++)
        {
            if (byNorm.TryGetValue(candidates[i], out var t))
            {
                return t;
            }
        }

        // 2) 以前缀命中（spine → spine1）
        for (var i = 0; i < candidates.Length; i++)
        {
            var prefix = candidates[i];
            if (prefix.Length == 0)
            {
                continue;
            }

            foreach (var kv in byNorm)
            {
                if (kv.Key.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    return kv.Value;
                }
            }
        }

        // 3) 以后缀命中（weapon_lhand → lhand；xxxbip001lhand 兜底）
        for (var i = 0; i < candidates.Length; i++)
        {
            var suffix = candidates[i];
            if (suffix.Length < 4)
            {
                continue;
            }

            foreach (var kv in byNorm)
            {
                if (kv.Key.EndsWith(suffix, System.StringComparison.Ordinal))
                {
                    return kv.Value;
                }
            }
        }

        return null;
    }

    static Transform FindChildByNames(Transform root, params string[] names)
    {
        var all = root.GetComponentsInChildren<Transform>(true);
        for (var n = 0; n < names.Length; n++)
        {
            var want = names[n];
            for (var i = 0; i < all.Length; i++)
            {
                if (string.Equals(all[i].name, want, System.StringComparison.OrdinalIgnoreCase))
                {
                    return all[i];
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 去掉 mixamorig: / Bip001 / 空格下划线连字符，便于跨骨架匹配。
    /// 例：Bip001-L-Hand → lhand；mixamorig:LeftHand → lefthand。
    /// </summary>
    static string NormalizeBoneName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var n = name;
        const string mixamo = "mixamorig:";
        if (n.StartsWith(mixamo, System.StringComparison.OrdinalIgnoreCase))
        {
            n = n.Substring(mixamo.Length);
        }

        // Bip001 可能不在最前（如 Character_Bip001-L-Hand），从首次出现处截断前缀
        const string bip = "Bip001";
        var bipIdx = n.IndexOf(bip, System.StringComparison.OrdinalIgnoreCase);
        if (bipIdx >= 0)
        {
            n = n.Substring(bipIdx + bip.Length);
        }

        // 去掉空格 / 下划线 / 连字符，使 Bip001-L-Hand → lhand，能命中 lefthand/lhand 候选
        n = n.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
        return n.ToLowerInvariant();
    }
}
