using System.Collections;
using UnityEngine;

/// <summary>
/// 为静态类提供协程运行能力
/// 挂载在场景中的持久化 GameObject 上，由 Application 初始化
/// </summary>
public class CoroutineRunner : MonoBehaviour
{
    private static CoroutineRunner _instance;

    public static void Initialize()
    {
        if (_instance != null) return;
        var go = new GameObject("[CoroutineRunner]");
        Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<CoroutineRunner>();
    }

    public static Coroutine Start(IEnumerator routine)
    {
        if (_instance == null)
        {
            GameDebug.LogError("CoroutineRunner", "未初始化，请在 Application.Start 中调用 CoroutineRunner.Initialize()");
            return null;
        }
        return _instance.StartCoroutine(routine);
    }

    public static void Stop(Coroutine coroutine)
    {
        _instance?.StopCoroutine(coroutine);
    }

    /// <summary>供热更代码将 UI 挂载到持久化节点下</summary>
    public static Transform Transform => _instance?.transform;
}
