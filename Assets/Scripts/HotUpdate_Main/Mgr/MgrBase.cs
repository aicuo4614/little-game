using System.Threading.Tasks;

/// <summary>
/// 业务 Mgr 基类（模块等对象中心）。与 <see cref="IService"/> 并列：Service 进 Facade，Mgr 用 <c>Instance</c>。
/// </summary>
public abstract class MgrBase
{
    /// <summary>为 true 时首次 <see cref="MgrBase{TSelf}.Instance"/> 后自动注册 <see cref="Tick"/>。</summary>
    protected virtual bool EnableTick => false;

    /// <summary>异步初始化；启动流程显式 await。幂等。</summary>
    public virtual Task InitAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>释放本 Mgr 持有的资源（缓存、句柄等）。</summary>
    public virtual void Release()
    {
    }

    /// <summary>登出 / 切号 / 回登录时清会话状态；默认调用 <see cref="Release"/>。</summary>
    public virtual void Clear()
    {
        Release();
    }

    /// <summary>每帧轮询；由 <see cref="MgrTickRunner"/> 在注册后调用。</summary>
    /// <param name="deltaTime">距上一帧秒数。</param>
    public virtual void Tick(float deltaTime)
    {
    }

    /// <summary>向 <see cref="MgrTickRunner"/> 注册；子类需动态启停 Tick 时调用。</summary>
    protected void RegisterTick()
    {
        MgrTickRunner.Register(this);
    }

    /// <summary>从 <see cref="MgrTickRunner"/> 注销。</summary>
    protected void UnregisterTick()
    {
        MgrTickRunner.Unregister(this);
    }
}

/// <summary>
/// 带懒加载单例的 Mgr 基类。
/// </summary>
/// <typeparam name="TSelf">具体 Mgr 类型。</typeparam>
public abstract class MgrBase<TSelf> : MgrBase
    where TSelf : MgrBase<TSelf>, new()
{
    static TSelf instance;

    /// <summary>单例；首次访问时创建并调用 <see cref="OnCreated"/>。</summary>
    public static TSelf Instance
    {
        get
        {
            if (instance == null)
            {
                instance = new TSelf();
                instance.OnCreated();
                if (instance.EnableTick)
                {
                    MgrTickRunner.Register(instance);
                }
            }

            return instance;
        }
    }

    public static void ResetInstance()
    {
        instance?.Clear();
        instance = null;
    }

    /// <summary>实例创建后、首次 <see cref="Instance"/> 时调用；仅做字段默认值，不碰外部依赖。</summary>
    protected virtual void OnCreated()
    {
    }

    /// <summary>释放资源并注销 Tick。</summary>
    public override void Release()
    {
        UnregisterTick();
    }
}
