using System.Threading.Tasks;

/// <summary>
/// 统一控制器基类（提炼自 Demo 的 BattleControlBase / BattleUnitControlBase / UnitControlBase）：
/// 由宿主注入 Owner，并按统一生命周期 Init → PrepareAsync → Start → Tick → End → ReleaseResources(Async) 驱动。
/// </summary>
public abstract class ControlBase<TOwner>
{
    /// <summary>所属宿主对象。</summary>
    public TOwner Owner { get; private set; }

    /// <summary>按注册顺序注入宿主与初始化依赖。</summary>
    public virtual void Init(TOwner owner)
    {
        Owner = owner;
    }

    /// <summary>初始化后的异步资源准备；完成前不启动后续控制器。</summary>
    public virtual Task PrepareAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>全部同级控制器初始化完成后统一启动。</summary>
    public virtual void Start()
    {
    }

    /// <summary>由宿主按注册顺序逐帧驱动。</summary>
    public virtual void Tick(float deltaTime)
    {
    }

    /// <summary>逆序结束行为与事件订阅，此时资源仍有效。</summary>
    public virtual void End()
    {
    }

    /// <summary>全部行为结束后逆序释放资源。</summary>
    public virtual void ReleaseResources()
    {
    }

    /// <summary>异步释放资源（如场景卸载）；默认复用同步释放。</summary>
    public virtual Task ReleaseResourcesAsync()
    {
        ReleaseResources();
        return Task.CompletedTask;
    }
}
