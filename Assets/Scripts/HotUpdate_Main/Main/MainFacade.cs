using PureMVC.Patterns.Facade;

/// <summary>
/// PureMVC Facade：注册常驻 Proxy / Mediator。
/// Service 由 <see cref="MVC.Init"/> 创建并对外暴露，不经本类访问。
/// </summary>
public class MainFacade : Facade
{
    public const string CORE_NAME = "MainCore";

    private MainFacade() : base(CORE_NAME)
    {
    }

    /// <summary>获取（或创建）主 Facade 单例。</summary>
    public static MainFacade GetInstance()
    {
        return (MainFacade)Facade.GetInstance(CORE_NAME, k => new MainFacade());
    }

}
