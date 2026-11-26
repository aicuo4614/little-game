using UnityEngine;

/// <summary>
/// 热更层全局服务入口与 PureMVC 通知入口（对齐 Demo MVC）。
/// Service 统一由本类创建并暴露（MVC.ResourceService / MVC.SOConfigService / MVC.SceneService / MVC.TimerService / MVC.LocalSaveService / MVC.DataConfigService）。
/// </summary>
public static class MVC
{
    public static MainFacade Facade { get; private set; }
    public static TimerService TimerService { get; private set; }
    public static LocalSaveService LocalSaveService { get; private set; }
    public static DataConfigService DataConfigService { get; private set; }
    public static ResourceService ResourceService { get; private set; }
    public static SOConfigService SOConfigService { get; private set; }
    public static SceneService SceneService { get; private set; }

    private static bool initialized;
    public static bool IsInitialized => initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ModuleMgr.ResetInstance();
        UiMgr.ResetInstance();
        PoolMgr.ResetInstance();
        initialized = false;
        Facade = null;
        TimerService = null;
        LocalSaveService = null;
        DataConfigService = null;
        ResourceService = null;
        SOConfigService = null;
        SceneService = null;
    }

    public static void Init()
    {
        if (initialized)
        {
            return;
        }

        PureMVC.Patterns.Facade.Facade.RemoveCore(MainFacade.CORE_NAME);
        Facade = MainFacade.GetInstance();
        TimerService = global::TimerService.CreateHost();
        LocalSaveService = new LocalSaveService();
        ResourceService = new ResourceService();
        SOConfigService = new SOConfigService();
        SceneService = new SceneService();
        DataConfigService = new DataConfigService();
        initialized = true;
    }

    public static void SendNotification(string notificationName, object body = null, string type = null)
    {
        Facade?.SendNotification(notificationName, body, type);
    }
}
