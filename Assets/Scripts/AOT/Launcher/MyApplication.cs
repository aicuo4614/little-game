using UnityEngine;

/// <summary>
/// AOT 启动总入口（对齐 Demo MyApplication）：
/// 1. 初始化 CoroutineRunner、LaunchSOConfig、AOTUiRoot、ResourceMgr；
/// 2. 执行 CodePackageHotUpdateFlow → 反射调用 MainHotUpdate.Launch()。
/// </summary>
public class MyApplication : MonoBehaviour
{
    private static LaunchSOConfig launchSOConfig;
    private static MyApplication instance;

    public static MyApplication Instance => instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        launchSOConfig = null;
        instance = null;
    }

    public static LaunchSOConfig GetLaunchSOConfig()
    {
        if (launchSOConfig == null)
        {
            launchSOConfig = Resources.Load<LaunchSOConfig>(LaunchSOConfig.ResourcesPath);
        }

        return launchSOConfig;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootCombat()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Combat") EnsureStarted();
    }

    public static void EnsureStarted()
    {
        if (instance != null)
        {
            return;
        }

        var go = new GameObject("[MyApplication]");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<MyApplication>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        CoroutineRunner.Initialize();
        AOTUiRoot.EnsureCreated();
        ApplyLaunchConfig();

        ResourceMgr.Instance.Init(success =>
        {
            if (!success)
            {
                GameDebug.LogError("Launcher", "LaunchPackage 初始化失败，停止启动；请检查资源收集与构建输出。");
                return;
            }
            GameDebug.Log("Launcher", $"AOT 启动就绪 (LaunchPackage={success}, CdnBase={ResourceMgr.Instance.CdnBase})");
            var flow = new CodePackageHotUpdateFlow();
            flow.Start();
        });
    }

    private void ApplyLaunchConfig()
    {
        launchSOConfig = Resources.Load<LaunchSOConfig>(LaunchSOConfig.ResourcesPath);
        if (launchSOConfig != null)
        {
            GameDebug.Enabled = launchSOConfig.LogEnabled;
        }
        else
        {
            GameDebug.Enabled = true;
        }

        ResourceMgr.Instance.ApplyLaunchCdn();
    }
}
