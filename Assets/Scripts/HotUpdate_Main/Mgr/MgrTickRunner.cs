using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 统一驱动 <see cref="MgrBase.Tick"/> 的 DontDestroyOnLoad 宿主。
/// </summary>
public sealed class MgrTickRunner : MonoBehaviour
{
    static MgrTickRunner runnerInstance;
    static List<MgrBase> tickTargets = new List<MgrBase>();

    /// <summary>将 Mgr 加入每帧 Tick 列表（幂等）。</summary>
    public static void Register(MgrBase mgr)
    {
        EnsureRunner();
        if (!tickTargets.Contains(mgr))
        {
            tickTargets.Add(mgr);
        }
    }

    /// <summary>将 Mgr 移出每帧 Tick 列表。</summary>
    public static void Unregister(MgrBase mgr)
    {
        tickTargets.Remove(mgr);
    }

    static void EnsureRunner()
    {
        if (runnerInstance != null)
        {
            return;
        }

        var go = new GameObject("[MgrTickRunner]");
        UnityEngine.Object.DontDestroyOnLoad(go);
        runnerInstance = go.AddComponent<MgrTickRunner>();
    }

    void Update()
    {
        var deltaTime = Time.deltaTime;
        for (var i = 0; i < tickTargets.Count; i++)
        {
            tickTargets[i].Tick(deltaTime);
        }
    }
}
