using UnityEngine;

/// <summary>
/// 运行时为战斗相机预制体挂载 <see cref="BattleCameraMono"/>，并按节点名解析引用（美术预制体不挂程序脚本）。
/// 约定层级：根 <c>BattleCamera</c> → <c>ShakeTrans</c> → <c>ContentTrans</c>（含 Camera）。
/// </summary>
public static class BattleCameraMonoBinder
{
    /// <summary>
    /// 确保实例上已绑定 BattleCameraMono；已有组件则补齐引用。
    /// </summary>
    public static BattleCameraMono EnsureBound(GameObject instance)
    {
        var mono = instance.GetComponent<BattleCameraMono>();
        if (mono == null)
        {
            mono = instance.AddComponent<BattleCameraMono>();
        }

        if (mono.RootTrans == null)
        {
            mono.RootTrans = instance.transform;
        }

        if (mono.ShakeTrans == null)
        {
            mono.ShakeTrans = FindChild(instance.transform, "ShakeTrans");
        }

        if (mono.ContentTrans == null)
        {
            mono.ContentTrans = FindChild(instance.transform, "ContentTrans");
        }

        if (mono.MyCamera == null)
        {
            if (mono.ContentTrans != null)
            {
                mono.MyCamera = mono.ContentTrans.GetComponent<Camera>();
            }

            if (mono.MyCamera == null)
            {
                mono.MyCamera = instance.GetComponentInChildren<Camera>(true);
            }
        }

        return mono;
    }

    static Transform FindChild(Transform root, string name)
    {
        if (root.name == name)
        {
            return root;
        }

        for (var i = 0; i < root.childCount; i++)
        {
            var found = FindChild(root.GetChild(i), name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
